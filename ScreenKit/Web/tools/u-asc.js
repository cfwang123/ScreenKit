(function(){
	// ASCII 0x00-0x7F。控制字符用标准缩写，右边大字预览各类写法。
	var NAMES = ["NUL", "SOH", "STX", "ETX", "EOT", "ENQ", "ACK", "BEL", "BS", "HT", "LF", "VT", "FF", "CR", "SO", "SI",
		"DLE", "DC1", "DC2", "DC3", "DC4", "NAK", "SYN", "ETB", "CAN", "EM", "SUB", "ESC", "FS", "GS", "RS", "US"];
	var CN = ["空字符", "标题开始", "正文开始", "正文结束", "传输结束", "询问", "确认", "响铃", "退格", "水平制表", "换行",
		"垂直制表", "换页", "回车", "移出", "移入", "数据链路转义", "设备控制1", "设备控制2", "设备控制3", "设备控制4",
		"否认", "同步空闲", "传输块结束", "取消", "介质结束", "替换", "转义", "文件分隔符", "组分隔符", "记录分隔符", "单元分隔符"];
	var PUNCT = ["EXCLAMATION MARK", "QUOTATION MARK", "NUMBER SIGN", "DOLLAR SIGN", "PERCENT SIGN", "AMPERSAND",
		"APOSTROPHE", "LEFT PARENTHESIS", "RIGHT PARENTHESIS", "ASTERISK", "PLUS SIGN", "COMMA", "HYPHEN-MINUS",
		"FULL STOP", "SOLIDUS", "COLON", "SEMICOLON", "LESS-THAN SIGN", "EQUALS SIGN", "GREATER-THAN SIGN",
		"QUESTION MARK", "COMMERCIAL AT", "LEFT SQUARE BRACKET", "REVERSE SOLIDUS", "RIGHT SQUARE BRACKET",
		"CIRCUMFLEX ACCENT", "LOW LINE", "GRAVE ACCENT", "LEFT CURLY BRACKET", "VERTICAL LINE",
		"RIGHT CURLY BRACKET", "TILDE"];
	var DIGITS = ["ZERO", "ONE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN", "EIGHT", "NINE"];
	var PUNCTCP = nruns([[33, 47], [58, 64], [91, 96], [123, 126]]);
	var GROUPS = [
		{ name: "全部", codes: nruns([[0, 127]]) },
		{ name: "控制字符", codes: nruns([[0, 31], [127, 127]]) },
		{ name: "空格与标点", codes: nruns([[32, 47], [58, 64], [91, 96], [123, 126]]) },
		{ name: "数字", codes: nruns([[48, 57]]) },
		{ name: "大写字母", codes: nruns([[65, 90]]) },
		{ name: "小写字母", codes: nruns([[97, 122]]) }
	];
	var cur = 0;

	SK.regutil("u-asc", {
		title: "ASCII码表",
		body: function(){
			return '<div class="uquery"><input id="a-q" type="text" placeholder="字符、0x41、65、0b1000001 或 NUL"><span id="a-msg" class="msg"></span></div>'
				+ '<p class="hint">点一下选中并复制。左边按类别分组，控制字符显示标准缩写。最右边是大字、十进制、十六进制、八进制、二进制、转义写法。</p>'
				+ '<div class="unitab"><div class="ugroups" id="a-groups"></div><div class="ugrid a16h" id="a-grid"></div>'
				+ '<aside class="uprev"><div class="uprev-ch ctrl" id="a-ch"></div><dl class="uprev-meta" id="a-meta"></dl></aside></div>';
		},
		run: function(){
			throw new Error("没有这个工具");
		},
		open: function(){
			ascgroups();
			ascshow(0, -1);
		},
		hideGo: true,
		live: false,
		hideOut: true
	});

	function nruns(runs){
		var out = [];
		var i;
		var cp;
		for (i = 0; i < runs.length; i++) {
			for (cp = runs[i][0]; cp <= runs[i][1]; cp++) out.push(cp);
		}
		return out;
	}

	function ascgroups(){
		var html = ['<div class="usum">共 128 个字符（0x00–0x7F）</div>'];
		var i;
		var range;
		var runs;
		for (i = 0; i < GROUPS.length; i++) {
			runs = ascruns(GROUPS[i].codes);
			range = ascrange(GROUPS[i].codes);
			html.push('<button type="button" data-i="' + i + '" title="'
				+ esc(runs.map(ascrun).join("、") + " · 点一下看这一组") + '"><b>' + esc(GROUPS[i].name)
				+ "</b><small>" + (range ? range + " · " : "") + GROUPS[i].codes.length + " 个</small></button>");
		}
		SK.$("a-groups").innerHTML = html.join("");
		SK.$("a-groups").onclick = function(ev){
			var el = ev.target;
			while (el && el.id !== "a-groups") {
				if (el.getAttribute && el.getAttribute("data-i") != null) {
					ascshow(parseInt(el.getAttribute("data-i"), 10), -1);
					return;
				}
				el = el.parentNode;
			}
		};
		SK.$("a-grid").onclick = function(ev){
			var el = ev.target;
			while (el && el.id !== "a-grid") {
				if (el.getAttribute && el.getAttribute("data-cp") != null) {
					var cp = parseInt(el.getAttribute("data-cp"), 10);
					ascmark(cp);
					ascpreview(cp);
					SK.copytext(ascpay(cp), "a-msg");
					return;
				}
				el = el.parentNode;
			}
		};
		SK.$("a-q").oninput = ascquery;
	}

	function ascruns(codes){
		var runs = [];
		var i = 0;
		var a;
		var b;
		while (i < codes.length) {
			a = codes[i];
			b = a;
			while (i + 1 < codes.length && codes[i + 1] === b + 1) {
				i++;
				b = codes[i];
			}
			runs.push([a, b]);
			i++;
		}
		return runs;
	}

	function ascrun(run){
		return asc2(run[0], 16) + (run[0] === run[1] ? "" : "–" + asc2(run[1], 16));
	}

	// 只有单段才把范围写进小字，多段的写不下，放进 title
	function ascrange(codes){
		var runs = ascruns(codes);
		return runs.length === 1 ? ascrun(runs[0]) : "";
	}

	function ascshow(index, focus){
		var g = GROUPS[index];
		var buttons = document.querySelectorAll("#a-groups button");
		var grid = SK.$("a-grid");
		var head = isfull(g);
		var pick = focus >= 0 && g.codes.indexOf(focus) >= 0 ? focus : g.codes[0];
		var html = [];
		var i;
		var cp;
		for (i = 0; i < buttons.length; i++) buttons[i].className = i === index ? "on" : "";
		if (buttons[index]) buttons[index].scrollIntoView({ block: "nearest" });
		cur = index;
		grid.className = "ugrid " + (head ? "a16h" : "a16");
		if (head) {
			html.push('<span class="ahead"></span>');
			for (i = 0; i < 16; i++) html.push('<span class="ahead">' + i.toString(16).toUpperCase() + "</span>");
		}
		for (i = 0; i < g.codes.length; i++) {
			cp = g.codes[i];
			if (head && i % 16 === 0) html.push('<span class="ahead">' + asc2(cp, 16) + "</span>");
			html.push(ascell(cp, cp === pick));
		}
		grid.innerHTML = html.join("");
		var on = grid.querySelector("button.on");
		if (on && focus >= 0) on.scrollIntoView({ block: "center" });
		ascpreview(pick);
	}

	// 整行对齐的组才画行列表头（只有「全部」是 0x00 起的连续整行）
	function isfull(g){
		var runs = ascruns(g.codes);
		return runs.length === 1 && g.codes.length % 16 === 0 && g.codes[0] % 16 === 0;
	}

	function isctrl(cp){
		return cp < 32 || cp === 127;
	}

	function ascell(cp, on){
		var cls = [];
		var text;
		if (isctrl(cp)) {
			text = NAMES[cp] || "DEL";
			cls.push("ctrl");
			if (cp !== 32) cls.push("alt");
		}
		else if (cp === 32) {
			text = "SP";
			cls.push("ctrl");
		}
		else {
			text = String.fromCharCode(cp);
		}
		if (on) cls.push("on");
		return '<button type="button" data-cp="' + cp + '" title="' + esc(asc2(cp, 16) + " " + cp + " " + ascname(cp)) + '"'
			+ (cls.length ? ' class="' + cls.join(" ") + '"' : "") + ">" + esc(text) + "</button>";
	}

	function ascmark(cp){
		var buttons = document.querySelectorAll("#a-grid button");
		var i;
		var cls;
		for (i = 0; i < buttons.length; i++) {
			cls = buttons[i].className.replace(/\bon\b/g, "").replace(/^\s+|\s+$/g, "").replace(/\s+/g, " ");
			if (parseInt(buttons[i].getAttribute("data-cp"), 10) === cp) cls = cls ? cls + " on" : "on";
			buttons[i].className = cls;
		}
	}

	function ascpreview(cp){
		var ctrl = isctrl(cp);
		var box = SK.$("a-ch");
		box.textContent = ctrl ? (NAMES[cp] || "DEL") : String.fromCharCode(cp);
		box.className = ctrl ? "uprev-ch ctrl" : "uprev-ch";
		var rows = [
			["十进制", String(cp)],
			["十六进制", asc2(cp, 16) + "   " + cp.toString(16).toUpperCase()],
			["八进制", "0o" + cp.toString(8)],
			["二进制", ascbin(cp)],
			["字符", ctrl ? "（" + NAMES[cp] + "）" : String.fromCharCode(cp)],
			["名称", ascname(cp)],
			["类别", asckind(cp)],
			["C 转义", ascc(cp)],
			["JavaScript", ascjs(cp)],
			["HTML", "&#" + cp + ";  &#x" + cp.toString(16).toUpperCase() + ";"],
			["URL 编码", "%" + asc2(cp, 16).slice(2)],
			["UTF-8", asc2(cp, 16).slice(2)],
			["可打印", ctrl ? (cp === 127 ? "否（删除）" : "否") : "是"]
		];
		if (ctrl) rows.push(["中文名", cp === 127 ? "删除" : CN[cp]]);
		SK.$("a-meta").innerHTML = rows.map(function(r){
			return "<dt>" + esc(r[0]) + "</dt><dd>" + esc(r[1]) + "</dd>";
		}).join("");
	}

	function asc2(cp, base){
		var s = cp.toString(base).toUpperCase();
		return "0x" + (s.length < 2 ? "0" + s : s);
	}

	function ascbin(cp){
		var s = cp.toString(2);
		while (s.length < 8) s = "0" + s;
		return s.slice(0, 4) + " " + s.slice(4);
	}

	function ascname(cp){
		if (isctrl(cp)) return cp === 127 ? "DELETE" : NAMES[cp];
		if (cp === 32) return "SPACE";
		if (cp >= 48 && cp <= 57) return "DIGIT " + DIGITS[cp - 48];
		if (cp >= 65 && cp <= 90) return "LATIN CAPITAL LETTER " + String.fromCharCode(cp);
		if (cp >= 97 && cp <= 122) return "LATIN SMALL LETTER " + String.fromCharCode(cp);
		return PUNCT[PUNCTCP.indexOf(cp)] || "PRINTABLE";
	}

	function asckind(cp){
		var i;
		for (i = 1; i < GROUPS.length; i++) {
			if (GROUPS[i].codes.indexOf(cp) >= 0) return GROUPS[i].name;
		}
		return "控制字符";
	}

	function ascc(cp){
		if (cp === 0) return "'\\0'";
		if (cp === 7) return "'\\a'";
		if (cp === 8) return "'\\b'";
		if (cp === 9) return "'\\t'";
		if (cp === 10) return "'\\n'";
		if (cp === 11) return "'\\v'";
		if (cp === 12) return "'\\f'";
		if (cp === 13) return "'\\r'";
		if (cp === 39) return "'\\''";
		if (cp === 92) return "'\\\\'";
		return "'" + (cp < 32 || cp === 127 ? "\\x" + asc2(cp, 16).slice(2) : String.fromCharCode(cp)) + "'";
	}

	function ascjs(cp){
		if (cp === 10) return '"\\n"';
		if (cp === 9) return '"\\t"';
		if (cp === 13) return '"\\r"';
		var h = asc2(cp, 16).slice(2);
		return '"\\x' + h + '"   "\\u00' + h + '"';
	}

	// 控制字符复制它的转义写法，可打印字符复制字符本身
	function ascpay(cp){
		if (cp === 127) return "\\x7F";
		if (cp < 32) return ascc(cp).slice(1, -1);
		return String.fromCharCode(cp);
	}

	function ascparse(s){
		var t = String(s || "").trim();
		var m;
		var up;
		var i;
		if (!t) return -2;
		if (t.length === 1 && t.charCodeAt(0) < 128) return t.charCodeAt(0);
		m = /^0x([0-9a-f]{1,2})$/i.exec(t);
		if (m) return parseInt(m[1], 16);
		m = /^([0-9a-f]{1,2})h$/i.exec(t);
		if (m) return parseInt(m[1], 16);
		m = /^([0-9]{1,3})$/.exec(t);
		if (m) {
			i = parseInt(m[1], 10);
			return i <= 127 ? i : -1;
		}
		m = /^0b([01]{1,7})$/.exec(t);
		if (m) return parseInt(m[1], 2);
		m = /^0o([0-7]{1,3})$/.exec(t);
		if (m) return parseInt(m[1], 8);
		up = t.toUpperCase();
		for (i = 0; i < 128; i++) {
			if (isctrl(i) && (NAMES[i] === up || (i === 127 && up === "DEL"))) return i;
		}
		if (up === "SP" || up === "SPACE") return 32;
		return -1;
	}

	function ascquery(){
		var cp = ascparse(SK.$("a-q").value);
		if (cp === -2) {
			SK.msg("a-msg", "");
			return;
		}
		if (cp < 0) {
			SK.msg("a-msg", "没有这个字符", true);
			return;
		}
		var i;
		for (i = 0; i < GROUPS.length; i++) {
			if (GROUPS[i].codes.indexOf(cp) >= 0) {
				SK.msg("a-msg", asc2(cp, 16) + " · " + cp + " · " + (isctrl(cp) ? NAMES[cp] : String.fromCharCode(cp)));
				ascshow(i, cp);
				return;
			}
		}
	}

	function esc(s){
		return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/"/g, "&quot;");
	}

	SK.ascgroups = GROUPS;
})();
