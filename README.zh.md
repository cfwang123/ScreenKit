# WpfOCR

Windows 桌面 OCR：截图识别、截图标注、长截图、**区域录屏**、剪贴板识图、PDF 工作台、语音识别/合成、可选翻译，以及可选本机 HTTP API（Umi 风格 PP-OCR 模型包）。

[English](README.md) · [中文](README.zh.md)

## 功能一览

| 模块 | 说明 |
|------|------|
| **截图识别** | 框选区域 → OCR；多显示器 DXGI 抓屏 |
| **截图标注** | 微信式工具条：矩形 / 椭圆 / 箭头 / 画笔 / 文字、色点、撤销 / 保存 / 完成 |
| **长截图** | 点选可滚动窗口 → 自动滚动拼接 → 上屏（不做 OCR） |
| **区域录屏** | 点选窗口或拖拽框选 → HUD → 导出 MP4（**仅 FFmpeg** x264/x265）+ 可选系统声/麦克风 |
| **剪贴板** | 粘贴图片识别；复制图片 / 文字 |
| **文字叠加** | 图上叠加文字层，拖选复制 |
| **PDF 工作台** | 打开 PDF → 分页识别 → 改字 → 导出可检索 PDF（不可见文字层） |
| **ASR / TTS** | 离线语音识别（sherpa-onnx）与语音合成；应用内安装发音人 |
| **推理设备** | CPU · NVIDIA CUDA（GPU）· 核显 DirectML；未装加速时自动 CPU |
| **安装功能** | 应用内下载模型与运行库（中文环境优先国内镜像） |
| **全局热键** | 主窗呼出/隐藏 · 截图标注 · 截图识别（可配置、可清空禁用） |
| **HTTP API** | 本机 Umi 兼容接口（默认 `127.0.0.1:1224`） |
| **CLI** | 批量识图、列模型、探测 CUDA、多屏抓取自检 |

## 运行环境

- Windows 10/11（x64）
- 终端用户：安装 [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48)
- 编译：Visual Studio / MSBuild（可编 `net48` WPF）
- 可选：NVIDIA 显卡 + 与 ORT GPU 包匹配的 CUDA 运行库（`onnxgpu64`）
- 可选：支持 DirectML 的 GPU（设备选「核显」，`onnxdml64`）
- 可选（录屏）：exe 旁 `ffmpeg64/` 放置 FFmpeg **4.4 shared** 动态库

## 目录结构

```
OCR/
├── WpfOCR/                 # 源码（WPF，net48）
│   ├── Assets/
│   └── bin/Release/net48/  # 发布输出（模型、运行库放这里）
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

> 请使用 **`net48`** 输出目录运行。勿使用只拷了 exe、缺少运行库的旁路目录。

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
2. HUD 显示选区外红框 + 开始 / 暂停 / 停止。
3. 停止后确认保存 → 写出 MP4；资源管理器打开目录并选中文件。
4. **捕获 → 录屏选项**：编码、帧率、CRF、音频来源、最大输出尺寸等。

**说明**

- 录屏依赖 **FFmpeg shared**（`ffmpeg64/`，应用内安装或手动放置）。**不再**使用 OpenCV 写视频。
- 系统环回在静音段会按墙钟补静音，避免后段声音前移到片头。
- 临时文件在程序 `tmp/` 下，保存成功后会清理。

## 默认热键

| 热键 | 作用 |
|------|------|
| `Ctrl+Alt+O` | 切换主窗口显示 / 隐藏 |
| `Ctrl+Alt+Q` | 截图标注 |
| `Ctrl+Alt+W` | 截图并识别 |

托盘：左键单击切换窗口；右键菜单含「从剪贴板识别」「退出」。关闭主窗口通常**隐藏到托盘**而非退出。

### 界面语言

- 菜单 **工具 → 界面语言** 切换 **中文 / English**（即时生效）
- 或在 **参数设置** 顶部选择界面语言
- 写入 `config.toml`：`ui_lang = "zh"` 或 `"en"`

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

设置保存在 exe 旁 `config.toml`（也可用 **工具 → 参数设置** / **录屏选项** 编辑）。主要段落：

- `[ocr]`：模型包、设备（`Cpu` / `Gpu` / `IntelGpu`）、检测阈值等  
- `[ui]`：热键、托盘、界面语言、`capture_log`  
- `[http]`：本机 API 开关与端口  
- `[pdf]`：导出与内部光栅 DPI  
- `[record]`：编码、帧率、CRF、音频  

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
