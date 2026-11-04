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
F.sink = function(node){
	if (!node || typeof node.setSinkId !== "function") return;
	var p;
	try { p = node.setSinkId("default"); }
	catch (e) { return; }
	if (p && p.then) p.then(function(){}, function(){
		try { node.setSinkId(""); } catch (e2) {}
	});
};
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
		SK.doms.calDate.value = today.getFullYear()
			+ "-" + (month < 10 ? "0" : "") + month
			+ "-" + (day < 10 ? "0" : "") + day;
		F.on(document, "click", onclick);
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
				F.pd(ev);
				togglefav(fav);
				return;
			}
			var tool = el.getAttribute && F.attr(el, "data-tool");
			if (tool) {
				if (tool !== "home" && ev.ctrlKey) opentab(tool);
				else show(tool);
				return;
			}
			// 独立页面：data-popup 的卡片用弹窗打开（只有标题栏，没有标签条和地址栏按钮）
			var pop = el.getAttribute && F.attr(el, "data-popup");
			if (pop) {
				F.pd(ev);
				openpopup(F.attr(el, "href") || ("/" + pop));
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

	// 独立页面弹窗：只留标题栏，隐藏地址栏和工具栏，看起来就像个应用窗口
	function openpopup(href){
		var w = Math.min(1280, Math.max(760, Math.round(screen.availWidth * 0.72)));
		var h = Math.min(900, Math.max(560, Math.round(screen.availHeight * 0.8)));
		var left = Math.max(0, Math.round((screen.availWidth - w) / 2));
		var top = Math.max(0, Math.round((screen.availHeight - h) / 2));
		var feat = "popup=yes,width=" + w + ",height=" + h + ",left=" + left + ",top=" + top
			+ ",menubar=no,toolbar=no,location=no,status=no,scrollbars=yes,resizable=yes";
		var win = window.open(href, "_blank", feat);
		if (win) {
			try { win.focus(); } catch (e) { }
			return win;
		}
		// 被拦了就退回普通窗口
		return window.open(href, "_blank");
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

	// 离开工具时调用 spec.dispose，用来停声音、关设备。没有资源的工具不用写。
	function disposeutil(name){
		if (!name || name === "home") return;
		var spec = SK.utils[name];
		if (!spec || !spec.dispose) return;
		try { spec.dispose(); }
		catch (ex) {}
	}

	function show(name, fromHist){
		var href = extpage(name);
		if (href) {
			disposeutil(SK.curtool);
			location.href = href;
			return;
		}
		if (SK.curtool !== name) disposeutil(SK.curtool);
		stoptick();
		SK.curtool = name;
		if (name.indexOf("u-") === 0) {
			openutil(name);
			name = "box";
		}
		var panels = F.qa(".panel");
		var i;
		for (i = 0; i < panels.length; i++)
			panels[i].className = panels[i].id === name ? "panel on" : "panel";
		SK.doms.back.hidden = name === "home";
		document.documentElement.classList.toggle("in-tool", SK.curtool !== "home");
		var favbtn = SK.doms.toolFav;
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
		// 缺译文时回退英文，再回退中文：小语种没补的键不该突然变中文。
		var i = langs().indexOf("en");
		if (i >= 0 && row[i]) return row[i];
		return row[0] || "";
	}

	// 键查不到就用兜底文案。多个工具原来各写一份 tx，统一到这里。
	function tx(key, fb){
		return t(key) || fb;
	}

	// 界面当前语言代码，读左上角的下拉。
	function langcode(){
		var sel = SK.doms.lang;
		return sel && sel.value ? sel.value : "zh";
	}

	// 改写 label 元素的第一个文本节点，保留里面的 input/select。
	// 换过一次语言后 markhtml 会把这段文本包成带 data-k 的 span，
	// 所以有 span 就改 span 的文字，否则会多出一层重复的标签。
	function setlab(id, key, fb){
		var el = domof(id);
		if (!el) return;
		var text = tx(key, fb);
		var first = el.firstChild;
		if (first && first.nodeType === 1 && first.hasAttribute("data-k")) {
			F.seta(first, "data-k", key);
			first.textContent = text;
			return;
		}
		if (first && first.nodeType === 3) first.nodeValue = text;
		else el.insertBefore(document.createTextNode(text), first);
	}

	// 给下拉里指定 value 的 option 换文案。
	function setopt(selId, value, key, fb){
		var sel = domof(selId);
		if (!sel) return;
		var i;
		for (i = 0; i < sel.options.length; i++) {
			if (sel.options[i].value === value) sel.options[i].textContent = tx(key, fb);
		}
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

	// 速查表里的数据（命令说明、备注、示例注释）是写死的中文，按整串查表翻成当前界面语言。
	// zz-l10n-00keys.js 先注册一份中文键表，各语言的 zz-l10n-<语言>.js 只按同样顺序存译文，
	// 省掉每份词典都带一遍中文键。查不到就显示原文。
	// 注意别占用 SK.tr：那是按 web.* 键反查界面文案的老函数，别的工具在用。
	var TRD = {};
	// 中文原文键表，按序号对应各语言译文。
	var TRK = null;
	SK.regkeys = function(list){
		TRK = list || null;
	};
	SK.regtrv = function(code, vals){
		if (!code || !TRK || !vals) return;
		var d = TRD[code] || (TRD[code] = {});
		var i;
		for (i = 0; i < TRK.length && i < vals.length; i++) {
			var v = vals[i];
			if (v == null || v === "") continue;
			d[TRK[i]] = v;
		}
	};
	SK.regtr = function(code, map){
		if (!code || !map) return;
		var d = TRD[code] || (TRD[code] = {});
		for (var k in map) {
			if (Object.prototype.hasOwnProperty.call(map, k)) d[k] = map[k];
		}
	};

	function trdict(){
		var code = langs()[langAt] || "zh";
		if (TRD[code]) return TRD[code];
		// 只做了英文词典：其它语言没补就显示英文，中文界面仍用原文。
		if (code !== "zh") return TRD.en || null;
		return null;
	}

	SK.td = function(s){
		var text = String(s == null ? "" : s);
		var d = trdict();
		if (!text || !d) return text;
		var val = d[text];
		return val == null || val === "" ? text : val;
	};

	// 示例是多行文本，注释行（# / rem）要按去掉前缀后的注释内容查表。
	// 词典里的键按不缩进的形式存的，所以先去掉首尾空白再查，查到再把缩进补回去。
	SK.tdline = function(line){
		var text = String(line == null ? "" : line);
		var d = trdict();
		if (!text || !d) return text;
		var pad = text.match(/^\s*/)[0];
		var tail = text.match(/\s*$/)[0];
		var body = text.substring(pad.length, text.length - tail.length);
		if (d[body] != null) return pad + d[body] + tail;
		var heads = ["# ", "rem ", "REM ", "#", "// "];
		var i, head, rest;
		for (i = 0; i < heads.length; i++) {
			head = heads[i];
			if (body.indexOf(head) !== 0) continue;
			rest = body.substring(head.length);
			if (d[rest] != null) return pad + head + d[rest] + tail;
		}
		return text;
	};

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
			// 语言菜单用各语言自己的名字。「中文」和网页文案相同，扫到就会被译走。
			if (el.id === "lang") return true;
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
		// 输入框自己的 placeholder / title 也要换，不能因为 skipped 把 INPUT 自己算进去；
		// 只看祖先里有没有不该动的地方（聊天记录等）。
		if (!el || el.nodeType !== 1 || (el.parentNode && skipped(el.parentNode))) return;
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
		var sel = SK.doms.lang;
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
	}

	function onlang(){
		var code = SK.doms.lang.value;
		var codes = langs();
		var i;
		langAt = 0;
		for (i = 0; i < codes.length; i++) if (codes[i] === code) langAt = i;
		applylang();
		// 表格是渲染时查表翻的，换语言要重画一遍；只重画当前打开的工具。
		// relang 有些模块直接指向 open(t)，要把当前页传进去，否则它拿不到 doms。
		if (util && SK.curtool === util && SK.utils[util] && SK.utils[util].relang) SK.utils[util].relang(SK.curpage);
		// 重建 body 的工具会把标题写回 data 里的原文，这里按卡片再刷一次。
		if (SK.curtool && SK.curtool !== "home")
			marktool(SK.curtool, SK.curtool.indexOf("u-") === 0 ? "box" : SK.curtool);
	}

	// 自绘表格的工具换语言时整个重建 body（占位、表头、提示都在 body 里），并尽量保留搜索词。
	SK.reopenutil = function(){
		if (!util || SK.curtool !== util) return;
		var old = F.q(".uquery input", SK.doms.boxBody);
		var val = old ? old.value : "";
		openutil(util);
		var box = F.q(".uquery input", SK.doms.boxBody);
		if (box && val) {
			box.value = val;
			box.dispatchEvent(new Event("input"));
		}
	};

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
		// 先把页面文案换成当前语言，marktool / settitle 再去读卡片上的名字，
		// 否则标题会拿到还没换过的中文。
		markhtml(document.body);
		if (SK.curtool && SK.curtool !== "home") {
			var panel = SK.curtool.indexOf("u-") === 0 ? "box" : SK.curtool;
			marktool(SK.curtool, panel);
		}
		settitle();
		if (SK.fillttslang && SK.doms.ttsLang) SK.fillttslang();
		if (SK.filltrlang) SK.filltrlang();
		// 语言下拉、语音语言这些是脚本临时填的，上一步扫不到，补扫一次。
		markhtml(document.body);
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
		var host = SK.doms.cards;
		var btns = F.qa("button, a", host);
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
			SK.doms.favNone = empty;
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
		var empty = SK.doms.favNone;
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
		var home = SK.doms.home;
		if (home) home.scrollTop = 0;
	}

	function paintcats(){
		var q = (SK.doms.toolQ.value || "").replace(/^\s+|\s+$/g, "").toLowerCase();
		var cards = F.qa(".catsec .cards button, .catsec .cards a");
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
			var buttons = grid ? F.qa("button, a", grid) : [];
			var visn = 0;
			var j;
			for (j = 0; j < buttons.length; j++) if (!buttons[j].hidden) visn++;
			if (cat === "fav" && !q) {
				secs[i].hidden = false;
				var empty = SK.doms.favNone;
				if (empty) empty.hidden = visn !== 0;
			}
			else secs[i].hidden = visn === 0;
		}
		SK.doms.toolNone.textContent = t("web.find.none") || "没有匹配的工具";
		SK.doms.toolNone.hidden = n !== 0;
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
		var cards = F.qa(".cards button, .cards a");
		var i;
		for (i = 0; i < cards.length; i++) {
			if (F.attr(cards[i], "data-clone")) continue;
			if ((F.attr(cards[i], "data-tool") || F.attr(cards[i], "data-page") || F.attr(cards[i], "data-link")) === id) return cards[i];
		}
		return null;
	}

	function extpage(id){
		var card = cardof(id);
		return card ? (F.attr(card, "href") || "") : "";
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
		var btn = SK.doms.toolFav;
		if (!btn) return;
		var picked = F.attr(btn, "data-fav") || "";
		var lit = picked && isfav(picked);
		btn.className = lit ? "back on" : "back";
		var favKey = lit ? "web.fav.on" : "web.fav";
		F.seta(btn, "data-ktip", favKey);
		btn.title = t(favKey) || (lit ? "已收藏" : "收藏");
	}

	// 工具传进来的键（如 "h-msg"）先在 page.doms 里找，没有才退回 HTML id。
	function domkey(id){
		var parts = String(id == null ? "" : id).split("-");
		var out = parts[0];
		var i;
		for (i = 1; i < parts.length; i++) out += parts[i].charAt(0).toUpperCase() + parts[i].substring(1);
		return out;
	}

	// 先看当前工具的 page.doms（新写法），再看页面树的 doms（壳），最后才退回 HTML id。
	function msg(id, text, bad){
		var el = (SK.curpage && SK.curpage.doms && SK.curpage.doms[domkey(id)])
			|| (SK.doms && SK.doms[domkey(id)]);
		if (!el) el = F.id(id);
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

	function playwav(b64, mid, onend){
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
		F.sink(audio);
		wavclip = audio;
		audio.onended = function(){
			if (wavclip === audio) stopwav();
			if (onend) onend();
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
		var box = domof(id);
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







	// ── 工具骨架：统一用 DOM 拼，不再返回 HTML 字符串 ──
	// 带 id 的控件同时登记进当前工具的 page.doms（id 转驼峰）；
	// 旧的 SK.val / combo / combofill 仍按 id 找，迁移期间两种取法并存。
	function domhost(){
		return SK.curpage || SK.doms || null;
	}

	function binddom(el, id){
		if (id) el.id = id;
		var page = domhost();
		if (id && page && page.doms) page.doms[domkey(id)] = el;
		return el;
	}

	function elem(tag, cls){
		var el = document.createElement(tag);
		if (cls) el.className = cls;
		return el;
	}

	function knode(text){
		var key = webkey(text);
		if (!key) return document.createTextNode(String(text == null ? "" : text));
		var span = elem("span");
		span.setAttribute("data-k", key);
		span.textContent = t(key);
		return span;
	}

	// 子节点可以是 DOM、DOM 数组或文本；null 直接跳过。
	function put(parent, child){
		var i;
		if (child == null || child === "") return;
		if (child instanceof Array) {
			for (i = 0; i < child.length; i++) put(parent, child[i]);
			return;
		}
		if (child instanceof Node) parent.appendChild(child);
		else parent.appendChild(document.createTextNode(String(child)));
	}

	function lab(title, inner){
		var el = elem("label");
		put(el, knode(title));
		put(el, inner);
		return el;
	}

	function row2(a, b){
		var el = elem("div", "row");
		put(el, a);
		put(el, b);
		return el;
	}

	function ta(id, rows, ph){
		var el = elem("textarea");
		el.rows = rows;
		var key = webkey(ph || "");
		if (key) {
			F.seta(el, "data-kph", key);
			el.placeholder = t(key);
		}
		else el.placeholder = ph || "";
		return binddom(el, id);
	}

	function textin(id, value, ph){
		var el = elem("input");
		el.type = "text";
		el.value = value == null ? "" : value;
		var key = webkey(ph || "");
		if (key) {
			F.seta(el, "data-kph", key);
			el.placeholder = t(key);
		}
		else el.placeholder = ph || "";
		return binddom(el, id);
	}

	// 可输入的下拉。combo 生成框，combofill 填分组。groups: [{ g, items: [{ v, t }] }]。
	var COMBOS = {};

	function combo(id, value, ph){
		var key = webkey(ph || "");
		var box = elem("div", "combo");
		var input = elem("input");
		input.type = "text";
		input.autocomplete = "off";
		input.value = value || "";
		input.placeholder = key ? t(key) : (ph || "");
		if (key) F.seta(input, "data-kph", key);
		binddom(input, id);
		var btn = elem("button", "combo-btn");
		btn.type = "button";
		btn.tabIndex = -1;
		put(btn, elem("i", "fa-solid fa-chevron-down"));
		var pop = elem("div", "combo-pop");
		pop.hidden = true;
		put(box, [input, btn, pop]);
		return box;
	}

	function numin(id, value){
		var el = elem("input");
		el.type = "number";
		el.value = value == null ? "" : value;
		return binddom(el, id);
	}

	function check(id, title, on){
		var el = elem("label", "check");
		var box = elem("input");
		box.type = "checkbox";
		box.checked = !!on;
		binddom(box, id);
		put(el, [box, document.createTextNode(" "), knode(title)]);
		return el;
	}

	function radio(name, id, title, on){
		var el = elem("label", "check");
		var box = elem("input");
		box.type = "radio";
		box.name = name;
		box.value = title;
		box.checked = !!on;
		binddom(box, id);
		put(el, [box, document.createTextNode(" "), knode(title)]);
		return el;
	}

	// 单个 option，给要在 JS 里拼下拉的地方用。
	function mkopt(value, text){
		var op = document.createElement("option");
		op.value = value;
		op.textContent = text;
		return op;
	}

	function opts(list, sel){
		var out = [];
		var i, op, key;
		for (i = 0; i < list.length; i++) {
			op = document.createElement("option");
			op.value = list[i][0];
			if (list[i][0] === sel) op.selected = true;
			key = webkey(list[i][1]);
			if (key) {
				op.setAttribute("data-k", key);
				op.textContent = t(key);
			}
			else op.textContent = list[i][1];
			out.push(op);
		}
		return out;
	}

	// 动态换下拉内容（单位、进制、日期单位之类）。
	function setopts(sel, list, chosen){
		if (!sel) return;
		while (sel.firstChild) sel.removeChild(sel.firstChild);
		var arr = opts(list, chosen);
		var i;
		for (i = 0; i < arr.length; i++) sel.appendChild(arr[i]);
	}

	function combofill(id, groups){
		var st = COMBOS[id] || (COMBOS[id] = { groups: [], hi: -1, all: true, visible: [] });
		st.groups = groups || [];
		st.hi = -1;
		if (comboopenq(id)) paintcombo(id);
	}

	function combowatch(){
		F.on(document, "mousedown", oncombodown);
		F.on(document, "click", oncomboclick);
		F.on(document, "keydown", oncombokey);
		F.on(document, "input", oncomboinput);
		F.on(document, "scroll", oncomboscroll, true);
		F.on(window, "resize", function(){ closecombos(); });
	}

	function comboroot(id){
		var box = domof(id);
		var el = box ? box.parentNode : null;
		if (!el || !el.className || String(el.className).indexOf("combo") < 0) return null;
		return el;
	}

	function combopop(root){
		return root ? F.q(".combo-pop", root) : null;
	}

	function comboopenq(id){
		var pop = combopop(comboroot(id));
		return !!(pop && !pop.hidden);
	}

	function comboid(el){
		while (el && el !== document.body) {
			if (el.className && String(el.className).indexOf("combo") >= 0 && String(el.className).indexOf("combo-") < 0) {
				var box = F.q("input", el);
				return box ? box.id : "";
			}
			el = el.parentNode;
		}
		return "";
	}

	function combohit(el, cls){
		while (el && el !== document.body) {
			if (el.className && (" " + el.className + " ").indexOf(" " + cls + " ") >= 0) return el;
			el = el.parentNode;
		}
		return null;
	}

	function closecombos(){
		var id;
		for (id in COMBOS) if (Object.prototype.hasOwnProperty.call(COMBOS, id)) comboclose(id);
	}

	function comboclose(id){
		var pop = combopop(comboroot(id));
		if (pop) pop.hidden = true;
		if (COMBOS[id]) COMBOS[id].hi = -1;
	}

	function comboopen(id, filter){
		var st = COMBOS[id] || (COMBOS[id] = { groups: [], hi: -1, all: true, visible: [] });
		var id2;
		for (id2 in COMBOS) if (id2 !== id && Object.prototype.hasOwnProperty.call(COMBOS, id2)) comboclose(id2);
		st.all = !filter;
		st.hi = -1;
		paintcombo(id);
		var pop = combopop(comboroot(id));
		if (!pop) return;
		pop.hidden = false;
		placecombo(id);
	}

	function paintcombo(id){
		var st = COMBOS[id];
		var pop = combopop(comboroot(id));
		if (!st || !pop) return;
		var box = domof(id);
		var q = st.all || !box ? "" : String(box.value || "").toLowerCase();
		var html = "";
		var vis = [];
		var gi, ii, grp, it, head, chunk;
		for (gi = 0; gi < st.groups.length; gi++) {
			grp = st.groups[gi];
			head = "";
			chunk = "";
			for (ii = 0; ii < grp.items.length; ii++) {
				it = grp.items[ii];
				if (q && String(it.v).toLowerCase().indexOf(q) < 0 && String(it.t).toLowerCase().indexOf(q) < 0) continue;
				chunk += '<div class="combo-item" data-v="' + htmlenc(it.v) + '">' + htmlenc(it.t) + "</div>";
				vis.push(it.v);
			}
			if (!chunk) continue;
			if (grp.g) head = '<div class="combo-g">' + htmlenc(grp.g) + "</div>";
			html += head + chunk;
		}
		st.visible = vis;
		if (st.hi >= vis.length) st.hi = vis.length - 1;
		pop.innerHTML = html;
		markcombo(id);
	}

	function markcombo(id){
		var st = COMBOS[id];
		var pop = combopop(comboroot(id));
		if (!st || !pop) return;
		var items = F.qa(".combo-item", pop);
		var i, el;
		for (i = 0; i < items.length; i++) {
			el = items[i];
			if (i === st.hi) el.className = "combo-item on";
			else el.className = "combo-item";
		}
		el = st.hi >= 0 ? items[st.hi] : null;
		if (!el) return;
		var top = el.offsetTop;
		var bot = top + el.offsetHeight;
		if (top < pop.scrollTop) pop.scrollTop = top;
		else if (bot > pop.scrollTop + pop.clientHeight) pop.scrollTop = bot - pop.clientHeight;
	}

	function placecombo(id){
		var box = domof(id);
		var pop = combopop(comboroot(id));
		if (!box || !pop || pop.hidden) return;
		var r = box.getBoundingClientRect();
		var below = window.innerHeight - r.bottom - 8;
		pop.style.left = Math.max(8, r.left) + "px";
		pop.style.width = r.width + "px";
		if (below < 180 && r.top > below) {
			pop.style.top = "auto";
			pop.style.bottom = (window.innerHeight - r.top + 4) + "px";
			pop.style.maxHeight = Math.max(120, r.top - 12) + "px";
		}
		else {
			pop.style.bottom = "auto";
			pop.style.top = (r.bottom + 4) + "px";
			pop.style.maxHeight = Math.max(120, below - 4) + "px";
		}
	}

	function combopick(id, value){
		var box = domof(id);
		if (!box) return;
		box.value = value;
		comboclose(id);
		box.focus();
		var ev;
		try { ev = new Event("input", { bubbles: true }); }
		catch (e) {
			ev = document.createEvent("Event");
			ev.initEvent("input", true, true);
		}
		box.dispatchEvent(ev);
	}

	function movecombo(id, dir){
		var st = COMBOS[id];
		if (!st || !st.visible || !st.visible.length) return;
		var n = st.visible.length;
		if (st.hi < 0) st.hi = dir > 0 ? 0 : n - 1;
		else st.hi = (st.hi + dir + n) % n;
		markcombo(id);
	}

	function oncombodown(ev){
		if (combohit(ev.target, "combo-btn") || combohit(ev.target, "combo-pop")) F.pd(ev);
	}

	function oncomboclick(ev){
		var btn = combohit(ev.target, "combo-btn");
		if (btn) {
			var id = comboid(btn);
			if (!id) return;
			if (comboopenq(id)) comboclose(id);
			else comboopen(id, false);
			return;
		}
		var item = combohit(ev.target, "combo-item");
		if (item) {
			combopick(comboid(item), F.attr(item, "data-v") || "");
			return;
		}
		if (!combohit(ev.target, "combo")) closecombos();
	}

	function oncombokey(ev){
		var box = ev.target;
		if (!box || !box.id || !COMBOS[box.id]) return;
		var id = box.id;
		if (ev.key === "Escape") { comboclose(id); return; }
		if (ev.key === "ArrowDown") {
			if (!comboopenq(id)) comboopen(id, false);
			movecombo(id, 1);
			F.pd(ev);
			return;
		}
		if (ev.key === "ArrowUp") {
			if (!comboopenq(id)) return;
			movecombo(id, -1);
			F.pd(ev);
			return;
		}
		if (ev.key === "Enter" && comboopenq(id)) {
			var st = COMBOS[id];
			if (st && st.hi >= 0 && st.visible && st.visible[st.hi] != null) {
				combopick(id, st.visible[st.hi]);
				F.pd(ev);
			}
		}
	}

	function oncomboinput(ev){
		var box = ev.target;
		if (!box || !box.id || !COMBOS[box.id] || !comboopenq(box.id)) return;
		COMBOS[box.id].all = false;
		COMBOS[box.id].hi = -1;
		paintcombo(box.id);
		placecombo(box.id);
	}

	function oncomboscroll(ev){
		var t = ev.target;
		if (t && t.className && String(t.className).indexOf("combo-pop") >= 0) return;
		closecombos();
	}







	// 工具里要拿元素时用 SK.dom("box-h")：先看当前工具的 page.doms，
	// 没有再退回 HTML id。迁移期间两种写法都能用。
	function domof(id){
		var k = domkey(id);
		return (SK.curpage && SK.curpage.doms && SK.curpage.doms[k]) || (SK.doms && SK.doms[k]) || F.id(id);
	}

	function val(id){
		var el = domof(id);
		return el && el.value != null ? String(el.value) : "";
	}

	function onbox(id){
		var el = domof(id);
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
			+ "\n" + tr("CMYK 用的是简单公式，不是印刷配置文件。")
			+ "\n" + tr("YUV 是 BT.601 全范围，分量 0–255。");
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
		var img = domof("ico-img");
		if (img) {
			img.onload = SK.drawicoprev;
			img.src = SK.icoUrl;
			img.hidden = false;
		}
		msg("box-msg", "已放入图片");
	}

	// 转义 HTML 文本和属性值。各工具统一用这个，别在各自文件里再写一份；
	// 属性里必须转引号，所以双引号也一并转掉。htmlenc 在此基础上多转单引号。
	function esc(s){
		return String(s == null ? "" : s)
			.replace(/&/g, "&amp;")
			.replace(/</g, "&lt;")
			.replace(/>/g, "&gt;")
			.replace(/"/g, "&quot;");
	}

	function openutil(name){
		stoptick();
		var spec = SK.utils[name];
		if (!spec) return;
		util = name;
		SK.doms.boxTitle.textContent = spec.title;
		var gokey = webkey(spec.go);
		if (gokey) {
			F.seta(SK.doms.boxGo, "data-k", gokey);
			SK.doms.boxGo.textContent = t(gokey);
		}
		else {
			F.unset(SK.doms.boxGo, "data-k");
			SK.doms.boxGo.textContent = spec.go;
		}
		SK.curpage = { doms: {} };
		var body = spec.body ? spec.body(SK.curpage) : "";
		if (typeof body === "string") SK.doms.boxBody.innerHTML = body;
		else {
			SK.doms.boxBody.innerHTML = "";
			put(SK.doms.boxBody, body);
		}
		SK.doms.boxOut.value = "";
		SK.doms.boxGo.parentElement.style.display = "";
		SK.doms.boxGo.style.display = "";
		SK.doms.boxCopy.style.display = "";
		SK.doms.boxOut.parentElement.style.display = "";
		if (SK.showreout) SK.showreout(false);
		if (SK.showrcolor) SK.showrcolor(false);
		msg("box-msg", "");
		if (spec.hideGo) SK.doms.boxGo.parentElement.style.display = "none";
		if (spec.noGo) SK.doms.boxGo.style.display = "none";
		if (spec.hideCopy) SK.doms.boxCopy.style.display = "none";
		if (spec.hideOut) SK.doms.boxOut.parentElement.style.display = "none";
		if (spec.open) spec.open(SK.curpage);
		if (spec.live === true || (spec.live !== false && textcard(name))) bindboxlive();
	}

	function textcard(name){
		var card = cardof(name);
		return !!(card && F.attr(card, "data-cat") === "text");
	}

	function bindboxlive(){
		var body = SK.doms.boxBody;
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
			spec.run(SK.curpage);
			return;
		}
		SK.reNote = "";
		try {
			var out = spec.run(SK.curpage);
			out = out == null ? "" : String(out);
			var outKey = webkey(out);
			SK.doms.boxOut.value = outKey ? (t(outKey) || out) : out;
			msg("box-msg", SK.reNote || "完成");
		}
		catch (ex) {
			SK.doms.boxOut.value = "";
			if (SK.showreout) SK.showreout(false);
			if (SK.showrcolor) SK.showrcolor(false);
			msg("box-msg", ex && ex.message ? ex.message : "失败", true);
		}
	}

	SK.boot = boot;
	SK.onclick = onclick;
	SK.onlang = onlang;
	SK.domkey = domkey;
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
	SK.dom = domof;
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
	SK.combo = combo;
	SK.combofill = combofill;
	SK.numin = numin;
	SK.check = check;
	SK.radio = radio;
	SK.opts = opts;
	SK.opt = mkopt;
	SK.setopts = setopts;
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
	SK.tx = tx;
	SK.langcode = langcode;
	SK.setlab = setlab;
	SK.setopt = setopt;
	SK.tr = tr;
	SK.markhtml = markhtml;
	SK.openutil = openutil;
	SK.utilrun = utilrun;
	SK.t = t;
	combowatch();
})();

