(function(){
	SK.regutil("u-ph", {
		title: "占位图",
		go: "生成",
		body: function(){
			return SK.row2(SK.lab("宽", SK.numin("box-w", "320")), SK.lab("高", SK.numin("box-h", "180")))
				+ SK.row2(SK.lab("底色", SK.textin("box-bg", "#eef1f4")), SK.lab("字色", SK.textin("box-fg", "#333333")))
				+ SK.lab("文字", SK.textin("box-in", "", "留空则写宽高"))
				+ '<img id="box-img" alt="" hidden>';
		},
		run: function(){
			return placeholder(SK.val("box-w"), SK.val("box-h"), SK.val("box-in"), SK.val("box-bg"), SK.val("box-fg"));
		}
	});
	function placesize(w, h){
		var width = parseInt(w, 10);
		var height = parseInt(h, 10);
		if (!(width >= 1 && width <= 2000) || !(height >= 1 && height <= 2000)) throw new Error("宽高要在 1 到 2000");
		return [width, height];
	}

	function placeholder(w, h, label, bg, fg){
		var wh = placesize(w, h);
		var back = SK.colorrgb("hex", bg);
		var ink = SK.colorrgb("hex", fg);
		if (typeof document === "undefined" || !document.createElement) throw new Error("浏览器不能画图");
		var canvas = document.createElement("canvas");
		canvas.width = wh[0];
		canvas.height = wh[1];
		var g = canvas.getContext("2d");
		if (!g) throw new Error("浏览器不能画图");
		g.fillStyle = "#" + SK.hex2(back[0]) + SK.hex2(back[1]) + SK.hex2(back[2]);
		g.fillRect(0, 0, wh[0], wh[1]);
		g.fillStyle = "#" + SK.hex2(ink[0]) + SK.hex2(ink[1]) + SK.hex2(ink[2]);
		g.font = Math.max(12, Math.round(Math.min(wh[0], wh[1]) / 8)) + "px sans-serif";
		g.textAlign = "center";
		g.textBaseline = "middle";
		g.fillText(String(label || "").trim() || (wh[0] + "×" + wh[1]), wh[0] / 2, wh[1] / 2);
		var url = canvas.toDataURL("image/png");
		var img = SK.$("box-img");
		if (img) {
			img.src = url;
			img.hidden = false;
		}
		return url;
	}

	SK.placesize = placesize;
	SK.placeholder = placeholder;
})();
