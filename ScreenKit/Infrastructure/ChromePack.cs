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

/// <summary>查询、安装、卸载 Chrome 翻译包（TranslateKit）和离线语音包（Wasm TTS）。</summary>
static class ChromePack {
	const string TTS_EXT = "gjjabgpgjpampikjhjpfhneeoapjbjaf";
	const string TTS_EXT_OLD = "nkeimhogjdpnpccoofpliimaahmaaome";
	const int SW_HIDE = 0;

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
		if (PackState("Installed") != FeatureInstallState.Installed) return 2;
		if (PackState("not installed") != FeatureInstallState.Missing) return 2;
		if (PackState("installing") != FeatureInstallState.Partial) return 2;
		return 0;
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
		var b = parts[1].ToUpperInvariant();
		return parts[0].ToLowerInvariant() + "-" + b;
	}

	public static List<ChromePackItem> Query(string proxyMode, string proxyAddr, Action<string> log, CancellationToken ct) {
		var list = new List<ChromePackItem>();
		list.AddRange(tritems());
		list.AddRange(ttsitems(proxyMode, proxyAddr, log, ct));
		return list;
	}

	public static void Apply(IReadOnlyList<ChromePackItem> items, bool install, string proxyMode, string proxyAddr, Action<string> log, Action<double> progress, CancellationToken ct) {
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
		if (tr.Count > 0) {
			ChromeTr.Stop();
			using var sess = Sess.Open(ChromeTr.ProfileDir(), proxyMode, proxyAddr, log, ct);
			sess.OpenPage("chrome://on-device-translation-internals/");
			sess.WaitPage("en - ", 20000, ct);
			foreach (var it in tr) {
				ct.ThrowIfCancellationRequested();
				log?.Invoke((install ? "安装" : "卸载") + " " + it.Title);
				onepair(sess, it.Pair, install, log, ct);
				done++;
				progress?.Invoke(done / (double)total);
			}
		}
		if (tts.Count == 0) return;
		ChromeTts.Stop();
		preparevoice(ChromeTts.ProfileDir());
		using var voice = Sess.Open(ChromeTts.ProfileDir(), proxyMode, proxyAddr, log, ct);
		voice.WaitExt(15000, ct);
		foreach (var it in tts) {
			ct.ThrowIfCancellationRequested();
			log?.Invoke((install ? "安装" : "卸载") + " " + it.Title);
			onevoice(voice, it.Lang, install, ct);
			done++;
			progress?.Invoke(done / (double)total);
		}
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
				Title = name.Replace("_", " - "),
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
		return langname(bits[0]) + " → " + langname(bits[1]);
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
		var packs = readvoices();
		var list = new List<ChromePackItem>();
		foreach (var g in packs)
			list.Add(g);
		if (list.Count == 0) {
			log?.Invoke(Loc.T("inst.chrome.novoice"));
			return list;
		}
		Dictionary<string, string> st = null;
		try {
			ChromeTts.Stop();
			preparevoice(ChromeTts.ProfileDir());
			using var sess = Sess.Open(ChromeTts.ProfileDir(), proxyMode, proxyAddr, log, ct);
			sess.WaitExt(15000, ct);
			var langs = new List<string>();
			foreach (var it in list) langs.Add(it.Lang);
			st = sess.VoiceStatus(langs, ct);
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex) {
			log?.Invoke(ex.Message);
		}
		if (st == null) return list;
		foreach (var it in list) {
			st.TryGetValue(it.Lang, out var s);
			it.State = PackState(s);
		}
		return list;
	}

	static List<ChromePackItem> readvoices() {
		var path = findvoices();
		var map = new Dictionary<string, ChromePackItem>(StringComparer.OrdinalIgnoreCase);
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
					var baseCode = lang.Split('-')[0];
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
				if (v.TryGetProperty("speakers", out var sp) && sp.ValueKind == JsonValueKind.Array && it.Detail.Length < 80) {
					foreach (var s in sp.EnumerateArray()) {
						var name = s.TryGetProperty("name", out var ne) ? ne.GetString() : "";
						if (string.IsNullOrWhiteSpace(name)) continue;
						if (it.Detail.Length > 0) it.Detail += " · ";
						it.Detail += name;
						if (it.Detail.Length >= 80) break;
					}
				}
			}
		}
		var list = new List<ChromePackItem>(map.Values);
		list.Sort((a, b) => string.Compare(a.Lang, b.Lang, StringComparison.OrdinalIgnoreCase));
		return list;
	}

	static void preparevoice(string profile) {
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
		var roots = new List<string> {
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "User Data", "WasmTtsEngine"),
			Path.Combine(ChromeTts.ProfileDir(), "WasmTtsEngine"),
		};
		string best = null;
		var bestTime = DateTime.MinValue;
		foreach (var root in roots) {
			if (!Directory.Exists(root)) continue;
			foreach (var dir in Directory.GetDirectories(root)) {
				var file = Path.Combine(dir, "voices.json");
				if (!File.Exists(file)) continue;
				var t = File.GetLastWriteTimeUtc(file);
				if (t >= bestTime) {
					bestTime = t;
					best = file;
				}
			}
		}
		return best;
	}

	static void onepair(Sess sess, string pair, bool install, Action<string> log, CancellationToken ct) {
		var want = install ? "install" : "uninstall";
		var r = sess.EvalPage(clickjs(pair, want), false, 15000, ct);
		if (r == "already") return;
		if (r != "clicked") throw new InvalidOperationException(pair + " " + r);
		var start = Environment.TickCount;
		while (unchecked(Environment.TickCount - start) < 180000) {
			ct.ThrowIfCancellationRequested();
			var text = sess.EvalPage(rowjs(pair), false, 8000, ct);
			var st = PackState(text);
			if (install && st == FeatureInstallState.Installed) return;
			if (!install && st == FeatureInstallState.Missing) return;
			Thread.Sleep(400);
		}
		throw new InvalidOperationException(pair + " " + Loc.T("inst.chrome.timeout"));
	}

	static void onevoice(Sess sess, string lang, bool install, CancellationToken ct) {
		var type = install ? "installLanguage" : "uninstallLanguage";
		var raw = sess.EvalExt(voicejs(type, lang), true, 180000, ct);
		var st = statusof(raw);
		if (install && st == FeatureInstallState.Installed) return;
		if (!install && st == FeatureInstallState.Missing) return;
		if (st == FeatureInstallState.Installed && !install)
			throw new InvalidOperationException(lang + " " + raw);
		if (st == FeatureInstallState.Missing && install)
			throw new InvalidOperationException(lang + " " + raw);
	}

	static FeatureInstallState statusof(string raw) {
		if (string.IsNullOrWhiteSpace(raw)) return FeatureInstallState.Missing;
		try {
			using var doc = JsonDocument.Parse(raw);
			if (doc.RootElement.TryGetProperty("status", out var s))
				return PackState(s.GetString());
			if (doc.RootElement.TryGetProperty("error", out var e))
				throw new InvalidOperationException(e.GetString());
		}
		catch (JsonException) { }
		return PackState(raw);
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

	static string voicejs(string type, string lang) {
		return "(async function(){var type=" + jsstr(type) + ";var lang=" + jsstr(lang) + ";" + VOICE + "})()";
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

	const string VOICE = @"
async function ready(){
if(!chrome.offscreen||!chrome.offscreen.createDocument)return;
var has=false;try{has=await chrome.offscreen.hasDocument();}catch(e){}
if(!has){try{await chrome.offscreen.createDocument({url:'offscreen.html',reasons:['AUDIO_PLAYBACK','USER_MEDIA'],justification:'ScreenKit voice pack'});}catch(e){}}
}
await ready();
var res=await new Promise(function(resolve){chrome.runtime.sendMessage({type:type,lang:lang},function(r){var err=chrome.runtime.lastError;resolve(err?{error:err.message}:(r||{}));});});
return JSON.stringify(res||{});
";

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

	sealed class Sess : IDisposable {
		Process proc;
		int port;
		Sock page;
		Sock ext;

		Sess() { }

		public static Sess Open(string profile, string proxyMode, string proxyAddr, Action<string> log, CancellationToken ct) {
			var exe = ChromeTts.FindExe(null);
			if (exe == null) throw new InvalidOperationException(Loc.T("inst.chrome.need"));
			log?.Invoke(Loc.T("inst.chrome.open"));
			var sess = new Sess();
			var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			sess.port = ((IPEndPoint)listener.LocalEndpoint).Port;
			listener.Stop();
			var args = new StringBuilder();
			args.Append("--user-data-dir=\"").Append(profile).Append("\" ");
			args.Append("--remote-debugging-port=").Append(sess.port).Append(' ');
			args.Append("--no-first-run --no-default-browser-check --disable-sync ");
			args.Append("--disable-session-crashed-bubble --hide-crash-restore-bubble ");
			args.Append("--disable-backgrounding-occluded-windows --disable-renderer-backgrounding ");
			args.Append("--window-position=-32000,-32000 --window-size=480,320 ");
			appendproxy(args, proxyMode, proxyAddr);
			args.Append("about:blank");
			sess.proc = Process.Start(new ProcessStartInfo {
				FileName = exe,
				Arguments = args.ToString(),
				UseShellExecute = false,
				CreateNoWindow = true,
			});
			var start = Environment.TickCount;
			while (unchecked(Environment.TickCount - start) < 15000) {
				ct.ThrowIfCancellationRequested();
				hide(sess.proc);
				if (sess.proc.HasExited) {
					sess.Dispose();
					throw new InvalidOperationException(Loc.T("inst.chrome.exit"));
				}
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
				hide(proc);
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
			var start = Environment.TickCount;
			while (unchecked(Environment.TickCount - start) < ms) {
				ct.ThrowIfCancellationRequested();
				hide(proc);
				ext ??= connectext();
				if (ext != null) return;
				Thread.Sleep(200);
			}
			throw new InvalidOperationException(Loc.T("inst.chrome.noext"));
		}

		public string EvalPage(string expr, bool wait, int ms, CancellationToken ct) {
			page ??= connect("page", "on-device-translation");
			if (page == null) throw new InvalidOperationException(Loc.T("inst.chrome.nopage"));
			return page.Eval(expr, wait, ms, ct);
		}

		public string EvalExt(string expr, bool wait, int ms, CancellationToken ct) {
			ext ??= connectext();
			if (ext == null) throw new InvalidOperationException(Loc.T("inst.chrome.noext"));
			return ext.Eval(expr, wait, ms, ct);
		}

		public Dictionary<string, string> VoiceStatus(List<string> langs, CancellationToken ct) {
			var arr = new StringBuilder("[");
			for (var i = 0; i < langs.Count; i++) {
				if (i > 0) arr.Append(',');
				arr.Append(jsstr(langs[i]));
			}
			arr.Append(']');
			var expr = "(async function(){var langs=" + arr + ";" + STATUS + "})()";
			var raw = EvalExt(expr, true, 120000, ct);
			var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "[]" : raw);
			if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var err))
				throw new InvalidOperationException(err.GetString());
			if (doc.RootElement.ValueKind != JsonValueKind.Array) return map;
			foreach (var el in doc.RootElement.EnumerateArray()) {
				var lang = el.TryGetProperty("lang", out var l) ? l.GetString() : "";
				var st = el.TryGetProperty("status", out var s) ? s.GetString() : "";
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
var res=await new Promise(function(resolve){chrome.runtime.sendMessage({type:'getLanguageStatus',lang:lang},function(r){var err=chrome.runtime.lastError;resolve(err?{error:err.message}:(r||{}));});});
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

		Sock connect(string type, string urlPart) {
			var req = WebRequest.Create("http://127.0.0.1:" + port + "/json/list");
			req.Timeout = 4000;
			using var resp = req.GetResponse();
			using var rd = new StreamReader(resp.GetResponseStream());
			using var doc = JsonDocument.Parse(rd.ReadToEnd());
			foreach (var el in doc.RootElement.EnumerateArray()) {
				var ty = el.TryGetProperty("type", out var t) ? t.GetString() : "";
				var url = el.TryGetProperty("url", out var u) ? u.GetString() : "";
				var ws = el.TryGetProperty("webSocketDebuggerUrl", out var w) ? w.GetString() : "";
				if (ty != type || string.IsNullOrEmpty(ws)) continue;
				if ((url ?? "").IndexOf(urlPart, StringComparison.OrdinalIgnoreCase) < 0) continue;
				return Sock.Connect(ws);
			}
			return null;
		}

		public void Dispose() {
			try { page?.Dispose(); } catch { }
			try { ext?.Dispose(); } catch { }
			page = null;
			ext = null;
			var p = proc;
			proc = null;
			if (p == null) return;
			try {
				if (!p.HasExited) {
					var kill = Process.Start(new ProcessStartInfo {
						FileName = "taskkill",
						Arguments = "/T /F /PID " + p.Id,
						UseShellExecute = false,
						CreateNoWindow = true,
					});
					kill?.WaitForExit(3000);
				}
			}
			catch { }
			try { if (!p.HasExited) p.Kill(); } catch { }
		}

		static void appendproxy(StringBuilder args, string mode, string addr) {
			mode = (mode ?? "").Trim().ToLowerInvariant();
			if (mode == "off") args.Append("--no-proxy-server ");
			else if (mode == "manual" && !string.IsNullOrWhiteSpace(addr))
				args.Append("--proxy-server=\"").Append(addr.Trim()).Append("\" ");
		}

		static void hide(Process p) {
			if (p == null) return;
			var pid = (uint)p.Id;
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
	}

	sealed class Sock : IDisposable {
		Socket io;
		MemoryStream pending = new();
		int seq;

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
				},
			});
			send(payload);
			var start = Environment.TickCount;
			while (unchecked(Environment.TickCount - start) < ms) {
				ct.ThrowIfCancellationRequested();
				var left = ms - unchecked(Environment.TickCount - start);
				if (left < 200) left = 200;
				var msg = recv(left);
				if (msg == null) continue;
				using var doc = JsonDocument.Parse(msg);
				if (!doc.RootElement.TryGetProperty("id", out var idEl) || idEl.GetInt32() != id) continue;
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
