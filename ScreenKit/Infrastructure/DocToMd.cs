using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ScreenKit;

/// <summary>把 .docx 和文本型 PDF 转成 Markdown。PDF 的文字用装好的 pdfium.dll 提取。</summary>
static class DocToMd {
	public sealed class Result {
		public string Markdown = "";
		public int Pages;
		public string Kind = "";
	}

	static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
	static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
	static readonly object pdfgate = new object();
	static bool pdfready;

	public static Result Convert(string path) {
		var ext = Path.GetExtension(path).ToLowerInvariant();
		if (ext == ".docx") {
			var md = fromdocx(path);
			return new Result { Markdown = md, Kind = "docx" };
		}
		if (ext == ".pdf") {
			var md = frompdf(path, out var pages);
			return new Result { Markdown = md, Kind = "pdf", Pages = pages };
		}
		throw new NotSupportedException("doc2md.errtype");
	}

	// ───────── docx ─────────

	static string fromdocx(string path) {
		using var zip = ZipFile.OpenRead(path);
		var ent = zip.GetEntry("word/document.xml");
		if (ent == null) throw new InvalidOperationException("doc2md.baddocx");
		XDocument doc;
		using (var st = ent.Open()) doc = XDocument.Load(st);
		var body = doc.Root?.Element(W + "body");
		if (body == null) return "";
		var styles = stylesmap(zip);
		var rels = relsmap(zip);
		var sb = new StringBuilder();
		walkbody(sb, body, styles, rels);
		return tidy(sb.ToString());
	}

	static void walkbody(StringBuilder sb, XElement parent, Dictionary<string, int> styles, Dictionary<string, string> rels) {
		foreach (var el in parent.Elements()) {
			if (el.Name == W + "p") para(sb, el, styles, rels);
			else if (el.Name == W + "tbl") table(sb, el, rels);
			else if (el.Name == W + "sdt") {
				var c = el.Element(W + "sdtContent");
				if (c != null) walkbody(sb, c, styles, rels);
			}
		}
	}

	static void para(StringBuilder sb, XElement p, Dictionary<string, int> styles, Dictionary<string, string> rels) {
		var text = inline(p, rels).Trim();
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

	static Dictionary<string, string> relsmap(ZipArchive zip) {
		var map = new Dictionary<string, string>();
		var ent = zip.GetEntry("word/_rels/document.xml.rels");
		if (ent == null) return map;
		XDocument doc;
		using (var st = ent.Open()) doc = XDocument.Load(st);
		if (doc.Root == null) return map;
		foreach (var r in doc.Root.Elements()) {
			var id = (string)r.Attribute("Id");
			var target = (string)r.Attribute("Target");
			var mode = (string)r.Attribute("TargetMode");
			if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target) && mode == "External")
				map[id] = target;
		}
		return map;
	}

	static string inline(XElement el, Dictionary<string, string> rels) {
		var sb = new StringBuilder();
		foreach (var n in el.Elements()) {
			if (n.Name == W + "r") sb.Append(run(n));
			else if (n.Name == W + "hyperlink") {
				var text = inline(n, rels);
				var id = (string)n.Attribute(R + "id");
				var url = id != null && rels.TryGetValue(id, out var u) ? u : "";
				if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase) && text.Trim().Length > 0)
					sb.Append('[').Append(text.Trim()).Append("](").Append(url).Append(')');
				else sb.Append(text);
			}
			else if (n.Name == W + "sdt" || n.Name == W + "sdtContent" || n.Name == W + "ins")
				sb.Append(inline(n, rels));
		}
		return sb.ToString();
	}

	static string run(XElement r) {
		var sb = new StringBuilder();
		foreach (var c in r.Elements()) {
			if (c.Name == W + "t") sb.Append(c.Value);
			else if (c.Name == W + "br" || c.Name == W + "cr") sb.Append("  \n");
			else if (c.Name == W + "tab") sb.Append("  ");
		}
		var text = sb.ToString();
		if (text.Length == 0) return "";
		var rpr = r.Element(W + "rPr");
		if (rpr == null || text.Trim().Length == 0) return text;
		if (ison(rpr.Element(W + "b"))) text = "**" + text + "**";
		if (ison(rpr.Element(W + "i"))) text = "*" + text + "*";
		return text;
	}

	static bool ison(XElement e) {
		if (e == null) return false;
		var v = (string)e.Attribute(W + "val");
		return v != "0" && !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);
	}

	static void table(StringBuilder sb, XElement tbl, Dictionary<string, string> rels) {
		var rows = tbl.Elements(W + "tr").ToList();
		if (rows.Count == 0) return;
		var grid = new List<List<string>>();
		foreach (var tr in rows) {
			var cells = new List<string>();
			foreach (var tc in tr.Elements(W + "tc")) cells.Add(celltext(tc, rels));
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

	static string celltext(XElement tc, Dictionary<string, string> rels) {
		var parts = new List<string>();
		foreach (var p in tc.Elements(W + "p")) {
			var s = inline(p, rels).Trim();
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
	static extern void FPDF_ClosePage(IntPtr page);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern void FPDF_CloseDocument(IntPtr doc);

	static string frompdf(string path, out int pages) {
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
							var tp = FPDFText_LoadPage(page);
							if (tp == IntPtr.Zero) continue;
							try {
								var count = FPDFText_CountChars(tp);
								if (count <= 0) continue;
								var buf = new char[count + 1];
								var got = FPDFText_GetText(tp, 0, count, buf);
								if (got <= 0) continue;
								var text = cleanpdf(new string(buf, 0, Math.Min(got, buf.Length)));
								if (text.Length == 0) continue;
								if (sb.Length > 0) sb.Append("\n\n---\n\n");
								sb.Append(text);
							}
							finally { FPDFText_ClosePage(tp); }
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
