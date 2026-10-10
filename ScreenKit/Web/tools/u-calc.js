(function(){
	SK.regutil("u-calc", {
		title: "计算器",
		go: "计算",
		body: function(){
			return SK.lab("算式", SK.ta("box-in", 4, "支持 + - * / % ^ 和括号，% 是取余"));
		},
		run: function(){
			return calcexpr(SK.val("box-in"));
		}
	});
	function calcexpr(src){
		var s = String(src == null ? "" : src).replace(/\s+/g, "");
		if (!s) throw new Error("请输入算式");
		if (s.length > 200) throw new Error("算式太长");
		var i = 0;
		var v = expr();
		if (i !== s.length) throw new Error("算式里有无法识别的部分");
		return SK.trimnum(v);
		function expr(){
			var left = term();
			while (peek() === "+" || peek() === "-") {
				var op = peek();
				i++;
				var right = term();
				left = op === "+" ? left + right : left - right;
			}
			return left;
		}
		function term(){
			var left = pow();
			while (peek() === "*" || peek() === "/" || peek() === "%") {
				var op = peek();
				i++;
				var right = pow();
				if (op === "*") left = left * right;
				else if (right === 0) throw new Error("除数不能为 0");
				else left = op === "/" ? left / right : left % right;
			}
			return left;
		}
		function pow(){
			var left = unary();
			if (peek() === "^") {
				i++;
				var right = pow();
				var v = Math.pow(left, right);
				if (!isFinite(v)) throw new Error("乘方结果无效");
				return v;
			}
			return left;
		}
		function unary(){
			if (peek() === "-") {
				i++;
				return -unary();
			}
			if (peek() === "+") {
				i++;
				return unary();
			}
			return primary();
		}
		function primary(){
			if (peek() === "(") {
				i++;
				var v = expr();
				if (peek() !== ")") throw new Error("括号没有配对");
				i++;
				return v;
			}
			return number();
		}
		function number(){
			var start = i;
			while (i < s.length && s.charAt(i) >= "0" && s.charAt(i) <= "9") i++;
			if (peek() === "." && i + 1 < s.length && s.charAt(i + 1) >= "0" && s.charAt(i + 1) <= "9") {
				i++;
				while (i < s.length && s.charAt(i) >= "0" && s.charAt(i) <= "9") i++;
			}
			if (start === i) throw new Error("这里需要数字");
			return Number(s.slice(start, i));
		}
		function peek(){
			return s.charAt(i);
		}
	}

	SK.calcexpr = calcexpr;
})();
