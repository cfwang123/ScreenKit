using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace ScreenKit.Soap;

/// <summary>WSDL 加载与解析（纯 XML 手写，不依赖 System.Web.Services）。</summary>
static class WsdlLoader {
	public const string NS_WSDL = "http://schemas.xmlsoap.org/wsdl/";
	public const string NS_SOAP11 = "http://schemas.xmlsoap.org/wsdl/soap/";
	public const string NS_SOAP12 = "http://schemas.xmlsoap.org/wsdl/soap12/";
	public const string NS_XSD = "http://www.w3.org/2001/XMLSchema";
	public const string NS_SOAPENC = "http://schemas.xmlsoap.org/soap/encoding/";

	static readonly XNamespace wsdl = NS_WSDL;
	static readonly XNamespace xsd = NS_XSD;

	// ==================== 加载入口 ====================
	// ==================== 加载入口 ====================
	#region
	public static WsdlModel load(string location) {
		var model = new WsdlModel { Location = location };
		if (string.IsNullOrWhiteSpace(location)) {
			model.LoadError = "地址为空";
			return model;
		}
		if (!Net.isRemote(location) && !File.Exists(location)) {
			model.LoadError = $"文件不存在: {location}";
			return model;
		}
		loadCore(model, location);
		return model;
	}

	public static WsdlModel loadText(string text, string baseLocation) {
		var model = new WsdlModel { Location = baseLocation };
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var queue = new Queue<string>();
		try {
			string abs = Net.absolutize(baseLocation, "");
			seen.Add(abs);
			addDoc(model, text, abs, seen, queue);
			drain(model, seen, queue);
		} catch (Exception ex) {
			model.LoadError = ex.Message;
			return model;
		}
		finish(model);
		return model;
	}

	static void loadCore(WsdlModel model, string location) {
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var queue = new Queue<string>();
		try {
			string abs = Net.absolutize(location, "");
			seen.Add(abs);
			string text = Net.readText(abs);
			addDoc(model, text, abs, seen, queue);
			drain(model, seen, queue);
		} catch (Exception ex) {
			model.LoadError = ex.Message;
			return;
		}
		finish(model);
	}

	static void finish(WsdlModel model) {
		model.IsWsdl = model.Docs.Any(d => d.IsWsdl);
		try { buildSchemas(model); } catch (Exception ex) { model.Warnings.Add($"Schema 装载失败: {ex.Message}"); }
		try { buildEndpoints(model); } catch (Exception ex) { model.Warnings.Add($"端点解析失败: {ex.Message}"); }
	}

	static void drain(WsdlModel model, HashSet<string> seen, Queue<string> queue) {
		while (queue.Count > 0) {
			string next = queue.Dequeue();
			try {
				string text = Net.readText(next);
				addDoc(model, text, next, seen, queue);
			} catch (Exception ex) {
				model.Warnings.Add($"导入 {next} 失败: {ex.Message}");
			}
		}
	}

	static string absUrl(string baseUrl, string location) {
		try {
			if (string.IsNullOrEmpty(location)) return Net.toUri(baseUrl)?.AbsoluteUri ?? baseUrl;
			var b = Net.toUri(baseUrl);
			return b == null ? location : new Uri(b, location).AbsoluteUri;
		} catch {
			try { return new Uri(location).AbsoluteUri; } catch { return location; }
		}
	}

	static void addDoc(WsdlModel model, string text, string abs, HashSet<string> seen, Queue<string> queue) {
		XDocument doc;
		try { doc = XDocument.Parse(text, LoadOptions.PreserveWhitespace); }
		catch (XmlException ex) { throw new InvalidOperationException($"XML 解析失败: {ex.Message}"); }
		if (doc.Root == null) throw new InvalidOperationException("文档为空");
		var root = doc.Root;

		var wdoc = new WsdlDoc {
			Url = abs, Path = pathOf(abs), Text = text, XDoc = doc, Root = root,
			IsWsdl = root.Name.LocalName == "definitions",
			TargetNamespace = (string)root.Attribute("targetNamespace") ?? ""
		};
		model.Docs.Add(wdoc);
		if (wdoc.IsWsdl && string.IsNullOrEmpty(model.RootTargetNamespace)) model.RootTargetNamespace = wdoc.TargetNamespace;

		foreach (var el in root.Descendants()) {
			string ln = el.Name.LocalName;
			if (ln != "import" && ln != "include") continue;
			string ns = el.Name.NamespaceName;
			if (ns != NS_WSDL && ns != NS_XSD) continue;
			string loc = (string)el.Attribute("schemaLocation") ?? (string)el.Attribute("location");
			if (string.IsNullOrWhiteSpace(loc)) continue;
			if (loc.StartsWith("#")) continue;
			string next;
			try { next = new Uri(new Uri(abs), loc).AbsoluteUri; }
			catch { continue; }
			if (seen.Add(next)) queue.Enqueue(next);
		}
	}

	public static string pathOf(string abs) {
		try {
			var uri = new Uri(abs);
			return uri.IsFile ? "/" + Path.GetFileName(uri.LocalPath) : uri.AbsolutePath;
		} catch { return "/" + Path.GetFileName(abs); }
	}
	#endregion

	// ==================== Schema 汇集 ====================
	#region
	static void buildSchemas(WsdlModel model) {
		var set = new XmlSchemaSet { XmlResolver = new CachedResolver(model) };
		var keys = new HashSet<string>();
		foreach (var doc in model.Docs) {
			foreach (var src in doc.Root.Descendants(xsd + "schema").ToList()) {
				string tns = (string)src.Attribute("targetNamespace") ?? "";
				string key = tns + "|" + doc.Url;
				if (!keys.Add(key)) continue;
				try {
					using var reader = src.CreateReader();
					var schema = XmlSchema.Read(reader, (_, e) => model.Warnings.Add($"Schema: {e.Message}"));
					if (schema != null) set.Add(schema);
				} catch (Exception ex) {
					model.Warnings.Add($"Schema 解析失败({doc.Url}): {ex.Message}");
				}
			}
		}
		try { set.Compile(); } catch (Exception ex) { model.Warnings.Add($"Schema 编译失败: {ex.Message}"); }
		model.Schemas = set;
	}

	/// <summary>把已下载的文档提供给 schema include/import 使用。</summary>
	class CachedResolver : XmlResolver {
		readonly WsdlModel model;
		public CachedResolver(WsdlModel model) { this.model = model; }

		public override System.Net.ICredentials Credentials { set { } }

		public override object GetEntity(Uri absoluteUri, string role, Type ofObjectToReturn) {
			string target = absoluteUri.AbsoluteUri;
			var doc = model.Docs.FirstOrDefault(d => string.Equals(d.Url, target, StringComparison.OrdinalIgnoreCase));
			if (doc != null) return new MemoryStream(new System.Text.UTF8Encoding(false).GetBytes(doc.Text), false);
			try { return new MemoryStream(Net.fetch(target), false); }
			catch { return null; }
		}
	}
	#endregion

	// ==================== 端点/方法解析 ====================
	#region
	static void buildEndpoints(WsdlModel model) {
		foreach (var doc in model.Docs.Where(d => d.IsWsdl).ToList()) parseDefinitions(model, doc);
		if (model.Endpoints.Count == 0) model.Warnings.Add("WSDL 中未找到 SOAP 端口定义");
	}

	static void parseDefinitions(WsdlModel model, WsdlDoc doc) {
		var root = doc.Root;
		string tns = doc.TargetNamespace;
		var messages = new Dictionary<string, XElement>();
		var bindings = new Dictionary<string, XElement>();
		var portTypes = new Dictionary<string, XElement>();

		foreach (var d in model.Docs.Where(x => x.IsWsdl)) {
			foreach (var m in d.Root.Elements(wsdl + "message")) {
				string nm = (string)m.Attribute("name");
				if (nm != null && !messages.ContainsKey(nm)) messages[nm] = m;
			}
			foreach (var b in d.Root.Elements(wsdl + "binding")) {
				string nm = (string)b.Attribute("name");
				if (nm != null && !bindings.ContainsKey(nm)) bindings[nm] = b;
			}
			foreach (var pt in d.Root.Elements(wsdl + "portType")) {
				string nm = (string)pt.Attribute("name");
				if (nm != null && !portTypes.ContainsKey(nm)) portTypes[nm] = pt;
			}
		}

		var services = root.Elements(wsdl + "service").ToList();
		if (services.Count > 0) {
			foreach (var svc in services) {
				string svcName = (string)svc.Attribute("name") ?? "Service";
				foreach (var port in svc.Elements(wsdl + "port")) {
					string portName = (string)port.Attribute("name") ?? "Port";
					string bindingRef = port.Attribute("binding")?.Value;
					if (string.IsNullOrEmpty(bindingRef)) continue;
					if (!bindings.TryGetValue(localNameOf(bindingRef), out var binding)) {
						model.Warnings.Add($"未找到 binding: {bindingRef}");
						continue;
					}
					var soapAddr = port.Elements().FirstOrDefault(e => isSoapNs(e.Name.NamespaceName) && e.Name.LocalName == "address");
					var ep = new Endpoint {
						ServiceName = svcName,
						PortName = portName,
						Address = (string)soapAddr?.Attribute("location") ?? "",
						BindingName = localNameOf(bindingRef),
						SoapVersion = soapAddr != null && soapAddr.Name.NamespaceName == NS_SOAP12 ? "1.2" : "1.1"
					};
					parseBinding(ep, binding, messages, portTypes, model, tns);
					if (ep.Ops.Count > 0 && !model.Endpoints.Any(x => x.ServiceName == ep.ServiceName && x.PortName == ep.PortName))
						model.Endpoints.Add(ep);
				}
			}
			return;
		}

		foreach (var kv in bindings) {
			var soapB = kv.Value.Elements().FirstOrDefault(e => isSoapNs(e.Name.NamespaceName) && e.Name.LocalName == "binding");
			if (soapB == null) continue;
			var ep = new Endpoint {
				ServiceName = "Service",
				PortName = kv.Key,
				BindingName = kv.Key,
				Address = "",
				SoapVersion = soapB.Name.NamespaceName == NS_SOAP12 ? "1.2" : "1.1"
			};
			parseBinding(ep, kv.Value, messages, portTypes, model, tns);
			if (ep.Ops.Count > 0) model.Endpoints.Add(ep);
		}
	}

	static bool isSoapNs(string ns) => ns == NS_SOAP11 || ns == NS_SOAP12;

	static void parseBinding(Endpoint ep, XElement binding, Dictionary<string, XElement> messages,
		Dictionary<string, XElement> portTypes, WsdlModel model, string tns) {
		var soapB = binding.Elements().FirstOrDefault(e => isSoapNs(e.Name.NamespaceName) && e.Name.LocalName == "binding");
		if (soapB != null) {
			string style = (string)soapB.Attribute("style");
			if (!string.IsNullOrEmpty(style)) ep.Style = style;
			ep.SoapVersion = soapB.Name.NamespaceName == NS_SOAP12 ? "1.2" : "1.1";
		}

		// binding@type 指向 portType，abstract 的 input/output@message 在 portType 上
		string ptName = localNameOf(binding.Attribute("type")?.Value);
		portTypes.TryGetValue(ptName ?? "", out var portType);
		var ptOps = new Dictionary<string, XElement>();
		if (portType != null) {
			foreach (var ptOp in portType.Elements(wsdl + "operation")) {
				string nm = (string)ptOp.Attribute("name");
				if (nm != null && !ptOps.ContainsKey(nm)) ptOps[nm] = ptOp;
			}
		}

		foreach (var opEl in binding.Elements(wsdl + "operation")) {
			string opName = (string)opEl.Attribute("name") ?? "";
			var op = new Operation { Name = opName, Style = ep.Style, Endpoint = ep };

			var soapOp = opEl.Elements().FirstOrDefault(e => isSoapNs(e.Name.NamespaceName) && e.Name.LocalName == "operation");
			if (soapOp != null) {
				op.SoapAction = (string)soapOp.Attribute("soapAction") ?? "";
				string st = (string)soapOp.Attribute("style");
				if (!string.IsNullOrEmpty(st)) op.Style = st;
			}
			if (string.IsNullOrEmpty(op.SoapAction)) op.SoapAction = tns.TrimEnd('/') + "/" + opName;

			ptOps.TryGetValue(opName, out var ptOp);
			var ptIn = ptOp?.Element(wsdl + "input");
			var ptOut = ptOp?.Element(wsdl + "output");
			op.OneWay = ptOut == null && opEl.Element(wsdl + "output") == null;

			string inMsgName = localNameOf(ptIn?.Attribute("message")?.Value) ?? localNameOf(opEl.Element(wsdl + "input")?.Attribute("message")?.Value);
			string outMsgName = localNameOf(ptOut?.Attribute("message")?.Value) ?? localNameOf(opEl.Element(wsdl + "output")?.Attribute("message")?.Value);
			op.InputMessage = inMsgName ?? "";
			op.OutputMessage = outMsgName ?? "";

			var bindIn = opEl.Element(wsdl + "input");
			var bindOut = opEl.Element(wsdl + "output");
			var inBody = bindIn?.Elements().FirstOrDefault(e => isSoapNs(e.Name.NamespaceName) && e.Name.LocalName == "body")
				?? ptIn?.Elements().FirstOrDefault(e => isSoapNs(e.Name.NamespaceName) && e.Name.LocalName == "body");
			var outBody = bindOut?.Elements().FirstOrDefault(e => isSoapNs(e.Name.NamespaceName) && e.Name.LocalName == "body")
				?? ptOut?.Elements().FirstOrDefault(e => isSoapNs(e.Name.NamespaceName) && e.Name.LocalName == "body");
			op.BodyNamespace = (string)inBody?.Attribute("namespace") ?? (string)outBody?.Attribute("namespace") ?? tns;
			if (op.BodyNamespace == null) op.BodyNamespace = tns;

			if (!string.IsNullOrEmpty(inMsgName) && messages.TryGetValue(inMsgName, out var inMsg)) op.InputParts = readParts(inMsg);
			if (!string.IsNullOrEmpty(outMsgName) && messages.TryGetValue(outMsgName, out var outMsg)) op.OutputParts = readParts(outMsg);
			ep.Ops.Add(op);
		}
	}
	static List<Part> readParts(XElement message) {
		var list = new List<Part>();
		foreach (var p in message.Elements(wsdl + "part")) {
			var part = new Part { Name = (string)p.Attribute("name") ?? "" };
			string el = p.Attribute("element")?.Value;
			string ty = p.Attribute("type")?.Value;
			if (!string.IsNullOrEmpty(el)) {
				splitQName(p, el, out string ens, out string en);
				part.ElementNs = ens;
				part.ElementName = en;
			} else if (!string.IsNullOrEmpty(ty)) {
				splitQName(p, ty, out string tns, out string tn);
				part.TypeNs = tns;
				part.TypeName = tn;
			}
			list.Add(part);
		}
		return list;
	}

	/// <summary>QName 属性值（前缀:局部名）→ 命名空间 + 局部名，前缀在 context 所在文档作用域内解析。</summary>
	static void splitQName(XElement context, string qname, out string ns, out string local) {
		ns = "";
		local = qname ?? "";
		if (local.Length == 0) return;
		int p = local.IndexOf(':');
		if (p <= 0) return;
		string prefix = local.Substring(0, p);
		local = local.Substring(p + 1);
		try {
			foreach (var a in context.AncestorsAndSelf().Attributes()) {
				if (a.IsNamespaceDeclaration && a.Name.LocalName == prefix) { ns = a.Value; return; }
			}
		} catch { }
	}

	static string localNameOf(string qname) {
		if (string.IsNullOrEmpty(qname)) return null;
		int p = qname.IndexOf(':');
		return p < 0 ? qname : qname.Substring(p + 1);
	}
	#endregion
}
