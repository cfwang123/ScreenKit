# WpfOCR

Windows desktop OCR tool: screenshot, annotate, recognize text (PP-OCR / RapidOCR packs), long screenshot, **screen recording**, PDF workbench, ASR/TTS, optional translation, and optional local HTTP API.

**Languages:** [English](README.md) · [中文](README.zh.md)

## Screenshots

![WpfOCR main window](docs/1%20screenshot.png)

## Features

| Area | Description |
|------|-------------|
| **Screenshot OCR** | Region capture → OCR; multi-monitor with DXGI Desktop Duplication |
| **Screenshot annotate** | WeChat-style tools: rect / ellipse / arrow / pen / text, color dots, undo / save / confirm |
| **Long screenshot** | Pick a window → auto-scroll stitch → open in viewer (no OCR) |
| **Screen recording** | Window or region → HUD (move/resize region, draggable bar) → MP4 (x264/x265 via **FFmpeg only**) + optional system/mic audio |
| **GIF recording** | Same region flow → capture 24 fps → preview (output FPS, scale, palette) → silent GIF |
| **Clipboard** | Paste image and run OCR; copy image / text |
| **Overlay text** | Text layer on the image; drag-select and copy |
| **PDF workbench** | Open PDF → page OCR → edit lines → export searchable PDF (invisible text layer) |
| **ASR / TTS** | Offline speech recognition (sherpa-onnx) and TTS (sherpa + system voices); install voices in-app |
| **Devices** | CPU · NVIDIA CUDA (GPU) · Intel / DirectML (iGPU); missing accel → CPU |
| **Install features** | In-app download of models and runtimes (CN mirrors when locale is Chinese) |
| **Hotkeys** | Toggle main window · snap annotate · snap OCR (configurable) |
| **HTTP API** | Local JSON API (default `127.0.0.1:1224`) |
| **CLI** | Batch image OCR, list models, probe CUDA, multi-monitor snap test |

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
├── WpfOCR/           # Application source (WPF, net48)
│   ├── Assets/
│   └── bin/Release/net48/   # Build output (models / runtimes live here)
├── docs/             # README screenshots
├── README.md
├── README.zh.md
├── CHANGELOG.md
└── AGENTS.md
```

Model packs and large native runtimes are **not** stored in source. Place or install them next to the executable:

```
WpfOCR/bin/Release/net48/
├── WpfOCR.exe
├── config.toml              # created/updated at runtime
├── ocrmodels/               # OCR packs (rapid-ch, rapid-i18n, …)
├── asrmodels/               # ASR packs (optional)
├── ttsmodels/               # TTS voices (optional)
├── translatemodels/         # Translation ONNX (optional)
├── onnxcpu64/               # ONNX Runtime for CPU EP (on-demand install)
├── onnxgpu64/               # CUDA ORT + CUDA libs (optional)
├── onnxdml64/               # DirectML ORT (optional)
└── ffmpeg64/                # FFmpeg shared DLLs for record (optional)
```

Each OCR pack needs ONNX models + `configs.txt` (and dict/keys as required by the pack).

## Build & run

```bash
cd WpfOCR
dotnet build -c Release
```

Run:

```bash
./WpfOCR/bin/Release/net48/WpfOCR.exe
```

A plain build does **not** ship models, `onnxcpu64`, full CUDA, or FFmpeg. Use **Tools → Install features** (or first-run wizard) inside the app.

### Slim release package (`bin\Release\WpfOCR\`)

Release builds also produce a **small redistributable** under `WpfOCR\bin\Release\WpfOCR\`:

- Includes: `WpfOCR.exe`, managed dependencies, **`wetext/`** (ITN), Assets, LICENSE.
- Does **not** include: OCR/ASR/TTS models, `onnxcpu64` / `onnxgpu64` / `onnxdml64`, OpenCV/Skia/PDFium/Sherpa natives, `ffmpeg64`.
- End users install those via **Install features** (downloads from mirrors / NuGet CDN).
- **Translation** is not covered by the installer: place Opus-MT ONNX under `translatemodels/` yourself if needed.

For local development with models and GPU already present, run **`bin\Release\net48\`** instead.

### Release archive (`release/wpfocr_x.x.x.7z`)

```bash
node scripts/publish-release.mjs
```

Runs Release build, then packs `WpfOCR/bin/Release/WpfOCR/` into `release/wpfocr_<version>.7z` (requires [7-Zip](https://www.7-zip.org/) on PATH). The `release/` folder is gitignored.

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

Settings are stored in `config.toml` beside the exe (also editable via **Tools → Settings** / **Record options**).

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
minimize_to_tray = true
capture_log = false             # true → log/capture.log
ui_lang = "zh"                  # zh | en

[http]
http_enabled = true
http_host = "127.0.0.1"
http_port = 1224
service_mode = false            # keep engine warm

[pdf]
pdf_invisible_text = true
pdf_dpi = 150                   # internal raster DPI; page size follows original PDF

[record]
record_codec = "x264"           # x264 | x265
record_fps = 24
record_crf = 28                 # 0–51, higher = smaller file
record_audio = true
record_audio_src = "Speakers"   # Speakers | Mic | MicAndSpeakers
record_audio_kbps = 96
record_max_size = false
record_max_w = 1920
record_max_h = 1080
record_lock_aspect = true      # lock aspect when resizing HUD region after Start (free before Start)

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
   - Red frame; drag the **top strip** to move, or **8 grips** to resize. **Before Start**, aspect ratio is free; **after Start**, resizing locks aspect ratio unless disabled in record options (`record_lock_aspect = false`). Encoded resolution is fixed when you press Start.
   - Floating **control bar**: drag via the left grip; **collapse** to mini bar; **Options** before Start (record/GIF settings); start/pause share one slot.
   - Bar auto-positions above/below the region and stays within the **current monitor** (multi-monitor safe).
3. Stop → confirm save → MP4 is written; Explorer opens and selects the file.
4. **Capture → Record options**: codec, FPS, CRF, audio source, max output size.

### GIF recording

1. **Capture → GIF record**: same window/region pick.
2. Same HUD; capture at **24 fps**; after Stop, the **preview window** lets you set output FPS (1–24), scale, and palette colors, then save a **silent GIF**.
3. **Capture → GIF record options**: default output FPS, max width/height, default colors.

**Notes**

- MP4 / GIF recording requires **FFmpeg shared** under `ffmpeg64/` (install in-app or place manually). OpenCV is **not** used for video encode.
- System-loopback audio pads silence on wall-clock gaps so late sound is not shifted to the start of the file.
- Temporary files live under the app `tmp/` folder and are cleaned up after a successful save.
- GIF size grows quickly with resolution and duration — use preview scale/FPS and the max-size limit.

## Default hotkeys

| Hotkey | Action |
|--------|--------|
| `Ctrl+Alt+O` | Toggle main window show / hide |
| `Ctrl+Alt+Q` | Screenshot annotate |
| `Ctrl+Alt+W` | Screenshot and OCR |

Tray icon: left-click toggles the window; context menu includes clipboard OCR and exit. Closing the main window typically **hides** to tray rather than exiting.

### UI language

- **Tools → Language** → **中文 / English** (applies immediately)
- Or set UI language at the top of **Settings**
- Persisted in `config.toml` as `ui_lang = "zh"` or `"en"`

## CLI

```text
WpfOCR --image <path> [options]
WpfOCR --snap [--out <dir>]
WpfOCR --list-models
WpfOCR --probe-cuda
WpfOCR --help
```

Useful options: `-d gpu|cpu`, `-p rapid-ch`, `-v <variant>`, `-m <models-dir>`, `--det-limit`, `--no-cls`.

## HTTP API (overview)

When enabled, a local server listens on `http_host:http_port` (default loopback only).

- `GET  /api` · `/api/status` — capabilities
- `POST /api/ocr` — image (JSON base64 or multipart)
- `GET  /api/ocr/get_options` — OCR options snapshot
- `GET  /api/asr/models` · `POST /api/asr` — speech recognition
- `GET  /api/tts/models` · `POST /api/tts` — TTS (wav base64)
- `POST /api/itn` — inverse text normalization

Bind to `127.0.0.1` unless you intentionally expose the service on a trusted network.

Full field reference: **[HTTP-API.md](HTTP-API.md)** · **[HTTP接口文档.md](HTTP接口文档.md)** (中文).

## Capture diagnostics

Set `capture_log = true` in `config.toml` to write `log/capture.log` (multi-monitor / DPI troubleshooting). Keep it off for normal use.

CLI: `WpfOCR --snap` dumps full-monitor bitmaps under `log/snap/` (or `--out`).

## License

**WpfOCR application source code** (this repository’s `WpfOCR/` sources, scripts, and docs authored for the project) is released under the **MIT License**. See [LICENSE](LICENSE).

```
Copyright (c) 2026 WpfOCR Contributors
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
