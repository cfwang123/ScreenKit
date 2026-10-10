(function(){
	var VOLU = [["mL", "毫升"], ["L", "升"], ["m3", "立方米"], ["gal", "美制加仑"]];
	var VOLS = { mL: 0.001, L: 1, m3: 1000, gal: 3.785411784 };
	SK.regutil("u-vol", {
		title: "体积换算",
		go: "换算",
		body: function(){
			return SK.row2(SK.lab("数值", SK.textin("box-in", "1")), SK.lab("单位", '<select id="box-unit">' + SK.opts(VOLU, "L") + "</select>"));
		},
		run: function(){
			return SK.unitconv(SK.val("box-in"), SK.val("box-unit"), VOLU, VOLS);
		}
	});
})();
