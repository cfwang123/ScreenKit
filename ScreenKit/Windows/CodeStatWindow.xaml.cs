using System.Collections.ObjectModel;
using System.ComponentModel;

namespace ScreenKit;

sealed class CodeStatLangView : INotifyPropertyChanged {
	public event PropertyChangedEventHandler PropertyChanged;
	bool enabled = true;

	public string Name { get; set; }
	public string Lang { get; set; }
	public int Files { get; set; }
	public int Lines { get; set; }
	public int Code { get; set; }
	public int Comments { get; set; }
	public int Blanks { get; set; }
	public bool Total { get; set; }
	public bool Selectable { get; set; }

	/// <summary>勾选后才进入合计，取消勾选立即重算。</summary>
	public bool Enabled {
		get => enabled;
		set {
			if (enabled == value) return;
			enabled = value;
			raise(nameof(Enabled));
			Changed?.Invoke(Lang ?? "", value);
		}
	}

	public Action<string, bool> Changed;

	public void settotals(CodeLangRow row) {
		Files = row.Files;
		Lines = row.Lines;
		Code = row.Code;
		Comments = row.Comments;
		Blanks = row.Blanks;
		raise(nameof(Files));
		raise(nameof(Lines));
		raise(nameof(Code));
		raise(nameof(Comments));
		raise(nameof(Blanks));
	}

	void raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
	readonly HashSet<string> offlangs = new(StringComparer.Ordinal);
	CodeStatResult last;
	CodeStatLangView totalrow;
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
		coluse.Header = Loc.T("codestat.col.use");
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
		foreach (var row in last.Langs) {
			var item = new CodeStatLangView {
				Name = row.Child ? "    " + row.Name : row.Name,
				Lang = row.Child ? "" : row.Name,
				Files = row.Files,
				Lines = row.Lines,
				Code = row.Code,
				Comments = row.Comments,
				Blanks = row.Blanks,
				Selectable = !row.Child,
				Enabled = row.Child || !offlangs.Contains(row.Name),
			};
			item.Changed = (lang, on) => {
				if (lang.Length == 0) return;
				if (on) offlangs.Remove(lang);
				else offlangs.Add(lang);
				sumlangs();
				showfiles();
			};
			langs.Add(item);
		}
		totalrow = new CodeStatLangView {
			Name = Loc.T("codestat.total"),
			Total = true,
		};
		langs.Add(totalrow);
		sumlangs();
		if (!string.IsNullOrEmpty(last.Error)) lbstat.Text = last.Error;
		showfiles();
	}

	void sumlangs() {
		if (last == null) return;
		var sum = CodeStat.Sum(last.Langs, offlangs);
		totalrow?.settotals(sum);
		if (string.IsNullOrEmpty(last.Error))
			lbstat.Text = Loc.T("codestat.done", sum.Files, last.Skipped);
	}

	void showfiles() {
		files.Clear();
		if (last == null) return;
		var view = lvlang.SelectedItem as CodeStatLangView;
		var lang = view != null && !view.Total ? view.Lang : "";
		foreach (var row in last.Files) {
			if (lang.Length > 0) {
				if (!string.Equals(row.Lang, lang, StringComparison.Ordinal)) continue;
			}
			else if (offlangs.Contains(row.Lang)) continue;
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

	/// <summary>双击文件列表：用系统默认程序打开这个文件。</summary>
	void openfile() {
		var row = lvfile.SelectedItem as CodeStatFileView;
		if (row == null || string.IsNullOrEmpty(row.Path)) return;
		try {
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(row.Path) {
				UseShellExecute = true,
			});
		}
		catch { }
	}

	/// <summary>--test-codestat-ui：灌入一次统计，勾掉一种语言看合计和文件列表是否跟着变。</summary>
	internal string SelfTest(string root) {
		paths.Add(root);
		last = CodeStat.Run(new[] { root }, false, null, CancellationToken.None);
		showlangs();
		var pick = langs.FirstOrDefault(x => x.Selectable && x.Files > 0);
		if (pick == null) return "FAIL: 没有可禁用的语言";
		if (totalrow.Selectable) return "FAIL: 合计行不该有勾选框";
		var before = totalrow.Files;
		var filesbefore = files.Count;
		pick.Enabled = false;
		if (totalrow.Files != before - pick.Files)
			return $"FAIL: 合计未随禁用变化 {before}->{totalrow.Files}";
		if (files.Count != filesbefore - pick.Files)
			return $"FAIL: 文件列表未排除禁用的语言 {filesbefore}->{files.Count}";
		pick.Enabled = true;
		if (totalrow.Files != before) return $"FAIL: 恢复勾选后合计不对 {totalrow.Files}";
		return $"OK: {pick.Lang} 禁用后合计 {before}->{totalrow.Files - pick.Files}，恢复 {totalrow.Files}";
	}
}
