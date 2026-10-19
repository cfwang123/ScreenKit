using System.Reflection;
using System.Text;

namespace ScreenKit;

/// <summary>
/// 用程序旁的 NPinyin.dll 把汉字转成拼音或首字母。程序集从字节加载，卸掉文件时不锁住。
/// 常见多音词读 pinyin.toml，pinyin.user.toml 里的同名词条盖住它。空读音表示去掉系统里的这一条。
/// </summary>
static class PinyinConvert {
	public const int MAXLEN = 20000;

	static readonly object gate = new();
	static Assembly asm;
	static MethodInfo py;
	static Dictionary<char, Hit[]> hits;
	static string sysstamp = "";
	static string userstamp = "";

	public static bool Installed => NativeRuntime.HasNPinyin();

	/// <summary>initials 为真时每个字一个首字母，否则是空格分开、不带声调的拼音。结果一律小写。</summary>
	public static string Convert(string text, bool initials) {
		if (!Installed) throw new InvalidOperationException("NPinyin 未安装");
		text ??= "";
		if (text.Length > MAXLEN) throw new ArgumentException("文字太长");
		lock (gate) {
			reload();
			ensure();
			var syl = new string[text.Length];
			for (var i = 0; i < text.Length;) {
				var n = apply(text, i, syl);
				if (n > 0) {
					i += n;
					continue;
				}
				syl[i] = onesyl(text[i]);
				i++;
			}
			var body = initials ? joinini(text, syl) : joinpy(text, syl);
			return body.ToLowerInvariant();
		}
	}

	static void reload() {
		var dir = NativeRuntime.BaseDir;
		var sys = Path.Combine(dir, "pinyin.toml");
		var user = Path.Combine(dir, "pinyin.user.toml");
		var st = stamp(sys);
		var ut = stamp(user);
		if (hits != null && st == sysstamp && ut == userstamp) return;
		var map = new Dictionary<string, string[]>();
		readwords(sys, map, false);
		readwords(user, map, true);
		hits = pack(map);
		sysstamp = st;
		userstamp = ut;
	}

	static string stamp(string path) {
		try {
			var info = new FileInfo(path);
			if (!info.Exists) return "";
			return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
		}
		catch {
			return "";
		}
	}

	static void readwords(string path, Dictionary<string, string[]> map, bool user) {
		string text;
		try {
			if (!File.Exists(path)) return;
			text = File.ReadAllText(path);
		}
		catch {
			return;
		}
		foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')) {
			var line = raw.Trim();
			if (line.Length == 0 || line[0] == '#' || line[0] == '[') continue;
			var eq = line.IndexOf('=');
			if (eq <= 0) continue;
			var word = unquote(line.Substring(0, eq).Trim());
			if (word.Length == 0 || hasblank(word)) continue;
			var val = unquote(striphash(line.Substring(eq + 1).Trim())).Trim();
			if (val.Length == 0) {
				if (user) map.Remove(word);
				continue;
			}
			var parts = val.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length != word.Length) continue;
			for (var i = 0; i < parts.Length; i++)
				parts[i] = parts[i].ToLowerInvariant();
			map[word] = parts;
		}
	}

	static string striphash(string val) {
		if (val.Length >= 2 && val[0] == '"') {
			var end = val.LastIndexOf('"');
			if (end > 0) return val.Substring(0, end + 1);
		}
		var hash = val.IndexOf('#');
		if (hash >= 0) return val.Substring(0, hash).Trim();
		return val;
	}

	static string unquote(string val) {
		if (val.Length >= 2 && val[0] == '"' && val[val.Length - 1] == '"')
			return val.Substring(1, val.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\");
		return val;
	}

	static bool hasblank(string word) {
		foreach (var ch in word)
			if (char.IsWhiteSpace(ch)) return true;
		return false;
	}

	static Dictionary<char, Hit[]> pack(Dictionary<string, string[]> map) {
		var lists = new Dictionary<char, List<Hit>>();
		foreach (var pair in map) {
			var word = pair.Key;
			if (!lists.TryGetValue(word[0], out var list)) {
				list = new List<Hit>();
				lists[word[0]] = list;
			}
			list.Add(new Hit { Word = word, Syl = pair.Value });
		}
		var built = new Dictionary<char, Hit[]>();
		foreach (var pair in lists) {
			pair.Value.Sort((a, b) => b.Word.Length.CompareTo(a.Word.Length));
			built[pair.Key] = pair.Value.ToArray();
		}
		return built;
	}

	static int apply(string text, int i, string[] syl) {
		if (hits == null || !hits.TryGetValue(text[i], out var list)) return 0;
		foreach (var hit in list) {
			if (i + hit.Word.Length > text.Length) continue;
			if (!same(text, i, hit.Word)) continue;
			for (var k = 0; k < hit.Word.Length; k++) syl[i + k] = hit.Syl[k];
			return hit.Word.Length;
		}
		return 0;
	}

	static bool same(string text, int i, string word) {
		for (var k = 0; k < word.Length; k++)
			if (text[i + k] != word[k]) return false;
		return true;
	}

	static string onesyl(char ch) {
		if (char.IsWhiteSpace(ch)) return "";
		return py.Invoke(null, new object[] { ch.ToString() }) as string ?? "";
	}

	/// <summary>和 NPinyin 整段结果一样：每个字后面跟一个空格，空白字再多一个空格，最后去掉两端空白。</summary>
	static string joinpy(string text, string[] syl) {
		var sb = new StringBuilder();
		for (var i = 0; i < text.Length; i++) {
			if (char.IsWhiteSpace(text[i])) sb.Append(' ');
			else sb.Append(syl[i] ?? "");
			sb.Append(' ');
		}
		return sb.ToString().Trim();
	}

	/// <summary>连续非空白算一组。组内取每个音节的首字母，组与组之间一个空格。</summary>
	static string joinini(string text, string[] syl) {
		var sb = new StringBuilder();
		var gap = false;
		var any = false;
		for (var i = 0; i < text.Length; i++) {
			if (char.IsWhiteSpace(text[i])) {
				if (any) gap = true;
				continue;
			}
			var s = syl[i];
			if (string.IsNullOrEmpty(s)) continue;
			if (gap) {
				sb.Append(' ');
				gap = false;
			}
			sb.Append(s[0]);
			any = true;
		}
		return sb.ToString();
	}

	static void ensure() {
		if (asm != null) return;
		var path = Path.Combine(NativeRuntime.BaseDir, "NPinyin.dll");
		asm = Assembly.Load(File.ReadAllBytes(path));
		var t = asm.GetType("NPinyin.Pinyin");
		if (t == null) throw new InvalidOperationException("NPinyin.dll 里没有 NPinyin.Pinyin");
		py = t.GetMethod("GetPinyin", new[] { typeof(string) });
		if (py == null) throw new InvalidOperationException("NPinyin.dll 缺少 GetPinyin");
	}

	struct Hit {
		public string Word;
		public string[] Syl;
	}
}
