(function(){
	SK.regutil("u-rmb", {
		title: "人民币大写",
		go: "转换",
		body: function(){
			return SK.lab("金额", SK.textin("box-in", "", "例如 10010.50"));
		},
		run: function(){
			return rmbupper(SK.val("box-in"));
		}
	});
	function rmbupper(text){
		var s = String(text).trim();
		if (!/^\d+(\.\d+)?$/.test(s)) throw new Error("请输入非负金额");
		var parts = s.split(".");
		if (parts[0].length > 12) throw new Error("金额太大");
		var yuan = parseInt(parts[0], 10);
		var frac = (parts[1] || "") + "000";
		var fen = parseInt(frac.slice(0, 2), 10);
		if (parseInt(frac.charAt(2), 10) >= 5) fen++;
		var all = yuan * 100 + fen;
		if (!isFinite(all) || all < 0 || Math.floor(all / 100) > 999999999999) throw new Error("金额太大");
		if (all === 0) return "零元整";
		var digit = "零壹贰叁肆伍陆柒捌玖";
		var small = ["", "拾", "佰", "仟"];
		var big = ["", "万", "亿"];
		var y = Math.floor(all / 100);
		var jiao = Math.floor(all / 10) % 10;
		var f = all % 10;
		var chunks = [];
		while (y > 0) {
			chunks.push(y % 10000);
			y = Math.floor(y / 10000);
		}
		var body = "";
		var gap = false;
		var gi;
		for (gi = chunks.length - 1; gi >= 0; gi--) {
			var sec = chunks[gi];
			if (!sec) {
				gap = true;
				continue;
			}
			var piece = "";
			var zero = false;
			var left = sec;
			var i;
			for (i = 0; i < 4; i++) {
				var d = left % 10;
				if (d === 0) {
					if (piece) zero = true;
				}
				else {
					piece = digit.charAt(d) + small[i] + (zero ? "零" : "") + piece;
					zero = false;
				}
				left = Math.floor(left / 10);
			}
			if (body && (gap || sec < 1000)) body += "零";
			body += piece + big[gi];
			gap = false;
		}
		var tail;
		if (jiao === 0 && f === 0) tail = "整";
		else {
			tail = "";
			if (jiao) tail += digit.charAt(jiao) + "角";
			else if (f && yuan) tail += "零";
			if (f) tail += digit.charAt(f) + "分";
		}
		if (!body) return tail;
		return body + "元" + tail;
	}

	SK.rmbupper = rmbupper;
})();
