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

	var qrsPic = null;
	var qrsUrl = "";

	function onqrsfile(){
		var file = SK.$("qrs-file").files && SK.$("qrs-file").files[0];
		if (file) setqrspic(file);
	}

	function onqrpaste(ev){
		if (SK.curtool !== "qrs") return;
		var cd = ev.clipboardData;
		if (!cd) return;
		var file = null;
		var i;
		if (cd.items) {
			for (i = 0; i < cd.items.length; i++) {
				var item = cd.items[i];
				if (item.kind === "file" && item.type.indexOf("image/") === 0) {
					file = item.getAsFile();
					break;
				}
			}
		}
		if (!file && cd.files && cd.files.length && cd.files[0].type.indexOf("image/") === 0)
			file = cd.files[0];
		if (!file) return;
		ev.preventDefault();
		setqrspic(file);
	}

	function setqrspic(file){
		qrsPic = file;
		if (qrsUrl) URL.revokeObjectURL(qrsUrl);
		qrsUrl = URL.createObjectURL(file);
		var img = SK.$("qrs-img");
		img.src = qrsUrl;
		img.hidden = false;
		SK.$("qrs-out").value = "";
		qrscan();
	}

	function qrscan(){
		var file = qrsPic || (SK.$("qrs-file").files && SK.$("qrs-file").files[0]);
		if (!file) {
			SK.msg("qrs-msg", "请选择或粘贴图片", true);
			return;
		}
		SK.msg("qrs-msg", "识别中…");
		SK.readfile(file, function(b64){
			SK.post("/api/qrscan", { base64: b64, format: "dict" }, function(data){
				var lines = [];
				var i;
				if (typeof data === "string") {
					SK.$("qrs-out").value = data;
					SK.msg("qrs-msg", "");
					return;
				}
				var arr = data;
				if (!arr || typeof arr.length !== "number") arr = [];
				for (i = 0; i < arr.length; i++) {
					var item = arr[i] || {};
					lines.push((item.type || "") + "  " + (item.text || ""));
				}
				SK.$("qrs-out").value = lines.join("\n");
				if (!lines.length) SK.msg("qrs-msg", "未检测到条码或二维码", true);
				else SK.msg("qrs-msg", "");
			}, "qrs-msg");
		}, "qrs-msg");
	}

	SK.$("qrs-file").onchange = onqrsfile;
	document.addEventListener("paste", onqrpaste);

	SK.qrcaptext = qrcaptext;
	SK.setqrcap = setqrcap;
	SK.qrgap = qrgap;
	SK.qrmake = qrmake;
	SK.qrscan = qrscan;
})();
