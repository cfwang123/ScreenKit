# Changelog / 更新日志

All notable changes to ScreenKit are documented here. / 本文件记录 ScreenKit 的重要变更。

Format based on [Keep a Changelog](https://keepachangelog.com/). Versions are project milestones. / 格式基于 Keep a Changelog，版本号表示项目里程碑。

Each version has matching **English** and **中文** sections. GitHub Release notes follow the same bilingual order. / 每个版本同时有英文与中文章节；GitHub Release 说明同样先英后中。

## Versions / 版本索引

- [unreleased](#unreleased)
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

#### Added

- Standalone interactive redesign prototype for the phone web file manager (`ScreenKit/SendFile/web/m-prototype.html`): a compact full-width list separated only by rules; no title/search bars; bottom Upload, Camera, and New actions. Tap a folder to open it or a file to download it; long-press to multi-select and use the download, ZIP, copy-link, rename, or delete action bar. It does not change the production `/m` page.
- File sync **web manager** on the same HTTP port (`17532`): desktop `/` and phone `/m`. Login (`sendfile_web_pass`, auto-generated if empty) is required to upload, mkdir, rename, or delete. A correct `/f/…` URL downloads without login. File sync tab **Web manager** shows the LAN URL, QR, and password. CLI `--test-sendfile` covers login and public download.
- Desktop web file manager (`/`): each row shows only **Download** in the Actions column; copy link, zip, rename, and delete are on the row right-click menu (folders: open, zip, rename, delete). Clicking a row toggles its checkbox (double-click a folder to open).
- Web file manager cache: HTML is served with `no-store`; JS/CSS URLs in HTML get `?&lt;bootUnix&gt;` replaced on each ScreenKit run (first `SendFileServer` in the process).
- File-transfer HTTP bind is exclusive (no `ReuseAddress`). A dead leftover listener no longer accepts connections and hangs the browser; bad/TLS probes get `400` instead of an open hang.
- Web manager: `[hidden]` now wins over `#main{display:flex}`, so the login card and file table no longer stack. CJK font prefers YaHei.
- File transfer: if the configured HTTP port is a leftover listen (process gone), bind the next free port, save it, and show that error instead of “service off”.
- File-transfer HTTP and the web manager now share the HTTP API port (`1224` by default). There is no separate sendfile HTTP port. UDP discovery still uses `17531`.
- Web manager: **Stay signed in** (30-day cookie, remembered across restarts). Folders have **Open** and **Zip**; the toolbar can zip the current folder or the selection.
- Phone web manager (`/m`) is a white layout with light accents and SVG icons on every button.
- Password generator **Variants** tab: type a password or phrase, pick an LLM (`pwgen_llm`), get memorable variants (mostly other-language translations spelled in ASCII romanization, mix case; at most one English paraphrase; no extra symbols, digits, leetspeak, or word-reordering). Double-click copies a row. CLI `--test-pwgen` covers JSON parse.
- LAN/USB screencast: **Tools → Screencast** (tray Tools too). Receive a phone or another PC (H.264 + AAC), or cast this desktop out. Quality 540p/720p/1080p; optional audio. Discovery UDP 17531 (shared with file transfer), media WebSocket `/cast` on HTTP 1224; USB uses AOA accessory (no adb / USB debugging). Settings → HTTP tab can disable receive (`cast_recv_enabled`). CLI `--test-cast`.

#### Changed

- USB accessory: phone starts reading the accessory fd as soon as it is opened, so the PC hello reply is not missed (the viewer was a black window for ~8s then closed). AOA helper still writes the hello reply before the USB read thread.
- ADB/WiFi capture: encoder repeats a static frame and the VirtualDisplay is kicked a few times after start, so the viewer is not black until the phone is touched.
- Phone discovery binds the UDP socket to Wi‑Fi (USB accessory no longer steals the default network). Unicast to the last PC IP. Discovery UDP is always **17531** (a bumped saved port left the phone scanning 17531 while the PC listened on 17535). File-transfer UDP replies even when only screencast receive is on.
- USB screencast hello: write `hello` before reading the PC reply (accessory read+write on one fd can stall the write ~2s). AOA bridge no longer treats 500ms USB idle as EOF, so the hello reply can arrive.
- WiFi/ADB screencast: the phone sends `hello` and must get a `hello` back before it starts capture or shows 投屏中; the PC opens the viewer only after that reply. A socket without this round-trip is not a session.
- WiFi/ADB screencast no longer uses extra TCP 19519/19520+ or UDP 19518. Discovery shares UDP 17531 with file transfer; media is WebSocket `GET /cast` on the HTTP API port (default 1224). `adb reverse` maps the same HTTP port. `--test-cast-recv` checks a localhost `/cast` hello.
- Phone web manager (`/m`): compact full-width list with divider rows only; tap folder/file to open or download; long-press multi-select with download, ZIP, copy link, rename, and delete; bottom bar Upload / Camera / New; breadcrumb path and account sheet (no title or search bar).
- Public `/f/…` download: `.txt` and `.md` use `Content-Disposition: attachment` so mobile browsers save the file instead of opening it inline.
- On launch, ScreenKit ends leftover copies from another folder, orphan `--cast-aoa` helpers, and live processes listening on TCP 19519, then takes the single-instance lock. A second start from the **same** folder still activates the existing window.
- WiFi screencast: discovery replies with the **actual** listen port (not always 19519); firewall allows TCP 19519–19534. Phone scan uses that port (`ip:19520` when 19519 is ghosted).
- WiFi/ADB viewer: exclusive listen only (no `ReuseAddress`). If 19519 is a dead-pid ghost socket, bind 19520+ and advertise that port; `adb reverse` maps device 19519 to the real host port. `--test-cast-recv` checks localhost hello reaches this process.
- Screencast viewer comes to the front when it opens (not always-on-top). **Tab** overlay is off until Tab is pressed. USB adb reverse is kept even while the AOA helper is running. Stop hides the window before decoder teardown (no ~1s black frame).
- Screencast viewer is no longer always-on-top. Picture scale can be **Fit** (letterbox) or **Fill** (crop); right-click the viewer or Tools → Screencast (`cast_view_fill`).
- USB screencast: the receiver starts the AOA helper by itself (no extra **USB配件** click). The helper stays after a session instead of exiting, and is no longer killed every 8s.
- USB accessory test pattern: the phone encodes a moving 640×360 block (no MediaProjection) over AOA bulk only (`scst_usb_pat`). No adb reverse, no Wi‑Fi.
- USB viewer: hello used to trigger an immediate `ping` on the same AOA bulk pipe, which can stall WinUSB so no video arrives and the window closes after 15s. Ping waits until the first frame. Handshake timeout is 8s. USB accessory bridges over named pipes (`ScreenKit.CastUsb` / `ScreenKit.CastUsbDown`) so a stuck TCP 19519 no longer blocks the viewer. Up and down are separate pipes so the helper can write video while waiting for PC control. Recv also tries to free 19519 from leftover ScreenKit processes.
- Screencast viewer: LibUsb never runs in the main process (that killed ScreenKit when a phone was plugged in, so the viewer never appeared). USB accessory uses a helper that requests AOA **and** bridges bulk to `127.0.0.1:19519`. Probe TCP connects no longer kick an active session. Auto-close no longer sets `hidebyuser`, so the next hello can show the window again.
- USB adb screencast: if 19519 is held by a dead listener, bind `127.0.0.1` (wins adb reverse) and each LAN IP (wins Wi‑Fi). A still-open TCP is a real session, not a probe. Phone probe uses a 1.5s `127.0.0.1` ping.
- Phone screencast keeps the selected quality in landscape: the encoder is rebound to the new size (same VirtualDisplay) instead of cropping a portrait frame. If rebind fails, crop is still the fallback.
- Password variants mostly translate into other languages then ASCII-romanize; at most one English paraphrase. They no longer reorder words, add digits, or use leetspeak.
- Viewer stays topmost and is centered; hello no longer uses a blocking UI invoke (could prevent the window from appearing). ShowCast always calls `Show()`, and OnGone delays hide so a quick reconnect cannot swallow the window.
- USB screencast uses Android Open Accessory bulk (no IP, no adb). LibUsb is not touched at ScreenKit startup (that crashed the process while a phone was plugged in). Cast settings has **USB配件**: helper process `--cast-aoa` requests the switch; the main process only opens accessory 18D1:2D00/2D01. USB NIC scan only targets RNDIS/42/137.
- AOA bulk read retries on USB timeout (a zero read was treated as EOF and killed the session).
- Phone screencast scans on resume (ephemeral UDP, no bind on 19518). Viewer ShowCast is synchronous and centered.
- USB tethering beacons on UDP so the PC can find the phone’s USB IP.
- Landscape: do not recreate the encoder (that crashed on Redmi); send orient and crop. Viewer window is sized to the cropped frame, not phone pixels.
- **USB 投屏** is accessory or USB tethering only (no adb). Enable 网络共享 in the notification shade.
- Phone screencast failures show a short “连接失败” toast/status (no abstract/IOException dump).
- Viewer title is **投屏 · wifi/usb/usb(adb)**; the phone status shows the same type. The IP box is filled with the connected host.
- Viewer title stays **投屏 · phone**, and the window is shown on the first video/audio packet (not only after JSON hello). Hello is sent before the encoder starts.
- Screencast encoder uses the real aspect (not a square canvas) and recreates on rotate, so opening Bilibili in landscape does not kill the process.
- USB 投屏 falls back to adb reverse when there is no accessory or USB tethering.
- Phone screencast screen scans for PCs on open.
- USB screencast: **USB 投屏** still uses accessory or USB tethering (phone listens / finds the PC); **USB 投屏(adb)** restores `adb reverse` (needs USB debugging). The PC repeats `adb reverse` in the background.
- Screencast audio: a dedicated send thread plus an 80ms socket send timeout so AAC is not stuck behind video; capture matches more Android audio usages.
- Viewer window refits when the phone rotates (crop + window aspect).
- Screencast freeze: video writes drop on send timeout; IDR every 2s; `adb reverse` stays off the UI thread.
- Screencast audio: the phone drains queued AAC before each video frame and never drops the encoder on a full send queue; ADTS is MPEG-4 `0xF1`.
- Phone stop sends `bye` and closes the socket at once; a new TCP client kicks the old session; idle >15s drops it. The PC hides the viewer and ignores leftover frames.
- USB tethering: the phone listens on the USB NIC and the PC connects out (USB adapters are often Public, so inbound 19519 is blocked). Port rules TCP 19519 / UDP 19518 are still added when ScreenKit can elevate.
- USB screencast uses AOA accessory, or USB tethering (network share) if no accessory. No USB debugging.
- Wi‑Fi screencast: create VirtualDisplay on the main thread and send SPS/PPS first so the PC is not stuck on a black window.
- Wi‑Fi screencast no longer freezes on a still frame: the phone encoder no longer waits on TCP, and the PC reads packets independently of decode.
- PC no longer polls LibUsb on every USB device at startup (that could kill ScreenKit while a phone is plugged in).
- Wi‑Fi screencast no longer freezes after ~10s: discovery/`adb` and ping left the UI thread; display keeps only the latest frame.
- Screencast settings and view windows use a dedicated monitor/cast icon.
- Android: single launcher **传文件/投屏** (English **ScreenKit**); screencast only from the in-app menu (no separate home icon or task affinity).
- Android file transfer: **Web file manager** opens `http://<PC>:<port>/m` in the default browser (button when connected, also in the title-bar menu). Title-bar menus jump between file transfer and screencast.
- `scripts/publish-release.mjs` omits local `config.toml`, `cli_last.log`, and `log/` from the 7z (API keys / paths).

#### Fixed

- File sync: copy toast is a small topmost overlay at the **primary monitor work-area bottom center** (not anchored to the main window). `ScreenKit --test-ui-toast` smoke-test.

### 中文

#### 新增

- 新增独立可交互的手机网页文件管理重设计原型（`ScreenKit/SendFile/web/m-prototype.html`）：100% 宽度紧凑列表，项目间仅保留分隔线；去掉标题栏和搜索栏，底部仅放上传、拍照、新建；点击文件夹进入、点击文件下载，长按进入多选并显示下载、ZIP、复制链接、改名、删除操作栏；不改动正式 `/m` 页面。
- 文件同步 **网页管理**（与手机传输同一 HTTP 端口 `17532`）：电脑版 `/`、手机版 `/m`。上传/建目录/改名/删除需登录（`sendfile_web_pass`，空则启动时自动生成）；正确的 `/f/…` 即可下载。文件同步 Tab **网页管理** 显示局域网地址、二维码和密码。CLI `--test-sendfile` 覆盖登录与公开下载。
- 电脑网页管理（`/`）：列表「操作」列仅保留 **下载**；复制链接、打包、改名、删除改为行 **右键菜单**（文件夹为进入、打包等）。点击行切换勾选（文件夹双击进入）。
- 网页管理缓存：HTML 不缓存；`index.html` / `m.html` 内引用的 JS/CSS 带本进程启动时间戳查询参数，重启 ScreenKit 后浏览器拉取新静态资源。
- 文件传输 HTTP 独占绑定（不再 `ReuseAddress`）。残留的死监听不再把浏览器连上后一直转圈；非 HTTP / TLS 探测立刻 `400`。
- 网页管理：`[hidden]` 不再被 `#main{display:flex}` 盖掉，登录框和文件表不会叠在一起；中文字体优先微软雅黑。
- 文件传输：配置端口若是残留监听（进程已死），自动改绑后面的空闲端口并写入配置；网页窗显示真实原因，不再一律说「未启用」。
- 文件传输 HTTP 与网页管理改为和 HTTP API 共用端口（默认 `1224`），不再单独占口。UDP 发现仍是 `17531`。
- 网页管理：登录可勾选 **保持登录**（Cookie 30 天，进程重启仍有效）；文件夹可 **进入**、**打包 zip**；工具栏可打包当前目录或所选。
- 手机网页管理（`/m`）改为白底、淡色点缀，按钮均带 SVG 图标。
- 密码生成器 **变体** Tab：输入一句密码或短语，选 LLM（`pwgen_llm`），多把意思译成其它语言再用拼音 / 罗马字拼写（可大小写；英文同义最多一条；不加符号、不加数字、不做 o→0、不调换词序）。双击一行复制。CLI `--test-pwgen` 覆盖 JSON 解析。
- 局域网 / USB 投屏：**工具 → 投屏**（托盘「工具」同样入口）。接收手机或另一台电脑画面（H.264 + AAC），也可把本机投出。画质 540p/720p/1080p，可关声音。发现与传文件共用 UDP 17531，画面走 HTTP `/cast`（默认 1224）；USB 走 AOA 配件（不用 adb / USB 调试）。参数设置 → 接口可关接收（`cast_recv_enabled`）。CLI `--test-cast`。
- **检查更新**：安卓传文件 App 侧栏菜单、手机网页 `/m` 账户面板均可从 GitHub Releases 查最新 APK（规则与 PC `AppUpdater` 一致）；网页经 `GET /api/web/apk-update` 由电脑代理（无需登录）。CLI `--test-sendfile` 会请求该接口。

#### 变更

- USB 配件：手机打开配件 fd 就开始读，电脑 hello 回包不再被丢掉（画面窗曾黑屏约 8 秒后关掉）。AOA 助手仍先写 hello 回包再开 USB 读线程。
- ADB/WiFi 采集：编码器在静止画面时重复上一帧，VirtualDisplay 启动后主动踢几帧，电脑不再等到手机操作才出画。
- 手机发现 UDP 绑到 Wi‑Fi（插着 USB 配件时不再走默认网卡）；并向上次电脑 IP 单播。发现口固定 **17531**（配置曾改口到 17535，手机仍扫 17531）。仅开投屏接收时，传文件 UDP 也会应答发现。
- USB 投屏 hello：先写出再读电脑应答（配件同一 fd 上先阻塞读再写会把 hello 卡约 2 秒）；AOA 桥不再把 500ms 空闲当断开，hello 回包才能到达。
- WiFi/ADB 投屏：手机发 hello，必须收到电脑 hello 才开始采集并显示「投屏中」；电脑回 hello 后才弹窗。只连上 socket 不算会话。
- WiFi/ADB 投屏不再另占 TCP 19519/19520+ 或 UDP 19518。发现与传文件共用 UDP 17531，画面走 HTTP 口上的 WebSocket `GET /cast`（默认 1224）。`adb reverse` 转发同一 HTTP 口。`--test-cast-recv` 校验本机 `/cast` hello。
- 安卓 **传文件** 改为单页：顶部连接状态、同页搜索/手填选电脑、上传、接收文件夹、文本同步与传输记录；仅侧栏保留 **参数设置**、**检查更新**（已移除独立选电脑页与文本同步页）。
- 安卓传文件：连接后 **网页文件管理** 按钮/菜单用系统浏览器打开电脑 `/m`；传文件与投屏标题栏菜单可互相跳转。
- 安卓：去掉桌面 **投屏** 图标；仅保留 **传文件/投屏** 入口（英文 **ScreenKit**），投屏从传文件标题栏菜单进入，同一任务栈（不再 `taskAffinity` 分栈）。
- 安卓传文件：主界面按钮按文字宽度流式排列；**上传文件** / **拍照上传** / **网页文件管理** / **投屏**；参数设置可配拍照格式（JPG/PNG）、JPG 质量（默认 60%）、限制最长边（默认 2000px，可关）。
- 安卓：传文件与投屏共用 **我的电脑** 居中弹窗（标题栏、选择连接、设别名、删除）；成功连接或开始投屏后自动记入列表。
- 安卓传文件：手填 IP 默认端口改为 **1224**（原误用 17532，导致连不上、电脑不弹配对）；UDP 发现增加子网广播；先 `info` 再配对。
- 电脑文件同步：手机上传或收到手机文本后自动复制到剪贴板，**主屏**工作区底部居中 Toast（非托盘气泡）。
- 电脑文件同步：接收目录多选后点 **推送**，将所选文件/文件夹发给已连接手机。
- 安卓传文件：「来自电脑」文本增加 **复制** 按钮，点文本区域也会复制。

#### 修复

- 电脑文件同步：接收后 Toast 固定在主显示器工作区底部居中（与语音浮层同类定位）。`ScreenKit --test-ui-toast` 可自测。

- 手机网页管理（`/m`）：100% 宽度紧凑列表、行间仅分隔线；点击文件夹进入、点击文件下载；长按多选后操作栏支持下载、ZIP、复制链接、改名、删除；底部为上传、拍照、新建；保留面包屑与账户入口，去掉标题栏和搜索栏。
- 公开下载 `/f/…`：`.txt`、`.md` 使用 `Content-Disposition: attachment`，手机浏览器会下载而不是页内打开。
- 启动时结束其它目录的旧 ScreenKit、残留 `--cast-aoa` 助手、以及占用 TCP 19519 的进程，再抢单实例锁。从**同一目录**再开仍只唤起已有窗口。
- WiFi 投屏：发现应答带**实际**监听端口（不再一律 19519）；防火墙放行 TCP 19519–19534。手机扫描使用该端口（19519 被占时为 `ip:19520`）。
- WiFi/ADB 弹窗：TCP 只独占监听，禁止 `ReuseAddress`。`19519` 若被已退出进程占死，改听 `19520+` 并写入发现包；`adb reverse` 把手机 19519 转到电脑实际端口。`--test-cast-recv` 校验本机 hello 进本进程。
- 弹出投屏窗时提到最前（不一直置顶）。**Tab** 信息默认不显示，按 Tab 才开。USB 配件助手在跑时仍保持 adb reverse。停止时先关窗再拆解码器，避免黑屏约 1 秒。
- 投屏画面窗不再置顶。画面可选 **适应窗口**（fit，留边）或 **铺满窗口**（fill，裁切）；右键画面窗或「工具 → 投屏」里切换（`cast_view_fill`）。
- USB 投屏：电脑开接收后自动拉起配件助手，不必再点 **USB配件**。助手一场结束后继续等，不再每 8 秒杀掉正在跑的助手。
- USB 配件测试画面：手机自绘 640×360 动态块（不截屏）只走 AOA bulk（`scst_usb_pat`），不用 adb reverse、不用 WiFi。
- USB 非 adb 投屏：hello 后立刻在同一条 AOA bulk 上 `ping`，WinUSB 可能卡死，没有视频，15 秒关窗。改为收到第一帧再 ping。握手超时 8 秒。配件桥改走命名管道（`ScreenKit.CastUsb` 上行 / `ScreenKit.CastUsbDown` 下行），不再被卡住的 TCP 19519 挡住弹窗；同一条管道上读写并发会把视频堵死，窗口只握手不更新（黑屏 0 fps）。接收启动时仍会尽量清掉占用 19519 的残留 ScreenKit。
- WiFi 投屏已握手却看不见窗：画面窗强制置顶到鼠标所在屏（不再只居中主屏），并显示标题栏。手机连发两次 hello 不再拆掉解码器；视频按序排队，避免只剩 1fps 的黑窗。
- 投屏窗反复不弹出：主进程不再调用 LibUsb（插着手机点 USB配件会把 ScreenKit 打崩）。配件助手会请求 AOA **并**把 bulk 桥到 `127.0.0.1:19519`。探测 TCP（立刻断开）不再踢掉正在投屏的会话。自动关窗不再记成用户关闭，下次 hello 还能弹出。
- USB(adb) 投屏不弹窗：19519 被已死进程的幽灵监听占着，新进程独占绑定失败等于没在听。失败时改绑更具体的 `127.0.0.1`（adb）和本机每个网卡 IP（WiFi）。探测短连不算正式会话。
- 手机投屏横屏保持所选画质：旋转时换编码器尺寸（不销毁 VirtualDisplay），不再从竖屏画面里裁一小条。失败仍回落裁切。
- 密码变体以其它语言译音（拼音 / 罗马字）为主，英文同义最多一条；不再调换词序、加数字或做 o→0 一类替换。
- 画面窗保持置顶并居中；hello 不再同步 Invoke UI（可能卡住不弹窗）。ShowCast 必调 `Show()`；OnGone 延迟关窗，避免重连瞬间把窗口关掉。
- USB 投屏走 AOA 配件 bulk（不用 IP、不用 adb）。启动时不再碰 LibUsb（插着手机时会把主程序崩掉）。投屏设置里有 **USB配件**：独立进程 `--cast-aoa` 请求切换，主进程只打开配件 18D1:2D00/2D01。USB 网卡扫描只认 RNDIS/42/137。
- AOA bulk 读在 USB 超时时重试（读到 0 会被当成 EOF 直接断会话）。
- 打开投屏界面（onResume）自动扫描；发现不再死绑 UDP 19518。
- USB 网络共享时手机发 UDP，电脑用该地址连入。
- 画面窗同步打开并居中。
- 横屏：不再重建编码器（红米上会闪退），只发方向并裁切；窗口按裁切后的画面适配，不用手机分辨率当窗口大小。
- **USB 投屏**只走配件或 USB 网络共享，不用 adb。通知栏把 USB 设为「网络共享」。
- 投屏连接失败只提示「连接失败」，不再把 abstract/IOException 打在状态栏。
- 画面窗标题为 **投屏 · wifi/usb/usb(adb)**；手机状态同样显示类型。手动 IP 自动填入连上的电脑地址。
- 画面窗标题为 **投屏 · 手机型号**；收到第一包视频/音频就打开（不等 JSON hello）。hello 在编码器启动前先发。
- 投屏按真实宽高编码（不再用大方块），旋转时重建采集，避免进 B 站横屏把进程打崩。
- USB 投屏在没有配件/网络共享时回落到 adb reverse。
- 手机打开投屏界面即扫描电脑。
- USB 投屏：原按钮走配件或 USB 网络共享；新增 **USB 投屏(adb)**，恢复 `adb reverse`（需 USB 调试）。电脑后台重复做 reverse。
- 投屏声音：音频独立发送线程，套接字写超时 80ms，避免被视频堵住；系统内录音匹配更多 usage。
- 横屏后画面窗按手机比例重新适配。
- 投屏卡死：视频写超时丢帧；每 2 秒要关键帧；`adb reverse` 不在 UI 线程。
- 投屏声音：控制包（含 AAC）排空后再发视频，队列满时不再停掉音频编码；ADTS 用 MPEG-4 `0xF1`。
- 手机点停止会发 `bye` 并立刻关套接字；新 TCP 会顶掉旧会话，超过 15 秒无包也断开。电脑关掉画面窗并丢掉残留帧。
- USB 网络共享：手机在 USB 网卡上监听，电脑主动连出（USB 网卡常是公用网络，进站 19519 会被防火墙拦住）。若有权限仍会加 TCP 19519 / UDP 19518 规则。
- USB 投屏：AOA 配件，或通知栏「USB 网络共享」；不用 USB 调试。
- WiFi 投屏黑屏：主线程创建 VirtualDisplay，并先发 SPS/PPS。
- WiFi 投屏停在某一帧：手机编码不再等 TCP 写完；电脑收包与解码分开。
- 电脑启动时不再用 LibUsb 枚举所有 USB 设备（插着手机时可能把 ScreenKit 打崩）。
- WiFi 投屏十几秒后画面卡死：发现/`adb` 与 ping 移出 UI 线程，显示只保留最新一帧。
- 投屏设置窗与画面窗使用独立显示器/投屏图标。
- 安卓「传文件」与「投屏」分属独立任务栈，可同时打开。
- `scripts/publish-release.mjs` 打包时排除本机 `config.toml`、`cli_last.log` 和 `log/`（避免把密钥/路径打进发布包）。

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
