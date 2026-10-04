# ScreenKit

Windows desktop tool (`ScreenKit.exe`): screenshot, annotate, OCR, barcode/QR, long screenshot, screen/GIF recording, PDF workbench, ASR/TTS, LLM chat, translation, face, local HTTP API, LAN file transfer, and LAN/USB screencast with an Android companion.

**Current version: 1.0.13** · [GitHub Releases](https://github.com/cfwang123/ScreenKit/releases/latest)

Changelog: [CHANGELOG.md](CHANGELOG.md) (English and Chinese per version).

**Languages:** [English](README.md) · [Chinese](README.zh.md)

## Contents

1. [Screenshots](#screenshots)
2. [Features](#features)
3. [Requirements](#requirements)
4. [Using ScreenKit](#using-screenkit)
5. [Install features](#install-features)
6. [Configuration](#configuration)
7. [HTTP API](#http-api-overview)
8. [CLI](#cli)
9. [x86host](#x86host-32-bit-sapi-only)
10. [Build from source](#build-from-source)
11. [License](#license)

## Screenshots

![ScreenKit main window](docs/1%20screenshot.en.png)

## Features

### Capture and recognition

| Area | Description |
|------|-------------|
| **Screenshot OCR** | Region capture → text OCR or barcode/QR; multi-monitor DXGI. Korean/English word spaces restored from visual gaps (no spaces between CJK). Optional overlay translation via LLM. The image bar is a compact WPF toolbar; items that do not fit move to the overflow menu. **History** lists `screenshots/` as thumbnails, previews with pan and zoom, and annotates under that preview. With marks, Done, OCR, or Open saves a new screenshot and copies it. With no marks they use the original file and do not write a new one. Done, OCR, and Open then close the history window and show the picture on the main window; OCR also recognizes it. Copy and Ctrl+C put the current picture on the clipboard and leave the window open. |
| **Annotate** | WeChat-style tools: rect / ellipse / arrow / pen / text; dropdown next to confirm for copy-as image / file / path. |
| **Long screenshot** | Pick a scrollable window → auto-scroll stitch (no OCR). |
| **Screen recording** | Window or region → HUD → MP4 + optional system/mic audio; optional mouse cursor and click highlight. Codecs: x264 / x265 / AV1 (FFmpeg), Windows Media Foundation H.264 (MP4, no download). |
| **GIF recording** | Same region flow → 24 fps capture → preview (FPS, scale, palette) → silent GIF. |
| **Clipboard** | Paste image and OCR; Edit menu copy image / file / path; menu/tray sets on-capture copy mode. |
| **Overlay text** | Click-release selects one OCR block; empty click clears; drag-select stays in range. Ctrl+C copies. |
| **PDF workbench** | Open PDF → page OCR → edit lines → export searchable PDF. |

### Speech, LLM, translation, face

| Area | Description |
|------|-------------|
| **ASR / TTS** | Live captions: offline or streaming. Sherpa / SAPI / WinRT offline TTS and Edge online natural voices. The Speech language list shows named languages first, then the remaining codes. Supertonic uses a fixed noise seed, so the same text sounds the same each time. |
| **LLM chat** | WeChat-style bubbles, Clear, mic, Speak / Auto speak, optional web + `tmp/llm/` tools. |
| **Translation** | Local Opus-MT ONNX or a configured **LLM**; 20-trip round-trip stops early on a repeat; popup `Ctrl+Alt+T`. |
| **Dictionary** | A main-window tab (**Options → Dictionary**). The hotkey is off by default; set `hotkey_dict` to show the main window and select that tab (it enables the tab if it was hidden) and to hide the main window when that tab is already showing. Looks up Chinese, English, Japanese, and Korean headwords in `dict.db` beside the program. Filter: all, Chinese, English, Japanese, or Korean. The hit list is two lines (language and headword, then the gloss) with a speak button. The entry uses smaller type and colors pronunciation, part of speech, sense numbers, language labels, and examples. Select text to open a popup beside the selection, with matching headwords plus Speak, Search, Translate, and Copy. Each example has a speak button. **Speak** and **Read Chinese** use Settings → Dictionary: per language, engine Auto / ONNX / SAPI / Windows speech / Edge, that engine's voice, and a rate from 0.5 to 2. Auto tries Windows speech, then SAPI, then Edge. ONNX is chosen explicitly. Spoken audio is cached in `tmp/voice` and kept for 1 day. This does not change the Speech tab. The read-only SQLite connection closes after 5 minutes without a lookup and opens again on the next search. The hotkey shows the main window and reads the clipboard. It searches only when that text is one word: 1–4 Chinese characters, 1–20 English letters, Japanese with kana up to 12 characters, or 1–8 Hangul syllables. A sentence, a blank clipboard, or anything else opens the dictionary and does not search. The database is not in the release archive. |
| **Face** | InsightFace ONNX detect/compare; optional landmarks and gender/age. Models in `facemodels/`. |
| **SAPI x86 helper** | Sidecar `x86host.exe` for classic voices visible only to 32-bit processes. |

### Transfer, API, setup

| Area | Description |
|------|-------------|
| **PC file transfer** | Same HTTP API port (`1224`) + UDP discovery `17531`. **File sync** tab lists the `sendfile/` inbox (no subfolders): **list / thumbnails** toggle, marquee and Shift/Ctrl multi-select, **Ctrl+X/C** cut/copy (cut items shown faded), paste, delete to Recycle Bin, drag to Explorer. Drop on the lower zone to send to the phone while connected; phone shares land in `sendfile/`. **Install on phone** checks and downloads the latest APK, then shows a LAN URL and QR. **Web manager** (same port): desktop `/`, phone `/m` (compact list, long-press multi-select, Upload/Camera/New); login to upload/manage; download only needs `/f/…`. Companion: `android/` (`com.whj.screenkit`, launchers **File transfer** / **Screencast**). |
| **HTTP API** | JSON API (default port `1224`). **Allow LAN access** opens every interface; otherwise it listens on `127.0.0.1` only. Main-window tab: call log + request builder. |
| **Install features** | Feature tree (installed items checked); add (green) / remove (red); Confirm installs/uninstalls. Voices on a separate tab. CN mirrors when locale is Chinese. |
| **Image convert** | **Tools → Image convert**: batch JPG/PNG/BMP, optional shorter-side cap (shrink only), rotate/flip; icon view with small thumbnails or a details list; live preview of the selected file after those settings; write to `output/` next to each source, a chosen folder, or replace the source (permanently delete, or Recycle Bin). Optional: keep the original file when the result is still ≥ N% of the original size (default 80%; rotate/resize still writes the new file). |
| **QR / barcode** | **Tools → QR / barcode**: QR, Data Matrix, Code 128/39, EAN, UPC. One line of original text under the image. UTF-8, GBK, or Hex bytes (default UTF-8). |
| **Batch rename** | **Tools → Batch rename**: source list (add/remove/move, sort by name / modified / added); new names in a textarea. Everything / FastCopy patterns (`%1`/`%2` shortest capture, `#` / `###`); changing the list or rules recalculates names. |
| **Hash** | **Tools → Hash**: MD5 / SHA-1 / SHA-256; paste an expected hash to match. |
| **Text tools** | **Tools → Text tools**: Base64, URL, UTF-8/GBK hex, Unicode escape, case, whitespace, counts, smart JSON pretty-print (short arrays/objects stay on one line). |
| **Password generator** | **Tools → Password generator**: crypto-random; length, sets, skip `0OIl1`, at least one of each class. Remembers last settings. **Word lex** tab: pick an LLM, then translations (including Literary Chinese, Ancient Greek, Latin, Sanskrit, Biblical Hebrew) plus pinyin / romaji / romanization. **Variants** tab: from a seed password, the LLM mostly translates into other languages and spells them in ASCII romanization (at most one English paraphrase; no extra symbols, digits, leetspeak, or word-reordering). |
| **Network tools** | **Tools → Network tools**: Ping, DNS, WHOIS, traceroute, ping-locate, proxy-locate, HTTP speed-locate. |
| **Window manager** | **Tools → Window manager**: list visible top-level windows, pick one by clicking, or enter an HWND. A green frame follows the window under the cursor while picking, and stays on the selected window. Pin in front or unpin (`HWND_TOPMOST`). |
| **Screencast** | Main window **Screencast** tab: cast this PC to another. Saved PCs stay in the left list; click one to fill the IP, or type an address / **Scan LAN**. **Max size** defaults to 1000×1000 (shrink to fit, never enlarge). **Start cast** first picks a region the same way as a screenshot (click a window or drag). Sending waits until Start is pressed again, so audio and its source (speakers, microphone, or both) can be changed. The red frame and toolbar can start, pause, or stop; pause keeps the connection and stops picture and audio until resume. The frame is not part of the picture. **Tools → Screencast** (same under the tray) still receives a phone or another PC, and can cast the whole desktop (quality 540p/720p/1080p). Audio plays continuously; the picture waits only as long as the sound is behind. The viewer is not always-on-top; picture can **Fit** or **Fill** the window. Rotating the phone keeps the viewer’s size, position, and maximized state; the picture fits or fills inside that window. Discovery shares UDP 17531 with file transfer; video uses WebSocket `HTTP /cast` (default 1224). **Connect Android USB accessory** (tray, file-sync tab, screencast window) opens “Waiting for Android USB accessory” and tells a phone already connected for file sync to enter accessory mode. Both sides show connected / waiting / not connected. It turns off after 2 minutes with no connection. Or **USB cast (adb)** with USB debugging. The phone app has a separate **Screencast** launcher icon. |
| **Hotkeys** | Toggle window · snap annotate · snap OCR · voice input · translate popup (configurable). |
| **Main tabs** | Settings → General can hide OCR / TTS / ASR / Chat / Translate / Face / HTTP / File sync / Screencast (`tab_*_visible`). |
| **Devices** | CPU · NVIDIA CUDA · Intel DirectML; missing accel → CPU. |
| **CLI** | Batch OCR, list models / SAPI voices, probe CUDA, multi-monitor snap test. |

## Requirements

- Windows 10/11 (x64)
- [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48)
- Build: Visual Studio / MSBuild with a `net48` WPF targeting pack
- Optional: NVIDIA GPU + CUDA matching `onnxgpu64`; DirectML GPU for `onnxdml64`
- Optional (x264 / x265 / AV1 and GIF): FFmpeg **4.4 shared** under `ffmpeg64/` next to the exe. Media Foundation H.264 needs no download.

## Using ScreenKit

### Screenshot, annotate, OCR

Capture a region (hotkey or menu). Result panel splits **OCR / Barcode**. Overlay translation uses a configured LLM when a dest language is selected. Annotate tools sit on the capture overlay; a region that spans monitors can be drawn on, moved, or resized on every screen it covers (move hot-zone is 10px outside the frame). The toolbar can open the shot with the default app. The Done dropdown copies as image, file, or path and remembers the default. **Settings → Capture** can cap the shorter side: if the smaller of width and height is over the limit, the saved file shrinks to fit and is never enlarged. That applies to `screenshots/` and copy-as-file; OCR still uses the full image.

### Screen / GIF recording

1. **Capture → Screen record** (or GIF record): click a window or drag a region.
2. **HUD** (drawn outside the capture area):
   - Red frame; drag the **5px strip** to move, or **8 grips** to resize. Aspect is free before **Start** and locked afterwards unless `record_lock_aspect = false`.
   - Floating **control bar**: left grip to drag; collapse; **Options** before Start; start/pause share one slot. Stays on the current monitor.
3. Stop → save MP4 (Explorer selects the file) or open the GIF preview (output FPS 1–24, scale, palette) then save a silent GIF.
4. **Capture → Record options**: codec (x264 / x265 / AV1 / Media Foundation H.264), FPS, CRF (x264/x265 only) or AV1 CRF (0–63, default 56; hidden for the other codecs), audio, **shorter-side cap** (shrink only, same rule as screenshots), **record mouse** / **highlight clicks**. AV1 needs libsvtav1 / libaom-av1 in `ffmpeg64` and fails clearly with no fallback to x264. Media Foundation does not use FFmpeg. System H.264 writes AAC during capture at 44.1 kHz or 48 kHz, and does not mix a second file when saving. A saved `record_codec` of `mjpeg` is read as system H.264.

GDI capture has no cursor: enable **record mouse** to overlay the pointer; **highlight clicks** draws a short yellow (left) / blue (right) / green (middle) ripple. GIF size grows quickly — use preview scale/FPS and the max-size limit. GIF still uses a max width and height box.

### File sync (PC ↔ Android)

1. On the PC, open the **File sync** tab (enable under Settings → API if needed; default on).
2. **Install on phone**: check and download the latest APK from GitHub, then show LAN URL `http://<pc-ip>:1224/apk` and a QR code (defaults to an internet-reachable NIC). Phone and PC on the same LAN; scan with a browser to install. The Windows release package does not include the APK.
3. First connection: the phone shows a waiting dialog (cancellable) until the PC allows pairing. The PC dialog stays always-on-top (also when the main window is in the tray).
4. The upper list is the PC `sendfile/` inbox (root only, no subfolder navigation; folders are rows). **Double-click** a file to open it with the system, or a folder to open Explorer. The **right-click** menu acts on the current selection (Open, Cut, Copy, Paste, Push, Delete) and does not clear a multi-selection when you click an already selected row. Use the toolbar to switch **list / thumbnails**; marquee, Shift range, and Ctrl multi-select; **Ctrl+X/C/V** cut/copy/paste (cut rows appear semi-transparent), Delete to Recycle Bin, or drag to Explorer. Dropping files onto the list imports them. Drop on the **lower** zone to send to the phone while the app is connected; otherwise nothing is sent. File/image paste goes into the inbox; text paste still goes to the right-hand pane. Phone share/upload always writes to PC `sendfile/`.
5. Both sides show in-progress transfers; the PC also has a transfer log. Text sync is one text box with **Send**, **Copy**, and **Clear**. The phone auto-connects by LAN IP only, not over USB.
6. **Web manager** (same HTTP port): **File sync → Web manager** shows the LAN URL, QR, and login password. Desktop page `/`, phone page `/m`. The page follows the browser language and has a **Chinese / EN** switch (saved in the browser). Sign in to upload, mkdir, rename, delete; optional **Stay signed in**. Phone UI: breadcrumb path, compact list (tap to open/download, long-press to multi-select), bottom **Upload / Camera / New**. **Camera** compresses in the browser before upload, using **Settings → API** (format, JPG quality, shorter-side cap). If the browser cannot compress, the PC applies the same settings. Reopen the page after updating the PC app, then take the photo. Zip/copy/rename/delete from the selection bar. A correct `/f/<relative-path>` URL downloads without login. Design reference: `ScreenKit/SendFile/web/m-prototype.html`.

Android details: [android/README.md](android/README.md).

### Screencast (PC ↔ Android / PC ↔ PC)

1. PC **Tools → Screencast** (receive starts with the app; Settings → API can turn it off). Same Wi‑Fi, not a guest network. Launching this build ends leftover ScreenKit from another folder. Wi‑Fi/ADB media uses the same HTTP port as file transfer (`/cast`); scan uses UDP 17531.
2. Phone **File transfer / Screencast** → **Screencast**. The page fills the saved PC IP; confirm it and tap **Start cast**, then allow screen capture. Use **Saved PCs** or **Scan LAN** to pick another PC. **Stop cast** appears only while casting. USB cast is a separate section below. The phone sends `hello` and waits for the PC’s `hello` before capture; the PC viewer opens only after that reply. Wi‑Fi media uses HTTP `/cast` (default 1224). The file-transfer listener on a LAN address completes that WebSocket handshake as well. If the PC app is closed, the handshake fails, the PC disconnects, or screen capture is denied, the phone shows a **Cast failed** dialog. After ~90s without an accessory it falls back to USB tethering. **USB debugging cast** uses `adb reverse` and needs USB debugging. Debug forward: `adb shell am start -n com.whj.screenkit/.CastActivity --ez scst_adb_probe true`. WiFi hello: `--es scst_ip <PC LAN IP> --ez scst_wifi_probe true`. USB-only test pattern: `scst_usb_pat`.
3. A view window opens in front, centered on the primary monitor. **Tab** overlay (type / resolution / fps) is off until you press Tab. Closing the window disconnects. Quality changes apply live. Landscape re-encodes at the same short-edge quality (does not crop a portrait frame). Stopping on the phone closes the PC window immediately (`GET /api/cast/stop`).
4. PC-to-PC: the receiver keeps this app open (receive is on by default). On the sender, open the **Screencast** tab, click a saved PC to fill the IP (or scan the LAN, which includes this PC), set max width and height, tap **Start cast**, then click a window or drag a region. **Tools → Screencast** can still scan and cast the whole desktop using a quality preset.

### Image convert

**Tools → Image convert**: drop files, folders, or images; choose JPG / PNG / BMP; optional shorter-side cap (if the smaller side is over the limit, shrink to fit; never upscale); rotate 90/180/270° or flip the selected item. **Icons** shows a small thumbnail per file (**List** for details). The right pane previews the selected file after those settings. Convert-all writes next to each source under `output/`, into a chosen folder, or replaces the source (permanently delete, or Recycle Bin; convert-all asks first).

### Default hotkeys

| Hotkey | Action |
|--------|--------|
| `Ctrl+Alt+O` | Toggle main window |
| `Ctrl+Alt+Q` | Screenshot annotate |
| `Ctrl+Alt+W` | Screenshot and OCR |
| `Ctrl+Alt+V` | Voice input (press again to stop) |
| `Ctrl+Alt+B` | Live caption |
| `Ctrl+Alt+T` | Translate popup |
| `Ctrl+Alt+P` | Cycle on-capture copy (image → file → path) |

Leave a hotkey string empty in Settings to disable it. The dictionary-tab hotkey is empty by default. Tray: left-click toggles the window; context menu has voice input, translate popup, clipboard OCR, on-capture copy mode, and exit. Closing the main window typically **hides** to tray. Switching the copy mode from the menu, tray, or `Ctrl+Alt+P` recopies the last screenshot and shows a toast at the bottom center of the screen. After a region is selected, the annotate bar shows the copy mode as a combo (for example “Copy as image p”). Choosing an item finishes the shot with that mode and remembers it. `P` on the bar cycles image / file / path and does not finish.

### UI language

**Options → Language** → Chinese / English, or Settings → General (`ui_lang = "zh"` / `"en"`). Covers menus, Settings, OCR toolbar Pack/Lang names (`ocr-display.json` `nameEn`), Translate, Face, the translate popup, and the dictionary window.

## Install features

1. First launch may open the wizard (nothing selected). System H.264 stays checked and needs no download.
2. Later: **Help → Install features**
   - **Features**: installed items are checked; add (green) / remove (red) counts and sizes; Reset restores. Confirm installs and uninstalls. Voices are not on this tab.
   - **Voices**: TTS models with language filter; progress shows **total batch size and downloaded bytes**. `.tar.bz2` packages are extracted in-process (no system `tar` / `bzip2`); a junction-based `ttsmodels` directory is supported. Japanese is Supertonic 3 (`sherpa-onnx-supertonic-3-tts-int8-2026-05-11`). Choose Japanese in the language list. That pack also speaks the other Supertonic 3 languages.
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

Settings live in `config.toml` beside the exe (**Options → Settings** / **Record options**). Tabs: General, OCR, Hotkeys, Speech, LLM, Translate, Capture, API. Main-tab visibility checkboxes are under **General**. **Options → Memory** shows process memory, unloads a loaded OCR, translation, face, or speech model, and can uninstall the ONNX CPU / CUDA / DirectML runtime. Unload forces a full GC. The status bar shows loaded engines and memory; click it to open that window.

| Section | Keys |
|---------|------|
| `[ocr]` | pack, variant, device (`Cpu` / `Gpu` / `IntelGpu`), det thresholds |
| `[ui]` | hotkeys, tray, `ui_lang`, `tab_*_visible`, `update_check_days`, `http_proxy`, `capture_log`, `llm_log`, `screenshot_keep_days`, `screenshot_short`, `imgconv_*` |
| `[http]` | OCR API (`http_lan`, port `1224`), service mode, `onnx_unload_min` |
| `[sendfile]` | LAN file transfer ports, display name, web login password, paired devices |
| `[pdf]` | invisible text, raster DPI |
| `[asr]` | voice/live mode, polish, split, `asr_llm` |
| `[[llm]]` | OpenAI-compatible endpoints (`name` / `url` / `key` / `model` / `think`) |
| `[translate]` | local ONNX device, `translate_llm`, `translate_llm_batch` |
| `[record]` / `[gif_record]` | codec, CRF, audio, mouse overlay |

Do **not** commit a machine-specific `config.toml`.

<details>
<summary>Example <code>config.toml</code></summary>

```toml
[ocr]
model_pack = "rapid-ch"
model_variant = "mobile"          # pack variant id; a Chinese pack may store a Chinese label
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
# hotkey_dict = ""                # dictionary tab; empty = off. Clipboard word only. Example: "Ctrl+Alt+D"
# dict_tts_zh_engine = "auto"     # auto | onnx | sapi | winrt | edge; same keys for en, ja, ko
# dict_tts_zh_rate = 1.0          # 0.5–2; audio cache tmp/voice, kept 1 day
# dict_tts_zh_sapi = ""           # local SAPI voice; empty = automatic
# dict_tts_zh_edge = "zh-CN-XiaoxiaoNeural"
# dict_tts_en_edge = "en-US-AriaNeural"
# dict_tts_ja_edge = "ja-JP-NanamiNeural"
# dict_tts_ko_edge = "ko-KR-SunHiNeural"
# hotkey_snap_copy = "Ctrl+Alt+P" # cycle copy as image / file / path
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
tab_dict_visible = true
update_check_days = 7           # auto-check interval on startup (days); 0 = off
screenshot_keep_days = 3        # screenshot history; 0 = unlimited. Cleaned at startup only
screenshot_short = false        # cap the shorter side (shrink only)
screenshot_short_px = 1080      # 16–16384
# http_proxy = false
# http_proxy_addr = "127.0.0.1:7897"
# ocr_translate_lang = ""       # empty = off; LLM lang code (zh/en/ja/ko/…)

[http]
http_enabled = true
http_lan = true                 # false listens on 127.0.0.1 only
http_port = 1224
service_mode = false            # keep engines warm; skips idle unload
onnx_unload_min = 5             # unload idle ONNX sessions after N minutes; 0 = never

[sendfile]
sendfile_enabled = true
# sendfile HTTP shares http_port (1224)
sendfile_udp_port = 17531
sendfile_name = ""              # empty = machine name
sendfile_web_pass = ""          # web manager login; empty = auto-generated on start
photo_fmt = "jpg"               # phone app + web Camera: jpg | png
photo_jpg_quality = 60          # 1–100
photo_limit = true              # cap the shorter side (shrink only)
photo_max_px = 2000             # 64–16000
# [[sendfile_device]]           # paired phones (id / name / token)

[pdf]
pdf_invisible_text = true
pdf_dpi = 150

[record]
record_codec = "x264"           # x264 | x265 | av1 | mf (mjpeg is read as mf)
record_fps = 24
record_crf = 28                 # x264/x265 only, 0–51
record_av1_crf = 56             # AV1 only, 0–63 (56 ≈ half of x265 CRF28 size)
record_audio = true
record_audio_src = "Speakers"   # Speakers | Mic | MicAndSpeakers
record_audio_kbps = 96
record_short = false            # cap the shorter side (shrink only)
record_short_px = 1080          # 16–16384
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
think = "low"                   # preset or typed token (minimal, …)

[translate]
translate_compute = "Auto"      # Auto | Gpu | Cpu | Igpu
# translate_llm = ""
# translate_llm_prompt = "Translate the user's text to {dst}. Output only the translation."
translate_llm_batch = 8         # items per LLM call, 1–64

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

`think`: `off` sends `thinking.type=disabled`. Any other token (`low` / `medium` / `high` / `max`, or a typed value such as `minimal`) sends `thinking.type=enabled` plus `reasoning_effort`. If `off` is rejected, retry with `low`. Access to **opencode.ai** adds `x-opencode-session` / `x-opencode-client`. Old keys `asr_llm_url` / `asr_llm_token` / `asr_llm_model` are ignored. `translate_llm_batch` is how many lines one LLM translate call takes (default 8).

Set `capture_log = true` for `log/capture.log` (DPI / save timings; `SLOW` if ≥500ms). Set `llm_log = true` for `log/llm.log` (API keys are not written). CLI `ScreenKit --snap` dumps full-monitor bitmaps under `log/snap/`; `--test-overlay-layout` shows the screenshot overlay and logs per-monitor HWND/DPI.

## HTTP API (overview)

When enabled, the OCR API listens on `http_port` (default `1224`). `http_lan = true` (default) accepts this PC and the LAN; `false` listens on `127.0.0.1` only. File transfer, the web manager, and Wi-Fi cast share this port and need LAN access left on. There is no authentication — do not expose it on an untrusted network.

LAN **PC file transfer** shares this HTTP port (`1224`) plus UDP `17531` (pairing required): [HTTP-API.md](HTTP-API.md) · [android/README.md](android/README.md).

- `GET  /api` · `/api/status`
- `GET/POST /api/cast/stop` — close screencast viewer now
- WebSocket `/cast` — screencast media (same HTTP port)
- `POST /api/ocr` — `box` is original-image pixels
- `POST /api/qr` — barcode / QR only (`/api/barcode`)
- `GET  /api/ocr/get_options`
- `GET  /api/asr/models` · `POST /api/asr`
- `GET  /api/tts/models` · `POST /api/tts` — Sherpa, SAPI, Windows (`engine=winrt`), Edge (`engine=edge`)
- `POST /api/itn`
- `POST /api/translate` — LLM batch (`items[]`)
- `GET  /api/face/models` · `POST /api/face`

Full field reference: **[HTTP-API.md](HTTP-API.md)** · **[Chinese HTTP API](HTTP%E6%8E%A5%E5%8F%A3%E6%96%87%E6%A1%A3.md)**.

## CLI

```text
ScreenKit --image <path> [options]
ScreenKit --snap [--out <dir>]
ScreenKit --test-overlay-layout   # screenshot overlay HWND/DPI per monitor
ScreenKit --test-overlay-span-adj # cross-monitor region handles on guest screens
ScreenKit --test-clipboard-path   # path copy after delayed image; 4K timing
ScreenKit --test-apk-qr            # encode/decode LAN APK QR; HTTP GET /apk
ScreenKit --test-img-convert       # png→jpg rotate 90 + box 100×100 + shorter side 40
ScreenKit --test-qr-make
ScreenKit --test-rename
ScreenKit --test-hash
ScreenKit --test-texttool
ScreenKit --test-pwgen
ScreenKit --test-nettool
ScreenKit --test-wintop           # list windows; pin/unpin a probe HWND
ScreenKit --test-dict-search      # read-only lookup in dict.db beside the exe
ScreenKit --test-dict-sel         # copy a selected word with Ctrl+C and read it back
ScreenKit --test-dict-word        # clipboard text is one dictionary word?
ScreenKit --test-dict-tts         # dictionary voice cache kept 1 day; rate math
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
└── HTTP-API.md · Chinese HTTP API doc
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
- Does **not** include: the Android APK, OCR/ASR/TTS/face models, ORT, OpenCV/Skia/PDFium/Sherpa natives, `ffmpeg64`, or `dict.db`. Place `dict.db` beside the executable yourself. The Windows SQLite library `e_sqlite3.dll` is included.
- End users install those via **Install features**. Local Opus-MT ONNX is placed under `translatemodels/` by hand if needed.

For local development with models already present, run **`bin\Release\net48\`**.

### Release archive

```bash
node scripts/publish-release.mjs
```

Release-builds, then packs `ScreenKit/bin/Release/ScreenKit/` into `release/screenkit_<version>.7z` (needs [7-Zip](https://www.7-zip.org/) on PATH). `release/` is gitignored.

Release documentation:

- Every `CHANGELOG.md` version has matching **English** and **Chinese** sections.
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
- [HTTP-API.md](HTTP-API.md) · [Chinese HTTP API](HTTP%E6%8E%A5%E5%8F%A3%E6%96%87%E6%A1%A3.md)
- [android/README.md](android/README.md) — companion app
- [LICENSE](LICENSE) · [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
- [README.zh.md](README.zh.md)
