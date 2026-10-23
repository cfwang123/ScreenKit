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
		bvoices: [],
		bmark: "",
		selpick: null,
		hit: null,
		hl: null,
		tablang: "all",
		pendingid: 0
	};
	var dcur = null;
	var dgen = 0;
	var maxbox = null;
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
		// 缺译文时回退英文，再回退中文
		var i = langs().indexOf("en");
		if (i >= 0 && row[i]) return row[i];
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
		hookbvoices();
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
		if ($("rd-dict")) {
			painttabs();
			paintmax($("rd-dict").classList.contains("max"));
		}
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
			'<a id="rd-models" class="rd-models" href="/chrome" target="_blank" rel="noopener" hidden data-k="web.reader.models">' + L("web.reader.models", "安装模型") + "</a>",
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
			'<button id="rd-dict-speak" class="rd-mini" type="button" title="' + esc(L("dict.speak", "发音")) + '"><i class="fa-solid fa-volume-high"></i></button>',
			'<button id="rd-dict-max" class="rd-mini" type="button" title="' + esc(L("web.reader.max", "最大化")) + '"><i class="fa-solid fa-expand"></i></button>',
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
			if (st.engine === "browser") setTimeout(next, 40);
			else next();
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
		if ((st.engine || "browser") === "browser") {
			speakbrowser(text, done);
			return;
		}
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
			playwav(b64, function(){ done(""); });
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

	function speakbrowser(text, done){
		var syn = window.speechSynthesis;
		if (!syn || typeof SpeechSynthesisUtterance !== "function") {
			done(L("web.aim.novoice", "这个浏览器不能朗读"));
			return;
		}
		var u = new SpeechSynthesisUtterance(text);
		var v = browservoice(st.voice);
		if (v) {
			u.voice = v;
			if (v.lang) u.lang = v.lang;
		}
		else if (st.vlang) u.lang = st.vlang;
		var rate = st.rate || 1;
		if (rate < 0.5) rate = 0.5;
		if (rate > 2) rate = 2;
		u.rate = rate;
		u.volume = 1;
		var settled = false;
		function finish(err){
			if (settled) return;
			settled = true;
			done(err || "");
		}
		u.onend = function(){ finish(""); };
		u.onerror = function(ev){
			var code = ev && ev.error;
			if (code === "interrupted" || code === "canceled" || code === "cancelled") {
				finish("");
				return;
			}
			finish(L("web.aim.novoice", "这个浏览器不能朗读"));
		};
		try {
			syn.resume();
			syn.speak(u);
		}
		catch (e) {
			finish(e && e.message ? e.message : L("web.aim.novoice", "这个浏览器不能朗读"));
		}
	}

	function browservoice(key){
		var syn = window.speechSynthesis;
		if (!syn || !key || !syn.getVoices) return null;
		var list = syn.getVoices() || [];
		var i;
		for (i = 0; i < list.length; i++) {
			var v = list[i];
			if (v && (v.voiceURI || v.name) === key) return v;
		}
		return null;
	}

	function stopsynth(){
		var syn = window.speechSynthesis;
		if (!syn) return;
		try { syn.cancel(); } catch (e) {}
	}

	function stopread(quiet){
		st.token++;
		st.reading = false;
		stopwav();
		stopsynth();
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
		if (!seen.browser) out.push("browser");
		var rank = { browser: 0, chrome: 1, winrt: 2, sapi: 3, edge: 4 };
		out.sort(function(a, b){
			var ra = rank[a] == null ? (a.indexOf("sherpa|") === 0 ? 5 : 6) : rank[a];
			var rb = rank[b] == null ? (b.indexOf("sherpa|") === 0 ? 5 : 6) : rank[b];
			return ra - rb;
		});
		return out;
	}

	function englabel(id){
		if (id === "browser") return L("web.reader.eng.browser", "浏览器语音");
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
		if (!st.engine) st.engine = list.indexOf("browser") >= 0 ? "browser" : (list[0] || "browser");
		sel.value = st.engine;
		fillvlang();
	}

	function langlabel(code){
		var names = {
			zh: "中文", en: "English", ja: "日本語", ko: "한국어", yue: "粤語", "zh-hk": "粤語",
			"zh-tw": "國語", "zh-cn": "中文", "en-us": "English", "en-gb": "English",
			"ja-jp": "日本語", "ko-kr": "한국어", "yue-hk": "粤語",
			de: "Deutsch", es: "Español", fr: "Français", pt: "Português",
			ru: "Русский", it: "Italiano", hi: "हिन्दी", id: "Bahasa Indonesia", vi: "Tiếng Việt",
			nl: "Nederlands", pl: "Polski", th: "ไทย", ar: "العربية", tr: "Türkçe"
		};
		return names[String(code || "").toLowerCase()] || code;
	}

	function langrank(code){
		var c = String(code || "").toLowerCase();
		var prefer = ["zh-cn", "zh", "zh-tw", "zh-hk", "yue-hk", "yue", "en-us", "en", "en-gb", "ja-jp", "ja", "ko-kr", "ko"];
		var i;
		for (i = 0; i < prefer.length; i++) if (prefer[i] === c) return i;
		return 100;
	}

	function fillvlang(){
		var sel = $("rd-vlang");
		if (!sel) return;
		var src = st.engine === "browser" ? st.bvoices : st.voices;
		var list = [];
		var seen = {};
		for (var i = 0; i < src.length; i++) {
			var v = src[i];
			if (engof(v) !== st.engine) continue;
			var code = v.lang || "";
			if (!code || seen[code]) continue;
			seen[code] = 1;
			list.push(code);
		}
		if (st.engine === "browser") {
			list.sort(function(a, b){
				var d = langrank(a) - langrank(b);
				if (d) return d;
				return a < b ? -1 : a > b ? 1 : 0;
			});
		}
		else list.sort();
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
		paintmodels();
	}

	function paintmodels(){
		var a = $("rd-models");
		if (!a) return;
		a.hidden = st.engine !== "browser";
	}

	function engof(v){
		return v.engine === "sherpa" ? "sherpa|" + v.model : v.engine;
	}

	function fillvoice(){
		var sel = $("rd-voice");
		if (!sel) return;
		var src = st.engine === "browser" ? st.bvoices : st.voices;
		var list = [];
		for (var i = 0; i < src.length; i++) {
			var v = src[i];
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

	function hookbvoices(){
		var syn = window.speechSynthesis;
		if (!syn) return;
		loadbvoices();
		syn.addEventListener("voiceschanged", loadbvoices);
	}

	function loadbvoices(){
		var syn = window.speechSynthesis;
		var list = syn && syn.getVoices ? (syn.getVoices() || []) : [];
		var next = [];
		var sig = [];
		var i;
		for (i = 0; i < list.length; i++) {
			var v = list[i];
			if (!v) continue;
			var key = v.voiceURI || v.name || "";
			if (!key) continue;
			var lang = String(v.lang || "").replace(/_/g, "-");
			next.push({
				engine: "browser",
				key: key,
				label: v.name || key,
				lang: lang
			});
			sig.push(key + "\t" + lang + "\t" + (v.name || ""));
		}
		var mark = sig.join("\n");
		if (mark === st.bmark) return;
		st.bmark = mark;
		st.bvoices = next;
		if (st.built && st.engine === "browser") fillvlang();
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
		$("rd-dict-speak").onclick = function(){
			if (!dcur) return;
			speakdict(dcur.speak || dcur.word || "", dcur.dict || "");
		};
		$("rd-dict-max").onclick = function(){ dictmax(); };
		$("rd-dict-go").onclick = function(){ dictsearch($("rd-dict-q").value); };
		on($("rd-dict-detail"), "click", function(ev){
			var say = ev.target.closest(".spk");
			if (say) {
				speakdict(attr(say, "data-say"), attr(say, "data-lang"));
				return;
			}
			var b = ev.target.closest(".link");
			if (!b) return;
			var q = attr(b, "data-q") || "";
			if (!q) return;
			$("rd-dict-q").value = q;
			dictsearch(q);
		});
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
		iniresize();
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
		if (!win || win.dataset.moved || win.classList.contains("max")) return;
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
			if (win.classList.contains("max")) return;
			dragging = true;
			var r = win.getBoundingClientRect();
			dx = ev.clientX - r.left;
			dy = ev.clientY - r.top;
			ev.preventDefault();
		});
		on(bar, "dblclick", function(ev){
			if (ev.target.closest && ev.target.closest("button")) return;
			dictmax();
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

	/** 四边、四角都能拖动改大小；不能小于 480×280，也不超出窗口。 */
	function iniresize(){
		var win = $("rd-dict");
		var dirs = ["n", "s", "e", "w", "ne", "nw", "se", "sw"];
		for (var i = 0; i < dirs.length; i++) {
			var h = el("div");
			h.className = "rd-rs";
			h.setAttribute("data-dir", dirs[i]);
			win.appendChild(h);
			on(h, "mousedown", resizefn(h));
		}
	}

	function resizefn(handle){
		return function(ev){
			var win = $("rd-dict");
			if (win.classList.contains("max")) return;
			ev.preventDefault();
			ev.stopPropagation();
			var dir = attr(handle, "data-dir") || "";
			var r = win.getBoundingClientRect();
			var start = { x: ev.clientX, y: ev.clientY, left: r.left, top: r.top, right: r.right, bottom: r.bottom };
			var vw = window.innerWidth;
			var vh = window.innerHeight;
			var minw = Math.min(600, vw - 8);
			var minh = Math.min(320, vh - 8);
			function move(e){
				var dx = e.clientX - start.x;
				var dy = e.clientY - start.y;
				var left = start.left;
				var top = start.top;
				var right = start.right;
				var bottom = start.bottom;
				if (dir.indexOf("e") >= 0) right = Math.min(vw, Math.max(left + minw, start.right + dx));
				if (dir.indexOf("w") >= 0) left = Math.max(0, Math.min(start.left + dx, right - minw));
				if (dir.indexOf("s") >= 0) bottom = Math.min(vh, Math.max(top + minh, start.bottom + dy));
				if (dir.indexOf("n") >= 0) top = Math.max(0, Math.min(start.top + dy, bottom - minh));
				win.dataset.moved = "1";
				win.style.left = Math.round(left) + "px";
				win.style.top = Math.round(top) + "px";
				win.style.width = Math.round(right - left) + "px";
				win.style.height = Math.round(bottom - top) + "px";
			}
			function up(){
				document.removeEventListener("mousemove", move);
				document.removeEventListener("mouseup", up);
			}
			document.addEventListener("mousemove", move);
			document.addEventListener("mouseup", up);
		};
	}

	function dictmax(onoff){
		var win = $("rd-dict");
		if (!win) return;
		var on = arguments.length ? !!onoff : !win.classList.contains("max");
		if (on === win.classList.contains("max")) return;
		if (on) {
			maxbox = {
				left: win.style.left,
				top: win.style.top,
				width: win.style.width,
				height: win.style.height,
				moved: win.dataset.moved || ""
			};
			win.classList.add("max");
		}
		else {
			win.classList.remove("max");
			if (maxbox) {
				win.style.left = maxbox.left || "";
				win.style.top = maxbox.top || "";
				win.style.width = maxbox.width || "";
				win.style.height = maxbox.height || "";
				if (maxbox.moved) win.dataset.moved = maxbox.moved;
				else win.removeAttribute("data-moved");
			}
		}
		paintmax(win.classList.contains("max"));
	}

	function paintmax(on){
		var b = $("rd-dict-max");
		if (!b) return;
		b.innerHTML = '<i class="fa-solid ' + (on ? "fa-compress" : "fa-expand") + '"></i>';
		b.title = on ? (L("web.reader.restore", "还原")) : (L("web.reader.max", "最大化"));
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
		$("rd-dict-status").textContent = hits.length ? dictcount(hits.length, data.ms) : "";
		list.innerHTML = "";
		if (!hits.length) {
			list.innerHTML = '<p class="rd-empty">' + esc(L("web.reader.nohit", "没有命中")) + "</p>";
			detail.innerHTML = "";
			dcur = null;
			dgen++;
			return;
		}
		for (var i = 0; i < hits.length; i++) list.appendChild(dicthit(hits[i]));
		var want = st.pendingid || 0;
		st.pendingid = 0;
		if (want) selecthit(want);
		else opendictentry(hits[0], list.children[0]);
	}

	function dictcount(n, ms){
		var s = t("dict.count") || "{0} 条 · {1} ms";
		return s.replace("{0}", String(n)).replace("{1}", String(ms || 0));
	}

	function dictshort(code){
		if (code === "zh") return t("dict.lab.zh") || "中";
		if (code === "en") return t("dict.lab.en") || "英";
		if (code === "ja") return t("dict.lab.ja") || "日";
		if (code === "ko") return t("dict.lab.ko") || "韩";
		return "";
	}

	function dicttitle(h){
		var title = (h && (h.title || h.word)) || "";
		if ((langs()[langAt] || "zh") !== "zh") return title;
		if (title.charAt(0) !== "[") return title;
		var end = title.indexOf("] ");
		if (end < 2 || end > 6) return title;
		var lab = dictshort(title.substring(1, end));
		if (!lab) return title;
		return "[" + lab + "] " + title.substring(end + 2);
	}

	function dictpreview(h){
		var text = h.preview || "";
		if (h.via) {
			var via = (t("dict.via") || "由 {0} 还原").replace("{0}", h.via);
			text = text ? text + "  ·  " + via : via;
		}
		return text;
	}

	function dicthit(h){
		var row = el("div");
		row.className = "rd-dhit";
		row.setAttribute("data-id", String(h.id));
		var txt = el("div");
		txt.className = "hit-txt";
		var w = el("b");
		w.textContent = dicttitle(h);
		var g = el("span");
		g.textContent = dictpreview(h);
		txt.appendChild(w);
		txt.appendChild(g);
		row.appendChild(txt);
		var spk = el("button");
		spk.type = "button";
		spk.className = "spk";
		spk.title = t("dict.speak") || "发音";
		spk.textContent = "🔊";
		spk.onclick = function(ev){
			ev.stopPropagation();
			speakdict(h.speak || h.word, h.dict);
		};
		row.appendChild(spk);
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
		var g = ++dgen;
		detail.innerHTML = '<p class="rd-empty">' + esc(L("web.reader.loading", "正在打开…")) + "</p>";
		detail.scrollTop = 0;
		fetchjson("/api/dict/entry?id=" + encodeURIComponent(h.id)).then(function(j){
			if (g !== dgen) return;
			if (!j || j.code !== 100) {
				detail.innerHTML = '<p class="rd-empty">' + esc(errtext(j)) + "</p>";
				return;
			}
			dcur = j.data;
			detail.innerHTML = "";
			detail.appendChild(dictdetail(j.data));
		}).catch(function(e){
			if (g !== dgen) return;
			detail.innerHTML = '<p class="rd-empty">' + esc(e && e.message ? e.message : "网络错误") + "</p>";
		});
	}

	// 详情按 /dict 的排法画：词头、读音、词性、义项编号、标签、例句、词组。
	function dictdetail(e){
		var box = el("div");
		var word = e.word || "";
		var lang = e.dict || "en";
		var head = dp("title");
		dadd(head, word, "", lang);
		if (e.kanji && e.kanji !== word) dadd(head, "  " + e.kanji, "", lang);
		box.appendChild(head);
		var reading = e.pron || e.reading || "";
		if (reading) box.appendChild(done(dbracket(reading), "pron", lang));
		var article = lang === "zh" && e.extra;
		if (e.pos) box.appendChild(done(e.pos, "pos", dguess(e.pos, lang)));
		if (!article) {
			if (e.usage && e.usage.length) {
				var tags = dp("");
				dadd(tags, t("dict.lab.tags") || "标签：", "lab", "");
				dadd(tags, e.usage.join(" · "), "", dguess(e.usage.join(" · "), lang));
				box.appendChild(tags);
			}
			if (e.etymology) {
				box.appendChild(done(t("dict.lab.etym") || "语源", "lab", ""));
				deachline(e.etymology, function(line){ box.appendChild(done(line, "", dguess(line, lang))); });
			}
			for (var ci = 0; e.conjugations && ci < e.conjugations.length; ci++) {
				var cp = dp("");
				dadd(cp, t("dict.lab.conj") || "活用：", "lab", "");
				dadd(cp, e.conjugations[ci], "", lang);
				box.appendChild(cp);
			}
			if (e.see && e.see.length) {
				var sp = dp("");
				dadd(sp, (t("dict.lab.see") || "参见") + " ", "lab", "");
				var first = true;
				for (var si = 0; si < e.see.length; si++) {
					if (!e.see[si]) continue;
					if (!first) dadd(sp, " · ", "", "");
					first = false;
					dlink(sp, e.see[si], e.see[si], lang);
				}
				box.appendChild(sp);
			}
			var n = 1;
			var lastpos = "";
			var any = false;
			var linkphrase = lang !== "ko";
			var senses = e.senses || [];
			for (var k = 0; k < senses.length; k++) {
				var s = senses[k];
				if (s.idiom) {
					if (any) box.appendChild(dp("gap"));
					var ip = dp("");
					dadd(ip, (t("dict.lab.phrase") || "词组") + " ", "lab", "");
					if (linkphrase) dlink(ip, s.idiom, dfirsttoken(s.idiom), lang);
					else dadd(ip, s.idiom, "", lang);
					box.appendChild(ip);
				}
				else if (any && !s.sub) box.appendChild(dp("gap"));
				any = true;
				if (s.pos && s.pos !== e.pos && s.pos !== lastpos) {
					box.appendChild(done("▶ " + s.pos, "pos", dguess(s.pos, lang)));
					lastpos = s.pos;
					n = 1;
				}
				var numbered = false;
				function gloss(tag, lemma, def, glang){
					if (!lemma && !def) return;
					var gp = dp("");
					if (!numbered && !s.sub) {
						dadd(gp, n + ". ", "num", "");
						numbered = true;
						n++;
					}
					if (tag) dadd(gp, tag + " ", "lab", glang);
					dadd(gp, djoin2(lemma, def), "", glang);
					box.appendChild(gp);
				}
				if (s.ko) gloss("", s.ko, "", "ko");
				gloss(t("dict.lab.zh") || "中", s.zh, s.zhDef, "zh");
				gloss(t("dict.lab.en") || "英", s.en, s.enDef, "en");
				gloss(t("dict.lab.ja") || "日", s.ja, s.jaDef, "ja");
				for (var pi = 0; s.phrases && pi < s.phrases.length; pi++) {
					var ph = s.phrases[pi];
					var pp = dp("");
					dadd(pp, (t("dict.lab.phrase") || "词组") + " ", "lab", "");
					if (linkphrase && ph.text) dlink(pp, ph.text, dfirsttoken(ph.text), lang);
					else dadd(pp, ph.text || "", "", lang);
					if (!linkphrase) {
						var psay = dexamplesrc(ph.text);
						if (psay) dspk(pp, psay, lang);
					}
					box.appendChild(pp);
					if (ph.zh) {
						var zp = dp("");
						dadd(zp, (t("dict.lab.zh") || "中") + " ", "lab", "zh");
						dadd(zp, ph.zh, "", "zh");
						box.appendChild(zp);
					}
				}
				if (s.sentences && s.sentences.length)
					box.appendChild(done(t("dict.lab.example") || "例句", "exlab", ""));
				for (var ei = 0; s.sentences && ei < s.sentences.length; ei++) {
					var ex = s.sentences[ei];
					var ep = dp("ex");
					dadd(ep, "· " + (ex.text || ""), "ex", lang);
					var esay = dexamplesrc(ex.text);
					if (esay) dspk(ep, esay, lang);
					box.appendChild(ep);
					if (ex.zh) {
						var ez = dp("");
						dadd(ez, (t("dict.lab.zh") || "中") + " ", "lab", "zh");
						dadd(ez, ex.zh, "", "zh");
						box.appendChild(ez);
					}
				}
				if (!s.sub && !numbered && (!s.phrases || !s.phrases.length) && (!s.sentences || !s.sentences.length)) {
					var empty = dp("");
					dadd(empty, n + ". ", "num", "");
					n++;
					dadd(empty, t("dict.empty.sense") || "（无释义）", "pron", "");
					box.appendChild(empty);
				}
				else if (!s.sub && !numbered) {
					box.appendChild(done(n + ".", "num", ""));
					n++;
				}
			}
		}
		if (e.extra && (article || lang !== "zh")) {
			if (box.childNodes.length) box.appendChild(dp("gap"));
			deachline(e.extra, function(line){
				box.appendChild(done(line, "", article ? "zh" : dguess(line, lang)));
			});
		}
		box.scrollTop = 0;
		return box;
	}

	function dp(cls){
		var node = el("p");
		if (cls) node.className = cls;
		return node;
	}
	function done(text, cls, lang){
		var node = dp(cls);
		dadd(node, text, cls, lang);
		return node;
	}
	function dadd(parent, text, cls, lang){
		if (!text) return;
		var s = el("span");
		if (cls) s.className = cls;
		if (lang) s.setAttribute("data-lang", lang);
		s.textContent = text;
		parent.appendChild(s);
	}
	function dlink(parent, label, q, lang){
		var b = el("button");
		b.type = "button";
		b.className = "link";
		b.setAttribute("data-q", q || label);
		if (lang) b.setAttribute("data-lang", lang);
		b.textContent = label;
		parent.appendChild(b);
	}
	function dspk(parent, text, lang){
		parent.appendChild(document.createTextNode(" "));
		var b = el("button");
		b.type = "button";
		b.className = "spk";
		b.setAttribute("data-say", text);
		b.setAttribute("data-lang", lang || "");
		b.textContent = "🔊";
		parent.appendChild(b);
	}

	/** 按文字里出现的中日韩汉字、假名、谚文、拉丁字母猜语言。 */
	function dguess(text, fallback){
		var s = String(text || "");
		var score = [0, 0, 0, 0];
		var allowJa = fallback === "ja";
		var han = 0;
		var kana = 0;
		var hang = 0;
		var lat = 0;
		for (var i = 0; i < s.length; i++) {
			var c = s.charCodeAt(i);
			if ((c >= 0x3040 && c <= 0x30FF) || (c >= 0xFF66 && c <= 0xFF9D)) kana++;
			else if (c >= 0xAC00 && c <= 0xD7A3) hang++;
			else if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF)) han++;
			else if ((c >= 65 && c <= 90) || (c >= 97 && c <= 122)) lat++;
		}
		if (kana > 0 && allowJa) score[2] += kana + han;
		else score[0] += han;
		score[3] += hang;
		score[1] += lat;
		var names = ["zh", "en", "ja", "ko"];
		var best = -1;
		var top = 0;
		for (i = 0; i < 4; i++) if (score[i] > top) { top = score[i]; best = i; }
		if (best < 0) return fallback || "en";
		return names[best];
	}
	function dbracket(s){
		if (!s) return s;
		if (s.charAt(0) === "[" || s.charAt(0) === "［") return s;
		return "[" + s + "]";
	}
	function djoin2(a, b){
		if (!a) return b || "";
		if (!b) return a;
		return a + "  " + b;
	}
	function dexamplesrc(text){
		var s = String(text || "").replace(/^\s+|\s+$/g, "").replace(/^·/, "").replace(/^\s+/, "");
		var i = s.indexOf("  ");
		if (i > 0) s = s.substring(0, i).replace(/\s+$/g, "");
		return s;
	}
	function dfirsttoken(phrase){
		var s = String(phrase || "").replace(/^\s+|\s+$/g, "");
		var i = s.indexOf(" ");
		var j = s.indexOf("　");
		if (i < 0 || (j >= 0 && j < i)) i = j;
		if (i <= 0) return s;
		return s.substring(0, i);
	}
	function deachline(text, fn){
		var parts = String(text || "").replace(/\r\n/g, "\n").replace(/\r/g, "\n").split("\n");
		for (var i = 0; i < parts.length; i++) fn(parts[i]);
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
