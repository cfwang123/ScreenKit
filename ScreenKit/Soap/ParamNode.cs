using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace ScreenKit.Soap;

/// <summary>参数树节点：由 XSD 推导，供界面编辑、序列化成 SOAP 报文。</summary>
public class PNode : INotifyPropertyChanged {
	string value = "";
	string rawName = "";
	int occurs = 1;

	public string Name {
		get => rawName;
		set { rawName = value; notify(nameof(Name)); }
	}

	public string Ns { get; set; }                 // null=继承父级，""=无命名空间
	public string TypeName { get; set; } = "";
	public bool IsLeaf { get; set; }
	public bool IsAttribute { get; set; }
	public bool IsAny { get; set; }
	public string FixedValue { get; set; }
	public string DefaultValue { get; set; }
	public bool Nillable { get; set; }
	public int Min { get; set; }
	public int Max { get; set; } = 1;
	public bool Recursive { get; set; }
	public bool IsSynthetic { get; set; }
	public List<PNode> Children { get; set; } = new();
	public PNode Parent { get; set; }

	public string Value {
		get => value;
		set { this.value = value ?? ""; notify(nameof(Value)); notify(nameof(Preview)); }
	}

	/// <summary>数组元素重复次数（界面可改）。</summary>
	public int Occurs {
		get => occurs;
		set {
			int v = value < 1 ? 1 : value;
			if (Max > 1 && v > Max) v = Max;
			if (Max < 0) { /* 不限 */ }
			occurs = v;
			notify(nameof(Occurs));
			notify(nameof(Preview));
		}
	}

	public bool IsArray => Max != 1;

	public string Preview {
		get {
			if (!string.IsNullOrEmpty(value)) return IsArray && occurs > 1 ? $"{value} ×{occurs}" : value;
			if (FixedValue != null) return FixedValue;
			if (DefaultValue != null) return DefaultValue;
			return "";
		}
	}

	public string Hint {
		get {
			var parts = new List<string>();
			if (!string.IsNullOrEmpty(TypeName)) parts.Add(TypeName);
			if (FixedValue != null) parts.Add($"固定 {FixedValue}");
			else if (DefaultValue != null) parts.Add($"默认 {DefaultValue}");
			if (Nillable) parts.Add("可空");
			if (IsArray) parts.Add(Max < 0 ? "可重复" : $"最多 {Max} 次");
			if (IsAttribute) parts.Add("属性");
			if (Recursive) parts.Add("递归");
			return string.Join("，", parts);
		}
	}

	public event PropertyChangedEventHandler PropertyChanged;
	void notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

	public PNode clone() {
		var n = new PNode {
			rawName = rawName, Ns = Ns, TypeName = TypeName, IsLeaf = IsLeaf, IsAttribute = IsAttribute,
			IsAny = IsAny, value = value, FixedValue = FixedValue, DefaultValue = DefaultValue,
			Nillable = Nillable, Min = Min, Max = Max, Recursive = Recursive, IsSynthetic = IsSynthetic,
			occurs = occurs
		};
		foreach (var c in Children) {
			var cc = c.clone();
			cc.Parent = n;
			n.Children.Add(cc);
		}
		return n;
	}

	public void resetValues() {
		if (IsLeaf) Value = FixedValue ?? "";
		if (IsArray) Occurs = 1;
		foreach (var c in Children) c.resetValues();
	}

	public IEnumerable<PNode> walk() {
		yield return this;
		foreach (var c in Children)
			foreach (var x in c.walk()) yield return x;
	}
}
