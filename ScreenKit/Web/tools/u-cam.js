(function(){
	var stream = null;
	var scanOn = false;
	var scanning = false;
	var scanGen = 0;
	var gen = 0;
	var detector = null;
	var detBroken = false;
	var last = "";

	SK.regutil("u-cam", {
		title: "相机",
		hideGo: true,
		hideCopy: true,
		hideOut: true,
		body: function(){
			return '<p class="hint" id="cam-hint"></p>'
				+ '<video id="cam-view" autoplay playsinline muted></video>'
				+ '<span id="cam-devlab"></span>'
				+ '<select id="cam-dev"></select>'
				+ '<div class="ops">'
				+ '<button type="button" id="cam-shot"></button>'
				+ '<button type="button" id="cam-scan"></button>'
				+ '<button type="button" id="cam-save"></button>'
				+ '</div>'
				+ '<img id="cam-shotimg" alt="" hidden>'
				+ '<pre id="cam-code"></pre>'
				+ '<p id="cam-msg" class="msg"></p>';
		},
		open: function(){
			labels();
			var shot = F.id("cam-shot");
			var scan = F.id("cam-scan");
			var save = F.id("cam-save");
			var dev = F.id("cam-dev");
			if (shot) shot.onclick = snapshot;
			if (scan) scan.onclick = togglescan;
			if (save) save.onclick = download;
			if (dev) dev.onchange = function(){ openid(dev.value); };
			openid("");
			arm();
		}
	});

	function tx(key, fb){
		var s = SK.t ? SK.t(key) : "";
		return s || fb;
	}

	function labels(){
		var hint = F.id("cam-hint");
		if (hint) hint.textContent = tx("web.cam.hint", "用浏览器的摄像头拍照，或扫描画面里的二维码和条码。");
		var lab = F.id("cam-devlab");
		if (lab) lab.textContent = tx("web.cam.dev", "摄像头");
		var shot = F.id("cam-shot");
		if (shot) shot.textContent = tx("web.cam.shot", "拍照");
		var scan = F.id("cam-scan");
		if (scan) scan.textContent = tx(scanOn ? "web.cam.scanstop" : "web.cam.scan", scanOn ? "停止扫码" : "扫码");
		var save = F.id("cam-save");
		if (save) save.textContent = tx("web.cam.save", "保存图片");
	}

	function arm(){
		var mine = ++gen;
		function tick(){
			if (mine !== gen) return;
			if (SK.curtool !== "u-cam") {
				halt();
				return;
			}
			if (scanOn) grabscan();
			setTimeout(tick, 700);
		}
		setTimeout(tick, 700);
	}

	function halt(){
		gen++;
		scanOn = false;
		scanning = false;
		if (stream) {
			var tracks = stream.getTracks();
			for (var i = 0; i < tracks.length; i++) tracks[i].stop();
			stream = null;
		}
		var v = F.id("cam-view");
		if (v) v.srcObject = null;
	}

	function openid(id){
		if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
			SK.msg("cam-msg", tx("web.cam.need", "浏览器没有开放摄像头"), true);
			return;
		}
		var video = id ? { deviceId: { exact: id } } : true;
		navigator.mediaDevices.getUserMedia({ video: video, audio: false }).then(function(st){
			if (SK.curtool !== "u-cam") {
				st.getTracks().forEach(function(t){ t.stop(); });
				return;
			}
			if (stream) stream.getTracks().forEach(function(t){ t.stop(); });
			stream = st;
			var v = F.id("cam-view");
			if (v) v.srcObject = st;
			listdev();
			SK.msg("cam-msg", "");
		}, function(){
			SK.msg("cam-msg", tx("web.cam.need", "浏览器没有开放摄像头"), true);
		});
	}

	function listdev(){
		if (!navigator.mediaDevices || !navigator.mediaDevices.enumerateDevices) return;
		navigator.mediaDevices.enumerateDevices().then(function(list){
			var sel = F.id("cam-dev");
			if (!sel) return;
			var cur = "";
			if (stream && stream.getVideoTracks().length) {
				var settings = stream.getVideoTracks()[0].getSettings();
				cur = settings && settings.deviceId ? settings.deviceId : "";
			}
			var prev = sel.value;
			sel.innerHTML = "";
			var n = 0;
			for (var i = 0; i < list.length; i++) {
				if (list[i].kind !== "videoinput") continue;
				var o = F.el("option");
				o.value = list[i].deviceId;
				n++;
				o.textContent = list[i].label || ("Camera " + n);
				sel.appendChild(o);
			}
			if (cur) sel.value = cur;
			else if (prev) sel.value = prev;
		});
	}

	function frame(maxw){
		var v = F.id("cam-view");
		if (!v || !v.videoWidth) return null;
		var w = v.videoWidth;
		var h = v.videoHeight;
		if (maxw && w > maxw) {
			h = Math.round(h * maxw / w);
			w = maxw;
		}
		var c = F.el("canvas");
		c.width = w;
		c.height = h;
		var g = c.getContext("2d");
		if (!g) return null;
		g.drawImage(v, 0, 0, w, h);
		return c;
	}

	function snapshot(){
		var c = frame(0);
		if (!c) {
			SK.msg("cam-msg", tx("web.cam.need", "浏览器没有开放摄像头"), true);
			return;
		}
		var img = F.id("cam-shotimg");
		if (img) {
			img.src = c.toDataURL("image/jpeg", 0.92);
			img.hidden = false;
		}
		SK.msg("cam-msg", "");
	}

	function download(){
		var img = F.id("cam-shotimg");
		if (!img || !img.src) return;
		var a = F.el("a");
		a.href = img.src;
		a.download = "cam.jpg";
		a.click();
	}

	function togglescan(){
		scanOn = !scanOn;
		if (!scanOn) scanning = false;
		labels();
	}

	function grabscan(){
		if (scanning) return;
		var v = F.id("cam-view");
		if (!v || !v.videoWidth) return;
		if (!detBroken && window.BarcodeDetector) {
			scanning = true;
			try {
				if (!detector) detector = new BarcodeDetector();
				detector.detect(v).then(function(codes){
					scanning = false;
					if (!scanOn || !codes || !codes.length) return;
					var lines = [];
					for (var i = 0; i < codes.length; i++) {
						var c = codes[i];
						if (!c || !c.rawValue) continue;
						lines.push("[" + (c.format || "") + "] " + c.rawValue);
					}
					showlines(lines);
				}, function(){
					scanning = false;
					detBroken = true;
					postscan();
				});
				return;
			}
			catch (ex) {
				detBroken = true;
				scanning = false;
			}
		}
		postscan();
	}

	function postscan(){
		if (scanning) return;
		var c = frame(960);
		if (!c) return;
		var url = c.toDataURL("image/jpeg", 0.7);
		var i = url.indexOf(",");
		var b64 = i >= 0 ? url.substring(i + 1) : url;
		scanning = true;
		var mine = ++scanGen;
		SK.post("/api/qrscan", { base64: b64, format: "dict" }, function(jo){
			if (mine === scanGen) scanning = false;
			if (!scanOn || !jo || jo.code === 101) return;
			var data = jo.data;
			var lines = [];
			if (typeof data === "string") lines.push(data);
			else if (data && data.length) {
				for (var k = 0; k < data.length; k++) {
					var it = data[k];
					if (!it) continue;
					if (typeof it === "string") lines.push(it);
					else if (it.text) lines.push("[" + (it.type || "") + "] " + it.text);
				}
			}
			showlines(lines);
		}, "cam-msg", true);
		setTimeout(function(){
			if (mine === scanGen) scanning = false;
		}, 4000);
	}

	function showlines(lines){
		if (!lines || !lines.length) return;
		var text = lines.join("\n");
		if (text === last) return;
		last = text;
		var box = F.id("cam-code");
		if (box) box.textContent = text;
		SK.msg("cam-msg", "");
	}
})();
