using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace ScreenKit;

/// <summary>工具 → 二维码 / 条码生成。</summary>
public partial class QrMakeWindow : Window {
	BitmapSource last;
	string lastText = "";
	int seq;
	CancellationTokenSource cts;

	public QrMakeWindow() {
		InitializeComponent();
		foreach (var f in QrMake.Formats) {
			efmt.Items.Add(new ComboBoxItem { Content = f.Title, Tag = f.Id });
		}
		efmt.SelectedIndex = 0;
		applylang();
		initev();
		WindowEsc.Attach(this);
		queue();
	}

	void initev() {
		etext.TextChanged += (_, _) => queue();
		efmt.SelectionChanged += (_, _) => queue();
		rutf8.Checked += (_, _) => queue();
		rgbk.Checked += (_, _) => queue();
		rhex.Checked += (_, _) => queue();
		bcopy.Click += (_, _) => copy();
		bsave.Click += (_, _) => save();
		bclose.Click += (_, _) => Close();
	}

	void applylang() {
		Title = Loc.T("qrmake.title");
		lbfmt.Text = Loc.T("qrmake.fmt");
		lbenc.Text = Loc.T("qrmake.enc");
		rutf8.Content = Loc.T("qrmake.utf8");
		rgbk.Content = Loc.T("qrmake.gbk");
		rhex.Content = Loc.T("qrmake.hex");
		rhex.ToolTip = Loc.T("qrmake.hex.tip");
		lbtext.Text = Loc.T("qrmake.text");
		lbempty.Text = Loc.T("qrmake.empty");
		ToolBtnUi.Set(bcopy, ToolBtnUi.Copy, Loc.T("qrmake.copy"));
		ToolBtnUi.Set(bsave, ToolBtnUi.Save, Loc.T("qrmake.save"));
		ToolBtnUi.Set(bclose, ToolBtnUi.Close, Loc.T("imgconv.close"));
	}

	string fmtid() => (efmt.SelectedItem as ComboBoxItem)?.Tag as string ?? "qr";
	string encid() {
		if (rhex.IsChecked == true) return "hex";
		if (rgbk.IsChecked == true) return "gbk";
		return "utf8";
	}

	void queue() {
		seq++;
		var n = seq;
		try { cts?.Cancel(); } catch { }
		cts = new CancellationTokenSource();
		var token = cts.Token;
		_ = run(n, etext.Text ?? "", fmtid(), encid(), token);
	}

	async Task run(int n, string text, string fmt, string enc, CancellationToken token) {
		text = text ?? "";
		if (text.Trim().Length == 0) {
			last = null;
			lastText = "";
			iprev.Source = null;
			lbcaption.Text = "";
			pprev.Visibility = Visibility.Collapsed;
			lbempty.Visibility = Visibility.Visible;
			lbstat.Text = "";
			return;
		}
		try {
			await Task.Delay(120, token).ConfigureAwait(true);
			if (n != seq) return;
			var bmp = await Task.Run(() => QrMake.Encode(text, fmt, enc, 8, false), token).ConfigureAwait(true);
			if (n != seq) return;
			last = bmp;
			lastText = text.Replace("\r", " ").Replace("\n", " ");
			iprev.Source = bmp;
			lbcaption.Text = lastText;
			pprev.Visibility = Visibility.Visible;
			lbempty.Visibility = Visibility.Collapsed;
			lbstat.Text = $"{bmp.PixelWidth} × {bmp.PixelHeight}  ·  {fmt}  ·  {enc}";
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) {
			if (n != seq) return;
			last = null;
			iprev.Source = null;
			lbcaption.Text = "";
			pprev.Visibility = Visibility.Collapsed;
			lbempty.Visibility = Visibility.Visible;
			lbstat.Text = Loc.T("qrmake.fail", ex.Message);
		}
	}

	BitmapSource exportimg() {
		if (last == null) return null;
		return QrMake.Encode(etext.Text ?? "", fmtid(), encid(), 8, true);
	}

	void copy() {
		try {
			var bmp = exportimg();
			if (bmp == null) return;
			Clipboard.SetImage(bmp);
			lbstat.Text = Loc.T("qrmake.copied");
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	void save() {
		if (last == null) return;
		var sfd = new Microsoft.Win32.SaveFileDialog {
			Title = Loc.T("qrmake.save"),
			Filter = "PNG|*.png",
			FileName = "barcode.png",
			DefaultExt = ".png",
			AddExtension = true,
		};
		if (sfd.ShowDialog(this) != true) return;
		try {
			ImageUtil.Savefile(exportimg(), sfd.FileName);
			lbstat.Text = Loc.T("qrmake.saved");
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}
}
