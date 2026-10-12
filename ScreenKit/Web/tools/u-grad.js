(function(){
	SK.regutil("u-grad", {
		title: "CSS 渐变",
		go: "生成",
		noGo: true,
		body: function(){
			return SK.lab("类型", '<select id="box-type"><option value="linear">线性</option><option value="radial">径向</option><option value="conic">锥形</option></select>')
				+ SK.row2(SK.lab("起始色", SK.textin("box-a", "#f97316")), SK.lab("结束色", SK.textin("box-b", "#111827")))
				+ SK.lab("角度", SK.numin("box-n", "90"))
				+ '<div id="grad-view" class="grad-view"></div>';
		},
		open: function(){
			var body = SK.$("box-body");
			body.oninput = gradlive;
			body.onchange = gradlive;
			gradlive();
		},
		run: function(){
			return gradcss(SK.val("box-type"), SK.val("box-a"), SK.val("box-b"), SK.val("box-n"));
		}
	});
	function gradlive(){
		var n = SK.$("box-n");
		if (n) n.disabled = SK.val("box-type") === "radial";
		SK.utilrun();
	}
	function gradcss(type, a, b, angle){
		type = String(type || "linear");
		if (type !== "linear" && type !== "radial" && type !== "conic") throw new Error("请选择渐变类型");
		var c1 = SK.colorrgb("hex", a);
		var c2 = SK.colorrgb("hex", b);
		var n = Number(angle);
		if (type !== "radial" && (!isFinite(n) || n < 0 || n > 360)) throw new Error("角度要在 0 到 360");
		var h1 = "#" + SK.hex2(c1[0]) + SK.hex2(c1[1]) + SK.hex2(c1[2]);
		var h2 = "#" + SK.hex2(c2[0]) + SK.hex2(c2[1]) + SK.hex2(c2[2]);
		var deg = SK.trimnum(isFinite(n) ? n : 0);
		var fn;
		if (type === "radial") fn = "radial-gradient(circle, " + h1 + ", " + h2 + ")";
		else if (type === "conic") fn = "conic-gradient(from " + deg + "deg, " + h1 + ", " + h2 + ")";
		else fn = "linear-gradient(" + deg + "deg, " + h1 + ", " + h2 + ")";
		var view = SK.$("grad-view");
		if (view) view.style.background = fn;
		return "background: " + fn + ";";
	}

	SK.gradcss = gradcss;
	SK.gradlive = gradlive;
})();
