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
	/// <summary>改写了写法的函数调用数。</summary>
	public int Funcs;
	/// <summary>认不出写法、按原样保留的函数名，如 DATEDIFF(mi,..) 的单位写成了别的。</summary>
	public readonly List<string> Skipped = new();
	/// <summary>目标方言做不到、只能退让的写法，连原因一起提示。</summary>
	public readonly List<string> Warnings = new();
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
	/// <summary>括号内的原文，用来保留 ASC / DESC 方向。</summary>
	public string Express = "";
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
			else if (low.Contains("drop table if exists")) {
				ver = new Version(13, 0);
				reasons.Add("DROP TABLE IF EXISTS 是 2016 起");
			}
			else if (low.Contains("iif(") || low.Contains("try_convert(") || low.Contains(" offset ") || low.Contains("fetch next")) {
				ver = new Version(11, 0);
				reasons.Add("IIF / OFFSET-FETCH 是 2012 起");
			}
			else if (low.Contains("datetime2") || low.Contains("datetimeoffset") || low.Contains("date '")) {
				ver = new Version(10, 0);
				reasons.Add("datetime2 / datetimeoffset 是 2008 起");
			}
			// 有语法特征就说明至少到那个版本；一条特征都没有时按 2016 保守处理
			known = reasons.Count > 0;
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
			// SQL Server 的 IF OBJECT_ID(...) DROP TABLE 也是删表兜底，转换时会重新生成
			if (head == "if" && Regex.IsMatch(body, @"drop\s+table", RegexOptions.IgnoreCase)) continue;
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
			if (Regex.IsMatch(body, @"^create\s+(unique\s+)?(nonclustered\s+|clustered\s+)?index\b", RegexOptions.IgnoreCase)) {
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
			sb.Append(createtable(t, src, dst, dver, o.WithDrop, res));
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
			// 原样保留的语句也过一遍函数改写：SELECT 里的日期函数照样换个写法
			foreach (var s in others) {
				splitnotes(s, out var head, out var body);
				if (head.Length > 0) sb.Append(head).Append("\r\n");
				var t = pretty(prettyrows(sqlfunc(body, src, dst, dver, res), "  "));
				sb.Append(wrapkeep(t, dst)).Append("\r\n");
			}
			res.Kept = others.Count;
		}
		appendfooter(sb, dst, tables);
		res.Text = sb.ToString();
		return res;
	}

	/// <summary>原样保留的语句也排一下版：一行一句的改写成一列一句</summary>
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

	/// <summary>拆出语句开头连续的注释行，剩下的才是 SQL 本体</summary>
	static void splitnotes(string s, out string head, out string body) {
		head = "";
		body = (s ?? "").Trim();
		var lines = body.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
		var k = 0;
		var sb = new StringBuilder();
		// 只认开头的注释行；SQL 中间的行尾注释仍然留在本体里
		while (k < lines.Length) {
			var t = lines[k].Trim();
			if (t.Length > 0 && !t.StartsWith("--", StringComparison.Ordinal) && !t.StartsWith("#", StringComparison.Ordinal)) break;
			if (t.Length > 0) sb.Append(t).Append("\r\n");
			k++;
		}
		if (k == 0 || k >= lines.Length) return;
		head = sb.ToString().TrimEnd();
		body = string.Join("\r\n", lines, k, lines.Length - k).Trim();
	}

	/// <summary>把语句按子句拆行；引号、括号里的内容一律不动</summary>
	static string pretty(string sql, string indent = "  ") {
		if (string.IsNullOrWhiteSpace(sql)) return sql ?? "";
		var src = Regex.Replace(sql.Replace("\r\n", " ").Replace('\n', ' ').Replace('\t', ' '), @" {2,}", " ");
		var sb = new StringBuilder(src.Length + 64);
		var line = new StringBuilder();
		var depth = 0;
		void flush() {
			var t = line.ToString().TrimEnd();
			if (t.Length > 0) sb.Append(t).Append("\r\n");
			line.Clear();
		}
		// 子句行的缩进：括号里多两级
		string pad() => new string(' ', 2 + depth * 2);
		var i = 0;
		var n = src.Length;
		while (i < n) {
			var ch = src[i];
			// 引号里的内容整段照抄，里面的关键字不算子句
			if (ch == '\'' || ch == '"' || ch == '`' || ch == '[') {
				var close = ch == '[' ? ']' : ch;
				var from = i;
				i++;
				while (i < n) {
					if (src[i] == close) {
						if (i + 1 < n && src[i + 1] == close && close != ']') {
							i += 2;
							continue;
						}
						i++;
						break;
					}
					i++;
				}
				line.Append(src.Substring(from, i - from));
				continue;
			}
			if (ch == '(') {
				depth++;
				line.Append(ch);
				i++;
				continue;
			}
			if (ch == ')') {
				depth = Math.Max(0, depth - 1);
				line.Append(ch);
				i++;
				continue;
			}
			if (ch == '-' && i + 1 < n && src[i + 1] == '-') {
				line.Append(src.Substring(i).TrimEnd());
				break;
			}
			if (wordat(src, i, out var word)) {
				var keep = line.ToString().Trim();
				// EXTRACT(year FROM x)、SUBSTRING(s FROM n)、TRIM(BOTH ' ' FROM s) 里的 FROM 不算子句
				if (word == "from" && Regex.IsMatch(keep, @"\b(extract|substring|substr|trim|overlay|position)\s*\([^()]*$", RegexOptions.IgnoreCase | RegexOptions.Singleline)) {
					if (!line.ToString().EndsWith(" ", StringComparison.Ordinal)) line.Append(' ');
					line.Append("FROM");
					i = wordend(src, i);
					continue;
				}
				// delete / insert 这类动词后面紧跟 FROM、INTO 时不断行
				var verbonly = keep.Length > 0 && !keep.Contains(' ')
					&& keep.ToLowerInvariant() is "delete" or "insert" or "select" or "update" or "replace";
				if (word is "from" or "where" or "having" or "limit" or "offset" or "fetch"
					or "union" or "except" or "intersect" or "returning" or "output" or "values" or "value") {
					if (!verbonly) {
						flush();
						line.Append(pad());
					}
					line.Append(word.ToUpperInvariant());
					i = wordend(src, i);
					continue;
				}
				if (word == "group" || word == "order") {
					// 后面跟 BY 才是子句，否则可能是列名（order_id 之类）
					var k = skipws(src, wordend(src, i));
					if (starts(src, k, "by")) {
						flush();
						line.Append(pad()).Append(word.ToUpperInvariant()).Append(" BY ");
						i = skipws(src, k + 2);
						continue;
					}
				}
				if (word == "select" && keep.Length > 0) {
					flush();
					line.Append(pad()).Append("SELECT");
					i = wordend(src, i);
					continue;
				}
				// 后面紧跟括号的函数少数几个（IF / LEFT / RIGHT / REPLACE）写法不动，其余关键字大写
				var after = skipws(src, wordend(src, i));
				var iscall = after < src.Length && src[after] == '(' && Callable(word);
				line.Append(Keyword(word) && !iscall ? word.ToUpperInvariant() : word);
				i = wordend(src, i);
				continue;
			}
			line.Append(ch);
			i++;
		}
		flush();
		var outText = sb.ToString().TrimEnd();
		return outText.Length == 0 ? sql : outText;
	}

	/// <summary>这些词在输出里统一写成大写</summary>
	static readonly SqlNameSet Keywords = new(
		"select", "insert", "update", "delete", "create", "alter", "drop", "truncate",
		"from", "where", "group", "order", "having", "limit", "offset", "fetch", "union", "except",
		"intersect", "returning", "output", "values", "join", "inner", "cross", "outer", "on", "set",
		"into", "as", "and", "or", "not", "in", "is", "like", "between", "asc", "desc", "distinct",
		"case", "when", "then", "else", "end", "by", "primary", "key", "unique", "index", "table",
		"exists", "if", "with", "left", "right", "replace", "nullif", "coalesce");

	/// <summary>只有独立成词的才算关键字：后面紧跟括号的是函数调用，别动它的写法</summary>
	static bool Keyword(string w) => Keywords.Has(w);

	/// <summary>关键字里也有同名函数的几个，后面带括号时按函数处理</summary>
	static bool Callable(string w) => w is "if" or "left" or "right" or "replace" or "nullif" or "coalesce";

	/// <summary>取关键字的结束位置</summary>
	static int wordend(string s, int i) {
		var j = i;
		while (j < s.Length && (char.IsLetter(s[j]) || s[j] == '_')) j++;
		return j;
	}

	/// <summary>当前位置是独立的关键字（前后都不是标识符字符）</summary>
	static bool wordat(string s, int i, out string word) {
		word = "";
		if (!char.IsLetter(s[i])) return false;
		if (i > 0 && (char.IsLetterOrDigit(s[i - 1]) || s[i - 1] == '_' || s[i - 1] == '.')) return false;
		var j = wordend(s, i);
		if (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_')) return false;
		word = s.Substring(i, j - i).ToLowerInvariant();
		return true;
	}

	/// <summary>INSERT 的多行元组排成一行一个，量大的脚本也好改</summary>
	static string prettyrows(string sql, string indent) {
		if (string.IsNullOrEmpty(sql) || !sql.Contains("), (")) return sql;
		var sb = new StringBuilder(sql.Length + 32);
		var depth = 0;
		var i = 0;
		var n = sql.Length;
		while (i < n) {
			var ch = sql[i];
			if (ch == '\'' || ch == '"' || ch == '`' || ch == '[') {
				var close = ch == '[' ? ']' : ch;
				var from = i;
				i++;
				while (i < n) {
					if (sql[i] == close) {
						if (i + 1 < n && sql[i + 1] == close && close != ']') {
							i += 2;
							continue;
						}
						i++;
						break;
					}
					i++;
				}
				sb.Append(sql.Substring(from, i - from));
				continue;
			}
			if (ch == '(') depth++;
			else if (ch == ')') depth--;
			if (ch == ')' && depth == 0) {
				var j = skipws(sql, i + 1);
				if (j < n && sql[j] == ',') {
					var k = skipws(sql, j + 1);
					if (k < n && sql[k] == '(') {
						sb.Append("),\r\n").Append(indent).Append('(');
						i = k + 1;
						continue;
					}
				}
			}
			sb.Append(ch);
			i++;
		}
		return sb.ToString();
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

	static string createtable(SqlCvTable t, SqlEngine src, SqlEngine dst, Version ver, bool withdrop, SqlConvertResult res) {
		var tn = qname(dst, t.Name, ver);
		var sb = new StringBuilder();
		if (withdrop) sb.Append(dropof(t.Name, dst, ver));
		sb.Append($"CREATE TABLE {tn} (\r\n");
		var lines = new List<string>();
		var notes = new List<string>();
		var pk = new List<string>();
		// SQL Server / SQLite 没有 COMMENT 语法，注释退化成行尾的 -- 说明
		var trail = dst == SqlEngine.Mssql || dst == SqlEngine.Sqlite;
		var inline = dst == SqlEngine.Sqlite || dst == SqlEngine.Postgres
			? t.Columns.FindIndex(c => c.Identity && c.PrimaryKey && t.Columns.FindAll(x => x.PrimaryKey).Count == 1)
			: -1;
		for (var i = 0; i < t.Columns.Count; i++) {
			var c = t.Columns[i];
			var line = $"  {qname(dst, c.Name, ver)} {typetext(c, dst, ver)}";
			var def = defaultology(c, src, dst, ver, res);
			if (def.Length > 0) line += " DEFAULT " + def;
			// SQLite 生成列只能 PERSISTED/VIRTUAL，PostgreSQL 只写 GENERATED ALWAYS AS (...)
			if (c.Generated.Length > 0) line += generatedof(c.Generated, src, dst, ver);
			// SQLite 的 AUTOINCREMENT 跟主键一起写在列上；Postgres/Oracle 的 IDENTITY 要单独带上
			if (c.Identity && !(i == inline && dst == SqlEngine.Sqlite)) line += identityof(dst, c, ver);
			if (!c.Nullable) line += " NOT NULL";
			if (c.Unique && !c.PrimaryKey) line += " UNIQUE";
			// MySQL 的列注释只能写在列上
			if (dst == SqlEngine.Mysql && c.Comment.Length > 0)
				line += " COMMENT '" + c.Comment.Replace("'", "''").Replace("\\", "\\\\") + "'";
			if (c.PrimaryKey && i == inline) line += pkinline(dst, c, ver);
			else if (c.PrimaryKey) pk.Add(qname(dst, c.Name, ver));
			lines.Add(line);
			notes.Add(trail ? oneline(c.Comment) : "");
		}
		if (pk.Count > 0) {
			var cols = string.Join(", ", pk.ToArray());
			lines.Add(dst == SqlEngine.Mssql
				? $"  CONSTRAINT {qname(dst, truncate("PK_" + t.Name, 120), ver)} PRIMARY KEY ({cols})"
				: dst == SqlEngine.Oracle && ver != null && ver.Major < 12
					? $"  CONSTRAINT {qname(dst, truncate("PK_" + t.Name, 28), ver)} PRIMARY KEY ({cols})"
					: $"  PRIMARY KEY ({cols})");
			notes.Add("");
		}
		for (var i = 0; i < lines.Count; i++) {
			sb.Append(lines[i]);
			if (i < lines.Count - 1) sb.Append(',');
			if (notes[i].Length > 0) sb.Append(" -- ").Append(notes[i]);
			sb.Append("\r\n");
		}
		sb.Append(")");
		if (dst == SqlEngine.Mysql) sb.Append(" ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
		if (dst == SqlEngine.Mysql && t.Comment.Length > 0)
			sb.Append(" COMMENT='" + t.Comment.Replace("'", "''") + "'");
		sb.Append(";\r\n");
		if (t.Comment.Length > 0 || t.Columns.Exists(c => c.Comment.Length > 0))
			sb.Append(commentof(t, dst, ver));
		return sb.ToString();
	}

	/// <summary>行尾注释只能占一行：换行和空白压成空格，免得把后面的 SQL 也注释掉。</summary>
	static string oneline(string s) {
		if (string.IsNullOrEmpty(s)) return "";
		var sb = new StringBuilder(s.Length);
		var space = false;
		foreach (var ch in s) {
			if (ch is '\r' or '\n' or '\t' || char.IsWhiteSpace(ch)) {
				space = sb.Length > 0;
				continue;
			}
			if (space) {
				sb.Append(' ');
				space = false;
			}
			sb.Append(ch);
		}
		return sb.ToString().Trim();
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
	static string generatedof(string expr, SqlEngine src, SqlEngine dst, Version ver) {
		var body = requote(expr, src, dst);
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

	/// <summary>把源方言的标识符引号换成目标方言的；单引号里的内容不动</summary>
	static string requote(string expr, SqlEngine src, SqlEngine dst) {
		if (string.IsNullOrEmpty(expr)) return expr;
		var sb = new StringBuilder(expr.Length + 8);
		var i = 0;
		while (i < expr.Length) {
			var ch = expr[i];
			if (ch == '\'') {
				var start = i;
				i++;
				while (i < expr.Length) {
					if (expr[i] == '\'') {
						if (i + 1 < expr.Length && expr[i + 1] == '\'') {
							i += 2;
							continue;
						}
						i++;
						break;
					}
					i++;
				}
				sb.Append(expr.Substring(start, i - start));
				continue;
			}
			// 双引号在 MySQL 默认是字符串，只有 ANSI 模式下才是标识符
			if (ch == '`' || ch == '[' || (ch == '"' && src != SqlEngine.Mysql)) {
				var close = ch == '[' ? ']' : ch;
				var j = expr.IndexOf(close, i + 1);
				if (j > 0) {
					sb.Append(qname(dst, expr.Substring(i + 1, j - i - 1), null));
					i = j + 1;
					continue;
				}
			}
			sb.Append(ch);
			i++;
		}
		return sb.ToString();
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
		// 降序索引只有 SQL Server / PostgreSQL 支持，其余方言退回升序
		var desc = Regex.IsMatch(ix.Express ?? "", @"\bdesc\b", RegexOptions.IgnoreCase);
		if (desc) {
			for (var i = 0; i < ix.Columns.Count; i++) {
				var idx = ix.Columns[i];
				if (!Regex.IsMatch(ix.Express, Regex.Escape(idx) + @"\s+desc", RegexOptions.IgnoreCase)) continue;
				if (d == SqlEngine.Mssql || d == SqlEngine.Postgres) {
					// 直接按原顺序把 DESC 写回去
					return $"CREATE {(ix.Unique ? "UNIQUE " : "")}INDEX {qname(d, name, ver)} ON {qname(d, t.Name, ver)} ({desccols(ix, d, ver)});\r\n";
				}
			}
		}
		return $"CREATE {(ix.Unique ? "UNIQUE " : "")}INDEX {qname(d, name, ver)} ON {qname(d, t.Name, ver)} ({cols});\r\n";
	}

	static string desccols(SqlCvIndex ix, SqlEngine d, Version ver) {
		var list = new List<string>();
		foreach (var c in ix.Columns) {
			var dir = Regex.IsMatch(ix.Express ?? "", Regex.Escape(c) + @"\s+desc", RegexOptions.IgnoreCase) ? " DESC" : "";
			list.Add(qname(d, c, ver) + dir);
		}
		return string.Join(", ", list.ToArray());
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
				sb.Append(literal(row[i], o.Source, d, col, ver, res));
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
		// 每行一条 VALUES，超长的脚本也好改
		return prettyrows(sb.ToString(), "  ");
	}

	/// <summary>字面量按源方言解析、按目标方言重新书写；函数表达式交给函数改写，认不出才原样搬过去。</summary>
	static string literal(string raw, SqlEngine src, SqlEngine dst, SqlCvColumn col, Version ver, SqlConvertResult res) {
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
		var fn = sqlfunc(v, src, dst, ver, res);
		// PostgreSQL 的布尔列不接受 0/1，字面量被改写成数字时补一次
		if (dst == SqlEngine.Postgres && col != null && col.Kind == SqlTypeKind.Bool && (v == "0" || v == "1"))
			return v == "1" ? "TRUE" : "FALSE";
		return fn;
	}

	static string boolval(bool b, SqlEngine d) {
		if (d == SqlEngine.Postgres) return b ? "TRUE" : "FALSE";
		return b ? "1" : "0";
	}

	static string strval(string s, SqlEngine d, SqlCvColumn col, Version ver) {
		s ??= "";
		// ISO 的 'T' 分隔只有部分方言认，日期时间列统一写成空格
		if (col != null && col.Kind is SqlTypeKind.Date or SqlTypeKind.DateTime && s.Length > 10 && s[10] == 'T')
			s = s.Substring(0, 10) + " " + s.Substring(11);
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
		// 默认不写 N 前缀：目标列是 nvarchar 时 SQL Server 自己会转，写了反而在其它工具里不好认
		return sb.ToString();
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

	/// <summary>默认值落到建表语句：SQLite 与老版本 MySQL 不认函数默认值，只能去掉</summary>
	static string defaultology(SqlCvColumn c, SqlEngine src, SqlEngine dst, Version ver, SqlConvertResult res) {
		var def = defaultof(c, src, dst, ver, res);
		if (def.Length == 0) return def;
		if (dst == SqlEngine.Sqlite) {
			if (def.Contains('(')) {
				if (res != null) res.Warnings.Add(Loc.T("sqlconv.warn.sqlitedefault", c.Name));
				return "";
			}
			return def;
		}
		// MySQL 5.7 起才允许除 CURRENT_TIMESTAMP 之外的表达式默认值
		if (dst == SqlEngine.Mysql && def.Contains('(')
			&& !Regex.IsMatch(def, @"^(CURRENT_TIMESTAMP|NOW|LOCALTIME|LOCALTIMESTAMP)\b", RegexOptions.IgnoreCase)) {
			if (ver == null || ver < new Version(5, 7)) {
				if (res != null) res.Warnings.Add(Loc.T("sqlconv.warn.mysqldefault", SqlEngines.Label(SqlEngine.Mysql, ver), c.Name));
				return "";
			}
		}
		return def;
	}

	/// <summary>默认值：字面量按目标方言重写，函数表达式交给函数改写</summary>
	static string defaultof(SqlCvColumn c, SqlEngine src, SqlEngine d, Version ver, SqlConvertResult res) {
		var raw = (c.Default ?? "").Trim();
		if (raw.Length == 0) return "";
		var low = raw.ToLowerInvariant();
		if (low is "null") return "";
		// 序列的默认值由 IDENTITY / SERIAL 接管
		if (low.StartsWith("nextval(", StringComparison.Ordinal) || low.Contains(".nextval")) return "";
		if (low is "true" or "false") {
			var b = low == "true";
			return boolval(b, d);
		}
		if (low is "0" or "1" && c.Kind == SqlTypeKind.Bool) {
			if (d == SqlEngine.Postgres) return low == "1" ? "TRUE" : "FALSE";
			return low;
		}
		// current_date / current_time 不带括号也算函数，MySQL 里尤其常见
		if (same(low, "current_date")) return d == SqlEngine.Oracle ? "TRUNC(SYSDATE)" : "CURRENT_DATE";
		if (same(low, "current_time")) return "CURRENT_TIME";
		if (raw[0] == '\'' || (raw.Length > 1 && raw[0] is 'N' or 'n' && raw[1] == '\'')) {
			var q = raw[0] == '\'' ? raw : raw.Substring(1);
			var s = unquote(q, SqlEngine.Mysql);
			return strval(s, d, c, ver);
		}
		if (Regex.IsMatch(raw, @"^[-+]?(\d+(\.\d*)?|\.\d+)$")) return raw;
		return sqlfunc(raw, src, d, ver, res);
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
		// 建表语句尾部可能带表注释：) ENGINE=InnoDB COMMENT='订单'
		parseTableTail(body.Substring(close + 1), t);
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

	/// <summary>建表语句尾部：COMMENT='...' 当表注释，其余（ENGINE / CHARSET）忽略。</summary>
	static void parseTableTail(string tail, SqlCvTable t) {
		var m = Regex.Match(tail ?? "", @"comment\s*=?\s*'((?:[^']|'')*)'", RegexOptions.IgnoreCase);
		if (m.Success) t.Comment = m.Groups[1].Value.Replace("''", "'");
	}

	static bool istableconstraint(string s) {		var k = keywordof(s);
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
			var inner2 = body.Substring(j);
			var cols = colsinside(inner2);
			if (cols.Count == 0) return;
			if (kw == "primary") return;
			t.Indexes.Add(new SqlCvIndex {
				Name = n2.Length > 0 ? n2 : "IX_" + t.Name + "_" + string.Join("_", cols.ToArray()),
				Unique = unique,
				Columns = cols,
				Express = inner2,
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
		// money / smallmoney 有固定的四位小数
		var bl = (bare ?? "").ToLowerInvariant();
		if (bl is "money" && prec == 0) {
			c.Precision = 19;
			c.Scale = 4;
		}
		else if (bl is "smallmoney" && prec == 0) {
			c.Precision = 10;
			c.Scale = 4;
		}
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
		i = skipws(body, i);
		if (starts(body, i, "on")) i = skipws(body, i + 2);
		var table = readident(body, ref i);
		table = lastschema(table);
		var inner3 = body.Substring(i);
		var cols = colsinside(inner3);
		if (cols.Count == 0) return null;
		var ix = new SqlCvIndex { Name = name, Unique = unique, Columns = cols, Express = inner3 };
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

	// ==================== 函数改写 ====================

	/// <summary>函数名集合，比较时不分大小写</summary>
	sealed class SqlNameSet {
		readonly HashSet<string> set;

		public SqlNameSet(params string[] names) => set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

		public bool Has(string n) => set.Contains(n ?? "");
	}

	/// <summary>认得出写法的函数名；不在表里的函数一律原样保留</summary>
	static readonly SqlNameSet SvFuncs = new(
		"now", "getdate", "sysdate", "systimestamp", "current_timestamp", "current_date", "current_time",
		"localtime", "localtimestamp", "getutcdate", "utc_timestamp",
		"dateadd", "date_add", "adddate", "add_months", "date_sub", "subdate",
		"datediff", "date_diff", "timestampdiff", "months_between",
		"datetrunc", "date_trunc", "trunc", "strftime", "datetime", "date_format",
		"to_char", "convert", "format", "date", "year", "month", "day", "hour", "minute", "second",
		"dayofweek", "weekday", "dayofyear", "quarter", "extract", "datepart", "datename",
		"unix_timestamp", "from_unixtime", "julianday", "str_to_date", "to_date", "to_timestamp",
		"last_day", "eomonth",
		"ifnull", "isnull", "nvl", "coalesce", "nullif", "concat", "concat_ws",
		"len", "length", "charindex", "instr", "locate", "left", "right",
		"random", "rand", "newid", "uuid", "gen_random_uuid", "sys_guid", "uuid_generate_v4",
		"iif", "if", "ceiling", "ceil");

	/// <summary>去掉源方言的标识符引号：反引号、方括号、双引号都按普通名字处理</summary>
	static string barename(string s) {
		var t = (s ?? "").Trim();
		if (t.Length >= 2 && t[0] == '`' && t[^1] == '`') t = t.Substring(1, t.Length - 2).Replace("``", "`");
		if (t.Length >= 2 && t[0] == '[' && t[^1] == ']') t = t.Substring(1, t.Length - 2).Replace("]]", "]");
		if (t.Length >= 2 && t[0] == '"' && t[^1] == '"') t = t.Substring(1, t.Length - 2).Replace("\"\"", "\"");
		return t;
	}

	/// <summary>这个名字是否值得改写：带点的是 schema.fn，不动</summary>
	static bool knownfunc(string name) {
		var n = barename(name).ToLowerInvariant();
		return n.Length > 0 && !n.Contains('.') && SvFuncs.Has(n);
	}

	/// <summary>把「名字 + 参数」拆出来；不是函数调用返回 false</summary>
	static bool callof(string v, out string name, out List<string> args) {
		name = "";
		args = null;
		var s = (v ?? "").Trim();
		var i = 0;
		while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '$' || s[i] == '#')) i++;
		if (i == 0) return false;
		name = s.Substring(0, i);
		var j = skipws(s, i);
		if (j >= s.Length || s[j] != '(') return false;
		var close = matchparen(s, j);
		if (close < 0) return false;
		if (s.Substring(close + 1).Trim().Length > 0) return false;
		args = splitargs(s.Substring(j + 1, close - j - 1)).ConvertAll(x => x.Trim());
		args.RemoveAll(x => x.Length == 0);
		return true;
	}

	/// <summary>再往后套一层运算时补括号，别把优先级拆散</summary>
	static string sqlnest(string s) {
		var t = (s ?? "").Trim();
		if (t.Length == 0) return t;
		if (Regex.IsMatch(t, @"^[A-Za-z_][A-Za-z0-9_]*$")) return t;
		// 带引号的标识符就是一个名字，不用再套括号
		if (t.Length > 2 && (t[0] == '`' || t[0] == '"' || t[0] == '[') && (t[^1] == '`' || t[^1] == '"' || t[^1] == ']'))
			return t;
		if (Regex.IsMatch(t, @"^[A-Za-z_][A-Za-z0-9_]*\s*\(.*\)$")) return t;
		if (Regex.IsMatch(t, @"^[-+]?(\d+(\.\d*)?|\.\d+)$")) return t;
		if (t.Length > 1 && t[0] == '\'' && t[^1] == '\'') return t;
		return "(" + t + ")";
	}

	/// <summary>日期部分名统一成 SQL Server 的写法；认不出的返回空串</summary>
	static string datepart(string p) {
		var s = (p ?? "").Trim().ToLowerInvariant().Trim('\'', '"');
		if (s.StartsWith("interval ", StringComparison.Ordinal)) {
			var sp = s.LastIndexOf(' ');
			s = sp > 0 ? s.Substring(sp + 1) : s;
		}
		switch (s) {
		case "year" or "yyyy" or "yy" or "y":
			return "year";
		case "quarter" or "qq" or "q":
			return "quarter";
		case "month" or "mon" or "mm" or "m":
			return "month";
		case "day" or "dd" or "d":
			return "day";
		case "dayofyear" or "dy":
			return "dayofyear";
		case "week" or "wk" or "ww" or "weekofyear" or "isoweek":
			return "week";
		case "weekday" or "dw" or "w":
			return "weekday";
		case "hour" or "hh":
			return "hour";
		case "minute" or "mi" or "n":
			return "minute";
		case "second" or "ss" or "s" or "sec":
			return "second";
		case "millisecond" or "ms":
			return "millisecond";
		case "microsecond" or "mcs" or "us":
			return "microsecond";
		case "nanosecond" or "ns":
			return "nanosecond";
		default:
			return "";
		}
	}

	/// <summary>SQL Server 的 DATEPART 不认 year 之外的别名，统一写全称</summary>
	static string mssqlunit(string part) => part is "dayofyear" or "weekday" or "millisecond" or "microsecond"
		or "nanosecond" ? part : part;

	/// <summary>从日期取一项：SQL Server 走 DATEPART，其余走 EXTRACT</summary>
	static string partof(string unit, string x, SqlEngine dst) {
		var v = sqlnest(x);
		if (dst == SqlEngine.Mssql) return $"DATEPART({mssqlunit(unit)}, {v})";
		if (dst == SqlEngine.Mysql) {
			var m = unit switch {
				"year" => "YEAR",
				"month" => "MONTH",
				"day" => "DAY",
				"hour" => "HOUR",
				"minute" => "MINUTE",
				"second" => "SECOND",
				"quarter" => "QUARTER",
				"week" => "WEEK",
				_ => "",
			};
			if (m.Length > 0) return $"{m}({v})";
			return "";
		}
		if (dst == SqlEngine.Sqlite) {
			var f = unit switch {
				"year" => "%Y",
				"month" => "%m",
				"day" => "%d",
				"hour" => "%H",
				"minute" => "%M",
				"second" => "%S",
				_ => "",
			};
			if (f.Length > 0) return $"CAST(strftime('{f}', {v}) AS INTEGER)";
			if (unit == "dayofyear") return $"CAST(strftime('%j', {v}) AS INTEGER)";
			return "";
		}
		// Oracle 没有 EXTRACT(dayofyear)，要借 TO_CHAR
		if (dst == SqlEngine.Oracle) {
			if (unit == "dayofyear") return $"TO_NUMBER(TO_CHAR({v}, 'DDD'))";
			if (unit == "dayofweek") return $"TO_NUMBER(TO_CHAR({v}, 'D'))";
			if (unit == "weekday") return $"TO_NUMBER(TO_CHAR({v}, 'D')) - 1";
			if (unit is "week") return $"TO_NUMBER(TO_CHAR({v}, 'IW'))";
		}
		return $"EXTRACT({unit} FROM {v})";
	}

	/// <summary>目标方言里的当前时间（服务器时区）</summary>
	static string curval(SqlEngine dst) => dst switch {
		SqlEngine.Mssql => "GETDATE()",
		SqlEngine.Oracle => "SYSDATE",
		SqlEngine.Sqlite => "datetime('now')",
		_ => "CURRENT_TIMESTAMP",
	};

	/// <summary>目标方言里的当前 UTC 时间；精度只对 MySQL 有意义</summary>
	static string utcval(int precision, SqlEngine dst, Version ver) {
		var ms = ver != null && ver >= new Version(5, 6);
		return dst switch {
			SqlEngine.Mssql => "GETUTCDATE()",
			SqlEngine.Mysql => precision > 0 && ms ? $"UTC_TIMESTAMP({precision})" : "UTC_TIMESTAMP()",
			SqlEngine.Sqlite => "CURRENT_TIMESTAMP",
			SqlEngine.Oracle => "SYS_EXTRACT_UTC(SYSTIMESTAMP)",
			_ => "(CURRENT_TIMESTAMP AT TIME ZONE 'UTC')",
		};
	}

	/// <summary>目标方言里的本地时间；只要日期就给当天零点</summary>
	static string localval(bool dateonly, SqlEngine dst) => dst switch {
		SqlEngine.Mssql => dateonly ? "CAST(GETDATE() AS date)" : "GETDATE()",
		SqlEngine.Oracle => dateonly ? "TRUNC(SYSDATE)" : "SYSDATE",
		SqlEngine.Mysql => dateonly ? "CURDATE()" : "NOW()",
		_ => dateonly ? "date('now','localtime')" : "datetime('now','localtime')",
	};

	/// <summary>目标方言里只保留日期部分的值</summary>
	static string dateval(string x, SqlEngine dst) => dst switch {
		SqlEngine.Mssql => $"CAST({sqlnest(x)} AS date)",
		SqlEngine.Oracle => $"TRUNC({sqlnest(x)})",
		SqlEngine.Mysql => $"DATE({sqlnest(x)})",
		_ => $"date({sqlnest(x)})",
	};

	/// <summary>把 strftime 风格的格式串落到目标方言；不认识的返回空串</summary>
	static string fmtout(string strf, SqlEngine dst) {
		var s = strf ?? "";
		if (s.Length == 0 || !s.Contains('%')) return "";
		if (dst == SqlEngine.Sqlite || dst == SqlEngine.Postgres) return s;
		if (dst == SqlEngine.Mysql) return s.Replace("%M", "%i").Replace("%S", "%s");
		if (dst == SqlEngine.Mssql) {
			// SQL Server 只有固定的样式号，认不准就不转
			if (s is "%Y-%m-%d") return "23";
			if (s is "%Y%m%d") return "112";
			if (s is "%Y-%m-%d %H:%M:%S") return "120";
			if (s is "%Y-%m-%d %H:%M:%S.%f") return "121";
			if (s is "%H:%M:%S") return "108";
			return "";
		}
		return s.Replace("%Y", "YYYY").Replace("%m", "MM").Replace("%d", "DD")
			.Replace("%H", "HH24").Replace("%i", "MI").Replace("%s", "SS")
			.Replace("%M", "MI").Replace("%S", "SS");
	}

	/// <summary>把 Oracle / SQL Server 的常见格式串换成 strftime 写法；认不准返回空串</summary>
	static string fmtin(string f) {
		var s = (f ?? "").Trim().Trim('\'').Trim();
		if (s.Length == 0) return "";
		if (s.Contains('%')) return s;
		var u = s.ToUpperInvariant().Replace("FM", "").Replace("HH24", "%H").Replace("HH12", "%I");
		var m = Regex.Match(u, @"^(YYYY|RRRR)([-/.])(MM|MON|MONTH)\2(DD)$");
		if (m.Success) return "%Y" + m.Groups[2].Value + "%m" + m.Groups[2].Value + "%d";
		if (Regex.IsMatch(u, @"^(YYYY|RRRR)$")) return "%Y";
		if (u == "YYYYMMDD") return "%Y%m%d";
		m = Regex.Match(u, @"^(YYYY|RRRR)([-/.])(MM)\2(DD) (%H|%I):MI:SS$");
		if (m.Success) return "%Y" + m.Groups[2].Value + "%m" + m.Groups[2].Value + "%d %H:%M:%S";
		if (u == "%H:MI:SS" || u == "HH24:MI:SS") return "%H:%M:%S";
		return "";
	}

	/// <summary>.NET 的日期格式串换成 strftime 写法；认不准返回空串</summary>
	static string fmtnet(string f) {
		var t = (f ?? "").Trim().Trim('\'');
		if (t.Length == 0) return "";
		if (t.Contains('%')) return t;
		var s = t
			.Replace("yyyy", "%Y").Replace("yyy", "%Y").Replace("yy", "%y")
			.Replace("MMMM", "%B").Replace("MMM", "%b").Replace("MM", "%m")
			.Replace("dddd", "%A").Replace("ddd", "%a").Replace("dd", "%d")
			.Replace("HH", "%H").Replace("hh", "%I")
			.Replace("mm", "%M").Replace("ss", "%S").Replace("fff", "%f").Replace("ff", "%f");
		if (Regex.IsMatch(s, @"[A-Za-z]{2,}")) return "";
		return s;
	}

	/// <summary>DATE_FORMAT / strftime / TO_CHAR 三者互转：fmt 是 strftime 写法，x 是日期表达式</summary>
	static string formatval(string strf, string x, SqlEngine dst) {
		var f = fmtout(strf, dst);
		if (f.Length == 0) return null;
		var v = sqlnest(x);
		if (dst == SqlEngine.Postgres) {
			var of = fmtout(strf, SqlEngine.Oracle);
			return of.Length == 0 ? null : $"TO_CHAR({v}, '{of}')";
		}
		return dst switch {
			SqlEngine.Mssql => $"CONVERT(varchar, {v}, {f})",
			SqlEngine.Oracle => $"TO_CHAR({v}, '{f}')",
			SqlEngine.Mysql => $"DATE_FORMAT({v}, '{f}')",
			_ => $"strftime('{f}', {v})",
		};
	}

	/// <summary>DATEADD / DATE_ADD / ADD_MONTHS / 日期加减：统一成「日期 + 数额 + 单位」</summary>
	static bool addparts(List<string> a, out string unit, out string amount, out string date) {
		unit = "";
		amount = "";
		date = "";
		if (a.Count == 3) {
			unit = datepart(a[0]);
			amount = a[1];
			date = a[2];
		}
		else if (a.Count == 2) {
			// MySQL 的 DATE_ADD(d, INTERVAL n unit)
			if (!Regex.IsMatch(a[1], @"^interval\b", RegexOptions.IgnoreCase)) return false;
			unit = datepart(a[1]);
			date = a[0];
			amount = Regex.Replace(Regex.Replace(a[1], @"^interval\s*", "", RegexOptions.IgnoreCase), @"\s+\w+$", "").Trim();
		}
		if (amount.Length == 0) amount = "0";
		return unit.Length > 0 && date.Length > 0;
	}

	static string addval(string unit, string amount, string date, SqlEngine dst) {
		var x = sqlnest(datelit(date, dst));
		var a = sqlnest(amount);
		return dst switch {
			SqlEngine.Mssql => $"DATEADD({mssqlunit(unit)}, {a}, {x})",
			SqlEngine.Mysql => $"DATE_ADD({x}, INTERVAL {a} {unit})",
			SqlEngine.Postgres => $"({x} + ({a}) * INTERVAL '1 {unit}')",
			// SQLite 的日期修饰符只认复数单位
			SqlEngine.Sqlite => $"datetime({x}, ({a}) || ' {unit}s')",
			_ => unit switch {
				"month" => $"ADD_MONTHS({x}, {a})",
				"quarter" => $"ADD_MONTHS({x}, ({a}) * 3)",
				"year" => $"ADD_MONTHS({x}, ({a}) * 12)",
				// Oracle 的日期加数字按天算，更小的单位要换算成一天的比例
				"day" => $"({x} + {a})",
				"week" => $"({x} + ({a}) * 7)",
				"hour" => $"({x} + ({a}) / 24)",
				"minute" => $"({x} + ({a}) / 1440)",
				_ => $"({x} + ({a}) / 86400)",
			},
		};
	}

	/// <summary>两个日期按单位求差：方向和 SQL Server 的 DATEDIFF 一致（前减后），按整单位截断</summary>
	static string diffval(string unit, string d1, string d2, SqlEngine dst) {
		var x = sqlnest(d1);
		var y = sqlnest(d2);
		if (dst == SqlEngine.Mssql) return $"DATEDIFF({mssqlunit(unit)}, {y}, {x})";
		if (dst == SqlEngine.Mysql) {
			var q = unit.ToUpperInvariant();
			if (q is "YEAR" or "QUARTER" or "MONTH" or "WEEK" or "DAY" or "HOUR" or "MINUTE" or "SECOND")
				return $"TIMESTAMPDIFF({q}, {y}, {x})";
			return null;
		}
		if (dst == SqlEngine.Postgres) {
			var secs = $"EXTRACT(epoch FROM (({x}) - ({y})))";
			return unit switch {
				"day" => $"CAST(({x} AS date) - ({y} AS date) AS integer)",
				"week" => $"CAST({secs} / 604800 AS integer)",
				"month" => monthdiff("EXTRACT(year FROM {0}) * 12 + EXTRACT(month FROM {0})", x, y),
				"quarter" => $"CAST(({monthdiff("EXTRACT(year FROM {0}) * 12 + EXTRACT(month FROM {0})", x, y)}) / 3 AS integer)",
				"year" => $"CAST(({monthdiff("EXTRACT(year FROM {0}) * 12 + EXTRACT(month FROM {0})", x, y)}) / 12 AS integer)",
				"hour" => $"CAST({secs} / 3600 AS integer)",
				"minute" => $"CAST({secs} / 60 AS integer)",
				"second" => $"CAST({secs} AS integer)",
				_ => null,
			};
		}
		if (dst == SqlEngine.Oracle) {
			return unit switch {
				"day" => $"(TRUNC({x}) - TRUNC({y}))",
				"hour" => $"TRUNC(({x} - {y}) * 24)",
				"minute" => $"TRUNC(({x} - {y}) * 1440)",
				"second" => $"TRUNC(({x} - {y}) * 86400)",
				"month" => $"TRUNC(MONTHS_BETWEEN({x}, {y}))",
				"quarter" => $"TRUNC(MONTHS_BETWEEN({x}, {y}) / 3)",
				"year" => $"TRUNC(MONTHS_BETWEEN({x}, {y}) / 12)",
				_ => null,
			};
		}
		// SQLite：按儒略日算天数，更小的单位再乘上去
		var days = $"julianday({x}) - julianday({y})";
		return unit switch {
			"day" => $"CAST({days} AS INTEGER)",
			"week" => $"CAST(({days}) / 7 AS INTEGER)",
			"hour" => $"CAST(({days}) * 24 AS INTEGER)",
			"minute" => $"CAST(({days}) * 1440 AS INTEGER)",
			"second" => $"CAST(({days}) * 86400 AS INTEGER)",
			"month" => $"CAST(({monthdiff("CAST(strftime('%Y', {0}) AS INTEGER) * 12 + CAST(strftime('%m', {0}) AS INTEGER)", x, y)}) AS INTEGER)",
			"quarter" => $"CAST(({monthdiff("CAST(strftime('%Y', {0}) AS INTEGER) * 12 + CAST(strftime('%m', {0}) AS INTEGER)", x, y)}) / 3 AS INTEGER)",
			"year" => $"CAST(({monthdiff("CAST(strftime('%Y', {0}) AS INTEGER) * 12 + CAST(strftime('%m', {0}) AS INTEGER)", x, y)}) / 12 AS INTEGER)",
			_ => null,
		};
	}

	/// <summary>日期字符串在目标方言里的写法：Oracle 要 TO_DATE，其余靠隐式转换</summary>
	static string datetimeval(string s, SqlEngine dst) {
		if (dst != SqlEngine.Oracle) return "'" + s + "'";
		var hasTime = s.Length > 10;
		var fmt = hasTime ? "YYYY-MM-DD HH24:MI:SS" : "YYYY-MM-DD";
		var lit = hasTime && s.Length == 16 ? s + ":00" : s;
		return $"TO_DATE('{lit}', '{fmt}')";
	}

	/// <summary>日期加减里的日期参数：Oracle 的字符串要显式 TO_DATE 才能参与运算</summary>
	static string datelit(string x, SqlEngine dst) {
		if (dst != SqlEngine.Oracle) return x;
		var m = Regex.Match((x ?? "").Trim(), @"^'((\d{4}-\d{2}-\d{2})([ T]\d{2}:\d{2}(:\d{2}(.\d{1,7})?)?)?)'$");
		if (!m.Success) return x;
		return datetimeval(m.Groups[1].Value.Replace('T', ' '), SqlEngine.Oracle);
	}

	/// <summary>月份差：先各自折成「年 * 12 + 月」，pattern 里的 {0} 换成日期表达式</summary>
	static string monthdiff(string pattern, string x, string y) =>
		$"(({pattern.Replace("{0}", x)}) - ({pattern.Replace("{0}", y)}))";

	/// <summary>截断到某个单位：DATE_TRUNC / DATETRUNC / TRUNC(date, fmt)</summary>
	static string truncval(string unit, string x, SqlEngine dst, Version ver) {
		var v = sqlnest(x);
		switch (dst) {
		case SqlEngine.Postgres:
			return $"date_trunc('{unit}', {v})";
		case SqlEngine.Mysql:
			return unit switch {
				"day" => $"CAST(DATE({v}) AS datetime)",
				"week" => $"CAST(DATE_FORMAT({v}, '%Y-%m-%d 00:00:00') AS datetime)",
				"month" => $"CAST(DATE_FORMAT({v}, '%Y-%m-01 00:00:00') AS datetime)",
				"quarter" => $"CAST(DATE_FORMAT({v}, '%Y-%m-01 00:00:00') AS datetime)",
				"year" => $"CAST(DATE_FORMAT({v}, '%Y-01-01 00:00:00') AS datetime)",
				"hour" => $"CAST(DATE_FORMAT({v}, '%Y-%m-%d %H:00:00') AS datetime)",
				_ => null,
			};
		case SqlEngine.Mssql:
			if (ver != null && ver.Major >= 16) return $"DATETRUNC({mssqlunit(unit)}, {v})";
			return $"DATEADD({mssqlunit(unit)}, DATEDIFF({mssqlunit(unit)}, 0, {v}), 0)";
		case SqlEngine.Sqlite:
			return unit switch {
				"day" => $"date({v})",
				"week" => $"date({v})",
				"month" => $"date({v}, 'start of month')",
				"quarter" => $"date({v}, 'start of month', '-' || ((CAST(strftime('%m', {v}) AS INTEGER) - 1) % 3) || ' months')",
				"year" => $"date({v}, 'start of year')",
				"hour" => $"strftime('%Y-%m-%d %H:00:00', {v})",
				_ => null,
			};
		default:
			return unit switch {
				"year" => $"TRUNC({v}, 'YYYY')",
				"quarter" => $"TRUNC({v}, 'Q')",
				"month" => $"TRUNC({v}, 'MM')",
				"week" => $"TRUNC({v}, 'IW')",
				"day" => $"TRUNC({v})",
				"hour" => $"TRUNC({v}, 'HH')",
				"minute" => $"TRUNC({v}, 'MI')",
				_ => null,
			};
		}
	}

	/// <summary>一个函数调用的改写；返回 null 表示认不出，保留原文</summary>
	static string funcbody(string name, List<string> a, SqlEngine src, SqlEngine dst, Version ver) {
		var n = barename(name).ToLowerInvariant();
		switch (n) {
		case "getutcdate":
		case "utc_timestamp":
			return utcval(a.Count > 0 && int.TryParse(a[0], out var p) ? p : 0, dst, ver);
		case "now":
		case "getdate":
			return curval(dst);
		case "current_timestamp":
			// 带精度参数的只有 MySQL 用得上
			if (a.Count > 0) return dst == SqlEngine.Mysql ? $"CURRENT_TIMESTAMP({a[0]})" : curval(dst);
			return curval(dst);
		case "sysdate":
		case "systimestamp":
			return localval(false, dst);
		case "localtime":
			return localval(true, dst);
		case "localtimestamp":
			return localval(false, dst);
		case "current_date":
			return dst == SqlEngine.Oracle ? "TRUNC(SYSDATE)" : "CURRENT_DATE";
		case "current_time":
			return dst == SqlEngine.Sqlite ? "time('now')" : "CURRENT_TIME";
		case "dateadd":
		case "date_add":
		case "adddate":
			if (a.Count < 2) return null;
			if (!addparts(a, out var u1, out var n1, out var d1)) return null;
			return addval(u1, n1, d1, dst);
		case "date_sub":
		case "subdate":
			if (!addparts(a, out var u2, out var n2, out var d2)) return null;
			return addval(u2, n2.StartsWith("-", StringComparison.Ordinal) ? n2.Substring(1) : "-" + n2, d2, dst);
		case "add_months":
			if (a.Count != 2) return null;
			return addval("month", a[1], a[0], dst);
		case "datediff":
		case "date_diff":
		case "timestampdiff":
			return diffcall(n, a, dst);
		case "months_between":
			if (a.Count != 2) return null;
			if (dst == SqlEngine.Mssql) return $"DATEDIFF(month, {sqlnest(a[1])}, {sqlnest(a[0])})";
			if (dst == SqlEngine.Mysql) return $"TIMESTAMPDIFF(MONTH, {sqlnest(a[1])}, {sqlnest(a[0])})";
			if (dst == SqlEngine.Sqlite)
				return monthdiff("CAST(strftime('%Y', {0}) AS INTEGER) * 12 + CAST(strftime('%m', {0}) AS INTEGER)", sqlnest(a[0]), sqlnest(a[1]));
			return $"MONTHS_BETWEEN({sqlnest(a[0])}, {sqlnest(a[1])})";
		case "datetrunc":
		case "date_trunc":
			if (a.Count != 2) return null;
			return truncval(datepart(a[0]), a[1], dst, ver);
		case "trunc":
			// TRUNC(date, 'MM') 是截断日期；Oracle 的 TRUNC(日期) 取到日，其它方言的 TRUNC 是取整
			if (a.Count == 2) {
				var tu = datepart(a[1]);
				if (tu.Length == 0) return null;
				return truncval(tu, a[0], dst, ver);
			}
			if (a.Count == 1 && src == SqlEngine.Oracle) return dateval(a[0], dst);
			return null;
		case "strftime":
			if (a.Count != 2) return null;
			return formatval(fmtin(a[0]), a[1], dst);
		case "date_format":
			if (a.Count != 2) return null;
			return formatval(fmtin(a[1]), a[0], dst);
		case "datetime":
			// SQLite 的 datetime(x) 只是把值规范成时间戳写法，不是取日期
			if (a.Count != 1) return null;
			return dst switch {
				SqlEngine.Sqlite => $"datetime({a[0]})",
				SqlEngine.Mysql => $"CAST({sqlnest(a[0])} AS datetime)",
				SqlEngine.Mssql => $"CONVERT(datetime, {sqlnest(a[0])}, 120)",
				SqlEngine.Oracle => $"TO_DATE({sqlnest(a[0])}, 'YYYY-MM-DD HH24:MI:SS')",
				_ => $"CAST({sqlnest(a[0])} AS timestamp)",
			};
		case "format":
			// SQL Server 的 FORMAT(x, 'yyyy-MM-dd') 用 .NET 格式串
			if (a.Count == 2) return formatval(fmtnet(a[1]), a[0], dst);
			return null;
		case "to_char":
			if (a.Count == 1) return dateval(a[0], dst);
			if (a.Count != 2) return null;
			return formatval(fmtin(a[1]), a[0], dst);
		case "date":
			if (a.Count != 1) return null;
			return dateval(a[0], dst);
		case "year":
		case "month":
		case "day":
		case "hour":
		case "minute":
		case "second":
		case "quarter":
			if (a.Count != 1) return null;
			return partof(n, a[0], dst);
		case "dayofweek":
			if (a.Count != 1) return null;
			return dst switch {
				SqlEngine.Mssql => $"DATEPART(weekday, {sqlnest(a[0])})",
				SqlEngine.Mysql => $"DAYOFWEEK({sqlnest(a[0])})",
				SqlEngine.Oracle => $"(TRUNC({sqlnest(a[0])}) - TRUNC({sqlnest(a[0])}, 'IW') + 1)",
				SqlEngine.Postgres => $"(EXTRACT(dow FROM {sqlnest(a[0])}) + 1)",
				_ => $"(CAST(strftime('%w', {sqlnest(a[0])}) AS INTEGER) + 1)",
			};
		case "weekday":
			if (a.Count != 1) return null;
			return dst switch {
				SqlEngine.Mssql => $"DATEPART(weekday, {sqlnest(a[0])})",
				SqlEngine.Mysql => $"WEEKDAY({sqlnest(a[0])})",
				SqlEngine.Oracle => $"(TRUNC({sqlnest(a[0])}) - TRUNC({sqlnest(a[0])}, 'IW'))",
				SqlEngine.Postgres => $"EXTRACT(dow FROM {sqlnest(a[0])})",
				_ => $"CAST(strftime('%w', {sqlnest(a[0])}) AS INTEGER)",
			};
		case "dayofyear":
			if (a.Count != 1) return null;
			return dst switch {
				SqlEngine.Mssql => $"DATEPART(dayofyear, {sqlnest(a[0])})",
				SqlEngine.Mysql => $"DAYOFYEAR({sqlnest(a[0])})",
				_ => $"EXTRACT(doy FROM {sqlnest(a[0])})",
			};
		case "extract":
			if (a.Count != 1) return null;
			var em = Regex.Match(a[0], @"^\s*(\w+)\s+from\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
			if (!em.Success) return null;
			var eu = datepart(em.Groups[1].Value);
			return eu.Length == 0 ? null : partof(eu, em.Groups[2].Value, dst);
		case "datepart":
		case "datename":
			if (a.Count != 2) return null;
			var pu = datepart(a[0]);
			return pu.Length == 0 ? null : partof(pu, a[1], dst);
		case "unix_timestamp":
			var ux = a.Count > 0 ? a[0] : "CURRENT_TIMESTAMP";
			return dst switch {
				SqlEngine.Mysql => a.Count > 0 ? $"UNIX_TIMESTAMP({sqlnest(ux)})" : "UNIX_TIMESTAMP()",
				SqlEngine.Postgres => $"CAST(EXTRACT(epoch FROM {sqlnest(ux)}) AS bigint)",
				SqlEngine.Sqlite => $"CAST(strftime('%s', {sqlnest(ux)}) AS INTEGER)",
				SqlEngine.Mssql => $"DATEDIFF(second, '1970-01-01', {sqlnest(ux)})",
				_ => $"TRUNC(({sqlnest(ux)} - TO_DATE('1970-01-01', 'YYYY-MM-DD')) * 86400)",
			};
		case "from_unixtime":
			if (a.Count < 1) return null;
			return dst switch {
				SqlEngine.Mysql => $"FROM_UNIXTIME({sqlnest(a[0])})",
				SqlEngine.Postgres => $"to_timestamp({sqlnest(a[0])})",
				SqlEngine.Sqlite => $"datetime({sqlnest(a[0])}, 'unixepoch')",
				SqlEngine.Mssql => $"DATEADD(second, {sqlnest(a[0])}, '1970-01-01')",
				_ => $"TO_DATE('1970-01-01', 'YYYY-MM-DD') + ({sqlnest(a[0])}) / 86400",
			};
		case "julianday":
			if (a.Count != 1) return null;
			if (dst == SqlEngine.Sqlite) return $"julianday({sqlnest(a[0])})";
			if (dst == SqlEngine.Mysql) return $"(TO_DAYS({sqlnest(a[0])}) + 1721424.5)";
			return null;
		case "to_date":
		case "to_timestamp":
		case "str_to_date":
			if (a.Count == 0) return null;
			var strf = a.Count > 1 ? fmtin(a[1]) : "";
			return dst switch {
				SqlEngine.Oracle => strf.Length > 0
					? $"TO_DATE({sqlnest(a[0])}, '{fmtout(strf, SqlEngine.Oracle)}')"
					: $"TO_DATE({sqlnest(a[0])}, 'YYYY-MM-DD HH24:MI:SS')",
				SqlEngine.Mysql => strf.Length > 0
					? $"STR_TO_DATE({sqlnest(a[0])}, '{fmtout(strf, SqlEngine.Mysql)}')"
					: $"CAST({sqlnest(a[0])} AS datetime)",
				SqlEngine.Mssql => $"CONVERT(datetime, {sqlnest(a[0])}, 120)",
				SqlEngine.Postgres => $"CAST({sqlnest(a[0])} AS timestamp)",
				_ => $"datetime({sqlnest(a[0])})",
			};
		case "last_day":
		case "eomonth":
			if (a.Count != 1) return null;
			return dst switch {
				SqlEngine.Oracle => $"LAST_DAY({sqlnest(a[0])})",
				SqlEngine.Mysql => $"LAST_DAY({sqlnest(a[0])})",
				SqlEngine.Postgres => $"(date_trunc('month', {sqlnest(a[0])}) + interval '1 month' - interval '1 day')",
				SqlEngine.Sqlite => $"date({sqlnest(a[0])}, 'start of month', '+1 month', '-1 day')",
				_ => $"EOMONTH({sqlnest(a[0])})",
			};
		case "convert":
			return convertval(a, dst);
		case "ifnull":
		case "isnull":
		case "nvl":
			if (a.Count != 2) return null;
			return dst switch {
				SqlEngine.Mssql => $"ISNULL({sqlnest(a[0])}, {sqlnest(a[1])})",
				SqlEngine.Mysql => $"IFNULL({sqlnest(a[0])}, {sqlnest(a[1])})",
				SqlEngine.Oracle => $"NVL({sqlnest(a[0])}, {sqlnest(a[1])})",
				_ => $"COALESCE({sqlnest(a[0])}, {sqlnest(a[1])})",
			};
		case "coalesce":
			return a.Count == 0 ? null : "COALESCE(" + string.Join(", ", a.ToArray()) + ")";
		case "concat":
			if (a.Count == 0) return null;
			if (dst == SqlEngine.Oracle || dst == SqlEngine.Sqlite) {
				if (a.Count == 1) return sqlnest(a[0]);
				return "(" + string.Join(" || ", a.ConvertAll(sqlnest).ToArray()) + ")";
			}
			return "CONCAT(" + string.Join(", ", a.ToArray()) + ")";
		case "concat_ws":
			if (a.Count < 2) return null;
			if (dst == SqlEngine.Mysql || dst == SqlEngine.Mssql)
				return "CONCAT_WS(" + string.Join(", ", a.ToArray()) + ")";
			return null;
		case "len":
		case "length":
			if (a.Count != 1) return null;
			return dst == SqlEngine.Mssql ? $"LEN({sqlnest(a[0])})" : $"LENGTH({sqlnest(a[0])})";
		case "charindex":
		case "instr":
		case "locate":
			if (a.Count < 2) return null;
			// SQL Server 是 (子串, 串)，MySQL / SQLite / Oracle 是 (串, 子串)
			var sub = n == "charindex" ? a[0] : a[1];
			var whole = n == "charindex" ? a[1] : a[0];
			return dst switch {
				SqlEngine.Mssql => $"CHARINDEX({sqlnest(sub)}, {sqlnest(whole)})",
				SqlEngine.Mysql => $"INSTR({sqlnest(whole)}, {sqlnest(sub)})",
				SqlEngine.Oracle => $"INSTR({sqlnest(whole)}, {sqlnest(sub)})",
				SqlEngine.Sqlite => $"INSTR({sqlnest(whole)}, {sqlnest(sub)})",
				_ => $"POSITION({sqlnest(sub)} IN {sqlnest(whole)})",
			};
		case "left":
			if (a.Count != 2) return null;
			return dst switch {
				SqlEngine.Mssql or SqlEngine.Mysql => $"LEFT({sqlnest(a[0])}, {sqlnest(a[1])})",
				SqlEngine.Oracle => $"SUBSTR({sqlnest(a[0])}, 1, {sqlnest(a[1])})",
				SqlEngine.Sqlite => $"SUBSTR({sqlnest(a[0])}, 1, {sqlnest(a[1])})",
				_ => $"SUBSTRING({sqlnest(a[0])} FROM 1 FOR {sqlnest(a[1])})",
			};
		case "right":
			if (a.Count != 2) return null;
			return dst switch {
				SqlEngine.Mssql or SqlEngine.Mysql => $"RIGHT({sqlnest(a[0])}, {sqlnest(a[1])})",
				SqlEngine.Oracle => $"SUBSTR({sqlnest(a[0])}, -({sqlnest(a[1])}))",
				SqlEngine.Sqlite => $"SUBSTR({sqlnest(a[0])}, -({sqlnest(a[1])}))",
				_ => $"RIGHT({sqlnest(a[0])}, {sqlnest(a[1])}::int)",
			};
		case "iif":
		case "if":
			if (a.Count != 3) return null;
			if (dst == SqlEngine.Mssql) return $"IIF({sqlnest(a[0])}, {sqlnest(a[1])}, {sqlnest(a[2])})";
			if (dst == SqlEngine.Mysql) return $"IF({sqlnest(a[0])}, {sqlnest(a[1])}, {sqlnest(a[2])})";
			return $"(CASE WHEN {sqlnest(a[0])} THEN {sqlnest(a[1])} ELSE {sqlnest(a[2])} END)";
		case "ceiling":
		case "ceil":
			if (a.Count != 1) return null;
			return dst == SqlEngine.Mssql ? $"CEILING({sqlnest(a[0])})" : $"CEIL({sqlnest(a[0])})";
		case "random":
		case "rand":
			return dst switch {
				SqlEngine.Sqlite => "(ABS(RANDOM()) / 9223372036854775807.0)",
				_ => "RAND()",
			};
		case "newid":
		case "uuid":
		case "gen_random_uuid":
		case "sys_guid":
		case "uuid_generate_v4":
			return dst switch {
				SqlEngine.Mssql => "NEWID()",
				SqlEngine.Mysql => "(UUID())",
				SqlEngine.Postgres => "gen_random_uuid()",
				SqlEngine.Oracle => "SYS_GUID()",
				_ => "lower(hex(randomblob(16)))",
			};
		}
		return null;
	}

	/// <summary>SQL Server 的 CONVERT(type, x, style)：只处理日期时间与文本这两类</summary>
	static string convertval(List<string> a, SqlEngine dst) {
		if (a.Count < 2) return null;
		var type = a[0].Trim().Trim('(', ')').ToLowerInvariant();
		var x = a[1];
		if (type is "date") return dateval(x, dst);
		if (type.StartsWith("varchar", StringComparison.Ordinal) || type.StartsWith("nvarchar", StringComparison.Ordinal)
			|| type.StartsWith("char", StringComparison.Ordinal) || type.StartsWith("nchar", StringComparison.Ordinal)) {
			var style = a.Count > 2 ? a[2].Trim() : "0";
			var strf = style switch {
				"112" => "%Y%m%d",
				"120" or "121" or "0" => "%Y-%m-%d %H:%M:%S",
				"108" => "%H:%M:%S",
				_ => "",
			};
			if (strf.Length == 0) return null;
			return formatval(strf, x, dst);
		}
		return null;
	}

	static string diffcall(string n, List<string> a, SqlEngine dst) {
		//差值的符号一律是「大的减小的」：SQL Server 的 DATEDIFF(单位, 起, 止) 取止减起，
		//MySQL 的两参数 DATEDIFF(止, 起) 与 TIMESTAMPDIFF(单位, 起, 止) 也是止减起
		string unit;
		string d1;
		string d2;
		if (n == "timestampdiff") {
			if (a.Count != 3) return null;
			unit = datepart(a[0]);
			d1 = a[2];
			d2 = a[1];
		}
		else if (a.Count == 3) {
			unit = datepart(a[0]);
			d1 = a[2];
			d2 = a[1];
		}
		else if (a.Count == 2) {
			unit = "day";
			d1 = a[0];
			d2 = a[1];
		}
		else return null;
		return unit.Length == 0 ? null : diffval(unit, d1, d2, dst);
	}

	/// <summary>逐个替换文本里的函数调用；识别不了的保留原文并记下名字</summary>
	static string mapfuncs(string v, SqlEngine src, SqlEngine dst, Version ver, SqlConvertResult res) {
		var sb = new StringBuilder(v.Length + 16);
		var i = 0;
		var n = v.Length;
		while (i < n) {
			var ch = v[i];
			if (ch is '\'' or '"' or '`') {
				var q = ch;
				var start = i;
				i++;
				while (i < n) {
					if (v[i] == q) {
						if (i + 1 < n && v[i + 1] == q) {
							i += 2;
							continue;
						}
						i++;
						break;
					}
					i++;
				}
				sb.Append(v.Substring(start, i - start));
				continue;
			}
			if (ch == '[') {
				var end = v.IndexOf(']', i + 1);
				if (end > 0) {
					sb.Append(v.Substring(i, end - i + 1));
					i = end + 1;
					continue;
				}
			}
			if (!char.IsLetter(ch) && ch != '_') {
				sb.Append(ch);
				i++;
				continue;
			}
			var s0 = i;
			while (i < n && (char.IsLetterOrDigit(v[i]) || v[i] == '_' || v[i] == '$')) i++;
			var word = v.Substring(s0, i - s0);
			var dotted = s0 > 0 && v[s0 - 1] == '.';
			var j = skipws(v, i);
			if (dotted || !knownfunc(word) || j >= n || v[j] != '(') {
				sb.Append(word);
				// 名字与括号之间的空白照抄，别把 SQL 挤成一行
				var k = i;
				while (k < n && char.IsWhiteSpace(v[k])) {
					sb.Append(v[k]);
					k++;
				}
				i = k;
				continue;
			}
			var close = matchparen(v, j);
			if (close < 0) {
				sb.Append(word);
				continue;
			}
			var raw = v.Substring(j + 1, close - j - 1);
			var args = splitargs(raw).ConvertAll(x => x.Trim());
			args.RemoveAll(x => x.Length == 0);
			// 参数里的函数也要换：DATEDIFF(NOW(), col) 里的 NOW() 不能漏
			for (var k2 = 0; k2 < args.Count; k2++) {
				if (args[k2].Contains('(')) args[k2] = mapfuncs(args[k2], src, dst, ver, res);
			}
			var body = funcbody(word, args, src, dst, ver);
			if (body == null) {
				var shown = barename(word).ToLowerInvariant() + "()";
				if (res != null && !res.Skipped.Contains(shown)) res.Skipped.Add(shown);
				sb.Append(word).Append('(').Append(raw).Append(')');
				i = close + 1;
				continue;
			}
			if (res != null && !same(body, v.Substring(s0, close + 1 - s0))) res.Funcs++;
			sb.Append(body);
			i = close + 1;
		}
		return sb.ToString();
	}

	/// <summary>表达式里的函数按目标方言改写、标识符引号也跟着换；同一方言原样返回</summary>
	static string sqlfunc(string v, SqlEngine src, SqlEngine dst, Version ver, SqlConvertResult res) {
		if (string.IsNullOrEmpty(v) || src == dst) return v;
		var t = requote(mapfuncs(v, src, dst, ver, res), src, dst);
		// dbo. 是 SQL Server 的默认架构，别的库里没有这一层
		if (src == SqlEngine.Mssql && dst != SqlEngine.Mssql)
			t = Regex.Replace(t, @"(\[dbo\]|\bdbo)\s*\.\s*", "", RegexOptions.IgnoreCase);
		return t;
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

	/// <summary>能直接写裸名的标识符：字母或下划线开头，只含字母数字下划线，且不是保留字。</summary>
	static bool bareok(SqlEngine d, string n) {
		if (!Regex.IsMatch(n, @"^[A-Za-z_][A-Za-z0-9_]*$")) return false;
		return !reserved(d, n);
	}

	/// <summary>各方言的保留字：只有这些才加引号，其余一律裸名，脚本更好读。</summary>
	static bool reserved(SqlEngine d, string n) {
		var w = n.ToUpperInvariant();
		if (CommonReserved.Contains(w)) return true;
		return d switch {
			SqlEngine.Mssql => MssqlReserved.Contains(w),
			SqlEngine.Mysql => MysqlReserved.Contains(w),
			SqlEngine.Postgres => PgReserved.Contains(w),
			SqlEngine.Oracle => OracleReserved.Contains(w),
			_ => false,
		};
	}

	static readonly HashSet<string> CommonReserved = new(StringComparer.Ordinal) {
		"ADD", "ALL", "ALTER", "AND", "ANY", "AS", "ASC", "BETWEEN", "BY", "CASE", "CAST", "CHECK",
		"COLUMN", "CONSTRAINT", "CREATE", "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP",
		"CURRENT_USER", "DEFAULT", "DELETE", "DESC", "DISTINCT", "DROP", "ELSE", "END", "EXCEPT",
		"EXISTS", "FALSE", "FOR", "FOREIGN", "FROM", "FULL", "GRANT", "GROUP", "HAVING", "IN",
		"INDEX", "INNER", "INSERT", "INTERSECT", "INTO", "IS", "JOIN", "KEY", "LEFT", "LIKE",
		"LIMIT", "NATURAL", "NOT", "NULL", "OFFSET", "ON", "OR", "ORDER", "OUTER", "PRIMARY",
		"REFERENCES", "RIGHT", "SELECT", "SESSION_USER", "SET", "SOME", "TABLE", "THEN", "TO",
		"TRUE", "UNION", "UNIQUE", "UPDATE", "USER", "USING", "VALUES", "VIEW", "WHEN", "WHERE", "WITH",
	};

	static readonly HashSet<string> MssqlReserved = new(StringComparer.Ordinal) {
		"ACTION", "CLOSE", "CLUSTERED", "COALESCE", "COMPUTE", "CONTAINS", "CONTINUE", "CONVERT",
		"CROSS", "CURRENT", "CURSOR", "DATABASE", "DBCC", "DEALLOCATE", "DECLARE", "DENY", "DISK",
		"DISTRIBUTED", "DOUBLE", "DUMP", "ERRLVL", "ESCAPE", "EXEC", "EXECUTE", "EXIT", "EXTERNAL",
		"FETCH", "FILE", "FILLFACTOR", "FUNCTION", "GOTO", "HOLDLOCK", "IDENTITY", "IDENTITY_INSERT",
		"IF", "IN", "NOCOUNT", "NOCHECK", "NONCLUSTERED", "NULLIF", "OF", "OFF", "OFFSETS", "OPEN",
		"OPENDATASOURCE", "OPENQUERY", "OPENROWSET", "OPENXML", "OPTION", "OVER", "PERCENT", "PIVOT",
		"PLAN", "PRECISION", "PRINT", "PROC", "PROCEDURE", "PUBLIC", "RAISERROR", "READ", "READTEXT",
		"RECONFIGURE", "REPLICATION", "RESTORE", "RESTRICT", "RETURN", "REVERT", "REVOKE", "ROLLBACK",
		"ROWCOUNT", "ROWGUIDCOL", "RULE", "SAVE", "SCHEMA", "SECURITYAUDIT", "SETUSER", "SHUTDOWN",
		"STATISTICS", "TABLESAMPLE", "TEXTSIZE", "TOP", "TRAN", "TRANSACTION", "TRIGGER", "TRUNCATE",
		"TRY_CONVERT", "TSEQUAL", "UNPIVOT", "UPDATETEXT", "USE", "VARYING", "WAITFOR", "WHILE",
		"WRITETEXT", "LEVEL", "RANGE", "ROWS", "ROW", "FILTER", "SYSTEM", "PASSWORD",
	};

	static readonly HashSet<string> MysqlReserved = new(StringComparer.Ordinal) {
		"ACCESSIBLE", "ANALYZE", "BEFORE", "BIGINT", "BINARY", "BLOB", "BOTH", "CALL", "CHANGE",
		"CHAR", "CHARACTER", "COLLATE", "CONDITION", "DATABASES", "DAY_HOUR", "DEC", "DECIMAL",
		"DECLARE", "DELAYED", "DESCRIBE", "DETERMINISTIC", "DIV", "DUAL", "EACH", "ELSEIF", "ENCLOSED",
		"ESCAPED", "EXIT", "EXPLAIN", "FLOAT", "FLUSH", "FORCE", "FULLTEXT", "GENERATED", "HIGH_PRIORITY",
		"HOUR_MINUTE", "IGNORE", "INFILE", "INOUT", "INSENSITIVE", "INT", "INTEGER", "INTERVAL",
		"ITERATE", "KEYS", "KILL", "LEADING", "LEAVE", "LINES", "LOAD", "LOCALTIME", "LOCK", "LONG",
		"LOOP", "LOW_PRIORITY", "MASTER_SSL_VERIFY_SERVER_CERT", "MATCH", "MAXVALUE", "MEDIUMINT",
		"MODIFIES", "MODIFY", "NO_WRITE_TO_BINLOG", "NUMERIC", "OPTIMIZE", "OPTIONALLY", "OUT", "OUTFILE",
		"PARTITION", "PROCEDURE", "PURGE", "RANGE", "READS", "READ_WRITE", "REAL", "REGEXP", "RELEASE",
		"RENAME", "REPEAT", "REPLACE", "REQUIRE", "RESIGNAL", "RESTRICT", "RETURN", "REVOKE", "RLIKE",
		"SCHEMA", "SCHEMAS", "SENSITIVE", "SEPARATOR", "SHOW", "SIGNAL", "SMALLINT", "SPATIAL",
		"SPECIFIC", "SQL", "SQLEXCEPTION", "SQLSTATE", "SQLWARNING", "SQL_BIG_RESULT", "SQL_CALC_FOUND_ROWS",
		"SQL_SMALL_RESULT", "SSL", "STARTING", "STRAIGHT_JOIN", "TERMINATED", "TINYINT", "TRAILING",
		"TRIGGER", "UNDO", "UNLOCK", "UNSIGNED", "USAGE", "UTC_DATE", "UTC_TIME", "UTC_TIMESTAMP",
		"VARBINARY", "VARCHAR", "VARCHARACTER", "VARYING", "WHILE", "WRITE", "XOR", "ZEROFILL",
		"GROUP", "RANK", "SYSTEM", "WINDOW", "LEVEL",
	};

	static readonly HashSet<string> PgReserved = new(StringComparer.Ordinal) {
		"ANALYSE", "ANALYZE", "ARRAY", "ASYMMETRIC", "AUTHORIZATION", "BINARY", "BOTH", "COLLATE",
		"CONCURRENTLY", "CURRENT_CATALOG", "CURRENT_ROLE", "CURRENT_SCHEMA", "DEFERRABLE", "DO", "ELSE",
		"FREEZE", "ILIKE", "INITIALLY", "ISNULL", "LATERAL", "LEADING", "NOTNULL", "OVERLAPS",
		"PLACING", "RETURNING", "SIMILAR", "SYMMETRIC", "TABLESAMPLE", "TRAILING", "VARIADIC", "VERBOSE",
		"WINDOW", "LEVEL", "RANGE", "ROWS", "FILTER", "GROUPS", "CURRENT", "USER", "SESSION_USER",
	};

	static readonly HashSet<string> OracleReserved = new(StringComparer.Ordinal) {
		"ACCESS", "AUDIT", "CLUSTER", "COMMENT", "COMPRESS", "CONNECT", "DATE", "DECIMAL", "EXCLUSIVE",
		"FILE", "FLOAT", "IDENTIFIED", "IMMEDIATE", "INCREMENT", "INITIAL", "INTEGER", "LOCK", "LONG",
		"MAXEXTENTS", "MINUS", "MODE", "MODIFY", "NOAUDIT", "NOCOMPRESS", "NOWAIT", "NUMBER", "OF",
		"OFFLINE", "ONLINE", "OPTION", "PCTFREE", "PRIOR", "PUBLIC", "RAW", "RENAME", "RESOURCE",
		"REVOKE", "ROW", "ROWID", "ROWNUM", "ROWS", "SESSION", "SHARE", "SIZE", "SMALLINT", "START",
		"SUCCESSFUL", "SYNONYM", "SYSDATE", "TRIGGER", "UID", "VALIDATE", "VARCHAR", "VARCHAR2", "LEVEL",
	};

	/// <summary>按目标方言写标识符：需要时才加引号，普通名字一律裸写。</summary>
	static string qname(SqlEngine d, string name, Version ver) {
		var n = name ?? "";
		// Oracle 裸名按大写存放，只有特殊字符才加引号，免得把用户的小写名变成区分大小写
		if (d == SqlEngine.Oracle) {
			if (Regex.IsMatch(n, @"^[A-Za-z][A-Za-z0-9_$#]*$") && !reserved(d, n)) return n.ToUpperInvariant();
			return "\"" + n.Replace("\"", "\"\"") + "\"";
		}
		if (bareok(d, n)) return n;
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
