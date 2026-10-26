using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenKit;

/// <summary>本程序隐藏 Chrome 里的翻译语言包和离线语音包。不改日常浏览的用户数据。</summary>
sealed class ChromePackItem {
	public string Id { get; set; } = "";
	public string Kind { get; set; } = "";
	public string Title { get; set; } = "";
	public string Detail { get; set; } = "";
	public long Size { get; set; }
	public string Pair { get; set; } = "";
	public string Lang { get; set; } = "";
	public List<string> Langs { get; set; } = new();
	public FeatureInstallState State { get; set; }
	public bool Selected { get; set; }

	public string KindText => Kind == "tr" ? Loc.T("inst.chrome.tr") : Loc.T("inst.chrome.tts");

	public string SizeText {
		get {
			if (Size <= 0) return "";
			if (Size >= 1024 * 1024) return (Size / 1048576.0).ToString("0.#") + " MB";
			if (Size >= 1024) return (Size / 1024.0).ToString("0.#") + " KB";
			return Size + " B";
		}
	}

	public string StateText => State switch {
		FeatureInstallState.Installed => Loc.T("inst.installed"),
		FeatureInstallState.Partial => Loc.T("inst.partial"),
		_ => Loc.T("inst.missing"),
	};
}

/// <summary>HTTP 朗读时这一语言的离线语音包还没装。桌面朗读会安装，接口不装。</summary>
sealed class ChromeVoiceMissingException : InvalidOperationException {
	public ChromeVoiceMissingException(string lang)
		: base(Loc.T("tts.chrome.voice.need", lang ?? "")) { }
}

/// <summary>查询、安装、卸载 Chrome 翻译包（TranslateKit）和离线语音包（Wasm TTS）。</summary>
static class ChromePack {
	const string TTS_EXT = "gjjabgpgjpampikjhjpfhneeoapjbjaf";
	const string TTS_EXT_OLD = "nkeimhogjdpnpccoofpliimaahmaaome";
	const int SW_HIDE = 0;
	const int SW_RESTORE = 9;
	const uint SWP_SHOW = 0x0044;
	const int GWL_EXSTYLE = -20;
	const long WS_EX_TOOLWINDOW = 0x80;
	const long WS_EX_APPWINDOW = 0x40000;
	/// <summary>
	/// 语音包安装/查询的全局锁。锁序固定为 voiceOp → ChromeHost.gate：
	/// 持 voiceOp 时只单向调用 ChromeHost（Stop/Sess/Pin），ChromeHost 不能反过来拿 voiceOp。
	/// </summary>
	static readonly object voiceOp = new();
	static Dictionary<string, bool> voiceKnown;
	static bool wasmProbed;
	static bool wasmHave;

	/// <summary>已安装的翻译包或语音包有变化。可能在后台线程上触发。</summary>
	public static event Action Changed;

	public static readonly string[] TrPairs = {
		"en - es", "en - ja", "ar - en", "bn - en", "de - en", "en - fr", "en - hi", "en - it",
		"en - ko", "en - nl", "en - pl", "en - pt", "en - ru", "en - th", "en - tr", "en - vi",
		"en - zh", "en - zh-Hant", "bg - en", "cs - en", "da - en", "el - en", "en - fi", "en - hr",
		"en - hu", "en - id", "en - iw", "en - lt", "en - no", "en - ro", "en - sk", "en - sl",
		"en - sv", "en - uk", "en - kn", "en - ta", "en - te", "en - mr",
	};

	public static int Test() {
		if (TrFolder("en - zh") != "en_zh") return 2;
		if (TrFolder("en - zh-Hant") != "en_zh-Hant") return 2;
		if (VoiceLang("ja-jp-x-multi") != "ja-JP") return 2;
		if (VoiceLang("yue-hk-x-multi") != "yue-HK") return 2;
		if (VoiceLang("cmn-cn-x-multi") != "zh-CN") return 2;
		if (VoiceLang("cmn-tw-x-multi-seanet") != "zh-TW") return 2;
		if (VoiceLang("en-us") != "en-US") return 2;
		if (FilterCode("en - zh").IndexOf("zh") < 0 || FilterCode("en - zh").IndexOf("en") < 0) return 2;
		if (FilterCode("en - zh-Hant").IndexOf("cht") < 0) return 2;
		if (FilterCode("ja-JP") != "ja") return 2;
		if (FilterCode("yue-HK") != "yue") return 2;
		if (TrPairs.Length < 30) return 2;
		var seen = false;
		foreach (var p in TrPairs)
			if (p == "en - zh") seen = true;
		if (!seen) return 2;
		var both = pairlabel("en - zh");
		if (both.IndexOf('↔') < 0 || both.IndexOf('→') >= 0) return 2;
		if (pairlabel("en - zh-Hant").IndexOf('↔') < 0) return 2;
		if (PackState("Installed") != FeatureInstallState.Installed) return 2;
		if (PackState("not installed") != FeatureInstallState.Missing) return 2;
		if (PackState("notInstalled") != FeatureInstallState.Missing) return 2;
		if (PackState("installing") != FeatureInstallState.Partial) return 2;
		if (SpeakLang("zh-HK") != "yue-HK" || SpeakLang("yue") != "yue-HK") return 2;
		if (SpeakLang("zh-CN") != "zh-CN" || SpeakLang("zh") != "zh-CN") return 2;
		if (SpeakLang("zh-TW") != "zh-TW" || SpeakLang("cht") != "zh-TW") return 2;
		if (SpeakLang("ja") != "ja-JP" || SpeakLang("en-GB") != "en-GB") return 2;
		if (SpeakLang("EN-us") != "en-US") return 2;
		if (SpeakLang("") != "" || SpeakLang(null) != "") return 2;
		if (Math.Abs(frac(0.37, 1) - 0.37) > 0.001) return 2;
		if (Math.Abs(frac(37, 100) - 0.37) > 0.001) return 2;
		if (frac(50, 0) != 0) return 2;
		if (frac(-1, 1) != -1) return 2;
		if (Math.Abs(percentin("Installing 45%") - 0.45) > 0.001) return 2;
		if (Math.Abs(percentin("下载 12.5%") - 0.125) > 0.001) return 2;
		if (percentin("已安装") != -1) return 2;
		var voices = readvoices();
		if (voices.Count < VOICE_LANGS.Length) return 2;
		var hasZh = false;
		foreach (var v in voices) {
			if (v.Kind != "tts" || v.Lang != "zh-CN") continue;
			hasZh = true;
			if (v.Detail.Length > 0 && v.Detail != Loc.T("inst.chrome.langpack")) return 2;
		}
		if (!hasZh) return 2;
		if (basespeakers("zh-CN").Count != 6) return 2;
		foreach (var p in InstalledTrPairs()) {
			if (p == null || p.Length != 2) return 2;
			if (p[0].Length == 0 || p[1].Length == 0) return 2;
			if (string.Equals(p[0], p[1], StringComparison.OrdinalIgnoreCase)) return 2;
		}
		var marks = markinstalled("installedTimestamps{\"en-us\":1,\"cmn-CN\":2}installedTimestamps{\"ja-JP\":3,\"yue-HK\":4}");
		if (marks == null || marks.Count != 2 || !marks.Contains("ja-JP") || !marks.Contains("yue-HK")) return 2;
		if (marks.Contains("en-US") || marks.Contains("zh-CN")) return 2;
		if (markinstalled("none") != null) return 2;
		var fake = new List<string[]> { new[] { "zh", "en" }, new[] { "en", "ja" } };
		var tg = new List<string>();
		trtargets(fake, tg, "zh");
		if (!tg.Contains("ja") || tg.Contains("zh")) return 2;
		var tg2 = new List<string>();
		trtargets(fake, tg2, "ja");
		if (!tg2.Contains("zh")) return 2;
		var tg3 = new List<string>();
		trtargets(fake, tg3, "en");
		if (!tg3.Contains("zh") || !tg3.Contains("ja")) return 2;
		return 0;
	}

	/// <summary>朗读用的语言代码，对应离线语音包。粤语是 yue-HK。</summary>
	public static string SpeakLang(string culture) {
		var s = (culture ?? "").Trim().Replace('_', '-');
		if (s.Length == 0) return "";
		var lower = s.ToLowerInvariant();
		if (lower is "zh-hk" or "yue" or "yue-hk") return "yue-HK";
		if (lower is "zh" or "zh-cn" or "zh-hans") return "zh-CN";
		if (lower is "zh-tw" or "zh-hant" or "cht") return "zh-TW";
		var dash = s.IndexOf('-');
		if (dash > 0 && dash < s.Length - 1) {
			var region = s.Substring(dash + 1);
			var cut = region.IndexOf('-');
			if (cut > 0) region = region.Substring(0, cut);
			if (region.Length > 0)
				return s.Substring(0, dash).ToLowerInvariant() + "-" + region.ToUpperInvariant();
		}
		return lower switch {
			"ja" => "ja-JP",
			"ko" => "ko-KR",
			"en" => "en-US",
			"de" => "de-DE",
			"fr" => "fr-FR",
			"es" => "es-ES",
			"pt" => "pt-BR",
			"it" => "it-IT",
			"ru" => "ru-RU",
			"hi" => "hi-IN",
			"id" => "id-ID",
			"vi" => "vi-VN",
			"nl" => "nl-NL",
			"pl" => "pl-PL",
			_ => s,
		};
	}

	/// <summary>已知已安装为 true，已知未安装为 false，还没查过为 null。</summary>
	public static bool? VoiceInstalled(string lang) => installedof(SpeakLang(lang));

	/// <summary>桌面朗读：这一语言没装就装上。已安装则马上返回。progress 的 frac 小于 0 表示还没有百分比。</summary>
	public static void EnsureVoice(string lang, string exe, string proxyMode, string proxyAddr,
		Action<string, double, long, long> progress, CancellationToken ct) {
		lang = SpeakLang(lang);
		if (lang.Length == 0 || installedof(lang) == true) return;
		lock (voiceOp) {
			if (installedof(lang) == true) return;
			if (wasmProbed && !wasmHave) return;
			ct.ThrowIfCancellationRequested();
			progress?.Invoke(Loc.T("tts.chrome.voice.check"), -1, 0, 0);
			ChromeTts.Stop();
			preparevoice(ChromeTts.ProfileDir());
			using var sess = Sess.Open(ChromeTts.ProfileDir(), proxyMode, proxyAddr, null, ct, exe);
			if (!sess.TryWaitExt(15000, ct)) {
				wasmProbed = true;
				wasmHave = false;
				return;
			}
			FeatureInstallState state;
			try { state = onestatus(sess, lang, ct); }
			catch (InvalidOperationException ex) {
				if (noconnect(ex.Message)) {
					wasmProbed = true;
					wasmHave = false;
					return;
				}
				throw;
			}
			if (state == FeatureInstallState.Installed) {
				remember(lang, true);
				progress?.Invoke(Loc.T("tts.chrome.voice.done"), 1, 0, 0);
				return;
			}
			var note = Loc.T("tts.chrome.voice.install", lang);
			var expect = voicesize(lang);
			try {
				progress?.Invoke(note, -1, 0, expect);
				onevoice(sess, lang, true, ct, (part, got, total) => progress?.Invoke(note, part, got, total));
			}
			catch (InvalidOperationException ex) {
				if (noconnect(ex.Message)) {
					wasmProbed = true;
					wasmHave = false;
					return;
				}
				throw;
			}
			wasmProbed = true;
			wasmHave = true;
			remember(lang, true);
			progress?.Invoke(Loc.T("tts.chrome.voice.done"), 1, expect, expect);
		}
	}

	static bool noconnect(string msg) {
		return (msg ?? "").IndexOf("Receiving end does not exist", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	/// <summary>HTTP：只查询，不安装。未安装抛 <see cref="ChromeVoiceMissingException"/>。</summary>
	public static void RequireVoice(string lang, string exe, string proxyMode, string proxyAddr, CancellationToken ct) {
		lang = SpeakLang(lang);
		if (lang.Length == 0) return;
		var known = installedof(lang);
		if (known == true) return;
		if (known == false) throw new ChromeVoiceMissingException(lang);
		lock (voiceOp) {
			known = installedof(lang);
			if (known == true) return;
			if (known == false) throw new ChromeVoiceMissingException(lang);
			ct.ThrowIfCancellationRequested();
			ChromeTts.Stop();
			preparevoice(ChromeTts.ProfileDir());
			using var sess = Sess.Open(ChromeTts.ProfileDir(), proxyMode, proxyAddr, null, ct, exe);
			sess.WaitExt(15000, ct);
			var on = onestatus(sess, lang, ct) == FeatureInstallState.Installed;
			remember(lang, on);
			if (!on) throw new ChromeVoiceMissingException(lang);
		}
	}

	/// <summary>本机已装的翻译语言对。每项是规范化后的两侧语言代码。</summary>
	public static List<string[]> InstalledTrPairs() {
		var list = new List<string[]>();
		foreach (var it in tritems()) {
			if (it.State != FeatureInstallState.Installed || it.Langs == null || it.Langs.Count < 2) continue;
			var a = TrLang.Normalize(it.Langs[0]);
			var b = TrLang.Normalize(it.Langs[1]);
			if (a.Length == 0 || b.Length == 0 || a == TrLang.Auto || b == TrLang.Auto) continue;
			if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) continue;
			list.Add(new[] { a, b });
		}
		return list;
	}

	public static void AddInstalledTrLangs(List<string> into) {
		if (into == null) return;
		foreach (var p in InstalledTrPairs()) {
			addtrcode(into, p[0]);
			addtrcode(into, p[1]);
		}
	}

	/// <summary>已装模型能翻到的语言。非英文对由 Chrome 经英文中转，所以要 src↔en 和 en↔dst 都有。</summary>
	public static void AddInstalledTrTargets(List<string> into, string src) {
		trtargets(InstalledTrPairs(), into, src);
	}

	static void trtargets(List<string[]> pairs, List<string> into, string src) {
		if (into == null || pairs == null) return;
		src = TrLang.Normalize(src);
		if (src.Length == 0 || src == TrLang.Auto) return;
		var viaen = !string.Equals(src, TrLang.En, StringComparison.OrdinalIgnoreCase) && haspair(pairs, src, TrLang.En);
		foreach (var p in pairs) {
			if (p == null || p.Length < 2) continue;
			if (string.Equals(p[0], src, StringComparison.OrdinalIgnoreCase)) addtrcode(into, p[1]);
			else if (string.Equals(p[1], src, StringComparison.OrdinalIgnoreCase)) addtrcode(into, p[0]);
			else if (viaen) {
				if (string.Equals(p[0], TrLang.En, StringComparison.OrdinalIgnoreCase)) addtrcode(into, p[1]);
				else if (string.Equals(p[1], TrLang.En, StringComparison.OrdinalIgnoreCase)) addtrcode(into, p[0]);
			}
		}
	}

	static bool haspair(List<string[]> pairs, string a, string b) {
		foreach (var p in pairs) {
			if (p == null || p.Length < 2) continue;
			if (string.Equals(p[0], a, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(p[1], b, StringComparison.OrdinalIgnoreCase)) return true;
			if (string.Equals(p[0], b, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(p[1], a, StringComparison.OrdinalIgnoreCase)) return true;
		}
		return false;
	}

	static void addtrcode(List<string> into, string code) {
		code = TrLang.Normalize(code);
		if (code.Length == 0 || code == TrLang.Auto) return;
		foreach (var s in into)
			if (string.Equals(s, code, StringComparison.OrdinalIgnoreCase)) return;
		into.Add(code);
	}

	public static string TrFolder(string pair) => (pair ?? "").Replace(" - ", "_");

	/// <summary>筛选用的语言代码，逗号分隔。翻译包含两侧，语音包取主语言。</summary>
	public static string FilterCode(string text) {
		var raw = text ?? "";
		if (raw.Contains(" - ")) {
			var bits = raw.Split(new[] { " - " }, StringSplitOptions.None);
			var list = new List<string>();
			foreach (var b in bits) {
				var c = onecode(b);
				if (c.Length > 0 && !list.Contains(c)) list.Add(c);
			}
			return string.Join(",", list);
		}
		return onecode(raw);
	}

	static List<string> splitcodes(string text) {
		var list = new List<string>();
		foreach (var p in (text ?? "").Split(',')) {
			var s = p.Trim();
			if (s.Length > 0 && !list.Contains(s)) list.Add(s);
		}
		return list;
	}

	static string onecode(string code) {
		var s = (code ?? "").Trim().ToLowerInvariant().Replace('_', '-');
		if (s is "zh" or "zh-hans" or "zh-cn") return "zh";
		if (s is "zh-hant" or "zh-tw") return "cht";
		if (s is "zh-hk" or "yue" or "yue-hk") return "yue";
		if (s is "iw" or "iw-il") return "he";
		var i = s.IndexOf('-');
		if (i > 0) return onecode(s.Substring(0, i));
		return s;
	}

	public static string VoiceLang(string id) {
		var parts = (id ?? "").Split('-');
		if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0) return "";
		var lang = parts[0].ToLowerInvariant() + "-" + parts[1].ToUpperInvariant();
		if (lang.Equals("cmn-CN", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
		if (lang.Equals("cmn-TW", StringComparison.OrdinalIgnoreCase)) return "zh-TW";
		return lang;
	}

	public static List<ChromePackItem> Query(string proxyMode, string proxyAddr, Action<string> log, CancellationToken ct) {
		var list = new List<ChromePackItem>();
		list.AddRange(tritems());
		list.AddRange(ttsitems(proxyMode, proxyAddr, log, ct));
		return list;
	}

	/// <summary>翻译模型看本机目录，语音包看扩展记下的安装时间，都不打开 Chrome。</summary>
	public static List<ChromePackItem> Catalog(bool probe, string proxyMode, string proxyAddr, Action<string> log, CancellationToken ct) {
		if (probe) return Query(proxyMode, proxyAddr, log, ct);
		var list = tritems();
		list.AddRange(readvoices());
		return list;
	}

	public static void Apply(IReadOnlyList<ChromePackItem> items, bool install, string proxyMode, string proxyAddr, Action<string> log, Action<double> progress, Action<string> status, Action<string> bytes, CancellationToken ct) {
		var tr = new List<ChromePackItem>();
		var tts = new List<ChromePackItem>();
		if (items != null) {
			foreach (var it in items) {
				if (it == null) continue;
				if (it.Kind == "tr") tr.Add(it);
				else if (it.Kind == "tts") tts.Add(it);
			}
		}
		var total = tr.Count + tts.Count;
		if (total == 0) return;
		var done = 0;
		void tick(double part, string title, string byteLine) {
			var shown = part;
			if (shown < 0) shown = 0;
			if (shown > 1) shown = 1;
			progress?.Invoke((done + shown) / (double)total);
			string text;
			if (!install) text = Loc.T("inst.chrome.remove", title);
			else if (part < 0) text = Loc.T("inst.chrome.working", title);
			else text = Loc.T("inst.chrome.down", title, (int)Math.Round(part * 100));
			status?.Invoke(text);
			bytes?.Invoke(byteLine ?? "");
		}
		if (tr.Count > 0) {
			ChromeTr.Stop();
			using var sess = Sess.Open(ChromeTr.ProfileDir(), proxyMode, proxyAddr, log, ct);
			var web = false;
			if (install) {
				try { web = sess.TryTranslator(ct); }
				catch (Exception ex) { log?.Invoke(ex.Message); }
			}
			if (!web) sess.OpenInternals(ct);
			foreach (var it in tr) {
				ct.ThrowIfCancellationRequested();
				log?.Invoke((install ? "安装" : "卸载") + " " + it.Title);
				onepair(sess, it, install, web, tick, log, ct);
				done++;
				progress?.Invoke(done / (double)total);
			}
		}
		if (tts.Count == 0) {
			raisechanged();
			return;
		}
		lock (voiceOp) {
			ChromeTts.Stop();
			preparevoice(ChromeTts.ProfileDir());
			using var voice = Sess.Open(ChromeTts.ProfileDir(), proxyMode, proxyAddr, log, ct);
			voice.WaitExt(15000, ct);
			foreach (var it in tts) {
				ct.ThrowIfCancellationRequested();
				log?.Invoke((install ? "安装" : "卸载") + " " + it.Title);
				onevoice(voice, it.Lang, install, ct, (part, got, all) => {
					var line = "";
					if (all > 0) line = FeatureInstaller.FormatBytes(got) + " / " + FeatureInstaller.FormatBytes(all);
					else if (got > 0) line = FeatureInstaller.FormatBytes(got);
					tick(part, it.Title, line);
				});
				remember(it.Lang, install);
				done++;
				progress?.Invoke(done / (double)total);
			}
		}
		raisechanged();
	}

	static List<ChromePackItem> tritems() {
		var root = Path.Combine(ChromeTr.ProfileDir(), "TranslateKit", "models");
		var list = new List<ChromePackItem>();
		var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var pair in TrPairs) {
			var folder = TrFolder(pair);
			known.Add(folder);
			var dir = Path.Combine(root, folder);
			var on = Directory.Exists(dir);
			list.Add(new ChromePackItem {
				Id = "tr:" + pair,
				Kind = "tr",
				Pair = pair,
				Title = pairlabel(pair),
				Detail = pair,
				Langs = splitcodes(FilterCode(pair)),
				Size = on ? dirsize(dir) : 0,
				State = on ? FeatureInstallState.Installed : FeatureInstallState.Missing,
			});
		}
		if (!Directory.Exists(root)) return list;
		foreach (var dir in Directory.GetDirectories(root)) {
			var name = Path.GetFileName(dir);
			if (known.Contains(name)) continue;
			list.Add(new ChromePackItem {
				Id = "tr:" + name,
				Kind = "tr",
				Pair = name.Replace("_", " - "),
				Title = pairlabel(name.Replace("_", " - ")),
				Langs = splitcodes(FilterCode(name.Replace("_", " - "))),
				Detail = name,
				Size = dirsize(dir),
				State = FeatureInstallState.Installed,
			});
		}
		return list;
	}

	static string pairlabel(string pair) {
		var bits = (pair ?? "").Split(new[] { " - " }, StringSplitOptions.None);
		if (bits.Length != 2) return pair ?? "";
		return $"{langname(bits[0])} ↔ {langname(bits[1])}";
	}

	static string langname(string code) {
		code = (code ?? "").Trim();
		var key = code.ToLowerInvariant() switch {
			"zh" or "zh-hans" => "zh",
			"zh-hant" => "cht",
			"iw" => "he",
			"no" => "no",
			_ => code.ToLowerInvariant(),
		};
		var name = Loc.LangName(key);
		if (string.IsNullOrWhiteSpace(name) || name == key) return code;
		return name;
	}

	static List<ChromePackItem> ttsitems(string proxyMode, string proxyAddr, Action<string> log, CancellationToken ct) {
		var list = readvoices();
		if (list.Count == 0) {
			log?.Invoke(Loc.T("inst.chrome.novoice"));
			return list;
		}
		ct.ThrowIfCancellationRequested();
		var on = installedset(out var known);
		foreach (var it in list) {
			if (!known) {
				var cached = installedof(it.Lang);
				if (cached == true) it.State = FeatureInstallState.Installed;
				continue;
			}
			var hit = on.Contains(it.Lang);
			it.State = hit ? FeatureInstallState.Installed : FeatureInstallState.Missing;
			remember(it.Lang, hit);
		}
		return list;
	}

	/// <summary>扩展记在 installedTimestamps 里的语言。不启动 Chrome。known 为 false 表示还没有这份记录。</summary>
	static HashSet<string> installedset(out bool known) {
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		known = false;
		var when = DateTime.MinValue;
		foreach (var id in new[] { TTS_EXT, TTS_EXT_OLD }) {
			var dir = Path.Combine(ChromeTts.ProfileDir(), "Default", "Local Extension Settings", id);
			if (!Directory.Exists(dir)) continue;
			string[] files;
			try { files = Directory.GetFiles(dir); }
			catch { continue; }
			foreach (var file in files) {
				var name = Path.GetFileName(file);
				if (name.Equals("LOCK", StringComparison.OrdinalIgnoreCase)) continue;
				if (name.Equals("CURRENT", StringComparison.OrdinalIgnoreCase)) continue;
				if (name.StartsWith("MANIFEST", StringComparison.OrdinalIgnoreCase)) continue;
				if (name.Equals("LOG", StringComparison.OrdinalIgnoreCase) || name.Equals("LOG.old", StringComparison.OrdinalIgnoreCase)) continue;
				byte[] buf;
				try {
					using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
					if (fs.Length <= 0 || fs.Length > 8 * 1024 * 1024) continue;
					buf = new byte[fs.Length];
					var n = 0;
					while (n < buf.Length) {
						var got = fs.Read(buf, n, buf.Length - n);
						if (got <= 0) break;
						n += got;
					}
				}
				catch { continue; }
				var text = Encoding.ASCII.GetString(buf);
				var parsed = markinstalled(text);
				if (parsed == null) continue;
				var t = File.GetLastWriteTimeUtc(file);
				if (known && t < when) continue;
				when = t;
				set = parsed;
				known = true;
			}
		}
		return set;
	}

	/// <summary>文本里最后一条 installedTimestamps。没有这条记录时返回 null。</summary>
	static HashSet<string> markinstalled(string text) {
		if (string.IsNullOrEmpty(text)) return null;
		var idx = text.LastIndexOf("installedTimestamps", StringComparison.Ordinal);
		if (idx < 0) return null;
		var brace = text.IndexOf('{', idx);
		if (brace < 0 || brace - idx > 64) return null;
		var end = text.IndexOf('}', brace);
		if (end < 0) return null;
		var json = text.Substring(brace, end - brace + 1);
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		try {
			using var doc = JsonDocument.Parse(json);
			if (doc.RootElement.ValueKind != JsonValueKind.Object) return set;
			foreach (var prop in doc.RootElement.EnumerateObject()) {
				var lang = VoiceLang(prop.Name);
				if (lang.Length == 0) lang = SpeakLang(prop.Name);
				if (lang.Length > 0) set.Add(lang);
			}
		}
		catch { return null; }
		return set;
	}

	static readonly string[] VOICE_LANGS = {
		"zh-CN", "yue-HK", "zh-TW", "en-US", "en-GB", "ja-JP", "ko-KR",
		"de-DE", "fr-FR", "es-ES", "es-US", "pt-BR", "it-IT", "ru-RU",
		"hi-IN", "id-ID", "vi-VN", "nl-NL", "pl-PL",
	};

	static List<ChromePackItem> readvoices() {
		ensurevoices(ChromeTts.ProfileDir());
		var map = new Dictionary<string, ChromePackItem>(StringComparer.OrdinalIgnoreCase);
		foreach (var it in builtinvoices())
			if (it.Lang.Length > 0) map[it.Lang] = it;
		foreach (var it in readvoicefile())
			if (it.Lang.Length > 0) map[it.Lang] = it;
		var list = new List<ChromePackItem>(map.Values);
		list.Sort((a, b) => string.Compare(a.Lang, b.Lang, StringComparison.OrdinalIgnoreCase));
		return list;
	}

	static List<ChromePackItem> builtinvoices() {
		var list = new List<ChromePackItem>();
		foreach (var lang in VOICE_LANGS) {
			var key = FilterCode(lang);
			list.Add(new ChromePackItem {
				Id = "tts:" + lang,
				Kind = "tts",
				Lang = lang,
				Langs = splitcodes(FilterCode(lang)),
				Title = langname(key) + "  " + lang,
				Detail = "",
				State = FeatureInstallState.Missing,
			});
		}
		return list;
	}

	static List<ChromePackItem> readvoicefile() {
		var path = findvoices();
		var map = new Dictionary<string, ChromePackItem>(StringComparer.OrdinalIgnoreCase);
		var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		if (path == null) return new List<ChromePackItem>();
		JsonDocument doc;
		try { doc = JsonDocument.Parse(File.ReadAllText(path)); }
		catch { return new List<ChromePackItem>(); }
		using (doc) {
			if (doc.RootElement.ValueKind != JsonValueKind.Array) return new List<ChromePackItem>();
			foreach (var v in doc.RootElement.EnumerateArray()) {
				var id = v.TryGetProperty("id", out var idEl) ? idEl.GetString() : "";
				var lang = VoiceLang(id);
				if (lang.Length == 0) continue;
				if (!map.TryGetValue(lang, out var it)) {
					var baseCode = FilterCode(lang);
					it = new ChromePackItem {
						Id = "tts:" + lang,
						Kind = "tts",
						Lang = lang,
						Langs = splitcodes(FilterCode(lang)),
						Title = langname(baseCode) + "  " + lang,
						Detail = "",
						State = FeatureInstallState.Missing,
					};
					map[lang] = it;
				}
				if (v.TryGetProperty("compressedSize", out var sz) && sz.TryGetInt64(out var n))
					it.Size += n;
				if (dependentvoice(v)) continue;
				var added = speakercount(v);
				if (added <= 0) continue;
				counts.TryGetValue(lang, out var have);
				counts[lang] = have + added;
			}
		}
		foreach (var it in map.Values) {
			if (!counts.TryGetValue(it.Lang, out var n) || n <= 0) continue;
			// 装的是这个语言的语音语言包，里面的发音人 desktop Chrome 用不到，不按发音人计数
			it.Detail = Loc.T("inst.chrome.langpack");
		}
		var list = new List<ChromePackItem>(map.Values);
		list.Sort((a, b) => string.Compare(a.Lang, b.Lang, StringComparison.OrdinalIgnoreCase));
		return list;
	}

	/// <summary>这一语言的基础发音人，不含 Natural 增强包里的同名重复。</summary>
	public static List<(string name, string lang, string gender)> BaseSpeakers() => basespeakers(null);

	static List<(string name, string lang, string gender)> basespeakers(string only) {
		var list = new List<(string name, string lang, string gender)>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var path = findvoices();
		if (path != null) {
			try {
				using var doc = JsonDocument.Parse(File.ReadAllText(path));
				collectspeakers(doc.RootElement, only, list, seen);
			}
			catch { }
		}
		if (list.Count == 0) {
			var text = cmntext();
			if (text.Length > 0) {
				try {
					using var doc = JsonDocument.Parse(text);
					collectspeakers(doc.RootElement, only, list, seen);
				}
				catch { }
			}
		}
		return list;
	}

	static void collectspeakers(JsonElement root, string only, List<(string name, string lang, string gender)> list, HashSet<string> seen) {
		if (root.ValueKind != JsonValueKind.Array) return;
		foreach (var v in root.EnumerateArray()) {
			if (dependentvoice(v)) continue;
			var lang = VoiceLang(v.TryGetProperty("id", out var idEl) ? idEl.GetString() : "");
			if (lang.Length == 0) continue;
			if (only != null && !lang.Equals(only, StringComparison.OrdinalIgnoreCase)) continue;
			if (!v.TryGetProperty("speakers", out var sp) || sp.ValueKind != JsonValueKind.Array) continue;
			foreach (var s in sp.EnumerateArray()) {
				var name = s.TryGetProperty("name", out var ne) ? ne.GetString() : "";
				if (string.IsNullOrWhiteSpace(name)) continue;
				if (!seen.Add(lang + "\n" + name)) continue;
				var gender = s.TryGetProperty("gender", out var ge) ? ge.GetString() ?? "" : "";
				list.Add((name, lang, gender));
			}
		}
	}

	static bool dependentvoice(JsonElement v) {
		return v.TryGetProperty("dependentVoiceId", out var dep)
			&& dep.ValueKind == JsonValueKind.String
			&& !string.IsNullOrWhiteSpace(dep.GetString());
	}

	static int speakercount(JsonElement v) {
		if (!v.TryGetProperty("speakers", out var sp) || sp.ValueKind != JsonValueKind.Array) return 0;
		var n = 0;
		foreach (var s in sp.EnumerateArray()) {
			var name = s.TryGetProperty("name", out var ne) ? ne.GetString() : "";
			if (!string.IsNullOrWhiteSpace(name)) n++;
		}
		return n;
	}

	static bool voiceReady;

	static void reloadvoice(Sess sess, CancellationToken ct) {
		if (voiceReady) return;
		sess.ReloadExt(ct);
		if (!sess.TryWaitExt(20000, ct))
			throw new InvalidOperationException(Loc.T("inst.chrome.noext"));
		voiceReady = true;
	}

	static string cmnjson;

	static void ensurevoices(string profile) {
		try {
			if (string.IsNullOrWhiteSpace(profile)) return;
			var text = cmntext();
			if (text.Length == 0) return;
			var root = Path.Combine(profile, "WasmTtsEngine");
			if (!Directory.Exists(root)) return;
			var extra = JsonNode.Parse(text) as JsonArray;
			if (extra == null) return;
			foreach (var dir in Directory.GetDirectories(root)) {
				var file = Path.Combine(dir, "voices.json");
				if (!File.Exists(file)) continue;
				if (mergevoices(file, extra)) {
					forgetvoice("zh-CN");
					forgetvoice("zh-TW");
				}
			}
		}
		catch { }
	}

	static bool cataloghas(string profile) {
		var dir = Path.Combine(profile ?? "", "WasmTtsEngine");
		if (!Directory.Exists(dir)) return true;
		var file = newestvoices(dir);
		if (file == null) return true;
		try { return File.ReadAllText(file).IndexOf("\"cmn-cn-x-multi\"", StringComparison.Ordinal) >= 0; }
		catch { return false; }
	}

	static string cmntext() {
		if (cmnjson != null) return cmnjson;
		using var s = typeof(ChromePack).Assembly.GetManifestResourceStream("chrome.cmn.voices.json");
		if (s == null) return "";
		using var rd = new StreamReader(s, Encoding.UTF8);
		cmnjson = rd.ReadToEnd();
		return cmnjson ?? "";
	}

	static bool mergevoices(string file, JsonArray extra) {
		var root = JsonNode.Parse(File.ReadAllText(file)) as JsonArray;
		if (root == null) return false;
		var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var n in root) {
			var id = n?["id"]?.GetValue<string>() ?? "";
			if (id.Length > 0) have.Add(id);
		}
		var added = false;
		foreach (var n in extra) {
			var id = n?["id"]?.GetValue<string>() ?? "";
			if (id.Length == 0 || have.Contains(id)) continue;
			root.Add(JsonNode.Parse(n.ToJsonString()));
			have.Add(id);
			added = true;
		}
		if (!added) return false;
		File.WriteAllText(file, root.ToJsonString(), new UTF8Encoding(false));
		return true;
	}

	static void forgetvoice(string lang) {
		if (string.IsNullOrWhiteSpace(lang)) return;
		lock (voiceOp) {
			loadvoice();
			if (!voiceKnown.Remove(lang)) return;
			savevoice();
		}
	}

	static void preparevoice(string profile) {
		ensurevoices(profile);
		try {
			if (string.IsNullOrWhiteSpace(profile)) return;
			Directory.CreateDirectory(profile);
			var path = Path.Combine(profile, "Local State");
			JsonNode root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) : new JsonObject();
			if (root is not JsonObject) root = new JsonObject();
			var settings = child(root, "settings");
			var a11y = child(settings, "a11y");
			var read = child(a11y, "read_anything");
			var epoch = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var us = (DateTime.UtcNow.AddDays(-20) - epoch).Ticks / 10;
			read["last_opened_date"] = us.ToString();
			read["tts_engine_reinstalled"] = false;
			File.WriteAllText(path, root.ToJsonString(), new UTF8Encoding(false));
		}
		catch { }
	}

	static JsonObject child(JsonNode parent, string key) {
		if (parent[key] is JsonObject obj) return obj;
		var next = new JsonObject();
		parent[key] = next;
		return next;
	}

	static string findvoices() {
		var profile = newestvoices(Path.Combine(ChromeTts.ProfileDir(), "WasmTtsEngine"));
		if (profile != null) return profile;
		var user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "User Data", "WasmTtsEngine");
		return newestvoices(user);
	}

	static string newestvoices(string root) {
		if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;
		string best = null;
		var bestTime = DateTime.MinValue;
		foreach (var dir in Directory.GetDirectories(root)) {
			var file = Path.Combine(dir, "voices.json");
			if (!File.Exists(file)) continue;
			var t = File.GetLastWriteTimeUtc(file);
			if (t < bestTime) continue;
			bestTime = t;
			best = file;
		}
		return best;
	}

	static void onepair(Sess sess, ChromePackItem it, bool install, bool web, Action<double, string, string> report, Action<string> log, CancellationToken ct) {
		var pair = it.Pair;
		var title = string.IsNullOrWhiteSpace(it.Title) ? pair : it.Title;
		if (install && web && webpair(sess, pair, title, report, log, ct)) return;
		if (!sess.HasPage) sess.OpenInternals(ct);
		var want = install ? "install" : "uninstall";
		sess.ArmNet(ct);
		sess.ClearBytes();
		report(-1, title, "");
		var r = sess.EvalPage(clickjs(pair, want), false, 15000, ct);
		if (r == "already") {
			report(1, title, "");
			return;
		}
		if (r != "clicked") throw new InvalidOperationException(pair + " " + r);
		var root = ChromeTr.ProfileDir();
		var baseBytes = dirsize(root);
		var start = Environment.TickCount;
		while (unchecked(Environment.TickCount - start) < 180000) {
			ct.ThrowIfCancellationRequested();
			sess.PumpAll(350, ct);
			var text = sess.EvalPage(rowjs(pair), false, 8000, ct);
			var st = PackState(text);
			if ((install && st == FeatureInstallState.Installed) || (!install && st == FeatureInstallState.Missing)) {
				report(1, title, "");
				return;
			}
			var disk = dirsize(root) - baseBytes;
			if (disk < 0) disk = 0;
			liveline(sess, it.Size, disk, percentin(text), out var part, out var line);
			report(part, title, line);
		}
		throw new InvalidOperationException(pair + " " + Loc.T("inst.chrome.timeout"));
	}

	/// <summary>模型还要下载时，在已经打开的朗读页里带上用户手势再创建一次。</summary>
	public static void EnsurePair(string src, string dst, string exe, CancellationToken ct, Action<double> progress = null) {
		src = (src ?? "").Trim();
		dst = (dst ?? "").Trim();
		if (src.Length == 0 || dst.Length == 0)
			throw new InvalidOperationException("请选择源语言和目标语言");
		using var hold = ChromeHost.Pin();
		progress?.Invoke(-1);
		var port = ChromeHost.DebugPort();
		if (port > 0) {
			using var live = Sess.Attach(port);
			try {
				if (live.Download(src, dst, ct, progress, true)) return;
			}
			catch {
				ChromeHost.Stop();
				throw;
			}
		}
		ChromeTr.Stop();
		using var sess = Sess.Open(ChromeTr.ProfileDir(), HttpProxy.Mode, HttpProxy.Addr, null, ct, exe);
		if (!sess.Download(src, dst, ct, progress, false))
			throw new InvalidOperationException("Chrome 不能下载这对语言的翻译模型");
	}

	/// <summary>和网页「浏览器小模型」一样，用 Translator.create 的 downloadprogress。</summary>
	static bool webpair(Sess sess, string pair, string title, Action<double, string, string> report, Action<string> log, CancellationToken ct) {
		var bits = (pair ?? "").Split(new[] { " - " }, StringSplitOptions.None);
		if (bits.Length != 2) return false;
		sess.ArmNet(ct);
		sess.ClearBytes();
		report(-1, title, "");
		sess.RequireGoogle(ct);
		var started = sess.EvalWeb(trstart(apilang(bits[0]), apilang(bits[1])), false, 15000, ct);
		if (started != "started") return false;
		string net = "";
		var start = Environment.TickCount;
		var moved = start;
		var lastBytes = -1.0;
		while (unchecked(Environment.TickCount - start) < 180000) {
			ct.ThrowIfCancellationRequested();
			sess.PumpAll(300, ct);
			if (!readtr(sess.EvalWeb(TRPOLL, false, 8000, ct), out var p, out var seen, out var done, out var err, out var loaded, out var total)) {
				stallor(moved);
				continue;
			}
			if (err == "noapi") return false;
			var line = bytelinetext(loaded, total);
			if (done) {
				if (!string.IsNullOrWhiteSpace(err)) {
					log?.Invoke(pair + " " + err);
					return false;
				}
				report(1, title, line);
				return true;
			}
			bumpdl(seen, loaded, ref lastBytes, ref moved);
			stallor(moved);
			var part = seen ? p : -1.0;
			if (part < 0) liveline(sess, 0, 0, -1, out part, out net);
			if (line.Length == 0) line = net ?? "";
			report(part, title, line);
		}
		throw new InvalidOperationException(pair + " " + Loc.T("inst.chrome.timeout"));
	}

	static void onevoice(Sess sess, string lang, bool install, CancellationToken ct, Action<double, long, long> live = null) {
		var type = install ? "installLanguage" : "uninstallLanguage";
		var engine = enginelang(lang);
		var root = ChromeTts.ProfileDir();
		ensurevoices(root);
		if ((engine == "cmn-CN" || engine == "cmn-TW") && !cataloghas(root))
			throw new InvalidOperationException(Loc.T("inst.chrome.voicefail"));
		reloadvoice(sess, ct);
		var baseBytes = dirsize(root);
		var expect = voicesize(lang);
		sess.ArmNet(ct);
		sess.ClearBytes();
		var started = sess.EvalExt(voicestart(type, engine), false, 15000, ct);
		if (started != "started") throw new InvalidOperationException(lang + " " + started);
		sess.ArmNet(ct);
		var start = Environment.TickCount;
		while (unchecked(Environment.TickCount - start) < 300000) {
			ct.ThrowIfCancellationRequested();
			sess.PumpAll(350, ct);
			if (!readvoice(sess.EvalExt(VOICEPOLL, false, 8000, ct), out var done, out var err, out var body))
				continue;
			if (done) {
				var raw = string.IsNullOrWhiteSpace(body) ? err : body;
				if (string.IsNullOrWhiteSpace(raw)) raw = err ?? "";
				var st = statusof(raw);
				if (install && st == FeatureInstallState.Installed) {
					live?.Invoke(1, expect, expect);
					return;
				}
				if (!install && st == FeatureInstallState.Missing) {
					live?.Invoke(1, 0, 0);
					return;
				}
				if (voicefailed(raw))
					throw new InvalidOperationException(Loc.T("inst.chrome.voicefail"));
				throw new InvalidOperationException(lang + " " + raw);
			}
			var disk = dirsize(root) - baseBytes;
			if (disk < 0) disk = 0;
			liveline(sess, expect, disk, -1, out var part, out _);
			long got = 0;
			long all = 0;
			if (part >= 0 && expect > 0) {
				got = (long)(part * expect);
				all = expect;
			}
			else if (disk > 0) {
				got = disk;
				all = expect;
			}
			live?.Invoke(part, got, all);
		}
		throw new InvalidOperationException(lang + " " + Loc.T("inst.chrome.timeout"));
	}

	static void liveline(Sess sess, long expect, long disk, double rowFrac, out double part, out string line) {
		part = rowFrac;
		line = "";
		var net = sess.TakeFrac(expect, out var netLine);
		if (part < 0) part = net;
		if (!string.IsNullOrEmpty(netLine)) line = netLine;
		if (part < 0 && disk > 0 && expect > 0) {
			part = disk / (double)expect;
			if (part > 0.99) part = 0.99;
			line = FeatureInstaller.FormatBytes(disk) + " / " + FeatureInstaller.FormatBytes(expect);
		}
		else if (line.Length == 0 && disk > 0)
			line = FeatureInstaller.FormatBytes(disk);
	}

	static FeatureInstallState onestatus(Sess sess, string lang, CancellationToken ct) {
		ensurevoices(ChromeTts.ProfileDir());
		reloadvoice(sess, ct);
		var key = enginelang(lang);
		var st = sess.VoiceStatus(new List<string> { key }, ct);
		if (!st.TryGetValue(key, out var raw)) st.TryGetValue(lang, out raw);
		return PackState(raw);
	}

	static string enginelang(string lang) {
		var s = SpeakLang(lang);
		if (s.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)) return "cmn-CN";
		if (s.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)) return "cmn-TW";
		return s;
	}

	static long voicesize(string lang) {
		foreach (var it in readvoices())
			if (string.Equals(it.Lang, lang, StringComparison.OrdinalIgnoreCase) && it.Size > 0)
				return it.Size;
		return 0;
	}

	static bool? installedof(string lang) {
		if (string.IsNullOrWhiteSpace(lang)) return null;
		lock (voiceOp) {
			loadvoice();
			if (!voiceKnown.TryGetValue(lang, out var on)) return null;
			return on;
		}
	}

	static void remember(string lang, bool on) {
		if (string.IsNullOrWhiteSpace(lang)) return;
		var fire = false;
		lock (voiceOp) {
			loadvoice();
			if (voiceKnown.TryGetValue(lang, out var cur) && cur == on) return;
			voiceKnown[lang] = on;
			savevoice();
			fire = true;
		}
		if (fire) raisechanged();
	}

	static void raisechanged() {
		try { Changed?.Invoke(); } catch { }
	}

	static void loadvoice() {
		if (voiceKnown != null) return;
		voiceKnown = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
		var path = voicefile();
		if (!File.Exists(path)) return;
		string[] lines;
		try { lines = File.ReadAllLines(path); }
		catch { return; }
		foreach (var line in lines) {
			var s = (line ?? "").Trim();
			if (s.Length == 0 || s[0] == '#') continue;
			var i = s.IndexOf(' ');
			if (i <= 0) continue;
			var key = s.Substring(0, i).Trim();
			var bit = s.Substring(i + 1).Trim();
			if (key.Length == 0) continue;
			if (bit == "1") voiceKnown[key] = true;
			else if (bit == "0") voiceKnown[key] = false;
		}
	}

	static void savevoice() {
		var sb = new StringBuilder();
		foreach (var kv in voiceKnown)
			sb.Append(kv.Key).Append(kv.Value ? " 1" : " 0").Append('\n');
		try { File.WriteAllText(voicefile(), sb.ToString(), new UTF8Encoding(false)); }
		catch { }
	}

	static string voicefile() => Path.Combine(ChromeTts.ProfileDir(), "voice-state.txt");

	static bool voicefailed(string raw) {
		var s = raw ?? "";
		return s.IndexOf("\"status\":\"failed\"", StringComparison.OrdinalIgnoreCase) >= 0
			|| s.IndexOf("\"status\": \"failed\"", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	static FeatureInstallState statusof(string raw) {
		if (string.IsNullOrWhiteSpace(raw)) return FeatureInstallState.Missing;
		try {
			using var doc = JsonDocument.Parse(raw);
			if (doc.RootElement.ValueKind == JsonValueKind.Object) {
				var status = fieldstatus(doc.RootElement);
				if (status.Length > 0) return PackState(status);
				if (doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) {
					var msg = e.GetString();
					if (!string.IsNullOrWhiteSpace(msg))
						throw new InvalidOperationException(msg);
				}
			}
		}
		catch (JsonException) { }
		catch (InvalidOperationException) { throw; }
		return PackState(raw);
	}

	static string fieldstatus(JsonElement el) {
		var status = "";
		if (el.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String)
			status = s.GetString() ?? "";
		if (status.Length == 0 && el.TryGetProperty("installStatus", out var i) && i.ValueKind == JsonValueKind.String)
			status = i.GetString() ?? "";
		return status;
	}

	public static FeatureInstallState PackState(string text) {
		var s = (text ?? "").ToLowerInvariant();
		if (s.Contains("not installed") || s.Contains("notinstalled") || s.Contains("未安装"))
			return FeatureInstallState.Missing;
		if (s.Contains("installing") || s.Contains("正在") || s.Contains("安装中"))
			return FeatureInstallState.Partial;
		if (s.Contains("installed") || s.Contains("已安装"))
			return FeatureInstallState.Installed;
		if (s.Contains("uninstall") || s.Contains("卸载"))
			return FeatureInstallState.Installed;
		return FeatureInstallState.Missing;
	}

	static string jsstr(string s) {
		return "'" + (s ?? "").Replace("\\", "\\\\").Replace("'", "\\'") + "'";
	}

	static string clickjs(string pair, string want) {
		return "(function(){var pair=" + jsstr(pair) + ";var want=" + jsstr(want) + ";" + CLICK + "})()";
	}

	static string rowjs(string pair) {
		return "(function(){var pair=" + jsstr(pair) + ";" + ROW + "})()";
	}

	static string voicestart(string type, string lang) {
		return "(function(){var type=" + jsstr(type) + ";var lang=" + jsstr(lang) + ";" + VOICEASK + VOICESTART + "})()";
	}

	static string trstart(string src, string dst) {
		return "(function(){var src=" + jsstr(src) + ";var dst=" + jsstr(dst) + ";" + TRSTART + "})()";
	}

	static string apilang(string code) {
		var s = (code ?? "").Trim();
		if (s.Equals("iw", StringComparison.OrdinalIgnoreCase)) return "he";
		return s;
	}

	/// <summary>与网页 downloadprogress 相同：0–1 直接用，大于 1 时按 total 换算。没有进度返回 -1。</summary>
	static double frac(double loaded, double total) {
		if (double.IsNaN(loaded) || loaded < 0) return -1;
		var n = loaded;
		if (n > 1) {
			if (!(total > 0)) return 0;
			n = n / total;
		}
		if (n < 0) n = 0;
		if (n > 1) n = 1;
		return n;
	}

	static double percentin(string text) {
		var s = text ?? "";
		for (var i = 0; i < s.Length; i++) {
			if (s[i] != '%') continue;
			var j = i - 1;
			while (j >= 0 && s[j] == ' ') j--;
			var end = j + 1;
			while (j >= 0 && (char.IsDigit(s[j]) || s[j] == '.')) j--;
			if (end <= j + 1) continue;
			var num = s.Substring(j + 1, end - (j + 1));
			if (!double.TryParse(num, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
				continue;
			if (v < 0) v = 0;
			if (v > 100) v = 100;
			return v / 100.0;
		}
		return -1;
	}

	static string bytelinetext(double loaded, double total) {
		if (total > 1)
			return FeatureInstaller.FormatBytes((long)loaded) + " / " + FeatureInstaller.FormatBytes((long)total);
		if (loaded > 1) return FeatureInstaller.FormatBytes((long)loaded);
		return "";
	}

	const int DLSTALL = 20000;

	/// <summary>有新的下载字节才算往前走。一直是 0 不算。</summary>
	static void bumpdl(bool seen, double loaded, ref double last, ref int tick) {
		if (!seen || loaded <= 0) return;
		if (last >= 0 && loaded <= last) return;
		last = loaded;
		tick = Environment.TickCount;
	}

	static void stallor(int tick) {
		if (unchecked(Environment.TickCount - tick) >= DLSTALL)
			throw new InvalidOperationException(Loc.T("inst.chrome.stall"));
	}

	static bool readtr(string raw, out double p, out bool seen, out bool done, out string err, out double loaded, out double total) {
		p = 0;
		seen = false;
		done = false;
		err = "";
		loaded = 0;
		total = 0;
		if (string.IsNullOrWhiteSpace(raw)) return false;
		try {
			using var doc = JsonDocument.Parse(raw);
			var root = doc.RootElement;
			if (root.TryGetProperty("p", out var pv) && pv.TryGetDouble(out var d)) p = d;
			seen = root.TryGetProperty("seen", out var sv) && sv.ValueKind == JsonValueKind.True;
			done = root.TryGetProperty("done", out var dv) && dv.ValueKind == JsonValueKind.True;
			if (root.TryGetProperty("err", out var ev) && ev.ValueKind == JsonValueKind.String) err = ev.GetString() ?? "";
			if (root.TryGetProperty("loaded", out var lv) && lv.TryGetDouble(out var ld)) loaded = ld;
			if (root.TryGetProperty("total", out var tv) && tv.TryGetDouble(out var td)) total = td;
			return true;
		}
		catch { return false; }
	}

	static bool readvoice(string raw, out bool done, out string err, out string body) {
		done = false;
		err = "";
		body = "";
		if (string.IsNullOrWhiteSpace(raw)) return false;
		try {
			using var doc = JsonDocument.Parse(raw);
			var root = doc.RootElement;
			done = root.TryGetProperty("done", out var dv) && dv.ValueKind == JsonValueKind.True;
			if (root.TryGetProperty("err", out var ev) && ev.ValueKind == JsonValueKind.String) err = ev.GetString() ?? "";
			if (root.TryGetProperty("raw", out var rv) && rv.ValueKind == JsonValueKind.String) body = rv.GetString() ?? "";
			return true;
		}
		catch { return false; }
	}

	const string WALK = @"
function nodes(n,acc){if(!n)return;if(n.shadowRoot)nodes(n.shadowRoot,acc);acc.push(n);var ch=n.children||[];for(var i=0;i<ch.length;i++)nodes(ch[i],acc);}
function text(n){if(!n)return '';var s='';if(n.shadowRoot)s+=text(n.shadowRoot);var ch=n.childNodes||[];for(var i=0;i<ch.length;i++){if(ch[i].nodeType===3)s+=ch[i].textContent;else s+=text(ch[i]);}return s;}
function hit(t){if(t.indexOf(pair)!==0)return false;var rest=t.slice(pair.length);return !rest.length||rest.charAt(0)===' ';}
function findrow(){var all=[];nodes(document.body,all);var best=null,bestLen=1e9;for(var i=0;i<all.length;i++){var t=(text(all[i])||'').replace(/\s+/g,' ').trim();if(!hit(t)||t.length>200)continue;var kids=[];nodes(all[i],kids);var has=false;for(var j=0;j<kids.length;j++){var tag=(kids[j].tagName||'').toLowerCase();if(tag==='button'||tag==='cr-button'){has=true;break;}}if(!has)continue;if(t.length<bestLen){best=all[i];bestLen=t.length;}}return best;}
";

	const string CLICK = WALK + @"
var row=findrow();if(!row)return 'missing';
var all=[];nodes(row,all);var btn=null;
for(var i=0;i<all.length;i++){var tag=(all[i].tagName||'').toLowerCase();if(tag==='button'||tag==='cr-button'){btn=all[i];break;}}
if(!btn)return 'nobutton';
var label=(btn.innerText||text(btn)||'').replace(/\s+/g,' ').trim().toLowerCase();
var isUn=label.indexOf('uninstall')>=0||label.indexOf('卸载')>=0||label.indexOf('移除')>=0;
var isIn=label.indexOf('install')>=0||label.indexOf('安装')>=0;
if(want==='install'&&isUn)return 'already';
if(want==='uninstall'&&isIn&&label.indexOf('uninstall')<0&&label.indexOf('卸载')<0)return 'already';
btn.click();return 'clicked';
";

	const string ROW = WALK + @"
var row=findrow();return row?(text(row)||'').replace(/\s+/g,' ').trim():'';
";

	const string VOICEASK = @"
function ask(msg){
return new Promise(function(resolve){
var n=0;
function go(){
var h=self.$h;
if(h&&msg.type==='installLanguage'&&h.onInstallLanguageRequest){h.onInstallLanguageRequest(msg.lang).then(function(r){resolve(r||{});},function(e){resolve({error:String(e&&e.message||e)});});return;}
if(h&&msg.type==='getLanguageStatus'&&h.onLanguageStatusRequest){h.onLanguageStatusRequest(msg.lang).then(function(r){resolve(r||{});},function(e){resolve({error:String(e&&e.message||e)});});return;}
if(h&&msg.type==='uninstallLanguage'&&h.onUninstallLanguageRequest){h.onUninstallLanguageRequest(msg.lang).then(function(r){resolve(r||{});},function(e){resolve({error:String(e&&e.message||e)});});return;}
if(n++<50&&!h&&String(location.href||'').indexOf('offscreen')>=0){setTimeout(go,100);return;}
try{chrome.runtime.sendMessage(msg,function(r){var err=chrome.runtime.lastError;resolve(err?{error:err.message}:(r||{}));});}
catch(e){resolve({error:String(e&&e.message||e)});}
}
go();
});
}
";

	const string VOICESTART = @"
self.__skp={done:false,err:'',raw:''};
function ready(){
if(!chrome.offscreen||!chrome.offscreen.createDocument)return Promise.resolve();
return chrome.offscreen.hasDocument().catch(function(){return false;}).then(function(has){
if(has)return;
return chrome.offscreen.createDocument({url:'offscreen.html',reasons:['AUDIO_PLAYBACK','USER_MEDIA'],justification:'ScreenKit voice pack'}).catch(function(){});
});
}
ready().then(function(){
return ask({type:type,lang:lang});
}).then(function(res){
self.__skp.raw=JSON.stringify(res||{});
self.__skp.err=(res&&res.error)||'';
self.__skp.done=true;
}).catch(function(e){self.__skp.err=String(e&&e.message||e);self.__skp.done=true;});
return 'started';
";

	const string VOICEPOLL = "(function(){var s=self.__skp||{};return JSON.stringify({done:!!s.done,err:s.err||'',raw:s.raw||''});})()";

	const string GOOGLEUP = "(async function(){var c=new AbortController();var t=setTimeout(function(){try{c.abort();}catch(e){}},8000);"
		+ "try{await fetch('https://www.google.com/generate_204',{mode:'no-cors',cache:'no-store',signal:c.signal});clearTimeout(t);return 'up';}"
		+ "catch(e){clearTimeout(t);return 'down';}})()";

	const string TRSTART = @"
window.__skp={p:0,seen:false,done:false,err:'',loaded:0,total:0};
var T=window.Translator;
if(!T||typeof T.create!=='function'){window.__skp.err='noapi';window.__skp.done=true;return 'noapi';}
var opt={sourceLanguage:src,targetLanguage:dst};
opt.monitor=function(m){
if(!m||!m.addEventListener)return;
m.addEventListener('downloadprogress',function(e){
var n=e&&e.loaded;if(n==null)return;
var total=e.total||0;
window.__skp.loaded=n;window.__skp.total=total;window.__skp.seen=true;
if(n>1)n=total?n/total:0;
if(n<0)n=0;if(n>1)n=1;
window.__skp.p=n;
});
};
try{
T.create(opt).then(function(){window.__skp.p=1;window.__skp.seen=true;window.__skp.done=true;},function(err){
window.__skp.err=(err&&(err.message||String(err)))||'error';
window.__skp.done=true;
});
}catch(ex){window.__skp.err=String(ex);window.__skp.done=true;return 'noapi';}
return 'started';
";

	const string TRPOLL = "(function(){var s=window.__skp||{};return JSON.stringify({p:s.p||0,seen:!!s.seen,done:!!s.done,err:s.err||'',loaded:s.loaded||0,total:s.total||0});})()";

	static long dirsize(string dir) {
		long n = 0;
		try {
			foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) {
				try { n += new FileInfo(f).Length; } catch { }
			}
		}
		catch { }
		return n;
	}

	public static string NudgeSpeak(int debugPort, int jobId, int pagePort, CancellationToken ct) {
		if (debugPort <= 0 || jobId <= 0) return "";
		using var sess = Sess.Attach(debugPort);
		return sess.PumpSpeak(jobId, false, pagePort, ct);
	}

	public static void FailSpeak(int debugPort, int jobId, int pagePort, CancellationToken ct) {
		if (debugPort <= 0 || jobId <= 0) return;
		using var sess = Sess.Attach(debugPort);
		sess.PumpSpeak(jobId, true, pagePort, ct);
	}

	sealed class Sess : IDisposable {
		Process proc;
		int port;
		bool own = true;
		Sock page;
		Sock web;
		Sock ext;
		Sock off;
		Sock browser;
		bool webNet;
		bool pageNet;
		bool extNet;
		bool offNet;
		bool browserNet;
		bool dlNet;

		public bool HasPage => page != null;

		public static Sess Attach(int debugPort) {
			var sess = new Sess { own = false, port = debugPort };
			return sess;
		}

		public string PumpSpeak(int jobId, bool fail, int pagePort, CancellationToken ct) {
			var call = fail ? "__skfail" : "__skpump";
			var expr = "(function(){return window." + call + "?window." + call + "(" + jobId + "):'nopage';})()";
			var best = "idle";
			var mark = pagePort > 0 ? ":" + pagePort + "/boot.html" : "boot.html";
			var req = WebRequest.Create("http://127.0.0.1:" + port + "/json/list");
			req.Timeout = 4000;
			string text;
			using (var resp = req.GetResponse())
			using (var rd = new StreamReader(resp.GetResponseStream()))
				text = rd.ReadToEnd();
			using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "[]" : text);
			if (doc.RootElement.ValueKind != JsonValueKind.Array) return best;
			foreach (var el in doc.RootElement.EnumerateArray()) {
				var url = el.TryGetProperty("url", out var u) ? u.GetString() : "";
				if ((url ?? "").IndexOf(mark, StringComparison.OrdinalIgnoreCase) < 0) continue;
				var ws = el.TryGetProperty("webSocketDebuggerUrl", out var w) ? w.GetString() : "";
				if (string.IsNullOrEmpty(ws)) continue;
				Sock sock = null;
				try {
					sock = Sock.Connect(ws);
					var r = sock.Eval(expr, false, 4000, ct);
					if (r == "armed" || r == "busy" || r == "fail") return r;
					if (r == "novoice") best = "novoice";
					else if (r == "nopage" && best == "idle") best = "nopage";
				}
				catch { }
				finally {
					try { sock?.Dispose(); } catch { }
				}
			}
			return best;
		}

		/// <summary>专用配置的 Chrome 已经开着调试口时，接上它，不另开一只、退出时也不关掉。</summary>
		static Sess tryattach(string profile) {
			try {
				if (string.IsNullOrWhiteSpace(profile)) return null;
				var path = Path.Combine(profile, "DevToolsActivePort");
				if (!File.Exists(path)) return null;
				var line = File.ReadAllLines(path);
				if (line.Length == 0 || !int.TryParse(line[0].Trim(), out var port) || port <= 0) return null;
				var req = WebRequest.Create("http://127.0.0.1:" + port + "/json/version");
				req.Timeout = 400;
				using (req.GetResponse()) { }
				return new Sess { own = false, port = port };
			}
			catch { return null; }
		}

		/// <summary>在已打开的页面里创建翻译器。boot 为真时只用朗读页，不另开窗口。</summary>
		public bool Download(string src, string dst, CancellationToken ct, Action<double> progress, bool boot) {
			if (boot) {
				web = connect("page", "boot.html");
				if (web == null) return false;
			}
			else if (!TryTranslator(ct)) return false;
			RequireGoogle(ct);
			var started = EvalWeb(trstart(src, dst), false, 15000, ct);
			if (started != "started") return false;
			var start = Environment.TickCount;
			var moved = start;
			var lastBytes = -1.0;
			while (unchecked(Environment.TickCount - start) < 180000) {
				ct.ThrowIfCancellationRequested();
				PumpAll(300, ct);
				if (!readtr(EvalWeb(TRPOLL, false, 8000, ct), out var p, out var seen, out var done, out var err, out var loaded, out _)) {
					stallor(moved);
					continue;
				}
				if (!done) {
					bumpdl(seen, loaded, ref lastBytes, ref moved);
					stallor(moved);
					progress?.Invoke(seen ? p : -1);
					continue;
				}
				if (!string.IsNullOrWhiteSpace(err))
					throw new InvalidOperationException(err);
				progress?.Invoke(1);
				return true;
			}
			throw new InvalidOperationException(Loc.T("inst.chrome.timeout"));
		}

		Sess() { }

		public static Sess Open(string profile, string proxyMode, string proxyAddr, Action<string> log, CancellationToken ct, string chromeExe = null) {
			var live = tryattach(profile);
			if (live != null) {
				log?.Invoke(Loc.T("inst.chrome.open"));
				return live;
			}
			var exe = ChromeTts.FindExe(chromeExe);
			if (exe == null) throw new InvalidOperationException(Loc.T("inst.chrome.need"));
			log?.Invoke(Loc.T("inst.chrome.open"));
			var sess = new Sess();
			var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			sess.port = ((IPEndPoint)listener.LocalEndpoint).Port;
			listener.Stop();
			var args = new StringBuilder();
			args.Append("--user-data-dir=\"").Append(profile).Append("\" ");
			args.Append("--remote-debugging-address=127.0.0.1 ");
			args.Append("--remote-debugging-port=").Append(sess.port).Append(' ');
			args.Append("--no-first-run --no-default-browser-check --disable-sync ");
			args.Append("--disable-breakpad --disable-crash-reporter ");
			args.Append("--disable-session-crashed-bubble --hide-crash-restore-bubble ");
			args.Append("--disable-backgrounding-occluded-windows --disable-renderer-backgrounding ");
			args.Append("--window-position=-32000,-32000 --window-size=480,320 ");
			args.Append("--disable-features=UseSystemProxyResolver ");
			appendproxy(args, proxyMode, proxyAddr);
			args.Append("about:blank");
			sess.proc = ChromeTts.StartProc(exe, args.ToString());
			if (sess.proc == null) throw new InvalidOperationException(Loc.T("inst.chrome.exit"));
			var start = Environment.TickCount;
			while (unchecked(Environment.TickCount - start) < 15000) {
				ct.ThrowIfCancellationRequested();
				sess.hidewin();
				try {
					var req = WebRequest.Create("http://127.0.0.1:" + sess.port + "/json/version");
					req.Timeout = 500;
					using var resp = req.GetResponse();
					return sess;
				}
				catch { Thread.Sleep(150); }
			}
			sess.Dispose();
			throw new InvalidOperationException(Loc.T("inst.chrome.exit"));
		}

		public void OpenPage(string url) {
			var req = WebRequest.Create("http://127.0.0.1:" + port + "/json/new?" + Uri.EscapeDataString(url));
			req.Method = "PUT";
			req.ContentLength = 0;
			req.Timeout = 8000;
			using var resp = req.GetResponse();
			using var rd = new StreamReader(resp.GetResponseStream());
			rd.ReadToEnd();
		}

		public void WaitPage(string part, int ms, CancellationToken ct) {
			var start = Environment.TickCount;
			while (unchecked(Environment.TickCount - start) < ms) {
				ct.ThrowIfCancellationRequested();
				hidewin();
				page ??= connect("page", "on-device-translation");
				if (page != null) {
					var text = EvalPage("(function(){function text(n){if(!n)return '';var s='';if(n.shadowRoot)s+=text(n.shadowRoot);var ch=n.childNodes||[];for(var i=0;i<ch.length;i++){if(ch[i].nodeType===3)s+=ch[i].textContent;else s+=text(ch[i]);}return s;}return text(document.body);})()", false, 8000, ct);
					if ((text ?? "").Contains(part)) return;
				}
				Thread.Sleep(250);
			}
			throw new InvalidOperationException(Loc.T("inst.chrome.nopage"));
		}

		public void WaitExt(int ms, CancellationToken ct) {
			if (!TryWaitExt(ms, ct))
				throw new InvalidOperationException(Loc.T("inst.chrome.noext"));
		}

		public bool TryWaitExt(int ms, CancellationToken ct) {
			var start = Environment.TickCount;
			var prev = GetForegroundWindow();
			var taps = 0;
			var nextTap = 0;
			var armed = false;
			var tried = false;
			while (unchecked(Environment.TickCount - start) < ms) {
				ct.ThrowIfCancellationRequested();
				ext ??= connectext();
				if (ext != null) {
					finishwait(prev, ct);
					return true;
				}
				if (!tried) {
					retitle(ct);
					tried = true;
				}
				var hwnd = chromehwnd();
				if (hwnd != IntPtr.Zero) {
					lift(hwnd);
					ShowWindow(hwnd, SW_RESTORE);
					SetWindowPos(hwnd, IntPtr.Zero, 80, 80, 720, 480, SWP_SHOW);
					if (GetForegroundWindow() != hwnd) focus(hwnd);
					if (!armed && GetForegroundWindow() == hwnd) {
						warmblank(ct);
						armed = true;
						nextTap = unchecked(Environment.TickCount + 400);
					}
					var now = Environment.TickCount;
					if (GetForegroundWindow() == hwnd && taps < 2 && nextTap != 0 && unchecked(now - nextTap) >= 0) {
						if (taps == 0 || !readingopen()) {
							tapread();
							taps++;
							nextTap = unchecked(now + 2500);
						}
					}
				}
				Thread.Sleep(200);
			}
			finishwait(prev, ct);
			return false;
		}

		void finishwait(IntPtr prev, CancellationToken ct) {
			restoretitle(ct);
			var hwnd = chromehwnd();
			if (hwnd != IntPtr.Zero) sink(hwnd);
			giveback(prev);
			hideown();
		}

		string oldtitle;

		void retitle(CancellationToken ct) {
			Sock page = null;
			try {
				page = connect("page", "boot.html");
				if (page == null) return;
				var title = page.Eval("(function(){var t=document.title||'';if(t.indexOf('skc')!==0)return '';document.title='voice';return t;})()", false, 2000, ct);
				if (!string.IsNullOrEmpty(title)) oldtitle = title;
			}
			catch { }
			finally { try { page?.Dispose(); } catch { } }
		}

		void restoretitle(CancellationToken ct) {
			if (string.IsNullOrEmpty(oldtitle)) return;
			var title = oldtitle;
			oldtitle = null;
			Sock page = null;
			try {
				page = connect("page", "boot.html") ?? connect("page", "voice");
				if (page == null) return;
				page.Eval("document.title=" + JsonSerializer.Serialize(title), false, 2000, ct);
			}
			catch { }
			finally { try { page?.Dispose(); } catch { } }
		}

		void warmblank(CancellationToken ct) {
			Sock page = null;
			try {
				page = connect("page", "about:blank");
				if (page == null) return;
				page.Call("Page.navigate", "{\"url\":\"data:text/html,<title>voice</title><body style='background:#fff;color:#000'>voice</body>\"}", ct);
			}
			catch { }
			finally { try { page?.Dispose(); } catch { } }
		}

		static void lift(IntPtr hwnd) {
			var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
			var next = (ex & ~WS_EX_TOOLWINDOW) | WS_EX_APPWINDOW;
			if (next != ex) SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(next));
		}

		static void sink(IntPtr hwnd) {
			var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
			var next = (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
			if (next != ex) SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(next));
		}

		void giveback(IntPtr prev) {
			var hwnd = chromehwnd();
			if (prev == IntPtr.Zero || prev == hwnd || !IsWindow(prev)) return;
			focus(prev);
		}

		bool readingopen() {
			try {
				var req = WebRequest.Create("http://127.0.0.1:" + port + "/json/list");
				req.Timeout = 2000;
				using var resp = req.GetResponse();
				using var rd = new StreamReader(resp.GetResponseStream());
				var text = rd.ReadToEnd();
				return text.IndexOf("read-anything", StringComparison.OrdinalIgnoreCase) >= 0
					|| text.IndexOf(TTS_EXT, StringComparison.OrdinalIgnoreCase) >= 0;
			}
			catch { return false; }
		}

		void hideown() {
			var pid = ownpid();
			if (pid == 0) {
				hidewin();
				return;
			}
			EnumWindows((h, _) => {
				GetWindowThreadProcessId(h, out var wpid);
				if (wpid == pid) ShowWindow(h, SW_HIDE);
				return true;
			}, IntPtr.Zero);
		}

		static void tapread() {
			keybd_event(0x12, 0, 0, UIntPtr.Zero);
			keybd_event(0x10, 0, 0, UIntPtr.Zero);
			keybd_event(0x52, 0, 0, UIntPtr.Zero);
			Thread.Sleep(40);
			keybd_event(0x52, 0, 2, UIntPtr.Zero);
			keybd_event(0x10, 0, 2, UIntPtr.Zero);
			keybd_event(0x12, 0, 2, UIntPtr.Zero);
		}

		static bool focus(IntPtr hwnd) {
			if (hwnd == IntPtr.Zero) return false;
			var fg = GetForegroundWindow();
			if (fg == hwnd) return true;
			uint fgThread = 0;
			if (fg != IntPtr.Zero) fgThread = GetWindowThreadProcessId(fg, out _);
			var target = GetWindowThreadProcessId(hwnd, out _);
			var cur = GetCurrentThreadId();
			var a1 = false;
			var a2 = false;
			if (fgThread != 0 && fgThread != cur) a1 = AttachThreadInput(fgThread, cur, true);
			if (target != 0 && target != cur && target != fgThread) a2 = AttachThreadInput(target, cur, true);
			ShowWindow(hwnd, SW_RESTORE);
			BringWindowToTop(hwnd);
			SetForegroundWindow(hwnd);
			var ok = GetForegroundWindow() == hwnd;
			if (!ok) {
				keybd_event(0x12, 0, 0, UIntPtr.Zero);
				SetForegroundWindow(hwnd);
				keybd_event(0x12, 0, 2, UIntPtr.Zero);
				ok = GetForegroundWindow() == hwnd;
			}
			if (a2) AttachThreadInput(target, cur, false);
			if (a1) AttachThreadInput(fgThread, cur, false);
			return ok;
		}

		IntPtr chromehwnd() {
			var pid = ownpid();
			if (pid == 0) return IntPtr.Zero;
			var hwnd = pickhwnd(pid, true);
			if (hwnd == IntPtr.Zero) hwnd = pickhwnd(pid, false);
			return hwnd;
		}

		uint ownpid() {
			if (port > 0) {
				var listen = PortUse.ListenerPid(port);
				if (listen > 0) return (uint)listen;
				var via = listenerpid();
				if (via > 0) return via;
			}
			if (proc != null) {
				try { if (!proc.HasExited) return (uint)proc.Id; }
				catch { }
			}
			return 0;
		}

		uint listenerpid() {
			try {
				var p = Process.Start(new ProcessStartInfo {
					FileName = "netstat",
					Arguments = "-ano -p tcp",
					UseShellExecute = false,
					RedirectStandardOutput = true,
					CreateNoWindow = true,
				});
				if (p == null) return 0;
				var text = p.StandardOutput.ReadToEnd();
				p.WaitForExit(3000);
				var needle = "127.0.0.1:" + port;
				foreach (var raw in text.Split('\n')) {
					var line = raw.Trim();
					if (line.IndexOf(needle, StringComparison.Ordinal) < 0) continue;
					if (line.IndexOf("0.0.0.0:0", StringComparison.Ordinal) < 0) continue;
					var bits = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
					if (bits.Length == 0) continue;
					if (uint.TryParse(bits[bits.Length - 1], out var n) && n > 0) return n;
				}
			}
			catch { }
			return 0;
		}

		IntPtr pickhwnd(uint pid, bool visibleOnly) {
			IntPtr best = IntPtr.Zero;
			IntPtr browser = IntPtr.Zero;
			var bestArea = -1;
			var browserArea = -1;
			EnumWindows((h, _) => {
				GetWindowThreadProcessId(h, out var wpid);
				if (wpid != pid) return true;
				if (visibleOnly && !IsWindowVisible(h)) return true;
				var area = 0;
				if (GetWindowRect(h, out var rc))
					area = Math.Max(0, rc.right - rc.left) * Math.Max(0, rc.bottom - rc.top);
				if (area >= bestArea) {
					bestArea = area;
					best = h;
				}
				if (classname(h) == "Chrome_WidgetWin_1" && windowtext(h).Length > 0 && area >= browserArea) {
					browserArea = area;
					browser = h;
				}
				return true;
			}, IntPtr.Zero);
			return browser != IntPtr.Zero ? browser : best;
		}

		static string windowtext(IntPtr hwnd) {
			var sb = new StringBuilder(256);
			if (InternalGetWindowText(hwnd, sb, sb.Capacity) <= 0) return "";
			return sb.ToString();
		}

		static string classname(IntPtr hwnd) {
			var sb = new StringBuilder(64);
			if (GetClassName(hwnd, sb, sb.Capacity) <= 0) return "";
			return sb.ToString();
		}

		public string EvalPage(string expr, bool wait, int ms, CancellationToken ct) {
			page ??= connect("page", "on-device-translation");
			if (page == null) throw new InvalidOperationException(Loc.T("inst.chrome.nopage"));
			return page.Eval(expr, wait, ms, ct);
		}

		public void ReloadExt(CancellationToken ct) {
			ext ??= connectext();
			if (ext == null) return;
			try { ext.Eval("(function(){try{chrome.runtime.reload();}catch(e){}return 'reloading';})()", false, 3000, ct); }
			catch { }
			try { ext.Dispose(); } catch { }
			ext = null;
			var start = Environment.TickCount;
			while (unchecked(Environment.TickCount - start) < 8000) {
				ct.ThrowIfCancellationRequested();
				if (!extlisted()) return;
				Thread.Sleep(200);
			}
		}

		bool extlisted() {
			try {
				var req = WebRequest.Create("http://127.0.0.1:" + port + "/json/list");
				req.Timeout = 4000;
				using var resp = req.GetResponse();
				using var rd = new StreamReader(resp.GetResponseStream());
				using var doc = JsonDocument.Parse(rd.ReadToEnd());
				foreach (var el in doc.RootElement.EnumerateArray()) {
					var url = el.TryGetProperty("url", out var u) ? u.GetString() : "";
					if ((url ?? "").IndexOf(TTS_EXT, StringComparison.OrdinalIgnoreCase) >= 0) return true;
					if ((url ?? "").IndexOf(TTS_EXT_OLD, StringComparison.OrdinalIgnoreCase) >= 0) return true;
				}
			}
			catch { }
			return false;
		}

		public string EvalExt(string expr, bool wait, int ms, CancellationToken ct) {
			ext ??= connectext();
			if (ext == null) throw new InvalidOperationException(Loc.T("inst.chrome.noext"));
			return ext.Eval(expr, wait, ms, ct);
		}

		public void RequireGoogle(CancellationToken ct) {
			string up;
			try { up = EvalWeb(GOOGLEUP, true, 12000, ct); }
			catch (OperationCanceledException) { throw; }
			catch { up = ""; }
			if (up != "up")
				throw new InvalidOperationException(Loc.T("tr.chrome.google"));
		}

		public string EvalWeb(string expr, bool wait, int ms, CancellationToken ct) {
			if (web == null) throw new InvalidOperationException(Loc.T("inst.chrome.nopage"));
			return web.Eval(expr, wait, ms, ct);
		}

		public void OpenInternals(CancellationToken ct) {
			if (page != null) return;
			OpenPage("chrome://on-device-translation-internals/");
			WaitPage("en - ", 20000, ct);
		}

		public bool TryTranslator(CancellationToken ct) {
			var portNo = 1224;
			try {
				var o = new OcrOptions();
				AppConfig.LoadInto(o);
				portNo = HttpOcrServer.PagePort(o);
			}
			catch { }
			OpenPage("http://127.0.0.1:" + portNo + "/");
			var start = Environment.TickCount;
			while (unchecked(Environment.TickCount - start) < 12000) {
				ct.ThrowIfCancellationRequested();
				hidewin();
				web ??= connect("page", "127.0.0.1");
				if (web != null) {
					string ok;
					try { ok = web.Eval("(function(){return (window.Translator&&window.isSecureContext)?'yes':'no';})()", false, 5000, ct); }
					catch { ok = ""; }
					if (ok == "yes") return true;
					if (ok == "no") return false;
				}
				Thread.Sleep(250);
			}
			return false;
		}

		public void ArmNet(CancellationToken ct) {
			arm(web, ref webNet, ct);
			arm(page, ref pageNet, ct);
			arm(ext, ref extNet, ct);
			if (off == null) off = connectany("offscreen");
			arm(off, ref offNet, ct);
			if (!dlNet) {
				try {
					if (browser == null) {
						var ws = browserws();
						if (!string.IsNullOrEmpty(ws)) browser = Sock.Connect(ws);
					}
					if (browser != null) {
						browser.Call("Browser.setDownloadBehavior", "{\"behavior\":\"default\",\"eventsEnabled\":true}", ct);
						dlNet = true;
					}
				}
				catch { dlNet = true; }
			}
			arm(browser, ref browserNet, ct);
		}

		public void ClearBytes() {
			page?.ClearBytes();
			web?.ClearBytes();
			ext?.ClearBytes();
			off?.ClearBytes();
			browser?.ClearBytes();
		}

		public void PumpAll(int ms, CancellationToken ct) {
			var start = Environment.TickCount;
			while (unchecked(Environment.TickCount - start) < ms) {
				ct.ThrowIfCancellationRequested();
				var any = false;
				if (web != null && web.Pump(40)) any = true;
				if (ext != null && ext.Pump(40)) any = true;
				if (off != null && off.Pump(40)) any = true;
				if (browser != null && browser.Pump(40)) any = true;
				if (page != null && page.Pump(40)) any = true;
				if (!any) Thread.Sleep(40);
				hidewin();
			}
		}

		public double TakeFrac(long expect, out string line) {
			line = "";
			var best = -1.0;
			scan(web, expect, ref best, ref line);
			scan(ext, expect, ref best, ref line);
			scan(off, expect, ref best, ref line);
			scan(browser, expect, ref best, ref line);
			scan(page, expect, ref best, ref line);
			return best;
		}

		static void scan(Sock sock, long expect, ref double best, ref string line) {
			if (sock == null) return;
			sock.ReadBytes(out var loaded, out var total, out var full);
			var frac = -1.0;
			var text = "";
			if (sock.ConsoleFrac >= 0 && sock.ConsoleFrac > frac) frac = sock.ConsoleFrac;
			if (full && total > 0) {
				frac = loaded / (double)total;
				if (frac > 0.99) frac = 0.99;
				text = FeatureInstaller.FormatBytes(loaded) + " / " + FeatureInstaller.FormatBytes(total);
			}
			else if (loaded > 0 && expect > 0) {
				var guess = loaded / (double)expect;
				if (guess > 0.99) guess = 0.99;
				if (guess > frac) frac = guess;
				text = FeatureInstaller.FormatBytes(loaded) + " / " + FeatureInstaller.FormatBytes(expect);
			}
			else if (loaded > 0)
				text = FeatureInstaller.FormatBytes(loaded);
			if (frac > best) {
				best = frac;
				if (text.Length > 0) line = text;
			}
			else if (best < 0 && line.Length == 0 && text.Length > 0)
				line = text;
		}

		void arm(Sock sock, ref bool flag, CancellationToken ct) {
			if (sock == null || flag) return;
			try {
				sock.Call("Network.enable", "{}", ct);
				sock.Call("Runtime.enable", "{}", ct);
				flag = true;
			}
			catch { flag = true; }
		}

		Sock connectany(string urlPart) {
			try { return connect("", urlPart, true); }
			catch { return null; }
		}

		string browserws() {
			try {
				var req = WebRequest.Create("http://127.0.0.1:" + port + "/json/version");
				req.Timeout = 4000;
				using var resp = req.GetResponse();
				using var rd = new StreamReader(resp.GetResponseStream());
				using var doc = JsonDocument.Parse(rd.ReadToEnd());
				return doc.RootElement.TryGetProperty("webSocketDebuggerUrl", out var w) ? w.GetString() : "";
			}
			catch { return ""; }
		}

		public Dictionary<string, string> VoiceStatus(List<string> langs, CancellationToken ct) {
			var arr = new StringBuilder("[");
			for (var i = 0; i < langs.Count; i++) {
				if (i > 0) arr.Append(',');
				arr.Append(jsstr(langs[i]));
			}
			arr.Append(']');
			var expr = "(async function(){var langs=" + arr + ";" + VOICEASK + STATUS + "})()";
			var raw = EvalExt(expr, true, 300000, ct);
			var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "[]" : raw);
			if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var err))
				throw new InvalidOperationException(err.GetString());
			if (doc.RootElement.ValueKind != JsonValueKind.Array) return map;
			foreach (var el in doc.RootElement.EnumerateArray()) {
				var lang = el.TryGetProperty("lang", out var l) ? l.GetString() : "";
				var st = fieldstatus(el);
				if (st.Length == 0 && el.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.String)
					st = errEl.GetString() ?? "";
				if (!string.IsNullOrEmpty(lang)) map[lang] = st ?? "";
			}
			return map;
		}

		const string STATUS = @"
async function ready(){
if(!chrome.offscreen||!chrome.offscreen.createDocument)return;
var has=false;try{has=await chrome.offscreen.hasDocument();}catch(e){}
if(!has){try{await chrome.offscreen.createDocument({url:'offscreen.html',reasons:['AUDIO_PLAYBACK','USER_MEDIA'],justification:'ScreenKit voice pack'});}catch(e){}}
}
await ready();
var out=[];
for(var i=0;i<langs.length;i++){
var lang=langs[i];
var res=await ask({type:'getLanguageStatus',lang:lang});
out.push({lang:lang,status:(res&&(res.status||res.error))||''});
}
return JSON.stringify(out);
";

		Sock connectext() {
			var sw = connect("service_worker", TTS_EXT) ?? connect("service_worker", TTS_EXT_OLD);
			if (sw != null) return sw;
			return connectbg();
		}

		Sock connectbg() {
			var req = WebRequest.Create("http://127.0.0.1:" + port + "/json/list");
			req.Timeout = 4000;
			using var resp = req.GetResponse();
			using var rd = new StreamReader(resp.GetResponseStream());
			using var doc = JsonDocument.Parse(rd.ReadToEnd());
			foreach (var el in doc.RootElement.EnumerateArray()) {
				var ty = el.TryGetProperty("type", out var t) ? t.GetString() : "";
				var url = el.TryGetProperty("url", out var u) ? u.GetString() : "";
				var ws = el.TryGetProperty("webSocketDebuggerUrl", out var w) ? w.GetString() : "";
				if (ty != "background_page" || string.IsNullOrEmpty(ws) || string.IsNullOrEmpty(url)) continue;
				if (url.IndexOf("offscreen", StringComparison.OrdinalIgnoreCase) >= 0) continue;
				if (url.IndexOf("hangout", StringComparison.OrdinalIgnoreCase) >= 0) continue;
				var hit = url.IndexOf(TTS_EXT, StringComparison.OrdinalIgnoreCase) >= 0
					|| url.IndexOf("WasmTts", StringComparison.OrdinalIgnoreCase) >= 0
					|| url.IndexOf("background_compiled", StringComparison.OrdinalIgnoreCase) >= 0;
				if (!hit) continue;
				return Sock.Connect(ws);
			}
			return null;
		}

		Sock connect(string type, string urlPart) => connect(type, urlPart, false);

		Sock connect(string type, string urlPart, bool anyType) {
			var req = WebRequest.Create("http://127.0.0.1:" + port + "/json/list");
			req.Timeout = 4000;
			using var resp = req.GetResponse();
			using var rd = new StreamReader(resp.GetResponseStream());
			using var doc = JsonDocument.Parse(rd.ReadToEnd());
			foreach (var el in doc.RootElement.EnumerateArray()) {
				var ty = el.TryGetProperty("type", out var t) ? t.GetString() : "";
				var url = el.TryGetProperty("url", out var u) ? u.GetString() : "";
				var ws = el.TryGetProperty("webSocketDebuggerUrl", out var w) ? w.GetString() : "";
				if (string.IsNullOrEmpty(ws)) continue;
				if (!anyType && ty != type) continue;
				if ((url ?? "").IndexOf(urlPart, StringComparison.OrdinalIgnoreCase) < 0) continue;
				return Sock.Connect(ws);
			}
			return null;
		}

		public void Dispose() {
			try { page?.Dispose(); } catch { }
			try { web?.Dispose(); } catch { }
			try { ext?.Dispose(); } catch { }
			try { off?.Dispose(); } catch { }
			try { browser?.Dispose(); } catch { }
			page = null;
			web = null;
			ext = null;
			off = null;
			browser = null;
			var p = proc;
			proc = null;
			if (!own || p == null) return;
			var pid = 0;
			try { if (!p.HasExited) pid = p.Id; } catch { }
			var listen = port > 0 ? PortUse.ListenerPid(port) : 0;
			if (pid > 0) ChromeTts.KillPid(pid);
			if (listen > 0 && listen != pid) ChromeTts.KillPid(listen);
			try { if (!p.HasExited) p.Kill(); } catch { }
			try { if (!p.HasExited) p.WaitForExit(4000); } catch { }
			try { p.Dispose(); } catch { }
		}

		static void appendproxy(StringBuilder args, string mode, string addr) {
			HttpProxy.AppendChrome(args, mode, addr);
		}

		/// <summary>管理员运行时浏览器是降权重起的那只，启动器已经没了，按调试口的进程找窗口。</summary>
		void hidewin() {
			var pid = port > 0 ? (uint)PortUse.ListenerPid(port) : 0;
			if (pid == 0 && proc != null) {
				try { if (!proc.HasExited) pid = (uint)proc.Id; }
				catch { }
			}
			hidepid(pid);
		}

		static void hidepid(uint pid) {
			if (pid == 0) return;
			EnumWindows((h, _) => {
				GetWindowThreadProcessId(h, out var wpid);
				if (wpid == pid) ShowWindow(h, SW_HIDE);
				return true;
			}, IntPtr.Zero);
		}

		delegate bool EnumProc(IntPtr h, IntPtr l);
		[DllImport("user32.dll")]
		static extern bool EnumWindows(EnumProc cb, IntPtr l);
		[DllImport("user32.dll")]
		static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
		[DllImport("user32.dll")]
		static extern bool ShowWindow(IntPtr h, int cmd);
		[DllImport("user32.dll")]
		static extern bool IsWindowVisible(IntPtr h);
		[DllImport("user32.dll")]
		static extern bool GetWindowRect(IntPtr h, out RECT rc);
		[StructLayout(LayoutKind.Sequential)]
		struct RECT {
			public int left;
			public int top;
			public int right;
			public int bottom;
		}
		[DllImport("user32.dll")]
		static extern bool SetForegroundWindow(IntPtr h);
		[DllImport("user32.dll")]
		static extern IntPtr GetForegroundWindow();
		[DllImport("user32.dll")]
		static extern bool IsWindow(IntPtr h);
		[DllImport("user32.dll")]
		static extern bool AttachThreadInput(uint from, uint to, bool attach);
		[DllImport("user32.dll")]
		static extern bool BringWindowToTop(IntPtr h);
		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
		[DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "InternalGetWindowText")]
		static extern int InternalGetWindowText(IntPtr hwnd, StringBuilder name, int count);
		[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
		static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
		[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
		static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
		[DllImport("kernel32.dll")]
		static extern uint GetCurrentThreadId();
		[DllImport("user32.dll")]
		static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
		[DllImport("user32.dll")]
		static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
	}

	sealed class Sock : IDisposable {
		Socket io;
		MemoryStream pending = new();
		int seq;
		readonly HashSet<string> watch = new();
		readonly HashSet<string> dlSkip = new();
		readonly Dictionary<string, long> reqgot = new();
		readonly Dictionary<string, long> reqtotal = new();
		double consolefrac = -1;

		public static Sock Connect(string wsUrl) {
			var uri = new Uri(wsUrl);
			var io = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			io.Connect(uri.Host, uri.Port);
			var key = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
			var req = "GET " + uri.PathAndQuery + " HTTP/1.1\r\nHost: " + uri.Host + ":" + uri.Port
				+ "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: " + key
				+ "\r\nSec-WebSocket-Version: 13\r\n\r\n";
			var bytes = Encoding.ASCII.GetBytes(req);
			io.Send(bytes);
			var buf = new byte[4096];
			var got = new MemoryStream();
			while (true) {
				var n = io.Receive(buf);
				if (n <= 0) throw new IOException("cdp");
				got.Write(buf, 0, n);
				var all = got.ToArray();
				var mark = find(all, Encoding.ASCII.GetBytes("\r\n\r\n"));
				if (mark < 0) continue;
				var sock = new Sock { io = io };
				var extra = all.Length - (mark + 4);
				if (extra > 0) sock.pending.Write(all, mark + 4, extra);
				return sock;
			}
		}

		public string Eval(string expr, bool wait, int ms, CancellationToken ct) {
			var id = ++seq;
			var payload = JsonSerializer.Serialize(new Dictionary<string, object> {
				["id"] = id,
				["method"] = "Runtime.evaluate",
				["params"] = new Dictionary<string, object> {
					["expression"] = expr,
					["returnByValue"] = true,
					["awaitPromise"] = wait,
					["userGesture"] = true,
				},
			});
			send(payload);
			var start = Environment.TickCount;
			while (unchecked(Environment.TickCount - start) < ms) {
				ct.ThrowIfCancellationRequested();
				var left = ms - unchecked(Environment.TickCount - start);
				if (left > 400) left = 400;
				if (left < 50) left = 50;
				var msg = recv(left);
				if (msg == null) continue;
				using var doc = JsonDocument.Parse(msg);
				if (!matchid(doc.RootElement, id)) {
					note(msg);
					continue;
				}
				if (doc.RootElement.TryGetProperty("error", out var err))
					throw new InvalidOperationException(err.ToString());
				if (!doc.RootElement.TryGetProperty("result", out var result)) return "";
				if (result.TryGetProperty("exceptionDetails", out var ex))
					throw new InvalidOperationException(ex.ToString());
				if (result.TryGetProperty("result", out var inner) && inner.TryGetProperty("value", out var val)) {
					if (val.ValueKind == JsonValueKind.String) return val.GetString() ?? "";
					return val.ToString();
				}
				return "";
			}
			throw new InvalidOperationException(Loc.T("inst.chrome.timeout"));
		}

		public double ConsoleFrac => consolefrac;

		public void Call(string method, string paramsJson, CancellationToken ct) {
			var id = ++seq;
			var node = new JsonObject {
				["id"] = id,
				["method"] = method ?? "",
				["params"] = JsonNode.Parse(string.IsNullOrWhiteSpace(paramsJson) ? "{}" : paramsJson),
			};
			send(node.ToJsonString());
			var start = Environment.TickCount;
			while (unchecked(Environment.TickCount - start) < 8000) {
				ct.ThrowIfCancellationRequested();
				var left = 8000 - unchecked(Environment.TickCount - start);
				if (left > 400) left = 400;
				if (left < 50) left = 50;
				var msg = recv(left);
				if (msg == null) continue;
				using var doc = JsonDocument.Parse(msg);
				if (!matchid(doc.RootElement, id)) {
					note(msg);
					continue;
				}
				if (doc.RootElement.TryGetProperty("error", out var err))
					throw new InvalidOperationException(err.ToString());
				return;
			}
			throw new InvalidOperationException(Loc.T("inst.chrome.timeout"));
		}

		public void ClearBytes() {
			watch.Clear();
			dlSkip.Clear();
			reqgot.Clear();
			reqtotal.Clear();
			consolefrac = -1;
		}

		public bool Pump(int ms) {
			if (ms < 1) ms = 1;
			var start = Environment.TickCount;
			var any = false;
			while (unchecked(Environment.TickCount - start) < ms) {
				var left = ms - unchecked(Environment.TickCount - start);
				if (left > 40) left = 40;
				if (left < 1) left = 1;
				var msg = recv(left);
				if (msg == null) continue;
				any = true;
				note(msg);
			}
			return any;
		}

		public void ReadBytes(out long loaded, out long total, out bool full) {
			long netGot = 0, netAll = 0, dlGot = 0, dlAll = 0;
			var netN = 0;
			var dlN = 0;
			var netFull = true;
			var dlFull = true;
			foreach (var kv in reqgot) {
				var dl = kv.Key.StartsWith("dl:");
				if (dl) { dlN++; dlGot += kv.Value; }
				else { netN++; netGot += kv.Value; }
				if (!reqtotal.TryGetValue(kv.Key, out var t) || t <= 0) {
					if (dl) dlFull = false;
					else netFull = false;
				}
				else if (dl) dlAll += t;
				else netAll += t;
			}
			var netOk = netN > 0 && netFull && netAll > 0;
			var dlOkNow = dlN > 0 && dlFull && dlAll > 0;
			if (netOk && dlOkNow) {
				if (dlGot > netGot) { loaded = dlGot; total = dlAll; full = true; }
				else { loaded = netGot; total = netAll; full = true; }
				return;
			}
			if (dlOkNow) { loaded = dlGot; total = dlAll; full = true; return; }
			if (netOk) { loaded = netGot; total = netAll; full = true; return; }
			if (dlGot >= netGot && dlGot > 0) { loaded = dlGot; total = 0; full = false; return; }
			loaded = netGot;
			total = 0;
			full = false;
		}

		static bool matchid(JsonElement root, int id) {
			if (!root.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number) return false;
			return idEl.TryGetInt32(out var n) && n == id;
		}

		void note(string msg) {
			try {
				using var doc = JsonDocument.Parse(msg);
				var root = doc.RootElement;
				if (!root.TryGetProperty("method", out var methodEl) || methodEl.ValueKind != JsonValueKind.String) return;
				var method = methodEl.GetString() ?? "";
				if (!root.TryGetProperty("params", out var p) || p.ValueKind != JsonValueKind.Object) return;
				if (method == "Network.requestWillBeSent") {
					var id = reqid(p);
					var url = p.TryGetProperty("request", out var req) ? text(req, "url") : "";
					if (id.Length > 0 && packurl(url)) watch.Add(id);
					return;
				}
				if (method == "Network.responseReceived" || method == "Network.responseReceivedExtraInfo") {
					var id = reqid(p);
					var url = "";
					if (p.TryGetProperty("response", out var resp)) url = text(resp, "url");
					if (id.Length > 0 && url.Length > 0 && !packurl(url)) {
						watch.Remove(id);
						reqgot.Remove(id);
						reqtotal.Remove(id);
						return;
					}
					if (id.Length > 0 && packurl(url)) watch.Add(id);
					if (watch.Contains(id)) settotal(id, contentlen(p));
					return;
				}
				if (method == "Network.dataReceived") {
					var id = reqid(p);
					if (!watch.Contains(id)) return;
					var n = jsonlong(p, "encodedDataLength");
					if (n <= 0) n = jsonlong(p, "dataLength");
					addgot(id, n);
					return;
				}
				if (method == "Network.loadingFinished") {
					var id = reqid(p);
					if (!watch.Contains(id)) return;
					setmax(id, jsonlong(p, "encodedDataLength"));
					return;
				}
				if (method == "Browser.downloadWillBegin") {
					var id = text(p, "guid");
					if (id.Length == 0) return;
					var url = text(p, "url");
					if (url.Length > 0 && !packurl(url)) {
						dlSkip.Add(id);
						reqgot.Remove("dl:" + id);
						reqtotal.Remove("dl:" + id);
						return;
					}
					dlSkip.Remove(id);
					return;
				}
				if (method == "Browser.downloadProgress" || method == "Page.downloadProgress") {
					var id = text(p, "guid");
					if (id.Length == 0) id = text(p, "id");
					if (id.Length == 0 || dlSkip.Contains(id)) return;
					var gotBytes = jsonlong(p, "receivedBytes");
					var all = jsonlong(p, "totalBytes");
					if (gotBytes > 0) reqgot["dl:" + id] = gotBytes;
					if (all > 0) reqtotal["dl:" + id] = all;
					return;
				}
				if (method != "Runtime.consoleAPICalled" && method != "Runtime.exceptionThrown") return;
				var line = method == "Runtime.consoleAPICalled" ? consoletext(p) : p.GetRawText();
				var low = line.ToLowerInvariant();
				if (!low.Contains("download") && !low.Contains("install") && !line.Contains("下载") && !line.Contains("安装") && !low.Contains("progress"))
					return;
				var frac = percentin(line);
				if (frac > consolefrac) consolefrac = frac;
			}
			catch { }
		}

		void addgot(string id, long add) {
			if (id.Length == 0 || add <= 0) return;
			reqgot.TryGetValue(id, out var cur);
			reqgot[id] = cur + add;
		}

		void setmax(string id, long n) {
			if (id.Length == 0 || n <= 0) return;
			reqgot.TryGetValue(id, out var cur);
			if (n > cur) reqgot[id] = n;
		}

		void settotal(string id, long n) {
			if (id.Length == 0 || n <= 0) return;
			reqtotal[id] = n;
		}

		static string text(JsonElement el, string name) {
			if (!el.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String) return "";
			return v.GetString() ?? "";
		}

		static string reqid(JsonElement p) {
			if (!p.TryGetProperty("requestId", out var v)) return "";
			if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? "";
			if (v.ValueKind == JsonValueKind.Number) return v.ToString();
			return "";
		}

		static bool packurl(string url) {
			if (string.IsNullOrEmpty(url)) return false;
			var s = url.ToLowerInvariant();
			return s.Contains("gvt1") || s.Contains(".crx") || s.Contains("wasm") || s.Contains("translate")
				|| s.Contains("edgedl") || s.Contains("dl.google.") || s.Contains("update2/crx") || s.Contains("clients2.google.");
		}

		static long jsonlong(JsonElement el, string name) {
			if (!el.TryGetProperty(name, out var v)) return 0;
			if (v.ValueKind == JsonValueKind.Number) {
				if (v.TryGetInt64(out var n)) return n;
				if (v.TryGetDouble(out var d)) return (long)d;
			}
			if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s)) return s;
			return 0;
		}

		static long contentlen(JsonElement p) {
			if (p.TryGetProperty("response", out var resp) && resp.ValueKind == JsonValueKind.Object && resp.TryGetProperty("headers", out var headers)) {
				var n = headerlen(headers);
				if (n > 0) return n;
			}
			if (p.TryGetProperty("headers", out var extra)) return headerlen(extra);
			return 0;
		}

		static long headerlen(JsonElement headers) {
			if (headers.ValueKind == JsonValueKind.Object) {
				foreach (var h in headers.EnumerateObject()) {
					if (!h.Name.Equals("content-length", StringComparison.OrdinalIgnoreCase)) continue;
					if (h.Value.ValueKind == JsonValueKind.String && long.TryParse(h.Value.GetString(), out var n)) return n;
					if (h.Value.ValueKind == JsonValueKind.Number && h.Value.TryGetInt64(out var n2)) return n2;
				}
			}
			if (headers.ValueKind == JsonValueKind.Array) {
				foreach (var el in headers.EnumerateArray()) {
					var name = text(el, "name");
					if (!name.Equals("content-length", StringComparison.OrdinalIgnoreCase)) continue;
					if (!el.TryGetProperty("value", out var val)) continue;
					if (val.ValueKind == JsonValueKind.String && long.TryParse(val.GetString(), out var n)) return n;
					if (val.ValueKind == JsonValueKind.Number && val.TryGetInt64(out var n2)) return n2;
				}
			}
			return 0;
		}

		static string consoletext(JsonElement p) {
			if (!p.TryGetProperty("args", out var args) || args.ValueKind != JsonValueKind.Array) return "";
			var sb = new StringBuilder();
			foreach (var a in args.EnumerateArray()) {
				if (sb.Length > 0) sb.Append(' ');
				if (a.TryGetProperty("value", out var v)) {
					if (v.ValueKind == JsonValueKind.String) sb.Append(v.GetString());
					else sb.Append(v.ToString());
				}
				else if (a.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String)
					sb.Append(d.GetString());
			}
			return sb.ToString();
		}

		void send(string text) {
			var payload = Encoding.UTF8.GetBytes(text);
			var hdr = new MemoryStream();
			hdr.WriteByte(0x81);
			if (payload.Length < 126) hdr.WriteByte((byte)(0x80 | payload.Length));
			else {
				hdr.WriteByte(0x80 | 126);
				hdr.WriteByte((byte)(payload.Length >> 8));
				hdr.WriteByte((byte)payload.Length);
			}
			var mask = new byte[4];
			new Random().NextBytes(mask);
			hdr.Write(mask, 0, 4);
			var masked = new byte[payload.Length];
			for (var i = 0; i < payload.Length; i++) masked[i] = (byte)(payload[i] ^ mask[i % 4]);
			var head = hdr.ToArray();
			io.Send(head);
			io.Send(masked);
		}

		string recv(int ms) {
			io.ReceiveTimeout = ms;
			while (true) {
				int b0, b1;
				try {
					b0 = readb();
					b1 = readb();
				}
				catch (SocketException) { return null; }
				var opcode = b0 & 15;
				var ln = b1 & 127;
				if (ln == 126) ln = (readb() << 8) | readb();
				else if (ln == 127) {
					long n = 0;
					for (var i = 0; i < 8; i++) n = (n << 8) | (byte)readb();
					ln = (int)n;
				}
				byte[] mask = null;
				if ((b1 & 128) != 0) {
					mask = new byte[4];
					for (var i = 0; i < 4; i++) mask[i] = (byte)readb();
				}
				var data = new byte[ln];
				for (var i = 0; i < ln; i++) data[i] = (byte)readb();
				if (mask != null)
					for (var i = 0; i < data.Length; i++) data[i] ^= mask[i % 4];
				if (opcode == 8) throw new IOException("cdp closed");
				if (opcode == 9) {
					pong(data);
					continue;
				}
				if (opcode == 1) return Encoding.UTF8.GetString(data);
			}
		}

		void pong(byte[] data) {
			var hdr = new byte[] { 0x8A, (byte)(0x80 | data.Length) };
			if (data.Length >= 126) return;
			var mask = new byte[] { 1, 2, 3, 4 };
			var masked = new byte[data.Length];
			for (var i = 0; i < data.Length; i++) masked[i] = (byte)(data[i] ^ mask[i % 4]);
			io.Send(hdr);
			io.Send(mask);
			io.Send(masked);
		}

		int readb() {
			if (pending.Length > 0) {
				var buf = pending.GetBuffer();
				var n = (int)pending.Length;
				var b = buf[0];
				var next = new MemoryStream();
				if (n > 1) next.Write(buf, 1, n - 1);
				pending = next;
				return b;
			}
			var one = new byte[1];
			var got = io.Receive(one);
			if (got <= 0) throw new IOException("cdp");
			return one[0];
		}

		static int find(byte[] buf, byte[] pat) {
			for (var i = 0; i <= buf.Length - pat.Length; i++) {
				var ok = true;
				for (var j = 0; j < pat.Length; j++)
					if (buf[i + j] != pat[j]) { ok = false; break; }
				if (ok) return i;
			}
			return -1;
		}

		public void Dispose() {
			try { io?.Close(); } catch { }
			io = null;
		}
	}
}
