using System.Data;
using System.Data.Common;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>工具 → 系统 → 数据库查询：SQLite 拖入即开，也能连 SQL Server / Oracle；看表、看结构、执行任意 SQL。</summary>
public partial class SqlWindow : Window {
	DbConnection cn;
	DbKind dkkind = DbKind.Sqlite;
	string kind = SqlTool.Sqlite;
	bool busy;
	int gen;
	readonly List<DbConnItem> saved = new();
	bool connUiLoading;

	public SqlWindow() {
		InitializeComponent();
		applylang();
		initev();
		initconns();
		setkind(false);
		WindowEsc.Attach(this);
		Closed += (_, _) => {
			gen++;
			var old = cn;
			cn = null;
			try { old?.Dispose(); } catch { }
		};
	}

	void applylang() {
		Title = Loc.T("sql.title");
		lbhint.Text = Loc.T("sql.hint");
		lbtype.Text = Loc.T("sql.type");
		lbfile.Text = Loc.T("sql.file");
		lbhost.Text = Loc.T("sql.host");
		lbdb.Text = Loc.T("sql.db");
		lbuser.Text = Loc.T("sql.user");
		lbpw.Text = Loc.T("sql.pw");
		lbohost.Text = Loc.T("sql.oracle.host");
		lboservice.Text = Loc.T("sql.oracle.service");
		lbouser.Text = Loc.T("sql.user");
		lbopw.Text = Loc.T("sql.pw");
		lbmhost.Text = Loc.T("sql.host");
		lbmdb.Text = Loc.T("sql.db");
		lbmuser.Text = Loc.T("sql.user");
		lbmpw.Text = Loc.T("sql.pw");
		lbconn.Text = Loc.T("sql.conn");
		lbconnname.Text = Loc.T("sql.conn.name");
		bsaveconn.Content = Loc.T("sql.conn.save");
		bdelconn.Content = Loc.T("sql.conn.del");
		ToolBtnUi.Set(bfile, ToolBtnUi.Browse, Loc.T("sql.browse"));
		bconnect.Content = cn == null ? Loc.T("sql.connect") : Loc.T("sql.disconnect");
		lbtables.Text = Loc.T("sql.tables");
		brefresh.Content = Loc.T("sql.refresh");
		bdata.Content = Loc.T("sql.query");
		bschema.Content = Loc.T("sql.schema");
		tabdata.Header = Loc.T("sql.data");
		tabschema.Header = Loc.T("sql.schema");
		lbtop.Text = Loc.T("sql.top");
		brun.Content = Loc.T("sql.run");
		brun.ToolTip = Loc.T("sql.run.tip");
		lbcreate.Text = Loc.T("sql.create");
	}

	void initev() {
		ecxkind.SelectionChanged += (_, _) => setkind(!connUiLoading);
		econn.SelectionChanged += (_, _) => applyconn();
		bsaveconn.Click += (_, _) => saveconn();
		bdelconn.Click += (_, _) => delconn();
		bfile.Click += (_, _) => pickfile();
		bconnect.Click += (_, _) => connect();
		brefresh.Click += (_, _) => reloadtables(false);
		bdata.Click += (_, _) => loadtable();
		bschema.Click += (_, _) => showschema();
		brun.Click += (_, _) => run();
		lstables.MouseDoubleClick += (_, _) => loadtable();
		esql.PreviewKeyDown += (_, e) => {
			if (e.Key == Key.F5 || (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)) {
				e.Handled = true;
				run();
			}
		};
		// 行号：行头显示当前视图里的序号，排序后重新编一次
		dg.LoadingRow += (_, e) => e.Row.Header = (e.Row.GetIndex() + 1).ToString();
		dgs.LoadingRow += (_, e) => e.Row.Header = (e.Row.GetIndex() + 1).ToString();
		dg.Sorting += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => renumber(dg)));
		dgs.Sorting += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => renumber(dgs)));
		etop.Items.Clear();
		etop.Items.Add("100");
		etop.Items.Add("1000");
		etop.Items.Add("5000");
		etop.Items.Add("20000");
		etop.SelectedIndex = 1;
		// 拖放回调要尽快返回，格式只在 DragEnter 问一次，否则资源管理器会等在这里
		bool dropok = false;
		DragEnter += (_, e) => {
			dropok = e.Data.GetDataPresent(DataFormats.FileDrop);
			e.Effects = dropok ? DragDropEffects.Copy : DragDropEffects.None;
			e.Handled = true;
		};
		DragOver += (_, e) => {
			if (!dropok) dropok = e.Data.GetDataPresent(DataFormats.FileDrop);
			e.Effects = dropok ? DragDropEffects.Copy : DragDropEffects.None;
			e.Handled = true;
		};
		DragLeave += (_, e) => {
			dropok = false;
			e.Handled = true;
		};
		Drop += (_, e) => {
			e.Handled = true;
			e.Effects = DragDropEffects.Copy;
			dropok = false;
			if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
			var file = files[0];
			Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => openfile(file)));
		};
	}

	/// <param name="prompt">用户主动切换引擎时提示装驱动；打开窗口或套用已保存连接时只在状态栏说一句。</param>
	void setkind(bool prompt) {
		dkkind = ecxkind.SelectedIndex == 3 ? DbKind.Oracle
			: ecxkind.SelectedIndex == 2 ? DbKind.Mysql
			: ecxkind.SelectedIndex == 1 ? DbKind.Mssql : DbKind.Sqlite;
		kind = dkkind switch {
			DbKind.Oracle => SqlTool.Oracle,
			DbKind.Mysql => SqlTool.Mysql,
			DbKind.Mssql => SqlTool.Mssql,
			_ => SqlTool.Sqlite,
		};
		if (cn != null) disconnect();
		psqlite.Visibility = dkkind == DbKind.Sqlite ? Visibility.Visible : Visibility.Collapsed;
		pmssql.Visibility = dkkind == DbKind.Mssql ? Visibility.Visible : Visibility.Collapsed;
		pmysql.Visibility = dkkind == DbKind.Mysql ? Visibility.Visible : Visibility.Collapsed;
		poracle.Visibility = dkkind == DbKind.Oracle ? Visibility.Visible : Visibility.Collapsed;
		checkdriver(prompt);
	}

	/// <summary>驱动没装就不让连：状态栏提示，需要时弹窗，可直接打开「安装功能」。</summary>
	bool checkdriver(bool prompt) {
		if (DbDriver.Installed(dkkind)) {
			bconnect.IsEnabled = !busy;
			return true;
		}
		bconnect.IsEnabled = false;
		lbstat.Text = Loc.T("sql.driver.missing", DbDriver.NameOf(dkkind));
		if (!prompt) return false;
		if (!DbDriver.Ensure(dkkind, this)) return false;
		bconnect.IsEnabled = !busy;
		lbstat.Text = Loc.T("sql.driver.ready", DbDriver.NameOf(dkkind));
		return true;
	}

	void pickfile() {
		var dlg = new Microsoft.Win32.OpenFileDialog {
			Title = Loc.T("sql.file"),
			Filter = Loc.T("sql.file.filter"),
		};
		if (dlg.ShowDialog(this) != true) return;
		efile.Text = dlg.FileName;
		connect();
	}

	void openfile(string file) {
		if (string.IsNullOrEmpty(file)) return;
		if (ecxkind.SelectedIndex != 0) ecxkind.SelectedIndex = 0;
		else if (cn != null) disconnect();
		dkkind = DbKind.Sqlite;
		kind = SqlTool.Sqlite;
		efile.Text = file;
		connUiLoading = true;
		econn.SelectedItem = null;
		connUiLoading = false;
		connect();
	}

	/// <summary>读 sql-conns.json。列表为空时下拉框只是空着，不影响手填。</summary>
	void initconns() {
		saved.Clear();
		saved.AddRange(DbConnStore.Load());
		econn.ItemsSource = saved;
	}

	static int kindindex(string k) => k switch {
		SqlTool.Mssql => 1,
		SqlTool.Mysql => 2,
		SqlTool.Oracle => 3,
		_ => 0,
	};

	/// <summary>选中已保存的连接：只把参数填进表单，连不连由用户按「连接」。</summary>
	void applyconn() {
		if (connUiLoading) return;
		if (econn.SelectedItem is not DbConnItem it) return;
		connUiLoading = true;
		ecxkind.SelectedIndex = kindindex(it.Kind);
		ecname.Text = it.Name;
		var pw = DbConnStore.Unprotect(it.Secret);
		if (it.Kind == SqlTool.Sqlite) efile.Text = it.File;
		else if (it.Kind == SqlTool.Mssql) {
			ehost.Text = it.Host;
			edb.Text = it.Db;
			euser.Text = it.User;
			epw.Password = pw;
		}
		else if (it.Kind == SqlTool.Mysql) {
			emhost.Text = it.Host;
			emdb.Text = it.Db;
			emuser.Text = it.User;
			empw.Password = pw;
		}
		else {
			eohost.Text = it.Host;
			eoservice.Text = it.Service;
			eouser.Text = it.User;
			eopw.Password = pw;
		}
		connUiLoading = false;
		setkind(false);
		lbstat.Text = Loc.T("sql.conn.applied", it.Name);
	}

	/// <summary>把当前表单存成一条连接；同名覆盖。</summary>
	void saveconn() {
		var name = (ecname.Text ?? "").Trim();
		if (name.Length == 0) {
			lbstat.Text = Loc.T("sql.conn.needname");
			ecname.Focus();
			return;
		}
		var it = new DbConnItem { Name = name, Kind = kind };
		switch (dkkind) {
		case DbKind.Sqlite:
			it.File = (efile.Text ?? "").Trim().Trim('"');
			break;
		case DbKind.Mssql:
			it.Host = ehost.Text.Trim();
			it.Db = edb.Text.Trim();
			it.User = euser.Text.Trim();
			it.Secret = DbConnStore.Protect(epw.Password);
			break;
		case DbKind.Mysql:
			it.Host = emhost.Text.Trim();
			it.Db = emdb.Text.Trim();
			it.User = emuser.Text.Trim();
			it.Secret = DbConnStore.Protect(empw.Password);
			break;
		default:
			it.Host = eohost.Text.Trim();
			it.Service = eoservice.Text.Trim();
			it.User = eouser.Text.Trim();
			it.Secret = DbConnStore.Protect(eopw.Password);
			break;
		}
		var hit = saved.FindIndex(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
		if (hit >= 0) saved[hit] = it;
		else saved.Add(it);
		refillconns(it);
		try {
			DbConnStore.Save(saved);
			lbstat.Text = Loc.T("sql.conn.saved", name);
		}
		catch (Exception ex) {
			lbstat.Text = Loc.T("sql.conn.savefail", ex.Message);
		}
	}

	void delconn() {
		if (econn.SelectedItem is not DbConnItem it) {
			lbstat.Text = Loc.T("sql.conn.needpick");
			return;
		}
		if (MessageBox.Show(this, Loc.T("sql.conn.delask", it.Name), Loc.T("sql.conn.deltitle"),
			MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
		saved.Remove(it);
		refillconns(null);
		try {
			DbConnStore.Save(saved);
			lbstat.Text = Loc.T("sql.conn.deleted", it.Name);
		}
		catch (Exception ex) {
			lbstat.Text = Loc.T("sql.conn.savefail", ex.Message);
		}
	}

	/// <summary>重新绑定列表并选回指定项；换 ItemsSource 时的选择事件不算用户操作。</summary>
	void refillconns(DbConnItem pick) {
		connUiLoading = true;
		econn.ItemsSource = null;
		econn.ItemsSource = saved;
		econn.SelectedItem = pick;
		connUiLoading = false;
	}

	void connect() {
		if (cn != null) {
			disconnect();
			return;
		}
		if (!checkdriver(true)) return;
		var k = kind;
		string conn;
		if (k == SqlTool.Sqlite) {
			var path = (efile.Text ?? "").Trim().Trim('"');
			if (path.Length == 0) {
				lbstat.Text = Loc.T("sql.nofile");
				return;
			}
			if (!File.Exists(path)) {
				lbstat.Text = Loc.T("sql.nofile2", path);
				return;
			}
			conn = SqlTool.SqliteConn(path);
		}
		else if (k == SqlTool.Mssql) {
			if (string.IsNullOrWhiteSpace(ehost.Text)) {
				lbstat.Text = Loc.T("sql.nohost");
				return;
			}
			conn = SqlTool.MssqlConn(ehost.Text, edb.Text, euser.Text, epw.Password);
		}
		else if (k == SqlTool.Mysql) {
			if (string.IsNullOrWhiteSpace(emhost.Text)) {
				lbstat.Text = Loc.T("sql.nohost");
				return;
			}
			if (string.IsNullOrWhiteSpace(emuser.Text)) {
				lbstat.Text = Loc.T("sql.nouser");
				return;
			}
			conn = SqlTool.MysqlConn(emhost.Text, emdb.Text, emuser.Text, empw.Password);
		}
		else {
			if (string.IsNullOrWhiteSpace(eohost.Text)) {
				lbstat.Text = Loc.T("sql.nohost");
				return;
			}
			if (string.IsNullOrWhiteSpace(eouser.Text)) {
				lbstat.Text = Loc.T("sql.nouser");
				return;
			}
			conn = SqlTool.OracleConn(eohost.Text, eoservice.Text, eouser.Text, eopw.Password);
		}
		busyset(true, Loc.T("sql.connecting"));
		var n = ++gen;
		Task.Run(() => {
			var got = SqlTool.Connect(k, conn, out var err);
			Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
				if (n != gen) {
					try { got?.Dispose(); } catch { }
					return;
				}
				busyset(false, "");
				if (got == null) {
					lbstat.Text = Loc.T("sql.fail", err);
					return;
				}
				cn = got;
				kind = k;
				bconnect.Content = Loc.T("sql.disconnect");
				ecxkind.IsEnabled = efile.IsEnabled = bfile.IsEnabled = false;
				ehost.IsEnabled = edb.IsEnabled = euser.IsEnabled = epw.IsEnabled = false;
				emhost.IsEnabled = emdb.IsEnabled = emuser.IsEnabled = empw.IsEnabled = false;
				eohost.IsEnabled = eoservice.IsEnabled = eouser.IsEnabled = eopw.IsEnabled = false;
				lbstat.Text = Loc.T("sql.connected");
				reloadtables(true);
			}));
		});
	}

	void disconnect() {
		var old = cn;
		cn = null;
		gen++;
		busy = false;
		busyset(false, Loc.T("sql.disconnected"));
		bconnect.Content = Loc.T("sql.connect");
		ecxkind.IsEnabled = efile.IsEnabled = bfile.IsEnabled = true;
		ehost.IsEnabled = edb.IsEnabled = euser.IsEnabled = epw.IsEnabled = true;
		emhost.IsEnabled = emdb.IsEnabled = emuser.IsEnabled = empw.IsEnabled = true;
		eohost.IsEnabled = eoservice.IsEnabled = eouser.IsEnabled = eopw.IsEnabled = true;
		checkdriver(false);
		lstables.ItemsSource = null;
		dg.ItemsSource = null;
		dgs.ItemsSource = null;
		ecreate.Text = "";
		try { old?.Dispose(); } catch { }
	}

	void busyset(bool on, string msg) {
		busy = on;
		bconnect.IsEnabled = !on;
		bfile.IsEnabled = !on;
		brefresh.IsEnabled = !on;
		bdata.IsEnabled = !on;
		bschema.IsEnabled = !on;
		brun.IsEnabled = !on;
		if (!string.IsNullOrEmpty(msg)) lbstat.Text = msg;
	}

	void reloadtables(bool quiet) {
		if (cn == null) return;
		if (busy) return;
		var n = ++gen;
		if (!quiet) busyset(true, Loc.T("sql.loading"));
		Task.Run(() => {
			var list = SqlTool.Tables(cn, kind, out var err);
			Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
				if (n != gen) return;
				busyset(false, "");
				if (err.Length > 0) {
					lbstat.Text = Loc.T("sql.fail", err);
					return;
				}
				lstables.ItemsSource = list;
				if (!quiet) lbstat.Text = Loc.T("sql.tables.count", list.Count);
			}));
		});
	}

	string selectedtable() => lstables.SelectedItem as string;

	void loadtable() {
		if (cn == null) {
			lbstat.Text = Loc.T("sql.needconn");
			return;
		}
		var table = selectedtable();
		if (string.IsNullOrEmpty(table)) {
			lbstat.Text = Loc.T("sql.needtable");
			return;
		}
		esql.Text = SqlTool.TableSql(kind, table, toplimit());
		esql.SelectAll();
		run();
	}

	void showschema() {
		if (cn == null) {
			lbstat.Text = Loc.T("sql.needconn");
			return;
		}
		var table = selectedtable();
		if (string.IsNullOrEmpty(table)) {
			lbstat.Text = Loc.T("sql.needtable");
			return;
		}
		if (busy) return;
		var n = ++gen;
		busyset(true, Loc.T("sql.running"));
		Task.Run(() => {
			var r = SqlTool.Schema(cn, kind, table);
			Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
				if (n != gen) return;
				busyset(false, "");
				if (r.Error.Length > 0) {
					lbstat.Text = Loc.T("sql.fail", r.Error);
					return;
				}
				dgs.ItemsSource = tableof(r).DefaultView;
				ecreate.Text = r.Note ?? "";
				lbcreate.Visibility = string.IsNullOrEmpty(ecreate.Text) ? Visibility.Collapsed : Visibility.Visible;
				ecreate.Visibility = lbcreate.Visibility;
				tabs.SelectedItem = tabschema;
				lbstat.Text = Loc.T("sql.okrows", r.Rows.Count, r.Ms);
			}));
		});
	}

	void run() {
		if (cn == null) {
			lbstat.Text = Loc.T("sql.needconn");
			return;
		}
		var sql = (esql.Text ?? "").Trim();
		if (sql.Length == 0) {
			lbstat.Text = Loc.T("sql.nosql");
			return;
		}
		if (busy) return;
		var n = ++gen;
		var top = toplimit();
		busyset(true, Loc.T("sql.running"));
		Task.Run(() => {
			var r = SqlTool.Run(cn, sql, top);
			Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
				if (n != gen) return;
				busyset(false, "");
				if (r.Error.Length > 0) {
					lbstat.Text = Loc.T("sql.fail", r.Error);
					return;
				}
				if (r.Columns.Count > 0) {
					dg.ItemsSource = tableof(r).DefaultView;
					tabs.SelectedItem = tabdata;
					var msg = Loc.T("sql.okrows", r.Rows.Count, r.Ms);
					if (!string.IsNullOrEmpty(r.Note)) msg += "  " + r.Note;
					lbstat.Text = msg;
				}
				else {
					dg.ItemsSource = null;
					lbstat.Text = Loc.T("sql.okaffected", r.Affected, r.Ms);
				}
				// 建表、改名、删表后表名有变化，悄悄重列一次
				Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => reloadtables(true)));
			}));
		});
	}

	int toplimit() {
		if (etop.SelectedItem is not string s) return 1000;
		return int.TryParse(s, out var v) ? v : 1000;
	}

	/// <summary>排序后行不会重新 Loading，显式把已实化的行头刷一遍序号。</summary>
	static void renumber(DataGrid g) {
		for (var i = 0; i < g.Items.Count; i++) {
			if (g.ItemContainerGenerator.ContainerFromIndex(i) is not DataGridRow row) continue;
			row.Header = (i + 1).ToString();
		}
	}

	static DataTable tableof(SqlResult r) {
		var dt = new DataTable();
		foreach (var c in r.Columns) dt.Columns.Add(c, typeof(string));
		foreach (var row in r.Rows) dt.Rows.Add(row);
		return dt;
	}
}
