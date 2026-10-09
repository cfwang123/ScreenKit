using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace ScreenKit;

// ==================== 词典数据 ====================
#region
sealed class DictHit {
	public long Id;
	public string Dict = "";
	public string Headword = "";
	public string Kanji = "";
	public string Reading = "";
	public string Pos = "";
	public string Preview = "";
	public string Matched = "";
	public string Via = "";
	public int Rank;
	public int ViaRank = -1;
	public bool IsHead;
}

sealed class DictText {
	public string Text = "";
	public string Zh = "";
}

sealed class DictSense {
	public string Pos = "";
	public string Ko = "";
	public string Zh = "";
	public string ZhDef = "";
	public string En = "";
	public string EnDef = "";
	public string Ja = "";
	public string JaDef = "";
	public List<DictText> Phrases = new();
	public List<DictText> Sentences = new();
	/// <summary>从属义项，详情里不单独编号。</summary>
	public bool Sub;
	/// <summary>惯用语词头。非空时详情在本义项前另起一行「词组」。</summary>
	public string Idiom = "";
}

sealed class DictEntry {
	public long Id;
	public string Dict = "";
	public string Headword = "";
	public string Word = "";
	public string Kanji = "";
	public string Reading = "";
	public string Pron = "";
	public string Pos = "";
	public string Extra = "";
	public string Etymology = "";
	public List<string> Usage = new();
	public List<string> See = new();
	public List<string> Conjugations = new();
	public List<DictSense> Senses = new();

	public string ZhSpeak() {
		var sb = new StringBuilder();
		foreach (var s in Senses) {
			var t = join2(s.Zh, s.ZhDef);
			if (t.Length == 0) continue;
			if (sb.Length > 0) sb.Append('。');
			sb.Append(t);
			if (sb.Length >= 180) break;
		}
		return sb.ToString();
	}

	static string join2(string a, string b) {
		if (string.IsNullOrEmpty(a)) return b ?? "";
		if (string.IsNullOrEmpty(b)) return a;
		return a + "  " + b;
	}
}
#endregion

// ==================== 词典库（只读） ====================
// dict.db：entry + lookup_key(entry_id, dict, key, key_len)。没有 key_rev / lookup_tri。
// 查询与 rustdict 一致：前缀 key LIKE 'q%'；前缀不够再 key LIKE '%q%' 且排除已是前缀的行。
// rank：0 全等 / 1 前缀 / 2 后缀 / 3 中缀。韩语活用形命中很少时再按语尾剥一层。
#region
static class DictDb {
	const int FETCHCAP = 4000;

	const int IDLE_MS = 5 * 60 * 1000;

	static SqliteConnection conn;
	static readonly object dblock = new();
	static Timer idletimer;
	static bool batteries;
	static string dberr = "";
	static string dbpath = "";
	static int lastuse;

	public static bool Ready => conn != null;
	/// <summary>missing：库文件不在；其它为打开失败说明。</summary>
	public static string Error => dberr;

	public static bool Init(string path) {
		lock (dblock) return openunlocked(path);
	}

	public static void Close() {
		lock (dblock) closeunlocked();
	}

	/// <summary>已打开则记下使用时间。空闲关掉之后按上次路径再打开。</summary>
	static bool readyunlocked() {
		if (conn != null) {
			lastuse = Environment.TickCount;
			return true;
		}
		if (string.IsNullOrEmpty(dbpath)) return false;
		return openunlocked(dbpath);
	}

	static bool openunlocked(string path) {
		closeunlocked();
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
			dberr = "missing";
			return false;
		}
		if (!NativeRuntime.HasSqlite()) {
			dberr = "sqlite";
			return false;
		}
		try {
			ensurebatteries();
			var csb = new SqliteConnectionStringBuilder {
				DataSource = path,
				Mode = SqliteOpenMode.ReadOnly,
				Cache = SqliteCacheMode.Shared,
				Pooling = false,
			};
			var c = new SqliteConnection(csb.ToString());
			c.Open();
			pragma(c, "PRAGMA query_only = ON;");
			pragma(c, "PRAGMA mmap_size = 268435456;");
			pragma(c, "PRAGMA case_sensitive_like = ON;");
			pragma(c, "PRAGMA temp_store = MEMORY;");
			using (var cmd = c.CreateCommand()) {
				cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('lookup_key','entry')";
				if (Convert.ToInt64(cmd.ExecuteScalar()) < 2) {
					c.Dispose();
					dberr = "dict.db schema";
					return false;
				}
			}
			conn = c;
			dbpath = path;
			dberr = "";
			lastuse = Environment.TickCount;
			if (idletimer == null)
				idletimer = new Timer(onidle, null, OnnxIdle.TickMs, OnnxIdle.TickMs);
			return true;
		}
		catch (Exception ex) {
			dberr = sqlitefail(ex) ? "sqlite" : ex.Message;
			return false;
		}
	}

	static void onidle(object _) {
		var closed = false;
		try {
			lock (dblock) {
				if (conn == null || !OnnxIdle.Due(lastuse, IDLE_MS)) return;
				closeunlocked();
				closed = true;
			}
		}
		catch { return; }
		if (!closed) return;
		try { CaptureLog.Info("dict db idle close"); } catch { }
	}

	static void closeunlocked() {
		try { conn?.Dispose(); } catch { }
		conn = null;
		lastuse = 0;
	}

	static bool sqlitefail(Exception ex) {
		for (var e = ex; e != null; e = e.InnerException) {
			if (e is DllNotFoundException) return true;
			var msg = e.Message ?? "";
			if (msg.IndexOf("e_sqlite3", StringComparison.OrdinalIgnoreCase) >= 0) return true;
		}
		return false;
	}

	static void ensurebatteries() {
		if (batteries) return;
		SQLitePCL.Batteries_V2.Init();
		batteries = true;
	}

	/// <summary>dict 为空查全部，否则 zh / en / ja / ko。limit 为界面条数上限。</summary>
	public static List<DictHit> Search(string rawq, string dict, int limit) {
		var empty = new List<DictHit>();
		if (string.IsNullOrWhiteSpace(rawq)) return empty;
		if (limit < 1) limit = 1;
		if (limit > 300) limit = 300;
		var q = normalize(rawq);
		if (q.Length == 0) return empty;
		var d = normdict(dict);
		var hits = new List<DictHit>();
		lock (dblock) {
			if (!readyunlocked()) return empty;
			prefixkeys(q, d, hits);
			var prefixN = uniquecount(hits);
			var wantContains = shouldruncontains(q, prefixN, limit);
			// 活用形通常不在索引里，先剥语尾，避免对整表做 %활용% 
			var deferContains = wantContains && KoStem.HasHangul(q) && prefixN < 4;
			if (wantContains && !deferContains) containskeys(q, d, hits);
			if (wantstem(q, d, uniquecount(hits))) stemkeys(q, d, hits, limit);
			if (deferContains && uniquecount(hits) < 4) containskeys(q, d, hits);
		}
		sort(hits);
		var outlist = new List<DictHit>();
		var seen = new HashSet<long>();
		foreach (var h in hits) {
			if (!seen.Add(h.Id)) continue;
			outlist.Add(h);
			if (outlist.Count >= limit) break;
		}
		lock (dblock) {
			if (readyunlocked()) fillmeta(outlist);
		}
		return outlist;
	}

	public static DictEntry Get(long id) {
		DictEntry e;
		string jsontext;
		lock (dblock) {
			if (!readyunlocked()) return null;
			using var cmd = conn.CreateCommand();
			cmd.CommandText = "SELECT id, dict, headword, json FROM entry WHERE id = @id";
			cmd.Parameters.AddWithValue("@id", id);
			using var r = cmd.ExecuteReader();
			if (!r.Read()) return null;
			e = new DictEntry {
				Id = r.GetInt64(0),
				Dict = r.IsDBNull(1) ? "" : r.GetString(1),
				Headword = r.IsDBNull(2) ? "" : r.GetString(2),
			};
			jsontext = r.IsDBNull(3) ? "" : r.GetString(3);
		}
		parsejson(e, jsontext);
		if (e.Word.Length == 0) e.Word = e.Headword;
		if (e.Pron.Length == 0) e.Pron = e.Reading;
		if (e.Reading.Length == 0) e.Reading = e.Pron;
		return e;
	}

	static void prefixkeys(string q, string dict, List<DictHit> hits) {
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT entry_id, key, dict, is_head FROM lookup_key " +
			"WHERE key LIKE @p ESCAPE '\\' AND (@d = '' OR dict = @d) LIMIT @n";
		cmd.Parameters.AddWithValue("@p", escape(q) + "%");
		cmd.Parameters.AddWithValue("@d", dict);
		cmd.Parameters.AddWithValue("@n", FETCHCAP);
		collect(cmd, q, hits, "");
	}

	static void exactkeys(string q, string dict, List<DictHit> hits, string via, int viarank) {
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT entry_id, key, dict, is_head FROM lookup_key " +
			"WHERE key = @p AND (@d = '' OR dict = @d) ORDER BY is_head DESC, key_len ASC LIMIT @n";
		cmd.Parameters.AddWithValue("@p", q);
		cmd.Parameters.AddWithValue("@d", dict);
		cmd.Parameters.AddWithValue("@n", 40);
		collect(cmd, q, hits, via, viarank);
	}

	static void containskeys(string q, string dict, List<DictHit> hits) {
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT entry_id, key, dict, is_head FROM lookup_key " +
			"WHERE key LIKE @p ESCAPE '\\' AND key NOT LIKE @pre ESCAPE '\\' " +
			"AND (@d = '' OR dict = @d) LIMIT @n";
		var esc = escape(q);
		cmd.Parameters.AddWithValue("@p", "%" + esc + "%");
		cmd.Parameters.AddWithValue("@pre", esc + "%");
		cmd.Parameters.AddWithValue("@d", dict);
		cmd.Parameters.AddWithValue("@n", FETCHCAP);
		collect(cmd, q, hits, "");
	}

	static void stemkeys(string q, string dict, List<DictHit> hits, int limit) {
		var d = dict.Length == 0 ? "ko" : dict;
		var seen = new HashSet<long>();
		foreach (var h in hits) seen.Add(h.Id);
		var vi = 0;
		foreach (var cand in KoStem.Candidates(q)) {
			if (cand == q) continue;
			var extra = new List<DictHit>();
			exactkeys(cand, d, extra, q, vi);
			if (extra.Count == 0 && countchars(cand) >= 2)
				prefixkeys(cand, d, extra);
			foreach (var h in extra) {
				if (!seen.Add(h.Id)) continue;
				if (h.Via.Length == 0) h.Via = q;
				if (h.ViaRank < 0) h.ViaRank = vi;
				hits.Add(h);
			}
			vi++;
			if (vi >= 8 || hits.Count >= limit * 2) break;
		}
	}

	static void collect(SqliteCommand cmd, string q, List<DictHit> hits, string via, int viarank = -1) {
		using var r = cmd.ExecuteReader();
		while (r.Read()) {
			var key = r.IsDBNull(1) ? "" : r.GetString(1);
			var h = new DictHit {
				Id = r.GetInt64(0),
				Matched = key,
				Dict = r.IsDBNull(2) ? "" : r.GetString(2),
				Rank = rankof(q, key),
				Via = via ?? "",
				ViaRank = viarank,
				IsHead = r.FieldCount > 3 && !r.IsDBNull(3) && r.GetInt64(3) != 0,
			};
			hits.Add(h);
		}
	}

	static void fillmeta(List<DictHit> hits) {
		if (hits.Count == 0 || conn == null) return;
		var ids = new List<long>();
		var seen = new HashSet<long>();
		foreach (var h in hits)
			if (seen.Add(h.Id)) ids.Add(h.Id);
		var map = new Dictionary<long, DictEntry>();
		using var cmd = conn.CreateCommand();
		var sql = new StringBuilder("SELECT id, headword, json FROM entry WHERE id IN (");
		for (var i = 0; i < ids.Count; i++) {
			if (i > 0) sql.Append(',');
			sql.Append("@i").Append(i);
			cmd.Parameters.AddWithValue("@i" + i, ids[i]);
		}
		sql.Append(')');
		cmd.CommandText = sql.ToString();
		using (var r = cmd.ExecuteReader()) {
			while (r.Read()) {
				var row = new DictEntry {
					Id = r.GetInt64(0),
					Headword = r.IsDBNull(1) ? "" : r.GetString(1),
				};
				parsejson(row, r.IsDBNull(2) ? "" : r.GetString(2));
				map[row.Id] = row;
			}
		}
		foreach (var h in hits) {
			if (!map.TryGetValue(h.Id, out var e)) continue;
			h.Headword = e.Word.Length > 0 ? e.Word : e.Headword;
			h.Kanji = h.Dict == "ja" ? e.Kanji : "";
			h.Reading = e.Pron;
			h.Pos = e.Pos;
			h.Preview = listpreview(e);
		}
	}

	/// <summary>日语词头发音。主表记已是假名就读主表记，否则读第一条没有汉字的平假名或片假名。</summary>
	internal static string JaSpeak(string word, string forms, string pron) {
		var w = (word ?? "").Trim();
		if (kanaonly(w)) return w;
		var kana = firstkana(pron);
		if (kana.Length == 0) kana = firstkana(forms);
		if (kana.Length > 0) return kana;
		return w;
	}

	static string firstkana(string forms) {
		if (string.IsNullOrEmpty(forms)) return "";
		foreach (var raw in forms.Split(',', '，')) {
			var s = cleanform(raw);
			if (!kanaonly(s)) continue;
			return s;
		}
		return "";
	}

	static string cleanform(string raw) {
		var s = (raw ?? "").Trim();
		if (s.Length == 0 || obsoleteform(s)) return "";
		var i = s.IndexOf('\'');
		if (i >= 0) s = s.Substring(0, i).Trim();
		return s;
	}

	static bool kanaonly(string s) {
		if (string.IsNullOrEmpty(s) || haskanji(s)) return false;
		return haskana(s);
	}

	static bool haskana(string s) {
		foreach (var c in s) {
			if (c >= '\u3040' && c <= '\u309F') return true;
			if (c >= '\u30A0' && c <= '\u30FF') return true;
			if (c >= '\uFF66' && c <= '\uFF9D') return true;
		}
		return false;
	}

	static bool haskanji(string s) {
		for (var i = 0; i < s.Length; i++) {
			var c = s[i];
			if (c >= '\u4E00' && c <= '\u9FFF') return true;
			if (c >= '\u3400' && c <= '\u4DBF') return true;
			if (c >= '\uF900' && c <= '\uFAFF') return true;
			if (!char.IsHighSurrogate(c) || i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1])) continue;
			var cp = char.ConvertToUtf32(s, i);
			if (cp >= 0x20000 && cp <= 0x3FFFF) return true;
			i++;
		}
		return false;
	}

	/// <summary>日语列表标题：主表记加别表记，逗号分隔。丢掉 out-dated / obsolete 的写法，其它括注去掉只留表记。</summary>
	internal static string JaForms(string word, string kanji) {
		var parts = new List<string>();
		pushform(parts, word);
		if (!string.IsNullOrEmpty(kanji)) {
			foreach (var raw in kanji.Split(',', '，'))
				pushform(parts, raw);
		}
		return string.Join(", ", parts);
	}

	static void pushform(List<string> parts, string raw) {
		var s = (raw ?? "").Trim();
		if (s.Length == 0 || obsoleteform(s)) return;
		var i = s.IndexOf('\'');
		if (i >= 0) s = s.Substring(0, i).Trim();
		if (s.Length == 0) return;
		foreach (var p in parts)
			if (p == s) return;
		parts.Add(s);
	}

	static bool obsoleteform(string s) {
		var i = s.IndexOf('\'');
		if (i < 0) return false;
		var note = s.Substring(i);
		return note.IndexOf("out-dated", StringComparison.OrdinalIgnoreCase) >= 0
			|| note.IndexOf("outdated", StringComparison.OrdinalIgnoreCase) >= 0
			|| note.IndexOf("obsolete", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	static string listpreview(DictEntry e) {
		if (e.Senses.Count > 0) {
			var sb = new StringBuilder();
			foreach (var s in e.Senses) {
				var g = sensegloss(s);
				if (g.Length == 0) continue;
				if (sb.Length > 0) sb.Append("; ");
				sb.Append(g);
			}
			if (sb.Length > 0) return sb.ToString();
		}
		return trunc(e.Extra, 80);
	}

	static string sensegloss(DictSense s) {
		var zh = join2(s.Zh, s.ZhDef);
		var en = join2(s.En, s.EnDef);
		if (Loc.IsEn && en.Length > 0) return en;
		if (zh.Length > 0) return zh;
		if (en.Length > 0) return en;
		if (s.Ko.Length > 0) return s.Ko;
		return join2(s.Ja, s.JaDef);
	}

	static string trunc(string s, int n) {
		if (string.IsNullOrEmpty(s) || s.Length <= n) return s ?? "";
		return s.Substring(0, n);
	}

	static bool shouldruncontains(string q, int prefixUnique, int limit) {
		if (prefixUnique >= limit) return false;
		if (prefixUnique > 0 && countchars(q) >= 4) return false;
		return true;
	}

	static bool wantstem(string q, string dict, int n) {
		if (n >= 4) return false;
		if (dict.Length > 0 && dict != "ko") return false;
		return KoStem.HasHangul(q);
	}

	static int uniquecount(List<DictHit> hits) {
		var s = new HashSet<long>();
		foreach (var h in hits) s.Add(h.Id);
		return s.Count;
	}

	static void sort(List<DictHit> hits) {
		hits.Sort((a, b) => {
			var r = a.Rank.CompareTo(b.Rank);
			if (r != 0) return r;
			r = a.ViaRank.CompareTo(b.ViaRank);
			if (r != 0) return r;
			r = b.IsHead.CompareTo(a.IsHead);
			if (r != 0) return r;
			r = dictrank(a.Dict).CompareTo(dictrank(b.Dict));
			if (r != 0) return r;
			r = a.Matched.Length.CompareTo(b.Matched.Length);
			if (r != 0) return r;
			r = string.CompareOrdinal(a.Matched, b.Matched);
			if (r != 0) return r;
			return a.Id.CompareTo(b.Id);
		});
	}

	static int dictrank(string d) {
		if (d == "zh") return 0;
		if (d == "en") return 1;
		if (d == "ja") return 2;
		if (d == "ko") return 3;
		return 9;
	}

	static int rankof(string q, string key) {
		if (key == q) return 0;
		if (key.StartsWith(q, StringComparison.Ordinal)) return 1;
		if (key.EndsWith(q, StringComparison.Ordinal)) return 2;
		return 3;
	}

	static string normdict(string dict) {
		var d = (dict ?? "").Trim().ToLowerInvariant();
		if (d == "zh" || d == "en" || d == "ja" || d == "ko") return d;
		return "";
	}

	// ==================== 词条 JSON ====================
	static void parsejson(DictEntry e, string jsontext) {
		if (string.IsNullOrWhiteSpace(jsontext)) return;
		try {
			JsonNode n = null;
			try { n = JsonNode.Parse(jsontext); }
			catch { n = JsonNode.Parse(quotekeys(jsontext)); }
			if (n == null) return;
			if (iscompact(n)) parsecmp(e, n);
			else parselong(e, n);
			if (e.Word.Length == 0) e.Word = e.Headword;
			if (e.Pron.Length == 0) e.Pron = e.Reading;
		}
		catch { }
	}

	static bool iscompact(JsonNode n) {
		if (n is not JsonObject o) return false;
		if (o.ContainsKey("word") || o.ContainsKey("senses") || o.ContainsKey("posGroups")) return false;
		return o.ContainsKey("w") || o.ContainsKey("n") || o.ContainsKey("g") || o.ContainsKey("o") || o.ContainsKey("c");
	}

	static void parsecmp(DictEntry e, JsonNode n) {
		e.Word = str(n["w"]);
		e.Pron = str(n["o"]);
		if (e.Kanji.Length == 0) e.Kanji = str(n["k"]);
		if (e.Pos.Length == 0) e.Pos = str(n["p"]);
		if (e.Etymology.Length == 0) e.Etymology = str(n["y"]);
		if (e.Usage.Count == 0) addstrings(n["u"], e.Usage, 8);
		e.Extra = str(n["x"]);
		if (e.Extra.Length == 0) e.Extra = str(n["r"]);
		if (n["c"] is JsonArray ca) {
			foreach (var c in ca) {
				if (e.Conjugations.Count >= 12) break;
				string form = "", zh = "";
				if (c is JsonArray a) {
					form = str(a.Count > 0 ? a[0] : null);
					zh = str(a.Count > 2 ? a[2] : null);
				}
				else form = str(c);
				if (form.Length == 0) continue;
				e.Conjugations.Add(zh.Length > 0 ? form + "　" + zh : form);
			}
		}
		if (n["n"] is JsonArray na) {
			foreach (var sn in na) addsense(e, sn, "");
		}
		if (n["g"] is JsonArray ga) {
			foreach (var g in ga) addgroup(e, g);
		}
		if (n["i"] is JsonArray ia) {
			foreach (var id in ia) addidiom(e, id);
		}
	}

	static void parselong(DictEntry e, JsonNode n) {
		e.Word = str(n["word"]);
		e.Kanji = str(n["kanji"]);
		e.Pron = str(n["pron"]);
		if (e.Pos.Length == 0) e.Pos = str(n["pos"]);
		e.Extra = str(n["extra"]);
		e.Etymology = str(n["etymology"]);
		addstrings(n["usage"], e.Usage, 8);
		addstrings(n["see"], e.See, 8);
		if (n["conjugations"] is JsonArray ca) {
			foreach (var c in ca) {
				if (e.Conjugations.Count >= 12) break;
				var f = str(c?["form"]);
				if (f.Length == 0) f = str(c);
				var pron = str(c?["pron"]);
				var zh = str(c?["zh"]);
				if (f.Length == 0) continue;
				var line = f;
				if (pron.Length > 0) line += "　" + pron;
				if (zh.Length > 0) line += "　" + zh;
				e.Conjugations.Add(line);
			}
		}
		if (n["senses"] is JsonArray sa) {
			foreach (var sn in sa) addsense(e, sn, e.Pos);
		}
		if (n["posGroups"] is JsonArray pg) {
			foreach (var g in pg) addposgroup(e, g);
		}
		if (n["idioms"] is JsonArray ia) {
			foreach (var id in ia) addidiom(e, id);
		}
	}

	static void addposgroup(DictEntry e, JsonNode n) {
		if (n is not JsonObject o) return;
		var pos = str(o["pos"]);
		if (e.Pos.Length == 0 && pos.Length > 0) e.Pos = pos;
		if (o["blocks"] is not JsonArray blocks) return;
		var idiom = "";
		foreach (var b in blocks) addblock(e, b, pos, ref idiom);
	}

	// 长 JSON：{phrase, blocks}。短 JSON：`i` 里的 [词组, [义项块…]]。
	static void addidiom(DictEntry e, JsonNode n) {
		string phrase = "";
		JsonArray blocks = null;
		if (n is JsonObject o) {
			phrase = str(o["phrase"]);
			blocks = o["blocks"] as JsonArray;
		}
		else if (n is JsonArray a && a.Count > 0) {
			phrase = str(a[0]);
			if (a.Count > 1) blocks = a[1] as JsonArray;
		}
		if (blocks == null) blocks = new JsonArray();
		var idiom = phrase ?? "";
		var before = e.Senses.Count;
		foreach (var b in blocks) addblock(e, b, "", ref idiom);
		if (e.Senses.Count == before && idiom.Length > 0) {
			e.Senses.Add(new DictSense { Sub = true, Idiom = idiom });
			return;
		}
		for (var i = before; i < e.Senses.Count; i++) e.Senses[i].Sub = true;
	}

	static void addblock(DictEntry e, JsonNode b, string pos, ref string idiom) {
		if (b is JsonObject o && (o.ContainsKey("main") || o.ContainsKey("subs"))) {
			addlongblock(e, o, pos, ref idiom);
			return;
		}
		if (issenseshape(b)) {
			addsense(e, b, pos, false, ref idiom);
			return;
		}
		if (b is not JsonArray inner) return;
		var sub = false;
		foreach (var x in inner) {
			addsense(e, x, pos, sub, ref idiom);
			sub = true;
		}
	}

	static void addlongblock(DictEntry e, JsonNode n, string pos, ref string idiom) {
		if (n is not JsonObject o) return;
		if (o["main"] != null) addsense(e, o["main"], pos, false, ref idiom);
		if (o["subs"] is JsonArray subs) {
			foreach (var s in subs) addsense(e, s, pos, true, ref idiom);
		}
	}

	static void addgroup(DictEntry e, JsonNode n) {
		if (n is not JsonArray a || a.Count == 0) return;
		if (e.Pos.Length == 0) e.Pos = str(a[0]);
		JsonArray blocks = null;
		if (a.Count >= 3 && a[1] is JsonValue && a[2] is JsonArray a2) blocks = a2;
		else if (a.Count >= 2 && a[1] is JsonArray a1) blocks = a1;
		if (blocks == null) return;
		var pos = str(a[0]);
		var idiom = "";
		foreach (var b in blocks) addblock(e, b, pos, ref idiom);
	}

	static void addsense(DictEntry e, JsonNode n, string pos) {
		var idiom = "";
		addsense(e, n, pos, false, ref idiom);
	}

	static void addsense(DictEntry e, JsonNode n, string pos, bool sub, ref string idiom) {
		if (n == null) return;
		var s = cmpsense(n);
		if (s.Pos.Length == 0) s.Pos = pos ?? "";
		s.Sub = sub;
		if (!string.IsNullOrEmpty(idiom)) {
			s.Idiom = idiom;
			s.Sub = true;
			idiom = "";
		}
		e.Senses.Add(s);
	}

	static bool issenseshape(JsonNode n) {
		if (n is JsonValue) return true;
		if (n is JsonObject o)
			return o.ContainsKey("en") || o.ContainsKey("zh") || o.ContainsKey("ko") || o.ContainsKey("ja");
		if (n is JsonArray a && a.Count > 0) return a[0] is JsonValue || a[0] == null;
		return false;
	}

	static DictSense cmpsense(JsonNode n) {
		var s = new DictSense();
		if (n is JsonValue) {
			spliteq(str(n), out s.En, out s.EnDef);
			return s;
		}
		if (n is JsonObject) {
			fillsenseobj(s, n);
			return s;
		}
		if (n is not JsonArray a) return s;
		spliteq(str(a.Count > 0 ? a[0] : null), out s.Zh, out s.ZhDef);
		spliteq(str(a.Count > 1 ? a[1] : null), out s.En, out s.EnDef);
		fillbilingual(a.Count > 2 ? a[2] : null, s.Phrases);
		fillbilingual(a.Count > 3 ? a[3] : null, s.Sentences);
		s.Ko = str(a.Count > 4 ? a[4] : null);
		spliteq(str(a.Count > 5 ? a[5] : null), out s.Ja, out s.JaDef);
		return s;
	}

	static void fillsenseobj(DictSense s, JsonNode sn) {
		s.Ko = str(sn["ko"]);
		spliteq(eqtext(sn["zh"]), out s.Zh, out s.ZhDef);
		spliteq(eqtext(sn["en"]), out s.En, out s.EnDef);
		spliteq(eqtext(sn["ja"]), out s.Ja, out s.JaDef);
		fillbilingual(sn["phrases"], s.Phrases);
		fillbilingual(sn["sentences"], s.Sentences);
	}

	static string eqtext(JsonNode n) {
		if (n == null) return "";
		if (n is JsonValue) return str(n);
		return join2(str(n["lemma"]), str(n["definition"]));
	}

	static void fillbilingual(JsonNode n, List<DictText> list) {
		if (n is JsonValue) {
			var t = str(n);
			if (t.Length > 0) list.Add(new DictText { Text = t });
			return;
		}
		if (n is not JsonArray a) return;
		foreach (var x in a) {
			var it = bilingual(x);
			if (it.Text.Length > 0) list.Add(it);
		}
	}

	static DictText bilingual(JsonNode n) {
		var t = new DictText();
		if (n is JsonArray a) {
			t.Text = str(a.Count > 0 ? a[0] : null);
			t.Zh = str(a.Count > 1 ? a[1] : null);
		}
		else if (n is JsonObject o) {
			t.Text = str(o["text"]);
			if (t.Text.Length == 0) t.Text = str(o["ko"]);
			if (t.Text.Length == 0) t.Text = eqtext(o["en"]);
			t.Zh = eqtext(o["zh"]);
			if (t.Text.Length == 0) t.Text = str(n);
		}
		else t.Text = str(n);
		return t;
	}

	static void addstrings(JsonNode n, List<string> list, int cap) {
		if (n is JsonValue) {
			var t = str(n);
			if (t.Length > 0) list.Add(t);
			return;
		}
		if (n is not JsonArray a) return;
		foreach (var x in a) {
			if (list.Count >= cap) break;
			var t = str(x);
			if (t.Length > 0) list.Add(t);
		}
	}

	static void spliteq(string s, out string lemma, out string def) {
		lemma = "";
		def = "";
		if (string.IsNullOrEmpty(s)) return;
		var i = s.IndexOf("  ", StringComparison.Ordinal);
		if (i < 0) { lemma = s; return; }
		lemma = s.Substring(0, i).Trim();
		def = s.Substring(i + 2).Trim();
	}

	static string join2(string a, string b) {
		if (string.IsNullOrEmpty(a)) return b ?? "";
		if (string.IsNullOrEmpty(b)) return a;
		return a + "  " + b;
	}

	static string str(JsonNode n) {
		try { return n == null ? "" : (n.GetValue<string>() ?? ""); }
		catch { return ""; }
	}

	static string quotekeys(string s) {
		var sb = new StringBuilder(s.Length + 32);
		var i = 0;
		var n = s.Length;
		while (i < n) {
			var c = s[i];
			if (c == '"') {
				sb.Append(c);
				i++;
				while (i < n) {
					var d = s[i];
					sb.Append(d);
					i++;
					if (d == '\\' && i < n) { sb.Append(s[i]); i++; continue; }
					if (d == '"') break;
				}
				continue;
			}
			if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_') {
				var start = i;
				i++;
				while (i < n) {
					var d = s[i];
					if ((d >= 'a' && d <= 'z') || (d >= 'A' && d <= 'Z') || (d >= '0' && d <= '9') || d == '_') i++;
					else break;
				}
				var j = i;
				while (j < n && (s[j] == ' ' || s[j] == '\t' || s[j] == '\n' || s[j] == '\r')) j++;
				if (j < n && s[j] == ':') {
					sb.Append('"');
					sb.Append(s, start, i - start);
					sb.Append('"');
					continue;
				}
				sb.Append(s, start, i - start);
				continue;
			}
			sb.Append(c);
			i++;
		}
		return sb.ToString();
	}

	static string normalize(string s) {
		var t = s.Trim().Trim('\u3000');
		var sb = new StringBuilder(t.Length);
		foreach (var c in t) {
			if (c >= '\uFF10' && c <= '\uFF19') sb.Append((char)(c - 0xFF10 + '0'));
			else if (c >= '\uFF21' && c <= '\uFF3A') sb.Append((char)(c - 0xFF21 + 'A'));
			else if (c >= '\uFF41' && c <= '\uFF5A') sb.Append((char)(c - 0xFF41 + 'a'));
			else sb.Append(c);
		}
		return sb.ToString().Replace('？', '?').ToLowerInvariant();
	}

	static string escape(string s) {
		var sb = new StringBuilder(s.Length);
		foreach (var c in s) {
			if (c == '\\' || c == '%' || c == '_') sb.Append('\\');
			sb.Append(c);
		}
		return sb.ToString();
	}

	static int countchars(string s) {
		var n = 0;
		foreach (var _ in s) n++;
		return n;
	}

	static void pragma(SqliteConnection c, string sql) {
		try {
			using var cmd = c.CreateCommand();
			cmd.CommandText = sql;
			cmd.ExecuteNonQuery();
		}
		catch { }
	}
}
#endregion
