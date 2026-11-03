(function(){
	// 文档阅读器：一次拖入多个 md/docx/pdf，每个文件一个标签页。
	// 转换在 C# 端做（和 WPF 阅读器同一份实现），这里只管排队、切标签和显示。
	var page = null;
	var hooked = false;
	var tabs = [];
	var cur = null;
	var seq = 0;
	var pics = null;
	var THEMES = ["light", "dark", "sepia", "green", "night"];
	var MAXNAME = 26;

	SK.regutil("u-mdread", {
		title: "文档阅读器",
		own: true,
		hideGo: true,
		hideCopy: true,
		hideOut: true,
		body: body,
		open: open,
		dispose: dispose
	});

	function body(t){
		page = t;
		var hint = SK.t("web.mdread.hint") || "";
		var openLabel = SK.t("web.mdread.open") || "打开";
		return F.CreateDOM(t, { x: "div.mdread", c: [
			{ x: "div.mdread-bar", c: [
				{ x: "button.md-open", name: "mdOpen", type: "button", c: openLabel },
				{ x: "input.md-file", name: "mdFile", type: "file", hidden: true, multiple: true,
					accept: ".md,.markdown,.mdown,.mkd,.docx,.pdf" },
				{ x: "select.md-theme.mdtheme", name: "mdTheme", title: SK.t("mdread.theme") || "" },
				{ x: "span.md-name", name: "mdName", "data-k": "web.mdread.hint", c: hint }
			] },
			{ x: "div.mdread-tabs", name: "mdTabs" },
			{ x: "div.mdread-stage", c: [
				{ x: "iframe.md-view", name: "mdView", title: "preview",
					sandbox: "allow-popups allow-scripts allow-same-origin" },
				{ x: "div.md-mask.mdread-mask", name: "mdMask" }
			] },
			// 点图放大用的宿主：Viewer.js 直接挂在这个容器上
			{ x: "div.mdread-pics", name: "mdPics", hidden: true }
		] });
	}

	function open(t){
		page = t;
		var btn = page.doms.mdOpen;
		var file = page.doms.mdFile;
		if (btn && file) btn.onclick = function(){ file.click(); };
		if (file) file.onchange = function(){
			takeall(file.files);
			file.value = "";
		};
		filltheme();
		var sel = page.doms.mdTheme;
		if (sel) sel.onchange = function(){
			savetheme(sel.value);
			retheme();
		};
		if (hooked) return;
		hooked = true;
		F.on(document, "dragenter", arm);
		F.on(document, "dragover", arm);
		F.on(document, "drop", ondrop);
		F.on(document, "dragend", disarm);
		F.on(window, "message", onmsg);
	}

	// 文档页在 iframe 里，点图时它把地址发上来，这里用 Viewer.js 弹层放大
	function onmsg(ev){
		var d = ev.data;
		if (!d || d.sk !== "docimg" || !d.src) return;
		showpic(d.list && d.list.length ? d.list : [d.src], d.index || 0);
	}

	function showpic(list, index){
		var box = page && page.doms.mdPics;
		if (!box || !window.Viewer) return;
		// 每个标签的图片各不相同，销毁重建最省事；重建前先把上一批写进去
		if (pics) {
			try { pics.destroy(); } catch (e) { }
			pics = null;
		}
		while (box.firstChild) box.removeChild(box.firstChild);
		var i, im;
		for (i = 0; i < list.length; i++) {
			im = F.el("img");
			im.src = list[i];
			// 弹层标题就用序号，图片自身的文件名在 /api/docasset 地址里看不出什么
			im.alt = list.length > 1 ? (i + 1) + " / " + list.length : "";
			box.appendChild(im);
		}
		pics = new Viewer(box, {
			transition: false,
			url: "src",
			initialViewIndex: index,
			navbar: list.length > 1 ? true : false,
			// 单张不显示标题，多张显示「第几张/共几张」；默认标题会把地址里的文件名抖出来
			title: list.length > 1 ? function(image){
				return image.alt || "";
			} : false,
			toolbar: {
				zoomIn: 1,
				zoomOut: 1,
				oneToOne: 1,
				reset: 1,
				prev: list.length > 1 ? 1 : 0,
				play: 0,
				next: list.length > 1 ? 1 : 0,
				rotateLeft: 1,
				rotateRight: 1,
				flipHorizontal: 0,
				flipVertical: 0
			},
			hidden: function(){
				// 关掉弹层就把图卸掉，免得下次打开还留着上一份
				while (box.firstChild) box.removeChild(box.firstChild);
			}
		});
		pics.view(index);
		// Viewer 自己只在全屏时响应 Esc，这里补一个随时能关的
		F.on(window, "keydown", function(ev){
			if (ev.key !== "Escape" && ev.keyCode !== 27) return;
			if (pics) pics.hide();
		});
	}

	// 离开工具时清掉标签，再进来是干净的
	function dispose(){
		var i;
		if (pics) {
			try { pics.destroy(); } catch (e) { }
			pics = null;
		}
		if (page && page.doms.mdPics) {
			while (page.doms.mdPics.firstChild) page.doms.mdPics.removeChild(page.doms.mdPics.firstChild);
		}
		for (i = 0; i < tabs.length; i++) free(tabs[i]);
		tabs = [];
		cur = null;
		if (page && page.doms.mdName) {
			F.seta(page.doms.mdName, "data-k", "web.mdread.hint");
			page.doms.mdName.textContent = SK.t("web.mdread.hint") || "";
		}
		painttabs();
	}

	function filltheme(){
		var sel = page.doms.mdTheme;
		if (!sel) return;
		while (sel.firstChild) sel.removeChild(sel.firstChild);
		var i;
		for (i = 0; i < THEMES.length; i++) {
			var op = F.el("option");
			op.value = THEMES[i];
			op.textContent = SK.t("mdread.theme." + THEMES[i]) || THEMES[i];
			sel.appendChild(op);
		}
		sel.value = loadtheme();
	}

	function loadtheme(){
		var v = "";
		try { v = F.lget("sk-mdtheme") || ""; } catch (e) { v = ""; }
		var i;
		for (i = 0; i < THEMES.length; i++) if (THEMES[i] === v) return v;
		return THEMES[0];
	}

	function savetheme(v){
		try { F.lset("sk-mdtheme", v || THEMES[0]); } catch (e) { }
	}

	function arm(ev){
		var mask = page.doms.mdMask;
		if (!mask) return;
		F.pd(ev);
		mask.className = "mdread-mask on";
	}

	function disarm(){
		var mask = page.doms.mdMask;
		if (mask) mask.className = "mdread-mask";
	}

	function ondrop(ev){
		var mask = page.doms.mdMask;
		if (!mask) return;
		F.pd(ev);
		disarm();
		var files = ev.dataTransfer && ev.dataTransfer.files;
		if (files && files.length) takeall(files);
	}

	function ismd(name){
		var n = (name || "").toLowerCase();
		return n.endsWith(".md") || n.endsWith(".markdown") || n.endsWith(".mdown") || n.endsWith(".mkd");
	}

	function isdoc(name){
		var n = (name || "").toLowerCase();
		return ismd(n) || n.endsWith(".docx") || n.endsWith(".pdf");
	}

	// 一次来的多个文件：已打开的跳过，其余排队转换，最后进来的那个留在前台
	function takeall(files){
		if (!files || !files.length) return;
		var list = [];
		var bad = 0;
		var i;
		for (i = 0; i < files.length; i++) {
			var f = files[i];
			if (!isdoc(f.name)) { bad++; continue; }
			var old = find(f.name);
			if (old) { list.push(old); continue; }
			var tab = {
				id: ++seq, name: f.name || "", kind: kindof(f.name),
				state: "load", html: "", url: "", pages: 0, file: f, error: ""
			};
			tabs.push(tab);
			list.push(tab);
			send(tab);
		}
		painttabs();
		if (list.length) activatetab(list[list.length - 1]);
		if (bad && !list.length) SK.msg("box-msg", SK.t("web.mdread.bad") || "", true);
		else if (bad) SK.msg("box-msg", SK.t("web.mdread.bad.n") || "", true);
		else SK.msg("box-msg", "");
	}

	function kindof(name){
		if (ismd(name)) return "md";
		var n = (name || "").toLowerCase();
		return n.endsWith(".pdf") ? "pdf" : "docx";
	}

	function find(name){
		var i;
		for (i = 0; i < tabs.length; i++) if (tabs[i].name === name) return tabs[i];
		return null;
	}

	// 把文件交给后端转换，回来的是整页 HTML 的地址
	function send(tab){
		var xhr = new XMLHttpRequest();
		var theme = page && page.doms.mdTheme ? page.doms.mdTheme.value : "";
		var url = "/api/docview?name=" + encodeURIComponent(tab.name)
			+ "&theme=" + encodeURIComponent(theme);
		xhr.open("POST", url, true);
		F.hdr(xhr, "Content-Type", "application/octet-stream");
		xhr.onload = function(){
			var jo;
			try { jo = F.parse(xhr.responseText); }
			catch (e) { fail(tab, SK.t("web.mdread.fail") || ""); return; }
			if (!jo || jo.code !== 100) {
				fail(tab, jo && typeof jo.data === "string" && jo.data ? jo.data : (SK.t("web.mdread.fail") || ""));
				return;
			}
			var d = jo.data || {};
			tab.state = "ok";
			tab.html = d.html || "";
			tab.id = d.id || "";
			tab.pages = d.pages || 0;
			tab.url = "/api/docasset?id=" + encodeURIComponent(d.id || "") + "&f=page.html";
			if (cur === tab) showtab(tab);
			showstat();
			painttabs();
		};
		xhr.onerror = function(){ fail(tab, SK.t("web.mdread.fail") || ""); };
		xhr.send(tab.file);
	}

	function fail(tab, text){
		tab.state = "bad";
		tab.error = text || "";
		if (cur === tab) showtab(tab);
		painttabs();
	}

	function showtab(tab){
		var view = page && page.doms.mdView;
		if (!view || !tab) return;
		if (tab.state === "ok") {
			// srcdoc 一旦设过就压过 src，先摘掉它再换地址，否则一直停在占位页
			view.removeAttribute("srcdoc");
			if (view.getAttribute("src") !== tab.url) view.setAttribute("src", tab.url);
			return;
		}
		view.removeAttribute("src");
		if (tab.state === "load") view.srcdoc = placeholder(SK.t("web.mdread.loading") || "");
		else view.srcdoc = placeholder(tab.error || SK.t("web.mdread.fail") || "");
	}

	function placeholder(text){
		return '<!DOCTYPE html><html><head><meta charset="utf-8"><style>'
			+ 'html,body{height:100%;margin:0}body{display:flex;align-items:center;justify-content:center;'
			+ 'font:14px/1.7 "Segoe UI","Microsoft YaHei UI",sans-serif;color:#8b949e;background:#f0f2f5;'
			+ 'text-align:center;padding:24px;box-sizing:border-box}</style></head><body>'
			+ esc(text) + "</body></html>";
	}

	function activatetab(tab){
		if (!tab) return;
		cur = tab;
		showstat();
		painttabs();
		showtab(tab);
	}

	// 换主题：Markdown 的排版随主题走，重新转一次；docx/pdf 与主题无关，只重刷当前页
	function retheme(){
		if (!cur) return;
		if (cur.kind !== "md" || !cur.file) {
			showtab(cur);
			return;
		}
		cur.state = "load";
		showtab(cur);
		painttabs();
		SK.msg("box-msg", SK.t("web.mdread.loading") || "");
		send(cur);
	}

	// ==================== 标签条 ====================
	function painttabs(){
		var host = page && page.doms.mdTabs;
		if (!host) return;
		host.hidden = tabs.length === 0;
		while (host.firstChild) host.removeChild(host.firstChild);
		var i;
		for (i = 0; i < tabs.length; i++) host.appendChild(maketab(tabs[i]));
	}

	function maketab(tab){
		var chip = F.el("div");
		chip.className = "mdread-tab" + (tab === cur ? " on" : "") + (tab.state === "load" ? " wait" : "");
		chip.title = tab.name;
		var lb = F.el("span");
		lb.className = "mdread-tabname";
		lb.textContent = shorten(tab.name);
		chip.appendChild(lb);
		var x = F.el("button");
		x.type = "button";
		x.className = "mdread-tabx";
		x.title = SK.t("mdread.tabclose") || "";
		x.textContent = "×";
		x.onclick = function(ev){
			ev.stopPropagation();
			closetab(tab);
		};
		chip.appendChild(x);
		chip.onclick = function(){
			if (tab !== cur) activatetab(tab);
		};
		chip.onmousedown = function(ev){
			if (ev.button === 1) {
				ev.preventDefault();
				closetab(tab);
			}
		};
		return chip;
	}

	function shorten(name){
		if (!name) return "";
		return name.length > MAXNAME ? name.substring(0, MAXNAME - 1) + "…" : name;
	}

	function closetab(tab){
		var i = tabs.indexOf(tab);
		if (i < 0) return;
		tabs.splice(i, 1);
		free(tab);
		if (!tabs.length) {
			cur = null;
			var view = page.doms.mdView;
			if (view) {
				view.removeAttribute("src");
				view.srcdoc = "";
			}
			showstat();
			painttabs();
			return;
		}
		activatetab(tabs[Math.min(i, tabs.length - 1)]);
	}

	// 临时目录由后端按数量清理，这里只断开引用
	function free(tab){
		tab.html = "";
		tab.url = "";
		tab.file = null;
	}

	function showstat(){
		var el = page && page.doms.mdName;
		if (!el) return;
		if (!cur) {
			F.seta(el, "data-k", "web.mdread.hint");
			el.textContent = SK.t("web.mdread.hint") || "";
			return;
		}
		var parts = [cur.name, cur.kind];
		if (cur.kind === "pdf" && cur.pages) parts.push(cur.pages + " " + (SK.t("web.mdread.pageunit") || ""));
		if (tabs.length > 1) parts.push(tabs.length + " " + (SK.t("web.mdread.tabunit") || ""));
		F.unset(el, "data-k");
		el.textContent = parts.join("  ·  ");
	}

	function esc(s){
		return String(s == null ? "" : s)
			.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
	}
})();
