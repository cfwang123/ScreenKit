# -*- coding: utf-8 -*-
"""给 CHANGELOG.md 追加一条中英文条目，并防止并发写坏（竞态）。

命令：
  changelog.cmd added "English text" "中文文本"
  changelog.cmd --kind fixed --en "English text" --zh "中文文本"
  changelog.cmd changed "…" "…" --dry-run

防竞态做法（写 CHANGELOG 时另一进程/编辑器常常也在改）：
  1. 先抢一个 `<文件>.lock` 独占锁，串行化本脚本的多个实例；
  2. 读入 → 在内存里改 → **写前再读一次比对**，内容变了就整段重来（compare-and-swap）;
  3. 重试若干次仍被插队才报错，绝不盲写。
同一条条目已经存在时直接跳过，可安全重跑。

只为「## unreleased」章节追加；章节或语言段缺失时直接报错，不动已发布的历史版本。
"""
from __future__ import annotations

import argparse
import os
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_FILE = ROOT / "CHANGELOG.md"

# 条目类别：英文标题、中文标题，顺序即「缺标题时新建」的先后
KINDS = [
    ("added", "Added", "新增"),
    ("changed", "Changed", "变更"),
    ("fixed", "Fixed", "修复"),
    ("removed", "Removed", "移除"),
    ("docs", "Documentation", "文档"),
    ("security", "Security / privacy notes", "安全与隐私"),
]
ORDER = {key: i for i, (key, _, _) in enumerate(KINDS)}
EN_TITLE = {key: en for key, en, _ in KINDS}
ZH_TITLE = {key: zh for key, _, zh in KINDS}
KIND_BY_NAME = {}
for _key, _en, _zh in KINDS:
    KIND_BY_NAME[_key] = _key
    KIND_BY_NAME[_en.lower()] = _key
    KIND_BY_NAME[_zh] = _key

UNRELEASED = "unreleased"
EN_SECTION = "english"
ZH_SECTION = "中文"
LOCK_TIMEOUT = 15.0
LOCK_STALE = 120.0
CAS_TRIES = 5


def norm_kind(raw: str) -> str:
    k = (raw or "").strip()
    if k.lower() in KIND_BY_NAME:
        return KIND_BY_NAME[k.lower()]
    if k in KIND_BY_NAME:
        return KIND_BY_NAME[k]
    raise SystemExit(f"未知的类别: {raw}（可用: {', '.join(en for _, en, _ in KINDS)}）")


def heading(line: str) -> tuple[int, str] | None:
    """返回 (级别, 标题)，不是标题返回 None。"""
    s = line.strip()
    if not s.startswith("#"):
        return None
    level = len(s) - len(s.lstrip("#"))
    if len(s) > level and s[level] == " ":
        return level, s[level + 1:].strip()
    return None


def find_unreleased(lines: list[str]) -> tuple[int, int]:
    """返回 ## unreleased 章节正文的首行、末行（不含标题行）。"""
    start = None
    for i, line in enumerate(lines):
        h = heading(line)
        if not h:
            continue
        level, title = h
        if level == 2:
            if start is not None:
                return start, i
            if title.strip().lower().startswith(UNRELEASED):
                start = i + 1
    if start is None:
        raise SystemExit("CHANGELOG 里没有找到「## unreleased」章节，先手动建好再跑")
    return start, len(lines)


def find_heading(lines: list[str], level: int, title: str, start: int, end: int) -> int | None:
    for i in range(start, end):
        h = heading(lines[i])
        if h and h[0] == level and h[1].lower() == title.lower():
            return i
    return None


def block_end(lines: list[str], start: int, end: int, level: int) -> int:
    """从 start 之后找第一个级别 <= level 的标题，作为本段末尾。"""
    for i in range(start + 1, end):
        h = heading(lines[i])
        if h and h[0] <= level:
            return i
    return end


def last_content(lines: list[str], start: int, end: int) -> int:
    """段内最后一个非空行的索引；段是空的就返回 start。"""
    for i in range(end - 1, start, -1):
        if lines[i].strip():
            return i
    return start


def ensure_kind_heading(lines: list[str], block: tuple[int, int], kind: str, title: str) -> int:
    """确保语言段里有 #### <类别> 标题，返回标题行索引；没有就按固定顺序新建。"""
    bi, be = block
    found = find_heading(lines, 4, title, bi, be)
    if found is not None:
        return found
    rank = ORDER[kind]
    at = be
    for i in range(bi, be):
        h = heading(lines[i])
        if not h or h[0] != 4:
            continue
        other = KIND_BY_NAME.get(h[1].lower())
        if other is not None and ORDER[other] > rank:
            at = i
            break
    lines[at:at] = [f"#### {title}", ""]
    return at


def insert_bullet(lines: list[str], section: str, title: str, kind: str, text: str) -> str:
    """往指定语言段的类别里插一条。返回 added / exists。"""
    start, end = find_unreleased(lines)
    si = find_heading(lines, 3, section, start, end)
    if si is None:
        raise SystemExit(f"## unreleased 里没有「### {section}」段落")
    block = (si + 1, block_end(lines, si, end, 3))
    ci = ensure_kind_heading(lines, block, kind, title)
    section_end = block_end(lines, ci, block[1], 4)
    bullet = f"- {text.strip()}"
    for i in range(ci + 1, section_end):
        if lines[i].strip() == bullet:
            return "exists"
    at = last_content(lines, ci, section_end)
    if at == ci:            # 段里没有内容：标题后补一个空行再写
        lines[at + 1:at + 1] = ["", bullet]
    else:
        lines[at + 1:at + 1] = [bullet]
    return "added"


class lockfile:
    """`<文件>.lock` 独占锁：串行化本脚本的多个实例，超时或残留过期锁会自愈。"""

    def __init__(self, target: Path, timeout: float = LOCK_TIMEOUT):
        self.path = Path(str(target) + ".lock")
        self.timeout = timeout
        self.fd = None

    def __enter__(self):
        deadline = time.monotonic() + self.timeout
        while True:
            try:
                self.fd = os.open(self.path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
                os.write(self.fd, str(os.getpid()).encode())
                return self
            except FileExistsError:
                try:
                    if time.time() - self.path.stat().st_mtime > LOCK_STALE:
                        self.path.unlink()
                        continue
                except OSError:
                    pass
                if time.monotonic() > deadline:
                    raise SystemExit(f"等锁超时：{self.path} 一直被占用，稍后再试")
                time.sleep(0.15)

    def __exit__(self, *exc):
        if self.fd is not None:
            os.close(self.fd)
            self.fd = None
        try:
            self.path.unlink()
        except OSError:
            pass
        return False


def add_entry(path: Path, kind: str, en: str, zh: str, dry: bool) -> str:
    """一次读改写。返回 added / exists。写前比对，被插队就整段重来。"""
    for _ in range(CAS_TRIES):
        with lockfile(path):
            before = path.read_bytes()
            nl = "\r\n" if b"\r\n" in before else "\n"
            lines = before.decode("utf-8").replace("\r\n", "\n").split("\n")
            act_en = insert_bullet(lines, EN_SECTION, EN_TITLE[kind], kind, en)
            act_zh = insert_bullet(lines, ZH_SECTION, ZH_TITLE[kind], kind, zh)
            if act_en == "exists" and act_zh == "exists":
                return "exists"
            if path.read_bytes() != before:
                continue        # 有人刚写过，整段重来
            if not dry:
                path.write_bytes(nl.join(lines).encode("utf-8"))
            return "added"
    raise SystemExit("连续几次都被别的进程插队改写，没写入，请稍后再试")


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        prog="changelog.py",
        description="往 CHANGELOG.md 的 ## unreleased 追加一条中英文条目（防并发覆盖）。",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""示例:
  changelog.cmd added "Tools page adds a color picker." "工具页增加取色器。"
  changelog.cmd fixed --en "Hotkey 12 was ignored." --zh "热键 12 被忽略。"
  changelog.cmd --kind 修复 "…" "…" --dry-run
""",
    )
    p.add_argument("rest", nargs="*", help="[类别] 英文条目 中文条目（类别也可用 --kind 给）")
    p.add_argument("--kind", default=None, help="类别：added / changed / fixed / removed / docs / security（也可写 新增/变更/修复…）")
    p.add_argument("--en", default=None, help="英文条目")
    p.add_argument("--zh", default=None, help="中文条目")
    p.add_argument("--file", default=str(DEFAULT_FILE), help="CHANGELOG 路径，默认仓库根目录的 CHANGELOG.md")
    p.add_argument("--dry-run", action="store_true", help="只预览，不写文件")
    return p


def main(argv: list[str]) -> int:
    args = build_parser().parse_args(argv[1:])
    rest = list(args.rest)
    kind_raw = args.kind
    # 第一个位置参数若正好是类别名，就当类别用；--kind 已给时不再从位置参数里认类别
    if kind_raw is None and rest and (rest[0] in KIND_BY_NAME or rest[0].strip().lower() in KIND_BY_NAME):
        kind_raw = rest.pop(0)
    kind = norm_kind(kind_raw or "added")
    if len(rest) > 2:
        raise SystemExit(f"多余的参数: {' '.join(rest[2:])}（只要英文条目和中文条目）")
    en = args.en if args.en is not None else (rest[0] if len(rest) > 0 else None)
    zh = args.zh if args.zh is not None else (rest[1] if len(rest) > 1 else None)
    if not en or not zh:
        raise SystemExit("英文和中文条目都要给：changelog.cmd <类别> \"English\" \"中文\"")
    path = Path(args.file)
    if not path.is_file():
        raise SystemExit(f"没有这个文件：{path}")
    act = add_entry(path, kind, en, zh, args.dry_run)
    tag = "（--dry-run：未写入）" if args.dry_run and act == "added" else ""
    if act == "exists":
        print("这条条目已经在 CHANGELOG 里了，跳过")
        return 0
    print(f"[{EN_TITLE[kind]} / {ZH_TITLE[kind]}] 已写入 {path}{tag}")
    print(f"  - {en.strip()}")
    print(f"  - {zh.strip()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
