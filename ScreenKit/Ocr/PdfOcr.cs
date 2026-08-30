using System.IO;
using OpenCvSharp;
using PDFtoImage;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using SkiaSharp;

namespace ScreenKit;

/// <summary>
/// PDF 渲染 / 识别 / 导出（可检索 PDF）。
/// 文字层：图像下实心中文字体嵌入，可选中复制不乱码。
/// </summary>
static class PdfOcr {
	public const int DefaultDpi = 150;
	const string CJK_FAMILY = "WpfOcrCjk";

	static bool fontReady;
	static string cjkFontPath;

	public static void EnsureFonts() {
		if (fontReady) return;
		cjkFontPath = findcjkfont();
		if (string.IsNullOrEmpty(cjkFontPath))
			throw new FileNotFoundException(
				"未找到可用中文字体（simhei.ttf / Deng.ttf 等）。请确认 C:\\Windows\\Fonts 存在黑体类 TTF。");
		try {
			if (GlobalFontSettings.FontResolver == null)
				GlobalFontSettings.FontResolver = new CjkFontResolver(cjkFontPath);
		}
		catch { }
		fontReady = true;
	}

	public static int GetPageCount(string pdfPath) {
		NativeRuntime.EnsureSkiaPdf();
		if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
			throw new FileNotFoundException("PDF 不存在", pdfPath);
		using var fs = File.OpenRead(pdfPath);
		return Conversion.GetPageCount(fs);
	}

	public static int GetPageCount(byte[] pdfBytes) {
		NativeRuntime.EnsureSkiaPdf();
		return Conversion.GetPageCount(pdfBytes);
	}

	/// <summary>渲染单页为 PNG 文件，返回宽高。</summary>
	public static (int w, int h) RenderPageToFile(byte[] pdfBytes, int pageIndex, int dpi, string pngPath) {
		NativeRuntime.EnsureSkiaPdf();
		dpi = Compat.Clamp(dpi <= 0 ? DefaultDpi : dpi, 72, 400);
		using var sk = Conversion.ToImage(pdfBytes, page: pageIndex, options: new PDFtoImage.RenderOptions { Dpi = dpi });
		if (sk == null || sk.Width < 1 || sk.Height < 1)
			throw new InvalidOperationException($"第 {pageIndex + 1} 页渲染失败");
		var dir = Path.GetDirectoryName(pngPath);
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		using (var img = SKImage.FromBitmap(sk))
		using (var data = img.Encode(SKEncodedImageFormat.Png, 92))
		using (var fs = File.Create(pngPath))
			data.SaveTo(fs);
		return (sk.Width, sk.Height);
	}

	/// <summary>从 PDF 建立工程：渲染全部页图到草稿目录（尚未识别）。</summary>
	public static PdfOcrProject CreateFromPdf(
		string pdfPath,
		int dpi,
		bool invisibleText,
		string draftDir,
		Action<int, int, string> progress) {
		NativeRuntime.EnsureSkiaPdf();
		if (!File.Exists(pdfPath))
			throw new FileNotFoundException("PDF 不存在", pdfPath);
		dpi = Compat.Clamp(dpi <= 0 ? DefaultDpi : dpi, 72, 400);
		var bytes = File.ReadAllBytes(pdfPath);
		var total = Conversion.GetPageCount(bytes);
		if (total <= 0) throw new InvalidOperationException("PDF 无页面");

		Directory.CreateDirectory(draftDir);
		Directory.CreateDirectory(Path.Combine(draftDir, "pages"));

		var proj = new PdfOcrProject {
			Title = Path.GetFileNameWithoutExtension(pdfPath),
			SourcePath = Path.GetFullPath(pdfPath),
			Dpi = dpi,
			InvisibleText = invisibleText,
			DraftDir = draftDir,
			Pages = new List<PdfPageEdit>(),
			Dirty = true,
		};

		for (int i = 0; i < total; i++) {
			progress?.Invoke(i + 1, total, $"渲染第 {i + 1}/{total} 页…");
			var rel = Path.Combine("pages", $"{i:D3}.png").Replace('\\', '/');
			var full = Path.Combine(draftDir, rel);
			var (w, h) = RenderPageToFile(bytes, i, dpi, full);
			proj.Pages.Add(new PdfPageEdit {
				Index = i,
				ImageFile = rel,
				Width = w,
				Height = h,
				Recognized = false,
				Lines = new List<PdfLineEdit>(),
			});
		}
		return proj;
	}

	/// <summary>识别单页图像文件，写回 page.Lines。</summary>
	public static void RecognizePage(PdfPageEdit page, string imagePath, OcrOptions opt, OcrRunner runner) {
		if (page == null) throw new ArgumentNullException(nameof(page));
		if (!File.Exists(imagePath))
			throw new FileNotFoundException("页面图像不存在", imagePath);
		using var mat = Cv2.ImRead(imagePath, ImreadModes.Color);
		if (mat == null || mat.Empty())
			throw new InvalidOperationException("无法读取页面图像");
		var ocr = runner.Run(opt, mat) ?? new OcrResult();
		page.Lines = (ocr.Lines ?? new List<OcrLine>()).Select(PdfLineEdit.FromOcr).ToList();
		page.Recognized = true;
		page.Width = mat.Width;
		page.Height = mat.Height;
	}

	/// <summary>导出可检索 PDF（使用工程内已编辑文字）。</summary>
	public static void Export(PdfOcrProject proj, string outputPath, Action<int, int, string> progress) {
		if (proj == null) throw new ArgumentNullException(nameof(proj));
		if (string.IsNullOrWhiteSpace(outputPath))
			throw new ArgumentException("输出路径无效", nameof(outputPath));
		if (string.IsNullOrWhiteSpace(proj.DraftDir))
			throw new InvalidOperationException("工程无草稿目录");

		EnsureFonts();
		var dpi = Compat.Clamp(proj.Dpi <= 0 ? DefaultDpi : proj.Dpi, 72, 400);
		var invisible = proj.InvisibleText;
		var outDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
		if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

		using var doc = new PdfDocument();
		doc.Info.Title = string.IsNullOrEmpty(proj.Title) ? "OCR" : proj.Title + " (OCR)";
		doc.Info.Creator = AppNames.Display;

		var charset = new System.Text.StringBuilder();
		var total = proj.Pages.Count;
		for (int i = 0; i < total; i++) {
			var page = proj.Pages[i];
			progress?.Invoke(i + 1, total, $"导出第 {i + 1}/{total} 页…");
			var imgPath = proj.ImagePath(page);
			if (!File.Exists(imgPath))
				throw new FileNotFoundException($"缺少第 {i + 1} 页图像", imgPath);

			var lines = (page.Lines ?? new List<PdfLineEdit>())
				.Select(l => l.ToOcrLine())
				.Where(l => !string.IsNullOrEmpty(l.Text))
				.ToList();
			foreach (var ln in lines) charset.Append(ln.Text);

			addpagefromfile(doc, imgPath, page.Width, page.Height, lines, dpi, invisible);
		}

		try {
			if (invisible && charset.Length > 0)
				doc.AddCharacters(makefont(10), charset.ToString());
		}
		catch { }

		doc.Save(outputPath);
	}

	// ── 兼容旧一键流程 ──

	public static void Process(
		string inputPath,
		string outputPath,
		OcrOptions opt,
		OcrRunner runner,
		bool invisibleText,
		int dpi,
		Action<int, int, string> progress) {
		var draft = PdfOcrProject.NewDraftDir(Path.GetFileNameWithoutExtension(inputPath));
		try {
			var proj = CreateFromPdf(inputPath, dpi, invisibleText, draft, progress);
			for (int i = 0; i < proj.Pages.Count; i++) {
				progress?.Invoke(i + 1, proj.Pages.Count, $"识别第 {i + 1}/{proj.Pages.Count} 页…");
				RecognizePage(proj.Pages[i], proj.ImagePath(proj.Pages[i]), opt, runner);
			}
			progress?.Invoke(proj.Pages.Count, proj.Pages.Count, "导出 PDF…");
			Export(proj, outputPath, progress);
			LastFullText = proj.FullText();
			LastPageCount = proj.Pages.Count;
			LastFontUsed = cjkFontPath;
		}
		finally {
			// 临时草稿可保留便于调试；不强制删除
		}
	}

	public static string LastFullText { get; private set; } = "";
	public static int LastPageCount { get; private set; }
	public static string LastFontUsed { get; private set; } = "";

	static void addpagefromfile(PdfDocument doc, string pngPath, int imgW, int imgH,
		List<OcrLine> lines, int dpi, bool invisibleText) {
		// 若宽高未知，从文件读
		if (imgW < 1 || imgH < 1) {
			using var skProbe = SKBitmap.Decode(pngPath);
			if (skProbe == null) throw new InvalidOperationException("无法解码页图: " + pngPath);
			imgW = skProbe.Width;
			imgH = skProbe.Height;
		}

		var page = doc.AddPage();
		var wPt = imgW * 72.0 / dpi;
		var hPt = imgH * 72.0 / dpi;
		page.Width = XUnit.FromPoint(wPt);
		page.Height = XUnit.FromPoint(hPt);

		if (invisibleText && lines != null && lines.Count > 0) {
			using var gfxText = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Prepend);
			foreach (var line in lines) {
				if (line?.Box == null || line.Box.Length < 2 || string.IsNullOrEmpty(line.Text))
					continue;
				try { drawline(gfxText, line, dpi, XBrushes.Black); }
				catch { }
			}
		}

		using var gfxImg = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
		using (var fs = File.OpenRead(pngPath))
		using (var ximg = XImage.FromStream(fs))
			gfxImg.DrawImage(ximg, 0, 0, wPt, hPt);
	}

	static void drawline(XGraphics gfx, OcrLine line, int dpi, XBrush brush) {
		var box = line.Box;
		double topt(float px) => px * 72.0 / dpi;

		float minX = box.Min(p => p.X), maxX = box.Max(p => p.X);
		float minY = box.Min(p => p.Y), maxY = box.Max(p => p.Y);
		var edgeW = Math.Max(2.0, maxX - minX);
		var edgeH = Math.Max(2.0, maxY - minY);

		var x0 = box[0].X;
		var y0 = box[0].Y;
		var x1 = box[1].X;
		var y1 = box[1].Y;
		var angle = Math.Atan2(y1 - y0, x1 - x0) * 180.0 / Math.PI;
		if (Math.Abs(angle) < 3 || Math.Abs(Math.Abs(angle) - 180) < 3) {
			angle = 0;
			x0 = minX;
			y0 = minY;
		}

		var fontSize = Compat.Clamp(topt((float)edgeH) * 0.95, 5.0, 120.0);
		var font = makefont(fontSize);
		var text = line.Text ?? "";
		if (text.Length == 0) return;

		var originX = topt(angle == 0 ? minX : x0);
		var originY = topt(angle == 0 ? (float)(minY + edgeH * 0.90) : (float)(y0 + edgeH * 0.90));

		gfx.Save();
		gfx.TranslateTransform(originX, originY);
		if (Math.Abs(angle) > 0.5)
			gfx.RotateTransform(angle);
		var size = gfx.MeasureString(text, font);
		if (size.Width > 0.5 && edgeW > 1) {
			var targetW = topt((float)edgeW);
			var sx = targetW / size.Width;
			if (sx > 0.2 && sx < 12)
				gfx.ScaleTransform(sx, 1);
		}
		gfx.DrawString(text, font, brush, new XPoint(0, 0));
		gfx.Restore();
	}

	static XFont makefont(double size) =>
		new XFont(CJK_FAMILY, size, XFontStyleEx.Regular);

	static string findcjkfont() {
		var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
		string[] names = [
			"simhei.ttf", "Deng.ttf", "STXIHEI.TTF", "simfang.ttf", "simkai.ttf",
			"STSONG.TTF", "STKAITI.TTF", "simsunb.ttf", "msyh.ttf",
		];
		foreach (var n in names) {
			var p = Path.Combine(fonts, n);
			if (File.Exists(p)) return p;
		}
		try {
			foreach (var f in Directory.GetFiles(fonts, "*.ttf")) {
				var name = Path.GetFileName(f).ToLowerInvariant();
				if (name.Contains("hei") || name.Contains("song") || name.Contains("deng")
					|| name.Contains("yuan") || name.Contains("kai"))
					return f;
			}
		}
		catch { }
		return null;
	}

	sealed class CjkFontResolver : IFontResolver {
		readonly string path;
		readonly string faceKey;

		public CjkFontResolver(string fontPath) {
			path = fontPath;
			faceKey = "WpfOcrCjk#" + Path.GetFileName(fontPath);
		}

		public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) {
			if (!string.IsNullOrEmpty(path))
				return new FontResolverInfo(faceKey);
			return null;
		}

		public byte[] GetFont(string faceName) {
			if (faceName != null && faceName.StartsWith("WpfOcrCjk", StringComparison.OrdinalIgnoreCase)
				&& File.Exists(path))
				return File.ReadAllBytes(path);
			return null;
		}
	}
}
