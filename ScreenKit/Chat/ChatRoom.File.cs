using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace ScreenKit;

static partial class ChatRoom {
	const int MAXFILE = 8 * 1024 * 1024;
	const int MAXRAW = MAXFILE + 256 * 1024;
	const int MAXUP = 48;

	static readonly Dictionary<string, Up> ups = new();
	static readonly List<string> uporder = new();

	/// <summary>删掉程序目录 tmp/ 下聊天上传的 chat- 文件。不碰子目录。</summary>
	public static void PurgeUploads() {
		lock (gate) {
			ups.Clear();
			uporder.Clear();
		}
		var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp");
		if (!Directory.Exists(dir)) return;
		string[] files;
		try { files = Directory.GetFiles(dir, "chat-*"); }
		catch { return; }
		foreach (var f in files) {
			var name = Path.GetFileName(f);
			if (name.Length != 37 || !name.StartsWith("chat-", StringComparison.Ordinal)) continue;
			if (!okid(name.Substring(5))) continue;
			try { File.Delete(f); } catch { }
		}
	}

	public static bool TryFile(SockCtx ctx) {
		var req = ctx.Request;
		var path = (req.Url?.AbsolutePath ?? "/").TrimEnd('/');
		if (path.Length == 0) path = "/";
		path = path.ToLowerInvariant();
		if (path == "/chat/file") {
			if (!string.Equals(req.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase)) {
				writehttp(ctx, 405, httperr("method"));
				return true;
			}
			saveupload(ctx);
			return true;
		}
		const string pre = "/chat/file/";
		if (!path.StartsWith(pre, StringComparison.Ordinal)) return false;
		if (!string.Equals(req.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase)) {
			writehttp(ctx, 405, httperr("method"));
			return true;
		}
		sendup(ctx, path.Substring(pre.Length));
		return true;
	}

	static void onfile(Conn c, JsonObject jo) {
		if (!c.joined) { enqueue(c, errjson("join")); return; }
		var fid = jo["file"]?.ToString() ?? "";
		if (!lookup(fid, out var up)) { enqueue(c, errjson("file")); return; }
		if (c.lastsay != 0 && Environment.TickCount - c.lastsay < SAYMS) {
			enqueue(c, errjson("fast"));
			return;
		}
		c.lastsay = Environment.TickCount;
		var line = filejson(c, up);
		hist.Add(line);
		if (hist.Count > HISTN) hist.RemoveAt(0);
		broadcast(line, null);
	}

	static void saveupload(SockCtx ctx) {
		var req = ctx.Request;
		if (req.ContentLength64 > MAXRAW) {
			writehttp(ctx, 200, httperr("big"));
			return;
		}
		var ctype = req.ContentType ?? "";
		if (ctype.IndexOf("multipart/", StringComparison.OrdinalIgnoreCase) < 0) {
			writehttp(ctx, 200, httperr("bad"));
			return;
		}
		var boundary = boundaryof(ctype);
		if (string.IsNullOrEmpty(boundary)) {
			writehttp(ctx, 200, httperr("bad"));
			return;
		}
		var raw = readlimited(req.InputStream, req.ContentLength64, out var rerr);
		if (rerr != null) {
			writehttp(ctx, 200, httperr(rerr));
			return;
		}
		if (!splitfile(raw, boundary, out var data, out var name, out var kind)) {
			writehttp(ctx, 200, httperr("bad"));
			return;
		}
		if (data == null || data.Length == 0) {
			writehttp(ctx, 200, httperr("empty"));
			return;
		}
		if (data.Length > MAXFILE) {
			writehttp(ctx, 200, httperr("big"));
			return;
		}
		var mime = imagemime(data);
		if (kind != "file") kind = "image";
		if (kind == "image") {
			if (mime == null) {
				writehttp(ctx, 200, httperr("img"));
				return;
			}
		}
		else mime = "application/octet-stream";
		var up = new Up {
			id = Guid.NewGuid().ToString("N"),
			name = cleanname(name),
			kind = kind,
			mime = mime,
			size = data.Length,
		};
		try {
			Directory.CreateDirectory(tmpdir());
			File.WriteAllBytes(pathof(up.id), data);
		}
		catch {
			writehttp(ctx, 200, httperr("bad"));
			return;
		}
		lock (gate) {
			ups[up.id] = up;
			uporder.Add(up.id);
			while (uporder.Count > MAXUP) {
				var old = uporder[0];
				uporder.RemoveAt(0);
				ups.Remove(old);
				try { File.Delete(pathof(old)); } catch { }
			}
		}
		writehttp(ctx, 200, new JsonObject {
			["code"] = 100,
			["id"] = up.id,
			["name"] = up.name,
			["kind"] = up.kind,
			["size"] = up.size,
			["mime"] = up.mime,
		}.ToJsonString(jsonopt));
	}

	static void sendup(SockCtx ctx, string id) {
		if (!lookup(id, out var up)) {
			writemiss(ctx);
			return;
		}
		byte[] data;
		try { data = File.ReadAllBytes(pathof(up.id)); }
		catch {
			writemiss(ctx);
			return;
		}
		var res = ctx.Response;
		res.StatusCode = 200;
		res.ContentType = up.mime;
		res.ContentLength64 = data.Length;
		res.Headers["X-Content-Type-Options"] = "nosniff";
		res.Headers["Cache-Control"] = "no-store";
		var disp = up.kind == "image" ? "inline" : "attachment";
		res.Headers["Content-Disposition"] = disp + "; filename*=UTF-8''" + Uri.EscapeDataString(up.name);
		try {
			res.OutputStream.Write(data, 0, data.Length);
		}
		finally {
			try { res.OutputStream.Close(); } catch { }
		}
	}

	static bool lookup(string id, out Up up) {
		up = null;
		if (!okid(id)) return false;
		lock (gate) return ups.TryGetValue(id, out up);
	}

	static string filejson(Conn c, Up up) => new JsonObject {
		["op"] = "file",
		["id"] = c.id,
		["nick"] = c.nick,
		["gender"] = c.gender,
		["file"] = up.id,
		["name"] = up.name,
		["kind"] = up.kind,
		["size"] = up.size,
		["mime"] = up.mime,
		["t"] = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
	}.ToJsonString(jsonopt);

	static string httperr(string code) =>
		new JsonObject { ["code"] = 400, ["err"] = code }.ToJsonString(jsonopt);

	static void writehttp(SockCtx ctx, int status, string json) {
		var bytes = Encoding.UTF8.GetBytes(json);
		var res = ctx.Response;
		res.StatusCode = status;
		res.ContentType = "application/json; charset=utf-8";
		res.ContentLength64 = bytes.Length;
		res.Headers["Access-Control-Allow-Origin"] = "*";
		try { res.OutputStream.Write(bytes, 0, bytes.Length); }
		finally { try { res.OutputStream.Close(); } catch { } }
	}

	static void writemiss(SockCtx ctx) {
		var res = ctx.Response;
		res.StatusCode = 404;
		res.ContentLength64 = 0;
		try { res.OutputStream.Close(); } catch { }
	}

	static byte[] readlimited(Stream src, long clen, out string err) {
		err = null;
		if (clen == 0) return Array.Empty<byte>();
		var cap = clen > 0 ? (int)Math.Min(clen, MAXRAW) : MAXRAW;
		var ms = new MemoryStream();
		var buf = new byte[8192];
		while (ms.Length < cap) {
			int n;
			try { n = src.Read(buf, 0, (int)Math.Min(buf.Length, cap - ms.Length)); }
			catch { err = "bad"; return null; }
			if (n <= 0) break;
			ms.Write(buf, 0, n);
		}
		if (clen > MAXRAW || (clen < 0 && ms.Length >= MAXRAW)) {
			err = "big";
			return null;
		}
		return ms.ToArray();
	}

	static string boundaryof(string ctype) {
		var i = ctype.IndexOf("boundary=", StringComparison.OrdinalIgnoreCase);
		if (i < 0) return "";
		var b = ctype.Substring(i + "boundary=".Length).Trim();
		var sc = b.IndexOf(';');
		if (sc >= 0) b = b.Substring(0, sc).Trim();
		return b.Trim().Trim('"');
	}

	static bool splitfile(byte[] raw, string boundary, out byte[] data, out string name, out string kind) {
		data = null;
		name = "";
		kind = "";
		var sep = Encoding.UTF8.GetBytes("--" + boundary);
		var at = 0;
		while (at < raw.Length) {
			var i = findbytes(raw, sep, at);
			if (i < 0) break;
			var start = i + sep.Length;
			if (start + 1 < raw.Length && raw[start] == (byte)'-' && raw[start + 1] == (byte)'-') break;
			if (start < raw.Length && raw[start] == (byte)'\r') start++;
			if (start < raw.Length && raw[start] == (byte)'\n') start++;
			var next = findbytes(raw, sep, start);
			if (next < 0) break;
			var end = next;
			if (end >= 2 && raw[end - 2] == (byte)'\r' && raw[end - 1] == (byte)'\n') end -= 2;
			else if (end >= 1 && raw[end - 1] == (byte)'\n') end -= 1;
			takepart(raw, start, end, ref data, ref name, ref kind);
			at = next;
		}
		return data != null;
	}

	static void takepart(byte[] raw, int start, int end, ref byte[] data, ref string name, ref string kind) {
		if (end <= start) return;
		var split = findheaderend(raw, start, end);
		if (split < 0) return;
		var header = Encoding.UTF8.GetString(raw, start, split - start);
		var dataStart = split;
		if (dataStart + 3 < end && raw[dataStart] == (byte)'\r') dataStart += 4;
		else dataStart += 2;
		if (dataStart > end) dataStart = end;
		cdof(header, out var field, out var file);
		var len = end - dataStart;
		var part = len > 0 ? new byte[len] : Array.Empty<byte>();
		if (len > 0) Buffer.BlockCopy(raw, dataStart, part, 0, len);
		if (field == "kind") {
			kind = Encoding.UTF8.GetString(part).Trim();
			if (kind != "image" && kind != "file") kind = "";
			return;
		}
		var isFile = file.Length > 0 || field is "file" or "image";
		if (!isFile || data != null) return;
		data = part;
		name = file;
	}

	static int findheaderend(byte[] raw, int start, int end) {
		for (var i = start; i + 3 < end; i++) {
			if (raw[i] == (byte)'\r' && raw[i + 1] == (byte)'\n' && raw[i + 2] == (byte)'\r' && raw[i + 3] == (byte)'\n')
				return i;
			if (raw[i] == (byte)'\n' && raw[i + 1] == (byte)'\n') return i;
		}
		return -1;
	}

	static int findbytes(byte[] raw, byte[] pat, int from) {
		var last = raw.Length - pat.Length;
		for (var i = from; i <= last; i++) {
			var ok = true;
			for (var j = 0; j < pat.Length; j++) {
				if (raw[i + j] == pat[j]) continue;
				ok = false;
				break;
			}
			if (ok) return i;
		}
		return -1;
	}

	static void cdof(string header, out string name, out string file) {
		name = "";
		file = "";
		var lines = header.Split('\n');
		foreach (var raw in lines) {
			var line = raw.Trim();
			if (!line.StartsWith("Content-Disposition", StringComparison.OrdinalIgnoreCase)) continue;
			name = token(line, "name");
			var star = tokenstar(line);
			file = star.Length > 0 ? star : token(line, "filename");
		}
	}

	static string token(string line, string key) {
		var quoted = key + "=\"";
		var i = line.IndexOf(quoted, StringComparison.OrdinalIgnoreCase);
		if (i >= 0) {
			i += quoted.Length;
			var j = line.IndexOf('"', i);
			if (j < 0) return "";
			return line.Substring(i, j - i);
		}
		var plain = key + "=";
		i = line.IndexOf(plain, StringComparison.OrdinalIgnoreCase);
		if (i < 0) return "";
		i += plain.Length;
		var k = i;
		while (k < line.Length && line[k] != ';' && line[k] != ' ' && line[k] != '\r') k++;
		return line.Substring(i, k - i).Trim('"');
	}

	static string tokenstar(string line) {
		var key = "filename*=";
		var i = line.IndexOf(key, StringComparison.OrdinalIgnoreCase);
		if (i < 0) return "";
		i += key.Length;
		var j = i;
		while (j < line.Length && line[j] != ';') j++;
		var v = line.Substring(i, j - i).Trim().Trim('"');
		var p = v.IndexOf("''", StringComparison.Ordinal);
		if (p >= 0) v = v.Substring(p + 2);
		try { return Uri.UnescapeDataString(v); }
		catch { return v; }
	}

	static string imagemime(byte[] d) {
		if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return "image/jpeg";
		if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47
			&& d[4] == 0x0D && d[5] == 0x0A && d[6] == 0x1A && d[7] == 0x0A) return "image/png";
		if (d.Length >= 6 && d[0] == 0x47 && d[1] == 0x49 && d[2] == 0x46 && d[3] == 0x38) return "image/gif";
		if (d.Length >= 2 && d[0] == 0x42 && d[1] == 0x4D) return "image/bmp";
		if (d.Length >= 12 && d[0] == 0x52 && d[1] == 0x49 && d[2] == 0x46 && d[3] == 0x46
			&& d[8] == 0x57 && d[9] == 0x45 && d[10] == 0x42 && d[11] == 0x50) return "image/webp";
		return null;
	}

	static string cleanname(string s) {
		s = Path.GetFileName(s ?? "");
		var sb = new StringBuilder();
		foreach (var ch in s) {
			if (ch < ' ' || ch == '"' || ch == '\\' || ch == '/' || ch == ':' || ch == '*' || ch == '?' || ch == '<' || ch == '>' || ch == '|')
				continue;
			sb.Append(ch);
			if (sb.Length >= 80) break;
		}
		s = sb.ToString().Trim();
		if (s.Length == 0) s = "file";
		return s;
	}

	static bool okid(string id) {
		if (id == null || id.Length != 32) return false;
		foreach (var ch in id) {
			if (ch >= '0' && ch <= '9') continue;
			if (ch >= 'a' && ch <= 'f') continue;
			return false;
		}
		return true;
	}

	static string tmpdir() => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp");

	static string pathof(string id) {
		var path = Path.GetFullPath(Path.Combine(tmpdir(), "chat-" + id));
		var root = Path.GetFullPath(tmpdir() + Path.DirectorySeparatorChar);
		if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("bad");
		return path;
	}

	sealed class Up {
		public string id;
		public string name;
		public string kind;
		public string mime;
		public int size;
	}
}
