using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Schema;

namespace ScreenKit.Soap;

/// <summary>WSDL 载入后的工作会话：保存模型、参数构建器、当前方法与参数树。</summary>
class WsdlSession {
	public WsdlModel Model;
	public ParamBuilder Builder;
	public string Error;
	public List<string> Messages = new();

	public bool Ok => Model != null && Error == null && Model.Endpoints.Count > 0;

	public static WsdlSession open(string location) {
		var s = new WsdlSession();
		try {
			s.Model = WsdlLoader.load(location);
		} catch (Exception ex) {
			s.Error = ex.Message;
			return s;
		}
		return s.finish();
	}

	public static WsdlSession openText(string text, string baseLocation) {
		var s = new WsdlSession();
		try {
			s.Model = WsdlLoader.loadText(text, baseLocation);
		} catch (Exception ex) {
			s.Error = ex.Message;
			return s;
		}
		return s.finish();
	}

	WsdlSession finish() {
		if (Model.LoadError != null) {
			Error = Model.LoadError;
			return this;
		}
		Messages.AddRange(Model.Warnings);
		if (Model.Schemas != null) Builder = new ParamBuilder(Model.Schemas);
		if (Model.Endpoints.Count == 0) Error = "未解析到可用的 SOAP 端口，请确认 WSDL 是否包含 soap:binding/service";
		return this;
	}

	public IEnumerable<Operation> ops() => Model?.Endpoints.SelectMany(e => e.Ops) ?? Enumerable.Empty<Operation>();

	public Operation findOp(string name) => Model?.findOp(name);

	/// <summary>构造输入参数树（每个 part 一棵）。</summary>
	public List<PNode> buildInput(Operation op) {
		var list = build(op, op.InputParts);
		if (list.Count == 0) {
			// 无 part 或 part 是 type 形式的 rpc 包装
			if (op.InputParts.Count == 0) {
				list.Add(new PNode {
					Name = op.Name, Ns = op.BodyNamespace ?? "", Min = 0, Max = 1,
					TypeName = "（无参数）", IsLeaf = true
				});
			}
		}
		return list;
	}

	public List<PNode> buildOutput(Operation op) => build(op, op.OutputParts);

	List<PNode> build(Operation op, List<Part> parts) {
		var list = new List<PNode>();
		if (Builder == null) return list;
		foreach (var part in parts) {
			PNode node = null;
			if (part.IsElement) node = Builder.fromQualifiedName(new System.Xml.XmlQualifiedName(part.ElementName, part.ElementNs));
			else if (!string.IsNullOrEmpty(part.TypeName)) node = Builder.fromQualifiedName(new System.Xml.XmlQualifiedName(part.TypeName, part.TypeNs));
			if (node == null) {
				node = new PNode {
					Name = string.IsNullOrEmpty(part.ElementName) ? part.Name : part.ElementName,
					Ns = part.ElementNs ?? "",
					TypeName = "（未解析）", IsLeaf = true, Max = 1
				};
			}
			list.Add(node);
		}
		return list;
	}

	/// <summary>模拟响应的默认内层 XML。</summary>
	public string defaultResponseInner(Operation op) {
		var parts = buildOutput(op);
		if (parts.Count == 0) return $"  <{op.Name}Response />";
		var xml = SoapEnvelope.buildResponse(op, parts);
		var body = SoapEnvelope.parse(xml, out _)?.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Body");
		if (body == null) return $"  <{op.Name}Response />";
		var sb = new System.Text.StringBuilder();
		foreach (var el in body.Elements())
			foreach (var line in el.ToString().Split('\n'))
				sb.AppendLine("  " + line.TrimEnd('\r'));
		return sb.ToString().TrimEnd();
	}
}
