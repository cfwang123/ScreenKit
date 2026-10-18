using System.Collections;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

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
		eth.ValueChanged += (_, _) => {
			lbthv.Text = ((int)eth.Value).ToString();
			showascii();
		};
		esize.ValueChanged += (_, _) => {
			var n = (int)esize.Value;
			lbsizev.Text = n.ToString();
			eout.FontSize = n;
		};
		ckinv.Checked += (_, _) => showascii();
		ckinv.Unchecked += (_, _) => showascii();
		elineset.SelectionChanged += (_, _) => showascii();
		eout.AllowDrop = true;
		AddHandler(DragOverEvent, new DragEventHandler(ondrag), true);
		AddHandler(DropEvent, new DragEventHandler(ondrop), true);
		eout.AddHandler(DragOverEvent, new DragEventHandler(ondrag), true);
		eout.AddHandler(DropEvent, new DragEventHandler(ondrop), true);
	}

	void applylang() {
		Title = Loc.T("charart.title");
		lbcols.Text = Loc.T("charart.cols");
		lbth.Text = Loc.T("charart.threshold");
		eth.ToolTip = Loc.T("charart.threshold.tip");
		lbsize.Text = Loc.T("charart.size");
		esize.ToolTip = Loc.T("charart.size.tip");
		ckinv.Content = Loc.T("charart.invert");
		lbmode.Text = Loc.T("charart.mode");
		elineset.ToolTip = Loc.T("charart.mode.tip");
		fillmodes();
		lbhint.Text = Loc.T("charart.hint");
		ToolBtnUi.Set(bpick, ToolBtnUi.Browse, Loc.T("charart.pick"));
		ToolBtnUi.Set(bcopy, ToolBtnUi.Copy, Loc.T("charart.copy"));
		ToolBtnUi.Set(bcolor, ToolBtnUi.Font, Loc.T("charart.color"));
		if (px == null) lbstat.Text = Loc.T("charart.empty");
	}

	void fillmodes() {
		var keep = elineset.SelectedIndex;
		elineset.Items.Clear();
		elineset.Items.Add(new ComboBoxItem { Content = Loc.T("charart.mode.shade"), Tag = CharArt.LINES_SHADE });
		elineset.Items.Add(new ComboBoxItem { Content = Loc.T("charart.mode.dots"), Tag = CharArt.LINES_DOTS });
		elineset.Items.Add(new ComboBoxItem { Content = Loc.T("charart.mode.simple"), Tag = CharArt.LINES_SIMPLE });
		elineset.Items.Add(new ComboBoxItem { Content = Loc.T("charart.mode.ascii"), Tag = CharArt.LINES_ASCII });
		elineset.Items.Add(new ComboBoxItem { Content = Loc.T("charart.mode.box"), Tag = CharArt.LINES_BOX });
		elineset.Items.Add(new ComboBoxItem { Content = Loc.T("charart.mode.legacy"), Tag = CharArt.LINES_LEGACY });
		elineset.SelectedIndex = keep >= 0 && keep < elineset.Items.Count ? keep : 0;
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
		e.Effects = candrop(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}

	void ondrop(object sender, DragEventArgs e) {
		if (e.Handled) return;
		e.Handled = true;
		var file = firstpath(e.Data);
		if (file == null) {
			lbstat.Text = Loc.T("charart.bad");
			return;
		}
		load(file);
	}

	static bool candrop(IDataObject data) {
		if (data == null) return false;
		try {
			if (data.GetDataPresent(DataFormats.FileDrop)) return true;
			if (data.GetDataPresent(DataFormats.Bitmap)) return true;
			if (data.GetDataPresent("FileNameW") || data.GetDataPresent("FileName")) return true;
			if (data.GetDataPresent("FileGroupDescriptorW")) return true;
		}
		catch { }
		return false;
	}

	static string firstpath(IDataObject data) {
		if (data == null) return null;
		var list = new List<string>();
		try { addfiledrop(data, list); } catch { }
		if (list.Count == 0) try { addfilename(data, list); } catch { }
		if (list.Count == 0) try { addvirtual(data, list); } catch { }
		if (list.Count > 0) return list[0];
		try {
			if (!data.GetDataPresent(DataFormats.Bitmap)) return null;
			var raw = data.GetData(DataFormats.Bitmap);
			if (raw is BitmapSource bs) return ImgConvert.SaveTemp(bs);
			if (raw is System.Drawing.Bitmap db) {
				using (db) {
					var path = TmpStore.NewPath("charart", ".png");
					db.Save(path, System.Drawing.Imaging.ImageFormat.Png);
					return path;
				}
			}
		}
		catch { }
		return null;
	}

	static void addfiledrop(IDataObject data, List<string> list) {
		var raw = data.GetData(DataFormats.FileDrop);
		if (raw is string[] arr) list.AddRange(ImgConvert.CollectImages(arr));
		else if (raw is string one) list.AddRange(ImgConvert.CollectImages(new[] { one }));
		else if (raw is IEnumerable en) {
			var paths = new List<string>();
			foreach (var o in en) {
				if (o is string s && s.Length > 0) paths.Add(s);
			}
			if (paths.Count > 0) list.AddRange(ImgConvert.CollectImages(paths));
		}
	}

	static void addfilename(IDataObject data, List<string> list) {
		var name = data.GetData("FileNameW") as string;
		if (string.IsNullOrWhiteSpace(name)) name = data.GetData("FileName") as string;
		if (!string.IsNullOrWhiteSpace(name)) list.AddRange(ImgConvert.CollectImages(new[] { name }));
	}

	static void addvirtual(IDataObject data, List<string> list) {
		if (!data.GetDataPresent("FileGroupDescriptorW")) return;
		if (data.GetData("FileGroupDescriptorW") is not MemoryStream desc) return;
		var buf = desc.ToArray();
		if (buf.Length < 4) return;
		var count = BitConverter.ToInt32(buf, 0);
		const int descSize = 592;
		const int nameOff = 72;
		if (count < 1 || 4 + descSize > buf.Length) return;
		var name = Encoding.Unicode.GetString(buf, 4 + nameOff, 520).TrimEnd('\0');
		var ext = Path.GetExtension(name);
		if (string.IsNullOrEmpty(ext) || !ImageUtil.IsImagePath("x" + ext)) return;
		if (data.GetData("FileContents") is not Stream content) return;
		var path = TmpStore.NewPath("charart", ext);
		using (var fs = File.Create(path)) content.CopyTo(fs);
		if (new FileInfo(path).Length > 0) list.Add(path);
		else try { File.Delete(path); } catch { }
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
		var cols = 250;
		int.TryParse((ecols.Text ?? "").Trim(), out cols);
		if (cols < 1) cols = 1;
		if (cols > 400) cols = 400;
		var threshold = (int)eth.Value;
		if (threshold < 0) threshold = 0;
		if (threshold > 255) threshold = 255;
		var mode = elineset.SelectedItem is ComboBoxItem it && it.Tag is int k ? k : CharArt.LINES_SHADE;
		eth.ToolTip = Loc.T(mode >= CharArt.LINES_SIMPLE ? "charart.line.th" : "charart.threshold.tip");
		eout.Text = mode switch {
			CharArt.LINES_DOTS => CharArt.ShadeDots(px, w, h, cols, ckinv.IsChecked == true, threshold),
			CharArt.LINES_SHADE => CharArt.Ascii(px, w, h, cols, ckinv.IsChecked == true, threshold),
			_ => CharArt.Lines(px, w, h, cols, ckinv.IsChecked == true, threshold, mode),
		};
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
