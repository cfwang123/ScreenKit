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

	SK.regutil("u-numread", {
		title: "数字表记转换",
		go: "转换",
		noGo: true,
		hideOut: true,
		body: function(){
			return SK.lab("数字", SK.textin("box-in", "", "例如 12345.67"))
				+ '<p class="hint">小数逢十进，中数逢万万进，大数自乘。小数点后依次是分、厘、毫、丝。SI 每千进一位，如 kilo、mega、zepto、yocto。</p>'
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
			["SI", siread(p)],
			["千分位", sign(p) + group3(p.ip) + (p.fp ? "." + p.fp : "")],
			["科学计数", sciform(p)],
			["百分数", percent(p)],
			["罗马数字", romanof(p)],
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
