(function(){
	SK.regutil("u-comp", {
		title: "复利",
		go: "计算",
		body: function(){
			return SK.row2(SK.lab("本金", SK.textin("box-p", "10000")), SK.lab("年利率 %", SK.textin("box-rate", "3")))
				+ SK.row2(SK.lab("年数", SK.textin("box-years", "5")), SK.lab("每年计息次数", SK.numin("box-n", "12")));
		},
		run: function(){
			return compound(SK.val("box-p"), SK.val("box-rate"), SK.val("box-years"), SK.val("box-n"));
		}
	});
	function compound(p, rate, years, times){
		var money = Number(String(p).trim());
		var y = Number(String(rate).trim());
		var t = Number(String(years).trim());
		var n = parseInt(times, 10);
		if (!(money > 0)) throw new Error("请输入本金");
		if (!(y >= 0) || y > 100) throw new Error("年利率填百分数，例如 3");
		if (!(t > 0) || t > 100) throw new Error("年数要大于 0，且不超过 100");
		if (!(n >= 1 && n <= 365)) throw new Error("每年计息次数要在 1 到 365");
		var a = money * Math.pow(1 + y / 100 / n, n * t);
		return "本息  " + SK.trimnum(a) + "\n利息  " + SK.trimnum(a - money);
	}

	SK.compound = compound;
})();
