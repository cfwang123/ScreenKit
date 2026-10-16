using Microsoft.Data.Sqlite;

namespace ScreenKit;

sealed class ImeHit {
	public string Text = "";
	public string Comment = "";
}

/// <summary>程序旁的 ime.db。韩语按词前缀取词，汉字和汉谚混写只作注释。</summary>
static class ImeLex {
	static SqliteConnection conn;
	static string opened = "";
	static bool batteries;

	public static string PathOf => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ime.db");

	public static bool Ready {
		get {
			try { return ensure(PathOf); }
			catch { return false; }
		}
	}

	/// <summary>测试时改打开这份库。传空则关掉。</summary>
	public static void Use(string path) {
		close();
		if (string.IsNullOrEmpty(path)) return;
		ensure(path);
	}

	public static void Close() => close();

	public static string Hanja(string hangul) {
		if (string.IsNullOrEmpty(hangul) || !ready()) return "";
		try {
			var list = new List<string>();
			using var cmd = conn.CreateCommand();
			cmd.CommandText = "SELECT hanja FROM kr_hanja WHERE hangul=$h ORDER BY rank LIMIT 3";
			cmd.Parameters.AddWithValue("$h", hangul);
			using var r = cmd.ExecuteReader();
			while (r.Read()) list.Add(r.GetString(0));
			return string.Join(" / ", list.ToArray());
		}
		catch { return ""; }
	}

	public static List<ImeHit> Query(string prefix) {
		var list = new List<ImeHit>();
		if (string.IsNullOrEmpty(prefix) || !ready()) return list;
		try {
			using var cmd = conn.CreateCommand();
			cmd.CommandText = "SELECT text, freq FROM kr_word WHERE text LIKE $p || '%' AND text != $p ORDER BY freq DESC LIMIT 50";
			cmd.Parameters.AddWithValue("$p", prefix);
			var words = new List<string>();
			using (var r = cmd.ExecuteReader()) {
				while (r.Read()) {
					words.Add(r.GetString(0));
					if (words.Count >= 8) break;
				}
			}
			foreach (var t in words)
				list.Add(new ImeHit { Text = t, Comment = Hanja(t) });
		}
		catch { list.Clear(); }
		return list;
	}

	static bool ready() {
		try { return ensure(string.IsNullOrEmpty(opened) ? PathOf : opened); }
		catch { return false; }
	}

	static bool ensure(string path) {
		if (conn != null && opened == path) return true;
		close();
		if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
		if (!NativeRuntime.HasSqlite()) return false;
		if (!batteries) {
			SQLitePCL.Batteries_V2.Init();
			batteries = true;
		}
		var csb = new SqliteConnectionStringBuilder {
			DataSource = path,
			Mode = SqliteOpenMode.ReadOnly,
			Cache = SqliteCacheMode.Shared,
			Pooling = false,
		};
		var c = new SqliteConnection(csb.ToString());
		c.Open();
		using (var cmd = c.CreateCommand()) {
			cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('kr_word','kr_hanja')";
			if (Convert.ToInt64(cmd.ExecuteScalar()) < 2) {
				c.Dispose();
				return false;
			}
		}
		using (var cmd = c.CreateCommand()) {
			cmd.CommandText = "PRAGMA query_only = ON;";
			cmd.ExecuteNonQuery();
		}
		conn = c;
		opened = path;
		return true;
	}

	static void close() {
		try { conn?.Dispose(); } catch { }
		conn = null;
		opened = "";
	}
}
