using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace ScreenKit;

/// <summary>
/// 工具 → 文档阅读器。标题栏自绘：左边菜单、中间 tab 芯片、右边窗口按钮。
/// 一次可拖入多个文件，每个文件一个 tab：Markdown 按阅读器规则排版，
/// docx/pdf 由 <see cref="DocViewHtml"/> 转成 HTML 后交给 IE11 内核显示。
/// </summary>
public partial class MdReadWindow : Window {
	/// <summary>缓存目录里保留的页面上限，超出就把最旧的那批删掉。</summary>
	const int MAXCACHE = 60;
	const string CACHEPREFIX = "mdread-";

	/// <summary>一个 tab 的文档状态。Html 是算好待写的页面，切到它时才真正写盘。</summary>
	sealed class Tab {
		public string Path = "";
		public string Name = "";
		public string Kind = "md";
		public string Md = "";
		public string Html = "";
		public string Page = "";
		public string Mode = "";
		public string ImageDir = "";
		public int Pages;
		public Border Head;
		public TextBlock Label;
	}

	System.Windows.Forms.WebBrowser browser;
	MdDrop drop;
	IntPtr drophwnd;
	OcrOptions opt;
	MdRead.MdTheme theme;
	Tab cur;
	bool ready;
	double dpi = 1;
	readonly List<Tab> tabs = new();
	/// <summary>写盘过的页面，关窗时统一删。</summary>
	readonly List<string> pages = new();
	readonly List<string> imgdirs = new();
	/// <summary>拖窗时点在标题栏空白处，先记下起点，超过阈值才真的拖。</summary>
	int titletick;
	Point titlepoint;

	public MdReadWindow(OcrOptions o) {
		opt = o ?? new OcrOptions();
		theme = MdRead.ThemeOf(opt.MdReadTheme);
		MdRead.EnsureIe11();
		InitializeComponent();
		dpi = uiscale();
		initbrowser();
		applylang();
		initetheme();
		initev();
		WindowEsc.Attach(this);
	}

	/// <summary>IE 控件按物理像素排版，窗口 DPI 是 125% 时正文只有四分之三大小，这里取比例补上。</summary>
	double uiscale() {
		try {
			var s = VisualTreeHelper.GetDpi(this).DpiScaleX;
			if (s >= 1 && s <= 4) return s;
		}
		catch { }
		return 1;
	}

	void initbrowser() {
		browser = new System.Windows.Forms.WebBrowser {
			Dock = System.Windows.Forms.DockStyle.Fill,
			ScriptErrorsSuppressed = true,
			AllowWebBrowserDrop = false,
		};
		drop = new MdDrop(opendrop);
		browser.HandleCreated += (_, _) => hookdrop();
		browser.DocumentCompleted += ondoc;
		browser.Navigating += onnav;
		phost.Child = browser;
	}

	void hookdrop() {
		if (browser == null || !browser.IsHandleCreated) return;
		var h = docwnd(browser.Handle);
		if (h == IntPtr.Zero) h = browser.Handle;
		if (h == drophwnd) return;
		if (drophwnd != IntPtr.Zero && drophwnd != h) {
			try { RevokeDragDrop(drophwnd); } catch { }
			drophwnd = IntPtr.Zero;
		}
		try { RevokeDragDrop(h); } catch { }
		if (RegisterDragDrop(h, drop) != 0) return;
		drophwnd = h;
	}

	void initev() {
		bopen.Click += (_, _) => pick();
		bword.Click += (_, _) => export(true);
		bpdf.Click += (_, _) => export(false);
		bmin.Click += (_, _) => WindowState = WindowState.Minimized;
		bmax.Click += (_, _) => togglemax();
		bclosewin.Click += (_, _) => Close();
		mnopen.Click += (_, _) => pick();
		mnclose.Click += (_, _) => closetab(cur);
		mncloseall.Click += (_, _) => closeall();
		mnword.Click += (_, _) => export(true);
		mnpdf.Click += (_, _) => export(false);
		mnreload.Click += (_, _) => rerender();
		mnpath.Click += (_, _) => copytext(cur?.Path);
		mnsysopen.Click += (_, _) => sysopen(cur?.Path);
		PreviewDragOver += (_, e) => {
			e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
			e.Handled = true;
		};
		Drop += (_, e) => {
			if (e.Data.GetData(DataFormats.FileDrop) is string[] files) opendrop(files);
		};
		StateChanged += (_, _) => {
			if (bmax != null) {
				var maxed = WindowState == WindowState.Maximized;
				bmax.Content = maxed ? "❐" : "□";
				bmax.ToolTip = maxed ? "还原" : "最大化";
			}
			try {
				var chrome = WindowChrome.GetWindowChrome(this);
				if (chrome != null)
					chrome.ResizeBorderThickness = WindowState == WindowState.Maximized
						? new Thickness(0) : new Thickness(8, 6, 8, 6);
				proot.BorderThickness = WindowState == WindowState.Maximized ? new Thickness(0) : new Thickness(1);
			}
			catch { }
		};
		Loaded += (_, _) => {
			dpi = uiscale();
			if (tabs.Count == 0) renderempty();
		};
		DpiChanged += (_, _) => {
			var s = uiscale();
			if (Math.Abs(s - dpi) < 0.01) return;
			dpi = s;
			rerender();
		};
		etheme.SelectionChanged += (_, _) => {
			if (!ready) return;
			var id = (etheme.SelectedItem as ComboBoxItem)?.Tag as string;
			if (string.Equals(id, theme.Id, StringComparison.Ordinal)) return;
			theme = MdRead.ThemeOf(id);
			opt.MdReadTheme = theme.Id;
			try { AppConfig.Save(opt); } catch { }
			applytheme();
		};
		Closed += (_, _) => {
			if (drophwnd != IntPtr.Zero) {
				try { RevokeDragDrop(drophwnd); } catch { }
				drophwnd = IntPtr.Zero;
			}
			try { browser?.Dispose(); } catch { }
			foreach (var p in pages) {
				try { if (File.Exists(p)) File.Delete(p); } catch { }
			}
			foreach (var d in imgdirs) {
				try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
			}
		};
	}

	void applylang() {
		Title = Loc.T("mdread.title");
		mnhamburger.ToolTip = Loc.T("mdread.menu");
		mnopen.Header = Loc.T("mdread.open");
		mnclose.Header = Loc.T("mdread.tabclose");
		mncloseall.Header = Loc.T("mdread.closeall");
		mnword.Header = Loc.T("mdread.word");
		mnpdf.Header = Loc.T("mdread.pdf");
		mnreload.Header = Loc.T("mdread.reload");
		mnpath.Header = Loc.T("mdread.copypath");
		mnsysopen.Header = Loc.T("mdread.sysopen");
		bopen.Content = Loc.T("mdread.open");
		bword.Content = Loc.T("mdread.word");
		bpdf.Content = Loc.T("mdread.pdf");
		lbtheme.Text = Loc.T("mdread.theme");
		etheme.ToolTip = Loc.T("mdread.theme");
		showstat();
	}

	/// <summary>主题下拉框按 MdRead.Themes 的顺序填。</summary>
	void initetheme() {
		etheme.Items.Clear();
		foreach (var t in MdRead.Themes) {
			var it = new ComboBoxItem { Content = Loc.T("mdread.theme." + t.Id), Tag = t.Id };
			etheme.Items.Add(it);
			if (t.Id == theme.Id) etheme.SelectedItem = it;
		}
		ready = true;
	}

	void applytheme() {
		try { phostbox.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme.Bg)); }
		catch { }
		rerender();
	}

	/// <summary>主题或 DPI 变了：Markdown 重新排版，docx/pdf 的页面不动，只导航一次。</summary>
	void rerender() {
		if (cur == null) {
			renderempty();
			return;
		}
		if (cur.Kind == "md" && cur.Md != null) {
			cur.Html = MdRead.ToHtml(cur.Md, Path.GetDirectoryName(cur.Path) ?? "", cur.Name, dpi, theme.Id);
			cur.Page = "";
		}
		showpage(cur);
	}

	void renderempty() => showhtml(MdRead.EmptyHtml(Loc.T("mdread.hint"), dpi, theme.Id));

	void pick() {
		var ofd = new Microsoft.Win32.OpenFileDialog {
			Title = Loc.T("mdread.open"),
			Filter = Loc.T("mdread.filter"),
			Multiselect = true,
		};
		if (ofd.ShowDialog(this) != true) return;
		opendrop(ofd.FileNames);
	}

	void opendrop(string[] files) {
		if (files == null || files.Length == 0) return;
		var opened = 0;
		var bad = 0;
		foreach (var f in files) {
			if (openone(MdRead.Resolve(f), true)) opened++;
			else bad++;
		}
		if (opened == 0) {
			lbstat.Text = Loc.T("mdread.bad");
			return;
		}
		updatetabs();
		// 一次拖入多个：最后进来的那个留在前台，前面的都在 tab 里。
		activatetab(tabs[tabs.Count - 1]);
		if (bad > 0) lbstat.Text = string.Format(Loc.T("mdread.bad.part"), opened, bad);
	}

	/// <summary>同一个文件已经开着就直接切过去，不重复解析。</summary>
	bool openone(string path, bool quiet) {
		if (!File.Exists(path)) return false;
		var kind = kindof(path);
		if (kind.Length == 0) return false;
		foreach (var t in tabs) {
			if (string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase)) {
				activatetab(t);
				return true;
			}
		}
		Tab tab = new() { Path = path, Name = Path.GetFileName(path), Kind = kind };
		try {
			if (kind == "md") {
				tab.Md = MdRead.ReadText(path);
				tab.Html = MdRead.ToHtml(tab.Md, Path.GetDirectoryName(path) ?? "", tab.Name, dpi, theme.Id);
			}
			else {
				if (tab.Kind == "pdf" && !NativeRuntime.HasPdfium()) {
					if (!FeaturePrompt.EnsureKinds(this, Loc.T("feat.prompt.pdf.title"),
						Loc.T("feat.prompt.pdf.detail"), FeatureKind.NativePdfium))
						return false;
				}
				var dir = Path.Combine(cachedir(), "img" + Environment.TickCount.ToString("x")
					+ "_" + Guid.NewGuid().ToString("N").Substring(0, 6));
				tab.ImageDir = dir;
				imgdirs.Add(dir);
				var res = DocViewHtml.Convert(path, dir);
				tab.Html = res.Html;
				tab.Pages = res.Pages;
			}
		}
		catch (Exception ex) {
			if (!quiet) lbstat.Text = Loc.T("mdread." + keyof(ex));
			cleanup(tab);
			return false;
		}
		tabs.Add(tab);
		return true;
	}

	static string kindof(string path) {
		if (MdRead.IsMd(path)) return "md";
		if (DocViewHtml.IsDoc(path)) return Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
		return "";
	}

	/// <summary>转换抛出的异常消息本身就是文案键，认不出来就退回通用失败提示。</summary>
	static string keyof(Exception ex) {
		var m = ex?.Message ?? "";
		if (m is "docview.baddocx" or "docview.badpdf") return m.Substring(8);
		if (m == "docview.errtype") return "bad";
		return "empty";
	}

	void activatetab(Tab t) {
		if (t == null) return;
		cur = t;
		Title = t.Name + " - " + Loc.T("mdread.title");
		showtabstate();
		showpage(t);
	}

	/// <summary>懒写盘：算好的 HTML 只在真正显示时才落文件，切 tab 切回来不重复写。</summary>
	void showpage(Tab t) {
		if (t == null) return;
		if (string.IsNullOrEmpty(t.Page)) t.Page = writeshell(t.Html);
		if (!string.IsNullOrEmpty(t.Page)) browser.Navigate(t.Page);
		showstat();
	}

	void showhtml(string html) {
		var page = writeshell(html);
		if (!string.IsNullOrEmpty(page)) browser.Navigate(page);
	}

	string writeshell(string html) {
		var dir = cachedir();
		guardcache();
		var next = Path.Combine(dir, CACHEPREFIX + Environment.TickCount.ToString("x")
			+ "_" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".html");
		try {
			File.WriteAllText(next, html ?? "", new UTF8Encoding(true));
			pages.Add(next);
			return next;
		}
		catch { return ""; }
	}

	void ondoc(object sender, System.Windows.Forms.WebBrowserDocumentCompletedEventArgs e) {
		if (e.Url != null && e.Url.IsFile) {
			try {
				dynamic dom = browser.Document.DomDocument;
				if (cur != null) cur.Mode = Convert.ToString(dom.documentMode);
			}
			catch { }
		}
		showstat();
		hookdrop();
	}

	void onnav(object sender, System.Windows.Forms.WebBrowserNavigatingEventArgs e) {
		var u = e.Url;
		if (u == null) return;
		if (u.IsFile) {
			var p = u.LocalPath;
			if (openone(p, true)) {
				e.Cancel = true;
				updatetabs();
			}
			return;
		}
		if (u.Scheme == "http" || u.Scheme == "https" || u.Scheme == "mailto") {
			e.Cancel = true;
			try {
				System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
			}
			catch { }
		}
	}

	// ==================== 标题栏 tab 芯片 ====================
	#region
	void updatetabs() {
		tabhost.Children.Clear();
		foreach (var t in tabs) tabhost.Children.Add(maketab(t));
		showstat();
	}

	/// <summary>一个芯片：文件名 + 关闭叉。标题栏属于非客户区，芯片要单独标可命中才能点。</summary>
	Border maketab(Tab t) {
		var title = t.Name.Length > 26 ? t.Name.Substring(0, 24) + "…" : t.Name;
		var label = new TextBlock {
			Text = title,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0, 0, 4, 0),
			MaxWidth = 168,
			FontSize = 12,
			TextTrimming = TextTrimming.CharacterEllipsis,
			Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x3A, 0x44)),
		};
		t.Label = label;
		var bclose = new Button {
			Content = "✕",
			Style = (Style)FindResource("CloseTabBtn"),
			ToolTip = Loc.T("mdread.tabclose"),
			Tag = t,
			VerticalAlignment = VerticalAlignment.Center,
		};
		bclose.Click += (_, e) => {
			e.Handled = true;
			closetab(t);
		};
		var panel = new StackPanel {
			Orientation = Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center,
		};
		panel.Children.Add(label);
		panel.Children.Add(bclose);
		var bd = new Border {
			Child = panel,
			Tag = t,
			Style = (Style)FindResource("TitleTab"),
			ToolTip = t.Path,
		};
		WindowChrome.SetIsHitTestVisibleInChrome(bd, true);
		bd.PreviewMouseLeftButtonDown += (_, e) => {
			if (e.ChangedButton != MouseButton.Left) return;
			// 点关闭叉不切 tab
			var d = e.OriginalSource as DependencyObject;
			while (d != null) {
				if (ReferenceEquals(d, bclose)) return;
				d = VisualTreeHelper.GetParent(d);
			}
			if (!ReferenceEquals(cur, t)) {
				activatetab(t);
				// 切 tab 后芯片样式变了，重新取当前的拖窗判断
				showstat();
			}
			e.Handled = true;
		};
		bd.MouseDown += (_, e) => {
			if (e.ChangedButton != MouseButton.Middle) return;
			closetab(t);
			e.Handled = true;
		};
		t.Head = bd;
		paintchip(t);
		return bd;
	}

	void showtabstate() {
		foreach (var t in tabs) paintchip(t);
	}

	void paintchip(Tab t) {
		if (t?.Head == null) return;
		var on = ReferenceEquals(t, cur);
		t.Head.Style = (Style)FindResource(on ? "TitleTabActive" : "TitleTab");
		if (t.Label != null)
			t.Label.Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27));
	}

	void closetab(Tab t) {
		if (t == null) return;
		var i = tabs.IndexOf(t);
		if (i < 0) return;
		tabs.RemoveAt(i);
		cleanup(t);
		updatetabs();
		if (tabs.Count == 0) {
			cur = null;
			Title = Loc.T("mdread.title");
			renderempty();
			showstat();
			return;
		}
		activatetab(tabs[Math.Min(i, tabs.Count - 1)]);
	}

	void closeall() {
		foreach (var t in tabs.ToList()) {
			cleanup(t);
		}
		tabs.Clear();
		cur = null;
		Title = Loc.T("mdread.title");
		updatetabs();
		renderempty();
		showstat();
	}

	void cleanup(Tab t) {
		if (t == null) return;
		if (!string.IsNullOrEmpty(t.Page)) {
			pages.Remove(t.Page);
			try { if (File.Exists(t.Page)) File.Delete(t.Page); } catch { }
		}
		if (!string.IsNullOrEmpty(t.ImageDir)) {
			imgdirs.Remove(t.ImageDir);
			try { if (Directory.Exists(t.ImageDir)) Directory.Delete(t.ImageDir, true); } catch { }
		}
	}
	#endregion

	// ==================== 窗口与菜单动作 ====================
	#region
	/// <summary>标题栏空白处拖动/双击最大化；点在按钮、菜单、芯片上时不动窗。</summary>
	void ontitledrag(object sender, MouseButtonEventArgs e) {
		if (e.ChangedButton != MouseButton.Left) return;
		if (istitleinteractive(e.OriginalSource as DependencyObject)) return;
		if (e.ClickCount == 2) {
			togglemax();
			titletick = 0;
			e.Handled = true;
			return;
		}
		var pos = e.GetPosition(this);
		if (titletick != 0 && Environment.TickCount - titletick < 400
			&& Math.Abs(pos.X - titlepoint.X) < 3 && Math.Abs(pos.Y - titlepoint.Y) < 3) {
			e.Handled = true;
			return;
		}
		titletick = Environment.TickCount;
		titlepoint = pos;
		try {
			if (Mouse.LeftButton != MouseButtonState.Pressed) return;
			if (WindowState == WindowState.Maximized) {
				// 最大化状态下拖出：先还原，再让窗口跟着鼠标
				var mouse = PointToScreen(e.GetPosition(this));
				var ratio = SystemParameters.PrimaryScreenWidth > 1
					? mouse.X / SystemParameters.PrimaryScreenWidth : 0.5;
				WindowState = WindowState.Normal;
				Left = mouse.X - ActualWidth * ratio;
				Top = Math.Max(0, mouse.Y - 16);
			}
			DragMove();
		}
		catch { }
	}

	static bool istitleinteractive(DependencyObject d) {
		while (d != null) {
			if (d is Menu || d is MenuItem || d is Button) return true;
			if (d is Border br && br.Tag is Tab) return true;
			if (d is ScrollViewer sv && sv.Name == "svtabs") return true;
			d = d is Visual || d is System.Windows.Media.Media3D.Visual3D
				? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
		}
		return false;
	}

	void togglemax() => WindowState = WindowState == WindowState.Maximized
		? WindowState.Normal : WindowState.Maximized;

	void copytext(string text) {
		if (string.IsNullOrEmpty(text)) return;
		try { Clipboard.SetText(text); lbstat.Text = Loc.T("mdread.copied"); }
		catch { }
	}

	void sysopen(string path) {
		if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
		try {
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
		}
		catch { }
	}
	#endregion

	// ==================== 导出（只对 Markdown 开放） ====================
	#region
	void export(bool word) {
		if (cur == null || cur.Kind != "md" || string.IsNullOrEmpty(cur.Page) || !File.Exists(cur.Page)) return;
		var ofd = new Microsoft.Win32.SaveFileDialog {
			Title = Loc.T(word ? "mdread.word" : "mdread.pdf"),
			Filter = word ? "Word|*.docx" : "PDF|*.pdf",
			FileName = Path.GetFileNameWithoutExtension(cur.Path) + (word ? ".docx" : ".pdf"),
			DefaultExt = word ? ".docx" : ".pdf",
			AddExtension = true,
			OverwritePrompt = true,
		};
		try {
			var dir = Path.GetDirectoryName(cur.Path);
			if (!string.IsNullOrEmpty(dir)) ofd.InitialDirectory = dir;
		}
		catch { }
		if (ofd.ShowDialog(this) != true) return;
		var dest = ofd.FileName;
		lbstat.Text = Loc.T("mdread.exporting");
		lbstat.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
		Mouse.OverrideCursor = Cursors.Wait;
		try {
			var html = MdExport.PrintHtml(File.ReadAllText(cur.Page));
			if (word) MdExport.ToWord(html, dest);
			else MdExport.ToPdf(html, dest);
			lbstat.Text = string.Format(Loc.T("mdread.exported"), dest);
		}
		catch (MdExportError ex) {
			lbstat.Text = Loc.T("mdread." + ex.Key);
		}
		catch (Exception ex) {
			lbstat.Text = string.Format(Loc.T("mdread.exportfail"), ex.Message);
		}
		finally {
			Mouse.OverrideCursor = null;
		}
	}
	#endregion

	// ==================== 缓存与状态 ====================
	#region
	static string cachedir() {
		var dir = Path.Combine(Path.GetTempPath(), "ScreenKit");
		Directory.CreateDirectory(dir);
		return dir;
	}

	/// <summary>缓存目录是各窗口共用的，只清自己这一类文件，别删别的窗口正在用的页面。</summary>
	static void guardcache() {
		try {
			var files = Directory.GetFiles(cachedir(), CACHEPREFIX + "*.html");
			if (files.Length <= MAXCACHE) return;
			Array.Sort(files, (a, b) => File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b)));
			for (var i = 0; i < files.Length - MAXCACHE; i++) {
				try { File.Delete(files[i]); } catch { }
			}
		}
		catch { }
	}

	void showstat() {
		mnclose.IsEnabled = cur != null;
		mncloseall.IsEnabled = tabs.Count > 0;
		var mdcur = cur != null && cur.Kind == "md";
		bword.IsEnabled = mdcur;
		bpdf.IsEnabled = mdcur;
		mnword.IsEnabled = mdcur;
		mnpdf.IsEnabled = mdcur;
		mnreload.IsEnabled = cur != null;
		mnpath.IsEnabled = cur != null;
		mnsysopen.IsEnabled = cur != null;
		if (cur == null) {
			lbstat.Text = Loc.T("mdread.hint");
			return;
		}
		List<string> parts = new() { cur.Path };
		parts.Add(cur.Kind);
		if (cur.Pages > 0) parts.Add(string.Format(Loc.T("mdread.pages"), cur.Pages));
		if (!string.IsNullOrEmpty(cur.Mode)) parts.Add(string.Format(Loc.T("mdread.mode"), cur.Mode));
		if (tabs.Count > 1) parts.Add(string.Format(Loc.T("mdread.tabs"), tabs.Count));
		lbstat.Text = string.Join("  ·  ", parts);
	}

	static IntPtr docwnd(IntPtr root) {
		IntPtr found = IntPtr.Zero;
		EnumChildWindows(root, (h, _) => {
			var name = new StringBuilder(64);
			GetClassName(h, name, name.Capacity);
			if (name.ToString() != "Internet Explorer_Server") return true;
			found = h;
			return false;
		}, IntPtr.Zero);
		return found;
	}

	delegate bool EnumProc(IntPtr hwnd, IntPtr lparam);

	[DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr hwnd, EnumProc proc, IntPtr lparam);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int len);
	[DllImport("ole32.dll")] static extern int RegisterDragDrop(IntPtr hwnd, IOleDropTarget target);
	[DllImport("ole32.dll")] static extern int RevokeDragDrop(IntPtr hwnd);
	#endregion
}

sealed class MdDrop : IOleDropTarget {
	readonly Action<string[]> open;
	bool ok;

	public MdDrop(Action<string[]> open) { this.open = open; }

	public int DragEnter(IntPtr data, uint key, PointL pt, ref uint effect) {
		ok = filesof(data) != null;
		effect = ok ? 1u : 0u;
		return 0;
	}

	public int DragOver(uint key, PointL pt, ref uint effect) {
		effect = ok ? 1u : 0u;
		return 0;
	}

	public int DragLeave() {
		ok = false;
		return 0;
	}

	public int Drop(IntPtr data, uint key, PointL pt, ref uint effect) {
		var files = filesof(data);
		effect = files != null ? 1u : 0u;
		if (files != null) {
			try { open(files); } catch { }
		}
		return 0;
	}

	static string[] filesof(IntPtr data) {
		if (data == IntPtr.Zero) return null;
		var obj = (IOleDataObject)Marshal.GetObjectForIUnknown(data);
		try {
			var fmt = new FormatEtc {
				cfFormat = 15,
				dwAspect = 1,
				lindex = -1,
				tymed = 1,
			};
			if (obj.GetData(ref fmt, out var med) != 0 || med.data == IntPtr.Zero) return null;
			try {
				uint n = DragQueryFile(med.data, 0xFFFFFFFF, null, 0);
				if (n == 0) return null;
				var list = new string[n];
				var sb = new StringBuilder(1024);
				for (uint i = 0; i < n; i++) {
					sb.Clear();
					if (DragQueryFile(med.data, i, sb, sb.Capacity) == 0) return null;
					list[i] = sb.ToString();
				}
				return list;
			}
			finally { ReleaseStgMedium(ref med); }
		}
		finally { Marshal.ReleaseComObject(obj); }
	}

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	static extern uint DragQueryFile(IntPtr drop, uint index, StringBuilder file, int len);
	[DllImport("ole32.dll")] static extern void ReleaseStgMedium(ref StgMedium medium);
}

[ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IOleDropTarget {
	[PreserveSig] int DragEnter(IntPtr data, uint key, PointL pt, ref uint effect);
	[PreserveSig] int DragOver(uint key, PointL pt, ref uint effect);
	[PreserveSig] int DragLeave();
	[PreserveSig] int Drop(IntPtr data, uint key, PointL pt, ref uint effect);
}

[ComImport, Guid("0000010E-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IOleDataObject {
	[PreserveSig] int GetData(ref FormatEtc format, out StgMedium medium);
}

[StructLayout(LayoutKind.Sequential)]
struct PointL {
	public int x;
	public int y;
}

[StructLayout(LayoutKind.Sequential)]
struct FormatEtc {
	public short cfFormat;
	public IntPtr ptd;
	public int dwAspect;
	public int lindex;
	public int tymed;
}

[StructLayout(LayoutKind.Sequential)]
struct StgMedium {
	public int tymed;
	public IntPtr data;
	public IntPtr unk;
}
