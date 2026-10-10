# Changelog / 更新日志

All notable changes to ScreenKit are documented here. / 本文件记录 ScreenKit 的重要变更。

Format based on [Keep a Changelog](https://keepachangelog.com/). Versions are project milestones. / 格式基于 Keep a Changelog，版本号表示项目里程碑。

Each version has matching **English** and **中文** sections. GitHub Release notes follow the same bilingual order. / 每个版本同时有英文与中文章节；GitHub Release 说明同样先英后中。

## Versions / 版本索引

- [unreleased](#unreleased)
- [v1.0.17 (2026-10-09 ~ 10-10)](#v1017-2026-10-09--10-10)
- [v1.0.16 (2026-10-08 ~ 10-09)](#v1016-2026-10-08--10-09)
- [v1.0.15 (2026-10-05 ~ 10-06)](#v1015-2026-10-05--10-06)
- [v1.0.14 (2026-10-04 ~ 10-05)](#v1014-2026-10-04--10-05)
- [v1.0.13 (2026-09-27 ~ 10-03)](#v1013-2026-09-27--10-03)
- [v1.0.12 (2026-09-26)](#v1012-2026-09-26)
- [v1.0.11 (2026-09-26)](#v1011-2026-09-26)
- [v1.0.10 (2026-09-24)](#v1010-2026-09-24)
- [v1.0.9 (2026-09-23)](#v109-2026-09-23)
- [v1.0.8 (2026-09-23)](#v108-2026-09-23)
- [v1.0.7 (2026-09-11)](#v107-2026-09-11)
- [v1.0.6 (2026-09-10)](#v106-2026-09-10)
- [v1.0.5 (2026-08-31 ~ 09-01)](#v105-2026-08-31--09-01)
- [v1.0.4 (2026-08-31)](#v104-2026-08-31)
- [v1.0.3 (2026-08-30)](#v103-2026-08-30)
- [v1.0.2 (2026-08-26)](#v102-2026-08-26)
- [v1.0.1 (2026-08-07)](#v101-2026-08-07)
- [v1.0.0 (2026-08-01)](#v100-2026-08-01)
- [v0.1.0](#v010-initial-milestone--初始里程碑)

## unreleased

### English

#### Fixed

- The tools page left categories and the search box now hide cards that do not match. A stylesheet `display` rule had been covering the `hidden` attribute, so the list never changed. Back and the in-tool favorite button stay off the home list for the same reason.

#### Changed

- Tray → Tools → Screencast opens the main window on the Screencast tab. If that tab was hidden, it is shown again.

### 中文

#### 修复

- 工具页左侧分类和搜索会藏起不匹配的卡片。样式里的 `display` 盖过了 `hidden`，所以列表看起来一直不变。返回和工具内的收藏按钮在首页同样不再露出来。

#### 变更

- 托盘 → 工具 → 投屏打开主窗口并切到投屏页。该页被隐藏时会重新显示。

## v1.0.17 (2026-10-09 ~ 10-10)

### English

#### Added

- `GET /` is the local tools page: categories on the left, four to six cards on a row, search, and favorites stored in this browser. A tool can also be starred from its own page. About sixty in-browser tools cover timestamps, units, color, hashes, text, and calculators. Cards are ordered by common use, with desktop tools first. OCR, speech synthesis, speech recognition, and QR or barcodes use the same engines as the main window.
- The tools page and the desktop file manager use Font Awesome icons. The phone file page keeps its own icons.
- Simplified/Traditional, calendars, and Japanese yomi have a window, HTTP, and CLI. These paths stay available when other HTTP modules are off. The Simplified/Traditional window keeps simplified text on top and traditional text below.
- Toast has HTTP and CLI. The overlay does not take focus.
- Network tools can show this PC's location. **Map** opens Amap. Inside China the pin uses GCJ-02. The OpenStreetMap link keeps WGS84.
- Windows speech recognition works without a Sherpa model, for files, recordings, hotkey dictation, live captions, subtitles, CLI, and HTTP. It does not stream partial text. The offline-model help icon says Windows recognition is weak for Chinese and suggests a Sherpa model such as SenseVoice.
- **Help → Install features** lists OCR, speech, and recognition in one language tree and uses one administrator prompt. The same window can install the small Opus-MT packs and removes only those packs on uninstall.
- The update window lists the notes for each version after the one that is installed.

#### Changed

- The desktop file manager moved to `/files`. The phone page stays `/m`. Tray **Web home** and **Tools → Web home** open the tools page and do not show the main window. If HTTP is not running, a toast says why.
- `POST /api/qrmake` and `POST /api/qrscan` stay available when other HTTP modules are off. Generate draws the code under the button. Style and script URLs carry the startup version and still cache for one hour. HTML pages are not cached.
- A new install uses Windows OCR, Windows speech, and Windows speech recognition. A saved choice is kept. CLI `-p` defaults to `winocr`. Speech still uses Edge online when Windows has no voice. Windows OCR no longer needs OpenCV. ONNX OCR, barcodes, and long screenshots still do. Startup no longer opens Install features. Installed Windows packs are checked once after startup. Refresh still checks every pack.
- The Speak button stays enabled during playback. Clicking it again restarts from the beginning.
- HTTP, the tools page, the file manager, and cast share one TCP listener. Opening the LAN address no longer needs an administrator or an HTTP.sys URL reservation.
- `GET /api/tts/engines` lists engines without scanning models. `GET /api/tts/models` caches the last full list. `refresh=1` scans again.
- The HTTP tab **Send** button sits on the method and path row. The status bar opens Memory from the model text and the web home from the HTTP text.
- Tray click follows the main-window hotkey, judged on mouse-down from the window that was in front. A second click during the show animation hides the window. The main-window hotkey and the dictionary hotkey show or hide by the foreground window. The dictionary hotkey opens the dictionary only when it shows the window, and searches only a one-word clipboard.
- Install features can follow the system proxy, use a typed address, or use no proxy. With a proxy, GitHub and Hugging Face come first. Without one, China mirrors come first. A failed address is still followed by the others. `.cn` hosts stay direct. In a Chinese locale, a mirror that fails, times out after 30 seconds, or returns an HTML page is skipped, and the original URL is still tried. Update checks and the TTS package list use the same order.
- FFmpeg 4.4 shared comes from the fixed `dict-db` package. The old BtbN zip URLs are gone.

#### Fixed

- Calendars and Japanese yomi run on an STA thread. HTTP query text is read as UTF-8.
- A low-confidence Windows speech result is no longer dropped. The best rejected candidate is kept.
- Dictionary entries keep every sense, example, and idiom. English `run` keeps its phrasal verbs, including `come running`.

### 中文

#### 新增

- `GET /` 是本机工具页：左边分类，右边一行 4 到 6 个，可搜索，收藏记在这个浏览器里。进入工具后也可以收藏。约 60 个浏览器内小工具，包括时间戳、单位、颜色、哈希、文本和计算器。卡片按常用程度排列，桌面程序里已有的功能在前。文字识别、语音合成、语音识别、二维码和条码的引擎选法与主窗口相同。
- 工具页和电脑版文件管理改用 Font Awesome 图标。手机版文件页仍用原来的图标。
- 简繁转换、历法、日文注音有窗口、HTTP 和命令行，其它 HTTP 模块关掉时仍可用。简繁窗口上面是简体，下面是繁体。
- Toast 增加 HTTP 和命令行。浮层不抢焦点。
- 网络工具可显示本机位置。**查看地图**打开高德。国内标点用 GCJ-02。OpenStreetMap 链接保持 WGS84。
- 可不装 Sherpa，直接用 Windows 语音识别，用于文件、录音、热键听写、实时字幕、批量字幕、命令行和 HTTP。没有半句流式结果。离线模型旁的帮助图标说明 Windows 中文识别较差，建议 SenseVoice 等 Sherpa 模型。
- **帮助 → 安装功能**把 OCR、语音和识别放在同一棵语言树里，只弹出一次管理员确认。同一窗口可安装小型 Opus-MT 翻译包，卸载只删除这两个包。
- 检查更新的窗口会列出当前版本之后每个版本的说明。

#### 变更

- 电脑版文件管理改到 `/files`。手机版仍是 `/m`。托盘 **web主页** 和 **工具 → web主页** 打开工具页，不唤起主窗口。HTTP 没在跑时，底部提示原因。
- `POST /api/qrmake` 和 `POST /api/qrscan` 在其它 HTTP 模块关掉时仍可用。生成按钮在下方画出码。样式和脚本地址带本次启动版本，同一次运行仍缓存 1 小时。HTML 页面不缓存。
- 新安装默认 Windows OCR、Windows 语音和 Windows 语音识别。已经保存的选择保持不变。命令行 `-p` 默认 `winocr`。没有 Windows 发音人时仍用 Edge 在线。Windows OCR 不再需要 OpenCV。ONNX 识别、条码和长截图仍需要。启动时不再打开安装功能。启动后只检查一次已安装的 Windows 功能包。刷新仍检查全部。
- 朗读过程中「朗读」按钮保持可点。再点一次会停止并从头重读。
- HTTP、工具页、文件管理和投屏共用一个 TCP 监听。开局域网不再需要管理员，也不需要 HTTP.sys 的 URL 预留。
- `GET /api/tts/engines` 只列引擎，不扫描模型。`GET /api/tts/models` 记住上次的全量结果。`refresh=1` 重新扫描。
- HTTP 接口页的**发送**改到方法和路径那一行。点状态栏的模型文字打开内存占用，点 HTTP 文字打开 web 主页。
- 单击托盘与主窗口热键相同，在按下时按点击前的前台窗口判断。显示动画还没播完时再点一次会隐藏。主窗口热键和词典热键按前台窗口决定显示或隐藏。词典热键只在唤出时打开词典，剪贴板像一个单词才搜索。
- 安装功能可跟随系统代理、手填地址，或不用代理。用了代理时先下 GitHub / Hugging Face；不用时先下国内镜像。前面的地址失败后仍会试后面的。`.cn` 仍然直连。中文环境下，镜像失败、30 秒没有响应，或返回网页时会跳过，并仍然尝试原来的地址。检查更新和发音人列表用同一顺序。
- FFmpeg 4.4 shared 改从固定的 `dict-db` 包下载。原先 BtbN 的 zip 地址已失效。

#### 修复

- 历法和日文注音改在 STA 线程上运行。HTTP 查询文字按 UTF-8 读取。
- Windows 识别不再因置信度低而返回空文字，会留下被拒结果里的最佳候选。
- 词典词条保留全部义项、例句和惯用语。英语 `run` 的词组还在，包括 `come running`。

## v1.0.16 (2026-10-08 ~ 10-09)

### English

#### Changed

- HTTP proxy follows the Windows system proxy whenever that proxy is enabled (`ProxyEnable`). The address in Settings is used only when the system proxy is off and Enable is checked. Local, private, `.cn`, and China-mirror hosts still go direct.
- HTTP `POST /api/ocr` can select **Windows OCR** with `ocr.engine=winocr` or `ocr.language=winocr`, without changing the main window. `ocr.language` is then a BCP-47 tag or a language name (`zh-Hans-CN`, `en-US`, `英语`). `ocr.maxSideLen` still caps the long side. `ocr.engine=onnx` switches back to an ONNX pack when the main window is Windows OCR. `GET /api/ocr/get_options` lists `ocr.engine`.
- Settings → Recognition shows what the selected engine does. Windows OCR: no download, one installed language, upright boxes, no rotation, confidence, or GPU; the side-length cap still applies. Umi uses larger server models (Simplified Chinese, English, Traditional Chinese, Japanese, Korean, Russian). Rapid mobile Chinese is Simplified Chinese only. Rapid all-languages adds Latin and Arabic. ONNX packs keep detection, rotation, recognition, device choice, thresholds, and a confidence score.
- Screenshot OCR can use **Windows OCR** (`model_pack = winocr`). It does not load an ONNX model. It is faster, and the result cannot stay stable. The choice is saved as `win_ocr_langs`.
- Web manager **Send to PC** copies the text to the clipboard and shows a toast `Copied text: …`. Clearing the text still only clears the File sync box.
- With HTTP enabled in Settings, a LAN address already holding the port no longer makes the HTTP tab say the API is disabled. This PC still listens on `127.0.0.1` and the tab shows that address.
- UI languages are toml files in `lang/` next to the program, one file per language. Startup reads every file. The language menu and Settings list Chinese, English, Japanese, Korean, and other languages (German, Spanish, French, Portuguese, Russian).
- The bottom status bar says `未加载模型` / `No model loaded` when no model is loaded.
- The bottom status bar keeps the HTTP listener next to the memory summary: `HTTP 0.0.0.0:1224` or `HTTP 127.0.0.1:1224` while the API listener is up, `HTTP LAN:1224` when only the per-NIC file-transfer sockets are up, `HTTP not started` (with the error) when it should be listening but is not, and `HTTP off` when the API, file transfer, and cast are all disabled.
- Korean dictionary phrases have the same speak button as examples. It reads the Korean phrase. The Chinese gloss on the next line is not spoken.
- **Help → Install features → Windows speech**: when ScreenKit is not an administrator, install or remove uses `start` to open one administrator window for the selected packs. Allow it in User Account Control. Exit 0 and 3010 still count as success. The DISM command can still be copied.
- **Help → Install features → Windows speech**: installed packs and voices are read only when this tab is opened, or when Refresh is clicked. When ScreenKit is not an administrator, that read uses `start` the same way as install or remove: one administrator window. Denying User Account Control leaves pack status blank and still shows voices this process can see.

#### Fixed

- Dictionary entries show every sense, example, and idiom. English `run` keeps its phrasal verbs (about 60, including `come running`) instead of stopping after 16 senses.
- Web manager **Stay signed in** still works after this program restarts. The browser keeps the 30-day cookie and saved token, retries while the PC is starting, and an old tab token no longer hides that saved login.
- Dictionary selection popup: hovering a hit keeps dark text on a light blue row. The row no longer turns the text white on a faint highlight.
- Double-clicking a word in a dictionary entry opens the selection popup. Dragging a selection and right-click still open it.
- Sending a file whose name contains Chinese (for example `XPlayer v2.9.0.0 高级版.apk`) to the phone no longer fails with “路径中具有非法字符”. The shared HTTP port decodes the download query as UTF-8.
- When the HTTP service fails to start, ScreenKit retries every 10 seconds until it listens, or until the API, file transfer, and cast are all turned off. The status bar keeps the error until a retry succeeds.

### 中文

#### 变更

- HTTP 代理在 Windows 系统代理开着时总是用系统代理（`ProxyEnable`）。系统代理关掉、并且勾选启用时，才用参数里的地址。本机、内网、`.cn` 和国内镜像仍然直连。
- HTTP `POST /api/ocr` 可用 `ocr.engine=winocr` 或 `ocr.language=winocr` 指定 **Windows 系统 OCR**，不必改主窗。这时 `ocr.language` 是 BCP-47 或语言名（`zh-Hans-CN`、`en-US`、`英语`）。`ocr.maxSideLen` 仍限制长边。主窗已是系统 OCR 时，`ocr.engine=onnx` 改回 ONNX 模型包。`GET /api/ocr/get_options` 会列出 `ocr.engine`。
- 参数设置的识别页会写明当前引擎的特点。Windows OCR：不用下载，一次一种已装语言，框是正矩形，没有方向分类、置信度和 GPU，边长上限仍然有效。Umi 用更大的 server 模型（简中、英、繁、日、韩、俄）。Rapid mobile 简中只做简体中文。Rapid 全语种另有拉丁字母和阿拉伯文。ONNX 包都有检测、方向分类、识别、设备、阈值和置信度。
- 截图识别可选用 **Windows 系统 OCR**（`model_pack = winocr`）。不加载 ONNX 模型。速度更快，效果不可能稳定。选择记在 `win_ocr_langs`。
- 网页管理 **推送到PC** 会把文本复制到剪切板，并弹出 toast「已复制文本：…」。清空仍只清掉文件同步页里的文本。
- 参数里已启用 HTTP 时，局域网地址占着端口不再把 HTTP 页显示成「未启用」。本机仍听 `127.0.0.1`，页上显示这个地址。
- 界面语言改为程序目录 `lang/` 下的 toml，每种语言一个文件。启动时读入全部文件。语言菜单和参数设置按中文、英文、日文、韩文，以及其它语言（德文、西班牙文、法文、葡萄牙文、俄文）排序。
- 底部状态栏在没有模型时显示「未加载模型」。
- 底部状态栏在内存汇总旁一直显示 HTTP 服务：接口监听中为 `HTTP 0.0.0.0:1224` 或 `HTTP 127.0.0.1:1224`，只有传文件的网卡监听时为「HTTP 局域网:1224」，该听却没听上为「HTTP 未启动」并带上失败原因，接口、传文件和投屏都关着为「HTTP 未启用」。
- 韩语词条里的词组和例句一样，原文后面有发音按钮，读韩语原文。下一行的中文释义不读。
- **帮助 → 安装功能 → Windows语音**：本进程不是管理员时，安装或卸载用 `start` 弹出一个管理员窗口，执行勾选的语音包。在用户账户控制里允许即可。退出码 0 和 3010 仍算成功。DISM 命令仍可复制。
- **帮助 → 安装功能 → Windows语音**：进入本页或点刷新时才读取已装语音包和发音人。本进程不是管理员时，这次读取和安装、卸载一样用 `start` 弹出一个管理员窗口。用户账户控制里拒绝后，不标语音包状态，仍显示本进程能看到的发音人。

#### 修复

- 词典词条会列出全部义项、例句和惯用语。英语 `run` 的词组（约 60 条，含 `come running`）不再在 16 条义项后被截掉。
- 网页管理勾选 **保持登录** 后，重启本程序仍然保持。浏览器留下 30 天 Cookie 和已保存的登录，电脑正在启动时会重试；旧页面里的 token 不再挡住这次登录。
- 词典划词浮窗：鼠标悬停词条时字保持深色，底为浅蓝。不再把字改成白色叠在很浅的高亮上。
- 在词典词条里双击单词会弹出划词浮窗。拖选和右键仍会弹出。
- 文件名含中文（如 `XPlayer v2.9.0.0 高级版.apk`）发到手机时，不再报「路径中具有非法字符」。共用 HTTP 口按 UTF-8 解码下载地址里的查询串。
- HTTP 服务启动失败后，每 10 秒再试一次，直到听上，或接口、传文件、投屏都关掉。状态栏在成功前仍显示失败原因。

## v1.0.15 (2026-10-05 ~ 10-06)

### English

#### Fixed

- The LLM log request and response show Chinese characters. JSON `\uXXXX` escapes are decoded when the row is opened.
- Speech recognition: **Refresh models** and **Copy** size to their labels.

#### Changed

- Japanese dictionary hits list every writing in the title, for example 其奴, そいつ, そやつ. Out-dated or obsolete writings are omitted from the list and still shown in the entry. The row speaker reads a kana form without kanji (hiragana or katakana). 破瓜 is spoken as はか. A headword that is already kana is read as written.
- Dictionary selection **Translate** opens the translate window, fills the source, and starts the translation.
- Speaking a selection in a dictionary entry uses that selection's language voice. Japanese glosses are only Chinese and English, so a Chinese gloss uses the Chinese voice and an English gloss uses the English voice. Kana in the headword, reading, or example keeps the Japanese voice. Chinese, English, and Korean entries choose only among those three voices.
- A dictionary search shows the hit count and the elapsed time in milliseconds on the status line.
- Dictionary list rows show every sense on the second line, separated by `; `. Text past one line is trimmed. The language tag is `zh`, `ja`, `ko`, or `en`. An English interface prefers the English gloss on that line.
- Removed unused code. ONNX session setup, the Sherpa device check, arrow drawing, and JSON field parsing each live in one place.
- **Diagnostics** is under **Help**. Ready checks show a green **正常**. Checks that are not ready show red text.
- **Settings → General** no longer shows the module switches. `mod_*` in `config.toml` is unchanged.
- Diagnostics shows an optional item that is off as **未启用**: service mode, CUDA / DirectML not installed, ONNX not loaded yet, face models absent, OCR engine not loaded. A missing file or a failed check still shows red **不正常**.
- Removed the unused OpenCV video library `opencv_videoio_ffmpeg4110_64.dll`. It is no longer in **Help → Install features** or Diagnostics. A build deletes any copy under the output folder. Recording still uses `ffmpeg64`.
- Removed unreferenced helpers and properties (ASR model lookup, capture overlay, scroll constants, and unread recording / speech / dictionary fields). `IFontResolver` methods stay.

#### Added

- **LLM log** tab lists the latest 1,000 LLM calls (model, HTTP status, input / output / total tokens, response time). Select a row to read the request and the response. Stored in `log/llm-calls.jsonl` next to the program. The address query string is omitted. Settings → General can hide it (`tab_llmlog_visible`).
- Web file manager (**File sync → Web manager**, desktop `/` and phone `/m`) has **Text**. The dialog shows the current text on the PC File sync page. Clear empties it on the PC. Send to PC writes the edited text back into that box.

### 中文

#### 修复

- LLM 日志的请求和响应按汉字显示 JSON 里的 `\uXXXX`。
- 语音识别里的 **刷新模型** 和 **复制** 按文字宽度显示。

#### 变更

- 日语词条在列表标题里显示各表记，例如「其奴, そいつ, そやつ」。过时或废用的表记不出现在列表里，详情里仍显示。词头发音改读没有汉字的假名（平假名或片假名）：「破瓜」读「はか」。词头本身已是假名则照读。
- 词典划词点 **翻译** 会打开翻译小窗，填入原文后立即翻译。
- 详情里划词发音按选区语言选用发音人。日语词条的释义只有中文和英文，汉字释义用中文发音人，英文释义用英文发音人。词头、读音和例句里的假名仍用日语发音人。汉语、英语、韩语词条只在中文、英文、韩语里判断。
- 词典查完后，状态栏显示条目数和用时（毫秒）。
- 词典列表第二行显示全部义项，用「; 」分隔，超出一行省略。语言标记改为 zh、ja、ko、en。界面为英文时，这一行优先显示英文释义。
- 删掉没有调用的代码。ONNX 会话创建、Sherpa 设备检查、箭头绘制、JSON 字段解析各只保留一处。
- **诊断** 改到 **帮助** 下面。已就绪的检查显示绿色 **正常**，未就绪的显示红字。
- **参数设置 → 常规** 不再显示启用模块。`config.toml` 里的 `mod_*` 不变。
- 诊断里没开的可选项显示 **未启用**：服务模式、未安装的 CUDA / DirectML、尚未加载的 ONNX、没有人脸模型、识别引擎未加载。缺文件或失败仍显示红字 **不正常**。
- 去掉用不到的 OpenCV 视频库 `opencv_videoio_ffmpeg4110_64.dll`。**帮助 → 安装功能** 和诊断里都不再出现。编译时会删掉输出目录里的这份文件。录屏仍用 `ffmpeg64`。
- 删掉没有引用的函数和字段（语音识别选模型、截图遮罩、滚动常量和未读取的录屏 / 语音 / 分词属性）。`IFontResolver` 的方法保留。

#### 新增

- **LLM日志** 页列出最近 1000 次 LLM 请求（模型、HTTP 状态、输入 / 输出 / 合计 token、响应时间）。选中一行查看请求和响应。记录在程序旁 `log/llm-calls.jsonl`。地址里的查询串不写入。参数设置 → 常规可隐藏（`tab_llmlog_visible`）。
- 网页文件管理（**文件同步 → 网页管理**，电脑 `/`、手机 `/m`）增加 **文本传输**。对话框显示电脑文件同步页里的当前文本。清空会清掉电脑上的文本。推送到 PC 把改过的内容写回那个输入框。

## v1.0.14 (2026-10-04 ~ 10-05)

### English

#### Added

- Dictionary on the main window (**Options → Dictionary**, `tab_dict_visible`, on by default). Read-only lookup in `dict.db` next to the program for Chinese, English, Japanese, and Korean. The file is not in the release archive. **Help → Install features** downloads `dict.7z` from the fixed GitHub release `dict-db` and extracts `dict.db`.
- **Help → Install features → Windows speech** lists `Language.TextToSpeech` packs and the voices Windows reports. Install or remove runs only when this process is an administrator; otherwise the DISM command is shown and can be copied. The speech page lists a new voice after ScreenKit restarts.
- **Settings → General** can turn OCR, TTS, ASR, chat, translate, face, and dictionary on or off (`mod_*`, default on). Off hides that page and stops its hotkey and tray entry. **Settings → API** can turn each HTTP path off (`http_ocr`, `http_tts`, `http_asr`, `http_translate`, `http_chat`, `http_face`). A disabled path returns code 810 and is left out of `GET /api`. Main-tab checkboxes still only hide the page.

#### Changed

- Dictionary language filters are buttons in a row: All, Chinese, Japanese, Korean, English. The list is two lines (language and headword, then the gloss) with a speak button on each row. Entry text is smaller and colors pronunciation, part of speech, sense numbers, language labels, and examples. Each example has a speak button.
- Selecting text in an entry opens a popup outside the selection, with matching headwords plus Speak, Search, Translate, and Copy. Speak reads the whole selection. The popup closes when its window is no longer the active window. Search, a matching headword, or a word link opens another dictionary window and looks up that text. The page you were reading stays.
- The dictionary hotkey (`hotkey_dict`, empty by default) shows the main window and selects that tab, enabling it when it was hidden. Pressing it again while that tab is showing hides the main window. It reads the clipboard and searches only when that text is one word: 1–4 Chinese characters, 1–20 English letters, Japanese with kana up to 12 characters, or 1–8 Hangul syllables. A sentence or a blank clipboard opens the dictionary and does not search.
- Dictionary speech (**Settings → Dictionary**): each of Chinese, English, Japanese, and Korean has an engine (Auto / ONNX / SAPI / Windows speech / Edge), a voice, and a rate from 0.5 to 2. Auto uses that language's Windows speech, otherwise Edge online. Japanese and Korean headwords are spoken only up to the first comma. Spoken audio is cached in `tmp/voice` and kept for 1 day. The SQLite connection closes after 5 minutes without a lookup. This does not change the Speech tab.
- Speech with no saved engine uses Windows speech when a voice is installed, otherwise Edge online. A saved engine is kept. The language list shows named languages first (Chinese, English, Japanese, Korean, Vietnamese, Cantonese, French, German, Spanish), then the remaining codes. SAPI and Windows speech list only languages that have an installed voice.
- `opencv_videoio_ffmpeg4110_64.dll` is no longer in the release archive. OCR and recording do not use it. **Help → Install features → OpenCV video** downloads it from the OpenCV NuGet package when needed.
- `ZXing.dll`, `SharpCompress.dll`, `SharpSevenZip.dll`, `7za.dll`, and `e_sqlite3.dll` are no longer in the release archive. **Help → Install features** can download each one. Barcode scan and QR images ask for ZXing. Opening the dictionary asks for SQLite. Extracting a 7z (the dictionary pack) downloads 7za and SharpSevenZip if they are missing. Extracting `tar.bz2` speech or ASR packs downloads SharpCompress if it is missing.
- **Help → Install features** renames Voices to **ONNX speech models**. Japanese is Supertonic 3 (`sherpa-onnx-supertonic-3-tts-int8-2026-05-11`). The Speech page can load that model and passes `lang` from the language filter, or from the script of the text.
- ONNX CPU/GPU runtimes are not loaded at startup. The first OCR, translation, face, speech, or dictionary ONNX use loads the matching runtime. After `onnx_unload_min` idle minutes (default 5; `0` keeps them loaded) the sessions unload, then this process drops the native libraries it loaded. A DLL still held by the managed runtime or Sherpa stays mapped until exit. Service mode still warms up and does not unload.
- **Options → Memory** shows the process working set, private bytes, and managed heap, and lists each loaded model by its weight-file size together with the mapped ONNX runtime. **Unload from memory** frees that row, runs a full GC, and compacts the large-object heap. A runtime still mapped in this process stays until restart. A row in use cannot be unloaded. Unload on the ONNX GPU row also releases the CUDA libraries this process loaded, including the extra references a Sherpa GPU voice keeps. The row stays only when those libraries are still mapped. The status bar summarizes what is loaded, for example `ONNX GPU loaded, 1 models, 1.73GB total`. Click the bar to open Memory.

#### Fixed

- Supertonic uses a fixed noise seed. The same text, voice, and rate now produce the same audio.

### 中文

#### 新增

- 主窗口增加词典页（**选项 → 词典**，`tab_dict_visible`，默认显示）。只读查询程序旁的 `dict.db`，支持汉语、英语、日语、韩语。词典库不进发布包。**帮助 → 安装功能**从固定的 GitHub Release `dict-db` 下载 `dict.7z` 并解出 `dict.db`。
- **帮助 → 安装功能 → Windows语音**列出 `Language.TextToSpeech` 功能包和 Windows 报告的发音人。本进程是管理员时才能安装或卸载，否则只显示 DISM 命令并可复制。重启本程序后，语音合成页才会列出新发音人。
- **参数设置 → 常规**可启用或停用截图识别、语音合成、语音识别、LLM对话、翻译、人脸、词典（`mod_*`，默认开）。关闭后隐藏对应页，并停用热键和托盘入口。**参数设置 → 接口**可分别关闭 OCR、TTS、ASR、翻译、对话、人脸的 HTTP 路径（`http_ocr` 等）。关闭时返回 810，`GET /api` 也不再列出。主界面 Tab 显示开关仍只隐藏入口。

#### 变更

- 词典语言改为并列按钮：全部、汉语、日语、韩语、英语。列表为两行（语言与词头、释义），每行可发音。详情字号缩小，读音、词性、义项编号、语种标签、例句分色。每条例句有发音按钮。
- 在详情里选中文字后，浮窗出现在选区之外，含匹配词条以及发音、搜索、翻译、复制。发音读完整选区。所在窗口不再是当前窗口时浮窗关掉。点搜索、匹配词条或详情里的词语链接，都再开一个词典窗口查询该词。正在看的这一页不变。
- 词典热键（`hotkey_dict`，默认不注册）打开主窗口并切到该页；页被隐藏时会先启用。已在该页时再按则隐藏主窗口。热键读取剪贴板，内容像一个单词才搜索：汉字 1–4 个，英文 1–20 个字母，带假名的日语最多 12 字，韩语 1–8 个音节。句子或空白只打开词典、不搜索。
- 词典发音（**参数设置 → 词典**）：汉、英、日、韩各自可选引擎（自动 / ONNX / SAPI / Windows 语音 / Edge）、发音人和语速（0.5～2）。自动先用该语言的 Windows 语音，没有再用 Edge 在线。日语、韩语词头发音只读到第一个逗号之前。发音缓存在 `tmp/voice`，保留 1 天。连续 5 分钟没有查询后 SQLite 连接关闭，下次查询再打开。不改语音合成页。
- 语音合成未保存引擎时，有 Windows 语音就用它，否则用 Edge 在线。已经保存的引擎保持不变。语言下拉先列出有名称的语言（中文、英文、日文、韩文、越南语、粤语、法语、德语、西班牙语），其余语言代码按字母排在后面。SAPI 和 Windows 语音只列出有发音人的语言。
- `opencv_videoio_ffmpeg4110_64.dll` 不再打进发布包。识别和录屏不用它。需要时在 **帮助 → 安装功能 → OpenCV 视频** 从 OpenCV 的 NuGet 包下载。
- `ZXing.dll`、`SharpCompress.dll`、`SharpSevenZip.dll`、`7za.dll`、`e_sqlite3.dll` 不再打进发布包。**帮助 → 安装功能**可以分别下载。识别条码、生成二维码时若没有 ZXing，会提示安装。打开词典时若没有 SQLite，会提示安装。解压 7z（词典包）时若没有 7za 或 SharpSevenZip，会先下载。解压语音或识别模型的 `tar.bz2` 时若没有 SharpCompress，会先下载。
- **帮助 → 安装功能**里原来的「发音人」页改名为 **onnx语音模型**。日语模型是 Supertonic 3（`sherpa-onnx-supertonic-3-tts-int8-2026-05-11`）。语音合成可以加载它，并按界面语言或文字选择 `lang`。
- ONNX 的 CPU/GPU 运行库不再在启动时加载。第一次识别、翻译、人脸、语音或词典里的 ONNX 才会加载。空闲达到 `onnx_unload_min` 分钟（默认 5，`0` 表示不自动卸载）后先卸会话，再释放本程序加载的原生库。托管运行时或 Sherpa 仍占用的 DLL 会留到退出。服务模式仍会预热，并且不按空闲卸载。
- **选项 → 内存占用**显示进程工作集、专用内存和托管堆，并按权重文件大小列出已加载的模型，同时列出已映射的 ONNX 运行库。每行「从内存卸载」会释放该项，并做完整 GC、压缩大对象堆。已经映射进本进程的运行库要重启后才从内存里消失。正在使用的项不能卸。ONNX GPU 这一行还会释放本进程加载的 CUDA 库，包括 Sherpa GPU 发音人多占的引用；只有这些库仍映射着时才留在列表里。状态栏改为汇总，例如「已加载ONNX GPU、1个模型 共1.73GB」。点击状态栏打开内存占用。

#### 修复

- Supertonic 使用固定噪声种子。同一段文字、同一个发音人、同一语速现在得到同一段声音。

## v1.0.13 (2026-09-27 ~ 10-03)

### English

#### Added

- Screenshot toolbar **History** opens the `screenshots/` folder as thumbnails. Select one to preview (wheel zoom, drag to pan, double-click fits). The annotate bar under the preview draws rect, ellipse, arrow, pen, and text. Done, OCR, and Open with the default app save a new screenshot and copy it only when the picture has marks. With no marks they use the original file: Done copies it, OCR recognizes it, and Open launches it. Nothing new is written. After Done, OCR, or Open, the history window closes and the picture is shown on the main window; OCR also runs recognition. **Copy** (and Ctrl+C) copies the selected picture to the clipboard and leaves the history window open. The source file is left as it is. The copy-mode menu only remembers the choice; P cycles it and does not save.
- Hotkey to cycle the on-capture clipboard mode (image → file → path), default **Ctrl+Alt+P** (Settings → hotkeys, `hotkey_snap_copy`). Each switch shows a toast at the bottom center of the screen and recopies the last screenshot.
- Annotate bar copy control shows the current mode as a combo (text plus `p`). Picking an item finishes the shot with that mode and saves it. `P` on the bar cycles the mode and does not finish.
- Screenshot toolbar: an icon opens the shot with the default app after capture (same save and clipboard path as Done; the main window stays behind the viewer).
- **Tools → Window manager**: list visible top-level windows (title, process, HWND). Select a row, click to pick a window, or type an HWND, then pin it in front or unpin it. While picking, a green frame follows the window under the cursor; the selected window keeps the frame. Tray entry and CLI `--test-wintop`.
- Screencast tab: cast this PC to another PC on the LAN. Saved PCs stay listed on the left; click one to fill the IP. Output is limited by max width and height (default 1000×1000, shrink to fit, never enlarge). Start picks a region the same way as a screenshot (click a window or drag), then waits so audio and its source (speakers, microphone, or both) can be changed before sending. The region shows a red frame and a toolbar (drag, resize, start, pause, stop). Pause keeps the connection and stops picture and audio until resume. The frame is excluded from the picture.

#### Changed

- Screenshot page image bar uses the built-in WPF toolbar (button, toggle, separator, language combo). It is shorter, and items that do not fit go to the overflow menu instead of being clipped.
- Main menu **Help** holds **Install features**, **Check for Updates**, **About**, and **GitHub** (opens https://github.com/cfwang123/ScreenKit). Those items are no longer under Options.
- LLM **thinking intensity** is an editable dropdown. Presets stay none / low / medium / high / max; other tokens such as `minimal` are kept and sent as `reasoning_effort`.
- **Settings → Translate** adds **LLM batch size** (`translate_llm_batch`, default 8, range 1–64). OCR line translation and `POST /api/translate` use it. A request `chunk` still overrides that default.
- Image convert: the max width/height box is now **Cap shorter side**. If the smaller side is over the limit, the image shrinks to fit; it is never enlarged. Config keys are `imgconv_short` and `imgconv_short_px` (default 1080). An old `imgconv_max_w` / `imgconv_max_h` pair is read once as the smaller of the two.
- Screenshot save uses the same shorter-side cap. **Settings → Capture** has one pixel value (`screenshot_short` / `screenshot_short_px`, default 1080). Files in `screenshots/` and copy-as-file shrink only when the smaller side is over it; OCR still uses the full image. An old `screenshot_max_w` / `screenshot_max_h` pair is read once as the smaller of the two. GIF and PC-to-PC cast still use a max width and height box.
- Screen recording uses that same shorter-side cap (`record_short` / `record_short_px`, default 1080, shrink only). An old `record_max_w` / `record_max_h` pair is read once as the smaller of the two.
- Record options show CRF only for x264/x265, and AV1 CRF only for AV1. Media Foundation hides both; the stored numbers stay.
- Screen recording adds Windows Media Foundation H.264 (MP4, no FFmpeg). x264, x265, AV1, and GIF still need FFmpeg and still fail clearly with no fallback. Install features lists system H.264 as built-in; FFmpeg stays the optional download for x264/x265/AV1 and GIF.
- The first-launch install window selects nothing. Confirm installs only what you check, and does not remove packages already on disk. Later, **Help → Install features** still checks what is installed. System H.264 stays checked.
- Phone camera upload and the web camera use that same shorter-side cap. **Settings → API** still has one pixel value (`photo_max_px`, default 2000). The phone and the browser shrink only when the smaller side is over it; the PC applies the same rule if the browser could not.
- Screencast tab **Scan LAN** lists this PC as well. Several local addresses fold into one row, marked “This PC”.
- Settings → API: the listen-address box is now **Allow LAN access**. Checked (default) listens on every interface; unchecked listens on `127.0.0.1` only. File transfer, the web manager, and Wi-Fi cast share this port. Config key is `http_lan`. An old `http_host` of `127.0.0.1` stays LAN-open when file transfer or cast was already on.
- The Windows release package no longer includes the Android APK.
- **Install on phone** can check GitHub Releases and download the latest APK. The QR code still serves that file from this PC over the LAN.
- The default LLM translate prompt no longer names the source language. It only says to translate into `{dst}`. A saved `translate_llm_prompt` that still contains `{src}` is left as it is.

#### Fixed

- Media Foundation recordings write AAC while capturing. The rate is 44.1 kHz or 48 kHz from the start, because system AAC rejects other rates such as 22.05 kHz (`0xC00D36B4`). Saving no longer builds a second file to mix the sound in.
- Android cast screen shows **投屏中** again after leaving to the main screen and opening cast. The service was still casting; the new screen had reset to “ready”.
- Android debug and release builds now sign with the same keystore (`android/app/debug.keystore`). Installing one over the other keeps the app’s private data.
- Phone screencast no longer fails with “WebSocket handshake failed”. A LAN address was answered by the file-transfer socket, which rejected `/cast` as unpaired. That socket now completes the WebSocket upgrade and passes the session to the viewer.
- Screencast viewer opens centered on the primary monitor’s work area. A new session no longer follows the cursor onto another monitor and hangs half off its edge. Size, position, and maximized state still stay put when the phone rotates.

### 中文

#### 新增

- 截图识别页工具栏增加 **截图历史**。打开 `screenshots/`，左侧缩略图，右侧预览（滚轮缩放、拖动平移、双击适应）。预览下方是标注条（矩形、椭圆、箭头、画笔、文字）。有标注时，完成、OCR、用系统软件打开会另存一张新截图并按当前方式复制（图片 / 文件 / 路径）。没有标注时直接用原图：完成只复制，OCR 识别原图，用系统软件打开原文件，不生成新图。完成、OCR 或用系统打开之后关闭历史窗口，并在主界面显示这张图；点 OCR 时接着识别。**复制**（以及 Ctrl+C）把选中的图片放入剪贴板，历史窗口不关闭。不改源文件。复制方式下拉只记住选择；P 只切换、不保存。
- 增加快捷键切换截图复制方式（图片 → 文件 → 路径），默认 **Ctrl+Alt+P**（参数设置里的热键，`hotkey_snap_copy`）。每次切换在屏幕底部中央弹出提示，并把上次截图按新方式再写入剪贴板。
- 截图标注条上的复制方式改为组合框，显示当前方式（如「复制为图片 p」）。下拉选一项会立刻按该方式完成并记住。标注时按 `P` 只切换方式，不结束本次截图。
- 截图标注工具条增加图标「截屏后用系统软件打开」：截完后按平时方式保存并复制，再用系统默认程序打开该文件。主窗不抢到查看器前面。
- **工具 → 窗口管理**：列出可见顶层窗口（标题、进程、HWND）。可选中、点选或填入 HWND，再设置或取消固定在前面。点选时绿框跟着光标下的窗口，选中后绿框留在该窗口上。托盘同样入口。CLI：`--test-wintop`。
- 主界面 **投屏** Tab：把本机画面投到另一台电脑。已保存的电脑直接列在左侧，点一项即填入 IP。输出按最大宽高限制（默认 1000×1000，等比缩小、不放大）。开始后先框选区域（单击窗口或拖拽，和截图一样），选好后可改声音开关和来源（扬声器、麦克风或两者），再点开始才发送。选区显示红框和操作条（可拖动、缩放、开始、暂停、停止）。暂停只停画面和声音，连接保留，可继续。红框不进入画面。

#### 变更

- 截图识别页的图区工具条改为 WPF 自带 `ToolBar`（按钮、开关、分隔线、目标语言下拉）。高度更矮；窗口变窄时放不下的项进入溢出菜单，不再被裁切。
- 主菜单增加 **帮助**。**安装功能**、**检查更新**、**关于** 从「选项」移到这里，并增加 **GitHub 主页**（浏览器打开 https://github.com/cfwang123/ScreenKit）。
- LLM **思考强度**改为可手输的下拉。预设仍是 none / low / medium / high / max；手输的 `minimal` 等会原样作为 `reasoning_effort` 发出。
- **参数设置 → 翻译**增加 **一批翻译数量**（`translate_llm_batch`，默认 8，范围 1–64）。识别多行翻译和 `POST /api/translate` 按这个数分组。请求里的 `chunk` 仍可覆盖。
- 图片格式转换：原来的最大宽高改为 **限制较短边**。宽和高里较小的一边超过设定值时等比缩小，不超过则不放大。配置键 `imgconv_short`、`imgconv_short_px`（默认 1080）。旧的 `imgconv_max_w` / `imgconv_max_h` 只在没有新键时读一次，取两者中较小的数。
- 截图保存改为同一套较短边限制。**参数设置 → 截图** 只有一个像素值（`screenshot_short` / `screenshot_short_px`，默认 1080）。写入 `screenshots/` 和复制为文件时，只在较短边超过时等比缩小；识别仍用原图。旧的 `screenshot_max_w` / `screenshot_max_h` 只在没有新键时读一次，取两者中较小的数。GIF 和电脑互投仍是最大宽高框。
- 录屏改为同一套较短边限制（`record_short` / `record_short_px`，默认 1080，超过才等比缩小、不放大）。旧的 `record_max_w` / `record_max_h` 只在没有新键时读一次，取两者中较小的数。
- 录屏选项里，CRF 只在 x264/x265 时显示，AV1 CRF 只在 AV1 时显示。系统 H.264 隐藏这两项，已保存的数值仍保留。
- 录屏增加 Windows Media Foundation H.264（MP4，不用 FFmpeg）。x264、x265、AV1 和 GIF 仍要 FFmpeg，失败时明确报错，不会改用别的编码器。安装功能里系统 H.264 标为系统自带；FFmpeg 仍是 x264/x265/AV1 与 GIF 的可选下载。
- 第一次启动的安装窗口默认不勾选。点确认只安装勾上的项，不会删掉已经在磁盘上的组件。之后从 **帮助 → 安装功能** 打开，仍会勾上已安装的功能。系统 H.264 保持勾选。
- 手机拍照上传和网页拍照改为同一套较短边限制。**参数设置 → 接口** 仍是一个像素值（`photo_max_px`，默认 2000）。手机和浏览器只在较短边超过时等比缩小；浏览器压不了时，电脑按同一规则再处理。
- 投屏页 **搜索局域网** 会列出本机。多块网卡并成一条，名称后标「本机」。
- **参数设置 → 接口**：监听地址改为勾选 **允许局域网访问**。勾选（默认）时所有网卡可连；不勾选时只听 `127.0.0.1`。文件传输、网页管理和 Wi-Fi 投屏共用此端口。配置键为 `http_lan`。旧配置里 `http_host` 为 `127.0.0.1` 且已开文件传输或投屏时，升级后仍允许局域网。
- Windows 发布包不再附带安卓 APK。
- **安装到手机**可以检查 GitHub Releases，并下载最新 APK。二维码仍从这台电脑的局域网地址提供刚下载的文件。
- LLM 翻译默认提示词不再写源语言，只要求翻译为 `{dst}`。配置里已经保存的 `translate_llm_prompt` 若仍含 `{src}`，不会被改掉。

#### 修复

- 系统 H.264 在录制时就把声音写成 AAC。采样率从一开始就是 44.1kHz 或 48kHz，因为系统 AAC 不接受 22050Hz 等其它采样率（`0xC00D36B4`）。保存时不再另做一次合成。
- 安卓投屏中退回主界面再打开投屏页，会重新显示「投屏中」和停止按钮。服务仍在投，只是新开的页面把状态清成了「准备投屏」。
- 安卓 debug 与 release 固定用同一把钥匙（`android/app/debug.keystore`）。互相覆盖安装会保留私有数据。
- 手机投屏不再出现「WebSocket 握手失败」。局域网 IP 上是文件传输的套接字在应答，它把 `/cast` 当成未配对请求拒绝了。现在这个套接字会完成 WebSocket 升级，再把会话交给投屏窗口。
- 接收投屏的画面窗默认开在主屏幕工作区中心。新开会话不再跟着光标跑到其它屏，避免停在屏边缘只露出一半。手机切换横竖屏时，窗口的大小、位置和最大化状态仍保持不变。

## v1.0.12 (2026-09-26)

### English

#### Fixed

- Screencast: rotating the phone keeps the viewer’s size, position, and maximized or full-screen state. The picture still fits or fills inside that window.
- Screencast: cast sound stays continuous. The picture waits only as long as the sound is actually behind, using capture timestamps, and the phone prefers a low-latency AAC encoder.
- Camera upload: the Android app decodes the camera file directly, so it no longer fails with “could not process the photo”. The web camera compresses with an image element; if the browser cannot, the PC still applies the JPG, quality, and long-edge settings. The phone page now loads a new script after the PC app updates; reopen the page before taking another photo.
- File sync list: double-click opens a file with the system. Right-click keeps the current selection (including the highlight while the menu is open) and shows Open, Cut, Copy, Paste, Push, and Delete for those items.

#### Removed

- Screencast: removed screen-off casting. The phone page and the cast notification no longer offer it.

### 中文

#### 修复

- 投屏：手机切换横竖屏时，电脑画面窗保持原来的大小、位置，以及最大化和全屏状态。画面仍在该窗口内适应或铺满。
- 投屏：声音连续播放。画面按采集时间对准正在播出的声音，只等待实际落后的那段，不再固定晚 0.5 秒。手机优先用低延迟 AAC。
- 拍照上传：安卓改为直接读缓存里的照片，不再因解码失败提示「处理照片失败」。网页拍照改用图片元素压缩；浏览器压不了时，电脑仍按 JPG、质量和最长边处理。网页脚本以前被浏览器永久缓存，更新电脑程序后要重新打开页面再拍。
- 文件同步列表：双击文件用系统打开。右键保持当前选中（菜单打开时高亮仍在），并对这些项弹出打开、剪切、复制、粘贴、推送、删除。

#### 移除

- 投屏：去掉熄屏投屏。投屏页和通知里不再提供该按钮。

## v1.0.11 (2026-09-26)

### English

#### Added

- **Screencast** (**Tools → Screencast**, also on the tray): receive a phone or another PC, or cast this desktop out. Quality 540p/720p/1080p, optional audio. Wi‑Fi uses `/cast` on the HTTP port; USB uses the Android accessory. The picture can fit or fill, and the window keeps its size when the phone rotates. **Connect Android USB accessory** waits for the phone and shows connected, waiting, or not connected. Plugging in USB does not start casting. The phone can turn the screen off and keep casting; the power button wakes it and leaves that mode.
- **Text sync**: one box with Send, Copy, and Clear. Text from the phone is written into that box.
- **Web file manager** on the HTTP API port (default 1224): desktop `/` and phone `/m`. Login is required to upload, rename, or delete; a correct link downloads without login. Stay signed in, open a folder, or zip it. The file-sync tab shows the LAN address, QR code, and password. The phone page can check for a new APK.
- **Password generator → Variants**: type a phrase, pick an LLM, and get memorable spellings in other languages. Double-click a row to copy.
- **Camera upload** format, JPG quality, and long-edge limit are set on the PC (**Settings → API**). The phone and the web camera button use those values.
- File-sync inbox can switch between list and thumbnails. **Push** sends the selection to the connected phone. A new file or text from the phone is copied to the clipboard, with a short toast.

### 中文

#### 新增

- **投屏**（**工具 → 投屏**，托盘同样入口）：接收手机或另一台电脑的画面，也可把本机投出。画质 540p/720p/1080p，可关声音。Wi‑Fi 走 HTTP `/cast`，USB 走安卓配件。画面可适应或铺满，手机旋转时窗口大小不变。**连接安卓 USB 配件**会等待手机，并显示已连接、等待中或未连接。插上 USB 不会自动开始投屏。手机可熄屏继续投，按电源键亮屏退出。
- **文本同步**：一个输入框，发送、复制、清空。手机发来的文字写入同一框。
- **网页文件管理**（与 HTTP API 同一端口，默认 1224）：电脑 `/`、手机 `/m`。登录后才能上传、改名、删除；正确链接可直接下载。可保持登录，文件夹可进入或打包。文件同步页显示局域网地址、二维码和密码。手机网页可检查新版本。
- **密码生成器 → 变体**：输入一句短语，选 LLM，得到其它语言的好记拼写。双击复制。
- **拍照上传**的格式、JPG 质量和最长边改在电脑 **参数设置 → 接口**。手机和网页拍照都用这组参数。
- 文件同步接收目录可在列表和缩略图之间切换。**推送**把所选发给已连接的手机。收到手机文件或文本后复制到剪贴板并短暂提示。

## v1.0.10 (2026-09-24)

### English

#### Added

- **Tools → Password generator**: crypto-random passwords; length, count, character sets, skip ambiguous `0OIl1`, at least one of each class. Tray and CLI `--test-pwgen`. Last settings are saved. **Word lex** tab: pick an LLM (`pwgen_llm`), then translate a word into Chinese/Japanese/Korean/English and more, including Literary Chinese, Ancient Greek, Latin, Sanskrit, and Biblical Hebrew, with pinyin, romaji, and romanization.
- **Tools → Network tools**: Ping, DNS lookup, domain/IP WHOIS, traceroute, ping-locate, proxy-locate, and HTTP speed-locate (latency to regional sites via the system proxy). Each action clears previous output. Tray and CLI `--test-nettool`.
- Image convert output: **Replace source (permanently delete)** and **Replace source (Recycle Bin)**. Convert-all asks before replacing. Same-name different-extension writes the new file then removes the original; same extension overwrites in place (Recycle Bin keeps the old file). “Keep original if still ≥ N%” skips and leaves the source. Settings: `imgconv_out_mode`. CLI: `--test-img-convert`.
- **File sync** tab: flat inbox listing of `sendfile/` (no subfolder navigation). Select, marquee, cut/copy/paste, Delete to Recycle Bin, drag to Explorer. Drop on the list imports; drop on the lower zone still sends to the phone. File/image paste goes into the inbox; text paste still goes to the phone pane.

#### Changed

- Windows opened from the tray have no owner (centered on screen, own taskbar button) so they do not minimize with the main window.
- File-transfer pairing dialog stays always-on-top (no owner; still visible when the main window is in the tray). The phone shows a waiting dialog until the PC allows or denies; Cancel aborts.
- `node build.js release` (Android) copies the APK next to ScreenKit.exe (`apk/`) and runs `slx ScreenKit` once.

#### Fixed

- File sync inbox list no longer freezes on click (OLE file-drag was starting on the press, then deadlocking against the tab’s own drop target).
- Batch rename `%1` / `%2` are shortest-match captures (FastCopy). A pattern like `2026-09%1 %2` → `%2` keeps the title after the first space, not the fragment after the last space.

### 中文

#### 新增

- **工具 → 密码生成器**：加密随机；长度、条数、字符集、排除易混 `0OIl1`、每类至少一个。记住上次设置。托盘与 CLI `--test-pwgen`。**单词译音** Tab：可选 LLM（`pwgen_llm`），译成中日韩英等，以及文言文、古希腊语、拉丁语、梵语、古希伯来语，并给出拼音 / romaji / 罗马字。
- **工具 → 网络工具**：Ping、域名解析、WHOIS、路由跟踪、Ping 定位、代理定位、HTTP 测速定位（系统代理访问各地站点测延迟）。每次点按钮清空旧输出。托盘与 CLI `--test-nettool`。
- 图片格式转换输出路径新增：**替换源文件（永久删除）**、**替换源文件（回收站）**。全部转换前会确认。后缀不同则写出新文件再删源；同后缀原地覆盖（回收站会先把旧文件移走）。「体积仍 ≥ N% 用原图」时跳过、不删源。配置：`imgconv_out_mode`。CLI：`--test-img-convert`。
- **文件同步** Tab：平铺浏览接收目录 `sendfile/`（不进入子目录）。可单选/多选、框选、剪切/复制/粘贴、删除到回收站、拖到资源管理器。拖到列表导入；拖到下方区域仍传到手机。粘贴文件/图片进入接收目录；粘贴文本仍到右侧。

#### 变更

- 托盘打开的窗口不再挂主窗为 Owner（居中屏幕、独立任务栏项），避免跟着主窗最小化。
- 文件传输配对弹窗始终置顶（不挂主窗；主窗在托盘时仍能点到）。手机端弹出等待窗直到电脑允许或拒绝，可取消。
- 安卓 `node build.js release` 会把 APK 拷到 ScreenKit exe 旁 `apk/`，并 `slx ScreenKit` 编译一次。

#### 修复

- 文件同步接收列表点击不再卡死（按下时误进 OLE 拖放，并与本页投放区死锁）。
- 批量重命名 `%1` / `%2` 改为最短匹配（与 FastCopy 相同）。`2026-09%1 %2` → `%2` 取第一个空格后的标题，不会变成最后一个空格后的「阴.mp4」。

## v1.0.9 (2026-09-23)

### English

#### Added

- **Tools** menu: **QR / barcode** (caption under the image; UTF-8, GBK, or Hex bytes, default UTF-8), **Batch rename** (Everything / FastCopy patterns: `%1`, `#` / `###`), **Hash** (MD5 / SHA-1 / SHA-256, paste to compare), **Text tools** (Base64, URL, UTF-8/GBK hex, Unicode escape, case, whitespace, smart JSON pretty-print that keeps short arrays/objects on one line). CLI: `--test-qr-make`, `--test-rename`, `--test-hash`, `--test-texttool`.
- Tray menu **Tools** submenu (same entries as the window Tools menu).

#### Changed

- Image convert: list / icon view are compact icon buttons; thumbnails follow rotate and flip. Optional “keep original if compressed size ≥ N% of original” (default on, 80%). Rotate / flip / actual resize still writes the new file.
- QR preview draws the caption as WPF text (clear, 4px below the code). Copy/save still bake a caption into the PNG. Hex mode parses hex into bytes then encodes.
- Tool buttons use a pale fill and a Segoe MDL2 icon.
- Batch rename: source files in a list (add/remove/move, sort by name / modified / added); new names in a textarea (editable). Changing the list or rules recalculates names.

#### Fixed

- Three-monitor / mixed-DPI screenshot overlay no longer shrinks to ~66%. CLI: `--test-overlay-layout`.
- A region that spans monitors can be moved, resized, and drawn on from every screen it covers. Move hot-zone is 10px outside the green frame.

### 中文

#### 新增

- **工具** 菜单：**二维码 / 条码生成**（图下原文；UTF-8 / GBK / Hex，默认 UTF-8）、**批量重命名**（Everything / FastCopy：`%1`、`#` / `###`）、**校验哈希**、**文本小工具**（含 JSON 智能美化）。CLI：`--test-qr-make`、`--test-rename`、`--test-hash`、`--test-texttool`。
- 托盘右键增加 **工具** 子菜单。

#### 变更

- 图片格式转换：列表/图片小图标按钮；缩略图随旋转镜像。可选「压缩后体积仍 ≥ 原图 N% 时用原图」（默认 80%）；有旋转或实际缩小时仍用新图。
- 二维码预览用界面文字画原文；复制/保存仍写入 PNG。新增 Hex 编码。
- 工具按钮浅底 + Segoe MDL2 图标。
- 批量重命名：源文件列表（添加/删除/上下移动，点列头排序）；目标名文本框可手改。改列表或规则时自动重算。

#### 修复

- 三屏/混合 DPI 截图遮罩不再整窗缩到约 66%。CLI：`--test-overlay-layout`。
- 跨屏选区可在每一块相交屏上拖动、缩放、画框。拖动热区在绿框外 10px。

## v1.0.8 (2026-09-23)

### English

#### Added

- LAN **PC file transfer** (HTTP `:17532`, UDP discovery `:17531`): first-connect pairing, `sendfile/` sandbox, main-window **File sync** tab. Companion Android app in `android/` (`com.whj.screenkit` / 「PC文件传输」). Other apps can share multiple files; in-app **Upload** is multi-select.
- File sync **Install on phone**: LAN URL (`GET /apk` on the file-transfer port, no pairing) and QR code (`--test-apk-qr`). With several NICs, pick which address the QR uses (default: internet-reachable physical NIC). A Release build copies the newest Android **release** APK into `apk/` next to the exe (only when newer; debug APKs are ignored).
- **Tools** menu **Image convert**: batch convert to JPG (default quality 60) / PNG / BMP; optional max width/height (same shrink-to-fit as screenshots); output to `output/` next to each source or a chosen folder. Drag files/folders/images into the list; **Icons** view shows a small thumbnail per file (toggle **List**). Select one item to rotate 90/180/270° or flip. Convert-all shows progress. The window is larger (~1.7×) with a live preview after applying quality, max size, rotate, and flip. Settings persist in `config.toml` (`imgconv_*`). CLI: `--test-img-convert`.

#### Changed

**Menus**

- The previous **Tools** menu is renamed **Options** (settings, install features, language, updates, about). Image convert lives under the new **Tools** menu.

**Install features**

- Feature tree opens with installed items checked. Add (green) / remove (red) with counts and sizes; **Reset** restores. **Confirm** installs and uninstalls immediately. Voices stay on their own tab.

**File sync**

- Lives on the main **File sync** tab; Tools menu and tray **Text sync** items were removed. Settings → General can hide the tab (`tab_sendfile_visible`).
- The tab no longer browses PC `sendfile/`. Drops/pastes of files, folders, or images go to the phone’s bound folder **only while the app is connected**; otherwise nothing is sent. Files shared from the phone always land in PC `sendfile/`. Both sides show a pending list with send/receive progress.
- PC tab adds a **transfer log** (completed/failed, time, size); the queue above it only shows in-progress items.
- Text sync shows the latest message in a read-only selectable box (PC and phone); the Copy button is removed.
- Android receive list shows a file-type icon; tapping a finished item opens the system “Open with” chooser.
- Android title-bar hamburger: **Settings** (bind receive folder) and **Switch PC**. Text sync sits to the right of Upload (10dp gap).

**Capture / OCR**

- Reverted screenshot-overlay drag experiments (four-rectangle mask / opaque window / related CLI). Region select again uses the transparent overlay, dim mask, and green selection frame.
- Overlay / result text: a block is selected only on mouse-up; clicking empty space clears the selection; drag-select stays in the dragged range (no snap-expand to a whole line).
- A region that spans monitors can be moved, resized, and drawn on (rect/ellipse/pen/arrow) from every screen it covers. Moving the region uses a 10px band **outside** the green frame.

#### Fixed

- PC file transfer HTTP `:17532` never listened: `HttpListener` registered every LAN address and Windows returned Access denied. It now binds `0.0.0.0` / IPv6 dual-stack with a TCP listener (no URL ACL), so `localhost` and LAN phones can connect.
- Phone pairing confirm dialog no longer sets the main window as `Owner`, so it still appears when the main window is hidden to the tray.
- Dropping files on the **File sync** tab no longer jumps to Speech Recognition: the ASR window-level media drop only applies while that tab is selected.
- Three monitors with a scaled output (desktop 2400×1350, capture frame 1920×1080): the right screen’s frozen image sat at 80% size in the top-left of the overlay, and saved region shots were shrunk. Capture candidates are ranked by how well the frame matches the screen’s physical bounds; when the frame is a uniform scale of the screen the mask still covers full Bounds; a single-screen crop rescales to the physical selection size.
- Android LAN discovery: Wi-Fi multicast lock, a second UDP probe, and a retry when the first scan is empty.
- Three-monitor screenshot: on a screen whose DPI differs from the system, the whole overlay shrank to about 66%. Overlay HWNDs are created as Per-Monitor V2 and sized with that screen’s DPI. CLI: `ScreenKit --test-overlay-layout`.

### 中文

#### 新增

- 局域网 **PC 文件传输**（HTTP `:17532`，UDP 发现 `:17531`）：首次连接弹窗配对、`sendfile/` 沙箱、主界面 **文件同步** Tab。配套安卓应用：`android/`（`com.whj.screenkit`「PC文件传输」）。其它应用可一次分享多个文件，App 内「上传」支持多选。
- 文件同步 **安装到手机**：本机局域网下载地址（文件传输端口 `GET /apk`，无需配对）和二维码（`--test-apk-qr`）。多网卡时可选择二维码地址（默认用能连外网的物理网卡）。编译时把 android 最新 **release** APK 拷到程序旁 `apk/`（仅当源更新；不用 debug）。
- **工具** 菜单 **图片格式转换**：批量转为 JPG（默认质量 60%）/ PNG / BMP；可选限制最大宽高（与截图相同的等比缩小）；输出到每个源文件旁的 `output/` 或指定目录。列表可拖入文件、文件夹、图片；**图片**视图为每张显示小缩略图（可切回 **列表**）。选中单张可旋转 90/180/270° 或镜像。一键全部转换并显示进度。窗口约放大到 1.7 倍，右侧预览已应用质量、宽高限制、旋转和镜像后的效果。参数写入 `config.toml`（`imgconv_*`）。CLI：`--test-img-convert`。

#### 变更

**菜单**

- 原 **工具** 菜单改名为 **选项**（参数设置、安装功能、界面语言、检查更新、关于）。图片格式转换在新的 **工具** 菜单下。

**安装功能**

- 功能选择树打开时勾选已装项。增删显示新增（绿）/ 删除（红）数量与大小，可复位。点**确认**即安装或卸载。发音人仍在单独 Tab。

**文件同步**

- 改到主界面 **文件同步** Tab；已去掉工具菜单和托盘里的「文本同步」入口。参数设置 → 常规可隐藏该 Tab（`tab_sendfile_visible`）。
- Tab 不再浏览电脑 `sendfile/`。电脑拖入或粘贴文件/文件夹/图片：仅在手机 App 已连接时发到手机绑定文件夹，无连接则不传。手机分享/多选上传一律写到电脑 `sendfile/`。两端都显示待传列表与传输进度。
- 电脑端增加**传输记录**栏（完成/失败、时间、大小）；上方队列只显示进行中的任务。
- 文本同步以只读文本框显示最新一条（电脑、手机），可选中复制；已去掉复制按钮。
- 安卓接收列表显示文件类型图标；点已完成项用系统「选择打开方式」打开。
- 安卓标题栏三横菜单：**参数设置**（绑定接收文件夹）、**换电脑**。文本同步按钮在上传右侧，间距 10dp。

**截图 / OCR**

- **撤回**截屏遮罩拖动相关性能试验（四矩形挖空 / 不透明窗 / 相关 CLI）。框选恢复为原先的透明遮罩窗、半透明暗角与绿色选区框。
- OCR 叠字/结果文本：松开鼠标才选中一块；点空白取消选择；拖选只保留拖过的范围，不再吸附扩展整行。
- 跨屏选区可在每一块相交的屏上拖动、缩放、画矩形/椭圆/画笔/箭头。拖动选区热区在绿框**外** 10px。

#### 修复

- PC 文件传输 HTTP `:17532` 实际没在听：`HttpListener` 把每块网卡 IP 都登记成前缀，Windows 返回拒绝访问，整段服务启动失败。现改为 TCP 监听 `0.0.0.0` / IPv6 双栈（不需要 URL ACL），`localhost` 和局域网手机都能连上。
- 手机配对确认弹窗不再绑定主窗口为 `Owner`，主窗托盘隐藏时仍能弹出。
- 往 **文件同步** Tab 拖文件不再跳到语音识别：窗口级音视频拖放仅在语音识别页生效。
- 三屏且右边屏为缩放输出（桌面 2400×1350，实际只能抓到 1920×1080 帧）时截图：遮罩里右屏冻结画面只有 80% 大小、贴在左上，框选存图内容也被缩小。现按「帧尺寸与屏物理 Bounds 的契合度」给抓取候选分档；帧与屏等比时遮罩窗仍按整屏 Bounds 铺满；单屏裁切按物理选区尺寸输出。
- 安卓局域网发现：申请 Wi-Fi 组播锁、重复发送 UDP 探测；第一次扫描为空时再扫一次。
- 三屏截图：系统 DPI 与某块屏不一致时，整块遮罩会缩到约 66%。遮罩 HWND 按 Per-Monitor V2 创建，并按该屏 DPI 钉到物理 Bounds。CLI：`ScreenKit --test-overlay-layout`。

## v1.0.7 (2026-09-11)

### English

#### Added

- The Speech Recognition tab's **Recognize** pane now exposes Offline / Streaming radio options for live captions.
- Seven `tab_*_visible` configuration switches and Settings → General checkboxes can independently hide main-window tabs; all default to visible.

#### Changed

- Round-trip translation still runs up to 20 trips, but now stops early when a translation matches any earlier result.

#### Fixed

- Speech Synthesis: rate and volume sliders no longer overlap the voice combo (the settings grid was missing a fifth row).

#### Documentation

- Clarified that every CHANGELOG entry and GitHub Release description must include both English and Chinese.

### 中文

#### 新增

- “语音识别”Tab 的“识别”子页增加实时字幕“离线 / 流式”单选项。
- 增加 7 个 `tab_*_visible` 配置开关及“参数设置 → 常规”复选框，可分别隐藏主界面 Tab；默认全部显示。

#### 变更

- 来回翻译仍最多执行 20 次；翻译结果与任一前序结果相同时提前停止。

#### 修复

- 语音合成：语速、音量不再与发音人下拉框重叠（设置区 Grid 漏了第 5 行）。

#### 文档

- 明确要求每条 CHANGELOG 记录和 GitHub Release 说明均同时提供英文与中文。

## v1.0.6 (2026-09-10)

### English

#### Added

- Main-window **LLM chat** tab: WeChat-style bubbles, one in-memory conversation, Clear, per-round timing, microphone input, Speak / Auto speak, and optional tools for web access plus sandboxed files/scripts under `tmp/llm/`. Configuration: `chat_llm`, `chat_llm_prompt`, `chat_agent`, `chat_auto_tts`. CLI: `--test-llm-chat`, `--test-llm-agent`.
- Agent weather lookup through wttr.in and a forced tool-call retry when the model refuses a weather request without using tools.
- HTTP `POST /api/chat`: text or audio input and text reply; optional TTS/WAV when `tts=true`. Status adds `llm_chat`; CLI: `--test-http-chat`.
- Speech Synthesis adds **Edge online natural voices**: 300+ voices including Korean, language/gender filtering, playback, MP3 export, and chat auto-speak. No model/API key is needed; Internet access is required and the configured proxy is honored. CLI: `--list-edge-tts`, `--test-edge-tts [voice]`.
- HTTP TTS adds **SAPI**, **Windows** (WinRT / OneCore), and **Edge online** alongside Sherpa. `engine` accepts `sapi`, `winrt`, or `edge`; `/api/status` adds `tts_sapi`, `tts_winrt`, and `tts_edge`. CLI: `--test-http-tts`.
- Screen record / GIF record options **Record mouse** and **Highlight mouse clicks** (`record_mouse` / `record_click_highlight`, `gif_mouse` / `gif_click_highlight`). GDI capture has no pointer; the overlay draws the system cursor and a short yellow / blue / green ripple for left / right / middle clicks. CLI: `ScreenKit --test-record-cursor`.

#### Changed

- Expired screenshot history is cleaned **only at startup** on a background thread (not during capture or when applying Settings), so the UI is not blocked.

#### Fixed

- LLM requests to `opencode.ai` now send `x-opencode-session` / `x-opencode-client`, preventing `MissingSessionID` for Go/Zen models.
- TTS voice installation no longer relies on Windows `tar.exe` invoking an external `bzip2`; `.tar.bz2` model packages are now extracted in-process, including when `ttsmodels` is a junction.
- Korean and other non-Chinese/English VITS voices are inferred from model-directory language tokens. Mimic3/Piper voices now receive their bundled/shared `espeak-ng-data` path; Mimic3 is forced to CPU because ONNX Runtime CUDA can terminate the process with an access violation. CLI regression test: `ScreenKit --test-tts-sherpa <model>`.
- Copy-as-path after a same-process delayed-render bitmap no longer goes through `Clipboard.SetText` / `OleSetClipboard` (that still flushed the old image first and froze the UI for seconds). Path text uses Win32 `EmptyClipboard` + `CF_UNICODETEXT` only. Screenshot encode runs after the overlay closes, on a background thread. `capture_log` records prep/encode/clip timings (`SLOW` if ≥500ms). CLI: `ScreenKit --test-clipboard-path` (includes 4K timing).

### 中文

#### 新增

- 主界面增加 **LLM 对话** Tab：微信式气泡、单一内存会话、清空、每轮用时、麦克风输入、朗读/自动朗读，以及可选的网页和 `tmp/llm/` 沙箱文件/脚本工具。配置：`chat_llm`、`chat_llm_prompt`、`chat_agent`、`chat_auto_tts`。CLI：`--test-llm-chat`、`--test-llm-agent`。
- Agent 天气查询接入 wttr.in；模型拒绝使用工具时会强制重试一次 tool_call。
- HTTP `POST /api/chat`：支持文本或音频输入、文本回复；仅 `tts=true` 时返回 TTS/WAV。状态增加 `llm_chat`；CLI：`--test-http-chat`。
- 语音合成增加 **Edge 在线自然语音**：300 多个发音人（含韩语），支持语言/性别筛选、朗读、MP3 导出及对话自动朗读；无需模型/API Key，但必须联网，并沿用代理设置。CLI：`--list-edge-tts`、`--test-edge-tts [发音人]`。
- HTTP TTS 在 Sherpa 之外增加 **SAPI**、**Windows**（WinRT / OneCore）及 **Edge 在线**；`engine` 支持 `sapi`、`winrt`、`edge`，状态增加 `tts_sapi`、`tts_winrt`、`tts_edge`。CLI：`--test-http-tts`。
- 录屏 / GIF 录屏选项 **录制鼠标**、**高亮鼠标点击**（`record_mouse` / `record_click_highlight`，`gif_mouse` / `gif_click_highlight`）。GDI 抓屏不含指针，叠加系统光标，并在左/右/中键处画短暂黄/蓝/绿散开圈。CLI：`ScreenKit --test-record-cursor`。

#### 变更

- 过期截图历史**仅启动时**在后台线程清理（截图保存、应用设置时不再同步删除），避免卡住界面。

#### 修复

- 访问 `opencode.ai` 时自动发送 `x-opencode-session` / `x-opencode-client`，修复 Go/Zen 模型的 `MissingSessionID`。
- TTS 发音人安装不再依赖 Windows `tar.exe` 调用外部 `bzip2`，改由程序内部解压 `.tar.bz2` 模型包，并支持 `ttsmodels` 为 Junction。
- 韩语等非中英文 VITS 发音人可从模型目录语言 token 正确识别；Mimic3/Piper 加载时传入包内或共享的 `espeak-ng-data`。因 ONNX Runtime CUDA 会访问冲突并终止进程，Mimic3 固定安全回退 CPU。CLI 回归测试：`ScreenKit --test-tts-sherpa <模型名>`。
- **截图复制为路径**卡几秒：同进程刚用延迟位图写入剪贴板后，`Clipboard.SetText`/`OleSetClipboard` 仍会先 Flush 旧图。现改为纯 Win32 `EmptyClipboard` + `CF_UNICODETEXT`（只 Release、不渲染延迟格式）。遮罩关闭后再后台编码落盘；`capture_log` 记录 prep/encode/clip 耗时（≥500ms 标 `SLOW`）。CLI：`ScreenKit --test-clipboard-path`（含 4K 计时）。

## v1.0.5 (2026-08-31 ~ 09-01)

### English

#### Added

- HTTP `POST /api/qr` (aliases `/api/barcode`, `/api/barcodes`): barcode / QR only, no OCR. JSON `base64` / `path` or multipart; `data` is `[{type,text,box}]` (or text when `format=text`).
- Polish / translate prompt **Default** button (resets to Chinese or English default for the current UI language).
- Main-window **HTTP API** tab: brief call log and a manual request builder (templates for status / OCR / QR / ASR / TTS / ITN / translate / face).
- **HTTP proxy** (Settings → General, `http_proxy` / `http_proxy_addr`): used for GitHub, Hugging Face, and other non-China sites; China mirrors go direct. Default off, address `127.0.0.1:7897`.
- **Auto-update interval** (Settings → General, default 7 days, `update_check_days`; 0 = off). On startup only, if that many days have passed since the last successful check, query GitHub Releases. Tools → Check for Updates is unchanged.
- Edit menu **Copy file**: current image is saved to `screenshots/` then copied as FileDrop (paste in Explorer). **Copy image** now copies the bitmap (toolbar follows).
- Edit menu **Copy path**: copies the image file path (source path for opened files; screenshots / pasted images are saved to `screenshots/` first). **Copy text** removed from the menu (Ctrl+C and the result-panel button remain).
- Annotate toolbar: dropdown next to Done for copy-as image / file / path; choosing one finishes, copies, and updates the default mode. In-window panel (not ContextMenu) so it stays open under Topmost overlays.

#### Changed

- LLM thinking intensity adds **medium** and the dropdown shows raw API values `none / low / medium / high / max` (config unchanged; `none` still maps to `off`; `medium` / `mid` / `中` no longer fall back to `low`).
- HTTP JSON responses write UTF-8 Chinese directly (no `\uXXXX` escapes).
- `GET /api/asr/models` `type` is an English keyword (`SenseVoice` / `Transducer` / …), not a UI label such as `流式 Transducer`.

#### Fixed

- English UI: OCR **Pack** / **Lang** combos used Chinese labels (`Rapid 全语种`, `Umi-OCR（多语言）`, `简体中文`, …). Display names now come from `ocr-display.json` (`name` / `nameEn`) and optional per-pack `pack.json`.
- HTTP `POST /api/translate` always returned 940 (LLM not configured): the options snapshot used by the HTTP server omitted `[[llm]]` / `translate_llm`.
- HTTP JSON 500 (`TypeInfoResolver`): STJ 8 requires a type-info resolver on `JsonSerializerOptions` before first use. The UTF-8 CJK encoder options omitted it, so `GET /api/face/models` (and other `/api/*` JSON) returned code 900.
- OCR detection boxes are mapped back to original-image pixels after long-side limit / align-32 (HTTP `box` is also original-image coords).

### 中文

#### 新增

- HTTP `POST /api/qr`（别名 `/api/barcode`、`/api/barcodes`）：只识别条码/二维码，不跑 OCR。JSON `base64` / `path` 或 multipart；`data` 为 `[{type,text,box}]`（`format=text` 时为纯文本）。
- 润色 / 翻译提示词 **默认** 按钮：按当前界面语言重置为中文或英文默认提示词。
- 主界面 **HTTP接口** Tab：简短调用日志，并可按模板手动构造请求（status / OCR / QR / ASR / TTS / ITN / 翻译 / 人脸）。
- **HTTP 代理**（参数设置 → 常规，`http_proxy` / `http_proxy_addr`）：访问 GitHub、Hugging Face 等非中国网站时使用，国内镜像直连。默认关闭，地址 `127.0.0.1:7897`。
- **自动更新间隔**（参数设置 → 常规，默认 7 天，`update_check_days`；0=不自动检查）。仅在启动时判断：超过该天数未成功检查则查询 GitHub Releases。菜单「检查更新」不受限。
- 「编辑」菜单 **复制文件**：当前图片先存 `screenshots/`，以 FileDrop 复制到剪贴板（资源管理器可粘贴）。**复制图片**改为复制位图（工具栏同步）。
- 「编辑」菜单 **复制路径**：复制图片文件路径（打开的图用源路径；截图 / 粘贴的图先存 `screenshots/`）。菜单去掉 **复制文字**（仍可用 Ctrl+C 与结果区按钮）。
- 截图标注工具条「完成」旁增加下拉：复制为图片 / 文件 / 路径；点选后关闭标注并复制，同时把该方式设为默认（同步菜单/托盘/配置）。下拉用同窗面板（非 ContextMenu），避免全屏 Topmost 下弹出即关。

#### 变更

- LLM 思考强度增加 **medium**，下拉显示 API 原值 `none / low / medium / high / max`（配置不变；`none` 仍按 `off`；`medium` / `mid` / `中` 不再回落到 `low`）。
- HTTP JSON 响应直接输出 UTF-8 中文，不再使用 `\uXXXX` 转义。
- `GET /api/asr/models` 的 `type` 改为英文关键字（`SenseVoice` / `Transducer` 等），不再返回「流式 Transducer」这类界面文案。

#### 修复

- 英文界面顶栏 **Pack / Lang** 下拉仍显示中文包名与变体（Rapid 全语种、Umi-OCR（多语言）、简体中文 等）。现从 `ocr-display.json` 的 `name` / `nameEn`（以及包内可选 `pack.json`）读取显示名。
- HTTP `POST /api/translate` 一直返回 940（未配置翻译 LLM）：HTTP 用的配置快照漏了 `[[llm]]` / `translate_llm`。
- HTTP JSON 响应 500（缺 TypeInfoResolver）：STJ 8 首次使用序列化选项前必须指定 TypeInfoResolver；为输出 UTF-8 中文而加的选项漏了该项，导致 `GET /api/face/models`（以及其它 `/api/*` JSON）返回 900。
- OCR 检测在限制边长 / 对齐 32 后，把框坐标按缩放比映射回原图像素（HTTP `box` 亦为原图坐标）。

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
