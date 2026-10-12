(function(){
	var GROUPS = [
		{ name: "字母", rows: [
			["A", ".-"], ["B", "-..."], ["C", "-.-."], ["D", "-.."], ["E", "."],
			["F", "..-."], ["G", "--."], ["H", "...."], ["I", ".."], ["J", ".---"],
			["K", "-.-"], ["L", ".-.."], ["M", "--"], ["N", "-."], ["O", "---"],
			["P", ".--."], ["Q", "--.-"], ["R", ".-."], ["S", "..."], ["T", "-"],
			["U", "..-"], ["V", "...-"], ["W", ".--"], ["X", "-..-"], ["Y", "-.--"],
			["Z", "--.."]
		]},
		{ name: "数字", rows: [
			["1", ".----"], ["2", "..---"], ["3", "...--"], ["4", "....-"], ["5", "....."],
			["6", "-...."], ["7", "--..."], ["8", "---.."], ["9", "----."], ["0", "-----"]
		]},
		{ name: "标点", rows: [
			[".", ".-.-.-", "句号"], [",", "--..--", "逗号"], ["?", "..--..", "问号"],
			["'", ".----.", "撇号"], ["!", "-.-.--", "叹号"], ["/", "-..-.", "斜线"],
			["(", "-.--.", "左括号"], [")", "-.--.-", "右括号"], ["&", ".-...", "和"],
			[":", "---...", "冒号"], [";", "-.-.-.", "分号"], ["=", "-...-", "等号"],
			["+", ".-.-.", "加号"], ["-", "-....-", "连字符"], ["_", "..--.-", "下划线"],
			["\"", ".-..-.", "引号"], ["$", "...-..-", "美元"], ["@", ".--.-.", "艾特"]
		]},
		{ name: "信号", rows: [
			["AR", ".-.-.", "报文完"], ["SK", "...-.-", "通信完"], ["AS", ".-...", "请等待"],
			["BT", "-...-", "分隔"], ["KN", "-.--.", "只请对方发"], ["HH", "........", "错误"]
		]}
	];

	SK.regutil("u-morse", {
		title: "摩斯码表",
		go: "显示",
		body: body,
		open: open,
		run: function(){
			throw new Error("没有这个工具");
		},
		hideGo: true,
		hideOut: true,
		live: false
	});

	function body(){
		return '<div class="uquery"><input id="morse-q" type="text" placeholder="字母、标点，或 .- / ·−">'
			+ '<span id="morse-msg" class="msg"></span></div>'
			+ '<p class="hint">国际摩斯码。点一下复制点划（短点 . ，长划 - ）。划约是点的 3 倍。字母之间空 3 点，词之间空 7 点。</p>'
			+ '<div class="morse" id="morse-list"></div>';
	}

	function open(){
		var q = document.getElementById("morse-q");
		var list = document.getElementById("morse-list");
		if (!q || !list) return;
		q.oninput = paint;
		list.onclick = onclick;
		paint();
	}

	function onclick(ev){
		var el = ev.target;
		while (el && el.id !== "morse-list") {
			if (el.getAttribute && el.getAttribute("data-code")) {
				SK.copytext(el.getAttribute("data-code"), "morse-msg");
				return;
			}
			el = el.parentNode;
		}
	}

	function paint(){
		var list = document.getElementById("morse-list");
		var qel = document.getElementById("morse-q");
		if (!list) return;
		var q = norm(qel ? qel.value : "");
		var html = "";
		var g, i, row, n, shown;
		for (g = 0; g < GROUPS.length; g++) {
			shown = "";
			n = 0;
			for (i = 0; i < GROUPS[g].rows.length; i++) {
				row = GROUPS[g].rows[i];
				if (q && !hit(row, q)) continue;
				n++;
				shown += '<button type="button" data-code="' + esc(row[1]) + '" title="'
					+ esc(tip(row)) + '"><b>' + esc(row[0]) + "</b>"
					+ '<span class="mvis">' + vis(row[1]) + "</span>"
					+ '<small>' + esc(row[1]) + (row[2] ? " " + esc(row[2]) : "") + "</small></button>";
			}
			if (!n) continue;
			html += '<h4 class="mgh">' + esc(GROUPS[g].name) + '<span class="n">' + n + "</span></h4>" + shown;
		}
		if (!html) html = '<p class="cnone">没有这条</p>';
		list.innerHTML = html;
	}

	function hit(row, q){
		var blob = (row[0] + " " + row[1] + " " + (row[2] || "")).toLowerCase();
		if (blob.indexOf(q) >= 0) return true;
		return norm(row[1]).indexOf(q) >= 0;
	}

	function tip(row){
		return row[0] + (row[2] ? " " + row[2] : "") + "  " + row[1];
	}

	function vis(code){
		var html = "";
		var i, ch;
		for (i = 0; i < code.length; i++) {
			ch = code.charAt(i);
			if (ch === ".") html += '<i class="d"></i>';
			else if (ch === "-") html += '<i class="s"></i>';
		}
		return html;
	}

	function norm(s){
		return String(s || "").trim().toLowerCase()
			.replace(/[·•‧∙．]/g, ".")
			.replace(/[−—–－]/g, "-")
			.replace(/\s+/g, "");
	}

	function esc(s){
		return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/"/g, "&quot;");
	}
})();
