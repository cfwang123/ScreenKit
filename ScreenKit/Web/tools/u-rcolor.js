(function(){
	SK.regutil("u-rcolor", {
		title: "随机颜色",
		go: "生成",
		body: function(){
			return SK.lab("个数", SK.numin("box-n", "5"));
		},
		run: function(){
			return rcolor(SK.val("box-n"));
		}
	});
	function rcolor(count){
		var n = parseInt(count, 10);
		if (!(n >= 1 && n <= 20)) throw new Error("个数要在 1 到 20");
		if (!window.crypto || typeof crypto.getRandomValues !== "function") throw new Error("浏览器不能生成随机数");
		var lines = [];
		var html = "";
		var i;
		for (i = 0; i < n; i++) {
			var b = new Uint8Array(3);
			crypto.getRandomValues(b);
			var hex = "#" + SK.hex2(b[0]) + SK.hex2(b[1]) + SK.hex2(b[2]);
			var rgb = "rgb(" + b[0] + ", " + b[1] + ", " + b[2] + ")";
			lines.push(hex + "  " + rgb);
			html += "<li><i style=\"background:" + hex + "\"></i><b>" + hex + "</b><span>" + rgb + "</span></li>";
		}
		var list = SK.$("rcolor-list");
		if (list) {
			list.innerHTML = html;
			if (SK.showrcolor) SK.showrcolor(true);
		}
		return lines.join("\n");
	}

	function showrcolor(on){
		var list = SK.$("rcolor-list");
		var out = SK.$("box-out");
		if (!list || !out) return;
		list.hidden = !on;
		out.hidden = !!on;
		if (!on) list.innerHTML = "";
	}

	SK.rcolor = rcolor;
	SK.showrcolor = showrcolor;
})();
