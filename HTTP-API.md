# ScreenKit HTTP API

**Languages:** [English](HTTP-API.md) · [中文](HTTP接口文档.md)

Local HTTP API: OCR endpoints, plus ASR / TTS / ITN / face extensions.

By default the server binds to loopback only. **Do not** expose this port on untrusted networks.

---

## 1. Enable and base URL

Turn on HTTP under **Settings**, or edit `config.toml` next to the executable:

```toml
http_enabled = true
http_host = "127.0.0.1"
http_port = 1224
```

| Item | Description |
|------|-------------|
| Base URL | `http://{http_host}:{http_port}` |
| Default | `http://127.0.0.1:1224` |
| Content-Type | Prefer `application/json; charset=utf-8` for JSON request/response |
| JSON text | UTF-8 CJK as-is (not `\uXXXX` escapes) |
| CORS | `*` allowed for local web debugging |
| OPTIONS | Preflight supported (204) |

When healthy, the status bar may show something like: `HTTP API · http://127.0.0.1:1224/api/ocr`.

---

## 2. Response conventions

Most endpoints return **HTTP 200** always; success or failure is indicated by the JSON `code` field. Unknown routes may return 404/405; uncaught exceptions may return 500.

### 2.1 Success (with payload)

```json
{
  "code": 100,
  "data": { },
  "time": 12,
  "timestamp": 1710000000
}
```

| Field | Type | Description |
|-------|------|-------------|
| `code` | int | `100` success; `101` OCR found no text; other values are errors |
| `data` | any | Result payload; on failure often an error message string |
| `time` | int | Optional processing time (ms) |
| `timestamp` | long | Optional Unix seconds |

### 2.2 Error example

```json
{
  "code": 802,
  "data": "Missing base64 field in request."
}
```

### 2.3 Common business codes

| code | Meaning (summary) |
|------|-------------------|
| 100 | Success |
| 101 | OCR: no text detected; Face: no face detected |
| 404 | Unknown path |
| 800 | Request parse failure / empty body |
| 801 | Empty request |
| 802 | Missing required field (e.g. base64, text) |
| 803 | Invalid field type/length |
| 804 | Failed to interpret options |
| 805 | Method not allowed (app-level; HTTP may also be 405) |
| 806 | base64 decode failed |
| 900 | Internal error |
| 901 | OCR recognition failed |
| 910+ | ASR-related |
| 920+ | TTS-related |
| 930+ | Face-related (930 no models, 931 failed, 932 missing det/rec files) |

---

## 3. Endpoint overview

| Method | Path | Description |
|--------|------|-------------|
| GET | `/` · `/api` | API name and endpoint list |
| GET | `/api/status` · `/api/health` | Service and capability status |
| GET | `/api/ocr/get_options` | OCR option descriptors |
| POST | `/api/ocr` | Image OCR |
| GET | `/api/asr/models` | List ASR models |
| POST | `/api/asr` | Speech recognition |
| GET | `/api/tts/models` | List TTS models |
| POST | `/api/tts` | Speech synthesis (WAV base64) |
| POST | `/api/itn` | Inverse text normalization (WeText + rules) |
| POST | `/api/translate` · `/api/translate/batch` | LLM batch translate (needs `[[llm]]`) |
| GET | `/api/face/models` | List face ONNX files |
| POST | `/api/face` | Face detect / embedding / compare two images |

Paths are case-insensitive; a trailing `/` is optional.

---

## 4. GET `/` · `/api`

Returns service name and endpoint list.

**Response example:**

```json
{
  "code": 100,
  "data": {
    "name": "ScreenKit HTTP API",
    "endpoints": [
      "GET  /api/status",
      "GET  /api/ocr/get_options",
      "POST /api/ocr   JSON{base64,options} or multipart",
      "GET  /api/asr/models",
      "POST /api/asr   JSON{base64|path, model?, lang?, itn?, postprocess?}",
      "GET  /api/tts/models",
      "POST /api/tts   JSON{text, model?, speaker_id?, speed?}",
      "POST /api/itn   JSON{text}  WeText+rules",
      "POST /api/translate  JSON{items[],src?,dst?}  LLM batch translate",
      "GET  /api/face/models",
      "POST /api/face  JSON{base64|base64_b|path} or multipart"
    ]
  }
}
```

---

## 5. GET `/api/status` · `/api/health`

Health check and capability probe.

**`data` fields:**

| Field | Type | Description |
|-------|------|-------------|
| `app` | string | `"ScreenKit"` |
| `http_enabled` | bool | Whether HTTP is enabled in config |
| `ocr_engine` | bool | OCR runner available |
| `asr_engine` | bool | ASR engine injected |
| `tts_engine` | bool | TTS (Sherpa) engine injected |
| `asr_models` | int | Scanned ASR model count |
| `tts_models` | int | Scanned TTS model count |
| `face_ready` | bool | Whether `facemodels` has det+rec ONNX |
| `face_models` | int | Scanned face ONNX count |
| `itn` | bool | WeText ITN available |
| `itn_error` | string | Reason when ITN is unavailable |

**Example:**

```bash
curl -s "http://127.0.0.1:1224/api/status"
```

---

## 6. OCR

### 6.1 GET `/api/ocr/get_options`

Returns option descriptors: each entry has `title` / `toolTip` / `default` / optional `optionsList`.

| Key | Meaning | Default |
|-----|---------|---------|
| `ocr.angle` | Text orientation (cls) | `true` |
| `ocr.maxSideLen` | Detection side limit | `1024` |
| `ocr.language` | Language / model variant title | `""` (use main window model) |
| `ocr.device` | Device: `cpu` / `gpu` / `intel` | `cpu` |
| `ocr.barcode` | Also scan barcodes / QR codes | `false` |
| `tbpu.parser` | Layout mode (kept for compatibility) | `multi_line` |
| `data.format` | Response format: `dict` or `text` | `dict` |

### 6.2 POST `/api/ocr`

Recognize text in an image. Supported input modes:

1. **JSON**: `base64` + optional `options`
2. **multipart/form-data**: file field + optional options

#### JSON body

```json
{
  "base64": "<image base64; data:image/png;base64, prefix allowed>",
  "options": {
    "ocr.angle": true,
    "ocr.maxSideLen": 1600,
    "ocr.language": "简体中文",
    "ocr.device": "gpu",
    "data.format": "dict"
  }
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `base64` | Yes | Image encoding; formats OpenCV can decode (png/jpg/bmp/webp, …) |
| `options` | No | Object; missing keys use get_options defaults, then merge with main-window model config |

**Common `options` keys:**

| Key | Type | Description |
|-----|------|-------------|
| `data.format` | string | `dict`: line list with boxes; `text`: plain text (`\n` between lines) |
| `ocr.angle` | bool/string | Orientation classification on/off |
| `ocr.maxSideLen` | int | Detection side length, roughly 320–4096 |
| `ocr.language` | string | Match model pack variant title (fuzzy contains) |
| `ocr.device` | string | `cpu` / `gpu` (CUDA) / `intel` (DirectML) |
| `ocr.detThresh` | number | Detection threshold (extension) |
| `ocr.detBoxThresh` | number | Box score threshold (extension) |
| `ocr.barcode` | bool/string | When `true`, also scan barcodes/QR; aliases: `ocr.qr`, `ocr.codes` |

#### Multipart request

| Part name | Description |
|-----------|-------------|
| `file` / `image` / `img` / `upload` / `pic` or a part with a filename | Image binary |
| `base64` / `image_base64` | Base64 text also accepted |
| `options` | JSON object string, or `key=value&...` |
| Any `ocr.xxx` / `data.format` field | Single option |

#### Success (`data.format = dict`)

```json
{
  "code": 100,
  "data": [
    {
      "text": "Hello",
      "score": 0.98,
      "box": [[10.0, 20.0], [100.0, 20.0], [100.0, 50.0], [10.0, 50.0]],
      "end": "\n"
    }
  ],
  "time": 45,
  "timestamp": 1710000000
}
```

| Field | Description |
|-------|-------------|
| `text` | Line text |
| `score` | Confidence |
| `box` | Four-point box `[[x,y],…]` (image pixels) |
| `end` | Line ending; fixed `"\n"` |

#### Success with barcodes (`ocr.barcode = true`)

When barcode scanning is enabled, the response includes a top-level `barcodes` array (always present if the option is on). OCR lines stay in `data`. If there is no OCR text but barcodes are found, `code` is still `100` and `data` may be an empty array (dict) or barcode plain text (text format).

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

| Field | Description |
|-------|-------------|
| `type` | Symbology name from ZXing, e.g. `QR_CODE`, `EAN_13`, `CODE_128`, `DATA_MATRIX`, `PDF_417`, `AZTEC`, `UPC_A`, `CODE_39`, … |
| `text` | Decoded payload |
| `box` | Corner points `[[x,y],…]` in image pixels (may be 2–4+ points) |

Supported formats (ZXingCpp / zxing-cpp + light OpenCV QR fallback): QR, Aztec, Data Matrix, PDF417, EAN-8/13, UPC-A/E, Code 39/93/128, Codabar, ITF, etc.

#### Success (`data.format = text`)

```json
{
  "code": 100,
  "data": "line one\nline two",
  "time": 40,
  "timestamp": 1710000000
}
```

With `ocr.barcode=true`, `barcodes` is also returned; if OCR is empty, `data` is filled with barcode lines like `[QR_CODE] payload`.

#### No text detected

```json
{
  "code": 101,
  "data": "No text detected",
  "time": 30,
  "timestamp": 1710000000
}
```

With `ocr.barcode=true` and nothing found: `data` is `"未检测到文字或条码"` and `barcodes` is `[]`.

#### Call examples

**PowerShell (JSON base64):**

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

**curl (multipart):**

```bash
curl -s -X POST "http://127.0.0.1:1224/api/ocr" \
  -F "file=@sample.png" \
  -F 'options={"data.format":"dict","ocr.angle":true}'
```

**Python:**

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

## 7. ASR (speech recognition)

Requires offline models under `asrmodels`. Streaming / dictation models are for the hotkey voice-input path only; **HTTP uses offline models only**.

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

`type` is an English keyword: `SenseVoice` / `Paraformer` / `Transducer` / `Whisper` / `ZipformerCtc`. Use `streaming` for live vs offline.

### 7.2 POST `/api/asr`

**Request JSON:**

| Field | Required | Description |
|-------|----------|-------------|
| `base64` | One of two | Audio base64 (wav/mp3/flac/webm, …; decoded server-side) |
| `path` | One of two | **Server-local** absolute audio path (local debugging only) |
| `filename` | No | Hint for extension, e.g. `a.wav` |
| `model` / `asr_model` | No | Model display name; default = config / first offline model |
| `lang` | No | Default `auto` (SenseVoice: zh/en/ja/ko/yue, …) |
| `itn` | No | Model ITN, default true |
| `postprocess` | No | Rule post-process after recognition, default true; `false` to skip |
| `device` / `compute` | No | `auto` / `gpu` / `cpu` / `igpu` |

**Success response:**

```json
{
  "code": 100,
  "data": {
    "text": "recognized text",
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

**Example:**

```bash
# Local path readable by the server process
curl -s -X POST "http://127.0.0.1:1224/api/asr" \
  -H "Content-Type: application/json" \
  -d "{\"path\":\"D:/audio/test.wav\",\"lang\":\"zh\"}"
```

---

## 8. TTS (speech synthesis)

HTTP uses the **Sherpa** TTS engine; usable models must exist under `ttsmodels`.

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

At most 64 speakers are listed per model.

### 8.2 POST `/api/tts`

| Field | Required | Description |
|-------|----------|-------------|
| `text` | Yes | Text to synthesize, max 20000 characters |
| `model` | No | Model display name |
| `speaker_id` / `sid` | No | Speaker id, default 0 |
| `speed` | No | Rate 0.5–2.0, default 1.0 |
| `device` / `compute` | No | `auto` / `gpu` / `cpu` / `igpu` |

**Success response:**

```json
{
  "code": 100,
  "data": {
    "format": "wav",
    "sample_rate": 22050,
    "samples": 44100,
    "wav_base64": "<base64 of full WAV file>",
    "model": "vits-zh",
    "speaker_id": 0,
    "provider": "CPU"
  },
  "time": 300,
  "timestamp": 1710000000
}
```

Decode `wav_base64` to obtain standard WAV bytes.

```python
import base64, json, urllib.request

req = urllib.request.Request(
    "http://127.0.0.1:1224/api/tts",
    data=json.dumps({"text": "Hello, world", "speed": 1.0}).encode("utf-8"),
    headers={"Content-Type": "application/json"},
    method="POST",
)
data = json.loads(urllib.request.urlopen(req).read().decode("utf-8"))
open("out.wav", "wb").write(base64.b64decode(data["data"]["wav_base64"]))
```

---

## 9. POST `/api/itn`

Inverse text normalization (WeText; rule post-process may still run if WeText is unavailable).

**Request:**

```json
{ "text": "二零二六年七月二十五日" }
```

**Response:**

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

| Field | Description |
|-------|-------------|
| `text` | Normalized text |
| `input` | Original input |
| `wetext` | Whether WeText binary/resources are available |

---

## 10. POST `/api/translate` · `/api/translate/batch`

LLM batch translate (internally grouped by 8; missing indexes retried one-by-one). **No cap on how many items one request may send.** Requires `[[llm]]` in settings and `translate_llm` (or pass `llm` in the request). `/api/translate/batch` is the same handler. Large lists take time proportional to the number of groups (about 90 s max per group).

**Request:**

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

| Field | Description |
|-------|-------------|
| `items` / `texts` | String array, unlimited length. Elements may also be `{"text":"…"}` |
| `text` | Single string; treated as a one-item batch |
| `src` / `dst` | Language codes (`zh` / `en` / `ja` / `ko` / `fr` / `de` / `es` / `ru` / `ar` / `th` / `cht` / …). If omitted, auto zh↔en from the first non-empty item |
| `dir` | Optional pair such as `zh-en` |
| `chunk` | Items per LLM call, default 8, range 1–10 |
| `llm` | Optional `[[llm]]` display name or model id; default `translate_llm`, else first list entry |

**Response:** `data.items[]` with `i` / `text` / `out`. `data.miss` is how many non-empty inputs came back empty. `GET /api/status` → `llm_translate` is true when a translate LLM is configured.

---

## 11. Face

Needs det+rec ONNX under `facemodels/`. Download InsightFace **buffalo_l** from **Tools → Install features**. Aliases: `POST /api/face/compare`, `POST /api/face/extract` (same handler).

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

One image: detect the largest face and extract an embedding. Two images: cosine similarity and same-person decision.

**JSON:**

| Field | Required | Description |
|-------|----------|-------------|
| `base64` / `image` / `path` | at least one | First image (base64 or server-local path) |
| `base64_b` / `image_b` / `path_b` | for compare | Second image |
| `det` / `reg` | no | Det/rec file names (fuzzy; default config or first in folder) |
| `threshold` | no | Compare threshold (config default, about 0.5) |
| `device` / `compute` | no | `auto` / `gpu` / `cpu` / `igpu` |
| `attr` / `genderage` | no | Run gender/age (`true` by default if `genderage.onnx` exists) |
| `include_feature` | no | Include embedding vector when `true` |

**multipart:** `file` + `file2` (or `image` / `image_b`), plus optional scalar fields.

**One-image success:**

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

**Two-image compare:**

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

No face: `code=101`.

```bash
curl -s -X POST "http://127.0.0.1:1224/api/face" \
  -F "file=@left.jpg" -F "file2=@right.jpg" -F "threshold=0.5"
```

---

## 11. Relationship to the main window

| Behavior | Description |
|----------|-------------|
| OCR model | If `ocr.language` is omitted, uses the main window’s current pack/variant |
| Device | May override with `ocr.device`; otherwise uses main-window device |
| Service mode | `service_mode = true` preloads engines for frequent API calls |
| Parameter changes | Changing model/device in the UI invalidates engines; next request reloads |

---

## 12. Security notes

1. Default bind is `127.0.0.1`. Do not use `0.0.0.0` without a firewall and authentication plan.
2. **No auth, no HTTPS** — trust only the local machine or a controlled LAN.
3. `POST /api/asr` and `POST /api/face` `path` / `path_b` read server-local files; never expose this to untrusted clients.
4. Large images / long audio use CPU/GPU and memory; watch concurrency (requests run via `Task.Run`; engines use locks).

---

## 13. Related

- Implementation: `ScreenKit/Ocr/HttpOcrServer.cs` · `HttpOcrServer.Face.cs` · `HttpOcrServer.Translate.cs`
- Config: `config.toml` (`http_enabled` / `http_host` / `http_port` / `service_mode`)
- Overview: [README.md](README.md) · [README.zh.md](README.zh.md)
