using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ScreenKit;

/// <summary>工具 → 视频批量转换。</summary>
public partial class VidConvertWindow : Window {
	readonly OcrOptions opt;
	readonly ObservableCollection<VidConvRow> rows = new();
	bool busy;
	bool ready;
	CancellationTokenSource cts;

	public VidConvertWindow(OcrOptions o) {
		opt = o ?? new OcrOptions();
		InitializeComponent();
		lv.ItemsSource = rows;
		applylang();
		loadui();
		initev();
		WindowEsc.Attach(this, onesc);
		syncoutui();
		refreshcount();
		ready = true;
	}

	void initev() {
		badd.Click += (_, _) => addfiles();
		bremove.Click += (_, _) => remove();
		bclear.Click += (_, _) => clear();
		bbrowse.Click += (_, _) => browse();
		bgo.Click += (_, _) => _ = go();
		bcancel.Click += (_, _) => cts?.Cancel();
		bclose.Click += (_, _) => Close();
		routsrc.Checked += (_, _) => syncoutui();
		routother.Checked += (_, _) => syncoutui();
		routrep.Checked += (_, _) => syncoutui();
		routrecycle.Checked += (_, _) => syncoutui();
		lv.PreviewDragOver += ondragover;
		lv.Drop += ondrop;
		PreviewDragOver += ondragover;
		Drop += ondrop;
		Closing += onclosing;
	}

	void applylang() {
		Title = Loc.T("vidconv.title");
		lbfmt.Text = Loc.T("vidconv.fmt");
		lbcrf.Text = Loc.T("vidconv.crf");
		ecrf.ToolTip = Loc.T("vidconv.crf.tip");
		eshorten.Content = Loc.T("vidconv.short");
		lbshort.Text = Loc.T("vidconv.shortpx");
		eaudio.Content = Loc.T("vidconv.audio");
		lbout.Text = Loc.T("vidconv.out");
		routsrc.Content = Loc.T("vidconv.outsrc");
		routother.Content = Loc.T("vidconv.outother");
		routrep.Content = Loc.T("vidconv.outreplace");
		routrecycle.Content = Loc.T("vidconv.outrecycle");
		routrep.ToolTip = Loc.T("vidconv.outreplace.tip");
		routrecycle.ToolTip = Loc.T("vidconv.outrecycle.tip");
		ToolBtnUi.Set(bbrowse, ToolBtnUi.Browse, Loc.T("vidconv.browse"));
		ToolBtnUi.Set(badd, ToolBtnUi.Add, Loc.T("vidconv.add"));
		ToolBtnUi.Set(bremove, ToolBtnUi.Delete, Loc.T("vidconv.remove"));
		ToolBtnUi.Set(bclear, ToolBtnUi.Clear, Loc.T("vidconv.clear"));
		ToolBtnUi.Set(bgo, ToolBtnUi.Play, Loc.T("vidconv.go"));
		ToolBtnUi.Set(bcancel, ToolBtnUi.Cancel, Loc.T("vidconv.cancel"));
		ToolBtnUi.Set(bclose, ToolBtnUi.Close, Loc.T("vidconv.close"));
		lbdrop.Text = Loc.T("vidconv.drop");
		colname.Header = Loc.T("vidconv.col.name");
		colst.Header = Loc.T("vidconv.col.status");
	}

	void loadui() {
		var fmt = VidConvert.NormFmt(opt.VidConvFormat);
		foreach (var item in efmt.Items) {
			if (item is ComboBoxItem ci && (ci.Tag as string) == fmt) {
				efmt.SelectedItem = ci;
				break;
			}
		}
		ecrf.Text = VidConvert.NormCrf(fmt, opt.VidConvCrf < 0 ? 23 : opt.VidConvCrf).ToString();
		eshorten.IsChecked = opt.VidConvShortEnabled;
		eshort.Text = (opt.VidConvShortPx < 16 ? 1080 : opt.VidConvShortPx).ToString();
		eaudio.IsChecked = opt.VidConvAudio;
		var mode = VidConvert.NormOutMode(opt.VidConvOutMode);
		routsrc.IsChecked = mode == VidConvert.OUTBESIDE;
		routother.IsChecked = mode == VidConvert.OUTOTHER;
		routrep.IsChecked = mode == VidConvert.OUTREPLACE;
		routrecycle.IsChecked = mode == VidConvert.OUTRECYCLE;
		eoutdir.Text = opt.VidConvOutDir ?? "";
	}

	bool saveopt(bool needDir) {
		var fmt = (efmt.SelectedItem as ComboBoxItem)?.Tag as string ?? "mp4";
		fmt = VidConvert.NormFmt(fmt);
		if (!int.TryParse((ecrf.Text ?? "").Trim(), out var crf)) {
			MessageBox.Show(this, Loc.T("vidconv.crf.bad"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
			return false;
		}
		crf = VidConvert.NormCrf(fmt, crf);
		var shortEn = eshorten.IsChecked == true;
		var sp = opt.VidConvShortPx < 16 ? 1080 : opt.VidConvShortPx;
		if (shortEn) {
			if (!int.TryParse((eshort.Text ?? "").Trim(), out sp) || sp < 16 || sp > 7680) {
				MessageBox.Show(this, Loc.T("vidconv.short.bad"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
				return false;
			}
		}
		opt.VidConvFormat = fmt;
		opt.VidConvCrf = crf;
		opt.VidConvShortEnabled = shortEn;
		opt.VidConvShortPx = sp;
		opt.VidConvAudio = eaudio.IsChecked == true;
		opt.VidConvOutMode = pickout();
		opt.VidConvOutDir = (eoutdir.Text ?? "").Trim();
		if (needDir && opt.VidConvOutMode == VidConvert.OUTOTHER && opt.VidConvOutDir.Length == 0) {
			MessageBox.Show(this, Loc.T("vidconv.nodir"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
			return false;
		}
		try { AppConfig.Save(opt); } catch { }
		return true;
	}

	string pickout() {
		if (routother.IsChecked == true) return VidConvert.OUTOTHER;
		if (routrep.IsChecked == true) return VidConvert.OUTREPLACE;
		if (routrecycle.IsChecked == true) return VidConvert.OUTRECYCLE;
		return VidConvert.OUTBESIDE;
	}

	void syncoutui() {
		var other = routother.IsChecked == true;
		eoutdir.IsEnabled = other;
		bbrowse.IsEnabled = other && !busy;
	}

	void refreshcount() {
		lbcount.Text = string.Format(Loc.T("vidconv.n"), rows.Count);
		lbdrop.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
	}

	void addfiles() {
		var ofd = new Microsoft.Win32.OpenFileDialog {
			Filter = Loc.T("vidconv.filter"),
			Multiselect = true,
		};
		if (ofd.ShowDialog(this) != true) return;
		addpaths(VidConvert.Collect(ofd.FileNames));
	}

	void addpaths(List<string> paths) {
		if (paths == null || paths.Count == 0) return;
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var row in rows)
			seen.Add(row.FilePath);
		foreach (var p in paths) {
			if (!seen.Add(p)) continue;
			rows.Add(new VidConvRow(p, Loc.T("vidconv.st.wait")));
		}
		refreshcount();
	}

	void remove() {
		var sel = lv.SelectedItems.Cast<VidConvRow>().ToList();
		foreach (var row in sel)
			rows.Remove(row);
		refreshcount();
	}

	void clear() {
		rows.Clear();
		refreshcount();
	}

	void browse() {
		var dlg = new System.Windows.Forms.FolderBrowserDialog();
		if (!string.IsNullOrWhiteSpace(eoutdir.Text) && System.IO.Directory.Exists(eoutdir.Text))
			dlg.SelectedPath = eoutdir.Text;
		if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
		eoutdir.Text = dlg.SelectedPath;
	}

	void ondragover(object sender, DragEventArgs e) {
		e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}

	void ondrop(object sender, DragEventArgs e) {
		if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
		var files = e.Data.GetData(DataFormats.FileDrop) as string[];
		addpaths(VidConvert.Collect(files));
		e.Handled = true;
	}

	void onesc() {
		if (busy) cts?.Cancel();
		else Close();
	}

	void onclosing(object sender, CancelEventArgs e) {
		if (busy) {
			e.Cancel = true;
			cts?.Cancel();
			return;
		}
		if (ready) saveopt(false);
	}

	void setbusy(bool on) {
		busy = on;
		bgo.IsEnabled = !on;
		badd.IsEnabled = !on;
		bremove.IsEnabled = !on;
		bclear.IsEnabled = !on;
		efmt.IsEnabled = !on;
		ecrf.IsEnabled = !on;
		eshorten.IsEnabled = !on;
		eshort.IsEnabled = !on;
		eaudio.IsEnabled = !on;
		routsrc.IsEnabled = !on;
		routother.IsEnabled = !on;
		routrep.IsEnabled = !on;
		routrecycle.IsEnabled = !on;
		bcancel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
		syncoutui();
	}

	async Task go() {
		if (busy) return;
		if (rows.Count == 0) {
			MessageBox.Show(this, Loc.T("vidconv.empty"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		if (!saveopt(true)) return;
		if (!FeaturePrompt.EnsureFfmpeg(this) || string.IsNullOrEmpty(VidConvert.FindExe())) {
			MessageBox.Show(this, Loc.T("vidconv.noffmpeg"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		if (VidConvert.IsReplace(opt.VidConvOutMode)) {
			var ask = opt.VidConvOutMode == VidConvert.OUTRECYCLE
				? Loc.T("vidconv.recycle.confirm")
				: Loc.T("vidconv.replace.confirm");
			var r = MessageBox.Show(this, string.Format(ask, rows.Count), Title,
				MessageBoxButton.OKCancel, MessageBoxImage.Warning);
			if (r != MessageBoxResult.OK) return;
		}
		setbusy(true);
		cts = new CancellationTokenSource();
		var token = cts.Token;
		var ok = 0;
		var fail = 0;
		var fmt = opt.VidConvFormat;
		var crf = opt.VidConvCrf;
		var shortEn = opt.VidConvShortEnabled;
		var shortPx = opt.VidConvShortPx;
		var audio = opt.VidConvAudio;
		var mode = opt.VidConvOutMode;
		var outDir = opt.VidConvOutDir;
		var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		try {
			for (var i = 0; i < rows.Count; i++) {
				if (token.IsCancellationRequested) break;
				var row = rows[i];
				row.Status = Loc.T("vidconv.st.run");
				lbmsg.Text = string.Format(Loc.T("vidconv.progress"), i + 1, rows.Count);
				string dst;
				try {
					dst = VidConvert.MakeOutPath(row.FilePath, fmt, mode, outDir, reserved);
				}
				catch (Exception ex) {
					row.Status = ex.Message;
					fail++;
					continue;
				}
				try {
					await Task.Run(() => VidConvert.ConvertOne(row.FilePath, dst, fmt, crf,
						shortEn, shortPx, audio, mode, token), token).ConfigureAwait(true);
					row.Status = Loc.T("vidconv.st.ok");
					ok++;
				}
				catch (OperationCanceledException) {
					row.Status = Loc.T("vidconv.st.cancel");
					break;
				}
				catch (Exception ex) {
					row.Status = ex.Message;
					fail++;
				}
			}
		}
		finally {
			setbusy(false);
			cts.Dispose();
			cts = null;
			lbmsg.Text = string.Format(Loc.T("vidconv.done"), ok, fail);
		}
	}

	sealed class VidConvRow : INotifyPropertyChanged {
		string status;
		public string FilePath { get; }
		public string FileName => System.IO.Path.GetFileName(FilePath);
		public string Status {
			get => status;
			set {
				if (status == value) return;
				status = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
			}
		}
		public event PropertyChangedEventHandler PropertyChanged;
		public VidConvRow(string path, string st) {
			FilePath = path;
			status = st;
		}
	}
}
