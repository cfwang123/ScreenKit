# ScreenKit

Windows 桌面工具（程序 `ScreenKit.exe`，中文界面标题「屏幕截图工具」）：截图识别、标注、条码/二维码、长截图、录屏/GIF、PDF 工作台、语音识别/合成、LLM 对话、翻译、人脸、本机 HTTP API，以及与安卓配套的局域网文件传输。

**当前版本：1.0.9** · [GitHub Releases](https://github.com/cfwang123/ScreenKit/releases/latest)

[English](README.md) · [中文](README.zh.md)

## 目录

1. [下载](#下载)
2. [截图](#截图)
3. [功能一览](#功能一览)
4. [运行环境](#运行环境)
5. [使用说明](#使用说明)
6. [安装功能](#安装功能)
7. [配置](#配置)
8. [HTTP API](#http-api简述)
9. [CLI](#cli简述)
10. [x86host](#x86host仅-32-位-sapi)
11. [从源码编译](#从源码编译)
12. [许可证](#许可证)

## 下载

| 文件 | 说明 |
|------|------|
| [`screenkit_1.0.9.7z`](https://github.com/cfwang123/ScreenKit/releases/latest) | Windows x64 精简包（exe + 托管依赖）。模型与运行库在程序内安装。 |
| `screenkit1.0.9.apk` | 局域网文件/文本同步安卓客户端（同一发布页，或电脑 **文件同步 → 安装到手机**）。 |

解压后运行 `ScreenKit/ScreenKit.exe`。首次启动可出现安装向导。需要 [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48)。

更新日志：[CHANGELOG.md](CHANGELOG.md)（每个版本均有英文 + 中文）。

## 截图

![ScreenKit 主界面](docs/1%20screenshot.png)

## 功能一览

### 截图与识别

| 模块 | 说明 |
|------|------|
| **截图识别** | 框选区域 → 按结果区 OCR / 条码 Tab 识别；多显示器 DXGI。韩语/英语词间空格按画面词距补回；汉字之间不插空格。可选 LLM 叠字翻译。 |
| **截图标注** | 微信式工具条：矩形 / 椭圆 / 箭头 / 画笔 / 文字；完成旁下拉「复制为图片 / 文件 / 路径」。 |
| **长截图** | 点选可滚动窗口 → 自动滚动拼接（不做 OCR）。 |
| **区域录屏** | 点选窗口或框选 → HUD → MP4（**仅 FFmpeg** x264/x265/AV1）+ 可选系统声/麦克风；可选叠加鼠标与点击高亮。 |
| **GIF 录屏** | 同选区流程 → 采集 24fps → 预览（帧率/缩放/调色板）→ 无声 GIF。 |
| **剪贴板** | 粘贴图片识别；「编辑」菜单复制图片 / 文件 / 路径；菜单/托盘可切换截图完成时的复制方式。 |
| **文字叠加** | 松开才选一块，点空白取消，拖选不自动扩展；Ctrl+C 复制。 |
| **PDF 工作台** | 打开 PDF → 分页识别 → 改字 → 导出可检索 PDF。 |

### 语音、对话、翻译、人脸

| 模块 | 说明 |
|------|------|
| **ASR / TTS** | 实时字幕可选离线或流式。Sherpa / SAPI / WinRT 离线合成及 Edge 在线自然语音。 |
| **LLM对话** | 微信式气泡、清空、麦克风、朗读/自动朗读，可选网页与 `tmp/llm/` 工具。 |
| **翻译** | 本地 Opus-MT ONNX 或已配置的 **LLM**；来回翻译最多 20 次，结果重复时提前停止；小窗 `Ctrl+Alt+T`。 |
| **人脸识别** | InsightFace ONNX 检测/比对，可选关键点与性别年龄。模型在 `facemodels/`。 |
| **SAPI x86 助手** | 旁路 `x86host.exe`，调用仅 32 位可见的经典发音人。 |

### 传输、接口、安装

| 模块 | 说明 |
|------|------|
| **PC 文件传输** | 局域网 HTTP `17532` + UDP 发现 `17531`。主界面 **文件同步** Tab：上表平铺浏览程序旁 `sendfile/`（不进子目录），可框选、剪切/复制/粘贴、删到回收站、拖到资源管理器。下方拖入在手机已连接时发到手机绑定文件夹；手机分享写到电脑 `sendfile/`。**安装到手机** 显示局域网地址和二维码。配套 App：`android/`（`com.whj.screenkit`「PC文件传输」）。 |
| **HTTP API** | 本机 JSON 接口（默认 `127.0.0.1:1224`）。主界面 Tab：调用日志 + 手动发请求。 |
| **安装功能** | 功能选择树打开时勾选已装项；增删显示绿/红数量与大小；点确认即安装或卸载。发音人单独一页。中文环境优先国内镜像。 |
| **图片格式转换** | 菜单 **工具 → 图片格式转换**：批量转 JPG/PNG/BMP，可限制最大宽高、旋转/镜像；列表支持图片视图（小缩略图）与列表视图；选中后预览已应用质量与变换的效果；输出到源文件旁 `output/`、指定目录，或替换源文件（永久删除 / 回收站）。可选：压缩后体积仍 ≥ 原图 N% 时用原图（默认 80%；有旋转或实际缩小时仍用新图）。 |
| **二维码 / 条码** | **工具 → 二维码 / 条码生成**：QR、Data Matrix、Code 128/39、EAN、UPC。图下显示一行原文。编码 UTF-8、GBK 或 Hex 二进制（默认 UTF-8）。 |
| **批量重命名** | **工具 → 批量重命名**：源文件列表（添加/删除/上下移动，点列头按名称、修改或添加日期排序）；目标名用文本框可手改。Everything / FastCopy 风格（`%1`/`%2` 最短捕获、`#` / `###`）；改列表或规则时自动重算。 |
| **校验哈希** | **工具 → 校验哈希**：MD5 / SHA-1 / SHA-256；可粘贴期望值比对。 |
| **文本小工具** | **工具 → 文本小工具**：Base64、URL、UTF-8/GBK 十六进制、Unicode 转义、大小写、空白、字数、JSON 智能美化（短数组/对象同一行）。 |
| **密码生成器** | **工具 → 密码生成器**：加密随机；可选长度、字符集、排除易混 `0OIl1`、每类至少一个。记住上次设置。**单词译音** Tab：可选 LLM，译成中日韩英等，以及文言文、古希腊语、拉丁语、梵语、古希伯来语，并给出拼音 / romaji / 罗马字。 |
| **网络工具** | **工具 → 网络工具**：Ping、域名解析、WHOIS、路由跟踪、Ping 定位、代理定位、HTTP 测速定位。 |
| **全局热键** | 主窗呼出/隐藏 · 截图标注 · 截图识别 · 语音输入 · 翻译小窗（可配置、可清空禁用）。 |
| **主界面 Tab** | 参数设置 → 常规可分别隐藏截图识别 / 语音合成 / 语音识别 / LLM对话 / 翻译 / 人脸 / HTTP接口 / 文件同步（`tab_*_visible`）。 |
| **推理设备** | CPU · NVIDIA CUDA · 核显 DirectML；未装加速时自动 CPU。 |
| **CLI** | 批量识图、列模型 / SAPI 发音人、探测 CUDA、多屏抓取自检。 |

## 运行环境

- Windows 10/11（x64）
- 终端用户：[.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48)
- 编译：Visual Studio / MSBuild（可编 `net48` WPF）
- 可选：NVIDIA 显卡 + 与 `onnxgpu64` 匹配的 CUDA；支持 DirectML 的 GPU（`onnxdml64`）
- 可选（录屏）：exe 旁 `ffmpeg64/` 放置 FFmpeg **4.4 shared**

## 使用说明

### 截图、标注、识别

框选区域（热键或菜单）。结果区拆成 **OCR / 条码**。已选目标语且配置了 LLM 时，识别后可叠字翻译。标注工具在截图遮罩上；跨屏选区可在每一块相交的屏上画框、拖动或缩放（拖动热区在绿框外侧 10px）。「完成」下拉可复制为图片、文件或路径，并记住默认方式。

### 区域录屏 / GIF 录屏

1. **捕获 → 录屏**（或 GIF录屏）：单击窗口或拖拽框选。
2. **HUD**（画在选区外）：
   - 红框；拖动**红线外侧 5px**移动，或 **8 向手柄**缩放。**开始前**可自由改比例；**开始后**按 `record_lock_aspect`（默认锁定）。
   - 浮动**控制条**：左侧手柄拖动；可收起；开始前显示**选项**；开始与暂停同一位置。限制在当前显示器内。
3. 停止后保存 MP4（资源管理器选中文件），或打开 GIF 预览（输出帧率 1–24、缩放、调色板）再保存无声 GIF。
4. **捕获 → 录屏选项**：编码（x264 / x265 / AV1）、帧率、CRF / AV1 CRF（0–63，默认 56）、音频、最大尺寸、**录制鼠标** / **高亮鼠标点击**。AV1 需要 `ffmpeg64` 含 libsvtav1 / libaom-av1；找不到编码器会明确失败，不会改回 x264。

GDI 抓屏不含指针：勾选 **录制鼠标** 叠加系统光标；**高亮鼠标点击** 在左键黄圈 / 右键蓝圈 / 中键绿圈处短暂散开。GIF 体积随分辨率与时长快速增大，请用预览缩放/帧率及最大尺寸限制。

### 文件同步（电脑 ↔ 安卓）

1. 电脑打开 **文件同步** Tab（参数设置 → 接口里启用「PC 文件传输」，默认开）。
2. **安装到手机**：局域网地址 `http://<电脑IP>:17532/apk` 和二维码（多网卡时默认用能连外网的地址）。手机与电脑同一局域网，用浏览器扫码安装。
3. 首次连接：手机弹出等待窗（可取消）直到电脑允许配对。电脑弹窗始终置顶（主窗在托盘时仍能点到）。
4. 上表为电脑 `sendfile/` 接收目录（只看根目录、不进子目录；文件夹显示为一行，双击用资源管理器打开）。可选中、框选、剪切/复制/粘贴、删除到回收站、拖到资源管理器。往列表上拖文件会导入接收目录。往**下方**拖放区拖入：仅在手机 App 已连接时发到手机绑定文件夹，无连接则不传。粘贴文件/图片进入接收目录；粘贴文本仍到右侧。手机分享/上传一律写到电脑 `sendfile/`。
5. 两端显示待传进度；电脑还有传输记录。文本同步以只读框显示最新一条，可选中复制。

安卓端说明：[android/README.md](android/README.md)。

### 图片格式转换

**工具 → 图片格式转换**：拖入文件、文件夹或图片；目标 JPG / PNG / BMP；可选最大宽高（与截图相同的等比缩小）；选中项可旋转 90/180/270° 或镜像。**图片**视图显示小缩略图（可切回 **列表**）。右侧预览已应用质量与变换后的效果。一键转换输出到每个源文件旁的 `output/`、指定目录，或替换源文件（永久删除 / 回收站；转换前会确认）。

### 默认热键

| 热键 | 作用 |
|------|------|
| `Ctrl+Alt+O` | 切换主窗口显示 / 隐藏 |
| `Ctrl+Alt+Q` | 截图标注 |
| `Ctrl+Alt+W` | 截图并识别 |
| `Ctrl+Alt+V` | 语音输入（再按结束） |
| `Ctrl+Alt+B` | 实时字幕 |
| `Ctrl+Alt+T` | 翻译小窗呼出 / 隐藏 |

热键字符串留空表示禁用。托盘：左键单击切换窗口；右键含语音输入、翻译小窗、从剪贴板识别、截图完成复制方式、退出。关闭主窗口通常**隐藏到托盘**。在菜单或托盘切换三种截图复制方式时，会立刻用新方式把**上次截图**再写入剪贴板。

### 界面语言

菜单 **选项 → 界面语言** 切换 **中文 / English**，或在 **参数设置 → 常规** 选择（`ui_lang = "zh"` / `"en"`）。已覆盖菜单、参数设置、OCR 工具栏模型/语言显示名（`ocr-display.json` 的 `name` / `nameEn`）、翻译、人脸、翻译小窗等。

## 安装功能

1. 首次启动可出现安装向导（默认勾选：截图识别简中、ASR 前两项、录屏；**不勾** GPU/核显）。
2. 之后：**选项 → 安装功能**
   - **功能选择**：打开时勾选已装功能；增删显示将新增（绿）/ 删除（红）的组件数与大小，可复位。点确认即安装或卸载。发音人不在此页。
   - **发音人**：按语言筛选；下载进度显示**整批总大小与已下载量**；`.tar.bz2` 包由程序内部解压，无需系统 `tar` / `bzip2`，并支持将 `ttsmodels` 设为 Junction。
3. 使用某功能时若缺依赖，会提示打开安装窗（例如：没有任何 ORT 时做 OCR → 提示安装 `onnxcpu64`）。

| 运行库 | 用途 | 约体积 |
|--------|------|--------|
| **onnxcpu64** | CPU 推理用 ONNX Runtime（无 GPU/核显 ORT 时 OCR 需要） | ~16 MB |
| **onnxgpu64** | NVIDIA CUDA EP + CUDA/cuDNN（可选） | 较大 |
| **onnxdml64** | 核显 DirectML EP（可选） | ~18 MB |
| **OpenCV** | 截图 / 图像管线 | ~61 MB |
| **ffmpeg64** | 录屏编码封装 | ~72 MB |

界面或系统区域为中文时，下载优先国内镜像（ModelScope / HF 镜像 / GitHub 代理）。

**Edge 在线**引擎无需安装模型或填写 API Key，可使用 300 多个 Microsoft 自然语音（含韩语）。必须联网，发音人目录和合成文本会发送到 Microsoft Edge「朗读」服务；不是收费的 Azure Speech API，可用性可能变化。程序沿用「HTTP 代理」设置。

可选环境变量（勿把本机密钥/绝对路径写进对外文档）：

| 变量 | 含义 |
|------|------|
| `WPF_OCR_CUDA_LIB` | 完整 CUDA / onnxgpu64 DLL 目录 |
| `WPF_OCR_FFMPEG_LIB` | FFmpeg 4.4 shared DLL 目录 |

## 配置

设置保存在 exe 旁 `config.toml`（**选项 → 参数设置** / **录屏选项**）。参数设置按 Tab 分组：常规、识别、热键、语音、LLM接口、翻译、截图、接口；主界面 Tab 显示复选框位于「常规」。

| 段落 | 主要内容 |
|------|----------|
| `[ocr]` | 模型包、设备（`Cpu` / `Gpu` / `IntelGpu`）、检测阈值 |
| `[ui]` | 热键、托盘、`ui_lang`、`tab_*_visible`、`update_check_days`、`http_proxy`、`capture_log`、`llm_log`、`screenshot_keep_days`、`imgconv_*` |
| `[http]` | 本机 OCR API（`127.0.0.1:1224`）、服务模式 |
| `[sendfile]` | 局域网文件传输端口、显示名、已配对设备 |
| `[pdf]` | 不可见文字层、光栅 DPI |
| `[asr]` | 听写/实时字幕模式、润色、分句、`asr_llm` |
| `[[llm]]` | OpenAI 兼容接口（`name` / `url` / `key` / `model` / `think`） |
| `[translate]` | 本地 ONNX 设备或 `translate_llm` |
| `[record]` / `[gif_record]` | 编码、CRF、音频、鼠标叠加 |

勿将含本机路径或隐私偏好的 `config.toml` 提交到公开仓库。

<details>
<summary>示例 <code>config.toml</code></summary>

```toml
[ocr]
model_pack = "rapid-ch"
model_variant = "简体中文 mobile"
device = "Cpu"          # Cpu | Gpu | IntelGpu
det_limit = 960
det_thresh = 0.3
det_box_thresh = 0.5
use_cls = true

[ui]
hotkey = "Ctrl+Alt+O"
hotkey_snap = "Ctrl+Alt+Q"
hotkey_snap_ocr = "Ctrl+Alt+W"
minimize_to_tray = true
capture_log = false
ui_lang = "zh"
tab_ocr_visible = true
tab_tts_visible = true
tab_asr_visible = true
tab_chat_visible = true
tab_translate_visible = true
tab_face_visible = true
tab_http_visible = true
tab_sendfile_visible = true
update_check_days = 7
screenshot_keep_days = 3
# http_proxy = false
# http_proxy_addr = "127.0.0.1:7897"

[http]
http_enabled = true
http_host = "127.0.0.1"
http_port = 1224
service_mode = false

[sendfile]
sendfile_enabled = true
sendfile_port = 17532
sendfile_udp_port = 17531
sendfile_name = ""

[pdf]
pdf_invisible_text = true
pdf_dpi = 150

[record]
record_codec = "x264"
record_fps = 24
record_crf = 28
record_av1_crf = 56
record_audio = true
record_audio_src = "Speakers"
record_audio_kbps = 96
record_lock_aspect = true
record_mouse = true
record_click_highlight = true

[asr]
asr_voice_mode = "stream"
asr_voice_polish = true
asr_voice_split = true
asr_voice_split_sec = 5
asr_live_mode = "stream"
asr_live_polish = false
asr_live_split = true
asr_llm = "gpt-4o-mini"

[[llm]]
name = "gpt-4o-mini"
url = "https://api.openai.com/v1"
# key = ""
model = "gpt-4o-mini"
think = "low"                   # off | low | medium | high | max

[translate]
translate_compute = "Auto"
# translate_llm = ""
# translate_llm_prompt = "请将用户给出的文本从{src}翻译为{dst}。只输出译文。"

[gif_record]
gif_fps = 8
gif_max_size = true
gif_max_w = 1280
gif_max_h = 720
gif_colors = 128
gif_scale = 100
gif_mouse = true
gif_click_highlight = true
```

</details>

`think`：`off` 发关闭思考；`low`/`medium`/`high`/`max` 发 `thinking.type=enabled` 与 `reasoning_effort`。若 `off` 被拒绝则改 `low` 再试。访问 **opencode.ai** 时自动加 `x-opencode-session` 头。旧键 `asr_llm_url` / `asr_llm_token` / `asr_llm_model` 已废弃。

`capture_log = true` 写 `log/capture.log`（多屏/DPI、截图落盘耗时，≥500ms 标 `SLOW`）。`llm_log = true` 写 `log/llm.log`（不含 key）。CLI `ScreenKit --snap` 把整屏位图写到 `log/snap/`；`--test-overlay-layout` 弹出截屏遮罩并记录各屏 HWND/DPI。

## HTTP API（简述）

启用后监听 `http_host:http_port`（默认仅本机）。勿在未受控网络上暴露。

另有独立 **PC 文件传输** 服务（HTTP `17532`、UDP 发现 `17531`，需配对）：[HTTP接口文档.md](HTTP接口文档.md) · [android/README.md](android/README.md)。

- `GET  /api` · `/api/status`
- `POST /api/ocr` — `box` 为原图像素
- `POST /api/qr` — 仅条码/二维码（`/api/barcode`）
- `GET  /api/ocr/get_options`
- `GET  /api/asr/models` · `POST /api/asr`
- `GET  /api/tts/models` · `POST /api/tts` — Sherpa、SAPI、Windows（`engine=winrt`）、Edge（`engine=edge`）
- `POST /api/itn`
- `POST /api/translate` — LLM 批量（`items[]`）
- `GET  /api/face/models` · `POST /api/face`

完整字段：[HTTP接口文档.md](HTTP接口文档.md) · [HTTP-API.md](HTTP-API.md)（English）。

## CLI（简述）

```text
ScreenKit --image <路径> [选项]
ScreenKit --snap [--out <目录>]
ScreenKit --test-overlay-layout   # 截屏遮罩各屏 HWND/DPI
ScreenKit --test-overlay-span-adj # 跨屏选区副屏手柄
ScreenKit --test-clipboard-path
ScreenKit --test-apk-qr
ScreenKit --test-img-convert
ScreenKit --test-qr-make
ScreenKit --test-rename
ScreenKit --test-hash
ScreenKit --test-texttool
ScreenKit --test-pwgen
ScreenKit --test-nettool
ScreenKit --test-sendfile
ScreenKit --test-face-overlay
ScreenKit --list-models
ScreenKit --list-face
ScreenKit --list-sapi
ScreenKit --test-tts-sherpa <模型名>
ScreenKit --list-edge-tts
ScreenKit --test-edge-tts ko-KR-SunHiNeural
ScreenKit --test-http-tts
ScreenKit --test-llm-chat
ScreenKit --test-llm-agent
ScreenKit --test-http-chat
ScreenKit --probe-cuda
ScreenKit --help
```

常用选项：`-d gpu|cpu`，`-p rapid-ch`，`-v <变体>`，`-m <模型目录>`，`--det-limit`，`--no-cls`。

## x86host（仅 32 位 SAPI）

部分经典 **SAPI** 发音人只在 32 位进程中可见。请将 **`x86host.exe`** 与 `ScreenKit.exe` 放在同目录（工程 `x86host/`；Release 编 ScreenKit 时会自动编译并拷贝）。

| 项 | 说明 |
|----|------|
| 作用 | HTTP 助手：列 SAPI 发音人 + 合成 WAV；**无 GUI** |
| 启动 | x64 主程序按需拉起，或手动运行 |
| 监听 | 仅 `127.0.0.1`，默认端口 **17886** |
| 空闲 | **60 秒**无请求自动退出（`--idle-ms` 可改） |
| API | `GET /api/sapi/status` · `GET /api/sapi/voices` · `POST /api/sapi/synth` · `POST /api/sapi/shutdown` |

```text
dotnet build x86host/x86host.csproj -c Release
x86host.exe --port 17886 --idle-ms 60000
x86host.exe --list-sapi
```

界面中引擎选 **SAPI**：列表含本机音与 **x86 独有**音（显示名带 `· x86`）。

## 从源码编译

```
OCR/
├── ScreenKit/                 # 主程序（WPF，net48，x64）
│   └── bin/Release/
│       ├── net48/          # 开发输出（模型、运行库放这里）
│       └── ScreenKit/      # 精简包：exe + x86host.exe + 托管依赖
├── x86host/                # 32 位 SAPI Web 助手
├── android/                # 配套 App（com.whj.screenkit）
├── docs/                   # README 截图
├── scripts/publish-release.mjs
├── README.md · README.zh.md · CHANGELOG.md
└── HTTP-API.md · HTTP接口文档.md
```

**源码目录不包含模型与大型运行库。** 请放到可执行文件旁，或在程序内「安装功能」下载：

```
ScreenKit/bin/Release/net48/
├── ScreenKit.exe
├── config.toml
├── ocrmodels/  asrmodels/  ttsmodels/  translatemodels/  facemodels/
├── onnxcpu64/  onnxgpu64/  onnxdml64/
└── ffmpeg64/
```

**Release** 编译不会复制这些目录。每个 OCR 模型包需包含 ONNX、`configs.txt` 以及字典/keys。可选 `pack.json`（`name` / `nameEn` / `variants`）提供英文显示名；内置默认在程序旁 `ocr-display.json`。

```bash
cd ScreenKit
dotnet build -c Release
./ScreenKit/bin/Release/net48/ScreenKit.exe
```

### 精简发布包（`bin\Release\ScreenKit\`）

- **包含**：`ScreenKit.exe`、**`x86host.exe`**、托管依赖、**`wetext/`**（ITN）、Assets、许可证。
- **不含**：OCR/ASR/TTS/人脸模型、ORT、OpenCV / Skia / PDFium、Sherpa natives、`ffmpeg64`。
- 用户通过 **安装功能** 按需下载。本地 Opus-MT 需自行将 ONNX 放到 `translatemodels/`。

本机开发且已有模型时，请继续用 **`bin\Release\net48\`**。

### 发布压缩包

```bash
node scripts/publish-release.mjs
```

Release 编译后，将 `ScreenKit/bin/Release/ScreenKit/` 打进 `release/screenkit_<版本>.7z`（需本机 7-Zip 且 `7z` 在 PATH）。`release/` 目录不提交 git。

发布文档规范：

- `CHANGELOG.md` 的每个版本必须同时包含 **English** 和 **中文**。
- GitHub Release 说明先写英文摘要，再写中文摘要。
- 校验值和链接放在两种语言摘要之后，仅列一次。

## 许可证

**本仓库应用程序源码**采用 **MIT License**，全文见 [LICENSE](LICENSE)。

```
Copyright (c) 2026 ScreenKit Contributors
```

在遵守 MIT 条件的前提下，可自由使用、复制、修改、合并、发布、分发、再授权及销售。**软件按「现状」提供，不附带任何明示或暗示担保。**

模型、FFmpeg、CUDA/cuDNN、部分原生库等**不**自动适用本仓库 MIT。摘要见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。请勿在未理清再分发条件的情况下，将大型 ONNX、CUDA 再发行库、FFmpeg shared 提交进 git。

## 相关文档

- [CHANGELOG.md](CHANGELOG.md)
- [HTTP接口文档.md](HTTP接口文档.md) · [HTTP-API.md](HTTP-API.md)
- [android/README.md](android/README.md) — 配套安卓客户端
- [LICENSE](LICENSE) · [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
- [README.md](README.md) — English documentation
