(function(){
	var ALL = "all";
	var WEEK = ["周日", "周一", "周二", "周三", "周四", "周五", "周六"];
	function tr(s){ return SK.tr ? SK.tr(s) : s; }
	var UNITS = [
		["s", "秒", 1, "秒"],
		["min", "分", 60, "分"],
		["h", "小时", 3600, "小时"],
		["d", "天", 86400, "天"],
		["m", "月", 0, "个月"],
		["y", "年", 0, "年"]
	];
	SK.regutil("u-datecalc", {
		title: "日期计算",
		go: "计算",
		body: function(){
			return SK.row2(
				SK.lab("模式", '<select id="dc-mode">' + SK.opts([["diff", "日期差"], ["add", "日期加减"]], "diff") + "</select>"),
				SK.lab("开始时间", '<input id="dc-a" type="datetime-local" step="1">'))
				+ SK.row2(
					'<span id="dc-bwrap">' + SK.lab("结束时间", '<input id="dc-b" type="datetime-local" step="1">') + "</span>"
					+ '<span id="dc-nwrap">' + SK.lab("数量", SK.textin("dc-n", "7", "可为负，例如 7、-3、1.5")) + "</span>",
					SK.lab("单位", '<select id="dc-unit"></select>'));
		},
		open: function(){
			var now = new Date();
			F.id("dc-a").value = stamp(now);
			F.id("dc-b").value = stamp(now);
			fillunits(false);
			F.id("dc-mode").onchange = onpick;
			F.id("dc-unit").onchange = onpick;
			F.id("dc-a").oninput = live;
			F.id("dc-b").oninput = live;
			F.id("dc-n").oninput = live;
			applymode();
			live();
		},
		run: function(){
			return calc();
		}
	});

	function unitrows(){
		var rows = [];
		var i;
		for (i = 0; i < UNITS.length; i++) rows.push([UNITS[i][0], UNITS[i][1]]);
		return rows;
	}

	function findunit(id){
		var i;
		for (i = 0; i < UNITS.length; i++)
			if (UNITS[i][0] === id) return { id: UNITS[i][0], name: UNITS[i][1], sec: UNITS[i][2], word: UNITS[i][3] };
		return { id: UNITS[3][0], name: UNITS[3][1], sec: UNITS[3][2], word: UNITS[3][3] };
	}

	function onpick(){
		if (this.id === "dc-mode") {
			fillunits(true);
			applymode();
		}
		live();
	}

	/** 日期差才有「全部」；日期加减的单位只能是具体单位。换模式时尽量留下原来选的单位。 */
	function fillunits(keep){
		var add = SK.val("dc-mode") === "add";
		var cur = keep ? SK.val("dc-unit") : "";
		var rows = add ? unitrows() : [[ALL, "全部"]].concat(unitrows());
		var pick = "";
		var i;
		for (i = 0; i < rows.length; i++) if (rows[i][0] === cur) pick = cur;
		if (!pick) pick = add ? "d" : ALL;
		F.id("dc-unit").innerHTML = SK.opts(rows, pick);
	}

	/** 加减藏起结束时间，差值藏起数量。单位始终在这一行的后面。 */
	function applymode(){
		var add = SK.val("dc-mode") === "add";
		F.id("dc-nwrap").hidden = !add;
		F.id("dc-bwrap").hidden = add;
		setlabel("dc-a", add ? "web.f.when" : "web.f.startat", add ? "时间" : "开始时间");
	}

	function setlabel(id, key, text){
		var el = F.id(id);
		var lab = el && el.parentNode;
		if (!lab) return;
		var span = F.q("[data-k]", lab);
		if (span) {
			F.seta(span, "data-k", key);
			span.textContent = (SK.t && SK.t(key)) || text;
			return;
		}
		if (lab.firstChild && lab.firstChild.nodeType === 3) lab.firstChild.nodeValue = text;
	}

	function live(){
		SK.msg("box-msg", "");
		try {
			F.id("box-out").value = calc();
			SK.msg("box-msg", "完成");
		}
		catch (ex) {
			F.id("box-out").value = "";
			SK.msg("box-msg", ex && ex.message ? ex.message : "失败", true);
		}
	}

	function calc(){
		var a = parselocal(SK.val("dc-a"));
		if (!a) throw new Error("请选择日期时间");
		var id = SK.val("dc-unit");
		if (SK.val("dc-mode") === "add") return addtext(a, findunit(id));
		var b = parselocal(SK.val("dc-b"));
		if (!b) throw new Error("请选择日期时间");
		if (id === ALL) return alltext(a, b);
		return difftext(a, b, findunit(id));
	}

	function parselocal(text){
		var s = String(text == null ? "" : text).trim();
		if (!s) return null;
		s = s.replace(" ", "T");
		if (s.indexOf("T") < 0) s += "T00:00:00";
		else if (/T\d{2}:\d{2}$/.test(s)) s += ":00";
		var d = new Date(s);
		return isNaN(d.getTime()) ? null : d;
	}

	function stamp(d){
		return SK.ymd(d) + "T" + SK.pad2(d.getHours()) + ":" + SK.pad2(d.getMinutes()) + ":" + SK.pad2(d.getSeconds());
	}

	function secs(lo, hi){
		return (hi.getTime() - lo.getTime()) / 1000;
	}

	/** 「全部」：每个单位一行。秒到天按真实时间差，月和年走日历。 */
	function alltext(a, b){
		var back = b.getTime() < a.getTime();
		var lo = back ? b : a;
		var hi = back ? a : b;
		var sign = back ? "-" : "";
		var span = secs(lo, hi);
		var p = calcdiff(lo, hi);
		var lines = [tr("从") + " " + SK.fmtdt(a) + " " + tr("到") + " " + SK.fmtdt(b) + " " + tr("相差")];
		var i;
		for (i = 0; i < UNITS.length; i++) {
			var unit = findunit(UNITS[i][0]);
			var text;
			if (unit.sec) text = SK.trimnum(span / unit.sec) + " " + tr(unit.name);
			else text = (unit.id === "m" ? monthparts : yearparts)(p);
			if (text === lines[lines.length - 1]) continue;
			lines.push(sign + text);
		}
		return lines.join("\n");
	}

	function difftext(a, b, unit){
		var back = b.getTime() < a.getTime();
		var lo = back ? b : a;
		var hi = back ? a : b;
		var span = secs(lo, hi);
		var days = span / 86400;
		var head = tr("从") + " " + SK.fmtdt(a) + " " + tr("到") + " " + SK.fmtdt(b) + " " + tr("相差") + " " + (back ? "-" : "");
		if (unit.sec) {
			var text = head + SK.trimnum(span / unit.sec) + " " + tr(unit.name);
			if (unit.id !== "d" && span) text += tr("（共") + " " + SK.trimnum(days) + " " + tr("天）");
			return text;
		}
		var parts = (unit.id === "y" ? yearparts : monthparts)(calcdiff(lo, hi));
		var line = head + parts;
		if (span && (parts.indexOf(tr("个月")) >= 0 || parts.indexOf(tr("年")) >= 0))
			line += tr("（共") + " " + SK.trimnum(days) + " " + tr("天）");
		return line;
	}

	function monthparts(p){
		var parts = [];
		var rest;
		if (p.months) parts.push(p.months + " " + tr("个月"));
		rest = resttext(p.rest);
		if (rest) parts.push(rest);
		return parts.length ? parts.join(" ") : tr("0 天");
	}

	function yearparts(p){
		var parts = [];
		var rest;
		if (p.y) parts.push(p.y + " " + tr("年"));
		if (p.m) parts.push(p.m + " " + tr("个月"));
		rest = resttext(p.rest);
		if (rest) parts.push(rest);
		return parts.length ? parts.join(" ") : tr("0 天");
	}

	function resttext(ms){
		var sec, d, h, m, parts;
		if (!(ms > 0)) return "";
		sec = Math.round(ms / 1000);
		d = Math.floor(sec / 86400);
		sec -= d * 86400;
		h = Math.floor(sec / 3600);
		sec -= h * 3600;
		m = Math.floor(sec / 60);
		sec -= m * 60;
		parts = [];
		if (d) parts.push(d + " " + tr("天"));
		if (h) parts.push(h + " " + tr("小时"));
		if (m) parts.push(m + " " + tr("分"));
		if (sec) parts.push(sec + " " + tr("秒"));
		return parts.join(" ");
	}

	/** 整月数和剩余时间。月从原日期整体推进，不逐月累积，免得 1 月 31 日被夹到 2 月 28 日后就一直按 28 日算。 */
	function calcdiff(lo, hi){
		var months = (hi.getFullYear() - lo.getFullYear()) * 12 + (hi.getMonth() - lo.getMonth());
		var t;
		while (months > 0 && addmonths(lo, months).getTime() > hi.getTime()) months--;
		while (addmonths(lo, months + 1).getTime() <= hi.getTime()) months++;
		t = addmonths(lo, months);
		return { y: Math.floor(months / 12), m: months % 12, months: months, rest: hi.getTime() - t.getTime() };
	}

	/** 一行：日期时间 增加：数量 单位 → 结果。数量为负就是往前。 */
	function addtext(base, unit){
		var raw = String(SK.val("dc-n")).trim().replace(/,/g, "");
		var n = Number(raw);
		var d, year;
		if (raw === "" || !isFinite(n)) throw new Error("请输入数量");
		if (unit.id === "m" || unit.id === "y") {
			var max = unit.id === "m" ? 1200 : 100;
			var what = unit.id === "m" ? "月数" : "年数";
			if (n !== Math.floor(n)) throw new Error(what + "要整数");
			if (Math.abs(n) > max) throw new Error(what + "最多 " + (unit.id === "m" ? "100 年" : "100"));
			d = addmonths(base, unit.id === "m" ? n : n * 12);
		}
		else {
			if (Math.abs(n) > 36600 * 86400 / unit.sec) throw new Error("数量最多 100 年");
			d = new Date(base.getTime() + n * unit.sec * 1000);
		}
		year = d.getFullYear();
		if (isNaN(d.getTime()) || year < 1 || year > 9999) throw new Error("结果超出范围");
		return SK.fmtdt(base) + " " + tr("增加：") + SK.trimnum(n) + " " + tr(unit.word)
			+ " → " + SK.fmtdt(d) + "  " + tr(WEEK[d.getDay()]);
	}

	function addmonths(date, n){
		var d = new Date(date.getTime());
		var day = d.getDate();
		var last;
		d.setDate(1);
		d.setMonth(d.getMonth() + n);
		last = new Date(d.getFullYear(), d.getMonth() + 1, 0).getDate();
		d.setDate(day < last ? day : last);
		return d;
	}
})();
