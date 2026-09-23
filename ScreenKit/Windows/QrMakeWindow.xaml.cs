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
		lbtext.Text = Loc.T("qrmake.text");
		lbempty.Text = Loc.T("qrmake.empty");
		bcopy.Content = Loc.T("qrmake.copy");
		bsave.Content = Loc.T("qrmake.save");
		bclose.Content = Loc.T("imgconv.close");
	}

	string fmtid() => (efmt.SelectedItem as ComboBoxItem)?.Tag as string ?? "qr";
	string encid() => rgbk.IsChecked == true ? "gbk" : "utf8";

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
			lbempty.Visibility = Visibility.Visible;
			lbstat.Text = "";
			return;
		}
		try {
			await Task.Delay(120, token).ConfigureAwait(true);
			if (n != seq) return;
			var bmp = await Task.Run(() => QrMake.Encode(text, fmt, enc, 8, true), token).ConfigureAwait(true);
			if (n != seq) return;
			last = bmp;
			lastText = text;
			iprev.Source = bmp;
			lbempty.Visibility = Visibility.Collapsed;
			lbstat.Text = $"{bmp.PixelWidth} × {bmp.PixelHeight}  ·  {fmt}  ·  {enc}";
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) {
			if (n != seq) return;
			last = null;
			iprev.Source = null;
			lbempty.Visibility = Visibility.Visible;
			lbstat.Text = Loc.T("qrmake.fail", ex.Message);
		}
	}

	void copy() {
		if (last == null) return;
		try {
			Clipboard.SetImage(last);
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
			ImageUtil.Savefile(last, sfd.FileName);
			lbstat.Text = Loc.T("qrmake.saved");
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}
}
