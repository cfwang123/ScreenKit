using System.Collections.ObjectModel;

namespace ScreenKit;

sealed class CodeStatLangView {
	public string Name { get; set; }
	public string Lang { get; set; }
	public int Files { get; set; }
	public int Lines { get; set; }
	public int Code { get; set; }
	public int Comments { get; set; }
	public int Blanks { get; set; }
	public bool Total { get; set; }
}

sealed class CodeStatFileView {
	public string Name { get; set; }
	public string Path { get; set; }
	public int Lines { get; set; }
	public int Code { get; set; }
}

/// <summary>工具 → 代码统计。拖入目录或文件，按语言列出代码、注释和空行。</summary>
public partial class CodeStatWindow : Window {
	readonly ObservableCollection<string> paths = new();
	readonly ObservableCollection<CodeStatLangView> langs = new();
	readonly ObservableCollection<CodeStatFileView> files = new();
	CodeStatResult last;
	List<string> roots = new();
	int seq;

	public CodeStatWindow() {
		InitializeComponent();
		lpaths.ItemsSource = paths;
		lvlang.ItemsSource = langs;
		lvfile.ItemsSource = files;
		applylang();
		initev();
		WindowEsc.Attach(this);
	}

	void initev() {
		badd.Click += (_, _) => addbydialog();
		bdel.Click += (_, _) => del();
		bclear.Click += (_, _) => paths.Clear();
		bgo.Click += (_, _) => go();
		lpaths.MouseDoubleClick += (_, _) => openpath();
		lvfile.MouseDoubleClick += (_, _) => openfile();
		lvlang.SelectionChanged += (_, _) => showfiles();
		PreviewDragOver += ondrag;
		Drop += ondrop;
	}

	void applylang() {
		Title = Loc.T("codestat.title");
		tabpick.Header = Loc.T("codestat.tab.pick");
		tabstat.Header = Loc.T("codestat.tab.stat");
		cgit.Content = Loc.T("codestat.git");
		lbignore.Text = Loc.T("codestat.ignore");
		eignore.ToolTip = Loc.T("codestat.ignore.tip");
		collang.Header = Loc.T("codestat.col.lang");
		colfiles.Header = Loc.T("codestat.col.files");
		collines.Header = Loc.T("codestat.col.lines");
		colcode.Header = Loc.T("codestat.col.code");
		colcmt.Header = Loc.T("codestat.col.comments");
		colblank.Header = Loc.T("codestat.col.blanks");
		colfname.Header = Loc.T("codestat.col.file");
		colflines.Header = Loc.T("codestat.col.lines");
		colfcode.Header = Loc.T("codestat.col.code");
		ToolBtnUi.Set(badd, ToolBtnUi.Add, Loc.T("codestat.add"));
		ToolBtnUi.Set(bdel, ToolBtnUi.Delete, Loc.T("codestat.del"));
		ToolBtnUi.Set(bclear, ToolBtnUi.Clear, Loc.T("imgconv.clear"));
		ToolBtnUi.Set(bgo, ToolBtnUi.Play, Loc.T("codestat.go"));
	}

	void addbydialog() {
		using var dlg = new System.Windows.Forms.FolderBrowserDialog {
			Description = Loc.T("codestat.add"),
		};
		if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
		addpath(dlg.SelectedPath);
	}

	void ondrag(object sender, DragEventArgs e) {
		e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}

	void ondrop(object sender, DragEventArgs e) {
		if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
		var list = e.Data.GetData(DataFormats.FileDrop) as string[];
		if (list == null) return;
		foreach (var p in list) addpath(p);
		tabs.SelectedIndex = 0;
	}

	void addpath(string path) {
		if (string.IsNullOrWhiteSpace(path)) return;
		foreach (var s in paths)
			if (string.Equals(s, path, StringComparison.OrdinalIgnoreCase)) return;
		paths.Add(path);
	}

	void del() {
		var sel = lpaths.SelectedItems.Cast<string>().ToList();
		foreach (var s in sel) paths.Remove(s);
	}

	void openpath() {
		var s = lpaths.SelectedItem as string;
		if (string.IsNullOrEmpty(s)) return;
		try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + s + "\""); }
		catch { }
	}

	async void go() {
		if (paths.Count == 0) {
			lbstat.Text = Loc.T("codestat.empty");
			tabs.SelectedIndex = 0;
			return;
		}
		var n = ++seq;
		var list = paths.ToList();
		roots = list;
		var rules = (eignore.Text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		var git = cgit.IsChecked == true;
		lbstat.Text = Loc.T("codestat.working");
		bgo.IsEnabled = false;
		tabs.SelectedIndex = 1;
		var nextui = Environment.TickCount - 1000;
		void progress(string phase, int n, string path) {
			var now = Environment.TickCount;
			if (unchecked(now - nextui) < 100 && n % 200 != 0) return;
			nextui = now;
			var text = phase == "count"
				? Loc.T("codestat.progress.count", n, path)
				: Loc.T("codestat.progress.scan", n);
			Dispatcher.BeginInvoke(new Action(() => { if (n == seq) lbstat.Text = text; }));
		}
		CodeStatResult result;
		try {
			result = await Task.Run(() => CodeStat.Run(list, git, rules, CancellationToken.None, progress)).ConfigureAwait(true);
		}
		finally {
			bgo.IsEnabled = true;
		}
		if (n != seq) return;
		last = result;
		showlangs();
	}

	void showlangs() {
		langs.Clear();
		files.Clear();
		if (last == null) return;
		var tf = 0; var tl = 0; var tc = 0; var tm = 0; var tb = 0;
		foreach (var row in last.Langs) {
			if (row.Child) continue;
			tf += row.Files; tl += row.Lines; tc += row.Code; tm += row.Comments; tb += row.Blanks;
		}
		foreach (var row in last.Langs) {
			langs.Add(new CodeStatLangView {
				Name = row.Child ? "    " + row.Name : row.Name,
				Lang = row.Child ? "" : row.Name,
				Files = row.Files,
				Lines = row.Lines,
				Code = row.Code,
				Comments = row.Comments,
				Blanks = row.Blanks,
			});
		}
		langs.Add(new CodeStatLangView {
			Name = Loc.T("codestat.total"),
			Files = tf, Lines = tl, Code = tc, Comments = tm, Blanks = tb, Total = true,
		});
		lbstat.Text = string.IsNullOrEmpty(last.Error)
			? Loc.T("codestat.done", tf, last.Skipped)
			: last.Error;
		showfiles();
	}

	void showfiles() {
		files.Clear();
		if (last == null) return;
		var view = lvlang.SelectedItem as CodeStatLangView;
		var lang = view != null && !view.Total ? view.Lang : "";
		foreach (var row in last.Files) {
			if (lang.Length > 0 && !string.Equals(row.Lang, lang, StringComparison.Ordinal)) continue;
			files.Add(new CodeStatFileView {
				Name = relname(row.Path),
				Path = row.Path,
				Lines = row.Lines,
				Code = row.Code,
			});
		}
	}

	string relname(string path) {
		var best = path;
		foreach (var root in roots) {
			string full, dir;
			try {
				full = Path.GetFullPath(path);
				dir = Directory.Exists(root) ? Path.GetFullPath(root) : Path.GetDirectoryName(Path.GetFullPath(root));
			}
			catch { continue; }
			if (string.IsNullOrEmpty(dir)) continue;
			dir = dir.TrimEnd('\\', '/');
			if (full.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase)
				|| full.StartsWith(dir + "/", StringComparison.OrdinalIgnoreCase)) {
				var rel = full.Substring(dir.Length + 1);
				if (rel.Length < best.Length) best = rel;
			}
		}
		return best;
	}

	void openfile() {
		var row = lvfile.SelectedItem as CodeStatFileView;
		if (row == null || string.IsNullOrEmpty(row.Path)) return;
		try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + row.Path + "\""); }
		catch { }
	}
}
