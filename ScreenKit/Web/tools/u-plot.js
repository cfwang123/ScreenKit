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
		{ g: "eq", mode: "2", fn: "x^3+y^3=3*x*y" },
		{ g: "eq", mode: "2", fn: "(x^2+y^2)^2=2*(x^2-y^2)" },
		{ g: "eq", mode: "2", fn: "(x^2+y^2)^3=4*x^2*y^2" },
		{ g: "eq", mode: "2", fn: "(x^2)^(1/3)+(y^2)^(1/3)=1" },
		{ g: "eq", mode: "2", fn: "x^2+(y-(x^2)^(1/3))^2=1" },
		{ g: "eq", mode: "2", fn: "(x^2+y^2-1)^3=x^2*y^3" },
		{ g: "z", mode: "3", fn: "sin(x)*cos(y)" },
		{ g: "z", mode: "3", fn: "x^2-y^2" },
		{ g: "z", mode: "3", fn: "sin(sqrt(x^2+y^2))" },
		{ g: "z", mode: "3", fn: "exp(-(x^2+y^2))" },
		{ g: "z", mode: "3", fn: "x^3-3*x*y^2" },
		{ g: "z", mode: "3", fn: "sin(x^2+y^2)" },
		{ g: "eq3", mode: "3", fn: "x^2+y^2+z^2=9" },
		{ g: "eq3", mode: "3", fn: "x^2/4+y^2/9+z^2=1" },
		{ g: "eq3", mode: "3", fn: "x^2+y^2-z^2=1" },
		{ g: "eq3", mode: "3", fn: "x^2+y^2=z^2" },
		{ g: "eq3", mode: "3", fn: "x+y+z=3" },
		{ g: "eq3", mode: "3", fn: "(x^2+y^2+z^2+3)^2=16*(x^2+y^2)" },
		{ g: "eq3", mode: "3", fn: "x^2+y^2-z^2=-1" },
		{ g: "eq3", mode: "3", fn: "(x^2+9/4*y^2+z^2-1)^3-x^2*z^3-9/80*y^2*z^3=0" }
	];
	var cam = { cx: 0, cy: 0, xspan: 4, yspan: 4, yaw: -0.6, pitch: 0.65, zoom: 1 };
	var HOME = { yaw: -0.6, pitch: 0.65 };
	var YSPAN = 4;
	// 隐式曲面的格宽倍数。球、双曲面这类每一列都要单独扫 z 求根，网格再密就看不清面了，
	// 所以格宽取显式曲面的两倍，也就是密度减半。
	var IMPDENS = 2;
	// 网格行列数下限。缩到很小时曲面在屏幕上只剩一两百像素，按固定格宽算出来的行列数
	// 会掉到十几根，网格稀疏到看不出形状，所以给个保底数量。
	var MINROWS = 40;
	var MINNX = 40;
	var view3 = { s: 1, ox: 0, oy: 0 };
	var canvas = null;
	var watch = null;
	var drag = null;
	// 拖动中先画粗样本，松手后再画细的。缓存键要看这个，否则松手后还会用拖动时那张图。
	var dragging = false;
	// 滚轮连续缩放时先按低精度出图，停手后再补一张细的，滚轮才不会排队卡住。
	var live = false;
	var liveid = 0;
	var livetimer = 0;
	function quick(){ return dragging || live; }
	// 一帧只画一次：滚轮事件比屏幕刷新快，逐条 paint 会越积越多。
	function kick(){
		live = true;
		if (!liveid) liveid = F.raf(function(){ liveid = 0; paint(); });
		// 走工具页的全局心跳，不用自己的 setTimeout：离开工具时框架会把这些挂起的任务一起清掉，
		// 否则停手补画会落在已经销毁的画布上。
		if (livetimer) SK.cancelafter(livetimer);
		livetimer = SK.after(settle, 160);
	}
	function settle(){
		livetimer = 0;
		if (!live) return;
		live = false;
		paint();
	}
	// 上一次定稿时曲面占的范围，拖动时接着用，免得一拖就退回整片范围、线突然变稀。
	var imparea = null;
	var impzr = null;
	// 适配倍率 / 基准倍率，fit3 里更新。隐式曲面用它估屏幕上的实际大小。
	var fitscaleRatio = 1;
	// 适配倍率（zoom=1 时的值）。只在复位、换公式、改尺寸时重算，
	// 转视角、平移时沿用同一个倍率，画面就不会自己忽大忽小。
	var fitbase = 0;
	var reffit = true;
	// fit3 刚刷新了 fitbase：网格行数得按新倍率再算一遍，否则行数还停在旧倍率上。
	var refitAgain = false;
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
		open: open,
		dispose: dispose
	});

	function tt(key, fb){
		return SK.t(key) || fb;
	}

	function body(t){
		return F.CreateDOM(t, { x: "div.plot", c: [
			F.CreateDOM(t, { x: "div.plot-bar", c: [
				F.CreateDOM(t, { x: "button", type: "button", class: "on", "data-act": "cross", "data-ktip": "web.plot.crossTip",
					title: tt("web.plot.crossTip", "竖线与曲线的交点。再点一次关闭。"), c: [
						F.CreateDOM(t, { x: "i.fa-solid.fa-crosshairs" }),
						F.CreateDOM(t, { x: "span", "data-k": "web.plot.cross", c: tt("web.plot.cross", "Cross") })
					] }),
				F.CreateDOM(t, { x: "span.plot-style.plot-seg", hidden: true, c: [
					styleBtn("fill", "fa-chart-area", "web.plot.fill", "曲面"),
					styleBtn("grid", "fa-border-all", "web.plot.grid", "网格"),
					styleBtn("dots", "fa-braille", "web.plot.dots", "点阵"),
					styleBtn("level", "fa-layer-group", "web.plot.level", "等高线")
				] }),
				F.CreateDOM(t, { x: "input#plot-fn", type: "text", value: "sin(x)", spellcheck: "false", autocomplete: "off",
					"data-kph": "web.plot.ph", placeholder: tt("web.plot.ph", "sin(x)"),
					"data-ktip": "web.plot.tip", title: tt("web.plot.tip", "支持 + - * / ^、括号、sin cos tan sqrt abs ln log exp，常数 pi 和 e，可以写 2x。自动识别函数和方程：y=f(x)、z=f(x,y)、x^2+y^2=1 这类等式，以及 x^2+y^2+z^2=9 这类含 z 的方程。") }),
				F.CreateDOM(t, { x: "div.plot-ex", c: [
					F.CreateDOM(t, { x: "button", type: "button", "data-act": "ex", c: [
						F.CreateDOM(t, { x: "i.fa-solid.fa-caret-down" }),
						F.CreateDOM(t, { x: "span", "data-k": "web.plot.ex", c: tt("web.plot.ex", "示例") })
					] }),
					F.CreateDOM(t, { x: "div#plot-menu.plot-menu", hidden: true, c: samples() })
				] }),
				F.CreateDOM(t, { x: "button", type: "button", "data-act": "reset", "data-k": "web.plot.reset", c: tt("web.plot.reset", "复位") })
			] }),
			F.CreateDOM(t, { x: "p.hint", "data-k": "web.plot.hint", c: tt("web.plot.hint", "拖动平移，滚轮缩放。按住 Ctrl 只缩放 x 轴，按住 Shift 只缩放 y 轴。三维时拖动转动视角，Shift 拖动平移。三角函数用弧度。双击恢复初始视图。输入会自动识别二维或三维：函数、方程、含 z 的曲面都能画。点「示例」可选择。") }),
			F.CreateDOM(t, { x: "div.plot-stage", c: [
				F.CreateDOM(t, { x: "canvas#plot-cv" }),
				F.CreateDOM(t, { x: "div#plot-read.plot-read", hidden: true })
			] }),
			F.CreateDOM(t, { x: "p#plot-msg.hint" })
		] });
	}

	function open(){
		canvas = SK.dom("plot-cv");
		if (!canvas) return;
		var bar = F.q(".plot-bar");
		if (bar) bar.onclick = onbar;
		var input = SK.dom("plot-fn");
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
			watch = new ResizeObserver(function(){
				if (mode === "3") {
					var aspect = stageaspect();
					// 换尺寸后 yaw、pitch 都跟着变，采样宽度要按新的重算，否则左右还是空。
					cam.pitch = pitchfor(aspect);
					cam.yaw = yawfor(aspect, cam.pitch);
					// 缩放关系是 xspan*zoom = spanfor、yspan*zoom = YSPAN。
					// 这里只改 xspan、不按同一个 zoom 改 yspan，两个方向就会差出十几倍，
					// 曲面被压扁、格子跟着变形，看上去就是「密度不对 / 整片空白」。
					var z = cam.zoom > 1e-9 ? cam.zoom : 1;
					cam.xspan = spanfor(aspect, cam.yaw, cam.pitch) / z;
					cam.yspan = YSPAN / z;
					reffit = true;
				}
				paint();
			});
			watch.observe(canvas.parentElement);
		}
		markmode();
		markstyle();
		resetview();
		reparse();
		F.raf(paint);
	}

	// 离开工具时框架会把面板 DOM 清掉，这里把还挂在外面的东西一并收掉：
	// 尺寸监听的宿主节点已经随面板移除，不停下来会继续对着脱落的画布重绘；
	// 挂起的补画任务由框架统一清，这里只需要把本地句柄复位。
	function dispose(){
		if (watch) {
			watch.disconnect();
			watch = null;
		}
		if (liveid) {
			cancelAnimationFrame(liveid);
			liveid = 0;
		}
		livetimer = 0;
		live = false;
		dragging = false;
		drag = null;
		plate = null;
		plateKey = "";
		hover = null;
		canvas = null;
	}


	function styleBtn(id, icon, key, fb){
		return F.CreateDOM(null, { x: "button", type: "button", "data-style": id,
			class: id === style ? "on" : "", c: [
				F.CreateDOM(null, { x: "i", class: "fa-solid " + icon }),
				F.CreateDOM(null, { x: "span", "data-k": key, c: tt(key, fb) })
			] });
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
			pick(sample);
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
	}

	function samples(){
		var out = [];
		var g = "";
		var i, row, key, fb;
		for (i = 0; i < SAMPLES.length; i++) {
			row = SAMPLES[i];
			if (row.g !== g) {
				g = row.g;
				key = g === "eq" ? "web.plot.exeq" : g === "eq3" ? "web.plot.exeq3" : g === "z" ? "web.plot.z" : "web.plot.y";
				fb = g === "eq" ? "方程" : g === "eq3" ? "三维方程" : g === "z" ? "z = f(x, y)" : "y = f(x)";
				out.push(F.CreateDOM(null, { x: "p", "data-k": key, c: tt(key, fb) }));
			}
			out.push(F.CreateDOM(null, { x: "button", type: "button", "data-ex": row.fn, "data-mode": row.mode, c: row.fn }));
		}
		return out;
	}

	function togglemenu(){
		var menu = SK.dom("plot-menu");
		if (!menu) return;
		menu.hidden = !menu.hidden;
	}

	function hidemenu(){
		var menu = SK.dom("plot-menu");
		if (menu) menu.hidden = true;
	}

	function ondoc(ev){
		var menu = SK.dom("plot-menu");
		if (!menu || menu.hidden) return;
		if (ev.target && ev.target.closest && ev.target.closest(".plot-ex")) return;
		menu.hidden = true;
	}

	function pick(src){
		var input = SK.dom("plot-fn");
		if (input) input.value = src;
		hidemenu();
		resetview();
		reparse();
		paint();
	}

	function markmode(){
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
		var el = SK.dom("plot-msg");
		if (!el) return;
		el.textContent = text || "";
		el.className = bad ? "hint bad" : "hint";
	}

	// 采样平面按画布宽高比张开，x、y 方向都铺到画布边缘。
	function stageaspect(){
		var stage = canvas && canvas.parentElement;
		var w = stage && stage.clientWidth > 2 ? stage.clientWidth : 800;
		var h = stage && stage.clientHeight > 2 ? stage.clientHeight : 420;
		var r = w / h;
		if (!(r > 0)) return 1;
		if (r < 0.25) return 0.25;
		return r > 4 ? 4 : r;
	}

	// 画布越宽，视角越往正前方收。
	// 对 z=f(x,y) 这种带状曲面，yaw 只让屏幕上的带子倾斜——倾斜的斜带子盖不住矩形画布的四角，
	// 一头翘起来那块就永远空着。立体感由 pitch 提供就够了，所以宽画布上把 yaw 收到几乎正对。
	// 屏幕上看到的倾斜量约为 tan(yaw)*sin(pitch)，这里直接给它设个上限。
	function yawfor(aspect, pitch){
		var yaw = Math.atan(Math.tan(HOME.yaw) * Math.min(1, 0.55 / Math.max(0.001, aspect)));
		var sp = Math.abs(Math.sin(pitch === undefined ? HOME.pitch : pitch));
		if (sp < 0.08) sp = 0.08;
		var lim = Math.atan(0.045 / sp);
		if (Math.abs(yaw) > lim) yaw = yaw < 0 ? -lim : lim;
		return yaw;
	}

	// 画布越宽，视角越抬高。
	// 俯角越大，采样矩形投到屏幕上越接近画布的矩形，四角才填得满；
	// 俯角小时采样矩形被压成一条斜带，四周必然大块空白。
	function pitchfor(aspect){
		var p = HOME.pitch + 0.42 * (Math.max(1, aspect) - 1);
		if (p > 1.15) p = 1.15;
		return p;
	}

	// 按画布宽高比反解采样半宽：把取样矩形投到屏幕上，让它的宽高比等于画布宽高比。
	// 这样曲面才会真的铺满，而不是上下满了、左右各空一大块。
	function spanfor(aspect, yaw, pitch){
		var Y = YSPAN;
		var c = Math.abs(Math.cos(yaw));
		var s = Math.abs(Math.sin(yaw));
		var sp = Math.abs(Math.sin(pitch === undefined ? HOME.pitch : pitch));
		var den = c - aspect * s * sp;
		var x;
		if (den > 0.02) x = Y * (aspect * c * sp - s) / den;
		else x = Y * aspect;
		if (!(x > Y * 0.5)) x = Y * 0.5;
		if (x > Y * 12) x = Y * 12;
		return x;
	}

	function resetview(){
		if (mode === "3") {
			var aspect = stageaspect();
			var pitch = pitchfor(aspect);
			var yaw = yawfor(aspect, pitch);
			cam.cx = 0;
			cam.cy = 0;
			cam.yspan = YSPAN;
			cam.yaw = yaw;
			cam.xspan = spanfor(aspect, yaw, pitch);
			cam.zoom = 1;
			cam.pitch = pitch;
			reffit = true;
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
		var input = SK.dom("plot-fn");
		var src = input ? input.value : "";
		implicit = false;
		imparea = null;
		impzr = null;
		// 换公式要按新曲面重新适配一次倍率。
		reffit = true;
		try {
			var norm = normalize(src);
			var zpre = /^\s*z\s*=/i.test(src);
			var eq = norm.indexOf("=") >= 0;
			var next;
			if (zpre) {
				fn = compile(src, 2);
				next = "3";
			}
			else if (eq) {
				fn = compile(src, 3);
				next = usesZ(fn) ? "3" : "2";
			}
			else {
				fn = compile(src, 3);
				if (usesZ(fn)) {
					fn = null;
					say(tt("web.plot.noz", "三维曲面只能使用 x、y；要写含 z 的方程，请用 F(x,y,z)=0 的等式。"), true);
					return;
				}
				next = usesY(fn) ? "3" : "2";
			}
			implicit = fn.t === "eq";
			if (next !== mode) {
				mode = next;
				markmode();
				resetview();
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
			// 屏幕裁剪框，(0,0)-(w,h) 是画布可见区
			clipbox.x0 = 0;
			clipbox.y0 = 0;
			clipbox.x1 = w;
			clipbox.y1 = h;
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
		var input = SK.dom("plot-fn");
		var src = input ? input.value : "";
		if (mode === "3")
			return "3|" + style + "|" + src + "|" + w + "x" + h + "|" + cam.cx + "," + cam.cy + "," + cam.xspan + "," + cam.yspan + "," + cam.yaw + "," + cam.pitch + "," + cam.zoom + (quick() ? "|d" : "");
		return "2|" + src + "|" + w + "x" + h + "|" + view.x0 + "," + view.x1 + "," + view.y0 + "," + view.y1 + (quick() ? "|d" : "");
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
		dragging = true;
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
		kick();
	}

	function up(){
		var was = drag && (implicit || crossOn);
		drag = null;
		dragging = false;
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
			// 缩放改采样范围本身，不复用倍率：
			// 采样范围一变，fit3 会把新的投影重新铺满画布，四周就不会越缩越空。
			zoomspan(factor);
			kick();
			return;
		}
		if (!yonly) {
			// 以世界原点为锚：原点在屏幕上的位置缩放前后不变，不会跟着鼠标漂走。
			var nx0 = view.x0 * factor;
			var nx1 = view.x1 * factor;
			if (nx1 - nx0 >= 1e-6 && nx1 - nx0 <= 1e6) {
				view.x0 = nx0;
				view.x1 = nx1;
			}
		}
		if (!xonly) {
			var ny0 = view.y0 * factor;
			var ny1 = view.y1 * factor;
			if (ny1 - ny0 >= 1e-6 && ny1 - ny0 <= 1e6) {
				view.y0 = ny0;
				view.y1 = ny1;
			}
		}
		kick();
	}

	// 三维缩放：倍率和采样范围一起按同一比例变。
	// 缩小 -> 倍率变小、采样范围变大：无限延伸的曲面照样铺满，不会缩成中间一小块。
	// 放大 -> 倍率变大、采样范围变小：球这种有边界的图形会真的放大，而不是原地重新采样。
	// 视角和适配基准都不动，只改这两项。
	function zoomspan(k){
		if (!(k > 0) || !isFinite(k)) return;
		var xs = cam.xspan * k, ys = cam.yspan * k;
		if (!(xs > 1e-4) || !(ys > 1e-4) || xs > 1e6 || ys > 1e6) return;
		var z = cam.zoom / k;
		// 上下限判断必须在改 cam 之前做，而且要和 setzoom 的夹取范围一致。
		// 否则 zoom 被夹在 0.05 不动、xspan 却继续变大：缩放一路到底再退回来，
		// xspan 回到原样而 zoom 涨了一大截，倍率对不上，画面就整个炸到画布外。
		if (!(z >= ZMIN) || z > ZMAX) return;
		// 以世界原点为锚：原点在屏幕上的位置缩放前后不变，
		// 也就是画面中心始终对着 (0,0,0)，不会跟着鼠标漂走。
		cam.xspan = xs;
		cam.yspan = ys;
		setzoom(z);
	}

	// 缩放上下限。zoomspan 和 setzoom 必须用同一对值，
	// 一处放行、一处夹取，就会让 xspan 与 zoom 失去同步。
	var ZMIN = 0.05, ZMAX = 20;

	function setzoom(v){
		if (!isFinite(v) || !(v > 0)) return;
		if (v < ZMIN) v = ZMIN;
		if (v > ZMAX) v = ZMAX;
		cam.zoom = v;
	}

	function hideRead(){
		var el = SK.dom("plot-read");
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
		var box = SK.dom("plot-read");
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

	// 一个世界单位在屏幕上占多少像素（不含缩放倍率）。缩放只改倍率，采样范围不变。
	function viewbase(w, h){
		var span = Math.max(0.001, Math.max(cam.xspan, cam.yspan));
		var s = Math.min(w, h) * 0.92 / (span * 2);
		return s > 0 && isFinite(s) ? s : 1;
	}

	function viewscale(w, h){
		return viewbase(w, h) * cam.zoom;
	}

	// 按曲面的实际投影范围定倍率，两个方向都铺到画布边缘。
	// 以前按采样范围的最小边定倍率，画布是宽的，x、y 方向就都留出大片空白。
	// 有真实解点时直接量点（球面比包围盒小一圈，量点才贴边），没有时才退回包围盒。
	// 返回 { s: 倍率, u: 水平中心, v: 竖直中心 }。
	function fitrect(w, h, box, list, cyaw, syaw, cp, sp){
		var u0 = Infinity, u1 = -Infinity, v0 = Infinity, v1 = -Infinity;
		var i, X, Y, Z, u, v;
		if (list && list.length) {
			for (i = 0; i < list.length; i++) {
				if (list[i].ok === false) continue;
				u = list[i].ux;
				v = list[i].uz;
				if (!isFinite(u) || !isFinite(v)) continue;
				if (u < u0) u0 = u;
				if (u > u1) u1 = u;
				if (v < v0) v0 = v;
				if (v > v1) v1 = v;
			}
		}
		if (!isFinite(u0) && box && isFinite(box.x0) && isFinite(box.y0) && isFinite(box.z0)) {
			var xs = [box.x0, box.x1], ys = [box.y0, box.y1], zs = [box.z0, box.z1];
			var j, k;
			for (i = 0; i < 2; i++) {
				for (j = 0; j < 2; j++) {
					for (k = 0; k < 2; k++) {
						X = xs[i];
						Y = ys[j];
						Z = zs[k];
						u = X * cyaw - Y * syaw;
						v = (X * syaw + Y * cyaw) * sp + Z * cp;
						if (u < u0) u0 = u;
						if (u > u1) u1 = u;
						if (v < v0) v0 = v;
						if (v > v1) v1 = v;
					}
				}
			}
		}
		// 画面中心固定对着坐标原点，所以倍率要按「到原点的最大距离」算，
		// 也就是把投影范围对称到原点两边再取尺寸。不然圆心不在原点的曲面
		// （如 x^2+y^2=9 圆心在原点没问题，但心形、碗底偏下的都不对称）
		// 会按包围盒尺寸算倍率、再以原点居中，结果整体偏出画布。
		var du = 2 * Math.max(Math.abs(u0), Math.abs(u1));
		var dv = 2 * Math.max(Math.abs(v0), Math.abs(v1));
		var s = 0;
		if (du > 1e-9) s = w * 0.94 / du;
		if (dv > 1e-9) {
			var sv = h * 0.94 / dv;
			if (!(s > 0) || sv < s) s = sv;
		}
		if (!(s > 0) || !isFinite(s)) s = viewbase(w, h);
		s *= cam.zoom;
		if (!(s > 1e-6)) s = 1e-6;
		if (s > 1e6) s = 1e6;
		// 居中锚点就是原点，这里不再返回包围盒中心。
		return { s: s, u: 0, v: 0 };
	}

	// z=f(x,y) 这种带状曲面：内接时上下、左右各贴一点边，但曲面是起伏的，
	// 四角处凹进去，看起来就是四块空。让它稍微溢出画布、由 clip 切掉，四角才填得住。
	// 但起伏很陡的曲面（如 x^2-y^2）本来纵向就顶到边，再放大就把尖峰切平了，所以按陡峭程度决定放多少。
	// 隐式闭曲面（球、心形）不能裁，裁了就缺一块。
	function bandover(lo, hi){
		var span = Math.max(1e-6, Math.abs(hi - lo));
		var flat = span / Math.max(1e-6, cam.yspan * 2);
		if (flat > 0.55) return 1;
		return flat > 0.3 ? 1.05 : 1.10;
	}

	function fit3(w, h, box, list, cyaw, syaw, cp, sp, over){
		var fit = fitrect(w, h, box, list, cyaw, syaw, cp, sp);
		// 只有复位、换公式、改尺寸时才重算基准倍率。
		// 转视角时 fitrect 量到的投影范围一直在变，跟着重算画面就会自己忽大忽小。
		var zoom = cam.zoom > 1e-9 ? cam.zoom : 1;
		var base0 = viewbase(w, h);
		if (reffit || !(fitbase > 0)) {
			var raw = fit.s * (over || 1) / zoom;
			// fitrect 量的是「世界单位 → 像素」的绝对比例，不含 cam.xspan，
			// 所以它除以 zoom 就是与缩放无关的基准倍率，直接取用。
			// 只有量不到解点、退回按采样范围估的退化值时才不可信（那会随 xspan 一起变），
			// 这种时候保留原基准，别把基准写坏——基准此后只在复位、换公式、改尺寸时才重算。
			if (!isFinite(raw) || !(raw > 1e-6)) raw = fitbase > 0 ? fitbase : base0;
			if (!(fitbase > 0) || raw !== fitbase) refitAgain = true;
			fitbase = raw;
			reffit = false;
		}
		var s = fitbase * zoom;
		view3.s = s;
		// 记下适配倍率与基准倍率的比值，隐式曲面据此估自己在屏幕上占多大，网格线才疏密合适。
		fitscaleRatio = base0 > 1e-9 ? s / (base0 * zoom) : 1;
		if (!isFinite(fitscaleRatio) || fitscaleRatio < 0.01) fitscaleRatio = 0.01;
		if (fitscaleRatio > 20) fitscaleRatio = 20;
		// 画面中心对准坐标原点（投影里的 u=0、v=0）。
		// 不能按 fitrect 量到的包围盒中心去居中：那个中心随可见解点变化，
		// 一缩放曲面还没变、可见范围先变了，原点就会跟着漂。原点固定在中心，
		// 放大缩小时才像是围着原点在缩放。平移量 cam.cx / cam.cy 已经在投影里减掉了，
		// 所以拖动时画面照常跟着走。
		view3.ox = w / 2;
		view3.oy = h / 2;
	}

	function boxone(box, x, y, z){
		if (!isFinite(x) || !isFinite(y) || !isFinite(z)) return;
		if (x < box.x0) box.x0 = x;
		if (x > box.x1) box.x1 = x;
		if (y < box.y0) box.y0 = y;
		if (y > box.y1) box.y1 = y;
		if (z < box.z0) box.z0 = z;
		if (z > box.z1) box.z1 = z;
	}

	function newbox(){
		return { x0: Infinity, x1: -Infinity, y0: Infinity, y1: -Infinity, z0: Infinity, z1: -Infinity };
	}

	// 三维点投影：x、y、z 直接按世界单位（x/y 和 z 同一把尺，不按采样范围拉伸），
	// 再按 yaw 旋转、pitch 俯仰。返回 view3 缩放前的 ux、uz、深度 d 和归一后的三维坐标。
	function proj3(x, y, z, cyaw, syaw, cp, sp, zmid, zfit){
		var X = x - cam.cx;
		var Y = y - cam.cy;
		var Z = (z - zmid) * zfit;
		var x1 = X * cyaw - Y * syaw;
		var y1 = X * syaw + Y * cyaw;
		return { ux: x1, uz: y1 * sp + Z * cp, d: y1 * cp - Z * sp, wx: X, wy: Y, wz: Z };
	}

	function scr3(p){
		p.x = view3.ox + p.ux * view3.s;
		p.y = view3.oy - p.uz * view3.s;
		return p;
	}

	// 世界坐标里 x、y 各走一个单位，在屏幕上是多长。
	// 投影把 x、y 压扁、斜切，两者的屏幕长度并不相等；不补这个，转视角时格子就会一边稀一边密。
	function projaxis(){
		var cyaw = Math.cos(cam.yaw), syaw = Math.sin(cam.yaw);
		var sp = Math.sin(cam.pitch);
		var ax = Math.sqrt(cyaw * cyaw + syaw * syaw * sp * sp);
		var ay = Math.sqrt(syaw * syaw + cyaw * cyaw * sp * sp);
		if (!(ax > 1e-6)) ax = 1e-6;
		if (!(ay > 1e-6)) ay = 1e-6;
		return { ax: ax, ay: ay };
	}

	// 网格线数固定，不跟着缩放增减，屏幕上的格子密度才不变。
	// 列数按 x 方向在屏幕上的实际宽度独立算，和行数用同一个像素格宽，格子才方正。
	function gridcount(rows, xs, ys, cell){
		var ax = projaxis();
		if (!(xs > 0)) xs = cam.xspan;
		if (!(ys > 0)) ys = cam.yspan;
		if (!(cell > 0)) cell = gridcell();
		var nx = Math.round((xs * 2 * ax.ax) * view3safes() / cell);
		if (!(nx >= MINNX)) nx = MINNX;
		if (nx > rows * 8) nx = rows * 8;
		return nx;
	}

	function gridcell(){
		return style === "dots" ? 5 : style === "fill" ? 9 : 7;
	}

	function view3safes(){
		return view3.s > 0 ? view3.s : 1;
	}

	// 显式曲面的网格行数：按 y 方向在屏幕上的实际高度除以固定像素格宽。
	// 用上一帧的 view3.s 先估一个，fit3 出新倍率后若差得多就按新倍率重画一次。
	function surfrows(w, h){
		var ax = projaxis();
		var s = view3safes();
		var rows = Math.round((cam.yspan * 2 * ax.ay) * s / gridcell());
		// 快速档只压 z 扫描和预算，网格线数保持不动：一改行数拖动时面貌似换了个粗糙模型，
		// 松手再变密，观感比稍慢一点更差。
		if (!(rows >= MINROWS)) rows = MINROWS;
		if (rows > 140) rows = 140;
		return rows;
	}

	function draw3(ctx, w, h){
		if (implicit) {
			draw3implicit(ctx, w, h);
			return;
		}
		// 行数用上一帧的倍率估，换公式、改尺寸时倍率会变，按新倍率重画一次就对齐了。
		var pass = 0;
		while (true) {
			refitAgain = false;
			draw3band(ctx, w, h);
			if (!refitAgain || pass++ >= 1) return;
		}
	}

	function draw3band(ctx, w, h){
		// 网格线数按投影到屏幕上的大小来定，屏幕上的格子像素间距才恒定：
		// 放大就多画几条、缩小就少画几条，转视角时格子也不会一边挤一边松。
		// 曲面和等高线用少一点，渐变就够细了。
		var ny = surfrows(w, h);
		var nx = gridcount(ny);
		var gw = nx + 1, gh = ny + 1;
		var zraw = new Array(gw * gh);
		var i, j, x, y, z, finite = [];
		for (j = 0; j < gh; j++) {
			y = cam.cy - cam.yspan + cam.yspan * 2 * j / ny;
			for (i = 0; i < gw; i++) {
				x = cam.cx - cam.xspan + cam.xspan * 2 * i / nx;
				z = fn ? ev(fn, x, y) : NaN;
				zraw[j * gw + i] = z;
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
		var zfit = 1;
		var pts = new Array(gw * gh);
		var box = newbox();
		var cyaw = Math.cos(cam.yaw), syaw = Math.sin(cam.yaw);
		var cp = Math.cos(cam.pitch), sp = Math.sin(cam.pitch);
		for (j = 0; j < gh; j++) {
			y = cam.cy - cam.yspan + cam.yspan * 2 * j / ny;
			for (i = 0; i < gw; i++) {
				x = cam.cx - cam.xspan + cam.xspan * 2 * i / nx;
				z = zraw[j * gw + i];
				var ok = isFinite(z);
				if (ok) {
					if (z < lo - pad) z = lo - pad;
					else if (z > hi + pad) z = hi + pad;
				}
				var X = x - cam.cx;
				var Y = y - cam.cy;
				var Z = ok ? (z - zmid) * zfit : 0;
				var x1 = X * cyaw - Y * syaw;
				var y1 = X * syaw + Y * cyaw;
				pts[j * gw + i] = {
					ux: x1,
					uz: y1 * sp + Z * cp,
					d: y1 * cp - Z * sp,
					ok: ok,
					t: ok ? (z - lo) / (hi - lo) : 0,
					wx: X,
					wy: Y,
					wz: Z
				};
				if (ok) boxone(box, X, Y, Z);
			}
		}
		fit3(w, h, box, pts, cyaw, syaw, cp, sp, bandover(lo, hi));
		for (i = 0; i < pts.length; i++) {
			pts[i].x = view3.ox + pts[i].ux * view3.s;
			pts[i].y = view3.oy - pts[i].uz * view3.s;
		}
		ctx.save();
		ctx.beginPath();
		ctx.rect(0, 0, w, h);
		ctx.clip();
		if (style === "grid") drawGrid(ctx, pts, nx, ny, gw);
		else if (style === "dots") drawDots(ctx, pts);
		else if (style === "level") drawLevels(ctx, pts, zraw, nx, ny, gw, lo, hi);
		else drawFill(ctx, pts, nx, ny, gw, zraw);
		drawaxes(ctx, cyaw, syaw, cp, sp, zmid, zfit, lo, hi, box);
		ctx.restore();
	}

	// 缩放后整片范围变了，上次的细扫范围要和它对一下：取交集，
	// 交完还剩下上次范围的大半就接着用。
	// 阈值要跟「上次的范围」比，不能跟整片比：缩得很小时曲面只占整片百分之几，
	// 拿整片当分母就会一贯判定太小、退回整片，拖动时格子立刻变稀。
	function keeparea(pre, full){
		if (!pre) return full;
		var a = {
			x0: Math.max(pre.x0, full.x0), x1: Math.min(pre.x1, full.x1),
			y0: Math.max(pre.y0, full.y0), y1: Math.min(pre.y1, full.y1)
		};
		if (!(a.x1 > a.x0) || !(a.y1 > a.y0)) return full;
		if (!((a.x1 - a.x0) > (pre.x1 - pre.x0) * 0.5)) return full;
		if (!((a.y1 - a.y0) > (pre.y1 - pre.y0) * 0.5)) return full;
		return a;
	}

	// 隐式曲面的整片采样范围。z 一律按 y 的采样范围取，不跟着缩放变。
	function fullarea(){
		return {
			x0: cam.cx - cam.xspan, x1: cam.cx + cam.xspan,
			y0: cam.cy - cam.yspan, y1: cam.cy + cam.yspan
		};
	}

	// 沿 z 扫描的半宽。锚在初始范围上，不跟着 xy 缩放一起收：
	// 一收就只在球中间扫一小段，上下两层解被切断，球面会裂成两片月牙。
	function zspan(){
		return Math.max(YSPAN, cam.yspan);
	}

	// 单帧求值量预算。隐式曲面每个格子都要沿 z 扫一遍再看二分，
	// 高倍缩放下格子数跟着屏幕像素走，不设上限一次缩放就要几千万次求值。
	var scanbudget = 0;

	// 在 area 范围内按 x、y 网格竖着扫一遍 F(x,y,z)=0 的零点，每一列得到若干个 z 解。
	// za、zb 只决定沿 z 扫多长；颜色深浅仍按整片 z 范围算，换视角时颜色不会跳。
	function impscan(area, rows, zsteps, za, zb){
		var n = Math.max(2, Math.round(rows));
		var m = Math.max(4, Math.round(zsteps));
		// 列数和行数一样用加倍的格宽，隐式曲面整体密度才是显式的一半。
		var cell = impcell();
		var nx = gridcount(n, area.x1 - area.x0, area.y1 - area.y0, cell);
		// 超预算时只降沿 z 的扫描步数，行列数一律不动：
		// 行数一砍网格线就稀了，缩小或拖动时会稀成几根，形状都看不出来。
		// z 步数也留个下限：压太低每列解出的环就少了，纵向同样会稀。
		if (scanbudget > 0) {
			var cost = (nx + 1) * (n + 1) * m;
			if (cost > scanbudget) {
				var zmin = quick() ? 18 : 30;
				m = Math.max(zmin, Math.floor(scanbudget / ((nx + 1) * (n + 1))));
				if (m > zsteps) m = Math.round(zsteps);
			}
		}
		var cw = nx + 1;
		var cols = new Array(cw * (n + 1));
		var box = newbox();
		var all = [];
		var cyaw = Math.cos(cam.yaw), syaw = Math.sin(cam.yaw);
		var cp = Math.cos(cam.pitch), sp = Math.sin(cam.pitch);
		var zs = zspan(), z0 = -zs, z1 = zs;
		// 传进来的 z 范围是上一帧留下的，那时 xy 采样范围可能比现在大得多（缩过又放大）。
		// 不夹回当前范围，同样的 z 步数会被摊到大出好几倍的区间上，每列解出的环就变少。
		if (za === undefined || !(zb > za) || za < z0 || zb > z1) {
			za = z0;
			zb = z1;
		}
		var dz = (zb - za) / m;
		var i, j, k, x, y, z, f0, f1, lo, hi, flo, fhi, zm, fm, zz, p, list;
		for (j = 0; j <= n; j++) {
			y = area.y0 + (area.y1 - area.y0) * j / n;
			for (i = 0; i <= nx; i++) {
				x = area.x0 + (area.x1 - area.x0) * i / nx;
				list = [];
				f0 = ev(fn, x, y, za);
				for (k = 1; k <= m; k++) {
					z = za + dz * k;
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
						p = proj3(x, y, zz, cyaw, syaw, cp, sp, 0, 1);
						p.t = (zz - z0) / (z1 - z0);
						p.z = zz;
						p.gx = x;
						p.gy = y;
						p.ok = true;
						list.push(p);
						all.push(p);
						boxone(box, x, y, zz);
					}
					f0 = f1;
				}
				cols[j * cw + i] = list;
			}
		}
		return { nx: nx, ny: n, m: m, cw: cw, cols: cols, all: all, box: box, area: area, za: za, zb: zb };
	}

	// 按粗扫到的范围往外放一格多点。曲面常常只占采样范围里的一小块，
	// 只在这一块上细扫，同样的计算量能多画出好几倍网格线。
	function fitarea(pre, full){
		if (!pre.all.length || !isFinite(pre.box.x0)) return full;
		var dx = (full.x1 - full.x0) / Math.max(1, pre.nx) * 1.2;
		var dy = (full.y1 - full.y0) / Math.max(1, pre.ny) * 1.2;
		var a = {
			x0: pre.box.x0 - dx, x1: pre.box.x1 + dx,
			y0: pre.box.y0 - dy, y1: pre.box.y1 + dy
		};
		if (a.x0 < full.x0) a.x0 = full.x0;
		if (a.x1 > full.x1) a.x1 = full.x1;
		if (a.y0 < full.y0) a.y0 = full.y0;
		if (a.y1 > full.y1) a.y1 = full.y1;
		if (!(a.x1 - a.x0 > 1e-9) || !(a.y1 - a.y0 > 1e-9)) return full;
		return a;
	}

	// 细扫出来的点贴到范围边上，说明范围取小了，得退回整片范围重扫。
	// 范围被夹到整片范围的地方本来就有曲面，那些边不算裁。
	function areaedge(res, full){
		var a = res.area;
		var dx = (a.x1 - a.x0) / Math.max(1, res.nx) * 1.5;
		var dy = (a.y1 - a.y0) / Math.max(1, res.ny) * 1.5;
		var left = a.x0 > full.x0 + 1e-9, right = a.x1 < full.x1 - 1e-9;
		var bottom = a.y0 > full.y0 + 1e-9, top = a.y1 < full.y1 - 1e-9;
		var i, p;
		for (i = 0; i < res.all.length; i++) {
			p = res.all[i];
			if (left && p.gx - a.x0 < dx) return true;
			if (right && a.x1 - p.gx < dx) return true;
			if (bottom && p.gy - a.y0 < dy) return true;
			if (top && a.y1 - p.gy < dy) return true;
		}
		return false;
	}

	// 网格线数按曲面在屏幕上的大小来定：画得大就多画几条，画得小就少画几条，
	// 屏幕上的格子大约就是一个固定像素宽，不会稀得只剩几根，也不会密成一块色块。
	// 用 view3.s——它已经含了适配倍率和缩放，转视角、滚轮缩放后格子像素间距都基本不变。
	// 隐式曲面每一列都要单独扫 z 求根，格子再密就看不清面了，格宽用显式的两倍。
	function impcell(){
		return gridcell() * IMPDENS;
	}

	function improws(w, h, area){
		var ax = projaxis();
		var ux = (area.x1 - area.x0) * ax.ax;
		var uy = (area.y1 - area.y0) * ax.ay;
		var px = Math.max(ux, uy) * view3safes();
		if (!(px > 0)) px = Math.max(w, h);
		var rows = Math.round(px / impcell());
		// 同上：快速档不动行数，只靠 z 步数和预算省时间
		// 上限也跟着格宽走：宽屏上 px 有 1400 多，固定 140 会顶到上限，
		// 格宽加倍了行数却降不下来。
		var cap = Math.round(280 / IMPDENS);
		if (!(rows >= MINROWS)) rows = MINROWS;
		if (rows > cap) rows = cap;
		return rows;
	}

	// 沿 z 扫的范围也收一下：曲面在 z 上一般也只占一段，少扫一截，同样的点数就细一倍。
	function fitz(pre, full){
		var zs = zspan(), z0 = -zs, z1 = zs;
		if (!pre.all.length || !isFinite(pre.box.z0)) return [z0, z1];
		var span = z1 - z0;
		// 补白按曲面实际占的 z 跨度算，不能按整片 z 跨度算：
		// 缩得很小时 span 有上百，拿它当基准补出来的白比曲面本身还大，
		// 收窄等于没做，同样的步数摊在大区间上，每列解出的环就少。
		var ext = pre.box.z1 - pre.box.z0;
		var pad = ext * 0.5;
		if (!(pad > 0)) pad = span * 0.02;
		if (pad > span * 0.06) pad = span * 0.06;
		var a = pre.box.z0 - pad, b = pre.box.z1 + pad;
		if (a < z0) a = z0;
		if (b > z1) b = z1;
		if (!(b - a > 1e-9)) return [z0, z1];
		return [a, b];
	}

	// 解贴到沿 z 扫的两端，说明 z 的范围取小了，得退回整片 z 重扫。
	function zedge(res){
		var zs = zspan(), z0 = -zs, z1 = zs;
		var dz = (res.zb - res.za) / Math.max(1, res.m) * 1.5;
		var low = res.za > z0 + 1e-9, high = res.zb < z1 - 1e-9;
		var i, p;
		for (i = 0; i < res.all.length; i++) {
			p = res.all[i];
			if (low && p.z - res.za < dz) return true;
			if (high && res.zb - p.z < dz) return true;
		}
		return false;
	}

	// 三维隐式方程 F(x,y,z)=0：按 x、y 网格沿 z 扫过零点，得到曲面点；
	// 再按样式画成填充面、网格线、点阵或水平等高线。和显式曲面一样按投影范围铺满画布。
	function draw3implicit(ctx, w, h){
		// 行数用上一帧的倍率估，倍率刷新后重画一次就对齐了。
		var pass = 0;
		while (true) {
			refitAgain = false;
			draw3impband(ctx, w, h);
			if (!refitAgain || pass++ >= 1) return;
		}
	}

	function draw3impband(ctx, w, h){
		var cyaw = Math.cos(cam.yaw), syaw = Math.sin(cam.yaw);
		var cp = Math.cos(cam.pitch), sp = Math.sin(cam.pitch);
		var full = fullarea();
		// 交互中只压 z 方向的扫描步数，网格行列数保持定稿时的密度：
		// 行数一减半，拖动时看起来就像换了个粗糙的模型，松手再变密，观感比稍慢还差。
		var zsteps = quick() ? 26 : 44;
		var res, area;
		// 预算要跟着行数走，压太狠会把格宽撑大、行数又被降回去，等于白设下限。
		scanbudget = quick() ? 1500000 : 2200000;
		if (quick()) {
			// 沿用上次定稿的范围（和整片范围取交集），不然一拖就退回整片范围、线突然变稀。
			area = keeparea(imparea, full);
			var zr0 = 0, zr1 = 0;
			if (impzr && area !== full) {
				// z 范围也要夹回当前范围：缩过又放大时旧范围比现在大好几倍，
				// 同样的 z 步数摊到大区间上，每列解出的环就变少、线明显变稀。
				var zlim = zspan();
				zr0 = impzr[0] < -zlim ? -zlim : impzr[0];
				zr1 = impzr[1] > zlim ? zlim : impzr[1];
				if (!(zr1 > zr0)) zr0 = zr1 = 0;
			}
			res = impscan(area, improws(w, h, area), zsteps, zr0, zr1);
		}
		else {
			// 先用粗网格扫一遍看曲面实际占多大，再只在这一块上按屏幕大小细扫。
			var pre = impscan(full, 20, 22);
			area = fitarea(pre, full);
			var zr = fitz(pre, full);
			res = impscan(area, improws(w, h, area), zsteps, zr[0], zr[1]);
			if (!res.all.length || (area !== full && areaedge(res, full)) || zedge(res)) {
				// 退回整片范围重扫时 z 也要收窄：不收窄就按 ±zspan 扫，
				// 区间大出好几倍、步数却没变，每列解出的环会明显变少。
				area = full;
				res = impscan(area, improws(w, h, area), zsteps, zr[0], zr[1]);
			}
			imparea = res.all.length ? area : null;
			impzr = res.all.length ? [res.za, res.zb] : null;
		}
		var cols = res.cols, nx = res.nx, ny = res.ny, cw = res.cw, box = res.box;
		var zs = zspan(), z0 = -zs, z1 = zs;
		var i, k, row, gap;
		fit3(w, h, box, res.all, cyaw, syaw, cp, sp);
		// 合并阈值的本意是「同一位置上的两层解」，所以按屏幕像素算，并给个上限。
		// 不加上限的话，高倍放大时这里会算出十几像素，把整列相邻的点全并成一个，
		// 网格就整片消失（缩回去又好了，看起来像是只在放大时坏的）。
		gap = (res.area.y1 - res.area.y0) * view3.s / ny;
		if (!(gap < 2.5)) gap = 2.5;
		for (i = 0; i < cols.length; i++) {
			row = cols[i];
			for (k = 0; k < row.length; k++) {
				row[k].x = view3.ox + row[k].ux * view3.s;
				row[k].y = view3.oy - row[k].uz * view3.s;
			}
			// 两层解在相切处几乎重合，合并成一个点，网格线就能连上。
			for (k = 0; k + 1 < row.length; k++) {
				if (Math.abs(row[k + 1].x - row[k].x) + Math.abs(row[k + 1].y - row[k].y) < gap) {
					var mm = {
						x: (row[k].x + row[k + 1].x) / 2,
						y: (row[k].y + row[k + 1].y) / 2,
						d: (row[k].d + row[k + 1].d) / 2,
						t: 0.5,
						z: (row[k].z + row[k + 1].z) / 2
					};
					row[k] = mm;
					row[k + 1] = mm;
				}
			}
		}
		ctx.save();
		ctx.beginPath();
		ctx.rect(0, 0, w, h);
		ctx.clip();
		if (style === "grid") {
			impgrid(ctx, cols, nx, ny, cw, gap);
			implevelzero(ctx, cyaw, syaw, cp, sp, res.area, ny);
		}
		else if (style === "dots") impdots(ctx, cols, nx, ny, cw, gap);
		else if (style === "level") implevels(ctx, z0, z1, cyaw, syaw, cp, sp, res.area, ny);
		else impfill(ctx, cols, nx, ny, cw, gap);
		// z 轴只画到曲面顶端，免得轴尖和标签被画布切掉。
		drawaxes(ctx, cyaw, syaw, cp, sp, 0, 1, z0, box.z1 > 0 ? Math.min(z1, box.z1) : z1, box);
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
	function impfill(ctx, cols, nx, ny, cw, gap){
		var pts = [];
		var i, j, r, rad = Math.max(2.4, gap * 1.6);
		for (j = 0; j <= ny; j++) {
			for (i = 0; i <= nx; i++) {
				var list = cols[j * cw + i];
				for (r = 0; r < list.length; r++) pts.push(list[r]);
			}
		}
		var cyaw = Math.cos(cam.yaw), syaw = Math.sin(cam.yaw);
		var cp = Math.cos(cam.pitch), sp = Math.sin(cam.pitch);
		impsurfnormal(pts, cyaw, syaw, cp, sp);
		var lower = [], upper = [], k, arr;
		for (i = 0; i < pts.length; i++) (pts[i].z < 0 ? lower : upper).push(pts[i]);
		lower.sort(function(a, b){ return b.d - a.d; });
		upper.sort(function(a, b){ return b.d - a.d; });
		for (k = 0; k < 2; k++) {
			arr = k ? upper : lower;
			for (i = 0; i < arr.length; i++) {
				var n = arr[i].n;
				ctx.fillStyle = n ? shadecolor(arr[i].t, n.nr, n.nu, n.nd) : zcolor(arr[i].t);
				ctx.beginPath();
				ctx.arc(arr[i].x, arr[i].y, rad, 0, 6.2832);
				ctx.fill();
			}
		}
	}

	// 隐式曲面的法线走解析式：F 的梯度就是法线方向，比差分稳，球的轮廓处也不会乱。
	function impsurfnormal(pts, cyaw, syaw, cp, sp){
		var i, p, g, len, c3;
		for (i = 0; i < pts.length; i++) {
			p = pts[i];
			g = grad(p.gx, p.gy, p.z);
			if (!g) continue;
			len = Math.sqrt(g[0] * g[0] + g[1] * g[1] + g[2] * g[2]);
			if (!(len > 1e-9)) continue;
			c3 = tocam(g[0] / len, g[1] / len, g[2] / len, cyaw, syaw, cp, sp);
			// 朝外：背向镜头的一面（nd<0）才是正面
			if (c3.nd > 0) { c3.nr = -c3.nr; c3.nu = -c3.nu; c3.nd = -c3.nd; }
			p.n = c3;
		}
	}

	// 数值梯度，供隐式曲面求法线用。
	function grad(x, y, z){
		var h = 1e-4;
		var fx = ev(fn, x + h, y, z) - ev(fn, x - h, y, z);
		var fy = ev(fn, x, y + h, z) - ev(fn, x, y - h, z);
		var fz = ev(fn, x, y, z + h) - ev(fn, x, y, z - h);
		if (!isFinite(fx) || !isFinite(fy) || !isFinite(fz)) return null;
		return [fx, fy, fz];
	}

	function imptri(tris, pts, a, b, c, lim){
		var p0 = pts[a], p1 = pts[b], p2 = pts[c];
		if (!p0 || !p1 || !p2) return;
		if (!impnear(p0, p1, lim) || !impnear(p1, p2, lim) || !impnear(p2, p0, lim)) return;
		tris.push({ p0: p0, p1: p1, p2: p2, d: (p0.d + p1.d + p2.d) / 3 });
	}

	// 线段缓冲：每个字段一条平行数组。隐式曲面一帧要产上万条线段，
	// 每条塞一个 {p,q,d,t} 对象，光分配和回收（GC）就占掉一成多时间。
	// 画布外的线段直接丢掉：clip 之后它们本来就看不见，却还要参与排序和描边。
	var clipbox = { x0: -1e9, y0: -1e9, x1: 1e9, y1: 1e9 };

	function segbuf(){
		return { x1: [], y1: [], x2: [], y2: [], t: [], d: [] };
	}

	function segadd(b, p, q, t, d){
		var m = 8;
		if (p.x < clipbox.x0 - m && q.x < clipbox.x0 - m) return;
		if (p.x > clipbox.x1 + m && q.x > clipbox.x1 + m) return;
		if (p.y < clipbox.y0 - m && q.y < clipbox.y0 - m) return;
		if (p.y > clipbox.y1 + m && q.y > clipbox.y1 + m) return;
		b.x1.push(p.x);
		b.y1.push(p.y);
		b.x2.push(q.x);
		b.y2.push(q.y);
		b.t.push(t);
		b.d.push(d);
	}

	function impgrid(ctx, cols, nx, ny, cw){
		var b = segbuf();
		var i, j, r, a, q, cnt;
		for (j = 0; j <= ny; j++) {
			for (i = 0; i <= nx; i++) {
				a = cols[j * cw + i];
				if (i < nx) {
					q = cols[j * cw + i + 1];
					cnt = Math.min(a.length, q.length);
					for (r = 0; r < cnt; r++) segadd(b, a[r], q[r], (a[r].t + q[r].t) / 2, (a[r].d + q[r].d) / 2);
				}
				if (j < ny) {
					q = cols[(j + 1) * cw + i];
					cnt = Math.min(a.length, q.length);
					for (r = 0; r < cnt; r++) segadd(b, a[r], q[r], (a[r].t + q[r].t) / 2, (a[r].d + q[r].d) / 2);
				}
			}
		}
		drawsegs(ctx, b, 1.4);
	}

	// 按深度排好序的线段逐条 stroke 太贵：每次 beginPath/stroke 都有固定开销。
	// 颜色相同的相邻段合并成一条路径，一次画完；同色段之间本来就没有遮挡关系。
	function strokeSeq(ctx, segs, width){
		var b = segbuf();
		var i;
		for (i = 0; i < segs.length; i++)
			segadd(b, segs[i].p, segs[i].q, segs[i].t, segs[i].d);
		drawsegs(ctx, b, width);
	}

	function drawsegs(ctx, b, width){
		var n = b.d.length;
		if (!n) return;
		var idx = new Array(n);
		var i, c, cur = "", open = false;
		for (i = 0; i < n; i++) idx[i] = i;
		idx.sort(function(u, v){ return b.d[v] - b.d[u]; });
		ctx.lineWidth = width;
		ctx.lineJoin = "round";
		ctx.lineCap = "round";
		for (i = 0; i < n; i++) {
			var k = idx[i];
			c = zcolor(b.t[k]);
			if (c !== cur) {
				if (open) ctx.stroke();
				ctx.strokeStyle = c;
				ctx.beginPath();
				cur = c;
				open = true;
			}
			ctx.moveTo(b.x1[k], b.y1[k]);
			ctx.lineTo(b.x2[k], b.y2[k]);
		}
		if (open) ctx.stroke();
	}

	// 两层曲面相切处的交界线（z=0 平面上的 F=0），补上网格中间的缝。
	function implevelzero(ctx, cyaw, syaw, cp, sp, area, rows){
		var gy = quick() ? 30 : Math.max(44, Math.round(rows * 1.3));
		var gx = gridcount(gy, area.x1 - area.x0, area.y1 - area.y0);
		var gw = gx + 1;
		var vals = new Float64Array(gw * (gy + 1));
		var i, j, x, y, v00, v10, v11, v01, ps, k;
		function proj0(x, y){
			return scr3(proj3(x, y, 0, cyaw, syaw, cp, sp, 0, 1));
		}
		for (j = 0; j <= gy; j++) {
			y = area.y0 + (area.y1 - area.y0) * j / gy;
			for (i = 0; i <= gx; i++) {
				x = area.x0 + (area.x1 - area.x0) * i / gx;
				vals[j * gw + i] = ev(fn, x, y, 0);
			}
		}
		ctx.strokeStyle = zcolor(0.5);
		ctx.lineWidth = 1.4;
		ctx.lineJoin = "round";
		ctx.lineCap = "round";
		ctx.beginPath();
		for (j = 0; j < gy; j++) {
			var y0 = area.y0 + (area.y1 - area.y0) * j / gy;
			var y1 = area.y0 + (area.y1 - area.y0) * (j + 1) / gy;
			for (i = 0; i < gx; i++) {
				var x0 = area.x0 + (area.x1 - area.x0) * i / gx;
				var x1 = area.x0 + (area.x1 - area.x0) * (i + 1) / gx;
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
				if (ps.length === 2) {
					var a0 = proj0(ps[0].x, ps[0].y), b0 = proj0(ps[1].x, ps[1].y);
					ctx.moveTo(a0.x, a0.y);
					ctx.lineTo(b0.x, b0.y);
				}
				else if (ps.length === 4) {
					var vc = ev(fn, (x0 + x1) / 2, (y0 + y1) / 2, 0);
					if (!isFinite(vc)) continue;
					var pairs = (vc > 0) === (v00 > 0) ? [[0, 1], [2, 3]] : [[0, 3], [1, 2]];
					for (k = 0; k < 2; k++) {
						var a1 = proj0(ps[pairs[k][0]].x, ps[pairs[k][0]].y);
						var b1 = proj0(ps[pairs[k][1]].x, ps[pairs[k][1]].y);
						ctx.moveTo(a1.x, a1.y);
						ctx.lineTo(b1.x, b1.y);
					}
				}
			}
		}
		ctx.stroke();
	}

	function impdots(ctx, cols, nx, ny, cw, gap){
		var pts = [];
		var i, j, r;
		for (j = 0; j <= ny; j++) {
			for (i = 0; i <= nx; i++) {
				var list = cols[j * cw + i];
				for (r = 0; r < list.length; r++) pts.push(list[r]);
			}
		}
		var rad = Math.max(1.2, Math.min(2.6, gap * 0.45));
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
	// 这一样式只有一圈圈的线，不会糊成面，所以不跟着 IMPDENS 减密：
	// 层数给足，圈与圈之间才不会稀到看不出曲面形状。
	function implevels(ctx, z0, z1, cyaw, syaw, cp, sp, area, rows){
		var levels = quick() ? 10 : 20;
		var gy = quick() ? 30 : Math.max(44, Math.round(rows * 1.6));
		var gx = gridcount(gy, area.x1 - area.x0, area.y1 - area.y0);
		var segs = [];
		function proj(x, y, z){
			return scr3(proj3(x, y, z, cyaw, syaw, cp, sp, 0, 1));
		}
		function add(xa, ya, xb, yb, z, t){
			var p = proj(xa, ya, z);
			var q = proj(xb, yb, z);
			segs.push({ p: p, q: q, d: (p.d + q.d) / 2, t: t });
		}
		var L, i, j, zc, t, gw = gx + 1, vals, v00, v10, v11, v01, ps, vc;
		for (L = 1; L < levels; L++) {
			zc = z0 + (z1 - z0) * L / levels;
			t = (zc - z0) / (z1 - z0);
			vals = new Float64Array(gw * (gy + 1));
			for (j = 0; j <= gy; j++) {
				var yv = area.y0 + (area.y1 - area.y0) * j / gy;
				for (i = 0; i <= gx; i++) {
					var xv = area.x0 + (area.x1 - area.x0) * i / gx;
					vals[j * gw + i] = ev(fn, xv, yv, zc);
				}
			}
			for (j = 0; j < gy; j++) {
				var y0 = area.y0 + (area.y1 - area.y0) * j / gy;
				var y1 = area.y0 + (area.y1 - area.y0) * (j + 1) / gy;
				for (i = 0; i < gx; i++) {
					var x0 = area.x0 + (area.x1 - area.x0) * i / gx;
					var x1 = area.x0 + (area.x1 - area.x0) * (i + 1) / gx;
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

	function drawFill(ctx, pts, nx, ny, gw, zraw){
		var tris = [];
		var i, j, a, b, c, d, p0, p1, p2;
		for (j = 0; j < ny; j++) {
			for (i = 0; i < nx; i++) {
				a = j * gw + i;
				b = a + 1;
				c = a + gw;
				d = c + 1;
				pushtri(tris, pts, a, c, b);
				pushtri(tris, pts, c, d, b);
			}
		}
		tris.sort(function(u, v){ return v.d - u.d; });
		var cyaw = Math.cos(cam.yaw), syaw = Math.sin(cam.yaw);
		var cp = Math.cos(cam.pitch), sp = Math.sin(cam.pitch);
		var nc = surfnormal(pts, zraw, nx, ny, gw, cyaw, syaw, cp, sp);
		for (i = 0; i < tris.length; i++) {
			p0 = pts[tris[i].a];
			p1 = pts[tris[i].b];
			p2 = pts[tris[i].c];
			// 三个顶点的法线取平均，小三角面之间才不会一块块跳色。
			var na = nc[tris[i].a], nb = nc[tris[i].b], ncc = nc[tris[i].c];
			var nr = (na.nr + nb.nr + ncc.nr) / 3;
			var nu = (na.nu + nb.nu + ncc.nu) / 3;
			var nd = (na.nd + nb.nd + ncc.nd) / 3;
			var nlen = Math.sqrt(nr * nr + nu * nu + nd * nd) || 1;
			ctx.beginPath();
			ctx.moveTo(p0.x, p0.y);
			ctx.lineTo(p1.x, p1.y);
			ctx.lineTo(p2.x, p2.y);
			ctx.closePath();
			ctx.fillStyle = shadecolor((p0.t + p1.t + p2.t) / 3, nr / nlen, nu / nlen, nd / nlen);
			ctx.fill();
		}
	}

	// 每个顶点的表面法线：沿 x、y 两个方向做中心差分求切向，叉乘得到法线。
	// 落在采样边上的点只有单侧差分，曲面边缘的高光才不会被砍掉半条。
	function surfnormal(pts, zraw, nx, ny, gw, cyaw, syaw, cp, sp){
		var out = new Array(pts.length);
		var i, j, a, hl, hr, hd, hu, dx, dy, x0, x1, y0, y1, nx1, ny1, nz1, len;
		var stepx = cam.xspan * 2 / Math.max(1, nx);
		var stepy = cam.yspan * 2 / Math.max(1, ny);
		for (j = 0; j <= ny; j++) {
			for (i = 0; i <= nx; i++) {
				a = j * gw + i;
				if (!pts[a].ok) continue;
				// 两个方向的差分步长，边上的点退回单侧
				hl = i > 0 && pts[a - 1].ok ? zraw[a - 1] : NaN;
				hr = i < nx && pts[a + 1].ok ? zraw[a + 1] : NaN;
				hd = j > 0 && pts[a - gw].ok ? zraw[a - gw] : NaN;
				hu = j < ny && pts[a + gw].ok ? zraw[a + gw] : NaN;
				if (isFinite(hl) && isFinite(hr)) {
					dx = 2 * stepx;
					x0 = hr - hl;
				}
				else if (isFinite(hr)) {
					dx = stepx;
					x0 = hr - zraw[a];
				}
				else if (isFinite(hl)) {
					dx = stepx;
					x0 = zraw[a] - hl;
				}
				else {
					dx = 0;
					x0 = 0;
				}
				if (isFinite(hd) && isFinite(hu)) {
					dy = 2 * stepy;
					y0 = hu - hd;
				}
				else if (isFinite(hu)) {
					dy = stepy;
					y0 = hu - zraw[a];
				}
				else if (isFinite(hd)) {
					dy = stepy;
					y0 = zraw[a] - hd;
				}
				else {
					dy = 0;
					y0 = 0;
				}
				// 切向 (dx,0,x0)、(0,dy,y0)，叉乘得法线，再归一化
				if (!(dx > 1e-9)) { nx1 = 1; ny1 = 0; nz1 = 0; }
				else if (!(dy > 1e-9)) { nx1 = 0; ny1 = 1; nz1 = 0; }
				else {
					nx1 = -x0 * dy;
					ny1 = -y0 * dx;
					nz1 = dx * dy;
				}
				len = Math.sqrt(nx1 * nx1 + ny1 * ny1 + nz1 * nz1);
				if (!(len > 1e-9)) { nx1 = 0; ny1 = 0; nz1 = 1; len = 1; }
				var c3 = tocam(nx1 / len, ny1 / len, nz1 / len, cyaw, syaw, cp, sp);
				// 保证朝上（z 分量正向），否则背面的三角会全黑
				if (c3.nu < 0) { c3.nr = -c3.nr; c3.nu = -c3.nu; c3.nd = -c3.nd; }
				out[a] = c3;
			}
		}
		return out;
	}

	function drawGrid(ctx, pts, nx, ny, gw){
		var segs = [];
		var i, j, a;
		for (j = 0; j <= ny; j++) {
			for (i = 0; i <= nx; i++) {
				a = j * gw + i;
				if (i < nx) pushseg(segs, pts, a, a + 1);
				if (j < ny) pushseg(segs, pts, a, a + gw);
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

	function drawLevels(ctx, pts, zraw, nx, ny, gw, lo, hi){
		// 等高线只有线、不会糊成面，层数给足；8 层在起伏大的曲面上稀得看不出形状。
		var bands = quick() ? 10 : 16;
		var segs = [];
		var k, level, i, j, a, z00, z10, z11, z01, p00, p10, p11, p01, e0, e1, e2, e3, hit;
		var span = hi - lo;
		if (!(span > 0)) return;
		for (k = 1; k < bands; k++) {
			level = lo + span * k / bands;
			for (j = 0; j < ny; j++) {
				for (i = 0; i < nx; i++) {
					a = j * gw + i;
					z00 = zraw[a];
					z10 = zraw[a + 1];
					z01 = zraw[a + gw];
					z11 = zraw[a + gw + 1];
					p00 = pts[a];
					p10 = pts[a + 1];
					p01 = pts[a + gw];
					p11 = pts[a + gw + 1];
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

	function drawaxes(ctx, cyaw, syaw, cp, sp, zmid, zfit, lo, hi, box){
		function proj(x, y, z){
			return scr3(proj3(x, y, z, cyaw, syaw, cp, sp, zmid, zfit));
		}
		var o = proj(0, 0, 0);
		// x、y 轴一直画到画布边缘，取原点到边缘的距离作为轴长。
		var stage = canvas && canvas.parentElement;
		var sw = stage && stage.clientWidth > 2 ? stage.clientWidth : 800;
		var sh = stage && stage.clientHeight > 2 ? stage.clientHeight : 420;
		var reach = axisedge(o, proj, sw, sh);
		var ax = proj(reach, 0, 0);
		var ay = proj(0, reach, 0);
		// z 轴只画到曲面最高处再多出一截，不按整个采样高度拉满，免得把画面上下撑开。
		var top = box && isFinite(box.z1) ? zmid + box.z1 / (zfit > 1e-9 ? zfit : 1) : hi;
		var ztip = top + Math.abs(top - zmid) * 0.12 + reach * 0.05;
		var az = proj(0, 0, ztip);
		ctx.lineWidth = 1.5;
		ctx.font = '12px "Segoe UI", "Microsoft YaHei UI", sans-serif';
		ctx.fillStyle = "#44403c";
		axis(ctx, o, ax, "#e11d48", "x");
		axis(ctx, o, ay, "#16a34a", "y");
		axis(ctx, o, az, "#2563eb", "z");
	}

	// 原点沿正方向走到画布边缘需要多长的世界坐标。标签要留在画布内，所以留一点余量。
	function axisedge(o, proj, w, h){
		var unit = proj(1, 0, 0);
		var dx = unit.x - o.x, dy = unit.y - o.y;
		var per = Math.sqrt(dx * dx + dy * dy);
		if (!(per > 1e-6)) return 1;
		var m = 24;
		var tx = dx > 1e-6 ? (w - m - o.x) / dx : dx < -1e-6 ? (m - o.x) / dx : Infinity;
		var ty = dy > 1e-6 ? (h - m - o.y) / dy : dy < -1e-6 ? (m - o.y) / dy : Infinity;
		var t = Math.min(tx, ty);
		if (!(t > 0) || !isFinite(t)) t = 1;
		return t;
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

	function zrgb(t){
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
		return [r, g, b];
	}

	// 颜色按 256 级缓存：每帧要拼上万个 rgb 字符串，光拼接就占掉可观的时间。
	var ZCACHE = new Array(257);

	function zcolor(t){
		var i = Math.round(t * 256);
		if (!(i >= 0)) i = 0;
		else if (i > 256) i = 256;
		var s = ZCACHE[i];
		if (s) return s;
		var c = zrgb(i / 256);
		s = "rgb(" + Math.round(c[0]) + "," + Math.round(c[1]) + "," + Math.round(c[2]) + ")";
		ZCACHE[i] = s;
		return s;
	}

	// 相机空间里指向光源的方向。金属靠窄高光出效果，光从左上前方打过来。
	var LIGHT = (function(){
		var lx = -0.45, ly = 0.55, lz = -0.70;
		var len = Math.sqrt(lx * lx + ly * ly + lz * lz) || 1;
		lx /= len;
		ly /= len;
		lz /= len;
		// 半角向量：视线固定为 (0,0,-1)，H = normalize(L + V)
		var hx = lx, hy = ly, hz = lz - 1;
		var hlen = Math.sqrt(hx * hx + hy * hy + hz * hz) || 1;
		return { lx: lx, ly: ly, lz: lz, hx: hx / hlen, hy: hy / hlen, hz: hz / hlen };
	})();

	// 金属着色。法线用相机空间分量给出：(nr, nu, nd)，nu 朝上、nd 朝里（背向镜头为正）。
	// 漫反射压低，主体靠环境渐变，再加一道收窄的白高光；轮廓光只提亮本色，不能掺白，
	// 否则陡坡会被冲成一圈灰的。
	function shadecolor(t, nr, nu, nd){
		var base = zrgb(t);
		var diff = nr * LIGHT.lx + nu * LIGHT.ly + nd * LIGHT.lz;
		if (!(diff > 0)) diff = 0;
		var spec = nr * LIGHT.hx + nu * LIGHT.hy + nd * LIGHT.hz;
		spec = spec > 0 ? Math.pow(spec, 40) : 0;
		var up = nu > 0 ? nu : 0;
		var face = nd < 0 ? -nd : 0;
		var rim = 1 - face;
		rim = rim * rim * rim;
		// 环境：上方偏亮、下方压暗，模拟一张顶光的反射贴图
		var env = 0.34 + 0.50 * up;
		var k = 0.14 + 0.62 * diff;
		var lit = k + env * 0.60 + rim * 0.16;
		var r = base[0] * lit + 255 * spec * 0.80;
		var g = base[1] * lit + 255 * spec * 0.84;
		var b = base[2] * lit + 255 * spec * 0.92;
		r = r > 255 ? 255 : r < 0 ? 0 : r;
		g = g > 255 ? 255 : g < 0 ? 0 : g;
		b = b > 255 ? 255 : b < 0 ? 0 : b;
		return "rgb(" + Math.round(r) + "," + Math.round(g) + "," + Math.round(b) + ")";
	}

	// 把世界法线转到相机空间：(nr, nu, nd)
	function tocam(nx, ny, nz, cyaw, syaw, cp, sp){
		return {
			nr: nx * cyaw - ny * syaw,
			nu: nx * syaw * sp + ny * cyaw * sp + nz * cp,
			nd: nx * syaw * cp + ny * cyaw * cp - nz * sp
		};
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
		jit(tree);
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

	// 公式求值占九成以上时间。把语法树拼成一段 JS 表达式交给 new Function，
	// 一次求值就是一次直接调用，省掉递归解释几十层的判断和闭包调用。
	var FUNSRC = {
		sin: "Math.sin", cos: "Math.cos", tan: "Math.tan", asin: "Math.asin", acos: "Math.acos",
		atan: "Math.atan", atan2: "Math.atan2", sqrt: "Math.sqrt", abs: "Math.abs", ln: "Math.log",
		exp: "Math.exp", pow: "Math.pow", min: "Math.min", max: "Math.max", hypot: "Math.hypot",
		floor: "Math.floor", ceil: "Math.ceil", round: "Math.round", sign: "Math.sign",
		log: "log10", log10: "log10", log2: "log2"
	};

	function jit(node){
		if (!node) return null;
		var src;
		try {
			src = gensrc(node);
			node.j = new Function("x", "y", "z", "var v=" + src + ";return v===Infinity||v===-Infinity?NaN:v;");
		}
		catch (ex) {
			node.j = null; //编译不了就退回逐节点解释
		}
		return node.j;
	}

	function gensrc(node){
		var i, out, a, b, n;
		if (node.t === "n") {
			n = node.v;
			if (n < 0) return "(" + n + ")";
			return String(n);
		}
		if (node.t === "v") return node.i === 2 ? "z" : node.i === 1 ? "y" : "x";
		if (node.t === "u") return "(-" + gensrc(node.a) + ")";
		if (node.t === "eq") return "(" + gensrc(node.a) + "-" + gensrc(node.b) + ")";
		if (node.t === "b") {
			a = gensrc(node.a);
			b = gensrc(node.b);
			if (node.op === "^") {
				//整数次幂展开成乘法，Math.pow 在热循环里贵一个数量级
				var k = node.b.t === "n" ? node.b.v : NaN;
				if (k === 2) return "(" + a + "*" + a + ")";
				if (k === 3) return "(" + a + "*" + a + "*" + a + ")";
				if (k === 4) return "((" + a + "*" + a + ")*(" + a + "*" + a + "))";
				return "Math.pow(" + a + "," + b + ")";
			}
			return "(" + a + node.op + b + ")";
		}
		out = FUNSRC[node.name];
		if (!out) return "(0/0)";
		if (node.name === "log2") return "(Math.log(" + gensrc(node.args[0]) + ")/Math.LN2)";
		if (node.name === "log" || node.name === "log10") return "(Math.log(" + gensrc(node.args[0]) + ")/Math.LN10)";
		var list = [];
		for (i = 0; i < node.args.length; i++) list.push(gensrc(node.args[i]));
		return out + "(" + list.join(",") + ")";
	}

	function ev(node, x, y, z){
		if (!node) return NaN;
		//编译过的公式直接走原生函数，递归解释一层要走几十次判断
		if (node.j) return node.j(x, y, z);
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

	function usesZ(node){
		if (!node) return false;
		if (node.t === "v") return node.i === 2;
		if (node.t === "u" || node.t === "eq") return usesZ(node.a) || usesZ(node.b);
		if (node.t === "b") return usesZ(node.a) || usesZ(node.b);
		if (node.t === "f") {
			var i;
			for (i = 0; i < node.args.length; i++) if (usesZ(node.args[i])) return true;
		}
		return false;
	}

	function isletter(c){ return (c >= "a" && c <= "z") || (c >= "A" && c <= "Z"); }
	function isdigit(c){ return c >= "0" && c <= "9"; }
})();

















































