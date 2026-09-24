# ScreenKit 安卓客户端（`com.whj.screenkit`）

与 ScreenKit 电脑端配套的安卓 App。当前版本：**1.0.10**（随 ScreenKit 1.0.10 发布）。安装后桌面有两个入口：**传文件**、**投屏**（各自独立任务，可同时打开并在最近任务里切换）。

- [用法](#用法)
- [构建](#构建)
- [协议](#协议)
- [更新日志](CHANGELOG.md)

## 用法

1. 电脑打开 ScreenKit 主界面 **文件同步** Tab（参数设置 → 接口里启用「PC 文件传输」，默认开）。点 **安装到手机** 显示本机局域网下载地址和二维码（`http://<电脑IP>:1224/apk`，与 HTTP API 同一端口；多网卡时默认用能连外网的地址），手机与电脑同一局域网，用浏览器扫码下载安装。也可用浏览器打开同一端口的 **网页管理**（电脑 `/`、手机 `/m`，需登录才能上传）。PC 编译会把本目录最新 **release** APK 拷到程序旁 `apk/`（仅当源更新；不用 debug）。
2. 安装本 App 后启动：单页完成选电脑与传文件。自动连接**上次成功的电脑**；失败则在同页搜索局域网或手填 IP 连接。
3. 首次连接：手机弹出「等待电脑确认」（可取消）；电脑弹窗始终置顶，点允许后手机等待窗消失。
4. 主界面自上而下：**连接状态**（已连接可点「更换」展开选电脑）、**上传到电脑**、**接收文件夹**（同页可绑定）、**文本同步**（发送 + 只读显示电脑最新一条）、**传输记录**（进度与完成；来自电脑的文件可点开用系统应用打开）。
5. 连接电脑后点 **网页文件管理**（或标题栏菜单同名项）用系统浏览器打开 `http://<电脑IP>:1224/m`。标题栏三横菜单：**投屏**（跳转投屏 App）、**网页文件管理**、**参数设置**、**检查更新**。投屏 App 标题栏菜单可 **传文件** 跳回。未绑定接收文件夹则无法接收电脑拖来的文件（参数设置里也可绑定）。
6. 系统分享文件到本 App（相册/文件管理可一次多选）：直接传到电脑 `sendfile/`，并出现在传输列表；先直连上次电脑，失败再选电脑，选完自动发完。
7. 桌面 **投屏**：打开即扫描同一 Wi‑Fi 上的电脑。电脑画面窗 Tab 显示类型 **网络 / usb / adb**。横屏会按当前画质重编码。停止投屏时向电脑发 `GET /api/cast/stop` 立刻关窗。**USB 投屏**：电脑必须已开 ScreenKit（开接收会自动配件助手，也可点 **USB配件**）；电脑未开时提示「电脑未打开 ScreenKit」，不会显示投屏中。配件口被 spacedesk 占用时电脑会换成 WinUSB，手机可因 `USB_ACCESSORY_ATTACHED` 自动开始；不用 adb reverse。约 90 秒无配件则改走 USB 网络共享。**USB 投屏(adb)**：需要 USB 调试。纯 USB 测试画面（不截屏）：`am start …/.CastActivity --ez scst_usb_pat true`。

## 构建

```text
node build.js run          # debug 编译、安装、启动（需一台 adb 真机）
node build.js install      # release 增量编译、安装、启动
node build.js build        # 默认 release APK
node build.js build --debug
node build.js release      # release，复制到 release/ 与 ScreenKit 输出 apk/，再 slx 编译电脑端
node build.js apk
node build.js devices
node build.js rebuild --debug
```

minSdk 24 / targetSdk 34。当前 debug/release 均用 debug 签名，便于覆盖安装。

## 协议

- 传文件：UDP 17531 发现（仅选电脑时）；HTTP 与 API 共用 1224，配对 + 文件 + 文本（见仓库 [HTTP接口文档.md](../HTTP接口文档.md)）
- 投屏：发现与传文件共用 UDP 17531；画面走 WebSocket `HTTP /cast`（默认 1224）；USB AOA 配件 bulk。帧 `SCST` + H.264 Annex-B + AAC ADTS
