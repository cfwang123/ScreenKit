using System.Collections.Generic;

namespace ScreenKit;

class Kana {
	static readonly Dictionary<string, string> map = build();
	static readonly HashSet<string> prefix = buildprefix();
	readonly List<string> chunks = new List<string>();
	string roma = "";

	public string Text => string.Concat(chunks) + roma;
	public string KanaText => string.Concat(chunks);
	public bool HasKana => chunks.Count > 0;

	public void Clear() {
		chunks.Clear();
		roma = "";
	}

	public void Back() {
		if (roma.Length > 0) {
			roma = roma.Substring(0, roma.Length - 1);
			return;
		}
		if (chunks.Count > 0) chunks.RemoveAt(chunks.Count - 1);
	}

	public void Push(char c) {
		if (c < 'a' || c > 'z') return;
		roma += c;
		pump();
	}

	public void Choon() {
		chunks.Add("ー");
		roma = "";
	}

	public void FinishN() {
		if (roma == "n") {
			chunks.Add("ん");
			roma = "";
		}
	}

	void pump() {
		while (roma.Length > 0) {
			if (map.ContainsKey(roma) && !longer(roma)) {
				chunks.Add(map[roma]);
				roma = "";
				return;
			}
			if (prefix.Contains(roma)) return;
			if (roma.Length >= 2 && roma[0] == roma[1] && cons(roma[0]) && roma[0] != 'n') {
				chunks.Add("っ");
				roma = roma.Substring(1);
				continue;
			}
			if (roma[0] == 'n' && roma.Length >= 2 && roma[1] != 'y' && !vowel(roma[1]) && roma[1] != 'n') {
				chunks.Add("ん");
				roma = roma.Substring(1);
				continue;
			}
			roma = roma.Substring(1);
		}
	}

	static bool longer(string s) {
		foreach (var k in map.Keys)
			if (k.Length > s.Length && k.StartsWith(s)) return true;
		return false;
	}

	static bool cons(char c) => "bcdfghjklmnpqrstvwxyz".IndexOf(c) >= 0;
	static bool vowel(char c) => "aeiou".IndexOf(c) >= 0;

	static Dictionary<string, string> build() {
		var d = new Dictionary<string, string>();
		add(d, "a", "あ"); add(d, "i", "い"); add(d, "u", "う"); add(d, "e", "え"); add(d, "o", "お");
		add(d, "ka", "か"); add(d, "ki", "き"); add(d, "ku", "く"); add(d, "ke", "け"); add(d, "ko", "こ");
		add(d, "ga", "が"); add(d, "gi", "ぎ"); add(d, "gu", "ぐ"); add(d, "ge", "げ"); add(d, "go", "ご");
		add(d, "sa", "さ"); add(d, "si", "し"); add(d, "su", "す"); add(d, "se", "せ"); add(d, "so", "そ");
		add(d, "shi", "し"); add(d, "sha", "しゃ"); add(d, "shu", "しゅ"); add(d, "sho", "しょ");
		add(d, "za", "ざ"); add(d, "zi", "じ"); add(d, "zu", "ず"); add(d, "ze", "ぜ"); add(d, "zo", "ぞ");
		add(d, "ji", "じ"); add(d, "ja", "じゃ"); add(d, "ju", "じゅ"); add(d, "jo", "じょ");
		add(d, "ta", "た"); add(d, "ti", "ち"); add(d, "tu", "つ"); add(d, "te", "て"); add(d, "to", "と");
		add(d, "chi", "ち"); add(d, "tsu", "つ"); add(d, "cha", "ちゃ"); add(d, "chu", "ちゅ"); add(d, "cho", "ちょ");
		add(d, "da", "だ"); add(d, "di", "ぢ"); add(d, "du", "づ"); add(d, "de", "で"); add(d, "do", "ど");
		add(d, "na", "な"); add(d, "ni", "に"); add(d, "nu", "ぬ"); add(d, "ne", "ね"); add(d, "no", "の");
		add(d, "ha", "は"); add(d, "hi", "ひ"); add(d, "hu", "ふ"); add(d, "he", "へ"); add(d, "ho", "ほ");
		add(d, "fu", "ふ");
		add(d, "ba", "ば"); add(d, "bi", "び"); add(d, "bu", "ぶ"); add(d, "be", "べ"); add(d, "bo", "ぼ");
		add(d, "pa", "ぱ"); add(d, "pi", "ぴ"); add(d, "pu", "ぷ"); add(d, "pe", "ぺ"); add(d, "po", "ぽ");
		add(d, "ma", "ま"); add(d, "mi", "み"); add(d, "mu", "む"); add(d, "me", "め"); add(d, "mo", "も");
		add(d, "ya", "や"); add(d, "yu", "ゆ"); add(d, "yo", "よ");
		add(d, "ra", "ら"); add(d, "ri", "り"); add(d, "ru", "る"); add(d, "re", "れ"); add(d, "ro", "ろ");
		add(d, "wa", "わ"); add(d, "wo", "を"); add(d, "nn", "ん");
		add(d, "kya", "きゃ"); add(d, "kyu", "きゅ"); add(d, "kyo", "きょ");
		add(d, "gya", "ぎゃ"); add(d, "gyu", "ぎゅ"); add(d, "gyo", "ぎょ");
		add(d, "nya", "にゃ"); add(d, "nyu", "にゅ"); add(d, "nyo", "にょ");
		add(d, "hya", "ひゃ"); add(d, "hyu", "ひゅ"); add(d, "hyo", "ひょ");
		add(d, "bya", "びゃ"); add(d, "byu", "びゅ"); add(d, "byo", "びょ");
		add(d, "pya", "ぴゃ"); add(d, "pyu", "ぴゅ"); add(d, "pyo", "ぴょ");
		add(d, "mya", "みゃ"); add(d, "myu", "みゅ"); add(d, "myo", "みょ");
		add(d, "rya", "りゃ"); add(d, "ryu", "りゅ"); add(d, "ryo", "りょ");
		return d;
	}

	static void add(Dictionary<string, string> d, string k, string v) { d[k] = v; }

	static HashSet<string> buildprefix() {
		var set = new HashSet<string>();
		foreach (var k in map.Keys) {
			for (int i = 1; i < k.Length; i++) set.Add(k.Substring(0, i));
		}
		return set;
	}
}
