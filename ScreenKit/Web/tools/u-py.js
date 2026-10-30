(function(){
	var seq = 0;
	var wait = 0;
	var spec = {
		title: "中文转拼音",
		go: "转换",
		own: true,
		hideOut: true,
		body: function(){
			return SK.lab("原文", SK.ta("box-in", 8, ""))
				+ SK.lab("web.pinyin.py", '<textarea id="py-full" rows="3" readonly></textarea>')
				+ SK.lab("web.pinyin.init", '<textarea id="py-ini" rows="2" readonly></textarea>')
				+ '<p id="py-hint" class="hint" data-k="web.pinyin.off">' + SK.esc(SK.t("web.pinyin.off") || "") + "</p>";
		},
		open: function(){
			SK.get("/api/pinyin", function(data){
				sethint(!!(data && data.installed));
			}, "", function(){});
		},
		run: run
	};
	SK.regutil("u-py", spec);

	function sethint(on){
		var el = F.id("py-hint");
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
		if (F.id("py-full")) F.id("py-full").value = full;
		if (F.id("py-ini")) F.id("py-ini").value = ini;
		if (F.id("box-out")) F.id("box-out").value = full && ini ? full + "\n" + ini : (full || ini);
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
