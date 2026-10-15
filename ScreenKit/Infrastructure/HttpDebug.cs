using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenKit;

sealed class HttpDbgGroup {
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
}

sealed class HttpDbgItem {
	public string Id { get; set; } = "";
	public string Group { get; set; } = "";
	public string Name { get; set; } = "";
	public string Method { get; set; } = "GET";
	public string Url { get; set; } = "";
	public string Query { get; set; } = "";
	public string Form { get; set; } = "";
	public string Json { get; set; } = "";
	/// <summary>params 或 json。GET 不使用正文。</summary>
	public string Body { get; set; } = "params";
}

sealed class HttpDbgBook {
	public List<HttpDbgGroup> Groups { get; set; } = new();
	public List<HttpDbgItem> Items { get; set; } = new();
	/// <summary>已经写入过「测试」分组。为真时不再补示例，删掉后不会回来。</summary>
	public bool Seeded { get; set; }
}

sealed class HttpDbgResult {
	public bool Ok { get; set; }
	public int Status { get; set; }
	public string Reason { get; set; } = "";
	public int Ms { get; set; }
	public string Head { get; set; } = "";
	public string Body { get; set; } = "";
	public string Error { get; set; } = "";
}

/// <summary>
/// HTTP 请求调试：一行一个参数，POST 可为表单或 JSON。
/// 请求存在程序目录 httpdbg.json，不写入 config.toml。
/// 还没有示例时补上分组「测试」和本程序 1224 端口的几条接口。
/// </summary>
static class HttpDebug {
	public const string FILE_NAME = "httpdbg.json";
	public const string DEFAULT_GROUP = "default";
	public const string SELF_GROUP = "self";
	const string SELF_ORIGIN = "http://127.0.0.1:1224";
	const int TIMEOUT_MS = 30000;
	const int MAX_BODY = 1024 * 1024;

	static readonly JsonSerializerOptions Opt = new() {
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	public static string StorePath() =>
		Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FILE_NAME);

	public static HttpDbgBook Load(string path, string defaultName, out string error) {
		error = null;
		var book = new HttpDbgBook();
		if (!string.IsNullOrEmpty(path) && File.Exists(path)) {
			try {
				var text = File.ReadAllText(path, Encoding.UTF8);
				book = JsonSerializer.Deserialize<HttpDbgBook>(text, Opt) ?? new HttpDbgBook();
			}
			catch (Exception ex) {
				error = ex.Message;
				book = new HttpDbgBook();
			}
		}
		normalize(book, defaultName);
		if (!book.Seeded) {
			seedself(book);
			book.Seeded = true;
		}
		return book;
	}

	public static void Save(string path, HttpDbgBook book) {
		if (string.IsNullOrEmpty(path)) throw new ArgumentException("path");
		normalize(book, "default");
		var dir = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		var json = JsonSerializer.Serialize(book, Opt);
		var tmp = path + ".tmp";
		File.WriteAllText(tmp, json, new UTF8Encoding(false));
		if (File.Exists(path)) {
			var bak = path + ".bak";
			if (!File.Exists(bak)) {
				try { File.Copy(path, bak); }
				catch { }
			}
			File.Replace(tmp, path, null);
		}
		else File.Move(tmp, path);
	}

	public static List<(string Key, string Value)> ParseLines(string text) {
		var list = new List<(string, string)>();
		if (string.IsNullOrEmpty(text)) return list;
		var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
		foreach (var raw in lines) {
			var line = raw.Trim();
			if (line.Length == 0 || line[0] == '#') continue;
			var i = line.IndexOf('=');
			if (i <= 0) continue;
			var key = line.Substring(0, i).Trim();
			var val = line.Substring(i + 1).Trim();
			if (key.Length == 0) continue;
			list.Add((key, val));
		}
		return list;
	}

	public static bool TryBuildUrl(string url, string query, out string built, out string error) {
		built = "";
		error = "";
		url = (url ?? "").Trim();
		if (url.Length == 0) {
			error = "empty";
			return false;
		}
		if (url.IndexOf("://", StringComparison.Ordinal) < 0) url = "http://" + url;
		if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host.Length == 0
			|| (uri.Scheme != "http" && uri.Scheme != "https")) {
			error = "bad";
			return false;
		}
		var sb = new StringBuilder();
		sb.Append(uri.GetLeftPart(UriPartial.Path));
		var q = uri.Query;
		if (q.StartsWith("?", StringComparison.Ordinal)) q = q.Substring(1);
		var has = q.Length > 0;
		if (has) sb.Append('?').Append(q);
		foreach (var pair in ParseLines(query)) {
			sb.Append(has ? '&' : '?');
			has = true;
			sb.Append(Uri.EscapeDataString(pair.Key));
			sb.Append('=');
			sb.Append(Uri.EscapeDataString(pair.Value));
		}
		if (!string.IsNullOrEmpty(uri.Fragment)) sb.Append(uri.Fragment);
		built = sb.ToString();
		return true;
	}

	public static string EncodeForm(string text) {
		var sb = new StringBuilder();
		foreach (var pair in ParseLines(text)) {
			if (sb.Length > 0) sb.Append('&');
			sb.Append(Uri.EscapeDataString(pair.Key));
			sb.Append('=');
			sb.Append(Uri.EscapeDataString(pair.Value));
		}
		return sb.ToString();
	}

	public static bool TryCheckJson(string text, out string error) {
		error = "";
		if (string.IsNullOrWhiteSpace(text)) {
			error = "emptyjson";
			return false;
		}
		try {
			using var doc = JsonDocument.Parse(text);
			return doc.RootElement.ValueKind != JsonValueKind.Undefined;
		}
		catch (Exception ex) {
			error = ex.Message;
			return false;
		}
	}

	public static async Task<HttpDbgResult> SendAsync(HttpDbgItem item, CancellationToken ct) {
		var r = new HttpDbgResult();
		item ??= new HttpDbgItem();
		var method = string.Equals(item.Method, "POST", StringComparison.OrdinalIgnoreCase) ? "POST" : "GET";
		if (!TryBuildUrl(item.Url, item.Query, out var url, out var uerr)) {
			r.Error = uerr;
			return r;
		}
		StringContent content = null;
		if (method == "POST") {
			if (string.Equals(item.Body, "json", StringComparison.OrdinalIgnoreCase)) {
				if (!TryCheckJson(item.Json, out var jerr)) {
					r.Error = jerr;
					return r;
				}
				content = new StringContent(item.Json ?? "", Encoding.UTF8, "application/json");
			}
			else content = new StringContent(EncodeForm(item.Form), Encoding.UTF8, "application/x-www-form-urlencoded");
		}
		var start = Environment.TickCount;
		try {
			var handler = HttpProxy.CreateHandler();
			handler.AllowAutoRedirect = false;
			using var http = new HttpClient(handler, true) { Timeout = TimeSpan.FromMilliseconds(TIMEOUT_MS) };
			using var req = new HttpRequestMessage(method == "POST" ? HttpMethod.Post : HttpMethod.Get, url);
			if (content != null) req.Content = content;
			using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
			r.Status = (int)resp.StatusCode;
			r.Reason = resp.ReasonPhrase ?? "";
			r.Head = head(resp);
			r.Body = await readbody(resp, ct).ConfigureAwait(false);
			r.Ok = true;
		}
		catch (OperationCanceledException) {
			r.Error = ct.IsCancellationRequested ? "cancel" : "timeout";
		}
		catch (Exception ex) {
			r.Error = ex.Message;
		}
		r.Ms = unchecked(Environment.TickCount - start);
		return r;
	}

	public static int SelfTest(Action<string> log, Action<string> err) {
		log ??= _ => { };
		err ??= _ => { };
		var bad = 0;
		void fail(string m) {
			err("FAIL: " + m);
			bad++;
		}
		var pairs = ParseLines("# c\n\n a = 1 \nnoeq\n=x\nb=\n");
		if (pairs.Count != 2 || pairs[0].Key != "a" || pairs[0].Value != "1" || pairs[1].Key != "b" || pairs[1].Value != "")
			fail("parse lines");
		if (!TryBuildUrl("http://127.0.0.1/p?x=1#f", "a=2\n#c\n\nb=", out var built, out _)
			|| built != "http://127.0.0.1/p?x=1&a=2&b=#f")
			fail("build url " + built);
		if (!TryBuildUrl("ex.com/p", "", out var noscheme, out _) || noscheme != "http://ex.com/p")
			fail("scheme " + noscheme);
		if (TryBuildUrl("", "", out _, out var empty) || empty != "empty") fail("empty url");
		if (TryBuildUrl("ftp://ex.com", "", out _, out var ftp) || ftp != "bad") fail("ftp");
		var form = EncodeForm("n=你好 1");
		if (form != "n=%E4%BD%A0%E5%A5%BD%201") fail("form " + form);
		if (TryCheckJson(" { ", out _)) fail("bad json accepted");
		if (!TryCheckJson("{\"a\":1}", out _)) fail("good json");
		if (TryCheckJson("  ", out var ej) || ej != "emptyjson") fail("empty json");
		var dir = Path.Combine(Path.GetTempPath(), "sk-httpdbg-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		var path = Path.Combine(dir, FILE_NAME);
		HttpListener lis = null;
		try {
			var book = new HttpDbgBook { Seeded = true };
			normalize(book, "未分组");
			book.Groups.Add(new HttpDbgGroup { Id = "g1", Name = "接口" });
			book.Items.Add(new HttpDbgItem {
				Id = "r1", Group = "g1", Name = "示例", Method = "POST", Url = "http://ex.com",
				Query = "q=1", Form = "a=1", Json = "{\"a\":1}", Body = "json",
			});
			Save(path, book);
			var back = Load(path, "未分组", out var lerr);
			if (lerr != null) fail("load " + lerr);
			if (back.Groups.Count != 2 || back.Items.Count != 1 || back.Items[0].Json != "{\"a\":1}"
				|| back.Items[0].Body != "json" || back.Groups[1].Name != "接口" || !back.Seeded)
				fail("roundtrip");
			var freshPath = Path.Combine(dir, "fresh.json");
			var fresh = Load(freshPath, "未分组", out var ferr);
			if (ferr != null) fail("fresh " + ferr);
			var self = fresh.Groups.FirstOrDefault(g => g.Id == SELF_GROUP);
			var usb = fresh.Items.FirstOrDefault(it => it.Id == "self-usb");
			var fx = fresh.Items.FirstOrDefault(it => it.Id == "self-fx");
			if (self == null || self.Name != "测试" || fresh.Items.Count(it => it.Group == SELF_GROUP) != 6)
				fail("seed group");
			if (usb == null || usb.Method != "GET" || usb.Url != SELF_ORIGIN + "/api/usb" || usb.Query.IndexOf("vid=046d", StringComparison.Ordinal) < 0)
				fail("seed usb");
			if (fx == null || fx.Method != "POST" || fx.Body != "json" || fx.Json.IndexOf("USD", StringComparison.Ordinal) < 0)
				fail("seed fx");
			fresh.Groups.RemoveAll(g => g.Id == SELF_GROUP);
			fresh.Items.RemoveAll(it => it.Group == SELF_GROUP);
			Save(freshPath, fresh);
			var kept = Load(freshPath, "未分组", out _);
			if (kept.Groups.Any(g => g.Id == SELF_GROUP) || kept.Items.Any(it => it.Id == "self-usb"))
				fail("seed returned");
			var port = freeport();
			lis = new HttpListener();
			lis.Prefixes.Add($"http://127.0.0.1:{port}/");
			lis.Start();
			var serve = Task.Run(() => serveloop(lis));
			var get = SendAsync(new HttpDbgItem {
				Method = "GET", Url = $"http://127.0.0.1:{port}/echo?x=1", Query = "a=2",
			}, CancellationToken.None).GetAwaiter().GetResult();
			log(get.Head);
			log(get.Body);
			if (!get.Ok || get.Status != 200 || get.Body.IndexOf("GET /echo?x=1&a=2", StringComparison.Ordinal) < 0)
				fail("GET " + get.Error + " " + get.Body);
			var post = SendAsync(new HttpDbgItem {
				Method = "POST", Url = $"http://127.0.0.1:{port}/echo", Body = "params", Form = "n=你好 1",
			}, CancellationToken.None).GetAwaiter().GetResult();
			log(post.Body);
			if (!post.Ok || post.Body.IndexOf("application/x-www-form-urlencoded", StringComparison.Ordinal) < 0
				|| post.Body.IndexOf("n=%E4%BD%A0%E5%A5%BD%201", StringComparison.Ordinal) < 0)
				fail("POST form " + post.Error + " " + post.Body);
			var js = SendAsync(new HttpDbgItem {
				Method = "POST", Url = $"http://127.0.0.1:{port}/echo", Body = "json", Json = "{\"a\":1}",
			}, CancellationToken.None).GetAwaiter().GetResult();
			log(js.Body);
			if (!js.Ok || js.Body.IndexOf("application/json", StringComparison.Ordinal) < 0
				|| js.Body.IndexOf("\"a\"", StringComparison.Ordinal) < 0)
				fail("POST json " + js.Error + " " + js.Body);
			var badj = SendAsync(new HttpDbgItem {
				Method = "POST", Url = $"http://127.0.0.1:{port}/echo", Body = "json", Json = "{",
			}, CancellationToken.None).GetAwaiter().GetResult();
			if (badj.Ok || badj.Status != 0 || badj.Error.Length == 0) fail("bad json sent");
			try { lis.Stop(); } catch { }
			serve.Wait(2000);
		}
		catch (Exception ex) {
			fail(ex.Message);
		}
		finally {
			try { lis?.Close(); } catch { }
			try { Directory.Delete(dir, true); } catch { }
		}
		return bad;
	}

	static void serveloop(HttpListener lis) {
		while (lis.IsListening) {
			HttpListenerContext ctx;
			try { ctx = lis.GetContext(); }
			catch { break; }
			try {
				string body;
				using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
					body = sr.ReadToEnd();
				var text = ctx.Request.HttpMethod + " " + ctx.Request.RawUrl + "\n"
					+ (ctx.Request.ContentType ?? "") + "\n" + body;
				var bytes = Encoding.UTF8.GetBytes(text);
				ctx.Response.StatusCode = 200;
				ctx.Response.ContentType = "text/plain; charset=utf-8";
				ctx.Response.ContentLength64 = bytes.Length;
				ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
				ctx.Response.OutputStream.Close();
			}
			catch { try { ctx.Response.Abort(); } catch { } }
		}
	}

	static int freeport() {
		var l = new TcpListener(IPAddress.Loopback, 0);
		l.Start();
		var port = ((IPEndPoint)l.LocalEndpoint).Port;
		l.Stop();
		return port;
	}

	static string head(HttpResponseMessage resp) {
		var sb = new StringBuilder();
		sb.Append((int)resp.StatusCode).Append(' ').Append(resp.ReasonPhrase);
		foreach (var h in resp.Headers)
			foreach (var v in h.Value)
				sb.Append("\r\n").Append(h.Key).Append(": ").Append(v);
		if (resp.Content != null) {
			foreach (var h in resp.Content.Headers)
				foreach (var v in h.Value)
					sb.Append("\r\n").Append(h.Key).Append(": ").Append(v);
		}
		return sb.ToString();
	}

	static async Task<string> readbody(HttpResponseMessage resp, CancellationToken ct) {
		if (resp.Content == null) return "";
		var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
		using (stream) {
			var buf = new MemoryStream();
			var tmp = new byte[8192];
			var left = MAX_BODY + 1;
			while (left > 0) {
				var n = await stream.ReadAsync(tmp, 0, Math.Min(tmp.Length, left), ct).ConfigureAwait(false);
				if (n <= 0) break;
				buf.Write(tmp, 0, n);
				left -= n;
			}
			var cut = buf.Length > MAX_BODY;
			var ntake = (int)Math.Min(buf.Length, MAX_BODY);
			var text = Encoding.UTF8.GetString(buf.GetBuffer(), 0, ntake);
			var ctype = resp.Content.Headers.ContentType?.MediaType ?? "";
			var trim = text.TrimStart();
			if (ctype.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0
				|| trim.StartsWith("{", StringComparison.Ordinal) || trim.StartsWith("[", StringComparison.Ordinal)) {
				try { text = TextTools.JsonPretty(text); }
				catch { }
			}
			if (cut) text += "\r\n…";
			return text;
		}
	}

	static string newid(string prefix) => prefix + Guid.NewGuid().ToString("N").Substring(0, 12);

	/// <summary>补上分组「测试」和本程序默认端口上的几条接口。分组已在时不改用户删过的请求。</summary>
	static void seedself(HttpDbgBook book) {
		if (book.Groups.Any(g => g.Id == SELF_GROUP)) return;
		var at = book.Groups.FindIndex(g => g.Id == DEFAULT_GROUP);
		var row = new HttpDbgGroup { Id = SELF_GROUP, Name = "测试" };
		if (at >= 0) book.Groups.Insert(at + 1, row);
		else book.Groups.Add(row);
		add("self-status", "状态", "GET", "/api/status", "", "");
		add("self-zhconv", "简繁", "GET", "/api/zhconv", "text=软件\nto=trad", "");
		add("self-pinyin", "拼音", "GET", "/api/pinyin", "text=中国", "");
		add("self-usb", "USB ID", "GET", "/api/usb", "vid=046d\npid=c52b", "");
		add("self-fx", "货币", "POST", "/api/fx", "", "{\n  \"amount\": 1,\n  \"from\": \"USD\",\n  \"to\": \"CNY\"\n}");
		add("self-text", "文本 Base64", "POST", "/api/text", "", "{\n  \"text\": \"hi\",\n  \"op\": \"b64enc\"\n}");

		void add(string id, string name, string method, string path, string query, string json) {
			if (book.Items.Any(it => it.Id == id)) return;
			book.Items.Add(new HttpDbgItem {
				Id = id,
				Group = SELF_GROUP,
				Name = name,
				Method = method,
				Url = SELF_ORIGIN + path,
				Query = query,
				Json = json ?? "",
				Body = string.IsNullOrEmpty(json) ? "params" : "json",
			});
		}
	}

	static void normalize(HttpDbgBook book, string defaultName) {
		if (book.Groups == null) book.Groups = new List<HttpDbgGroup>();
		if (book.Items == null) book.Items = new List<HttpDbgItem>();
		for (var i = book.Groups.Count - 1; i >= 0; i--) {
			var g = book.Groups[i];
			if (g == null || string.IsNullOrWhiteSpace(g.Id)) {
				book.Groups.RemoveAt(i);
				continue;
			}
			g.Id = g.Id.Trim();
			g.Name = (g.Name ?? "").Trim();
			if (g.Name.Length == 0) g.Name = g.Id;
		}
		if (!book.Groups.Any(g => g.Id == DEFAULT_GROUP)) {
			var name = string.IsNullOrWhiteSpace(defaultName) ? "default" : defaultName.Trim();
			book.Groups.Insert(0, new HttpDbgGroup { Id = DEFAULT_GROUP, Name = name });
		}
		var ids = new HashSet<string>(book.Groups.Select(g => g.Id));
		for (var i = book.Items.Count - 1; i >= 0; i--) {
			var it = book.Items[i];
			if (it == null) {
				book.Items.RemoveAt(i);
				continue;
			}
			if (string.IsNullOrWhiteSpace(it.Id)) it.Id = newid("r");
			it.Id = it.Id.Trim();
			it.Group = (it.Group ?? "").Trim();
			if (!ids.Contains(it.Group)) it.Group = DEFAULT_GROUP;
			it.Name = it.Name ?? "";
			it.Method = string.Equals(it.Method, "POST", StringComparison.OrdinalIgnoreCase) ? "POST" : "GET";
			it.Url = it.Url ?? "";
			it.Query = it.Query ?? "";
			it.Form = it.Form ?? "";
			it.Json = it.Json ?? "";
			it.Body = string.Equals(it.Body, "json", StringComparison.OrdinalIgnoreCase) ? "json" : "params";
		}
	}
}
