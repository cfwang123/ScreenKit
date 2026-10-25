using System.Collections.Generic;

namespace ScreenKit;

static class JaCand {
	static readonly string[] split = { "は", "を" };
	static readonly string[] parts = {
		"ながら", "けれど", "ので", "のに", "には", "では", "とは", "ても", "でも",
		"から", "まで", "より", "って", "けど",
		"は", "が", "を", "に", "の", "で", "と", "も", "へ"
	};
	static readonly HashSet<string> kanafirst = new HashSet<string> {
		"ながら", "けれど", "ので", "のに", "には", "では", "とは", "ても", "でも",
		"から", "まで", "より", "って", "けど",
		"は", "が", "を", "に", "の", "で", "と", "も", "へ",
		"する", "した", "して", "します", "です", "ます", "ました", "でした", "だ", "ない", "なく", "たい"
	};

	public static bool HasKanji(string s) {
		if (string.IsNullOrEmpty(s)) return false;
		foreach (var c in s) {
			if (c == '々' || c == '〆') return true;
			if (c >= 0x3400 && c <= 0x9FFF) return true;
			if (c >= 0xF900 && c <= 0xFAFF) return true;
		}
		return false;
	}

	public static string Kata(string hira) {
		if (string.IsNullOrEmpty(hira)) return "";
		var sb = new System.Text.StringBuilder(hira.Length);
		foreach (var c in hira) {
			if (c >= 0x3041 && c <= 0x3096) sb.Append((char)(c + 0x60));
			else sb.Append(c);
		}
		return sb.ToString();
	}

	public static List<Cand> Build(string reading, List<Cand> rows) {
		var list = new List<Cand>();
		if (string.IsNullOrEmpty(reading)) return list;
		var groups = new Dictionary<string, List<Cand>>();
		if (rows != null) {
			foreach (var r in rows) {
				if (r == null || string.IsNullOrEmpty(r.Code) || string.IsNullOrEmpty(r.Raw)) continue;
				if (!reading.StartsWith(r.Code)) continue;
				List<Cand> g;
				if (!groups.TryGetValue(r.Code, out g)) {
					g = new List<Cand>();
					groups[r.Code] = g;
				}
				g.Add(r);
			}
		}
		foreach (var g in groups.Values) {
			g.Sort(cmp);
			if (g.Count > 48) g.RemoveRange(48, g.Count - 48);
		}
		var stem = pick(reading, groups);
		var rest = stem.Length < reading.Length ? reading.Substring(stem.Length) : "";
		var primary = new List<Cand>();
		List<Cand> grown;
		if (groups.TryGetValue(stem, out grown)) {
			foreach (var r in grown) add(primary, r, r.Raw + rest, r.Rank);
		}
		if (stem != reading) {
			List<Cand> full;
			if (groups.TryGetValue(reading, out full)) {
				foreach (var r in full) add(primary, r, r.Raw, r.Pinned ? r.Rank : r.Rank + 100000);
			}
		}
		primary.Sort(cmp);
		if (primary.Count > 48) primary.RemoveRange(48, primary.Count - 48);
		var seen = new HashSet<string>();
		foreach (var c in primary) seen.Add(c.Text);
		bool lead = !pinned(primary) && (reading.Length <= 1 || kanafirst.Contains(reading));
		var hira = reading;
		var kata = Kata(hira);
		if (lead) {
			var head = new List<Cand>();
			push(head, seen, hira, hira);
			if (kata != hira) push(head, seen, kata, hira);
			head.AddRange(primary);
			return head;
		}
		if (kata != hira) push(primary, seen, kata, hira);
		push(primary, seen, hira, hira);
		return primary;
	}

	static string pick(string reading, Dictionary<string, List<Cand>> groups) {
		foreach (var p in split) {
			if (p.Length >= reading.Length || !reading.EndsWith(p)) continue;
			var stem = reading.Substring(0, reading.Length - p.Length);
			if (groups.ContainsKey(stem)) return stem;
		}
		if (groups.ContainsKey(reading)) return reading;
		string best = null;
		int bestTier = 0, bestRank = 0, bestLen = 0;
		foreach (var kv in groups) {
			if (kv.Key.Length >= reading.Length) continue;
			var rest = reading.Substring(kv.Key.Length);
			int tier = head(rest) != null ? 1 : 2;
			int rank = minrank(kv.Value);
			int len = kv.Key.Length;
			bool win = best == null || tier < bestTier || (tier == bestTier && rank < bestRank) ||
				(tier == bestTier && rank == bestRank && len > bestLen);
			if (!win) continue;
			best = kv.Key;
			bestTier = tier;
			bestRank = rank;
			bestLen = len;
		}
		return best ?? reading;
	}

	static int minrank(List<Cand> list) {
		int rank = int.MaxValue;
		foreach (var r in list)
			if (r.Rank < rank) rank = r.Rank;
		return rank;
	}

	static string head(string rest) {
		foreach (var p in parts)
			if (rest.StartsWith(p)) return p;
		return null;
	}

	static void add(List<Cand> list, Cand src, string text, int rank) {
		if (string.IsNullOrEmpty(text)) return;
		foreach (var old in list) {
			if (old.Text != text) continue;
			if (src.Pinned) old.Pinned = true;
			if (src.Picks > old.Picks) old.Picks = src.Picks;
			if (rank < old.Rank) old.Rank = rank;
			if (src.Pinned && rank < old.Rank) old.Rank = rank;
			return;
		}
		list.Add(new Cand {
			Text = text, Raw = src.Raw, Code = src.Code,
			Pinned = src.Pinned, Picks = src.Picks, Rank = rank
		});
	}

	static void push(List<Cand> list, HashSet<string> seen, string text, string reading) {
		if (string.IsNullOrEmpty(text) || !seen.Add(text)) return;
		list.Add(new Cand { Text = text, Raw = text, Code = reading, Rank = int.MaxValue });
	}

	static bool pinned(List<Cand> list) {
		foreach (var c in list)
			if (c.Pinned) return true;
		return false;
	}

	static int cmp(Cand a, Cand b) {
		int c = b.Pinned.CompareTo(a.Pinned);
		if (c != 0) return c;
		c = b.Picks.CompareTo(a.Picks);
		if (c != 0) return c;
		c = a.Rank.CompareTo(b.Rank);
		if (c != 0) return c;
		return string.CompareOrdinal(a.Text, b.Text);
	}
}
