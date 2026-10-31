using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ScreenKit.Soap;

/// <summary>
/// 模拟 SOAP 服务器：用 TcpListener 自实现最小 HTTP/1.1，避免 HttpListener 的 URL ACL 限制。
/// 发布 WSDL/XSD，并按方法名返回可配置的响应。
/// </summary>
class MockServer {
	public const int MAXLOG = 300;
	const int MAXBODY = 8 * 1024 * 1024;

	TcpListener listener;
	Thread acceptThread;
	volatile bool running;
	readonly object sync = new();
	readonly List<Thread> workers = new();

	public int Port { get; private set; } = 8080;
	public bool BindAll { get; private set; }
	public bool Running => running;
	public string BaseUrl => $"http://127.0.0.1:{Port}";
	public WsdlModel Model { get; private set; }

	/// <summary>方法名 → 响应内层 XML（或完整 Envelope）。返回 null 时用默认模板。</summary>
	public Func<string, string> Resolver;

	readonly Dictionary<string, string> docs = new(StringComparer.OrdinalIgnoreCase);
	readonly Dictionary<string, string> byName = new(StringComparer.OrdinalIgnoreCase);
	readonly List<MockLog> logs = new();
	int logver;

	// ==================== 日志 ====================
	#region
	public int LogVersion => logver;

	public List<MockLog> logsnapshot() {
		lock (sync) return logs.ToList();
	}

	public void clearLogs() {
		lock (sync) {
			logs.Clear();
			logver++;
		}
	}

	public class MockLog {
		public string TimeText { get; set; } = "";
		public string Method { get; set; } = "";
		public string Url { get; set; } = "";
		public string Body { get; set; } = "";
		public string ResponseBody { get; set; } = "";
		public int Status { get; set; }
		public long ElapsedMs { get; set; }
		public string Note { get; set; } = "";
		public override string ToString() => $"{TimeText}  {Note}  {Url}";
	}
	#endregion

	// ==================== 启停 ====================
	#region
	public bool start(int port, bool bindAll, out string error) {
		error = null;
		stop();
		if (port <= 0 || port > 65535) { error = "端口不合法"; return false; }
		try {
			var l = new TcpListener(bindAll ? IPAddress.Any : IPAddress.Loopback, port);
			l.Start();
			listener = l;
			Port = port;
			BindAll = bindAll;
			running = true;
			acceptThread = new Thread(acceptLoop) { IsBackground = true, Name = "MockAccept" };
			acceptThread.Start();
			return true;
		} catch (Exception ex) {
			error = (ex.InnerException ?? ex).Message;
			stop();
			return false;
		}
	}

	public void stop() {
		running = false;
		try { listener?.Stop(); } catch { }
		listener = null;
		acceptThread = null;
		lock (sync) workers.Clear();
	}
	#endregion

	// ==================== 接收循环 ====================
	#region
	void acceptLoop() {
		while (running) {
			TcpClient client;
			try { client = listener.AcceptTcpClient(); }
			catch { break; }
			var t = new Thread(() => serve(client)) { IsBackground = true, Name = "MockConn" };
			lock (sync) {
				workers.RemoveAll(w => !w.IsAlive);
				if (workers.Count > 32) { try { client.Close(); } catch { } continue; }
				workers.Add(t);
			}
			t.Start();
		}
	}

	void serve(TcpClient client) {
		int start = Environment.TickCount;
		try {
			using (client) {
				client.ReceiveTimeout = 10000;
				client.SendTimeout = 10000;
				var stream = client.GetStream();
				var head = readHead(stream);
				if (head == null || head.Count == 0) return;
				var parts = head[0].Split(' ');
				if (parts.Length < 3) return;
				string method = parts[0].ToUpperInvariant();
				string rawUrl = parts[1];

				var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				for (int i = 1; i < head.Count; i++) {
					int c = head[i].IndexOf(':');
					if (c <= 0) continue;
					headers[head[i].Substring(0, c).Trim()] = head[i].Substring(c + 1).Trim();
				}

				if (headers.TryGetValue("Expect", out var expect) && expect.IndexOf("100-continue", StringComparison.OrdinalIgnoreCase) >= 0)
					writeRaw(stream, Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n"));

				int length = 0;
				if (headers.TryGetValue("Content-Length", out var cl)) int.TryParse(cl, out length);
				if (length < 0 || length > MAXBODY) length = 0;
				string body = length > 0 ? Net.decode(readExactly(stream, length)) : "";

				string path = rawUrl;
				int q = path.IndexOf('?');
				if (q >= 0) path = path.Substring(0, q);

				string doc = documentFor(path);
				if (doc != null && (method == "GET" || method == "HEAD")) {
					respond(stream, 200, "text/xml; charset=utf-8", doc, method == "HEAD");
					addLog(start, method, rawUrl, body, doc, 200, "WSDL/XSD");
					return;
				}
				if (method == "GET" || method == "HEAD") {
					string msg = "SOAP 模拟服务运行中。\n\n已发布文档:\n" +
						string.Join("\n", paths().Select(p => "  " + BaseUrl + p)) +
						"\n\n向业务地址 POST SOAP 报文即可得到此处配置的响应。";
					respond(stream, 200, "text/plain; charset=utf-8", msg, method == "HEAD");
					addLog(start, method, rawUrl, body, msg, 200, "说明页");
					return;
				}

				string opName = SoapEnvelope.bodyOperationName(body);
				string version = detectVersion(headers.TryGetValue("Content-Type", out var ct) ? ct : "", body);
				var op = opName != null ? FindOp(opName) : null;
				string custom = opName != null ? Resolver?.Invoke(opName) : null;

				string xml;
				if (!string.IsNullOrWhiteSpace(custom)) {
					custom = Template.expand(custom);
					string t = custom.TrimStart();
					xml = t.StartsWith("<", StringComparison.Ordinal) && t.IndexOf("Envelope", StringComparison.OrdinalIgnoreCase) >= 0
						? custom
						: SoapEnvelope.buildResponseXml(version, custom);
				} else if (op != null) {
					xml = SoapEnvelope.buildResponse(version, DefaultResponse(op));
				} else {
					xml = SoapEnvelope.buildResponseXml(version,
						$"  <!-- 未匹配到方法 {opName}，请在模拟页配置该接口的返回内容 -->\n  <{opName ?? "Unknown"}Response />");
				}

				string rct = version == "1.2" ? "application/soap+xml; charset=utf-8" : "text/xml; charset=utf-8";
				respond(stream, 200, rct, xml, false);
				addLog(start, method, rawUrl, body, xml, 200, opName == null ? "SOAP（未识别）" : $"SOAP {opName}");
			}
		} catch { }
	}

	static List<string> readHead(NetworkStream stream) {
		var buf = new List<byte>(1024);
		var one = new byte[1];
		while (buf.Count < 64 * 1024) {
			int n;
			try { n = stream.Read(one, 0, 1); } catch { return null; }
			if (n <= 0) return buf.Count > 0 ? parseHead(buf) : null;
			buf.Add(one[0]);
			int c = buf.Count;
			if (c >= 4 && buf[c - 1] == 10 && buf[c - 2] == 13 && buf[c - 3] == 10 && buf[c - 4] == 13)
				return parseHead(buf);
		}
		return parseHead(buf);
	}

	static List<string> parseHead(List<byte> buf) {
		string text = Encoding.UTF8.GetString(buf.ToArray());
		var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None).Where(l => l.Length > 0).ToList();
		return lines.Count == 0 ? null : lines;
	}

	static byte[] readExactly(NetworkStream stream, int length) {
		var data = new byte[length];
		int off = 0;
		while (off < length) {
			int n = stream.Read(data, off, length - off);
			if (n <= 0) break;
			off += n;
		}
		return off == length ? data : data.Take(off).ToArray();
	}

	static void respond(NetworkStream stream, int status, string contentType, string text, bool headOnly) {
		byte[] data = new UTF8Encoding(false).GetBytes(text ?? "");
		string reason = status == 200 ? "OK" : status == 404 ? "Not Found" : status == 500 ? "Internal Server Error" : "Status";
		var sb = new StringBuilder();
		sb.Append($"HTTP/1.1 {status} {reason}\r\n");
		sb.Append($"Content-Type: {contentType}\r\n");
		sb.Append($"Content-Length: {data.Length}\r\n");
		sb.Append("Connection: close\r\n\r\n");
		writeRaw(stream, Encoding.ASCII.GetBytes(sb.ToString()));
		if (!headOnly) writeRaw(stream, data);
	}

	static void writeRaw(NetworkStream stream, byte[] data) {
		try {
			stream.Write(data, 0, data.Length);
			stream.Flush();
		} catch { }
	}

	static string detectVersion(string contentType, string body) {
		if (!string.IsNullOrEmpty(contentType) && contentType.IndexOf("soap+xml", StringComparison.OrdinalIgnoreCase) >= 0) return "1.2";
		if (!string.IsNullOrEmpty(body) && body.IndexOf(SoapEnvelope.NS_SOAP12, StringComparison.OrdinalIgnoreCase) >= 0) return "1.2";
		return "1.1";
	}

	/// <summary>按方法名找操作，先精确匹配，再忽略大小写。</summary>
	Operation FindOp(string name) {
		if (Model == null || string.IsNullOrEmpty(name)) return null;
		var op = Model.findOp(name);
		// 兼容 document/rpc 包装差异：请求根元素可能带 Response 后缀等
		if (op == null) op = Model.allOps().FirstOrDefault(o => name.StartsWith(o.Name, StringComparison.OrdinalIgnoreCase));
		return op;
	}

	/// <summary>默认响应：按 WSDL 输出参数生成（空值），保证命名空间正确。</summary>
	public List<PNode> DefaultResponse(Operation op) {
		if (Builder == null || op == null) return null;
		var list = new List<PNode>();
		foreach (var part in op.OutputParts) {
			PNode node = null;
			if (part.IsElement) node = Builder.fromQualifiedName(new System.Xml.XmlQualifiedName(part.ElementName, part.ElementNs));
			else if (!string.IsNullOrEmpty(part.TypeName)) node = Builder.fromQualifiedName(new System.Xml.XmlQualifiedName(part.TypeName, part.TypeNs));
			if (node != null) list.Add(node);
		}
		return list;
	}

	/// <summary>构建默认响应所需的参数构建器（由 WsdlSession 注入）。</summary>
	public ParamBuilder Builder { get; set; }

	void addLog(int start, string method, string url, string body, string resp, int status, string note) {
		var log = new MockLog {
			TimeText = DateTime.Now.ToString("HH:mm:ss.fff"),
			Method = method,
			Url = url,
			Body = body,
			ResponseBody = resp,
			Status = status,
			ElapsedMs = Environment.TickCount - start,
			Note = note
		};
		lock (sync) {
			logs.Insert(0, log);
			while (logs.Count > MAXLOG) logs.RemoveAt(logs.Count - 1);
			logver++;
		}
	}
	#endregion

	// ==================== 文档发布 ====================
	#region
	/// <summary>发布 WSDL/XSD，并把服务地址重写到本机模拟端口。</summary>
	public void publish(WsdlModel model, int port) {
		Model = model;
		if (port > 0) Port = port;
		lock (sync) {
			docs.Clear();
			byName.Clear();
			if (model == null) return;
			foreach (var doc in model.Docs) {
				string path = string.IsNullOrEmpty(doc.Path) ? WsdlLoader.pathOf(doc.Url) : doc.Path;
				string text = doc.IsWsdl ? rewriteWsdl(doc.Text) : (doc.Text ?? "");
				docs[path] = text;
				byName[Path.GetFileName(path)] = path;
			}
		}
	}

	public string documentFor(string path) {
		lock (sync) {
			if (docs.TryGetValue(path, out var t)) return t;
			string name = Path.GetFileName(path);
			if (byName.TryGetValue(name, out var full) && docs.TryGetValue(full, out var t2)) return t2;
			return null;
		}
	}

	public IEnumerable<string> paths() {
		lock (sync) return docs.Keys.ToList();
	}

	string rewriteWsdl(string text) {
		if (string.IsNullOrEmpty(text)) return text;
		try {
			var doc = System.Xml.Linq.XDocument.Parse(text, System.Xml.Linq.LoadOptions.PreserveWhitespace);
			foreach (var addr in doc.Descendants().Where(e => e.Name.LocalName == "address")) {
				string loc = (string)addr.Attribute("location");
				if (loc == null) continue;
				addr.SetAttributeValue("location", BaseUrl + "/" + svcPath(loc));
			}
			foreach (var el in doc.Descendants().Where(e => e.Attribute("schemaLocation") != null || e.Attribute("location") != null)) {
				var attr = el.Attribute("schemaLocation") ?? el.Attribute("location");
				string v = attr.Value;
				if (string.IsNullOrEmpty(v) || v.StartsWith("http", StringComparison.OrdinalIgnoreCase) || v.StartsWith("file", StringComparison.OrdinalIgnoreCase)) continue;
				attr.Value = "./" + Path.GetFileName(v);
			}
			return doc.ToString();
		} catch { return text; }
	}

	static string svcPath(string location) {
		try {
			if (Uri.TryCreate(location, UriKind.Absolute, out var uri)) return uri.AbsolutePath.TrimStart('/');
			return location.TrimStart('.', '/');
		} catch { return "service"; }
	}
	#endregion
}
