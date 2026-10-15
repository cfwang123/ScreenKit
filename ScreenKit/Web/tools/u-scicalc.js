(function(){
	var KEYS = [
		["second", "2nd", "2nd", "second", ""],
		["lp", "(", "(", "(", ""],
		["rp", ")", ")", ")", ""],
		["bs", "⌫", "⌫", "bs", ""],
		["ce", "CE", "CE", "ce", ""],
		["ac", "AC", "AC", "ac", ""],
		["sin", "sin", "sin⁻¹", "sin", "asin"],
		["cos", "cos", "cos⁻¹", "cos", "acos"],
		["tan", "tan", "tan⁻¹", "tan", "atan"],
		["ln", "ln", "eˣ", "ln", "exp"],
		["log", "log", "10ˣ", "log", "tenx"],
		["sqrt", "√", "³√", "sqrt", "cbrt"],
		["sq", "x²", "x⁻¹", "sq", "inv"],
		["pow", "xʸ", "ʸ√x", "pow", "nroot"],
		["fact", "n!", "|x|", "fact", "abs"],
		["inv", "1/x", "1/x", "inv", ""],
		["pi", "π", "π", "pi", ""],
		["e", "e", "e", "e", ""],
		["7", "7", "7", "7", "", 1],
		["8", "8", "8", "8", "", 1],
		["9", "9", "9", "9", "", 1],
		["div", "/", "/", "/", ""],
		["pct", "%", "mod", "pct", "mod"],
		["ans", "Ans", "Ans", "ans", ""],
		["4", "4", "4", "4", "", 1],
		["5", "5", "5", "5", "", 1],
		["6", "6", "6", "6", "", 1],
		["mul", "*", "*", "*", ""],
		["sign", "±", "±", "sign", ""],
		["expn", "EXP", "EXP", "expn", ""],
		["1", "1", "1", "1", "", 1],
		["2", "2", "2", "2", "", 1],
		["3", "3", "3", "3", "", 1],
		["sub", "-", "-", "-", ""],
		["mc", "MC", "MC", "mc", ""],
		["mr", "MR", "MR", "mr", ""],
		["0", "0", "0", "0", "", 1],
		["dot", ".", ".", ".", ""],
		["eq", "=", "=", "eq", ""],
		["add", "+", "+", "+", ""],
		["mp", "M+", "M+", "mp", ""],
		["ms", "MS", "MS", "ms", ""]
	];
	var FUNCS = ["asin(", "acos(", "atan(", "sqrt(", "cbrt(", "abs(", "exp(", "sin(", "cos(", "tan(", "ln(", "log(", "10^("];
	var pad = { expr: "", value: "0", ok: true, deg: true, second: false, done: false, mem: 0, ans: 0 };

	SK.regutil("u-scicalc", {
		title: "科学计算器",
		go: "计算",
		hideGo: true,
		hideCopy: true,
		hideOut: true,
		body: function(){
			var html = '<div class="scicalc" tabindex="0"><div class="scitop"><span class="scimem"></span><button type="button" data-act="deg">DEG</button></div>';
			html += '<div class="sciexpr"></div><div class="scival">0</div><div class="scikeys">';
			for (var n = 0; n < KEYS.length; n++) {
				var row = KEYS[n];
				var cls = row[0] === "eq" ? " eq" : (row[0] === "ac" || row[0] === "ce" ? " danger" : (row[5] ? " digit" : ""));
				html += '<button type="button" class="scikey' + cls + '" data-i="' + n + '">' + row[1] + "</button>";
			}
			html += "</div></div>";
			return html;
		},
		open: function(){ bind(); }
	});
	SK.sciTry = tryeval;

	function bind(){
		var root = F.q("#box-body .scicalc");
		if (!root) return;
		pad = { expr: "", value: "0", ok: true, deg: true, second: false, done: false, mem: 0, ans: 0 };
		var buttons = root.querySelectorAll("button");
		for (var n = 0; n < buttons.length; n++) {
			buttons[n].onclick = function(){
				var act = this.getAttribute("data-act");
				if (act === "deg") press("deg", false);
				else {
					var row = KEYS[Number(this.getAttribute("data-i"))];
					var use2 = pad.second && row[4];
					press(use2 ? row[4] : row[3], !!use2);
				}
				paint(root);
			};
		}
		root.onkeydown = function(ev){
			var key = keyof(ev);
			if (!key) return;
			F.pd(ev);
			press(key, false);
			paint(root);
		};
		var val = root.querySelector(".scival");
		val.onclick = function(){
			var text = val.textContent || "";
			if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(text);
		};
		paint(root);
		root.focus();
	}

	function paint(root){
		root.querySelector(".sciexpr").textContent = shown(pad.expr);
		root.querySelector(".scival").textContent = pad.ok ? pad.value : "错误";
		root.querySelector(".scimem").textContent = pad.mem === 0 ? "" : "M";
		root.querySelector("[data-act=deg]").textContent = pad.deg ? "DEG" : "RAD";
		var buttons = root.querySelectorAll(".scikey");
		for (var n = 0; n < buttons.length; n++) {
			var row = KEYS[n];
			if (row[0] === "second") buttons[n].className = "scikey" + (pad.second ? " eq" : "");
			if (row[2] && row[2] !== row[1]) buttons[n].textContent = pad.second ? row[2] : row[1];
		}
	}

	function shown(s){
		return String(s || "");
	}

	function press(key, fromSecond){
		if (key === "second") { pad.second = !pad.second; return; }
		if (fromSecond) pad.second = false;
		if (key === "ac") { pad.expr = ""; pad.value = "0"; pad.ok = true; pad.done = false; return; }
		if (key === "bs") { back(); return; }
		if (key === "ce") { entryclear(); return; }
		if (key === "sign") { sign(); return; }
		if (key === "eq") { eq(); return; }
		if (key === "mc") { pad.mem = 0; return; }
		if (key === "ms" || key === "mp" || key === "mm") { mem(key); return; }
		if (key === "mr") { insertnum(fmt(pad.mem)); return; }
		if (key === "ans") { insertnum(fmt(pad.ans)); return; }
		if (key === "deg") {
			pad.deg = !pad.deg;
			var src = pad.expr || fmt(pad.ans);
			var v = tryeval(src, pad.deg);
			if (pad.done && v != null) { pad.ans = v; pad.value = fmt(v); pad.ok = true; }
			else preview();
			return;
		}
		put(token(key));
	}

	function token(key){
		if (key === "pi") return "π";
		if (key === "tenx") return "10^(";
		if (key === "sq") return "^2";
		if (key === "inv") return "^-1";
		if (key === "nroot") return "√";
		if (key === "expn") return "E";
		if (key === "pow") return "^";
		if (key === "fact") return "!";
		if (key === "pct") return "%";
		if (key === "sin" || key === "cos" || key === "tan" || key === "asin" || key === "acos" || key === "atan" ||
			key === "ln" || key === "log" || key === "sqrt" || key === "cbrt" || key === "abs" || key === "exp")
			return key + "(";
		return key;
	}

	function put(bit){
		if (!bit) return;
		if (pad.done && fresh(bit)) pad.expr = "";
		else if (pad.done && isop(bit)) pad.expr = fmt(pad.ans);
		pad.done = false;
		pad.ok = true;
		if (pad.expr.length + bit.length > 180) return;
		pad.expr += bit;
		preview();
	}

	function insertnum(bit){
		if (pad.done) pad.expr = "";
		pad.done = false;
		pad.ok = true;
		pad.expr += bit;
		preview();
	}

	function eq(){
		var src = pad.expr || fmt(pad.ans);
		var v = tryeval(src, pad.deg);
		if (v == null) { pad.ok = false; return; }
		pad.ans = v;
		pad.expr = src;
		pad.value = fmt(v);
		pad.ok = true;
		pad.done = true;
	}

	function back(){
		if (pad.done) { pad.expr = ""; pad.value = "0"; pad.ok = true; pad.done = false; return; }
		pad.expr = cutone(pad.expr);
		pad.ok = true;
		preview();
	}

	function entryclear(){
		if (pad.done) { pad.expr = ""; pad.value = "0"; pad.ok = true; pad.done = false; return; }
		var span = numspan(pad.expr);
		if (!span) { preview(); return; }
		var a = span[0];
		if (a > 0 && pad.expr.charAt(a - 1) === "-" && unaryminus(pad.expr, a - 1)) a--;
		pad.expr = pad.expr.slice(0, a);
		pad.ok = true;
		preview();
	}

	function sign(){
		if (pad.done) pad.expr = fmt(pad.ans);
		pad.done = false;
		pad.ok = true;
		var s = pad.expr;
		if (s.slice(-2) === "E-") s = s.slice(0, -1);
		else if (s.slice(-2) === "E+") s = s.slice(0, -1) + "-";
		else if (s.slice(-1) === "E") s += "-";
		else {
			var span = numspan(s);
			if (span) {
				var a = span[0];
				if (a > 0 && s.charAt(a - 1) === "-" && unaryminus(s, a - 1)) s = s.slice(0, a - 1) + s.slice(a);
				else s = s.slice(0, a) + "-" + s.slice(a);
			}
			else s = "-" + s;
		}
		pad.expr = s;
		preview();
	}

	function mem(key){
		var v = current();
		if (v == null) { pad.ok = false; return; }
		if (key === "ms") pad.mem = v;
		else if (key === "mp") pad.mem += v;
		else pad.mem -= v;
	}

	function preview(){
		if (!pad.expr) { pad.value = "0"; pad.ok = true; return; }
		var v = tryeval(pad.expr, pad.deg);
		if (v != null) { pad.value = fmt(v); pad.ok = true; return; }
		pad.ok = true;
		var span = numspan(pad.expr);
		pad.value = span ? pad.expr.slice(span[0], span[1]) : "0";
	}

	function current(){
		if (pad.done) return pad.ans;
		if (!pad.expr) return 0;
		var v = tryeval(pad.expr, pad.deg);
		if (v != null) return v;
		var span = numspan(pad.expr);
		if (!span) return null;
		var n = Number(pad.expr.slice(span[0], span[1]));
		return isFinite(n) ? n : null;
	}

	function fresh(bit){
		var c = bit.charAt(0);
		if (c >= "0" && c <= "9") return true;
		if (c === "." || c === "(" || bit === "π" || bit === "E") return true;
		return bit.slice(-1) === "(";
	}

	function isop(bit){
		return bit === "+" || bit === "-" || bit === "*" || bit === "/" || bit === "^" || bit === "√" || bit === "mod";
	}

	function cutone(s){
		for (var n = 0; n < FUNCS.length; n++)
			if (s.slice(-FUNCS[n].length) === FUNCS[n]) return s.slice(0, -FUNCS[n].length);
		if (s.slice(-3) === "mod") return s.slice(0, -3);
		if (!s) return s;
		return s.slice(0, -1);
	}

	function unaryminus(s, at){
		if (s.charAt(at) !== "-") return false;
		if (at === 0) return true;
		var p = s.charAt(at - 1);
		return p === "+" || p === "-" || p === "*" || p === "/" || p === "^" || p === "√" || p === "(" || p === "E" || p === "e";
	}

	function numspan(s){
		var j = s.length;
		var b = j;
		while (j > 0 && s.charAt(j - 1) >= "0" && s.charAt(j - 1) <= "9") j--;
		if (j === b) return null;
		if (j > 0 && (s.charAt(j - 1) === "E" || s.charAt(j - 1) === "e")) {
			j--;
			if (j > 0 && (s.charAt(j - 1) === "+" || s.charAt(j - 1) === "-")) j--;
			var k = j;
			while (j > 0 && s.charAt(j - 1) >= "0" && s.charAt(j - 1) <= "9") j--;
			if (j === k) return null;
		}
		if (j > 0 && s.charAt(j - 1) === ".") {
			j--;
			while (j > 0 && s.charAt(j - 1) >= "0" && s.charAt(j - 1) <= "9") j--;
		}
		return j < b ? [j, b] : null;
	}

	function fmt(v){
		if (!isFinite(v)) return "0";
		var s = String(Number(v.toPrecision(12)));
		return s;
	}

	function tryeval(src, deg){
		try {
			var s = norm(src || "");
			if (!s) return null;
			var i = 0;
			var node = expr();
			if (i !== s.length) return null;
			var v = ev(node);
			return isFinite(v) ? v : null;
		}
		catch (ex) { return null; }

		function norm(raw){
			return String(raw).replace(/[ \t]/g, "").replace(/×/g, "*").replace(/÷/g, "/").replace(/−/g, "-").replace(/π/g, "pi");
		}
		function expr(){
			var left = factor();
			while (i < s.length && (s.charAt(i) === "+" || s.charAt(i) === "-")) {
				var op = s.charAt(i);
				i++;
				left = bin(op, left, factor());
			}
			return left;
		}
		function factor(){
			var left = unary();
			while (i < s.length) {
				var op = "";
				if (s.charAt(i) === "*" || s.charAt(i) === "/") op = s.charAt(i);
				else if (ismod()) op = "mod";
				else break;
				i += op === "mod" ? 3 : 1;
				left = bin(op, left, unary());
			}
			return left;
		}
		function unary(){
			if (i < s.length && (s.charAt(i) === "+" || s.charAt(i) === "-")) {
				var op = s.charAt(i);
				i++;
				return { k: "u", op: op, a: unary() };
			}
			return power();
		}
		function power(){
			var left = postfix();
			if (i < s.length && (s.charAt(i) === "^" || s.charAt(i) === "√")) {
				var op = s.charAt(i);
				i++;
				return bin(op, left, unary());
			}
			return left;
		}
		function postfix(){
			var v = primary();
			while (i < s.length && (s.charAt(i) === "!" || s.charAt(i) === "%")) {
				v = s.charAt(i) === "!" ? { k: "f", a: v } : { k: "p", a: v };
				i++;
			}
			return v;
		}
		function primary(){
			if (i >= s.length) throw new Error("end");
			if (s.charAt(i) === "(") {
				i++;
				var v = expr();
				if (s.charAt(i) !== ")") throw new Error(")");
				i++;
				return v;
			}
			if (isdigit(s.charAt(i)) || s.charAt(i) === ".") return { k: "n", v: readnum() };
			var id = readid();
			if (id === "pi") return { k: "n", v: Math.PI };
			if (id === "e") return { k: "n", v: Math.E };
			if (s.charAt(i) !== "(") throw new Error("call");
			i++;
			var arg = expr();
			if (s.charAt(i) !== ")") throw new Error(")");
			i++;
			return { k: "c", op: id, a: arg };
		}
		function ismod(){
			return s.slice(i, i + 3) === "mod" && (i + 3 === s.length || !letter(s.charAt(i + 3)));
		}
		function readnum(){
			var start = i;
			if (s.charAt(i) === ".") i++;
			if (i >= s.length || !isdigit(s.charAt(i)) && s.charAt(start) !== ".") throw new Error("num");
			while (i < s.length && isdigit(s.charAt(i))) i++;
			if (s.charAt(start) !== "." && s.charAt(i) === ".") {
				i++;
				while (i < s.length && isdigit(s.charAt(i))) i++;
			}
			if (s.charAt(i) === "E" || s.charAt(i) === "e") {
				var k = i;
				i++;
				if (s.charAt(i) === "+" || s.charAt(i) === "-") i++;
				var d0 = i;
				while (i < s.length && isdigit(s.charAt(i))) i++;
				if (i === d0) i = k;
			}
			return Number(s.slice(start, i));
		}
		function readid(){
			var start = i;
			while (letter(s.charAt(i))) i++;
			if (start === i) throw new Error("id");
			return s.slice(start, i);
		}
		function ev(n){
			if (n.k === "n") return n.v;
			if (n.k === "u") return n.op === "-" ? -ev(n.a) : ev(n.a);
			if (n.k === "f") return fact(ev(n.a));
			if (n.k === "p") return ev(n.a) / 100;
			if (n.k === "c") return callv(n.op, ev(n.a));
			var lv = ev(n.l);
			if (n.r.k === "p" && (n.op === "+" || n.op === "-" || n.op === "*" || n.op === "/"))
				return percent(n.op, lv, ev(n.r.a));
			var rv = ev(n.r);
			if (n.op === "+") return lv + rv;
			if (n.op === "-") return lv - rv;
			if (n.op === "*") return lv * rv;
			if (n.op === "/") { if (rv === 0) throw new Error("0"); return lv / rv; }
			if (n.op === "mod") { if (rv === 0) throw new Error("0"); return lv % rv; }
			if (n.op === "^") return Math.pow(lv, rv);
			if (Math.abs(lv) < 1e-15) throw new Error("0");
			if (rv < 0) {
				var round = Math.round(lv);
				if (Math.abs(lv - round) > 1e-9 || round % 2 === 0) throw new Error("root");
				return -Math.pow(-rv, 1 / lv);
			}
			return Math.pow(rv, 1 / lv);
		}
		function callv(name, x){
			if (name === "sin") return Math.sin(radin(x));
			if (name === "cos") return Math.cos(radin(x));
			if (name === "tan") return Math.tan(radin(x));
			if (name === "asin") return radout(Math.asin(x));
			if (name === "acos") return radout(Math.acos(x));
			if (name === "atan") return radout(Math.atan(x));
			if (name === "ln") return Math.log(x);
			if (name === "log") return Math.log(x) / Math.LN10;
			if (name === "sqrt") return Math.sqrt(x);
			if (name === "cbrt") return x < 0 ? -Math.pow(-x, 1 / 3) : Math.pow(x, 1 / 3);
			if (name === "abs") return Math.abs(x);
			if (name === "exp") return Math.exp(x);
			throw new Error(name);
		}
		function radin(x){ return deg ? x * Math.PI / 180 : x; }
		function radout(x){ return deg ? x * 180 / Math.PI : x; }
		function percent(op, left, raw){
			if (op === "+") return left + left * raw / 100;
			if (op === "-") return left - left * raw / 100;
			if (op === "*") return left * raw / 100;
			if (raw === 0) throw new Error("0");
			return left / (raw / 100);
		}
		function fact(v){
			if (v < 0 || v > 170 || Math.abs(v - Math.round(v)) > 1e-9) throw new Error("!");
			var n = Math.round(v);
			var r = 1;
			for (var k = 2; k <= n; k++) r *= k;
			return r;
		}
		function bin(op, l, r){ return { k: "b", op: op, l: l, r: r }; }
		function isdigit(c){ return c >= "0" && c <= "9"; }
		function letter(c){ return (c >= "a" && c <= "z") || (c >= "A" && c <= "Z"); }
	}

	function keyof(ev){
		var k = ev.key;
		if (k >= "0" && k <= "9") return k;
		if (k === ".") return ".";
		if (k === "+") return "+";
		if (k === "-") return "-";
		if (k === "*") return "*";
		if (k === "/") return "/";
		if (k === "Enter") return "eq";
		if (k === "Backspace") return "bs";
		if (k === "Delete") return "ac";
		return "";
	}
})();
