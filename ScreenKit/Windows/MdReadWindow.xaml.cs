using System.Text;

namespace ScreenKit;

/// <summary>工具 → MD 阅读器。拖入 Markdown，用 Windows 10 的 IE11 WebBrowser 显示。</summary>
public partial class MdReadWindow : Window {
	System.Windows.Forms.WebBrowser browser;
	string curpath = "";
	string curmode = "";
	string page;
	readonly List<string> oldpages = new();

	public MdReadWindow() {
		MdRead.EnsureIe11();
		InitializeComponent();
		initbrowser();
		applylang();
		initev();
		WindowEsc.Attach(this);
	}

	void initbrowser() {
		browser = new System.Windows.Forms.WebBrowser {
			Dock = System.Windows.Forms.DockStyle.Fill,
			ScriptErrorsSuppressed = true,
			AllowWebBrowserDrop = false,
			AllowDrop = true,
		};
		browser.DragEnter += ondrag;
		browser.DragDrop += ondrop;
		browser.DocumentCompleted += ondoc;
		browser.Navigating += onnav;
		phost.Child = browser;
	}

	void initev() {
		bopen.Click += (_, _) => pick();
		bclose.Click += (_, _) => Close();
		PreviewDragOver += (_, e) => {
			e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
			e.Handled = true;
		};
		Drop += (_, e) => {
			if (e.Data.GetData(DataFormats.FileDrop) is string[] files) opendrop(files);
		};
		Loaded += (_, _) => showhtml(MdRead.EmptyHtml(Loc.T("mdread.hint")));
		Closed += (_, _) => {
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
		showstat();
	}

	void pick() {
		var ofd = new Microsoft.Win32.OpenFileDialog {
			Title = Loc.T("mdread.open"),
			Filter = "Markdown|*.md;*.markdown;*.mdown;*.mkd",
		};
		if (ofd.ShowDialog(this) != true) return;
		openpath(ofd.FileName);
	}

	void ondrag(object sender, System.Windows.Forms.DragEventArgs e) {
		var ok = e.Data.GetDataPresent(System.Windows.Forms.DataFormats.FileDrop);
		e.Effect = ok ? System.Windows.Forms.DragDropEffects.Copy : System.Windows.Forms.DragDropEffects.None;
	}

	void ondrop(object sender, System.Windows.Forms.DragEventArgs e) {
		opendrop(e.Data.GetData(System.Windows.Forms.DataFormats.FileDrop) as string[]);
	}

	void opendrop(string[] files) {
		if (files == null) return;
		foreach (var f in files) {
			if (MdRead.IsMd(f) && File.Exists(f)) {
				openpath(f);
				return;
			}
		}
		curpath = "";
		lbstat.Text = Loc.T("mdread.bad");
	}

	void openpath(string path) {
		string md;
		try { md = MdRead.ReadText(path); }
		catch (Exception ex) {
			lbstat.Text = ex.Message;
			return;
		}
		curpath = path;
		Title = Path.GetFileName(path) + " - " + Loc.T("mdread.title");
		var dir = Path.GetDirectoryName(path) ?? "";
		showhtml(MdRead.ToHtml(md, dir, Path.GetFileName(path)));
		showstat();
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
}
