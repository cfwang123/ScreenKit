# 把 ScreenKit/Web/tools/*.js 按文件名拼成 tools.js。无 BOM。末尾没有换行则补一个。
import sys
from pathlib import Path

src = Path(sys.argv[1])
dst = Path(sys.argv[2])
files = sorted(src.glob("*.js"), key=lambda p: p.name.lower())
if not files:
    sys.exit("no tool scripts in " + str(src))
parts = []
for f in files:
    text = f.read_text(encoding="utf-8")
    if text and not text.endswith("\n"):
        text += "\n"
    parts.append(text)
data = "".join(parts).encode("utf-8")
dst.parent.mkdir(parents=True, exist_ok=True)
if dst.is_file() and dst.read_bytes() == data:
    sys.exit(0)
dst.write_bytes(data)
