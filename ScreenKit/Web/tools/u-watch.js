(function(){
	SK.regutil("u-watch", {
		title: "秒表",
		go: "复位",
		body: function(){
			return '<div class="row"><button type="button" id="box-start">开始</button></div>';
		},
		run: function(){
			return watchreset();
		},
		open: function(){
			SK.$("box-start").onclick = watchtoggle;
			SK.$("box-out").value = watchreset();
		}
	});
	function fmtwatch(ms){
		if (ms < 0) ms = 0;
		var cs = Math.floor(ms / 10);
		var cent = cs % 100;
		var sec = Math.floor(cs / 100);
		return SK.pad2(Math.floor(sec / 60)) + ":" + SK.pad2(sec % 60) + "." + SK.pad2(cent);
	}

	function watchtoggle(){
		var btn = SK.$("box-start");
		if (SK.watchOn) {
			SK.stoptick();
			if (btn) btn.textContent = "开始";
			SK.$("box-out").value = fmtwatch(SK.watchAcc);
			return;
		}
		SK.watch0 = Date.now();
		SK.watchOn = true;
		if (btn) btn.textContent = "停止";
		SK.tick = setInterval(function(){
			SK.$("box-out").value = fmtwatch(SK.watchAcc + Date.now() - SK.watch0);
		}, 50);
	}

	function watchreset(){
		SK.stoptick();
		SK.watchAcc = 0;
		var btn = SK.$("box-start");
		if (btn) btn.textContent = "开始";
		return "00:00.00";
	}

	SK.fmtwatch = fmtwatch;
	SK.watchtoggle = watchtoggle;
	SK.watchreset = watchreset;
})();
