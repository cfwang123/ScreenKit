(function(){
	var TIMEU = [["ms", "毫秒"], ["s", "秒"], ["min", "分"], ["h", "小时"], ["d", "天"], ["week", "周"]];
	var TIMES = { ms: 0.001, s: 1, min: 60, h: 3600, d: 86400, week: 604800 };
	SK.regutil("u-tunit", {
		title: "时间单位",
		go: "换算",
		body: function(){
			return SK.row2(SK.lab("数值", SK.textin("box-in", "1")), SK.lab("单位", '<select id="box-unit">' + SK.opts(TIMEU, "h") + "</select>"));
		},
		run: function(){
			return SK.unitconv(SK.val("box-in"), SK.val("box-unit"), TIMEU, TIMES);
		}
	});
})();
