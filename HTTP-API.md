# ScreenKit HTTP API

**Languages:** [English](HTTP-API.md) · [中文](HTTP接口文档.md)

Local HTTP API: OCR endpoints, plus ASR / TTS / ITN / barcode / face extensions.

LAN access is on by default. Turn it off to listen on this PC only. **Do not** expose this port on untrusted networks.

---

## 1. Enable and base URL

Turn on HTTP under **Settings**, or edit `config.toml` next to the executable:

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

Each `http_*` module defaults to on. Turning one off leaves that path out of `GET /api` and makes the path return code 810. `/api/status`, `/api/toast`, `/api/zhconv`, `/api/calendar`, `/api/jpyomi`, `/api/text`, and `/api/cast/stop` stay available. `GET /` is the local tools page. `http_enabled = false` turns the whole server off. OCR also covers `/api/qr`. ASR also covers `/api/itn` and `/api/asr/models`.

| Item | Description |
|------|-------------|
| Base URL | `http://127.0.0.1:{http_port}`. With `http_lan = true`, LAN clients use this PC’s NIC address on the same port |
| Default | Port `1224`, `http_lan = true` (this PC and the LAN). `http_lan = false` listens on `127.0.0.1` only |
| Content-Type | Prefer `application/json; charset=utf-8` for JSON request/response |
| JSON text | UTF-8 CJK as-is (not `\uXXXX` escapes) |
| CORS | `*` allowed for local web debugging |
| OPTIONS | Preflight supported (204) |

The bottom status bar keeps the listener beside the memory summary, for example `HTTP 0.0.0.0:1224` or `HTTP 127.0.0.1:1224`. Per-NIC file-transfer sockets without the API listener show `HTTP LAN:1224`. If the API, file transfer, and cast are all off it shows `HTTP off`. If one of them is on but nothing is listening it shows `HTTP not started` and the error.

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
| 101 | OCR: no text; barcode: none found; Face: no face |
| 404 | Unknown path |
| 800 | Request parse failure / empty body |
| 801 | Empty request |
| 802 | Missing required field (e.g. base64, text) |
| 803 | Invalid field type/length |
| 804 | Failed to interpret options |
| 805 | Method not allowed (app-level; HTTP may also be 405) |
| 806 | base64 decode failed |
| 810 | That API module is turned off (Settings → API) |
| 900 | Internal error |
| 901 | OCR recognition failed |
| 910+ | ASR-related |
| 920+ | TTS-related (921 Sherpa unavailable, 922 no model/voice, 923 empty audio, 924 synth failed, 925 unknown engine) |
| 930+ | Face-related (930 no models, 931 failed, 932 missing det/rec files) |
| 950 | Barcode / QR scan failed |

---

## 3. Endpoint overview

| Method | Path | Description |
|--------|------|-------------|
| GET | `/` | Local tools page (HTML). Assets `/sk/tools.css` and `/sk/tools.js` |
| GET | `/api` | API name and endpoint list |
| GET | `/api/status` · `/api/health` | Service and capability status |
| GET/POST | `/api/toast` | Bottom-of-screen toast (not tied to a module switch) |
| GET/POST | `/api/zhconv` | Simplified/traditional (not tied to a module switch) |
| GET/POST | `/api/calendar` | Gregorian to another calendar (not tied to a module switch) |
| GET/POST | `/api/jpyomi` | Japanese reading (not tied to a module switch) |
| GET/POST | `/api/text` | Text encode/decode (not tied to a module switch) |
| GET | `/api/ocr/get_options` | OCR option descriptors |
| POST | `/api/ocr` | Image OCR |
| POST | `/api/qr` · `/api/barcode` · `/api/barcodes` | Barcode / QR only (no OCR) |
| GET | `/api/asr/models` | List ASR models |
| POST | `/api/asr` | Speech recognition |
| GET | `/api/tts/models` | List TTS models |
| POST | `/api/tts` | Speech synthesis (WAV base64) |
| POST | `/api/itn` | Inverse text normalization (WeText + rules) |
| POST | `/api/translate` · `/api/translate/batch` | LLM batch translate (needs `[[llm]]`) |
| POST | `/api/chat` | LLM chat (text or audio in; optional TTS `wav_base64`) |
| GET/POST | `/api/cast/stop` | Close the screencast viewer and drop the current receive session |
| WebSocket | `/cast` | Screencast media (same HTTP port; SCST frames as binary messages) |
| GET | `/api/face/models` | List face ONNX files |
| POST | `/api/face` | Face detect / embedding / compare two images |

Paths are case-insensitive; a trailing `/` is optional. A module turned off under Settings → API is omitted from `GET /api` and returns code 810.

---

## 4. GET `/` and GET `/api`

`GET /` and `HEAD /` return the local tools page: simplified/traditional, calendars, Japanese yomi, and text. The page loads `/sk/tools.css` and `/sk/tools.js`, and calls `/api/zhconv`, `/api/calendar`, `/api/jpyomi`, and `/api/text`. The desktop file manager is `/files`.

`GET /api` returns the service name and endpoint list.

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
      "POST /api/qr    JSON{base64|path} or multipart barcode/QR",
      "GET  /api/asr/models",
      "POST /api/asr   JSON{base64|path, model?, lang?, itn?, postprocess?}",
      "GET  /api/tts/models",
      "POST /api/tts   JSON{text, engine?, model?, voice?, speaker_id?, speed?, volume?}",
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
| `http_ocr` | bool | OCR and barcode paths enabled |
| `http_tts` | bool | TTS paths enabled |
| `http_asr` | bool | ASR and ITN paths enabled |
| `http_translate` | bool | Translate path enabled |
| `http_chat` | bool | Chat path enabled |
| `http_face` | bool | Face paths enabled |
| `ocr_engine` | bool | OCR runner available |
| `asr_engine` | bool | ASR engine injected |
| `tts_engine` | bool | TTS (Sherpa) engine injected |
| `tts_sapi` | bool | Local SAPI voices available |
| `tts_winrt` | bool | Windows (WinRT / OneCore) voices available |
| `tts_edge` | bool | Edge online voice feature is built in (actual synthesis still needs network access) |
| `asr_models` | int | Scanned ASR model count |
| `tts_models` | int | Scanned Sherpa TTS model count |
| `face_ready` | bool | Whether `facemodels` has det+rec ONNX |
| `face_models` | int | Scanned face ONNX count |
| `barcode` | bool | Barcode / QR endpoint is built in (no extra model). Scanning still needs `ZXing.dll`, which is not in the release archive |
| `itn` | bool | WeText ITN available |
| `itn_error` | string | Reason when ITN is unavailable |
| `llm_translate` | bool | Translate LLM configured |
| `llm_chat` | bool | Chat LLM configured |

**Example:**

```bash
curl -s "http://127.0.0.1:1224/api/status"

---

## 5.1 GET/POST `/api/toast`

Shows a toast at the bottom of the primary monitor work area. It does not take focus. Duration is clamped to 800–8000 ms.

**GET:** `/api/toast?text=Copied&ms=1900`

**POST** `application/json`

| Field | Type | Description |
|-------|------|-------------|
| `text` | string | Message. `message` is an alias |
| `ms` | number | Visible milliseconds, default 1900 |

```json
{ "text": "Copied", "ms": 1900 }
```

**Response `data`:** `text` and `ms` (clamped to 800–8000). An empty `text` returns code 802.

```bash
curl -s -X POST "http://127.0.0.1:1224/api/toast" -H "Content-Type: application/json" -d "{\"text\":\"Copied\",\"ms\":1900}"
```

The CLI shows the same toast without HTTP:

```bash
ScreenKit --toast "Copied" --ms 1900
```
```

---

## 5.2 GET/POST `/api/zhconv`

`LCMapStringEx` simplified/traditional conversion. Traditional uses `zh-TW`. The default direction is traditional. GET query strings are percent-encoded and decoded as UTF-8.

**GET:** `/api/zhconv?text=软件&to=trad`

**POST** `application/json`

| Field | Type | Description |
|-------|------|-------------|
| `text` | string | Source. `message` is an alias |
| `to` | string | `trad` (default) or `simp` |

```json
{ "text": "软件", "to": "trad" }
```

**Response `data`:** `text`, `to`. An empty `text` returns 802.

```bash
ScreenKit --zhconv "软件"
ScreenKit --zhconv "國發" --simp
```

---

## 5.3 GET/POST `/api/calendar`

`Windows.Globalization.Calendar`. Chinese lunar includes the sexagenary year and leap month. The date is converted at local noon. Omitted date is today; omitted calendar is lunar.

**GET:** `/api/calendar?date=2024-02-10&cal=lunar`

**POST** `application/json`

| Field | Type | Description |
|-------|------|-------------|
| `date` | string | `yyyy-MM-dd`. Omitted means today |
| `cal` | string | `lunar` (default), `gregorian`, `jp`, `jplunar`, `tw`, `ko`, `vilunar`, `he`, `hijri`, `umalqura`, `fa`, `th`, `julian` |

```json
{ "date": "2024-02-10", "cal": "lunar" }
```

**Response `data`:** `cal`, `gregorian`, `ganzhi`, `era`, `era_num`, `year`, `year_num`, `month`, `month_num`, `month_count`, `day`, `day_num`, `week`, `leap`. A bad date or calendar returns 802.

```bash
ScreenKit --calendar 2024-02-10 --cal lunar
```

---

## 5.4 GET/POST `/api/jpyomi`

`JapanesePhoneticAnalyzer`. The Japanese language feature must be installed. Windows has no equivalent Chinese pinyin API.

**GET:** `/api/jpyomi?text=東京は晴れです`

**POST** `application/json`

| Field | Type | Description |
|-------|------|-------------|
| `text` | string | Japanese. `message` is an alias |
| `mono` | bool | `true` reads character by character. Default is by word |

```json
{ "text": "東京は晴れです", "mono": false }
```

**Response `data`:** `ruby`, `yomi`, `mono`. An empty `text` returns 802.

```bash
ScreenKit --jpyomi "東京は晴れです"
ScreenKit --jpyomi "東京" --mono
```

---

## 5.5 GET/POST `/api/text`

Text helpers. Not tied to a module switch. `text` may be empty. Unknown `op` returns 802. Bad Base64, hex, or JSON returns 500.

| Field | Type | Description |
|-------|------|-------------|
| `text` | string | Source. `message` is an alias |
| `op` | string | `b64enc` `b64dec` `urlenc` `urldec` `utf8hex` `utf8unhex` `gbkhex` `gbkunhex` `uesc` `uunesc` `upper` `lower` `collapse` `dropempty` `json` `stats` |

```json
{ "text": "hi", "op": "b64enc" }
```

**Response `data`:** `text` is the result (`aGk=` for the example). `op` echoes the operation. `stats` puts a short multiline count in `text`.

---

## 6. OCR

### 6.1 GET `/api/ocr/get_options`

Returns option descriptors: each entry has `title` / `toolTip` / `default` / optional `optionsList`.

| Key | Meaning | Default |
|-----|---------|---------|
| `ocr.angle` | Text orientation (cls) | `true` |
| `ocr.maxSideLen` | Detection side limit | `1024` |
| `ocr.engine` | Engine: empty = main window, `winocr` = Windows OCR, `onnx` = ONNX pack | `""` |
| `ocr.language` | ONNX variant title, or a Windows OCR language | `""` (main window model / language) |
| `ocr.device` | Device: `cpu` / `gpu` / `intel` | `cpu` |
| `ocr.barcode` | Also scan barcodes / QR codes | `false` (barcode-only: `POST /api/qr`) |
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
    "ocr.engine": "winocr",
    "ocr.language": "zh-Hans-CN",
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
| `ocr.engine` | string | Empty follows the main window. `winocr` / `windows` / `Windows 系统 OCR` uses Windows OCR and does not load ONNX. `onnx` switches back to the first ONNX pack when the main window is Windows OCR |
| `ocr.language` | string | ONNX: match a variant title (fuzzy contains). Windows OCR: BCP-47 or a language name (`zh-Hans-CN`, `en-US`, `简体中文`, `英语`, `韩语`). Setting this field to `winocr` also selects Windows OCR. An unknown installed language fails |
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
| `box` | Four-point box `[[x,y],…]` in **original image pixels** (detection may downscale by `maxSideLen`; coordinates are mapped back) |
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

Uses either an offline model under `asrmodels` or an installed Windows system recognizer (`System.Speech`). Sherpa streaming models are for live paths only; **HTTP uses offline recognition**.

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

`type` is `Windows` for a system recognizer, otherwise `SenseVoice` / `Paraformer` / `Transducer` / `Whisper` / `ZipformerCtc`. Windows entries include a BCP-47 `culture`. Use `streaming` for live vs offline.

### 7.2 POST `/api/asr`

**Request JSON:**

| Field | Required | Description |
|-------|----------|-------------|
| `base64` | One of two | Audio base64 (wav/mp3/flac/webm, …; decoded server-side) |
| `path` | One of two | **Server-local** absolute audio path (local debugging only) |
| `filename` | No | Hint for extension, e.g. `a.wav` |
| `model` / `asr_model` | No | Model display name; accepts a Sherpa model or Windows recognizer; default = config / first offline entry |
| `lang` | No | Default `auto` (SenseVoice: zh/en/ja/ko/yue, …) |
| `itn` | No | Model ITN, default true |
| `postprocess` | No | Rule post-process after recognition, default true; `false` to skip |
| `device` / `compute` | No | `auto` / `gpu` / `cpu` / `igpu`; ignored by Windows ASR |

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

Four engines: **Sherpa** (ONNX packs under `ttsmodels`), **SAPI** (classic `System.Speech`, including x86-only voices via `x86host.exe`), **Windows** (`engine=winrt`, WinRT / OneCore neural voices), and **Edge online** (`engine=edge`, no model/API key, Internet required). Omitting `engine` keeps the Sherpa path when a Sherpa model exists; otherwise it uses Windows speech when a voice is installed, or Edge online when none is.

### 8.1 GET `/api/tts/models`

```json
{
  "code": 100,
  "data": [
    {
      "name": "vits-zh",
      "engine": "sherpa",
      "type": "Vits",
      "speakers": [
        { "id": 0, "name": "speaker0", "lang": "zh", "gender": "" }
      ]
    },
    {
      "name": "SAPI",
      "engine": "sapi",
      "type": "Sapi",
      "speakers": [
        { "id": 0, "name": "Microsoft Huihui Desktop", "lang": "zh", "gender": "female", "key": "sapi:Microsoft Huihui Desktop" }
      ]
    },
    {
      "name": "Windows",
      "engine": "winrt",
      "type": "WinRt",
      "speakers": [
        { "id": 0, "name": "{voice-id}", "lang": "zh", "gender": "female", "key": "winrt:{voice-id}" }
      ]
    },
    {
      "name": "Edge Online",
      "engine": "edge",
      "type": "Edge",
      "speakers": [
        { "id": 0, "name": "ko-KR-SunHiNeural", "lang": "ko", "gender": "female", "key": "edge:ko-KR-SunHiNeural" }
      ]
    }
  ],
  "count": 4
}
```

Sherpa lists at most 64 speakers per model. SAPI / Windows list all installed voices; the Edge online catalog is fetched from Microsoft. x86-only SAPI voices have `key` prefix `sapi-x86:`.

### 8.2 POST `/api/tts`

| Field | Required | Description |
|-------|----------|-------------|
| `text` | Yes | Text to synthesize, max 20000 characters |
| `engine` | No | `sherpa` / `sapi` / `winrt` / `edge` (plus aliases). Empty = infer from `model` / `voice`; Sherpa if a model is present, otherwise Windows speech or Edge online |
| `model` | No | Sherpa display name, or `SAPI` / `Windows` / `Edge Online` |
| `voice` / `speaker` | No | System/online voice name or `key` (`sapi:…` / `sapi-x86:…` / `winrt:…` / `edge:…`) |
| `speaker_id` / `sid` | No | Speaker index (Sherpa sid, or index into that engine’s list). Default 0 |
| `speed` | No | Rate 0.5–2.0, default 1.0 |
| `volume` | No | 0–100, default 100 (SAPI / Windows / Edge; ignored by Sherpa) |
| `device` / `compute` | No | Sherpa only: `auto` / `gpu` / `cpu` / `igpu` |

**Success response:**

```json
{
  "code": 100,
  "data": {
    "format": "wav",
    "sample_rate": 22050,
    "samples": 44100,
    "wav_base64": "<base64 of full WAV file>",
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

`provider` is Sherpa’s EP (`CPU` / CUDA / …), or `SAPI` / `SAPI x86` / `WinRT` / `Edge Online`. Decode `wav_base64` to obtain standard WAV bytes.

```python
import base64, json, urllib.request

req = urllib.request.Request(
    "http://127.0.0.1:1224/api/tts",
    data=json.dumps({"text": "Hello, world", "engine": "winrt", "speed": 1.0}).encode("utf-8"),
    headers={"Content-Type": "application/json"},
    method="POST",
)
data = json.loads(urllib.request.urlopen(req).read().decode("utf-8"))
open("out.wav", "wb").write(base64.b64decode(data["data"]["wav_base64"]))
```

CLI: `ScreenKit --test-http-tts` starts a loopback server and checks SAPI / Windows / Edge WAV output.

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

LLM batch translate (grouped by `translate_llm_batch`, default 8; missing indexes retried one-by-one). **No cap on how many items one request may send.** Requires `[[llm]]` in settings and `translate_llm` (or pass `llm` in the request). `/api/translate/batch` is the same handler. Large lists take time proportional to the number of groups (about 90 s max per group).

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
| `chunk` | Items per LLM call. Omitted uses `translate_llm_batch` (default 8). Range 1–64 |
| `llm` | Optional `[[llm]]` display name or model id; default `translate_llm`, else first list entry |

**Response:** `data.items[]` with `i` / `text` / `out`. `data.miss` is how many non-empty inputs came back empty. `GET /api/status` → `llm_translate` is true when a translate LLM is configured.

---

## 11. POST `/api/chat`

LLM chat. **Text** or **audio** user message; always returns text; WAV `wav_base64` only when request **`tts`: true** (ignores config and other field names). Needs `[[llm]]` (`chat_llm` or request `llm`).

| Field | Required | Description |
|-------|----------|-------------|
| `text` / `message` / `content` | text **or** audio | Current user text (max 8000) |
| `base64` / `path` | text **or** audio | User speech; text wins if both set |
| `messages` | no | Prior `user`/`assistant` turns (not including current user) |
| `tts` | no | **Only this field** controls TTS; `true` → `wav_base64`, default/`false` → text only |
| `agent` | no | Tool agent; default from `chat_agent` |
| `llm` | no | Endpoint display name / model id |
| TTS / ASR fields | no | Same as `/api/tts` and `/api/asr` (`engine`, `voice`, `asr_model`, `lang`, …) |

**Success `data`:** `text`/`reply`, `user_text`, `llm_ms` / `asr_ms` / `tts_ms`, optional `wav_base64` + `format`/`sample_rate`/`engine`. TTS failure still returns text with `tts_error`. Codes: `960` no LLM, `961` missing input, `962` ASR fail, `963` chat fail. Status flag: `llm_chat`.

```bash
curl -s -X POST "http://127.0.0.1:1224/api/chat" \
  -H "Content-Type: application/json" \
  -d "{\"text\":\"hello\",\"tts\":true,\"engine\":\"sapi\"}"
```

---

## 12. Face

Needs det+rec ONNX under `facemodels/`. Download InsightFace **buffalo_l** from **Help → Install features**. Aliases: `POST /api/face/compare`, `POST /api/face/extract` (same handler).

### 12.1 GET `/api/face/models`

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

### 12.2 POST `/api/face`

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

## 12. QR / barcode

Dedicated barcode scan — **does not run OCR**. Same ZXingCpp pipeline as the result-panel **Barcode** tab (QR, Aztec, Data Matrix, PDF417, EAN-8/13, UPC-A/E, Code 39/93/128, Codabar, ITF, plus a light OpenCV QR fallback). `ZXing.dll` is not in the release archive. **Help → Install features** downloads it. A request without that file returns code 950. Aliases: `POST /api/barcode`, `POST /api/barcodes` (same handler). Combined OCR+barcode remains `POST /api/ocr` with `options.ocr.barcode`.

### 12.1 POST `/api/qr`

**JSON:**

```json
{
  "base64": "<image base64>",
  "path": "D:\\sample.png",
  "format": "dict"
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `base64` | one of | Image bytes (png/jpg/bmp/webp, …) |
| `path` | one of | Server-local file path (same caveat as ASR/face) |
| `image` / `img` | — | Aliases of `base64` |
| `format` | no | `dict` (default) or `text`; also `data.format` or `options.data.format` |

**multipart:** file field `file` / `image` / `img` / `upload`, same as OCR.

**Success (`format=dict`):**

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

`type` / `text` / `box` match the `barcodes[]` objects on `POST /api/ocr`.

**Success (`format=text`):** `data` is lines like `[QRCode] https://example.com`.

**None found:** `code=101`, `count=0`; `data` is `"未检测到条码或二维码"` (dict) or `""` (text). Scan errors: `950`.

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

## 13. Relationship to the main window

| Behavior | Description |
|----------|-------------|
| OCR model | With `ocr.engine` and `ocr.language` omitted, follows the main window. `ocr.engine=winocr` (or `ocr.language=winocr`) uses Windows OCR. Its language is `ocr.language`, or the main window `win_ocr_langs` when omitted, and no ONNX model is loaded. When the main window is already Windows OCR, `ocr.engine=onnx` or an ONNX variant title switches back to a model pack. `ocr.maxSideLen` still applies and defaults to the main window `det_limit` |
| Device | May override with `ocr.device`; otherwise uses main-window device |
| Service mode | `service_mode = true` preloads engines for frequent API calls and does not unload them on idle |
| Idle unload | `onnx_unload_min` (default 5) unloads OCR, translation, face, speech recognition, and speech synthesis after that many idle minutes. `0` disables it. Ignored while service mode is on |
| Parameter changes | Changing model/device in the UI invalidates engines; next request reloads |

---

## 14. Security notes

1. Default is `http_lan = true` (every interface). Set `http_lan = false` to listen on `127.0.0.1` only. Do not expose the port on an untrusted network; there is no firewall rule or authentication built in.
2. **No auth, no HTTPS** — trust only the local machine or a controlled LAN.
3. `POST /api/asr`, `POST /api/face`, and `POST /api/qr` `path` / `path_b` read server-local files; never expose this to untrusted clients.
4. Large images / long audio use CPU/GPU and memory; watch concurrency (requests run via `Task.Run`; engines use locks).

---

## 15. PC file transfer (LAN, pairing required)

Shares the HTTP API port (default **1224**). Enable under **Settings → API → PC file transfer**. Phones and browsers on the LAN also need **Allow LAN access** (`http_lan = true`). UDP discovery **17531**. Files are restricted to `sendfile/` next to the exe.

`GET /apk` needs **no pairing** (phone scans the QR before the app is installed). `GET /files` and `GET /m` are the web file manager (desktop / phone). `GET /` is the tools page, not the file manager. `GET /f/<rel>` downloads **without login**. Web upload/list/delete need a login (cookie `sk_web` or `X-Web-Token`). Other phone routes after pairing: `X-Device-Id` + `Authorization: Bearer <token>`.

Discovery: UDP broadcast `SCREENKIT_DISCOVER` to port 17531; the PC replies with JSON `{v,name,httpPort,pcId}`.

| Method | Path | Notes |
|--------|------|--------|
| GET | `/files` | Desktop web page (mobile UA redirects to `/m`; `?pc=1` forces desktop) |
| GET | `/index.html` | Redirects to `/files` |
| GET | `/m` | Phone web page |
| GET | `/web/app.js` · `/web/d.css` · `/web/m.css` | Page assets |
| GET | `/f/<rel>` | Public download (no login; path must stay inside sendfile/) |
| GET | `/d?p=` | Public download (query) |
| GET | `/apk` | APK bytes from this PC (no pairing; File sync **Install on phone** QR) |
| POST | `/api/web/login` | body `{password, keep?}` → cookie `sk_web` + `{token}`. `keep=true` stores a 30-day cookie (Max-Age and Expires) on disk and still works after the program restarts |
| GET/POST | `/api/web/zip?path=` · `?paths=a\|b` | Zip download (login required; file or folder) |
| POST | `/api/web/logout` | Clear session |
| GET | `/api/web/me` | Whether signed in |
| GET | `/api/web/list?path=` | List (login) |
| POST | `/api/web/upload?path=` | Raw-body upload (login) |
| POST | `/api/web/mkdir` | body `{path}` (login) |
| POST | `/api/web/rename` | body `{from,to}` (login) |
| DELETE | `/api/web/delete?path=` | Delete (login) |
| GET | `/api/web/text` | Current text on the PC File sync page `{text}` (login) |
| POST | `/api/web/text` | body `{text}` writes that box; an empty string clears it (login, max 65536) |
| POST | `/api/sendfile/pair` | body `{id,name}`; first time shows a confirm dialog on the PC |
| GET | `/api/sendfile/info` | name / pcId |
| GET | `/api/sendfile/list?path=&deep=` | `deep=1` recursive |
| GET | `/api/sendfile/download?path=` | octet-stream |
| POST | `/api/sendfile/upload?path=` | raw body |
| POST | `/api/sendfile/mkdir` | body `{path}` |
| DELETE | `/api/sendfile/delete?path=` | file or directory |
| POST | `/api/sendfile/text` | body `{text}` → File sync tab text list |
| GET | `/api/sendfile/text?since=` | messages from PC to this phone |
| GET | `/api/sendfile/pull` | files queued from the PC for this phone `{items:[{id,path,rel,name,size}]}` (`path` is for download) |
| POST | `/api/sendfile/pulldone` | body `{id}`; ack and delete the PC staging copy |

JSON `code` 100 = success; 401/403 unpaired; 410 bad path.

Android app: [android/README.md](android/README.md).

---

## 16. Related

- Implementation: `ScreenKit/Ocr/HttpOcrServer.cs` · `HttpOcrServer.Face.cs` · `HttpOcrServer.Translate.cs` · `HttpOcrServer.Qr.cs`
- Config: `config.toml` (`http_enabled` / `http_lan` / `http_port` / `service_mode`)
- Overview: [README.md](README.md) · [README.zh.md](README.zh.md)
