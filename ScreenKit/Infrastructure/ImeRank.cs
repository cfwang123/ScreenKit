using System;
using System.Collections.Generic;

namespace ScreenKit;

static class WubiCode {
	public static string Phrase(string word, Func<char, string> full) {
		if (string.IsNullOrEmpty(word)) return "";
		var codes = new string[word.Length];
		for (int i = 0; i < word.Length; i++) {
			codes[i] = full(word[i]) ?? "";
			if (codes[i].Length == 0) return "";
		}
		int n = word.Length;
		if (n == 1) return codes[0];
		if (n == 2) return take(codes[0], 2) + take(codes[1], 2);
		if (n == 3) return take(codes[0], 1) + take(codes[1], 1) + take(codes[2], 2);
		if (n == 4) return take(codes[0], 1) + take(codes[1], 1) + take(codes[2], 1) + take(codes[3], 1);
		return take(codes[0], 1) + take(codes[1], 1) + take(codes[2], 1) + take(codes[n - 1], 1);
	}

	static string take(string s, int n) => s.Length <= n ? s : s.Substring(0, n);
}

static class Rank {
	public static List<Cand> Finish(List<Cand> raw, string prefix, bool trad, Func<char, IList<string>> alts) {
		raw.Sort((a, b) => cmp(a, b, prefix));
		if (raw.Count > 200) raw = raw.GetRange(0, 200);
		var list = new List<Cand>();
		foreach (var c in raw) {
			c.Extra = c.Code != null && c.Code.StartsWith(prefix) ? c.Code.Substring(prefix.Length) : "";
			if (!trad) {
				c.Text = c.Raw;
				list.Add(c);
				continue;
			}
			foreach (var v in Trad.Expand(c.Raw, alts)) {
				list.Add(new Cand {
					Text = v, Raw = c.Raw, Code = c.Code, Extra = c.Extra,
					Pinned = c.Pinned, Picks = c.Picks, Rank = c.Rank
				});
				if (list.Count >= 200) return list;
			}
		}
		return list;
	}

	static int cmp(Cand a, Cand b, string prefix) {
		int c = b.Pinned.CompareTo(a.Pinned);
		if (c != 0) return c;
		int n = prefix == null ? 0 : prefix.Length;
		int ta = band(a, n);
		int tb = band(b, n);
		if (ta != tb) return ta < tb ? -1 : 1;
		int ra = remain(a, n);
		int rb = remain(b, n);
		if (ra != rb) return ra < rb ? -1 : 1;
		if (ta == 0 || ta == 2) {
			c = b.Picks.CompareTo(a.Picks);
			if (c != 0) return c;
		}
		c = a.Rank.CompareTo(b.Rank);
		if (c != 0) return c;
		c = a.Code.Length.CompareTo(b.Code.Length);
		if (c != 0) return c;
		return string.Compare(a.Raw, b.Raw, StringComparison.Ordinal);
	}

	static int band(Cand c, int n) {
		bool exact = c.Code != null && c.Code.Length == n;
		bool user = c.Picks > 0;
		if (exact && user) return 0;
		if (exact) return 1;
		if (user) return 2;
		return 3;
	}

	static int remain(Cand c, int n) {
		if (c.Code == null) return 99;
		int r = c.Code.Length - n;
		return r < 0 ? 99 : r;
	}
}

static class Trad {
	public static List<string> Expand(string text, Func<char, IList<string>> alts) {
		var res = new List<string>();
		if (string.IsNullOrEmpty(text)) return res;
		walk(text, 0, "", alts, res);
		if (res.Count == 0) res.Add(text);
		return res;
	}

	static void walk(string text, int i, string acc, Func<char, IList<string>> alts, List<string> res) {
		if (res.Count >= 8) return;
		if (i >= text.Length) {
			res.Add(acc);
			return;
		}
		var opts = alts(text[i]);
		if (opts == null || opts.Count == 0) {
			walk(text, i + 1, acc + text[i], alts, res);
			return;
		}
		foreach (var s in opts) {
			walk(text, i + 1, acc + s, alts, res);
			if (res.Count >= 8) return;
		}
	}
}
