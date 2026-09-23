using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScreenKit;

sealed class RenameRow {
	public string Path { get; set; }
	public string From { get; set; }
	public DateTime MTime { get; set; }
	public DateTime Added { get; set; }
	public string MTimeText => MTime.ToString("yyyy-MM-dd HH:mm");
	public string AddedText => Added.ToString("yyyy-MM-dd HH:mm");
}

/// <summary>工具 → 批量重命名：源列表 + 目标名文本框。</summary>
public partial class BatchRenameWindow : Window {
	readonly ObservableCollection<RenameRow> rows = new();
	bool infering;
	bool filling;
	bool busy;
	string sortKey = "";
	bool sortAsc = true;

	public BatchRenameWindow() {
		InitializeComponent();
		lv.ItemsSource = rows;
		applylang();
		initev();
		WindowEsc.Attach(this);
		refresh();
	}

	void initev() {
		badd.Click += (_, _) => addfiles();
		bfolder.Click += (_, _) => addfolder();
		bremove.Click += (_, _) => remove();
		bup.Click += (_, _) => move(-1);
		bdown.Click += (_, _) => move(1);
		bclear.Click += (_, _) => {
			rows.Clear();
			filling = true;
			eto.Text = "";
			filling = false;
			refresh();
		};
		bgo.Click += (_, _) => go();
		bclose.Click += (_, _) => Close();
		eold.TextChanged += (_, _) => { if (!infering) previewfromrule(); };
		enew.TextChanged += (_, _) => { if (!infering) previewfromrule(); };
		cmatch.Checked += (_, _) => previewfromrule();
		cmatch.Unchecked += (_, _) => previewfromrule();
		cregex.Checked += (_, _) => previewfromrule();
		cregex.Unchecked += (_, _) => previewfromrule();
		cnoext.Checked += (_, _) => { infer(); previewfromrule(); };
		cnoext.Unchecked += (_, _) => { infer(); previewfromrule(); };
		eto.TextChanged += (_, _) => { if (!filling) refresh(); };
		lv.PreviewKeyDown += onlistkey;
		PreviewDragOver += ondrag;
		Drop += ondrop;
	}

	void applylang() {
		Title = Loc.T("rename.title");
		lbhint.Text = Loc.T("rename.hint");
		lbold.Text = Loc.T("rename.old");
		lbnew.Text = Loc.T("rename.new");
		cmatch.Content = Loc.T("rename.case");
		cregex.Content = Loc.T("rename.regex");
		cnoext.Content = Loc.T("rename.noext");
		lbsrc.Text = Loc.T("rename.src");
		lbdst.Text = Loc.T("rename.dst");
		colname.Content = Loc.T("rename.col.name");
		colmtime.Content = Loc.T("rename.col.mtime");
		coladded.Content = Loc.T("rename.col.added");
		eto.ToolTip = Loc.T("rename.dst.tip");
		ToolBtnUi.Set(badd, ToolBtnUi.Add, Loc.T("rename.add"));
		ToolBtnUi.Set(bfolder, ToolBtnUi.Folder, Loc.T("rename.folder"));
		ToolBtnUi.Set(bremove, ToolBtnUi.Delete, Loc.T("rename.remove"));
		ToolBtnUi.Set(bup, ToolBtnUi.Up, Loc.T("rename.up"));
		ToolBtnUi.Set(bdown, ToolBtnUi.Down, Loc.T("rename.down"));
		ToolBtnUi.Set(bclear, ToolBtnUi.Clear, Loc.T("imgconv.clear"));
		ToolBtnUi.Set(bgo, ToolBtnUi.Rename, Loc.T("rename.go"));
		ToolBtnUi.Set(bclose, ToolBtnUi.Close, Loc.T("imgconv.close"));
	}

	RenameOptions opts() => new() {
		OldPattern = eold.Text ?? "%1",
		NewPattern = enew.Text ?? "%1",
		MatchCase = cmatch.IsChecked == true,
		UseRegex = cregex.IsChecked == true,
		IgnoreExtension = cnoext.IsChecked == true,
	};

	void infer() {
		if (rows.Count < 2) return;
		infering = true;
		var names = rows.Select(r => r.From).ToList();
		var (o, n) = BatchRename.CommonPattern(names, cnoext.IsChecked == true);
		eold.Text = o;
		enew.Text = n;
		infering = false;
	}

	void previewfromrule() {
		var paths = rows.Select(r => r.Path).ToList();
		var froms = rows.Select(r => r.From).ToList();
		var plans = BatchRename.Plan(paths, froms, opts());
		filling = true;
		eto.Text = string.Join("\r\n", plans.Select(p => p.To));
		filling = false;
		refresh();
	}

	List<string> dstlines() {
		var raw = (eto.Text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
		return raw.Split('\n').Select(s => s.TrimEnd()).ToList();
	}

	void refresh() {
		var n = rows.Count;
		var lines = dstlines();
		var froms = rows.Select(r => r.From).ToList();
		var tos = new List<string>(n);
		for (var i = 0; i < n; i++)
			tos.Add(i < lines.Count ? lines[i] : froms[i]);
		var plans = BatchRename.PlanTo(rows.Select(r => r.Path).ToList(), froms, tos);
		var ready = plans.Count(p => p.Kind == RenameKind.Ready);
		lbstat.Text = Loc.T("rename.n", n, ready);
	}

	void addfiles() {
		var ofd = new Microsoft.Win32.OpenFileDialog {
			Title = Loc.T("rename.add"),
			Multiselect = true,
			Filter = Loc.T("rename.filter"),
		};
		if (ofd.ShowDialog(this) != true) return;
		addpaths(ofd.FileNames);
	}

	void addfolder() {
		var dlg = new System.Windows.Forms.FolderBrowserDialog();
		if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
		addpaths(Directory.GetFileSystemEntries(dlg.SelectedPath));
	}

	void addpaths(IEnumerable<string> paths) {
		var seen = new HashSet<string>(rows.Select(r => r.Path), StringComparer.OrdinalIgnoreCase);
		var now = DateTime.Now;
		foreach (var p in paths) {
			if (string.IsNullOrWhiteSpace(p) || !seen.Add(p)) continue;
			if (!File.Exists(p) && !Directory.Exists(p)) continue;
			var mtime = DateTime.Now;
			try { mtime = File.GetLastWriteTime(p); } catch { }
			rows.Add(new RenameRow {
				Path = p,
				From = Path.GetFileName(p),
				MTime = mtime,
				Added = now,
			});
		}
		infer();
		previewfromrule();
	}

	void remove() {
		var sel = lv.SelectedItems.Cast<RenameRow>().ToList();
		foreach (var r in sel) rows.Remove(r);
		previewfromrule();
	}

	void move(int delta) {
		var sel = lv.SelectedItems.Cast<RenameRow>().ToList();
		if (sel.Count == 0) return;
		var idx = sel.Select(r => rows.IndexOf(r)).Where(i => i >= 0).OrderBy(i => i).ToList();
		if (idx.Count == 0) return;
		if (delta < 0) {
			if (idx[0] <= 0) return;
			foreach (var i in idx)
				rows.Move(i, i - 1);
		}
		else {
			if (idx[idx.Count - 1] >= rows.Count - 1) return;
			for (var k = idx.Count - 1; k >= 0; k--)
				rows.Move(idx[k], idx[k] + 1);
		}
		lv.SelectedItems.Clear();
		foreach (var r in sel) lv.SelectedItems.Add(r);
		previewfromrule();
	}

	void onsort(object sender, RoutedEventArgs e) {
		var tag = (sender as GridViewColumnHeader)?.Tag as string ?? "name";
		if (sortKey == tag) sortAsc = !sortAsc;
		else {
			sortKey = tag;
			sortAsc = true;
		}
		IOrderedEnumerable<RenameRow> q;
		if (tag == "mtime")
			q = sortAsc ? rows.OrderBy(r => r.MTime) : rows.OrderByDescending(r => r.MTime);
		else if (tag == "added")
			q = sortAsc ? rows.OrderBy(r => r.Added) : rows.OrderByDescending(r => r.Added);
		else
			q = sortAsc
				? rows.OrderBy(r => r.From, StringComparer.CurrentCultureIgnoreCase)
				: rows.OrderByDescending(r => r.From, StringComparer.CurrentCultureIgnoreCase);
		var list = q.ToList();
		rows.Clear();
		foreach (var r in list) rows.Add(r);
		previewfromrule();
	}

	void onlistkey(object sender, KeyEventArgs e) {
		if (e.Key == Key.Delete) {
			remove();
			e.Handled = true;
		}
	}

	void ondrag(object sender, DragEventArgs e) {
		if (e.Data.GetDataPresent(DataFormats.FileDrop))
			e.Effects = DragDropEffects.Copy;
		else
			e.Effects = DragDropEffects.None;
		e.Handled = true;
	}

	void ondrop(object sender, DragEventArgs e) {
		if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
		var files = e.Data.GetData(DataFormats.FileDrop) as string[];
		if (files != null) addpaths(files);
	}

	void go() {
		if (busy) return;
		var nsrc = rows.Count;
		if (nsrc == 0) return;
		var lines = dstlines();
		var froms = rows.Select(r => r.From).ToList();
		var tos = new List<string>(nsrc);
		for (var i = 0; i < nsrc; i++)
			tos.Add(i < lines.Count && lines[i].Length > 0 ? lines[i] : froms[i]);
		var plans = BatchRename.PlanTo(rows.Select(r => r.Path).ToList(), froms, tos);
		var n = 0;
		var fail = 0;
		busy = true;
		try {
			foreach (var p in plans) {
				if (p.Kind != RenameKind.Ready) continue;
				try {
					BatchRename.Apply(p);
					n++;
				}
				catch { fail++; }
			}
		}
		finally { busy = false; }
		for (var i = 0; i < rows.Count; i++) {
			var plan = i < plans.Count ? plans[i] : null;
			if (plan == null || plan.Kind != RenameKind.Ready) continue;
			if (File.Exists(plan.Dest) || Directory.Exists(plan.Dest)) {
				rows[i].Path = plan.Dest;
				rows[i].From = Path.GetFileName(plan.Dest);
				try { rows[i].MTime = File.GetLastWriteTime(plan.Dest); } catch { }
			}
		}
		lv.Items.Refresh();
		previewfromrule();
		MessageBox.Show(this, Loc.T("rename.done", n, fail), Title,
			MessageBoxButton.OK, MessageBoxImage.Information);
	}
}
