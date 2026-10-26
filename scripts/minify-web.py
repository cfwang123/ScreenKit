"""编译产物 web 目录的 js/css 压缩。

    python -X utf8 scripts/minify-web.py <webdir>         生成 *.min.js / *.min.css（源文件较新才做）
    python -X utf8 scripts/minify-web.py --slim <webdir>  精简包：js/css 原地换成压缩内容，删掉生成的 *.min.* 副本

tools.js 由 join-tools-js.py 先合成，这里只负责压缩。L.js 是运行时按语言包生成的，跳过。
"""
import os
import subprocess
import sys
from pathlib import Path

NO_MINIFY = {"l.js"}


def srcs(d):
    for p in sorted(d.iterdir()):
        if not p.is_file():
            continue
        low = p.name.lower()
        if low.endswith(".min.js") or low.endswith(".min.css") or low in NO_MINIFY:
            continue
        if p.suffix.lower() in (".js", ".css"):
            yield p


def minpath(p):
    return p.with_name(p.stem + ".min" + p.suffix)


def minify(src, dst):
    tmp = dst.with_name(dst.name + ".tmp")
    try:
        r = subprocess.run(["minify", "-o", str(tmp), str(src)], capture_output=True)
    except FileNotFoundError:
        sys.exit("minify.exe 不在 PATH 上")
    if r.returncode != 0 or not tmp.is_file() or tmp.stat().st_size == 0:
        tmp.unlink(missing_ok=True)
        sys.exit("minify 失败: %s\n%s" % (src.name, r.stderr.decode("utf-8", "replace")[:400]))
    os.replace(tmp, dst)


def do_min(d):
    n = 0
    for p in srcs(d):
        dst = minpath(p)
        if dst.is_file() and dst.stat().st_mtime >= p.stat().st_mtime:
            continue
        minify(p, dst)
        n += 1
    if n:
        print("web minify: %d file(s) updated" % n)


def do_slim(d):
    n = 0
    for p in list(srcs(d)):
        dst = minpath(p)
        if dst.is_file() and dst.stat().st_size > 0:
            os.replace(dst, p)
        else:
            minify(p, p)
        n += 1
    # 删掉编译期生成的副本。viewer.min.* 本身是发布文件，没有同名源文件，保留。
    for p in list(d.iterdir()):
        if not p.is_file():
            continue
        low = p.name.lower()
        if not (low.endswith(".min.js") or low.endswith(".min.css")):
            continue
        plain = p.with_name(p.name[: -len(".min" + p.suffix)] + p.suffix)
        if plain.is_file():
            p.unlink()
    if n:
        print("slim web minify: %d file(s)" % n)


def main():
    args = sys.argv[1:]
    slim = "--slim" in args
    args = [a for a in args if a != "--slim"]
    if not args:
        sys.exit(__doc__)
    d = Path(args[0])
    if not d.is_dir():
        sys.exit("web 目录不存在: " + str(d))
    (do_slim if slim else do_min)(d)


if __name__ == "__main__":
    main()
