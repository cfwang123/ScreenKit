(function(){
	SK.regutil("u-pick", {
		title: "取色",
		go: "转换",
		body: function(){
			return '<div class="cpick">'
				+ '<div class="cpick-top"><span>颜色</span><i id="cpick-swatch"></i></div>'
				+ '<div class="cpick-sv" id="cpick-sv"><i id="cpick-svdot"></i></div>'
				+ '<div class="cpick-hue" id="cpick-hue"><i id="cpick-huedot"></i></div>'
				+ '<div class="cpick-rgb">'
				+ '<label>R<input id="cpick-r" inputmode="numeric"></label>'
				+ '<label>G<input id="cpick-g" inputmode="numeric"></label>'
				+ '<label>B<input id="cpick-b" inputmode="numeric"></label>'
				+ "</div>"
				+ '<input id="box-color" type="hidden" value="#3366cc">'
				+ "</div>";
		},
		run: function(){
			return SK.colorconv("hex", SK.val("box-color"));
		},
		open: function(){
			bindpick();
		}
	});
	function bindpick(){
		var hue = 220;
		var sat = 0.75;
		var val = 0.8;
		var sv = SK.$("cpick-sv");
		var huebar = SK.$("cpick-hue");
		var drag = "";
		function clamp01(n){
			if (n < 0) return 0;
			if (n > 1) return 1;
			return n;
		}
		function paint(){
			var rgb = hsv2rgb(hue, sat * 100, val * 100);
			var hex = "#" + SK.hex2(rgb[0]) + SK.hex2(rgb[1]) + SK.hex2(rgb[2]);
			SK.$("box-color").value = hex;
			SK.$("cpick-swatch").style.background = hex;
			sv.style.backgroundColor = "hsl(" + hue + ",100%,50%)";
			SK.$("cpick-svdot").style.left = (sat * 100) + "%";
			SK.$("cpick-svdot").style.top = ((1 - val) * 100) + "%";
			SK.$("cpick-huedot").style.left = (hue / 360 * 100) + "%";
			SK.$("cpick-r").value = String(rgb[0]);
			SK.$("cpick-g").value = String(rgb[1]);
			SK.$("cpick-b").value = String(rgb[2]);
		}
		function fromsv(ev){
			var r = sv.getBoundingClientRect();
			sat = clamp01((ev.clientX - r.left) / r.width);
			val = 1 - clamp01((ev.clientY - r.top) / r.height);
			paint();
		}
		function fromhue(ev){
			var r = huebar.getBoundingClientRect();
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
		sv.onpointerdown = function(ev){ down("sv", ev); };
		sv.onpointermove = function(ev){ if (drag === "sv") fromsv(ev); };
		sv.onpointerup = function(){ drag = ""; };
		huebar.onpointerdown = function(ev){ down("hue", ev); };
		huebar.onpointermove = function(ev){ if (drag === "hue") fromhue(ev); };
		huebar.onpointerup = function(){ drag = ""; };
		function fromrgb(){
			var r = Number(SK.$("cpick-r").value);
			var g = Number(SK.$("cpick-g").value);
			var b = Number(SK.$("cpick-b").value);
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
		SK.$("cpick-r").oninput = fromrgb;
		SK.$("cpick-g").oninput = fromrgb;
		SK.$("cpick-b").oninput = fromrgb;
		var start = SK.rgb2hsv(51, 102, 204);
		hue = start[0];
		sat = start[1] / 100;
		val = start[2] / 100;
		paint();
	}

	function hue(s){
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

	SK.bindpick = bindpick;
	SK.hue = hue;
	SK.hsv2rgb = hsv2rgb;
	SK.hue2rgb = hue2rgb;
})();
