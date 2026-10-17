using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace ScreenKit;

sealed class ImeHit {
	public string Text = "";
	public string Comment = "";
}

/// <summary>程序旁的 ime.db。五笔、日语、韩语都从这份可安装词库取词。用户置顶和选用次数写在同一文件的 user_* 表。</summary>
static class ImeLex {
	static SqliteConnection conn;
	static string opened = "";
	static bool batteries;
	static bool writable;
	static Dictionary<char, List<string>> tradmap = new Dictionary<char, List<string>>();

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
		var list = new List<string>();
		each("SELECT hanja FROM kr_hanja WHERE hangul=$h ORDER BY rank LIMIT 3",
			r => list.Add(r.GetString(0)), ("$h", hangul));
		return string.Join(" / ", list.ToArray());
	}

	public static List<ImeHit> Query(string prefix) {
		var list = new List<ImeHit>();
		if (string.IsNullOrEmpty(prefix) || !ready()) return list;
		var words = new List<string>();
		each("SELECT text, freq FROM kr_word WHERE text LIKE $p || '%' AND text != $p ORDER BY freq DESC LIMIT 8",
			r => words.Add(r.GetString(0)), ("$p", prefix));
		foreach (var t in words)
			list.Add(new ImeHit { Text = t, Comment = Hanja(t) });
		return list;
	}

	public static List<Cand> QueryKr(string prefix) {
		var map = new Dictionary<string, Cand>();
		if (string.IsNullOrEmpty(prefix) || !ready()) return new List<Cand>();
		each("SELECT text,freq FROM kr_word WHERE text LIKE $p || '%' AND text != $p LIMIT 50", r => {
			var t = r.GetString(0);
			map[t] = new Cand { Text = t, Raw = t, Rank = -r.GetInt32(1), Comment = Hanja(t) };
		}, ("$p", prefix));
		each("SELECT text,pinned,picks FROM user_kr WHERE text LIKE $p || '%' AND text != $p LIMIT 50", r => {
			var t = r.GetString(0);
			Cand c;
			if (!map.TryGetValue(t, out c)) {
				c = new Cand { Text = t, Raw = t, Comment = Hanja(t) };
				map[t] = c;
			}
			c.Pinned = r.GetInt32(1) != 0;
			c.Picks = r.GetInt32(2);
		}, ("$p", prefix));
		var list = new List<Cand>(map.Values);
		list.Sort((a, b) => {
			int c = b.Pinned.CompareTo(a.Pinned);
			if (c != 0) return c;
			c = b.Picks.CompareTo(a.Picks);
			if (c != 0) return c;
			return a.Rank.CompareTo(b.Rank);
		});
		if (list.Count > 8) list = list.GetRange(0, 8);
		return list;
	}

	public static void TouchKr(string text) {
		if (string.IsNullOrEmpty(text) || !writable) return;
		var known = scalar("SELECT 1 FROM kr_word WHERE text=$t UNION SELECT 1 FROM user_kr WHERE text=$t", ("$t", text));
		if (known == null || known is DBNull) return;
		cmd(@"INSERT INTO user_kr(text,pinned,picks) VALUES($t,0,1)
			ON CONFLICT(text) DO UPDATE SET picks=picks+1", ("$t", text));
	}

	public static void TogglePinKr(string text) {
		if (string.IsNullOrEmpty(text) || !writable) return;
		var o = scalar("SELECT pinned FROM user_kr WHERE text=$t", ("$t", text));
		if (o == null || o is DBNull)
			cmd("INSERT INTO user_kr(text,pinned,picks) VALUES($t,1,0)", ("$t", text));
		else
			cmd("UPDATE user_kr SET pinned=$p WHERE text=$t",
				("$p", Convert.ToInt32(o) == 0 ? 1 : 0), ("$t", text));
	}

	public static List<Cand> QueryWubi(string prefix, bool trad) {
		var map = new Dictionary<string, Cand>();
		if (string.IsNullOrEmpty(prefix) || !ready()) return new List<Cand>();
		void put(Cand c) {
			var k = c.Code + "\n" + c.Raw;
			Cand old;
			if (!map.TryGetValue(k, out old)) { map[k] = c; return; }
			if (c.Pinned) old.Pinned = true;
			if (c.Picks > old.Picks) old.Picks = c.Picks;
		}
		each(@"SELECT code,text,rank FROM wubi WHERE code LIKE $p || '%'
			ORDER BY length(code), rank LIMIT 400",
			r => put(new Cand { Code = r.GetString(0), Raw = r.GetString(1), Rank = r.GetInt32(2) }),
			("$p", prefix));
		each(@"SELECT code,text,pinned,picks FROM user_wubi WHERE code LIKE $p || '%'
			ORDER BY length(code), picks DESC LIMIT 400", r => {
			put(new Cand {
				Code = r.GetString(0), Raw = r.GetString(1),
				Pinned = r.GetInt32(2) != 0, Picks = r.GetInt32(3), Rank = 500
			});
		}, ("$p", prefix));
		return Rank.Finish(new List<Cand>(map.Values), prefix, trad, alts);
	}

	public static void TouchWubi(string code, string text) {
		if (!writable || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(text)) return;
		cmd(@"INSERT INTO user_wubi(code,text,pinned,picks) VALUES($c,$t,0,1)
			ON CONFLICT(code,text) DO UPDATE SET picks=picks+1",
			("$c", code), ("$t", text));
	}

	public static void TogglePinWubi(string code, string text) {
		if (!writable || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(text)) return;
		var o = scalar("SELECT pinned FROM user_wubi WHERE code=$c AND text=$t", ("$c", code), ("$t", text));
		if (o == null || o is DBNull)
			cmd("INSERT INTO user_wubi(code,text,pinned,picks) VALUES($c,$t,1,0)", ("$c", code), ("$t", text));
		else
			cmd("UPDATE user_wubi SET pinned=$p WHERE code=$c AND text=$t",
				("$p", Convert.ToInt32(o) == 0 ? 1 : 0), ("$c", code), ("$t", text));
	}

	public static string FullCode(char ch) {
		if (!ready()) return "";
		var o = scalar(@"SELECT code FROM wubi WHERE text=$t AND length(code)>=4
			ORDER BY length(code) DESC LIMIT 1", ("$t", ch.ToString()));
		return o == null || o is DBNull ? "" : o.ToString();
	}

	public static List<Cand> QueryJa(string reading) {
		var rows = new List<Cand>();
		if (string.IsNullOrEmpty(reading)) return rows;
		if (ready()) {
			int n = reading.Length > 24 ? 24 : reading.Length;
			var names = new string[n];
			var args = new (string n, object v)[n];
			for (int i = 0; i < n; i++) {
				names[i] = "$p" + i;
				args[i] = (names[i], reading.Substring(0, i + 1));
			}
			var inn = string.Join(",", names);
			each("SELECT reading,text,rank FROM ja_word WHERE reading IN (" + inn + ")", r => {
				rows.Add(new Cand { Code = r.GetString(0), Raw = r.GetString(1), Rank = r.GetInt32(2) });
			}, args);
			each("SELECT reading,text,pinned,picks FROM user_ja WHERE reading IN (" + inn + ")", r => {
				rows.Add(new Cand {
					Code = r.GetString(0), Raw = r.GetString(1),
					Pinned = r.GetInt32(2) != 0, Picks = r.GetInt32(3), Rank = 1
				});
			}, args);
		}
		return JaCand.Build(reading, rows);
	}

	public static void TouchJa(string reading, string text) {
		if (!writable || string.IsNullOrEmpty(reading) || string.IsNullOrEmpty(text)) return;
		cmd(@"INSERT INTO user_ja(reading,text,pinned,picks) VALUES($r,$t,0,1)
			ON CONFLICT(reading,text) DO UPDATE SET picks=picks+1",
			("$r", reading), ("$t", text));
	}

	public static void TogglePinJa(string reading, string text) {
		if (!writable || string.IsNullOrEmpty(reading) || string.IsNullOrEmpty(text)) return;
		var o = scalar("SELECT pinned FROM user_ja WHERE reading=$r AND text=$t", ("$r", reading), ("$t", text));
		if (o == null || o is DBNull)
			cmd("INSERT INTO user_ja(reading,text,pinned,picks) VALUES($r,$t,1,0)", ("$r", reading), ("$t", text));
		else
			cmd("UPDATE user_ja SET pinned=$p WHERE reading=$r AND text=$t",
				("$p", Convert.ToInt32(o) == 0 ? 1 : 0), ("$r", reading), ("$t", text));
	}

	static IList<string> alts(char ch) {
		List<string> list;
		return tradmap.TryGetValue(ch, out list) ? list : null;
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
			Mode = SqliteOpenMode.ReadWrite,
			Cache = SqliteCacheMode.Shared,
			Pooling = false,
		};
		SqliteConnection c;
		try {
			c = new SqliteConnection(csb.ToString());
			c.Open();
			writable = true;
		}
		catch {
			csb.Mode = SqliteOpenMode.ReadOnly;
			c = new SqliteConnection(csb.ToString());
			c.Open();
			writable = false;
		}
		using (var cmd = c.CreateCommand()) {
			cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('kr_word','kr_hanja')";
			if (Convert.ToInt64(cmd.ExecuteScalar()) < 2) {
				c.Dispose();
				writable = false;
				return false;
			}
		}
		conn = c;
		opened = path;
		if (writable) ensureuser();
		else {
			try {
				using var q = conn.CreateCommand();
				q.CommandText = "PRAGMA query_only = ON;";
				q.ExecuteNonQuery();
			}
			catch { }
		}
		loadtrad();
		return true;
	}

	static void ensureuser() {
		exec(@"CREATE TABLE IF NOT EXISTS user_wubi (
			code TEXT NOT NULL, text TEXT NOT NULL,
			pinned INTEGER NOT NULL DEFAULT 0, picks INTEGER NOT NULL DEFAULT 0,
			PRIMARY KEY (code, text))");
		exec(@"CREATE TABLE IF NOT EXISTS user_kr (
			text TEXT PRIMARY KEY, pinned INTEGER NOT NULL DEFAULT 0, picks INTEGER NOT NULL DEFAULT 0)");
		exec(@"CREATE TABLE IF NOT EXISTS user_ja (
			reading TEXT NOT NULL, text TEXT NOT NULL,
			pinned INTEGER NOT NULL DEFAULT 0, picks INTEGER NOT NULL DEFAULT 0,
			PRIMARY KEY (reading, text))");
	}

	static void loadtrad() {
		tradmap = new Dictionary<char, List<string>>();
		each("SELECT simp,trad FROM trad ORDER BY rank", r => {
			var simp = r.GetString(0);
			if (simp.Length != 1) return;
			var ch = simp[0];
			List<string> list;
			if (!tradmap.TryGetValue(ch, out list)) {
				list = new List<string>();
				tradmap[ch] = list;
			}
			list.Add(r.GetString(1));
		});
	}

	static void exec(string sql) {
		if (conn == null) return;
		try {
			using var c = conn.CreateCommand();
			c.CommandText = sql;
			c.ExecuteNonQuery();
		}
		catch { writable = false; }
	}

	static void cmd(string sql, params (string n, object v)[] args) {
		if (conn == null || !writable) return;
		try {
			using var c = conn.CreateCommand();
			c.CommandText = sql;
			foreach (var a in args) c.Parameters.AddWithValue(a.n, a.v ?? "");
			c.ExecuteNonQuery();
		}
		catch { }
	}

	static object scalar(string sql, params (string n, object v)[] args) {
		if (conn == null) return null;
		try {
			using var c = conn.CreateCommand();
			c.CommandText = sql;
			foreach (var a in args) c.Parameters.AddWithValue(a.n, a.v ?? "");
			return c.ExecuteScalar();
		}
		catch { return null; }
	}

	static void each(string sql, Action<SqliteDataReader> row, params (string n, object v)[] args) {
		if (conn == null) return;
		try {
			using var c = conn.CreateCommand();
			c.CommandText = sql;
			foreach (var a in args) c.Parameters.AddWithValue(a.n, a.v ?? "");
			using var r = c.ExecuteReader();
			while (r.Read()) row(r);
		}
		catch { }
	}

	static void close() {
		try { conn?.Dispose(); } catch { }
		conn = null;
		opened = "";
		writable = false;
		tradmap = new Dictionary<char, List<string>>();
	}
}
