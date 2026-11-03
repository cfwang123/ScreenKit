using System.Security.Cryptography;
using System.Text;
using ScreenKit.Json;

namespace ScreenKit;

/// <summary>一条保存下来的数据库连接。密码只存 DPAPI 加密后的 base64。</summary>
sealed class DbConnItem {
	public string Name { get; set; } = "";
	/// <summary>SqlTool 的引擎名：sqlite / mssql / mysql / oracle。</summary>
	public string Kind { get; set; } = SqlTool.Sqlite;
	public string File { get; set; } = "";
	public string Host { get; set; } = "";
	/// <summary>Oracle 的服务名。</summary>
	public string Service { get; set; } = "";
	public string User { get; set; } = "";
	public string Secret { get; set; } = "";
}

/// <summary>连接列表放在 data.json 的 sqlconns 段（不写 config.toml，也不进安装包）。</summary>
static class DbConnStore {
	/// <summary>data.json 里这一段的名字。</summary>
	const string SECTION = "sqlconns";

	public static string StorePath => DataStore.StorePath;

	/// <summary>读不到或内容坏了都当没有；没有名字的条目丢掉。</summary>
	public static List<DbConnItem> Load(string path = null) {
		var list = new List<DbConnItem>();
		try {
			List<DbConnItem> got;
			if (path == null) got = DataStore.Get<List<DbConnItem>>(SECTION);
			else {
				if (!File.Exists(path)) return list;
				got = JSONF.Parse(File.ReadAllText(path, Encoding.UTF8)).ToObject<List<DbConnItem>>();
			}
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
		if (path == null) {
			DataStore.Put(SECTION, list ?? new List<DbConnItem>());
			return;
		}
		var json = JSONF.FromObject(list, skipNull: true, skipDefault: true, camel: true).ToJSONString(0);
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
