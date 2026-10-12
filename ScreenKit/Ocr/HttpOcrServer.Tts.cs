using System.Net;
using System.Speech.Synthesis;
using System.Text.Json.Nodes;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>HTTP TTS：Sherpa + SAPI + Windows（WinRT）+ Edge 在线语音。</summary>
sealed partial class HttpOcrServer {
	const int VoiceCacheMs = 30_000;
	readonly object sapiGate = new();
	readonly object winRtGate = new();
	readonly object edgeGate = new();
	SapiTts httpSapi;
	WinRtTts httpWinRt;
	EdgeOnlineTts httpEdge;
	List<SapiVoiceItem> cachedSapiVoices;
	List<SapiVoiceItem> cachedWinRtVoices;
	List<SapiVoiceItem> cachedEdgeVoices;
	int lastVoiceScan;
	readonly object ttsModelsGate = new();
	JsonArray cachedTtsModels;

	void handlettsengines(SockCtx ctx) {
		var arr = new JsonArray {
			ttsengine("sherpa", "Sherpa"),
			ttsengine("sapi", "SAPI"),
			ttsengine("winrt", "Windows"),
			ttsengine("edge", "Edge Online"),
		};
		writejson(ctx, 200, new JsonObject {
			["code"] = 100,
			["data"] = arr,
			["count"] = arr.Count,
		});
	}

	static JsonObject ttsengine(string id, string name) => new() {
		["engine"] = id,
		["name"] = name,
	};

	void handlettsmodels(SockCtx ctx) {
		var engineRaw = query(ctx.Request, "engine");
		string engine = null;
		if (!string.IsNullOrWhiteSpace(engineRaw)) {
			if (!tryparseengine(engineRaw, out var kind) || kind == null) {
				writejson(ctx, 200, err(802, "未知 engine（sherpa / sapi / winrt / edge）"));
				return;
			}
			engine = engineid(kind.Value);
		}
		var refresh = parseboolstr(query(ctx.Request, "refresh"), false);
		JsonArray arr;
		var fromCache = false;
		try { arr = ttsmodels(engine, refresh, out fromCache); }
		catch (Exception ex) {
			writejson(ctx, 200, err(920, "扫描 TTS 失败: " + ex.Message));
			return;
		}
		writejson(ctx, 200, new JsonObject {
			["code"] = 100,
			["data"] = arr,
			["count"] = arr.Count,
			["cached"] = fromCache,
		});
	}

	JsonArray ttsmodels(string engine, bool refresh, out bool fromCache) {
		if (!refresh) {
			var cat = svc?.TtsCatalog?.Invoke();
			if (cat != null) {
				fromCache = true;
				return buildfromcatalog(cat, engine);
			}
			lock (ttsModelsGate) {
				if (cachedTtsModels != null) {
					fromCache = true;
					return engine == null ? clonejson(cachedTtsModels) : filterengine(cachedTtsModels, engine);
				}
			}
		}
		var built = buildttsmodels(engine);
		fromCache = false;
		if (engine != null) return built;
		lock (ttsModelsGate) {
			cachedTtsModels = clonejson(built);
		}
		return built;
	}

	static JsonArray clonejson(JsonArray arr) {
		return JsonNode.Parse(arr.ToJsonString()) as JsonArray ?? new JsonArray();
	}

	static JsonArray filterengine(JsonArray src, string engine) {
		var arr = new JsonArray();
		foreach (var n in src) {
			if (n is not JsonObject o) continue;
			var id = o["engine"]?.GetValue<string>() ?? "";
			if (!string.Equals(id, engine, StringComparison.OrdinalIgnoreCase)) continue;
			arr.Add(JsonNode.Parse(o.ToJsonString()));
		}
		return arr;
	}

	static string engineid(TtsEngineKind kind) => kind switch {
		TtsEngineKind.Sapi => "sapi",
		TtsEngineKind.WinRt => "winrt",
		TtsEngineKind.Edge => "edge",
		_ => "sherpa",
	};

	void handlettsdevices(SockCtx ctx) {
		try {
			var list = TtsSpeak.Devices();
			var arr = new JsonArray();
			foreach (var d in list) arr.Add(outdevjson(d));
			writejson(ctx, 200, new JsonObject {
				["code"] = 100,
				["data"] = arr,
				["count"] = arr.Count,
			});
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(924, "枚举播放设备失败: " + ex.Message));
		}
	}

	void handlettsspeak(SockCtx ctx) {
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
		if (!tryplaymode(jo, out var immediate, out var playErr)) {
			writejson(ctx, 200, err(802, playErr));
			return;
		}
		if (!TtsSpeak.TryPick(readoutdevs(jo), out var devs, out var devErr)) {
			writejson(ctx, 200, err(802, devErr));
			return;
		}
		var t0 = Environment.TickCount;
		if (!maketts(jo, text, out var made, out var bad)) {
			writejson(ctx, 200, bad);
			return;
		}
		var ids = new List<string>();
		var arr = new JsonArray();
		foreach (var d in devs) {
			ids.Add(d.Id);
			arr.Add(outdevjson(d));
		}
		var ahead = TtsSpeak.Enqueue(made.Samples, made.Sr, ids, immediate);
		var ms = Math.Max(0, Environment.TickCount - t0);
		writejson(ctx, 200, new JsonObject {
			["code"] = 100,
			["data"] = new JsonObject {
				["play"] = immediate ? "now" : "queue",
				["ahead"] = ahead,
				["devices"] = arr,
				["sample_rate"] = made.Sr,
				["samples"] = made.Samples.Length,
				["engine"] = made.Engine,
				["model"] = made.Model,
				["voice"] = made.Voice,
				["speaker_id"] = made.Sid,
				["provider"] = made.Provider,
			},
			["time"] = ms,
			["timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds(),
		});
	}

	static JsonObject outdevjson(TtsOutDev d) => new() {
		["id"] = d.Id ?? "",
		["name"] = d.Name ?? "",
		["index"] = d.Index,
		["default"] = d.Default,
	};

	static bool tryplaymode(JsonObject jo, out bool immediate, out string err) {
		immediate = false;
		err = "";
		JsonNode n = null;
		if (jo.ContainsKey("play")) n = jo["play"];
		else if (jo.ContainsKey("mode")) n = jo["mode"];
		var s = "";
		if (n is JsonValue v && v.TryGetValue<string>(out var str)) s = str ?? "";
		s = s.Trim().ToLowerInvariant();
		if (s.Length == 0 || s is "queue" or "wait" or "排队") return true;
		if (s is "now" or "immediate" or "cut" or "立即" or "立即播放") {
			immediate = true;
			return true;
		}
		err = "play 只能是 queue（排队）或 now（立即）";
		return false;
	}

	static List<string> readoutdevs(JsonObject jo) {
		var want = new List<string>();
		JsonNode n = null;
		if (jo.ContainsKey("devices")) n = jo["devices"];
		else if (jo.ContainsKey("outputs")) n = jo["outputs"];
		if (n is JsonArray arr) {
			foreach (var one in arr) adddevtoken(want, one);
		}
		else adddevtoken(want, n);
		return want;
	}

	static void adddevtoken(List<string> want, JsonNode n) {
		if (n is not JsonValue v) return;
		if (v.TryGetValue<string>(out var s)) {
			if (string.IsNullOrWhiteSpace(s)) return;
			foreach (var part in s.Split(new[] { ',', '，', ';' }, StringSplitOptions.RemoveEmptyEntries)) {
				var t = part.Trim();
				if (t.Length > 0) want.Add(t);
			}
			return;
		}
		if (v.TryGetValue<int>(out var i)) want.Add(i.ToString());
	}

	void handletts(SockCtx ctx) {
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
		var t0 = Environment.TickCount;
		if (!maketts(jo, text, out var made, out var bad)) {
			writejson(ctx, 200, bad);
			return;
		}
		var wav = floatstowav(made.Samples, made.Sr);
		var ms = Math.Max(0, Environment.TickCount - t0);
		writejson(ctx, 200, new JsonObject {
			["code"] = 100,
			["data"] = new JsonObject {
				["format"] = "wav",
				["sample_rate"] = made.Sr,
				["samples"] = made.Samples.Length,
				["wav_base64"] = Convert.ToBase64String(wav),
				["engine"] = made.Engine,
				["model"] = made.Model,
				["voice"] = made.Voice,
				["speaker_id"] = made.Sid,
				["provider"] = made.Provider,
			},
			["time"] = ms,
			["timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds(),
		});
	}

	bool maketts(JsonObject jo, string text, out TtsMade made, out JsonObject bad) {
		made = null;
		bad = null;
		var engineRaw = jo["engine"]?.GetValue<string>() ?? "";
		var modelName = jo["model"]?.GetValue<string>() ?? "";
		var voice = jo["voice"]?.GetValue<string>()
			?? jo["speaker"]?.GetValue<string>() ?? "";
		int? sid = null;
		if (jo["speaker_id"] != null) sid = asint(jo["speaker_id"], 0);
		else if (jo["sid"] != null) sid = asint(jo["sid"], 0);
		var sidVal = sid ?? 0;
		var speed = 1f;
		if (jo["speed"] != null) speed = Compat.Clamp(asfloat(jo["speed"], 1f), 0.5f, 2f);
		var volume = 100;
		if (jo["volume"] != null) volume = Compat.Clamp(asint(jo["volume"], 100), 0, 100);
		var compute = parsecompute(jo["device"]?.GetValue<string>() ?? jo["compute"]?.GetValue<string>() ?? "auto");

		if (!tryparseengine(engineRaw, out var kind)) {
			bad = err(925, "未知 engine（sherpa / sapi / winrt / edge）");
			return false;
		}
		if (kind == null)
			kind = inferengine(modelName, voice);
		if (kind == TtsEngineKind.Sherpa && svc?.TtsEngine == null) {
			bad = err(921, "TTS 引擎不可用（Sherpa）");
			return false;
		}

		try {
			float[] samples;
			int sr;
			string provider;
			string modelOut;
			string voiceOut;
			int sidOut;
			string engOut;
			if (kind == TtsEngineKind.Sapi)
				(samples, sr, provider, modelOut, voiceOut, sidOut, engOut) =
					synthsapi(text, voice, sid, speed, volume);
			else if (kind == TtsEngineKind.WinRt)
				(samples, sr, provider, modelOut, voiceOut, sidOut, engOut) =
					synthwinrt(text, voice, sid, speed, volume);
			else if (kind == TtsEngineKind.Edge)
				(samples, sr, provider, modelOut, voiceOut, sidOut, engOut) =
					synthedge(text, voice, sid, speed, volume);
			else
				(samples, sr, provider, modelOut, voiceOut, sidOut, engOut) =
					synthsherpa(text, modelName, sidVal, speed, compute);

			if (samples == null || samples.Length == 0) {
				bad = err(923, "合成结果为空");
				return false;
			}
			made = new TtsMade {
				Samples = samples,
				Sr = sr,
				Provider = provider,
				Model = modelOut,
				Voice = voiceOut,
				Sid = sidOut,
				Engine = engOut,
			};
			return true;
		}
		catch (InvalidOperationException ex) {
			var msg = ex.Message ?? "";
			var code = msg.IndexOf("无可用", StringComparison.Ordinal) >= 0
				|| msg.IndexOf("未找到", StringComparison.Ordinal) >= 0
				|| msg.IndexOf("引擎不可用", StringComparison.Ordinal) >= 0
				? 922 : 924;
			bad = err(code, msg);
			return false;
		}
		catch (Exception ex) {
			bad = err(924, "合成失败: " + ex.Message);
			return false;
		}
	}

	sealed class TtsMade {
		public float[] Samples;
		public int Sr;
		public string Provider;
		public string Model;
		public string Voice;
		public int Sid;
		public string Engine;
	}

	JsonArray buildttsmodels(string only) {
		var arr = new JsonArray();
		var all = string.IsNullOrEmpty(only);
		if (all || only == "sherpa") {
			List<TtsModelInfo> list = null;
			try { list = svc?.ScanTts?.Invoke(); } catch { }
			addsherpa(arr, list);
		}
		if (all || only == "sapi" || only == "winrt") refreshvoices();
		if (all || only == "sapi")
			addsysmodel(arr, "SAPI", "sapi", "Sapi", cachedSapiVoices);
		if (all || only == "winrt")
			addsysmodel(arr, "Windows", "winrt", "WinRt", cachedWinRtVoices);
		if (all || only == "edge") {
			cachedEdgeVoices ??= listedgevoices();
			addsysmodel(arr, "Edge Online", "edge", "Edge", cachedEdgeVoices);
		}
		return arr;
	}

	JsonArray buildfromcatalog(TtsUiCatalog cat, string only) {
		var arr = new JsonArray();
		var all = string.IsNullOrEmpty(only);
		if (all || only == "sherpa")
			addsherpa(arr, cat?.Sherpa);
		if (all || only == "sapi")
			addsysmodel(arr, "SAPI", "sapi", "Sapi", cat?.Sapi);
		if (all || only == "winrt")
			addsysmodel(arr, "Windows", "winrt", "WinRt", cat?.WinRt);
		if (all || only == "edge")
			addsysmodel(arr, "Edge Online", "edge", "Edge", cat?.Edge);
		return arr;
	}

	static void addsherpa(JsonArray arr, List<TtsModelInfo> list) {
		if (list == null) return;
		foreach (var m in list) {
			var speakers = new JsonArray();
			if (m.Speakers != null) {
				foreach (var s in m.Speakers) {
					speakers.Add(new JsonObject {
						["id"] = s.Id,
						["name"] = s.Name ?? "",
						["label"] = s.DisplayName ?? "",
						["lang"] = s.Lang ?? "",
						["gender"] = s.Gender ?? "",
					});
				}
			}
			arr.Add(new JsonObject {
				["name"] = m.DisplayName ?? "",
				["engine"] = "sherpa",
				["type"] = m.Type.ToString(),
				["speakers"] = speakers,
			});
		}
	}

	static void addsysmodel(JsonArray arr, string name, string engine, string type,
		List<SapiVoiceItem> voices) {
		if (voices == null || voices.Count == 0) return;
		var speakers = new JsonArray();
		for (var i = 0; i < voices.Count; i++) {
			var v = voices[i];
			speakers.Add(new JsonObject {
				["id"] = i,
				["name"] = v.Name ?? "",
				["label"] = string.IsNullOrEmpty(v.DisplayName) ? (v.Name ?? "") : v.DisplayName,
				["lang"] = v.Lang ?? "",
				["gender"] = v.Gender ?? "",
				["key"] = v.Key ?? "",
			});
		}
		arr.Add(new JsonObject {
			["name"] = name,
			["engine"] = engine,
			["type"] = type,
			["speakers"] = speakers,
		});
	}

	(float[] samples, int sr, string provider, string model, string voice, int sid, string engine)
		synthsherpa(string text, string modelName, int sid, float speed, TtsComputeMode compute) {
		if (svc?.TtsEngine == null)
			throw new InvalidOperationException("TTS 引擎不可用（Sherpa）");
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
		if (model == null)
			throw new InvalidOperationException("无可用 TTS 模型");

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
		return (samples, sr, provider, model.DisplayName, "", sid, "sherpa");
	}

	(float[] samples, int sr, string provider, string model, string voice, int sid, string engine)
		synthsapi(string text, string voice, int? sid, float speed, int volume) {
		refreshvoices();
		var list = cachedSapiVoices ?? new List<SapiVoiceItem>();
		if (list.Count == 0)
			throw new InvalidOperationException("无可用 SAPI 发音人");
		var pick = pickvoice(list, voice, sid);
		if (pick == null)
			throw new InvalidOperationException("未找到 SAPI 发音人: " + (voice ?? ""));
		var rate = (int)Math.Round((speed - 1.0) * 10);
		rate = Compat.Clamp(rate, -10, 10);
		var segs = TtsTextSplitter.Split(text);
		if (segs.Count == 0)
			segs.Add(new TtsSegment { Text = text });

		var parts = new List<(float[] s, int sr)>();
		if (pick.Source == "sapi-x86") {
			foreach (var seg in segs) {
				if (string.IsNullOrWhiteSpace(seg.Text)) continue;
				parts.Add(SapiX86Client.SynthToFloat(seg.Text, pick.Name, rate, volume));
			}
			var (samples, sr) = concatsamples(parts);
			return (samples, sr, "SAPI x86", "SAPI", pick.Key, list.IndexOf(pick), "sapi");
		}

		lock (sapiGate) {
			var eng = ensuresapi();
			if (!string.IsNullOrEmpty(pick.Name))
				eng.SelectVoice(pick.Name);
			eng.Rate = rate;
			eng.Volume = volume;
			foreach (var seg in segs) {
				if (string.IsNullOrWhiteSpace(seg.Text)) continue;
				parts.Add(eng.SynthToFloat(seg.Text));
			}
		}
		var (s2, sr2) = concatsamples(parts);
		return (s2, sr2, "SAPI", "SAPI", pick.Key, list.IndexOf(pick), "sapi");
	}

	(float[] samples, int sr, string provider, string model, string voice, int sid, string engine)
		synthwinrt(string text, string voice, int? sid, float speed, int volume) {
		refreshvoices();
		var list = cachedWinRtVoices ?? new List<SapiVoiceItem>();
		if (list.Count == 0)
			throw new InvalidOperationException("无可用 Windows 语音");
		var pick = pickvoice(list, voice, sid);
		if (pick == null)
			throw new InvalidOperationException("未找到 Windows 语音: " + (voice ?? ""));
		var segs = TtsTextSplitter.Split(text);
		if (segs.Count == 0)
			segs.Add(new TtsSegment { Text = text });

		var parts = new List<(float[] s, int sr)>();
		try {
			runsta(() => {
				lock (winRtGate) {
					var eng = ensurewinrt();
					if (!eng.SelectVoice(pick.Key) && !eng.SelectVoice(pick.Name))
						throw new InvalidOperationException("无法选择 Windows 语音: " + pick.Name);
					eng.SetRateVolume(speed, volume);
					foreach (var seg in segs) {
						if (string.IsNullOrWhiteSpace(seg.Text)) continue;
						var task = eng.Synthesize(seg.Text);
						parts.Add(waitdispatchertask(task));
					}
				}
				return 0;
			});
		}
		catch (Exception ex) {
			throw new InvalidOperationException($"{pick.Key}: {ex.Message}", ex);
		}
		var (samples, srOut) = concatsamples(parts);
		return (samples, srOut, "WinRT", "Windows", pick.Key, list.IndexOf(pick), "winrt");
	}

	(float[] samples, int sr, string provider, string model, string voice, int sid, string engine)
		synthedge(string text, string voice, int? sid, float speed, int volume) {
		lock (edgeGate) {
			var eng = ensureedge();
			var list = eng.LoadVoicesAsync(CancellationToken.None).GetAwaiter().GetResult().ToList();
			cachedEdgeVoices = list;
			var pick = pickvoice(list, voice, sid);
			if (pick == null)
				throw new InvalidOperationException("未找到 Edge 在线发音人: " + (voice ?? ""));
			if (!eng.SelectVoice(pick.Key))
				throw new InvalidOperationException("无法选择 Edge 在线发音人: " + pick.Name);
			eng.SetRateVolume(speed, volume);
			var parts = new List<(float[] s, int sr)>();
			var segs = TtsTextSplitter.Split(text);
			if (segs.Count == 0) segs.Add(new TtsSegment { Text = text });
			foreach (var seg in segs)
				parts.Add(eng.Synthesize(seg.Text).GetAwaiter().GetResult());
			var (samples, sr) = concatsamples(parts);
			return (samples, sr, "Edge Online", "Edge Online", pick.Key, list.IndexOf(pick), "edge");
		}
	}

	TtsEngineKind inferengine(string modelName, string voice) {
		var v = (voice ?? "").Trim();
		if (v.StartsWith("sapi-x86:", StringComparison.OrdinalIgnoreCase)
			|| v.StartsWith("sapi:", StringComparison.OrdinalIgnoreCase))
			return TtsEngineKind.Sapi;
		if (v.StartsWith("winrt:", StringComparison.OrdinalIgnoreCase))
			return TtsEngineKind.WinRt;
		if (v.StartsWith("edge:", StringComparison.OrdinalIgnoreCase))
			return TtsEngineKind.Edge;

		var m = (modelName ?? "").Trim();
		if (m.Equals("SAPI", StringComparison.OrdinalIgnoreCase)
			|| m.Equals("sapi5", StringComparison.OrdinalIgnoreCase))
			return TtsEngineKind.Sapi;
		if (m.Equals("Windows", StringComparison.OrdinalIgnoreCase)
			|| m.Equals("WinRT", StringComparison.OrdinalIgnoreCase)
			|| m.Equals("WinRt", StringComparison.OrdinalIgnoreCase))
			return TtsEngineKind.WinRt;
		if (m.Equals("Edge", StringComparison.OrdinalIgnoreCase)
			|| m.Equals("Edge Online", StringComparison.OrdinalIgnoreCase))
			return TtsEngineKind.Edge;

		var sherpa = svc?.ScanTts?.Invoke();
		if (!string.IsNullOrEmpty(m) && sherpa != null) {
			if (sherpa.Any(x =>
				string.Equals(x.DisplayName, m, StringComparison.OrdinalIgnoreCase)
				|| Compat.Contains(x.DisplayName, m, StringComparison.OrdinalIgnoreCase)))
				return TtsEngineKind.Sherpa;
		}
		if (sherpa != null && sherpa.Count > 0)
			return TtsEngineKind.Sherpa;
		refreshvoices();
		if (cachedWinRtVoices != null && cachedWinRtVoices.Count > 0)
			return TtsEngineKind.WinRt;
		return TtsEngineKind.Edge;
	}

	static bool tryparseengine(string raw, out TtsEngineKind? kind) {
		kind = null;
		var s = (raw ?? "").Trim().ToLowerInvariant();
		if (s.Length == 0) return true;
		if (s is "sherpa" or "sherpa-onnx" or "vits" or "matcha" or "onnx") {
			kind = TtsEngineKind.Sherpa;
			return true;
		}
		if (s is "sapi" or "sapi5" or "system.speech") {
			kind = TtsEngineKind.Sapi;
			return true;
		}
		if (s is "winrt" or "windows" or "onecore" or "win") {
			kind = TtsEngineKind.WinRt;
			return true;
		}
		if (s is "edge" or "edge-online" or "edge_tts") {
			kind = TtsEngineKind.Edge;
			return true;
		}
		return false;
	}

	static SapiVoiceItem pickvoice(List<SapiVoiceItem> list, string voice, int? sid) {
		if (list == null || list.Count == 0) return null;
		var q = (voice ?? "").Trim();
		if (q.Length > 0) {
			var hit = list.FirstOrDefault(v =>
				string.Equals(v.Key, q, StringComparison.OrdinalIgnoreCase));
			if (hit != null) return hit;
			hit = list.FirstOrDefault(v =>
				string.Equals(v.Name, q, StringComparison.OrdinalIgnoreCase)
				&& v.Source != "sapi-x86");
			if (hit != null) return hit;
			hit = list.FirstOrDefault(v =>
				string.Equals(v.Name, q, StringComparison.OrdinalIgnoreCase));
			if (hit != null) return hit;
			hit = list.FirstOrDefault(v =>
				string.Equals(v.DisplayName, q, StringComparison.OrdinalIgnoreCase));
			if (hit != null) return hit;
			hit = list.FirstOrDefault(v =>
				Compat.Contains(v.Name, q, StringComparison.OrdinalIgnoreCase)
				|| Compat.Contains(v.DisplayName, q, StringComparison.OrdinalIgnoreCase));
			return hit;
		}
		if (sid != null && sid.Value >= 0 && sid.Value < list.Count)
			return list[sid.Value];
		return list.FirstOrDefault(v => v.Lang == "zh")
			?? list.FirstOrDefault(v => v.Lang == "en")
			?? list[0];
	}

	void refreshvoices() {
		if (cachedSapiVoices != null
			&& unchecked(Environment.TickCount - lastVoiceScan) < VoiceCacheMs)
			return;
		cachedSapiVoices = listsapivoices();
		cachedWinRtVoices = listwinrtvoices();
		lastVoiceScan = Environment.TickCount;
	}

	List<SapiVoiceItem> listsapivoices() {
		var list = new List<SapiVoiceItem>();
		try {
			var eng = ensuresapi();
			foreach (var v in eng.Voices) {
				var culture = v.Culture?.Name ?? "";
				var lang = SapiVoiceItem.LangOf(culture);
				var g = sapigender(v.Gender);
				var gLabel = TtsGender.Label(g);
				var tail = string.IsNullOrEmpty(culture) ? "" : " · " + culture;
				if (!string.IsNullOrEmpty(gLabel)) tail += " · " + gLabel;
				list.Add(new SapiVoiceItem {
					DisplayName = (v.Name ?? "") + tail,
					Key = "sapi:" + (v.Name ?? ""),
					Name = v.Name ?? "",
					Culture = culture,
					Lang = lang,
					Gender = g,
					Source = "sapi",
				});
			}
		}
		catch (Exception ex) {
			CaptureLog.Ex("http tts sapi enum", ex);
		}
		if (SapiX86Client.ExeAvailable) {
			try {
				var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var one in list)
					if (!string.IsNullOrEmpty(one.Name)) seen.Add(one.Name);
				foreach (var v in SapiX86Client.ListVoices()) {
					if (string.IsNullOrEmpty(v.Name) || !seen.Add(v.Name)) continue;
					list.Add(v);
				}
			}
			catch (Exception ex) {
				CaptureLog.Ex("http tts sapi-x86 enum", ex);
			}
		}
		return list;
	}

	List<SapiVoiceItem> listwinrtvoices() {
		try {
			return runsta(() => {
				var eng = ensurewinrt();
				return eng.Voices.ToList();
			});
		}
		catch (Exception ex) {
			CaptureLog.Ex("http tts winrt enum", ex);
			return new List<SapiVoiceItem>();
		}
	}

	List<SapiVoiceItem> listedgevoices() {
		try {
			return ensureedge().LoadVoicesAsync(CancellationToken.None).GetAwaiter().GetResult().ToList();
		}
		catch (Exception ex) {
			CaptureLog.Ex("http tts edge enum", ex);
			return new List<SapiVoiceItem>();
		}
	}

	SapiTts ensuresapi() {
		lock (sapiGate) {
			httpSapi ??= new SapiTts();
			return httpSapi;
		}
	}

	WinRtTts ensurewinrt() {
		lock (winRtGate) {
			httpWinRt ??= new WinRtTts();
			return httpWinRt;
		}
	}

	EdgeOnlineTts ensureedge() {
		lock (edgeGate) {
			httpEdge ??= new EdgeOnlineTts();
			return httpEdge;
		}
	}

	bool ttssapiavailable() {
		try { return ensuresapi().Voices.Count > 0; }
		catch { return false; }
	}

	bool ttswinrtavailable() {
		if (cachedWinRtVoices != null) return cachedWinRtVoices.Count > 0;
		try {
			cachedWinRtVoices = listwinrtvoices();
			return cachedWinRtVoices.Count > 0;
		}
		catch { return false; }
	}

	void disposettsvoices() {
		lock (sapiGate) {
			try { httpSapi?.Dispose(); } catch { }
			httpSapi = null;
		}
		lock (winRtGate) {
			try { httpWinRt?.Dispose(); } catch { }
			httpWinRt = null;
		}
		lock (edgeGate) {
			try { httpEdge?.Dispose(); } catch { }
			httpEdge = null;
		}
		cachedSapiVoices = null;
		cachedWinRtVoices = null;
		cachedEdgeVoices = null;
	}

	static string sapigender(VoiceGender g) => g switch {
		VoiceGender.Female => TtsGender.Female,
		VoiceGender.Male => TtsGender.Male,
		_ => "",
	};

	static (float[] samples, int sr) concatsamples(List<(float[] s, int sr)> parts) {
		if (parts == null || parts.Count == 0)
			return (Array.Empty<float>(), 22050);
		var sr = 0;
		var n = 0;
		foreach (var p in parts) {
			if (p.s == null || p.s.Length == 0) continue;
			if (sr == 0) sr = p.sr;
			if (p.sr != sr)
				throw new InvalidOperationException($"TTS 采样率不一致 {sr} vs {p.sr}");
			n += p.s.Length;
		}
		if (n == 0) return (Array.Empty<float>(), sr > 0 ? sr : 22050);
		var all = new float[n];
		var off = 0;
		foreach (var p in parts) {
			if (p.s == null || p.s.Length == 0) continue;
			Array.Copy(p.s, 0, all, off, p.s.Length);
			off += p.s.Length;
		}
		return (all, sr);
	}

	static T runsta<T>(Func<T> fn) {
		T result = default;
		Exception err = null;
		var t = new Thread(() => {
			try {
				_ = Dispatcher.CurrentDispatcher;
				result = fn();
			}
			catch (Exception ex) { err = ex; }
		});
		t.SetApartmentState(ApartmentState.STA);
		t.IsBackground = true;
		t.Name = "http-tts-winrt";
		t.Start();
		t.Join();
		if (err != null) throw err;
		return result;
	}

	/// <summary>WinRT IAsync 在 STA 上要泵消息，不能直接 GetResult。</summary>
	static T waitdispatchertask<T>(Task<T> task) {
		var disp = Dispatcher.CurrentDispatcher;
		var frame = new DispatcherFrame();
		task.ContinueWith(_ => {
			try { disp.BeginInvoke(new Action(() => frame.Continue = false)); }
			catch { frame.Continue = false; }
		}, TaskScheduler.Default);
		Dispatcher.PushFrame(frame);
		return task.GetAwaiter().GetResult();
	}
}
