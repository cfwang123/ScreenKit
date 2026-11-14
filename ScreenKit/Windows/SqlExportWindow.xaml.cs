using System.Windows;

namespace ScreenKit;

/// <summary>数据库查询 → 导出：选范围、格式、目标方言与版本，再落盘。</summary>
public partial class SqlExportWindow : Window {
	sealed class ScopeItem {
		public readonly string Text;
		public readonly SqlExportScope Scope;

		public ScopeItem(string text, SqlExportScope scope) {
			Text = text;
			Scope = scope;
		}

		public override string ToString() => Text;
	}

	sealed class DialectItem {
		public readonly string Text;
		public readonly SqlDialect Dialect;

		public DialectItem(string text, SqlDialect d) {
			Text = text;
			Dialect = d;
		}

		public override string ToString() => Text;
	}

	readonly string table;
	readonly string resultSql;
	readonly string sourceKind;
	string autopath = "";
	bool loading;

	internal SqlExportOptions Result { get; private set; }
	internal bool Cancelled { get; private set; }

	/// <param name="table">当前选中的表，范围选「整库」时用不到。</param>
	/// <param name="resultSql">当前 SQL 文的语句；范围选「当前查询结果」时它就是导出内容。</param>
	public SqlExportWindow(string table, string resultSql, string sourceKind) {
		InitializeComponent();
		this.table = table ?? "";
		this.resultSql = (resultSql ?? "").Trim();
		this.sourceKind = string.IsNullOrEmpty(sourceKind) ? SqlTool.Sqlite : sourceKind;
		initev();
		applylang();
		fillscopes();
		filldialects();
		addialect();
		WindowEsc.Attach(this, () => Cancelled = true);
	}

	void applylang() {
		Title = Loc.T("sql.export.title");
		lbhint.Text = Loc.T("sql.export.hint");
		lbscope.Text = Loc.T("sql.export.scope");
		lbformat.Text = Loc.T("sql.export.format");
		lbdialect.Text = Loc.T("sql.export.dialect");
		lbver.Text = Loc.T("sql.export.version");
		lbtarget.Text = Loc.T("sql.export.path");
		eschema.Content = Loc.T("sql.export.withschema");
		edata.Content = Loc.T("sql.export.withdata");
		bcancel.Content = Loc.T("cancel");
		bok.Content = Loc.T("sql.export.go");
		ToolBtnUi.Set(bbrowse, ToolBtnUi.Folder, Loc.T("sql.export.browse"));
		rfmt.Content = "SQL";
	}

	void initev() {
		escope.SelectionChanged += (_, _) => {
			if (loading) return;
			updatepath(true);
		};
		edialect.SelectionChanged += (_, _) => {
			if (loading) return;
			fillversions();
			updatepath(false);
		};
		ever.SelectionChanged += (_, _) => {
			if (loading) return;
			updatetip();
		};
		rfmt.Checked += (_, _) => onformat();
		rxlsx.Checked += (_, _) => onformat();
		rcsv.Checked += (_, _) => onformat();
		rhtml.Checked += (_, _) => onformat();
		eschema.Checked += (_, _) => updatetip();
		edata.Checked += (_, _) => updatetip();
		bbrowse.Click += (_, _) => browse();
		bok.Click += (_, _) => ok();
		bcancel.Click += (_, _) => {
			Cancelled = true;
			Close();
		};
	}

	void fillscopes() {
		loading = true;
		var items = new List<ScopeItem>();
		if (resultSql.Length > 0)
			items.Add(new ScopeItem(Loc.T("sql.export.scope.result"), SqlExportScope.Result));
		if (table.Length > 0)
			items.Add(new ScopeItem(Loc.T("sql.export.scope.table", table), SqlExportScope.Table));
		items.Add(new ScopeItem(Loc.T("sql.export.scope.db"), SqlExportScope.Database));
		escope.ItemsSource = items;
		escope.SelectedIndex = 0;
		loading = false;
	}

	void filldialects() {
		loading = true;
		var items = new List<DialectItem> {
			new(DialectName(SqlDialect.Sqlite), SqlDialect.Sqlite),
			new(DialectName(SqlDialect.Mssql), SqlDialect.Mssql),
			new(DialectName(SqlDialect.Mysql), SqlDialect.Mysql),
		};
		edialect.ItemsSource = items;
		// 同引擎优先：SQLite 库默认导 SQLite，MySQL 库默认导 MySQL
		var want = sourceKind == SqlTool.Mysql ? SqlDialect.Mysql
			: sourceKind == SqlTool.Mssql ? SqlDialect.Mssql
			: SqlDialect.Sqlite;
		edialect.SelectedIndex = items.FindIndex(x => x.Dialect == want);
		if (edialect.SelectedIndex < 0) edialect.SelectedIndex = 0;
		loading = false;
	}

	static string DialectName(SqlDialect d) => SqlExport.DialectName(d);

	void fillversions() {
		var d = dialect();
		loading = true;
		var list = SqlTargets.List(d);
		ever.ItemsSource = list;
		if (list.Count > 0) ever.SelectedIndex = list.Count - 1;
		loading = false;
		updateversionui();
		updatetip();
	}

	void updateversionui() {
		var on = SqlTargets.Selectable(dialect()) && format() == SqlExportFormat.Sql;
		pver.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
	}

	void onformat() {
		var sql = format() == SqlExportFormat.Sql;
		psql.Visibility = sql ? Visibility.Visible : Visibility.Collapsed;
		if (sql) {
			updateversionui();
			updatetip();
		}
		updatepath(true);
	}

	void addialect() {
		fillversions();
		onformat();
	}

	void updatetip() {
		if (format() != SqlExportFormat.Sql) {
			lbtip.Text = "";
			return;
		}
		var d = dialect();
		var v = version();
		if (d != SqlDialect.Mssql) {
			lbtip.Text = Loc.T("sql.export.tip." + (d == SqlDialect.Mysql ? "mysql" : "sqlite"));
			return;
		}
		lbtip.Text = SqlTargets.Modern(v) ? Loc.T("sql.export.tip.mssql") : Loc.T("sql.export.tip.mssqlold");
	}

	SqlDialect dialect() =>
		edialect.SelectedItem is DialectItem it ? it.Dialect : SqlDialect.Sqlite;

	Version version() {
		if (ever.SelectedItem is SqlTargets.Item it) return it.Version;
		return SqlTargets.Latest(SqlDialect.Mssql);
	}

	SqlExportFormat format() {
		if (rxlsx.IsChecked == true) return SqlExportFormat.Xlsx;
		if (rcsv.IsChecked == true) return SqlExportFormat.Csv;
		if (rhtml.IsChecked == true) return SqlExportFormat.Html;
		return SqlExportFormat.Sql;
	}

	SqlExportScope scope() =>
		escope.SelectedItem is ScopeItem it ? it.Scope : SqlExportScope.Table;

	/// <summary>换格式或范围时，路径还是上次自动填的就重新生成，用户改过的只调扩展名。</summary>
	void updatepath(bool byscope) {
		var f = format();
		var multi = f != SqlExportFormat.Sql && scope() == SqlExportScope.Database;
		var cur = (epath.Text ?? "").Trim();
		var auto = string.Equals(cur, autopath, StringComparison.OrdinalIgnoreCase);
		if (!auto && cur.Length > 0) {
			if (!multi) {
				var ext = SqlExport.ExtOf(f);
				var old = Path.GetExtension(cur);
				epath.Text = known(old) ? Path.ChangeExtension(cur, ext) : cur + ext;
			}
			return;
		}
		autopath = defaultpath(f, scope(), multi);
		epath.Text = autopath;
	}

	static bool known(string ext) {
		if (string.IsNullOrEmpty(ext)) return false;
		return string.Equals(ext, ".sql", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(ext, ".html", StringComparison.OrdinalIgnoreCase);
	}

	string defaultpath(SqlExportFormat f, SqlExportScope s, bool multi) {
		var name = s == SqlExportScope.Result ? "query" : table;
		name = SqlExport.SafeName(string.IsNullOrWhiteSpace(name) ? "export" : name);
		var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ScreenKit");
		try { Directory.CreateDirectory(dir); }
		catch { dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
		return multi ? Path.Combine(dir, name) : Path.Combine(dir, name + SqlExport.ExtOf(f));
	}

	void browse() {
		var f = format();
		var s = scope();
		if (f != SqlExportFormat.Sql && s == SqlExportScope.Database) {
			using var dlg = new System.Windows.Forms.FolderBrowserDialog {
				Description = Loc.T("sql.export.path"),
				ShowNewFolderButton = true,
			};
			var cur = (epath.Text ?? "").Trim();
			if (cur.Length > 0 && Directory.Exists(cur)) dlg.SelectedPath = cur;
			if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) epath.Text = dlg.SelectedPath;
			return;
		}
		var ext = SqlExport.ExtOf(f);
		var save = new Microsoft.Win32.SaveFileDialog {
			Title = Loc.T("sql.export.path"),
			Filter = $"{MainWindowTitle(f)} (*{ext})|*{ext}|{Loc.T("doc2md.allfiles")} (*.*)|*.*",
			FilterIndex = 1,
			AddExtension = true,
			DefaultExt = ext,
			OverwritePrompt = true,
		};
		var cur2 = (epath.Text ?? "").Trim();
		if (cur2.Length > 0) {
			try { save.FileName = Path.GetFileName(cur2); } catch { }
			try {
				var d2 = Path.GetDirectoryName(cur2);
				if (!string.IsNullOrEmpty(d2)) save.InitialDirectory = d2;
			}
			catch { }
		}
		if (save.ShowDialog(this) == true) {
			epath.Text = save.FileName;
			if (f != SqlExportFormat.Sql) {
				var want = SqlExport.ExtOf(f);
				if (!epath.Text.EndsWith(want, StringComparison.OrdinalIgnoreCase))
					epath.Text = epath.Text.TrimEnd('.') + want;
			}
		}
	}

	static string MainWindowTitle(SqlExportFormat f) => f switch {
		SqlExportFormat.Xlsx => "Excel",
		SqlExportFormat.Csv => "CSV",
		SqlExportFormat.Html => "HTML",
		_ => "SQL",
	};

	void ok() {
		var path = (epath.Text ?? "").Trim().Trim('"');
		if (path.Length == 0) {
			stat(Loc.T("sql.export.nopath"));
			return;
		}
		var f = format();
		var s = scope();
		var multi = f != SqlExportFormat.Sql && s == SqlExportScope.Database;
		if (!multi && f == SqlExportFormat.Sql) {
			var ext = SqlExport.ExtOf(f);
			if (!path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) path += ext;
		}
		if (!multi && f != SqlExportFormat.Sql) {
			var ext = SqlExport.ExtOf(f);
			if (!path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) path = path.TrimEnd('.') + ext;
		}
		Result = new SqlExportOptions {
			Format = f,
			Scope = s,
			Dialect = dialect(),
			Version = version(),
			Kind = sourceKind,
			WithSchema = eschema.IsChecked == true,
			WithData = edata.IsChecked == true,
			Table = table,
			ResultSql = s == SqlExportScope.Result ? resultSql : "",
			Path = path,
		};
		if (s == SqlExportScope.Result && resultSql.Length == 0) {
			stat(Loc.T("sql.export.noresult"));
			return;
		}
		if (f == SqlExportFormat.Sql && !Result.WithSchema && !Result.WithData) {
			stat(Loc.T("sql.export.nothing"));
			return;
		}
		DialogResult = true;
		Close();
	}

	void stat(string text) => lbstat.Text = text ?? "";
}
