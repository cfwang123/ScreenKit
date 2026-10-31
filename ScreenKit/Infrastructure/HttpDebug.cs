using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
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
	/// <summary>请求头：一行一个 Name: Value，# 开头为注释。</summary>
	public string Headers { get; set; } = "";
	public string Form { get; set; } = "";
	public string Json { get; set; } = "";
	public string Xml { get; set; } = "";
	public string Raw { get; set; } = "";
	/// <summary>自定义正文的 Content-Type。可带 charset。</summary>
	public string Mime { get; set; } = "";
	/// <summary>params、json、xml 或 mime。GET 不使用正文。</summary>
	public string Body { get; set; } = "params";
}

sealed class HttpDbgBook {
	public List<HttpDbgGroup> Groups { get; set; } = new();
	public List<HttpDbgItem> Items { get; set; } = new();
	/// <summary>已经写入过「测试」分组。为真时不再补示例，删掉后不会回来。</summary>
	public bool Seeded { get; set; }
}

/// <summary>导出/导入的一组请求。导入时也接受整本 httpdbg.json。</summary>
sealed class HttpDbgFile {
	public string Format { get; set; } = "screenkit-httpdbg";
	public int Version { get; set; } = 1;
	public string Group { get; set; } = "";
	public List<HttpDbgItem> Items { get; set; } = new();
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
/// HTTP 请求调试：一行一个参数，请求头一行一个 Name: Value。POST 可为表单、JSON、XML，或自定义 MIME 的原文。
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
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};
	/// <summary>导出文件只留有效字段，空的 id、分组和参数不写进去。</summary>
	static readonly JsonSerializerOptions FileOpt = new() {
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};
	static string uacache;

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

	/// <summary>把一组请求写成 JSON 文本。组名一起带上，导入时据此还原分组。</summary>
	public static string ExportGroup(HttpDbgBook book, string groupId) {
		var g = book?.Groups.FirstOrDefault(x => x.Id == groupId);
		var file = new HttpDbgFile { Group = g?.Name ?? "" };
		if (book?.Items != null) {
			foreach (var it in book.Items) {
				if (it.Group != groupId) continue;
				file.Items.Add(copyitem(it));
			}
		}
		return JsonSerializer.Serialize(file, FileOpt);
	}

	/// <summary>导出用副本：id、分组和空字段留 null，写文件时整项省略。</summary>
	static HttpDbgItem copyitem(HttpDbgItem src) => new() {
		Id = null,
		Group = null,
		Name = nz(src.Name),
		Method = src.Method,
		Url = nz(src.Url),
		Query = nz(src.Query),
		Headers = nz(src.Headers),
		Form = nz(src.Form),
		Json = nz(src.Json),
		Xml = nz(src.Xml),
		Raw = nz(src.Raw),
		Mime = nz(src.Mime),
		Body = src.Body,
	};

	static string nz(string s) => string.IsNullOrEmpty(s) ? null : s;

	/// <summary>
	/// 读入导出文件（或整本 httpdbg.json），按组名并入：已有同名分组就加进去，没有就新建。
	/// 返回导入的请求数；文件不认识返回 -1，error 给原因。firstGroup 是首个请求落到的新组 id。
	/// </summary>
	public static int ImportInto(HttpDbgBook book, string json, string fallbackGroup, out string firstGroup, out string error) {
		error = "";
		firstGroup = null;
		if (book == null || string.IsNullOrWhiteSpace(json)) {
			error = "badformat";
			return -1;
		}
		JsonDocument doc;
		try { doc = JsonDocument.Parse(json); }
		catch (Exception ex) { error = ex.Message; return -1; }
		using (doc) {
			var root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object) { error = "badformat"; return -1; }
			var byname = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var g in book.Groups) {
				var name = (g.Name ?? "").Trim();
				if (name.Length > 0 && !byname.ContainsKey(name)) byname[name] = g.Id;
			}
			var hasgroups = root.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array;
			var map = new Dictionary<string, string>(StringComparer.Ordinal);
			if (hasgroups) {
				foreach (var g in groups.EnumerateArray()) {
					if (g.ValueKind != JsonValueKind.Object) continue;
					var oldid = g.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
						? (idEl.GetString() ?? "") : "";
					var name = g.TryGetProperty("name", out var nmEl) && nmEl.ValueKind == JsonValueKind.String
						? (nmEl.GetString() ?? "").Trim() : "";
					if (name.Length == 0) name = oldid.Trim();
					var target = groupfor(book, byname, name);
					if (oldid.Length > 0) map[oldid] = target;
				}
			}
			var single = !string.IsNullOrEmpty(fallbackGroup) && book.Groups.Any(g => g.Id == fallbackGroup)
				? fallbackGroup : groupfor(book, byname, "");
			if (!hasgroups && root.TryGetProperty("group", out var grpEl) && grpEl.ValueKind == JsonValueKind.String) {
				var name = (grpEl.GetString() ?? "").Trim();
				if (name.Length > 0) single = groupfor(book, byname, name);
			}
			if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) {
				error = "badformat";
				return -1;
			}
			var added = 0;
			foreach (var el in items.EnumerateArray()) {
				if (el.ValueKind != JsonValueKind.Object) continue;
				HttpDbgItem it;
				try { it = el.Deserialize<HttpDbgItem>(Opt); }
				catch { continue; }
				if (it == null) continue;
				var gid = single;
				if (hasgroups && !string.IsNullOrEmpty(it.Group) && map.TryGetValue(it.Group, out var mapped)) gid = mapped;
				it.Id = newid("r");
				it.Group = gid;
				book.Items.Add(it);
				firstGroup ??= gid;
				added++;
			}
			return added;
		}
	}

	/// <summary>按名字找分组，没有就新建；空名字落到第一个分组。</summary>
	static string groupfor(HttpDbgBook book, Dictionary<string, string> byname, string name) {
		name = (name ?? "").Trim();
		if (name.Length == 0) {
			if (book.Groups.Count > 0) return book.Groups[0].Id;
			var id0 = newid("g");
			book.Groups.Add(new HttpDbgGroup { Id = id0, Name = DEFAULT_GROUP });
			return id0;
		}
		if (byname.TryGetValue(name, out var hit) && book.Groups.Any(g => g.Id == hit)) return hit;
		var id = newid("g");
		book.Groups.Add(new HttpDbgGroup { Id = id, Name = name });
		byname[name] = id;
		return id;
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

	/// <summary>请求头：一行一个 Name: Value，# 开头为注释。格式错时 badline 为出错的行号（从 1 起）。</summary>
	public static List<(string Key, string Value)> ParseHeaders(string text, out int badline) {
		badline = 0;
		var list = new List<(string, string)>();
		if (string.IsNullOrEmpty(text)) return list;
		var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
		for (var i = 0; i < lines.Length; i++) {
			var line = lines[i].Trim();
			if (line.Length == 0 || line[0] == '#') continue;
			var at = line.IndexOf(':');
			var key = at > 0 ? line.Substring(0, at).Trim() : "";
			if (key.Length == 0 || key.IndexOfAny(new[] { ' ', '\t', '?' }) >= 0) {
				badline = i + 1;
				return list;
			}
			list.Add((key, line.Substring(at + 1).Trim()));
		}
		return list;
	}

	static bool iscontentheader(string name) =>
		name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)
		|| name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
		|| name.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase)
		|| name.Equals("Content-Language", StringComparison.OrdinalIgnoreCase)
		|| name.Equals("Content-Location", StringComparison.OrdinalIgnoreCase)
		|| name.Equals("Content-Range", StringComparison.OrdinalIgnoreCase)
		|| name.Equals("Content-MD5", StringComparison.OrdinalIgnoreCase)
		|| name.Equals("Content-Disposition", StringComparison.OrdinalIgnoreCase)
		|| name.Equals("Allow", StringComparison.OrdinalIgnoreCase)
		|| name.Equals("Expires", StringComparison.OrdinalIgnoreCase)
		|| name.Equals("Last-Modified", StringComparison.OrdinalIgnoreCase);

	/// <summary>把请求头文本框里的行加到请求上。Content-* 走正文头并覆盖默认类型，其余走请求头。</summary>
	public static bool ApplyHeaders(HttpRequestMessage req, string text, out int badline) {
		var list = ParseHeaders(text, out badline);
		if (badline > 0) return false;
		foreach (var pair in list) {
			if (iscontentheader(pair.Key)) {
				if (req.Content == null) continue;
				req.Content.Headers.Remove(pair.Key);
				req.Content.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
			}
			else {
				try { req.Headers.TryAddWithoutValidation(pair.Key, pair.Value); }
				catch { }
			}
		}
		return true;
	}

	/// <summary>地址里的查询串转成一行一个 key=value（已解码）。没有查询串时为空。</summary>
	public static string QueryOfUrl(string url) {
		url ??= "";
		var hash = url.IndexOf('#');
		var head = hash >= 0 ? url.Substring(0, hash) : url;
		var q = head.IndexOf('?');
		if (q < 0) return "";
		var sb = new StringBuilder();
		foreach (var seg in head.Substring(q + 1).Split('&')) {
			if (seg.Length == 0) continue;
			var eq = seg.IndexOf('=');
			var key = eq >= 0 ? seg.Substring(0, eq) : seg;
			var val = eq >= 0 ? seg.Substring(eq + 1) : "";
			key = Uri.UnescapeDataString(key.Replace('+', ' '));
			val = Uri.UnescapeDataString(val.Replace('+', ' '));
			if (sb.Length > 0) sb.Append('\n');
			sb.Append(key).Append('=').Append(val);
		}
		return sb.ToString();
	}

	/// <summary>把 key=value 行编码后写进地址的查询串，原有查询串被替换，锚点保留。</summary>
	public static string UrlWithQuery(string url, string query) {
		url ??= "";
		var hash = url.IndexOf('#');
		var frag = hash >= 0 ? url.Substring(hash) : "";
		var head = hash >= 0 ? url.Substring(0, hash) : url;
		var q = head.IndexOf('?');
		var basePart = q >= 0 ? head.Substring(0, q) : head;
		var sb = new StringBuilder();
		foreach (var pair in ParseLines(query)) {
			if (sb.Length > 0) sb.Append('&');
			sb.Append(Uri.EscapeDataString(pair.Key));
			sb.Append('=');
			sb.Append(Uri.EscapeDataString(pair.Value));
		}
		return basePart + (sb.Length > 0 ? "?" + sb : "") + frag;
	}

	/// <summary>POST 正文类型对应的默认 Content-Type；自定义类型用填写的 MIME。</summary>
	public static string DefaultContentType(string bodykind, string mime) {
		var kind = (bodykind ?? "").Trim().ToLowerInvariant();
		if (kind == "json") return "application/json";
		if (kind == "xml") return "application/xml";
		if (kind == "mime") {
			var m = (mime ?? "").Trim();
			return m.Length > 0 ? m : "text/plain";
		}
		return "application/x-www-form-urlencoded";
	}

	/// <summary>默认模仿 Chrome 的 User-Agent；本机装了 Chrome 就用它的主版本。</summary>
	public static string DefaultUserAgent() {
		if (uacache != null) return uacache;
		var major = "131";
		try {
			var exe = ChromeTts.FindExe(null);
			if (!string.IsNullOrEmpty(exe)) {
				var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileVersion;
				var head = (v ?? "").Split('.')[0].Trim();
				if (int.TryParse(head, out var n) && n >= 40 && n <= 999) major = head;
			}
		}
		catch { }
		uacache = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
			+ "(KHTML, like Gecko) Chrome/" + major + ".0.0.0 Safari/537.36";
		return uacache;
	}

	/// <summary>请求头文字里是否已有这一项（名称不区分大小写）。</summary>
	public static bool HasHeader(string headers, string name) {
		if (string.IsNullOrEmpty(headers) || string.IsNullOrEmpty(name)) return false;
		foreach (var raw in headers.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')) {
			var line = raw.Trim();
			if (line.Length == 0 || line[0] == '#') continue;
			var at = line.IndexOf(':');
			if (at <= 0) continue;
			if (line.Substring(0, at).Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
		}
		return false;
	}

	/// <summary>没有 User-Agent 时补一行默认的 Chrome UA；用户写了就不动。</summary>
	public static string EnsureUserAgent(string headers) {
		var text = (headers ?? "").Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
		if (HasHeader(text, "User-Agent")) return headers ?? "";
		if (text.Length > 0) text += "\r\n";
		return text + "User-Agent: " + DefaultUserAgent();
	}

	/// <summary>类型部分（分号前）小写；带 charset 的同类算相同。</summary>
	public static string MediaTypeOf(string ct) {		var s = (ct ?? "").Trim();
		var i = s.IndexOf(';');
		if (i >= 0) s = s.Substring(0, i);
		return s.Trim().ToLowerInvariant();
	}

	public static bool SameMediaType(string a, string b) {
		var ma = MediaTypeOf(a);
		return ma.Length > 0 && ma == MediaTypeOf(b);
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
		// 地址里已经有查询串就以它为准，参数行只在没有时补，避免两处重复。
		if (!has) {
			foreach (var pair in ParseLines(query)) {
				sb.Append(has ? '&' : '?');
				has = true;
				sb.Append(Uri.EscapeDataString(pair.Key));
				sb.Append('=');
				sb.Append(Uri.EscapeDataString(pair.Value));
			}
		}
		if (!string.IsNullOrEmpty(uri.Fragment)) sb.Append(uri.Fragment);
		built = sb.ToString();
		return true;
	}

	/// <summary>按给定 Content-Type 发送 UTF-8 或该类型里 charset 指定的编码。类型无效时 error 为 badmime。</summary>
	public static bool TryRawContent(string text, string mime, out StringContent content, out string error) {
		content = null;
		error = "";
		mime = (mime ?? "").Trim();
		if (mime.Length == 0 || !MediaTypeHeaderValue.TryParse(mime, out var parsed)
			|| string.IsNullOrEmpty(parsed.MediaType) || parsed.MediaType.IndexOf('/') < 0) {
			error = "badmime";
			return false;
		}
		Encoding enc;
		try {
			enc = string.IsNullOrEmpty(parsed.CharSet) ? Encoding.UTF8 : Encoding.GetEncoding(parsed.CharSet);
		}
		catch {
			error = "badmime";
			return false;
		}
		if (string.IsNullOrEmpty(parsed.CharSet)) parsed.CharSet = "utf-8";
		content = new StringContent(text ?? "", enc);
		content.Headers.ContentType = parsed;
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
			var body = (item.Body ?? "").Trim();
			if (string.Equals(body, "json", StringComparison.OrdinalIgnoreCase)) {
				if (!TryCheckJson(item.Json, out var jerr)) {
					r.Error = jerr;
					return r;
				}
				if (!TryRawContent(item.Json, "application/json", out content, out var jmime)) {
					r.Error = jmime;
					return r;
				}
			}
			else if (string.Equals(body, "xml", StringComparison.OrdinalIgnoreCase)) {
				if (!TryRawContent(item.Xml, "application/xml", out content, out var xerr)) {
					r.Error = xerr;
					return r;
				}
			}
			else if (string.Equals(body, "mime", StringComparison.OrdinalIgnoreCase)) {
				if (!TryRawContent(item.Raw, item.Mime, out content, out var merr)) {
					r.Error = merr;
					return r;
				}
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
			if (!ApplyHeaders(req, item.Headers, out var badline)) {
				r.Error = "badheader:" + badline;
				return r;
			}
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
			|| built != "http://127.0.0.1/p?x=1#f")
			fail("build url keep " + built);
		if (!TryBuildUrl("http://127.0.0.1/p#f", "a=2\n#c\n\nb=", out var builtq, out _)
			|| builtq != "http://127.0.0.1/p?a=2&b=#f")
			fail("build url add " + builtq);
		if (QueryOfUrl("http://a/p?x=1&y=%E4%BD%A0&z=a+b#f") != "x=1\ny=你\nz=a b")
			fail("query of url");
		if (QueryOfUrl("http://a/p") != "") fail("query empty");
		if (UrlWithQuery("http://a/p?x=1#f", "a=2\nb=你") != "http://a/p?a=2&b=%E4%BD%A0#f")
			fail("url with query " + UrlWithQuery("http://a/p?x=1#f", "a=2\nb=你"));
		if (UrlWithQuery("http://a/p?x=1#f", "") != "http://a/p#f") fail("url no query");
		if (DefaultContentType("params", "") != "application/x-www-form-urlencoded"
			|| DefaultContentType("json", "") != "application/json"
			|| DefaultContentType("xml", "application/atom+xml") != "application/xml"
			|| DefaultContentType("mime", "text/x-test") != "text/x-test"
			|| DefaultContentType("mime", "") != "text/plain")
			fail("default content type");
		if (!SameMediaType("application/json; charset=utf-8", "application/json")
			|| SameMediaType("application/vnd.api+json", "application/json"))
			fail("same media type");
		var ua = DefaultUserAgent();
		if (!ua.StartsWith("Mozilla/5.0", StringComparison.Ordinal)
			|| ua.IndexOf("Chrome/", StringComparison.Ordinal) < 0
			|| ua.IndexOf(" Safari/537.36", StringComparison.Ordinal) < 0)
			fail("user agent " + ua);
		if (!HasHeader("x-a: 1\r\nuser-agent: mine", "User-Agent")) fail("has header");
		if (HasHeader("# User-Agent: c\nX-A: 1", "User-Agent")) fail("has header comment");
		if (EnsureUserAgent("").IndexOf("User-Agent: Mozilla/5.0", StringComparison.Ordinal) < 0)
			fail("ensure ua empty");
		if (EnsureUserAgent("X-A: 1\r\nUser-Agent: mine") != "X-A: 1\r\nUser-Agent: mine")
			fail("ensure ua keep");
		if (!EnsureUserAgent("X-A: 1").StartsWith("X-A: 1\r\nUser-Agent: ", StringComparison.Ordinal))
			fail("ensure ua append");
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
				Query = "q=1", Headers = "X-Test: 1", Form = "a=1", Json = "{\"a\":1}", Xml = "<a/>", Raw = "hi",
				Mime = "text/plain", Body = "mime",
			});
			Save(path, book);
			var back = Load(path, "未分组", out var lerr);
			if (lerr != null) fail("load " + lerr);
			if (back.Groups.Count != 2 || back.Items.Count != 1 || back.Items[0].Json != "{\"a\":1}"
				|| back.Items[0].Xml != "<a/>" || back.Items[0].Raw != "hi" || back.Items[0].Mime != "text/plain"
				|| back.Items[0].Headers != "X-Test: 1"
				|| back.Items[0].Body != "mime" || back.Groups[1].Name != "接口" || !back.Seeded)
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
			var expbook = new HttpDbgBook { Seeded = true };
			normalize(expbook, "未分组");
			var exported = ExportGroup(back, "g1");
			log(exported);
			if (exported.IndexOf("\"format\"", StringComparison.Ordinal) < 0
				|| exported.IndexOf("\"接口\"", StringComparison.Ordinal) < 0)
				fail("export json " + exported);
			var nimp = ImportInto(expbook, exported, DEFAULT_GROUP, out var firstg, out var imperr);
			if (!string.IsNullOrEmpty(imperr) || nimp != 1) fail("import " + imperr + " " + nimp);
			if (!expbook.Groups.Any(g => g.Name == "接口") || expbook.Items.Count != 1) fail("import group");
			var impitem = expbook.Items[0];
			if (impitem.Json != "{\"a\":1}" || impitem.Headers != "X-Test: 1"
				|| impitem.Group != expbook.Groups.First(g => g.Name == "接口").Id
				|| firstg != impitem.Group)
				fail("import item");
			if (ImportInto(expbook, exported, DEFAULT_GROUP, out _, out _) != 1 || expbook.Items.Count != 2
				|| expbook.Groups.Count != 2)
				fail("import twice");
			if (ImportInto(expbook, "{}", DEFAULT_GROUP, out _, out var e1) != -1 || e1 != "badformat")
				fail("import bad");
			if (ImportInto(expbook, "{", DEFAULT_GROUP, out _, out _) != -1) fail("import broken json");
			var wholebook = new HttpDbgBook { Seeded = true };
			if (ImportInto(wholebook, JsonSerializer.Serialize(back, Opt), DEFAULT_GROUP, out _, out var wher) != 1
				|| !string.IsNullOrEmpty(wher) || wholebook.Groups.Count != 2)
				fail("import whole " + wher + " " + wholebook.Groups.Count);
			var port = freeport();
			lis = new HttpListener();
			lis.Prefixes.Add($"http://127.0.0.1:{port}/");
			lis.Start();
			var serve = Task.Run(() => serveloop(lis));
			var get = SendAsync(new HttpDbgItem {
				Method = "GET", Url = $"http://127.0.0.1:{port}/echo", Query = "a=2",
			}, CancellationToken.None).GetAwaiter().GetResult();
			log(get.Head);
			log(get.Body);
			if (!get.Ok || get.Status != 200 || get.Body.IndexOf("GET /echo?a=2", StringComparison.Ordinal) < 0)
				fail("GET " + get.Error + " " + get.Body);
			var getq = SendAsync(new HttpDbgItem {
				Method = "GET", Url = $"http://127.0.0.1:{port}/echo?x=1", Query = "a=2",
			}, CancellationToken.None).GetAwaiter().GetResult();
			log(getq.Body);
			if (!getq.Ok || getq.Body.IndexOf("GET /echo?x=1", StringComparison.Ordinal) < 0
				|| getq.Body.IndexOf("a=2", StringComparison.Ordinal) >= 0)
				fail("GET url query wins " + getq.Error + " " + getq.Body);
			var post = SendAsync(new HttpDbgItem {
				Method = "POST", Url = $"http://127.0.0.1:{port}/echo", Body = "params", Form = "n=你好 1",
			}, CancellationToken.None).GetAwaiter().GetResult();
			log(post.Body);
			if (!post.Ok || post.Body.IndexOf("application/x-www-form-urlencoded", StringComparison.Ordinal) < 0
				|| post.Body.IndexOf("n=%E4%BD%A0%E5%A5%BD%201", StringComparison.Ordinal) < 0)
				fail("POST form " + post.Error + " " + post.Body);
			var hd = SendAsync(new HttpDbgItem {
				Method = "POST", Url = $"http://127.0.0.1:{port}/echo", Body = "params", Form = "a=1",
				Headers = "# 注释\nX-Test: hello\nX-Other: 1\n",
			}, CancellationToken.None).GetAwaiter().GetResult();
			log(hd.Body);
			if (!hd.Ok || hd.Body.IndexOf("\nhello\n", StringComparison.Ordinal) < 0)
				fail("POST headers " + hd.Error + " " + hd.Body);
			var ct = SendAsync(new HttpDbgItem {
				Method = "POST", Url = $"http://127.0.0.1:{port}/echo", Body = "mime",
				Mime = "text/plain; charset=utf-8", Raw = "hi", Headers = "Content-Type: application/xml\nX-Test: 2",
			}, CancellationToken.None).GetAwaiter().GetResult();
			log(ct.Body);
			if (!ct.Ok || ct.Body.IndexOf("application/xml", StringComparison.Ordinal) < 0)
				fail("content-type header " + ct.Error + " " + ct.Body);
			var bhd = SendAsync(new HttpDbgItem {
				Method = "GET", Url = $"http://127.0.0.1:{port}/echo", Headers = "oops\nX-Test: 1",
			}, CancellationToken.None).GetAwaiter().GetResult();
			if (bhd.Ok || bhd.Error != "badheader:1") fail("bad header " + bhd.Error);
			var hp = ParseHeaders("A: 1\n# c\n\nB:2:3\n", out var hbline);
			if (hbline != 0 || hp.Count != 2 || hp[0].Value != "1" || hp[1].Value != "2:3") fail("parse headers");
			ParseHeaders("A: 1\n nope\n", out var hbline2);
			if (hbline2 != 2) fail("header line " + hbline2);
			var js = SendAsync(new HttpDbgItem {
				Method = "POST", Url = $"http://127.0.0.1:{port}/echo", Body = "json", Json = "{\"a\":1}",
			}, CancellationToken.None).GetAwaiter().GetResult();
			log(js.Body);
			if (!js.Ok || js.Body.IndexOf("application/json", StringComparison.Ordinal) < 0
				|| js.Body.IndexOf("\"a\"", StringComparison.Ordinal) < 0)
				fail("POST json " + js.Error + " " + js.Body);
			var xml = SendAsync(new HttpDbgItem {
				Method = "POST", Url = $"http://127.0.0.1:{port}/echo", Body = "xml", Xml = "<a>1</a>",
			}, CancellationToken.None).GetAwaiter().GetResult();
			log(xml.Body);
			if (!xml.Ok || xml.Body.IndexOf("application/xml", StringComparison.Ordinal) < 0
				|| xml.Body.IndexOf("<a>1</a>", StringComparison.Ordinal) < 0)
				fail("POST xml " + xml.Error + " " + xml.Body);
			var raw = SendAsync(new HttpDbgItem {
				Method = "POST", Url = $"http://127.0.0.1:{port}/echo", Body = "mime",
				Mime = "text/plain; charset=utf-8", Raw = "hi",
			}, CancellationToken.None).GetAwaiter().GetResult();
			log(raw.Body);
			if (!raw.Ok || raw.Body.IndexOf("text/plain", StringComparison.Ordinal) < 0
				|| raw.Body.IndexOf("hi", StringComparison.Ordinal) < 0)
				fail("POST mime " + raw.Error + " " + raw.Body);
			var badm = SendAsync(new HttpDbgItem {
				Method = "POST", Url = $"http://127.0.0.1:{port}/echo", Body = "mime", Mime = "nope", Raw = "x",
			}, CancellationToken.None).GetAwaiter().GetResult();
			if (badm.Ok || badm.Error != "badmime") fail("bad mime sent " + badm.Error);
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
					+ (ctx.Request.ContentType ?? "") + "\n" + (ctx.Request.Headers["X-Test"] ?? "") + "\n" + body;
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
			it.Headers = it.Headers ?? "";
			it.Form = it.Form ?? "";
			it.Json = it.Json ?? "";
			it.Xml = it.Xml ?? "";
			it.Raw = it.Raw ?? "";
			it.Mime = (it.Mime ?? "").Trim();
			var body = (it.Body ?? "").Trim();
			if (!string.Equals(body, "json", StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(body, "xml", StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(body, "mime", StringComparison.OrdinalIgnoreCase))
				body = "params";
			it.Body = body.ToLowerInvariant();
		}
	}
}
