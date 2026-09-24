using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenKit;

sealed class PasswordVariantRow {
	public string Password { get; set; } = "";
	public string Note { get; set; } = "";
}

/// <summary>密码生成器：一句种子密码 → LLM 变体（转写、译词、大小写）。</summary>
static class PasswordVariant {
	public const int MinCount = 4;
	public const int MaxCount = 20;
	public const int DefaultCount = 10;

	static readonly string SysPrompt =
		"You invent memorable password variants from one seed phrase or password. " +
		"Reply with ONLY a JSON array, no markdown, no commentary. " +
		"Each element: {\"pw\":\"variant\",\"note\":\"short reason\"}. " +
		"Never repeat the seed unchanged. Each pw must be distinct. " +
		"Use several of these techniques, mixed: " +
		"paraphrase: rewrite the same meaning with different words or a short phrase " +
		"(synonyms, idioms, compact sentences), then join as CamelCase or concatenated ASCII; " +
		"include several paraphrase-based variants; " +
		"translate word parts into other languages then ASCII-romanize (pinyin, romaji, Revised Romanization, etc.); " +
		"mix case (CamelCase). " +
		"Keep each pw 8–32 characters when possible; ASCII letters only. " +
		"Do not reorder the seed's words. " +
		"Do not add digits, and do not substitute letters with digits (no o→0 e→3 i→1 a→4 s→5). " +
		"Do not add punctuation or symbols (!@#$%^&*-_=+?~ and similar). " +
		"No spaces (concatenate or CamelCase); no newlines. " +
		"note: one short phrase in the same language as the seed (Chinese seed → Chinese note).";

	public static List<PasswordVariantRow> Generate(OcrOptions o, string seed, int count = DefaultCount,
		LlmEndpoint ep = null, CancellationToken ct = default) {
		seed = (seed ?? "").Trim();
		if (seed.Length == 0)
			throw new InvalidOperationException("empty");
		count = Compat.Clamp(count <= 0 ? DefaultCount : count, MinCount, MaxCount);
		ep ??= o?.SelectedPwLexLlm();
		if (!AsrLlmClient.IsEndpointReady(ep))
			throw new InvalidOperationException(Loc.T("pwgen.lex.nollm"));
		var user = $"n={count}\nseed:\n{seed}";
		object messages = new object[] {
			new { role = "system", content = SysPrompt },
			new { role = "user", content = user },
		};
		LlmLog.Info($"pwvar model={ep.Model} seedLen={seed.Length} n={count}");
		var raw = AsrLlmClient.ChatOnce(ep, messages, 90_000, 0.8f, ct);
		if (string.IsNullOrWhiteSpace(raw))
			throw new InvalidOperationException(Loc.T("pwgen.var.empty"));
		var rows = Parse(raw, seed);
		if (rows.Count == 0)
			throw new InvalidOperationException(Loc.T("pwgen.var.empty"));
		return rows;
	}

	public static List<PasswordVariantRow> Parse(string text, string seed = "") {
		var list = new List<PasswordVariantRow>();
		var json = extractjson(text ?? "");
		if (json.Length == 0) return list;
		JsonDocument doc;
		try { doc = JsonDocument.Parse(json); }
		catch { return list; }
		using (doc) {
			var root = doc.RootElement;
			if (root.ValueKind == JsonValueKind.Object) {
				foreach (var name in new[] { "items", "rows", "data", "variants" }) {
					if (root.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array) {
						root = arr;
						break;
					}
				}
			}
			if (root.ValueKind != JsonValueKind.Array) return list;
			var seen = new HashSet<string>(StringComparer.Ordinal);
			var seedTrim = (seed ?? "").Trim();
			if (seedTrim.Length > 0) seen.Add(seedTrim);
			foreach (var el in root.EnumerateArray()) {
				if (el.ValueKind != JsonValueKind.Object) continue;
				var pw = str(el, "pw", "password", "variant", "text");
				var note = str(el, "note", "reason", "how", "method");
				pw = normpw(pw);
				if (pw.Length == 0) continue;
				if (!seen.Add(pw)) continue;
				list.Add(new PasswordVariantRow { Password = pw, Note = note });
				if (list.Count >= MaxCount) break;
			}
		}
		return list;
	}

	static string normpw(string s) {
		s = (s ?? "").Trim();
		if (s.Length == 0) return "";
		if (s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0) {
			s = s.Replace("\r", "").Replace("\n", "");
			s = s.Trim();
		}
		if (s.Length > 128) s = s.Substring(0, 128);
		return s;
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
