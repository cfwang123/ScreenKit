(function(){
	SK.regutil("u-mac", {
		title: "MAC 地址",
		go: "生成",
		body: function(){
			return SK.lab("个数", SK.numin("box-n", "1"));
		},
		run: function(){
			return macgen(SK.val("box-n"));
		}
	});
	function macgen(count){
		var n = parseInt(count, 10);
		if (!(n >= 1 && n <= 20)) throw new Error("个数要在 1 到 20");
		if (!window.crypto || typeof crypto.getRandomValues !== "function") throw new Error("浏览器不能生成随机数");
		var lines = [];
		var i;
		for (i = 0; i < n; i++) {
			var b = new Uint8Array(6);
			crypto.getRandomValues(b);
			b[0] = (b[0] & 252) | 2;
			var parts = [];
			var j;
			for (j = 0; j < 6; j++) parts.push(SK.hex2(b[j]).toUpperCase());
			lines.push(parts.join(":"));
		}
		return lines.join("\n");
	}

	SK.macgen = macgen;
})();
