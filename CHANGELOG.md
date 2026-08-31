# Changelog / 更新日志

All notable changes to ScreenKit are documented here. / 本文件记录 ScreenKit 的重要变更。

Format based on [Keep a Changelog](https://keepachangelog.com/). Versions are project milestones. / 格式基于 Keep a Changelog，版本号表示项目里程碑。

## unreleased

### English

#### Added

- LLM endpoint **thinking intensity** adds a **medium** option and now displays raw API values `none / low / medium / high / max` (config values unchanged; `none` still maps to `off` internally, `medium` / `mid` / `中` are kept instead of falling back to `low`).
- Edit menu: **Copy file** — copies the current image as a file (saved to `screenshots/` first, FileDrop on clipboard; paste in Explorer).
- Edit menu: **Copy path** — copies the current image's file path. Images opened from a file copy the source path; screenshots / pasted images are saved to `screenshots/` first and the path text is copied (persist OLE text).

#### Changed

- Edit menu **Copy image** now copies the bitmap to the clipboard (previously copied as file; file copy moved to the new Copy file item). Toolbar button follows.
- Edit menu: removed **Copy text** (still available via Ctrl+C and the result-panel copy button).
- README screenshots recaptured: `docs/1 screenshot.png` (Chinese) and `docs/1 screenshot.en.png` (English), including translated Pack/Lang names.
- English README OCR feature notes Korean/English word-space restoration (already in the Chinese README).

#### Fixed

- English UI: OCR **Pack** / **Lang** combos used Chinese labels (`Rapid 全语种`, `Umi-OCR（多语言）`, `简体中文`, …). Display names now come from `ocr-display.json` (`name` / `nameEn`) and optional per-pack `pack.json`.

### 中文

#### 新增

- LLM 接口 **思考强度** 新增 **medium** 档，且下拉直接显示 API 原值 `none / low / medium / high / max`（配置值不变；`none` 内部仍按 `off` 关思考，`medium` / `mid` / `中` 不再回落到 `low`）。
- 「编辑」菜单新增 **复制文件**：当前图片先存 `screenshots/`，以文件（FileDrop）形式复制到剪贴板，可在资源管理器 Ctrl+V 粘贴。
- 「编辑」菜单新增 **复制路径**：复制当前图片的文件路径。打开的图片复制源文件路径；截图 / 粘贴的图先存 `screenshots/` 再复制路径文本（persist OLE 文本）。

#### 变更

- 「编辑」菜单 **复制图片** 改为复制位图到剪贴板（原先复制为文件；文件复制移到新增的「复制文件」），工具栏“复制图”按钮同步。
- 「编辑」菜单移除 **复制文字**（仍可用 Ctrl+C 与结果区“复制”按钮）。
- README 截图已重截：`docs/1 screenshot.png`（中文）与 `docs/1 screenshot.en.png`（英文），含已翻译的 Pack/Lang 显示名。
- 英文 README 截图识别说明补回韩语/英语词间空格处理（中文 README 已有）。

#### 修复

- 英文界面顶栏 **Pack / Lang** 下拉仍显示中文包名与变体（Rapid 全语种、Umi-OCR（多语言）、简体中文 等）。现从 `ocr-display.json` 的 `name` / `nameEn`（以及包内可选 `pack.json`）读取显示名。

## v1.0.4 (2026-08-31)

### English

#### Added

- LLM translate / polish **continues** when `finish_reason` is `length` / `max_tokens` (up to 6 extra rounds, remaining timeout). Overlapping chunk tails are stripped. CLI: `ScreenKit --test-llm-continue`.
- HTTP `POST /api/translate` (alias `/api/translate/batch`): LLM batch translate (no item-count cap; groups of 8, missing indexes retried one-by-one). `GET /api/status` includes `llm_translate`.
- OCR tab: target-language combo (default **no translation**) and a **Translate** toggle. After OCR, if a language is set, the toggle turns on and overlay text is replaced with the translation (`ocr_translate_lang`). The result panel follows the same toggle (original vs translated) and shows translation elapsed time in the result meta.
- Floating **translate popup** (always on top). Global hotkey **Ctrl+Alt+T** shows/hides it (empty = off). First open is empty; later opens keep the last source/translation. Paste is a button. Also: Tools menu, tray. Uses the Translate-tab engine (LLM or local ONNX). Esc hides; Ctrl+Enter translates.

#### Changed

- LLM translate source/target combos list **many languages** (zh/en/ja/ko plus French, German, Spanish, Russian, Arabic, Thai, …; Traditional Chinese and Cantonese). Local ONNX still only lists installed model pairs. OCR overlay dest uses the same list. HTTP `src`/`dst` accept these codes.
- Release build does **not** copy or junction model / ORT / FFmpeg folders. Place `ocrmodels`, `asrmodels`, `ttsmodels`, `translatemodels`, `facemodels`, `onnxcpu64`, `onnxgpu64`, `onnxdml64`, and `ffmpeg64` under `bin/Release/net48/` yourself (junctions are fine). NuGet GPU/DirectML natives are no longer copied into the output.
- Chinese window / tray title is **屏幕截图工具** (English remains ScreenKit).
- README screenshots updated: `docs/1 screenshot.png` (Chinese) and `docs/1 screenshot.en.png` (English), including overlay dest language and the Translate toggle.
- Settings **LLM** tab hints recommend small models (Qwen3.5-4B / Qwen3-8B / Hunyuan-MT-7B) and thinking **Off** for polish and translation.

#### Fixed

- **Ctrl+Alt+T** now hides the translate popup whenever it is already visible (not only when it is the active window). The hotkey is registered on the main window, so the popup often lost `IsActive` before the handler ran and the second press only brought it to the front.
- Switching the UI language now updates Translate tab hints/status, the translate popup, round-trip dialog strings, and the OCR “translate Xs” suffix (Loc zh/en). ASR/TTS/OCR runtime status lines are still mostly Chinese.
- Copy-as-path after a screenshot sometimes left the clipboard empty: `OleSetClipboard(null)` then Win32 text then `OleFlushClipboard` emptied the text (Flush calls `EmptyClipboard` on a null OLE object). Path copy now sets a persist OLE text DataObject. CLI: `ScreenKit --test-clipboard-path` also covers re-copy last screenshot as path.

### 中文

#### 新增

- LLM 翻译 / 润色在 `finish_reason` 为 `length` / `max_tokens` 时**超长续写**（最多再 6 轮，受剩余超时限制），去掉片段重叠。CLI：`ScreenKit --test-llm-continue`。
- HTTP `POST /api/translate`（别名 `/api/translate/batch`）：LLM 批量翻译，**不限制条数**（每批 8 条编号请求，缺号逐条补）。`GET /api/status` 增加 `llm_translate`。
- 截图识别页：目标语言下拉（默认**不翻译**）+ **翻译**开关。识别完成时若已选语言则默认打开，图上叠字换成译文（`ocr_translate_lang`）。右侧识别结果随开关显示原文/译文，并在结果 meta 中显示翻译用时。
- **翻译小窗**（置顶浮窗）。全局热键 **Ctrl+Alt+T** 呼出/隐藏（留空禁用）。首次打开为空，之后保留上次原文/译文；剪贴板需点「粘贴」。入口：工具菜单、托盘。引擎与翻译 Tab 相同（LLM 或本地 ONNX）。Esc 隐藏；Ctrl+Enter 翻译。

#### 变更

- LLM 翻译源/目标语言下列出**多种语言**（中/英/日/韩，以及法/德/西/俄/阿/泰等；含繁体中文与粤语）。本地 ONNX 仍只列出已装模型方向。OCR 叠字目标语同一列表。HTTP `src`/`dst` 接受这些代码。
- Release 编译**不再**复制或联接模型 / ORT / FFmpeg 目录。请自行把 `ocrmodels`、`asrmodels`、`ttsmodels`、`translatemodels`、`facemodels`、`onnxcpu64`、`onnxgpu64`、`onnxdml64`、`ffmpeg64` 放到 `bin/Release/net48/`（目录联接即可）。NuGet 的 GPU/DirectML 原生库也不再拷进输出。
- 中文界面程序标题改为**屏幕截图工具**（英文仍为 ScreenKit）。
- README 截图已重截：`docs/1 screenshot.png`（中文）与 `docs/1 screenshot.en.png`（英文），含叠字目标语和「翻译」开关。
- 参数设置 **LLM接口** 增加提示：润色/翻译推荐小模型（Qwen3.5-4B / Qwen3-8B / Hunyuan-MT-7B），思考选关闭。

#### 修复

- **Ctrl+Alt+T** 在翻译小窗已显示时一律隐藏（不再要求窗口在前台）。热键挂在主窗上，第二次按下时小窗往往已经不是 `IsActive`，原先只会把它拉到前面。
- 切换界面语言时，翻译 Tab 提示/状态、翻译小窗、来回翻译对话框，以及 OCR 结果里的「翻译 Xs」会跟着变（中/英）。ASR/TTS/OCR 过程状态仍多为中文。
- **截图复制为路径**有时截图完剪贴板是空的：`OleSetClipboard(null)` 后再 Win32 写文本并 `OleFlushClipboard`，Flush 会对空 OLE 对象执行 `EmptyClipboard`，刚写入的路径被清掉。现改为 persist 的 OLE 文本 DataObject。CLI：`ScreenKit --test-clipboard-path` 同时覆盖「重新复制为路径」。

## v1.0.3 (2026-08-30)

### English

#### Added

- Main window **Face** tab: InsightFace ONNX detect/recognize/compare. Models live in `facemodels/` next to the exe (Release build junctions the central library). CLI: `ScreenKit --list-face`.
- HTTP `GET /api/face/models` and `POST /api/face` (extract one image or compare two). Install Features can download InsightFace **buffalo_l** into `facemodels/`.
- Settings **LLM** tab: multiple OpenAI-compatible endpoints as TOML `[[llm]]` (`name` / `url` / `key` / `model` / `think`). Speech tab picks one by display name (`asr_llm`); polish prompt stays on Speech. Display name defaults to model id. Old `asr_llm_url` / `asr_llm_token` / `asr_llm_model` keys are ignored (re-enter in the new UI). **Copy** duplicates the selected endpoint.
- Per-endpoint **thinking intensity** (`think`: `off` / `low` / `high` / `max`, default `low`). `off` sends `thinking.type=disabled`; `low`/`high`/`max` send `thinking.type=enabled` plus `reasoning_effort` (GLM-5.3 cannot disable thinking). If `off` is rejected, retry with `low`; other HTTP 400s drop think fields.
- Translate tab can use a configured **LLM** instead of local Opus-MT (`translate_llm`). The LLM prompt (`translate_llm_prompt`, `{src}`/`{dst}`) is edited in **Settings → Translate**. LLM also supports 来回翻译 without a reverse ONNX pair.
- Project, folder, namespace, mutex, HTTP `app`, PDF drafts (`%LocalAppData%\ScreenKit\drafts`), and self-update (GitHub `ScreenKit` / `screenkit_*.7z` / `ScreenKit.exe`) are all **ScreenKit**. The old `%LocalAppData%\WpfOCR\drafts` folder is unused.

#### Changed

- README screenshot: English UI in `docs/1 screenshot.en.png`; Chinese README uses an updated `docs/1 screenshot.png`.
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

- 主界面增加 **人脸识别** Tab：InsightFace ONNX 检测/识别/比对。模型放在程序旁 `facemodels/`（Release 编译联接到中央库）。CLI：`ScreenKit --list-face`。
- HTTP `GET /api/face/models`、`POST /api/face`（单图提取或两图比对）。「安装功能」可下载 InsightFace **buffalo_l** 到 `facemodels/`。
- 参数设置增加 **LLM接口** Tab：多套 OpenAI 兼容接口以 TOML `[[llm]]` 数组保存（`name` / `url` / `key` / `model` / `think`）。语音 Tab 用显示名称选择润色接口（`asr_llm`），提示词仍在语音。显示名称默认等于模型 id。旧键 `asr_llm_url` / `asr_llm_token` / `asr_llm_model` 不再读取，需在新界面重新填写。列表可**复制**当前接口。
- 每条 LLM 可配 **思考强度**（`think`：`off` / `low` / `high` / `max`，默认 `low`）。`off` 发关闭思考；`low`/`high`/`max` 发 `thinking.type=enabled` 与 `reasoning_effort`（GLM-5.3 不能关思考）。若 `off` 被拒绝则改 `low` 再试；其它 400 再去掉思考字段。
- **翻译**可选用已配置的 LLM（`translate_llm`），不必装 Opus-MT。LLM 提示词（`translate_llm_prompt`，`{src}`/`{dst}`）在**参数设置 → 翻译**编辑。LLM 下来回翻译也不需要反向 ONNX 模型。
- 工程目录、csproj、命名空间、Mutex、HTTP `app`、PDF 草稿（`%LocalAppData%\ScreenKit\drafts`）与自更新（GitHub `ScreenKit` 仓库 / `screenkit_*.7z` / `ScreenKit.exe`）全部为 **ScreenKit**。不再使用 `%LocalAppData%\WpfOCR\drafts`。

#### 变更

- README 增加英文界面截图 `docs/1 screenshot.en.png`；中文 README 截图更新为当前 ScreenKit 主界面。
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
- Added **x86host**, a standalone 32-bit local SAPI HTTP helper for voices visible only to x86 processes.
  - Project: `x86host/` produces `x86host.exe` beside `ScreenKit.exe`; ScreenKit Release builds compile and copy it automatically.
  - API: `GET /api/sapi/status`, `GET /api/sapi/voices`, `POST /api/sapi/synth` (WAV), and `POST /api/sapi/shutdown`.
  - Listens only on `127.0.0.1`, uses port `17886` by default, and exits after **60 seconds idle** (configurable with `--idle-ms`).
  - Has no GUI or OCR/ASR features and no extra NuGet dependencies, avoiding DLL conflicts with the main application.
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
- Replaced the secondary `WpfOCR.x86.exe` build with standalone `x86host.exe` for 32-bit SAPI support.
- Changing the menu/tray capture-copy mode among image, file, and path now **re-copies the latest screenshot** without creating another file; the status bar reports when no history exists.

#### Fixed

- The recording HUD can now move the selection by dragging anywhere along the outer 5 px ring around the red border, instead of only a narrow inner strip at the top.
- Fixed repeated Sherpa runtime installation prompts caused by Release builds removing `sherpa-onnx-c-api.dll` from `net48`. Development builds now keep it beside the executable; slim packages still install it on demand.
- Fixed the `Ctrl+Alt+V` voice-input toggle immediately restarting when keys were still held or injected text retriggered the hotkey. The hotkey now unregisters during shutdown and registers again after release.
- Voice recognition no longer replaces the whole HUD with “Recognizing.” The first line remains the listening status; recognition/polishing and content share the second line.
- The HUD second line now clears after polished text is inserted.

### 中文

#### 新增

- 录屏编码增加 **AV1**（`record_codec = av1`）：与 x264/x265 使用同一套采集到 FFmpeg MP4 管线；选项窗口可选；ffmpeg64 没有 AV1 编码器时明确失败，不回落到 x264。CLI：`ScreenKit --test-record-codec av1 --repeat 2`。
- 截图识别结果拆分为 **OCR / 条码** 两个 Tab：进入某个 Tab 时，若当前图片尚未执行该类型识别，则自动识别一次。
- **条码识别**改用原生 **ZXingCpp**，更快、更稳定地识别 QR、EAN、UPC、Code39/128、DataMatrix、PDF417、Aztec 等格式；难图依次尝试原图、放大图和底部区域双倍放大图。
- HTTP `POST /api/ocr`：启用 `options.ocr.barcode`（别名 `ocr.qr` / `ocr.codes`）后，响应增加 `barcodes:[{type,text,box}]`。
- 新增 **x86host** 独立 32 位本机 SAPI HTTP 助手，用于调用仅 x86 进程可见的发音人。
  - 工程 `x86host/` 生成与 `ScreenKit.exe` 同目录的 `x86host.exe`；ScreenKit Release 编译时自动编译并复制。
  - API：`GET /api/sapi/status`、`GET /api/sapi/voices`、`POST /api/sapi/synth`（WAV）、`POST /api/sapi/shutdown`。
  - 仅监听 `127.0.0.1`，默认端口 `17886`；空闲 **60 秒**后自动退出，可用 `--idle-ms` 调整。
  - 无 GUI、OCR 或 ASR 功能，也无额外 NuGet 依赖，避免与主程序 DLL 冲突。
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
- 32 位 SAPI 支持改用独立 `x86host.exe`，不再二次编译 `WpfOCR.x86.exe` 旁路包。
- 在菜单或托盘切换截图复制模式（图片、文件、路径）时，会按新方式**重新复制最近一次截图**，不创建新文件；没有历史截图时在状态栏提示。

#### 修复

- 录屏 HUD 可在红色边框外侧 5px 整圈拖动选区，不再仅限顶部内侧狭窄区域。
- 修复 Sherpa 运行库反复提示安装：Release 编译曾从 `net48` 输出目录移除 `sherpa-onnx-c-api.dll`。开发输出现在保留该 DLL；精简包仍按需安装。
- 修复 `Ctrl+Alt+V` 结束语音输入时，按键未松开或注入文字再次触发热键导致立即重启的问题；结束时先注销热键，松键后重新注册。
- 语音识别时不再用“识别中”覆盖整个浮窗；第一行保持听写状态，第二行同时显示识别/润色状态和内容。
- 润色文字输入完成后清空浮窗第二行。

## v1.0.1 (2026-08-07)

### English

#### Added

- **Publish script**: `node scripts/publish-release.mjs` → `release/screenkit_<version>.7z` (slim Release package; `release/` gitignored).
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

- **发布脚本**：`node scripts/publish-release.mjs` 生成 `release/screenkit_<version>.7z` 精简发布包，`release/` 已忽略提交。
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
- Target framework **.NET Framework 4.8** (`net48`); output under `bin/Release/net48/`.
- ORT load hardening: absolute-path load of real `onnxruntime.dll` before managed P/Invoke (avoids broken System32 stub without `OrtGetApiBase`).

#### Changed

- **Removed** root `install.ps1` / `install.cmd`; install models and runtimes from the app (**Tools → Install features**).
- **Screen recording is FFmpeg-only** (`ffmpeg64/`); OpenCV `videoio` / ffmpeg DLLs are stripped after build and not used for encode.
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
- Do not commit `config.toml`, capture logs, private screenshots, recorded videos, API keys, or absolute machine paths.
- Do not commit large ONNX weights, CUDA redistributables, or FFmpeg shared binaries without a redistribution plan.

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
- 目标框架改为 **.NET Framework 4.8**（`net48`），输出到 `bin/Release/net48/`。
- 加强 ORT 加载：在托管 P/Invoke 前按绝对路径加载真实 `onnxruntime.dll`，避免 System32 无效存根缺少 `OrtGetApiBase`。

#### 变更

- 删除根目录 `install.ps1` / `install.cmd`，改用应用内**工具 → 安装功能**。
- 屏幕录制仅使用 `ffmpeg64/` 下的 FFmpeg，不再使用 OpenCV videoio/ffmpeg DLL 编码。
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
- 不应提交 `config.toml`、截图日志、私密截图、录屏、API 密钥或本机绝对路径。
- 未制定再分发方案前，不应提交大型 ONNX 权重、CUDA 可再发行组件或 FFmpeg 共享库。

## v0.1.0 (initial milestone / 初始里程碑)

### English

#### Added

- **MIT License** for application source ([LICENSE](LICENSE)); third-party notes in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
- **UI language** switch: 中文 / English via **Tools → Language** or Settings; persisted as `ui_lang` in `config.toml`.
- **Screen recording**: pick window or drag a region → red HUD → save MP4 (FFmpeg shared preferred).
- **Long screenshot**: pick a scrollable window → auto-scroll stitch → show image (no OCR).
- Top menu bar and compact toolbar; OCR progress UI; global hotkeys; WeChat-style annotate tools.
- DXGI multi-monitor capture; PDF workbench; HTTP API; device choices CPU / CUDA / DirectML.
- Capture diagnostic log switch; CLI snap / list-models / probe-cuda.
- Main window size/position restore; close hides to tray when tray mode is enabled.

#### Changed

- Main hotkey toggles window (not clipboard OCR); tray still offers clipboard OCR.
- Model packs live under Release output `ocrmodels/` only (not under source tree).
- Empty hotkey strings persist as **disabled**.
- Recording HUD chrome is outside the capture rect so it is not baked into the video.

#### Fixed

- PerMonitorV2 DPI crop issues; secondary-monitor black/stretch (DXGI).
- OCR busy state no longer blocks applying a newly captured image.
- A/V desync on screen record (WASAPI silence padding).

### 中文

#### 新增

- 应用源码采用 **MIT License**（见 [LICENSE](LICENSE)），第三方声明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
- 界面支持中文/English 切换，可通过**工具 → 语言**或设置更改，并保存到 `config.toml` 的 `ui_lang`。
- 屏幕录制支持点选窗口或框选区域，通过红框 HUD 保存 MP4，优先使用 FFmpeg shared。
- 长截图支持选择可滚动窗口、自动滚动拼接并显示图片，不自动 OCR。
- 新增顶部菜单、紧凑工具栏、OCR 进度、全局热键和微信式截图标注工具。
- 支持 DXGI 多显示器捕获、PDF 工作台、HTTP API 以及 CPU/CUDA/DirectML 设备选择。
- 增加捕获诊断日志开关，以及截图、列模型、探测 CUDA 等 CLI 命令。
- 恢复主窗口尺寸和位置；启用托盘模式时，关闭窗口会隐藏到托盘。

#### 变更

- 主热键改为显示/隐藏窗口，不再执行剪贴板 OCR；托盘仍提供剪贴板 OCR。
- 模型包仅存放于 Release 输出目录的 `ocrmodels/`，不再放在源码目录。
- 空热键配置保持为**禁用**状态。
- 录屏 HUD 外框位于捕获区域之外，不会被录入视频。

#### 修复

- 修复 PerMonitorV2 DPI 裁剪问题和副屏 DXGI 黑屏/拉伸问题。
- OCR 忙碌状态不再阻止应用新截取的图片。
- 修复屏幕录制 WASAPI 静音填充导致的音画不同步。
