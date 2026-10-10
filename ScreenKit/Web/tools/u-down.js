(function(){
	SK.regutil("u-down", {
		title: "倒计时",
		go: "开始",
		body: function(){
			return SK.lab("秒数", SK.numin("box-n", "60"));
		},
		run: function(){
			try {
				countdowngo();
				SK.msg("box-msg", "计时中");
			}
			catch (ex) {
				SK.msg("box-msg", ex && ex.message ? ex.message : "失败", true);
			}
		},
		own: true
	});
	function fmthms(sec){
		var s = sec < 0 ? 0 : sec;
		return SK.pad2(Math.floor(s / 3600)) + ":" + SK.pad2(Math.floor(s / 60) % 60) + ":" + SK.pad2(s % 60);
	}

	function countdowngo(){
		SK.stoptick();
		var sec = parseInt(SK.val("box-n"), 10);
		if (!(sec >= 1 && sec <= 86400)) throw new Error("秒数要在 1 到 86400");
		var end = Date.now() + sec * 1000;
		SK.$("box-out").value = fmthms(sec);
		SK.tick = setInterval(function(){
			var left = Math.ceil((end - Date.now()) / 1000);
			if (left <= 0) {
				if (SK.tick) { clearInterval(SK.tick); SK.tick = null; }
				SK.$("box-out").value = "00:00:00\n时间到";
				SK.msg("box-msg", "时间到");
				return;
			}
			SK.$("box-out").value = fmthms(left);
		}, 200);
	}

	SK.fmthms = fmthms;
	SK.countdowngo = countdowngo;
})();
