using System;
using System.Collections.Generic;
using System.Linq;

namespace ScreenKit;

// ==================== 韩语活用形还原 ====================
// 索引里是词头（가다）和释义键，活用形（갑니다）往往没有键。
// 按常见语尾剥回可能的原形，再交给词典核实；剥错的候选查不到就丢掉。
// 只有 -ㅂ니다 / -ㅂ니까 / -ㅂ시다 这类「辅音并进前一音节」的语尾，剥完才去掉末字收音
// （갑니다 = 가 + ㅂ니다 → 가다）。-습니다 / -었습니다 是完整音节，词干原样保留。
#region
static class KoStem {
	const int MAXCAND = 16;
	const int MAXDEPTH = 3;

	static readonly string[] RawSuffixes = {
		"시겠습니다", "셨습니다", "었습니다", "았습니다", "였습니다", "겠습니다",
		"습니다", "습니까", "십시오", "셨어요", "었어요", "았어요", "였어요", "겠어요",
		"었다", "았다", "였다", "겠다", "세요",
		"어요", "아요", "여요", "에요", "예요", "지만", "는데", "은데", "으니까",
		"으려고", "려고", "으러", "러", "면서", "자", "지", "고", "서", "면", "니", "요",
		"에서", "에게", "한테", "으로", "하고", "이나", "부터", "까지",
		"은", "는", "을", "를", "이", "가", "에", "께", "도", "만", "과", "와", "나", "의", "로",
		"어", "아",
	};

	static readonly string[] DropSuffixes = { "니다", "니까", "시다" };

	static readonly string[] Suffixes = RawSuffixes.OrderByDescending(s => s.Length).ThenBy(s => s, StringComparer.Ordinal).ToArray();
	static readonly string[] Drops = DropSuffixes.OrderByDescending(s => s.Length).ThenBy(s => s, StringComparer.Ordinal).ToArray();

	/// <summary>候选原形。第一项是原词。去重且保持顺序，越靠前越接近原形。</summary>
	public static List<string> Candidates(string word) {
		var outlist = new List<string>();
		if (string.IsNullOrWhiteSpace(word)) return outlist;
		var w = word.Trim();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		add(outlist, seen, w);
		expand(w, outlist, seen, 0);
		return outlist;
	}

	static void expand(string w, List<string> outlist, HashSet<string> seen, int depth) {
		if (depth >= MAXDEPTH || w.Length < 2 || outlist.Count >= MAXCAND) return;
		foreach (var suf in Suffixes) {
			if (w.Length <= suf.Length || !w.EndsWith(suf, StringComparison.Ordinal)) continue;
			var stem = w[..^suf.Length];
			if (stem.Length == 0) continue;
			addstem(stem, false, outlist, seen, depth);
			expand(stem, outlist, seen, depth + 1);
			if (outlist.Count >= MAXCAND) return;
		}
		foreach (var suf in Drops) {
			if (w.Length <= suf.Length || !w.EndsWith(suf, StringComparison.Ordinal)) continue;
			var stem = w[..^suf.Length];
			if (stem.Length == 0) continue;
			addstem(stem, true, outlist, seen, depth);
			expand(stem, outlist, seen, depth + 1);
			if (outlist.Count >= MAXCAND) return;
		}
	}

	static void addstem(string stem, bool drop, List<string> outlist, HashSet<string> seen, int depth) {
		if (drop) {
			var bare = DropFinal(stem);
			if (bare != stem) {
				add(outlist, seen, bare + "다");
				add(outlist, seen, bare);
				expand(bare, outlist, seen, depth + 1);
			}
		}
		add(outlist, seen, stem + "다");
		add(outlist, seen, stem);
	}

	static void add(List<string> outlist, HashSet<string> seen, string s) {
		if (string.IsNullOrEmpty(s) || outlist.Count >= MAXCAND) return;
		if (seen.Add(s)) outlist.Add(s);
	}

	/// <summary>去掉最后一个音节的收音（갑 → 가）。</summary>
	public static string DropFinal(string s) {
		if (string.IsNullOrEmpty(s)) return s;
		var last = s[^1];
		var bare = dropfinalchar(last);
		if (bare == last) return s;
		return s[..^1] + bare;
	}

	static char dropfinalchar(char c) {
		if (c < '\uAC00' || c > '\uD7A3') return c;
		var i = c - '\uAC00';
		if (i % 28 == 0) return c;
		return (char)('\uAC00' + i / 28 * 28);
	}

	public static bool IsHangul(char c) => c >= '\uAC00' && c <= '\uD7A3';

	public static bool HasHangul(string s) {
		if (string.IsNullOrEmpty(s)) return false;
		foreach (var c in s)
			if (IsHangul(c)) return true;
		return false;
	}
}
#endregion
