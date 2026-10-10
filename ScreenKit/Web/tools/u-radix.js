(function(){
	var BASES = [2, 8, 10, 16];

	SK.regutil("u-radix", {
		title: "进制转换",
		go: "转换",
		body: function(){
			var list = [["all", "全部"]];
			var i;
			for (i = 0; i < BASES.length; i++) list.push([String(BASES[i]), BASES[i] + "进制"]);
			return SK.lab("整数", SK.textin("box-in", "", "例如 FF"))
				+ SK.row2(SK.lab("从", SK.numin("box-from", "16")), SK.lab("转换到", '<select id="box-to">' + SK.opts(list, "all") + "</select>"));
		},
		open: function(){
			SK.$("box-in").oninput = rerun;
			SK.$("box-from").oninput = rerun;
			SK.$("box-to").onchange = rerun;
		},
		run: function(){
			return radixconv(SK.val("box-in"), SK.val("box-from"), SK.val("box-to"));
		}
	});

	function rerun(){
		if (!String(SK.val("box-in")).trim()) {
			SK.$("box-out").value = "";
			SK.msg("box-msg", "");
			return;
		}
		SK.utilrun();
	}

	function radixconv(text, from, to){
		var a = parseInt(from, 10);
		if (!(a >= 2 && a <= 36)) throw new Error("进制要在 2 到 36");
		var parsed = readint(text, a);
		var bases = BASES;
		var i;
		if (to !== "all") {
			var b = parseInt(to, 10);
			if (!(b >= 2 && b <= 36)) throw new Error("请选择进制");
			bases = [b];
		}
		var lines = [a + "进制 " + parsed.show];
		for (i = 0; i < bases.length; i++)
			lines.push(bases[i] + "进制 " + writeint(parsed.n, parsed.neg, bases[i]));
		return lines.join("\n");
	}

	function readint(text, base){
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
			if (d < 0 || d >= base) throw new Error("有数字超出进制");
			n = n * base + d;
			if (n > 9007199254740991) throw new Error("超出安全整数");
		}
		return { n: n, neg: neg, show: (neg ? "-" : "") + t.toUpperCase() };
	}

	function writeint(n, neg, base){
		return (neg ? "-" : "") + n.toString(base).toUpperCase();
	}

	SK.radixconv = radixconv;
})();
