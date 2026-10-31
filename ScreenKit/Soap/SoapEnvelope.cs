using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ScreenKit.Soap;

/// <summary>SOAP 报文生成/解析。按 1.1 / 1.2 生成不同 envelope 与 Content-Type。</summary>
static class SoapEnvelope {
	public const string NS_SOAP11 = "http://schemas.xmlsoap.org/soap/envelope/";
	public const string NS_SOAP12 = "http://www.w3.org/2003/05/soap-envelope";
	public const string NS_XSI = "http://www.w3.org/2001/XMLSchema-instance";
	public const string NS_XSD = "http://www.w3.org/2001/XMLSchema";

	public static string envelopeNs(string version) => version == "1.2" ? NS_SOAP12 : NS_SOAP11;

	public static string contentType(string version) =>
		version == "1.2" ? "application/soap+xml; charset=utf-8" : "text/xml; charset=utf-8";

	// ==================== Envelope 骨架 ====================
	#region
	static string buildEnvelope(string version, Action<XmlWriter, PrefixMap> writeBody) {
		var sb = new StringBuilder();
		var settings = new XmlWriterSettings {
			Indent = true,
			IndentChars = "  ",
			Encoding = new UTF8Encoding(false),
			OmitXmlDeclaration = false
		};
		using (var w = XmlWriter.Create(sb, settings)) {
			string env = envelopeNs(version);
			var prefixes = new PrefixMap();
			w.WriteStartDocument();
			w.WriteStartElement("soapenv", "Envelope", env);
			w.WriteAttributeString("xmlns", "xsi", null, NS_XSI);
			w.WriteAttributeString("xmlns", "xsd", null, NS_XSD);
			w.WriteStartElement("soapenv", "Body", env);
			writeBody(w, prefixes);
			w.WriteEndElement();
			w.WriteEndElement();
			w.WriteEndDocument();
		}
		// XmlWriter 写 StringBuilder 时声明固定为 utf-16，实际发送时是 utf-8
		return sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"");
	}

	/// <summary>命名空间 → 前缀（XmlWriter 会按需自动输出 xmlns 声明）。</summary>
	public sealed class PrefixMap {
		readonly Dictionary<string, string> known = new();
		int seq;

		public PrefixMap() {
			known[NS_XSI] = "xsi";
			known[NS_XSD] = "xsd";
			known[NS_SOAP11] = "soapenv";
			known[NS_SOAP12] = "soapenv";
		}

		public string prefixFor(string ns) {
			if (string.IsNullOrEmpty(ns)) return "";
			if (known.TryGetValue(ns, out var p)) return p;
			string prefix = $"ns{seq++}";
			known[ns] = prefix;
			return prefix;
		}
	}
	#endregion

	// ==================== 请求/响应报文 ====================
	#region
	/// <summary>把参数树序列化为完整 SOAP 请求报文。</summary>
	public static string buildRequest(Operation op, IEnumerable<PNode> parts) =>
		buildBody(op, parts, false);

	/// <summary>按响应参数树生成完整 SOAP 响应报文（模拟服务器用）。</summary>
	public static string buildResponse(Operation op, IEnumerable<PNode> parts) =>
		buildBody(op, parts, true);

	static string buildBody(Operation op, IEnumerable<PNode> parts, bool isResponse) {
		string version = op.SoapVersionValue;
		bool rpc = string.Equals(op.Style, "rpc", StringComparison.OrdinalIgnoreCase);
		string bns = string.IsNullOrEmpty(op.BodyNamespace) ? (op.Endpoint?.Address ?? "") : op.BodyNamespace;
		var list = parts?.Where(p => p != null).ToList() ?? new List<PNode>();
		return buildEnvelope(version, (w, p) => {
			if (rpc) {
				string wrapper = isResponse ? op.Name + "Response" : op.Name;
				w.WriteStartElement(p.prefixFor(bns), wrapper, string.IsNullOrEmpty(bns) ? null : bns);
				foreach (var part in list) writeNode(w, p, part, bns, true);
				w.WriteEndElement();
			} else {
				foreach (var part in list) writeNode(w, p, part, op.BodyNamespace, true);
			}
		});
	}

	public static string buildResponseXml(string version, string innerXml) {
		string env = envelopeNs(version);
		var sb = new StringBuilder();
		sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
		sb.AppendLine($"<soap:Envelope xmlns:soap=\"{env}\">");
		sb.AppendLine("  <soap:Body>");
		sb.AppendLine(innerXml);
		sb.AppendLine("  </soap:Body>");
		sb.AppendLine("</soap:Envelope>");
		return sb.ToString();
	}

	/// <summary>按输出参数树生成完整响应报文（元素与命名空间来自 WSDL）。</summary>
	public static string buildResponse(string version, IEnumerable<PNode> parts) {
		var list = parts?.Where(p => p != null).ToList() ?? new List<PNode>();
		return buildEnvelope(version, (w, p) => {
			foreach (var part in list) writeNode(w, p, part, "", true);
		});
	}

	static void writeNode(XmlWriter w, PrefixMap prefixes, PNode node, string inheritedNs, bool skipSynthetic) {
		if (skipSynthetic && node.IsSynthetic) {
			foreach (var c in node.Children) writeNode(w, prefixes, c, inheritedNs, skipSynthetic);
			return;
		}
		string ns = node.IsAttribute ? "" : (node.Ns ?? inheritedNs ?? "");
		string name = string.IsNullOrEmpty(node.Name) ? "value" : node.Name;
		w.WriteStartElement(prefixes.prefixFor(ns), name, string.IsNullOrEmpty(ns) ? null : ns);

		foreach (var a in node.Children.Where(c => c.IsAttribute)) {
			string av = string.IsNullOrEmpty(a.Value) ? (a.FixedValue ?? "") : a.Value;
			w.WriteAttributeString(a.Name, av);
		}

		var children = node.Children.Where(c => !c.IsAttribute).ToList();
		if (children.Count == 0) {
			if (node.FixedValue != null) w.WriteString(node.FixedValue);
			else if (!string.IsNullOrEmpty(node.Value)) w.WriteString(node.Value);
			else if (node.DefaultValue != null) w.WriteString(node.DefaultValue);
		} else {
			foreach (var child in children) writeChild(w, prefixes, child, ns);
		}
		w.WriteEndElement();
	}

	/// <summary>数组节点按 Occurs 重复；叶子多行文本按行拆成多个元素。</summary>
	static void writeChild(XmlWriter w, PrefixMap prefixes, PNode node, string parentNs) {
		if (node.IsArray && node.IsLeaf && node.Value.IndexOf('\n') >= 0) {
			foreach (var line in node.Value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
				var one = node.clone();
				one.Value = line.Trim();
				one.Occurs = 1;
				writeNode(w, prefixes, one, parentNs, false);
			}
			return;
		}
		int count = node.IsArray ? Math.Max(1, node.Occurs) : 1;
		for (int i = 0; i < count; i++) writeNode(w, prefixes, node, parentNs, false);
	}
	#endregion

	// ==================== 解析响应 ====================
	#region
	public static XDocument parse(string xml, out string error) {
		error = null;
		if (string.IsNullOrWhiteSpace(xml)) { error = "响应为空"; return null; }
		try {
			var settings = new XmlReaderSettings {
				DtdProcessing = DtdProcessing.Ignore,
				XmlResolver = null,
				CheckCharacters = false
			};
			using var sr = new StringReader(xml);
			using var xr = XmlReader.Create(sr, settings);
			return XDocument.Load(xr, LoadOptions.PreserveWhitespace);
		} catch (Exception ex) {
			error = ex.Message;
			return null;
		}
	}

	public static bool tryGetFault(string xml, out string code, out string message) {
		code = message = null;
		var doc = parse(xml, out _);
		if (doc?.Root == null) return false;
		var fault = doc.Root.Descendants().FirstOrDefault(e => e.Name.LocalName == "Fault");
		if (fault == null) return false;
		var codeEl = fault.Descendants().FirstOrDefault(e => e.Name.LocalName == "faultcode" || e.Name.LocalName == "Value");
		var msgEl = fault.Descendants().FirstOrDefault(e => e.Name.LocalName == "faultstring" || e.Name.LocalName == "Text");
		code = codeEl?.Value?.Trim();
		message = msgEl?.Value?.Trim();
		return true;
	}

	public static XElement firstBodyElement(string xml) {
		var doc = parse(xml, out _);
		var body = doc?.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Body");
		return body?.Elements().FirstOrDefault();
	}

	public static string bodyOperationName(string xml) => firstBodyElement(xml)?.Name.LocalName;
	#endregion
}
