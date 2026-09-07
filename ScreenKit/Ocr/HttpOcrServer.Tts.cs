using System.Net;
using System.Speech.Synthesis;
using System.Text.Json.Nodes;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>HTTP TTS：Sherpa + SAPI + Windows（WinRT）语音。</summary>
sealed partial class HttpOcrServer {
	const int VoiceCacheMs = 30_000;
	readonly object sapiGate = new();
	readonly object winRtGate = new();
	SapiTts httpSapi;
	WinRtTts httpWinRt;
	List<SapiVoiceItem> cachedSapiVoices;
	List<SapiVoiceItem> cachedWinRtVoices;
	int lastVoiceScan;

	void handlettsmodels(HttpListenerContext ctx) {
		JsonArray arr;
		try { arr = buildttsmodels(); }
		catch (Exception ex) {
			writejson(ctx, 200, err(920, "扫描 TTS 失败: " + ex.Message));
			return;
		}
		writejson(ctx, 200, new JsonObject {
			["code"] = 100,
			["data"] = arr,
			["count"] = arr.Count,
		});
	}

	void handletts(HttpListenerContext ctx) {
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
			writejson(ctx, 200, err(925, "未知 engine（sherpa / sapi / winrt）"));
			return;
		}
		if (kind == null)
			kind = inferengine(modelName, voice);
		if (kind == TtsEngineKind.Sherpa && svc?.TtsEngine == null) {
			writejson(ctx, 200, err(921, "TTS 引擎不可用（Sherpa）"));
			return;
		}

		var t0 = Environment.TickCount;
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
			else
				(samples, sr, provider, modelOut, voiceOut, sidOut, engOut) =
					synthsherpa(text, modelName, sidVal, speed, compute);

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
					["engine"] = engOut,
					["model"] = modelOut,
					["voice"] = voiceOut,
					["speaker_id"] = sidOut,
					["provider"] = provider,
				},
				["time"] = ms,
				["timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds(),
			});
		}
		catch (InvalidOperationException ex) {
			var msg = ex.Message ?? "";
			var code = msg.IndexOf("无可用", StringComparison.Ordinal) >= 0
				|| msg.IndexOf("未找到", StringComparison.Ordinal) >= 0
				|| msg.IndexOf("引擎不可用", StringComparison.Ordinal) >= 0
				? 922 : 924;
			writejson(ctx, 200, err(code, msg));
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(924, "合成失败: " + ex.Message));
		}
	}

	JsonArray buildttsmodels() {
		var arr = new JsonArray();
		List<TtsModelInfo> list = null;
		try { list = svc?.ScanTts?.Invoke(); } catch { }
		list ??= new List<TtsModelInfo>();
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
				["engine"] = "sherpa",
				["type"] = m.Type.ToString(),
				["speakers"] = speakers,
			});
		}

		refreshvoices();
		addsysmodel(arr, "SAPI", "sapi", "Sapi", cachedSapiVoices);
		addsysmodel(arr, "Windows", "winrt", "WinRt", cachedWinRtVoices);
		return arr;
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

	TtsEngineKind inferengine(string modelName, string voice) {
		var v = (voice ?? "").Trim();
		if (v.StartsWith("sapi-x86:", StringComparison.OrdinalIgnoreCase)
			|| v.StartsWith("sapi:", StringComparison.OrdinalIgnoreCase))
			return TtsEngineKind.Sapi;
		if (v.StartsWith("winrt:", StringComparison.OrdinalIgnoreCase))
			return TtsEngineKind.WinRt;

		var m = (modelName ?? "").Trim();
		if (m.Equals("SAPI", StringComparison.OrdinalIgnoreCase)
			|| m.Equals("sapi5", StringComparison.OrdinalIgnoreCase))
			return TtsEngineKind.Sapi;
		if (m.Equals("Windows", StringComparison.OrdinalIgnoreCase)
			|| m.Equals("WinRT", StringComparison.OrdinalIgnoreCase)
			|| m.Equals("WinRt", StringComparison.OrdinalIgnoreCase))
			return TtsEngineKind.WinRt;

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
		if (cachedSapiVoices != null && cachedSapiVoices.Count > 0)
			return TtsEngineKind.Sapi;
		return TtsEngineKind.Sherpa;
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
				list.Add(new SapiVoiceItem {
					DisplayName = v.Name ?? "",
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
		if (SapiX86Client.ExeAvailable && SapiX86Client.IsRunning()) {
			try {
				foreach (var v in SapiX86Client.ListVoices())
					list.Add(v);
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
		cachedSapiVoices = null;
		cachedWinRtVoices = null;
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
