using System.Globalization;
using System.IO;
using System.Speech.Synthesis;

namespace ScreenKit;

/// <summary>词典发音。用本程序的 ONNX / SAPI / Windows 语音 / Edge，不改语音合成页。自动：该语言有 Windows 语音则用，否则 Edge。</summary>
static class DictTts {
	public const string AUTO = "auto";
	public const string ONNX = "onnx";
	public const string SAPI = "sapi";
	public const string WINRT = "winrt";
	public const string EDGE = "edge";
	public const string CHROME = "chrome";

	public static readonly double[] RateSteps = { 0.5, 0.8, 1.0, 1.2, 1.5, 2.0 };
	/// <summary>表格里始终保留的语言：中、日、韩、英。</summary>
	public static readonly string[] CoreLangs = { "zh", "ja", "ko", "en" };

	const int CACHE_DAYS = 1;
	static int cachePurged;
	static readonly object OnnxGate = new();
	static TtsEngine onnxEng;

	public static string ResolveEngine(string engine, string lang) {
		return resolve(new DictTtsLang { Engine = NormEngine(engine) }, lang);
	}

	public static string NormEngine(string raw) {
		var s = (raw ?? "").Trim().ToLowerInvariant();
		if (s == ONNX || s == SAPI || s == WINRT || s == EDGE || s == CHROME || s == AUTO) return s;
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

	public static Dictionary<string, DictTtsLang> NewMap() {
		var map = new Dictionary<string, DictTtsLang>(StringComparer.OrdinalIgnoreCase);
		EnsureMap(map);
		return map;
	}

	public static void EnsureMap(Dictionary<string, DictTtsLang> map) {
		if (map == null) return;
		foreach (var lang in CoreLangs) {
			if (map.ContainsKey(lang)) continue;
			map[lang] = DictTtsLang.Make(DefaultEdge(lang));
		}
	}

	public static Dictionary<string, DictTtsLang> CopyMap(Dictionary<string, DictTtsLang> src) {
		var map = new Dictionary<string, DictTtsLang>(StringComparer.OrdinalIgnoreCase);
		if (src != null) {
			foreach (var kv in src) {
				var lang = NormLang(kv.Key);
				if (lang.Length == 0 || kv.Value == null) continue;
				map[lang] = kv.Value.Clone();
			}
		}
		EnsureMap(map);
		return map;
	}

	/// <summary>两位或稍长的语言码。中日韩英以及表格里新增的语言。</summary>
	public static string NormLang(string lang) {
		var s = TtsLang.Normalize(lang);
		if (s.Length < 2 || s.Length > 8) return "";
		foreach (var ch in s) {
			if (ch < 'a' || ch > 'z') return "";
		}
		return s;
	}

	public static bool IsCoreLang(string lang) {
		lang = NormLang(lang);
		foreach (var c in CoreLangs)
			if (c == lang) return true;
		return false;
	}

	/// <summary>中日韩英在前，其余按字母。</summary>
	public static List<string> LangsOf(Dictionary<string, DictTtsLang> map) {
		EnsureMap(map);
		var list = new List<string>();
		foreach (var c in CoreLangs) list.Add(c);
		var rest = new List<string>();
		if (map != null) {
			foreach (var k in map.Keys) {
				var n = NormLang(k);
				if (n.Length == 0 || IsCoreLang(n)) continue;
				rest.Add(n);
			}
		}
		rest.Sort(StringComparer.Ordinal);
		list.AddRange(rest);
		return list;
	}

	public static DictTtsLang For(OcrOptions o, string lang) {
		lang = NormLang(lang);
		if (lang.Length == 0) lang = "en";
		DictTtsLang src = null;
		var map = o?.DictTtsMap;
		if (map != null) {
			if (!map.TryGetValue(lang, out src) && lang != "en")
				map.TryGetValue("en", out src);
		}
		var pref = src != null ? src.Clone() : DictTtsLang.Make(DefaultEdge(lang));
		pref.Engine = NormEngine(pref.Engine);
		if (string.IsNullOrWhiteSpace(pref.Edge)) pref.Edge = DefaultEdge(lang);
		pref.Sapi = pref.Sapi ?? "";
		pref.Onnx = pref.Onnx ?? "";
		pref.WinRt = pref.WinRt ?? "";
		pref.Chrome = pref.Chrome ?? "";
		pref.Rate = NormRate(pref.Rate);
		return pref;
	}

	public static string VoiceOf(DictTtsLang pref, string lang, string engine) =>
		voiceof(pref, lang, engine);

	/// <summary>Chrome 只出声，不产 wav。网页发音走这条，读完再交一段很短的静音。</summary>
	public static void PlayChrome(OcrOptions o, DictTtsLang pref, string lang, string text) {
		text = (text ?? "").Trim();
		if (text.Length > 400) text = text.Substring(0, 400);
		if (text.Length == 0) throw new InvalidOperationException("novoice");
		var exe = ChromeTts.FindExe(o?.ChromeExe);
		if (string.IsNullOrEmpty(exe))
			throw new InvalidOperationException(Loc.T("tts.chrome.missing"));
		pref = pref ?? DictTtsLang.Make(DefaultEdge(lang));
		var voice = (VoiceOf(pref, lang, CHROME) ?? "").Trim();
		var culture = ChromePack.SpeakLang(lang);
		foreach (var v in ChromeTts.Voices) {
			if (voice.Length > 0) {
				if (!string.Equals(v.Name, voice, StringComparison.OrdinalIgnoreCase)) continue;
			}
			else if (!string.Equals(v.Lang, lang, StringComparison.OrdinalIgnoreCase)) continue;
			voice = v.Name ?? "";
			if (!string.IsNullOrEmpty(v.Culture)) culture = v.Culture;
			break;
		}
		var idle = ChromeTts.ClampIdle(o == null ? 60 : o.ChromeIdleSec);
		ChromeTts.Speak(text, voice, culture, NormRate(pref.Rate), 1, exe, idle, CancellationToken.None);
	}

	public static string AckWav() {
		var path = Path.Combine(cachedir(), "chrome-ack.wav");
		if (File.Exists(path)) return path;
		Directory.CreateDirectory(cachedir());
		File.WriteAllBytes(path, ackwav());
		return path;
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
		if (engine == CHROME) {
			list.Add((Loc.T("dict.tts.pickauto"), ""));
			foreach (var v in ChromeTts.Voices) {
				if (!string.Equals(v.Lang, lang, StringComparison.OrdinalIgnoreCase)) continue;
				list.Add((v.DisplayName, v.Name));
			}
			return list;
		}
		return list;
	}

	/// <summary>合成并写入 tmp/voice，命中未过期文件则直接返回路径。失败抛「novoice」。</summary>
	public static async Task<string> EnsureWav(DictTtsLang pref, string lang, string text, int maxChars = 400) {
		text = (text ?? "").Trim();
		if (maxChars < 1) maxChars = 400;
		if (maxChars > 4000) maxChars = 4000;
		if (text.Length > maxChars) text = text.Substring(0, maxChars);
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
		var auto = resolve(new DictTtsLang { Engine = AUTO }, "zh");
		var hasWin = winrtvoices("zh").Count > 0;
		if (hasWin && auto != WINRT) return 6;
		if (!hasWin && auto != EDGE) return 6;
		if (resolve(new DictTtsLang { Engine = SAPI }, "zh") != SAPI) return 7;
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
		if (engine == CHROME) {
			var name = (pref.Chrome ?? "").Trim();
			if (name.Length > 0) return name;
			foreach (var v in ChromeTts.Voices) {
				if (!string.Equals(v.Lang, lang, StringComparison.OrdinalIgnoreCase)) continue;
				return v.Name ?? "";
			}
			return "";
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
		if (engine == CHROME) throw new InvalidOperationException("novoice");
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

	static byte[] ackwav() {
		const int rate = 8000;
		const int samples = 800;
		var data = samples * 2;
		var buf = new byte[44 + data];
		puttxt(buf, 0, "RIFF");
		put32(buf, 4, 36 + data);
		puttxt(buf, 8, "WAVE");
		puttxt(buf, 12, "fmt ");
		put32(buf, 16, 16);
		put16(buf, 20, 1);
		put16(buf, 22, 1);
		put32(buf, 24, rate);
		put32(buf, 28, rate * 2);
		put16(buf, 32, 2);
		put16(buf, 34, 16);
		puttxt(buf, 36, "data");
		put32(buf, 40, data);
		return buf;
	}

	static void puttxt(byte[] buf, int at, string text) {
		for (var i = 0; i < text.Length; i++) buf[at + i] = (byte)text[i];
	}

	static void put16(byte[] buf, int at, int value) {
		buf[at] = (byte)value;
		buf[at + 1] = (byte)(value >> 8);
	}

	static void put32(byte[] buf, int at, int value) {
		buf[at] = (byte)value;
		buf[at + 1] = (byte)(value >> 8);
		buf[at + 2] = (byte)(value >> 16);
		buf[at + 3] = (byte)(value >> 24);
	}

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
