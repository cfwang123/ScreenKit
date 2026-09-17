# PC文件传输（`com.whj.screenkit`）

与 ScreenKit 电脑端局域网文件/文本同步的安卓客户端。

## 用法

1. 电脑打开 ScreenKit，在 **参数设置 → 接口** 启用「PC 文件传输」（默认开）。
2. 安装本 App 后启动：直接连接**上次成功的电脑**；失败再搜索/手选。
3. 首次连接时电脑会弹窗确认配对。
4. 浏览电脑程序目录下 `sendfile/`：进入目录、点文件下载、长按删除、上传。
5. **绑定文件夹** 后，「同步」把电脑 `sendfile/` **单向**拉到该目录（不删除手机多余文件，不同步回电脑）。
6. **文本同步** 打开独立页面：输入发送到电脑列表；电脑发来的文本单行显示，点「复制」取全文。
7. 系统分享文本/文件到本 App：先直连上次电脑再发送；失败再选电脑，选完自动发完。

## 构建

```text
node build.js run       # debug 编译、安装、启动（需一台 adb 设备）
node build.js build     # release APK
node build.js install   # release 安装
```

minSdk 24 / targetSdk 34。debug 签名，便于覆盖安装。

## 协议

- UDP 17531 发现（仅选电脑时）
- HTTP 17532：配对 + 文件 + 文本（见仓库 [HTTP接口文档.md](../HTTP接口文档.md)）
