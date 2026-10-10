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
