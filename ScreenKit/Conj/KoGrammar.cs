using System.Collections.Generic;

namespace ScreenKit;

/// <summary>分级语法拼词时用到的基础形。由动词变形引擎算好传进来。</summary>
sealed class KoPatForms {
	public string Stem;
	public string Ae;
	public string Past;
	public string Eu;
	public string Eud;
	public string Rl;
	public string Nn;
	public string Neun;
	public string Drop;
	public string Mod;
	public string Gi;
	public string Mieum;
	public string Plain;
	public string Hap;
	public string Hapq;
	public string Seyo;
	public string Sip;
	public string Bsida;
	public string Hao;
	public string Nde;
	public string Saop;
	public bool Verb;
	public bool Adj;
}

/// <summary>把分级语法接到当前词上。动词、形容词走词干，名词走原词。</summary>
static class KoGrammar {
	public static List<ConjRow> VerbRows(int li, KoPatForms f) {
		var rows = new List<ConjRow>();
		foreach (var c in KoGrammarCatalog.Verb) {
			if (!fits(c, f.Verb, f.Adj)) continue;
			var text = apply(f, c[1]);
			if (text.Length == 0) continue;
			rows.Add(new ConjRow(label(c, li), withneg(f, c[1], text), grp(c[0], li)));
		}
		return rows;
	}

	// 语法条目本身已是完整说法，只给能安全推导的接续加长否定。
	// 안 不能套在复合结构前面（안 -고 싶다 不成立），所以只加「-지 않-」。
	static string withneg(KoPatForms f, string pos, string text) {
		if (text.IndexOf(" / ", System.StringComparison.Ordinal) >= 0) return text;
		if (text.IndexOf("않", System.StringComparison.Ordinal) >= 0) return text;
		if (text.IndexOf("못", System.StringComparison.Ordinal) >= 0) return text;
		var tail = negtail(pos);
		if (tail == null) return text;
		return text + " / " + f.Stem + "지 않" + tail;
	}

	// 把接续换成「-지 않-」后接同样的尾巴。含固定谓词的复合语法（싶다 / 있다 / 하다 等）
	// 否定要改的是后面那个谓词，套不上这条规则，返回 null 保持原样。
	static string negtail(string pos) {
		var plus = pos.IndexOf('+');
		var key = plus < 0 ? pos : pos.Substring(0, plus);
		var tail = plus < 0 ? "" : pos.Substring(plus + 1);
		if (haspred(pos)) return null;
		switch (key) {
			case "stem": return tail;
			case "ae": return "아" + tail;
			// 「-았/었-」本身只是词尾片段，接成 먹지 않았 会断在半截，保持原样
			case "past": return null;
			case "eu": return "으" + tail;
			case "eud": return "으" + tail;
			case "rl": return "을" + tail;
			case "nn": return "은" + tail;
			case "neun": return "는" + tail;
			case "gi": return "기" + tail;
			case "mieum": return "음" + tail;
			case "mod": return "는" + tail;
			case "drop": return tail;
			case "plain": return tail;
			case "hap": return "습니다";
			// 这几条词尾本身就是完整的否定形，不能再叠一层「지 않」
			case "sip": return null;
			case "seyo": return null;
			case "bsida": return null;
			case "an": return null;
			case "mot": return null;
			case "hao": return "소";
			case "nde": return "는데" + tail;
			default: return null;
		}
	}

	// 尾部谓词固定写死的语法，否定落点不在主动词上，不套用通用规则
	static bool haspred(string pos) {
		string[] fixedpred = { "싶다", "싶어 하다", "있다", "없다", "하다", "되다", "보다", "주다",
			"모르다", "알다", "말다", "드리다", "버리다", "놓다", "두다", "내다", "나다", "대다",
			"만들다", "보이다", "지다", "쉽다", "어렵다", "마련이다", "틀림없다", "법하다", "그래요" };
		foreach (var w in fixedpred) {
			if (pos.IndexOf(w, System.StringComparison.Ordinal) >= 0) return true;
		}
		return false;
	}

	public static List<ConjRow> NounRows(int li, string noun, bool bat) {
		var rows = new List<ConjRow>();
		foreach (var c in KoGrammarCatalog.Noun) {
			var text = applynoun(noun, bat, c[1], c[2]);
			if (text.Length == 0) continue;
			rows.Add(new ConjRow(label(c, li), withnegNoun(noun, bat, c[1], text), grp(c[0], li)));
		}
		return rows;
	}

	// 名词里只有判断句式（예요 / 입니다 / 입니까）能换成否定，助词本身没有否定说法
	static string withnegNoun(string noun, bool bat, string pos, string text) {
		if (text.IndexOf("아니", System.StringComparison.Ordinal) >= 0) return text;
		var p = noun + (bat ? "이" : "가") + " 아니";
		switch (pos) {
			case "ieyo": return text + " / " + p + "에요";
			case "noun+입니다": return text + " / " + p + "입니다";
			case "noun+입니까": return text + " / " + p + "입니까";
			default: return text;
		}
	}

	static bool fits(string[] c, bool verb, bool adj) {
		var who = c[4];
		if (who == "v") return verb;
		if (who == "a") return adj;
		return true;
	}

	static string label(string[] c, int li) {
		if (li == 0) return c[2] + "　" + c[3];
		if (li == 1) return c[2];
		return c[2];
	}

	static string grp(string level, int li) {
		if (level == "其它") return li == 0 ? "其它语法" : (li == 1 ? "기타 문법" : "Other grammar");
		if (li == 0) return level + " 级语法";
		if (li == 1) return level + "급 문법";
		return "Level " + level + " grammar";
	}

	static string apply(KoPatForms f, string pos) {
		var plus = pos.IndexOf('+');
		var key = plus < 0 ? pos : pos.Substring(0, plus);
		var tail = plus < 0 ? "" : pos.Substring(plus + 1);
		string head;
		switch (key) {
			case "stem": head = f.Stem; break;
			case "ae": head = f.Ae; break;
			case "past": head = f.Past; break;
			case "eu": head = f.Eu; break;
			case "eud": head = f.Eud; break;
			case "rl": head = f.Rl; break;
			case "nn": head = f.Nn; break;
			case "neun": head = f.Neun; break;
			case "drop": head = f.Drop; break;
			case "mod": head = f.Mod; break;
			case "gi": head = f.Gi; break;
			case "mieum": head = f.Mieum; break;
			case "plain": head = f.Plain; break;
			case "hap": head = f.Hap; break;
			case "hapq": head = f.Hapq; break;
			case "seyo": head = f.Seyo; break;
			case "sip": head = f.Sip; break;
			case "bsida": head = f.Bsida; break;
			case "hao": head = f.Hao; break;
			case "nde": head = f.Nde; break;
			default: return special(f, pos);
		}
		if (head.Length == 0) return "";
		return join(head, tail);
	}

	// 尾巴带「 / 」时每一段都要各自接上词头，所以并行形式在目录里要写全（적이 있다 / 적이 없다）
	static string join(string head, string tail) {
		if (tail.Length == 0) return head;
		if (tail.IndexOf(" / ") >= 0) {
			var parts = tail.Split(new[] { " / " }, System.StringSplitOptions.None);
			for (var i = 0; i < parts.Length; i++) parts[i] = head + parts[i];
			return string.Join(" / ", parts);
		}
		return head + tail;
	}

	static string special(KoPatForms f, string pos) {
		switch (pos) {
			case "an": return "안 " + f.Ae + "요";
			case "mot": return f.Verb ? "못 " + f.Ae + "요" : "";
			case "guna": return f.Verb ? f.Neun + "구나" : f.Stem + "구나";
			case "gunyo": return f.Verb ? f.Neun + "군요" : f.Stem + "군요";
			case "suitda": return f.Rl + " 수 있다 / " + f.Rl + " 수 없다";
			case "jeokitda": return f.Nn + " 적이 있다 / " + f.Nn + " 적이 없다";
			case "julmorda": return f.Rl + " 줄 알다 / " + f.Rl + " 줄 모르다";
			case "giswipda": return f.Gi + " 쉽다 / " + f.Gi + " 어렵다";
			case "ryeoneuncham": return f.Eu + "려는 참이다";
			case "njiEolma": return f.Nn + " 지 얼마 안 되다";
			case "dagabomyeon": return f.Drop + "다가 보면";
			case "jianeursueopda": return f.Stem + "지 않을 수 없다";
			case "mod3": return f.Nn + " / " + f.Mod + " / " + f.Rl;
			case "si": return f.Eud + "시다";
			case "qplain": return f.Adj ? f.Eud + "냐" : f.Drop + "느냐";
			case "qnya": return f.Adj ? f.Eud + "냐고 하다" : f.Drop + "느냐고 하다";
			case "naboda": return f.Adj ? f.Nn + "가 보다" : f.Drop + "나 보다";
			case "nji": return f.Adj ? f.Nn + "지 알다 / 모르다" : f.Drop + "는지 알다 / 모르다";
			case "ppun": return f.Rl + " 뿐만 아니라";
			case "ppunida": return f.Rl + " 뿐이다";
			case "keonyeong": return f.Gi + "는커녕";
			case "bulgu": return f.Mieum + "에도 불구하고";
			case "teullim": return f.Mieum + "에 틀림없다";
			case "mullon": return f.Mieum + "은 물론이다";
			case "modga": return f.Mod + (f.Adj ? "가 하면" : "가 하면");
			case "damyeonseo": return f.Plain + "면서요?";
			case "damyeon": return f.Plain.EndsWith("다") ? f.Plain.Substring(0, f.Plain.Length - 1) + "다면" : f.Plain + "면";
			case "dahese": return f.Plain + "고 해서";
			case "dason": return f.Plain.EndsWith("다") ? f.Plain.Substring(0, f.Plain.Length - 1) + "다손 치더라도" : f.Plain + "손 치더라도";
			case "daneun": return f.Plain.EndsWith("다") ? f.Plain.Substring(0, f.Plain.Length - 1) + "다는 것이다" : f.Plain + "는 것이다";
			case "daboda": return f.Plain.EndsWith("다") ? f.Plain.Substring(0, f.Plain.Length - 1) + "다기보다는" : f.Plain + "기보다는";
			case "dareum": return f.Gi + "에 다름 아니다";
			case "ijeon": return f.Gi + " 이전에";
			case "bichu": return f.Mieum + "에 비추어";
			case "jira": return f.Nn + "지라";
			case "rldae": return f.Rl + " 대로 " + f.Plain;
			case "geonman": return endsrieul(f) ? f.Drop + "련마는" : f.Stem + "건마는";
			case "dapsigo": return f.Plain.EndsWith("다") ? f.Plain.Substring(0, f.Plain.Length - 1) + "답시고" : f.Stem + "답시고";
			case "plainstem+다 보면": return f.Drop + "다 보면";
			case "plainstem+다가는": return f.Drop + "다가는";
			case "plainstem+다 못해": return f.Drop + "다 못해";
			case "plainstem+다 보니": return f.Drop + "다 보니";
			case "plainstem+다마다": return f.Drop + "다마다";
			case "quote4": return f.Plain.EndsWith("다") ? f.Plain.Substring(0, f.Plain.Length - 1) + "대요" : f.Plain + "요";
			case "yeo":
			case "irrb":
			case "irrd":
			case "irrs":
			case "irrh":
			case "irrr":
			case "irre":
			case "irrl":
			case "caus":
			case "pass":
				return "";
			case "ndedo": return f.Nde + "도 불구하고";
			case "giro": return f.Stem + "기로 하다";
			case "girodeul": return f.Stem + "기로 들면";
			case "gillae": return f.Stem + "길래";
			case "motge": return f.Verb ? "못 " + f.Stem + "게 하다" : "";
			case "hapdi": return f.Hap.EndsWith("니다") ? f.Hap.Substring(0, f.Hap.Length - 2) + "디다 / " + f.Hap.Substring(0, f.Hap.Length - 2) + "디까" : "";
			case "saop2": return f.Saop.Length == 0 ? "" : f.Saop.Substring(0, f.Saop.Length - 2) + "나이다 / " + f.Saop.Substring(0, f.Saop.Length - 2) + "소서";
			case "saoe": return f.Stem + "사외다";
			case "doda": return f.Verb ? f.Neun + "도다" : f.Stem + "도다";
			case "daneun+ 듯(이)": return (f.Plain.EndsWith("다") ? f.Plain.Substring(0, f.Plain.Length - 1) + "다는" : f.Plain + "는") + " 듯(이)";
			default: return "";
		}
	}

	static bool endsrieul(KoPatForms f) => f.Rl.Length > 0 && f.Rl[f.Rl.Length - 1] == 'ㄹ' && f.Stem.Length > 0 && f.Stem[f.Stem.Length - 1] == 'ㄹ';

	static string applynoun(string noun, bool bat, string pos, string form) {
		var plus = pos.IndexOf('+');
		var key = plus < 0 ? pos : pos.Substring(0, plus);
		var tail = plus < 0 ? "" : pos.Substring(plus + 1);
		string head;
		switch (key) {
			case "noun": head = particle(noun, bat, bat && !endsrieuln(noun), form, ref tail); break;
			case "cop": head = noun + (bat && !endsrieuln(noun) ? "으" : "") + tail; tail = ""; break;
			case "copb": head = noun + (bat ? "이" : ""); break;
			case "eul": head = noun + (bat ? "을" : "를"); break;
			case "wa": head = noun + (bat ? "과" : "와"); break;
			case "topic": head = noun + (bat ? "은" : "는"); break;
			default: return specialn(noun, bat, pos);
		}
		return head + tail;
	}

	// 助词本身写在标签里：이/가、에、에게 / 한테。开音节取斜杠后，闭音节取斜杠前。
	static string particle(string noun, bool bat, bool eu, string form, ref string tail) {
		var body = form.StartsWith("-") ? form.Substring(1) : form;
		var bits = body.Split(new[] { " / " }, System.StringSplitOptions.None);
		for (var i = 0; i < bits.Length; i++) bits[i] = noun + pickpair(bat, eu, bits[i].Trim());
		tail = "";
		return string.Join(" / ", bits);
	}

	static string pickpair(bool bat, bool eu, string bit) {
		if (bit.StartsWith("(이)")) return (bat ? "이" : "") + bit.Substring(3);
		if (bit.StartsWith("(으)")) return (eu ? "으" : "") + bit.Substring(3);
		var slash = bit.IndexOf('/');
		if (slash <= 0) return bit;
		var left = bit.Substring(0, slash);
		var right = bit.Substring(slash + 1);
		if (left.Length == 0 || right.Length == 0 || left.Length > 2 || right.Length > 2) return bit;
		return bat ? left : right;
	}

	static bool endsrieuln(string noun) {
		if (noun.Length == 0) return false;
		var c = noun[noun.Length - 1] - 0xAC00;
		if (c < 0 || c > 11171) return false;
		return c % 28 == 8;
	}

	static string specialn(string noun, bool bat, string pos) {
		switch (pos) {
			case "and": return noun + (bat ? "과" : "와") + ", " + noun + "하고, " + noun + (bat ? "이랑" : "랑");
			case "bare": return "하다못해";
			case "ieyo": return noun + (bat ? "이에요" : "예요");
			case "anida": return noun + (bat ? "이" : "가") + " 아니에요";
			case "copb+라고 하다": return noun + (bat ? "이라고" : "라고") + " 하다";
			case "quote4": return noun + (bat ? "이래요" : "래요");
			case "ppun": return noun + (bat ? "일" : "일") + " 뿐만 아니라";
			case "ppunida": return noun + "일 뿐이다";
			case "keonyeong": return noun + (bat ? "은" : "는") + "커녕";
			case "bulgu": return noun + "임에도 불구하고";
			case "teullim": return noun + "임에 틀림없다";
			case "mullon": return noun + "임은 물론이다";
			case "damyeonseo": return noun + (bat ? "이라면서요?" : "라면서요?");
			case "damyeon": return noun + (bat ? "이라면" : "라면");
			case "dahese": return noun + (bat ? "이라고 해서" : "라고 해서");
			case "daboda": return noun + (bat ? "이라기보다는" : "라기보다는");
			case "dareum": return noun + "임에 다름 아니다";
			case "ijeon": return noun + "이기 이전에";
			case "doda": return noun + (bat ? "이로다" : "로다");
			default: return "";
		}
	}
}
