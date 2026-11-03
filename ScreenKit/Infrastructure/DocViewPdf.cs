using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenKit;

/// <summary>
/// PDF 的网页化：pdfium 逐页渲染成 PNG，再拼成图片页 HTML，另外提供文档页共用的 HTML 外壳。
/// 图片写 file:// 绝对地址，IE 内核和外部浏览器都认得，不用再起临时 HTTP 服务。
/// </summary>
static class DocViewPdf {
	/// <summary>渲染 DPI。120 下 A4 约 993×1404，屏幕看字清楚又不至于太占内存。</summary>
	const double DPI = 120;
	/// <summary>单页像素上限，防止超大页面把内存吃光。</summary>
	const long MAXPIXELS = 40_000_000;

	static readonly object gate = new();
	static bool ready;

	public static DocViewHtml.Result Render(string path, string imgdir) {
		var bytes = File.ReadAllBytes(path);
		var pages = renders(bytes, imgdir);
		var sb = new StringBuilder();
		foreach (var p in pages) {
			sb.Append("<div class=\"doc-pdfpage\"><img src=\"")
				.Append(FileUrl(p)).Append("\" alt=\"\"></div>");
		}
		return new DocViewHtml.Result {
			Html = Shell(sb.ToString(), Path.GetFileName(path)),
			Pages = pages.Count,
			Images = pages.Count,
		};
	}

	/// <summary>逐页出图，返回 PNG 绝对路径。pages 只用于自测计数时可为 null。</summary>
	public static List<string> renders(byte[] bytes, string imgdir) {
		List<string> list = new();
		if (bytes == null || bytes.Length < 8) throw new InvalidOperationException("docview.badpdf");
		Directory.CreateDirectory(imgdir);
		var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
		try {
			lock (gate) {
				if (!load()) throw new DllNotFoundException("pdfium.dll");
				var doc = FPDF_LoadMemDocument(handle.AddrOfPinnedObject(), bytes.Length, null);
				if (doc == IntPtr.Zero) throw new InvalidOperationException("docview.badpdf");
				try {
					var total = FPDF_GetPageCount(doc);
					for (var i = 0; i < total; i++) {
						var png = page(doc, i, imgdir);
						if (!string.IsNullOrEmpty(png)) list.Add(png);
					}
				}
				finally { FPDF_CloseDocument(doc); }
			}
		}
		finally { handle.Free(); }
		return list;
	}

	static string page(IntPtr doc, int index, string imgdir) {
		var page = FPDF_LoadPage(doc, index);
		if (page == IntPtr.Zero) return "";
		try {
			var wpt = FPDF_GetPageWidth(page);
			var hpt = FPDF_GetPageHeight(page);
			if (wpt <= 0 || hpt <= 0) return "";
			var w = Math.Max(1, (int)Math.Round(wpt * DPI / 72));
			var h = Math.Max(1, (int)Math.Round(hpt * DPI / 72));
			if ((long)w * h > MAXPIXELS) return "";
			var bmp = FPDFBitmap_Create(w, h, 1);
			if (bmp == IntPtr.Zero) return "";
			try {
				FPDFBitmap_FillRect(bmp, 0, 0, w, h, 0xFFFFFFFF);
				// 0x01 是 LCD 文本，0x10 是注释；两者都开，屏幕上看接近阅读器。
				FPDF_RenderPageBitmap(bmp, page, 0, 0, w, h, 0, 0x01 | 0x10);
				var full = Path.Combine(imgdir, $"p{index + 1:000}.png");
				savebmp(bmp, w, h, full);
				return full;
			}
			finally { FPDFBitmap_Destroy(bmp); }
		}
		finally { FPDF_ClosePage(page); }
	}

	static void savebmp(IntPtr bmp, int w, int h, string path) {
		var stride = FPDFBitmap_GetStride(bmp);
		var buf = FPDFBitmap_GetBuffer(bmp);
		if (buf == IntPtr.Zero) return;
		var pf = FPDFBitmap_GetFormat(bmp) switch {
			4 => PixelFormats.Bgra32,
			3 => PixelFormats.Bgr32,
			1 => PixelFormats.Gray8,
			_ => PixelFormats.Bgr24,
		};
		var bytes = new byte[stride * h];
		Marshal.Copy(buf, bytes, 0, bytes.Length);
		var src = BitmapSource.Create(w, h, 96, 96, pf, null, bytes, stride);
		var enc = new PngBitmapEncoder();
		enc.Frames.Add(BitmapFrame.Create(src));
		using var fs = File.Create(path);
		enc.Save(fs);
	}

	static bool load() {
		if (ready) return true;
		List<string> dirs = new() { AppDomain.CurrentDomain.BaseDirectory };
		try {
			var asmDir = Path.GetDirectoryName(typeof(DocViewPdf).Assembly.Location);
			if (!string.IsNullOrEmpty(asmDir)) dirs.Add(asmDir);
		}
		catch { }
		string target = null;
		foreach (var d in dirs) {
			foreach (var rel in new[] { "pdfium.dll", @"x64\pdfium.dll" }) {
				var p = Path.Combine(d, rel);
				if (File.Exists(p)) { target = p; break; }
			}
			if (target != null) break;
		}
		if (target == null) return false;
		try {
			if (LoadLibrary(target) == IntPtr.Zero) return false;
			FPDF_InitLibrary();
			ready = true;
			return true;
		}
		catch { return false; }
	}

	/// <summary>本地路径转 file:// 地址，规则与 Markdown 页一致（中文、空格原样写）。</summary>
	public static string FileUrl(string path) => MdHtml.FileUrl(path);

	// ==================== 外壳 ====================
	#region
	/// <summary>阅读用 HTML 外壳：亮色纸张、限宽居中、段距按阅读习惯。</summary>
	public static string Shell(string body, string title) {
		var sb = new StringBuilder();
		sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\">")
			.Append("<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\">")
			.Append("<title>").Append(esc(title)).Append("</title>")
			.Append("<style>").Append(css()).Append("</style></head><body>")
			.Append(body).Append("</body></html>");
		return sb.ToString();
	}

	static string css() => """
		*{box-sizing:border-box}
		html,body{margin:0;padding:0;background:#f0f2f5;color:#1f2328;
			font-family:"Segoe UI","Microsoft YaHei UI","Microsoft YaHei",sans-serif}
		body{padding:18px 14px 40px;font-size:15px}
		.doc-page,.doc-pdfpage{max-width:860px;margin:0 auto 14px;background:#fff;padding:38px 46px;
			box-shadow:0 1px 3px rgba(15,23,42,.14);border-radius:3px;line-height:1.72;
			word-wrap:break-word;overflow-wrap:break-word}
		/* PDF 页之间要有明显间隔：页与页的缝隙固定 10px，别用 margin 合并掉 */
		.doc-pdfpage{padding:0;line-height:0;background:#fff;box-shadow:0 1px 3px rgba(15,23,42,.14);
			margin:0 auto 10px;border-radius:0}
		.doc-pdfpage:last-child{margin-bottom:14px}
		.doc-pdfpage img{width:100%;display:block;background:#fff}
		.doc-page p{margin:0 0 .62em}
		.doc-page .doc-h1{font-size:1.72em;font-weight:600;margin:1.1em 0 .55em;line-height:1.35}
		.doc-page .doc-h2{font-size:1.42em;font-weight:600;margin:1em 0 .5em;line-height:1.35}
		.doc-page .doc-h3{font-size:1.2em;font-weight:600;margin:.9em 0 .45em}
		.doc-page .doc-h4,.doc-page .doc-h5,.doc-page .doc-h6{font-size:1.05em;font-weight:600;margin:.8em 0 .4em}
		.doc-page .doc-title{font-size:2em;font-weight:600;text-align:center;margin:.2em 0 .8em}
		.doc-page .doc-subtitle{font-size:1.2em;color:#57606a;text-align:center;margin:0 0 1em}
		.doc-page .doc-gap{height:.5em;margin:0}
		.doc-page .doc-quote{margin:.6em 0;padding:.2em 0 .2em .9em;border-left:3px solid #d0d7de;color:#57606a}
		.doc-page .doc-li{display:flex;align-items:flex-start;margin:0 0 .3em}
		.doc-page .doc-li.lv1{margin-left:1.4em}
		.doc-page .doc-li.lv2{margin-left:2.8em}
		.doc-page .doc-li.lv3{margin-left:4.2em}
		.doc-page .doc-li.lv4{margin-left:5.6em}
		.doc-page .doc-li.lv5{margin-left:7em}
		.doc-page .doc-bullet{flex:0 0 auto;width:1em;color:#57606a}
		.doc-page .doc-ul .doc-bullet::before{content:"•"}
		.doc-page .doc-ol .doc-bullet::before{content:"▪"}
		.doc-page .doc-lit{flex:1 1 auto;min-width:0}
		.doc-page .doc-lip{margin:0}
		.doc-page .doc-tbl{border-collapse:collapse;margin:.5em 0 .9em;width:100%;font-size:.96em}
		.doc-page .doc-tbl td{border:1px solid #d8dee6;padding:5px 9px;vertical-align:top}
		.doc-page .doc-tbl td.doc-vm{border-top-color:#fff}
		.doc-page .doc-tab{display:inline-block;width:2em}
		.doc-page .doc-strike{text-decoration:line-through}
		.doc-page a,.doc-page .doc-link-text{color:#0969da;text-decoration:none}
		.doc-page a:hover{text-decoration:underline}
		.doc-page img{max-width:100%;height:auto;border-radius:2px}
		""";

	static string esc(string s) =>
		(s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
	#endregion

	// ==================== pdfium ====================
	#region
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
	static extern double FPDF_GetPageWidth(IntPtr page);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern double FPDF_GetPageHeight(IntPtr page);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startx, int starty,
		int sizeX, int sizeY, int rotate, int flags);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern int FPDFBitmap_GetStride(IntPtr bitmap);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern int FPDFBitmap_GetFormat(IntPtr bitmap);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern void FPDFBitmap_Destroy(IntPtr bitmap);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern void FPDF_ClosePage(IntPtr page);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
	static extern void FPDF_CloseDocument(IntPtr doc);
	#endregion
}
