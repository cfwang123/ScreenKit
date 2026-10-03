using System.Speech.Synthesis;

namespace ScreenKit;

/// <summary>词典发音目录与本地合成。不改语音合成页的设置，不下载语音。</summary>
static class DictTts {
	public const string AUTO = "auto";
	public const string SAPI = "sapi";
	public const string EDGE = "edge";

	public static string NormEngine(string raw) {
		var s = (raw ?? "").Trim().ToLowerInvariant();
		if (s == SAPI || s == EDGE || s == AUTO) return s;
		return AUTO;
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

	/// <summary>本地 SAPI。没有该语言语音时 missing=true，调用方改走 Edge。</summary>
	public static (float[] samples, int sampleRate, bool missing) SynthSapi(string text, string lang, string want) {
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
		if (string.IsNullOrEmpty(pick)) return (Array.Empty<float>(), 0, true);
		sapi.SelectVoice(pick);
		sapi.Rate = 0;
		var got = sapi.SynthToFloat(text);
		return (got.samples, got.sampleRate, false);
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

	static bool langof(VoiceInfo voice, string lang) {
		try {
			var two = voice?.Culture?.TwoLetterISOLanguageName ?? "";
			return string.Equals(two, lang, StringComparison.OrdinalIgnoreCase);
		}
		catch { return false; }
	}
}
