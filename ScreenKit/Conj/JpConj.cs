using System;
using System.Collections.Generic;
using System.Text;

namespace ScreenKit;

/// <summary>labs() 的标签表。字段名和 u-jverb.js 的键一一对应，方便对照。</summary>
sealed class JpConjLabels {
	public string dict, masu, masen, mashita, masend;
	public string nai, nakatta, nakute, naide;
	public string te, ta, tara, ba;
	public string cmd, nasai, na, tamae, stop, vol, pot;
	public string pas, cau, cap, tai, takunai;
	public string c1, c2, c3s, c3k;
	public string iku;
	public string gson, gken, gtei;
	public string son, sonm, sonr, sonrm, kud;
	public string ken, kenm, itas, itasm;
	public string tei, teim;
	public string bad2, bad3, bad1;
	public string teiru;
	public string aux, auxte, auxpol;
	public string ngara, tsutsu, tsutsuaru;
	public string tagaru, tagaranai, yagaru;
	public string sugiru, suginai, yasui, nikui;
	public string souda, souni;
	public string kiru, kireru, au;
	public string kakeru, kakaru, sokonau;
	public string teoku, teshimau, temiru, tearu;
	public string tekuru, teiku, tekudasai;
	public string mashou, mashouka, masenka;
	public string teita, teinai, teimasu, teshimatta, tewa;
	public string nakereba, nakucha, noda, terasshai;
	public string rough, zo, ze, wa, sa, me, kke, mon, tekure, kusaru, koku;
	public string reg, jdegozaru, jdearimasu, jnya, jnanoda, jdesuwa, jnoja, jssu, jssho, jjan, jnari, jdesourou, jojaru;
	public string ai, an, nn, badai;
	public string pol, polneg, polpast, polpastneg;
	public string adv, naru, rentai;
	public string darou, deshou, tarou, dearu;
	public string nda, nanda, tte;
	public string evid, tasouda, rashii, youda;
	public string mitaida, kamoshirenai, hazuda;
	public string kchigai, ktono, ktoka, kktite;
	public string kppoi, kge, kkehai;
	public string duty, wake, wakenai, beki;
	public string bekinai, monoda, kotoda, tsumori, yotei;
	public string degree, gachi, kimi, ippou, bakari;
	public string tokoro1, tokoro2, tokoro3;
	public string confirm, kana, kashira, deshoune, janai;
	public string reason, nodakara, monode, okage;
	public string naihasei, nasa, nasage, nasasugiru;
	public string cop, degozaru, degozaimasu;
	public string degozaimashita, degozaimashou;
	public string deirassharu, deirasshaimasu;
	public string dks, dky, dth, dhi, term;
	public bool zh;
}

/// <summary>evidrows()／patternrows() 用的接续形集合。属性名对应 JS 里 k 对象的字段。</summary>
sealed class JpConjForms {
	public string plain = "", pred = "", drop = "", attrib = "", attribn = "";
	public string ren = "", ta = "", teiru = "", stem = "", neg = "", negta = "";
}

/// <summary>一个词性的变形结果（对应 JS 里 build() 内部返回的对象）。</summary>
sealed class JpConjPacked {
	public bool ok = true;
	public string err = "";
	public string plain = "", masu = "", te = "", ta = "", nai = "", sub = "";
	public List<ConjRow> rows = new();
	/// <summary>要拼在整张表最后的行（N5~N0 句型）。</summary>
	public List<ConjRow> tail;
}

/// <summary>敬语词条（lexOf）与生产型敬语（prodOf）用的小结构。</summary>
sealed class JpConjLex {
	public string son, ken, itas, tei, kud;
	public bool only, onlyson, onlyken;
}

/// <summary>猜出来的词性：kind 对应 1／2／3／ai／an／n。</summary>
sealed class JpConjKind {
	public string Kind = "";
	public string Flag = "";
	public string Sub = "";
}

/// <summary>日语词形变化。移植自 Web/tools/u-jverb.js 的 build() 全链路，纯函数、无共享可变状态。</summary>
static class JpConj {
	/// <summary>五段动词词尾的活用表：a,i,u,e,o,て,た。</summary>
	static readonly Dictionary<string, string[]> COL = new() {
		["う"] = new[] { "わ", "い", "う", "え", "お", "って", "った" },
		["く"] = new[] { "か", "き", "く", "け", "こ", "いて", "いた" },
		["ぐ"] = new[] { "が", "ぎ", "ぐ", "げ", "ご", "いで", "いだ" },
		["す"] = new[] { "さ", "し", "す", "せ", "そ", "して", "した" },
		["つ"] = new[] { "た", "ち", "つ", "て", "と", "って", "った" },
		["ぬ"] = new[] { "な", "に", "ぬ", "ね", "の", "んで", "んだ" },
		["ぶ"] = new[] { "ば", "び", "ぶ", "べ", "ぼ", "んで", "んだ" },
		["む"] = new[] { "ま", "み", "む", "め", "も", "んで", "んだ" },
		["る"] = new[] { "ら", "り", "る", "れ", "ろ", "って", "った" },
	};

	/// <summary>网页下拉里的常见词表：g,辞书形,读音,类,flag,sub,中文,English。</summary>
	static readonly string[][] PICKS = new[] {
		new[] { "sp", "する", "する", "3", "", "suru", "做", "do" },
		new[] { "sp", "来る", "くる", "3", "", "kuru", "来", "come" },
		new[] { "sp", "行く", "いく", "1", "iku", "", "去 · て＝って", "go · te=tte" },
		new[] { "sp", "ある", "ある", "1", "arunai", "", "有 · 否定＝ない", "exist · nai=nai" },
		new[] { "sp", "くれる", "くれる", "2", "kure", "", "给 · 命令＝くれ", "give · cmd=kure" },
		new[] { "sp", "問う", "とう", "1", "ute", "", "问 · て＝うて", "ask · te=ute" },
		new[] { "sp", "請う", "こう", "1", "ute", "", "请求 · て＝うて", "request · te=ute" },
		new[] { "sp", "なさる", "なさる", "1", "aru", "", "做 · ます＝います", "do · masu=imasu" },
		new[] { "sp", "くださる", "くださる", "1", "aru", "", "给 · ます＝います", "give · masu=imasu" },
		new[] { "sp", "いらっしゃる", "いらっしゃる", "1", "aru", "", "在/来/去 · ます＝います", "be/come/go · masu=imasu" },
		new[] { "sp", "おっしゃる", "おっしゃる", "1", "aru", "", "说 · ます＝います", "say · masu=imasu" },
		new[] { "sp", "ござる", "ござる", "1", "aru", "", "有 · ます＝います", "exist · masu=imasu" },
		new[] { "cm", "いる", "いる", "2", "", "", "在", "be" },
		new[] { "cm", "要る", "いる", "1", "", "", "需要", "need" },
		new[] { "cm", "切る", "きる", "1", "", "", "切", "cut" },
		new[] { "cm", "着る", "きる", "2", "", "", "穿", "wear" },
		new[] { "cm", "帰る", "かえる", "1", "", "", "回", "return" },
		new[] { "cm", "変える", "かえる", "2", "", "", "改变", "change" },
		new[] { "cm", "入る", "はいる", "1", "", "", "进", "enter" },
		new[] { "cm", "出る", "でる", "2", "", "", "出", "exit" },
		new[] { "cm", "走る", "はしる", "1", "", "", "跑", "run" },
		new[] { "cm", "知る", "しる", "1", "", "", "知道", "know" },
		new[] { "cm", "作る", "つくる", "1", "", "", "做", "make" },
		new[] { "cm", "取る", "とる", "1", "", "", "取", "take" },
		new[] { "cm", "乗る", "のる", "1", "", "", "乘", "ride" },
		new[] { "cm", "座る", "すわる", "1", "", "", "坐", "sit" },
		new[] { "cm", "終わる", "おわる", "1", "", "", "结束", "end" },
		new[] { "cm", "始まる", "はじまる", "1", "", "", "开始", "begin" },
		new[] { "cm", "始める", "はじめる", "2", "", "", "开始", "begin" },
		new[] { "cm", "売る", "うる", "1", "", "", "卖", "sell" },
		new[] { "cm", "送る", "おくる", "1", "", "", "送", "send" },
		new[] { "cm", "分かる", "わかる", "1", "", "", "懂", "understand" },
		new[] { "cm", "入れる", "いれる", "2", "", "", "放入", "put in" },
		new[] { "cm", "食べる", "たべる", "2", "", "", "吃", "eat" },
		new[] { "cm", "飲む", "のむ", "1", "", "", "喝", "drink" },
		new[] { "cm", "見る", "みる", "2", "", "", "看", "see" },
		new[] { "cm", "聞く", "きく", "1", "", "", "听", "hear" },
		new[] { "cm", "読む", "よむ", "1", "", "", "读", "read" },
		new[] { "cm", "書く", "かく", "1", "", "", "写", "write" },
		new[] { "cm", "話す", "はなす", "1", "", "", "说", "speak" },
		new[] { "cm", "言う", "いう", "1", "", "", "说", "say" },
		new[] { "cm", "買う", "かう", "1", "", "", "买", "buy" },
		new[] { "cm", "待つ", "まつ", "1", "", "", "等", "wait" },
		new[] { "cm", "会う", "あう", "1", "", "", "见", "meet" },
		new[] { "cm", "思う", "おもう", "1", "", "", "想", "think" },
		new[] { "cm", "できる", "できる", "2", "", "", "能", "can" },
		new[] { "cm", "勉強する", "べんきょうする", "3", "", "suru", "学习", "study" },
		new[] { "cm", "起きる", "おきる", "2", "", "", "起床", "get up" },
		new[] { "cm", "寝る", "ねる", "2", "", "", "睡觉", "sleep" },
		new[] { "cm", "教える", "おしえる", "2", "", "", "教", "teach" },
		new[] { "cm", "習う", "ならう", "1", "", "", "学", "learn" },
		new[] { "cm", "立つ", "たつ", "1", "", "", "站", "stand" },
		new[] { "cm", "出す", "だす", "1", "", "", "拿出", "take out" },
		new[] { "cm", "死ぬ", "しぬ", "1", "", "", "死", "die" },
		new[] { "cm", "泳ぐ", "およぐ", "1", "", "", "游", "swim" },
		new[] { "cm", "遊ぶ", "あそぶ", "1", "", "", "玩", "play" },
		new[] { "cm", "急ぐ", "いそぐ", "1", "", "", "急", "hurry" },
		new[] { "cm", "貸す", "かす", "1", "", "", "借出", "lend" },
		new[] { "cm", "借りる", "かりる", "2", "", "", "借入", "borrow" },
		new[] { "cm", "開ける", "あける", "2", "", "", "打开", "open" },
		new[] { "cm", "閉める", "しめる", "2", "", "", "关上", "close" },
		new[] { "cm", "働く", "はたらく", "1", "", "", "工作", "work" },
		new[] { "cm", "休む", "やすむ", "1", "", "", "休息", "rest" },
		new[] { "cm", "住む", "すむ", "1", "", "", "住", "live" },
		new[] { "cm", "呼ぶ", "よぶ", "1", "", "", "叫", "call" },
		new[] { "cm", "使う", "つかう", "1", "", "", "用", "use" },
		new[] { "cm", "見せる", "みせる", "2", "", "", "给看", "show" },
		new[] { "ai", "高い", "たかい", "ai", "", "", "高", "high" },
		new[] { "ai", "安い", "やすい", "ai", "", "", "便宜", "cheap" },
		new[] { "ai", "いい", "いい", "ai", "", "", "好 · 否定＝よくない", "good · nai=yokunai" },
		new[] { "ai", "大きい", "おおきい", "ai", "", "", "大", "big" },
		new[] { "ai", "小さい", "ちいさい", "ai", "", "", "小", "small" },
		new[] { "ai", "新しい", "あたらしい", "ai", "", "", "新", "new" },
		new[] { "ai", "暑い", "あつい", "ai", "", "", "热", "hot" },
		new[] { "ai", "かわいい", "かわいい", "ai", "", "", "可爱 · 规则变化", "cute · regular" },
		new[] { "an", "静か", "しずか", "an", "", "", "安静", "quiet" },
		new[] { "an", "便利", "べんり", "an", "", "", "方便", "convenient" },
		new[] { "an", "きれい", "きれい", "an", "", "", "漂亮 · 不是い形容词", "pretty · not i-adj" },
		new[] { "an", "元気", "げんき", "an", "", "", "精神", "healthy" },
		new[] { "an", "有名", "ゆうめい", "an", "", "", "有名", "famous" },
		new[] { "an", "好き", "すき", "an", "", "", "喜欢", "like" },
		new[] { "nn", "学生", "がくせい", "n", "", "", "学生", "student" },
		new[] { "nn", "先生", "せんせい", "n", "", "", "老师", "teacher" },
		new[] { "nn", "日本人", "にほんじん", "n", "", "", "日本人", "Japanese" },
		new[] { "nn", "本", "ほん", "n", "", "", "书", "book" },
	};

	/// <summary>敬语词条：只(only)、只缺尊敬(onlySon)、只缺谦让(onlyKen)。</summary>
	static readonly Dictionary<string, JpConjLex> KEIGO = new() {
		["する"] = new JpConjLex { son = "なさる", ken = "いたす", tei = "いたす", kud = "なさってください", only = true },
		["くる"] = new JpConjLex { son = "いらっしゃる、おいでになる", ken = "参る、伺う", tei = "参る", kud = "いらしてください、お越しください", only = true },
		["いく"] = new JpConjLex { son = "いらっしゃる、おいでになる", ken = "参る、伺う", tei = "参る", kud = "いらしてください", only = true },
		["いる"] = new JpConjLex { son = "いらっしゃる、おいでになる", ken = "おる", tei = "おる", kud = "いらしてください", only = true },
		["みる"] = new JpConjLex { son = "ご覧になる", ken = "拝見する", kud = "ご覧ください", only = true },
		["いう"] = new JpConjLex { son = "おっしゃる", ken = "申す、申し上げる", tei = "申す", kud = "おっしゃってください", only = true },
		["たべる"] = new JpConjLex { son = "召し上がる", ken = "いただく", tei = "いただく", kud = "召し上がってください", onlyken = true },
		["のむ"] = new JpConjLex { son = "召し上がる", ken = "いただく", tei = "いただく", kud = "召し上がってください", onlyken = true },
		["くれる"] = new JpConjLex { son = "くださる", kud = "ください", only = true },
		["あげる"] = new JpConjLex { ken = "差し上げる", onlyken = true },
		["もらう"] = new JpConjLex { ken = "いただく、頂戴する", tei = "いただく", onlyken = true },
		["しる"] = new JpConjLex { son = "ご存じです", ken = "存じる、存じ上げる", tei = "存じる", kud = "ご存じください" },
		["おもう"] = new JpConjLex { ken = "存じる", tei = "存じる", onlyken = true },
		["きく"] = new JpConjLex { ken = "伺う" },
		["あう"] = new JpConjLex { ken = "お目にかかる", onlyken = true },
		["kiru"] = new JpConjLex { son = "お召しになる", kud = "お召しください", onlyson = true },
		["ねる"] = new JpConjLex { son = "お休みになる", kud = "お休みください", onlyson = true },
		["かりる"] = new JpConjLex { ken = "拝借する", onlyken = true },
		["よむ"] = new JpConjLex { ken = "拝読する", onlyken = true },
		["ある"] = new JpConjLex { tei = "ござる", only = true },
		["みせる"] = new JpConjLex { ken = "お目にかける", onlyken = true },
		["できる"] = new JpConjLex { son = "おできになる", kud = "おできください", only = true },
		["しぬ"] = new JpConjLex { son = "お亡くなりになる", kud = "お亡くなりください", only = true },
	};

	/// <summary>汉字写法的别名，先于假名表查。</summary>
	static readonly Dictionary<string, string> KEYALIAS = new() {
		["為る"] = "する", ["来る"] = "くる", ["來る"] = "くる", ["行く"] = "いく",
		["逝く"] = "いく", ["往く"] = "いく", ["居る"] = "いる", ["見る"] = "みる",
		["観る"] = "みる", ["言う"] = "いう", ["云う"] = "いう", ["食べる"] = "たべる",
		["飲む"] = "のむ", ["呉れる"] = "くれる", ["貰う"] = "もらう", ["知る"] = "しる",
		["思う"] = "おもう", ["聞く"] = "きく", ["訊く"] = "きく", ["会う"] = "あう",
		["着る"] = "kiru", ["寝る"] = "ねる", ["借りる"] = "かりる", ["読む"] = "よむ",
		["有る"] = "ある", ["在る"] = "ある", ["見せる"] = "みせる", ["出来る"] = "できる",
		["死ぬ"] = "しぬ",
	};

	/// <summary>假名写法的词条键。</summary>
	static readonly Dictionary<string, string> KEYKANA = new() {
		["する"] = "する", ["くる"] = "くる", ["いく"] = "いく", ["いる"] = "いる",
		["みる"] = "みる", ["いう"] = "いう", ["たべる"] = "たべる", ["のむ"] = "のむ",
		["くれる"] = "くれる", ["あげる"] = "あげる", ["もらう"] = "もらう", ["しる"] = "しる",
		["おもう"] = "おもう", ["きく"] = "きく", ["あう"] = "あう", ["ねる"] = "ねる",
		["かりる"] = "かりる", ["よむ"] = "よむ", ["ある"] = "ある", ["みせる"] = "みせる",
		["できる"] = "できる", ["しぬ"] = "しぬ",
	};

	/// <summary>入口，对应 JS 的 build(word, kind, flag, sub, lang)。</summary>
	public static ConjResult Build(string word, string kind, string flag, string sub, string uiLang) {
		word = norm(word);
		kind = kind ?? "";
		flag = flag ?? "";
		sub = sub ?? "";
		var L = labs(uiLang);
		JpConjPacked pk;
		if (kind == "ai") pk = iadj(word, L);
		else if (kind == "an") pk = naadj(word, L);
		else if (kind == "n") pk = nounrows(word, L);
		else if (kind == "2") pk = ichidan(word, L);
		else if (kind == "3") pk = surukuru(word, sub, L);
		else if (kind == "1") pk = godan(word, flag, L);
		else return new ConjResult { Ok = false, Err = L.bad1 };
		if (!pk.ok) return new ConjResult { Ok = false, Err = pk.err };
		var name = kind == "2" ? L.c2 : kind == "3" ? (pk.sub == "kuru" ? L.c3k : L.c3s)
			: kind == "1" ? L.c1 : kind == "ai" ? L.ai : kind == "an" ? L.an : L.nn;
		if (flag == "iku" || (isiku(word) && kind == "1")) name += " · " + L.iku;
		var rows = pk.rows;
		if (kind != "ai" && kind != "an" && kind != "n") rows.AddRange(dialectrows(word, kind, pk, L));
		if (pk.tail != null) rows.AddRange(pk.tail);
		return new ConjResult { Ok = true, Head = word, Name = name, Rows = rows };
	}

	/// <summary>按词尾猜词性，对应 JS 的 guess()；猜不出返回 null。</summary>
	public static JpConjKind Guess(string word) {
		word = norm(word);
		if (word.Length == 0) return null;
		var pre = findpre(word);
		if (pre != null) return new JpConjKind { Kind = pre[3], Flag = pre[4], Sub = pre[5] };
		if (iskuru(word)) return new JpConjKind { Kind = "3", Sub = "kuru" };
		if (issuru(word)) return new JpConjKind { Kind = "3", Sub = "suru" };
		if (isaru(word)) return new JpConjKind { Kind = "1", Flag = "arunai" };
		if (issonkearu(word)) return new JpConjKind { Kind = "1", Flag = "aru" };
		if (iskureru(word)) return new JpConjKind { Kind = "2", Flag = "kure" };
		if (isute(word)) return new JpConjKind { Kind = "1", Flag = "ute" };
		var end = last(word);
		if (end == "る") return new JpConjKind { Kind = "2" };
		if (COL.ContainsKey(end)) return new JpConjKind { Kind = "1", Flag = isiku(word) ? "iku" : "" };
		if (end == "い") return new JpConjKind { Kind = "ai" };
		return new JpConjKind { Kind = "n" };
	}

	/// <summary>在示例词表里找词，返回 PICKS 的一行（[1]=辞书形 [2]=读音 [3]=类 [4]=flag [5]=sub）。</summary>
	public static string[] FindPre(string word) {
		word = norm(word);
		if (word.Length == 0) return null;
		var h = hira(word);
		string[] one = null;
		var n = 0;
		foreach (var p in PICKS) {
			if (p[1] == word) return p;
			if (p[2] == word || p[2] == h) { n++; one = p; }
		}
		return n == 1 ? one : null;
	}

	/// <summary>把词典的 pos 文本翻成 kind/flag/sub，对应 JS 的 poskind()。</summary>
	public static JpConjKind PosKind(string pos) {
		var s = (pos ?? "").ToLowerInvariant();
		if (s.Length == 0) return null;
		var flag = "";
		if (s.Contains("iku/yuku")) flag = "iku";
		else if (s.Contains("ending (special") && s.Contains("'u'")) flag = "ute";
		else if (s.Contains("-aru special")) flag = "aru";
		else if (s.Contains("irregular verb")) flag = "arunai";
		var kind = "";
		var sub = "";
		if (s.Contains("keiyodoshi")) kind = "an";
		else if (s.Contains("keiyoushi")) kind = "ai";
		else if (s.Contains("ichidan")) kind = "2";
		else if (s.Contains("kuru verb")) { kind = "3"; sub = "kuru"; }
		else if (s.Contains("suru verb") && !s.Contains("takes the aux")) { kind = "3"; sub = "suru"; }
		else if (s.Contains("godan")) kind = "1";
		else if (s.Contains("noun")) kind = "n";
		if (kind.Length == 0) return null;
		return new JpConjKind { Kind = kind, Flag = flag, Sub = sub };
	}

	// ───────── 标签表 ─────────

	static JpConjLabels labs(string lang) {
		var L = new JpConjLabels();
		if (string.IsNullOrEmpty(lang)) lang = "zh";
		if (lang.StartsWith("zh", StringComparison.Ordinal)) {
			L.dict = "辞書形";
			L.masu = "ます形";
			L.masen = "ません";
			L.mashita = "ました";
			L.masend = "ませんでした";
			L.nai = "ない形";
			L.nakatta = "なかった";
			L.nakute = "なくて";
			L.naide = "ないで";
			L.te = "て形";
			L.ta = "た形";
			L.tara = "たら";
			L.ba = "ば形";
			L.cmd = "命令形";
			L.nasai = "なさい";
			L.na = "な（=なさい）";
			L.tamae = "給え";
			L.stop = "禁止形";
			L.vol = "意向形";
			L.pot = "可能形";
			L.pas = "受身形";
			L.cau = "使役形";
			L.cap = "使役受身";
			L.tai = "たい";
			L.takunai = "たくない";
			L.c1 = "1类（五段）";
			L.c2 = "2类（一段）";
			L.c3s = "3类（する）";
			L.c3k = "3类（くる）";
			L.iku = "いく";
			L.gson = "尊敬语";
			L.gken = "谦让语";
			L.gtei = "丁重语";
			L.son = "になる";
			L.sonm = "になります";
			L.sonr = "れる";
			L.sonrm = "れます";
			L.kud = "ください";
			L.ken = "する";
			L.kenm = "します";
			L.itas = "いたす";
			L.itasm = "いたします";
			L.tei = "辞書";
			L.teim = "ます";
			L.bad2 = "2类要以る结尾";
			L.bad3 = "3类是する或くる";
			L.bad1 = "1类词尾要是う段假名";
			L.teiru = "ている";
			L.aux = "补助动词";
			L.auxte = "て形＋";
			L.auxpol = "劝诱・邀请";
			L.ngara = "ながら";
			L.tsutsu = "つつ";
			L.tsutsuaru = "つつある";
			L.tagaru = "たがる";
			L.tagaranai = "たがらない";
			L.yagaru = "やがる";
			L.sugiru = "すぎる";
			L.suginai = "すぎない";
			L.yasui = "やすい";
			L.nikui = "にくい";
			L.souda = "そうだ";
			L.souni = "そうに・そうな";
			L.kiru = "きる";
			L.kireru = "きれる";
			L.au = "あう";
			L.kakeru = "かける";
			L.kakaru = "かかる";
			L.sokonau = "そこなう";
			L.teoku = "ておく";
			L.teshimau = "てしまう";
			L.temiru = "てみる";
			L.tearu = "てある";
			L.tekuru = "てくる";
			L.teiku = "ていく";
			L.tekudasai = "てください";
			L.mashou = "ましょう";
			L.mashouka = "ましょうか";
			L.masenka = "ませんか";
			L.teita = "ていた";
			L.teinai = "ていない";
			L.teimasu = "ています";
			L.teshimatta = "てしまった";
			L.tewa = "ては";
			L.nakereba = "なければ";
			L.nakucha = "なくては";
			L.noda = "のです";
			L.terasshai = "ていらっしゃい";
			L.rough = "粗俗・男性语";
			L.zo = "ぞ";
			L.ze = "ぜ";
			L.wa = "わ";
			L.sa = "さ";
			L.me = "め（蔑称）";
			L.kke = "っけ";
			L.mon = "もん（理由）";
			L.tekure = "てくれ";
			L.kusaru = "くさる（粗）";
			L.koku = "こく（粗）";
			L.reg = "角色语";
			L.jdegozaru = "でござる（武士）";
			L.jdearimasu = "であります（军队）";
			L.jnya = "にゃ（猫）";
			L.jnanoda = "なのだ（解说）";
			L.jdesuwa = "ですわ（大小姐）";
			L.jnoja = "のじゃ（老人）";
			L.jssu = "っす（体育会）";
			L.jssho = "っしょ（辣妹）";
			L.jjan = "じゃん（口语）";
			L.jnari = "なり（武士）";
			L.jdesourou = "で候（文语）";
			L.jojaru = "でおじゃる（古风）";
			L.ai = "い形容词";
			L.an = "な形容词";
			L.nn = "名词";
			L.badai = "い形容词要以い结尾";
			L.pol = "丁宁形";
			L.polneg = "丁宁否定";
			L.polpast = "丁宁过去";
			L.polpastneg = "丁宁过去否定";
			L.adv = "副词形";
			L.naru = "なる";
			L.sa = "さ";
			L.rentai = "连体形";
			L.darou = "だろう";
			L.deshou = "でしょう";
			L.tarou = "たろう";
			L.dearu = "である";
			L.nda = "んだ";
			L.nanda = "なんだ";
			L.tte = "って";
			L.evid = "传闻・推量";
			L.tasouda = "たそうだ";
			L.rashii = "らしい";
			L.youda = "ようだ";
			L.mitaida = "みたいだ";
			L.kamoshirenai = "かもしれない";
			L.hazuda = "はずだ";
			L.kchigai = "に違いない";
			L.ktono = "とのことだ";
			L.ktoka = "とか";
			L.kktite = "って聞いた";
			L.kppoi = "っぽい";
			L.kge = "げ";
			L.kkehai = "気配だ";
			L.duty = "判断・义务";
			L.wake = "わけだ";
			L.wakenai = "わけがない";
			L.beki = "べきだ";
			L.bekinai = "べきではない";
			L.monoda = "ものだ";
			L.kotoda = "ことだ";
			L.tsumori = "つもりだ";
			L.yotei = "予定だ";
			L.degree = "程度・倾向";
			L.gachi = "がちだ";
			L.kimi = "気味だ";
			L.ippou = "一方だ";
			L.bakari = "ばかりだ";
			L.tokoro1 = "ところだ（即将）";
			L.tokoro2 = "ところだ（刚刚）";
			L.tokoro3 = "ところだ（正在）";
			L.confirm = "疑问・确认";
			L.kana = "かな";
			L.kashira = "かしら";
			L.deshoune = "でしょうね";
			L.janai = "んじゃないか";
			L.reason = "原因・理由";
			L.nodakara = "のだから";
			L.monode = "もので";
			L.okage = "おかげで";
			L.naihasei = "ない的派生";
			L.nasa = "なさ";
			L.nasage = "なさげ";
			L.nasasugiru = "なさすぎる";
			L.cop = "丁重・尊敬（だ・です）";
			L.degozaru = "でござる";
			L.degozaimasu = "でございます";
			L.degozaimashita = "でございました";
			L.degozaimashou = "でございましょう";
			L.deirassharu = "でいらっしゃる";
			L.deirasshaimasu = "でいらっしゃいます";
			L.dks = "方言・关西";
			L.dky = "方言・九州";
			L.dth = "方言・东北";
			L.dhi = "方言・广岛";
			L.term = "终止形";
			L.zh = true;
			return L;
		}
		L.dict = "dictionary";
		L.masu = "masu";
		L.masen = "masen";
		L.mashita = "mashita";
		L.masend = "masen deshita";
		L.nai = "nai";
		L.nakatta = "nakatta";
		L.nakute = "nakute";
		L.naide = "naide";
		L.te = "te";
		L.ta = "ta";
		L.tara = "tara";
		L.ba = "ba";
		L.cmd = "imperative";
		L.nasai = "nasai";
		L.na = "na (= nasai)";
		L.tamae = "tamae";
		L.stop = "prohibitive";
		L.vol = "volitional";
		L.pot = "potential";
		L.pas = "passive";
		L.cau = "causative";
		L.cap = "causative-passive";
		L.tai = "tai";
		L.takunai = "takunai";
		L.c1 = "group 1 (godan)";
		L.c2 = "group 2 (ichidan)";
		L.c3s = "group 3 (suru)";
		L.c3k = "group 3 (kuru)";
		L.iku = "iku";
		L.gson = "Honorific";
		L.gken = "Humble";
		L.gtei = "Deferential";
		L.son = "ni naru";
		L.sonm = "masu";
		L.sonr = "reru";
		L.sonrm = "remasu";
		L.kud = "kudasai";
		L.ken = "suru";
		L.kenm = "shimasu";
		L.itas = "itasu";
		L.itasm = "itashimasu";
		L.tei = "plain";
		L.teim = "masu";
		L.bad2 = "Group 2 must end with る";
		L.bad3 = "Group 3 is する or くる";
		L.bad1 = "Group 1 must end with an u-row kana";
		L.teiru = "teiru";
		L.aux = "Auxiliaries";
		L.auxte = "te-form +";
		L.auxpol = "Suggestions";
		L.ngara = "nagara";
		L.tsutsu = "tsutsu";
		L.tsutsuaru = "tsutsu aru";
		L.tagaru = "tagaru";
		L.tagaranai = "tagaranai";
		L.yagaru = "yagaru";
		L.sugiru = "sugiru";
		L.suginai = "suginai";
		L.yasui = "yasui";
		L.nikui = "nikui";
		L.souda = "souda";
		L.souni = "sou ni / sou na";
		L.kiru = "kiru";
		L.kireru = "kireru";
		L.au = "au";
		L.kakeru = "kakeru";
		L.kakaru = "kakaru";
		L.sokonau = "sokonau";
		L.teoku = "teoku";
		L.teshimau = "teshimau";
		L.temiru = "temiru";
		L.tearu = "tearu";
		L.tekuru = "tekuru";
		L.teiku = "teiku";
		L.tekudasai = "tekudasai";
		L.mashou = "mashou";
		L.mashouka = "mashou ka";
		L.masenka = "masen ka";
			L.teita = "teita";
			L.teinai = "teinai";
			L.teimasu = "teimasu";
			L.teshimatta = "teshimatta";
			L.tewa = "tewa";
			L.nakereba = "nakereba";
			L.nakucha = "nakucha";
			L.noda = "noda";
			L.terasshai = "teirasshai";
		L.rough = "Rough / masculine";
		L.zo = "zo";
		L.ze = "ze";
		L.wa = "wa";
		L.sa = "sa";
		L.me = "me (derogatory)";
		L.kke = "kke";
		L.mon = "mon (because)";
		L.tekure = "tekure";
		L.kusaru = "kusaru (rough)";
		L.koku = "koku (rough)";
		L.reg = "Character speech";
		L.jdegozaru = "degozaru (samurai)";
		L.jdearimasu = "de arimasu (military)";
		L.jnya = "nya (cat)";
		L.jnanoda = "na no da (narrator)";
		L.jdesuwa = "desu wa (ojou-sama)";
		L.jnoja = "no ja (elder)";
		L.jssu = "ssu (sports club)";
		L.jssho = "ssho (gal)";
		L.jjan = "jan (casual)";
		L.jnari = "nari (samurai)";
		L.jdesourou = "de sourou (classical)";
		L.jojaru = "de ojaru (archaic)";
		L.ai = "i-adjective";
		L.an = "na-adjective";
		L.nn = "noun";
		L.badai = "An i-adjective must end with い";
		L.pol = "polite";
		L.polneg = "polite negative";
		L.polpast = "polite past";
		L.polpastneg = "polite past negative";
		L.adv = "adverbial";
		L.naru = "naru";
		L.sa = "sa";
		L.rentai = "attributive";
		L.darou = "darou";
		L.deshou = "deshou";
		L.tarou = "tarou";
		L.dearu = "dearu";
		L.nda = "nda";
		L.nanda = "nanda";
		L.tte = "tte";
		L.evid = "Hearsay / conjecture";
		L.tasouda = "ta + souda";
		L.rashii = "rashii";
		L.youda = "youda";
		L.mitaida = "mitaida";
		L.kamoshirenai = "kamoshirenai";
		L.hazuda = "hazuda";
		L.kchigai = "ni chigai nai";
		L.ktono = "to no koto da";
		L.ktoka = "toka";
		L.kktite = "tte kiita";
		L.kppoi = "ppoi";
		L.kge = "ge";
		L.kkehai = "kehai da";
		L.duty = "Judgement / duty";
		L.wake = "wake da";
		L.wakenai = "wake ga nai";
		L.beki = "beki da";
		L.bekinai = "beki dewa nai";
		L.monoda = "mono da";
		L.kotoda = "koto da";
		L.tsumori = "tsumori da";
		L.yotei = "yotei da";
		L.degree = "Degree / tendency";
		L.gachi = "gachi da";
		L.kimi = "gimi da";
		L.ippou = "ippou da";
		L.bakari = "bakari da";
		L.tokoro1 = "tokoro da (about to)";
		L.tokoro2 = "tokoro da (just did)";
		L.tokoro3 = "tokoro da (doing)";
		L.confirm = "Question / confirmation";
		L.kana = "kana";
		L.kashira = "kashira";
		L.deshoune = "deshou ne";
		L.janai = "n ja nai ka";
		L.reason = "Reason";
		L.nodakara = "no dakara";
		L.monode = "mono de";
		L.okage = "okage de";
		L.naihasei = "Derived from nai";
		L.nasa = "nasa";
		L.nasage = "nage";
		L.nasasugiru = "nasa sugiru";
		L.cop = "Deferential / honorific copula";
		L.degozaru = "de gozaru";
		L.degozaimasu = "de gozaimasu";
		L.degozaimashita = "de gozaimashita";
		L.degozaimashou = "de gozaimashou";
		L.deirassharu = "de irassharu";
		L.deirasshaimasu = "de irasshaimasu";
		L.dks = "Kansai dialect";
		L.dky = "Kyushu dialect";
		L.dth = "Tohoku dialect";
		L.dhi = "Hiroshima dialect";
		L.term = "terminal form";
		L.zh = false;
		return L;
	}

	// 语法句型用的接续形。文语的未然形 する→せ、来る→こ，其余同 ない 的词干。
	static JpPatForms jpforms(string plain, string masu, string nai, string te, string ta, string cau) {
		var p = plain ?? "";
		var naistem = cut(nai, 2);
		var bunzen = naistem;
		if (p == "為る") bunzen = "せ";
		else if (p.EndsWith("する", StringComparison.Ordinal)) bunzen = cut(p, 2) + "せ";
		return new JpPatForms {
			Plain = plain, Ren = cut(masu, 2), Nai = nai, Naistem = naistem,
			Bunzen = bunzen, Te = te, Ta = ta, Cau = cau,
		};
	}

	// ───────── 形容词、名词 ─────────

	// い形容词：高くない、高かった。いい（良い）和 かっこいい 的活用按「よ」走。
	static JpConjPacked iadj(string word, JpConjLabels L) {
		if (word.Length < 2 || last(word) != "い") return fail(L.badai);
		var s = iadjstem(word);
		var rows = new List<ConjRow> {
			row(L.dict, word),
			row(L.pol, word + "です"),
			row(L.nai, s + "くない"),
			row(L.polneg, s + "くありません"),
			row(L.ta, s + "かった"),
			row(L.polpast, s + "かったです"),
			row(L.nakatta, s + "くなかった"),
			row(L.polpastneg, s + "くありませんでした"),
			row(L.te, s + "くて、" + s + "くなくて"),
			row(L.adv, s + "く"),
			row(L.ba, s + "ければ、" + s + "けりゃ"),
			row(L.nakereba, s + "くなければ、" + s + "くなきゃ"),
			row(L.tara, s + "かったら、" + s + "くなかったら"),
			row(L.naru, s + "くなる"),
			row(L.souda, (irri(word) ? s + "さそうだ" : s + "そうだ") + "、" + s + "くなさそうだ"),
			row(L.sugiru, s + "すぎる"),
			row(L.sa, s + "さ"),
			row(L.darou, word + "だろう、" + s + "くないだろう、" + s + "かっただろう、" + s + "くなかっただろう"),
			row(L.deshou, word + "でしょう、" + s + "くないでしょう、" + s + "かったでしょう、" + s + "くなかったでしょう"),
			row(L.tarou, s + "かったろう、" + s + "くなかったろう"),
			row(L.nda, word + "んだ、" + s + "くないんだ"),
			row(L.tte, word + "って、" + s + "くないって"),
		};
		var k = new JpConjForms {
			plain = word, pred = word, drop = word, attrib = word, attribn = word,
			ren = s + "く", ta = s + "かった", stem = s,
			neg = s + "くない", negta = s + "くなかった",
		};
		rows.AddRange(evidrows(L, k));
		rows.AddRange(patternrows(L, k, "ai"));
		rows.AddRange(naihaseirows(L, s + "くない"));
		rows.AddRange(dialectiadj(L, s));
		rows.AddRange(jargonrows(L, word, "i", ""));
		rows.AddRange(roughrows(L, word, "i"));
			return new JpConjPacked { plain = word, rows = rows, tail = JpGrammar.PosRows(L.zh, "i", word) };
	}

	static string iadjstem(string word) {
		if (irri(word))
			return word == "いい" || word == "良い" || word == "よい" || word == "好い" ? "よ" : cut(word, 2) + "よ";
		return cut(word, 1);
	}

	// いい・かっこいい 这类词干是「よ」，「そうだ」要变成「よさそうだ」。
	static bool irri(string word) {
		if (word == "いい" || word == "良い" || word == "よい" || word == "好い") return true;
		return ends(word, "いい") && word != "かわいい" && word != "可愛い";
	}

	// な形容词：静かだ、静かだった。
	static JpConjPacked naadj(string word, JpConjLabels L) {
		if (ends(word, "な") || ends(word, "だ")) word = cut(word, 1);
		if (word.Length == 0) return fail(L.bad3);
		var rows = new List<ConjRow> {
			row(L.dict, word + "だ"),
			row(L.pol, word + "です"),
			row(L.nai, word + "ではない、" + word + "じゃない"),
			row(L.polneg, word + "ではありません"),
			row(L.ta, word + "だった"),
			row(L.polpast, word + "でした"),
			row(L.nakatta, word + "ではなかった、" + word + "じゃなかった"),
			row(L.polpastneg, word + "ではありませんでした"),
			row(L.te, word + "で、" + word + "ではなくて"),
			row(L.rentai, word + "な、" + word + "でない"),
			row(L.adv, word + "に"),
			row(L.ba, word + "なら、" + word + "なりゃ"),
			row(L.nakereba, word + "でなければ、" + word + "じゃなきゃ"),
			row(L.tara, word + "だったら、" + word + "ではなかったら"),
			row(L.naru, word + "になる"),
			row(L.dearu, word + "である、" + word + "ではない"),
			row(L.souda, word + "そうだ、" + word + "じゃなさそうだ"),
			row(L.sugiru, word + "すぎる"),
			row(L.sa, word + "さ"),
			row(L.darou, word + "だろう、" + word + "ではないだろう、" + word + "だっただろう、" + word + "ではなかっただろう"),
			row(L.deshou, word + "でしょう、" + word + "ではないでしょう、" + word + "だったでしょう、" + word + "ではなかったでしょう"),
			row(L.tarou, word + "だったろう、" + word + "ではなかったろう"),
			row(L.nanda, word + "なんだ、" + word + "ではないんだ"),
			row(L.tte, word + "だって、" + word + "ではないって"),
		};
		var k = new JpConjForms {
			plain = word, pred = word + "だ", drop = word, attrib = word + "な", attribn = word + "な",
			ren = word + "に", ta = word + "だった", stem = word,
			neg = word + "ではない", negta = word + "ではなかった",
		};
		rows.AddRange(evidrows(L, k));
		rows.AddRange(patternrows(L, k, "an"));
		rows.AddRange(naihaseirows(L, word + "じゃない"));
		rows.AddRange(copularows(L, word));
		rows.AddRange(jargonrows(L, word, "a", ""));
		rows.AddRange(roughrows(L, word, "a"));
		rows.AddRange(dialectnounrows(L, word));
			return new JpConjPacked { plain = word, rows = rows, tail = JpGrammar.PosRows(L.zh, "a", word) };
	}

	// 名词：学生だ、学生だった。和な形容词一样接だ・で・だった，连体形用の。
	static JpConjPacked nounrows(string word, JpConjLabels L) {
		if (ends(word, "だ")) word = cut(word, 1);
		if (word.Length == 0) return fail(L.bad3);
		var rows = new List<ConjRow> {
			row(L.dict, word + "だ"),
			row(L.pol, word + "です"),
			row(L.nai, word + "ではない、" + word + "じゃない"),
			row(L.polneg, word + "ではありません"),
			row(L.ta, word + "だった"),
			row(L.polpast, word + "でした"),
			row(L.nakatta, word + "ではなかった、" + word + "じゃなかった"),
			row(L.polpastneg, word + "ではありませんでした"),
			row(L.te, word + "で、" + word + "ではなくて"),
			row(L.rentai, word + "の、" + word + "でない"),
			row(L.ba, word + "なら、" + word + "なりゃ"),
			row(L.nakereba, word + "でなければ、" + word + "じゃなきゃ"),
			row(L.tara, word + "だったら、" + word + "ではなかったら"),
			row(L.dearu, word + "である、" + word + "ではない"),
			row(L.darou, word + "だろう、" + word + "ではないだろう、" + word + "だっただろう、" + word + "ではなかっただろう"),
			row(L.deshou, word + "でしょう、" + word + "ではないでしょう、" + word + "だったでしょう、" + word + "ではなかったでしょう"),
			row(L.tarou, word + "だったろう、" + word + "ではなかったろう"),
			row(L.nanda, word + "なんだ、" + word + "ではないんだ"),
			row(L.tte, word + "だって、" + word + "ではないって"),
		};
		var k = new JpConjForms {
			plain = word, pred = word + "だ", drop = word, attrib = word + "な", attribn = word + "の",
			ren = word + "に", ta = word + "だった", stem = word,
			neg = word + "ではない", negta = word + "ではなかった",
		};
		rows.AddRange(evidrows(L, k));
		rows.AddRange(patternrows(L, k, "n"));
		rows.AddRange(naihaseirows(L, word + "じゃない"));
		rows.AddRange(copularows(L, word));
		rows.AddRange(jargonrows(L, word, "n", ""));
		rows.AddRange(roughrows(L, word, "n"));
		rows.AddRange(dialectnounrows(L, word));
			return new JpConjPacked { plain = word, rows = rows, tail = JpGrammar.PosRows(L.zh, "n", word) };
	}

	// ───────── 动词 ─────────

	static JpConjPacked pack(JpConjLabels L, string plain, string masu, string nai, string te, string ta, string ba,
		string cmd, string vol, string pot, string pas, string cau, string cap) {
		var tai = cut(masu, 2) + "たい";
		var rows = new List<ConjRow> {
			row(L.dict, plain),
			row(L.masu, masu),
			row(L.masen, cut(masu, 2) + "ません"),
			row(L.mashita, cut(masu, 2) + "ました"),
			row(L.masend, cut(masu, 2) + "ませんでした"),
			row(L.nai, nai + "、" + cut(nai, 2) + "ねえ、" + cut(nai, 2) + "んない"),
			row(L.nakatta, cut(nai, 2) + "なかった"),
			row(L.nakute, cut(nai, 2) + "なくて"),
			row(L.naide, cut(nai, 2) + "ないで"),
			row(L.te, te),
			row(L.ta, ta),
			row(L.tara, ta + "ら、" + cut(nai, 2) + "なかったら"),
			row(L.ba, ba + "、" + ryaf(ba)),
			row(L.nakereba, cut(nai, 2) + "なければ、" + cut(nai, 2) + "なきゃ"),
			row(L.nakucha, cut(nai, 2) + "なくては、" + cut(nai, 2) + "なくちゃ"),
			row(L.cmd, cmd),
		};
		rows.AddRange(nasairows(L, plain, cut(masu, 2)));
		rows.Add(row(L.stop, plain + "な"));
		rows.Add(row(L.vol, vol));
		rows.Add(row(L.pot, pot.Contains("られる") ? pot + "、" + pot.Replace("られる", "れる") : pot));
		rows.Add(row(L.pas, pas));
		rows.Add(row(L.cau, cau));
		rows.Add(row(L.cap, cap));
		rows.Add(row(L.tai, tai));
		rows.Add(row(L.takunai, cut(tai, 2) + "たくない"));
		rows.Add(row(L.nda, plain + "んだ、" + nai + "んだ"));
		rows.Add(row(L.tte, plain + "って、" + nai + "って"));
		rows.Add(row(L.noda, plain + "のです、" + plain + "んです"));
		rows.Add(row(L.kke, ta + "っけ、" + plain + "っけ"));
		rows.Add(row(L.mon, plain + "もん、" + plain + "んだもん"));
		rows.AddRange(auxrows(L, plain, masu, te, nai));
		rows.AddRange(evidrows(L, verbforms(plain, masu, nai, te, ta)));
		rows.AddRange(patternrows(L, verbforms(plain, masu, nai, te, ta), "v"));
		rows.AddRange(naihaseirows(L, nai));
		rows.AddRange(keigo(plain, masu, pas, L));
		rows.AddRange(jargonrows(L, plain, "v", masu));
		rows.AddRange(roughrows(L, plain, "v"));
		// 尊敬语命令：～ていらっしゃい／～てらっしゃい（行ってらっしゃい）
		if (!issonkearu(plain) && !isaru(plain)) rows.Add(new ConjRow(L.terasshai, te + "いらっしゃい、" + te + "らっしゃい", L.gson));
		// N5~N0 的句型放到整张表的最后，由 Build 在方言之后拼接。
		var tail = JpGrammar.Rows(L.zh, jpforms(plain, masu, nai, te, ta, cau));
		return new JpConjPacked { plain = plain, masu = masu, te = te, ta = ta, nai = nai, rows = rows, tail = tail };
	}

	// 语气句型用的接续形（动词）。
	static JpConjForms verbforms(string plain, string masu, string nai, string te, string ta) {
		return new JpConjForms {
			plain = plain, pred = plain, drop = plain, attrib = plain, attribn = plain,
			ren = cut(masu, 2), ta = ta, teiru = te + "いる", stem = cut(masu, 2),
			neg = nai, negta = cut(nai, 2) + "なかった",
		};
	}

	// ない 的派生：なさ（名词化）、なさげ、なさすぎる。都是 ない 去掉い再加。
	static List<ConjRow> naihaseirows(JpConjLabels L, string neg) {
		var s = cut(neg, 1);
		return new List<ConjRow> {
			new(L.nasa, s + "さ", L.naihasei),
			new(L.nasage, s + "さげ", L.naihasei),
			new(L.nasasugiru, s + "さすぎる", L.naihasei),
		};
	}

	// い形容词的方言：关西 高うない／高うて／高うなる，九州（博多）高か／高うなか／高うて。
	static List<ConjRow> dialectiadj(JpConjLabels L, string s) {
		return new List<ConjRow> {
			new(L.nai, s + "うない", L.dks),
			new(L.te, s + "うて", L.dks),
			new(L.naru, s + "うなる", L.dks),
			new(L.term, s + "か", L.dky),
			new(L.nai, s + "うなか", L.dky),
			new(L.te, s + "うて", L.dky),
		};
	}

	// な形容词、名词的方言：关西的断定是 や，否定 やない，过去 やった。
	static List<ConjRow> dialectnounrows(JpConjLabels L, string word) {
		return new List<ConjRow> {
			new(L.dict, word + "や", L.dks),
			new(L.nai, word + "やない", L.dks),
			new(L.ta, word + "やった", L.dks),
		};
	}

	// 一条语气句型：肯定和否定并列放在同一格里，各带一个发音按钮。
	static List<ConjRow> pair(string label, string pos, string neg, string group) {
		var v = string.IsNullOrEmpty(neg) ? pos : pos + "、" + neg;
		return new List<ConjRow> { new(label, v, group) };
	}

	// 传闻・推量：pred 是普通形现在的说法（な形容词、名词带だ），plain 去掉だ的形，
	// ta 是普通形过去，attrib 是接ようだ・はずだ时的形（な形容词な，名词の），neg／negta 是它们的否定。
	static List<ConjRow> evidrows(JpConjLabels L, JpConjForms k) {
		var rows = new List<ConjRow>();
		rows.AddRange(pair(L.souda, k.pred + "そうだ", k.neg + "そうだ", L.evid));
		rows.AddRange(pair(L.tasouda, k.ta + "そうだ", k.negta + "そうだ", L.evid));
		rows.AddRange(pair(L.rashii, k.plain + "らしい", k.neg + "らしい", L.evid));
		rows.AddRange(pair(L.youda, k.attribn + "ようだ", k.neg + "ようだ", L.evid));
		rows.AddRange(pair(L.mitaida, k.plain + "みたいだ", k.neg + "みたいだ", L.evid));
		rows.AddRange(pair(L.kamoshirenai, k.plain + "かもしれない", k.neg + "かもしれない", L.evid));
		rows.AddRange(pair(L.hazuda, k.attribn + "はずだ", k.neg + "はずだ", L.evid));
		return rows;
	}

	// 判断・义务、程度・倾向、疑问・确认、原因・理由这些句型。
	// k 是这个词性对应的接续形：
	//   plain 普通形、pred 带だ的普通形、drop 去掉だ、attrib 接わけだ等的连体形（な形容词な、名词な）、
	//   attribN 接ようだ等的连体形（名词の）、ren 连用形、ta 过去、stem 词干。
	// 只有动词能接的（べきだ、がちだ 等）和只有形容词能接的（げ）在这里按词性取舍。
	static List<ConjRow> patternrows(JpConjLabels L, JpConjForms k, string cat) {
		var verb = cat == "v";
		var adj = cat == "ai" || cat == "an";
		var noun = cat == "n";
		var rows = new List<ConjRow>();
		rows.AddRange(pair(L.kchigai, k.drop + "に違いない", k.neg + "に違いない", L.evid));
		rows.AddRange(pair(L.ktono, k.pred + "とのことだ", k.neg + "とのことだ", L.evid));
		rows.AddRange(pair(L.ktoka, k.pred + "とか", k.neg + "とか", L.evid));
		rows.AddRange(pair(L.kktite, k.pred + "って聞いた", k.neg + "って聞いた", L.evid));
		rows.AddRange(pair(L.kppoi, k.stem + "っぽい", "", L.evid));
		if (adj) rows.AddRange(pair(L.kge, k.stem + "げ", "", L.evid));
		if (verb) rows.AddRange(pair(L.kkehai, k.plain + "気配だ", k.neg + "気配だ", L.evid));
		rows.AddRange(pair(L.wake, k.attrib + "わけだ", k.neg + "わけだ", L.duty));
		rows.AddRange(pair(L.wakenai, k.attrib + "わけがない", k.neg + "わけがない", L.duty));
		if (verb) {
			rows.AddRange(pair(L.beki, k.plain + "べきだ", "", L.duty));
			rows.AddRange(pair(L.bekinai, k.plain + "べきではない", "", L.duty));
		}
		if (!noun) rows.AddRange(pair(L.monoda, k.attrib + "ものだ", k.neg + "ものだ", L.duty));
		if (verb) {
			rows.AddRange(pair(L.kotoda, k.plain + "ことだ", k.neg + "ことだ", L.duty));
			rows.AddRange(pair(L.tsumori, k.plain + "つもりだ", k.neg + "つもりだ", L.duty));
			rows.AddRange(pair(L.yotei, k.plain + "予定だ", k.neg + "予定だ", L.duty));
			rows.AddRange(pair(L.gachi, k.ren + "がちだ", "", L.degree));
			rows.AddRange(pair(L.kimi, k.ren + "気味だ", "", L.degree));
			rows.AddRange(pair(L.ippou, k.plain + "一方だ", "", L.degree));
			rows.AddRange(pair(L.bakari, k.plain + "ばかりだ", "", L.degree));
			// ところだ 有三种：辞書形＝即将，た形＝刚刚，ている＝正在。
			rows.AddRange(pair(L.tokoro1, k.plain + "ところだ", "", L.degree));
			rows.AddRange(pair(L.tokoro2, k.ta + "ところだ", "", L.degree));
			rows.AddRange(pair(L.tokoro3, k.teiru + "ところだ", "", L.degree));
		}
		rows.AddRange(pair(L.kana, k.drop + "かな", k.neg + "かな", L.confirm));
		rows.AddRange(pair(L.kashira, k.drop + "かしら", k.neg + "かしら", L.confirm));
		rows.AddRange(pair(L.deshoune, k.drop + "でしょうね", k.neg + "でしょうね", L.confirm));
		rows.AddRange(pair(L.janai, k.attrib + "んじゃないか", k.neg + "んじゃないか", L.confirm));
		rows.AddRange(pair(L.nodakara, k.attrib + "のだから", k.neg + "のだから", L.reason));
		rows.AddRange(pair(L.monode, k.attrib + "もので", k.neg + "もので", L.reason));
		if (verb) rows.AddRange(pair(L.okage, k.ta + "おかげで", "", L.reason));
		if (noun) rows.AddRange(pair(L.okage, k.attribn + "おかげで", "", L.reason));
		return rows;
	}

	// 粗俗、男性语：ぞ・ぜ・わ・さ 这类终助词，名词还能加蔑称的「め」。
	static List<ConjRow> roughrows(JpConjLabels L, string plain, string kind) {
		var dan = kind == "a" || kind == "n" ? "だ" : "";
		var rows = new List<ConjRow> {
			new(L.zo, plain + dan + "ぞ", L.rough),
			new(L.ze, plain + dan + "ぜ", L.rough),
			new(L.wa, plain + dan + "わ", L.rough),
			new(L.sa, plain + dan + "さ", L.rough),
		};
		if (kind == "n") rows.Add(new ConjRow(L.me, plain + "め", L.rough));
		return rows;
	}
	// 小圈子的角色语：武士、大小姐、猫语、网络语这类只在特定语境里用的说法。
	static List<ConjRow> jargonrows(JpConjLabels L, string plain, string kind, string masu) {
		var noun = kind == "n";
		var na = kind == "a";
		var dan = noun || na ? "な" : "";
		var rows = new List<ConjRow> {
			new(L.jdegozaru, plain + "でござる", L.reg),
			new(L.jdearimasu, plain + "であります", L.reg),
			new(L.jnya, plain + "にゃ", L.reg),
			new(L.jnanoda, plain + dan + "のだ", L.reg),
			// ですわ 只接名词和形容词；动词要先变ます（食べますわ），不能接辞书形。
			new(L.jdesuwa, kind == "v" ? masu + "わ" : plain + "ですわ", L.reg),
			new(L.jnoja, plain + dan + "のじゃ", L.reg),
			new(L.jssu, plain + "っす", L.reg),
			new(L.jssho, plain + "っしょ", L.reg),
			new(L.jjan, plain + "じゃん", L.reg),
			new(L.jdesourou, plain + "で候", L.reg),
			new(L.jojaru, plain + "でおじゃる", L.reg),
		};
		if (noun || na) rows.Insert(9, new ConjRow(L.jnari, plain + "なり", L.reg));
		return rows;
	}
	// だ・です的敬语说法：丁重的 でござる、尊敬的 でいらっしゃる。な形容词和名词都能接。
	static List<ConjRow> copularows(JpConjLabels L, string word) {
		return new List<ConjRow> {
			new(L.degozaru, word + "でござる", L.cop),
			new(L.degozaimasu, word + "でございます、" + word + "ではございません", L.cop),
			new(L.degozaimashita, word + "でございました、" + word + "ではございませんでした", L.cop),
			new(L.degozaimashou, word + "でございましょう", L.cop),
			new(L.deirassharu, word + "でいらっしゃる", L.cop),
			new(L.deirasshaimasu, word + "でいらっしゃいます", L.cop),
		};
	}

	// 连用形、て形后面常接的补助动词，和敬体的劝诱・疑问，各成一组。
	// ある・いる 是状态动词，なさる・くださる 本身是敬语，都不接这些。
	static List<ConjRow> auxrows(JpConjLabels L, string plain, string masu, string te, string nai) {
		if (isaru(plain) || isstateiru(plain) || issonkearu(plain)) return new List<ConjRow>();
		var ren = cut(masu, 2);
		var rows = new List<ConjRow> {
			row(L.ngara, ren + "ながら"),
			row(L.tsutsu, ren + "つつ"),
			row(L.tsutsuaru, ren + "つつある"),
			row(L.tagaru, ren + "たがる"),
			row(L.tagaranai, ren + "たがらない"),
			row(L.yagaru, ren + "やがる"),
			row(L.sugiru, ren + "すぎる"),
			row(L.suginai, ren + "すぎない"),
			row(L.yasui, ren + "やすい"),
			row(L.nikui, ren + "にくい"),
			row(L.souda, ren + "そうだ、" + cut(nai, 1) + "さそうだ"),
			row(L.souni, ren + "そうに、" + ren + "そうな"),
			row(L.kiru, ren + "きる"),
			row(L.kireru, ren + "きれる"),
			row(L.au, ren + "あう"),
			row(L.kakeru, ren + "かける"),
			row(L.kakaru, ren + "かかる"),
			row(L.sokonau, ren + "そこなう"),
		};
		// 每个形都把原形和口语缩略排在一起，中间用顿号分开。
		var stem = cut(te, 1);
		var cha = te.EndsWith("で") ? "じゃ" : "ちゃ";
		var toku = stem + (te.EndsWith("で") ? "どく" : "とく");
		var teerows = new List<ConjRow> {
			row(L.teiru, te + "いる、" + te + "る"),
			row(L.teita, te + "いた、" + te + "た"),
			row(L.teinai, te + "いない、" + te + "ない"),
			row(L.teimasu, te + "います、" + te + "ます"),
			row(L.teoku, te + "おく、" + toku),
			row(L.teshimau, te + "しまう、" + stem + cha + "う、" + stem + (te.EndsWith("で") ? "じまう" : "ちまう")),
			row(L.teshimatta, te + "しまった、" + stem + cha + "った"),
			row(L.tewa, te + "は、" + stem + cha),
			row(L.temiru, te + "みる"),
			row(L.tearu, te + "ある"),
			row(L.tekuru, te + "くる、" + te + "く、" + te + "来い"),
			row(L.teiku, te + "いく、" + te + "く"),
			row(L.tekure, te + "くれ、" + te + "くれないか"),
			row(L.tekudasai, te + "ください"),
		};
		var polrows = new List<ConjRow> {
			row(L.mashou, ren + "ましょう"),
			row(L.mashouka, ren + "ましょうか"),
			row(L.masenka, ren + "ませんか"),
		};
		var outs = setgroup(rows, L.aux);
		outs.AddRange(setgroup(teerows, L.auxte));
		outs.AddRange(setgroup(polrows, L.auxpol));
		return outs;
	}

	// ば形 → 口语缩略：行けば→行きゃ、食べれば→食べりゃ、すれば→すりゃ。
	static string ryaf(string ba) {
		if (string.IsNullOrEmpty(ba) || !ba.EndsWith("ば")) return ba;
		var s = ba.Substring(0, ba.Length - 1);
		const string e = "けれせてねめげべでへ";
		const string i = "きりしちにみぎびぢひ";
		var last = s[s.Length - 1];
		var n = e.IndexOf(last);
		if (n < 0) return s + "ゃ";
		return s.Substring(0, s.Length - 1) + i[n] + "ゃ";
	}

	static List<ConjRow> setgroup(List<ConjRow> rows, string g) {
		foreach (var one in rows) one.G = g;
		return rows;
	}

	static bool isstateiru(string word) {
		word = norm(word);
		return word == "居る" || hira(word) == "いる";
	}

	// なさい・給え接连用形（行き＋なさい／行きたまえ）；な是なさい的省略，同样接连用形（行きな），
	// 和禁止形的「辞书形＋な」（行くな）不是一回事。なさる、くださる这类尊敬动词本身已是敬语，不加。
	static List<ConjRow> nasairows(JpConjLabels L, string plain, string ren) {
		if (issonkearu(plain)) return new List<ConjRow>();
		return new List<ConjRow> {
			row(L.nasai, ren + "なさい"),
			row(L.na, ren + "な"),
			row(L.tamae, ren + "たまえ"),
		};
	}

	// 方言只列和标准语不同的形，按使用人口和知名度的顺序：关西、九州、东北、广岛。
	// 同一方言内部的细节各地不同，这里取最常见的说法。
	static List<ConjRow> dialectrows(string word, string kind, JpConjPacked std, JpConjLabels L) {
		if (issonkearu(word)) return new List<ConjRow>();
		var rows = new List<ConjRow>();
		var ren = cut(std.masu, 2);
		var mz = mizen(word, kind, ren);
		var tes = cut(std.te, 1);
		var kuru = kind == "3" && iskuru(word);
		var suru = kind == "3" && !kuru;
		var pre = suru ? cut(ren, 1) : "";
		var aru = isaru(word);
		var kure = iskureru(word);
		var nai = suru ? pre + "せえへん" : kuru ? "けえへん" : kansainai(mz, kind);
		var n = suru ? pre + "せん" : kuru ? "こん" : mz + "ん";
		// 关西：ない＝へん，ないで＝んといて，尊敬＝はる，命令＝なはれ
		rows.Add(new ConjRow(L.nai, nai, L.dks));
		if (!aru) {
			rows.Add(new ConjRow(L.naide, (suru ? pre + "せ" : kuru ? "こ" : mz) + "んといて", L.dks));
			rows.Add(new ConjRow(L.gson, ren + "はる", L.dks));
			if (!kure) rows.Add(new ConjRow(L.cmd, ren + "なはれ", L.dks));
		}
		// 九州（博多）：ない＝ん，ている＝とる，一段とサ変的命令＝れ
		rows.Add(new ConjRow(L.nai, n, L.dky));
		if (!aru) rows.Add(new ConjRow(L.teiru, tes + "とる", L.dky));
		if (!kure && (kind == "2" || suru)) rows.Add(new ConjRow(L.cmd, suru ? pre + "せれ" : mz + "れ", L.dky));
		// 东北：意向＝べ，一段的命令＝れ
		rows.Add(new ConjRow(L.vol, std.plain + "べ", L.dth));
		if (!kure && kind == "2") rows.Add(new ConjRow(L.cmd, mz + "れ", L.dth));
		// 广岛：ない＝ん，なかった＝んかった，ている＝とる・ちょる，尊敬＝んさる
		rows.Add(new ConjRow(L.nai, n, L.dhi));
		rows.Add(new ConjRow(L.nakatta, n + "かった", L.dhi));
		if (!aru) {
			rows.Add(new ConjRow(L.teiru, tes + "とる、" + tes + "ちょる", L.dhi));
			rows.Add(new ConjRow(L.gson, ren + "んさる", L.dhi));
		}
		return rows;
	}

	// 方言用的未然形：五段是a段（行か），一段是词干（食べ），する是连用形（勉強し），くる是こ。
	static string mizen(string word, string kind, string ren) {
		if (kind == "3") return iskuru(word) ? "こ" : ren;
		if (kind == "2") return cut(word, 1);
		var col = COL.TryGetValue(last(word), out var found) ? found : null;
		return col != null ? cut(word, 1) + col[0] : "";
	}

	// 关西的「ない」。一段で词干只有一拍时（見・寝・出）用「やへん」：見やへん、寝やへん。
	static string kansainai(string mz, string kind) {
		if (kind == "2" && mz.Length == 1) return mz + "やへん";
		return mz + "へん";
	}

	static JpConjPacked godan(string word, string flag, JpConjLabels L) {
		var end = last(word);
		var col = COL.TryGetValue(end, out var found) ? found : null;
		if (col == null) return fail(L.bad1);
		var stem = cut(word, 1);
		var a = stem + col[0];
		var i = stem + col[1];
		var e = stem + col[3];
		var o = stem + col[4];
		var te = stem + col[5];
		var ta = stem + col[6];
		var cmd = e;
		if (flag == "iku" || isiku(word)) { te = stem + "って"; ta = stem + "った"; }
		if (flag == "ute" || isute(word)) { te = stem + "うて"; ta = stem + "うた"; }
		if (flag == "aru" || issonkearu(word)) { i = stem + "い"; cmd = stem + "い"; }
		var nai = isaru(word) ? arunai(word) : a + "ない";
		return pack(L, word, i + "ます", nai, te, ta, e + "ば", cmd, o + "う", e + "る",
			a + "れる", a + "せる", a + "せられる");
	}

	static JpConjPacked ichidan(string word, JpConjLabels L) {
		if (last(word) != "る") return fail(L.bad2);
		var stem = cut(word, 1);
		var cmd = iskureru(word) ? stem : stem + "ろ、" + stem + "よ";
		return pack(L, word, stem + "ます", stem + "ない", stem + "て", stem + "た", stem + "れば",
			cmd, stem + "よう", stem + "られる", stem + "られる", stem + "させる", stem + "させられる");
	}

	static JpConjPacked surukuru(string word, string sub, JpConjLabels L) {
		if (sub == "kuru" || iskuru(word)) return kuru(word, L);
		if (!issuru(word)) return fail(L.bad3);
		var stem = word == "為る" || hira(word) == "する" ? "" : cut(word, 2);
		var plain = stem + "する";
		var got = pack(L, plain, stem + "します", stem + "しない", stem + "して", stem + "した", stem + "すれば",
			stem + "しろ、" + stem + "せよ", stem + "しよう", stem + "できる", stem + "される",
			stem + "させる", stem + "させられる");
		got.sub = "suru";
		return got;
	}

	static JpConjPacked kuru(string word, JpConjLabels L) {
		var k = word == "来る" || word == "來る";
		var nai = k ? "来ない" : "こない";
		var masu = k ? "来ます" : "きます";
		var te = k ? "来て" : "きて";
		var ta = k ? "来た" : "きた";
		var plain = k ? "来る" : "くる";
		var got = pack(L, plain, masu, nai, te, ta, k ? "来れば" : "くれば",
			k ? "来い" : "こい", k ? "来よう" : "こよう", k ? "来られる" : "こられる",
			k ? "来られる" : "こられる", k ? "来させる" : "こさせる", k ? "来させられる" : "こさせられる");
		got.sub = "kuru";
		return got;
	}

	static string arunai(string word) {
		if (word == "有る" || word == "在る") return "ない";
		return cut(word, 2) + "ない";
	}

	// ───────── 判断 ─────────

	static string[] findpre(string word) {
		word = norm(word);
		if (word.Length == 0) return null;
		var h = hira(word);
		string[] one = null;
		var n = 0;
		foreach (var p in PICKS) {
			if (p[1] == word) return p;
			if (p[2] == word || p[2] == h) { n++; one = p; }
		}
		return n == 1 ? one : null;
	}

	static bool iskuru(string word) {
		word = norm(word);
		return hira(word) == "くる" || word == "来る" || word == "來る";
	}

	static bool issuru(string word) {
		word = norm(word);
		return hira(word) == "する" || word == "為る" || ends(hira(word), "する");
	}

	static bool isiku(string word) {
		word = norm(word);
		if (ends(hira(word), "いく")) return true;
		return ends(word, "行く") || ends(word, "逝く") || ends(word, "往く");
	}

	static bool isaru(string word) {
		word = norm(word);
		return word == "有る" || word == "在る" || ends(hira(word), "ある");
	}

	static bool iskureru(string word) {
		word = norm(word);
		return word == "くれる" || word == "呉れる" || hira(word) == "くれる";
	}

	static bool isute(string word) {
		word = norm(word);
		var h = hira(word);
		if (word == "問う" || word == "請う" || word == "乞う" || word == "恋う" || word == "厭う" || word == "訪う")
			return true;
		return h == "とう" || h == "こう";
	}

	static bool issonkearu(string word) {
		word = norm(word);
		var h = hira(word);
		if (h == "なさる" || h == "くださる" || h == "いらっしゃる" || h == "おっしゃる" || h == "ござる")
			return true;
		return word == "為さる" || word == "下さる";
	}

	static string norm(string s) {
		var sb = new StringBuilder();
		foreach (var c in s ?? "")
			if (!char.IsWhiteSpace(c)) sb.Append(c);
		return hira(sb.ToString());
	}

	static string hira(string s) {
		var sb = new StringBuilder(s.Length);
		foreach (var c in s) {
			var code = (int)c;
			sb.Append(code >= 0x30A1 && code <= 0x30F6 ? (char)(code - 0x60) : c);
		}
		return sb.ToString();
	}

	static string last(string s) => string.IsNullOrEmpty(s) ? "" : s.Substring(s.Length - 1);

	// JS 的 slice(0, -n)：不足 n 个字符时得空串，不能像 C# 那样抛异常。
	static string cut(string s, int n) {
		var k = s.Length - n;
		return k > 0 ? s.Substring(0, k) : "";
	}

	static bool ends(string s, string suf) =>
		s.Length >= suf.Length && s.EndsWith(suf, StringComparison.Ordinal);

	static ConjRow row(string lab, string val) => new ConjRow(lab, val);

	static JpConjPacked fail(string err) => new JpConjPacked { ok = false, err = err };

	// ───────── 敬语 ─────────

	static List<ConjRow> keigo(string word, string masu, string pas, JpConjLabels L) {
		if (issonkearu(word)) return new List<ConjRow>();
		var ren = cut(masu, 2);
		var spec = lexof(word);
		var prod = prodof(word, ren);
		var blockS = spec != null && (spec.only || spec.onlyson);
		var blockK = spec != null && (spec.only || spec.onlyken);
		var son = string.IsNullOrEmpty(spec?.son) ? "" : spec.son;
		var ken = string.IsNullOrEmpty(spec?.ken) ? "" : spec.ken;
		if (!blockS) son = joinuniq(son, prod.son);
		if (!blockK) ken = joinuniq(ken, prod.ken);
		var itas = blockK ? "" : (prod.itas ?? "");
		if (itas.Length > 0 && ken.IndexOf(itas, StringComparison.Ordinal) >= 0) itas = "";
		var tei = string.IsNullOrEmpty(spec?.tei) ? "" : spec.tei;
		if (tei.Length > 0 && tei == ken) tei = "";
		var kud = string.IsNullOrEmpty(spec?.kud) ? (blockS ? "" : (prod.kud ?? "")) : spec.kud;
		var rows = new List<ConjRow>();
		pushk(rows, L.son, son, L.gson);
		pushm(rows, L.sonm, son, L.gson);
		if (!string.IsNullOrEmpty(pas)) {
			pushk(rows, L.sonr, pas, L.gson);
			pushm(rows, L.sonrm, pas, L.gson);
		}
		pushk(rows, L.kud, kud, L.gson);
		pushk(rows, L.ken, ken, L.gken);
		pushm(rows, L.kenm, ken, L.gken);
		pushk(rows, L.itas, itas, L.gken);
		pushm(rows, L.itasm, itas, L.gken);
		pushk(rows, L.tei, tei, L.gtei);
		pushm(rows, L.teim, tei, L.gtei);
		return rows;
	}

	static void pushk(List<ConjRow> rows, string lab, string val, string g) {
		if (string.IsNullOrEmpty(val)) return;
		rows.Add(new ConjRow(lab, val, g));
	}

	static void pushm(List<ConjRow> rows, string lab, string val, string g) {
		var m = mapmasu(val);
		if (string.IsNullOrEmpty(m) || m == val) return;
		rows.Add(new ConjRow(lab, m, g));
	}

	static JpConjLex prodof(string word, string ren) {
		var empty = new JpConjLex();
		var h = hira(word);
		if (issonkearu(word) || iskureru(word)) return empty;
		if (h == "する" || word == "為る" || iskuru(word)) return empty;
		if (h == "いく" || word == "行く" || word == "逝く" || word == "往く") return empty;
		if (h == "いう" || word == "言う" || word == "云う") return empty;
		if (h == "いる" || word == "居る") return empty;
		if (h == "みる" || word == "見る" || word == "観る") return empty;
		if (isiku(word)) return empty;
		if (issuru(word)) {
			var pre = cut(word, 2);
			if (pre.Length == 0) return empty;
			if (kanjionly(pre)) {
				return new JpConjLex {
					son = "ご" + pre + "になる",
					ken = "ご" + pre + "する",
					itas = pre + "いたす",
					kud = pre + "なさってください",
				};
			}
			return new JpConjLex {
				son = "お" + pre + "になる",
				ken = "お" + pre + "する",
				itas = pre + "いたす",
				kud = "お" + pre + "ください",
			};
		}
		if (string.IsNullOrEmpty(ren) || ren.Length < 2) return empty;
		return new JpConjLex {
			son = "お" + ren + "になる",
			ken = "お" + ren + "する",
			itas = "お" + ren + "いたす",
			kud = "お" + ren + "ください",
		};
	}

	static JpConjLex lexof(string word) {
		var k = keyof(word);
		if (k.Length == 0) return null;
		return KEIGO.TryGetValue(k, out var found) ? found : null;
	}

	static string keyof(string word) {
		word = norm(word);
		var h = hira(word);
		if (KEYALIAS.TryGetValue(word, out var alias)) return alias;
		return KEYKANA.TryGetValue(h, out var kana) ? kana : "";
	}

	static string joinuniq(string a, string b) {
		var parts = new List<string>();
		add(a);
		add(b);
		return string.Join("、", parts);

		void add(string s) {
			if (string.IsNullOrEmpty(s)) return;
			foreach (var p in s.Split('、'))
				if (p.Length > 0 && !parts.Contains(p)) parts.Add(p);
		}
	}

	static string mapmasu(string s) {
		if (string.IsNullOrEmpty(s)) return "";
		var outs = new List<string>();
		foreach (var one in s.Split('、')) {
			var m = masuone(one);
			if (m.Length > 0 && !outs.Contains(m)) outs.Add(m);
		}
		return string.Join("、", outs);
	}

	static string masuone(string s) {
		var h = hira(s);
		if (ends(s, "です") || ends(h, "です")) return s;
		if (ends(h, "いらっしゃる") || ends(s, "いらっしゃる")) return cut(s, 1) + "います";
		if (ends(h, "おっしゃる") || ends(s, "おっしゃる")) return cut(s, 1) + "います";
		if (ends(h, "くださる") || ends(s, "くださる") || ends(s, "下さる")) return cut(s, 1) + "います";
		if (ends(h, "ござる") || ends(s, "ござる")) return cut(s, 1) + "います";
		if (ends(h, "なさる") || ends(s, "なさる") || ends(s, "為さる")) return cut(s, 1) + "います";
		if (ends(s, "になる") || ends(h, "になる")) return cut(s, 3) + "になります";
		if (ends(s, "する") || ends(h, "する")) return cut(s, 2) + "します";
		if (ends(s, "いたす") || ends(h, "いたす")) return cut(s, 3) + "いたします";
		if (ends(s, "られる") || ends(s, "れる") || ends(h, "られる") || ends(h, "れる")) return cut(s, 1) + "ます";
		if (ends(s, "上げる") || ends(s, "存じる") || ends(s, "かける")) return cut(s, 1) + "ます";
		if (ends(h, "あげる") || ends(h, "ぞんじる") || ends(h, "かける")) return cut(s, 1) + "ます";
		return godanmasu(s);
	}

	static string godanmasu(string s) {
		var m = new Dictionary<string, string> {
			["う"] = "い", ["く"] = "き", ["ぐ"] = "ぎ", ["す"] = "し", ["つ"] = "ち",
			["ぬ"] = "に", ["ぶ"] = "び", ["む"] = "み", ["る"] = "り",
		};
		var end = last(s);
		return m.TryGetValue(end, out var one) ? cut(s, 1) + one + "ます" : "";
	}

	static bool kanjionly(string s) {
		if (s.Length == 0) return false;
		foreach (var c in s) {
			var code = (int)c;
			if (code < 0x4E00 || code > 0x9FFF) return false;
		}
		return true;
	}
}
