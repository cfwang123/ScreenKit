(function(){
	var ZHS = "零一二三四五六七八九";
	var ZHB = "零壹贰叁肆伍陆柒捌玖";
	var ZU = ["千", "百", "十", ""];
	var ZB = ["仟", "佰", "拾", ""];
	var ZG = ["", "万", "亿", "万亿"];
	// 小数逢十，中数逢万万，大数自乘。万在三套里都是 10^4。
	var BIGU = ["万", "亿", "兆", "京", "垓", "秭", "穰", "沟", "涧", "正", "载", "极", "恒河沙", "阿僧祇", "那由他", "不可思议", "无量大数"];
	var FRACU = ["分", "厘", "毫", "丝", "忽", "微", "纤", "沙", "尘", "埃", "渺", "漠", "模糊", "逡巡", "须臾", "瞬息", "弹指", "刹那", "六德", "虚空", "清净", "阿赖耶", "阿摩罗", "涅槃寂静"];
	var EN1 = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"];
	var EN10 = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];
	var ENS = ["", " thousand", " million", " billion", " trillion", " quadrillion"];
	var SUP = { "0": "⁰", "1": "¹", "2": "²", "3": "³", "4": "⁴", "5": "⁵", "6": "⁶", "7": "⁷", "8": "⁸", "9": "⁹" };
	// 每千进一位，从 10^-30 到 10^30。下标 10 是个位，没有词头。
	var SI = [
		["quecto", "q"], ["ronto", "r"], ["yocto", "y"], ["zepto", "z"], ["atto", "a"],
		["femto", "f"], ["pico", "p"], ["nano", "n"], ["micro", "μ"], ["milli", "m"],
		null,
		["kilo", "k"], ["mega", "M"], ["giga", "G"], ["tera", "T"], ["peta", "P"],
		["exa", "E"], ["zetta", "Z"], ["yotta", "Y"], ["ronna", "R"], ["quetta", "Q"]
	];
	var ROM = [[1000, "M"], [900, "CM"], [500, "D"], [400, "CD"], [100, "C"], [90, "XC"], [50, "L"], [40, "XL"], [10, "X"], [9, "IX"], [5, "V"], [4, "IV"], [1, "I"]];
	var KO_N10 = ["", "열", "스물", "서른", "마흔", "쉰", "예순", "일흔", "여든", "아흔"];
	var KO_N1 = ["", "하나", "둘", "셋", "넷", "다섯", "여섯", "일곱", "여덟", "아홉"];
	var KO_ND = ["영", "하나", "둘", "셋", "넷", "다섯", "여섯", "일곱", "여덟", "아홉"];
	var KO_D = ["영", "일", "이", "삼", "사", "오", "육", "칠", "팔", "구"];
	var KO_U4 = ["천", "백", "십", ""];
	var KO_SC = ["", "만", "억", "조", "경", "해", "자", "양", "구", "간", "정", "재", "극", "항하사", "아승기", "나유타", "불가사의", "무량대수"];
	var JA_D = ["", "いち", "に", "さん", "よん", "ご", "ろく", "なな", "はち", "きゅう"];
	var JA_DIG = ["れい", "いち", "に", "さん", "よん", "ご", "ろく", "なな", "はち", "きゅう"];
	var JA_SC = ["", "まん", "おく", "ちょう", "けい", "がい", "じょ", "じょう", "こう", "かん", "せい", "さい", "ごく", "ごうがしゃ", "あそうぎ", "なゆた", "ふかしぎ", "むりょうたいすう"];
	var DE_U = ["", "ein", "zwei", "drei", "vier", "fünf", "sechs", "sieben", "acht", "neun"];
	var DE_DIG = ["null", "eins", "zwei", "drei", "vier", "fünf", "sechs", "sieben", "acht", "neun"];
	var DE_BIG = [null, null, ["Million", "Millionen"], ["Milliarde", "Milliarden"], ["Billion", "Billionen"], ["Billiarde", "Billiarden"], ["Trillion", "Trillionen"]];
	var FR_D = ["zéro", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf"];
	var FR_TEEN = ["dix", "onze", "douze", "treize", "quatorze", "quinze", "seize"];
	var FR_TENS = ["", "", "vingt", "trente", "quarante", "cinquante", "soixante"];
	var FR_SC = ["", "", "million", "milliard", "billion", "billiard", "trillion", "trilliard"];
	var ES_1 = ["", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve", "diez", "once", "doce", "trece", "catorce", "quince", "dieciséis", "diecisiete", "dieciocho", "diecinueve"];
	var ES_20 = ["", "", "veintidós", "veintitrés", "veinticuatro", "veinticinco", "veintiséis", "veintisiete", "veintiocho", "veintinueve"];
	var ES_10 = ["", "", "veinte", "treinta", "cuarenta", "cincuenta", "sesenta", "setenta", "ochenta", "noventa"];
	var ES_100 = ["", "ciento", "doscientos", "trescientos", "cuatrocientos", "quinientos", "seiscientos", "setecientos", "ochocientos", "novecientos"];
	var ES_LL = [["millón", "millones"], ["billón", "billones"], ["trillón", "trillones"], ["cuatrillón", "cuatrillones"]];
	var ES_DIG = ["cero", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve"];
	var PT_1 = ["", "um", "dois", "três", "quatro", "cinco", "seis", "sete", "oito", "nove", "dez", "onze", "doze", "treze", "catorze", "quinze", "dezesseis", "dezessete", "dezoito", "dezenove"];
	var PT_10 = ["", "", "vinte", "trinta", "quarenta", "cinquenta", "sessenta", "setenta", "oitenta", "noventa"];
	var PT_100 = ["", "cento", "duzentos", "trezentos", "quatrocentos", "quinhentos", "seiscentos", "setecentos", "oitocentos", "novecentos"];
	var PT_BIG = [null, null, ["milhão", "milhões"], ["bilhão", "bilhões"], ["trilhão", "trilhões"], ["quatrilhão", "quatrilhões"], ["quintilhão", "quintilhões"], ["sextilhão", "sextilhões"], ["septilhão", "septilhões"], ["octilhão", "octilhões"]];
	var PT_DIG = ["zero", "um", "dois", "três", "quatro", "cinco", "seis", "sete", "oito", "nove"];
	var RU_M = ["", "один", "два", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять"];
	var RU_DIG = ["ноль", "один", "два", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять"];
	var RU_F = ["", "одна", "две", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять"];
	var RU_TEEN = ["десять", "одиннадцать", "двенадцать", "тринадцать", "четырнадцать", "пятнадцать", "шестнадцать", "семнадцать", "восемнадцать", "девятнадцать"];
	var RU_10 = ["", "", "двадцать", "тридцать", "сорок", "пятьдесят", "шестьдесят", "семьдесят", "восемьдесят", "девяносто"];
	var RU_100 = ["", "сто", "двести", "триста", "четыреста", "пятьсот", "шестьсот", "семьсот", "восемьсот", "девятьсот"];
	var RU_SC = [null, ["тысяча", "тысячи", "тысяч", true], ["миллион", "миллиона", "миллионов", false], ["миллиард", "миллиарда", "миллиардов", false], ["биллион", "биллиона", "биллионов", false], ["биллиард", "биллиарда", "биллиардов", false], ["триллион", "триллиона", "триллионов", false]];
	var RU_DEN1 = ["", "десятая", "сотая", "тысячная", "десятитысячная", "стотысячная", "миллионная", "десятимиллионная", "стомиллионная", "миллиардная"];
	var RU_DEN = ["", "десятых", "сотых", "тысячных", "десятитысячных", "стотысячных", "миллионных", "десятимиллионных", "стомиллионных", "миллиардных"];

	SK.regutil("u-numread", {
		title: "数字表记转换",
		go: "转换",
		noGo: true,
		hideOut: true,
		body: function(){
			return SK.lab("数字", SK.textin("box-in", "", "例如 12345.67"))
				+ '<p class="hint">小数逢十进，中数逢万万进，大数自乘。小数点后依次是分、厘、毫、丝。另有韩文固有、韩文汉字、日语，以及德、法、西、葡、俄。SI 每千进一位，如 kilo、mega、zepto、yocto。正整数另给英文多边形名，如 pentagon、hectogon、chiliagon、myriagon，到 1 亿边。</p>'
				+ '<div class="num-list" id="num-list"></div>';
		},
		open: function(){
			var box = SK.$("box-in");
			var list = SK.$("num-list");
			if (box) box.oninput = rerun;
			if (list && !list.getAttribute("data-bind")) {
				list.setAttribute("data-bind", "1");
				list.onclick = onlist;
			}
		},
		run: function(){
			return numread(SK.val("box-in"));
		}
	});

	function rerun(){
		if (!String(SK.val("box-in")).trim()) {
			paint([]);
			if (SK.$("box-out")) SK.$("box-out").value = "";
			SK.msg("box-msg", "");
			return;
		}
		SK.utilrun();
	}

	function onlist(ev){
		var btn = ev.target;
		var list = SK.$("num-list");
		while (btn && btn !== list && btn.tagName !== "BUTTON") btn = btn.parentNode;
		if (!btn || btn === list) return;
		SK.copytext(btn.getAttribute("data-v") || "", "box-msg");
	}

	function numread(text){
		var parsed;
		try {
			parsed = parsenum(text);
		}
		catch (ex) {
			paint([]);
			throw ex;
		}
		if (!parsed) {
			paint([]);
			return "";
		}
		var rows = makerows(parsed);
		paint(rows);
		return joinrows(rows);
	}

	function parsenum(text){
		var s = String(text).trim().replace(/[\s,，]/g, "");
		if (!s) return null;
		if (s.length > 220) throw new Error("太长");
		var neg = false;
		var c0 = s.charAt(0);
		if (c0 === "+" || c0 === "-" || c0 === "−") {
			neg = c0 !== "+";
			s = s.slice(1);
			if (!s || s === ".") return null;
		}
		if (s.charAt(s.length - 1) === ".") s = s.slice(0, -1);
		if (!s) return null;
		if (!/^\d+(\.\d+)?$/.test(s)) throw new Error("请输入数字");
		var parts = s.split(".");
		var ip = parts[0].replace(/^0+/, "") || "0";
		var fp = (parts[1] || "").replace(/0+$/, "");
		if (ip.length > 129) throw new Error("整数部分最多 129 位");
		if (fp.length > FRACU.length) throw new Error("小数部分最多 " + FRACU.length + " 位");
		if (ip === "0" && !fp) neg = false;
		return { neg: neg, ip: ip, fp: fp };
	}

	function makerows(p){
		var plain = sign(p) + p.ip + (p.fp ? "." + p.fp : "");
		var rows = [
			["数字", plain],
			["中文读法", zhread(p, false)],
			["中文大写", zhread(p, true)],
			["小数", scaleread(p, "small")],
			["中数", scaleread(p, "mid")],
			["大数", scaleread(p, "big")],
			["英文读法", enread(p)],
			["韩文固有", koNative(p)],
			["韩文汉字", koSino(p)],
			["日语", jaRead(p)],
			["德语", deRead(p)],
			["法语", frRead(p)],
			["西班牙语", esRead(p)],
			["葡萄牙语", ptRead(p)],
			["俄语", ruRead(p)],
			["SI", siread(p)],
			["千分位", sign(p) + group3(p.ip) + (p.fp ? "." + p.fp : "")],
			["科学计数", sciform(p)],
			["百分数", percent(p)],
			["罗马数字", romanof(p)],
			["多边形", polyof(p)],
			["二进制", baseof(p, 2)],
			["八进制", baseof(p, 8)],
			["十六进制", baseof(p, 16)]
		];
		return rows;
	}

	function sign(p){
		return p.neg ? "-" : "";
	}

	function zhread(p, formal){
		var body = zhint(p.ip, formal);
		if (p.fp) body += "点" + zhdigits(p.fp, formal);
		return (p.neg ? "负" : "") + body;
	}

	function zhint(ip, formal){
		if (ip === "0") return "零";
		var chunks = splitend(ip, 4);
		var body = "";
		var gap = false;
		var hi = chunks.length - 1;
		var gi;
		for (gi = hi; gi >= 0; gi--) {
			var sec = parseInt(chunks[gi], 10);
			if (!sec) {
				gap = true;
				continue;
			}
			var piece = read4(chunks[gi], formal, gi === hi);
			if (body && (gap || sec < 1000)) body += "零";
			body += piece + groupname(gi);
			gap = false;
		}
		return body;
	}

	// 小写只在最高一节省掉「一十」的「一」：10 是十，110000 是十一万；下一节仍写一十，如 100010 是十万零一十。大写保留壹拾。
	function read4(raw, formal, drop){
		var digs = formal ? ZHB : ZHS;
		var units = formal ? ZB : ZU;
		var s = ("0000" + raw).slice(-4);
		var out = "";
		var zero = false;
		var started = false;
		var i;
		for (i = 0; i < 4; i++) {
			var d = s.charCodeAt(i) - 48;
			if (!d) {
				if (started) zero = true;
				continue;
			}
			if (zero) out += "零";
			zero = false;
			var ch = digs.charAt(d);
			if (!formal && drop && !started && i === 2 && d === 1) ch = "";
			out += ch + units[i];
			started = true;
		}
		return out;
	}

	function groupname(i){
		if (i < ZG.length) return ZG[i];
		var s = i % 2 ? "万" : "";
		var n = Math.floor(i / 2);
		var k;
		for (k = 0; k < n; k++) s += "亿";
		return s;
	}

	function expsFor(mode){
		var exps = [];
		var e = 4;
		var i;
		for (i = 0; i < BIGU.length; i++) {
			exps.push(e);
			if (mode === "small") e += 1;
			else if (mode === "mid") e = i === 0 ? 8 : e + 8;
			else e = i === 0 ? 8 : e * 2;
		}
		return exps;
	}

	function scaleread(p, mode){
		var body = "";
		if (p.ip !== "0") body = readscale(p.ip, expsFor(mode));
		if (p.fp) {
			if (body) body += "又";
			body += readfrac(p.fp);
		}
		if (!body) body = "零";
		return (p.neg ? "负" : "") + body;
	}

	function readscale(ip, exps){
		if (ip === "0") return "零";
		return readat(ip, exps, true);
	}

	function readat(ip, exps, drop){
		var exp = -1;
		var name = "";
		var i;
		for (i = exps.length - 1; i >= 0; i--) {
			if (exps[i] < ip.length) {
				exp = exps[i];
				name = BIGU[i];
				break;
			}
		}
		if (exp < 0) return read4(ip, false, drop);
		var q = ip.slice(0, ip.length - exp).replace(/^0+/, "");
		var r = ip.slice(ip.length - exp).replace(/^0+/, "");
		var left = readat(q, exps, drop) + name;
		if (!r) return left;
		if (r.length < exp) left += "零";
		return left + readat(r, exps, false);
	}

	function readfrac(fp){
		var out = "";
		var zero = false;
		var started = false;
		var i;
		for (i = 0; i < fp.length; i++) {
			var d = fp.charCodeAt(i) - 48;
			if (!d) {
				if (started) zero = true;
				continue;
			}
			if (zero) out += "零";
			zero = false;
			out += ZHS.charAt(d) + FRACU[i];
			started = true;
		}
		return out;
	}

	function zhdigits(fp, formal){
		var digs = formal ? ZHB : ZHS;
		var out = "";
		var i;
		for (i = 0; i < fp.length; i++) out += digs.charAt(fp.charCodeAt(i) - 48);
		return out;
	}

	function enread(p){
		if (p.ip.length > 18) return (p.neg ? "negative " : "") + "超出";
		var body = enint(p.ip);
		if (p.fp) body += " point " + endigits(p.fp);
		return (p.neg ? "negative " : "") + body;
	}

	function enint(ip){
		if (ip === "0") return "zero";
		if (ip.length > 18) return "超出";
		var chunks = splitend(ip, 3);
		var parts = [];
		var i;
		for (i = chunks.length - 1; i >= 0; i--) {
			var n = parseInt(chunks[i], 10);
			if (!n) continue;
			parts.push(en999(n) + ENS[i]);
		}
		return parts.join(" ");
	}

	function en999(n){
		var s = "";
		if (n >= 100) {
			s = EN1[Math.floor(n / 100)] + " hundred";
			n = n % 100;
			if (n) s += " ";
		}
		if (n >= 20) {
			s += EN10[Math.floor(n / 10)];
			if (n % 10) s += "-" + EN1[n % 10];
		}
		else if (n) s += EN1[n];
		return s;
	}

	function endigits(fp){
		var out = [];
		var i;
		for (i = 0; i < fp.length; i++) out.push(EN1[fp.charCodeAt(i) - 48]);
		return out.join(" ");
	}

	function spellDigits(fp, words, sep){
		var out = [];
		var i;
		for (i = 0; i < fp.length; i++) out.push(words[fp.charCodeAt(i) - 48]);
		return out.join(sep || "");
	}

	function koNative(p){
		if (p.ip.length > 2) return "固有数词只到九十九";
		var n = parseInt(p.ip, 10);
		var body = n ? KO_N10[Math.floor(n / 10)] + KO_N1[n % 10] : "영";
		if (p.fp) body += " 점 " + spellDigits(p.fp, KO_ND);
		return (p.neg ? "마이너스 " : "") + body;
	}

	function koSino(p){
		if (p.ip.length > KO_SC.length * 4) return "超出";
		var body = koInt(p.ip);
		if (p.fp) body += " 점 " + spellDigits(p.fp, KO_D);
		return (p.neg ? "마이너스 " : "") + body;
	}

	function koInt(ip){
		if (ip === "0") return "영";
		var chunks = splitend(ip, 4);
		var parts = [];
		var i;
		for (i = chunks.length - 1; i >= 0; i--) {
			var n = parseInt(chunks[i], 10);
			if (!n) continue;
			parts.push(ko4(n) + KO_SC[i]);
		}
		return parts.join(" ");
	}

	// 十、百、千前的「일」省掉，万及以上保留。中间空位不补「영」。
	function ko4(n){
		var s = ("0000" + n).slice(-4);
		var out = "";
		var i;
		for (i = 0; i < 4; i++) {
			var d = s.charCodeAt(i) - 48;
			if (!d) continue;
			if (!(d === 1 && KO_U4[i])) out += KO_D[d];
			out += KO_U4[i];
		}
		return out;
	}

	function jaRead(p){
		if (p.ip.length > JA_SC.length * 4) return "超出";
		var body = p.ip === "0" ? "ゼロ" : jaInt(p.ip);
		if (p.fp) body += " てん " + spellDigits(p.fp, JA_DIG);
		return (p.neg ? "マイナス " : "") + body;
	}

	function jaInt(ip){
		var chunks = splitend(ip, 4);
		var parts = [];
		var i;
		for (i = chunks.length - 1; i >= 0; i--) {
			var n = parseInt(chunks[i], 10);
			if (!n) continue;
			parts.push(jaGroup(n, i));
		}
		return parts.join(" ");
	}

	// ちょう 只在整段正好是 いち / はち / じゅう 时促音。其余单位直接拼接。
	function jaGroup(n, i){
		var piece = ja4(n);
		if (i === 3) {
			if (piece === "いち") return "いっちょう";
			if (piece === "はち") return "はっちょう";
			if (piece === "じゅう") return "じゅっちょう";
		}
		return piece + JA_SC[i];
	}

	function ja4(n){
		var s = ("0000" + n).slice(-4);
		var d0 = s.charCodeAt(0) - 48;
		var d1 = s.charCodeAt(1) - 48;
		var d2 = s.charCodeAt(2) - 48;
		var d3 = s.charCodeAt(3) - 48;
		var out = "";
		if (d0 === 1) out += "せん";
		else if (d0 === 3) out += "さんぜん";
		else if (d0 === 8) out += "はっせん";
		else if (d0) out += JA_D[d0] + "せん";
		if (d1 === 1) out += "ひゃく";
		else if (d1 === 3) out += "さんびゃく";
		else if (d1 === 6) out += "ろっぴゃく";
		else if (d1 === 8) out += "はっぴゃく";
		else if (d1) out += JA_D[d1] + "ひゃく";
		if (d2 === 1) out += "じゅう";
		else if (d2) out += JA_D[d2] + "じゅう";
		if (d3) out += JA_D[d3];
		return out;
	}

	function deRead(p){
		if (p.ip.length > 21) return "超出";
		var body = deInt(p.ip);
		if (p.fp) body += " Komma " + spellDigits(p.fp, DE_DIG, " ");
		return (p.neg ? "minus " : "") + body;
	}

	function deInt(ip){
		if (ip === "0") return "null";
		var chunks = splitend(ip, 3);
		var parts = [];
		var i;
		for (i = chunks.length - 1; i >= 2; i--) {
			var n = parseInt(chunks[i], 10);
			if (!n) continue;
			var pair = DE_BIG[i];
			parts.push((n === 1 ? "eine" : de999(n, true)) + " " + (n === 1 ? pair[0] : pair[1]));
		}
		var low = parseInt(chunks[0], 10);
		if (chunks.length > 1) low += parseInt(chunks[1], 10) * 1000;
		if (low) parts.push(deBelow(low));
		return parts.join(" ");
	}

	function deBelow(n){
		if (n < 1000) return de999(n, true);
		var th = Math.floor(n / 1000);
		var rest = n % 1000;
		var s = (th === 1 ? "ein" : de999(th, false)) + "tausend";
		if (rest) s += de999(rest, true);
		return s;
	}

	function de999(n, end){
		var s = "";
		if (n >= 100) {
			var h = Math.floor(n / 100);
			s = (h === 1 ? "ein" : DE_U[h]) + "hundert";
			n = n % 100;
		}
		if (n) s += de99(n, end);
		return s;
	}

	function de99(n, end){
		if (n < 10) return n === 1 ? (end ? "eins" : "ein") : DE_U[n];
		if (n === 11) return "elf";
		if (n === 12) return "zwölf";
		if (n < 20) {
			if (n === 16) return "sechzehn";
			if (n === 17) return "siebzehn";
			return DE_U[n - 10] + "zehn";
		}
		var u = n % 10;
		var ten = deTen(Math.floor(n / 10));
		if (!u) return ten;
		return (u === 1 ? "ein" : DE_U[u]) + "und" + ten;
	}

	function deTen(t){
		if (t === 2) return "zwanzig";
		if (t === 3) return "dreißig";
		if (t === 6) return "sechzig";
		if (t === 7) return "siebzig";
		return DE_U[t] + "zig";
	}

	function frRead(p){
		if (p.ip.length > 24) return "超出";
		var body = frInt(p.ip);
		if (p.fp) body += " virgule " + spellDigits(p.fp, FR_D, " ");
		return (p.neg ? "moins " : "") + body;
	}

	function frInt(ip){
		if (ip === "0") return "zéro";
		var chunks = splitend(ip, 3);
		var parts = [];
		var i;
		for (i = chunks.length - 1; i >= 0; i--) {
			var n = parseInt(chunks[i], 10);
			if (!n) continue;
			if (i === 0) parts.push(fr999(n, true));
			else if (i === 1) parts.push((n === 1 ? "" : fr999(n, false) + " ") + "mille");
			else parts.push(fr999(n, false) + " " + FR_SC[i] + (n > 1 ? "s" : ""));
		}
		return parts.join(" ");
	}

	// final：这一段后面没有别的成分，vingt / cent 才加 s。
	function fr999(n, final){
		var h = Math.floor(n / 100);
		var rest = n % 100;
		var s = "";
		if (h === 1) s = "cent";
		else if (h) s = FR_D[h] + " " + (rest || !final ? "cent" : "cents");
		if (rest) s += (s ? " " : "") + fr99(rest, final);
		return s;
	}

	function fr99(n, final){
		if (n < 10) return FR_D[n];
		if (n < 17) return FR_TEEN[n - 10];
		if (n < 20) return "dix-" + FR_D[n - 10];
		if (n < 70) {
			var u = n % 10;
			var name = FR_TENS[Math.floor(n / 10)];
			if (!u) return name;
			if (u === 1) return name + " et un";
			return name + "-" + FR_D[u];
		}
		if (n < 80) {
			if (n === 71) return "soixante et onze";
			return "soixante-" + fr99(n - 60, final);
		}
		var r = n - 80;
		if (!r) return final ? "quatre-vingts" : "quatre-vingt";
		if (n === 81) return "quatre-vingt-un";
		return "quatre-vingt-" + fr99(r, final);
	}

	function esRead(p){
		if (p.ip.length > 30) return "超出";
		var body = esInt(p.ip);
		if (p.fp) body += " coma " + spellDigits(p.fp, ES_DIG, " ");
		return (p.neg ? "menos " : "") + body;
	}

	function esInt(ip){
		if (ip === "0") return "cero";
		var chunks = splitend(ip, 3);
		var items = [];
		var i;
		for (i = chunks.length - 1; i >= 0; i--) {
			var n = parseInt(chunks[i], 10);
			if (!n) continue;
			items.push([n, esPart(n, i)]);
		}
		if (items.length === 1) return items[0][1];
		var text = items[0][1];
		for (i = 1; i < items.length; i++) {
			var last = i === items.length - 1 && items[i][0] < 100;
			text += (last ? " y " : " ") + items[i][1];
		}
		return text;
	}

	function esPart(n, i){
		if (i === 0) return es999(n, false);
		if (i === 1) return n === 1 ? "mil" : es999(n, true) + " mil";
		var pair = ES_LL[Math.floor((i - 2) / 2)];
		if (i % 2 === 0) return (n === 1 ? "un" : es999(n, true)) + " " + (n === 1 ? pair[0] : pair[1]);
		if (n === 1) return "mil " + pair[1];
		return es999(n, true) + " mil " + pair[1];
	}

	function es999(n, apo){
		if (n === 100) return "cien";
		var h = Math.floor(n / 100);
		var rest = n % 100;
		var s = h ? ES_100[h] : "";
		if (rest) s += (s ? " " : "") + es99(rest, apo);
		return s;
	}

	function es99(n, apo){
		if (n < 20) return n === 1 && apo ? "un" : ES_1[n];
		if (n === 20) return "veinte";
		if (n < 30) return n === 21 ? (apo ? "veintiún" : "veintiuno") : ES_20[n - 20];
		var u = n % 10;
		var s = ES_10[Math.floor(n / 10)];
		if (!u) return s;
		return s + " y " + (u === 1 && apo ? "un" : ES_1[u]);
	}

	function ptRead(p){
		if (p.ip.length > 30) return "超出";
		var body = ptInt(p.ip);
		if (p.fp) body += " vírgula " + spellDigits(p.fp, PT_DIG, " ");
		return (p.neg ? "menos " : "") + body;
	}

	function ptInt(ip){
		if (ip === "0") return "zero";
		var chunks = splitend(ip, 3);
		var items = [];
		var i;
		for (i = chunks.length - 1; i >= 0; i--) {
			var n = parseInt(chunks[i], 10);
			if (!n) continue;
			items.push([n, ptPart(n, i)]);
		}
		var text = items[0][1];
		for (i = 1; i < items.length; i++) {
			var n = items[i][0];
			var last = i === items.length - 1 && (n < 100 || n % 100 === 0);
			text += (last ? " e " : " ") + items[i][1];
		}
		return text;
	}

	function ptPart(n, i){
		if (i === 0) return pt999(n);
		if (i === 1) return n === 1 ? "mil" : pt999(n) + " mil";
		var pair = PT_BIG[i];
		return (n === 1 ? "um " + pair[0] : pt999(n) + " " + pair[1]);
	}

	function pt999(n){
		if (n === 100) return "cem";
		var h = Math.floor(n / 100);
		var rest = n % 100;
		var s = h ? PT_100[h] : "";
		if (!rest) return s;
		return s ? s + " e " + pt99(rest) : pt99(rest);
	}

	function pt99(n){
		if (n < 20) return PT_1[n];
		var u = n % 10;
		var s = PT_10[Math.floor(n / 10)];
		if (!u) return s;
		return s + " e " + PT_1[u];
	}

	function ruRead(p){
		if (p.ip.length > 21) return "超出";
		var fem = p.fp && p.fp.length <= 9;
		var body = ruInt(p.ip, fem);
		if (p.fp) {
			if (fem) {
				var num = parseInt(p.fp, 10);
				var one = ruForm(num) === "one";
				var cel = ruForm(parseInt(p.ip.slice(-2), 10)) === "one" ? "целая" : "целых";
				body += " " + cel + " " + ruInt(String(num), true) + " " + (one ? RU_DEN1[p.fp.length] : RU_DEN[p.fp.length]);
			}
			else body += " запятая " + spellDigits(p.fp, RU_DIG, " ");
		}
		return (p.neg ? "минус " : "") + body;
	}

	function ruInt(ip, fem){
		if (ip === "0") return "ноль";
		var chunks = splitend(ip, 3);
		var parts = [];
		var i;
		for (i = chunks.length - 1; i >= 0; i--) {
			var n = parseInt(chunks[i], 10);
			if (!n) continue;
			if (i === 0) parts.push(ru999(n, fem));
			else {
				var sc = RU_SC[i];
				var form = ruForm(n);
				var name = form === "one" ? sc[0] : form === "few" ? sc[1] : sc[2];
				parts.push(ru999(n, sc[3]) + " " + name);
			}
		}
		return parts.join(" ");
	}

	function ru999(n, fem){
		var ones = fem ? RU_F : RU_M;
		var h = Math.floor(n / 100);
		var rest = n % 100;
		var parts = [];
		if (h) parts.push(RU_100[h]);
		if (rest >= 10 && rest < 20) parts.push(RU_TEEN[rest - 10]);
		else {
			var t = Math.floor(rest / 10);
			var u = rest % 10;
			if (t) parts.push(RU_10[t]);
			if (u) parts.push(ones[u]);
		}
		return parts.join(" ");
	}

	function ruForm(n){
		var m100 = n % 100;
		var m10 = n % 10;
		if (m100 >= 11 && m100 <= 14) return "many";
		if (m10 === 1) return "one";
		if (m10 >= 2 && m10 <= 4) return "few";
		return "many";
	}

	function siread(p){
		if (p.ip === "0" && !p.fp) return "0";
		var exp;
		var digits;
		if (p.ip !== "0") {
			exp = p.ip.length - 1;
			digits = (p.ip + p.fp).replace(/0+$/, "");
		}
		else {
			var i = 0;
			while (p.fp.charAt(i) === "0") i++;
			exp = -(i + 1);
			digits = p.fp.slice(i);
		}
		var idx = Math.floor(exp / 3);
		if (idx > 10) idx = 10;
		if (idx < -10) idx = -10;
		var mant = placePoint(digits, exp - idx * 3 + 1);
		var unit = SI[idx + 10];
		var text = mant;
		if (unit) text += " " + unit[0] + " (" + unit[1] + ")";
		return sign(p) + text;
	}

	function placePoint(digits, point){
		var left;
		var right;
		var z;
		if (point <= 0) {
			left = "0";
			right = digits;
			for (z = 0; z < -point; z++) right = "0" + right;
		}
		else if (point >= digits.length) {
			left = digits;
			for (z = digits.length; z < point; z++) left += "0";
			right = "";
		}
		else {
			left = digits.slice(0, point);
			right = digits.slice(point);
		}
		right = right.replace(/0+$/, "");
		return left + (right ? "." + right : "");
	}

	function group3(ip){
		var s = ip;
		var out = "";
		while (s.length > 3) {
			out = "," + s.slice(-3) + out;
			s = s.slice(0, -3);
		}
		return s + out;
	}

	function sciform(p){
		if (p.ip === "0" && !p.fp) return "0";
		var exp;
		var mant;
		if (p.ip !== "0") {
			exp = p.ip.length - 1;
			var rest = (p.ip.slice(1) + p.fp).replace(/0+$/, "");
			mant = p.ip.charAt(0) + (rest ? "." + rest : "");
		}
		else {
			var i = 0;
			while (p.fp.charAt(i) === "0") i++;
			exp = -(i + 1);
			var tail = p.fp.slice(i + 1).replace(/0+$/, "");
			mant = p.fp.charAt(i) + (tail ? "." + tail : "");
		}
		return sign(p) + mant + " × 10" + sup(exp);
	}

	function sup(exp){
		var n = exp < 0 ? -exp : exp;
		var s = String(n);
		var out = exp < 0 ? "⁻" : "";
		var i;
		for (i = 0; i < s.length; i++) out += SUP[s.charAt(i)];
		return out;
	}

	function percent(p){
		var all = p.ip + p.fp;
		var point = p.ip.length + 2;
		var left;
		var right;
		if (point >= all.length) {
			left = all;
			var k;
			for (k = all.length; k < point; k++) left += "0";
			right = "";
		}
		else {
			left = all.slice(0, point);
			right = all.slice(point).replace(/0+$/, "");
		}
		left = left.replace(/^0+/, "") || "0";
		return sign(p) + left + (right ? "." + right : "") + "%";
	}

	// 英文多边形名：1–9999 用 hecta 体系，万以上用「若干次 myriad」。超过 1 亿显示超出。
	function polyof(p){
		if (p.neg || p.fp || p.ip === "0") return "—";
		if (p.ip.length > 9) return "超出";
		var n = parseInt(p.ip, 10);
		if (n > 100000000) return "超出";
		return polyName(n);
	}

	function polyName(n){
		if (n === 3) return "triangle";
		if (n === 4) return "quadrilateral";
		var name;
		if (n === 100000000) name = "myriakismyriagon";
		else if (n < 10000) name = stemUnderMyriad(n) + "gon";
		else {
			var high = Math.floor(n / 10000);
			var low = n % 10000;
			var pref = high === 1 ? "myria" : polyAdverb(high) + "myria";
			name = low ? pref + "kai" + stemUnderMyriad(low) + "gon" : pref + "gon";
		}
		var alias = megaAlias(n);
		if (alias) name += " (" + alias + ")";
		return name;
	}

	function stemUnderMyriad(n){
		if (n < 1000) return stemUnder1000(n);
		var t = Math.floor(n / 1000);
		var rest = n % 1000;
		var th = polyThousand(t);
		if (!rest) return th;
		return th + "kai" + stemUnder1000(rest);
	}

	function stemUnder1000(n){
		if (n < 100) return stemUnder100(n);
		var h = Math.floor(n / 100);
		var rest = n % 100;
		if (!rest) return h === 1 ? "hecto" : polyUnit(h) + "hecta";
		if (h === 1) return "hecaton" + stemUnder100(rest);
		return polyUnit(h) + "hecta" + stemUnder100(rest);
	}

	function stemUnder100(n){
		var ones = n % 10;
		var tens = Math.floor(n / 10);
		if (n < 10) return polyUnit(n);
		if (n < 20) return polyTeen(n - 10);
		if (n === 20) return "icosa";
		if (n < 30) return "icosikai" + polyUnit(ones);
		var ten = polyTen(tens);
		if (!ones) return ten;
		return ten + "kai" + polyUnit(ones);
	}

	function polyUnit(d){
		return ["", "hena", "di", "tri", "tetra", "penta", "hexa", "hepta", "octa", "ennea"][d];
	}

	function polyTeen(i){
		return ["deca", "hendeca", "dodeca", "trideca", "tetradeca", "pentadeca", "hexadeca", "heptadeca", "octadeca", "enneadeca"][i];
	}

	function polyTen(t){
		if (t === 2) return "icosa";
		return ["", "", "", "triaconta", "tetraconta", "pentaconta", "hexaconta", "heptaconta", "octaconta", "enneaconta"][t];
	}

	function polyThousand(t){
		return ["", "chilia", "dischilia", "trischilia", "tetrakischilia", "pentakischilia", "hexakischilia", "heptakischilia", "octakischilia", "enneakischilia"][t];
	}

	function polyAdverb(k){
		if (k === 2) return "dis";
		if (k === 3) return "tris";
		return kisStem(k) + "kis";
	}

	function kisStem(k){
		if (k < 100) return stemUnder100(k);
		if (k < 1000) {
			var h = Math.floor(k / 100);
			var rest = k % 100;
			if (!rest) return h === 1 ? "hecatonta" : polyCosi(h, "cosa");
			return (h === 1 ? "hecaton" : polyCosi(h, "cosi")) + "kai" + stemUnder100(rest);
		}
		var t = Math.floor(k / 1000);
		var low = k % 1000;
		var th = polyThousand(t);
		if (!low) return th;
		return th + "kai" + kisStem(low);
	}

	function polyCosi(d, tail){
		var p = polyUnit(d);
		if (p.charAt(p.length - 1) === "a") p = p.slice(0, -1);
		return p + "a" + tail;
	}

	function megaAlias(n){
		if (n % 1000000) return "";
		var k = n / 1000000;
		if (k === 1) return "megagon";
		if (k === 10) return "decamegagon";
		if (k === 20) return "icosamegagon";
		if (k === 100) return "hectamegagon";
		if (k >= 2 && k <= 9) return polyUnit(k) + "megagon";
		if (k >= 30 && k <= 90 && k % 10 === 0) return stemUnder100(k) + "megagon";
		return "";
	}

	function romanof(p){
		if (p.neg || p.fp || p.ip.length > 4) return "—";
		var n = parseInt(p.ip, 10);
		if (!(n >= 1 && n <= 3999)) return "—";
		var out = "";
		var i;
		for (i = 0; i < ROM.length; i++) {
			while (n >= ROM[i][0]) {
				out += ROM[i][1];
				n -= ROM[i][0];
			}
		}
		return out;
	}

	function baseof(p, radix){
		if (p.fp) return "仅整数";
		if (typeof BigInt !== "function") return "—";
		return sign(p) + BigInt(p.ip).toString(radix).toUpperCase();
	}

	function splitend(s, n){
		var chunks = [];
		var left = s;
		while (left.length) {
			var take = left.length > n ? n : left.length;
			chunks.push(left.slice(-take));
			left = left.slice(0, -take);
		}
		return chunks;
	}

	function joinrows(rows){
		var lines = [];
		var i;
		for (i = 0; i < rows.length; i++) lines.push(rows[i][0] + "\t" + rows[i][1]);
		return lines.join("\n");
	}

	function paint(rows){
		var list = SK.$("num-list");
		if (!list) return;
		if (!rows.length) {
			list.innerHTML = "";
			return;
		}
		var html = "";
		var i;
		for (i = 0; i < rows.length; i++) {
			var v = esc(rows[i][1]);
			html += "<span>" + esc(rows[i][0]) + "</span><button type=\"button\" data-v=\"" + v + "\">" + v + "</button>";
		}
		list.innerHTML = html;
	}

	function esc(s){
		return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/"/g, "&quot;");
	}

	SK.numread = numread;
})();
