using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace ScreenKit.Soap;

/// <summary>模拟响应的持久化配置（XML 存储，可读可编辑）。跨线程安全。</summary>
class MockConfig {
	readonly object sync = new();
	readonly Dictionary<string, string> responses = new(StringComparer.OrdinalIgnoreCase);

	public string WsdlLocation { get; set; } = "";
	public int Port { get; set; } = 8080;

	public int Count {
		get { lock (sync) return responses.Count; }
	}

	// ==================== 读写 ====================
	#region
	public string get(string op) {
		if (string.IsNullOrEmpty(op)) return null;
		lock (sync) return responses.TryGetValue(op, out var v) ? v : null;
	}

	public void set(string op, string xml) {
		if (string.IsNullOrEmpty(op)) return;
		lock (sync) {
			if (xml == null) responses.Remove(op);
			else responses[op] = xml;
		}
	}

	public bool has(string op) {
		if (string.IsNullOrEmpty(op)) return false;
		lock (sync) return responses.ContainsKey(op);
	}

	public List<KeyValuePair<string, string>> snapshot() {
		lock (sync) return responses.ToList();
	}
	#endregion

	// ==================== 存取 ====================
	#region
	public void save(string path) {
		var items = snapshot().OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToList();
		var doc = new XDocument(
			new XElement("MockConfig",
				new XAttribute("wsdl", WsdlLocation ?? ""),
				new XAttribute("port", Port),
				items.Select(kv => new XElement("response",
					new XAttribute("method", kv.Key),
					new XCData(kv.Value ?? "")))));
		File.WriteAllText(path, doc.ToString(), new UTF8Encoding(false));
	}

	public static MockConfig load(string path, out string error) {
		error = null;
		var cfg = new MockConfig();
		try {
			var doc = XDocument.Parse(File.ReadAllText(path));
			var root = doc.Root;
			if (root == null) return cfg;
			cfg.WsdlLocation = (string)root.Attribute("wsdl") ?? "";
			if (int.TryParse((string)root.Attribute("port"), out int p)) cfg.Port = p;
			foreach (var el in root.Elements("response")) {
				string m = (string)el.Attribute("method");
				if (!string.IsNullOrEmpty(m)) cfg.set(m, el.Value);
			}
		} catch (Exception ex) {
			error = ex.Message;
		}
		return cfg;
	}
	#endregion
}
