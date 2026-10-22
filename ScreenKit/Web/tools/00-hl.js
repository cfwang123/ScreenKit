(function(){
	// 速查表的简单语法高亮：SQL 关键字/函数/字符串，命令行里的命令、参数、变量、
	// 管道和注释。只做着色，不改文本，复制时仍取原始字符串。
	var KW = {};
	("SELECT FROM WHERE GROUP BY ORDER HAVING LIMIT OFFSET JOIN LEFT RIGHT INNER OUTER FULL CROSS ON " +
		"AS AND OR NOT NULL IS IN LIKE ILIKE BETWEEN EXISTS DISTINCT UNION ALL INTERSECT EXCEPT MINUS " +
		"INSERT INTO VALUES UPDATE SET DELETE CREATE TABLE VIEW INDEX ALTER DROP TRUNCATE CASE WHEN THEN ELSE END " +
		"WITH RECURSIVE OVER PARTITION ROWS RANGE PRECEDING FOLLOWING CURRENT ROW UNBOUNDED FETCH FIRST NEXT ONLY " +
		"ASC DESC NULLS LAST RETURNING CONFLICT DO NOTHING TOP MERGE USING MATCHED DECLARE EXEC EXECUTE GO " +
		"BEGIN COMMIT ROLLBACK PRAGMA SHOW DESCRIBE EXPLAIN ANALYZE VACUUM ATTACH DATABASE DUAL CONNECT START " +
		"PRIOR LEVEL IDENTITY GENERATED ALWAYS VIRTUAL STORED WITHOUT STRICT FILTER WITHIN OUTPUT INSERTED")
		.split(" ").forEach(function(k){ KW[k] = 1; });

	var SQL_RE = /(--[^\n]*|\/\*[\s\S]*?\*\/)|('(?:[^']|'')*'|"(?:[^"]|"")*"|`[^`]*`)|(\b\d+(?:\.\d+)?\b)|([A-Za-z_][A-Za-z0-9_]*\()|([A-Za-z_][A-Za-z0-9_$]*)|(\|\||->>|->|#>|#>>|<>|!=|>=|<=|::|[|,;()[\].=<>+*\/%-])/g;

	var CMP = /^-(eq|ne|gt|lt|ge|le|like|notlike|match|notmatch|contains|notcontains|in|notin|replace|split|join|is|isnot|as|not)$/i;

	var KEEPCMD = { sudo: 1, doas: 1, env: 1, nohup: 1, time: 1, xargs: 1, watch: 1, command: 1, exec: 1, builtin: 1, nice: 1 };

	SK.hl = function(mode, text){
		var s = String(text == null ? "" : text);
		if (!s) return "";
		if (mode === "sql") return sql(s);
		return shell(s, mode);
	};

	function sql(s){
		return scan(s, SQL_RE, function(m){
			var com = m[1], str = m[2], num = m[3], fn = m[4], word = m[5], op = m[6];
			if (com) return sp("cm", com);
			if (str) return sp("s", str);
			if (num) return sp("n", num);
			if (fn) {
				var name = fn.slice(0, -1);
				if (KW[name.toUpperCase()]) return sp("k", name) + esc("(");
				return sp("f", name) + esc("(");
			}
			if (word) return KW[word.toUpperCase()] ? sp("k", word) : esc(word);
			if (op) return sp("o", op);
			return esc(m[0]);
		});
	}

	function shell(s, mode){
		var out = "";
		var i = 0;
		var n = s.length;
		var expectCmd = true;
		var t2, m;
		while (i < n) {
			var ch = s.charAt(i);
			if (ch === " " || ch === "\t" || ch === "\n") {
				out += ch;
				i++;
				continue;
			}
			if (mode !== "cmd" && ch === "#") {
				out += sp("cm", s.slice(i));
				break;
			}
			if (mode === "cmd" && (s.substr(i, 2) === "::" || (expectCmd && s.substr(i, 3).toLowerCase() === "rem" && !/[A-Za-z0-9_]/.test(s.charAt(i + 3))))) {
				out += sp("cm", s.slice(i));
				break;
			}
			if (ch === "\"" || ch === "'") {
				var end = strend(s, i, mode);
				out += sp("s", s.slice(i, end));
				i = end;
				expectCmd = false;
				continue;
			}
			if (mode === "cmd" && ch === "%") {
				m = /^%~?[A-Za-z0-9_*]+%|^%%[A-Za-z]/.exec(s.slice(i));
				if (m) {
					out += sp("v", m[0]);
					i += m[0].length;
					expectCmd = false;
					continue;
				}
			}
			if (mode === "cmd" && ch === "!") {
				m = /^![A-Za-z0-9_]+!/.exec(s.slice(i));
				if (m) {
					out += sp("v", m[0]);
					i += m[0].length;
					expectCmd = false;
					continue;
				}
			}
			if (mode !== "cmd" && ch === "$") {
				m = /^\$\{[^}]*\}|^\$[A-Za-z_][A-Za-z0-9_]*(?::[A-Za-z_][A-Za-z0-9_]*)?|^\$[0-9@?*#$!]/.exec(s.slice(i));
				if (m) {
					out += sp("v", m[0]);
					i += m[0].length;
					expectCmd = false;
					continue;
				}
			}
			t2 = s.substr(i, 2);
			if (s.substr(i, 4) === "2>&1") {
				out += sp("o", "2>&1");
				i += 4;
				expectCmd = false;
				continue;
			}
			if (t2 === "||" || t2 === "&&" || t2 === ">>" || t2 === "|&" || t2 === "2>") {
				out += sp("o", t2);
				i += 2;
				expectCmd = (t2 === "||" || t2 === "&&" || t2 === "|&");
				continue;
			}
			if (ch === "|" || ch === "&" || ch === ";") {
				out += sp("o", ch);
				i++;
				expectCmd = true;
				continue;
			}
			if (ch === ">" || ch === "<" || (mode === "cmd" && ch === "^")) {
				if (mode === "cmd" && ch === "^" && i + 1 < n) {
					out += sp("o", ch) + esc(s.charAt(i + 1));
					i += 2;
					expectCmd = false;
					continue;
				}
				out += sp("o", ch);
				i++;
				expectCmd = false;
				continue;
			}
			if (mode === "sh" && ch === "\\" && i + 1 < n) {
				out += sp("o", t2);
				i += 2;
				continue;
			}
			m = /^[^\s|&;<>()"']+/.exec(s.slice(i));
			if (m) {
				var tok = m[0];
				if (expectCmd) {
					out += sp("cmd", tok);
					expectCmd = !!KEEPCMD[tok.toLowerCase()];
				}
				else if (CMP.test(tok)) out += sp("o", tok);
				else if (isflag(tok, mode)) out += sp("fl", tok);
				else if (/^\d+$/.test(tok)) out += sp("n", tok);
				else out += esc(tok);
				i += tok.length;
				continue;
			}
			out += esc(ch);
			i++;
		}
		return out;
	}

	function isflag(tok, mode){
		if (tok.length < 2 || tok.charAt(0) !== "-") return false;
		return !/^-\d/.test(tok);
	}

	// 字符串要按各命令行的转义规则找到结尾，避免把后面的代码也染成字符串。
	function strend(s, i, mode){
		var q = s.charAt(i);
		var j = i + 1;
		while (j < s.length) {
			var c = s.charAt(j);
			if (mode === "ps" && c === "`") {
				j += 2;
				continue;
			}
			if (mode !== "ps" && c === "\\" && q === "\"") {
				j += 2;
				continue;
			}
			if (c === q) {
				if (mode !== "ps" && q === "'" && s.charAt(j + 1) === "'") {
					j += 2;
					continue;
				}
				return j + 1;
			}
			j++;
		}
		return s.length;
	}

	function scan(s, re, fn){
		var out = "";
		var last = 0;
		var m;
		re.lastIndex = 0;
		while ((m = re.exec(s))) {
			if (m.index > last) out += esc(s.slice(last, m.index));
			out += fn(m);
			last = m.index + m[0].length;
			if (m[0].length === 0) re.lastIndex++;
		}
		if (last < s.length) out += esc(s.slice(last));
		return out;
	}

	function sp(cls, t){
		return '<span class="hl-' + cls + '">' + esc(t) + "</span>";
	}

	function esc(s){
		return String(s == null ? "" : s)
			.replace(/&/g, "&amp;")
			.replace(/</g, "&lt;")
			.replace(/>/g, "&gt;")
			.replace(/"/g, "&quot;");
	}
})();
