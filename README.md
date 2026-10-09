# ScreenKit

Windows desktop tool (`ScreenKit.exe`): screenshot, annotate, OCR, barcode/QR, long screenshot, screen/GIF recording, PDF workbench, ASR/TTS, LLM chat, translation, face, local HTTP API, LAN file transfer, and LAN/USB screencast with an Android companion.

**Current version: 1.0.16** · [GitHub Releases](https://github.com/cfwang123/ScreenKit/releases/latest)

Changelog: [CHANGELOG.md](CHANGELOG.md) (English and Chinese per version).

**Languages:** [English](README.md) · [Chinese](README.zh.md)

## Contents

1. [Screenshots](#screenshots)
2. [Features](#features)
3. [Dictionary](#dictionary)
4. [Requirements](#requirements)
5. [Using ScreenKit](#using-screenkit)
6. [Install features](#install-features)
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
| **Screenshot OCR** | Region capture → text OCR or barcode/QR; multi-monitor DXGI. The model list includes especially fast **Windows OCR** (the default when none is saved; no model download and no OpenCV; one language, shown only for that engine; the side-length cap applies to both) and ONNX packs. ONNX OCR, barcodes, and long screenshots still ask for OpenCV. Settings → Recognition describes the selected engine directly below the model picker. Korean/English word spaces restored from visual gaps (no spaces between CJK). Optional overlay translation via LLM. The image bar is a compact WPF toolbar; items that do not fit move to the overflow menu. **History** lists `screenshots/` as thumbnails, previews with pan and zoom, and annotates under that preview. With marks, Done, OCR, or Open saves a new screenshot and copies it. With no marks they use the original file and do not write a new one. Done, OCR, and Open then close the history window and show the picture on the main window; OCR also recognizes it. Copy and Ctrl+C put the current picture on the clipboard and leave the window open. |
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
| **ASR / TTS** | ASR supports Sherpa offline/streaming models and installed **Windows system recognizers** (`System.Speech`, offline utterance recognition; the default offline model when none is saved) for files, recordings, hotkey dictation, live captions, subtitle batches, CLI, and HTTP. The help icon next to the offline-model picker notes that Windows ASR performs poorly for Chinese and recommends SenseVoice. TTS supports Sherpa / SAPI / WinRT offline voices and Edge online natural voices. Speak remains available during playback; clicking it again stops the current playback and restarts from the beginning. |
| **LLM chat** | WeChat-style bubbles, Clear, mic, Speak / Auto speak, optional web + `tmp/llm/` tools. |
| **LLM log** | Main-window list of the latest 1,000 LLM calls: model, status, input / output / total tokens, and response time. Select a row to read the request and response. Chinese in that JSON is shown as characters. The address query string is not stored. |
| **Translation** | Local Opus-MT ONNX or a configured **LLM**; 20-trip round-trip stops early on a repeat; **Tools → Translate popup** (`Ctrl+Alt+T`). |
| **Dictionary** | Chinese, English, Japanese, and Korean headwords on a main-window tab. See [Dictionary](#dictionary). Menu **Tools → Dictionary**. `dict.db` is not in the release archive. **Help → Install features** can download it. |
| **Face** | InsightFace ONNX detect/compare; optional landmarks and gender/age. Models in `facemodels/`. |
| **SAPI x86 helper** | Sidecar `x86host.exe` for classic voices visible only to 32-bit processes. |

### Transfer, API, setup

| Area | Description |
|------|-------------|
| **PC file transfer** | Same HTTP API port (`1224`) + UDP discovery `17531`. **File sync** tab lists the `sendfile/` inbox (no subfolders): **list / thumbnails** toggle, marquee and Shift/Ctrl multi-select, **Ctrl+X/C** cut/copy (cut items shown faded), paste, delete to Recycle Bin, drag to Explorer. Drop on the lower zone to send to the phone while connected; phone shares land in `sendfile/`. **Install on phone** checks and downloads the latest APK, then shows a LAN URL and QR. **Web manager** (same port): desktop `/files`, phone `/m` (compact list, long-press multi-select, Upload/Camera/New/Text); login to upload/manage; download only needs `/f/…`. Companion: `android/` (`com.whj.screenkit`, launchers **File transfer** / **Screencast**). |
| **HTTP API** | JSON API (default port `1224`). **Allow LAN access** opens every interface; otherwise it listens on `127.0.0.1` only. Main-window tab: call log + request builder. **Tools → Web home** and the tray **Web home** open `http://127.0.0.1:port/`, the local tools page (simplified/traditional, calendars, Japanese yomi, text, OCR, speech synthesis, speech recognition, QR and barcodes). OCR and speech can pick an engine and a model. The selected template shows what that endpoint does and which parameters it takes. Styles, scripts, and images are cached for 1 hour. HTML pages are not cached. Restarting ScreenKit changes the version on those style and script URLs, so the browser loads the new files. The bottom status bar shows `HTTP 0.0.0.0:1224`, `HTTP 127.0.0.1:1224`, `HTTP LAN:1224`, `HTTP not started`, or `HTTP off`. Click that HTTP text to open the web home; click the model summary to open Memory; the empty space to the right does nothing. A failed start is retried every 10 seconds until the listener is up, or until the API, file transfer, and cast are all off. |
| **Install features** | Feature tree (installed items checked); add (green) / remove (red); Confirm installs/uninstalls. ONNX speech models have their own tab. Windows OCR, speech synthesis, and speech recognition share one language tree. CN mirrors when locale is Chinese. |
| **Image convert** | **Tools → Image convert**: batch JPG/PNG/BMP, optional shorter-side cap (shrink only), rotate/flip; icon view with small thumbnails or a details list; live preview of the selected file after those settings; write to `output/` next to each source, a chosen folder, or replace the source (permanently delete, or Recycle Bin). Optional: keep the original file when the result is still ≥ N% of the original size (default 80%; rotate/resize still writes the new file). |
| **QR / barcode** | **Tools → QR / barcode**: QR, Data Matrix, Code 128/39, EAN, UPC. One line of original text under the image. UTF-8, GBK, or Hex bytes (default UTF-8). |
| **Batch rename** | **Tools → Batch rename**: source list (add/remove/move, sort by name / modified / added); new names in a textarea. Everything / FastCopy patterns (`%1`/`%2` shortest capture, `#` / `###`); changing the list or rules recalculates names. |
| **Hash** | **Tools → Hash**: MD5 / SHA-1 / SHA-256; paste an expected hash to match. |
| **Text tools** | **Tools → Text tools**: Base64, URL, UTF-8/GBK hex, Unicode escape, case, whitespace, counts, smart JSON pretty-print (short arrays/objects stay on one line). |
| **Password generator** | **Tools → Password generator**: crypto-random; length, sets, skip `0OIl1`, at least one of each class. Remembers last settings. **Word lex** tab: pick an LLM, then translations (including Literary Chinese, Ancient Greek, Latin, Sanskrit, Biblical Hebrew) plus pinyin / romaji / romanization. **Variants** tab: from a seed password, the LLM mostly translates into other languages and spells them in ASCII romanization (at most one English paraphrase; no extra symbols, digits, leetspeak, or word-reordering). |
| **Network tools** | **Tools → Network tools**: Ping, DNS, WHOIS, traceroute, ping-locate, proxy-locate, HTTP speed-locate, system location (Windows geolocation over Wi-Fi or GPS; the system asks for permission the first time). **Map** opens Amap in the browser (GCJ-02 inside China) and the result includes an OpenStreetMap link (WGS84). |
| **Simplified / Traditional** | **Tools → Simplified/Traditional**: simplified on top, traditional below, two buttons convert either way. `LCMapStringEx`; traditional Chinese follows the `zh-TW` table (Taiwan character forms), not the Hong Kong table, and it does not rewrite whole words. |
| **Calendars** | **Tools → Calendars**: `Windows.Globalization.Calendar`. Gregorian to Chinese lunar (sexagenary year, leap month), plus Japanese, Taiwan, Hebrew, Hijri, Persian, and others. |
| **Japanese yomi** | **Tools → Japanese yomi**: `JapanesePhoneticAnalyzer` turns a Japanese sentence into readings (by word or by character). The Japanese language feature must be installed. Windows has no equivalent Chinese pinyin API. |
| **Window manager** | **Tools → Window manager**: list visible top-level windows, pick one by clicking, or enter an HWND. A green frame follows the window under the cursor while picking, and stays on the selected window. Pin in front or unpin (`HWND_TOPMOST`). |
| **Screencast** | Main window **Screencast** tab: cast this PC to another. Saved PCs stay in the left list; click one to fill the IP, or type an address / **Scan LAN**. **Max size** defaults to 1000×1000 (shrink to fit, never enlarge). **Start cast** first picks a region the same way as a screenshot (click a window or drag). Sending waits until Start is pressed again, so audio and its source (speakers, microphone, or both) can be changed. The red frame and toolbar can start, pause, or stop; pause keeps the connection and stops picture and audio until resume. The frame is not part of the picture. **tray → Tools → Screencast** still receives a phone or another PC, and can cast the whole desktop (quality 540p/720p/1080p). Audio plays continuously; the picture waits only as long as the sound is behind. The viewer is not always-on-top; picture can **Fit** or **Fill** the window. Rotating the phone keeps the viewer’s size, position, and maximized state; the picture fits or fills inside that window. Discovery shares UDP 17531 with file transfer; video uses WebSocket `HTTP /cast` (default 1224). **Connect Android USB accessory** (tray, file-sync tab, screencast window) opens “Waiting for Android USB accessory” and tells a phone already connected for file sync to enter accessory mode. Both sides show connected / waiting / not connected. It turns off after 2 minutes with no connection. Or **USB cast (adb)** with USB debugging. The phone app has a separate **Screencast** launcher icon. |
| **Hotkeys** | Toggle window · snap annotate · snap OCR · voice input · translate popup (configurable). |
| **Modules** | `mod_*` in `config.toml` (default on) turns OCR, TTS, ASR, chat, translate, face, and dictionary on or off. Settings → General does not list these switches. Off hides that page and stops its hotkey and tray entry. Settings → API turns each HTTP path on or off (`http_ocr`, `http_tts`, `http_asr`, `http_translate`, `http_chat`, `http_face`). Off returns code 810. |
| **Main tabs** | Settings → General can hide OCR / TTS / ASR / Chat / LLM log / Translate / Face / HTTP / File sync / Screencast / Dictionary (`tab_*_visible`). Hiding a tab does not stop its hotkey. |
| **Diagnostics** | **Help → Diagnostics**. A ready check is green. An optional item that is off (service mode, CUDA / DirectML not installed, ONNX not loaded, no face models, OCR engine not loaded) shows as not enabled. A missing file or a failed check is red. **Help → Memory** shows process memory and unloads loaded models; click the model summary on the status bar to open the same window. |
| **Devices** | CPU · NVIDIA CUDA · Intel DirectML; missing accel → CPU. |
| **CLI** | Batch OCR, list models / SAPI voices, probe CUDA, multi-monitor snap test. |

## Dictionary

Look up Chinese, English, Japanese, and Korean. The release archive does not include the dictionary file. **Help → Install features** downloads it next to the program.

### Using it

- Language buttons: All, Chinese, Japanese, Korean, English.
- Each hit shows the language, the headword, and the gloss, and can be spoken. The entry shows pronunciation, part of speech, senses, phrases, and examples. Each example can be spoken. A Korean phrase has the same speak button after the Korean text and reads that text.
- Double-click, drag, or right-click text in an entry to open a popup of matching headwords. Hover and selection keep dark text on a light blue row.
- **Settings → Dictionary** sets, for each language, an TTS engine (Auto / ONNX / SAPI / Windows speech / Edge), a voice, and a rate from 0.5 to 2. Auto uses that language's Windows speech, or Edge online when none is installed.

### Data sources

`dict.db` holds four dictionaries in one file. ScreenKit does not build the file.

- **Chinese.** Characters, words, and idioms from [mapull/chinese-dictionary](https://github.com/mapull/chinese-dictionary) (a pinyin dictionary). License: MIT. The upstream project notes that the origin of some material is unclear.
- **Japanese.** Vocabulary senses from the [JMdict/EDICT Dictionary Project](https://www.edrdg.org/wiki/index.php/JMdict-EDICT_Dictionary_Project) (EDRDG). License: [CC BY-SA 4.0](https://www.edrdg.org/edrdg/license.html). Redistribution has to keep the EDRDG attribution and follow that project's update terms.
- **Korean.** A derivative of the National Institute of Korean Language dictionary. License: CC BY-SA 2.0 KR.

## Requirements

- Windows 10/11 (x64)
- [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48)
- Build: Visual Studio / MSBuild with a `net48` WPF targeting pack
- Optional: NVIDIA GPU + CUDA matching `onnxgpu64`; DirectML GPU for `onnxdml64`
- Optional (x264 / x265 / AV1 and GIF): FFmpeg **4.4 shared** under `ffmpeg64/` next to the exe, or **Help → Install features** (`ffmpeg.7z` from `dict-db`, ~33 MB). Media Foundation H.264 needs no download.

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
5. Both sides show in-progress transfers; the PC also has a transfer log. The phone saves the original file name, including Chinese, spaces, and brackets. Text sync is one text box with **Send**, **Copy**, and **Clear**. The phone auto-connects by LAN IP only, not over USB.
6. **Web manager** (same HTTP port): **File sync → Web manager** shows the LAN URL, QR, and login password. Desktop page `/files`, phone page `/m`. `/index.html` redirects to `/files`. The page follows the browser language and has a **Chinese / EN** switch (saved in the browser). Sign in to upload, mkdir, rename, delete; optional **Stay signed in** (about 30 days, still signed in after this program restarts). Phone UI: breadcrumb path, compact list (tap to open/download, long-press to multi-select), bottom **Upload / Camera / New / Text**. The desktop toolbar has **Text**. The dialog shows the current text on the PC File sync page. **Clear** empties it there. **Send to PC** writes the edited text back, copies it to the clipboard, and shows a toast `Copied text: …`. **Camera** compresses in the browser before upload, using **Settings → API** (format, JPG quality, shorter-side cap). If the browser cannot compress, the PC applies the same settings. Reopen the page after updating the PC app, then take the photo. Zip/copy/rename/delete from the selection bar. A correct `/f/<relative-path>` URL downloads without login. Design reference: `ScreenKit/SendFile/web/m-prototype.html`.

Android details: [android/README.md](android/README.md).

### Screencast (PC ↔ Android / PC ↔ PC)

1. PC **tray → Tools → Screencast** (receive starts with the app; Settings → API can turn it off). Same Wi‑Fi, not a guest network. Launching this build ends leftover ScreenKit from another folder. Wi‑Fi/ADB media uses the same HTTP port as file transfer (`/cast`); scan uses UDP 17531.
2. Phone **File transfer / Screencast** → **Screencast**. The page fills the saved PC IP; confirm it and tap **Start cast**, then allow screen capture. Use **Saved PCs** or **Scan LAN** to pick another PC. **Stop cast** appears only while casting. USB cast is a separate section below. The phone sends `hello` and waits for the PC’s `hello` before capture; the PC viewer opens only after that reply. Wi‑Fi media uses HTTP `/cast` (default 1224). The file-transfer listener on a LAN address completes that WebSocket handshake as well. If the PC app is closed, the handshake fails, the PC disconnects, or screen capture is denied, the phone shows a **Cast failed** dialog. After ~90s without an accessory it falls back to USB tethering. **USB debugging cast** uses `adb reverse` and needs USB debugging. Debug forward: `adb shell am start -n com.whj.screenkit/.CastActivity --ez scst_adb_probe true`. WiFi hello: `--es scst_ip <PC LAN IP> --ez scst_wifi_probe true`. USB-only test pattern: `scst_usb_pat`.
3. A view window opens in front, centered on the primary monitor. **Tab** overlay (type / resolution / fps) is off until you press Tab. Closing the window disconnects. Quality changes apply live. Landscape re-encodes at the same short-edge quality (does not crop a portrait frame). Stopping on the phone closes the PC window immediately (`GET /api/cast/stop`).
4. PC-to-PC: the receiver keeps this app open (receive is on by default). On the sender, open the **Screencast** tab, click a saved PC to fill the IP (or scan the LAN, which includes this PC), set max width and height, tap **Start cast**, then click a window or drag a region. **tray → Tools → Screencast** can still scan and cast the whole desktop using a quality preset.

### Image convert

**Tools → Image convert**: drop files, folders, or images; choose JPG / PNG / BMP; optional shorter-side cap (if the smaller side is over the limit, shrink to fit; never upscale); rotate 90/180/270° or flip the selected item. **Icons** shows a small thumbnail per file (**List** for details). The right pane previews the selected file after those settings. Convert-all writes next to each source under `output/`, into a chosen folder, or replaces the source (permanently delete, or Recycle Bin; convert-all asks first).

### Default hotkeys

| Hotkey | Action |
|--------|--------|
| `Ctrl+Alt+O` | Show the main window on top when another window is active; hide it when the main window is active |
| `Ctrl+Alt+Q` | Screenshot annotate |
| `Ctrl+Alt+W` | Screenshot and OCR |
| `Ctrl+Alt+V` | Voice input (press again to stop) |
| `Ctrl+Alt+B` | Live caption |
| `Ctrl+Alt+T` | Translate popup |
| `Ctrl+Alt+P` | Cycle on-capture copy (image → file → path) |

Leave a hotkey string empty in Settings to disable it. The dictionary-tab hotkey is empty by default: if another window is active it shows the main window on top and selects the dictionary (and searches only when the clipboard is one word); if the main window is already active it hides the window. Tray left-click uses the same rule as the main-window hotkey (hide when the main window is in front, otherwise show and bring it to the front); context menu has voice input, translate popup, clipboard OCR, on-capture copy mode, and exit. Closing the main window typically **hides** to tray. Switching the copy mode from the menu, tray, or `Ctrl+Alt+P` recopies the last screenshot and shows a toast at the bottom center of the screen. After a region is selected, the annotate bar shows the copy mode as a combo (for example “Copy as image p”). Choosing an item finishes the shot with that mode and remembers it. `P` on the bar cycles image / file / path and does not finish.

### UI language

**Options → Language**, or Settings → General. Each language is a toml file in `lang/` next to the program (Chinese, English, Japanese, Korean, and other languages: German, Spanish, French, Portuguese, Russian; you can add more). Startup reads them all and lists Chinese, English, Japanese, Korean, then the other languages. `ui_lang` in `config.toml` is that file name. A missing phrase falls back to Chinese. Covers menus, Settings, OCR toolbar Pack/Lang names (`ocr-display.json` `nameEn`), Translate, Face, the translate popup, and the dictionary window.

### Updates

**Help → Check for updates** also runs at startup after the interval in Settings. When a newer release exists, a window lists every version after the current build up to the latest. Select a row to read that version’s notes. **中文 / English** switches the notes. **Update** downloads and installs the latest package. **Ignore** closes the window.

## Install features

1. Startup does not open the installer. Windows OCR is available immediately.
2. **Help → Install features**
   - **Features**: installed items are checked; add (green) / remove (red) counts and sizes; Reset restores. Confirm installs and uninstalls. Speech packs are not on this tab. Dictionary, Chinese↔English ONNX translation, and FFmpeg 4.4 shared download `dict.7z` / `translatemodels.7z` / `ffmpeg.7z` from the fixed `dict-db` release (not the app update package). Translation includes the two minimal Opus-MT runtime packs (~981 MB download, ~1.08 GiB extracted) and automatically selects CPU ONNX Runtime. Extracting either archive downloads `7za.dll` and `SharpSevenZip.dll` when they are not already installed. Opening the database needs `e_sqlite3.dll`.
   - **ONNX speech models**: TTS models with language filter; progress shows **total batch size and downloaded bytes**. `.tar.bz2` packages are extracted in-process (no system `tar` / `bzip2`); a junction-based `ttsmodels` directory is supported. Japanese is Supertonic 3 (`sherpa-onnx-supertonic-3-tts-int8-2026-05-11`). Choose Japanese in the language list. That pack also speaks the other Supertonic 3 languages.
   - **Windows OCR/Speech**: one language tree manages `Language.OCR`, `Language.TextToSpeech`, and `Language.Speech`. Languages are sorted Chinese, English, Japanese, Korean, then others. A language with an installed pack or currently available engine opens by default; other languages remain collapsed but can be opened for installation. Each language has fixed OCR, speech synthesis, and speech recognition children, with a tri-state language checkbox. The page shows WinRT OCR languages, voices, and `System.Speech` recognizers. The installed-pack check runs once after ScreenKit starts. Opening this window again uses that result. Refresh checks every pack again. Installing or removing rechecks only those packs, in the same administrator window when one is already required. Querying or changing a mixed selection uses one administrator window/UAC prompt. The copyable command box groups all install commands before all remove commands. Restart ScreenKit after changes. Windows OCR is especially fast; Windows recognition performs poorly for Chinese, so SenseVoice is recommended for Chinese ASR.
3. Using a feature that needs a missing package prompts to open the installer (e.g. OCR without any ORT → install `onnxcpu64`).

| Runtime | Role | Typical size |
|---------|------|----------------|
| **onnxcpu64** | CPU ONNX Runtime (required for OCR if no GPU/iGPU ORT) | ~16 MB |
| **onnxgpu64** | NVIDIA CUDA EP + CUDA/cuDNN (optional) | large |
| **onnxdml64** | DirectML EP for iGPU (optional) | ~18 MB |
| **OpenCV** | Capture / image pipeline (`OpenCvSharpExtern.dll`). The OpenCV video library is not included | ~61 MB |
| **ZXing** | `ZXing.dll`. Barcode scan and QR images. Asked for when you use them | ~3 MB |
| **SharpCompress** | `SharpCompress.dll`. Extracts `tar.bz2` speech and ASR packs | ~2.5 MB |
| **SharpSevenZip / 7za** | Extract dictionary and translation model `.7z` packages | ~1.8 MB + ~0.4 MB |
| **SQLite** | `e_sqlite3.dll`. Opens `dict.db` | ~1.7 MB |
| **ffmpeg64** | Screen record encode/mux | ~33 MB 7z / ~117 MB unpacked |

Download prefers CN mirrors when UI or system locale is Chinese.

**Edge online** TTS: 300+ Microsoft natural voices (including Korean), no model or API key. Requires Internet; listing and synthesis go to Microsoft Edge Read Aloud (unofficial; not paid Azure Speech). The configured HTTP proxy is honored. If the Windows system proxy is enabled, that proxy is always used instead of the address in Settings.

Optional env vars for local full libraries (do not commit secrets/paths):

| Variable | Meaning |
|----------|---------|
| `WPF_OCR_CUDA_LIB` | Folder with full CUDA / onnxgpu64 DLLs |
| `WPF_OCR_FFMPEG_LIB` | Folder with FFmpeg 4.4 shared DLLs |

## HTTP API (overview)

When enabled, the OCR API listens on `http_port` (default `1224`). `http_lan = true` (default) accepts this PC and the LAN; `false` listens on `127.0.0.1` only. File transfer, the web manager, and Wi-Fi cast share this port and need LAN access left on. There is no authentication — do not expose it on an untrusted network.

LAN **PC file transfer** shares this HTTP port (`1224`) plus UDP `17531` (pairing required): [HTTP-API.md](HTTP-API.md) · [android/README.md](android/README.md).

- `GET  /api` · `/api/status`
- `GET/POST /api/toast` — bottom toast (`text`, optional `ms`). CLI: `ScreenKit --toast "text" [--ms 1900]`. Both are in the HTTP tab template list.
- `GET/POST /api/zhconv` — simplified/traditional (`text`, `to=trad|simp`). GET query strings are UTF-8. CLI: `ScreenKit --zhconv "text" [--trad|--simp]`
- `GET/POST /api/calendar` — calendars (`date`, `cal=lunar` and others). CLI: `ScreenKit --calendar 2024-02-10 --cal lunar`
- `GET/POST /api/jpyomi` — Japanese reading (`text`, optional `mono`). CLI: `ScreenKit --jpyomi "text" [--mono]`
- `GET/POST /api/cast/stop` — close screencast viewer now
- WebSocket `/cast` — screencast media (same HTTP port)
- `POST /api/ocr` — `box` is original-image pixels. `ocr.engine=winocr` uses Windows OCR on this request
- `POST /api/qr` — barcode / QR only (`/api/barcode`)
- `GET  /api/ocr/get_options`
- `GET  /api/asr/models` · `POST /api/asr` — Sherpa or an installed Windows system recognizer selected by `model`
- `GET  /api/tts/engines` · `GET /api/tts/models?engine=` · `POST /api/tts` — Sherpa, SAPI, Windows (`engine=winrt`), Edge (`engine=edge`). The full model list is cached; `refresh=1` scans again.
- `POST /api/itn`
- `POST /api/translate` — LLM batch (`items[]`)
- `GET  /api/face/models` · `POST /api/face`

Full field reference: **[HTTP-API.md](HTTP-API.md)** · **[Chinese HTTP API](HTTP%E6%8E%A5%E5%8F%A3%E6%96%87%E6%A1%A3.md)**.

## CLI

```text
ScreenKit --image <path> [options]
ScreenKit --snap [--out <dir>]
ScreenKit --list-models
ScreenKit --list-face
ScreenKit --list-sapi              # local SAPI + (x64) x86host voices
ScreenKit --list-asr               # Sherpa models + Windows system recognizers
ScreenKit --list-edge-tts
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

A **Release** build does not copy those folders. Each OCR pack needs ONNX + `configs.txt` (and dict/keys). Optional `pack.json` (`name` / `nameEn` / `variants`) supplies English UI labels; defaults live in `ocr-display.json`. **Windows OCR** in the model list does not use these folders; its languages come from the OCR packs installed in Windows.

```bash
cd ScreenKit
dotnet build -c Release
./ScreenKit/bin/Release/net48/ScreenKit.exe
```

### Slim package (`bin\Release\ScreenKit\`)

- Includes: `ScreenKit.exe`, **`x86host.exe`**, managed deps, **`wetext/`** (ITN), Assets, LICENSE.
- Does **not** include: the Android APK, OCR/ASR/TTS/translation/face models, ORT, OpenCV/Skia/PDFium/Sherpa natives, `ffmpeg64`, `dict.db`, `ZXing.dll`, `SharpCompress.dll`, `SharpSevenZip.dll`, `7za.dll`, or `e_sqlite3.dll`. Install the dictionary or Chinese↔English ONNX translation models from **Help → Install features**, or place them beside the executable yourself. See [Dictionary](#dictionary) for where its data comes from. Barcode, archive extract, and the dictionary each ask to install the missing library when you use them.
- End users install those via **Install features**. Custom Opus-MT ONNX packs can still be placed under `translatemodels/` manually.

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
