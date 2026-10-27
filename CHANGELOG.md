# Changelog / 更新日志

All notable changes to ScreenKit are documented here. / 本文件记录 ScreenKit 的重要变更。

Format based on [Keep a Changelog](https://keepachangelog.com/). Versions are project milestones. / 格式基于 Keep a Changelog，版本号表示项目里程碑。

Each version has matching **English** and **中文** sections. GitHub Release notes follow the same bilingual order. / 每个版本同时有英文与中文章节；GitHub Release 说明同样先英后中。

## Versions / 版本索引

- [unreleased](#unreleased)
- [v1.0.29 (2026-10-23)](#v1029-2026-10-23)
- [v1.0.28 (2026-10-23)](#v1028-2026-10-23)
- [v1.0.27 (2026-10-21 ~ 22)](#v1027-2026-10-21--22)
- [v1.0.26 (2026-10-20)](#v1026-2026-10-20)
- [v1.0.25 (2026-10-17 ~ 18)](#v1025-2026-10-17--18)
- [v1.0.24 (2026-10-17)](#v1024-2026-10-17)
- [v1.0.23.1 (2026-10-17)](#v10231-2026-10-17)
- [v1.0.22 (2026-10-15 ~ 16)](#v1022-2026-10-15--16)
- [v1.0.21 (2026-10-14 ~ 15)](#v1021-2026-10-14--15)
- [v1.0.20 (2026-10-13 ~ 14)](#v1020-2026-10-13--14)
- [v1.0.19 (2026-10-10 ~ 12)](#v1019-2026-10-10--12)
- [v1.0.18 (2026-10-10)](#v1018-2026-10-10)
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

## unreleased

### English

#### Added

- Novel reader can speak with the current browser's own voices, without asking the app to synthesize audio. That engine shows an Install models link.
- Settings can set a hotkey for the web tools home (default Ctrl+Alt+H). The Tools menu and the tray show it, and the hotkey opens that page in the browser.
- The Chrome model page can play a short test line for an installed voice.
- Japanese word forms: verbs by class plus i-adjectives, na-adjectives and nouns, with politeness levels, auxiliary and modality patterns, dialect forms, and a speak button on each form.
- Korean word forms: verbs, adjectives and nouns by type, with speech levels, dialect and mood groups, and a speak button on each form.
- English word forms: verb tenses and moods, irregular verbs, adjectives and nouns, with British, American and Australian variants, and a speak button on each form.
- Russian word forms: verb conjugation, adjective and noun declension, with colloquial and dialect variants, and a speak button on each form.
- Chrome translation can now pick non-English language pairs such as Chinese to Japanese; Chrome pivots through English, so both the English to Chinese and English to Japanese models are needed.
- The local web server gzips only HTML, JS and CSS files when the client asks for it, and caches the compressed static files instead of recompressing them on every request. JSON API responses are no longer compressed.
- The HTTP API supports Keep-Alive: HTTP/1.1 reuses connections and an idle connection is held for at most 10 seconds (configurable with http_keepalive).
- Windows OCR splits a row of icon-separated segments into one result per segment instead of joining them into one line.
- Korean verbs: North Korean (Munhwao), DPRK broadcast and Yanbian Korean endings, including -ㅂ네다, -습네다, hapsyo -ㅂ니다, -시였습니다, -리라, -ㅁ다/슴다 and -ㄴ다이.
- The Japanese word-form tool now lists N5 to N1 verb patterns, classical (N0) forms and compound verbs, such as てもいい, てあげる, ざるを得ない and んばかり
- Web dictionary: the detail pane now has tabs; verbs, adjectives and nouns get a searchable word-forms tab (Japanese, Korean, English)
- Main window dictionary: the detail pane now uses tabs; verbs, adjectives and nouns get a searchable word-forms tab (Japanese, Korean, English)
- Dictionary word-forms tab: each form has a speaker button

#### Changed

- Novel reader uses the current browser's voices as the default reading engine.
- Novel reader Google voices use the offline pack. A missing pack is installed when reading starts, with a cancellable progress dialog.
- A voice install no longer switches the browser in front into reading mode.
- Novel reader keeps txt and md files in the browser instead of uploading them.
- The in-browser model voice list shows whether each Chrome voice pack is installed.
- Novel reader prepares the next sentence while the current one is still playing, so the pause between sentences is shorter.
- Refreshing the tools page rebuilds the tool scripts, so web tool edits show up without restarting.
- Install features marks an old ime.db as a new version. Install replaces it with the current lexicon, including Japanese spellings and conjugations; uninstall removes the old file.
- A partial Japanese reading lists every lexicon word that can still be completed.
- The Chrome model page shows whether the open profile belongs to the program or to the user.
- The web tools home drops the left category column, and file transfer is now a card in the tool list under Files.
- A Chrome voice pack shows its speaker count, and Chinese, Traditional Chinese, and Cantonese list every speaker in the pack.
- The build merges and minifies web scripts and styles; the slim package ships only merged, minified js/css.
- In Chinese + English recognition, numbers and Latin runs now come from the English pass, and the two results are no longer pasted twice; a line with one wide gap stays a single line instead of being split into columns.
- HTTP TTS: the Edge online voice list is cached for an hour instead of being fetched on every request.
- HTTP API: OCR, ASR, TTS, translation, face, chat, dictionary speech, and network lookups now run at most 8 at a time and answer 503 immediately beyond that, so /api/status and model lists stay responsive during a burst. The listener also caps connections at 256 and raises the thread-pool minimum.
- Korean verbs: stop listing forms that match standard Korean (자요, 더랬-, plain passive, broadcast 합쇼체/-리라)
- Dictionary: one- and two-character lookups scan key blocks in parallel instead of one single-threaded pass, about 0.5 s down to under 0.1 s
- WinRT calendar, Japanese readings and speech synthesis now share one resident STA thread instead of spawning a thread per request
- HTTP requests never pop the install dialog anymore; a missing runtime library returns error code 953
- HTTP concurrency details: fx rates download outside the cache lock with a shared HttpClient, usb.ids loads once, reader uploads read the body outside the file lock, status caches model scans for 2 s, and the font/encoding tables initialize under a lock
- LLM translation runs at most 2 at a time and retries 429/503 quickly; extra requests return code 503 after 300 ms
- Japanese, Korean and English word forms are now computed by the app itself and served by the new /api/conj endpoint; the web tools fetch the table from it
- Japanese word forms: N5-N0 grammar patterns now show at the end of the table
- Word forms, in the four word-form tools and in the dictionary on both the web page and the main window: a form name sits above its forms, several examples stack with one speaker button each, group names take their own row, and the table runs 1-4 columns in the tools and 2-3 in the dictionary.

#### Fixed

- Novel reader browser speech uses the system default playback device, so a Bluetooth headset plays in stereo instead of its hands-free output.
- Browser Microsoft voices such as Huihui follow the system default playback device.
- The Chrome model page no longer counts Windows voices such as Huihui as an installed Google offline voice.
- Chrome voice packs list immediately and can be cancelled, and reading mode is opened so offline voices can install.
- Chrome translation uses the manual proxy when the system proxy is off, and reports an error if Google cannot be reached.
- Translate and Speech open the Chrome model page on screen. The dedicated profile had kept the hidden Chrome's off-screen window.
- Opening an already running Chrome to install a voice pack did not send the reading-mode shortcut, so the speech component never started.
- Installing Chinese reported success when the Chrome voice list had no Mandarin pack, so a refresh showed it missing.
- Chrome offline speech failed immediately with not-allowed because the hidden page spoke without a user gesture.
- The status bar hid Chrome child processes while speaking, because the launcher process had already exited.
- Refreshing the Chrome model page no longer marks an installed voice pack as missing.
- Google voices in the in-browser model tool speak with the installed offline pack, so a proxy is not required.
- The Speech tab Chrome engine reads Traditional Chinese with Google 國語, the same voice as the in-browser model, instead of the slower on-device Natural voice.
- The window stopped responding for about a second every few seconds while the Chrome child process was open, because hiding that window waited on Chrome.
- Force-closing Chrome from the memory window also ends the browser that kept running after the launcher exited, so the app is no longer taken down a minute or two later.
- The Chrome model list in Install features reads the voice-pack record on disk, so it opens immediately instead of starting Chrome and waiting about half a minute.
- The Tools menu on the main window and in the tray now includes the novel reader and the dictionary under Text, matching the web tools page.
- Japanese input queries each reading with the matching parameter, so the added spellings show up as candidates.
- Web pronunciation works when a language uses Chrome's Google voice. Korean was refused because that engine does not return an audio file.
- With the IME off, the next-language hotkey opens the first enabled method in Wubi, Japanese, Korean order.
- Disabling Wubi stays off when settings are opened again.
- The Chrome window used for speech and translation no longer keeps a taskbar button.
- Installing a Chrome voice pack from the user's own Chrome still starts the speech component.
- Voice install can focus its own Chrome while the user's Chrome stays in front, so the speech component starts.
- Voice install waits until its Chrome window is in front and the page is ready before sending the reading shortcut.
- Voice install sends the reading shortcut to the browser window, not the empty Chrome widget.
- Chrome model page: the Chrome window now reaches 127.0.0.1 directly again, so the page shows which Chrome profile it is running in.
- Chrome model page runs in its own Chrome profile now, so installing or removing packs only restarts the background Chrome and no longer closes the page window.
- Chrome TTS no longer lists the six Chrome OS Chinese speakers; Windows Chrome exposes only one voice per language, so those six entries all played the same voice. The list now matches the voices Chrome actually reports.
- Chrome voice filtering now keeps Cantonese in its own language entry instead of listing it under Chinese.
- Show an installation prompt when Russian speech is unavailable.
- Web pages now pick up changed scripts and styles after a normal refresh: the cache version follows the modification time of the web files instead of the page's own mtime.
- Windows OCR reads characters near the image edge as fragments (语 as 讠吾, 则 as 贝刂); a band of background colour is now added around the image before recognition.
- Mixed Chinese + English recognition compared the cell box, already scaled back to the source image, with word boxes still in the enlarged image, so on upscaled screenshots the English numbers were never used; both are now compared at the same scale.
- Mixed recognition took the dollar sign as its own word and the English result brought it again, so prices came out as $$0.30; the sign now stays with the number.
- Idle model unload no longer waits behind a running synthesis or recognition, so the UI thread is not blocked.
- Temporary WAV and image files no longer collide when several requests land in the same millisecond; the name now carries a random suffix.
- Early error responses (503, 405, bad request) now drain the request body before closing, so clients read the JSON error instead of getting a connection reset.
- Chrome offline speech now opens its page when the app runs as administrator; Chrome restarts itself with lower rights and the launcher exits first, so the real browser is waited for and reused.
- Chrome speech fails after 10 seconds when the voice never starts, and cancels the stuck utterance, instead of waiting for the long timeout.
- Main window dictionary: the word-forms tab for Japanese nouns showed the group-1 verb error instead of noun forms.

#### Security / privacy notes

- POST /api/tts/speak, and /api/tts with the Chrome engine, now accept loopback callers only, so another device on the LAN can no longer make this PC speak.
- GET /api/dict/speak is loopback-only when that language speaks through Chrome, which plays on this PC instead of returning audio.
- Request bodies over 64MB are refused with HTTP 413 without being read into memory

### 中文

#### 新增

- 小说阅读器可以用当前浏览器自己的语音朗读，不经过程序合成。选中这个引擎时显示「安装模型」链接。
- 参数设置可配置 web 工具主页热键（默认 Ctrl+Alt+H）。工具菜单和托盘会显示该热键，按下后在浏览器打开工具页。
- Chrome 模型页可以为已安装的语音试听一句。
- 日语词形变化：动词按 1/2/3 类列出，也支持い形容词、な形容词和名词，含敬语、补助动词、语气句型和方言说法，每项可发音。
- 韩语词形变化：按类型列出动词、形容词和名词的变化，含语体、方言和语气分组，每项可发音。
- 英语词形变化：动词时态与语气、不规则动词、形容词和名词，含英美澳写法差异，每项可发音。
- 俄语词形变化：动词变位、形容词和名词变格，含口语和方言说法，每项可发音。
- Chrome 翻译可以选中文 ↔ 日文这类非英文语言对。Chrome 内部经英文中转，需要同时装好英文 ↔ 中文和英文 ↔ 日文的模型。
- 本机网页服务只对 HTML、JS、CSS 按 gzip 压缩，客户端支持才压；静态文件的压缩结果会缓存，文件没改不会重复压缩。JSON 接口不再压缩。
- HTTP 接口支持 Keep-Alive：HTTP/1.1 默认复用连接，空闲连接最多保持 10 秒（可用 http_keepalive 调整）。
- Windows OCR 把一行里用图标隔开的多段文字拆成多条结果，不再拼成一行。
- 韩语动词变化：新增朝鲜文化语、朝鲜播音体和延边朝鲜语词尾，如 -ㅂ네다、-습네다、합쇼체 -ㅂ니다、-시였습니다、-리라、-ㅁ다/슴다、-ㄴ다이。
- 日语词形变化新增 N5～N1 句型、文语（N0）和复合动词，如 てもいい、てあげる、ざるを得ない、んばかり
- 网页词典：详情栏改为多标签，动词/形容词/名词词条多出可搜索的「形态变化」页（日、韩、英）
- 主窗口词典：详情栏改成标签页，动词/形容词/名词词条多出可搜索的「形态变化」页（日、韩、英）
- 词典形态变化页：每行加了发音按钮

#### 变更

- 小说阅读器默认用当前浏览器自己的语音朗读。
- 小说阅读器的 Google 发音人改为走离线包。没装时在朗读前安装，并显示可取消的进度。
- 语音安装时不再把当前浏览器切进阅读模式。
- 小说阅读器的 txt、md 改存在浏览器里，不再上传到程序。
- 浏览器小模型选择 Chrome 发音时，会标出该模型是否已安装。
- 小说阅读器在读当前句时提前合成下一句，缩短两句之间的停顿。
- 刷新工具页会重新拼接网页工具脚本，改完不用重启程序。
- 安装功能里，旧的 ime.db 会标成有新版本。安装会换成当前词库（含日语写法和活用），卸载会删掉旧文件。
- 日语读音只打到一半时，候选列出还能接着输入的词。
- Chrome 模型页显示当前打开的配置是程序的还是用户的。
- web 工具主页去掉左侧分类栏，文件传输改成「文件」分类里的工具卡片。
- Chrome 语音包说明改为发音人个数。中文、繁体中文和粤语的发音人列表列出这一包里的全部发音人。
- 编译时合并并压缩 web 的 js/css；精简包只带合并、压缩后的 js/css。
- 中英混合识别时，数字和英文改用英文引擎的结果，不再和中文结果重复拼接；只有一个大空档的行仍是一整行，不当成分栏切开。
- HTTP 语音合成：Edge 在线发音人列表缓存一小时，不再每个请求都联网拉一遍。
- HTTP 接口：识别、合成、翻译、人脸、对话、词典发音和联网查询同时最多 8 个，超出的当场返回 503，突发请求时 /api/status 和模型列表仍能立刻响应。监听连接上限 256，线程池下限也调高了。
- 韩语动词变化：不再列出与标准语同形的说法（자요、더랬-、普通被动词、播音 합쇼체/-리라）
- 词典：一字、两字查询改成分块并行扫键段，不再单线程全表走一遍，约 0.5 秒降到 0.1 秒内
- WinRT 日历换算、日文读音、语音合成改用常驻 STA 线程，不再每个请求新建线程
- HTTP 请求不再弹安装提示，缺运行库时直接返回错误码 953
- HTTP 并发细节：汇率下载移出缓存锁并复用静态 HttpClient，usb.ids 单飞，reader 上传在锁外读请求体，status 的模型扫描缓存 2 秒，字体表/编码表加锁初始化
- LLM 翻译最多同时 2 路，429/503 快速退避重试；超出的请求 300 毫秒后返回 code 503
- 日/韩/英词形变化改由程序端计算，新增 /api/conj 接口，网页工具改为调用该接口渲染
- 日语词形变化：N5~N0 句型移到整张表的最后
- 词形变化（四个词形工具，以及网页和主窗口的词典）：形变名在上、例句在下竖排，每句一个发音按钮，分组名单独占一行；工具按宽度排 1～4 栏，词典 2～3 栏。

#### 修复

- 小说阅读器的浏览器朗读改走系统默认播放设备，蓝牙耳机走立体声，不再走免提。
- 浏览器里的微软语音（如 Huihui）改走系统默认播放设备。
- Chrome 模型页不再把 Huihui 这类 Windows 语音当成已安装的 Google 离线语音。
- Chrome 语音包列表会马上显示并且可以取消；打开朗读模式后才能安装离线语音。
- 未开系统代理时 Chrome 翻译改走手动代理；连不上 Google 会直接报错。
- 翻译和语音里打开 Chrome 模型页时，窗口回到屏幕内。专用配置里存过隐藏 Chrome 的屏幕外位置。
- 已经开着的 Chrome 里安装语音包时，朗读快捷键没有发到那个窗口，语音组件不会启动。
- 当前 Chrome 语音目录没有普通话包，安装会立刻报成功，刷新后又变成未安装。
- 隐藏页里的朗读没有用户手势，Chrome 立刻拒绝并报 not-allowed。
- 朗读时状态栏不显示 Chrome 子进程，是因为启动进程已经退出，实际在跑的是配置目录里的 Chrome。
- 刷新 Chrome 模型页不再把已经安装的语音包显示成未安装。
- 浏览器小模型里的 Google 语音改用已安装的离线包朗读，不必开代理。
- 语音合成的 Chrome 引擎读台湾中文时，改用和浏览器小模型一样的 Google 國語，不再改用更慢的离线 Natural 音。
- Chrome 子进程开着时，主线程每隔几秒会卡约一秒：藏窗口时在等 Chrome 自己处理消息。
- 在内存占用里强制关闭 Chrome 时，会把启动进程退出后仍在跑的浏览器一起结束，程序不再过一两分钟跟着退出。
- 安装功能里的 Chrome 模型列表改为直接读本机语音包记录，打开时不再启动 Chrome，也不再等大约半分钟。
- 主界面和托盘的「工具 → 文字」补上小说阅读器，词典也按网页分类放进这一组。
- 日语输入按读音查词时参数对上了，补进词库的写法会进候选。
- 语言选用 Chrome 的 Google 语音时，网页上的发音可以出声。韩语原先因为这种语音不返回音频而被拒绝。
- 输入法处于关闭时，切换下一个会按五笔、日语、韩语的顺序打开第一个启用的输入法。
- 关掉五笔后再打开设置，勾选保持关闭。
- 朗读和翻译用的 Chrome 不再留在任务栏上。
- 在用户自己的 Chrome 里安装语音包时，也会唤起语音组件。
- 用户的 Chrome 在前台时，安装语音仍能让程序自己的 Chrome 获得焦点并启动语音组件。
- 安装语音时等程序的 Chrome 窗口到前台并打开页面后，再发朗读快捷键。
- 安装语音时把朗读快捷键发给带页面的浏览器窗口，不再发给那个空白的 Chrome 窗口。
- Chrome 模型页：Chrome 窗口访问 127.0.0.1 改回直连，页面重新显示当前用的 Chrome 配置目录。
- Chrome 模型页改用单独的 Chrome 配置，装包、卸载只重启后台 Chrome，不再把模型页窗口关掉。
- Chrome 朗读不再把语音包里的 6 个 Chrome OS 中文发音人列成多个声音；Windows 版 Chrome 每种语言只有一个声音，原来那 6 项都落到同一个声音。现在列表只显示 Chrome 实际提供的声音。
- Chrome 语音筛选把粤语单独列出，不再混在中文里。
- 俄语语音不可用时提示安装，而不是只显示无法发音。
- 网页工具改过脚本或样式后，普通刷新就能取到新文件（版本号跟着 web 文件的修改时间走），不用再按 Ctrl+F5。
- Windows OCR 遇到贴着图像边的文字会把字认碎（「语」认成「讠吾」、「则」认成「贝刂」），识别前在四周补一圈背景色。
- 中英混合识别时，格子框已缩回原图，词框还是放大后的坐标，放大过的小图上取不到英文数字；现在按同一尺度比较。
- 中英混合时美元符号被单独认成一个词，换成英文结果后又带了一遍，价格变成 $$0.30；现在符号跟后面的数字一起整段处理。
- 空闲卸载模型时不再排队等正在进行的合成或识别，界面线程不会被卡住。
- 同一毫秒内的多个请求不再生成同名临时文件（临时文件名加了随机段），词典发音、SAPI 合成等不再偶发互相覆盖。
- 提前返回错误（503、405、请求非法）时先把请求体收掉再断开，客户端能读到 JSON 错误，不再表现为连接被重置。
- 程序以管理员身份运行时，Chrome 朗读不再报「Chrome 没有打开朗读页」：Chrome 会降权重启、启动器先退出，现在按调试口等真正的浏览器并复用它。
- Chrome 朗读 10 秒还没开始出声就判失败并取消卡住的那句，不再一直等到超时。
- 主窗口词典：日语名词的形态变化页显示「1类词尾要是う段假名」，没有名词变化内容。

#### 安全与隐私

- `POST /api/tts/speak` 以及 `/api/tts` 走 Chrome 引擎时只接受本机调用，局域网里别的设备不能再让这台机器出声。
- GET /api/dict/speak 在该语言走 Chrome 发音（只在这台机器出声）时，同样只接受本机调用。
- 请求体超过 64MB 直接返回 HTTP 413，不读进内存

## v1.0.29 (2026-10-23)

### English

#### Added

- Desktop note: added a pin button to keep the note above other windows; the pin state is saved with the note.
- IME settings: enable or disable Wubi, Japanese, and Korean individually; disabled languages are skipped by the cycle hotkey and their shortcuts do nothing.
- Web tools: the lookup tables are now translated into Japanese, Korean, Spanish, French, Portuguese, Russian and German as well as English
- 8-bit synth: octave -/+ buttons with a C1-C7 readout, same range as the MIDI synth
- Plot: 3D equations such as x^2+y^2+z^2=9 draw implicit surfaces; added a 3D equations example group (sphere, ellipsoid, hyperboloid, cone, plane, heart).
- Plot: added more examples - folium of Descartes, lemniscate, four-leaf rose, astroid, heart curve, torus, hyperboloid, monkey saddle and ripple surface.

#### Changed

- Web tools: the 12 lookup tables under Info (Windows/Linux commands, Linux software, HTTP status codes, MIME types, ports, file signatures, time zones, codes, key codes, USB IDs, SQL functions) now have English text, and search matches English too
- Missing translations now fall back to English first (web tools and the WPF interface), then to Chinese
- The adb server no longer runs alongside the app: it is only started while a USB debugging device is attached and is stopped again once the device is gone
- Cast tab: new Connect ADB checkbox; adb is only started while it is checked (USB casting), switches itself off after 10 minutes without a connection, and starts unchecked after a restart
- Plot: removed the 2D/3D mode buttons; the input type (function, 2D equation, 3D surface, equation with z) is detected automatically.
- Settings: added an Input method tab (language toggles, switch hotkeys, gVim/rare options, lexicon); the IME tray settings now opens this tab.

#### Fixed

- Web tools: switching the interface language while a lookup table is open now redraws the table right away, keeping the search text
- HTTP port stays occupied after exit when a child process (adb server) inherited the listening socket; sockets are no longer inheritable and a ghost holder is cleaned up before falling back
- IME: after pausing, the Tools item is no longer left checked and the tray menu no longer shows a language and Off checked together; clicking the Tools item while paused resumes the IME.
- Plot: 3D equations now follow the Curved/Grid/Dots/Contour style buttons instead of always drawing a dot cloud.
- Plot: the Curved style turned a double cone into a cylinder; 3D equations now draw every solution point so cones keep their hourglass shape.
- Plot: the 3D equation grid no longer leaves a gap where the two solution layers meet.

### 中文

#### 新增

- 桌面便笺：标题栏增加置顶图钉，可让便笺始终显示在最前面，置顶状态随内容一起保存。
- 输入法设置：可分别启用五笔、日语、韩语；关掉的语言不进切换循环，对应快捷键也不会切过去。
- 网页工具：信息查询的速查表除英文外，又补上日、韩、西、法、葡、俄、德七种语言的译文
- 8bit 合成器：加了八度 − / + 按钮和 C1–C7 显示，范围和 MIDI 合成器一致
- 函数图像：支持 x、y、z 的方程，例如 x^2+y^2+z^2=9 会画出球面；示例新增「三维方程」一组（球面、椭球、双曲面、锥面、平面、心形）。
- 函数图像：新增叶形线、双纽线、四叶玫瑰线、星形线、心形线、环面、双叶双曲面、猴鞍面和波纹曲面等示例。

#### 变更

- 网页工具：信息查询里的 12 个速查表（Windows/Linux 常用命令、Linux 软件、HTTP 状态码、MIME 类型、端口、文件头魔数、时区、国家/语言/货币、键盘码、USB ID、SQL 函数）补上英文，搜索也能用英文命中
- 缺译文时先回退英文、再回退中文，网页工具和 WPF 界面一致
- adb server 不再随程序常驻：只有 USB 上插着开了调试的设备时才拉起，设备拔掉后就收掉
- 投屏页新增「连接 adb」勾选框：勾上才启动 adb 给 USB 投屏做转发，10 分钟没有连接会自动取消，重启程序后默认关闭
- 函数图像：去掉二维/三维模式按钮，输入后自动识别函数、二维方程、三维曲面和含 z 的方程。
- 参数设置新增「输入法」页（语言开关、切换热键、gVim/生僻字、词库），输入法托盘菜单的设置改为打开该页。

#### 修复

- 网页工具：速查表打开时切换界面语言会立即重画，并保留搜索词
- 子进程（adb server）继承了监听套接字，退出后 HTTP 端口仍被占着；监听套接字不再被子进程继承，绑定失败时也会先清掉幽灵占用再重试设定端口
- 输入法：暂停（关闭）后「工具」菜单不再保持勾选，托盘菜单也不再同时勾选语言和「关闭」；暂停时点「工具」里的输入法项可恢复。
- 函数图像：三维方程也会跟随「曲面 / 网格 / 点阵 / 等高线」样式，不再一律画成点阵。
- 函数图像：曲面样式把双锥面画成了柱面；改为逐点绘制后恢复沙漏形状。
- 函数图像：三维方程的网格在两层解相接处不再断开。

## v1.0.28 (2026-10-23)

### English

#### Added

- The novel reader's dictionary popup can be resized from its edges and corners, and toggled to full screen from the title bar button or a title-bar double-click.
- File analysis adds image, audio, and video tabs; each shows only its own fields, and the overview keeps the rest.
- Image analysis shows JPEG quality, encoding, chroma subsampling, components and DPI, plus bit depth, color type, palette colors and transparency entries for PNG, GIF, BMP and TIFF.
- Audio and video tabs show the container, stream count, codec profile and level, pixel format, color space, field order, frame count, display aspect, channel layout and bit depth.
- Tools → Text adds Word/PDF to MD, which converts .docx or a text PDF to Markdown with a preview, copy, and save. PDF text is read through the installed PDFium.
- New Windows commands tool: PowerShell and CMD tables with categories, search and click-to-copy; the chosen shell is remembered
- Word/PDF to MD extracts document images into a chosen folder and writes Markdown image links. The default folder is images with 001.png, 002.png…, and {name} uses the document name.
- Windows commands gains a 语法帮助 group in every shell tab: 32 entries on CMD, Windows PowerShell, and pwsh quoting, variables, comparison operators, pipelines, redirection, and PowerShell 7 operators
- Markdown reader: export the current document to Word or PDF
- New SQL function lookup: MySQL, SQL Server, Oracle, SQLite and PostgreSQL tabs, 10 categories (strings, numbers, dates, casts, aggregation, window functions, conditions, JSON, metadata, misc), and the last database is remembered
- HTTP API: list engines and query models per engine for OCR, speech recognition, and translation
- MD reader: dropping a .lnk shortcut to a Markdown file opens its target
- MD reader: five themes (light, dark, sepia, eye care, midnight) with a theme picker in the WPF window and the web tool; the choice is remembered
- Command tools show a commented multi-line example for every command; the Linux tool also gains a Bash syntax group
- MD reader: click an image to view it full size, click again or press Esc to close (no animation); works in the WPF window and the web tool

#### Changed

- The novel reader's dictionary popup uses the dictionary page's layout and colors for its hit list and entry detail.
- File analysis shows the EXE/DLL sections as separate tabs instead of a drop-down list.
- File analysis reads media parameters from the ffmpeg64 shared libraries installed by the features page; ffmpeg lookup uses only that folder.
- Merge Linux command lookup into one tool with a distribution switch (Ubuntu, Debian, Deepin/UOS, RHEL, Fedora, openEuler, Arch, openSUSE, Alpine); the last distribution is remembered
- Windows commands adds a pwsh (PowerShell 7) tab beside Windows PowerShell and CMD, with pwsh-only commands such as ForEach-Object -Parallel, Get-Error, Get-Uptime and Test-Json
- HTTP TTS: Chrome voice packs are no longer installed automatically; a missing voice returns 927
- HTTP TTS: an unknown Chrome voice or language now fails with 927 instead of falling back to another voice
- Linux, Windows and SQL tools highlight syntax: SQL keywords/functions/strings/comments, and command names, parameters, variables, pipes, redirection and comments in PowerShell, CMD and shell rows
- Command tool examples now show right in the list, compacted without blank lines
- Number notation converter: rows whose result exceeds 400 characters are hidden

#### Fixed

- Status bar shows the blocked HTTP port and the temporary port when the port is in use
- MD reader: text now follows the Windows display scale instead of rendering at physical-pixel size
- MD reader: images whose path contains Chinese characters or spaces now load (IE cannot decode percent-encoded UTF-8 in file:// URLs)
- MD reader: URL-encoded relative image paths such as docs/1%20screenshot.en.png now resolve instead of doubling the escape
- Command tools scroll the list back to the top when switching categories and reset the detail pane, while clicking a row no longer moves the list

### 中文

#### 新增

- 小说阅读器的词典浮窗可以从四边、四角拖动改大小，点标题栏按钮或双击标题栏在窗口和全屏之间切换。
- 文件分析增加图像、音频、视频选项卡，各自只显示对应的信息，概要保留其余内容。
- 图像分析显示 JPEG 质量、编码方式、色度采样、颜色分量和 DPI，以及 PNG、GIF、BMP、TIFF 的位深、颜色类型、调色板颜色和透明色项。
- 音频、视频页显示容器、流数、编码配置和级别、像素格式、色彩空间、扫描方式、帧数、显示比例、声道布局和位深。
- 工具 → 文字增加「Word/PDF 转 MD」，把 .docx 或文本型 PDF 转成 Markdown，可预览、复制、保存；PDF 文字用已安装的 PDFium 提取。
- 新增 Windows 常用命令工具：PowerShell 和 CMD 两套命令，按类别分组、可搜索、点一下复制，并记住上次选用的命令行
- Word/PDF 转 MD 会把文档里的图片抽到指定目录并写成 Markdown 图片链接。默认 images 目录、001.png/002.png…，可用 {name} 换成文档名。
- Windows 常用命令在每个命令行页签里新增「语法帮助」组，共 32 条，覆盖 CMD、Windows PowerShell、pwsh 的引号、变量、比较运算符、管道、重定向和 7 专有运算符
- MD 阅读器：可把当前文档导出为 Word 或 PDF
- 新增 SQL 函数查询：MySQL、SQL Server、Oracle、SQLite、PostgreSQL 五个页签，按字符串、数值、日期、类型转换、聚合、窗口函数、条件、JSON、元数据、其他分类，并记住上次选的数据库
- HTTP 接口：OCR、语音识别、翻译增加查引擎、按引擎查模型
- MD 阅读器：可拖入指向 Markdown 的 .lnk 快捷方式
- MD 阅读器：新增明亮、暗色、米黄、护眼、午夜蓝五种主题，WPF 窗口和网页工具都能切换并记住选择
- 命令工具每条命令都配带注释的多行示例；Linux 工具新增 Bash 语法速查组
- MD 阅读器：点击图片放大查看，再点或按 Esc 关闭，无动画；WPF 窗口和网页工具都支持

#### 变更

- 小说阅读器的词典浮窗，列表和词条详情改用与词典页相同的排版与配色。
- 文件分析工具的 EXE/DLL 各分节改为独立选项卡，不再用下拉框切换。
- 文件分析改用安装功能提供的 ffmpeg64 共享库读取媒体参数，ffmpeg 查找只认这个目录。
- Linux 命令查询合并为一个工具，可切换 Ubuntu、Debian、Deepin/UOS、RHEL、Fedora、openEuler、Arch、openSUSE、Alpine，并记住上次选的发行版
- Windows 常用命令增加 pwsh（PowerShell 7）页签，与 Windows PowerShell、CMD 并列，补充 ForEach-Object -Parallel、Get-Error、Get-Uptime、Test-Json 等 7 专有写法
- HTTP 语音合成：Chrome 语音包不再自动安装，缺失时直接返回 927
- HTTP 语音合成：Chrome 发音人或语言不存在时直接返回 927，不再换用其它发音人
- Linux、Windows、SQL 三个工具的表格和示例增加语法高亮：SQL 的关键字、函数、字符串、注释，命令行的命令名、参数、变量、管道、重定向和注释
- 命令工具的示例改为直接在列表里显示，去掉空行更紧凑
- 数字表记转换：结果超过 400 字符的行不再显示

#### 修复

- HTTP 端口打不开时，状态栏显示原端口和临时端口
- MD 阅读器：正文按系统显示缩放排版，不再因高 DPI 显得过小
- MD 阅读器：修复图片路径含中文或空格时不显示（IE 无法解码 file:// 地址里的 UTF-8 转义）
- MD 阅读器：相对图片路径按 URL 编码写（如 docs/1%20screenshot.en.png）时不再重复转义，能正常显示
- 命令工具切换分类时回到列表顶部并重置详情面板，点行复制不再让列表跳动

## v1.0.27 (2026-10-21 ~ 22)

### English

#### Added

- Number names list FP64, FP32, FP16, BF16, and FP8 (E4M3, E5M2) as hex and a*2^x when the value is a finite float.
- File info adds an EXE/DLL page with sections, imports, exports, the signature, and public .NET types.
- If the HTTP port is busy, ScreenKit listens on a random localhost port instead, and menus and buttons open web pages on that port.
- Check for updates lists the notes for each version between the current one and the new one, and a version can be opened on its own.
- The selection popup keeps the same size with no hits, and Translate on the language row detects text and translates it into the native language.
- Lookup tools add HTTP status code and MIME type tables with search, category filters, and click-to-copy.
- Lookup tools add common ports, file signatures, time zones, country/language/currency codes, and key codes.
- Lookup tools add an Ubuntu command table and a Linux software table with package names and install commands.
- Tools page has a novel reader. Put txt/md files under web/up/reader, select text for the dictionary popup, and read aloud with Chrome offline or another voice. Reading starts at the top of the screen or at the selected word, and the current sentence is highlighted.
- The novel reader page uploads txt/md files dropped onto it and opens the first one.
- Chat draft thumbnails open a zoomable preview.

#### Changed

- Speech and translation open Chrome model installation in Chrome with this app's private profile.
- Help → Install features expects the current dict3.7z on the fixed dict-db release (279,822,816 bytes).
- Numbers longer than 10,000 digits show only the Chinese large-number reading and scientific notation.
- The large-number reading can repeat 无量大数, so exponents up to nine digits still show that reading and scientific notation.
- With the HTTP switch off, a random localhost port still serves the tools pages and the HTTP API.
- The selection popup skips the dictionary when the text has punctuation a word cannot contain, or is longer than a word, and still opens first.
- Selection popup translation has Auto on both languages. With both on Auto, native text goes to the learning language and other text goes to the native language. Switching language or translating keeps the popup width.
- Selection popup speak controls are icon buttons, with the selection one in the top bar. Switching back to Translate keeps the existing translation.
- Selection popup translation shows the resolved source and target languages, keeps the language menus compact, and lets the source text be edited.
- The selection popup restores its last size, and a Read tab speaks the text sentence by sentence without closing or watching new selections.
- The selection popup can be dragged, and Read aloud highlights the sentence currently being spoken.
- Expired screenshots are cleaned in the background at startup and again every 24 hours while the program keeps running.
- Read aloud in the selection popup can use the Chrome engine and its voices.
- Temporary web ports, including Chrome's debugging port, listen on localhost only.
- Read aloud in the selection popup has a speed control shown as 1x; click it to open a slider.
- Dictionary speech settings are a table. Chinese, Japanese, Korean, and English always stay, and other languages can be added. Selection-popup Read uses that row when the engine is Auto.
- Settings splits Speech into Speech synthesis and Speech recognition. The per-language speech table is under Speech synthesis.
- Polish, chat, and translation prompts, plus the translation batch size, are set under Settings, LLM. Speech recognition keeps the polish switches. Translate keeps the native and learning languages.
- The Linux software table is a single cross-distribution list of open-source software.
- Pasted chat images stay as drafts and send with the text; text and images appear as separate messages.
- The novel reader and the dictionary are separate pages at /reader and /dict, opened from link cards under Text on the tools page: a click opens the page in this tab and Ctrl+click opens a new window. Both pages have a logo that returns to the tools home.

#### Fixed

- Chrome translation download stops after about 20 seconds without new bytes, and closes that Chrome.
- Scientific notation with a six-digit exponent, such as 1e193321, still shows the large-number reading and scientific notation when it fits.
- Selection popup Chrome speech installs a missing voice pack and retries, and the hidden Chrome window is no longer muted.
- Pasting the same chat image no longer creates duplicate drafts.
- Reading aloud in the novel reader no longer stops after one sentence: a stop request that arrives late no longer cancels the next sentence.
- Reading aloud with Windows, SAPI, Edge, or local models continues to the next sentence; the end-of-audio callback was not being called.

### 中文

#### 新增

- 数字表记在数值能转成有限浮点数时，列出 FP64、FP32、FP16、BF16 和 FP8（E4M3、E5M2）的十六进制，以及 a*2^x 里 a、b、x 的取值。转不成浮点数时不显示这些行。
- 文件分析在打开 EXE 或 DLL 时多出一页，列出节、导入导出、签名和 .NET 公开类型。
- HTTP 端口打不开时改听本机随机端口，菜单和按钮打开网页时用这个端口。
- 检查到新版本时列出当前版本到新版本之间各版说明，可点版本号只看该版。
- 划词浮窗没查到词时大小不变。语言栏的翻译会自动识别并译成母语，改语言后要点一次翻译。
- 信息查询增加 HTTP 状态码表和 MIME 类型表，可搜索、按类别筛选，点一行复制。
- 信息查询增加常见端口号、文件头魔数、时区表、国家/语言/货币代码和键盘码。
- 信息查询增加 Ubuntu 常用命令表和 Linux 常用软件表，可查命令示例、包名和安装命令。
- 工具页新增「小说阅读器」：txt/md 放在 web/up/reader 下管理，划词查词典；可用 Chrome 离线等发音人朗读，从屏幕最上面或选中的词开始，逐句高亮当前句。
- 小说阅读器页支持把 txt/md 文件直接拖进来上传，拖完打开第一篇。
- 聊天室草稿缩略图可点击放大预览。

#### 变更

- 语音合成和翻译页的「Chrome模型安装」用本程序的专用配置打开 Chrome，进入模型页。
- 帮助 → 安装功能按固定 dict-db 上当前 dict3.7z 的大小校验（279,822,816 字节）。
- 超过一万位的数字只显示中文大数和科学计数，不再整段报太长。
- 大数可重复「无量大数」，指数最多九位，仍只显示大数和科学计数。
- 关掉 HTTP 接口时，仍在本机随机端口提供工具页和 HTTP 接口。
- 划词选区含单词里不会出现的标点，或长过一个单词时，不查词典，浮窗仍先弹出。
- 划词翻译的源语言和译文语言都可以选自动。两个都是自动时，母语译成学习语言，其它译成母语。切换词典语言或翻译时浮窗宽度不变。
- 划词浮窗的三个发音改为图标，选区发音放到顶部栏。已经翻译过再切回翻译时保持原文和译文，不再重译。
- 划词翻译标出实际的源语言和目标语言，语言下拉不再拉满一行，原文可以修改。
- 划词浮窗会记住上次大小。朗读页按句朗读，打开时浮窗不自动关闭，也不接收新的划词。
- 划词浮窗可以拖动。朗读时会高亮当前正在读的句子。
- 过期截图在启动时后台清理，程序一直开着时每 24 小时再清一次。
- 划词浮窗朗读可以选用 Chrome 引擎和对应发音人。
- 临时网页端口（含 Chrome 调试端口）只监听本机，不接受局域网访问。
- 划词朗读增加语速，显示为 1x，点击后弹出滑块。
- 词典发音改为按语言的表格。中文、日语、韩语、英语始终保留，其它语言可以添加。划词浮窗朗读选自动时用这一行的引擎、发音人和语速。
- 参数设置的「语音」拆成「语音合成」和「语音识别」。按语言的发音表放在「语音合成」里。
- 润色、对话和翻译的提示词，以及翻译的一批条数，改到参数设置的「LLM接口」。语音识别仍保留是否润色；翻译页仍保留母语和学习语言。
- Linux 常用软件表改为跨发行版的开源软件列表。
- 聊天室粘贴图片先显示为草稿，随文本一起发送；文本和图片分开发送和显示。
- 小说阅读器和词典都是单独页面（/reader、/dict），在工具页「文字」分类下的链接卡片打开：点击在当前页打开，Ctrl+点击开新窗口。两页都有回到工具页首页的 Logo。

#### 修复

- Chrome 翻译模型下载若约 20 秒没有新数据就停止，并关掉这只 Chrome。
- 1e193321 这种六位指数不再直接报太长，大数能读时仍显示大数和科学计数。
- 划词浮窗的 Chrome 朗读在没声音时会安装语音包再读一次，隐藏的 Chrome 窗口不再被静音。
- 聊天室同一张图片粘贴时不再重复生成草稿。
- 小说阅读器朗读不再读完一句就停：晚到的停止请求不会打断下一句。
- 用 Windows、SAPI、Edge 或离线模型朗读时，播完一句会继续下一句；此前音频结束的回调没有接到。

## v1.0.26 (2026-10-20)

### English

#### Added

- The 8-bit synthesizer shows a live waveform of what is playing.
- The MIDI synthesizer shows a live waveform of what the computer is playing.

#### Changed

- The selection popup also works in this program. Dictionary entries always use that same popup, and the dictionary page no longer has its own. Drag the lower-right corner to resize; the size lasts until you quit.
- The IME tray menu shows each language hotkey, and Close shows the cycle hotkey.
- Chrome translation and speech list only installed languages. Speech has the same Chrome model install link.
- Dictionary lookup reads dict3.bin (RDS1 format) instead of the SQLite dict3.db. Opening it keeps only the block directories, so a cold load adds about 1 MB of commit memory instead of about 130 MB. Install features extracts dict3.bin from dict3.7z.
- The dictionary releases its block directories after the idle timeout, so the ~1 MB is held only while the dictionary is in use.
- Dictionary result backfill reads entries block by block instead of one block per hit, and fills only the list fields. A 4000-hit lookup drops from several seconds to about 0.5 s.
- Dictionary results appear as soon as matching finishes; a row reads its title and preview when it scrolls into view, so long hit lists no longer wait for every entry.
- The memory window's unload button is now Unload all: it unloads every model, the ONNX runtime, the dictionary, and the Chrome helper, then runs a full GC.
- Dictionary install and uninstall remove old dict.db and dict2.db files beside the program.

#### Fixed

- Dictionary selection popup sat far from the selected word because the screen position was scaled twice.
- Dictionary list keeps the same entry selected when switching the dictionary language; if the entry is not in the new dictionary the list returns to the top.
- Chinese readings join 万 into the same 亿 group, so 1000100000000 is 一万零一亿.
- Chinese-to-pinyin results are lowercase, including initials.
- Chrome speech language list includes languages that already have a local voice.
- The MIDI waveform is scaled up so a quiet system synth still fills the display.
- The MIDI waveform stays phase-locked, so a periodic tone no longer drifts sideways.
- The 8-bit waveform stays phase-locked, so a periodic tone no longer drifts sideways.
- Input method hotkeys are not registered while ime.db is missing; installing it registers them without a restart.
- Dictionary lookups of four or more characters no longer skip infix matches when prefix results exist; dict returns 70 English hits instead of 14.
- Korean romanized input restores conjugated forms, so gapnida returns the same hits as 갑니다 (previously none).
- OCR on CPU loads onnxcpu64's onnxruntime.dll instead of the GPU package; switching to GPU afterwards asks for a restart.

#### Removed

- The tray menu item OCR from clipboard is removed.

### 中文

#### 新增

- 8bit 合成器显示正在播放声音的实时波形。
- MIDI 合成器显示本机正在播出声音的实时波形。

#### 变更

- 划词浮窗在本程序里也同样弹出。词典词条始终用这同一个浮窗，词典页自己的浮层已去掉。右下角可拖动改大小，本次运行会记住。
- 输入法托盘菜单在五笔、日语、韩语旁显示各自热键，关闭旁显示轮流切换热键。
- Chrome 翻译和语音合成只列出已安装的语言。语音合成页增加与翻译相同的 Chrome 模型安装链接。
- 词典改为读取 dict3.bin（RDS1 格式），不再打开 SQLite 的 dict3.db。打开时只常驻分块目录，冷启动提交内存约增加 1 MB，原来约 130 MB。安装功能从 dict3.7z 解出 dict3.bin。
- 词典闲置关闭时一并释放分块目录，那约 1 MB 只在查词期间占用。
- 词典结果回填改为按块连续读，并且只解析列表要用的字段；四千条命中的查询从数秒降到约 0.5 秒。
- 词典列表在命中后立即显示，标题和预览等该行滚到可见处才读；命中很多的长列表不再等全部回填。
- 内存占用窗口的「卸载」改为「全部卸载」：把所有模型、ONNX 运行库、词典和 Chrome 辅助进程一并卸掉，再强制回收。
- 词典安装和卸载会删掉程序旁的旧 dict.db 和 dict2.db。

#### 修复

- 词典里划词浮窗离选中的字很远，是因为屏幕坐标被缩放了两次。
- 切换词典语言后仍选中原来那条词条、列表滚到它；该词条不在新词典里则回到顶部。
- 中文读法把万并进同一节的亿，1000100000000 读作一万零一亿。
- 中文转拼音的结果改为小写，首字母也是小写。
- Chrome 语音的语言下拉会列出本机已有语音的语言。
- MIDI 波形按峰值拉开，系统合成器声音小也能看清起伏。
- MIDI 波形按上升过零对齐，周期信号不再左右飘。
- 8bit 合成器的周期波形按上升过零对齐，不再左右飘。
- ime.db 未安装时不注册输入法热键；装好后立即注册，无需重启。
- 四字以上的查词在前缀已有结果时也继续查中缀，dict 的英文结果从 14 条补到 70 条。
- 韩语罗马字输入会还原活用形，gapnida 与直接输入 갑니다 结果一致（原来查不到）。
- OCR 选 CPU 时不再加载 onnxgpu64 的 onnxruntime.dll，优先用 onnxcpu64；之后再切到 GPU 会提示重启。

#### 移除

- 移除托盘菜单「从剪贴板识别」

## v1.0.25 (2026-10-17 ~ 18)

### English

#### Added

- Translation auto mode is configurable: **Settings → Translate** adds **Native language** and **Learning language** (default zh/en). Text that is not native is translated into the native language; native text into the learning language. The translate tab, popup, web tool, CLI, and `POST /api/translate` all follow the pair.
- Auto translation now shows the detected direction in the translate tab and popup (for example `Auto detect: English → Chinese`) and updates it as you type.
- On the Translate tab, **Ctrl+Enter** starts translation (same as the translate popup).
- Wubi, Japanese, and Korean input takes custom lexicons. The lexicon window (IME tray menu, or Ctrl+Shift+F7) imports a text lexicon, exports the user words, and deletes entries; the text under dict/ next to the program is the source of truth, so a reinstalled ime.db restores it. Wubi is code + word, Japanese reading + word (kana or romaji), Korean just the word. --ime-dict-import <wubi|ja|ko> <file> imports from the command line.
- Wubi candidates can skip rare characters: entries in the lexicon with no word frequency (CJK extension characters, uncommon idioms) are hidden by default. The Rare characters item in the IME tray menu, or the checkbox in IME settings, brings them back. Words you pinned or imported are always shown.
- Japanese numbers: the row now shows the kanji form with the kana reading in parentheses, e.g. 一京（いっけい）. The speak button still reads only the kana.
- Translate tab: a Chrome models link on the hint line opens the model page in the local Chrome.
- Web tools translate card: added engine and model pickers. The engine list is auto / local ONNX / Chrome / every LLM endpoint from Settings; the model list shows local ONNX models only, and picking one moves the source and target languages to that model's direction.
- HTTP translate: GET /api/translate/langs now also returns the engines and the local ONNX models; POST /api/translate takes engine=auto|onnx|chrome|llm, and with engine=onnx the model field picks a local model by direction key or folder name.
- Character art: the line modes offer three character sets (basic, rich ASCII, box drawing); corners, tees, and wide strokes draw with matching characters.
- Character art: a new dot shading mode picks · ░ ▒ ▓ █ by brightness.

#### Changed

- Rare characters are now judged by the character tables instead of word frequency alone: a candidate is rare when the lexicon gives it no word frequency, when it holds a character GBK cannot encode (CJK extension planes, e.g. 𠛈), or when it is outside the GB2312 7000-character set and rarely used. Frequently used characters missing from that table (啰, 囧, 喆) stay visible, and rare words you picked earlier now follow the switch too — pin one to keep it always shown.
- Input method: your own lexicon, pins and pick counts now live in ime_user.db next to the program (custom / pin / pick tables); ime.db holds only the lexicon, so the file you upload carries no personal words or usage. --ime-db-clean [src] [out] writes a cleaned copy.
- Character art: the Lines checkbox is gone; the mode list now holds Shades ASCII (default) plus the line modes and dot shading, with the earlier per-cell drawing back as Legacy.

#### Fixed

- Wubi/Japanese typing no longer drops keys. Drawing the candidate bar inside the keyboard hook could exceed the Windows hook timeout (300 ms), after which the system silently removed the hook and letters or Backspace stopped responding; the bar is now drawn after the hook returns and pre-rendered when the IME is switched on. The lexicon is warmed up on a background thread, and the tray icon is only touched when it changes.
- Number notation: the middle-number reading advanced by 10^8, so 1e16 read the same as the large-number reading. It now advances by 10^4, so 1e16 is a jing (一京).
- Japanese reading: 1e16 read いちけい. Units starting with a voiceless consonant now take the 促音 (六京 ろっけい、百京 ひゃっけい), and a は行 unit turns semi-voiced after it.
- Korean: the native-numeral row used native digits for the fraction (0.12345 read 영 점 하나둘셋넷다섯). Native numerals do not cover decimals, so that row stays blank and the Sino-Korean row reads 영 점 일이삼사오.
- Tool windows opened from the Tools menu are no longer owned by the main window: they no longer minimize with it or get pulled up by its menu.

### 中文

#### 新增

- 翻译的自动互译可配置：**参数设置 → 翻译** 增加 **母语** 与 **学习语言**（默认 zh/en）。非母语文本译成母语，母语文本译成学习语言。翻译页、翻译小窗、网页工具、命令行和 `POST /api/translate` 都按这对语言互译。
- 翻译页和翻译小窗在「自动」时显示判断结果（如「自动检测：英文 → 中文」），随输入实时更新。
- 翻译页支持 **Ctrl+Enter** 直接开始翻译（与翻译小窗一致）。
- 五笔、日语、韩语输入支持自定义词库。「词库」窗口（输入法托盘菜单，或 Ctrl+Shift+F7）可导入文本词库、导出用户词、删除词条；程序旁 dict/ 下的文本是词库本体，重装 ime.db 后自动恢复。五笔是「编码 + 词」，日语「读音 + 词」（假名或罗马字），韩语只有「词」。命令行 --ime-dict-import <wubi|ja|ko> <文件> 也能导入。
- 五笔候选可以不要生僻字：词库里没有词频的字词（CJK 扩展区、罕用成语）默认不显示，输入法托盘菜单的「生僻字」，或输入法设置里的勾选框可以打开。自己置顶和导入的词照常显示。
- 日语数字：写成「汉字（读法）」，如 一京（いっけい）；发音仍只念括号里的读法。
- 翻译页提示行右侧新增「Chrome模型安装」链接，用本机 Chrome 打开 Chrome 模型页。
- 网页工具翻译卡增加「引擎」和「模型」两个下拉。引擎可选自动、本地 ONNX、Chrome 和参数设置里的每个 LLM 端点；模型只列本地 ONNX 模型，选定后源/目标语言跟着模型方向走。
- HTTP 翻译：GET /api/translate/langs 同时返回可选引擎和本地 ONNX 模型；POST /api/translate 支持 engine=auto|onnx|chrome|llm，engine=onnx 时用 model 按方向键或目录名指定本地模型。
- 字符画：线条画法有三档字符集（基本、丰富 ASCII、制表符框线），拐角、三岔和宽线都按方向用对应字符画出。
- 字符画：新增「点阵阴影」模式，按亮度在 · ░ ▒ ▓ █ 之间取字符。

#### 变更

- 生僻字改按字表判定：词库没给词频的行、含 GBK 编不进的字（CJK 扩展区，如 𠛈）、以及 GB2312 七千字表外又很少用的字都算生僻。表外的常用字（啰、囧、喆）照旧显示；以前选用过的生僻词现在也跟着开关走，想一直在就置顶。
- 输入法：自定义词库、置顶和选用次数改存程序旁的 ime_user.db（custom / pin / pick 三张表），ime.db 只剩词库本体，上传的 ime.db 不再带个人词和使用记录；--ime-db-clean [源] [产物] 可另存一份干净的。
- 字符画：去掉「线条」勾选框，改成「模式」下拉，含明暗 ASCII（默认）、点阵阴影和几种线条画法；改版前每个格子只判一个方向的画法作为「旧版」保留。

#### 修复

- 五笔/日语打字不再丢键。候选条原来在键盘钩子里现建现画，一旦超过系统的钩子超时（300 毫秒），系统会悄悄摘掉钩子，之后字母和退格就没了反应。现在候选条改在钩子返回后再画，开启输入法时先离屏渲染一遍；词库在启动时由后台线程预热，托盘图标只在真变了才碰。
- 数字表记：中数原来按万万（10^8）递进，1e16 与大数同为「兆」；改为按万（10^4）递进，1e16 读作「一京」。
- 日语读法：1e16 原来读 いちけい。清音开头的单位前改按促音读，如 六京 ろっけい、百京 ひゃっけい；は行促音后变半浊音。
- 韩文固有：小数原来按固有数词逐位读（0.12345 是 영 점 하나둘셋넷다섯）。固有数词不用于小数，这一行改为留空，小数由韩文汉字行读，如 영 점 일이삼사오。
- 工具菜单打开的工具窗不再挂主窗为父窗口：不跟着主窗最小化，也不会被主窗菜单带起。

## v1.0.24 (2026-10-17)

### English

#### Changed

- Opening the dictionary keeps one copy of the index and uses a bigram index for contains-search. A cold load of dict3.db adds about 130MB of commit memory instead of about 530MB.
- Help installs the dictionary from dict3.7z on the fixed release dict-db and extracts dict3.db.
- Dictionary search converts Japanese romaji to hiragana and Korean romanization to Hangul (aligned with rustdict).
- The tray menu can turn the dictionary selection popup on or off, and shows its hotkey.
- Windows OCR mixed Chinese-English splits a line into cells first, then keeps Latin from the English pass without dropping Chinese.

#### Fixed

- OCR no longer inserts a space before @ or / or after - or @.
- Korean romanized dictionary search skips when Japanese romaji applies; romanized Korean uses prefix only, not infix.
- Dictionary search runs both Japanese romaji and Korean romanization under All; Korean roman hits use prefix only.

### 中文

#### 变更

- 打开词典只保留一份索引字节，包含查询改走双字倒排。冷启动加载 dict3.db 的提交内存大约增加 130MB，不再是大约 530MB。
- 帮助里的词典安装改为从固定 Release dict-db 下载 dict3.7z，解出 dict3.db。
- 词典查询会把日语 romaji 转成平假名、韩语罗马字转成谚文再搜（与 rustdict 一致）。
- 托盘菜单可开关划词浮窗，并显示对应热键。
- Windows OCR 中英混合先按格拆行，格内用英文结果替换拉丁块，汉字仍保留。

#### 修复

- 识别结果在 @、/ 前和 -、@ 后不再多插空格。
- 韩语罗马字检索在日语 romaji 已成立时跳过；罗马字转谚文只走前缀，不走中缀。
- 词典「全部」下同时做日语 romaji 与韩语罗马字检索；韩语罗马字结果只走前缀。

## v1.0.23.1 (2026-10-17)

本版包含原计划的 v1.0.23 全部变更；对外仅发布 v1.0.23.1。/ This release includes all changes planned for v1.0.23; only v1.0.23.1 was published.

### English
#### Added
- Chrome text selection shows a placeholder toolbar. Copy uses the text that was read; the other buttons only show a note.
- The dictionary page and Dictionary settings can turn on a selection popup, with a hotkey that toggles it. A drag or double-click in another program shows the same lookup popup as the dictionary.
- Help → Install features can download the input-method file ime.db from the fixed dict-db release. Korean input then lists words, with Hanja notes.
- The in-browser model tool links to the Chrome model page, where translation models and offline voice packs can be viewed and installed.
- The in-browser model Speak task can filter voices by language.

#### Changed
- Screenshots default to JPG at quality 60. A saved format and quality in config.toml stay as they are.
- The selection popup can choose All, Chinese, Japanese, Korean, or English, and lists up to 100 hits that scroll.
- File info lists Explorer-style PE details, platform, and .NET target framework
- Korean input uses the installable ime.db for Wubi, Japanese, and Korean, with the same candidate bar. Hotkeys cycle languages or jump to one language or off.
- File info media fields split duration (ms), resolution, codecs, and bit rates
- LLM chat and the LLM log are hidden by default.
- File info size shows KB/MB with exact byte count
- Turning the input method off hides its tray icon.
- IME requires installed ime.db; menu and hotkeys stay off until then
- Dictionary selection popup has Close and turn-off controls
- Optional gVim mode sync (Settings → Hotkeys, default on): normal forces English; insert or replace restores the last insert language; Esc returns to English immediately (queries Vim mode()).
- The input method tray menu opens a dedicated input method settings window (language hotkeys and gVim mode). It no longer opens the main Settings window.
- Full-monitor capture is now repeat-last-region capture (hotkey, menu, tray). The region is saved after Screenshot OCR or annotate.
- Repeat capture copies as an image, shows it on the Screenshot OCR tab, and does not force the main window forward.
- Wubi, Japanese, and Korean IME also handles keys in ScreenKit's own windows.
- The tray menu adds repeat-last capture with the shortcut shown on the item.

#### Fixed
- Checking screenshot OCR or translation also checks the ONNX CPU row and counts its size once. After install that row stays checked instead of turning red for removal.
- OCR overlay text keeps its normal shape. Short lines leave the rest of the box blank instead of stretching the glyphs.
- iGPU DirectML size follows the NuGet packages to download (about 205 MB). The progress total grows when a package is larger than the estimate.
- POST /api/qrcode matches Umi-OCR. Each code includes format (QRCode for a QR code) and text.
- A selection lookup opens the dictionary file when it is not open yet, then searches.
- Korean input shows one jamo per key. w is ㅈ, not the compound ㄽ.
- System voices in the in-browser model, such as Microsoft Huihui, are synthesized by this app and played on the system default output. Google voices stay in the browser.
- Align IME candidate bar with gVim and WezTerm caret (ported from standalone IME)
- Fix IME and selection popups near gVim caret (no mouse fallback, physical DPI)
- Align gVim caret query and popup DPI with standalone ime
- WezTerm cursor-agent popup anchor (arrow line, sync read)
- Turning the input method on no longer stalls the keyboard. The hook only reads a cached caret, a swallowed key-up is not armed again, and the hook is reinstalled every two seconds.
- Korean candidates include words that can still be typed when the last syllable is only partial, such as ann-n matching annyeong.
- Hotkey display shows Ctrl+` instead of Ctrl+Oem3 for the grave key.

#### Removed
- Remove legacy hotkey_hangul (Ctrl+Alt+K) setting and global toggle
- Removed the duplicate Close item from the input method tray menu. The Off entry in the language list still turns input off.

### 中文
#### 新增
- 在 Chrome 里划选文字会弹出占位工具条。复制会写入读到的文字，其余按钮只提示。
- 词典页和词典参数可以打开划词浮窗，并设置切换这个开关的快捷键。在其它程序里拖选或双击时，会弹出和词典里一样的查词浮窗。
- 帮助 → 安装功能可从固定的 dict-db 下载输入法 ime.db。装上之后，韩语输入会列出词，汉字只作注释。
- 浏览器小模型工具增加 Chrome 模型页链接，用来查看和安装翻译模型与离线语音包。
- 浏览器小模型的朗读可以按语言筛选声音。

#### 变更
- 截图默认保存为 JPG，质量 60。config.toml 里已经写过的格式和质量保持不变。
- 查词浮窗可以选全部、汉语、日语、韩语、英语，列表最多 100 条，可以滚动查看。
- 文件分析增加与资源管理器详细信息相近的 exe/dll 字段、PE 平台与 .NET 目标框架
- 韩语输入改用可安装的 ime.db，候选条支持五笔、日语和韩语。快捷键可以轮换语言，或切到某一种语言和关闭。
- 文件分析音视频拆分为毫秒时长、分辨率、编码与码率等字段
- LLM对话和 LLM日志默认不显示。
- 文件分析大小显示 KB/MB 并附带精确字节数
- 输入法切到关闭后隐藏托盘图标。
- 输入法须先安装 ime.db，未安装时禁用菜单与快捷键
- 划词查词浮窗增加关闭与关闭划词浮窗按钮
- gVim 可选「随模式切换」（参数→热键，默认开）：普通模式英文，插入/替换恢复上次插入中英，Esc 立即英文（查询 Vim mode()）。
- 输入法托盘菜单的「参数设置」改为打开单独的输入法设置窗口，只含输入法热键和 gVim 模式，不再打开主程序参数设置窗口。
- 整屏截屏改为重复上次框选区域（热键、菜单、托盘）；区域在截图识别或标注成功后记住。
- 重复截图复制为图片、在截图识别页显示，且不强制弹出主窗。
- 五笔/日语/韩语输入法在本程序窗口内同样接管按键。
- 托盘增加重复上次截图，并显示热键。

#### 修复
- 勾选截图识别或翻译时会同时勾上 ONNX CPU，体积在合计里只算一次。装完后该项保持勾选，不再显示为红色待删除。
- 识别结果的叠加文字按正常字形显示。短行右侧留白，不再把字拉满整行。
- 核显 DirectML 的安装大小按要下载的 NuGet 包计算（约 205 MB）。包比预估更大时，进度分母跟着加大。
- POST /api/qrcode 与 Umi-OCR 兼容。每条码带 format（二维码为 QRCode）和 text。
- 词典库还没打开时，划词会先打开库再查词。
- 韩语输入每个键一个单音。w 是 ㅈ，不再打出合用收音 ㄽ。
- 浏览器小模型里的系统语音（如微软慧慧）改由本程序合成，并在系统默认输出设备上播放。Google 语音仍由浏览器朗读。
- 输入法候选条在 gVim、WezTerm 下按终端/编辑器光标定位，并修正高 DPI 坐标
- gVim 下输入法候选条与划词浮窗贴近编辑器光标，不再误用系统插入符/鼠标
- gVim 光标查询与候选/划词浮窗 DPI 对齐独立 ime，并修编译错误
- WezTerm/cursor-agent 浮窗对齐底部输入行，修正偏上
- 开启输入法不再把键盘卡住。钩子只读光标缓存，被吞掉的键抬起不会再次登记，钩子每两秒重新装上。
- 韩语最后一个字只打了部分音节时，候选仍包含还能继续打出的词，例如안ㄴ对应안녕。
- 热键界面将 Ctrl+Oem3 显示为 Ctrl+`。

#### 移除
- 删除旧的 hotkey_hangul（Ctrl+Alt+K）配置项与全局切换热键
- 去掉输入法托盘菜单里重复的「关闭」项；语言列表里的「关闭」仍可关闭输入。

## v1.0.22 (2026-10-15 ~ 16)

### English

#### Added

- System tools list processes, file locks, environment variables, startup items, installed programs, services, hotfixes, store apps, and file associations. Services are listed only.
- System info shows each memory module and the motherboard.
- File info compares a file header with its extension and shows version, image, media, and text encoding.
- Sound recorder can include system audio. Its device list shows microphones only.
- Help → Install features adds Single components. It installs or removes one package at a time, such as FFmpeg, without the other packages a feature would pull in.
- Install features, Chrome page: On-device model opens chrome://on-device-internals. Chrome models opens a separate page in Chrome, not a web tool. That page lists translation models and offline voice packs, filters by language, and colors installed packs green and missing packs red. Voice packs are checked in the Chrome that is showing the page. Installing a pack shows download progress.
- A borderless translucent performance window shows CPU, GPU, memory, network, and disk, plus temperature and power when available. Right-click turns each field on or off.
- File menu has Restart, which quits and starts the program again.
- Windows OCR can recognize Chinese and English together. Both languages run once. Chinese characters stay from the Chinese pass, and Latin text comes from the English pass.

#### Changed

- Character art defaults to 250 columns and preview font size 6. A slider sets the preview font size from 6 to 36. With Invert off, darker cells use denser characters. Line mode at threshold 0 keeps faint edges, including edges on a cell boundary.
- chrome.exe path, Detect path, and idle seconds are on Settings → General. Detect path fills chrome.exe from common install locations.
- Chrome speech and translation share one hidden Chrome process. Requests run one after another, so speech stays in the foreground tab. The window stays off the taskbar. Memory lists that process and can close it. It uses the HTTP proxy from Settings. Loopback, private networks, .cn, and China mirrors stay direct. A proxy change restarts the process.
- Chrome speech plays immediately. If it cannot speak, that language's offline voice pack is installed, with a progress window that can be cancelled, and the text is spoken once more. It does not switch to another voice. If it still cannot speak, the desktop shows an error and the HTTP API returns an error. Chrome translation downloads a missing language pack in that same window, shows the download and the percent when it is known, then translates.
- The performance window is a two-column slate panel. A pin keeps it in front. It samples on the window thread, so CPU, memory, network, and disk show up without administrator rights. CPU temperature comes from the thermal zone counter and does not block the other readings. Settings set the refresh interval from 0.2 to 5 seconds.
- The web tools page is built in JavaScript. tools.html only links the stylesheets and scripts. The scripts run after the page is created, so the tools home shows its cards and panels.
- The dictionary hotkey does not open the SQLite dictionary when there is nothing to search.

#### Fixed

- Edge online speech uses the current Sec-MS-GEC v2 token. The previous token was rejected with HTTP 403.
- Windows OCR in Chinese or English puts spaces back into English words where the picture has a word gap.

### 中文

#### 新增

- 系统工具只读列出进程、文件占用、环境变量、启动项、已安装程序、服务、补丁、商店应用和文件关联。服务只列出，不控制。
- 系统信息显示内存条和主板。
- 文件分析对照文件头与扩展名，并显示版本、图片、媒体和文本编码。
- 录音机可以同时录系统声音，设备列表只显示麦克风。
- 帮助 → 安装功能增加「单个组件安装」。可以单独安装或删除一个组件，例如 FFmpeg，不会带上该功能里的其它组件。
- 安装功能的 Chrome 页：「本机模型」用 Chrome 打开 chrome://on-device-internals。Chrome 模型是单独页面，在这里用 Chrome 打开，不再放在网页工具里。页面列出翻译模型和离线语音包，可按语言筛选，已安装为绿色，未安装为红色。语音包在正在显示该页的 Chrome 里查询。安装过程中显示下载进度。
- 性能监控是半透明无边框小窗，显示 CPU、GPU、内存、网络和磁盘，能读到时也显示温度和功率。右键可开关每一项。
- 文件菜单增加重启，退出后重新打开程序。
- Windows OCR 增加中英混合：中文和英文各识别一次，汉字留中文结果，英文留英文结果。

#### 变更

- 字符画默认列数为 250，预览字号为 6。预览字号可用滑块在 6～36 之间调整。不勾选反色时，越暗的地方字符越密。线条模式在阈值为 0 时保留很淡的边缘，格子交界上的线也会画出来。
- chrome.exe 路径、检测路径和空闲秒改到参数设置 → 常规。检测路径从常见安装位置填入 chrome.exe。
- Chrome 朗读和翻译共用一个隐藏进程。请求在这个窗口里排队，朗读页保持在前台。窗口不出现在任务栏。内存占用列出这个进程，可以手动关闭。它使用参数设置里的 HTTP 代理。回环、内网、.cn 和国内镜像仍然直连。代理改了会重新打开这个进程。
- Chrome 语音先直接朗读。发不出声才安装该语言的离线语音包，进度窗可以中止，装完再读一次。不再改用别的语音。仍然发不出声时，桌面弹出错误，HTTP 接口返回错误。Chrome 翻译缺少语言模型时，在这个已经打开的窗口里下载，显示正在下载，有百分比时一并显示，然后再翻译。
- 性能小窗改为双列石板样式，标题旁的图钉可固定到前面。采样在窗口线程进行，CPU、内存、网络和磁盘不用管理员也会出数。CPU 温度从温度区域计数器读取，查温度不再挡住其它读数。参数设置里可把更新间隔调成 0.2 到 5 秒。
- 网页工具页改由脚本生成，tools.html 只保留样式和脚本引用。脚本在页面生成之后再执行，工具主页会画出卡片和各个面板。
- 词典热键在没有搜索内容时不打开 SQLite 词典库。

#### 修复

- Edge 在线语音改用现在的 Sec-MS-GEC v2 令牌。旧令牌在合成时会被拒绝并返回 403。
- Windows OCR 在中文或英文下，按画面词距把粘在一起的英文补回空格。

## v1.0.21 (2026-10-14 ~ 15)

### English

#### Added

- Network tools can look up a city for an IP or domain, also as GET/POST /api/ipcity. An empty address uses this PC's public IP.
- Network tools can look up the IEEE vendor of a MAC address, also as GET/POST /api/mac.
- Network tools can look up a USB vendor and product from usb.ids, also as GET/POST /api/usb.
- System info has a Fonts tab showing each installed font name, a live preview, and its font file names.
- System info lists displays, cameras, Bluetooth, network and input adapters, physical disks, batteries, PCI/system devices, and devices with Windows problem codes.
- Network tools can find other computers and network devices on active local IPv4 networks, listing host name, IP, and MAC address.
- Network tools show the DNS resolution process with PTR lookups, and provide local ARP lookup and reverse lookup by MAC.
- HTTP request debugger adds a Test group with sample calls to this program's own API.
- Network tools are grouped in a submenu, with DNS record lookup, TCP port tests, local adapter and route details, Wake-on-LAN, and NetBIOS names for discovered computers.
- Tools menu and the tools page add a Lookup section, including a common USB ID list.
- Network tools can use Windows network discovery to list other computers with their names, comments, IP addresses, and MAC addresses.
- Network tools renames Find other computers to IP scan, and adds a Port scan tab for an IP address and a port range.
- Tools and the tray add Pixel art: choose a size, paint large cells, and preview the real pixel size.
- Tools menu adds Korean input. A hotkey takes over the keyboard and composes two-set Hangul at the caret, like the Windows Korean IME.
- Tools and the tray add a sound recorder. It records the microphone, can pause and continue, then play or save a WAV.
- Tools and the tray add a Markdown reader. A dropped .md file is shown as HTML in the Windows 10 IE11 WebBrowser.
- Tools → Camera previews a camera, saves a JPEG or PNG, and records an MP4 with Media Foundation. Scan reads a QR code or barcode and needs the optional OpenCV and ZXing install. The tools page camera takes a photo and scans the live picture.
- HTTP request debugger POST body can be XML or raw text with a MIME type you type.
- Character art can draw detected edges with - | / \ and +.
- The tools page has a Markdown reader. Drop a file to preview it with the same renderer, and POST /api/md returns the page.
- Screenshot defaults: save to D:/s/ when drive D exists, otherwise screenshots/ next to the program. The file name defaults to dd_##. The folder box and copied screenshot paths use /.
- Scientific calculator on the Tools menu, tray, and tools page: compact keypad, degrees or radians, memory, percent, and a borderless black translucent window.
- The web port forwards one RTSP stream over WebSocket /rtsp, and the tools page can play H.264.
- Tools page: browser on-device model and this-browser info cards.
- Desktop RTSP test server draws a bouncing picture, encodes H.264 with libx264 in ffmpeg64, and serves rtsp://127.0.0.1:port/live.
- Desktop note: a borderless translucent window for text or a to-do list. Closing it keeps the note and the window position.
- The tools-page camera plays a square-wave beep when a code is accepted.
- Tools page: Chinese segmentation with Intl.Segmenter, by word, sentence, or grapheme.
- Chrome TTS plays Google voices in a hidden local Chrome. Set chrome.exe under Speech. It closes after an idle timeout (default 60 seconds, 0 keeps it). Playback uses the current system default output device. Audio is not exported or returned as WAV.
- IP scan list can copy the host name, IP, or MAC from the right-click menu.
- RTSP forward and the tools-page player can include PCMU, PCMA, or AAC. The RTSP test server has a sound switch for an 8 kHz PCMU tone.
- Translate can use Chrome's built-in model. Pick Chrome on the Translate tab. It uses the chrome.exe path from Speech and hides the window.
- Settings, API, has an RTSP forward switch. Off refuses WebSocket /rtsp.
- Translate popup can choose local ONNX, Chrome, or a configured LLM, and still picks the ONNX model by language direction.
- The web dictionary is a separate page at /dict. The tools page, menu, and tray open it in a new window. It looks up Chinese, English, Japanese, and Korean the same way as the desktop dictionary.
- Install Features can list, install, and remove Chrome translation packs and offline voice packs. They stay in this app's private Chrome profile.
- The web tools page has a world clock under Calendar. It lists local time for common places and can show one more region.
- The tools-page chat room accepts Ctrl+V to paste an image or a file.
- Chat images open in the local Viewer.js files, with transitions turned off.
- The status bar shows when a Chrome child process started by ScreenKit is still running.

#### Changed

- 7z extract uses 7za.dll only. SharpSevenZip is no longer downloaded or required.
- The release folder keeps only the current screenkit package. Older screenkit_*.7z files are removed after packing.
- On startup, a newer screenkit_*.7z in the program folder asks whether to install it.
- Sound / mic shows active devices by default. Show all includes inactive devices and keeps active devices first.
- System info queries each tab only on first entry. A permission-denied device query can retry through the elevated device-scan CLI; failures remain visible with Refresh available.
- USB vendor lookup is now USB ID lookup and returns the vendor, product, model, and interfaces.
- Local network is a button in the network tools window, not a Lookup menu item.
- Network diagnostics, discovery, DNS records, port test, and Wake-on-LAN are buttons in the Network tools window. The Tools menu and tray open that window as one item.
- Port scan shows a progress bar and lists each open port with its type.
- IP scan uses a progress bar and a host list, and can open Port scan for the selected address. Port scan reads a service banner, and accepts a timeout and common ports.
- GET/POST /api/pinyin no longer takes mode. One call returns data.pinyin and data.py.
- Korean input uses its own tray icon for Hangul or English, with close and settings on the right-click menu.
- Shift alone switches Korean input between Hangul and English. Shift plus a letter still types a double jamo.
- Korean input no longer joins vowels into a diphthong, and a repeated key does not type a second vowel.
- The slim release package includes 7za.dll and License.7za.txt.
- The color picker can sample the screen. The camera can take an ImageCapture still and tune zoom, torch, and focus. Pen strokes keep coalesced points and a predicted tail, and a pen can use the system ink trail. Scanning reads frames with MediaStreamTrackProcessor when the browser has it.
- Desktop note: the lower-right chip opens the color dialog. The corner mark resizes the window. The color is kept.
- The tools-page scan beep is a 0.2 second 440 Hz square wave, the same pitch as the 8bit buzzer.
- Browser model can speak with speechSynthesis and dictate with webkitSpeechRecognition.
- Scientific calculator: Escape clears the expression and does not close the window.
- The tools page language script web/L.js is rebuilt only when a lang/*.toml file is newer.
- Chinese segmentation lists each word, sentence, or grapheme on its own line.
- Tools is one menu. Web entries are marked (web). Korean input stays on Tools.
- RTSP test server video bitrate now defaults to 200 kbps.

#### Fixed

- Video to GIF accepts a dropped video and shows a release hint while dragging.
- Pixel art fills every cell between mouse samples, so a fast stroke stays continuous.
- The Korean input tray icon uses a larger Hangul glyph, and its menu opens above the taskbar.
- The camera window no longer opens the device twice. A second open was waiting on the window thread, so the window looked stuck and the camera did not start.
- Opening the Markdown reader no longer fails because the page view cannot take a file drop that way.
- The Markdown reader keeps tabbed nested lists, including the bullet used in the source, at the same depth.
- The Markdown reader stays within the page. Wide tables scroll inside the preview.
- The camera preview shows the picture. Media Foundation RGB32 leaves the alpha byte at zero, so the preview drew it as fully transparent.
- Opened tools stay within the window. Extra content scrolls inside the tool.
- The camera preview picks a mode up to 1280x720 with a higher frame rate. The old default stayed on a large, slow format, about 2 frames per second.
- When Check for updates cannot reach GitHub, the dialog says to put a newer screenkit_*.7z in the program folder and click Check for updates again.
- Camera scan shows the same code again after 3 seconds, on the desktop window and the tools page.
- The RTSP test preview no longer flashes black. The picture is copied before the next frame clears the buffer.
- Scientific calculator keys + - * / are add, subtract, multiply, and divide, including Shift+8 and Shift+=.
- Web RTSP playback keeps every slice of one picture together, so the test square is no longer missing a corner or gaining an extra block.
- Scientific calculator accepts parentheses from Shift+9 and Shift+0.
- IP scan lists hosts as soon as they are found, and fills each name as it resolves.
- RTSP test server preview now shows the newest frame instead of lagging the player.
- RTSP test server paints the preview on screen before that frame is encoded and sent.
- Chrome translation install clicks the language-pack row that has the button. Voice packs load the speech component in current Chrome.

#### Removed

- The tools page no longer has the duplicate Calculator and Scientific cards. Scientific calculator remains.

### 中文

#### 新增

- 网络工具可查询 IP 或域名所在城市，并提供 GET/POST /api/ipcity。地址留空时查本机出口。
- 网络工具可查询 MAC 地址的 IEEE 厂家，并提供 GET/POST /api/mac。
- 网络工具可按 usb.ids 查询 USB 厂家和产品，并提供 GET/POST /api/usb。
- 系统信息新增字体列表页，显示已安装字体名称、实际字体预览和字体文件名。
- 系统信息新增显示器、摄像头、蓝牙、网络与输入设备、物理磁盘、电池、PCI/系统设备和异常设备列表。
- 网络工具可搜索活动本地 IPv4 网络中的其它计算机和网络设备，并列出主机名、IP 和 MAC 地址。
- 网络工具可显示 DNS 正向解析与 PTR 反查过程，并新增本地 ARP 解析和按 MAC 反查。
- HTTP 请求调试默认增加分组「测试」，里面是几条本程序自己的接口。
- 网络工具收进子菜单，并增加 DNS 记录、TCP 端口测试、本机网卡与路由、网络唤醒，以及搜索计算机时的 NetBIOS 名称。
- 工具菜单和网页工具增加「信息查询」，网页里可列表查看常见 USB ID。
- 网络工具可通过 Windows 网络发现列出其它电脑的名称、说明、IP 和 MAC。
- 网络工具把“搜索其它计算机”改为“IP扫描”，并增加“端口扫描”标签页，填写 IP 和端口范围后开始。
- 工具菜单和托盘增加像素画：选定尺寸后用大格作画，并按真实像素预览。
- 工具菜单增加韩语输入。快捷键接管键盘后，按 Windows 韩语输入法在光标处用两套式组字。
- 工具菜单和托盘增加录音机。从麦克风录音，暂停后可以继续，停止后播放或另存为 WAV。
- 工具菜单和托盘增加 MD 阅读器。拖入 .md 文件后，用 Windows 10 的 IE11 内核显示成网页。
- 工具 → 相机用系统摄像头预览、拍照，并用 Media Foundation 录成 MP4。扫码识别二维码和条码，需要另装的 OpenCV 与 ZXing。工具页的相机可以拍照，并扫描实时画面。
- HTTP 请求调试的 POST 正文可以是 XML，或自己填写 MIME 的原文。
- 字符画可以勾选线条，按检测出的边缘用 - | / \ 和 + 来画。
- 工具页增加 MD 阅读器。拖入文件后用和桌面相同的规则预览，POST /api/md 返回网页。
- 截图默认：有 D 盘时存到 D:/s/，否则仍是程序旁的 screenshots/。文件名默认 dd_##。保存位置和复制出来的截图路径用 /。
- 工具菜单、托盘和工具页增加科学计算器：紧凑按键、角度或弧度、内存和百分数，窗口无边框、黑色半透明。
- 网页端口用 WebSocket /rtsp 转发一路 RTSP，工具页可播放 H.264。
- 工具页新增「浏览器小模型」和「本机信息」。
- 桌面 RTSP 测试服务器画出弹跳画面，用 ffmpeg64 里的 libx264 编成 H.264，并在本机端口提供 rtsp://127.0.0.1:端口/live。
- 桌面便笺：无边框半透明窗口，可写文字或待办。关掉后再打开，内容和窗口位置仍在。
- 工具页相机扫出码时播放一声方波 8bit 提示音。
- 工具页新增「中文分词」，用浏览器分词按词、句或字素切开。
- 增加 Chrome 语音：用本机隐藏的 Chrome 朗读 Google 语音，可指定 chrome.exe。空闲默认 60 秒后关闭（可改，0 为不关）。桌面和网页工具播放都走系统当前的默认输出设备。不能导出，HTTP 也不返回 WAV。
- IP 扫描列表可右键复制主机名、IP 或 MAC
- RTSP 转发和工具页播放可以带上 PCMU、PCMA 或 AAC。RTSP 测试服务器增加声音开关，打开时发送 8 kHz PCMU 提示音。
- 翻译引擎增加 Chrome：用本机 Chrome 的翻译模型。在翻译页选择 Chrome，沿用语音合成里的 chrome.exe，窗口隐藏。
- 参数设置的 web/接口 增加 RTSP转发，关闭后不接收 /rtsp
- 翻译小窗可以选择本地 ONNX、Chrome 或已配置的 LLM，本地模型仍按语言方向自动选用。
- 网页词典是单独的 /dict 页面。工具页、菜单和托盘都用新窗口打开。查汉语、英语、日语、韩语，和桌面词典一样。
- 安装功能可以查询、安装和卸载 Chrome 翻译语言包和离线语音包。它们放在本程序专用的 Chrome 配置里。
- 网页工具的历法分类增加世界时钟，默认列出常用地区的本地时间，也可以再选一个地区。
- 网页聊天室可以用 Ctrl+V 粘贴图片或文件。
- 聊天图片用本机的 Viewer.js 点击放大，并关闭过渡动画。
- 状态栏在本程序启动的 Chrome 子进程还在运行时显示状态。

#### 变更

- 解压 7z 只使用 7za.dll，不再下载或依赖 SharpSevenZip。
- release 目录只保留当前版本的 screenkit 安装包。打包后会删掉更早的 screenkit_*.7z。
- 程序目录里有更新的 screenkit_版本.7z 时，启动会询问是否安装。
- 声音/麦克风默认只显示活动设备；勾选显示全部后会包含非活动设备，并保持活动设备在前。
- 系统信息改为首次进入各 Tab 时才查询；设备查询权限不足时可通过管理员设备扫描命令行重试，失败原因会保留并可点刷新重试。
- USB 厂家改为 USB ID 查询，返回厂家、产品、型号和接口。
- 本机网络改在网络工具窗口里作为按钮，不再放在信息查询菜单。
- 网络诊断、网络发现、DNS 记录、端口测试和网络唤醒改为网络工具窗口里的按钮。工具菜单和托盘只保留一项网络工具。
- 端口扫描显示进度条，并用列表列出开放端口和类型。
- IP 扫描改为进度条和主机列表，选中后可打开该地址的端口扫描。端口扫描会读取服务标识，并可设置超时和常用端口。
- GET/POST /api/pinyin 不再接收 mode，一次返回 data.pinyin 和 data.py。
- 韩语输入改为单独的托盘图标，显示韩文或英文，右键可关闭或打开参数设置。
- 韩语输入单独按 Shift 在韩文和英文之间切换。Shift 加字母仍输入双辅音或双元音。
- 韩语输入不再把元音并成双元音，按键连发也不会再打出一个元音。
- 精简发布包带上 7za.dll 和 License.7za.txt。
- 工具页取色可以从屏幕吸色。相机可以改用 ImageCapture 拍静帧，并调节变焦、手电和对焦。画笔补上快速移动丢掉的点，并画出预测的一段，触控笔走系统墨迹。扫码在浏览器提供时用 MediaStreamTrackProcessor 读帧。
- 桌面便笺：右下角色块打开选色，角标用来改大小，所选颜色会保留。
- 工具页扫码提示音改为 0.2 秒、440 Hz 方波，音高跟 8bit 蜂鸣一样。
- 浏览器小模型可以朗读，也可以用语音识别听写。
- 科学计算器按 Esc 清空算式，不再关闭窗口。
- 工具页语言脚本 web/L.js 只在某个 lang/*.toml 比它新时才重新合成。
- 中文分词按词、句或字素切开后，每行一段。
- 工具菜单合并成一个，网页工具标注 (web)，韩语输入直接放在工具下
- RTSP 测试服务器的视频码率默认改为 200 kbps。

#### 修复

- 视频转 GIF 可拖入打开视频，拖动经过窗口时提示松开。
- 像素画在两次鼠标采样之间补上经过的格子，快速移动时线条不断。
- 韩语输入托盘图标的韩文更大，右键菜单改到任务栏上方，不再被挡住。
- 相机窗口不再把同一摄像头打开两次。第二次打开会堵在窗口线程上，窗口看起来卡住，摄像头也起不来。
- 打开 MD 阅读器不再因为页面不能那样接收拖放而报错。
- MD 阅读器按源码的制表符层级显示嵌套列表，圆点项目和源码同级缩进。
- MD 阅读器不再把页面撑出横向滚动，宽表格在预览内部滚动。
- 相机预览能看到画面。Media Foundation 的 RGB32 把透明字节留成 0，预览就把它画成全透明。
- 打开工具后宽度不超过窗口，超出的内容在工具内部滚动。
- 相机预览改选不超过 1280×720、帧率更高的模式。原先停在又大又慢的格式上，大约每秒 2 帧。
- 检查更新连不上 GitHub 时，提示可以将新版本的 7z 放到程序目录下，再点击检查更新以更新。
- 桌面相机和工具页扫码时，同一个码隔 3 秒会再显示一次。
- RTSP 测试画面不再闪黑。先复制完一帧，再让下一帧清屏。
- 科学计算器按键 + - * / 对应加、减、乘、除，Shift+8 和 Shift+= 也一样。
- 网页播放 RTSP 时把同一帧的切片合成一张，测试画面的方块不再缺一块或多一块。
- 科学计算器可用 Shift+9 和 Shift+0 输入括号。
- IP 扫描发现主机后立即列出，每解析出一个主机名就更新一行
- RTSP 测试服务器的预览改为显示最新画面，不再比播放器慢一截。
- RTSP 测试服务器先把预览画到窗口上，再编码送出这一帧。
- 安装 Chrome 翻译包时点带按钮的那一行。语音包会加载当前 Chrome 的语音组件。

#### 移除

- 工具页去掉重复的计算器和科学计算，只保留科学计算器。

## v1.0.20 (2026-10-13 ~ 14)

### English

#### Added

- Settings can write screenshot paths with forward slashes (snap_path_slash).
- Function plots can draw equations such as x^2+y^2=1, and Examples opens a list of curves and surfaces.
- 3D function plots can switch among a shaded surface, a grid, points, and contour lines.
- Unicode, ASCII, and GBK charts can draw with an installed font. GET /api/fonts lists the system fonts. The choice is remembered.
- 2D function plots include a Cross tool, on by default, that lists where the crosshair meets the curve.
- Fast copy can delete the listed files permanently or move them to the Recycle Bin. It asks before starting, and does not need a destination.
- Fast copy accepts a drop on the list as sources and a drop on the destination as the target folder. A dropped file uses its parent folder.
- Character bitmaps turns each character into a dot matrix from a font and pixel size, then exports a C array, hex, or a dot picture.
- Character bitmaps can shift the dots with X and Y offsets.
- The file-transfer sign-in page and the file page link to the web tools home (/).
- ASCII character art has a threshold from 0 to 255. Brightness at or below it becomes a space. 0 keeps the full range.
- Ports in use lists TCP and UDP ports and the process that owns each one. It uses the system iphlpapi and needs no extra library.
- MIDI synthesizer plays through a system MIDI output and can open, edit, and save MIDI files.

#### Changed

- The System tab of System info groups the computer, processor, graphics, memory, disks, volumes, and network into blocks.
- RMB in words is a row on Number notation. The separate card is gone. A non-negative amount of at most 12 integer digits is rounded to fen; otherwise the row is blank.
- Number notation no longer shows a row when that reading has no value.
- Number notation accepts integers longer than 129 digits. The whole entry still stops at 10000 characters.
- Number notation accepts scientific input such as 1e9 and 1e-9, expanded from the digit string.
- Number notation no longer caps the decimal length. A Chinese scale row is omitted when a place is smaller than 涅槃寂静.
- The drawing board opens on the pen, black, at 2 px.
- GET /api/fonts caches the installed font list for 10 minutes. An empty list is not cached.
- The 2D Cross tool draws only a vertical line and lists where that line meets the curve.
- Clicking an 8-bit effect stops the others. Volume starts at 100%.
- Number notation speak plays through the system default output device.
- Translate speak on the tools page plays through the system default output device.
- Screen recording defaults to Windows Media Foundation H.264, which does not need FFmpeg.
- 3D function plots open on the grid. The dimension and the drawing style are each one segmented control.
- GIF screen recording uses system H.264 and encodes the GIF without FFmpeg.
- Number notation and translate speak request a WAV from /api/tts and play it in the page.
- Color character art opens through cmd. Auto uses quadrant blocks for double resolution, and a 1/8 to 7/8 block only when that vertical edge fits better.
- Character bitmaps put every character in one C array, one line each.
- Ports in use lists listening ports first.
- Web tools translate labels, hints, result lines, and Unicode block names that stayed in Chinese.

#### Fixed

- Windows OCR joins fragments on the same baseline into one line.
- Windows OCR no longer shrinks a short strip just because its long side exceeds the detection limit.
- Windows OCR enlarges an image whose short side is under 72 pixels so small gray text is not reduced to the last word.
- Chinese usernames stay as family name plus given name. Bogus was stripping the characters and leaving only a separator or digits.
- Windows OCR ignores the detection side limit. A short text line is enlarged about 2x with Lanczos so a small second line is kept, and punctuation sitting against a word is not split off with a space.
- Unicode, ASCII, and GBK charts show the character detail card again. Entering a tool hid every aside, including that card.
- Creating a symbolic link no longer counts as success when Windows rejects it.

#### Removed

- The tray menu no longer includes Connect Android USB accessory. The file-sync page and the screencast window still open it.

### 中文

#### 新增

- 参数设置可让复制截图路径时把反斜杠写成正斜杠（snap_path_slash）。
- 函数图像可以画 x^2+y^2=1 这类等式，点「示例」后从列表里选曲线或曲面。
- 三维函数图像可在曲面、网格、点阵、等高线之间切换。
- Unicode、ASCII、GBK 码表可以用本机已安装的字体显示。GET /api/fonts 列出系统字体。选过的字体会记住。
- 二维函数图像增加 Cross，默认开启，显示十字线与曲线的交点坐标。
- 快速复制可以永久删除列表里的文件，或删除到回收站。开始前会再确认，不用填目标。
- 快速复制可以把拖到列表上的项当作源，拖到目标上的文件夹当作目标。拖进去的是文件时，用它所在的文件夹。
- 字符取模按字体和字号把每个字符画成点阵，可导出 C 数组、十六进制或点阵文本。
- 字符取模可以用 X、Y 偏移挪动点阵。
- 文件传输的登录页和文件页可以回到工具主页（/）。
- 单色 ASCII 字符画增加 threshold（0～255）。亮度不超过该值的地方用空格，0 表示不截断。
- 网络端口占用列出正在使用的 TCP、UDP 端口和对应进程。调用系统 iphlpapi，不另装库。
- MIDI 合成器用系统 MIDI 输出弹琴，并能打开、编辑、保存 MIDI 文件。

#### 变更

- 系统信息的系统页按计算机、处理器、显卡、内存、硬盘、分区和网络分块显示。
- 人民币大写并入数字表记，不再单独成卡。非负且整数不超过 12 位时四舍五入到分；否则该行留空。
- 数字表记不再显示没有值的行。
- 数字表记不再把整数限制在 129 位。整段输入超过 10000 个字符时仍提示太长。
- 数字表记可输入 1e9、1e-9 这种科学计数，按数字字符串展开。
- 数字表记不再限制小数位数。小数位小于「涅槃寂静」时，小数、中数、大数三行不显示。
- 画板进入后使用黑色、2 像素的画笔。
- GET /api/fonts 把已安装字体列表缓存 10 分钟。空列表不缓存。
- 二维 Cross 只画竖线，并显示这条线与曲线的交点。
- 8bit 合成器点一个音效会停掉其它正在响的。音量默认 100%。
- 数字表记的发音改走系统默认播放设备。
- 工具页翻译的发音改走系统默认播放设备。
- 录屏默认使用 Windows Media Foundation 的 H.264，不需要 FFmpeg。
- 三维函数图像默认用网格。维度和画法各自是一组按钮。
- GIF 录屏改用系统 H.264，编码 GIF 不再需要 FFmpeg。
- 数字表记和翻译的发音改为向 /api/tts 要 WAV，再在网页里播放。
- 全色字符画改由 cmd 打开。自动以四分块为主，分辨率是整块的两倍；左右边界不在正中、而且更贴时才用 1/8～7/8。
- 字符取模的 C 代码把全部字符写进同一个数组，每个字一行。
- 网络端口占用先列出监听端口。
- 网页工具里原先仍显示中文的标签、提示、结果行和 Unicode 区段名会随界面语言切换。

#### 修复

- Windows OCR 把同一行上的碎片拼回一整行。
- Windows OCR 遇到扁图时不再按检测边长缩小，避免小字被丢掉。
- Windows OCR 会把短边不足 72 像素的图先放大，避免浅色小字只剩最后一个词。
- 中文用户名改为姓加名。原先 Bogus 会抹掉汉字，只剩分隔符或数字。
- Windows OCR 不再按检测边长缩小。字行太矮时用 Lanczos 放大约 2 倍，避免短行被丢掉；紧挨着的标点不再被拆成带空格的词。
- Unicode、ASCII、GBK 码表重新显示右侧字符详情。进入工具时隐藏了所有 aside，详情卡片也被藏起来。
- 创建符号链接时，Windows 拒绝之后不再算成功。

#### 移除

- 托盘菜单去掉「连接安卓USB配件」。文件同步页和投屏窗仍可打开。

## v1.0.19 (2026-10-10 ~ 12)

### English

#### Added

- The tools page adds Function graph under Calculate. Type `y = f(x)` or `z = f(x, y)` and it draws the curve or the surface. Drag pans and the wheel zooms. In 3D, drag rotates and Shift-drag pans. Trig functions use radians. Double-click resets the view.
- The tools page Image category has a board. It draws a rectangle, an ellipse, an arrow, a pen stroke, or text. Ctrl+V pastes a picture. Drag moves it, and the corners change its size.
- The tools page merges Stopwatch and Countdown into Timer. It can count up, count down from a number of seconds, or count down to a date and time you pick.
- **Tools → HTML layout** edits HTML on the left and previews it on the right as you type. Templates replace the current text: buttons, a card, a media card, inputs, choices, and a form. The tools page has the same card under Image. That preview is a sandboxed frame.
- **Tools → Fast copy** copies or moves files and folders. The same volume prefers a rename. Same-name files can be overwritten, skipped, or renamed. **Register menu** adds a current-user Explorer cascade (fast cut, copy, delete to the Recycle Bin, and copy paths). After a cut or copy, folder background gets fast paste and cancel. Shift copies paths relative to their common folder, and Shift+paste keeps the clip. No administrator. On Windows 11 the items are under Show more options. Selecting more than about 100 items can hide this static menu.
- The tools page **8-bit synthesizer** can arrange a loop on 4 channels: two squares, a triangle, and noise. Click a cell, then a key, to write a note. Noise cells cycle through six pitches. Tempo is in BPM, and each cell is a sixteenth note. The loop stays in the browser.
- The tools page has an **8-bit synthesizer** under Speech. Pick a voice (square, narrow pulse, thin pulse, triangle, saw, organ, bell, bass, flute, or noise drum), play the keyboard or the computer keys, and try the built-in effects: explosion, alarm, fire alarm, buzzer, phone, laser, jump, coin, power-up, hit, shot, pickup, fail, and heartbeat. Looping alarms stop when clicked again, or with Stop. Sound is generated in the browser.
- `GET /api/tts/devices` lists playback devices. `POST /api/tts/speak` synthesizes speech and plays it on one or more of those devices. `play=queue` waits its turn. `play=now` interrupts the current clip. The call returns when playback is queued, without WAV bytes.
- **Tools → Auto key** repeats a key or a mouse click to the foreground window. A hotkey (default F6, editable in the tool) starts and stops it only while that window is open. Closing the window stops listening and stops a run. The button in the window does the same. The interval is 20–60000 ms (default 200). A click lands at the cursor, or at the center of that window when the cursor is elsewhere, and the cursor then moves back. Nothing is sent while ScreenKit is in front.
- **Tools → Chinese to pinyin** turns characters into pinyin without tone marks, or into one capital initial per character, using `NPinyin.dll`. Common polyphonic words use their usual reading (`重庆` is `chong qing`, `银行` is `yin hang`). The library still writes ü as u. The tools page has the same card under Text. The DLL is about 75 KB and is not in the slim package. **Help → Install features → Tools** installs it. `GET/POST /api/pinyin` takes `text` and `mode=py|init`. Without the DLL the call returns 803. CLI: `ScreenKit --pinyin "重庆" [--init]`. The word list is `pinyin.toml` beside the program. The same word in `pinyin.user.toml` overrides it, and an empty reading drops that system word. Updates replace `pinyin.toml` and leave `pinyin.user.toml`.
- **Tools → Batch video convert** converts added videos to MP4, MKV, or WebM with FFmpeg. Defaults are x265, CRF 40, and a 480 shorter-side cap. Audio defaults to 64 kbps, 22050 Hz, and mono. MP4 and MKV can also use x264; WebM stays VP9. Audio can be turned off. Output goes to `output/` beside each source, a chosen folder, or replaces the source (permanent delete or Recycle Bin).
- **Tools → Video to GIF** previews one video, takes a start and end (at most 60 seconds), and saves a silent GIF. Frame rate, palette (32/64/128/256), and a shorter-side cap match GIF recording. Needs `ffmpeg64`.
- The tools-page chat room sends a heartbeat every 5 seconds and disconnects when that heartbeat fails. You can address a message to one person. Private messages are delivered only to the two of you. **Direct** uploads a file through the server to the selected person, and the download starts only after they click Accept.
- **Help → Install features** can install Bogus (`Bogus.dll`, about 2.3 MB, not in the slim package). The tools page **Generate test data** says whether it is installed. Without it, the page keeps the built-in generator. With it, pick a Bogus language first, then a category that language actually has data for. Dates, IP addresses, MAC addresses, and UUIDs stay available for every language. `GET /api/fake` returns the languages and categories. `POST` takes `locale`, `kind`, and `n`.
- The tools-page chat room can send an image or a file, up to 8 MB. The bytes are stored as `tmp/chat-` plus an id beside the program. Starting ScreenKit deletes those files and leaves the other `tmp` folders alone. Pictures show in the room. Other files are download links.
- The web tools page has a **Chat room** under Talk. A nickname and a gender (private, male, or female) are required before entering. The last nickname and gender are kept in the browser. People on the same ScreenKit talk over WebSocket `/chat`. Text is kept only in memory and is dropped when the program exits. A nickname already in the room is rejected.
- Edge and the other speech engines show a language name for every code in the filter, such as Afrikaans (af). Chinese, English, Japanese, Korean, Vietnamese, Cantonese, French, German, and Spanish stay at the top.
- Text convert on the tools page has Swap next to Copy. It exchanges the source and the result, and drops a conversion that has not come back yet.
- Right-click the screenshot area to paste (a picture, a copied image file, or an image path), open screenshot history, copy as an image, file, or path, save the picture, open with the default app, show in Explorer, annotate again, recognize text or a barcode, or clear the picture and its results.
- The tools page adds Common colors under Image. Colors are grouped by hue. A click copies the hex value.
- The tools page opens in the desktop UI language. A choice in the page language menu lasts until the page is opened again. The desktop Web tools menu lists each tool in that same language.
- The tools page language menu loads `/sk/L.js`. ScreenKit builds that script when it is requested. The script is one assignment, `L={...}`. Each value is an array of the installed UI languages, in the same order as the desktop language list, Chinese first. The browser may cache the script for 1 hour. Restarting ScreenKit changes the script address, so the next page load fetches it again. Choosing a language updates the open page at once and does not reload it.
- `ScreenKit --test-mem-load` starts one process per feature (baseline, OCR, translation, offline ASR, streaming ASR, TTS, dictionary, face) and prints commit size, private working set, and working set after that feature loads. The default device is CPU. ASR and TTS load the smallest installed model. `winocr` runs Windows OCR on a tiny image and does not load an ONNX pack.
- **Help → Memory** lists the open dictionary database and can unload it, which closes the database and drops the in-memory index. **Force GC** runs a full collection without unloading a row. The summary line also shows commit size. Private working set is the physical memory held by this process alone.
- `scripts\langtext.cmd` adds or changes one UI phrase in `ScreenKit/lang/*.toml` from a single command. `-l zh` writes one language, `-l all` writes every language, and `--each zh=… --each en=…` gives each language its own text. A new key goes to the end of `[text]` or after `--after KEY`. Line endings, the missing BOM, and untouched lines are kept. `--if-missing` only fills empty keys, `--dry-run` previews, and `--sync` also copies into the `lang/` folders already present under `ScreenKit/bin`. `get`, `missing`, `fill`, `check`, and `langs` round out the tool. `del KEY…` removes one or more keys (`rm` / `remove` / `delete` are the same command, and `set KEY --delete` works too); deleting in only some languages prints which ones still keep the key.
- `scripts\changelog.cmd` adds one bilingual entry to `CHANGELOG.md` under `## unreleased` (`added` / `changed` / `fixed` / `removed` / `docs` / `security`, or the Chinese category names). It takes a lock file and re-reads the file right before writing, so two writers cannot clobber each other; adding the same entry again is a no-op. `--file` points it at another changelog for testing.
- **Tools → HTTP request debugger** saves requests in groups. GET and POST parameters are one `key=value` line each (`#` starts a comment). A POST body is either those parameters or JSON. Sending uses the program HTTP proxy; loopback and private addresses stay direct. The list is `httpdbg.json` beside the program.
- Tools page, Text: Morse chart. Letters, digits, punctuation, and procedure signals. A click copies the dots and dashes.
- New tool: ASCII code chart (Text). It covers 0x00-0x7F in six groups and draws the classic 16-column grid with column and row headers; control characters show their usual abbreviations (NUL, HT, DEL ...). Click a cell to select and copy it (controls copy their escape), and the preview lists decimal, hex, octal, binary, the Unicode name and the category plus the C, JavaScript, HTML, URL and UTF-8 forms.
- Tools, Image convert: palette PNG with a choice of 2, 4, 8, 16, 32, 64, 128, or 256 colors. The file extension stays .png.
- Tools page: the Morse chart can play a whole typed text, not just one code. Letters are separated by three units and words by seven, the strip below shows the dots and dashes of what you type, and the current letter is highlighted while it plays. Playback stops at 20 seconds and says how much was left out.
- Tools page: the 8-bit synthesizer keyboard now shows three octaves. It draws one row when the width allows and one octave per row (three rows) when it does not; holding Ctrl plays an octave up and holding Shift an octave down, the keys move with it, and a note that was started keeps sounding until its own key is released.
- Tools, USB devices: list USB devices that are connected now. Select one to see its name, VID/PID, class, status, hardware IDs, driver, location, parent, and other parameters. The window only reads the system record and does not open the device.
- Tools, Character art: a picture becomes plain ASCII, or a console picture in full color using a full block, upper and lower halves, quadrants, and left blocks from 1/8 to 7/8. The wheel zooms, dragging pans, and resizing the console redraws.
- Tools, System info: one window with System, USB devices, and Serial ports. System lists the processor, graphics, memory, disks, volumes, and network. USB devices is now a tab in that window. Serial ports lists COM ports. It only reads the system record and does not open a device or a port.
- System info adds Printers and Sound / mic. Printers are local and connected. Sound / mic lists playback and recording devices, including the default role. Both are read-only and load in the background.
- The tools page adds Translate under Text. It follows the translate popup: pick languages, swap them, and speak either side. Ctrl+Enter translates. A ready translate LLM is used; otherwise an installed local model is used.
- The translate popup has Speak on the source and on the translation. It uses the dictionary voice for that language.

#### Changed

- The tools page board lasso lifts the ink inside the loop so you can drag it, then paints it back onto the picture. It does not stay a selectable object. A larger page rebuilds the canvas at the new size and does not scale the picture. A smaller page leaves the canvas as it is.
- The tools page board fills its card and does not scroll. The title and the hint are hidden. A rectangle, ellipse, arrow, or pen stroke is painted onto the picture. Only a pasted picture or text can be moved or resized.
- **Tools → Auto key** listens for its hotkey only while that window is open. Closing the window unregisters the hotkey and stops a run that is still sending.
- Starting no longer puts the SendFile listen line on the status bar.
- PDF workbench is on the Tools menu. It is no longer on the File menu.
- The tools page no longer has the HTML table card.
- Packing the slim release deletes runtime files from `bin/Release/ScreenKit/` before the archive is built: `screenshots/`, `log/`, `sendfile/`, `tmp/`, `config.toml`, and `*.log`. A Release build removes the same files when it refreshes that folder.
- The screenshot toolbar no longer has History, Paste, Copy image, or Save. Paste, screenshot history, and Save image are on the picture's right-click menu. Paste also accepts text that is an image path. The File menu still has Save image.
- The tools page opens speech synthesis on Windows speech, and OCR on Windows OCR, when that engine has a voice or an installed language. A later choice on the same page stays. Speech falls back to the first listed engine when Windows has no voice. OCR falls back to the main window's pack when Windows OCR has no language.
- Unit conversion does foreign currency in the program. It reads the Bank of China middle rate and keeps either a success or a failure for 10 minutes. A failed query uses the built-in 2026-10-10 table and is not tried again until that cache ends. The list has no jiao or fen. `GET/POST /api/fx` stays available when other HTTP modules are off.
- Tools-page radix conversion uses a Convert to dropdown. It starts on All. All prints the original number on the first line, then bases 2, 8, 10, and 16, one result per line.
- **Long screenshot** no longer shows the picture for OCR. After stitching it copies with the same mode as a normal screenshot (image, file, or path) and does not recognize. Overlap is matched on a Bitmap, so this feature does not need OpenCV.
- Windows OCR no longer uses OpenCV types. Image convert reads JPG, PNG, BMP, GIF, and TIFF without OpenCV. If `OpenCvSharpExtern.dll` is already present it also reads WebP and other formats, and does not ask to install. Chinese labels are drawn with GDI+ on a Bitmap. Face overlay still copies that label onto a Mat. ONNX OCR, face, and QR still use OpenCV.
- The password generator, on the desktop and the tools page, starts with lowercase letters and digits, length 6, and 10 passwords. Uppercase, symbols, skip ambiguous, one of each class, and a typed character set start off. The desktop still keeps the last settings after you change them. Opening the window or the tools-page card generates one batch immediately. Opening the desktop window again generates another batch.
- QR / barcode generation, in the window and on the web tools page, can show or hide the line of text under the code. It is on by default. In the window, and in a copied or saved image, the gap from the code to that line is half a line of text.
- The tools page adds All under Favorites. All is selected by default and lists favorites plus every category. Choosing a category lists only that category.
- The Unicode and GBK code charts list the total number of characters. Each group lists its count, its first code, and its last code. Unassigned Unicode code points and empty GBK slots are not counted. The group list fills the tool card. The card itself does not scroll. The total stays on top of the list and covers the groups that scroll under it.
- Settings → General feature switches hide the page, disable the feature (hotkeys, tray, and its HTTP routes), and release loaded models. LLM log only hides its page. The HTTP page only hides that entry. File sync and Screencast also stop those services.
- With every feature switch off, the main window no longer keeps the OCR tab on screen.
- Common colors shows the web English name beside each Chinese name, and every color is now a compact row: a small swatch on the left, two lines on the right. The search box also matches the English name (peacock, darkgoldenrod) and ignores spaces in it.
- Common colors now covers all 148 CSS named colors. The curated 83 grew to 155 rows (139 named colors plus 16 Chinese-specific colors) with a new White / Paper group for the near-white tones, and spellings that differ between US and UK share one row (Gray / Grey, Cyan / Aqua, Fuchsia / Magenta).
- Tools → Fast copy Explorer menu now has the full cascade: fast cut, copy, delete, copy as symbolic link, copy as hard link, total size, copy paths, and settings. A symbolic link points at the selected file or folder. A hard link must stay on the same volume; a folder becomes a directory tree of file hard links. Size shows the file count, folder count, and bytes, and does not follow reparse points. Settings opens the fast-copy window with those paths. An already registered menu is rewritten when the program starts. Paste also handles the new link clips.
- The HTML layout tool is now only the tools-page card HTML tool, under Image. The desktop window, Tools menu item, and tray item are gone. Templates also include a table, a nav bar, alerts, tags, a list, progress, an empty state, a dialog, pages, and steps.
- Common colors is now Color table and opens as a tree with two branches. Web colors holds the 148 CSS named colors in 11 hue groups; C# colors holds the 174 names of .NET (System.Drawing.KnownColor), 141 named colors plus 33 system colors whose values follow the Windows theme. The branches never mix, the All entry is gone, and Transparent is drawn as a checkerboard with an A=0 note.
- Web tool addresses and favorites accept only a current card id. An unknown id is ignored. config.toml reads only keys the program still writes; retired keys and old spellings are ignored, and a missing key keeps the field default.
- The 8-bit arranger stores an instrument on each of the four channels. Playback uses that instrument, and the choice is kept in the browser with the loop.
- The 8-bit arranger has six one-bar example loops: march, jump, underwater, dungeon, battle, and stage clear. Each sets the tempo and the four instruments.
- Random color on the tools page shows each generated color as a swatch in an HTML list, with HEX and RGB beside it.
- Screenshot annotate restores the last tool after the next region select, including no tool. The choice stays in memory and is written when ScreenKit exits.
- Tools page: clicking a Morse code now plays it through the 8-bit synthesizer (about 784 Hz, one unit per dot and three per dash at 90 ms, one unit between symbols), using the instrument and volume chosen in the 8-bit tool.
- Random colors list HEX, RGB, HSL, HSV, CMYK, and YUV. Click one value to copy it. Pick color also writes YUV (BT.601 full range, 0-255).
- CSS gradient on the tools page supports linear, radial, and conic gradients. Linear and conic take an angle. The preview and the background code update as you change the settings.
- Image convert places palette colors to minimize the total squared error. A palette color does not have to be a color from the picture.
- Random color on the tools page defaults to 10 colors and generates once when the card opens.
- The placeholder image redraws as soon as width, height, colors, or caption change. Ctrl+C or Copy result puts the PNG on the clipboard.
- Image convert preview zooms with the wheel toward the cursor, pans by dragging, and fits again on a double-click.
- CSS gradient on the tools page takes 2 to 8 colors from the browser color picker. They are spaced evenly in the order shown.
- The chat room can send a message to yourself, and your name is listed with the other people. The nickname field matches the gender list and the join button.
- Date math takes a date and time. The unit sits after the amount when adding or subtracting.
- Tools page: the 8-bit synthesizer's octave keys are now Shift (one octave up) and Alt (one octave down) while held, instead of Ctrl and Shift.
- The tools page reads a number in Chinese and English, and lists grouped digits, scientific notation, percent, formal Chinese, Roman numerals, and base 2, 8, and 16.
- Number notation also reads Chinese in the small, middle, and large scales, and names the digits after the decimal point from fen downward.
- Number notation adds an SI row, one prefix for each power of one thousand, from quecto and yocto through kilo, mega, and quetta.
- Number notation adds Korean native and Sino-Korean, Japanese, and German, French, Spanish, Portuguese, and Russian readings.
- Number notation names a positive integer as an English polygon, from triangle and hectogon through myriakismyriagon.
- Polygon names continue past 100 million sides by stacking myriads, so one billion is decakismyriakismyriagon (gigagon).
- Number notation leaves a cell blank when that reading or notation does not apply.
- Number notation also leaves the Korean native reading and the base-2, base-8, and base-16 cells blank when they do not apply.
- Number notation is listed under Text on the tools page.
- The web tools page has a top bar with the logo, back, favorite, and language. Opening a tool hides the left category list.
- Function graph: hold Ctrl to zoom the x-axis only, or Shift to zoom the y-axis only.
- Number notation rows can be selected by dragging. Each row has Copy and Speak instead of copying on click.
- The Morse chart text box starts as HELLO.
- Holding Ctrl while opening a web tool opens that tool in a new browser tab.
- Radix conversion adds Custom. Choosing it shows a target base on the right, from 2 to 36.

#### Fixed

- The tools page board text tool opens an editor where you click and leaves it open so you can type. Enter or a click elsewhere places the text.
- The web tools color picker updates the result while you drag or edit RGB. The Convert button is gone.
- **Tools → Auto key** reaches WezTerm. With a Chinese keyboard layout, simulated keys and clicks were counted but never arrived. They are now delivered as window messages.
- HTTP API failures return 200. The error stays in the JSON `code` and `data`, including an unknown path, a wrong method, and an internal error. OPTIONS stays 204. A successful WebSocket upgrade stays 101.
- Settings → Screenshot keeps `#` in the file name. The editable list was dropping that character as it was typed, and reading the config treated a `#` inside quotes as a comment.
- The tools page SAPI voice list starts `x86host` and includes 32-bit-only voices such as VW Lily. It used to list them only when that helper was already running.
- **Help → Install features** treats a `dict2.db` symbolic link beside the program as installed when the target is at least 100 MB. Windows reports the link itself as 0 bytes.
- The Common colors grid keeps its swatch size and scrolls. A short or narrow window used to squeeze every card until the color band collapsed to nothing, and the clipped rows could not be reached.
- The tools page QR caption stays readable. A short window used to squeeze that line until only a sliver of the letters showed.
- Image convert accepts a dropped JPEG, including a virtual file with no real path. The picture is decoded from memory, so the thumbnail and the encode do not lock the source file.
- Color character art keeps half blocks, quadrants, and 1/8 to 7/8 blocks when the two colors differ. Nearly identical colors still use a full block.
- Character art accepts a picture dropped on the text area, including a file, a file name, a virtual JPEG, or a bitmap.
- System info opens at once. The system, USB, and serial tabs read in the background and show Reading until each list arrives.
- Color character art defaults to quadrant blocks. A cell splits when its quadrants differ, and a flat cell is a space with the background color instead of a full block. Key 1 still draws full blocks.
- Morse chart playback now moves the highlight with each character. The timers were waiting on the audio clock, so only the first character lit up.
- Function graph fills the remaining height of the tool page.

#### Removed

- The tools page no longer has a Color conversion card under Image. Pick color already shows HEX, RGB, HSL, HSV, and CMYK.
- The tools page no longer has a Palette card under Image.
- The tools page no longer has the CSS button card.

### 中文

#### 新增

- 工具页「计算」增加函数图像。输入 `y=f(x)` 或 `z=f(x,y)` 画出曲线或曲面。拖动平移，滚轮缩放。三维时拖动转动视角，Shift 拖动平移。三角函数用弧度。双击恢复初始视图。
- 工具页「图像」增加画板。可以画矩形、椭圆、箭头、画笔和文字。Ctrl+V 粘贴图片后，拖动改位置，角点改大小。
- 工具页把秒表和倒计时合成「计时」。可以正向计时、按秒倒计时，或选一个截止日期时间看剩余时间。
- **工具 → HTML 界面设计**：左边写 HTML，右边随输入显示效果。模板会换掉当前内容，有按钮、卡片、图文卡片、输入框、选项和表单。工具页「图像」里有同样的卡片。网页上的预览放在隔离的框架里。
- **工具 → 快速复制**：复制或剪切文件和文件夹。同一分区优先改名。同名可以覆盖、跳过或重命名。**注册菜单**给当前用户加上资源管理器级联菜单（快速剪切、快速复制、快速删除到回收站、复制路径）。剪切或复制之后，文件夹空白处出现快速粘贴和取消。按着 Shift 复制相对路径，Shift 粘贴会留下剪贴内容。不用管理员。Windows 11 在「显示更多选项」里。一次选中大约超过 100 项时，这项静态菜单可能不出现。
- 工具页 **8bit合成器** 可以编排循环，四个通道：两路方波、三角波、杂音。点格子再点琴键写入。杂音格子在 6 档之间轮换。速度按每分钟拍数，一格是十六分音符。循环记在浏览器里。
- 工具页「语音」增加 **8bit合成器**。可选方波、窄脉冲、细脉冲、三角波、锯齿、风琴、铃、贝斯、笛子、噪声鼓，用琴键或电脑键盘演奏。音效有爆炸、警报、火警、蜂鸣、电话、激光、跳跃、金币、升级、受伤、射击、拾取、失败、心跳。循环的警报再点一次停止，或点「停止」。声音在浏览器里生成。
- `GET /api/tts/devices` 列出播放设备。`POST /api/tts/speak` 按与 `/api/tts` 相同的参数合成，并在一个或多个设备上直接播放。`play=queue` 排队，`play=now` 打断当前并立即播放。接口在排上队后就返回，不带回 WAV。
- **工具 → 自动按键**：向前台窗口连续发送按键或鼠标点击。快捷键（默认 F6，可在该窗口里改）只在该窗口打开时开始和停止。关掉窗口就不再监听，正在发送的也会停。窗口里的按钮同样可以。间隔 20～60000 毫秒（默认 200）。点击落在光标处；光标不在该窗口上时点客户区中心，再把光标移回。前台是本程序时不发送。
- **工具 → 中文转拼音**：用 `NPinyin.dll` 把汉字转成不带声调的拼音，或每个字一个大写首字母。常见多音词改成常用读法（「重庆」是 `chong qing`，「银行」是 `yin hang`）。ü 仍写成 u。工具页「文字」里有同样的卡片。库约 75 KB，不进精简包。在 **帮助 → 安装功能 → 工具集** 安装。`GET/POST /api/pinyin` 传 `text` 和 `mode=py|init`。没装库时返回 803。命令行：`ScreenKit --pinyin "重庆" [--init]`。词表是程序旁的 `pinyin.toml`。`pinyin.user.toml` 里的同名词条盖住它，读音留空就去掉系统里的这一条。升级会换掉 `pinyin.toml`，不会动 `pinyin.user.toml`。
- **工具 → 视频批量转换**：添加视频后用 FFmpeg 转到 MP4、MKV 或 WebM。默认 x265、CRF 40、较短边 480。声音默认 64 kbps、22050 Hz、单声道。MP4 / MKV 也可选 x264，WebM 仍用 VP9。声音可以关掉。输出到每个源文件旁的 `output/`、指定目录，或替换源文件（永久删除 / 回收站）。
- **工具 → 视频转 GIF**：预览一个视频，选取起止时间（最长 60 秒），按帧率、调色板（32/64/128/256）和较短边生成无声 GIF 并保存。参数与 GIF 录屏一致。需要 `ffmpeg64`。
- 工具页聊天室每 5 秒心跳一次，失败就断开。发送时可以指定一个人。私密消息只有双方能看到。直传把文件经服务器交给选中的人，对方点接收后才开始下载。
- **帮助 → 安装功能**可以安装 Bogus（`Bogus.dll`，约 2.3 MB，不进精简包）。工具页「生成测试数据」会提示是否已安装。未安装时仍用内置生成。安装后先选 Bogus 的语言，再选该语言自己有数据的分类。日期、IP、MAC、UUID 每种语言都有。`GET /api/fake` 返回语言和分类。`POST` 传 `locale`、`kind`、`n`。
- 工具页聊天室可以发图片和文件，单个不超过 8 MB。内容写到程序目录 `tmp/chat-` 加编号。启动时删掉这些文件，其它 `tmp` 子目录不动。图片直接显示，其它文件是下载链接。
- web 工具增加 **聊天室**，在「交流」分类。进入前填写昵称和性别（保密、男、女）。浏览器记住上次的昵称和性别。同一台 ScreenKit 上的人通过 WebSocket `/chat` 发文字。内容只留在内存里，程序退出后没有了。已经有人用的昵称不能再进。
- Edge 以及其它语音引擎的语言筛选都会显示名称，例如南非荷兰语 (af)。中文、英文、日文、韩文、越南语、粤语、法语、德语、西班牙语仍排在前面。
- 工具页「文本转换」在复制旁边增加「交换」。点一下把原文和结果对调，还没回来的转换会被丢掉。
- 截图区右键可以粘贴（图片、复制的图片文件或图片路径）、打开截图历史、复制为图片、文件或路径、保存图片，用系统打开，在资源管理器中查看，重新标注，做文字识别或条码识别，以及清空当前图片和识别结果。
- 工具页在图像分类增加常用颜色表。按红、橙、黄、绿、青、蓝、紫等分组。点一下复制十六进制。
- 工具页打开时使用桌面程序的界面语言。在页面里改语言只保持到下次打开。桌面「web工具」菜单按同一语言列出每个工具。
- 工具页侧栏的语言菜单加载 `/sk/L.js`。程序在请求时生成这个脚本，内容是一次赋值 `L={...}`。每个值是已安装界面语言组成的数组，顺序与桌面语言列表相同，中文在前。浏览器可缓存 1 小时。重新启动后脚本地址会变，下次打开页面会重新取。切换语言立即改当前页上的文字，不刷新页面。
- `ScreenKit --test-mem-load` 为每个功能单独冷启动（基线、识别、翻译、离线语音识别、流式语音识别、语音合成、词典、人脸），打印加载后的提交大小、专用工作集和工作集。默认 CPU。语音识别和语音合成加载已安装模型里最小的一份。`winocr` 用一张小图走 Windows 自带识别，不加载 ONNX 模型包。
- **帮助 → 内存占用**列出已打开的词典数据库，并可卸载：关掉库并丢掉内存里的索引。**强制回收**只做完整回收，不卸载某一行。摘要行同时显示提交大小。专用工作集是本进程此刻独占的物理内存。
- `scripts\langtext.cmd` 一条命令新增或修改 `ScreenKit/lang/*.toml` 里的一条界面文案。`-l zh` 只写一个语言，`-l all` 写进全部语言，`--each zh=… --each en=…` 逐语言给不同文本。新键追加到 `[text]` 末尾，或用 `--after <键>` 插到其后。保留每个文件原有的换行风格、无 BOM，未涉及的行不动。`--if-missing` 只补空，`--dry-run` 只预览，`--sync` 顺带覆盖 `ScreenKit/bin` 下已有的 `lang/` 副本。另有 `get`、`missing`、`fill`、`check`、`langs`。`del 键…` 删除一条或多条键（`rm` / `remove` / `delete` 是同一个命令，写成 `set 键 --delete` 也一样）；只删部分语言时会提示哪些语言还留着这个键。
- `scripts\changelog.cmd` 往 `CHANGELOG.md` 的 `## unreleased` 追加一条中英文条目（`added` / `changed` / `fixed` / `removed` / `docs` / `security`，也可写中文类别名）。写盘前先抢锁文件、再读一次比对，别人插队就整段重来，两个写入方不会互相覆盖；同一条重复添加直接跳过。`--file` 可指向别的 changelog，便于测试。
- **工具 → HTTP 请求调试**：保存请求，并按分组管理。GET / POST 参数一行一个，写成 `key=value`（`#` 开头是注释）。POST 正文可以是这些参数，也可以是 JSON。发送走程序的 HTTP 代理，本机和内网直连。列表写在程序旁的 `httpdbg.json`。
- 工具页「文字」增加摩斯码表。字母、数字、标点和信号。点一下复制点划。
- 新增工具：ASCII码表（文字）。0x00–0x7F 分六组，画成经典 16 列并带行列码表头；控制字符显示 NUL、HT、DEL 等惯用缩写。点格子选中并复制（控制字符复制它的转义写法），右边预览列出十进制、十六进制、八进制、二进制、Unicode 名称、类别，以及 C、JavaScript、HTML、URL、UTF-8 写法。
- 工具 → 图片格式转换可输出 PNG 调色板，颜色数可选 2、4、8、16、32、64、128、256，扩展名仍是 .png。
- 工具页：摩斯码表可以整段播放输入的文字，不只是单条码。字母之间空 3 点、词之间空 7 点，输入框下面显示转换出的点划，播放时逐字高亮。最长播 20 秒，超了会截断并说明只播了前几个字符。
- 工具页：8bit合成器的琴键显示 3 个八度，宽度够就一行放下、不够就一行一个八度分三行；按住 Ctrl 升 8 度、Shift 降 8 度（琴键跟着换一组；先松开修饰键不会把已经按下的音弄丢，松开那个音自己的键才停）。
- 工具 → USB 设备：列出当前已连接的 USB 设备。点一项查看名称、VID/PID、类、状态、硬件 ID、驱动、位置和父设备等参数。只读系统记录，不打开设备。
- 新增工具「字符画」。选图或拖入后生成纯 ASCII；全色在命令行窗口用整块、上下半块、四象限和从左加宽的 1/8～7/8 块显示，可平移、缩放，并随窗口大小重画。
- 工具 → 系统信息：一个窗口，分系统、USB 设备、串口三页。系统页列出处理器、显卡、内存、硬盘、分区和网络。原来的 USB 设备窗口改为其中一页。串口页列出 COM 口。只读系统记录，不打开设备，也不打开串口。
- 系统信息增加打印机和声音/麦克风。打印机含本机和已连接的。声音/麦克风列出播放和录制设备，并标出默认用途。都是只读，在后台读取。
- 工具页「文字」增加翻译。做法和翻译小窗一样：选语言、交换，两边都可以发音。Ctrl+Enter 翻译。翻译 LLM 就绪时用 LLM，否则用已安装的本地模型。
- 翻译小窗的原文和译文各有发音，用词典里该语言的引擎和发音人。

#### 变更

- 工具页画板的圈选抠起圈里的笔迹，拖动后画回图里，不再变成可选择的对象。页面变大时按新尺寸重建画布，内容不缩放。页面变小时画布保持原样。
- 工具页画板铺满卡片，不显示标题和说明，页面不滚动。矩形、椭圆、箭头和画笔直接画进图里。只有粘贴的图片和文字可以拖动、改大小。
- **工具 → 自动按键**只在该窗口打开时监听快捷键。关掉窗口就注销快捷键，正在发送的也会停。
- 启动时状态栏不再显示 SendFile 的监听端口。
- PDF 识别工作台改到「工具」菜单，不再放在「文件」菜单。
- 工具页去掉 HTML 表格。
- 打精简发布包前，先删掉 `bin/Release/ScreenKit/` 里运行时留下的 `screenshots/`、`log/`、`sendfile/`、`tmp/`、`config.toml` 和 `*.log`。Release 编译刷新这个目录时同样删掉这些文件。
- 截图工具栏去掉截图历史、粘贴、复制图和保存。粘贴、截图历史和保存图片改到图片右键菜单。粘贴也接受一段图片路径。文件菜单仍有保存图片。
- 工具页打开语音合成时默认 Windows 语音，打开文字识别时默认 Windows 系统 OCR（这一引擎已有发音人或已安装语言时）。同一次打开里改过的选项会保留。没有 Windows 发音人时语音合成用列表里的第一项。Windows OCR 没有已安装语言时，文字识别改跟主窗口的模型包。
- 单位换算的外币在程序里计算。向中国银行查中间价，成功或失败都缓存 10 分钟。查询失败时用内置的 2026-10-10 牌价，这 10 分钟内不再查。货币单位没有角和分。`GET/POST /api/fx` 在其它 HTTP 模块关闭时仍可用。
- 工具页的进制转换把「转换到」改成下拉，默认「全部」。全部的第一行是原来的进制数，后面 2、8、10、16 进制各一行。
- **长截图**不再把图片放到主界面供识别。拼接完成后按普通截图的复制方式写入剪贴板（图片、文件或路径），不识别。重叠行在 Bitmap 上比对，不再需要 OpenCV。
- Windows 系统 OCR 不再使用 OpenCV 类型。图片格式转换不装 OpenCV 也能读 JPG、PNG、BMP、GIF、TIFF。程序旁已经有 `OpenCvSharpExtern.dll` 时还会读 WebP 等更多格式，并且不会为此弹出安装。中文标签用 GDI+ 画在 Bitmap 上。人脸框仍把这块字贴回 Mat。ONNX 识别、人脸和二维码仍用 OpenCV。
- 桌面和工具页的密码生成默认只用小写字母和数字，长度 6，一次 10 条。大写、符号、去掉易混字符、每类至少一个、指定字符集默认都不开。桌面改过之后仍记住上次设置。打开窗口或工具页卡片后马上生成一次。桌面窗口还开着时再从菜单进入，会再生成一次。
- 二维码 / 条码生成（窗口和 web 工具）可以开关图下的一行原文，默认开。窗口里，以及复制或保存的图片上，码和这行字的间隔是半行字高。
- 工具页在收藏下增加「全部」，默认选中。全部列出收藏和每个分类。选中某个分类时只列出该分类。
- Unicode 码表和 GBK 码表列出总字数。每个分组列出字数、起始码和结束码。未分配的 Unicode 码位和 GBK 空位不计入。分组列表铺满工具卡片，卡片本身不再滚动。总字数这一行吸在列表顶上，滚上去的分组不会露在它上面。
- 参数设置 → 常规的功能开关关闭后隐藏页面，停用该功能（热键、托盘和对应 HTTP），并释放已加载的模型。LLM 日志只隐藏页面。HTTP 接口页只隐藏入口。文件同步和投屏同时停用对应服务。
- 功能开关全部关闭时，主窗口不再留下截图识别页。
- 常用颜色表在中文名旁边显示 web 英文名，色卡改成紧凑一行：左边小色块，右边两行文字（中文名 + 英文名 / 十六进制）。搜索框也能按英文名查（peacock、darkgoldenrod），英文里的空格可以省。
- 常用颜色表补全到全部 148 个 CSS 命名色：原来的 83 条扩到 155 条（139 个命名色 + 16 个中文专名色），近白色单开「白/纸色」组；英美拼写同值的合并成一条显示双拼（Gray / Grey、Cyan / Aqua、Fuchsia / Magenta）。
- 工具 → 快速复制的资源管理器菜单补齐为：快速剪切、快速复制、快速删除、复制为符号链接、复制为硬链接、统计大小、复制路径、参数设置。符号链接指向选中的文件或文件夹。硬链接必须在同一分区，文件夹会按目录为每个文件建硬链接。统计大小给出文件数、文件夹数和字节，不进入重解析点。参数设置打开快速复制窗口并带上这些路径。已经注册过的菜单在程序启动时会重写。粘贴也能处理这两种链接。
- HTML 界面设计改为工具页「图像」里的 HTML工具。桌面窗口、工具菜单和托盘项已去掉。模板增加表格、导航、提示、标签、列表、进度、空状态、对话框、分页和步骤。
- 「常用颜色表」改名「颜色表」，改成树形两支：web 颜色是 148 个 CSS 命名色（11 个色相分组），C# 颜色是 .NET 的 174 个名字（System.Drawing.KnownColor），141 个命名色 + 33 个系统色（色值随 Windows 主题）。两支互不混，不再有「全部」；Transparent 画成棋盘格并标注 A=0。
- 网页工具的地址和收藏只认当前卡片。没有这张卡片就忽略。config.toml 只读程序仍在写出的键。已停用的键和旧写法忽略，缺键用字段默认值。
- 8bit 编排的四个通道各自记住乐器。播放用这一路所选的乐器，选择和循环一起记在浏览器里。
- 8bit 编排增加六段一小节示例：进行曲、跳跃、水下、地牢、战斗、通关。每段会带上速度和四路乐器。
- 工具页随机颜色把每个生成的颜色显示成 HTML 列表里的色块，旁边是 HEX 和 RGB。
- 截图标注在下次框选后沿用上次的工具，也可以不选工具。选择留在内存里，退出程序时才写入配置。
- 工具页：摩斯码表点一下会用 8bit 合成器把这条码播出来（约 784Hz，一个单位 90 毫秒，点 1 单位、划 3 单位，符号之间空 1 单位），音色和音量用 8bit 工具里选的。
- 工具页随机颜色列出 HEX、RGB、HSL、HSV、CMYK 和 YUV，点一个色值就复制它。取色也写出 YUV（BT.601 全范围，0–255）。
- 工具页 CSS 渐变支持线性、径向和锥形。线性和锥形可设角度。预览和 background 代码随设置立即更新。
- 图片格式转换的调色板按总平方色差最小来分配颜色，调色板里的颜色不必取自原图。
- 工具页随机颜色默认 10 个，打开后立即生成一次。
- 占位图在改宽高、颜色或文字后马上出预览。Ctrl+C 或「复制结果」把 PNG 放进剪贴板。
- 图片格式转换的预览可以用滚轮对着光标缩放、拖动平移，双击重新适应窗口。
- 工具页 CSS 渐变可以用浏览器选色器选 2 到 8 种颜色，按列出的顺序均匀过渡。
- 聊天室可以给自己发消息，在线名单里也有自己。昵称框与性别、进入按钮样式一致。
- 日期计算改为输入日期时间。日期加减时，单位放在数量后面。
- 工具页：8bit合成器升降 8 度的键从 Ctrl/Shift 改成按住 Shift 升、Alt 降。
- 工具页输入数字后列出中文读法、英文读法，以及千分位、科学计数、百分数、中文大写、罗马数字和二、八、十六进制。
- 数字表记增加中文小数、中数、大数三种读法，小数点后用分、厘、毫、丝等单位。
- 数字表记增加 SI 词头，按千进位，从 quecto、yocto 到 kilo、mega、quetta。
- 数字表记增加韩文固有、韩文汉字、日语，以及德语、法语、西班牙语、葡萄牙语和俄语读法。
- 数字表记为正整数增加英文多边形名，从 triangle、hectogon 到 myriakismyriagon。
- 英文多边形名超过 1 亿边后按 myriad 继续叠乘，十亿边是 decakismyriakismyriagon（gigagon）。
- 数字表记在某一读法或表记不适用时，该格留空。
- 数字表记在韩文固有和二、八、十六进制不适用时也留空。
- 工具页里数字表记改到「文字」。
- web 工具页上方增加标题栏，放 logo、返回、收藏和语言。进入工具后隐藏左侧分类栏。
- 函数图像按住 Ctrl 只缩放 x 轴，按住 Shift 只缩放 y 轴。
- 数字表记每一行可以拖选文字，旁边有复制和发音，不再一点就复制。
- 摩斯码表的文字框打开时是 HELLO。
- 按住 Ctrl 再点 web 工具，用新的浏览器标签页打开。
- 进制转换的「转换到」增加「自定义」。选中后右边出现目标进制，范围 2 到 36。

#### 修复

- 工具页画板的文字工具在点击处打开输入框，这次点击不再把空的输入框立刻关掉。回车或点到别处写入文字。
- 工具页取色在拖动或修改 RGB 时直接更新结果，去掉了转换按钮。
- **工具 → 自动按键**可以作用到 WezTerm。中文键盘布局下，模拟的按键和点击会计数，但到不了终端。现在改成窗口消息送进去。
- HTTP 接口失败也返回 200。错误在 JSON 的 `code` 和 `data` 里，包括未知路径、方法不对和内部错误。OPTIONS 仍是 204。WebSocket 升级成功仍是 101。
- 参数设置 → 截图的文件名可以保留 `#`。可编辑下拉在输入时会丢掉这个字符，读配置时又把引号里的 `#` 当成注释。
- 工具页的 SAPI 发音人会启动 `x86host`，并列出只在 32 位里有的语音，例如 VW Lily。以前只有这个助手已经在跑时才会出现。
- **帮助 → 安装功能**把程序旁的 `dict2.db` 符号链接按目标文件判断。目标至少 100 MB 时词典显示已安装。Windows 把链接本身报成 0 字节。
- 常用颜色表不再被压扁：色卡保持大小，内容区自己滚动。以前窗口矮或窄时每张色卡会被等分压缩，色条被挤成一条线，超出的行也滚不到。
- 工具页二维码图下的原文保持完整。窗口较矮时，这行字以前会被挤到只剩一条缝。
- 图片格式转换可以拖入 JPEG，没有真实路径的虚拟文件也会收下。图片在内存里解码，缩略图和转码不再独占源文件。
- 全色字符画在两种颜色有差别时保留半块、四象限和 1/8～7/8 块。几乎相同的颜色仍用整块。
- 字符画可以把图片拖到文字区域。文件、文件名、虚拟 JPEG 和位图都可以。
- 系统信息窗口马上打开。系统、USB 和串口在后台读取，读完前状态显示正在读取。
- 全色字符画默认改用四象限。象限之间有色差就拆开，接近一色的格子用空格铺背景色，不再铺整块。按 1 仍是整块。
- 摩斯码表整段播放时，高亮跟着当前字符走。原先把音频时钟当成等待时间，所以只有第一个字会亮。
- 函数图像的画布占满工具页剩余高度。

#### 移除

- 工具页「图像」不再单列「颜色转换」。取色已经给出 HEX、RGB、HSL、HSV 和 CMYK。
- 工具页「图像」不再有「调色板」。
- 工具页去掉 CSS 按钮。

## v1.0.18 (2026-10-10)

### English

#### Added

- **Settings → Dictionary** has a database hold time in minutes. The dictionary database closes after that many minutes without a lookup, and the next lookup opens it again. The default is 1. 0 keeps it open. The same page sets how many hits to list (1–4000, default 1000). **Export** writes every hit for the current search, not only the rows on screen. Each entry is the plain text of its detail. txt joins them into one text. csv is a sheet. html and md are a table with columns Word and Detail. The detail column fills the remaining width.
- Tools-page date difference, date add/subtract, and month add/subtract are one tool, **Date arithmetic**. **Difference** shows how far apart two dates are and starts on **All**, which prints one line per unit: seconds, minutes, hours, days, and months, plus a year line once the gap passes a year. A unit of month or year follows the calendar and keeps the leftover days, as in `3 months 5 days (95 days in all)`. **Add or subtract** moves one date and answers on a single line, as in `2026-10-10 add: 7 days → 2026-10-17  Sat`. The count may be negative, and may be fractional from seconds through days. Month and year clamp to the last day, so January 31 plus one month is February 28. An offset in seconds, minutes, or hours, or a fractional day, also shows the resulting time. A favorite of the three old tools opens Date arithmetic.
- The left of the main-window status bar shows this program's commit size, CPU, and GPU. It updates every 2 seconds while the window is visible.
- The tools-page timestamp tool has a date-time picker. Choosing a time fills the text and converts. Typing a timestamp or a date converts immediately. Converting moves the picker to that local time.
- The Unicode code chart preview lists UTF-32, GBK, GB18030, Big5, Shift_JIS, and EUC-KR for the selected character. Those code-page bytes come from `GET/POST /api/enc`. A code point an encoding cannot store shows none. Latin-1 is shown for U+0000 through U+00FF.
- **GBK chart** sits next to **Unicode chart**. Lead bytes are pages on the left. Each page is a 16-column grid. A click copies the character. The preview shows the GBK bytes, the GB2312 section when there is one, the Unicode code point, and UTF-8. Empty slots stay blank.
- The tools page can turn a picture into an ICO. Paste or choose an image, preview it at the chosen pixel size with a border, pick one size (32 by default), then download. Scale with fit, stretch, or crop. Fit is the default: the picture stays in proportion and the empty area stays transparent. Stretch fills the square. Crop fills the square from the center and cuts off the rest. The preview and the downloaded icon use the same mode.
- The tools page splits QR / barcode into **QR generate** and **QR recognize**. Recognize accepts a pasted picture (Ctrl+V) or a chosen file, shows a preview, and reads the code. Generate prints one line of the source text under the image. A long line is cut with an ellipsis.
- The cast toolbar shows the current audio source. **Options** stays available while casting. It can turn audio on or off and pick speakers, the microphone, or both. The change applies to the live cast. The recording toolbar shows the current audio source and can change it, or turn audio off, while recording. The rest of the recording settings stay editable only before Start. GIF recording still has no audio.
- Settings → Capture sets the screenshot folder (`screenshot_dir`). The box shows `screenshots/` by default (next to the program) and also offers `D:/s/`. Another folder can be typed or browsed. History, cleanup, and copy-as-file use that folder. The file name (`screenshot_name`) is a pattern you can type or pick. The default is `shot_yyyyMMdd_HHmmss_fff`. Repeated `r` is random digits, as in `shot_yyyyMMdd_HHmmss_rrr`. Repeated `#` is a counter: `####` writes `0001.png`, and `yyyyMMdd_###` takes the next number among files with that same date prefix.
- `GET /api` lists each enabled route with its method, a short introduction, and parameter text taken from the HTTP tab. `lang` selects that language and defaults to the app's current UI language. The short `endpoints` list remains.
- The tools-page OCR screen accepts a pasted picture (Ctrl+V), shows a preview, and starts as soon as a picture is chosen or pasted. The result shows the same summary as the desktop: infer time, end-to-end time, confidence, engine, image size, line count, and the detection side limit. The Recognize button still runs it again.
- The tools-page regex tester can insert a common pattern: email, mobile number, ID card, URL, IPv4, date, time, integer, decimal, Chinese, color, postal code, QQ, license plate, or a blank line. An empty text box receives a one-line sample. It can also batch-test one line at a time. Each line is shown as `text [pass]` or `text [fail]`. Pass is green and fail is red. A line passes only when the whole line matches.
- The tools-page speech screen can filter voices by language and gender. **Save WAV** downloads the same synthesis as **Speak**.
- The tools-page random password tool generates 1 to 50 passwords at once. Length is 4 to 128. Each selected character set can be required at least once. The symbol set matches the desktop generator. Password generator, on the desktop and the tools page, can use a character set you type. Duplicates count once. The usual letter, digit, and symbol choices stay available when that option is off.

#### Fixed

- Image-to-ICO fit and stretch stay sharper when a large picture is reduced to the icon. The preview and the downloaded file average the source pixels. Crop is unchanged.
- Hovering a category in the main-window **Tools → Web tools** menu no longer closes the menu. Opening a category was rebuilding the list.
- The tools-page speech screen lists the models and voices the speech tab already loaded, as soon as it opens. It no longer waits on another model scan or on the Edge voice download. Every speaker is included, including models with more than 64. Names match the desktop list. Windows voices no longer show the registry id.
- The tools page search box hides cards that do not match. A stylesheet `display` rule had been covering the `hidden` attribute, so the list never changed. Back and the in-tool favorite button stay off the home list for the same reason.

#### Changed

- Opening the tools page drops favorites that no longer match a tool. A favorite of a merged tool is kept as that tool. The list is saved in this browser.
- The tools page **File transfer** link opens `/files` in a new window. The tools page stays open.
- Dictionary lookup reads `dict2.db` next to the program. Each word is stored once. Prefix, suffix, and infix search use the packed index. The old `lookup_key` search is gone. The file is not in the release archive. **Help → Install features** downloads `dict2.7z` from the fixed release `dict-db`, extracts `dict2.db`, and deletes an old `dict.db` beside the program (a symlink is removed, and the link target is left in place). Release builds leave a local `dict2.db` in place and no longer delete or recreate `dict.db`. The slim package still omits both.
- Test data picks one kind: Chinese name, Chinese screen name, English screen name, email, phone, address, company, username, ID number, MAC address, or UUID. Each line is one value. The separate MAC address and UUID cards are gone. The card is named **Generate test data**. An old favorite or `#u-mac` / `#u-uuid` opens that tool with the matching kind selected.
- Text-category tools update the result when the text or an option changes. Japanese yomi and the regex tester stay on their buttons. Unit conversion does the same.
- Length, area, volume, mass, temperature, time, storage, pressure, power, px/rem, and currency are one tool, Unit conversion. The number box accepts a unit, such as `1 m`, `3 jin`, or `5 kg`, and the kind and the original unit follow that text. The new-unit list gains **All** and starts on it, so one conversion lists every unit of that kind. The first line echoes what you entered. Picking one unit puts that single answer on the next line. Foreign currency uses the Bank of China middle rate of 2026-10-10, and the result says so. Text compare, JSON diff, and find-and-replace are removed. An old favorite of one of those tools opens Unit conversion.
- Each tools-page tool is its own script under `web/tools`. When ScreenKit starts, those scripts are joined into `tools.js` if one of them is newer than that file. The joined `ScreenKit/Web/tools.js` is not committed. The slim package ships that joined `web/tools.js` only.
- The tools page puts the open tool in the address hash, such as `#ocr`. Back returns to the previous tool or the home list. Categories stay on the left and that category's tools sit under a heading on the right. Scrolling highlights the matching category. There is no All Tools category. A favorite is listed again once under its own category.
- The tray **Web tools** menu and the main-window **Tools → Web tools** menu list every tool under its category. The first item is **Web tools home**. The next is **File transfer**, which opens `/files`.
- Password generator, on the desktop and the tools page, rewrites the character set when a class changes (lower, upper, digits, symbols, or skip ambiguous). A set you type is kept only while that option is on. The random-password card is under Text, directly after Text convert. The random-string card is removed.
- The tools-page ASCII chart is now **Unicode chart**. Groups are on the left. The chosen group is a grid of every character in that group, with each cell wide enough to show the whole glyph. A query takes a character, U+hex, or a decimal code. Clicking a cell copies it. Blocks larger than 1024 code points are split. Surrogates, private-use areas, and CJK Extension B onward are not listed. Cells the installed fonts cannot draw show a unified equivalent when the character is a compatibility ideograph, and the preview names that code point. An unassigned code point is marked empty. An assigned character with no local glyph and no drawable equivalent says a CJK font such as HanaMin may have it. The preview column shows the character large, with its code point, decimal value, block, plane, kind, and UTF-8, UTF-16, HTML, JavaScript, and URL forms.
- The tools-page color picker stays on the page. The color chip is a small square, and the saturation square, hue bar, and RGB fields sit under it.
- OCR on the tools page is under Image. These former cards are buttons on Text convert, and an old favorite of one of them opens Text convert: simplified/traditional, Japanese yomi (by word and by character), variable names, crontab, token estimate, User-Agent parse, MD5, SHA-256, JSON minify, JSON escape and unescape, HTML encode and decode, HTML / CSS / JS minify, XML format and minify, SQL format and minify, and HTML-to-JS. The tools-page batch-rename card is removed. Desktop batch rename, the desktop Japanese yomi window, and the desktop simplified/traditional window stay.
- Tray → Tools → Screencast opens the main window on the Screencast tab. If that tab was hidden, it is shown again.
- `scripts\build.cmd` asks the running ScreenKit to close its HTTP listener and exit, then calls `slx`. If that exit does not finish within 8 seconds, `slx` still kills the process and compiles. The global `slx` script is unchanged. The listen port stays the configured port.

### 中文

#### 新增

- **参数设置 → 词典** 增加数据库保持时间，单位是分钟。连续这么久没有查询就关闭词典数据库，下次查询再打开。默认 1。0 表示不关闭。同一页可设结果条数，范围 1～4000，默认 1000。**导出**写出这次搜索的全部词条，不受界面条数限制。每条是详情的纯文本。txt 接成一篇。csv 是表。html 和 md 是表格，列为单词、详情，详情列占满剩余宽度。
- 工具页的「日期差」「日期加减」「月份加减」合成 **日期计算**。**日期差** 看两个日期相差多少，单位默认就是第一项 **全部**：秒、分、小时、天、月逐行列出，超过一年再补一行年。按月和按年的差走日历、余数照留，如 `3 个月 5 天（共 95 天）`。**日期加减** 把一个日期往前或往后挪，结果占一行，如 `2026-10-10 增加：7 天 → 2026-10-17  周六`。数量可以是负数（往前），秒到天还可以带小数。月数和年数按月末夹住，1 月 31 日加 1 个月是 2 月 28 日。按秒、分、小时加减，或天数带小数时，结果会连时间一起显示。原来收藏了这三个工具之一时，会打开「日期计算」。
- 主窗口状态栏左边显示本程序的提交大小、CPU 和 GPU。窗口可见时每 2 秒更新。
- 工具页的时间戳增加日期时间选择。选好时间会写入文本并转换。输入时间戳或日期时马上转换。转换时，选择控件会跳到对应的本地时间。
- Unicode码表选中字符后，预览列出 UTF-32、GBK、GB18030、Big5、Shift_JIS、EUC-KR。这些代码页的字节由 `GET/POST /api/enc` 计算。放不下的码位显示「无」。U+0000 到 U+00FF 另外显示 Latin-1。
- 工具页增加 **GBK码表**，放在 **Unicode码表** 旁边。左边按首字节分页，每页 16 列。点一下复制字符。右边显示 GBK 字节、有区位时的区位码、Unicode 码位和 UTF-8。空位留空。
- 工具页可以把图片做成 ICO。可粘贴或选择图片，按所选像素大小带边框预览，尺寸默认 32，然后下载。缩放可选 fit、拉伸、裁剪。默认 fit，等比放进方框，空白透明。拉伸铺满方框。裁剪从中间铺满并切掉多余部分。预览和下载的图标用同一种。
- 工具页的「二维码 / 条码」分成 **二维码生成** 和 **二维码识别**。识别可 Ctrl+V 粘贴图片或选择文件，先显示预览再读码。生成在图下显示一行原文，过长时末尾省略。
- 投屏控制条显示当前声音来源。投屏中仍可点**选项**，开关声音或改成扬声器、麦克风、两者，马上作用到正在发送的投屏。录屏控制条同样显示当前声音来源，录制中可改来源或关闭声音。其它录制参数仍只在开始前修改。GIF 录屏仍然没有声音。
- **参数设置 → 截图** 的保存位置（`screenshot_dir`）默认填 `screenshots/`（程序目录下），下拉可选 `D:/s/`，也可以填写或浏览其它目录。历史、清理和复制为文件都用这个目录。文件名（`screenshot_name`）可输入，也可下拉。默认 `shot_yyyyMMdd_HHmmss_fff`。连续 `r` 是随机数字，如 `shot_yyyyMMdd_HHmmss_rrr`。连续 `#` 是序号：`####` 写成 `0001.png`；`yyyyMMdd_###` 只在相同日期前缀的文件里取下一个序号。
- `GET /api` 列出仍开启的接口，含方法、介绍和参数说明，文字与 HTTP 接口页相同。`lang` 选择语言，省略时用程序当前界面语言。原有 `endpoints` 简表仍在。
- 工具页的文字识别可以 Ctrl+V 粘贴图片并显示预览。选择或粘贴后立即开始识别。结果会显示和桌面一样的统计：推理耗时、端到端耗时、置信度、引擎、图片宽高、行数、检测边长。识别按钮仍可再跑一次。
- 工具页的正则测试可以选择常用表达式：邮箱、手机号、身份证、网址、IPv4、日期、时间、整数、小数、中文、颜色、邮编、QQ 号、车牌、空白行。文本为空时放入一行示例。也可以批量测试，一行一条。每行显示为 `文本 [通过]` 或 `文本 [未通过]`，通过为绿色，未通过为红色。整行匹配才算通过。
- 工具页的语音合成可以按语言和性别筛选发音人。**生成 WAV** 下载和朗读相同的合成结果。
- 工具页的随机密码可以一次生成 1 到 50 条。长度 4 到 128。可选每类至少一个。符号集与桌面密码生成器相同。桌面和工具页都可以改用手动输入的字符集。重复字符只算一次。不勾选时仍用原来的大小写、数字和符号。

#### 修复

- 图片转 ICO 的 fit 和拉伸把大图缩成图标时不再发糊。预览和下载的文件按源图像素平均缩小。裁剪不变。
- 主窗口 **工具 → web工具** 里，鼠标停在某个分类上时菜单不再自己关掉。之前打开分类会把整份列表重建一遍。
- 工具页的语音合成一打开就列出语音合成页已经加载的模型和发音人，不再另外扫描模型，也不再等待 Edge 发音人下载。发音人全部列出，包括超过 64 个的模型。名称与桌面列表相同。Windows 语音不再显示注册表 Id。
- 工具页搜索会藏起不匹配的卡片。样式里的 `display` 盖过了 `hidden`，所以列表看起来一直不变。返回和工具内的收藏按钮在首页同样不再露出来。

#### 变更

- 打开工具页时，收藏里已经没有的工具会去掉。合并进别的工具的收藏会改成那个工具。改过的列表写回这个浏览器。
- 工具页左下角的 **文件传输** 在新窗口打开 `/files`，工具页保持不动。
- 词典改为读取程序旁的 `dict2.db`。每个词只存一份，前缀、后缀、中缀走压缩索引。原来的 `lookup_key` 检索已去掉。这个文件不进发布包。**帮助 → 安装功能**从固定 Release `dict-db` 下载 `dict2.7z`，解出 `dict2.db`，并删除程序旁的旧 `dict.db`（符号链接只删链接，不删目标文件）。Release 编译不会动本机已有的 `dict2.db`，也不再删除或重建 `dict.db`。精简包仍不带这两个文件。
- 「生成测试数据」先选一种：中文姓名、中文网名、英文网名、邮箱、手机、地址、公司、用户名、身份证、MAC 地址、UUID。一行一条。单独的「MAC 地址」和「UUID」已去掉。原来收藏了这两项，或打开 `#u-mac`、`#u-uuid`，会打开这个工具并选中对应类型。
- 文字分类的工具在修改文字或选项时直接出结果。日文注音和正则仍要点按钮。单位换算同样是一改就出结果。
- 长度、面积、体积、重量、温度、时间、存储、压力、功率、像素和货币合成「单位换算」。数值框可以直接带单位，例如 `1米`、`3斤`、`5kg`，类型和原单位跟着这个单位走。新单位里多了 **全部**，并且默认就是它，一次列出该类型的全部单位。第 1 行回显原值；选具体单位时，下一行就是那一行结果。外币用中国银行 2026-10-10 中间价，结果里会注明。文本比对、JSON 差异和查找替换已去掉。原来收藏了其中一项时，会打开「单位换算」。
- 工具页的每个工具单独放在 `web/tools` 里。程序启动时，若其中有文件比 `tools.js` 新，就合并成 `tools.js`。合成后的 `ScreenKit/Web/tools.js` 不提交。精简发布包只带这一份 `web/tools.js`。
- 工具页进入某个工具时，地址带上 `#工具名`，例如 `#ocr`。后退回到上一个工具或首页。左边竖排全部分类，右边按分类列出工具。滚动时左边高亮对应分类。去掉「全部工具」。收藏里的工具在原分类再出现一次。
- 托盘 **web工具** 和主窗口 **工具 → web工具** 按分类列出全部工具。第一项是 **web工具主页**，下一项是 **文件传输**，打开 `/files`。
- 桌面和工具页的密码生成，改小写、大写、数字、符号或去掉易混字符时，字符集会改成对应的那一组。只有勾着「指定字符集」时才保留手输的内容。随机密码改到「文字」分类，排在「文本转换」下面。工具页去掉「随机字符串」。
- 工具页的 ASCII 表改为 **Unicode码表**。左边是分组，右边按宽度排成多列，格子够放下整个字。可输入字符、U+ 十六进制或十进制查找，点一下复制。超过 1024 个码位的区按段切开。代理项、私用区和汉字扩展 B 及以后未列入。本机字体画不出的兼容汉字改显示等价汉字，预览写明对应码位。没有分配的码位标成空位。已分配但本机画不出、又没有等价字的，预览会写明可能要另装花园明朝（HanaMin）等字体。预览栏用大字显示选中字符，并给出码位、十进制、分组、平面、类型，以及 UTF-8、UTF-16、HTML、JavaScript、URL。
- 工具页取色的选色器一直留在页面上。颜色块是小方块，下面是饱和度方块、色相条和 RGB。
- 工具页的文字识别改到「图像」。下面这些原来的单独卡片改成「文本转换」里的按钮，收藏了其中一项时会打开「文本转换」：简繁转换、日文注音（非逐字、逐字）、变量名、Crontab、Token 估算、User-Agent解析、MD5、SHA-256、JSON 压缩、JSON 转义和还原、HTML 编码和解码、HTML / CSS / JS 压缩、XML 格式化和压缩、SQL 格式化和压缩、HTML 转 JS。工具页去掉「批量改名」。桌面的批量改名、日文注音和简繁转换仍在。
- 托盘 → 工具 → 投屏打开主窗口并切到投屏页。该页被隐藏时会重新显示。
- `scripts\build.cmd` 先请求正在运行的 ScreenKit 关掉 HTTP 并退出，再调用 `slx`。8 秒内没退完时，仍由 `slx` 结束进程再编译。全局 `slx` 脚本不改。监听端口仍是设定的那个。


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
- HTTP `POST /api/ocr` can select **Windows OCR** with `ocr.engine=winocr` or `ocr.language=winocr`, without changing the main window. `ocr.language` is then a BCP-47 tag or a language name (`zh-Hans-CN`, `en-US`, `English`). `ocr.maxSideLen` still caps the long side. `ocr.engine=onnx` switches back to an ONNX pack when the main window is Windows OCR. `GET /api/ocr/get_options` lists `ocr.engine`.
- Settings → Recognition shows what the selected engine does. Windows OCR: no download, one installed language, upright boxes, no rotation, confidence, or GPU; the side-length cap still applies. Umi uses larger server models (Simplified Chinese, English, Traditional Chinese, Japanese, Korean, Russian). Rapid mobile Chinese is Simplified Chinese only. Rapid all-languages adds Latin and Arabic. ONNX packs keep detection, rotation, recognition, device choice, thresholds, and a confidence score.
- Screenshot OCR can use **Windows OCR** (`model_pack = winocr`). It does not load an ONNX model. It is faster, and the result cannot stay stable. The choice is saved as `win_ocr_langs`.
- Web manager **Send to PC** copies the text to the clipboard and shows a toast `Copied text: …`. Clearing the text still only clears the File sync box.
- With HTTP enabled in Settings, a LAN address already holding the port no longer makes the HTTP tab say the API is disabled. This PC still listens on `127.0.0.1` and the tab shows that address.
- UI languages are toml files in `lang/` next to the program, one file per language. Startup reads every file. The language menu and Settings list Chinese, English, Japanese, Korean, and other languages (German, Spanish, French, Portuguese, Russian).
- The bottom status bar says `No model loaded` when no model is loaded.
- The bottom status bar keeps the HTTP listener next to the memory summary: `HTTP 0.0.0.0:1224` or `HTTP 127.0.0.1:1224` while the API listener is up, `HTTP LAN:1224` when only the per-NIC file-transfer sockets are up, `HTTP not started` (with the error) when it should be listening but is not, and `HTTP off` when the API, file transfer, and cast are all disabled.
- Korean dictionary phrases have the same speak button as examples. It reads the Korean phrase. The Chinese gloss on the next line is not spoken.
- **Help → Install features → Windows speech**: when ScreenKit is not an administrator, install or remove uses `start` to open one administrator window for the selected packs. Allow it in User Account Control. Exit 0 and 3010 still count as success. The DISM command can still be copied.
- **Help → Install features → Windows speech**: installed packs and voices are read only when this tab is opened, or when Refresh is clicked. When ScreenKit is not an administrator, that read uses `start` the same way as install or remove: one administrator window. Denying User Account Control leaves pack status blank and still shows voices this process can see.

#### Fixed

- Dictionary entries show every sense, example, and idiom. English `run` keeps its phrasal verbs (about 60, including `come running`) instead of stopping after 16 senses.
- Web manager **Stay signed in** still works after this program restarts. The browser keeps the 30-day cookie and saved token, retries while the PC is starting, and an old tab token no longer hides that saved login.
- Dictionary selection popup: hovering a hit keeps dark text on a light blue row. The row no longer turns the text white on a faint highlight.
- Double-clicking a word in a dictionary entry opens the selection popup. Dragging a selection and right-click still open it.
- Sending a file whose name contains Chinese characters to the phone no longer fails with an illegal-characters-in-path error. The shared HTTP port decodes the download query as UTF-8.
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

- Japanese dictionary hits list every writing in the title, for example the kanji, hiragana, and katakana forms of one word. Out-dated or obsolete writings are omitted from the list and still shown in the entry. The row speaker reads a kana form without kanji (hiragana or katakana). A kanji headword is spoken as its kana reading. A headword that is already kana is read as written.
- Dictionary selection **Translate** opens the translate window, fills the source, and starts the translation.
- Speaking a selection in a dictionary entry uses that selection's language voice. Japanese glosses are only Chinese and English, so a Chinese gloss uses the Chinese voice and an English gloss uses the English voice. Kana in the headword, reading, or example keeps the Japanese voice. Chinese, English, and Korean entries choose only among those three voices.
- A dictionary search shows the hit count and the elapsed time in milliseconds on the status line.
- Dictionary list rows show every sense on the second line, separated by `; `. Text past one line is trimmed. The language tag is `zh`, `ja`, `ko`, or `en`. An English interface prefers the English gloss on that line.
- Removed unused code. ONNX session setup, the Sherpa device check, arrow drawing, and JSON field parsing each live in one place.
- **Diagnostics** is under **Help**. Ready checks show a green **OK**. Checks that are not ready show red text.
- **Settings → General** no longer shows the module switches. `mod_*` in `config.toml` is unchanged.
- Diagnostics shows an optional item that is off as **Off**: service mode, CUDA / DirectML not installed, ONNX not loaded yet, face models absent, OCR engine not loaded. A missing file or a failed check still shows red **Failed**.
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
- Android cast screen shows **Casting** again after leaving to the main screen and opening cast. The service was still casting; the new screen had reset to “ready”.
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

- LAN **PC file transfer** (HTTP `:17532`, UDP discovery `:17531`): first-connect pairing, `sendfile/` sandbox, main-window **File sync** tab. Companion Android app in `android/` (`com.whj.screenkit`). Other apps can share multiple files; in-app **Upload** is multi-select.
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

### 中文

#### 新增

- “语音识别”Tab 的“识别”子页增加实时字幕“离线 / 流式”单选项。
- 增加 7 个 `tab_*_visible` 配置开关及“参数设置 → 常规”复选框，可分别隐藏主界面 Tab；默认全部显示。

#### 变更

- 来回翻译仍最多执行 20 次；翻译结果与任一前序结果相同时提前停止。

#### 修复

- 语音合成：语速、音量不再与发音人下拉框重叠（设置区 Grid 漏了第 5 行）。

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

- LLM thinking intensity adds **medium** and the dropdown shows raw API values `none / low / medium / high / max` (config unchanged; `none` still maps to `off`; `medium` / `mid` no longer fall back to `low`).
- HTTP JSON responses write UTF-8 Chinese directly (no `\uXXXX` escapes).
- `GET /api/asr/models` `type` is an English keyword (`SenseVoice` / `Transducer` / …), not a localized UI label such as streaming Transducer.

#### Fixed

- English UI: OCR **Pack** / **Lang** combos used Chinese labels (Rapid all languages, Umi-OCR multilingual, Simplified Chinese, and so on). Display names now come from `ocr-display.json` (`name` / `nameEn`) and optional per-pack `pack.json`.
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

- The Chinese window and tray title is the localized screenshot-tool name. English remains ScreenKit.

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
- Translate tab can use a configured **LLM** instead of local Opus-MT (`translate_llm`). The LLM prompt (`translate_llm_prompt`, `{src}`/`{dst}`) is edited in **Settings → Translate**. LLM also supports round-trip translation without a reverse ONNX pair.

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
- Install Features: clearer status badges for not installed / partial / installed.

#### Changed

- Main window title shows version (e.g. `ScreenKit v1.0.1`).
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

- **In-app Install Features** window (feature components and voices tabs):
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

- **UI language** switch: Chinese / English via **Tools → Language** or Settings; persisted as `ui_lang` in `config.toml`.
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



