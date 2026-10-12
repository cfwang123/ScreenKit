(function(){
	var ac, master, waves, nbuf;
	var held = {};
	var sources = [];
	var loop = null;
	var demot = [];
	var bound = false;
	var INST = [
		{ id: "sq", name: "方波", wave: "square", a: 0.008, d: 0.09, s: 0.55, r: 0.08 },
		{ id: "p25", name: "窄脉冲", wave: "pulse", duty: 0.25, a: 0.008, d: 0.07, s: 0.45, r: 0.07 },
		{ id: "p12", name: "细脉冲", wave: "pulse", duty: 0.125, a: 0.005, d: 0.06, s: 0.35, r: 0.06 },
		{ id: "tri", name: "三角波", wave: "triangle", a: 0.01, d: 0.04, s: 0.8, r: 0.06 },
		{ id: "saw", name: "锯齿", wave: "sawtooth", a: 0.008, d: 0.1, s: 0.45, r: 0.1 },
		{ id: "org", name: "风琴", wave: "square", detune: 14, a: 0.03, d: 0.12, s: 0.7, r: 0.18 },
		{ id: "bell", name: "铃", wave: "triangle", harm: 2.76, a: 0.004, d: 0.2, s: 0.02, r: 0.4 },
		{ id: "bass", name: "贝斯", wave: "triangle", oct: -1, a: 0.004, d: 0.14, s: 0.35, r: 0.08 },
		{ id: "flute", name: "笛子", wave: "square", vib: 6, vibhz: 5.5, a: 0.05, d: 0.08, s: 0.55, r: 0.12 },
		{ id: "drum", name: "噪声鼓", wave: "noise", a: 0.002, d: 0.1, s: 0.001, r: 0.04 }
	];
	var SFX = [
		{ id: "boom", name: "爆炸" },
		{ id: "alarm", name: "警报", loop: "alarm" },
		{ id: "fire", name: "火警", loop: "fire" },
		{ id: "buzz", name: "蜂鸣", loop: "buzz" },
		{ id: "phone", name: "电话", loop: "phone" },
		{ id: "laser", name: "激光" },
		{ id: "jump", name: "跳跃" },
		{ id: "coin", name: "金币" },
		{ id: "up", name: "升级" },
		{ id: "hit", name: "受伤" },
		{ id: "shot", name: "射击" },
		{ id: "pick", name: "拾取" },
		{ id: "fail", name: "失败" },
		{ id: "heart", name: "心跳" }
	];
	var NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
	var CODE = {
		KeyA: 60, KeyW: 61, KeyS: 62, KeyE: 63, KeyD: 64,
		KeyF: 65, KeyT: 66, KeyG: 67, KeyY: 68, KeyH: 69,
		KeyU: 70, KeyJ: 71, KeyK: 72, KeyO: 73, KeyL: 74,
		KeyP: 75, Semicolon: 76, Quote: 77, BracketRight: 78,
		Backslash: 79, KeyZ: 80, KeyX: 81, KeyC: 82, KeyV: 83
	};
	var LABEL = {
		60: "A", 61: "W", 62: "S", 63: "E", 64: "D", 65: "F", 66: "T", 67: "G",
		68: "Y", 69: "H", 70: "U", 71: "J", 72: "K", 73: "O", 74: "L", 75: "P",
		76: ";", 77: "'", 78: "]", 79: "\\", 80: "Z", 81: "X", 82: "C", 83: "V"
	};

	SK.regutil("u-bit", {
		title: "8bit合成器",
		go: "停止",
		hideGo: true,
		hideCopy: true,
		hideOut: true,
		live: false,
		body: body,
		open: open
	});

	var show0 = SK.show;
	SK.show = function(name, fromHist){
		if (name !== "u-bit") leave();
		show0(name, fromHist);
	};

	function body(){
		var opts = "";
		var i;
		for (i = 0; i < INST.length; i++)
			opts += '<option value="' + INST[i].id + '">' + INST[i].name + "</option>";
		var sfx = "";
		for (i = 0; i < SFX.length; i++)
			sfx += '<button type="button" class="bit-sfx" data-sfx="' + SFX[i].id + '">' + SFX[i].name + "</button>";
		return '<div class="bit">'
			+ '<div class="row">'
			+ '<label>乐器<select id="bit-inst">' + opts + "</select></label>"
			+ '<label>音量<input id="bit-vol" type="range" min="0" max="100" value="70"></label>'
			+ '<button type="button" id="bit-demo">试听</button>'
			+ '<button type="button" id="bit-stop">停止</button>'
			+ "</div>"
			+ '<div class="bitkeys" id="bit-keys">' + keys() + "</div>"
			+ '<p class="kicker">音效</p>'
			+ '<div class="ops" id="bit-sfx">' + sfx + "</div>"
			+ '<p class="bitmsg" id="bit-msg">点琴键，或用键盘 A–V 那一排。警报、火警、蜂鸣、电话再点一次就停。</p>'
			+ "</div>";
	}

	function keys(){
		var whites = "";
		var blacks = "";
		var wi = -1;
		var i, midi, name, lab;
		for (i = 0; i < 24; i++) {
			midi = 60 + i;
			name = NAMES[i % 12];
			lab = LABEL[midi] || "";
			if (name.indexOf("#") < 0) {
				wi++;
				whites += '<button type="button" class="w" data-midi="' + midi + '">'
					+ name + "<small>" + lab + "</small></button>";
			}
			else {
				blacks += '<button type="button" class="b" data-midi="' + midi + '" data-after="' + wi + '">'
					+ "<small>" + lab + "</small></button>";
			}
		}
		return '<div class="whites">' + whites + "</div>" + blacks;
	}

	function open(){
		var root = document.getElementById("bit-keys");
		var sfx = document.getElementById("bit-sfx");
		if (!root || !sfx) return;
		root.onpointerdown = onpointerdown;
		root.onpointerup = onpointerup;
		root.onpointercancel = onpointerup;
		sfx.onclick = onsfx;
		document.getElementById("bit-demo").onclick = demo;
		document.getElementById("bit-stop").onclick = stopaudio;
		document.getElementById("bit-vol").oninput = applyvol;
		bind();
		placeblack();
		requestAnimationFrame(placeblack);
	}

	function bind(){
		if (bound) return;
		bound = true;
		window.addEventListener("keydown", ondown);
		window.addEventListener("keyup", onup);
		window.addEventListener("blur", releaseall);
		window.addEventListener("resize", placeblack);
	}

	function unbind(){
		if (!bound) return;
		bound = false;
		window.removeEventListener("keydown", ondown);
		window.removeEventListener("keyup", onup);
		window.removeEventListener("blur", releaseall);
		window.removeEventListener("resize", placeblack);
	}

	function leave(){
		stopaudio();
		unbind();
	}

	function placeblack(){
		var whites = document.querySelectorAll("#bit-keys .w");
		var blacks = document.querySelectorAll("#bit-keys .b");
		var i, w;
		if (!whites.length) return;
		for (i = 0; i < blacks.length; i++) {
			w = whites[Number(blacks[i].getAttribute("data-after"))];
			if (!w || w.offsetWidth < 2) continue;
			blacks[i].style.left = (w.offsetLeft + w.offsetWidth - 14) + "px";
		}
	}

	function inst(){
		var el = document.getElementById("bit-inst");
		var id = el ? el.value : "sq";
		var i;
		for (i = 0; i < INST.length; i++) if (INST[i].id === id) return INST[i];
		return INST[0];
	}

	function sfxof(id){
		var i;
		for (i = 0; i < SFX.length; i++) if (SFX[i].id === id) return SFX[i];
		return null;
	}

	function volnow(){
		var el = document.getElementById("bit-vol");
		var v = el ? Number(el.value) : 70;
		if (!(v >= 0)) v = 70;
		return v / 100;
	}

	function ensure(){
		var Ctor = window.AudioContext || window.webkitAudioContext;
		if (!Ctor) {
			say("这个浏览器不能发声");
			return null;
		}
		if (!ac) {
			ac = new Ctor();
			master = ac.createGain();
			master.gain.value = volnow();
			master.connect(ac.destination);
			waves = {};
		}
		if (ac.state === "suspended") ac.resume();
		return ac;
	}

	function applyvol(){
		if (!master || !ac) return;
		master.gain.setTargetAtTime(volnow(), ac.currentTime, 0.02);
	}

	function say(text){
		var el = document.getElementById("bit-msg");
		if (el) el.textContent = text;
	}

	function freq(midi){
		return 440 * Math.pow(2, (midi - 69) / 12);
	}

	function pulsewave(c, duty){
		var key = String(duty);
		if (waves[key]) return waves[key];
		var n = 48;
		var real = new Float32Array(n);
		var imag = new Float32Array(n);
		var i;
		for (i = 1; i < n; i++)
			imag[i] = (2 / (i * Math.PI)) * Math.sin(i * Math.PI * duty);
		waves[key] = c.createPeriodicWave(real, imag);
		return waves[key];
	}

	function noisebuf(c){
		if (nbuf && nbuf.sampleRate === c.sampleRate) return nbuf;
		var len = c.sampleRate;
		nbuf = c.createBuffer(1, len, c.sampleRate);
		var data = nbuf.getChannelData(0);
		var hold = 8;
		var v = 0;
		var i;
		for (i = 0; i < len; i++) {
			if (i % hold === 0) v = Math.random() * 2 - 1;
			data[i] = v;
		}
		return nbuf;
	}

	function remember(node){
		sources.push(node);
		node.onended = function(){
			var i = sources.indexOf(node);
			if (i >= 0) sources.splice(i, 1);
		};
	}

	function envgain(c, spec, t0, peak){
		var g = c.createGain();
		var a = Math.max(spec.a, 0.004);
		var d = Math.max(spec.d, 0.004);
		var s = Math.max(spec.s * peak, 0.0001);
		g.gain.setValueAtTime(0.0001, t0);
		g.gain.exponentialRampToValueAtTime(Math.max(peak, 0.0001), t0 + a);
		g.gain.exponentialRampToValueAtTime(s, t0 + a + d);
		return g;
	}

	function noteon(midi){
		if (held[midi]) return;
		var c = ensure();
		if (!c) return;
		var spec = inst();
		var t0 = c.currentTime;
		var f = freq(midi);
		if (spec.oct) f *= Math.pow(2, spec.oct);
		var g = envgain(c, spec, t0, spec.wave === "noise" ? 0.55 : 0.22);
		var oscs = [];
		g.connect(master);
		if (spec.wave === "noise") {
			var src = c.createBufferSource();
			src.buffer = noisebuf(c);
			src.loop = true;
			var flt = c.createBiquadFilter();
			flt.type = "lowpass";
			flt.frequency.value = Math.max(180, f * 6);
			src.connect(flt);
			flt.connect(g);
			src.start(t0);
			oscs.push(src);
		}
		else {
			var o = makeosc(c, spec, f);
			if (spec.vib) {
				var lfo = c.createOscillator();
				var depth = c.createGain();
				lfo.frequency.value = spec.vibhz || 5;
				depth.gain.value = spec.vib;
				lfo.connect(depth);
				depth.connect(o.frequency);
				lfo.start(t0);
				oscs.push(lfo);
			}
			o.connect(g);
			o.start(t0);
			oscs.push(o);
			if (spec.detune) {
				var o2 = makeosc(c, spec, f);
				o2.detune.value = spec.detune;
				var o3 = makeosc(c, spec, f);
				o3.detune.value = -spec.detune;
				o2.connect(g);
				o3.connect(g);
				o2.start(t0);
				o3.start(t0);
				oscs.push(o2, o3);
			}
			if (spec.harm) {
				var oh = c.createOscillator();
				var hg = c.createGain();
				oh.type = "square";
				oh.frequency.value = f * spec.harm;
				hg.gain.value = 0.28;
				oh.connect(hg);
				hg.connect(g);
				oh.start(t0);
				oscs.push(oh);
			}
		}
		held[midi] = { g: g, oscs: oscs, rel: Math.max(spec.r, 0.03) };
		paintkey(midi, true);
	}

	function makeosc(c, spec, f){
		var o = c.createOscillator();
		if (spec.wave === "pulse") o.setPeriodicWave(pulsewave(c, spec.duty || 0.25));
		else o.type = spec.wave;
		o.frequency.value = f;
		return o;
	}

	function noteoff(midi){
		var v = held[midi];
		if (!v || !ac) {
			delete held[midi];
			paintkey(midi, false);
			return;
		}
		delete held[midi];
		paintkey(midi, false);
		var t = ac.currentTime;
		var now = v.g.gain.value;
		if (now < 0.0001) now = 0.0001;
		v.g.gain.cancelScheduledValues(t);
		v.g.gain.setValueAtTime(now, t);
		v.g.gain.exponentialRampToValueAtTime(0.0001, t + v.rel);
		var i;
		for (i = 0; i < v.oscs.length; i++) {
			try { v.oscs[i].stop(t + v.rel + 0.03); } catch (e) {}
		}
	}

	function releaseall(){
		var k;
		for (k in held) noteoff(Number(k));
	}

	function paintkey(midi, on){
		var btn = document.querySelector('#bit-keys button[data-midi="' + midi + '"]');
		if (!btn) return;
		if (on) btn.classList.add("on");
		else btn.classList.remove("on");
	}

	function cleardemo(){
		var i;
		for (i = 0; i < demot.length; i++) clearTimeout(demot[i]);
		demot = [];
	}

	function stopaudio(){
		cleardemo();
		stoploop();
		releaseall();
		var i;
		for (i = 0; i < sources.length; i++) {
			try { sources[i].stop(); } catch (e) {}
		}
		sources = [];
	}

	function stoploop(){
		if (!loop) return;
		if (loop.timer) clearInterval(loop.timer);
		try { loop.o.stop(); } catch (e) {}
		if (loop.o2) {
			try { loop.o2.stop(); } catch (e2) {}
		}
		loop = null;
		paintloops();
	}

	function paintloops(){
		var nodes = document.querySelectorAll(".bit-sfx");
		var i, id;
		for (i = 0; i < nodes.length; i++) {
			id = nodes[i].getAttribute("data-sfx");
			if (loop && loop.kind === id) nodes[i].classList.add("primary");
			else nodes[i].classList.remove("primary");
		}
	}

	function startloop(kind){
		var c = ensure();
		if (!c) return;
		stoploop();
		var g = c.createGain();
		g.gain.value = 0.2;
		g.connect(master);
		var o = c.createOscillator();
		o.type = "square";
		o.connect(g);
		var o2 = null;
		var timer = null;
		if (kind === "alarm") o.frequency.value = 520;
		else if (kind === "fire") {
			o.frequency.value = 1760;
			g.gain.value = 0;
		}
		else if (kind === "buzz") o.frequency.value = 440;
		else if (kind === "phone") {
			o.frequency.value = 440;
			o2 = c.createOscillator();
			o2.type = "square";
			o2.frequency.value = 480;
			o2.connect(g);
			g.gain.value = 0.14;
		}
		o.start();
		if (o2) o2.start();
		if (kind === "alarm") {
			var hi = false;
			timer = setInterval(function(){
				hi = !hi;
				try { o.frequency.setValueAtTime(hi ? 780 : 520, c.currentTime); } catch (e) {}
			}, 260);
		}
		else if (kind === "fire") {
			var on = false;
			timer = setInterval(function(){
				on = !on;
				try { g.gain.setValueAtTime(on ? 0.18 : 0, c.currentTime); } catch (e) {}
			}, 80);
		}
		else if (kind === "phone") {
			var ring = true;
			timer = setInterval(function(){
				ring = !ring;
				try { g.gain.setValueAtTime(ring ? 0.14 : 0, c.currentTime); } catch (e) {}
			}, 420);
		}
		loop = { kind: kind, o: o, o2: o2, g: g, timer: timer };
		paintloops();
	}

	function tone(type, f0, f1, t0, dur, level){
		var o = ac.createOscillator();
		o.type = type;
		o.frequency.setValueAtTime(Math.max(f0, 1), t0);
		if (f1 != null) o.frequency.exponentialRampToValueAtTime(Math.max(f1, 1), t0 + dur);
		var g = ac.createGain();
		var pk = Math.max(level, 0.0001);
		var atk = Math.min(0.02, dur * 0.3);
		if (atk < 0.004) atk = 0.004;
		if (atk > dur * 0.8) atk = dur * 0.3;
		g.gain.setValueAtTime(0.0001, t0);
		g.gain.exponentialRampToValueAtTime(pk, t0 + atk);
		g.gain.exponentialRampToValueAtTime(0.0001, t0 + dur);
		o.connect(g);
		g.connect(master);
		o.start(t0);
		o.stop(t0 + dur + 0.03);
		remember(o);
	}

	function burst(t0, dur, fromHz, toHz, level){
		var src = ac.createBufferSource();
		src.buffer = noisebuf(ac);
		src.loop = true;
		var flt = ac.createBiquadFilter();
		flt.type = "lowpass";
		flt.frequency.setValueAtTime(Math.max(fromHz, 1), t0);
		flt.frequency.exponentialRampToValueAtTime(Math.max(toHz, 1), t0 + dur);
		var g = ac.createGain();
		g.gain.setValueAtTime(0.0001, t0);
		g.gain.exponentialRampToValueAtTime(Math.max(level, 0.0001), t0 + 0.015);
		g.gain.exponentialRampToValueAtTime(0.0001, t0 + dur);
		src.connect(flt);
		flt.connect(g);
		g.connect(master);
		src.start(t0);
		src.stop(t0 + dur + 0.02);
		remember(src);
	}

	function sfxboom(){
		var c = ensure();
		if (!c) return;
		var t0 = c.currentTime;
		tone("square", 180, 40, t0, 0.48, 0.28);
		burst(t0, 0.62, 2200, 90, 0.7);
	}

	function sfxlaser(){
		var c = ensure();
		if (!c) return;
		tone("square", 1400, 110, c.currentTime, 0.22, 0.22);
	}

	function sfxjump(){
		var c = ensure();
		if (!c) return;
		tone("square", 280, 920, c.currentTime, 0.16, 0.22);
	}

	function sfxcoin(){
		var c = ensure();
		if (!c) return;
		var t0 = c.currentTime;
		tone("square", 988, null, t0, 0.08, 0.2);
		tone("square", 1319, null, t0 + 0.08, 0.18, 0.2);
	}

	function sfxup(){
		var c = ensure();
		if (!c) return;
		var notes = [523, 659, 784, 1046];
		var t0 = c.currentTime;
		var i;
		for (i = 0; i < notes.length; i++)
			tone("square", notes[i], null, t0 + i * 0.09, 0.1, 0.2);
	}

	function sfxhit(){
		var c = ensure();
		if (!c) return;
		var t0 = c.currentTime;
		tone("square", 220, 55, t0, 0.14, 0.26);
		burst(t0, 0.12, 1800, 300, 0.45);
	}

	function sfxshot(){
		var c = ensure();
		if (!c) return;
		var t0 = c.currentTime;
		tone("square", 880, 220, t0, 0.07, 0.2);
		burst(t0, 0.06, 4000, 800, 0.35);
	}

	function sfxpick(){
		var c = ensure();
		if (!c) return;
		var notes = [784, 988, 1175];
		var t0 = c.currentTime;
		var i;
		for (i = 0; i < notes.length; i++)
			tone("square", notes[i], null, t0 + i * 0.06, 0.07, 0.18);
	}

	function sfxfail(){
		var c = ensure();
		if (!c) return;
		var notes = [440, 349, 294, 262];
		var t0 = c.currentTime;
		var i;
		for (i = 0; i < notes.length; i++)
			tone("square", notes[i], null, t0 + i * 0.16, 0.17, 0.2);
	}

	function sfxheart(){
		var c = ensure();
		if (!c) return;
		var t0 = c.currentTime;
		tone("square", 90, 48, t0, 0.12, 0.3);
		tone("square", 70, 40, t0 + 0.2, 0.14, 0.26);
	}

	var PLAY = {
		boom: sfxboom, laser: sfxlaser, jump: sfxjump, coin: sfxcoin, up: sfxup,
		hit: sfxhit, shot: sfxshot, pick: sfxpick, fail: sfxfail, heart: sfxheart
	};

	function onsfx(ev){
		var btn = ev.target.closest(".bit-sfx");
		if (!btn) return;
		var spec = sfxof(btn.getAttribute("data-sfx"));
		if (!spec) return;
		if (spec.loop) {
			if (loop && loop.kind === spec.loop) stoploop();
			else startloop(spec.loop);
			return;
		}
		var fn = PLAY[spec.id];
		if (fn) fn();
	}

	function demo(){
		stopaudio();
		var seq = [60, 64, 67, 72, 76, 79, 84];
		function step(n){
			if (n > 0) noteoff(seq[n - 1]);
			if (n >= seq.length) return;
			noteon(seq[n]);
			demot.push(setTimeout(function(){ step(n + 1); }, 140));
		}
		step(0);
	}

	function typing(el){
		if (!el) return false;
		var tag = el.tagName;
		return tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT";
	}

	function ondown(ev){
		if (SK.curtool !== "u-bit" || ev.repeat || typing(ev.target)) return;
		var midi = CODE[ev.code];
		if (!midi) return;
		ev.preventDefault();
		noteon(midi);
	}

	function onup(ev){
		var midi = CODE[ev.code];
		if (!midi) return;
		noteoff(midi);
	}

	function onpointerdown(ev){
		var btn = ev.target.closest("button[data-midi]");
		if (!btn) return;
		ev.preventDefault();
		try { btn.setPointerCapture(ev.pointerId); } catch (e) {}
		noteon(Number(btn.getAttribute("data-midi")));
	}

	function onpointerup(ev){
		var btn = ev.target.closest("button[data-midi]");
		if (!btn) return;
		noteoff(Number(btn.getAttribute("data-midi")));
	}
})();
