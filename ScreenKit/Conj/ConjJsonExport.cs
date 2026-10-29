using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ScreenKit;

/// <summary>导出一条待处理的词：语言、类型、flag/sub、原形。</summary>
sealed class ConjJsonWord {
	public string Lang = "";
	public string Kind = "";
	public string Flag = "";
	public string Sub = "";
	public string Word = "";
}

/// <summary>把日/韩/英变形表按类型导出为 JSON。俄语没有 C# 实现，另走 web 端脚本。</summary>
static class ConjJsonExport {
	static readonly Dictionary<string, string[]> DEFAULTS = new() {
		["ja"] = new[] {
			"1:書く", "1:話す", "1:待つ", "1:飲む", "1:泳ぐ", "1:買う", "1:行く",
			"2:食べる", "2:見る",
			"3:する", "3:来る", "3:勉強する",
			"ai:高い", "ai:かわいい", "ai:いい",
			"an:静か", "an:便利", "an:好き",
			"n:学生", "n:本",
		},
		["ko"] = new[] {
			"v:먹다", "v:가다", "v:하다", "v:보다", "v:살다", "v:듣다",
			"a:크다", "a:예쁘다", "a:좋다", "a:많다",
			"n:학생", "n:책",
		},
		["en"] = new[] {
			"v:write", "v:go", "v:eat", "v:be", "v:do",
			"a:big", "a:good", "a:beautiful",
			"n:book", "n:child",
			"pron:I", "pron:they",
		},
	};

	static readonly Dictionary<string, Dictionary<string, string>> NAMES = new() {
		["ja"] = new() {
			["1"] = "1类（五段）",
			["2"] = "2类（一段）",
			["3"] = "3类（する・くる）",
			["ai"] = "い形容词",
			["an"] = "な形容词",
			["n"] = "名词",
		},
		["ko"] = new() {
			["v"] = "动词",
			["a"] = "形容词",
			["n"] = "名词",
		},
		["en"] = new() {
			["v"] = "动词",
			["a"] = "形容词",
			["n"] = "名词",
			["pron"] = "代词",
		},
	};

	/// <summary>导出。path 为空时写到当前目录 conj-export.json；words 为空时用该语言的内置代表词。</summary>
	public static int Run(string lang, string path, IReadOnlyList<string> words, Action<string> log) {
		lang = (lang ?? "ja").Trim().ToLowerInvariant();
		if (lang == "ru") {
			log("ru 由 web 端 u-rverb.js 计算，请用 node scripts/export-ru-conj.js 导出");
			return 2;
		}
		if (!DEFAULTS.ContainsKey(lang)) {
			log("unsupported lang: " + lang);
			return 2;
		}
		if (string.IsNullOrWhiteSpace(path)) path = "conj-export.json";
		var specs = words == null || words.Count == 0 ? DEFAULTS[lang] : words;
		var items = new List<(ConjJsonWord W, ConjResult R)>();
		var bad = 0;
		foreach (var spec in specs) {
			var w = parse(lang, spec);
			if (w == null) {
				log("skip: " + spec);
				bad++;
				continue;
			}
			var r = w.Lang == "ja" ? JpConj.Build(w.Word, w.Kind, w.Flag, w.Sub, "zh")
				: w.Lang == "ko" ? KoConj.Build(w.Word, w.Kind, "zh")
				: EnConj.Build(w.Word, w.Kind, "zh");
			if (r == null || !r.Ok) {
				log($"fail: {w.Word} {r?.Err ?? ""}".TrimEnd());
				bad++;
			}
			items.Add((w, r));
		}
		var doc = new {
			schema = "screenkit.conj.v1",
			lang,
			exported_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
			types = items.GroupBy(x => x.W.Kind).Select(g => new {
				kind = g.Key,
				name = kindname(lang, g.Key),
				words = g.Select(x => new {
					head = x.R?.Head ?? x.W.Word,
					name = lang == "ja" ? (x.R?.Name ?? kindname(lang, x.W.Kind)) : kindname(lang, x.W.Kind),
					detail = x.R?.Name ?? "",
					ok = x.R != null && x.R.Ok,
					err = x.R?.Err ?? "",
					note = x.R?.Note ?? "",
					forms = (x.R?.Rows ?? new List<ConjRow>()).Select(row => new {
						group = string.IsNullOrEmpty(row.G) ? "基础" : row.G,
						label = row.L ?? "",
						value = row.V ?? "",
					}).ToArray(),
				}).ToArray(),
			}).ToArray(),
		};
		var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions {
			WriteIndented = true,
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		});
		var full = Path.GetFullPath(path);
		var dir = Path.GetDirectoryName(full);
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		File.WriteAllText(full, json + "\n", new UTF8Encoding(false));
		var nrows = items.Sum(x => x.R?.Rows?.Count ?? 0);
		log($"wrote {full}");
		log($"lang={lang} types={items.Select(x => x.W.Kind).Distinct().Count()} words={items.Count} forms={nrows}");
		return bad == 0 ? 0 : 1;
	}

	/// <summary>支持 kind:词；不写 kind 时按语言自动判断。日语用 Guess，韩语用 KoConj 自动结果。</summary>
	static ConjJsonWord parse(string lang, string spec) {
		var s = (spec ?? "").Trim();
		if (s.Length == 0) return null;
		var kind = "";
		var word = s;
		var c = s.IndexOf(':');
		if (c > 0) {
			var k = s.Substring(0, c);
			if (NAMES[lang].ContainsKey(k)) {
				kind = k;
				word = s.Substring(c + 1).Trim();
			}
		}
		if (word.Length == 0) return null;
		var w = new ConjJsonWord { Lang = lang, Kind = kind, Word = word };
		if (kind.Length == 0) {
			if (lang == "ja") {
				var g = JpConj.Guess(word);
				if (g == null || string.IsNullOrEmpty(g.Kind)) return null;
				w.Kind = g.Kind;
				w.Flag = g.Flag ?? "";
				w.Sub = g.Sub ?? "";
			}
			else if (lang == "ko") {
				var r = KoConj.Build(word, "auto", "zh");
				if (r == null || !r.Ok) return null;
				w.Kind = kokind(r.Name);
			}
			else {
				var r = EnConj.Build(word, "auto", "zh");
				if (r == null || !r.Ok) return null;
				w.Kind = string.IsNullOrEmpty(r.Kind) ? "v" : r.Kind;
			}
		}
		if (!NAMES[lang].ContainsKey(w.Kind)) return null;
		if (lang == "ja" && w.Kind == "1" && word == "行く") w.Flag = "iku";
		if (lang == "ja" && w.Kind == "3") {
			var g = JpConj.Guess(word);
			w.Sub = g?.Sub ?? "";
		}
		return w;
	}

	static string kokind(string name) => name switch {
		"v" => "v",
		"a" => "a",
		"n" => "n",
		"ida" => "n",
		"anida" => "n",
		"itda" => "a",
		_ => "n",
	};

	static string kindname(string lang, string kind) =>
		NAMES[lang].TryGetValue(kind ?? "", out var name) ? name : kind ?? "";
}
