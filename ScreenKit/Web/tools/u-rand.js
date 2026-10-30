(function(){
	SK.regutil("u-rand", {
		title: "随机数",
		go: "生成",
		body: function(){
			return SK.row2(SK.lab("最小", SK.textin("box-min", "1")), SK.lab("最大", SK.textin("box-max", "100")))
				+ SK.row2(SK.lab("个数", SK.numin("box-n", "5")), SK.lab("类型", '<select id="box-kind"><option value="int">整数</option><option value="float">小数</option></select>'));
		},
		run: function(){
			return randtext(SK.val("box-min"), SK.val("box-max"), SK.val("box-n"), SK.val("box-kind") === "float");
		}
	});
	function randtext(min, max, count, asFloat){
		var a = Number(String(min).trim());
		var b = Number(String(max).trim());
		var c = parseInt(count, 10);
		if (!isFinite(a) || !isFinite(b)) throw new Error("请输入范围");
		if (a > b) throw new Error("最小值不能大于最大值");
		if (!(c >= 1 && c <= 100)) throw new Error("个数要在 1 到 100");
		var lines = [];
		var i;
		if (asFloat) {
			for (i = 0; i < c; i++) lines.push(SK.trimnum(a + (b - a) * (SK.randint(0x1000000) / 0x1000000)));
			return lines.join("\n");
		}
		if (Math.floor(a) !== a || Math.floor(b) !== b) throw new Error("整数范围请填整数");
		if (Math.abs(a) > 9007199254740991 || Math.abs(b) > 9007199254740991) throw new Error("超出安全整数");
		var span = b - a + 1;
		for (i = 0; i < c; i++) lines.push(String(a + SK.randint(span)));
		return lines.join("\n");
	}

	SK.randtext = randtext;
})();
