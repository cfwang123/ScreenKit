using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenKit;

/// <summary>一条保存下来的数据库连接。密码只存 DPAPI 加密后的 base64。</summary>
sealed class DbConnItem {
	public string Name { get; set; } = "";
	/// <summary>SqlTool 的引擎名：sqlite / mssql / mysql / oracle。</summary>
	public string Kind { get; set; } = SqlTool.Sqlite;
	public string File { get; set; } = "";
	public string Host { get; set; } = "";
	public string Db { get; set; } = "";
	/// <summary>Oracle 的服务名。</summary>
	public string Service { get; set; } = "";
	public string User { get; set; } = "";
	public string Secret { get; set; } = "";
}

/// <summary>连接列表存在程序目录 sql-conns.json（不写 config.toml，也不进安装包）。</summary>
static class DbConnStore {
	const string FILE_NAME = "sql-conns.json";

	static readonly JsonSerializerOptions Opt = new() {
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	public static string StorePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FILE_NAME);

	/// <summary>读不到或内容坏了都当没有；没有名字的条目丢掉。</summary>
	public static List<DbConnItem> Load(string path = null) {
		path ??= StorePath;
		var list = new List<DbConnItem>();
		try {
			if (!File.Exists(path)) return list;
			var text = File.ReadAllText(path, Encoding.UTF8);
			var got = JsonSerializer.Deserialize<List<DbConnItem>>(text, Opt);
			if (got == null) return list;
			foreach (var it in got) {
				if (it == null || string.IsNullOrWhiteSpace(it.Name)) continue;
				list.Add(it);
			}
		}
		catch { }
		return list;
	}

	public static void Save(List<DbConnItem> list, string path = null) {
		path ??= StorePath;
		var json = JsonSerializer.Serialize(list, Opt);
		var tmp = path + ".tmp";
		File.WriteAllText(tmp, json, new UTF8Encoding(false));
		if (File.Exists(path)) File.Replace(tmp, path, null);
		else File.Move(tmp, path);
	}

	/// <summary>密码用当前用户的 DPAPI 加密；换机器或换用户解不开时给空密码。</summary>
	public static string Protect(string pw) {
		if (string.IsNullOrEmpty(pw)) return "";
		try {
			var buf = ProtectedData.Protect(Encoding.UTF8.GetBytes(pw), null, DataProtectionScope.CurrentUser);
			return Convert.ToBase64String(buf);
		}
		catch {
			return "";
		}
	}

	public static string Unprotect(string secret) {
		if (string.IsNullOrEmpty(secret)) return "";
		try {
			var buf = ProtectedData.Unprotect(Convert.FromBase64String(secret), null, DataProtectionScope.CurrentUser);
			return Encoding.UTF8.GetString(buf);
		}
		catch {
			return "";
		}
	}
}
