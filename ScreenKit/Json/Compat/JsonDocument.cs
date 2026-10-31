using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ScreenKit.Json;

namespace System.Text.Json;

public enum JsonValueKind { Undefined = 0, Object, Array, String, Number, True, False, Null }

public enum JsonCommentHandling { Disallow = 0, Skip, Allow }

public struct JsonDocumentOptions {
	public bool AllowTrailingCommas { get; set; }
	public JsonCommentHandling CommentHandling { get; set; }
	public int MaxDepth { get; set; }
}

public sealed class JsonDocument : IDisposable {
	readonly JSONF root;

	JsonDocument(JSONF root) {
		this.root = root;
	}

	public JsonElement RootElement => new(root);

	public static JsonDocument Parse(string json, JsonDocumentOptions options = default) {
		if (json == null) throw new ArgumentNullException(nameof(json));
		var t = json.Trim();
		JSONF n;
		if (t.Length == 0) n = JSONF.NULL;
		else if (t[0] == '{' || t[0] == '[') {
			JsonValidate.Validate(t, options);
			n = JSONF.Parse(t);
		}
		else {
			JsonValidate.Validate(t, options);
			n = JSONF.Parse("[" + t + "]").get(0);
		}
		return new JsonDocument(n);
	}

	public static JsonDocument Parse(Stream stream, JsonDocumentOptions options = default) {
		if (stream == null) throw new ArgumentNullException(nameof(stream));
		using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true);
		return Parse(reader.ReadToEnd(), options);
	}

	public void Dispose() { }
}

public readonly struct JsonElement {
	internal readonly JSONF Node;

	internal JsonElement(in JSONF node) {
		Node = node;
	}

	public JsonValueKind ValueKind {
		get {
			switch (Node.Type) {
				case JSONF.NodeType.Object: return JsonValueKind.Object;
				case JSONF.NodeType.Array: return JsonValueKind.Array;
				case JSONF.NodeType.String:
				case JSONF.NodeType.Expression: return JsonValueKind.String;
				case JSONF.NodeType.Int:
				case JSONF.NodeType.Long:
				case JSONF.NodeType.Float:
				case JSONF.NodeType.Double:
				case JSONF.NodeType.Decimal: return JsonValueKind.Number;
				case JSONF.NodeType.Bool: return Node.Val.BoolVal ? JsonValueKind.True : JsonValueKind.False;
				case JSONF.NodeType.Null: return JsonValueKind.Null;
				default: return JsonValueKind.Undefined;
			}
		}
	}

	public JsonElement this[int index] => new(Node.get(index));

	public int GetArrayLength() => Node.IsArray ? Node.Count : 0;

	public IEnumerable<JsonElement> EnumerateArray() {
		if (!Node.IsArray) yield break;
		foreach (var v in Node)
			yield return new JsonElement(v);
	}

	public IEnumerable<JsonProperty> EnumerateObject() {
		if (!Node.IsObject) yield break;
		foreach (var (k, v) in Node.KeyVals)
			yield return new JsonProperty(k, v);
	}

	public bool TryGetProperty(string name, out JsonElement value) {
		if (Node.IsObject && Node.ContainsKey(name)) {
			value = new JsonElement(Node.get(name));
			return true;
		}
		value = default;
		return false;
	}

	public JsonElement GetProperty(string name) => TryGetProperty(name, out var v) ? v : default;

	public string GetString() => Node.IsNull ? null : Node.Value;
	public int GetInt32() => Node.AsInt;
	public long GetInt64() => Node.AsLong;
	public float GetSingle() => Node.AsFloat;
	public double GetDouble() => Node.AsDouble;
	public bool GetBoolean() => Node.AsBool;
	public T Deserialize<T>(JsonSerializerOptions options = null) =>
		JsonSerializer.Deserialize<T>(GetRawText(), options);

	public bool TryGetInt32(out int value) {
		if (Node.Type is JSONF.NodeType.Int or JSONF.NodeType.Long or JSONF.NodeType.Float or JSONF.NodeType.Double or JSONF.NodeType.Decimal) {
			value = Node.AsInt;
			return true;
		}
		return int.TryParse(Node.Value, out value);
	}

	public bool TryGetInt64(out long value) {
		if (Node.Type is JSONF.NodeType.Int or JSONF.NodeType.Long or JSONF.NodeType.Float or JSONF.NodeType.Double or JSONF.NodeType.Decimal) {
			value = Node.AsLong;
			return true;
		}
		return long.TryParse(Node.Value, out value);
	}

	public bool TryGetDouble(out double value) {
		if (Node.Type is JSONF.NodeType.Int or JSONF.NodeType.Long or JSONF.NodeType.Float or JSONF.NodeType.Double or JSONF.NodeType.Decimal) {
			value = Node.AsDouble;
			return true;
		}
		return double.TryParse(Node.Value, System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out value);
	}

	public bool TryGetBoolean(out bool value) {
		if (Node.Type == JSONF.NodeType.Bool) {
			value = Node.Val.BoolVal;
			return true;
		}
		return bool.TryParse(Node.Value, out value);
	}

	public string GetRawText() => Node.IsNull ? "null" : Node.ToJSONString(-1);

	public JsonElement Clone() => new(Node.Clone);

	public override string ToString() {
		if (Node.IsNull) return "";
		if (Node.IsValue) return Node.Value ?? "";
		return Node.ToJSONString(-1);
	}
}

public readonly struct JsonProperty {
	public JsonProperty(string name, in JSONF value) {
		Name = name;
		Value = new JsonElement(value);
	}

	public string Name { get; }
	public JsonElement Value { get; }
}
