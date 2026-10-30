(function(){
	var HANDLES = ["nw", "n", "ne", "e", "se", "s", "sw", "w"];
	var items = [];
	var undo = [];
	var tool = "pen";
	var color = "#000000";
	var width = 2;
	var sel = -1;
	var drag = null;
	var draft = null;
	var lift = null;
	var edit = null;
	var texttok = 0;
	var textarmed = false;
	var canvas = null;
	var base = null;
	var dirty = false;
	var watch = null;
	var hooked = false;
	var busy = false;
	var ink = null;
	var inkWait = null;
	var PALETTE = [
		"#000000", "#808080", "#800000", "#808000", "#008000", "#008080", "#000080", "#800080", "#808040", "#004040", "#0080FF", "#004080", "#8000FF", "#804000",
		"#FFFFFF", "#C0C0C0", "#FF0000", "#FFFF00", "#00FF00", "#00FFFF", "#0000FF", "#FF00FF", "#FFFF80", "#00FF80", "#80FFFF", "#8080FF", "#FF0080", "#FF8040"
	];
	var FB = {
		"web.board.copied": "已复制",
		"web.board.saved": "已保存",
		"web.board.noclip": "请用 Ctrl+V 粘贴图片"
	};

	SK.regutil("u-board", {
		title: "画板",
		go: "画",
		own: true,
		hideGo: true,
		hideCopy: true,
		hideOut: true,
		body: body,
		open: open
	});

	function body(){
		tool = "pen";
		color = "#000000";
		width = 2;
		return '<div class="board">'
			+ '<div class="board-bar">'
			+ '<div class="board-tools">'
			+ tbtn("select", "web.board.select", "选择", false)
			+ tbtn("rect", "web.board.rect", "矩形", false)
			+ tbtn("ellipse", "web.board.ellipse", "椭圆", false)
			+ tbtn("arrow", "web.board.arrow", "箭头", false)
			+ tbtn("pen", "web.board.pen", "画笔", true)
			+ tbtn("lasso", "web.board.lasso", "圈选", false)
			+ tbtn("text", "web.board.text", "文字", false)
			+ '<span class="sep"></span>'
			+ wbtn(2) + wbtn(4) + wbtn(8)
			+ '<span class="sep"></span>'
			+ abtn("paste", "web.board.paste", "粘贴")
			+ abtn("undo", "web.board.undo", "撤销")
			+ abtn("clear", "web.board.clear", "清空")
			+ abtn("copy", "web.board.copy", "复制图片")
			+ abtn("save", "web.board.save", "保存")
			+ "</div>"
			+ colors()
			+ "</div>"
			+ '<div class="board-stage"><canvas id="board-cv"></canvas>'
			+ '<input id="board-txt" class="board-text" type="text" hidden spellcheck="false">'
			+ '<p class="board-msg" id="board-msg"></p>'
			+ "</div></div>";
	}

	function tip(key, fb){
		return SK.esc(SK.t(key) || fb);
	}

	function tbtn(name, key, fb, on){
		return '<button type="button" data-board="' + name + '"' + (on ? ' class="on"' : "") + ' data-ktip="' + key + '" title="' + tip(key, fb) + '">'
			+ icon(name) + "</button>";
	}

	function abtn(name, key, fb){
		return '<button type="button" data-act="' + name + '" data-ktip="' + key + '" title="' + tip(key, fb) + '">'
			+ '<span data-k="' + key + '">' + tip(key, fb) + "</span></button>";
	}

	function wbtn(n){
		return '<button type="button" data-w="' + n + '"' + (width === n ? ' class="on"' : "") + ' data-ktip="web.board.wide" title="' + tip("web.board.wide", "线宽") + '">' + n + "</button>";
	}

	function colors(){
		var top = "", bot = "", i, hex, btn;
		for (i = 0; i < PALETTE.length; i++) {
			hex = PALETTE[i];
			btn = '<button type="button" data-color="' + hex + '"' + (hex.toLowerCase() === String(color).toLowerCase() ? ' class="on"' : "") + ' title="' + hex + '" style="background:' + hex + '"></button>';
			if (i < 14) top += btn;
			else bot += btn;
		}
		return '<div class="board-colors">'
			+ '<input type="color" id="board-color" value="' + color + '" data-ktip="web.board.color" title="' + tip("web.board.color", "颜色") + '">'
			+ '<span class="board-swatches"><span>' + top + "</span><span>" + bot + "</span></span></div>";
	}

	function icon(name){
		var d = {
			select: '<path fill="currentColor" d="M3.2 1.6l.8 12.2 3-3.1 2.2 4.3 1.6-.8-2.2-4.2h4.6L3.2 1.6z"/>',
			rect: '<rect x="2.4" y="2.4" width="11.2" height="11.2" fill="none" stroke="currentColor" stroke-width="1.6"/>',
			ellipse: '<ellipse cx="8" cy="8" rx="5.6" ry="4.6" fill="none" stroke="currentColor" stroke-width="1.6"/>',
			arrow: '<path fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" d="M2.8 13.2L13.2 2.8M8.2 2.8h5v5"/>',
			pen: '<path fill="none" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round" stroke-linecap="round" d="M2.6 12.2l2.8-2.8 6.2-6.2 2.2 2.2-6.2 6.2-2.8 2.8-2.2-2.2zm6.6-7.6l2.2 2.2"/>',
			lasso: '<path fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-dasharray="2.2 1.4" d="M8.1 2.5a4.3 3.6 0 1 0 .5 6.9"/><path fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" d="M8.4 9.2l2.3 3.3"/>',
			text: '<path fill="currentColor" d="M2.2 2.2h11.6v2.2H9.2v9.2H6.8V4.4H2.2V2.2z"/>'
		};
		return '<svg viewBox="0 0 16 16" aria-hidden="true">' + (d[name] || "") + "</svg>";
	}

	function open(){
		hook();
		canvas = F.id("board-cv");
		if (!canvas) return;
		canvas.onpointerdown = down;
		canvas.onpointermove = move;
		canvas.onpointerup = up;
		canvas.onpointercancel = up;
		canvas.onpointerrawupdate = rawink;
		canvas.onmousedown = function(ev){
			if (tool === "text" && ev.button === 0 && ev.preventDefault) F.pd(ev);
		};
		canvas.ondblclick = dbl;
		var bar = F.q(".board-bar");
		if (bar) bar.onclick = onbar;
		var col = F.id("board-color");
		if (col) {
			col.value = color;
			col.oninput = function(){ setcolor(col.value); };
		}
		var txt = F.id("board-txt");
		if (txt) {
			txt.onkeydown = function(ev){
				if (ev.key === "Enter") { F.pd(ev); committext(true); }
				else if (ev.key === "Escape") { F.pd(ev); committext(false); }
			};
			txt.onblur = function(){
				var token = texttok;
				var armed = textarmed;
				setTimeout(function(){
					if (!armed || token !== texttok || !edit || document.activeElement === txt) return;
					committext(true);
				}, 0);
			};
			txt.onmousedown = function(ev){ ev.stopPropagation(); };
		}
		settool(tool);
		setwidth(width);
		setcolor(color);
		fit();
		F.raf(fit);
		if (watch) watch.disconnect();
		if (window.ResizeObserver && canvas.parentElement) {
			watch = new ResizeObserver(function(){ fit(); });
			watch.observe(canvas.parentElement);
		}
	}

	function hook(){
		if (hooked) return;
		hooked = true;
		F.on(document, "paste", onpaste);
		F.on(document, "keydown", onkey);
	}

	function onbar(ev){
		var el = ev.target && ev.target.closest ? ev.target.closest("button") : ev.target;
		if (!el || !el.getAttribute) return;
		var mode = F.attr(el, "data-board");
		var act = F.attr(el, "data-act");
		if (mode) settool(mode);
		else if (F.attr(el, "data-color")) setcolor(F.attr(el, "data-color"));
		else if (F.attr(el, "data-w")) setwidth(parseInt(F.attr(el, "data-w"), 10) || 4);
		else if (act === "paste") dopaste();
		else if (act === "undo") undolast();
		else if (act === "clear") clearall();
		else if (act === "copy") copyimg();
		else if (act === "save") saveimg();
	}

	function settool(name){
		if (edit) committext(true);
		var baked = name !== tool && !!lift;
		if (baked) stamplit();
		tool = name;
		var nodes = F.qa(".board-bar [data-board]");
		var i;
		for (i = 0; i < nodes.length; i++)
			nodes[i].className = F.attr(nodes[i], "data-board") === name ? "on" : "";
		var stage = F.q(".board-stage");
		if (stage) stage.className = "board-stage" + (name === "select" ? " select" : "");
		if (baked) paint(true);
	}

	function setwidth(n){
		width = n;
		var nodes = F.qa(".board-tools [data-w]");
		var i;
		for (i = 0; i < nodes.length; i++)
			nodes[i].className = String(F.attr(nodes[i], "data-w")) === String(n) ? "on" : "";
	}

	function setcolor(hex){
		color = hex;
		var col = F.id("board-color");
		if (col && col.value !== hex) col.value = hex;
		var nodes = F.qa(".board-colors [data-color]");
		var i, want = String(hex).toLowerCase();
		for (i = 0; i < nodes.length; i++)
			nodes[i].className = String(F.attr(nodes[i], "data-color")).toLowerCase() === want ? "on" : "";
	}

	function onkey(ev){
		if (SK.curtool !== "u-board") return;
		var tag = ev.target && ev.target.tagName;
		if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT") {
			if (ev.target && ev.target.id === "board-txt") {
				if (ev.key === "Escape") { F.pd(ev); committext(false); }
				else if (ev.key === "Enter") { F.pd(ev); committext(true); }
			}
			return;
		}
		if ((ev.ctrlKey || ev.metaKey) && (ev.key === "z" || ev.key === "Z") && !ev.shiftKey) {
			F.pd(ev);
			undolast();
			return;
		}
		if (ev.key === "Escape") {
			if (lift) {
				if (undo.length) undolast();
				else { stamplit(); paint(true); }
				return;
			}
			draft = null;
			drag = null;
			paint(true);
			return;
		}
		if ((ev.key === "Delete" || ev.key === "Backspace") && sel >= 0 && sel < items.length) {
			F.pd(ev);
			pushundo();
			items.splice(sel, 1);
			sel = -1;
			paint(true);
		}
	}

	function onpaste(ev){
		if (SK.curtool !== "u-board") return;
		var cd = ev.clipboardData;
		if (!cd || !cd.items) return;
		var i, item, blob = null;
		for (i = 0; i < cd.items.length; i++) {
			item = cd.items[i];
			if (item.kind === "file" && item.type.indexOf("image/") === 0) {
				blob = item.getAsFile();
				break;
			}
		}
		if (!blob && cd.files && cd.files.length && cd.files[0].type.indexOf("image/") === 0)
			blob = cd.files[0];
		if (!blob) return;
		F.pd(ev);
		addblob(blob);
	}

	function dopaste(){
		if (edit) committext(true);
		if (!navigator.clipboard || !navigator.clipboard.read) { say("web.board.noclip"); return; }
		navigator.clipboard.read().then(function(list){
			var i, j, types;
			for (i = 0; i < list.length; i++) {
				types = list[i].types || [];
				for (j = 0; j < types.length; j++) {
					if (types[j].indexOf("image/") === 0) {
						list[i].getType(types[j]).then(addblob, function(){ say("web.board.noclip"); });
						return;
					}
				}
			}
			say("web.board.noclip");
		}, function(){ say("web.board.noclip"); });
	}

	function addblob(blob){
		if (!blob) return;
		var url = F.url(blob);
		var im = new Image();
		im.onload = function(){
			F.unurl(url);
			placeimg(im);
		};
		im.onerror = function(){ F.unurl(url); };
		im.src = url;
	}

	function placeimg(im){
		var bw = canvas.width, bh = canvas.height;
		var w = im.naturalWidth || im.width;
		var h = im.naturalHeight || im.height;
		if (!w || !h || bw < 2 || bh < 2) return;
		var s = Math.min(1, (bw * 0.6) / w, (bh * 0.6) / h);
		w = Math.max(8, Math.round(w * s));
		h = Math.max(8, Math.round(h * s));
		var n = 0, i, x, y;
		for (i = 0; i < items.length; i++) if (items[i].t === "img") n++;
		x = Math.round((bw - w) / 2) + (n % 6) * 16;
		y = Math.round((bh - h) / 2) + (n % 6) * 16;
		if (x + w > bw - 8) x = Math.max(8, bw - w - 8);
		if (y + h > bh - 8) y = Math.max(8, bh - h - 8);
		pushundo();
		items.push({ t: "img", img: im, x: x, y: y, w: w, h: h });
		sel = items.length - 1;
		settool("select");
		paint(true);
	}

	function say(key){
		var el = F.id("board-msg");
		if (el) el.textContent = SK.t(key) || FB[key] || "";
	}

	function clearall(){
		if (edit) committext(true);
		if (!items.length && !dirty && !lift) return;
		pushundo();
		items = [];
		sel = -1;
		draft = null;
		lift = null;
		dirty = false;
		whitebase();
		paint(true);
	}

	function undolast(){
		if (edit) committext(false);
		var snap = undo.pop();
		if (!snap) return;
		items = snap.items;
		sel = snap.sel;
		dirty = snap.dirty;
		if (sel >= items.length) sel = items.length - 1;
		if (snap.pixels && base && snap.pixels.width === base.width && snap.pixels.height === base.height)
			F.ctx(base).putImageData(snap.pixels, 0, 0);
		draft = null;
		drag = null;
		lift = null;
		paint(true);
	}

	function copyimg(){
		exportpng(function(blob){
			if (!blob || !navigator.clipboard || !window.ClipboardItem) { say("web.board.noclip"); return; }
			var item = {};
			item["image/png"] = blob;
			navigator.clipboard.write([new ClipboardItem(item)]).then(function(){
				say("web.board.copied");
			}, function(){ say("web.board.noclip"); });
		});
	}

	function saveimg(){
		exportpng(function(blob){
			if (!blob) return;
			var a = F.el("a");
			var url = F.url(blob);
			a.href = url;
			a.download = "board.png";
			F.append(document.body, a);
			a.click();
			a.remove();
			setTimeout(function(){ F.unurl(url); }, 1500);
			say("web.board.saved");
		});
	}

	function exportpng(done){
		if (!canvas || busy) return;
		busy = true;
		paint(false);
		canvas.toBlob(function(blob){
			busy = false;
			paint(true);
			done(blob);
		}, "image/png");
	}

	function down(ev){
		if (busy || !canvas || ev.button != null && ev.button !== 0) return;
		var p = xy(ev);
		if (tool === "text") {
			if (ev.preventDefault) F.pd(ev);
			var ti = hittext(p);
			if (ti >= 0) begintext(ti);
			else begintext(-1, p.x, p.y);
			return;
		}
		if (canvas.setPointerCapture) canvas.setPointerCapture(ev.pointerId);
		if (tool === "select") {
			if (sel >= 0 && sel < items.length) {
				var handle = handleat(boxof(items[sel]), p);
				if (handle) {
					drag = { mode: "resize", handle: handle, i: sel, x0: p.x, y0: p.y, start: cloneitem(items[sel]), box: boxof(items[sel]), moved: false };
					pushundo();
					return;
				}
			}
			var hit = topmost(p);
			sel = hit;
			if (hit >= 0) {
				drag = { mode: "move", i: hit, x0: p.x, y0: p.y, start: cloneitem(items[hit]), moved: false };
				pushundo();
			}
			paint(true);
			return;
		}
		if (tool === "lasso") {
			if (lift && hitlift(p)) {
				drag = { mode: "lift", x0: p.x, y0: p.y, ox: lift.x, oy: lift.y, moved: false };
				return;
			}
			if (lift) stamplit();
			draft = { t: "lasso", pts: [{ x: p.x, y: p.y }] };
			paint(true);
			return;
		}
		if (tool === "pen") {
			draft = { t: "pen", x: p.x, y: p.y, pts: [{ x: 0, y: 0 }], pred: [], c: color, lw: width };
			ensureink();
			inktrail(ev);
		}
		else if (tool === "arrow") draft = { t: "arrow", x1: p.x, y1: p.y, x2: p.x, y2: p.y, c: color, lw: width };
		else draft = { t: tool, x: p.x, y: p.y, w: 0, h: 0, c: color, lw: width };
		paint(true);
	}

	function move(ev){
		if (!canvas) return;
		var p = xy(ev);
		if (drag) {
			var dx = p.x - drag.x0, dy = p.y - drag.y0;
			if (!drag.moved && Math.hypot(dx, dy) < 1) return;
			drag.moved = true;
			if (drag.mode === "lift" && lift) {
				lift.x = drag.ox + dx;
				lift.y = drag.oy + dy;
				paint(true);
				return;
			}
			var it = items[drag.i];
			if (!it) return;
			if (drag.mode === "move") applymove(it, drag.start, dx, dy);
			else applyresize(it, drag.start, drag.box, drag.handle, dx, dy, ev.shiftKey);
			paint(true);
			return;
		}
		if (!draft) return;
		if (draft.t === "lasso") addlasso(ev);
		else if (draft.t === "pen") addpen(ev);
		else if (draft.t === "arrow") { draft.x2 = p.x; draft.y2 = p.y; }
		else { draft.w = p.x - draft.x; draft.h = p.y - draft.y; }
		paint(true);
	}

	function up(){
		if (tool === "text" && edit && !textarmed) {
			var txt = F.id("board-txt");
			if (txt) setTimeout(function(){
				if (!edit || textarmed) return;
				txt.focus();
				textarmed = document.activeElement === txt;
			}, 0);
		}
		if (drag) {
			var waslift = drag.mode === "lift";
			if (!waslift && !drag.moved && undo.length) undo.pop();
			drag = null;
			if (waslift) stamplit();
			paint(true);
		}
		if (draft) commitdraft();
	}

	function dbl(ev){
		if (tool !== "select" || !canvas) return;
		var i = topmost(xy(ev));
		if (i >= 0 && items[i].t === "text") begintext(i);
	}

	function commitdraft(){
		var d = draft;
		draft = null;
		if (!d) return;
		if (d.t === "lasso") { liftlasso(d); return; }
		if (d.t === "pen") {
			d.pred = null;
			if (!d.pts || d.pts.length < 2) { paint(true); return; }
		}
		else if (d.t === "arrow") {
			if (Math.hypot(d.x2 - d.x1, d.y2 - d.y1) < 3) { paint(true); return; }
		}
		else {
			if (d.w < 0) { d.x += d.w; d.w = -d.w; }
			if (d.h < 0) { d.y += d.h; d.h = -d.h; }
			if (d.w < 3 && d.h < 3) { paint(true); return; }
			if (d.w < 3) d.w = 3;
			if (d.h < 3) d.h = 3;
		}
		if (!base) fit();
		if (!base) { paint(true); return; }
		pushundo();
		drawitem(F.ctx(base), d);
		dirty = true;
		sel = -1;
		paint(true);
	}

	function liftlasso(d){
		var pts = d.pts;
		if (!pts || pts.length < 3) { paint(true); return; }
		if (!base) fit();
		if (!base) { paint(true); return; }
		var i, len = 0;
		for (i = 1; i < pts.length; i++) len += Math.hypot(pts[i].x - pts[i - 1].x, pts[i].y - pts[i - 1].y);
		if (len < 12) { paint(true); return; }
		var minx = pts[0].x, miny = pts[0].y, maxx = minx, maxy = miny;
		for (i = 1; i < pts.length; i++) {
			if (pts[i].x < minx) minx = pts[i].x;
			if (pts[i].y < miny) miny = pts[i].y;
			if (pts[i].x > maxx) maxx = pts[i].x;
			if (pts[i].y > maxy) maxy = pts[i].y;
		}
		var take = [], it, box;
		for (i = 0; i < items.length; i++) {
			it = items[i];
			box = boxof(it);
			if (inpoly(box.x + box.w / 2, box.y + box.h / 2, pts)) {
				take.push(it);
				if (box.x < minx) minx = box.x;
				if (box.y < miny) miny = box.y;
				if (box.x + box.w > maxx) maxx = box.x + box.w;
				if (box.y + box.h > maxy) maxy = box.y + box.h;
			}
		}
		minx = Math.max(0, Math.floor(minx));
		miny = Math.max(0, Math.floor(miny));
		maxx = Math.min(base.width, Math.ceil(maxx));
		maxy = Math.min(base.height, Math.ceil(maxy));
		var bw = maxx - minx, bh = maxy - miny;
		if (bw < 2 || bh < 2) { paint(true); return; }
		var mask = F.el("canvas");
		mask.width = bw;
		mask.height = bh;
		var mctx = F.ctx(mask);
		mctx.beginPath();
		mctx.moveTo(pts[0].x - minx, pts[0].y - miny);
		for (i = 1; i < pts.length; i++) mctx.lineTo(pts[i].x - minx, pts[i].y - miny);
		mctx.closePath();
		mctx.fill();
		var md = mctx.getImageData(0, 0, bw, bh).data;
		var bctx = F.ctx(base);
		var src = bctx.getImageData(minx, miny, bw, bh);
		var cut = F.el("canvas");
		cut.width = bw;
		cut.height = bh;
		var cctx = F.ctx(cut);
		var out = cctx.createImageData(bw, bh);
		var sd = src.data, od = out.data, n = bw * bh, p, o, r, g, b, a, any = take.length > 0;
		for (p = 0; p < n; p++) {
			if (md[p * 4 + 3] < 128) continue;
			o = p * 4;
			r = sd[o]; g = sd[o + 1]; b = sd[o + 2]; a = sd[o + 3];
			if (a < 8 || (r >= 250 && g >= 250 && b >= 250)) continue;
			od[o] = r; od[o + 1] = g; od[o + 2] = b; od[o + 3] = a;
			sd[o] = 255; sd[o + 1] = 255; sd[o + 2] = 255; sd[o + 3] = 255;
			any = true;
		}
		if (!any) { paint(true); return; }
		pushundo();
		bctx.putImageData(src, minx, miny);
		cctx.putImageData(out, 0, 0);
		cctx.save();
		cctx.translate(-minx, -miny);
		for (i = 0; i < take.length; i++) drawitem(cctx, take[i]);
		cctx.restore();
		var kept = [], seen;
		for (i = 0; i < items.length; i++) {
			seen = false;
			for (p = 0; p < take.length; p++) if (take[p] === items[i]) seen = true;
			if (!seen) kept.push(items[i]);
		}
		items = kept;
		lift = { img: cut, x: minx, y: miny, w: bw, h: bh };
		sel = -1;
		dirty = true;
		paint(true);
	}

	function hitlift(p){
		if (!lift) return false;
		var pad = 4;
		return p.x >= lift.x - pad && p.y >= lift.y - pad && p.x <= lift.x + lift.w + pad && p.y <= lift.y + lift.h + pad;
	}

	function stamplit(){
		if (!lift) return;
		var box = lift;
		lift = null;
		sel = -1;
		if (!base) return;
		F.ctx(base).drawImage(box.img, box.x, box.y, box.w, box.h);
		dirty = true;
	}

	function inpoly(x, y, pts){
		var inside = false, i, j = pts.length - 1, xi, yi, xj, yj;
		for (i = 0; i < pts.length; i++) {
			xi = pts[i].x; yi = pts[i].y;
			xj = pts[j].x; yj = pts[j].y;
			if (((yi > y) !== (yj > y)) && (x < (xj - xi) * (y - yi) / ((yj - yi) || 1e-9) + xi)) inside = !inside;
			j = i;
		}
		return inside;
	}

	function begintext(i, x, y){
		texttok++;
		textarmed = false;
		if (edit) committext(true);
		var txt = F.id("board-txt");
		if (!txt || !canvas) return;
		var it = i >= 0 ? items[i] : null;
		edit = { i: i, x: it ? it.x : x, y: it ? it.y : y };
		var rect = F.rect(canvas);
		var sx = rect.width / canvas.width || 1;
		var sy = rect.height / canvas.height || 1;
		var px = it ? it.x : x;
		var py = it ? it.y : y;
		var size = it ? it.size : 28;
		txt.value = it ? it.s : "";
		txt.style.left = (canvas.offsetLeft + px * sx) + "px";
		txt.style.top = (canvas.offsetTop + py * sy) + "px";
		txt.style.fontSize = Math.max(12, size * sy) + "px";
		txt.style.color = it ? it.c : color;
		txt.style.width = Math.max(80, (it ? measure(it).w : 140) * sx) + "px";
		txt.hidden = false;
		txt.style.height = Math.max(32, Math.round(size * sy) + 8) + "px";
		var token = texttok;
		setTimeout(function(){
			if (token !== texttok || !edit) return;
			txt.focus();
			textarmed = document.activeElement === txt;
			if (textarmed && it) txt.select();
		}, 0);
		paint(true);
	}

	function committext(keep){
		if (!edit) return;
		var ed = edit;
		var txt = F.id("board-txt");
		var val = txt ? String(txt.value || "").replace(/^\s+|\s+$/g, "") : "";
		edit = null;
		if (txt) txt.hidden = true;
		if (!keep) { paint(true); return; }
		if (ed.i < 0) {
			if (!val) { paint(true); return; }
			pushundo();
			items.push({ t: "text", x: ed.x, y: ed.y, s: val, c: color, size: 28 });
			sel = items.length - 1;
		}
		else {
			var it = items[ed.i];
			if (!it) { paint(true); return; }
			if (!val) {
				pushundo();
				items.splice(ed.i, 1);
				if (sel === ed.i) sel = -1;
				else if (sel > ed.i) sel--;
			}
			else if (it.s !== val || it.c !== color) {
				pushundo();
				it.s = val;
				it.c = color;
				sel = ed.i;
			}
		}
		paint(true);
	}

	function applymove(it, st, dx, dy){
		it.x = st.x + dx;
		it.y = st.y + dy;
	}

	function applyresize(it, st, box, handle, dx, dy, shift){
		var ar = (shift && it.t === "img" && box.h) ? box.w / box.h : 0;
		var nb = resizebox(box, handle, dx, dy, ar, 8);
		if (it.t === "img") {
			it.x = nb.x; it.y = nb.y; it.w = nb.w; it.h = nb.h;
		}
		else if (it.t === "text") {
			it.x = nb.x; it.y = nb.y;
			it.size = Math.max(8, st.size * nb.h / Math.max(8, box.h));
		}
	}

	function resizebox(box, handle, dx, dy, ar, min){
		var x1 = box.x, y1 = box.y, x2 = box.x + box.w, y2 = box.y + box.h;
		if (handle.indexOf("w") >= 0) x1 += dx;
		if (handle.indexOf("e") >= 0) x2 += dx;
		if (handle.indexOf("n") >= 0) y1 += dy;
		if (handle.indexOf("s") >= 0) y2 += dy;
		if (ar > 0 && handle.length === 2) {
			var sw = x2 - x1, sh = y2 - y1;
			var aw = Math.abs(sw), ah = Math.abs(sh);
			if (aw / ar >= ah) {
				var nh = aw / ar;
				if (handle.indexOf("n") >= 0) y1 = y2 - (sh < 0 ? -nh : nh);
				else y2 = y1 + (sh < 0 ? -nh : nh);
			}
			else {
				var nw = ah * ar;
				if (handle.indexOf("w") >= 0) x1 = x2 - (sw < 0 ? -nw : nw);
				else x2 = x1 + (sw < 0 ? -nw : nw);
			}
		}
		if (x2 < x1) { var tx = x1; x1 = x2; x2 = tx; }
		if (y2 < y1) { var ty = y1; y1 = y2; y2 = ty; }
		if (x2 - x1 < min) {
			if (handle.indexOf("w") >= 0 && handle.indexOf("e") < 0) x1 = x2 - min;
			else x2 = x1 + min;
		}
		if (y2 - y1 < min) {
			if (handle.indexOf("n") >= 0 && handle.indexOf("s") < 0) y1 = y2 - min;
			else y2 = y1 + min;
		}
		return { x: x1, y: y1, w: x2 - x1, h: y2 - y1 };
	}

	function pushundo(){
		var a = [], i, pixels = null;
		for (i = 0; i < items.length; i++) a.push(cloneitem(items[i]));
		if (base) pixels = F.ctx(base).getImageData(0, 0, base.width, base.height);
		undo.push({ items: a, sel: sel, pixels: pixels, dirty: dirty });
		if (undo.length > 20) undo.shift();
	}

	function cloneitem(it){
		var o = {}, k, i;
		for (k in it) o[k] = it[k];
		if (it.pts) {
			o.pts = [];
			for (i = 0; i < it.pts.length; i++) o.pts.push({ x: it.pts[i].x, y: it.pts[i].y });
		}
		return o;
	}

	function fit(){
		if (!canvas || !canvas.parentElement) return;
		var stage = canvas.parentElement;
		var sw = stage.clientWidth, sh = stage.clientHeight;
		if (sw < 2 || sh < 2) return;
		var w = sw, h = sh;
		if (base) {
			if (base.width > w) w = base.width;
			if (base.height > h) h = base.height;
		}
		if (base && base.width === w && base.height === h && canvas.width === w && canvas.height === h) return;
		if (lift && base && (w !== base.width || h !== base.height)) stamplit();
		var prev = base;
		var grew = !!(prev && (w !== prev.width || h !== prev.height));
		base = F.el("canvas");
		base.width = w;
		base.height = h;
		var bctx = F.ctx(base);
		bctx.fillStyle = "#ffffff";
		bctx.fillRect(0, 0, w, h);
		if (prev && prev.width && prev.height) bctx.drawImage(prev, 0, 0);
		if (grew) undo = [];
		canvas.width = w;
		canvas.height = h;
		paint(true);
	}

	function whitebase(){
		if (!base) return;
		var bctx = F.ctx(base);
		bctx.fillStyle = "#ffffff";
		bctx.fillRect(0, 0, base.width, base.height);
	}

	function paint(showSel){
		if (!canvas) return;
		var ctx = F.ctx(canvas);
		var i;
		ctx.clearRect(0, 0, canvas.width, canvas.height);
		if (base) ctx.drawImage(base, 0, 0);
		else {
			ctx.fillStyle = "#ffffff";
			ctx.fillRect(0, 0, canvas.width, canvas.height);
		}
		for (i = 0; i < items.length; i++) {
			if (edit && edit.i === i) continue;
			drawitem(ctx, items[i]);
		}
		if (lift) {
			drawimg(ctx, lift);
			if (showSel) drawliftbox(ctx);
		}
		if (draft) {
			if (draft.t === "lasso") drawlasso(ctx, draft);
			else drawitem(ctx, draft);
		}
		if (showSel && !draft) drawsel(ctx);
	}

	function drawitem(ctx, it){
		if (it.t === "rect") drawrect(ctx, it);
		else if (it.t === "ellipse") drawellipse(ctx, it);
		else if (it.t === "arrow") drawarrow(ctx, it);
		else if (it.t === "pen") drawpen(ctx, it);
		else if (it.t === "text") drawtext(ctx, it);
		else if (it.t === "img") drawimg(ctx, it);
	}

	function linestyle(ctx, it){
		ctx.strokeStyle = it.c;
		ctx.fillStyle = it.c;
		ctx.lineWidth = it.lw;
		ctx.lineCap = "round";
		ctx.lineJoin = "round";
	}

	function drawrect(ctx, it){
		linestyle(ctx, it);
		ctx.strokeRect(it.x, it.y, it.w, it.h);
	}

	function drawellipse(ctx, it){
		var x = it.x, y = it.y, w = it.w, h = it.h;
		if (w < 0) { x += w; w = -w; }
		if (h < 0) { y += h; h = -h; }
		linestyle(ctx, it);
		ctx.beginPath();
		ctx.ellipse(x + w / 2, y + h / 2, Math.max(0.5, w / 2), Math.max(0.5, h / 2), 0, 0, Math.PI * 2);
		ctx.stroke();
	}

	function drawarrow(ctx, it){
		var dx = it.x2 - it.x1, dy = it.y2 - it.y1;
		var len = Math.hypot(dx, dy);
		if (len < 1) return;
		var ang = Math.atan2(dy, dx);
		var head = Math.min(Math.max(10, it.lw * 3.5), len * 0.45);
		linestyle(ctx, it);
		ctx.beginPath();
		ctx.moveTo(it.x1, it.y1);
		ctx.lineTo(it.x2 - Math.cos(ang) * head, it.y2 - Math.sin(ang) * head);
		ctx.stroke();
		ctx.beginPath();
		ctx.moveTo(it.x2, it.y2);
		ctx.lineTo(it.x2 - head * Math.cos(ang - 0.45), it.y2 - head * Math.sin(ang - 0.45));
		ctx.lineTo(it.x2 - head * Math.cos(ang + 0.45), it.y2 - head * Math.sin(ang + 0.45));
		ctx.closePath();
		ctx.fill();
	}

	function drawpen(ctx, it){
		if (!it.pts || !it.pts.length) return;
		linestyle(ctx, it);
		ctx.beginPath();
		ctx.moveTo(it.x + it.pts[0].x, it.y + it.pts[0].y);
		var i;
		for (i = 1; i < it.pts.length; i++) ctx.lineTo(it.x + it.pts[i].x, it.y + it.pts[i].y);
		ctx.stroke();
		if (!it.pred || !it.pred.length) return;
		var last = it.pts[it.pts.length - 1];
		ctx.save();
		ctx.globalAlpha = 0.35;
		ctx.setLineDash([4, 3]);
		ctx.beginPath();
		ctx.moveTo(it.x + last.x, it.y + last.y);
		for (i = 0; i < it.pred.length; i++) ctx.lineTo(it.x + it.pred[i].x, it.y + it.pred[i].y);
		ctx.stroke();
		ctx.restore();
	}

	function eventsOf(ev){
		var list = ev.getCoalescedEvents ? ev.getCoalescedEvents() : null;
		if (!list || !list.length) return [ev];
		return list;
	}

	function addpen(ev){
		var list = eventsOf(ev);
		var i, p, dx, dy, last;
		for (i = 0; i < list.length; i++) {
			p = xy(list[i]);
			dx = p.x - draft.x;
			dy = p.y - draft.y;
			last = draft.pts[draft.pts.length - 1];
			if (last && Math.hypot(dx - last.x, dy - last.y) < 0.4) continue;
			draft.pts.push({ x: dx, y: dy });
		}
		draft.pred = [];
		if (ev.getPredictedEvents) {
			list = ev.getPredictedEvents();
			for (i = 0; i < list.length; i++) {
				p = xy(list[i]);
				draft.pred.push({ x: p.x - draft.x, y: p.y - draft.y });
			}
		}
		inktrail(ev);
	}

	function addlasso(ev){
		var list = eventsOf(ev);
		var i, p, last;
		for (i = 0; i < list.length; i++) {
			p = xy(list[i]);
			last = draft.pts[draft.pts.length - 1];
			if (last && Math.hypot(p.x - last.x, p.y - last.y) < 2) continue;
			draft.pts.push(p);
		}
	}

	function ensureink(){
		if (ink || inkWait || !navigator.ink || !navigator.ink.requestPresenter || !canvas) return;
		try {
			inkWait = navigator.ink.requestPresenter({ presentationArea: canvas });
		}
		catch (ex) {
			inkWait = null;
			return;
		}
		inkWait.then(function(p){
			ink = p;
			inkWait = null;
		}, function(){ inkWait = null; });
	}

	function inktrail(ev){
		if (!ink || !ev || ev.pointerType !== "pen" || tool !== "pen") return;
		try {
			ink.updateInkTrailStartPoint(ev, { color: color, diameter: width });
		}
		catch (ex) {}
	}

	function rawink(ev){
		if (!draft || draft.t !== "pen") return;
		inktrail(ev);
	}

	function drawtext(ctx, it){
		ctx.font = it.size + 'px "Microsoft YaHei UI", sans-serif';
		ctx.fillStyle = it.c;
		ctx.textBaseline = "top";
		ctx.fillText(it.s, it.x, it.y);
	}

	function drawimg(ctx, it){
		var src = it.img;
		if (!src) return;
		if (src.getContext) {
			if (!src.width) return;
		}
		else if (src.complete === false || !src.naturalWidth) return;
		ctx.drawImage(src, it.x, it.y, it.w, it.h);
	}

	function drawliftbox(ctx){
		if (!lift) return;
		ctx.save();
		ctx.strokeStyle = "#3b82f6";
		ctx.lineWidth = 1;
		ctx.setLineDash([6, 4]);
		ctx.strokeRect(lift.x, lift.y, Math.max(1, lift.w), Math.max(1, lift.h));
		ctx.restore();
	}

	function drawlasso(ctx, it){
		if (!it.pts || it.pts.length < 2) return;
		ctx.save();
		ctx.strokeStyle = "#3b82f6";
		ctx.lineWidth = 1.5;
		ctx.setLineDash([6, 4]);
		ctx.lineJoin = "round";
		ctx.lineCap = "round";
		ctx.beginPath();
		ctx.moveTo(it.pts[0].x, it.pts[0].y);
		var i;
		for (i = 1; i < it.pts.length; i++) ctx.lineTo(it.pts[i].x, it.pts[i].y);
		ctx.stroke();
		ctx.restore();
	}

	function drawsel(ctx){
		if (sel < 0 || sel >= items.length) return;
		if (edit && edit.i === sel) return;
		var b = boxof(items[sel]);
		var u = px(1);
		var hs = px(5);
		var i, c;
		ctx.save();
		ctx.strokeStyle = "#f97316";
		ctx.lineWidth = u;
		ctx.setLineDash([px(4), px(3)]);
		ctx.strokeRect(b.x, b.y, Math.max(1, b.w), Math.max(1, b.h));
		ctx.setLineDash([]);
		for (i = 0; i < HANDLES.length; i++) {
			c = handlepos(b, HANDLES[i]);
			ctx.fillStyle = "#ffffff";
			ctx.strokeStyle = "#f97316";
			ctx.lineWidth = u;
			ctx.fillRect(c.x - hs, c.y - hs, hs * 2, hs * 2);
			ctx.strokeRect(c.x - hs, c.y - hs, hs * 2, hs * 2);
		}
		ctx.restore();
	}

	function boxof(it){
		if (it.t === "text") return measure(it);
		return { x: it.x, y: it.y, w: it.w, h: it.h };
	}

	function measure(it){
		var ctx = F.ctx(canvas);
		ctx.font = it.size + 'px "Microsoft YaHei UI", sans-serif';
		var w = ctx.measureText(it.s || " ").width;
		return { x: it.x, y: it.y, w: Math.max(12, w), h: it.size * 1.35 };
	}

	function handlepos(box, h){
		var x = box.x, y = box.y;
		if (h.indexOf("e") >= 0) x = box.x + box.w;
		else if (h.indexOf("w") < 0) x = box.x + box.w / 2;
		if (h.indexOf("s") >= 0) y = box.y + box.h;
		else if (h.indexOf("n") < 0) y = box.y + box.h / 2;
		return { x: x, y: y };
	}

	function handleat(box, p){
		var s = px(8), i, c;
		for (i = 0; i < HANDLES.length; i++) {
			c = handlepos(box, HANDLES[i]);
			if (Math.abs(p.x - c.x) <= s && Math.abs(p.y - c.y) <= s) return HANDLES[i];
		}
		return "";
	}

	function topmost(p){
		var i;
		for (i = items.length - 1; i >= 0; i--)
			if (inside(items[i], p)) return i;
		return -1;
	}

	function hittext(p){
		var i;
		for (i = items.length - 1; i >= 0; i--)
			if (items[i].t === "text" && inside(items[i], p)) return i;
		return -1;
	}

	function inside(it, p){
		var b = boxof(it), pad = 4;
		return p.x >= b.x - pad && p.y >= b.y - pad && p.x <= b.x + b.w + pad && p.y <= b.y + b.h + pad;
	}

	function px(n){
		var r = F.rect(canvas);
		var w = r.width || canvas.width || 1;
		return n * canvas.width / w;
	}

	function xy(ev){
		var r = F.rect(canvas);
		var w = r.width || canvas.width || 1;
		var h = r.height || canvas.height || 1;
		return {
			x: (ev.clientX - r.left) * canvas.width / w,
			y: (ev.clientY - r.top) * canvas.height / h
		};
	}
})();
