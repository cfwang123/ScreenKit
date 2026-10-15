(function(){
	var stream = null;
	var scanOn = false;
	var scanning = false;
	var scanGen = 0;
	var gen = 0;
	var detector = null;
	var detBroken = false;
	var last = "";
	var icap = false;
	var torchOn = false;
	var shotUrl = "";
	var reader = null;
	var readGen = 0;
	var frameScan = false;
	var nextDetect = 0;

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
				+ '<button type="button" id="cam-mode"></button>'
				+ '<button type="button" id="cam-scan"></button>'
				+ '<button type="button" id="cam-save"></button>'
				+ '</div>'
				+ '<div id="cam-tune" hidden>'
				+ '<label id="cam-zoomlab"><span id="cam-zoomtxt"></span><input id="cam-zoom" type="range"></label>'
				+ '<button type="button" id="cam-torch" hidden></button>'
				+ '<button type="button" id="cam-focus" hidden></button>'
				+ '</div>'
				+ '<img id="cam-shotimg" alt="" hidden>'
				+ '<pre id="cam-code"></pre>'
				+ '<p id="cam-msg" class="msg"></p>';
		},
		open: function(){
			labels();
			var shot = F.id("cam-shot");
			var mode = F.id("cam-mode");
			var scan = F.id("cam-scan");
			var save = F.id("cam-save");
			var dev = F.id("cam-dev");
			var zoom = F.id("cam-zoom");
			var torch = F.id("cam-torch");
			var focus = F.id("cam-focus");
			if (shot) shot.onclick = snapshot;
			if (mode) mode.onclick = toggleicap;
			if (scan) scan.onclick = togglescan;
			if (save) save.onclick = download;
			if (dev) dev.onchange = function(){ openid(dev.value); };
			if (zoom) zoom.oninput = applyzoom;
			if (torch) torch.onclick = toggletorch;
			if (focus) focus.onclick = applyfocus;
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
		var mode = F.id("cam-mode");
		if (mode) mode.textContent = tx(icap ? "web.cam.mode" : "web.cam.modeprev", icap ? "静帧拍照" : "预览拍照");
		var zoomtxt = F.id("cam-zoomtxt");
		if (zoomtxt) zoomtxt.textContent = tx("web.cam.zoom", "变焦");
		var torch = F.id("cam-torch");
		if (torch) torch.textContent = tx(torchOn ? "web.cam.torchoff" : "web.cam.torch", torchOn ? "关闭手电" : "手电");
		var focus = F.id("cam-focus");
		if (focus) focus.textContent = tx("web.cam.focus", "对焦");
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
		stopframes();
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
			stopframes();
			torchOn = false;
			if (stream) stream.getTracks().forEach(function(t){ t.stop(); });
			stream = st;
			var v = F.id("cam-view");
			if (v) v.srcObject = st;
			listdev();
			tunecols();
			if (scanOn) startframes();
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

	function trackof(){
		return stream && stream.getVideoTracks().length ? stream.getVideoTracks()[0] : null;
	}

	function toggleicap(){
		if (!window.ImageCapture) {
			SK.msg("cam-msg", tx("web.cam.noicap", "这个浏览器没有静帧拍照"), true);
			return;
		}
		icap = !icap;
		labels();
	}

	function tunecols(){
		var row = F.id("cam-tune");
		var zoom = F.id("cam-zoom");
		var torch = F.id("cam-torch");
		var focus = F.id("cam-focus");
		var track = trackof();
		var caps = {};
		if (track && track.getCapabilities) {
			try { caps = track.getCapabilities() || {}; }
			catch (ex) { caps = {}; }
		}
		var any = false;
		if (zoom && zoom.parentElement) {
			var z = caps.zoom;
			if (z && isFinite(z.max) && z.max > z.min) {
				zoom.min = z.min;
				zoom.max = z.max;
				zoom.step = z.step || 0.01;
				var settings = track.getSettings ? track.getSettings() : {};
				zoom.value = settings && settings.zoom != null ? settings.zoom : z.min;
				zoom.parentElement.hidden = false;
				any = true;
			}
			else zoom.parentElement.hidden = true;
		}
		if (torch) {
			torch.hidden = !caps.torch;
			if (caps.torch) any = true;
		}
		if (focus) {
			focus.hidden = !(caps.focusMode && caps.focusMode.length);
			if (caps.focusMode && caps.focusMode.length) any = true;
		}
		if (row) row.hidden = !any;
		labels();
	}

	function applyzoom(){
		var track = trackof();
		var zoom = F.id("cam-zoom");
		if (!track || !zoom || !track.applyConstraints) return;
		track.applyConstraints({ advanced: [{ zoom: Number(zoom.value) }] }).then(function(){}, function(){});
	}

	function toggletorch(){
		var track = trackof();
		if (!track || !track.applyConstraints) return;
		torchOn = !torchOn;
		var want = torchOn;
		track.applyConstraints({ advanced: [{ torch: want }] }).then(function(){
			labels();
		}, function(){
			torchOn = !want;
			labels();
		});
	}

	function applyfocus(){
		var track = trackof();
		if (!track || !track.applyConstraints || !track.getCapabilities) return;
		var caps = {};
		try { caps = track.getCapabilities() || {}; }
		catch (ex) { return; }
		var modes = caps.focusMode || [];
		var mode = "";
		if (modes.indexOf("continuous") >= 0) mode = "continuous";
		else if (modes.indexOf("single-shot") >= 0) mode = "single-shot";
		else if (modes.length) mode = modes[0];
		if (!mode) return;
		track.applyConstraints({ advanced: [{ focusMode: mode }] }).then(function(){}, function(){});
	}

	function showshot(url){
		var img = F.id("cam-shotimg");
		if (!img) return;
		if (shotUrl && shotUrl !== url) F.unurl(shotUrl);
		shotUrl = url || "";
		img.src = url;
		img.hidden = !url;
	}

	function snapshot(){
		if (icap) { shotstill(); return; }
		shotpreview();
	}

	function shotpreview(){
		var c = frame(0);
		if (!c) {
			SK.msg("cam-msg", tx("web.cam.need", "浏览器没有开放摄像头"), true);
			return;
		}
		showshot(c.toDataURL("image/jpeg", 0.92));
		SK.msg("cam-msg", "");
	}

	function shotstill(){
		var track = trackof();
		if (!track || !window.ImageCapture) {
			SK.msg("cam-msg", tx("web.cam.noicap", "这个浏览器没有静帧拍照"), true);
			return;
		}
		var cap;
		try { cap = new ImageCapture(track); }
		catch (ex) {
			SK.msg("cam-msg", tx("web.cam.noicap", "这个浏览器没有静帧拍照"), true);
			return;
		}
		var resume = frameScan;
		if (resume) stopframes();
		cap.takePhoto().then(function(blob){
			if (resume && scanOn) startframes();
			if (SK.curtool !== "u-cam") return;
			showshot(F.url(blob));
			SK.msg("cam-msg", "");
		}, function(){
			if (resume && scanOn) startframes();
			if (SK.curtool !== "u-cam") return;
			shotpreview();
			SK.msg("cam-msg", tx("web.cam.icapfail", "静帧失败，已改用预览画面"), true);
		});
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
		if (!scanOn) {
			scanning = false;
			stopframes();
		}
		else startframes();
		labels();
	}

	function stopframes(){
		readGen++;
		frameScan = false;
		nextDetect = 0;
		if (!reader) return;
		var r = reader;
		reader = null;
		r.cancel().then(function(){}, function(){});
	}

	function startframes(){
		stopframes();
		var track = trackof();
		if (!scanOn || !track || !window.MediaStreamTrackProcessor || !window.BarcodeDetector) return false;
		var mine = ++readGen;
		try {
			var proc = new MediaStreamTrackProcessor({ track: track });
			reader = proc.readable.getReader();
		}
		catch (ex) {
			reader = null;
			return false;
		}
		frameScan = true;
		pump(mine);
		return true;
	}

	function pump(mine){
		if (mine !== readGen || !reader) return;
		reader.read().then(function(res){
			if (mine !== readGen) {
				if (res && res.value && res.value.close) res.value.close();
				return;
			}
			if (!res || res.done) {
				frameScan = false;
				return;
			}
			var frame = res.value;
			var now = Date.now();
			if (!scanOn || scanning || detBroken || now < nextDetect) {
				if (frame && frame.close) frame.close();
				pump(mine);
				return;
			}
			nextDetect = now + 400;
			scanning = true;
			var det;
			try { det = detector || (detector = new BarcodeDetector()); }
			catch (ex) {
				scanning = false;
				detBroken = true;
				if (frame && frame.close) frame.close();
				stopframes();
				return;
			}
			det.detect(frame).then(function(codes){
				if (frame && frame.close) frame.close();
				scanning = false;
				if (scanOn && codes && codes.length) fillcodes(codes);
				pump(mine);
			}, function(){
				if (frame && frame.close) frame.close();
				scanning = false;
				stopframes();
			});
		}, function(){
			if (mine === readGen) frameScan = false;
		});
	}

	function fillcodes(codes){
		var lines = [];
		var i, c;
		for (i = 0; i < codes.length; i++) {
			c = codes[i];
			if (!c || !c.rawValue) continue;
			lines.push("[" + (c.format || "") + "] " + c.rawValue);
		}
		showlines(lines);
	}

	function grabscan(){
		if (scanning || frameScan) return;
		var v = F.id("cam-view");
		if (!v || !v.videoWidth) return;
		if (!detBroken && window.BarcodeDetector) {
			scanning = true;
			try {
				if (!detector) detector = new BarcodeDetector();
				detector.detect(v).then(function(codes){
					scanning = false;
					if (!scanOn || !codes || !codes.length) return;
					fillcodes(codes);
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
