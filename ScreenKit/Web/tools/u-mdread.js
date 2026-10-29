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

	function body(t){
		var hint = SK.t("web.mdread.hint") || "";
		var openLabel = SK.t("web.mdread.open") || "打开";
		return F.CreateDOM(t, { x: "div.mdread", c: [
			F.CreateDOM(t, { x: "div.mdread-bar", c: [
				F.CreateDOM(t, { x: "button#md-open", type: "button", c: openLabel }),
				F.CreateDOM(t, { x: "input#md-file", type: "file", accept: ".md,.markdown,.mdown,.mkd,text/markdown", hidden: true }),
				F.CreateDOM(t, { x: "select#md-theme.mdtheme", title: SK.t("mdread.theme") || "" }),
				F.CreateDOM(t, { x: "span#md-name", "data-k": "web.mdread.hint", c: hint })
			] }),
			F.CreateDOM(t, { x: "div.mdread-stage", c: [
				F.CreateDOM(t, { x: "iframe#md-view", title: "preview", sandbox: "allow-popups allow-scripts" }),
				F.CreateDOM(t, { x: "div#md-mask.mdread-mask" })
			] })
		] });
	}

	function open(){
		var btn = SK.dom("md-open");
		var file = SK.dom("md-file");
		if (btn && file) btn.onclick = function(){ file.click(); };
		if (file) file.onchange = function(){
			take(file.files && file.files[0]);
			file.value = "";
		};
		filltheme();
		var sel = SK.dom("md-theme");
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
		var sel = SK.dom("md-theme");
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
		var mask = SK.dom("md-mask");
		if (!mask) return;
		F.pd(ev);
		mask.className = "mdread-mask on";
	}

	function disarm(){
		var mask = SK.dom("md-mask");
		if (mask) mask.className = "mdread-mask";
	}

	function ondrop(ev){
		var mask = SK.dom("md-mask");
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
			var name = SK.dom("md-name");
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
		var sel = SK.dom("md-theme");
		SK.post("/api/md", { base64: lastb64, title: lasttitle, theme: sel ? sel.value : "" }, function(data){
			var view = SK.dom("md-view");
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
