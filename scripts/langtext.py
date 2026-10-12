# -*- coding: utf-8 -*-
"""ScreenKit 界面文案（ScreenKit/lang/*.toml）小工具：一条命令新增或修改一条文案。

命令：
  set <key> [text]     新增或修改一条文案（默认中文 zh）
  get <key>            查看某个键在各语言下的值
  missing              列出各语言相对基础语言缺失或为空的键
  fill --from zh       用某个语言的文案补齐其它语言（批量新增，默认不覆盖）
  check                检查各语言键集合的差异
  langs                列出语言文件

要点：
  * 只动 [text] 段里的键，`name`（语言显示名）不碰。
  * 保留每个文件原有的换行风格（CRLF / LF）、无 BOM、末尾换行。
  * 新增默认追加到 [text] 段末尾，可用 --after <key> 插到某个键后面。
  * 文本里的换行写 \\n、制表 \\t、引号 \\"，与 Loc.cs 的解析一致。

示例（cmd，Windows）：
  scripts\\langtext.cmd set app.demo.title "演示标题" -l zh
  scripts\\langtext.cmd set app.demo.title "Demo title" -l all
  scripts\\langtext.cmd set app.demo.title -l all --each zh=演示标题 --each en=Demo title
  scripts\\langtext.cmd get app.demo.title -l zh,en
  scripts\\langtext.cmd missing --base zh
  scripts\\langtext.cmd fill --from zh -l all
"""
from __future__ import annotations

import argparse
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
LANGDIR = ROOT / "ScreenKit" / "lang"
BOM = b"\xef\xbb\xbf"

# 语言菜单顺序，与 Loc.cs 的 rank() 一致：其余按代码排序。
ORDER = ["zh", "en", "ja", "ko", "es", "fr", "pt", "ru", "de"]
# 允许的语言别名，与 Loc.cs 的 norm() 一致。
ALIAS = {
    "english": "en", "chinese": "zh", "zh-cn": "zh", "zh-hans": "zh", "zh-sg": "zh",
    "japanese": "ja", "jp": "ja", "korean": "ko", "kr": "ko",
    "spanish": "es", "espanol": "es", "español": "es",
    "french": "fr", "francais": "fr", "français": "fr",
    "portuguese": "pt", "portugues": "pt", "português": "pt",
    "russian": "ru", "german": "de", "deutsch": "de",
}
# 语言显示名不算文案，禁止用本工具改。
META_KEYS = ("name", "code")


def norm_code(raw: str) -> str:
    c = (raw or "").strip().lower().replace("_", "-")
    return ALIAS.get(c, c)


def unquote(val: str) -> str:
    """与 Loc.cs unquote() 一致：只认双引号，处理 \\n \\r \\t 和转义符。"""
    if len(val) < 2 or val[0] != '"' or val[-1] != '"':
        return val
    inner = val[1:-1]
    out: list[str] = []
    i = 0
    while i < len(inner):
        c = inner[i]
        if c != "\\" or i + 1 >= len(inner):
            out.append(c)
            i += 1
            continue
        n = inner[i + 1]
        i += 2
        if n == "n":
            out.append("\n")
        elif n == "r":
            out.append("\r")
        elif n == "t":
            out.append("\t")
        else:
            out.append(n)
    return "".join(out)


def escape(val: str) -> str:
    out: list[str] = []
    for c in val:
        if c == "\\":
            out.append("\\\\")
        elif c == '"':
            out.append('\\"')
        elif c == "\n":
            out.append("\\n")
        elif c == "\r":
            out.append("\\r")
        elif c == "\t":
            out.append("\\t")
        else:
            out.append(c)
    return "".join(out)


def split_line(line: str) -> tuple[str, str] | tuple[None, None]:
    """取一行里的 key / 值，返回 (key, 值)；不是键值行则返回 (None, None)。"""
    s = line.strip()
    if not s or s[0] == "#" or s[0] == "[":
        return None, None
    eq = s.find("=")
    if eq <= 0:
        return None, None
    key = s[:eq].strip()
    if not key:
        return None, None
    return key, unquote(s[eq + 1:].strip())


class Lang:
    """一个 lang/*.toml 文件。按行处理，保留原有格式。"""

    def __init__(self, path: Path):
        self.path = path
        self.code = path.stem.lower()
        raw = path.read_bytes()
        if raw[:3] == BOM:
            raw = raw[3:]
        text = raw.decode("utf-8")
        self.newline = "\r\n" if "\r\n" in text else "\n"
        lines = text.replace("\r\n", "\n").split("\n")
        self.trailing_nl = False
        if lines and lines[-1] == "":
            lines.pop()
            self.trailing_nl = True
        self.lines = lines
        self.orig = list(lines)
        self.name = ""

    # ---------- 读取 ----------

    @property
    def changed(self) -> bool:
        return self.lines != self.orig

    def section(self, name: str = "text") -> tuple[int | None, int | None]:
        """返回 [text] 段内容的首行、末行（不含标题行）；没有该段返回 (None, None)。"""
        start = None
        for i, line in enumerate(self.lines):
            s = line.strip()
            if not s.startswith("["):
                continue
            end = s.find("]")
            cur = s[1:end].strip().lower() if end > 1 else ""
            if cur == name:
                start = i + 1
            elif start is not None:
                return start, i
        if start is None:
            return None, None
        return start, len(self.lines)

    def entries(self) -> list[tuple[int, str, str]]:
        """返回 [text] 段里的 (行索引, key, 值)，按文件顺序。"""
        start, end = self.section("text")
        out: list[tuple[int, str, str]] = []
        if start is None or end is None:
            return out
        for i in range(start, end):
            key, val = split_line(self.lines[i])
            if key:
                out.append((i, key, val))
        return out

    def keys(self) -> list[str]:
        return [k for _, k, _ in self.entries()]

    def index_of(self, key: str) -> int | None:
        """键不区分大小写，与其他语言文件里实际的拼写无关。"""
        want = key.strip().lower()
        for i, k, _ in self.entries():
            if k.lower() == want:
                return i
        return None

    def get(self, key: str) -> str | None:
        i = self.index_of(key)
        if i is None:
            return None
        return split_line(self.lines[i])[1]

    # ---------- 写入 ----------

    def put(self, key: str, value: str, after: str | None = None,
            mode: str = "any") -> tuple[str, str | None]:
        """新增或修改一条文案。mode：any 覆盖 / missing 只补空 / add 只新增。

        返回 (动作, 旧值)：added / updated / unchanged / skip。
        """
        i = self.index_of(key)
        if i is not None:
            old = split_line(self.lines[i])[1]
            if mode == "add":
                return "skip", old
            if mode == "missing" and old:
                return "skip", old
            if old == value:
                return "unchanged", old
            line = self.lines[i]
            lead = line[:len(line) - len(line.lstrip())]
            spell = split_line(line)[0] or key
            self.lines[i] = f'{lead}{spell} = "{escape(value)}"'
            return "updated", old
        self.insert(key, value, after)
        return "added", None

    def insert(self, key: str, value: str, after: str | None = None) -> None:
        """新增一行。after 命中则插在其后，否则追加到 [text] 段末尾。"""
        line = f'{key} = "{escape(value)}"'
        if after:
            j = self.index_of(after)
            if j is not None:
                self.lines.insert(j + 1, line)
                return
        start, end = self.section("text")
        if start is not None and end is not None:
            self.lines.insert(end, line)
            return
        if self.lines and self.lines[-1].strip():
            self.lines.append("")
        self.lines.append("[text]")
        self.lines.append(line)

    def save(self, dry: bool = False) -> bool:
        if not self.changed:
            return False
        if dry:
            return True
        text = self.newline.join(self.lines)
        if self.trailing_nl:
            text += self.newline
        self.path.write_bytes(text.encode("utf-8"))
        self.orig = list(self.lines)
        return True


# ---------- 语言文件集合 ----------


def lang_files(root: Path) -> dict[str, Path]:
    out: dict[str, Path] = {}
    if not root.is_dir():
        return out
    for f in sorted(root.glob("*.toml")):
        code = f.stem.strip().lower()
        if code and code.replace("-", "").replace("_", "").isalnum():
            out[code] = f
    return out


def sort_codes(codes) -> list[str]:
    def rank(code: str) -> tuple[int, str]:
        return (ORDER.index(code) if code in ORDER else len(ORDER), code)
    return sorted(set(codes), key=rank)


def resolve_codes(specs, files: dict[str, Path], default: list[str]) -> list[str]:
    codes: list[str] = []
    for raw in specs or []:
        for part in str(raw).split(","):
            c = norm_code(part)
            if not c:
                continue
            if c in ("all", "*"):
                codes.extend(sort_codes(files))
            elif c in files:
                codes.append(c)
            else:
                raise SystemExit(f"没有语言文件: {c}（{LANGDIR} 下只有 {', '.join(sort_codes(files))}）")
    if not codes:
        codes = list(default)
    codes = [c for c in codes if c in files] or list(default)
    return sort_codes(codes)


def sync_copies(code: str, src: Path) -> list[str]:
    """把改好的文件覆盖到 ScreenKit/bin 下已有的同名 lang 副本。"""
    base = ROOT / "ScreenKit" / "bin"
    done: list[str] = []
    if not base.is_dir():
        return done
    for d in sorted(base.rglob("lang")):
        if not d.is_dir() or " - 副本" in str(d):
            continue
        dest = d / f"{code}.toml"
        if dest.is_file():
            shutil.copy2(src, dest)
            done.append(str(dest))
    return done


def parse_each(pairs) -> dict[str, str]:
    out: dict[str, str] = {}
    for raw in pairs or []:
        if "=" not in raw:
            raise SystemExit(f"--each 要写成 语言=文本，例如 --each en=English，收到: {raw}")
        code, text = raw.split("=", 1)
        c = norm_code(code)
        if not c:
            raise SystemExit(f"--each 缺少语言代码: {raw}")
        out[c] = text
    return out


# ---------- 各命令 ----------


def cmd_set(args, files: dict[str, Path]) -> int:
    if args.key.strip().lower() in META_KEYS:
        raise SystemExit(f"{args.key} 是语言显示名，不是文案键；改它请直接编辑 lang/*.toml")
    each = parse_each(args.each)
    if not args.lang and each:
        codes = resolve_codes(list(each.keys()), files, ["zh"])
        extra = [c for c in each if c not in codes]
        if extra:
            print(f"提示：--each 里的 {', '.join(extra)} 没有对应语言文件，已忽略")
    else:
        codes = resolve_codes(args.lang, files, ["zh"])
        extra = [c for c in each if c not in codes]
        if extra:
            print(f"提示：--each 里的 {', '.join(extra)} 不在 -l 目标内，已忽略")
    if args.add_only and args.if_missing:
        raise SystemExit("--add-only 与 --if-missing 只能用一个")
    mode = "add" if args.add_only else "missing" if args.if_missing else "any"
    touched: list[tuple[str, Path]] = []
    for code in codes:
        text = each.get(code, args.text)
        if text is None:
            print(f"[跳过] {code}.toml 没有给文本（用位置参数或 --each {code}=...）")
            continue
        lang = Lang(files[code])
        act, old = lang.put(args.key, text, after=args.after, mode=mode)
        show = text.replace("\n", "\\n")
        if act in ("added", "updated", "unchanged"):
            touched.append((code, lang.path))
        if act == "added":
            print(f"[新增] {code}.toml  {args.key} = \"{show}\"")
        elif act == "updated":
            print(f"[修改] {code}.toml  {args.key} = \"{show}\"   （原: \"{old}\"）")
        elif act == "unchanged":
            print(f"[未变] {code}.toml  {args.key} 已经是这个值")
        else:
            print(f"[跳过] {code}.toml  {args.key} 已存在（--if-missing / --add-only）")
        if text == "":
            print(f"       注意：{code} 的空值会被当成缺失，运行时回退中文")
        if lang.save(dry=args.dry_run) and args.sync:
            for p in sync_copies(code, lang.path):
                print(f"[同步] {p}")
    if args.dry_run:
        print(f"（--dry-run：预览了 {len(touched)} 个文件，未写入）")
    elif touched:
        print(f"共处理 {len(touched)} 个文件")
    return 0


def cmd_get(args, files: dict[str, Path]) -> int:
    codes = resolve_codes(args.lang, files, sort_codes(files))
    for code in codes:
        lang = Lang(files[code])
        val = lang.get(args.key)
        if val is None:
            print(f"{code}.toml  (缺失)")
        else:
            print(f"{code}.toml  \"{val.replace(chr(10), chr(92) + 'n')}\"")
    return 0


def cmd_missing(args, files: dict[str, Path]) -> int:
    base = norm_code(args.base)
    if base not in files:
        raise SystemExit(f"没有基础语言文件: {base}")
    codes = resolve_codes(args.lang, files, sort_codes(files))
    bl = Lang(files[base])
    bkeys = bl.keys()
    for code in codes:
        if code == base:
            continue
        lang = Lang(files[code])
        lacks = [k for k in bkeys if not lang.get(k)]
        extra = [k for k in lang.keys() if k not in bkeys]
        head = f"{code}.toml  缺 {len(lacks)} / 共 {len(bkeys)}"
        if extra:
            head += f"  多 {len(extra)}"
        print(head)
        for k in lacks[:max(0, args.limit)]:
            print(f"    - {k}")
        if args.limit and len(lacks) > args.limit:
            print(f"    … 还有 {len(lacks) - args.limit} 条")
        if extra:
            for k in extra[:max(0, args.limit)]:
                print(f"    + {k}")
    return 0


def cmd_fill(args, files: dict[str, Path]) -> int:
    src = norm_code(args.src)
    if src not in files:
        raise SystemExit(f"没有源语言文件: {src}")
    codes = resolve_codes(args.lang, files, [c for c in sort_codes(files) if c != src])
    base = Lang(files[src])
    pairs = [(k, v) for _, k, v in base.entries()]
    total_add = total_upd = 0
    for code in codes:
        if code == src:
            continue
        lang = Lang(files[code])
        add = upd = 0
        prev: str | None = None
        for key, val in pairs:
            if lang.index_of(key) is None:
                lang.insert(key, val, after=prev)
                add += 1
            elif args.overwrite:
                act, _ = lang.put(key, val)
                if act == "updated":
                    upd += 1
            prev = key
        total_add += add
        total_upd += upd
        if add or upd:
            print(f"[补齐] {code}.toml  新增 {add} 条" + (f"，覆盖 {upd} 条" if upd else ""))
            if lang.save(dry=args.dry_run) and args.sync:
                for p in sync_copies(code, lang.path):
                    print(f"[同步] {p}")
        else:
            print(f"[已全] {code}.toml  没有缺的键")
    if args.dry_run:
        print(f"（--dry-run：将新增 {total_add} 条、覆盖 {total_upd} 条，未写入）")
    elif total_add or total_upd:
        print(f"共新增 {total_add} 条，覆盖 {total_upd} 条（源语言 {src}）")
    return 0


def cmd_check(args, files: dict[str, Path]) -> int:
    codes = sort_codes(files)
    allkeys: set[str] = set()
    maps = {}
    for code in codes:
        lang = Lang(files[code])
        maps[code] = set(lang.keys())
        allkeys |= maps[code]
    print(f"语言 {len(codes)} 个，键并集 {len(allkeys)} 条")
    bad = 0
    for code in codes:
        lacks = sorted(allkeys - maps[code])
        if lacks:
            bad += 1
            print(f"{code}.toml  缺 {len(lacks)}: {', '.join(lacks[:8])}" + (" …" if len(lacks) > 8 else ""))
        else:
            print(f"{code}.toml  齐全（{len(maps[code])} 条）")
    if bad:
        print(f"提示：缺的键可用 fill --from zh -l {','.join(c for c in codes if allkeys - maps[c])} 批量补")
    return 0


def cmd_langs(args, files: dict[str, Path]) -> int:
    for code in sort_codes(files):
        lang = Lang(files[code])
        name = ""
        for line in lang.lines[:20]:
            k, v = split_line(line)
            if k and k.lower() == "name":
                name = v
                break
        print(f"{code:5} {name:12} {len(lang.keys()):5} 条  {files[code]}")
    return 0


EXAMPLES = """示例:
  langtext.cmd set app.demo.title "演示标题" -l zh
  langtext.cmd set app.demo.title "Demo title" -l all
  langtext.cmd set app.demo.title -l all --each zh=演示标题 --each en=Demo title
  langtext.cmd set app.demo.title "文本" -l all --each en=English   # en 用 --each，其余用"文本"
  langtext.cmd get app.demo.title -l zh,en
  langtext.cmd missing --base zh
  langtext.cmd fill --from zh -l all
"""


def common_flags() -> argparse.ArgumentParser:
    """公共选项。写在子命令前后都可以（子命令里用 SUPPRESS，避免覆盖主解析器的默认值）。"""
    c = argparse.ArgumentParser(add_help=False)
    c.add_argument("--root", default=argparse.SUPPRESS, help="语言目录，默认 ScreenKit/lang")
    c.add_argument("--dry-run", action="store_true", default=argparse.SUPPRESS, help="只预览，不写文件")
    c.add_argument("--sync", action="store_true", default=argparse.SUPPRESS,
                   help="同时覆盖 ScreenKit/bin 下已有的 lang 副本")
    return c


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        prog="langtext.py",
        description="新增或修改 ScreenKit 界面文案（ScreenKit/lang/*.toml）。",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=EXAMPLES,
    )
    p.add_argument("--root", default=str(LANGDIR), help="语言目录，默认 ScreenKit/lang")
    p.add_argument("--dry-run", action="store_true", help="只预览，不写文件")
    p.add_argument("--sync", action="store_true", help="同时覆盖 ScreenKit/bin 下已有的 lang 副本")
    common = common_flags()
    sub = p.add_subparsers(dest="cmd", required=True)

    s = sub.add_parser("set", help="新增或修改一条文案", parents=[common])
    s.add_argument("key", help="文案键，如 app.demo.title")
    s.add_argument("text", nargs="?", default=None, help="文本；含换行写 \\n")
    s.add_argument("-l", "--lang", action="append", default=[],
                   help="目标语言：zh / en / all / zh,en；可重复。默认 zh")
    s.add_argument("--each", action="append", default=[], metavar="CODE=TEXT",
                   help="逐语言给不同文本，可重复，如 --each en=English")
    s.add_argument("--after", default=None, help="新增时插到这个键后面")
    s.add_argument("--if-missing", action="store_true", help="只在缺键或为空时写入")
    s.add_argument("--add-only", action="store_true", help="只在键不存在时写入")
    s.set_defaults(func=cmd_set)

    g = sub.add_parser("get", help="查看某个键在各语言下的值", parents=[common])
    g.add_argument("key")
    g.add_argument("-l", "--lang", action="append", default=[], help="目标语言，默认全部")
    g.set_defaults(func=cmd_get)

    m = sub.add_parser("missing", help="列出各语言相对基础语言缺失或为空的键", parents=[common])
    m.add_argument("-l", "--lang", action="append", default=[], help="目标语言，默认全部")
    m.add_argument("--base", default="zh", help="基础语言，默认 zh")
    m.add_argument("--limit", type=int, default=20, help="每种语言最多列几条，0 表示全部")
    m.set_defaults(func=cmd_missing)

    f = sub.add_parser("fill", help="用某个语言的文案补齐其它语言", parents=[common])
    f.add_argument("--from", dest="src", default="zh", help="源语言，默认 zh")
    f.add_argument("-l", "--lang", action="append", default=[], help="目标语言，默认除源语言外全部")
    f.add_argument("--overwrite", action="store_true", help="已存在的键也覆盖")
    f.set_defaults(func=cmd_fill)

    c = sub.add_parser("check", help="检查各语言键集合的差异", parents=[common])
    c.set_defaults(func=cmd_check)

    l = sub.add_parser("langs", help="列出语言文件", parents=[common])
    l.set_defaults(func=cmd_langs)
    return p


def main(argv: list[str]) -> int:
    args = build_parser().parse_args(argv[1:])
    files = lang_files(Path(args.root))
    if not files:
        raise SystemExit(f"没有找到语言文件：{args.root}")
    return args.func(args, files)


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
