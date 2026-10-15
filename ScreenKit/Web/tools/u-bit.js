(function(){
	var ac, master, waves, nbuf;
	var held = {};
	var sources = [];
	var blips = [];
	var loop = null;
	var demot = [];
	var bound = false;
	var shift = 0;		// 按住 Shift / Alt 时的八度偏移（+12 / -12）
	var pressed = {};	// 键码 -> 正在响的音，松开时按这个停
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
			+ '<label>音量<input id="bit-vol" type="range" min="0" max="100" value="100"></label>'
			+ '<button type="button" id="bit-demo">试听</button>'
			+ '<button type="button" id="bit-stop">停止</button>'
			+ "</div>"
			+ '<div class="bitkeys" id="bit-keys">' + keys() + "</div>"
			+ '<p class="kicker">编排</p>'
			+ '<div class="row">'
			+ '<label>速度<input id="bit-bpm" type="number" min="40" max="240" value="120"></label>'
			+ '<label>记谱<select id="bit-oct"><option value="-12">低八度</option><option value="0" selected>原调</option><option value="12">高八度</option></select></label>'
			+ '<button type="button" id="bit-play">播放</button>'
			+ '<button type="button" id="bit-songstop">停止</button>'
			+ '<label>示例<select id="bit-ex">' + exopts() + "</select></label>"
			+ '<button type="button" id="bit-clear">清空</button>'
			+ "</div>"
			+ '<div class="bitgrid" id="bit-grid"></div>'
			+ '<p class="bitmsg">四个通道，每路自己选乐器，选择和曲子一起记在这台浏览器里。点格子再点琴键写入，写完到下一格；再点当前格就清掉。噪声鼓点一下在 1–6 档之间换。速度是每分钟多少拍，一格是十六分音符。示例是六段 FC 风格的一小节循环。</p>'
			+ '<p class="kicker">音效</p>'
			+ '<div class="ops" id="bit-sfx">' + sfx + "</div>"
			+ '<p class="bitmsg" id="bit-msg">点琴键，或用键盘 A–V 那一排；按住 Shift 升 8 度、Alt 降 8 度（琴键跟着换一组）。点一个音效会停掉其它正在响的。警报、火警、蜂鸣、电话再点一次就停。</p>'
			+ "</div>";
	}

	// 三个八度（36 个半音）：上面两个八度是 A–V 那一排的键位，整组跟着 Shift/Alt 移调
	function keys(){
		var whites = "";
		var blacks = "";
		var wi = -1;
		var lo = 48 + shift;
		var i, midi, name, lab;
		for (i = 0; i < 36; i++) {
			midi = lo + i;
			name = NAMES[midi % 12];
			lab = LABEL[midi - shift] || "";
			if (name.indexOf("#") < 0) {
				wi++;
				whites += '<button type="button" class="w" data-midi="' + midi + '">'
					+ (name === "C" ? name + (Math.floor(midi / 12) - 1) : name)
					+ "<small>" + lab + "</small></button>";
			}
			else {
				blacks += '<button type="button" class="b" data-midi="' + midi + '" data-after="' + wi + '">'
					+ "<small>" + lab + "</small></button>";
			}
		}
		return '<div class="whites">' + whites + "</div>" + blacks;
	}

	function open(){
		var root = F.id("bit-keys");
		var sfx = F.id("bit-sfx");
		if (!root || !sfx) return;
		root.onpointerdown = onpointerdown;
		root.onpointerup = onpointerup;
		root.onpointercancel = onpointerup;
		sfx.onclick = onsfx;
		F.id("bit-demo").onclick = demo;
		F.id("bit-stop").onclick = stopaudio;
		F.id("bit-play").onclick = songplay;
		F.id("bit-songstop").onclick = function(){ songstop(); };
		F.id("bit-ex").onchange = onex;
		F.id("bit-clear").onclick = clearsong;
		F.id("bit-bpm").onchange = function(){ readbpm(); savesong(); };
		F.id("bit-grid").onclick = ongrid;
		F.id("bit-grid").onchange = onmute;
		loadsong();
		paintgrid();
		F.id("bit-vol").oninput = applyvol;
		bind();
		layoutkeys();
		F.raf(layoutkeys);
	}

	function bind(){
		if (bound) return;
		bound = true;
		F.on(window, "keydown", ondown);
		F.on(window, "keyup", onup);
		F.on(window, "blur", releaseall);
		F.on(window, "resize", layoutkeys);
	}

	function unbind(){
		if (!bound) return;
		bound = false;
		window.removeEventListener("keydown", ondown);
		window.removeEventListener("keyup", onup);
		window.removeEventListener("blur", releaseall);
		window.removeEventListener("resize", layoutkeys);
	}

	function leave(){
		stopaudio();
		unbind();
	}

	function placeblack(){
		var whites = F.qa("#bit-keys .w");
		var blacks = F.qa("#bit-keys .b");
		var i, w, bw;
		if (!whites.length) return;
		for (i = 0; i < blacks.length; i++) {
			w = whites[Number(F.attr(blacks[i], "data-after"))];
			if (!w || w.offsetWidth < 2) continue;
			bw = Math.max(16, Math.round(w.offsetWidth * 0.6));
			blacks[i].style.width = bw + "px";
			blacks[i].style.height = Math.round(w.offsetHeight * 0.62) + "px";
			blacks[i].style.left = (w.offsetLeft + w.offsetWidth - bw / 2) + "px";
			blacks[i].style.top = w.offsetTop + "px";
		}
	}

	// 白键能到 40px 宽就一行放下 21 个，不然一行一个八度分三行（21×40 + 20 = 860）
	function layoutkeys(){
		var root = F.id("bit-keys");
		if (!root) return;
		root.className = "bitkeys " + (root.clientWidth >= 21 * 40 + 20 ? "onerow" : "rows3");
		placeblack();
	}

	// 换一组八度：重画琴键，再把还在响的音点亮
	function toshift(next){
		if (next === shift) return;
		shift = next;
		paintkeys();
	}

	function paintkeys(){
		var root = F.id("bit-keys");
		var k;
		if (!root) return;
		root.innerHTML = keys();
		placeblack();
		for (k in held) paintkey(Number(k), true);
	}

	function syncmods(ev){
		toshift((ev.shiftKey ? 12 : 0) - (ev.altKey ? 12 : 0));
	}

	function inst(){
		var el = F.id("bit-inst");
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
		var el = F.id("bit-vol");
		var v = el ? Number(el.value) : 100;
		if (!(v >= 0)) v = 100;
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
			F.sink(ac);
			master = ac.createGain();
			master.gain.value = volnow();
			master.connect(ac.destination);
			waves = {};
		}
		if (ac.state === "suspended") ac.resume();
		F.sink(ac);
		return ac;
	}

	function applyvol(){
		if (!master || !ac) return;
		master.gain.setTargetAtTime(volnow(), ac.currentTime, 0.02);
	}

	function say(text){
		var el = F.id("bit-msg");
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
		var btn = F.q('#bit-keys button[data-midi="' + midi + '"]');
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
		songstop();
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
		var nodes = F.qa(".bit-sfx");
		var i, id;
		for (i = 0; i < nodes.length; i++) {
			id = F.attr(nodes[i], "data-sfx");
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

	function sfxbeep(){
		var c = ensure();
		if (!c) return;
		var t0 = c.currentTime;
		var o = c.createOscillator();
		o.type = "square";
		o.frequency.value = 440;
		var g = c.createGain();
		g.gain.setValueAtTime(0.0001, t0);
		g.gain.exponentialRampToValueAtTime(0.22, t0 + 0.005);
		g.gain.setValueAtTime(0.22, t0 + 0.19);
		g.gain.exponentialRampToValueAtTime(0.0001, t0 + 0.2);
		o.connect(g);
		g.connect(master);
		o.start(t0);
		o.stop(t0 + 0.23);
		remember(o);
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

	function stoponeshots(){
		var i;
		for (i = 0; i < sources.length; i++) {
			try { sources[i].stop(); } catch (e) {}
		}
		sources = [];
	}

	function onsfx(ev){
		var btn = ev.target.closest(".bit-sfx");
		if (!btn) return;
		var spec = sfxof(F.attr(btn, "data-sfx"));
		if (!spec) return;
		if (spec.loop) {
			if (loop && loop.kind === spec.loop) {
				stoploop();
				return;
			}
			stoponeshots();
			startloop(spec.loop);
			return;
		}
		stoploop();
		stoponeshots();
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
		var base = CODE[ev.code];
		var midi;
		if (ev.key === "Shift" || ev.key === "Alt") {
			syncmods(ev);
			return;
		}
		if (!base) return;
		F.pd(ev);
		syncmods(ev);
		midi = base + shift;
		if (writenote(midi)) return;
		pressed[ev.code] = midi;
		noteon(midi);
	}

	function onup(ev){
		var base = CODE[ev.code];
		var midi;
		if (ev.key === "Shift" || ev.key === "Alt") {
			syncmods(ev);
			return;
		}
		if (!base) return;
		midi = pressed[ev.code];
		delete pressed[ev.code];
		if (midi) noteoff(midi);
	}

	function onpointerdown(ev){
		var btn = ev.target.closest("button[data-midi]");
		if (!btn) return;
		F.pd(ev);
		try { btn.setPointerCapture(ev.pointerId); } catch (e) {}
		var midi = Number(F.attr(btn, "data-midi"));
		if (writenote(midi)) return;
		noteon(midi);
	}

	var STEPS = 16;
	var DEFINST = ["sq", "p25", "tri", "drum"];
	var chinst = DEFINST.slice();
	var EXS = [
		{
			id: "march", name: "进行曲", bpm: 120, inst: ["sq", "p25", "tri", "drum"],
			song: [
				[60, 0, 64, 0, 67, 0, 64, 0, 69, 0, 67, 0, 64, 0, 60, 0],
				[67, 0, 72, 0, 76, 0, 72, 0, 74, 0, 72, 0, 67, 0, 64, 0],
				[36, 0, 0, 0, 43, 0, 0, 0, 41, 0, 0, 0, 36, 0, 0, 0],
				[2, 0, 5, 0, 2, 0, 5, 0, 2, 0, 5, 0, 2, 0, 6, 0]
			]
		},
		{
			id: "jump", name: "跳跃", bpm: 150, inst: ["sq", "p12", "bass", "drum"],
			song: [
				[72, 0, 76, 0, 79, 0, 84, 0, 79, 0, 76, 0, 72, 0, 67, 0],
				[60, 0, 64, 0, 67, 0, 72, 0, 67, 0, 64, 0, 60, 0, 55, 0],
				[48, 0, 0, 48, 43, 0, 0, 43, 41, 0, 0, 41, 36, 0, 0, 36],
				[4, 0, 2, 0, 5, 0, 2, 0, 4, 0, 2, 0, 6, 0, 2, 5]
			]
		},
		{
			id: "water", name: "水下", bpm: 96, inst: ["flute", "tri", "bass", "drum"],
			song: [
				[67, 0, 0, 71, 72, 0, 0, 67, 64, 0, 0, 60, 62, 0, 64, 0],
				[60, 0, 64, 0, 67, 0, 64, 0, 60, 0, 55, 0, 57, 0, 60, 0],
				[43, 0, 0, 0, 48, 0, 0, 0, 41, 0, 0, 0, 36, 0, 0, 0],
				[1, 0, 0, 3, 1, 0, 0, 3, 1, 0, 0, 3, 1, 0, 2, 4]
			]
		},
		{
			id: "cave", name: "地牢", bpm: 88, inst: ["p25", "org", "bass", "drum"],
			song: [
				[60, 0, 63, 0, 67, 0, 63, 0, 58, 0, 63, 0, 55, 0, 51, 0],
				[55, 0, 58, 0, 63, 0, 58, 0, 51, 0, 58, 0, 48, 0, 46, 0],
				[36, 0, 0, 0, 39, 0, 0, 0, 34, 0, 0, 0, 36, 0, 0, 43],
				[3, 0, 0, 0, 5, 0, 2, 0, 3, 0, 0, 0, 6, 0, 0, 2]
			]
		},
		{
			id: "fight", name: "战斗", bpm: 168, inst: ["sq", "saw", "tri", "drum"],
			song: [
				[64, 64, 67, 0, 72, 0, 70, 0, 67, 67, 64, 0, 60, 0, 64, 0],
				[52, 0, 55, 0, 59, 0, 55, 0, 52, 0, 48, 0, 47, 0, 48, 0],
				[40, 0, 40, 0, 43, 0, 43, 0, 36, 0, 36, 0, 35, 0, 36, 0],
				[5, 2, 4, 2, 5, 2, 6, 2, 5, 2, 4, 2, 6, 5, 2, 4]
			]
		},
		{
			id: "clear", name: "通关", bpm: 132, inst: ["bell", "sq", "tri", "drum"],
			song: [
				[72, 0, 76, 0, 79, 0, 84, 84, 0, 0, 84, 0, 79, 0, 84, 0],
				[60, 0, 64, 0, 67, 0, 72, 72, 0, 0, 72, 0, 67, 0, 72, 0],
				[48, 0, 0, 0, 43, 0, 0, 0, 48, 0, 0, 0, 36, 0, 48, 0],
				[4, 0, 2, 0, 5, 0, 6, 0, 0, 0, 4, 0, 2, 0, 6, 0]
			]
		}
	];
	var song = blanks();
	var en = [true, true, true, true];
	var bpm = 120;
	var sel = null;
	var playOn = false;
	var playTimer = null;
	var songgen = 0;
	var nextTime = 0;
	var cursor = 0;
	var queued = [];
	var heard = -1;

	function blanks(){
		var a = [];
		var ch, s, row;
		for (ch = 0; ch < 4; ch++) {
			row = [];
			for (s = 0; s < STEPS; s++) row.push(0);
			a.push(row);
		}
		return a;
	}

	function notename(midi){
		if (!midi) return "";
		return NAMES[((midi % 12) + 12) % 12] + (Math.floor(midi / 12) - 1);
	}

	function instbyid(id){
		var i;
		for (i = 0; i < INST.length; i++) if (INST[i].id === id) return INST[i];
		return null;
	}

	function chaninst(ch){
		return instbyid(chinst[ch]) || INST[0];
	}

	function isnoise(ch){
		return chaninst(ch).wave === "noise";
	}

	function instopts(ch){
		var html = "";
		var i, it, sel;
		for (i = 0; i < INST.length; i++) {
			it = INST[i];
			sel = it.id === chinst[ch] ? " selected" : "";
			html += '<option value="' + it.id + '"' + sel + ">" + it.name + "</option>";
		}
		return html;
	}

	function celltext(ch, v){
		if (!v) return "";
		if (isnoise(ch)) return String(v);
		return notename(v);
	}

	function paintgrid(){
		var grid = F.id("bit-grid");
		var bpmel = F.id("bit-bpm");
		if (!grid) return;
		if (bpmel) bpmel.value = String(bpm);
		var html = "";
		var ch, s, v, cls;
		for (ch = 0; ch < 4; ch++) {
			html += '<div class="bitrow"><select class="bitinst" data-inst="' + ch + '">'
				+ instopts(ch) + "</select>";
			html += '<label class="check"><input type="checkbox" data-mute="' + ch + '"'
				+ (en[ch] ? " checked" : "") + ">开</label>";
			for (s = 0; s < STEPS; s++) {
				v = song[ch][s];
				cls = "bitcell";
				if (s % 4 === 0) cls += " bar";
				if (v) cls += " on";
				if (sel && sel.ch === ch && sel.step === s) cls += " sel";
				html += '<button type="button" class="' + cls + '" data-ch="' + ch
					+ '" data-step="' + s + '">' + celltext(ch, v) + "</button>";
			}
			html += "</div>";
		}
		grid.innerHTML = html;
		marknow(playOn ? heard : -1);
	}

	function marknow(step){
		var nodes = F.qa("#bit-grid .bitcell");
		var i, on;
		for (i = 0; i < nodes.length; i++) {
			on = step >= 0 && Number(F.attr(nodes[i], "data-step")) === step;
			if (on) nodes[i].classList.add("now");
			else nodes[i].classList.remove("now");
		}
	}

	function paintplaybtn(){
		var b = F.id("bit-play");
		if (!b) return;
		if (playOn) b.classList.add("primary");
		else b.classList.remove("primary");
	}

	function ongrid(ev){
		var cell = ev.target.closest(".bitcell");
		if (!cell) return;
		var ch = Number(F.attr(cell, "data-ch"));
		var step = Number(F.attr(cell, "data-step"));
		if (isnoise(ch)) {
			var v = song[ch][step];
			song[ch][step] = v >= 6 ? 0 : v + 1;
			if (song[ch][step]) preview(ch, song[ch][step]);
			sel = null;
		}
		else if (sel && sel.ch === ch && sel.step === step) {
			song[ch][step] = 0;
			sel = null;
		}
		else sel = { ch: ch, step: step };
		savesong();
		paintgrid();
	}

	function onmute(ev){
		var t = ev.target;
		var inst = t.getAttribute && F.attr(t, "data-inst");
		if (inst != null) {
			setchaninst(Number(inst), t.value);
			return;
		}
		var m = t.getAttribute && F.attr(t, "data-mute");
		if (m == null) return;
		en[Number(m)] = !!t.checked;
		savesong();
	}

	function setchaninst(ch, id){
		var next = instbyid(id);
		if (!next || ch < 0 || ch > 3) return;
		var was = isnoise(ch);
		chinst[ch] = next.id;
		var now = next.wave === "noise";
		var s, v;
		if (was !== now) {
			for (s = 0; s < STEPS; s++) {
				v = song[ch][s];
				if (now) song[ch][s] = v >= 1 && v <= 6 ? v : 0;
				else if (v > 0 && v < 24) song[ch][s] = 0;
			}
			sel = null;
		}
		savesong();
		paintgrid();
	}

	function writenote(midi){
		if (!sel) return false;
		var ch = sel.ch;
		if (isnoise(ch)) return false;
		var oct = F.id("bit-oct");
		var shift = oct ? Number(oct.value) : 0;
		if (shift !== -12 && shift !== 12) shift = 0;
		var n = midi + shift;
		if (n < 24) n = 24;
		if (n > 108) n = 108;
		song[ch][sel.step] = n;
		preview(ch, n);
		sel.step = (sel.step + 1) % STEPS;
		savesong();
		paintgrid();
		return true;
	}

	function clearsong(){
		song = blanks();
		sel = null;
		savesong();
		paintgrid();
	}

	function exopts(){
		var html = '<option value="">示例</option>';
		var i;
		for (i = 0; i < EXS.length; i++)
			html += '<option value="' + EXS[i].id + '">' + EXS[i].name + "</option>";
		return html;
	}

	function exof(id){
		var i;
		for (i = 0; i < EXS.length; i++) if (EXS[i].id === id) return EXS[i];
		return null;
	}

	function onex(){
		var el = F.id("bit-ex");
		var id = el ? el.value : "";
		if (!id) return;
		fillex(id);
		if (el) el.value = "";
	}

	function fillex(id){
		var ex = exof(id) || EXS[0];
		var ch, s;
		song = blanks();
		chinst = ex.inst.slice();
		bpm = ex.bpm;
		for (ch = 0; ch < 4; ch++)
			for (s = 0; s < STEPS; s++) song[ch][s] = ex.song[ch][s];
		sel = null;
		savesong();
		paintgrid();
	}

	function readbpm(){
		var el = F.id("bit-bpm");
		var v = el ? Number(el.value) : bpm;
		if (!(v >= 40)) v = 40;
		if (v > 240) v = 240;
		bpm = Math.round(v);
		if (el) el.value = String(bpm);
		return bpm;
	}

	function stepdur(){
		return 60 / readbpm() / 4;
	}

	function savesong(){
		try {
			F.lset("sk-bit-song", F.json({
				bpm: bpm, en: en, song: song, inst: chinst
			}));
		}
		catch (e) {}
	}

	function loadsong(){
		var raw, o, ch, s, v;
		try { raw = F.lget("sk-bit-song"); }
		catch (e) { return; }
		if (!raw) return;
		try { o = F.parse(raw); }
		catch (e2) { return; }
		if (!o || !o.song || o.song.length !== 4) return;
		for (ch = 0; ch < 4; ch++) {
			if (!o.song[ch] || o.song[ch].length !== STEPS) return;
			for (s = 0; s < STEPS; s++) {
				v = Number(o.song[ch][s]);
				if (!(v >= 0) || v > 127) v = 0;
				song[ch][s] = v;
			}
			if (o.en && o.en.length === 4) en[ch] = !!o.en[ch];
		}
		if (o.bpm >= 40 && o.bpm <= 240) bpm = Math.round(o.bpm);
		if (o.inst && o.inst.length === 4) {
			for (ch = 0; ch < 4; ch++)
				if (instbyid(o.inst[ch])) chinst[ch] = o.inst[ch];
		}
	}

	function preview(ch, value){
		var c = ensure();
		if (!c) return;
		schedulevoice(ch, value, c.currentTime, 0.12);
	}

	function schedulevoice(ch, value, t, dur){
		if (!value || !en[ch]) return;
		var spec = chaninst(ch);
		var c = ac;
		var peak = spec.wave === "noise" ? 0.2 : 0.14;
		var g = stepgain(c, spec, t, dur, peak);
		g.connect(master);
		if (spec.wave === "noise") {
			var level = value > 6 ? 6 : value;
			var src = c.createBufferSource();
			src.buffer = noisebuf(c);
			src.loop = true;
			src.playbackRate.value = 0.35 + level * 0.45;
			var flt = c.createBiquadFilter();
			flt.type = "bandpass";
			flt.frequency.value = 180 + level * 650;
			flt.Q.value = 0.6;
			src.connect(flt);
			flt.connect(g);
			src.start(t);
			src.stop(t + dur);
			remember(src);
			return;
		}
		var f = freq(value);
		if (spec.oct) f *= Math.pow(2, spec.oct);
		var o = makeosc(c, spec, f);
		o.connect(g);
		o.start(t);
		o.stop(t + dur + 0.02);
		remember(o);
		if (spec.vib) {
			var lfo = c.createOscillator();
			var depth = c.createGain();
			lfo.frequency.value = spec.vibhz || 5;
			depth.gain.value = spec.vib;
			lfo.connect(depth);
			depth.connect(o.frequency);
			lfo.start(t);
			lfo.stop(t + dur + 0.02);
			remember(lfo);
		}
		if (spec.detune) {
			var o2 = makeosc(c, spec, f);
			var o3 = makeosc(c, spec, f);
			o2.detune.value = spec.detune;
			o3.detune.value = -spec.detune;
			o2.connect(g);
			o3.connect(g);
			o2.start(t);
			o3.start(t);
			o2.stop(t + dur + 0.02);
			o3.stop(t + dur + 0.02);
			remember(o2);
			remember(o3);
		}
		if (spec.harm) {
			var oh = c.createOscillator();
			var hg = c.createGain();
			oh.type = "square";
			oh.frequency.value = f * spec.harm;
			hg.gain.value = 0.28;
			oh.connect(hg);
			hg.connect(g);
			oh.start(t);
			oh.stop(t + dur + 0.02);
			remember(oh);
		}
	}

	function stepgain(c, spec, t, dur, peak){
		var g = c.createGain();
		var a = spec.a;
		if (a < 0.002) a = 0.002;
		if (a > dur * 0.35) a = dur * 0.35;
		var rel = spec.r;
		if (rel > dur * 0.45) rel = dur * 0.45;
		if (rel < 0.01) rel = 0.01;
		var susAt = t + a + spec.d;
		var endHold = t + dur - rel;
		if (susAt > endHold) susAt = endHold;
		var top = Math.max(peak, 0.0001);
		var sus = Math.max(spec.s * peak, 0.0001);
		g.gain.setValueAtTime(0.0001, t);
		g.gain.exponentialRampToValueAtTime(top, t + a);
		if (susAt > t + a + 0.004)
			g.gain.exponentialRampToValueAtTime(sus, susAt);
		g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
		return g;
	}

	function firestep(step, t){
		var ch;
		var dur = Math.max(0.04, stepdur() * 0.92);
		for (ch = 0; ch < 4; ch++) schedulevoice(ch, song[ch][step], t, dur);
		queued.push({ step: step, t: t });
	}

	function songplay(){
		var c = ensure();
		if (!c) return;
		songstop();
		var gen = songgen;
		playOn = true;
		cursor = 0;
		nextTime = c.currentTime + 0.06;
		queued = [];
		paintplaybtn();
		function pump(){
			if (!playOn || gen !== songgen || !ac) return;
			var horizon = ac.currentTime + 0.18;
			var dur = stepdur();
			var i, hear = -1;
			while (nextTime < horizon) {
				firestep(cursor, nextTime);
				cursor = (cursor + 1) % STEPS;
				nextTime += dur;
			}
			for (i = 0; i < queued.length; i++)
				if (queued[i].t <= ac.currentTime) hear = queued[i].step;
			heard = hear;
			queued = queued.filter(function(q){ return q.t + dur > ac.currentTime - 0.05; });
			marknow(heard);
		}
		pump();
		playTimer = setInterval(pump, 40);
	}

	function songstop(){
		songgen++;
		playOn = false;
		if (playTimer) clearInterval(playTimer);
		playTimer = null;
		queued = [];
		heard = -1;
		marknow(-1);
		paintplaybtn();
	}

	function onpointerup(ev){
		var btn = ev.target.closest("button[data-midi]");
		if (!btn) return;
		noteoff(Number(F.attr(btn, "data-midi")));
	}

	// 给别的工具用：先在点击里打开音频，之后才能响。
	SK.bitarm = function(){ ensure(); };
	// 扫码成功：0.2 秒方波，音高跟循环蜂鸣一样。
	SK.bitok = sfxbeep;

	// 给别的工具用（摩斯码表）：按当前乐器和音量播一段音。
	// midi 音高，dur 秒数，at 是 AudioContext 时间轴上的起点（留空即现在）；返回结束时间，发不了声返回 -1
	SK.bitblip = function(midi, dur, at){
		var c = ensure();
		var spec;
		var t0;
		var f;
		var g;
		var o;
		var rel;
		if (!c || !(dur > 0)) return -1;
		spec = inst();
		// 噪声鼓当音色时用方波代替，否则听不出点划
		if (spec.wave === "noise") spec = { wave: "square", a: spec.a, d: spec.d, s: spec.s, r: spec.r };
		t0 = Math.max(c.currentTime + 0.01, Number(at) || 0);
		f = freq(midi);
		if (spec.oct) f *= Math.pow(2, spec.oct);
		rel = Math.min(0.025, dur / 3);
		g = envgain(c, spec, t0, 0.22);
		g.gain.setTargetAtTime(0.0001, t0 + dur - rel, rel / 3);
		g.connect(master);
		o = makeosc(c, spec, f);
		o.connect(g);
		o.start(t0);
		o.stop(t0 + dur + 0.03);
		o.onended = function(){
			var i = blips.indexOf(o);
			if (i >= 0) blips.splice(i, 1);
		};
		blips.push(o);
		return t0 + dur;
	};

	// 停掉 bitblip 排出去还没响完的音
	SK.bitblipstop = function(){
		var list = blips;
		var i;
		blips = [];
		for (i = 0; i < list.length; i++) {
			try { list[i].stop(); } catch (e) {}
		}
	};
})();
