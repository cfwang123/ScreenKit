(function(){
	function textop(op){
		if (op === "yomi" || op === "yomimono") {
			SK.post("/api/jpyomi", { text: SK.$("tx-in").value, mono: op === "yomimono" }, function(data){
				var ruby = data.ruby || "";
				var yomi = data.yomi || "";
				SK.$("tx-out").value = ruby && yomi && ruby !== yomi ? ruby + "\n" + yomi : (yomi || ruby);
			}, "tx-msg");
			return;
		}
		if (op === "trad" || op === "simp") {
			SK.post("/api/zhconv", { text: SK.$("tx-in").value, to: op }, function(data){
				SK.$("tx-out").value = data.text || "";
			}, "tx-msg");
			return;
		}
		if (op === "md5" || op === "sha" || op === "jsonmin" || op === "htmlenc" || op === "htmldec"
			|| op === "hmin" || op === "cmin" || op === "jsmin"
			|| op === "jesc" || op === "juesc" || op === "xml" || op === "xmlmin"
			|| op === "sql" || op === "sqlmin" || op === "hjs"
			|| op === "name" || op === "cron" || op === "tok" || op === "ua") {
			textlocal(op);
			return;
		}
		SK.post("/api/text", { text: SK.$("tx-in").value, op: op }, function(data){
			SK.$("tx-out").value = data.text || "";
		}, "tx-msg");
	}

	function textlocal(op){
		var text = SK.$("tx-in").value;
		if (op === "sha") {
			if (text.length > 200000) {
				SK.msg("tx-msg", "文字太长", true);
				return;
			}
			if (!window.crypto || !crypto.subtle || typeof crypto.subtle.digest !== "function") {
				SK.msg("tx-msg", "浏览器没有 SHA-256", true);
				return;
			}
			SK.msg("tx-msg", "计算中");
			crypto.subtle.digest("SHA-256", SK.utf8bytes(text)).then(function(buf){
				SK.$("tx-out").value = SK.hexbytes(new Uint8Array(buf));
				SK.msg("tx-msg", "完成");
			}, function(){
				SK.msg("tx-msg", "SHA-256 失败", true);
			});
			return;
		}
		try {
			var out = "";
			if (op === "md5") out = md5hex(text);
			else if (op === "jsonmin") out = jsonmin(text);
			else if (op === "jesc") out = jsonesc(text, "enc");
			else if (op === "juesc") out = jsonesc(text, "dec");
			else if (op === "xml") out = xmlfmt(text, "pretty");
			else if (op === "xmlmin") out = xmlfmt(text, "min");
			else if (op === "sql") out = sqlfmt(text, "pretty");
			else if (op === "sqlmin") out = sqlfmt(text, "min");
			else if (op === "hjs") out = html2js(text);
			else if (op === "name") out = nameconv(text);
			else if (op === "cron") out = crondesc(text);
			else if (op === "tok") out = tokencount(text);
			else if (op === "ua") out = uaparse(text);
			else if (op === "htmlenc") out = SK.htmlenc(text);
			else if (op === "htmldec") out = htmldec(text);
			else if (op === "hmin") out = htmlmin(text);
			else if (op === "cmin") out = cssmin(text);
			else if (op === "jsmin") out = jsmin(text);
			SK.$("tx-out").value = out;
			SK.msg("tx-msg", "完成");
		}
		catch (ex) {
			SK.$("tx-out").value = "";
			SK.msg("tx-msg", ex && ex.message ? ex.message : "失败", true);
		}
	}

	function htmldec(text){
		text = String(text);
		if (text.length > 200000) throw new Error("文字太长");
		var named = {
			amp: "&", lt: "<", gt: ">", quot: '"', apos: "'", nbsp: "\u00a0",
			copy: "\u00a9", reg: "\u00ae", trade: "\u2122", mdash: "\u2014",
			ndash: "\u2013", hellip: "\u2026", middot: "\u00b7", times: "\u00d7", divide: "\u00f7",
		};
		return text.replace(/&(#x[0-9a-f]+|#\d+|[a-z][a-z0-9]*);/gi, function(all, body){
			if (body.charAt(0) !== "#") {
				var v = named[body.toLowerCase()];
				return v == null ? all : v;
			}
			var hex = body.charAt(1) === "x" || body.charAt(1) === "X";
			var n = parseInt(body.slice(hex ? 2 : 1), hex ? 16 : 10);
			if (!isFinite(n) || n < 0 || n > 0x10FFFF) return all;
			return String.fromCodePoint(n);
		});
	}

	function jsonmin(text){
		text = String(text);
		if (text.length > 200000) throw new Error("文字太长");
		try { return JSON.stringify(JSON.parse(text)); }
		catch (ex) { throw new Error("不是合法的 JSON"); }
	}

	function md5hex(text){
		var bytes = SK.utf8bytes(text);
		var n = bytes.length;
		var bitLen = n * 8;
		var withPad = n + 1;
		while (withPad % 64 !== 56) withPad++;
		var buf = new Uint8Array(withPad + 8);
		buf.set(bytes);
		buf[n] = 0x80;
		var lo = bitLen >>> 0;
		var hi = Math.floor(bitLen / 0x100000000);
		buf[withPad] = lo & 255;
		buf[withPad + 1] = (lo >>> 8) & 255;
		buf[withPad + 2] = (lo >>> 16) & 255;
		buf[withPad + 3] = (lo >>> 24) & 255;
		buf[withPad + 4] = hi & 255;
		buf[withPad + 5] = (hi >>> 8) & 255;
		buf[withPad + 6] = (hi >>> 16) & 255;
		buf[withPad + 7] = (hi >>> 24) & 255;
		var k = [
			0xd76aa478, 0xe8c7b756, 0x242070db, 0xc1bdceee, 0xf57c0faf, 0x4787c62a, 0xa8304613, 0xfd469501,
			0x698098d8, 0x8b44f7af, 0xffff5bb1, 0x895cd7be, 0x6b901122, 0xfd987193, 0xa679438e, 0x49b40821,
			0xf61e2562, 0xc040b340, 0x265e5a51, 0xe9b6c7aa, 0xd62f105d, 0x02441453, 0xd8a1e681, 0xe7d3fbc8,
			0x21e1cde6, 0xc33707d6, 0xf4d50d87, 0x455a14ed, 0xa9e3e905, 0xfcefa3f8, 0x676f02d9, 0x8d2a4c8a,
			0xfffa3942, 0x8771f681, 0x6d9d6122, 0xfde5380c, 0xa4beea44, 0x4bdecfa9, 0xf6bb4b60, 0xbebfbc70,
			0x289b7ec6, 0xeaa127fa, 0xd4ef3085, 0x04881d05, 0xd9d4d039, 0xe6db99e5, 0x1fa27cf8, 0xc4ac5665,
			0xf4292244, 0x432aff97, 0xab9423a7, 0xfc93a039, 0x655b59c3, 0x8f0ccc92, 0xffeff47d, 0x85845dd1,
			0x6fa87e4f, 0xfe2ce6e0, 0xa3014314, 0x4e0811a1, 0xf7537e82, 0xbd3af235, 0x2ad7d2bb, 0xeb86d391,
		];
		var shift = [
			7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
			5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
			4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
			6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
		];
		var a0 = 0x67452301;
		var b0 = 0xefcdab89;
		var c0 = 0x98badcfe;
		var d0 = 0x10325476;
		var off;
		for (off = 0; off < buf.length; off += 64) {
			var m = new Uint32Array(16);
			var j;
			for (j = 0; j < 16; j++) {
				var p = off + j * 4;
				m[j] = (buf[p] | (buf[p + 1] << 8) | (buf[p + 2] << 16) | (buf[p + 3] << 24)) >>> 0;
			}
			var a = a0;
			var b = b0;
			var c = c0;
			var d = d0;
			var t;
			for (t = 0; t < 64; t++) {
				var f;
				var g;
				if (t < 16) {
					f = (b & c) | (~b & d);
					g = t;
				}
				else if (t < 32) {
					f = (d & b) | (~d & c);
					g = (5 * t + 1) % 16;
				}
				else if (t < 48) {
					f = b ^ c ^ d;
					g = (3 * t + 5) % 16;
				}
				else {
					f = c ^ (b | ~d);
					g = (7 * t) % 16;
				}
				f = (f + a + k[t] + m[g]) >>> 0;
				var rot = ((f << shift[t]) | (f >>> (32 - shift[t]))) >>> 0;
				a = d;
				d = c;
				c = b;
				b = (b + rot) >>> 0;
			}
			a0 = (a0 + a) >>> 0;
			b0 = (b0 + b) >>> 0;
			c0 = (c0 + c) >>> 0;
			d0 = (d0 + d) >>> 0;
		}
		return lehex(a0) + lehex(b0) + lehex(c0) + lehex(d0);
	}

	function lehex(n){
		var s = "";
		var i;
		for (i = 0; i < 4; i++) {
			var b = (n >>> (i * 8)) & 255;
			s += (b < 16 ? "0" : "") + b.toString(16);
		}
		return s;
	}

	function nameconv(text){
		var raw = String(text).trim();
		if (!raw) throw new Error("请输入名称");
		if (raw.length > 200) throw new Error("太长");
		var bits = raw.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[^A-Za-z0-9]+/g, " ").split(/\s+/);
		var w = [];
		var i;
		for (i = 0; i < bits.length; i++) if (bits[i]) w.push(bits[i].toLowerCase());
		if (!w.length) throw new Error("没有可用的字母或数字");
		function cap(s){ return s.charAt(0).toUpperCase() + s.slice(1); }
		var camel = w[0];
		var pascal = "";
		for (i = 0; i < w.length; i++) {
			if (i > 0) camel += cap(w[i]);
			pascal += cap(w[i]);
		}
		return "小驼峰  " + camel
			+ "\n大驼峰  " + pascal
			+ "\n下划线  " + w.join("_")
			+ "\n大写下划线  " + w.join("_").toUpperCase()
			+ "\n短横线  " + w.join("-");
	}

	function jsonesc(text, mode){
		text = String(text);
		if (text.length > 200000) throw new Error("文字太长");
		if (mode === "dec") {
			var src = text.trim();
			if (!src) throw new Error("请输入文字");
			if (src.charAt(0) !== '"') src = '"' + src + '"';
			var v;
			try { v = JSON.parse(src); }
			catch (ex) { throw new Error("不是合法的 JSON 字符串"); }
			if (typeof v !== "string") throw new Error("请输入 JSON 字符串");
			return v;
		}
		return JSON.stringify(text);
	}

	function uaparse(text){
		var s = String(text).trim();
		if (!s) throw new Error("请输入 User-Agent");
		if (s.length > 2000) throw new Error("太长");
		var os = "未知";
		if (/Windows NT 10\.0/.test(s)) os = "Windows 10 或 11";
		else if (/Windows NT 6\.3/.test(s)) os = "Windows 8.1";
		else if (/Windows NT 6\.1/.test(s)) os = "Windows 7";
		else if (/Android/.test(s)) os = "Android";
		else if (/iPhone|iPad|iPod/.test(s)) os = "iOS";
		else if (/Mac OS X/.test(s)) os = "macOS";
		else if (/Linux/.test(s)) os = "Linux";
		var br = "未知";
		var m;
		if ((m = /Edg\/(\d+)/.exec(s))) br = "Edge " + m[1];
		else if ((m = /OPR\/(\d+)/.exec(s))) br = "Opera " + m[1];
		else if ((m = /Chrome\/(\d+)/.exec(s))) br = "Chrome " + m[1];
		else if ((m = /Firefox\/(\d+)/.exec(s))) br = "Firefox " + m[1];
		else if (/Safari\//.test(s) && (m = /Version\/(\d+)/.exec(s))) br = "Safari " + m[1];
		return "系统  " + os + "\n浏览器  " + br;
	}

	function crondesc(expr){
		var s = String(expr).trim().replace(/\s+/g, " ");
		if (!s) throw new Error("请输入表达式");
		var alias = {
			"@yearly": "0 0 1 1 *",
			"@annually": "0 0 1 1 *",
			"@monthly": "0 0 1 * *",
			"@weekly": "0 0 * * 0",
			"@daily": "0 0 * * *",
			"@midnight": "0 0 * * *",
			"@hourly": "0 * * * *",
		};
		if (alias[s]) s = alias[s];
		else if (s.charAt(0) === "@") throw new Error("不支持这个 @ 简写");
		var p = s.split(" ");
		var names;
		var ranges;
		if (p.length === 5) {
			names = ["分", "时", "日", "月", "周"];
			ranges = [[0, 59], [0, 23], [1, 31], [1, 12], [0, 7]];
		}
		else if (p.length === 6) {
			names = ["秒", "分", "时", "日", "月", "周"];
			ranges = [[0, 59], [0, 59], [0, 23], [1, 31], [1, 12], [0, 7]];
		}
		else throw new Error("用 5 段：分 时 日 月 周");
		var lines = [];
		var i;
		for (i = 0; i < p.length; i++) lines.push(names[i] + "  " + cronpart(p[i], ranges[i][0], ranges[i][1]));
		return lines.join("\n");
	}

	function cronpart(part, lo, hi){
		if (part === "*") return "每档";
		var step = part.split("/");
		if (step.length > 2) throw new Error("表达式无效");
		var base = step[0];
		var every = "";
		if (step.length === 2) {
			if (!/^\d+$/.test(step[1]) || Number(step[1]) < 1) throw new Error("步长无效");
			every = "，每隔 " + step[1];
		}
		if (base === "*") return "每档" + every;
		var bits = base.split(",");
		var out = [];
		var i;
		for (i = 0; i < bits.length; i++) {
			var bit = bits[i];
			var span = bit.split("-");
			if (span.length === 1) {
				cronnum(span[0], lo, hi);
				out.push(span[0]);
			}
			else if (span.length === 2) {
				cronnum(span[0], lo, hi);
				cronnum(span[1], lo, hi);
				out.push(span[0] + " 到 " + span[1]);
			}
			else throw new Error("表达式无效");
		}
		return out.join("、") + every;
	}

	function cronnum(s, lo, hi){
		if (!/^\d+$/.test(s)) throw new Error("只支持数字");
		var n = Number(s);
		if (n < lo || n > hi) throw new Error("有数字超出范围");
	}

	function htmlmin(text){
		var s = String(text);
		if (s.length > 200000) throw new Error("文字太长");
		var out = "";
		var i = 0;
		var keep = "";
		while (i < s.length) {
			if (!keep && s.slice(i, i + 4) === "<!--") {
				var end = s.indexOf("-->", i + 4);
				if (end < 0) { out += s.slice(i); break; }
				i = end + 3;
				continue;
			}
			if (s.charAt(i) === "<") {
				var rest = s.slice(i);
				var open = /^<(pre|script|style)\b/i.exec(rest);
				var close = /^<\/(pre|script|style)\b/i.exec(rest);
				if (!keep && open) keep = open[1].toLowerCase();
				else if (keep && close && close[1].toLowerCase() === keep) keep = "";
			}
			if (!keep && isws(s.charAt(i)) && out.charAt(out.length - 1) === ">") {
				var j = i;
				while (j < s.length && isws(s.charAt(j))) j++;
				if (s.charAt(j) === "<") { i = j; continue; }
			}
			out += s.charAt(i);
			i++;
		}
		return out.trim();
	}

	function isws(c){
		return c === " " || c === "\n" || c === "\r" || c === "\t" || c === "\f";
	}

	function cssmin(text){
		var s = String(text);
		if (s.length > 200000) throw new Error("文字太长");
		var out = "";
		var i = 0;
		var q = "";
		while (i < s.length) {
			var c = s.charAt(i);
			if (q) {
				out += c;
				if (c === "\\" && i + 1 < s.length) { out += s.charAt(i + 1); i += 2; continue; }
				if (c === q) q = "";
				i++;
				continue;
			}
			if (c === '"' || c === "'") { q = c; out += c; i++; continue; }
			if (c === "/" && s.charAt(i + 1) === "*") {
				var end = s.indexOf("*/", i + 2);
				if (end < 0) throw new Error("注释没有结束");
				i = end + 2;
				continue;
			}
			out += c;
			i++;
		}
		return out.replace(/\s+/g, " ").replace(/\s*([{}:;,])\s*/g, "$1").trim();
	}

	function xmlfmt(text, mode){
		var s = String(text).trim();
		if (!s) throw new Error("请输入 XML");
		if (s.length > 200000) throw new Error("文字太长");
		if (typeof DOMParser === "function") {
			var doc = new DOMParser().parseFromString(s, "application/xml");
			if (doc.getElementsByTagName("parsererror").length) throw new Error("不是合法的 XML");
		}
		s = s.replace(/>\s+</g, "><");
		if (mode === "min") return s;
		var lines = s.replace(/(>)(<)/g, "$1\n$2").split("\n");
		var pad = 0;
		var out = [];
		var i;
		for (i = 0; i < lines.length; i++) {
			var line = lines[i];
			if (/^<\/[^>]+>/.test(line)) pad = Math.max(pad - 1, 0);
			var indent = "";
			var k;
			for (k = 0; k < pad; k++) indent += "  ";
			out.push(indent + line);
			if (/^<[^!?/][^>]*>$/.test(line) && !/\/>$/.test(line)) pad++;
		}
		return out.join("\n");
	}

	function jsmin(text){
		var s = String(text);
		if (s.length > 200000) throw new Error("文字太长");
		var out = "";
		var i = 0;
		var q = "";
		while (i < s.length) {
			var c = s.charAt(i);
			var n = s.charAt(i + 1);
			if (q) {
				out += c;
				if (c === "\\" && i + 1 < s.length) { out += n; i += 2; continue; }
				if (c === q) q = "";
				i++;
				continue;
			}
			if (c === '"' || c === "'" || c === "`") { q = c; out += c; i++; continue; }
			if (c === "/" && n === "/") {
				i += 2;
				while (i < s.length && s.charAt(i) !== "\n") i++;
				continue;
			}
			if (c === "/" && n === "*") {
				var end = s.indexOf("*/", i + 2);
				if (end < 0) throw new Error("注释没有结束");
				i = end + 2;
				continue;
			}
			out += c;
			i++;
		}
		return out.replace(/[ \t]+\n/g, "\n").replace(/\n{3,}/g, "\n\n").trim();
	}

	function sqlfmt(text, mode){
		var s = String(text).trim();
		if (!s) throw new Error("请输入 SQL");
		if (s.length > 200000) throw new Error("文字太长");
		s = s.replace(/--[^\n]*/g, " ").replace(/\s+/g, " ").trim();
		if (mode === "min") return s;
		var keys = ["order by", "group by", "inner join", "left join", "right join", "insert into", "delete from", "select", "from", "where", "having", "limit", "values", "update", "set", "and", "or"];
		var i;
		for (i = 0; i < keys.length; i++) {
			var k = keys[i];
			var re = new RegExp("\\b" + k.replace(" ", "\\s+") + "\\b", "ig");
			s = s.replace(re, "\n" + k.toUpperCase());
		}
		return s.replace(/^\n+/, "").replace(/\n{2,}/g, "\n").trim();
	}

	function tokencount(text){
		var s = String(text);
		if (s.length > 200000) throw new Error("文字太长");
		var n = 0;
		var re = /[\u3400-\u9fff]|[A-Za-z]+|\d+|[^\s]/g;
		while (re.exec(s)) n++;
		return "约 " + n + " 个 token\n字符  " + s.length + "\n这是按汉字、英文单词和符号粗算的，不是某个模型的分词器。";
	}

	function html2js(text){
		var s = String(text);
		if (s.length > 200000) throw new Error("文字太长");
		return "var html = " + JSON.stringify(s) + ";";
	}

	SK.textop = textop;
	SK.textlocal = textlocal;
	SK.htmldec = htmldec;
	SK.jsonmin = jsonmin;
	SK.md5hex = md5hex;
	SK.lehex = lehex;
	SK.nameconv = nameconv;
	SK.jsonesc = jsonesc;
	SK.uaparse = uaparse;
	SK.crondesc = crondesc;
	SK.cronpart = cronpart;
	SK.cronnum = cronnum;
	SK.htmlmin = htmlmin;
	SK.isws = isws;
	SK.cssmin = cssmin;
	SK.xmlfmt = xmlfmt;
	SK.jsmin = jsmin;
	SK.sqlfmt = sqlfmt;
	SK.tokencount = tokencount;
	SK.html2js = html2js;
})();
