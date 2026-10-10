window.SK = window.SK || {};
var SK = window.SK;
SK.icoPic = null;
SK.icoUrl = "";
SK.curtool = "home";
SK.reNote = "";
SK.tick = null;
SK.watchOn = false;
SK.watch0 = 0;
SK.watchAcc = 0;
(function(){
	SK.utils = {};
	SK.regutil = function(name, spec){
		SK.utils[name] = spec;
	};
	var curcat = "fav";
	var spyLock = 0;
	var favs = [];
	var util = "";
	function boot(){
		var today = new Date();
		var month = today.getMonth() + 1;
		var day = today.getDate();
		$("cal-date").value = today.getFullYear()
			+ "-" + (month < 10 ? "0" : "") + month
			+ "-" + (day < 10 ? "0" : "") + day;
		document.addEventListener("click", onclick);
		$("cal-go").onclick = SK.cal;
		$("tx-copy").onclick = function(){ copytext($("tx-out").value, "tx-msg"); };
		$("qr-make").onclick = SK.qrmake;
		$("qr-scan").onclick = SK.qrscan;
		$("ocr-pack").onchange = SK.fillocrmodels;
		$("ocr-file").onchange = SK.onocrfile;
		$("ocr-go").onclick = SK.ocrgo;
		document.addEventListener("paste", SK.onocrpaste);
		$("ocr-copy").onclick = function(){ copytext($("ocr-out").value, "ocr-msg"); };
		$("tts-eng").onchange = SK.fillttsmodels;
		$("tts-lang").onchange = SK.fillttsmodels;
		$("tts-gender").onchange = SK.fillttsmodels;
		$("tts-model").onchange = SK.fillttsvoices;
		$("tts-go").onclick = function(){ SK.ttsgo(false); };
		$("tts-wav").onclick = function(){ SK.ttsgo(true); };
		$("asr-eng").onchange = SK.fillasrmodels;
		$("asr-filego").onclick = SK.asrfile;
		$("asr-rec").onclick = SK.asrrec;
		$("asr-copy").onclick = function(){ copytext($("asr-out").value, "asr-msg"); };
		$("tool-q").oninput = paintcats;
		$("box-go").onclick = utilrun;
		$("box-copy").onclick = function(){ copytext($("box-out").value, "box-msg"); };
		loadfav();
		buildgroups();
		paintfav();
		paintstars();
		window.addEventListener("scroll", spyon);
		window.addEventListener("popstate", applyhash);
		var start = toolhash();
		if (start === "zh" || start === "yomi" || start === "u-name" || start === "u-cron"
			|| start === "u-ua" || start === "u-tok") start = "text";
		if (start === "u-byte" || start === "u-len" || start === "u-temp" || start === "u-px"
			|| start === "u-mass" || start === "u-area" || start === "u-vol" || start === "u-tunit"
			|| start === "u-press" || start === "u-power") start = "u-unit";
		if (start && start !== "home" && cardof(start)) show(start, true);
	}

	function onclick(ev){
		var el = ev.target;
		while (el && el !== document.body) {
			if (el.id === "tx-ops") return;
			var fav = el.getAttribute && el.getAttribute("data-fav");
			if (fav) {
				togglefav(fav);
				return;
			}
			var tool = el.getAttribute && el.getAttribute("data-tool");
			if (tool) {
				show(tool);
				return;
			}
			var cat = el.getAttribute && el.getAttribute("data-cat");
			if (cat && el.tagName === "BUTTON" && !el.getAttribute("data-tool")) {
				jumpcat(cat);
				return;
			}
			var op = el.getAttribute && el.getAttribute("data-op");
			if (op) {
				SK.textop(op);
				return;
			}
			el = el.parentNode;
		}
	}

	function toolhash(){
		var h = location.hash || "";
		if (h.charAt(0) === "#") h = h.substring(1);
		try { h = decodeURIComponent(h); }
		catch (e) { return ""; }
		return h;
	}

	function pushhash(name){
		var next = !name || name === "home" ? "" : "#" + name;
		var now = location.hash || "";
		if (now === next) return;
		try {
			history.pushState({ tool: name || "home" }, "", location.pathname + location.search + next);
		}
		catch (e) {}
	}

	function applyhash(){
		var id = toolhash();
		if (id === "zh" || id === "yomi" || id === "u-name" || id === "u-cron"
			|| id === "u-ua" || id === "u-tok") id = "text";
		if (id === "u-byte" || id === "u-len" || id === "u-temp" || id === "u-px"
			|| id === "u-mass" || id === "u-area" || id === "u-vol" || id === "u-tunit"
			|| id === "u-press" || id === "u-power") id = "u-unit";
		if (!id || id === "home") {
			if (SK.curtool !== "home") show("home", true);
			return;
		}
		if (!cardof(id) || SK.curtool === id) return;
		show(id, true);
	}

	function show(name, fromHist){
		stoptick();
		SK.curtool = name;
		if (name.indexOf("u-") === 0) {
			openutil(name);
			name = "box";
		}
		var panels = document.querySelectorAll(".panel");
		var i;
		for (i = 0; i < panels.length; i++)
			panels[i].className = panels[i].id === name ? "panel on" : "panel";
		$("back").hidden = name === "home";
		var favbtn = $("tool-fav");
		if (SK.curtool === "home") {
			favbtn.hidden = true;
			favbtn.removeAttribute("data-fav");
		}
		else {
			favbtn.hidden = false;
			favbtn.setAttribute("data-fav", SK.curtool);
		}
		paintstars();
		if (name === "home") {
			var back = pickcat();
			if (back) curcat = back;
			paintcats();
		}
		else marktool(SK.curtool, name);
		if (name === "ocr") SK.loadoocr();
		if (name === "tts") SK.loadtts();
		if (name === "asr") SK.loadasr();
		if (!fromHist) pushhash(SK.curtool);
	}

	function marktool(id, panelId){
		var card = cardof(id);
		if (!card) return;
		var cat = card.getAttribute("data-cat") || "";
		if (cat) {
			curcat = cat;
			paintnav();
		}
		var catName = cattitle(cat);
		var span = card.querySelector("span");
		var toolName = span ? span.textContent : "";
		var h1 = document.querySelector("#" + panelId + " h1");
		if (h1 && catName && toolName) h1.textContent = catName + " - " + toolName;
	}

	function cattitle(cat){
		var btn = document.querySelector('#cats button[data-cat="' + cat + '"]');
		if (!btn) return "";
		return btn.getAttribute("data-title") || "";
	}

	function buildgroups(){
		var host = $("cards");
		var btns = host.querySelectorAll("button");
		var nav = document.querySelectorAll("#cats button");
		var pending = [];
		var map = {};
		var i;
		for (i = 0; i < btns.length; i++) pending.push(btns[i]);
		for (i = 0; i < nav.length; i++) {
			var cat = nav[i].getAttribute("data-cat");
			var sec = document.createElement("section");
			sec.className = "catsec";
			sec.id = "sec-" + cat;
			sec.setAttribute("data-cat", cat);
			var h = document.createElement("h2");
			var icon = nav[i].querySelector("i");
			if (icon) h.appendChild(icon.cloneNode(true));
			h.appendChild(document.createTextNode(nav[i].getAttribute("data-title") || ""));
			var grid = document.createElement("div");
			grid.className = "cards";
			sec.appendChild(h);
			sec.appendChild(grid);
			if (cat === "fav") {
				var empty = document.createElement("p");
				empty.className = "none";
				empty.id = "fav-none";
				empty.hidden = true;
				empty.textContent = "还没有收藏。点卡片右侧的星可以加进来。";
				sec.appendChild(empty);
			}
			map[cat] = grid;
			host.appendChild(sec);
		}
		for (i = 0; i < pending.length; i++) {
			var into = map[pending[i].getAttribute("data-cat") || ""];
			if (into) into.appendChild(pending[i]);
		}
		host.className = "groups";
		host.id = "groups";
	}

	function paintfav(){
		var grid = document.querySelector("#sec-fav .cards");
		if (!grid) return;
		while (grid.firstChild) grid.removeChild(grid.firstChild);
		var i;
		for (i = 0; i < favs.length; i++) {
			var src = cardof(favs[i]);
			if (!src) continue;
			var copy = src.cloneNode(true);
			copy.setAttribute("data-clone", "1");
			copy.hidden = false;
			grid.appendChild(copy);
		}
		var empty = $("fav-none");
		if (empty) empty.hidden = favs.length !== 0;
	}

	function paintnav(){
		var cats = document.querySelectorAll("#cats button");
		var i;
		for (i = 0; i < cats.length; i++)
			cats[i].className = cats[i].getAttribute("data-cat") === curcat ? "on" : "";
	}

	function jumpcat(cat){
		curcat = cat;
		spyLock = Date.now() + 800;
		if (SK.curtool !== "home") show("home");
		else paintnav();
		var sec = $("sec-" + cat);
		if (!sec) return;
		var y = sec.getBoundingClientRect().top + window.pageYOffset - 12;
		if (y < 0) y = 0;
		window.scrollTo(0, y);
	}

	function pickcat(){
		var secs = document.querySelectorAll(".catsec");
		var pick = "";
		var i;
		for (i = 0; i < secs.length; i++) {
			if (secs[i].hidden) continue;
			var top = secs[i].getBoundingClientRect().top;
			if (top - 80 <= 0) pick = secs[i].getAttribute("data-cat");
			else if (!pick) {
				pick = secs[i].getAttribute("data-cat");
				break;
			}
		}
		return pick;
	}

	function spyon(){
		if (SK.curtool !== "home") return;
		if (Date.now() < spyLock) return;
		var pick = pickcat();
		if (!pick || pick === curcat) return;
		curcat = pick;
		paintnav();
	}

	function paintcats(){
		var q = ($("tool-q").value || "").replace(/^\s+|\s+$/g, "").toLowerCase();
		var cards = document.querySelectorAll(".cards button");
		var n = 0;
		var i;
		for (i = 0; i < cards.length; i++) {
			var name = (cards[i].getAttribute("data-name") || "").toLowerCase();
			var vis = !q || name.indexOf(q) >= 0;
			cards[i].hidden = !vis;
			if (vis) n++;
		}
		var secs = document.querySelectorAll(".catsec");
		for (i = 0; i < secs.length; i++) {
			var grid = secs[i].querySelector(".cards");
			var buttons = grid ? grid.querySelectorAll("button") : [];
			var visn = 0;
			var j;
			for (j = 0; j < buttons.length; j++) if (!buttons[j].hidden) visn++;
			var cat = secs[i].getAttribute("data-cat");
			if (cat === "fav" && !q) {
				secs[i].hidden = false;
				var empty = $("fav-none");
				if (empty) empty.hidden = visn !== 0;
			}
			else secs[i].hidden = visn === 0;
		}
		$("tool-none").textContent = "没有匹配的工具";
		$("tool-none").hidden = n !== 0;
		paintnav();
	}

	function loadfav(){
		favs = [];
		var raw = "";
		try { raw = localStorage.getItem("sk-tool-fav") || ""; }
		catch (e) { return; }
		var list;
		try { list = JSON.parse(raw || "[]"); }
		catch (e) { return; }
		if (!list || typeof list.length !== "number") return;
		var i, seen = {};
		for (i = 0; i < list.length; i++) {
			var id = String(list[i] || "");
			if (id === "u-md5" || id === "u-sha" || id === "u-jsonmin" || id === "u-html"
				|| id === "u-hmin" || id === "u-cmin" || id === "u-jsmin"
				|| id === "u-xml" || id === "u-jesc" || id === "u-sql" || id === "u-hjs"
				|| id === "zh" || id === "yomi" || id === "u-name" || id === "u-cron"
				|| id === "u-ua" || id === "u-tok") id = "text";
			if (id === "u-byte" || id === "u-len" || id === "u-temp" || id === "u-px"
				|| id === "u-mass" || id === "u-area" || id === "u-vol" || id === "u-tunit"
				|| id === "u-press" || id === "u-power") id = "u-unit";
			if (!id || seen[id] || !cardof(id)) continue;
			seen[id] = 1;
			favs.push(id);
		}
	}

	function cardof(id){
		var cards = document.querySelectorAll(".cards button");
		var i;
		for (i = 0; i < cards.length; i++) {
			if (cards[i].getAttribute("data-clone")) continue;
			if (cards[i].getAttribute("data-tool") === id) return cards[i];
		}
		return null;
	}

	function favindex(id){
		var i;
		for (i = 0; i < favs.length; i++) if (favs[i] === id) return i;
		return -1;
	}

	function isfav(id){
		return favindex(id) >= 0;
	}

	function togglefav(id){
		if (!id || id === "home" || !cardof(id)) return;
		var next = [];
		var had = false;
		var i;
		for (i = 0; i < favs.length; i++) {
			if (favs[i] === id) had = true;
			else next.push(favs[i]);
		}
		if (!had) next.push(id);
		favs = next;
		try { localStorage.setItem("sk-tool-fav", JSON.stringify(favs)); }
		catch (e) {}
		paintfav();
		paintstars();
		if (SK.curtool === "home") paintcats();
	}

	function paintstars(){
		var nodes = document.querySelectorAll(".cards .star");
		var i;
		for (i = 0; i < nodes.length; i++) {
			var on = isfav(nodes[i].getAttribute("data-fav"));
			nodes[i].className = on ? "star on fa-solid fa-star" : "star fa-solid fa-star";
			nodes[i].title = on ? "取消收藏" : "收藏";
		}
		var btn = $("tool-fav");
		if (!btn) return;
		var picked = btn.getAttribute("data-fav") || "";
		var lit = picked && isfav(picked);
		btn.className = lit ? "back on" : "back";
		var label = btn.querySelector("span");
		if (label) label.textContent = lit ? "已收藏" : "收藏";
	}

	function $(id){
		return document.getElementById(id);
	}

	function msg(id, text, bad){
		var el = $(id);
		el.textContent = text || "";
		el.className = bad ? "msg bad" : "msg";
	}

	function post(url, body, done, mid, raw){
		msg(mid, "");
		var xhr = new XMLHttpRequest();
		xhr.open("POST", url, true);
		xhr.setRequestHeader("Content-Type", "application/json;charset=utf-8");
		xhr.onload = function(){
			var jo;
			try { jo = JSON.parse(xhr.responseText); }
			catch (e) {
				msg(mid, "响应不是 JSON", true);
				return;
			}
			if (!jo || jo.code !== 100) {
				if (!(raw && jo && jo.code === 101)) {
					var err = jo && jo.data;
					msg(mid, typeof err === "string" && err ? err : "失败", true);
					return;
				}
			}
			done(raw ? jo : (jo.data || {}));
		};
		xhr.onerror = function(){ msg(mid, "网络错误", true); };
		xhr.send(JSON.stringify(body));
	}

	function get(url, done, mid, fail){
		msg(mid, "");
		var xhr = new XMLHttpRequest();
		xhr.open("GET", url, true);
		xhr.onload = function(){
			var jo;
			try { jo = JSON.parse(xhr.responseText); }
			catch (e) {
				msg(mid, "响应不是 JSON", true);
				if (fail) fail();
				return;
			}
			if (!jo || jo.code !== 100) {
				var err = jo && jo.data;
				msg(mid, typeof err === "string" && err ? err : "失败", true);
				if (fail) fail();
				return;
			}
			done(jo.data);
		};
		xhr.onerror = function(){
			msg(mid, "网络错误", true);
			if (fail) fail();
		};
		xhr.send();
	}

	function fillsel(sel, rows, pick){
		sel.innerHTML = "";
		var i;
		for (i = 0; i < rows.length; i++) {
			var op = document.createElement("option");
			op.value = rows[i].value;
			op.textContent = rows[i].label;
			sel.appendChild(op);
		}
		if (pick) sel.value = pick;
		if (sel.selectedIndex < 0 && sel.options.length) sel.selectedIndex = 0;
	}

	function readfile(file, done, mid){
		var reader = new FileReader();
		reader.onload = function(){
			var raw = String(reader.result || "");
			var comma = raw.indexOf(",");
			done(comma >= 0 ? raw.substring(comma + 1) : raw);
		};
		reader.onerror = function(){ msg(mid, "读取文件失败", true); };
		reader.readAsDataURL(file);
	}

	function copytext(text, mid){
		if (!text) {
			msg(mid, "没有可复制的内容", true);
			return;
		}
		if (navigator.clipboard && navigator.clipboard.writeText) {
			navigator.clipboard.writeText(text).then(function(){
				msg(mid, "已复制");
			}, function(){
				fallback(text, mid);
			});
			return;
		}
		fallback(text, mid);
	}

	function fallback(text, mid){
		var ta = document.createElement("textarea");
		ta.value = text;
		document.body.appendChild(ta);
		ta.select();
		try {
			document.execCommand("copy");
			msg(mid, "已复制");
		}
		catch (e) {
			msg(mid, "复制失败", true);
		}
		document.body.removeChild(ta);
	}

	function lab(title, inner){
		return "<label>" + title + inner + "</label>";
	}

	function row2(a, b){
		return '<div class="row">' + a + b + "</div>";
	}

	function ta(id, rows, ph){
		return '<textarea id="' + id + '" rows="' + rows + '" placeholder="' + ph + '"></textarea>';
	}

	function textin(id, value, ph){
		return '<input id="' + id + '" type="text" value="' + value + '" placeholder="' + (ph || "") + '">';
	}

	function numin(id, value){
		return '<input id="' + id + '" type="number" value="' + value + '">';
	}

	function check(id, title, on){
		return '<label class="check"><input id="' + id + '" type="checkbox"' + (on ? " checked" : "") + "> " + title + "</label>";
	}

	function radio(name, id, title, on){
		return '<label class="check"><input id="' + id + '" name="' + name + '" type="radio" value="' + title + '"' + (on ? " checked" : "") + "> " + title + "</label>";
	}

	function opts(list, sel){
		var html = "";
		var i;
		for (i = 0; i < list.length; i++) {
			html += '<option value="' + list[i][0] + '"' + (list[i][0] === sel ? " selected" : "") + ">" + list[i][1] + "</option>";
		}
		return html;
	}

	function val(id){
		var el = $(id);
		return el && el.value != null ? String(el.value) : "";
	}

	function onbox(id){
		var el = $(id);
		return !!(el && el.checked);
	}

	function ymd(d){
		return d.getFullYear() + "-" + pad2(d.getMonth() + 1) + "-" + pad2(d.getDate());
	}

	function pad2(n){
		return (n < 10 ? "0" : "") + n;
	}

	function fmtdt(d){
		return d.getFullYear() + "-" + pad2(d.getMonth() + 1) + "-" + pad2(d.getDate())
			+ " " + pad2(d.getHours()) + ":" + pad2(d.getMinutes()) + ":" + pad2(d.getSeconds());
	}

	function trimnum(n){
		if (n === 0) return "0";
		if (!isFinite(n)) throw new Error("结果无效");
		var neg = n < 0;
		var a = Math.abs(n);
		var s;
		if (a >= 1e15 || a < 1e-8) s = a.toExponential(6);
		else {
			s = a.toFixed(10);
			s = s.replace(/\.?0+$/, "");
		}
		return (neg ? "-" : "") + s;
	}

	function utf8bytes(s){
		s = String(s);
		if (s.length > 200000) throw new Error("文字太长");
		if (typeof TextEncoder === "function") return new TextEncoder().encode(s);
		var bin = unescape(encodeURIComponent(s));
		var a = new Uint8Array(bin.length);
		var i;
		for (i = 0; i < bin.length; i++) a[i] = bin.charCodeAt(i) & 255;
		return a;
	}

	function hexbytes(arr){
		var s = "";
		var i;
		for (i = 0; i < arr.length; i++) {
			var v = arr[i];
			s += (v < 16 ? "0" : "") + v.toString(16);
		}
		return s;
	}

	function randint(n){
		if (!(n >= 1)) throw new Error("范围无效");
		if (n > 0x100000000) throw new Error("范围太大");
		if (!window.crypto || typeof crypto.getRandomValues !== "function") throw new Error("浏览器不能生成随机数");
		var buf = new Uint32Array(1);
		var limit = Math.floor(0x100000000 / n) * n;
		var x;
		do {
			crypto.getRandomValues(buf);
			x = buf[0];
		} while (x >= limit);
		return x % n;
	}

	function unitconv(text, unit, table, scale){
		var raw = String(text).trim();
		var n = Number(raw);
		if (raw === "" || !isFinite(n)) throw new Error("请输入数字");
		var base = scale[unit];
		if (!base) throw new Error("请选择单位");
		var v = n * base;
		var lines = [];
		var i;
		for (i = 0; i < table.length; i++) lines.push(table[i][1] + "  " + trimnum(v / scale[table[i][0]]));
		return lines.join("\n");
	}

	function htmlenc(text){
		text = String(text);
		if (text.length > 200000) throw new Error("文字太长");
		return text.replace(/[&<>"']/g, function(ch){
			if (ch === "&") return "&amp;";
			if (ch === "<") return "&lt;";
			if (ch === ">") return "&gt;";
			if (ch === '"') return "&quot;";
			return "&#39;";
		});
	}

	function colorconv(mode, text){
		var rgb = colorrgb(mode, text);
		var hsl = rgb2hsl(rgb[0], rgb[1], rgb[2]);
		var hsv = rgb2hsv(rgb[0], rgb[1], rgb[2]);
		var cmyk = rgb2cmyk(rgb[0], rgb[1], rgb[2]);
		return "HEX  #" + hex2(rgb[0]) + hex2(rgb[1]) + hex2(rgb[2])
			+ "\nRGB  " + rgb[0] + ", " + rgb[1] + ", " + rgb[2]
			+ "\nHSL  " + hsl[0] + ", " + hsl[1] + "%, " + hsl[2] + "%"
			+ "\nHSV  " + hsv[0] + ", " + hsv[1] + "%, " + hsv[2] + "%"
			+ "\nCMYK  " + cmyk[0] + "%, " + cmyk[1] + "%, " + cmyk[2] + "%, " + cmyk[3] + "%"
			+ "\nCMYK 用的是简单公式，不是印刷配置文件。";
	}

	function colorrgb(mode, text){
		text = String(text).trim();
		if (mode === "hex") {
			var h = text.replace(/^#/, "");
			if (!/^[0-9a-f]{3}$/i.test(h) && !/^[0-9a-f]{6}$/i.test(h)) throw new Error("HEX 用 #RGB 或 #RRGGBB");
			if (h.length === 3) h = h.charAt(0) + h.charAt(0) + h.charAt(1) + h.charAt(1) + h.charAt(2) + h.charAt(2);
			return [parseInt(h.slice(0, 2), 16), parseInt(h.slice(2, 4), 16), parseInt(h.slice(4, 6), 16)];
		}
		var p = text.split(/[,，\s]+/);
		if (p.length < 3 || (mode === "cmyk" && p.length < 4)) throw new Error("请按所选格式填写，用逗号或空格分开");
		if (mode === "rgb") return [byte8(p[0]), byte8(p[1]), byte8(p[2])];
		if (mode === "hsl") return hsl2rgb(SK.hue(p[0]), pct(p[1]), pct(p[2]));
		if (mode === "hsv") return SK.hsv2rgb(SK.hue(p[0]), pct(p[1]), pct(p[2]));
		if (mode === "cmyk") return cmyk2rgb(pct(p[0]), pct(p[1]), pct(p[2]), pct(p[3]));
		throw new Error("请选择格式");
	}

	function byte8(s){
		var n = Number(s);
		if (!isFinite(n) || n < 0 || n > 255) throw new Error("RGB 分量要在 0 到 255");
		return Math.round(n);
	}

	function pct(s){
		var n = Number(String(s).replace("%", ""));
		if (!isFinite(n) || n < 0 || n > 100) throw new Error("百分比要在 0 到 100");
		return n;
	}

	function hex2(n){
		var s = n.toString(16);
		return (s.length < 2 ? "0" : "") + s;
	}

	function rgb2hsl(r, g, b){
		r /= 255; g /= 255; b /= 255;
		var max = Math.max(r, g, b);
		var min = Math.min(r, g, b);
		var l = (max + min) / 2;
		var h = 0;
		var s = 0;
		if (max !== min) {
			var d = max - min;
			s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
			if (max === r) h = (g - b) / d + (g < b ? 6 : 0);
			else if (max === g) h = (b - r) / d + 2;
			else h = (r - g) / d + 4;
			h *= 60;
		}
		return [Math.round(h), Math.round(s * 100), Math.round(l * 100)];
	}

	function rgb2hsv(r, g, b){
		r /= 255; g /= 255; b /= 255;
		var max = Math.max(r, g, b);
		var min = Math.min(r, g, b);
		var d = max - min;
		var h = 0;
		if (d !== 0) {
			if (max === r) h = (g - b) / d + (g < b ? 6 : 0);
			else if (max === g) h = (b - r) / d + 2;
			else h = (r - g) / d + 4;
			h *= 60;
		}
		var s = max === 0 ? 0 : d / max;
		return [Math.round(h), Math.round(s * 100), Math.round(max * 100)];
	}

	function rgb2cmyk(r, g, b){
		if (r === 0 && g === 0 && b === 0) return [0, 0, 0, 100];
		var rp = r / 255;
		var gp = g / 255;
		var bp = b / 255;
		var k = 1 - Math.max(rp, gp, bp);
		return [
			Math.round((1 - rp - k) / (1 - k) * 100),
			Math.round((1 - gp - k) / (1 - k) * 100),
			Math.round((1 - bp - k) / (1 - k) * 100),
			Math.round(k * 100),
		];
	}

	function hsl2rgb(h, s, l){
		s /= 100; l /= 100;
		var c = (1 - Math.abs(2 * l - 1)) * s;
		var hp = h / 60;
		var x = c * (1 - Math.abs(hp % 2 - 1));
		var m = l - c / 2;
		return SK.hue2rgb(hp, c, x, m);
	}

	function cmyk2rgb(c, m, y, k){
		c /= 100; m /= 100; y /= 100; k /= 100;
		return [
			Math.round(255 * (1 - c) * (1 - k)),
			Math.round(255 * (1 - m) * (1 - k)),
			Math.round(255 * (1 - y) * (1 - k)),
		];
	}

	function stoptick(){
		if (SK.watchOn) SK.watchAcc += Date.now() - SK.watch0;
		SK.watchOn = false;
		if (SK.tick) {
			clearInterval(SK.tick);
			SK.tick = null;
		}
	}

	function seticopic(file){
		if (!file || String(file.type || "").indexOf("image/") !== 0) {
			msg("box-msg", "请放入图片", true);
			return;
		}
		if (file.size > 20 * 1024 * 1024) {
			msg("box-msg", "图片要小于 20 MB", true);
			return;
		}
		SK.icoPic = file;
		if (SK.icoUrl) URL.revokeObjectURL(SK.icoUrl);
		SK.icoUrl = URL.createObjectURL(file);
		var img = $("ico-img");
		if (img) {
			img.onload = SK.drawicoprev;
			img.src = SK.icoUrl;
			img.hidden = false;
		}
		msg("box-msg", "已放入图片");
	}

	function esc(s){
		return String(s == null ? "" : s)
			.replace(/&/g, "&amp;")
			.replace(/</g, "&lt;")
			.replace(/>/g, "&gt;");
	}

	function openutil(name){
		stoptick();
		var spec = SK.utils[name];
		if (!spec) return;
		util = name;
		$("box-title").textContent = spec.title;
		$("box-go").textContent = spec.go;
		$("box-body").innerHTML = spec.body();
		$("box-out").value = "";
		$("box-go").parentElement.style.display = "";
		$("box-copy").style.display = "";
		$("box-out").parentElement.style.display = "";
		if (SK.showreout) SK.showreout(false);
		msg("box-msg", "");
		if (spec.hideGo) $("box-go").parentElement.style.display = "none";
		if (spec.hideCopy) $("box-copy").style.display = "none";
		if (spec.hideOut) $("box-out").parentElement.style.display = "none";
		if (spec.open) spec.open();
	}

	function utilrun(){
		msg("box-msg", "");
		var spec = SK.utils[util];
		if (!spec || !spec.run) return;
		if (spec.own) {
			spec.run();
			return;
		}
		SK.reNote = "";
		try {
			$("box-out").value = spec.run();
			msg("box-msg", SK.reNote || "完成");
		}
		catch (ex) {
			$("box-out").value = "";
			if (SK.showreout) SK.showreout(false);
			msg("box-msg", ex && ex.message ? ex.message : "失败", true);
		}
	}

	SK.boot = boot;
	SK.onclick = onclick;
	SK.toolhash = toolhash;
	SK.pushhash = pushhash;
	SK.applyhash = applyhash;
	SK.show = show;
	SK.marktool = marktool;
	SK.cattitle = cattitle;
	SK.buildgroups = buildgroups;
	SK.paintfav = paintfav;
	SK.paintnav = paintnav;
	SK.jumpcat = jumpcat;
	SK.pickcat = pickcat;
	SK.spyon = spyon;
	SK.paintcats = paintcats;
	SK.loadfav = loadfav;
	SK.cardof = cardof;
	SK.favindex = favindex;
	SK.isfav = isfav;
	SK.togglefav = togglefav;
	SK.paintstars = paintstars;
	SK.$ = $;
	SK.msg = msg;
	SK.post = post;
	SK.get = get;
	SK.fillsel = fillsel;
	SK.readfile = readfile;
	SK.copytext = copytext;
	SK.fallback = fallback;
	SK.lab = lab;
	SK.row2 = row2;
	SK.ta = ta;
	SK.textin = textin;
	SK.numin = numin;
	SK.check = check;
	SK.radio = radio;
	SK.opts = opts;
	SK.val = val;
	SK.onbox = onbox;
	SK.ymd = ymd;
	SK.pad2 = pad2;
	SK.fmtdt = fmtdt;
	SK.trimnum = trimnum;
	SK.utf8bytes = utf8bytes;
	SK.hexbytes = hexbytes;
	SK.randint = randint;
	SK.unitconv = unitconv;
	SK.htmlenc = htmlenc;
	SK.colorconv = colorconv;
	SK.colorrgb = colorrgb;
	SK.byte8 = byte8;
	SK.pct = pct;
	SK.hex2 = hex2;
	SK.rgb2hsl = rgb2hsl;
	SK.rgb2hsv = rgb2hsv;
	SK.rgb2cmyk = rgb2cmyk;
	SK.hsl2rgb = hsl2rgb;
	SK.cmyk2rgb = cmyk2rgb;
	SK.stoptick = stoptick;
	SK.seticopic = seticopic;
	SK.esc = esc;
	SK.openutil = openutil;
	SK.utilrun = utilrun;
})();
(function(){
	var asrItems = [];
	var asrRec = null;
	function loadasr(){
		if (asrItems.length) return;
		SK.get("/api/asr/models", function(data){
			asrItems = data && data.length != null ? data : [];
			var hasOff = false;
			var hasWin = false;
			var i;
			for (i = 0; i < asrItems.length; i++) {
				if (asrItems[i].streaming) continue;
				if (asrItems[i].type === "Windows") hasWin = true;
				else hasOff = true;
			}
			var rows = [];
			if (hasOff) rows.push({ value: "offline", label: "离线模型" });
			if (hasWin) rows.push({ value: "windows", label: "Windows 语音识别" });
			SK.fillsel(SK.$("asr-eng"), rows, "");
			fillasrmodels();
		}, "asr-msg");
	}

	function fillasrmodels(){
		var win = SK.$("asr-eng").value === "windows";
		var rows = [];
		var i;
		for (i = 0; i < asrItems.length; i++) {
			var it = asrItems[i];
			if (!it || it.streaming) continue;
			var isWin = it.type === "Windows";
			if (isWin !== win) continue;
			rows.push({ value: it.name || "", label: it.name || "" });
		}
		SK.fillsel(SK.$("asr-model"), rows, "");
	}

	function postasr(b64, filename){
		SK.post("/api/asr", {
			base64: b64,
			filename: filename || "",
			model: SK.$("asr-model").value,
			lang: SK.$("asr-lang").value,
		}, function(data){
			SK.$("asr-out").value = (data && data.text) || "";
		}, "asr-msg");
	}

	function asrfile(){
		var file = SK.$("asr-file").files && SK.$("asr-file").files[0];
		if (!file) {
			SK.msg("asr-msg", "请选择音频", true);
			return;
		}
		SK.readfile(file, function(b64){ postasr(b64, file.name); }, "asr-msg");
	}

	function setasr(on){
		var b = SK.$("asr-rec");
		var i = b.querySelector("i");
		var s = b.querySelector("span");
		if (i) i.className = on ? "fa-solid fa-stop" : "fa-solid fa-microphone";
		if (s) s.textContent = on ? "停止并识别" : "录音";
	}

	function asrrec(){
		if (asrRec) {
			asrRec.stop();
			return;
		}
		if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
			SK.msg("asr-msg", "浏览器不能录音", true);
			return;
		}
		navigator.mediaDevices.getUserMedia({ audio: true }).then(function(stream){
			var chunks = [];
			var rec;
			try { rec = new MediaRecorder(stream); }
			catch (e) {
				stream.getTracks().forEach(function(tr){ tr.stop(); });
				SK.msg("asr-msg", "无法开始录音", true);
				return;
			}
			asrRec = rec;
			setasr(true);
			SK.msg("asr-msg", "正在录音");
			rec.ondataavailable = function(ev){
				if (ev.data && ev.data.size) chunks.push(ev.data);
			};
			rec.onstop = function(){
				stream.getTracks().forEach(function(tr){ tr.stop(); });
				asrRec = null;
				setasr(false);
				var blob = new Blob(chunks, { type: rec.mimeType || "audio/webm" });
				SK.readfile(blob, function(b64){ postasr(b64, "rec.webm"); }, "asr-msg");
			};
			rec.start();
		}, function(){
			SK.msg("asr-msg", "无法使用麦克风", true);
		});
	}

	SK.loadasr = loadasr;
	SK.fillasrmodels = fillasrmodels;
	SK.postasr = postasr;
	SK.asrfile = asrfile;
	SK.setasr = setasr;
	SK.asrrec = asrrec;
})();
(function(){
	function cal(){
		SK.post("/api/calendar", {
			date: SK.$("cal-date").value,
			cal: SK.$("cal-id").value,
		}, function(data){
			var rows = [
				["公历", data.gregorian],
				["干支", data.ganzhi],
				["纪年", data.era],
				["年", data.year],
				["月", data.month],
				["日", data.day],
				["星期", data.week],
				["闰月", data.leap ? "是" : "否"],
			];
			var html = "";
			var i;
			for (i = 0; i < rows.length; i++) {
				if (!rows[i][1] && rows[i][0] !== "闰月") continue;
				html += "<dt>" + rows[i][0] + "</dt><dd>" + SK.esc(rows[i][1]) + "</dd>";
			}
			SK.$("cal-out").innerHTML = html;
		}, "cal-msg");
	}

	SK.cal = cal;
})();
(function(){
	var ocrPacks = [];
	var ocrPic = null;
	var ocrPicUrl = "";
	var ocrLoading = false;
	var ocrAfter = [];
	var ocrGen = 0;
	function loadoocr(after){
		if (ocrPacks.length) {
			if (after) after();
			return;
		}
		if (after) ocrAfter.push(after);
		if (ocrLoading) return;
		ocrLoading = true;
		SK.get("/api/ocr/models", function(data){
			ocrLoading = false;
			ocrPacks = (data && data.packs) || [];
			var rows = [];
			var i;
			for (i = 0; i < ocrPacks.length; i++)
				rows.push({ value: ocrPacks[i].id, label: ocrPacks[i].name || ocrPacks[i].id });
			var cur = (data && data.current) || {};
			SK.fillsel(SK.$("ocr-pack"), rows, cur.pack || "");
			if (cur.device) SK.$("ocr-dev").value = cur.device;
			fillocrmodels(cur.language || "");
			var wait = ocrAfter;
			ocrAfter = [];
			for (i = 0; i < wait.length; i++) wait[i]();
		}, "ocr-msg", function(){
			ocrLoading = false;
			ocrAfter = [];
		});
	}

	function ocrpack(){
		var id = SK.$("ocr-pack").value;
		var i;
		for (i = 0; i < ocrPacks.length; i++)
			if (ocrPacks[i].id === id) return ocrPacks[i];
		return null;
	}

	function fillocrmodels(prefer){
		var pack = ocrpack();
		var models = (pack && pack.models) || [];
		var rows = [];
		var i;
		for (i = 0; i < models.length; i++)
			rows.push({ value: models[i].id, label: models[i].name || models[i].id });
		var pick = typeof prefer === "string" ? prefer : "";
		SK.fillsel(SK.$("ocr-model"), rows, pick);
		var win = !pack || pack.engine === "winocr";
		SK.$("ocr-dev-lab").style.display = win ? "none" : "";
		SK.$("ocr-model-lab").firstChild.nodeValue = win ? "语言" : "模型";
	}

	function onocrfile(){
		var file = SK.$("ocr-file").files && SK.$("ocr-file").files[0];
		if (file) setocrpic(file);
	}

	function onocrpaste(ev){
		if (SK.curtool !== "ocr" && SK.curtool !== "u-ico") return;
		var cd = ev.clipboardData;
		if (!cd) return;
		var file = null;
		var i;
		if (cd.items) {
			for (i = 0; i < cd.items.length; i++) {
				var item = cd.items[i];
				if (item.kind === "file" && item.type.indexOf("image/") === 0) {
					file = item.getAsFile();
					break;
				}
			}
		}
		if (!file && cd.files && cd.files.length && cd.files[0].type.indexOf("image/") === 0)
			file = cd.files[0];
		if (!file) return;
		ev.preventDefault();
		if (SK.curtool === "u-ico") SK.seticopic(file);
		else setocrpic(file);
	}

	function setocrpic(file){
		ocrPic = file;
		if (ocrPicUrl) URL.revokeObjectURL(ocrPicUrl);
		ocrPicUrl = URL.createObjectURL(file);
		var img = SK.$("ocr-img");
		img.src = ocrPicUrl;
		img.hidden = false;
		SK.$("ocr-out").value = "";
		clearocrmeta();
		loadoocr(ocrgo);
	}

	function clearocrmeta(){
		var meta = SK.$("ocr-meta");
		if (!meta) return;
		meta.textContent = "";
		meta.hidden = true;
	}

	function ocrmeta(stat, wallMs){
		if (!stat) return "";
		var inferS = (Math.max(0, Number(stat.infer) || 0) / 1000).toFixed(2);
		var wallS = (Math.max(0, wallMs) / 1000).toFixed(2);
		var loadPart = stat.load > 0 ? " · 加载 " + (stat.load / 1000).toFixed(2) + "s" : "";
		var conf = (Number(stat.score) || 0).toFixed(2);
		var device = stat.device || "";
		var model = stat.model ? " | " + stat.model : "";
		var res = stat.width > 0 && stat.height > 0 ? " | " + stat.width + "×" + stat.height : "";
		return "推理 " + inferS + "s · 端到端 " + wallS + "s" + loadPart
			+ " | 置信度 " + conf + " | " + device + model + res
			+ " | " + (stat.lines || 0) + " 行 | 边长" + (stat.limit || 0);
	}

	function ocrgo(){
		var file = ocrPic || (SK.$("ocr-file").files && SK.$("ocr-file").files[0]);
		if (!file) {
			SK.msg("ocr-msg", "请选择或粘贴图片", true);
			return;
		}
		var pack = ocrpack();
		if (!pack) {
			SK.msg("ocr-msg", "没有可用的识别引擎", true);
			return;
		}
		clearocrmeta();
		var gen = ++ocrGen;
		var wall0 = Date.now();
		SK.msg("ocr-msg", "识别中…");
		SK.readfile(file, function(b64){
			if (gen !== ocrGen) return;
			SK.post("/api/ocr", {
				base64: b64,
				options: {
					"ocr.engine": pack.engine,
					"ocr.pack": pack.id,
					"ocr.language": SK.$("ocr-model").value,
					"ocr.device": SK.$("ocr-dev").value,
					"data.format": "text",
				},
			}, function(jo){
				if (gen !== ocrGen) return;
				SK.msg("ocr-msg", "");
				var data = jo ? jo.data : "";
				SK.$("ocr-out").value = typeof data === "string" ? data : "";
				var meta = SK.$("ocr-meta");
				var text = ocrmeta(jo && jo.stat, Date.now() - wall0);
				if (meta) {
					meta.textContent = text;
					meta.hidden = !text;
				}
				if (jo && jo.code === 101)
					SK.msg("ocr-msg", typeof data === "string" && data ? data : "未检测到文字", true);
			}, "ocr-msg", true);
			if (gen === ocrGen) SK.msg("ocr-msg", "识别中…");
		}, "ocr-msg");
	}

	SK.loadoocr = loadoocr;
	SK.ocrpack = ocrpack;
	SK.fillocrmodels = fillocrmodels;
	SK.onocrfile = onocrfile;
	SK.onocrpaste = onocrpaste;
	SK.setocrpic = setocrpic;
	SK.clearocrmeta = clearocrmeta;
	SK.ocrmeta = ocrmeta;
	SK.ocrgo = ocrgo;
})();
(function(){
	function qrcaptext(text){
		return String(text || "").replace(/\r|\n/g, " ").trim();
	}

	function setqrcap(text){
		var cap = SK.$("qr-cap");
		if (!cap) return;
		var line = qrcaptext(text);
		cap.textContent = line;
		cap.title = line;
		cap.hidden = !line;
		if (!line) cap.style.marginTop = "";
	}

	function qrgap(){
		var img = SK.$("qr-img");
		var cap = SK.$("qr-cap");
		if (!img || !cap || cap.hidden || !img.naturalWidth) return;
		var w = img.naturalWidth;
		var h = img.naturalHeight;
		var canvas = document.createElement("canvas");
		canvas.width = w;
		canvas.height = h;
		var ctx = canvas.getContext("2d");
		var bottom = 0;
		try {
			ctx.drawImage(img, 0, 0);
			var data = ctx.getImageData(0, 0, w, h).data;
			var y;
			var x;
			for (y = h - 1; y >= 0; y--) {
				var white = true;
				for (x = 0; x < w; x++) {
					var i = (y * w + x) * 4;
					if (data[i] < 250 || data[i + 1] < 250 || data[i + 2] < 250) {
						white = false;
						break;
					}
				}
				if (!white) break;
				bottom++;
			}
		}
		catch (e) { bottom = 0; }
		var shown = img.clientWidth > 0 ? bottom * (img.clientWidth / w) : bottom;
		var half = parseFloat(window.getComputedStyle(cap).fontSize) / 2;
		if (isNaN(half)) half = 8;
		cap.style.marginTop = (half - shown) + "px";
	}

	function qrmake(){
		var text = SK.$("qr-in").value;
		SK.post("/api/qrmake", {
			text: text,
			format: SK.$("qr-fmt").value,
			encoding: SK.$("qr-enc").value,
		}, function(data){
			var img = SK.$("qr-img");
			if (!data || !data.png) {
				img.removeAttribute("src");
				img.hidden = true;
				setqrcap("");
				SK.msg("qr-msg", "没有图片", true);
				return;
			}
			img.onload = function(){ requestAnimationFrame(qrgap); };
			img.src = "data:image/png;base64," + data.png;
			img.hidden = false;
			setqrcap(text);
			SK.msg("qr-msg", "已生成");
		}, "qr-msg");
	}

	function qrscan(){
		var file = SK.$("qr-file").files && SK.$("qr-file").files[0];
		if (!file) {
			SK.msg("qr-smsg", "请选择图片", true);
			return;
		}
		var reader = new FileReader();
		reader.onload = function(){
			var raw = String(reader.result || "");
			var comma = raw.indexOf(",");
			var b64 = comma >= 0 ? raw.substring(comma + 1) : raw;
			SK.post("/api/qrscan", { base64: b64, format: "dict" }, function(data){
				var lines = [];
				var i;
				if (typeof data === "string") {
					SK.$("qr-out").value = data;
					return;
				}
				var arr = data;
				if (!arr || typeof arr.length !== "number") arr = [];
				for (i = 0; i < arr.length; i++) {
					var item = arr[i] || {};
					lines.push((item.type || "") + "  " + (item.text || ""));
				}
				SK.$("qr-out").value = lines.join("\n");
				if (!lines.length) SK.msg("qr-smsg", "未检测到条码或二维码", true);
			}, "qr-smsg");
		};
		reader.onerror = function(){ SK.msg("qr-smsg", "读图失败", true); };
		reader.readAsDataURL(file);
	}

	SK.qrcaptext = qrcaptext;
	SK.setqrcap = setqrcap;
	SK.qrgap = qrgap;
	SK.qrmake = qrmake;
	SK.qrscan = qrscan;
})();
(function(){
	function textop(op){
		if (op === "yomi" || op === "yomimono") {
			SK.post("/api/jpyomi", { text: SK.$("tx-in").value, mono: op === "yomimono" }, function(data){
				var ruby = data.ruby || "";
				var yomi = data.yomi || "";
				SK.$("tx-out").value = ruby && yomi && ruby !== yomi ? ruby + "\n" + yomi : (yomi || ruby);
			}, "tx-msg");
			return;
		}
		if (op === "trad" || op === "simp") {
			SK.post("/api/zhconv", { text: SK.$("tx-in").value, to: op }, function(data){
				SK.$("tx-out").value = data.text || "";
			}, "tx-msg");
			return;
		}
		if (op === "md5" || op === "sha" || op === "jsonmin" || op === "htmlenc" || op === "htmldec"
			|| op === "hmin" || op === "cmin" || op === "jsmin"
			|| op === "jesc" || op === "juesc" || op === "xml" || op === "xmlmin"
			|| op === "sql" || op === "sqlmin" || op === "hjs"
			|| op === "name" || op === "cron" || op === "tok" || op === "ua") {
			textlocal(op);
			return;
		}
		SK.post("/api/text", { text: SK.$("tx-in").value, op: op }, function(data){
			SK.$("tx-out").value = data.text || "";
		}, "tx-msg");
	}

	function textlocal(op){
		var text = SK.$("tx-in").value;
		if (op === "sha") {
			if (text.length > 200000) {
				SK.msg("tx-msg", "文字太长", true);
				return;
			}
			if (!window.crypto || !crypto.subtle || typeof crypto.subtle.digest !== "function") {
				SK.msg("tx-msg", "浏览器没有 SHA-256", true);
				return;
			}
			SK.msg("tx-msg", "计算中");
			crypto.subtle.digest("SHA-256", SK.utf8bytes(text)).then(function(buf){
				SK.$("tx-out").value = SK.hexbytes(new Uint8Array(buf));
				SK.msg("tx-msg", "完成");
			}, function(){
				SK.msg("tx-msg", "SHA-256 失败", true);
			});
			return;
		}
		try {
			var out = "";
			if (op === "md5") out = md5hex(text);
			else if (op === "jsonmin") out = jsonmin(text);
			else if (op === "jesc") out = jsonesc(text, "enc");
			else if (op === "juesc") out = jsonesc(text, "dec");
			else if (op === "xml") out = xmlfmt(text, "pretty");
			else if (op === "xmlmin") out = xmlfmt(text, "min");
			else if (op === "sql") out = sqlfmt(text, "pretty");
			else if (op === "sqlmin") out = sqlfmt(text, "min");
			else if (op === "hjs") out = html2js(text);
			else if (op === "name") out = nameconv(text);
			else if (op === "cron") out = crondesc(text);
			else if (op === "tok") out = tokencount(text);
			else if (op === "ua") out = uaparse(text);
			else if (op === "htmlenc") out = SK.htmlenc(text);
			else if (op === "htmldec") out = htmldec(text);
			else if (op === "hmin") out = htmlmin(text);
			else if (op === "cmin") out = cssmin(text);
			else if (op === "jsmin") out = jsmin(text);
			SK.$("tx-out").value = out;
			SK.msg("tx-msg", "完成");
		}
		catch (ex) {
			SK.$("tx-out").value = "";
			SK.msg("tx-msg", ex && ex.message ? ex.message : "失败", true);
		}
	}

	function htmldec(text){
		text = String(text);
		if (text.length > 200000) throw new Error("文字太长");
		var named = {
			amp: "&", lt: "<", gt: ">", quot: '"', apos: "'", nbsp: "\u00a0",
			copy: "\u00a9", reg: "\u00ae", trade: "\u2122", mdash: "\u2014",
			ndash: "\u2013", hellip: "\u2026", middot: "\u00b7", times: "\u00d7", divide: "\u00f7",
		};
		return text.replace(/&(#x[0-9a-f]+|#\d+|[a-z][a-z0-9]*);/gi, function(all, body){
			if (body.charAt(0) !== "#") {
				var v = named[body.toLowerCase()];
				return v == null ? all : v;
			}
			var hex = body.charAt(1) === "x" || body.charAt(1) === "X";
			var n = parseInt(body.slice(hex ? 2 : 1), hex ? 16 : 10);
			if (!isFinite(n) || n < 0 || n > 0x10FFFF) return all;
			return String.fromCodePoint(n);
		});
	}

	function jsonmin(text){
		text = String(text);
		if (text.length > 200000) throw new Error("文字太长");
		try { return JSON.stringify(JSON.parse(text)); }
		catch (ex) { throw new Error("不是合法的 JSON"); }
	}

	function md5hex(text){
		var bytes = SK.utf8bytes(text);
		var n = bytes.length;
		var bitLen = n * 8;
		var withPad = n + 1;
		while (withPad % 64 !== 56) withPad++;
		var buf = new Uint8Array(withPad + 8);
		buf.set(bytes);
		buf[n] = 0x80;
		var lo = bitLen >>> 0;
		var hi = Math.floor(bitLen / 0x100000000);
		buf[withPad] = lo & 255;
		buf[withPad + 1] = (lo >>> 8) & 255;
		buf[withPad + 2] = (lo >>> 16) & 255;
		buf[withPad + 3] = (lo >>> 24) & 255;
		buf[withPad + 4] = hi & 255;
		buf[withPad + 5] = (hi >>> 8) & 255;
		buf[withPad + 6] = (hi >>> 16) & 255;
		buf[withPad + 7] = (hi >>> 24) & 255;
		var k = [
			0xd76aa478, 0xe8c7b756, 0x242070db, 0xc1bdceee, 0xf57c0faf, 0x4787c62a, 0xa8304613, 0xfd469501,
			0x698098d8, 0x8b44f7af, 0xffff5bb1, 0x895cd7be, 0x6b901122, 0xfd987193, 0xa679438e, 0x49b40821,
			0xf61e2562, 0xc040b340, 0x265e5a51, 0xe9b6c7aa, 0xd62f105d, 0x02441453, 0xd8a1e681, 0xe7d3fbc8,
			0x21e1cde6, 0xc33707d6, 0xf4d50d87, 0x455a14ed, 0xa9e3e905, 0xfcefa3f8, 0x676f02d9, 0x8d2a4c8a,
			0xfffa3942, 0x8771f681, 0x6d9d6122, 0xfde5380c, 0xa4beea44, 0x4bdecfa9, 0xf6bb4b60, 0xbebfbc70,
			0x289b7ec6, 0xeaa127fa, 0xd4ef3085, 0x04881d05, 0xd9d4d039, 0xe6db99e5, 0x1fa27cf8, 0xc4ac5665,
			0xf4292244, 0x432aff97, 0xab9423a7, 0xfc93a039, 0x655b59c3, 0x8f0ccc92, 0xffeff47d, 0x85845dd1,
			0x6fa87e4f, 0xfe2ce6e0, 0xa3014314, 0x4e0811a1, 0xf7537e82, 0xbd3af235, 0x2ad7d2bb, 0xeb86d391,
		];
		var shift = [
			7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
			5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
			4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
			6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
		];
		var a0 = 0x67452301;
		var b0 = 0xefcdab89;
		var c0 = 0x98badcfe;
		var d0 = 0x10325476;
		var off;
		for (off = 0; off < buf.length; off += 64) {
			var m = new Uint32Array(16);
			var j;
			for (j = 0; j < 16; j++) {
				var p = off + j * 4;
				m[j] = (buf[p] | (buf[p + 1] << 8) | (buf[p + 2] << 16) | (buf[p + 3] << 24)) >>> 0;
			}
			var a = a0;
			var b = b0;
			var c = c0;
			var d = d0;
			var t;
			for (t = 0; t < 64; t++) {
				var f;
				var g;
				if (t < 16) {
					f = (b & c) | (~b & d);
					g = t;
				}
				else if (t < 32) {
					f = (d & b) | (~d & c);
					g = (5 * t + 1) % 16;
				}
				else if (t < 48) {
					f = b ^ c ^ d;
					g = (3 * t + 5) % 16;
				}
				else {
					f = c ^ (b | ~d);
					g = (7 * t) % 16;
				}
				f = (f + a + k[t] + m[g]) >>> 0;
				var rot = ((f << shift[t]) | (f >>> (32 - shift[t]))) >>> 0;
				a = d;
				d = c;
				c = b;
				b = (b + rot) >>> 0;
			}
			a0 = (a0 + a) >>> 0;
			b0 = (b0 + b) >>> 0;
			c0 = (c0 + c) >>> 0;
			d0 = (d0 + d) >>> 0;
		}
		return lehex(a0) + lehex(b0) + lehex(c0) + lehex(d0);
	}

	function lehex(n){
		var s = "";
		var i;
		for (i = 0; i < 4; i++) {
			var b = (n >>> (i * 8)) & 255;
			s += (b < 16 ? "0" : "") + b.toString(16);
		}
		return s;
	}

	function nameconv(text){
		var raw = String(text).trim();
		if (!raw) throw new Error("请输入名称");
		if (raw.length > 200) throw new Error("太长");
		var bits = raw.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[^A-Za-z0-9]+/g, " ").split(/\s+/);
		var w = [];
		var i;
		for (i = 0; i < bits.length; i++) if (bits[i]) w.push(bits[i].toLowerCase());
		if (!w.length) throw new Error("没有可用的字母或数字");
		function cap(s){ return s.charAt(0).toUpperCase() + s.slice(1); }
		var camel = w[0];
		var pascal = "";
		for (i = 0; i < w.length; i++) {
			if (i > 0) camel += cap(w[i]);
			pascal += cap(w[i]);
		}
		return "小驼峰  " + camel
			+ "\n大驼峰  " + pascal
			+ "\n下划线  " + w.join("_")
			+ "\n大写下划线  " + w.join("_").toUpperCase()
			+ "\n短横线  " + w.join("-");
	}

	function jsonesc(text, mode){
		text = String(text);
		if (text.length > 200000) throw new Error("文字太长");
		if (mode === "dec") {
			var src = text.trim();
			if (!src) throw new Error("请输入文字");
			if (src.charAt(0) !== '"') src = '"' + src + '"';
			var v;
			try { v = JSON.parse(src); }
			catch (ex) { throw new Error("不是合法的 JSON 字符串"); }
			if (typeof v !== "string") throw new Error("请输入 JSON 字符串");
			return v;
		}
		return JSON.stringify(text);
	}

	function uaparse(text){
		var s = String(text).trim();
		if (!s) throw new Error("请输入 User-Agent");
		if (s.length > 2000) throw new Error("太长");
		var os = "未知";
		if (/Windows NT 10\.0/.test(s)) os = "Windows 10 或 11";
		else if (/Windows NT 6\.3/.test(s)) os = "Windows 8.1";
		else if (/Windows NT 6\.1/.test(s)) os = "Windows 7";
		else if (/Android/.test(s)) os = "Android";
		else if (/iPhone|iPad|iPod/.test(s)) os = "iOS";
		else if (/Mac OS X/.test(s)) os = "macOS";
		else if (/Linux/.test(s)) os = "Linux";
		var br = "未知";
		var m;
		if ((m = /Edg\/(\d+)/.exec(s))) br = "Edge " + m[1];
		else if ((m = /OPR\/(\d+)/.exec(s))) br = "Opera " + m[1];
		else if ((m = /Chrome\/(\d+)/.exec(s))) br = "Chrome " + m[1];
		else if ((m = /Firefox\/(\d+)/.exec(s))) br = "Firefox " + m[1];
		else if (/Safari\//.test(s) && (m = /Version\/(\d+)/.exec(s))) br = "Safari " + m[1];
		return "系统  " + os + "\n浏览器  " + br;
	}

	function crondesc(expr){
		var s = String(expr).trim().replace(/\s+/g, " ");
		if (!s) throw new Error("请输入表达式");
		var alias = {
			"@yearly": "0 0 1 1 *",
			"@annually": "0 0 1 1 *",
			"@monthly": "0 0 1 * *",
			"@weekly": "0 0 * * 0",
			"@daily": "0 0 * * *",
			"@midnight": "0 0 * * *",
			"@hourly": "0 * * * *",
		};
		if (alias[s]) s = alias[s];
		else if (s.charAt(0) === "@") throw new Error("不支持这个 @ 简写");
		var p = s.split(" ");
		var names;
		var ranges;
		if (p.length === 5) {
			names = ["分", "时", "日", "月", "周"];
			ranges = [[0, 59], [0, 23], [1, 31], [1, 12], [0, 7]];
		}
		else if (p.length === 6) {
			names = ["秒", "分", "时", "日", "月", "周"];
			ranges = [[0, 59], [0, 59], [0, 23], [1, 31], [1, 12], [0, 7]];
		}
		else throw new Error("用 5 段：分 时 日 月 周");
		var lines = [];
		var i;
		for (i = 0; i < p.length; i++) lines.push(names[i] + "  " + cronpart(p[i], ranges[i][0], ranges[i][1]));
		return lines.join("\n");
	}

	function cronpart(part, lo, hi){
		if (part === "*") return "每档";
		var step = part.split("/");
		if (step.length > 2) throw new Error("表达式无效");
		var base = step[0];
		var every = "";
		if (step.length === 2) {
			if (!/^\d+$/.test(step[1]) || Number(step[1]) < 1) throw new Error("步长无效");
			every = "，每隔 " + step[1];
		}
		if (base === "*") return "每档" + every;
		var bits = base.split(",");
		var out = [];
		var i;
		for (i = 0; i < bits.length; i++) {
			var bit = bits[i];
			var span = bit.split("-");
			if (span.length === 1) {
				cronnum(span[0], lo, hi);
				out.push(span[0]);
			}
			else if (span.length === 2) {
				cronnum(span[0], lo, hi);
				cronnum(span[1], lo, hi);
				out.push(span[0] + " 到 " + span[1]);
			}
			else throw new Error("表达式无效");
		}
		return out.join("、") + every;
	}

	function cronnum(s, lo, hi){
		if (!/^\d+$/.test(s)) throw new Error("只支持数字");
		var n = Number(s);
		if (n < lo || n > hi) throw new Error("有数字超出范围");
	}

	function htmlmin(text){
		var s = String(text);
		if (s.length > 200000) throw new Error("文字太长");
		var out = "";
		var i = 0;
		var keep = "";
		while (i < s.length) {
			if (!keep && s.slice(i, i + 4) === "<!--") {
				var end = s.indexOf("-->", i + 4);
				if (end < 0) { out += s.slice(i); break; }
				i = end + 3;
				continue;
			}
			if (s.charAt(i) === "<") {
				var rest = s.slice(i);
				var open = /^<(pre|script|style)\b/i.exec(rest);
				var close = /^<\/(pre|script|style)\b/i.exec(rest);
				if (!keep && open) keep = open[1].toLowerCase();
				else if (keep && close && close[1].toLowerCase() === keep) keep = "";
			}
			if (!keep && isws(s.charAt(i)) && out.charAt(out.length - 1) === ">") {
				var j = i;
				while (j < s.length && isws(s.charAt(j))) j++;
				if (s.charAt(j) === "<") { i = j; continue; }
			}
			out += s.charAt(i);
			i++;
		}
		return out.trim();
	}

	function isws(c){
		return c === " " || c === "\n" || c === "\r" || c === "\t" || c === "\f";
	}

	function cssmin(text){
		var s = String(text);
		if (s.length > 200000) throw new Error("文字太长");
		var out = "";
		var i = 0;
		var q = "";
		while (i < s.length) {
			var c = s.charAt(i);
			if (q) {
				out += c;
				if (c === "\\" && i + 1 < s.length) { out += s.charAt(i + 1); i += 2; continue; }
				if (c === q) q = "";
				i++;
				continue;
			}
			if (c === '"' || c === "'") { q = c; out += c; i++; continue; }
			if (c === "/" && s.charAt(i + 1) === "*") {
				var end = s.indexOf("*/", i + 2);
				if (end < 0) throw new Error("注释没有结束");
				i = end + 2;
				continue;
			}
			out += c;
			i++;
		}
		return out.replace(/\s+/g, " ").replace(/\s*([{}:;,])\s*/g, "$1").trim();
	}

	function xmlfmt(text, mode){
		var s = String(text).trim();
		if (!s) throw new Error("请输入 XML");
		if (s.length > 200000) throw new Error("文字太长");
		if (typeof DOMParser === "function") {
			var doc = new DOMParser().parseFromString(s, "application/xml");
			if (doc.getElementsByTagName("parsererror").length) throw new Error("不是合法的 XML");
		}
		s = s.replace(/>\s+</g, "><");
		if (mode === "min") return s;
		var lines = s.replace(/(>)(<)/g, "$1\n$2").split("\n");
		var pad = 0;
		var out = [];
		var i;
		for (i = 0; i < lines.length; i++) {
			var line = lines[i];
			if (/^<\/[^>]+>/.test(line)) pad = Math.max(pad - 1, 0);
			var indent = "";
			var k;
			for (k = 0; k < pad; k++) indent += "  ";
			out.push(indent + line);
			if (/^<[^!?/][^>]*>$/.test(line) && !/\/>$/.test(line)) pad++;
		}
		return out.join("\n");
	}

	function jsmin(text){
		var s = String(text);
		if (s.length > 200000) throw new Error("文字太长");
		var out = "";
		var i = 0;
		var q = "";
		while (i < s.length) {
			var c = s.charAt(i);
			var n = s.charAt(i + 1);
			if (q) {
				out += c;
				if (c === "\\" && i + 1 < s.length) { out += n; i += 2; continue; }
				if (c === q) q = "";
				i++;
				continue;
			}
			if (c === '"' || c === "'" || c === "`") { q = c; out += c; i++; continue; }
			if (c === "/" && n === "/") {
				i += 2;
				while (i < s.length && s.charAt(i) !== "\n") i++;
				continue;
			}
			if (c === "/" && n === "*") {
				var end = s.indexOf("*/", i + 2);
				if (end < 0) throw new Error("注释没有结束");
				i = end + 2;
				continue;
			}
			out += c;
			i++;
		}
		return out.replace(/[ \t]+\n/g, "\n").replace(/\n{3,}/g, "\n\n").trim();
	}

	function sqlfmt(text, mode){
		var s = String(text).trim();
		if (!s) throw new Error("请输入 SQL");
		if (s.length > 200000) throw new Error("文字太长");
		s = s.replace(/--[^\n]*/g, " ").replace(/\s+/g, " ").trim();
		if (mode === "min") return s;
		var keys = ["order by", "group by", "inner join", "left join", "right join", "insert into", "delete from", "select", "from", "where", "having", "limit", "values", "update", "set", "and", "or"];
		var i;
		for (i = 0; i < keys.length; i++) {
			var k = keys[i];
			var re = new RegExp("\\b" + k.replace(" ", "\\s+") + "\\b", "ig");
			s = s.replace(re, "\n" + k.toUpperCase());
		}
		return s.replace(/^\n+/, "").replace(/\n{2,}/g, "\n").trim();
	}

	function tokencount(text){
		var s = String(text);
		if (s.length > 200000) throw new Error("文字太长");
		var n = 0;
		var re = /[\u3400-\u9fff]|[A-Za-z]+|\d+|[^\s]/g;
		while (re.exec(s)) n++;
		return "约 " + n + " 个 token\n字符  " + s.length + "\n这是按汉字、英文单词和符号粗算的，不是某个模型的分词器。";
	}

	function html2js(text){
		var s = String(text);
		if (s.length > 200000) throw new Error("文字太长");
		return "var html = " + JSON.stringify(s) + ";";
	}

	SK.textop = textop;
	SK.textlocal = textlocal;
	SK.htmldec = htmldec;
	SK.jsonmin = jsonmin;
	SK.md5hex = md5hex;
	SK.lehex = lehex;
	SK.nameconv = nameconv;
	SK.jsonesc = jsonesc;
	SK.uaparse = uaparse;
	SK.crondesc = crondesc;
	SK.cronpart = cronpart;
	SK.cronnum = cronnum;
	SK.htmlmin = htmlmin;
	SK.isws = isws;
	SK.cssmin = cssmin;
	SK.xmlfmt = xmlfmt;
	SK.jsmin = jsmin;
	SK.sqlfmt = sqlfmt;
	SK.tokencount = tokencount;
	SK.html2js = html2js;
})();
(function(){
	var ttsItems = [];
	function ttslabel(engine){
		if (engine === "sherpa") return "离线模型";
		if (engine === "sapi") return "SAPI";
		if (engine === "winrt") return "Windows 语音";
		if (engine === "edge") return "Edge 在线";
		return engine || "";
	}

	var ttsEdgeWait = 0;

	function loadtts(){
		if (ttsItems.length) return;
		fetchtts();
	}

	function fetchtts(){
		SK.get("/api/tts/models", function(data){
			var items = data && data.length != null ? data : [];
			var hasEdge = false;
			var i;
			for (i = 0; i < items.length; i++) {
				if ((items[i].engine || "") === "edge") { hasEdge = true; break; }
			}
			if (ttsItems.length && items.length <= ttsItems.length) return;
			ttsItems = items;
			var seen = {};
			var rows = [];
			for (i = 0; i < ttsItems.length; i++) {
				var eng = ttsItems[i].engine || "";
				if (seen[eng]) continue;
				seen[eng] = true;
				rows.push({ value: eng, label: ttslabel(eng) });
			}
			SK.fillsel(SK.$("tts-eng"), rows, SK.$("tts-eng").value);
			fillttsmodels();
			if (!hasEdge && ttsEdgeWait < 2) {
				ttsEdgeWait++;
				setTimeout(fetchtts, ttsEdgeWait === 1 ? 2000 : 4000);
			}
		}, "tts-msg");
	}

	function fillttsmodels(){
		fillttslang();
		var eng = SK.$("tts-eng").value;
		var want = ttswant();
		var prev = SK.$("tts-model").value;
		var rows = [];
		var i;
		for (i = 0; i < ttsItems.length; i++) {
			if ((ttsItems[i].engine || "") !== eng) continue;
			if (!ttsmodelok(ttsItems[i], want)) continue;
			rows.push({ value: String(i), label: ttsItems[i].name || eng });
		}
		SK.fillsel(SK.$("tts-model"), rows, prev);
		fillttsvoices();
	}

	function ttsitem(){
		var n = parseInt(SK.$("tts-model").value, 10);
		if (isNaN(n) || n < 0 || n >= ttsItems.length) return null;
		return ttsItems[n];
	}

	function fillttsvoices(){
		var item = ttsitem();
		var speakers = (item && item.speakers) || [];
		var want = ttswant();
		var prevIdx = parseInt(SK.$("tts-voice").value, 10);
		var prev = speakers[prevIdx] || null;
		var rows = [];
		var pick = "";
		var i;
		for (i = 0; i < speakers.length; i++) {
			var sp = speakers[i] || {};
			if (!ttsspeakerok(item, sp, want)) continue;
			var label = sp.label || sp.name || sp.key || String(i);
			if (!sp.label && sp.lang) label += " · " + sp.lang;
			rows.push({ value: String(i), label: label });
			if (prev && ((prev.key && prev.key === sp.key) || (!prev.key && prev.name && prev.name === sp.name)))
				pick = String(i);
		}
		SK.fillsel(SK.$("tts-voice"), rows, pick);
	}

	function ttsgo(save){
		var item = ttsitem();
		if (!item) {
			SK.msg("tts-msg", "没有可用的语音", true);
			return;
		}
		if (!SK.$("tts-voice").options.length) {
			SK.msg("tts-msg", "没有符合筛选的发音人", true);
			return;
		}
		var speakers = item.speakers || [];
		var vi = parseInt(SK.$("tts-voice").value, 10);
		var sp = speakers[vi] || {};
		var speed = parseFloat(SK.$("tts-speed").value);
		if (isNaN(speed)) speed = 1;
		SK.post("/api/tts", {
			text: SK.$("tts-in").value,
			engine: item.engine,
			model: item.name,
			voice: sp.key || sp.name || "",
			speaker_id: sp.id != null ? sp.id : vi,
			speed: speed,
		}, function(data){
			if (!data || !data.wav_base64) {
				SK.msg("tts-msg", "没有音频", true);
				return;
			}
			if (save) {
				ttssavewav(data.wav_base64, sp, item);
				return;
			}
			var audio = SK.$("tts-audio");
			audio.src = "data:audio/wav;base64," + data.wav_base64;
			audio.hidden = false;
			audio.play();
		}, "tts-msg");
	}

	function ttssavewav(b64, sp, item){
		var bin = atob(b64);
		var bytes = new Uint8Array(bin.length);
		var i;
		for (i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i) & 255;
		var blob = new Blob([bytes], { type: "audio/wav" });
		var a = document.createElement("a");
		var url = URL.createObjectURL(blob);
		var name = (sp && (sp.label || sp.name || sp.key)) || (item && item.name) || "tts";
		name = String(name).replace(/[\\/:*?"<>|]+/g, " ").replace(/^\s+|\s+$/g, "");
		if (!name) name = "tts";
		if (name.length > 40) name = name.substring(0, 40).replace(/\s+$/g, "");
		a.href = url;
		a.download = name + ".wav";
		document.body.appendChild(a);
		a.click();
		document.body.removeChild(a);
		setTimeout(function(){ URL.revokeObjectURL(url); }, 4000);
		SK.msg("tts-msg", "已下载 " + a.download);
	}

	function ttswant(){
		return {
			lang: (SK.$("tts-lang") && SK.$("tts-lang").value) || "",
			gender: (SK.$("tts-gender") && SK.$("tts-gender").value) || "",
		};
	}

	function fillttslang(){
		var eng = SK.$("tts-eng").value;
		var prev = SK.$("tts-lang").value;
		var seen = {};
		var named = [
			["zh", "中文 (zh)"], ["en", "英文 (en)"], ["ja", "日文 (ja)"], ["ko", "韩文 (ko)"],
			["vi", "越南语 (vi)"], ["yue", "粤语 (yue)"], ["fr", "法语 (fr)"], ["de", "德语 (de)"], ["es", "西班牙语 (es)"],
		];
		var i, j;
		for (i = 0; i < ttsItems.length; i++) {
			if ((ttsItems[i].engine || "") !== eng) continue;
			ttslangadd(seen, ttsItems[i].lang);
			var sps = ttsItems[i].speakers || [];
			for (j = 0; j < sps.length; j++) ttslangadd(seen, sps[j].lang);
		}
		var rows = [{ value: "", label: "全部语言" }];
		var used = {};
		for (i = 0; i < named.length; i++) {
			if (!seen[named[i][0]]) continue;
			rows.push({ value: named[i][0], label: named[i][1] });
			used[named[i][0]] = 1;
		}
		var rest = [];
		for (var k in seen) if (!used[k]) rest.push(k);
		rest.sort();
		for (i = 0; i < rest.length; i++) rows.push({ value: rest[i], label: rest[i] });
		SK.fillsel(SK.$("tts-lang"), rows, seen[ttsnormlang(prev)] ? ttsnormlang(prev) : "");
	}

	function ttslangadd(seen, raw){
		var parts = String(raw || "").split(/[,/|+]/);
		var i;
		for (i = 0; i < parts.length; i++) {
			var n = ttsnormlang(parts[i]);
			if (n) seen[n] = 1;
		}
	}

	function ttsnormlang(s){
		s = String(s || "").replace(/^\s+|\s+$/g, "").toLowerCase().replace(/_/g, "-");
		if (!s) return "";
		var dash = s.indexOf("-");
		var p = dash > 0 ? s.substring(0, dash) : s;
		if (p === "zh" || p === "cmn" || p === "chinese" || p === "cn") return "zh";
		if (p === "en" || p === "english") return "en";
		if (p === "vi" || p === "vie" || p === "vietnamese") return "vi";
		if (p === "ja" || p === "jpn" || p === "japanese") return "ja";
		if (p === "ko" || p === "kor" || p === "korean") return "ko";
		if (p === "yue" || p === "cantonese") return "yue";
		return p.length <= 3 ? p : s;
	}

	function ttslangmatch(have, want){
		want = ttsnormlang(want);
		if (!want) return true;
		if (!ttsnormlang(have)) return false;
		var parts = String(have || "").split(/[,/|+]/);
		var i;
		for (i = 0; i < parts.length; i++)
			if (ttsnormlang(parts[i]) === want) return true;
		return false;
	}

	function ttsnormgender(s){
		s = String(s || "").replace(/^\s+|\s+$/g, "").toLowerCase();
		if (s === "m" || s === "male" || s === "man" || s === "男" || s === "男声") return "male";
		if (s === "f" || s === "female" || s === "woman" || s === "女" || s === "女声") return "female";
		return s;
	}

	function ttsgendermatch(have, want){
		want = ttsnormgender(want);
		if (!want) return true;
		have = ttsnormgender(have);
		return !!have && have === want;
	}

	function ttsmodelok(item, want){
		if (!item) return false;
		var speakers = item.speakers || [];
		var i;
		var langOk = !want.lang || ttslangmatch(item.lang, want.lang);
		if (!langOk) {
			for (i = 0; i < speakers.length; i++) {
				if (ttslangmatch((speakers[i] || {}).lang, want.lang)) { langOk = true; break; }
			}
		}
		if (!langOk) return false;
		if (!want.gender) return true;
		if (ttsgendermatch(item.gender, want.gender)) return true;
		var any = false;
		var allEmpty = !ttsnormgender(item.gender);
		for (i = 0; i < speakers.length; i++) {
			var g = ttsnormgender((speakers[i] || {}).gender);
			if (ttsgendermatch(g, want.gender)) any = true;
			if (g) allEmpty = false;
		}
		return any || allEmpty;
	}

	function ttsspeakerok(item, sp, want){
		if (!sp) return false;
		if (want.lang && !ttslangmatch(sp.lang, want.lang) && !ttslangmatch(item && item.lang, want.lang))
			return false;
		if (want.gender) {
			var sg = ttsnormgender(sp.gender);
			var mg = ttsnormgender(item && item.gender);
			if (sg && !ttsgendermatch(sg, want.gender)) return false;
			if (!sg && mg && !ttsgendermatch(mg, want.gender)) return false;
		}
		return true;
	}

	SK.ttslabel = ttslabel;
	SK.loadtts = loadtts;
	SK.fetchtts = fetchtts;
	SK.fillttsmodels = fillttsmodels;
	SK.ttsitem = ttsitem;
	SK.fillttsvoices = fillttsvoices;
	SK.ttsgo = ttsgo;
	SK.ttssavewav = ttssavewav;
	SK.ttswant = ttswant;
	SK.fillttslang = fillttslang;
	SK.ttslangadd = ttslangadd;
	SK.ttsnormlang = ttsnormlang;
	SK.ttslangmatch = ttslangmatch;
	SK.ttsnormgender = ttsnormgender;
	SK.ttsgendermatch = ttsgendermatch;
	SK.ttsmodelok = ttsmodelok;
	SK.ttsspeakerok = ttsspeakerok;
})();
(function(){
	var uniSlices = null;
	var uniCan = null;
	var uniNot = null;
	var uniInk = {};
	var uniSeen = {};
	SK.regutil("u-ascii", {
		title: "Unicode 表",
		go: "显示",
		body: function(){
			return '<div class="uquery"><input id="u-q" type="text" placeholder="字、U+4E00 或十进制"><span id="u-msg" class="msg"></span></div>'
				+ '<p class="hint">点一下选中并复制。最右边是大字和这个字符的编码。</p>'
				+ '<div class="unitab"><div class="ugroups" id="u-groups"></div><div class="ugrid" id="u-grid"></div>'
				+ '<aside class="uprev"><div class="uprev-ch" id="u-ch"></div><dl class="uprev-meta" id="u-meta"></dl></aside></div>';
		},
		run: function(){
			throw new Error("没有这个工具");
		},
		open: function(){
			unibuild();
		},
		hideGo: true,
		hideOut: true
	});
	function unibuild(){
		var slices = unislices();
		var html = [];
		var i;
		for (i = 0; i < slices.length; i++)
			html.push('<button type="button" data-i="' + i + '" title="' + slices[i][0] + '">' + slices[i][0] + '</button>');
		SK.$("u-groups").innerHTML = html.join("");
		SK.$("u-groups").onclick = function(ev){
			var el = ev.target;
			while (el && el.id !== "u-groups") {
				if (el.getAttribute && el.getAttribute("data-i") != null) {
					unishow(parseInt(el.getAttribute("data-i"), 10), -1);
					return;
				}
				el = el.parentNode;
			}
		};
		SK.$("u-grid").onclick = function(ev){
			var el = ev.target;
			while (el && el.id !== "u-grid") {
				if (el.getAttribute && el.getAttribute("data-cp") != null) {
					var cp = parseInt(el.getAttribute("data-cp"), 10);
					unimark(cp);
					unipreview(cp);
					SK.copytext(String.fromCodePoint(cp), "u-msg");
					return;
				}
				el = el.parentNode;
			}
		};
		SK.$("u-q").oninput = uniquery;
		unishow(0, -1);
	}

	function uniquery(){
		var cp = uniparse(SK.$("u-q").value);
		if (cp === -1) {
			SK.msg("u-msg", "");
			return;
		}
		if (cp < 0) {
			SK.msg("u-msg", "没有这个码位", true);
			return;
		}
		var i = unifind(cp);
		if (i < 0) {
			SK.msg("u-msg", "没有这个码位", true);
			return;
		}
		SK.msg("u-msg", "U+" + unihex(cp));
		unishow(i, cp);
	}

	function unishow(index, focus){
		var slices = unislices();
		var g = slices[index];
		var groups = document.querySelectorAll("#u-groups button");
		var i;
		for (i = 0; i < groups.length; i++)
			groups[i].className = i === index ? "on" : "";
		if (groups[index]) groups[index].scrollIntoView({ block: "nearest" });
		var pick = focus >= g[1] && focus <= g[2] ? focus : g[1];
		var html = [];
		var cp;
		for (cp = g[1]; cp <= g[2]; cp++) {
			var glyph = uniglyph(cp);
			var cls = glyph.ctrl ? "ctrl" : (glyph.empty ? "gap" : (glyph.alt ? "alt" : ""));
			html.push('<button type="button" data-cp="' + cp + '" title="U+' + unihex(cp) + '"'
				+ (cls ? ' class="' + cls + '"' : '') + (cp === pick ? ' id="u-on"' : '') + '>'
				+ glyph.t + '</button>');
		}
		SK.$("u-grid").innerHTML = html.join("");
		var on = SK.$("u-on");
		if (on) {
			on.className = (on.className ? on.className + " " : "") + "on";
			on.removeAttribute("id");
			if (focus >= g[1] && focus <= g[2]) on.scrollIntoView({ block: "center" });
		}
		unipreview(pick);
	}

	function unimark(cp){
		var buttons = document.querySelectorAll("#u-grid button");
		var i, cls;
		for (i = 0; i < buttons.length; i++) {
			cls = buttons[i].className.replace(/\bon\b/g, "").replace(/^\s+|\s+$/g, "").replace(/\s+/g, " ");
			if (parseInt(buttons[i].getAttribute("data-cp"), 10) === cp)
				cls = cls ? cls + " on" : "on";
			buttons[i].className = cls;
		}
	}

	function unipreview(cp){
		var glyph = uniglyph(cp);
		var box = SK.$("u-ch");
		if (glyph.empty) {
			box.textContent = "";
			box.className = "uprev-ch gap";
		}
		else if (glyph.ctrl) {
			box.textContent = glyph.t;
			box.className = "uprev-ch ctrl";
		}
		else {
			var show = glyph.alt || cp;
			var ch = String.fromCodePoint(show);
			if (unicomb(show)) ch = "\u25CC" + ch;
			box.textContent = ch;
			box.className = "uprev-ch";
		}
		var rows = [
			["码位", "U+" + unihex(cp)],
			["十进制", String(cp)],
			["分组", uniblock(cp)],
			["平面", uniplane(cp)],
			["类型", unikind(cp)],
			["UTF-8", uniutf8(cp)],
			["UTF-16", uniutf16(cp)],
			["HTML", "&#" + cp + ";  &#x" + unihex(cp) + ";"],
			["JavaScript", unijs(cp)],
			["URL", uniurl(cp)]
		];
		if (glyph.empty) {
			if (unicn(cp))
				rows.push(["说明", "这个码位没有分配字符，没有字体可以显示。"]);
			else
				rows.push(["说明", "本机字体没有这个字。花园明朝（HanaMin）或 BabelStone Han 里可能有。"]);
		}
		if (glyph.alt)
			rows.push(
				["等价", "U+" + unihex(glyph.alt) + " " + String.fromCodePoint(glyph.alt)],
				["字形", "本机字体没有单独字形，格内是等价汉字。复制的仍是原码位。花园明朝（HanaMin）或 BabelStone Han 有独立字形。"]
			);
		var html = [];
		var i;
		for (i = 0; i < rows.length; i++)
			html.push("<dt>" + rows[i][0] + "</dt><dd>" + unihtml(rows[i][1]) + "</dd>");
		SK.$("u-meta").innerHTML = html.join("");
	}

	function unicn(cp){
		return /^\p{Cn}$/u.test(String.fromCodePoint(cp));
	}

	function unihtml(s){
		return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;");
	}

	function uniblock(cp){
		var i = unifind(cp);
		if (i < 0) return "未收录";
		return unislices()[i][0];
	}

	function uniplane(cp){
		if (cp <= 0xFFFF) return "基本多文种平面";
		if (cp <= 0x1FFFF) return "第一辅助平面";
		if (cp <= 0x2FFFF) return "第二辅助平面";
		return "其他平面";
	}

	function unikind(cp){
		if (cp < 32 || cp === 0x7F || (cp >= 0x80 && cp <= 0x9F)) return "控制字符";
		if ((cp & 0xFFFF) === 0xFFFE || (cp & 0xFFFF) === 0xFFFF) return "非字符";
		if (unicomb(cp)) return "组合字符";
		if (cp === 0x20 || cp === 0xA0 || cp === 0xAD
			|| (cp >= 0x2000 && cp <= 0x200A) || cp === 0x2028 || cp === 0x2029
			|| cp === 0x202F || cp === 0x205F || cp === 0x3000 || cp === 0xFEFF) return "空白";
		return "图形字符";
	}

	function uniutf8(cp){
		var bytes;
		if (cp < 0x80) bytes = [cp];
		else if (cp < 0x800) bytes = [0xC0 | (cp >> 6), 0x80 | (cp & 0x3F)];
		else if (cp < 0x10000) bytes = [0xE0 | (cp >> 12), 0x80 | ((cp >> 6) & 0x3F), 0x80 | (cp & 0x3F)];
		else bytes = [0xF0 | (cp >> 18), 0x80 | ((cp >> 12) & 0x3F), 0x80 | ((cp >> 6) & 0x3F), 0x80 | (cp & 0x3F)];
		var i, out = [];
		for (i = 0; i < bytes.length; i++) {
			var h = bytes[i].toString(16).toUpperCase();
			if (h.length < 2) h = "0" + h;
			out.push(h);
		}
		return out.join(" ");
	}

	function uniutf16(cp){
		if (cp <= 0xFFFF) return unihex(cp);
		var u = cp - 0x10000;
		return unihex(0xD800 + (u >> 10)) + " " + unihex(0xDC00 + (u & 0x3FF));
	}

	function unijs(cp){
		var hex = unihex(cp).toLowerCase();
		if (cp <= 0xFFFF) return "\\u" + hex;
		var pair = uniutf16(cp).toLowerCase().split(" ");
		return "\\u{" + hex + "}   \\u" + pair[0] + "\\u" + pair[1];
	}

	function uniurl(cp){
		var bytes = uniutf8(cp).split(" ");
		var i, out = "";
		for (i = 0; i < bytes.length; i++) out += "%" + bytes[i];
		return out;
	}

	function uniparse(raw){
		raw = String(raw).replace(/^\s+|\s+$/g, "");
		if (!raw) return -1;
		var m = /^(?:[uU]\+|0x)([0-9a-fA-F]{1,6})$/.exec(raw);
		if (m) return parseInt(m[1], 16);
		if (/^[0-9]{1,7}$/.test(raw)) {
			var n = parseInt(raw, 10);
			if (n <= 0x10FFFF) return n;
			return -2;
		}
		if (/^[0-9a-fA-F]{2,6}$/.test(raw) && /[a-fA-F]/.test(raw)) return parseInt(raw, 16);
		if (raw.length > 8) return -1;
		var cp = raw.codePointAt(0);
		if (raw.length === (cp > 0xFFFF ? 2 : 1)) return cp;
		return -1;
	}

	function unifind(cp){
		var slices = unislices();
		var i;
		for (i = 0; i < slices.length; i++) {
			if (cp >= slices[i][1] && cp <= slices[i][2]) return i;
		}
		return -1;
	}

	function unihex(cp){
		var s = cp.toString(16).toUpperCase();
		while (s.length < 4) s = "0" + s;
		return s;
	}

	function uniglyph(cp){
		var names = ["NUL", "SOH", "STX", "ETX", "EOT", "ENQ", "ACK", "BEL", "BS", "HT", "LF", "VT", "FF", "CR", "SO", "SI", "DLE", "DC1", "DC2", "DC3", "DC4", "NAK", "SYN", "ETB", "CAN", "EM", "SUB", "ESC", "FS", "GS", "RS", "US"];
		if (cp < 32) return { t: names[cp], ctrl: true };
		if (cp === 0x7F) return { t: "DEL", ctrl: true };
		if (cp >= 0x80 && cp <= 0x9F) return { t: unihex(cp), ctrl: true };
		if ((cp & 0xFFFF) === 0xFFFE || (cp & 0xFFFF) === 0xFFFF) return { t: unihex(cp), ctrl: true };
		if (cp === 0xAD || (cp >= 0x200B && cp <= 0x200F) || cp === 0x2028 || cp === 0x2029 || cp === 0x2060 || cp === 0xFEFF)
			return { t: unihex(cp), ctrl: true, empty: false, alt: 0 };
		var gap = unigap(cp);
		if (gap.empty) return { t: "", ctrl: false, empty: true, alt: 0 };
		var show = gap.alt || cp;
		var ch = String.fromCodePoint(show);
		if (unicomb(show)) ch = "\u25CC" + ch;
		if (ch === "&") ch = "&amp;";
		else if (ch === "<") ch = "&lt;";
		else if (ch === ">") ch = "&gt;";
		return { t: ch, ctrl: false, empty: false, alt: gap.alt };
	}

	function unigap(cp){
		if (uniSeen[cp]) return uniSeen[cp];
		var ch = String.fromCodePoint(cp);
		var nfkc = ch.normalize ? ch.normalize("NFKC") : ch;
		var eq = nfkc.codePointAt(0);
		var one = nfkc.length === (eq > 0xFFFF ? 2 : 1);
		var alt = 0;
		var miss = unimiss(cp);
		if (miss && one && eq !== cp && !unimiss(eq)) alt = eq;
		var rec = { empty: miss && !alt, alt: alt };
		uniSeen[cp] = rec;
		return rec;
	}

	function unimiss(cp){
		if (uniInk[cp] != null) return uniInk[cp];
		if (uniNot == null) {
			var a = uniink(0xFDD0);
			var b = uniink(0xFDD1);
			uniNot = a === b ? a : -1;
		}
		var miss = uniNot >= 0 && uniink(cp) === uniNot;
		uniInk[cp] = miss;
		return miss;
	}

	function uniink(cp){
		var c = uniCan;
		if (!c) {
			c = document.createElement("canvas");
			c.width = 24;
			c.height = 24;
			uniCan = c;
		}
		var g = c.getContext("2d", { willReadFrequently: true });
		g.clearRect(0, 0, 24, 24);
		g.font = "16px \"Segoe UI Emoji\",\"Segoe UI Symbol\",\"Microsoft YaHei\",\"Meiryo\",\"Yu Gothic\",\"Microsoft JhengHei\",\"MingLiU\",\"Noto Sans SC\",\"Noto Sans JP\",\"Noto Sans KR\",sans-serif";
		g.fillStyle = "#000";
		g.textAlign = "center";
		g.textBaseline = "middle";
		g.fillText(String.fromCodePoint(cp), 12, 12);
		var d = g.getImageData(0, 0, 24, 24).data;
		var n = 0;
		var i;
		for (i = 3; i < d.length; i += 4) n += d[i];
		return n;
	}

	function unicomb(cp){
		return (cp >= 0x0300 && cp <= 0x036F)
			|| (cp >= 0x0483 && cp <= 0x0489)
			|| (cp >= 0x0591 && cp <= 0x05BD)
			|| cp === 0x05BF || (cp >= 0x05C1 && cp <= 0x05C2) || cp === 0x05C4 || cp === 0x05C5 || cp === 0x05C7
			|| (cp >= 0x0610 && cp <= 0x061A) || (cp >= 0x064B && cp <= 0x065F) || cp === 0x0670
			|| (cp >= 0x06D6 && cp <= 0x06ED)
			|| (cp >= 0x1AB0 && cp <= 0x1AFF)
			|| (cp >= 0x1DC0 && cp <= 0x1DFF)
			|| (cp >= 0x20D0 && cp <= 0x20FF)
			|| (cp >= 0xFE20 && cp <= 0xFE2F);
	}

	function unislices(){
		if (uniSlices) return uniSlices;
		var src = [
			["基本拉丁", 0x0000, 0x007F],
			["拉丁补充", 0x0080, 0x00FF],
			["拉丁扩展 A", 0x0100, 0x017F],
			["拉丁扩展 B", 0x0180, 0x024F],
			["国际音标", 0x0250, 0x02AF],
			["修饰字母", 0x02B0, 0x02FF],
			["组合附加", 0x0300, 0x036F],
			["希腊文", 0x0370, 0x03FF],
			["西里尔文", 0x0400, 0x04FF],
			["西里尔补充", 0x0500, 0x052F],
			["亚美尼亚文", 0x0530, 0x058F],
			["希伯来文", 0x0590, 0x05FF],
			["阿拉伯文", 0x0600, 0x06FF],
			["叙利亚文", 0x0700, 0x074F],
			["阿拉伯补充", 0x0750, 0x077F],
			["它拿文", 0x0780, 0x07BF],
			["西非书面文", 0x07C0, 0x07FF],
			["撒马利亚文", 0x0800, 0x083F],
			["曼达文", 0x0840, 0x085F],
			["叙利亚补充", 0x0860, 0x086F],
			["阿拉伯扩展 B", 0x0870, 0x089F],
			["阿拉伯扩展 A", 0x08A0, 0x08FF],
			["天城文", 0x0900, 0x097F],
			["孟加拉文", 0x0980, 0x09FF],
			["果鲁穆奇文", 0x0A00, 0x0A7F],
			["古吉拉特文", 0x0A80, 0x0AFF],
			["奥里亚文", 0x0B00, 0x0B7F],
			["泰米尔文", 0x0B80, 0x0BFF],
			["泰卢固文", 0x0C00, 0x0C7F],
			["卡纳达文", 0x0C80, 0x0CFF],
			["马拉雅拉姆文", 0x0D00, 0x0D7F],
			["僧伽罗文", 0x0D80, 0x0DFF],
			["泰文", 0x0E00, 0x0E7F],
			["老挝文", 0x0E80, 0x0EFF],
			["藏文", 0x0F00, 0x0FFF],
			["缅甸文", 0x1000, 0x109F],
			["格鲁吉亚文", 0x10A0, 0x10FF],
			["谚文字母", 0x1100, 0x11FF],
			["埃塞俄比亚文", 0x1200, 0x137F],
			["埃塞俄比亚补充", 0x1380, 0x139F],
			["切罗基文", 0x13A0, 0x13FF],
			["加拿大音节", 0x1400, 0x167F],
			["欧甘文", 0x1680, 0x169F],
			["卢恩文", 0x16A0, 0x16FF],
			["他加禄文", 0x1700, 0x171F],
			["哈努诺文", 0x1720, 0x173F],
			["布希德文", 0x1740, 0x175F],
			["塔格班瓦文", 0x1760, 0x177F],
			["高棉文", 0x1780, 0x17FF],
			["蒙古文", 0x1800, 0x18AF],
			["加拿大音节扩展", 0x18B0, 0x18FF],
			["林布文", 0x1900, 0x194F],
			["德宏傣文", 0x1950, 0x197F],
			["新傣仂文", 0x1980, 0x19DF],
			["高棉符号", 0x19E0, 0x19FF],
			["布吉文", 0x1A00, 0x1A1F],
			["老傣文", 0x1A20, 0x1AAF],
			["组合附加扩展", 0x1AB0, 0x1AFF],
			["巴厘文", 0x1B00, 0x1B7F],
			["巽他文", 0x1B80, 0x1BBF],
			["巴塔克文", 0x1BC0, 0x1BFF],
			["雷布查文", 0x1C00, 0x1C4F],
			["桑塔利文", 0x1C50, 0x1C7F],
			["西里尔扩展 C", 0x1C80, 0x1C8F],
			["格鲁吉亚扩展", 0x1C90, 0x1CBF],
			["巽他补充", 0x1CC0, 0x1CCF],
			["吠陀扩展", 0x1CD0, 0x1CFF],
			["音标扩展", 0x1D00, 0x1D7F],
			["音标扩展补充", 0x1D80, 0x1DBF],
			["组合附加补充", 0x1DC0, 0x1DFF],
			["拉丁扩展附加", 0x1E00, 0x1EFF],
			["希腊文扩展", 0x1F00, 0x1FFF],
			["常用标点", 0x2000, 0x206F],
			["上下标", 0x2070, 0x209F],
			["货币符号", 0x20A0, 0x20CF],
			["符号用附加", 0x20D0, 0x20FF],
			["字母式符号", 0x2100, 0x214F],
			["数字形式", 0x2150, 0x218F],
			["箭头", 0x2190, 0x21FF],
			["数学运算符", 0x2200, 0x22FF],
			["杂项技术符号", 0x2300, 0x23FF],
			["控制图片", 0x2400, 0x243F],
			["光学识别", 0x2440, 0x245F],
			["带圈字母数字", 0x2460, 0x24FF],
			["制表符", 0x2500, 0x257F],
			["方块元素", 0x2580, 0x259F],
			["几何形状", 0x25A0, 0x25FF],
			["杂项符号", 0x2600, 0x26FF],
			["装饰符号", 0x2700, 0x27BF],
			["数学符号 A", 0x27C0, 0x27EF],
			["箭头补充 A", 0x27F0, 0x27FF],
			["盲文", 0x2800, 0x28FF],
			["箭头补充 B", 0x2900, 0x297F],
			["数学符号 B", 0x2980, 0x29FF],
			["数学运算符补充", 0x2A00, 0x2AFF],
			["符号和箭头", 0x2B00, 0x2BFF],
			["格拉哥里文", 0x2C00, 0x2C5F],
			["拉丁扩展 C", 0x2C60, 0x2C7F],
			["科普特文", 0x2C80, 0x2CFF],
			["格鲁吉亚补充", 0x2D00, 0x2D2F],
			["提非纳文", 0x2D30, 0x2D7F],
			["埃塞俄比亚扩展", 0x2D80, 0x2DDF],
			["西里尔扩展 A", 0x2DE0, 0x2DFF],
			["标点补充", 0x2E00, 0x2E7F],
			["汉字部首补充", 0x2E80, 0x2EFF],
			["康熙部首", 0x2F00, 0x2FDF],
			["表意描述符", 0x2FF0, 0x2FFF],
			["中日韩符号", 0x3000, 0x303F],
			["平假名", 0x3040, 0x309F],
			["片假名", 0x30A0, 0x30FF],
			["注音", 0x3100, 0x312F],
			["谚文兼容字母", 0x3130, 0x318F],
			["汉文", 0x3190, 0x319F],
			["注音扩展", 0x31A0, 0x31BF],
			["汉字笔画", 0x31C0, 0x31EF],
			["片假名扩展", 0x31F0, 0x31FF],
			["带圈中日韩", 0x3200, 0x32FF],
			["中日韩兼容", 0x3300, 0x33FF],
			["汉字扩展 A", 0x3400, 0x4DBF],
			["易经卦象", 0x4DC0, 0x4DFF],
			["汉字", 0x4E00, 0x9FFF],
			["彝文音节", 0xA000, 0xA48F],
			["彝文部首", 0xA490, 0xA4CF],
			["傈僳文", 0xA4D0, 0xA4FF],
			["瓦伊文", 0xA500, 0xA63F],
			["西里尔扩展 B", 0xA640, 0xA69F],
			["巴姆穆文", 0xA6A0, 0xA6FF],
			["声调修饰", 0xA700, 0xA71F],
			["拉丁扩展 D", 0xA720, 0xA7FF],
			["锡尔赫特文", 0xA800, 0xA82F],
			["印度数字", 0xA830, 0xA83F],
			["八思巴文", 0xA840, 0xA87F],
			["索拉什特拉文", 0xA880, 0xA8DF],
			["天城文扩展", 0xA8E0, 0xA8FF],
			["克耶文", 0xA900, 0xA92F],
			["勒姜文", 0xA930, 0xA95F],
			["谚文字母扩展 A", 0xA960, 0xA97F],
			["爪哇文", 0xA980, 0xA9DF],
			["缅甸扩展 B", 0xA9E0, 0xA9FF],
			["占文", 0xAA00, 0xAA5F],
			["缅甸扩展 A", 0xAA60, 0xAA7F],
			["越南傣文", 0xAA80, 0xAADF],
			["曼尼普尔扩展", 0xAAE0, 0xAAFF],
			["埃塞俄比亚扩展 A", 0xAB00, 0xAB2F],
			["拉丁扩展 E", 0xAB30, 0xAB6F],
			["切罗基补充", 0xAB70, 0xABBF],
			["曼尼普尔文", 0xABC0, 0xABFF],
			["谚文音节", 0xAC00, 0xD7A3],
			["谚文字母扩展 B", 0xD7B0, 0xD7FF],
			["兼容汉字", 0xF900, 0xFAFF],
			["字母表现形式", 0xFB00, 0xFB4F],
			["阿拉伯表现 A", 0xFB50, 0xFDFF],
			["变体选择符", 0xFE00, 0xFE0F],
			["竖排标点", 0xFE10, 0xFE1F],
			["组合半角符", 0xFE20, 0xFE2F],
			["中日韩兼容形式", 0xFE30, 0xFE4F],
			["小写变体", 0xFE50, 0xFE6F],
			["阿拉伯表现 B", 0xFE70, 0xFEFF],
			["半角和全角", 0xFF00, 0xFFEF],
			["特殊", 0xFFF0, 0xFFFF],
			["麻将", 0x1F000, 0x1F02F],
			["扑克", 0x1F0A0, 0x1F0FF],
			["带圈字母补充", 0x1F100, 0x1F1FF],
			["带圈汉字补充", 0x1F200, 0x1F2FF],
			["杂项象形", 0x1F300, 0x1F5FF],
			["表情", 0x1F600, 0x1F64F],
			["装饰符号补充", 0x1F650, 0x1F67F],
			["交通和地图", 0x1F680, 0x1F6FF],
			["炼金符号", 0x1F700, 0x1F77F],
			["几何形状扩展", 0x1F780, 0x1F7FF],
			["箭头补充 C", 0x1F800, 0x1F8FF],
			["补充象形", 0x1F900, 0x1F9FF],
			["象棋符号", 0x1FA00, 0x1FA6F],
			["符号扩展 A", 0x1FA70, 0x1FAFF]
		];
		var out = [];
		var i, a, b, p, n, name;
		for (i = 0; i < src.length; i++) {
			name = src[i][0];
			a = src[i][1];
			b = src[i][2];
			if (b - a < 1024) {
				out.push([name, a, b]);
				continue;
			}
			for (p = a; p <= b; p += 1024) {
				n = p + 1023;
				if (n > b) n = b;
				out.push([name + " " + unihex(p), p, n]);
			}
		}
		uniSlices = out;
		return out;
	}

	SK.unibuild = unibuild;
	SK.uniquery = uniquery;
	SK.unishow = unishow;
	SK.unimark = unimark;
	SK.unipreview = unipreview;
	SK.unicn = unicn;
	SK.unihtml = unihtml;
	SK.uniblock = uniblock;
	SK.uniplane = uniplane;
	SK.unikind = unikind;
	SK.uniutf8 = uniutf8;
	SK.uniutf16 = uniutf16;
	SK.unijs = unijs;
	SK.uniurl = uniurl;
	SK.uniparse = uniparse;
	SK.unifind = unifind;
	SK.unihex = unihex;
	SK.uniglyph = uniglyph;
	SK.unigap = unigap;
	SK.unimiss = unimiss;
	SK.uniink = uniink;
	SK.unicomb = unicomb;
	SK.unislices = unislices;
})();
(function(){
	SK.regutil("u-b64img", {
		title: "图片 Base64",
		go: "转换",
		body: function(){
			return SK.lab("图片", '<input id="box-file" type="file" accept="image/*">');
		},
		run: function(){
			imgb64();
		},
		own: true
	});
	function imgb64(){
		var input = SK.$("box-file");
		var file = input && input.files && input.files[0];
		if (!file) {
			SK.msg("box-msg", "请选择图片", true);
			return;
		}
		if (file.size > 8 * 1024 * 1024) {
			SK.msg("box-msg", "图片要小于 8 MB", true);
			return;
		}
		var reader = new FileReader();
		reader.onload = function(){
			SK.$("box-out").value = String(reader.result || "");
			SK.msg("box-msg", "完成");
		};
		reader.onerror = function(){ SK.msg("box-msg", "读取失败", true); };
		SK.msg("box-msg", "读取中");
		reader.readAsDataURL(file);
	}

	SK.imgb64 = imgb64;
})();
(function(){
	SK.regutil("u-bmi", {
		title: "BMI",
		go: "计算",
		body: function(){
			return SK.row2(SK.lab("身高（厘米）", SK.textin("box-h", "")), SK.lab("体重（千克）", SK.textin("box-w", "")));
		},
		run: function(){
			return bmicalc(SK.val("box-h"), SK.val("box-w"));
		}
	});
	function bmicalc(cm, kg){
		var h = Number(String(cm).trim());
		var w = Number(String(kg).trim());
		if (!(h > 0) || !(w > 0)) throw new Error("请输入身高和体重");
		if (h > 300 || w > 500) throw new Error("数值超出常见范围");
		var bmi = w / ((h / 100) * (h / 100));
		var band = "肥胖";
		if (bmi < 18.5) band = "偏瘦";
		else if (bmi < 24) band = "正常";
		else if (bmi < 28) band = "超重";
		return "BMI " + SK.trimnum(bmi) + "\n" + band;
	}

	SK.bmicalc = bmicalc;
})();
(function(){
	SK.regutil("u-btn", {
		title: "CSS 按钮",
		go: "生成",
		body: function(){
			return SK.lab("文字", SK.textin("box-in", "按钮"))
				+ SK.row2(SK.lab("底色", SK.textin("box-bg", "#f97316")), SK.lab("字色", SK.textin("box-fg", "#ffffff")))
				+ SK.lab("圆角", SK.numin("box-n", "8"));
		},
		run: function(){
			return cssbtn(SK.val("box-in"), SK.val("box-bg"), SK.val("box-fg"), SK.val("box-n"));
		}
	});
	function cssbtn(label, bg, fg, radius){
		var back = SK.colorrgb("hex", bg);
		var ink = SK.colorrgb("hex", fg);
		var r = parseInt(radius, 10);
		if (!(r >= 0 && r <= 999)) throw new Error("圆角要在 0 到 999");
		var text = String(label || "按钮");
		return ".btn {\n  background: #" + SK.hex2(back[0]) + SK.hex2(back[1]) + SK.hex2(back[2])
			+ ";\n  color: #" + SK.hex2(ink[0]) + SK.hex2(ink[1]) + SK.hex2(ink[2])
			+ ";\n  border: 0;\n  border-radius: " + r + "px;\n  padding: 8px 14px;\n}\n/* " + text + " */";
	}

	SK.cssbtn = cssbtn;
})();
(function(){
	SK.regutil("u-calc", {
		title: "计算器",
		go: "计算",
		body: function(){
			return SK.lab("算式", SK.ta("box-in", 4, "支持 + - * / % ^ 和括号，% 是取余"));
		},
		run: function(){
			return calcexpr(SK.val("box-in"));
		}
	});
	function calcexpr(src){
		var s = String(src == null ? "" : src).replace(/\s+/g, "");
		if (!s) throw new Error("请输入算式");
		if (s.length > 200) throw new Error("算式太长");
		var i = 0;
		var v = expr();
		if (i !== s.length) throw new Error("算式里有无法识别的部分");
		return SK.trimnum(v);
		function expr(){
			var left = term();
			while (peek() === "+" || peek() === "-") {
				var op = peek();
				i++;
				var right = term();
				left = op === "+" ? left + right : left - right;
			}
			return left;
		}
		function term(){
			var left = pow();
			while (peek() === "*" || peek() === "/" || peek() === "%") {
				var op = peek();
				i++;
				var right = pow();
				if (op === "*") left = left * right;
				else if (right === 0) throw new Error("除数不能为 0");
				else left = op === "/" ? left / right : left % right;
			}
			return left;
		}
		function pow(){
			var left = unary();
			if (peek() === "^") {
				i++;
				var right = pow();
				var v = Math.pow(left, right);
				if (!isFinite(v)) throw new Error("乘方结果无效");
				return v;
			}
			return left;
		}
		function unary(){
			if (peek() === "-") {
				i++;
				return -unary();
			}
			if (peek() === "+") {
				i++;
				return unary();
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
			return number();
		}
		function number(){
			var start = i;
			while (i < s.length && s.charAt(i) >= "0" && s.charAt(i) <= "9") i++;
			if (peek() === "." && i + 1 < s.length && s.charAt(i + 1) >= "0" && s.charAt(i + 1) <= "9") {
				i++;
				while (i < s.length && s.charAt(i) >= "0" && s.charAt(i) <= "9") i++;
			}
			if (start === i) throw new Error("这里需要数字");
			return Number(s.slice(start, i));
		}
		function peek(){
			return s.charAt(i);
		}
	}

	SK.calcexpr = calcexpr;
})();
(function(){
	SK.regutil("u-color", {
		title: "颜色转换",
		go: "转换",
		body: function(){
			return SK.lab("颜色", SK.textin("box-in", "#3366cc", "HEX、RGB、HSL、HSV 或 CMYK"))
				+ SK.lab("格式", '<select id="box-mode"><option value="hex">HEX</option><option value="rgb">RGB</option><option value="hsl">HSL</option><option value="hsv">HSV</option><option value="cmyk">CMYK</option></select>');
		},
		run: function(){
			return SK.colorconv(SK.val("box-mode"), SK.val("box-in"));
		}
	});
})();
(function(){
	SK.regutil("u-comp", {
		title: "复利",
		go: "计算",
		body: function(){
			return SK.row2(SK.lab("本金", SK.textin("box-p", "10000")), SK.lab("年利率 %", SK.textin("box-rate", "3")))
				+ SK.row2(SK.lab("年数", SK.textin("box-years", "5")), SK.lab("每年计息次数", SK.numin("box-n", "12")));
		},
		run: function(){
			return compound(SK.val("box-p"), SK.val("box-rate"), SK.val("box-years"), SK.val("box-n"));
		}
	});
	function compound(p, rate, years, times){
		var money = Number(String(p).trim());
		var y = Number(String(rate).trim());
		var t = Number(String(years).trim());
		var n = parseInt(times, 10);
		if (!(money > 0)) throw new Error("请输入本金");
		if (!(y >= 0) || y > 100) throw new Error("年利率填百分数，例如 3");
		if (!(t > 0) || t > 100) throw new Error("年数要大于 0，且不超过 100");
		if (!(n >= 1 && n <= 365)) throw new Error("每年计息次数要在 1 到 365");
		var a = money * Math.pow(1 + y / 100 / n, n * t);
		return "本息  " + SK.trimnum(a) + "\n利息  " + SK.trimnum(a - money);
	}

	SK.compound = compound;
})();
(function(){
	SK.regutil("u-dadd", {
		title: "日期加减",
		go: "计算",
		body: function(){
			return SK.row2(SK.lab("日期", '<input id="box-a" type="date">'), SK.lab("加减天数", SK.textin("box-n", "7")));
		},
		run: function(){
			return dateadd(SK.val("box-a"), SK.val("box-n"));
		},
		open: function(){
			SK.$("box-a").value = SK.ymd(new Date());
		}
	});
	function dateadd(date, days){
		if (!date) throw new Error("请选择日期");
		var n = parseInt(days, 10);
		if (!isFinite(n) || Math.abs(n) > 36600) throw new Error("天数超出范围");
		var d = new Date(date + "T00:00:00");
		if (isNaN(d.getTime())) throw new Error("日期无效");
		d.setDate(d.getDate() + n);
		return SK.ymd(d) + "  周" + "日一二三四五六".charAt(d.getDay());
	}

	SK.dateadd = dateadd;
})();
(function(){
	SK.regutil("u-datediff", {
		title: "日期差",
		go: "计算",
		body: function(){
			return SK.row2(SK.lab("开始", '<input id="box-a" type="date">'), SK.lab("结束", '<input id="box-b" type="date">'));
		},
		run: function(){
			return dateconv(SK.val("box-a"), SK.val("box-b"));
		},
		open: function(){
			var day = new Date();
			SK.$("box-b").value = SK.ymd(day);
			day.setDate(day.getDate() - 7);
			SK.$("box-a").value = SK.ymd(day);
		}
	});
	function dateconv(a, b){
		if (!a || !b) throw new Error("请选择两个日期");
		var da = new Date(a + "T00:00:00");
		var db = new Date(b + "T00:00:00");
		if (isNaN(da.getTime()) || isNaN(db.getTime())) throw new Error("日期无效");
		var days = Math.round((db.getTime() - da.getTime()) / 86400000);
		return "从 " + a + " 到 " + b + " 相差 " + days + " 天";
	}

	SK.dateconv = dateconv;
})();
(function(){
	SK.regutil("u-down", {
		title: "倒计时",
		go: "开始",
		body: function(){
			return SK.lab("秒数", SK.numin("box-n", "60"));
		},
		run: function(){
			try {
				countdowngo();
				SK.msg("box-msg", "计时中");
			}
			catch (ex) {
				SK.msg("box-msg", ex && ex.message ? ex.message : "失败", true);
			}
		},
		own: true
	});
	function fmthms(sec){
		var s = sec < 0 ? 0 : sec;
		return SK.pad2(Math.floor(s / 3600)) + ":" + SK.pad2(Math.floor(s / 60) % 60) + ":" + SK.pad2(s % 60);
	}

	function countdowngo(){
		SK.stoptick();
		var sec = parseInt(SK.val("box-n"), 10);
		if (!(sec >= 1 && sec <= 86400)) throw new Error("秒数要在 1 到 86400");
		var end = Date.now() + sec * 1000;
		SK.$("box-out").value = fmthms(sec);
		SK.tick = setInterval(function(){
			var left = Math.ceil((end - Date.now()) / 1000);
			if (left <= 0) {
				if (SK.tick) { clearInterval(SK.tick); SK.tick = null; }
				SK.$("box-out").value = "00:00:00\n时间到";
				SK.msg("box-msg", "时间到");
				return;
			}
			SK.$("box-out").value = fmthms(left);
		}, 200);
	}

	SK.fmthms = fmthms;
	SK.countdowngo = countdowngo;
})();
(function(){
	SK.regutil("u-fake", {
		title: "测试数据",
		go: "生成",
		body: function(){
			return SK.lab("行数", SK.numin("box-n", "5"));
		},
		run: function(){
			return fakedata(SK.val("box-n"));
		}
	});
	function fakedata(count){
		var n = parseInt(count, 10);
		if (!(n >= 1 && n <= 50)) throw new Error("行数要在 1 到 50");
		var sur = ["赵", "钱", "孙", "李", "周", "吴", "郑", "王"];
		var giv = ["伟", "芳", "娜", "敏", "静", "磊", "洋", "艳"];
		var lines = ["姓名,邮箱,手机"];
		var i;
		for (i = 0; i < n; i++) {
			var name = sur[SK.randint(sur.length)] + giv[SK.randint(giv.length)];
			var phone = "1" + String(3 + SK.randint(6));
			var k;
			for (k = 0; k < 9; k++) phone += String(SK.randint(10));
			lines.push(name + ",user" + (i + 1) + "@example.com," + phone);
		}
		return lines.join("\n");
	}

	SK.fakedata = fakedata;
})();
(function(){
	SK.regutil("u-grad", {
		title: "CSS 渐变",
		go: "生成",
		body: function(){
			return SK.row2(SK.lab("起始色", SK.textin("box-a", "#f97316")), SK.lab("结束色", SK.textin("box-b", "#111827")))
				+ SK.lab("角度", SK.numin("box-n", "90"));
		},
		run: function(){
			return gradcss(SK.val("box-a"), SK.val("box-b"), SK.val("box-n"));
		}
	});
	function gradcss(a, b, angle){
		var c1 = SK.colorrgb("hex", a);
		var c2 = SK.colorrgb("hex", b);
		var n = Number(angle);
		if (!isFinite(n) || n < 0 || n > 360) throw new Error("角度要在 0 到 360");
		var h1 = "#" + SK.hex2(c1[0]) + SK.hex2(c1[1]) + SK.hex2(c1[2]);
		var h2 = "#" + SK.hex2(c2[0]) + SK.hex2(c2[1]) + SK.hex2(c2[2]);
		return "background: linear-gradient(" + SK.trimnum(n) + "deg, " + h1 + ", " + h2 + ");";
	}

	SK.gradcss = gradcss;
})();
(function(){
	SK.regutil("u-ico", {
		title: "图片转 ICO",
		go: "生成",
		body: function(){
			return SK.lab("图片", '<input id="box-file" type="file" accept="image/*">')
				+ '<p class="hint">在此页按 Ctrl+V 可粘贴剪贴板里的图片。</p>'
				+ '<img id="ico-img" alt="预览" hidden>'
				+ '<div class="ops">'
				+ SK.radio("ico-size", "ico-16", "16", false) + SK.radio("ico-size", "ico-32", "32", true)
				+ SK.radio("ico-size", "ico-48", "48", false) + SK.radio("ico-size", "ico-64", "64", false)
				+ SK.radio("ico-size", "ico-128", "128", false) + SK.radio("ico-size", "ico-256", "256", false)
				+ "</div>"
				+ '<div class="ico-out"><canvas id="ico-prev" width="32" height="32"></canvas><span id="ico-prev-size">32×32</span></div>'
				+ '<p class="hint">只生成所选的一种尺寸。方图按实际像素显示，边框就是图标大小。画面等比放进方框，空白透明。</p>';
		},
		run: function(){
			icogo();
		},
		open: function(){
			SK.$("box-file").onchange = function(){
				var file = SK.$("box-file").files && SK.$("box-file").files[0];
				if (file) SK.seticopic(file);
			};
			var sizes = document.querySelectorAll('input[name="ico-size"]');
			var s;
			for (s = 0; s < sizes.length; s++) sizes[s].onchange = drawicoprev;
			if (SK.icoUrl) {
				var prev = SK.$("ico-img");
				prev.onload = drawicoprev;
				prev.src = SK.icoUrl;
				prev.hidden = false;
			}
			drawicoprev();
		},
		hideOut: true,
		hideCopy: true,
		own: true
	});
	function icosize(){
		var picked = document.querySelector('input[name="ico-size"]:checked');
		var size = picked ? parseInt(picked.value, 10) : 32;
		if (size !== 16 && size !== 32 && size !== 48 && size !== 64 && size !== 128 && size !== 256)
			size = 32;
		return size;
	}

	function drawicoprev(){
		var canvas = SK.$("ico-prev");
		if (!canvas) return;
		var size = icosize();
		var label = SK.$("ico-prev-size");
		if (label) label.textContent = size + "×" + size;
		canvas.width = size;
		canvas.height = size;
		var g = canvas.getContext("2d");
		if (!g) return;
		g.clearRect(0, 0, size, size);
		var src = SK.$("ico-img");
		if (!src || src.hidden || !src.naturalWidth) return;
		fiticon(g, src, size);
	}

	function fiticon(g, img, size){
		var iw = img.naturalWidth || img.width;
		var ih = img.naturalHeight || img.height;
		if (!iw || !ih) return;
		var scale = Math.min(size / iw, size / ih);
		var w = Math.max(1, Math.round(iw * scale));
		var h = Math.max(1, Math.round(ih * scale));
		g.imageSmoothingEnabled = true;
		try { g.imageSmoothingQuality = "high"; } catch (e) {}
		g.drawImage(img, Math.floor((size - w) / 2), Math.floor((size - h) / 2), w, h);
	}

	function icogo(){
		if (!SK.icoPic) {
			SK.msg("box-msg", "请选择或粘贴图片", true);
			return;
		}
		var size = icosize();
		var sizes = [size];
		var img = new Image();
		img.onload = function(){
			var jobs = [];
			var n;
			for (n = 0; n < sizes.length; n++) jobs.push(iconpng(img, sizes[n]));
			Promise.all(jobs).then(function(parts){
				var blob = icoblob(sizes, parts);
				var a = document.createElement("a");
				var url = URL.createObjectURL(blob);
				a.href = url;
				a.download = iconame(SK.icoPic.name);
				document.body.appendChild(a);
				a.click();
				document.body.removeChild(a);
				setTimeout(function(){ URL.revokeObjectURL(url); }, 4000);
				SK.msg("box-msg", "已下载 " + a.download + "（" + blob.size + " 字节）");
			}, function(){
				SK.msg("box-msg", "生成失败", true);
			});
		};
		img.onerror = function(){ SK.msg("box-msg", "无法读取图片", true); };
		SK.msg("box-msg", "生成中");
		img.src = SK.icoUrl;
	}

	function iconame(name){
		var base = String(name || "icon").replace(/\.[^.]+$/, "");
		if (!base) base = "icon";
		return base + ".ico";
	}

	function iconpng(img, size){
		var canvas = document.createElement("canvas");
		canvas.width = size;
		canvas.height = size;
		var g = canvas.getContext("2d");
		if (!g) return Promise.reject();
		g.clearRect(0, 0, size, size);
		fiticon(g, img, size);
		return new Promise(function(ok, bad){
			canvas.toBlob(function(blob){
				if (!blob) { bad(); return; }
				blob.arrayBuffer().then(ok, bad);
			}, "image/png");
		});
	}

	function icoblob(sizes, parts){
		var count = sizes.length;
		var off = 6 + count * 16;
		var total = off;
		var i;
		for (i = 0; i < parts.length; i++) total += parts[i].byteLength;
		var buf = new Uint8Array(total);
		var view = new DataView(buf.buffer);
		view.setUint16(2, 1, true);
		view.setUint16(4, count, true);
		for (i = 0; i < count; i++) {
			var at = 6 + i * 16;
			var side = sizes[i] >= 256 ? 0 : sizes[i];
			buf[at] = side;
			buf[at + 1] = side;
			view.setUint16(at + 4, 1, true);
			view.setUint16(at + 6, 32, true);
			view.setUint32(at + 8, parts[i].byteLength, true);
			view.setUint32(at + 12, off, true);
			buf.set(new Uint8Array(parts[i]), off);
			off += parts[i].byteLength;
		}
		return new Blob([buf], { type: "image/vnd.microsoft.icon" });
	}

	SK.icosize = icosize;
	SK.drawicoprev = drawicoprev;
	SK.fiticon = fiticon;
	SK.icogo = icogo;
	SK.iconame = iconame;
	SK.iconpng = iconpng;
	SK.icoblob = icoblob;
})();
(function(){
	SK.regutil("u-loan", {
		title: "房贷",
		go: "计算",
		body: function(){
			return SK.row2(SK.lab("金额", SK.textin("box-p", "100000")), SK.lab("年利率 %", SK.textin("box-rate", "3.5")))
				+ SK.row2(SK.lab("月数", SK.numin("box-n", "360")), SK.lab("方式", '<select id="box-mode"><option value="annuity">等额本息</option><option value="principal">等额本金</option></select>'));
		},
		run: function(){
			return loanpay(SK.val("box-p"), SK.val("box-rate"), SK.val("box-n"), SK.val("box-mode"));
		}
	});
	function loanpay(p, rate, months, mode){
		var money = Number(String(p).trim());
		var y = Number(String(rate).trim());
		var n = parseInt(months, 10);
		if (!(money > 0)) throw new Error("请输入贷款金额");
		if (!(y >= 0) || y > 100) throw new Error("年利率填百分数，例如 3.5");
		if (!(n >= 1 && n <= 600)) throw new Error("月数要在 1 到 600");
		var r = y / 100 / 12;
		if (mode === "principal") {
			var prin = money / n;
			var first = prin + money * r;
			var last = prin + prin * r;
			var interest = (n + 1) * money * r / 2;
			return "等额本金\n每月本金  " + SK.trimnum(prin)
				+ "\n首月还款  " + SK.trimnum(first)
				+ "\n末月还款  " + SK.trimnum(last)
				+ "\n利息合计  " + SK.trimnum(interest)
				+ "\n还款合计  " + SK.trimnum(money + interest);
		}
		var pay = r === 0 ? money / n : money * r * Math.pow(1 + r, n) / (Math.pow(1 + r, n) - 1);
		var total = pay * n;
		return "等额本息\n每月还款  " + SK.trimnum(pay)
			+ "\n利息合计  " + SK.trimnum(total - money)
			+ "\n还款合计  " + SK.trimnum(total);
	}

	SK.loanpay = loanpay;
})();
(function(){
	SK.regutil("u-mac", {
		title: "MAC 地址",
		go: "生成",
		body: function(){
			return SK.lab("个数", SK.numin("box-n", "1"));
		},
		run: function(){
			return macgen(SK.val("box-n"));
		}
	});
	function macgen(count){
		var n = parseInt(count, 10);
		if (!(n >= 1 && n <= 20)) throw new Error("个数要在 1 到 20");
		if (!window.crypto || typeof crypto.getRandomValues !== "function") throw new Error("浏览器不能生成随机数");
		var lines = [];
		var i;
		for (i = 0; i < n; i++) {
			var b = new Uint8Array(6);
			crypto.getRandomValues(b);
			b[0] = (b[0] & 252) | 2;
			var parts = [];
			var j;
			for (j = 0; j < 6; j++) parts.push(SK.hex2(b[j]).toUpperCase());
			lines.push(parts.join(":"));
		}
		return lines.join("\n");
	}

	SK.macgen = macgen;
})();
(function(){
	SK.regutil("u-madd", {
		title: "月份加减",
		go: "计算",
		body: function(){
			return SK.row2(SK.lab("日期", '<input id="box-a" type="date">'), SK.lab("加减月数", SK.textin("box-n", "1")));
		},
		run: function(){
			return monthadd(SK.val("box-a"), SK.val("box-n"));
		},
		open: function(){
			SK.$("box-a").value = SK.ymd(new Date());
		}
	});
	function monthadd(date, months){
		if (!date) throw new Error("请选择日期");
		var n = parseInt(months, 10);
		if (!isFinite(n) || Math.abs(n) > 1200) throw new Error("月数超出范围");
		var d = new Date(date + "T00:00:00");
		if (isNaN(d.getTime())) throw new Error("日期无效");
		var day = d.getDate();
		d.setDate(1);
		d.setMonth(d.getMonth() + n);
		var last = new Date(d.getFullYear(), d.getMonth() + 1, 0).getDate();
		d.setDate(day < last ? day : last);
		return SK.ymd(d) + "  周" + "日一二三四五六".charAt(d.getDay());
	}

	SK.monthadd = monthadd;
})();
(function(){
	SK.regutil("u-pal", {
		title: "调色板",
		go: "生成",
		body: function(){
			return SK.lab("基准色", SK.textin("box-in", "#f97316", "#RRGGBB"));
		},
		run: function(){
			return palette(SK.val("box-in"));
		}
	});
	function palette(hex){
		var rgb = SK.colorrgb("hex", hex);
		var hsl = SK.rgb2hsl(rgb[0], rgb[1], rgb[2]);
		function one(delta){
			var h = (hsl[0] + delta) % 360;
			if (h < 0) h += 360;
			return hslhex(h, hsl[1], hsl[2]);
		}
		return "基准  " + one(0) + "\n互补  " + one(180) + "\n邻近  " + one(-30) + "\n邻近  " + one(30) + "\n三角  " + one(120) + "\n三角  " + one(240);
	}

	function hslhex(h, s, l){
		var rgb = SK.hsl2rgb(h, s, l);
		return "#" + SK.hex2(rgb[0]) + SK.hex2(rgb[1]) + SK.hex2(rgb[2]);
	}

	SK.palette = palette;
	SK.hslhex = hslhex;
})();
(function(){
	SK.regutil("u-ph", {
		title: "占位图",
		go: "生成",
		body: function(){
			return SK.row2(SK.lab("宽", SK.numin("box-w", "320")), SK.lab("高", SK.numin("box-h", "180")))
				+ SK.row2(SK.lab("底色", SK.textin("box-bg", "#eef1f4")), SK.lab("字色", SK.textin("box-fg", "#333333")))
				+ SK.lab("文字", SK.textin("box-in", "", "留空则写宽高"))
				+ '<img id="box-img" alt="" hidden>';
		},
		run: function(){
			return placeholder(SK.val("box-w"), SK.val("box-h"), SK.val("box-in"), SK.val("box-bg"), SK.val("box-fg"));
		}
	});
	function placesize(w, h){
		var width = parseInt(w, 10);
		var height = parseInt(h, 10);
		if (!(width >= 1 && width <= 2000) || !(height >= 1 && height <= 2000)) throw new Error("宽高要在 1 到 2000");
		return [width, height];
	}

	function placeholder(w, h, label, bg, fg){
		var wh = placesize(w, h);
		var back = SK.colorrgb("hex", bg);
		var ink = SK.colorrgb("hex", fg);
		if (typeof document === "undefined" || !document.createElement) throw new Error("浏览器不能画图");
		var canvas = document.createElement("canvas");
		canvas.width = wh[0];
		canvas.height = wh[1];
		var g = canvas.getContext("2d");
		if (!g) throw new Error("浏览器不能画图");
		g.fillStyle = "#" + SK.hex2(back[0]) + SK.hex2(back[1]) + SK.hex2(back[2]);
		g.fillRect(0, 0, wh[0], wh[1]);
		g.fillStyle = "#" + SK.hex2(ink[0]) + SK.hex2(ink[1]) + SK.hex2(ink[2]);
		g.font = Math.max(12, Math.round(Math.min(wh[0], wh[1]) / 8)) + "px sans-serif";
		g.textAlign = "center";
		g.textBaseline = "middle";
		g.fillText(String(label || "").trim() || (wh[0] + "×" + wh[1]), wh[0] / 2, wh[1] / 2);
		var url = canvas.toDataURL("image/png");
		var img = SK.$("box-img");
		if (img) {
			img.src = url;
			img.hidden = false;
		}
		return url;
	}

	SK.placesize = placesize;
	SK.placeholder = placeholder;
})();
(function(){
	SK.regutil("u-pick", {
		title: "取色",
		go: "转换",
		body: function(){
			return '<div class="cpick">'
				+ '<div class="cpick-top"><span>颜色</span><i id="cpick-swatch"></i></div>'
				+ '<div class="cpick-sv" id="cpick-sv"><i id="cpick-svdot"></i></div>'
				+ '<div class="cpick-hue" id="cpick-hue"><i id="cpick-huedot"></i></div>'
				+ '<div class="cpick-rgb">'
				+ '<label>R<input id="cpick-r" inputmode="numeric"></label>'
				+ '<label>G<input id="cpick-g" inputmode="numeric"></label>'
				+ '<label>B<input id="cpick-b" inputmode="numeric"></label>'
				+ "</div>"
				+ '<input id="box-color" type="hidden" value="#3366cc">'
				+ "</div>";
		},
		run: function(){
			return SK.colorconv("hex", SK.val("box-color"));
		},
		open: function(){
			bindpick();
		}
	});
	function bindpick(){
		var hue = 220;
		var sat = 0.75;
		var val = 0.8;
		var sv = SK.$("cpick-sv");
		var huebar = SK.$("cpick-hue");
		var drag = "";
		function clamp01(n){
			if (n < 0) return 0;
			if (n > 1) return 1;
			return n;
		}
		function paint(){
			var rgb = hsv2rgb(hue, sat * 100, val * 100);
			var hex = "#" + SK.hex2(rgb[0]) + SK.hex2(rgb[1]) + SK.hex2(rgb[2]);
			SK.$("box-color").value = hex;
			SK.$("cpick-swatch").style.background = hex;
			sv.style.backgroundColor = "hsl(" + hue + ",100%,50%)";
			SK.$("cpick-svdot").style.left = (sat * 100) + "%";
			SK.$("cpick-svdot").style.top = ((1 - val) * 100) + "%";
			SK.$("cpick-huedot").style.left = (hue / 360 * 100) + "%";
			SK.$("cpick-r").value = String(rgb[0]);
			SK.$("cpick-g").value = String(rgb[1]);
			SK.$("cpick-b").value = String(rgb[2]);
		}
		function fromsv(ev){
			var r = sv.getBoundingClientRect();
			sat = clamp01((ev.clientX - r.left) / r.width);
			val = 1 - clamp01((ev.clientY - r.top) / r.height);
			paint();
		}
		function fromhue(ev){
			var r = huebar.getBoundingClientRect();
			hue = clamp01((ev.clientX - r.left) / r.width) * 360;
			if (hue >= 360) hue = 0;
			paint();
		}
		function down(which, ev){
			drag = which;
			ev.currentTarget.setPointerCapture(ev.pointerId);
			if (which === "sv") fromsv(ev);
			else fromhue(ev);
		}
		sv.onpointerdown = function(ev){ down("sv", ev); };
		sv.onpointermove = function(ev){ if (drag === "sv") fromsv(ev); };
		sv.onpointerup = function(){ drag = ""; };
		huebar.onpointerdown = function(ev){ down("hue", ev); };
		huebar.onpointermove = function(ev){ if (drag === "hue") fromhue(ev); };
		huebar.onpointerup = function(){ drag = ""; };
		function fromrgb(){
			var r = Number(SK.$("cpick-r").value);
			var g = Number(SK.$("cpick-g").value);
			var b = Number(SK.$("cpick-b").value);
			if (!isFinite(r) || !isFinite(g) || !isFinite(b)) return;
			if (r < 0) r = 0;
			if (g < 0) g = 0;
			if (b < 0) b = 0;
			if (r > 255) r = 255;
			if (g > 255) g = 255;
			if (b > 255) b = 255;
			r = Math.round(r);
			g = Math.round(g);
			b = Math.round(b);
			var hsv = SK.rgb2hsv(r, g, b);
			hue = hsv[0];
			sat = hsv[1] / 100;
			val = hsv[2] / 100;
			paint();
		}
		SK.$("cpick-r").oninput = fromrgb;
		SK.$("cpick-g").oninput = fromrgb;
		SK.$("cpick-b").oninput = fromrgb;
		var start = SK.rgb2hsv(51, 102, 204);
		hue = start[0];
		sat = start[1] / 100;
		val = start[2] / 100;
		paint();
	}

	function hue(s){
		var n = Number(String(s).replace(/deg$/i, ""));
		if (!isFinite(n)) throw new Error("色相无效");
		n = n % 360;
		if (n < 0) n += 360;
		return n;
	}

	function hsv2rgb(h, s, v){
		s /= 100; v /= 100;
		var c = v * s;
		var hp = h / 60;
		var x = c * (1 - Math.abs(hp % 2 - 1));
		var m = v - c;
		return hue2rgb(hp, c, x, m);
	}

	function hue2rgb(hp, c, x, m){
		var r = 0, g = 0, b = 0;
		if (hp < 1) { r = c; g = x; }
		else if (hp < 2) { r = x; g = c; }
		else if (hp < 3) { g = c; b = x; }
		else if (hp < 4) { g = x; b = c; }
		else if (hp < 5) { r = x; b = c; }
		else { r = c; b = x; }
		return [Math.round((r + m) * 255), Math.round((g + m) * 255), Math.round((b + m) * 255)];
	}

	SK.bindpick = bindpick;
	SK.hue = hue;
	SK.hsv2rgb = hsv2rgb;
	SK.hue2rgb = hue2rgb;
})();
(function(){
	SK.regutil("u-pw", {
		title: "随机密码",
		go: "生成",
		body: function(){
			return SK.row2(SK.lab("长度", SK.numin("box-len", "16")), SK.lab("条数", SK.numin("box-n", "5")))
				+ SK.check("box-low", "小写", true) + SK.check("box-up", "大写", true)
				+ SK.check("box-dig", "数字", true) + SK.check("box-sym", "符号", true)
				+ SK.check("box-amb", "去掉易混字符（0、O、o、1、l、I）", false)
				+ SK.check("box-each", "每类至少一个", true)
				+ SK.check("box-set", "指定字符集", false)
				+ SK.lab("字符集", SK.textin("box-set-chars", "", "手动输入，重复的只算一次"));
		},
		run: function(){
			return pwtext();
		},
		open: function(){
			var pwids = ["box-low", "box-up", "box-dig", "box-sym", "box-amb"];
			var pi;
			for (pi = 0; pi < pwids.length; pi++) SK.$(pwids[pi]).onchange = fillpwset;
			SK.$("box-set").onchange = pwsetmode;
			pwsetmode();
		}
	});
	function pwsetmode(){
		var on = SK.onbox("box-set");
		var ids = ["box-low", "box-up", "box-dig", "box-sym", "box-amb", "box-each"];
		var i;
		for (i = 0; i < ids.length; i++) {
			var el = SK.$(ids[i]);
			if (el) el.disabled = on;
		}
		var box = SK.$("box-set-chars");
		if (box) box.disabled = !on;
		if (!on) fillpwset();
	}

	function pwgroups(){
		var drop = SK.onbox("box-amb");
		function clean(s){
			return drop ? s.replace(/[0Oo1lI]/g, "") : s;
		}
		var groups = [];
		if (SK.onbox("box-low")) groups.push(clean("abcdefghijklmnopqrstuvwxyz"));
		if (SK.onbox("box-up")) groups.push(clean("ABCDEFGHIJKLMNOPQRSTUVWXYZ"));
		if (SK.onbox("box-dig")) groups.push(clean("0123456789"));
		if (SK.onbox("box-sym")) groups.push("!@#$%^&*-_=+?~");
		return groups;
	}

	function fillpwset(){
		if (SK.onbox("box-set")) return;
		var box = SK.$("box-set-chars");
		if (!box) return;
		var groups = pwgroups();
		var s = "";
		var i;
		for (i = 0; i < groups.length; i++) s += groups[i];
		box.value = s;
	}

	function pwcharset(s){
		var seen = {};
		var out = "";
		var i;
		s = String(s || "").replace(/[\r\n]/g, "");
		for (i = 0; i < s.length && out.length < 256; i++) {
			var ch = s.charAt(i);
			if (ch.charCodeAt(0) < 32) continue;
			if (seen[ch]) continue;
			seen[ch] = 1;
			out += ch;
		}
		return out;
	}

	function pwtext(){
		var len = parseInt(SK.val("box-len"), 10);
		var count = parseInt(SK.val("box-n"), 10);
		if (!isFinite(len)) len = 16;
		if (len < 4) len = 4;
		if (len > 128) len = 128;
		if (!isFinite(count)) count = 5;
		if (count < 1) count = 1;
		if (count > 50) count = 50;
		if (SK.onbox("box-set")) {
			var set = pwcharset(SK.val("box-set-chars"));
			if (!set) throw new Error("请填写字符集");
			var made = [];
			var k;
			for (k = 0; k < count; k++) made.push(pwone(len, [set], set, false));
			return made.join("\n");
		}
		var each = SK.onbox("box-each");
		var groups = pwgroups();
		var kept = [];
		var pool = "";
		var g;
		for (g = 0; g < groups.length; g++) {
			if (!groups[g]) throw new Error("去掉易混字符后某一类是空的");
			kept.push(groups[g]);
			pool += groups[g];
		}
		if (!pool) throw new Error("请至少选一类字符");
		if (each && len < kept.length) len = kept.length;
		var lines = [];
		var n;
		for (n = 0; n < count; n++) lines.push(pwone(len, kept, pool, each));
		return lines.join("\n");
	}

	function pwone(len, groups, pool, each){
		var chars = [];
		var g;
		if (each) {
			for (g = 0; g < groups.length; g++)
				chars.push(groups[g].charAt(SK.randint(groups[g].length)));
		}
		while (chars.length < len) chars.push(pool.charAt(SK.randint(pool.length)));
		for (var i = chars.length - 1; i > 0; i--) {
			var j = SK.randint(i + 1);
			var tmp = chars[i];
			chars[i] = chars[j];
			chars[j] = tmp;
		}
		return chars.join("");
	}

	SK.pwsetmode = pwsetmode;
	SK.pwgroups = pwgroups;
	SK.fillpwset = fillpwset;
	SK.pwcharset = pwcharset;
	SK.pwtext = pwtext;
	SK.pwone = pwone;
})();
(function(){
	SK.regutil("u-radix", {
		title: "进制转换",
		go: "转换",
		body: function(){
			return SK.lab("整数", SK.textin("box-in", "", "例如 FF")) + SK.row2(SK.lab("从", SK.numin("box-from", "16")), SK.lab("到", SK.numin("box-to", "10")));
		},
		run: function(){
			return radixconv(SK.val("box-in"), SK.val("box-from"), SK.val("box-to"));
		}
	});
	function radixconv(text, from, to){
		var a = parseInt(from, 10);
		var b = parseInt(to, 10);
		if (!(a >= 2 && a <= 36) || !(b >= 2 && b <= 36)) throw new Error("进制要在 2 到 36");
		var t = String(text).trim();
		if (!t) throw new Error("请输入整数");
		if (t.length > 40) throw new Error("太长");
		var neg = false;
		if (t.charAt(0) === "-") {
			neg = true;
			t = t.slice(1);
		}
		if (!t) throw new Error("请输入整数");
		var alpha = "0123456789abcdefghijklmnopqrstuvwxyz";
		var n = 0;
		var i;
		for (i = 0; i < t.length; i++) {
			var d = alpha.indexOf(t.charAt(i).toLowerCase());
			if (d < 0 || d >= a) throw new Error("有数字超出进制");
			n = n * a + d;
			if (n > 9007199254740991) throw new Error("超出安全整数");
		}
		return (neg ? "-" : "") + n.toString(b).toUpperCase();
	}

	SK.radixconv = radixconv;
})();
(function(){
	SK.regutil("u-rand", {
		title: "随机数",
		go: "生成",
		body: function(){
			return SK.row2(SK.lab("最小", SK.textin("box-min", "1")), SK.lab("最大", SK.textin("box-max", "100")))
				+ SK.row2(SK.lab("个数", SK.numin("box-n", "5")), SK.lab("类型", '<select id="box-kind"><option value="int">整数</option><option value="float">小数</option></select>'));
		},
		run: function(){
			return randtext(SK.val("box-min"), SK.val("box-max"), SK.val("box-n"), SK.val("box-kind") === "float");
		}
	});
	function randtext(min, max, count, asFloat){
		var a = Number(String(min).trim());
		var b = Number(String(max).trim());
		var c = parseInt(count, 10);
		if (!isFinite(a) || !isFinite(b)) throw new Error("请输入范围");
		if (a > b) throw new Error("最小值不能大于最大值");
		if (!(c >= 1 && c <= 100)) throw new Error("个数要在 1 到 100");
		var lines = [];
		var i;
		if (asFloat) {
			for (i = 0; i < c; i++) lines.push(SK.trimnum(a + (b - a) * (SK.randint(0x1000000) / 0x1000000)));
			return lines.join("\n");
		}
		if (Math.floor(a) !== a || Math.floor(b) !== b) throw new Error("整数范围请填整数");
		if (Math.abs(a) > 9007199254740991 || Math.abs(b) > 9007199254740991) throw new Error("超出安全整数");
		var span = b - a + 1;
		for (i = 0; i < c; i++) lines.push(String(a + SK.randint(span)));
		return lines.join("\n");
	}

	SK.randtext = randtext;
})();
(function(){
	SK.regutil("u-rcolor", {
		title: "随机颜色",
		go: "生成",
		body: function(){
			return SK.lab("个数", SK.numin("box-n", "5"));
		},
		run: function(){
			return rcolor(SK.val("box-n"));
		}
	});
	function rcolor(count){
		var n = parseInt(count, 10);
		if (!(n >= 1 && n <= 20)) throw new Error("个数要在 1 到 20");
		if (!window.crypto || typeof crypto.getRandomValues !== "function") throw new Error("浏览器不能生成随机数");
		var lines = [];
		var i;
		for (i = 0; i < n; i++) {
			var b = new Uint8Array(3);
			crypto.getRandomValues(b);
			var hex = "#" + SK.hex2(b[0]) + SK.hex2(b[1]) + SK.hex2(b[2]);
			lines.push(hex + "  rgb(" + b[0] + ", " + b[1] + ", " + b[2] + ")");
		}
		return lines.join("\n");
	}

	SK.rcolor = rcolor;
})();
(function(){
	SK.regutil("u-re", {
		title: "正则测试",
		go: "测试",
		body: function(){
			return SK.lab("常用", '<select id="box-preset">' + reopts() + "</select>")
				+ '<div class="row grow">' + SK.lab("表达式", SK.textin("box-pat", "", "例如 \\d+")) + SK.lab("标志", SK.textin("box-flags", "g", "g i m")) + "</div>"
				+ SK.lab("方式", '<select id="box-re-mode"><option value="find">查找匹配</option><option value="lines">批量测试</option></select>')
				+ '<p class="hint" id="re-hint">在文本里查找匹配。</p>'
				+ SK.lab("文本", SK.ta("box-in", 8, ""));
		},
		run: function(){
			return retest(SK.val("box-pat"), SK.val("box-flags"), SK.val("box-in"));
		},
		open: function(){
			SK.$("box-preset").onchange = repick;
			SK.$("box-re-mode").onchange = retip;
			retip();
		}
	});
	function repatterns(){
		return [
			["邮箱", "[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}", "gi", "name@example.com"],
			["手机号", "1[3-9]\\d{9}", "g", "13800138000"],
			["身份证", "[1-9]\\d{5}(?:19|20)\\d{2}(?:0[1-9]|1[0-2])(?:0[1-9]|[12]\\d|3[01])\\d{3}[\\dXx]", "gi", "110101199001011234"],
			["网址", "https?:\\/\\/[^\\s<>\"]+", "gi", "https://example.com/a"],
			["IPv4", "(?:(?:25[0-5]|2[0-4]\\d|[01]?\\d\\d?)\\.){3}(?:25[0-5]|2[0-4]\\d|[01]?\\d\\d?)", "g", "192.168.0.1"],
			["日期", "\\d{4}-(?:0[1-9]|1[0-2])-(?:0[1-9]|[12]\\d|3[01])", "g", "2026-10-10"],
			["时间", "(?:[01]\\d|2[0-3]):[0-5]\\d(?::[0-5]\\d)?", "g", "09:30:00"],
			["整数", "-?\\d+", "g", "-12"],
			["小数", "-?\\d+\\.\\d+", "g", "3.14"],
			["中文", "[\\u4e00-\\u9fff]+", "g", "汉字"],
			["颜色", "#[0-9A-Fa-f]{3}(?:[0-9A-Fa-f]{3})?", "g", "#3366cc"],
			["邮编", "[1-9]\\d{5}", "g", "100000"],
			["QQ 号", "[1-9]\\d{4,10}", "g", "10001"],
			["车牌", "[京津沪渝冀豫云辽黑湘皖鲁新苏浙赣鄂桂甘晋蒙陕吉闽贵粤青藏川宁琼使领][A-Z][A-HJ-NP-Z0-9]{4,5}[A-HJ-NP-Z0-9挂学警港澳]", "g", "京A12345"],
			["空白行", "^[ \\t]*$", "gm", "a\n\nb"],
		];
	}

	function reopts(){
		var list = repatterns();
		var html = '<option value="">请选择</option>';
		var i;
		for (i = 0; i < list.length; i++)
			html += '<option value="' + i + '">' + SK.esc(list[i][0]) + "</option>";
		return html;
	}

	function repick(){
		var sel = SK.$("box-preset");
		var list = repatterns();
		var i = sel ? parseInt(sel.value, 10) : -1;
		if (!(i >= 0) || !list[i]) return;
		var box = SK.$("box-in");
		var cur = box ? box.value : "";
		var sample = !cur;
		var k;
		if (!sample) {
			for (k = 0; k < list.length; k++) if (cur === list[k][3]) sample = true;
		}
		SK.$("box-pat").value = list[i][1];
		SK.$("box-flags").value = list[i][2];
		if (sample && box) box.value = list[i][3] || "";
	}

	function retip(){
		var lines = SK.val("box-re-mode") === "lines";
		var hint = SK.$("re-hint");
		var box = SK.$("box-in");
		if (hint) hint.textContent = lines ? "一行一条。整行匹配算通过。" : "在文本里查找匹配。";
		if (box) box.placeholder = lines ? "一行一条" : "";
	}

	function showreout(batch){
		var list = SK.$("re-list");
		var out = SK.$("box-out");
		if (!list || !out) return;
		list.hidden = !batch;
		out.hidden = !!batch;
		if (!batch) list.innerHTML = "";
	}

	function recompile(pat, flags){
		pat = String(pat);
		flags = String(flags || "");
		if (!pat) throw new Error("请输入表达式");
		if (pat.length > 200) throw new Error("表达式太长");
		if (!/^[gim]*$/.test(flags)) throw new Error("标志只支持 g、i、m");
		var seen = {};
		var i;
		for (i = 0; i < flags.length; i++) {
			if (seen[flags.charAt(i)]) throw new Error("标志重复了");
			seen[flags.charAt(i)] = 1;
		}
		try { return new RegExp(pat, flags); }
		catch (ex) { throw new Error("表达式无效"); }
	}

	function relines(pat, flags, text){
		flags = String(flags || "");
		var re = recompile(pat, flags);
		text = String(text);
		if (text.length > 20000) throw new Error("文本太长");
		var rows = text.replace(/\r\n/g, "\n").replace(/\r/g, "\n").split("\n");
		if (rows.length && rows[rows.length - 1] === "") rows.pop();
		if (!rows.length) throw new Error("请输入文本");
		if (rows.length > 500) throw new Error("最多 500 行");
		var full = new RegExp("^(?:" + re.source + ")$", flags.replace(/g/g, ""));
		var html = "";
		var plain = [];
		var pass = 0;
		var i;
		for (i = 0; i < rows.length; i++) {
			var ok = full.test(rows[i]);
			if (ok) pass++;
			var mark = ok ? "通过" : "未通过";
			html += '<div>' + SK.esc(rows[i]) + ' <span class="' + (ok ? "ok" : "bad") + '">[' + mark + "]</span></div>";
			plain.push(rows[i] + " [" + mark + "]");
		}
		SK.$("re-list").innerHTML = html;
		showreout(true);
		SK.reNote = "通过 " + pass + "，未通过 " + (rows.length - pass);
		return plain.join("\n");
	}

	function retest(pat, flags, text){
		flags = String(flags || "");
		if (SK.val("box-re-mode") === "lines") return relines(pat, flags, text);
		showreout(false);
		var re = recompile(pat, flags);
		text = String(text);
		if (text.length > 20000) throw new Error("文本太长");
		function one(m){
			var line = "[" + m.index + "] " + m[0];
			var g;
			for (g = 1; g < m.length; g++) line += "\n  " + g + ": " + (m[g] == null ? "" : m[g]);
			return line;
		}
		if (flags.indexOf("g") < 0) {
			var hit = re.exec(text);
			return hit ? one(hit) : "无匹配";
		}
		var lines = [];
		var guard = 0;
		var m2;
		while ((m2 = re.exec(text)) && lines.length < 100) {
			lines.push(one(m2));
			if (m2[0].length === 0) re.lastIndex++;
			guard++;
			if (guard > 10000) break;
		}
		if (!lines.length) return "无匹配";
		if (lines.length >= 100) lines.push("…只显示前 100 处");
		return lines.join("\n");
	}

	SK.repatterns = repatterns;
	SK.reopts = reopts;
	SK.repick = repick;
	SK.retip = retip;
	SK.showreout = showreout;
	SK.recompile = recompile;
	SK.relines = relines;
	SK.retest = retest;
})();
(function(){
	SK.regutil("u-rmb", {
		title: "人民币大写",
		go: "转换",
		body: function(){
			return SK.lab("金额", SK.textin("box-in", "", "例如 10010.50"));
		},
		run: function(){
			return rmbupper(SK.val("box-in"));
		}
	});
	function rmbupper(text){
		var s = String(text).trim();
		if (!/^\d+(\.\d+)?$/.test(s)) throw new Error("请输入非负金额");
		var parts = s.split(".");
		if (parts[0].length > 12) throw new Error("金额太大");
		var yuan = parseInt(parts[0], 10);
		var frac = (parts[1] || "") + "000";
		var fen = parseInt(frac.slice(0, 2), 10);
		if (parseInt(frac.charAt(2), 10) >= 5) fen++;
		var all = yuan * 100 + fen;
		if (!isFinite(all) || all < 0 || Math.floor(all / 100) > 999999999999) throw new Error("金额太大");
		if (all === 0) return "零元整";
		var digit = "零壹贰叁肆伍陆柒捌玖";
		var small = ["", "拾", "佰", "仟"];
		var big = ["", "万", "亿"];
		var y = Math.floor(all / 100);
		var jiao = Math.floor(all / 10) % 10;
		var f = all % 10;
		var chunks = [];
		while (y > 0) {
			chunks.push(y % 10000);
			y = Math.floor(y / 10000);
		}
		var body = "";
		var gap = false;
		var gi;
		for (gi = chunks.length - 1; gi >= 0; gi--) {
			var sec = chunks[gi];
			if (!sec) {
				gap = true;
				continue;
			}
			var piece = "";
			var zero = false;
			var left = sec;
			var i;
			for (i = 0; i < 4; i++) {
				var d = left % 10;
				if (d === 0) {
					if (piece) zero = true;
				}
				else {
					piece = digit.charAt(d) + small[i] + (zero ? "零" : "") + piece;
					zero = false;
				}
				left = Math.floor(left / 10);
			}
			if (body && (gap || sec < 1000)) body += "零";
			body += piece + big[gi];
			gap = false;
		}
		var tail;
		if (jiao === 0 && f === 0) tail = "整";
		else {
			tail = "";
			if (jiao) tail += digit.charAt(jiao) + "角";
			else if (f && yuan) tail += "零";
			if (f) tail += digit.charAt(f) + "分";
		}
		if (!body) return tail;
		return body + "元" + tail;
	}

	SK.rmbupper = rmbupper;
})();
(function(){
	SK.regutil("u-sci", {
		title: "科学计算",
		go: "计算",
		body: function(){
			return SK.lab("算式", SK.ta("box-in", 4, "支持三角函数（角度）、sqrt、log、ln、abs 和 pi"));
		},
		run: function(){
			return sciexpr(SK.val("box-in"));
		}
	});
	function sciexpr(src){
		var s = String(src == null ? "" : src).replace(/\s+/g, "");
		if (!s) throw new Error("请输入算式");
		if (s.length > 200) throw new Error("算式太长");
		var i = 0;
		var v = expr();
		if (i !== s.length) throw new Error("算式里有无法识别的部分");
		return SK.trimnum(v);
		function expr(){
			var left = term();
			while (peek() === "+" || peek() === "-") {
				var op = peek();
				i++;
				var right = term();
				left = op === "+" ? left + right : left - right;
			}
			return left;
		}
		function term(){
			var left = pow();
			while (peek() === "*" || peek() === "/" || peek() === "%") {
				var op = peek();
				i++;
				var right = pow();
				if (op === "*") left = left * right;
				else if (right === 0) throw new Error("除数不能为 0");
				else left = op === "/" ? left / right : left % right;
			}
			return left;
		}
		function pow(){
			var left = unary();
			if (peek() === "^") {
				i++;
				var right = pow();
				var v = Math.pow(left, right);
				if (!isFinite(v)) throw new Error("乘方结果无效");
				return v;
			}
			return left;
		}
		function unary(){
			if (peek() === "-" || peek() === "+") {
				var op = peek();
				i++;
				return op === "-" ? -unary() : unary();
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
			var name = s.slice(start, i).toLowerCase();
			if (peek() === "(") {
				i++;
				var arg = expr();
				if (peek() !== ")") throw new Error("括号没有配对");
				i++;
				return applyfn(name, arg);
			}
			if (name === "pi") return Math.PI;
			if (name === "e") return Math.E;
			throw new Error("没有这个名字");
		}
		function number(){
			var start = i;
			while (i < s.length && s.charAt(i) >= "0" && s.charAt(i) <= "9") i++;
			if (peek() === "." && i + 1 < s.length && s.charAt(i + 1) >= "0" && s.charAt(i + 1) <= "9") {
				i++;
				while (i < s.length && s.charAt(i) >= "0" && s.charAt(i) <= "9") i++;
			}
			if (start === i) throw new Error("这里需要数字");
			return Number(s.slice(start, i));
		}
		function peek(){ return s.charAt(i); }
		function isletter(c){ return (c >= "a" && c <= "z") || (c >= "A" && c <= "Z"); }
	}

	function applyfn(name, v){
		var rad = v * Math.PI / 180;
		var out;
		if (name === "sin") out = Math.sin(rad);
		else if (name === "cos") out = Math.cos(rad);
		else if (name === "tan") out = Math.tan(rad);
		else if (name === "asin") out = Math.asin(v) * 180 / Math.PI;
		else if (name === "acos") out = Math.acos(v) * 180 / Math.PI;
		else if (name === "atan") out = Math.atan(v) * 180 / Math.PI;
		else if (name === "ln") out = Math.log(v);
		else if (name === "log") out = Math.log(v) / Math.LN10;
		else if (name === "sqrt") out = Math.sqrt(v);
		else if (name === "abs") out = Math.abs(v);
		else if (name === "floor") out = Math.floor(v);
		else if (name === "ceil") out = Math.ceil(v);
		else throw new Error("没有这个函数");
		if (!isFinite(out)) throw new Error("函数结果无效");
		return out;
	}

	SK.sciexpr = sciexpr;
	SK.applyfn = applyfn;
})();
(function(){
	SK.regutil("u-table", {
		title: "HTML 表格",
		go: "生成",
		body: function(){
			return SK.lab("表格文字", SK.ta("box-in", 8, "第一行是表头。用逗号或制表符分列"));
		},
		run: function(){
			return htmltable(SK.val("box-in"));
		}
	});
	function htmltable(text){
		var raw = String(text).replace(/\r\n/g, "\n").replace(/\r/g, "\n");
		if (raw.length > 200000) throw new Error("文字太长");
		var rows = raw.split("\n");
		var body = [];
		var i;
		for (i = 0; i < rows.length; i++) if (rows[i]) body.push(rows[i]);
		if (!body.length) throw new Error("请输入表格文字");
		if (body.length > 500) throw new Error("最多 500 行");
		var sep = body[0].indexOf("\t") >= 0 ? "\t" : ",";
		var html = "<table>\n";
		for (i = 0; i < body.length; i++) {
			var cells = body[i].split(sep);
			var tag = i === 0 ? "th" : "td";
			var j;
			html += "<tr>";
			for (j = 0; j < cells.length; j++) html += "<" + tag + ">" + SK.htmlenc(cells[j]) + "</" + tag + ">";
			html += "</tr>\n";
		}
		return html + "</table>";
	}

	SK.htmltable = htmltable;
})();
(function(){
	SK.regutil("u-ts", {
		title: "时间戳",
		go: "转换",
		body: function(){
			return SK.lab("时间戳或日期", SK.ta("box-in", 4, "留空为现在。秒、毫秒，或 2026-10-09 12:00:00"));
		},
		run: function(){
			return tsconv(SK.val("box-in"));
		},
		open: function(){
			SK.$("box-in").value = String(Math.floor(Date.now() / 1000));
		}
	});
	function tsconv(raw){
		raw = String(raw || "").trim();
		var d;
		if (!raw) d = new Date();
		else if (/^-?\d+(\.\d+)?$/.test(raw)) {
			var n = Number(raw);
			if (!isFinite(n)) throw new Error("无法解析");
			var ms = Math.abs(n) > 1e12 ? n : n * 1000;
			d = new Date(ms);
		}
		else d = parsedate(raw);
		if (isNaN(d.getTime())) throw new Error("无法解析");
		return "本地 " + SK.fmtdt(d) + "\n秒 " + Math.floor(d.getTime() / 1000) + "\n毫秒 " + d.getTime();
	}

	function parsedate(s){
		var m = /^(\d{4})-(\d{2})-(\d{2})(?:[ T](\d{2}):(\d{2})(?::(\d{2}))?)?$/.exec(String(s).trim());
		if (!m) throw new Error("日期格式用 YYYY-MM-DD HH:mm:ss");
		var d = new Date(+m[1], +m[2] - 1, +m[3], +(m[4] || 0), +(m[5] || 0), +(m[6] || 0));
		if (d.getFullYear() !== +m[1] || d.getMonth() !== +m[2] - 1 || d.getDate() !== +m[3]) throw new Error("日期无效");
		return d;
	}

	SK.tsconv = tsconv;
	SK.parsedate = parsedate;
})();
(function(){
	var KINDS = [
		kind("len", "长度", "m", "ft", [
			["mm", "毫米"], ["cm", "厘米"], ["m", "米"], ["km", "千米"],
			["in", "英寸"], ["ft", "英尺"], ["yd", "码"], ["mi", "英里"]
		], { mm: 1, cm: 10, m: 1000, km: 1000000, "in": 25.4, ft: 304.8, yd: 914.4, mi: 1609344 }),
		kind("area", "面积", "m2", "mu", [
			["mm2", "平方毫米"], ["cm2", "平方厘米"], ["m2", "平方米"], ["ha", "公顷"],
			["mu", "亩"], ["km2", "平方千米"], ["ft2", "平方英尺"]
		], { mm2: 0.000001, cm2: 0.0001, m2: 1, ha: 10000, mu: 2000 / 3, km2: 1000000, ft2: 0.09290304 }),
		kind("vol", "体积", "L", "mL", [
			["mL", "毫升"], ["L", "升"], ["m3", "立方米"], ["gal", "美制加仑"]
		], { mL: 0.001, L: 1, m3: 1000, gal: 3.785411784 }),
		kind("mass", "重量", "kg", "jin", [
			["mg", "毫克"], ["g", "克"], ["kg", "千克"], ["t", "吨"],
			["jin", "市斤"], ["liang", "市两"], ["lb", "磅"], ["oz", "盎司"]
		], { mg: 0.001, g: 1, kg: 1000, t: 1000000, jin: 500, liang: 50, lb: 453.59237, oz: 28.349523125 }),
		kind("temp", "温度", "C", "F", [
			["C", "摄氏度"], ["F", "华氏度"], ["K", "开尔文"]
		], null),
		kind("time", "时间", "h", "min", [
			["ms", "毫秒"], ["s", "秒"], ["min", "分"], ["h", "小时"], ["d", "天"], ["week", "周"]
		], { ms: 0.001, s: 1, min: 60, h: 3600, d: 86400, week: 604800 }),
		kind("byte", "存储", "MB", "GB", [
			["B", "字节"], ["KB", "KB"], ["MB", "MB"], ["GB", "GB"], ["TB", "TB"]
		], { B: 1, KB: 1024, MB: 1048576, GB: 1073741824, TB: 1099511627776 }),
		kind("press", "压力", "kPa", "psi", [
			["Pa", "帕"], ["kPa", "千帕"], ["MPa", "兆帕"], ["bar", "巴"],
			["atm", "标准大气压"], ["psi", "磅力/平方英寸"]
		], { Pa: 1, kPa: 1000, MPa: 1000000, bar: 100000, atm: 101325, psi: 6894.757293168 }),
		kind("power", "功率", "kW", "hp", [
			["W", "瓦"], ["kW", "千瓦"], ["MW", "兆瓦"], ["hp", "公制马力"]
		], { W: 1, kW: 1000, MW: 1000000, hp: 735.49875 }),
		kind("px", "像素", "px", "rem", [
			["px", "px"], ["rem", "rem"]
		], null),
		kind("money", "货币", "CNY", "USD", [
			["CNY", "人民币元"], ["jiao", "角"], ["fen", "分"],
			["USD", "美元"], ["HKD", "港币"], ["MOP", "澳门元"], ["EUR", "欧元"], ["GBP", "英镑"],
			["JPY", "日元"], ["KRW", "韩元"], ["AUD", "澳元"], ["CAD", "加元"],
			["SGD", "新加坡元"], ["CHF", "瑞士法郎"], ["THB", "泰铢"], ["TWD", "新台币"]
		], {
			CNY: 1, jiao: 0.1, fen: 0.01,
			USD: 6.733, HKD: 0.858, MOP: 0.8326, EUR: 7.5323, GBP: 8.8872,
			JPY: 0.042571, KRW: 0.004996, AUD: 4.6746, CAD: 4.7204,
			SGD: 5.246, CHF: 8.0821, THB: 0.1994, TWD: 0.2099
		})
	];
	SK.regutil("u-unit", {
		title: "单位换算",
		go: "换算",
		body: function(){
			var names = [];
			var i;
			for (i = 0; i < KINDS.length; i++) names.push([KINDS[i].id, KINDS[i].name]);
			return SK.row2(
				SK.lab("类型", '<select id="box-kind">' + SK.opts(names, "len") + "</select>"),
				SK.lab("数值", SK.textin("box-in", "1")))
				+ SK.row2(
					SK.lab("原单位", '<select id="box-from"></select>'),
					SK.lab("新单位", '<select id="box-to"></select>'))
				+ '<div class="row" id="box-root-row" hidden>' + SK.lab("根字号", SK.textin("box-root", "16")) + "</div>";
		},
		open: function(){
			fillunits();
			SK.$("box-kind").onchange = fillunits;
		},
		run: function(){
			return convert();
		}
	});

	function kind(id, name, from, to, units, scale){
		return { id: id, name: name, from: from, to: to, units: units, scale: scale };
	}

	function findkind(id){
		var i;
		for (i = 0; i < KINDS.length; i++) if (KINDS[i].id === id) return KINDS[i];
		return null;
	}

	function fillunits(){
		var spec = findkind(SK.val("box-kind")) || KINDS[0];
		SK.$("box-from").innerHTML = SK.opts(spec.units, spec.from);
		SK.$("box-to").innerHTML = SK.opts(spec.units, spec.to);
		SK.$("box-root-row").hidden = spec.id !== "px";
	}

	function convert(){
		var spec = findkind(SK.val("box-kind"));
		if (!spec) throw new Error("请选择类型");
		var raw = String(SK.val("box-in")).trim();
		var n = Number(raw);
		if (raw === "" || !isFinite(n)) throw new Error("请输入数字");
		var from = SK.val("box-from");
		var to = SK.val("box-to");
		var out;
		var note = "";
		if (spec.id === "temp") out = tempto(n, from, to);
		else if (spec.id === "px") {
			var root = Number(String(SK.val("box-root")).trim());
			if (!(root > 0)) throw new Error("根字号要大于 0");
			out = pxto(n, from, to, root);
			if (from === "rem" || to === "rem") note = "按根字号 " + SK.trimnum(root) + "。";
		}
		else out = scaleto(n, from, to, spec.scale);
		if (spec.id === "money" && !cashonly(from, to)) note = "外币按中国银行 2026-10-10 中间价，不是实时牌价。";
		var line = SK.trimnum(out) + " " + unitname(spec.units, to);
		return note ? line + "\n" + note : line;
	}

	function scaleto(n, from, to, scale){
		var a = scale[from];
		var b = scale[to];
		if (!a || !b) throw new Error("请选择单位");
		return n * a / b;
	}

	function tempto(n, from, to){
		var c;
		if (from === "C") c = n;
		else if (from === "F") c = (n - 32) * 5 / 9;
		else if (from === "K") c = n - 273.15;
		else throw new Error("请选择单位");
		if (c < -273.15 - 1e-9) throw new Error("低于绝对零度");
		if (to === "C") return c;
		if (to === "F") return c * 9 / 5 + 32;
		if (to === "K") return c + 273.15;
		throw new Error("请选择单位");
	}

	function pxto(n, from, to, root){
		var px;
		if (from === "px") px = n;
		else if (from === "rem") px = n * root;
		else throw new Error("请选择单位");
		if (to === "px") return px;
		if (to === "rem") return px / root;
		throw new Error("请选择单位");
	}

	function cashonly(from, to){
		return iscash(from) && iscash(to);
	}

	function iscash(id){
		return id === "CNY" || id === "jiao" || id === "fen";
	}

	function unitname(units, id){
		var i;
		for (i = 0; i < units.length; i++) if (units[i][0] === id) return units[i][1];
		return id;
	}
})();
(function(){
	SK.regutil("u-uuid", {
		title: "UUID",
		go: "生成",
		body: function(){
			return SK.lab("个数", SK.numin("box-n", "1"));
		},
		run: function(){
			return uuidtext(SK.val("box-n"));
		}
	});
	function uuidtext(count){
		var c = parseInt(count, 10);
		if (!(c >= 1 && c <= 20)) throw new Error("个数要在 1 到 20");
		var lines = [];
		var n;
		for (n = 0; n < c; n++) lines.push(uuid1());
		return lines.join("\n");
	}

	function uuid1(){
		var b = new Uint8Array(16);
		if (!window.crypto || typeof crypto.getRandomValues !== "function") throw new Error("浏览器不能生成随机数");
		crypto.getRandomValues(b);
		b[6] = (b[6] & 15) | 64;
		b[8] = (b[8] & 63) | 128;
		var h = SK.hexbytes(b);
		return h.slice(0, 8) + "-" + h.slice(8, 12) + "-" + h.slice(12, 16) + "-" + h.slice(16, 20) + "-" + h.slice(20);
	}

	SK.uuidtext = uuidtext;
	SK.uuid1 = uuid1;
})();
(function(){
	SK.regutil("u-watch", {
		title: "秒表",
		go: "复位",
		body: function(){
			return '<div class="row"><button type="button" id="box-start">开始</button></div>';
		},
		run: function(){
			return watchreset();
		},
		open: function(){
			SK.$("box-start").onclick = watchtoggle;
			SK.$("box-out").value = watchreset();
		}
	});
	function fmtwatch(ms){
		if (ms < 0) ms = 0;
		var cs = Math.floor(ms / 10);
		var cent = cs % 100;
		var sec = Math.floor(cs / 100);
		return SK.pad2(Math.floor(sec / 60)) + ":" + SK.pad2(sec % 60) + "." + SK.pad2(cent);
	}

	function watchtoggle(){
		var btn = SK.$("box-start");
		if (SK.watchOn) {
			SK.stoptick();
			if (btn) btn.textContent = "开始";
			SK.$("box-out").value = fmtwatch(SK.watchAcc);
			return;
		}
		SK.watch0 = Date.now();
		SK.watchOn = true;
		if (btn) btn.textContent = "停止";
		SK.tick = setInterval(function(){
			SK.$("box-out").value = fmtwatch(SK.watchAcc + Date.now() - SK.watch0);
		}, 50);
	}

	function watchreset(){
		SK.stoptick();
		SK.watchAcc = 0;
		var btn = SK.$("box-start");
		if (btn) btn.textContent = "开始";
		return "00:00.00";
	}

	SK.fmtwatch = fmtwatch;
	SK.watchtoggle = watchtoggle;
	SK.watchreset = watchreset;
})();
(function(){
	SK.regutil("u-work", {
		title: "工作日",
		go: "计算",
		body: function(){
			return SK.row2(SK.lab("开始", '<input id="box-a" type="date">'), SK.lab("结束", '<input id="box-b" type="date">'));
		},
		run: function(){
			return workdays(SK.val("box-a"), SK.val("box-b"));
		},
		open: function(){
			var day = new Date();
			SK.$("box-b").value = SK.ymd(day);
			day.setDate(day.getDate() - 7);
			SK.$("box-a").value = SK.ymd(day);
		}
	});
	function workdays(a, b){
		if (!a || !b) throw new Error("请选择两个日期");
		var da = new Date(a + "T00:00:00");
		var db = new Date(b + "T00:00:00");
		if (isNaN(da.getTime()) || isNaN(db.getTime())) throw new Error("日期无效");
		if (db < da) throw new Error("开始不能晚于结束");
		var days = Math.round((db.getTime() - da.getTime()) / 86400000);
		if (days > 3660) throw new Error("最多 10 年");
		var n = 0;
		var d = da;
		while (d <= db) {
			var w = d.getDay();
			if (w !== 0 && w !== 6) n++;
			d.setDate(d.getDate() + 1);
		}
		return "工作日  " + n + " 天\n已跳过周六和周日，不含法定假日";
	}

	SK.workdays = workdays;
})();
(function(){
	SK.boot();
	window.sktools = { show: SK.show };
})();
