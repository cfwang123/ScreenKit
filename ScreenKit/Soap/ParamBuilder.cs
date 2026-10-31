using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Schema;

namespace ScreenKit.Soap;

/// <summary>从编译后的 XmlSchemaSet 推导参数树（纯 XSD 对象模型，不用 SOAP 库）。</summary>
class ParamBuilder {
	static readonly XmlQualifiedName ANY_TYPE = new("anyType", WsdlLoader.NS_XSD);

	readonly XmlSchemaSet set;

	public ParamBuilder(XmlSchemaSet set) { this.set = set; }

	// ==================== 入口 ====================
	#region
	public PNode fromQualifiedName(XmlQualifiedName qn) {
		if (qn == null) return null;
		var el = set.GlobalElements[qn] as XmlSchemaElement;
		if (el != null) return fromElement(el, qn.Name, qn.Namespace, null);
		var type = set.GlobalTypes[qn] as XmlSchemaType;
		if (type != null) return fromType(type, qn.Name, qn.Namespace, null);
		return null;
	}

	public PNode fromElement(XmlSchemaElement el, string name, string parentNs, HashSet<string> stack) {
		if (el == null) return null;
		var real = el;
		if (!el.RefName.IsEmpty) {
			var g = set.GlobalElements[el.RefName] as XmlSchemaElement;
			if (g != null) real = g;
		}
		string localName = !string.IsNullOrEmpty(name) ? name
			: (!real.QualifiedName.IsEmpty ? real.QualifiedName.Name : (real.Name ?? "element"));
		var node = new PNode {
			Name = localName,
			Ns = nsOf(real),
			Min = clamp(real.MinOccurs),
			Max = real.MaxOccursString == "unbounded" ? -1 : clamp(real.MaxOccurs),
			Nillable = real.IsNillable,
			FixedValue = real.FixedValue,
			DefaultValue = real.DefaultValue
		};
		fill(node, resolveType(real), stack);
		return node;
	}

	public PNode fromType(XmlSchemaType type, string name, string parentNs, HashSet<string> stack) {
		var node = new PNode { Name = name, Ns = parentNs, Min = 0, Max = 1 };
		fill(node, type, stack);
		return node;
	}

	static int clamp(decimal v) => v >= int.MaxValue ? int.MaxValue : (int)v;
	#endregion

	// ==================== 递归展开 ====================
	#region
	void fill(PNode node, XmlSchemaType type, HashSet<string> stack) {
		if (type == null) {
			node.IsLeaf = true;
			node.TypeName = "xs:string";
			return;
		}
		node.TypeName = displayType(type);
		if (type.QualifiedName == ANY_TYPE) {
			node.IsLeaf = true;
			node.IsAny = true;
			return;
		}
		if (type is not XmlSchemaComplexType ct || ct.ContentType == XmlSchemaContentType.TextOnly) {
			node.IsLeaf = true;
			return;
		}
		string key = type.QualifiedName.IsEmpty ? node.TypeName + "#" + type.GetHashCode() : type.QualifiedName.ToString();
		if (stack != null && stack.Contains(key)) {
			node.Recursive = true;
			node.IsLeaf = true;
			return;
		}

		var local = stack == null ? new HashSet<string>() : new HashSet<string>(stack);
		local.Add(key);

		var chain = new List<XmlSchemaComplexType>();
		for (XmlSchemaType t = ct; t is XmlSchemaComplexType c2; t = c2.BaseXmlSchemaType) {
			// anyType 是 all 类型的根，其 content 是通配 any，不能算作基类成员
			if (c2.QualifiedName == ANY_TYPE) break;
			chain.Insert(0, c2);
			if (chain.Count > 32) break;
		}
		foreach (var t in chain) {
			addAttributes(node, t);
			collect(node, t.ContentTypeParticle, node.Ns, local);
		}
		if (node.Children.Count == 0) node.IsLeaf = true;
	}

	void addAttributes(PNode node, XmlSchemaComplexType ct) {
		if (ct.AttributeUses == null) return;
		foreach (XmlSchemaAttribute entry in ct.AttributeUses.Values) {
			var a = entry;
			if (!a.RefName.IsEmpty) {
				var g = set.GlobalAttributes[a.RefName] as XmlSchemaAttribute;
				if (g != null) a = g;
			}
			if (a.Use == XmlSchemaUse.Prohibited) continue;
			string nm = !a.QualifiedName.IsEmpty ? a.QualifiedName.Name : a.Name;
			if (string.IsNullOrEmpty(nm)) continue;
			node.Children.Add(new PNode {
				Name = nm,
				Ns = "",
				TypeName = a.AttributeSchemaType != null ? displayType(a.AttributeSchemaType) : "xs:string",
				IsLeaf = true,
				IsAttribute = true,
				Min = a.Use == XmlSchemaUse.Required ? 1 : 0,
				Max = 1,
				FixedValue = a.FixedValue,
				DefaultValue = a.DefaultValue,
				Parent = node
			});
		}
	}

	void collect(PNode parent, XmlSchemaParticle particle, string parentNs, HashSet<string> stack) {
		switch (particle) {
			case null:
				return;
			case XmlSchemaElement el:
				addElement(parent, el, parentNs, stack);
				return;
			case XmlSchemaAny any:
				parent.Children.Add(new PNode {
					Name = "any",
					Ns = any.Namespace ?? "",
					TypeName = "xs:any",
					IsLeaf = true,
					IsAny = true,
					Min = clamp(any.MinOccurs),
					Max = any.MaxOccursString == "unbounded" ? -1 : clamp(any.MaxOccurs),
					Parent = parent
				});
				return;
			case XmlSchemaGroupRef gr:
				collect(parent, gr.Particle, parentNs, stack);
				return;
			case XmlSchemaGroupBase gb:
				foreach (var item in gb.Items) {
					if (item is XmlSchemaParticle p) collect(parent, p, parentNs, stack);
					else if (item is XmlSchemaElement e) addElement(parent, e, parentNs, stack);
				}
				return;
		}
	}

	void addElement(PNode parent, XmlSchemaElement el, string parentNs, HashSet<string> stack) {
		var node = fromElement(el, null, parentNs, stack);
		if (node == null) return;
		node.Parent = parent;
		parent.Children.Add(node);
	}

	XmlSchemaType resolveType(XmlSchemaElement el) {
		if (el.ElementSchemaType != null) return el.ElementSchemaType;
		if (el.SchemaType != null) return el.SchemaType;
		if (!el.SchemaTypeName.IsEmpty) return set.GlobalTypes[el.SchemaTypeName] as XmlSchemaType;
		if (!el.RefName.IsEmpty && set.GlobalElements[el.RefName] is XmlSchemaElement g) return resolveType(g);
		return null;
	}
	#endregion

	// ==================== 辅助 ====================
	#region
	/// <summary>
	/// 元素是否带命名空间：local 元素按 elementFormDefault 决定；
	/// 返回 null 表示沿用父元素命名空间。
	/// </summary>
	static string nsOf(XmlSchemaElement el) {
		if (!el.QualifiedName.IsEmpty) return el.QualifiedName.Namespace ?? "";
		var schema = ownerSchema(el);
		if (schema == null) return null;
		if (schema.ElementFormDefault == XmlSchemaForm.Qualified) return schema.TargetNamespace ?? "";
		return "";   // unqualified：子元素不带命名空间
	}

	static XmlSchema ownerSchema(XmlSchemaObject obj) {
		for (var p = obj?.Parent; p != null; p = p.Parent)
			if (p is XmlSchema s) return s;
		return null;
	}

	static string displayType(XmlSchemaType type) {
		if (type == null) return "xs:string";
		if (!type.QualifiedName.IsEmpty) {
			var qn = type.QualifiedName;
			return qn.Namespace == WsdlLoader.NS_XSD ? "xs:" + qn.Name : qn.Name;
		}
		if (type is XmlSchemaComplexType) return "complex";
		return string.IsNullOrEmpty(type.Name) ? "complex" : type.Name;
	}
	#endregion
}
