(function(){
	SK.regutil("u-pal", {
		title: "调色板",
		go: "生成",
		body: function(){
			return SK.lab("基准色", SK.textin("box-in", "#f97316", "#RRGGBB"));
		},
		run: function(){
			return palette(SK.val("box-in"));
		}
	});
	function palette(hex){
		var rgb = SK.colorrgb("hex", hex);
		var hsl = SK.rgb2hsl(rgb[0], rgb[1], rgb[2]);
		function one(delta){
			var h = (hsl[0] + delta) % 360;
			if (h < 0) h += 360;
			return hslhex(h, hsl[1], hsl[2]);
		}
		return "基准  " + one(0) + "\n互补  " + one(180) + "\n邻近  " + one(-30) + "\n邻近  " + one(30) + "\n三角  " + one(120) + "\n三角  " + one(240);
	}

	function hslhex(h, s, l){
		var rgb = SK.hsl2rgb(h, s, l);
		return "#" + SK.hex2(rgb[0]) + SK.hex2(rgb[1]) + SK.hex2(rgb[2]);
	}

	SK.palette = palette;
	SK.hslhex = hslhex;
})();
