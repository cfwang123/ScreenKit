using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace ScreenKit;

/// <summary>工具 → 图片格式转换。</summary>
public partial class ImgConvertWindow : Window {
	readonly OcrOptions opt;
	readonly ObservableCollection<ImgConvRow> rows = new();
	bool busy;
	bool syncing;
	CancellationTokenSource cts;

	public ImgConvertWindow(OcrOptions o) {
		opt = o ?? new OcrOptions();
		InitializeComponent();
		lv.ItemsSource = rows;
		applylang();
		loadui();
		initev();
		WindowEsc.Attach(this, onesc);
		syncoutui();
		syncfmtui();
		syncselui();
		refreshcount();
	}

	void initev() {
		badd.Click += (_, _) => addfiles();
		bremove.Click += (_, _) => remove();
		bclear.Click += (_, _) => clear();
		bbrowse.Click += (_, _) => browse();
		bgo.Click += (_, _) => _ = go();
		bclose.Click += (_, _) => Close();
		efmt.SelectionChanged += (_, _) => syncfmtui();
		routsrc.Checked += (_, _) => syncoutui();
		routother.Checked += (_, _) => syncoutui();
		lv.SelectionChanged += (_, _) => syncselui();
		rrot0.Checked += (_, _) => applyxf();
		rrot90.Checked += (_, _) => applyxf();
		rrot180.Checked += (_, _) => applyxf();
		rrot270.Checked += (_, _) => applyxf();
		cmirror.Checked += (_, _) => applyxf();
		cmirror.Unchecked += (_, _) => applyxf();
		lv.PreviewDragOver += ondragover;
		lv.Drop += ondrop;
		PreviewDragOver += ondragover;
		Drop += ondrop;
		Closing += onclosing;
	}

	void applylang() {
		Title = Loc.T("imgconv.title");
		lbfmt.Text = Loc.T("imgconv.fmt");
		lbjpgq.Text = Loc.T("imgconv.jpgq");
		ejpgq.ToolTip = Loc.T("imgconv.jpgq.tip");
		emaxen.Content = Loc.T("imgconv.max");
		lbmaxw.Text = Loc.T("imgconv.maxw");
		lbmaxh.Text = Loc.T("imgconv.maxh");
		lbout.Text = Loc.T("imgconv.out");
		routsrc.Content = Loc.T("imgconv.outsrc");
		routother.Content = Loc.T("imgconv.outother");
		bbrowse.Content = Loc.T("imgconv.browse");
		badd.Content = Loc.T("imgconv.add");
		bremove.Content = Loc.T("imgconv.remove");
		bclear.Content = Loc.T("imgconv.clear");
		lbdrop.Text = Loc.T("imgconv.drop");
		colname.Header = Loc.T("imgconv.col.name");
		colxf.Header = Loc.T("imgconv.col.xf");
		colst.Header = Loc.T("imgconv.col.status");
		lbrot.Text = Loc.T("imgconv.rot");
		rrot0.Content = Loc.T("imgconv.rot0");
		rrot90.Content = Loc.T("imgconv.rot90");
		rrot180.Content = Loc.T("imgconv.rot180");
		rrot270.Content = Loc.T("imgconv.rot270");
		cmirror.Content = Loc.T("imgconv.mirror");
		bgo.Content = Loc.T("imgconv.go");
		bclose.Content = Loc.T("imgconv.close");
		foreach (ComboBoxItem it in efmt.Items) {
			var tag = it.Tag as string ?? "";
			it.Content = tag switch {
				"png" => Loc.T("imgconv.fmt.png"),
				"bmp" => Loc.T("imgconv.fmt.bmp"),
				_ => Loc.T("imgconv.fmt.jpg"),
			};
		}
		refreshcount();
	}

	void loadui() {
		var fmt = ImgConvert.NormFmt(opt.ImgConvFormat);
		foreach (ComboBoxItem it in efmt.Items) {
			if (string.Equals(it.Tag as string, fmt, StringComparison.OrdinalIgnoreCase)) {
				efmt.SelectedItem = it;
				break;
			}
		}
		if (efmt.SelectedItem == null) efmt.SelectedIndex = 0;
		var q = Compat.Clamp(opt.ImgConvJpgQuality <= 0 ? 60 : opt.ImgConvJpgQuality, 1, 100);
		ejpgq.Text = q.ToString();
		emaxen.IsChecked = opt.ImgConvMaxSizeEnabled;
		emaxw.Text = (opt.ImgConvMaxWidth < 16 ? 1920 : opt.ImgConvMaxWidth).ToString();
		emaxh.Text = (opt.ImgConvMaxHeight < 16 ? 1080 : opt.ImgConvMaxHeight).ToString();
		if (opt.ImgConvOutBeside) routsrc.IsChecked = true;
		else routother.IsChecked = true;
		eoutdir.Text = opt.ImgConvOutDir ?? "";
	}

	bool saveui() {
		opt.ImgConvFormat = (efmt.SelectedItem as ComboBoxItem)?.Tag as string ?? "jpg";
		if (!int.TryParse((ejpgq.Text ?? "").Trim(), out var q) || q < 1 || q > 100) {
			MessageBox.Show(this, Loc.T("imgconv.jpgq.bad"), Loc.T("imgconv.title"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
			ejpgq.Focus();
			return false;
		}
		opt.ImgConvJpgQuality = q;
		opt.ImgConvMaxSizeEnabled = emaxen.IsChecked == true;
		if (opt.ImgConvMaxSizeEnabled) {
			if (!tryint(emaxw, Loc.T("imgconv.maxw"), 16, 16384, out var mw)) return false;
			if (!tryint(emaxh, Loc.T("imgconv.maxh"), 16, 16384, out var mh)) return false;
			opt.ImgConvMaxWidth = mw;
			opt.ImgConvMaxHeight = mh;
		}
		opt.ImgConvOutBeside = routsrc.IsChecked == true;
		opt.ImgConvOutDir = (eoutdir.Text ?? "").Trim();
		if (!opt.ImgConvOutBeside && string.IsNullOrWhiteSpace(opt.ImgConvOutDir)) {
			MessageBox.Show(this, Loc.T("imgconv.nodir"), Loc.T("imgconv.title"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
			browse();
			if (string.IsNullOrWhiteSpace((eoutdir.Text ?? "").Trim())) return false;
			opt.ImgConvOutDir = (eoutdir.Text ?? "").Trim();
		}
		return true;
	}

	bool tryint(TextBox box, string name, int min, int max, out int value) {
		value = 0;
		if (!int.TryParse((box.Text ?? "").Trim(), out value)) {
			MessageBox.Show(this, Loc.T("imgconv.int.bad", name), Loc.T("imgconv.title"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
			box.Focus();
			return false;
		}
		if (value < min || value > max) {
			MessageBox.Show(this, Loc.T("imgconv.int.range", name, min, max), Loc.T("imgconv.title"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
			box.Focus();
			return false;
		}
		return true;
	}

	void syncfmtui() {
		var jpg = string.Equals((efmt.SelectedItem as ComboBoxItem)?.Tag as string, "jpg",
			StringComparison.OrdinalIgnoreCase);
		ejpgq.IsEnabled = jpg && !busy;
		lbjpgq.Opacity = jpg ? 1 : 0.55;
		ejpgq.Opacity = jpg ? 1 : 0.55;
	}

	void syncoutui() {
		var other = routother.IsChecked == true;
		eoutdir.IsEnabled = other && !busy;
		bbrowse.IsEnabled = other && !busy;
		eoutdir.Opacity = other ? 1 : 0.55;
	}

	void syncselui() {
		var it = lv.SelectedItem as ImgConvRow;
		var on = it != null && !busy;
		rrot0.IsEnabled = on;
		rrot90.IsEnabled = on;
		rrot180.IsEnabled = on;
		rrot270.IsEnabled = on;
		cmirror.IsEnabled = on;
		if (it == null) return;
		syncing = true;
		rrot0.IsChecked = it.Rotate == 0;
		rrot90.IsChecked = it.Rotate == 90;
		rrot180.IsChecked = it.Rotate == 180;
		rrot270.IsChecked = it.Rotate == 270;
		cmirror.IsChecked = it.Mirror;
		syncing = false;
	}

	void applyxf() {
		if (syncing || busy) return;
		var it = lv.SelectedItem as ImgConvRow;
		if (it == null) return;
		if (rrot90.IsChecked == true) it.Rotate = 90;
		else if (rrot180.IsChecked == true) it.Rotate = 180;
		else if (rrot270.IsChecked == true) it.Rotate = 270;
		else it.Rotate = 0;
		it.Mirror = cmirror.IsChecked == true;
	}

	void ondragover(object sender, DragEventArgs e) {
		if (busy) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
		if (hasdrop(e.Data)) {
			e.Effects = DragDropEffects.Copy;
			e.Handled = true;
		}
	}

	void ondrop(object sender, DragEventArgs e) {
		if (busy) return;
		var paths = pickpaths(e.Data);
		if (paths.Count == 0) return;
		e.Handled = true;
		addpaths(paths);
	}

	static bool hasdrop(System.Windows.IDataObject data) {
		if (data == null) return false;
		try {
			if (data.GetDataPresent(DataFormats.FileDrop)) return true;
		}
		catch { }
		try {
			if (data.GetDataPresent(DataFormats.Bitmap)) return true;
		}
		catch { }
		return false;
	}

	List<string> pickpaths(System.Windows.IDataObject data) {
		var r = new List<string>();
		if (data == null) return r;
		try {
			if (data.GetDataPresent(DataFormats.FileDrop)) {
				var files = data.GetData(DataFormats.FileDrop) as string[];
				if (files != null && files.Length > 0)
					r.AddRange(ImgConvert.CollectImages(files));
			}
		}
		catch { }
		if (r.Count > 0) return r;
		try {
			if (!data.GetDataPresent(DataFormats.Bitmap)) return r;
			var raw = data.GetData(DataFormats.Bitmap);
			if (raw is BitmapSource bs) {
				r.Add(ImgConvert.SaveTemp(bs));
				return r;
			}
			if (raw is System.Drawing.Bitmap db) {
				using (db) {
					var path = TmpStore.NewPath("imgconv", ".png");
					db.Save(path, System.Drawing.Imaging.ImageFormat.Png);
					r.Add(path);
				}
			}
		}
		catch { }
		return r;
	}

	void addfiles() {
		if (busy) return;
		var ofd = new Microsoft.Win32.OpenFileDialog {
			Title = Loc.T("imgconv.add"),
			Multiselect = true,
			Filter = Loc.T("imgconv.filter"),
		};
		if (ofd.ShowDialog(this) != true) return;
		addpaths(ImgConvert.CollectImages(ofd.FileNames));
	}

	void addpaths(IEnumerable<string> paths) {
		if (paths == null) return;
		var seen = new HashSet<string>(rows.Select(x => x.FilePath), StringComparer.OrdinalIgnoreCase);
		var n = 0;
		foreach (var p in paths) {
			if (string.IsNullOrWhiteSpace(p) || !File.Exists(p)) continue;
			if (!seen.Add(p)) continue;
			rows.Add(new ImgConvRow(p));
			n++;
		}
		refreshcount();
		if (n > 0 && lv.SelectedItem == null && rows.Count > 0)
			lv.SelectedIndex = 0;
	}

	void remove() {
		if (busy) return;
		var sel = lv.SelectedItems.Cast<ImgConvRow>().ToList();
		if (sel.Count == 0) return;
		foreach (var it in sel) rows.Remove(it);
		refreshcount();
		syncselui();
	}

	void clear() {
		if (busy) return;
		rows.Clear();
		refreshcount();
		syncselui();
	}

	void browse() {
		using var dlg = new System.Windows.Forms.FolderBrowserDialog {
			Description = Loc.T("imgconv.outother"),
			ShowNewFolderButton = true,
		};
		var cur = (eoutdir.Text ?? "").Trim();
		if (Directory.Exists(cur)) dlg.SelectedPath = cur;
		if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
		eoutdir.Text = dlg.SelectedPath ?? "";
		routother.IsChecked = true;
		syncoutui();
	}

	void refreshcount() {
		lbcount.Text = Loc.T("imgconv.n", rows.Count);
		lbdrop.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
	}

	void setbusy(bool on) {
		busy = on;
		badd.IsEnabled = !on;
		bremove.IsEnabled = !on;
		bclear.IsEnabled = !on;
		efmt.IsEnabled = !on;
		emaxen.IsEnabled = !on;
		emaxw.IsEnabled = !on;
		emaxh.IsEnabled = !on;
		routsrc.IsEnabled = !on;
		routother.IsEnabled = !on;
		bclose.IsEnabled = !on;
		bgo.Content = on ? Loc.T("imgconv.cancel") : Loc.T("imgconv.go");
		syncfmtui();
		syncoutui();
		syncselui();
	}

	async Task go() {
		if (busy) {
			try { cts?.Cancel(); } catch { }
			bgo.IsEnabled = false;
			lbstatus.Text = Loc.T("imgconv.cancelling");
			return;
		}
		if (rows.Count == 0) {
			MessageBox.Show(this, Loc.T("imgconv.empty"), Loc.T("imgconv.title"),
				MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		if (!saveui()) return;
		try { AppConfig.Save(opt); } catch { }

		cts = new CancellationTokenSource();
		var token = cts.Token;
		setbusy(true);
		bgo.IsEnabled = true;
		var fmt = ImgConvert.NormFmt(opt.ImgConvFormat);
		var quality = opt.ImgConvJpgQuality;
		var maxEn = opt.ImgConvMaxSizeEnabled;
		var maxW = opt.ImgConvMaxWidth;
		var maxH = opt.ImgConvMaxHeight;
		var beside = opt.ImgConvOutBeside;
		var outDir = opt.ImgConvOutDir;
		var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var total = rows.Count;
		var ok = 0;
		var fail = 0;
		pbar.Maximum = total;
		pbar.Value = 0;
		lbpct.Text = "0%";
		foreach (var row in rows)
			row.Status = Loc.T("imgconv.st.wait");
		try {
			for (var i = 0; i < rows.Count; i++) {
				token.ThrowIfCancellationRequested();
				var row = rows[i];
				row.Status = Loc.T("imgconv.st.run");
				lbstatus.Text = Loc.T("imgconv.busy", i + 1, total, row.FileName);
				pbar.Value = i;
				lbpct.Text = $"{(int)(i * 100.0 / Math.Max(1, total))}%";
				var src = row.FilePath;
				var rot = row.Rotate;
				var mir = row.Mirror;
				try {
					var dst = ImgConvert.MakeOutPath(src, fmt, beside, outDir, reserved);
					await Task.Run(() => ImgConvert.ConvertOne(src, dst, fmt, quality,
						maxEn, maxW, maxH, rot, mir, token), token).ConfigureAwait(true);
					row.Status = Loc.T("imgconv.st.ok");
					ok++;
				}
				catch (OperationCanceledException) { throw; }
				catch (Exception ex) {
					fail++;
					row.Status = Loc.T("imgconv.st.fail") + " · " + (ex.Message ?? "");
				}
			}
			pbar.Value = total;
			lbpct.Text = "100%";
			lbstatus.Text = Loc.T("imgconv.done", ok, fail);
		}
		catch (OperationCanceledException) {
			lbstatus.Text = Loc.T("imgconv.cancelled", ok, fail);
		}
		catch (Exception ex) {
			lbstatus.Text = Loc.T("imgconv.fail", ex.Message);
		}
		finally {
			try { cts.Dispose(); } catch { }
			cts = null;
			setbusy(false);
			bgo.IsEnabled = true;
		}
	}

	void onesc() {
		if (busy) {
			try { cts?.Cancel(); } catch { }
			return;
		}
		Close();
	}

	void onclosing(object sender, System.ComponentModel.CancelEventArgs e) {
		if (busy) {
			try { cts?.Cancel(); } catch { }
			e.Cancel = true;
			lbstatus.Text = Loc.T("imgconv.cancelling");
			return;
		}
		try { saveui(); } catch { }
		try { AppConfig.Save(opt); } catch { }
	}

	sealed class ImgConvRow : INotifyPropertyChanged {
		int rotate;
		bool mirror;
		string status;

		public string FilePath { get; }
		public string FileName => System.IO.Path.GetFileName(FilePath) ?? FilePath;

		public int Rotate {
			get => rotate;
			set {
				if (rotate == value) return;
				rotate = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(XfText));
			}
		}

		public bool Mirror {
			get => mirror;
			set {
				if (mirror == value) return;
				mirror = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(XfText));
			}
		}

		public string Status {
			get => status;
			set {
				if (status == value) return;
				status = value ?? "";
				OnPropertyChanged();
			}
		}

		public string XfText {
			get {
				var parts = new List<string>();
				if (rotate != 0) parts.Add(rotate + "°");
				if (mirror) parts.Add(Loc.T("imgconv.mirror"));
				return parts.Count == 0 ? "—" : string.Join(" · ", parts);
			}
		}

		public event PropertyChangedEventHandler PropertyChanged;

		public ImgConvRow(string path) {
			FilePath = path ?? "";
			status = Loc.T("imgconv.st.wait");
		}

		void OnPropertyChanged([CallerMemberName] string name = null) {
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
		}
	}
}
