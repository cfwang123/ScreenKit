using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenKit;

/// <summary>MD 阅读器导出用到的失败类型。Key 是 mdread.* 语言键的后半段。</summary>
sealed class MdExportError : Exception {
	public string Key { get; }

	public MdExportError(string key) : base(key) { Key = key; }
}

/// <summary>把阅读器显示的 HTML 导出为 docx（Word COM）或 pdf（Edge/Chrome 无头打印）。</summary>
static class MdExport {
	/// <summary>在阅读器样式后面追加打印样式，去掉满屏滚动，按 A4 分页。</summary>
	public static string PrintHtml(string html) {
		if (string.IsNullOrEmpty(html)) return "";
		const string css = "<style>"
			+ "@page{size:A4;margin:18mm 16mm;}"
			+ "html,body{height:auto;overflow:visible;}"
			+ "html{background:#fff;}"
			+ "body{padding:0;font-size:11.5pt;line-height:1.6;background:#fff;color:#1f2937;}"
			+ "pre{white-space:pre-wrap;word-wrap:break-word;page-break-inside:auto;}"
			+ ".tbl{overflow:visible;}"
			+ "tr,img{page-break-inside:avoid;}"
			+ "h1,h2,h3,h4{page-break-after:avoid;}"
			+ "h1,h2,hr,th,td{border-color:#e5e7eb;}"
			+ "th{background:#f9fafb;}"
			+ "code,pre{background:#f3f4f6;}"
			+ "blockquote{color:#4b5563;border-left-color:#d1d5db;}"
			+ "a{color:#1f2937;}"
			+ "</style>";
		var i = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
		return i < 0 ? html : html.Insert(i, css);
	}

	/// <summary>用 Word 打开 HTML 再另存为 docx（FileFormat 12）。</summary>
	public static void ToWord(string html, string outpath) {
		var type = Type.GetTypeFromProgID("Word.Application");
		if (type == null) throw new MdExportError("noword");
		var src = writetemp(html, ".html");
		object app = null, doc = null;
		try {
			app = Activator.CreateInstance(type);
			dynamic w = app;
			w.Visible = false;
			w.DisplayAlerts = 0;
			doc = w.Documents.Open(src, false, true, false);
			// Word 导入 HTML 时图片是外链（wdInlineShapeLinkedPicture），断开链接才写进 docx
			unlink(doc);
			((dynamic)doc).SaveAs(outpath, 12);
		}
		finally {
			if (doc != null) {
				try { ((dynamic)doc).Close(false); } catch { }
				try { if (Marshal.IsComObject(doc)) Marshal.ReleaseComObject(doc); } catch { }
			}
			if (app != null) {
				try { ((dynamic)app).Quit(false); } catch { }
				try { if (Marshal.IsComObject(app)) Marshal.ReleaseComObject(app); } catch { }
			}
			delfile(src);
		}
	}

	/// <summary>用 Edge 或 Chrome 的无头模式打印成 PDF。</summary>
	public static void ToPdf(string html, string outpath) {
		var exe = findbrowser();
		if (exe == null) throw new MdExportError("nobrowser");
		var dir = Path.Combine(Path.GetTempPath(), "ScreenKit");
		Directory.CreateDirectory(dir);
		var src = writetemp(html, ".html");
		var profile = Path.Combine(dir, "pdfprofile-" + Environment.TickCount.ToString("x"));
		try {
			Directory.CreateDirectory(profile);
			var uri = new Uri(src).AbsoluteUri;
			var done = false;
			foreach (var head in new[] { "--headless=new", "--headless" }) {
				delfile(outpath);
				var args = $"{head} --disable-gpu --no-first-run --no-default-browser-check --no-pdf-header-footer"
					+ $" --allow-file-access-from-files --virtual-time-budget=10000"
					+ $" --user-data-dir=\"{profile}\" --print-to-pdf=\"{outpath}\" \"{uri}\"";
				using (var p = Process.Start(new ProcessStartInfo {
					FileName = exe,
					Arguments = args,
					UseShellExecute = false,
					CreateNoWindow = true,
					WindowStyle = ProcessWindowStyle.Hidden,
					WorkingDirectory = dir,
				})) {
					if (p == null) continue;
					if (!p.WaitForExit(60000)) {
						try { p.Kill(); } catch { }
						continue;
					}
				}
				for (var i = 0; i < 20 && !fileok(outpath); i++) Thread.Sleep(100);
				if (fileok(outpath)) { done = true; break; }
			}
			if (!done) throw new MdExportError("pdffail");
		}
		finally {
			delfile(src);
			deldir(profile);
		}
	}

	static void unlink(object doc) {
		dynamic shapes = ((dynamic)doc).InlineShapes;
		var n = (int)shapes.Count;
		for (var i = 1; i <= n; i++) {
			try {
				dynamic sh = shapes.Item(i);
				if ((int)sh.Type == 4) sh.LinkFormat.BreakLink();
			}
			catch { }
		}
	}

	static string writetemp(string html, string ext) {
		var dir = Path.Combine(Path.GetTempPath(), "ScreenKit");
		Directory.CreateDirectory(dir);
		var path = Path.Combine(dir, "mdexport-" + Environment.TickCount.ToString("x") + ext);
		File.WriteAllText(path, html ?? "", new UTF8Encoding(false));
		return path;
	}

	static string findbrowser() {
		var roots = new[] {
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		};
		foreach (var root in roots) {
			if (string.IsNullOrEmpty(root)) continue;
			var edge = Path.Combine(root, @"Microsoft\Edge\Application\msedge.exe");
			if (fileok(edge)) return edge;
		}
		foreach (var root in roots) {
			if (string.IsNullOrEmpty(root)) continue;
			var chrome = Path.Combine(root, @"Google\Chrome\Application\chrome.exe");
			if (fileok(chrome)) return chrome;
		}
		var found = ChromeTts.FindExe(null);
		return fileok(found) ? found : null;
	}

	static bool fileok(string path) {
		try { return !string.IsNullOrEmpty(path) && File.Exists(path) && new FileInfo(path).Length > 0; }
		catch { return false; }
	}

	static void delfile(string path) {
		for (var i = 0; i < 5; i++) {
			try {
				if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
				return;
			}
			catch { Thread.Sleep(100); }
		}
	}

	static void deldir(string path) {
		for (var i = 0; i < 10; i++) {
			try {
				if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) Directory.Delete(path, true);
				return;
			}
			catch { Thread.Sleep(150); }
		}
	}
}
