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
		bindrcolor();
		var lines = [];
		var html = "";
		var i;
		for (i = 0; i < n; i++) {
			var b = new Uint8Array(3);
			crypto.getRandomValues(b);
			var parts = colorparts(b[0], b[1], b[2]);
			lines.push(parts.join("  "));
			html += "<li><i style=\"background:" + parts[0] + "\"></i>";
			var k;
			for (k = 0; k < parts.length; k++)
				html += "<button type=\"button\" data-v=\"" + escv(parts[k]) + "\">" + escv(parts[k]) + "</button>";
			html += "</li>";
		}
		var list = SK.$("rcolor-list");
		if (list) {
			list.innerHTML = html;
			if (SK.showrcolor) SK.showrcolor(true);
		}
		return lines.join("\n");
	}

	function colorparts(r, g, b){
		var hex = "#" + SK.hex2(r) + SK.hex2(g) + SK.hex2(b);
		var hsl = SK.rgb2hsl(r, g, b);
		var hsv = SK.rgb2hsv(r, g, b);
		var cmyk = SK.rgb2cmyk(r, g, b);
		var yuv = SK.rgb2yuv(r, g, b);
		return [
			hex,
			"rgb(" + r + ", " + g + ", " + b + ")",
			"hsl(" + hsl[0] + ", " + hsl[1] + "%, " + hsl[2] + "%)",
			"hsv(" + hsv[0] + ", " + hsv[1] + "%, " + hsv[2] + "%)",
			"cmyk(" + cmyk[0] + "%, " + cmyk[1] + "%, " + cmyk[2] + "%, " + cmyk[3] + "%)",
			"yuv(" + yuv[0] + ", " + yuv[1] + ", " + yuv[2] + ")",
		];
	}

	function escv(s){
		return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/"/g, "&quot;");
	}

	function bindrcolor(){
		var list = SK.$("rcolor-list");
		if (!list || list.getAttribute("data-bind")) return;
		list.setAttribute("data-bind", "1");
		list.onclick = function(ev){
			var btn = ev.target;
			while (btn && btn !== list && btn.tagName !== "BUTTON") btn = btn.parentNode;
			if (!btn || btn === list) return;
			SK.copytext(btn.getAttribute("data-v") || btn.textContent, "box-msg");
		};
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
