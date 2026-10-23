(function(){
	var mode = "2";
	var style = "grid";
	var crossOn = true;
	var hover = null;
	var plate = null;
	var plateKey = "";
	var fn = null;
	var implicit = false;
	var docOn = false;
	var view = { x0: -8, x1: 8, y0: -5, y1: 5 };
	var SAMPLES = [
		{ g: "y", mode: "2", fn: "sin(x)" },
		{ g: "y", mode: "2", fn: "x^2" },
		{ g: "y", mode: "2", fn: "1/x" },
		{ g: "y", mode: "2", fn: "exp(-x^2)" },
		{ g: "y", mode: "2", fn: "sin(x)/x" },
		{ g: "y", mode: "2", fn: "tan(x)" },
		{ g: "eq", mode: "2", fn: "x^2+y^2=1" },
		{ g: "eq", mode: "2", fn: "x^2/4+y^2/9=1" },
		{ g: "eq", mode: "2", fn: "x^2-y^2=1" },
		{ g: "eq", mode: "2", fn: "y^2=x^3-x" },
		{ g: "eq", mode: "2", fn: "(x^2+y^2-1)^3=x^2*y^3" },
		{ g: "z", mode: "3", fn: "sin(x)*cos(y)" },
		{ g: "z", mode: "3", fn: "x^2-y^2" },
		{ g: "z", mode: "3", fn: "sin(sqrt(x^2+y^2))" },
		{ g: "z", mode: "3", fn: "exp(-(x^2+y^2))" },
		{ g: "eq3", mode: "3", fn: "x^2+y^2+z^2=9" },
		{ g: "eq3", mode: "3", fn: "x^2/4+y^2/9+z^2=1" },
		{ g: "eq3", mode: "3", fn: "x^2+y^2-z^2=1" },
		{ g: "eq3", mode: "3", fn: "x^2+y^2=z^2" },
		{ g: "eq3", mode: "3", fn: "x+y+z=3" },
		{ g: "eq3", mode: "3", fn: "(x^2+9/4*y^2+z^2-1)^3-x^2*z^3-9/80*y^2*z^3=0" }
	];
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
			+ '<span class="plot-seg">'
			+ '<button type="button" data-mode="2" class="on" data-k="web.plot.y">' + SK.esc(tt("web.plot.y", "y = f(x)")) + "</button>"
			+ '<button type="button" data-mode="3" data-k="web.plot.z">' + SK.esc(tt("web.plot.z", "z = f(x, y)")) + "</button>"
			+ "</span>"
			+ '<button type="button" class="on" data-act="cross" data-ktip="web.plot.crossTip" title="' + SK.esc(tt("web.plot.crossTip", "竖线与曲线的交点。再点一次关闭。")) + '"><i class="fa-solid fa-crosshairs"></i><span data-k="web.plot.cross">' + SK.esc(tt("web.plot.cross", "Cross")) + "</span></button>"
			+ '<span class="plot-style plot-seg" hidden>'
			+ styleBtn("fill", "fa-chart-area", "web.plot.fill", "曲面")
			+ styleBtn("grid", "fa-border-all", "web.plot.grid", "网格")
			+ styleBtn("dots", "fa-braille", "web.plot.dots", "点阵")
			+ styleBtn("level", "fa-layer-group", "web.plot.level", "等高线")
			+ "</span>"
			+ '<input id="plot-fn" type="text" value="sin(x)" spellcheck="false" autocomplete="off"'
			+ ' data-kph="web.plot.ph" placeholder="' + SK.esc(tt("web.plot.ph", "sin(x)")) + '"'
			+ ' data-ktip="web.plot.tip" title="' + SK.esc(tt("web.plot.tip", "支持 + - * / ^、括号、sin cos tan sqrt abs ln log exp，常数 pi 和 e，可以写 2x。二维可以写等式，例如 x^2+y^2=1；三维可以写 z=f(x, y)，也可以写方程，例如 x^2+y^2+z^2=9 画球面。")) + '">'
			+ '<div class="plot-ex"><button type="button" data-act="ex"><i class="fa-solid fa-caret-down"></i><span data-k="web.plot.ex">' + SK.esc(tt("web.plot.ex", "示例")) + "</span></button>"
			+ '<div class="plot-menu" id="plot-menu" hidden>' + samples() + "</div></div>"
			+ '<button type="button" data-act="reset" data-k="web.plot.reset">' + SK.esc(tt("web.plot.reset", "复位")) + "</button>"
			+ "</div>"
			+ '<p class="hint" data-k="web.plot.hint">' + SK.esc(tt("web.plot.hint", "拖动平移，滚轮缩放。按住 Ctrl 只缩放 x 轴，按住 Shift 只缩放 y 轴。三维时拖动转动视角，Shift 拖动平移。三角函数用弧度。双击恢复初始视图。等式如 x^2+y^2=1 会画出曲线；三维方程如 x^2+y^2+z^2=9 会画出曲面。点「示例」可选择。")) + "</p>"
			+ '<div class="plot-stage"><canvas id="plot-cv"></canvas><div class="plot-read" id="plot-read" hidden></div></div>'
			+ '<p class="hint" id="plot-msg"></p>'
			+ "</div>";
	}

	function open(){
		canvas = F.id("plot-cv");
		if (!canvas) return;
		var bar = F.q(".plot-bar");
		if (bar) bar.onclick = onbar;
		var input = F.id("plot-fn");
		if (input) input.oninput = function(){ reparse(); paint(); };
		canvas.onpointerdown = down;
		canvas.onpointermove = move;
		canvas.onpointerup = up;
		canvas.onpointercancel = up;
		canvas.onpointerleave = leave;
		canvas.ondblclick = function(){ resetview(); paint(); };
		canvas.oncontextmenu = function(ev){ F.pd(ev); };
		F.on(canvas, "wheel", wheel, { passive: false });
		if (!docOn) {
			docOn = true;
			F.on(document, "pointerdown", ondoc);
		}
		if (watch) watch.disconnect();
		if (window.ResizeObserver && canvas.parentElement) {
			watch = new ResizeObserver(function(){ paint(); });
			watch.observe(canvas.parentElement);
		}
		markmode();
		markstyle();
		resetview();
		reparse();
		F.raf(paint);
	}

	function styleBtn(id, icon, key, fb){
		var on = id === style ? ' class="on"' : "";
		return '<button type="button" data-style="' + id + '"' + on + '><i class="fa-solid ' + icon + '"></i><span data-k="' + key + '">' + SK.esc(tt(key, fb)) + "</span></button>";
	}

	function onbar(ev){
		var el = ev.target && ev.target.closest ? ev.target.closest("button") : ev.target;
		if (!el || !el.getAttribute) return;
		if (F.attr(el, "data-act") === "ex") {
			togglemenu();
			return;
		}
		var sample = F.attr(el, "data-ex");
		if (sample != null) {
			pick(sample, F.attr(el, "data-mode"));
			return;
		}
		if (F.attr(el, "data-act") === "cross") {
			crossOn = !crossOn;
			el.className = crossOn ? "on" : "";
			if (!crossOn) hover = null;
			paint();
			return;
		}
		if (F.attr(el, "data-act") === "reset") {
			hidemenu();
			resetview();
			paint();
			return;
		}
		var st = F.attr(el, "data-style");
		if (st) {
			if (st === style) return;
			style = st;
			markstyle();
			paint();
			return;
		}
		var next = F.attr(el, "data-mode");
		if (!next || next === mode) return;
		hidemenu();
		mode = next;
		markmode();
		resetview();
		reparse();
		paint();
	}

	function samples(){
		var html = "";
		var g = "";
		var i, row, key, fb;
		for (i = 0; i < SAMPLES.length; i++) {
			row = SAMPLES[i];
			if (row.g !== g) {
				g = row.g;
				key = g === "eq" ? "web.plot.exeq" : g === "eq3" ? "web.plot.exeq3" : g === "z" ? "web.plot.z" : "web.plot.y";
				fb = g === "eq" ? "方程" : g === "eq3" ? "三维方程" : g === "z" ? "z = f(x, y)" : "y = f(x)";
				html += '<p data-k="' + key + '">' + SK.esc(tt(key, fb)) + "</p>";
			}
			html += '<button type="button" data-ex="' + SK.esc(row.fn) + '" data-mode="' + row.mode + '">' + SK.esc(row.fn) + "</button>";
		}
		return html;
	}

	function togglemenu(){
		var menu = F.id("plot-menu");
		if (!menu) return;
		menu.hidden = !menu.hidden;
	}

	function hidemenu(){
		var menu = F.id("plot-menu");
		if (menu) menu.hidden = true;
	}

	function ondoc(ev){
		var menu = F.id("plot-menu");
		if (!menu || menu.hidden) return;
		if (ev.target && ev.target.closest && ev.target.closest(".plot-ex")) return;
		menu.hidden = true;
	}

	function pick(src, next){
		var input = F.id("plot-fn");
		if (input) input.value = src;
		hidemenu();
		if (next && next !== mode) {
			mode = next;
			markmode();
		}
		resetview();
		reparse();
		paint();
	}

	function markmode(){
		var nodes = F.qa(".plot-seg > button[data-mode]");
		var i;
		for (i = 0; i < nodes.length; i++)
			nodes[i].className = F.attr(nodes[i], "data-mode") === mode ? "on" : "";
		var box = F.q(".plot-style");
		if (box) box.hidden = mode !== "3";
		var cross = F.q('.plot-bar > [data-act="cross"]');
		if (cross) cross.hidden = mode !== "2";
		if (mode !== "2") hover = null;
	}

	function markstyle(){
		var nodes = F.qa(".plot-style [data-style]");
		var i;
		for (i = 0; i < nodes.length; i++)
			nodes[i].className = F.attr(nodes[i], "data-style") === style ? "on" : "";
	}

	function say(text, bad){
		var el = F.id("plot-msg");
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
		var input = F.id("plot-fn");
		var src = input ? input.value : "";
		implicit = false;
		try {
			var norm = normalize(src);
			var eq = norm.indexOf("=") >= 0;
			fn = compile(src, mode === "3" && eq ? 3 : (mode === "3" || eq ? 2 : 1));
			implicit = fn.t === "eq";
			if (mode !== "3" && !implicit && usesY(fn)) {
				fn = null;
				say(tt("web.plot.xonly", "二维函数只能使用 x。要画圆、椭圆这类图，请写成等式，例如 x^2+y^2=1。"), true);
				return;
			}
			say("");
		}
		catch (ex) {
			fn = null;
			implicit = false;
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
		var ctx = F.ctx(canvas);
		var key = sceneKey(w, h);
		if (plateKey !== key) {
			if (!plate) plate = F.el("canvas");
			plate.width = bw;
			plate.height = bh;
			var pctx = F.ctx(plate);
			pctx.setTransform(dpr, 0, 0, dpr, 0, 0);
			pctx.clearRect(0, 0, w, h);
			pctx.fillStyle = "#fafaf9";
			pctx.fillRect(0, 0, w, h);
			if (mode === "3") draw3(pctx, w, h);
			else draw2(pctx, w, h);
			plateKey = key;
		}
		ctx.setTransform(1, 0, 0, 1, 0, 0);
		ctx.clearRect(0, 0, bw, bh);
		ctx.drawImage(plate, 0, 0);
		if (mode === "2" && crossOn && hover && !drag) {
			ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
			drawCross(ctx, w, h);
		}
		else hideRead();
	}

	function sceneKey(w, h){
		var input = F.id("plot-fn");
		var src = input ? input.value : "";
		if (mode === "3")
			return "3|" + style + "|" + src + "|" + w + "x" + h + "|" + cam.cx + "," + cam.cy + "," + cam.xspan + "," + cam.yspan + "," + cam.yaw + "," + cam.pitch;
		return "2|" + src + "|" + w + "x" + h + "|" + view.x0 + "," + view.x1 + "," + view.y0 + "," + view.y1;
	}

	function down(ev){
		if (!canvas || ev.button != null && ev.button !== 0 && ev.button !== 1 && ev.button !== 2) return;
		if (ev.preventDefault) F.pd(ev);
		drag = {
			x: ev.clientX, y: ev.clientY, button: ev.button, shift: !!ev.shiftKey,
			x0: view.x0, x1: view.x1, y0: view.y0, y1: view.y1,
			cx: cam.cx, cy: cam.cy, xspan: cam.xspan, yspan: cam.yspan, yaw: cam.yaw, pitch: cam.pitch
		};
		canvas.className = "drag";
		if (canvas.setPointerCapture) canvas.setPointerCapture(ev.pointerId);
	}

	function track(ev){
		if (!canvas || mode !== "2" || !crossOn) return;
		var rect = F.rect(canvas);
		hover = { px: ev.clientX - rect.left, py: ev.clientY - rect.top };
	}

	function leave(){
		hover = null;
		if (mode === "2" && crossOn) paint();
	}

	function move(ev){
		track(ev);
		if (!drag || !canvas) {
			if (mode === "2" && crossOn) paint();
			return;
		}
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
		var was = drag && (implicit || crossOn);
		drag = null;
		if (canvas) canvas.className = "";
		if (was) paint();
	}

	function wheel(ev){
		if (!canvas) return;
		F.pd(ev);
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
		var rect = F.rect(canvas);
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

	function hideRead(){
		var el = F.id("plot-read");
		if (el) el.hidden = true;
	}

	function drawCross(ctx, w, h){
		var mx = view.x0 + hover.px / w * (view.x1 - view.x0);
		var my = view.y1 - hover.py / h * (view.y1 - view.y0);
		var xSpan = view.x1 - view.x0;
		var ySpan = view.y1 - view.y0;
		function X(x){ return (x - view.x0) / xSpan * w; }
		function Y(y){ return (view.y1 - y) / ySpan * h; }
		ctx.save();
		ctx.beginPath();
		ctx.rect(0, 0, w, h);
		ctx.clip();
		ctx.strokeStyle = "rgba(37, 99, 235, .8)";
		ctx.lineWidth = 1;
		ctx.setLineDash([4, 4]);
		ctx.beginPath();
		ctx.moveTo(hover.px, 0);
		ctx.lineTo(hover.px, h);
		ctx.stroke();
		ctx.setLineDash([]);
		var pts = crossings(mx, my);
		var i, p, px, py, lines = [];
		for (i = 0; i < pts.length; i++) {
			p = pts[i];
			lines.push("(" + fmt(p.x) + ", " + fmt(p.y) + ")");
			px = X(p.x);
			py = Y(p.y);
			if (px < -6 || px > w + 6 || py < -6 || py > h + 6) continue;
			ctx.fillStyle = "#fff";
			ctx.beginPath();
			ctx.arc(px, py, 4.5, 0, 6.2832);
			ctx.fill();
			ctx.fillStyle = "#1d4ed8";
			ctx.beginPath();
			ctx.arc(px, py, 3, 0, 6.2832);
			ctx.fill();
		}
		ctx.restore();
		var box = F.id("plot-read");
		if (!box) return;
		if (!lines.length) {
			box.hidden = true;
			return;
		}
		box.textContent = lines.join("\n");
		box.hidden = false;
		var left = hover.px + 14;
		var top = hover.py + 14;
		if (left > w - 130) left = hover.px - 130;
		if (top > h - lines.length * 18 - 8) top = hover.py - lines.length * 18 - 8;
		if (left < 4) left = 4;
		if (top < 4) top = 4;
		box.style.left = left + "px";
		box.style.top = top + "px";
	}

	function crossings(mx, my){
		var raw = [];
		function add(x, y){
			if (!isFinite(x) || !isFinite(y)) return;
			var i, dx, dy;
			for (i = 0; i < raw.length; i++) {
				dx = (raw[i].x - x) / (view.x1 - view.x0);
				dy = (raw[i].y - y) / (view.y1 - view.y0);
				if (dx * dx + dy * dy < 1e-5) return;
			}
			raw.push({ x: x, y: y });
		}
		if (!fn) return raw;
		var i, ys;
		if (implicit) {
			ys = zeros(function(y){ return ev(fn, mx, y); }, view.y0, view.y1);
			for (i = 0; i < ys.length; i++) add(mx, ys[i]);
		}
		else add(mx, ev(fn, mx, 0));
		var sy = view.y1 - view.y0;
		raw.sort(function(a, b){
			var e1 = (a.y - my) / sy;
			var e2 = (b.y - my) / sy;
			return e1 * e1 - e2 * e2;
		});
		if (raw.length > 8) raw.length = 8;
		return raw;
	}

	function zeros(sample, a0, a1){
		var n = 360;
		var out = [];
		var i, k, t0, t1, f0, f1, lo, hi, flo, fhi, tm, fm;
		f0 = sample(a0);
		for (i = 1; i <= n; i++) {
			t1 = a0 + (a1 - a0) * i / n;
			f1 = sample(t1);
			t0 = a0 + (a1 - a0) * (i - 1) / n;
			if (isFinite(f0) && isFinite(f1) && (f0 === 0 || (f1 === 0 && i === n) || (f0 > 0) !== (f1 > 0))) {
				if (Math.abs(f0) > 1e4 && Math.abs(f1) > 1e4) { f0 = f1; continue; }
				if (f0 === 0) out.push(t0);
				else if (f1 === 0 && i === n) out.push(t1);
				else if (f1 !== 0) {
					lo = t0;
					hi = t1;
					flo = f0;
					fhi = f1;
					for (k = 0; k < 12; k++) {
						tm = (lo + hi) / 2;
						fm = sample(tm);
						if (!isFinite(fm)) { lo = NaN; break; }
						if ((flo > 0) === (fm > 0)) { lo = tm; flo = fm; }
						else { hi = tm; fhi = fm; }
					}
					if (isFinite(lo)) out.push((lo + hi) / 2);
				}
			}
			f0 = f1;
		}
		return out;
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
		if (fn && implicit) drawContour(ctx, w, h, X, Y);
		else if (fn) {
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

	function drawContour(ctx, w, h, X, Y){
		var cols = drag ? 110 : Math.max(160, Math.min(420, Math.floor(w / 2.5)));
		var rows = Math.max(80, Math.round(cols * h / Math.max(w, 1)));
		if (drag) rows = Math.max(60, Math.round(rows * 0.6));
		var gw = cols + 1;
		var gh = rows + 1;
		var vals = new Array(gw * gh);
		var i, j, x0, y0, x1, y1;
		for (j = 0; j < gh; j++) {
			y0 = view.y0 + (view.y1 - view.y0) * j / rows;
			for (i = 0; i < gw; i++) {
				x0 = view.x0 + (view.x1 - view.x0) * i / cols;
				vals[j * gw + i] = ev(fn, x0, y0);
			}
		}
		ctx.strokeStyle = "#e11d48";
		ctx.lineWidth = 2;
		ctx.lineJoin = "round";
		ctx.lineCap = "round";
		ctx.beginPath();
		for (j = 0; j < rows; j++) {
			y0 = view.y0 + (view.y1 - view.y0) * j / rows;
			y1 = view.y0 + (view.y1 - view.y0) * (j + 1) / rows;
			for (i = 0; i < cols; i++) {
				x0 = view.x0 + (view.x1 - view.x0) * i / cols;
				x1 = view.x0 + (view.x1 - view.x0) * (i + 1) / cols;
				cell(ctx, X, Y, x0, y0, x1, y1, vals[j * gw + i], vals[j * gw + i + 1], vals[(j + 1) * gw + i + 1], vals[(j + 1) * gw + i]);
			}
		}
		ctx.stroke();
	}

	function cell(ctx, X, Y, x0, y0, x1, y1, v00, v10, v11, v01){
		if (v00 === 0 && v10 === 0) addseg(ctx, X, Y, { x: x0, y: y0 }, { x: x1, y: y0 });
		if (v10 === 0 && v11 === 0) addseg(ctx, X, Y, { x: x1, y: y0 }, { x: x1, y: y1 });
		if (v11 === 0 && v01 === 0) addseg(ctx, X, Y, { x: x1, y: y1 }, { x: x0, y: y1 });
		if (v01 === 0 && v00 === 0) addseg(ctx, X, Y, { x: x0, y: y1 }, { x: x0, y: y0 });
		var pB = edgePt(x0, y0, v00, x1, y0, v10);
		var pR = edgePt(x1, y0, v10, x1, y1, v11);
		var pT = edgePt(x1, y1, v11, x0, y1, v01);
		var pL = edgePt(x0, y1, v01, x0, y0, v00);
		var pts = distinct([pB, pR, pT, pL]);
		if (pts.length === 2) {
			addseg(ctx, X, Y, pts[0], pts[1]);
			return;
		}
		if (pts.length !== 4) return;
		var fc = ev(fn, (x0 + x1) / 2, (y0 + y1) / 2);
		var same = isFinite(fc) && fc !== 0 && (fc > 0) === (v00 > 0);
		if (!isFinite(fc) || fc === 0) {
			addseg(ctx, X, Y, pB, pT);
			addseg(ctx, X, Y, pL, pR);
		}
		else if (same) {
			addseg(ctx, X, Y, pB, pR);
			addseg(ctx, X, Y, pT, pL);
		}
		else {
			addseg(ctx, X, Y, pB, pL);
			addseg(ctx, X, Y, pR, pT);
		}
	}

	function edgePt(x0, y0, f0, x1, y1, f1){
		if (!isFinite(f0) || !isFinite(f1)) return null;
		if (f0 === 0 && f1 === 0) return null;
		if (f0 === 0) return { x: x0, y: y0 };
		if (f1 === 0) return { x: x1, y: y1 };
		if ((f0 > 0) === (f1 > 0)) return null;
		var a0 = f0 < 0 ? -f0 : f0;
		var a1 = f1 < 0 ? -f1 : f1;
		if (a0 > 1e4 && a1 > 1e4) {
			var fm = ev(fn, (x0 + x1) / 2, (y0 + y1) / 2);
			var am = fm < 0 ? -fm : fm;
			if (!isFinite(fm) || am > 1e4) return null;
		}
		var t = f0 / (f0 - f1);
		if (!(t >= 0 && t <= 1)) return null;
		return { x: x0 + (x1 - x0) * t, y: y0 + (y1 - y0) * t };
	}

	function distinct(list){
		var out = [];
		var i, j, a, dup;
		for (i = 0; i < list.length; i++) {
			a = list[i];
			if (!a) continue;
			dup = false;
			for (j = 0; j < out.length; j++) {
				if (Math.abs(a.x - out[j].x) < 1e-9 && Math.abs(a.y - out[j].y) < 1e-9) dup = true;
			}
			if (!dup) out.push(a);
		}
		return out;
	}

	function addseg(ctx, X, Y, a, b){
		if (!a || !b) return;
		if (Math.abs(a.x - b.x) < 1e-15 && Math.abs(a.y - b.y) < 1e-15) return;
		ctx.moveTo(X(a.x), Y(a.y));
		ctx.lineTo(X(b.x), Y(b.y));
	}

	function draw3(ctx, w, h){
		if (implicit) {
			draw3implicit(ctx, w, h);
			return;
		}
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
		ctx.save();
		ctx.beginPath();
		ctx.rect(0, 0, w, h);
		ctx.clip();
		if (style === "grid") drawGrid(ctx, pts, n, grid);
		else if (style === "dots") drawDots(ctx, pts);
		else if (style === "level") drawLevels(ctx, pts, zraw, n, grid, lo, hi);
		else drawFill(ctx, pts, n, grid);
		drawaxes(ctx, w, h, scale, cyaw, syaw, cp, sp, zmid, zfit, lo, hi);
		ctx.restore();
	}

	// 三维隐式方程 F(x,y,z)=0：按 x、y 网格沿 z 扫过零点，得到曲面点；
	// 再按样式画成填充面、网格线、点阵或水平等高线。
	function draw3implicit(ctx, w, h){
		var fit = Math.max(cam.xspan, cam.yspan);
		var scale = Math.min(w, h) * 0.42 / fit;
		var n = drag ? 46 : (style === "dots" ? 88 : 72);
		var m = drag ? 46 : 80;
		var cyaw = Math.cos(cam.yaw), syaw = Math.sin(cam.yaw);
		var cp = Math.cos(cam.pitch), sp = Math.sin(cam.pitch);
		var cw = n + 1;
		var cols = new Array(cw * cw);
		var z0 = -fit, z1 = fit, dz = (z1 - z0) / m;
		var i, j, k, x, y, z, f0, f1, lo, hi, flo, fhi, zm, fm, zz, X, Y, x1, y1, y2, z2;
		for (j = 0; j <= n; j++) {
			y = cam.cy - cam.yspan + cam.yspan * 2 * j / n;
			for (i = 0; i <= n; i++) {
				x = cam.cx - cam.xspan + cam.xspan * 2 * i / n;
				var list = [];
				f0 = ev(fn, x, y, z0);
				for (k = 1; k <= m; k++) {
					z = z0 + dz * k;
					f1 = ev(fn, x, y, z);
					if (isFinite(f0) && isFinite(f1) && (f0 === 0 || (f1 !== 0 && (f0 > 0) !== (f1 > 0)))) {
						lo = z - dz;
						hi = z;
						flo = f0;
						fhi = f1;
						if (f1 === 0) {
							lo = z;
							hi = z;
						}
						else if (f0 !== 0) {
							for (zm = 0; zm < 10; zm++) {
								var zmid = (lo + hi) / 2;
								fm = ev(fn, x, y, zmid);
								if (!isFinite(fm)) break;
								if ((flo > 0) === (fm > 0)) {
									lo = zmid;
									flo = fm;
								}
								else {
									hi = zmid;
									fhi = fm;
								}
							}
						}
						zz = (lo + hi) / 2;
						X = (x - cam.cx) / cam.xspan * fit;
						Y = (y - cam.cy) / cam.yspan * fit;
						x1 = X * cyaw - Y * syaw;
						y1 = X * syaw + Y * cyaw;
						y2 = y1 * cp - zz * sp;
						z2 = y1 * sp + zz * cp;
						list.push({
							x: w / 2 + x1 * scale,
							y: h / 2 - z2 * scale,
							d: y2,
							t: (zz - z0) / (z1 - z0),
							z: zz
						});
					}
					f0 = f1;
				}
				cols[j * cw + i] = list;
				cols[j * cw + i] = list;
			}
		}
		ctx.save();
		ctx.beginPath();
		ctx.rect(0, 0, w, h);
		ctx.clip();
		var gap = 2 * fit * scale / n;
		if (style === "grid") impgrid(ctx, cols, n, cw, gap);
		else if (style === "dots") impdots(ctx, cols, n, cw, gap);
		else if (style === "level") implevels(ctx, w, h, fit, scale, z0, z1, cyaw, syaw, cp, sp);
		else impfill(ctx, cols, n, cw, gap);
		drawaxes(ctx, w, h, scale, cyaw, syaw, cp, sp, 0, 1, z0, z1);
		ctx.restore();
	}

	// 相邻两列同序号的解离得太远就当作曲面断开了，不连。
	function impnear(a, b, lim){
		var dx = a.x - b.x, dy = a.y - b.y;
		return dx * dx + dy * dy <= lim * lim;
	}

	function impnearpick(list, p, lim){
		var best = null, bd = lim * lim, i, dx, dy, d2;
		for (i = 0; i < list.length; i++) {
			dx = list[i].x - p.x;
			dy = list[i].y - p.y;
			d2 = dx * dx + dy * dy;
			if (d2 < bd) {
				bd = d2;
				best = list[i];
			}
		}
		return best;
	}

	// 所有解点按深度排序后画成实心圆盘，叠起来就是曲面，不会把锥面连成柱面。
	function impfill(ctx, cols, n, cw, gap){
		var pts = [];
		var i, j, r, rad = Math.max(2.4, gap * 1.6);
		for (j = 0; j <= n; j++) {
			for (i = 0; i <= n; i++) {
				var list = cols[j * cw + i];
				for (r = 0; r < list.length; r++) pts.push(list[r]);
			}
		}
		var lower = [], upper = [], k, arr;
		for (i = 0; i < pts.length; i++) (pts[i].z < 0 ? lower : upper).push(pts[i]);
		lower.sort(function(a, b){ return b.d - a.d; });
		upper.sort(function(a, b){ return b.d - a.d; });
		for (k = 0; k < 2; k++) {
			arr = k ? upper : lower;
			for (i = 0; i < arr.length; i++) {
				ctx.fillStyle = zcolor(arr[i].t);
				ctx.beginPath();
				ctx.arc(arr[i].x, arr[i].y, rad, 0, 6.2832);
				ctx.fill();
			}
		}
	}

	function imptri(tris, pts, a, b, c, lim){
		var p0 = pts[a], p1 = pts[b], p2 = pts[c];
		if (!p0 || !p1 || !p2) return;
		if (!impnear(p0, p1, lim) || !impnear(p1, p2, lim) || !impnear(p2, p0, lim)) return;
		tris.push({ p0: p0, p1: p1, p2: p2, d: (p0.d + p1.d + p2.d) / 3 });
	}

	function impgrid(ctx, cols, n, cw, gap){
		var segs = [];
		var lim = gap * 2.4;
		var i, j, r, a, b, p;
		function add(p, q){
			segs.push({ p: p, q: q, d: (p.d + q.d) / 2, t: (p.t + q.t) / 2 });
		}
		for (j = 0; j <= n; j++) {
			for (i = 0; i <= n; i++) {
				a = cols[j * cw + i];
				for (r = 0; r < a.length; r++) {
					p = a[r];
					if (i < n) {
						b = impnearpick(cols[j * cw + i + 1], p, lim);
						if (b) add(p, b);
					}
					if (j < n) {
						b = impnearpick(cols[(j + 1) * cw + i], p, lim);
						if (b) add(p, b);
					}
				}
			}
		}
		segs.sort(function(u, v){ return v.d - u.d; });
		ctx.lineWidth = 1.4;
		ctx.lineJoin = "round";
		ctx.lineCap = "round";
		for (i = 0; i < segs.length; i++) {
			ctx.strokeStyle = zcolor(segs[i].t);
			ctx.beginPath();
			ctx.moveTo(segs[i].p.x, segs[i].p.y);
			ctx.lineTo(segs[i].q.x, segs[i].q.y);
			ctx.stroke();
		}
	}

	function impdots(ctx, cols, n, cw, gap){
		var pts = [];
		var i, j, r;
		for (j = 0; j <= n; j++) {
			for (i = 0; i <= n; i++) {
				var list = cols[j * cw + i];
				for (r = 0; r < list.length; r++) pts.push(list[r]);
			}
		}
		var rad = Math.max(1.5, Math.min(4.2, gap * 0.8));
		pts.sort(function(a, b){ return b.d - a.d; });
		ctx.globalAlpha = 0.92;
		for (i = 0; i < pts.length; i++) {
			ctx.fillStyle = zcolor(pts[i].t);
			ctx.beginPath();
			ctx.arc(pts[i].x, pts[i].y, rad, 0, 6.2832);
			ctx.fill();
		}
		ctx.globalAlpha = 1;
	}

	// 等高线：在每个水平 z 层上对 F(x,y,z)=0 做一次等值线。
	function implevels(ctx, w, h, fit, scale, z0, z1, cyaw, syaw, cp, sp){
		var levels = drag ? 6 : 12;
		var gn = drag ? 30 : 52;
		var segs = [];
		function proj(x, y, z){
			var X = (x - cam.cx) / cam.xspan * fit;
			var Y = (y - cam.cy) / cam.yspan * fit;
			var x1 = X * cyaw - Y * syaw;
			var y1 = X * syaw + Y * cyaw;
			var y2 = y1 * cp - z * sp;
			var z2 = y1 * sp + z * cp;
			return { x: w / 2 + x1 * scale, y: h / 2 - z2 * scale, d: y2 };
		}
		function add(xa, ya, xb, yb, z, t){
			var p = proj(xa, ya, z);
			var q = proj(xb, yb, z);
			segs.push({ p: p, q: q, d: (p.d + q.d) / 2, t: t });
		}
		var L, i, j, zc, t, gw = gn + 1, vals, v00, v10, v11, v01, ps, vc;
		for (L = 1; L < levels; L++) {
			zc = z0 + (z1 - z0) * L / levels;
			t = (zc - z0) / (z1 - z0);
			vals = new Float64Array(gw * gw);
			for (j = 0; j <= gn; j++) {
				var yv = cam.cy - cam.yspan + cam.yspan * 2 * j / gn;
				for (i = 0; i <= gn; i++) {
					var xv = cam.cx - cam.xspan + cam.xspan * 2 * i / gn;
					vals[j * gw + i] = ev(fn, xv, yv, zc);
				}
			}
			for (j = 0; j < gn; j++) {
				var y0 = cam.cy - cam.yspan + cam.yspan * 2 * j / gn;
				var y1 = cam.cy - cam.yspan + cam.yspan * 2 * (j + 1) / gn;
				for (i = 0; i < gn; i++) {
					var x0 = cam.cx - cam.xspan + cam.xspan * 2 * i / gn;
					var x1 = cam.cx - cam.xspan + cam.xspan * 2 * (i + 1) / gn;
					v00 = vals[j * gw + i];
					v10 = vals[j * gw + i + 1];
					v11 = vals[(j + 1) * gw + i + 1];
					v01 = vals[(j + 1) * gw + i];
					if (!isFinite(v00) || !isFinite(v10) || !isFinite(v11) || !isFinite(v01)) continue;
					ps = [];
					if ((v00 > 0) !== (v10 > 0)) ps.push({ x: x0 + (x1 - x0) * (v00 / (v00 - v10)), y: y0 });
					if ((v10 > 0) !== (v11 > 0)) ps.push({ x: x1, y: y0 + (y1 - y0) * (v10 / (v10 - v11)) });
					if ((v11 > 0) !== (v01 > 0)) ps.push({ x: x1 + (x0 - x1) * (v11 / (v11 - v01)), y: y1 });
					if ((v01 > 0) !== (v00 > 0)) ps.push({ x: x0, y: y1 + (y0 - y1) * (v01 / (v01 - v00)) });
					if (ps.length === 2) add(ps[0].x, ps[0].y, ps[1].x, ps[1].y, zc, t);
					else if (ps.length === 4) {
						vc = ev(fn, (x0 + x1) / 2, (y0 + y1) / 2, zc);
						if (!isFinite(vc)) continue;
						if ((vc > 0) === (v00 > 0)) {
							add(ps[0].x, ps[0].y, ps[1].x, ps[1].y, zc, t);
							add(ps[2].x, ps[2].y, ps[3].x, ps[3].y, zc, t);
						}
						else {
							add(ps[0].x, ps[0].y, ps[3].x, ps[3].y, zc, t);
							add(ps[1].x, ps[1].y, ps[2].x, ps[2].y, zc, t);
						}
					}
				}
			}
		}
		segs.sort(function(u, v){ return v.d - u.d; });
		ctx.lineWidth = 1.5;
		ctx.lineJoin = "round";
		ctx.lineCap = "round";
		for (i = 0; i < segs.length; i++) {
			ctx.strokeStyle = zcolor(segs[i].t);
			ctx.beginPath();
			ctx.moveTo(segs[i].p.x, segs[i].p.y);
			ctx.lineTo(segs[i].q.x, segs[i].q.y);
			ctx.stroke();
		}
	}

	function drawFill(ctx, pts, n, grid){
		var tris = [];
		var i, j, a, b, c, d, p0, p1, p2;
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
	}

	function drawGrid(ctx, pts, n, grid){
		var segs = [];
		var i, j, a;
		for (j = 0; j < grid; j++) {
			for (i = 0; i < grid; i++) {
				a = j * grid + i;
				if (i + 1 < grid) pushseg(segs, pts, a, a + 1);
				if (j + 1 < grid) pushseg(segs, pts, a, a + grid);
			}
		}
		strokeSegs(ctx, segs, 1);
	}

	function drawDots(ctx, pts){
		var list = [];
		var i, p;
		for (i = 0; i < pts.length; i++) if (pts[i].ok) list.push(pts[i]);
		list.sort(function(a, b){ return b.d - a.d; });
		for (i = 0; i < list.length; i++) {
			p = list[i];
			ctx.fillStyle = zcolor(p.t);
			ctx.beginPath();
			ctx.arc(p.x, p.y, 2.2, 0, 6.2832);
			ctx.fill();
		}
	}

	function drawLevels(ctx, pts, zraw, n, grid, lo, hi){
		var bands = 8;
		var segs = [];
		var k, level, i, j, a, z00, z10, z11, z01, p00, p10, p11, p01, e0, e1, e2, e3, hit;
		var span = hi - lo;
		if (!(span > 0)) return;
		for (k = 1; k < bands; k++) {
			level = lo + span * k / bands;
			for (j = 0; j < n; j++) {
				for (i = 0; i < n; i++) {
					a = j * grid + i;
					z00 = zraw[a];
					z10 = zraw[a + 1];
					z01 = zraw[a + grid];
					z11 = zraw[a + grid + 1];
					p00 = pts[a];
					p10 = pts[a + 1];
					p01 = pts[a + grid];
					p11 = pts[a + grid + 1];
					e0 = crossZ(p00, p10, z00, z10, level);
					e1 = crossZ(p10, p11, z10, z11, level);
					e2 = crossZ(p11, p01, z11, z01, level);
					e3 = crossZ(p01, p00, z01, z00, level);
					hit = [];
					if (e0) hit.push(e0);
					if (e1) hit.push(e1);
					if (e2) hit.push(e2);
					if (e3) hit.push(e3);
					if (hit.length === 4) {
						var zc = (z00 + z10 + z11 + z01) / 4;
						if ((zc >= level) === (z00 >= level)) {
							pushLevel(segs, e0, e1, level, lo, span);
							pushLevel(segs, e2, e3, level, lo, span);
						}
						else {
							pushLevel(segs, e0, e3, level, lo, span);
							pushLevel(segs, e1, e2, level, lo, span);
						}
					}
					else if (hit.length === 2) pushLevel(segs, hit[0], hit[1], level, lo, span);
				}
			}
		}
		strokeSegs(ctx, segs, 1.6);
	}

	function pushLevel(segs, p, q, level, lo, span){
		segs.push({ p: p, q: q, d: (p.d + q.d) / 2, t: (level - lo) / span });
	}

	function crossZ(p, q, z0, z1, level){
		if (!p.ok || !q.ok || !isFinite(z0) || !isFinite(z1)) return null;
		if ((z0 >= level) === (z1 >= level)) return null;
		var t = (level - z0) / (z1 - z0);
		if (!(t >= 0 && t <= 1)) return null;
		return {
			x: p.x + (q.x - p.x) * t,
			y: p.y + (q.y - p.y) * t,
			d: p.d + (q.d - p.d) * t,
			t: p.t + (q.t - p.t) * t
		};
	}

	function pushseg(segs, pts, a, b){
		var p = pts[a], q = pts[b];
		if (!p.ok || !q.ok) return;
		segs.push({ p: p, q: q, d: (p.d + q.d) / 2, t: (p.t + q.t) / 2 });
	}

	function strokeSegs(ctx, segs, width){
		var i, s;
		segs.sort(function(a, b){ return b.d - a.d; });
		ctx.lineWidth = width;
		ctx.lineJoin = "round";
		ctx.lineCap = "round";
		for (i = 0; i < segs.length; i++) {
			s = segs[i];
			ctx.strokeStyle = zcolor(s.t);
			ctx.beginPath();
			ctx.moveTo(s.p.x, s.p.y);
			ctx.lineTo(s.q.x, s.q.y);
			ctx.stroke();
		}
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
		var tree = relation();
		if (i !== s.length) throw new Error("公式里有无法识别的部分");
		return tree;
		function relation(){
			var left = expr();
			if (peek() !== "=") return left;
			i++;
			if (peek() === "=") i++;
			var right = expr();
			if (peek() === "=") throw new Error("公式里有无法识别的部分");
			return { t: "eq", a: left, b: right };
		}
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
				if (dim < 2) throw new Error(tt("web.plot.xonly", "二维函数只能使用 x。要画圆、椭圆这类图，请写成等式，例如 x^2+y^2=1。"));
				return { t: "v", i: 1 };
			}
			if (name === "z") {
				if (dim < 3) throw new Error(tt("web.plot.noz", "三维曲面只能使用 x、y；要写含 z 的方程，请用 F(x,y,z)=0 的等式。"));
				return { t: "v", i: 2 };
			}
			throw new Error("没有这个名字");
		}
		function splitknown(word){
			var n;
			for (n = word.length; n > 0; n--) {
				var p = word.slice(0, n);
				if (ARITY[p] != null || p === "pi" || p === "e" || p === "x" || p === "y" || (dim >= 3 && p === "z")) return n;
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

	function ev(node, x, y, z){
		if (!node) return NaN;
		if (node.t === "n") return node.v;
		if (node.t === "v") return node.i === 2 ? z : node.i ? y : x;
		if (node.t === "u") return -ev(node.a, x, y, z);
		if (node.t === "eq") return ev(node.a, x, y, z) - ev(node.b, x, y, z);
		if (node.t === "b") {
			var a = ev(node.a, x, y, z);
			var b = ev(node.b, x, y, z);
			if (node.op === "+") return a + b;
			if (node.op === "-") return a - b;
			if (node.op === "*") return a * b;
			if (node.op === "/") return b === 0 ? NaN : a / b;
			if (node.op === "%") return b === 0 ? NaN : a % b;
			return Math.pow(a, b);
		}
		var args = [];
		var i;
		for (i = 0; i < node.args.length; i++) args.push(ev(node.args[i], x, y, z));
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

	function usesY(node){
		if (!node) return false;
		if (node.t === "v") return node.i === 1;
		if (node.t === "u" || node.t === "eq") return usesY(node.a) || usesY(node.b);
		if (node.t === "b") return usesY(node.a) || usesY(node.b);
		if (node.t === "f") {
			var i;
			for (i = 0; i < node.args.length; i++) if (usesY(node.args[i])) return true;
		}
		return false;
	}

	function isletter(c){ return (c >= "a" && c <= "z") || (c >= "A" && c <= "Z"); }
	function isdigit(c){ return c >= "0" && c <= "9"; }
})();
