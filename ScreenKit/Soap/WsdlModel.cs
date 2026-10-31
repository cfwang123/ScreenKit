using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Xml.Schema;

namespace ScreenKit.Soap;

/// <summary>从 WSDL 解析出的调用端信息（不依赖 SOAP 库，全手写解析）。</summary>
class Endpoint {
	public string ServiceName { get; set; } = "";
	public string PortName { get; set; } = "";
	public string Address { get; set; } = "";
	public string BindingName { get; set; } = "";
	public string SoapVersion { get; set; } = "1.1";     // 1.1 / 1.2
	public string Style { get; set; } = "document";
	public List<Operation> Ops { get; } = new();
	public string Scope => string.IsNullOrEmpty(ServiceName) ? PortName : $"{ServiceName} / {PortName}";
	public override string ToString() => string.IsNullOrEmpty(ServiceName) ? PortName : $"{ServiceName} - {PortName}";
}

class Operation {
	public string Name { get; set; } = "";
	public string SoapAction { get; set; } = "";
	public string Style { get; set; } = "document";      // document / rpc
	public string BodyNamespace { get; set; } = "";      // soap:body/@namespace
	public List<Part> InputParts { get; set; } = new();
	public List<Part> OutputParts { get; set; } = new();
	public string InputMessage { get; set; } = "";
	public string OutputMessage { get; set; } = "";
	public bool OneWay { get; set; }
	public Endpoint Endpoint { get; set; }

	/// <summary>界面上可强制指定 SOAP 版本；为 null 时用 WSDL 中的版本。</summary>
	public string SoapVersionOverride { get; set; }

	public string SoapVersionValue => SoapVersionOverride ?? Endpoint?.SoapVersion ?? "1.1";

	public override string ToString() => Name;
}

class Part {
	public string Name { get; set; } = "";
	public string ElementNs { get; set; } = "";
	public string ElementName { get; set; } = "";
	public string TypeNs { get; set; } = "";
	public string TypeName { get; set; } = "";
	public bool IsElement => !string.IsNullOrEmpty(ElementName);
}

class WsdlDoc {
	public string Url { get; set; } = "";       // 绝对地址（http/https/file）
	public string Path { get; set; } = "";      // URI 路径，用于模拟服务器路由
	public string Text { get; set; } = "";
	public bool IsWsdl { get; set; }
	public string TargetNamespace { get; set; } = "";
	internal XDocument XDoc;
	internal XElement Root;
}

class WsdlModel {
	public string Location { get; set; } = "";
	public List<WsdlDoc> Docs { get; } = new();
	public XmlSchemaSet Schemas { get; set; } = new();
	public List<Endpoint> Endpoints { get; } = new();
	public List<string> Warnings { get; } = new();
	public string RootTargetNamespace { get; set; } = "";
	public bool IsWsdl { get; set; }
	public string LoadError { get; set; }

	public Endpoint firstEndpoint() => Endpoints.FirstOrDefault(e => e.Ops.Count > 0) ?? Endpoints.FirstOrDefault();
	public IEnumerable<Operation> allOps() => Endpoints.SelectMany(e => e.Ops);
	public Operation findOp(string name) => allOps().FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
}
