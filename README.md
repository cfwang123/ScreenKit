# ScreenKit

Windows desktop tool (exe `ScreenKit.exe`; Chinese UI title **屏幕截图工具**): screenshot, annotate, OCR, barcode/QR, long screenshot, screen/GIF recording, PDF workbench, ASR/TTS, LLM chat, translation, face, local HTTP API, LAN file transfer, and LAN/USB screencast with an Android companion.

**Current version: 1.0.10** · [GitHub Releases](https://github.com/cfwang123/ScreenKit/releases/latest)

**Languages:** [English](README.md) · [中文](README.zh.md)

## Contents

1. [Download](#download)
2. [Screenshots](#screenshots)
3. [Features](#features)
4. [Requirements](#requirements)
5. [Using ScreenKit](#using-screenkit)
6. [Install features](#install-features)
7. [Configuration](#configuration)
8. [HTTP API](#http-api-overview)
9. [CLI](#cli)
10. [x86host](#x86host-32-bit-sapi-only)
11. [Build from source](#build-from-source)
12. [License](#license)

## Download

| File | What |
|------|------|
| [`screenkit_1.0.10.7z`](https://github.com/cfwang123/ScreenKit/releases/latest) | Windows x64 app (slim package: exe + managed deps). Install models and runtimes in-app. |
| `screenkit1.0.10.apk` | Android companion: **传文件** + **投屏** launchers in separate tasks (same release page, or **File sync → Install on phone**). |

Unpack the 7z and run `ScreenKit/ScreenKit.exe`. First launch may open the install wizard. Requires [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48).

Changelog: [CHANGELOG.md](CHANGELOG.md) (English + 中文 per version).

## Screenshots

![ScreenKit main window](docs/1%20screenshot.en.png)

## Features

### Capture and recognition

| Area | Description |
|------|-------------|
| **Screenshot OCR** | Region capture → text OCR or barcode/QR; multi-monitor DXGI. Korean/English word spaces restored from visual gaps (no spaces between CJK). Optional overlay translation via LLM. |
| **Annotate** | WeChat-style tools: rect / ellipse / arrow / pen / text; dropdown next to confirm for copy-as image / file / path. |
| **Long screenshot** | Pick a scrollable window → auto-scroll stitch (no OCR). |
| **Screen recording** | Window or region → HUD → MP4 (x264/x265/AV1 via **FFmpeg only**) + optional system/mic audio; optional mouse cursor and click highlight. |
| **GIF recording** | Same region flow → 24 fps capture → preview (FPS, scale, palette) → silent GIF. |
| **Clipboard** | Paste image and OCR; Edit menu copy image / file / path; menu/tray sets on-capture copy mode. |
| **Overlay text** | Click-release selects one OCR block; empty click clears; drag-select stays in range. Ctrl+C copies. |
| **PDF workbench** | Open PDF → page OCR → edit lines → export searchable PDF. |

### Speech, LLM, translation, face

| Area | Description |
|------|-------------|
| **ASR / TTS** | Live captions: offline or streaming. Sherpa / SAPI / WinRT offline TTS and Edge online natural voices. |
| **LLM chat** | WeChat-style bubbles, Clear, mic, Speak / Auto speak, optional web + `tmp/llm/` tools. |
| **Translation** | Local Opus-MT ONNX or a configured **LLM**; 20-trip round-trip stops early on a repeat; popup `Ctrl+Alt+T`. |
| **Face** | InsightFace ONNX detect/compare; optional landmarks and gender/age. Models in `facemodels/`. |
| **SAPI x86 helper** | Sidecar `x86host.exe` for classic voices visible only to 32-bit processes. |

### Transfer, API, setup

| Area | Description |
|------|-------------|
| **PC file transfer** | Same HTTP API port (`1224`) + UDP discovery `17531`. **File sync** tab lists the `sendfile/` inbox (no subfolders): select, marquee, cut/copy/paste, delete to Recycle Bin, drag to Explorer. Drop on the lower zone to send to the phone while connected; phone shares land in `sendfile/`. **Install on phone** shows a LAN URL and QR. **Web manager** (same port): desktop `/`, phone `/m` (white UI, SVG icons); login to upload/manage; download only needs `/f/…`. Companion: `android/` (`com.whj.screenkit`, launchers **传文件** / **投屏**). |
| **HTTP API** | Local JSON API (default `127.0.0.1:1224`). Main-window tab: call log + request builder. |
| **Install features** | Feature tree (installed items checked); add (green) / remove (red); Confirm installs/uninstalls. Voices on a separate tab. CN mirrors when locale is Chinese. |
| **Image convert** | **Tools → Image convert**: batch JPG/PNG/BMP, optional max size, rotate/flip; icon view with small thumbnails or a details list; live preview of the selected file after those settings; write to `output/` next to each source, a chosen folder, or replace the source (permanently delete, or Recycle Bin). Optional: keep the original file when the result is still ≥ N% of the original size (default 80%; rotate/resize still writes the new file). |
| **QR / barcode** | **Tools → QR / barcode**: QR, Data Matrix, Code 128/39, EAN, UPC. One line of original text under the image. UTF-8, GBK, or Hex bytes (default UTF-8). |
| **Batch rename** | **Tools → Batch rename**: source list (add/remove/move, sort by name / modified / added); new names in a textarea. Everything / FastCopy patterns (`%1`/`%2` shortest capture, `#` / `###`); changing the list or rules recalculates names. |
| **Hash** | **Tools → Hash**: MD5 / SHA-1 / SHA-256; paste an expected hash to match. |
| **Text tools** | **Tools → Text tools**: Base64, URL, UTF-8/GBK hex, Unicode escape, case, whitespace, counts, smart JSON pretty-print (short arrays/objects stay on one line). |
| **Password generator** | **Tools → Password generator**: crypto-random; length, sets, skip `0OIl1`, at least one of each class. Remembers last settings. **Word lex** tab: pick an LLM, then translations (including Literary Chinese, Ancient Greek, Latin, Sanskrit, Biblical Hebrew) plus pinyin / romaji / romanization. **Variants** tab: from a seed password, the LLM mostly translates into other languages and spells them in ASCII romanization (at most one English paraphrase; no extra symbols, digits, leetspeak, or word-reordering). |
| **Network tools** | **Tools → Network tools**: Ping, DNS, WHOIS, traceroute, ping-locate, proxy-locate, HTTP speed-locate. |
| **Screencast** | **Tools → Screencast** (same under the tray Tools menu): receive a phone or another PC over LAN/USB, or cast this desktop out. Quality 540p/720p/1080p, optional audio. The viewer is not always-on-top; picture can **Fit** or **Fill** the window. Discovery UDP 19518, media TCP 19519. USB without network: receive starts the AOA helper. Or **USB 投屏(adb)** with USB debugging. The phone app has a separate **投屏** launcher icon. |
| **Hotkeys** | Toggle window · snap annotate · snap OCR · voice input · translate popup (configurable). |
| **Main tabs** | Settings → General can hide OCR / TTS / ASR / Chat / Translate / Face / HTTP / File sync (`tab_*_visible`). |
| **Devices** | CPU · NVIDIA CUDA · Intel DirectML; missing accel → CPU. |
| **CLI** | Batch OCR, list models / SAPI voices, probe CUDA, multi-monitor snap test. |

## Requirements

- Windows 10/11 (x64)
- [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48)
- Build: Visual Studio / MSBuild with a `net48` WPF targeting pack
- Optional: NVIDIA GPU + CUDA matching `onnxgpu64`; DirectML GPU for `onnxdml64`
- Optional (record): FFmpeg **4.4 shared** under `ffmpeg64/` next to the exe

## Using ScreenKit

### Screenshot, annotate, OCR

Capture a region (hotkey or menu). Result panel splits **OCR / Barcode**. Overlay translation uses a configured LLM when a dest language is selected. Annotate tools sit on the capture overlay; a region that spans monitors can be drawn on, moved, or resized on every screen it covers (move hot-zone is 10px outside the frame). The Done dropdown copies as image, file, or path and remembers the default.

### Screen / GIF recording

1. **Capture → Screen record** (or GIF record): click a window or drag a region.
2. **HUD** (drawn outside the capture area):
   - Red frame; drag the **5px strip** to move, or **8 grips** to resize. Aspect is free before **Start** and locked afterwards unless `record_lock_aspect = false`.
   - Floating **control bar**: left grip to drag; collapse; **Options** before Start; start/pause share one slot. Stays on the current monitor.
3. Stop → save MP4 (Explorer selects the file) or open the GIF preview (output FPS 1–24, scale, palette) then save a silent GIF.
4. **Capture → Record options**: codec (x264 / x265 / AV1), FPS, CRF / AV1 CRF (0–63, default 56), audio, max size, **record mouse** / **highlight clicks**. AV1 needs libsvtav1 / libaom-av1 in `ffmpeg64`.

GDI capture has no cursor: enable **record mouse** to overlay the pointer; **highlight clicks** draws a short yellow (left) / blue (right) / green (middle) ripple. GIF size grows quickly — use preview scale/FPS and the max-size limit.

### File sync (PC ↔ Android)

1. On the PC, open the **File sync** tab (enable under Settings → API if needed; default on).
2. **Install on phone**: LAN URL `http://<pc-ip>:1224/apk` and QR (defaults to an internet-reachable NIC). Phone and PC on the same LAN; scan with a browser to install.
3. First connection: the phone shows a waiting dialog (cancellable) until the PC allows pairing. The PC dialog stays always-on-top (also when the main window is in the tray).
4. The upper list is the PC `sendfile/` inbox (root only, no subfolder navigation; folders are rows, double-click opens Explorer). Select, marquee, cut/copy/paste, Delete to Recycle Bin, or drag to Explorer. Dropping files onto the list imports them. Drop on the **lower** zone to send to the phone while the app is connected; otherwise nothing is sent. File/image paste goes into the inbox; text paste still goes to the right-hand pane. Phone share/upload always writes to PC `sendfile/`.
5. Both sides show in-progress transfers; the PC also has a transfer log. Text sync shows the latest message in a read-only selectable box.
6. **Web manager** (same HTTP port): **File sync → Web manager** shows the LAN URL, QR, and login password. Desktop page `/`, phone page `/m` (white UI, SVG icons). Sign in to upload, mkdir, rename, delete; optional **Stay signed in**. Open subfolders; zip a folder or the selection. A correct `/f/相对路径` URL downloads without login.

Android details: [android/README.md](android/README.md).

### Screencast (PC ↔ Android / PC ↔ PC)

1. PC **Tools → Screencast** (receive starts with the app; Settings → API can turn it off). Same Wi‑Fi, not a guest network.
2. Phone launcher **投屏** → allow capture / mic / notifications → **Scan** → pick the PC → **Start**. Or type an IP. USB without adb reverse: ScreenKit starts the AOA helper when receive is on (Tools → Screencast → **USB配件** still works). If spacedesk owns the accessory interface it force-binds inbox WinUSB, then bridges bulk; the UI process never calls LibUsb. The phone can also auto-start when the accessory attaches. After ~90s without an accessory it falls back to USB tethering. **USB 投屏(adb)** uses `adb reverse` and needs USB debugging. USB-only test pattern (no capture): `scst_usb_pat`.
3. A view window opens topmost on the monitor under the mouse (title bar visible). **Tab** toggles resolution / fps / bitrate / latency. Closing the window disconnects. Quality changes apply live. Landscape re-encodes at the same short-edge quality (does not crop a portrait frame).
4. PC-to-PC: both receive; the sender scans and casts to the selected host.

### Image convert

**Tools → Image convert**: drop files, folders, or images; choose JPG / PNG / BMP; optional max width/height (same shrink-to-fit as screenshots); rotate 90/180/270° or flip the selected item. **Icons** shows a small thumbnail per file (**List** for details). The right pane previews the selected file after those settings. Convert-all writes next to each source under `output/`, into a chosen folder, or replaces the source (permanently delete, or Recycle Bin; convert-all asks first).

### Default hotkeys

| Hotkey | Action |
|--------|--------|
| `Ctrl+Alt+O` | Toggle main window |
| `Ctrl+Alt+Q` | Screenshot annotate |
| `Ctrl+Alt+W` | Screenshot and OCR |
| `Ctrl+Alt+V` | Voice input (press again to stop) |
| `Ctrl+Alt+B` | Live caption |
| `Ctrl+Alt+T` | Translate popup |

Leave a hotkey string empty in Settings to disable it. Tray: left-click toggles the window; context menu has voice input, translate popup, clipboard OCR, on-capture copy mode, and exit. Closing the main window typically **hides** to tray.

### UI language

**Options → Language** → 中文 / English, or Settings → General (`ui_lang = "zh"` / `"en"`). Covers menus, Settings, OCR toolbar Pack/Lang names (`ocr-display.json` `nameEn`), Translate, Face, and the translate popup.

## Install features

1. First launch may open the wizard (defaults: Simplified-Chinese OCR, first two ASR packs, recording; GPU/iGPU **off**).
2. Later: **Options → Install features**
   - **Features**: installed items are checked; add (green) / remove (red) counts and sizes; Reset restores. Confirm installs and uninstalls. Voices are not on this tab.
   - **Voices**: TTS models with language filter; progress shows **total batch size and downloaded bytes**. `.tar.bz2` packages are extracted in-process (no system `tar` / `bzip2`); a junction-based `ttsmodels` directory is supported.
3. Using a feature that needs a missing package prompts to open the installer (e.g. OCR without any ORT → install `onnxcpu64`).

| Runtime | Role | Typical size |
|---------|------|----------------|
| **onnxcpu64** | CPU ONNX Runtime (required for OCR if no GPU/iGPU ORT) | ~16 MB |
| **onnxgpu64** | NVIDIA CUDA EP + CUDA/cuDNN (optional) | large |
| **onnxdml64** | DirectML EP for iGPU (optional) | ~18 MB |
| **OpenCV** | Capture / image pipeline | ~61 MB |
| **ffmpeg64** | Screen record encode/mux | ~72 MB |

Download prefers CN mirrors when UI or system locale is Chinese.

**Edge online** TTS: 300+ Microsoft natural voices (including Korean), no model or API key. Requires Internet; listing and synthesis go to Microsoft Edge Read Aloud (unofficial; not paid Azure Speech). The configured HTTP proxy is honored.

Optional env vars for local full libraries (do not commit secrets/paths):

| Variable | Meaning |
|----------|---------|
| `WPF_OCR_CUDA_LIB` | Folder with full CUDA / onnxgpu64 DLLs |
| `WPF_OCR_FFMPEG_LIB` | Folder with FFmpeg 4.4 shared DLLs |

## Configuration

Settings live in `config.toml` beside the exe (**Options → Settings** / **Record options**). Tabs: General, OCR, Hotkeys, Speech, LLM, Translate, Capture, API. Main-tab visibility checkboxes are under **General**.

| Section | Keys |
|---------|------|
| `[ocr]` | pack, variant, device (`Cpu` / `Gpu` / `IntelGpu`), det thresholds |
| `[ui]` | hotkeys, tray, `ui_lang`, `tab_*_visible`, `update_check_days`, `http_proxy`, `capture_log`, `llm_log`, `screenshot_keep_days`, `imgconv_*` |
| `[http]` | OCR API bind (`127.0.0.1:1224`), service mode |
| `[sendfile]` | LAN file transfer ports, display name, web login password, paired devices |
| `[pdf]` | invisible text, raster DPI |
| `[asr]` | voice/live mode, polish, split, `asr_llm` |
| `[[llm]]` | OpenAI-compatible endpoints (`name` / `url` / `key` / `model` / `think`) |
| `[translate]` | local ONNX device or `translate_llm` |
| `[record]` / `[gif_record]` | codec, CRF, audio, mouse overlay |

Do **not** commit a machine-specific `config.toml`.

<details>
<summary>Example <code>config.toml</code></summary>

```toml
[ocr]
model_pack = "rapid-ch"
model_variant = "简体中文 mobile"
device = "Cpu"          # Cpu | Gpu | IntelGpu
det_limit = 960
det_thresh = 0.3
det_box_thresh = 0.5
use_cls = true

[ui]
hotkey = "Ctrl+Alt+O"           # show / hide main window
hotkey_snap = "Ctrl+Alt+Q"      # screenshot annotate
hotkey_snap_ocr = "Ctrl+Alt+W"  # screenshot + OCR
# hotkey_translate = "Ctrl+Alt+T" # translate popup show/hide
minimize_to_tray = true
capture_log = false             # true → log/capture.log (DPI + save timings)
# llm_log = false               # true → log/llm.log (polish HTTP; API key not written)
ui_lang = "zh"                  # zh | en
tab_ocr_visible = true          # main tabs; all default to true
tab_tts_visible = true
tab_asr_visible = true
tab_chat_visible = true
tab_translate_visible = true
tab_face_visible = true
tab_http_visible = true
tab_sendfile_visible = true
update_check_days = 7           # auto-check interval on startup (days); 0 = off
screenshot_keep_days = 3        # screenshot history; 0 = unlimited. Cleaned at startup only
# http_proxy = false
# http_proxy_addr = "127.0.0.1:7897"
# ocr_translate_lang = ""       # empty = off; LLM lang code (zh/en/ja/ko/…)

[http]
http_enabled = true
http_host = "127.0.0.1"
http_port = 1224
service_mode = false            # keep engine warm

[sendfile]
sendfile_enabled = true
# sendfile HTTP shares http_port (1224)
sendfile_udp_port = 17531
sendfile_name = ""              # empty = machine name
sendfile_web_pass = ""          # web manager login; empty = auto-generated on start
# [[sendfile_device]]           # paired phones (id / name / token)

[pdf]
pdf_invisible_text = true
pdf_dpi = 150

[record]
record_codec = "x264"           # x264 | x265 | av1
record_fps = 24
record_crf = 28                 # x264/x265 only, 0–51
record_av1_crf = 56             # AV1 only, 0–63 (56 ≈ half of x265 CRF28 size)
record_audio = true
record_audio_src = "Speakers"   # Speakers | Mic | MicAndSpeakers
record_audio_kbps = 96
record_max_size = false
record_max_w = 1920
record_max_h = 1080
record_lock_aspect = true
record_mouse = true
record_click_highlight = true

[asr]
asr_voice_mode = "stream"       # stream | offline
asr_voice_polish = true
asr_voice_split = true
asr_voice_split_sec = 5
asr_live_mode = "stream"
asr_live_polish = false
asr_live_split = true
asr_llm = "gpt-4o-mini"
# asr_llm_prompt = "..."
# chat_llm = ""
# chat_llm_prompt = "..."
# chat_agent = true
# chat_auto_tts = true

[[llm]]
name = "gpt-4o-mini"
url = "https://api.openai.com/v1"
# key = ""
model = "gpt-4o-mini"
think = "low"                   # off | low | medium | high | max

[translate]
translate_compute = "Auto"      # Auto | Gpu | Cpu | Igpu
# translate_llm = ""
# translate_llm_prompt = "请将用户给出的文本从{src}翻译为{dst}。只输出译文。"

[gif_record]
gif_fps = 8
gif_max_size = true
gif_max_w = 1280
gif_max_h = 720
gif_colors = 128
gif_scale = 100
gif_mouse = true
gif_click_highlight = true
```

</details>

`think`: `off` sends `thinking.type=disabled`; `low`/`medium`/`high`/`max` send `thinking.type=enabled` plus `reasoning_effort`. If `off` is rejected, retry with `low`. Access to **opencode.ai** adds `x-opencode-session` / `x-opencode-client`. Old keys `asr_llm_url` / `asr_llm_token` / `asr_llm_model` are ignored.

Set `capture_log = true` for `log/capture.log` (DPI / save timings; `SLOW` if ≥500ms). Set `llm_log = true` for `log/llm.log` (API keys are not written). CLI `ScreenKit --snap` dumps full-monitor bitmaps under `log/snap/`; `--test-overlay-layout` shows the screenshot overlay and logs per-monitor HWND/DPI.

## HTTP API (overview)

When enabled, the OCR API listens on `http_host:http_port` (default loopback only). Bind to `127.0.0.1` unless you intentionally expose it on a trusted network.

LAN **PC file transfer** shares this HTTP port (`1224`) plus UDP `17531` (pairing required): [HTTP-API.md](HTTP-API.md) · [android/README.md](android/README.md).

- `GET  /api` · `/api/status`
- `POST /api/ocr` — `box` is original-image pixels
- `POST /api/qr` — barcode / QR only (`/api/barcode`)
- `GET  /api/ocr/get_options`
- `GET  /api/asr/models` · `POST /api/asr`
- `GET  /api/tts/models` · `POST /api/tts` — Sherpa, SAPI, Windows (`engine=winrt`), Edge (`engine=edge`)
- `POST /api/itn`
- `POST /api/translate` — LLM batch (`items[]`)
- `GET  /api/face/models` · `POST /api/face`

Full field reference: **[HTTP-API.md](HTTP-API.md)** · **[HTTP接口文档.md](HTTP接口文档.md)** (中文).

## CLI

```text
ScreenKit --image <path> [options]
ScreenKit --snap [--out <dir>]
ScreenKit --test-overlay-layout   # screenshot overlay HWND/DPI per monitor
ScreenKit --test-overlay-span-adj # cross-monitor region handles on guest screens
ScreenKit --test-clipboard-path   # path copy after delayed image; 4K timing
ScreenKit --test-apk-qr            # encode/decode LAN APK QR; HTTP GET /apk
ScreenKit --test-img-convert       # png→jpg rotate 90 + max 100×100
ScreenKit --test-qr-make
ScreenKit --test-rename
ScreenKit --test-hash
ScreenKit --test-texttool
ScreenKit --test-pwgen
ScreenKit --test-nettool
ScreenKit --test-sendfile          # sendfile sandbox + web login / public download
ScreenKit --test-face-overlay
ScreenKit --list-models
ScreenKit --list-face
ScreenKit --list-sapi              # local SAPI + (x64) x86host voices
ScreenKit --test-tts-sherpa <model> # `-d auto|gpu|cpu`
ScreenKit --list-edge-tts
ScreenKit --test-edge-tts ko-KR-SunHiNeural
ScreenKit --test-http-tts
ScreenKit --test-llm-chat
ScreenKit --test-llm-agent
ScreenKit --test-http-chat
ScreenKit --probe-cuda
ScreenKit --help
```

Useful options: `-d gpu|cpu`, `-p rapid-ch`, `-v <variant>`, `-m <models-dir>`, `--det-limit`, `--no-cls`.

## x86host (32-bit SAPI only)

Some classic **SAPI** voices register only for 32-bit processes. Ship **`x86host.exe`** next to `ScreenKit.exe` (built from `x86host/`; a Release build of ScreenKit also builds and copies it).

| Item | Detail |
|------|--------|
| Role | HTTP helper: list SAPI voices + synth WAV; **no GUI** |
| Start | On demand by the x64 app, or run `x86host.exe` |
| Bind | `127.0.0.1` only, default port **17886** |
| Idle | Exit after **60s** (`--idle-ms` to override) |
| API | `GET /api/sapi/status` · `GET /api/sapi/voices` · `POST /api/sapi/synth` · `POST /api/sapi/shutdown` |

```text
dotnet build x86host/x86host.csproj -c Release
x86host.exe --port 17886 --idle-ms 60000
x86host.exe --list-sapi
```

In the UI, engine **SAPI** lists local voices plus **x86-only** entries (name ends with `· x86`).

## Build from source

```
OCR/
├── ScreenKit/                 # WPF app (net48, x64)
│   └── bin/Release/
│       ├── net48/          # Dev output (models / runtimes live here)
│       └── ScreenKit/      # Slim package: exe + x86host.exe + managed deps
├── x86host/                # 32-bit SAPI HTTP helper
├── android/                # Companion app (com.whj.screenkit)
├── docs/                   # README screenshots
├── scripts/publish-release.mjs
├── README.md · README.zh.md · CHANGELOG.md
└── HTTP-API.md · HTTP接口文档.md
```

Model packs and large native runtimes are **not** in git. Place them next to the exe (or install in-app):

```
ScreenKit/bin/Release/net48/
├── ScreenKit.exe
├── config.toml
├── ocrmodels/  asrmodels/  ttsmodels/  translatemodels/  facemodels/
├── onnxcpu64/  onnxgpu64/  onnxdml64/
└── ffmpeg64/
```

A **Release** build does not copy those folders. Each OCR pack needs ONNX + `configs.txt` (and dict/keys). Optional `pack.json` (`name` / `nameEn` / `variants`) supplies English UI labels; defaults live in `ocr-display.json`.

```bash
cd ScreenKit
dotnet build -c Release
./ScreenKit/bin/Release/net48/ScreenKit.exe
```

### Slim package (`bin\Release\ScreenKit\`)

- Includes: `ScreenKit.exe`, **`x86host.exe`**, managed deps, **`wetext/`** (ITN), Assets, LICENSE.
- Does **not** include: OCR/ASR/TTS/face models, ORT, OpenCV/Skia/PDFium/Sherpa natives, `ffmpeg64`.
- End users install those via **Install features**. Local Opus-MT ONNX is placed under `translatemodels/` by hand if needed.

For local development with models already present, run **`bin\Release\net48\`**.

### Release archive

```bash
node scripts/publish-release.mjs
```

Release-builds, then packs `ScreenKit/bin/Release/ScreenKit/` into `release/screenkit_<version>.7z` (needs [7-Zip](https://www.7-zip.org/) on PATH). `release/` is gitignored.

Release documentation:

- Every `CHANGELOG.md` version has matching **English** and **中文** sections.
- Every GitHub Release description is bilingual (English first, Chinese second).
- Checksums and links are listed once after both summaries.

## License

**ScreenKit application source** is **MIT**. See [LICENSE](LICENSE).

```
Copyright (c) 2026 ScreenKit Contributors
```

**THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND.**

Bundled or optional dependencies are **not** all MIT (models, FFmpeg, CUDA/cuDNN, some natives). See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Do not commit large ONNX weights, CUDA redistributables, or FFmpeg shared binaries without a redistribution plan.

## See also

- [CHANGELOG.md](CHANGELOG.md)
- [HTTP-API.md](HTTP-API.md) · [HTTP接口文档.md](HTTP接口文档.md)
- [android/README.md](android/README.md) — companion app
- [LICENSE](LICENSE) · [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
- [README.zh.md](README.zh.md)
