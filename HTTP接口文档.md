# ScreenKit HTTP 接口文档

**语言：** [中文](HTTP接口文档.md) · [English](HTTP-API.md)

本机 HTTP API：提供 OCR 接口，并扩展 ASR / TTS / ITN / 人脸。

默认仅监听本机；**不要**在未受控网络上暴露该端口。

---

## 1. 启用与地址

在 **参数设置** 中开启 HTTP，或编辑程序目录 `config.toml`：

```toml
http_enabled = true
http_host = "127.0.0.1"
http_port = 1224
```

| 项 | 说明 |
|----|------|
| 基址 | `http://{http_host}:{http_port}` |
| 默认 | `http://127.0.0.1:1224` |
| Content-Type | 请求/响应 JSON 均建议 `application/json; charset=utf-8` |
| CORS | 已允许 `*`，便于本机网页调试 |
| OPTIONS | 支持预检（204） |

状态栏成功时会显示类似：`HTTP API · http://127.0.0.1:1224/api/ocr`。

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
| 101 | OCR 未检测到文字；人脸未检测到人脸 |
| 404 | 未知路径 |
| 800 | 请求解析失败 / 体为空 |
| 801 | 请求为空 |
| 802 | 缺少必要字段（如 base64、text） |
| 803 | 字段类型/长度非法 |
| 804 | options 解释失败 |
| 805 | 方法不允许（应用层提示；同时 HTTP 可能为 405） |
| 806 | base64 解码失败 |
| 900 | 内部错误 |
| 901 | OCR 识别失败 |
| 910+ | ASR 相关 |
| 920+ | TTS 相关 |
| 930+ | 人脸相关（930 无模型，931 识别失败，932 无检测/识别文件） |

---

## 3. 接口一览

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/` · `/api` | API 说明与端点列表 |
| GET | `/api/status` · `/api/health` | 服务与能力状态 |
| GET | `/api/ocr/get_options` | OCR 可选项描述 |
| POST | `/api/ocr` | 图片 OCR |
| GET | `/api/asr/models` | 列出 ASR 模型 |
| POST | `/api/asr` | 语音识别 |
| GET | `/api/tts/models` | 列出 TTS 模型 |
| POST | `/api/tts` | 语音合成（返回 WAV base64） |
| POST | `/api/itn` | 文本逆归一化（WeText + 规则后处理） |
| POST | `/api/translate` · `/api/translate/batch` | LLM 批量翻译（需已配置 `[[llm]]`） |
| GET | `/api/face/models` | 列出人脸 ONNX |
| POST | `/api/face` | 人脸检测 / 特征 / 两图比对 |

路径大小写不敏感；尾部 `/` 可有可无。

---

## 4. GET `/` · `/api`

返回服务名称与端点列表。

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
      "GET  /api/asr/models",
      "POST /api/asr   JSON{base64|path, model?, lang?, itn?, postprocess?}",
      "GET  /api/tts/models",
      "POST /api/tts   JSON{text, model?, speaker_id?, speed?}",
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
| `ocr_engine` | bool | OCR 运行器是否可用 |
| `asr_engine` | bool | ASR 引擎是否注入 |
| `tts_engine` | bool | TTS（Sherpa）引擎是否注入 |
| `asr_models` | int | 扫描到的 ASR 模型数量 |
| `tts_models` | int | 扫描到的 TTS 模型数量 |
| `face_ready` | bool | `facemodels` 是否已有检测+识别模型 |
| `face_models` | int | 扫描到的人脸 ONNX 数量 |
| `itn` | bool | WeText ITN 是否可用 |
| `itn_error` | string | ITN 不可用时的原因 |

**示例：**

```bash
curl -s "http://127.0.0.1:1224/api/status"
```

---

## 6. OCR

### 6.1 GET `/api/ocr/get_options`

返回参数描述对象：每项含 `title` / `toolTip` / `default` / 可选 `optionsList`。

| 键 | 含义 | 默认 |
|----|------|------|
| `ocr.angle` | 纠正文本方向（方向分类 cls） | `true` |
| `ocr.maxSideLen` | 检测边长上限 | `1024` |
| `ocr.language` | 识别语言/模型变体标题 | `""`（用主窗当前模型） |
| `ocr.device` | 设备：`cpu` / `gpu` / `intel` | `cpu` |
| `ocr.barcode` | 同时识别条码/二维码 | `false` |
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
    "ocr.language": "简体中文",
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
| `ocr.language` | string | 匹配模型包变体标题（模糊包含） |
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
| `box` | 四点坐标 `[[x,y],…]`（图像像素） |
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

依赖主程序已加载 ASR 能力（`asrmodels` 目录下有离线模型）。流式/听写模型仅供热键语音输入，**HTTP 只使用离线模型**。

### 7.1 GET `/api/asr/models`

```json
{
  "code": 100,
  "data": [
    {
      "name": "sensevoice-small",
      "type": "SenseVoice",
      "streaming": false,
      "sample_rate": 16000
    }
  ],
  "count": 1
}
```

### 7.2 POST `/api/asr`

**请求 JSON：**

| 字段 | 必填 | 说明 |
|------|------|------|
| `base64` | 二选一 | 音频 base64（wav/mp3/flac/webm 等，由服务端解码） |
| `path` | 二选一 | **服务端本机**音频绝对路径（仅本机调试） |
| `filename` | 否 | 辅助猜扩展名，如 `a.wav` |
| `model` / `asr_model` | 否 | 模型显示名；默认用配置/第一个离线模型 |
| `lang` | 否 | 默认 `auto`（SenseVoice：zh/en/ja/ko/yue 等） |
| `itn` | 否 | 模型 ITN，默认 true |
| `postprocess` | 否 | 是否再跑规则后处理，默认 true；`false` 关闭 |
| `device` / `compute` | 否 | `auto` / `gpu` / `cpu` / `igpu` |

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

当前 HTTP 走 **Sherpa** TTS 引擎；需 `ttsmodels` 中有可用模型。

### 8.1 GET `/api/tts/models`

```json
{
  "code": 100,
  "data": [
    {
      "name": "vits-zh",
      "type": "Vits",
      "speakers": [
        { "id": 0, "name": "speaker0", "lang": "zh", "gender": "" }
      ]
    }
  ],
  "count": 1
}
```

每个模型最多列出 64 个 speaker。

### 8.2 POST `/api/tts`

| 字段 | 必填 | 说明 |
|------|------|------|
| `text` | 是 | 待合成文本，最长 20000 字 |
| `model` | 否 | 模型显示名 |
| `speaker_id` / `sid` | 否 | 发音人 id，默认 0 |
| `speed` | 否 | 语速 0.5～2.0，默认 1.0 |
| `device` / `compute` | 否 | `auto` / `gpu` / `cpu` / `igpu` |

**成功响应：**

```json
{
  "code": 100,
  "data": {
    "format": "wav",
    "sample_rate": 22050,
    "samples": 44100,
    "wav_base64": "<整段 WAV 文件的 base64>",
    "model": "vits-zh",
    "speaker_id": 0,
    "provider": "CPU"
  },
  "time": 300,
  "timestamp": 1710000000
}
```

解码 `wav_base64` 即为标准 WAV 文件字节。

```python
import base64, json, urllib.request

req = urllib.request.Request(
    "http://127.0.0.1:1224/api/tts",
    data=json.dumps({"text": "你好，世界", "speed": 1.0}).encode("utf-8"),
    headers={"Content-Type": "application/json"},
    method="POST",
)
data = json.loads(urllib.request.urlopen(req).read().decode("utf-8"))
open("out.wav", "wb").write(base64.b64decode(data["data"]["wav_base64"]))
```

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

LLM 批量翻译（编号一次 5–10 条，缺号再逐条补）。需在参数设置配置 `[[llm]]`，并指定 `translate_llm`（或请求里传 `llm`）。别名 `/api/translate/batch` 同一处理。

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
| `items` / `texts` | 字符串数组，最多 50 条。元素也可以是 `{"text":"…"}` |
| `text` | 单条时可用，等价于 `items` 只有一项 |
| `src` / `dst` | 语言代码 `zh` / `en` / `ja` / `ko`。可省略，则按首条非空文本做中英互译自动检测 |
| `dir` | 可选，如 `zh-en`，与 `src`/`dst` 二选一 |
| `chunk` | 每批送给模型的条数，默认 8，范围 1–10 |
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

## 11. 人脸

依赖程序目录 `facemodels/` 中有检测+识别 ONNX。可在 **工具 → 安装功能** 下载 InsightFace **buffalo_l**。别名：`POST /api/face/compare`、`POST /api/face/extract`（同一处理）。

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

## 11. 与主窗口的关系

| 行为 | 说明 |
|------|------|
| OCR 模型 | 请求未指定 `ocr.language` 时使用主窗当前模型包/变体 |
| 设备 | 可用 `ocr.device` 覆盖；否则用主窗设备配置 |
| 服务模式 | `service_mode = true` 时启动预热，引擎常驻，适合频繁 API 调用 |
| 改参 | 主窗改模型/设备后会 Invalidate 引擎，下次请求重新加载 |

---

## 12. 安全建议

1. 默认只绑 `127.0.0.1`，勿改为 `0.0.0.0` 除非有防火墙与鉴权。
2. **无鉴权、无 HTTPS**，仅信任本机或可信局域网。
3. `POST /api/asr` 与 `POST /api/face` 的 `path` / `path_b` 会读服务端本地文件，勿对不可信来源开放。
4. 大图 / 长音频会占用 CPU/GPU 与内存；注意并发（当前实现按请求并行 `Task.Run`，引擎侧有锁）。

---

## 13. 相关

- 程序内：`ScreenKit/Ocr/HttpOcrServer.cs` · `HttpOcrServer.Face.cs` · `HttpOcrServer.Translate.cs`
- 配置：`config.toml`（`http_enabled` / `http_host` / `http_port` / `service_mode`）
- 总览：[README.zh.md](README.zh.md) · [README.md](README.md)
- 英文版：[HTTP-API.md](HTTP-API.md)
