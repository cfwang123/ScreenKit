using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenKit;

/// <summary>局域网文件传输：UDP 发现 + HTTP（配对后访问 sendfile/）。</summary>
public sealed class SendFileServer : IDisposable {
	readonly object listenLock = new();
	readonly Func<OcrOptions> getOpts;
	readonly Action save;
	HttpListener listener;
	UdpClient udp;
	volatile bool running;
	bool disposed;
	public readonly SendFileAuth Auth;
	public readonly SendFileText Text = new();
	public string LastDeviceId = "";
	public event Action<string> Logged;

	static readonly JsonSerializerOptions JsonUtf8 = new(JsonSerializerOptions.Default) {
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	const string DISCOVER = "SCREENKIT_DISCOVER";
	const int MAXTEXT = 65536;

	public SendFileServer(Func<OcrOptions> optionsFactory, Action saveCfg) {
		getOpts = optionsFactory ?? throw new ArgumentNullException(nameof(optionsFactory));
		save = saveCfg;
		Auth = new SendFileAuth(getOpts, save);
	}

	public bool IsRunning => running;

	public void Start() {
		Compat.ThrowIfDisposed(disposed, this);
		var o = getOpts() ?? new OcrOptions();
		var httpPort = Compat.Clamp(o.SendFilePort <= 0 ? 17532 : o.SendFilePort, 1, 65535);
		var udpPort = Compat.Clamp(o.SendFileUdpPort <= 0 ? 17531 : o.SendFileUdpPort, 1, 65535);
		SendFilePaths.EnsureRoot();
		ensurepcid(o);
		lock (listenLock) {
			Stop();
			var l = new HttpListener();
			var n = 0;
			foreach (var ip in lanips()) {
				try {
					l.Prefixes.Add($"http://{ip}:{httpPort}/");
					n++;
				}
				catch { }
			}
			if (n == 0)
				throw new InvalidOperationException("无法绑定 HTTP 前缀");
			l.Start();
			listener = l;
			running = true;
			_ = Task.Run(acceptloop);
			try {
				udp = new UdpClient(udpPort);
				udp.EnableBroadcast = true;
				_ = Task.Run(udploop);
			}
			catch (Exception ex) {
				log($"UDP :{udpPort} 失败: {ex.Message}");
			}
		}
		tryfirewall(httpPort, udpPort);
		log($"SendFile HTTP :{httpPort} UDP :{udpPort}");
	}

	public void Stop() {
		running = false;
		HttpListener l;
		UdpClient u;
		lock (listenLock) {
			l = listener;
			listener = null;
			u = udp;
			udp = null;
		}
		if (l != null) {
			try { l.Abort(); } catch {
				try { l.Stop(); } catch { }
			}
			try { l.Close(); } catch { }
		}
		if (u != null) {
			try { u.Close(); } catch { }
		}
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		Stop();
	}

	void ensurepcid(OcrOptions o) {
		if (!string.IsNullOrWhiteSpace(o.SendFilePcId)) return;
		o.SendFilePcId = Guid.NewGuid().ToString("N");
		try { save?.Invoke(); } catch { }
	}

	async Task acceptloop() {
		while (running) {
			HttpListener l;
			lock (listenLock) l = listener;
			if (l == null || !l.IsListening) break;
			HttpListenerContext ctx;
			try {
				ctx = await l.GetContextAsync().ConfigureAwait(false);
			}
			catch (ObjectDisposedException) { break; }
			catch (HttpListenerException) { break; }
			catch {
				if (!running) break;
				continue;
			}
			_ = Task.Run(() => handle(ctx));
		}
	}

	void udploop() {
		while (running) {
			UdpClient u;
			lock (listenLock) u = udp;
			if (u == null) break;
			try {
				IPEndPoint ep = null;
				var data = u.Receive(ref ep);
				if (ep == null || data == null || data.Length < 8) continue;
				var s = Encoding.UTF8.GetString(data).Trim();
				if (!s.StartsWith(DISCOVER, StringComparison.OrdinalIgnoreCase)) continue;
				var o = getOpts() ?? new OcrOptions();
				if (!o.SendFileEnabled) continue;
				var name = string.IsNullOrWhiteSpace(o.SendFileName) ? Environment.MachineName : o.SendFileName.Trim();
				var httpPort = Compat.Clamp(o.SendFilePort <= 0 ? 17532 : o.SendFilePort, 1, 65535);
				var json = new JsonObject {
					["v"] = 1,
					["name"] = name,
					["httpPort"] = httpPort,
					["pcId"] = o.SendFilePcId ?? "",
					["cmd"] = "discover",
				}.ToJsonString(JsonUtf8);
				var bytes = Encoding.UTF8.GetBytes(json);
				try { u.Send(bytes, bytes.Length, ep); } catch { }
			}
			catch (ObjectDisposedException) { break; }
			catch (SocketException) {
				if (!running) break;
			}
			catch {
				if (!running) break;
			}
		}
	}

	void handle(HttpListenerContext ctx) {
		try {
			var req = ctx.Request;
			var method = req.HttpMethod ?? "";
			var pathRaw = (req.Url?.AbsolutePath ?? "/").TrimEnd('/');
			if (pathRaw.Length == 0) pathRaw = "/";
			if (string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase)) {
				writecors(ctx, 204);
				return;
			}
			var path = pathRaw.ToLowerInvariant();
			if (path is "/api/sendfile/pair") {
				if (!ispost(method)) { writejson(ctx, 405, err(805, "pair 仅支持 POST")); return; }
				handlepair(ctx);
				return;
			}
			var dev = authed(req);
			if (dev == null) {
				writejson(ctx, 401, err(401, "未配对或 token 无效"));
				return;
			}
			LastDeviceId = dev.Id ?? "";
			if (path is "/api/sendfile/info") {
				if (!isget(method)) { writejson(ctx, 405, err(805, "info 仅支持 GET")); return; }
				handleinfo(ctx, dev);
				return;
			}
			if (path is "/api/sendfile/list") {
				if (!isget(method)) { writejson(ctx, 405, err(805, "list 仅支持 GET")); return; }
				handlelist(ctx);
				return;
			}
			if (path is "/api/sendfile/download") {
				if (!isget(method)) { writejson(ctx, 405, err(805, "download 仅支持 GET")); return; }
				handledownload(ctx);
				return;
			}
			if (path is "/api/sendfile/upload") {
				if (!ispost(method)) { writejson(ctx, 405, err(805, "upload 仅支持 POST")); return; }
				handleupload(ctx);
				return;
			}
			if (path is "/api/sendfile/mkdir") {
				if (!ispost(method)) { writejson(ctx, 405, err(805, "mkdir 仅支持 POST")); return; }
				handlemkdir(ctx);
				return;
			}
			if (path is "/api/sendfile/delete") {
				if (!string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase)
					&& !ispost(method)) {
					writejson(ctx, 405, err(805, "delete 仅支持 DELETE/POST"));
					return;
				}
				handledelete(ctx);
				return;
			}
			if (path is "/api/sendfile/text") {
				if (isget(method)) { handletextget(ctx, dev); return; }
				if (ispost(method)) { handletextpost(ctx); return; }
				writejson(ctx, 405, err(805, "text 仅支持 GET/POST"));
				return;
			}
			writejson(ctx, 404, err(404, "未知路径"));
		}
		catch (Exception ex) {
			try { writejson(ctx, 500, err(900, ex.Message)); } catch { }
		}
	}

	void handlepair(HttpListenerContext ctx) {
		var body = readjson(ctx.Request);
		var id = str(body, "id");
		var name = str(body, "name");
		if (string.IsNullOrWhiteSpace(id)) {
			writejson(ctx, 200, err(802, "缺少 id"));
			return;
		}
		var ip = ctx.Request.RemoteEndPoint?.Address?.ToString() ?? "";
		var tokenHdr = bearertoken(ctx.Request);
		var existing = Auth.Find(id, tokenHdr ?? "");
		if (existing != null) {
			LastDeviceId = existing.Id;
			writejson(ctx, 200, ok(new JsonObject {
				["token"] = existing.Token,
				["name"] = getOpts()?.SendFileName ?? Environment.MachineName,
				["pcId"] = getOpts()?.SendFilePcId ?? "",
			}));
			return;
		}
		var dev = Auth.Pair(id, name, ip);
		if (dev == null) {
			writejson(ctx, 403, err(403, "已拒绝配对"));
			return;
		}
		LastDeviceId = dev.Id;
		writejson(ctx, 200, ok(new JsonObject {
			["token"] = dev.Token,
			["name"] = getOpts()?.SendFileName ?? Environment.MachineName,
			["pcId"] = getOpts()?.SendFilePcId ?? "",
		}));
	}

	void handleinfo(HttpListenerContext ctx, SendFileDevice dev) {
		var o = getOpts() ?? new OcrOptions();
		SendFilePaths.EnsureRoot();
		writejson(ctx, 200, ok(new JsonObject {
			["name"] = string.IsNullOrWhiteSpace(o.SendFileName) ? Environment.MachineName : o.SendFileName,
			["pcId"] = o.SendFilePcId ?? "",
			["device"] = dev.Name ?? "",
			["root"] = "sendfile",
		}));
	}

	void handlelist(HttpListenerContext ctx) {
		var rel = ctx.Request.QueryString["path"] ?? "";
		var deep = truthy(ctx.Request.QueryString["deep"]);
		try {
			var items = SendFileOps.List(rel, deep);
			writejson(ctx, 200, ok(new JsonObject {
				["path"] = rel ?? "",
				["items"] = items,
			}));
		}
		catch (InvalidOperationException ex) {
			writejson(ctx, 200, err(410, ex.Message));
		}
	}

	void handledownload(HttpListenerContext ctx) {
		var rel = ctx.Request.QueryString["path"] ?? "";
		if (!SendFilePaths.TryResolve(rel, out var full, out var pathErr)) {
			writejson(ctx, 200, err(410, pathErr ?? "路径非法"));
			return;
		}
		if (!File.Exists(full)) {
			writejson(ctx, 200, err(410, "文件不存在"));
			return;
		}
		FileStream fs = null;
		try {
			fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
			var res = ctx.Response;
			res.StatusCode = 200;
			res.ContentType = "application/octet-stream";
			res.ContentLength64 = fs.Length;
			res.Headers["Access-Control-Allow-Origin"] = "*";
			var fn = Path.GetFileName(full) ?? "file";
			res.Headers["Content-Disposition"] = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(fn)}";
			fs.CopyTo(res.OutputStream);
		}
		finally {
			try { fs?.Dispose(); } catch { }
			try { ctx.Response.OutputStream.Close(); } catch { }
			try { ctx.Response.Close(); } catch { }
		}
	}

	void handleupload(HttpListenerContext ctx) {
		var rel = ctx.Request.QueryString["path"] ?? "";
		try {
			SendFileOps.SaveStream(rel, ctx.Request.InputStream);
			writejson(ctx, 200, ok(new JsonObject { ["path"] = rel ?? "" }));
		}
		catch (InvalidOperationException ex) {
			writejson(ctx, 200, err(410, ex.Message));
		}
	}

	void handlemkdir(HttpListenerContext ctx) {
		var body = readjson(ctx.Request);
		var rel = str(body, "path");
		if (string.IsNullOrWhiteSpace(rel))
			rel = ctx.Request.QueryString["path"] ?? "";
		try {
			SendFileOps.Mkdir(rel);
			writejson(ctx, 200, ok(new JsonObject { ["path"] = rel ?? "" }));
		}
		catch (InvalidOperationException ex) {
			writejson(ctx, 200, err(410, ex.Message));
		}
	}

	void handledelete(HttpListenerContext ctx) {
		var rel = ctx.Request.QueryString["path"] ?? "";
		if (string.IsNullOrWhiteSpace(rel)) {
			var body = readjson(ctx.Request);
			rel = str(body, "path");
		}
		try {
			SendFileOps.Delete(rel);
			writejson(ctx, 200, ok(new JsonObject { ["path"] = rel ?? "" }));
		}
		catch (InvalidOperationException ex) {
			writejson(ctx, 200, err(410, ex.Message));
		}
	}

	void handletextget(HttpListenerContext ctx, SendFileDevice dev) {
		long since = 0;
		var q = ctx.Request.QueryString["since"];
		if (!string.IsNullOrWhiteSpace(q))
			long.TryParse(q, NumberStyles.Integer, CultureInfo.InvariantCulture, out since);
		var list = Text.PullOut(dev.Id, since);
		var arr = new JsonArray();
		foreach (var m in list) {
			arr.Add(new JsonObject {
				["id"] = m.Id,
				["text"] = m.Text ?? "",
				["time"] = m.Unix,
			});
		}
		writejson(ctx, 200, ok(arr));
	}

	void handletextpost(HttpListenerContext ctx) {
		var body = readjson(ctx.Request);
		var text = str(body, "text");
		if (text.Length > MAXTEXT) text = text.Substring(0, MAXTEXT);
		if (text.Length == 0) {
			writejson(ctx, 200, err(802, "缺少 text"));
			return;
		}
		var msg = Text.PushInbox(text);
		writejson(ctx, 200, ok(new JsonObject { ["id"] = msg.Id }));
	}

	SendFileDevice authed(HttpListenerRequest req) {
		var id = req.Headers["X-Device-Id"] ?? req.QueryString["device"] ?? "";
		var token = bearertoken(req);
		if (string.IsNullOrEmpty(token))
			token = req.Headers["X-Token"] ?? "";
		return Auth.Find(id, token);
	}

	static string bearertoken(HttpListenerRequest req) {
		var h = req.Headers["Authorization"] ?? "";
		if (h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
			return h.Substring(7).Trim();
		return "";
	}

	static JsonObject readjson(HttpListenerRequest req) {
		try {
			using var sr = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
			var s = sr.ReadToEnd();
			if (string.IsNullOrWhiteSpace(s)) return new JsonObject();
			return JsonNode.Parse(s) as JsonObject ?? new JsonObject();
		}
		catch { return new JsonObject(); }
	}

	static string str(JsonObject o, string key) {
		if (o == null) return "";
		try {
			if (!o.TryGetPropertyValue(key, out var n) || n == null) return "";
			if (n is JsonValue jv) {
				try { return jv.GetValue<string>() ?? ""; }
				catch { return jv.ToString() ?? ""; }
			}
			return n.ToString() ?? "";
		}
		catch { }
		return "";
	}

	static bool truthy(string s) {
		s = (s ?? "").Trim();
		return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("yes", StringComparison.OrdinalIgnoreCase);
	}

	static bool isget(string m) => string.Equals(m, "GET", StringComparison.OrdinalIgnoreCase);
	static bool ispost(string m) => string.Equals(m, "POST", StringComparison.OrdinalIgnoreCase);

	static JsonObject ok(JsonNode data) => new() {
		["code"] = 100,
		["data"] = data,
	};

	static JsonObject err(int code, string msg) => new() {
		["code"] = code,
		["data"] = msg ?? "",
	};

	static void writecors(HttpListenerContext ctx, int status) {
		var res = ctx.Response;
		res.StatusCode = status;
		res.Headers["Access-Control-Allow-Origin"] = "*";
		res.Headers["Access-Control-Allow-Methods"] = "GET, POST, DELETE, OPTIONS";
		res.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization, X-Device-Id, X-Token";
		res.ContentLength64 = 0;
		try { res.Close(); } catch { }
	}

	static void writejson(HttpListenerContext ctx, int httpStatus, JsonNode body) {
		var bytes = Encoding.UTF8.GetBytes(body.ToJsonString(JsonUtf8));
		var res = ctx.Response;
		res.StatusCode = httpStatus;
		res.ContentType = "application/json; charset=utf-8";
		res.ContentEncoding = Encoding.UTF8;
		res.ContentLength64 = bytes.Length;
		res.Headers["Access-Control-Allow-Origin"] = "*";
		res.Headers["Access-Control-Allow-Methods"] = "GET, POST, DELETE, OPTIONS";
		res.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization, X-Device-Id, X-Token";
		try {
			res.OutputStream.Write(bytes, 0, bytes.Length);
		}
		finally {
			try { res.OutputStream.Close(); } catch { }
			try { res.Close(); } catch { }
		}
	}

	static List<string> lanips() {
		var list = new List<string> { "127.0.0.1" };
		try {
			foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()) {
				if (ni.OperationalStatus != OperationalStatus.Up) continue;
				if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
				foreach (var ua in ni.GetIPProperties().UnicastAddresses) {
					if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
					var ip = ua.Address.ToString();
					if (!list.Contains(ip)) list.Add(ip);
				}
			}
		}
		catch { }
		return list;
	}

	static void tryfirewall(int tcp, int udp) {
		runnetsh($"advfirewall firewall delete rule name=\"ScreenKit SendFile TCP\"");
		runnetsh($"advfirewall firewall delete rule name=\"ScreenKit SendFile UDP\"");
		runnetsh($"advfirewall firewall add rule name=\"ScreenKit SendFile TCP\" dir=in action=allow protocol=TCP localport={tcp}");
		runnetsh($"advfirewall firewall add rule name=\"ScreenKit SendFile UDP\" dir=in action=allow protocol=UDP localport={udp}");
	}

	static void runnetsh(string args) {
		try {
			var psi = new ProcessStartInfo {
				FileName = "netsh",
				Arguments = args,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};
			using var p = Process.Start(psi);
			if (p == null) return;
			p.WaitForExit(4000);
		}
		catch { }
	}

	void log(string s) {
		try { Logged?.Invoke(s); } catch { }
	}
}
