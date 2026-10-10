(function(){
	SK.regutil("u-ts", {
		title: "时间戳",
		go: "转换",
		body: function(){
			return SK.lab("时间戳或日期", SK.ta("box-in", 4, "留空为现在。秒、毫秒，或 2026-10-09 12:00:00"));
		},
		run: function(){
			return tsconv(SK.val("box-in"));
		},
		open: function(){
			SK.$("box-in").value = String(Math.floor(Date.now() / 1000));
		}
	});
	function tsconv(raw){
		raw = String(raw || "").trim();
		var d;
		if (!raw) d = new Date();
		else if (/^-?\d+(\.\d+)?$/.test(raw)) {
			var n = Number(raw);
			if (!isFinite(n)) throw new Error("无法解析");
			var ms = Math.abs(n) > 1e12 ? n : n * 1000;
			d = new Date(ms);
		}
		else d = parsedate(raw);
		if (isNaN(d.getTime())) throw new Error("无法解析");
		return "本地 " + SK.fmtdt(d) + "\n秒 " + Math.floor(d.getTime() / 1000) + "\n毫秒 " + d.getTime();
	}

	function parsedate(s){
		var m = /^(\d{4})-(\d{2})-(\d{2})(?:[ T](\d{2}):(\d{2})(?::(\d{2}))?)?$/.exec(String(s).trim());
		if (!m) throw new Error("日期格式用 YYYY-MM-DD HH:mm:ss");
		var d = new Date(+m[1], +m[2] - 1, +m[3], +(m[4] || 0), +(m[5] || 0), +(m[6] || 0));
		if (d.getFullYear() !== +m[1] || d.getMonth() !== +m[2] - 1 || d.getDate() !== +m[3]) throw new Error("日期无效");
		return d;
	}

	SK.tsconv = tsconv;
	SK.parsedate = parsedate;
})();
