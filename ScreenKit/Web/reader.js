// 小说阅读器：web/up/reader 下的 txt、md，划词查词典，按句朗读并高亮当前句。
(function(){
	var langAt = 0;
	var wavclip = null;
	var wavurl = "";
	var st = {
		built: false,
		books: [],
		cur: "",
		ext: "",
		enc: "",
		text: "",
		chap: [],
		sent: [],
		font: 19,
		dlang: "",
		token: 0,
		reading: false,
		engine: "",
		vlang: "",
		voice: "",
		rate: 1,
		voices: [],
		selpick: null,
		hit: null,
		hl: null,
		tablang: "all",
		pendingid: 0
	};
	var scrollever = 0;


	// ==================== 页面与语言 ====================
	function $(id){ return document.getElementById(id); }
	function qa(sel, root){ return (root || document).querySelectorAll(sel); }
	function el(tag){ return document.createElement(tag); }
	function on(target, type, fn, opt){
		if (arguments.length < 4) target.addEventListener(type, fn);
		else target.addEventListener(type, fn, opt);
	}
	function attr(node, name){ return node.getAttribute(name); }
	function rect(node){ return node.getBoundingClientRect(); }
	function urlof(blob){ return URL.createObjectURL(blob); }
	function unurl(url){ URL.revokeObjectURL(url); }
	function sink(node){
		if (!node || typeof node.setSinkId !== "function") return;
		var p;
		try { p = node.setSinkId("default"); }
		catch (e) { return; }
		if (p && p.then) p.then(function(){}, function(){
			try { node.setSinkId(""); } catch (e2) {}
		});
	}

	function langs(){
		if (window.L && window.L._lang && window.L._lang.length) return window.L._lang;
		return ["zh", "en", "ja", "ko", "es", "fr", "pt", "ru", "de"];
	}

	function t(key){
		var row = window.L && window.L[key];
		if (!row || !row.length) return "";
		var s = row[langAt];
		if (s) return s;
		return row[0] || "";
	}

	function filllang(){
		var sel = $("lang");
		if (!sel) return;
		var codes = langs();
		var names = (window.L && window.L._name) || [];
		sel.innerHTML = "";
		var i;
		for (i = 0; i < codes.length; i++) {
			var op = el("option");
			op.value = codes[i];
			op.textContent = names[i] || codes[i];
			sel.appendChild(op);
		}
		var use = String(window.SKL || "").toLowerCase();
		langAt = 0;
		for (i = 0; i < codes.length; i++) if (use === codes[i]) langAt = i;
		sel.value = codes[langAt] || codes[0] || "";
		sel.onchange = function(){
			langAt = 0;
			for (i = 0; i < codes.length; i++) if (codes[i] === sel.value) langAt = i;
			applylang();
			relabel();
		};
	}

	function applylang(){
		var nodes = qa("[data-k]");
		var i;
		var s;
		for (i = 0; i < nodes.length; i++) {
			s = t(attr(nodes[i], "data-k"));
			if (s) nodes[i].textContent = s;
		}
		nodes = qa("[data-kph]");
		for (i = 0; i < nodes.length; i++) {
			s = t(attr(nodes[i], "data-kph"));
			if (s) nodes[i].placeholder = s;
		}
		nodes = qa("[data-ktip]");
		for (i = 0; i < nodes.length; i++) {
			s = t(attr(nodes[i], "data-ktip"));
			if (s) nodes[i].title = s;
		}
		var code = langs()[langAt] || "zh";
		document.documentElement.lang = code === "zh" ? "zh-CN" : code;
		document.title = t("web.tool.reader") || "小说阅读器";
	}

	// ==================== 网页播放 ====================
	function stopwav(){
		if (wavclip) {
			wavclip.pause();
			wavclip.src = "";
			wavclip = null;
		}
		if (wavurl) {
			unurl(wavurl);
			wavurl = "";
		}
	}

	function playwav(b64, onend){
		if (!b64) return;
		var bin = atob(b64);
		var bytes = new Uint8Array(bin.length);
		var i;
		for (i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i) & 255;
		var blob = new Blob([bytes], { type: "audio/wav" });
		var url = urlof(blob);
		stopwav();
		wavurl = url;
		var audio = new Audio(url);
		sink(audio);
		wavclip = audio;
		audio.onended = function(){
			if (wavclip === audio) stopwav();
			if (onend) onend();
		};
		var played = audio.play();
		if (played && played.then) played.then(function(){}, function(){
			if (wavclip === audio) stopwav();
			setmsg(t("web.reader.novoice") || "无法播放", true);
		});
	}

	// ==================== 启动 ====================
	function boot(){
		filllang();
		build();
		applylang();
		loadbooks(restorelast);
		loadvoices();
		wiredrop();
		on(window, "pagehide", function(){
			stopread(true);
			try { navigator.sendBeacon("/api/reader/stop", new Blob()); } catch (e) {}
		});
	}

	function restorelast(){
		if (st.cur) return;
		var last = "";
		try { last = localStorage.getItem("sk-reader-file") || ""; } catch (e) {}
		if (last && hasbook(last)) openbook(last, false);
	}

	/** 界面语言换了：引擎名、章节和空列表按新语言重画。 */
	function relabel(){
		if (!st.built) return;
		savetts();
		fillengines();
		fillchap();
		settitle(st.cur);
		drawrate();
		paintbooks();
		if ($("rd-dict")) painttabs();
	}

	// ==================== 界面 ====================
	function build(){
		st.built = true;
		var root = $("rd-root");
		if (!root) return;
		try { st.font = parseInt(localStorage.getItem("sk-reader-font") || "19", 10); } catch (e) { st.font = 19; }
		if (!st.font || st.font < 14) st.font = 14;
		if (st.font > 32) st.font = 32;
		try { st.rate = parseFloat(localStorage.getItem("sk-reader-rate") || "1"); } catch (e) { st.rate = 1; }
		if (!isFinite(st.rate) || st.rate < 0.5) st.rate = 1;
		if (st.rate > 2) st.rate = 2;
		root.innerHTML = [
			'<aside class="rd-side">',
			'<div class="rd-side-top">',
			'<button id="rd-upload" type="button"><i class="fa-solid fa-upload"></i><span data-k="web.reader.upload">' + L("web.reader.upload", "上传") + "</span></button>",
			'<input id="rd-file" type="file" accept=".txt,.md,.markdown,.mdown,.mkd,text/plain,text/markdown" multiple hidden>',
			"</div>",
			'<input id="rd-filter" class="rd-filter" type="search" autocomplete="off" placeholder="' + esc(L("web.reader.filter", "筛文件名")) + '" data-kph="web.reader.filter">',
			'<div id="rd-books" class="rd-books"></div>',
			'<div class="rd-side-foot" data-k="web.reader.hint">' + L("web.reader.hint", "划选正文查词典；选中后点朗读从这个词开始。") + "</div>",
			"</aside>",
			'<div class="rd-main">',
			'<div class="rd-bar">',
			'<div class="rd-bar1">',
			'<span id="rd-title" class="rd-title">' + L("web.reader.pick", "从左边选一篇开始读") + "</span>",
			'<span id="rd-prog" class="rd-prog" hidden>0%</span>',
			'<span id="rd-msg" class="rd-msg"></span>',
			'<span class="rd-grow"></span>',
			'<button id="rd-fontm" class="rd-btn" type="button" title="' + esc(L("web.reader.fontsmall", "字小一点")) + '">A−</button>',
			'<button id="rd-fontp" class="rd-btn" type="button" title="' + esc(L("web.reader.fontbig", "字大一点")) + '">A+</button>',
			'<button id="rd-dictbtn" class="rd-btn" type="button"><i class="fa-solid fa-book"></i><span data-k="web.reader.dict">' + L("web.reader.dict", "词典") + "</span></button>",
			'<button id="rd-read" class="rd-btn primary" type="button"><i class="fa-solid fa-play"></i><span data-k="web.reader.read">' + L("web.reader.read", "朗读") + "</span></button>",
			'<button id="rd-stop" class="rd-btn" type="button" disabled><i class="fa-solid fa-stop"></i><span data-k="web.reader.stop">' + L("web.reader.stop", "停止") + "</span></button>",
			"</div>",
			'<div class="rd-bar2">',
			'<label class="rd-f"><span data-k="web.reader.enc">' + L("web.reader.enc", "编码") + "</span>",
			'<select id="rd-enc"><option value="" data-k="web.reader.auto">' + L("web.reader.auto", "自动") + "</option>",
			'<option value="utf-8">UTF-8</option><option value="gb18030">GB18030</option><option value="gbk">GBK</option>',
			'<option value="big5">Big5</option><option value="utf-16le">UTF-16 LE</option><option value="utf-16be">UTF-16 BE</option></select></label>',
			'<label class="rd-f"><span data-k="web.reader.dlang">' + L("web.reader.dlang", "词典") + "</span>",
			'<select id="rd-dlang"><option value="" data-k="web.reader.dictall">' + L("web.reader.dictall", "全部词典") + "</option>",
			'<option value="zh" data-k="dict.filter.zh">' + L("dict.filter.zh", "汉语") + '</option><option value="en" data-k="dict.filter.en">' + L("dict.filter.en", "英语") + "</option>",
			'<option value="ja" data-k="dict.filter.ja">' + L("dict.filter.ja", "日语") + '</option><option value="ko" data-k="dict.filter.ko">' + L("dict.filter.ko", "韩语") + "</option></select></label>",
			'<label class="rd-f"><span data-k="web.reader.chap">' + L("web.reader.chap", "章节") + '</span><select id="rd-chap"><option value="">' + L("web.reader.nochap", "没有章节") + "</option></select></label>",
			'<label class="rd-f"><span data-k="web.reader.engine">' + L("web.reader.engine", "引擎") + '</span><select id="rd-engine"></select></label>',
			'<label class="rd-f"><span data-k="web.reader.vlang">' + L("web.reader.vlang", "发音语言") + '</span><select id="rd-vlang"></select></label>',
			'<label class="rd-f"><span data-k="web.reader.voice">' + L("web.reader.voice", "发音人") + '</span><select id="rd-voice"></select></label>',
			'<label class="rd-f rd-frate"><span data-k="web.reader.rate">' + L("web.reader.rate", "语速") + '</span><input id="rd-rate" type="range" min="0.5" max="2" step="0.1"><span id="rd-ratev" class="rd-ratev"></span></label>',
			"</div>",
			"</div>",
			'<div id="rd-scroll" class="rd-scroll"><article id="rd-body" class="rd-body"></article></div>',
			"</div>",
			'<div id="rd-pop" class="rd-pop" hidden>',
			'<div class="rd-pop-head"><span id="rd-pop-word" class="rd-pop-word"></span><button id="rd-pop-hide" class="rd-x" type="button" title="' + esc(L("web.reader.close", "关闭")) + '">×</button></div>',
			'<div id="rd-pop-items" class="rd-pop-items"></div>',
			'<div class="rd-pop-ops">',
			'<button id="rd-pop-speak" type="button" data-k="web.reader.speak">' + L("web.reader.speak", "发音") + "</button>",
			'<button id="rd-pop-read" type="button" data-k="web.reader.readhere">' + L("web.reader.readhere", "从这里朗读") + "</button>",
			'<button id="rd-pop-copy" type="button" data-k="web.reader.copy">' + L("web.reader.copy", "复制") + "</button>",
			"</div>",
			"</div>",
			'<div id="rd-dict" class="rd-dict" hidden>',
			'<div id="rd-dict-bar" class="rd-dict-bar"><strong data-k="dict.title">' + L("dict.title", "词典") + "</strong>",
			'<span id="rd-dict-status" class="rd-dict-status"></span>',
			'<button id="rd-dict-close" class="rd-x" type="button" title="' + esc(L("web.reader.close", "关闭")) + '">×</button></div>',
			'<div class="rd-dict-search"><input id="rd-dict-q" type="search" autocomplete="off" placeholder="' + esc(L("web.reader.dictq", "词、字、拼音或英文")) + '" data-kph="web.reader.dictq">',
			'<button id="rd-dict-go" type="button" data-k="web.reader.search">' + L("web.reader.search", "查") + "</button></div>",
			'<div id="rd-dict-tabs" class="rd-dict-tabs">',
			'<button type="button" data-lang="all" data-k="dict.filter.all">' + L("dict.filter.all", "全部") + "</button>",
			'<button type="button" data-lang="zh" data-k="dict.filter.zh">' + L("dict.filter.zh", "汉语") + "</button>",
			'<button type="button" data-lang="en" data-k="dict.filter.en">' + L("dict.filter.en", "英语") + "</button>",
			'<button type="button" data-lang="ja" data-k="dict.filter.ja">' + L("dict.filter.ja", "日语") + "</button>",
			'<button type="button" data-lang="ko" data-k="dict.filter.ko">' + L("dict.filter.ko", "韩语") + "</button>",
			"</div>",
			'<div class="rd-dict-main"><div id="rd-dict-list" class="rd-dict-list"></div><div id="rd-dict-detail" class="rd-dict-detail"></div></div>',
			"</div>"
		].join("");
		wire();
		applyfont();
		drawrate();
		settitle(st.cur);
	}

	function wire(){
		$("rd-upload").onclick = function(){ $("rd-file").click(); };
		$("rd-file").onchange = function(){
			upload($("rd-file").files);
			$("rd-file").value = "";
		};
		$("rd-filter").oninput = paintbooks;
		$("rd-fontm").onclick = function(){ setfont(st.font - 1); };
		$("rd-fontp").onclick = function(){ setfont(st.font + 1); };
		$("rd-enc").onchange = function(){
			st.enc = $("rd-enc").value || "";
			savemeta("enc", st.enc);
			if (st.cur) openbook(st.cur, false);
		};
		$("rd-dlang").onchange = function(){
			st.dlang = $("rd-dlang").value || "";
			savemeta("lang", st.dlang);
			painttabs();
			var q = $("rd-dict-q").value.trim();
			if (q) dictsearch(q);
		};
		$("rd-chap").onchange = function(){
			var id = $("rd-chap").value;
			var node = id ? $(id) : null;
			if (node) node.scrollIntoView({ block: "start" });
		};
		$("rd-read").onclick = function(){ readstart(); };
		$("rd-stop").onclick = function(){ stopread(false); };
		$("rd-dictbtn").onclick = function(){ dicttoggle(); };
		$("rd-engine").onchange = function(){
			st.engine = $("rd-engine").value;
			savetts();
			fillvlang();
		};
		$("rd-vlang").onchange = function(){
			st.vlang = $("rd-vlang").value;
			savetts();
			fillvoice();
		};
		$("rd-voice").onchange = function(){
			st.voice = $("rd-voice").value;
			savetts();
		};
		$("rd-rate").oninput = function(){
			st.rate = parseFloat($("rd-rate").value) || 1;
			drawrate();
			savetts();
		};
		var sc = $("rd-scroll");
		on(sc, "scroll", function(){
			if (!st.cur) return;
			st.selpick = null;
			paintprog();
			if (scrollever) clearTimeout(scrollever);
			scrollever = setTimeout(function(){
				scrollever = 0;
				if (!st.cur) return;
				try {
					localStorage.setItem("sk-reader-scroll." + st.cur, String(sc.scrollTop));
					localStorage.setItem("sk-reader-pct." + st.cur, String(progpct()));
				} catch (e) {}
				paintbooks();
			}, 200);
		});
		wirepop();
		wiredict();
	}

	function settitle(name){
		var el = $("rd-title");
		if (el) el.textContent = name || L("web.reader.pick", "从左边选一篇开始读");
		if ($("rd-prog")) $("rd-prog").hidden = !name;
	}

	function setfont(n){
		st.font = Math.max(14, Math.min(32, n));
		try { localStorage.setItem("sk-reader-font", String(st.font)); } catch (e) {}
		applyfont();
		paintprog();
	}

	function applyfont(){
		var body = $("rd-body");
		if (body) body.style.fontSize = st.font + "px";
	}

	function drawrate(){
		var inp = $("rd-rate");
		if (inp) inp.value = String(st.rate);
		var lab = $("rd-ratev");
		if (lab) lab.textContent = st.rate.toFixed(1) + "×";
	}

	function setmsg(text, bad){
		var el = $("rd-msg");
		if (!el) return;
		el.textContent = text || "";
		el.title = text || "";
		el.className = bad ? "rd-msg bad" : "rd-msg";
	}

	// ==================== 书库 ====================
	function loadbooks(done){
		fetchjson("/api/reader/list").then(function(j){
			if (!j || j.code !== 100) {
				setmsg(errtext(j), true);
				return;
			}
			st.books = (j.data && j.data.items) || [];
			paintbooks();
			if (done) done();
		}).catch(function(e){ setmsg(e && e.message ? e.message : "网络错误", true); });
	}

	function paintbooks(){
		var box = $("rd-books");
		if (!box) return;
		var q = ($("rd-filter").value || "").trim().toLowerCase();
		box.innerHTML = "";
		var n = 0;
		for (var i = 0; i < st.books.length; i++) {
			var b = st.books[i];
			if (q && String(b.name).toLowerCase().indexOf(q) < 0) continue;
			n++;
			box.appendChild(bookrow(b));
		}
		if (!n) {
			var p = el("p");
			p.className = "rd-empty";
			p.textContent = st.books.length
				? L("web.reader.nomatch", "没有对上的文件")
				: L("web.reader.empty", "还没有书。上传 txt 或 md 文件放进来。");
			box.appendChild(p);
		}
	}

	function bookrow(b){
		var row = el("div");
		row.className = "rd-book" + (b.name === st.cur ? " on" : "");
		var name = el("button");
		name.type = "button";
		name.className = "rd-book-name";
		name.textContent = b.name;
		name.title = b.name;
		name.onclick = function(){ openbook(b.name, true); };
		row.appendChild(name);
		var pct = "";
		try { pct = localStorage.getItem("sk-reader-pct." + b.name) || ""; } catch (e) {}
		if (pct) {
			var p = el("span");
			p.className = "rd-book-pct";
			p.textContent = pct + "%";
			row.appendChild(p);
		}
		row.appendChild(opbtn("fa-pen", L("web.reader.rename", "改名"), function(){
			var next = window.prompt(L("web.reader.rename", "改名"), b.name);
			if (!next || next === b.name) return;
			renamebook(b.name, next);
		}));
		row.appendChild(opbtn("fa-trash-can", L("web.reader.delete", "删除"), function(){
			var msg = L("web.reader.confirm", "删除「{0}」？").replace("{0}", b.name);
			if (!window.confirm(msg)) return;
			delbook(b.name);
		}));
		return row;
	}

	function opbtn(icon, tip, fn){
		var b = el("button");
		b.type = "button";
		b.className = "rd-book-op";
		b.title = tip || "";
		b.innerHTML = '<i class="fa-solid ' + icon + '"></i>';
		b.onclick = function(ev){ ev.stopPropagation(); fn(); };
		return b;
	}

	function hasbook(name){
		for (var i = 0; i < st.books.length; i++) if (st.books[i].name === name) return true;
		return false;
	}

	function openbook(name, jump){
		if (!name) return;
		st.cur = name;
		st.ext = extname(name);
		st.selpick = null;
		hidepop();
		stopread(true);
		settitle(name);
		paintbooks();
		try { localStorage.setItem("sk-reader-file", name); } catch (e) {}
		var enc = readmeta("enc");
		st.enc = enc;
		$("rd-enc").value = enc;
		st.dlang = readmeta("lang");
		$("rd-dlang").value = st.dlang;
		var body = $("rd-body");
		body.className = "rd-body";
		body.innerHTML = '<p class="rd-empty">' + esc(L("web.reader.loading", "正在打开…")) + "</p>";
		var url = "/api/reader/file?name=" + encodeURIComponent(name) + (enc ? "&enc=" + encodeURIComponent(enc) : "");
		var mine = ++st.token;
		fetchjson(url).then(function(j){
			if (mine !== st.token || st.cur !== name) return;
			if (!j || j.code !== 100) {
				body.innerHTML = '<p class="rd-empty">' + esc(errtext(j)) + "</p>";
				return;
			}
			st.text = (j.data && j.data.text) || "";
			st.ext = (j.data && j.data.ext) || extname(name);
			if (j.data && j.data.enc && j.data.enc !== st.enc) {
				st.enc = j.data.enc;
				$("rd-enc").value = st.enc;
			}
			render(st.text, st.ext);
			var y = 0;
			if (!jump) {
				try { y = parseInt(localStorage.getItem("sk-reader-scroll." + name) || "0", 10) || 0; } catch (e) { y = 0; }
			}
			var sc = $("rd-scroll");
			sc.scrollTop = y;
			paintprog();
			setmsg("");
		}).catch(function(e){
			if (mine !== st.token) return;
			body.innerHTML = '<p class="rd-empty">' + esc(e && e.message ? e.message : "网络错误") + "</p>";
		});
	}

	function readmeta(kind){
		if (!st.cur) return "";
		try { return localStorage.getItem("sk-reader-" + kind + "." + st.cur) || ""; } catch (e) { return ""; }
	}

	function savemeta(kind, val){
		if (!st.cur) return;
		try {
			if (val) localStorage.setItem("sk-reader-" + kind + "." + st.cur, val);
			else localStorage.removeItem("sk-reader-" + kind + "." + st.cur);
		} catch (e) {}
	}

	/** 页面里拖入文件时就上传，不用先点上传。 */
	function wiredrop(){
		var depth = 0;
		on(document, "dragenter", function(ev){
			if (!hasfiles(ev)) return;
			ev.preventDefault();
			depth++;
			showdrop(true);
		});
		on(document, "dragover", function(ev){
			if (!hasfiles(ev)) return;
			ev.preventDefault();
			if (ev.dataTransfer) ev.dataTransfer.dropEffect = "copy";
			showdrop(true);
		});
		on(document, "dragleave", function(ev){
			if (!hasfiles(ev)) return;
			depth--;
			if (depth > 0) return;
			depth = 0;
			showdrop(false);
		});
		on(document, "drop", function(ev){
			if (!hasfiles(ev)) return;
			ev.preventDefault();
			depth = 0;
			showdrop(false);
			var files = ev.dataTransfer && ev.dataTransfer.files;
			if (files && files.length) upload(files);
		});
	}

	function hasfiles(ev){
		var dt = ev.dataTransfer;
		if (!dt || !dt.types) return false;
		for (var i = 0; i < dt.types.length; i++) {
			if (dt.types[i] === "Files") return true;
		}
		return false;
	}

	function showdrop(on_){
		var box = $("rd-drop");
		if (box) box.hidden = !on_;
	}

	function upload(files){
		if (!files || !files.length) return;
		var i = 0;
		var first = "";
		step();
		function step(){
			if (i >= files.length) {
				loadbooks();
				if (first) openbook(first, true);
				return;
			}
			var f = files[i++];
			var ext = extname(f.name);
			if (ext !== ".txt" && ext !== ".md" && ext !== ".markdown" && ext !== ".mdown" && ext !== ".mkd") {
				setmsg(L("web.reader.badfile", "请上传 txt 或 md 文件"), true);
				step();
				return;
			}
			if (f.size > 20 * 1024 * 1024) {
				setmsg(L("web.reader.toobig", "文件不能超过 20 MB"), true);
				step();
				return;
			}
			var overwrite = hasbook(f.name);
			if (overwrite) {
				var q = L("web.reader.overwrite", "覆盖同名文件「{0}」？").replace("{0}", f.name);
				if (!window.confirm(q)) { step(); return; }
				overwrite = true;
			}
			setmsg(L("web.reader.uploading", "正在上传…") + " " + f.name);
			var url = "/api/reader/upload?name=" + encodeURIComponent(f.name) + (overwrite ? "&overwrite=1" : "");
			fetch(url, { method: "POST", body: f }).then(function(r){ return r.json(); }).then(function(j){
				if (!j || j.code !== 100) {
					setmsg(errtext(j), true);
					step();
					return;
				}
				var saved = (j.data && j.data.name) || f.name;
				if (!first) first = saved;
				setmsg(L("web.reader.uploaded", "已上传") + " " + saved);
				step();
			}).catch(function(e){
				setmsg(e && e.message ? e.message : "网络错误", true);
				step();
			});
		}
	}

	function renamebook(from, to){
		fetchjson("/api/reader/rename", {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify({ from: from, to: to })
		}).then(function(j){
			if (!j || j.code !== 100) { setmsg(errtext(j), true); return; }
			var next = (j.data && j.data.name) || to;
			if (st.cur === from) {
				st.cur = next;
				settitle(next);
			}
			mvlocal(from, next);
			loadbooks();
		}).catch(function(e){ setmsg(e && e.message ? e.message : "网络错误", true); });
	}

	function delbook(name){
		fetchjson("/api/reader/delete", {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify({ name: name })
		}).then(function(j){
			if (!j || j.code !== 100) { setmsg(errtext(j), true); return; }
			if (st.cur === name) {
				st.cur = "";
				st.text = "";
				st.sent = [];
				st.chap = [];
				stopread(true);
				$("rd-body").innerHTML = '<p class="rd-empty">' + esc(L("web.reader.pick", "从左边选一篇开始读")) + "</p>";
				$("rd-scroll").scrollTop = 0;
				settitle("");
				$("rd-chap").innerHTML = '<option value="">' + esc(L("web.reader.nochap", "没有章节")) + "</option>";
				try { localStorage.removeItem("sk-reader-file"); } catch (e) {}
			}
			rmlocal(name);
			loadbooks();
		}).catch(function(e){ setmsg(e && e.message ? e.message : "网络错误", true); });
	}

	function mvlocal(from, to){
		try {
			var keys = ["scroll", "pct", "enc", "lang"];
			for (var i = 0; i < keys.length; i++) {
				var v = localStorage.getItem("sk-reader-" + keys[i] + "." + from);
				if (v == null) continue;
				localStorage.setItem("sk-reader-" + keys[i] + "." + to, v);
				localStorage.removeItem("sk-reader-" + keys[i] + "." + from);
			}
		} catch (e) {}
	}

	function rmlocal(name){
		try {
			var keys = ["scroll", "pct", "enc", "lang"];
			for (var i = 0; i < keys.length; i++) localStorage.removeItem("sk-reader-" + keys[i] + "." + name);
		} catch (e) {}
	}

	// ==================== 正文 ====================
	function render(text, ext){
		var body = $("rd-body");
		st.chap = [];
		st.hl = null;
		var md = ext === ".md" || ext === ".markdown" || ext === ".mdown" || ext === ".mkd";
		body.className = "rd-body" + (md ? " md" : " txt");
		body.innerHTML = md ? mdhtml(text) : txthtml(text);
		var list = qa("span.rs", body);
		st.sent = [];
		for (var i = 0; i < list.length; i++) {
			list[i].setAttribute("data-i", String(i));
			st.sent.push(list[i]);
		}
		fillchap();
	}

	function txthtml(text){
		var lines = String(text).replace(/^\uFEFF/, "").split(/\r\n|\n|\r/);
		var out = [];
		var n = 0;
		var any = false;
		for (var i = 0; i < lines.length; i++) {
			var s = lines[i].replace(/\s+$/g, "");
			if (!s.trim()) continue;
			any = true;
			if (ischap(s)) {
				var id = "rdc" + n++;
				out.push('<h2 id="' + id + '">' + spans(s.trim(), null) + "</h2>");
				st.chap.push({ id: id, text: s.trim() });
			}
			else out.push("<p>" + spans(s.trim(), null) + "</p>");
		}
		if (!any) return '<p class="rd-empty">' + esc(L("web.reader.blank", "这篇是空的。")) + "</p>";
		return out.join("");
	}

	function mdhtml(src){
		var lines = String(src || "").replace(/^\uFEFF/, "").replace(/\r\n/g, "\n").replace(/\r/g, "\n").split("\n");
		var out = [];
		var n = 0;
		var i = 0;
		while (i < lines.length) {
			var line = lines[i];
			if (!line.trim()) { i++; continue; }
			if (line.indexOf("```") === 0) {
				var buf = [];
				i++;
				while (i < lines.length && lines[i].indexOf("```") !== 0) { buf.push(lines[i]); i++; }
				if (i < lines.length) i++;
				out.push("<pre><code>" + esc(buf.join("\n")) + "</code></pre>");
				continue;
			}
			var hm = /^(#{1,6})\s+(.*)$/.exec(line);
			if (hm) {
				var lv = hm[1].length;
				var title = hm[2].replace(/\s+#+\s*$/, "");
				var id = "rdc" + n++;
				out.push("<h" + lv + ' id="' + id + '">' + spans(title, inline) + "</h" + lv + ">");
				if (lv <= 3) st.chap.push({ id: id, text: title });
				i++;
				continue;
			}
			if (/^>\s?/.test(line)) {
				var quote = [];
				while (i < lines.length && /^>\s?/.test(lines[i])) {
					quote.push(lines[i].replace(/^>\s?/, ""));
					i++;
				}
				out.push("<blockquote><p>" + spans(quote.join(" "), inline) + "</p></blockquote>");
				continue;
			}
			if (/^\s*[-*+]\s+/.test(line)) {
				out.push("<ul>");
				while (i < lines.length && /^\s*[-*+]\s+/.test(lines[i])) {
					out.push("<li>" + spans(lines[i].replace(/^\s*[-*+]\s+/, ""), inline) + "</li>");
					i++;
				}
				out.push("</ul>");
				continue;
			}
			if (/^\s*\d+[.)]\s+/.test(line)) {
				out.push("<ol>");
				while (i < lines.length && /^\s*\d+[.)]\s+/.test(lines[i])) {
					out.push("<li>" + spans(lines[i].replace(/^\s*\d+[.)]\s+/, ""), inline) + "</li>");
					i++;
				}
				out.push("</ol>");
				continue;
			}
			if (/^\s*([-*_])\1{2,}\s*$/.test(line)) {
				out.push("<hr>");
				i++;
				continue;
			}
			var para = [line];
			i++;
			while (i < lines.length && lines[i].trim() && !blockstart(lines[i])) {
				para.push(lines[i]);
				i++;
			}
			out.push("<p>" + spans(para.join(" "), inline) + "</p>");
		}
		if (!out.length) return '<p class="rd-empty">' + esc(L("web.reader.blank", "这篇是空的。")) + "</p>";
		return out.join("");
	}

	function blockstart(line){
		return line.indexOf("```") === 0 || /^(#{1,6})\s+/.test(line) || /^>\s?/.test(line)
			|| /^\s*[-*+]\s+/.test(line) || /^\s*\d+[.)]\s+/.test(line)
			|| /^\s*([-*_])\1{2,}\s*$/.test(line);
	}

	function spans(text, fmt){
		var arr = splitsents(text);
		var out = [];
		for (var i = 0; i < arr.length; i++) {
			var one = arr[i];
			if (!one) continue;
			out.push('<span class="rs">' + (fmt ? fmt(one) : esc(one)) + "</span>");
		}
		return out.join("");
	}

	function splitsents(text){
		var s = String(text);
		var out = [];
		var buf = "";
		var closers = "」』”’\"')）]】";
		for (var i = 0; i < s.length; i++) {
			var ch = s.charAt(i);
			buf += ch;
			var end = "。！？!?…".indexOf(ch) >= 0;
			if (!end && (ch === ";" || ch === "；")) end = true;
			if (!end && ch === "." && (i + 1 >= s.length || /\s/.test(s.charAt(i + 1)))) end = true;
			if (!end) continue;
			while (i + 1 < s.length && closers.indexOf(s.charAt(i + 1)) >= 0) buf += s.charAt(++i);
			if (buf.replace(/\s+/g, "")) out.push(buf);
			buf = "";
		}
		if (buf.replace(/\s+/g, "")) out.push(buf);
		return out;
	}

	function inline(raw){
		var s = String(raw);
		var out = "";
		var i = 0;
		while (i < s.length) {
			if (s.charAt(i) === "`") {
				var j = s.indexOf("`", i + 1);
				if (j > i) {
					out += "<code>" + esc(s.slice(i + 1, j)) + "</code>";
					i = j + 1;
					continue;
				}
			}
			if (s.substr(i, 2) === "**") {
				var j2 = s.indexOf("**", i + 2);
				if (j2 > i) {
					out += "<strong>" + inline(s.slice(i + 2, j2)) + "</strong>";
					i = j2 + 2;
					continue;
				}
			}
			if (s.charAt(i) === "*") {
				var j3 = s.indexOf("*", i + 1);
				if (j3 > i) {
					out += "<em>" + inline(s.slice(i + 1, j3)) + "</em>";
					i = j3 + 1;
					continue;
				}
			}
			if (s.charAt(i) === "[") {
				var m = /^\[([^\]]+)\]\(([^)\s]+)\)/.exec(s.slice(i));
				if (m && /^https?:\/\//i.test(m[2])) {
					out += '<a href="' + esc(m[2]) + '" target="_blank" rel="noopener">' + inline(m[1]) + "</a>";
					i += m[0].length;
					continue;
				}
			}
			var n = s.length;
			for (var k = i + 1; k < s.length; k++) {
				var c = s.charAt(k);
				if (c === "`" || c === "*" || c === "[") {
					n = k;
					break;
				}
			}
			out += esc(s.slice(i, n));
			i = n;
		}
		return out;
	}

	function ischap(line){
		var s = String(line).trim();
		if (!s || s.length > 28) return false;
		if (/^第[0-9０-９一二三四五六七八九十百千零〇两]{1,8}[回章节卷篇部集]/.test(s)) return true;
		if (/^[卷篇][0-9０-９一二三四五六七八九十百千零〇两]{1,6}/.test(s) && s.length <= 24) return true;
		if (s.length <= 16 && /第[一二三四五六七八九十百零〇]{1,4}$/.test(s)) return true;
		return false;
	}

	function fillchap(){
		var sel = $("rd-chap");
		if (!sel) return;
		sel.innerHTML = "";
		if (!st.chap.length) {
			var one = el("option");
			one.value = "";
			one.textContent = L("web.reader.nochap", "没有章节");
			sel.appendChild(one);
			return;
		}
		var head = el("option");
		head.value = "";
		head.textContent = L("web.reader.chap", "章节") + " · " + st.chap.length;
		sel.appendChild(head);
		for (var i = 0; i < st.chap.length; i++) {
			var op = el("option");
			op.value = st.chap[i].id;
			op.textContent = st.chap[i].text;
			sel.appendChild(op);
		}
	}

	function progpct(){
		var sc = $("rd-scroll");
		if (!sc) return 0;
		var max = sc.scrollHeight - sc.clientHeight;
		if (max <= 1) return 100;
		var pct = Math.round(sc.scrollTop / max * 100);
		if (pct < 0) pct = 0;
		if (pct > 100) pct = 100;
		return pct;
	}

	function paintprog(){
		var el = $("rd-prog");
		if (!el) return;
		if (!st.cur) {
			el.hidden = true;
			return;
		}
		el.hidden = false;
		el.textContent = progpct() + "%";
	}

	// ==================== 朗读 ====================
	function readstart(){
		if (!st.sent.length) {
			setmsg(L("web.reader.notext", "先打开一篇"), true);
			return;
		}
		var pick = livepick() || st.selpick;
		if (!pick) pick = { i: firstonscreen(), off: 0 };
		hidepop();
		st.selpick = null;
		try {
			var sel = window.getSelection();
			if (sel) sel.removeAllRanges();
		} catch (e) {}
		readfrom(pick.i, pick.off || 0);
	}

	function readfrom(start, off){
		var paused = stopread(true);
		var token = ++st.token;
		st.reading = true;
		$("rd-read").disabled = true;
		$("rd-stop").disabled = false;
		var queue = [];
		for (var i = start; i < st.sent.length; i++) {
			var text = st.sent[i].textContent || "";
			if (i === start && off > 0) text = text.slice(off);
			if (!text.replace(/\s+/g, "")) continue;
			queue.push({ i: i, text: text });
		}
		if (!queue.length) {
			stopread(false);
			return;
		}
		setmsg(L("web.reader.reading", "朗读中…"));
		paused.then(function(){
			if (token !== st.token || !st.reading) return;
			next();
		});
		function next(){
			if (token !== st.token || !st.reading) return;
			if (!queue.length) {
				st.reading = false;
				$("rd-read").disabled = false;
				$("rd-stop").disabled = true;
				clearhl();
				setmsg(L("web.reader.done", "读完了"));
				return;
			}
			var job = queue.shift();
			highlight(job.i);
			speak(job.text, function(err){
				if (token !== st.token || !st.reading) return;
				if (err) {
					st.reading = false;
					$("rd-read").disabled = false;
					$("rd-stop").disabled = true;
					clearhl();
					setmsg(err, true);
					return;
				}
				next();
			});
		}
	}

	function speak(text, done){
		var body = ttsbody(text);
		fetch("/api/tts", {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify(body)
		}).then(function(r){ return r.json(); }).then(function(j){
			if (!j || j.code !== 100) {
				done(errtext(j));
				return;
			}
			if (body.engine === "chrome") {
				done("");
				return;
			}
			var b64 = j.data && j.data.wav_base64;
			if (!b64) {
				done(L("web.reader.novoice", "没有音频"));
				return;
			}
			playwav(b64, "", function(){ done(""); });
		}).catch(function(e){
			done(e && e.message ? e.message : "网络错误");
		});
	}

	function ttsbody(text){
		var b = { text: text, speed: st.rate, volume: 100 };
		var eng = st.engine || "chrome";
		if (eng.indexOf("sherpa|") === 0) {
			b.engine = "sherpa";
			b.model = eng.substring(7);
		}
		else b.engine = eng;
		var v = curvoice();
		if (v && v.key) b.voice = v.key;
		if (v && v.engine === "sherpa") b.speaker_id = v.id;
		if (st.vlang) b.lang = st.vlang;
		return b;
	}

	function stopread(quiet){
		st.token++;
		st.reading = false;
		stopwav();
		clearhl();
		if (st.built) {
			if ($("rd-read")) $("rd-read").disabled = false;
			if ($("rd-stop")) $("rd-stop").disabled = true;
		}
		var done = fetch("/api/reader/stop", { method: "POST" }).catch(function(){});
		if (!quiet) setmsg(L("web.reader.stopped", "已停止"));
		return done;
	}

	function highlight(i){
		clearhl();
		var el = st.sent[i];
		if (!el) return;
		el.className = "rs on";
		st.hl = el;
		var sc = $("rd-scroll");
		var box = rect(sc);
		var r = rect(el);
		if (r.top < box.top + 8 || r.bottom > box.bottom - 8) el.scrollIntoView({ block: "center" });
	}

	function clearhl(){
		if (!st.hl) return;
		st.hl.className = "rs";
		st.hl = null;
	}

	function firstonscreen(){
		var sc = $("rd-scroll");
		var box = rect(sc);
		var list = st.sent;
		for (var i = 0; i < list.length; i++) {
			var r = rect(list[i]);
			if (!r.width && !r.height) continue;
			if (r.bottom > box.top + 2) return i;
		}
		return 0;
	}

	/** 选区落在哪一句、从这一句的第几个字开始。 */
	function livepick(){
		var sel = window.getSelection();
		if (!sel || sel.isCollapsed || !sel.rangeCount) return null;
		var body = $("rd-body");
		if (!body) return null;
		var range = sel.getRangeAt(0);
		if (!body.contains(range.startContainer)) return null;
		var node = range.startContainer;
		var el = node && node.nodeType === 1 ? node : (node ? node.parentNode : null);
		var span = el && el.closest ? el.closest("span.rs") : null;
		if (!span) return null;
		var i = parseInt(span.getAttribute("data-i") || "-1", 10);
		if (!(i >= 0)) return null;
		var off = 0;
		try {
			var r = document.createRange();
			r.setStart(span, 0);
			r.setEnd(node, range.startOffset);
			off = r.toString().length;
		}
		catch (e) { off = 0; }
		return { i: i, off: off };
	}

	// ==================== 发音人与引擎 ====================
	function loadvoices(){
		fetchjson("/api/tts/models").then(function(j){
			if (!j || j.code !== 100) {
				fillengines();
				return;
			}
			st.voices = [];
			var models = j.data || [];
			for (var i = 0; i < models.length; i++) {
				var m = models[i];
				var sps = m.speakers || [];
				for (var k = 0; k < sps.length; k++) {
					var s = sps[k];
					st.voices.push({
						engine: m.engine,
						model: m.name,
						id: s.id,
						key: s.key || "",
						label: s.label || s.name || "",
						lang: s.lang || ""
					});
				}
			}
			fillengines();
		}).catch(function(){ fillengines(); });
	}

	function engines(){
		var out = [];
		var seen = {};
		var i;
		for (i = 0; i < st.voices.length; i++) {
			var v = st.voices[i];
			var id = v.engine === "sherpa" ? "sherpa|" + v.model : v.engine;
			if (!id || seen[id]) continue;
			seen[id] = 1;
			out.push(id);
		}
		var rank = { chrome: 0, winrt: 1, sapi: 2, edge: 3 };
		out.sort(function(a, b){
			var ra = rank[a] == null ? (a.indexOf("sherpa|") === 0 ? 4 : 5) : rank[a];
			var rb = rank[b] == null ? (b.indexOf("sherpa|") === 0 ? 4 : 5) : rank[b];
			return ra - rb;
		});
		return out;
	}

	function englabel(id){
		if (id === "chrome") return L("web.reader.eng.chrome", "Chrome 离线语音");
		if (id === "winrt") return "Windows";
		if (id === "sapi") return "SAPI";
		if (id === "edge") return L("web.reader.eng.edge", "Edge 在线");
		if (id.indexOf("sherpa|") === 0) return id.substring(7);
		return id;
	}

	function fillengines(){
		var sel = $("rd-engine");
		if (!sel) return;
		var list = engines();
		sel.innerHTML = "";
		for (var i = 0; i < list.length; i++) {
			var op = el("option");
			op.value = list[i];
			op.textContent = englabel(list[i]);
			sel.appendChild(op);
		}
		var keep = "";
		try { keep = localStorage.getItem("sk-reader-engine") || ""; } catch (e) {}
		st.engine = "";
		if (keep) {
			for (i = 0; i < list.length; i++) if (list[i] === keep) st.engine = keep;
		}
		if (!st.engine) {
			st.engine = list.indexOf("chrome") >= 0 ? "chrome" : (list[0] || "chrome");
		}
		sel.value = st.engine;
		fillvlang();
	}

	function langlabel(code){
		var names = {
			zh: "中文", en: "English", ja: "日本語", ko: "한국어", yue: "粤語", "zh-hk": "粤語",
			"zh-tw": "國語", de: "Deutsch", es: "Español", fr: "Français", pt: "Português",
			ru: "Русский", it: "Italiano", hi: "हिन्दी", id: "Bahasa Indonesia", vi: "Tiếng Việt",
			nl: "Nederlands", pl: "Polski", th: "ไทย", ar: "العربية", tr: "Türkçe"
		};
		return names[String(code || "").toLowerCase()] || code;
	}

	function fillvlang(){
		var sel = $("rd-vlang");
		if (!sel) return;
		var list = [];
		var seen = {};
		for (var i = 0; i < st.voices.length; i++) {
			var v = st.voices[i];
			if (engof(v) !== st.engine) continue;
			var code = v.lang || "";
			if (!code || seen[code]) continue;
			seen[code] = 1;
			list.push(code);
		}
		list.sort();
		sel.innerHTML = "";
		var all = el("option");
		all.value = "";
		all.textContent = L("web.reader.vall", "全部语言");
		sel.appendChild(all);
		for (i = 0; i < list.length; i++) {
			var op = el("option");
			op.value = list[i];
			op.textContent = langlabel(list[i]);
			sel.appendChild(op);
		}
		var keep = "";
		try { keep = localStorage.getItem("sk-reader-vlang") || ""; } catch (e) {}
		if (keep && (keep === "" || list.indexOf(keep) >= 0)) st.vlang = keep;
		else st.vlang = list.indexOf("zh") >= 0 ? "zh" : (list.length ? list[0] : "");
		sel.value = st.vlang;
		fillvoice();
	}

	function engof(v){
		return v.engine === "sherpa" ? "sherpa|" + v.model : v.engine;
	}

	function fillvoice(){
		var sel = $("rd-voice");
		if (!sel) return;
		var list = [];
		for (var i = 0; i < st.voices.length; i++) {
			var v = st.voices[i];
			if (engof(v) !== st.engine) continue;
			if (st.vlang && v.lang !== st.vlang) continue;
			list.push(v);
		}
		sel.innerHTML = "";
		for (i = 0; i < list.length; i++) {
			var op = el("option");
			op.value = voiceid(list[i]);
			op.textContent = voiceLabel(list[i]);
			sel.appendChild(op);
		}
		var keep = "";
		try { keep = localStorage.getItem("sk-reader-voice") || ""; } catch (e) {}
		var pick = "";
		for (i = 0; i < list.length; i++) if (voiceid(list[i]) === keep) pick = keep;
		if (!pick && list.length) pick = voiceid(list[0]);
		st.voice = pick;
		sel.value = pick;
		fillrate();
	}

	function voiceid(v){
		if (v.engine === "sherpa") return "sherpa|" + v.model + "|" + v.id;
		return v.key || v.label;
	}

	function voiceLabel(v){
		var tail = "";
		if (v.lang && !st.vlang) tail = " · " + langlabel(v.lang);
		return v.label + tail;
	}

	function curvoice(){
		for (var i = 0; i < st.voices.length; i++) {
			var v = st.voices[i];
			if (voiceid(v) === st.voice) return v;
		}
		return null;
	}

	function fillrate(){
		var inp = $("rd-rate");
		if (inp) inp.value = String(st.rate);
	}

	function savetts(){
		try {
			localStorage.setItem("sk-reader-engine", st.engine || "");
			localStorage.setItem("sk-reader-vlang", st.vlang || "");
			localStorage.setItem("sk-reader-voice", st.voice || "");
			localStorage.setItem("sk-reader-rate", String(st.rate));
		} catch (e) {}
	}

	// ==================== 划词浮窗 ====================
	function wirepop(){
		$("rd-pop-hide").onclick = hidepop;
		$("rd-pop-copy").onclick = function(){
			copytext($("rd-pop-word").textContent);
		};
		$("rd-pop-read").onclick = function(){
			if (!st.selpick) {
				readstart();
				return;
			}
			var pick = st.selpick;
			hidepop();
			try {
				var sel = window.getSelection();
				if (sel) sel.removeAllRanges();
			} catch (e) {}
			readfrom(pick.i, pick.off || 0);
		};
		$("rd-pop-speak").onclick = function(){
			var text = $("rd-pop-word").textContent;
			if (!text) return;
			var lang = st.hit && st.hit.dict ? st.hit.dict : (st.dlang || "");
			speakdict(text, lang);
		};
		on(document, "mousedown", function(ev){
			var body = $("rd-body");
			if (body && body.contains(ev.target)) st.selpick = null;
			var pop = $("rd-pop");
			if (!pop || pop.hidden) return;
			if (pop.contains(ev.target)) return;
			var win = $("rd-dict");
			if (win && win.contains(ev.target)) return;
			hidepop();
		});
		on(document, "mouseup", function(){
			setTimeout(function(){ onselect(false); }, 10);
		});
		on(document, "selectionchange", function(){
			if (!window.matchMedia("(hover: none) and (pointer: coarse)").matches && window.innerWidth > 800) return;
			setTimeout(function(){ onselect(false); }, 30);
		});
		on(document, "keydown", function(ev){
			if (ev.key === "Escape") {
				hidepop();
				if ($("rd-dict") && !$("rd-dict").hidden) $("rd-dict").hidden = true;
			}
			if (ev.key === "F1") {
				ev.preventDefault();
				dicttoggle();
			}
		});
	}

	function onselect(force){
		var pop = $("rd-pop");
		if (!pop) return;
		if ($("rd-dict") && !$("rd-dict").hidden && !force) return;
		var sel = window.getSelection();
		if (!sel) {
			if (force) hidepop();
			return;
		}
		if (sel.isCollapsed && !force) return;
		var text = String(sel).trim().replace(/\s+/g, " ");
		if (text.length > 48) text = text.slice(0, 48);
		if (!text) {
			if (force) hidepop();
			return;
		}
		var node = sel.anchorNode;
		var el = node && node.nodeType === 1 ? node : (node ? node.parentNode : null);
		if (!el || !el.closest) return;
		if (el.closest("#rd-pop, #rd-dict, #rd-books, .rd-bar")) {
			if (!force) hidepop();
			return;
		}
		if (!force && !el.closest("#rd-body")) {
			hidepop();
			return;
		}
		st.selpick = livepick();
		$("rd-pop-word").textContent = text;
		$("rd-pop-items").innerHTML = '<p class="rd-empty">' + esc(L("web.reader.searching", "查询中…")) + "</p>";
		pop.hidden = false;
		placepop(sel);
		dictsearch(text, 8, function(items, err){
			if (pop.hidden || $("rd-pop-word").textContent !== text) return;
			var box = $("rd-pop-items");
			box.innerHTML = "";
			if (err) {
				box.innerHTML = '<p class="rd-empty">' + esc(err) + "</p>";
				placepop(window.getSelection());
				return;
			}
			if (!items || !items.length) {
				box.innerHTML = '<p class="rd-empty">' + esc(L("web.reader.nohit", "没有命中")) + "</p>";
				placepop(window.getSelection());
				return;
			}
			st.hit = items[0];
			for (var i = 0; i < items.length; i++) box.appendChild(pophit(items[i]));
			placepop(window.getSelection());
		});
	}

	function pophit(h){
		var b = el("button");
		b.type = "button";
		b.className = "rd-hit";
		var w = el("span");
		w.className = "rd-hit-w";
		w.textContent = h.title || h.word || "";
		b.appendChild(w);
		if (h.preview) {
			var g = el("span");
			g.className = "rd-hit-g";
			g.textContent = h.preview;
			b.appendChild(g);
		}
		b.onclick = function(){
			st.hit = h;
			hidepop();
			dictopen(h.word || "", h.id);
		};
		return b;
	}

	function placepop(sel){
		var pop = $("rd-pop");
		if (!pop) return;
		var edge = 8;
		var gap = 8;
		var vw = window.innerWidth;
		var vh = window.innerHeight;
		var pw = pop.offsetWidth;
		var ph = pop.offsetHeight;
		var box = selbox(sel);
		var left = edge;
		var top = edge;
		if (box) {
			left = box.left;
			if (left + pw > vw - edge) left = vw - edge - pw;
			if (left < edge) left = edge;
			var below = box.bottom + gap;
			var above = box.top - gap - ph;
			if (below + ph <= vh - edge) top = below;
			else if (above >= edge) top = above;
			else {
				top = vh - edge - ph;
				if (top < edge) top = edge;
			}
		}
		pop.style.left = Math.round(left) + "px";
		pop.style.top = Math.round(top) + "px";
	}

	function selbox(sel){
		if (!sel || !sel.rangeCount) return null;
		var list = sel.getRangeAt(0).getClientRects();
		var box = null;
		var vh = window.innerHeight;
		for (var i = 0; i < list.length; i++) {
			var r = list[i];
			if (!r.width && !r.height) continue;
			if (r.bottom <= 0 || r.top >= vh) continue;
			if (!box) box = { left: r.left, top: r.top, right: r.right, bottom: r.bottom };
			else {
				if (r.left < box.left) box.left = r.left;
				if (r.top < box.top) box.top = r.top;
				if (r.right > box.right) box.right = r.right;
				if (r.bottom > box.bottom) box.bottom = r.bottom;
			}
		}
		return box;
	}

	function hidepop(){
		var pop = $("rd-pop");
		if (pop) pop.hidden = true;
	}

	// ==================== 词典窗口 ====================
	function wiredict(){
		$("rd-dict-close").onclick = function(){ $("rd-dict").hidden = true; };
		$("rd-dict-go").onclick = function(){ dictsearch($("rd-dict-q").value); };
		$("rd-dict-q").onkeydown = function(ev){
			if (ev.key === "Enter") {
				ev.preventDefault();
				dictsearch($("rd-dict-q").value);
			}
			else if (ev.key === "Escape") $("rd-dict").hidden = true;
		};
		var tabs = qa("#rd-dict-tabs button");
		for (var i = 0; i < tabs.length; i++) {
			tabs[i].onclick = function(){
				if (st.dlang) return;
				st.tablang = attr(this, "data-lang") || "all";
				painttabs();
				var q = $("rd-dict-q").value.trim();
				if (q) dictsearch(q);
			};
		}
		painttabs();
		inidrag();
	}

	function painttabs(){
		var v = st.dlang || st.tablang || "all";
		var tabs = qa("#rd-dict-tabs button");
		for (var i = 0; i < tabs.length; i++) {
			var id = attr(tabs[i], "data-lang");
			tabs[i].className = id === v ? "on" : "";
			tabs[i].disabled = !!st.dlang && id !== st.dlang;
		}
	}

	function dicttoggle(){
		var win = $("rd-dict");
		if (!win) return;
		if (win.hidden) dictopen("", 0);
		else win.hidden = true;
	}

	function dictopen(q, id){
		$("rd-dict").hidden = false;
		painttabs();
		if (q) {
			$("rd-dict-q").value = q;
			st.pendingid = id || 0;
			dictsearch(q);
		}
		else {
			$("rd-dict-q").focus();
			if ($("rd-dict-list").innerHTML === "") {
				$("rd-dict-detail").innerHTML = '<p class="rd-empty">' + esc(L("web.reader.dictempty", "输入词语查词典，也可以在正文里划选文字。")) + "</p>";
			}
		}
		plaacedict();
	}

	function plaacedict(){
		var win = $("rd-dict");
		if (!win || win.dataset.moved) return;
		var w = win.offsetWidth || 860;
		var h = win.offsetHeight || 560;
		win.style.left = Math.max(8, Math.round((window.innerWidth - w) / 2)) + "px";
		win.style.top = Math.max(8, Math.round((window.innerHeight - h) / 2)) + "px";
	}

	function inidrag(){
		var bar = $("rd-dict-bar");
		var win = $("rd-dict");
		var dx = 0;
		var dy = 0;
		var dragging = false;
		on(bar, "mousedown", function(ev){
			if (ev.target.closest && ev.target.closest("button")) return;
			dragging = true;
			var r = win.getBoundingClientRect();
			dx = ev.clientX - r.left;
			dy = ev.clientY - r.top;
			ev.preventDefault();
		});
		on(document, "mousemove", function(ev){
			if (!dragging) return;
			win.dataset.moved = "1";
			var x = Math.max(60 - win.offsetWidth, Math.min(ev.clientX - dx, window.innerWidth - 60));
			var y = Math.max(0, Math.min(ev.clientY - dy, window.innerHeight - 40));
			win.style.left = Math.round(x) + "px";
			win.style.top = Math.round(y) + "px";
		});
		on(document, "mouseup", function(){ dragging = false; });
	}

	function usedictlang(){
		return st.dlang || st.tablang || "all";
	}

	function dictsearch(q, limit, cb){
		q = String(q == null ? "" : q).trim();
		if (q.length > 64) q = q.slice(0, 64);
		if (!q) return;
		var url = "/api/dict?limit=" + (limit || 40) + "&dict=" + encodeURIComponent(usedictlang()) + "&q=" + encodeURIComponent(q);
		fetchjson(url).then(function(j){
			if (cb) {
				cb(j && j.code === 100 ? ((j.data && j.data.hits) || []) : null, j && j.code === 100 ? "" : errtext(j));
				return;
			}
			if (!j || j.code !== 100) {
				$("rd-dict-list").innerHTML = '<p class="rd-empty">' + esc(errtext(j)) + "</p>";
				$("rd-dict-detail").innerHTML = "";
				return;
			}
			renderdict(j.data);
		}).catch(function(e){
			var msg = e && e.message ? e.message : "网络错误";
			if (cb) { cb(null, msg); return; }
			$("rd-dict-list").innerHTML = '<p class="rd-empty">' + esc(msg) + "</p>";
		});
	}

	function renderdict(data){
		var hits = (data && data.hits) || [];
		var list = $("rd-dict-list");
		var detail = $("rd-dict-detail");
		$("rd-dict-status").textContent = hits.length
			? L("web.reader.hits", "{0} 条").replace("{0}", String(hits.length)) + (data.ms != null ? " · " + data.ms + " ms" : "")
			: "";
		list.innerHTML = "";
		if (!hits.length) {
			list.innerHTML = '<p class="rd-empty">' + esc(L("web.reader.nohit", "没有命中")) + "</p>";
			detail.innerHTML = "";
			return;
		}
		for (var i = 0; i < hits.length; i++) list.appendChild(dicthit(hits[i]));
		var want = st.pendingid || 0;
		st.pendingid = 0;
		if (want) selecthit(want);
		else opendictentry(hits[0], null);
	}

	function dicthit(h){
		var row = el("div");
		row.className = "rd-dhit";
		row.setAttribute("data-id", String(h.id));
		var top = el("div");
		top.className = "rd-dhit-top";
		var w = el("span");
		w.className = "rd-dhit-w";
		w.textContent = h.title || h.word || "";
		top.appendChild(w);
		if (h.via) {
			var v = el("span");
			v.className = "rd-tag";
			v.textContent = L("web.reader.via", "活用形");
			top.appendChild(v);
		}
		row.appendChild(top);
		if (h.preview) {
			var g = el("div");
			g.className = "rd-dhit-g";
			g.textContent = h.preview;
			row.appendChild(g);
		}
		row.onclick = function(){ opendictentry(h, row); };
		return row;
	}

	function selecthit(id){
		var rows = qa("#rd-dict-list .rd-dhit");
		for (var i = 0; i < rows.length; i++) {
			if (attr(rows[i], "data-id") === String(id)) {
				rows[i].click();
				return;
			}
		}
	}

	function opendictentry(h, row){
		var rows = qa("#rd-dict-list .rd-dhit");
		for (var i = 0; i < rows.length; i++) rows[i].className = "rd-dhit";
		if (row) row.className = "rd-dhit on";
		st.hit = h;
		var detail = $("rd-dict-detail");
		detail.innerHTML = '<p class="rd-empty">' + esc(L("web.reader.loading", "正在打开…")) + "</p>";
		detail.scrollTop = 0;
		fetchjson("/api/dict/entry?id=" + encodeURIComponent(h.id)).then(function(j){
			if (!j || j.code !== 100) {
				detail.innerHTML = '<p class="rd-empty">' + esc(errtext(j)) + "</p>";
				return;
			}
			detail.innerHTML = "";
			detail.appendChild(dictdetail(j.data, h));
		}).catch(function(e){
			detail.innerHTML = '<p class="rd-empty">' + esc(e && e.message ? e.message : "网络错误") + "</p>";
		});
	}

	function dictdetail(e, h){
		var box = el("div");
		box.className = "rd-detail";
		var head = el("div");
		head.className = "rd-ddhead";
		var word = el("strong");
		word.textContent = e.word || e.headword || (h && h.word) || "";
		head.appendChild(word);
		var speak = el("button");
		speak.type = "button";
		speak.className = "rd-mini";
		speak.innerHTML = '<i class="fa-solid fa-volume-high"></i>';
		speak.title = L("dict.speak", "发音");
		speak.onclick = function(){ speakdict(e.speak || e.word || "", e.dict || ""); };
		head.appendChild(speak);
		addtag(head, dictlabel(e.dict));
		if (e.pron) addtag(head, e.pron);
		if (e.reading && e.reading !== e.pron) addtag(head, e.reading);
		if (e.pos) addtag(head, e.pos);
		box.appendChild(head);
		if (e.conjugations && e.conjugations.length) {
			var conj = el("p");
			conj.className = "rd-conj";
			conj.textContent = e.conjugations.join(" · ");
			box.appendChild(conj);
		}
		var senses = e.senses || [];
		for (var i = 0; i < senses.length; i++) box.appendChild(dictsense(senses[i]));
		if (e.extra) {
			var extra = el("p");
			extra.className = "rd-extra";
			extra.textContent = e.extra;
			box.appendChild(extra);
		}
		if (e.etymology) {
			var et = el("p");
			et.className = "rd-extra";
			et.textContent = e.etymology;
			box.appendChild(et);
		}
		if (!senses.length && !e.extra) {
			var empty = el("p");
			empty.className = "rd-empty";
			empty.textContent = L("web.reader.noentry", "这条没有释义");
			box.appendChild(empty);
		}
		return box;
	}

	function addsense(box, label, arr){
		if (!arr || !arr.length) return;
		var wrap = el("div");
		wrap.className = "rd-bits";
		var lab = el("span");
		lab.className = "rd-blabel";
		lab.textContent = label;
		wrap.appendChild(lab);
		for (var i = 0; i < arr.length; i++) {
			var one = arr[i];
			if (!one || !one.text) continue;
			var line = el("div");
			line.className = "rd-bit";
			line.textContent = one.text + (one.zh ? "　" + one.zh : "");
			wrap.appendChild(line);
		}
		box.appendChild(wrap);
	}

	function dictsense(s){
		var box = el("div");
		box.className = "rd-sense";
		var head = el("div");
		head.className = "rd-sense-head";
		if (s.pos) addtag(head, s.pos);
		if (s.idiom) addtag(head, s.idiom);
		if (head.childNodes.length) box.appendChild(head);
		var lines = [
			s.zh ? s.zh + (s.zhDef ? "　" + s.zhDef : "") : "",
			s.en ? s.en + (s.enDef ? "　" + s.enDef : "") : "",
			s.ja ? s.ja + (s.jaDef ? "　" + s.jaDef : "") : "",
			s.ko || ""
		];
		for (var i = 0; i < lines.length; i++) {
			if (!lines[i]) continue;
			var p = el("div");
			p.className = "rd-def";
			p.textContent = lines[i];
			box.appendChild(p);
		}
		addsense(box, L("dict.lab.phrase", "词组"), s.phrases);
		addsense(box, L("dict.lab.example", "例句"), s.sentences);
		return box;
	}

	function dictlabel(code){
		return code === "zh" ? "汉" : code === "en" ? "英" : code === "ja" ? "日" : code === "ko" ? "韩" : (code || "");
	}

	function addtag(box, text){
		if (!text) return;
		var s = el("span");
		s.className = "rd-tag";
		s.textContent = text;
		box.appendChild(s);
	}

	function speakdict(text, lang){
		text = String(text || "").trim();
		if (!text) return;
		var url = "/api/dict/speak?text=" + encodeURIComponent(text) + "&lang=" + encodeURIComponent(lang || "");
		fetch(url).then(function(r){
			var ct = (r.headers.get("Content-Type") || "").toLowerCase();
			if (ct.indexOf("json") >= 0) {
				return r.json().then(function(j){
					setmsg(errtext(j), true);
				});
			}
			return r.blob().then(function(blob){
				var url2 = urlof(blob);
				var audio = new Audio(url2);
				sink(audio);
				audio.onended = function(){ unurl(url2); };
				var p = audio.play();
				if (p && p.then) p.then(function(){}, function(){ unurl(url2); });
			});
		}).catch(function(e){
			setmsg(e && e.message ? e.message : "网络错误", true);
		});
	}

	// ==================== 小工具 ====================
	function copytext(text){
		text = String(text || "");
		if (!text) return;
		if (navigator.clipboard && navigator.clipboard.writeText) {
			navigator.clipboard.writeText(text).then(function(){ setmsg(L("web.reader.copied", "已复制")); }, function(){});
			return;
		}
		var ta = el("textarea");
		ta.value = text;
		document.body.appendChild(ta);
		ta.select();
		try { document.execCommand("copy"); setmsg(L("web.reader.copied", "已复制")); }
		catch (e) {}
		document.body.removeChild(ta);
	}

	function fetchjson(url, opt){
		return fetch(url, opt || {}).then(function(r){
			return r.json().catch(function(){ return { code: 0, data: "响应不是 JSON" }; });
		});
	}

	function errtext(j){
		if (!j) return "失败";
		if (typeof j.data === "string" && j.data) return j.data;
		if (j.error) return String(j.error);
		if (j.code) return "失败 (" + j.code + ")";
		return "失败";
	}

	function extname(name){
		var n = String(name || "").toLowerCase();
		var i = n.lastIndexOf(".");
		return i >= 0 ? n.slice(i) : "";
	}

	function L(key, text){
		var s = t(key);
		return s || text || "";
	}

	function esc(s){
		return String(s == null ? "" : s).replace(/[&<>"']/g, function(c){
			return { "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;" }[c];
		});
	}
	boot();
})();
