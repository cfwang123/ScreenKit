(function(){
	var mode = "2";
	var fn = null;
	var view = { x0: -8, x1: 8, y0: -5, y1: 5 };
	var cam = { cx: 0, cy: 0, xspan: 4, yspan: 4, yaw: -0.6, pitch: 0.65 };
	var canvas = null;
	var watch = null;
	var drag = null;
	var ARITY = {
		sin: 1, cos: 1, tan: 1, asin: 1, acos: 1, atan: 1, atan2: 2,
		sqrt: 1, abs: 1, ln: 1, log: 1, log10: 1, log2: 1, exp: 1,
		floor: 1, ceil: 1, round: 1, sign: 1, pow: 2, min: 2, max: 2, hypot: 2
	};

	SK.regutil("u-plot", {
		title: "函数图像",
		go: "显示",
		hideGo: true,
		hideCopy: true,
		hideOut: true,
		body: body,
		open: open
	});

	function tt(key, fb){
		return SK.t(key) || fb;
	}

	function body(){
		return '<div class="plot">'
			+ '<div class="plot-bar">'
			+ '<button type="button" data-mode="2" class="on" data-k="web.plot.y">' + SK.esc(tt("web.plot.y", "y = f(x)")) + "</button>"
			+ '<button type="button" data-mode="3" data-k="web.plot.z">' + SK.esc(tt("web.plot.z", "z = f(x, y)")) + "</button>"
			+ '<input id="plot-fn" type="text" value="sin(x)" spellcheck="false" autocomplete="off"'
			+ ' data-kph="web.plot.ph" placeholder="' + SK.esc(tt("web.plot.ph", "sin(x)")) + '"'
			+ ' data-ktip="web.plot.tip" title="' + SK.esc(tt("web.plot.tip", "支持 + - * / ^、括号、sin cos tan sqrt abs ln log exp，常数 pi 和 e，可以写 2x。")) + '">'
			+ '<button type="button" data-act="reset" data-k="web.plot.reset">' + SK.esc(tt("web.plot.reset", "复位")) + "</button>"
			+ "</div>"
			+ '<p class="hint" data-k="web.plot.hint">' + SK.esc(tt("web.plot.hint", "拖动平移，滚轮缩放。按住 Ctrl 只缩放 x 轴，按住 Shift 只缩放 y 轴。三维时拖动转动视角，Shift 拖动平移。三角函数用弧度。双击恢复初始视图。")) + "</p>"
			+ '<div class="plot-stage"><canvas id="plot-cv"></canvas></div>'
			+ '<p class="hint" id="plot-msg"></p>'
			+ "</div>";
	}

	function open(){
		canvas = SK.$("plot-cv");
		if (!canvas) return;
		var bar = document.querySelector(".plot-bar");
		if (bar) bar.onclick = onbar;
		var input = SK.$("plot-fn");
		if (input) input.oninput = function(){ reparse(); paint(); };
		canvas.onpointerdown = down;
		canvas.onpointermove = move;
		canvas.onpointerup = up;
		canvas.onpointercancel = up;
		canvas.ondblclick = function(){ resetview(); paint(); };
		canvas.oncontextmenu = function(ev){ ev.preventDefault(); };
		canvas.addEventListener("wheel", wheel, { passive: false });
		if (watch) watch.disconnect();
		if (window.ResizeObserver && canvas.parentElement) {
			watch = new ResizeObserver(function(){ paint(); });
			watch.observe(canvas.parentElement);
		}
		resetview();
		reparse();
		requestAnimationFrame(paint);
	}

	function onbar(ev){
		var el = ev.target && ev.target.closest ? ev.target.closest("button") : ev.target;
		if (!el || !el.getAttribute) return;
		if (el.getAttribute("data-act") === "reset") {
			resetview();
			paint();
			return;
		}
		var next = el.getAttribute("data-mode");
		if (!next || next === mode) return;
		mode = next;
		var nodes = document.querySelectorAll(".plot-bar [data-mode]");
		var i;
		for (i = 0; i < nodes.length; i++)
			nodes[i].className = nodes[i].getAttribute("data-mode") === mode ? "on" : "";
		resetview();
		reparse();
		paint();
	}

	function say(text, bad){
		var el = SK.$("plot-msg");
		if (!el) return;
		el.textContent = text || "";
		el.className = bad ? "hint bad" : "hint";
	}

	function resetview(){
		if (mode === "3") {
			cam.cx = 0;
			cam.cy = 0;
			cam.xspan = 4;
			cam.yspan = 4;
			cam.yaw = -0.6;
			cam.pitch = 0.65;
			return;
		}
		var stage = canvas && canvas.parentElement;
		var w = stage && stage.clientWidth > 2 ? stage.clientWidth : 800;
		var h = stage && stage.clientHeight > 2 ? stage.clientHeight : 420;
		view.x0 = -8;
		view.x1 = 8;
		var half = 8 * h / w;
		view.y0 = -half;
		view.y1 = half;
	}

	function reparse(){
		var input = SK.$("plot-fn");
		var src = input ? input.value : "";
		try {
			fn = compile(src, mode === "3" ? 2 : 1);
			say("");
		}
		catch (ex) {
			fn = null;
			say(ex && ex.message ? ex.message : tt("web.plot.bad", "公式无法识别"), true);
		}
	}

	function paint(){
		if (!canvas || !canvas.parentElement) return;
		var stage = canvas.parentElement;
		var w = stage.clientWidth;
		var h = stage.clientHeight;
		if (w < 2 || h < 2) return;
		var dpr = window.devicePixelRatio || 1;
		var bw = Math.max(1, Math.floor(w * dpr));
		var bh = Math.max(1, Math.floor(h * dpr));
		if (canvas.width !== bw || canvas.height !== bh) {
			canvas.width = bw;
			canvas.height = bh;
		}
		var ctx = canvas.getContext("2d");
		ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
		ctx.clearRect(0, 0, w, h);
		ctx.fillStyle = "#fafaf9";
		ctx.fillRect(0, 0, w, h);
		if (mode === "3") draw3(ctx, w, h);
		else draw2(ctx, w, h);
	}

	function down(ev){
		if (!canvas || ev.button != null && ev.button !== 0 && ev.button !== 1 && ev.button !== 2) return;
		if (ev.preventDefault) ev.preventDefault();
		drag = {
			x: ev.clientX, y: ev.clientY, button: ev.button, shift: !!ev.shiftKey,
			x0: view.x0, x1: view.x1, y0: view.y0, y1: view.y1,
			cx: cam.cx, cy: cam.cy, xspan: cam.xspan, yspan: cam.yspan, yaw: cam.yaw, pitch: cam.pitch
		};
		canvas.className = "drag";
		if (canvas.setPointerCapture) canvas.setPointerCapture(ev.pointerId);
	}

	function move(ev){
		if (!drag || !canvas) return;
		var dx = ev.clientX - drag.x;
		var dy = ev.clientY - drag.y;
		if (mode === "3") {
			var pan = drag.shift || drag.button === 2 || drag.button === 1 || ev.shiftKey;
			if (pan) {
				var stage = canvas.parentElement;
				var w = stage.clientWidth || 1;
				cam.cx = drag.cx - dx * (drag.xspan * 2 / w);
				cam.cy = drag.cy + dy * (drag.yspan * 2 / w);
			}
			else {
				cam.yaw = drag.yaw + dx * 0.01;
				cam.pitch = drag.pitch + dy * 0.01;
				if (cam.pitch > 1.45) cam.pitch = 1.45;
				if (cam.pitch < -1.45) cam.pitch = -1.45;
			}
		}
		else {
			var stage2 = canvas.parentElement;
			var w2 = stage2.clientWidth || 1;
			var h2 = stage2.clientHeight || 1;
			var mx = (drag.x1 - drag.x0) / w2;
			var my = (drag.y1 - drag.y0) / h2;
			view.x0 = drag.x0 - dx * mx;
			view.x1 = drag.x1 - dx * mx;
			view.y0 = drag.y0 + dy * my;
			view.y1 = drag.y1 + dy * my;
		}
		paint();
	}

	function up(){
		drag = null;
		if (canvas) canvas.className = "";
	}

	function wheel(ev){
		if (!canvas) return;
		ev.preventDefault();
		var delta = ev.deltaY || ev.deltaX;
		if (!delta) return;
		var factor = delta > 0 ? 1.12 : 1 / 1.12;
		var xonly = ev.ctrlKey && !ev.shiftKey;
		var yonly = ev.shiftKey && !ev.ctrlKey;
		if (mode === "3") {
			if (!yonly) setspan("xspan", factor);
			if (!xonly) setspan("yspan", factor);
			paint();
			return;
		}
		var rect = canvas.getBoundingClientRect();
		var px = ev.clientX - rect.left;
		var py = ev.clientY - rect.top;
		var w = rect.width || 1;
		var h = rect.height || 1;
		if (!yonly) {
			var mx = view.x0 + px / w * (view.x1 - view.x0);
			var nx0 = mx - (mx - view.x0) * factor;
			var nx1 = mx + (view.x1 - mx) * factor;
			if (nx1 - nx0 >= 1e-6 && nx1 - nx0 <= 1e6) {
				view.x0 = nx0;
				view.x1 = nx1;
			}
		}
		if (!xonly) {
			var my = view.y1 - py / h * (view.y1 - view.y0);
			var ny0 = my - (my - view.y0) * factor;
			var ny1 = my + (view.y1 - my) * factor;
			if (ny1 - ny0 >= 1e-6 && ny1 - ny0 <= 1e6) {
				view.y0 = ny0;
				view.y1 = ny1;
			}
		}
		paint();
	}

	function setspan(key, factor){
		var v = cam[key] * factor;
		if (v < 0.05) v = 0.05;
		if (v > 1000) v = 1000;
		cam[key] = v;
	}

	function draw2(ctx, w, h){
		var xSpan = view.x1 - view.x0;
		var ySpan = view.y1 - view.y0;
		if (!(xSpan > 0) || !(ySpan > 0)) return;
		function X(x){ return (x - view.x0) / xSpan * w; }
		function Y(y){ return (view.y1 - y) / ySpan * h; }
		var xs = nicestep(xSpan);
		var ys = nicestep(ySpan);
		ctx.save();
		ctx.beginPath();
		ctx.rect(0, 0, w, h);
		ctx.clip();
		ctx.strokeStyle = "#e7e5e4";
		ctx.lineWidth = 1;
		ctx.fillStyle = "#a8a29e";
		ctx.font = '12px "Segoe UI", "Microsoft YaHei UI", sans-serif';
		ctx.textAlign = "center";
		ctx.textBaseline = "top";
		var x, y, px, py;
		ctx.beginPath();
		for (x = Math.ceil(view.x0 / xs) * xs; x < view.x1; x += xs) {
			px = X(x);
			ctx.moveTo(px, 0);
			ctx.lineTo(px, h);
		}
		for (y = Math.ceil(view.y0 / ys) * ys; y < view.y1; y += ys) {
			py = Y(y);
			ctx.moveTo(0, py);
			ctx.lineTo(w, py);
		}
		ctx.stroke();
		ctx.strokeStyle = "#78716c";
		ctx.lineWidth = 1.25;
		ctx.beginPath();
		if (view.y0 < 0 && view.y1 > 0) {
			py = Y(0);
			ctx.moveTo(0, py);
			ctx.lineTo(w, py);
		}
		if (view.x0 < 0 && view.x1 > 0) {
			px = X(0);
			ctx.moveTo(px, 0);
			ctx.lineTo(px, h);
		}
		ctx.stroke();
		for (x = Math.ceil(view.x0 / xs) * xs; x < view.x1; x += xs) {
			if (Math.abs(x) < xs * 1e-6) continue;
			ctx.fillText(fmt(x), X(x), Math.min(h - 16, Math.max(2, Y(0) + 4)));
		}
		ctx.textAlign = "left";
		ctx.textBaseline = "middle";
		for (y = Math.ceil(view.y0 / ys) * ys; y < view.y1; y += ys) {
			if (Math.abs(y) < ys * 1e-6) continue;
			ctx.fillText(fmt(y), Math.min(w - 36, Math.max(4, X(0) + 4)), Y(y));
		}
		if (fn) {
			ctx.strokeStyle = "#e11d48";
			ctx.lineWidth = 2;
			ctx.lineJoin = "round";
			ctx.lineCap = "round";
			ctx.beginPath();
			var n = Math.max(300, Math.floor(w * 2));
			var i, yy, started = false, prev = 0;
			for (i = 0; i <= n; i++) {
				x = view.x0 + xSpan * i / n;
				yy = ev(fn, x, 0);
				px = X(x);
				if (!isFinite(yy)) { started = false; continue; }
				py = Y(yy);
				if (started && Math.abs(py - prev) > h * 1.25) started = false;
				if (!started) ctx.moveTo(px, py);
				else ctx.lineTo(px, py);
				started = true;
				prev = py;
			}
			ctx.stroke();
		}
		ctx.restore();
	}

	function draw3(ctx, w, h){
		var n = 46;
		var grid = n + 1;
		var fit = Math.max(cam.xspan, cam.yspan);
		var zraw = new Array(grid * grid);
		var i, j, x, y, z, finite = [];
		for (j = 0; j < grid; j++) {
			y = cam.cy - cam.yspan + cam.yspan * 2 * j / n;
			for (i = 0; i < grid; i++) {
				x = cam.cx - cam.xspan + cam.xspan * 2 * i / n;
				z = fn ? ev(fn, x, y) : NaN;
				zraw[j * grid + i] = z;
				if (isFinite(z)) finite.push(z);
			}
		}
		var lo = 0, hi = 1;
		if (finite.length) {
			finite.sort(function(a, b){ return a - b; });
			lo = finite[Math.floor(finite.length * 0.04)];
			hi = finite[Math.floor(finite.length * 0.96)];
			if (!(hi > lo)) { lo = finite[0]; hi = finite[finite.length - 1]; }
			if (!(hi > lo)) { lo -= 1; hi += 1; }
		}
		var pad = (hi - lo) * 0.35;
		var zmid = (lo + hi) / 2;
		var zfit = fit / (hi - lo);
		var scale = Math.min(w, h) * 0.42 / fit;
		var pts = new Array(grid * grid);
		var cyaw = Math.cos(cam.yaw), syaw = Math.sin(cam.yaw);
		var cp = Math.cos(cam.pitch), sp = Math.sin(cam.pitch);
		for (j = 0; j < grid; j++) {
			y = cam.cy - cam.yspan + cam.yspan * 2 * j / n;
			for (i = 0; i < grid; i++) {
				x = cam.cx - cam.xspan + cam.xspan * 2 * i / n;
				z = zraw[j * grid + i];
				var ok = isFinite(z);
				if (ok) {
					if (z < lo - pad) z = lo - pad;
					else if (z > hi + pad) z = hi + pad;
				}
				var X = (x - cam.cx) / cam.xspan * fit;
				var Y = (y - cam.cy) / cam.yspan * fit;
				var Z = ok ? (z - zmid) * zfit : 0;
				var x1 = X * cyaw - Y * syaw;
				var y1 = X * syaw + Y * cyaw;
				var y2 = y1 * cp - Z * sp;
				var z2 = y1 * sp + Z * cp;
				pts[j * grid + i] = {
					x: w / 2 + x1 * scale,
					y: h / 2 - z2 * scale,
					d: y2,
					ok: ok,
					t: ok ? (z - lo) / (hi - lo) : 0
				};
			}
		}
		var tris = [];
		var a, b, c, d, p0, p1, p2;
		for (j = 0; j < n; j++) {
			for (i = 0; i < n; i++) {
				a = j * grid + i;
				b = a + 1;
				c = a + grid;
				d = c + 1;
				pushtri(tris, pts, a, c, b);
				pushtri(tris, pts, c, d, b);
			}
		}
		tris.sort(function(u, v){ return v.d - u.d; });
		ctx.save();
		ctx.beginPath();
		ctx.rect(0, 0, w, h);
		ctx.clip();
		for (i = 0; i < tris.length; i++) {
			p0 = pts[tris[i].a];
			p1 = pts[tris[i].b];
			p2 = pts[tris[i].c];
			ctx.beginPath();
			ctx.moveTo(p0.x, p0.y);
			ctx.lineTo(p1.x, p1.y);
			ctx.lineTo(p2.x, p2.y);
			ctx.closePath();
			ctx.fillStyle = zcolor((p0.t + p1.t + p2.t) / 3);
			ctx.fill();
		}
		drawaxes(ctx, w, h, scale, cyaw, syaw, cp, sp, zmid, zfit, lo, hi);
		ctx.restore();
	}

	function pushtri(tris, pts, a, b, c){
		var p0 = pts[a], p1 = pts[b], p2 = pts[c];
		if (!p0.ok || !p1.ok || !p2.ok) return;
		tris.push({ a: a, b: b, c: c, d: (p0.d + p1.d + p2.d) / 3 });
	}

	function drawaxes(ctx, w, h, scale, cyaw, syaw, cp, sp, zmid, zfit, lo, hi){
		function proj(x, y, z){
			var fit = Math.max(cam.xspan, cam.yspan);
			var X = (x - cam.cx) / cam.xspan * fit;
			var Y = (y - cam.cy) / cam.yspan * fit;
			var Z = (z - zmid) * zfit;
			var x1 = X * cyaw - Y * syaw;
			var y1 = X * syaw + Y * cyaw;
			var z2 = y1 * sp + Z * cp;
			return { x: w / 2 + x1 * scale, y: h / 2 - z2 * scale };
		}
		var o = proj(0, 0, 0);
		var ax = proj(cam.xspan * 0.85, 0, 0);
		var ay = proj(0, cam.yspan * 0.85, 0);
		var az = proj(0, 0, hi);
		ctx.lineWidth = 1.5;
		ctx.font = '12px "Segoe UI", "Microsoft YaHei UI", sans-serif';
		ctx.fillStyle = "#44403c";
		axis(ctx, o, ax, "#e11d48", "x");
		axis(ctx, o, ay, "#16a34a", "y");
		axis(ctx, o, az, "#2563eb", "z");
	}

	function axis(ctx, a, b, color, name){
		ctx.strokeStyle = color;
		ctx.beginPath();
		ctx.moveTo(a.x, a.y);
		ctx.lineTo(b.x, b.y);
		ctx.stroke();
		ctx.fillStyle = color;
		ctx.fillText(name, b.x + 4, b.y);
	}

	function zcolor(t){
		if (t < 0) t = 0;
		if (t > 1) t = 1;
		var r, g, b;
		if (t < 0.5) {
			var u = t / 0.5;
			r = 37 + u * 50;
			g = 99 + u * 140;
			b = 196 - u * 50;
		}
		else {
			var v = (t - 0.5) / 0.5;
			r = 87 + v * 168;
			g = 239 - v * 140;
			b = 146 - v * 110;
		}
		return "rgb(" + Math.round(r) + "," + Math.round(g) + "," + Math.round(b) + ")";
	}

	function nicestep(span){
		var raw = span / 8;
		if (!(raw > 0)) return 1;
		var p = Math.pow(10, Math.floor(Math.log10(raw)));
		var e = raw / p;
		var n = e < 1.5 ? 1 : e < 3.5 ? 2 : e < 7.5 ? 5 : 10;
		return n * p;
	}

	function fmt(v){
		if (!isFinite(v)) return "";
		var a = Math.abs(v);
		if (a !== 0 && (a < 0.001 || a >= 10000)) return v.toExponential(2);
		return String(Math.round(v * 1000) / 1000);
	}

	function compile(src, dim){
		var s = normalize(src);
		if (!s) throw new Error("请输入公式");
		if (s.length > 400) throw new Error("公式太长");
		var i = 0;
		var tree = expr();
		if (i !== s.length) throw new Error("公式里有无法识别的部分");
		return tree;
		function expr(){
			var left = term();
			while (peek() === "+" || peek() === "-") {
				var op = peek();
				i++;
				left = { t: "b", op: op, a: left, b: term() };
			}
			return left;
		}
		function term(){
			var left = pow();
			while (true) {
				var c = peek();
				if (c === "*" || c === "/" || c === "%") {
					i++;
					left = { t: "b", op: c, a: left, b: pow() };
				}
				else if (c === "(" || isletter(c) || isdigit(c) || c === ".") {
					left = { t: "b", op: "*", a: left, b: pow() };
				}
				else break;
			}
			return left;
		}
		function pow(){
			var left = unary();
			if (peek() === "^") {
				i++;
				return { t: "b", op: "^", a: left, b: pow() };
			}
			return left;
		}
		function unary(){
			if (peek() === "-" || peek() === "+") {
				var op = peek();
				i++;
				var inner = unary();
				return op === "-" ? { t: "u", op: "-", a: inner } : inner;
			}
			return primary();
		}
		function primary(){
			if (peek() === "(") {
				i++;
				var v = expr();
				if (peek() !== ")") throw new Error("括号没有配对");
				i++;
				return v;
			}
			if (isletter(peek())) return named();
			return number();
		}
		function named(){
			var start = i;
			while (isletter(peek())) i++;
			var word = s.slice(start, i).toLowerCase();
			var cut = splitknown(word);
			i = start + cut;
			var name = word.slice(0, cut);
			if (name === "log" && s.slice(i, i + 2) === "10" && !isdigit(s.charAt(i + 2))) {
				name = "log10";
				i += 2;
			}
			else if (name === "log" && s.charAt(i) === "2" && !isdigit(s.charAt(i + 1))) {
				name = "log2";
				i += 1;
			}
			if (ARITY[name] != null) {
				if (peek() === "(") return call(name);
				if (ARITY[name] === 1) return { t: "f", name: name, args: [unary()] };
				throw new Error("函数参数个数不对");
			}
			if (name === "pi") return { t: "n", v: Math.PI };
			if (name === "e") return { t: "n", v: Math.E };
			if (name === "x") return { t: "v", i: 0 };
			if (name === "y") {
				if (dim < 2) throw new Error(tt("web.plot.xonly", "二维公式只能使用 x"));
				return { t: "v", i: 1 };
			}
			throw new Error("没有这个名字");
		}
		function splitknown(word){
			var n;
			for (n = word.length; n > 0; n--) {
				var p = word.slice(0, n);
				if (ARITY[p] != null || p === "pi" || p === "e" || p === "x" || p === "y") return n;
			}
			return word.length;
		}
		function call(name){
			if (ARITY[name] == null) throw new Error("没有这个函数");
			i++;
			var args = [expr()];
			while (peek() === ",") {
				i++;
				args.push(expr());
			}
			if (peek() !== ")") throw new Error("括号没有配对");
			i++;
			if (args.length !== ARITY[name]) throw new Error("函数参数个数不对");
			return { t: "f", name: name, args: args };
		}
		function number(){
			var start = i;
			if (peek() === "." && isdigit(s.charAt(i + 1))) i++;
			while (isdigit(peek())) i++;
			if (peek() === "." && isdigit(s.charAt(i + 1))) {
				i++;
				while (isdigit(peek())) i++;
			}
			if ((peek() === "e" || peek() === "E") && expdigits(i + 1)) {
				i++;
				if (peek() === "+" || peek() === "-") i++;
				while (isdigit(peek())) i++;
			}
			if (start === i) throw new Error("这里需要数字");
			var v = Number(s.slice(start, i));
			if (!isFinite(v)) throw new Error("数字无效");
			return { t: "n", v: v };
		}
		function expdigits(k){
			if (s.charAt(k) === "+" || s.charAt(k) === "-") k++;
			return isdigit(s.charAt(k));
		}
		function peek(){ return s.charAt(i); }
	}

	function normalize(src){
		var s = String(src == null ? "" : src);
		s = s.replace(/\u03c0|\u03a0/g, "pi");
		s = s.replace(/[×·]/g, "*").replace(/÷/g, "/");
		s = s.replace(/（/g, "(").replace(/）/g, ")");
		s = s.replace(/\s+/g, "");
		s = s.replace(/\*\*/g, "^");
		if (s.length >= 2 && (s.charAt(0) === "y" || s.charAt(0) === "Y" || s.charAt(0) === "z" || s.charAt(0) === "Z") && s.charAt(1) === "=")
			s = s.slice(2);
		else if (/^f\([^)]*\)=/i.test(s)) s = s.replace(/^f\([^)]*\)=/i, "");
		return s;
	}

	function ev(node, x, y){
		if (!node) return NaN;
		if (node.t === "n") return node.v;
		if (node.t === "v") return node.i ? y : x;
		if (node.t === "u") return -ev(node.a, x, y);
		if (node.t === "b") {
			var a = ev(node.a, x, y);
			var b = ev(node.b, x, y);
			if (node.op === "+") return a + b;
			if (node.op === "-") return a - b;
			if (node.op === "*") return a * b;
			if (node.op === "/") return b === 0 ? NaN : a / b;
			if (node.op === "%") return b === 0 ? NaN : a % b;
			return Math.pow(a, b);
		}
		var args = [];
		var i;
		for (i = 0; i < node.args.length; i++) args.push(ev(node.args[i], x, y));
		return applyfn(node.name, args);
	}

	function applyfn(name, args){
		var a = args[0], b = args.length > 1 ? args[1] : 0;
		var out;
		if (name === "sin") out = Math.sin(a);
		else if (name === "cos") out = Math.cos(a);
		else if (name === "tan") out = Math.tan(a);
		else if (name === "asin") out = Math.asin(a);
		else if (name === "acos") out = Math.acos(a);
		else if (name === "atan") out = Math.atan(a);
		else if (name === "atan2") out = Math.atan2(a, b);
		else if (name === "sqrt") out = Math.sqrt(a);
		else if (name === "abs") out = Math.abs(a);
		else if (name === "ln") out = Math.log(a);
		else if (name === "log" || name === "log10") out = Math.log(a) / Math.LN10;
		else if (name === "log2") out = Math.log(a) / Math.LN2;
		else if (name === "exp") out = Math.exp(a);
		else if (name === "floor") out = Math.floor(a);
		else if (name === "ceil") out = Math.ceil(a);
		else if (name === "round") out = Math.round(a);
		else if (name === "sign") out = Math.sign(a);
		else if (name === "pow") out = Math.pow(a, b);
		else if (name === "min") out = Math.min(a, b);
		else if (name === "max") out = Math.max(a, b);
		else if (name === "hypot") out = Math.hypot(a, b);
		else return NaN;
		return isFinite(out) ? out : NaN;
	}

	function isletter(c){ return (c >= "a" && c <= "z") || (c >= "A" && c <= "Z"); }
	function isdigit(c){ return c >= "0" && c <= "9"; }
})();
