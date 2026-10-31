using System.IO;
using OpenCvSharp;

namespace ScreenKit;

/// <summary>
/// PDF 渲染 / 识别 / 导出（可检索 PDF）的入口。
/// 真正操作 PdfSharp / PDFtoImage / SkiaSharp 的代码在 <see cref="PdfRender"/>：
/// 这些程序集按需安装，只有确认已就绪后才加载，精简包缺 DLL 时不会类型加载失败。
/// </summary>
static class PdfOcr {
	public const int DefaultDpi = 150;

	public static void EnsureFonts() {
		NativeRuntime.EnsurePdf();
		PdfRender.EnsureFonts();
	}

	public static int GetPageCount(string pdfPath) {
		NativeRuntime.EnsurePdf();
		return PdfRender.GetPageCount(pdfPath);
	}

	public static int GetPageCount(byte[] pdfBytes) {
		NativeRuntime.EnsurePdf();
		return PdfRender.GetPageCount(pdfBytes);
	}

	/// <summary>渲染单页为 PNG 文件，返回宽高。</summary>
	public static (int w, int h) RenderPageToFile(byte[] pdfBytes, int pageIndex, int dpi, string pngPath) {
		NativeRuntime.EnsurePdf();
		return PdfRender.RenderPageToFile(pdfBytes, pageIndex, dpi, pngPath);
	}

	/// <summary>从 PDF 建立工程：渲染全部页图到草稿目录（尚未识别）。</summary>
	public static PdfOcrProject CreateFromPdf(
		string pdfPath,
		int dpi,
		bool invisibleText,
		string draftDir,
		Action<int, int, string> progress) {
		NativeRuntime.EnsurePdf();
		return PdfRender.CreateFromPdf(pdfPath, dpi, invisibleText, draftDir, progress);
	}

	/// <summary>识别单页图像文件，写回 page.Lines。</summary>
	public static void RecognizePage(PdfPageEdit page, string imagePath, OcrOptions opt, OcrRunner runner) {
		if (page == null) throw new ArgumentNullException(nameof(page));
		if (!File.Exists(imagePath))
			throw new FileNotFoundException("页面图像不存在", imagePath);
		if (WinOcr.Is(opt)) {
			var win = runner.Run(opt, imagePath) ?? new OcrResult();
			page.Lines = (win.Lines ?? new List<OcrLine>()).Select(PdfLineEdit.FromOcr).ToList();
			page.Recognized = true;
			return;
		}
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
		NativeRuntime.EnsurePdf();
		PdfRender.Export(proj, outputPath, progress);
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
		var proj = CreateFromPdf(inputPath, dpi, invisibleText, draft, progress);
		for (int i = 0; i < proj.Pages.Count; i++) {
			progress?.Invoke(i + 1, proj.Pages.Count, $"识别第 {i + 1}/{proj.Pages.Count} 页…");
			RecognizePage(proj.Pages[i], proj.ImagePath(proj.Pages[i]), opt, runner);
		}
		progress?.Invoke(proj.Pages.Count, proj.Pages.Count, "导出 PDF…");
		Export(proj, outputPath, progress);
		LastFullText = proj.FullText();
		LastPageCount = proj.Pages.Count;
		LastFontUsed = PdfRender.FontPath;
	}

	public static string LastFullText { get; private set; } = "";
	public static int LastPageCount { get; private set; }
	public static string LastFontUsed { get; private set; } = "";
}
