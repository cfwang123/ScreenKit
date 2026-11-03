using System.Collections.ObjectModel;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ScreenKit;

/// <summary>工具 → 快速复制。</summary>
public partial class FastCopyWindow : Window {
	readonly OcrOptions opt;
	readonly ObservableCollection<string> rows = new();
	readonly DispatcherTimer statwait = new() { Interval = TimeSpan.FromMilliseconds(250) };
	bool busy;
	bool dead;
	bool ready;

	public FastCopyWindow(OcrOptions opt, IEnumerable<string> paths = null) {
		this.opt = opt ?? new OcrOptions();
		InitializeComponent();
		lsrc.ItemsSource = rows;
		rows.CollectionChanged += (_, _) => onrows();
		statwait.Tick += (_, _) => {
			statwait.Stop();
			updatestats();
		};
		applylang();
		initev();
		if (paths != null) addpaths(paths);
		WindowEsc.Attach(this, onesc);
	}

	void initev() {
		badd.Click += (_, _) => addfiles();
		badddir.Click += (_, _) => adddir();
		bremove.Click += (_, _) => removerows();
		bclear.Click += (_, _) => { if (!busy) rows.Clear(); };
		bstat.Click += (_, _) => countfiles();
		bbrowse.Click += (_, _) => pickdest();
		bstart.Click += (_, _) => start();
		breg.Click += (_, _) => togglereg();
		rcopy.Checked += (_, _) => onmode();
		rmove.Checked += (_, _) => onmode();
		rsym.Checked += (_, _) => onmode();
		rhard.Checked += (_, _) => onmode();
		rdel.Checked += (_, _) => onmode();
		rrecycle.Checked += (_, _) => onmode();
		econflict.SelectionChanged += (_, _) => saveopt();
		eworkers.LostFocus += (_, _) => saveopt();
		Drop += (_, e) => {
			if (busy) return;
			if (e.Data.GetData(DataFormats.FileDrop) is string[] files) addpaths(files);
		};
		DragOver += (_, e) => {
			e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
			e.Handled = true;
		};
		pdest.PreviewDragOver += (_, e) => {
			var ok = !busy && edest.IsEnabled && e.Data.GetDataPresent(DataFormats.FileDrop);
			e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
			e.Handled = true;
		};
		pdest.PreviewDrop += (_, e) => {
			e.Handled = true;
			if (busy || !edest.IsEnabled) return;
			if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
			var dest = FastCopy.DropDest(files);
			if (dest.Length > 0) edest.Text = dest;
		};
		Closing += (_, _) => {
			dead = true;
			saveopt();
		};
		pickmode(opt.FastCopyMode);
		syncmode();
		eworkers.Text = FastCopy.NormWorkers(opt.FastCopyWorkers).ToString();
		fillconflict();
		syncreg();
		ready = true;
	}

	void applylang() {
		Title = Loc.T("fc.title");
		lbhint.Text = Loc.T("fc.hint");
		badd.Content = Loc.T("fc.add");
		badddir.Content = Loc.T("fc.adddir");
		bremove.Content = Loc.T("fc.remove");
		bclear.Content = Loc.T("fc.clear");
		bstat.Content = Loc.T("fc.countfiles");
		lbdest.Text = Loc.T("fc.dest");
		bbrowse.Content = Loc.T("fc.browse");
		lbmode.Text = Loc.T("fc.mode");
		rcopy.Content = Loc.T("fc.modecopy");
		rmove.Content = Loc.T("fc.modemove");
		rsym.Content = Loc.T("fc.modesym");
		rhard.Content = Loc.T("fc.modehard");
		rdel.Content = Loc.T("fc.modedelete");
		rrecycle.Content = Loc.T("fc.moderecycle");
		lbconflict.Text = Loc.T("fc.conflict");
		lbworkers.Text = Loc.T("fc.workers");
		bstart.Content = Loc.T("fc.start");
	}

	/// <summary>源列表有变化：稍等一下再统计，连续添加时只算一次。</summary>
	void onrows() {
		if (dead) return;
		statwait.Stop();
		if (rows.Count == 0) {
			lbtotal.Text = "";
			return;
		}
		statwait.Start();
	}

	/// <summary>「统计文件」：弹出进度窗，只算文件数量和小计大小，不算文件夹。</summary>
	void countfiles() {
		if (busy || dead) return;
		if (rows.Count == 0) { lbstat.Text = Loc.T("fc.empty"); return; }
		statwait.Stop();
		var win = new FastCopyProgWindow(rows.ToList(), true) { Owner = this };
		win.ShowDialog();
		if (dead) return;
		lbtotal.Text = win.Stats == null ? Loc.T("fc.cancelled") : Loc.T("fc.files.msg", win.Stats.Files, FastCopy.FormatBytes(win.Stats.Bytes));
	}

	/// <summary>源列表变化后统计文件数、文件夹数和大小，进度在单独的窗口里。</summary>
	void updatestats() {
		if (dead) return;
		var paths = rows.ToList();
		if (paths.Count == 0) { lbtotal.Text = ""; return; }
		var win = new FastCopyProgWindow(paths, false) { Owner = this };
		win.ShowDialog();
		if (dead) return;
		if (win.Stats == null) { lbtotal.Text = Loc.T("fc.cancelled"); return; }
		lbtotal.Text = Loc.T("fc.size.msg", win.Stats.Files, win.Stats.Dirs, FastCopy.FormatBytes(win.Stats.Bytes));
	}

	void fillconflict() {
		econflict.Items.Clear();
		econflict.Items.Add(new ComboBoxItem { Content = Loc.T("fc.overwrite"), Tag = "overwrite" });
		econflict.Items.Add(new ComboBoxItem { Content = Loc.T("fc.skip"), Tag = "skip" });
		econflict.Items.Add(new ComboBoxItem { Content = Loc.T("fc.rename"), Tag = "rename" });
		var want = FastCopy.NormConflict(opt.FastCopyConflict);
		for (int i = 0; i < econflict.Items.Count; i++) {
			if ((econflict.Items[i] as ComboBoxItem)?.Tag as string == want) {
				econflict.SelectedIndex = i;
				return;
			}
		}
		econflict.SelectedIndex = 0;
	}

	void onesc() {
		if (busy) return;
		Close();
	}

	void addfiles() {
		var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = "*.*|*.*" };
		if (dlg.ShowDialog(this) == true) addpaths(dlg.FileNames);
	}

	void adddir() {
		using var dlg = new Forms.FolderBrowserDialog();
		if (dlg.ShowDialog() == Forms.DialogResult.OK) addpaths(new[] { dlg.SelectedPath });
	}

	void addpaths(IEnumerable<string> paths) {
		if (paths == null) return;
		foreach (var raw in paths) {
			var p = (raw ?? "").Trim().Trim('"');
			if (p.Length == 0) continue;
			if (!File.Exists(p) && !Directory.Exists(p)) continue;
			try { p = Path.GetFullPath(p); } catch { }
			var dup = false;
			foreach (var one in rows) {
				if (string.Equals(one, p, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
			}
			if (!dup) rows.Add(p);
		}
	}

	void removerows() {
		if (busy) return;
		var pick = new List<string>();
		foreach (var one in lsrc.SelectedItems) {
			if (one is string s) pick.Add(s);
		}
		foreach (var s in pick) rows.Remove(s);
	}

	void pickdest() {
		using var dlg = new Forms.FolderBrowserDialog();
		if (!string.IsNullOrWhiteSpace(edest.Text) && Directory.Exists(edest.Text))
			dlg.SelectedPath = edest.Text;
		if (dlg.ShowDialog() == Forms.DialogResult.OK) edest.Text = dlg.SelectedPath;
	}

	void togglereg() {
		try {
			if (FastCopy.MenuExists()) FastCopy.Unregister();
			else FastCopy.Register();
		}
		catch (Exception ex) {
			lbstat.Text = Loc.T("fc.reg.fail", ex.Message);
			return;
		}
		syncreg();
	}

	void syncreg() {
		if (FastCopy.IsRegistered()) {
			breg.Content = Loc.T("fc.unregister");
			lbstat.Text = Loc.T("fc.reg.on");
		}
		else if (FastCopy.MenuExists()) {
			breg.Content = Loc.T("fc.unregister");
			lbstat.Text = Loc.T("fc.reg.other");
		}
		else {
			breg.Content = Loc.T("fc.register");
			lbstat.Text = Loc.T("fc.reg.off");
		}
	}

	string conflict() => (econflict.SelectedItem as ComboBoxItem)?.Tag as string ?? "overwrite";

	string curmode() {
		if (rmove.IsChecked == true) return "move";
		if (rsym.IsChecked == true) return "symlink";
		if (rhard.IsChecked == true) return "hardlink";
		if (rdel.IsChecked == true) return "delete";
		if (rrecycle.IsChecked == true) return "recycle";
		return "copy";
	}

	void pickmode(string mode) {
		mode = FastCopy.NormMode(mode);
		if (mode == "move") rmove.IsChecked = true;
		else if (mode == "symlink") rsym.IsChecked = true;
		else if (mode == "hardlink") rhard.IsChecked = true;
		else if (mode == "delete") rdel.IsChecked = true;
		else if (mode == "recycle") rrecycle.IsChecked = true;
		else rcopy.IsChecked = true;
	}

	void onmode() {
		syncmode();
		saveopt();
	}

	void syncmode() {
		var del = curmode() is "delete" or "recycle";
		lbdest.IsEnabled = !del;
		edest.IsEnabled = !del;
		bbrowse.IsEnabled = !del;
		lbconflict.IsEnabled = !del;
		econflict.IsEnabled = !del;
		lbworkers.IsEnabled = !del;
		eworkers.IsEnabled = !del;
	}

	void saveopt() {
		if (!ready || opt == null) return;
		opt.FastCopyMode = FastCopy.NormMode(curmode());
		opt.FastCopyConflict = FastCopy.NormConflict(conflict());
		if (int.TryParse((eworkers.Text ?? "").Trim(), out var n))
			opt.FastCopyWorkers = FastCopy.NormWorkers(n);
		else opt.FastCopyWorkers = FastCopy.WORKERS_DEF;
		try { AppConfig.Save(opt); } catch { }
	}

	void start() {
		if (busy) return;
		if (rows.Count == 0) { lbstat.Text = Loc.T("fc.empty"); return; }
		var mode = curmode();
		var del = mode is "delete" or "recycle";
		var dest = (edest.Text ?? "").Trim();
		if (!del && (dest.Length == 0 || File.Exists(dest))) { lbstat.Text = Loc.T("fc.nodest"); return; }
		if (del) {
			var ask = Loc.T(mode == "recycle" ? "fc.recycle.ask" : "fc.delete.ask", rows.Count);
			if (MessageBox.Show(ask, Loc.T("fc.title"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
				return;
		}
		saveopt();
		var job = new FastCopy.Job {
			Dest = dest,
			Move = mode == "move",
			Link = mode == "symlink" || mode == "hardlink" ? mode : "",
			Delete = del ? mode : "",
			Conflict = conflict(),
			Workers = opt.FastCopyWorkers,
			Sources = rows.ToList(),
		};
		var win = new FastCopyProgWindow(job) { Owner = this };
		win.ShowDialog();
		if (dead) return;
		finish(jobresult(win), null);
	}

	static FastCopy.Result jobresult(FastCopyProgWindow win) {
		if (win.Code == 0) return new FastCopy.Result { Ok = win.Ok };
		if (win.Code == 3) return new FastCopy.Result { Ok = win.Ok, Failed = win.Failed, Cancelled = true };
		return new FastCopy.Result { Ok = win.Ok, Failed = win.Failed, Error = "see" };
	}

	void finish(FastCopy.Result res, AggregateException ex) {
		busy = false;
		bstart.IsEnabled = true;
		breg.IsEnabled = true;
		bstat.IsEnabled = true;
		if (dead) return;
		if (res == null) {
			lbstat.Text = ex?.InnerException?.Message ?? ex?.Message ?? "";
			return;
		}
		if (res.Cancelled) lbstat.Text = Loc.T("fc.cancelled");
		else if (res.Error == "empty") lbstat.Text = Loc.T("fc.empty");
		else if (res.Error == "dest") lbstat.Text = Loc.T("fc.nodest");
		else if (res.Error == "inside") lbstat.Text = Loc.T("fc.inside");
		else if (res.Error == "see") lbstat.Text = Loc.T("fc.done", res.Ok, res.Skipped, res.Failed);
		else {
			var text = Loc.T("fc.done", res.Ok, res.Skipped, res.Failed);
			if (res.Bytes > 0 && curmode() is "copy" or "move") text += "  " + FastCopy.FormatBytes(res.Bytes);
			if (!string.IsNullOrEmpty(res.Error) && res.Failed > 0) text += "  " + res.Error;
			lbstat.Text = text;
		}
		if (!res.Cancelled && curmode() is "delete" or "recycle") {
			var gone = new List<string>();
			foreach (var p in rows)
				if (!File.Exists(p) && !Directory.Exists(p)) gone.Add(p);
			foreach (var p in gone) rows.Remove(p);
		}
	}
}
