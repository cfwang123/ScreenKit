using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ScreenKit;

/// <summary>SQL 脚本的引擎。SQL Server / MySQL / SQLite / Oracle / PostgreSQL。</summary>
enum SqlEngine {
	Sqlite,
	Mysql,
	Mssql,
	Oracle,
	Postgres,
}

/// <summary>识别结果的展示名与版本判断。</summary>
static class SqlEngines {
	public static string Name(SqlEngine e) => e switch {
		SqlEngine.Mysql => "MySQL",
		SqlEngine.Mssql => "SQL Server",
		SqlEngine.Oracle => "Oracle",
		SqlEngine.Postgres => "PostgreSQL",
		_ => "SQLite",
	};

	public static string Code(SqlEngine e) => e switch {
		SqlEngine.Mysql => SqlTool.Mysql,
		SqlEngine.Mssql => SqlTool.Mssql,
		SqlEngine.Oracle => SqlTool.Oracle,
		SqlEngine.Postgres => "postgres",
		_ => SqlTool.Sqlite,
	};

	/// <summary>可选版本。SQLite 只列大版本，Oracle 按发布版本，其余按官方大版本。</summary>
	public static List<SqlTargets.Item> Versions(SqlEngine e) {
		switch (e) {
		case SqlEngine.Mssql:
			return new List<SqlTargets.Item> {
				new("SQL Server 2005", new Version(9, 0)),
				new("SQL Server 2008", new Version(10, 0)),
				new("SQL Server 2012", new Version(11, 0)),
				new("SQL Server 2014", new Version(12, 0)),
				new("SQL Server 2016", new Version(13, 0)),
				new("SQL Server 2017", new Version(14, 0)),
				new("SQL Server 2019", new Version(15, 0)),
				new("SQL Server 2022", new Version(16, 0)),
			};
		case SqlEngine.Mysql:
			return new List<SqlTargets.Item> {
				new("MySQL 5.5", new Version(5, 5)),
				new("MySQL 5.6", new Version(5, 6)),
				new("MySQL 5.7", new Version(5, 7)),
				new("MySQL 8.0", new Version(8, 0)),
			};
		case SqlEngine.Oracle:
			return new List<SqlTargets.Item> {
				new("Oracle 11g", new Version(11, 0)),
				new("Oracle 12c", new Version(12, 0)),
				new("Oracle 18c", new Version(18, 0)),
				new("Oracle 19c", new Version(19, 0)),
				new("Oracle 21c", new Version(21, 0)),
			};
		case SqlEngine.Postgres:
			return new List<SqlTargets.Item> {
				new("PostgreSQL 9.6", new Version(9, 6)),
				new("PostgreSQL 10", new Version(10, 0)),
				new("PostgreSQL 11", new Version(11, 0)),
				new("PostgreSQL 13", new Version(13, 0)),
				new("PostgreSQL 16", new Version(16, 0)),
			};
		default:
			return new List<SqlTargets.Item> {
				new("SQLite 3.8", new Version(3, 8)),
				new("SQLite 3.24", new Version(3, 24)),
				new("SQLite 3.35", new Version(3, 35)),
				new("SQLite 3.45", new Version(3, 45)),
			};
		}
	}

	/// <summary>没识别出版本时的保守取值：老语法在目标端总能跑。</summary>
	public static Version Default(SqlEngine e) => e switch {
		SqlEngine.Mssql => new Version(10, 0),
		SqlEngine.Mysql => new Version(5, 6),
		SqlEngine.Oracle => new Version(11, 0),
		SqlEngine.Postgres => new Version(9, 6),
		_ => new Version(3, 24),
	};

	public static string Label(SqlEngine e, Version v) {
		if (v == null) return Name(e);
		if (e == SqlEngine.Mssql) {
			var year = v.Major switch {
				9 => 2005, 10 => 2008, 11 => 2012, 12 => 2014,
				13 => 2016, 14 => 2017, 15 => 2019, 16 => 2022, _ => 0,
			};
			return year > 0 ? $"SQL Server {year}" : $"{Name(e)} {v}";
		}
		if (e == SqlEngine.Oracle) return $"Oracle {v.Major}c";
		if (e == SqlEngine.Postgres) return v.Minor > 0 ? $"PostgreSQL {v.Major}.{v.Minor}" : $"PostgreSQL {v.Major}";
		if (e == SqlEngine.Mysql) return $"MySQL {v.Major}.{v.Minor}";
		return $"SQLite {v.Major}.{v.Minor}";
	}
}

/// <summary>识别到的源引擎、版本和依据（依据给用户看，也解释为什么选中这个版本）。</summary>
sealed class SqlDetect {
	public SqlEngine Engine = SqlEngine.Sqlite;
	public Version Version;
	public bool VersionKnown;
	public readonly List<string> Reasons = new();

	public string Summary => VersionKnown
		? $"{SqlEngines.Label(Engine, Version)}"
		: $"{SqlEngines.Name(Engine)}（版本未识别，按 {SqlEngines.Label(Engine, Version = SqlEngines.Default(Engine))} 处理）";
}

/// <summary>转换结果：脚本正文 + 统计，供界面显示。</summary>
sealed class SqlConvertResult {
	public string Text = "";
	public int Tables;
	public int Indexes;
	public int Inserts;
	public long Rows;
	/// <summary>原样保留、未做方言改写的语句数（复杂语句会保留原文）。</summary>
	public int Kept;
	public string Error = "";
}

/// <summary>转换选项。</summary>
sealed class SqlConvertOptions {
	public SqlEngine Source = SqlEngine.Sqlite;
	public Version SourceVersion;
	public SqlEngine Target = SqlEngine.Mysql;
	public Version TargetVersion;
	public bool WithDrop = true;
	public bool KeepComments = true;
}

/// <summary>解析出来的一张表：列、索引、可带注释。</summary>
sealed class SqlCvTable {
	public string Name = "";
	public readonly List<SqlCvColumn> Columns = new();
	public readonly List<SqlCvIndex> Indexes = new();
	public string Comment = "";
	public string Prefix = "";
}

sealed class SqlCvColumn {
	public string Name = "";
	public string SourceType = "";
	public SqlTypeKind Kind = SqlTypeKind.Text;
	public int Size;
	public int Precision;
	public int Scale;
	public bool Nullable = true;
	public bool PrimaryKey;
	public bool Identity;
	public bool Unique;
	public string Default = "";
	public string Comment = "";
	/// <summary>生成列表达式（原方言写法），没有生成列就是空。</summary>
	public string Generated = "";
}

sealed class SqlCvIndex {
	public string Name = "";
	public bool Unique;
	public List<string> Columns = new();
}

/// <summary>解析出来的一条 INSERT：列名与已经拆好的字面量。</summary>
sealed class SqlCvInsert {
	public string Table = "";
	public readonly List<string> Columns = new();
	/// <summary>每行一组原始字面量文本，按源方言书写。</summary>
	public readonly List<List<string>> Rows = new();
}

/// <summary>
/// 把一份 .sql 脚本识别出引擎和版本，再改写成另一种方言。
/// 认得的语句按目标方言重建（建表、索引、INSERT、删除表），
/// 认不得的（复杂 ALTER、存储过程、SELECT）原样保留并在统计里计数。
/// </summary>
static class SqlScript {
	// ==================== 识别 ====================

	const string Ksqlserver = "sqlserver";
	const string Kmysql = "mysql";
	const string Koracle = "oracle";
	const string Kpostgres = "postgres";

	/// <summary>按语句里的特征词打分，取分最高者为源引擎；版本取命中的最高特征。</summary>
	public static SqlDetect Detect(string text) {
		var d = new SqlDetect();
		var s = text ?? "";
		var low = s.ToLowerInvariant();
		var score = new Dictionary<SqlEngine, int> {
			[SqlEngine.Sqlite] = 0,
			[SqlEngine.Mysql] = 0,
			[SqlEngine.Mssql] = 0,
			[SqlEngine.Oracle] = 0,
			[SqlEngine.Postgres] = 0,
		};
		void hit(SqlEngine e, int n, string why) {
			score[e] += n;
			if (n >= 2 && d.Reasons.Count < 8 && !d.Reasons.Contains(why)) d.Reasons.Add(why);
		}

		if (low.Contains("pragma ")) hit(SqlEngine.Sqlite, 4, "PRAGMA 语句");
		if (low.Contains("autoincrement")) hit(SqlEngine.Sqlite, 3, "AUTOINCREMENT");
		if (low.Contains("without rowid")) hit(SqlEngine.Sqlite, 3, "WITHOUT ROWID");
		if (low.Contains("sqlite_")) hit(SqlEngine.Sqlite, 2, "sqlite_ 系统表");

		if (low.Contains("engine=innodb") || low.Contains("engine=myisam")) hit(SqlEngine.Mysql, 4, "ENGINE=InnoDB 表选项");
		if (low.Contains("auto_increment")) hit(SqlEngine.Mysql, 4, "AUTO_INCREMENT");
		if (low.Contains("utf8mb4") || low.Contains("latin1")) hit(SqlEngine.Mysql, 2, "字符集选项");
		if (low.Contains("/*!4")) hit(SqlEngine.Mysql, 4, "mysqldump 版本注释");
		if (low.Contains("`")) hit(SqlEngine.Mysql, 1, "反引号标识符");
		if (low.Contains(" longtext") || low.Contains(" mediumtext") || low.Contains(" tinyint(1)"))
			hit(SqlEngine.Mysql, 2, "MySQL 专有类型");
		if (low.Contains("set names ")) hit(SqlEngine.Mysql, 2, "SET NAMES");

		if (low.Contains("identity(1,1)") || low.Contains("identity (")) hit(SqlEngine.Mssql, 4, "IDENTITY(1,1)");
		if (low.Contains("set identity_insert")) hit(SqlEngine.Mssql, 4, "SET IDENTITY_INSERT");
		if (low.Contains("nvarchar") || low.Contains("datetime2") || low.Contains("uniqueidentifier"))
			hit(SqlEngine.Mssql, 3, "SQL Server 专有类型");
		if (low.Contains("[dbo]") || low.Contains("dbo.")) hit(SqlEngine.Mssql, 2, "dbo 架构");
		if (low.Contains("set nocount on")) hit(SqlEngine.Mssql, 3, "SET NOCOUNT ON");
		if (Regex.IsMatch(low, @"(?m)^\s*go\s*$")) hit(SqlEngine.Mssql, 2, "GO 批处理分隔");
		if (low.Contains("[") && low.Contains("]") && low.Contains("create table")) hit(SqlEngine.Mssql, 1, "方括号标识符");

		if (low.Contains("varchar2") || low.Contains("nvarchar2")) hit(SqlEngine.Oracle, 4, "VARCHAR2");
		if (low.Contains(" from dual")) hit(SqlEngine.Oracle, 4, "FROM DUAL");
		if (low.Contains("nvl(") || low.Contains("nvl2(")) hit(SqlEngine.Oracle, 2, "NVL 函数");
		if (low.Contains("number(")) hit(SqlEngine.Oracle, 2, "NUMBER 类型");
		if (low.Contains("tablespace ")) hit(SqlEngine.Oracle, 3, "TABLESPACE");
		if (low.Contains(".nextval")) hit(SqlEngine.Oracle, 3, "序列 NEXTVAL");
		if (low.Contains("sysdate")) hit(SqlEngine.Oracle, 2, "SYSDATE");

		if (low.Contains("serial") && low.Contains("create table")) hit(SqlEngine.Postgres, 3, "SERIAL 自增");
		if (low.Contains("bytea") || low.Contains("jsonb") || low.Contains("timestamptz") || low.Contains("::"))
			hit(SqlEngine.Postgres, 3, "PostgreSQL 专有写法");
		if (low.Contains("set standard_conforming_strings")) hit(SqlEngine.Postgres, 3, "SET standard_conforming_strings");
		if (low.Contains("public.")) hit(SqlEngine.Postgres, 2, "public 架构");

		var best = SqlEngine.Sqlite;
		var max = -1;
		foreach (var kv in score) {
			// 同分时按更常见的库优先，避免全空的脚本被认成 Oracle
			if (kv.Value > max) {
				max = kv.Value;
				best = kv.Key;
			}
		}
		d.Engine = best;
		if (max <= 0) d.Reasons.Add("没有明显的方言特征，按 SQLite 处理");
		var ver = detectversion(best, s, d.Reasons, out var known);
		d.Version = ver;
		d.VersionKnown = known;
		return d;
	}

	/// <summary>版本按脚本里出现的语法特征取最高的一个；mysqldump 的头部注释优先。</summary>
	static Version detectversion(SqlEngine e, string text, List<string> reasons, out bool known) {
		known = false;
		var low = text.ToLowerInvariant();
		var m = Regex.Match(low, @"server version[ \t:]*(\d+)\.(\d+)\.?(\d+)?");
		if (m.Success) {
			if (int.TryParse(m.Groups[1].Value, out var maj) && int.TryParse(m.Groups[2].Value, out var min)) {
				known = true;
				reasons.Add($"脚本头部注释：Server version {maj}.{min}");
				return new Version(maj, min);
			}
		}
		switch (e) {
		case SqlEngine.Mssql: {
			var ver = new Version(10, 0);
			if (low.Contains("greatest(") || low.Contains("generate_series(") || low.Contains("date_bucket(")) {
				ver = new Version(16, 0);
				reasons.Add("GREATEST / GENERATE_SERIES 是 2022 起");
			}
			else if (low.Contains("string_agg(") || low.Contains("trim(") || low.Contains("concat_ws(")) {
				ver = new Version(14, 0);
				reasons.Add("STRING_AGG / TRIM 是 2017 起");
			}
			else if (low.Contains("iif(") || low.Contains("try_convert(") || low.Contains(" offset ") || low.Contains("fetch next")) {
				ver = new Version(11, 0);
				reasons.Add("IIF / OFFSET-FETCH 是 2012 起");
			}
			else if (low.Contains("datetime2") || low.Contains("datetimeoffset") || low.Contains("date '")
				|| low.Contains("drop table if exists")) {
				ver = new Version(13, 0);
				reasons.Add("datetime2 / DROP TABLE IF EXISTS 是 2008 起，IF EXISTS 写法按 2016");
			}
			if (reasons.Exists(x => x.StartsWith("datetime2", StringComparison.Ordinal))) ver = new Version(13, 0);
			known = reasons.Exists(x => x.Contains("起") || x.StartsWith("脚本头部", StringComparison.Ordinal));
			if (!known) ver = new Version(13, 0);
			return ver;
		}
		case SqlEngine.Mysql: {
			var ver = new Version(5, 6);
			if (low.Contains("utf8mb4_0900") || low.Contains(" invisible")) {
				ver = new Version(8, 0);
				reasons.Add("utf8mb4_0900 / INVISIBLE 是 8.0 起");
				known = true;
			}
			else if (low.Contains(" json ") || low.Contains("generated always as")) {
				ver = new Version(5, 7);
				reasons.Add("JSON 类型是 5.7 起");
				known = true;
			}
			else if (low.Contains("datetime(3)") || low.Contains("datetime(6)")) {
				ver = new Version(5, 6);
				reasons.Add("DATETIME(n) 小数秒是 5.6 起");
				known = true;
			}
			if (!known) ver = new Version(5, 6);
			return ver;
		}
		case SqlEngine.Oracle: {
			var ver = new Version(11, 0);
			if (low.Contains(" identity") || low.Contains("fetch first") || low.Contains("json_value(")) {
				ver = new Version(12, 0);
				reasons.Add("IDENTITY 列 / FETCH FIRST 是 12c 起");
				known = true;
			}
			return ver;
		}
		case SqlEngine.Postgres: {
			var ver = new Version(9, 6);
			if (low.Contains("generated always as identity") || low.Contains("generated by default as identity")) {
				ver = new Version(10, 0);
				reasons.Add("IDENTITY 列是 10 起");
				known = true;
			}
			else if (low.Contains("istored") || low.Contains("generated always as (")) {
				ver = new Version(12, 0);
				reasons.Add("生成列是 12 起");
				known = true;
			}
			return ver;
		}
		default: {
			var ver = new Version(3, 24);
			if (low.Contains(" strict;") || low.Contains(" strict,") || low.Contains(") strict")) {
				ver = new Version(3, 37);
				reasons.Add("STRICT 表是 3.37 起");
				known = true;
			}
			else if (low.Contains("returning ")) {
				ver = new Version(3, 35);
				reasons.Add("RETURNING 是 3.35 起");
				known = true;
			}
			else if (low.Contains("on conflict") || low.Contains("without rowid")) {
				ver = new Version(3, 24);
				reasons.Add("UPSERT 是 3.24 起");
				known = true;
			}
			return ver;
		}
		}
	}

	// ==================== 转换入口 ====================

	public static SqlConvertResult Convert(string text, SqlConvertOptions o) {
		var res = new SqlConvertResult();
		if (string.IsNullOrWhiteSpace(text)) {
			res.Error = Loc.T("sqlconv.notext");
			return res;
		}
		var src = o.Source;
		var dst = o.Target;
		var dver = o.TargetVersion ?? SqlEngines.Default(dst);
		var sb = new StringBuilder();
		sb.Append("-- ").Append(Loc.T("sqlconv.header", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append("\r\n");
		sb.Append("-- ").Append(Loc.T("sqlconv.header.source", SqlEngines.Label(src, o.SourceVersion))).Append("\r\n");
		sb.Append("-- ").Append(Loc.T("sqlconv.header.target", SqlEngines.Label(dst, dver))).Append("\r\n");
		sb.Append("\r\n");
		appendpreamble(sb, dst);

		var tables = new List<SqlCvTable>();
		var inserts = new List<SqlCvInsert>();
		var others = new List<string>();

		foreach (var raw in SplitStatements(text)) {
			var body = stripcomments(raw.Trim());
			if (body.Length == 0) continue;
			var head = keywordof(body);
			if (head == "drop" && Regex.IsMatch(body, @"^drop\s+table", RegexOptions.IgnoreCase)) continue;
			if (head is "set" or "pragma" or "use" or "go" or "begin" or "commit" or "start" or "lock" or "unlock") continue;
			if (Regex.IsMatch(body, @"^(create|alter)\s+(database|schema|user|login|tablespace)\b", RegexOptions.IgnoreCase)) continue;

			if (Regex.IsMatch(body, @"^create\s+table\b", RegexOptions.IgnoreCase)) {
				var t = parsetable(body, o, dst);
				if (t == null) {
					others.Add(raw.Trim());
					continue;
				}
				// 同名表后出现的覆盖前面；在表内部的索引也一并收下
				tables.Add(t);
				continue;
			}
			if (Regex.IsMatch(body, @"^create\s+(unique\s+)?(clustered\s+)?index\b", RegexOptions.IgnoreCase)) {
				var ix = parseindex(body, tables);
				if (ix == null) others.Add(raw.Trim());
				continue;
			}
			if (Regex.IsMatch(body, @"^insert\s+into\b", RegexOptions.IgnoreCase)) {
				var ins = parseinsert(body, tables);
				if (ins != null) inserts.Add(ins);
				else others.Add(raw.Trim());
				continue;
			}
			others.Add(raw.Trim());
		}

		// 表在前、数据在后：导入时外键才不会指向还不存在的表
		foreach (var t in tables) {
			if (t.Columns.Count == 0) continue;
			if (o.KeepComments && t.Comment.Length > 0) sb.Append("-- ").Append(t.Comment).Append("\r\n");
			sb.Append(createtable(t, dst, dver, o.WithDrop));
			foreach (var ix in t.Indexes) sb.Append(createindex(t, ix, dst, dver));
			sb.Append("\r\n");
			res.Tables++;
			res.Indexes += t.Indexes.Count;
		}
		foreach (var ins in inserts) {
			var t = tables.Find(x => same(x.Name, ins.Table));
			sb.Append(insertof(ins, t, o, dver, res));
			sb.Append("\r\n");
			res.Inserts++;
		}
		if (others.Count > 0) {
			sb.Append("-- ").Append(Loc.T("sqlconv.header.kept", others.Count)).Append("\r\n");
			foreach (var s in others) sb.Append(wrapkeep(s, dst)).Append("\r\n");
			res.Kept = others.Count;
		}
		appendfooter(sb, dst, tables);
		res.Text = sb.ToString();
		return res;
	}

	static string wrapkeep(string s, SqlEngine dst) {
		// 原样保留的语句用注释框起来，导入时不会因方言不合直接报错
		if (dst == SqlEngine.Oracle) {
			var lines = s.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
			var sb = new StringBuilder();
			foreach (var l in lines) sb.Append("-- ").Append(l).Append("\r\n");
			return sb.ToString().TrimEnd();
		}
		var t = s.TrimEnd();
		if (!t.EndsWith(";", StringComparison.Ordinal)) t += ";";
		return t;
	}

	static void appendpreamble(StringBuilder sb, SqlEngine e) {
		switch (e) {
		case SqlEngine.Mysql:
			sb.Append("SET NAMES utf8mb4;\r\n");
			sb.Append("SET FOREIGN_KEY_CHECKS = 0;\r\n\r\n");
			break;
		case SqlEngine.Sqlite:
			sb.Append("PRAGMA foreign_keys = OFF;\r\n\r\n");
			break;
		case SqlEngine.Mssql:
			sb.Append("SET NOCOUNT ON;\r\n\r\n");
			break;
		case SqlEngine.Postgres:
			sb.Append("SET client_encoding = 'UTF8';\r\n");
			sb.Append("SET standard_conforming_strings = on;\r\n\r\n");
			break;
		default:
			sb.Append("SET DEFINE OFF;\r\n\r\n");
			break;
		}
	}

	static void appendfooter(StringBuilder sb, SqlEngine e, List<SqlCvTable> tables) {
		if (e == SqlEngine.Mysql) sb.Append("SET FOREIGN_KEY_CHECKS = 1;\r\n");
		else if (e == SqlEngine.Sqlite) sb.Append("PRAGMA foreign_keys = ON;\r\n");
		else if (e == SqlEngine.Oracle) sb.Append("COMMIT;\r\n");
		else if (e == SqlEngine.Postgres) sb.Append("COMMIT;\r\n");
		if (e == SqlEngine.Oracle && tables.Count > 0) {
			// Oracle 的普通脚本没有事务包裹，补一句提交以免用户忘了
		}
	}

	// ==================== 建表 ====================

	static string createtable(SqlCvTable t, SqlEngine dst, Version ver, bool withdrop) {
		var tn = qname(dst, t.Name, ver);
		var sb = new StringBuilder();
		if (withdrop) sb.Append(dropof(t.Name, dst, ver));
		sb.Append($"CREATE TABLE {tn} (\r\n");
		var lines = new List<string>();
		var pk = new List<string>();
		var inline = dst == SqlEngine.Sqlite || dst == SqlEngine.Postgres
			? t.Columns.FindIndex(c => c.Identity && c.PrimaryKey && t.Columns.FindAll(x => x.PrimaryKey).Count == 1)
			: -1;
		for (var i = 0; i < t.Columns.Count; i++) {
			var c = t.Columns[i];
			var line = $"  {qname(dst, c.Name, ver)} {typetext(c, dst, ver)}";
			var def = defaultof(c, dst, ver);
			if (def.Length > 0) line += " DEFAULT " + def;
			// SQLite 生成列只能 PERSISTED/VIRTUAL，PostgreSQL 只写 GENERATED ALWAYS AS (...)
			if (c.Generated.Length > 0) line += generatedof(c.Generated, dst, ver);
			// SQLite 的 AUTOINCREMENT 跟主键一起写在列上；Postgres/Oracle 的 IDENTITY 要单独带上
			if (c.Identity && !(i == inline && dst == SqlEngine.Sqlite)) line += identityof(dst, c, ver);
			if (!c.Nullable) line += " NOT NULL";
			if (c.Unique && !c.PrimaryKey) line += " UNIQUE";
			if (c.PrimaryKey && i == inline) line += pkinline(dst, c, ver);
			else if (c.PrimaryKey) pk.Add(qname(dst, c.Name, ver));
			lines.Add(line);
		}
		if (pk.Count > 0) {
			var cols = string.Join(", ", pk.ToArray());
			if (dst == SqlEngine.Mssql)
				lines.Add($"  CONSTRAINT {qname(dst, truncate("PK_" + t.Name, 120), ver)} PRIMARY KEY ({cols})");
			else if (dst == SqlEngine.Oracle && ver != null && ver.Major < 12)
				lines.Add($"  CONSTRAINT {qname(dst, truncate("PK_" + t.Name, 28), ver)} PRIMARY KEY ({cols})");
			else lines.Add($"  PRIMARY KEY ({cols})");
		}
		sb.Append(string.Join(",\r\n", lines.ToArray()));
		sb.Append("\r\n)");
		if (dst == SqlEngine.Mysql) sb.Append(" ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
		sb.Append(";\r\n");
		if (t.Comment.Length > 0 || t.Columns.Exists(c => c.Comment.Length > 0))
			sb.Append(commentof(t, dst, ver));
		return sb.ToString();
	}

	/// <summary>注释：PostgreSQL / Oracle 用 COMMENT ON；MySQL 写在建表语句里，其余方言不支持就丢掉。</summary>
	static string commentof(SqlCvTable t, SqlEngine d, Version ver) {
		if (d != SqlEngine.Postgres && d != SqlEngine.Oracle) return "";
		var sb = new StringBuilder();
		var tn = qname(d, t.Name, ver);
		if (t.Comment.Length > 0)
			sb.Append($"COMMENT ON TABLE {tn} IS '{t.Comment.Replace("'", "''")}';\r\n");
		foreach (var c in t.Columns) {
			if (c.Comment.Length == 0) continue;
			sb.Append($"COMMENT ON COLUMN {tn}.{qname(d, c.Name, ver)} IS '{c.Comment.Replace("'", "''")}';\r\n");
		}
		return sb.ToString();
	}

	static string dropof(string name, SqlEngine d, Version ver) {
		var tn = qname(d, name, ver);
		if (d == SqlEngine.Mssql) {
			if (SqlTargets.DropIfExists(ver)) return $"DROP TABLE IF EXISTS {tn};\r\n";
			return $"IF OBJECT_ID('{name.Replace("'", "''")}', 'U') IS NOT NULL DROP TABLE {tn};\r\n";
		}
		if (d == SqlEngine.Oracle) {
			return $"BEGIN\r\n  EXECUTE IMMEDIATE 'DROP TABLE {tn.Replace("'", "''")}';\r\n"
				+ "EXCEPTION\r\n  WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF;\r\nEND;\r\n/\r\n";
		}
		return $"DROP TABLE IF EXISTS {tn};\r\n";
	}

	/// <summary>SQLite 自增必须写在列上；PostgreSQL 的 identity 也在列上。</summary>
	static string pkinline(SqlEngine d, SqlCvColumn c, Version ver) {
		if (d == SqlEngine.Sqlite) return c.Identity ? " PRIMARY KEY AUTOINCREMENT" : " PRIMARY KEY";
		if (d == SqlEngine.Postgres) return " PRIMARY KEY";
		return "";
	}

	/// <summary>生成列：各方言写法不同，目标不支持就退化成普通列（数据里带着算好的值）。</summary>
	static string generatedof(string expr, SqlEngine dst, Version ver) {
		var body = requote(expr, dst);
		switch (dst) {
		case SqlEngine.Postgres:
			return $" GENERATED ALWAYS AS ({body}) STORED";
		case SqlEngine.Mysql:
			return ver != null && ver >= new Version(5, 7) ? $" GENERATED ALWAYS AS ({body}) STORED" : "";
		case SqlEngine.Mssql:
			return ver != null && ver.Major >= 13 ? $" AS ({body}) PERSISTED" : "";
		case SqlEngine.Sqlite:
			return ver != null && ver >= new Version(3, 31) ? $" GENERATED ALWAYS AS ({body}) STORED" : "";
		default:
			// Oracle 的虚拟列在 11g 起写法是 AS (expr)
			return ver != null && ver.Major >= 11 ? $" AS ({body})" : "";
		}
	}

	/// <summary>生成列表达式里的反引号/方括号标识符换成目标引号。</summary>
	static string requote(string expr, SqlEngine dst) {
		var outText = new StringBuilder(expr.Length + 8);
		var i = 0;
		while (i < expr.Length) {
			var ch = expr[i];
			if (ch == '`' || ch == '[' || (ch == '"' && dst != SqlEngine.Postgres && dst != SqlEngine.Oracle)) {
				var close = ch == '[' ? ']' : ch;
				var j = expr.IndexOf(close, i + 1);
				if (j > 0) {
					var name = expr.Substring(i + 1, j - i - 1);
					outText.Append(qname(dst, name, null));
					i = j + 1;
					continue;
				}
			}
			outText.Append(ch);
			i++;
		}
		// MySQL 的 CONCAT(x,y) 在其它方言里写法相同，原样留着
		return outText.ToString();
	}

	static string identityof(SqlEngine d, SqlCvColumn c, Version ver) {
		switch (d) {
		case SqlEngine.Mssql: return " IDENTITY(1,1)";
		case SqlEngine.Mysql: return " AUTO_INCREMENT";
		case SqlEngine.Postgres:
			return ver != null && ver.Major >= 10 ? " GENERATED BY DEFAULT AS IDENTITY" : "";
		case SqlEngine.Oracle:
			return ver != null && ver.Major >= 12 ? " GENERATED BY DEFAULT AS IDENTITY" : "";
		default: return "";
		}
	}

	static bool samedialect(SqlEngine a, SqlEngine b) => a == b;

	// ==================== 索引 ====================

	static string createindex(SqlCvTable t, SqlCvIndex ix, SqlEngine d, Version ver) {
		if (ix.Columns.Count == 0) return "";
		var name = ix.Name.Length > 0 ? ix.Name : "IX_" + t.Name + "_" + string.Join("_", ix.Columns.ToArray());
		if (d == SqlEngine.Sqlite && !name.StartsWith(t.Name + "_", StringComparison.OrdinalIgnoreCase))
			name = t.Name + "_" + name;
		name = truncate(name, d == SqlEngine.Oracle ? 28 : 120);
		var cols = string.Join(", ", ix.Columns.ConvertAll(c => qname(d, c, ver)).ToArray());
		return $"CREATE {(ix.Unique ? "UNIQUE " : "")}INDEX {qname(d, name, ver)} ON {qname(d, t.Name, ver)} ({cols});\r\n";
	}

	// ==================== INSERT ====================

	static string insertof(SqlCvInsert ins, SqlCvTable t, SqlConvertOptions o, Version ver, SqlConvertResult res) {
		var d = o.Target;
		var tn = qname(d, ins.Table, ver);
		var cols = ins.Columns;
		if (cols.Count == 0 && t != null) cols = t.Columns.ConvertAll(c => c.Name);
		if (cols.Count == 0) cols = new List<string>();
		var head = "INSERT INTO " + tn + " (" + string.Join(", ",
			cols.ConvertAll(c => qname(d, c, ver)).ToArray()) + ") VALUES";
		var sb = new StringBuilder();
		var ident = d == SqlEngine.Mssql && t != null && t.Columns.Exists(c => c.Identity);
		if (ident) sb.Append($"SET IDENTITY_INSERT {tn} ON;\r\n");
		var batch = d == SqlEngine.Mssql && !SqlTargets.Modern(ver) ? 1 : 200;
		var n = 0;
		var first = true;
		foreach (var row in ins.Rows) {
			if (first) sb.Append(head);
			else sb.Append(',');
			first = false;
			sb.Append("\r\n(");
			for (var i = 0; i < row.Count; i++) {
				if (i > 0) sb.Append(", ");
				var col = t != null && i < cols.Count
					? t.Columns.Find(x => same(x.Name, cols[i]))
					: null;
				sb.Append(literal(row[i], o.Source, d, col, ver));
			}
			sb.Append(')');
			n++;
			res.Rows++;
			if (n >= batch) {
				sb.Append(";\r\n");
				n = 0;
				first = true;
			}
		}
		if (!first) sb.Append(";\r\n");
		if (ident) sb.Append($"SET IDENTITY_INSERT {tn} OFF;\r\n");
		return sb.ToString();
	}

	/// <summary>字面量按源方言解析、按目标方言重新书写；解析不了的（函数表达式）原样搬过去。</summary>
	static string literal(string raw, SqlEngine src, SqlEngine dst, SqlCvColumn col, Version ver) {
		var v = (raw ?? "").Trim();
		if (v.Length == 0) return "NULL";
		if (same(v, "null")) return "NULL";
		if (src == SqlEngine.Mssql && (v.StartsWith("N'", StringComparison.OrdinalIgnoreCase)
			|| v.StartsWith("n'", StringComparison.OrdinalIgnoreCase))) {
			var str = unquote(v.Substring(1), src);
			return strval(str, dst, col, ver);
		}
		if (v[0] == '\'') return strval(unquote(v, src), dst, col, ver);
		if (same(v, "true") || same(v, "false")) return boolval(same(v, "true"), dst);
		var hex = hexof(v);
		if (hex != null) return bytesof(hex, dst, ver);
		if (Regex.IsMatch(v, @"^[-+]?(\d+(\.\d*)?|\.\d+)([eE][-+]?\d+)?$")) {
			// MySQL/SQL Server 的布尔列存 0/1，PostgreSQL 必须是 TRUE/FALSE
			if (dst == SqlEngine.Postgres && col != null && col.Kind == SqlTypeKind.Bool && (v == "0" || v == "1"))
				return v == "1" ? "TRUE" : "FALSE";
			return v;
		}
		// 函数、CASE、子查询：不能在文本层重写，原样搬
		return v;
	}

	static string boolval(bool b, SqlEngine d) {
		if (d == SqlEngine.Postgres) return b ? "TRUE" : "FALSE";
		return b ? "1" : "0";
	}

	static string strval(string s, SqlEngine d, SqlCvColumn col, Version ver) {
		s ??= "";
		if (d == SqlEngine.Oracle && col != null && col.Kind is SqlTypeKind.Date or SqlTypeKind.DateTime
			&& Regex.IsMatch(s, @"^\d{4}-\d{2}-\d{2}([ T]\d{2}:\d{2}(:\d{2})?)?$")) {
			var hasTime = s.Length > 10;
			var fmt = hasTime ? "YYYY-MM-DD HH24:MI:SS" : "YYYY-MM-DD";
			var lit = hasTime ? s.Replace('T', ' ') : s;
			if (hasTime && lit.Length == 16) lit += ":00";
			return $"TO_DATE('{lit}', '{fmt}')";
		}
		var sb = new StringBuilder();
		sb.Append('\'');
		if (d == SqlEngine.Mysql) {
			foreach (var ch in s) {
				if (ch == '\\') sb.Append("\\\\");
				else if (ch == '\'') sb.Append("''");
				else sb.Append(ch);
			}
		}
		else sb.Append(s.Replace("'", "''"));
		sb.Append('\'');
		var text = sb.ToString();
		if (d == SqlEngine.Mssql && haswide(s)) return "N" + text;
		return text;
	}

	static bool haswide(string s) {
		foreach (var ch in s) if (ch > 0x7F) return true;
		return false;
	}

	static string bytesof(string hex, SqlEngine d, Version ver) {
		var h = hex.ToUpperInvariant();
		return d switch {
			SqlEngine.Mssql => "0x" + h,
			SqlEngine.Postgres => $"decode('{h}', 'hex')",
			SqlEngine.Oracle => $"HEXTORAW('{h}')",
			_ => $"X'{h}'",
		};
	}

	/// <summary>0x.. / X'..' / 0x.. 且内容是偶数位十六进制返回十六进制串，否则 null。</summary>
	static string hexof(string v) {
		if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
			var h = v.Substring(2);
			return ishex(h) ? h : null;
		}
		if (v.Length > 3 && (v[0] == 'X' || v[0] == 'x') && v[1] == '\'' && v[^1] == '\'') {
			var h = v.Substring(2, v.Length - 3);
			return ishex(h) ? h : null;
		}
		return null;
	}

	static bool ishex(string h) {
		if (h.Length == 0 || (h.Length & 1) != 0) return false;
		foreach (var ch in h) {
			var ok = (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
			if (!ok) return false;
		}
		return true;
	}

	/// <summary>去掉外层引号，把双写引号还原；MySQL 的反斜杠转义也在这里还原。</summary>
	static string unquote(string v, SqlEngine src) {
		if (v.Length < 2) return v;
		var body = v.Substring(1, v.Length - 2);
		var sb = new StringBuilder(body.Length);
		for (var i = 0; i < body.Length; i++) {
			var ch = body[i];
			if (ch == '\'' && i + 1 < body.Length && body[i + 1] == '\'') {
				sb.Append('\'');
				i++;
				continue;
			}
			if (ch == '\\' && src == SqlEngine.Mysql && i + 1 < body.Length) {
				var nx = body[++i];
				sb.Append(nx switch {
					'n' => '\n',
					'r' => '\r',
					't' => '\t',
					'0' => '\0',
					'Z' => '\u001A',
					'b' => '\b',
					_ => nx,
				});
				continue;
			}
			sb.Append(ch);
		}
		return sb.ToString();
	}

	// ==================== 类型 ====================

	/// <summary>目标方言下的列类型。跨引擎时按列语义重写，同引擎也重写以便统一到目标版本。</summary>
	static string typetext(SqlCvColumn c, SqlEngine d, Version ver) {
		var size = c.Size;
		var prec = c.Precision > 0 ? c.Precision : 18;
		var scale = c.Scale;
		// JSON 列：MySQL / PostgreSQL 有原生类型，其余方言只能落到大文本
		var st = (c.SourceType ?? "").Trim().ToLowerInvariant();
		if (st is "json" or "jsonb") {
			switch (d) {
			case SqlEngine.Postgres: return "jsonb";
			case SqlEngine.Mysql: return ver != null && ver >= new Version(5, 7) ? "json" : "longtext";
			case SqlEngine.Mssql: return "nvarchar(max)";
			case SqlEngine.Oracle: return "CLOB";
			default: return "TEXT";
			}
		}
		switch (d) {
		case SqlEngine.Mssql: {
			var modern = SqlTargets.Modern(ver);
			return c.Kind switch {
				SqlTypeKind.Int => "int",
				SqlTypeKind.BigInt => "bigint",
				SqlTypeKind.Decimal => $"decimal({prec},{scale})",
				SqlTypeKind.Float => "float",
				SqlTypeKind.Bool => "bit",
				SqlTypeKind.Date => modern ? "date" : "datetime",
				SqlTypeKind.Time => modern ? "time(0)" : "datetime",
				SqlTypeKind.DateTime => modern ? "datetime2(3)" : "datetime",
				SqlTypeKind.Blob => "varbinary(max)",
				SqlTypeKind.Guid => "uniqueidentifier",
				_ => size > 0 && size <= 4000 ? $"nvarchar({size})" : "nvarchar(max)",
			};
		}
		case SqlEngine.Mysql:
			return c.Kind switch {
				SqlTypeKind.Int => "int",
				SqlTypeKind.BigInt => "bigint",
				SqlTypeKind.Decimal => $"decimal({prec},{scale})",
				SqlTypeKind.Float => "double",
				SqlTypeKind.Bool => "tinyint(1)",
				SqlTypeKind.Date => "date",
				// 5.5 不支持小数秒
				SqlTypeKind.Time => ver != null && ver >= new Version(5, 6) ? "time(3)" : "time",
				SqlTypeKind.DateTime => ver != null && ver >= new Version(5, 6) ? "datetime(3)" : "datetime",
				SqlTypeKind.Blob => "longblob",
				SqlTypeKind.Guid => "char(36)",
				_ => size > 0 && size <= 4000 ? $"varchar({size})" : "longtext",
			};
		case SqlEngine.Postgres:
			return c.Kind switch {
				SqlTypeKind.Int => "integer",
				SqlTypeKind.BigInt => "bigint",
				SqlTypeKind.Decimal => $"numeric({prec},{scale})",
				SqlTypeKind.Float => "double precision",
				SqlTypeKind.Bool => "boolean",
				SqlTypeKind.Date => "date",
				SqlTypeKind.Time => "time",
				SqlTypeKind.DateTime => "timestamp(3)",
				SqlTypeKind.Blob => "bytea",
				SqlTypeKind.Guid => "uuid",
				_ => size > 0 && size <= 10485760 ? $"varchar({size})" : "text",
			};
		case SqlEngine.Oracle:
			return c.Kind switch {
				SqlTypeKind.Int => "NUMBER(10)",
				SqlTypeKind.BigInt => "NUMBER(19)",
				SqlTypeKind.Decimal => $"NUMBER({prec},{scale})",
				SqlTypeKind.Float => "BINARY_DOUBLE",
				SqlTypeKind.Bool => "NUMBER(1)",
				SqlTypeKind.Date => "DATE",
				SqlTypeKind.Time => "VARCHAR2(15)",
				SqlTypeKind.DateTime => "TIMESTAMP(3)",
				SqlTypeKind.Blob => "BLOB",
				SqlTypeKind.Guid => "VARCHAR2(36)",
				_ => size > 0 && size <= 4000 ? $"VARCHAR2({size})" : "CLOB",
			};
		default:
			return c.Kind switch {
				SqlTypeKind.Int or SqlTypeKind.BigInt => "INTEGER",
				SqlTypeKind.Decimal => $"NUMERIC({prec},{scale})",
				SqlTypeKind.Float => "REAL",
				SqlTypeKind.Bool => "INTEGER",
				SqlTypeKind.Blob => "BLOB",
				_ => "TEXT",
			};
		}
	}

	/// <summary>源类型名到列语义。带长度和精度的解析完后交给 typetext 按目标重写。</summary>
	static SqlTypeKind kindof(string bare, int size, int prec, int scale) {
		var n = (bare ?? "").ToLowerInvariant();
		if (n.Contains("unsigned")) n = n.Replace("unsigned", "").Trim();
		switch (n) {
		case "bigint":
		case "int8":
		case "bigserial":
		case "bigint unsigned":
			return SqlTypeKind.BigInt;
		case "int":
		case "integer":
		case "int4":
		case "smallint":
		case "tinyint":
		case "mediumint":
		case "serial":
		case "smallserial":
		case "int2":
		case "year":
			// MySQL 的 tinyint(1) 就是布尔
			if (n == "tinyint" && size == 1) return SqlTypeKind.Bool;
			return SqlTypeKind.Int;
		case "bit":
		case "bool":
		case "boolean":
			return SqlTypeKind.Bool;
		case "decimal":
		case "numeric":
		case "money":
		case "smallmoney":
		case "number":
			if (n == "number") {
				if (scale > 0) return SqlTypeKind.Decimal;
				if (prec > 0 && prec <= 10) return SqlTypeKind.Int;
				if (prec > 0 && prec <= 19) return SqlTypeKind.BigInt;
				return SqlTypeKind.Decimal;
			}
			return SqlTypeKind.Decimal;
		case "float":
		case "double":
		case "double precision":
		case "real":
		case "float4":
		case "float8":
		case "binary_double":
		case "binary_float":
			return SqlTypeKind.Float;
		case "date":
			return SqlTypeKind.Date;
		case "time":
		case "time without time zone":
		case "timetz":
			return SqlTypeKind.Time;
		case "datetime":
		case "datetime2":
		case "smalldatetime":
		case "datetimeoffset":
		case "timestamp":
		case "timestamp without time zone":
		case "timestamptz":
		case "timestamp with time zone":
			return SqlTypeKind.DateTime;
		case "blob":
		case "tinyblob":
		case "mediumblob":
		case "longblob":
		case "binary":
		case "varbinary":
		case "image":
		case "bytea":
		case "raw":
		case "long raw":
			return SqlTypeKind.Blob;
		case "uniqueidentifier":
		case "uuid":
			return SqlTypeKind.Guid;
		case "json":
		case "jsonb":
		case "xml":
		case "xmltype":
		case "clob":
		case "nclob":
		case "ntext":
		case "long":
			return SqlTypeKind.Text;
		default:
			return SqlTypeKind.Text;
		}
	}

	// ==================== 默认值 ====================

	static string defaultof(SqlCvColumn c, SqlEngine d, Version ver) {
		var raw = (c.Default ?? "").Trim();
		if (raw.Length == 0) return "";
		var low = raw.ToLowerInvariant();
		if (low is "null") return "";
		if (low.StartsWith("nextval(", StringComparison.Ordinal) || low.Contains(".nextval")) return "";
		// now() / CURRENT_TIMESTAMP(3)：参数只对 MySQL 有意义，其余方言去掉，免得建表报语法错
		var tsm = Regex.Match(low, @"^(?:now|current_timestamp|localtime|localtimestamp|getdate|getutcdate)\s*(?:\((\d*)\))?$");
		if (tsm.Success) {
			if (low.StartsWith("getutcdate", StringComparison.Ordinal)) {
				return d switch {
					SqlEngine.Mssql => "GETUTCDATE()",
					SqlEngine.Postgres => "(CURRENT_TIMESTAMP AT TIME ZONE 'UTC')",
					SqlEngine.Sqlite => "CURRENT_TIMESTAMP",
					SqlEngine.Mysql => "UTC_TIMESTAMP(3)",
					_ => "SYS_EXTRACT_UTC(SYSTIMESTAMP)",
				};
			}
			if (d == SqlEngine.Mysql && tsm.Groups[1].Success && tsm.Groups[1].Value.Length > 0
				&& ver >= new Version(5, 6)) return "CURRENT_TIMESTAMP(" + tsm.Groups[1].Value + ")";
			return "CURRENT_TIMESTAMP";
		}
		if (low is "current_timestamp" or "current_date" or "current_time")
			return raw.ToUpperInvariant();
		// 布尔默认值
		if (low is "0" or "1" && c.Kind == SqlTypeKind.Bool) {
			if (d == SqlEngine.Postgres) return low == "1" ? "TRUE" : "FALSE";
			return low;
		}
		if (low is "true" or "false") {
			var b = low == "true";
			if (d == SqlEngine.Postgres) return b ? "TRUE" : "FALSE";
			return b ? "1" : "0";
		}
		// 字符串字面量：MySQL 可能带反斜杠转义，按目标重写
		if (low == "uuid_generate_v4()" || low == "newid()" || low == "gen_random_uuid()") {
			if (d == SqlEngine.Mssql) return "NEWID()";
			if (d == SqlEngine.Mysql) return "(UUID())";
			if (d == SqlEngine.Postgres) return "gen_random_uuid()";
			return "";
		}
		if (raw[0] == '\'' || (raw.Length > 1 && raw[0] is 'N' or 'n' && raw[1] == '\'')) {
			var q = raw[0] == '\'' ? raw : raw.Substring(1);
			var s = unquote(q, SqlEngine.Mysql);
			return strval(s, d, c, ver);
		}
		if (Regex.IsMatch(raw, @"^[-+]?(\d+(\.\d*)?|\.\d+)$")) return raw;
		return raw;
	}

	// ==================== 语句拆分 ====================

	/// <summary>按分号拆语句，引号内的分号不动；GO 单独一行也算分隔。</summary>
	static List<string> SplitStatements(string text) {
		var list = new List<string>();
		var sb = new StringBuilder();
		var i = 0;
		var n = text.Length;
		while (i < n) {
			var ch = text[i];
			if (ch == '\'' || ch == '"' || ch == '`') {
				var q = ch;
				sb.Append(ch);
				i++;
				while (i < n) {
					var c2 = text[i];
					sb.Append(c2);
					i++;
					if (c2 == q) {
						if (i < n && text[i] == q) {
							sb.Append(text[i]);
							i++;
							continue;
						}
						break;
					}
					// MySQL 的单引号里反斜杠转义
					if (q == '\'' && c2 == '\\' && i < n) {
						sb.Append(text[i]);
						i++;
					}
				}
				continue;
			}
			if (ch == '[') {
				sb.Append(ch);
				i++;
				while (i < n) {
					sb.Append(text[i]);
					if (text[i] == ']') {
						i++;
						break;
					}
					i++;
				}
				continue;
			}
			if (ch == '-' && i + 1 < n && text[i + 1] == '-') {
				while (i < n && text[i] != '\n') sb.Append(text[i++]);
				continue;
			}
			if (ch == '#') {
				while (i < n && text[i] != '\n') sb.Append(text[i++]);
				continue;
			}
			if (ch == '/' && i + 1 < n && text[i + 1] == '*') {
				sb.Append("/*");
				i += 2;
				while (i < n && !(text[i] == '*' && i + 1 < n && text[i + 1] == '/')) sb.Append(text[i++]);
				if (i < n) {
					sb.Append("*/");
					i += 2;
				}
				continue;
			}
			if (ch == ';') {
				push(list, sb);
				i++;
				continue;
			}
			sb.Append(ch);
			i++;
			if (ch == '\n') {
				var line = sb.ToString();
				var t = line.TrimEnd().TrimStart();
				if (t.Length <= 2 && t.Equals("go", StringComparison.OrdinalIgnoreCase)) {
					sb.Clear();
					list.Add("");
				}
			}
		}
		push(list, sb);
		// 去掉 GO 留下的空占位
		list.RemoveAll(x => x.Trim().Length == 0);
		return list;
	}

	static void push(List<string> list, StringBuilder sb) {
		var s = sb.ToString();
		sb.Clear();
		if (s.Trim().Length > 0) list.Add(s);
	}

	/// <summary>去掉开头整段的注释，用来判断语句类型。</summary>
	static string stripcomments(string s) {
		var i = 0;
		while (i < s.Length) {
			if (char.IsWhiteSpace(s[i])) {
				i++;
				continue;
			}
			if (s[i] == '-' && i + 1 < s.Length && s[i + 1] == '-') {
				while (i < s.Length && s[i] != '\n') i++;
				continue;
			}
			if (s[i] == '#') {
				while (i < s.Length && s[i] != '\n') i++;
				continue;
			}
			if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*') {
				var end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
				if (end < 0) return "";
				i = end + 2;
				continue;
			}
			break;
		}
		// 行尾注释留着没意义，去掉便于匹配
		return i >= s.Length ? "" : s.Substring(i).Trim();
	}

	static string keywordof(string body) {
		var m = Regex.Match(body, @"^([a-z]+)", RegexOptions.IgnoreCase);
		return m.Success ? m.Groups[1].Value.ToLowerInvariant() : "";
	}

	// ==================== CREATE TABLE 解析 ====================

	static SqlCvTable parsetable(string body, SqlConvertOptions o, SqlEngine dst) {
		var t = new SqlCvTable();
		var i = "create".Length;
		i = skipws(body, i);
		if (starts(body, i, "table")) i += 5;
		i = skipws(body, i);
		if (starts(body, i, "if")) {
			i += 2;
			i = skipws(body, i);
			if (starts(body, i, "not")) i += 3;
			i = skipws(body, i);
			if (starts(body, i, "exists")) i += 6;
			i = skipws(body, i);
		}
		var name = readident(body, ref i);
		if (name.Length == 0) return null;
		t.Name = lastschema(name);
		i = skipws(body, i);
		if (i >= body.Length || body[i] != '(') return null;
		var open = i;
		var close = matchparen(body, open);
		if (close < 0) return null;
		var inner = body.Substring(open + 1, close - open - 1);
		foreach (var part in splitargs(inner)) {
			var item = part.Trim();
			if (item.Length == 0) continue;
			if (istableconstraint(item)) {
				handletableconstraint(item, t);
				continue;
			}
			var c = parsecolumn(item, o);
			if (c == null || c.Name.Length == 0) continue;
			// MySQL 在列上写 KEY / UNIQUE KEY 时也建索引
			if (c.Unique) t.Indexes.Add(new SqlCvIndex { Name = "UX_" + t.Name + "_" + c.Name, Unique = true, Columns = { c.Name } });
			if (c.PrimaryKey) c.Nullable = false;
			t.Columns.Add(c);
		}
		return t;
	}

	static bool istableconstraint(string s) {
		var k = keywordof(s);
		switch (k) {
		case "constraint":
		case "primary":
		case "unique":
		case "key":
		case "index":
		case "foreign":
		case "check":
		case "fulltext":
		case "spatial":
			return true;
		default:
			return false;
		}
	}

	static void handletableconstraint(string item, SqlCvTable t) {
		var i = 0;
		if (keywordof(item) == "constraint") {
			i = skipws(item, "constraint".Length);
			readident(item, ref i);
			i = skipws(item, i);
		}
		var body = item.Substring(i);
		var k = keywordof(body);
		if (k == "primary") {
			var cols = colsinside(body);
			foreach (var c in cols) {
				var col = t.Columns.Find(x => same(x.Name, c));
				if (col != null) {
					col.PrimaryKey = true;
					col.Nullable = false;
				}
			}
			return;
		}
		if (k is "unique" or "key" or "index" or "fulltext" or "spatial") {
			var unique = k == "unique" || body.TrimStart().StartsWith("unique ", StringComparison.OrdinalIgnoreCase);
			var j = 0;
			var kw = keywordof(body);
			j = skipws(body, kw.Length);
			if (kw == "unique" && starts(body, j, "key")) j = skipws(body, j + 3);
			else if (kw == "unique" && starts(body, j, "index")) j = skipws(body, j + 5);
			var n2 = readident(body, ref j);
			j = skipws(body, j);
			// KEY (a,b)：名字被省略，括号前没有标识符
			if (body.Length > j && body[j] == '(') n2 = "";
			var cols = colsinside(body.Substring(j));
			if (cols.Count == 0) return;
			if (kw == "primary") return;
			t.Indexes.Add(new SqlCvIndex {
				Name = n2.Length > 0 ? n2 : "IX_" + t.Name + "_" + string.Join("_", cols.ToArray()),
				Unique = unique,
				Columns = cols,
			});
			return;
		}
		// foreign key / check 不转换，留给用户手工处理
	}

	static List<string> colsinside(string s) {
		var list = new List<string>();
		var open = s.IndexOf('(');
		if (open < 0) return list;
		var close = matchparen(s, open);
		if (close < 0) return list;
		foreach (var part in splitargs(s.Substring(open + 1, close - open - 1))) {
			// 去掉 ASC/DESC 与长度前缀
			var p = part.Trim();
			var cut = p.IndexOfAny(new[] { '(', ' ' });
			if (cut > 0) p = p.Substring(0, cut);
			p = trimident(p);
			if (p.Length > 0) list.Add(p);
		}
		return list;
	}

	static SqlCvColumn parsecolumn(string item, SqlConvertOptions o) {
		var i = 0;
		var name = readident(item, ref i);
		if (name.Length == 0) return null;
		var c = new SqlCvColumn { Name = name };
		i = skipws(item, i);
		// 类型名：可能多词（double precision / character varying / timestamp with time zone）
		var tstart = i;
		while (i < item.Length) {
			var ch = item[i];
			if (ch == '(' || char.IsWhiteSpace(ch)) break;
			i++;
		}
		var bare = item.Substring(tstart, i - tstart).Trim();
		i = skipws(item, i);
		var size = 0;
		var prec = 0;
		var scale = 0;
		if (i < item.Length && item[i] == '(') {
			var close = matchparen(item, i);
			if (close > 0) {
				var args = splitargs(item.Substring(i + 1, close - i - 1));
				var nums = new List<int>();
				foreach (var a in args) {
					if (int.TryParse(a.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) nums.Add(v);
				}
				if (nums.Count == 1) size = nums[0];
				else if (nums.Count >= 2) {
					prec = nums[0];
					scale = nums[1];
					size = nums[1];
				}
				i = skipws(item, close + 1);
			}
		}
		// 多词类型：把后面的关键词补进类型名
		foreach (var extra in new[] { "unsigned", "zerofill", "precision", "varying", "without",
			"with", "time", "zone", "char", "character", "double", "long", "raw", "(p)" }) {
			if (starts(item, i, extra)) {
				bare = (bare + " " + extra).Trim();
				i = skipws(item, i + extra.Length);
				// 多词类型里可能又跟一个括号，如 timestamp(3) with time zone
				if (i < item.Length && item[i] == '(') {
					var close = matchparen(item, i);
					if (close > 0) i = skipws(item, close + 1);
				}
			}
		}
		c.SourceType = bare;
		c.Size = size;
		c.Precision = prec;
		c.Scale = scale;
		c.Kind = kindof(bare, size, prec, scale);
		if (bare.Length == 0) c.Kind = SqlTypeKind.Text;
		var rest = item.Substring(Math.Min(i, item.Length));
		parseconstraints(c, rest);
		return c;
	}

	static void parseconstraints(SqlCvColumn c, string rest) {
		var i = 0;
		while (i < rest.Length) {
			i = skipws(rest, i);
			if (i >= rest.Length) break;
			if (starts(rest, i, "not")) {
				var j = skipws(rest, i + 3);
				if (starts(rest, j, "null")) {
					c.Nullable = false;
					i = j + 4;
					continue;
				}
				i += 3;
				continue;
			}
			if (starts(rest, i, "null")) {
				i += 4;
				continue;
			}
			if (starts(rest, i, "primary")) {
				c.PrimaryKey = true;
				c.Nullable = false;
				i += 7;
				continue;
			}
			if (starts(rest, i, "autoincrement") || starts(rest, i, "auto_increment") || starts(rest, i, "auto"))
				{
				c.Identity = true;
				var kw = keywordof(rest.Substring(i));
				i += kw.Length;
				continue;
			}
			if (starts(rest, i, "identity")) {
				c.Identity = true;
				i += 8;
				i = skipws(rest, i);
				if (i < rest.Length && rest[i] == '(') {
					var close = matchparen(rest, i);
					if (close > 0) i = close + 1;
				}
				continue;
			}
			if (starts(rest, i, "unique")) {
				c.Unique = true;
				i += 6;
				continue;
			}
			if (starts(rest, i, "default")) {
				var j = skipws(rest, i + 7);
				var (expr, next) = readexpr(rest, j);
				c.Default = expr;
				i = next;
				continue;
			}
			if (starts(rest, i, "comment")) {
				var j = skipws(rest, i + 7);
				if (j < rest.Length && rest[j] == '\'') {
					var end = rest.IndexOf('\'', j + 1);
					if (end > 0) {
						c.Comment = rest.Substring(j + 1, end - j - 1);
						i = end + 1;
						continue;
					}
				}
				i += 7;
				continue;
			}
			if (starts(rest, i, "generated")) {
				// GENERATED ALWAYS AS (expr) STORED / AS IDENTITY
				var jid = rest.IndexOf("identity", i, StringComparison.OrdinalIgnoreCase);
				var j2 = rest.IndexOf('(', i);
				if (jid >= 0 && (j2 < 0 || jid < j2)) {
					c.Identity = true;
					i = jid + 8;
					continue;
				}
				if (j2 >= 0) {
					var close = matchparen(rest, j2);
					if (close > 0) {
						c.Generated = rest.Substring(j2 + 1, close - j2 - 1).Trim();
						// 生成列不能写默认值，也不能是自增
						c.Identity = false;
						c.Default = "";
						i = skipws(rest, close + 1);
						// 跳过 STORED / VIRTUAL / PERSISTED
						var kw = keywordof(rest.Substring(i));
						if (kw is "stored" or "virtual" or "persisted") i = skipws(rest, i + kw.Length);
						continue;
					}
				}
				i += 9;
				continue;
			}
			if (starts(rest, i, "collate") || starts(rest, i, "on") || starts(rest, i, "references")
				|| starts(rest, i, "check") || starts(rest, i, "character") || starts(rest, i, "charset")
				|| starts(rest, i, "constraint")) {
				// 跳到本约束结束：下一个约束关键词或行尾
				var j = i;
				var depth = 0;
				while (j < rest.Length) {
					var ch = rest[j];
					if (ch == '(') depth++;
					else if (ch == ')') depth--;
					if (depth == 0 && j > i && char.IsWhiteSpace(ch)) break;
					j++;
				}
				i = j;
				continue;
			}
			i++;
		}
	}

	/// <summary>读一个默认值表达式：到下一个约束关键词为止，括号里不管。</summary>
	static (string Expr, int Next) readexpr(string s, int i) {
		var start = i;
		var depth = 0;
		while (i < s.Length) {
			var ch = s[i];
			if (ch == '\'' || ch == '"') {
				var q = ch;
				i++;
				while (i < s.Length) {
					if (s[i] == q) {
						if (i + 1 < s.Length && s[i + 1] == q) {
							i += 2;
							continue;
						}
						i++;
						break;
					}
					i++;
				}
				continue;
			}
			if (ch == '(') depth++;
			else if (ch == ')') {
				if (depth == 0) break;
				depth--;
			}
			else if (depth == 0 && char.IsWhiteSpace(ch)) {
				var j = skipws(s, i);
				if (j < s.Length && isconstraintword(s, j)) break;
				i = j;
				continue;
			}
			i++;
		}
		return (s.Substring(start, i - start).Trim(), i);
	}

	static bool isconstraintword(string s, int i) {
		var t = s.Substring(i).ToLowerInvariant();
		return t.StartsWith("not ", StringComparison.Ordinal)
			|| t.StartsWith("null", StringComparison.Ordinal)
			|| t.StartsWith("primary", StringComparison.Ordinal)
			|| t.StartsWith("unique", StringComparison.Ordinal)
			|| t.StartsWith("auto", StringComparison.Ordinal)
			|| t.StartsWith("identity", StringComparison.Ordinal)
			|| t.StartsWith("comment", StringComparison.Ordinal)
			|| t.StartsWith("collate", StringComparison.Ordinal)
			|| t.StartsWith("references", StringComparison.Ordinal)
			|| t.StartsWith("check", StringComparison.Ordinal)
			|| t.StartsWith("constraint", StringComparison.Ordinal)
			|| t.StartsWith("on update", StringComparison.Ordinal)
			|| t.StartsWith("generated", StringComparison.Ordinal);
	}

	// ==================== CREATE INDEX 解析 ====================

	static SqlCvIndex parseindex(string body, List<SqlCvTable> tables) {
		var i = "create".Length;
		i = skipws(body, i);
		var unique = false;
		if (starts(body, i, "unique")) {
			unique = true;
			i = skipws(body, i + 6);
		}
		if (starts(body, i, "clustered") || starts(body, i, "nonclustered")) {
			i = skipws(body, i + (starts(body, i, "clustered") ? 9 : 12));
		}
		if (starts(body, i, "index")) i = skipws(body, i + 5);
		if (starts(body, i, "concurrently")) i = skipws(body, i + 12);
		if (starts(body, i, "if")) {
			i += 2;
			i = skipws(body, i);
			if (starts(body, i, "not")) i += 3;
			i = skipws(body, i);
			if (starts(body, i, "exists")) i += 6;
			i = skipws(body, i);
		}
		var name = readident(body, ref i);
		if (name.Length == 0) return null;
		if (starts(body, i, "on")) i = skipws(body, i + 2);
		var table = readident(body, ref i);
		table = lastschema(table);
		var cols = colsinside(body.Substring(i));
		if (cols.Count == 0) return null;
		var ix = new SqlCvIndex { Name = name, Unique = unique, Columns = cols };
		var t = tables.Find(x => same(x.Name, table));
		if (t != null) t.Indexes.Add(ix);
		return ix;
	}

	// ==================== INSERT 解析 ====================

	static SqlCvInsert parseinsert(string body, List<SqlCvTable> tables) {
		var ins = new SqlCvInsert();
		var m = Regex.Match(body, @"^insert\s+(?:ignore\s+)?into\s+([^\s(]+)\s*", RegexOptions.IgnoreCase);
		if (!m.Success) return null;
		ins.Table = lastschema(m.Groups[1].Value);
		var i = m.Index + m.Length;
		i = skipws(body, i);
		if (i < body.Length && body[i] == '(') {
			var close = matchparen(body, i);
			if (close > 0) {
				foreach (var p in splitargs(body.Substring(i + 1, close - i - 1))) {
					var c = trimident(p.Trim());
					if (c.Length > 0) ins.Columns.Add(c);
				}
				i = skipws(body, close + 1);
			}
		}
		if (!starts(body, i, "values")) return null;
		i += 6;
		// 逐组读 (...) 直到语句结束
		while (i < body.Length) {
			i = skipws(body, i);
			if (i < body.Length && body[i] == ',') {
				i++;
				continue;
			}
			if (i >= body.Length || body[i] != '(') break;
			var close = matchparen(body, i);
			if (close < 0) return null;
			var vals = splitargs(body.Substring(i + 1, close - i - 1));
			ins.Rows.Add(vals);
			i = close + 1;
		}
		return ins.Rows.Count > 0 ? ins : null;
	}

	// ==================== 文本工具 ====================

	static bool same(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

	static int skipws(string s, int i) {
		while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
		return i;
	}

	static bool starts(string s, int i, string word) {
		if (i < 0 || i + word.Length > s.Length) return false;
		if (string.Compare(s, i, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;
		// 词边界：后面不能紧跟标识符字符
		var end = i + word.Length;
		if (end >= s.Length) return true;
		var ch = s[end];
		if (char.IsLetterOrDigit(ch) || ch == '_') return false;
		return true;
	}

	/// <summary>读一个标识符：裸名、"双引号"、`反引号`、[方括号]；允许 schema.name 中间的点。</summary>
	static string readident(string s, ref int i) {
		i = skipws(s, i);
		if (i >= s.Length) return "";
		var sb = new StringBuilder();
		while (i < s.Length) {
			var ch = s[i];
			if (ch == '"' || ch == '`' || ch == '[') {
				var close = ch == '[' ? ']' : ch;
				i++;
				while (i < s.Length) {
					if (s[i] == close) {
						if (i + 1 < s.Length && s[i + 1] == close && close != ']') {
							sb.Append(close);
							i += 2;
							continue;
						}
						i++;
						break;
					}
					sb.Append(s[i]);
					i++;
				}
				continue;
			}
			if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '$' || ch == '#' || ch == '@') {
				sb.Append(ch);
				i++;
				continue;
			}
			if (ch == '.' && sb.Length > 0) {
				sb.Append(ch);
				i++;
				continue;
			}
			break;
		}
		return sb.ToString().TrimEnd('.');
	}

	/// <summary>把 dbo.x / [dbo].[x] / "s"."x" / `s`.`x` 拆成各段并去掉引号。</summary>
	static List<string> splitident(string s) {
		var list = new List<string>();
		var i = 0;
		while (i < s.Length) {
			var q = readident(s, ref i);
			if (q.Length > 0) {
				foreach (var part in q.Split('.')) {
					var p = trimident(part.Trim());
					if (p.Length > 0) list.Add(p);
				}
			}
			i++;
		}
		return list;
	}

	static string trimident(string s) {
		if (s.Length == 0) return s;
		if (s[0] == '"' || s[0] == '`' || s[0] == '[' || s[0] == '\'') {
			var close = s[0] == '[' ? ']' : s[0];
			var end = s.IndexOf(close, 1);
			if (end > 0) return s.Substring(1, end - 1);
		}
		return s;
	}

	/// <summary>去掉 schema 前缀：跨引擎时 dbo. / public. 在目标端没有对应物。</summary>
	static string lastschema(string name) {
		var parts = splitident(name ?? "");
		return parts.Count > 0 ? parts[parts.Count - 1] : (name ?? "").Trim();
	}

	static int matchparen(string s, int open) {
		var depth = 0;
		for (var i = open; i < s.Length; i++) {
			var ch = s[i];
			if (ch == '\'' || ch == '"' || ch == '`') {
				var q = ch;
				i++;
				while (i < s.Length) {
					if (s[i] == q) {
						if (i + 1 < s.Length && s[i + 1] == q) {
							i += 2;
							continue;
						}
						break;
					}
					if (q == '\'' && s[i] == '\\') i++;
					i++;
				}
				continue;
			}
			if (ch == '[') {
				var end = s.IndexOf(']', i + 1);
				if (end > 0) {
					i = end;
					continue;
				}
			}
			if (ch == '(') depth++;
			else if (ch == ')') {
				depth--;
				if (depth == 0) return i;
			}
		}
		return -1;
	}

	/// <summary>顶层逗号拆分，引号与括号里的逗号不动。</summary>
	static List<string> splitargs(string s) {
		var list = new List<string>();
		var start = 0;
		var depth = 0;
		var i = 0;
		while (i < s.Length) {
			var ch = s[i];
			if (ch == '\'' || ch == '"' || ch == '`') {
				var q = ch;
				i++;
				while (i < s.Length) {
					if (s[i] == q) {
						if (i + 1 < s.Length && s[i + 1] == q) {
							i += 2;
							continue;
						}
						break;
					}
					if (q == '\'' && s[i] == '\\') i++;
					i++;
				}
				i++;
				continue;
			}
			if (ch == '[') {
				var end = s.IndexOf(']', i + 1);
				i = end > 0 ? end + 1 : s.Length;
				continue;
			}
			if (ch == '(') depth++;
			else if (ch == ')') depth--;
			else if (ch == ',' && depth == 0) {
				list.Add(s.Substring(start, i - start));
				start = i + 1;
			}
			i++;
		}
		if (start < s.Length) list.Add(s.Substring(start));
		return list;
	}

	/// <summary>按目标方言写标识符：MySQL 反引号，SQL Server 方括号，其余双引号。</summary>
	static string qname(SqlEngine d, string name, Version ver) {
		var n = name ?? "";
		if (d == SqlEngine.Oracle) {
			// Oracle 裸名按大写存放，只有特殊字符才加引号，免得把用户的小写名变成区分大小写
			if (Regex.IsMatch(n, @"^[A-Za-z][A-Za-z0-9_$#]*$")) return n.ToUpperInvariant();
			return "\"" + n.Replace("\"", "\"\"") + "\"";
		}
		if (d == SqlEngine.Mysql) return "`" + n.Replace("`", "``") + "`";
		if (d == SqlEngine.Mssql) return "[" + n.Replace("]", "]]") + "]";
		return "\"" + n.Replace("\"", "\"\"") + "\"";
	}

	static string truncate(string s, int n) => string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n);

	// ==================== 文件读写 ====================

	/// <summary>按 BOM 或严格 UTF-8 读；不是 UTF-8 就按 GBK 读，脚本里有中文注释时很常见。</summary>
	public static string ReadText(string path) {
		var bytes = File.ReadAllBytes(path);
		if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
			return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
		if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
			return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
		if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
			return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
		try {
			return new UTF8Encoding(false, true).GetString(bytes);
		}
		catch {
			try { return Encoding.GetEncoding(936).GetString(bytes); }
			catch { return Encoding.UTF8.GetString(bytes); }
		}
	}

	public static void WriteText(string path, string text) {
		File.WriteAllText(path, text ?? "", new UTF8Encoding(false));
	}
}
