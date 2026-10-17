using System;
using System.Collections.Generic;
using System.Text;

// 对齐 rustdict dict-core/src/romanize.rs：搜索用日语 romaji → 平假名、韩语罗马字 → 谚文。
namespace ScreenKit;

static class DictRomanize {
	public static List<string> RomajiToHiraganaVariants(string q) {
		var h = romajitohiragana(q);
		if (h == null) return new List<string>();
		var outv = new List<string>(2) { h };
		if (h.Length >= 3) {
			var alt = particlealt(h);
			if (alt != null && alt != h) outv.Add(alt);
		}
		return outv;
	}

	public static List<string> RomanToHangulVariants(string q) {
		var cleaned = cleankoroman(q);
		if (cleaned == null) return new List<string>();
		if (countalpha(cleaned) < 2) return new List<string>();
		var outv = new List<string>(2);
		var h1 = parsehangul(cleaned, true);
		if (h1 != null) outv.Add(h1);
		var h2 = parsehangul(cleaned, false);
		if (h2 != null && !outv.Contains(h2)) outv.Add(h2);
		return outv;
	}

	/// <summary>CLI / 自检。</summary>
	internal static bool SelfTest() {
		if (romajitohiragana("watashi") != "わたし") return false;
		if (romajitohiragana("saseko") != "させこ") return false;
		if (romajitohiragana("hanguk") != null) return false;
		var v = RomajiToHiraganaVariants("konnichiwa");
		if (!v.Contains("こんにちは")) return false;
		var kimchi = RomanToHangulVariants("kimchi");
		if (!kimchi.Contains("김치")) return false;
		if (RomanToHangulVariants("hanguk").Count != 1 || RomanToHangulVariants("hanguk")[0] != "한국") return false;
		return true;
	}

	static string romajitohiragana(string q) {
		if (countalpha(q) < 2) return null;
		foreach (var c in q) {
			if (c >= 'a' && c <= 'z') continue;
			if (c == '\'' || c == '-') continue;
			return null;
		}
		var s = q;
		var i = 0;
		var outb = new StringBuilder();
		while (i < s.Length) {
			if (s[i] == '\'') { i++; continue; }
			if (s[i] == '-') { outb.Append('ー'); i++; continue; }
			if (consumesokuon(s, i)) { outb.Append('っ'); i++; continue; }
			var n = consumen(s, i);
			if (n != null) { outb.Append('ん'); i += n.Value; continue; }
			var mora = matchmora(s, i);
			if (mora == null) return null;
			outb.Append(mora.Value.Kana);
			i += mora.Value.Len;
		}
		var text = outb.ToString();
		if (text.Length == 0) return null;
		foreach (var c in text) if (!ishiraout(c)) return null;
		return text;
	}

	static string particlealt(string h) {
		if (h.Length == 0) return null;
		var last = h[h.Length - 1];
		char repl;
		switch (last) {
			case 'わ': repl = 'は'; break;
			case 'え': repl = 'へ'; break;
			case 'お': repl = 'を'; break;
			default: return null;
		}
		return h.Substring(0, h.Length - 1) + repl;
	}

	static int countalpha(string s) {
		var n = 0;
		foreach (var c in s) if (c >= 'a' && c <= 'z') n++;
		return n;
	}

	static bool ishiraout(char c) => c >= 'ぁ' && c <= 'ゔ' || c == 'ー';

	static bool isvowel(char c) => c == 'a' || c == 'i' || c == 'u' || c == 'e' || c == 'o';

	static bool consumesokuon(string s, int i) {
		if (i >= s.Length) return false;
		var c = s[i];
		if (isvowel(c) || c == 'n' || c == 'y') return false;
		if (c == 't' && i + 2 < s.Length && s[i + 1] == 'c' && s[i + 2] == 'h') return true;
		return i + 1 < s.Length && s[i + 1] == c;
	}

	static int? consumen(string s, int i) {
		if (i >= s.Length) return null;
		var c = s[i];
		if (c == 'm') {
			if (i + 1 < s.Length && (s[i + 1] == 'b' || s[i + 1] == 'p')) return 1;
			return null;
		}
		if (c != 'n') return null;
		char? next = i + 1 < s.Length ? s[i + 1] : null;
		char? next2 = i + 2 < s.Length ? s[i + 2] : null;
		if (next == '\'') return 2;
		if (next == 'n') {
			if (next2 == null || (!isvowel(next2.Value) && next2 != 'y')) return 2;
			return 1;
		}
		if (next == null || (!isvowel(next.Value) && next != 'y' && next != '\'')) return 1;
		return null;
	}

	static (string Kana, int Len)? matchmora(string s, int i) {
		var rest = s.Substring(i);
		foreach (var (k, v) in MORA) {
			if (rest.StartsWith(k, StringComparison.Ordinal)) return (v, k.Length);
		}
		return null;
	}

	static string cleankoroman(string q) {
		var sb = new StringBuilder(q.Length);
		foreach (var c in q) {
			if (c >= 'a' && c <= 'z') sb.Append(c);
			else if (c == '\'' || c == '-') {
				if (sb.Length == 0 || sb[sb.Length - 1] != '-') sb.Append('-');
			}
			else return null;
		}
		return sb.ToString();
	}

	static string parsehangul(string s, bool lenient) {
		var i = 0;
		var outb = new StringBuilder();
		while (i < s.Length) {
			if (s[i] == '-') { i++; continue; }
			var onset = parseonset(s, i, lenient);
			if (onset == null) return null;
			var vowel = parsevowel(s, onset.Value.J);
			if (vowel == null) return null;
			var coda = choosecoda(s, vowel.Value.J, lenient);
			if (coda == null) return null;
			outb.Append(composehangul(onset.Value.Idx, vowel.Value.Idx, coda.Value.Idx));
			i = coda.Value.J;
		}
		var text = outb.ToString();
		if (text.Length == 0) return null;
		foreach (var c in text) if (c < '가' || c > '힣') return null;
		return text;
	}

	static char composehangul(int onset, int vowel, int coda) =>
		(char)(0xAC00 + onset * 21 * 28 + vowel * 28 + coda);

	static (int Idx, int J)? parseonset(string s, int i, bool lenient) {
		if (i >= s.Length || s[i] == '-') return null;
		var m = matchonsetcons(s, i, lenient);
		if (m != null) return (m.Value.Idx, i + m.Value.Len);
		if (parsevowel(s, i) != null) return (11, i);
		return null;
	}

	static bool canstartsyllable(string s, int i, bool lenient) {
		var j = i;
		while (j < s.Length && s[j] == '-') j++;
		if (j >= s.Length) return false;
		var onset = parseonset(s, j, lenient);
		if (onset == null) return false;
		return parsevowel(s, onset.Value.J) != null;
	}

	static bool startswithvowelroman(string s, int i) {
		var j = i;
		while (j < s.Length && s[j] == '-') j++;
		return j < s.Length && parsevowel(s, j) != null && matchonsetcons(s, j, true) == null;
	}

	static (int Idx, int J)? choosecoda(string s, int i, bool lenient) {
		int bestIdx = 0, bestJ = 0, bestN = -1;
		var bestVowelStart = true;
		var found = false;
		foreach (var (pat, idx) in codatable(lenient)) {
			if (pat.Length == 0) {
				if (i == s.Length || canstartsyllable(s, i, lenient)) {
					var vowelStart = i < s.Length && startswithvowelroman(s, i);
					if (!found || (!vowelStart && bestVowelStart) || (vowelStart == bestVowelStart && 0 > bestN)) {
						found = true;
						bestIdx = idx;
						bestJ = i;
						bestVowelStart = vowelStart;
						bestN = 0;
					}
				}
				continue;
			}
			if (i + pat.Length <= s.Length && s.Substring(i, pat.Length) == pat) {
				var j = i + pat.Length;
				if (j == s.Length || canstartsyllable(s, j, lenient)) {
					var vowelStart = j < s.Length && startswithvowelroman(s, j);
					if (!found || (!vowelStart && bestVowelStart) || (vowelStart == bestVowelStart && pat.Length > bestN)) {
						found = true;
						bestIdx = idx;
						bestJ = j;
						bestVowelStart = vowelStart;
						bestN = pat.Length;
					}
				}
			}
		}
		return found ? (bestIdx, bestJ) : null;
	}

	static (int Idx, int Len)? matchonsetcons(string s, int i, bool lenient) {
		var rest = s.Substring(i);
		foreach (var (pat, idx) in onsettable(lenient)) {
			if (rest.StartsWith(pat, StringComparison.Ordinal)) return (idx, pat.Length);
		}
		return null;
	}

	static (int Idx, int J)? parsevowel(string s, int i) {
		if (i >= s.Length) return null;
		var rest = s.Substring(i);
		foreach (var (pat, idx) in VOWEL) {
			if (rest.StartsWith(pat, StringComparison.Ordinal)) return (idx, i + pat.Length);
		}
		return null;
	}

	static (string Key, int Value)[] onsettable(bool lenient) => lenient ? ONSET_LENIENT : ONSET_STRICT;

	static (string Key, int Value)[] codatable(bool lenient) => lenient ? CODA_LENIENT : CODA_STRICT;

	static readonly (string Key, string Value)[] MORA = {
		("xtsu", "っ"),
		("ltsu", "っ"),
		("hwyu", "ふゅ"),
		("t'yu", "てゅ"),
		("d'yu", "でゅ"),
		("kya", "きゃ"),
		("kyi", "きぃ"),
		("kyu", "きゅ"),
		("kye", "きぇ"),
		("kyo", "きょ"),
		("gya", "ぎゃ"),
		("gyi", "ぎぃ"),
		("gyu", "ぎゅ"),
		("gye", "ぎぇ"),
		("gyo", "ぎょ"),
		("sha", "しゃ"),
		("shu", "しゅ"),
		("sho", "しょ"),
		("she", "しぇ"),
		("sya", "しゃ"),
		("syi", "しぃ"),
		("syu", "しゅ"),
		("syo", "しょ"),
		("sye", "しぇ"),
		("cha", "ちゃ"),
		("chu", "ちゅ"),
		("cho", "ちょ"),
		("che", "ちぇ"),
		("cya", "ちゃ"),
		("cyi", "ちぃ"),
		("cyu", "ちゅ"),
		("cye", "ちぇ"),
		("cyo", "ちょ"),
		("tya", "ちゃ"),
		("tyi", "ちぃ"),
		("tyu", "ちゅ"),
		("tyo", "ちょ"),
		("tye", "ちぇ"),
		("nya", "にゃ"),
		("nyi", "にぃ"),
		("nyu", "にゅ"),
		("nye", "にぇ"),
		("nyo", "にょ"),
		("hya", "ひゃ"),
		("hyi", "ひぃ"),
		("hyu", "ひゅ"),
		("hye", "ひぇ"),
		("hyo", "ひょ"),
		("bya", "びゃ"),
		("byi", "びぃ"),
		("byu", "びゅ"),
		("bye", "びぇ"),
		("byo", "びょ"),
		("pya", "ぴゃ"),
		("pyi", "ぴぃ"),
		("pyu", "ぴゅ"),
		("pye", "ぴぇ"),
		("pyo", "ぴょ"),
		("mya", "みゃ"),
		("myi", "みぃ"),
		("myu", "みゅ"),
		("mye", "みぇ"),
		("myo", "みょ"),
		("rya", "りゃ"),
		("ryi", "りぃ"),
		("ryu", "りゅ"),
		("rye", "りぇ"),
		("ryo", "りょ"),
		("jya", "じゃ"),
		("jyi", "じぃ"),
		("jyu", "じゅ"),
		("jye", "じぇ"),
		("jyo", "じょ"),
		("zya", "じゃ"),
		("zyi", "じぃ"),
		("zyu", "じゅ"),
		("zyo", "じょ"),
		("zye", "じぇ"),
		("dya", "ぢゃ"),
		("dyi", "ぢぃ"),
		("dyu", "ぢゅ"),
		("dye", "ぢぇ"),
		("dyo", "ぢょ"),
		("wha", "うぁ"),
		("whi", "うぃ"),
		("whu", "う"),
		("whe", "うぇ"),
		("who", "うぉ"),
		("tsa", "つぁ"),
		("tsi", "つぃ"),
		("tsu", "つ"),
		("tse", "つぇ"),
		("tso", "つぉ"),
		("tha", "てゃ"),
		("thi", "てぃ"),
		("thu", "てゅ"),
		("the", "てぇ"),
		("tho", "てょ"),
		("dha", "でゃ"),
		("dhi", "でぃ"),
		("dhu", "でゅ"),
		("dhe", "でぇ"),
		("dho", "でょ"),
		("twa", "とぁ"),
		("twi", "とぃ"),
		("twu", "とぅ"),
		("twe", "とぇ"),
		("two", "とぉ"),
		("dwa", "どぁ"),
		("dwi", "どぃ"),
		("dwu", "どぅ"),
		("dwe", "どぇ"),
		("dwo", "どぉ"),
		("kwa", "くぁ"),
		("kwi", "くぃ"),
		("kwu", "くぅ"),
		("kwe", "くぇ"),
		("kwo", "くぉ"),
		("gwa", "ぐぁ"),
		("gwi", "ぐぃ"),
		("gwu", "ぐぅ"),
		("gwe", "ぐぇ"),
		("gwo", "ぐぉ"),
		("hwa", "ふぁ"),
		("hwi", "ふぃ"),
		("hwe", "ふぇ"),
		("hwo", "ふぉ"),
		("dzi", "ぢ"),
		("dzu", "づ"),
		("chi", "ち"),
		("shi", "し"),
		("xtu", "っ"),
		("ltu", "っ"),
		("t'i", "てぃ"),
		("t'u", "とぅ"),
		("d'i", "でぃ"),
		("d'u", "どぅ"),
		("fya", "ふゃ"),
		("fyu", "ふゅ"),
		("fyo", "ふょ"),
		("vya", "ゔゃ"),
		("vyi", "ゔぃ"),
		("vyu", "ゔゅ"),
		("vye", "ゔぇ"),
		("vyo", "ゔょ"),
		("ka", "か"),
		("ki", "き"),
		("ku", "く"),
		("ke", "け"),
		("ko", "こ"),
		("ga", "が"),
		("gi", "ぎ"),
		("gu", "ぐ"),
		("ge", "げ"),
		("go", "ご"),
		("sa", "さ"),
		("si", "し"),
		("su", "す"),
		("se", "せ"),
		("so", "そ"),
		("za", "ざ"),
		("zi", "じ"),
		("zu", "ず"),
		("ze", "ぜ"),
		("zo", "ぞ"),
		("ta", "た"),
		("ti", "ち"),
		("tu", "つ"),
		("te", "て"),
		("to", "と"),
		("da", "だ"),
		("di", "ぢ"),
		("du", "づ"),
		("de", "で"),
		("do", "ど"),
		("na", "な"),
		("ni", "に"),
		("nu", "ぬ"),
		("ne", "ね"),
		("no", "の"),
		("ha", "は"),
		("hi", "ひ"),
		("hu", "ふ"),
		("he", "へ"),
		("ho", "ほ"),
		("ba", "ば"),
		("bi", "び"),
		("bu", "ぶ"),
		("be", "べ"),
		("bo", "ぼ"),
		("pa", "ぱ"),
		("pi", "ぴ"),
		("pu", "ぷ"),
		("pe", "ぺ"),
		("po", "ぽ"),
		("ma", "ま"),
		("mi", "み"),
		("mu", "む"),
		("me", "め"),
		("mo", "も"),
		("ya", "や"),
		("yu", "ゆ"),
		("yo", "よ"),
		("ra", "ら"),
		("ri", "り"),
		("ru", "る"),
		("re", "れ"),
		("ro", "ろ"),
		("wa", "わ"),
		("wi", "うぃ"),
		("we", "うぇ"),
		("wo", "を"),
		("fa", "ふぁ"),
		("fi", "ふぃ"),
		("fu", "ふ"),
		("fe", "ふぇ"),
		("fo", "ふぉ"),
		("va", "ゔぁ"),
		("vi", "ゔぃ"),
		("vu", "ゔ"),
		("ve", "ゔぇ"),
		("vo", "ゔぉ"),
		("ja", "じゃ"),
		("ji", "じ"),
		("ju", "じゅ"),
		("je", "じぇ"),
		("jo", "じょ"),
		("ca", "か"),
		("ci", "し"),
		("cu", "く"),
		("ce", "せ"),
		("co", "こ"),
		("qa", "くぁ"),
		("qi", "くぃ"),
		("qu", "く"),
		("qe", "くぇ"),
		("qo", "くぉ"),
		("wu", "う"),
		("xn", "ん"),
		("ye", "いぇ"),
		("a", "あ"),
		("i", "い"),
		("u", "う"),
		("e", "え"),
		("o", "お"),
};	static readonly (string Key, int Value)[] ONSET_LENIENT = {
		("kk", 1),
		("tt", 3),
		("pp", 8),
		("ss", 10),
		("jj", 13),
		("ch", 14),
		("kh", 15),
		("th", 16),
		("ph", 17),
		("g", 0),
		("k", 0),
		("n", 2),
		("d", 3),
		("t", 3),
		("r", 5),
		("l", 5),
		("m", 6),
		("b", 7),
		("p", 7),
		("s", 9),
		("j", 12),
		("h", 18),
};	static readonly (string Key, int Value)[] ONSET_STRICT = {
		("kk", 1),
		("tt", 3),
		("pp", 8),
		("ss", 10),
		("jj", 13),
		("ch", 14),
		("g", 0),
		("n", 2),
		("d", 3),
		("r", 5),
		("l", 5),
		("m", 6),
		("b", 7),
		("s", 9),
		("j", 12),
		("k", 15),
		("t", 16),
		("p", 17),
		("h", 18),
};	static readonly (string Key, int Value)[] VOWEL = {
		("yeo", 6),
		("yae", 3),
		("wae", 10),
		("ye", 7),
		("ya", 2),
		("yo", 12),
		("yu", 17),
		("wa", 9),
		("wo", 14),
		("we", 15),
		("wi", 16),
		("ui", 19),
		("oe", 11),
		("eo", 4),
		("eu", 18),
		("ae", 1),
		("oo", 13),
		("ee", 20),
		("uh", 4),
		("a", 0),
		("e", 5),
		("o", 8),
		("u", 13),
		("i", 20),
};	static readonly (string Key, int Value)[] CODA_LENIENT = {
		("ng", 21),
		("kk", 2),
		("ss", 20),
		("ks", 3),
		("gs", 3),
		("nj", 5),
		("nh", 6),
		("lk", 9),
		("lg", 9),
		("lm", 10),
		("lb", 11),
		("ls", 12),
		("lt", 13),
		("lp", 14),
		("lh", 15),
		("ps", 18),
		("bs", 18),
		("ch", 23),
		("k", 1),
		("g", 1),
		("n", 4),
		("t", 7),
		("d", 7),
		("l", 8),
		("r", 8),
		("m", 16),
		("p", 17),
		("b", 17),
		("s", 19),
		("j", 22),
		("h", 27),
		("", 0),
};	static readonly (string Key, int Value)[] CODA_STRICT = {
		("ng", 21),
		("kk", 2),
		("ss", 20),
		("ks", 3),
		("nj", 5),
		("nh", 6),
		("lk", 9),
		("lm", 10),
		("lb", 11),
		("ls", 12),
		("lt", 13),
		("lp", 14),
		("lh", 15),
		("ps", 18),
		("ch", 23),
		("k", 1),
		("g", 1),
		("n", 4),
		("t", 7),
		("l", 8),
		("r", 8),
		("m", 16),
		("p", 17),
		("b", 17),
		("s", 19),
		("j", 22),
		("h", 27),
		("", 0),
};}
