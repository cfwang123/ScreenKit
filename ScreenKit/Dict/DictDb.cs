using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

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

// ==================== 词典库（只读二进制） ====================
// dict3.bin 是 RDS1 专用格式，只读，文件固定不动，不写回任何翻译结果。
// 查词时打开文件，内存里只留三级目录的第一级和词条块偏移；闲置关闭后这些也一起释放。
// 检索词块、倒排、gram、词条都按块读盘。
// 前缀段按词序；后缀段按字符反转后的 UTF-8 序；三字以上的中缀走双字 gram 倒排。
// meta 低 2 位是词典号（0 英 / 1 日 / 2 韩 / 3 汉），bit2 是词头。
// rank：0 全等 / 1 前缀 / 2 后缀 / 3 中缀。韩语活用形命中很少时再按语尾剥一层。
#region
static class DictDb {
	const int FETCHCAP = 4000;
	/// <summary>界面一次最多列出的条数。</summary>
	public const int SEARCH_MAX = 4000;

	const int HEADER_SIZE = 256;
	const int CODEC_ZLIB = 1;
	const int DIRCACHE = 32;
	const int KEYCACHE = 64;
	const int ENTRYCACHE = 8;
	const int GRAMCACHE = 2 * 1024 * 1024;
	const int PREVIEW_CAP = 120;

	static readonly string[] DICTNAME = { "en", "ja", "ko", "zh" };

	/// <summary>空闲分钟数。未设置时用 1。0 表示不关闭。</summary>
	public static Func<int> HoldMinutes;

	/// <summary>L1 目录项：首键、块偏移、块长。gram 段另有四个字段。</summary>
	sealed class Sec {
		public byte[] Key;
		public long Off;
		public int Len;
		public long IdOff;
		public int IdLen;
		public long RecOff;
		public int RecLen;
		public int Inner;
		public int BodyLen;
	}

	sealed class KeyBlock {
		public byte[] Raw;
		public int[] KeyAt;
		public int[] KeyLen;
		public int[] Lex;
		public int[] PostAt;
		public int[] NPost;
		public int Count;
		/// <summary>块内所有键拼在一起，KeyAt 是各自起点。</summary>
		public List<byte> keystore;
	}

	static FileStream fs;
	static readonly object dblock = new();
	static Timer idletimer;
	static string dberr = "";
	static string dbpath = "";
	static int lastuse;
	static long flen;

	static int nentries;
	static int nkeys;
	static int entriesperblock;
	static int gramchars;
	static Sec[] prel1;
	static Sec[] sufl1;
	static Sec[] graml1;
	static long[] entoff;
	static int[] entlen;

	static readonly Dictionary<long, KeyBlock> keycache = new();
	static readonly List<long> keyorder = new();
	static readonly Dictionary<long, Sec[]> dircache = new();
	static readonly List<long> dirorder = new();
	static readonly Dictionary<int, byte[]> entcache = new();
	static readonly List<int> entorder = new();
	static readonly Dictionary<long, byte[]> gramcache = new();
	static readonly List<long> gramorder = new();
	static long grambytes;

	public static bool Ready => fs != null;
	/// <summary>missing：库文件不在；其它为打开失败说明。</summary>
	public static string Error => dberr;

	public static bool Init(string path) {
		lock (dblock) return openunlocked(path);
	}

	public static void Close() {
		lock (dblock) closeunlocked();
	}

	/// <summary>关掉文件和内存里的目录。下次查询再打开。</summary>
	public static void Release() {
		lock (dblock) closeunlocked();
	}

	/// <summary>文件还开着，或目录还在内存里。大小是库文件。</summary>
	public static bool TryMem(out string name, out long bytes) {
		lock (dblock) {
			name = string.IsNullOrEmpty(dbpath) ? "dict3.bin" : Path.GetFileName(dbpath);
			bytes = flen;
			return fs != null || prel1 != null;
		}
	}

	public static int MemSelfTest() {
		Release();
		if (TryMem(out _, out _)) return 1;
		return 0;
	}

	/// <summary>已打开则记下使用时间。空闲关掉之后按上次路径再打开。</summary>
	static bool readyunlocked() {
		if (fs != null) {
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
		try {
			var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			if (!loadheader(f)) {
				f.Dispose();
				clearindex();
				dberr = "dict3.bin format";
				return false;
			}
			fs = f;
			dbpath = path;
			dberr = "";
			lastuse = Environment.TickCount;
			if (idletimer == null)
				idletimer = new Timer(onidle, null, OnnxIdle.TickMs, OnnxIdle.TickMs);
			return true;
		}
		catch (Exception ex) {
			fs = null;
			clearindex();
			dberr = ex.Message;
			return false;
		}
	}

	static bool loadheader(FileStream f) {
		var h = readraw(f, 0, HEADER_SIZE);
		if (h == null) return false;
		if (h[0] != (byte)'R' || h[1] != (byte)'D' || h[2] != (byte)'S' || h[3] != (byte)'1') return false;
		if (u16(h, 4) != 1 || u16(h, 56) != CODEC_ZLIB) return false;
		nentries = (int)u32(h, 8);
		nkeys = (int)u32(h, 12);
		entriesperblock = u16(h, 24);
		gramchars = u16(h, 26);
		if (nentries < 1 || nkeys < 1 || entriesperblock < 1 || gramchars < 1) return false;
		prel1 = readl1(f, u64(h, 68), (int)u32(h, 44));
		sufl1 = readl1(f, u64(h, 76), (int)u32(h, 48));
		graml1 = readl1(f, u64(h, 84), (int)u32(h, 52));
		if (prel1 == null || sufl1 == null || graml1 == null) return false;
		if (!readentryindex(f, u64(h, 92))) return false;
		flen = f.Length;
		return true;
	}

	static bool readentryindex(FileStream f, long off) {
		var head = readraw(f, off, 4);
		if (head == null) return false;
		var n = (int)u32(head, 0);
		var cnt = nentries / entriesperblock + (nentries % entriesperblock == 0 ? 0 : 1);
		if (n < 1 || n != cnt) return false;
		var all = readraw(f, off + 4, cnt * 12);
		if (all == null) return false;
		entoff = new long[cnt];
		entlen = new int[cnt];
		for (var i = 0; i < cnt; i++) {
			entoff[i] = u64(all, i * 12);
			entlen[i] = (int)u32(all, i * 12 + 8);
		}
		return true;
	}

	/// <summary>目录整块读进内存再解析：每级只有几百到几千条，读一次就够。</summary>
	static Sec[] readl1(FileStream f, long off, int n) {
		if (n < 0 || n > 8_000_000) return null;
		var head = readraw(f, off, 4);
		if (head == null || (int)u32(head, 0) != n) return null;
		var buf = readraw(f, off + 4, l1size(f, off + 4, n, 12));
		if (buf == null) return null;
		var list = new Sec[n];
		var p = 0;
		for (var i = 0; i < n; i++) {
			if (p + 2 > buf.Length) return null;
			var klen = u16(buf, p);
			p += 2;
			if (p + klen + 12 > buf.Length) return null;
			var key = new byte[klen];
			Buffer.BlockCopy(buf, p, key, 0, klen);
			p += klen;
			list[i] = new Sec {
				Key = key,
				Off = u64(buf, p),
				Len = (int)u32(buf, p + 8),
			};
			p += 12;
		}
		return list;
	}

	/// <summary>先按上限估一个长度整块读，键本身最长不超过 64KB。</summary>
	static int l1size(FileStream f, long off, int n, int extra) {
		var avail = f.Length - off;
		if (avail < 0) avail = 0;
		var guess = (long)n * (2 + 64 + extra) + 64;
		return (int)Math.Min(Math.Min(guess, avail), 64L * 1024 * 1024);
	}

	static void clearindex() {
		prel1 = null;
		sufl1 = null;
		graml1 = null;
		entoff = null;
		entlen = null;
		keycache.Clear();
		keyorder.Clear();
		dircache.Clear();
		dirorder.Clear();
		entcache.Clear();
		entorder.Clear();
		gramcache.Clear();
		gramorder.Clear();
		grambytes = 0;
	}

	static void onidle(object _) {
		var closed = false;
		try {
			lock (dblock) {
				if (fs == null || !OnnxIdle.Due(lastuse, holdms())) return;
				closeunlocked();
				closed = true;
			}
		}
		catch { return; }
		if (!closed) return;
		try { CaptureLog.Info("dict bin idle release"); } catch { }
	}

	static int holdms() {
		var min = 1;
		try {
			if (HoldMinutes != null) min = HoldMinutes();
		}
		catch { min = 1; }
		return OnnxIdle.LimitMs(min);
	}

	/// <summary>关文件和分块目录，下次查询再按上次路径打开。</summary>
	static void closeunlocked() {
		try { fs?.Dispose(); } catch { }
		fs = null;
		lastuse = 0;
		clearindex();
	}

	/// <summary>dict 为空查全部，否则 zh / en / ja / ko。limit 为界面条数上限。</summary>
	public static List<DictHit> Search(string rawq, string dict, int limit) => search(rawq, dict, limit, true);

	/// <summary>只要命中，不读词条正文。列表先列出来，标题和预览再按行取。</summary>
	public static List<DictHit> SearchBrief(string rawq, string dict, int limit) => search(rawq, dict, limit, false);

	static List<DictHit> search(string rawq, string dict, int limit, bool meta) {
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
		if (!meta) return outlist;
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
			affixkeys(q, d, hits, cap, limit);
			var jaon = d.Length == 0 || d == "ja";
			var moon = d.Length == 0 || d == "ko";
			if (jaon) {
				foreach (var hira in DictRomanize.RomajiToHiraganaVariants(q)) {
					if (hira == q) continue;
					affixkeys(hira, "ja", hits, cap, limit);
				}
			}
			if (moon) {
				foreach (var hangul in DictRomanize.RomanToHangulVariants(q)) {
					if (hangul == q) continue;
					affixkeysroman(hangul, "ko", hits, cap);
				}
			}
		}
		sort(hits);
		return hits;
	}

	static void affixkeys(string q, string dict, List<DictHit> hits, int cap, int limit) {
		prefixkeys(q, dict, hits, cap);
		var prefixN = uniquecount(hits);
		var wantContains = shouldruncontains(q, prefixN, limit);
		var deferContains = wantContains && KoStem.HasHangul(q) && prefixN < 4;
		if (wantContains && !deferContains) containskeys(q, dict, hits, cap);
		if (wantstem(q, dict, uniquecount(hits))) stemkeys(q, dict, hits, limit);
		if (deferContains && uniquecount(hits) < 4) containskeys(q, dict, hits, cap);
	}

	/// <summary>韩语罗马字转谚文后的补充检索：只做前缀，避免中缀把 사세고 扩成 사세。</summary>
	static void affixkeysroman(string q, string dict, List<DictHit> hits, int cap) {
		prefixkeys(q, dict, hits, cap);
	}

	public static DictEntry Get(long id) {
		DictEntry e;
		string jsontext;
		lock (dblock) {
			if (!readyunlocked()) return null;
			if (!readentry(id, out var head, out jsontext, out var code)) return null;
			e = new DictEntry {
				Id = id,
				Dict = code >= 0 && code < DICTNAME.Length ? DICTNAME[code] : "",
				Headword = head,
			};
		}
		parsejson(e, jsontext);
		if (e.Word.Length == 0) e.Word = e.Headword;
		if (e.Pron.Length == 0) e.Pron = e.Reading;
		if (e.Reading.Length == 0) e.Reading = e.Pron;
		return e;
	}

	/// <summary>列表按行取标题和预览。只读一条，和完整词条分开。</summary>
	public static bool HitMeta(long id, out string word, out string kanji, out string pron, out string preview) {
		word = "";
		kanji = "";
		pron = "";
		preview = "";
		lock (dblock) {
			if (!readyunlocked()) return false;
			if (!readentry(id, out var head, out var jsontext, out _)) return false;
			var row = new DictEntry { Id = id, Headword = head };
			preview = previewmeta(row, jsontext);
			word = row.Word.Length > 0 ? row.Word : head;
			kanji = row.Kanji;
			pron = row.Pron;
		}
		return true;
	}

	/// <summary>读一条词条：词典号、词头、正文 json。调用方已持锁。</summary>
	static bool readentry(long id, out string head, out string json, out int code) {
		head = "";
		json = "";
		code = 0;
		if (id < 1 || id > nentries) return false;
		var per = entriesperblock;
		var bi = (int)((id - 1) / per);
		var inner = (int)((id - 1) % per);
		var blob = entryblock(bi);
		if (blob == null) return false;
		return parseentryat(blob, inner, out code, out head, out json);
	}

	/// <summary>按 ids 原顺序批量读词典号、词头和正文。按块排序后读，同一块只解压一次。</summary>
	static void readbulk(long[] ids, out int[] codes, out string[] heads, out string[] jsons) {
		var n = ids.Length;
		codes = new int[n];
		heads = new string[n];
		jsons = new string[n];
		var order = new int[n];
		var keys = new long[n];
		var cnt = 0;
		for (var i = 0; i < n; i++) {
			codes[i] = -1;
			heads[i] = "";
			jsons[i] = "";
			var id = ids[i];
			if (id < 1 || id > nentries) continue;
			var bi = (id - 1) / entriesperblock;
			var inner = (id - 1) % entriesperblock;
			order[cnt] = i;
			keys[cnt] = (bi << 16) | inner;
			cnt++;
		}
		Array.Sort(keys, order, 0, cnt);
		var last = -1;
		byte[] blob = null;
		for (var k = 0; k < cnt; k++) {
			var i = order[k];
			var id = ids[i];
			var bi = (int)((id - 1) / entriesperblock);
			var inner = (int)((id - 1) % entriesperblock);
			if (bi != last) {
				blob = entryblock(bi);
				last = bi;
			}
			if (blob == null) continue;
			if (!parseentryat(blob, inner, out var code, out var head, out var json)) continue;
			codes[i] = code;
			heads[i] = head;
			jsons[i] = json;
		}
	}

	static byte[] entryblock(int bi) {
		if (entoff == null || bi < 0 || bi >= entoff.Length) return null;
		if (entcache.TryGetValue(bi, out var hit)) {
			entorder.Remove(bi);
			entorder.Add(bi);
			return hit;
		}
		var raw = readraw(fs, entoff[bi], entlen[bi]);
		if (raw == null) return null;
		var blob = inflate(raw);
		if (blob == null) return null;
		entcache[bi] = blob;
		entorder.Add(bi);
		while (entorder.Count > ENTRYCACHE) {
			var old = entorder[0];
			entorder.RemoveAt(0);
			entcache.Remove(old);
		}
		return blob;
	}

	/// <summary>块内数到第 inner 条。每条是词典号、校对标记、源 id 和四段字符串。</summary>
	static bool parseentryat(byte[] blob, int inner, out int code, out string head, out string json) {
		code = 0;
		head = "";
		json = "";
		var p = 0;
		var n = blob.Length;
		for (var i = 0; i <= inner; i++) {
			if (p + 10 > n) return false;
			var c = blob[p];
			p += 10;
			string h = null, j = null;
			for (var k = 0; k < 4; k++) {
				if (p + 4 > n) return false;
				var len = (int)u32(blob, p);
				p += 4;
				if (len < 0 || p + len > n) return false;
				if (i == inner && k < 2) {
					var s = Encoding.UTF8.GetString(blob, p, len);
					if (k == 0) h = s;
					else j = s;
				}
				p += len;
			}
			if (i != inner) continue;
			code = c;
			head = h ?? "";
			json = j ?? "";
			return true;
		}
		return false;
	}

	// ==================== 检索 ====================

	static void prefixkeys(string q, string dict, List<DictHit> hits, int cap) {
		if (prel1 == null || q.Length == 0) return;
		var lo = encodepoints(q, 0, -1);
		var hi = prefixhi(q);
		var code = dictcode(dict);
		walk(prefixl1(), lo, hi, (key, blk, j) => {
			var rank = samebytes(key, lo) ? 0 : 1;
			push(blk, j, key, rank, code, hits, cap, false);
			return hits.Count < cap;
		}, cap);
	}

	static void exactkeys(string q, string dict, List<DictHit> hits, string via, int viarank) {
		if (prel1 == null || q.Length == 0) return;
		var qb = encodepoints(q, 0, -1);
		var tmp = new List<DictHit>();
		var code = dictcode(dict);
		walk(prefixl1(), qb, null, (key, blk, j) => {
			if (!samebytes(key, qb)) return true;
			push(blk, j, key, 0, code, tmp, int.MaxValue, false);
			return true;
		}, int.MaxValue, exactonly: qb);
		tmp.Sort((a, b) => {
			var c = b.IsHead.CompareTo(a.IsHead);
			if (c != 0) return c;
			return a.Id.CompareTo(b.Id);
		});
		var n = tmp.Count < 40 ? tmp.Count : 40;
		for (var k = 0; k < n; k++) {
			var h = tmp[k];
			h.Via = via ?? "";
			h.ViaRank = viarank;
			hits.Add(h);
		}
	}

	static void containskeys(string q, string dict, List<DictHit> hits, int cap) {
		if (prel1 == null || q.Length == 0) return;
		var code = dictcode(dict);
		var nchars = codepoints(q).Count;
		if (nchars >= 3 && nchars >= gramchars) {
			suffixkeys(q, code, hits, cap);
			infixkeys(q, code, hits, cap);
			return;
		}
		// 单字：只能用前缀段顺序扫一遍，跳过已经当前缀命中的。
		var qb = encodepoints(q, 0, -1);
		walk(prefixl1(), null, null, (key, blk, j) => {
			if (startswith(key, qb)) return true;
			if (indexof(key, qb) < 0) return true;
			push(blk, j, key, endswith(key, qb) ? 2 : 3, code, hits, cap, false);
			return hits.Count < cap;
		}, cap);
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
			if (extra.Count == 0 && codepoints(cand).Count >= 2)
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

	static void suffixkeys(string q, int code, List<DictHit> hits, int cap) {
		if (sufl1 == null) return;
		var rq = revcodepoints(q);
		var qb = encodepoints(q, 0, -1);
		var lo = encodepoints(rq, 0, -1);
		var hi = prefixhi(rq);
		walk(sufl1, lo, hi, (key, blk, j) => {
			var orig = revbytes(key);
			if (startswith(orig, qb)) return true;
			push(blk, j, orig, 2, code, hits, cap, false);
			return hits.Count < cap;
		}, cap);
	}

	static void infixkeys(string q, int code, List<DictHit> hits, int cap) {
		if (graml1 == null || gramchars < 1) return;
		var cps = codepoints(q);
		if (cps.Count < 3 || cps.Count < gramchars) return;
		var grams = new List<byte[]>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		for (var s = 0; s + gramchars <= cps.Count; s++) {
			var g = encodepoints(cps, s, gramchars);
			if (!seen.Add(Convert.ToBase64String(g))) continue;
			grams.Add(g);
		}
		if (grams.Count == 0) return;
		var loaded = new List<Sec>();
		var ids = new List<int[]>();
		foreach (var g in grams) {
			var rec = findgram(g);
			if (rec == null) return;
			var idlist = readids(rec);
			if (idlist.Length == 0) return;
			loaded.Add(rec);
			ids.Add(idlist);
		}
		var order = new int[loaded.Count];
		for (var i = 0; i < order.Length; i++) order[i] = i;
		Array.Sort(order, (a, b) => ids[a].Length.CompareTo(ids[b].Length));
		HashSet<int> other = null;
		for (var k = 1; k < order.Length; k++) {
			var cur = new HashSet<int>(ids[order[k]]);
			if (other == null) other = cur;
			else other.IntersectWith(cur);
			if (other.Count == 0) return;
		}
		var best = order[0];
		var offs = readrecs(loaded[best], out var recb);
		if (offs == null) return;
		var qb = encodepoints(q, 0, -1);
		var bestids = ids[best];
		for (var i = 0; i < bestids.Length; i++) {
			if (hits.Count >= cap) break;
			var lex = bestids[i];
			if (other != null && !other.Contains(lex)) continue;
			if (!recat(offs, recb, i, out var key, out var postAt, out var np)) continue;
			if (indexof(key, qb) < 0) continue;
			if (startswith(key, qb) || endswith(key, qb)) continue;
			pushraw(recb, postAt, np, key, 3, code, hits, cap, true);
		}
	}

	static Sec findgram(byte[] g) {
		return findexact(graml1, g);
	}

	static Sec findexact(Sec[] l1, byte[] key) {
		var g = lowerrec(l1, key);
		if (g < 0) return null;
		var recs = group(l1, g);
		var lo = 0;
		var hi = recs.Length;
		while (lo < hi) {
			var mid = lo + ((hi - lo) >> 1);
			if (cmpbytes(recs[mid].Key, key) < 0) lo = mid + 1;
			else hi = mid;
		}
		if (lo >= recs.Length || !samebytes(recs[lo].Key, key)) return null;
		return recs[lo];
	}

	static int[] readids(Sec rec) {
		if (rec.IdLen < 4) return Array.Empty<int>();
		var raw = readraw(fs, rec.IdOff, rec.IdLen);
		if (raw == null) return Array.Empty<int>();
		var n = rec.IdLen >> 2;
		var ids = new int[n];
		for (var i = 0; i < n; i++) ids[i] = (int)u32(raw, i * 4);
		return ids;
	}

	/// <summary>取 gram 记录块：按块 zlib 解压后裁出本体，再返回偏移表。</summary>
	static int[] readrecs(Sec rec, out byte[] body) {
		body = null;
		if (rec.RecLen <= 0) return null;
		if (!gramcache.TryGetValue(rec.RecOff, out var raw)) {
			var comp = readraw(fs, rec.RecOff, rec.RecLen);
			if (comp == null) return null;
			raw = inflate(comp);
			if (raw == null) return null;
			gramcache[rec.RecOff] = raw;
			gramorder.Add(rec.RecOff);
			grambytes += raw.Length;
			while (grambytes > GRAMCACHE * 2 && gramorder.Count > 1) {
				var old = gramorder[0];
				gramorder.RemoveAt(0);
				if (gramcache.TryGetValue(old, out var gone)) grambytes -= gone.Length;
				gramcache.Remove(old);
			}
		}
		else {
			gramorder.Remove(rec.RecOff);
			gramorder.Add(rec.RecOff);
		}
		if (rec.Inner < 0 || rec.Inner + rec.BodyLen > raw.Length) return null;
		body = raw;
		var n = (int)u32(raw, rec.Inner);
		if (n < 1 || rec.Inner + 4 + n * 4 > raw.Length) return null;
		var baseoff = rec.Inner + 4 + n * 4;
		var offs = new int[n];
		for (var i = 0; i < n; i++) offs[i] = baseoff + (int)u32(raw, rec.Inner + 4 + i * 4);
		return offs;
	}

	/// <summary>按倒排次序取第 lex 条记录：键长、键、倒排条数。</summary>
	static bool recat(int[] offs, byte[] body, int lex, out byte[] key, out int postAt, out int np) {
		key = null;
		postAt = 0;
		np = 0;
		if (lex < 0 || lex >= offs.Length) return false;
		var p = offs[lex];
		if (p + 2 > body.Length) return false;
		var klen = u16(body, p);
		p += 2;
		if (p + klen + 2 > body.Length) return false;
		key = new byte[klen];
		Buffer.BlockCopy(body, p, key, 0, klen);
		p += klen;
		np = u16(body, p);
		postAt = p + 2;
		if (postAt + np * 5 > body.Length) return false;
		return true;
	}

	// ==================== 目录与键块 ====================

	static Sec[] prefixl1() => prel1;

	static Sec[] group(Sec[] l1, int gi) {
		if (l1 == null || gi < 0 || gi >= l1.Length) return Array.Empty<Sec>();
		var key = l1[gi].Off;
		if (dircache.TryGetValue(key, out var hit)) {
			dirorder.Remove(key);
			dirorder.Add(key);
			return hit;
		}
		var raw = readraw(fs, l1[gi].Off, l1[gi].Len);
		if (raw == null) return Array.Empty<Sec>();
		var p = 0;
		if (raw.Length < 2) return Array.Empty<Sec>();
		var n = u16(raw, p);
		p += 2;
		var list = new Sec[n];
		for (var i = 0; i < n; i++) {
			if (p + 2 > raw.Length) return Array.Empty<Sec>();
			var klen = u16(raw, p);
			p += 2;
			if (p + klen + 24 > raw.Length) return Array.Empty<Sec>();
			var k = new byte[klen];
			Buffer.BlockCopy(raw, p, k, 0, klen);
			p += klen;
			// 六个 u32：键段是 块偏移 / 块长 / lex / 0 / 0 / 0，gram 段是 倒排区 / 记录块 / 块内偏移。
			var f0 = u32(raw, p);
			var f1 = u32(raw, p + 4);
			var f2 = u32(raw, p + 8);
			var f3 = u32(raw, p + 12);
			var f4 = u32(raw, p + 16);
			var f5 = u32(raw, p + 20);
			list[i] = new Sec {
				Key = k,
				Off = f0,
				Len = (int)f1,
				IdOff = f0,
				IdLen = (int)f1,
				RecOff = f2,
				RecLen = (int)f3,
				Inner = (int)f4,
				BodyLen = (int)f5,
			};
			p += 24;
		}
		dircache[key] = list;
		dirorder.Add(key);
		while (dirorder.Count > DIRCACHE) {
			var old = dirorder[0];
			dirorder.RemoveAt(0);
			dircache.Remove(old);
		}
		return list;
	}

	/// <summary>组内第一个首键不大于 key 的组；没有则 -1。</summary>
	static int lowerrec(Sec[] l1, byte[] key) {
		if (l1 == null || l1.Length == 0) return -1;
		var lo = 0;
		var hi = l1.Length;
		while (lo < hi) {
			var mid = lo + ((hi - lo) >> 1);
			if (cmpbytes(l1[mid].Key, key) <= 0) lo = mid + 1;
			else hi = mid;
		}
		return lo == 0 ? -1 : lo - 1;
	}

	static KeyBlock keyblock(Sec rec) {
		if (keycache.TryGetValue(rec.Off, out var hit)) {
			keyorder.Remove(rec.Off);
			keyorder.Add(rec.Off);
			return hit;
		}
		var raw = readraw(fs, rec.Off, rec.Len);
		if (raw == null) return null;
		if (raw.Length < 2) return null;
		var n = u16(raw, 0);
		var blk = new KeyBlock {
			Raw = raw,
			Count = n,
			KeyAt = new int[n],
			KeyLen = new int[n],
			Lex = new int[n],
			PostAt = new int[n],
			NPost = new int[n],
		};
		var p = 2;
		var keybuf = new List<byte>(256);
		for (var i = 0; i < n; i++) {
			if (p + 4 > raw.Length) return null;
			var shared = u16(raw, p);
			var nrest = u16(raw, p + 2);
			p += 4;
			if (p + nrest + 6 > raw.Length) return null;
			keybuf.RemoveRange(shared, keybuf.Count - shared);
			for (var k = 0; k < nrest; k++) keybuf.Add(raw[p + k]);
			p += nrest;
			blk.Lex[i] = (int)u32(raw, p);
			var np = u16(raw, p + 4);
			p += 6;
			blk.KeyAt[i] = -1;
			blk.KeyLen[i] = keybuf.Count;
			blk.PostAt[i] = p;
			blk.NPost[i] = np;
			// 键按需从块里重建，这里先存位置：整块键保存在 raw 之外，逐条存一份太占内存
			blk.KeyAt[i] = storekey(blk, i, keybuf);
			if (p + np * 5 > raw.Length) return null;
			p += np * 5;
		}
		keycache[rec.Off] = blk;
		keyorder.Add(rec.Off);
		while (keyorder.Count > KEYCACHE) {
			var old = keyorder[0];
			keyorder.RemoveAt(0);
			keycache.Remove(old);
		}
		return blk;
	}

	/// <summary>键放在块外的键表里，检索时直接按索引取。</summary>
	static int storekey(KeyBlock blk, int i, List<byte> keybuf) {
		var all = blk.keystore ?? (blk.keystore = new List<byte>(4096));
		var at = all.Count;
		all.AddRange(keybuf);
		return at;
	}

	static byte[] keyof(KeyBlock blk, int i) {
		var at = blk.KeyAt[i];
		var len = blk.KeyLen[i];
		var key = new byte[len];
		if (blk.keystore == null || at < 0) return key;
		blk.keystore.CopyTo(at, key, 0, len);
		return key;
	}

	/// <summary>
	/// 按字节序走一段检索词：lo 为空表示从头；hi 为空表示走到末尾。
	/// callback 返回 false 时停止。exactonly 非空时只走到这个键为止。
	/// </summary>
	static void walk(Sec[] l1, byte[] lo, byte[] hi, Func<byte[], KeyBlock, int, bool> fn, int cap,
		byte[] exactonly = null) {
		if (l1 == null || l1.Length == 0 || fs == null) return;
		var start = lo == null ? 0 : Math.Max(0, lowerrec(l1, lo));
		for (var g = start; g < l1.Length; g++) {
			var recs = group(l1, g);
			for (var r = 0; r < recs.Length; r++) {
				var rec = recs[r];
				if (hi != null && cmpbytes(rec.Key, hi) >= 0) return;
				var blk = keyblock(rec);
				if (blk == null) return;
				for (var i = 0; i < blk.Count; i++) {
					var key = keyof(blk, i);
					if (lo != null && cmpbytes(key, lo) < 0) continue;
					if (hi != null && cmpbytes(key, hi) >= 0) return;
					if (exactonly != null && cmpbytes(key, exactonly) > 0) return;
					if (!fn(key, blk, i)) return;
				}
			}
		}
	}

	static void push(KeyBlock blk, int i, byte[] key, int rank, int code, List<DictHit> hits, int cap, bool qualify) {
		pushraw(blk.Raw, blk.PostAt[i], blk.NPost[i], key, rank, code, hits, cap, qualify);
	}

	static void pushraw(byte[] raw, int postAt, int np, byte[] key, int rank, int code,
		List<DictHit> hits, int cap, bool qualify) {
		var han = -1;
		var p = postAt;
		for (var j = 0; j < np; j++) {
			if (hits.Count >= cap) return;
			var e = (int)u32(raw, p);
			var m = raw[p + 4];
			p += 5;
			var d = m & 3;
			if (code >= 0 && d != code) continue;
			if (qualify) {
				var head = (m & 4) != 0;
				if (!head) {
					if (d != 1) continue;
					if (han < 0) han = hanonly(key) ? 1 : 0;
					if (han == 0) continue;
				}
			}
			if (d >= DICTNAME.Length) continue;
			hits.Add(new DictHit {
				Id = (uint)e,
				Dict = DICTNAME[d],
				Matched = Encoding.UTF8.GetString(key),
				Rank = rank,
				IsHead = (m & 4) != 0,
			});
		}
	}

	// ==================== 字节工具 ====================

	static byte[] readraw(FileStream f, long off, int len) {
		if (f == null || off < 0 || len < 0) return null;
		if (off + len > f.Length) return null;
		var buf = new byte[len];
		f.Seek(off, SeekOrigin.Begin);
		var got = 0;
		while (got < len) {
			var n = f.Read(buf, got, len - got);
			if (n <= 0) return null;
			got += n;
		}
		return buf;
	}

	/// <summary>zlib 流：跳过两字节头，正文交给 deflate 解压。</summary>
	static byte[] inflate(byte[] raw) {
		if (raw == null || raw.Length < 3) return null;
		try {
			using var inp = new MemoryStream(raw, 2, raw.Length - 2, false);
			using var def = new DeflateStream(inp, CompressionMode.Decompress);
			using var outm = new MemoryStream();
			def.CopyTo(outm);
			return outm.ToArray();
		}
		catch { return null; }
	}

	static int u16(byte[] b, int i) => b[i] | (b[i + 1] << 8);

	static long u32(byte[] b, int i) => (uint)(b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24));

	static long u64(byte[] b, int i) => u32(b, i) | (u32(b, i + 4) << 32);

	static int cmpbytes(byte[] a, byte[] b) {
		var n = Math.Min(a.Length, b.Length);
		for (var i = 0; i < n; i++) {
			if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;
		}
		return a.Length.CompareTo(b.Length);
	}

	static bool samebytes(byte[] a, byte[] b) => a != null && b != null && a.Length == b.Length && cmpbytes(a, b) == 0;

	static bool startswith(byte[] s, byte[] pre) {
		if (pre.Length == 0) return true;
		if (s.Length < pre.Length) return false;
		for (var i = 0; i < pre.Length; i++)
			if (s[i] != pre[i]) return false;
		return true;
	}

	static bool endswith(byte[] s, byte[] suf) {
		if (suf.Length == 0) return true;
		if (s.Length < suf.Length) return false;
		var off = s.Length - suf.Length;
		for (var i = 0; i < suf.Length; i++)
			if (s[off + i] != suf[i]) return false;
		return true;
	}

	static int indexof(byte[] s, byte[] q) => indexof(s, 0, s.Length, q);

	static int indexof(byte[] s, int start, int end, byte[] q) {
		if (q.Length == 0) return start;
		var last = end - q.Length;
		for (var i = start; i <= last; i++) {
			if (s[i] != q[0]) continue;
			var ok = true;
			for (var j = 1; j < q.Length; j++) {
				if (s[i + j] == q[j]) continue;
				ok = false;
				break;
			}
			if (ok) return i;
		}
		return -1;
	}

	static int nchars_of(byte[] b) {
		var n = 0;
		foreach (var x in b)
			if ((x & 0xC0) != 0x80) n++;
		return n;
	}

	static List<int> codepoints(string s) {
		var list = new List<int>(s.Length);
		var i = 0;
		while (i < s.Length) {
			var c = s[i++];
			if (c >= '\uD800' && c <= '\uDBFF' && i < s.Length) {
				var d = s[i];
				if (d >= '\uDC00' && d <= '\uDFFF') {
					i++;
					list.Add(0x10000 + ((c - 0xD800) << 10) + (d - 0xDC00));
					continue;
				}
			}
			list.Add(c);
		}
		return list;
	}

	/// <summary>取码位并编码成 UTF-8；n 小于 0 表示到末尾。</summary>
	static byte[] encodepoints(string s, int start, int n) => encodepoints(codepoints(s), start, n < 0 ? int.MaxValue : n);

	static byte[] encodepoints(List<int> cps, int start, int n) {
		var sb = new StringBuilder(cps.Count);
		var end = Math.Min(cps.Count, start + n);
		for (var i = start; i < end; i++) sb.Append(char.ConvertFromUtf32(cps[i]));
		return Encoding.UTF8.GetBytes(sb.ToString());
	}

	static string revcodepoints(string s) {
		var cps = codepoints(s);
		cps.Reverse();
		var sb = new StringBuilder(cps.Count);
		foreach (var cp in cps) sb.Append(char.ConvertFromUtf32(cp));
		return sb.ToString();
	}

	/// <summary>反转后的 UTF-8 键还原成原词。</summary>
	static byte[] revbytes(byte[] rev) {
		var cps = new List<int>();
		var i = 0;
		while (i < rev.Length) {
			var c = rev[i];
			int len;
			if (c < 0x80) len = 1;
			else if (c < 0xE0) len = 2;
			else if (c < 0xF0) len = 3;
			else len = 4;
			if (i + len > rev.Length) break;
			var cp = 0;
			if (len == 1) cp = c;
			else if (len == 2) cp = ((c & 0x1F) << 6) | (rev[i + 1] & 0x3F);
			else if (len == 3) cp = ((c & 0x0F) << 12) | ((rev[i + 1] & 0x3F) << 6) | (rev[i + 2] & 0x3F);
			else cp = ((c & 0x07) << 18) | ((rev[i + 1] & 0x3F) << 12) | ((rev[i + 2] & 0x3F) << 6) | (rev[i + 3] & 0x3F);
			cps.Add(cp);
			i += len;
		}
		cps.Reverse();
		var sb = new StringBuilder(cps.Count);
		foreach (var cp in cps) {
			if (cp < 0 || cp > 0x10FFFF) continue;
			sb.Append(char.ConvertFromUtf32(cp));
		}
		return Encoding.UTF8.GetBytes(sb.ToString());
	}

	/// <summary>前缀段的上界：末位码位加一，跳过代理区。</summary>
	static byte[] prefixhi(string q) {
		var cps = codepoints(q);
		while (cps.Count > 0 && cps[cps.Count - 1] == 0x10FFFF) cps.RemoveAt(cps.Count - 1);
		if (cps.Count == 0) return null;
		var n = cps[cps.Count - 1] + 1;
		cps.RemoveAt(cps.Count - 1);
		if (n > 0x10FFFF) return null;
		if (n >= 0xD800 && n < 0xE000) n = 0xE000;
		cps.Add(n);
		return encodepoints(cps, 0, cps.Count);
	}

	static bool hanonly(byte[] key) {
		var i = 0;
		while (i < key.Length) {
			var c = key[i];
			int cp, len;
			if (c < 0x80) { cp = c; len = 1; }
			else if (c < 0xE0 && i + 1 < key.Length) { cp = ((c & 0x1F) << 6) | (key[i + 1] & 0x3F); len = 2; }
			else if (c < 0xF0 && i + 2 < key.Length) {
				cp = ((c & 0x0F) << 12) | ((key[i + 1] & 0x3F) << 6) | (key[i + 2] & 0x3F);
				len = 3;
			}
			else if (i + 3 < key.Length) {
				cp = ((c & 0x07) << 18) | ((key[i + 1] & 0x3F) << 12)
					| ((key[i + 2] & 0x3F) << 6) | (key[i + 3] & 0x3F);
				len = 4;
			}
			else return false;
			i += len;
			if (cp == 0x3007) continue;
			if (cp >= 0x3400 && cp <= 0x4DBF) continue;
			if (cp >= 0x4E00 && cp <= 0x9FFF) continue;
			if (cp >= 0xF900 && cp <= 0xFAFF) continue;
			return false;
		}
		return true;
	}

	// ==================== 结果整理 ====================

	static void fillmeta(List<DictHit> hits) {
		if (hits.Count == 0 || fs == null) return;
		var ids = new long[hits.Count];
		for (var i = 0; i < hits.Count; i++) ids[i] = hits[i].Id;
		readbulk(ids, out var codes, out var heads, out var jsons);
		for (var i = 0; i < hits.Count; i++) {
			if (codes[i] < 0) continue;
			var h = hits[i];
			var row = new DictEntry { Id = h.Id, Headword = heads[i] };
			var preview = previewmeta(row, jsons[i]);
			h.Headword = row.Word.Length > 0 ? row.Word : row.Headword;
			h.Kanji = h.Dict == "ja" ? row.Kanji : "";
			h.Reading = row.Pron;
			h.Pos = row.Pos;
			h.Preview = preview;
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

	/// <summary>列表回填只取词头、读音、词性和一行预览，不建完整词条。</summary>
	static string previewmeta(DictEntry e, string jsontext) {
		if (string.IsNullOrWhiteSpace(jsontext)) return "";
		JsonNode n;
		try { n = JsonNode.Parse(jsontext); }
		catch {
			try { n = JsonNode.Parse(quotekeys(jsontext)); }
			catch { return ""; }
		}
		if (n == null) return "";
		var sb = new StringBuilder();
		if (iscompact(n)) {
			e.Word = str(n["w"]);
			e.Pron = str(n["o"]);
			e.Kanji = str(n["k"]);
			e.Pos = str(n["p"]);
			e.Extra = str(n["x"]);
			if (e.Extra.Length == 0) e.Extra = str(n["r"]);
			if (n["n"] is JsonArray na)
				foreach (var sn in na) previewsense(sn, sb);
			if (n["g"] is JsonArray ga)
				foreach (var g in ga) {
					if (e.Pos.Length == 0 && g is JsonArray a && a.Count > 0) e.Pos = str(a[0]);
					previewgroup(g, sb);
				}
			if (n["i"] is JsonArray ia)
				foreach (var id in ia) previewidiom(id, sb);
		}
		else {
			e.Word = str(n["word"]);
			e.Kanji = str(n["kanji"]);
			e.Pron = str(n["pron"]);
			e.Pos = str(n["pos"]);
			e.Extra = str(n["extra"]);
			if (n["senses"] is JsonArray sa)
				foreach (var sn in sa) previewsense(sn, sb);
			if (n["posGroups"] is JsonArray pg)
				foreach (var g in pg) {
					if (g is not JsonObject o) continue;
					if (e.Pos.Length == 0) e.Pos = str(o["pos"]);
					previewblock(o["blocks"], sb);
				}
			if (n["idioms"] is JsonArray ia)
				foreach (var id in ia) previewidiom(id, sb);
		}
		return sb.Length > 0 ? trunc(sb.ToString(), PREVIEW_CAP) : trunc(e.Extra, 80);
	}

	static void previewsense(JsonNode n, StringBuilder sb) {
		if (n == null || sb.Length >= PREVIEW_CAP) return;
		var g = glossnode(n);
		if (g.Length == 0) return;
		if (sb.Length > 0) sb.Append("; ");
		sb.Append(g);
	}

	static string glossnode(JsonNode n) {
		if (n is JsonValue) return str(n);
		if (n is JsonObject o) {
			return pickgloss(eqtext(o["zh"]), eqtext(o["en"]), str(o["ko"]), eqtext(o["ja"]));
		}
		if (n is JsonArray a) {
			return pickgloss(str(a.Count > 0 ? a[0] : null), str(a.Count > 1 ? a[1] : null),
				str(a.Count > 4 ? a[4] : null), str(a.Count > 5 ? a[5] : null));
		}
		return "";
	}

	static string pickgloss(string zh, string en, string ko, string ja) {
		if (Loc.IsEn && en.Length > 0) return en;
		if (zh.Length > 0) return zh;
		if (en.Length > 0) return en;
		if (ko.Length > 0) return ko;
		return ja;
	}

	static void previewgroup(JsonNode n, StringBuilder sb) {
		if (n is not JsonArray a) return;
		if (a.Count >= 3 && a[1] is JsonValue && a[2] is JsonArray b2) previewblock(b2, sb);
		else if (a.Count >= 2 && a[1] is JsonArray b1) previewblock(b1, sb);
	}

	static void previewblock(JsonNode n, StringBuilder sb) {
		if (n == null || sb.Length >= PREVIEW_CAP) return;
		if (issenseshape(n)) {
			previewsense(n, sb);
			return;
		}
		if (n is JsonArray a) {
			foreach (var x in a) previewblock(x, sb);
			return;
		}
		if (n is JsonObject o) {
			previewblock(o["main"], sb);
			previewblock(o["subs"], sb);
		}
	}

	static void previewidiom(JsonNode n, StringBuilder sb) {
		JsonNode blocks = null;
		if (n is JsonObject o) blocks = o["blocks"];
		else if (n is JsonArray a && a.Count > 1) blocks = a[1];
		previewblock(blocks, sb);
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
			r = countchars(a.Matched).CompareTo(countchars(b.Matched));
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

	static int dictcode(string dict) {
		if (dict == "en") return 0;
		if (dict == "ja") return 1;
		if (dict == "ko") return 2;
		if (dict == "zh") return 3;
		return -1;
	}

	static string normdict(string dict) {
		var d = (dict ?? "").Trim().ToLowerInvariant();
		if (d == "zh" || d == "en" || d == "ja" || d == "ko") return d;
		return "";
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
}
#endregion
