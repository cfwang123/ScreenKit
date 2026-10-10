# 给 ScreenKit.exe 写入 Win32 字符串 101。
# 中文（简体，语言 0x0804）为「屏幕截图工具」，英文（0x0409）为 ScreenKit。
# 字符串编号必须与 ScreenKit/Infrastructure/ShellName.cs 的 STRING_ID 一致。
import ctypes
from ctypes import wintypes
import sys

RT_STRING = 6
STRING_ID = 101
LANG_EN = 0x0409
LANG_ZH = 0x0804
NAMES = (
    (LANG_EN, "ScreenKit"),
    (LANG_ZH, "屏幕截图工具"),
)


def winerr(what):
    err = ctypes.get_last_error()
    raise SystemExit(f"{what} 失败，Win32 {err}")


def encode_block(text):
    # 16 条一组。101 在第 7 组的第 5 条（从 0 数）。
    slot = STRING_ID % 16
    parts = []
    raw = text.encode("utf-16le")
    n = len(text)
    for i in range(16):
        if i != slot:
            parts.append(b"\x00\x00")
            continue
        parts.append(n.to_bytes(2, "little"))
        parts.append(raw)
    return b"".join(parts)


def stamp(path):
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    begin = k32.BeginUpdateResourceW
    begin.argtypes = [wintypes.LPCWSTR, wintypes.BOOL]
    begin.restype = wintypes.HANDLE
    update = k32.UpdateResourceW
    update.argtypes = [
        wintypes.HANDLE,
        ctypes.c_void_p,
        ctypes.c_void_p,
        wintypes.WORD,
        ctypes.c_void_p,
        wintypes.DWORD,
    ]
    update.restype = wintypes.BOOL
    end = k32.EndUpdateResourceW
    end.argtypes = [wintypes.HANDLE, wintypes.BOOL]
    end.restype = wintypes.BOOL

    h = begin(path, False)
    if not h:
        winerr("BeginUpdateResource " + path)
    bufs = []
    block = STRING_ID // 16 + 1
    try:
        for lang, text in NAMES:
            blob = encode_block(text)
            buf = ctypes.create_string_buffer(blob)
            bufs.append(buf)
            ok = update(h, RT_STRING, block, lang, ctypes.addressof(buf), len(blob))
            if not ok:
                winerr(f"UpdateResource {path} lang={lang:04x}")
        if not end(h, False):
            winerr("EndUpdateResource " + path)
    except BaseException:
        end(h, True)
        raise
    print(f"shell name stamped: {path}")


def main():
    if len(sys.argv) < 2:
        raise SystemExit("用法: stamp-shell-name.py <ScreenKit.exe> [...]")
    for path in sys.argv[1:]:
        stamp(path)


if __name__ == "__main__":
    main()
