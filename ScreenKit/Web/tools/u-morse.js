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

	// 试听用：约 784Hz 的报务音，一个点 90 毫秒（划 3 点）
	var MIDI = 79;
	var UNIT = 90;
	var timer = 0;

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
			+ '<p class="hint">国际摩斯码。点一下复制点划，并用 8bit 合成器的当前音色试听（约 784Hz）。'
			+ '短点 . ，长划 - ，划约是点的 3 倍。字母之间空 3 点，词之间空 7 点。</p>'
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
				play(el);
				return;
			}
			el = el.parentNode;
		}
	}

	// 按国际时值把这条码排出去：点 1 单位、划 3 单位，符号之间空 1 单位
	function play(btn){
		var code = btn.getAttribute("data-code");
		var unit = UNIT / 1000;
		var t = 0;
		var end = -1;
		var i;
		var ch;
		var dur;
		if (!SK.bitblip) return;
		stop();
		for (i = 0; i < code.length; i++) {
			ch = code.charAt(i);
			if (ch === ".") dur = unit;
			else if (ch === "-") dur = unit * 3;
			else {
				t += unit * 3;
				continue;
			}
			end = SK.bitblip(MIDI, dur, t);
			if (end < 0) return;
			t = end + unit;
		}
		if (end < 0) return;
		btn.classList.add("on");
		timer = setTimeout(function(){
			timer = 0;
			stop();
		}, (t - unit) * 1000 + 80);
	}

	function stop(){
		var on;
		var i;
		if (timer) clearTimeout(timer);
		timer = 0;
		if (SK.bitblipstop) SK.bitblipstop();
		on = document.querySelectorAll("#morse-list button.on");
		for (i = 0; i < on.length; i++) on[i].classList.remove("on");
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
