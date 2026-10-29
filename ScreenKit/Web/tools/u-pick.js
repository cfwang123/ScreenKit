(function(){
	var tx = SK.tx;
	var page = null;
	var hue = 220;
	var sat = .75;
	var val = .8;
	var drag = "";

	SK.regutil("u-pick", {
		title: "取色",
		go: "转换",
		noGo: true,
		body: function(t){
			page = t;
			return build();
		},
		open: function(t){
			page = t;
			var start = SK.rgb2hsv(51, 102, 204);
			hue = start[0];
			sat = start[1] / 100;
			val = start[2] / 100;
			page.doms.eye.textContent = tx("web.pick.eye", "屏幕取色");
			paint();
		},
		run: output
	});

	// 整页骨架只在这里建一次：脚本要用的节点都带 name，之后一律 page.doms.xxx
	function build(){
		return F.CreateDOM(page, { x: "div.cpick", c: [
			{ x: "div.cpick-top", c: [
				{ x: "span", c: "颜色" },
				{ x: "i.cpick-swatch", name: "swatch" },
				{ x: "button.cpick-eye", type: "button", name: "eye", onclick: pickeye }
			] },
			{ x: "p.cpick-msg.msg", name: "msg" },
			{ x: "div.cpick-sv", name: "sv", onpointerdown: svdown, onpointermove: svmove,
				onpointerup: stopdrag, c: [{ x: "i", name: "svdot" }] },
			{ x: "div.cpick-hue", name: "hue", onpointerdown: huedown, onpointermove: huemove,
				onpointerup: stopdrag, c: [{ x: "i", name: "huedot" }] },
			{ x: "div.cpick-rgb", c: [
				{ x: "label", c: ["R", { x: "input", name: "r", inputmode: "numeric", oninput: fromrgb }] },
				{ x: "label", c: ["G", { x: "input", name: "g", inputmode: "numeric", oninput: fromrgb }] },
				{ x: "label", c: ["B", { x: "input", name: "b", inputmode: "numeric", oninput: fromrgb }] }
			] },
			{ x: "input", type: "hidden", name: "color", value: "#3366cc" }
		] });
	}

	function output(){
		return SK.colorconv("hex", page.doms.color.value);
	}

	function paint(){
		var doms = page.doms;
		var rgb = hsv2rgb(hue, sat * 100, val * 100);
		var hex = "#" + SK.hex2(rgb[0]) + SK.hex2(rgb[1]) + SK.hex2(rgb[2]);
		doms.color.value = hex;
		doms.swatch.style.background = hex;
		doms.sv.style.backgroundColor = "hsl(" + hue + ",100%,50%)";
		doms.svdot.style.left = (sat * 100) + "%";
		doms.svdot.style.top = ((1 - val) * 100) + "%";
		doms.huedot.style.left = (hue / 360 * 100) + "%";
		doms.r.value = String(rgb[0]);
		doms.g.value = String(rgb[1]);
		doms.b.value = String(rgb[2]);
		setmsg("");
		SK.utilrun();
	}

	function setmsg(text, bad){
		var msg = page.doms.msg;
		if (!msg) return;
		msg.textContent = text || "";
		msg.className = bad ? "msg bad" : "msg";
	}

	function clamp01(n){
		if (n < 0) return 0;
		if (n > 1) return 1;
		return n;
	}

	function fromhex(hex){
		var m = /^#?([0-9a-f]{6})$/i.exec(String(hex).trim());
		if (!m) return;
		var n = parseInt(m[1], 16);
		var hsv = SK.rgb2hsv((n >> 16) & 255, (n >> 8) & 255, n & 255);
		hue = hsv[0];
		sat = hsv[1] / 100;
		val = hsv[2] / 100;
		paint();
	}

	function fromsv(ev){
		var r = F.rect(page.doms.sv);
		sat = clamp01((ev.clientX - r.left) / r.width);
		val = 1 - clamp01((ev.clientY - r.top) / r.height);
		paint();
	}

	function fromhue(ev){
		var r = F.rect(page.doms.hue);
		hue = clamp01((ev.clientX - r.left) / r.width) * 360;
		if (hue >= 360) hue = 0;
		paint();
	}

	function down(which, ev){
		drag = which;
		ev.currentTarget.setPointerCapture(ev.pointerId);
		if (which === "sv") fromsv(ev);
		else fromhue(ev);
	}

	function svdown(ev){
		down("sv", ev);
	}

	function svmove(ev){
		if (drag === "sv") fromsv(ev);
	}

	function huedown(ev){
		down("hue", ev);
	}

	function huemove(ev){
		if (drag === "hue") fromhue(ev);
	}

	function stopdrag(){
		drag = "";
	}

	function fromrgb(){
		var doms = page.doms;
		var r = Number(doms.r.value);
		var g = Number(doms.g.value);
		var b = Number(doms.b.value);
		if (!isFinite(r) || !isFinite(g) || !isFinite(b)) return;
		if (r < 0) r = 0;
		if (g < 0) g = 0;
		if (b < 0) b = 0;
		if (r > 255) r = 255;
		if (g > 255) g = 255;
		if (b > 255) b = 255;
		r = Math.round(r);
		g = Math.round(g);
		b = Math.round(b);
		var hsv = SK.rgb2hsv(r, g, b);
		hue = hsv[0];
		sat = hsv[1] / 100;
		val = hsv[2] / 100;
		paint();
	}

	function pickeye(){
		if (!window.EyeDropper) {
			setmsg(tx("web.pick.noeye", "这个浏览器没有屏幕取色"), true);
			return;
		}
		var drop = new EyeDropper();
		drop.open().then(function(res){
			if (!res || !res.sRGBHex) return;
			fromhex(res.sRGBHex);
		}, function(err){
			if (err && err.name === "AbortError") return;
			setmsg(tx("web.pick.noeye", "这个浏览器没有屏幕取色"), true);
		});
	}

	function hue2deg(s){
		var n = Number(String(s).replace(/deg$/i, ""));
		if (!isFinite(n)) throw new Error("色相无效");
		n = n % 360;
		if (n < 0) n += 360;
		return n;
	}

	function hsv2rgb(h, s, v){
		s /= 100; v /= 100;
		var c = v * s;
		var hp = h / 60;
		var x = c * (1 - Math.abs(hp % 2 - 1));
		var m = v - c;
		return hue2rgb(hp, c, x, m);
	}

	function hue2rgb(hp, c, x, m){
		var r = 0, g = 0, b = 0;
		if (hp < 1) { r = c; g = x; }
		else if (hp < 2) { r = x; g = c; }
		else if (hp < 3) { g = c; b = x; }
		else if (hp < 4) { g = x; b = c; }
		else if (hp < 5) { r = x; b = c; }
		else { r = c; b = x; }
		return [Math.round((r + m) * 255), Math.round((g + m) * 255), Math.round((b + m) * 255)];
	}

	// 00-core 的 colorconv 仍要用这几个换算函数，保留导出
	SK.hue = hue2deg;
	SK.hsv2rgb = hsv2rgb;
	SK.hue2rgb = hue2rgb;
})();
