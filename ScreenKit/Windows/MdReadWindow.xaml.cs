using System.Runtime.InteropServices;
using System.Text;

namespace ScreenKit;

/// <summary>工具 → MD 阅读器。拖入 Markdown，用 Windows 10 的 IE11 WebBrowser 显示。</summary>
public partial class MdReadWindow : Window {
	System.Windows.Forms.WebBrowser browser;
	MdDrop drop;
	IntPtr drophwnd;
	OcrOptions opt;
	MdRead.MdTheme theme;
	string curpath = "";
	string curmode = "";
	string curmd;
	string page;
	bool hasmd;
	bool ready;
	double dpi = 1;
	readonly List<string> oldpages = new();

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
		bclose.Click += (_, _) => Close();
		bword.Click += (_, _) => exportword();
		bpdf.Click += (_, _) => exportpdf();
		PreviewDragOver += (_, e) => {
			e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
			e.Handled = true;
		};
		Drop += (_, e) => {
			if (e.Data.GetData(DataFormats.FileDrop) is string[] files) opendrop(files);
		};
		Loaded += (_, _) => {
			dpi = uiscale();
			applytheme();
		};
		DpiChanged += (_, _) => {
			var s = uiscale();
			if (Math.Abs(s - dpi) < 0.01) return;
			dpi = s;
			render();
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
			foreach (var p in oldpages) {
				try { if (File.Exists(p)) File.Delete(p); } catch { }
			}
		};
	}

	void applylang() {
		Title = Loc.T("mdread.title");
		ToolBtnUi.Set(bopen, ToolBtnUi.Folder, Loc.T("mdread.open"));
		ToolBtnUi.Set(bclose, ToolBtnUi.Close, Loc.T("imgconv.close"));
		ToolBtnUi.Set(bword, ToolBtnUi.Save, Loc.T("mdread.word"));
		ToolBtnUi.Set(bpdf, ToolBtnUi.Save, Loc.T("mdread.pdf"));
		bword.IsEnabled = hasmd;
		bpdf.IsEnabled = hasmd;
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
		etheme.ToolTip = Loc.T("mdread.theme");
		ready = true;
	}

	void applytheme() {
		try { phostbox.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme.Bg)); }
		catch { }
		render();
	}

	void render() {
		if (hasmd)
			showhtml(MdRead.ToHtml(curmd ?? "", Path.GetDirectoryName(curpath) ?? "", Path.GetFileName(curpath), dpi, theme.Id));
		else
			showhtml(MdRead.EmptyHtml(Loc.T("mdread.hint"), dpi, theme.Id));
	}

	void pick() {
		var ofd = new Microsoft.Win32.OpenFileDialog {
			Title = Loc.T("mdread.open"),
			Filter = "Markdown|*.md;*.markdown;*.mdown;*.mkd",
		};
		if (ofd.ShowDialog(this) != true) return;
		openpath(ofd.FileName);
	}

	void opendrop(string[] files) {
		if (files == null) return;
		foreach (var f in files) {
			var p = MdRead.Resolve(f);
			if (MdRead.IsMd(p) && File.Exists(p)) {
				openpath(p);
				return;
			}
		}
		curpath = "";
		lbstat.Text = Loc.T("mdread.bad");
	}

	void openpath(string path) {
		path = MdRead.Resolve(path);
		string md;
		try { md = MdRead.ReadText(path); }
		catch (Exception ex) {
			lbstat.Text = ex.Message;
			return;
		}
		curpath = path;
		curmd = md;
		hasmd = true;
		bword.IsEnabled = true;
		bpdf.IsEnabled = true;
		Title = Path.GetFileName(path) + " - " + Loc.T("mdread.title");
		var dir = Path.GetDirectoryName(path) ?? "";
		showhtml(MdRead.ToHtml(md, dir, Path.GetFileName(path), dpi, theme.Id));
		showstat();
	}

	void exportword() => export(true);

	void exportpdf() => export(false);

	void export(bool word) {
		if (!hasmd || string.IsNullOrEmpty(page) || !File.Exists(page)) return;
		var ofd = new Microsoft.Win32.SaveFileDialog {
			Title = Loc.T(word ? "mdread.word" : "mdread.pdf"),
			Filter = word ? "Word|*.docx" : "PDF|*.pdf",
			FileName = Path.GetFileNameWithoutExtension(curpath) + (word ? ".docx" : ".pdf"),
			DefaultExt = word ? ".docx" : ".pdf",
			AddExtension = true,
			OverwritePrompt = true,
		};
		try {
			var dir = Path.GetDirectoryName(curpath);
			if (!string.IsNullOrEmpty(dir)) ofd.InitialDirectory = dir;
		}
		catch { }
		if (ofd.ShowDialog(this) != true) return;
		var dest = ofd.FileName;
		lbstat.Text = Loc.T("mdread.exporting");
		lbstat.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
		Mouse.OverrideCursor = Cursors.Wait;
		try {
			var html = MdExport.PrintHtml(File.ReadAllText(page));
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

	void showhtml(string html) {
		var dir = Path.Combine(Path.GetTempPath(), "ScreenKit");
		Directory.CreateDirectory(dir);
		var next = Path.Combine(dir, "mdread-" + Environment.TickCount.ToString("x") + ".html");
		File.WriteAllText(next, html, new UTF8Encoding(true));
		if (!string.IsNullOrEmpty(page)) oldpages.Add(page);
		page = next;
		browser.Navigate(next);
	}

	void ondoc(object sender, System.Windows.Forms.WebBrowserDocumentCompletedEventArgs e) {
		if (e.Url == null || !e.Url.IsFile) return;
		try {
			if (!string.IsNullOrEmpty(page)) {
				var cur = Path.GetFullPath(page);
				var got = Path.GetFullPath(e.Url.LocalPath);
				if (!string.Equals(cur, got, StringComparison.OrdinalIgnoreCase)) return;
			}
		}
		catch { }
		try {
			dynamic dom = browser.Document.DomDocument;
			curmode = Convert.ToString(dom.documentMode);
		}
		catch { curmode = ""; }
		showstat();
		trimpages();
		hookdrop();
	}

	void onnav(object sender, System.Windows.Forms.WebBrowserNavigatingEventArgs e) {
		var u = e.Url;
		if (u == null) return;
		if (u.IsFile) {
			var p = u.LocalPath;
			if (MdRead.IsMd(p) && File.Exists(p)) {
				e.Cancel = true;
				openpath(p);
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

	void showstat() {
		var name = string.IsNullOrEmpty(curpath) ? Loc.T("mdread.hint") : curpath;
		if (!string.IsNullOrEmpty(curmode))
			lbstat.Text = name + "  ·  " + Loc.T("mdread.mode", curmode);
		else
			lbstat.Text = name;
	}

	void trimpages() {
		foreach (var p in oldpages.ToList()) {
			try { if (File.Exists(p)) File.Delete(p); } catch { }
		}
		oldpages.Clear();
		if (!string.IsNullOrEmpty(page)) oldpages.Add(page);
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
