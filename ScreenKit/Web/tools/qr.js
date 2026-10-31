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

	// 与 WPF 版 QrMake.withcaption 一致：裁掉底部静区，字号取宽/18，文字离墨迹约半行字高。
	function qrcompose(im, text){
		var w = im.naturalWidth || im.width;
		var h = im.naturalHeight || im.height;
		if (!w || !h) return im.src;
		var canvas = F.el("canvas");
		canvas.width = w;
		canvas.height = h;
		var ctx = F.ctx(canvas);
		ctx.drawImage(im, 0, 0, w, h);
		var contentH = Math.max(1, h - qrquiet(ctx, w, h));
		var fs = Math.max(16, w / 18);
		var font = qrfont(fs);
		var line = qrline(ctx, text, font, w);
		var ink = qrink(line, font, fs, w);
		var gap = Math.round(fs * 0.66);
		var bot = 8;
		canvas.width = w;
		canvas.height = contentH + gap + ink.height + bot;
		ctx = F.ctx(canvas);
		ctx.fillStyle = "#fff";
		ctx.fillRect(0, 0, canvas.width, canvas.height);
		ctx.drawImage(im, 0, 0, w, contentH);
		ctx.fillStyle = "#111";
		ctx.font = font;
		ctx.textAlign = "center";
		ctx.textBaseline = "alphabetic";
		ctx.fillText(line, w / 2, contentH + gap - ink.top + fs);
		return canvas.toDataURL("image/png");
	}

	function qrfont(fs){
		return fs + "px 'Microsoft YaHei UI', 'Segoe UI', sans-serif";
	}

	// 底部连续近白的行数。ZXing 留的静区算在里面，墨迹行不算。
	function qrquiet(ctx, w, h){
		var data;
		try { data = ctx.getImageData(0, 0, w, h).data; }
		catch (e) { return 0; }
		var rows = 0;
		var x;
		var y;
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
			rows++;
		}
		return rows;
	}

	// 单行显示，放不下就在末尾用省略号。
	function qrline(ctx, text, font, w){
		ctx.font = font;
		if (ctx.measureText(text).width <= w) return text;
		var line = text;
		while (line.length && ctx.measureText(line + "…").width > w) line = line.substring(0, line.length - 1);
		return line + "…";
	}

	// 文字墨迹在行框里的位置，用来把墨迹顶部对齐到指定间距，避免行框顶部空白把间隔撑大。
	function qrink(line, font, fs, w){
		var cw = Math.max(1, Math.ceil(w));
		var ch = Math.ceil(fs * 2.2);
		var c = F.el("canvas");
		c.width = cw;
		c.height = ch;
		var ctx = F.ctx(c);
		ctx.fillStyle = "#000";
		ctx.font = font;
		ctx.textAlign = "center";
		ctx.textBaseline = "alphabetic";
		ctx.fillText(line, cw / 2, fs);
		var data;
		try { data = ctx.getImageData(0, 0, cw, ch).data; }
		catch (e) { return { top: Math.round(fs * 0.8), height: Math.round(fs * 0.8) }; }
		var top = -1;
		var bottom = -1;
		var x;
		var y;
		for (y = 0; y < ch; y++) {
			var ink = false;
			for (x = 0; x < cw; x++) {
				if (data[(y * cw + x) * 4 + 3] > 0) {
					ink = true;
					break;
				}
			}
			if (ink) {
				if (top < 0) top = y;
				bottom = y;
			}
		}
		if (top < 0) return { top: Math.round(fs * 0.8), height: Math.round(fs * 0.8) };
		return { top: top, height: bottom + 1 - top };
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
