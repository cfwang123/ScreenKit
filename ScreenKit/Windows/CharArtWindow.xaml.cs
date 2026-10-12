using System.IO;
using System.Windows;

namespace ScreenKit;

/// <summary>工具 → 字符画。单色 ASCII 在本窗口，全色块另开命令行。</summary>
public partial class CharArtWindow : Window {
	string path;
	byte[] px;
	int w;
	int h;

	public CharArtWindow() {
		InitializeComponent();
		applylang();
		initev();
		WindowEsc.Attach(this);
	}

	void initev() {
		bpick.Click += (_, _) => pick();
		bcopy.Click += (_, _) => copy();
		bcolor.Click += (_, _) => color();
		ecols.TextChanged += (_, _) => showascii();
		ckinv.Checked += (_, _) => showascii();
		ckinv.Unchecked += (_, _) => showascii();
		PreviewDragOver += ondrag;
		Drop += ondrop;
	}

	void applylang() {
		Title = Loc.T("charart.title");
		lbcols.Text = Loc.T("charart.cols");
		ckinv.Content = Loc.T("charart.invert");
		lbhint.Text = Loc.T("charart.hint");
		ToolBtnUi.Set(bpick, ToolBtnUi.Browse, Loc.T("charart.pick"));
		ToolBtnUi.Set(bcopy, ToolBtnUi.Copy, Loc.T("charart.copy"));
		ToolBtnUi.Set(bcolor, ToolBtnUi.Font, Loc.T("charart.color"));
		if (px == null) lbstat.Text = Loc.T("charart.empty");
	}

	void pick() {
		var ofd = new Microsoft.Win32.OpenFileDialog {
			Title = Loc.T("charart.pick"),
			Filter = Loc.T("charart.filter"),
		};
		if (ofd.ShowDialog(this) != true) return;
		load(ofd.FileName);
	}

	void ondrag(object sender, DragEventArgs e) {
		e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}

	void ondrop(object sender, DragEventArgs e) {
		var file = firstimage(e.Data.GetData(DataFormats.FileDrop));
		if (file != null) load(file);
	}

	static string firstimage(object data) {
		if (data is string one) return imagefile(one);
		if (data is string[] many) {
			foreach (var p in many) {
				var hit = imagefile(p);
				if (hit != null) return hit;
			}
		}
		return null;
	}

	static string imagefile(string p) {
		if (string.IsNullOrWhiteSpace(p)) return null;
		if (File.Exists(p) && ImageUtil.IsImagePath(p)) return p;
		if (!Directory.Exists(p)) return null;
		try {
			foreach (var f in Directory.GetFiles(p))
				if (ImageUtil.IsImagePath(f)) return f;
		}
		catch { }
		return null;
	}

	void load(string file) {
		try {
			var src = ImageUtil.Fromfile(file);
			px = CharArt.Bgra(src, out w, out h);
			path = file;
			showascii();
		}
		catch (Exception ex) {
			px = null;
			path = null;
			eout.Text = "";
			lbstat.Text = Loc.T("charart.bad") + " " + ex.Message;
		}
	}

	void showascii() {
		if (px == null) return;
		var cols = 100;
		int.TryParse((ecols.Text ?? "").Trim(), out cols);
		if (cols < 1) cols = 1;
		if (cols > 400) cols = 400;
		eout.Text = CharArt.Ascii(px, w, h, cols, ckinv.IsChecked == true);
		var lines = 1;
		foreach (var ch in eout.Text)
			if (ch == '\n') lines++;
		lbstat.Text = Loc.T("charart.stat", w, h, cols, lines);
	}

	void copy() {
		var text = eout.Text ?? "";
		if (text.Length == 0) return;
		try { Clipboard.SetText(text); }
		catch (Exception ex) { lbstat.Text = ex.Message; }
	}

	void color() {
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
			lbstat.Text = Loc.T("charart.empty");
			return;
		}
		try {
			CharArt.Open(path);
			lbstat.Text = Loc.T("charart.opened");
		}
		catch (Exception ex) {
			lbstat.Text = ex.Message;
		}
	}
}
