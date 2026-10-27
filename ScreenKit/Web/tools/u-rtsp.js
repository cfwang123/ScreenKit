(function(){
	var ws = null;
	var decoder = null;
	var sps = null;
	var pps = null;
	var cfgSps = null;
	var cfgPps = null;
	var waitkey = true;
	var ts = 0;
	var actx = null;
	var adec = null;
	var aacok = false;
	var nextAt = 0;
	var atsus = 0;
	var ULAW = null;
	SK.regutil("u-rtsp", {
		title: "RTSP播放",
		own: true,
		hideGo: true,
		hideCopy: true,
		hideOut: true,
		body: body,
		open: open,
		dispose: halt
	});

	function body(){
		var hint = SK.t("web.rtsp.hint") || "";
		var play = SK.t("web.rtsp.play") || "播放";
		var stop = SK.t("web.rtsp.stop") || "停止";
		var sound = SK.t("web.rtsp.audio") || "声音";
		var saved = "";
		var hear = "1";
		try { saved = localStorage.getItem("sk-rtsp") || ""; } catch (e) { saved = ""; }
		try { hear = localStorage.getItem("sk-rtsp-audio"); if (hear == null) hear = "1"; } catch (e2) { hear = "1"; }
		return '<div class="rtsp">'
			+ '<div class="rtsp-bar">'
			+ '<input id="rtsp-url" type="text" spellcheck="false" value="' + SK.esc(saved) + '" placeholder="rtsp://user:pass@host:554/Streaming/Channels/101">'
			+ '<label><input id="rtsp-audio" type="checkbox"' + (hear === "0" ? "" : " checked") + ">" + SK.esc(sound) + "</label>"
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
		var box = F.id("rtsp-audio");
		var hear = !box || box.checked;
		try { localStorage.setItem("sk-rtsp", url); } catch (e) {}
		try { localStorage.setItem("sk-rtsp-audio", hear ? "1" : "0"); } catch (e2) {}
		halt();
		sps = null;
		pps = null;
		cfgSps = null;
		cfgPps = null;
		waitkey = true;
		ts = 0;
		if (hear && typeof AudioContext !== "undefined") {
			try { actx = new AudioContext(); F.sink(actx); actx.resume(); } catch (e3) { actx = null; }
		}
		var wsurl = (location.protocol === "https:" ? "wss://" : "ws://") + location.host + "/rtsp?rtsp=" + encodeURIComponent(url) + "&audio=" + (hear ? "1" : "0");
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
			closeaud();
		};
		ws.onerror = function(){ say(SK.t("web.rtsp.bad") || "", true); };
	}

	function halt(){
		if (ws) {
			try { ws.close(); } catch (e) {}
			ws = null;
		}
		closedec();
		closeaud();
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
		if (frame.length >= 9 && frame[0] === 0xA1) {
			var video = frame.length >= 8 && frame[4] === 0 && frame[5] === 0 && (frame[6] === 1 || (frame[6] === 0 && frame[7] === 1));
			if (!video) { onaud(frame); return; }
		}
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

	function onaud(frame){
		var kind = frame[1];
		var rate = (frame[2] << 8) | frame[3];
		var ch = frame[4] || 1;
		var data = frame.subarray(9);
		if (!rate) rate = 8000;
		if (kind === 3) { aaccfg(rate, ch, data); return; }
		if (kind === 0 || kind === 1) playpcm(g711(kind, data), rate);
		else if (kind === 2) playaac(data);
	}

	function g711(kind, data){
		var tab = kind === 1 ? alawtab() : ulawtab();
		var pcm = new Float32Array(data.length);
		for (var i = 0; i < data.length; i++) pcm[i] = tab[data[i]];
		return pcm;
	}

	function ulawtab(){
		if (ULAW) return ULAW;
		ULAW = new Float32Array(256);
		var i, u, sign, exp, man, s;
		for (i = 0; i < 256; i++) {
			u = (~i) & 255;
			sign = u & 0x80;
			exp = (u >> 4) & 7;
			man = u & 0x0F;
			s = ((man << 3) + 0x84) << exp;
			s -= 0x84;
			if (sign) s = -s;
			ULAW[i] = s / 32768;
		}
		return ULAW;
	}

	function alawtab(){
		if (alawtab.tab) return alawtab.tab;
		var tab = new Float32Array(256);
		var i, a, t, seg;
		for (i = 0; i < 256; i++) {
			a = i ^ 0x55;
			t = (a & 0x0F) << 4;
			seg = (a & 0x70) >> 4;
			if (seg === 0) t += 8;
			else { t += 0x108; if (seg > 1) t <<= seg - 1; }
			if ((a & 0x80) === 0) t = -t;
			tab[i] = t / 32768;
		}
		alawtab.tab = tab;
		return tab;
	}

	function playpcm(pcm, rate){
		if (!actx || !pcm || !pcm.length) return;
		var buf = actx.createBuffer(1, pcm.length, rate);
		buf.getChannelData(0).set(pcm);
		sched(buf);
	}

	function sched(buf){
		var src = actx.createBufferSource();
		src.buffer = buf;
		src.connect(actx.destination);
		var t = actx.currentTime;
		if (nextAt < t + 0.02) nextAt = t + 0.06;
		src.start(nextAt);
		nextAt += buf.duration;
	}

	function aaccfg(rate, ch, asc){
		if (!actx || typeof AudioDecoder === "undefined" || !asc || !asc.length) return;
		if (!adec || adec.state === "closed") {
			adec = new AudioDecoder({
				output: function(audio){
					try {
						var frames = audio.numberOfFrames;
						var nch = audio.numberOfChannels || 1;
						var out = actx.createBuffer(nch, frames, audio.sampleRate);
						var c, plane;
						for (c = 0; c < nch; c++) {
							plane = new Float32Array(frames);
							audio.copyTo(plane, { planeIndex: c, format: "f32-planar" });
							out.getChannelData(c).set(plane);
						}
						sched(out);
					} catch (e) {}
					try { audio.close(); } catch (e2) {}
				},
				error: function(e){ say(e.message, true); }
			});
		}
		try {
			adec.configure({
				codec: "mp4a.40.2",
				sampleRate: rate,
				numberOfChannels: ch,
				description: asc
			});
			aacok = true;
			atsus = 0;
		} catch (e3) {
			aacok = false;
			say(e3.message, true);
		}
	}

	function playaac(data){
		if (!aacok || !adec || adec.state !== "configured" || !data.length) return;
		try {
			adec.decode(new EncodedAudioChunk({ type: "key", timestamp: atsus, data: data }));
			atsus += 21333;
		} catch (e) { say(e.message, true); }
	}

	function closeaud(){
		nextAt = 0;
		atsus = 0;
		aacok = false;
		if (adec) {
			try { adec.close(); } catch (e) {}
			adec = null;
		}
		if (actx) {
			try { actx.close(); } catch (e2) {}
			actx = null;
		}
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
