using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ScreenKit;

/// <summary>界面语言一项。Code 是程序目录 lang/ 下的 toml 文件名。</summary>
public sealed class UiLang {
	public readonly string Code;
	public readonly string Name;

	internal UiLang(string code, string name) {
		Code = code;
		Name = name;
	}
}

/// <summary>
/// 界面文案。启动时读取程序目录 lang/*.toml。缺键回退中文，再回退键名。
/// 列表顺序：中文、英文、日文、韩文，其余按代码排序。
/// </summary>
static class Loc {
	public const string Zh = "zh";
	public const string En = "en";

	static readonly object gate = new();
	static string testRoot;
	static bool loaded;
	static string lang = Zh;
	static List<UiLang> langs = new();
	static Dictionary<string, Dictionary<string, string>> maps =
		new(StringComparer.OrdinalIgnoreCase);

	public static IReadOnlyList<UiLang> Languages {
		get { ensure(); return langs; }
	}

	public static string Lang {
		get { ensure(); return lang; }
		set { ensure(); lang = norm(value); }
	}

	public static bool IsEn => isprefix(Lang, En);
	public static bool IsZh => isprefix(Lang, Zh);

	public static string NameOf(string code) {
		ensure();
		var c = (code ?? "").Trim();
		foreach (var one in langs) {
			if (string.Equals(one.Code, c, StringComparison.OrdinalIgnoreCase))
				return one.Name;
		}
		return c;
	}

	public static string Normalize(string raw) {
		ensure();
		return norm(raw);
	}

	internal static void SetRootForTest(string dir) {
		lock (gate) {
			testRoot = string.IsNullOrWhiteSpace(dir) ? null : Path.GetFullPath(dir);
			loaded = false;
		}
	}

	internal static void Reload() {
		lock (gate) loaded = false;
		ensure();
	}

	public static string T(string key) {
		if (string.IsNullOrEmpty(key)) return "";
		ensure();
		if (tryget(lang, key, out var hit)) return hit;
		if (!string.Equals(lang, Zh, StringComparison.OrdinalIgnoreCase) && tryget(Zh, key, out hit))
			return hit;
		return key;
	}

	public static string T(string key, params object[] args) {
		try { return string.Format(T(key), args); }
		catch { return T(key); }
	}

	public static void SetFromConfig(string uiLang) => Lang = uiLang;

	public static string Compute(TtsComputeMode mode) => mode switch {
		TtsComputeMode.Gpu => T("compute.gpu"),
		TtsComputeMode.Igpu => T("compute.igpu"),
		TtsComputeMode.Cpu => T("compute.cpu"),
		_ => T("compute.auto"),
	};

	public static string LangName(string code) {
		code = TrLang.Normalize(code ?? "");
		if (string.IsNullOrEmpty(code)) return T("lang.all");
		if (code == "auto") return T("lang.auto");
		var key = "lang." + code;
		var name = T(key);
		return name == key ? code : name;
	}

	static bool tryget(string code, string key, out string value) {
		value = "";
		if (string.IsNullOrEmpty(code) || !maps.TryGetValue(code, out var map) || map == null)
			return false;
		if (!map.TryGetValue(key, out value) || string.IsNullOrEmpty(value)) return false;
		return true;
	}

	static void ensure() {
		if (loaded) return;
		lock (gate) {
			if (loaded) return;
			load();
			loaded = true;
		}
	}

	static void load() {
		var next = new List<UiLang>();
		var nextmaps = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
		var dir = root();
		if (Directory.Exists(dir)) {
			string[] files;
			try { files = Directory.GetFiles(dir, "*.toml"); }
			catch { files = Array.Empty<string>(); }
			foreach (var f in files) {
				var code = Path.GetFileNameWithoutExtension(f).Trim().ToLowerInvariant();
				if (!okcode(code)) continue;
				if (!readfile(f, out var name, out var map)) continue;
				if (string.IsNullOrWhiteSpace(name)) name = code;
				nextmaps[code] = map;
				next.Add(new UiLang(code, name.Trim()));
			}
		}
		next.Sort(cmp);
		maps = nextmaps;
		langs = next;
		lang = norm(lang);
	}

	static int cmp(UiLang a, UiLang b) {
		var d = rank(a.Code) - rank(b.Code);
		if (d != 0) return d;
		return string.Compare(a.Code, b.Code, StringComparison.OrdinalIgnoreCase);
	}

	static int rank(string code) {
		if (isprefix(code, "zh")) return 0;
		if (isprefix(code, "en")) return 1;
		if (isprefix(code, "ja")) return 2;
		if (isprefix(code, "ko")) return 3;
		if (isprefix(code, "es")) return 4;
		if (isprefix(code, "fr")) return 5;
		if (isprefix(code, "pt")) return 6;
		if (isprefix(code, "ru")) return 7;
		if (isprefix(code, "de")) return 8;
		return 9;
	}

	static bool isprefix(string code, string prefix) {
		if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(prefix)) return false;
		if (string.Equals(code, prefix, StringComparison.OrdinalIgnoreCase)) return true;
		return code.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase);
	}

	static bool okcode(string code) {
		if (string.IsNullOrEmpty(code)) return false;
		for (var i = 0; i < code.Length; i++) {
			var c = code[i];
			var ok = c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-' || c == '_';
			if (!ok) return false;
		}
		return true;
	}

	static string norm(string raw) {
		var v = (raw ?? "").Trim().ToLowerInvariant().Replace('_', '-');
		if (v is "english") v = En;
		else if (v is "chinese" or "zh-cn" or "zh-hans" or "zh-sg") v = Zh;
		else if (v is "japanese" or "jp") v = "ja";
		else if (v is "korean" or "kr") v = "ko";
		else if (v is "spanish" or "espanol" or "español") v = "es";
		else if (v is "french" or "francais" or "français") v = "fr";
		else if (v is "portuguese" or "portugues" or "português") v = "pt";
		else if (v is "russian") v = "ru";
		else if (v is "german" or "deutsch") v = "de";
		if (has(v)) return v;
		if (v.StartsWith("zh", StringComparison.Ordinal) && has(Zh)) return Zh;
		if (v.StartsWith("en", StringComparison.Ordinal) && has(En)) return En;
		if (v.StartsWith("ja", StringComparison.Ordinal) && has("ja")) return "ja";
		if (v.StartsWith("ko", StringComparison.Ordinal) && has("ko")) return "ko";
		if (v.StartsWith("es", StringComparison.Ordinal) && has("es")) return "es";
		if (v.StartsWith("fr", StringComparison.Ordinal) && has("fr")) return "fr";
		if (v.StartsWith("pt", StringComparison.Ordinal) && has("pt")) return "pt";
		if (v.StartsWith("ru", StringComparison.Ordinal) && has("ru")) return "ru";
		if (v.StartsWith("de", StringComparison.Ordinal) && has("de")) return "de";
		if (has(Zh)) return Zh;
		if (langs.Count > 0) return langs[0].Code;
		return Zh;
	}

	static bool has(string code) =>
		!string.IsNullOrEmpty(code) && maps.ContainsKey(code);

	static string root() {
		if (!string.IsNullOrEmpty(testRoot)) return testRoot;
		return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lang");
	}

	static bool readfile(string path, out string name, out Dictionary<string, string> map) {
		name = "";
		map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string text;
		try { text = File.ReadAllText(path, Encoding.UTF8); }
		catch { return false; }
		var section = "";
		foreach (var raw in text.Replace("\r\n", "\n").Split('\n')) {
			var line = raw.Trim();
			if (line.Length == 0 || line[0] == '#') continue;
			if (line[0] == '[') {
				var end = line.IndexOf(']');
				section = end > 1 ? line.Substring(1, end - 1).Trim().ToLowerInvariant() : "";
				continue;
			}
			var eq = line.IndexOf('=');
			if (eq <= 0) continue;
			var key = line.Substring(0, eq).Trim();
			if (key.Length == 0) continue;
			var val = unquote(line.Substring(eq + 1).Trim());
			if (section.Length == 0 || section == "meta") {
				if (key.Equals("name", StringComparison.OrdinalIgnoreCase)) {
					name = val;
					continue;
				}
				if (key.Equals("code", StringComparison.OrdinalIgnoreCase)) continue;
				if (section == "meta") continue;
			}
			if (section.Length == 0 || section == "text" || section == "strings")
				map[key] = val;
		}
		return true;
	}

	static string unquote(string val) {
		if (val.Length < 2 || val[0] != '"' || val[val.Length - 1] != '"') return val;
		var inner = val.Substring(1, val.Length - 2);
		var sb = new StringBuilder(inner.Length);
		for (var i = 0; i < inner.Length; i++) {
			var c = inner[i];
			if (c != '\\' || i + 1 >= inner.Length) {
				sb.Append(c);
				continue;
			}
			var n = inner[++i];
			if (n == 'n') sb.Append('\n');
			else if (n == 'r') sb.Append('\r');
			else if (n == 't') sb.Append('\t');
			else sb.Append(n);
		}
		return sb.ToString();
	}
}
