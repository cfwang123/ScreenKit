using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenKit.Json;

/// <summary>标了这个特性的属性或字段不参与 FromObject / ToObject。</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class JsonIgnoreAttribute : Attribute { }

// 从 Helper3 的 JSONF.cs 移植。保留宽松解析：键可以不带引号，允许 // 和 /* */ 注释。
// 词典的紧凑词条就是这种写法，标准 JSON 解析器读不了。

/// <summary>8 字节联合体，按节点类型取其中一种解释。</summary>
[StructLayout(LayoutKind.Explicit, Size = 8)]
public struct DWord {
	[FieldOffset(0)] public bool BoolVal;
	[FieldOffset(0)] public int IntVal;
	[FieldOffset(0)] public long LongVal;
	[FieldOffset(0)] public float FloatVal;
	[FieldOffset(0)] public double DoubleVal;
	[FieldOffset(0)] public DateTime TimeVal;

	public static DWord Make(bool v) {
		var d = new DWord();
		d.LongVal = 0;
		d.BoolVal = v;
		return d;
	}

	public static DWord Make(int v) => new DWord { LongVal = v };
	public static DWord Make(long v) => new DWord { LongVal = v };
	public static DWord Make(float v) => new DWord { FloatVal = v };
	public static DWord Make(double v) => new DWord { DoubleVal = v };
	public static DWord Make(DateTime v) => new DWord { TimeVal = v };
}

/// <summary>可返回 ref 的轻量列表，JSONF 的数组节点用。</summary>
sealed class JsonList<T> : IEnumerable<T> {
	T[] arr;
	int count;

	public JsonList(int capacity = 0) {
		arr = new T[Math.Max(capacity, 4)];
	}

	public int Count => count;

	public int Capacity {
		get => arr.Length;
		set {
			if (value > arr.Length) {
				var next = new T[value];
				Array.Copy(arr, 0, next, 0, arr.Length);
				arr = next;
			}
		}
	}

	public ref T this[int index] => ref arr[index];

	public void Add(T value) {
		if (count == arr.Length) Capacity = arr.Length * 2;
		arr[count++] = value;
	}

	public void Add(ref T value) {
		if (count == arr.Length) Capacity = arr.Length * 2;
		arr[count++] = value;
	}

	public void Insert(int index, T value) {
		if (index < 0 || index > count) throw new ArgumentOutOfRangeException(nameof(index));
		if (count == arr.Length) Capacity = arr.Length * 2;
		for (var i = count; i > index; i--) arr[i] = arr[i - 1];
		arr[index] = value;
		count++;
	}

	public void RemoveAt(int index) {
		if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
		for (var i = index; i < count - 1; i++) arr[i] = arr[i + 1];
		arr[--count] = default;
	}

	public void Clear() {
		Array.Clear(arr, 0, count);
		count = 0;
	}

	public IEnumerator<T> GetEnumerator() {
		for (var i = 0; i < count; i++)
			yield return arr[i];
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>按插入顺序枚举的字典，JSONF 的对象节点用。</summary>
sealed class JsonMap<K, V> : IEnumerable<(K, V)> {
	readonly Dictionary<K, int> posmap;
	readonly JsonList<V> items;
	readonly List<int> emptypos = new();

	public JsonMap(int capacity = 0, IEqualityComparer<K> cmp = null) {
		posmap = new Dictionary<K, int>(capacity, cmp);
		items = new JsonList<V>(capacity);
	}

	public int Count => posmap.Count;
	public IEnumerable<K> Keys => posmap.Keys;

	public int Capacity {
		get => items.Capacity;
		set => items.Capacity = value;
	}

	public ref V this[K key] => ref items[posmap[key]];

	public bool ContainsKey(K key) => posmap.ContainsKey(key);

	public bool TryGetValue(K key, out V value) {
		value = default;
		if (!posmap.TryGetValue(key, out var pos)) return false;
		value = items[pos];
		return true;
	}

	public void Add(K key, V value) => Add(key, ref value);

	public void Add(K key, ref V value) {
		if (posmap.TryGetValue(key, out var pos)) {
			items[pos] = value;
			return;
		}
		if (emptypos.Count > 0) {
			pos = emptypos[emptypos.Count - 1];
			emptypos.RemoveAt(emptypos.Count - 1);
			posmap[key] = pos;
			items[pos] = value;
			return;
		}
		posmap[key] = items.Count;
		items.Add(ref value);
	}

	public bool Remove(K key) {
		if (!posmap.TryGetValue(key, out var pos)) return false;
		items[pos] = default;
		emptypos.Add(pos);
		posmap.Remove(key);
		return true;
	}

	public void Clear() {
		posmap.Clear();
		items.Clear();
		emptypos.Clear();
	}

	public IEnumerator<(K, V)> GetEnumerator() {
		foreach (var kv in posmap)
			yield return (kv.Key, items[kv.Value]);
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>宽松 JSON 节点。数组/对象共享引用，标量按值复制。</summary>
public struct JSONF : IEnumerable<JSONF> {
	public enum NodeType { NotExist = 0, Null, Array, Object, Int, Long, Float, Double, DateTime, Bool, Decimal, String, ByteArray, CustomObject, Expression, Ignore }

	public NodeType Type;
	public DWord Val;
	public object Oval;

	public JSONF(NodeType type = NodeType.NotExist, DWord val = default, object oval = null) {
		Type = type;
		Val = val;
		Oval = oval;
		if (oval == null) {
			if (type == NodeType.Array) Oval = new JsonList<JSONF>();
			else if (type == NodeType.Object) Oval = new JsonMap<string, JSONF>();
		}
	}

	public bool IsNull => Type == NodeType.Null || Type == NodeType.NotExist;
	public bool IsNotNull => !IsNull;
	public bool NotExist => Type == NodeType.NotExist;
	public bool IsArray => Type == NodeType.Array;
	public bool IsObject => Type == NodeType.Object;
	public bool IsValue => Type is NodeType.String or NodeType.Int or NodeType.Long or NodeType.Float
		or NodeType.Double or NodeType.Bool or NodeType.Decimal or NodeType.DateTime
		or NodeType.ByteArray or NodeType.CustomObject or NodeType.Expression;

	public int Count {
		get {
			if (Oval is JsonList<JSONF> arr) return arr.Count;
			if (Oval is JsonMap<string, JSONF> map) return map.Count;
			return 0;
		}
	}

	public string Value {
		get {
			ParseExpression();
			switch (Type) {
				case NodeType.String: return (string)Oval ?? "";
				case NodeType.Int: return Val.IntVal.ToString(CultureInfo.InvariantCulture);
				case NodeType.Long: return Val.LongVal.ToString(CultureInfo.InvariantCulture);
				case NodeType.Float:
					var f = Val.FloatVal;
					if (float.IsNaN(f)) return "NaN";
					if (float.IsPositiveInfinity(f)) return "Inf";
					if (float.IsNegativeInfinity(f)) return "-Inf";
					return removetrailingzeros(f.ToString(CultureInfo.InvariantCulture));
				case NodeType.Double:
					var d = Val.DoubleVal;
					if (double.IsNaN(d)) return "NaN";
					if (double.IsPositiveInfinity(d)) return "Inf";
					if (double.IsNegativeInfinity(d)) return "-Inf";
					return removetrailingzeros(d.ToString(CultureInfo.InvariantCulture));
				case NodeType.Bool: return Val.BoolVal ? "1" : "0";
				case NodeType.DateTime: return Val.TimeVal.ToString(DATEFMT, CultureInfo.InvariantCulture);
				case NodeType.Decimal: return removetrailingzeros(((decimal)Oval!).ToString(CultureInfo.InvariantCulture));
				case NodeType.ByteArray: return bin2hex((byte[])Oval!);
				case NodeType.Array: return "[Array]";
				case NodeType.Object: return "[Object]";
				case NodeType.CustomObject: return Oval?.ToString() ?? "";
				default: return "";
			}
		}
		set {
			Type = NodeType.String;
			Oval = value ?? "";
			Val = default;
		}
	}

	public void SetNull() {
		Type = NodeType.Null;
		Oval = null;
		Val = default;
	}

	public int AsInt {
		get {
			ParseExpression();
			switch (Type) {
				case NodeType.String: return toint((string)Oval);
				case NodeType.Int: return Val.IntVal;
				case NodeType.Long: return (int)Val.LongVal;
				case NodeType.Float: return (int)Val.FloatVal;
				case NodeType.Double: return (int)Val.DoubleVal;
				case NodeType.Bool: return Val.BoolVal ? 1 : 0;
				case NodeType.Decimal: return (int)(decimal)Oval!;
				default: return 0;
			}
		}
		set {
			Type = NodeType.Int;
			Val = DWord.Make(value);
			Oval = null;
		}
	}

	public long AsLong {
		get {
			ParseExpression();
			switch (Type) {
				case NodeType.String: return tolong((string)Oval);
				case NodeType.Int: return Val.IntVal;
				case NodeType.Long: return Val.LongVal;
				case NodeType.Float: return (long)Val.FloatVal;
				case NodeType.Double: return (long)Val.DoubleVal;
				case NodeType.Bool: return Val.BoolVal ? 1 : 0;
				case NodeType.Decimal: return (long)(decimal)Oval!;
				default: return 0;
			}
		}
		set {
			Type = NodeType.Long;
			Val = DWord.Make(value);
			Oval = null;
		}
	}

	public float AsFloat {
		get {
			ParseExpression();
			switch (Type) {
				case NodeType.String: return tofloat((string)Oval);
				case NodeType.Int: return Val.IntVal;
				case NodeType.Long: return Val.LongVal;
				case NodeType.Float: return Val.FloatVal;
				case NodeType.Double: return (float)Val.DoubleVal;
				case NodeType.Bool: return Val.BoolVal ? 1 : 0;
				case NodeType.Decimal: return (float)(decimal)Oval!;
				default: return 0;
			}
		}
		set {
			Type = NodeType.Float;
			Val = DWord.Make(value);
			Oval = null;
		}
	}

	public double AsDouble {
		get {
			ParseExpression();
			switch (Type) {
				case NodeType.String: return todouble((string)Oval);
				case NodeType.Int: return Val.IntVal;
				case NodeType.Long: return Val.LongVal;
				case NodeType.Float: return Val.FloatVal;
				case NodeType.Double: return Val.DoubleVal;
				case NodeType.Bool: return Val.BoolVal ? 1 : 0;
				case NodeType.Decimal: return (double)(decimal)Oval!;
				default: return 0;
			}
		}
		set {
			Type = NodeType.Double;
			Val = DWord.Make(value);
			Oval = null;
		}
	}

	public decimal AsDecimal {
		get {
			ParseExpression();
			switch (Type) {
				case NodeType.String: return todecimal((string)Oval);
				case NodeType.Int: return Val.IntVal;
				case NodeType.Long: return Val.LongVal;
				case NodeType.Float: return (decimal)Val.FloatVal;
				case NodeType.Double: return (decimal)Val.DoubleVal;
				case NodeType.Bool: return Val.BoolVal ? 1 : 0;
				case NodeType.Decimal: return (decimal)Oval!;
				default: return 0;
			}
		}
		set {
			Type = NodeType.Decimal;
			Oval = value;
			Val = default;
		}
	}

	public DateTime AsDateTime {
		get {
			ParseExpression();
			switch (Type) {
				case NodeType.String: return timeval((string)Oval);
				case NodeType.DateTime: return Val.TimeVal;
				default: return EPOCH;
			}
		}
		set {
			Type = NodeType.DateTime;
			Val = DWord.Make(value);
			Oval = null;
		}
	}

	public bool AsBool {
		get {
			ParseExpression();
			switch (Type) {
				case NodeType.String:
					var s = (string)Oval;
					return !string.IsNullOrEmpty(s) && s != "0";
				case NodeType.Int: return Val.IntVal != 0;
				case NodeType.Long: return Val.LongVal != 0;
				case NodeType.Float: return Val.FloatVal != 0;
				case NodeType.Double: return Val.DoubleVal != 0;
				case NodeType.Bool: return Val.BoolVal;
				case NodeType.Decimal: return (decimal)Oval! != 0;
				case NodeType.Array: return ((JsonList<JSONF>)Oval!).Count > 0;
				case NodeType.Object: return ((JsonMap<string, JSONF>)Oval!).Count > 0;
				default: return false;
			}
		}
		set {
			Type = NodeType.Bool;
			Val = DWord.Make(value);
			Oval = null;
		}
	}

	static readonly byte[] emptybytes = Array.Empty<byte>();

	public byte[] AsByteArray {
		get => Oval is byte[] b ? b : emptybytes;
		set {
			Type = NodeType.ByteArray;
			Oval = value;
			Val = default;
		}
	}

	public JSONF AsObject => Type == NodeType.Object ? this : newObject();
	public JSONF AsArray => Type == NodeType.Array ? this : newArray();

	public ref JSONF this[int index] {
		get {
			if (Oval is JsonList<JSONF> arr && index >= 0 && index < arr.Count)
				return ref arr[index];
			var x = new JSONF[1];
			return ref x[0];
		}
	}

	public ref JSONF this[string key] {
		get {
			if (Oval is JsonMap<string, JSONF> map) {
				if (map.TryGetValue(key, out _)) return ref map[key];
				map.Add(key, NULL);
				return ref map[key];
			}
			var x = new JSONF[1];
			return ref x[0];
		}
	}

	public JSONF get(int index, JSONF def = default) {
		if (Oval is JsonList<JSONF> arr && index >= 0 && index < arr.Count) return arr[index];
		return def;
	}

	public JSONF get(string key, JSONF def = default) {
		if (Oval is JsonMap<string, JSONF> map && map.TryGetValue(key, out var v)) return v;
		return def;
	}

	public bool ContainsKey(string key) => Oval is JsonMap<string, JSONF> map && map.ContainsKey(key);

	/// <summary>写一个键并返回自身，便于链式拼对象。</summary>
	public JSONF Set(string key, JSONF value) {
		if (Type == NodeType.Object) this[key] = value;
		return this;
	}

	/// <summary>追加一项并返回自身，便于链式拼数组。</summary>
	public JSONF Push(JSONF value) {
		if (Type == NodeType.Array) Add(value);
		return this;
	}

	public bool IsString => Type == NodeType.String;
	public bool IsNumber => Type is NodeType.Int or NodeType.Long or NodeType.Float or NodeType.Double or NodeType.Decimal;
	public bool IsBool => Type == NodeType.Bool;
	public bool IsTrue => Type == NodeType.Bool && Val.BoolVal;
	public bool IsFalse => Type == NodeType.Bool && !Val.BoolVal;

	/// <summary>取子项；不存在返回 false，value 为未定义节点。</summary>
	public bool tryget(string key, out JSONF value) {
		if (Oval is JsonMap<string, JSONF> map && map.TryGetValue(key, out value)) return true;
		value = default;
		return false;
	}

	public bool tryget(int index, out JSONF value) {
		if (Oval is JsonList<JSONF> arr && index >= 0 && index < arr.Count) {
			value = arr[index];
			return true;
		}
		value = default;
		return false;
	}

	/// <summary>按整数取值；非数字返回 false。</summary>
	public bool tryint(out int value) {
		value = 0;
		if (Type == NodeType.String)
			return int.TryParse((string)Oval, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
		if (!IsNumber) return false;
		value = AsInt;
		return true;
	}

	public bool trylong(out long value) {
		value = 0;
		if (Type == NodeType.String)
			return long.TryParse((string)Oval, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
		if (!IsNumber) return false;
		value = AsLong;
		return true;
	}

	public bool trydouble(out double value) {
		value = 0;
		if (Type == NodeType.String)
			return double.TryParse((string)Oval, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
		if (!IsNumber) return false;
		value = AsDouble;
		return true;
	}

	/// <summary>取字符串；键不存在或为 null 时给默认值（默认 null）。</summary>
	public string Str(string key, string def = null) {
		var v = get(key);
		return v.IsNull ? def : v.Value;
	}

	public int Int(string key, int def = 0) {
		var v = get(key);
		return v.IsNull ? def : v.AsInt;
	}

	public long Long(string key, long def = 0) {
		var v = get(key);
		return v.IsNull ? def : v.AsLong;
	}

	public double Double(string key, double def = 0) {
		var v = get(key);
		return v.IsNull ? def : v.AsDouble;
	}

	public bool Bool(string key, bool def = false) {
		var v = get(key);
		return v.IsNull ? def : v.AsBool;
	}

	public void Add(string key, JSONF value) {
		if (Type != NodeType.Object || value.Type == NodeType.NotExist) return;
		if (Oval is JsonMap<string, JSONF> map) map.Add(key, value);
	}

	public void Add(JSONF value) {
		if (Type != NodeType.Array || value.Type == NodeType.NotExist) return;
		if (Oval is JsonList<JSONF> arr) arr.Add(value);
	}

	public bool Remove(string key) {
		if (Oval is JsonMap<string, JSONF> map) return map.Remove(key);
		return false;
	}

	public void Insert(int index, JSONF value) {
		if (Oval is JsonList<JSONF> arr) arr.Insert(index, value);
	}

	public void RemoveAt(int index) {
		if (Oval is JsonList<JSONF> arr) arr.RemoveAt(index);
	}

	public void Clear() {
		if (Oval is JsonList<JSONF> arr) arr.Clear();
		else if (Oval is JsonMap<string, JSONF> map) map.Clear();
	}

	public void EnsureCapacity(int capacity) {
		if (Oval is JsonList<JSONF> arr) arr.Capacity = capacity;
		else if (Oval is JsonMap<string, JSONF> map) map.Capacity = capacity;
	}

	public static JSONF concat(JSONF arr, params JSONF[] arrs) {
		foreach (var a in arrs)
			foreach (var v in a) arr.Add(v);
		return arr;
	}

	public JSONF concat(params JSONF[] arrs) => concat(this, arrs);

	public static JSONF extend(JSONF obj, params JSONF[] objs) {
		foreach (var o in objs)
			foreach (var (k, v) in o.KeyVals) obj[k] = v;
		return obj;
	}

	public JSONF extend(params JSONF[] objs) => extend(this, objs);

	public JSONF map(Func<JSONF, string, int, JSONF> fn) {
		var arr = newArray(Count);
		if (Type == NodeType.Array) {
			for (var i = 0; i < Count; i++) arr.Add(fn(this[i], "", i));
		}
		else {
			var i = 0;
			foreach (var (k, val) in KeyVals) {
				arr.Add(fn(val, k, i));
				i++;
			}
		}
		return arr;
	}

	public JSONF map(Func<JSONF, JSONF> fn) {
		var arr = newArray(Count);
		if (Type == NodeType.Array) {
			for (var i = 0; i < Count; i++) arr.Add(fn(this[i]));
		}
		else {
			foreach (var (_, val) in KeyVals) arr.Add(fn(val));
		}
		return arr;
	}

	public JSONF mapToObj(Func<JSONF, string, int, (string, JSONF)> fn) {
		var obj = newObject(Count);
		if (Type == NodeType.Array) {
			for (var i = 0; i < Count; i++) {
				var (k, v) = fn(this[i], "", i);
				if (k != null) obj.Add(k, v);
			}
		}
		else {
			var i = 0;
			foreach (var (k, val) in KeyVals) {
				var (nk, v) = fn(val, k, i);
				if (nk != null) obj.Add(nk, v);
				i++;
			}
		}
		return obj;
	}

	public JSONF mapToObj(Func<JSONF, (string, JSONF)> fn) {
		var obj = newObject(Count);
		if (Type == NodeType.Array) {
			for (var i = 0; i < Count; i++) {
				var (k, v) = fn(this[i]);
				if (k != null) obj.Add(k, v);
			}
		}
		else {
			foreach (var (_, val) in KeyVals) {
				var (nk, v) = fn(val);
				if (nk != null) obj.Add(nk, v);
			}
		}
		return obj;
	}

	public void FillValue(JSONF val) {
		if (Type == NodeType.Array) {
			for (var i = 0; i < Count; i++) this[i] = val;
		}
		else {
			foreach (var k in Keys) this[k] = val;
		}
	}

	public JSONF Clone {
		get {
			switch (Type) {
				case NodeType.Array:
					var arr = newArray(Count);
					foreach (var v in this) arr.Add(v.Clone);
					return arr;
				case NodeType.Object:
					var obj = newObject(Count);
					foreach (var (k, v) in KeyVals) obj.Add(k, v.Clone);
					return obj;
				case NodeType.ByteArray:
					var src = AsByteArray;
					var copy = new byte[src.Length];
					Array.Copy(src, copy, src.Length);
					return copy;
				default:
					return this;
			}
		}
	}

	public static JSONF newArray(int capacity = 0) => new JSONF(NodeType.Array, default, new JsonList<JSONF>(capacity));
	public static JSONF newArray(params JSONF[] items) {
		var list = new JsonList<JSONF>(items.Length);
		foreach (var item in items) {
			if (item.Type != NodeType.Ignore && item.Type != NodeType.NotExist)
				list.Add(item);
		}
		return new JSONF(NodeType.Array, default, list);
	}

	public static JSONF newArrayWithVals(int len, JSONF val) {
		var arr = newArray(len);
		for (var i = 0; i < len; i++) arr.Add(val);
		return arr;
	}

	public static JSONF newObject(int capacity = 0, IEqualityComparer<string> scmp = null) =>
		new JSONF(NodeType.Object, default, new JsonMap<string, JSONF>(capacity, scmp));

	public static JSONF newObject(params (string, JSONF)[] items) {
		var map = new JsonMap<string, JSONF>(items.Length);
		foreach (var (k, v) in items) {
			if (v.Type != NodeType.Ignore && v.Type != NodeType.NotExist)
				map.Add(k, v);
		}
		return new JSONF(NodeType.Object, default, map);
	}

	public static JSONF NULL => new JSONF(NodeType.Null, default, null);
	public static JSONF IGNORE => new JSONF(NodeType.Ignore, default, null);
	public static JSONF newData(int v) => new JSONF(NodeType.Int, DWord.Make(v), null);
	public static JSONF newData(long v) => new JSONF(NodeType.Long, DWord.Make(v), null);
	public static JSONF newData(ulong v) => v <= long.MaxValue ? newData((long)v) : newData(v.ToString(CultureInfo.InvariantCulture));
	public static JSONF newData(float v) => new JSONF(NodeType.Float, DWord.Make(v), null);
	public static JSONF newData(double v) => new JSONF(NodeType.Double, DWord.Make(v), null);
	public static JSONF newData(DateTime v) => new JSONF(NodeType.DateTime, DWord.Make(v), null);
	public static JSONF newData(bool v) => new JSONF(NodeType.Bool, DWord.Make(v), null);
	public static JSONF newData(decimal v) => new JSONF(NodeType.Decimal, default, v);
	public static JSONF newData(string v) => v == null ? NULL : new JSONF(NodeType.String, default, v);
	public static JSONF newData(byte[] v) => new JSONF(NodeType.ByteArray, default, v);
	public static JSONF newCustomData(object v) => new JSONF(NodeType.CustomObject, default, v);

	public static implicit operator JSONF(bool v) => newData(v);
	public static implicit operator JSONF(int v) => newData(v);
	public static implicit operator JSONF(long v) => newData(v);
	public static implicit operator JSONF(float v) => newData(v);
	public static implicit operator JSONF(double v) => newData(v);
	public static implicit operator JSONF(DateTime v) => newData(v);
	public static implicit operator JSONF(decimal v) => newData(v);
	public static implicit operator JSONF(string v) => newData(v);
	public static implicit operator JSONF(byte[] v) => newData(v);
	public static implicit operator string(JSONF v) => v.Value;
	public static explicit operator bool(JSONF v) => v.AsBool;
	public static explicit operator int(JSONF v) => v.AsInt;
	public static explicit operator long(JSONF v) => v.AsLong;
	public static explicit operator float(JSONF v) => v.AsFloat;
	public static explicit operator double(JSONF v) => v.AsDouble;
	public static explicit operator decimal(JSONF v) => v.AsDecimal;
	public static explicit operator DateTime(JSONF v) => v.AsDateTime;
	public static explicit operator byte[](JSONF v) => v.AsByteArray;

	/// <summary>标量取值（装箱）；数组和对象返回节点本身。</summary>
	public object GetValueObject() {
		switch (Type) {
			case NodeType.String: return Value;
			case NodeType.Int: return Val.IntVal;
			case NodeType.Long: return Val.LongVal;
			case NodeType.Float: return Val.FloatVal;
			case NodeType.Double: return Val.DoubleVal;
			case NodeType.Bool: return Val.BoolVal;
			case NodeType.Decimal: return (decimal)Oval!;
			case NodeType.DateTime: return Val.TimeVal;
			case NodeType.ByteArray: return Oval;
			default: return this;
		}
	}

	/// <summary>普通对象（POCO、字典、集合）→ JSONF。skipNull 不写 null，skipDefault 连默认值也不写，camel 把字段名首字母改小写。</summary>
	public static JSONF FromObject(object v, bool skipNull = false, bool skipDefault = false, bool camel = false) {
		if (v == null) return NULL;
		if (v is JSONF jf) return jf;
		if (v is string s) return newData(s);
		if (v is bool b) return newData(b);
		if (v is int i) return newData(i);
		if (v is long l) return newData(l);
		if (v is short sh) return newData((long)sh);
		if (v is byte by) return newData((long)by);
		if (v is sbyte sb) return newData((long)sb);
		if (v is ushort us) return newData((long)us);
		if (v is uint ui) return newData((long)ui);
		if (v is ulong ul) return newData(ul);
		if (v is float f) return newData(f);
		if (v is double d) return newData(d);
		if (v is decimal m) return newData(m);
		if (v is DateTime dt) return newData(dt);
		if (v is Enum e) return newData(Convert.ToInt64(e, CultureInfo.InvariantCulture));
		if (v is IDictionary dict) {
			var o = newObject();
			foreach (DictionaryEntry de in dict)
				o.Add(de.Key?.ToString() ?? "", FromObject(de.Value, skipNull, skipDefault, camel));
			return o;
		}
		if (v is IEnumerable en) {
			var a = newArray();
			foreach (var item in en) a.Add(FromObject(item, skipNull, skipDefault, camel));
			return a;
		}
		var t = v.GetType();
		var obj = newObject();
		foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
			if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
			if (p.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
			var pv = p.GetValue(v);
			if (skipval(pv, p.PropertyType, skipNull, skipDefault)) continue;
			obj[camel ? camelname(p.Name) : p.Name] = FromObject(pv, skipNull, skipDefault, camel);
		}
		foreach (var fld in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
			if (fld.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
			var fv = fld.GetValue(v);
			if (skipval(fv, fld.FieldType, skipNull, skipDefault)) continue;
			obj[camel ? camelname(fld.Name) : fld.Name] = FromObject(fv, skipNull, skipDefault, camel);
		}
		return obj;
	}

	static string camelname(string name) =>
		name.Length == 0 || char.IsLower(name[0]) ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

	static bool skipval(object v, Type t, bool skipNull, bool skipDefault) {
		if (v == null) return skipNull;
		if (!skipDefault || !t.IsValueType) return false;
		return v.Equals(Activator.CreateInstance(t));
	}

	/// <summary>按类型还原对象；键先按原名找，再按不区分大小写找。</summary>
	public object ToObject(Type t) {
		if (t == typeof(JSONF)) return this;
		if (t == typeof(string)) return IsNull ? null : Value;
		if (t == typeof(object)) return GetValueObject();
		var u = Nullable.GetUnderlyingType(t);
		if (u != null) return IsNull ? null : ToObject(u);
		if (t == typeof(bool)) return AsBool;
		if (t == typeof(int)) return AsInt;
		if (t == typeof(long)) return AsLong;
		if (t == typeof(short)) return (short)AsInt;
		if (t == typeof(byte)) return (byte)AsInt;
		if (t == typeof(sbyte)) return (sbyte)AsInt;
		if (t == typeof(ushort)) return (ushort)AsInt;
		if (t == typeof(uint)) return (uint)AsLong;
		if (t == typeof(ulong)) return (ulong)AsLong;
		if (t == typeof(float)) return AsFloat;
		if (t == typeof(double)) return AsDouble;
		if (t == typeof(decimal)) return AsDecimal;
		if (t == typeof(DateTime)) return AsDateTime;
		if (t.IsEnum) return Enum.ToObject(t, AsLong);
		if (t.IsArray) {
			var et = t.GetElementType();
			var arr = Array.CreateInstance(et, Count);
			for (var i = 0; i < Count; i++) arr.SetValue(get(i).ToObject(et), i);
			return arr;
		}
		if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) {
			var et = t.GetGenericArguments()[0];
			var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(et));
			foreach (var item in this) list.Add(item.ToObject(et));
			return list;
		}
		if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>)) {
			var kt = t.GetGenericArguments()[0];
			var vt = t.GetGenericArguments()[1];
			var dd = (IDictionary)Activator.CreateInstance(t);
			foreach (var (k, val) in KeyVals) {
				var key = kt == typeof(string) ? k : Convert.ChangeType(k, kt, CultureInfo.InvariantCulture);
				dd[key] = val.ToObject(vt);
			}
			return dd;
		}
		if (IsNull) return null;
		var obj = Activator.CreateInstance(t);
		foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
			if (!p.CanWrite || p.GetIndexParameters().Length > 0) continue;
			if (p.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
			if (!trykey(p.Name, out var pv)) continue;
			p.SetValue(obj, pv.ToObject(p.PropertyType));
		}
		foreach (var fld in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
			if (fld.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
			if (!trykey(fld.Name, out var fv)) continue;
			fld.SetValue(obj, fv.ToObject(fld.FieldType));
		}
		return obj;
	}

	public T ToObject<T>() => (T)ToObject(typeof(T));

	bool trykey(string name, out JSONF value) {
		if (ContainsKey(name)) {
			value = get(name);
			return true;
		}
		foreach (var k in Keys) {
			if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) {
				value = get(k);
				return true;
			}
		}
		value = default;
		return false;
	}

	public string RoundTo(int dec, bool trailingzero = false) {
		if (dec < 0) dec = 0;
		var v = Math.Round(AsDouble, dec);
		var fmt = trailingzero ? "F" + dec : dec <= 0 ? "0" : "0." + new string('#', dec);
		return v.ToString(fmt, CultureInfo.InvariantCulture);
	}

	public IEnumerator<JSONF> GetEnumerator() {
		if (Oval is JsonList<JSONF> arr) {
			foreach (var v in arr) yield return v;
		}
		else if (Oval is JsonMap<string, JSONF> map) {
			foreach (var v in map) yield return v.Item2;
		}
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	public IEnumerable<JSONF> Vals {
		get {
			if (Oval is JsonList<JSONF> arr) {
				foreach (var v in arr) yield return v;
			}
			else if (Oval is JsonMap<string, JSONF> map) {
				foreach (var v in map) yield return v.Item2;
			}
		}
	}

	public IEnumerable<string> Keys {
		get {
			if (Oval is JsonMap<string, JSONF> map) {
				foreach (var k in map.Keys) yield return k;
			}
		}
	}

	public IEnumerable<(string Name, JSONF Value)> KeyVals {
		get {
			if (Oval is JsonMap<string, JSONF> map) {
				foreach (var v in map) yield return v;
			}
		}
	}

	public override string ToString() => ToString(-1);

	public string ToString(int pre = -1) {
		if (Type is NodeType.Array or NodeType.Object) return ToJSONString(pre);
		return Value;
	}

	public string ToJSONString(int pre = -1) {
		var sb = new StringBuilder(256);
		writejson(sb, pre, 0);
		return sb.ToString();
	}

	public void ToJSONString(Action<string> write, int pre = -1) => write(ToJSONString(pre));

	void writejson(StringBuilder sb, int pre, int depth) {
		switch (Type) {
			case NodeType.Array:
				var arr = (JsonList<JSONF>)Oval!;
				if (arr.Count == 0) {
					sb.Append("[]");
					break;
				}
				sb.Append('[');
				if (pre >= 0) {
					sb.Append("\r\n");
					indent(sb, depth + 1);
				}
				for (var i = 0; i < arr.Count; i++) {
					if (i > 0) {
						sb.Append(',');
						if (pre >= 0) {
							sb.Append("\r\n");
							indent(sb, depth + 1);
						}
					}
					arr[i].writejson(sb, pre, depth + 1);
				}
				if (pre >= 0) {
					sb.Append("\r\n");
					indent(sb, depth);
				}
				sb.Append(']');
				break;
			case NodeType.Object:
				var map = (JsonMap<string, JSONF>)Oval!;
				if (map.Count == 0) {
					sb.Append("{}");
					break;
				}
				sb.Append('{');
				if (pre >= 0) {
					sb.Append("\r\n");
					indent(sb, depth + 1);
				}
				var first = true;
				foreach (var key in map.Keys) {
					if (!first) {
						sb.Append(',');
						if (pre >= 0) {
							sb.Append("\r\n");
							indent(sb, depth + 1);
						}
					}
					first = false;
					sb.Append('"');
					escape(sb, key);
					sb.Append("\":");
					if (pre >= 0) sb.Append(' ');
					map[key].writejson(sb, pre, depth + 1);
				}
				if (pre >= 0) {
					sb.Append("\r\n");
					indent(sb, depth);
				}
				sb.Append('}');
				break;
			case NodeType.ByteArray:
				sb.Append('"').Append(bin2hex((byte[])Oval!)).Append('"');
				break;
			case NodeType.String:
				sb.Append('"');
				escape(sb, (string)Oval ?? "");
				sb.Append('"');
				break;
			case NodeType.Null:
				sb.Append("null");
				break;
			case NodeType.Bool:
				sb.Append(Val.BoolVal ? "true" : "false");
				break;
			case NodeType.Int:
				sb.Append(Val.IntVal.ToString(CultureInfo.InvariantCulture));
				break;
			case NodeType.Long:
				sb.Append(Val.LongVal.ToString(CultureInfo.InvariantCulture));
				break;
			case NodeType.Float:
				var f = Val.FloatVal;
				if (float.IsNaN(f)) sb.Append("\"NaN\"");
				else if (float.IsPositiveInfinity(f)) sb.Append("\"Inf\"");
				else if (float.IsNegativeInfinity(f)) sb.Append("\"-Inf\"");
				else sb.Append(removetrailingzeros(f.ToString(CultureInfo.InvariantCulture)));
				break;
			case NodeType.Double:
				var d = Val.DoubleVal;
				if (double.IsNaN(d)) sb.Append("\"NaN\"");
				else if (double.IsPositiveInfinity(d)) sb.Append("\"Inf\"");
				else if (double.IsNegativeInfinity(d)) sb.Append("\"-Inf\"");
				else sb.Append(removetrailingzeros(d.ToString(CultureInfo.InvariantCulture)));
				break;
			case NodeType.Decimal:
				sb.Append(removetrailingzeros(((decimal)Oval!).ToString(CultureInfo.InvariantCulture)));
				break;
			case NodeType.DateTime:
				sb.Append('"').Append(Val.TimeVal.ToString(DATEFMT, CultureInfo.InvariantCulture)).Append('"');
				break;
			case NodeType.Expression:
				sb.Append((string)Oval ?? "");
				break;
			default:
				sb.Append("\"\"");
				break;
		}
	}

	static void indent(StringBuilder sb, int depth) {
		for (var i = 0; i < depth; i++) sb.Append("  ");
	}

	static void escape(StringBuilder sb, string s) {
		if (s == null) return;
		foreach (var c in s) {
			switch (c) {
				case '\\': sb.Append("\\\\"); break;
				case '"': sb.Append("\\\""); break;
				case '\n': sb.Append("\\n"); break;
				case '\r': sb.Append("\\r"); break;
				case '\t': sb.Append("\\t"); break;
				case '\b': sb.Append("\\b"); break;
				case '\f': sb.Append("\\f"); break;
				default:
					if (char.IsControl(c)) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
					else sb.Append(c);
					break;
			}
		}
	}

	/// <summary>宽松解析。键可不加引号，支持 // 与 /* */ 注释、尾逗号。</summary>
	/// <summary>从流解析（按 UTF-8 读到底，流不关闭）。</summary>
	public static JSONF Parse(System.IO.Stream stream) {
		using var sr = new System.IO.StreamReader(stream, Encoding.UTF8, true, 8192, leaveOpen: true);
		return Parse(sr.ReadToEnd());
	}

	public static JSONF Parse(string aJSON, bool runexp = true) {
		if (aJSON == null) return "";
		var stack = new Stack<JSONF>();
		var ctx = new JSONF(NodeType.Null, default, null);
		var i = 0;
		var sToken = new StringBuilder(64);
		var tokenName = "";
		var quoteMode = 0;
		while (i < aJSON.Length) {
			switch (aJSON[i]) {
				case '/':
					if (quoteMode == 0 && i + 1 < aJSON.Length) {
						if (aJSON[i + 1] == '/') {
							i += 2;
							while (i < aJSON.Length && aJSON[i] != '\n') i++;
							i--;
							break;
						}
						if (aJSON[i + 1] == '*') {
							i += 2;
							while (i + 1 < aJSON.Length) {
								if (aJSON[i] == '*' && aJSON[i + 1] == '/') {
									i += 2;
									break;
								}
								i++;
							}
							i--;
							break;
						}
					}
					sToken.Append(aJSON[i]);
					break;
				case '{':
					if (quoteMode == 1) {
						sToken.Append(aJSON[i]);
						break;
					}
					stack.Push(newObject());
					if (ctx.Type != NodeType.Null) {
						tokenName = tokenName.Trim();
						if (ctx.Oval is JsonList<JSONF> arr) arr.Add(stack.Peek());
						else if (tokenName != "") ctx.Add(tokenName, stack.Peek());
					}
					tokenName = "";
					sToken.Clear();
					quoteMode = 0;
					ctx = stack.Peek();
					break;
				case '[':
					if (quoteMode == 1) {
						sToken.Append(aJSON[i]);
						break;
					}
					stack.Push(newArray());
					if (ctx.Type != NodeType.Null) {
						tokenName = tokenName.Trim();
						if (ctx.Oval is JsonList<JSONF> arr) arr.Add(stack.Peek());
						else if (tokenName != "") ctx.Add(tokenName, stack.Peek());
					}
					tokenName = "";
					sToken.Clear();
					quoteMode = 0;
					ctx = stack.Peek();
					break;
				case '}':
				case ']':
					if (quoteMode == 1) {
						sToken.Append(aJSON[i]);
						break;
					}
					if (stack.Count == 0) return default;
					stack.Pop();
					tokenName = tokenName.Trim();
					if (sToken.Length > 0 || quoteMode == 2) {
						if (ctx.Oval is JsonList<JSONF> arr) arr.Add(t1(sToken.ToString(), quoteMode, runexp));
						else ctx.Add(tokenName, t1(sToken.ToString(), quoteMode, runexp));
					}
					tokenName = "";
					sToken.Clear();
					quoteMode = 0;
					if (stack.Count > 0) ctx = stack.Peek();
					break;
				case ':':
					if (quoteMode == 1) {
						sToken.Append(aJSON[i]);
						break;
					}
					tokenName = sToken.ToString();
					sToken.Clear();
					quoteMode = 0;
					break;
				case '"':
					quoteMode = quoteMode == 0 ? 1 : 2;
					break;
				case ',':
					if (quoteMode == 1) {
						sToken.Append(aJSON[i]);
						break;
					}
					tokenName = tokenName.Trim();
					if (sToken.Length > 0 || quoteMode == 2) {
						if (ctx.Oval is JsonList<JSONF> arr) arr.Add(t1(sToken.ToString(), quoteMode, runexp));
						else if (tokenName != "") ctx.Add(tokenName, t1(sToken.ToString(), quoteMode, runexp));
					}
					tokenName = "";
					sToken.Clear();
					quoteMode = 0;
					break;
				case '\r':
				case '\n':
					break;
				case ' ':
				case '\t':
					if (quoteMode == 1) sToken.Append(aJSON[i]);
					break;
				case '\\':
					if (i + 1 >= aJSON.Length) break;
					i++;
					if (quoteMode == 1) {
						var c = aJSON[i];
						switch (c) {
							case 't': sToken.Append('\t'); break;
							case 'r': sToken.Append('\r'); break;
							case 'n': sToken.Append('\n'); break;
							case 'b': sToken.Append('\b'); break;
							case 'f': sToken.Append('\f'); break;
							case 'u':
								if (i + 4 < aJSON.Length) {
									var hex = aJSON.Substring(i + 1, 4);
									try {
										sToken.Append((char)int.Parse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
									}
									catch { }
									i += 4;
								}
								break;
							default: sToken.Append(c); break;
						}
					}
					break;
				default:
					sToken.Append(aJSON[i]);
					break;
			}
			i++;
		}
		if (quoteMode == 1) return "";
		if (ctx.Type == NodeType.Null) ctx.Value = sToken.ToString();
		return ctx;
	}

	void ParseExpression() {
		if (Type != NodeType.Expression) return;
		var s = (string)Oval;
		switch (s) {
			case "true":
				Type = NodeType.Bool;
				Val = DWord.Make(true);
				Oval = null;
				return;
			case "false":
				Type = NodeType.Bool;
				Val = DWord.Make(false);
				Oval = null;
				return;
			case "null":
				SetNull();
				return;
			case "NaN":
				Type = NodeType.Float;
				Val = DWord.Make(float.NaN);
				Oval = null;
				return;
			case "Inf":
				Type = NodeType.Float;
				Val = DWord.Make(float.PositiveInfinity);
				Oval = null;
				return;
			case "-Inf":
				Type = NodeType.Float;
				Val = DWord.Make(float.NegativeInfinity);
				Oval = null;
				return;
		}
		var t = s.Trim();
		if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv)) {
			Type = NodeType.Int;
			Val = DWord.Make(iv);
			Oval = null;
			return;
		}
		if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lv)) {
			Type = NodeType.Long;
			Val = DWord.Make(lv);
			Oval = null;
			return;
		}
		if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv)) {
			Type = NodeType.Double;
			Val = DWord.Make(dv);
			Oval = null;
			return;
		}
		Type = NodeType.String;
		Oval = s;
	}

	static JSONF t1(string s, int quotemode = 0, bool runexp = false) {
		if (quotemode != 0) return s;
		switch (s) {
			case "true": return true;
			case "false": return false;
			case "null": return NULL;
			default:
				var d = new JSONF(NodeType.Expression, default, s);
				if (runexp) d.ParseExpression();
				return d;
		}
	}

	static readonly HashSet<Type> jsondatatypes = new() {
		typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
		typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal),
		typeof(DateTime), typeof(bool), typeof(string), typeof(byte[]),
	};

	public static bool IsJSONDataType(object o) => o == null || jsondatatypes.Contains(o.GetType());

	public static JSONF ToJSONData(object o) {
		if (o == null) return NULL;
		if (o is byte[] bytes) return newData(bytes);
		switch (System.Type.GetTypeCode(o.GetType())) {
			case TypeCode.Byte: return newData((byte)o);
			case TypeCode.SByte: return newData((sbyte)o);
			case TypeCode.Int16: return newData((short)o);
			case TypeCode.UInt16: return newData((ushort)o);
			case TypeCode.Int32: return newData((int)o);
			case TypeCode.UInt32: return newData((uint)o);
			case TypeCode.Int64: return newData((long)o);
			case TypeCode.UInt64: return newData((ulong)o);
			case TypeCode.Single: return newData((float)o);
			case TypeCode.Double: return newData((double)o);
			case TypeCode.Decimal: return newData((decimal)o);
			case TypeCode.DateTime: return newData((DateTime)o);
			case TypeCode.Boolean: return newData((bool)o);
			case TypeCode.String: return newData((string)o);
			default: return NULL;
		}
	}

	// ==================== 小工具 ====================

	const string DATEFMT = "yyyy-MM-dd HH:mm:ss";
	static readonly DateTime EPOCH = new DateTime(1970, 1, 1);

	static int toint(string s) {
		if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
		if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return (int)l;
		if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return (int)d;
		return 0;
	}

	static long tolong(string s) {
		if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
		if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return (long)d;
		return 0;
	}

	static float tofloat(string s) {
		if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
		if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return (float)d;
		return 0;
	}

	static double todouble(string s) {
		if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
		return 0;
	}

	static decimal todecimal(string s) {
		if (decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
		if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return (decimal)d;
		return 0;
	}

	static DateTime timeval(string s) =>
		DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var v) ? v : EPOCH;

	static string removetrailingzeros(string s) {
		if (s.IndexOf('.') < 0) return s;
		var end = s.Length;
		while (end > 0 && s[end - 1] == '0') end--;
		if (end > 0 && s[end - 1] == '.') end--;
		return s.Substring(0, end);
	}

	static string bin2hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
}
