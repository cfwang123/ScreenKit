(function(){
	var page = null;
	var seq = 0;
	var wait = 0;
	var spec = {
		title: "中文转拼音",
		go: "转换",
		own: true,
		hideOut: true,
		body: function(t){
			page = t;
			return [
				SK.lab("原文", SK.ta("box-in", 8, "")),
				SK.lab("web.pinyin.py", F.CreateDOM(t, { x: "textarea.py-full", name: "pyFull", rows: "3", readonly: true })),
				SK.lab("web.pinyin.init", F.CreateDOM(t, { x: "textarea.py-ini", name: "pyIni", rows: "2", readonly: true })),
				F.CreateDOM(t, { x: "p.py-hint.hint", name: "pyHint", "data-k": "web.pinyin.off", c: SK.t("web.pinyin.off") || "" })
			];
		},
		open: function(t){
			page = t;
			SK.get("/api/pinyin", function(data){
				sethint(!!(data && data.installed));
			}, "", function(){});
		},
		run: run
	};
	SK.regutil("u-py", spec);

	function sethint(on){
		var el = page.doms.pyHint;
		if (!el) return;
		if (on) {
			F.unset(el, "data-k");
			el.textContent = "";
			return;
		}
		F.seta(el, "data-k", "web.pinyin.off");
		el.textContent = SK.t("web.pinyin.off") || "";
	}

	function show(data, on){
		var full = on && data ? (data.pinyin || "") : "";
		var ini = on && data ? (data.py || "") : "";
		if (page.doms.pyFull) page.doms.pyFull.value = full;
		if (page.doms.pyIni) page.doms.pyIni.value = ini;
		if (SK.doms.boxOut) SK.doms.boxOut.value = full && ini ? full + "\n" + ini : (full || ini);
	}

	function run(){
		var n = ++seq;
		if (wait) clearTimeout(wait);
		wait = setTimeout(function(){
			wait = 0;
			SK.post("/api/pinyin", { text: SK.val("box-in") }, function(data){
				if (n !== seq) return;
				var on = !!(data && data.installed);
				sethint(on);
				show(data, on);
				if (on) SK.msg("box-msg", "完成");
			}, "box-msg");
		}, 200);
	}
})();
