using System.Collections.ObjectModel;

namespace ScreenKit;

/// <summary>选项 → 内存占用。进程工作集，以及已加载模型的权重文件大小。</summary>
public partial class MemWindow : Window {
	readonly Func<MemSnap> read;
	readonly Func<string, string> unload;
	readonly ObservableCollection<MemHold> rows = new();
	bool unloading;
	string selId = "";

	internal MemWindow(Func<MemSnap> read, Func<string, string> unload) {
		this.read = read ?? throw new ArgumentNullException(nameof(read));
		this.unload = unload ?? throw new ArgumentNullException(nameof(unload));
		InitializeComponent();
		lv.ItemsSource = rows;
		applylang();
		initev();
		WindowEsc.Attach(this);
		fill(true);
	}

	void initev() {
		brefresh.Click += (_, _) => fill(true);
		bclose.Click += (_, _) => Close();
		bunload.Click += (_, _) => unloadsel();
		lv.MouseDoubleClick += (_, _) => {
			if (lv.SelectedItem != null) unloadsel();
		};
		lv.SelectionChanged += (_, _) => {
			if (lv.SelectedItem is MemHold h) selId = h.Id ?? "";
		};
		var timer = new System.Windows.Threading.DispatcherTimer {
			Interval = TimeSpan.FromSeconds(2),
		};
		timer.Tick += (_, _) => {
			if (!unloading) fill(false);
		};
		timer.Start();
		Closed += (_, _) => timer.Stop();
	}

	void applylang() {
		Title = Loc.T("mem.title");
		lbhead.Text = Loc.T("mem.head");
		lbhint.Text = Loc.T("mem.hint");
		coleng.Header = Loc.T("mem.col.engine");
		colname.Header = Loc.T("mem.col.name");
		coldev.Header = Loc.T("mem.col.device");
		colbytes.Header = Loc.T("mem.col.bytes");
		bunload.Content = Loc.T("mem.unload");
		brefresh.Content = Loc.T("mem.refresh");
		bclose.Content = Loc.T("mem.close");
	}

	void onrowunload(object sender, RoutedEventArgs e) {
		if (sender is Button b && b.DataContext is MemHold row)
			unloadrow(row);
		e.Handled = true;
	}

	void fill(bool force) {
		MemSnap snap;
		try { snap = read(); }
		catch { return; }
		if (snap == null) return;
		lbproc.Text = Loc.T("mem.ws", FeatureInstaller.FormatBytes(snap.WorkingSet))
			+ "    " + Loc.T("mem.priv", FeatureInstaller.FormatBytes(snap.PrivateBytes))
			+ "    " + Loc.T("mem.gc", FeatureInstaller.FormatBytes(snap.GcBytes));
		var items = snap.Items ?? new List<MemHold>();
		long sum = 0;
		var action = Loc.T("mem.unload.row");
		foreach (var it in items) {
			it.EngineText = Loc.T("mem.eng." + (it.Engine ?? ""));
			it.BytesText = FeatureInstaller.FormatBytes(it.Bytes);
			it.ActionText = action;
			sum += it.Bytes;
		}
		if (!force && samerows(items)) {
			lbsum.Text = items.Count == 0
				? Loc.T("mem.empty")
				: Loc.T("mem.sum", items.Count, FeatureInstaller.FormatBytes(sum));
			return;
		}
		rows.Clear();
		foreach (var it in items) rows.Add(it);
		if (!string.IsNullOrEmpty(selId)) {
			foreach (var it in rows) {
				if (it.Id == selId) {
					lv.SelectedItem = it;
					break;
				}
			}
		}
		lbsum.Text = items.Count == 0
			? Loc.T("mem.empty")
			: Loc.T("mem.sum", items.Count, FeatureInstaller.FormatBytes(sum));
	}

	bool samerows(List<MemHold> items) {
		if (rows.Count != items.Count) return false;
		for (int i = 0; i < items.Count; i++) {
			if (rows[i].Id != items[i].Id || rows[i].Bytes != items[i].Bytes
				|| rows[i].Name != items[i].Name) return false;
			rows[i].Device = items[i].Device;
			rows[i].EngineText = items[i].EngineText;
			rows[i].BytesText = items[i].BytesText;
			rows[i].ActionText = items[i].ActionText;
		}
		return true;
	}

	void unloadsel() {
		if (lv.SelectedItem is not MemHold row) return;
		unloadrow(row);
	}

	void unloadrow(MemHold row) {
		if (unloading) return;
		if (row == null || string.IsNullOrEmpty(row.Id)) return;
		unloading = true;
		bunload.IsEnabled = false;
		var id = row.Id;
		var name = row.Name ?? "";
		var before = 0L;
		try { before = MemUsage.Read().WorkingSet; } catch { }
		Task.Run(() => unload(id)).ContinueWith(t => {
			Dispatcher.BeginInvoke(new Action(() => {
				unloading = false;
				bunload.IsEnabled = true;
				string msg;
				if (t.IsFaulted)
					msg = t.Exception?.GetBaseException().Message ?? "";
				else if (t.Result == "busy")
					msg = Loc.T("mem.busy");
				else {
					var after = 0L;
					try { after = MemUsage.Read().WorkingSet; } catch { }
					msg = Loc.T("mem.reclaim", name, FeatureInstaller.FormatBytes(before),
						FeatureInstaller.FormatBytes(after));
					if (t.Result == "resident")
						msg = msg + Loc.T("mem.ort.resident");
				}
				lbsum.Text = msg;
				fill(true);
				if (!string.IsNullOrEmpty(msg)) lbsum.Text = msg;
			}));
		});
	}
}
