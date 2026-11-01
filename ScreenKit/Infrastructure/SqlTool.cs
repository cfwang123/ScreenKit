using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace ScreenKit;

/// <summary>一次查询或改表的结果。Columns 为空表示只有影响行数。</summary>
sealed class SqlResult {
	public List<string> Columns { get; } = new();
	public List<string[]> Rows { get; } = new();
	public int Affected { get; set; }
	public int Ms { get; set; }
	public string Note { get; set; } = "";
	public string Error { get; set; } = "";
}

/// <summary>SQLite / SQL Server / MySQL / Oracle 的连接串、表枚举、查询和表结构（都用 DbConnection，共用一套执行代码）。</summary>
static class SqlTool {
	public const string Sqlite = "sqlite";
	public const string Mssql = "mssql";
	public const string Mysql = "mysql";
	public const string Oracle = "oracle";

	public static string SqliteConn(string path) {
		var b = new SqliteConnectionStringBuilder {
			DataSource = path,
			Mode = SqliteOpenMode.ReadWriteCreate,
			Pooling = false,
		};
		return b.ConnectionString;
	}

	/// <summary>SQL Server 留空则连登录名的默认库，MySQL 留空则不选库；库在窗口里选。</summary>
	/// <param name="user">留空则用 Windows 身份验证。</param>
	public static string MssqlConn(string host, string user, string pw) {
		var b = new SqlConnectionStringBuilder {
			DataSource = (host ?? "").Trim(),
			TrustServerCertificate = true,
			ConnectTimeout = 8,
		};
		if (string.IsNullOrWhiteSpace(user)) b.IntegratedSecurity = true;
		else {
			b.UserID = user.Trim();
			b.Password = pw ?? "";
		}
		return b.ConnectionString;
	}

	/// <summary>Oracle：<paramref name="service"/> 为空时按 host 直连（EZ Connect），否则拼成 host/service。</summary>
	public static string OracleConn(string host, string service, string user, string pw) {
		var ds = (host ?? "").Trim().TrimEnd('/');
		var svc = (service ?? "").Trim().Trim('/');
		if (svc.Length > 0) ds += "/" + svc;
		var b = new DbConnectionStringBuilder {
			["Data Source"] = ds,
			["User Id"] = (user ?? "").Trim(),
			["Password"] = pw ?? "",
			["Connection Timeout"] = 8,
			["Pooling"] = false,
		};
		return b.ConnectionString;
	}

	/// <summary>MySQL：<paramref name="host"/> 可带端口（host:3306）；库连上后再选，不写进连接串。</summary>
	public static string MysqlConn(string host, string user, string pw) {
		var b = new DbConnectionStringBuilder {
			["Server"] = (host ?? "").Trim(),
			["User ID"] = (user ?? "").Trim(),
			["Password"] = pw ?? "",
			["Connection Timeout"] = 8,
			["Pooling"] = false,
		};
		return b.ConnectionString;
	}

	/// <summary>按引擎建连接。Oracle / MySQL 走反射加载的 DbProviderFactory，SQLite / SQL Server 直接用各自连接类。</summary>
	public static DbConnection Connect(string kind, string conn, out string err) {
		err = "";
		DbConnection cn;
		try {
			if (kind == Mssql) cn = new SqlConnection();
			else if (kind == Sqlite) cn = new SqliteConnection();
			else cn = DbDriver.Factory(DbKindOf(kind)).CreateConnection();
		}
		catch (Exception ex) {
			err = ex.Message;
			return null;
		}
		try {
			cn.ConnectionString = conn;
			cn.Open();
			return cn;
		}
		catch (Exception ex) {
			err = ex.Message;
			try { cn.Dispose(); } catch { }
			return null;
		}
	}

	public static DbKind DbKindOf(string kind) => kind switch {
		Mssql => DbKind.Mssql,
		Mysql => DbKind.Mysql,
		Oracle => DbKind.Oracle,
		_ => DbKind.Sqlite,
	};

	/// <summary>连上后能切的库。SQLite 只有一个文件、Oracle 只有当前模式，都返回空。</summary>
	public static List<string> Databases(DbConnection cn, string kind, out string err) {
		err = "";
		var list = new List<string>();
		if (cn == null) return list;
		string sql;
		if (kind == Mssql) sql = "SELECT name FROM sys.databases ORDER BY name";
		else if (kind == Mysql) sql = "SHOW DATABASES";
		else return list;
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

	/// <summary>切库。SQL Server 换当前目录，MySQL 发 USE；SQLite / Oracle 没有库的概念，忽略。</summary>
	public static bool UseDb(DbConnection cn, string kind, string db, out string err) {
		err = "";
		if (cn == null || string.IsNullOrWhiteSpace(db)) return true;
		try {
			if (kind == Mssql) cn.ChangeDatabase(db);
			else if (kind == Mysql) {
				using var cmd = cn.CreateCommand();
				cmd.CommandText = "USE " + quote(kind, db);
				cmd.ExecuteNonQuery();
			}
			return true;
		}
		catch (Exception ex) {
			err = ex.Message;
			return false;
		}
	}

	public static List<string> Tables(DbConnection cn, string kind, out string err) {
		err = "";
		var list = new List<string>();
		if (cn == null) return list;
		string sql;
		if (kind == Mssql)
			sql = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE IN ('BASE TABLE','VIEW') ORDER BY TABLE_NAME";
		else if (kind == Oracle)
			// 当前用户模式下的表与视图
			sql = "SELECT TABLE_NAME FROM USER_TABLES UNION SELECT VIEW_NAME FROM USER_VIEWS ORDER BY 1";
		else if (kind == Mysql)
			sql = "SELECT TABLE_NAME FROM information_schema.tables WHERE table_schema = DATABASE() " +
				"AND table_type IN ('BASE TABLE','VIEW') ORDER BY TABLE_NAME";
		else
			sql = "SELECT name FROM sqlite_master WHERE type IN ('table','view') AND name NOT LIKE 'sqlite_%' ORDER BY name";
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

	/// <summary>执行任意语句。有结果集就填 Columns/Rows，否则给出影响行数。</summary>
	public static SqlResult Run(DbConnection cn, string sql, int maxRows) {
		var res = new SqlResult();
		if (cn == null) {
			res.Error = Loc.T("sql.needconn");
			return res;
		}
		var tick = Environment.TickCount;
		try {
			using var cmd = cn.CreateCommand();
			cmd.CommandText = sql;
			cmd.CommandTimeout = 30;
			using var rd = cmd.ExecuteReader();
			if (rd.FieldCount > 0) {
				var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
				for (var i = 0; i < rd.FieldCount; i++)
					res.Columns.Add(unique(rd.GetName(i), i, seen));
				while (rd.Read()) {
					if (maxRows > 0 && res.Rows.Count >= maxRows) {
						res.Note = Loc.T("sql.truncated", maxRows);
						break;
					}
					var row = new string[rd.FieldCount];
					for (var i = 0; i < row.Length; i++) row[i] = text(rd.GetValue(i));
					res.Rows.Add(row);
				}
			}
			res.Affected = rd.RecordsAffected;
		}
		catch (Exception ex) {
			res.Error = ex.Message;
		}
		res.Ms = Environment.TickCount - tick;
		return res;
	}

	/// <summary>表结构。SQLite 走 PRAGMA table_info，SQL Server / MySQL 走 INFORMATION_SCHEMA.COLUMNS，Oracle 走 USER_TAB_COLUMNS。</summary>
	public static SqlResult Schema(DbConnection cn, string kind, string table) {
		var res = new SqlResult();
		if (cn == null || string.IsNullOrEmpty(table)) {
			res.Error = Loc.T("sql.needtable");
			return res;
		}
		var tick = Environment.TickCount;
		try {
			using var cmd = cn.CreateCommand();
			if (kind == Mssql) {
				cmd.CommandText = "SELECT ORDINAL_POSITION, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, " +
					"IS_NULLABLE, COLUMN_DEFAULT FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t " +
					"ORDER BY ORDINAL_POSITION";
				var p = cmd.CreateParameter();
				p.ParameterName = "@t";
				p.Value = table;
				cmd.Parameters.Add(p);
				res.Columns.Add(Loc.T("sql.schema.no"));
				res.Columns.Add(Loc.T("sql.schema.name"));
				res.Columns.Add(Loc.T("sql.schema.type"));
				res.Columns.Add(Loc.T("sql.schema.len"));
				res.Columns.Add(Loc.T("sql.schema.null"));
				res.Columns.Add(Loc.T("sql.schema.def"));
				using var rd = cmd.ExecuteReader();
				while (rd.Read()) {
					var row = new string[6];
					for (var i = 0; i < 6; i++) row[i] = text(rd.GetValue(i));
					res.Rows.Add(row);
				}
			}
			else if (kind == Oracle) {
				cmd.CommandText = "SELECT COLUMN_ID, COLUMN_NAME, DATA_TYPE, DATA_LENGTH, NULLABLE, DATA_DEFAULT " +
					"FROM USER_TAB_COLUMNS WHERE TABLE_NAME = :t ORDER BY COLUMN_ID";
				var p = cmd.CreateParameter();
				p.ParameterName = "t";
				p.Value = table;
				cmd.Parameters.Add(p);
				res.Columns.Add(Loc.T("sql.schema.no"));
				res.Columns.Add(Loc.T("sql.schema.name"));
				res.Columns.Add(Loc.T("sql.schema.type"));
				res.Columns.Add(Loc.T("sql.schema.len"));
				res.Columns.Add(Loc.T("sql.schema.null"));
				res.Columns.Add(Loc.T("sql.schema.def"));
				using (var rd = cmd.ExecuteReader()) {
					while (rd.Read()) {
						res.Rows.Add(new[] {
							text(rd.GetValue(0)),
							text(rd.GetValue(1)),
							text(rd.GetValue(2)),
							text(rd.GetValue(3)),
							text(rd.GetValue(4)) == "Y" ? "√" : "",
							text(rd.GetValue(5)),
						});
					}
				}
				// 建表语句：DBMS_METADATA 需要权限，取不到就算了
				try {
					using var cmd2 = cn.CreateCommand();
					cmd2.CommandText = "SELECT DBMS_METADATA.GET_DDL('TABLE', :t) FROM DUAL";
					var p2 = cmd2.CreateParameter();
					p2.ParameterName = "t";
					p2.Value = table;
					cmd2.Parameters.Add(p2);
					using var rd2 = cmd2.ExecuteReader();
					if (rd2.Read()) res.Note = rd2.GetString(0) ?? "";
				}
				catch { res.Note = ""; }
			}
			else if (kind == Mysql) {
				cmd.CommandText = "SELECT ORDINAL_POSITION, COLUMN_NAME, COLUMN_TYPE, CHARACTER_MAXIMUM_LENGTH, " +
					"IS_NULLABLE, COLUMN_DEFAULT FROM information_schema.columns " +
					"WHERE table_schema = DATABASE() AND table_name = @t ORDER BY ORDINAL_POSITION";
				var p = cmd.CreateParameter();
				p.ParameterName = "@t";
				p.Value = table;
				cmd.Parameters.Add(p);
				res.Columns.Add(Loc.T("sql.schema.no"));
				res.Columns.Add(Loc.T("sql.schema.name"));
				res.Columns.Add(Loc.T("sql.schema.type"));
				res.Columns.Add(Loc.T("sql.schema.len"));
				res.Columns.Add(Loc.T("sql.schema.null"));
				res.Columns.Add(Loc.T("sql.schema.def"));
				using (var rd = cmd.ExecuteReader()) {
					while (rd.Read()) {
						res.Rows.Add(new[] {
							text(rd.GetValue(0)),
							text(rd.GetValue(1)),
							text(rd.GetValue(2)),
							text(rd.GetValue(3)),
							text(rd.GetValue(4)) == "YES" ? "√" : "",
							text(rd.GetValue(5)),
						});
					}
				}
				// 建表语句：SHOW CREATE TABLE
				try {
					using var cmd2 = cn.CreateCommand();
					cmd2.CommandText = "SHOW CREATE TABLE " + quote(kind, table);
					using var rd2 = cmd2.ExecuteReader();
					if (rd2.Read() && rd2.FieldCount > 1) res.Note = text(rd2.GetValue(1));
				}
				catch { res.Note = ""; }
			}
			else {
				cmd.CommandText = "PRAGMA table_info(" + quote(kind, table) + ")";
				res.Columns.Add(Loc.T("sql.schema.no"));
				res.Columns.Add(Loc.T("sql.schema.name"));
				res.Columns.Add(Loc.T("sql.schema.type"));
				res.Columns.Add(Loc.T("sql.schema.null"));
				res.Columns.Add(Loc.T("sql.schema.def"));
				res.Columns.Add(Loc.T("sql.schema.pk"));
				using (var rd = cmd.ExecuteReader()) {
					while (rd.Read()) {
						// cid,name,type,notnull,dflt_value,pk
						res.Rows.Add(new[] {
							text(rd.GetValue(0)),
							text(rd.GetValue(1)),
							text(rd.GetValue(2)),
							text(rd.GetValue(3)) == "0" ? "√" : "",
							text(rd.GetValue(4)),
							text(rd.GetValue(5)) == "0" ? "" : text(rd.GetValue(5)),
						});
					}
				}
				using var cmd2 = cn.CreateCommand();
				cmd2.CommandText = "SELECT sql FROM sqlite_master WHERE name = @t";
				var p = cmd2.CreateParameter();
				p.ParameterName = "@t";
				p.Value = table;
				cmd2.Parameters.Add(p);
				var o = cmd2.ExecuteScalar();
				res.Note = o == null || o is DBNull ? "" : Convert.ToString(o, CultureInfo.InvariantCulture);
			}
		}
		catch (Exception ex) {
			res.Error = ex.Message;
		}
		res.Ms = Environment.TickCount - tick;
		return res;
	}

	/// <summary>取表内容的 SQL；top 为 0 时取全部。</summary>
	public static string TableSql(string kind, string table, int top) {
		var q = quote(kind, table);
		if (kind == Mssql) return top > 0 ? $"SELECT TOP ({top}) * FROM {q}" : $"SELECT * FROM {q}";
		if (kind == Oracle) return top > 0 ? $"SELECT * FROM {q} WHERE ROWNUM <= {top}" : $"SELECT * FROM {q}";
		return top > 0 ? $"SELECT * FROM {q} LIMIT {top}" : $"SELECT * FROM {q}";
	}

	static string quote(string kind, string name) {
		if (kind == Mssql) return "[" + (name ?? "").Replace("]", "]]") + "]";
		if (kind == Mysql) return "`" + (name ?? "").Replace("`", "``") + "`";
		return "\"" + (name ?? "").Replace("\"", "\"\"") + "\"";
	}

	static string unique(string name, int index, Dictionary<string, int> seen) {
		if (string.IsNullOrWhiteSpace(name)) name = "col" + (index + 1);
		if (!seen.TryGetValue(name, out var n)) {
			seen[name] = 1;
			return name;
		}
		string next;
		do {
			n++;
			next = name + "_" + n;
		} while (seen.ContainsKey(next));
		seen[name] = n;
		seen[next] = 1;
		return next;
	}

	static string text(object v) {
		if (v == null || v is DBNull) return "NULL";
		if (v is byte[] b) return $"<{b.Length} bytes>";
		// 时间部分是 0 就只给日期，免得纯日期列全带一串 00:00:00
		if (v is DateTime dt)
			return dt.TimeOfDay == TimeSpan.Zero
				? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
				: dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
		if (v is DateTimeOffset dto)
			return dto.TimeOfDay == TimeSpan.Zero
				? dto.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
				: dto.ToString("yyyy-MM-dd HH:mm:sszzz", CultureInfo.InvariantCulture);
		if (v is bool bo) return bo ? "1" : "0";
		return Convert.ToString(v, CultureInfo.InvariantCulture);
	}
}
