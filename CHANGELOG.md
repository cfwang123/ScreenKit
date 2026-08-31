# Changelog / 更新日志

All notable changes to ScreenKit are documented here. / 本文件记录 ScreenKit 的重要变更。

Format based on [Keep a Changelog](https://keepachangelog.com/). Versions are project milestones. / 格式基于 Keep a Changelog，版本号表示项目里程碑。

## unreleased

### English

#### Added

- Polish / translate prompt **Default** button (resets to Chinese or English default for the current UI language).
- Main-window **HTTP API** tab: brief call log and a manual request builder (templates for status / OCR / ASR / TTS / ITN / translate / face).
- **HTTP proxy** (Settings → General, `http_proxy` / `http_proxy_addr`): used for GitHub, Hugging Face, and other non-China sites; China mirrors go direct. Default off, address `127.0.0.1:7897`.
- **Auto-update interval** (Settings → General, default 7 days, `update_check_days`; 0 = off). On startup only, if that many days have passed since the last successful check, query GitHub Releases. Tools → Check for Updates is unchanged.
- Edit menu **Copy file**: current image is saved to `screenshots/` then copied as FileDrop (paste in Explorer). **Copy image** now copies the bitmap (toolbar follows).
- Edit menu **Copy path**: copies the image file path (source path for opened files; screenshots / pasted images are saved to `screenshots/` first). **Copy text** removed from the menu (Ctrl+C and the result-panel button remain).

#### Changed

- LLM thinking intensity adds **medium** and the dropdown shows raw API values `none / low / medium / high / max` (config unchanged; `none` still maps to `off`; `medium` / `mid` / `中` no longer fall back to `low`).
- HTTP JSON responses write UTF-8 Chinese directly (no `\uXXXX` escapes).
- `GET /api/asr/models` `type` is an English keyword (`SenseVoice` / `Transducer` / …), not a UI label such as `流式 Transducer`.

#### Fixed

- English UI: OCR **Pack** / **Lang** combos used Chinese labels (`Rapid 全语种`, `Umi-OCR（多语言）`, `简体中文`, …). Display names now come from `ocr-display.json` (`name` / `nameEn`) and optional per-pack `pack.json`.
- HTTP `POST /api/translate` always returned 940 (LLM not configured): the options snapshot used by the HTTP server omitted `[[llm]]` / `translate_llm`.
- HTTP JSON 500 (`TypeInfoResolver`): STJ 8 requires a type-info resolver on `JsonSerializerOptions` before first use. The UTF-8 CJK encoder options omitted it, so `GET /api/face/models` (and other `/api/*` JSON) returned code 900.

### 中文

#### 新增

- 润色 / 翻译提示词 **默认** 按钮：按当前界面语言重置为中文或英文默认提示词。
- 主界面 **HTTP接口** Tab：简短调用日志，并可按模板手动构造请求（status / OCR / ASR / TTS / ITN / 翻译 / 人脸）。
- **HTTP 代理**（参数设置 → 常规，`http_proxy` / `http_proxy_addr`）：访问 GitHub、Hugging Face 等非中国网站时使用，国内镜像直连。默认关闭，地址 `127.0.0.1:7897`。
- **自动更新间隔**（参数设置 → 常规，默认 7 天，`update_check_days`；0=不自动检查）。仅在启动时判断：超过该天数未成功检查则查询 GitHub Releases。菜单「检查更新」不受限。
- 「编辑」菜单 **复制文件**：当前图片先存 `screenshots/`，以 FileDrop 复制到剪贴板（资源管理器可粘贴）。**复制图片**改为复制位图（工具栏同步）。
- 「编辑」菜单 **复制路径**：复制图片文件路径（打开的图用源路径；截图 / 粘贴的图先存 `screenshots/`）。菜单去掉 **复制文字**（仍可用 Ctrl+C 与结果区按钮）。

#### 变更

- LLM 思考强度增加 **medium**，下拉显示 API 原值 `none / low / medium / high / max`（配置不变；`none` 仍按 `off`；`medium` / `mid` / `中` 不再回落到 `low`）。
- HTTP JSON 响应直接输出 UTF-8 中文，不再使用 `\uXXXX` 转义。
- `GET /api/asr/models` 的 `type` 改为英文关键字（`SenseVoice` / `Transducer` 等），不再返回「流式 Transducer」这类界面文案。

#### 修复

- 英文界面顶栏 **Pack / Lang** 下拉仍显示中文包名与变体（Rapid 全语种、Umi-OCR（多语言）、简体中文 等）。现从 `ocr-display.json` 的 `name` / `nameEn`（以及包内可选 `pack.json`）读取显示名。
- HTTP `POST /api/translate` 一直返回 940（未配置翻译 LLM）：HTTP 用的配置快照漏了 `[[llm]]` / `translate_llm`。
- HTTP JSON 响应 500（缺 TypeInfoResolver）：STJ 8 首次使用序列化选项前必须指定 TypeInfoResolver；为输出 UTF-8 中文而加的选项漏了该项，导致 `GET /api/face/models`（以及其它 `/api/*` JSON）返回 900。

## v1.0.4 (2026-08-31)

### English

#### Added

- **LLM translate**: continue when `finish_reason` is `length` / `max_tokens` (up to 6 rounds); HTTP `POST /api/translate` batch (no item cap); OCR dest-language combo + Translate toggle; translate popup (Tools / tray / **Ctrl+Alt+T**); many source/target languages (ONNX still lists installed pairs only).

#### Changed

- Chinese window / tray title is **屏幕截图工具** (English remains ScreenKit).

#### Fixed

- Copy-as-path after a screenshot sometimes left the clipboard empty (`OleFlushClipboard` on a null OLE object). Path copy now sets a persist OLE text DataObject. CLI: `ScreenKit --test-clipboard-path`.

### 中文

#### 新增

- **LLM 翻译**：超长续写（`length` / `max_tokens`，最多 6 轮）；HTTP `POST /api/translate` 批量不限条数；截图识别页目标语下拉 + 翻译开关；翻译小窗（工具菜单 / 托盘 / **Ctrl+Alt+T**）；LLM 源/目标多种语言（本地 ONNX 仍只列出已装方向）。

#### 变更

- 中文界面程序标题改为**屏幕截图工具**（英文仍为 ScreenKit）。

#### 修复

- **截图复制为路径**有时剪贴板是空的（`OleFlushClipboard` 清掉刚写入的文本）。现改为 persist 的 OLE 文本 DataObject。CLI：`ScreenKit --test-clipboard-path`。

## v1.0.3 (2026-08-30)

### English

#### Added

- Main window **Face** tab: InsightFace ONNX detect/recognize/compare. Models live in `facemodels/` next to the exe. CLI: `ScreenKit --list-face`.
- HTTP `GET /api/face/models` and `POST /api/face` (extract one image or compare two). Install Features can download InsightFace **buffalo_l** into `facemodels/`.
- Settings **LLM** tab: multiple OpenAI-compatible endpoints as TOML `[[llm]]` (`name` / `url` / `key` / `model` / `think`). Speech tab picks one by display name (`asr_llm`); polish prompt stays on Speech. Display name defaults to model id. Old `asr_llm_url` / `asr_llm_token` / `asr_llm_model` keys are ignored (re-enter in the new UI). **Copy** duplicates the selected endpoint.
- Per-endpoint **thinking intensity** (`think`: `off` / `low` / `high` / `max`, default `low`). `off` sends `thinking.type=disabled`; `low`/`high`/`max` send `thinking.type=enabled` plus `reasoning_effort` (GLM-5.3 cannot disable thinking). If `off` is rejected, retry with `low`; other HTTP 400s drop think fields.
- Translate tab can use a configured **LLM** instead of local Opus-MT (`translate_llm`). The LLM prompt (`translate_llm_prompt`, `{src}`/`{dst}`) is edited in **Settings → Translate**. LLM also supports 来回翻译 without a reverse ONNX pair.

#### Changed

- Extended **English UI** coverage: main window TTS/ASR/Translate/Face tabs, settings, annotate window, install-features window, PDF workbench (static + dynamic strings), feature catalog, voice-input HUD, and feature-install prompts (`FeaturePrompt`).

#### Fixed

- Restored **word spaces** for Korean and English: PP-OCR rec rarely emits space (the Chinese dict has none). Spaces are inserted from low-contrast column valleys on the cropped line (word-sized gaps only); CJK–CJK pairs are left unchanged.
- Copy-as-path still occasionally pasted as `▀` because WPF `ContainsImage`/`GetText` after Win32 `EmptyClipboard` could re-attach the previous delayed-render bitmap (OLE). Path copy now drops the OLE data object, writes Unicode/ANSI text via Win32, flushes, and verifies formats without WPF clipboard APIs. CLI: `ScreenKit --test-clipboard-path`.
- Face tab gender/age overlay used OpenCV Hershey (no CJK), so labels showed `?? 22??`. They now use Microsoft YaHei. CLI: `ScreenKit --test-face-overlay`.
- HTTP JSON write no longer passes a resolver-less `JsonSerializerOptions` (STJ 8), so `GET /api`, `GET /api/ocr/get_options`, and `GET /api/face/models` no longer return 500.
- Voice-input HUD stayed on **Recognizing** while the LLM request ran (stop ran ASR+HTTP on the UI thread). It now switches to **Polishing** after ASR, and stop no longer blocks the UI.
- Polishing HUD showed only “Polishing · Esc to stop” and dropped the recognized source text: a later status callback overwrote the second line. The original transcript is kept on that line.
- LLM HTTP timeout was treated as Esc-cancel, so ending dictation dropped the sentence. Timeout now injects the original ASR text. Empty `message.content` (thinking-only replies) also falls back to the original.
- Settings **LLM** tab: optional `llm_log` writes `log/llm.log` (request/response/timing; API key not written).
- Main window tabs (Screenshot OCR / TTS / ASR / Translate) follow the UI language.
- `applylang()` runs after feature tabs initialize so combo labels respect the selected language.

### 中文

#### 新增

- 主界面增加 **人脸识别** Tab：InsightFace ONNX 检测/识别/比对。模型放在程序旁 `facemodels/`。CLI：`ScreenKit --list-face`。
- HTTP `GET /api/face/models`、`POST /api/face`（单图提取或两图比对）。「安装功能」可下载 InsightFace **buffalo_l** 到 `facemodels/`。
- 参数设置增加 **LLM接口** Tab：多套 OpenAI 兼容接口以 TOML `[[llm]]` 数组保存（`name` / `url` / `key` / `model` / `think`）。语音 Tab 用显示名称选择润色接口（`asr_llm`），提示词仍在语音。显示名称默认等于模型 id。旧键 `asr_llm_url` / `asr_llm_token` / `asr_llm_model` 不再读取，需在新界面重新填写。列表可**复制**当前接口。
- 每条 LLM 可配 **思考强度**（`think`：`off` / `low` / `high` / `max`，默认 `low`）。`off` 发关闭思考；`low`/`high`/`max` 发 `thinking.type=enabled` 与 `reasoning_effort`（GLM-5.3 不能关思考）。若 `off` 被拒绝则改 `low` 再试；其它 400 再去掉思考字段。
- **翻译**可选用已配置的 LLM（`translate_llm`），不必装 Opus-MT。LLM 提示词（`translate_llm_prompt`，`{src}`/`{dst}`）在**参数设置 → 翻译**编辑。LLM 下来回翻译也不需要反向 ONNX 模型。

#### 变更

- **补全英文界面**：主窗口 TTS/ASR/翻译/人脸 Tab、设置、标注窗口、安装功能、PDF 工作台（静态与动态文案）、功能目录、语音输入 HUD、功能安装提示（`FeaturePrompt`）等。

#### 修复

- **韩语/英语词间空格**：PP-OCR 识别很少输出空格（中文字典本身没有空格）。按裁剪图列对比度谷（词距宽度）补回空格；汉字与汉字之间不插。中文模型识别拉丁文时同样生效。
- **截图复制为路径**仍会偶尔粘成「▀」：Win32 清空后若再用 WPF `ContainsImage`/`GetText` 校验，会把上一张延迟渲染的位图重新挂上。现改为先丢掉 OLE 数据对象，只写入 Unicode/ANSI 文本并 Flush，用 Win32 枚举格式校验。CLI：`ScreenKit --test-clipboard-path`。
- 人脸框上的性别年龄原先用 OpenCV Hershey 字体，汉字显示成 `?? 22??`。改为微软雅黑绘制。CLI：`ScreenKit --test-face-overlay`。
- HTTP 写出 JSON 不再传入缺 TypeInfoResolver 的 `JsonSerializerOptions`（STJ 8），`GET /api`、`GET /api/ocr/get_options`、`GET /api/face/models` 不再 500。
- 语音输入结束时浮窗一直显示**识别中**：收尾把 ASR 和 LLM 请求堵在 UI 线程上，界面无法切到润色。现改为识别完成后显示**润色中**，结束听写不再卡住界面。
- **润色中**第二行被后续状态回调清空，只剩「润色中 · Esc 停止」。现会保留识别原文。
- LLM HTTP 超时被当成 Esc 取消，结束听写时整句丢弃。现超时回退输出识别原文；`message.content` 为空（只有思考）同样回退原文。
- LLM 接口 Tab 可开 `llm_log`，写入 `log/llm.log`（请求/响应/耗时，不含 API key）。
- 主窗口顶栏 Tab（截图识别 / 语音合成 / 语音识别 / 翻译）随界面语言切换。
- `applylang()` 改在功能 Tab 初始化之后执行，下拉框等控件随语言正确刷新。

## v1.0.2 (2026-08-26)

### English

#### Added

- Added **AV1** screen recording (`record_codec = av1`) using the same capture-to-FFmpeg MP4 pipeline as x264/x265. The options window exposes it; missing AV1 encoders fail explicitly without falling back to x264. CLI: `ScreenKit --test-record-codec av1 --repeat 2`.
- Split recognition results into **OCR / Barcode** tabs. Entering a tab runs that recognition type once if the current image has not yet been processed for it.
- Switched **barcode recognition** to native **ZXingCpp** for faster and more reliable QR, EAN, UPC, Code39/128, DataMatrix, PDF417, and Aztec detection. Difficult images use original, enlarged, and doubled-bottom-region passes.
- HTTP `POST /api/ocr`: enabling `options.ocr.barcode` (aliases `ocr.qr` / `ocr.codes`) adds `barcodes:[{type,text,box}]` to the response.
- Added **x86host**, a standalone 32-bit local SAPI HTTP helper for voices visible only to x86 processes (`x86host.exe` beside `ScreenKit.exe`).
  - API: `GET /api/sapi/status`, `GET /api/sapi/voices`, `POST /api/sapi/synth` (WAV), and `POST /api/sapi/shutdown`.
  - Listens only on `127.0.0.1`, uses port `17886` by default, and exits after **60 seconds idle** (configurable with `--idle-ms`).
- Main-app TTS starts `x86host` on demand, merges native and **x86-only** voices, and uses web synthesis for x86 selections.
- CLI `ScreenKit --list-sapi` lists voices from the current process and merges 32-bit voices through x86host on x64.
- The voice-input HUD now shows **Polishing** and the recognized source text on its second line while offline LLM polishing is in progress.
- Added **Capture → Voice Input** and tray voice-input commands, matching `Ctrl+Alt+V` toggle behavior.
- Added Speech-tab controls for voice-input polishing/auto-splitting and live-caption model mode, polishing, and splitting. These share OpenAI-compatible `asr_llm_*` settings.
- With voice-input **auto-split** enabled, each completed sentence is polished when configured and inserted immediately.
- Voice-input and live-caption polishing sends prior output from the current session (about one thousand characters) to improve homophones, names, and references; the model returns only the current sentence.
- Added a voice-input **split interval** (default 5 seconds): a sentence ends only after that much silence; uninterrupted speech is not split.
- Pressing **Esc during voice recognition/polishing** immediately ends the session, cancels polishing, and suppresses the current sentence. The toggle hotkey still finishes and outputs it. Esc also cancels active OCR.

#### Changed

- Frontend screenshot recognition now follows the currently selected **OCR / Barcode** result tab and keeps that tab selected. Annotation and screen-board recognition use the same behavior.
- Settings are now organized into **General**, **Recognition**, **Hotkeys**, **Speech**, **Capture**, and **API** tabs. Service mode moved to API.
- **Offline voice input** now records the whole utterance and recognizes it once when the toggle hotkey is pressed, followed by optional polishing.
- Polished output automatically removes `<think>` / `<thinking>` reasoning blocks, orphaned `</think>` tags, and whole Markdown fences.
- **AV1 recording** prefers `libsvtav1` and has an independent **`record_av1_crf` / AV1 CRF** option (0–63, default 56), separate from x264/x265 `record_crf`. Default 56 targets about half the size of x265 CRF 28; the hidden offset mapping was removed.
- Changing the menu/tray capture-copy mode among image, file, and path now **re-copies the latest screenshot** without creating another file; the status bar reports when no history exists.

#### Fixed

- The recording HUD can now move the selection by dragging anywhere along the outer 5 px ring around the red border, instead of only a narrow inner strip at the top.
- Fixed repeated Sherpa runtime installation prompts when `sherpa-onnx-c-api.dll` was missing beside the executable.
- Fixed the `Ctrl+Alt+V` voice-input toggle immediately restarting when keys were still held or injected text retriggered the hotkey. The hotkey now unregisters during shutdown and registers again after release.
- Voice recognition no longer replaces the whole HUD with “Recognizing.” The first line remains the listening status; recognition/polishing and content share the second line.
- The HUD second line now clears after polished text is inserted.

### 中文

#### 新增

- 录屏编码增加 **AV1**（`record_codec = av1`）：与 x264/x265 使用同一套采集到 FFmpeg MP4 管线；选项窗口可选；ffmpeg64 没有 AV1 编码器时明确失败，不回落到 x264。CLI：`ScreenKit --test-record-codec av1 --repeat 2`。
- 截图识别结果拆分为 **OCR / 条码** 两个 Tab：进入某个 Tab 时，若当前图片尚未执行该类型识别，则自动识别一次。
- **条码识别**改用原生 **ZXingCpp**，更快、更稳定地识别 QR、EAN、UPC、Code39/128、DataMatrix、PDF417、Aztec 等格式；难图依次尝试原图、放大图和底部区域双倍放大图。
- HTTP `POST /api/ocr`：启用 `options.ocr.barcode`（别名 `ocr.qr` / `ocr.codes`）后，响应增加 `barcodes:[{type,text,box}]`。
- 新增 **x86host** 独立 32 位本机 SAPI HTTP 助手，用于调用仅 x86 进程可见的发音人（`x86host.exe` 与 `ScreenKit.exe` 同目录）。
  - API：`GET /api/sapi/status`、`GET /api/sapi/voices`、`POST /api/sapi/synth`（WAV）、`POST /api/sapi/shutdown`。
  - 仅监听 `127.0.0.1`，默认端口 `17886`；空闲 **60 秒**后自动退出，可用 `--idle-ms` 调整。
- 主程序 TTS 按需启动 `x86host`，合并本机与 **x86 独有**发音人；选择 x86 发音人时通过 Web 合成。
- CLI `ScreenKit --list-sapi` 列出当前进程发音人，并在 x64 下通过 x86host 合并 32 位发音人。
- 离线听写使用 LLM 润色时，桌面浮窗第二行显示**润色中**和识别原文，请求完成前保持可见。
- 主菜单增加**截图 → 语音输入**，托盘增加“语音输入”，行为与 `Ctrl+Alt+V` 开始/结束听写一致。
- **语音** Tab 增加语音输入润色/自动分句、实时字幕模型模式、润色和分句设置，共用 OpenAI 兼容的 `asr_llm_*` 配置。
- 语音输入启用**自动分句**后，每个完整句子会按配置润色并立即输入。
- 听写和实时字幕润色时附带本轮已输出上文（约千字），用于纠正同音字、专有名词和指代；模型仅返回当前句。
- 语音输入增加**分句间隔**（默认 5 秒）：仅静音达到该时长才结束一句，连续说话不切句。
- 语音**识别/润色中按 Esc**会立即结束本轮、取消润色且不输出当前句；切换热键仍会正常结束并输出。OCR 识别中按 Esc 可取消当前 OCR。

#### 变更

- 前端截图识别按结果区当前 **OCR / 条码** Tab 识别文字或条码/二维码，识别前后不切换该 Tab；截图标注和屏幕画板识别入口行为一致。
- 参数设置改为**常规、识别、热键、语音、截图、接口**多个 Tab，服务模式移至“接口”。
- **离线听写**改为录制整段语音，再按切换热键后一次性识别输出，随后可选润色。
- 润色结果自动移除 `<think>` / `<thinking>` 推理块、孤立的 `</think>` 标签和整段 Markdown 代码围栏。
- **AV1 录屏**优先使用 `libsvtav1`；新增独立的 **`record_av1_crf` / AV1 CRF** 选项（0–63，默认 56），与 x264/x265 的 `record_crf` 分离。默认 56 目标体积约为 x265 CRF 28 的一半，并移除隐藏偏移映射。
- 在菜单或托盘切换截图复制模式（图片、文件、路径）时，会按新方式**重新复制最近一次截图**，不创建新文件；没有历史截图时在状态栏提示。

#### 修复

- 录屏 HUD 可在红色边框外侧 5px 整圈拖动选区，不再仅限顶部内侧狭窄区域。
- 修复 Sherpa 运行库反复提示安装：程序旁缺少 `sherpa-onnx-c-api.dll` 时会误报未安装。
- 修复 `Ctrl+Alt+V` 结束语音输入时，按键未松开或注入文字再次触发热键导致立即重启的问题；结束时先注销热键，松键后重新注册。
- 语音识别时不再用“识别中”覆盖整个浮窗；第一行保持听写状态，第二行同时显示识别/润色状态和内容。
- 润色文字输入完成后清空浮窗第二行。

## v1.0.1 (2026-08-07)

### English

#### Added

- **GIF screen record** (Capture menu / tray): region pick → HUD → **preview window** (output FPS 1–24, scale, palette colors) → save silent GIF. Capture at 24 fps; config `[gif_record]`.
- **Screenshot save options** (Settings / `config.toml`): format `png|jpg`, JPG quality 1–100, optional max width/height (fit, no upscale). Affects `screenshots/` and copy-as-file; OCR still uses full resolution.
- **Record HUD** (MP4 + GIF): draggable control bar with left grip; collapse mini bar; **Options** button before Start; icon buttons; move/resize region before and during recording (aspect lock after Start when enabled in record options).
- **Check for Updates** (Tools menu): GitHub Releases check/download, self-update via tmp copy + CLI apply.
- Install Features: clearer status badges for 未安装 / 部分 / 已安装.

#### Changed

- Main window title shows version (e.g. `ScreenKit — 截图识别 v1.0.1`).
- Record HUD: start vs pause shown as a single icon control; smoother bar dragging (lightweight move path).

#### Fixed

- Record HUD control bar could render outside the visible area on a **secondary monitor** when the capture region was near the bottom edge.

### 中文

#### 新增

- **GIF 录屏**：通过截图菜单或托盘选择区域，使用 HUD 录制，在预览窗口调整输出帧率、缩放和调色板后保存无声 GIF。采集帧率为 24 fps，配置位于 `[gif_record]`。
- **截图保存选项**：在设置和 `config.toml` 中配置 `png|jpg`、JPG 品质 1–100、可选最大宽高（等比缩小、不放大）。仅影响 `screenshots/` 和复制为文件，OCR 仍使用原始分辨率。
- **录屏 HUD**：控制条可拖动，左侧有抓手，可折叠为迷你条；开始前可打开选项；录制前后均可移动/缩放区域，录制期间可按配置锁定宽高比。
- **检查更新**：通过工具菜单从 GitHub Releases 检查、下载，并借助临时副本和 CLI 自更新。
- “安装功能”更清楚地区分未安装、部分安装和已安装状态。

#### 变更

- 主窗口标题显示版本号，例如 `ScreenKit — 截图识别 v1.0.1`。
- 录屏 HUD 将开始和暂停合并为单个控制按钮，并优化控制条拖动流畅度。

#### 修复

- 修复捕获区域靠近副屏底部时，录屏 HUD 控制条可能显示到屏幕可见范围之外的问题。

## v1.0.0 (2026-08-01)

### English

#### Added

- **In-app Install Features** window (功能组件 + 发音人 tabs):
  - OCR packs (`rapid-ch`, `rapid-i18n`, …), ASR models, FFmpeg, CUDA GPU, DirectML iGPU.
  - On-demand natives: OpenCV, Skia, PDFium, Sherpa `c-api`, **ONNX Runtime CPU (`onnxcpu64`)**.
  - TTS voice catalog with language filter; download progress shows **batch total size and downloaded bytes**.
  - CN-first download mirrors when UI/locale is Chinese (ModelScope / HF mirror / ghproxy).
  - First-run wizard defaults: OpenCV, OrtCpu, Sherpa, rapid-ch, first two ASR packs, FFmpeg (accel off).
- **Feature prompts**: missing OpenCV / OCR models / ORT / PDF stack / FFmpeg / Sherpa / ASR models offer install before use.
- **ASR / TTS / translation** UI and pipelines (sherpa-onnx, SAPI/WinRT voices, Opus-MT ONNX where configured).
- ORT load hardening: absolute-path load of real `onnxruntime.dll` before managed P/Invoke (avoids broken System32 stub without `OrtGetApiBase`).

#### Changed

- **Removed** root `install.ps1` / `install.cmd`; install models and runtimes from the app (**Tools → Install features**).
- **Screen recording is FFmpeg-only** (`ffmpeg64/`); OpenCV is not used for encode.
- **onnxcpu64** is on-demand (not shipped with every build). If neither GPU nor iGPU ORT is present, OCR prompts to install CPU ORT (~16 MB).
- Model roots fixed under program folders only: `ocrmodels/`, `asrmodels/`, `ttsmodels/`, `translatemodels/`.
- GPU (`onnxgpu64`) and DirectML (`onnxdml64`) remain optional accel installs; CPU OCR does not require them when `onnxcpu64` is installed.
- Device selection falls back to **CPU** when CUDA or DirectML is not installed / not ready.

#### Fixed

- OCR failure when System32 ships a tiny invalid `onnxruntime.dll` (EntryPointNotFound `OrtGetApiBase`).
- Misleading “DirectML missing” error on pure CPU path when no ORT package was installed.
- Recording no longer depends on OpenCV video writers.

#### Security / privacy notes

- Default HTTP bind is loopback (`127.0.0.1`).

### 中文

#### 新增

- 新增应用内“安装功能”窗口，包含功能组件和发音人 Tab：
  - OCR 模型包、ASR 模型、FFmpeg、CUDA GPU 和 DirectML 核显支持。
  - 按需安装 OpenCV、Skia、PDFium、Sherpa、**ORT CPU（`onnxcpu64`）**。
  - TTS 发音人目录支持语言筛选，下载进度显示整批总大小和已下载量。
  - 中文环境优先使用 ModelScope、Hugging Face 镜像和 ghproxy 等国内源。
  - 首次运行向导默认选择 OpenCV、OrtCpu、Sherpa、rapid-ch、前两个 ASR 包和 FFmpeg，不默认选择 GPU/核显。
- 缺少 OpenCV、OCR 模型、ORT、PDF、FFmpeg、Sherpa 或 ASR 模型时，可在使用相关功能前提示安装。
- 新增离线 ASR、TTS 和翻译界面及管线，使用 sherpa-onnx、SAPI/WinRT 发音人和可选 Opus-MT ONNX。
- 加强 ORT 加载：在托管 P/Invoke 前按绝对路径加载真实 `onnxruntime.dll`，避免 System32 无效存根缺少 `OrtGetApiBase`。

#### 变更

- 删除根目录 `install.ps1` / `install.cmd`，改用应用内**工具 → 安装功能**。
- 屏幕录制仅使用 `ffmpeg64/` 下的 FFmpeg，不再用 OpenCV 编码。
- `onnxcpu64` 改为按需安装；未安装 GPU、核显或 CPU ORT 时，OCR 会提示安装约 16 MB 的 CPU ORT。
- 模型目录固定为 Release 输出旁的 `ocrmodels/`、`asrmodels/`、`ttsmodels/`、`translatemodels/`。
- GPU `onnxgpu64` 和 DirectML `onnxdml64` 保持可选；安装 `onnxcpu64` 后，CPU OCR 不依赖它们。
- 未安装或无法使用 CUDA/DirectML 时，设备选择自动回退 CPU。

#### 修复

- 修复 System32 中存在无效小型 `onnxruntime.dll` 时，因缺少 `OrtGetApiBase` 导致 OCR 失败的问题。
- 修复纯 CPU 路径未安装 ORT 时错误提示缺少 DirectML 的问题。
- 录屏编码不再依赖 OpenCV VideoWriter。

#### 安全与隐私

- HTTP 默认仅绑定回环地址 `127.0.0.1`。

## v0.1.0 (initial milestone / 初始里程碑)

### English

#### Added

- **UI language** switch: 中文 / English via **Tools → Language** or Settings; persisted as `ui_lang` in `config.toml`.
- **Screen recording**: pick window or drag a region → red HUD → save MP4 (FFmpeg shared preferred).
- **Long screenshot**: pick a scrollable window → auto-scroll stitch → show image (no OCR).
- Top menu bar and compact toolbar; OCR progress UI; global hotkeys; WeChat-style annotate tools.
- DXGI multi-monitor capture; PDF workbench; HTTP API; device choices CPU / CUDA / DirectML.
- Capture diagnostic log switch; CLI snap / list-models / probe-cuda.
- Main window size/position restore; close hides to tray when tray mode is enabled.

#### Changed

- Main hotkey toggles window (not clipboard OCR); tray still offers clipboard OCR.
- Empty hotkey strings persist as **disabled**.
- Recording HUD chrome is outside the capture rect so it is not baked into the video.

#### Fixed

- PerMonitorV2 DPI crop issues; secondary-monitor black/stretch (DXGI).
- OCR busy state no longer blocks applying a newly captured image.
- A/V desync on screen record (WASAPI silence padding).

### 中文

#### 新增

- 界面支持中文/English 切换，可通过**工具 → 语言**或设置更改，并保存到 `config.toml` 的 `ui_lang`。
- 屏幕录制支持点选窗口或框选区域，通过红框 HUD 保存 MP4，优先使用 FFmpeg shared。
- 长截图支持选择可滚动窗口、自动滚动拼接并显示图片，不自动 OCR。
- 新增顶部菜单、紧凑工具栏、OCR 进度、全局热键和微信式截图标注工具。
- 支持 DXGI 多显示器捕获、PDF 工作台、HTTP API 以及 CPU/CUDA/DirectML 设备选择。
- 增加捕获诊断日志开关，以及截图、列模型、探测 CUDA 等 CLI 命令。
- 恢复主窗口尺寸和位置；启用托盘模式时，关闭窗口会隐藏到托盘。

#### 变更

- 主热键改为显示/隐藏窗口，不再执行剪贴板 OCR；托盘仍提供剪贴板 OCR。
- 空热键配置保持为**禁用**状态。
- 录屏 HUD 外框位于捕获区域之外，不会被录入视频。

#### 修复

- 修复 PerMonitorV2 DPI 裁剪问题和副屏 DXGI 黑屏/拉伸问题。
- OCR 忙碌状态不再阻止应用新截取的图片。
- 修复屏幕录制 WASAPI 静音填充导致的音画不同步。
