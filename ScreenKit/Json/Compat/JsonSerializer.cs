using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ScreenKit.Json;

namespace System.Text.Json;

public sealed class JsonSerializerOptions {
	public static JsonSerializerOptions Default { get; } = new();

	public bool WriteIndented { get; set; }
	public JsonNamingPolicy PropertyNamingPolicy { get; set; }
	public JsonIgnoreCondition DefaultIgnoreCondition { get; set; }
	public JavaScriptEncoder Encoder { get; set; }

	public JsonSerializerOptions() { }

	public JsonSerializerOptions(JsonSerializerOptions options) {
		if (options == null) return;
		WriteIndented = options.WriteIndented;
		PropertyNamingPolicy = options.PropertyNamingPolicy;
		DefaultIgnoreCondition = options.DefaultIgnoreCondition;
		Encoder = options.Encoder;
	}
}

public static class JsonSerializer {
	public static string Serialize<T>(T value, JsonSerializerOptions options = null) =>
		tojsonf(value, options).ToJSONString(options != null && options.WriteIndented ? 0 : -1);

	public static T Deserialize<T>(string json, JsonSerializerOptions options = null) {
		if (string.IsNullOrWhiteSpace(json)) return default;
		var n = JSONF.Parse(json);
		if (n.Type == JSONF.NodeType.NotExist) return default;
		return (T)fromjsonf(n, typeof(T), options);
	}

	static JSONF tojsonf(object v, JsonSerializerOptions opt) {
		if (v == null) return JSONF.NULL;
		if (v is JsonNode jn) return jn == null ? JSONF.NULL : jn.Node;
		if (v is JsonElement je) return je.Node;
		if (v is string s) return JSONF.newData(s);
		if (v is bool b) return JSONF.newData(b);
		if (v is int i) return JSONF.newData(i);
		if (v is long l) return JSONF.newData(l);
		if (v is short sh) return JSONF.newData((long)sh);
		if (v is byte by) return JSONF.newData((long)by);
		if (v is sbyte sb) return JSONF.newData((long)sb);
		if (v is ushort us) return JSONF.newData((long)us);
		if (v is uint ui) return JSONF.newData((long)ui);
		if (v is ulong ul) return JSONF.newData(ul);
		if (v is float f) return JSONF.newData(f);
		if (v is double d) return JSONF.newData(d);
		if (v is decimal m) return JSONF.newData(m);
		if (v is DateTime dt) return JSONF.newData(dt);
		if (v is Enum e) return JSONF.newData(Convert.ToInt64(e, CultureInfo.InvariantCulture));
		if (v is IDictionary dict) {
			var o = JSONF.newObject();
			foreach (DictionaryEntry de in dict)
				o.Add(de.Key?.ToString() ?? "", tojsonf(de.Value, opt));
			return o;
		}
		if (v is IEnumerable en) {
			var a = JSONF.newArray();
			foreach (var item in en) a.Add(tojsonf(item, opt));
			return a;
		}
		var t = v.GetType();
		var obj = JSONF.newObject();
		foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
			if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
			if (p.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
			var pv = p.GetValue(v);
			if (skip(pv, p.PropertyType, opt)) continue;
			obj.Add(opt?.PropertyNamingPolicy?.ConvertName(p.Name) ?? p.Name, tojsonf(pv, opt));
		}
		foreach (var fld in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
			if (fld.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
			var fv = fld.GetValue(v);
			if (skip(fv, fld.FieldType, opt)) continue;
			obj.Add(opt?.PropertyNamingPolicy?.ConvertName(fld.Name) ?? fld.Name, tojsonf(fv, opt));
		}
		return obj;
	}

	static bool skip(object v, Type t, JsonSerializerOptions opt) {
		if (v == null) return opt?.DefaultIgnoreCondition == JsonIgnoreCondition.WhenWritingNull
			|| opt?.DefaultIgnoreCondition == JsonIgnoreCondition.WhenWritingDefault;
		if (opt?.DefaultIgnoreCondition != JsonIgnoreCondition.WhenWritingDefault) return false;
		if (!t.IsValueType) return false;
		return v.Equals(Activator.CreateInstance(t));
	}

	static object fromjsonf(in JSONF n, Type t, JsonSerializerOptions opt) {
		if (t == typeof(JsonNode)) return JsonNode.From(n);
		if (t == typeof(JsonObject)) return n.IsObject ? new JsonObject(n) : null;
		if (t == typeof(JsonArray)) return n.IsArray ? new JsonArray(n) : null;
		if (t == typeof(JsonValue)) return n.IsValue ? new JsonValue(n) : null;
		if (t == typeof(JsonElement)) return new JsonElement(n);
		if (t == typeof(string)) return n.IsNull ? null : n.Value;
		if (t == typeof(object)) return n.IsNull ? null : n.GetValueObject();
		var u = Nullable.GetUnderlyingType(t);
		if (u != null) return n.IsNull ? null : fromjsonf(n, u, opt);
		if (t == typeof(bool)) return n.AsBool;
		if (t == typeof(int)) return n.AsInt;
		if (t == typeof(long)) return n.AsLong;
		if (t == typeof(short)) return (short)n.AsInt;
		if (t == typeof(byte)) return (byte)n.AsInt;
		if (t == typeof(sbyte)) return (sbyte)n.AsInt;
		if (t == typeof(ushort)) return (ushort)n.AsInt;
		if (t == typeof(uint)) return (uint)n.AsLong;
		if (t == typeof(ulong)) return (ulong)n.AsLong;
		if (t == typeof(float)) return n.AsFloat;
		if (t == typeof(double)) return n.AsDouble;
		if (t == typeof(decimal)) return n.AsDecimal;
		if (t == typeof(DateTime)) return n.AsDateTime;
		if (t.IsEnum) return Enum.ToObject(t, n.AsLong);
		if (t.IsArray) {
			var et = t.GetElementType();
			var arr = Array.CreateInstance(et, n.Count);
			for (var i = 0; i < n.Count; i++) arr.SetValue(fromjsonf(n.get(i), et, opt), i);
			return arr;
		}
		if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) {
			var et = t.GetGenericArguments()[0];
			var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(et));
			foreach (var item in n) list.Add(fromjsonf(item, et, opt));
			return list;
		}
		if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>)) {
			var kt = t.GetGenericArguments()[0];
			var vt = t.GetGenericArguments()[1];
			var dict = (IDictionary)Activator.CreateInstance(t);
			foreach (var (k, v) in n.KeyVals) {
				var key = kt == typeof(string) ? k : Convert.ChangeType(k, kt, CultureInfo.InvariantCulture);
				dict[key] = fromjsonf(v, vt, opt);
			}
			return dict;
		}
		if (n.IsNull) return null;
		var obj = Activator.CreateInstance(t);
		foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
			if (!p.CanWrite || p.GetIndexParameters().Length > 0) continue;
			if (p.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
			var name = opt?.PropertyNamingPolicy?.ConvertName(p.Name) ?? p.Name;
			if (!find(n, name, out var pv)) continue;
			p.SetValue(obj, fromjsonf(pv, p.PropertyType, opt));
		}
		foreach (var fld in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
			if (fld.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
			var name = opt?.PropertyNamingPolicy?.ConvertName(fld.Name) ?? fld.Name;
			if (!find(n, name, out var fv)) continue;
			fld.SetValue(obj, fromjsonf(fv, fld.FieldType, opt));
		}
		return obj;
	}

	static bool find(in JSONF n, string name, out JSONF value) {
		if (!n.IsObject) {
			value = default;
			return false;
		}
		if (n.ContainsKey(name)) {
			value = n.get(name);
			return true;
		}
		foreach (var k in n.Keys) {
			if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) {
				value = n.get(k);
				return true;
			}
		}
		value = default;
		return false;
	}
}

public class JsonException : Exception {
	public JsonException(string message) : base(message) { }
	public JsonException(string message, Exception inner) : base(message, inner) { }
}

public abstract class JsonNamingPolicy {
	public abstract string ConvertName(string name);

	public static JsonNamingPolicy CamelCase { get; } = new CamelCasePolicy();

	sealed class CamelCasePolicy : JsonNamingPolicy {
		public override string ConvertName(string name) {
			if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0])) return name;
			return char.ToLowerInvariant(name[0]) + name.Substring(1);
		}
	}
}
