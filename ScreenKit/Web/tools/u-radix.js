(function(){
	SK.regutil("u-radix", {
		title: "进制转换",
		go: "转换",
		body: function(){
			return SK.lab("整数", SK.textin("box-in", "", "例如 FF")) + SK.row2(SK.lab("从", SK.numin("box-from", "16")), SK.lab("到", SK.numin("box-to", "10")));
		},
		run: function(){
			return radixconv(SK.val("box-in"), SK.val("box-from"), SK.val("box-to"));
		}
	});
	function radixconv(text, from, to){
		var a = parseInt(from, 10);
		var b = parseInt(to, 10);
		if (!(a >= 2 && a <= 36) || !(b >= 2 && b <= 36)) throw new Error("进制要在 2 到 36");
		var t = String(text).trim();
		if (!t) throw new Error("请输入整数");
		if (t.length > 40) throw new Error("太长");
		var neg = false;
		if (t.charAt(0) === "-") {
			neg = true;
			t = t.slice(1);
		}
		if (!t) throw new Error("请输入整数");
		var alpha = "0123456789abcdefghijklmnopqrstuvwxyz";
		var n = 0;
		var i;
		for (i = 0; i < t.length; i++) {
			var d = alpha.indexOf(t.charAt(i).toLowerCase());
			if (d < 0 || d >= a) throw new Error("有数字超出进制");
			n = n * a + d;
			if (n > 9007199254740991) throw new Error("超出安全整数");
		}
		return (neg ? "-" : "") + n.toString(b).toUpperCase();
	}

	SK.radixconv = radixconv;
})();
