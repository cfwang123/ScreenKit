using System.Text;

namespace ScreenKit;

/// <summary>自定义词库：程序旁 dict/ 下的 wubi.txt、ja.txt、kr.txt。
/// 文本是词库本体，输入法开启和打开词库窗口时按文件变化同步进 ime.db 的用户词表，
/// 重装 ime.db 后由这些文件恢复。用户平时选用、置顶的词也留在同一批表里。</summary>
static class ImeDict {
	/// <summary>三种语言的代码，也是 user_* 表的分类。</summary>
	public static readonly string[] Kinds = { "wubi", "ja", "ko" };

	static string root;

	public static string DirOf => string.IsNullOrEmpty(root)
		? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dict")
		: root;

	public static bool Known(string kind) => Array.IndexOf(Kinds, kind) >= 0;

	public static string FileOf(string kind) => Path.Combine(DirOf, name(kind));

	/// <summary>测试时换一个词库目录。传空恢复程序旁。</summary>
	public static void SetRootForTest(string dir) => root = string.IsNullOrWhiteSpace(dir) ? null : dir;

	static string name(string kind) => kind == "ja" ? "ja.txt" : kind == "ko" ? "kr.txt" : "wubi.txt";

	static string title(string kind) => kind == "ja" ? "日语" : kind == "ko" ? "韩语" : "五笔";

	/// <summary>ime.db 里该语言的用户词，导入的加平时攒的。</summary>
	public static List<ImeDictEntry> List(string kind) => ImeLex.UserWords(kind);

	/// <summary>dict/&lt;kind&gt;.txt 里的词。bad 是认不出的行数。</summary>
	public static List<ImeDictEntry> Read(string kind, out int bad) {
		bad = 0;
		if (!Known(kind)) return new List<ImeDictEntry>();
		var path = FileOf(kind);
		try {
			if (!File.Exists(path)) return new List<ImeDictEntry>();
			return Parse(kind, ReadText(path), out bad);
		}
		catch { return new List<ImeDictEntry>(); }
	}

	/// <summary>文件变了就重新灌进 ime.db。返回导入条数；0 表示没文件或没变化。</summary>
	public static int Sync(string kind) {
		if (!Known(kind)) return 0;
		var path = FileOf(kind);
		string stamp;
		try {
			if (!File.Exists(path)) return 0;
			stamp = Stamp(path);
			if (ImeLex.Meta("dict:" + kind) == stamp) return 0;
		}
		catch { return 0; }
		return load(kind, stamp);
	}

	public static int SyncAll() {
		int n = 0;
		foreach (var kind in Kinds) n += Sync(kind);
		return n;
	}

	/// <summary>把一份文本词库并进 dict/&lt;kind&gt;.txt，再同步进库。
	/// 返回新并入的条数，total 是合并后文件里的总数，bad 是认不出的行数。</summary>
	public static int Import(string kind, string path, out int total, out int bad) {
		total = 0;
		bad = 0;
		if (!Known(kind) || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return 0;
		var add = Parse(kind, ReadText(path), out bad);
		var have = Read(kind, out _);
		var seen = new HashSet<string>();
		foreach (var e in have) seen.Add(sig(e));
		int n = 0;
		foreach (var e in add) {
			if (!seen.Add(sig(e))) continue;
			have.Add(e);
			n++;
		}
		total = have.Count;
		Write(kind, have);
		load(kind, Stamp(FileOf(kind)));
		return n;
	}

	/// <summary>把该语言的全部用户词按词库格式写到指定文件。返回条数。</summary>
	public static int Export(string kind, string path) {
		if (!Known(kind) || string.IsNullOrWhiteSpace(path)) return 0;
		var rows = List(kind);
		var sb = new StringBuilder();
		header(sb, kind);
		foreach (var e in rows) sb.AppendLine(line(e));
		File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
		return rows.Count;
	}

	/// <summary>删掉一条：先从库里删，再从词库里删，然后按词库重新灌一遍。</summary>
	public static void Remove(string kind, ImeDictEntry e) {
		if (!Known(kind) || e == null) return;
		ImeLex.DeleteWord(kind, e);
		var have = Read(kind, out _);
		int n = have.RemoveAll(x => sig(x) == sig(e));
		if (n > 0) {
			Write(kind, have);
			load(kind, Stamp(FileOf(kind)));
		}
	}

	/// <summary>清空该语言的用户词：库里全删，词库文本只留表头。</summary>
	public static void Clear(string kind) {
		if (!Known(kind)) return;
		ImeLex.ClearWords(kind);
		Write(kind, new List<ImeDictEntry>());
		try { ImeLex.SaveMeta("dict:" + kind, Stamp(FileOf(kind))); } catch { }
	}

	static int load(string kind, string stamp) {
		int bad;
		var rows = Read(kind, out bad);
		int n = ImeLex.ImportWords(kind, rows);
		try { ImeLex.SaveMeta("dict:" + kind, stamp); } catch { }
		return n;
	}

	static void Write(string kind, List<ImeDictEntry> rows) {
		try {
			Directory.CreateDirectory(DirOf);
			var sb = new StringBuilder();
			header(sb, kind);
			foreach (var e in rows) sb.AppendLine(line(e));
			File.WriteAllText(FileOf(kind), sb.ToString(), new UTF8Encoding(false));
		}
		catch { }
	}

	static void header(StringBuilder sb, string kind) {
		sb.AppendLine("# " + title(kind) + "自定义词库。一行一条，制表符或空格分开，行首 # 是注释。");
		if (kind == "ja") sb.AppendLine("# 读音 + 词。读音写假名，写罗马字也行，导入时转成假名。");
		else if (kind == "ko") sb.AppendLine("# 词。第二个字段可写词频，现在用不上，留空即可。");
		else sb.AppendLine("# 编码 + 词。");
		sb.AppendLine("# 输入法开启和打开词库窗口时，按文件变化同步进 ime.db 的用户词；重装 ime.db 后由本文件恢复。");
	}

	static string line(ImeDictEntry e) {
		if (e == null) return "";
		return string.IsNullOrEmpty(e.Key) ? e.Text : e.Key + "\t" + e.Text;
	}

	static string sig(ImeDictEntry e) => e == null ? "" : e.Key + "\n" + e.Text;

	static string Stamp(string path) {
		var f = new FileInfo(path);
		return f.Length + ":" + f.LastWriteTimeUtc.Ticks;
	}

	/// <summary>认 BOM；没 BOM 先按 UTF-8 严解，失败再按 GBK。</summary>
	public static string ReadText(string path) {
		var b = File.ReadAllBytes(path);
		if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
			return Encoding.Unicode.GetString(b, 2, b.Length - 2);
		if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
			return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
		if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
			return new UTF8Encoding(false).GetString(b, 3, b.Length - 3);
		try { return new UTF8Encoding(false, true).GetString(b); }
		catch {
			try { return Encoding.GetEncoding(936).GetString(b); }
			catch { return Encoding.Default.GetString(b); }
		}
	}

	/// <summary>解析词库文本。认不出的行丢掉并计数。</summary>
	public static List<ImeDictEntry> Parse(string kind, string text, out int bad) {
		bad = 0;
		var list = new List<ImeDictEntry>();
		if (string.IsNullOrEmpty(text) || !Known(kind)) return list;
		var seen = new HashSet<string>();
		foreach (var raw in text.Split('\n')) {
			var s = raw.Trim();
			if (s.Length == 0 || s[0] == '#' || s[0] == ';') continue;
			var e = one(kind, field(s));
			if (e == null) { bad++; continue; }
			if (!seen.Add(sig(e))) continue;
			list.Add(e);
		}
		return list;
	}

	static string[] field(string line) {
		var parts = line.Split('\t');
		if (parts.Length >= 2) return parts;
		return line.Split(new[] { ' ', '，', ',' }, StringSplitOptions.RemoveEmptyEntries);
	}

	static ImeDictEntry one(string kind, string[] raw) {
		var f = new List<string>();
		foreach (var s in raw) {
			var v = s.Trim();
			if (v.Length > 0) f.Add(v);
		}
		if (kind == "ko") {
			if (f.Count == 0) return null;
			return new ImeDictEntry { Text = f[0] };
		}
		if (f.Count < 2) return null;
		string key, text;
		if (kind == "wubi") {
			if (ascii(f[0])) { key = f[0]; text = f[1]; }
			else if (ascii(f[1])) { key = f[1]; text = f[0]; }
			else return null;
			key = key.ToLowerInvariant();
			if (key.Length > 8) return null;
		} else {
			if (kana(f[0]) && !kana(f[1])) { key = f[0]; text = f[1]; }
			else if (kana(f[1]) && !kana(f[0])) { key = f[1]; text = f[0]; }
			else if (ascii(f[0]) && !ascii(f[1])) { key = f[0]; text = f[1]; }
			else if (ascii(f[1]) && !ascii(f[0])) { key = f[1]; text = f[0]; }
			else { key = f[0]; text = f[1]; }
			key = roman(key);
		}
		if (key.Length == 0 || text.Length == 0) return null;
		return new ImeDictEntry { Key = key, Text = text };
	}

	static bool ascii(string s) {
		if (string.IsNullOrEmpty(s)) return false;
		foreach (var c in s) {
			if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) continue;
			return false;
		}
		return true;
	}

	/// <summary>整串都是假名（含长音符、小假名），没有汉字和其它字：日语读音的那一边。</summary>
	static bool kana(string s) {
		if (string.IsNullOrEmpty(s)) return false;
		bool hit = false;
		foreach (var c in s) {
			if (c == 'ー' || c == '・' || c == '　') { hit = true; continue; }
			if (c >= '\u3041' && c <= '\u3096') { hit = true; continue; }
			if (c >= '\u30A1' && c <= '\u30FA') { hit = true; continue; }
			return false;
		}
		return hit;
	}

	/// <summary>罗马字读音转平假名，转不动就原样留着。</summary>
	static string roman(string s) {
		if (!ascii(s)) return s;
		var k = new Kana();
		foreach (var c in s.ToLowerInvariant()) k.Push(c);
		k.FinishN();
		var t = k.KanaText;
		return t.Length > 0 ? t : s;
	}
}
