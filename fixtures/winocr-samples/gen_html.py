# -*- coding: utf-8 -*-
"""生成多字体 HTML，并用 Chrome 无头模式截图（不弹出窗口）。"""
import argparse
import html
import os
import shutil
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
TEXT_DIR = os.path.join(HERE, "text")
HTML_DIR = os.path.join(HERE, "html")
SHOT_DIR = os.path.join(HERE, "screenshots")

FONTS = [
    ("Segoe UI", "Segoe UI, sans-serif"),
    ("Microsoft YaHei", "'Microsoft YaHei', sans-serif"),
    ("SimSun", "SimSun, 'NSimSun', serif"),
    ("SimHei", "SimHei, sans-serif"),
    ("DengXian", "DengXian, '等线', sans-serif"),
    ("Arial", "Arial, Helvetica, sans-serif"),
    ("Times New Roman", "'Times New Roman', Times, serif"),
    ("Consolas", "Consolas, monospace"),
    ("Courier New", "'Courier New', monospace"),
]

WARN_STEMS = ("01-dsh-warning",)
SHOT_W = 1520
SHOT_H = 2400


def read_text(path):
    with open(path, encoding="utf-8") as f:
        return f.read().strip()


def page_body(stem, body_lines):
    warn = any(stem.startswith(w) for w in WARN_STEMS)
    extra = ""
    if warn:
        extra = """
    .warn {
      background: #ececec;
      color: #9a3412;
      padding: 10px 14px;
      border-radius: 4px;
      margin-bottom: 8px;
    }
    .hint {
      color: #9ca3af;
      font-size: 12px;
      margin-bottom: 14px;
    }
"""
    return f"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8"/>
  <title>{html.escape(stem)}</title>
  <style>
    * {{ box-sizing: border-box; }}
    body {{
      margin: 0;
      padding: 20px 24px 32px;
      background: #f8fafc;
      width: {SHOT_W}px;
    }}
    h1 {{
      font: 600 15px/1.4 "Segoe UI", sans-serif;
      color: #334155;
      margin: 0 0 18px;
    }}
    .block {{
      margin-bottom: 22px;
      padding-bottom: 18px;
      border-bottom: 1px solid #e2e8f0;
    }}
    .label {{
      font: 11px/1.2 Consolas, monospace;
      color: #64748b;
      margin-bottom: 6px;
    }}
    .line {{
      font-size: 17px;
      line-height: 1.45;
      color: #0f172a;
      white-space: pre-wrap;
      word-break: break-word;
    }}
{extra}
  </style>
</head>
<body>
  <h1>{html.escape(stem)}</h1>
{body_lines}
</body>
</html>
"""


def blocks_for(text, stem):
    parts = []
    warn = any(stem.startswith(w) for w in WARN_STEMS)
    if warn:
        parts.append(
            '  <p class="hint">提示：/update 自动更新 /ul 开重启恢复会话 /tips 更多技巧</p>'
        )
    for name, family in FONTS:
        inner = html.escape(text)
        cls = "line warn" if warn else "line"
        parts.append(
            f'  <div class="block">\n'
            f'    <div class="label">{html.escape(name)}</div>\n'
            f'    <div class="{cls}" style="font-family:{family}">{inner}</div>\n'
            f"  </div>"
        )
    return "\n".join(parts)


def gallery_index(entries):
    items = "\n".join(
        f'    <li><a href="{html.escape(h)}">{html.escape(stem)}</a></li>'
        for stem, h in entries
    )
    return f"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8"/>
  <title>winocr font samples</title>
  <style>
    body {{ font: 15px/1.5 "Segoe UI", sans-serif; margin: 24px; }}
    a {{ color: #2563eb; }}
  </style>
</head>
<body>
  <h1>WinOCR 字体样本</h1>
  <p>截图：<code>scripts\\shot-winocr-samples.cmd</code>（Chrome 无头，不弹窗）</p>
  <ul>
{items}
  </ul>
</body>
</html>
"""


def gen_html():
    os.makedirs(HTML_DIR, exist_ok=True)
    txts = sorted(f for f in os.listdir(TEXT_DIR) if f.endswith(".txt"))
    if not txts:
        print("no txt in", TEXT_DIR, file=sys.stderr)
        return 1, []
    entries = []
    for fn in txts:
        stem = fn[:-4]
        text = read_text(os.path.join(TEXT_DIR, fn))
        body = blocks_for(text, stem)
        out_name = stem + ".html"
        out_path = os.path.join(HTML_DIR, out_name)
        with open(out_path, "w", encoding="utf-8", newline="\n") as f:
            f.write(page_body(stem, body))
        entries.append((stem, out_name))
        print("html", out_path)
    idx = os.path.join(HTML_DIR, "index.html")
    with open(idx, "w", encoding="utf-8", newline="\n") as f:
        f.write(gallery_index(entries))
    print("index", idx)
    return 0, entries


def find_chrome():
    pf = os.environ.get("ProgramFiles", r"C:\Program Files")
    pfx = os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")
    local = os.environ.get("LocalAppData", "")
    cands = [
        os.path.join(pf, "Google", "Chrome", "Application", "chrome.exe"),
        os.path.join(pfx, "Google", "Chrome", "Application", "chrome.exe"),
        os.path.join(local, "Google", "Chrome", "Application", "chrome.exe"),
    ]
    for p in cands:
        if p and os.path.isfile(p):
            return p
    return None


def shot_page(chrome, html_path, png_path):
    uri = os.path.abspath(html_path).replace("\\", "/")
    if not os.path.isfile(html_path):
        raise FileNotFoundError(html_path)
    uri = "file:///" + uri
    if os.path.isfile(png_path):
        os.remove(png_path)
    cmd = [
        chrome,
        "--headless",
        "--disable-gpu",
        "--hide-scrollbars",
        f"--window-size={SHOT_W},{SHOT_H}",
        f"--screenshot={png_path}",
        uri,
    ]
    subprocess.run(cmd, check=False, capture_output=True)
    time.sleep(0.25)
    if not os.path.isfile(png_path):
        raise RuntimeError("screenshot failed: " + html_path)


def shot_all(copy_dir=None):
    chrome = find_chrome()
    if not chrome:
        print("Chrome not found", file=sys.stderr)
        return 1
    os.makedirs(SHOT_DIR, exist_ok=True)
    pages = sorted(
        f for f in os.listdir(HTML_DIR)
        if f.endswith(".html") and f != "index.html"
    )
    if not pages:
        print("no html pages; run without --shot-only first", file=sys.stderr)
        return 1
    if copy_dir:
        os.makedirs(copy_dir, exist_ok=True)
    n = 0
    for fn in pages:
        n += 1
        stem = fn[:-5]
        html_path = os.path.join(HTML_DIR, fn)
        png_path = os.path.join(SHOT_DIR, stem + ".png")
        shot_page(chrome, html_path, png_path)
        print("shot", png_path)
        if copy_dir:
            dst = os.path.join(copy_dir, "18_{:02d}.png".format(n))
            shutil.copy2(png_path, dst)
            print("copy", dst)
    return 0


def main():
    ap = argparse.ArgumentParser(description="WinOCR 字体样本：HTML + Chrome 无头截图")
    ap.add_argument("--html-only", action="store_true", help="只生成 HTML")
    ap.add_argument("--shot-only", action="store_true", help="只截图（需已有 HTML）")
    ap.add_argument(
        "--copy-dir",
        default=os.environ.get("WINOCR_SHOT_COPY", ""),
        help="额外复制 PNG 到此目录（默认关闭；设 WINOCR_SHOT_COPY 或传入路径启用）",
    )
    args = ap.parse_args()
    copy_dir = (args.copy_dir or "").strip() or None
    if copy_dir and not os.path.isdir(copy_dir):
        try:
            os.makedirs(copy_dir, exist_ok=True)
        except OSError:
            copy_dir = None

    if args.shot_only:
        return shot_all(copy_dir)

    code, _ = gen_html()
    if code != 0:
        return code
    if args.html_only:
        return 0
    return shot_all(copy_dir)


if __name__ == "__main__":
    sys.exit(main())
