using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ScreenKit;

/// <summary>导出一条待处理的词：类、flag/sub、辞书形。</summary>
sealed class JpConjExportWord {
	public string Kind = "";
	public string Flag = "";
	public string Sub = "";
	public string Word = "";
}

/// <summary>把日语变形表按类型导出为 JSON。只处理页面下拉里的 1/2/3/ai/an/n。</summary>
static class JpConjExport {
	/// <summary>每个类型选几个代表词；动词覆盖五段音便和三类不规则。</summary>
	static readonly string[] DEFAULTS = {
		"1:書く", "1:話す", "1:待つ", "1:飲む", "1:泳ぐ", "1:買う", "1:行く",
		"2:食べる", "2:見る",
		"3:する", "3:来る", "3:勉強する",
		"ai:高い", "ai:かわいい", "ai:いい",
		"an:静か", "an:便利", "an:好き",
		"n:学生", "n:本",
	};

	static readonly Dictionary<string, string> NAMES = new() {
		["1"] = "1类（五段）",
		["2"] = "2类（一段）",
		["3"] = "3类（する・くる）",
		["ai"] = "い形容词",
		["an"] = "な形容词",
		["n"] = "名词",
	};

	/// <summary>导出。path 为空时写到当前目录 conj-export.json；words 为空时用内置代表词。</summary>
	public static int Run(string path, IReadOnlyList<string> words, Action<string> log) {
		if (string.IsNullOrWhiteSpace(path)) path = "conj-export.json";
		var specs = words == null || words.Count == 0 ? DEFAULTS : words;
		var items = new List<(JpConjExportWord W, ConjResult R)>();
		var bad = 0;
		foreach (var spec in specs) {
			var w = parse(spec);
			if (w == null) {
				log("skip: " + spec);
				bad++;
				continue;
			}
			var r = JpConj.Build(w.Word, w.Kind, w.Flag, w.Sub, "zh");
			if (r == null || !r.Ok) {
				log($"fail: {w.Word} {r?.Err ?? ""}".TrimEnd());
				bad++;
			}
			items.Add((w, r));
		}
		var doc = new {
			schema = "screenkit.conj.v1",
			lang = "ja",
			exported_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
			types = items.GroupBy(x => x.W.Kind).Select(g => new {
				kind = g.Key,
				name = kindname(g.Key),
				words = g.Select(x => new {
					head = x.R?.Head ?? x.W.Word,
					name = x.R?.Name ?? "",
					ok = x.R != null && x.R.Ok,
					err = x.R?.Err ?? "",
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
		log($"types={items.Select(x => x.W.Kind).Distinct().Count()} words={items.Count} forms={nrows}");
		return bad == 0 ? 0 : 1;
	}

	/// <summary>支持 kind:词；不写 kind 时按词尾猜。な形容词/名词建议显式写 an:/n:。</summary>
	static JpConjExportWord parse(string spec) {
		var s = (spec ?? "").Trim();
		if (s.Length == 0) return null;
		var kind = "";
		var word = s;
		var c = s.IndexOf(':');
		if (c > 0) {
			var k = s.Substring(0, c);
			if (NAMES.ContainsKey(k)) {
				kind = k;
				word = s.Substring(c + 1).Trim();
			}
		}
		if (word.Length == 0) return null;
		var g = JpConj.Guess(word);
		if (kind.Length == 0) {
			if (g == null || string.IsNullOrEmpty(g.Kind)) return null;
			return new JpConjExportWord { Kind = g.Kind, Flag = g.Flag ?? "", Sub = g.Sub ?? "", Word = word };
		}
		return new JpConjExportWord {
			Kind = kind,
			Flag = g != null && g.Kind == kind ? g.Flag ?? "" : "",
			Sub = g != null && g.Kind == kind ? g.Sub ?? "" : "",
			Word = word,
		};
	}

	static string kindname(string kind) => NAMES.TryGetValue(kind ?? "", out var name) ? name : kind ?? "";
}
