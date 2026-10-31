using System.Data;
using System.Data.Common;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>工具 → 系统 → 数据库查询：SQLite 拖入即开，也能连 SQL Server；看表、看结构、执行任意 SQL。</summary>
public partial class SqlWindow : Window {
	DbConnection cn;
	string kind = SqlTool.Sqlite;
	bool busy;
	int gen;

	public SqlWindow() {
		InitializeComponent();
		applylang();
		initev();
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
		ecxkind.SelectionChanged += (_, _) => setkind();
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

	void setkind() {
		var mssql = ecxkind.SelectedIndex == 1;
		if (cn != null) disconnect();
		psqlite.Visibility = mssql ? Visibility.Collapsed : Visibility.Visible;
		pmssql.Visibility = mssql ? Visibility.Visible : Visibility.Collapsed;
		kind = mssql ? SqlTool.Mssql : SqlTool.Sqlite;
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
		kind = SqlTool.Sqlite;
		efile.Text = file;
		connect();
	}

	void connect() {
		if (cn != null) {
			disconnect();
			return;
		}
		var k = ecxkind.SelectedIndex == 1 ? SqlTool.Mssql : SqlTool.Sqlite;
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
		else {
			if (string.IsNullOrWhiteSpace(ehost.Text)) {
				lbstat.Text = Loc.T("sql.nohost");
				return;
			}
			conn = SqlTool.MssqlConn(ehost.Text, edb.Text, euser.Text, epw.Password);
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
				lbstat.Text = Loc.T("sql.connected");
				reloadtables(true);
			}));
		});
	}

	void disconnect() {
		var old = cn;
		cn = null;
		kind = SqlTool.Sqlite;
		gen++;
		busy = false;
		busyset(false, Loc.T("sql.disconnected"));
		bconnect.Content = Loc.T("sql.connect");
		ecxkind.IsEnabled = efile.IsEnabled = bfile.IsEnabled = true;
		ehost.IsEnabled = edb.IsEnabled = euser.IsEnabled = epw.IsEnabled = true;
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

	static DataTable tableof(SqlResult r) {
		var dt = new DataTable();
		foreach (var c in r.Columns) dt.Columns.Add(c, typeof(string));
		foreach (var row in r.Rows) dt.Rows.Add(row);
		return dt;
	}
}
