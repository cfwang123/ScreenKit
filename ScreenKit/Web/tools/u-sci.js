(function(){
	SK.regutil("u-sci", {
		title: "科学计算",
		go: "计算",
		body: function(){
			return SK.lab("算式", SK.ta("box-in", 4, "支持三角函数（角度）、sqrt、log、ln、abs 和 pi"));
		},
		run: function(){
			return sciexpr(SK.val("box-in"));
		}
	});
	function sciexpr(src){
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
			if (peek() === "-" || peek() === "+") {
				var op = peek();
				i++;
				return op === "-" ? -unary() : unary();
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
			if (isletter(peek())) return named();
			return number();
		}
		function named(){
			var start = i;
			while (isletter(peek())) i++;
			var name = s.slice(start, i).toLowerCase();
			if (peek() === "(") {
				i++;
				var arg = expr();
				if (peek() !== ")") throw new Error("括号没有配对");
				i++;
				return applyfn(name, arg);
			}
			if (name === "pi") return Math.PI;
			if (name === "e") return Math.E;
			throw new Error("没有这个名字");
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
		function peek(){ return s.charAt(i); }
		function isletter(c){ return (c >= "a" && c <= "z") || (c >= "A" && c <= "Z"); }
	}

	function applyfn(name, v){
		var rad = v * Math.PI / 180;
		var out;
		if (name === "sin") out = Math.sin(rad);
		else if (name === "cos") out = Math.cos(rad);
		else if (name === "tan") out = Math.tan(rad);
		else if (name === "asin") out = Math.asin(v) * 180 / Math.PI;
		else if (name === "acos") out = Math.acos(v) * 180 / Math.PI;
		else if (name === "atan") out = Math.atan(v) * 180 / Math.PI;
		else if (name === "ln") out = Math.log(v);
		else if (name === "log") out = Math.log(v) / Math.LN10;
		else if (name === "sqrt") out = Math.sqrt(v);
		else if (name === "abs") out = Math.abs(v);
		else if (name === "floor") out = Math.floor(v);
		else if (name === "ceil") out = Math.ceil(v);
		else throw new Error("没有这个函数");
		if (!isFinite(out)) throw new Error("函数结果无效");
		return out;
	}

	SK.sciexpr = sciexpr;
	SK.applyfn = applyfn;
})();
