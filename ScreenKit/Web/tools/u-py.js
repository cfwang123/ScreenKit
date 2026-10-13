(function(){
	var seq = 0;
	var wait = 0;
	var spec = {
		title: "中文转拼音",
		go: "转换",
		own: true,
		body: function(){
			var py = SK.t("web.pinyin.py") || "拼音";
			var ini = SK.t("web.pinyin.init") || "首字母";
			var mode = SK.t("web.pinyin.mode") || "方式";
			return SK.lab("原文", SK.ta("box-in", 8, ""))
				+ '<div class="row"><label><span data-k="web.pinyin.mode">' + SK.esc(mode) + '</span>'
				+ '<select id="py-mode"><option value="py">' + SK.esc(py) + '</option>'
				+ '<option value="init">' + SK.esc(ini) + '</option></select></label></div>'
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

	function run(){
		var n = ++seq;
		if (wait) clearTimeout(wait);
		wait = setTimeout(function(){
			wait = 0;
			var mode = SK.val("py-mode") === "init" ? "init" : "py";
			SK.post("/api/pinyin", { text: SK.val("box-in"), mode: mode }, function(data){
				if (n !== seq) return;
				var on = !!(data && data.installed);
				sethint(on);
				if (F.id("box-out")) F.id("box-out").value = on ? (data.text || "") : "";
				if (on) SK.msg("box-msg", "完成");
			}, "box-msg");
		}, 200);
	}
})();
