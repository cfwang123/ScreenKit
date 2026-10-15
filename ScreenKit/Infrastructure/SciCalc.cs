using System.Globalization;

namespace ScreenKit;

/// <summary>科学计算器。表达式由按键拼出，= 时求值。角度默认度。</summary>
sealed class SciCalc {
	const int NUM = 0;
	const int BIN = 1;
	const int CALL = 2;
	const int UNI = 3;
	const int POST = 4;
	const int PCT = 5;

	public string Expr = "";
	public string Value = "0";
	public bool Ok = true;
	public bool Deg = true;
	public bool Second;
	public bool Done;
	public double Memory;
	public double Ans;

	public string Shown {
		get {
			return Expr;
		}
	}

	public void Press(string key, bool fromSecond = false) {
		if (key == "second") {
			Second = !Second;
			return;
		}
		if (fromSecond) Second = false;
		if (key == "ac") { clear(); return; }
		if (key == "bs") { back(); return; }
		if (key == "ce") { entryclear(); return; }
		if (key == "sign") { sign(); return; }
		if (key == "eq") { eq(); return; }
		if (key == "mc") { Memory = 0; return; }
		if (key == "ms" || key == "mp" || key == "mm") { mem(key); return; }
		if (key == "mr") { insertnum(Fmt(Memory)); return; }
		if (key == "ans") { insertnum(Fmt(Ans)); return; }
		if (key == "deg") {
			Deg = !Deg;
			if (Done && TryEval(Expr.Length == 0 ? Fmt(Ans) : Expr, Deg, out var v)) {
				Ans = v;
				Value = Fmt(v);
				Ok = true;
			}
			else preview();
			return;
		}
		put(token(key));
	}

	public static string Fmt(double v) {
		if (double.IsNaN(v) || double.IsInfinity(v)) return "0";
		return v.ToString("G12", CultureInfo.InvariantCulture);
	}

	sealed class Nd {
		public int kind;
		public double num;
		public string op;
		public Nd left, right;
	}

	public static bool TryEval(string src, bool deg, out double value) {
		value = 0;
		var s = "";
		var i = 0;
		try {
			s = norm(src ?? "");
			if (s.Length == 0) return false;
			var node = expr();
			if (i != s.Length) return false;
			value = ev(node);
			return !double.IsNaN(value) && !double.IsInfinity(value);
		}
		catch {
			return false;
		}

		static string norm(string raw) {
			var sb = new System.Text.StringBuilder(raw.Length);
			foreach (var c in raw) {
				if (c == ' ' || c == '\t') continue;
				if (c == '×') sb.Append('*');
				else if (c == '÷') sb.Append('/');
				else if (c == '−') sb.Append('-');
				else if (c == 'π') sb.Append("pi");
				else sb.Append(c);
			}
			return sb.ToString();
		}

		Nd expr() {
			var left = factor();
			while (i < s.Length && (s[i] == '+' || s[i] == '-')) {
				var op = s[i].ToString();
				i++;
				left = bin(op, left, factor());
			}
			return left;
		}

		Nd factor() {
			var left = unary();
			while (i < s.Length) {
				string op;
				if (s[i] == '*' || s[i] == '/') op = s[i].ToString();
				else if (ismod()) op = "mod";
				else break;
				i += op == "mod" ? 3 : 1;
				left = bin(op, left, unary());
			}
			return left;
		}

		Nd unary() {
			if (i < s.Length && (s[i] == '+' || s[i] == '-')) {
				var op = s[i].ToString();
				i++;
				return uni(op, unary());
			}
			return power();
		}

		Nd power() {
			var left = postfix();
			if (i < s.Length && (s[i] == '^' || s[i] == '√')) {
				var op = s[i].ToString();
				i++;
				return bin(op, left, unary());
			}
			return left;
		}

		Nd postfix() {
			var v = primary();
			while (i < s.Length && (s[i] == '!' || s[i] == '%')) {
				v = s[i] == '!' ? post(v) : pct(v);
				i++;
			}
			return v;
		}

		Nd primary() {
			if (i >= s.Length) throw new InvalidOperationException();
			if (s[i] == '(') {
				i++;
				var v = expr();
				if (i >= s.Length || s[i] != ')') throw new InvalidOperationException();
				i++;
				return v;
			}
			if (isdigit(s[i]) || s[i] == '.') return num(readnum());
			var id = readid();
			if (id == "pi") return num(Math.PI);
			if (id == "e") return num(Math.E);
			if (i >= s.Length || s[i] != '(') throw new InvalidOperationException();
			i++;
			var arg = expr();
			if (i >= s.Length || s[i] != ')') throw new InvalidOperationException();
			i++;
			return call(id, arg);
		}

		bool ismod() {
			return i + 3 <= s.Length && s[i] == 'm' && s[i + 1] == 'o' && s[i + 2] == 'd' &&
				(i + 3 == s.Length || !letter(s[i + 3]));
		}

		double readnum() {
			var start = i;
			if (s[i] == '.') i++;
			if (i >= s.Length || !isdigit(s[i]) && s[start] != '.') throw new InvalidOperationException();
			while (i < s.Length && isdigit(s[i])) i++;
			if (s[start] != '.' && i < s.Length && s[i] == '.') {
				i++;
				while (i < s.Length && isdigit(s[i])) i++;
			}
			if (i < s.Length && (s[i] == 'E' || s[i] == 'e')) {
				var k = i;
				i++;
				if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
				var d0 = i;
				while (i < s.Length && isdigit(s[i])) i++;
				if (i == d0) i = k;
			}
			return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
		}

		string readid() {
			var start = i;
			while (i < s.Length && letter(s[i])) i++;
			if (start == i) throw new InvalidOperationException();
			return s.Substring(start, i - start);
		}

		double ev(Nd n) {
			if (n.kind == NUM) return n.num;
			if (n.kind == UNI) {
				var v = ev(n.left);
				return n.op == "-" ? -v : v;
			}
			if (n.kind == POST) return fact(ev(n.left));
			if (n.kind == PCT) return ev(n.left) / 100.0;
			if (n.kind == CALL) return callv(n.op, ev(n.left));
			var lv = ev(n.left);
			if (n.right.kind == PCT && (n.op == "+" || n.op == "-" || n.op == "*" || n.op == "/"))
				return percent(n.op, lv, ev(n.right.left));
			var rv = ev(n.right);
			if (n.op == "+") return lv + rv;
			if (n.op == "-") return lv - rv;
			if (n.op == "*") return lv * rv;
			if (n.op == "/") {
				if (rv == 0) throw new InvalidOperationException();
				return lv / rv;
			}
			if (n.op == "mod") {
				if (rv == 0) throw new InvalidOperationException();
				return lv % rv;
			}
			if (n.op == "^") return Math.Pow(lv, rv);
			if (Math.Abs(lv) < 1e-15) throw new InvalidOperationException();
			if (rv < 0) {
				var round = Math.Round(lv);
				if (Math.Abs(lv - round) > 1e-9 || ((int)round) % 2 == 0)
					throw new InvalidOperationException();
				return -Math.Pow(-rv, 1.0 / lv);
			}
			return Math.Pow(rv, 1.0 / lv);
		}

		double callv(string name, double x) {
			if (name == "sin") return Math.Sin(radin(x));
			if (name == "cos") return Math.Cos(radin(x));
			if (name == "tan") return Math.Tan(radin(x));
			if (name == "asin") return radout(Math.Asin(x));
			if (name == "acos") return radout(Math.Acos(x));
			if (name == "atan") return radout(Math.Atan(x));
			if (name == "ln") return Math.Log(x);
			if (name == "log") return Math.Log10(x);
			if (name == "sqrt") return Math.Sqrt(x);
			if (name == "cbrt") return x < 0 ? -Math.Pow(-x, 1.0 / 3) : Math.Pow(x, 1.0 / 3);
			if (name == "abs") return Math.Abs(x);
			if (name == "exp") return Math.Exp(x);
			throw new InvalidOperationException();
		}

		double radin(double x) => deg ? x * Math.PI / 180.0 : x;
		double radout(double x) => deg ? x * 180.0 / Math.PI : x;

		static double percent(string op, double left, double raw) {
			if (op == "+") return left + left * raw / 100.0;
			if (op == "-") return left - left * raw / 100.0;
			if (op == "*") return left * raw / 100.0;
			if (op == "/") {
				if (raw == 0) throw new InvalidOperationException();
				return left / (raw / 100.0);
			}
			if (op == "mod" || op == "^" || op == "√") return left;
			throw new InvalidOperationException();
		}

		static double fact(double v) {
			if (v < 0 || v > 170 || Math.Abs(v - Math.Round(v)) > 1e-9)
				throw new InvalidOperationException();
			var n = (int)Math.Round(v);
			double r = 1;
			for (var k = 2; k <= n; k++) r *= k;
			return r;
		}

		static bool isdigit(char c) => c >= '0' && c <= '9';
		static bool letter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

		static Nd num(double v) => new Nd { kind = NUM, num = v };
		static Nd bin(string op, Nd l, Nd r) => new Nd { kind = BIN, op = op, left = l, right = r };
		static Nd uni(string op, Nd a) => new Nd { kind = UNI, op = op, left = a };
		static Nd post(Nd a) => new Nd { kind = POST, left = a };
		static Nd pct(Nd a) => new Nd { kind = PCT, left = a };
		static Nd call(string name, Nd a) => new Nd { kind = CALL, op = name, left = a };
	}

	static readonly string[] FUNCS = {
		"asin(", "acos(", "atan(", "sqrt(", "cbrt(", "abs(", "exp(",
		"sin(", "cos(", "tan(", "ln(", "log(", "10^("
	};

	void insertnum(string bit) {
		if (Done) Expr = "";
		Done = false;
		Ok = true;
		if (Expr.Length + bit.Length > 180) return;
		Expr += bit;
		preview();
	}

	void put(string bit) {
		if (bit.Length == 0) return;
		if (Done && fresh(bit)) Expr = "";
		else if (Done && isop(bit)) Expr = Fmt(Ans);
		Done = false;
		Ok = true;
		if (Expr.Length + bit.Length > 180) return;
		Expr += bit;
		preview();
	}

	void eq() {
		var src = Expr.Length == 0 ? Fmt(Ans) : Expr;
		if (!TryEval(src, Deg, out var v)) {
			Ok = false;
			return;
		}
		Ans = v;
		Expr = src;
		Value = Fmt(v);
		Ok = true;
		Done = true;
	}

	void clear() {
		Expr = "";
		Value = "0";
		Ok = true;
		Done = false;
	}

	void back() {
		if (Done) {
			clear();
			return;
		}
		Expr = cutone(Expr);
		Ok = true;
		preview();
	}

	void entryclear() {
		if (Done) {
			clear();
			return;
		}
		if (!numspan(Expr, out var a, out _)) {
			preview();
			return;
		}
		if (a > 0 && Expr[a - 1] == '-' && unaryminus(Expr, a - 1)) a--;
		Expr = Expr.Substring(0, a);
		Ok = true;
		preview();
	}

	void sign() {
		if (Done) Expr = Fmt(Ans);
		Done = false;
		Ok = true;
		if (Expr.EndsWith("E-", StringComparison.Ordinal))
			Expr = Expr.Substring(0, Expr.Length - 1);
		else if (Expr.EndsWith("E+", StringComparison.Ordinal))
			Expr = Expr.Substring(0, Expr.Length - 1) + "-";
		else if (Expr.EndsWith("E", StringComparison.Ordinal))
			Expr += "-";
		else if (numspan(Expr, out var a, out _)) {
			if (a > 0 && Expr[a - 1] == '-' && unaryminus(Expr, a - 1))
				Expr = Expr.Substring(0, a - 1) + Expr.Substring(a);
			else
				Expr = Expr.Substring(0, a) + "-" + Expr.Substring(a);
		}
		else Expr = "-" + Expr;
		preview();
	}

	void mem(string key) {
		if (!current(out var v)) {
			Ok = false;
			return;
		}
		if (key == "ms") Memory = v;
		else if (key == "mp") Memory += v;
		else Memory -= v;
	}

	void preview() {
		if (Expr.Length == 0) {
			Value = "0";
			Ok = true;
			return;
		}
		if (TryEval(Expr, Deg, out var v)) {
			Value = Fmt(v);
			Ok = true;
			return;
		}
		Ok = true;
		Value = numspan(Expr, out var a, out var b) ? Expr.Substring(a, b - a) : "0";
	}

	bool current(out double v) {
		if (Done) {
			v = Ans;
			return true;
		}
		if (Expr.Length == 0) {
			v = 0;
			return true;
		}
		if (TryEval(Expr, Deg, out v)) return true;
		if (numspan(Expr, out var a, out var b) &&
			double.TryParse(Expr.Substring(a, b - a), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
			return true;
		v = 0;
		return false;
	}

	static string token(string key) {
		if (key == "pi") return "π";
		if (key == "tenx") return "10^(";
		if (key == "sq") return "^2";
		if (key == "inv") return "^-1";
		if (key == "nroot") return "√";
		if (key == "expn") return "E";
		if (key == "pow") return "^";
		if (key == "fact") return "!";
		if (key == "pct") return "%";
		if (key == "sin" || key == "cos" || key == "tan" || key == "asin" || key == "acos" || key == "atan" ||
			key == "ln" || key == "log" || key == "sqrt" || key == "cbrt" || key == "abs" || key == "exp")
			return key + "(";
		return key;
	}

	static bool fresh(string bit) {
		if (bit.Length == 0) return false;
		var c = bit[0];
		if (c >= '0' && c <= '9') return true;
		if (c == '.' || c == '(') return true;
		return bit == "π" || bit.EndsWith("(", StringComparison.Ordinal) || bit == "E";
	}

	static bool isop(string bit) {
		return bit == "+" || bit == "-" || bit == "*" || bit == "/" || bit == "^" || bit == "√" || bit == "mod";
	}

	static string cutone(string s) {
		foreach (var f in FUNCS)
			if (s.EndsWith(f, StringComparison.Ordinal))
				return s.Substring(0, s.Length - f.Length);
		if (s.EndsWith("mod", StringComparison.Ordinal)) return s.Substring(0, s.Length - 3);
		if (s.EndsWith("pi", StringComparison.Ordinal)) return s.Substring(0, s.Length - 2);
		if (s.Length == 0) return s;
		return s.Substring(0, s.Length - 1);
	}

	static bool unaryminus(string s, int at) {
		if (at < 0 || s[at] != '-') return false;
		if (at == 0) return true;
		var p = s[at - 1];
		return p == '+' || p == '-' || p == '*' || p == '/' || p == '^' || p == '√' || p == '(' || p == 'E' || p == 'e';
	}

	static bool numspan(string s, out int a, out int b) {
		a = 0;
		b = s.Length;
		var j = b;
		while (j > 0 && s[j - 1] >= '0' && s[j - 1] <= '9') j--;
		if (j == b) return false;
		if (j > 0 && (s[j - 1] == 'E' || s[j - 1] == 'e')) {
			j--;
			if (j > 0 && (s[j - 1] == '+' || s[j - 1] == '-')) j--;
			var k = j;
			while (j > 0 && s[j - 1] >= '0' && s[j - 1] <= '9') j--;
			if (j == k) return false;
		}
		if (j > 0 && s[j - 1] == '.') {
			j--;
			while (j > 0 && s[j - 1] >= '0' && s[j - 1] <= '9') j--;
		}
		a = j;
		return a < b;
	}
}
