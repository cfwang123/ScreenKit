using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ScreenKit;

/// <summary>把 Markdown 转成 HTML，并用 Windows 10 自带的 IE11 内核打开。</summary>
static class MdRead {
	/// <summary>IE11 默认文档模式。Windows 10 的 iexplore 用这个值：有 HTML5 DOCTYPE 时走 IE11。</summary>
	public const int EMUL_IE11 = 11000;
	const string EMUL_KEY = @"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION";

	public static bool IsMd(string path) {
		var ext = Path.GetExtension(path ?? "");
		return ext.Equals(".md", StringComparison.OrdinalIgnoreCase)
			|| ext.Equals(".markdown", StringComparison.OrdinalIgnoreCase)
			|| ext.Equals(".mdown", StringComparison.OrdinalIgnoreCase)
			|| ext.Equals(".mkd", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>在创建 WebBrowser 之前写入仿真值。内核只有系统里的 IE11，这里让文档模式不要停在控件默认的 IE7。</summary>
	public static int EnsureIe11() {
		var exe = "ScreenKit.exe";
		try { exe = Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName); }
		catch { }
		if (string.IsNullOrEmpty(exe)) exe = "ScreenKit.exe";
		using (var key = Registry.CurrentUser.CreateSubKey(EMUL_KEY)) {
			key.SetValue(exe, EMUL_IE11, RegistryValueKind.DWord);
		}
		return EMUL_IE11;
	}

	public static int EmulValue() {
		var exe = "ScreenKit.exe";
		try { exe = Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName); }
		catch { }
		using (var key = Registry.CurrentUser.OpenSubKey(EMUL_KEY)) {
			var v = key?.GetValue(exe);
			if (v is int n) return n;
		}
		return 0;
	}

	public static string ReadText(string path) => Decode(File.ReadAllBytes(path));

	public static string Decode(byte[] bytes) {
		if (bytes == null || bytes.Length == 0) return "";
		if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
			return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
		if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
			return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
		if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
			return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
		try {
			return new UTF8Encoding(false, true).GetString(bytes);
		}
		catch (DecoderFallbackException) {
			return Encoding.GetEncoding(936).GetString(bytes);
		}
	}

	/// <summary>页面基准字号（DIP）。正文 15 在 100% 缩放下偏小，按 16 排版。</summary>
	public const double BaseFont = 16;

	/// <summary>阅读器配色。IE11 没有 CSS 变量，颜色直接写进样式。</summary>
	public sealed class MdTheme {
		public string Id;
		public string Bg;
		public string Fg;
		public string Muted;
		public string Border;
		public string Soft;
		public string CodeBg;
		public string Link;
		public string Quote;
		public string Bar;
		public string ThBg;
	}

	/// <summary>可选主题，数组顺序就是下拉框顺序。Id 存进 config.toml 的 mdread_theme。</summary>
	public static readonly MdTheme[] Themes = {
		new MdTheme { Id = "light", Bg = "#ffffff", Fg = "#1f2937", Muted = "#6b7280", Border = "#e5e7eb", Soft = "#f3f4f6", CodeBg = "#f3f4f6", Link = "#c2410c", Quote = "#4b5563", Bar = "#d1d5db", ThBg = "#f9fafb" },
		new MdTheme { Id = "dark", Bg = "#1e1f22", Fg = "#d8dbe0", Muted = "#8b919c", Border = "#3a3d43", Soft = "#2b2d31", CodeBg = "#2b2d31", Link = "#ffab70", Quote = "#a9b0ba", Bar = "#4a4e56", ThBg = "#26282c" },
		new MdTheme { Id = "sepia", Bg = "#f6efdf", Fg = "#3b3226", Muted = "#8a7a60", Border = "#ded0b6", Soft = "#ece2cd", CodeBg = "#ece2cd", Link = "#96551c", Quote = "#5b4d3b", Bar = "#d3c2a2", ThBg = "#efe5d0" },
		new MdTheme { Id = "green", Bg = "#cde8d1", Fg = "#1f2f22", Muted = "#55705a", Border = "#a9c9ae", Soft = "#bfe0c4", CodeBg = "#bfe0c4", Link = "#0e6b3a", Quote = "#33513a", Bar = "#8fb497", ThBg = "#bfe0c4" },
		new MdTheme { Id = "night", Bg = "#1b2430", Fg = "#ccd6e2", Muted = "#7f92a8", Border = "#2e3d50", Soft = "#223040", CodeBg = "#223040", Link = "#7cb3ff", Quote = "#a2b3c6", Bar = "#3a5270", ThBg = "#223040" },
	};

	/// <summary>按 id 取主题；未知或留空回落到明亮。</summary>
	public static MdTheme ThemeOf(string id) {
		foreach (var t in Themes) {
			if (string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) return t;
		}
		return Themes[0];
	}

	/// <param name="scale">窗口 DPI 缩放。IE 控件按物理像素排版，不跟随系统缩放，这里补上比例。</param>
	/// <param name="theme">主题 Id，见 <see cref="Themes"/>。</param>
	public static string EmptyHtml(string hint, double scale = 1, string theme = null) {
		var sb = new StringBuilder();
		shell(sb, "", "", scale, ThemeOf(theme));
		sb.Append("<div class=\"hint\">").Append(esc(hint ?? "")).Append("</div>");
		shellend(sb);
		return sb.ToString();
	}

	/// <summary>正文 HTML 片段，不含页面外壳。小说阅读器嵌进自己页面时也用这一份。</summary>
	public static string ToBody(string md, string baseDir, bool sentence = false, List<(string Id, string Text)> chapters = null) {
		var r = MdHtml.Render(md, new MdHtml.Opt { BaseDir = baseDir ?? "", Sentence = sentence, Chapter = sentence });
		if (chapters != null && r.Chapters.Count > 0) chapters.AddRange(r.Chapters);
		return r.Html;
	}

	public static string ToHtml(string md, string baseDir, string title, double scale = 1, string theme = null) {
		var sb = new StringBuilder();
		shell(sb, baseDir, title, scale, ThemeOf(theme));
		sb.Append(ToBody(md, baseDir));
		shellend(sb);
		return sb.ToString();
	}

	/// <summary>.lnk 快捷方式取目标文件；不是快捷方式或解析不出来时原样返回。</summary>
	public static string Resolve(string path) {
		if (string.IsNullOrEmpty(path) || !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return path;
		object shell = null;
		try {
			var type = Type.GetTypeFromProgID("WScript.Shell");
			if (type == null) return path;
			shell = Activator.CreateInstance(type);
			dynamic sc = ((dynamic)shell).CreateShortcut(path);
			var target = Convert.ToString(sc.TargetPath);
			if (string.IsNullOrEmpty(target)) return path;
			if (!Path.IsPathRooted(target)) {
				var dir = Path.GetDirectoryName(path);
				if (string.IsNullOrEmpty(dir)) return path;
				target = Path.GetFullPath(Path.Combine(dir, target));
			}
			return File.Exists(target) ? target : path;
		}
		catch { return path; }
		finally {
			if (shell != null && Marshal.IsComObject(shell)) {
				try { Marshal.ReleaseComObject(shell); } catch { }
			}
		}
	}

	public static int SelfTest(Action<string> log) {
		void say(string s) { try { log?.Invoke(s); } catch { } }
		var dir = Path.Combine(Path.GetTempPath(), "ScreenKit");
		Directory.CreateDirectory(dir);
		var src = Path.Combine(dir, "mdread-test.md");
		var page = Path.Combine(dir, "mdread-test.html");
		try {
			var html = ToHtml("""
				# 标题

				Hello **粗** and `a<b>`.

				- 一
				- 二

				```
				<script>
				```

				| A | B |
				|---|---|
				| 1 | 2 |

				![图](img.png)

				[坏](javascript:alert(1))
				""", @"D:\docs", "t.md");
			if (html.IndexOf("<h1", StringComparison.Ordinal) < 0 || html.IndexOf("标题", StringComparison.Ordinal) < 0)
				return bad(say, "heading");
			if (html.IndexOf("<strong>粗</strong>", StringComparison.Ordinal) < 0)
				return bad(say, "bold");
			if (html.IndexOf("<code>a&lt;b&gt;</code>", StringComparison.Ordinal) < 0)
				return bad(say, "code");
			if (html.IndexOf("<li>", StringComparison.Ordinal) < 0)
				return bad(say, "list");
			var nest = ToHtml("- 父\n\t● 子\n\t\t● 孙\n", "", "t");
			if (nest.IndexOf("<li>父<ul><li>子<ul><li>孙</li></ul></li></ul></li>", StringComparison.Ordinal) < 0)
				return bad(say, "nest");
			if (html.IndexOf("&lt;script&gt;", StringComparison.Ordinal) < 0)
				return bad(say, "fence");
			if (html.IndexOf("<table>", StringComparison.Ordinal) < 0 || html.IndexOf("<td>1</td>", StringComparison.Ordinal) < 0)
				return bad(say, "table");
			if (html.IndexOf("file:///D:/docs/img.png", StringComparison.Ordinal) < 0)
				return bad(say, "img");
			var cjk = ToHtml("![图](图 1.png)", @"D:\我的文档", "t");
			if (cjk.IndexOf("src=\"file:///D:/我的文档/图%201.png\"", StringComparison.Ordinal) < 0)
				return bad(say, "img cjk");
			if (cjk.IndexOf("<base href=\"file:///D:/我的文档/\">", StringComparison.Ordinal) < 0)
				return bad(say, "base cjk");
			var enc = ToHtml("![x](docs/1%20screenshot.en.png)", @"D:\repo", "t");
			if (enc.IndexOf("src=\"file:///D:/repo/docs/1%20screenshot.en.png\"", StringComparison.Ordinal) < 0)
				return bad(say, "img escaped");
			if (html.IndexOf("mdzoom-tip", StringComparison.Ordinal) < 0 || html.IndexOf("window.mdzoom", StringComparison.Ordinal) < 0)
				return bad(say, "zoom script");
			if (html.IndexOf("javascript:", StringComparison.OrdinalIgnoreCase) >= 0)
				return bad(say, "script link");
			if (html.IndexOf("IE=11", StringComparison.Ordinal) < 0)
				return bad(say, "meta");
			if (html.IndexOf("<base href=\"file:///D:/docs/\">", StringComparison.Ordinal) < 0)
				return bad(say, "base");
			File.WriteAllBytes(src, Encoding.GetEncoding(936).GetBytes("# 中文\n"));
			if (ReadText(src).IndexOf("中文", StringComparison.Ordinal) < 0)
				return bad(say, "gbk");
			if (!IsMd(src) || IsMd("a.txt"))
				return bad(say, "ext");
			if (ToHtml("# 缩放\n", "", "t", 1.5).IndexOf("font:24px/1.65", StringComparison.Ordinal) < 0)
				return bad(say, "scale");
			var dark = ToHtml("# 暗\n", "", "t", 1, "dark");
			if (dark.IndexOf("background:#1e1f22", StringComparison.Ordinal) < 0 || dark.IndexOf("#ffffff", StringComparison.Ordinal) >= 0)
				return bad(say, "theme dark");
			if (ThemeOf("nope").Id != "light" || Themes.Length < 4)
				return bad(say, "theme fallback");
			var frag = new List<(string Id, string Text)>();
			var sp = ToBody("# 一\n\n句子一。句子二。\n", "", true, frag);
			if (sp.IndexOf("<span class=\"rs\">句子一。</span>", StringComparison.Ordinal) < 0)
				return bad(say, "sentence");
			if (frag.Count != 1 || frag[0].Id != "rdc0" || frag[0].Text != "一")
				return bad(say, "chapter");
			if (ToBody("# 一\n", "").IndexOf("class=\"rs\"", StringComparison.Ordinal) >= 0)
				return bad(say, "plain");
			if (MdExport.PrintHtml(dark).IndexOf("background:#fff", StringComparison.Ordinal) < 0)
				return bad(say, "print theme");
			var lnk = Path.Combine(dir, "mdread-test.lnk");
			try {
				var w = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
				dynamic sc = ((dynamic)w).CreateShortcut(lnk);
				sc.TargetPath = src;
				sc.Save();
				var got = Resolve(lnk);
				if (!string.Equals(got, src, StringComparison.OrdinalIgnoreCase))
					return bad(say, "lnk " + got);
				sc = null;
			}
			catch (Exception ex) { return bad(say, "lnk " + ex.Message); }
			finally { try { File.Delete(lnk); } catch { } }
			if (!string.Equals(Resolve(src), src, StringComparison.Ordinal))
				return bad(say, "lnk passthrough");
			if (EnsureIe11() != EMUL_IE11 || EmulValue() != EMUL_IE11)
				return bad(say, "emul " + EmulValue());
			File.WriteAllText(page, html, new UTF8Encoding(true));
			if (!probeshell(page, out var mode, out var ua))
				return bad(say, "probe " + ua);
			if (mode != 11)
				return bad(say, "documentMode " + mode + " " + ua);
			if (ua.IndexOf("Trident/7.0", StringComparison.Ordinal) < 0)
				return bad(say, "ua " + ua);
			say("documentMode " + mode);
			say("OK");
			return 0;
		}
		catch (Exception ex) {
			return bad(say, ex.Message);
		}
		finally {
			try { File.Delete(src); } catch { }
			try { File.Delete(page); } catch { }
		}
	}

	static int bad(Action<string> say, string msg) {
		say("FAIL: " + msg);
		return 1;
	}

	static bool probeshell(string page, out int mode, out string ua) {
		mode = 0;
		ua = "";
		var f = new System.Windows.Forms.Form {
			ShowInTaskbar = false,
			FormBorderStyle = System.Windows.Forms.FormBorderStyle.None,
			Opacity = 0,
			StartPosition = System.Windows.Forms.FormStartPosition.Manual,
			Location = new System.Drawing.Point(-2400, -2400),
			Size = new System.Drawing.Size(480, 320),
		};
		var wb = new System.Windows.Forms.WebBrowser {
			Dock = System.Windows.Forms.DockStyle.Fill,
			ScriptErrorsSuppressed = true,
		};
		f.Controls.Add(wb);
		var frame = new DispatcherFrame();
		var err = "";
		var got = 0;
		var agent = "";
		wb.DocumentCompleted += (_, e) => {
			if (e.Url == null || !e.Url.IsFile) return;
			try {
				dynamic dom = wb.Document.DomDocument;
				got = Convert.ToInt32(dom.documentMode);
				agent = (string)dom.parentWindow.navigator.userAgent;
			}
			catch (Exception ex) { err = ex.GetType().Name + " " + ex.Message; }
			frame.Continue = false;
		};
		var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
		timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
		try {
			f.Show();
			timer.Start();
			wb.Navigate(page);
			Dispatcher.PushFrame(frame);
		}
		finally {
			timer.Stop();
			try { f.Close(); } catch { }
			try { wb.Dispose(); } catch { }
			try { f.Dispose(); } catch { }
		}
		mode = got;
		ua = agent;
		if (mode == 0 && err.Length > 0) ua = err;
		return mode != 0;
	}

	// ==================== HTML ====================
	#region
	static void shell(StringBuilder sb, string baseDir, string title, double scale, MdTheme th) {
		if (scale < 1) scale = 1;
		if (scale > 4) scale = 4;
		th = th ?? Themes[0];
		var fs = (BaseFont * scale).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
		sb.Append("<!DOCTYPE html><html><head>");
		sb.Append("<meta http-equiv=\"X-UA-Compatible\" content=\"IE=11\">");
		sb.Append("<meta charset=\"utf-8\">");
		if (!string.IsNullOrEmpty(title))
			sb.Append("<title>").Append(esc(title)).Append("</title>");
		if (!string.IsNullOrEmpty(baseDir)) {
			var dir = baseDir;
			if (!dir.EndsWith("\\") && !dir.EndsWith("/")) dir += "\\";
			try {
				sb.Append("<base href=\"").Append(esc(MdHtml.FileUrl(dir))).Append("\">");
			}
			catch { }
		}
		// 字号留一处 px（随 DPI 缩放），其余用 em，导出时改 body 字号即可整体缩回 A4 排版
		sb.Append("<style>");
		sb.Append("html{height:100%;background:").Append(th.Bg).Append(";}");
		sb.Append("body{box-sizing:border-box;height:100%;margin:0;max-width:100%;overflow:auto;padding:1.75em 2em 4em;");
		sb.Append("font:").Append(fs).Append("px/1.65 'Segoe UI','Microsoft YaHei UI',sans-serif;");
		sb.Append("color:").Append(th.Fg).Append(";background:").Append(th.Bg).Append(";}");
		sb.Append("h1{font-size:1.75em;line-height:1.3;border-bottom:1px solid ").Append(th.Border).Append(";padding-bottom:0.5em;}");
		sb.Append("h2{font-size:1.375em;line-height:1.35;border-bottom:1px solid ").Append(th.Soft).Append(";padding-bottom:0.25em;}");
		sb.Append("h3{font-size:1.125em;}");
		sb.Append("p{margin:0 0 0.75em;}");
		sb.Append("ul,ol{margin:0;padding:0 0 0 2em;}");
		sb.Append("body>ul,body>ol{padding-left:0;}");
		sb.Append("li{margin:0;}");
		sb.Append("a{color:").Append(th.Link).Append(";}");
		sb.Append("code{font-family:Consolas,'Cascadia Mono',monospace;background:").Append(th.CodeBg).Append(";padding:0.07em 0.27em;}");
		sb.Append("pre{background:").Append(th.CodeBg).Append(";padding:0.75em 0.9em;overflow:auto;max-width:100%;}");
		sb.Append("pre code{background:transparent;padding:0;}");
		sb.Append("blockquote{margin:0 0 0.75em;padding:0.12em 0.9em;border-left:4px solid ").Append(th.Bar);
		sb.Append(";color:").Append(th.Quote).Append(";}");
		sb.Append(".tbl{max-width:100%;overflow:auto;margin:0 0 0.9em;}");
		sb.Append("table{border-collapse:collapse;margin:0;}");
		sb.Append("th,td{border:1px solid ").Append(th.Border).Append(";padding:0.4em 0.63em;}");
		sb.Append("th{background:").Append(th.ThBg).Append(";}");
		sb.Append("img{max-width:100%;height:auto;cursor:zoom-in;}");
		sb.Append(".mdzoom{position:fixed;left:0;top:0;width:100%;height:100%;z-index:9999;overflow:auto;");
		sb.Append("background:rgba(0,0,0,.88);cursor:zoom-out;text-align:center;}");
		sb.Append(".mdzoom img{display:block;max-width:96%;max-height:92%;margin:2% auto 0;background:#fff;cursor:zoom-out;}");
		sb.Append(".mdzoom-tip{position:fixed;left:0;right:0;bottom:10px;color:#d5d5d5;font-size:13px;text-align:center;}");
		sb.Append("hr{border:0;border-top:1px solid ").Append(th.Border).Append(";margin:1em 0;}");
		sb.Append(".hint{color:").Append(th.Muted).Append(";font-size:1.07em;padding:3em 0.5em;}");
		sb.Append("</style></head><body>");
	}

	static void shellend(StringBuilder sb) {
		sb.Append("<script>function mdmode(){try{return document.documentMode+'|'+navigator.userAgent;}catch(e){return '';}}</script>");
		sb.Append(zoomjs());
		sb.Append("</body></html>");
	}

	/// <summary>
	/// 点图片放大到满窗，再点或按 Esc 关闭，不做动画。
	/// Web 版的 viewer.js 是 ES6 语法，IE11 解析不了，这里用同样效果的 ES5 实现。
	/// </summary>
	static string zoomjs() {
		return "<script>(function(){"
			+ "var box=null;"
			+ "function shut(){if(box&&box.parentNode)box.parentNode.removeChild(box);box=null;}"
			+ "function open(img){shut();"
			+ "box=document.createElement('div');box.className='mdzoom';"
			+ "var big=document.createElement('img');big.src=img.src;big.alt=img.alt||'';"
			+ "box.appendChild(big);"
			+ "var tip=document.createElement('div');tip.className='mdzoom-tip';tip.innerHTML=" + jsstr(Loc.T("mdread.zoom.hint")) + ";"
			+ "box.appendChild(tip);"
			+ "box.onclick=shut;document.body.appendChild(box);"
			+ "}"
			+ "document.onclick=function(e){if(box)return;"
			+ "var t=e?e.target:(window.event?window.event.srcElement:null);"
			+ "if(!t||t.tagName!='IMG')return;"
			+ "if(e&&e.preventDefault)e.preventDefault();else if(window.event)window.event.returnValue=false;"
			+ "open(t);};"
			+ "document.onkeydown=function(e){var k=(e||window.event||{}).keyCode;if(k==27)shut();};"
			+ "window.mdzoom=open;"
			+ "})();</script>";
	}

	static string jsstr(string s) {
		var sb = new StringBuilder("\"");
		foreach (var c in s ?? "") {
			if (c == '"' || c == '\\') sb.Append('\\').Append(c);
			else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
			else sb.Append(c);
		}
		return sb.Append('"').ToString();
	}

	static string esc(string s) {
		if (string.IsNullOrEmpty(s)) return "";
		return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
	}
	#endregion
}
