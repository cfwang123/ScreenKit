(function(){
	var MASSU = [["mg", "毫克"], ["g", "克"], ["kg", "千克"], ["t", "吨"], ["jin", "市斤"], ["liang", "市两"], ["lb", "磅"], ["oz", "盎司"]];
	var MASSS = { mg: 0.001, g: 1, kg: 1000, t: 1000000, jin: 500, liang: 50, lb: 453.59237, oz: 28.349523125 };
	SK.regutil("u-mass", {
		title: "重量换算",
		go: "换算",
		body: function(){
			return SK.row2(SK.lab("数值", SK.textin("box-in", "1")), SK.lab("单位", '<select id="box-unit">' + SK.opts(MASSU, "kg") + "</select>"));
		},
		run: function(){
			return SK.unitconv(SK.val("box-in"), SK.val("box-unit"), MASSU, MASSS);
		}
	});
})();
