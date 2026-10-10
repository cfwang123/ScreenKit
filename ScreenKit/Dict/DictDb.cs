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
// dict2.db：entry 仍是词条正文。检索在 idx 里：每个词只存一份，倒排是整数。
// 前缀按词序；词典过滤走 scope；后缀按反转序；三字以上的中缀走 grams。
// rank：0 全等 / 1 前缀 / 2 后缀 / 3 中缀。韩语活用形命中很少时再按语尾剥一层。
#region
static class DictDb {
	const int FETCHCAP = 4000;
	/// <summary>界面一次最多列出的条数。</summary>
	public const int SEARCH_MAX = 4000;

	static readonly string[] DICTNAME = { "en", "ja", "ko", "zh" };

	/// <summary>空闲分钟数。未设置时用 1。0 表示不关闭。</summary>
	public static Func<int> HoldMinutes;

	static SqliteConnection conn;
	static readonly object dblock = new();
	static Timer idletimer;
	static bool batteries;
	static string dberr = "";
	static string dbpath = "";
	static int lastuse;
	static string loaded;
	static long loadedlen;
	static long loadedwrite;
	static string[] keys;
	static int[] rev;
	static int[] postoff;
	static int[] postentry;
	static byte[] postmeta;
	static int[][] scope;
	static Dictionary<string, byte[]> grams;

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
		var fi = new FileInfo(path);
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
				cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('entry','idx','idx_info')";
				if (Convert.ToInt64(cmd.ExecuteScalar()) < 3) {
					c.Dispose();
					dberr = "dict2.db schema";
					return false;
				}
			}
			if (loaded != path || keys == null || loadedlen != fi.Length || loadedwrite != fi.LastWriteTimeUtc.Ticks) {
				if (!loadindex(c)) {
					c.Dispose();
					clearindex();
					dberr = "dict2.db schema";
					return false;
				}
				loaded = path;
				loadedlen = fi.Length;
				loadedwrite = fi.LastWriteTimeUtc.Ticks;
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
				if (conn == null || !OnnxIdle.Due(lastuse, holdms())) return;
				closeunlocked();
				closed = true;
			}
		}
		catch { return; }
		if (!closed) return;
		try { CaptureLog.Info("dict db idle close"); } catch { }
	}

	static int holdms() {
		var min = 1;
		try {
			if (HoldMinutes != null) min = HoldMinutes();
		}
		catch { min = 1; }
		return OnnxIdle.LimitMs(min);
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
		if (limit < 1) limit = 1;
		if (limit > SEARCH_MAX) limit = SEARCH_MAX;
		var hits = match(rawq, dict, limit, FETCHCAP);
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

	/// <summary>同一条查询的全部词条 id，不受界面条数和内部 4000 条上限约束。顺序与 Search 相同。</summary>
	public static List<long> SearchIds(string rawq, string dict) {
		var hits = match(rawq, dict, int.MaxValue, int.MaxValue);
		var ids = new List<long>();
		var seen = new HashSet<long>();
		foreach (var h in hits) {
			if (h.Id == 0 || !seen.Add(h.Id)) continue;
			ids.Add(h.Id);
		}
		return ids;
	}

	static List<DictHit> match(string rawq, string dict, int limit, int cap) {
		var hits = new List<DictHit>();
		if (string.IsNullOrWhiteSpace(rawq)) return hits;
		var q = normalize(rawq);
		if (q.Length == 0) return hits;
		var d = normdict(dict);
		lock (dblock) {
			if (!readyunlocked()) return hits;
			prefixkeys(q, d, hits, cap);
			var prefixN = uniquecount(hits);
			var wantContains = shouldruncontains(q, prefixN, limit);
			var deferContains = wantContains && KoStem.HasHangul(q) && prefixN < 4;
			if (wantContains && !deferContains) containskeys(q, d, hits, cap);
			if (wantstem(q, d, uniquecount(hits))) stemkeys(q, d, hits, limit);
			if (deferContains && uniquecount(hits) < 4) containskeys(q, d, hits, cap);
		}
		sort(hits);
		return hits;
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

	static void prefixkeys(string q, string dict, List<DictHit> hits, int cap) {
		if (keys == null || q.Length == 0) return;
		var lo = lowerkey(q);
		var up = prefixhi(q);
		var hi = up == null ? keys.Length : lowerkey(up);
		var code = dictcode(dict);
		if (code < 0) {
			for (var i = lo; i < hi && hits.Count < cap; i++)
				addposts(i, -1, hits, q, "", -1, cap, false);
			return;
		}
		var sc = scope[code];
		var a = lowerint(sc, lo);
		var b = lowerint(sc, hi);
		for (var i = a; i < b && hits.Count < cap; i++)
			addposts(sc[i], code, hits, q, "", -1, cap, false);
	}

	static void exactkeys(string q, string dict, List<DictHit> hits, string via, int viarank) {
		if (keys == null || q.Length == 0) return;
		var i = lowerkey(q);
		if (i >= keys.Length || keys[i] != q) return;
		var tmp = new List<DictHit>();
		addposts(i, dictcode(dict), tmp, q, via, viarank, int.MaxValue, false);
		tmp.Sort((a, b) => {
			var c = b.IsHead.CompareTo(a.IsHead);
			if (c != 0) return c;
			return a.Id.CompareTo(b.Id);
		});
		var n = tmp.Count < 40 ? tmp.Count : 40;
		for (var k = 0; k < n; k++) hits.Add(tmp[k]);
	}

	static void containskeys(string q, string dict, List<DictHit> hits, int cap) {
		if (keys == null || q.Length == 0) return;
		var code = dictcode(dict);
		if (countchars(q) >= 3) {
			suffixkeys(q, code, hits, cap);
			infixkeys(q, code, hits, cap);
			return;
		}
		for (var i = 0; i < keys.Length && hits.Count < cap; i++) {
			var key = keys[i];
			if (key.IndexOf(q, StringComparison.Ordinal) < 0) continue;
			if (key.StartsWith(q, StringComparison.Ordinal)) continue;
			addposts(i, code, hits, q, "", -1, cap, false);
		}
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
				prefixkeys(cand, d, extra, FETCHCAP);
			foreach (var h in extra) {
				if (!seen.Add(h.Id)) continue;
				if (h.Via.Length == 0) h.Via = q;
				if (h.ViaRank < 0) h.ViaRank = vi;
				hits.Add(h);
			}
			vi++;
			if (vi >= 8) break;
			if (limit > 0 && limit <= int.MaxValue / 2 && hits.Count >= limit * 2) break;
		}
	}

	static void clearindex() {
		loaded = null;
		loadedlen = 0;
		loadedwrite = 0;
		keys = null;
		rev = null;
		postoff = null;
		postentry = null;
		postmeta = null;
		scope = null;
		grams = null;
	}

	static bool loadindex(SqliteConnection c) {
		int nkeys, nposts, ngrams;
		var blobs = new Dictionary<string, byte[]>();
		using (var cmd = c.CreateCommand()) {
			cmd.CommandText = "SELECT n_keys, n_posts, n_grams FROM idx_info";
			using var r = cmd.ExecuteReader();
			if (!r.Read()) return false;
			nkeys = Convert.ToInt32(r.GetValue(0));
			nposts = Convert.ToInt32(r.GetValue(1));
			ngrams = Convert.ToInt32(r.GetValue(2));
		}
		if (nkeys < 1 || nposts < 1 || ngrams < 1) return false;
		using (var cmd = c.CreateCommand()) {
			cmd.CommandText = "SELECT name, data FROM idx";
			using var r = cmd.ExecuteReader();
			while (r.Read()) {
				if (r.IsDBNull(0) || r.IsDBNull(1)) return false;
				var raw = r.GetValue(1) as byte[];
				if (raw == null) return false;
				blobs[r.GetString(0)] = raw;
			}
		}
		if (!blobs.TryGetValue("key_off", out var keyoffb)) return false;
		if (!blobs.TryGetValue("keys", out var keysb)) return false;
		var off = tou32(keyoffb);
		var ks = splitkeys(keysb, off, nkeys);
		var rv = tou32(blobs.TryGetValue("rev", out var revb) ? revb : null);
		var po = tou32(blobs.TryGetValue("post_off", out var pob) ? pob : null);
		var pe = tou32(blobs.TryGetValue("post_entry", out var peb) ? peb : null);
		var pm = blobs.TryGetValue("post_meta", out var pmb) ? pmb : null;
		if (ks == null || rv == null || rv.Length != nkeys) return false;
		if (po == null || po.Length != nkeys + 1 || po[nkeys] != nposts) return false;
		if (pe == null || pe.Length != nposts || pm == null || pm.Length != nposts) return false;
		var sc = new int[4][];
		for (var i = 0; i < 4; i++) {
			if (!blobs.TryGetValue("scope" + i, out var sb)) return false;
			sc[i] = tou32(sb);
			if (sc[i] == null) return false;
		}
		if (!blobs.TryGetValue("grams", out var gb)) return false;
		var gd = readgrams(gb, ngrams);
		if (gd == null) return false;
		keys = ks;
		rev = rv;
		postoff = po;
		postentry = pe;
		postmeta = pm;
		scope = sc;
		grams = gd;
		return true;
	}

	static int[] tou32(byte[] b) {
		if (b == null || (b.Length & 3) != 0) return null;
		var a = new int[b.Length >> 2];
		Buffer.BlockCopy(b, 0, a, 0, b.Length);
		return a;
	}

	static string[] splitkeys(byte[] raw, int[] off, int n) {
		if (raw == null || off == null || off.Length != n + 1) return null;
		var ks = new string[n];
		for (var i = 0; i < n; i++) {
			var a = off[i];
			var b = off[i + 1];
			if (a < 0 || b < a || b > raw.Length) return null;
			ks[i] = Encoding.UTF8.GetString(raw, a, b - a);
		}
		return ks;
	}

	// grams：uint32 个数，然后每条 uint16 长度、utf-8、uint32 倒排长度、差值 7bit varint。
	static Dictionary<string, byte[]> readgrams(byte[] b, int expect) {
		if (b == null || b.Length < 4) return null;
		var n = BitConverter.ToInt32(b, 0);
		if (n != expect) return null;
		var d = new Dictionary<string, byte[]>(n);
		var p = 4;
		for (var i = 0; i < n; i++) {
			if (p + 2 > b.Length) return null;
			var glen = b[p] | (b[p + 1] << 8);
			p += 2;
			if (p + glen + 4 > b.Length) return null;
			var g = Encoding.UTF8.GetString(b, p, glen);
			p += glen;
			var blen = BitConverter.ToInt32(b, p);
			p += 4;
			if (blen < 0 || p + blen > b.Length) return null;
			var blob = new byte[blen];
			Buffer.BlockCopy(b, p, blob, 0, blen);
			p += blen;
			d[g] = blob;
		}
		if (p != b.Length) return null;
		return d;
	}

	static void addposts(int lex, int code, List<DictHit> hits, string q, string via, int viarank, int limit, bool qualify) {
		var key = keys[lex];
		var a = postoff[lex];
		var b = postoff[lex + 1];
		if (a < 0 || b < a || b > postentry.Length) return;
		var han = qualify && hanonly(key);
		for (var j = a; j < b && hits.Count < limit; j++) {
			var meta = postmeta[j];
			var d = meta & 3;
			if (code >= 0 && d != code) continue;
			var head = (meta & 4) != 0;
			if (qualify && !head && !(d == 1 && han)) continue;
			if (d < 0 || d >= DICTNAME.Length) continue;
			hits.Add(new DictHit {
				Id = (long)(uint)postentry[j],
				Matched = key,
				Dict = DICTNAME[d],
				Rank = rankof(q, key),
				Via = via ?? "",
				ViaRank = viarank,
				IsHead = head,
			});
		}
	}

	static int dictcode(string dict) {
		if (dict == "en") return 0;
		if (dict == "ja") return 1;
		if (dict == "ko") return 2;
		if (dict == "zh") return 3;
		return -1;
	}

	// 与 SQLite 的二进制序一致：按码位，不按 UTF-16 码元。
	static int cmpkey(string a, string b) {
		var ia = 0;
		var ib = 0;
		while (ia < a.Length && ib < b.Length) {
			var ca = nextcp(a, ref ia);
			var cb = nextcp(b, ref ib);
			if (ca != cb) return ca < cb ? -1 : 1;
		}
		if (ia < a.Length) return 1;
		if (ib < b.Length) return -1;
		return 0;
	}

	static int nextcp(string s, ref int i) {
		var c = s[i++];
		if (c < '\uD800' || c > '\uDBFF' || i >= s.Length) return c;
		var d = s[i];
		if (d < '\uDC00' || d > '\uDFFF') return c;
		i++;
		return 0x10000 + ((c - 0xD800) << 10) + (d - 0xDC00);
	}

	static int prevcp(string s, ref int i) {
		var c = s[--i];
		if (c < '\uDC00' || c > '\uDFFF' || i <= 0) return c;
		var d = s[i - 1];
		if (d < '\uD800' || d > '\uDBFF') return c;
		i--;
		return 0x10000 + ((d - 0xD800) << 10) + (c - 0xDC00);
	}

	static int lowerkey(string q) {
		var lo = 0;
		var hi = keys.Length;
		while (lo < hi) {
			var mid = lo + ((hi - lo) >> 1);
			if (cmpkey(keys[mid], q) < 0) lo = mid + 1;
			else hi = mid;
		}
		return lo;
	}

	static int lowerint(int[] a, int x) {
		var lo = 0;
		var hi = a.Length;
		while (lo < hi) {
			var mid = lo + ((hi - lo) >> 1);
			if (a[mid] < x) lo = mid + 1;
			else hi = mid;
		}
		return lo;
	}

	static string prefixhi(string q) {
		var cps = new List<int>();
		var i = 0;
		while (i < q.Length) cps.Add(nextcp(q, ref i));
		while (cps.Count > 0 && cps[cps.Count - 1] == 0x10FFFF) cps.RemoveAt(cps.Count - 1);
		if (cps.Count == 0) return null;
		cps[cps.Count - 1]++;
		var sb = new StringBuilder();
		foreach (var cp in cps) sb.Append(char.ConvertFromUtf32(cp));
		return sb.ToString();
	}

	static string reversecps(string s) {
		var cps = new List<int>();
		var i = 0;
		while (i < s.Length) cps.Add(nextcp(s, ref i));
		cps.Reverse();
		var sb = new StringBuilder();
		foreach (var cp in cps) sb.Append(char.ConvertFromUtf32(cp));
		return sb.ToString();
	}

	static int cmprev(string key, string rq) {
		var ik = key.Length;
		var iq = 0;
		while (ik > 0 && iq < rq.Length) {
			var ck = prevcp(key, ref ik);
			var cq = nextcp(rq, ref iq);
			if (ck != cq) return ck < cq ? -1 : 1;
		}
		var restk = 0;
		while (ik > 0) { prevcp(key, ref ik); restk++; }
		var restq = 0;
		while (iq < rq.Length) { nextcp(rq, ref iq); restq++; }
		return restk - restq;
	}

	static int lowerrev(string rq) {
		var lo = 0;
		var hi = rev.Length;
		while (lo < hi) {
			var mid = lo + ((hi - lo) >> 1);
			if (cmprev(keys[rev[mid]], rq) < 0) lo = mid + 1;
			else hi = mid;
		}
		return lo;
	}

	static void suffixkeys(string q, int code, List<DictHit> hits, int cap) {
		var rq = reversecps(q);
		var up = prefixhi(rq);
		var lo = lowerrev(rq);
		var hi = up == null ? rev.Length : lowerrev(up);
		for (var i = lo; i < hi && hits.Count < cap; i++) {
			var lex = rev[i];
			if (lex < 0 || lex >= keys.Length) continue;
			if (keys[lex].StartsWith(q, StringComparison.Ordinal)) continue;
			addposts(lex, code, hits, q, "", -1, cap, false);
		}
	}

	static void infixkeys(string q, int code, List<DictHit> hits, int cap) {
		if (grams == null || q.Length < 3) return;
		HashSet<int> ids = null;
		var seen = new HashSet<string>();
		for (var i = 0; i + 3 <= q.Length; i++) {
			var g = q.Substring(i, 3);
			if (!seen.Add(g)) continue;
			if (!grams.TryGetValue(g, out var blob)) return;
			var cur = decodeposts(blob);
			if (ids == null) ids = cur;
			else ids.IntersectWith(cur);
			if (ids.Count == 0) return;
		}
		if (ids == null) return;
		var list = new List<int>(ids);
		list.Sort();
		foreach (var lex in list) {
			if (hits.Count >= cap) break;
			if (lex < 0 || lex >= keys.Length) continue;
			var key = keys[lex];
			if (key.IndexOf(q, StringComparison.Ordinal) < 0) continue;
			if (key.StartsWith(q, StringComparison.Ordinal)) continue;
			if (key.EndsWith(q, StringComparison.Ordinal)) continue;
			addposts(lex, code, hits, q, "", -1, cap, true);
		}
	}

	static HashSet<int> decodeposts(byte[] b) {
		var set = new HashSet<int>();
		var p = 0;
		var prev = 0;
		while (p < b.Length) {
			var v = 0;
			var shift = 0;
			while (p < b.Length) {
				var by = b[p++];
				v |= (by & 0x7F) << shift;
				if ((by & 0x80) == 0) break;
				shift += 7;
				if (shift > 28) return set;
			}
			prev += v;
			set.Add(prev);
		}
		return set;
	}

	static bool hanonly(string s) {
		if (string.IsNullOrEmpty(s)) return false;
		var i = 0;
		while (i < s.Length) {
			var cp = nextcp(s, ref i);
			if (cp == 0x3007) continue;
			if (cp >= 0x3400 && cp <= 0x4DBF) continue;
			if (cp >= 0x4E00 && cp <= 0x9FFF) continue;
			if (cp >= 0xF900 && cp <= 0xFAFF) continue;
			return false;
		}
		return true;
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
