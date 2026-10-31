using System.Data.Common;
using System.Reflection;
using System.Windows;

namespace ScreenKit;

/// <summary>数据库查询可选的引擎。</summary>
enum DbKind {
	Sqlite,
	Mssql,
	Mysql,
	Oracle,
}

/// <summary>
/// 数据库驱动：SQLite 与 Oracle 的托管库按需安装到程序目录，精简包不带；
/// SQL Server 用 .NET Framework 自带的 System.Data.SqlClient，不用下载。
/// </summary>
static class DbDriver {
	public const string SqliteManaged = "Microsoft.Data.Sqlite.dll";
	public const string OracleManaged = "Oracle.ManagedDataAccess.dll";
	public const string MysqlManaged = "MySqlConnector.dll";
	public const string Asn1 = "System.Formats.Asn1.dll";
	public const string SqliteNative = "e_sqlite3.dll";

	/// <summary>SQLite 驱动：Microsoft.Data.Sqlite + SQLitePCLRaw + 原生 e_sqlite3。</summary>
	public static readonly string[] SqliteFiles = {
		SqliteManaged,
		"SQLitePCLRaw.core.dll",
		"SQLitePCLRaw.batteries_v2.dll",
		"SQLitePCLRaw.provider.dynamic_cdecl.dll",
		SqliteNative,
	};

	/// <summary>Oracle 驱动：ODP.NET 托管库 + 它依赖的 ASN.1。</summary>
	public static readonly string[] OracleFiles = { OracleManaged, Asn1 };

	/// <summary>MySQL 驱动：MySqlConnector（纯托管，其余依赖随本应用已带）。</summary>
	public static readonly string[] MysqlFiles = { MysqlManaged };

	public static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;

	public static string[] FilesOf(DbKind kind) => kind switch {
		DbKind.Sqlite => SqliteFiles,
		DbKind.Oracle => OracleFiles,
		DbKind.Mysql => MysqlFiles,
		_ => new string[0],
	};

	public static FeatureKind FeatureOf(DbKind kind) => kind switch {
		DbKind.Sqlite => FeatureKind.NativeSqlite,
		DbKind.Oracle => FeatureKind.DbOracle,
		DbKind.Mysql => FeatureKind.DbMysql,
		_ => FeatureKind.DbMssql,
	};

	public static string NameOf(DbKind kind) => Loc.T("feat." + FeatureOf(kind) + ".title");

	/// <summary>缺哪些文件；系统自带的引擎返回空。</summary>
	public static List<string> Missing(DbKind kind) {
		var list = new List<string>();
		foreach (var f in FilesOf(kind)) {
			if (!File.Exists(Path.Combine(BaseDir, f))) list.Add(f);
		}
		return list;
	}

	public static bool Installed(DbKind kind) => Missing(kind).Count == 0;

	/// <summary>未安装就提示，可直接打开「安装功能」。返回装好后是否可用。</summary>
	public static bool Ensure(DbKind kind, Window owner) {
		if (Installed(kind)) return true;
		var missing = Missing(kind);
		if (missing.Count == 0) return true;
		var ask = Loc.T("sql.driver.ask", NameOf(kind)) + "\n\n"
			+ Loc.T("sql.driver.detail", string.Join("、", missing.ToArray()));
		var title = Loc.T("sql.driver.needtitle");
		var r = owner != null
			? MessageBox.Show(owner, ask, title, MessageBoxButton.YesNo, MessageBoxImage.Information)
			: MessageBox.Show(ask, title, MessageBoxButton.YesNo, MessageBoxImage.Information);
		if (r != MessageBoxResult.Yes) return false;
		FeaturePrompt.OpenInstall(owner, FeatureOf(kind));
		return Installed(kind);
	}

	/// <summary>删除驱动文件。正在使用的文件删不掉，返回 false（重启程序后再删）。</summary>
	public static bool Uninstall(DbKind kind, IProgress<string> log) {
		var ok = true;
		foreach (var f in FilesOf(kind)) {
			var path = Path.Combine(BaseDir, f);
			if (!File.Exists(path)) continue;
			try {
				File.Delete(path);
				log?.Report(Loc.T("sql.driver.deleted", f));
			}
			catch (Exception ex) {
				ok = false;
				log?.Report(Loc.T("sql.driver.deletefail", f, ex.Message));
			}
		}
		return ok;
	}

	static readonly Dictionary<DbKind, DbProviderFactory> factories = new();
	static readonly object gate = new();

	/// <summary>
	/// 反射加载托管驱动取 DbProviderFactory（Oracle / MySQL）。
	/// 按字节加载，程序运行中卸载时文件不被占住。
	/// </summary>
	public static DbProviderFactory Factory(DbKind kind) {
		lock (gate) {
			if (factories.TryGetValue(kind, out var hit) && hit != null) return hit;
			string file, cls;
			switch (kind) {
			case DbKind.Oracle:
				file = OracleManaged;
				cls = "Oracle.ManagedDataAccess.Client.OracleClientFactory";
				break;
			case DbKind.Mysql:
				file = MysqlManaged;
				cls = "MySqlConnector.MySqlConnectorFactory";
				break;
			default:
				throw new InvalidOperationException("该引擎不需要额外驱动: " + kind);
			}
			var path = Path.Combine(BaseDir, file);
			if (!File.Exists(path))
				throw new MissingFeatureException(Loc.T("sql.driver.need", NameOf(kind)));
			Assembly asm;
			try {
				asm = Assembly.Load(File.ReadAllBytes(path));
			}
			catch (Exception ex) {
				throw new MissingFeatureException(Loc.T("sql.driver.loadfail", file, ex.Message));
			}
			var type = asm.GetType(cls, false, true);
			if (type == null)
				throw new MissingFeatureException(Loc.T("sql.driver.loadfail", file, cls));
			var val = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
				?? type.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
			var made = val as DbProviderFactory;
			if (made == null)
				throw new MissingFeatureException(Loc.T("sql.driver.loadfail", file, "Instance"));
			factories[kind] = made;
			return made;
		}
	}

	/// <summary>测试或改文件后用：丢掉已缓存的工厂。</summary>
	public static void Reset() {
		lock (gate) factories.Clear();
	}
}
