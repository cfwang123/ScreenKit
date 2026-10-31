(function(){
	var timer = null;
	var seq = 0;

	function qrcaptext(text){
		return String(text || "").replace(/\r|\n/g, " ").trim();
	}

	function qrenc(){
		var gbk = F.id("qrEncGbk");
		var hex = F.id("qrEncHex");
		if (gbk && gbk.checked) return "gbk";
		if (hex && hex.checked) return "hex";
		return "utf8";
	}

	function qrcapon(){
		var box = SK.doms.qrCapon;
		return !box || box.checked;
	}

	// 输入或切换选项后等 200ms 再生成，连续打字只发最后一次请求。
	function qrschedule(){
		if (timer) clearTimeout(timer);
		timer = setTimeout(qrmake, 200);
	}

	function qrmake(){
		if (timer) {
			clearTimeout(timer);
			timer = null;
		}
		var img = SK.doms.qrImg;
		var text = SK.doms.qrIn.value;
		if (!text) {
			seq++;
			F.unset(img, "src");
			img.hidden = true;
			SK.msg("qr-msg", "");
			return;
		}
		var mine = ++seq;
		SK.post("/api/qrmake", {
			text: text,
			format: SK.doms.qrFmt.value,
			encoding: qrenc(),
		}, function(data){
			if (mine !== seq) return;
			if (!data || !data.png) {
				F.unset(img, "src");
				img.hidden = true;
				SK.msg("qr-msg", "没有图片", true);
				return;
			}
			qrshow("data:image/png;base64," + data.png, text, mine);
			SK.msg("qr-msg", "已生成");
		}, "qr-msg");
	}

	// 带文字时把文字画进图片底部，不用页面上的说明文字。
	function qrshow(src, text, mine){
		var im = new Image();
		im.onload = function(){
			if (mine !== seq) return;
			var out = src;
			var cap = qrcaptext(text);
			if (qrcapon() && cap) {
				try { out = qrcompose(im, cap); }
				catch (e) { out = src; }
			}
			var img = SK.doms.qrImg;
			img.src = out;
			img.hidden = false;
		};
		im.src = src;
	}

	function qrcompose(im, text){
		var w = im.naturalWidth || im.width;
		var h = im.naturalHeight || im.height;
		if (!w || !h) return im.src;
		var fs = Math.round(Math.max(12, Math.min(30, w * 0.048)));
		var lh = Math.round(fs * 1.32);
		var pad = Math.round(fs * 0.55);
		var font = fs + "px 'Microsoft YaHei UI', 'Segoe UI', sans-serif";
		var canvas = F.el("canvas");
		var ctx = F.ctx(canvas);
		ctx.font = font;
		var lines = qrwrap(ctx, text, w * 0.88, 3);
		canvas.width = w;
		canvas.height = h + pad * 2 + lines.length * lh;
		ctx = F.ctx(canvas);
		ctx.fillStyle = "#fff";
		ctx.fillRect(0, 0, canvas.width, canvas.height);
		ctx.drawImage(im, 0, 0, w, h);
		ctx.fillStyle = "#111";
		ctx.font = font;
		ctx.textAlign = "center";
		ctx.textBaseline = "middle";
		var i;
		for (i = 0; i < lines.length; i++)
			ctx.fillText(lines[i], w / 2, h + pad + lh * (i + 0.5));
		return canvas.toDataURL("image/png");
	}

	// 按画布宽度断行，最多几行；放不下的部分在最后一行用省略号收尾。
	function qrwrap(ctx, text, maxw, maxlines){
		var lines = [];
		var line = "";
		var i;
		var ch;
		for (i = 0; i < text.length; i++) {
			ch = text.charAt(i);
			if (line && ctx.measureText(line + ch).width > maxw) {
				if (lines.length === maxlines - 1) {
					var rest = line + text.substring(i);
					while (rest.length && ctx.measureText(rest + "…").width > maxw) rest = rest.substring(0, rest.length - 1);
					lines.push(rest + "…");
					return lines;
				}
				lines.push(line);
				line = ch;
			}
			else line += ch;
		}
		if (line) lines.push(line);
		return lines;
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
	SK.doms.qrIn.oninput = qrschedule;
	SK.doms.qrCapon.onchange = qrschedule;
	SK.doms.qrFmt.onchange = qrschedule;
	var encids = ["qrEncUtf8", "qrEncGbk", "qrEncHex"];
	var ei;
	for (ei = 0; ei < encids.length; ei++) {
		var enc = F.id(encids[ei]);
		if (!enc) continue;
		enc.name = "qrEnc";
		enc.onchange = qrschedule;
	}
	F.on(document, "paste", onqrpaste);

	SK.qrcaptext = qrcaptext;
	SK.qrmake = qrmake;
	SK.qrschedule = qrschedule;
	SK.qrcompose = qrcompose;
	SK.qrscan = qrscan;
})();
