(function(){
	var LENU = [["mm", "毫米"], ["cm", "厘米"], ["m", "米"], ["km", "千米"], ["in", "英寸"], ["ft", "英尺"], ["yd", "码"], ["mi", "英里"]];
	var LENS = { mm: 1, cm: 10, m: 1000, km: 1000000, "in": 25.4, ft: 304.8, yd: 914.4, mi: 1609344 };
	SK.regutil("u-len", {
		title: "长度换算",
		go: "换算",
		body: function(){
			return SK.row2(SK.lab("数值", SK.textin("box-in", "1")), SK.lab("单位", '<select id="box-unit">' + SK.opts(LENU, "m") + "</select>"));
		},
		run: function(){
			return SK.unitconv(SK.val("box-in"), SK.val("box-unit"), LENU, LENS);
		}
	});
})();
