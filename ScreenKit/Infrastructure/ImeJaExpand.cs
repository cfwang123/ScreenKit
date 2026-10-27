using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace ScreenKit;

/// <summary>把词典里的日语汉字写法，以及动词、一类形容词的活用，写进 ime.db 的 ja_word。
/// 词库原来一条读音只留十几条，有的读音一条都没有。</summary>
static class ImeJaExpand {
	const string Mark = "2";
	static readonly object gate = new object();
	static readonly Dictionary<char, string[]> col = new Dictionary<char, string[]> {
		{ 'う', new[] { "わ", "い", "う", "え", "お", "って", "った" } },
		{ 'く', new[] { "か", "き", "く", "け", "こ", "いて", "いた" } },
		{ 'ぐ', new[] { "が", "ぎ", "ぐ", "げ", "ご", "いで", "いだ" } },
		{ 'す', new[] { "さ", "し", "す", "せ", "そ", "して", "した" } },
		{ 'つ', new[] { "た", "ち", "つ", "て", "と", "って", "った" } },
		{ 'ぬ', new[] { "な", "に", "ぬ", "ね", "の", "んで", "んだ" } },
		{ 'ぶ', new[] { "ば", "び", "ぶ", "べ", "ぼ", "んで", "んだ" } },
		{ 'む', new[] { "ま", "み", "む", "め", "も", "んで", "んだ" } },
		{ 'る', new[] { "ら", "り", "る", "れ", "ろ", "って", "った" } },
	};

	public static int Ensure() {
		var ime = ImeLex.PathOf;
		var dict = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dict3.bin");
		if (!File.Exists(ime) || !File.Exists(dict)) return 0;
		return Fill(ime, dict, null);
	}

	/// <summary>已经写过当前这一版就返回 0。否则扫词典写入，返回新条数。</summary>
	public static int Fill(string ime, string dict, Action<string> log) {
		lock (gate) {
			if (string.IsNullOrEmpty(ime) || string.IsNullOrEmpty(dict)) return 0;
			if (!File.Exists(ime) || !File.Exists(dict)) return 0;
			if (done(ime)) return 0;
			return fill(ime, dict, log);
		}
	}

	public static string Check() {
		string need(string read, string write, int kind, string flag, string sub, bool adj, bool yoi, string rr, string ww) {
			var hit = false;
			emit(read, write, 1, kind, flag, sub, adj, yoi, (r, w, _) => { if (r == rr && w == ww) hit = true; });
			return hit ? null : rr + "/" + ww;
		}
		string ban(string read, string write, int kind, string flag, string sub, bool adj, bool yoi, string rr, string ww) {
			var hit = false;
			emit(read, write, 1, kind, flag, sub, adj, yoi, (r, w, _) => { if (r == rr && w == ww) hit = true; });
			return hit ? "extra " + rr + "/" + ww : null;
		}
		string err;
		err = need("よみがえる", "蘇る", 1, "", "", false, false, "よみがえる", "蘇る"); if (err != null) return err;
		err = need("よみがえる", "甦る", 1, "", "", false, false, "よみがえる", "甦る"); if (err != null) return err;
		err = need("よみがえる", "蘇える", 1, "", "", false, false, "よみがえる", "蘇える"); if (err != null) return err;
		err = need("よみがえる", "甦える", 1, "", "", false, false, "よみがえる", "甦える"); if (err != null) return err;
		err = need("よみがえる", "蘇る", 1, "", "", false, false, "よみがえらない", "蘇らない"); if (err != null) return err;
		err = need("よみがえる", "蘇える", 1, "", "", false, false, "よみがえらない", "蘇えらない"); if (err != null) return err;
		err = need("よみがえる", "蘇る", 1, "", "", false, false, "よみがえって", "蘇って"); if (err != null) return err;
		err = need("よみがえる", "蘇える", 1, "", "", false, false, "よみがえって", "蘇えって"); if (err != null) return err;
		err = need("よみがえる", "蘇る", 1, "", "", false, false, "よみがえった", "蘇った"); if (err != null) return err;
		err = need("たべる", "食べる", 2, "", "", false, false, "たべない", "食べない"); if (err != null) return err;
		err = need("たべる", "食べる", 2, "", "", false, false, "たべろ", "食べろ"); if (err != null) return err;
		err = need("たべる", "食べる", 2, "", "", false, false, "たべよ", "食べよ"); if (err != null) return err;
		err = need("いく", "行く", 1, "iku", "", false, false, "いって", "行って"); if (err != null) return err;
		err = ban("いく", "行く", 1, "iku", "", false, false, "いいて", "行いて"); if (err != null) return err;
		err = need("くる", "来る", 3, "", "kuru", false, false, "こない", "来ない"); if (err != null) return err;
		err = need("くる", "来る", 3, "", "kuru", false, false, "きて", "来て"); if (err != null) return err;
		err = need("べんきょうする", "勉強する", 3, "", "suru", false, false, "べんきょうしない", "勉強しない"); if (err != null) return err;
		err = need("べんきょうする", "勉強する", 3, "", "suru", false, false, "べんきょうできる", "勉強できる"); if (err != null) return err;
		err = need("する", "為る", 3, "", "suru", false, false, "する", "為る"); if (err != null) return err;
		err = need("しんずる", "信ずる", 3, "", "zuru", false, false, "しんじて", "信じて"); if (err != null) return err;
		err = need("しんずる", "信ずる", 3, "", "zuru", false, false, "しんじない", "信じない"); if (err != null) return err;
		err = need("しんずる", "信ずる", 3, "", "zuru", false, false, "しんぜられる", "信ぜられる"); if (err != null) return err;
		err = ban("しんずる", "信ずる", 3, "", "zuru", false, false, "しんずります", "信ずります"); if (err != null) return err;
		err = need("おもんずる", "重んずる", 3, "", "zuru", false, false, "おもんじて", "重んじて"); if (err != null) return err;
		err = need("とう", "問う", 1, "ute", "", false, false, "とうて", "問うて"); if (err != null) return err;
		err = ban("とう", "問う", 1, "ute", "", false, false, "とって", "問って"); if (err != null) return err;
		err = need("ある", "有る", 1, "arunai", "", false, false, "ある", "有る"); if (err != null) return err;
		err = ban("ある", "有る", 1, "arunai", "", false, false, "あらない", "有らない"); if (err != null) return err;
		err = need("くださる", "下さる", 1, "aru", "", false, false, "くださいます", "下さいます"); if (err != null) return err;
		err = ban("くださる", "下さる", 1, "aru", "", false, false, "くださります", "下さります"); if (err != null) return err;
		err = need("いい", "良い", 0, "", "", true, true, "よくない", "良くない"); if (err != null) return err;
		err = need("いい", "良い", 0, "", "", true, true, "よかった", "良かった"); if (err != null) return err;
		err = need("たかい", "高い", 0, "", "", true, false, "たかくない", "高くない"); if (err != null) return err;
		err = need("たかい", "高い", 0, "", "", true, false, "たかかった", "高かった"); if (err != null) return err;
		return null;
	}

	static int fill(string ime, string dict, Action<string> log) {
		var prev = DictDb.HoldMinutes;
		DictDb.HoldMinutes = () => 0;
		var added = 0;
		try {
			if (!DictDb.Init(dict)) throw new InvalidOperationException(DictDb.Error.Length > 0 ? DictDb.Error : "dict");
			using var conn = open(ime);
			exec(conn, "PRAGMA journal_mode=WAL");
			exec(conn, "PRAGMA synchronous=NORMAL");
			exec(conn, "DROP INDEX IF EXISTS ix_ja_reading_rank");
			using var cmd = conn.CreateCommand();
			cmd.CommandText = "INSERT OR IGNORE INTO ja_word(reading,text,rank) VALUES($r,$t,$k)";
			var pr = cmd.Parameters.Add("$r", SqliteType.Text);
			var pt = cmd.Parameters.Add("$t", SqliteType.Text);
			var pk = cmd.Parameters.Add("$k", SqliteType.Integer);
			var tx = conn.BeginTransaction();
			cmd.Transaction = tx;
			var pending = 0;
			void put(string reading, string text, int rank) {
				if (reading.Length == 0 || reading.Length > 24 || text.Length == 0 || text.Length > 32) return;
				if (!JaCand.HasKanji(text) || text == reading) return;
				pr.Value = reading;
				pt.Value = text;
				pk.Value = rank;
				added += cmd.ExecuteNonQuery();
				pending++;
				if (pending < 8000) return;
				tx.Commit();
				tx.Dispose();
				tx = conn.BeginTransaction();
				cmd.Transaction = tx;
				pending = 0;
			}
			var reads = new List<string>();
			var writes = new List<string>();
			log?.Invoke("扫描日语词典…");
			var ok = DictDb.Scan(1, (head, json) => {
				if (!fields(head, json, out var word, out var kanji, out var pos)) return;
				reads.Clear();
				writes.Clear();
				pushform(reads, writes, word);
				pushform(reads, writes, head);
				if (!string.IsNullOrEmpty(kanji)) {
					foreach (var raw in kanji.Split(',', '，')) pushform(reads, writes, raw);
				}
				if (reads.Count == 0 || writes.Count == 0) return;
				kindof(pos, out var kind, out var flag, out var sub, out var adj, out var yoi);
				if (hira(word).EndsWith("いく") || hira(word).EndsWith("ゆく")) {
					if (flag.Length == 0) flag = "iku";
				}
				foreach (var w in writes) {
					foreach (var r in reads) emit(r, w, reads.Count, kind, flag, sub, adj, yoi, put);
				}
			}, (at, all) => {
				if (at == 0 || at == all || at % 800 == 0) log?.Invoke($"日语词条 {at}/{all}");
			});
			tx.Commit();
			if (!ok) throw new InvalidOperationException("scan");
			log?.Invoke("建立读音索引…");
			exec(conn, "CREATE INDEX IF NOT EXISTS ix_ja_reading_rank ON ja_word(reading, rank, text)");
			if (added > 0) {
				exec(conn, "CREATE TABLE IF NOT EXISTS ime_meta(k TEXT PRIMARY KEY, v TEXT)");
				exec(conn, "INSERT INTO ime_meta(k,v) VALUES('ja_verb','" + Mark + "') ON CONFLICT(k) DO UPDATE SET v='" + Mark + "'");
			}
			log?.Invoke("写入 " + added);
		}
		finally {
			DictDb.HoldMinutes = prev;
		}
		return added;
	}

	static void emit(string read, string write, int nread, int kind, string flag, string sub, bool adj, bool yoi, Action<string, string, int> put) {
		if (!pairable(read, write, nread)) return;
		var rk = key(read);
		if (rk.Length > 0 && JaCand.HasKanji(write)) put(rk, write, 40);
		if (adj) {
			zip(iadj(read, yoi), iadj(write, yoi), put);
			return;
		}
		if (kind == 0) return;
		zip(conj(read, kind, flag, sub), conj(write, kind, flag, sub), put);
	}

	static void zip(List<string> rs, List<string> ws, Action<string, string, int> put) {
		if (rs == null || ws == null || rs.Count != ws.Count) return;
		for (var i = 0; i < rs.Count; i++) {
			var k = key(rs[i]);
			if (k.Length == 0 || !JaCand.HasKanji(ws[i])) continue;
			put(k, ws[i], 80);
		}
	}

	/// <summary>这份 ime.db 已是当前词库版本。</summary>
	public static bool IsCurrent(string ime) => done(ime);

	static bool done(string ime) {
		try {
			using var conn = open(ime);
			using var cmd = conn.CreateCommand();
			cmd.CommandText = "SELECT v FROM ime_meta WHERE k='ja_verb'";
			var o = cmd.ExecuteScalar();
			return o != null && o.ToString() == Mark;
		}
		catch { return false; }
	}

	static SqliteConnection open(string ime) {
		var csb = new SqliteConnectionStringBuilder {
			DataSource = ime,
			Mode = SqliteOpenMode.ReadWrite,
			Cache = SqliteCacheMode.Shared,
			Pooling = false,
		};
		try { SQLitePCL.Batteries_V2.Init(); } catch { }
		var c = new SqliteConnection(csb.ToString());
		c.Open();
		using var cmd = c.CreateCommand();
		cmd.CommandText = "PRAGMA busy_timeout=8000";
		cmd.ExecuteNonQuery();
		return c;
	}

	static void exec(SqliteConnection conn, string sql) {
		using var cmd = conn.CreateCommand();
		cmd.CommandText = sql;
		cmd.ExecuteNonQuery();
	}

	static bool fields(string head, string json, out string word, out string kanji, out string pos) {
		DictDb.HeadFields(head, json, out word, out kanji, out pos);
		return word.Length > 0 || kanji.Length > 0;
	}

	static void kindof(string pos, out int kind, out string flag, out string sub, out bool adj, out bool yoi) {
		kind = 0;
		flag = "";
		sub = "";
		adj = false;
		yoi = false;
		var s = (pos ?? "").ToLowerInvariant();
		if (s.Length == 0) return;
		if ((s.Contains("keiyoushi") || s.Contains("i-adjective")) && !s.Contains("keiyodoshi")) adj = true;
		if (s.Contains("yoi/ii")) yoi = true;
		if (s.Contains("iku/yuku")) flag = "iku";
		else if (s.Contains("'u'") && s.Contains("ending (special")) flag = "ute";
		else if (s.Contains("-aru special")) flag = "aru";
		else if (s.Contains("irregular verb")) flag = "arunai";
		if (s.Contains("ichidan verb") || s.Contains("ichidan")) kind = 2;
		else if (s.Contains("kuru verb")) { kind = 3; sub = "kuru"; }
		else if (s.Contains("zuru verb")) { kind = 3; sub = "zuru"; }
		else if (s.Contains("suru verb") && !s.Contains("takes the aux")) { kind = 3; sub = "suru"; }
		else if (s.Contains("godan")) kind = 1;
	}

	static void pushform(List<string> reads, List<string> writes, string raw) {
		var s = clean(raw);
		if (s.Length == 0) return;
		var list = kanaonly(s) ? reads : JaCand.HasKanji(s) ? writes : null;
		if (list == null) return;
		foreach (var old in list) if (old == s) return;
		list.Add(s);
	}

	static string clean(string raw) {
		var s = (raw ?? "").Trim();
		if (s.Length == 0) return "";
		var i = s.IndexOf('\'');
		if (i >= 0) {
			var note = s.Substring(i);
			if (note.IndexOf("out-dated", StringComparison.OrdinalIgnoreCase) >= 0
				|| note.IndexOf("outdated", StringComparison.OrdinalIgnoreCase) >= 0
				|| note.IndexOf("obsolete", StringComparison.OrdinalIgnoreCase) >= 0) return "";
			s = s.Substring(0, i).Trim();
		}
		return s;
	}

	static bool pairable(string read, string write, int nread) {
		if (kuruword(read) != kuruword(write)) return false;
		var h = hira(read);
		if (h.Length == 0) return false;
		var suf = kanasuf(write);
		if (suf.Length == 0) return nread == 1;
		return h.EndsWith(suf);
	}

	static string kanasuf(string s) {
		if (string.IsNullOrEmpty(s)) return "";
		var i = s.Length;
		while (i > 0 && iskana(s[i - 1])) i--;
		return hira(s.Substring(i));
	}

	static string key(string s) {
		var h = hira(s);
		if (h.Length == 0 || h.Length > 24) return "";
		foreach (var c in h) if (!iskana(c)) return "";
		return h;
	}

	static string hira(string s) {
		if (string.IsNullOrEmpty(s)) return "";
		var sb = new System.Text.StringBuilder(s.Length);
		foreach (var c in s) {
			if (c >= 'ァ' && c <= 'ヶ') sb.Append((char)(c - 0x60));
			else sb.Append(c);
		}
		return sb.ToString();
	}

	static bool iskana(char c) {
		if (c >= 'ぁ' && c <= 'ゔ') return true;
		if (c >= 'ァ' && c <= 'ヺ') return true;
		return c == 'ー' || c == '・';
	}

	static bool kanaonly(string s) {
		if (string.IsNullOrEmpty(s) || JaCand.HasKanji(s)) return false;
		foreach (var c in s) if (iskana(c)) return true;
		return false;
	}

	static bool kuruword(string word) {
		var h = hira(word);
		return h == "くる" || word == "来る" || word == "來る";
	}

	static bool suruword(string word) {
		if (word == "為る") return true;
		return hira(word).EndsWith("する");
	}

	// ずる动词：信ずる、命ずる、重んずる。辞书形以 ずる 结尾，活用介于 する 和 じる 之间。
	static bool zuruword(string word) {
		var h = hira(word);
		return h.Length >= 3 && h.EndsWith("ずる");
	}

	static List<string> conj(string word, int kind, string flag, string sub) {
		if (string.IsNullOrEmpty(word)) return null;
		if (kind == 2) return ichidan(word);
		if (kind == 3) {
			if (sub == "kuru") return kuruword(word) ? kuru(word) : null;
			if (sub == "zuru" || zuruword(word)) return zuruword(word) ? zuru(word) : null;
			return suruword(word) ? suru(word) : null;
		}
		if (kind == 1) return godan(word, flag);
		return null;
	}

	static List<string> godan(string word, string flag) {
		if (word.Length == 0) return null;
		var end = word[word.Length - 1];
		if (!col.TryGetValue(end, out var c)) return null;
		var stem = word.Substring(0, word.Length - 1);
		var a = stem + c[0];
		var i = stem + c[1];
		var e = stem + c[3];
		var o = stem + c[4];
		var te = stem + c[5];
		var ta = stem + c[6];
		var cmd = e;
		if (flag == "iku") { te = stem + "って"; ta = stem + "った"; }
		if (flag == "ute") { te = stem + "うて"; ta = stem + "うた"; }
		if (flag == "aru") { i = stem + "い"; cmd = stem + "い"; }
		var nai = flag == "arunai" ? arunai(word) : a + "ない";
		return pack(word, i + "ます", nai, te, ta, e + "ば", new[] { cmd }, o + "う", e + "る", a + "れる", a + "せる", a + "せられる");
	}

	static List<string> ichidan(string word) {
		if (word.Length == 0 || word[word.Length - 1] != 'る') return null;
		var stem = word.Substring(0, word.Length - 1);
		return pack(word, stem + "ます", stem + "ない", stem + "て", stem + "た", stem + "れば",
			new[] { stem + "ろ", stem + "よ" }, stem + "よう", stem + "られる", stem + "られる", stem + "させる", stem + "させられる");
	}

	static List<string> suru(string word) {
		var stem = word == "為る" || hira(word) == "する" ? "" : word.EndsWith("する") ? word.Substring(0, word.Length - 2) : null;
		if (stem == null) return null;
		return pack(stem + "する", stem + "します", stem + "しない", stem + "して", stem + "した", stem + "すれば",
			new[] { stem + "しろ", stem + "せよ" }, stem + "しよう", stem + "できる", stem + "される", stem + "させる", stem + "させられる");
	}

	// ずる动词：未然 ぜ／じ、連用 じ。候选里 じ 系和 ぜ 系都写，信じて／信ぜられた 都能出。
	static List<string> zuru(string word) {
		if (!zuruword(word)) return null;
		var stem = word.Substring(0, word.Length - 2);
		var ji = stem + "じ";
		var ze = stem + "ぜ";
		var list = pack(word, ji + "ます", ji + "ない", ji + "て", ji + "た", stem + "ずれば",
			new[] { ji + "ろ", ze + "よ" }, ji + "よう", ji + "られる", ze + "られる", ze + "させる", ji + "させられる");
		list.Add(ze + "ない");
		list.Add(ze + "なかった");
		list.Add(ji + "られる");
		list.Add(ji + "させる");
		return list;
	}

	static List<string> kuru(string word) {
		var kanji = word == "來る" ? "來" : word == "来る" ? "来" : "";
		string p(string ka, string kn) => kanji.Length > 0 ? kn : ka;
		var k = kanji;
		return pack(p("くる", k + "る"), p("きます", k + "ます"), p("こない", k + "ない"), p("きて", k + "て"), p("きた", k + "た"),
			p("くれば", k + "れば"), new[] { p("こい", k + "い") }, p("こよう", k + "よう"), p("こられる", k + "られる"),
			p("こられる", k + "られる"), p("こさせる", k + "させる"), p("こさせられる", k + "させられる"));
	}

	static string arunai(string word) {
		if (word == "有る" || word == "在る" || hira(word) == "ある") return "ない";
		if (word.Length < 2) return "ない";
		return word.Substring(0, word.Length - 2) + "ない";
	}

	static List<string> iadj(string word, bool yoi) {
		if (string.IsNullOrEmpty(word)) return null;
		if (yoi || hira(word) == "いい" || hira(word) == "よい") return yoiforms(word);
		var h = hira(word);
		if (!h.EndsWith("い") || word[word.Length - 1] != 'い' && !(word.Length > 0 && h[h.Length - 1] == 'い' && word[word.Length - 1] == 'い')) {
			if (word.Length == 0 || word[word.Length - 1] != 'い') return null;
		}
		if (word[word.Length - 1] != 'い') return null;
		var stem = word.Substring(0, word.Length - 1);
		return adjpack(word, stem + "くない", stem + "かった", stem + "くなかった", stem + "くて", stem + "ければ", stem + "かろう", stem + "く");
	}

	static List<string> yoiforms(string word) {
		var h = hira(word);
		string dict, stem;
		if (h == "いい") { dict = "いい"; stem = "よ"; }
		else if (h == "よい") { dict = "よい"; stem = "よ"; }
		else if (word.EndsWith("い")) { dict = word; stem = word.Substring(0, word.Length - 1); }
		else return null;
		return adjpack(dict, stem + "くない", stem + "かった", stem + "くなかった", stem + "くて", stem + "ければ", stem + "かろう", stem + "く");
	}

	static List<string> adjpack(string dict, string kunai, string katta, string kunakatta, string kute, string kereba, string karou, string ku) {
		return new List<string> { dict, kunai, katta, kunakatta, kute, kereba, karou, ku };
	}

	static List<string> pack(string plain, string masu, string nai, string te, string ta, string ba, string[] cmd, string vol, string pot, string pas, string cau, string cap) {
		var list = new List<string> {
			plain, masu, stemof(masu) + "ません", stemof(masu) + "ました", stemof(masu) + "ませんでした",
			nai, stemof(nai) + "なかった", stemof(nai) + "なくて", stemof(nai) + "ないで",
			te, ta, ta + "ら", ba
		};
		list.AddRange(cmd);
		var tai = stemof(masu) + "たい";
		list.Add(plain + "な");
		list.Add(vol);
		list.Add(pot);
		list.Add(pas);
		list.Add(cau);
		list.Add(cap);
		list.Add(tai);
		list.Add(stemof(tai) + "たくない");
		return list;
	}

	static string stemof(string s) {
		if (s.Length < 2) return s;
		return s.Substring(0, s.Length - 2);
	}
}
