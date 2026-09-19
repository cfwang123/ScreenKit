# -*- coding: utf-8 -*-
"""把 android 侧最新 APK 拷到 ScreenKit 输出目录 apk/（仅当源更新）。"""
from __future__ import annotations

import re
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ANDROID = ROOT / "android"
GRADLE = ANDROID / "app" / "build.gradle.kts"


def gradle_version() -> str:
    if not GRADLE.is_file():
        return "1.0.0"
    text = GRADLE.read_text(encoding="utf-8")
    m = re.search(r'versionName\s*=\s*["\']([^"\']+)["\']', text)
    return m.group(1).strip() if m else "1.0.0"


def parse_ver(name: str) -> tuple[int, int, int]:
    m = re.search(r"(\d+)\.(\d+)(?:\.(\d+))?", name or "")
    if not m:
        return (0, 0, 0)
    return (int(m.group(1)), int(m.group(2)), int(m.group(3) or 0))


def is_debug(path: Path) -> bool:
    s = str(path).replace("\\", "/").lower()
    n = path.name.lower()
    return "debug" in n or "/debug/" in s


def collect_sources() -> list[Path]:
    globs = [
        ANDROID / "release" / "*.apk",
        ANDROID / "app" / "build" / "outputs" / "apk" / "release" / "*.apk",
        ANDROID / "app" / "build" / "outputs" / "apk" / "debug" / "*.apk",
    ]
    out: list[Path] = []
    for g in globs:
        parent, pattern = g.parent, g.name
        if not parent.is_dir():
            continue
        for f in parent.glob(pattern):
            try:
                if f.is_file() and f.stat().st_size > 64:
                    out.append(f.resolve())
            except OSError:
                continue
    return out


def rank(path: Path) -> tuple:
    st = path.stat()
    ver = parse_ver(path.name)
    if ver == (0, 0, 0):
        ver = parse_ver(gradle_version())
    return (0 if is_debug(path) else 1, ver, st.st_mtime, st.st_size)


def pick_latest(files: list[Path]) -> Path | None:
    if not files:
        return None
    release = [p for p in files if not is_debug(p)]
    pool = release or files
    return max(pool, key=rank)


def dest_name(src: Path) -> str:
    n = src.name
    if n.lower().startswith("screenkit") and n.lower().endswith(".apk"):
        return n
    ver = parse_ver(n)
    ver_s = gradle_version() if ver == (0, 0, 0) else f"{ver[0]}.{ver[1]}.{ver[2]}"
    suffix = "-debug" if is_debug(src) else ""
    return f"screenkit{ver_s}{suffix}.apk"


def existing_apks(dest_dir: Path) -> list[Path]:
    if not dest_dir.is_dir():
        return []
    out = []
    for f in dest_dir.glob("*.apk"):
        try:
            if f.is_file() and f.stat().st_size > 64:
                out.append(f)
        except OSError:
            continue
    return out


def newer_than(src: Path, dest: Path | None) -> str | None:
    """返回需要复制的原因；None 表示跳过。"""
    if dest is None or not dest.is_file():
        return "目标不存在"
    sv, dv = rank(src)[1], rank(dest)[1]
    if sv > dv:
        return f"版本 {fmt_ver(sv)} > {fmt_ver(dv)}"
    if sv < dv:
        return None
    if not is_debug(src) and is_debug(dest):
        return "release 覆盖 debug"
    if is_debug(src) and not is_debug(dest):
        return None
    ss, ds = src.stat(), dest.stat()
    if ss.st_mtime > ds.st_mtime + 1 or ss.st_size != ds.st_size:
        return "源文件更新"
    return None


def fmt_ver(v: tuple[int, int, int]) -> str:
    return f"{v[0]}.{v[1]}.{v[2]}"


def copy_to(src: Path, out_dir: Path) -> None:
    apk_dir = out_dir / "apk"
    apk_dir.mkdir(parents=True, exist_ok=True)
    name = dest_name(src)
    dest = apk_dir / name
    old = pick_latest(existing_apks(apk_dir))
    # 同名已有文件也参与比较
    if dest.is_file():
        old = dest
    why = newer_than(src, old)
    if not why:
        print(f"APK 已是最新，跳过: {dest}")
        return
    shutil.copy2(src, dest)
    print(f"已复制 APK（{why}）: {src} -> {dest}")
    for f in existing_apks(apk_dir):
        if f.resolve() != dest.resolve():
            try:
                f.unlink()
                print(f"已删除旧 APK: {f.name}")
            except OSError as ex:
                print(f"删除旧 APK 失败 {f.name}: {ex}")


def main(argv: list[str]) -> int:
    dests = [Path(a.strip().strip('"').rstrip("\\/")) for a in argv[1:] if a.strip()]
    if not dests:
        print("用法: copy-latest-apk.py <输出目录> [输出目录…]", file=sys.stderr)
        return 2
    srcs = collect_sources()
    src = pick_latest(srcs)
    if src is None:
        print("未找到 android APK，跳过复制")
        return 0
    print(f"最新 APK: {src}")
    for d in dests:
        try:
            copy_to(src, d)
        except OSError as ex:
            print(f"复制到 {d} 失败: {ex}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
