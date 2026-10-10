(function(){
	var BYTEU = [["B", "B"], ["KB", "KB"], ["MB", "MB"], ["GB", "GB"], ["TB", "TB"]];
	var BYTES = { B: 1, KB: 1024, MB: 1048576, GB: 1073741824, TB: 1099511627776 };
	SK.regutil("u-byte", {
		title: "存储换算",
		go: "换算",
		body: function(){
			return SK.row2(SK.lab("数值", SK.textin("box-in", "1")), SK.lab("单位", '<select id="box-unit">' + SK.opts(BYTEU, "MB") + "</select>"));
		},
		run: function(){
			return SK.unitconv(SK.val("box-in"), SK.val("box-unit"), BYTEU, BYTES);
		}
	});
})();
