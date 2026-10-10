(function(){
	SK.regutil("u-uuid", {
		title: "UUID",
		go: "生成",
		body: function(){
			return SK.lab("个数", SK.numin("box-n", "1"));
		},
		run: function(){
			return uuidtext(SK.val("box-n"));
		}
	});
	function uuidtext(count){
		var c = parseInt(count, 10);
		if (!(c >= 1 && c <= 20)) throw new Error("个数要在 1 到 20");
		var lines = [];
		var n;
		for (n = 0; n < c; n++) lines.push(uuid1());
		return lines.join("\n");
	}

	function uuid1(){
		var b = new Uint8Array(16);
		if (!window.crypto || typeof crypto.getRandomValues !== "function") throw new Error("浏览器不能生成随机数");
		crypto.getRandomValues(b);
		b[6] = (b[6] & 15) | 64;
		b[8] = (b[8] & 63) | 128;
		var h = SK.hexbytes(b);
		return h.slice(0, 8) + "-" + h.slice(8, 12) + "-" + h.slice(12, 16) + "-" + h.slice(16, 20) + "-" + h.slice(20);
	}

	SK.uuidtext = uuidtext;
	SK.uuid1 = uuid1;
})();
