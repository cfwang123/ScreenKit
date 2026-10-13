(function(){
	SK.regutil("u-ts", {
		title: "时间戳",
		go: "转换",
		body: function(){
			return SK.lab("日期时间", '<input id="ts-pick" type="datetime-local" step="1">')
				+ SK.lab("时间戳或日期", SK.ta("box-in", 4, "留空为现在。秒、毫秒，或 2026-10-09 12:00:00"));
		},
		run: function(){
			var d = tsdate(SK.val("box-in"));
			settspick(d);
			return tsline(d);
		},
		open: function(){
			var now = new Date();
			F.id("box-in").value = String(Math.floor(now.getTime() / 1000));
			settspick(now);
			F.id("ts-pick").onchange = function(){
				var v = F.id("ts-pick").value;
				if (!v) return;
				F.id("box-in").value = v.replace("T", " ");
				SK.utilrun();
			};
			F.id("box-in").oninput = function(){
				try { tsdate(SK.val("box-in")); }
				catch (e) { return; }
				SK.utilrun();
			};
			SK.utilrun();
		}
	});
	function tr(s){ return SK.tr ? SK.tr(s) : s; }
	function tsline(d){
		return tr("本地") + " " + SK.fmtdt(d) + "\n" + tr("秒") + " " + Math.floor(d.getTime() / 1000) + "\n" + tr("毫秒") + " " + d.getTime();
	}
	function tsconv(raw){
		var d = tsdate(raw);
		return tsline(d);
	}

	function tsdate(raw){
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
		return d;
	}

	function settspick(d){
		var el = F.id("ts-pick");
		if (!el) return;
		el.value = SK.fmtdt(d).replace(" ", "T");
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
