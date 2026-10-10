(function(){
	SK.regutil("u-btn", {
		title: "CSS 按钮",
		go: "生成",
		body: function(){
			return SK.lab("文字", SK.textin("box-in", "按钮"))
				+ SK.row2(SK.lab("底色", SK.textin("box-bg", "#f97316")), SK.lab("字色", SK.textin("box-fg", "#ffffff")))
				+ SK.lab("圆角", SK.numin("box-n", "8"));
		},
		run: function(){
			return cssbtn(SK.val("box-in"), SK.val("box-bg"), SK.val("box-fg"), SK.val("box-n"));
		}
	});
	function cssbtn(label, bg, fg, radius){
		var back = SK.colorrgb("hex", bg);
		var ink = SK.colorrgb("hex", fg);
		var r = parseInt(radius, 10);
		if (!(r >= 0 && r <= 999)) throw new Error("圆角要在 0 到 999");
		var text = String(label || "按钮");
		return ".btn {\n  background: #" + SK.hex2(back[0]) + SK.hex2(back[1]) + SK.hex2(back[2])
			+ ";\n  color: #" + SK.hex2(ink[0]) + SK.hex2(ink[1]) + SK.hex2(ink[2])
			+ ";\n  border: 0;\n  border-radius: " + r + "px;\n  padding: 8px 14px;\n}\n/* " + text + " */";
	}

	SK.cssbtn = cssbtn;
})();
