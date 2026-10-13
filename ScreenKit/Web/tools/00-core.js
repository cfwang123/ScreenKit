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

window.F = {};
var F = window.F;
F.id = function(id){ return document.getElementById(id); };
F.q = function(sel, root){ return (root || document).querySelector(sel); };
F.qa = function(sel, root){ return (root || document).querySelectorAll(sel); };
F.el = function(tag){ return document.createElement(tag); };
F.on = function(el, type, fn, opt){
	if (arguments.length < 4) el.addEventListener(type, fn);
	else el.addEventListener(type, fn, opt);
};
F.pd = function(ev){ ev.preventDefault(); };
F.attr = function(el, name){ return el.getAttribute(name); };
F.seta = function(el, name, val){ el.setAttribute(name, val); };
F.unset = function(el, name){ el.removeAttribute(name); };
F.ctx = function(el, opt){
	if (arguments.length < 2) return el.getContext("2d");
	return el.getContext("2d", opt);
};
F.append = function(parent, child){ return parent.appendChild(child); };
F.parse = function(){ return JSON.parse.apply(JSON, arguments); };
F.json = function(){ return JSON.stringify.apply(JSON, arguments); };
F.lget = function(key){ return localStorage.getItem(key); };
F.lset = function(key, val){ localStorage.setItem(key, val); };
F.url = function(obj){ return URL.createObjectURL(obj); };
F.unurl = function(url){ URL.revokeObjectURL(url); };
F.cp = function(){ return String.fromCodePoint.apply(String, arguments); };
F.ch = function(){ return String.fromCharCode.apply(String, arguments); };
F.raf = function(fn){ return requestAnimationFrame(fn); };
F.rand = function(buf){ return crypto.getRandomValues(buf); };
F.rect = function(el){ return el.getBoundingClientRect(); };
F.into = function(el, opt){
	if (arguments.length < 2) el.scrollIntoView();
	else el.scrollIntoView(opt);
};
F.hdr = function(xhr, name, val){ xhr.setRequestHeader(name, val); };
F.clip = function(text){ return navigator.clipboard.writeText(text); };
F.tags = function(name, root){ return (root || document).getElementsByTagName(name); };

(function(){
	SK.utils = {};
	SK.regutil = function(name, spec){
		SK.utils[name] = spec;
	};
	var curcat = "all";
	var favs = [];
	var util = "";
	var langAt = 0;
	var webmap = null;
	var GO = {
		"生成": "web.go.gen",
		"计算": "web.go.calc",
		"转换": "web.go.convert",
		"显示": "web.go.show",
		"开始": "web.go.start",
		"测试": "web.go.test",
		"复位": "web.go.reset",
		"换算": "web.go.unit"
	};
	function boot(){
		hookhtml();
		var today = new Date();
		var month = today.getMonth() + 1;
		var day = today.getDate();
		F.id("cal-date").value = today.getFullYear()
			+ "-" + (month < 10 ? "0" : "") + month
			+ "-" + (day < 10 ? "0" : "") + day;
		F.on(document, "click", onclick);
		F.id("cal-go").onclick = SK.cal;
		F.id("tr-go").onclick = function(){ if (SK.trgo) SK.trgo(); };
		F.id("tr-swap").onclick = function(){ if (SK.trswap) SK.trswap(); };
		F.id("tr-copy").onclick = function(){ copytext(F.id("tr-out").value, "tr-msg"); };
		F.id("tr-paste").onclick = function(){ if (SK.trpaste) SK.trpaste(); };
		F.id("tr-speak-src").onclick = function(){ if (SK.trspeak) SK.trspeak("src"); };
		F.id("tr-speak-dst").onclick = function(){ if (SK.trspeak) SK.trspeak("dst"); };
		F.id("tr-src").onchange = function(){ if (SK.trdst) SK.trdst(true); };
		F.id("tr-in").onkeydown = function(ev){
			if (ev.key === "Enter" && (ev.ctrlKey || ev.metaKey)) {
				F.pd(ev);
				if (SK.trgo) SK.trgo();
			}
		};
		F.id("tx-copy").onclick = function(){ copytext(F.id("tx-out").value, "tx-msg"); };
		F.id("qr-make").onclick = SK.qrmake;
		F.id("qr-scan").onclick = SK.qrscan;
		F.id("ocr-pack").onchange = SK.fillocrmodels;
		F.id("ocr-file").onchange = SK.onocrfile;
		F.id("ocr-go").onclick = SK.ocrgo;
		F.on(document, "paste", SK.onocrpaste);
		F.id("ocr-copy").onclick = function(){ copytext(F.id("ocr-out").value, "ocr-msg"); };
		F.id("tts-eng").onchange = SK.fillttsmodels;
		F.id("tts-lang").onchange = SK.fillttsmodels;
		F.id("tts-gender").onchange = SK.fillttsmodels;
		F.id("tts-model").onchange = SK.fillttsvoices;
		F.id("tts-go").onclick = function(){ SK.ttsgo(false); };
		F.id("tts-wav").onclick = function(){ SK.ttsgo(true); };
		F.id("asr-eng").onchange = SK.fillasrmodels;
		F.id("asr-filego").onclick = SK.asrfile;
		F.id("asr-rec").onclick = SK.asrrec;
		F.id("asr-copy").onclick = function(){ copytext(F.id("asr-out").value, "asr-msg"); };
		F.id("tool-q").oninput = paintcats;
		F.id("box-go").onclick = utilrun;
		F.id("box-copy").onclick = function(){ copytext(F.id("box-out").value, "box-msg"); };
		filllang();
		loadfav();
		buildgroups();
		paintfav();
		paintstars();
		paintcats();
		F.on(window, "popstate", applyhash);
		var start = toolhash();
		if (start && start !== "home" && cardof(start)) show(start, true);
		applylang();
	}

	function onclick(ev){
		var el = ev.target;
		while (el && el !== document.body) {
			if (el.id === "tx-ops") return;
			var fav = el.getAttribute && F.attr(el, "data-fav");
			if (fav) {
				togglefav(fav);
				return;
			}
			var tool = el.getAttribute && F.attr(el, "data-tool");
			if (tool) {
				if (tool !== "home" && ev.ctrlKey) opentab(tool);
				else show(tool);
				return;
			}
			var cat = el.getAttribute && F.attr(el, "data-cat");
			if (cat && el.tagName === "BUTTON" && !F.attr(el, "data-tool")) {
				jumpcat(cat);
				return;
			}
			var op = el.getAttribute && F.attr(el, "data-op");
			if (op) {
				SK.textop(op);
				return;
			}
			el = el.parentNode;
		}
	}

	function opentab(name){
		window.open(location.pathname + location.search + "#" + name, "_blank");
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
		if (!id || id === "home") {
			if (SK.curtool !== "home") show("home", true);
			return;
		}
		if (!cardof(id) || SK.curtool === id) return;
		show(id, true);
	}

	function show(name, fromHist){
		stoptick();
		var prev = SK.curtool;
		SK.curtool = name;
		if (name.indexOf("u-") === 0) {
			openutil(name);
			name = "box";
		}
		var panels = F.qa(".panel");
		var i;
		for (i = 0; i < panels.length; i++)
			panels[i].className = panels[i].id === name ? "panel on" : "panel";
		F.id("back").hidden = name === "home";
		document.documentElement.classList.toggle("in-tool", SK.curtool !== "home");
		var favbtn = F.id("tool-fav");
		if (SK.curtool === "home") {
			favbtn.hidden = true;
			F.unset(favbtn, "data-fav");
		}
		else {
			favbtn.hidden = false;
			F.seta(favbtn, "data-fav", SK.curtool);
		}
		paintstars();
		if (name === "home") paintcats();
		else marktool(SK.curtool, name);
		if (name === "ocr") SK.loadoocr();
		if (name === "tts") SK.loadtts();
		if (name === "tr" && SK.loadtr) SK.loadtr();
		if (name === "asr") SK.loadasr();
		if (prev === "chat" && SK.curtool !== "chat" && SK.chatclose) SK.chatclose();
		if (SK.curtool === "chat" && SK.chatopen) SK.chatopen();
		settitle();
		if (!fromHist) pushhash(SK.curtool);
	}

	function settitle(){
		if (!SK.curtool || SK.curtool === "home") {
			document.title = t("web.title") || "web工具集";
			return;
		}
		var card = cardof(SK.curtool);
		var span = card ? F.q("span", card) : null;
		var toolName = span ? span.textContent : "";
		document.title = toolName || t("web.title") || "web工具集";
	}

	function langs(){
		if (window.L && L._lang && L._lang.length) return L._lang;
		return ["zh", "en", "ja", "ko", "es", "fr", "pt", "ru", "de"];
	}

	function t(key){
		var row = window.L && L[key];
		if (!row || !row.length) return "";
		var s = row[langAt];
		if (s) return s;
		return row[0] || "";
	}

	function webkey(text){
		if (!text || !window.L) return "";
		if (GO[text]) return GO[text];
		if (!webmap) {
			webmap = {};
			var codes = langs();
			var zi = 0;
			var i;
			for (i = 0; i < codes.length; i++) if (codes[i] === "zh") zi = i;
			for (var k in L) {
				if (!Object.prototype.hasOwnProperty.call(L, k)) continue;
				if (k.length < 4 || k.substring(0, 4) !== "web.") continue;
				var row = L[k];
				if (!row || !row[zi] || webmap[row[zi]]) continue;
				webmap[row[zi]] = k;
			}
		}
		return webmap[text] || "";
	}

	function khtml(text){
		var key = webkey(text);
		if (!key) return text;
		return '<span data-k="' + key + '">' + esc(t(key)) + "</span>";
	}

	function tr(text){
		if (!text) return "";
		var key = webkey(text);
		if (!key) return text;
		return t(key) || text;
	}

	function hookhtml(){
		if (hookhtml.done) return;
		hookhtml.done = true;
		var desc = Object.getOwnPropertyDescriptor(Element.prototype, "innerHTML");
		if (!desc || !desc.set) return;
		Object.defineProperty(Element.prototype, "innerHTML", {
			configurable: true,
			enumerable: desc.enumerable,
			get: desc.get,
			set: function(v){
				desc.set.call(this, v);
				try { markhtml(this); } catch (e) {}
			}
		});
	}

	function skipped(el){
		while (el && el.nodeType === 1) {
			if (el.id === "chat-log" || el.id === "chat-peers" || el.id === "chat-offers") return true;
			if (el.classList && (el.classList.contains("num-v") || el.classList.contains("uprev-ch"))) return true;
			var tag = el.tagName;
			if (tag === "TEXTAREA" || tag === "SCRIPT" || tag === "STYLE" || tag === "INPUT") return true;
			el = el.parentNode;
		}
		return false;
	}

	function markhtml(root){
		if (!root || root.nodeType !== 1 || skipped(root)) return;
		var list = [];
		var walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, null);
		var n;
		while ((n = walker.nextNode())) list.push(n);
		var i;
		for (i = 0; i < list.length; i++) marktext(list[i]);
		markattr(root);
		var els = root.querySelectorAll("[placeholder], [title], [alt]");
		for (i = 0; i < els.length; i++) markattr(els[i]);
	}

	function marktext(node){
		var parent = node.parentNode;
		if (!parent || parent.nodeType !== 1 || skipped(parent)) return;
		if (parent.hasAttribute("data-k") || parent.hasAttribute("data-kz")) return;
		var raw = node.nodeValue;
		if (!raw) return;
		var s = raw.replace(/^\s+|\s+$/g, "");
		if (!s) return;
		var key = webkey(s);
		if (!key) return;
		var shown = t(key) || s;
		if (parent.childNodes.length === 1 && parent.tagName !== "BODY" && parent.tagName !== "HTML") {
			F.seta(parent, "data-k", key);
			parent.textContent = shown;
			return;
		}
		var span = F.el("span");
		F.seta(span, "data-k", key);
		span.textContent = shown;
		var lead = raw.match(/^\s*/)[0];
		var tail = raw.match(/\s*$/)[0];
		if (lead) parent.insertBefore(document.createTextNode(lead), node);
		parent.insertBefore(span, node);
		if (tail) parent.insertBefore(document.createTextNode(tail), node);
		parent.removeChild(node);
	}

	function markattr(el){
		if (!el || el.nodeType !== 1 || skipped(el)) return;
		var ph = el.getAttribute("placeholder");
		var key;
		if (ph && !el.getAttribute("data-kph")) {
			key = webkey(ph);
			if (key) {
				F.seta(el, "data-kph", key);
				F.seta(el, "placeholder", t(key) || ph);
			}
		}
		var title = el.getAttribute("title");
		if (title && !el.getAttribute("data-ktip") && !el.getAttribute("data-kt")) {
			key = webkey(title);
			if (key) {
				F.seta(el, "data-ktip", key);
				F.seta(el, "title", t(key) || title);
			}
		}
		var alt = el.getAttribute("alt");
		if (alt && !el.getAttribute("data-ka")) {
			key = webkey(alt);
			if (key) {
				F.seta(el, "data-ka", key);
				F.seta(el, "alt", t(key) || alt);
			}
		}
	}

	function filllang(){
		var sel = F.id("lang");
		if (!sel) return;
		var codes = langs();
		var names = (window.L && L._name) || [];
		while (sel.firstChild) sel.removeChild(sel.firstChild);
		var i;
		for (i = 0; i < codes.length; i++) {
			var op = F.el("option");
			op.value = codes[i];
			op.textContent = names[i] || codes[i];
			F.append(sel, op);
		}
		var use = String(window.SKL || "").toLowerCase();
		langAt = 0;
		for (i = 0; i < codes.length; i++) {
			if (use === codes[i]) { langAt = i; break; }
		}
		sel.value = codes[langAt] || codes[0] || "";
		sel.onchange = onlang;
	}

	function onlang(){
		var code = F.id("lang").value;
		var codes = langs();
		var i;
		langAt = 0;
		for (i = 0; i < codes.length; i++) if (codes[i] === code) langAt = i;
		applylang();
	}

	function applylang(){
		var nodes = F.qa("[data-k]");
		var i, s;
		for (i = 0; i < nodes.length; i++) {
			s = t(F.attr(nodes[i], "data-k"));
			if (s) nodes[i].textContent = s;
		}
		nodes = F.qa("[data-kph]");
		for (i = 0; i < nodes.length; i++) {
			s = t(F.attr(nodes[i], "data-kph"));
			if (s) nodes[i].placeholder = s;
		}
		nodes = F.qa("[data-kt]");
		for (i = 0; i < nodes.length; i++) {
			s = t(F.attr(nodes[i], "data-kt"));
			if (s) F.seta(nodes[i], "data-title", s);
		}
		nodes = F.qa("[data-ktip]");
		for (i = 0; i < nodes.length; i++) {
			s = t(F.attr(nodes[i], "data-ktip"));
			if (s) nodes[i].title = s;
		}
		nodes = F.qa("[data-ka]");
		for (i = 0; i < nodes.length; i++) {
			s = t(F.attr(nodes[i], "data-ka"));
			if (s) F.seta(nodes[i], "alt", s);
		}
		nodes = F.qa("[data-kz]");
		for (i = 0; i < nodes.length; i++) {
			s = tr(F.attr(nodes[i], "data-kz"));
			if (s) nodes[i].textContent = s + (F.attr(nodes[i], "data-kx") || "");
		}
		var code = langs()[langAt] || "zh";
		document.documentElement.lang = code === "zh" ? "zh-CN" : code;
		if (SK.curtool && SK.curtool !== "home") {
			var panel = SK.curtool.indexOf("u-") === 0 ? "box" : SK.curtool;
			marktool(SK.curtool, panel);
		}
		settitle();
		markhtml(document.body);
		if (SK.fillttslang && F.id("tts-lang")) SK.fillttslang();
		if (SK.filltrlang) SK.filltrlang();
		paintstars();
		if (!SK.curtool || SK.curtool === "home") paintcats();
	}

	function marktool(id, panelId){
		var card = cardof(id);
		if (!card) return;
		var cat = F.attr(card, "data-cat") || "";
		var catName = cattitle(cat);
		var span = F.q("span", card);
		var toolName = span ? span.textContent : "";
		var h1 = F.q("#" + panelId + " h1");
		if (h1 && catName && toolName) h1.textContent = catName + " - " + toolName;
	}

	function cattitle(cat){
		var btn = F.q('#cats button[data-cat="' + cat + '"]');
		if (!btn) return "";
		return F.attr(btn, "data-title") || "";
	}

	function buildgroups(){
		var host = F.id("cards");
		var btns = F.qa("button", host);
		var nav = F.qa("#cats button");
		var pending = [];
		var map = {};
		var i;
		for (i = 0; i < btns.length; i++) pending.push(btns[i]);
		for (i = 0; i < nav.length; i++) {
			var cat = F.attr(nav[i], "data-cat");
			if (cat === "all") continue;
			var sec = F.el("section");
			sec.className = "catsec";
			sec.id = "sec-" + cat;
			F.seta(sec, "data-cat", cat);
			var h = F.el("h2");
			var icon = F.q("i", nav[i]);
			if (icon) F.append(h, icon.cloneNode(true));
			var catKey = "web.cat." + cat;
			var catLabel = t(catKey) || F.attr(nav[i], "data-title") || "";
			var catSpan = F.el("span");
			F.seta(catSpan, "data-k", catKey);
			catSpan.textContent = catLabel;
			F.append(h, catSpan);
			var grid = F.el("div");
			grid.className = "cards";
			F.append(sec, h);
			F.append(sec, grid);
			if (cat === "fav") {
				var empty = F.el("p");
				empty.className = "none";
				empty.id = "fav-none";
				empty.hidden = true;
				F.seta(empty, "data-k", "web.fav.none");
				empty.textContent = t("web.fav.none") || "还没有收藏。点卡片右侧的星可以加进来。";
				F.append(sec, empty);
			}
			map[cat] = grid;
			F.append(host, sec);
		}
		for (i = 0; i < pending.length; i++) {
			var into = map[F.attr(pending[i], "data-cat") || ""];
			if (into) F.append(into, pending[i]);
		}
		host.className = "groups";
		host.id = "groups";
	}

	function paintfav(){
		var grid = F.q("#sec-fav .cards");
		if (!grid) return;
		while (grid.firstChild) grid.removeChild(grid.firstChild);
		var i;
		for (i = 0; i < favs.length; i++) {
			var src = cardof(favs[i]);
			if (!src) continue;
			var copy = src.cloneNode(true);
			F.seta(copy, "data-clone", "1");
			copy.hidden = false;
			F.append(grid, copy);
		}
		var empty = F.id("fav-none");
		if (empty) empty.hidden = favs.length !== 0;
	}

	function paintnav(){
		var cats = F.qa("#cats button");
		var i;
		for (i = 0; i < cats.length; i++)
			cats[i].className = F.attr(cats[i], "data-cat") === curcat ? "on" : "";
	}

	function jumpcat(cat){
		curcat = cat;
		if (SK.curtool !== "home") show("home");
		else paintcats();
		var home = F.id("home");
		if (home) home.scrollTop = 0;
	}

	function paintcats(){
		var q = (F.id("tool-q").value || "").replace(/^\s+|\s+$/g, "").toLowerCase();
		var cards = F.qa(".catsec .cards button");
		var n = 0;
		var i;
		for (i = 0; i < cards.length; i++) {
			var sec = cards[i].parentNode ? cards[i].parentNode.parentNode : null;
			var cat = sec ? (F.attr(sec, "data-cat") || "") : "";
			var incat = curcat === "all" || cat === curcat;
			var name = (F.attr(cards[i], "data-name") || "").toLowerCase();
			var vis = incat && (!q || name.indexOf(q) >= 0);
			cards[i].hidden = !vis;
			if (vis) n++;
		}
		var secs = F.qa(".catsec");
		for (i = 0; i < secs.length; i++) {
			var cat = F.attr(secs[i], "data-cat");
			if (curcat !== "all" && cat !== curcat) {
				secs[i].hidden = true;
				continue;
			}
			var grid = F.q(".cards", secs[i]);
			var buttons = grid ? F.qa("button", grid) : [];
			var visn = 0;
			var j;
			for (j = 0; j < buttons.length; j++) if (!buttons[j].hidden) visn++;
			if (cat === "fav" && !q) {
				secs[i].hidden = false;
				var empty = F.id("fav-none");
				if (empty) empty.hidden = visn !== 0;
			}
			else secs[i].hidden = visn === 0;
		}
		F.id("tool-none").textContent = t("web.find.none") || "没有匹配的工具";
		F.id("tool-none").hidden = n !== 0;
		paintnav();
	}

	function loadfav(){
		favs = [];
		var raw = "";
		try { raw = F.lget("sk-tool-fav") || ""; }
		catch (e) { return; }
		var list;
		try { list = F.parse(raw || "[]"); }
		catch (e) { return; }
		if (!list || typeof list.length !== "number") return;
		var i, seen = {};
		for (i = 0; i < list.length; i++) {
			var id = String(list[i] || "");
			if (!id || seen[id] || !cardof(id)) continue;
			seen[id] = 1;
			favs.push(id);
		}
		if (favchanged(list)) savefav();
	}

	function favchanged(list){
		if (list.length !== favs.length) return true;
		var i;
		for (i = 0; i < list.length; i++) if (String(list[i] || "") !== favs[i]) return true;
		return false;
	}

	function savefav(){
		try { F.lset("sk-tool-fav", F.json(favs)); }
		catch (e) {}
	}

	function cardof(id){
		var cards = F.qa(".cards button");
		var i;
		for (i = 0; i < cards.length; i++) {
			if (F.attr(cards[i], "data-clone")) continue;
			if (F.attr(cards[i], "data-tool") === id) return cards[i];
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
		savefav();
		paintfav();
		paintstars();
		if (SK.curtool === "home") paintcats();
	}

	function paintstars(){
		var nodes = F.qa(".cards .star");
		var i;
		for (i = 0; i < nodes.length; i++) {
			var on = isfav(F.attr(nodes[i], "data-fav"));
			nodes[i].className = on ? "star on fa-solid fa-star" : "star fa-solid fa-star";
			var tipKey = on ? "web.fav.tip.off" : "web.fav.tip";
			F.seta(nodes[i], "data-ktip", tipKey);
			nodes[i].title = t(tipKey) || (on ? "取消收藏" : "收藏");
		}
		var btn = F.id("tool-fav");
		if (!btn) return;
		var picked = F.attr(btn, "data-fav") || "";
		var lit = picked && isfav(picked);
		btn.className = lit ? "back on" : "back";
		var favKey = lit ? "web.fav.on" : "web.fav";
		F.seta(btn, "data-ktip", favKey);
		btn.title = t(favKey) || (lit ? "已收藏" : "收藏");
	}

	function msg(id, text, bad){
		var el = F.id(id);
		if (!el) return;
		var key = webkey(text);
		if (key) {
			F.seta(el, "data-k", key);
			el.textContent = t(key);
		}
		else {
			F.unset(el, "data-k");
			el.textContent = text || "";
		}
		el.className = bad ? "msg bad" : "msg";
	}

	function post(url, body, done, mid, raw){
		msg(mid, "");
		var xhr = new XMLHttpRequest();
		xhr.open("POST", url, true);
		F.hdr(xhr, "Content-Type", "application/json;charset=utf-8");
		xhr.onload = function(){
			var jo;
			try { jo = F.parse(xhr.responseText); }
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
		xhr.send(F.json(body));
	}

	var wavclip = null;
	var wavurl = "";

	function stopwav(){
		if (wavclip) {
			wavclip.pause();
			wavclip.src = "";
			wavclip = null;
		}
		if (wavurl) {
			F.unurl(wavurl);
			wavurl = "";
		}
	}

	function playwav(b64, mid){
		var bin, bytes, i, blob, url, audio, played;
		if (!b64) {
			msg(mid, "没有音频", true);
			return;
		}
		bin = atob(b64);
		bytes = new Uint8Array(bin.length);
		for (i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i) & 255;
		blob = new Blob([bytes], { type: "audio/wav" });
		url = F.url(blob);
		stopwav();
		wavurl = url;
		audio = new Audio(url);
		wavclip = audio;
		audio.onended = function(){
			if (wavclip === audio) stopwav();
		};
		played = audio.play();
		if (played && played.then) played.then(function(){}, function(){
			msg(mid, "无法播放", true);
			if (wavclip === audio) stopwav();
		});
	}

	var chartFont = null;
	var chartFonts = null;
	var chartAt = 0;
	var chartWait = [];
	var chartPend = false;
	var chartMs = 10 * 60 * 1000;

	function chartfresh(){
		return chartFonts && chartAt && Date.now() - chartAt < chartMs;
	}

	function chartfont(id){
		var box = F.id(id);
		if (!box) return;
		if (chartFont == null) {
			chartFont = "";
			try { chartFont = F.lget("sk-chart-font") || ""; } catch (e) {}
		}
		box.onchange = function(){
			chartFont = box.value || "";
			try { F.lset("sk-chart-font", chartFont); } catch (e) {}
			applychart();
			if (SK.onchartfont) SK.onchartfont();
		};
		if (chartfresh()) {
			paintchart(box);
			return;
		}
		if (chartFonts) paintchart(box);
		chartWait.push(box);
		if (chartPend) return;
		chartPend = true;
		get("/api/fonts", function(data){
			var i;
			chartPend = false;
			chartFonts = [];
			chartAt = 0;
			if (data && data.length) {
				for (i = 0; i < data.length; i++) chartFonts.push(String(data[i]));
				chartAt = Date.now();
			}
			var nodes = chartWait;
			chartWait = [];
			for (i = 0; i < nodes.length; i++) paintchart(nodes[i]);
		}, "", function(){
			chartPend = false;
			if (!chartFonts) chartFonts = [];
			var nodes = chartWait;
			var i;
			chartWait = [];
			for (i = 0; i < nodes.length; i++) paintchart(nodes[i]);
		});
	}

	function paintchart(box){
		var html = '<option value="">默认</option>';
		var i;
		var name;
		for (i = 0; i < chartFonts.length; i++) {
			name = chartFonts[i];
			html += '<option value="' + htmlenc(name) + '" style="font-family:' + htmlenc(chartfaceq(name)) + ',sans-serif">'
				+ htmlenc(name) + "</option>";
		}
		box.innerHTML = html;
		if (chartFont) box.value = chartFont;
		if (box.value !== chartFont) {
			chartFont = "";
			try { F.lset("sk-chart-font", ""); } catch (e) {}
		}
		applychart();
		if (SK.onchartfont) SK.onchartfont();
	}

	function applychart(){
		var root = document.documentElement;
		if (!chartFont) {
			root.style.removeProperty("--ufont");
			F.unset(root, "data-ufont");
			return;
		}
		root.style.setProperty("--ufont", chartfaceq(chartFont));
		F.seta(root, "data-ufont", "1");
	}

	function chartface(){
		return chartFont ? chartfaceq(chartFont) : "";
	}

	function chartfaceq(name){
		return '"' + String(name).replace(/\\/g, "\\\\").replace(/"/g, '\\"') + '"';
	}

	function get(url, done, mid, fail){
		msg(mid, "");
		var xhr = new XMLHttpRequest();
		xhr.open("GET", url, true);
		xhr.onload = function(){
			var jo;
			try { jo = F.parse(xhr.responseText); }
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
			var op = F.el("option");
			op.value = rows[i].value;
			op.textContent = rows[i].label;
			F.append(sel, op);
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
			F.clip(text).then(function(){
				msg(mid, "已复制");
			}, function(){
				fallback(text, mid);
			});
			return;
		}
		fallback(text, mid);
	}

	function fallback(text, mid){
		var ta = F.el("textarea");
		ta.value = text;
		F.append(document.body, ta);
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
		return "<label>" + khtml(title) + inner + "</label>";
	}

	function row2(a, b){
		return '<div class="row">' + a + b + "</div>";
	}

	function ta(id, rows, ph){
		var key = webkey(ph || "");
		var shown = key ? t(key) : ph;
		var attr = key ? ' data-kph="' + key + '"' : "";
		return '<textarea id="' + id + '" rows="' + rows + '" placeholder="' + shown + '"' + attr + "></textarea>";
	}

	function textin(id, value, ph){
		var key = webkey(ph || "");
		var shown = key ? t(key) : (ph || "");
		var attr = key ? ' data-kph="' + key + '"' : "";
		return '<input id="' + id + '" type="text" value="' + value + '" placeholder="' + shown + '"' + attr + ">";
	}

	function numin(id, value){
		return '<input id="' + id + '" type="number" value="' + value + '">';
	}

	function check(id, title, on){
		return '<label class="check"><input id="' + id + '" type="checkbox"' + (on ? " checked" : "") + "> " + khtml(title) + "</label>";
	}

	function radio(name, id, title, on){
		return '<label class="check"><input id="' + id + '" name="' + name + '" type="radio" value="' + title + '"' + (on ? " checked" : "") + "> " + khtml(title) + "</label>";
	}

	function opts(list, sel){
		var html = "";
		var i;
		var label;
		var key;
		for (i = 0; i < list.length; i++) {
			label = list[i][1];
			key = webkey(label);
			html += '<option value="' + list[i][0] + '"' + (list[i][0] === sel ? " selected" : "")
				+ (key ? ' data-k="' + key + '"' : "") + ">" + (key ? esc(t(key)) : label) + "</option>";
		}
		return html;
	}

	function val(id){
		var el = F.id(id);
		return el && el.value != null ? String(el.value) : "";
	}

	function onbox(id){
		var el = F.id(id);
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
			F.rand(buf);
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
		var yuv = rgb2yuv(rgb[0], rgb[1], rgb[2]);
		return "HEX  #" + hex2(rgb[0]) + hex2(rgb[1]) + hex2(rgb[2])
			+ "\nRGB  " + rgb[0] + ", " + rgb[1] + ", " + rgb[2]
			+ "\nHSL  " + hsl[0] + ", " + hsl[1] + "%, " + hsl[2] + "%"
			+ "\nHSV  " + hsv[0] + ", " + hsv[1] + "%, " + hsv[2] + "%"
			+ "\nCMYK  " + cmyk[0] + "%, " + cmyk[1] + "%, " + cmyk[2] + "%, " + cmyk[3] + "%"
			+ "\nYUV  " + yuv[0] + ", " + yuv[1] + ", " + yuv[2]
			+ "\nCMYK 用的是简单公式，不是印刷配置文件。"
			+ "\nYUV 是 BT.601 全范围，分量 0–255。";
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

	function rgb2yuv(r, g, b){
		var y = 0.299 * r + 0.587 * g + 0.114 * b;
		var u = -0.168736 * r - 0.331264 * g + 0.5 * b + 128;
		var v = 0.5 * r - 0.418688 * g - 0.081312 * b + 128;
		return [yuv8(y), yuv8(u), yuv8(v)];
	}

	function yuv8(n){
		n = Math.round(n);
		if (n < 0) return 0;
		if (n > 255) return 255;
		return n;
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
		if (SK.icoUrl) F.unurl(SK.icoUrl);
		SK.icoUrl = F.url(file);
		var img = F.id("ico-img");
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
		F.id("box-title").textContent = spec.title;
		var gokey = webkey(spec.go);
		if (gokey) {
			F.seta(F.id("box-go"), "data-k", gokey);
			F.id("box-go").textContent = t(gokey);
		}
		else {
			F.unset(F.id("box-go"), "data-k");
			F.id("box-go").textContent = spec.go;
		}
		F.id("box-body").innerHTML = spec.body();
		F.id("box-out").value = "";
		F.id("box-go").parentElement.style.display = "";
		F.id("box-go").style.display = "";
		F.id("box-copy").style.display = "";
		F.id("box-out").parentElement.style.display = "";
		if (SK.showreout) SK.showreout(false);
		if (SK.showrcolor) SK.showrcolor(false);
		msg("box-msg", "");
		if (spec.hideGo) F.id("box-go").parentElement.style.display = "none";
		if (spec.noGo) F.id("box-go").style.display = "none";
		if (spec.hideCopy) F.id("box-copy").style.display = "none";
		if (spec.hideOut) F.id("box-out").parentElement.style.display = "none";
		if (spec.open) spec.open();
		if (spec.live !== false && textcard(name)) bindboxlive();
	}

	function textcard(name){
		var card = cardof(name);
		return !!(card && F.attr(card, "data-cat") === "text");
	}

	function bindboxlive(){
		var body = F.id("box-body");
		if (!body) return;
		body.oninput = function(){ utilrun(); };
		body.onchange = function(ev){
			var t = ev.target;
			if (!t) return;
			if (t.tagName === "SELECT" || t.type === "checkbox" || t.type === "radio") utilrun();
		};
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
			var out = spec.run();
			out = out == null ? "" : String(out);
			var outKey = webkey(out);
			F.id("box-out").value = outKey ? (t(outKey) || out) : out;
			msg("box-msg", SK.reNote || "完成");
		}
		catch (ex) {
			F.id("box-out").value = "";
			if (SK.showreout) SK.showreout(false);
			if (SK.showrcolor) SK.showrcolor(false);
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
	SK.paintcats = paintcats;
	SK.loadfav = loadfav;
	SK.cardof = cardof;
	SK.favindex = favindex;
	SK.isfav = isfav;
	SK.togglefav = togglefav;
	SK.paintstars = paintstars;
	SK.$ = F.id;
	SK.msg = msg;
	SK.post = post;
	SK.playwav = playwav;
	SK.stopwav = stopwav;
	SK.get = get;
	SK.chartfont = chartfont;
	SK.chartface = chartface;
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
	SK.rgb2yuv = rgb2yuv;
	SK.rgb2cmyk = rgb2cmyk;
	SK.hsl2rgb = hsl2rgb;
	SK.cmyk2rgb = cmyk2rgb;
	SK.stoptick = stoptick;
	SK.seticopic = seticopic;
	SK.esc = esc;
	SK.tr = tr;
	SK.markhtml = markhtml;
	SK.openutil = openutil;
	SK.utilrun = utilrun;
	SK.t = t;
})();
