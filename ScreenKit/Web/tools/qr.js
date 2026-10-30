(function(){
	function qrcaptext(text){
		return String(text || "").replace(/\r|\n/g, " ").trim();
	}

	function setqrcap(text){
		var cap = SK.doms.qrCap;
		if (!cap) return;
		var line = qrcaptext(text);
		cap.textContent = line;
		cap.title = line;
		cap.hidden = !line || !qrcapon();
		if (cap.hidden) cap.style.marginTop = "";
	}

	function qrcapon(){
		var box = SK.doms.qrCapon;
		return !box || box.checked;
	}

	function onqrcap(){
		var img = SK.doms.qrImg;
		if (!img || img.hidden) return;
		setqrcap(SK.doms.qrIn.value);
		if (!SK.doms.qrCap.hidden) F.raf(qrgap);
	}

	function qrgap(){
		var img = SK.doms.qrImg;
		var cap = SK.doms.qrCap;
		if (!img || !cap || cap.hidden || !img.naturalWidth) return;
		var w = img.naturalWidth;
		var h = img.naturalHeight;
		var canvas = F.el("canvas");
		canvas.width = w;
		canvas.height = h;
		var ctx = F.ctx(canvas);
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
		var text = SK.doms.qrIn.value;
		SK.post("/api/qrmake", {
			text: text,
			format: SK.doms.qrFmt.value,
			encoding: SK.doms.qrEnc.value,
		}, function(data){
			var img = SK.doms.qrImg;
			if (!data || !data.png) {
				F.unset(img, "src");
				img.hidden = true;
				setqrcap("");
				SK.msg("qr-msg", "没有图片", true);
				return;
			}
			img.onload = function(){ F.raf(qrgap); };
			img.src = "data:image/png;base64," + data.png;
			img.hidden = false;
			setqrcap(text);
			SK.msg("qr-msg", "已生成");
		}, "qr-msg");
	}

	var qrsPic = null;
	var qrsUrl = "";

	function onqrsfile(){
		var file = SK.doms.qrsFile.files && SK.doms.qrsFile.files[0];
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
		F.pd(ev);
		setqrspic(file);
	}

	function setqrspic(file){
		qrsPic = file;
		if (qrsUrl) F.unurl(qrsUrl);
		qrsUrl = F.url(file);
		var img = SK.doms.qrsImg;
		img.src = qrsUrl;
		img.hidden = false;
		SK.doms.qrsOut.value = "";
		qrscan();
	}

	function qrscan(){
		var file = qrsPic || (SK.doms.qrsFile.files && SK.doms.qrsFile.files[0]);
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
					SK.doms.qrsOut.value = data;
					SK.msg("qrs-msg", "");
					return;
				}
				var arr = data;
				if (!arr || typeof arr.length !== "number") arr = [];
				for (i = 0; i < arr.length; i++) {
					var item = arr[i] || {};
					lines.push((item.type || "") + "  " + (item.text || ""));
				}
				SK.doms.qrsOut.value = lines.join("\n");
				if (!lines.length) SK.msg("qrs-msg", "未检测到条码或二维码", true);
				else SK.msg("qrs-msg", "");
			}, "qrs-msg");
		}, "qrs-msg");
	}

	SK.doms.qrsFile.onchange = onqrsfile;
	SK.doms.qrCapon.onchange = onqrcap;
	F.on(document, "paste", onqrpaste);

	SK.qrcaptext = qrcaptext;
	SK.setqrcap = setqrcap;
	SK.qrgap = qrgap;
	SK.qrmake = qrmake;
	SK.qrscan = qrscan;
})();
