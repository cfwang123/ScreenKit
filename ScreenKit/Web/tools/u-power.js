(function(){
	var POWERU = [["W", "瓦"], ["kW", "千瓦"], ["MW", "兆瓦"], ["hp", "公制马力"]];
	var POWERS = { W: 1, kW: 1000, MW: 1000000, hp: 735.49875 };
	SK.regutil("u-power", {
		title: "功率换算",
		go: "换算",
		body: function(){
			return SK.row2(SK.lab("数值", SK.textin("box-in", "1")), SK.lab("单位", '<select id="box-unit">' + SK.opts(POWERU, "kW") + "</select>"));
		},
		run: function(){
			return SK.unitconv(SK.val("box-in"), SK.val("box-unit"), POWERU, POWERS);
		}
	});
})();
