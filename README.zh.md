# WpfOCR

Windows 桌面 OCR：截图识别、截图标注、长截图、**区域录屏**、剪贴板识图、PDF 工作台、语音识别/合成、可选翻译，以及可选本机 HTTP API（PP-OCR / RapidOCR 模型包）。

当前版本：**1.0.2**

[English](README.md) · [中文](README.zh.md)

## 截图

![WpfOCR 主界面](docs/1%20screenshot.png)

## 功能一览

| 模块 | 说明 |
|------|------|
| **截图识别** | 框选区域 → 按结果区当前 OCR / 条码 Tab 识别文字或条码/二维码，识别后保持当前 Tab；多显示器 DXGI 抓屏 |
| **截图标注** | 微信式工具条：矩形 / 椭圆 / 箭头 / 画笔 / 文字、色点、撤销 / 保存 / 完成 |
| **长截图** | 点选可滚动窗口 → 自动滚动拼接 → 上屏（不做 OCR） |
| **区域录屏** | 点选窗口或拖拽框选 → HUD（可移动/缩放选区、可拖动控制条）→ MP4（**仅 FFmpeg** x264/x265/AV1）+ 可选系统声/麦克风 |
| **GIF 录屏** | 同选区流程 → 采集 24fps → 预览（输出帧率/缩放/调色板）→ 无声 GIF |
| **剪贴板** | 粘贴图片识别；复制图片 / 文字；菜单/托盘可切换截图完成时「复制为图片 / 文件 / 路径」，切换时会按新方式重写上次截图到剪贴板 |
| **文字叠加** | 图上叠加文字层，拖选复制 |
| **PDF 工作台** | 打开 PDF → 分页识别 → 改字 → 导出可检索 PDF（不可见文字层） |
| **ASR / TTS** | 离线语音识别（sherpa-onnx）与语音合成（Sherpa + SAPI / WinRT 系统音）；应用内安装发音人 |
| **SAPI x86 助手** | 旁路 `x86host.exe`（仅 32 位 SAPI Web），调用仅 x86 可见的经典系统发音人 |
| **推理设备** | CPU · NVIDIA CUDA（GPU）· 核显 DirectML；未装加速时自动 CPU |
| **安装功能** | 应用内下载模型与运行库（中文环境优先国内镜像） |
| **全局热键** | 主窗呼出/隐藏 · 截图标注 · 截图识别 · 语音输入（可配置、可清空禁用） |
| **HTTP API** | 本机 JSON 接口（默认 `127.0.0.1:1224`） |
| **CLI** | 批量识图、列模型 / SAPI 发音人、探测 CUDA、多屏抓取自检 |

## 运行环境

- Windows 10/11（x64）
- 终端用户：安装 [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48)
- 编译：Visual Studio / MSBuild（可编 `net48` WPF）
- 可选：NVIDIA 显卡 + 与 ORT GPU 包匹配的 CUDA 运行库（`onnxgpu64`）
- 可选：支持 DirectML 的 GPU（设备选「核显」，`onnxdml64`）
- 可选（录屏）：exe 旁 `ffmpeg64/` 放置 FFmpeg **4.4 shared** 动态库
- 可选（部分 SAPI 仅 32 位）：同目录 `x86host.exe`（见下文）

## 目录结构

```
OCR/
├── WpfOCR/                 # 主程序源码（WPF，net48，x64）
│   ├── Assets/
│   └── bin/Release/
│       ├── net48/          # 开发输出（模型、运行库放这里）
│       └── WpfOCR/         # 精简包：WpfOCR.exe + x86host.exe + 托管依赖
├── x86host/                # 独立 32 位 SAPI Web 助手（仅 x86host.exe）
├── docs/                   # README 截图
├── scripts/publish-release.mjs
├── README.md
├── README.zh.md
├── CHANGELOG.md
└── AGENTS.md
```

**源码目录不包含模型与大型运行库。** 请放到可执行文件旁，或在程序内「安装功能」下载：

```
WpfOCR/bin/Release/net48/
├── WpfOCR.exe
├── config.toml              # 运行时生成/更新
├── ocrmodels/               # OCR 模型包
├── asrmodels/               # ASR 模型（可选）
├── ttsmodels/               # TTS 发音人（可选）
├── translatemodels/         # 翻译 ONNX（可选）
├── onnxcpu64/               # CPU 用 ONNX Runtime（按需安装）
├── onnxgpu64/               # CUDA ORT + CUDA 库（可选）
├── onnxdml64/               # DirectML ORT（可选）
└── ffmpeg64/                # 录屏 FFmpeg shared（可选）
```

每个 OCR 模型包需包含 ONNX、`configs.txt`，以及字典/keys 等该包所需文件。

## 编译与运行

```bash
cd WpfOCR
dotnet build -c Release
```

运行：

```bash
./WpfOCR/bin/Release/net48/WpfOCR.exe
```

纯编译**不会**附带模型、`onnxcpu64`、完整 CUDA 或 FFmpeg。请在程序内使用 **工具 → 安装功能**（或首次启动向导）。

### 精简发布包（`bin\Release\WpfOCR\`）

Release 编译后会同步生成可对外分发的精简目录 `WpfOCR\bin\Release\WpfOCR\`：

- **包含**：`WpfOCR.exe`、**`x86host.exe`**（32 位 SAPI Web）、托管依赖、**`wetext/`**（ITN）、Assets、许可证。
- **不含**：OCR/ASR/TTS 模型、`onnxcpu64` / GPU / 核显、OpenCV / Skia / PDFium、Sherpa natives、`ffmpeg64`。
- 用户通过 **安装功能** 按需下载（国内镜像 / NuGet CDN）。
- **翻译**不在安装窗内：如需翻译，自行将 Opus-MT ONNX 放到 `translatemodels/`。

本机开发且已有模型/GPU 时，请继续用 **`bin\Release\net48\`**。

### 发布压缩包（`release/wpfocr_x.x.x.7z`）

```bash
node scripts/publish-release.mjs
```

Release 编译后，将 `WpfOCR/bin/Release/WpfOCR/` 打成 `release/wpfocr_<版本>.7z`（需本机安装 7-Zip 且 `7z` 在 PATH）。`release/` 目录不提交 git。

## 应用内安装（推荐）

1. 首次启动可出现安装向导（默认勾选：OpenCV、**ORT CPU**、OCR `rapid-ch`、ASR 前两项、FFmpeg；**不勾** GPU/核显）。
2. 之后：**工具 → 安装功能**
   - **功能组件**：OpenCV、Skia、PDFium、Sherpa、**ORT CPU（onnxcpu64）**、OCR/ASR 包、CUDA、DirectML、FFmpeg。
   - **发音人**：按语言筛选；下载进度显示**整批总大小与已下载量**。
3. 使用某功能时若缺依赖，会提示打开安装窗（例如：没有任何 ORT 时做 OCR → 提示安装 `onnxcpu64`）。

| 运行库 | 用途 | 约体积 |
|--------|------|--------|
| **onnxcpu64** | CPU 推理用 ONNX Runtime（无 GPU/核显 ORT 时 OCR 需要） | ~16 MB |
| **onnxgpu64** | NVIDIA CUDA EP + CUDA/cuDNN（可选） | 较大 |
| **onnxdml64** | 核显 DirectML EP（可选） | ~18 MB |
| **OpenCV** | 截图 / 图像管线 | ~61 MB |
| **ffmpeg64** | 录屏编码封装 | ~72 MB |

界面或系统区域为中文时，下载优先国内镜像（ModelScope / HF 镜像 / GitHub 代理）。

可选环境变量（本地完整库路径，勿把本机密钥/绝对路径写进对外文档）：

| 变量 | 含义 |
|------|------|
| `WPF_OCR_CUDA_LIB` | 完整 CUDA / onnxgpu64 DLL 目录 |
| `WPF_OCR_FFMPEG_LIB` | FFmpeg 4.4 shared DLL 目录 |

## 区域录屏

1. **捕获 → 录屏**（或工具栏）：单击窗口或拖拽框选。
2. **HUD**（画在选区外）：
   - 红框；拖动**红线外侧 5px**移动，或 **8 向手柄**缩放。**开始前**可自由改比例；**开始后**按录屏选项决定是否锁定宽高比（`record_lock_aspect`，默认 true）。点「开始」时锁定编码分辨率。
   - 浮动**控制条**：左侧**手柄**拖动；可**收起**；开始前显示**选项**（录屏/GIF 参数）；开始与暂停同一位置切换。
   - 控制条自动贴在选区上/下方，并限制在**当前显示器**内（多屏可用）。
3. 停止后确认保存 → 写出 MP4；资源管理器打开目录并选中文件。
4. **捕获 → 录屏选项**：编码（x264 / x265 / AV1）、帧率、**CRF**（x264/x265）与 **AV1 CRF**（仅 AV1，0–63 独立刻度，默认 56）、音频来源、最大输出尺寸等。AV1 需要 `ffmpeg64` 含 libsvtav1 / libaom-av1；找不到编码器会明确失败，**不会**改回 x264。

### GIF 录屏

1. **捕获 → GIF录屏**：同样点选/框选区域。
2. 同一套 HUD；**采集固定 24fps**；停止后打开 **预览窗**（可调输出帧率 1–24、缩放、调色板颜色数）。
3. 确认后 **保存** 为无声 GIF。
4. **捕获 → GIF录屏选项**：默认输出帧率、最大宽高、默认颜色数等。

**说明**

- 录屏 / GIF 录屏依赖 **FFmpeg shared**（`ffmpeg64/`，应用内安装或手动放置）。**不再**使用 OpenCV 写视频。
- 系统环回在静音段会按墙钟补静音，避免后段声音前移到片头。
- 临时文件在程序 `tmp/` 下，保存成功后会清理。
- GIF 体积随分辨率与时长快速增大，请用预览缩放/帧率及最大尺寸限制。

## 默认热键

| 热键 | 作用 |
|------|------|
| `Ctrl+Alt+O` | 切换主窗口显示 / 隐藏 |
| `Ctrl+Alt+Q` | 截图标注 |
| `Ctrl+Alt+W` | 截图并识别 |
| `Ctrl+Alt+V` | 语音输入（再按结束） |
| `Ctrl+Alt+B` | 实时字幕 |

托盘：左键单击切换窗口；右键菜单含「语音输入」「从剪贴板识别」「截图完成 · 复制为…」「退出」。关闭主窗口通常**隐藏到托盘**而非退出。在菜单或托盘切换三种截图复制方式时，会立刻用新方式把**上次截图**再写入剪贴板。主菜单 **截图 → 语音输入** 与热键相同。

### 界面语言

- 菜单 **工具 → 界面语言** 切换 **中文 / English**（即时生效）
- 或在 **参数设置 → 常规** 选择界面语言
- 写入 `config.toml`：`ui_lang = "zh"` 或 `"en"`

## CLI（简述）

```text
WpfOCR --image <路径> [选项]
WpfOCR --snap [--out <目录>]
WpfOCR --list-models
WpfOCR --list-sapi              # 本机 SAPI +（x64 时）x86host 发音人
WpfOCR --probe-cuda
WpfOCR --help
```

## x86host（仅 32 位 SAPI）

部分经典 **SAPI** 发音人只在 32 位进程中可见。请将 **`x86host.exe`** 与 `WpfOCR.exe` 放在同目录（工程 `x86host/`；Release 编 WpfOCR 时会自动编译并拷贝）。

| 项 | 说明 |
|----|------|
| 作用 | HTTP 助手：列 SAPI 发音人 + 合成 WAV；**无 GUI**，无 OCR/ASR |
| 启动 | x64 主程序按需拉起，或手动运行 `x86host.exe` |
| 监听 | 仅 `127.0.0.1`，默认端口 **17886** |
| 空闲 | **60 秒**无请求自动退出（`--idle-ms` 可改） |
| API | `GET /api/sapi/status` · `GET /api/sapi/voices` · `POST /api/sapi/synth` · `POST /api/sapi/shutdown` |

```text
dotnet build x86host/x86host.csproj -c Release
x86host.exe --port 17886 --idle-ms 60000
x86host.exe --list-sapi
```

界面中引擎选 **SAPI**：列表含本机音与 **x86 独有**音（显示名带 `· x86`）；选中后朗读/导出经 x86host 合成。

## HTTP API（简述）

启用后监听 `http_host:http_port`（默认仅本机）。

- `GET  /api` · `/api/status` — 能力与状态
- `POST /api/ocr` — 图片（JSON base64 或 multipart）
- `GET  /api/ocr/get_options` — 当前 OCR 参数快照
- `GET  /api/asr/models` · `POST /api/asr` — 语音识别（base64/本地 path）
- `GET  /api/tts/models` · `POST /api/tts` — 语音合成（返回 wav base64）
- `POST /api/itn` — 文本逆归一化（WeText + 规则）

默认绑定 `127.0.0.1`，勿在未受控网络上暴露。

完整请求/响应字段与示例见 **[HTTP接口文档.md](HTTP接口文档.md)** · **[HTTP-API.md](HTTP-API.md)**（English）。

## 配置摘要

设置保存在 exe 旁 `config.toml`（也可用 **工具 → 参数设置** / **录屏选项** 编辑）。参数设置窗按 Tab 分组：常规、识别、热键、语音、截图、接口。主要段落：

- `[ocr]`：模型包、设备（`Cpu` / `Gpu` / `IntelGpu`）、检测阈值等  
- `[ui]`：热键、托盘、界面语言、`capture_log`  
- `[http]`：本机 API 开关与端口、服务模式  
- `[pdf]`：导出与内部光栅 DPI  
- `[asr]`：离线/流式模型、听写 `asr_voice_mode`（`stream`/`offline`）、`asr_voice_polish` / `asr_voice_split`（自动分句时成句即润色并输入）、`asr_voice_split_sec`（仅静音达到该秒数才切句，默认 5，连续说话不切）；实时字幕 `asr_live_mode`（`stream`/`offline` 静音切句）、`asr_live_polish` / `asr_live_split`；共用 `asr_llm_*`（token 勿提交公开仓库；会去掉 `<think>` 等推理块）。润色时附带本轮已输出上文（约千字），模型只返回当前句。听写时浮窗第一行保持「听写中」提示；第二行显示「识别中 · Esc 停止」或「润色中」及原文（同一行）；已输出后第二行清空。识别/润色中按 Esc 立刻结束本轮听写且不输出。  
- `[record]`：编码（x264 / x265 / av1）、帧率、`record_crf`（x264/x265）、`record_av1_crf`（AV1）、音频、`record_lock_aspect`（录制中缩放选区是否锁定比例）
- `[gif_record]`：GIF 默认输出帧率（1–24）、最大宽高、默认颜色数/缩放  

热键字符串留空表示禁用该热键。勿将含本机路径或隐私偏好的 `config.toml` 提交到公开仓库。

## 许可证

**本仓库应用程序源码**（`WpfOCR/` 源码、为本项目撰写的文档等）采用 **MIT License**，全文见 [LICENSE](LICENSE)。

```
Copyright (c) 2026 WpfOCR Contributors
```

在遵守 MIT 条件（保留版权与许可声明）的前提下，可自由使用、复制、修改、合并、发布、分发、再授权及销售。**软件按「现状」提供，不附带任何明示或暗示担保。**

### 第三方组件

模型、FFmpeg 构建、CUDA/cuDNN、部分原生库等**不**自动适用本仓库 MIT，须遵守各自许可。摘要见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

请勿在未理清再分发条件的情况下，将大型 ONNX、CUDA 再发行库、FFmpeg shared 提交进 git 或对外打包。

## 相关文档

- [HTTP接口文档.md](HTTP接口文档.md) · [HTTP-API.md](HTTP-API.md) — 本机 HTTP API 完整说明
- [LICENSE](LICENSE) — MIT（本项目源码）
- [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) — 第三方许可说明
- [CHANGELOG.md](CHANGELOG.md)
- [README.md](README.md) — English documentation
