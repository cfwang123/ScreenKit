using System;

namespace System.Text.Json;

/// <summary>标准 JSON 结构校验。JSONF 本身是宽松的，JsonDocument/JsonNode 仍按标准拒绝坏格式。</summary>
static class JsonValidate {
	public static void Validate(string s, JsonDocumentOptions opt) {
		var i = 0;
		var comments = opt.CommentHandling != JsonCommentHandling.Disallow;
		skip(s, ref i, comments);
		if (i >= s.Length) throw new JsonException("empty json");
		value(s, ref i, comments, opt.AllowTrailingCommas, 0);
		skip(s, ref i, comments);
		if (i != s.Length) throw new JsonException("trailing json at " + i);
	}

	static void value(string s, ref int i, bool comments, bool trailing, int depth) {
		if (depth > 512) throw new JsonException("json too deep");
		skip(s, ref i, comments);
		if (i >= s.Length) throw new JsonException("json value expected");
		var c = s[i];
		if (c == '{') {
			obj(s, ref i, comments, trailing, depth + 1);
			return;
		}
		if (c == '[') {
			arr(s, ref i, comments, trailing, depth + 1);
			return;
		}
		if (c == '"') {
			str(s, ref i);
			return;
		}
		if (c == 't' && match(s, i, "true")) { i += 4; return; }
		if (c == 'f' && match(s, i, "false")) { i += 5; return; }
		if (c == 'n' && match(s, i, "null")) { i += 4; return; }
		if (c == '-' || (c >= '0' && c <= '9')) {
			num(s, ref i);
			return;
		}
		throw new JsonException("bad json value at " + i);
	}

	static void obj(string s, ref int i, bool comments, bool trailing, int depth) {
		i++;
		skip(s, ref i, comments);
		if (i < s.Length && s[i] == '}') { i++; return; }
		while (true) {
			skip(s, ref i, comments);
			if (i >= s.Length || s[i] != '"') throw new JsonException("object key expected at " + i);
			str(s, ref i);
			skip(s, ref i, comments);
			if (i >= s.Length || s[i] != ':') throw new JsonException("':' expected at " + i);
			i++;
			value(s, ref i, comments, trailing, depth);
			skip(s, ref i, comments);
			if (i < s.Length && s[i] == ',') {
				i++;
				skip(s, ref i, comments);
				if (trailing && i < s.Length && s[i] == '}') { i++; return; }
				continue;
			}
			if (i < s.Length && s[i] == '}') { i++; return; }
			throw new JsonException("'}' expected at " + i);
		}
	}

	static void arr(string s, ref int i, bool comments, bool trailing, int depth) {
		i++;
		skip(s, ref i, comments);
		if (i < s.Length && s[i] == ']') { i++; return; }
		while (true) {
			value(s, ref i, comments, trailing, depth);
			skip(s, ref i, comments);
			if (i < s.Length && s[i] == ',') {
				i++;
				skip(s, ref i, comments);
				if (trailing && i < s.Length && s[i] == ']') { i++; return; }
				continue;
			}
			if (i < s.Length && s[i] == ']') { i++; return; }
			throw new JsonException("']' expected at " + i);
		}
	}

	static void str(string s, ref int i) {
		i++;
		while (i < s.Length) {
			var c = s[i];
			if (c == '"') { i++; return; }
			if (c < ' ') throw new JsonException("control char in string at " + i);
			if (c == '\\') {
				i++;
				if (i >= s.Length) throw new JsonException("bad escape at " + i);
				var e = s[i];
				if (e == 'u') {
					if (i + 4 >= s.Length) throw new JsonException("bad unicode escape at " + i);
					for (var k = 1; k <= 4; k++)
						if (!Uri.IsHexDigit(s[i + k])) throw new JsonException("bad unicode escape at " + i);
					i += 4;
				}
				else if (e is not ('"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't'))
					throw new JsonException("bad escape at " + i);
			}
			i++;
		}
		throw new JsonException("unclosed string");
	}

	static void num(string s, ref int i) {
		if (i < s.Length && s[i] == '-') i++;
		if (i >= s.Length) throw new JsonException("bad number");
		if (s[i] == '0') i++;
		else if (s[i] >= '1' && s[i] <= '9') {
			while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
		}
		else throw new JsonException("bad number at " + i);
		if (i < s.Length && s[i] == '.') {
			i++;
			if (i >= s.Length || s[i] < '0' || s[i] > '9') throw new JsonException("bad fraction at " + i);
			while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
		}
		if (i < s.Length && (s[i] == 'e' || s[i] == 'E')) {
			i++;
			if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
			if (i >= s.Length || s[i] < '0' || s[i] > '9') throw new JsonException("bad exponent at " + i);
			while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
		}
	}

	static bool match(string s, int i, string lit) =>
		i + lit.Length <= s.Length && string.CompareOrdinal(s, i, lit, 0, lit.Length) == 0;

	static void skip(string s, ref int i, bool comments) {
		while (i < s.Length) {
			var c = s[i];
			if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { i++; continue; }
			if (!comments || c != '/' || i + 1 >= s.Length) return;
			if (s[i + 1] == '/') {
				i += 2;
				while (i < s.Length && s[i] != '\n') i++;
				continue;
			}
			if (s[i + 1] == '*') {
				i += 2;
				while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
				if (i + 1 >= s.Length) throw new JsonException("unclosed comment");
				i += 2;
				continue;
			}
			return;
		}
	}
}
