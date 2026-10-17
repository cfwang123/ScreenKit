using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace ScreenKit;

sealed class ImeHit {
	public string Text = "";
	public string Comment = "";
}

/// <summary>程序旁的 ime.db 是五笔、日语、韩语的词库本体（可安装、可上传）。
/// 个人数据放同目录的 ime_user.db：custom（自定义词库）、pin（置顶）、pick（选用次数）三张表，
/// 这样上传的 ime.db 里只有词库，不带自己的词和使用记录。</summary>
static class ImeLex {
	/// <summary>ime_user.db 里的三张表，外加记文件指纹的 meta。</summary>
	const string usertables = @"
CREATE TABLE IF NOT EXISTS custom (kind TEXT NOT NULL, key TEXT NOT NULL, text TEXT NOT NULL, PRIMARY KEY (kind,key,text));
CREATE TABLE IF NOT EXISTS pin (kind TEXT NOT NULL, key TEXT NOT NULL, text TEXT NOT NULL, PRIMARY KEY (kind,key,text));
CREATE TABLE IF NOT EXISTS pick (kind TEXT NOT NULL, key TEXT NOT NULL, text TEXT NOT NULL, picks INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (kind,key,text));
CREATE INDEX IF NOT EXISTS ix_custom_kind ON custom(kind,key);
CREATE INDEX IF NOT EXISTS ix_pin_kind ON pin(kind,key);
CREATE INDEX IF NOT EXISTS ix_pick_kind ON pick(kind,key);
CREATE TABLE IF NOT EXISTS meta (k TEXT PRIMARY KEY, v TEXT)";

	static SqliteConnection conn;
	static string opened = "";
	static string usropened = "";
	static bool batteries;
	static bool writable;
	static bool userwrite;
	static Dictionary<char, List<string>> tradmap = new Dictionary<char, List<string>>();

	public static string PathOf => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ime.db");

	/// <summary>自定义词库、置顶、选用次数所在的库，跟 ime.db 同目录。</summary>
	public static string UserPathOf => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ime_user.db");

	/// <summary>帮助 → 安装功能 里的 ime.db 已完整安装。</summary>
	public static bool DbInstalled =>
		FeatureInstaller.Probe(FeatureKind.ImeDb) == FeatureInstallState.Installed;

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

	public static List<Cand> QueryKr(IList<string> lo, IList<string> hi, string skip) {
		var map = new Dictionary<string, Cand>();
		if (lo == null || hi == null || lo.Count == 0 || lo.Count != hi.Count || !ready()) return new List<Cand>();
		var where = "";
		var args = new List<(string n, object v)>();
		for (int i = 0; i < lo.Count; i++) {
			if (i > 0) where += " OR ";
			where += "(text>=$a" + i + " AND text<$b" + i + ")";
			args.Add(("$a" + i, lo[i]));
			args.Add(("$b" + i, hi[i]));
		}
		if (!string.IsNullOrEmpty(skip)) {
			where = "(" + where + ") AND text!=$skip";
			args.Add(("$skip", skip));
		}
		var arg = args.ToArray();
		each("SELECT text,freq FROM kr_word WHERE " + where + " ORDER BY freq DESC LIMIT 40", r => {
			var t = r.GetString(0);
			map[t] = new Cand { Text = t, Raw = t, Rank = -r.GetInt32(1), Comment = Hanja(t) };
		}, arg);
		userwords("ko", false, where, arg, true, c => {
			Cand old;
			if (!map.TryGetValue(c.Raw, out old)) {
				c.Text = c.Raw;
				c.Comment = Hanja(c.Raw);
				map[c.Raw] = c;
				return;
			}
			if (c.Pinned) old.Pinned = true;
			if (c.Picks > old.Picks) old.Picks = c.Picks;
		});
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
		if (string.IsNullOrEmpty(text)) return;
		var known = scalar(@"SELECT 1 FROM kr_word WHERE text=$t
			UNION SELECT 1 FROM u.custom WHERE kind='ko' AND text=$t
			UNION SELECT 1 FROM u.pick WHERE kind='ko' AND text=$t", ("$t", text));
		if (known == null || known is DBNull) return;
		touch("ko", "", text);
	}

	public static void TogglePinKr(string text) => pin("ko", "", text);

	/// <summary>五笔候选。rare 关掉时不要生僻字：词库没词频的行（rank 0 或 1000000 起）、
	/// 编不进 GBK 的字、以及 GB2312 七千字表外的罕用字（判据见 <see cref="ImeRare"/>）。</summary>
	public static List<Cand> QueryWubi(string prefix, bool trad, bool rare) {
		var map = new Dictionary<string, Cand>();
		if (string.IsNullOrEmpty(prefix) || !ready()) return new List<Cand>();
		void put(Cand c) {
			var k = c.Code + "\n" + c.Raw;
			Cand old;
			if (!map.TryGetValue(k, out old)) { map[k] = c; return; }
			if (c.Pinned) old.Pinned = true;
			if (c.Picks > old.Picks) old.Picks = c.Picks;
		}
		each(@"SELECT code,text,rank FROM wubi WHERE code LIKE $p || '%'"
			+ (rare ? "" : " AND rank<>0 AND rank<1000000")
			+ @" ORDER BY length(code), rank LIMIT 400",
			r => {
				var code = r.GetString(0);
				var text = r.GetString(1);
				var rank = r.GetInt32(2);
				if (!rare && ImeRare.Is(text, rank)) return;
				put(new Cand { Code = code, Raw = text, Rank = rank });
			},
			("$p", prefix));
		userwords("wubi", true, "key LIKE $p || '%'", new[] { ("$p", (object)prefix) }, rare, put);
		return Rank.Finish(new List<Cand>(map.Values), prefix, trad, alts);
	}

	/// <summary>把 ime_user.db 三张表里某个 kind 的词并进候选：自定义词（放进词库的）和置顶的一直出，
	/// 只有平时选用过的生僻词跟着开关走。</summary>
	static void userwords(string kind, bool keyed, string where, (string n, object v)[] args, bool rare, Action<Cand> put) {
		if (!userok()) return;
		var extra = string.IsNullOrEmpty(where) ? "" : " AND " + where;
		var key = new List<(string n, object v)> { ("$k", kind) };
		if (args != null) key.AddRange(args);
		var all = key.ToArray();
		Cand one(SqliteDataReader r, bool pin, int picks) => new Cand {
			Code = keyed ? r.GetString(0) : null,
			Raw = r.GetString(1),
			Pinned = pin, Picks = picks, Rank = 500
		};
		each("SELECT key,text FROM u.custom WHERE kind=$k" + extra, r => put(one(r, false, 1)), all);
		each("SELECT key,text FROM u.pin WHERE kind=$k" + extra, r => put(one(r, true, 0)), all);
		each("SELECT key,text,picks FROM u.pick WHERE kind=$k" + extra, r => {
			var text = r.GetString(1);
			if (!rare && ImeRare.Is(text, null)) return;
			put(one(r, false, r.GetInt32(2)));
		}, all);
	}

	/// <summary>启用输入法时先把三种查询各跑一遍：第一次查五笔要把整表扫进缓存，留在打字时会拖慢键盘钩子。</summary>
	public static void Warmup() {
		if (!ready()) return;
		try { QueryWubi("a", false, false); } catch { }
		try { QueryJa("あ"); } catch { }
		try { QueryKr(new List<string> { "가" }, new List<string> { "가" }, "가"); } catch { }
	}

	static bool prewarmed;

	/// <summary>后台另开只读连接把 ime.db 扫进系统缓存，别等第一次打字时现读。
	/// 不用打字那条连接：那样两边会互相等，反而把钩子拖住。</summary>
	public static void Prewarm() {
		if (prewarmed) return;
		prewarmed = true;
		if (!DbInstalled || !NativeRuntime.HasSqlite()) return;
		if (!batteries) {
			SQLitePCL.Batteries_V2.Init();
			batteries = true;
		}
		var th = new Thread(() => {
			try {
				Thread.Sleep(600);      // 让启动时的窗口、图标先忙完
				var csb = new SqliteConnectionStringBuilder {
					DataSource = PathOf,
					Mode = SqliteOpenMode.ReadOnly,
					Cache = SqliteCacheMode.Shared,
					Pooling = false,
				};
				using var c = new SqliteConnection(csb.ToString());
				c.Open();
				foreach (var sql in new[] {
					"SELECT code,text,rank FROM wubi WHERE code LIKE 'a%' ORDER BY length(code), rank LIMIT 400",
					"SELECT code,text,rank FROM wubi WHERE code LIKE 'w%' ORDER BY length(code), rank LIMIT 400",
					"SELECT code,text,rank FROM wubi WHERE code LIKE 'y%' ORDER BY length(code), rank LIMIT 400",
					"SELECT reading,text,rank FROM ja_word WHERE reading IN ('あ','あい','きょう') LIMIT 40",
					"SELECT text,freq FROM kr_word WHERE text LIKE '가%' ORDER BY freq DESC LIMIT 40",
				}) {
					try {
						using var q = c.CreateCommand();
						q.CommandText = sql;
						using var r = q.ExecuteReader();
						while (r.Read()) { }
					}
					catch { }
				}
			}
			catch { }
		}) { IsBackground = true };
		th.Start();
	}

	public static void TouchWubi(string code, string text) {
		if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(text)) return;
		touch("wubi", code, text);
	}

	public static void TogglePinWubi(string code, string text) => pin("wubi", code, text);

	/// <summary>选用次数记一笔。</summary>
	static void touch(string kind, string key, string text) {
		if (!userok() || string.IsNullOrEmpty(text)) return;
		cmd(@"INSERT INTO u.pick(kind,key,text,picks) VALUES($k,$c,$t,1)
			ON CONFLICT(kind,key,text) DO UPDATE SET picks=picks+1",
			("$k", kind), ("$c", key ?? ""), ("$t", text));
	}

	/// <summary>置顶表里有这条就是置顶，再点一次取消。</summary>
	static void pin(string kind, string key, string text) {
		if (!userok() || string.IsNullOrEmpty(text)) return;
		var o = scalar("SELECT 1 FROM u.pin WHERE kind=$k AND key=$c AND text=$t",
			("$k", kind), ("$c", key ?? ""), ("$t", text));
		if (o == null || o is DBNull)
			cmd("INSERT INTO u.pin(kind,key,text) VALUES($k,$c,$t)", ("$k", kind), ("$c", key ?? ""), ("$t", text));
		else
			cmd("DELETE FROM u.pin WHERE kind=$k AND key=$c AND text=$t", ("$k", kind), ("$c", key ?? ""), ("$t", text));
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
			userwords("ja", true, "key IN (" + inn + ")", args, true, c => rows.Add(c));
		}
		return JaCand.Build(reading, rows);
	}

	public static void TouchJa(string reading, string text) {
		if (string.IsNullOrEmpty(reading) || string.IsNullOrEmpty(text)) return;
		touch("ja", reading, text);
	}

	public static void TogglePinJa(string reading, string text) => pin("ja", reading, text);

	// ───────── 自定义词库：同步 / 导入 / 删除 ─────────

	/// <summary>该语言自定义词库里的词，就是 dict/ 文本同步进来的那份。</summary>
	public static List<ImeDictEntry> CustomWords(string kind) {
		var list = new List<ImeDictEntry>();
		if (string.IsNullOrEmpty(kind) || !userok()) return list;
		each("SELECT key,text FROM u.custom WHERE kind=$k ORDER BY key, text",
			r => list.Add(new ImeDictEntry { Key = r.GetString(0), Text = r.GetString(1) }),
			("$k", kind));
		return list;
	}

	/// <summary>按 kind 整份换成给定词条（文本改了以后同步）。返回写入条数，0 也可能是空词库。</summary>
	public static int ReplaceCustom(string kind, IList<ImeDictEntry> rows) {
		if (string.IsNullOrEmpty(kind) || !userok()) return 0;
		int n = 0;
		try {
			using var tx = conn.BeginTransaction();
			using (var d = conn.CreateCommand()) {
				d.Transaction = tx;
				d.CommandText = "DELETE FROM u.custom WHERE kind=$k";
				d.Parameters.AddWithValue("$k", kind);
				d.ExecuteNonQuery();
			}
			n = insertcustom(tx, kind, rows);
			tx.Commit();
		}
		catch { return 0; }
		return n;
	}

	/// <summary>并入若干条自定义词，已有的跳过。返回新并入条数。</summary>
	public static int AddCustom(string kind, IList<ImeDictEntry> rows) {
		if (string.IsNullOrEmpty(kind) || !userok() || rows == null || rows.Count == 0) return 0;
		int n = 0;
		try {
			using var tx = conn.BeginTransaction();
			n = insertcustom(tx, kind, rows);
			tx.Commit();
		}
		catch { return 0; }
		return n;
	}

	static int insertcustom(SqliteTransaction tx, string kind, IList<ImeDictEntry> rows) {
		if (rows == null || rows.Count == 0) return 0;
		int n = 0;
		using var c = conn.CreateCommand();
		c.Transaction = tx;
		c.CommandText = "INSERT OR IGNORE INTO u.custom(kind,key,text) VALUES($k,$c,$t)";
		c.Parameters.AddWithValue("$k", kind);
		var pc = c.Parameters.AddWithValue("$c", "");
		var pt = c.Parameters.AddWithValue("$t", "");
		foreach (var e in rows) {
			if (e == null || string.IsNullOrEmpty(e.Text)) continue;
			if (kind != "ko" && string.IsNullOrEmpty(e.Key)) continue;
			pc.Value = e.Key ?? "";
			pt.Value = e.Text;
			n += c.ExecuteNonQuery();
		}
		return n;
	}

	public static int DeleteCustom(string kind, ImeDictEntry e) {
		if (e == null || string.IsNullOrEmpty(e.Text)) return 0;
		return exec1("DELETE FROM u.custom WHERE kind=$k AND key=$c AND text=$t",
			("$k", kind), ("$c", e.Key ?? ""), ("$t", e.Text));
	}

	/// <summary>清空该语言的自定义词、置顶和选用记录。</summary>
	public static int ClearWords(string kind) {
		if (string.IsNullOrEmpty(kind) || !userok()) return 0;
		int n = 0;
		try {
			using var tx = conn.BeginTransaction();
			foreach (var t in new[] { "custom", "pin", "pick" }) {
				using var c = conn.CreateCommand();
				c.Transaction = tx;
				c.CommandText = "DELETE FROM u." + t + " WHERE kind=$k";
				c.Parameters.AddWithValue("$k", kind);
				n += c.ExecuteNonQuery();
			}
			tx.Commit();
		}
		catch { return 0; }
		return n;
	}

	/// <summary>用户库里的一份记录，给自定义词库记文件指纹用。</summary>
	public static string Meta(string key) {
		if (conn == null) return "";
		var o = scalar("SELECT v FROM u.meta WHERE k=$k", ("$k", key));
		return o == null || o is DBNull ? "" : o.ToString();
	}

	public static void SaveMeta(string key, string value) {
		if (!userok()) return;
		cmd("INSERT INTO u.meta(k,v) VALUES($k,$v) ON CONFLICT(k) DO UPDATE SET v=$v",
			("$k", key), ("$v", value ?? ""));
	}

	static int exec1(string sql, params (string n, object v)[] args) {
		if (!userok()) return 0;
		try {
			using var c = conn.CreateCommand();
			c.CommandText = sql;
			foreach (var a in args) c.Parameters.AddWithValue(a.n, a.v ?? "");
			return c.ExecuteNonQuery();
		}
		catch { return 0; }
	}

	static IList<string> alts(char ch) {
		List<string> list;
		return tradmap.TryGetValue(ch, out list) ? list : null;
	}

	static bool ready() {
		try { return ensure(string.IsNullOrEmpty(opened) ? PathOf : opened); }
		catch { return false; }
	}

	/// <summary>用户库（自定义词 / 置顶 / 选用）能不能用。</summary>
	static bool userok() {
		if (!ready() || !userwrite) return false;
		return true;
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
		attachuser(c, path);
		if (!writable) {
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

	/// <summary>把 ime_user.db 挂成 u：自定义词库、置顶、选用次数各一张表。挂不上就只当没有用户词。</summary>
	static void attachuser(SqliteConnection c, string path) {
		userwrite = false;
		usropened = "";
		try {
			var dir = Path.GetDirectoryName(path);
			if (string.IsNullOrEmpty(dir)) return;
			var up = Path.Combine(dir, "ime_user.db");
			using (var a = c.CreateCommand()) {
				a.CommandText = "ATTACH DATABASE '" + up.Replace("'", "''") + "' AS u";
				a.ExecuteNonQuery();
			}
			using (var t = c.CreateCommand()) {
				t.CommandText = usertables;
				t.ExecuteNonQuery();
			}
			usropened = up;
			userwrite = true;
		}
		catch {
			userwrite = false;
		}
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
		usropened = "";
		writable = false;
		userwrite = false;
		tradmap = new Dictionary<char, List<string>>();
	}
}
