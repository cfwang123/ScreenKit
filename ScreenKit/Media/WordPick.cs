using System;
using System.Collections.Generic;
using System.IO;

namespace ScreenKit;

// ==================== 外挂字幕/歌词自动匹配 ====================
#region
static class TimedTextFinder {
	static readonly string[] langtags = {
		"zh", "chs", "cht", "chinese", "chi", "zho", "cn", "sc", "tc",
		"en", "eng", "english",
		"ja", "jp", "jpn", "japanese",
		"ko", "kr", "kor", "korean",
	};
	static readonly string[] prefs = { ".srt", ".vtt", ".ass", ".ssa", ".lrc" };

	/// <summary>在媒体文件旁找同名字幕/歌词，按 完全同名 → 同名+语言后缀 排序。</summary>
	public static string Find(string mediaPath) {
		if (string.IsNullOrWhiteSpace(mediaPath)) return null;
		var dir = Path.GetDirectoryName(mediaPath);
		if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
		var stem = Path.GetFileNameWithoutExtension(mediaPath);
		if (string.IsNullOrEmpty(stem)) return null;
		List<string> files;
		try {
			files = new List<string>(Directory.EnumerateFiles(dir));
		}
		catch { return null; }
		string best = null;
		var bestscore = int.MinValue;
		foreach (var f in files) {
			var ext = Path.GetExtension(f).ToLowerInvariant();
			if (Array.IndexOf(prefs, ext) < 0) continue;
			var name = Path.GetFileNameWithoutExtension(f);
			if (!name.StartsWith(stem, StringComparison.OrdinalIgnoreCase)) continue;
			var score = scorefor(name, stem, ext);
			if (score > bestscore) {
				bestscore = score;
				best = f;
			}
		}
		return best;
	}

	static int scorefor(string name, string stem, string ext) {
		// 尾随部分必须是空或语言标记，避免 a.srt 匹配到 abc.srt
		var tail = name.Length > stem.Length ? name.Substring(stem.Length) : "";
		if (tail.Length > 0 && tail[0] != '.' && tail[0] != '_' && tail[0] != '-' && tail[0] != ' ') return int.MinValue;
		var extscore = Array.IndexOf(prefs, ext);
		if (extscore < 0) extscore = prefs.Length;
		var bonus = 0;
		if (tail.Length == 0) bonus = 100;
		else {
			var tag = tail.Trim('.', '_', '-', ' ');
			var idx = Array.IndexOf(langtags, tag.ToLowerInvariant());
			if (idx >= 0) bonus = 50 - idx;
			else bonus = 10;
		}
		// 扩展名优先级次要，同分时 srt > vtt > ass > ssa > lrc
		return bonus * 10 - extscore;
	}

	/// <summary>找歌词：优先 .lrc，其次同名 .srt。</summary>
	public static string FindLyric(string mediaPath) {
		if (string.IsNullOrWhiteSpace(mediaPath)) return null;
		var dir = Path.GetDirectoryName(mediaPath);
		if (string.IsNullOrEmpty(dir)) return null;
		var stem = Path.GetFileNameWithoutExtension(mediaPath);
		try {
			foreach (var cand in new[] { stem + ".lrc", stem + ".LRC" }) {
				var p = Path.Combine(dir, cand);
				if (File.Exists(p)) return p;
			}
		}
		catch { }
		var all = Find(mediaPath);
		if (all != null && Path.GetExtension(all).Equals(".lrc", StringComparison.OrdinalIgnoreCase)) return all;
		return null;
	}
}
#endregion

// ==================== 点词切片 ====================
// 从字幕一行里按点中位置切出最小可查词：拉丁按单词、CJK 按最长词典匹配。
#region
static class WordPick {
	public enum CharClass { Punct, Digit, Latin, Han, Kana, Hangul, Other }

	public static CharClass ClassOf(char c) {
		if (char.IsWhiteSpace(c)) return CharClass.Punct;
		if (c >= '\u4E00' && c <= '\u9FFF' || c >= '\u3400' && c <= '\u4DBF') return CharClass.Han;
		if (c >= '\u3040' && c <= '\u30FF' || c >= '\uFF66' && c <= '\uFF9D') return CharClass.Kana;
		if (c >= '\uAC00' && c <= '\uD7A3' || c >= '\u1100' && c <= '\u11FF') return CharClass.Hangul;
		if (c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '\uFF21' && c <= '\uFF3A' || c >= '\uFF41' && c <= '\uFF5A')
			return CharClass.Latin;
		if (char.IsDigit(c)) return CharClass.Digit;
		if (c == '\'' || c == '-' || c == '\u2019') return CharClass.Latin;
		return CharClass.Punct;
	}

	/// <summary>全角转半角，只处理字母数字。</summary>
	public static string Fold(string s) {
		if (string.IsNullOrEmpty(s)) return s;
		var wide = false;
		foreach (var c in s)
			if (c >= '\uFF01' && c <= '\uFF5E') { wide = true; break; }
		if (!wide) return s;
		var buf = s.ToCharArray();
		for (var i = 0; i < buf.Length; i++) {
			var c = buf[i];
			if (c >= '\uFF01' && c <= '\uFF5E') buf[i] = (char)(c - 0xFEE0);
		}
		return new string(buf);
	}

	/// <summary>
	/// 取点中位置所在的原始片段（不查词典）：拉丁整词、CJK 单字。
	/// 查询失败时退回用它，保证总有东西可查。
	/// </summary>
	public static string Raw(string text, int index, out int start, out int len) {
		start = 0;
		len = 0;
		if (string.IsNullOrEmpty(text)) return "";
		if (index < 0) index = 0;
		if (index >= text.Length) index = text.Length - 1;
		var cls = ClassOf(text[index]);
		if (cls == CharClass.Latin || cls == CharClass.Digit) {
			var a = index;
			var b = index;
			while (a > 0 && (ClassOf(text[a - 1]) == cls || cls == CharClass.Latin && ClassOf(text[a - 1]) == CharClass.Latin)) a--;
			while (b + 1 < text.Length && ClassOf(text[b + 1]) == cls) b++;
			start = a;
			len = b - a + 1;
			return Fold(text.Substring(start, len));
		}
		start = index;
		len = 1;
		return text.Substring(index, 1);
	}

	/// <summary>CJK 候选窗口：点中字向左右各扩 maxspan 个同语种字符，按长度优先排列。</summary>
	public static List<string> CjkCandidates(string text, int index, int maxspan = 6) {
		var list = new List<string>();
		if (string.IsNullOrEmpty(text) || index < 0 || index >= text.Length) return list;
		var cls = ClassOf(text[index]);
		if (cls != CharClass.Han && cls != CharClass.Kana && cls != CharClass.Hangul) return list;
		var a = index;
		while (a > 0 && IsCjkClass(ClassOf(text[a - 1])) && index - a < maxspan) a--;
		var b = index;
		while (b + 1 < text.Length && IsCjkClass(ClassOf(text[b + 1])) && b - index < maxspan) b++;
		for (var l = b - a + 1; l >= 1; l--) {
			for (var s = a; s + l - 1 <= b; s++) {
				if (index < s || index > s + l - 1) continue;
				list.Add(text.Substring(s, l));
			}
		}
		return list;
	}

	/// <summary>
	/// 最长匹配：窗口内所有含点中字的子串按长度从长到短试，命中即返回。
	/// 长优先保证「图书馆」胜过「书馆」；同长度时从左侧起头。
	/// 都失败退回点中单字。
	/// </summary>
	public static string Longest(string text, int index, Func<string, bool> exists, int maxspan = 6) {
		if (string.IsNullOrEmpty(text) || index < 0 || index >= text.Length) return "";
		var cls = ClassOf(text[index]);
		if (cls != CharClass.Han && cls != CharClass.Kana && cls != CharClass.Hangul) return "";
		foreach (var c in CjkCandidates(text, index, maxspan)) {
			if (c.Length < 2) break;
			if (exists(c)) return c;
		}
		return text.Substring(index, 1);
	}

	static bool IsCjkClass(CharClass c) =>
		c == CharClass.Han || c == CharClass.Kana || c == CharClass.Hangul;
}
#endregion
