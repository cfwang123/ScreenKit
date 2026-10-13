using System.Collections.ObjectModel;
using Forms = System.Windows.Forms;

namespace ScreenKit;

/// <summary>工具 → 快速复制。</summary>
public partial class FastCopyWindow : Window {
	readonly OcrOptions opt;
	readonly ObservableCollection<string> rows = new();
	CancellationTokenSource cts;
	bool busy;
	bool dead;
	bool ready;

	public FastCopyWindow(OcrOptions opt, IEnumerable<string> paths = null) {
		this.opt = opt ?? new OcrOptions();
		InitializeComponent();
		lsrc.ItemsSource = rows;
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
		bbrowse.Click += (_, _) => pickdest();
		bstart.Click += (_, _) => start();
		bcancel.Click += (_, _) => cts?.Cancel();
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
			try { cts?.Cancel(); } catch { }
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
		bcancel.Content = Loc.T("fc.cancel");
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
		if (busy) { cts?.Cancel(); return; }
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
		busy = true;
		bstart.IsEnabled = false;
		bcancel.IsEnabled = true;
		breg.IsEnabled = false;
		cts = new CancellationTokenSource();
		var job = new FastCopy.Job {
			Dest = dest,
			Move = mode == "move",
			Link = mode == "symlink" || mode == "hardlink" ? mode : "",
			Delete = del ? mode : "",
			Conflict = conflict(),
			Workers = opt.FastCopyWorkers,
			Cancel = cts.Token,
			Sources = rows.ToList(),
		};
		int nextui = Environment.TickCount - 1000;
		job.OnProgress = p => {
			var now = Environment.TickCount;
			if (unchecked(now - nextui) < 80 && p.DoneFiles < p.TotalFiles) return;
			nextui = now;
			if (dead) return;
			Dispatcher.BeginInvoke(new Action(() => {
				if (dead) return;
				lbstat.Text = $"{p.DoneFiles}/{p.TotalFiles}  {Path.GetFileName(p.File)}";
			}));
		};
		Task.Run(() => FastCopy.Run(job)).ContinueWith(t => {
			FastCopy.Result res = null;
			if (t.Status == TaskStatus.RanToCompletion) res = t.Result;
			Dispatcher.BeginInvoke(new Action(() => finish(res, t.Exception)));
		});
	}

	void finish(FastCopy.Result res, AggregateException ex) {
		busy = false;
		bstart.IsEnabled = true;
		bcancel.IsEnabled = false;
		breg.IsEnabled = true;
		try { cts?.Dispose(); } catch { }
		cts = null;
		if (dead) return;
		if (res == null) {
			lbstat.Text = ex?.InnerException?.Message ?? ex?.Message ?? "";
			return;
		}
		if (res.Cancelled) lbstat.Text = Loc.T("fc.cancelled");
		else if (res.Error == "empty") lbstat.Text = Loc.T("fc.empty");
		else if (res.Error == "dest") lbstat.Text = Loc.T("fc.nodest");
		else if (res.Error == "inside") lbstat.Text = Loc.T("fc.inside");
		else lbstat.Text = Loc.T("fc.done", res.Ok, res.Skipped, res.Failed)
			+ (string.IsNullOrEmpty(res.Error) || res.Failed == 0 ? "" : "  " + res.Error);
		if (!res.Cancelled && curmode() is "delete" or "recycle") {
			var gone = new List<string>();
			foreach (var p in rows)
				if (!File.Exists(p) && !Directory.Exists(p)) gone.Add(p);
			foreach (var p in gone) rows.Remove(p);
		}
	}
}
