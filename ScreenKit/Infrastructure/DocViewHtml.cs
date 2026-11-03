using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace ScreenKit;

/// <summary>
/// 文档查看器用的 HTML 生成：docx 按 document.xml 还原段落、标题、列表、表格、图片和常用字符格式，
/// PDF 交给 <see cref="DocViewPdf"/> 逐页出图。只读原文件，图片统一落到调用方给的目录。
/// </summary>
static class DocViewHtml {
	public sealed class Result {
		public string Html = "";
		public int Pages;
		public int Images;
		/// <summary>HTML 只是外壳、还要用原始文件（PDF 按需出页），调用方别删源文件。</summary>
		public bool KeepSource;
	}

	static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
	static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
	static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
	static readonly XNamespace V = "urn:schemas-microsoft-com:vml";
	static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";

	/// <summary>按扩展名分派；不认识的类型抛 <see cref="NotSupportedException"/>，交调用方提示。</summary>
	public static Result Convert(string path, string imgdir) {
		var ext = Path.GetExtension(path ?? "").ToLowerInvariant();
		Directory.CreateDirectory(imgdir);
		if (ext == ".docx") return fromdocx(path, imgdir);
		if (ext == ".pdf") return DocViewPdf.Render(path, imgdir);
		throw new NotSupportedException("docview.errtype");
	}

	public static bool IsDoc(string path) {
		var ext = Path.GetExtension(path ?? "").ToLowerInvariant();
		return ext == ".docx" || ext == ".pdf";
	}

	/// <summary>文档内图片只认这几类；emf/wmf 等浏览器放不了，按缺图跳过。</summary>
	static bool imgok(string path) {
		var ext = Path.GetExtension(path).ToLowerInvariant();
		return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif"
			|| ext == ".bmp" || ext == ".webp";
	}

	// ==================== docx ====================
	#region
	sealed class Rel {
		public string Target = "";
		public string Type = "";
	}

	sealed class Ctx {
		public ZipArchive Zip;
		public string Dir = "";
		public Dictionary<string, Rel> Rels = new(StringComparer.OrdinalIgnoreCase);
		public Dictionary<string, string> StyleClass = new(StringComparer.OrdinalIgnoreCase);
		public Dictionary<string, bool> NumOrdered = new(StringComparer.OrdinalIgnoreCase);
		public int NextImage = 1;
	}

	static Result fromdocx(string path, string imgdir) {
		using var zip = ZipFile.OpenRead(path);
		var ent = zip.GetEntry("word/document.xml") ?? throw new InvalidOperationException("docview.baddocx");
		XDocument doc;
		using (var st = ent.Open()) doc = XDocument.Load(st);
		var body = doc.Root?.Element(W + "body") ?? throw new InvalidOperationException("docview.baddocx");
		Ctx ctx = new() { Zip = zip, Dir = imgdir };
		ctx.Rels = relsmap(zip);
		ctx.StyleClass = styleclass(zip);
		ctx.NumOrdered = numordered(zip);
		var sb = new StringBuilder();
		sb.Append("<div class=\"doc-page\">");
		blocks(sb, body, ctx);
		sb.Append("</div>");
		return new Result {
			Html = DocViewPdf.Shell(sb.ToString(), Path.GetFileName(path)),
			Images = ctx.NextImage - 1,
		};
	}

	static void blocks(StringBuilder sb, XElement parent, Ctx ctx) {
		foreach (var el in parent.Elements()) {
			if (el.Name == W + "p") para(sb, el, ctx);
			else if (el.Name == W + "tbl") table(sb, el, ctx);
			else if (el.Name == W + "sdt") {
				var c = el.Element(W + "sdtContent");
				if (c != null) blocks(sb, c, ctx);
			}
		}
	}

	static void para(StringBuilder sb, XElement p, Ctx ctx) {
		var cls = paraclass(p, ctx);
		var align = alignof(p);
		var list = listlevel(p, ctx);
		var inner = inline(p, ctx);
		if (inner.Trim().Length == 0) {
			sb.Append("<p class=\"doc-gap\"></p>");
			return;
		}
		if (list != null) {
			sb.Append("<div class=\"doc-li doc-").Append(list.Value.ordered ? "ol" : "ul")
				.Append(" lv").Append(list.Value.lv).Append("\"><span class=\"doc-bullet\"></span><div class=\"doc-lit\">");
			wrap(sb, "p", (cls + " doc-lip").Trim(), align, inner);
			sb.Append("</div></div>");
			return;
		}
		wrap(sb, "p", cls, align, inner);
	}

	static void wrap(StringBuilder sb, string tag, string cls, string align, string inner) {
		sb.Append('<').Append(tag);
		if (!string.IsNullOrEmpty(cls)) sb.Append(" class=\"").Append(cls).Append('"');
		if (!string.IsNullOrEmpty(align)) sb.Append(" style=\"text-align:").Append(align).Append('"');
		sb.Append('>').Append(inner).Append("</").Append(tag).Append('>');
	}

	/// <summary>标题按 outlineLvl 和大纲样式名判定；引用、题注等只认样式名，不猜格式。</summary>
	static string paraclass(XElement p, Ctx ctx) {
		var ppr = p.Element(W + "pPr");
		if (ppr == null) return "";
		var outline = ppr.Element(W + "outlineLvl");
		if (outline != null && int.TryParse((string)outline.Attribute(W + "val"), out var lv))
			return "doc-h" + (lv < 0 ? 1 : (lv > 5 ? 6 : lv + 1));
		var id = (string)ppr.Element(W + "pStyle")?.Attribute(W + "val");
		if (id != null && ctx.StyleClass.TryGetValue(id, out var cls)) return cls;
		return "";
	}

	static string alignof(XElement p) =>
		((string)p.Element(W + "pPr")?.Element(W + "jc")?.Attribute(W + "val") ?? "") switch {
			"center" => "center",
			"right" or "end" => "right",
			"both" or "distribute" => "justify",
			_ => "",
		};

	/// <summary>编号段落返回 (缩进级别, 是否有序)；不是编号段落返回 null。</summary>
	static (int lv, bool ordered)? listlevel(XElement p, Ctx ctx) {
		var numpr = p.Element(W + "pPr")?.Element(W + "numPr");
		if (numpr == null) return null;
		var ilvl = (int?)numpr.Element(W + "ilvl")?.Attribute(W + "val") ?? 0;
		var numid = (string)numpr.Element(W + "numId")?.Attribute(W + "val") ?? "";
		var ordered = ctx.NumOrdered.TryGetValue(numid, out var o) && o;
		if (ilvl < 0) ilvl = 0;
		if (ilvl > 5) ilvl = 5;
		return (ilvl, ordered);
	}

	static Dictionary<string, string> styleclass(ZipArchive zip) {
		Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
		var ent = zip.GetEntry("word/styles.xml");
		if (ent == null) return map;
		XDocument doc;
		using (var st = ent.Open()) doc = XDocument.Load(st);
		if (doc.Root == null) return map;
		foreach (var s in doc.Root.Elements(W + "style")) {
			var id = (string)s.Attribute(W + "styleId");
			if (string.IsNullOrEmpty(id)) continue;
			var name = ((string)s.Element(W + "name")?.Attribute(W + "val") ?? "").Trim();
			var lv = headnumber(name);
			if (lv > 0) { map[id] = "doc-h" + lv; continue; }
			if (name.Equals("Title", StringComparison.OrdinalIgnoreCase) || name == "标题")
				map[id] = "doc-title";
			else if (name.Equals("Subtitle", StringComparison.OrdinalIgnoreCase) || name == "副标题")
				map[id] = "doc-subtitle";
			else if (name.Equals("Quote", StringComparison.OrdinalIgnoreCase) || name == "引用")
				map[id] = "doc-quote";
		}
		return map;
	}

	static int headnumber(string name) {
		if (string.IsNullOrEmpty(name)) return 0;
		var m = Regex.Match(name, @"^(?:heading|标题)\s*([1-9])", RegexOptions.IgnoreCase);
		return m.Success ? Math.Min(6, int.Parse(m.Groups[1].Value)) : 0;
	}

	/// <summary>numId → 是否有序号。看它指向的 abstractNum 第一级的 numFmt。</summary>
	static Dictionary<string, bool> numordered(ZipArchive zip) {
		Dictionary<string, bool> map = new(StringComparer.OrdinalIgnoreCase);
		var ent = zip.GetEntry("word/numbering.xml");
		if (ent == null) return map;
		XDocument doc;
		using (var st = ent.Open()) doc = XDocument.Load(st);
		if (doc.Root == null) return map;
		Dictionary<string, bool> abstractOrdered = new(StringComparer.OrdinalIgnoreCase);
		foreach (var an in doc.Root.Elements(W + "abstractNum")) {
			var aid = (string)an.Attribute(W + "abstractNumId");
			if (string.IsNullOrEmpty(aid)) continue;
			var fmt = (string)an.Elements(W + "lvl").FirstOrDefault()?.Element(W + "numFmt")?.Attribute(W + "val") ?? "bullet";
			abstractOrdered[aid] = !fmt.Equals("bullet", StringComparison.OrdinalIgnoreCase)
				&& !fmt.Equals("none", StringComparison.OrdinalIgnoreCase);
		}
		foreach (var num in doc.Root.Elements(W + "num")) {
			var nid = (string)num.Attribute(W + "numId");
			var aid = (string)num.Element(W + "abstractNumId")?.Attribute(W + "val");
			if (string.IsNullOrEmpty(nid) || string.IsNullOrEmpty(aid)) continue;
			map[nid] = abstractOrdered.TryGetValue(aid, out var o) && o;
		}
		return map;
	}

	static Dictionary<string, Rel> relsmap(ZipArchive zip) {
		Dictionary<string, Rel> map = new(StringComparer.OrdinalIgnoreCase);
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
		var t = (target ?? "").Replace('\\', '/').TrimStart('/');
		List<string> parts = new() { "word" };
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

	static string inline(XElement el, Ctx ctx) {
		var sb = new StringBuilder();
		foreach (var n in el.Elements()) {
			if (n.Name == W + "r") sb.Append(run(n, ctx));
			else if (n.Name == W + "hyperlink") sb.Append(hyperlink(n, ctx));
			else if (n.Name == W + "sdt" || n.Name == W + "sdtContent"
				|| n.Name == W + "ins" || n.Name == W + "smartTag")
				sb.Append(inline(n, ctx));
		}
		return sb.ToString();
	}

	static string hyperlink(XElement n, Ctx ctx) {
		var text = inline(n, ctx);
		if (text.Trim().Length == 0) return text;
		var id = (string)n.Attribute(R + "id");
		var url = id != null && ctx.Rels.TryGetValue(id, out var u) && u.Type == "link" ? u.Target : "";
		if (url.Length == 0 || !(url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
			|| url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)))
			return "<span class=\"doc-link-text\">" + text + "</span>";
		return "<a href=\"" + esc(url) + "\">" + text + "</a>";
	}

	static string run(XElement r, Ctx ctx) {
		var sb = new StringBuilder();
		foreach (var c in r.Elements()) {
			if (c.Name == W + "t") sb.Append(esc(c.Value));
			else if (c.Name == W + "br" || c.Name == W + "cr") sb.Append("<br>");
			else if (c.Name == W + "tab") sb.Append("<span class=\"doc-tab\"></span>");
			else if (c.Name == W + "drawing" || c.Name == W + "pict" || c.Name == W + "object")
				sb.Append(image(c, ctx));
			else if (c.Name == W + "noBreakHyphen") sb.Append('-');
			else if (c.Name == W + "sym") sb.Append(symbol(c));
		}
		var text = sb.ToString();
		if (text.Length == 0) return "";
		var rpr = r.Element(W + "rPr");
		if (rpr == null) return text;
		var vert = (string)rpr.Element(W + "vertAlign")?.Attribute(W + "val") ?? "";
		if (vert == "superscript") text = "<sup>" + text + "</sup>";
		else if (vert == "subscript") text = "<sub>" + text + "</sub>";
		if (ison(rpr.Element(W + "strike")) || ison(rpr.Element(W + "dstrike")))
			text = "<span class=\"doc-strike\">" + text + "</span>";
		if (rpr.Element(W + "u") != null) text = "<u>" + text + "</u>";
		if (ison(rpr.Element(W + "i"))) text = "<em>" + text + "</em>";
		if (ison(rpr.Element(W + "b"))) text = "<strong>" + text + "</strong>";
		return text;
	}

	/// <summary>Symbol 字体的码位偏移到希腊字母区；其余符号字体拿不准，宁可不显示也不出方框。</summary>
	static string symbol(XElement c) {
		try {
			var font = (string)c.Attribute(W + "font") ?? "";
			if (!font.StartsWith("Symbol", StringComparison.OrdinalIgnoreCase)) return "";
			var code = int.Parse((string)c.Attribute(W + "char") ?? "0", System.Globalization.NumberStyles.HexNumber);
			if (code < 0x41 || code > 0x7A) return "";
			return esc(((char)(code + 0x0391 - 0x41)).ToString());
		}
		catch { return ""; }
	}

	static string image(XElement el, Ctx ctx) {
		var blip = el.Descendants(A + "blip").FirstOrDefault();
		string id = null;
		if (blip != null) id = (string)blip.Attribute(R + "embed") ?? (string)blip.Attribute(R + "link");
		if (string.IsNullOrEmpty(id)) {
			var vml = el.Descendants(V + "imagedata").FirstOrDefault();
			if (vml != null) id = (string)vml.Attribute(R + "id");
		}
		if (string.IsNullOrEmpty(id)) return "";
		if (!ctx.Rels.TryGetValue(id, out var rel) || rel.Type != "image") return "";
		var name = copyimage(rel.Target, ctx);
		if (name.Length == 0) return "";
		var (w, h) = extent(el);
		var style = w > 0 ? $" style=\"width:{w}px;height:{h}px\"" : "";
		return $"<img src=\"{DocViewPdf.FileUrl(Path.Combine(ctx.Dir, name))}\" alt=\"\"{style}>";
	}

	/// <summary>drawing 的显示尺寸：EMU 转 CSS 像素（1px = 9525 EMU），过宽的按比例收到正文宽度。</summary>
	static (double w, double h) extent(XElement el) {
		try {
			var ext = el.Descendants(WP + "extent").FirstOrDefault();
			if (ext == null) return (0, 0);
			var cx = (double?)ext.Attribute("cx") ?? 0;
			var cy = (double?)ext.Attribute("cy") ?? 0;
			if (cx <= 0 || cy <= 0) return (0, 0);
			var w = cx / 9525;
			var h = cy / 9525;
			if (w > 780) {
				h = Math.Round(h * 780 / w);
				w = 780;
			}
			return (Math.Round(w), Math.Round(h));
		}
		catch { return (0, 0); }
	}

	static string copyimage(string zipPath, Ctx ctx) {
		var ent = ctx.Zip.GetEntry(zipPath);
		if (ent == null) return "";
		var name = $"{ctx.NextImage:000}{Path.GetExtension(zipPath).ToLowerInvariant()}";
		try {
			using (var st = ent.Open())
			using (var fs = File.Create(Path.Combine(ctx.Dir, name)))
				st.CopyTo(fs);
			ctx.NextImage++;
			return name;
		}
		catch { return ""; }
	}

	static void table(StringBuilder sb, XElement tbl, Ctx ctx) {
		var rows = tbl.Elements(W + "tr").ToList();
		if (rows.Count == 0) return;
		sb.Append("<table class=\"doc-tbl\">");
		foreach (var tr in rows) {
			sb.Append("<tr>");
			foreach (var tc in tr.Elements(W + "tc")) {
				var span = (int?)tc.Element(W + "tcPr")?.Element(W + "gridSpan")?.Attribute(W + "val") ?? 1;
				var vmerge = tc.Element(W + "tcPr")?.Element(W + "vMerge");
				var cont = vmerge != null && ((string)vmerge.Attribute(W + "val") == "continue");
				sb.Append("<td");
				if (span > 1) sb.Append(" colspan=\"").Append(span).Append('"');
				if (cont) sb.Append(" class=\"doc-vm\"");
				sb.Append('>').Append(cont ? "" : cellhtml(tc, ctx)).Append("</td>");
			}
			sb.Append("</tr>");
		}
		sb.Append("</table>");
	}

	static string cellhtml(XElement tc, Ctx ctx) {
		var sb = new StringBuilder();
		foreach (var p in tc.Elements(W + "p")) {
			var inner = inline(p, ctx).Trim();
			if (inner.Length == 0) continue;
			if (sb.Length > 0) sb.Append("<br>");
			sb.Append(inner);
		}
		foreach (var t in tc.Elements(W + "tbl")) {
			if (sb.Length > 0) sb.Append("<br>");
			table(sb, t, ctx);
		}
		return sb.ToString();
	}

	static bool ison(XElement e) {
		if (e == null) return false;
		var v = (string)e.Attribute(W + "val");
		return v != "0" && !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);
	}
	#endregion

	static string esc(string s) =>
		(s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

	/// <summary>
	/// 自测：现场拼一个最小 docx（标题、中文正文、加粗、项目符号、表格、图片），
	/// 核对转出来的 HTML；给了 pdf 样例就顺带核对 PDF 出图页数。
	/// </summary>
	public static int SelfTest(Action<string> log, string pdfpath = null) {
		void say(string s) {
			try { log?.Invoke(s); } catch { }
		}
		var dir = Path.Combine(Path.GetTempPath(), "ScreenKit", "docview-test");
		try {
			if (Directory.Exists(dir)) Directory.Delete(dir, true);
			Directory.CreateDirectory(dir);
			var docx = Path.Combine(dir, "t.docx");
			makedocx(docx);
			var imgdir = Path.Combine(dir, "img");
			var res = Convert(docx, imgdir);
			if (res.Html.IndexOf("<p class=\"doc-h1\">标题</p>", StringComparison.Ordinal) < 0)
				return fail(say, "heading");
			if (res.Html.IndexOf("<strong>粗体</strong>", StringComparison.Ordinal) < 0)
				return fail(say, "bold");
			if (res.Html.IndexOf("doc-ul", StringComparison.Ordinal) < 0 || res.Html.IndexOf("doc-ol", StringComparison.Ordinal) < 0)
				return fail(say, "list");
			if (res.Html.IndexOf("<table class=\"doc-tbl\">", StringComparison.Ordinal) < 0
				|| res.Html.IndexOf("<td colspan=\"2\">合并</td>", StringComparison.Ordinal) < 0)
				return fail(say, "table");
			if (res.Html.IndexOf("file:///", StringComparison.Ordinal) < 0)
				return fail(say, "image url");
			if (!Directory.EnumerateFiles(imgdir).Any())
				return fail(say, "image file");
			if (res.Html.IndexOf("<div class=\"doc-pdfpage\"", StringComparison.Ordinal) >= 0)
				return fail(say, "docx 不该出 pdf 页");
			say($"docx: {Path.GetFileName(docx)} → {res.Html.Length} 字符，{res.Images} 张图");
			if (!string.IsNullOrEmpty(pdfpath)) {
				var pdf = Convert(pdfpath, Path.Combine(dir, "pdfimg"));
				if (pdf.Pages <= 0) return fail(say, "pdf pages");
				if (pdf.Html.IndexOf("<div class=\"doc-pdfpage\"", StringComparison.Ordinal) < 0)
					return fail(say, "pdf html");
				say($"pdf: {pdfpath} → {pdf.Pages} 页");
			}
			say("OK");
			return 0;
		}
		catch (Exception ex) {
			return fail(say, ex.Message);
		}
		finally {
			try { Directory.Delete(dir, true); } catch { }
		}
	}

	static int fail(Action<string> say, string msg) {
		say("FAIL: " + msg);
		return 1;
	}

	/// <summary>用 ZipArchive 现写一个 docx，内容足够覆盖各类元素，不依赖外部样例文件。</summary>
	static void makedocx(string path) {
		using var fs = File.Create(path);
		using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
		void put(string name, string text) {
			var e = zip.CreateEntry(name);
			using var s = e.Open();
			var bytes = Encoding.UTF8.GetBytes(text);
			s.Write(bytes, 0, bytes.Length);
		}
		void putbin(string name, byte[] data) {
			var e = zip.CreateEntry(name);
			using var s = e.Open();
			s.Write(data, 0, data.Length);
		}
		put("[Content_Types].xml", """
			<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
			<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
			<Default Extension="xml" ContentType="application/xml"/>
			<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
			<Default Extension="png" ContentType="image/png"/>
			<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
			</Types>
			""");
		put("_rels/.rels", """
			<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
			<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
			<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
			</Relationships>
			""");
		put("word/_rels/document.xml.rels", """
			<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
			<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
			<Relationship Id="rIdImg" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/1.png"/>
			<Relationship Id="rIdLink" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://example.com/" TargetMode="External"/>
			</Relationships>
			""");
		put("word/styles.xml", """
			<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
			<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
			<w:style w:styleId="Heading1" w:type="paragraph"><w:name w:val="heading 1"/></w:style>
			</w:styles>
			""");
		put("word/numbering.xml", """
			<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
			<w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
			<w:abstractNum w:abstractNumId="0"><w:lvl w:ilvl="0"><w:numFmt w:val="bullet"/></w:lvl></w:abstractNum>
			<w:abstractNum w:abstractNumId="1"><w:lvl w:ilvl="0"><w:numFmt w:val="decimal"/></w:lvl></w:abstractNum>
			<w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
			<w:num w:numId="2"><w:abstractNumId w:val="1"/></w:num>
			</w:numbering>
			""");
		put("word/document.xml", """
			<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
			<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
				xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
				xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
				xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing">
			<w:body>
			<w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>标题</w:t></w:r></w:p>
			<w:p><w:r><w:t>正文</w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>粗体</w:t></w:r>
			<w:hyperlink r:id="rIdLink"><w:r><w:t>链接</w:t></w:r></w:hyperlink></w:p>
			<w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>项目一</w:t></w:r></w:p>
			<w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="2"/></w:numPr></w:pPr><w:r><w:t>第一</w:t></w:r></w:p>
			<w:tbl>
			<w:tr><w:tc><w:tcPr><w:gridSpan w:val="2"/></w:tcPr><w:p><w:r><w:t>合并</w:t></w:r></w:p></w:tc></w:tr>
			<w:tr><w:tc><w:p><w:r><w:t>甲</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>乙</w:t></w:r></w:p></w:tc></w:tr>
			</w:tbl>
			<w:p><w:r><w:drawing><wp:inline><wp:extent cx="1905000" cy="952500"/>
			<a:graphic><a:graphicData><a:blip r:embed="rIdImg"/></a:graphicData></a:graphic>
			</wp:inline></w:drawing></w:r></w:p>
			</w:body></w:document>
			""");
		putbin("word/media/1.png", pngbytes());
	}

	/// <summary>8×8 的纯色 PNG，够图片元素取到尺寸。</summary>
	static byte[] pngbytes() {
		var bmp = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, new byte[8 * 8 * 4], 8 * 4);
		var enc = new PngBitmapEncoder();
		enc.Frames.Add(BitmapFrame.Create(bmp));
		using var ms = new MemoryStream();
		enc.Save(ms);
		return ms.ToArray();
	}
}
