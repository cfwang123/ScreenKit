# WpfOCR HTTP API

**Languages:** [English](HTTP-API.md) · [中文](HTTP接口文档.md)

Local HTTP API: Umi-OCR–style OCR endpoints, plus ASR / TTS / ITN extensions.

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
| CORS | `*` allowed for local web debugging |
| OPTIONS | Preflight supported (204) |

When healthy, the status bar may show something like: `HTTP API · http://127.0.0.1:1224/api/ocr`.

---

## 2. Response conventions

Most endpoints return **HTTP 200** always; success or failure is indicated by the JSON `code` field (Umi-style). Unknown routes may return 404/405; uncaught exceptions may return 500.

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
| 101 | OCR: no text detected |
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

---

## 3. Endpoint overview

| Method | Path | Description |
|--------|------|-------------|
| GET | `/` · `/api` | API name and endpoint list |
| GET | `/api/status` · `/api/health` | Service and capability status |
| GET | `/api/ocr/get_options` | OCR option schema (Umi-style) |
| POST | `/api/ocr` | Image OCR |
| GET | `/api/asr/models` | List ASR models |
| POST | `/api/asr` | Speech recognition |
| GET | `/api/tts/models` | List TTS models |
| POST | `/api/tts` | Speech synthesis (WAV base64) |
| POST | `/api/itn` | Inverse text normalization (WeText + rules) |

Paths are case-insensitive; a trailing `/` is optional.

---

## 4. GET `/` · `/api`

Returns service name and endpoint list.

**Response example:**

```json
{
  "code": 100,
  "data": {
    "name": "WpfOCR HTTP API",
    "umi_compatible": true,
    "endpoints": [
      "GET  /api/status",
      "GET  /api/ocr/get_options",
      "POST /api/ocr   JSON{base64,options} or multipart",
      "GET  /api/asr/models",
      "POST /api/asr   JSON{base64|path, model?, lang?, itn?, postprocess?}",
      "GET  /api/tts/models",
      "POST /api/tts   JSON{text, model?, speaker_id?, speed?}",
      "POST /api/itn   JSON{text}  WeText+rules"
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
| `app` | string | `"WpfOCR"` |
| `http_enabled` | bool | Whether HTTP is enabled in config |
| `ocr_engine` | bool | OCR runner available |
| `asr_engine` | bool | ASR engine injected |
| `tts_engine` | bool | TTS (Sherpa) engine injected |
| `asr_models` | int | Scanned ASR model count |
| `tts_models` | int | Scanned TTS model count |
| `itn` | bool | WeText ITN available |
| `itn_error` | string | Reason when ITN is unavailable |

**Example:**

```bash
curl -s "http://127.0.0.1:1224/api/status"
```

---

## 6. OCR

### 6.1 GET `/api/ocr/get_options`

Returns Umi-like option descriptors: each entry has `title` / `toolTip` / `default` / optional `optionsList`.

| Key | Meaning | Default |
|-----|---------|---------|
| `ocr.angle` | Text orientation (cls) | `true` |
| `ocr.maxSideLen` | Detection side limit | `1024` |
| `ocr.language` | Language / model variant title | `""` (use main window model) |
| `ocr.device` | Device: `cpu` / `gpu` / `intel` | `cpu` |
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

#### Success (`data.format = text`)

```json
{
  "code": 100,
  "data": "line one\nline two",
  "time": 40,
  "timestamp": 1710000000
}
```

#### No text detected

```json
{
  "code": 101,
  "data": "No text detected",
  "time": 30,
  "timestamp": 1710000000
}
```

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

## 10. Relationship to the main window

| Behavior | Description |
|----------|-------------|
| OCR model | If `ocr.language` is omitted, uses the main window’s current pack/variant |
| Device | May override with `ocr.device`; otherwise uses main-window device |
| Service mode | `service_mode = true` preloads engines for frequent API calls |
| Parameter changes | Changing model/device in the UI invalidates engines; next request reloads |

---

## 11. Security notes

1. Default bind is `127.0.0.1`. Do not use `0.0.0.0` without a firewall and authentication plan.
2. **No auth, no HTTPS** — trust only the local machine or a controlled LAN.
3. `POST /api/asr` `path` reads server-local files; never expose this to untrusted clients.
4. Large images / long audio use CPU/GPU and memory; watch concurrency (requests run via `Task.Run`; engines use locks).

---

## 12. Related

- Implementation: `WpfOCR/Ocr/HttpOcrServer.cs`
- Config: `config.toml` (`http_enabled` / `http_host` / `http_port` / `service_mode`)
- Overview: [README.md](README.md) · [README.zh.md](README.zh.md)
