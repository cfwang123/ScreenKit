using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ScreenKit;

sealed class TextStats {
	public int Chars;
	public int CharsNoWs;
	public int Lines;
	public int Utf8Bytes;
	public int GbkBytes;
}

/// <summary>文本编解码 / 空白 / 统计。</summary>
static class TextTools {
	static bool encReady;

	static void ensureenc() {
		if (encReady) return;
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		encReady = true;
	}

	public static Encoding Gbk {
		get {
			ensureenc();
			return Encoding.GetEncoding(936);
		}
	}

	public static TextStats Stats(string text) {
		text ??= "";
		ensureenc();
		var lines = text.Length == 0 ? 0 : text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Length;
		var noWs = 0;
		foreach (var ch in text) {
			if (!char.IsWhiteSpace(ch)) noWs++;
		}
		return new TextStats {
			Chars = text.Length,
			CharsNoWs = noWs,
			Lines = lines,
			Utf8Bytes = Encoding.UTF8.GetByteCount(text),
			GbkBytes = Gbk.GetByteCount(text),
		};
	}

	public static string Base64Enc(string text) {
		text ??= "";
		return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
	}

	public static string Base64Dec(string text) {
		var s = (text ?? "").Trim();
		s = s.Replace("\r", "").Replace("\n", "").Replace(" ", "");
		var bytes = Convert.FromBase64String(s);
		return Encoding.UTF8.GetString(bytes);
	}

	public static string UrlEnc(string text) =>
		Uri.EscapeDataString(text ?? "");

	public static string UrlDec(string text) =>
		Uri.UnescapeDataString((text ?? "").Replace("+", " "));

	public static string Utf8Hex(string text) =>
		tohex(Encoding.UTF8.GetBytes(text ?? ""));

	public static string GbkHex(string text) =>
		tohex(Gbk.GetBytes(text ?? ""));

	public static string FromUtf8Hex(string hex) =>
		Encoding.UTF8.GetString(fromhex(hex));

	public static string FromGbkHex(string hex) =>
		Gbk.GetString(fromhex(hex));

	public static string UnicodeEsc(string text) {
		text ??= "";
		var sb = new StringBuilder(text.Length * 2);
		foreach (var ch in text) {
			if (ch < 0x80) sb.Append(ch);
			else sb.Append("\\u").Append(((int)ch).ToString("x4"));
		}
		return sb.ToString();
	}

	public static string UnicodeUnesc(string text) {
		text ??= "";
		var sb = new StringBuilder(text.Length);
		for (var i = 0; i < text.Length; i++) {
			if (i + 5 < text.Length && text[i] == '\\' && (text[i + 1] == 'u' || text[i + 1] == 'U')) {
				if (int.TryParse(text.Substring(i + 2, 4), System.Globalization.NumberStyles.HexNumber, null, out var cp)) {
					sb.Append((char)cp);
					i += 5;
					continue;
				}
			}
			sb.Append(text[i]);
		}
		return sb.ToString();
	}

	public static string CollapseWs(string text) {
		text ??= "";
		var sb = new StringBuilder(text.Length);
		var was = false;
		foreach (var ch in text) {
			if (char.IsWhiteSpace(ch)) {
				if (!was) {
					sb.Append(' ');
					was = true;
				}
			}
			else {
				sb.Append(ch);
				was = false;
			}
		}
		return sb.ToString().Trim();
	}

	/// <summary>
	/// 智能美化 JSON：短的纯值数组/对象压成一行（如 [0,"key",true,null]），
	/// 含嵌套结构的再按 Tab 缩进展开。冒号后无空格，与 eprj2 project.json 同类。
	/// </summary>
	public static string JsonPretty(string text) {
		text = (text ?? "").Trim();
		if (text.Length == 0) return "";
		using var doc = JsonDocument.Parse(text, new JsonDocumentOptions {
			AllowTrailingCommas = true,
			CommentHandling = JsonCommentHandling.Skip,
		});
		var sb = new StringBuilder(text.Length + 64);
		writejson(sb, doc.RootElement, 0);
		return sb.ToString();
	}

	const int JsonCompactMax = 100;

	static readonly JsonSerializerOptions JsonCompactOpt = new() {
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	static bool jsonleaf(JsonElement el) {
		if (el.ValueKind == JsonValueKind.Array) {
			foreach (var x in el.EnumerateArray()) {
				if (x.ValueKind == JsonValueKind.Object || x.ValueKind == JsonValueKind.Array)
					return false;
			}
			return true;
		}
		if (el.ValueKind == JsonValueKind.Object) {
			foreach (var p in el.EnumerateObject()) {
				if (p.Value.ValueKind == JsonValueKind.Object || p.Value.ValueKind == JsonValueKind.Array)
					return false;
			}
			return true;
		}
		return true;
	}

	static string jsoncompact(JsonElement el) =>
		JsonSerializer.Serialize(el, JsonCompactOpt);

	static void writejson(StringBuilder sb, JsonElement el, int depth) {
		if (el.ValueKind != JsonValueKind.Array && el.ValueKind != JsonValueKind.Object) {
			sb.Append(jsoncompact(el));
			return;
		}
		var compact = jsoncompact(el);
		if (jsonleaf(el) && compact.Length <= JsonCompactMax) {
			sb.Append(compact);
			return;
		}
		if (el.ValueKind == JsonValueKind.Array) {
			sb.Append('[');
			var first = true;
			foreach (var x in el.EnumerateArray()) {
				if (!first) sb.Append(',');
				first = false;
				sb.Append('\n');
				indent(sb, depth + 1);
				writejson(sb, x, depth + 1);
			}
			if (!first) {
				sb.Append('\n');
				indent(sb, depth);
			}
			sb.Append(']');
			return;
		}
		sb.Append('{');
		var firstObj = true;
		foreach (var p in el.EnumerateObject()) {
			if (!firstObj) sb.Append(',');
			firstObj = false;
			sb.Append('\n');
			indent(sb, depth + 1);
			sb.Append(JsonSerializer.Serialize(p.Name, JsonCompactOpt));
			sb.Append(':');
			writejson(sb, p.Value, depth + 1);
		}
		if (!firstObj) {
			sb.Append('\n');
			indent(sb, depth);
		}
		sb.Append('}');
	}

	static void indent(StringBuilder sb, int depth) {
		for (var i = 0; i < depth; i++)
			sb.Append('\t');
	}

	public static string DropEmptyLines(string text) {
		text ??= "";
		var nl = text.Contains("\r\n") ? "\r\n" : "\n";
		var parts = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
		return string.Join(nl, parts.Where(l => l.Trim().Length > 0));
	}

	static string tohex(byte[] bytes) {
		if (bytes == null || bytes.Length == 0) return "";
		var sb = new StringBuilder(bytes.Length * 3);
		for (var i = 0; i < bytes.Length; i++) {
			if (i > 0) sb.Append(' ');
			sb.Append(bytes[i].ToString("x2"));
		}
		return sb.ToString();
	}

	static byte[] fromhex(string hex) {
		var n = HashTool.NormHex(hex);
		if (n.Length % 2 != 0)
			throw new FormatException("hex length");
		var bytes = new byte[n.Length / 2];
		for (var i = 0; i < bytes.Length; i++)
			bytes[i] = Convert.ToByte(n.Substring(i * 2, 2), 16);
		return bytes;
	}
}
