(function(){
	var hooked = false;
	var lastb64 = "";
	var lasttitle = "";
	var THEMES = ["light", "dark", "sepia", "green", "night"];

	SK.regutil("u-mdread", {
		title: "MD阅读器",
		own: true,
		hideGo: true,
		hideCopy: true,
		hideOut: true,
		body: body,
		open: open
	});

	function body(){
		var hint = SK.t("web.mdread.hint") || "";
		var openLabel = SK.t("web.mdread.open") || "打开";
		return '<div class="mdread">'
			+ '<div class="mdread-bar">'
			+ '<button type="button" id="md-open">' + SK.esc(openLabel) + "</button>"
			+ '<input id="md-file" type="file" accept=".md,.markdown,.mdown,.mkd,text/markdown" hidden>'
			+ '<select id="md-theme" class="mdtheme" title="' + SK.esc(SK.t("mdread.theme") || "") + '"></select>'
			+ '<span id="md-name" data-k="web.mdread.hint">' + SK.esc(hint) + "</span>"
			+ "</div>"
			+ '<div class="mdread-stage">'
			+ '<iframe id="md-view" title="preview" sandbox="allow-popups"></iframe>'
			+ '<div id="md-mask" class="mdread-mask"></div>'
			+ "</div></div>";
	}

	function open(){
		var btn = F.id("md-open");
		var file = F.id("md-file");
		if (btn && file) btn.onclick = function(){ file.click(); };
		if (file) file.onchange = function(){
			take(file.files && file.files[0]);
			file.value = "";
		};
		filltheme();
		var sel = F.id("md-theme");
		if (sel) sel.onchange = function(){
			savetheme(sel.value);
			render();
		};
		if (hooked) return;
		hooked = true;
		F.on(document, "dragenter", arm);
		F.on(document, "dragover", arm);
		F.on(document, "drop", ondrop);
		F.on(document, "dragend", disarm);
	}

	function filltheme(){
		var sel = F.id("md-theme");
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
		var mask = F.id("md-mask");
		if (!mask) return;
		F.pd(ev);
		mask.className = "mdread-mask on";
	}

	function disarm(){
		var mask = F.id("md-mask");
		if (mask) mask.className = "mdread-mask";
	}

	function ondrop(ev){
		var mask = F.id("md-mask");
		if (!mask) return;
		F.pd(ev);
		disarm();
		var files = ev.dataTransfer && ev.dataTransfer.files;
		if (files && files.length) take(files[0]);
	}

	function ismd(name){
		var n = (name || "").toLowerCase();
		return n.endsWith(".md") || n.endsWith(".markdown") || n.endsWith(".mdown") || n.endsWith(".mkd");
	}

	function take(file){
		if (!file) return;
		if (!ismd(file.name)) {
			SK.msg("box-msg", SK.t("web.mdread.bad") || "", true);
			return;
		}
		var reader = new FileReader();
		reader.onload = function(){
			var bytes = new Uint8Array(reader.result);
			lastb64 = tob64(bytes);
			lasttitle = file.name || "";
			var name = F.id("md-name");
			if (name) {
				F.unset(name, "data-k");
				name.textContent = file.name || "";
			}
			render();
		};
		reader.readAsArrayBuffer(file);
	}

	function render(){
		if (!lastb64) return;
		var sel = F.id("md-theme");
		SK.post("/api/md", { base64: lastb64, title: lasttitle, theme: sel ? sel.value : "" }, function(data){
			var view = F.id("md-view");
			if (view) {
				view.srcdoc = (data && data.html) || "";
				if (data && data.bg) view.style.background = data.bg;
			}
			SK.msg("box-msg", "");
		}, "box-msg");
	}

	function tob64(bytes){
		var s = "";
		var step = 0x8000;
		for (var i = 0; i < bytes.length; i += step)
			s += String.fromCharCode.apply(null, bytes.subarray(i, i + step));
		return btoa(s);
	}
})();
