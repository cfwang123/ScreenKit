using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NAudio.Wave;
using OpenCvSharp;

namespace ScreenKit;

/// <summary>
/// HTTP API（Umi 兼容 OCR + 本项目扩展）。
/// <list type="bullet">
/// <item>GET  /api · /api/status</item>
/// <item>GET  /api/ocr/get_options · POST /api/ocr</item>
/// <item>GET  /api/asr/models · POST /api/asr</item>
/// <item>GET  /api/tts/models · POST /api/tts</item>
/// <item>POST /api/itn</item>
/// <item>POST /api/translate · /api/translate/batch</item>
/// <item>GET  /api/face/models · POST /api/face</item>
/// </list>
/// </summary>
sealed partial class HttpOcrServer : IDisposable {
	readonly Func<OcrOptions> getOpts;
	readonly OcrRunner runner;
	readonly object listenLock = new();
	HttpListener listener;
	bool disposed;
	volatile bool running;
	HttpApiServices svc;
	public event Action<string> Logged;

	public HttpOcrServer(Func<OcrOptions> optionsFactory, OcrRunner sharedRunner) {
		getOpts = optionsFactory ?? throw new ArgumentNullException(nameof(optionsFactory));
		runner = sharedRunner ?? throw new ArgumentNullException(nameof(sharedRunner));
	}

	/// <summary>注入 ASR/TTS 等扩展能力（可在服务启动后设置）。</summary>
	public void SetServices(HttpApiServices services) => svc = services;

	public bool IsRunning => running;

	public void Start(string host, int port) {
		Compat.ThrowIfDisposed(disposed, this);
		host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
		port = Compat.Clamp(port, 1, 65535);
		lock (listenLock) {
			Stop();
			var prefix = $"http://{host}:{port}/";
			var l = new HttpListener();
			l.Prefixes.Add(prefix);
			// 兼容无尾斜杠访问
			try { l.Prefixes.Add($"http://{host}:{port}/api/"); } catch { }
			l.Start();
			listener = l;
			running = true;
			_ = Task.Run(acceptloop);
		}
	}

	public void Stop() {
		running = false;
		HttpListener l;
		lock (listenLock) {
			l = listener;
			listener = null;
		}
		if (l == null) return;
		// Abort 比 Stop 更快打断 GetContextAsync，避免退出时挂起
		try { l.Abort(); } catch {
			try { l.Stop(); } catch { }
		}
		try { l.Close(); } catch { }
	}

	/// <summary>参数变更时与主窗口共用 runner，统一 Invalidate。</summary>
	public void InvalidateEngine() => runner?.Invalidate();

	async Task acceptloop() {
		while (running) {
			HttpListener l;
			lock (listenLock) l = listener;
			if (l == null || !l.IsListening) break;
			HttpListenerContext ctx;
			try {
				ctx = await l.GetContextAsync().ConfigureAwait(false);
			}
			catch (ObjectDisposedException) { break; }
			catch (HttpListenerException) { break; }
			catch {
				if (!running) break;
				continue;
			}
			_ = Task.Run(() => handle(ctx));
		}
	}

	void handle(HttpListenerContext ctx) {
		var t0 = Environment.TickCount;
		var method = "";
		var pathRaw = "";
		try {
			var req = ctx.Request;
			method = req.HttpMethod ?? "";
			pathRaw = (req.Url?.AbsolutePath ?? "/").TrimEnd('/');
			if (pathRaw.Length == 0) pathRaw = "/";
			// CORS 预检
			if (string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase)) {
				writecors(ctx, 204);
				return;
			}

			var path = pathRaw.ToLowerInvariant();

			if (path is "/api/ocr/get_options" or "/api/ocr/get_options/") {
				if (!isget(req)) {
					writejson(ctx, 405, err(805, "get_options 仅支持 GET"));
					return;
				}
				writejson(ctx, 200, buildoptionsobj());
				return;
			}

			if (path is "/api/ocr" or "/api/ocr/") {
				if (!ispost(req)) {
					writejson(ctx, 405, err(805, "ocr 仅支持 POST"));
					return;
				}
				handleocr(ctx);
				return;
			}

			if (path is "/api/status" or "/api/health") {
				if (!isget(req)) {
					writejson(ctx, 405, err(805, "status 仅支持 GET"));
					return;
				}
				handlestatus(ctx);
				return;
			}

			if (path is "/api/asr/models") {
				if (!isget(req)) {
					writejson(ctx, 405, err(805, "asr/models 仅支持 GET"));
					return;
				}
				handleasrmodels(ctx);
				return;
			}

			if (path is "/api/asr") {
				if (!ispost(req)) {
					writejson(ctx, 405, err(805, "asr 仅支持 POST"));
					return;
				}
				handleasr(ctx);
				return;
			}

			if (path is "/api/tts/models") {
				if (!isget(req)) {
					writejson(ctx, 405, err(805, "tts/models 仅支持 GET"));
					return;
				}
				handlettsmodels(ctx);
				return;
			}

			if (path is "/api/tts") {
				if (!ispost(req)) {
					writejson(ctx, 405, err(805, "tts 仅支持 POST"));
					return;
				}
				handletts(ctx);
				return;
			}

			if (path is "/api/itn") {
				if (!ispost(req)) {
					writejson(ctx, 405, err(805, "itn 仅支持 POST"));
					return;
				}
				handleitn(ctx);
				return;
			}

			if (path is "/api/translate" or "/api/translate/batch") {
				if (!ispost(req)) {
					writejson(ctx, 405, err(805, "translate 仅支持 POST"));
					return;
				}
				handletranslate(ctx);
				return;
			}

			if (path is "/api/face/models") {
				if (!isget(req)) {
					writejson(ctx, 405, err(805, "face/models 仅支持 GET"));
					return;
				}
				handlefacemodels(ctx);
				return;
			}

			if (path is "/api/face" or "/api/face/compare" or "/api/face/extract") {
				if (!ispost(req)) {
					writejson(ctx, 405, err(805, "face 仅支持 POST"));
					return;
				}
				handleface(ctx);
				return;
			}

			if (path is "/" or "/api" or "/api/") {
				writejson(ctx, 200, new JsonObject {
					["code"] = 100,
					["data"] = new JsonObject {
						["name"] = AppNames.Current + " HTTP API",
						["umi_compatible"] = true,
						["endpoints"] = new JsonArray {
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
							"POST /api/face  JSON{base64|base64_b|path} 或 multipart 人脸检测/比对",
						},
					},
				});
				return;
			}

			writejson(ctx, 404, err(404, $"未知接口: {path}"));
		}
		catch (Exception ex) {
			try { writejson(ctx, 500, err(900, $"内部错误: {ex.Message}")); } catch { }
		}
		finally {
			if (!string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase)) {
				var ms = unchecked(Environment.TickCount - t0);
				var st = 0;
				try { st = ctx.Response.StatusCode; } catch { }
				emitlog(method, pathRaw, st, ms);
			}
		}
	}

	void emitlog(string method, string path, int status, int ms) {
		var line = $"{DateTime.Now:HH:mm:ss}  {method}  {path}  {status}  {ms}ms";
		try { Logged?.Invoke(line); } catch { }
	}

	static bool isget(HttpListenerRequest req) =>
		string.Equals(req.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(req.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase);

	static bool ispost(HttpListenerRequest req) =>
		string.Equals(req.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase);

	// ───────── status / ASR / TTS / ITN ─────────

	void handlestatus(HttpListenerContext ctx) {
		var o = getOpts?.Invoke() ?? new OcrOptions();
		var asrN = 0;
		var ttsN = 0;
		var faceN = 0;
		try { asrN = svc?.ScanAsr?.Invoke()?.Count ?? 0; } catch { }
		try { ttsN = svc?.ScanTts?.Invoke()?.Count ?? 0; } catch { }
		try { faceN = FaceModels.ListOnnx().Count; } catch { }
		writejson(ctx, 200, new JsonObject {
			["code"] = 100,
			["data"] = new JsonObject {
				["app"] = "ScreenKit",
				["http_enabled"] = o.HttpEnabled,
				["ocr_engine"] = runner != null,
				["asr_engine"] = svc?.AsrEngine != null,
				["tts_engine"] = svc?.TtsEngine != null,
				["asr_models"] = asrN,
				["tts_models"] = ttsN,
				["face_ready"] = FaceModels.IsReady(),
				["face_models"] = faceN,
				["itn"] = WetextItn.IsAvailable,
				["itn_error"] = WetextItn.IsAvailable ? "" : (WetextItn.LastError ?? ""),
				["llm_translate"] = AsrLlmClient.IsEndpointReady(
					o.SelectedTranslateLlm() ?? o.SelectedLlm()),
			},
			["timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds(),
		});
	}

	void handleasrmodels(HttpListenerContext ctx) {
		List<AsrModelInfo> list = null;
		try { list = svc?.ScanAsr?.Invoke(); } catch (Exception ex) {
			writejson(ctx, 200, err(910, "扫描 ASR 失败: " + ex.Message));
			return;
		}
		list ??= new List<AsrModelInfo>();
		var arr = new JsonArray();
		foreach (var m in list) {
			arr.Add(new JsonObject {
				["name"] = m.DisplayName ?? "",
				["type"] = m.TypeLabel ?? m.Type.ToString(),
				["streaming"] = m.IsStreaming,
				["sample_rate"] = m.SampleRate,
			});
		}
		writejson(ctx, 200, new JsonObject {
			["code"] = 100,
			["data"] = arr,
			["count"] = arr.Count,
		});
	}

	void handleasr(HttpListenerContext ctx) {
		if (svc?.AsrEngine == null) {
			writejson(ctx, 200, err(911, "ASR 引擎不可用"));
			return;
		}
		JsonObject jo;
		try { jo = readjsonbody(ctx.Request); }
		catch (Exception ex) {
			writejson(ctx, 200, err(800, ex.Message));
			return;
		}

		byte[] audioBytes = null;
		string pathHint = null;
		// base64 音频
		if (jo["base64"] != null && jo["base64"].GetValueKind() != JsonValueKind.Null) {
			try { audioBytes = decodebase64(jo["base64"]?.GetValue<string>() ?? ""); }
			catch (Exception ex) {
				writejson(ctx, 200, err(806, "base64 解码失败: " + ex.Message));
				return;
			}
		}
		// 服务端本地路径（仅本机调试）
		if ((audioBytes == null || audioBytes.Length == 0)
			&& jo["path"] != null && jo["path"].GetValueKind() == JsonValueKind.String) {
			pathHint = jo["path"].GetValue<string>();
		}
		// multipart 图片接口也可传 file，此处仅 JSON
		if ((audioBytes == null || audioBytes.Length == 0) && string.IsNullOrWhiteSpace(pathHint)) {
			writejson(ctx, 200, err(802, "请提供 base64 或 path 音频"));
			return;
		}

		var modelName = jo["model"]?.GetValue<string>() ?? jo["asr_model"]?.GetValue<string>() ?? "";
		var lang = jo["lang"]?.GetValue<string>() ?? "auto";
		var useItn = jo["itn"] == null || jo["itn"].GetValueKind() == JsonValueKind.Null
			|| (jo["itn"].GetValueKind() == JsonValueKind.True)
			|| (jo["itn"].GetValueKind() == JsonValueKind.String
				&& !string.Equals(jo["itn"].GetValue<string>(), "false", StringComparison.OrdinalIgnoreCase));
		var post = jo["postprocess"] == null || jo["postprocess"].GetValueKind() != JsonValueKind.False;
		var computeStr = (jo["device"]?.GetValue<string>() ?? jo["compute"]?.GetValue<string>() ?? "auto").Trim();

		var models = svc.ScanAsr?.Invoke() ?? new List<AsrModelInfo>();
		// HTTP 默认离线模型（流式包不能 Offline 识别）
		AsrModelInfo model = null;
		if (!string.IsNullOrWhiteSpace(modelName))
			model = models.FirstOrDefault(m =>
				!m.IsStreaming && string.Equals(m.DisplayName, modelName, StringComparison.OrdinalIgnoreCase))
				?? models.FirstOrDefault(m =>
					!m.IsStreaming && Compat.Contains(m.DisplayName, modelName, StringComparison.OrdinalIgnoreCase));
		if (model == null) {
			var opt = getOpts?.Invoke();
			if (!string.IsNullOrEmpty(opt?.AsrModel))
				model = models.FirstOrDefault(m =>
					!m.IsStreaming && string.Equals(m.DisplayName, opt.AsrModel, StringComparison.OrdinalIgnoreCase));
		}
		model ??= models.FirstOrDefault(m => !m.IsStreaming);
		if (model == null) {
			writejson(ctx, 200, err(912, "无可用离线 ASR 模型（流式模型请用热键听写）"));
			return;
		}

		var compute = parsecompute(computeStr);
		string tmpPath = null;
		var t0 = Environment.TickCount;
		try {
			if (audioBytes != null && audioBytes.Length > 0) {
				var ext = guessext(audioBytes, jo["filename"]?.GetValue<string>() ?? ".wav");
				tmpPath = Path.Combine(Path.GetTempPath(), "wpocr_asr_" + Guid.NewGuid().ToString("N") + ext);
				File.WriteAllBytes(tmpPath, audioBytes);
				pathHint = tmpPath;
			}
			if (string.IsNullOrWhiteSpace(pathHint) || !File.Exists(pathHint)) {
				writejson(ctx, 200, err(802, "音频文件不存在"));
				return;
			}

			string text;
			string provider;
			int loadMs, recMs;
			double audioSec;
			lock (svc.AsrGate ?? new object()) {
				var eng = svc.AsrEngine;
				eng.Mode = compute;
				var tLoad = Environment.TickCount;
				eng.LoadModel(model, string.IsNullOrWhiteSpace(lang) ? "auto" : lang, useItn);
				loadMs = Math.Max(0, Environment.TickCount - tLoad);
				provider = eng.Provider;
				var (samples, sr) = AsrAudio.LoadMedia(pathHint);
				audioSec = samples.Length / (double)Math.Max(1, sr);
				var tRec = Environment.TickCount;
				text = eng.Recognize(samples, sr) ?? "";
				recMs = Math.Max(0, Environment.TickCount - tRec);
			}
			if (post)
				text = AsrTextNorm.Postprocess(text ?? "");

			var ms = Math.Max(0, Environment.TickCount - t0);
			writejson(ctx, 200, new JsonObject {
				["code"] = 100,
				["data"] = new JsonObject {
					["text"] = text ?? "",
					["model"] = model.DisplayName,
					["provider"] = provider,
					["sample_rate"] = 16000,
					["audio_sec"] = Math.Round(audioSec, 3),
					["load_ms"] = loadMs,
					["recognize_ms"] = recMs,
					["postprocess"] = post,
				},
				["time"] = ms,
				["timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds(),
			});
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(913, "识别失败: " + ex.Message));
		}
		finally {
			if (tmpPath != null)
				try { File.Delete(tmpPath); } catch { }
		}
	}

	void handlettsmodels(HttpListenerContext ctx) {
		List<TtsModelInfo> list = null;
		try { list = svc?.ScanTts?.Invoke(); } catch (Exception ex) {
			writejson(ctx, 200, err(920, "扫描 TTS 失败: " + ex.Message));
			return;
		}
		list ??= new List<TtsModelInfo>();
		var arr = new JsonArray();
		foreach (var m in list) {
			var speakers = new JsonArray();
			if (m.Speakers != null) {
				foreach (var s in m.Speakers.Take(64)) {
					speakers.Add(new JsonObject {
						["id"] = s.Id,
						["name"] = s.Name ?? "",
						["lang"] = s.Lang ?? "",
						["gender"] = s.Gender ?? "",
					});
				}
			}
			arr.Add(new JsonObject {
				["name"] = m.DisplayName ?? "",
				["type"] = m.Type.ToString(),
				["speakers"] = speakers,
			});
		}
		writejson(ctx, 200, new JsonObject {
			["code"] = 100,
			["data"] = arr,
			["count"] = arr.Count,
		});
	}

	void handletts(HttpListenerContext ctx) {
		if (svc?.TtsEngine == null) {
			writejson(ctx, 200, err(921, "TTS 引擎不可用（Sherpa）"));
			return;
		}
		JsonObject jo;
		try { jo = readjsonbody(ctx.Request); }
		catch (Exception ex) {
			writejson(ctx, 200, err(800, ex.Message));
			return;
		}
		var text = jo["text"]?.GetValue<string>() ?? "";
		if (string.IsNullOrWhiteSpace(text)) {
			writejson(ctx, 200, err(802, "缺少 text"));
			return;
		}
		if (text.Length > 20000) {
			writejson(ctx, 200, err(803, "text 过长（上限 20000 字）"));
			return;
		}

		var modelName = jo["model"]?.GetValue<string>() ?? "";
		var sid = 0;
		if (jo["speaker_id"] != null) sid = asint(jo["speaker_id"], 0);
		else if (jo["sid"] != null) sid = asint(jo["sid"], 0);
		var speed = 1f;
		if (jo["speed"] != null) speed = Compat.Clamp(asfloat(jo["speed"], 1f), 0.5f, 2f);
		var compute = parsecompute(jo["device"]?.GetValue<string>() ?? jo["compute"]?.GetValue<string>() ?? "auto");

		var models = svc.ScanTts?.Invoke() ?? new List<TtsModelInfo>();
		TtsModelInfo model = null;
		if (!string.IsNullOrWhiteSpace(modelName))
			model = models.FirstOrDefault(m =>
				string.Equals(m.DisplayName, modelName, StringComparison.OrdinalIgnoreCase))
				?? models.FirstOrDefault(m =>
					Compat.Contains(m.DisplayName, modelName, StringComparison.OrdinalIgnoreCase));
		if (model == null) {
			var opt = getOpts?.Invoke();
			if (!string.IsNullOrEmpty(opt?.TtsModel))
				model = models.FirstOrDefault(m =>
					string.Equals(m.DisplayName, opt.TtsModel, StringComparison.OrdinalIgnoreCase));
		}
		model ??= models.FirstOrDefault();
		if (model == null) {
			writejson(ctx, 200, err(922, "无可用 TTS 模型"));
			return;
		}

		var t0 = Environment.TickCount;
		try {
			float[] samples;
			int sr;
			string provider;
			lock (svc.TtsGate ?? new object()) {
				var eng = svc.TtsEngine;
				eng.Mode = compute;
				eng.LoadModel(model);
				provider = eng.Provider;
				(samples, sr) = eng.Synthesize(text, sid, speed);
			}
			if (samples == null || samples.Length == 0) {
				writejson(ctx, 200, err(923, "合成结果为空"));
				return;
			}
			var wav = floatstowav(samples, sr);
			var ms = Math.Max(0, Environment.TickCount - t0);
			writejson(ctx, 200, new JsonObject {
				["code"] = 100,
				["data"] = new JsonObject {
					["format"] = "wav",
					["sample_rate"] = sr,
					["samples"] = samples.Length,
					["wav_base64"] = Convert.ToBase64String(wav),
					["model"] = model.DisplayName,
					["speaker_id"] = sid,
					["provider"] = provider,
				},
				["time"] = ms,
				["timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds(),
			});
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(924, "合成失败: " + ex.Message));
		}
	}

	void handleitn(HttpListenerContext ctx) {
		JsonObject jo;
		try { jo = readjsonbody(ctx.Request); }
		catch (Exception ex) {
			writejson(ctx, 200, err(800, ex.Message));
			return;
		}
		var text = jo["text"]?.GetValue<string>() ?? "";
		var t0 = Environment.TickCount;
		var outText = AsrTextNorm.Postprocess(text ?? "");
		var ms = Math.Max(0, Environment.TickCount - t0);
		writejson(ctx, 200, new JsonObject {
			["code"] = 100,
			["data"] = new JsonObject {
				["text"] = outText ?? "",
				["input"] = text ?? "",
				["wetext"] = WetextItn.IsAvailable,
			},
			["time"] = ms,
			["timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds(),
		});
	}

	static JsonObject readjsonbody(HttpListenerRequest req) {
		string body;
		using (var sr = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
			body = sr.ReadToEnd();
		if (string.IsNullOrWhiteSpace(body))
			throw new InvalidOperationException("请求为空。");
		JsonNode root;
		try { root = JsonNode.Parse(body); }
		catch (Exception ex) {
			throw new InvalidOperationException("请求无法解析为 json: " + ex.Message);
		}
		if (root is not JsonObject jo)
			throw new InvalidOperationException("请求体须为 JSON 对象。");
		return jo;
	}

	static TtsComputeMode parsecompute(string s) {
		s = (s ?? "auto").Trim().ToLowerInvariant();
		return s switch {
			"gpu" or "cuda" => TtsComputeMode.Gpu,
			"cpu" => TtsComputeMode.Cpu,
			"igpu" or "dml" or "directml" => TtsComputeMode.Igpu,
			_ => TtsComputeMode.Auto,
		};
	}

	static string guessext(byte[] data, string fallback) {
		if (data != null && data.Length >= 4) {
			// RIFF....WAVE
			if (data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F')
				return ".wav";
			if (data[0] == 0xFF && (data[1] & 0xE0) == 0xE0) return ".mp3";
			if (data[0] == (byte)'f' && data[1] == (byte)'L' && data[2] == (byte)'a' && data[3] == (byte)'C')
				return ".flac";
			if (data[0] == 0x1A && data[1] == 0x45 && data[2] == 0xDF && data[3] == 0xA3) return ".webm";
		}
		var ext = Path.GetExtension(fallback ?? "");
		if (string.IsNullOrEmpty(ext)) ext = ".wav";
		return ext;
	}

	static byte[] floatstowav(float[] samples, int sampleRate) {
		using var ms = new MemoryStream();
		// WaveFileWriter 关闭时才写完头；Dispose 后 ms 仍可读
		using (var writer = new WaveFileWriter(new IgnoreDisposeStream(ms),
			WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1))) {
			writer.WriteSamples(samples, 0, samples.Length);
		}
		return ms.ToArray();
	}

	/// <summary>NAudio WaveFileWriter 会关掉底层流；包装一层忽略 Dispose。</summary>
	sealed class IgnoreDisposeStream : Stream {
		readonly Stream inner;
		public IgnoreDisposeStream(Stream inner) => this.inner = inner;
		public override bool CanRead => inner.CanRead;
		public override bool CanSeek => inner.CanSeek;
		public override bool CanWrite => inner.CanWrite;
		public override long Length => inner.Length;
		public override long Position { get => inner.Position; set => inner.Position = value; }
		public override void Flush() => inner.Flush();
		public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
		public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
		public override void SetLength(long value) => inner.SetLength(value);
		public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
		protected override void Dispose(bool disposing) { /* 不关 inner */ }
	}

	static void writecors(HttpListenerContext ctx, int status) {
		var res = ctx.Response;
		res.StatusCode = status;
		res.Headers["Access-Control-Allow-Origin"] = "*";
		res.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
		res.Headers["Access-Control-Allow-Headers"] = "Content-Type";
		res.ContentLength64 = 0;
		try { res.Close(); } catch { }
	}

	void handleocr(HttpListenerContext ctx) {
		var req = ctx.Request;
		byte[] imageBytes = null;
		JsonObject optNode = null;

		var ctype = req.ContentType ?? "";
		try {
			if (ctype.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase)) {
				(imageBytes, optNode) = readmultipart(req);
			}
			else {
				// JSON（含 text/plain 误标的 json）
				string body;
				using (var sr = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
					body = sr.ReadToEnd();
				if (string.IsNullOrWhiteSpace(body)) {
					writejson(ctx, 200, err(801, "请求为空。"));
					return;
				}
				JsonNode root;
				try {
					root = JsonNode.Parse(body);
				}
				catch (Exception ex) {
					writejson(ctx, 200, err(800, $"请求无法解析为json。 {ex.Message}"));
					return;
				}
				if (root is not JsonObject jo) {
					writejson(ctx, 200, err(801, "请求为空。"));
					return;
				}
				if (jo["base64"] == null || jo["base64"].GetValueKind() == JsonValueKind.Null) {
					writejson(ctx, 200, err(802, "请求中缺少 base64 字段。"));
					return;
				}
				var b64 = jo["base64"]?.GetValue<string>() ?? "";
				if (string.IsNullOrWhiteSpace(b64)) {
					writejson(ctx, 200, err(802, "请求中缺少 base64 字段。"));
					return;
				}
				try {
					imageBytes = decodebase64(b64);
				}
				catch (Exception ex) {
					writejson(ctx, 200, err(806, $"base64 解码失败: {ex.Message}"));
					return;
				}
				if (jo["options"] != null && jo["options"].GetValueKind() != JsonValueKind.Null) {
					if (jo["options"] is not JsonObject) {
						writejson(ctx, 200, err(803, "请求中 options 字段必须为字典。"));
						return;
					}
					optNode = jo["options"].AsObject();
				}
			}
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(800, $"请求解析失败: {ex.Message}"));
			return;
		}

		if (imageBytes == null || imageBytes.Length == 0) {
			writejson(ctx, 200, err(802, "未提供图片（base64 或 multipart 文件）。"));
			return;
		}

		// 合并默认 options
		var optMap = mergedefaults(optNode);
		string format;
		OcrOptions ocrOpt;
		bool wantBarcode;
		try {
			format = getstr(optMap, "data.format", "dict");
			if (format != "dict" && format != "text") format = "dict";
			ocrOpt = buildocroptions(optMap);
			// ocr.barcode / ocr.qr / ocr.codes：识别二维码与各类条码
			wantBarcode = getbool(optMap, "ocr.barcode", false)
				|| getbool(optMap, "ocr.qr", false)
				|| getbool(optMap, "ocr.codes", false);
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(804, $"options 解释失败。 {ex.Message}"));
			return;
		}

		var t0 = Environment.TickCount;
		OcrResult result;
		try {
			result = runocr(imageBytes, ocrOpt);
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(901, $"识别失败: {ex.Message}"));
			return;
		}

		QrResult codes = null;
		if (wantBarcode) {
			try {
				codes = QrScan.Run(imageBytes);
			}
			catch (Exception ex) {
				writejson(ctx, 200, err(901, $"条码识别失败: {ex.Message}"));
				return;
			}
		}
		var ms = Math.Max(0, Environment.TickCount - t0);

		var hasText = result?.Lines != null && result.Lines.Count > 0;
		var hasCodes = codes != null && codes.DecodedCount > 0;
		if (!hasText && !hasCodes) {
			var empty = new JsonObject {
				["code"] = 101,
				["data"] = wantBarcode ? "未检测到文字或条码" : "未检测到文字",
				["time"] = ms,
				["timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds(),
			};
			if (wantBarcode)
				empty["barcodes"] = new JsonArray();
			writejson(ctx, 200, empty);
			return;
		}

		JsonArray barcodeArr = null;
		if (wantBarcode)
			barcodeArr = buildbarcodearray(codes);

		if (format == "text") {
			string text;
			if (hasText)
				text = string.Join("\n", result.Lines.Select(l => l.Text ?? ""));
			else
				text = codes?.FullText ?? "";
			var jo = new JsonObject {
				["code"] = 100,
				["data"] = text,
				["time"] = ms,
				["timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds(),
			};
			if (barcodeArr != null)
				jo["barcodes"] = barcodeArr;
			writejson(ctx, 200, jo);
			return;
		}

		// dict：Umi 风格 list[{text,score,box,end}]
		var arr = new JsonArray();
		if (hasText) {
			for (int i = 0; i < result.Lines.Count; i++) {
				var ln = result.Lines[i];
				var box = new JsonArray();
				if (ln.Box != null) {
					foreach (var p in ln.Box)
						box.Add(new JsonArray { Math.Round(p.X, 1), Math.Round(p.Y, 1) });
				}
				arr.Add(new JsonObject {
					["text"] = ln.Text ?? "",
					["score"] = Math.Round(ln.Score, 8),
					["box"] = box,
					// 与 Umi 一致：行末换行（最后一行也给 \n，拼接时更稳）
					["end"] = "\n",
				});
			}
		}
		var resp = new JsonObject {
			["code"] = 100,
			["data"] = arr,
			["time"] = ms,
			["timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds(),
		};
		if (barcodeArr != null)
			resp["barcodes"] = barcodeArr;
		writejson(ctx, 200, resp);
	}

	static JsonArray buildbarcodearray(QrResult codes) {
		var arr = new JsonArray();
		if (codes?.Codes == null) return arr;
		foreach (var c in codes.Codes) {
			if (c == null || string.IsNullOrEmpty(c.Text)) continue;
			var box = new JsonArray();
			if (c.Box != null) {
				foreach (var p in c.Box)
					box.Add(new JsonArray { Math.Round(p.X, 1), Math.Round(p.Y, 1) });
			}
			arr.Add(new JsonObject {
				["type"] = string.IsNullOrEmpty(c.Type) ? "UNKNOWN" : c.Type,
				["text"] = c.Text ?? "",
				["box"] = box,
			});
		}
		return arr;
	}

	OcrResult runocr(byte[] imageBytes, OcrOptions o) {
		using var mat = Cv2.ImDecode(imageBytes, ImreadModes.Color);
		if (mat == null || mat.Empty())
			throw new InvalidOperationException("无法解码图片（支持 png/jpg/bmp/webp 等）");
		return runner.Run(o, mat);
	}

	OcrOptions buildocroptions(Dictionary<string, JsonNode> map) {
		var baseOpt = getOpts() ?? new OcrOptions();
		// 浅拷贝，避免污染主窗口
		var o = new OcrOptions {
			ModelPackId = baseOpt.ModelPackId,
			ModelVariant = baseOpt.ModelVariant,
			ModelsDir = baseOpt.ModelsDir,
			Device = baseOpt.Device,
			DetLimitSideLen = baseOpt.DetLimitSideLen,
			DetPadding = baseOpt.DetPadding,
			DetThresh = baseOpt.DetThresh,
			DetBoxThresh = baseOpt.DetBoxThresh,
			DetUnclipRatio = baseOpt.DetUnclipRatio,
			DetUseDilation = baseOpt.DetUseDilation,
			RecImgH = baseOpt.RecImgH,
			RecMaxWidth = baseOpt.RecMaxWidth,
			RecAbsMaxWidth = baseOpt.RecAbsMaxWidth,
			RecBatchNum = baseOpt.RecBatchNum,
			UseCls = baseOpt.UseCls,
		};

		// ocr.angle → UseCls
		if (map.TryGetValue("ocr.angle", out var ang) && ang != null) {
			if (ang.GetValueKind() == JsonValueKind.True) o.UseCls = true;
			else if (ang.GetValueKind() == JsonValueKind.False) o.UseCls = false;
			else if (ang.GetValueKind() == JsonValueKind.String) {
				var s = ang.GetValue<string>();
				if (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1") o.UseCls = true;
				else if (s.Equals("false", StringComparison.OrdinalIgnoreCase) || s == "0") o.UseCls = false;
			}
		}

		// ocr.maxSideLen
		if (map.TryGetValue("ocr.maxSideLen", out var msl) && msl != null) {
			var n = asint(msl, o.DetLimitSideLen);
			if (n > 0) o.DetLimitSideLen = Compat.Clamp(n, 320, 4096);
		}

		// 扩展：本项目额外支持的设备/阈值（非 Umi 必填，可选）
		if (map.TryGetValue("ocr.device", out var dev) && dev != null) {
			var s = dev.ToString().Trim().Trim('"');
			if (s.Equals("cpu", StringComparison.OrdinalIgnoreCase)
				|| s.Equals("auto", StringComparison.OrdinalIgnoreCase)) o.Device = OcrDevice.Cpu;
			else if (s.Equals("gpu", StringComparison.OrdinalIgnoreCase)
				|| s.Equals("cuda", StringComparison.OrdinalIgnoreCase)
				|| s.Equals("nvidia", StringComparison.OrdinalIgnoreCase)) o.Device = OcrDevice.Gpu;
			else if (s.Equals("intel", StringComparison.OrdinalIgnoreCase)
				|| s.Equals("intelgpu", StringComparison.OrdinalIgnoreCase)
				|| s.Equals("dml", StringComparison.OrdinalIgnoreCase)
				|| s.Equals("directml", StringComparison.OrdinalIgnoreCase)) o.Device = OcrDevice.IntelGpu;
		}
		if (map.TryGetValue("ocr.detThresh", out var dt) && dt != null)
			o.DetThresh = Compat.Clamp(asfloat(dt, o.DetThresh), 0.05f, 0.95f);
		if (map.TryGetValue("ocr.detBoxThresh", out var dbt) && dbt != null)
			o.DetBoxThresh = Compat.Clamp(asfloat(dbt, o.DetBoxThresh), 0.05f, 0.95f);

		// ocr.language：尽量匹配变体标题
		if (map.TryGetValue("ocr.language", out var lang) && lang != null) {
			var title = lang.GetValueKind() == JsonValueKind.String
				? lang.GetValue<string>()
				: lang.ToString();
			if (!string.IsNullOrWhiteSpace(title)) {
				var packs = ModelCatalog.Scan();
				foreach (var p in packs) {
					var hit = p.Variants.FirstOrDefault(v =>
						string.Equals(v.Title, title, StringComparison.OrdinalIgnoreCase)
						|| Compat.Contains(v.Title, title, StringComparison.OrdinalIgnoreCase)
						|| Compat.Contains(title, v.Title, StringComparison.OrdinalIgnoreCase));
					if (hit != null) {
						o.ModelPackId = p.Id;
						o.ModelVariant = hit.Title;
						o.ModelsDir = p.Dir;
						break;
					}
				}
			}
		}

		return o;
	}

	static Dictionary<string, JsonNode> mergedefaults(JsonObject optNode) {
		var def = getdefaulmap();
		if (optNode != null) {
			foreach (var kv in optNode) {
				if (kv.Value != null)
					def[kv.Key] = kv.Value.DeepClone();
			}
		}
		return def;
	}

	static Dictionary<string, JsonNode> getdefaulmap() {
		var o = buildoptionsobj();
		var map = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
		foreach (var kv in o) {
			if (kv.Value is JsonObject jo && jo["default"] != null)
				map[kv.Key] = jo["default"].DeepClone();
			else
				map[kv.Key] = kv.Value?.DeepClone();
		}
		return map;
	}

	static JsonObject buildoptionsobj() {
		// 与 Umi get_options 结构相近：每项含 title / default / optionsList 等
		return new JsonObject {
			["ocr.angle"] = new JsonObject {
				["title"] = "纠正文本方向",
				["toolTip"] = "启用方向分类（cls）",
				["default"] = true,
				["optionsList"] = new JsonArray {
					new JsonArray { true, "启用" },
					new JsonArray { false, "禁用" },
				},
			},
			["ocr.maxSideLen"] = new JsonObject {
				["title"] = "检测边长上限",
				["toolTip"] = "图像最长边缩放到此值再检测，越大越准但越慢",
				["default"] = 1024,
				["type"] = "int",
			},
			["ocr.language"] = new JsonObject {
				["title"] = "识别语言/模型",
				["toolTip"] = "对应模型变体标题，如「简体中文」",
				["default"] = "",
				["type"] = "string",
			},
			["ocr.device"] = new JsonObject {
				["title"] = "推理设备",
				["toolTip"] = "cpu / gpu / intel（扩展项，Umi 客户端可忽略）",
				["default"] = "cpu",
				["optionsList"] = new JsonArray {
					new JsonArray { "cpu", "CPU" },
					new JsonArray { "gpu", "GPU (CUDA)" },
					new JsonArray { "intel", "核显 (DirectML)" },
				},
			},
			["tbpu.parser"] = new JsonObject {
				["title"] = "排版解析方案",
				["toolTip"] = "当前实现按检测顺序输出；参数保留兼容",
				["default"] = "multi_line",
				["optionsList"] = new JsonArray {
					new JsonArray { "multi_para", "多栏-按自然段换行" },
					new JsonArray { "multi_line", "多栏-总是换行" },
					new JsonArray { "multi_none", "多栏-无换行" },
					new JsonArray { "none", "不做处理" },
				},
			},
			["data.format"] = new JsonObject {
				["title"] = "数据返回格式",
				["toolTip"] = "返回值中 data 字段的格式",
				["default"] = "dict",
				["optionsList"] = new JsonArray {
					new JsonArray { "dict", "含有位置等信息的原始字典" },
					new JsonArray { "text", "纯文本" },
				},
			},
			["ocr.barcode"] = new JsonObject {
				["title"] = "识别条码/二维码",
				["toolTip"] = "同时扫描 QR / EAN / Code128 / DataMatrix 等；结果写入 barcodes[{type,text,box}]。"
					+ " 别名：ocr.qr、ocr.codes",
				["default"] = false,
				["optionsList"] = new JsonArray {
					new JsonArray { true, "启用" },
					new JsonArray { false, "禁用" },
				},
			},
		};
	}

	// ───────── multipart ─────────

	static (byte[] image, JsonObject options) readmultipart(HttpListenerRequest req) {
		var ctype = req.ContentType ?? "";
		var boundary = extractboundary(ctype);
		if (string.IsNullOrEmpty(boundary))
			throw new InvalidOperationException("multipart 缺少 boundary");

		using var ms = new MemoryStream();
		req.InputStream.CopyTo(ms);
		var raw = ms.ToArray();
		var parts = parseparts(raw, boundary);

		byte[] image = null;
		JsonObject options = null;
		string b64 = null;

		foreach (var p in parts) {
			var name = (p.Name ?? "").ToLowerInvariant();
			var fn = p.FileName ?? "";
			var isFile = !string.IsNullOrEmpty(fn)
				|| name is "file" or "image" or "img" or "upload" or "pic";
			// 有 Content-Type 且像图片
			if (!isFile && p.ContentType != null
				&& p.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
				isFile = true;

			if (isFile && image == null && p.Data != null && p.Data.Length > 0) {
				image = p.Data;
				continue;
			}

			var text = p.Text ?? "";
			if (name is "base64" or "image_base64" or "img_base64") {
				b64 = text.Trim();
				continue;
			}
			if (name is "options" or "option" or "ocr_options") {
				if (!string.IsNullOrWhiteSpace(text)) {
					options = parseoptionsfield(text);
				}
				continue;
			}
			// 单个 option 字段：ocr.angle / data.format 等
			if (((name)?.IndexOf('.') ?? -1) >= 0 && !string.IsNullOrWhiteSpace(text)) {
				options ??= new JsonObject();
				options[p.Name] = tryparsescalar(text);
			}
		}

		if (image == null && !string.IsNullOrWhiteSpace(b64))
			image = decodebase64(b64);

		return (image, options);
	}

	static JsonObject parseoptionsfield(string text) {
		text = (text ?? "").Trim();
		if (text.Length == 0) return null;
		try {
			var node = JsonNode.Parse(text);
			if (node is JsonObject jo) return jo;
			throw new InvalidOperationException("options 字段必须为字典。");
		}
		catch (JsonException) {
			// 兼容 data.format=text&ocr.angle=true 或单行 key=value
			var jo = new JsonObject();
			foreach (var piece in text.Split(new[] { '&', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
				var eq = piece.IndexOf('=');
				if (eq <= 0) continue;
				var k = piece[..eq].Trim();
				var v = piece[(eq + 1)..].Trim().Trim('"').Trim('\'');
				if (k.Length == 0) continue;
				jo[k] = tryparsescalar(v);
			}
			if (jo.Count == 0)
				throw new InvalidOperationException($"options 无法解析: {text}");
			return jo;
		}
	}

	static JsonNode tryparsescalar(string s) {
		s = s.Trim();
		if (s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
		if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
		if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
		if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
		// 尝试 JSON
		if ((s.StartsWith("{") && s.EndsWith("}")) || (s.StartsWith("[") && s.EndsWith("]"))) {
			try { return JsonNode.Parse(s); } catch { }
		}
		return s;
	}

	static string extractboundary(string contentType) {
		// multipart/form-data; boundary=----xxx
		foreach (var part in contentType.Split(';')) {
			var p = part.Trim();
			if (p.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase)) {
				var b = p["boundary=".Length..].Trim().Trim('"');
				return b;
			}
		}
		return null;
	}

	sealed class MpPart {
		public string Name;
		public string FileName;
		public string ContentType;
		public byte[] Data;
		public string Text;
	}

	static List<MpPart> parseparts(byte[] raw, string boundary) {
		var list = new List<MpPart>();
		var sep = Encoding.UTF8.GetBytes("--" + boundary);
		var indices = findall(raw, sep);
		if (indices.Count < 2) return list;

		for (int i = 0; i < indices.Count - 1; i++) {
			var start = indices[i] + sep.Length;
			// 跳过 \r\n 或 \n
			if (start < raw.Length && raw[start] == (byte)'\r') start++;
			if (start < raw.Length && raw[start] == (byte)'\n') start++;
			// 结束标记 --
			if (start < raw.Length && raw[start] == (byte)'-') continue;

			var end = indices[i + 1];
			// 去掉尾部 \r\n
			var bodyEnd = end;
			if (bodyEnd >= 2 && raw[bodyEnd - 2] == (byte)'\r' && raw[bodyEnd - 1] == (byte)'\n')
				bodyEnd -= 2;
			else if (bodyEnd >= 1 && raw[bodyEnd - 1] == (byte)'\n')
				bodyEnd -= 1;

			if (bodyEnd <= start) continue;
			var slice = new byte[bodyEnd - start];
			Buffer.BlockCopy(raw, start, slice, 0, slice.Length);

			// 头与体以空行分隔
			var split = findheaderend(slice);
			if (split < 0) continue;
			var headerBytes = new byte[split];
			Buffer.BlockCopy(slice, 0, headerBytes, 0, split);
			var header = Encoding.UTF8.GetString(headerBytes);
			var dataStart = split;
			// 跳过 \r\n\r\n 或 \n\n
			if (dataStart + 3 < slice.Length && slice[dataStart] == (byte)'\r')
				dataStart += 4; // \r\n\r\n
			else
				dataStart += 2; // \n\n
			if (dataStart > slice.Length) dataStart = slice.Length;
			var dataLen = slice.Length - dataStart;
			var data = dataLen > 0 ? new byte[dataLen] : Array.Empty<byte>();
			if (dataLen > 0) Buffer.BlockCopy(slice, dataStart, data, 0, dataLen);

			var part = new MpPart { Data = data };
			foreach (var line in header.Replace("\r\n", "\n").Split('\n')) {
				var t = line.Trim();
				if (t.StartsWith("Content-Disposition:", StringComparison.OrdinalIgnoreCase)) {
					part.Name = extractcd(t, "name");
					part.FileName = extractcd(t, "filename");
				}
				else if (t.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase)) {
					part.ContentType = t["Content-Type:".Length..].Trim();
				}
			}
			// 非文件字段转文本
			if (string.IsNullOrEmpty(part.FileName)) {
				try { part.Text = Encoding.UTF8.GetString(data); }
				catch { part.Text = ""; }
			}
			list.Add(part);
		}
		return list;
	}

	static string extractcd(string line, string key) {
		// name="file"; filename="a.png"
		var keyEq = key + "=";
		var idx = line.IndexOf(keyEq, StringComparison.OrdinalIgnoreCase);
		if (idx < 0) return null;
		var rest = line[(idx + keyEq.Length)..].Trim();
		if (rest.Length == 0) return "";
		if (rest[0] == '"') {
			var end = rest.IndexOf('"', 1);
			return end > 0 ? rest[1..end] : rest.Trim('"');
		}
		var semi = rest.IndexOf(';');
		return (semi >= 0 ? rest[..semi] : rest).Trim();
	}

	static int findheaderend(byte[] slice) {
		for (int i = 0; i + 3 < slice.Length; i++) {
			if (slice[i] == (byte)'\r' && slice[i + 1] == (byte)'\n'
				&& slice[i + 2] == (byte)'\r' && slice[i + 3] == (byte)'\n')
				return i;
		}
		for (int i = 0; i + 1 < slice.Length; i++) {
			if (slice[i] == (byte)'\n' && slice[i + 1] == (byte)'\n')
				return i;
		}
		return -1;
	}

	static List<int> findall(byte[] data, byte[] pat) {
		var list = new List<int>();
		if (pat.Length == 0 || data.Length < pat.Length) return list;
		for (int i = 0; i <= data.Length - pat.Length; i++) {
			var ok = true;
			for (int j = 0; j < pat.Length; j++) {
				if (data[i + j] != pat[j]) { ok = false; break; }
			}
			if (ok) list.Add(i);
		}
		return list;
	}

	// ───────── helpers ─────────

	static byte[] decodebase64(string b64) {
		b64 = (b64 ?? "").Trim();
		// data:image/png;base64,xxxx
		var comma = b64.IndexOf(',');
		if (b64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
			b64 = b64[(comma + 1)..];
		b64 = b64.Replace("\r", "").Replace("\n", "").Replace(" ", "");
		return Convert.FromBase64String(b64);
	}

	static string getstr(Dictionary<string, JsonNode> map, string key, string def) {
		if (!map.TryGetValue(key, out var n) || n == null) return def;
		if (n.GetValueKind() == JsonValueKind.String) return n.GetValue<string>() ?? def;
		return n.ToString() ?? def;
	}

	static bool getbool(Dictionary<string, JsonNode> map, string key, bool def) {
		if (!map.TryGetValue(key, out var n) || n == null) return def;
		return asbool(n, def);
	}

	static bool asbool(JsonNode n, bool def) {
		if (n == null) return def;
		try {
			return n.GetValueKind() switch {
				JsonValueKind.True => true,
				JsonValueKind.False => false,
				JsonValueKind.Number => n.GetValue<double>() != 0,
				JsonValueKind.String => parseboolstr(n.GetValue<string>(), def),
				_ => def,
			};
		}
		catch { return def; }
	}

	static bool parseboolstr(string s, bool def) {
		if (string.IsNullOrWhiteSpace(s)) return def;
		s = s.Trim();
		if (s.Equals("true", StringComparison.OrdinalIgnoreCase)
			|| s.Equals("yes", StringComparison.OrdinalIgnoreCase)
			|| s.Equals("on", StringComparison.OrdinalIgnoreCase)
			|| s == "1") return true;
		if (s.Equals("false", StringComparison.OrdinalIgnoreCase)
			|| s.Equals("no", StringComparison.OrdinalIgnoreCase)
			|| s.Equals("off", StringComparison.OrdinalIgnoreCase)
			|| s == "0") return false;
		return def;
	}

	static int asint(JsonNode n, int def) {
		if (n == null) return def;
		try {
			return n.GetValueKind() switch {
				JsonValueKind.Number => n.GetValue<int>(),
				JsonValueKind.String => int.TryParse(n.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def,
				_ => def,
			};
		}
		catch { return def; }
	}

	static float asfloat(JsonNode n, float def) {
		if (n == null) return def;
		try {
			return n.GetValueKind() switch {
				JsonValueKind.Number => (float)n.GetValue<double>(),
				JsonValueKind.String => float.TryParse(n.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def,
				_ => def,
			};
		}
		catch { return def; }
	}

	static JsonObject err(int code, string msg) => new() {
		["code"] = code,
		["data"] = msg ?? "",
	};

	static void writejson(HttpListenerContext ctx, int httpStatus, JsonNode body) {
		var bytes = Encoding.UTF8.GetBytes(body.ToJsonString());
		var res = ctx.Response;
		res.StatusCode = httpStatus;
		res.ContentType = "application/json; charset=utf-8";
		res.ContentEncoding = Encoding.UTF8;
		res.ContentLength64 = bytes.Length;
		// CORS：方便本地网页调用
		res.Headers["Access-Control-Allow-Origin"] = "*";
		res.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
		res.Headers["Access-Control-Allow-Headers"] = "Content-Type";
		try {
			res.OutputStream.Write(bytes, 0, bytes.Length);
		}
		finally {
			try { res.OutputStream.Close(); } catch { }
			try { res.Close(); } catch { }
		}
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		Stop();
		disposeface();
		// runner 由 MainWindow 持有并释放，此处不 Dispose
	}
}
