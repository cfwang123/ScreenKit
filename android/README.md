# ScreenKit 安卓客户端（`com.whj.screenkit`）

与 ScreenKit 电脑端配套的安卓 App。当前版本：**1.0.10**（随 ScreenKit 1.0.10 发布）。安装后桌面有两个入口：**传文件**、**投屏**（各自独立任务，可同时打开并在最近任务里切换）。

- [用法](#用法)
- [构建](#构建)
- [协议](#协议)
- [更新日志](CHANGELOG.md)

## 用法

1. 电脑打开 ScreenKit 主界面 **文件同步** Tab（参数设置 → 接口里启用「PC 文件传输」，默认开）。点 **安装到手机** 显示本机局域网下载地址和二维码（`http://<电脑IP>:17532/apk`，多网卡时默认用能连外网的地址），手机与电脑同一局域网，用浏览器扫码下载安装。PC 编译会把本目录最新 **release** APK 拷到程序旁 `apk/`（仅当源更新；不用 debug）。
2. 安装本 App 后启动：直接连接**上次成功的电脑**；失败再搜索/手选。
3. 首次连接：手机弹出「等待电脑确认」（可取消）；电脑弹窗始终置顶，点允许后手机等待窗消失。
4. 主界面显示**传输列表**（待传、进度、完成）。电脑发来的文件带类型图标，点开后用系统「选择打开方式」打开。「上传」可多选，写到电脑 `sendfile/` 根目录。「文本同步」在上传右侧：电脑发来的文本以只读框显示最新一条，可选中复制。
5. 标题栏三横菜单：**参数设置**（绑定接收文件夹）、**换电脑**。未绑定文件夹则无法接收电脑拖来的文件。
6. 系统分享文件到本 App（相册/文件管理可一次多选）：直接传到电脑 `sendfile/`，并出现在传输列表；先直连上次电脑，失败再选电脑，选完自动发完。
7. 桌面 **投屏**：打开即扫描同一 Wi‑Fi 上的电脑（手动 IP 会自动填入连上的地址）。状态显示类型 **wifi / usb / usb(adb)**。允许截屏后把画面推到 ScreenKit。画质三档 540p/720p/1080p，可关声音。电脑需保持投屏接收（默认开）。**USB 投屏**：配件或通知栏 **网络共享**，没有则回落 adb。**USB 投屏(adb)**：开 USB 调试。

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

- 传文件：UDP 17531 发现（仅选电脑时）；HTTP 17532 配对 + 文件 + 文本（见仓库 [HTTP接口文档.md](../HTTP接口文档.md)）
- 投屏：UDP 19518 发现（`app=screencast`）；TCP 19519 或 USB AOA 配件 bulk，帧 `SCST` + H.264 Annex-B + AAC ADTS
