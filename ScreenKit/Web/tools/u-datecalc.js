(function(){
	var WEEK = "日一二三四五六";
	var UNITS = [
		["s", "秒", 1],
		["min", "分", 60],
		["h", "小时", 3600],
		["d", "天", 86400],
		["m", "月", 0],
		["y", "年", 0]
	];
	SK.regutil("u-datecalc", {
		title: "日期计算",
		go: "计算",
		body: function(){
			return SK.row2(
				SK.lab("模式", '<select id="dc-mode">' + SK.opts([["diff", "日期差"], ["add", "日期加减"]], "diff") + "</select>"),
				SK.lab("开始日期", '<input id="dc-a" type="date">'))
				+ SK.row2(
					SK.lab("单位", '<select id="dc-unit">' + SK.opts(unitrows(), "d") + "</select>"),
					'<span id="dc-bwrap">' + SK.lab("结束日期", '<input id="dc-b" type="date">') + "</span>")
				+ '<div class="row" id="dc-add-row">'
					+ SK.lab("方向", '<select id="dc-dir">' + SK.opts([["after", "之后"], ["before", "之前"]], "after") + "</select>")
					+ SK.lab("数量", SK.textin("dc-n", "7", "例如 7、1.5"))
				+ "</div>";
		},
		open: function(){
			var today = new Date();
			SK.$("dc-a").value = SK.ymd(today);
			SK.$("dc-b").value = SK.ymd(today);
			SK.$("dc-mode").onchange = onpick;
			SK.$("dc-unit").onchange = onpick;
			SK.$("dc-dir").onchange = onpick;
			SK.$("dc-a").oninput = live;
			SK.$("dc-b").oninput = live;
			SK.$("dc-n").oninput = live;
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
		for (i = 0; i < UNITS.length; i++) if (UNITS[i][0] === id) return { id: UNITS[i][0], name: UNITS[i][1], sec: UNITS[i][2] };
		return UNITS[3];
	}

	function onpick(){
		applymode();
		live();
	}

	/** 加减模式藏起结束日期，差值模式藏起方向和数量。 */
	function applymode(){
		var add = SK.val("dc-mode") === "add";
		SK.$("dc-add-row").hidden = !add;
		SK.$("dc-bwrap").hidden = add;
		setlabel("dc-a", add ? "日期" : "开始日期");
	}

	function setlabel(id, text){
		var el = SK.$(id);
		var lab = el && el.parentNode;
		if (lab && lab.firstChild && lab.firstChild.nodeType === 3) lab.firstChild.nodeValue = text;
	}

	function live(){
		SK.msg("box-msg", "");
		try {
			SK.$("box-out").value = calc();
			SK.msg("box-msg", "完成");
		}
		catch (ex) {
			SK.$("box-out").value = "";
			SK.msg("box-msg", ex && ex.message ? ex.message : "失败", true);
		}
	}

	function calc(){
		var a = parselocal(SK.val("dc-a"));
		if (!a) throw new Error("请选择日期");
		var unit = findunit(SK.val("dc-unit"));
		if (SK.val("dc-mode") === "add") return addtext(a, unit);
		var b = parselocal(SK.val("dc-b"));
		if (!b) throw new Error("请选择日期");
		return difftext(a, b, unit);
	}

	function parselocal(text){
		var s = String(text == null ? "" : text).trim();
		if (!s) return null;
		var d = new Date(s + "T00:00:00");
		return isNaN(d.getTime()) ? null : d;
	}

	function daycount(lo, hi){
		return Math.round((hi.getTime() - lo.getTime()) / 86400000);
	}

	function difftext(a, b, unit){
		var back = b.getTime() < a.getTime();
		var lo = back ? b : a;
		var hi = back ? a : b;
		var days = daycount(lo, hi);
		var head = "从 " + SK.ymd(lo) + " 到 " + SK.ymd(hi) + " 相差 " + (back ? "-" : "");
		if (unit.sec) {
			var text = head + SK.trimnum(days * 86400 / unit.sec) + " " + unit.name;
			if (unit.id !== "d" && days) text += "（共 " + days + " 天）";
			return text;
		}
		var p = calcdiff(lo, hi);
		var parts = [];
		if (unit.id === "y") {
			if (p.y) parts.push(p.y + " 年");
			if (p.m) parts.push(p.m + " 个月");
		}
		else if (p.months) parts.push(p.months + " 个月");
		if (p.d) parts.push(p.d + " 天");
		if (!parts.length) parts.push("0 天");
		var line = head + parts.join(" ");
		return days ? line + "（共 " + days + " 天）" : line;
	}

	/** 整月数和余天。月从原日期整体推进，不逐月累积，免得 1 月 31 日被夹到 2 月 28 日后就一直按 28 日算。 */
	function calcdiff(lo, hi){
		var months = (hi.getFullYear() - lo.getFullYear()) * 12 + (hi.getMonth() - lo.getMonth());
		while (months > 0 && addmonths(lo, months).getTime() > hi.getTime()) months--;
		while (addmonths(lo, months + 1).getTime() <= hi.getTime()) months++;
		var t = addmonths(lo, months);
		return { y: Math.floor(months / 12), m: months % 12, months: months, d: daycount(t, hi) };
	}

	function addtext(base, unit){
		var raw = String(SK.val("dc-n")).trim().replace(/,/g, "");
		var n = Number(raw);
		if (raw === "" || !isFinite(n)) throw new Error("请输入数量");
		var back = SK.val("dc-dir") === "before";
		if (back) n = -n;
		var d;
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
		var year = d.getFullYear();
		if (isNaN(d.getTime()) || year < 1 || year > 9999) throw new Error("结果超出范围");
		var showtime = unit.sec < 86400 || n !== Math.floor(n);
		return SK.ymd(base) + (back ? " 之前 " : " 之后 ") + SK.trimnum(Math.abs(n)) + " " + unit.name
			+ "\n" + SK.ymd(d) + (showtime && unit.sec ? " " + clock(d) : "")
			+ "  周" + WEEK.charAt(d.getDay());
	}

	function clock(d){
		return SK.pad2(d.getHours()) + ":" + SK.pad2(d.getMinutes()) + ":" + SK.pad2(d.getSeconds());
	}

	function addmonths(date, n){
		var d = new Date(date.getTime());
		var day = d.getDate();
		d.setDate(1);
		d.setMonth(d.getMonth() + n);
		var last = new Date(d.getFullYear(), d.getMonth() + 1, 0).getDate();
		d.setDate(day < last ? day : last);
		return d;
	}
})();
