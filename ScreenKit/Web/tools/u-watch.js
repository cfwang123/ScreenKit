(function(){
	SK.regutil("u-watch", {
		title: "计时",
		go: "复位",
		body: function(){
			return '<div class="row">'
				+ moderadio("watch-sw", "web.watch.sw", "秒表", true)
				+ moderadio("watch-down", "web.watch.down", "倒计时", false)
				+ moderadio("watch-until", "web.watch.until", "截止", false)
				+ "</div>"
				+ '<div id="watch-secrow">' + SK.lab("秒数", SK.numin("box-n", "60")) + "</div>"
				+ '<div id="watch-untilrow" hidden>' + SK.lab("日期时间", '<input id="watch-at" type="datetime-local" step="1">') + "</div>"
				+ '<div class="row"><button type="button" id="box-start" data-k="web.go.start">开始</button></div>';
		},
		run: function(){
			return watchreset();
		},
		open: function(){
			F.id("box-start").onclick = watchtoggle;
			F.id("watch-sw").onchange = watchmode;
			F.id("watch-down").onchange = watchmode;
			F.id("watch-until").onchange = watchmode;
			F.id("watch-at").oninput = watchuntil;
			filluntil();
			watchmode();
		}
	});

	function moderadio(id, key, fallback, on){
		var label = (SK.t && SK.t(key)) || fallback;
		return '<label class="check"><input id="' + id + '" name="watch-mode" type="radio" value="' + id + '"'
			+ (on ? " checked" : "") + '> <span data-k="' + key + '">' + label + "</span></label>";
	}

	function curmode(){
		if (F.id("watch-down") && F.id("watch-down").checked) return "down";
		if (F.id("watch-until") && F.id("watch-until").checked) return "until";
		return "sw";
	}

	function watchcap(btn, key, fallback){
		if (!btn) return;
		F.seta(btn, "data-k", key);
		btn.textContent = (SK.t && SK.t(key)) || fallback;
	}

	function say(key, fallback){
		return (SK.t && SK.t(key)) || fallback;
	}

	function fmtwatch(ms){
		if (ms < 0) ms = 0;
		var cs = Math.floor(ms / 10);
		var cent = cs % 100;
		var sec = Math.floor(cs / 100);
		return SK.pad2(Math.floor(sec / 60)) + ":" + SK.pad2(sec % 60) + "." + SK.pad2(cent);
	}

	function fmthms(sec){
		var s = sec < 0 ? 0 : sec;
		return SK.pad2(Math.floor(s / 3600)) + ":" + SK.pad2(Math.floor(s / 60) % 60) + ":" + SK.pad2(s % 60);
	}

	function fmtleft(ms){
		var sec = Math.ceil(ms / 1000);
		if (sec < 0) sec = 0;
		var days = Math.floor(sec / 86400);
		var hms = fmthms(sec % 86400);
		if (days <= 0) return hms;
		return say("web.watch.day", "{0}天 ").replace("{0}", String(days)) + hms;
	}

	function readsec(strict){
		var sec = parseInt(SK.val("box-n"), 10);
		if (!(sec >= 1 && sec <= 86400)) {
			if (strict) throw new Error(say("web.watch.badsec", "秒数要在 1 到 86400"));
			return 60;
		}
		return sec;
	}

	function filluntil(){
		var el = F.id("watch-at");
		if (!el || el.value) return;
		var d = new Date(Date.now() + 3600000);
		el.value = d.getFullYear() + "-" + SK.pad2(d.getMonth() + 1) + "-" + SK.pad2(d.getDate())
			+ "T" + SK.pad2(d.getHours()) + ":" + SK.pad2(d.getMinutes()) + ":" + SK.pad2(d.getSeconds());
	}

	function parseuntil(){
		var s = SK.val("watch-at");
		if (!s) return null;
		var t = new Date(s).getTime();
		if (!isFinite(t)) return null;
		return t;
	}

	function donebox(){
		SK.stoptick();
		watchcap(F.id("box-start"), "web.go.start", "开始");
		F.id("box-out").value = "00:00:00\n" + say("web.watch.done", "时间到");
		SK.msg("box-msg", say("web.watch.done", "时间到"));
	}

	function watchmode(){
		SK.stoptick();
		SK.watchAcc = 0;
		watchcap(F.id("box-start"), "web.go.start", "开始");
		var mode = curmode();
		F.id("watch-secrow").hidden = mode !== "down";
		F.id("watch-untilrow").hidden = mode !== "until";
		SK.msg("box-msg", "");
		if (mode === "down") {
			F.id("box-out").value = fmthms(readsec(false));
			return;
		}
		if (mode === "until") {
			filluntil();
			watchuntil();
			return;
		}
		F.id("box-out").value = "00:00.00";
	}

	function watchtoggle(){
		var mode = curmode();
		var btn = F.id("box-start");
		if (mode === "down") {
			if (SK.watchOn) {
				SK.stoptick();
				watchcap(btn, "web.go.start", "开始");
				return;
			}
			try { startdown(); }
			catch (ex) { SK.msg("box-msg", ex && ex.message ? ex.message : "失败", true); }
			return;
		}
		if (mode === "until") {
			if (SK.watchOn) {
				SK.stoptick();
				watchcap(btn, "web.go.start", "开始");
				return;
			}
			watchuntil();
			return;
		}
		if (SK.watchOn) {
			SK.stoptick();
			watchcap(btn, "web.go.start", "开始");
			F.id("box-out").value = fmtwatch(SK.watchAcc);
			return;
		}
		SK.watch0 = Date.now();
		SK.watchOn = true;
		watchcap(btn, "web.f.stop", "停止");
		SK.tick = setInterval(function(){
			F.id("box-out").value = fmtwatch(SK.watchAcc + Date.now() - SK.watch0);
		}, 50);
	}

	function startdown(){
		SK.stoptick();
		var sec = readsec(true);
		var end = Date.now() + sec * 1000;
		SK.watchOn = true;
		watchcap(F.id("box-start"), "web.f.stop", "停止");
		F.id("box-out").value = fmthms(sec);
		SK.msg("box-msg", say("web.watch.run", "计时中"));
		SK.tick = setInterval(function(){
			var left = Math.ceil((end - Date.now()) / 1000);
			if (left <= 0) {
				donebox();
				return;
			}
			F.id("box-out").value = fmthms(left);
		}, 200);
	}

	function watchuntil(){
		SK.stoptick();
		var end = parseuntil();
		if (end == null) {
			SK.msg("box-msg", say("web.watch.baddt", "请选择截止时间"), true);
			return;
		}
		if (end <= Date.now()) {
			donebox();
			return;
		}
		SK.watchOn = true;
		watchcap(F.id("box-start"), "web.f.stop", "停止");
		SK.msg("box-msg", say("web.watch.run", "计时中"));
		function tick(){
			var left = end - Date.now();
			if (left <= 0) {
				donebox();
				return;
			}
			F.id("box-out").value = fmtleft(left);
		}
		tick();
		SK.tick = setInterval(tick, 200);
	}

	function watchreset(){
		SK.stoptick();
		SK.watchAcc = 0;
		watchcap(F.id("box-start"), "web.go.start", "开始");
		SK.msg("box-msg", "");
		var mode = curmode();
		if (mode === "down") return fmthms(readsec(false));
		if (mode === "until") {
			var end = parseuntil();
			if (end == null) return "";
			var left = end - Date.now();
			if (left <= 0) return "00:00:00\n" + say("web.watch.done", "时间到");
			return fmtleft(left);
		}
		return "00:00.00";
	}

	SK.fmtwatch = fmtwatch;
	SK.fmthms = fmthms;
	SK.watchtoggle = watchtoggle;
	SK.watchreset = watchreset;
})();
