(function(){
	SK.regutil("u-grad", {
		title: "CSS 渐变",
		go: "生成",
		body: function(){
			return SK.row2(SK.lab("起始色", SK.textin("box-a", "#f97316")), SK.lab("结束色", SK.textin("box-b", "#111827")))
				+ SK.lab("角度", SK.numin("box-n", "90"));
		},
		run: function(){
			return gradcss(SK.val("box-a"), SK.val("box-b"), SK.val("box-n"));
		}
	});
	function gradcss(a, b, angle){
		var c1 = SK.colorrgb("hex", a);
		var c2 = SK.colorrgb("hex", b);
		var n = Number(angle);
		if (!isFinite(n) || n < 0 || n > 360) throw new Error("角度要在 0 到 360");
		var h1 = "#" + SK.hex2(c1[0]) + SK.hex2(c1[1]) + SK.hex2(c1[2]);
		var h2 = "#" + SK.hex2(c2[0]) + SK.hex2(c2[1]) + SK.hex2(c2[2]);
		return "background: linear-gradient(" + SK.trimnum(n) + "deg, " + h1 + ", " + h2 + ");";
	}

	SK.gradcss = gradcss;
})();
