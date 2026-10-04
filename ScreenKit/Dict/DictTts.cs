using System.Globalization;
using System.IO;
using System.Speech.Synthesis;

namespace ScreenKit;

/// <summary>词典发音。用本程序的 ONNX / SAPI / Windows 语音 / Edge，不改语音合成页。</summary>
static class DictTts {
	public const string AUTO = "auto";
	public const string ONNX = "onnx";
	public const string SAPI = "sapi";
	public const string WINRT = "winrt";
	public const string EDGE = "edge";

	public static readonly double[] RateSteps = { 0.5, 0.8, 1.0, 1.2, 1.5, 2.0 };

	const int CACHE_DAYS = 1;
	static int cachePurged;
	static readonly object OnnxGate = new();
	static TtsEngine onnxEng;

	public static string NormEngine(string raw) {
		var s = (raw ?? "").Trim().ToLowerInvariant();
		if (s == ONNX || s == SAPI || s == WINRT || s == EDGE || s == AUTO) return s;
		return AUTO;
	}

	public static double NormRate(double rate) {
		if (double.IsNaN(rate) || double.IsInfinity(rate)) return 1;
		if (rate < 0.5) return 0.5;
		if (rate > 2) return 2;
		return rate;
	}

	public static int SapiRate(double speed) {
		var n = (int)Math.Round((NormRate(speed) - 1) * 10);
		if (n < -10) return -10;
		if (n > 10) return 10;
		return n;
	}

	public static string DefaultEdge(string lang) {
		if (lang == "ja") return "ja-JP-NanamiNeural";
		if (lang == "ko") return "ko-KR-SunHiNeural";
		if (lang == "zh") return "zh-CN-XiaoxiaoNeural";
		return "en-US-AriaNeural";
	}

	public static string EdgeName(string lang, string configured) {
		var name = (configured ?? "").Trim();
		var prefix = (string.IsNullOrEmpty(lang) ? "en" : lang) + "-";
		if (name.Length > 0 && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			return name;
		return DefaultEdge(lang);
	}

	public static DictTtsLang For(OcrOptions o, string lang) {
		DictTtsLang src = null;
		if (o != null) {
			if (lang == "zh") src = o.DictTtsZh;
			else if (lang == "ja") src = o.DictTtsJa;
			else if (lang == "ko") src = o.DictTtsKo;
			else src = o.DictTtsEn;
		}
		var pref = src != null ? src.Clone() : DictTtsLang.Make(DefaultEdge(lang));
		pref.Engine = NormEngine(pref.Engine);
		if (string.IsNullOrWhiteSpace(pref.Edge)) pref.Edge = DefaultEdge(lang);
		pref.Sapi = pref.Sapi ?? "";
		pref.Onnx = pref.Onnx ?? "";
		pref.WinRt = pref.WinRt ?? "";
		pref.Rate = NormRate(pref.Rate);
		return pref;
	}

	public static (string Id, string LocKey)[] EdgeVoices(string lang) {
		if (lang == "ja") return new[] {
			("ja-JP-NanamiNeural", "dict.edge.nanami"),
			("ja-JP-KeitaNeural", "dict.edge.keita"),
		};
		if (lang == "ko") return new[] {
			("ko-KR-SunHiNeural", "dict.edge.sunhi"),
			("ko-KR-InJoonNeural", "dict.edge.injoon"),
		};
		if (lang == "zh") return new[] {
			("zh-CN-XiaoxiaoNeural", "dict.edge.xiaoxiao"),
			("zh-CN-YunxiNeural", "dict.edge.yunxi"),
		};
		return new[] {
			("en-US-AriaNeural", "dict.edge.aria"),
			("en-US-GuyNeural", "dict.edge.guy"),
			("en-GB-SoniaNeural", "dict.edge.sonia"),
			("en-GB-RyanNeural", "dict.edge.ryan"),
		};
	}

	public static List<(string Label, string Id)> Voices(string engine, string lang) {
		engine = NormEngine(engine);
		var list = new List<(string, string)>();
		if (engine == ONNX) {
			foreach (var v in onnxvoices(lang)) list.Add((v.Label, v.Id));
			return list;
		}
		if (engine == SAPI) {
			list.Add((Loc.T("dict.tts.pickauto"), ""));
			foreach (var v in LocalVoices()) {
				if (!string.Equals(v.Lang, lang, StringComparison.OrdinalIgnoreCase)) continue;
				list.Add((v.Name, v.Name));
			}
			return list;
		}
		if (engine == WINRT) {
			list.Add((Loc.T("dict.tts.pickauto"), ""));
			foreach (var v in winrtvoices(lang)) list.Add((v.Label, v.Id));
			return list;
		}
		if (engine == EDGE) {
			foreach (var v in EdgeVoices(lang)) list.Add((Loc.T(v.LocKey), v.Id));
			return list;
		}
		return list;
	}

	/// <summary>合成并写入 tmp/voice，命中未过期文件则直接返回路径。失败抛「novoice」。</summary>
	public static async Task<string> EnsureWav(DictTtsLang pref, string lang, string text) {
		text = (text ?? "").Trim();
		if (text.Length > 400) text = text.Substring(0, 400);
		if (text.Length == 0) throw new InvalidOperationException("novoice");
		pref = pref ?? DictTtsLang.Make(DefaultEdge(lang));
		purgeonce();
		var engine = resolve(pref, lang);
		var voice = voiceof(pref, lang, engine);
		var rate = NormRate(pref.Rate);
		// 种子写进键，避免沿用种子随机时留下的旧 wav。
		var cacheEng = engine == ONNX ? engine + ":s14" : engine;
		var path = cachepath(cacheEng, voice, rate, lang, text);
		if (fresh(path)) return path;
		var got = await synth(engine, voice, lang, text, rate).ConfigureAwait(true);
		if (got.samples == null || got.samples.Length == 0 || got.sampleRate <= 0)
			throw new InvalidOperationException("novoice");
		Directory.CreateDirectory(cachedir());
		var part = path + ".part";
		try {
			TtsPlayer.SaveWav(part, got.samples, got.sampleRate);
			if (File.Exists(path)) File.Delete(path);
			File.Move(part, path);
		}
		catch {
			try { if (File.Exists(part)) File.Delete(part); } catch { }
			throw;
		}
		return path;
	}

	public static bool IdleUnload(int limitMs) {
		lock (OnnxGate) {
			if (onnxEng == null) return false;
			return onnxEng.IdleUnload(limitMs);
		}
	}

	public static bool TryOnnxMem(out string name, out string device, out long bytes) {
		lock (OnnxGate) {
			if (onnxEng != null && onnxEng.TryMem(out name, out device, out bytes))
				return true;
			name = "";
			device = "";
			bytes = 0;
			return false;
		}
	}

	public static void UnloadOnnx() {
		lock (OnnxGate) {
			try { onnxEng?.UnloadSafe(); } catch { }
		}
	}

	/// <summary>自检：过期文件删掉，一天内的留下；语速换算。</summary>
	public static int TestCache() {
		var dir = cachedir();
		Directory.CreateDirectory(dir);
		var oldp = Path.Combine(dir, "test_old.wav");
		var newp = Path.Combine(dir, "test_new.wav");
		File.WriteAllBytes(oldp, new byte[] { 1, 2, 3, 4 });
		File.WriteAllBytes(newp, new byte[] { 1, 2, 3, 4 });
		File.SetLastWriteTime(oldp, DateTime.Now.AddDays(-2));
		Purge();
		var oldGone = !File.Exists(oldp);
		var stay = File.Exists(newp);
		try { if (File.Exists(newp)) File.Delete(newp); } catch { }
		if (!oldGone || !stay) return 1;
		if (NormEngine("ONNX") != ONNX || NormEngine("windows") != AUTO) return 2;
		if (Math.Abs(NormRate(9) - 2) > 0.001 || Math.Abs(NormRate(0.1) - 0.5) > 0.001) return 3;
		if (SapiRate(1) != 0 || SapiRate(0.5) != -5 || SapiRate(2) != 10) return 4;
		var id = cachepath(EDGE, "en-US-AriaNeural", 1, "en", "hello");
		if (!id.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return 5;
		if (Path.GetFileName(id).Length < 8) return 5;
		return 0;
	}

	public static void Purge() {
		var dir = cachedir();
		if (!Directory.Exists(dir)) return;
		var now = DateTime.Now;
		foreach (var f in Directory.EnumerateFiles(dir)) {
			if (!fresh(f, now)) {
				try { File.Delete(f); } catch { }
			}
		}
	}

	static string resolve(DictTtsLang pref, string lang) {
		var engine = NormEngine(pref.Engine);
		if (engine != AUTO) return engine;
		if (winrtvoices(lang).Count > 0) return WINRT;
		if (sapifor(lang).Count > 0) return SAPI;
		return EDGE;
	}

	static string voiceof(DictTtsLang pref, string lang, string engine) {
		if (engine == ONNX) {
			var key = (pref.Onnx ?? "").Trim();
			if (splitonnx(key, out _, out _)) return key;
			var list = onnxvoices(lang);
			return list.Count > 0 ? list[0].Id : "";
		}
		if (engine == SAPI) {
			var name = (pref.Sapi ?? "").Trim();
			if (name.Length > 0) return name;
			var list = sapifor(lang);
			return list.Count > 0 ? list[0] : "";
		}
		if (engine == WINRT) {
			var id = (pref.WinRt ?? "").Trim();
			if (id.Length > 0) return id;
			var list = winrtvoices(lang);
			return list.Count > 0 ? list[0].Id : "";
		}
		return EdgeName(lang, pref.Edge);
	}

	static async Task<(float[] samples, int sampleRate)> synth(
		string engine, string voice, string lang, string text, double rate) {
		if (engine == ONNX)
			return await Task.Run(() => synthonnx(lang, voice, text, rate)).ConfigureAwait(true);
		if (engine == SAPI)
			return await Task.Run(() => synthsapi(text, lang, voice, rate)).ConfigureAwait(true);
		if (engine == WINRT) return await synthwinrt(lang, voice, text, rate).ConfigureAwait(true);
		var edge = new EdgeOnlineTts();
		try {
			edge.SetVoiceName(string.IsNullOrEmpty(voice) ? DefaultEdge(lang) : voice);
			edge.SetRateVolume(rate, 100);
			return await edge.Synthesize(text).ConfigureAwait(true);
		}
		finally { edge.Dispose(); }
	}

	static (float[] samples, int sampleRate) synthonnx(string lang, string voice, string text, double rate) {
		TtsModelInfo model = null;
		var sid = 0;
		var named = splitonnx(voice, out var name, out sid);
		foreach (var m in TtsModelScanner.Scan()) {
			if (named) {
				if (!string.Equals(m.DisplayName, name, StringComparison.OrdinalIgnoreCase)) continue;
				model = m;
				break;
			}
			if (m.Speakers == null) continue;
			foreach (var sp in m.Speakers) {
				if (!onnxlang(m, sp, lang)) continue;
				model = m;
				sid = sp.Id;
				break;
			}
			if (model != null) break;
		}
		if (model == null) return (Array.Empty<float>(), 0);
		lock (OnnxGate) {
			onnxEng ??= new TtsEngine();
			onnxEng.LoadModel(model);
			return onnxEng.Synthesize(text, sid, (float)rate, lang: lang);
		}
	}

	static (float[] samples, int sampleRate) synthsapi(string text, string lang, string want, double rate) {
		using var sapi = new SapiTts();
		string pick = null;
		string first = null;
		var name = (want ?? "").Trim();
		foreach (var v in sapi.Voices) {
			if (!langof(v, lang)) continue;
			if (first == null) first = v.Name;
			if (name.Length > 0 && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)) {
				pick = v.Name;
				break;
			}
		}
		if (pick == null) pick = first;
		if (string.IsNullOrEmpty(pick)) return (Array.Empty<float>(), 0);
		sapi.SelectVoice(pick);
		sapi.Rate = SapiRate(rate);
		return sapi.SynthToFloat(text);
	}

	static async Task<(float[] samples, int sampleRate)> synthwinrt(
		string lang, string voice, string text, double rate) {
		var win = new WinRtTts();
		var id = (voice ?? "").Trim();
		if (id.Length == 0 || !win.SelectVoice(id)) {
			var list = winrtvoices(lang);
			if (list.Count == 0 || !win.SelectVoice(list[0].Id))
				return (Array.Empty<float>(), 0);
		}
		win.SetRateVolume(rate, 100);
		return await win.Synthesize(text).ConfigureAwait(true);
	}

	static List<(string Label, string Id)> onnxvoices(string lang) {
		var list = new List<(string, string)>();
		foreach (var m in TtsModelScanner.Scan()) {
			if (m.Speakers == null) continue;
			foreach (var sp in m.Speakers) {
				if (!onnxlang(m, sp, lang)) continue;
				var label = m.DisplayName;
				if (m.Speakers.Count > 1) label = label + " · " + sp.DisplayName;
				list.Add((label, m.DisplayName + "\t" + sp.Id));
			}
		}
		return list;
	}

	static bool onnxlang(TtsModelInfo m, TtsSpeakerInfo sp, string lang) {
		var have = sp != null && !string.IsNullOrEmpty(sp.Lang) ? sp.Lang : m?.Lang ?? "";
		return TtsLang.Match(have, lang);
	}

	static bool splitonnx(string key, out string model, out int sid) {
		model = "";
		sid = 0;
		key = key ?? "";
		var i = key.LastIndexOf('\t');
		if (i <= 0) return false;
		model = key.Substring(0, i);
		return int.TryParse(key.Substring(i + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out sid);
	}

	static List<string> sapifor(string lang) {
		var list = new List<string>();
		foreach (var v in LocalVoices()) {
			if (string.Equals(v.Lang, lang, StringComparison.OrdinalIgnoreCase))
				list.Add(v.Name);
		}
		return list;
	}

	public static List<(string Lang, string Name)> LocalVoices() {
		var list = new List<(string, string)>();
		try {
			using var sapi = new SapiTts();
			foreach (var v in sapi.Voices) {
				string lang = "";
				try { lang = v.Culture?.TwoLetterISOLanguageName ?? ""; } catch { }
				if (string.IsNullOrEmpty(v.Name)) continue;
				list.Add((lang, v.Name));
			}
		}
		catch { }
		return list;
	}

	static List<(string Label, string Id)> winrtvoices(string lang) {
		var list = new List<(string, string)>();
		try {
			using var win = new WinRtTts();
			foreach (var v in win.Voices) {
				if (!string.Equals(v.Lang, lang, StringComparison.OrdinalIgnoreCase)) continue;
				if (string.IsNullOrEmpty(v.Name)) continue;
				list.Add((v.DisplayName, v.Name));
			}
		}
		catch { }
		return list;
	}

	static bool langof(VoiceInfo voice, string lang) {
		try {
			var two = voice?.Culture?.TwoLetterISOLanguageName ?? "";
			return string.Equals(two, lang, StringComparison.OrdinalIgnoreCase);
		}
		catch { return false; }
	}

	static string cachedir() => Path.Combine(TmpStore.Root, "voice");

	static string cachepath(string engine, string voice, double rate, string lang, string text) {
		var raw = engine + "\n" + lang + "\n" + (voice ?? "") + "\n"
			+ NormRate(rate).ToString("0.00", CultureInfo.InvariantCulture) + "\n" + text;
		return Path.Combine(cachedir(), fnv(raw) + ".wav");
	}

	static string fnv(string text) {
		var bytes = System.Text.Encoding.UTF8.GetBytes(text ?? "");
		ulong hash = 14695981039346656037UL;
		foreach (var b in bytes) {
			hash ^= b;
			hash *= 1099511628211UL;
		}
		return hash.ToString("x16");
	}

	static void purgeonce() {
		if (System.Threading.Interlocked.Exchange(ref cachePurged, 1) == 1) return;
		Purge();
	}

	static bool fresh(string path) => fresh(path, DateTime.Now);

	static bool fresh(string path, DateTime now) {
		try {
			var info = new FileInfo(path);
			if (!info.Exists || info.Length == 0) return false;
			return now - info.LastWriteTime < TimeSpan.FromDays(CACHE_DAYS);
		}
		catch { return false; }
	}
}
