(function(){
	var HANDLES = ["nw", "n", "ne", "e", "se", "s", "sw", "w"];
	var items = [];
	var undo = [];
	var tool = "select";
	var color = "#e11d48";
	var width = 4;
	var sel = -1;
	var drag = null;
	var draft = null;
	var edit = null;
	var canvas = null;
	var base = null;
	var dirty = false;
	var watch = null;
	var hooked = false;
	var busy = false;
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
		return '<div class="board">'
			+ '<div class="board-bar">'
			+ tbtn("select", "web.board.select", "选择", true)
			+ tbtn("rect", "web.board.rect", "矩形", false)
			+ tbtn("ellipse", "web.board.ellipse", "椭圆", false)
			+ tbtn("arrow", "web.board.arrow", "箭头", false)
			+ tbtn("pen", "web.board.pen", "画笔", false)
			+ tbtn("text", "web.board.text", "文字", false)
			+ '<span class="sep"></span>'
			+ '<input type="color" id="board-color" value="' + color + '" data-ktip="web.board.color" title="' + tip("web.board.color", "颜色") + '">'
			+ '<select id="board-w" data-ktip="web.board.wide" title="' + tip("web.board.wide", "线宽") + '"><option value="2">2</option><option value="4">4</option><option value="8">8</option></select>'
			+ '<span class="sep"></span>'
			+ abtn("paste", "web.board.paste", "粘贴")
			+ abtn("undo", "web.board.undo", "撤销")
			+ abtn("clear", "web.board.clear", "清空")
			+ abtn("copy", "web.board.copy", "复制图片")
			+ abtn("save", "web.board.save", "保存")
			+ "</div>"
			+ '<div class="board-stage select"><canvas id="board-cv"></canvas>'
			+ '<input id="board-txt" class="board-text" type="text" hidden spellcheck="false">'
			+ '<p class="board-msg" id="board-msg"></p>'
			+ "</div></div>";
	}

	function tip(key, fb){
		return SK.esc(SK.t(key) || fb);
	}

	function tbtn(name, key, fb, on){
		return '<button type="button" data-board="' + name + '"' + (on ? ' class="on"' : "") + ' data-ktip="' + key + '" title="' + tip(key, fb) + '">'
			+ icon(name) + '<span data-k="' + key + '">' + tip(key, fb) + "</span></button>";
	}

	function abtn(name, key, fb){
		return '<button type="button" data-act="' + name + '" data-ktip="' + key + '" title="' + tip(key, fb) + '">'
			+ icon(name) + '<span data-k="' + key + '">' + tip(key, fb) + "</span></button>";
	}

	function icon(name){
		var d = {
			select: '<path fill="currentColor" d="M3.2 1.6l.8 12.2 3-3.1 2.2 4.3 1.6-.8-2.2-4.2h4.6L3.2 1.6z"/>',
			rect: '<rect x="2.4" y="2.4" width="11.2" height="11.2" fill="none" stroke="currentColor" stroke-width="1.6"/>',
			ellipse: '<ellipse cx="8" cy="8" rx="5.6" ry="4.6" fill="none" stroke="currentColor" stroke-width="1.6"/>',
			arrow: '<path fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" d="M2.8 13.2L13.2 2.8M8.2 2.8h5v5"/>',
			pen: '<path fill="none" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round" stroke-linecap="round" d="M2.6 12.2l2.8-2.8 6.2-6.2 2.2 2.2-6.2 6.2-2.8 2.8-2.2-2.2zm6.6-7.6l2.2 2.2"/>',
			text: '<path fill="currentColor" d="M2.2 2.2h11.6v2.2H9.2v9.2H6.8V4.4H2.2V2.2z"/>',
			paste: '<path fill="currentColor" d="M5 1.6h6v2.2h2.2v10.6H2.8V3.8H5V1.6zm1.2 1.2v2h3.6V2.8H6.2zM4.4 6.2h7.2v6.6H4.4V6.2z"/>',
			undo: '<path fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" d="M3 7.2h6.6a3.2 3.2 0 1 1 0 6.4H6.2M3 7.2l2.6-2.6M3 7.2l2.6 2.6"/>',
			clear: '<path fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" d="M3 4.2h10M5.2 4.2l.7 9h4.2l.7-9M6.4 4.2V2.4h3.2v1.8"/>',
			copy: '<path fill="none" stroke="currentColor" stroke-width="1.4" d="M6 2.4h7.2v8.4H6zM2.8 5.2v8.4h7.2"/>',
			save: '<path fill="currentColor" d="M2.4 2h8.2l3 3v8.8H2.4V2zm1.6 1.2v3.6h6.4V3.2H4zm.6 6.2h6.8v3.4H4.6V9.4z"/>'
		};
		return '<svg viewBox="0 0 16 16" aria-hidden="true">' + (d[name] || "") + "</svg>";
	}

	function open(){
		hook();
		canvas = SK.$("board-cv");
		if (!canvas) return;
		canvas.onpointerdown = down;
		canvas.onpointermove = move;
		canvas.onpointerup = up;
		canvas.onpointercancel = up;
		canvas.ondblclick = dbl;
		var bar = document.querySelector(".board-bar");
		if (bar) bar.onclick = onbar;
		var col = SK.$("board-color");
		if (col) {
			col.value = color;
			col.oninput = function(){ color = col.value; };
		}
		var wsel = SK.$("board-w");
		if (wsel) {
			wsel.value = String(width);
			wsel.onchange = function(){ width = parseInt(wsel.value, 10) || 4; };
		}
		var txt = SK.$("board-txt");
		if (txt) {
			txt.onkeydown = function(ev){
				if (ev.key === "Enter") { ev.preventDefault(); committext(true); }
				else if (ev.key === "Escape") { ev.preventDefault(); committext(false); }
			};
			txt.onblur = function(){ committext(true); };
		}
		settool(tool);
		fit();
		requestAnimationFrame(fit);
		if (watch) watch.disconnect();
		if (window.ResizeObserver && canvas.parentElement) {
			watch = new ResizeObserver(function(){ fit(); });
			watch.observe(canvas.parentElement);
		}
	}

	function hook(){
		if (hooked) return;
		hooked = true;
		document.addEventListener("paste", onpaste);
		document.addEventListener("keydown", onkey);
	}

	function onbar(ev){
		var el = ev.target && ev.target.closest ? ev.target.closest("button") : ev.target;
		if (!el || !el.getAttribute) return;
		var mode = el.getAttribute("data-board");
		var act = el.getAttribute("data-act");
		if (mode) settool(mode);
		else if (act === "paste") dopaste();
		else if (act === "undo") undolast();
		else if (act === "clear") clearall();
		else if (act === "copy") copyimg();
		else if (act === "save") saveimg();
	}

	function settool(name){
		if (edit) committext(true);
		tool = name;
		var nodes = document.querySelectorAll(".board-bar [data-board]");
		var i;
		for (i = 0; i < nodes.length; i++)
			nodes[i].className = nodes[i].getAttribute("data-board") === name ? "on" : "";
		var stage = document.querySelector(".board-stage");
		if (stage) stage.className = "board-stage" + (name === "select" ? " select" : "");
	}

	function onkey(ev){
		if (SK.curtool !== "u-board") return;
		var tag = ev.target && ev.target.tagName;
		if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT") {
			if (ev.target && ev.target.id === "board-txt") {
				if (ev.key === "Escape") { ev.preventDefault(); committext(false); }
				else if (ev.key === "Enter") { ev.preventDefault(); committext(true); }
			}
			return;
		}
		if ((ev.ctrlKey || ev.metaKey) && (ev.key === "z" || ev.key === "Z") && !ev.shiftKey) {
			ev.preventDefault();
			undolast();
			return;
		}
		if (ev.key === "Escape") {
			draft = null;
			drag = null;
			paint(true);
			return;
		}
		if ((ev.key === "Delete" || ev.key === "Backspace") && sel >= 0 && sel < items.length) {
			ev.preventDefault();
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
		ev.preventDefault();
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
		var url = URL.createObjectURL(blob);
		var im = new Image();
		im.onload = function(){
			URL.revokeObjectURL(url);
			placeimg(im);
		};
		im.onerror = function(){ URL.revokeObjectURL(url); };
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
		var el = SK.$("board-msg");
		if (el) el.textContent = SK.t(key) || FB[key] || "";
	}

	function clearall(){
		if (edit) committext(true);
		if (!items.length && !dirty) return;
		pushundo();
		items = [];
		sel = -1;
		draft = null;
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
			base.getContext("2d").putImageData(snap.pixels, 0, 0);
		draft = null;
		drag = null;
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
			var a = document.createElement("a");
			var url = URL.createObjectURL(blob);
			a.href = url;
			a.download = "board.png";
			document.body.appendChild(a);
			a.click();
			a.remove();
			setTimeout(function(){ URL.revokeObjectURL(url); }, 1500);
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
		if (canvas.setPointerCapture) canvas.setPointerCapture(ev.pointerId);
		var p = xy(ev);
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
		if (tool === "text") {
			var ti = hittext(p);
			if (ti >= 0) begintext(ti);
			else begintext(-1, p.x, p.y);
			return;
		}
		if (tool === "pen") draft = { t: "pen", x: p.x, y: p.y, pts: [{ x: 0, y: 0 }], c: color, lw: width };
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
			var it = items[drag.i];
			if (!it) return;
			if (drag.mode === "move") applymove(it, drag.start, dx, dy);
			else applyresize(it, drag.start, drag.box, drag.handle, dx, dy, ev.shiftKey);
			paint(true);
			return;
		}
		if (!draft) return;
		if (draft.t === "pen") draft.pts.push({ x: p.x - draft.x, y: p.y - draft.y });
		else if (draft.t === "arrow") { draft.x2 = p.x; draft.y2 = p.y; }
		else { draft.w = p.x - draft.x; draft.h = p.y - draft.y; }
		paint(true);
	}

	function up(){
		if (drag) {
			if (!drag.moved && undo.length) undo.pop();
			drag = null;
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
		if (d.t === "pen") {
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
		drawitem(base.getContext("2d"), d);
		dirty = true;
		sel = -1;
		paint(true);
	}

	function begintext(i, x, y){
		if (edit) committext(true);
		var txt = SK.$("board-txt");
		if (!txt || !canvas) return;
		var it = i >= 0 ? items[i] : null;
		edit = { i: i, x: it ? it.x : x, y: it ? it.y : y };
		var rect = canvas.getBoundingClientRect();
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
		txt.focus();
		if (it) txt.select();
		paint(true);
	}

	function committext(keep){
		if (!edit) return;
		var ed = edit;
		var txt = SK.$("board-txt");
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
		if (base) pixels = base.getContext("2d").getImageData(0, 0, base.width, base.height);
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
		var w = stage.clientWidth, h = stage.clientHeight;
		if (w < 2 || h < 2) return;
		if (base && base.width === w && base.height === h && canvas.width === w && canvas.height === h) return;
		var prev = base;
		base = document.createElement("canvas");
		base.width = w;
		base.height = h;
		var bctx = base.getContext("2d");
		bctx.fillStyle = "#ffffff";
		bctx.fillRect(0, 0, w, h);
		if (prev && prev.width && prev.height) {
			bctx.drawImage(prev, 0, 0, w, h);
			if (prev.width !== w || prev.height !== h) {
				scaleitems(w / prev.width, h / prev.height);
				undo = [];
			}
		}
		canvas.width = w;
		canvas.height = h;
		paint(true);
	}

	function whitebase(){
		if (!base) return;
		var bctx = base.getContext("2d");
		bctx.fillStyle = "#ffffff";
		bctx.fillRect(0, 0, base.width, base.height);
	}

	function scaleitems(sx, sy){
		var i, it;
		for (i = 0; i < items.length; i++) {
			it = items[i];
			it.x *= sx;
			it.y *= sy;
			if (it.w) it.w *= sx;
			if (it.h) it.h *= sy;
			if (it.t === "text") it.size = Math.max(8, it.size * sy);
		}
		if (edit) { edit.x *= sx; edit.y *= sy; }
	}

	function paint(showSel){
		if (!canvas) return;
		var ctx = canvas.getContext("2d");
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
		if (draft) drawitem(ctx, draft);
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
	}

	function drawtext(ctx, it){
		ctx.font = it.size + 'px "Microsoft YaHei UI", sans-serif';
		ctx.fillStyle = it.c;
		ctx.textBaseline = "top";
		ctx.fillText(it.s, it.x, it.y);
	}

	function drawimg(ctx, it){
		if (!it.img || it.img.complete === false || !it.img.naturalWidth) return;
		ctx.drawImage(it.img, it.x, it.y, it.w, it.h);
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
		var ctx = canvas.getContext("2d");
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
		var r = canvas.getBoundingClientRect();
		var w = r.width || canvas.width || 1;
		return n * canvas.width / w;
	}

	function xy(ev){
		var r = canvas.getBoundingClientRect();
		var w = r.width || canvas.width || 1;
		var h = r.height || canvas.height || 1;
		return {
			x: (ev.clientX - r.left) * canvas.width / w,
			y: (ev.clientY - r.top) * canvas.height / h
		};
	}
})();
