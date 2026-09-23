using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenKit;

sealed class WordLexRow {
	public string Code { get; set; } = "";
	public string Lang { get; set; } = "";
	public string Native { get; set; } = "";
	public string Latin { get; set; } = "";
}

/// <summary>密码生成器：单词 → 多语译文 + 拉丁转写（LLM）。</summary>
static class WordLex {
	public static readonly string[] Codes = {
		"zh", "ja", "ko", "en",
		"es", "fr", "de", "ru", "it", "pt",
		"vi", "th", "ar", "hi", "id", "nl", "tr", "pl",
	};

	const string SysPrompt =
		"Translate one word or short phrase into many languages for password seeds. " +
		"Reply with ONLY a JSON array, no markdown, no commentary. " +
		"Each element: {\"lang\":\"code\",\"native\":\"in that script\",\"latin\":\"ASCII romanization\"}. " +
		"lang codes in this order: zh,ja,ko,en,es,fr,de,ru,it,pt,vi,th,ar,hi,id,nl,tr,pl. " +
		"zh latin = Hanyu Pinyin without tones (e.g. mi ma). " +
		"ja latin = Hepburn romaji (e.g. pasuwaado). " +
		"ko latin = Revised Romanization ASCII (e.g. bimilbeonho). " +
		"ru/ar/hi/th native = original script; latin = ASCII. " +
		"en native and latin may be the same English word. " +
		"latin: ASCII letters, optional spaces or hyphens, no diacritics. " +
		"Keep translations short (one word or a short compound).";

	public static List<WordLexRow> Translate(OcrOptions o, string word, LlmEndpoint ep = null,
		CancellationToken ct = default) {
		word = (word ?? "").Trim();
		if (word.Length == 0)
			throw new InvalidOperationException("empty");
		ep ??= o?.SelectedPwLexLlm();
		if (!AsrLlmClient.IsEndpointReady(ep))
			throw new InvalidOperationException(Loc.T("pwgen.lex.nollm"));
		object messages = new object[] {
			new { role = "system", content = SysPrompt },
			new { role = "user", content = word },
		};
		LlmLog.Info($"wordlex model={ep.Model} wordLen={word.Length}");
		var raw = AsrLlmClient.ChatOnce(ep, messages, 90_000, 0.2f, ct);
		if (string.IsNullOrWhiteSpace(raw))
			throw new InvalidOperationException(Loc.T("pwgen.lex.empty"));
		var rows = Parse(raw);
		if (rows.Count == 0)
			throw new InvalidOperationException(Loc.T("pwgen.lex.empty"));
		return rows;
	}

	public static List<WordLexRow> Parse(string text) {
		var list = new List<WordLexRow>();
		var json = extractjson(text ?? "");
		if (json.Length == 0) return list;
		JsonDocument doc;
		try { doc = JsonDocument.Parse(json); }
		catch { return list; }
		using (doc) {
			var root = doc.RootElement;
			if (root.ValueKind == JsonValueKind.Object) {
				foreach (var name in new[] { "items", "rows", "data", "translations" }) {
					if (root.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array) {
						root = arr;
						break;
					}
				}
			}
			if (root.ValueKind != JsonValueKind.Array) return list;
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var el in root.EnumerateArray()) {
				if (el.ValueKind != JsonValueKind.Object) continue;
				var code = str(el, "lang", "code", "l").Trim().ToLowerInvariant();
				if (code == "jp") code = "ja";
				if (code == "kr") code = "ko";
				if (code == "cn" || code == "zh-cn") code = "zh";
				var native = str(el, "native", "text", "word", "script");
				var latin = str(el, "latin", "romaji", "pinyin", "roman", "ascii");
				if (native.Length == 0 && latin.Length == 0) continue;
				if (code.Length == 0) code = "x";
				if (!seen.Add(code)) continue;
				list.Add(new WordLexRow {
					Code = code,
					Lang = Loc.LangName(code),
					Native = native,
					Latin = latin,
				});
			}
		}
		list.Sort((a, b) => rank(a.Code).CompareTo(rank(b.Code)));
		return list;
	}

	static int rank(string code) {
		for (var i = 0; i < Codes.Length; i++) {
			if (string.Equals(Codes[i], code, StringComparison.OrdinalIgnoreCase))
				return i;
		}
		return 1000;
	}

	static string str(JsonElement el, params string[] names) {
		foreach (var n in names) {
			if (!el.TryGetProperty(n, out var v)) continue;
			if (v.ValueKind == JsonValueKind.String) return (v.GetString() ?? "").Trim();
			if (v.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
				return v.ToString().Trim();
		}
		return "";
	}

	static string extractjson(string text) {
		text = (text ?? "").Trim();
		if (text.Length == 0) return "";
		var fence = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)```", RegexOptions.IgnoreCase);
		if (fence.Success) text = fence.Groups[1].Value.Trim();
		var i = text.IndexOf('[');
		var j = text.LastIndexOf(']');
		if (i >= 0 && j > i) return text.Substring(i, j - i + 1);
		i = text.IndexOf('{');
		j = text.LastIndexOf('}');
		if (i >= 0 && j > i) return text.Substring(i, j - i + 1);
		return text;
	}
}
