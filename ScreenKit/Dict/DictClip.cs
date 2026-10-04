namespace ScreenKit;

// ==================== 词典热键：剪贴板是否像一个单词 ====================
// 不向前台发 Ctrl+C。汉字 1–4，英文 1–20 个字母，带假名的日语 1–12 字，韩语 1–8 个音节。
#region
static class DictClip {
	const string WRAP = "\"'“”‘’「」『』《》〈〉()（）[]【】<>";
	const string TAIL = ".,，。!！?？;；:：、…";

	/// <summary>像单词时返回要查询的词，否则返回空。会去掉首尾空白和一层包裹符号。</summary>
	public static string Word(string raw) {
		var s = trimwrap(raw);
		if (s.Length == 0) return "";
		for (var i = 0; i < s.Length; i++)
			if (char.IsWhiteSpace(s[i])) return "";
		s = foldlatin(s);
		if (ishanword(s) || islatinword(s) || isjapaneseword(s) || iskoreanword(s))
			return s;
		return "";
	}

	static string trimwrap(string raw) {
		if (string.IsNullOrWhiteSpace(raw)) return "";
		var s = raw.Trim();
		int a = 0, b = s.Length;
		while (a < b && WRAP.IndexOf(s[a]) >= 0) a++;
		while (b > a && (WRAP.IndexOf(s[b - 1]) >= 0 || TAIL.IndexOf(s[b - 1]) >= 0)) b--;
		if (a >= b) return "";
		return s.Substring(a, b - a).Trim();
	}

	static string foldlatin(string s) {
		var wide = false;
		for (var i = 0; i < s.Length; i++) {
			var c = s[i];
			if (c >= '\uFF21' && c <= '\uFF3A' || c >= '\uFF41' && c <= '\uFF5A') {
				wide = true;
				break;
			}
		}
		if (!wide) return s;
		var buf = s.ToCharArray();
		for (var i = 0; i < buf.Length; i++) {
			var c = buf[i];
			if (c >= '\uFF21' && c <= '\uFF3A') buf[i] = (char)(c - '\uFF21' + 'A');
			else if (c >= '\uFF41' && c <= '\uFF5A') buf[i] = (char)(c - '\uFF41' + 'a');
		}
		return new string(buf);
	}

	static bool ishan(char c) =>
		c >= '\u4E00' && c <= '\u9FFF' || c >= '\u3400' && c <= '\u4DBF';

	static bool iskana(char c) =>
		c >= '\u3040' && c <= '\u30FF' || c >= '\uFF66' && c <= '\uFF9D';

	static bool ishangul(char c) => c >= '\uAC00' && c <= '\uD7A3';

	static bool ishanword(string s) {
		if (s.Length < 1 || s.Length > 4) return false;
		for (var i = 0; i < s.Length; i++)
			if (!ishan(s[i])) return false;
		return true;
	}

	static bool islatinword(string s) {
		if (s.Length < 1 || s.Length > 40) return false;
		var letters = 0;
		var prevSep = false;
		for (var i = 0; i < s.Length; i++) {
			var c = s[i];
			if (c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z') {
				letters++;
				prevSep = false;
				continue;
			}
			var sep = c == '\'' || c == '-' || c == '\u2019';
			if (sep && i > 0 && i < s.Length - 1 && !prevSep) {
				prevSep = true;
				continue;
			}
			return false;
		}
		return letters >= 1 && letters <= 20;
	}

	static bool isjapaneseword(string s) {
		if (s.Length < 1 || s.Length > 12) return false;
		var kana = false;
		for (var i = 0; i < s.Length; i++) {
			var c = s[i];
			if (iskana(c)) { kana = true; continue; }
			if (ishan(c)) continue;
			return false;
		}
		return kana;
	}

	static bool iskoreanword(string s) {
		if (s.Length < 1 || s.Length > 8) return false;
		for (var i = 0; i < s.Length; i++)
			if (!ishangul(s[i])) return false;
		return true;
	}
}
#endregion
