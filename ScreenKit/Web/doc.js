// 文档阅读器独立页：一次拖入多个 md/docx/pdf，每个文件一个标签。
// 转换仍走 C# 的 /api/docview，和桌面阅读器、网页工具集里那个是同一份实现。
(function(){
	var MAXNAME = 26;
	var THEME = "light";
	var tabs = [];
	var cur = null;
	var seq = 0;
	var pics = null;
	var PICBOX = null;

	var tabsEl = document.getElementById("tabs");
	var stageEl = document.getElementById("stage");
	var viewEl = document.getElementById("view");
	var dropEl = document.getElementById("drop");
	var emptyEl = document.getElementById("empty");
	var tipEl = document.getElementById("tip");
	var fileEl = document.getElementById("file");

	// ==================== 文案 ====================
	var langs = (window.L && window.L._lang && window.L._lang.length)
		? window.L._lang : ["zh"];
	var langAt = 0;
	var i;

	function t(key, fallback){
		var row = window.L && window.L[key];
		if (!row || !row.length) return fallback || "";
		var s = row[langAt];
		if (s) return s;
		var en = langs.indexOf("en");
		if (en >= 0 && row[en]) return row[en];
		return row[0] || fallback || "";
	}

	function initlang(){
		var use = String(window.SKL || "").toLowerCase();
		for (i = 0; i < langs.length; i++) if (langs[i] === use) langAt = i;
		var nodes = document.querySelectorAll("[data-k]");
		var j, s;
		for (j = 0; j < nodes.length; j++) {
			s = t(nodes[j].getAttribute("data-k"), "");
			if (s) nodes[j].textContent = s;
		}
		document.documentElement.lang = langs[langAt] === "zh" ? "zh-CN" : (langs[langAt] || "zh");
		document.title = t("web.tool.mdread", "文档阅读器");
	}

	// ==================== 提示 ====================
	var tipTok = 0;
	function say(text){
		tipEl.textContent = text || "";
		tipEl.className = "on";
		var mine = ++tipTok;
		setTimeout(function(){ if (mine === tipTok) tipEl.className = ""; }, 1600);
	}

	// ==================== 文件 ====================
	function ismd(name){
		var n = (name || "").toLowerCase();
		return n.endsWith(".md") || n.endsWith(".markdown") || n.endsWith(".mdown") || n.endsWith(".mkd");
	}

	function isdoc(name){
		var n = (name || "").toLowerCase();
		return ismd(n) || n.endsWith(".docx") || n.endsWith(".pdf");
	}

	function kindof(name){
		if (ismd(name)) return "md";
		return (name || "").toLowerCase().endsWith(".pdf") ? "pdf" : "docx";
	}

	function find(name){
		for (var k = 0; k < tabs.length; k++) if (tabs[k].name === name) return tabs[k];
		return null;
	}

	// 一次来的多个文件：已打开的跳过，其余排队转换，最后进来的留在前台
	function takeall(files){
		if (!files || !files.length) return;
		var list = [];
		var bad = 0;
		var k;
		for (k = 0; k < files.length; k++) {
			var f = files[k];
			if (!isdoc(f.name)) { bad++; continue; }
			var old = find(f.name);
			if (old) { list.push(old); continue; }
			var tab = {
				id: ++seq, name: f.name || "", kind: kindof(f.name),
				state: "load", url: "", pages: 0, file: f, error: ""
			};
			tabs.push(tab);
			list.push(tab);
			send(tab);
		}
		painttabs();
		if (list.length) activate(list[list.length - 1]);
		if (bad && !list.length) say(t("web.mdread.bad", "只能打开 Markdown、Word 或 PDF"));
		else if (bad) say(t("web.mdread.bad.n", "有文件格式不支持，已跳过"));
	}

	function send(tab){
		var xhr = new XMLHttpRequest();
		var url = "/api/docview?name=" + encodeURIComponent(tab.name)
			+ "&theme=" + encodeURIComponent(THEME);
		xhr.open("POST", url, true);
		xhr.setRequestHeader("Content-Type", "application/octet-stream");
		xhr.onload = function(){
			var jo;
			try { jo = JSON.parse(xhr.responseText); }
			catch (e) { fail(tab, t("web.mdread.fail", "打开失败")); return; }
			if (!jo || jo.code !== 100) {
				fail(tab, jo && typeof jo.data === "string" && jo.data ? jo.data : t("web.mdread.fail", "打开失败"));
				return;
			}
			var d = jo.data || {};
			tab.state = "ok";
			tab.pages = d.pages || 0;
			tab.url = "/api/docasset?id=" + encodeURIComponent(d.id || "") + "&f=page.html";
			if (cur === tab) showtab(tab);
			painttabs();
		};
		xhr.onerror = function(){ fail(tab, t("web.mdread.fail", "打开失败")); };
		xhr.send(tab.file);
	}

	function fail(tab, text){
		tab.state = "bad";
		tab.error = text || "";
		if (cur === tab) showtab(tab);
		painttabs();
	}

	function showtab(tab){
		if (!tab) return;
		emptyEl.hidden = true;
		if (tab.state === "ok") {
			// srcdoc 一旦设过就压过 src，先摘掉再换地址
			viewEl.removeAttribute("srcdoc");
			if (viewEl.getAttribute("src") !== tab.url) viewEl.setAttribute("src", tab.url);
			return;
		}
		viewEl.removeAttribute("src");
		if (tab.state === "load") viewEl.srcdoc = placeholder(t("web.mdread.loading", "正在读取…"));
		else viewEl.srcdoc = placeholder(tab.error || t("web.mdread.fail", "打开失败"));
	}

	function placeholder(text){
		return '<!DOCTYPE html><html><head><meta charset="utf-8"><style>'
			+ 'html,body{height:100%;margin:0}body{display:flex;align-items:center;justify-content:center;'
			+ 'font:14px/1.7 "Segoe UI","Microsoft YaHei UI",sans-serif;color:#8b949e;background:#fff;'
			+ 'text-align:center;padding:24px;box-sizing:border-box}</style></head><body>'
			+ esc(text) + "</body></html>";
	}

	function activate(tab){
		if (!tab) return;
		cur = tab;
		painttabs();
		showtab(tab);
	}

	// ==================== 标签条 ====================
	function painttabs(){
		var k;
		tabsEl.hidden = tabs.length === 0;
		document.body.className = tabs.length ? "hastabs" : "";
		while (tabsEl.firstChild) tabsEl.removeChild(tabsEl.firstChild);
		for (k = 0; k < tabs.length; k++) tabsEl.appendChild(maketab(tabs[k]));
	}

	function maketab(tab){
		var chip = document.createElement("div");
		chip.className = "doc-tab" + (tab === cur ? " on" : "") + (tab.state === "load" ? " wait" : "");
		chip.title = tab.name;
		var lb = document.createElement("span");
		lb.className = "doc-tabname";
		lb.textContent = shorten(tab.name);
		chip.appendChild(lb);
		var x = document.createElement("button");
		x.type = "button";
		x.className = "doc-tabx";
		x.title = t("mdread.tabclose", "关闭");
		x.textContent = "×";
		x.onclick = function(ev){ ev.stopPropagation(); closetab(tab); };
		chip.appendChild(x);
		chip.onclick = function(){ if (tab !== cur) activate(tab); };
		chip.onmousedown = function(ev){
			if (ev.button === 1) { ev.preventDefault(); closetab(tab); }
		};
		return chip;
	}

	function shorten(name){
		if (!name) return "";
		return name.length > MAXNAME ? name.substring(0, MAXNAME - 1) + "…" : name;
	}

	function closetab(tab){
		var k = tabs.indexOf(tab);
		if (k < 0) return;
		tabs.splice(k, 1);
		tab.file = null;
		tab.url = "";
		if (!tabs.length) {
			cur = null;
			viewEl.removeAttribute("src");
			viewEl.srcdoc = "";
			emptyEl.hidden = false;
			painttabs();
			closepic();
			return;
		}
		activate(tabs[Math.min(k, tabs.length - 1)]);
	}

	// ==================== 点图放大 ====================
	// 文档页在 iframe 里，点图时它把图片地址发上来，这里用 Viewer.js 弹层
	window.addEventListener("message", function(ev){
		var d = ev.data;
		if (!d || d.sk !== "docimg" || !d.src) return;
		showpic(d.list && d.list.length ? d.list : [d.src], d.index || 0);
	});

	function showpic(list, index){
		if (!window.Viewer) return;
		if (!PICBOX) {
			PICBOX = document.createElement("div");
			PICBOX.className = "doc-pics";
			document.body.appendChild(PICBOX);
		}
		closepic();
		var k, im;
		for (k = 0; k < list.length; k++) {
			im = document.createElement("img");
			im.src = list[k];
			im.alt = list.length > 1 ? (k + 1) + " / " + list.length : "";
			PICBOX.appendChild(im);
		}
		pics = new Viewer(PICBOX, {
			transition: false,
			url: "src",
			initialViewIndex: index,
			navbar: list.length > 1,
			title: list.length > 1 ? function(image){ return image.alt || ""; } : false,
			toolbar: {
				zoomIn: 1, zoomOut: 1, oneToOne: 1, reset: 1,
				prev: list.length > 1 ? 1 : 0, play: 0, next: list.length > 1 ? 1 : 0,
				rotateLeft: 1, rotateRight: 1, flipHorizontal: 0, flipVertical: 0
			},
			hidden: function(){ while (PICBOX.firstChild) PICBOX.removeChild(PICBOX.firstChild); }
		});
		pics.view(index);
	}

	function closepic(){
		if (!pics) return;
		try { pics.destroy(); } catch (e) { }
		pics = null;
		if (PICBOX) while (PICBOX.firstChild) PICBOX.removeChild(PICBOX.firstChild);
	}

	// Viewer 只在全屏时响应 Esc，补一个随时能关的
	window.addEventListener("keydown", function(ev){
		if (ev.key !== "Escape" && ev.keyCode !== 27) return;
		if (pics) pics.hide();
	});

	// ==================== 拖入与选择 ====================
	var dragging = 0;

	function isfile(ev){
		var dt = ev.dataTransfer;
		if (!dt) return false;
		if (dt.types) {
			for (var k = 0; k < dt.types.length; k++) if (dt.types[k] === "Files") return true;
		}
		return false;
	}

	window.addEventListener("dragenter", function(ev){
		if (!isfile(ev)) return;
		ev.preventDefault();
		dragging++;
		dropEl.hidden = false;
	});

	window.addEventListener("dragover", function(ev){
		if (!isfile(ev)) return;
		ev.preventDefault();
		ev.dataTransfer.dropEffect = "copy";
	});

	window.addEventListener("dragleave", function(ev){
		if (!isfile(ev)) return;
		dragging = Math.max(0, dragging - 1);
		if (!dragging) dropEl.hidden = true;
	});

	window.addEventListener("drop", function(ev){
		if (!isfile(ev)) return;
		ev.preventDefault();
		dragging = 0;
		dropEl.hidden = true;
		var files = ev.dataTransfer && ev.dataTransfer.files;
		if (files && files.length) takeall(files);
	});

	// 双击空白处选文件；Ctrl+O 也行
	stageEl.addEventListener("dblclick", function(ev){
		if (ev.target !== stageEl && ev.target !== emptyEl && ev.target !== viewEl) return;
		fileEl.click();
	});

	fileEl.onchange = function(){
		takeall(fileEl.files);
		fileEl.value = "";
	};

	window.addEventListener("keydown", function(ev){
		if ((ev.ctrlKey || ev.metaKey) && (ev.key === "o" || ev.key === "O")) {
			ev.preventDefault();
			fileEl.click();
		}
	});

	// 拖文件到页面外时浏览器会直接打开，挡掉
	window.addEventListener("dragover", function(ev){ if (isfile(ev)) ev.preventDefault(); });
	window.addEventListener("drop", function(ev){ if (isfile(ev)) ev.preventDefault(); });

	function esc(s){
		return String(s == null ? "" : s)
			.replace(/&/g, "&amp;").replace(/</g, "&lt;")
			.replace(/>/g, "&gt;").replace(/"/g, "&quot;");
	}

	initlang();
	painttabs();
})();
