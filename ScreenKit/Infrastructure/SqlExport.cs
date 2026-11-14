using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace ScreenKit;

/// <summary>导出 SQL 脚本的目标方言。</summary>
enum SqlDialect {
	Sqlite,
	Mysql,
	Mssql,
}

/// <summary>与具体引擎无关的列类型，落成建表语句时再换成各方言写法。</summary>
enum SqlTypeKind {
	Int,
	BigInt,
	Decimal,
	Float,
	Bool,
	Date,
	Time,
	DateTime,
	Text,
	Blob,
	Guid,
}

enum SqlExportFormat {
	Sql,
	Xlsx,
	Csv,
	Html,
}

enum SqlExportScope {
	Result,
	Table,
	Database,
}

sealed class SqlExportColumn {
	public string Name = "";
	public int Size;
	public SqlTypeKind Kind;
	/// <summary>源库里的原始类型名，同引擎导出时原样写回。</summary>
	public string SourceType = "";
	public bool Nullable = true;
	public bool PrimaryKey;
	public bool Identity;
	/// <summary>原始默认值表达式，解析不了就留空。</summary>
	public string Default = "";
}

sealed class SqlExportIndex {
	public string Name = "";
	public bool Unique;
	public List<string> Columns = new();
}

sealed class SqlExportTable {
	public string Name = "";
	public List<SqlExportColumn> Columns = new();
	public List<SqlExportIndex> Indexes = new();
}

sealed class SqlExportOptions {
	public SqlExportFormat Format = SqlExportFormat.Sql;
	public SqlExportScope Scope = SqlExportScope.Table;
	public SqlDialect Dialect = SqlDialect.Sqlite;
	/// <summary>目标 SQL Server / MySQL 版本；null 按最新处理。</summary>
	public Version Version = SqlTargets.Latest(SqlDialect.Mssql);
	/// <summary>源库引擎，决定取表结构和引号的方式。</summary>
	public string Kind = SqlTool.Sqlite;
	public bool WithSchema = true;
	public bool WithData = true;
	public string Table = "";
	/// <summary>范围是当前查询结果时必填。</summary>
	public string ResultSql = "";
	/// <summary>SQL 格式是文件；整库导出表格格式时是目录。</summary>
	public string Path = "";
}

enum SqlExportPhase {
	Schema,
	Data,
	Index,
	Done,
}

sealed class SqlExportProgress {
	/// <summary>当前第几张表，从 1 开始。</summary>
	public int Index;
	public int Total;
	public string Table = "";
	/// <summary>本次导出累计已写行数。</summary>
	public long Rows;
	/// <summary>当前表已写行数。</summary>
	public long TableRows;
	/// <summary>当前表总行数，0 表示未知。</summary>
	public long TableTotal;
	public SqlExportPhase Phase;
}

sealed class SqlExportResult {
	public int Files;
	public long Rows;
	public int Ms;
	public bool Cancelled;
	public string Note = "";
	public string Error = "";
}

/// <summary>一次导出任务的共享状态：结果集、进度上报、取消标记。</summary>
sealed class SqlExportJob {
	public readonly SqlExportResult Result = new();
	public readonly IProgress<SqlExportProgress> Sink;
	public readonly CancellationToken Token;
	int tick;

	public SqlExportJob(IProgress<SqlExportProgress> sink, CancellationToken token) {
		Sink = sink;
		Token = token;
		tick = Environment.TickCount;
	}

	public void cancel() => Token.ThrowIfCancellationRequested();

	/// <summary>进度每 60ms 最多报一次，行数照常累计；收尾用 force。</summary>
	public void report(SqlExportPhase phase, int index, int total, string table, long tableRows, long tableTotal, bool force = false) {
		cancel();
		if (Sink == null) return;
		var now = Environment.TickCount;
		if (!force && now - tick < 60) return;
		tick = now;
		Sink.Report(new SqlExportProgress {
			Index = index, Total = total, Table = table, Phase = phase,
			Rows = Result.Rows, TableRows = tableRows, TableTotal = tableTotal,
		});
	}
}

/// <summary>各目标引擎可选的版本，以及按版本要降级的语法。</summary>
static class SqlTargets {
	public static Version Latest(SqlDialect d) => d switch {
		SqlDialect.Mssql => new Version(16, 0),
		SqlDialect.Mysql => new Version(8, 0),
		_ => new Version(3, 0),
	};

	/// <summary>下拉里的一项：显示名 + 版本。</summary>
	public sealed class Item {
		public readonly string Text;
		public readonly Version Version;

		public Item(string text, Version version) {
			Text = text;
			Version = version;
		}

		public override string ToString() => Text;
	}

	/// <summary>SQL Server 按年份列全部受支持的版本；MySQL、SQLite 的导出脚本各版本一致。</summary>
	public static List<Item> List(SqlDialect d) {
		if (d == SqlDialect.Mssql) {
			return new List<Item> {
				new("SQL Server 2005", new Version(9, 0)),
				new("SQL Server 2008", new Version(10, 0)),
				new("SQL Server 2012", new Version(11, 0)),
				new("SQL Server 2014", new Version(12, 0)),
				new("SQL Server 2016", new Version(13, 0)),
				new("SQL Server 2017", new Version(14, 0)),
				new("SQL Server 2019", new Version(15, 0)),
				new("SQL Server 2022", new Version(16, 0)),
			};
		}
		if (d == SqlDialect.Mysql)
			return new List<Item> { new(Loc.T("sql.export.ver.mysql"), new Version(5, 5)) };
		return new List<Item> { new("SQLite 3", new Version(3, 0)) };
	}

	/// <summary>版本下拉只有在选项真的会影响脚本时才能改。</summary>
	public static bool Selectable(SqlDialect d) => d == SqlDialect.Mssql;

	/// <summary>date / time / datetime2 / datetimeoffset 与多行 VALUES 都是 2008 起。</summary>
	public static bool Modern(Version v) => v == null || v >= new Version(10, 0);

	/// <summary>DROP TABLE IF EXISTS 是 2016 起；更早要 IF OBJECT_ID(...)。</summary>
	public static bool DropIfExists(Version v) => v == null || v >= new Version(13, 0);
}

/// <summary>
/// 把表或查询结果写成 SQL 脚本（SQLite / MySQL / SQL Server 三种方言，SQL Server 可降到 2005）
/// 或 xlsx / csv / html。只读源库，边读边写，不把整表攒进内存。
/// </summary>
static class SqlExport {
	public static string ExtOf(SqlExportFormat f) => f switch {
		SqlExportFormat.Xlsx => ".xlsx",
		SqlExportFormat.Csv => ".csv",
		SqlExportFormat.Html => ".html",
		_ => ".sql",
	};

	public static string DialectName(SqlDialect d) => d switch {
		SqlDialect.Mssql => "SQL Server",
		SqlDialect.Mysql => "MySQL",
		_ => "SQLite",
	};

	/// <summary>源库里的基表；视图导不出建表语句，不算。</summary>
	public static List<string> Tables(DbConnection cn, string kind, out string err) {
		err = "";
		var list = new List<string>();
		if (cn == null) return list;
		string sql;
		if (kind == SqlTool.Mssql)
			sql = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME";
		else if (kind == SqlTool.Oracle)
			sql = "SELECT TABLE_NAME FROM USER_TABLES ORDER BY TABLE_NAME";
		else if (kind == SqlTool.Mysql)
			sql = "SELECT TABLE_NAME FROM information_schema.tables WHERE table_schema = DATABASE() " +
				"AND table_type = 'BASE TABLE' ORDER BY TABLE_NAME";
		else
			sql = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
		try {
			using var cmd = cn.CreateCommand();
			cmd.CommandText = sql;
			using var rd = cmd.ExecuteReader();
			while (rd.Read()) list.Add(Convert.ToString(rd.GetValue(0), CultureInfo.InvariantCulture));
		}
		catch (Exception ex) {
			err = ex.Message;
		}
		return list;
	}

	public static SqlExportResult Run(DbConnection cn, SqlExportOptions o, IProgress<SqlExportProgress> progress = null,
		CancellationToken token = default) {
		var job = new SqlExportJob(progress, token);
		var res = job.Result;
		var tick = Environment.TickCount;
		if (cn == null) {
			res.Error = Loc.T("sql.needconn");
			return res;
		}
		if (o == null || string.IsNullOrWhiteSpace(o.Path)) {
			res.Error = Loc.T("sql.export.nopath");
			return res;
		}
		if (o.Format == SqlExportFormat.Sql && !o.WithSchema && !o.WithData) {
			res.Error = Loc.T("sql.export.nothing");
			return res;
		}
		try {
			if (o.Format == SqlExportFormat.Sql) sqlfile(cn, o, job);
			else tablefiles(cn, o, job);
		}
		catch (OperationCanceledException) {
			res.Cancelled = true;
		}
		catch (Exception ex) {
			res.Error = ex.Message;
		}
		res.Ms = Environment.TickCount - tick;
		return res;
	}

	// ==================== SQL 脚本 ====================

	static void sqlfile(DbConnection cn, SqlExportOptions o, SqlExportJob job) {
		var res = job.Result;
		var isresult = o.Scope == SqlExportScope.Result;
		var names = new List<string>();
		if (isresult) {
			if (string.IsNullOrWhiteSpace(o.ResultSql)) {
				res.Error = Loc.T("sql.export.noresult");
				return;
			}
			names.Add(string.IsNullOrWhiteSpace(o.Table) ? "query" : o.Table);
		}
		else if (o.Scope == SqlExportScope.Table) {
			if (string.IsNullOrWhiteSpace(o.Table)) {
				res.Error = Loc.T("sql.needtable");
				return;
			}
			names.Add(o.Table);
		}
		else {
			names = Tables(cn, o.Kind, out var terr);
			if (terr.Length > 0) {
				res.Error = terr;
				return;
			}
			if (names.Count == 0) {
				res.Error = Loc.T("sql.export.notable");
				return;
			}
		}

		var d = o.Dialect;
		var ver = o.Version ?? SqlTargets.Latest(d);
		using var w = new StreamWriter(o.Path, false, new UTF8Encoding(false));
		w.NewLine = "\r\n";
		w.WriteLine("-- " + Loc.T("sql.export.header", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
		w.WriteLine("-- " + Loc.T("sql.export.target", targetlabel(d, ver)));
		w.WriteLine("-- " + Loc.T("sql.export.source", KindName(o.Kind)));
		w.WriteLine();
		if (d == SqlDialect.Mssql) w.WriteLine("SET NOCOUNT ON;");
		else if (d == SqlDialect.Mysql) {
			w.WriteLine("SET NAMES utf8mb4;");
			w.WriteLine("SET FOREIGN_KEY_CHECKS = 0;");
		}
		else w.WriteLine("PRAGMA foreign_keys = OFF;");
		w.WriteLine();

		for (var i = 0; i < names.Count; i++) {
			job.report(SqlExportPhase.Schema, i + 1, names.Count, names[i], 0, 0, true);
			var table = new SqlExportTable { Name = names[i] };
			if (isresult) {
				using var rs = openreader(cn, o.ResultSql);
				if (rs == null) {
					res.Error = Loc.T("sql.export.noresult");
					return;
				}
				table.Columns = Columns(rs.GetSchemaTable());
				if (table.Columns.Count == 0) {
					res.Error = Loc.T("sql.export.nocols", names[i]);
					return;
				}
				w.WriteLine("-- " + Loc.T("sql.export.schema", names[i]));
				w.WriteLine();
				w.WriteLine(createof(table, d, ver, false));
				if (o.WithData) {
					w.WriteLine();
					w.WriteLine("-- " + Loc.T("sql.export.data", names[i]));
					res.Rows += insertrows(w, rs, table, d, ver, job, i + 1, names.Count);
				}
				job.report(SqlExportPhase.Done, i + 1, names.Count, names[i], 0, 0, true);
				w.WriteLine();
				res.Files = 1;
				continue;
			}

			ReadSchema(cn, o.Kind, table);
			if (table.Columns.Count == 0) {
				res.Error = Loc.T("sql.export.nocols", names[i]);
				return;
			}
			ReadIndexes(cn, o.Kind, table);
			DbDataReader rd = null;
			try {
				rd = openreader(cn, SelectOf(o.Kind, table.Name));
				if (rd == null) {
					res.Error = Loc.T("sql.export.notable");
					return;
				}
				if (o.WithSchema) {
					w.WriteLine("-- " + Loc.T("sql.export.schema", table.Name));
					w.WriteLine();
					w.WriteLine(createof(table, d, ver, o.Kind == DialectKind(d)));
				}
				if (o.WithData) {
					if (o.WithSchema) w.WriteLine();
					w.WriteLine("-- " + Loc.T("sql.export.data", table.Name));
					res.Rows += insertrows(w, rd, table, d, ver, job, i + 1, names.Count);
				}
			}
			finally {
				try { rd?.Dispose(); } catch { }
			}
			if (o.WithSchema) {
				var ddl = indexof(table, d, ver);
				if (ddl.Count > 0) {
					w.WriteLine();
					w.WriteLine("-- " + Loc.T("sql.export.index", table.Name));
					foreach (var line in ddl) w.WriteLine(line);
				}
			}
			job.report(SqlExportPhase.Done, i + 1, names.Count, names[i], 0, 0, true);
			w.WriteLine();
			res.Files = 1;
		}
		if (d == SqlDialect.Mysql) w.WriteLine("SET FOREIGN_KEY_CHECKS = 1;");
		else if (d == SqlDialect.Sqlite) w.WriteLine("PRAGMA foreign_keys = ON;");
	}

	static string targetlabel(SqlDialect d, Version v) {
		var name = DialectName(d);
		if (d != SqlDialect.Mssql || v == null) return name;
		var year = v.Major switch {
			9 => 2005,
			10 => 2008,
			11 => 2012,
			12 => 2014,
			13 => 2016,
			14 => 2017,
			15 => 2019,
			16 => 2022,
			_ => 0,
		};
		return year > 0 ? $"{name} {year}" : $"{name} {v}";
	}

	static string DialectKind(SqlDialect d) => d switch {
		SqlDialect.Mssql => SqlTool.Mssql,
		SqlDialect.Mysql => SqlTool.Mysql,
		_ => SqlTool.Sqlite,
	};

	public static string KindName(string kind) => kind switch {
		SqlTool.Mssql => "SQL Server",
		SqlTool.Mysql => "MySQL",
		SqlTool.Oracle => "Oracle",
		_ => "SQLite",
	};

	/// <summary>建表语句。同引擎导出时列类型原样保留，跨引擎按目标方言重写。</summary>
	static string createof(SqlExportTable t, SqlDialect d, Version ver, bool sametype) {
		var q = DialectKind(d);
		var tname = QuoteName(q, t.Name);
		var sb = new StringBuilder();
		if (d == SqlDialect.Mssql) {
			if (SqlTargets.DropIfExists(ver)) {
				sb.Append($"DROP TABLE IF EXISTS {tname};\r\n");
			}
			else sb.Append($"IF OBJECT_ID(N'{strlit(t.Name)}', N'U') IS NOT NULL DROP TABLE {tname};\r\n");
		}
		else sb.Append($"DROP TABLE IF EXISTS {tname};\r\n");
		sb.Append($"CREATE TABLE {tname} (\r\n");
		var lines = new List<string>();
		var pk = new List<string>();
		foreach (var c in t.Columns) {
			var line = $"  {QuoteName(q, c.Name)} {typetext(c, d, ver, sametype)}";
			var def = defaultof(c, d, ver);
			if (def.Length > 0) line += " DEFAULT " + def;
			if (c.Identity) line += identityof(d, c);
			if (!c.Nullable) line += " NOT NULL";
			if (c.PrimaryKey) pk.Add(QuoteName(q, c.Name));
			lines.Add(line);
		}
		if (pk.Count > 0) {
			if (d == SqlDialect.Mssql)
				lines.Add($"  CONSTRAINT {QuoteName(q, truncate("PK_" + t.Name, 120))} PRIMARY KEY ({string.Join(", ", pk.ToArray())})");
			else lines.Add($"  PRIMARY KEY ({string.Join(", ", pk.ToArray())})");
		}
		sb.Append(string.Join(",\r\n", lines.ToArray()));
		sb.Append("\r\n)");
		if (d == SqlDialect.Mysql) sb.Append(" ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
		sb.Append(";\r\n");
		return sb.ToString();
	}

	/// <summary>自增写法各方言不同；SQLite 只有 INTEGER PRIMARY KEY 能自增，非主键列加不了。</summary>
	static string identityof(SqlDialect d, SqlExportColumn c) => d switch {
		SqlDialect.Mssql => " IDENTITY(1,1)",
		SqlDialect.Mysql => " AUTO_INCREMENT",
		_ => c.PrimaryKey && c.Kind is SqlTypeKind.Int or SqlTypeKind.BigInt ? " AUTOINCREMENT" : "",
	};

	static string truncate(string s, int n) => string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n);

	static List<string> indexof(SqlExportTable t, SqlDialect d, Version ver) {
		var list = new List<string>();
		var q = DialectKind(d);
		var tname = QuoteName(q, t.Name);
		foreach (var ix in t.Indexes) {
			if (ix.Columns.Count == 0) continue;
			// 目标端不能给 text / nvarchar(max) / blob 建普通索引，跳过免得导入报错
			var bad = false;
			foreach (var col in ix.Columns) {
				var c = t.Columns.Find(x => string.Equals(x.Name, col, StringComparison.OrdinalIgnoreCase));
				if (c != null && indexable(c, d, ver) == false) {
					bad = true;
					break;
				}
			}
			if (bad) continue;
			var name = ix.Name;
			if (d == SqlDialect.Sqlite) name = t.Name + "_" + name;
			name = truncate(name, 120);
			list.Add($"CREATE {(ix.Unique ? "UNIQUE " : "")}INDEX {QuoteName(q, name)} ON {tname} "
				+ $"({string.Join(", ", ix.Columns.ConvertAll(c => QuoteName(q, c)).ToArray())});");
		}
		return list;
	}

	static bool indexable(SqlExportColumn c, SqlDialect d, Version ver) {
		if (c.Kind == SqlTypeKind.Blob) return false;
		if (c.Kind == SqlTypeKind.Text) {
			if (d == SqlDialect.Sqlite) return true;
			return c.Size > 0 && c.Size <= 4000;
		}
		return true;
	}

	/// <summary>多行 INSERT。SQL Server 2008 之前只支持一条 VALUES。</summary>
	static long insertrows(StreamWriter w, DbDataReader rd, SqlExportTable t, SqlDialect d, Version ver,
		SqlExportJob job, int index, int total) {
		var q = DialectKind(d);
		var tname = QuoteName(q, t.Name);
		var cols = t.Columns;
		var head = "INSERT INTO " + tname + " ("
			+ string.Join(", ", cols.ConvertAll(c => QuoteName(q, c.Name)).ToArray()) + ") VALUES";
		var batch = d == SqlDialect.Mssql && !SqlTargets.Modern(ver) ? 1 : 200;
		var sb = new StringBuilder();
		var n = 0;
		long rows = 0;
		while (rd.Read()) {
			var vals = new object[rd.FieldCount];
			rd.GetValues(vals);
			if (n == 0) sb.Append(head);
			else sb.Append(',');
			sb.Append("\r\n(");
			for (var i = 0; i < cols.Count && i < vals.Length; i++) {
				if (i > 0) sb.Append(", ");
				sqlvalue(sb, vals[i], cols[i], d, ver);
			}
			sb.Append(')');
			n++;
			rows++;
			if (n >= batch) {
				w.WriteLine(sb.Append(';').ToString());
				sb.Clear();
				n = 0;
			}
			if ((rows & 0x3F) == 0) job.report(SqlExportPhase.Data, index, total, t.Name, rows, 0);
		}
		if (n > 0) w.WriteLine(sb.Append(';').ToString());
		job.report(SqlExportPhase.Data, index, total, t.Name, rows, 0, true);
		return rows;
	}

	static void sqlvalue(StringBuilder sb, object v, SqlExportColumn c, SqlDialect d, Version ver) {
		if (v == null || v is DBNull) {
			sb.Append("NULL");
			return;
		}
		switch (c.Kind) {
		case SqlTypeKind.Blob: {
			var bytes = v as byte[];
			if (bytes == null) bytes = Encoding.UTF8.GetBytes(Convert.ToString(v, CultureInfo.InvariantCulture) ?? "");
			if (d == SqlDialect.Mssql) sb.Append("0x").Append(hex(bytes));
			else sb.Append("X'").Append(hex(bytes)).Append('\'');
			return;
		}
		case SqlTypeKind.Bool:
			sb.Append(v is bool b ? (b ? "1" : "0") : num(v));
			return;
		case SqlTypeKind.Int:
		case SqlTypeKind.BigInt:
		case SqlTypeKind.Decimal:
		case SqlTypeKind.Float:
			if (isnumber(v)) sb.Append(num(v));
			else strlit(sb, Convert.ToString(v, CultureInfo.InvariantCulture), d);
			return;
		case SqlTypeKind.Date: {
			var s = v is DateTime dt
				? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
				: Convert.ToString(v, CultureInfo.InvariantCulture);
			// 2005 没有 date，只能写整点 datetime 字面量
			if (d == SqlDialect.Mssql && !SqlTargets.Modern(ver)) s += " 00:00:00";
			strlit(sb, s, d);
			return;
		}
		case SqlTypeKind.Time: {
			var s = v is DateTime dt
				? dt.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)
				: v is TimeSpan ts
					? ts.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture)
					: Convert.ToString(v, CultureInfo.InvariantCulture);
			strlit(sb, s, d);
			return;
		}
		case SqlTypeKind.DateTime:
			if (v is DateTime dtv) strlit(sb, dtv.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), d);
			else if (v is DateTimeOffset dto) strlit(sb, dto.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), d);
			else strlit(sb, Convert.ToString(v, CultureInfo.InvariantCulture), d);
			return;
		}
		strlit(sb, Convert.ToString(v, CultureInfo.InvariantCulture), d);
	}

	/// <summary>SQL Server 非 ASCII 加 N 前缀；MySQL 反斜杠要转义。</summary>
	static void strlit(StringBuilder sb, string s, SqlDialect d) {
		s ??= "";
		if (d == SqlDialect.Mssql && !isascii(s)) sb.Append('N');
		sb.Append('\'');
		if (d == SqlDialect.Mysql) {
			for (var i = 0; i < s.Length; i++) {
				var ch = s[i];
				if (ch == '\\') sb.Append("\\\\");
				else if (ch == '\'') sb.Append("''");
				else sb.Append(ch);
			}
		}
		else sb.Append(s.Replace("'", "''"));
		sb.Append('\'');
	}

	static string strlit(string s) => (s ?? "").Replace("'", "''");

	static bool isascii(string s) {
		for (var i = 0; i < s.Length; i++)
			if (s[i] > 127) return false;
		return true;
	}

	static string hex(byte[] bytes) {
		var sb = new StringBuilder(bytes.Length * 2);
		foreach (var b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
		return sb.ToString();
	}

	static bool isnumber(object v) =>
		v is int || v is long || v is short || v is byte || v is sbyte || v is uint || v is ulong
		|| v is ushort || v is decimal || v is double || v is float;

	static string num(object v) => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "0";

	// ==================== 类型与默认值 ====================

	public static string typetext(SqlExportColumn c, SqlDialect d, Version ver) => typetext(c, d, ver, false);

	public static string typetext(SqlExportColumn c, SqlDialect d, Version ver, bool sametype) {
		if (sametype && !string.IsNullOrWhiteSpace(c.SourceType)) return c.SourceType.Trim();
		var size = c.Size;
		if (d == SqlDialect.Mssql) {
			var modern = SqlTargets.Modern(ver);
			return c.Kind switch {
				SqlTypeKind.Int => "int",
				SqlTypeKind.BigInt => "bigint",
				SqlTypeKind.Decimal => "decimal(18,6)",
				SqlTypeKind.Float => "float",
				SqlTypeKind.Bool => "bit",
				SqlTypeKind.Date => modern ? "date" : "datetime",
				SqlTypeKind.Time => modern ? "time" : "datetime",
				SqlTypeKind.DateTime => modern ? "datetime2" : "datetime",
				SqlTypeKind.Blob => "varbinary(max)",
				SqlTypeKind.Guid => "uniqueidentifier",
				_ => size > 0 && size <= 4000 ? $"nvarchar({size})" : "nvarchar(max)",
			};
		}
		if (d == SqlDialect.Mysql) {
			return c.Kind switch {
				SqlTypeKind.Int => "int",
				SqlTypeKind.BigInt => "bigint",
				SqlTypeKind.Decimal => "decimal(18,6)",
				SqlTypeKind.Float => "double",
				SqlTypeKind.Bool => "tinyint(1)",
				SqlTypeKind.Date => "date",
				SqlTypeKind.Time => "time",
				SqlTypeKind.DateTime => "datetime",
				SqlTypeKind.Blob => "longblob",
				SqlTypeKind.Guid => "char(36)",
				_ => size > 0 && size <= 4000 ? $"varchar({size})" : "longtext",
			};
		}
		return c.Kind switch {
			SqlTypeKind.Int or SqlTypeKind.BigInt or SqlTypeKind.Bool => "INTEGER",
			SqlTypeKind.Decimal => "NUMERIC",
			SqlTypeKind.Float => "REAL",
			SqlTypeKind.Blob => "BLOB",
			_ => "TEXT",
		};
	}

	enum DefKind {
		None,
		Number,
		Text,
		Now,
		Today,
	}

	static void parsedefault(SqlExportColumn c, out DefKind kind, out string text) {
		kind = DefKind.None;
		text = "";
		var raw = (c.Default ?? "").Trim();
		if (raw.Length == 0) return;
		raw = stripparens(raw);
		if (raw.Length == 0) return;
		var up = raw.ToUpperInvariant();
		if (up == "NULL") return;
		if (up is "CURRENT_TIMESTAMP" or "NOW()" or "GETDATE()" or "SYSDATETIME()" or "LOCALTIMESTAMP"
			|| up.StartsWith("DATETIME('NOW')", StringComparison.Ordinal) || up.StartsWith("GETUTCDATE()", StringComparison.Ordinal)) {
			kind = DefKind.Now;
			return;
		}
		if (up is "CURRENT_DATE" or "CURDATE()" or "TRUNC(GETDATE())" || up.StartsWith("CONVERT([DATE]", StringComparison.Ordinal)) {
			kind = DefKind.Today;
			return;
		}
		if (isnumeric(raw)) {
			kind = DefKind.Number;
			text = raw;
			return;
		}
		if (c.Kind is SqlTypeKind.Int or SqlTypeKind.BigInt or SqlTypeKind.Decimal or SqlTypeKind.Float or SqlTypeKind.Bool
			&& isnumeric(raw.Trim('\''))) {
			kind = DefKind.Number;
			text = raw.Trim('\'');
			return;
		}
		if (raw.Length >= 2 && (raw[0] == '\'' && raw[raw.Length - 1] == '\''
			|| raw[0] == '"' && raw[raw.Length - 1] == '"')) {
			kind = DefKind.Text;
			text = raw.Substring(1, raw.Length - 2).Replace("''", "'");
			return;
		}
		// 源库把字符串默认值裸着给（MySQL 的 INFORMATION_SCHEMA），按列类型当字面量
		if (!raw.Contains("(") && !raw.Contains(")")) {
			kind = c.Kind is SqlTypeKind.Int or SqlTypeKind.BigInt or SqlTypeKind.Decimal
				or SqlTypeKind.Float or SqlTypeKind.Bool ? DefKind.Number : DefKind.Text;
			text = raw;
		}
	}

	static string defaultof(SqlExportColumn c, SqlDialect d, Version ver) {
		parsedefault(c, out var kind, out var text);
		switch (kind) {
		case DefKind.Number:
			return text;
		case DefKind.Text: {
			var sb = new StringBuilder();
			strlit(sb, text, d);
			return sb.ToString();
		}
		case DefKind.Now:
			return d switch {
				SqlDialect.Mssql => "GETDATE()",
				_ => "CURRENT_TIMESTAMP",
			};
		case DefKind.Today:
			if (d == SqlDialect.Mssql) return SqlTargets.Modern(ver) ? "CAST(GETDATE() AS date)" : "GETDATE()";
			return "CURRENT_DATE";
		default:
			return "";
		}
	}

	static string stripparens(string s) {
		while (s.Length >= 2 && s[0] == '(' && s[s.Length - 1] == ')') {
			var depth = 0;
			var whole = true;
			for (var i = 0; i < s.Length; i++) {
				if (s[i] == '(') depth++;
				else if (s[i] == ')') {
					depth--;
					if (depth == 0 && i < s.Length - 1) {
						whole = false;
						break;
					}
				}
				if (depth < 0) {
					whole = false;
					break;
				}
			}
			if (!whole) break;
			s = s.Substring(1, s.Length - 2).Trim();
		}
		return s;
	}

	static bool isnumeric(string s) {
		if (string.IsNullOrEmpty(s)) return false;
		return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
	}

	// ==================== 表格类格式 ====================

	static void tablefiles(DbConnection cn, SqlExportOptions o, SqlExportJob job) {
		var res = job.Result;
		var names = new List<string>();
		string resultsql = null;
		if (o.Scope == SqlExportScope.Result) {
			if (string.IsNullOrWhiteSpace(o.ResultSql)) {
				res.Error = Loc.T("sql.export.noresult");
				return;
			}
			names.Add(string.IsNullOrWhiteSpace(o.Table) ? "query" : o.Table);
			resultsql = o.ResultSql;
		}
		else if (o.Scope == SqlExportScope.Table) {
			if (string.IsNullOrWhiteSpace(o.Table)) {
				res.Error = Loc.T("sql.needtable");
				return;
			}
			names.Add(o.Table);
		}
		else {
			names = Tables(cn, o.Kind, out var terr);
			if (terr.Length > 0) {
				res.Error = terr;
				return;
			}
			if (names.Count == 0) {
				res.Error = Loc.T("sql.export.notable");
				return;
			}
		}
		var multi = o.Scope == SqlExportScope.Database;
		if (multi) Directory.CreateDirectory(o.Path);
		var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (var i = 0; i < names.Count; i++) {
			job.report(SqlExportPhase.Schema, i + 1, names.Count, names[i], 0, 0, true);
			var path = multi
				? Path.Combine(o.Path, UniqueFile(SafeName(names[i]), used) + ExtOf(o.Format))
				: o.Path;
			var sql = resultsql ?? SelectOf(o.Kind, names[i]);
			var cols = new List<SqlExportColumn>();
			DbDataReader rd = null;
			try {
				rd = openreader(cn, sql);
				if (rd == null) {
					res.Error = Loc.T("sql.export.notable");
					return;
				}
				cols = Columns(rd.GetSchemaTable());
				if (cols.Count == 0) {
					res.Error = Loc.T("sql.export.nocols", names[i]);
					return;
				}
				var dir = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
				if (o.Format == SqlExportFormat.Csv) res.Rows += writecsv(rd, cols, path, res, job, i + 1, names.Count);
				else if (o.Format == SqlExportFormat.Html) res.Rows += writehtml(rd, cols, path, names[i], res, job, i + 1, names.Count);
				else res.Rows += writexlsx(rd, cols, path, names[i], res, job, i + 1, names.Count);
				res.Files++;
			}
			finally {
				try { rd?.Dispose(); } catch { }
			}
			job.report(SqlExportPhase.Done, i + 1, names.Count, names[i], 0, 0, true);
		}
	}

	/// <summary>单元格文本：空值留空，不与字面量 NULL 混淆。</summary>
	static string celltext(object v) => v == null || v is DBNull ? "" : SqlTool.Text(v);

	static long writecsv(DbDataReader rd, List<SqlExportColumn> cols, string path, SqlExportResult res,
		SqlExportJob job, int index, int total) {
		using var w = new StreamWriter(path, false, new UTF8Encoding(true));
		w.NewLine = "\r\n";
		w.WriteLine(string.Join(",", cols.ConvertAll(c => csv(c.Name)).ToArray()));
		var cells = new string[cols.Count];
		long rows = 0;
		while (rd.Read()) {
			for (var i = 0; i < cells.Length; i++) cells[i] = celltext(rd.GetValue(i));
			w.WriteLine(string.Join(",", Array.ConvertAll(cells, csv)));
			rows++;
			if ((rows & 0xFF) == 0) job.report(SqlExportPhase.Data, index, total, "", rows, 0);
			if (rows >= 1048576) {
				res.Note = Loc.T("sql.export.toomany");
				break;
			}
		}
		return rows;
	}

	const string HtmlStyle =
		"body{font-family:'Segoe UI','Microsoft YaHei UI',sans-serif;font-size:13px;color:#1a202c;margin:16px;background:#fff}"
		+ "table{border-collapse:collapse}"
		+ "th,td{border:1px solid #e2e8f0;padding:5px 10px;text-align:left;vertical-align:top;white-space:pre-wrap}"
		+ "thead th{background:#f4f6f9;font-weight:600}"
		+ "tbody tr:nth-child(even){background:#fafbfc}"
		+ ".cap{color:#718096;font-size:12px;margin-top:10px}";

	static long writehtml(DbDataReader rd, List<SqlExportColumn> cols, string path, string title, SqlExportResult res,
		SqlExportJob job, int index, int total) {
		using var w = new StreamWriter(path, false, new UTF8Encoding(false));
		w.NewLine = "\r\n";
		w.WriteLine("<!DOCTYPE html>");
		w.WriteLine("<html><head><meta charset=\"utf-8\">");
		w.WriteLine("<title>" + Xesc(title) + "</title>");
		w.WriteLine("<style>" + HtmlStyle + "</style></head><body>");
		w.WriteLine("<table>");
		w.WriteLine("<thead><tr>" + string.Concat(cols.ConvertAll(c => "<th>" + Xesc(c.Name) + "</th>").ToArray()) + "</tr></thead>");
		w.WriteLine("<tbody>");
		var cells = new string[cols.Count];
		long rows = 0;
		while (rd.Read()) {
			for (var i = 0; i < cells.Length; i++) cells[i] = celltext(rd.GetValue(i));
			w.WriteLine("<tr>" + string.Concat(Array.ConvertAll(cells, v => "<td>" + Xesc(v) + "</td>")) + "</tr>");
			rows++;
			if ((rows & 0xFF) == 0) job.report(SqlExportPhase.Data, index, total, title, rows, 0);
		}
		w.WriteLine("</tbody></table>");
		w.WriteLine("<p class=\"cap\">" + Xesc(Loc.T("sql.export.rowscap", rows)) + "</p>");
		w.WriteLine("</body></html>");
		return rows;
	}

	static string csv(string s) {
		s ??= "";
		var need = s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
			|| s.Length > 0 && (s[0] == ' ' || s[s.Length - 1] == ' ');
		if (!need) return s;
		return "\"" + s.Replace("\"", "\"\"") + "\"";
	}

	static string Xesc(string s) {
		if (string.IsNullOrEmpty(s)) return "";
		var sb = new StringBuilder(s.Length + 8);
		foreach (var ch in s) {
			switch (ch) {
			case '&': sb.Append("&amp;"); break;
			case '<': sb.Append("&lt;"); break;
			case '>': sb.Append("&gt;"); break;
			case '"': sb.Append("&quot;"); break;
			// XML 1.0 不允许的控制字符直接丢掉，否则 Excel 打不开
			default:
				if (ch < 0x20 && ch != '\t' && ch != '\n' && ch != '\r') break;
				sb.Append(ch);
				break;
			}
		}
		return sb.ToString();
	}

	/// <summary>手写最小工作簿，边读边写，不依赖 OpenXml（精简包里也能用）。</summary>
	static long writexlsx(DbDataReader rd, List<SqlExportColumn> cols, string path, string title, SqlExportResult res,
		SqlExportJob job, int index, int total) {
		const int MaxRows = 1048575;
		using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
		using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
		puts(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
			+ "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
			+ "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
			+ "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
			+ "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
			+ "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"
			+ "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>"
			+ "</Types>");
		puts(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
			+ "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
			+ "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>"
			+ "</Relationships>");
		puts(zip, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
			+ "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" "
			+ "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
			+ "<sheets><sheet name=\"" + Xesc(SheetName(title)) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets>"
			+ "</workbook>");
		puts(zip, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
			+ "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
			+ "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>"
			+ "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>"
			+ "</Relationships>");
		puts(zip, "xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
			+ "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
			+ "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font>"
			+ "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>"
			+ "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill>"
			+ "<fill><patternFill patternType=\"gray125\"/></fill></fills>"
			+ "<borders count=\"1\"><border/></borders>"
			+ "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
			+ "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>"
			+ "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>"
			+ "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>"
			+ "</styleSheet>");

		var entry = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest);
		using var s = entry.Open();
		using var w = new StreamWriter(s, new UTF8Encoding(false));
		w.NewLine = "";
		w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
		w.Write("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
		w.Write("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
		w.Write("<sheetData>");
		w.Write("<row r=\"1\">");
		for (var i = 0; i < cols.Count; i++)
			w.Write($"<c r=\"{ColName(i)}1\" t=\"inlineStr\" s=\"1\"><is><t xml:space=\"preserve\">{Xesc(cols[i].Name)}</t></is></c>");
		w.Write("</row>");
		var cells = new string[cols.Count];
		long rows = 0;
		while (rd.Read()) {
			if (rows >= MaxRows) {
				res.Note = Loc.T("sql.export.toomany");
				break;
			}
			rows++;
			var r = rows + 1;
			w.Write($"<row r=\"{r}\">");
			for (var i = 0; i < cells.Length; i++) cells[i] = celltext(rd.GetValue(i));
			for (var i = 0; i < cells.Length; i++) {
				var v = cells[i];
				var ref_ = ColName(i) + r.ToString(CultureInfo.InvariantCulture);
				if (v.Length == 0) continue;
				if (isnumbervalue(v, cols[i].Kind) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) {
					w.Write($"<c r=\"{ref_}\"><v>{v}</v></c>");
					continue;
				}
				w.Write($"<c r=\"{ref_}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Xesc(v)}</t></is></c>");
			}
			w.Write("</row>");
			if ((rows & 0xFF) == 0) job.report(SqlExportPhase.Data, index, total, title, rows, 0);
		}
		w.Write("</sheetData>");
		w.Write("<autoFilter ref=\"A1:" + ColName(Math.Max(0, cols.Count - 1)) + "1\"/>");
		w.Write("</worksheet>");
		return rows;
	}

	static bool isnumbervalue(string v, SqlTypeKind kind) {
		if (kind != SqlTypeKind.Int && kind != SqlTypeKind.BigInt && kind != SqlTypeKind.Decimal
			&& kind != SqlTypeKind.Float && kind != SqlTypeKind.Bool) return false;
		// 前导 0 的数字（编号、电话）当文本，免得 Excel 吃掉
		if (v.Length > 1 && v[0] == '0' && v[1] != '.') return false;
		return true;
	}

	static string SheetName(string name) {
		var bad = new[] { ':', '\\', '/', '?', '*', '[', ']' };
		var s = name ?? "";
		foreach (var ch in bad) s = s.Replace(ch, '_');
		if (s.Trim().Length == 0) s = "Sheet1";
		return s.Length > 31 ? s.Substring(0, 31) : s;
	}

	static string ColName(int index) {
		var sb = new StringBuilder();
		var n = index + 1;
		while (n > 0) {
			var r = (n - 1) % 26;
			sb.Insert(0, (char)('A' + r));
			n = (n - 1) / 26;
		}
		return sb.ToString();
	}

	static void puts(ZipArchive zip, string name, string text) {
		var e = zip.CreateEntry(name, CompressionLevel.Fastest);
		using var s = e.Open();
		var bytes = new UTF8Encoding(false).GetBytes(text);
		s.Write(bytes, 0, bytes.Length);
	}

	// ==================== 源库结构 ====================

	/// <summary>读表结构（列、主键、自增、默认值）与二级索引，填进 table。</summary>
	public static void ReadSchema(DbConnection cn, string kind, SqlExportTable t) {
		t.Columns = kind switch {
			SqlTool.Mssql => schemamssql(cn, t.Name),
			SqlTool.Mysql => schemamysql(cn, t.Name),
			SqlTool.Oracle => schemaoracle(cn, t.Name),
			_ => schemasqlite(cn, t.Name),
		};
	}

	public static void ReadIndexes(DbConnection cn, string kind, SqlExportTable t) {
		try {
			if (kind == SqlTool.Mysql) mysqlinDexes(cn, t);
			else if (kind == SqlTool.Mssql) mssqlindexes(cn, t);
			else if (kind == SqlTool.Sqlite) sqliteindexes(cn, t);
		}
		catch { }
	}

	static List<SqlExportColumn> schemasqlite(DbConnection cn, string table) {
		var list = new List<SqlExportColumn>();
		var autoinc = false;
		try {
			using var cmd = cn.CreateCommand();
			cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = @t";
			var p = cmd.CreateParameter();
			p.ParameterName = "@t";
			p.Value = table;
			cmd.Parameters.Add(p);
			var o = cmd.ExecuteScalar();
			autoinc = o != null && o is not DBNull
				&& Convert.ToString(o, CultureInfo.InvariantCulture).IndexOf("AUTOINCREMENT", StringComparison.OrdinalIgnoreCase) >= 0;
		}
		catch { }
		try {
			using var cmd = cn.CreateCommand();
			cmd.CommandText = "PRAGMA table_info(" + QuoteName(SqlTool.Sqlite, table) + ")";
			using var rd = cmd.ExecuteReader();
			while (rd.Read()) {
				var declared = rd.GetValue(2) == DBNull.Value ? "" : Convert.ToString(rd.GetValue(2), CultureInfo.InvariantCulture);
				var c = new SqlExportColumn {
					Name = Convert.ToString(rd.GetValue(1), CultureInfo.InvariantCulture),
					SourceType = declared,
					Default = rd.GetValue(4) == DBNull.Value ? "" : Convert.ToString(rd.GetValue(4), CultureInfo.InvariantCulture),
					Nullable = Convert.ToString(rd.GetValue(3), CultureInfo.InvariantCulture) == "0",
				};
				c.Kind = kindofdeclared(declared, out var sz);
				c.Size = sz;
				var pk = Convert.ToInt32(rd.GetValue(5), CultureInfo.InvariantCulture);
				c.PrimaryKey = pk > 0;
				c.Identity = autoinc && c.PrimaryKey && c.Kind is SqlTypeKind.Int or SqlTypeKind.BigInt;
				list.Add(c);
			}
		}
		catch { }
		return list;
	}

	static List<SqlExportColumn> schemamysql(DbConnection cn, string table) {
		var list = new List<SqlExportColumn>();
		try {
			using var cmd = cn.CreateCommand();
			cmd.CommandText = "SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT, EXTRA, COLUMN_KEY "
				+ "FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = @t "
				+ "ORDER BY ORDINAL_POSITION";
			var p = cmd.CreateParameter();
			p.ParameterName = "@t";
			p.Value = table;
			cmd.Parameters.Add(p);
			using var rd = cmd.ExecuteReader();
			while (rd.Read()) {
				var coltype = Convert.ToString(rd.GetValue(1), CultureInfo.InvariantCulture) ?? "";
				var c = new SqlExportColumn {
					Name = Convert.ToString(rd.GetValue(0), CultureInfo.InvariantCulture),
					SourceType = coltype,
					Nullable = Convert.ToString(rd.GetValue(2), CultureInfo.InvariantCulture) == "YES",
					Default = rd.GetValue(3) == DBNull.Value ? "" : Convert.ToString(rd.GetValue(3), CultureInfo.InvariantCulture),
				};
				c.Kind = kindofdeclared(coltype, out var sz);
				c.Size = sz;
				var extra = Convert.ToString(rd.GetValue(4), CultureInfo.InvariantCulture) ?? "";
				c.Identity = extra.IndexOf("auto_increment", StringComparison.OrdinalIgnoreCase) >= 0;
				c.PrimaryKey = Convert.ToString(rd.GetValue(5), CultureInfo.InvariantCulture) == "PRI";
				list.Add(c);
			}
		}
		catch { }
		return list;
	}

	static List<SqlExportColumn> schemamssql(DbConnection cn, string table) {
		var list = new List<SqlExportColumn>();
		try {
			using var cmd = cn.CreateCommand();
			cmd.CommandText = "SELECT c.COLUMN_NAME, c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE, c.COLUMN_DEFAULT, "
				+ "COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsIdentity') AS IsIdentity, "
				+ "CASE WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc "
				+ "  JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku ON ku.CONSTRAINT_NAME = tc.CONSTRAINT_NAME "
				+ "  WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY' AND tc.TABLE_NAME = c.TABLE_NAME AND ku.COLUMN_NAME = c.COLUMN_NAME) "
				+ "THEN 1 ELSE 0 END AS IsPk "
				+ "FROM INFORMATION_SCHEMA.COLUMNS c WHERE c.TABLE_NAME = @t ORDER BY c.ORDINAL_POSITION";
			var p = cmd.CreateParameter();
			p.ParameterName = "@t";
			p.Value = table;
			cmd.Parameters.Add(p);
			using var rd = cmd.ExecuteReader();
			while (rd.Read()) {
				var dtype = Convert.ToString(rd.GetValue(1), CultureInfo.InvariantCulture) ?? "";
				var c = new SqlExportColumn {
					Name = Convert.ToString(rd.GetValue(0), CultureInfo.InvariantCulture),
					SourceType = dtype,
					Nullable = Convert.ToString(rd.GetValue(3), CultureInfo.InvariantCulture) == "YES",
					Default = rd.GetValue(4) == DBNull.Value ? "" : Convert.ToString(rd.GetValue(4), CultureInfo.InvariantCulture),
				};
				c.Kind = kindofdeclared(dtype, out var sz);
				var len = rd.GetValue(2) == DBNull.Value ? 0 : Convert.ToInt32(rd.GetValue(2), CultureInfo.InvariantCulture);
				c.Size = len > 0 && len < int.MaxValue ? len : sz;
				var ident = rd.GetValue(5) == DBNull.Value ? 0 : Convert.ToInt32(rd.GetValue(5), CultureInfo.InvariantCulture);
				c.Identity = ident == 1;
				c.PrimaryKey = Convert.ToInt32(rd.GetValue(6), CultureInfo.InvariantCulture) == 1;
				list.Add(c);
			}
		}
		catch { }
		return list;
	}

	static List<SqlExportColumn> schemaoracle(DbConnection cn, string table) {
		var list = new List<SqlExportColumn>();
		try {
			using var cmd = cn.CreateCommand();
			cmd.CommandText = "SELECT COLUMN_NAME, DATA_TYPE, DATA_LENGTH, NULLABLE, DATA_DEFAULT FROM USER_TAB_COLUMNS "
				+ "WHERE TABLE_NAME = :t ORDER BY COLUMN_ID";
			var p = cmd.CreateParameter();
			p.ParameterName = "t";
			p.Value = table;
			cmd.Parameters.Add(p);
			var pkcols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			try {
				using var pc = cn.CreateCommand();
				pc.CommandText = "SELECT cc.COLUMN_NAME FROM USER_CONSTRAINTS c JOIN USER_CONS_COLUMNS cc "
					+ "ON cc.CONSTRAINT_NAME = c.CONSTRAINT_NAME WHERE c.CONSTRAINT_TYPE = 'P' AND c.TABLE_NAME = :t";
				var pp = pc.CreateParameter();
				pp.ParameterName = "t";
				pp.Value = table;
				pc.Parameters.Add(pp);
				using var pr = pc.ExecuteReader();
				while (pr.Read()) pkcols.Add(Convert.ToString(pr.GetValue(0), CultureInfo.InvariantCulture));
			}
			catch { }
			using var rd = cmd.ExecuteReader();
			while (rd.Read()) {
				var dtype = Convert.ToString(rd.GetValue(1), CultureInfo.InvariantCulture) ?? "";
				var c = new SqlExportColumn {
					Name = Convert.ToString(rd.GetValue(0), CultureInfo.InvariantCulture),
					SourceType = dtype,
					Nullable = Convert.ToString(rd.GetValue(3), CultureInfo.InvariantCulture) == "Y",
					Default = rd.GetValue(4) == DBNull.Value ? "" : Convert.ToString(rd.GetValue(4), CultureInfo.InvariantCulture),
				};
				c.Kind = kindofdeclared(dtype, out var sz);
				var len = rd.GetValue(2) == DBNull.Value ? 0 : Convert.ToInt32(rd.GetValue(2), CultureInfo.InvariantCulture);
				c.Size = len > 0 ? len : sz;
				if (c.Kind == SqlTypeKind.Text && dtype.Equals("VARCHAR2", StringComparison.OrdinalIgnoreCase)
					&& c.Size * 3 > 4000) c.Size = 0;
				c.PrimaryKey = pkcols.Contains(c.Name);
				list.Add(c);
			}
		}
		catch { }
		return list;
	}

	static void sqliteindexes(DbConnection cn, SqlExportTable t) {
		var names = new List<(string Name, bool Unique)>();
		using (var cmd = cn.CreateCommand()) {
			cmd.CommandText = "PRAGMA index_list(" + QuoteName(SqlTool.Sqlite, t.Name) + ")";
			using var rd = cmd.ExecuteReader();
			while (rd.Read()) {
				var name = Convert.ToString(rd.GetValue(1), CultureInfo.InvariantCulture) ?? "";
				if (name.StartsWith("sqlite_autoindex", StringComparison.OrdinalIgnoreCase)) continue;
				names.Add((name, Convert.ToString(rd.GetValue(2), CultureInfo.InvariantCulture) != "0"));
			}
		}
		foreach (var (name, unique) in names) {
			var ix = new SqlExportIndex { Name = name, Unique = unique };
			using var cmd = cn.CreateCommand();
			cmd.CommandText = "PRAGMA index_info(" + QuoteName(SqlTool.Sqlite, name) + ")";
			using var rd = cmd.ExecuteReader();
			var order = new List<(int Seq, string Col)>();
			while (rd.Read()) {
				var col = Convert.ToString(rd.GetValue(2), CultureInfo.InvariantCulture);
				if (string.IsNullOrEmpty(col)) continue;
				order.Add((Convert.ToInt32(rd.GetValue(0), CultureInfo.InvariantCulture), col));
			}
			order.Sort((a, b) => a.Seq.CompareTo(b.Seq));
			foreach (var o in order) ix.Columns.Add(o.Col);
			if (ix.Columns.Count > 0) t.Indexes.Add(ix);
		}
	}

	static void mysqlinDexes(DbConnection cn, SqlExportTable t) {
		var map = new Dictionary<string, SqlExportIndex>(StringComparer.OrdinalIgnoreCase);
		var order = new List<string>();
		using var cmd = cn.CreateCommand();
		cmd.CommandText = "SELECT INDEX_NAME, NON_UNIQUE, SEQ_IN_INDEX, COLUMN_NAME FROM information_schema.statistics "
			+ "WHERE table_schema = DATABASE() AND table_name = @t ORDER BY INDEX_NAME, SEQ_IN_INDEX";
		var p = cmd.CreateParameter();
		p.ParameterName = "@t";
		p.Value = t.Name;
		cmd.Parameters.Add(p);
		using var rd = cmd.ExecuteReader();
		while (rd.Read()) {
			var name = Convert.ToString(rd.GetValue(0), CultureInfo.InvariantCulture) ?? "";
			if (name.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase)) continue;
			if (!map.TryGetValue(name, out var ix)) {
				ix = new SqlExportIndex { Name = name, Unique = Convert.ToString(rd.GetValue(1), CultureInfo.InvariantCulture) == "0" };
				map[name] = ix;
				order.Add(name);
			}
			var col = Convert.ToString(rd.GetValue(3), CultureInfo.InvariantCulture);
			if (!string.IsNullOrEmpty(col)) ix.Columns.Add(col);
		}
		foreach (var name in order) {
			var ix = map[name];
			if (ix.Columns.Count > 0) t.Indexes.Add(ix);
		}
	}

	static void mssqlindexes(DbConnection cn, SqlExportTable t) {
		var map = new Dictionary<string, SqlExportIndex>(StringComparer.OrdinalIgnoreCase);
		var order = new List<string>();
		using var cmd = cn.CreateCommand();
		cmd.CommandText = "SELECT i.name, i.is_unique, ic.key_ordinal, c.name FROM sys.indexes i "
			+ "JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id "
			+ "JOIN sys.columns c ON c.object_id = i.object_id AND c.column_id = ic.column_id "
			+ "WHERE i.object_id = OBJECT_ID(@t) AND i.is_primary_key = 0 AND i.is_unique_constraint = 0 "
			+ "AND ic.is_included_column = 0 AND i.name IS NOT NULL ORDER BY i.name, ic.key_ordinal";
		var p = cmd.CreateParameter();
		p.ParameterName = "@t";
		p.Value = t.Name;
		cmd.Parameters.Add(p);
		using var rd = cmd.ExecuteReader();
		while (rd.Read()) {
			var name = Convert.ToString(rd.GetValue(0), CultureInfo.InvariantCulture) ?? "";
			if (!map.TryGetValue(name, out var ix)) {
				ix = new SqlExportIndex { Name = name, Unique = Convert.ToBoolean(rd.GetValue(1), CultureInfo.InvariantCulture) };
				map[name] = ix;
				order.Add(name);
			}
			var col = Convert.ToString(rd.GetValue(3), CultureInfo.InvariantCulture);
			if (!string.IsNullOrEmpty(col)) ix.Columns.Add(col);
		}
		foreach (var name in order) {
			var ix = map[name];
			if (ix.Columns.Count > 0) t.Indexes.Add(ix);
		}
	}

	/// <summary>从声明的类型名（含跨引擎信息）推断列类型，并取括号里的长度。</summary>
	public static SqlTypeKind kindofdeclared(string declared, out int size) {
		size = 0;
		var raw = (declared ?? "").Trim();
		if (raw.Length == 0) return SqlTypeKind.Text;
		var tn = raw.ToLowerInvariant();
		var lp = tn.IndexOf('(');
		if (lp > 0) {
			var rp = tn.IndexOf(')', lp);
			if (rp > lp) {
				var inner = tn.Substring(lp + 1, rp - lp - 1);
				var comma = inner.IndexOf(',');
				if (comma > 0) inner = inner.Substring(0, comma);
				int.TryParse(inner.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out size);
			}
			tn = tn.Substring(0, lp).Trim();
		}
		var uns = tn.EndsWith(" unsigned", StringComparison.Ordinal);
		if (uns) tn = tn.Substring(0, tn.Length - " unsigned".Length).Trim();
		switch (tn) {
		case "bit":
		case "boolean":
		case "bool":
			return SqlTypeKind.Bool;
		case "tinyint":
			return size == 1 && raw.IndexOf("tinyint(1)", StringComparison.OrdinalIgnoreCase) >= 0
				? SqlTypeKind.Bool : SqlTypeKind.Int;
		case "smallint":
		case "int":
		case "integer":
		case "mediumint":
			return SqlTypeKind.Int;
		case "bigint":
		case "int8":
			return SqlTypeKind.BigInt;
		case "decimal":
		case "numeric":
		case "money":
		case "smallmoney":
			return SqlTypeKind.Decimal;
		case "real":
		case "float":
		case "double":
		case "double precision":
			return SqlTypeKind.Float;
		case "date":
			return SqlTypeKind.Date;
		case "time":
		case "time without time zone":
			return SqlTypeKind.Time;
		case "datetime":
		case "datetime2":
		case "smalldatetime":
		case "timestamp":
		case "datetimeoffset":
			return SqlTypeKind.DateTime;
		case "blob":
		case "binary":
		case "varbinary":
		case "image":
		case "longblob":
		case "mediumblob":
		case "tinyblob":
		case "bytea":
		case "raw":
			return SqlTypeKind.Blob;
		case "uniqueidentifier":
		case "uuid":
		case "guid":
			return SqlTypeKind.Guid;
		case "char":
		case "nchar":
		case "varchar":
		case "nvarchar":
		case "text":
		case "ntext":
		case "clob":
		case "nclob":
		case "longtext":
		case "mediumtext":
		case "tinytext":
		case "varchar2":
		case "nvarchar2":
		case "json":
		case "jsonb":
			return SqlTypeKind.Text;
		}
		// SQLite 的宽松类型：先按名字兜底，再看长度
		if (tn.Contains("int")) return SqlTypeKind.Int;
		if (tn.Contains("char") || tn.Contains("text") || tn.Contains("clob")) return SqlTypeKind.Text;
		if (tn.Contains("blob")) return SqlTypeKind.Blob;
		if (tn.Contains("date") || tn.Contains("time")) return SqlTypeKind.DateTime;
		if (tn.Contains("real") || tn.Contains("floa") || tn.Contains("doub") || tn.Contains("num") || tn.Contains("dec"))
			return SqlTypeKind.Float;
		return SqlTypeKind.Text;
	}

	/// <summary>读一次结果集拿列信息。SchemaTable 各驱动字段不完全一致，缺字段按默认。</summary>
	public static List<SqlExportColumn> Columns(DataTable st) {
		var list = new List<SqlExportColumn>();
		if (st == null) return list;
		foreach (DataRow r in st.Rows) {
			var c = new SqlExportColumn {
				Name = cellstr(r, "ColumnName"),
				Size = (int)celllong(r, "ColumnSize"),
			};
			var dtype = cellobj(r, "DataType") as Type;
			var tname = cellstr(r, "DataTypeName");
			c.SourceType = tname;
			c.Kind = tname.Length > 0 ? kindofdeclared(tname, out _) : kindofdotnet(dtype);
			c.Nullable = cellobj(r, "AllowDBNull") is bool nb ? nb : true;
			c.PrimaryKey = cellobj(r, "IsKey") is bool key && key;
			if (c.Name.Length == 0) c.Name = "col" + (list.Count + 1);
			list.Add(c);
		}
		return list;
	}

	static SqlTypeKind kindofdotnet(Type t) {
		if (t == typeof(bool)) return SqlTypeKind.Bool;
		if (t == typeof(byte[])) return SqlTypeKind.Blob;
		if (t == typeof(Guid)) return SqlTypeKind.Guid;
		if (t == typeof(DateTime) || t == typeof(DateTimeOffset)) return SqlTypeKind.DateTime;
		if (t == typeof(TimeSpan)) return SqlTypeKind.Time;
		if (t == typeof(decimal) || t == typeof(double) || t == typeof(float)) return SqlTypeKind.Float;
		if (t == typeof(long) || t == typeof(ulong)) return SqlTypeKind.BigInt;
		if (t == typeof(int) || t == typeof(short) || t == typeof(byte) || t == typeof(uint) || t == typeof(ushort))
			return SqlTypeKind.Int;
		return SqlTypeKind.Text;
	}

	static object cellobj(DataRow r, string name) {
		try {
			if (!r.Table.Columns.Contains(name)) return null;
			var v = r[name];
			return v is DBNull ? null : v;
		}
		catch { return null; }
	}

	static string cellstr(DataRow r, string name) =>
		Convert.ToString(cellobj(r, name), CultureInfo.InvariantCulture) ?? "";

	static long celllong(DataRow r, string name) {
		var v = cellobj(r, name);
		try { return v == null ? 0 : Convert.ToInt64(v, CultureInfo.InvariantCulture); }
		catch { return 0; }
	}

	// ==================== 通用 ====================

	/// <summary>按源库方言给标识符加引号，支持 a.b 这种带架构/库的名字。</summary>
	public static string QuoteName(string kind, string name) {
		var parts = (name ?? "").Split('.');
		var sb = new StringBuilder();
		for (var i = 0; i < parts.Length; i++) {
			if (i > 0) sb.Append('.');
			sb.Append(SqlTool.Quote(kind, parts[i]));
		}
		return sb.ToString();
	}

	static string SelectOf(string kind, string table) => "SELECT * FROM " + QuoteName(kind, table);

	static DbDataReader openreader(DbConnection cn, string sql) {
		var cmd = cn.CreateCommand();
		cmd.CommandText = sql;
		cmd.CommandTimeout = 0;
		return cmd.ExecuteReader();
	}

	public static string SafeName(string name) {
		var bad = Path.GetInvalidFileNameChars();
		var sb = new StringBuilder();
		foreach (var ch in name ?? "") sb.Append(Array.IndexOf(bad, ch) >= 0 ? '_' : ch);
		var s = sb.ToString().Trim().TrimEnd('.');
		if (s.Length == 0) s = "table";
		return s.Length > 80 ? s.Substring(0, 80) : s;
	}

	static string UniqueFile(string baseName, HashSet<string> used) {
		var name = baseName;
		var n = 2;
		while (!used.Add(name)) {
			name = baseName + "_" + n;
			n++;
		}
		return name;
	}
}
