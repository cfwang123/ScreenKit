using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace ScreenKit;

/// <summary>把 .docx 和文本型 PDF 转成 Markdown，并抽出图片另存。</summary>
static class DocToMd {
	public sealed class Image {
		public string Name = "";
		public byte[] Data = Array.Empty<byte>();
	}

	public sealed class Result {
		public string Markdown = "";
		public int Pages;
		public string Kind = "";
		public string ImageDir = "images";
		public List<Image> Images = new();
	}

	sealed class Ctx {
		public ZipArchive Zip;
		public readonly Dictionary<string, string> RelImage = new(StringComparer.OrdinalIgnoreCase);
		public readonly Dictionary<string, string> RelLink = new(StringComparer.OrdinalIgnoreCase);
		public readonly Dictionary<string, Image> ByPath = new(StringComparer.OrdinalIgnoreCase);
		public readonly List<Image> Images = new();
		public string Dir = "images";
		public int Next = 1;

		public string Add(string key, byte[] data, string ext) {
			if (ByPath.TryGetValue(key, out var got)) return got.Name;
			var name = Next.ToString("000") + ext.ToLowerInvariant();
			Next++;
			var img = new Image { Name = name, Data = data };
			Images.Add(img);
			ByPath[key] = img;
			return name;
		}
	}

	static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
	static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
	static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
	static readonly XNamespace V = "urn:schemas-microsoft-com:vml";
	static readonly object pdfgate = new object();
	static bool pdfready;

	public static Result Convert(string path, string imagedir) {
		var ext = Path.GetExtension(path).ToLowerInvariant();
		var dir = ResolveDir(imagedir, path);
		if (ext == ".docx") {
			var md = fromdocx(path, dir, out var images);
			return new Result { Markdown = md, Kind = "docx", ImageDir = dir, Images = images };
		}
		if (ext == ".pdf") {
			var ctx = new Ctx { Dir = dir };
			var md = frompdf(path, ctx, out var pages);
			return new Result { Markdown = md, Kind = "pdf", Pages = pages, ImageDir = dir, Images = ctx.Images };
		}
		throw new NotSupportedException("doc2md.errtype");
	}

	/// <summary>把图片目录模板变成相对路径；{name} 换成文档名。</summary>
	public static string ResolveDir(string template, string srcpath) {
		var d = (template ?? "").Trim().Replace('\\', '/');
		var name = Path.GetFileNameWithoutExtension(srcpath ?? "");
		name = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
		d = d.Replace("{name}", name);
		while (d.StartsWith("/", StringComparison.Ordinal)) d = d.Substring(1);
		while (d.EndsWith("/", StringComparison.Ordinal)) d = d.Substring(0, d.Length - 1);
		return d.Length == 0 ? "images" : d;
	}

	public static bool ValidDir(string template, string srcpath, out string dir) {
		dir = ResolveDir(template, srcpath);
		if (Path.IsPathRooted(dir) || dir.Contains(":") || dir == ".."
			|| dir.StartsWith("../", StringComparison.Ordinal) || dir.Contains("/../"))
			return false;
		return true;
	}

	// ───────── docx ─────────

	static string fromdocx(string path, string imagedir, out List<Image> images) {
		using var zip = ZipFile.OpenRead(path);
		var ent = zip.GetEntry("word/document.xml");
		if (ent == null) throw new InvalidOperationException("doc2md.baddocx");
		XDocument doc;
		using (var st = ent.Open()) doc = XDocument.Load(st);
		var body = doc.Root?.Element(W + "body");
		images = new List<Image>();
		if (body == null) return "";
		var ctx = new Ctx { Zip = zip, Dir = imagedir };
		var styles = stylesmap(zip);
		var rels = relsmap(zip);
		foreach (var r in rels) {
			if (r.Value.Type == "image" && r.Value.Target.Length > 0)
				ctx.RelImage[r.Key] = r.Value.Target;
			else if (r.Value.Type == "link" && r.Value.Target.Length > 0)
				ctx.RelLink[r.Key] = r.Value.Target;
		}
		var sb = new StringBuilder();
		walkbody(sb, body, styles, ctx);
		images = ctx.Images;
		return tidy(sb.ToString());
	}

	static void walkbody(StringBuilder sb, XElement parent, Dictionary<string, int> styles, Ctx ctx) {
		foreach (var el in parent.Elements()) {
			if (el.Name == W + "p") para(sb, el, styles, ctx);
			else if (el.Name == W + "tbl") table(sb, el, ctx);
			else if (el.Name == W + "sdt") {
				var c = el.Element(W + "sdtContent");
				if (c != null) walkbody(sb, c, styles, ctx);
			}
		}
	}

	static void para(StringBuilder sb, XElement p, Dictionary<string, int> styles, Ctx ctx) {
		var text = inline(p, ctx).Trim();
		if (text.Length == 0) {
			if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.Append('\n');
			return;
		}
		var level = headlevel(p, styles);
		if (level > 0) sb.Append('#', level).Append(' ');
		else if (p.Element(W + "pPr")?.Element(W + "numPr") != null) sb.Append("- ");
		sb.Append(text).Append("\n\n");
	}

	static int headlevel(XElement p, Dictionary<string, int> styles) {
		var ppr = p.Element(W + "pPr");
		if (ppr == null) return 0;
		var outline = ppr.Element(W + "outlineLvl");
		if (outline != null && int.TryParse((string)outline.Attribute(W + "val"), out var lv))
			return Math.Min(6, lv + 1);
		var id = (string)ppr.Element(W + "pStyle")?.Attribute(W + "val");
		return id != null && styles.TryGetValue(id, out var h) ? h : 0;
	}

	static Dictionary<string, int> stylesmap(ZipArchive zip) {
		var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		var ent = zip.GetEntry("word/styles.xml");
		if (ent == null) return map;
		XDocument doc;
		using (var st = ent.Open()) doc = XDocument.Load(st);
		if (doc.Root == null) return map;
		foreach (var s in doc.Root.Elements(W + "style")) {
			var id = (string)s.Attribute(W + "styleId");
			var name = ((string)s.Element(W + "name")?.Attribute(W + "val") ?? "").Trim();
			var lv = headnumber(name);
			if (lv > 0 && !string.IsNullOrEmpty(id)) map[id] = lv;
		}
		return map;
	}

	static int headnumber(string name) {
		if (name.Equals("Title", StringComparison.OrdinalIgnoreCase) || name == "标题") return 1;
		var m = Regex.Match(name, @"(?:heading|标题)\s*([1-9])", RegexOptions.IgnoreCase);
		return m.Success ? Math.Min(6, int.Parse(m.Groups[1].Value)) : 0;
	}

	sealed class Rel {
		public string Target = "";
		public string Type = "";
	}

	static Dictionary<string, Rel> relsmap(ZipArchive zip) {
		var map = new Dictionary<string, Rel>();
		var ent = zip.GetEntry("word/_rels/document.xml.rels");
		if (ent == null) return map;
		XDocument doc;
		using (var st = ent.Open()) doc = XDocument.Load(st);
		if (doc.Root == null) return map;
		foreach (var r in doc.Root.Elements()) {
			var id = (string)r.Attribute("Id");
			var target = (string)r.Attribute("Target") ?? "";
			var type = (string)r.Attribute("Type") ?? "";
			var mode = (string)r.Attribute("TargetMode");
			if (string.IsNullOrEmpty(id) || target.Length == 0) continue;
			if (type.EndsWith("/image", StringComparison.OrdinalIgnoreCase)) {
				if (mode == "External") continue;
				var zp = resolvezip(target);
				if (zp.Length > 0 && zip.GetEntry(zp) != null && imgok(zp))
					map[id] = new Rel { Target = zp, Type = "image" };
			}
			else if (mode == "External") map[id] = new Rel { Target = target, Type = "link" };
		}
		return map;
	}

	static string resolvezip(string target) {
		var t = target.Replace('\\', '/').TrimStart('/');
		var parts = new List<string> { "word" };
		foreach (var s in t.Split('/')) {
			if (s.Length == 0 || s == ".") continue;
			if (s == "..") {
				if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
				continue;
			}
			parts.Add(s);
		}
		return string.Join("/", parts);
	}

	static bool imgok(string path) {
		var ext = Path.GetExtension(path).ToLowerInvariant();
		return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif"
			|| ext == ".bmp" || ext == ".tif" || ext == ".tiff" || ext == ".webp";
	}

	static string inline(XElement el, Ctx ctx) {
		var sb = new StringBuilder();
		foreach (var n in el.Elements()) {
			if (n.Name == W + "r") sb.Append(run(n, ctx));
			else if (n.Name == W + "hyperlink") {
				var text = inline(n, ctx);
				var id = (string)n.Attribute(R + "id");
				var url = id != null && ctx.RelLink.TryGetValue(id, out var u) ? u : "";
				if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase) && text.Trim().Length > 0)
					sb.Append('[').Append(text.Trim()).Append("](").Append(url).Append(')');
				else sb.Append(text);
			}
			else if (n.Name == W + "sdt" || n.Name == W + "sdtContent" || n.Name == W + "ins")
				sb.Append(inline(n, ctx));
		}
		return sb.ToString();
	}

	static string run(XElement r, Ctx ctx) {
		var sb = new StringBuilder();
		var hasimg = false;
		foreach (var c in r.Elements()) {
			if (c.Name == W + "t") sb.Append(c.Value);
			else if (c.Name == W + "br" || c.Name == W + "cr") sb.Append("  \n");
			else if (c.Name == W + "tab") sb.Append("  ");
			else if (c.Name == W + "drawing" || c.Name == W + "pict" || c.Name == W + "object") {
				var p = imagepath(c, ctx);
				if (p.Length > 0) {
					sb.Append("![image](").Append(p).Append(')');
					hasimg = true;
				}
			}
		}
		var text = sb.ToString();
		if (text.Length == 0 || hasimg) return text;
		var rpr = r.Element(W + "rPr");
		if (rpr == null || text.Trim().Length == 0) return text;
		if (ison(rpr.Element(W + "b"))) text = "**" + text + "**";
		if (ison(rpr.Element(W + "i"))) text = "*" + text + "*";
		return text;
	}

	static string imagepath(XElement el, Ctx ctx) {
		var blip = el.Descendants(A + "blip").FirstOrDefault();
		if (blip != null) {
			var id = (string)blip.Attribute(R + "embed") ?? (string)blip.Attribute(R + "link");
			var p = imagebyid(id, ctx);
			if (p.Length > 0) return p;
		}
		var img = el.Descendants(V + "imagedata").FirstOrDefault();
		if (img != null) {
			var p = imagebyid((string)img.Attribute(R + "id"), ctx);
			if (p.Length > 0) return p;
		}
		return "";
	}

	static string imagebyid(string id, Ctx ctx) {
		if (string.IsNullOrEmpty(id)) return "";
		if (!ctx.RelImage.TryGetValue(id, out var zp)) return "";
		if (ctx.ByPath.TryGetValue(zp, out var got)) return ctx.Dir + "/" + got.Name;
		var ent = ctx.Zip.GetEntry(zp);
		if (ent == null) return "";
		try {
			using var st = ent.Open();
			using var ms = new MemoryStream();
			st.CopyTo(ms);
			var name = ctx.Add(zp, ms.ToArray(), Path.GetExtension(zp));
			return ctx.Dir + "/" + name;
		}
		catch { return ""; }
	}

	static bool ison(XElement e) {
		if (e == null) return false;
		var v = (string)e.Attribute(W + "val");
		return v != "0" && !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);
	}

	static void table(StringBuilder sb, XElement tbl, Ctx ctx) {
		var rows = tbl.Elements(W + "tr").ToList();
		if (rows.Count == 0) return;
		var grid = new List<List<string>>();
		foreach (var tr in rows) {
			var cells = new List<string>();
			foreach (var tc in tr.Elements(W + "tc")) cells.Add(celltext(tc, ctx));
			grid.Add(cells);
		}
		var cols = grid.Max(r => r.Count);
		if (cols == 0) return;
		sb.Append('|');
		for (var c = 0; c < cols; c++) sb.Append(' ').Append(esc(cellat(grid[0], c))).Append(" |");
		sb.Append('\n').Append('|');
		for (var c = 0; c < cols; c++) sb.Append(" --- |");
		sb.Append('\n');
		for (var r = 1; r < grid.Count; r++) {
			sb.Append('|');
			for (var c = 0; c < cols; c++) sb.Append(' ').Append(esc(cellat(grid[r], c))).Append(" |");
			sb.Append('\n');
		}
		sb.Append('\n');
	}

	static string cellat(List<string> row, int i) => i < row.Count ? row[i] : "";

	static string celltext(XElement tc, Ctx ctx) {
		var parts = new List<string>();
		foreach (var p in tc.Elements(W + "p")) {
			var s = inline(p, ctx).Trim();
			if (s.Length > 0) parts.Add(s);
		}
		return string.Join(" ", parts);
	}

	static string esc(string s) =>
		(s ?? "").Replace("\n", " ").Replace("|", "\\|").Trim();

	// ───────── pdf ─────────

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern IntPtr LoadLibrary(string path);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern void FPDF_InitLibrary();

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern IntPtr FPDF_LoadMemDocument(IntPtr data, int size, [MarshalAs(UnmanagedType.LPStr)] string password);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern int FPDF_GetPageCount(IntPtr doc);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern IntPtr FPDF_LoadPage(IntPtr doc, int index);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern IntPtr FPDFText_LoadPage(IntPtr page);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern int FPDFText_CountChars(IntPtr textpage);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
	static extern int FPDFText_GetText(IntPtr textpage, int start, int count, [Out] char[] buffer);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern void FPDFText_ClosePage(IntPtr textpage);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern int FPDFPage_CountObjects(IntPtr page);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern IntPtr FPDFPage_GetObject(IntPtr page, int index);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern int FPDFPageObj_GetType(IntPtr obj);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern IntPtr FPDFImageObj_GetRenderedBitmap(IntPtr doc, IntPtr page, IntPtr image);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern int FPDFBitmap_GetWidth(IntPtr bmp);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern int FPDFBitmap_GetHeight(IntPtr bmp);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern int FPDFBitmap_GetStride(IntPtr bmp);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bmp);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern int FPDFBitmap_GetFormat(IntPtr bmp);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern void FPDFBitmap_Destroy(IntPtr bmp);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern void FPDF_ClosePage(IntPtr page);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern void FPDF_CloseDocument(IntPtr doc);

	static string frompdf(string path, Ctx ctx, out int pages) {
		pages = 0;
		var bytes = File.ReadAllBytes(path);
		if (bytes.Length < 8) throw new InvalidOperationException("doc2md.badpdf");
		var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
		try {
			lock (pdfgate) {
				loadpdfium();
				if (!pdfready) throw new DllNotFoundException("pdfium.dll");
				var doc = FPDF_LoadMemDocument(handle.AddrOfPinnedObject(), bytes.Length, null);
				if (doc == IntPtr.Zero) throw new InvalidOperationException("doc2md.badpdf");
				try {
					pages = FPDF_GetPageCount(doc);
					var sb = new StringBuilder();
					for (var i = 0; i < pages; i++) {
						var page = FPDF_LoadPage(doc, i);
						if (page == IntPtr.Zero) continue;
						try {
							var text = pagetext(page);
							var imgs = pageimages(doc, page, ctx);
							if (text.Length == 0 && imgs.Length == 0) continue;
							if (sb.Length > 0) sb.Append("\n\n---\n\n");
							sb.Append(text);
							if (imgs.Length > 0) {
								if (text.Length > 0) sb.Append("\n\n");
								sb.Append(imgs);
							}
						}
						finally { FPDF_ClosePage(page); }
					}
					return tidy(sb.ToString());
				}
				finally { FPDF_CloseDocument(doc); }
			}
		}
		finally { handle.Free(); }
	}

	static string pagetext(IntPtr page) {
		var tp = FPDFText_LoadPage(page);
		if (tp == IntPtr.Zero) return "";
		try {
			var count = FPDFText_CountChars(tp);
			if (count <= 0) return "";
			var buf = new char[count + 1];
			var got = FPDFText_GetText(tp, 0, count, buf);
			if (got <= 0) return "";
			return cleanpdf(new string(buf, 0, Math.Min(got, buf.Length)));
		}
		finally { FPDFText_ClosePage(tp); }
	}

	static string pageimages(IntPtr doc, IntPtr page, Ctx ctx) {
		var sb = new StringBuilder();
		var n = FPDFPage_CountObjects(page);
		for (var i = 0; i < n; i++) {
			var obj = FPDFPage_GetObject(page, i);
			if (obj == IntPtr.Zero || FPDFPageObj_GetType(obj) != 3) continue;
			var bmp = FPDFImageObj_GetRenderedBitmap(doc, page, obj);
			if (bmp == IntPtr.Zero) continue;
			try {
				var name = bitmapname(bmp, ctx);
				if (name.Length == 0) continue;
				if (sb.Length > 0) sb.Append("\n\n");
				sb.Append("![image](").Append(ctx.Dir).Append('/').Append(name).Append(')');
			}
			finally { FPDFBitmap_Destroy(bmp); }
		}
		return sb.ToString();
	}

	static string bitmapname(IntPtr bmp, Ctx ctx) {
		try {
			var w = FPDFBitmap_GetWidth(bmp);
			var h = FPDFBitmap_GetHeight(bmp);
			var stride = FPDFBitmap_GetStride(bmp);
			if (w <= 0 || h <= 0 || stride <= 0) return "";
			var buf = FPDFBitmap_GetBuffer(bmp);
			if (buf == IntPtr.Zero) return "";
			var bytes = new byte[stride * h];
			Marshal.Copy(buf, bytes, 0, bytes.Length);
			var fmt = FPDFBitmap_GetFormat(bmp);
			var pf = fmt == 4 ? PixelFormats.Bgra32 : fmt == 3 ? PixelFormats.Bgr32
				: fmt == 1 ? PixelFormats.Gray8 : PixelFormats.Bgr24;
			var src = BitmapSource.Create(w, h, 96, 96, pf, null, bytes, stride);
			var enc = new PngBitmapEncoder();
			enc.Frames.Add(BitmapFrame.Create(src));
			using var ms = new MemoryStream();
			enc.Save(ms);
			var key = "pdf:" + ctx.Next;
			return ctx.Add(key, ms.ToArray(), ".png");
		}
		catch { return ""; }
	}

	static void loadpdfium() {
		if (pdfready) return;
		var dir = AppDomain.CurrentDomain.BaseDirectory;
		var asmDir = "";
		try { asmDir = Path.GetDirectoryName(typeof(DocToMd).Assembly.Location) ?? ""; }
		catch { }
		var target = "";
		foreach (var d in new[] { dir, asmDir }) {
			if (string.IsNullOrEmpty(d)) continue;
			var direct = Path.Combine(d, "pdfium.dll");
			var sub = Path.Combine(d, "x64", "pdfium.dll");
			if (File.Exists(direct)) { target = direct; break; }
			if (File.Exists(sub)) { target = sub; break; }
		}
		if (target.Length == 0) return;
		try {
			if (LoadLibrary(target) == IntPtr.Zero) return;
			FPDF_InitLibrary();
			pdfready = true;
		}
		catch { }
	}

	static string cleanpdf(string s) {
		s = (s ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Replace("\0", "");
		var lines = s.Split('\n');
		for (var i = 0; i < lines.Length; i++) lines[i] = lines[i].TrimEnd();
		return string.Join("\n", lines).Trim();
	}

	static string tidy(string s) {
		s = (s ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
		while (s.Contains("\n\n\n")) s = s.Replace("\n\n\n", "\n\n");
		return s.Trim() + "\n";
	}
}
