using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows.Data;

namespace ScreenKit;

/// <summary>工具 → 系统 → 系统工具。进程、文件占用、环境变量、启动项、已安装程序。只读。</summary>
public partial class SysToolWindow : Window {
	List<SysTool.ProcRow> procs = new();
	List<SysTool.LockRow> locks = new();
	List<SysTool.EnvRow> envs = new();
	List<SysTool.StartRow> starts = new();
	List<SysTool.AppRow> apps = new();
	readonly HashSet<string> loaded = new();
	int procgen;
	int lockgen;
	int envgen;
	int startgen;
	int appgen;

	public SysToolWindow() {
		InitializeComponent();
		applylang();
		initev();
		initmore();
		initsort();
		WindowEsc.Attach(this);
		ensure();
	}

	sealed class ProcItem {
		public string Name { get; set; }
		public int Pid { get; set; }
		public string Mem { get; set; }
		public ulong MemBytes { get; set; }
		public string Bits { get; set; }
		public string Admin { get; set; }
		public string Path { get; set; }
		public string Cmd { get; set; }
	}

	sealed class LockItem {
		public string Name { get; set; }
		public int Pid { get; set; }
		public string Path { get; set; }
	}

	sealed class EnvItem {
		public string Scope { get; set; }
		public string Name { get; set; }
		public string Value { get; set; }
	}

	sealed class StartItem {
		public string Source { get; set; }
		public string Name { get; set; }
		public string Cmd { get; set; }
	}

	sealed class AppItem {
		public string Name { get; set; }
		public string Ver { get; set; }
		public string Pub { get; set; }
		public string Date { get; set; }
		public string Scope { get; set; }
		public string Uninstall { get; set; }
	}

	void initev() {
		bprocrefresh.Click += (_, _) => loadproc();
		bproccopy.Click += (_, _) => copyproc();
		bprocopen.Click += (_, _) => openproc();
		eproc.TextChanged += (_, _) => paintproc();
		lvproc.SelectionChanged += (_, _) => showproc();
		blockfind.Click += (_, _) => loadlock();
		blockfile.Click += (_, _) => pickfile();
		blockdir.Click += (_, _) => pickdir();
		lvlock.SelectionChanged += (_, _) => showlock();
		benvrefresh.Click += (_, _) => loadenv();
		benvcopy.Click += (_, _) => copyenv();
		eenv.TextChanged += (_, _) => paintenv();
		lvenv.SelectionChanged += (_, _) => showenv();
		bstartrefresh.Click += (_, _) => loadstart();
		bstartcopy.Click += (_, _) => copystart();
		estart.TextChanged += (_, _) => paintstart();
		lvstart.SelectionChanged += (_, _) => showstart();
		bapprefresh.Click += (_, _) => loadapp();
		bappcopy.Click += (_, _) => copyapp();
		eapp.TextChanged += (_, _) => paintapp();
		lvapp.SelectionChanged += (_, _) => showapp();
		tabs.SelectionChanged += (_, e) => {
			if (e.Source == tabs) ensure();
		};
		var plock = tablock.Content as Grid;
		if (plock != null) {
			plock.AllowDrop = true;
			plock.DragOver += ondrag;
			plock.Drop += ondrop;
		}
	}

	void applylang() {
		Title = Loc.T("systool.title");
		tabproc.Header = Loc.T("systool.tab.proc");
		tablock.Header = Loc.T("systool.tab.lock");
		tabenv.Header = Loc.T("systool.tab.env");
		tabstart.Header = Loc.T("systool.tab.start");
		tabapp.Header = Loc.T("systool.tab.app");
		lbprochint.Text = Loc.T("systool.hint.proc");
		lblockhint.Text = Loc.T("systool.hint.lock");
		lbenvhint.Text = Loc.T("systool.hint.env");
		lbstarthint.Text = Loc.T("systool.hint.start");
		lbapphint.Text = Loc.T("systool.hint.app");
		lbprocfilter.Text = Loc.T("systool.filter");
		lbenvfilter.Text = Loc.T("systool.filter");
		lbstartfilter.Text = Loc.T("systool.filter");
		lbappfilter.Text = Loc.T("systool.filter");
		colprocname.Header = Loc.T("systool.col.name");
		colprocpid.Header = Loc.T("systool.col.pid");
		colprocmem.Header = Loc.T("systool.col.mem");
		colprocbits.Header = Loc.T("systool.col.bits");
		colprocelev.Header = Loc.T("systool.col.elev");
		colprocpath.Header = Loc.T("systool.col.path");
		colproccmd.Header = Loc.T("systool.col.cmd");
		collockname.Header = Loc.T("systool.col.name");
		collockpid.Header = Loc.T("systool.col.pid");
		collockpath.Header = Loc.T("systool.col.path");
		colenvscope.Header = Loc.T("systool.col.scope");
		colenvname.Header = Loc.T("systool.col.name");
		colenvvalue.Header = Loc.T("systool.col.value");
		colstartsrc.Header = Loc.T("systool.col.source");
		colstartname.Header = Loc.T("systool.col.name");
		colstartcmd.Header = Loc.T("systool.col.cmd");
		colappname.Header = Loc.T("systool.col.name");
		colappver.Header = Loc.T("systool.col.ver");
		colapppub.Header = Loc.T("systool.col.pub");
		colappdate.Header = Loc.T("systool.col.date");
		colappscope.Header = Loc.T("systool.col.scope");
		ToolBtnUi.Set(bprocrefresh, ToolBtnUi.Swap, Loc.T("systool.refresh"));
		ToolBtnUi.Set(bproccopy, ToolBtnUi.Copy, Loc.T("systool.copy"));
		ToolBtnUi.Set(bprocopen, ToolBtnUi.Folder, Loc.T("systool.open"));
		ToolBtnUi.Set(blockfile, ToolBtnUi.Browse, Loc.T("systool.file"));
		ToolBtnUi.Set(blockdir, ToolBtnUi.Folder, Loc.T("systool.dir"));
		ToolBtnUi.Set(blockfind, ToolBtnUi.Swap, Loc.T("systool.find"));
		ToolBtnUi.Set(benvrefresh, ToolBtnUi.Swap, Loc.T("systool.refresh"));
		ToolBtnUi.Set(benvcopy, ToolBtnUi.Copy, Loc.T("systool.copy"));
		ToolBtnUi.Set(bstartrefresh, ToolBtnUi.Swap, Loc.T("systool.refresh"));
		ToolBtnUi.Set(bstartcopy, ToolBtnUi.Copy, Loc.T("systool.copy"));
		ToolBtnUi.Set(bapprefresh, ToolBtnUi.Swap, Loc.T("systool.refresh"));
		ToolBtnUi.Set(bappcopy, ToolBtnUi.Copy, Loc.T("systool.copy"));
		applymore();
	}

	void ensure() {
		if (tabs.SelectedItem == tabproc && loaded.Add("proc")) loadproc();
		else if (tabs.SelectedItem == tabenv && loaded.Add("env")) loadenv();
		else if (tabs.SelectedItem == tabstart && loaded.Add("start")) loadstart();
		else if (tabs.SelectedItem == tabapp && loaded.Add("app")) loadapp();
		else if (tabs.SelectedItem == tabsvc && loaded.Add("svc")) loadsvc();
		else if (tabs.SelectedItem == tabpatch && loaded.Add("patch")) loadpatch();
		else if (tabs.SelectedItem == tabstore && loaded.Add("store")) loadstore();
		else if (tabs.SelectedItem == tabassoc && loaded.Add("assoc")) loadassoc();
	}

	void loadproc() {
		var n = ++procgen;
		bprocrefresh.IsEnabled = false;
		lbprocstat.Text = Loc.T("systool.working");
		Task.Run(() => {
			string err = null;
			List<SysTool.ProcRow> got = null;
			try { got = SysTool.Procs(); }
			catch (Exception ex) { err = ex.Message; }
			Dispatcher.Invoke(() => {
				if (n != procgen) return;
				bprocrefresh.IsEnabled = true;
				if (err != null) {
					lbprocstat.Text = err;
					return;
				}
				procs = got ?? new List<SysTool.ProcRow>();
				paintproc();
			});
		});
	}

	void paintproc() {
		var q = (eproc.Text ?? "").Trim();
		var view = new List<ProcItem>();
		foreach (var r in procs) {
			var it = new ProcItem {
				Name = r.Name, Pid = r.Pid, Mem = r.Mem, MemBytes = r.MemBytes, Path = r.Path, Cmd = r.Cmd,
				Bits = procbits(r.Bits), Admin = procelev(r.Elev),
			};
			if (q.Length == 0 || has(q, it.Name, it.Path, it.Cmd, it.Pid.ToString(), it.Mem, it.Bits, it.Admin))
				view.Add(it);
		}
		fill(lvproc, view);
		lbprocstat.Text = Loc.T("systool.stat", view.Count, procs.Count);
		showproc();
	}

	void showproc() {
		if (lvproc.SelectedItem is not ProcItem it) {
			lbprocinfo.Text = "";
			eproccmd.Text = "";
			bprocopen.IsEnabled = false;
			return;
		}
		bprocopen.IsEnabled = File.Exists(it.Path);
		lbprocinfo.Text = join($"{it.Name}  {it.Pid}  {it.Mem}", it.Path, procmeta(it.Bits, it.Admin));
		eproccmd.Text = noquote(it.Cmd);
	}

	void copyproc() {
		if (lvproc.SelectedItem is not ProcItem it) return;
		setclip(join($"{it.Name}  {it.Pid}  {it.Mem}", it.Path, procmeta(it.Bits, it.Admin), noquote(it.Cmd)));
	}

	static string procmeta(string bits, string admin) => $"{bits}  {admin}".Trim();

	static string procbits(int bits) => label($"systool.proc.bit{bits}", $"{bits} 位");

	static string procelev(int elev) {
		if (elev < 0) return "";
		return elev == 0 ? label("systool.proc.normal", "普通用户") : label("systool.proc.admin", "管理员");
	}

	void openproc() {
		if (lvproc.SelectedItem is not ProcItem it) return;
		openpath(it.Path);
	}

	void pickfile() {
		var dlg = new Microsoft.Win32.OpenFileDialog { Title = Loc.T("systool.file") };
		if (dlg.ShowDialog(this) == true) {
			elock.Text = dlg.FileName;
			loadlock();
		}
	}

	void pickdir() {
		using var dlg = new System.Windows.Forms.FolderBrowserDialog();
		if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
			elock.Text = dlg.SelectedPath;
			loadlock();
		}
	}

	void ondrag(object sender, DragEventArgs e) {
		e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}

	void ondrop(object sender, DragEventArgs e) {
		if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
		elock.Text = files[0];
		loadlock();
	}

	void loadlock() {
		var path = (elock.Text ?? "").Trim();
		var n = ++lockgen;
		blockfind.IsEnabled = false;
		lblockstat.Text = Loc.T("systool.working");
		Task.Run(() => {
			string err = null;
			List<SysTool.LockRow> got = null;
			try { got = SysTool.Locks(path, out err); }
			catch (Exception ex) { err = ex.Message; }
			Dispatcher.Invoke(() => {
				if (n != lockgen) return;
				blockfind.IsEnabled = true;
				locks = got ?? new List<SysTool.LockRow>();
				var view = new List<LockItem>();
				foreach (var r in locks)
					view.Add(new LockItem { Name = r.Name, Pid = r.Pid, Path = r.Path });
				fill(lvlock, view);
				if (err == "empty") lblockstat.Text = Loc.T("systool.lock.empty");
				else if (err == "missing") lblockstat.Text = Loc.T("systool.lock.missing");
				else if (!string.IsNullOrEmpty(err)) lblockstat.Text = err;
				else lblockstat.Text = Loc.T("systool.stat", view.Count, view.Count);
				showlock();
			});
		});
	}

	void showlock() {
		if (lvlock.SelectedItem is not LockItem it) {
			elockdetail.Text = "";
			return;
		}
		elockdetail.Text = join($"{it.Name}  {it.Pid}", it.Path);
	}

	void loadenv() {
		var n = ++envgen;
		benvrefresh.IsEnabled = false;
		lbenvstat.Text = Loc.T("systool.working");
		Task.Run(() => {
			string err = null;
			string warn = null;
			List<SysTool.EnvRow> got = null;
			try { got = SysTool.Env(out warn); }
			catch (Exception ex) { err = ex.Message; }
			Dispatcher.Invoke(() => {
				if (n != envgen) return;
				benvrefresh.IsEnabled = true;
				if (err != null) {
					lbenvstat.Text = err;
					return;
				}
				envs = got ?? new List<SysTool.EnvRow>();
				paintenv();
				if (!string.IsNullOrEmpty(warn)) lbenvstat.Text = $"{lbenvstat.Text}  {warn}";
			});
		});
	}

	void paintenv() {
		var q = (eenv.Text ?? "").Trim();
		var view = new List<EnvItem>();
		foreach (var r in envs) {
			var it = new EnvItem { Scope = scope(r.Scope), Name = r.Name, Value = r.Value };
			if (q.Length == 0 || has(q, it.Scope, it.Name, it.Value)) view.Add(it);
		}
		fill(lvenv, view);
		lbenvstat.Text = Loc.T("systool.stat", view.Count, envs.Count);
		showenv();
	}

	void showenv() {
		if (lvenv.SelectedItem is not EnvItem it) {
			eenvdetail.Text = "";
			return;
		}
		var value = it.Value ?? "";
		if (value.IndexOf(';') >= 0) value = value.Replace(";", "\n");
		eenvdetail.Text = join($"{it.Scope}  {it.Name}", value);
	}

	void copyenv() {
		if (lvenv.SelectedItem is not EnvItem it) return;
		setclip($"{it.Name}={it.Value}");
	}

	void loadstart() {
		var n = ++startgen;
		bstartrefresh.IsEnabled = false;
		lbstartstat.Text = Loc.T("systool.working");
		Task.Run(() => {
			string err = null;
			List<SysTool.StartRow> got = null;
			try { got = SysTool.Startup(); }
			catch (Exception ex) { err = ex.Message; }
			Dispatcher.Invoke(() => {
				if (n != startgen) return;
				bstartrefresh.IsEnabled = true;
				if (err != null) {
					lbstartstat.Text = err;
					return;
				}
				starts = got ?? new List<SysTool.StartRow>();
				paintstart();
			});
		});
	}

	void paintstart() {
		var q = (estart.Text ?? "").Trim();
		var view = new List<StartItem>();
		foreach (var r in starts) {
			var it = new StartItem { Source = src(r.Source), Name = r.Name, Cmd = r.Cmd };
			if (q.Length == 0 || has(q, it.Source, it.Name, it.Cmd)) view.Add(it);
		}
		fill(lvstart, view);
		lbstartstat.Text = Loc.T("systool.stat", view.Count, starts.Count);
		showstart();
	}

	void showstart() {
		if (lvstart.SelectedItem is not StartItem it) {
			estartdetail.Text = "";
			return;
		}
		estartdetail.Text = join($"{it.Source}  {it.Name}", it.Cmd);
	}

	void copystart() {
		if (lvstart.SelectedItem is not StartItem it) return;
		setclip(join($"{it.Source}  {it.Name}", it.Cmd));
	}

	void loadapp() {
		var n = ++appgen;
		bapprefresh.IsEnabled = false;
		lbappstat.Text = Loc.T("systool.working");
		Task.Run(() => {
			string err = null;
			List<SysTool.AppRow> got = null;
			try { got = SysTool.Apps(); }
			catch (Exception ex) { err = ex.Message; }
			Dispatcher.Invoke(() => {
				if (n != appgen) return;
				bapprefresh.IsEnabled = true;
				if (err != null) {
					lbappstat.Text = err;
					return;
				}
				apps = got ?? new List<SysTool.AppRow>();
				paintapp();
			});
		});
	}

	void paintapp() {
		var q = (eapp.Text ?? "").Trim();
		var view = new List<AppItem>();
		foreach (var r in apps) {
			var it = new AppItem {
				Name = r.Name, Ver = r.Ver, Pub = r.Pub, Date = r.Date,
				Scope = scope(r.Scope), Uninstall = r.Uninstall,
			};
			if (q.Length == 0 || has(q, it.Name, it.Ver, it.Pub, it.Date, it.Scope, it.Uninstall)) view.Add(it);
		}
		fill(lvapp, view);
		lbappstat.Text = Loc.T("systool.stat", view.Count, apps.Count);
		showapp();
	}

	void showapp() {
		if (lvapp.SelectedItem is not AppItem it) {
			eappdetail.Text = "";
			return;
		}
		eappdetail.Text = join($"{it.Name}  {it.Ver}", it.Pub, it.Date, it.Scope, it.Uninstall);
	}

	void copyapp() {
		if (lvapp.SelectedItem is not AppItem it) return;
		setclip(join($"{it.Name}  {it.Ver}", it.Pub, it.Date, it.Scope, it.Uninstall));
	}

	string scope(string key) {
		var id = $"systool.scope.{key}";
		var t = Loc.T(id);
		return t == id ? key : t;
	}

	string src(string key) {
		var id = $"systool.src.{key}";
		var t = Loc.T(id);
		return t == id ? key : t;
	}

	// ==================== 点列头排序 ====================
	sealed class SortState {
		public GridViewColumn Col { get; set; }
		public string Key { get; set; }
		public string Raw { get; set; }
		public ListSortDirection Dir { get; set; }
	}

	readonly Dictionary<ListView, SortState> sorts = new();

	void initsort() {
		foreach (var lv in new[] { lvproc, lvlock, lvenv, lvstart, lvapp, lvsvc, lvpatch, lvstore, lvassoc })
			lv.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(onheadclick));
	}

	// 第一次点升序，再点同一列倒序；换列从升序开始。
	void onheadclick(object sender, RoutedEventArgs e) {
		if (e.OriginalSource is not GridViewColumnHeader head) return;
		if (head.Role != GridViewColumnHeaderRole.Normal) return;
		var col = head.Column;
		if (col == null || sender is not ListView lv) return;
		var key = GridViewSort.GetSortPath(col);
		if (string.IsNullOrEmpty(key) && col.DisplayMemberBinding is Binding bd) key = bd.Path?.Path;
		if (string.IsNullOrEmpty(key)) return;
		sorts.TryGetValue(lv, out var st);
		var same = st != null && ReferenceEquals(st.Col, col);
		var dir = same && st.Dir == ListSortDirection.Ascending
			? ListSortDirection.Descending : ListSortDirection.Ascending;
		var raw = same ? st.Raw : (col.Header as string ?? "");
		if (st != null && !same) st.Col.Header = st.Raw;
		col.Header = raw + (dir == ListSortDirection.Ascending ? " ▲" : " ▼");
		sorts[lv] = new SortState { Col = col, Key = key, Raw = raw, Dir = dir };
		applysort(lv, key, dir);
	}

	static void applysort(ListView lv, string key, ListSortDirection dir) {
		if (lv.ItemsSource == null) return;
		var view = CollectionViewSource.GetDefaultView(lv.ItemsSource);
		if (view == null) return;
		using (view.DeferRefresh()) {
			view.SortDescriptions.Clear();
			view.SortDescriptions.Add(new SortDescription(key, dir));
		}
	}

	// 换筛选结果后重新套用当前排序。
	void fill<T>(ListView lv, List<T> rows) {
		lv.ItemsSource = rows;
		if (sorts.TryGetValue(lv, out var st)) applysort(lv, st.Key, st.Dir);
	}

	static bool has(string q, params string[] parts) {
		foreach (var p in parts)
			if (!string.IsNullOrEmpty(p) && p.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		return false;
	}

	static string join(params string[] parts) {
		var sb = new StringBuilder();
		foreach (var p in parts) {
			if (string.IsNullOrEmpty(p)) continue;
			if (sb.Length > 0) sb.Append('\n');
			sb.Append(p);
		}
		return sb.ToString();
	}

	// 命令行里的引号去掉，方便逐项看和复制。
	static string noquote(string cmd) {
		if (string.IsNullOrEmpty(cmd)) return "";
		return cmd.Replace("\"", "");
	}

	static void setclip(string text) {
		if (string.IsNullOrEmpty(text)) return;
		try { Clipboard.SetText(text); } catch { }
	}

	static void openpath(string path) {
		if (!File.Exists(path)) return;
		try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
	}
}
