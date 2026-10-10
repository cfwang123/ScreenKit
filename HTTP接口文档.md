# ScreenKit HTTP 接口文档

**语言：** [中文](HTTP接口文档.md) · [English](HTTP-API.md)

本机 HTTP API：提供 OCR 接口，并扩展 ASR / TTS / ITN / 条码 / 人脸。

默认允许局域网访问；关掉后只听本机。**不要**在未受控网络上暴露该端口。

---

## 1. 启用与地址

在 **参数设置** 中开启 HTTP，或编辑程序目录 `config.toml`：

```toml
http_enabled = true
http_ocr = true
http_tts = true
http_asr = true
http_translate = true
http_chat = true
http_face = true
http_lan = true
http_port = 1224
```

各 `http_*` 默认开启。关掉一项后，`GET /api` 不再列出该路径，请求返回 810。`/api/status`、`/api/toast`、`/api/zhconv`、`/api/calendar`、`/api/jpyomi`、`/api/text`、`/api/qrmake`、`/api/qrscan` 和 `/api/cast/stop` 仍可用。`GET /` 是本机工具页。`http_enabled = false` 关闭整个服务。OCR 同时包含 `/api/qr`。ASR 同时包含 `/api/itn` 和 `/api/asr/models`。

| 项 | 说明 |
|----|------|
| 基址 | `http://127.0.0.1:{http_port}`；`http_lan = true` 时局域网用本机网卡 IP，同一端口 |
| 默认 | 端口 `1224`，`http_lan = true`（本机与局域网）。`http_lan = false` 时只听 `127.0.0.1` |
| Content-Type | 请求/响应 JSON 均建议 `application/json; charset=utf-8` |
| JSON 文本 | 中文等直接 UTF-8 输出，不用 `\uXXXX` 转义 |
| CORS | 已允许 `*`，便于本机网页调试 |
| OPTIONS | 支持预检（204） |

底部状态栏在内存汇总旁一直显示监听地址，例如 `HTTP 0.0.0.0:1224` 或 `HTTP 127.0.0.1:1224`。只有传文件的网卡监听时显示「HTTP 局域网:1224」。接口、传文件和投屏都关闭时显示「HTTP 未启用」。其中一项开着但没听上时显示「HTTP 未启动」，并带上失败原因。点 HTTP 文字用浏览器打开 web工具页；点模型摘要打开内存占用；右边空白没有点击效果。

---

## 2. 统一响应约定

多数接口 **HTTP 状态码固定 200**，业务成败看 JSON 里的 `code`。路由错误可能返回 404/405；未捕获异常可能为 500。

### 2.1 成功（有业务数据）

```json
{
  "code": 100,
  "data": { },
  "time": 12,
  "timestamp": 1710000000
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `code` | int | `100` 成功；`101` OCR 未检出文字；其它为错误码 |
| `data` | any | 结果；失败时多为错误说明字符串 |
| `time` | int | 可选，处理耗时（毫秒） |
| `timestamp` | long | 可选，Unix 秒 |

### 2.2 错误示例

```json
{
  "code": 802,
  "data": "请求中缺少 base64 字段。"
}
```

### 2.3 常见业务码

| code | 含义（摘要） |
|------|----------------|
| 100 | 成功 |
| 101 | OCR 未检测到文字；条码未检出；人脸未检测到人脸 |
| 404 | 未知路径 |
| 800 | 请求解析失败 / 体为空 |
| 801 | 请求为空 |
| 802 | 缺少必要字段（如 base64、text） |
| 803 | 字段类型/长度非法 |
| 804 | options 解释失败 |
| 805 | 方法不允许（应用层提示；同时 HTTP 可能为 405） |
| 806 | base64 解码失败 |
| 810 | 该接口模块已关闭（参数设置 → 接口） |
| 900 | 内部错误 |
| 901 | OCR 识别失败 |
| 910+ | ASR 相关 |
| 920+ | TTS 相关（921 Sherpa 不可用，922 无模型/发音人，923 音频空，924 合成失败，925 未知 engine） |
| 930+ | 人脸相关（930 无模型，931 识别失败，932 无检测/识别文件） |
| 950 | 条码/二维码识别失败 |
| 960+ | LLM 对话（960 未配置 LLM，961 缺文本/音频，962 语音识别失败，963 对话失败） |

---

## 3. 接口一览

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/` | 本机工具页（HTML）。资源 `/sk/tools.css`、`/sk/tools.js`、`/sk/fa.css`、`/sk/fa-solid-900.woff2` |
| GET | `/api` | API 说明与端点列表 |
| GET | `/api/status` · `/api/health` | 服务与能力状态 |
| GET/POST | `/api/toast` | 屏幕底部 Toast（不随模块开关关闭） |
| GET/POST | `/api/zhconv` | 简繁转换（不随模块开关关闭） |
| GET/POST | `/api/calendar` | 公历换历法（不随模块开关关闭） |
| GET/POST | `/api/jpyomi` | 日文注音（不随模块开关关闭） |
| GET/POST | `/api/text` | 文本编解码（不随模块开关关闭） |
| GET/POST | `/api/qrmake` | 生成二维码/条码（不随模块开关关闭） |
| POST | `/api/qrscan` | 识别二维码/条码（不随模块开关关闭） |
| GET | `/api/ocr/models` | 识别引擎和模型（与主窗口相同） |
| GET | `/api/ocr/get_options` | OCR 可选项描述 |
| POST | `/api/ocr` | 图片 OCR |
| POST | `/api/qr` · `/api/barcode` · `/api/barcodes` | 仅条码/二维码（不跑 OCR） |
| GET | `/api/asr/models` | 列出 ASR 模型 |
| POST | `/api/asr` | 语音识别 |
| GET | `/api/tts/engines` | 只列出 TTS 引擎 |
| GET | `/api/tts/models` | 列出 TTS 模型。`engine` 按引擎过滤；全量结果会缓存 |
| POST | `/api/tts` | 语音合成（返回 WAV base64） |
| POST | `/api/itn` | 文本逆归一化（WeText + 规则后处理） |
| POST | `/api/translate` · `/api/translate/batch` | LLM 批量翻译（需已配置 `[[llm]]`） |
| POST | `/api/chat` | LLM 对话（文本或语音入；可选 TTS 返回 `wav_base64`） |
| GET/POST | `/api/cast/stop` | 立即关闭投屏画面窗并断开当前接收 |
| WebSocket | `/cast` | 投屏媒体（与 HTTP 同端口；二进制帧为 SCST） |
| GET | `/api/face/models` | 列出人脸 ONNX |
| POST | `/api/face` | 人脸检测 / 特征 / 两图比对 |

路径大小写不敏感；尾部 `/` 可有可无。参数设置 → 接口里关掉的模块不会出现在 `GET /api` 里，请求返回 810。

---

## 4. GET `/` 与 GET `/api`

`GET /` 和 `HEAD /` 返回本机工具页：简繁、历法、日文注音、文本、二维码和条码。页面加载 `/sk/tools.css`、`/sk/tools.js` 和 `/sk/fa.css`（Font Awesome，字体 `/sk/fa-solid-900.woff2`），并调用 `/api/zhconv`、`/api/calendar`、`/api/jpyomi`、`/api/text`、`/api/qrmake`、`/api/qrscan`。电脑版文件管理在 `/files`。

`GET /api` 返回服务名称与端点列表。

**响应示例：**

```json
{
  "code": 100,
  "data": {
    "name": "ScreenKit HTTP API",
    "endpoints": [
      "GET  /api/status",
      "GET  /api/ocr/get_options",
      "POST /api/ocr   JSON{base64,options} 或 multipart",
      "POST /api/qr    JSON{base64|path} 或 multipart 条码/二维码",
      "GET  /api/asr/models",
      "POST /api/asr   JSON{base64|path, model?, lang?, itn?, postprocess?}",
      "GET  /api/tts/models",
      "POST /api/tts   JSON{text, engine?, model?, voice?, speaker_id?, speed?, volume?}",
      "POST /api/itn   JSON{text}  WeText+规则后处理",
      "POST /api/translate  JSON{items[],src?,dst?}  LLM 批量翻译",
      "GET  /api/face/models",
      "POST /api/face  JSON{base64|base64_b|path} 或 multipart"
    ]
  }
}
```

---

## 5. GET `/api/status` · `/api/health`

健康检查与能力探测。

**响应 `data` 字段：**

| 字段 | 类型 | 说明 |
|------|------|------|
| `app` | string | `"ScreenKit"` |
| `http_enabled` | bool | 配置中是否启用 HTTP |
| `http_ocr` | bool | OCR 与条码路径是否启用 |
| `http_tts` | bool | TTS 路径是否启用 |
| `http_asr` | bool | ASR 与 ITN 路径是否启用 |
| `http_translate` | bool | 翻译路径是否启用 |
| `http_chat` | bool | 对话路径是否启用 |
| `http_face` | bool | 人脸路径是否启用 |
| `ocr_engine` | bool | OCR 运行器是否可用 |
| `asr_engine` | bool | ASR 引擎是否注入 |
| `tts_engine` | bool | TTS（Sherpa）引擎是否注入 |
| `tts_sapi` | bool | 本机 SAPI 发音人是否可用 |
| `tts_winrt` | bool | Windows（WinRT / OneCore）语音是否可用 |
| `tts_edge` | bool | Edge 在线自然语音功能是否已内置（实际合成仍取决于网络） |
| `asr_models` | int | 扫描到的 ASR 模型数量 |
| `tts_models` | int | 扫描到的 Sherpa TTS 模型数量 |
| `face_ready` | bool | `facemodels` 是否已有检测+识别模型 |
| `face_models` | int | 扫描到的人脸 ONNX 数量 |
| `barcode` | bool | 条码/二维码接口已内置（无需额外模型）。实际识别还需要 `ZXing.dll`，发布包不含它 |
| `itn` | bool | WeText ITN 是否可用 |
| `itn_error` | string | ITN 不可用时的原因 |
| `llm_translate` | bool | 是否已配置可用的翻译 LLM |
| `llm_chat` | bool | 是否已配置可用的对话 LLM |

**示例：**

```bash
curl -s "http://127.0.0.1:1224/api/status"

---

## 5.1 GET/POST `/api/toast`

在主显示器工作区底部显示一条 Toast。不抢焦点，显示期间可继续操作。时长限制在 800～8000 毫秒。

**GET**

```
/api/toast?text=已复制&ms=1900
```

**POST** `application/json`

| 字段 | 类型 | 说明 |
|------|------|------|
| `text` | string | 要显示的文字。也可用 `message` |
| `ms` | number | 显示毫秒，默认 1900 |

```json
{ "text": "已复制", "ms": 1900 }
```

**响应 `data`：** `text`、`ms`（已按 800～8000 收束）。`text` 为空时返回 802。

```bash
curl -s -X POST "http://127.0.0.1:1224/api/toast" -H "Content-Type: application/json" -d "{\"text\":\"已复制\",\"ms\":1900}"
```

命令行不经过 HTTP，直接弹出同一条 Toast：

```bash
ScreenKit --toast "已复制" --ms 1900
```
```

---

## 5.2 GET/POST `/api/zhconv`

用系统 `LCMapStringEx` 做简体/繁体。繁体按 `zh-TW`。默认转为繁体。GET 查询串按 UTF-8 百分号编码；服务端按 UTF-8 解码。

**GET**

```
/api/zhconv?text=软件&to=trad
```

**POST** `application/json`

| 字段 | 类型 | 说明 |
|------|------|------|
| `text` | string | 原文。也可用 `message` |
| `to` | string | `trad`（默认）或 `simp` |

```json
{ "text": "软件", "to": "trad" }
```

**响应 `data`：** `text`、`to`。`text` 为空返回 802。

```bash
ScreenKit --zhconv "软件"
ScreenKit --zhconv "國發" --simp
```

---

## 5.3 GET/POST `/api/calendar`

用 `Windows.Globalization.Calendar` 把公历换成指定历法。农历返回干支、正月/闰月和初几。日期按本地中午换算。缺省日期是今天，缺省历法是农历。

**GET**

```
/api/calendar?date=2024-02-10&cal=lunar
```

**POST** `application/json`

| 字段 | 类型 | 说明 |
|------|------|------|
| `date` | string | `yyyy-MM-dd`。省略为今天 |
| `cal` | string | `lunar`（默认）、`gregorian`、`jp`、`jplunar`、`tw`、`ko`、`vilunar`、`he`、`hijri`、`umalqura`、`fa`、`th`、`julian` |

```json
{ "date": "2024-02-10", "cal": "lunar" }
```

**响应 `data`：** `cal`、`gregorian`、`ganzhi`、`era`、`era_num`、`year`、`year_num`、`month`、`month_num`、`month_count`、`day`、`day_num`、`week`、`leap`。日期或历法无法识别时返回 802。

```bash
ScreenKit --calendar 2024-02-10 --cal lunar
```

---

## 5.4 GET/POST `/api/jpyomi`

用系统 `JapanesePhoneticAnalyzer` 给日文句子注音。需要本机日语语言支持。Windows 没有同等的汉语拼音接口。

**GET**

```
/api/jpyomi?text=東京は晴れです
```

**POST** `application/json`

| 字段 | 类型 | 说明 |
|------|------|------|
| `text` | string | 日文。也可用 `message` |
| `mono` | bool | `true` 为逐字注音，默认按词组 |

```json
{ "text": "東京は晴れです", "mono": false }
```

**响应 `data`：** `ruby`（原文夹读音）、`yomi`（读音）、`mono`。`text` 为空返回 802。

```bash
ScreenKit --jpyomi "東京は晴れです"
ScreenKit --jpyomi "東京" --mono
```

---

## 5.5 GET/POST `/api/text`

文本编解码。不随模块开关关闭。`text` 可以为空。未知 `op` 返回 802。Base64、十六进制或 JSON 不合法返回 500。

| 字段 | 类型 | 说明 |
|------|------|------|
| `text` | string | 原文。也可用 `message` |
| `op` | string | `b64enc` `b64dec` `urlenc` `urldec` `utf8hex` `utf8unhex` `gbkhex` `gbkunhex` `uesc` `uunesc` `upper` `lower` `collapse` `dropempty` `json` `stats` |

```json
{ "text": "hi", "op": "b64enc" }
```

**响应 `data`：** `text` 是结果（上例为 `aGk=`）。`op` 回显操作。`stats` 把简短的多行统计放进 `text`。

---

## 5.6 GET/POST `/api/qrmake` · POST `/api/qrscan`

生成或识别二维码和条码。不随模块开关关闭。生成走 ZXing 写码，识别不跑 OCR。

**生成** `GET/POST /api/qrmake`

| 字段 | 类型 | 说明 |
|------|------|------|
| `text` | string | 内容。也可用 `message` |
| `format` | string | `qr`（默认）、`datamatrix`、`code128`、`code39`、`ean13`、`ean8`、`upca` |
| `encoding` | string | `utf8`（默认）、`gbk`、`hex`。一维码按字符写入 |

```json
{ "text": "hello", "format": "qr", "encoding": "utf8" }
```

**响应 `data`：** `png` 是 PNG 的 Base64，另有 `format`、`encoding`。空内容返回 802。

**识别** `POST /api/qrscan`

图片用 `base64`、`path` 或 multipart，和 `POST /api/qr` 相同。`format` 为 `dict`（默认，带坐标）或 `text`。没有码时 `code` 为 101。

---

## 6. OCR

### 6.0 GET `/api/ocr/models`

列出和主窗口一样的识别引擎。`packs[]` 第一项是 Windows 系统 OCR（`id=winocr`，`models` 为已安装语言的 `id` BCP-47 和显示名），后面是 `ocrmodels` 里的 ONNX 包（`models[].id` 是变体标题）。`current` 是主窗口当前的 `pack`、`language`、`device`（`cpu` / `gpu` / `intel`）。

`POST /api/ocr` 的 `options` 可加 `ocr.pack`（包 Id）。指定后只在该包里匹配 `ocr.language`。

### 6.1 GET `/api/ocr/get_options`

返回参数描述对象：每项含 `title` / `toolTip` / `default` / 可选 `optionsList`。

| 键 | 含义 | 默认 |
|----|------|------|
| `ocr.angle` | 纠正文本方向（方向分类 cls） | `true` |
| `ocr.maxSideLen` | 检测边长上限 | `1024` |
| `ocr.engine` | 引擎：空=跟主窗，`winocr`=Windows 系统 OCR，`onnx`=ONNX 模型 | `""` |
| `ocr.language` | ONNX 变体标题，或系统 OCR 的语言 | `""`（用主窗当前模型/语言） |
| `ocr.device` | 设备：`cpu` / `gpu` / `intel` | `cpu` |
| `ocr.barcode` | 同时识别条码/二维码 | `false`（仅条码请用 `POST /api/qr`） |
| `tbpu.parser` | 排版方案（兼容保留） | `multi_line` |
| `data.format` | 返回格式：`dict` 或 `text` | `dict` |

### 6.2 POST `/api/ocr`

识别图片文字。支持：

1. **JSON**：`base64` + 可选 `options`
2. **multipart/form-data**：文件字段 + 可选 options

#### JSON 请求体

```json
{
  "base64": "<图片 base64，可带 data:image/png;base64, 前缀>",
  "options": {
    "ocr.angle": true,
    "ocr.maxSideLen": 1600,
    "ocr.engine": "winocr",
    "ocr.language": "zh-Hans-CN",
    "ocr.device": "gpu",
    "data.format": "dict"
  }
}
```

| 字段 | 必填 | 说明 |
|------|------|------|
| `base64` | 是 | 图片编码；支持 png/jpg/bmp/webp 等 OpenCV 可解码格式 |
| `options` | 否 | 对象；未给出的项用 get_options 默认值；再与主窗当前模型配置合并 |

**`options` 常用项：**

| 键 | 类型 | 说明 |
|----|------|------|
| `data.format` | string | `dict`：带坐标的行列表；`text`：纯文本（行间 `\n`） |
| `ocr.angle` | bool/string | 是否方向分类 |
| `ocr.maxSideLen` | int | 检测边长，约 320～4096 |
| `ocr.engine` | string | 空=跟主窗。`winocr` / `windows` / `Windows 系统 OCR` 用系统 OCR，不加载 ONNX。`onnx` 在主窗是系统 OCR 时改回第一个 ONNX 包 |
| `ocr.language` | string | ONNX：匹配变体标题（模糊包含）。系统 OCR：BCP-47 或语言名（`zh-Hans-CN`、`en-US`、`简体中文`、`英语`、`韩语`）。本字段设为 `winocr` 也表示用系统 OCR。没有这种已安装语言时失败 |
| `ocr.device` | string | `cpu` / `gpu`（cuda） / `intel`（dml） |
| `ocr.detThresh` | number | 检测阈值（扩展） |
| `ocr.detBoxThresh` | number | 框置信阈值（扩展） |
| `ocr.barcode` | bool/string | `true` 时额外扫条码/二维码；别名：`ocr.qr`、`ocr.codes` |

#### multipart 请求

| 部件名 | 说明 |
|--------|------|
| `file` / `image` / `img` / `upload` / `pic` 或带文件名的 part | 图片二进制 |
| `base64` / `image_base64` | 也可传 base64 文本 |
| `options` | JSON 对象字符串，或 `key=value&...` |
| 任意 `ocr.xxx` / `data.format` 字段 | 单项参数 |

#### 成功响应（`data.format = dict`）

```json
{
  "code": 100,
  "data": [
    {
      "text": "你好",
      "score": 0.98,
      "box": [[10.0, 20.0], [100.0, 20.0], [100.0, 50.0], [10.0, 50.0]],
      "end": "\n"
    }
  ],
  "time": 45,
  "timestamp": 1710000000
}
```

| 字段 | 说明 |
|------|------|
| `text` | 行文本 |
| `score` | 置信度 |
| `box` | 四点坐标 `[[x,y],…]`，**原图像素**（检测会限制边长，返回前已按缩放比还原） |
| `end` | 行尾分隔，固定 `"\n"` |

#### 启用条码（`ocr.barcode = true`）

开启后响应顶层增加 `barcodes` 数组。OCR 行仍在 `data`。若无文字但有条码，仍返回 `code=100`（dict 时 `data` 可为 `[]`；text 时 `data` 为 `[类型] 内容` 文本）。

```json
{
  "code": 100,
  "data": [],
  "barcodes": [
    {
      "type": "QR_CODE",
      "text": "https://example.com",
      "box": [[12.0, 40.0], [180.0, 40.0], [180.0, 210.0], [12.0, 210.0]]
    },
    {
      "type": "EAN_13",
      "text": "6901234567892",
      "box": [[20.0, 300.0], [220.0, 300.0], [220.0, 340.0], [20.0, 340.0]]
    }
  ],
  "time": 55,
  "timestamp": 1710000000
}
```

| 字段 | 说明 |
|------|------|
| `type` | 码制，如 `QR_CODE`、`EAN_13`、`CODE_128`、`DATA_MATRIX`、`PDF_417`、`AZTEC`、`UPC_A`、`CODE_39` 等 |
| `text` | 解码内容 |
| `box` | 角点 `[[x,y],…]`（图像像素） |

支持格式（ZXingCpp / zxing-cpp 原生 + 轻量 OpenCV QR 补充）：QR、Aztec、Data Matrix、PDF417、EAN-8/13、UPC-A/E、Code 39/93/128、Codabar、ITF 等。

#### 成功响应（`data.format = text`）

```json
{
  "code": 100,
  "data": "第一行\n第二行",
  "time": 40,
  "timestamp": 1710000000
}
```

开启 `ocr.barcode` 时同样返回 `barcodes`；仅条码无文字时 `data` 为 `[QR_CODE] …` 形式。

#### 未检出文字

```json
{
  "code": 101,
  "data": "未检测到文字",
  "time": 30,
  "timestamp": 1710000000
}
```

开启 `ocr.barcode` 且均未检出：`data` 为「未检测到文字或条码」，`barcodes` 为 `[]`。

#### 调用示例

**PowerShell（JSON base64）：**

```powershell
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes("D:\sample.png"))
$body = @{
  base64 = $b64
  options = @{
    "data.format" = "text"
    "ocr.maxSideLen" = 1600
  }
} | ConvertTo-Json -Depth 5
Invoke-RestMethod -Uri "http://127.0.0.1:1224/api/ocr" -Method Post `
  -ContentType "application/json; charset=utf-8" -Body $body
```

**curl（multipart）：**

```bash
curl -s -X POST "http://127.0.0.1:1224/api/ocr" \
  -F "file=@sample.png" \
  -F 'options={"data.format":"dict","ocr.angle":true}'
```

**Python：**

```python
import base64, json, urllib.request

with open("sample.png", "rb") as f:
    b64 = base64.b64encode(f.read()).decode("ascii")

req = urllib.request.Request(
    "http://127.0.0.1:1224/api/ocr",
    data=json.dumps({
        "base64": b64,
        "options": {"data.format": "text", "ocr.maxSideLen": 1600},
    }).encode("utf-8"),
    headers={"Content-Type": "application/json"},
    method="POST",
)
print(urllib.request.urlopen(req).read().decode("utf-8"))
```

---

## 7. ASR（语音识别）

可使用 `asrmodels` 下的离线模型，或系统已安装的 Windows 语音识别器（`System.Speech`）。Sherpa 流式模型仅供实时路径使用，**HTTP 使用离线识别**。

### 7.1 GET `/api/asr/models`

```json
{
  "code": 100,
  "data": [
    {
      "name": "sensevoice-small",
      "type": "SenseVoice",
      "streaming": false,
      "sample_rate": 16000,
      "culture": ""
    }
  ],
  "count": 1
}
```

系统识别器的 `type` 为 `Windows`，其它为 `SenseVoice` / `Paraformer` / `Transducer` / `Whisper` / `ZipformerCtc`。Windows 项还会返回 BCP-47 `culture`。是否流式看 `streaming`。

### 7.2 POST `/api/asr`

**请求 JSON：**

| 字段 | 必填 | 说明 |
|------|------|------|
| `base64` | 二选一 | 音频 base64（wav/mp3/flac/webm 等，由服务端解码） |
| `path` | 二选一 | **服务端本机**音频绝对路径（仅本机调试） |
| `filename` | 否 | 辅助猜扩展名，如 `a.wav` |
| `model` / `asr_model` | 否 | 模型显示名，可选 Sherpa 模型或 Windows 识别器；默认用配置/第一个离线项 |
| `lang` | 否 | 默认 `auto`（SenseVoice：zh/en/ja/ko/yue 等） |
| `itn` | 否 | 模型 ITN，默认 true |
| `postprocess` | 否 | 是否再跑规则后处理，默认 true；`false` 关闭 |
| `device` / `compute` | 否 | `auto` / `gpu` / `cpu` / `igpu`；Windows ASR 忽略此项 |

**成功响应：**

```json
{
  "code": 100,
  "data": {
    "text": "识别出的文字",
    "model": "sensevoice-small",
    "provider": "CUDA",
    "sample_rate": 16000,
    "audio_sec": 3.2,
    "load_ms": 120,
    "recognize_ms": 80,
    "postprocess": true
  },
  "time": 250,
  "timestamp": 1710000000
}
```

**示例：**

```bash
# 本机路径（服务端能读到的文件）
curl -s -X POST "http://127.0.0.1:1224/api/asr" \
  -H "Content-Type: application/json" \
  -d "{\"path\":\"D:/audio/test.wav\",\"lang\":\"zh\"}"
```

---

## 8. TTS（语音合成）

四种引擎：**Sherpa**（`ttsmodels` 下 ONNX 包）、**SAPI**（经典 `System.Speech`，含经 `x86host.exe` 的 32 位音）、**Windows**（`engine=winrt`，WinRT / OneCore 神经语音）、**Edge 在线**（`engine=edge`，无需模型/API Key但必须联网）。省略 `engine` 时：有 Sherpa 模型则走 Sherpa；否则有 Windows 语音就用它，没有再用 Edge 在线。

### 8.1 GET `/api/tts/engines` · GET `/api/tts/models`

`GET /api/tts/engines` 只返回四个引擎，不扫描模型和发音人。

| engine | name |
|--------|------|
| `sherpa` | Sherpa |
| `sapi` | SAPI |
| `winrt` | Windows |
| `edge` | Edge Online |

`GET /api/tts/models` 列出模型和发音人。不带参数时扫描全部，并记住这次结果。之后同样的全量查询直接返回缓存，响应里 `cached` 为 `true`。`refresh=1`（或 `true`）忽略缓存并重新扫描；全量重新扫描会换掉缓存。

`engine` 只返回这一引擎：`sherpa`、`sapi`、`winrt`（也可写 `windows`）、`edge`。已有全量缓存时从缓存里筛，不再扫描。未知 `engine` 返回 802。

```json
{
  "code": 100,
  "data": [
    {
      "name": "vits-zh",
      "engine": "sherpa",
      "type": "Vits",
      "speakers": [
        { "id": 0, "name": "speaker0", "label": "speaker0", "lang": "zh", "gender": "" }
      ]
    },
    {
      "name": "SAPI",
      "engine": "sapi",
      "type": "Sapi",
      "speakers": [
        { "id": 0, "name": "Microsoft Huihui Desktop", "label": "Microsoft Huihui Desktop · zh-CN · 女", "lang": "zh", "gender": "female", "key": "sapi:Microsoft Huihui Desktop" }
      ]
    },
    {
      "name": "Windows",
      "engine": "winrt",
      "type": "WinRt",
      "speakers": [
        { "id": 0, "name": "{voice-id}", "label": "Microsoft Hanhan · zh-TW · 女", "lang": "zh", "gender": "female", "key": "winrt:{voice-id}" }
      ]
    },
    {
      "name": "Edge Online",
      "engine": "edge",
      "type": "Edge",
      "speakers": [
        { "id": 0, "name": "ko-KR-SunHiNeural", "label": "SunHi · ko-KR · 女", "lang": "ko", "gender": "female", "key": "edge:ko-KR-SunHiNeural" }
      ]
    }
  ],
  "count": 4,
  "cached": false
}
```

`label` 是和桌面下拉相同的可读名称。`name` 仍是合成用的原始名；Windows 语音的 `name` 是语音 Id。Sherpa 每个模型最多列出 64 个 speaker。SAPI / Windows 列出全部已装发音人，Edge 在线目录从 Microsoft 获取。仅 x86 可见的 SAPI 音 `key` 前缀为 `sapi-x86:`。

### 8.2 POST `/api/tts`

| 字段 | 必填 | 说明 |
|------|------|------|
| `text` | 是 | 待合成文本，最长 20000 字 |
| `engine` | 否 | `sherpa` / `sapi` / `winrt` / `edge`（另有别名）。空则从 `model`/`voice` 推断；有 Sherpa 模型走 Sherpa，否则 Windows 语音或 Edge 在线 |
| `model` | 否 | Sherpa 显示名，或 `SAPI` / `Windows` / `Edge Online` |
| `voice` / `speaker` | 否 | 系统/在线发音人名或 `key`（`sapi:…` / `sapi-x86:…` / `winrt:…` / `edge:…`） |
| `speaker_id` / `sid` | 否 | 发音人序号（Sherpa 的 sid，或该引擎列表下标），默认 0 |
| `speed` | 否 | 语速 0.5～2.0，默认 1.0 |
| `volume` | 否 | 0～100，默认 100（SAPI / Windows / Edge；Sherpa 忽略） |
| `device` / `compute` | 否 | 仅 Sherpa：`auto` / `gpu` / `cpu` / `igpu` |

**成功响应：**

```json
{
  "code": 100,
  "data": {
    "format": "wav",
    "sample_rate": 22050,
    "samples": 44100,
    "wav_base64": "<整段 WAV 文件的 base64>",
    "engine": "winrt",
    "model": "Windows",
    "voice": "winrt:{voice-id}",
    "speaker_id": 0,
    "provider": "WinRT"
  },
  "time": 300,
  "timestamp": 1710000000
}
```

`provider` 为 Sherpa 的 EP（`CPU` / CUDA 等），或 `SAPI` / `SAPI x86` / `WinRT` / `Edge Online`。解码 `wav_base64` 即为标准 WAV 文件字节。

```python
import base64, json, urllib.request

req = urllib.request.Request(
    "http://127.0.0.1:1224/api/tts",
    data=json.dumps({"text": "你好，世界", "engine": "winrt", "speed": 1.0}).encode("utf-8"),
    headers={"Content-Type": "application/json"},
    method="POST",
)
data = json.loads(urllib.request.urlopen(req).read().decode("utf-8"))
open("out.wav", "wb").write(base64.b64decode(data["data"]["wav_base64"]))
```

命令行：`ScreenKit --test-http-tts` 在本机环回拉起服务，校验 SAPI / Windows / Edge 的 WAV。

---

## 9. POST `/api/itn`

对文本做逆文本归一化（WeText，不可用时仍可能跑规则后处理）。

**请求：**

```json
{ "text": "二零二六年七月二十五日" }
```

**响应：**

```json
{
  "code": 100,
  "data": {
    "text": "2026年7月25日",
    "input": "二零二六年七月二十五日",
    "wetext": true
  },
  "time": 5,
  "timestamp": 1710000000
}
```

| 字段 | 说明 |
|------|------|
| `text` | 处理后文本 |
| `input` | 原始输入 |
| `wetext` | WeText 可执行文件/资源是否可用 |

---

## 10. POST `/api/translate` · `/api/translate/batch`

LLM 批量翻译（按 `translate_llm_batch` 分组，默认 8 条一组，缺号再逐条补）。**不限制一次请求的条数**。需在参数设置配置 `[[llm]]`，并指定 `translate_llm`（或请求里传 `llm`）。别名 `/api/translate/batch` 同一处理。条数很多时耗时按组累加（每组最长约 90 秒）。

**请求：**

```json
{
  "src": "zh",
  "dst": "en",
  "items": [
    "单击目标窗口 · Esc 取消",
    "按 Ctrl+Alt+V 开始听写"
  ]
}
```

| 字段 | 说明 |
|------|------|
| `items` / `texts` | 字符串数组，条数不限。元素也可以是 `{"text":"…"}` |
| `text` | 单条时可用，等价于 `items` 只有一项 |
| `src` / `dst` | 语言代码（`zh` / `en` / `ja` / `ko` / `fr` / `de` / `es` / `ru` / `ar` / `th` / `cht` 等）。可省略，则按首条非空文本做中英互译自动检测 |
| `dir` | 可选，如 `zh-en`，与 `src`/`dst` 二选一 |
| `chunk` | 每批送给模型的条数。不传则用 `translate_llm_batch`（默认 8），范围 1–64 |
| `llm` | 可选，`[[llm]]` 显示名称或模型 id；默认 `translate_llm`，再否则列表第一项 |

**响应：**

```json
{
  "code": 100,
  "data": {
    "src": "zh",
    "dst": "en",
    "model": "Qwen/Qwen3.5-4B",
    "llm": "硅基 Qwen3.5-4B",
    "chunk": 8,
    "count": 2,
    "miss": 0,
    "items": [
      { "i": 1, "text": "单击目标窗口 · Esc 取消", "out": "Click the target window · Esc to cancel" },
      { "i": 2, "text": "按 Ctrl+Alt+V 开始听写", "out": "Press Ctrl+Alt+V to start dictation." }
    ]
  },
  "time": 2100,
  "timestamp": 1710000000
}
```

`GET /api/status` 的 `llm_translate` 为 true 时表示已配置可用的翻译 LLM。

```python
import json, urllib.request
req = urllib.request.Request(
    "http://127.0.0.1:1224/api/translate",
    data=json.dumps({
        "src": "zh", "dst": "en",
        "items": ["单击目标窗口 · Esc 取消", "增加文本语音合成"],
    }).encode("utf-8"),
    headers={"Content-Type": "application/json"},
    method="POST",
)
print(json.loads(urllib.request.urlopen(req).read().decode("utf-8")))
```

---

## 11. POST `/api/chat`

LLM 多轮对话。支持**文本**或**语音**用户消息；始终返回文本回复；仅当请求 **`tts`: true** 时在响应中附带 WAV 的 `wav_base64`（不看配置、也不看其它字段名）。需已配置 `[[llm]]`（`chat_llm` 或请求里 `llm`）。

**请求（文本）：**

```json
{
  "text": "用一句话介绍你自己",
  "messages": [
    { "role": "user", "content": "你好" },
    { "role": "assistant", "content": "你好，有什么可以帮你？" }
  ],
  "tts": true,
  "engine": "sapi",
  "agent": false,
  "llm": ""
}
```

（`tts` 省略或 `false` 时只返回文本。）

**请求（语音消息）：** 不传 `text`，传音频 `base64` 或本机 `path`（与 `/api/asr` 相同），先 ASR 再对话。

```json
{
  "base64": "<wav/mp3/... base64>",
  "lang": "zh",
  "asr_model": "",
  "tts": true,
  "engine": "winrt"
}
```

| 字段 | 必填 | 说明 |
|------|------|------|
| `text` / `message` / `content` | 文本与音频二选一 | 本轮用户文本，最长 8000 字 |
| `base64` / `path` | 文本与音频二选一 | 用户语音；有文本时优先文本 |
| `messages` | 否 | 此前历史（仅 `user`/`assistant`）；**不含**本轮 user（本轮用 `text`/ASR） |
| `tts` | 否 | **仅此字段**控制是否合成语音并返回 `wav_base64`；`true` 才合成，默认/`false` 不合成 |
| `agent` | 否 | 是否走对话 Agent（工具）；默认取配置 `chat_agent` |
| `llm` / `chat_llm` | 否 | `[[llm]]` 显示名或模型 id；默认 `chat_llm` |
| `engine` / `voice` / `speaker_id` / `speed` / `volume` / `tts_model` | 否 | TTS 参数，语义同 `/api/tts` |
| `asr_model` / `lang` / `itn` / `postprocess` / `device` | 否 | 语音入时的 ASR 参数，语义同 `/api/asr` |

**成功响应：**

```json
{
  "code": 100,
  "data": {
    "text": "我是 ScreenKit 里的助手。",
    "reply": "我是 ScreenKit 里的助手。",
    "user_text": "用一句话介绍你自己",
    "llm": "硅基 Qwen",
    "model": "Qwen/Qwen3.5-4B",
    "agent": false,
    "asr_ms": 0,
    "llm_ms": 800,
    "tts_ms": 200,
    "format": "wav",
    "sample_rate": 22050,
    "wav_base64": "<整段 WAV base64>",
    "engine": "sapi",
    "total_ms": 1000
  },
  "time": 1000,
  "timestamp": 1710000000
}
```

TTS 失败时仍返回文本（`code=100`），并带 `tts_error` 说明。业务码：`960` 未配置 LLM，`961` 缺输入，`962` 语音识别失败，`963` 对话失败。

```bash
curl -s -X POST "http://127.0.0.1:1224/api/chat" \
  -H "Content-Type: application/json" \
  -d "{\"text\":\"你好\",\"tts\":true,\"engine\":\"sapi\"}"
```

---

## 11. 人脸

依赖程序目录 `facemodels/` 中有检测+识别 ONNX。可在 **帮助 → 安装功能** 下载 InsightFace **buffalo_l**。别名：`POST /api/face/compare`、`POST /api/face/extract`（同一处理）。

### 11.1 GET `/api/face/models`

```json
{
  "code": 100,
  "data": {
    "root": ".../facemodels",
    "ready": true,
    "det": ["det_10g.onnx"],
    "rec": ["w600k_r50.onnx"],
    "landmark": ["2d106det.onnx", "1k3d68.onnx"],
    "attr": ["genderage.onnx"]
  },
  "count": 5
}
```

### 11.2 POST `/api/face`

一张图：检测最大脸并提取特征。两张图：两侧特征余弦相似度并判定是否同一人。

**JSON：**

| 字段 | 必填 | 说明 |
|------|------|------|
| `base64` / `image` / `path` | 至少一张 | 第一张图（base64 或服务端本机路径） |
| `base64_b` / `image_b` / `path_b` | 比对时 | 第二张图 |
| `det` / `reg` | 否 | 检测/识别文件名（模糊匹配；默认配置或目录中第一个） |
| `threshold` | 否 | 比对阈值，默认配置（约 0.5） |
| `device` / `compute` | 否 | `auto` / `gpu` / `cpu` / `igpu` |
| `attr` / `genderage` | 否 | 是否跑性别年龄（有 `genderage.onnx` 时默认 true） |
| `include_feature` | 否 | `true` 时在结果中附带特征向量 |

**multipart：** `file` + `file2`（或 `image` / `image_b`），可选 `threshold` 等字段。

**单图成功：**

```json
{
  "code": 100,
  "data": {
    "faces": 1,
    "det": "det_10g.onnx",
    "reg": "w600k_r50.onnx",
    "provider": "CUDA",
    "face": {
      "score": 0.91,
      "box": [80.0, 40.0, 220.0, 210.0],
      "landmarks5": [[x, y], "..."],
      "gender": "女",
      "age": 22
    }
  },
  "time": 80
}
```

**两图比对成功：**

```json
{
  "code": 100,
  "data": {
    "similarity": 0.7234,
    "match": true,
    "threshold": 0.5,
    "det": "det_10g.onnx",
    "reg": "w600k_r50.onnx",
    "provider": "CUDA",
    "left": { "score": 0.91, "box": [80, 40, 220, 210], "gender": "女", "age": 22 },
    "right": { "score": 0.88, "box": [70, 30, 200, 200], "gender": "男", "age": 26 }
  },
  "time": 150
}
```

未检出人脸时 `code=101`。

```bash
curl -s -X POST "http://127.0.0.1:1224/api/face" \
  -F "file=@left.jpg" -F "file2=@right.jpg" -F "threshold=0.5"
```

---

## 12. 条码 / 二维码

只扫条码，**不跑 OCR**。与结果区 **条码** Tab 同一套 ZXingCpp 流程（QR、Aztec、Data Matrix、PDF417、EAN-8/13、UPC-A/E、Code 39/93/128、Codabar、ITF，外加轻量 OpenCV QR 补充）。`ZXing.dll` 不在发布包里，用 **帮助 → 安装功能** 下载。没有这个文件时返回代码 950。别名：`POST /api/barcode`、`POST /api/barcodes`（同一处理）。OCR 顺带扫码仍用 `POST /api/ocr` 的 `options.ocr.barcode`。

### 12.1 POST `/api/qr`

**JSON：**

```json
{
  "base64": "<图片 base64>",
  "path": "D:\\sample.png",
  "format": "dict"
}
```

| 字段 | 必填 | 说明 |
|------|------|------|
| `base64` | 二选一 | 图片字节（png/jpg/bmp/webp 等） |
| `path` | 二选一 | 服务端本地文件路径（与 ASR/人脸相同注意点） |
| `image` / `img` | — | `base64` 的别名 |
| `format` | 否 | `dict`（默认）或 `text`；也可用 `data.format` / `options.data.format` |

**multipart：** 文件字段 `file` / `image` / `img` / `upload`，与 OCR 相同。

**成功（`format=dict`）：**

```json
{
  "code": 100,
  "data": [
    {
      "type": "QRCode",
      "text": "https://example.com",
      "box": [[12.0, 40.0], [180.0, 40.0], [180.0, 210.0], [12.0, 210.0]]
    }
  ],
  "count": 1,
  "time": 18,
  "timestamp": 1710000000
}
```

`type` / `text` / `box` 与 `POST /api/ocr` 的 `barcodes[]` 一致。

**成功（`format=text`）：** `data` 为 `[QRCode] https://example.com` 这种多行文本。

**未检出：** `code=101`，`count=0`；`data` 为「未检测到条码或二维码」（dict）或 `""`（text）。识别异常：`950`。

```bash
curl -s -X POST "http://127.0.0.1:1224/api/qr" -F "file=@code.png"
```

```python
import json, base64, urllib.request
b64 = base64.b64encode(open("code.png", "rb").read()).decode("ascii")
req = urllib.request.Request(
    "http://127.0.0.1:1224/api/qr",
    data=json.dumps({"base64": b64}).encode("utf-8"),
    headers={"Content-Type": "application/json"},
    method="POST",
)
print(json.loads(urllib.request.urlopen(req).read().decode("utf-8")))
```

---

## 13. 与主窗口的关系

| 行为 | 说明 |
|------|------|
| OCR 模型 | 未指定 `ocr.engine` 和 `ocr.language` 时跟主窗。`ocr.engine=winocr`（或 `ocr.language=winocr`）走 Windows 系统 OCR，语言用 `ocr.language`，缺省为主窗 `win_ocr_langs`，不加载 ONNX。主窗已是系统 OCR 时，`ocr.engine=onnx` 或一个 ONNX 变体标题改回模型包。边长仍用 `ocr.maxSideLen`，缺省跟主窗 `det_limit` |
| 设备 | 可用 `ocr.device` 覆盖；否则用主窗设备配置 |
| 服务模式 | `service_mode = true` 时启动预热，引擎常驻，适合频繁 API 调用；不按空闲卸载 |
| 空闲卸载 | `onnx_unload_min`（默认 5）分钟内未使用则卸载 OCR、翻译、人脸、语音识别和合成。`0` 表示不自动卸载。服务模式开启时忽略 |
| 改参 | 主窗改模型/设备后会 Invalidate 引擎，下次请求重新加载 |

---

## 14. 安全建议

1. 默认 `http_lan = true`，监听所有网卡。只要本机使用时设 `http_lan = false`（只听 `127.0.0.1`）。无防火墙与鉴权时不要暴露到不可信网络。
2. **无鉴权、无 HTTPS**，仅信任本机或可信局域网。
3. `POST /api/asr`、`POST /api/face`、`POST /api/qr` 的 `path` / `path_b` 会读服务端本地文件，勿对不可信来源开放。
4. 大图 / 长音频会占用 CPU/GPU 与内存；注意并发（当前实现按请求并行 `Task.Run`，引擎侧有锁）。

---

## 15. PC 文件传输（局域网，需配对）

与 HTTP API **共用同一端口**（默认 **1224**）。在 **参数设置 → 接口 → PC 文件传输** 启用。手机和网页从局域网访问时，须同时勾选 **允许局域网访问**（`http_lan = true`）。UDP 发现 **17531**。文件仅限程序旁 `sendfile/`。

`GET /apk` **无需配对**（给未装 App 的手机扫码下载）。`GET /files`、`GET /m` 为网页文件管理（电脑 / 手机各一套）。`GET /` 是工具页，不是文件管理。`GET /f/<相对路径>` **无需登录**即可下载。网页上传/列出/删除等需登录（Cookie `sk_web` 或 `X-Web-Token`）。其它手机接口配对后请求头：`X-Device-Id` + `Authorization: Bearer <token>`。

发现：向 UDP 17531 广播 `SCREENKIT_DISCOVER`，电脑应答 JSON `{v,name,httpPort,pcId}`。

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/files` | 电脑版网页（手机 UA 会跳到 `/m`；`?pc=1` 强制电脑版） |
| GET | `/index.html` | 转到 `/files` |
| GET | `/m` | 手机版网页 |
| GET | `/web/app.js` · `/web/d.css` · `/web/m.css` | 页面资源 |
| GET | `/f/<相对路径>` | 公开下载（无需登录；路径须正好落在 sendfile/） |
| GET | `/d?p=` | 公开下载（查询串） |
| GET | `/apk` | 本机 APK 二进制（无需配对；主界面「安装到手机」二维码） |
| POST | `/api/web/login` | body `{password, keep?}` → Cookie `sk_web` + `{token}`。`keep=true` 时 Cookie 30 天（Max-Age 与 Expires）并落盘，重启程序后仍有效 |
| GET/POST | `/api/web/zip?path=` · `?paths=a\|b` | 打包 zip 下载（需登录；目录或文件；多路径用 `\|`） |
| POST | `/api/web/logout` | 清会话 |
| GET | `/api/web/me` | 是否已登录 |
| GET | `/api/web/list?path=` | 列出（需登录） |
| POST | `/api/web/upload?path=` | 原始 body 上传（需登录） |
| POST | `/api/web/mkdir` | body `{path}`（需登录） |
| POST | `/api/web/rename` | body `{from,to}`（需登录） |
| DELETE | `/api/web/delete?path=` | 删除（需登录） |
| GET | `/api/web/text` | 电脑文件同步页当前文本 `{text}`（需登录） |
| POST | `/api/web/text` | body `{text}`，写入该文本框；空字符串为清空（需登录，最长 65536） |
| POST | `/api/sendfile/pair` | body `{id,name}`；首次在电脑弹窗确认 |
| GET | `/api/sendfile/info` | 显示名 / pcId |
| GET | `/api/sendfile/list?path=&deep=` | `deep=1` 递归 |
| GET | `/api/sendfile/download?path=` | 二进制流 |
| POST | `/api/sendfile/upload?path=` | 原始 body |
| POST | `/api/sendfile/mkdir` | body `{path}` |
| DELETE | `/api/sendfile/delete?path=` | 文件或目录 |
| POST | `/api/sendfile/text` | body `{text}` → 电脑「文件同步」Tab 文本列表 |
| GET | `/api/sendfile/text?since=` | 电脑发给该手机的消息 |
| GET | `/api/sendfile/pull` | 电脑待发给该手机的文件 `{items:[{id,path,rel,name,size}]}`（`path` 用于 download） |
| POST | `/api/sendfile/pulldone` | body `{id}`，确认已收下并删除电脑暂存 |

JSON `code` 100 成功；401/403 未配对；410 路径非法。

安卓应用：[android/README.md](android/README.md)。

---

## 16. 相关

- 程序内：`ScreenKit/Ocr/HttpOcrServer.cs` · `HttpOcrServer.Face.cs` · `HttpOcrServer.Translate.cs` · `HttpOcrServer.Qr.cs`
- 配置：`config.toml`（`http_enabled` / `http_lan` / `http_port` / `service_mode`）
- 总览：[README.zh.md](README.zh.md) · [README.md](README.md)
- 英文版：[HTTP-API.md](HTTP-API.md)
