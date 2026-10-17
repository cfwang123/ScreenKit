# WinOCR 字体截图样本

用于 Windows 中英混合 OCR 回归：`text/` 放原文 `.txt`，`html/` 用多种系统字体排版，`screenshots/` 为 Chrome 无头截图。

## 生成（Chrome 无头，不弹窗）

```text
scripts\shot-winocr-samples.cmd
```

一条命令：根据 `text/*.txt` 生成 `html/*.html`，再调用 Chrome `--headless --screenshot=…` 写入 `screenshots/*.png`。需要另复制时设环境变量 `WINOCR_SHOT_COPY` 为目录。

```text
scripts\shot-winocr-samples.cmd --html-only
scripts\shot-winocr-samples.cmd --shot-only
```

实现见 `fixtures/winocr-samples/gen_html.py`。

## 手动预览

需要肉眼核对时，用普通 Chrome 打开 `html\index.html`（与无头截图无关）。

## 字体

Segoe UI、微软雅黑、宋体、黑体、等线、Arial、Times New Roman、Consolas、Courier New。

`01-dsh-warning` 页使用浅灰底 + 棕色警告样式，并带一行淡色提示（模拟终端 UI）。
