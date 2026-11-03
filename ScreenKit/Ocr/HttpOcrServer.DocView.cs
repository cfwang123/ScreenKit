using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ScreenKit.Json;

namespace ScreenKit;

/// <summary>
/// web 文档阅读器的后端：/api/docview 收文档、转 HTML；/api/docasset 发转换出来的图片和页面。
/// 转换本身复用 WPF 阅读器那两份实现，网页和桌面看到的是同一套排版。
/// </summary>
sealed partial class HttpOcrServer {
	const long DOCVIEWMAX = 60L * 1024 * 1024;
	/// <summary>每个文档一个目录，超过一天或数量过多时清理，别让 tmp 越堆越大。</summary>
	const int DOCVIEWKEEP = 40;

	/// <summary>转换产物根目录：tmp/docview/&lt;随机&gt;/，下面是 page.html 和图片。</summary>
	static string docviewroot() {
		var dir = Path.Combine(TmpStore.Root, "docview");
		Directory.CreateDirectory(dir);
		return dir;
	}

	static bool docviewext(string path) {
		var ext = Path.GetExtension(path ?? "").ToLowerInvariant();
		return ext is ".md" or ".markdown" or ".mdown" or ".mkd" or ".docx" or ".pdf";
	}

	/// <summary>只留文件名，去掉路径和非法字符；不认识的扩展名返回空串。</summary>
	static string docviewname(string raw) {
		var name = Path.GetFileName((raw ?? "").Trim().Replace('\\', '/'));
		foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, ' ');
		name = name.Trim().Trim('.');
		if (name.Length == 0) return "";
		if (name.Length > 160) {
			var ext = Path.GetExtension(name).ToLowerInvariant();
			name = name.Substring(0, 160 - ext.Length) + ext;
		}
		return docviewext(name) ? name : "";
	}

	/// <summary>把上传的字节转成页面。Markdown 走 MdRead.ToHtml，docx/pdf 走 DocViewHtml。</summary>
	void handledocview(SockCtx ctx) {
		if (!ispost(ctx.Request)) {
			writejson(ctx, 405, err(805, "docview 仅支持 POST"));
			return;
		}
		var name = docviewname(query(ctx.Request, "name"));
		if (name.Length == 0) {
			writejson(ctx, 200, err(802, "请上传 .md、.docx 或 .pdf 文件"));
			return;
		}
		var clen = ctx.Request.ContentLength64;
		if (clen > DOCVIEWMAX) {
			writejson(ctx, 200, err(803, "文件超过 60 MB"));
			return;
		}
		var theme = (query(ctx.Request, "theme") ?? "").Trim().ToLowerInvariant();
		var scale = 1.0;
		if (double.TryParse(query(ctx.Request, "scale") ?? "", System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out var sc))
			scale = sc < 0.6 ? 0.6 : (sc > 4 ? 4 : sc);

		var id = Environment.TickCount.ToString("x") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
		var dir = Path.Combine(docviewroot(), id);
		var ext = Path.GetExtension(name).ToLowerInvariant();
		DocViewHtml.Result res;
		try {
			Directory.CreateDirectory(dir);
			var raw = Path.Combine(dir, "src" + ext);
			long total = 0;
			using (var fs = new FileStream(raw, FileMode.Create, FileAccess.Write, FileShare.None)) {
				var buf = new byte[64 * 1024];
				int n;
				while ((n = ctx.Request.InputStream.Read(buf, 0, buf.Length)) > 0) {
					total += n;
					if (total > DOCVIEWMAX) throw new InvalidOperationException("文件超过 60 MB");
					fs.Write(buf, 0, n);
				}
			}
			if (total == 0) throw new InvalidOperationException("文件是空的");
			if (ext is ".md" or ".markdown" or ".mdown" or ".mkd") {
				// 只拿到 md 的字节，同目录的图片取不到，基准目录留空，别生成打不开的 file:// 地址
				var md = MdRead.ReadText(raw);
				var html = MdRead.ToHtml(md, "", name, scale, theme);
				res = new DocViewHtml.Result { Html = rewritehtml(html, id, dir), Pages = 0 };
			}
			else {
				if (ext == ".pdf" && !NativeRuntime.HasPdfium())
					throw new MissingFeatureException("缺少 pdfium.dll，请先在帮助 → 安装功能里安装 PDFium");
				res = DocViewHtml.Convert(raw, Path.Combine(dir, "img"));
				res.Html = rewritehtml(res.Html, id, dir);
			}
			File.WriteAllText(Path.Combine(dir, "page.html"), res.Html ?? "", new UTF8Encoding(true));
			// 原文件已经拆成页面和图片，留着占地方
			try { File.Delete(raw); } catch { }
			docviewtrim();
			writejson(ctx, 200, JSONF.newObject(("code", 100), ("data", JSONF.newObject(
				("id", id), ("name", name), ("kind", ext.TrimStart('.')), ("pages", res.Pages),
				("images", res.Images), ("html", res.Html)))));
		}
		catch (Exception ex) {
			try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
			writejson(ctx, 200, err(failcode(ex), ex.Message));
		}
	}

	/// <summary>
	/// 页面里的 file:// 地址在浏览器里打不开，换成 /api/docasset?id=…&amp;f=…。
	/// 图片都在同一个转换目录（docx/pdf 在 img 子目录），只取相对路径最后两段。
	/// </summary>
	static string rewritehtml(string html, string id, string dir) {
		if (string.IsNullOrEmpty(html)) return "";
		var root = Path.GetFullPath(dir);
		return Regex.Replace(html, "file://+([^\"'\\s]*)", m => {
			var full = m.Groups[1].Value.Replace('/', '\\');
			string f = null;
			try {
				var abs = Path.GetFullPath(full);
				if (abs.StartsWith(root, StringComparison.OrdinalIgnoreCase))
					f = abs.Substring(root.Length).TrimStart('\\').Replace('\\', '/');
			}
			catch { }
			if (string.IsNullOrEmpty(f)) {
				// 不在转换目录里（如 md 引用了外部图片），交给浏览器按原地址处理
				return m.Value;
			}
			return "/api/docasset?id=" + id + "&f=" + Uri.EscapeDataString(f);
		});
	}

	/// <summary>发转换产物：page.html 或转换目录里的图片。目录名只认本进程生成的随机名。</summary>
	void handledocasset(SockCtx ctx) {
		if (!isget(ctx.Request)) {
			writejson(ctx, 405, err(805, "docasset 仅支持 GET"));
			return;
		}
		var id = (query(ctx.Request, "id") ?? "").Trim();
		var name = (query(ctx.Request, "f") ?? "").Replace('\\', '/').Trim();
		if (!docviewidok(id) || name.Length == 0 || name.Contains("..") || name.StartsWith("/")) {
			writejson(ctx, 404, err(404, "文件不存在"));
			return;
		}
		var dir = Path.GetFullPath(Path.Combine(docviewroot(), id));
		var full = Path.GetFullPath(Path.Combine(dir, name));
		if (!full.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
			|| !File.Exists(full)) {
			writejson(ctx, 404, err(404, "文件不存在"));
			return;
		}
		var mime = Path.GetExtension(full).ToLowerInvariant() switch {
			".html" => "text/html; charset=utf-8",
			".png" => "image/png",
			".jpg" or ".jpeg" => "image/jpeg",
			".gif" => "image/gif",
			".bmp" => "image/bmp",
			".webp" => "image/webp",
			_ => "application/octet-stream",
		};
		try {
			var bytes = File.ReadAllBytes(full);
			var res = ctx.Response;
			res.StatusCode = 200;
			res.ContentType = mime;
			res.ContentLength64 = bytes.Length;
			res.Headers["Access-Control-Allow-Origin"] = "*";
			res.Headers["Cache-Control"] = mime.StartsWith("text/html", StringComparison.Ordinal)
				? SendFileWebPages.NO_CACHE : SendFileWebPages.CACHE_CONTROL;
			res.OutputStream.Write(bytes, 0, bytes.Length);
			try { res.OutputStream.Close(); } catch { }
			try { res.Close(); } catch { }
		}
		catch (Exception ex) {
			writejson(ctx, 500, err(500, ex.Message));
		}
	}

	/// <summary>目录名只允许十六进制和短横线，避免拼出目录外的路径。</summary>
	static bool docviewidok(string id) {
		if (string.IsNullOrEmpty(id) || id.Length > 40) return false;
		foreach (var c in id) {
			if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && c != '_')
				return false;
		}
		return true;
	}

	/// <summary>转换目录按时间保留 40 个，多的直接删；一天前的也删。</summary>
	static void docviewtrim() {
		try {
			var dirs = Directory.GetDirectories(docviewroot());
			if (dirs.Length <= DOCVIEWKEEP) return;
			Array.Sort(dirs, (a, b) => Directory.GetLastWriteTimeUtc(a).CompareTo(Directory.GetLastWriteTimeUtc(b)));
			for (var i = 0; i < dirs.Length - DOCVIEWKEEP; i++) {
				try { Directory.Delete(dirs[i], true); } catch { }
			}
		}
		catch { }
	}
}
