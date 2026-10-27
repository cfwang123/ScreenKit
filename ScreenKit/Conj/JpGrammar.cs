using System.Collections.Generic;

namespace ScreenKit;

/// <summary>拼句型时用到的基础形。全部由动词变形引擎算好传进来。</summary>
sealed class JpPatForms {
	public string Plain;
	public string Ren;
	public string Nai;
	public string Naistem;
	public string Bunzen;
	public string Te;
	public string Ta;
	public string Cau;
}

/// <summary>一条句型：接续 + 后缀、否定形、标签、分组。</summary>
sealed class JpPat {
	public string Conn;
	public string Pos;
	public string NegConn;
	public string Neg;
	public string Zh;
	public string En;
	public string Grp;

	public JpPat(string conn, string pos, string negconn, string neg, string zh, string en, string grp) {
		Conn = conn;
		Pos = pos;
		NegConn = negconn;
		Neg = neg;
		Zh = zh;
		En = en;
		Grp = grp;
	}
}

/// <summary>日语语法句型（N5~N1、文语、复合动词）。按接续类型拼词尾，动词专用。</summary>
static class JpGrammar {
	static readonly JpPat[] PATS = new[] {
		// ── N5 ──
		new JpPat("te", "もいい", "naistem", "なくてもいい", "てもいい", "te mo ii", "n5"),
		new JpPat("te", "はいけない", "naistem", "なくてはいけない", "てはいけない", "te wa ikenai", "n5"),
		new JpPat("naistem", "なければならない", "", "", "なければならない", "nakereba naranai", "n5"),
		new JpPat("te", "から", "", "", "てから", "te kara", "n5"),
		new JpPat("nai", "でください", "", "", "ないでください", "nai de kudasai", "n5"),
		new JpPat("ta", "ことがある", "ta", "ことがない", "たことがある", "ta koto ga aru", "n5"),
		new JpPat("plain", "ことがある", "nai", "ことがない", "ことがある（习惯）", "koto ga aru", "n5"),
		new JpPat("plain", "まえに", "", "", "まえに", "mae ni", "n5"),
		new JpPat("ta", "あとで", "", "", "あとで", "ato de", "n5"),
		new JpPat("plain", "とき", "nai", "とき", "とき", "toki", "n5"),

		// ── N4 ──
		new JpPat("te", "あげる", "", "", "てあげる", "te ageru", "n4"),
		new JpPat("te", "くれる", "", "", "てくれる", "te kureru", "n4"),
		new JpPat("te", "もらう", "", "", "てもらう", "te morau", "n4"),
		new JpPat("te", "やる", "", "", "てやる", "te yaru", "n4"),
		new JpPat("te", "さしあげる", "", "", "てさしあげる", "te sashiageru", "n4"),
		new JpPat("te", "くださる", "", "", "てくださる", "te kudasaru", "n4"),
		new JpPat("te", "いただく", "", "", "ていただく", "te itadaku", "n4"),
		new JpPat("te", "ほしい", "nai", "でほしい", "てほしい", "te hoshii", "n4"),
		new JpPat("plain", "ことにする", "nai", "ことにしない", "ことにする", "koto ni suru", "n4"),
		new JpPat("plain", "ことになる", "nai", "ことにならない", "ことになる", "koto ni naru", "n4"),
		new JpPat("plain", "ようにする", "nai", "ようにする", "ようにする", "you ni suru", "n4"),
		new JpPat("plain", "ようになる", "nai", "ようになる", "ようになる", "you ni naru", "n4"),
		new JpPat("ta", "ばかり", "", "", "たばかり", "ta bakari", "n4"),
		new JpPat("ta", "ところだ", "", "", "たところだ", "ta tokoro da", "n4"),
		new JpPat("cau", "ください", "", "", "させてください", "sasete kudasai", "n4"),
		new JpPat("cau", "いただく", "", "", "させていただく", "sasete itadaku", "n4"),
		new JpPat("plain", "ので", "nai", "ので", "ので", "node", "n4"),
		new JpPat("plain", "のに", "nai", "のに", "のに", "noni", "n4"),
		new JpPat("te", "も", "", "", "ても", "te mo", "n4"),
		new JpPat("plain", "と", "nai", "と", "と（自然条件）", "to", "n4"),
		new JpPat("plain", "なら", "nai", "なら", "なら", "nara", "n4"),

		// ── N3 ──
		new JpPat("plain", "ために", "nai", "ために", "ために", "tame ni", "n3"),
		new JpPat("plain", "ように", "nai", "ように", "ように（目的）", "you ni", "n3"),
		new JpPat("plain", "しかない", "", "", "しかない", "shika nai", "n3"),
		new JpPat("ta", "まま", "nai", "まま", "まま", "mama", "n3"),
		new JpPat("plain", "うちに", "nai", "うちに", "うちに", "uchi ni", "n3"),
		new JpPat("teiru", "あいだに", "", "", "あいだに", "aida ni", "n3"),
		new JpPat("plain", "たびに", "nai", "たびに", "たびに", "tabi ni", "n3"),
		new JpPat("ta", "ついでに", "", "", "ついでに", "tsuide ni", "n3"),
		new JpPat("ta", "とたん", "", "", "とたん", "totan", "n3"),
		new JpPat("teiru", "最中", "", "", "最中", "saichuu", "n3"),
		new JpPat("plain", "ばかりか", "nai", "ばかりか", "ばかりか", "bakari ka", "n3"),
		new JpPat("plain", "どころか", "nai", "どころか", "どころか", "dokoro ka", "n3"),
		new JpPat("bunzen", "ずに", "", "", "ずに", "zu ni", "n3"),
		new JpPat("plain", "かわりに", "nai", "かわりに", "かわりに", "kawari ni", "n3"),
		new JpPat("plain", "とおりに", "naistem", "ないとおりに", "とおりに", "toori ni", "n3"),
		new JpPat("ren", "っぱなし", "", "", "っぱなし", "ppanashi", "n3"),

		// ── N2 ──
		new JpPat("bunzen", "ざるを得ない", "", "", "ざるを得ない", "zaru o enai", "n2"),
		new JpPat("nai", "わけにはいかない", "", "", "ないわけにはいかない", "nai wake ni wa ikanai", "n2"),
		new JpPat("nai", "ではいられない", "", "", "ないではいられない", "nai de wa irarenai", "n2"),
		new JpPat("te", "ならない", "", "", "てならない", "te naranai", "n2"),
		new JpPat("te", "たまらない", "", "", "てたまらない", "te tamaranai", "n2"),
		new JpPat("te", "しょうがない", "", "", "てしょうがない", "te shou ga nai", "n2"),
		new JpPat("te", "仕方がない", "", "", "て仕方がない", "te shikata ga nai", "n2"),
		new JpPat("ren", "かねる", "", "", "かねる", "kaneru", "n2"),
		new JpPat("ren", "かねない", "", "", "かねない", "kanenai", "n2"),
		new JpPat("ren", "っこない", "", "", "っこない", "kkonai", "n2"),
		new JpPat("ren", "ようがない", "", "", "ようがない", "you ga nai", "n2"),
		new JpPat("plain", "べく", "", "", "べく", "beku", "n2"),
		new JpPat("plain", "まい", "", "", "まい", "mai", "n2"),
		new JpPat("ta", "ものの", "", "", "ものの", "mono no", "n2"),
		new JpPat("plain", "とはいえ", "", "", "とはいえ", "to wa ie", "n2"),
		new JpPat("plain", "にもかかわらず", "nai", "にもかかわらず", "にもかかわらず", "ni mo kakawarazu", "n2"),
		new JpPat("plain", "くせに", "nai", "くせに", "くせに", "kuse ni", "n2"),
		new JpPat("plain", "わりに", "nai", "わりに", "わりに", "wari ni", "n2"),
		new JpPat("plain", "にしては", "nai", "にしては", "にしては", "ni shite wa", "n2"),
		new JpPat("ren", "つつも", "", "", "つつも", "tsutsu mo", "n2"),
		new JpPat("ren", "ながらも", "", "", "ながらも", "nagara mo", "n2"),
		new JpPat("plain", "どころではない", "nai", "どころではない", "どころではない", "dokoro de wa nai", "n2"),
		new JpPat("ta", "ばかりに", "", "", "ばかりに", "bakari ni", "n2"),
		new JpPat("ta", "あげく", "", "", "あげく", "ageku", "n2"),
		new JpPat("ta", "すえに", "", "", "すえに", "sue ni", "n2"),
		new JpPat("ta", "せいで", "nai", "せいで", "せいで", "sei de", "n2"),
		new JpPat("plain", "ものだから", "nai", "ものだから", "ものだから", "mono dakara", "n2"),
		new JpPat("ren", "次第", "", "", "次第", "shidai", "n2"),
		new JpPat("plain", "やいなや", "", "", "やいなや", "ya ina ya", "n2"),
		new JpPat("ta", "が早いか", "", "", "が早いか", "ga hayai ka", "n2"),
		new JpPat("plain", "そばから", "", "", "そばから", "soba kara", "n2"),
		new JpPat("ta", "かと思うと", "", "", "かと思うと", "ka to omou to", "n2"),
		new JpPat("te", "からでないと", "", "", "てからでないと", "te kara de nai to", "n2"),
		new JpPat("te", "はじめて", "", "", "てはじめて", "te hajimete", "n2"),
		new JpPat("te", "以来", "", "", "て以来", "te irai", "n2"),
		new JpPat("te", "こそ", "", "", "てこそ", "te koso", "n2"),
		new JpPat("plain", "としたら", "nai", "としたら", "としたら", "to shitara", "n2"),
		new JpPat("plain", "にしても", "nai", "にしても", "にしても", "ni shite mo", "n2"),
		new JpPat("ren", "さえすれば", "", "", "さえ…ば", "sae sureba", "n2"),

		// ── N1 ──
		new JpPat("bunzen", "んばかり", "", "", "んばかり", "n bakari", "n1"),
		new JpPat("plain", "とばかり", "", "", "とばかり", "to bakari", "n1"),
		new JpPat("bunzen", "んがため", "", "", "んがため", "n ga tame", "n1"),
		new JpPat("plain", "まじき", "", "", "まじき", "majiki", "n1"),
		new JpPat("plain", "かたわら", "", "", "かたわら", "katawara", "n1"),
		new JpPat("ta", "ものを", "", "", "ものを", "mono o", "n1"),
		new JpPat("plain", "ことなしに", "nai", "ことなしに", "ことなしに", "koto nashi ni", "n1"),
		new JpPat("ta", "が最後", "", "", "が最後", "ga saigo", "n1"),
		new JpPat("bunzen", "ずにはおかない", "", "", "ずにはおかない", "zu ni wa okanai", "n1"),
		new JpPat("bunzen", "ずにはすまない", "", "", "ずにはすまない", "zu ni wa sumanai", "n1"),
		new JpPat("nai", "ではすまない", "", "", "ないではすまない", "nai de wa sumanai", "n1"),
		new JpPat("te", "やまない", "", "", "てやまない", "te yamanai", "n1"),
		new JpPat("plain", "べからず", "", "", "べからず", "bekarazu", "n1"),

		// ── 文语（N0）──
		new JpPat("naistem", "ず", "", "", "ず（文语否定）", "zu", "bun"),
		new JpPat("bunzen", "ざる", "", "", "ざる", "zaru", "bun"),
		new JpPat("bunzen", "ぬ", "", "", "ぬ", "nu", "bun"),
		new JpPat("ren", "き", "", "", "き（过去）", "ki", "bun"),
		new JpPat("ren", "けり", "", "", "けり", "keri", "bun"),
		new JpPat("ren", "たり", "", "", "たり（完了）", "tari", "bun"),
		new JpPat("ren", "り", "", "", "り（存续）", "ri", "bun"),
		new JpPat("ren", "む", "", "", "む（推量）", "mu", "bun"),
		new JpPat("ren", "らむ", "", "", "らむ", "ramu", "bun"),
		new JpPat("ren", "けむ", "", "", "けむ", "kemu", "bun"),
		new JpPat("plain", "べし", "", "", "べし", "beshi", "bun"),
		new JpPat("plain", "まじ", "", "", "まじ", "maji", "bun"),
		new JpPat("plain", "なり", "", "", "なり（断定）", "nari", "bun"),
		new JpPat("ren", "たまふ", "", "", "たまふ", "tamau", "bun"),
		new JpPat("ren", "給ふ", "", "", "給ふ", "tamau (kanji)", "bun"),
		new JpPat("ren", "候ふ", "", "", "候ふ", "saburau", "bun"),
		new JpPat("plain", "ごとし", "", "", "ごとし", "gotoshi", "bun"),
		new JpPat("plain", "ごとく", "", "", "ごとく", "gotoku", "bun"),

		// ── 复合动词 ──
		new JpPat("ren", "直す", "", "", "直す（重新）", "naosu", "comp"),
		new JpPat("ren", "慣れる", "", "", "慣れる", "nareru", "comp"),
		new JpPat("ren", "忘れる", "", "", "忘れる", "wasureru", "comp"),
		new JpPat("ren", "遅れる", "", "", "遅れる", "okureru", "comp"),
		new JpPat("ren", "抜く", "", "", "抜く（坚持到底）", "nuku", "comp"),
		new JpPat("ren", "込む", "", "", "込む", "komu", "comp"),
		new JpPat("ren", "立てる", "", "", "立てる", "tateru", "comp"),
		new JpPat("ren", "付ける", "", "", "付ける（习惯）", "tsukeru", "comp"),
		new JpPat("ren", "合わせる", "", "", "合わせる", "awaseru", "comp"),
		new JpPat("ren", "回す", "", "", "回す", "mawasu", "comp"),
		new JpPat("ren", "まくる", "", "", "まくる", "makuru", "comp"),
		new JpPat("ren", "通す", "", "", "通す", "toosu", "comp"),
		new JpPat("ren", "出す", "", "", "出す（开始）", "dasu", "comp"),
		new JpPat("ren", "始める", "", "", "始める", "hajimeru", "comp"),
		new JpPat("ren", "終わる", "", "", "終わる", "owaru", "comp"),
		new JpPat("ren", "続ける", "", "", "続ける", "tsuzukeru", "comp"),
		new JpPat("ren", "上がる", "", "", "上がる", "agaru", "comp"),
		new JpPat("ren", "下がる", "", "", "下がる", "sagaru", "comp"),
		new JpPat("ren", "そびれる", "", "", "そびれる", "sobireru", "comp"),
		new JpPat("ren", "かねる", "", "", "かねる", "kaneru", "comp"),
		new JpPat("ren", "あぐねる", "", "", "あぐねる", "aguneru", "comp"),
		new JpPat("ren", "尽くす", "", "", "尽くす", "tsukusu", "comp"),
	};

	/// <summary>接续类型取基础形。</summary>
	static string stem(JpPatForms f, string conn) => conn switch {
		"te" => f.Te,
		"ren" => f.Ren,
		"nai" => f.Nai,
		"naistem" => f.Naistem,
		"bunzen" => f.Bunzen,
		"plain" => f.Plain,
		"ta" => f.Ta,
		"cau" => f.Cau,
		"teiru" => f.Te + "いる",
		_ => f.Plain,
	};

	/// <summary>按分组生成句型行。分组名随界面语言。</summary>
	public static List<ConjRow> Rows(bool zh, JpPatForms f) {
		var rows = new List<ConjRow>(PATS.Length);
		foreach (var p in PATS) {
			var g = grpname(p.Grp, zh);
			var pos = stem(f, p.Conn) + p.Pos;
			var neg = p.NegConn.Length == 0 ? "" : stem(f, p.NegConn) + p.Neg;
			rows.Add(new ConjRow(zh ? p.Zh : p.En, neg.Length > 0 ? pos + "、" + neg : pos, g));
		}
		return rows;
	}

	/// <summary>形容词和名词能接的句型（N5~N1）。接续按词性算，只出接得上的组合。</summary>
	public static List<ConjRow> PosRows(bool zh, string pos, string word) {
		var i = pos == "i";
		var n = pos == "n";
		// いい・かっこいい 的词干是「よ」（よく・よかった），其余去掉词尾的い。
		var s = i ? (word.EndsWith("いい") && word != "かわいい" && word != "可愛い" ? word.Substring(0, word.Length - 2) + "よ" : (word.EndsWith("い") ? word.Substring(0, word.Length - 1) : word)) : word;
		var plain = i ? word : word + "だ";
		var ku = i ? s + "く" : word + "で";
		var te = i ? s + "くて" : word + "で";
		var ta = i ? s + "かった" : word + "だった";
		var nai = i ? s + "くない" : word + "ではない";
		var aNa = i ? "" : "な";
		var aNo = i ? "" : (n ? "の" : "な");
		var rows = new List<ConjRow>();
		void add(string grp, string zh2, string en2, string v) {
			if (v.Length > 0) rows.Add(new ConjRow(zh ? zh2 : en2, v, grpname(grp, zh)));
		}
		// N5
		add("n5", "てもいい", "te mo ii", i ? ku + "てもいい" : word + "でもいい");
		add("n5", "てはいけない", "te wa ikenai", te + "はいけない");
		add("n5", "なくてもいい", "nakute mo ii", i ? ku + "なくてもいい" : word + "じゃなくてもいい");
		add("n5", "なければならない", "nakereba naranai", i ? ku + "なければならない" : word + "でなければならない");
		add("n5", "とき", "toki", i ? plain + "とき、" + nai + "とき" : word + aNo + "とき");
		add("n5", "ので", "node", i ? plain + "ので、" + nai + "ので" : word + aNa + "ので");
		add("n5", "のに", "noni", i ? plain + "のに" : word + aNa + "のに");
		add("n5", "ても", "te mo", i ? ku + "ても" : te + "も");
		add("n5", "と（自然条件）", "to", i ? plain + "と" : word + "だと");
		add("n5", "なら", "nara", i ? plain + "なら" : word + "なら");
		add("n5", "ことがある", "koto ga aru", i ? ta + "ことがある、" + plain + "ことがある" : ta + "ことがある");
		// N4
		add("n4", "そうだ", "souda", i ? plain + "そうだ" : plain + "そうだ");
		add("n4", "らしい", "rashii", i ? plain + "らしい" : word + "らしい");
		add("n4", "ようだ", "you da", i ? plain + "ようだ" : word + aNo + "ようだ");
		add("n4", "みたいだ", "mitai da", i ? plain + "みたいだ" : word + "みたいだ");
		add("n4", "かもしれない", "ka mo shirenai", i ? plain + "かもしれない" : word + "かもしれない");
		add("n4", "はずだ", "hazu da", i ? plain + "はずだ" : word + aNo + "はずだ");
		add("n4", "わけだ", "wake da", i ? plain + "わけだ" : word + aNa + "わけだ");
		add("n4", "ようになる", "you ni naru", i ? ku + "なる" : word + "になる");
		add("n4", "ようにする", "you ni suru", i ? ku + "なるようにする" : word + "になるようにする");
		// N3
		add("n3", "ために", "tame ni", i ? plain + "ために" : word + aNo + "ために");
		add("n3", "ように（目的）", "you ni", i ? ku + "なるように" : word + "になるように");
		add("n3", "まま", "mama", i ? plain + "まま" : word + aNo + "まま");
		add("n3", "うちに", "uchi ni", i ? plain + "うちに" : word + aNo + "うちに");
		add("n3", "ばかりか", "bakari ka", i ? plain + "ばかりか" : word + "ばかりか");
		add("n3", "どころか", "dokoro ka", i ? plain + "どころか" : word + "どころか");
		add("n3", "かわりに", "kawari ni", i ? plain + "かわりに" : word + aNo + "かわりに");
		// N2
		add("n2", "くせに", "kuse ni", i ? plain + "くせに" : word + aNo + "くせに");
		add("n2", "わりに", "wari ni", i ? plain + "わりに" : word + aNo + "わりに");
		add("n2", "にしては", "ni shite wa", i ? plain + "にしては" : word + "にしては");
		add("n2", "ものの", "mono no", ta + "ものの");
		add("n2", "とはいえ", "to wa ie", i ? plain + "とはいえ" : word + "とはいえ");
		add("n2", "せいで", "sei de", i ? plain + "せいで" : word + aNo + "せいで");
		add("n2", "ものだから", "mono dakara", i ? plain + "ものだから" : word + aNa + "ものだから");
		add("n2", "にもかかわらず", "ni mo kakawarazu", i ? plain + "にもかかわらず" : word + "にもかかわらず");
		add("n2", "どころではない", "dokoro de wa nai", i ? plain + "どころではない" : word + "どころではない");
		add("n2", "一方だ", "ippou da", i ? ku + "なる一方だ" : word + "になる一方だ");
		add("n2", "ばかりだ", "bakari da", i ? ku + "なるばかりだ" : word + "になるばかりだ");
		add("n2", "に違いない", "ni chigai nai", i ? plain + "に違いない" : word + "に違いない");
		// N1
		add("n1", "ものを", "mono o", ta + "ものを");
		return rows;
	}

	static string grpname(string key, bool zh) => key switch {
		"n5" => zh ? "N5 句型" : "N5 patterns",
		"n4" => zh ? "N4 句型" : "N4 patterns",
		"n3" => zh ? "N3 句型" : "N3 patterns",
		"n2" => zh ? "N2 句型" : "N2 patterns",
		"n1" => zh ? "N1 句型" : "N1 patterns",
		"bun" => zh ? "文语（N0）" : "Classical (N0)",
		"comp" => zh ? "复合动词" : "Compound verbs",
		_ => key,
	};
}
