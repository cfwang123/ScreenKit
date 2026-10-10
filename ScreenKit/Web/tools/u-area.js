(function(){
	var AREAU = [["mm2", "平方毫米"], ["cm2", "平方厘米"], ["m2", "平方米"], ["ha", "公顷"], ["mu", "亩"], ["km2", "平方千米"], ["ft2", "平方英尺"]];
	var AREAS = { mm2: 0.000001, cm2: 0.0001, m2: 1, ha: 10000, mu: 2000 / 3, km2: 1000000, ft2: 0.09290304 };
	SK.regutil("u-area", {
		title: "面积换算",
		go: "换算",
		body: function(){
			return SK.row2(SK.lab("数值", SK.textin("box-in", "1")), SK.lab("单位", '<select id="box-unit">' + SK.opts(AREAU, "m2") + "</select>"));
		},
		run: function(){
			return SK.unitconv(SK.val("box-in"), SK.val("box-unit"), AREAU, AREAS);
		}
	});
})();
