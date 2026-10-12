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
	// 试听用：约 784Hz 的报务音，一个点 90 毫秒（划 3 点），整段最长播 20 秒
	var MIDI = 79;
	var UNIT = 90;
	var MAXMS = 20000;
	var CODES = buildcodes();
	var timers = [];

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

	function buildcodes(){
		var map = {};
		var g, i, row;
		for (g = 0; g < GROUPS.length; g++) {
			for (i = 0; i < GROUPS[g].rows.length; i++) {
				row = GROUPS[g].rows[i];
				map[row[0]] = row[1];
			}
		}
		return map;
	}

	function body(){
		return '<div class="uquery"><input id="morse-q" type="text" placeholder="搜索：字母、标点，或 .- / ·−">'
			+ '<span id="morse-msg" class="msg"></span></div>'
			+ '<p class="hint">国际摩斯码。点下面的码复制并用 8bit 合成器试听（约 784Hz），也可以输入一段文字整段播放。'
			+ '短点 . ，长划 - ，划约是点的 3 倍。字母之间空 3 点，词之间空 7 点。</p>'
			+ '<div class="mtry"><input id="morse-t" type="text" maxlength="64" value="HELLO" placeholder="输入一段文字，如 SOS HELLO 2026">'
			+ '<button type="button" id="morse-play" class="primary">播放</button>'
			+ '<button type="button" id="morse-stop">停止</button>'
			+ '<span id="morse-tmsg" class="msg"></span></div>'
			+ '<div class="mpv" id="morse-pv"></div>'
			+ '<div class="morse" id="morse-list"></div>';
	}

	function open(){
		var q = document.getElementById("morse-q");
		var list = document.getElementById("morse-list");
		var text = document.getElementById("morse-t");
		var play = document.getElementById("morse-play");
		var stopb = document.getElementById("morse-stop");
		if (!q || !list) return;
		q.oninput = paint;
		list.onclick = onclick;
		if (text) text.oninput = paintpv;
		if (text) {
			text.onkeydown = function(ev){
				if (ev.key === "Enter") playtext();
			};
		}
		if (play) play.onclick = playtext;
		if (stopb) stopb.onclick = stop;
		paint();
		paintpv();
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

	// 点单条码：符号之间空 1 单位
	function play(btn){
		var code = btn.getAttribute("data-code");
		var items = [];
		var i;
		var r;
		stop();
		for (i = 0; i < code.length; i++)
			items.push([code.charAt(i), i === code.length - 1 ? 0 : 1]);
		r = queue(items);
		if (!r) return;
		btn.classList.add("on");
		later(r.end * 1000 + 80, stop);
	}

	// 整段播放：字符内空 1 单位，字符之间空 3 单位，词之间空 7 单位
	function playtext(){
		var el = document.getElementById("morse-t");
		var msg = document.getElementById("morse-tmsg");
		var got = tokens(clean(el ? el.value : ""));
		var plan;
		var r;
		var info;
		var i;
		if (!got.list.length) {
			say(msg, "先输入要播的文字", true);
			return;
		}
		plan = sequence(got.list);
		stop();
		r = queue(plan.items);
		if (!r) {
			say(msg, "这个浏览器不能发声", true);
			return;
		}
		info = plan.cut ? "只播前 " + plan.order + " 个字符（最长 " + Math.round(MAXMS / 1000) + " 秒）"
			: "共 " + plan.order + " 个字符，约 " + r.end.toFixed(1) + " 秒";
		if (got.skipped) info += "；" + got.skipped + " 个字符没有码，已跳过";
		say(msg, info);
		for (i = 0; i < plan.marks.length; i++)
			mark(plan.marks[i][1], (r.starts[plan.marks[i][0]] + 0.03) * 1000);
		later(r.end * 1000 + 150, stop);
	}

	function stop(){
		var on;
		var i;
		for (i = 0; i < timers.length; i++) clearTimeout(timers[i]);
		timers = [];
		if (SK.bitblipstop) SK.bitblipstop();
		on = document.querySelectorAll("#morse-list button.on, #morse-pv span.on");
		for (i = 0; i < on.length; i++) on[i].classList.remove("on");
	}

	function later(ms, fn){
		timers.push(setTimeout(fn, ms));
	}

	// 播到第 order 个字符时，把预览里那一段点亮
	function mark(order, ms){
		later(ms, function(){
			var on = document.querySelectorAll("#morse-pv span.on");
			var el = document.querySelector('#morse-pv span[data-i="' + order + '"]');
			var i;
			for (i = 0; i < on.length; i++) on[i].classList.remove("on");
			if (el) el.classList.add("on");
		});
	}

	// 依次发声；items 每项 [符号, 后面停几个单位]，符号为空表示只停顿。
	// bitblip 的 at 是音频时钟上的绝对时间，0 表示现在。
	// starts 和 end 给页面高亮的 setTimeout 用，必须是从现在起的秒数。
	function queue(items){
		var unit = UNIT / 1000;
		var starts = [];
		var rel = 0;
		var origin = 0;
		var i, it, end, dur, at;
		if (!SK.bitblip) return null;
		for (i = 0; i < items.length; i++) {
			it = items[i];
			starts.push(rel);
			if (it[0]) {
				dur = it[0] === "-" ? unit * 3 : unit;
				at = origin ? origin + rel : 0;
				end = SK.bitblip(MIDI, dur, at);
				if (end < 0) return null;
				if (!origin) origin = end - dur;
				rel += dur;
			}
			rel += unit * (it[1] || 0);
		}
		return { end: rel, starts: starts };
	}

	// 排音：超时就截断
	function sequence(list){
		var items = [];
		var marks = [];
		var units = 0;
		var order = 0;
		var cut = false;
		var i, j, tk;
		for (i = 0; i < list.length; i++) {
			tk = list[i];
			if (tk.word) {
				if ((units + 7) * UNIT > MAXMS) {
					cut = true;
					break;
				}
				items.push(["", 7]);
				units += 7;
				continue;
			}
			if ((units + tk.code.length + 2) * UNIT > MAXMS) {
				cut = true;
				break;
			}
			marks.push([items.length, order]);
			for (j = 0; j < tk.code.length; j++)
				items.push([tk.code.charAt(j), j === tk.code.length - 1 ? 3 : 1]);
			units += tk.code.length + 2;
			order++;
		}
		return { items: items, marks: marks, order: order, cut: cut };
	}

	// 文字 -> 令牌；没有码的字符丢掉并计数，空格当词间隔
	function tokens(text){
		var out = [];
		var skipped = 0;
		var i, ch;
		for (i = 0; i < text.length; i++) {
			ch = text.charAt(i);
			if (ch === " ") {
				if (out.length && !out[out.length - 1].word) out.push({ word: true });
				continue;
			}
			if (!CODES[ch]) {
				skipped++;
				continue;
			}
			out.push({ ch: ch, code: CODES[ch] });
		}
		while (out.length && out[out.length - 1].word) out.pop();
		return { list: out, skipped: skipped };
	}

	function clean(s){
		return String(s || "").toUpperCase().replace(/[\s\u3000]+/g, " ").replace(/^ | $/g, "");
	}

	// 输入框下面的点划预览，播放时逐字高亮
	function paintpv(){
		var el = document.getElementById("morse-t");
		var out = document.getElementById("morse-pv");
		var got;
		var html = "";
		var order = 0;
		var i, tk;
		if (!out) return;
		if (el) say(document.getElementById("morse-tmsg"), "");
		got = tokens(clean(el ? el.value : ""));
		for (i = 0; i < got.list.length; i++) {
			tk = got.list[i];
			if (tk.word) {
				html += '<i>/</i>';
				continue;
			}
			html += '<span data-i="' + order + '">' + esc(tk.code) + "</span>";
			order++;
		}
		out.innerHTML = html || '<small>输入文字后这里显示点划</small>';
	}

	function say(el, text, err){
		if (!el) return;
		el.textContent = text;
		el.className = err ? "msg err" : "msg";
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
