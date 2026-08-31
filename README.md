# ScreenKit

Windows desktop tool (project ScreenKit, exe `ScreenKit.exe`; Chinese UI title **屏幕截图工具**): screenshot, annotate, recognize text (PP-OCR / RapidOCR packs), long screenshot, **screen recording**, PDF workbench, ASR/TTS, optional translation, and optional local HTTP API.

Current version: **1.0.5**

**Languages:** [English](README.md) · [中文](README.zh.md)

## Screenshots

![ScreenKit main window](docs/1%20screenshot.en.png)

## Features

| Area | Description |
|------|-------------|
| **Screenshot recognition** | Region capture → text OCR or barcode/QR recognition; multi-monitor DXGI capture. Korean/English word spaces are restored from visual gaps on the line (same for Latin text under the Chinese rec model); no spaces inserted between CJK characters. Optional overlay translation via LLM (default off) |
| **Screenshot annotate** | WeChat-style tools: rect / ellipse / arrow / pen / text, color dots, undo / save / confirm |
| **Long screenshot** | Pick a window → auto-scroll stitch → open in viewer (no OCR) |
| **Screen recording** | Window or region → HUD (move/resize region, draggable bar) → MP4 (x264/x265/AV1 via **FFmpeg only**) + optional system/mic audio |
| **GIF recording** | Same region flow → capture 24 fps → preview (output FPS, scale, palette) → silent GIF |
| **Clipboard** | Paste image and run OCR; Edit menu: copy image / file / path; copy text via Ctrl+C or the result-panel button; menu/tray can switch on-capture copy mode (image / file / path) |
| **Overlay text** | Text layer on the image; drag-select and copy |
| **PDF workbench** | Open PDF → page OCR → edit lines → export searchable PDF (invisible text layer) |
| **ASR / TTS** | Offline speech recognition (sherpa-onnx) and TTS (Sherpa + SAPI / WinRT system voices); install voices in-app |
| **Translation** | Opus-MT ONNX locally, or any configured **LLM** (`[[llm]]`); pick the engine on the Translate tab; floating translate popup (`Ctrl+Alt+T`) |
| **Face** | InsightFace ONNX detect/compare two images; optional landmarks and gender/age overlay; models in `facemodels/` (download **buffalo_l** via Install Features) |
| **SAPI x86 helper** | Sidecar `x86host.exe` (32-bit SAPI web only) for classic voices visible only in x86 processes |
| **Devices** | CPU · NVIDIA CUDA (GPU) · Intel / DirectML (iGPU); missing accel → CPU |
| **Install features** | In-app download of models and runtimes (CN mirrors when locale is Chinese) |
| **Hotkeys** | Toggle main window · snap annotate · snap OCR · voice input · translate popup (configurable) |
| **HTTP API** | Local JSON API (default `127.0.0.1:1224`). Main-window tab: call log + manual request |
| **CLI** | Batch OCR, list models / SAPI voices, probe CUDA, multi-monitor snap test |

## Requirements

- Windows 10/11 (x64)
- [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48) (runtime on end-user PCs)
- Build: Visual Studio / MSBuild with .NET Framework 4.8 targeting pack (or SDK that can build `net48` WPF)
- Optional: NVIDIA GPU + CUDA stack matching the ORT GPU package (`onnxgpu64`)
- Optional: DirectML-capable GPU for the “核显” device option (`onnxdml64`)
- Optional (screen record): FFmpeg **4.4 shared** libraries under `ffmpeg64/` next to the exe

## Project layout

```
OCR/
├── ScreenKit/                 # Application source (WPF, net48, x64)
│   ├── Assets/
│   └── bin/Release/
│       ├── net48/          # Dev output (models / runtimes live here)
│       └── ScreenKit/      # Slim package: ScreenKit.exe + x86host.exe + managed deps
├── x86host/                # Standalone 32-bit SAPI web helper (x86host.exe only)
├── docs/                   # README screenshots
├── scripts/publish-release.mjs
├── README.md
├── README.zh.md
├── CHANGELOG.md
└── AGENTS.md
```

Model packs and large native runtimes are **not** stored in source. Place or install them next to the executable:

```
ScreenKit/bin/Release/net48/
├── ScreenKit.exe
├── config.toml              # created/updated at runtime
├── ocrmodels/               # OCR packs (rapid-ch, rapid-i18n, …)
├── asrmodels/               # ASR packs (optional)
├── ttsmodels/               # TTS voices (optional)
├── translatemodels/         # Translation ONNX (optional)
├── facemodels/              # Face ONNX (optional, InsightFace; Install Features can fetch buffalo_l)
├── onnxcpu64/               # ONNX Runtime for CPU EP (on-demand install)
├── onnxgpu64/               # CUDA ORT + CUDA libs (optional)
├── onnxdml64/               # DirectML ORT (optional)
└── ffmpeg64/                # FFmpeg shared DLLs for record (optional)
```

Each OCR pack needs ONNX models + `configs.txt` (and dict/keys as required by the pack). Optional `pack.json` (`name` / `nameEn` / `variants`) supplies English UI labels; built-in defaults live in `ocr-display.json` next to the exe.

A **Release** build does not copy or junction these folders (or `onnxcpu64` / `onnxgpu64` / `onnxdml64` / `ffmpeg64`). Place them under `bin/Release/net48/` yourself.

## Build & run

```bash
cd ScreenKit
dotnet build -c Release
```

Run:

```bash
./ScreenKit/bin/Release/net48/ScreenKit.exe
```

A plain build does **not** ship models, `onnxcpu64`, full CUDA, or FFmpeg. Use **Tools → Install features** (or first-run wizard) inside the app.

### Slim release package (`bin\Release\ScreenKit\`)

Release builds also produce a **small redistributable** under `ScreenKit\bin\Release\ScreenKit\`:

- Includes: `ScreenKit.exe`, **`x86host.exe`** (32-bit SAPI web), managed dependencies, **`wetext/`** (ITN), Assets, LICENSE.
- Does **not** include: OCR/ASR/TTS/face models, `onnxcpu64` / `onnxgpu64` / `onnxdml64`, OpenCV/Skia/PDFium/Sherpa natives, `ffmpeg64`.
- End users install those via **Install features** (downloads from mirrors / NuGet CDN).
- **Translation** is not covered by the installer: place Opus-MT ONNX under `translatemodels/` yourself if needed.

For local development with models and GPU already present, run **`bin\Release\net48\`** instead.

### Release archive (`release/screenkit_x.x.x.7z`)

```bash
node scripts/publish-release.mjs
```

Runs Release build, then packs `ScreenKit/bin/Release/ScreenKit/` (folder included) into `release/screenkit_<version>.7z` (requires [7-Zip](https://www.7-zip.org/) on PATH). The `release/` folder is gitignored.

## In-app install (recommended)

1. First launch may open the install wizard (defaults: OpenCV, **ORT CPU**, OCR `rapid-ch`, first two ASR packs, FFmpeg; GPU/iGPU **off**).
2. Later: **Tools → Install features**
   - **Components**: OpenCV, Skia, PDFium, Sherpa, **ORT CPU (`onnxcpu64`)**, OCR/ASR packs, CUDA, DirectML, FFmpeg.
   - **Voices**: TTS models with language filter; progress shows **total batch size and downloaded bytes**.
3. Using a feature that needs a missing package prompts to open the installer (e.g. OCR without any ORT → install `onnxcpu64`).

| Runtime | Role | Typical size |
|---------|------|----------------|
| **onnxcpu64** | CPU ONNX Runtime (required for OCR if no GPU/iGPU ORT) | ~16 MB |
| **onnxgpu64** | NVIDIA CUDA EP + CUDA/cuDNN (optional) | large |
| **onnxdml64** | DirectML EP for iGPU (optional) | ~18 MB |
| **OpenCV** | Capture / image pipeline | ~61 MB |
| **ffmpeg64** | Screen record encode/mux | ~72 MB |

Download prefers CN mirrors when UI or system locale is Chinese.

Optional env vars for local full libraries (do not commit secrets/paths into docs meant for others):

| Variable | Meaning |
|----------|---------|
| `WPF_OCR_CUDA_LIB` | Folder with full CUDA / onnxgpu64 DLLs |
| `WPF_OCR_FFMPEG_LIB` | Folder with FFmpeg 4.4 shared DLLs |

## Configuration

Settings are stored in `config.toml` beside the exe (also editable via **Tools → Settings** / **Record options**). The Settings window groups options into tabs: General, OCR, Hotkeys, Speech, LLM, Translate, Capture, API.

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
capture_log = false             # true → log/capture.log
# llm_log = false               # true → log/llm.log (polish HTTP; API key not written)
ui_lang = "zh"                  # zh | en
update_check_days = 7           # auto-check interval on startup (days); 0 = off. Menu Check for Updates always works.
# http_proxy = false            # HTTP proxy for GitHub / Hugging Face / other non-China sites
# http_proxy_addr = "127.0.0.1:7897"
# ocr_translate_lang = ""       # OCR overlay/result target: empty = off; LLM lang code (zh/en/ja/ko/fr/…)

[http]
http_enabled = true
http_host = "127.0.0.1"
http_port = 1224
service_mode = false            # keep engine warm

[pdf]
pdf_invisible_text = true
pdf_dpi = 150                   # internal raster DPI; page size follows original PDF

[record]
record_codec = "x264"           # x264 | x265 | av1
record_fps = 24
record_crf = 28                 # x264/x265 only, 0–51, higher = smaller
record_av1_crf = 56             # AV1 only, 0–63 (different scale; 56 ≈ half of x265 CRF28 size)
record_audio = true
record_audio_src = "Speakers"   # Speakers | Mic | MicAndSpeakers
record_audio_kbps = 96
record_max_size = false
record_max_w = 1920
record_max_h = 1080
record_lock_aspect = true      # lock aspect when resizing HUD region after Start (free before Start)

[asr]
asr_voice_mode = "stream"       # stream = live; offline = record until hotkey stop, then one-shot ASR
asr_voice_polish = true         # LLM polish for voice input (needs selected [[llm]] url + model)
asr_voice_split = true
asr_voice_split_sec = 5         # split only after this many seconds of silence (1–30); do not cut continuous speech
asr_live_mode = "stream"        # stream | offline (offline splits on silence)
asr_live_polish = false         # LLM polish each live-caption sentence
asr_live_split = true           # auto-split after polish / completed sentences
asr_llm = "gpt-4o-mini"         # display name of the [[llm]] entry used for polish (empty = first)
# asr_llm_prompt = "..."

[[llm]]
name = "gpt-4o-mini"            # display name; defaults to model id
url = "https://api.openai.com/v1"
# key = ""                    # do not commit secrets
model = "gpt-4o-mini"           # polish/translate: prefer small models e.g. Qwen/Qwen3.5-4B
think = "low"                   # off | low | medium | high | max; Off for small models; GLM-5.3 cannot off
# Polish sends prior output in the same session as context (homophones / names).

[translate]
translate_compute = "Auto"      # Auto | Gpu | Cpu | Igpu (local Opus-MT ONNX)
# translate_llm = ""            # empty = local ONNX; else [[llm]] display name
# translate_llm_prompt = "请将用户给出的文本从{src}翻译为{dst}。只输出译文。"

[gif_record]
gif_fps = 8                     # default output FPS in preview (1–24); capture is 24 fps
gif_max_size = true
gif_max_w = 1280
gif_max_h = 720
gif_colors = 128                # palette colors in preview (32/64/128/256)
gif_scale = 100                 # default scale % in preview
```

Leave a hotkey string empty to disable that hotkey.

Do **not** commit real `config.toml` if it encodes machine-specific paths or preferences you want private.

## Screen recording

1. **Capture → Screen record** (or the toolbar button): click a window or drag a region.
2. **HUD** (drawn outside the capture area):
   - Red frame; drag the **5px strip** to move, or **8 grips** to resize. Aspect ratio is free before **Start** and locked afterwards unless disabled in record options (`record_lock_aspect = false`).
   - Floating **control bar**: drag via the left grip; **collapse** to mini bar; **Options** before Start (record/GIF settings); start/pause share one slot.
   - Bar auto-positions above/below the region and stays within the **current monitor** (multi-monitor safe).
3. Stop → confirm save → MP4 is written; Explorer opens and selects the file.
4. **Capture → Record options**: codec (x264 / x265 / AV1), FPS, **CRF** (x264/x265) and **AV1 CRF** (AV1 only, separate scale 0–63, default 56), audio source, max output size. AV1 needs libsvtav1 / libaom-av1 in `ffmpeg64`.

### GIF recording

1. **Capture → GIF record**: same window/region pick.
2. Same HUD; capture at **24 fps**; after Stop, the **preview window** lets you set output FPS (1–24), scale, and palette colors, then save a **silent GIF**.
3. **Capture → GIF record options**: default output FPS, max width/height, default colors.

**Notes**

- MP4 / GIF recording requires **FFmpeg shared** under `ffmpeg64/` (install in-app or place manually). OpenCV is **not** used for video encode.
- GIF size grows quickly with resolution and duration — use preview scale/FPS and the max-size limit.

## Default hotkeys

| Hotkey | Action |
|--------|--------|
| `Ctrl+Alt+O` | Toggle main window show / hide |
| `Ctrl+Alt+Q` | Screenshot annotate |
| `Ctrl+Alt+W` | Screenshot and OCR |
| `Ctrl+Alt+V` | Voice input (press again to stop) |
| `Ctrl+Alt+B` | Live caption |
| `Ctrl+Alt+T` | Translate popup show / hide |

Tray icon: left-click toggles the window; context menu includes voice input, **translate popup**, clipboard OCR, on-capture copy mode (image / file / path), and exit. Closing the main window typically **hides** to tray rather than exiting.

### UI language

- **Tools → Language** → **中文 / English** (applies immediately)
- Or set UI language in **Settings → General**
- Persisted in `config.toml` as `ui_lang = "zh"` or `"en"`
- Covers menus, Settings, OCR toolbar (overlay dest language, **Pack / Lang** combo names via `ocr-display.json` `nameEn`), Translate tab, translate popup, and Face tab.

## CLI

```text
ScreenKit --image <path> [options]
ScreenKit --snap [--out <dir>]
ScreenKit --test-clipboard-path
ScreenKit --test-face-overlay
ScreenKit --list-models
ScreenKit --list-face
ScreenKit --list-sapi              # local SAPI + (x64) x86host voices
ScreenKit --probe-cuda
ScreenKit --help
```

Useful options: `-d gpu|cpu`, `-p rapid-ch`, `-v <variant>`, `-m <models-dir>`, `--det-limit`, `--no-cls`.

## x86host (32-bit SAPI only)

Some classic **SAPI** voices register only for 32-bit processes. Ship **`x86host.exe`** next to `ScreenKit.exe` (built from `x86host/`; a Release build of ScreenKit also builds and copies it).

| Item | Detail |
|------|--------|
| Role | HTTP helper: list SAPI voices + synth WAV; **no GUI**, no OCR/ASR |
| Start | On demand by the x64 app, or run `x86host.exe` manually |
| Bind | `127.0.0.1` only, default port **17886** |
| Idle | Exit after **60s** without requests (`--idle-ms` to override) |
| API | `GET /api/sapi/status` · `GET /api/sapi/voices` · `POST /api/sapi/synth` · `POST /api/sapi/shutdown` |

```text
dotnet build x86host/x86host.csproj -c Release
x86host.exe --port 17886 --idle-ms 60000
x86host.exe --list-sapi
```

In the UI, choose engine **SAPI**: local voices plus **x86-only** entries (display name ends with `· x86`); speak/export for those voices goes through x86host.

## HTTP API (overview)

When enabled, a local server listens on `http_host:http_port` (default loopback only).

- `GET  /api` · `/api/status` — capabilities
- `POST /api/ocr` — image (JSON base64 or multipart)
- `POST /api/qr` — barcode / QR only (`/api/barcode`; JSON base64/path or multipart)
- `GET  /api/ocr/get_options` — OCR options snapshot
- `GET  /api/asr/models` · `POST /api/asr` — speech recognition
- `GET  /api/tts/models` · `POST /api/tts` — TTS (wav base64)
- `POST /api/itn` — inverse text normalization
- `POST /api/translate` — LLM batch translate (`items[]`; needs configured LLM)
- `GET  /api/face/models` · `POST /api/face` — face detect / compare

Bind to `127.0.0.1` unless you intentionally expose the service on a trusted network.

Full field reference: **[HTTP-API.md](HTTP-API.md)** · **[HTTP接口文档.md](HTTP接口文档.md)** (中文).

## Capture diagnostics

Set `capture_log = true` in `config.toml` to write `log/capture.log` (multi-monitor / DPI troubleshooting). Keep it off for normal use.

Set `llm_log = true` (Settings → LLM) to write `log/llm.log` for polish HTTP traces. API keys are not written. Keep it off for normal use.

CLI: `ScreenKit --snap` dumps full-monitor bitmaps under `log/snap/` (or `--out`).

## License

**ScreenKit application source code** (this repository’s `ScreenKit/` sources, scripts, and docs authored for the project) is released under the **MIT License**. See [LICENSE](LICENSE).

```
Copyright (c) 2026 ScreenKit Contributors
```

You may use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, subject to including the copyright and permission notice. **THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND.**

### Third-party components

Bundled or optional dependencies are **not** all MIT. Models, FFmpeg builds, CUDA/cuDNN, and some native libraries keep their own terms. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Do **not** commit large ONNX weights, CUDA redistributables, or FFmpeg shared binaries into git without a redistribution plan that matches their licenses.

## See also

- [LICENSE](LICENSE) — MIT (application source)
- [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) — dependency license notes
- [CHANGELOG.md](CHANGELOG.md)
- [HTTP-API.md](HTTP-API.md) · [HTTP接口文档.md](HTTP接口文档.md) — HTTP API
- [README.zh.md](README.zh.md) — Chinese documentation
