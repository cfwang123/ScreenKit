(function(){
	function qrcaptext(text){
		return String(text || "").replace(/\r|\n/g, " ").trim();
	}

	function setqrcap(text){
		var cap = SK.$("qr-cap");
		if (!cap) return;
		var line = qrcaptext(text);
		cap.textContent = line;
		cap.title = line;
		cap.hidden = !line;
		if (!line) cap.style.marginTop = "";
	}

	function qrgap(){
		var img = SK.$("qr-img");
		var cap = SK.$("qr-cap");
		if (!img || !cap || cap.hidden || !img.naturalWidth) return;
		var w = img.naturalWidth;
		var h = img.naturalHeight;
		var canvas = document.createElement("canvas");
		canvas.width = w;
		canvas.height = h;
		var ctx = canvas.getContext("2d");
		var bottom = 0;
		try {
			ctx.drawImage(img, 0, 0);
			var data = ctx.getImageData(0, 0, w, h).data;
			var y;
			var x;
			for (y = h - 1; y >= 0; y--) {
				var white = true;
				for (x = 0; x < w; x++) {
					var i = (y * w + x) * 4;
					if (data[i] < 250 || data[i + 1] < 250 || data[i + 2] < 250) {
						white = false;
						break;
					}
				}
				if (!white) break;
				bottom++;
			}
		}
		catch (e) { bottom = 0; }
		var shown = img.clientWidth > 0 ? bottom * (img.clientWidth / w) : bottom;
		var half = parseFloat(window.getComputedStyle(cap).fontSize) / 2;
		if (isNaN(half)) half = 8;
		cap.style.marginTop = (half - shown) + "px";
	}

	function qrmake(){
		var text = SK.$("qr-in").value;
		SK.post("/api/qrmake", {
			text: text,
			format: SK.$("qr-fmt").value,
			encoding: SK.$("qr-enc").value,
		}, function(data){
			var img = SK.$("qr-img");
			if (!data || !data.png) {
				img.removeAttribute("src");
				img.hidden = true;
				setqrcap("");
				SK.msg("qr-msg", "没有图片", true);
				return;
			}
			img.onload = function(){ requestAnimationFrame(qrgap); };
			img.src = "data:image/png;base64," + data.png;
			img.hidden = false;
			setqrcap(text);
			SK.msg("qr-msg", "已生成");
		}, "qr-msg");
	}

	function qrscan(){
		var file = SK.$("qr-file").files && SK.$("qr-file").files[0];
		if (!file) {
			SK.msg("qr-smsg", "请选择图片", true);
			return;
		}
		var reader = new FileReader();
		reader.onload = function(){
			var raw = String(reader.result || "");
			var comma = raw.indexOf(",");
			var b64 = comma >= 0 ? raw.substring(comma + 1) : raw;
			SK.post("/api/qrscan", { base64: b64, format: "dict" }, function(data){
				var lines = [];
				var i;
				if (typeof data === "string") {
					SK.$("qr-out").value = data;
					return;
				}
				var arr = data;
				if (!arr || typeof arr.length !== "number") arr = [];
				for (i = 0; i < arr.length; i++) {
					var item = arr[i] || {};
					lines.push((item.type || "") + "  " + (item.text || ""));
				}
				SK.$("qr-out").value = lines.join("\n");
				if (!lines.length) SK.msg("qr-smsg", "未检测到条码或二维码", true);
			}, "qr-smsg");
		};
		reader.onerror = function(){ SK.msg("qr-smsg", "读图失败", true); };
		reader.readAsDataURL(file);
	}

	SK.qrcaptext = qrcaptext;
	SK.setqrcap = setqrcap;
	SK.qrgap = qrgap;
	SK.qrmake = qrmake;
	SK.qrscan = qrscan;
})();
