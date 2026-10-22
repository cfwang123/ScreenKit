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

	public static string ToHtml(string md, string baseDir, string title, double scale = 1, string theme = null) {
		var sb = new StringBuilder();
		shell(sb, baseDir, title, scale, ThemeOf(theme));
		sb.Append(body(md ?? "", baseDir));
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
				sb.Append("<base href=\"").Append(esc(fileurl(dir))).Append("\">");
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
		sb.Append("img{max-width:100%;height:auto;}");
		sb.Append("hr{border:0;border-top:1px solid ").Append(th.Border).Append(";margin:1em 0;}");
		sb.Append(".hint{color:").Append(th.Muted).Append(";font-size:1.07em;padding:3em 0.5em;}");
		sb.Append("</style></head><body>");
	}

	static void shellend(StringBuilder sb) {
		sb.Append("<script>function mdmode(){try{return document.documentMode+'|'+navigator.userAgent;}catch(e){return '';}}</script>");
		sb.Append("</body></html>");
	}

	static string esc(string s) {
		if (string.IsNullOrEmpty(s)) return "";
		return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
	}
	#endregion

	// ==================== Markdown ====================
	#region
	static string body(string md, string baseDir) {
		var lines = (md ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
		var sb = new StringBuilder();
		var i = 0;
		if (lines.Length > 0 && lines[0].Trim() == "---") {
			var j = 1;
			while (j < lines.Length && j < 80 && lines[j].Trim() != "---") j++;
			if (j < lines.Length && lines[j].Trim() == "---") i = j + 1;
		}
		while (i < lines.Length) {
			if (string.IsNullOrWhiteSpace(lines[i])) { i++; continue; }
			var line = lines[i];
			if (fenceopen(line, out var mk)) { i = addcode(sb, lines, i, mk); continue; }
			if (isheading(line, out var level, out var title)) { heading(sb, level, title, baseDir); i++; continue; }
			if (i + 1 < lines.Length && setext(lines[i + 1], out level) && !blockstart(line)) {
				heading(sb, level, line.Trim(), baseDir);
				i += 2;
				continue;
			}
			if (ishr(line)) { sb.Append("<hr>"); i++; continue; }
			if (istable(lines, i)) { i = addtable(sb, lines, i, baseDir); continue; }
			if (line.TrimStart().StartsWith(">")) { i = addquote(sb, lines, i, baseDir); continue; }
			if (listline(line, out _, out _, out _)) { i = addlist(sb, lines, i, baseDir); continue; }
			i = addpara(sb, lines, i, baseDir);
		}
		return sb.ToString();
	}

	static int addcode(StringBuilder sb, string[] lines, int i, string mk) {
		var code = new StringBuilder();
		i++;
		while (i < lines.Length && !fenceclose(lines[i], mk)) {
			if (code.Length > 0) code.Append('\n');
			code.Append(lines[i]);
			i++;
		}
		if (i < lines.Length) i++;
		sb.Append("<pre><code>").Append(esc(code.ToString())).Append("</code></pre>");
		return i;
	}

	static void heading(StringBuilder sb, int level, string title, string baseDir) {
		if (level < 1) level = 1;
		if (level > 6) level = 6;
		var id = slug(title);
		sb.Append("<h").Append(level);
		if (id.Length > 0) sb.Append(" id=\"").Append(esc(id)).Append('"');
		sb.Append('>');
		inline(sb, title, baseDir);
		sb.Append("</h").Append(level).Append('>');
	}

	static int addquote(StringBuilder sb, string[] lines, int i, string baseDir) {
		var inner = new StringBuilder();
		while (i < lines.Length && lines[i].TrimStart().StartsWith(">")) {
			var s = lines[i].TrimStart();
			s = s.Substring(1);
			if (s.StartsWith(" ")) s = s.Substring(1);
			inner.Append(s).Append('\n');
			i++;
		}
		sb.Append("<blockquote>").Append(body(inner.ToString(), baseDir)).Append("</blockquote>");
		return i;
	}

	static int addlist(StringBuilder sb, string[] lines, int i, string baseDir) {
		if (i >= lines.Length || !listline(lines[i], out var ind, out var ordered, out _)) return i;
		var start = i;
		sb.Append(ordered ? "<ol>" : "<ul>");
		while (i < lines.Length) {
			if (string.IsNullOrWhiteSpace(lines[i])) {
				var k = i + 1;
				while (k < lines.Length && string.IsNullOrWhiteSpace(lines[k])) k++;
				if (k < lines.Length && listline(lines[k], out var ind2, out _, out _) && ind2 >= ind) {
					i = k;
					continue;
				}
				break;
			}
			if (!listline(lines[i], out var cur, out var ord, out var item)) break;
			if (cur < ind) break;
			if (cur > ind) break;
			if (ord != ordered) break;
			sb.Append("<li>");
			if (!ordered && item.Length >= 4 && item[0] == '[' && item[2] == ']' && item[3] == ' '
				&& (item[1] == ' ' || item[1] == 'x' || item[1] == 'X')) {
				sb.Append(item[1] == ' '
					? "<input type=\"checkbox\" disabled> "
					: "<input type=\"checkbox\" disabled checked> ");
				item = item.Substring(4);
			}
			inline(sb, item.TrimEnd(), baseDir);
			var n = i + 1;
			if (n < lines.Length && listline(lines[n], out var nind, out _, out _) && nind > cur)
				n = addlist(sb, lines, n, baseDir);
			else {
				while (n < lines.Length && !string.IsNullOrWhiteSpace(lines[n]) && !listline(lines[n], out _, out _, out _)
					&& !blockstart(lines[n]) && indentof(lines[n]) > cur) {
					sb.Append("<br>");
					inline(sb, lines[n].Trim(), baseDir);
					n++;
				}
			}
			sb.Append("</li>");
			i = n;
			if (i == start) break;
		}
		sb.Append(ordered ? "</ol>" : "</ul>");
		if (i == start) i++;
		return i;
	}

	static int addtable(StringBuilder sb, string[] lines, int i, string baseDir) {
		var head = splitrow(lines[i]);
		var sep = splitrow(lines[i + 1]);
		sb.Append("<div class=\"tbl\"><table><thead><tr>");
		for (var c = 0; c < head.Count; c++) {
			var al = c < sep.Count ? alignof(sep[c]) : "";
			sb.Append("<th");
			if (al.Length > 0) sb.Append(" style=\"text-align:").Append(al).Append('"');
			sb.Append('>');
			inline(sb, head[c], baseDir);
			sb.Append("</th>");
		}
		sb.Append("</tr></thead><tbody>");
		i += 2;
		while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && lines[i].IndexOf('|') >= 0) {
			var cells = splitrow(lines[i]);
			sb.Append("<tr>");
			for (var c = 0; c < head.Count; c++) {
				var al = c < sep.Count ? alignof(sep[c]) : "";
				sb.Append("<td");
				if (al.Length > 0) sb.Append(" style=\"text-align:").Append(al).Append('"');
				sb.Append('>');
				if (c < cells.Count) inline(sb, cells[c], baseDir);
				sb.Append("</td>");
			}
			sb.Append("</tr>");
			i++;
		}
		sb.Append("</tbody></table></div>");
		return i;
	}

	static int addpara(StringBuilder sb, string[] lines, int i, string baseDir) {
		var buf = new List<string>();
		var brs = new List<bool>();
		while (i < lines.Length && !stoppara(lines, i)) {
			var raw = lines[i];
			var br = raw.EndsWith("  ") || raw.EndsWith("\\");
			if (br) {
				var cut = raw.EndsWith("\\") ? 1 : 2;
				raw = raw.Substring(0, raw.Length - cut);
			}
			buf.Add(raw.Trim());
			brs.Add(br);
			i++;
		}
		if (buf.Count == 0) return i + 1;
		sb.Append("<p>");
		for (var k = 0; k < buf.Count; k++) {
			inline(sb, buf[k], baseDir);
			if (k + 1 < buf.Count) sb.Append(brs[k] ? "<br>" : " ");
		}
		sb.Append("</p>");
		return i;
	}

	static bool stoppara(string[] lines, int i) {
		if (i >= lines.Length || string.IsNullOrWhiteSpace(lines[i])) return true;
		if (blockstart(lines[i])) return true;
		if (istable(lines, i)) return true;
		if (i + 1 < lines.Length && setext(lines[i + 1], out _)) return true;
		return false;
	}

	static bool blockstart(string line) {
		if (string.IsNullOrWhiteSpace(line)) return true;
		if (fenceopen(line, out _)) return true;
		if (isheading(line, out _, out _)) return true;
		if (ishr(line)) return true;
		if (line.TrimStart().StartsWith(">")) return true;
		if (listline(line, out _, out _, out _)) return true;
		return false;
	}

	static bool fenceopen(string line, out string mk) {
		var t = line.Trim();
		mk = "";
		if (t.StartsWith("```")) mk = "```";
		else if (t.StartsWith("~~~")) mk = "~~~";
		return mk.Length > 0;
	}

	static bool fenceclose(string line, string mk) {
		var t = line.Trim();
		return t.StartsWith(mk) && t.Trim('`', '~', ' ', '\t').Length == 0;
	}

	static bool isheading(string line, out int level, out string title) {
		level = 0;
		title = "";
		var t = line.Trim();
		while (level < t.Length && level < 6 && t[level] == '#') level++;
		if (level == 0) return false;
		if (level < t.Length && t[level] != ' ' && t[level] != '\t') { level = 0; return false; }
		title = t.Substring(level).Trim();
		while (title.EndsWith("#")) title = title.Substring(0, title.Length - 1).TrimEnd();
		return true;
	}

	static bool setext(string line, out int level) {
		var t = line.Trim();
		level = 0;
		if (t.Length < 3) return false;
		if (t.Trim('=').Length == 0) { level = 1; return true; }
		if (t.IndexOf('|') < 0 && t.Trim('-').Length == 0) { level = 2; return true; }
		return false;
	}

	static bool ishr(string line) {
		var t = line.Trim();
		if (t.Length < 3 || t.IndexOf('|') >= 0) return false;
		var c = t[0];
		if (c != '-' && c != '*' && c != '_') return false;
		var n = 0;
		foreach (var ch in t) {
			if (ch == c) { n++; continue; }
			if (ch != ' ' && ch != '\t') return false;
		}
		return n >= 3;
	}

	static bool istable(string[] lines, int i) {
		if (i + 1 >= lines.Length) return false;
		if (lines[i].IndexOf('|') < 0) return false;
		return tablesep(lines[i + 1]);
	}

	static bool tablesep(string line) {
		if (line.IndexOf('-') < 0) return false;
		var cells = splitrow(line);
		if (cells.Count == 0) return false;
		foreach (var cell in cells) {
			var c = cell.Trim();
			if (c.Length == 0) return false;
			foreach (var ch in c) {
				if (ch != '-' && ch != ':' && ch != ' ' && ch != '\t') return false;
			}
			var dash = 0;
			foreach (var ch in c) if (ch == '-') dash++;
			if (dash < 1) return false;
		}
		return true;
	}

	static List<string> splitrow(string line) {
		var s = line.Trim();
		if (s.StartsWith("|")) s = s.Substring(1);
		if (s.EndsWith("|")) s = s.Substring(0, s.Length - 1);
		var list = new List<string>();
		foreach (var part in s.Split('|')) list.Add(part.Trim());
		return list;
	}

	static string alignof(string cell) {
		var c = cell.Trim();
		var left = c.StartsWith(":");
		var right = c.EndsWith(":");
		if (left && right) return "center";
		if (right) return "right";
		if (left) return "left";
		return "";
	}

	static bool listline(string line, out int indent, out bool ordered, out string item) {
		var n = skipindent(line, out indent);
		ordered = false;
		item = "";
		var s = n < line.Length ? line.Substring(n) : "";
		if (s.StartsWith("- ") || s.StartsWith("* ") || s.StartsWith("+ ") || s.StartsWith("● ") || s.StartsWith("• ")) {
			item = s.Substring(2);
			return true;
		}
		var j = 0;
		while (j < s.Length && char.IsDigit(s[j])) j++;
		if (j > 0 && j < s.Length && (s[j] == '.' || s[j] == ')') && j + 1 < s.Length && s[j + 1] == ' ') {
			ordered = true;
			item = s.Substring(j + 2);
			return true;
		}
		return false;
	}

	static int indentof(string line) {
		skipindent(line, out var col);
		return col;
	}

	static int skipindent(string line, out int col) {
		col = 0;
		var n = 0;
		while (n < line.Length) {
			var c = line[n];
			if (c == ' ') { col++; n++; continue; }
			if (c == '\t') {
				col += 4 - (col % 4);
				n++;
				continue;
			}
			break;
		}
		return n;
	}

	static string slug(string title) {
		var sb = new StringBuilder();
		foreach (var c in title ?? "") {
			if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c > 127) sb.Append(c);
			else if (char.IsWhiteSpace(c) && sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
		}
		return sb.ToString().Trim('-');
	}

	static void inline(StringBuilder sb, string text, string baseDir) {
		inlineat(sb, text ?? "", baseDir, 0);
	}

	static void inlineat(StringBuilder sb, string text, string baseDir, int depth) {
		if (depth > 8) { sb.Append(esc(text)); return; }
		var i = 0;
		while (i < text.Length) {
			var c = text[i];
			if (c == '\\' && i + 1 < text.Length) {
				sb.Append(esc(text[i + 1].ToString()));
				i += 2;
				continue;
			}
			if (c == '`') {
				var end = text.IndexOf('`', i + 1);
				if (end > i) {
					sb.Append("<code>").Append(esc(text.Substring(i + 1, end - i - 1))).Append("</code>");
					i = end + 1;
					continue;
				}
			}
			if (c == '!' && i + 1 < text.Length && text[i + 1] == '[' && trylink(text, i + 1, out var alt, out var src, out var nxt)) {
				var href = safehref(src, baseDir);
				if (href.Length > 0)
					sb.Append("<img alt=\"").Append(esc(alt)).Append("\" src=\"").Append(esc(href)).Append("\">");
				else
					sb.Append(esc(alt));
				i = nxt;
				continue;
			}
			if (c == '[' && trylink(text, i, out var lab, out var url, out var next)) {
				var href = safehref(url, baseDir);
				if (href.Length == 0) inlineat(sb, lab, baseDir, depth + 1);
				else {
					sb.Append("<a href=\"").Append(esc(href)).Append("\">");
					inlineat(sb, lab, baseDir, depth + 1);
					sb.Append("</a>");
				}
				i = next;
				continue;
			}
			if (c == '<' && tryangle(text, i, out var ang, out var angEnd)) {
				var href = safehref(ang, baseDir);
				if (href.Length > 0) {
					sb.Append("<a href=\"").Append(esc(href)).Append("\">").Append(esc(ang)).Append("</a>");
					i = angEnd;
					continue;
				}
			}
			if (trymark(text, i, "**", out var mend) || trymark(text, i, "__", out mend)) {
				var mk = text.Substring(i, 2);
				sb.Append("<strong>");
				inlineat(sb, text.Substring(i + 2, mend - (i + 2)), baseDir, depth + 1);
				sb.Append("</strong>");
				i = mend + 2;
				continue;
			}
			if (trymark(text, i, "~~", out mend)) {
				sb.Append("<del>");
				inlineat(sb, text.Substring(i + 2, mend - (i + 2)), baseDir, depth + 1);
				sb.Append("</del>");
				i = mend + 2;
				continue;
			}
			if ((c == '*' || c == '_') && trymark(text, i, c.ToString(), out mend)) {
				sb.Append("<em>");
				inlineat(sb, text.Substring(i + 1, mend - (i + 1)), baseDir, depth + 1);
				sb.Append("</em>");
				i = mend + 1;
				continue;
			}
			if (tryurl(text, i, out var bare, out var bareEnd)) {
				var href = safehref(bare, baseDir);
				if (href.Length > 0) {
					sb.Append("<a href=\"").Append(esc(href)).Append("\">").Append(esc(bare)).Append("</a>");
					i = bareEnd;
					continue;
				}
			}
			sb.Append(esc(c.ToString()));
			i++;
		}
	}

	static bool trylink(string text, int i, out string label, out string dest, out int next) {
		label = "";
		dest = "";
		next = i;
		if (i >= text.Length || text[i] != '[') return false;
		var rb = text.IndexOf(']', i + 1);
		if (rb < 0 || rb + 1 >= text.Length || text[rb + 1] != '(') return false;
		var close = text.IndexOf(')', rb + 2);
		if (close < 0) return false;
		label = text.Substring(i + 1, rb - i - 1);
		dest = splitdest(text.Substring(rb + 2, close - (rb + 2)));
		next = close + 1;
		return true;
	}

	static string splitdest(string raw) {
		raw = (raw ?? "").Trim();
		if (raw.StartsWith("<")) {
			var gt = raw.IndexOf('>');
			if (gt > 0) return raw.Substring(1, gt - 1).Trim();
		}
		if (raw.Length > 2 && (raw.EndsWith("\"") || raw.EndsWith("'"))) {
			var q = raw[raw.Length - 1];
			var a = raw.LastIndexOf(q, raw.Length - 2);
			if (a > 0 && raw[a - 1] == ' ') raw = raw.Substring(0, a).TrimEnd();
		}
		return raw.Trim();
	}

	static bool tryangle(string text, int i, out string url, out int next) {
		url = "";
		next = i;
		var j = text.IndexOf('>', i + 1);
		if (j < 0) return false;
		var inner = text.Substring(i + 1, j - i - 1).Trim();
		if (!inner.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
			&& !inner.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
			&& !inner.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
			return false;
		url = inner;
		next = j + 1;
		return true;
	}

	static bool tryurl(string text, int i, out string url, out int next) {
		url = "";
		next = i;
		if (!at(text, i, "http://") && !at(text, i, "https://")) return false;
		var j = i;
		while (j < text.Length && !char.IsWhiteSpace(text[j]) && text[j] != '<' && text[j] != '"' && text[j] != ')')
			j++;
		while (j > i && (text[j - 1] == '.' || text[j - 1] == ',' || text[j - 1] == ';' || text[j - 1] == ':' || text[j - 1] == '!'))
			j--;
		if (j <= i) return false;
		url = text.Substring(i, j - i);
		next = j;
		return true;
	}

	static bool trymark(string text, int i, string mark, out int end) {
		end = -1;
		if (i + mark.Length > text.Length) return false;
		if (!at(text, i, mark)) return false;
		if (mark[0] == '_' && i > 0 && (char.IsLetterOrDigit(text[i - 1]) || text[i - 1] == '_')) return false;
		var n = i + mark.Length;
		if (n >= text.Length || char.IsWhiteSpace(text[n])) return false;
		var j = text.IndexOf(mark, n, StringComparison.Ordinal);
		if (j <= n) return false;
		if (char.IsWhiteSpace(text[j - 1])) return false;
		if (mark[0] == '_' && j + mark.Length < text.Length && char.IsLetterOrDigit(text[j + mark.Length])) return false;
		end = j;
		return true;
	}

	static bool at(string text, int i, string s) {
		if (i < 0 || s.Length == 0 || i + s.Length > text.Length) return false;
		return string.CompareOrdinal(text, i, s, 0, s.Length) == 0;
	}

	/// <summary>
	/// 本地路径转 file:// 地址。IE 把 %E5 这类 UTF-8 转义当 Latin-1 解码（变成 å¾），
	/// 中文、空格等只能原样写进 URL，只转义会弄坏地址的 ASCII 符号。
	/// </summary>
	static string fileurl(string full) {
		if (string.IsNullOrEmpty(full)) return "";
		var p = full.Replace('\\', '/');
		var sb = new StringBuilder(p.Length + 16);
		sb.Append(p.StartsWith("//", StringComparison.Ordinal) ? "file:" : "file:///");
		foreach (var c in p) {
			if (c == '%' || c == '#' || c == '?' || c == '&' || c == '"' || c == '<' || c == '>' || c == '{' || c == '}')
				sb.Append('%').Append(((int)c).ToString("X2"));
			else if (c == ' ') sb.Append("%20");
			else sb.Append(c);
		}
		return sb.ToString();
	}

	static string safehref(string url, string baseDir) {
		url = (url ?? "").Trim();
		if (url.Length == 0) return "";
		if (url.StartsWith("#")) return url;
		if (url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) return "";
		if (url.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase)) return "";
		if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return "";
		if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
			|| url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
			|| url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
			|| url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
			return url;
		try {
			if (url.Length >= 3 && char.IsLetter(url[0]) && url[1] == ':')
				return fileurl(url);
			if (string.IsNullOrEmpty(baseDir)) return "";
			var full = Path.GetFullPath(Path.Combine(baseDir, url.Replace('/', Path.DirectorySeparatorChar)));
			return fileurl(full);
		}
		catch { return ""; }
	}
	#endregion
}
