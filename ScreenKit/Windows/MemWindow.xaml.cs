using System.Collections.ObjectModel;

namespace ScreenKit;

/// <summary>选项 → 内存占用。进程工作集，以及已加载模型的权重文件大小。</summary>
public partial class MemWindow : Window {
	readonly Func<MemSnap> read;
	readonly Func<string, string> unload;
	readonly Func<string> unloadAll;
	readonly ObservableCollection<MemHold> rows = new();
	bool unloading;
	string selId = "";

	internal MemWindow(Func<MemSnap> read, Func<string, string> unload, Func<string> unloadAll) {
		this.read = read ?? throw new ArgumentNullException(nameof(read));
		this.unload = unload ?? throw new ArgumentNullException(nameof(unload));
		this.unloadAll = unloadAll ?? throw new ArgumentNullException(nameof(unloadAll));
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
		bortcpu.Click += (_, _) => uninstallort(FeatureKind.OrtCpu, "mem.ort.cpu");
		bortcuda.Click += (_, _) => uninstallort(FeatureKind.CudaGpu, "mem.ort.cuda");
		bortdml.Click += (_, _) => uninstallort(FeatureKind.DirectMl, "mem.ort.dml");
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
		lbort.Text = Loc.T("mem.ort.head");
		paintort();
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
		foreach (var it in items) {
			it.EngineText = Loc.T("mem.eng." + (it.Engine ?? ""));
			it.BytesText = FeatureInstaller.FormatBytes(it.Bytes);
			sum += it.Bytes;
		}
		if (!force && samerows(items)) {
			lbsum.Text = items.Count == 0
				? Loc.T("mem.empty")
				: Loc.T("mem.sum", items.Count, FeatureInstaller.FormatBytes(sum));
			paintort();
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
		paintort();
	}

	bool samerows(List<MemHold> items) {
		if (rows.Count != items.Count) return false;
		for (int i = 0; i < items.Count; i++) {
			if (rows[i].Id != items[i].Id || rows[i].Bytes != items[i].Bytes
				|| rows[i].Name != items[i].Name) return false;
			rows[i].Device = items[i].Device;
			rows[i].EngineText = items[i].EngineText;
			rows[i].BytesText = items[i].BytesText;
		}
		return true;
	}

	void unloadsel() {
		if (unloading) return;
		if (lv.SelectedItem is not MemHold row || string.IsNullOrEmpty(row.Id)) return;
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
				}
				lbsum.Text = msg;
				fill(true);
				if (!string.IsNullOrEmpty(msg)) lbsum.Text = msg;
			}));
		});
	}

	void paintort() {
		paintone(bortcpu, FeatureKind.OrtCpu, "mem.ort.cpu", "cpu");
		paintone(bortcuda, FeatureKind.CudaGpu, "mem.ort.cuda", "cuda");
		paintone(bortdml, FeatureKind.DirectMl, "mem.ort.dml", "dml");
	}

	void paintone(Button b, FeatureKind kind, string nameKey, string flavor) {
		var it = new FeatureItem { Kind = kind };
		FeatureInstaller.RefreshState(it);
		var name = Loc.T(nameKey);
		var missing = it.State == FeatureInstallState.Missing;
		if (missing)
			b.Content = Loc.T("mem.ort.missing", name);
		else {
			var state = CudaBootstrap.LoadedOrtFlavor() == flavor
				? Loc.T("mem.ort.inproc")
				: it.StateText;
			b.Content = Loc.T("mem.ort.btn", name, it.SizeText, state);
		}
		b.IsEnabled = !unloading && !missing;
	}

	void uninstallort(FeatureKind kind, string nameKey) {
		if (unloading) return;
		if (FeatureInstaller.Probe(kind) == FeatureInstallState.Missing) return;
		var name = Loc.T(nameKey);
		if (MessageBox.Show(this, Loc.T("mem.ort.ask", name), Title,
			MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
			return;
		unloading = true;
		bunload.IsEnabled = false;
		paintort();
		var before = 0L;
		try { before = MemUsage.Read().WorkingSet; } catch { }
		var flavor = kind == FeatureKind.CudaGpu ? "cuda" : kind == FeatureKind.DirectMl ? "dml" : "cpu";
		Task.Run(() => {
			var loaded = CudaBootstrap.LoadedOrtFlavor();
			if (loaded == flavor) {
				if (unloadAll() == "busy") return "busy";
			}
			else
				MemUsage.Trim();
			FeatureInstaller.Uninstall(kind, null);
			if (FeatureInstaller.Probe(kind) != FeatureInstallState.Missing) return "left";
			if (CudaBootstrap.LoadedOrtFlavor() == flavor) return "resident";
			return "";
		}).ContinueWith(t => {
			Dispatcher.BeginInvoke(new Action(() => {
				unloading = false;
				bunload.IsEnabled = true;
				string msg;
				if (t.IsFaulted)
					msg = Loc.T("mem.ort.fail", name, t.Exception?.GetBaseException().Message ?? "");
				else if (t.Result == "busy")
					msg = Loc.T("mem.ort.busy");
				else if (t.Result == "left")
					msg = Loc.T("mem.ort.fail", name, Loc.T("mem.ort.left"));
				else {
					var after = 0L;
					try { after = MemUsage.Read().WorkingSet; } catch { }
					msg = Loc.T("mem.ort.done", name, FeatureInstaller.FormatBytes(before),
						FeatureInstaller.FormatBytes(after));
					if (t.Result == "resident")
						msg = msg + Loc.T("mem.ort.resident");
				}
				fill(true);
				lbsum.Text = msg;
			}));
		});
	}
}
