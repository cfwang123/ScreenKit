(function(){
	var PRESSU = [["Pa", "帕"], ["kPa", "千帕"], ["MPa", "兆帕"], ["bar", "巴"], ["atm", "标准大气压"], ["psi", "磅力/平方英寸"]];
	var PRESSS = { Pa: 1, kPa: 1000, MPa: 1000000, bar: 100000, atm: 101325, psi: 6894.757293168 };
	SK.regutil("u-press", {
		title: "压力换算",
		go: "换算",
		body: function(){
			return SK.row2(SK.lab("数值", SK.textin("box-in", "1")), SK.lab("单位", '<select id="box-unit">' + SK.opts(PRESSU, "kPa") + "</select>"));
		},
		run: function(){
			return SK.unitconv(SK.val("box-in"), SK.val("box-unit"), PRESSU, PRESSS);
		}
	});
})();
