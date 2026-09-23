using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace ScreenKit;

sealed class RenameRow : INotifyPropertyChanged {
	public string Path { get; set; }
	string from, to, status;
	public string From { get => from; set { from = value; On(); } }
	public string To { get => to; set { to = value; On(); } }
	public string Status { get => status; set { status = value; On(); } }
	public event PropertyChangedEventHandler PropertyChanged;
	void On([CallerMemberName] string n = null) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>工具 → 批量重命名（Everything / FastCopy 表达式）。</summary>
public partial class BatchRenameWindow : Window {
	readonly ObservableCollection<RenameRow> rows = new();
	bool infering;
	bool busy;

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
		bclear.Click += (_, _) => { rows.Clear(); refresh(); };
		bgo.Click += (_, _) => go();
		bclose.Click += (_, _) => Close();
		eold.TextChanged += (_, _) => { if (!infering) preview(); };
		enew.TextChanged += (_, _) => { if (!infering) preview(); };
		cmatch.Checked += (_, _) => preview();
		cmatch.Unchecked += (_, _) => preview();
		cregex.Checked += (_, _) => preview();
		cregex.Unchecked += (_, _) => preview();
		cnoext.Checked += (_, _) => { infer(); preview(); };
		cnoext.Unchecked += (_, _) => { infer(); preview(); };
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
		colfrom.Header = Loc.T("rename.col.from");
		colto.Header = Loc.T("rename.col.to");
		colst.Header = Loc.T("rename.col.status");
		ToolBtnUi.Set(badd, ToolBtnUi.Add, Loc.T("rename.add"));
		ToolBtnUi.Set(bfolder, ToolBtnUi.Folder, Loc.T("rename.folder"));
		ToolBtnUi.Set(bremove, ToolBtnUi.Delete, Loc.T("rename.remove"));
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

	void preview() {
		var paths = rows.Select(r => r.Path).ToList();
		var froms = rows.Select(r => r.From).ToList();
		var plans = BatchRename.Plan(paths, froms, opts());
		for (var i = 0; i < rows.Count && i < plans.Count; i++) {
			rows[i].To = plans[i].To;
			rows[i].Status = statusof(plans[i].Kind);
		}
		refresh();
	}

	string statusof(RenameKind k) => k switch {
		RenameKind.Ready => Loc.T("rename.st.ready"),
		RenameKind.Unchanged => Loc.T("rename.st.same"),
		RenameKind.Conflict => Loc.T("rename.st.conflict"),
		_ => Loc.T("rename.st.bad"),
	};

	void refresh() {
		var n = rows.Count;
		var ready = rows.Count(r => r.Status == Loc.T("rename.st.ready"));
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
		foreach (var p in paths) {
			if (string.IsNullOrWhiteSpace(p) || !seen.Add(p)) continue;
			if (!File.Exists(p) && !Directory.Exists(p)) continue;
			rows.Add(new RenameRow {
				Path = p,
				From = Path.GetFileName(p),
			});
		}
		infer();
		preview();
	}

	void remove() {
		var sel = lv.SelectedItems.Cast<RenameRow>().ToList();
		foreach (var r in sel) rows.Remove(r);
		preview();
	}

	void ondrag(object sender, System.Windows.DragEventArgs e) {
		if (e.Data.GetDataPresent(DataFormats.FileDrop))
			e.Effects = DragDropEffects.Copy;
		else
			e.Effects = DragDropEffects.None;
		e.Handled = true;
	}

	void ondrop(object sender, System.Windows.DragEventArgs e) {
		if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
		var files = e.Data.GetData(DataFormats.FileDrop) as string[];
		if (files != null) addpaths(files);
	}

	void go() {
		if (busy) return;
		preview();
		var paths = rows.Select(r => r.Path).ToList();
		var froms = rows.Select(r => r.From).ToList();
		var plans = BatchRename.Plan(paths, froms, opts());
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
		for (var i = rows.Count - 1; i >= 0; i--) {
			var p = rows[i].Path;
			var parent = Path.GetDirectoryName(p) ?? "";
			var name = Path.GetFileName(p);
			var plan = plans.FirstOrDefault(x => x.Source == p);
			if (plan != null && plan.Kind == RenameKind.Ready && n > 0) {
				var dest = plan.Dest;
				if (File.Exists(dest) || Directory.Exists(dest)) {
					rows[i].Path = dest;
					rows[i].From = Path.GetFileName(dest);
				}
			}
		}
		infer();
		preview();
		MessageBox.Show(this, Loc.T("rename.done", n, fail), Title,
			MessageBoxButton.OK, MessageBoxImage.Information);
	}
}
