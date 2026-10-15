(function(){
	var ws = null;
	var decoder = null;
	var sps = null;
	var pps = null;
	var cfgSps = null;
	var cfgPps = null;
	var waitkey = true;
	var ts = 0;
	SK.regutil("u-rtsp", {
		title: "RTSP播放",
		own: true,
		hideGo: true,
		hideCopy: true,
		hideOut: true,
		body: body,
		open: open
	});

	function body(){
		var hint = SK.t("web.rtsp.hint") || "";
		var play = SK.t("web.rtsp.play") || "播放";
		var stop = SK.t("web.rtsp.stop") || "停止";
		var saved = "";
		try { saved = localStorage.getItem("sk-rtsp") || ""; } catch (e) { saved = ""; }
		return '<div class="rtsp">'
			+ '<div class="rtsp-bar">'
			+ '<input id="rtsp-url" type="text" spellcheck="false" value="' + SK.esc(saved) + '" placeholder="rtsp://user:pass@host:554/Streaming/Channels/101">'
			+ '<button type="button" id="rtsp-play">' + SK.esc(play) + "</button>"
			+ '<button type="button" id="rtsp-stop" hidden>' + SK.esc(stop) + "</button>"
			+ "</div>"
			+ '<div id="rtsp-msg" class="rtsp-msg" data-k="web.rtsp.hint">' + SK.esc(hint) + "</div>"
			+ '<div class="rtsp-stage"><canvas id="rtsp-view"></canvas></div>'
			+ "</div>";
	}

	function open(){
		var play = F.id("rtsp-play");
		var stop = F.id("rtsp-stop");
		if (play) play.onclick = start;
		if (stop) stop.onclick = halt;
	}

	function start(){
		var url = (F.id("rtsp-url").value || "").trim();
		if (!/^rtsp:\/\//i.test(url)) {
			say(SK.t("web.rtsp.bad") || "", true);
			return;
		}
		if (typeof VideoDecoder === "undefined") {
			say(SK.t("web.rtsp.nocodec") || "", true);
			return;
		}
		try { localStorage.setItem("sk-rtsp", url); } catch (e) {}
		halt();
		sps = null;
		pps = null;
		cfgSps = null;
		cfgPps = null;
		waitkey = true;
		ts = 0;
		var wsurl = (location.protocol === "https:" ? "wss://" : "ws://") + location.host + "/rtsp?rtsp=" + encodeURIComponent(url);
		ws = new WebSocket(wsurl);
		ws.binaryType = "arraybuffer";
		F.id("rtsp-play").hidden = true;
		F.id("rtsp-stop").hidden = false;
		say(SK.t("web.rtsp.wait") || "", false);
		ws.onmessage = function(ev){
			if (typeof ev.data === "string") {
				say(ev.data, ev.data.indexOf("ERROR") === 0);
				return;
			}
			onbin(new Uint8Array(ev.data));
		};
		ws.onclose = function(){
			if (F.id("rtsp-play")) {
				F.id("rtsp-play").hidden = false;
				F.id("rtsp-stop").hidden = true;
			}
			closedec();
		};
		ws.onerror = function(){ say(SK.t("web.rtsp.bad") || "", true); };
	}

	function halt(){
		if (ws) {
			try { ws.close(); } catch (e) {}
			ws = null;
		}
		closedec();
		var play = F.id("rtsp-play");
		var stop = F.id("rtsp-stop");
		if (play) play.hidden = false;
		if (stop) stop.hidden = true;
	}

	function say(text, bad){
		var el = F.id("rtsp-msg");
		if (!el) return;
		F.unset(el, "data-k");
		el.textContent = text || "";
		el.style.color = bad ? "var(--danger)" : "";
	}

	function onbin(frame){
		var payload = frame;
		if (frame.length >= 8) {
			var sc = (frame[4] === 0 && frame[5] === 0 && frame[6] === 0 && frame[7] === 1)
				|| (frame[4] === 0 && frame[5] === 0 && frame[6] === 1);
			if (sc) payload = frame.subarray(4);
		}
		var nals = split(payload);
		var parts = [];
		var key = false;
		var bytes = 0;
		for (var i = 0; i < nals.length; i++) {
			var nal = nals[i];
			if (!nal.length) continue;
			var typ = nal[0] & 0x1F;
			if (typ === 7) { sps = nal.slice(); continue; }
			if (typ === 8) { pps = nal.slice(); continue; }
			if (typ !== 1 && typ !== 5 && typ !== 6) continue;
			if (typ === 5) key = true;
			parts.push(nal);
			bytes += 4 + nal.length;
		}
		setup();
		if (!parts.length || !decoder || decoder.state !== "configured") return;
		if (waitkey && !key) return;
		if (key) waitkey = false;
		var data = new Uint8Array(bytes);
		var o = 0;
		for (var j = 0; j < parts.length; j++) {
			var n = parts[j];
			data[o] = (n.length >>> 24) & 255;
			data[o + 1] = (n.length >>> 16) & 255;
			data[o + 2] = (n.length >>> 8) & 255;
			data[o + 3] = n.length & 255;
			data.set(n, o + 4);
			o += 4 + n.length;
		}
		try {
			decoder.decode(new EncodedVideoChunk({
				type: key ? "key" : "delta",
				timestamp: ts,
				data: data
			}));
			ts += 33333;
		} catch (e) { say(e.message, true); }
	}

	function setup(){
		if (!sps || !pps || sps.length < 4) return;
		if (same(cfgSps, sps) && same(cfgPps, pps) && decoder && decoder.state === "configured") return;
		if (!decoder || decoder.state === "closed") makedec();
		try {
			decoder.configure({
				codec: codecof(sps),
				description: avcconfig(sps, pps),
				hardwareAcceleration: "prefer-software",
				optimizeForLatency: true
			});
			cfgSps = sps;
			cfgPps = pps;
		} catch (e) {
			say(e.message, true);
		}
	}

	function same(a, b){
		if (!a || !b || a.length !== b.length) return false;
		for (var i = 0; i < a.length; i++) if (a[i] !== b[i]) return false;
		return true;
	}

	function makedec(){
		closedec();
		var canvas = F.id("rtsp-view");
		var ctx = canvas.getContext("2d", { alpha: false });
		decoder = new VideoDecoder({
			output: function(frame){
				if (canvas.width !== frame.displayWidth || canvas.height !== frame.displayHeight) {
					canvas.width = frame.displayWidth;
					canvas.height = frame.displayHeight;
				}
				ctx.drawImage(frame, 0, 0);
				frame.close();
			},
			error: function(e){ say(e.message, true); }
		});
	}

	function closedec(){
		if (!decoder) return;
		try { decoder.close(); } catch (e) {}
		decoder = null;
	}

	function split(data){
		var nals = [];
		var i = 0;
		var start = -1;
		while (i < data.length) {
			var sc = 0;
			if (i + 3 < data.length && data[i] === 0 && data[i + 1] === 0 && data[i + 2] === 0 && data[i + 3] === 1) sc = 4;
			else if (i + 2 < data.length && data[i] === 0 && data[i + 1] === 0 && data[i + 2] === 1) sc = 3;
			if (sc) {
				if (start >= 0) nals.push(data.subarray(start, i));
				start = i + sc;
				i += sc;
			} else i++;
		}
		if (start >= 0 && start < data.length) nals.push(data.subarray(start));
		return nals;
	}

	function avcc(nal){
		var out = new Uint8Array(4 + nal.length);
		out[0] = (nal.length >>> 24) & 255;
		out[1] = (nal.length >>> 16) & 255;
		out[2] = (nal.length >>> 8) & 255;
		out[3] = nal.length & 255;
		out.set(nal, 4);
		return out;
	}

	function avcconfig(s, p){
		var cfg = new Uint8Array(11 + s.length + p.length);
		cfg[0] = 1;
		cfg[1] = s[1];
		cfg[2] = s[2];
		cfg[3] = s[3];
		cfg[4] = 0xFF;
		cfg[5] = 0xE1;
		cfg[6] = (s.length >> 8) & 255;
		cfg[7] = s.length & 255;
		cfg.set(s, 8);
		cfg[8 + s.length] = 1;
		cfg[9 + s.length] = (p.length >> 8) & 255;
		cfg[10 + s.length] = p.length & 255;
		cfg.set(p, 11 + s.length);
		return cfg;
	}

	function codecof(s){
		function hex(b){ var h = b.toString(16); return h.length < 2 ? "0" + h : h; }
		return "avc1." + hex(s[1]) + hex(s[2]) + hex(s[3]);
	}
})();
