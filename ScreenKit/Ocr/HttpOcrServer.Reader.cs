using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace ScreenKit;

/// <summary>小说阅读器页（/reader）和 web\up\reader 下的 txt、md 文件。</summary>
sealed partial class HttpOcrServer {
	const long READERMAX = 20L * 1024 * 1024;
	static readonly object readerlock = new();

	/// <summary>独立的阅读器页，和 /dict 一样只认 GET。</summary>
	void handlereaderpage(SockCtx ctx, string path) {
		string name;
		string mime;
		if (path == "/sk/reader.css") {
			name = "reader.css";
			mime = "text/css; charset=utf-8";
		}
		else if (path == "/sk/reader.js") {
			name = "reader.js";
			mime = "application/javascript; charset=utf-8";
		}
		else if (path == "/sk/dictdoc.js") {
			name = "dictdoc.js";
			mime = "application/javascript; charset=utf-8";
		}
		else {
			name = "reader.html";
			mime = "text/html; charset=utf-8";
		}
		var html = name == "reader.html";
		sendwebasset(ctx, name, mime, html);
	}

	static string readerdir() {
		var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web", "up", "reader");
		Directory.CreateDirectory(dir);
		return dir;
	}

	static bool readerext(string name) {
		var ext = Path.GetExtension(name ?? "").ToLowerInvariant();
		return ext is ".txt" or ".md" or ".markdown" or ".mdown" or ".mkd";
	}

	/// <summary>只留文件名，去掉路径和非法字符。没有扩展名时按 txt 补上。</summary>
	static string readername(string raw) {
		var name = Path.GetFileName((raw ?? "").Trim().Replace('\\', '/'));
		foreach (var c in Path.GetInvalidFileNameChars())
			name = name.Replace(c, ' ');
		name = name.Trim().Trim('.');
		if (name.Length > 180) {
			var ext = Path.GetExtension(name).ToLowerInvariant();
			name = name.Substring(0, 180 - ext.Length) + ext;
		}
		if (name.Length == 0) return "";
		if (Path.GetExtension(name).Length == 0) name += ".txt";
		return readerext(name) ? name : "";
	}

	static string readerenc(string raw) {
		var enc = (raw ?? "").Trim().ToLowerInvariant();
		if (enc == "utf8") return "utf-8";
		if (enc == "utf16" || enc == "utf-16") return "utf-16le";
		if (enc is "utf-8" or "gb18030" or "gbk" or "big5" or "utf-16le" or "utf-16be") return enc;
		return "";
	}

	void handlereader(SockCtx ctx, string path) {
		try {
			if (path is "/api/reader/list") {
				if (!isget(ctx.Request)) {
					writejson(ctx, 405, err(805, "reader/list 仅支持 GET"));
					return;
				}
				writejson(ctx, 200, new JsonObject {
					["code"] = 100,
					["data"] = new JsonObject {
						["dir"] = "web/up/reader",
						["items"] = readerlist(),
					},
				});
				return;
			}
			if (path is "/api/reader/file") {
				if (!isget(ctx.Request)) {
					writejson(ctx, 405, err(805, "reader/file 仅支持 GET"));
					return;
				}
				readerread(ctx);
				return;
			}
			if (path is "/api/reader/upload") {
				if (!ispost(ctx.Request)) {
					writejson(ctx, 405, err(805, "reader/upload 仅支持 POST"));
					return;
				}
				readerupload(ctx);
				return;
			}
			if (path is "/api/reader/rename") {
				if (!ispost(ctx.Request)) {
					writejson(ctx, 405, err(805, "reader/rename 仅支持 POST"));
					return;
				}
				readerrename(ctx);
				return;
			}
			if (path is "/api/reader/delete") {
				if (!ispost(ctx.Request)) {
					writejson(ctx, 405, err(805, "reader/delete 仅支持 POST"));
					return;
				}
				readerdelete(ctx);
				return;
			}
			if (path is "/api/reader/stop") {
				if (!ispost(ctx.Request)) {
					writejson(ctx, 405, err(805, "reader/stop 仅支持 POST"));
					return;
				}
				ChromeTts.StopSpeak();
				TtsRoute.Release();
				writejson(ctx, 200, new JsonObject { ["code"] = 100, ["data"] = new JsonObject() });
				return;
			}
			writejson(ctx, 404, err(404, "未知路径"));
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(924, ex.Message));
		}
	}

	static JsonArray readerlist() {
		var arr = new JsonArray();
		var rows = new List<FileInfo>();
		try {
			foreach (var f in Directory.GetFiles(readerdir())) {
				if (!readerext(f)) continue;
				try { rows.Add(new FileInfo(f)); }
				catch { }
			}
		}
		catch { }
		rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
		foreach (var f in rows) {
			arr.Add(new JsonObject {
				["name"] = f.Name,
				["bytes"] = f.Length,
				["mtime"] = new DateTimeOffset(f.LastWriteTimeUtc).ToUnixTimeMilliseconds(),
			});
		}
		return arr;
	}

	void readerread(SockCtx ctx) {
		var name = readername(query(ctx.Request, "name"));
		if (name.Length == 0) {
			writejson(ctx, 200, err(802, "文件名不正确"));
			return;
		}
		var full = Path.Combine(readerdir(), name);
		if (!File.Exists(full)) {
			writejson(ctx, 200, err(802, "文件不存在"));
			return;
		}
		byte[] bytes;
		try {
			var fi = new FileInfo(full);
			if (fi.Length > READERMAX) {
				writejson(ctx, 200, err(803, "文件超过 20 MB"));
				return;
			}
			bytes = File.ReadAllBytes(full);
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(924, "读不出：" + ex.Message));
			return;
		}
		string used;
		string text;
		try { text = readerdecode(bytes, readerenc(query(ctx.Request, "enc")), out used); }
		catch (Exception ex) {
			writejson(ctx, 200, err(924, "解码失败：" + ex.Message));
			return;
		}
		writejson(ctx, 200, new JsonObject {
			["code"] = 100,
			["data"] = new JsonObject {
				["name"] = name,
				["ext"] = Path.GetExtension(name).ToLowerInvariant(),
				["enc"] = used,
				["bytes"] = bytes.Length,
				["text"] = text,
			},
		});
	}

	static string readerdecode(byte[] bytes, string enc, out string used) {
		used = enc ?? "";
		if (bytes == null || bytes.Length == 0) return "";
		if (string.IsNullOrEmpty(enc)) {
			if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) {
				used = "utf-8";
				return new UTF8Encoding(false, false).GetString(bytes, 3, bytes.Length - 3);
			}
			if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) {
				used = "utf-16le";
				return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
			}
			if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) {
				used = "utf-16be";
				return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
			}
			try {
				var text = new UTF8Encoding(false, true).GetString(bytes);
				used = "utf-8";
				return text;
			}
			catch (DecoderFallbackException) {
				used = "gb18030";
				return Encoding.GetEncoding("gb18030").GetString(bytes);
			}
		}
		if (enc == "utf-8") {
			var i = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
			return new UTF8Encoding(false, false).GetString(bytes, i, bytes.Length - i);
		}
		if (enc == "utf-16le") {
			var i = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE ? 2 : 0;
			return Encoding.Unicode.GetString(bytes, i, bytes.Length - i);
		}
		if (enc == "utf-16be") {
			var i = bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF ? 2 : 0;
			return Encoding.BigEndianUnicode.GetString(bytes, i, bytes.Length - i);
		}
		return Encoding.GetEncoding(enc).GetString(bytes);
	}

	void readerupload(SockCtx ctx) {
		var name = readername(query(ctx.Request, "name"));
		if (name.Length == 0) {
			writejson(ctx, 200, err(802, "请上传 txt 或 md 文件"));
			return;
		}
		var overwrite = (query(ctx.Request, "overwrite") ?? "") == "1";
		var dir = readerdir();
		var dest = Path.Combine(dir, name);
		if (File.Exists(dest) && !overwrite) {
			writejson(ctx, 200, err(804, "已有同名文件"));
			return;
		}
		var clen = ctx.Request.ContentLength64;
		if (clen > READERMAX) {
			writejson(ctx, 200, err(803, "文件超过 20 MB"));
			return;
		}
		// 先写到自己的临时名，客户端流在锁外读，慢上传不占着 readerlock。
		var tmp = dest + ".part-" + Guid.NewGuid().ToString("N");
		long total = 0;
		try {
			using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None)) {
				var buf = new byte[64 * 1024];
				int n;
				while ((n = ctx.Request.InputStream.Read(buf, 0, buf.Length)) > 0) {
					total += n;
					if (total > READERMAX) throw new InvalidOperationException("文件超过 20 MB");
					fs.Write(buf, 0, n);
				}
			}
			if (total == 0) throw new InvalidOperationException("文件是空的");
			lock (readerlock) {
				if (File.Exists(dest)) File.Delete(dest);
				File.Move(tmp, dest);
			}
		}
		catch (Exception ex) {
			try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
			writejson(ctx, 200, err(803, ex.Message));
			return;
		}
		writejson(ctx, 200, new JsonObject {
			["code"] = 100,
			["data"] = new JsonObject {
				["name"] = name,
				["bytes"] = total,
				["overwrite"] = overwrite,
			},
		});
	}

	void readerrename(SockCtx ctx) {
		JsonObject jo;
		try { jo = readjsonbody(ctx.Request); }
		catch (Exception ex) {
			writejson(ctx, 200, err(800, ex.Message));
			return;
		}
		var from = readername(field(ctx, jo, "from"));
		var to = readername(field(ctx, jo, "to"));
		if (from.Length == 0 || to.Length == 0) {
			writejson(ctx, 200, err(802, "文件名不正确"));
			return;
		}
		var dir = readerdir();
		var src = Path.Combine(dir, from);
		var dest = Path.Combine(dir, to);
		if (!File.Exists(src)) {
			writejson(ctx, 200, err(802, "文件不存在"));
			return;
		}
		if (string.Equals(from, to, StringComparison.Ordinal)) {
			writejson(ctx, 200, new JsonObject { ["code"] = 100, ["data"] = new JsonObject { ["name"] = to } });
			return;
		}
		if (File.Exists(dest)) {
			writejson(ctx, 200, err(804, "已有同名文件"));
			return;
		}
		lock (readerlock) File.Move(src, dest);
		writejson(ctx, 200, new JsonObject { ["code"] = 100, ["data"] = new JsonObject { ["name"] = to } });
	}

	void readerdelete(SockCtx ctx) {
		JsonObject jo;
		try { jo = readjsonbody(ctx.Request); }
		catch (Exception ex) {
			writejson(ctx, 200, err(800, ex.Message));
			return;
		}
		var name = readername(field(ctx, jo, "name"));
		if (name.Length == 0) {
			writejson(ctx, 200, err(802, "文件名不正确"));
			return;
		}
		var full = Path.Combine(readerdir(), name);
		if (!File.Exists(full)) {
			writejson(ctx, 200, err(802, "文件不存在"));
			return;
		}
		lock (readerlock) File.Delete(full);
		writejson(ctx, 200, new JsonObject { ["code"] = 100, ["data"] = new JsonObject { ["name"] = name } });
	}
}
