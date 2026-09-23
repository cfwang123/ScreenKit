using System.Text;

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
