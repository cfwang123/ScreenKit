# Changelog

All notable changes to WpfOCR are documented in this file.

Format based on [Keep a Changelog](https://keepachangelog.com/). Versions are project milestones (not necessarily NuGet package versions).

## [Unreleased]

### Added

- 录屏编码增加 **AV1**（`record_codec = av1`）：与 x264/x265 同一套采集→FFmpeg 写 MP4；选项窗可选；ffmpeg64 无 AV1 编码器时明确失败、不回落 x264。CLI：`WpfOCR --test-record-codec av1 --repeat 2`。
- 截图识别「识别结果」拆为 **OCR / 条码** 两个 Tab：进入某 Tab 时，若当前图尚未对该类型识别过则自动识别 1 次。
- **条码识别**改用 **ZXingCpp**（zxing-cpp 原生，较 ZXing.Net 更快更稳）：QR / EAN / UPC / Code39·128 / DataMatrix / PDF417 / Aztec 等；难图走「原图 → 放大 → 底区×2」快路径。
- HTTP `POST /api/ocr`：`options.ocr.barcode`（别名 `ocr.qr` / `ocr.codes`）启用时，响应增加 `barcodes:[{type,text,box}]`。
- **x86host**（独立 32 位程序）：仅提供本机 SAPI HTTP 服务，供 x64 主程序调用仅 32 位可见的系统发音人。
  - 工程：`x86host/` → 产物 `x86host.exe`（与 `WpfOCR.exe` 同目录；Release 编 WpfOCR 时自动编并拷贝）。
  - API：`GET /api/sapi/status` · `GET /api/sapi/voices` · `POST /api/sapi/synth`（WAV）· `POST /api/sapi/shutdown`。
  - 仅监听 `127.0.0.1`，默认端口 `17886`；**空闲 60 秒**无请求自动退出（可用 `--idle-ms` 调整）。
  - 无 GUI、无 OCR/ASR 等其它功能；无额外 NuGet 依赖，避免与主程序 DLL 冲突。
- 主程序 TTS（SAPI 引擎）：按需启动 `x86host`，发音人列表合并本机 + **x86 独有**音；选中 x86 音时朗读/导出走 Web 合成。
- CLI：`WpfOCR --list-sapi` 列出本进程 SAPI，并在 x64 下经 x86host 合并 32 位发音人。

### Changed

- **AV1 录屏**：优先 `libsvtav1`；新增独立 **`record_av1_crf` / 选项窗「AV1 CRF」**（0–63，默认 56），与 x264/x265 的 `record_crf` 并列、互不换算。默认 56 约 x265 CRF28 一半体积；不再用隐藏偏移映射。
- SAPI 32 位支持改为独立 `x86host.exe`，不再二次编译 `WpfOCR.x86.exe` 旁路包。
- 菜单 / 托盘切换「截图完成 · 复制为图片 / 文件 / 路径」时，按新方式**重新复制一次上次截图**（不新建 `screenshots/` 文件；无历史则状态栏提示）。

### Fixed

- 录屏 HUD：红线外侧 5px 整圈可拖动移动选区（原先仅顶部内侧 10px 条，不易点到）。

## [1.0.1] — 2026-08-07

### Added

- **Publish script**: `node scripts/publish-release.mjs` → `release/wpfocr_<version>.7z` (slim Release package; `release/` gitignored).
- **GIF screen record** (Capture menu / tray): region pick → HUD → **preview window** (output FPS 1–24, scale, palette colors) → save silent GIF. Capture at 24 fps; config `[gif_record]`.
- **Screenshot save options** (Settings / `config.toml`): format `png|jpg`, JPG quality 1–100, optional max width/height (fit, no upscale). Affects `screenshots/` and copy-as-file; OCR still uses full resolution.
- **Record HUD** (MP4 + GIF): draggable control bar with left grip; collapse mini bar; **Options** button before Start; icon buttons; move/resize region before and during recording (aspect lock after Start when enabled in record options).
- **Check for Updates** (Tools menu): GitHub Releases check/download, self-update via tmp copy + CLI apply.
- Install Features: clearer status badges for 未安装 / 部分 / 已安装.

### Changed

- Main window title shows version (e.g. `WpfOCR — 截图识别 v1.0.1`).
- Record HUD: start vs pause shown as a single icon control; smoother bar dragging (lightweight move path).

### Fixed

- Record HUD control bar could render outside the visible area on a **secondary monitor** when the capture region was near the bottom edge.

## [1.0.0] — 2026-08-01

### Added

- **In-app Install Features** window (功能组件 + 发音人 tabs):
  - OCR packs (`rapid-ch`, `rapid-i18n`, …), ASR models, FFmpeg, CUDA GPU, DirectML iGPU.
  - On-demand natives: OpenCV, Skia, PDFium, Sherpa `c-api`, **ONNX Runtime CPU (`onnxcpu64`)**.
  - TTS voice catalog with language filter; download progress shows **batch total size and downloaded bytes**.
  - CN-first download mirrors when UI/locale is Chinese (ModelScope / HF mirror / ghproxy).
  - First-run wizard defaults: OpenCV, OrtCpu, Sherpa, rapid-ch, first two ASR packs, FFmpeg (accel off).
- **Feature prompts**: missing OpenCV / OCR models / ORT / PDF stack / FFmpeg / Sherpa / ASR models offer install before use.
- **ASR / TTS / translation** UI and pipelines (sherpa-onnx, SAPI/WinRT voices, Opus-MT ONNX where configured).
- Target framework **.NET Framework 4.8** (`net48`); output under `bin/Release/net48/`.
- ORT load hardening: absolute-path load of real `onnxruntime.dll` before managed P/Invoke (avoids broken System32 stub without `OrtGetApiBase`).

### Changed

- **Removed** root `install.ps1` / `install.cmd`; install models and runtimes from the app (**Tools → Install features**).
- **Screen recording is FFmpeg-only** (`ffmpeg64/`); OpenCV `videoio` / ffmpeg DLLs are stripped after build and not used for encode.
- **onnxcpu64** is on-demand (not shipped with every build). If neither GPU nor iGPU ORT is present, OCR prompts to install CPU ORT (~16 MB).
- Model roots fixed under program folders only: `ocrmodels/`, `asrmodels/`, `ttsmodels/`, `translatemodels/`.
- GPU (`onnxgpu64`) and DirectML (`onnxdml64`) remain optional accel installs; CPU OCR does not require them when `onnxcpu64` is installed.
- Device selection falls back to **CPU** when CUDA or DirectML is not installed / not ready.

### Fixed

- OCR failure when System32 ships a tiny invalid `onnxruntime.dll` (EntryPointNotFound `OrtGetApiBase`).
- Misleading “DirectML missing” error on pure CPU path when no ORT package was installed.
- Recording no longer depends on OpenCV video writers.

### Security / privacy notes

- Default HTTP bind is loopback (`127.0.0.1`).
- Do not commit `config.toml`, capture logs, private screenshots, recorded videos, API keys, or absolute machine paths.
- Do not commit large ONNX weights, CUDA redistributables, or FFmpeg shared binaries without a redistribution plan.

## [0.1.0] — initial milestone

### Added

- **MIT License** for application source ([LICENSE](LICENSE)); third-party notes in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
- **UI language** switch: 中文 / English via **Tools → Language** or Settings; persisted as `ui_lang` in `config.toml`.
- **Screen recording**: pick window or drag a region → red HUD → save MP4 (FFmpeg shared preferred).
- **Long screenshot**: pick a scrollable window → auto-scroll stitch → show image (no OCR).
- Top menu bar and compact toolbar; OCR progress UI; global hotkeys; WeChat-style annotate tools.
- DXGI multi-monitor capture; PDF workbench; HTTP API; device choices CPU / CUDA / DirectML.
- Capture diagnostic log switch; CLI snap / list-models / probe-cuda.
- Main window size/position restore; close hides to tray when tray mode is enabled.

### Changed

- Main hotkey toggles window (not clipboard OCR); tray still offers clipboard OCR.
- Model packs live under Release output `ocrmodels/` only (not under source tree).
- Empty hotkey strings persist as **disabled**.
- Recording HUD chrome is outside the capture rect so it is not baked into the video.

### Fixed

- PerMonitorV2 DPI crop issues; secondary-monitor black/stretch (DXGI).
- OCR busy state no longer blocks applying a newly captured image.
- A/V desync on screen record (WASAPI silence padding).
