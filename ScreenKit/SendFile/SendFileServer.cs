using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenKit;

/// <summary>局域网文件传输：UDP 发现 + HTTP（配对后访问 sendfile/）。</summary>
public sealed partial class SendFileServer : IDisposable {
	readonly object listenLock = new();
	readonly Func<OcrOptions> getOpts;
	readonly Action save;
	TcpListener tcp;
	UdpClient udp;
	volatile bool running;
	bool disposed;
	public readonly SendFileAuth Auth;
	public readonly SendFileWeb Web;
	public readonly SendFileText Text = new();
	readonly SendFileOutbox Outbox = new();
	public readonly SendFileJobs Jobs = new();
	public string LastDeviceId = "";
	string lastSeenId = "";
	int lastSeenTick;
	public event Action<string> Logged;

	static readonly JsonSerializerOptions JsonUtf8 = new(JsonSerializerOptions.Default) {
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	const string DISCOVER = "SCREENKIT_DISCOVER";
	const string STAGING = ".to_phone";
	const int MAXTEXT = 65536;
	const int MAXHDR = 65536;
	const int ONLINE_MS = 10000;

	public SendFileServer(Func<OcrOptions> optionsFactory, Action saveCfg) {
		getOpts = optionsFactory ?? throw new ArgumentNullException(nameof(optionsFactory));
		save = saveCfg;
		Auth = new SendFileAuth(getOpts, save);
		Web = new SendFileWeb(getOpts, save);
	}

	public bool IsRunning => running;

	public bool PhoneOnline {
		get {
			if (!running || string.IsNullOrEmpty(lastSeenId)) return false;
			return unchecked(Environment.TickCount - lastSeenTick) < ONLINE_MS;
		}
	}

	public string OnlineDeviceId => PhoneOnline ? lastSeenId : "";

	void touch(string id) {
		if (string.IsNullOrWhiteSpace(id)) return;
		lastSeenId = id;
		lastSeenTick = Environment.TickCount;
		LastDeviceId = id;
	}

	/// <summary>把本地文件/文件夹拷进 .to_phone 并入队；返回入队文件数。</summary>
	public int PushFilesToPhone(string deviceId, IEnumerable<string> srcPaths) {
		if (string.IsNullOrWhiteSpace(deviceId) || srcPaths == null) return 0;
		var n = 0;
		foreach (var src in srcPaths) {
			if (string.IsNullOrWhiteSpace(src)) continue;
			try {
				var destRel = SendFileOps.Import(src, STAGING);
				n += enqueueimported(deviceId, destRel);
			}
			catch { }
		}
		return n;
	}

	public int PushStoreToPhone(string deviceId, string storeRel) {
		if (string.IsNullOrWhiteSpace(deviceId)) return 0;
		return enqueueimported(deviceId, storeRel);
	}

	int enqueueimported(string deviceId, string destRel) {
		if (!SendFilePaths.TryResolve(destRel, out var full, out _)) return 0;
		if (File.Exists(full)) {
			long size = 0;
			try { size = new FileInfo(full).Length; } catch { }
			var phoneRel = stripstaging(destRel);
			Outbox.Add(deviceId, destRel, phoneRel, size);
			Jobs.Add(Path.GetFileName(phoneRel), toPhone: true, size, destRel);
			return 1;
		}
		if (!Directory.Exists(full)) return 0;
		var n = 0;
		foreach (var f in Directory.GetFiles(full, "*", SearchOption.AllDirectories)) {
			var rel = SendFilePaths.RelFrom(f);
			long size = 0;
			try { size = new FileInfo(f).Length; } catch { }
			var phoneRel = stripstaging(rel);
			Outbox.Add(deviceId, rel, phoneRel, size);
			Jobs.Add(Path.GetFileName(phoneRel), toPhone: true, size, rel);
			n++;
		}
		return n;
	}

	static string stripstaging(string rel) {
		rel = (rel ?? "").Replace('\\', '/').Trim('/');
		var p = STAGING + "/";
		if (rel.StartsWith(p, StringComparison.OrdinalIgnoreCase))
			return rel.Substring(p.Length);
		return rel;
	}

	static bool isstaging(string rel) {
		rel = (rel ?? "").Replace('\\', '/').Trim('/');
		return rel.Equals(STAGING, StringComparison.OrdinalIgnoreCase)
			|| rel.StartsWith(STAGING + "/", StringComparison.OrdinalIgnoreCase);
	}

	public void Start() {
		Compat.ThrowIfDisposed(disposed, this);
		var o = getOpts() ?? new OcrOptions();
		var httpPort = Compat.Clamp(o.SendFilePort <= 0 ? 17532 : o.SendFilePort, 1, 65535);
		var udpPort = Compat.Clamp(o.SendFileUdpPort <= 0 ? 17531 : o.SendFileUdpPort, 1, 65535);
		SendFilePaths.EnsureRoot();
		ensurepcid(o);
		try { Web.EnsurePass(); } catch { }
		lock (listenLock) {
			Stop();
			tcp = bindtcp(httpPort);
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
		TcpListener t;
		UdpClient u;
		lock (listenLock) {
			t = tcp;
			tcp = null;
			u = udp;
			udp = null;
		}
		if (t != null) {
			try { t.Stop(); } catch { }
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

	static TcpListener bindtcp(int port) {
		TcpListener l6 = null;
		try {
			l6 = new TcpListener(IPAddress.IPv6Any, port);
			l6.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
			l6.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
			l6.Start();
			return l6;
		}
		catch {
			try { l6?.Stop(); } catch { }
		}
		var l = new TcpListener(IPAddress.Any, port);
		l.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
		l.Start();
		return l;
	}

	async Task acceptloop() {
		while (running) {
			TcpListener l;
			lock (listenLock) l = tcp;
			if (l == null) break;
			TcpClient c;
			try {
				c = await l.AcceptTcpClientAsync().ConfigureAwait(false);
			}
			catch (ObjectDisposedException) { break; }
			catch (SocketException) {
				if (!running) break;
				continue;
			}
			catch {
				if (!running) break;
				continue;
			}
			_ = Task.Run(() => serve(c));
		}
	}

	void serve(TcpClient c) {
		try {
			c.NoDelay = true;
			c.ReceiveTimeout = 120000;
			c.SendTimeout = 120000;
			var ns = c.GetStream();
			var ctx = readctx(ns, c.Client.RemoteEndPoint as IPEndPoint);
			if (ctx == null) return;
			handle(ctx);
			try { ctx.Response.Close(); } catch { }
		}
		catch { }
		finally {
			try { c.Close(); } catch { }
		}
	}

	static SfCtx readctx(NetworkStream ns, IPEndPoint remote) {
		var buf = new byte[MAXHDR];
		var n = 0;
		var end = -1;
		while (n < MAXHDR) {
			var r = ns.Read(buf, n, Math.Min(1024, MAXHDR - n));
			if (r <= 0) return null;
			n += r;
			end = findhdrend(buf, n);
			if (end >= 0) break;
		}
		if (end < 0) return null;
		var text = Encoding.ASCII.GetString(buf, 0, end);
		var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
		if (lines.Length == 0) return null;
		var parts = lines[0].Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length < 2) return null;
		var method = parts[0];
		var target = parts[1];
		Uri uri;
		try { uri = new Uri("http://localhost" + (target.StartsWith("/") ? target : "/" + target)); }
		catch { return null; }
		var headers = new SfHeaders();
		for (var i = 1; i < lines.Length; i++) {
			var line = lines[i];
			if (string.IsNullOrEmpty(line)) continue;
			var colon = line.IndexOf(':');
			if (colon <= 0) continue;
			headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
		}
		long clen = 0;
		var cls = headers["Content-Length"];
		if (!string.IsNullOrEmpty(cls))
			long.TryParse(cls, NumberStyles.Integer, CultureInfo.InvariantCulture, out clen);
		if (clen < 0) clen = 0;
		byte[] leftover;
		if (n > end) {
			leftover = new byte[n - end];
			Buffer.BlockCopy(buf, end, leftover, 0, n - end);
		}
		else leftover = Array.Empty<byte>();
		var input = new SfIn(ns, leftover, clen);
		var query = parsequery(uri.Query);
		var req = new SfReq {
			HttpMethod = method,
			Url = uri,
			QueryString = query,
			Headers = headers,
			InputStream = input,
			ContentEncoding = encodingof(headers["Content-Type"]),
			RemoteEndPoint = remote,
			ContentLength = clen,
		};
		var res = new SfRes(ns);
		return new SfCtx { Request = req, Response = res };
	}

	static int findhdrend(byte[] b, int n) {
		for (var i = 0; i + 3 < n; i++) {
			if (b[i] == 13 && b[i + 1] == 10 && b[i + 2] == 13 && b[i + 3] == 10)
				return i + 4;
		}
		return -1;
	}

	static SfQuery parsequery(string q) {
		var query = new SfQuery();
		if (string.IsNullOrEmpty(q)) return query;
		if (q[0] == '?') q = q.Substring(1);
		foreach (var part in q.Split('&')) {
			if (part.Length == 0) continue;
			var eq = part.IndexOf('=');
			string k, v;
			if (eq < 0) { k = part; v = ""; }
			else { k = part.Substring(0, eq); v = part.Substring(eq + 1); }
			query.Set(unesc(k), unesc(v));
		}
		return query;
	}

	static string unesc(string s) {
		if (string.IsNullOrEmpty(s)) return "";
		try { return Uri.UnescapeDataString(s.Replace('+', ' ')); }
		catch { return s; }
	}

	static Encoding encodingof(string ct) {
		if (string.IsNullOrEmpty(ct)) return Encoding.UTF8;
		var i = ct.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
		if (i < 0) return Encoding.UTF8;
		var cs = ct.Substring(i + 8).Trim();
		var semi = cs.IndexOf(';');
		if (semi >= 0) cs = cs.Substring(0, semi).Trim();
		cs = cs.Trim('"', '\'');
		try { return Encoding.GetEncoding(cs); }
		catch { return Encoding.UTF8; }
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

	void handle(SfCtx ctx) {
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
			if (path is "/apk") {
				if (!isget(method) && !ishead(method)) {
					writejson(ctx, 405, err(805, "apk 仅支持 GET/HEAD"));
					return;
				}
				handleapk(ctx, ishead(method));
				return;
			}
			if (tryweb(ctx, method, path, pathRaw))
				return;
			var dev = authed(req);
			if (dev == null) {
				writejson(ctx, 401, err(401, "未配对或 token 无效"));
				return;
			}
			touch(dev.Id);
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
			if (path is "/api/sendfile/pull") {
				if (!isget(method)) { writejson(ctx, 405, err(805, "pull 仅支持 GET")); return; }
				handlepull(ctx, dev);
				return;
			}
			if (path is "/api/sendfile/pulldone") {
				if (!ispost(method)) { writejson(ctx, 405, err(805, "pulldone 仅支持 POST")); return; }
				handlepulldone(ctx, dev);
				return;
			}
			writejson(ctx, 404, err(404, "未知路径"));
		}
		catch (Exception ex) {
			try { writejson(ctx, 500, err(900, ex.Message)); } catch { }
		}
	}

	void handlepair(SfCtx ctx) {
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
			touch(existing.Id);
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
		touch(dev.Id);
		writejson(ctx, 200, ok(new JsonObject {
			["token"] = dev.Token,
			["name"] = getOpts()?.SendFileName ?? Environment.MachineName,
			["pcId"] = getOpts()?.SendFilePcId ?? "",
		}));
	}

	void handlepull(SfCtx ctx, SendFileDevice dev) {
		var list = Outbox.List(dev.Id);
		var arr = new JsonArray();
		foreach (var it in list) {
			arr.Add(new JsonObject {
				["id"] = it.Id,
				["path"] = it.StoreRel ?? "",
				["rel"] = it.PhoneRel ?? "",
				["name"] = it.Name ?? "",
				["size"] = it.Size,
			});
		}
		writejson(ctx, 200, ok(new JsonObject { ["items"] = arr }));
	}

	void handlepulldone(SfCtx ctx, SendFileDevice dev) {
		var body = readjson(ctx.Request);
		var id = jsonlong(body, "id");
		if (id <= 0) {
			long.TryParse(ctx.Request.QueryString["id"] ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
		}
		if (id <= 0) {
			writejson(ctx, 200, err(802, "缺少 id"));
			return;
		}
		var it = Outbox.Remove(dev.Id, id);
		if (it != null) {
			var job = Jobs.FindStore(it.StoreRel);
			if (job != null) Jobs.Finish(job.Id, true, null);
			if (isstaging(it.StoreRel)) {
				try { SendFileOps.Delete(it.StoreRel); } catch { }
			}
		}
		writejson(ctx, 200, ok(new JsonObject { ["id"] = id }));
	}

	void handleinfo(SfCtx ctx, SendFileDevice dev) {
		var o = getOpts() ?? new OcrOptions();
		SendFilePaths.EnsureRoot();
		writejson(ctx, 200, ok(new JsonObject {
			["name"] = string.IsNullOrWhiteSpace(o.SendFileName) ? Environment.MachineName : o.SendFileName,
			["pcId"] = o.SendFilePcId ?? "",
			["device"] = dev.Name ?? "",
			["root"] = "sendfile",
		}));
	}

	void handlelist(SfCtx ctx) {
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

	void handleapk(SfCtx ctx, bool head) {
		var full = ApkHost.FindFile();
		if (string.IsNullOrEmpty(full) || !File.Exists(full)) {
			writejson(ctx, 404, err(404, "未找到 APK"));
			return;
		}
		FileStream fs = null;
		try {
			var fi = new FileInfo(full);
			var res = ctx.Response;
			res.StatusCode = 200;
			res.ContentType = "application/vnd.android.package-archive";
			res.ContentLength64 = fi.Length;
			res.Headers["Access-Control-Allow-Origin"] = "*";
			var fn = Path.GetFileName(full) ?? "screenkit.apk";
			res.Headers["Content-Disposition"] = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(fn)}";
			if (head) {
				try { res.OutputStream.Close(); } catch { }
				try { res.Close(); } catch { }
				return;
			}
			fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
			var buf = new byte[64 * 1024];
			int n;
			while ((n = fs.Read(buf, 0, buf.Length)) > 0)
				res.OutputStream.Write(buf, 0, n);
		}
		finally {
			try { fs?.Dispose(); } catch { }
			try { ctx.Response.OutputStream.Close(); } catch { }
			try { ctx.Response.Close(); } catch { }
		}
	}

	void handledownload(SfCtx ctx) {
		sendfile(ctx, ctx.Request.QueryString["path"] ?? "", ishead(ctx.Request.HttpMethod),
			asAttachment: true, trackJob: true);
	}

	/// <summary>把 sendfile/ 内文件写入响应。路径非法或不是文件时返回 JSON 错误。</summary>
	void sendfile(SfCtx ctx, string rel, bool head, bool asAttachment, bool trackJob) {
		if (!SendFilePaths.TryResolve(rel, out var full, out var pathErr)) {
			writejson(ctx, 200, err(410, pathErr ?? "路径非法"));
			return;
		}
		if (!File.Exists(full)) {
			writejson(ctx, 200, err(410, "文件不存在"));
			return;
		}
		var job = trackJob ? Jobs.FindStore((rel ?? "").Replace('\\', '/').Trim('/')) : null;
		FileStream fs = null;
		try {
			fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
			var res = ctx.Response;
			res.StatusCode = 200;
			res.ContentType = mimeof(full);
			res.ContentLength64 = fs.Length;
			res.Headers["Access-Control-Allow-Origin"] = "*";
			var fn = Path.GetFileName(full) ?? "file";
			var disp = asAttachment ? "attachment" : "inline";
			res.Headers["Content-Disposition"] = $"{disp}; filename*=UTF-8''{Uri.EscapeDataString(fn)}";
			if (head) {
				try { res.OutputStream.Close(); } catch { }
				try { res.Close(); } catch { }
				return;
			}
			if (job != null) Jobs.SetRun(job.Id);
			var buf = new byte[64 * 1024];
			int n;
			while ((n = fs.Read(buf, 0, buf.Length)) > 0) {
				res.OutputStream.Write(buf, 0, n);
				if (job != null) Jobs.AddDone(job.Id, n);
			}
		}
		catch (Exception ex) {
			if (job != null) Jobs.Finish(job.Id, false, ex.Message);
			throw;
		}
		finally {
			try { fs?.Dispose(); } catch { }
			try { ctx.Response.OutputStream.Close(); } catch { }
			try { ctx.Response.Close(); } catch { }
		}
	}

	static string mimeof(string full) {
		var ext = (Path.GetExtension(full) ?? "").ToLowerInvariant();
		return ext switch {
			".png" => "image/png",
			".jpg" or ".jpeg" => "image/jpeg",
			".gif" => "image/gif",
			".webp" => "image/webp",
			".bmp" => "image/bmp",
			".svg" => "image/svg+xml",
			".txt" or ".log" or ".md" or ".csv" => "text/plain; charset=utf-8",
			".json" => "application/json; charset=utf-8",
			".pdf" => "application/pdf",
			".mp3" => "audio/mpeg",
			".wav" => "audio/wav",
			".mp4" => "video/mp4",
			".zip" => "application/zip",
			_ => "application/octet-stream",
		};
	}

	void handleupload(SfCtx ctx) {
		var rel = ctx.Request.QueryString["path"] ?? "";
		var name = Path.GetFileName((rel ?? "").Replace('\\', '/'));
		if (string.IsNullOrEmpty(name)) name = "file";
		var job = Jobs.Add(name, toPhone: false, ctx.Request.ContentLength, "");
		Jobs.SetRun(job.Id);
		try {
			SendFileOps.SaveStream(rel, ctx.Request.InputStream, n => Jobs.AddDone(job.Id, n));
			Jobs.Finish(job.Id, true, null);
			writejson(ctx, 200, ok(new JsonObject { ["path"] = rel ?? "" }));
		}
		catch (InvalidOperationException ex) {
			Jobs.Finish(job.Id, false, ex.Message);
			writejson(ctx, 200, err(410, ex.Message));
		}
		catch (Exception ex) {
			Jobs.Finish(job.Id, false, ex.Message);
			throw;
		}
	}

	void handlemkdir(SfCtx ctx) {
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

	void handledelete(SfCtx ctx) {
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

	void handletextget(SfCtx ctx, SendFileDevice dev) {
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

	void handletextpost(SfCtx ctx) {
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

	SendFileDevice authed(SfReq req) {
		var id = req.Headers["X-Device-Id"] ?? req.QueryString["device"] ?? "";
		var token = bearertoken(req);
		if (string.IsNullOrEmpty(token))
			token = req.Headers["X-Token"] ?? "";
		return Auth.Find(id, token);
	}

	static string bearertoken(SfReq req) {
		var h = req.Headers["Authorization"] ?? "";
		if (h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
			return h.Substring(7).Trim();
		return "";
	}

	static JsonObject readjson(SfReq req) {
		try {
			using var sr = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
			var s = sr.ReadToEnd();
			if (string.IsNullOrWhiteSpace(s)) return new JsonObject();
			return JsonNode.Parse(s) as JsonObject ?? new JsonObject();
		}
		catch { return new JsonObject(); }
	}

	static long jsonlong(JsonObject o, string key) {
		if (o == null) return 0;
		try {
			if (!o.TryGetPropertyValue(key, out var n) || n == null) return 0;
			if (n is JsonValue jv) {
				try { return jv.GetValue<long>(); } catch { }
				try { return jv.GetValue<int>(); } catch { }
				if (long.TryParse(jv.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var x))
					return x;
			}
		}
		catch { }
		return 0;
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
	static bool ishead(string m) => string.Equals(m, "HEAD", StringComparison.OrdinalIgnoreCase);

	static JsonObject ok(JsonNode data) => new() {
		["code"] = 100,
		["data"] = data,
	};

	static JsonObject err(int code, string msg) => new() {
		["code"] = code,
		["data"] = msg ?? "",
	};

	static void writecors(SfCtx ctx, int status) {
		var res = ctx.Response;
		res.StatusCode = status;
		res.Headers["Access-Control-Allow-Origin"] = "*";
		res.Headers["Access-Control-Allow-Methods"] = "GET, POST, DELETE, OPTIONS";
		res.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization, X-Device-Id, X-Token, X-Web-Token";
		res.ContentLength64 = 0;
		try { res.Close(); } catch { }
	}

	static void writejson(SfCtx ctx, int httpStatus, JsonNode body) {
		var bytes = Encoding.UTF8.GetBytes(body.ToJsonString(JsonUtf8));
		var res = ctx.Response;
		res.StatusCode = httpStatus;
		res.ContentType = "application/json; charset=utf-8";
		res.ContentEncoding = Encoding.UTF8;
		res.ContentLength64 = bytes.Length;
		res.Headers["Access-Control-Allow-Origin"] = "*";
		res.Headers["Access-Control-Allow-Methods"] = "GET, POST, DELETE, OPTIONS";
		res.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization, X-Device-Id, X-Token, X-Web-Token";
		try {
			res.OutputStream.Write(bytes, 0, bytes.Length);
		}
		finally {
			try { res.OutputStream.Close(); } catch { }
			try { res.Close(); } catch { }
		}
	}

	static void tryfirewall(int tcpPort, int udpPort) {
		runnetsh($"advfirewall firewall delete rule name=\"ScreenKit SendFile TCP\"");
		runnetsh($"advfirewall firewall delete rule name=\"ScreenKit SendFile UDP\"");
		runnetsh($"advfirewall firewall add rule name=\"ScreenKit SendFile TCP\" dir=in action=allow protocol=TCP localport={tcpPort}");
		runnetsh($"advfirewall firewall add rule name=\"ScreenKit SendFile UDP\" dir=in action=allow protocol=UDP localport={udpPort}");
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

sealed class SfCtx {
	public SfReq Request;
	public SfRes Response;
}

sealed class SfReq {
	public string HttpMethod;
	public Uri Url;
	public SfQuery QueryString;
	public SfHeaders Headers;
	public Stream InputStream;
	public Encoding ContentEncoding;
	public IPEndPoint RemoteEndPoint;
	public long ContentLength;
}

sealed class SfQuery {
	readonly Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
	public string this[string key] =>
		key != null && map.TryGetValue(key, out var v) ? v : null;
	public void Set(string k, string v) {
		if (string.IsNullOrEmpty(k)) return;
		map[k] = v ?? "";
	}
}

sealed class SfHeaders {
	readonly List<KeyValuePair<string, string>> items = new();
	public string this[string key] {
		get {
			foreach (var kv in items) {
				if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
					return kv.Value;
			}
			return null;
		}
		set {
			for (var i = 0; i < items.Count; i++) {
				if (string.Equals(items[i].Key, key, StringComparison.OrdinalIgnoreCase)) {
					items[i] = new KeyValuePair<string, string>(items[i].Key, value ?? "");
					return;
				}
			}
			items.Add(new KeyValuePair<string, string>(key, value ?? ""));
		}
	}
	public IEnumerable<KeyValuePair<string, string>> All => items;
}

sealed class SfRes {
	readonly SfOut output;
	public int StatusCode = 200;
	public string ContentType;
	public Encoding ContentEncoding;
	public long ContentLength64 = -1;
	public readonly SfHeaders Headers = new();
	public Stream OutputStream => output;

	public SfRes(Stream ns) {
		output = new SfOut(ns, this);
	}

	public void Close() {
		output.Finish();
	}
}

sealed class SfOut : Stream {
	readonly Stream inner;
	readonly SfRes res;
	bool sent;

	public SfOut(Stream inner, SfRes res) {
		this.inner = inner;
		this.res = res;
	}

	public override bool CanRead => false;
	public override bool CanSeek => false;
	public override bool CanWrite => true;
	public override long Length => throw new NotSupportedException();
	public override long Position {
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	public override void Flush() {
		try { inner.Flush(); } catch { }
	}

	public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();

	public override void Write(byte[] buffer, int offset, int count) {
		sendhdr();
		if (count > 0) inner.Write(buffer, offset, count);
	}

	public void Finish() {
		sendhdr();
		try { inner.Flush(); } catch { }
	}

	void sendhdr() {
		if (sent) return;
		sent = true;
		var sb = new StringBuilder();
		sb.Append("HTTP/1.1 ").Append(res.StatusCode).Append(' ').Append(reason(res.StatusCode)).Append("\r\n");
		if (!string.IsNullOrEmpty(res.ContentType))
			sb.Append("Content-Type: ").Append(res.ContentType).Append("\r\n");
		if (res.ContentLength64 >= 0)
			sb.Append("Content-Length: ").Append(res.ContentLength64.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
		var sawConn = false;
		foreach (var kv in res.Headers.All) {
			if (kv.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase)) sawConn = true;
			sb.Append(kv.Key).Append(": ").Append(kv.Value).Append("\r\n");
		}
		if (!sawConn) sb.Append("Connection: close\r\n");
		sb.Append("\r\n");
		var hb = Encoding.ASCII.GetBytes(sb.ToString());
		inner.Write(hb, 0, hb.Length);
	}

	static string reason(int code) => code switch {
		200 => "OK",
		204 => "No Content",
		302 => "Found",
		401 => "Unauthorized",
		403 => "Forbidden",
		404 => "Not Found",
		405 => "Method Not Allowed",
		500 => "Internal Server Error",
		_ => "OK",
	};
}

sealed class SfIn : Stream {
	readonly Stream inner;
	readonly byte[] head;
	readonly long limit;
	int pos;
	long taken;

	public SfIn(Stream inner, byte[] leftover, long limit) {
		this.inner = inner;
		head = leftover ?? Array.Empty<byte>();
		this.limit = limit < 0 ? 0 : limit;
	}

	public override bool CanRead => true;
	public override bool CanSeek => false;
	public override bool CanWrite => false;
	public override long Length => limit;
	public override long Position {
		get => taken;
		set => throw new NotSupportedException();
	}

	public override void Flush() { }
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	public override int Read(byte[] buffer, int offset, int count) {
		if (buffer == null) throw new ArgumentNullException(nameof(buffer));
		var left = limit - taken;
		if (left <= 0 || count <= 0) return 0;
		if (count > left) count = (int)left;
		var n = 0;
		if (pos < head.Length) {
			var c = Math.Min(count, head.Length - pos);
			Buffer.BlockCopy(head, pos, buffer, offset, c);
			pos += c;
			n += c;
			offset += c;
			count -= c;
		}
		if (count > 0)
			n += inner.Read(buffer, offset, count);
		taken += n;
		return n;
	}
}
