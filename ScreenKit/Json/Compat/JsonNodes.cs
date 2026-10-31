using System;
using System.Collections;
using System.Collections.Generic;
using ScreenKit.Json;

namespace System.Text.Json.Nodes;

public struct JsonNodeOptions {
	public bool PropertyNameCaseInsensitive { get; set; }
}

/// <summary>
/// System.Text.Json.Nodes 的轻量替代，底层全部走 JSONF。
/// 保留原 API 形状，现有调用点不用改。
/// </summary>
public abstract class JsonNode {
	internal JSONF Node;

	internal JsonNode(JSONF node) {
		Node = node;
	}

	public static JsonNode Parse(string json, JsonNodeOptions? nodeOptions = null, JsonDocumentOptions documentOptions = default) {
		if (json == null) throw new ArgumentNullException(nameof(json));
		var t = json.Trim();
		if (t.Length == 0) return null;
		JSONF n;
		if (t[0] == '{' || t[0] == '[') {
			JsonValidate.Validate(t, documentOptions);
			n = JSONF.Parse(t);
		}
		else {
			JsonValidate.Validate(t, documentOptions);
			n = JSONF.Parse("[" + t + "]").get(0);
		}
		return From(n);
	}

	internal static JsonNode From(in JSONF n) {
		switch (n.Type) {
			case JSONF.NodeType.Object: return new JsonObject(n);
			case JSONF.NodeType.Array: return new JsonArray(n);
			case JSONF.NodeType.Null:
			case JSONF.NodeType.NotExist: return null;
			default: return new JsonValue(n);
		}
	}

	public virtual JsonNode this[int index] {
		get => null;
		set => throw new InvalidOperationException();
	}

	public virtual JsonNode this[string key] {
		get => null;
		set => throw new InvalidOperationException();
	}

	public virtual int Count => 0;

	public virtual JsonValueKind GetValueKind() {
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

	public T GetValue<T>() => (T)ConvertTo(typeof(T));

	public bool TryGetValue<T>(out T value) {
		try {
			value = GetValue<T>();
			return true;
		}
		catch {
			value = default;
			return false;
		}
	}

	object ConvertTo(Type t) {
		if (t == typeof(string)) return Node.Value ?? "";
		if (t == typeof(int)) return Node.AsInt;
		if (t == typeof(long)) return Node.AsLong;
		if (t == typeof(float)) return Node.AsFloat;
		if (t == typeof(double)) return Node.AsDouble;
		if (t == typeof(decimal)) return Node.AsDecimal;
		if (t == typeof(bool)) return Node.AsBool;
		if (t == typeof(DateTime)) return Node.AsDateTime;
		if (t == typeof(object)) return Node.GetValueObject();
		return Convert.ChangeType(Node.GetValueObject(), t, System.Globalization.CultureInfo.InvariantCulture);
	}

	public JsonObject AsObject() => this as JsonObject ?? throw new InvalidOperationException("node is not an object");
	public JsonArray AsArray() => this as JsonArray ?? throw new InvalidOperationException("node is not an array");
	public JsonValue AsValue() => this as JsonValue ?? throw new InvalidOperationException("node is not a value");

	public string ToJsonString(JsonSerializerOptions options = null) =>
		Node.ToJSONString(options != null && options.WriteIndented ? 0 : -1);

	public JsonNode DeepClone() => From(Node.Clone);

	public override string ToString() {
		if (Node.IsNull) return "";
		if (Node.IsValue) return Node.Value ?? "";
		return ToJsonString();
	}

	public static implicit operator JsonNode(string value) =>
		value == null ? null : new JsonValue(JSONF.newData(value));
	public static implicit operator JsonNode(int value) => new JsonValue(JSONF.newData(value));
	public static implicit operator JsonNode(long value) => new JsonValue(JSONF.newData(value));
	public static implicit operator JsonNode(float value) => new JsonValue(JSONF.newData(value));
	public static implicit operator JsonNode(double value) => new JsonValue(JSONF.newData(value));
	public static implicit operator JsonNode(decimal value) => new JsonValue(JSONF.newData(value));
	public static implicit operator JsonNode(bool value) => new JsonValue(JSONF.newData(value));
	public static implicit operator JsonNode(DateTime value) => new JsonValue(JSONF.newData(value));

	public static explicit operator string(JsonNode value) => value?.Node.Value ?? "";
	public static explicit operator int(JsonNode value) => value?.Node.AsInt ?? 0;
	public static explicit operator long(JsonNode value) => value?.Node.AsLong ?? 0;
	public static explicit operator float(JsonNode value) => value?.Node.AsFloat ?? 0;
	public static explicit operator double(JsonNode value) => value?.Node.AsDouble ?? 0;
	public static explicit operator decimal(JsonNode value) => value?.Node.AsDecimal ?? 0;
	public static explicit operator bool(JsonNode value) => value != null && value.Node.AsBool;
	public static explicit operator DateTime(JsonNode value) => value?.Node.AsDateTime ?? default;
}

public sealed class JsonObject : JsonNode, IDictionary<string, JsonNode> {
	public JsonObject() : base(JSONF.newObject()) { }
	internal JsonObject(JSONF n) : base(n) { }

	public override JsonNode this[string key] {
		get {
			var v = Node.get(key);
			return v.IsNull ? null : From(v);
		}
		set => Node[key] = value == null ? JSONF.NULL : value.Node;
	}

	public override int Count => Node.Count;
	public bool IsReadOnly => false;

	public ICollection<string> Keys {
		get {
			var list = new List<string>(Node.Count);
			foreach (var k in Node.Keys) list.Add(k);
			return list;
		}
	}

	public ICollection<JsonNode> Values {
		get {
			var list = new List<JsonNode>(Node.Count);
			foreach (var v in Node.Vals) list.Add(From(v));
			return list;
		}
	}

	public bool ContainsKey(string key) => Node.ContainsKey(key);

	public bool TryGetValue(string key, out JsonNode value) {
		if (!Node.ContainsKey(key)) {
			value = null;
			return false;
		}
		var v = Node.get(key);
		value = v.IsNull ? null : From(v);
		return true;
	}

	public bool TryGetPropertyValue(string key, out JsonNode value) => TryGetValue(key, out value);

	public void Add(string key, JsonNode value) => Node.Add(key, value == null ? JSONF.NULL : value.Node);

	public bool Remove(string key) => Node.Remove(key);

	public void Add(KeyValuePair<string, JsonNode> item) => Add(item.Key, item.Value);

	public void Clear() => Node.Clear();

	public bool Contains(KeyValuePair<string, JsonNode> item) =>
		TryGetValue(item.Key, out var v) && v == item.Value;

	public void CopyTo(KeyValuePair<string, JsonNode>[] array, int arrayIndex) {
		foreach (var kv in this) array[arrayIndex++] = kv;
	}

	public bool Remove(KeyValuePair<string, JsonNode> item) => Remove(item.Key);

	public IEnumerator<KeyValuePair<string, JsonNode>> GetEnumerator() {
		foreach (var (k, v) in Node.KeyVals)
			yield return new KeyValuePair<string, JsonNode>(k, From(v));
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class JsonArray : JsonNode, IList<JsonNode> {
	public JsonArray() : base(JSONF.newArray()) { }
	internal JsonArray(JSONF n) : base(n) { }
	public JsonArray(params JsonNode[] items) : base(JSONF.newArray()) {
		foreach (var item in items) Add(item);
	}

	public override JsonNode this[int index] {
		get => From(Node.get(index));
		set => Node[index] = value == null ? JSONF.NULL : value.Node;
	}

	public override int Count => Node.Count;
	public bool IsReadOnly => false;

	public void Add(JsonNode item) => Node.Add(item == null ? JSONF.NULL : item.Node);
	public void AddRange(IEnumerable<JsonNode> items) {
		foreach (var item in items) Add(item);
	}

	public void Clear() => Node.Clear();
	public bool Contains(JsonNode item) => IndexOf(item) >= 0;

	public void CopyTo(JsonNode[] array, int arrayIndex) {
		foreach (var item in this) array[arrayIndex++] = item;
	}

	public int IndexOf(JsonNode item) {
		for (var i = 0; i < Count; i++)
			if (ReferenceEquals(this[i], item)) return i;
		return -1;
	}

	public void Insert(int index, JsonNode item) => Node.Insert(index, item == null ? JSONF.NULL : item.Node);
	public bool Remove(JsonNode item) {
		var i = IndexOf(item);
		if (i < 0) return false;
		Node.RemoveAt(i);
		return true;
	}
	public void RemoveAt(int index) => Node.RemoveAt(index);

	public IEnumerator<JsonNode> GetEnumerator() {
		foreach (var v in Node) yield return From(v);
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class JsonValue : JsonNode {
	internal JsonValue(JSONF n) : base(n) { }
	public static JsonValue Create<T>(T value) => new JsonValue(JSONF.ToJSONData(value));
	public static JsonValue Create(string value) => new JsonValue(JSONF.newData(value));
	public object Value => Node.GetValueObject();
}
