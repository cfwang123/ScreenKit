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

/// <summary>SQLite 与 SQL Server 的连接串、表枚举、查询和表结构（都用 DbConnection，共用一套执行代码）。</summary>
static class SqlTool {
	public const string Sqlite = "sqlite";
	public const string Mssql = "mssql";

	public static string SqliteConn(string path) {
		var b = new SqliteConnectionStringBuilder {
			DataSource = path,
			Mode = SqliteOpenMode.ReadWriteCreate,
			Pooling = false,
		};
		return b.ConnectionString;
	}

	/// <param name="user">留空则用 Windows 身份验证。</param>
	public static string MssqlConn(string host, string db, string user, string pw) {
		var b = new SqlConnectionStringBuilder {
			DataSource = (host ?? "").Trim(),
			InitialCatalog = (db ?? "").Trim(),
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

	public static DbConnection Connect(string kind, string conn, out string err) {
		err = "";
		DbConnection cn = kind == Mssql ? new SqlConnection(conn) : (DbConnection)new SqliteConnection(conn);
		try {
			cn.Open();
			return cn;
		}
		catch (Exception ex) {
			err = ex.Message;
			try { cn.Dispose(); } catch { }
			return null;
		}
	}

	public static List<string> Tables(DbConnection cn, string kind, out string err) {
		err = "";
		var list = new List<string>();
		if (cn == null) return list;
		var sql = kind == Mssql
			? "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE IN ('BASE TABLE','VIEW') ORDER BY TABLE_NAME"
			: "SELECT name FROM sqlite_master WHERE type IN ('table','view') AND name NOT LIKE 'sqlite_%' ORDER BY name";
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

	/// <summary>表结构。SQLite 走 PRAGMA table_info，SQL Server 走 INFORMATION_SCHEMA.COLUMNS。</summary>
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
		return top > 0 ? $"SELECT * FROM {q} LIMIT {top}" : $"SELECT * FROM {q}";
	}

	static string quote(string kind, string name) =>
		kind == Mssql
			? "[" + (name ?? "").Replace("]", "]]") + "]"
			: "\"" + (name ?? "").Replace("\"", "\"\"") + "\"";

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
		if (v is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
		if (v is DateTimeOffset dto) return dto.ToString("yyyy-MM-dd HH:mm:sszzz", CultureInfo.InvariantCulture);
		if (v is bool bo) return bo ? "1" : "0";
		return Convert.ToString(v, CultureInfo.InvariantCulture);
	}
}
