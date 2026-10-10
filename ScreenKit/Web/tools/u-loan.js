(function(){
	SK.regutil("u-loan", {
		title: "房贷",
		go: "计算",
		body: function(){
			return SK.row2(SK.lab("金额", SK.textin("box-p", "100000")), SK.lab("年利率 %", SK.textin("box-rate", "3.5")))
				+ SK.row2(SK.lab("月数", SK.numin("box-n", "360")), SK.lab("方式", '<select id="box-mode"><option value="annuity">等额本息</option><option value="principal">等额本金</option></select>'));
		},
		run: function(){
			return loanpay(SK.val("box-p"), SK.val("box-rate"), SK.val("box-n"), SK.val("box-mode"));
		}
	});
	function loanpay(p, rate, months, mode){
		var money = Number(String(p).trim());
		var y = Number(String(rate).trim());
		var n = parseInt(months, 10);
		if (!(money > 0)) throw new Error("请输入贷款金额");
		if (!(y >= 0) || y > 100) throw new Error("年利率填百分数，例如 3.5");
		if (!(n >= 1 && n <= 600)) throw new Error("月数要在 1 到 600");
		var r = y / 100 / 12;
		if (mode === "principal") {
			var prin = money / n;
			var first = prin + money * r;
			var last = prin + prin * r;
			var interest = (n + 1) * money * r / 2;
			return "等额本金\n每月本金  " + SK.trimnum(prin)
				+ "\n首月还款  " + SK.trimnum(first)
				+ "\n末月还款  " + SK.trimnum(last)
				+ "\n利息合计  " + SK.trimnum(interest)
				+ "\n还款合计  " + SK.trimnum(money + interest);
		}
		var pay = r === 0 ? money / n : money * r * Math.pow(1 + r, n) / (Math.pow(1 + r, n) - 1);
		var total = pay * n;
		return "等额本息\n每月还款  " + SK.trimnum(pay)
			+ "\n利息合计  " + SK.trimnum(total - money)
			+ "\n还款合计  " + SK.trimnum(total);
	}

	SK.loanpay = loanpay;
})();
