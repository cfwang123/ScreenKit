using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace ScreenKit;

/// <summary>同一端口上的网页文件管理：页面、登录、公开下载。</summary>
public sealed partial class SendFileServer {
	bool tryweb(SfCtx ctx, string method, string path, string pathRaw) {
		if (path.StartsWith("/f/") || path == "/f" || path == "/d") {
			if (!isget(method) && !ishead(method)) {
				writejson(ctx, 405, err(805, "下载仅支持 GET/HEAD"));
				return true;
			}
			handlewebdl(ctx, path, pathRaw, ishead(method));
			return true;
		}
		if (path.StartsWith("/api/web/")) {
			handlewebapi(ctx, method, path);
			return true;
		}
		if (path is "/" or "/m" or "/index.html" or "/m.html"
			|| path.StartsWith("/web/")) {
			if (!isget(method) && !ishead(method)) {
				writejson(ctx, 405, err(805, "页面仅支持 GET/HEAD"));
				return true;
			}
			handlewebpage(ctx, path, ishead(method));
			return true;
		}
		return false;
	}

	void handlewebdl(SfCtx ctx, string path, string pathRaw, bool head) {
		string rel;
		if (path == "/d")
			rel = ctx.Request.QueryString["p"] ?? ctx.Request.QueryString["path"] ?? "";
		else {
			var raw = pathRaw ?? "";
			if (raw.Length >= 3 && (raw[1] == 'f' || raw[1] == 'F'))
				rel = raw.Length > 3 ? raw.Substring(3) : "";
			else
				rel = "";
			if (rel.StartsWith("/")) rel = rel.TrimStart('/');
		}
		rel = (rel ?? "").Replace('\\', '/').Trim('/');
		if (string.IsNullOrEmpty(rel)) {
			writejson(ctx, 404, err(404, "缺少文件路径"));
			return;
		}
		sendfile(ctx, rel, head, asAttachment: false, trackJob: false);
	}

	void handlewebapi(SfCtx ctx, string method, string path) {
		if (path is "/api/web/login") {
			if (!ispost(method)) { writejson(ctx, 405, err(805, "login 仅支持 POST")); return; }
			handleweblogin(ctx);
			return;
		}
		if (path is "/api/web/logout") {
			if (!ispost(method) && !isget(method)) {
				writejson(ctx, 405, err(805, "logout 仅支持 POST"));
				return;
			}
			handleweblogout(ctx);
			return;
		}
		if (path is "/api/web/me") {
			if (!isget(method)) { writejson(ctx, 405, err(805, "me 仅支持 GET")); return; }
			if (!Web.Authed(ctx.Request)) {
				writejson(ctx, 401, err(401, "未登录"));
				return;
			}
			var o = getOpts() ?? new OcrOptions();
			writejson(ctx, 200, ok(new JsonObject {
				["ok"] = true,
				["name"] = string.IsNullOrWhiteSpace(o.SendFileName) ? Environment.MachineName : o.SendFileName,
			}));
			return;
		}
		if (!Web.Authed(ctx.Request)) {
			writejson(ctx, 401, err(401, "未登录"));
			return;
		}
		if (path is "/api/web/list") {
			if (!isget(method)) { writejson(ctx, 405, err(805, "list 仅支持 GET")); return; }
			handlelist(ctx);
			return;
		}
		if (path is "/api/web/upload") {
			if (!ispost(method)) { writejson(ctx, 405, err(805, "upload 仅支持 POST")); return; }
			handleupload(ctx);
			return;
		}
		if (path is "/api/web/mkdir") {
			if (!ispost(method)) { writejson(ctx, 405, err(805, "mkdir 仅支持 POST")); return; }
			handlemkdir(ctx);
			return;
		}
		if (path is "/api/web/delete") {
			if (!string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase) && !ispost(method)) {
				writejson(ctx, 405, err(805, "delete 仅支持 DELETE/POST"));
				return;
			}
			handledelete(ctx);
			return;
		}
		if (path is "/api/web/rename") {
			if (!ispost(method)) { writejson(ctx, 405, err(805, "rename 仅支持 POST")); return; }
			handlewebrename(ctx);
			return;
		}
		if (path is "/api/web/zip") {
			if (!isget(method) && !ishead(method) && !ispost(method)) {
				writejson(ctx, 405, err(805, "zip 仅支持 GET/POST"));
				return;
			}
			handlewebzip(ctx, ishead(method));
			return;
		}
		writejson(ctx, 404, err(404, "未知路径"));
	}

	void handleweblogin(SfCtx ctx) {
		var body = readjson(ctx.Request);
		var pass = str(body, "password");
		if (string.IsNullOrEmpty(pass))
			pass = ctx.Request.QueryString["password"] ?? "";
		var keep = flag(body, "keep") || truthy(ctx.Request.QueryString["keep"]);
		var token = Web.Login(pass, keep);
		if (string.IsNullOrEmpty(token)) {
			writejson(ctx, 401, err(401, "密码错误"));
			return;
		}
		var res = ctx.Response;
		res.Headers["Set-Cookie"] = SendFileWeb.SetCookie(token, keep);
		writejson(ctx, 200, ok(new JsonObject {
			["ok"] = true,
			["token"] = token,
			["keep"] = keep,
		}));
	}

	void handlewebzip(SfCtx ctx, bool head) {
		var rels = zippaths(ctx);
		string tmp = null;
		try {
			SendFileOps.ZipToTemp(rels, out tmp, out var zipName);
			if (string.IsNullOrEmpty(tmp) || !File.Exists(tmp)) {
				writejson(ctx, 200, err(410, "打包失败"));
				return;
			}
			var fi = new FileInfo(tmp);
			var res = ctx.Response;
			res.StatusCode = 200;
			res.ContentType = "application/zip";
			res.ContentLength64 = fi.Length;
			res.Headers["Content-Disposition"] = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(zipName)}";
			if (head) {
				try { res.Close(); } catch { }
				return;
			}
			using (var fs = new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.Read)) {
				var buf = new byte[64 * 1024];
				int n;
				while ((n = fs.Read(buf, 0, buf.Length)) > 0)
					res.OutputStream.Write(buf, 0, n);
			}
			try { res.OutputStream.Close(); } catch { }
			try { res.Close(); } catch { }
		}
		catch (InvalidOperationException ex) {
			writejson(ctx, 200, err(410, ex.Message));
		}
		finally {
			try { if (tmp != null && File.Exists(tmp)) File.Delete(tmp); } catch { }
		}
	}

	static List<string> zippaths(SfCtx ctx) {
		var list = new List<string>();
		var body = ispost(ctx.Request.HttpMethod) ? readjson(ctx.Request) : null;
		if (body != null && body["paths"] is JsonArray arr) {
			foreach (var n in arr) {
				var s = n?.ToString() ?? "";
				if (s.Length > 0) list.Add(s);
			}
		}
		var one = ctx.Request.QueryString["path"] ?? "";
		if (one.Length > 0) list.Add(one);
		var many = ctx.Request.QueryString["paths"] ?? "";
		if (many.Length > 0) {
			foreach (var p in many.Split(new[] { '|', '\n' }, StringSplitOptions.RemoveEmptyEntries))
				list.Add(p.Trim());
		}
		if (list.Count == 0) list.Add("");
		return list;
	}

	void handleweblogout(SfCtx ctx) {
		Web.Logout(SendFileWeb.TokenOf(ctx.Request));
		ctx.Response.Headers["Set-Cookie"] = SendFileWeb.ClearCookie();
		writejson(ctx, 200, ok(new JsonObject { ["ok"] = true }));
	}

	void handlewebrename(SfCtx ctx) {
		var body = readjson(ctx.Request);
		var from = str(body, "from");
		var to = str(body, "to");
		if (string.IsNullOrWhiteSpace(from))
			from = ctx.Request.QueryString["from"] ?? "";
		if (string.IsNullOrWhiteSpace(to))
			to = ctx.Request.QueryString["to"] ?? "";
		try {
			SendFileOps.Rename(from, to);
			writejson(ctx, 200, ok(new JsonObject {
				["from"] = from ?? "",
				["to"] = to ?? "",
			}));
		}
		catch (InvalidOperationException ex) {
			writejson(ctx, 200, err(410, ex.Message));
		}
	}

	void handlewebpage(SfCtx ctx, string path, bool head) {
		if (path is "/" or "/index.html") {
			var ua = ctx.Request.Headers["User-Agent"] ?? "";
			var forcePc = truthy(ctx.Request.QueryString["pc"]);
			if (!forcePc && SendFileWeb.IsMobileUa(ua)) {
				writeredir(ctx, "/m");
				return;
			}
			writewebfile(ctx, "index.html", "text/html; charset=utf-8", head);
			return;
		}
		if (path is "/m" or "/m.html") {
			writewebfile(ctx, "m.html", "text/html; charset=utf-8", head);
			return;
		}
		if (path is "/web/app.js") {
			writewebfile(ctx, "app.js", "application/javascript; charset=utf-8", head);
			return;
		}
		if (path is "/web/d.css") {
			writewebfile(ctx, "d.css", "text/css; charset=utf-8", head);
			return;
		}
		if (path is "/web/m.css") {
			writewebfile(ctx, "m.css", "text/css; charset=utf-8", head);
			return;
		}
		writejson(ctx, 404, err(404, "未知路径"));
	}

	static void writewebfile(SfCtx ctx, string name, string contentType, bool head) {
		if (!SendFileWebPages.TryLoad(name, out var bytes) || bytes == null || bytes.Length == 0) {
			writejson(ctx, 404, err(404, "页面文件缺失: " + name));
			return;
		}
		var res = ctx.Response;
		res.StatusCode = 200;
		res.ContentType = contentType;
		res.ContentLength64 = bytes.Length;
		res.Headers["Cache-Control"] = "no-cache";
		res.Headers["Access-Control-Allow-Origin"] = "*";
		try {
			if (!head)
				res.OutputStream.Write(bytes, 0, bytes.Length);
		}
		finally {
			try { res.OutputStream.Close(); } catch { }
			try { res.Close(); } catch { }
		}
	}

	static void writeredir(SfCtx ctx, string loc) {
		var res = ctx.Response;
		res.StatusCode = 302;
		res.Headers["Location"] = loc ?? "/";
		res.ContentLength64 = 0;
		try { res.Close(); } catch { }
	}
}

/// <summary>网页静态文件：优先读 exe 旁 web/，否则用嵌入资源。</summary>
static class SendFileWebPages {
	public static bool TryLoad(string name, out byte[] bytes) {
		bytes = null;
		if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { '/', '\\' }) >= 0)
			return false;
		try {
			var disk = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web", name);
			if (File.Exists(disk)) {
				bytes = File.ReadAllBytes(disk);
				return true;
			}
		}
		catch { }
		try {
			var asm = typeof(SendFileWebPages).Assembly;
			using var s = asm.GetManifestResourceStream("web." + name);
			if (s == null) return false;
			using var ms = new MemoryStream();
			s.CopyTo(ms);
			bytes = ms.ToArray();
			return bytes.Length > 0;
		}
		catch { return false; }
	}
}
