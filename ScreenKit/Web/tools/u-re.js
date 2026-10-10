(function(){
	SK.regutil("u-re", {
		title: "正则测试",
		go: "测试",
		body: function(){
			return SK.lab("常用", '<select id="box-preset">' + reopts() + "</select>")
				+ '<div class="row grow">' + SK.lab("表达式", SK.textin("box-pat", "", "例如 \\d+")) + SK.lab("标志", SK.textin("box-flags", "g", "g i m")) + "</div>"
				+ SK.lab("方式", '<select id="box-re-mode"><option value="find">查找匹配</option><option value="lines">批量测试</option></select>')
				+ '<p class="hint" id="re-hint">在文本里查找匹配。</p>'
				+ SK.lab("文本", SK.ta("box-in", 8, ""));
		},
		run: function(){
			return retest(SK.val("box-pat"), SK.val("box-flags"), SK.val("box-in"));
		},
		open: function(){
			SK.$("box-preset").onchange = repick;
			SK.$("box-re-mode").onchange = retip;
			retip();
		},
		live: false
	});
	function repatterns(){
		return [
			["邮箱", "[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}", "gi", "name@example.com"],
			["手机号", "1[3-9]\\d{9}", "g", "13800138000"],
			["身份证", "[1-9]\\d{5}(?:19|20)\\d{2}(?:0[1-9]|1[0-2])(?:0[1-9]|[12]\\d|3[01])\\d{3}[\\dXx]", "gi", "110101199001011234"],
			["网址", "https?:\\/\\/[^\\s<>\"]+", "gi", "https://example.com/a"],
			["IPv4", "(?:(?:25[0-5]|2[0-4]\\d|[01]?\\d\\d?)\\.){3}(?:25[0-5]|2[0-4]\\d|[01]?\\d\\d?)", "g", "192.168.0.1"],
			["日期", "\\d{4}-(?:0[1-9]|1[0-2])-(?:0[1-9]|[12]\\d|3[01])", "g", "2026-10-10"],
			["时间", "(?:[01]\\d|2[0-3]):[0-5]\\d(?::[0-5]\\d)?", "g", "09:30:00"],
			["整数", "-?\\d+", "g", "-12"],
			["小数", "-?\\d+\\.\\d+", "g", "3.14"],
			["中文", "[\\u4e00-\\u9fff]+", "g", "汉字"],
			["颜色", "#[0-9A-Fa-f]{3}(?:[0-9A-Fa-f]{3})?", "g", "#3366cc"],
			["邮编", "[1-9]\\d{5}", "g", "100000"],
			["QQ 号", "[1-9]\\d{4,10}", "g", "10001"],
			["车牌", "[京津沪渝冀豫云辽黑湘皖鲁新苏浙赣鄂桂甘晋蒙陕吉闽贵粤青藏川宁琼使领][A-Z][A-HJ-NP-Z0-9]{4,5}[A-HJ-NP-Z0-9挂学警港澳]", "g", "京A12345"],
			["空白行", "^[ \\t]*$", "gm", "a\n\nb"],
		];
	}

	function reopts(){
		var list = repatterns();
		var html = '<option value="">请选择</option>';
		var i;
		for (i = 0; i < list.length; i++)
			html += '<option value="' + i + '">' + SK.esc(list[i][0]) + "</option>";
		return html;
	}

	function repick(){
		var sel = SK.$("box-preset");
		var list = repatterns();
		var i = sel ? parseInt(sel.value, 10) : -1;
		if (!(i >= 0) || !list[i]) return;
		var box = SK.$("box-in");
		var cur = box ? box.value : "";
		var sample = !cur;
		var k;
		if (!sample) {
			for (k = 0; k < list.length; k++) if (cur === list[k][3]) sample = true;
		}
		SK.$("box-pat").value = list[i][1];
		SK.$("box-flags").value = list[i][2];
		if (sample && box) box.value = list[i][3] || "";
	}

	function retip(){
		var lines = SK.val("box-re-mode") === "lines";
		var hint = SK.$("re-hint");
		var box = SK.$("box-in");
		if (hint) hint.textContent = lines ? "一行一条。整行匹配算通过。" : "在文本里查找匹配。";
		if (box) box.placeholder = lines ? "一行一条" : "";
	}

	function showreout(batch){
		var list = SK.$("re-list");
		var out = SK.$("box-out");
		if (!list || !out) return;
		list.hidden = !batch;
		out.hidden = !!batch;
		if (!batch) list.innerHTML = "";
	}

	function recompile(pat, flags){
		pat = String(pat);
		flags = String(flags || "");
		if (!pat) throw new Error("请输入表达式");
		if (pat.length > 200) throw new Error("表达式太长");
		if (!/^[gim]*$/.test(flags)) throw new Error("标志只支持 g、i、m");
		var seen = {};
		var i;
		for (i = 0; i < flags.length; i++) {
			if (seen[flags.charAt(i)]) throw new Error("标志重复了");
			seen[flags.charAt(i)] = 1;
		}
		try { return new RegExp(pat, flags); }
		catch (ex) { throw new Error("表达式无效"); }
	}

	function relines(pat, flags, text){
		flags = String(flags || "");
		var re = recompile(pat, flags);
		text = String(text);
		if (text.length > 20000) throw new Error("文本太长");
		var rows = text.replace(/\r\n/g, "\n").replace(/\r/g, "\n").split("\n");
		if (rows.length && rows[rows.length - 1] === "") rows.pop();
		if (!rows.length) throw new Error("请输入文本");
		if (rows.length > 500) throw new Error("最多 500 行");
		var full = new RegExp("^(?:" + re.source + ")$", flags.replace(/g/g, ""));
		var html = "";
		var plain = [];
		var pass = 0;
		var i;
		for (i = 0; i < rows.length; i++) {
			var ok = full.test(rows[i]);
			if (ok) pass++;
			var mark = ok ? "通过" : "未通过";
			html += '<div>' + SK.esc(rows[i]) + ' <span class="' + (ok ? "ok" : "bad") + '">[' + mark + "]</span></div>";
			plain.push(rows[i] + " [" + mark + "]");
		}
		SK.$("re-list").innerHTML = html;
		showreout(true);
		SK.reNote = "通过 " + pass + "，未通过 " + (rows.length - pass);
		return plain.join("\n");
	}

	function retest(pat, flags, text){
		flags = String(flags || "");
		if (SK.val("box-re-mode") === "lines") return relines(pat, flags, text);
		showreout(false);
		var re = recompile(pat, flags);
		text = String(text);
		if (text.length > 20000) throw new Error("文本太长");
		function one(m){
			var line = "[" + m.index + "] " + m[0];
			var g;
			for (g = 1; g < m.length; g++) line += "\n  " + g + ": " + (m[g] == null ? "" : m[g]);
			return line;
		}
		if (flags.indexOf("g") < 0) {
			var hit = re.exec(text);
			return hit ? one(hit) : "无匹配";
		}
		var lines = [];
		var guard = 0;
		var m2;
		while ((m2 = re.exec(text)) && lines.length < 100) {
			lines.push(one(m2));
			if (m2[0].length === 0) re.lastIndex++;
			guard++;
			if (guard > 10000) break;
		}
		if (!lines.length) return "无匹配";
		if (lines.length >= 100) lines.push("…只显示前 100 处");
		return lines.join("\n");
	}

	SK.repatterns = repatterns;
	SK.reopts = reopts;
	SK.repick = repick;
	SK.retip = retip;
	SK.showreout = showreout;
	SK.recompile = recompile;
	SK.relines = relines;
	SK.retest = retest;
})();
