# PC文件传输（`com.whj.screenkit`）

与 ScreenKit 电脑端局域网文件/文本同步的安卓客户端。

## 用法

1. 电脑打开 ScreenKit 主界面 **文件同步** Tab（参数设置 → 接口里启用「PC 文件传输」，默认开）。
2. 安装本 App 后启动：直接连接**上次成功的电脑**；失败再搜索/手选。
3. 首次连接时电脑会弹窗确认配对。
4. 主界面显示**传输列表**（待传、进度、完成）。电脑发来的文件带类型图标，点开后用系统「选择打开方式」打开。「上传」可多选，写到电脑 `sendfile/` 根目录。
5. 标题栏三横菜单：**参数设置**（绑定接收文件夹）、**换电脑**、**文本同步**（电脑发来的文本只显示最新一条，点复制取全文）。未绑定文件夹则无法接收电脑拖来的文件。
6. 系统分享文件到本 App（相册/文件管理可一次多选）：直接传到电脑 `sendfile/`，并出现在传输列表；先直连上次电脑，失败再选电脑，选完自动发完。

## 构建

```text
node build.js run          # debug 编译、安装、启动（需一台 adb 真机）
node build.js install      # release 增量编译、安装、启动
node build.js build        # 默认 release APK
node build.js build --debug
node build.js release      # release 并复制到 release/screenkit{version}.apk
node build.js apk
node build.js devices
node build.js rebuild --debug
```

minSdk 24 / targetSdk 34。当前 debug/release 均用 debug 签名，便于覆盖安装。

## 协议

- UDP 17531 发现（仅选电脑时）
- HTTP 17532：配对 + 文件 + 文本（见仓库 [HTTP接口文档.md](../HTTP接口文档.md)）
