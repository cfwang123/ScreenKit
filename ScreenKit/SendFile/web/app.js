window.sfweb = (function(){
	var t = {
		init: init,
		mobile: false,
	};
	var path = "";
	var items = [];
	var zh = true;
	var busy = false;
	var token = "";

	return t;

	function init(opts) {
		t.mobile = !!(opts && opts.mobile);
		zh = !/^en\b/i.test(navigator.language || "");
		bind();
		i18n();
		me();
	}

	function bind() {
		on("blogin", "click", login);
		on("bout", "click", logout);
		on("bup", "click", function(){ el("efile").click(); });
		on("bmkdir", "click", mkdir);
		on("bref", "click", load);
		on("bzip", "click", zipcur);
		on("bdel", "click", delsel);
		on("efile", "change", onfiles);
		var keep = el("ekeep");
		if (keep) {
			try { keep.checked = localStorage.getItem("sk_web_keep") === "1"; } catch (e) {}
			keep.addEventListener("change", function(){
				try { localStorage.setItem("sk_web_keep", keep.checked ? "1" : "0"); } catch (e) {}
			});
		}
		var p = el("epass");
		if (p) p.addEventListener("keydown", function(e){
			if (e.key === "Enter") login();
		});
		var all = el("eall");
		if (all) all.addEventListener("change", function(){
			var boxes = document.querySelectorAll("#rows input[type=checkbox]");
			for (var i = 0; i < boxes.length; i++) boxes[i].checked = all.checked;
			syncsel();
		});
		document.addEventListener("dragover", function(e){
			if (!t.mobile) e.preventDefault();
		});
		document.addEventListener("drop", function(e){
			if (t.mobile) return;
			e.preventDefault();
			if (e.dataTransfer && e.dataTransfer.files)
				uploadfiles(e.dataTransfer.files);
		});
	}

	function i18n() {
		settxt("ltitle", zh ? "文件管理" : "Files");
		settxt("lhint", zh
			? "登录后可上传、新建、删除。下载只需正确链接。"
			: "Sign in to upload or manage. A correct download URL is enough.");
		settxt("blogin", zh ? "登录" : "Sign in");
		settxt("lkeep", zh ? "保持登录" : "Stay signed in");
		setph("epass", zh ? "登录密码" : "Password");
		settxt("bzip", zh ? (t.mobile ? "打包" : "打包下载") : (t.mobile ? "Zip" : "Download zip"));
		settxt("btitle", zh ? (t.mobile ? "文件" : "文件管理") : "Files");
		settxt("bup", zh ? "上传" : "Upload");
		settxt("bmkdir", zh ? (t.mobile ? "新建" : "新建文件夹") : (t.mobile ? "New" : "New folder"));
		settxt("bref", zh ? "刷新" : "Refresh");
		settxt("bout", zh ? "退出" : "Sign out");
		settxt("bdel", zh ? "删除所选" : "Delete selected");
		settxt("hname", zh ? "名称" : "Name");
		settxt("hsize", zh ? "大小" : "Size");
		settxt("htime", zh ? "修改时间" : "Modified");
		settxt("hact", zh ? "操作" : "Actions");
		settxt("empty", zh ? "这个文件夹是空的" : "This folder is empty");
	}

	function me() {
		api("GET", "/api/web/me", null, function(ok, data){
			if (!ok) { show(false); return; }
			show(true);
			var bn = el("bname");
			if (bn) bn.textContent = (data && data.name) || "";
			load();
		});
	}

	function login() {
		var pass = (el("epass") && el("epass").value) || "";
		if (!pass) { showerr("lerr", zh ? "请输入密码" : "Enter the password"); return; }
		var keep = !!(el("ekeep") && el("ekeep").checked);
		try { localStorage.setItem("sk_web_keep", keep ? "1" : "0"); } catch (e) {}
		api("POST", "/api/web/login", {password: pass, keep: keep}, function(ok, data){
			if (!ok) {
				showerr("lerr", (data && data.data) || (zh ? "密码错误" : "Wrong password"));
				return;
			}
			if (data && data.token) savetoken(data.token, keep || !!(data && data.keep));
			if (el("epass")) el("epass").value = "";
			hide("lerr");
			me();
		});
	}

	function logout() {
		api("POST", "/api/web/logout", {}, function(){
			savetoken("");
			items = [];
			show(false);
		});
	}

	function load() {
		api("GET", "/api/web/list?path=" + enc(path), null, function(ok, data){
			if (!ok) {
				if (is401(data)) { show(false); return; }
				showerr("err", msg(data) || (zh ? "列出失败" : "List failed"));
				return;
			}
			hide("err");
			items = (data && data.items) || [];
			render();
		});
	}

	function render() {
		crumbs();
		var box = el("rows");
		if (!box) return;
		box.innerHTML = "";
		var empty = items.length === 0;
		showel("empty", empty);
		for (var i = 0; i < items.length; i++)
			box.appendChild(t.mobile ? card(items[i]) : row(items[i]));
		syncsel();
	}

	function crumbs() {
		var nav = el("crumbs");
		if (!nav) return;
		nav.innerHTML = "";
		nav.appendChild(crumba(zh ? "根目录" : "Root", "", true));
		if (!path) return;
		var parts = path.split("/");
		var cur = "";
		for (var i = 0; i < parts.length; i++) {
			if (!parts[i]) continue;
			nav.appendChild(sep());
			cur = cur ? cur + "/" + parts[i] : parts[i];
			nav.appendChild(crumba(parts[i], cur, false));
		}
	}

	function crumba(label, p, home) {
		var a = document.createElement("a");
		a.href = "#";
		if (home && t.mobile) a.appendChild(svguse("i-home"));
		var sp = document.createElement("span");
		sp.textContent = label;
		a.appendChild(sp);
		a.addEventListener("click", function(e){
			e.preventDefault();
			path = p || "";
			load();
		});
		return a;
	}

	function sep() {
		var s = document.createElement("span");
		if (t.mobile) {
			s.className = "sep";
			s.appendChild(svguse("i-chevron"));
			return s;
		}
		s.textContent = "/";
		s.className = "muted";
		return s;
	}

	function row(it) {
		var tr = document.createElement("tr");
		var c0 = document.createElement("td");
		c0.className = "c0";
		var ck = document.createElement("input");
		ck.type = "checkbox";
		ck.setAttribute("data-path", it.path || "");
		ck.addEventListener("change", syncsel);
		c0.appendChild(ck);
		var cn = document.createElement("td");
		cn.className = "name";
		cn.appendChild(namea(it));
		var cs = document.createElement("td");
		cs.textContent = it.dir ? "—" : size(it.size);
		var ct = document.createElement("td");
		ct.textContent = when(it.mtime);
		var ca = document.createElement("td");
		ca.className = "acts";
		if (it.dir) {
			ca.appendChild(btna(zh ? "进入" : "Open", function(){ enter(it.path); }));
			ca.appendChild(btna(zh ? "打包" : "Zip", function(){ zipone(it.path); }));
			tr.addEventListener("dblclick", function(){ enter(it.path); });
		}
		else {
			ca.appendChild(link(zh ? "下载" : "Download", dlurl(it.path), true));
			ca.appendChild(btna(zh ? "复制链接" : "Copy link", function(){ copylink(it.path); }));
			ca.appendChild(btna(zh ? "打包" : "Zip", function(){ zipone(it.path); }));
		}
		ca.appendChild(btna(zh ? "改名" : "Rename", function(){ rename(it); }));
		ca.appendChild(btna(zh ? "删除" : "Delete", function(){ delone(it.path); }, true));
		tr.appendChild(c0);
		tr.appendChild(cn);
		tr.appendChild(cs);
		tr.appendChild(ct);
		tr.appendChild(ca);
		return tr;
	}

	function card(it) {
		var li = document.createElement("li");
		li.className = "item";
		var rowel = document.createElement("div");
		rowel.className = "row";
		var kind = document.createElement("div");
		kind.className = "kind" + (it.dir ? "" : " file");
		kind.appendChild(svguse(it.dir ? "i-folder" : "i-file"));
		var meta = document.createElement("div");
		meta.className = "meta";
		meta.appendChild(namea(it));
		var sub = document.createElement("div");
		sub.className = "sub";
		sub.textContent = (it.dir ? (zh ? "文件夹" : "Folder") : size(it.size)) + "  ·  " + when(it.mtime);
		meta.appendChild(sub);
		rowel.appendChild(kind);
		rowel.appendChild(meta);
		li.appendChild(rowel);
		var acts = document.createElement("div");
		acts.className = "acts";
		if (it.dir) {
			acts.appendChild(btna(zh ? "进入" : "Open", function(){ enter(it.path); }, false, "i-enter"));
			acts.appendChild(btna(zh ? "打包" : "Zip", function(){ zipone(it.path); }, false, "i-zip"));
			kind.addEventListener("click", function(){ enter(it.path); });
		}
		else {
			acts.appendChild(link(zh ? "下载" : "Download", dlurl(it.path), true, "i-down"));
			acts.appendChild(btna(zh ? "复制" : "Copy", function(){ copylink(it.path); }, false, "i-copy"));
			acts.appendChild(btna(zh ? "打包" : "Zip", function(){ zipone(it.path); }, false, "i-zip"));
		}
		acts.appendChild(btna(zh ? "改名" : "Rename", function(){ rename(it); }, false, "i-edit"));
		acts.appendChild(btna(zh ? "删除" : "Delete", function(){ delone(it.path); }, true, "i-trash"));
		li.appendChild(acts);
		return li;
	}

	function namea(it) {
		var a = document.createElement("a");
		a.className = it.dir ? "dir nm" : "nm";
		a.href = it.dir ? "#" : dlurl(it.path);
		a.textContent = it.name || it.path || "";
		if (it.dir) {
			a.addEventListener("click", function(e){
				e.preventDefault();
				path = it.path || "";
				load();
			});
		}
		return a;
	}

	function enter(p) {
		path = p || "";
		load();
	}

	function zipurl(rels) {
		if (!rels || !rels.length) return "/api/web/zip?path=";
		if (rels.length === 1) return "/api/web/zip?path=" + enc(rels[0]);
		return "/api/web/zip?paths=" + enc(rels.join("|"));
	}

	function zipone(p) {
		window.location.href = zipurl([p || ""]);
	}

	function zipcur() {
		var boxes = document.querySelectorAll("#rows input[type=checkbox]:checked");
		var rels = [];
		for (var i = 0; i < boxes.length; i++) {
			var p = boxes[i].getAttribute("data-path") || "";
			if (p) rels.push(p);
		}
		if (!rels.length) rels.push(path || "");
		window.location.href = zipurl(rels);
	}

	function mkdir() {
		var name = window.prompt(zh ? "新文件夹名称" : "Folder name");
		if (!name) return;
		name = name.replace(/[\\/]/g, "").trim();
		if (!name) return;
		var rel = join(path, name);
		api("POST", "/api/web/mkdir", {path: rel}, function(ok, data){
			if (!ok) { showerr("err", msg(data) || (zh ? "新建失败" : "Failed")); return; }
			load();
		});
	}

	function rename(it) {
		var old = it.name || "";
		var name = window.prompt(zh ? "新名称" : "New name", old);
		if (!name || name === old) return;
		name = name.replace(/[\\/]/g, "").trim();
		if (!name) return;
		var to = join(parentof(it.path), name);
		api("POST", "/api/web/rename", {from: it.path, to: to}, function(ok, data){
			if (!ok) { showerr("err", msg(data) || (zh ? "改名失败" : "Rename failed")); return; }
			load();
		});
	}

	function delone(p) {
		if (!p) return;
		if (!window.confirm(zh ? "删除「" + p + "」？" : "Delete “" + p + "”?")) return;
		api("POST", "/api/web/delete?path=" + enc(p), {}, function(ok, data){
			if (!ok) { showerr("err", msg(data) || (zh ? "删除失败" : "Delete failed")); return; }
			load();
		});
	}

	function delsel() {
		var boxes = document.querySelectorAll("#rows input[type=checkbox]:checked");
		if (!boxes.length) return;
		if (!window.confirm(zh ? "删除这 " + boxes.length + " 项？" : "Delete " + boxes.length + " item(s)?"))
			return;
		var i = 0;
		step();
		function step() {
			if (i >= boxes.length) { load(); return; }
			var p = boxes[i].getAttribute("data-path") || "";
			i++;
			if (!p) { step(); return; }
			api("POST", "/api/web/delete?path=" + enc(p), {}, function(){ step(); });
		}
	}

	function onfiles() {
		var f = el("efile");
		if (!f || !f.files || !f.files.length) return;
		uploadfiles(f.files);
		f.value = "";
	}

	function uploadfiles(files) {
		if (!files || !files.length || busy) return;
		busy = true;
		var i = 0;
		step();
		function step() {
			if (i >= files.length) {
				busy = false;
				load();
				return;
			}
			var f = files[i++];
			var rel = join(path, f.name);
			showerr("err", (zh ? "上传中 " : "Uploading ") + f.name + " (" + i + "/" + files.length + ")");
			rawpost("/api/web/upload?path=" + enc(rel), f, function(ok, data){
				if (!ok) showerr("err", msg(data) || (zh ? "上传失败" : "Upload failed"));
				step();
			});
		}
	}

	function copylink(p) {
		var url = location.origin + dlurl(p);
		if (navigator.clipboard && navigator.clipboard.writeText) {
			navigator.clipboard.writeText(url).then(function(){
				showerr("err", zh ? "已复制下载链接" : "Link copied");
			}, function(){ window.prompt(zh ? "复制链接" : "Copy link", url); });
			return;
		}
		window.prompt(zh ? "复制链接" : "Copy link", url);
	}

	function syncsel() {
		var b = el("bdel");
		if (!b) return;
		var n = document.querySelectorAll("#rows input[type=checkbox]:checked").length;
		b.hidden = n === 0;
	}

	function show(logged) {
		showel("login", !logged);
		showel("main", logged);
	}

	function api(method, url, body, done) {
		var opt = {method: method, credentials: "same-origin", headers: {}};
		var tok = loadtoken();
		if (tok) opt.headers["X-Web-Token"] = tok;
		if (body != null) {
			opt.headers["Content-Type"] = "application/json; charset=utf-8";
			opt.body = JSON.stringify(body);
		}
		fetch(url, opt).then(function(r){
			return r.text().then(function(txt){
				var j = parse(txt);
				var ok = r.ok && j && j.code === 100;
				var payload = pick(ok, j, r.status, txt);
				if (ok && payload && payload.token)
					savetoken(payload.token, !!(payload.keep));
				done(ok, payload);
			});
		}).catch(function(e){
			done(false, {code: 900, data: String(e)});
		});
	}

	function rawpost(url, blob, done) {
		var opt = {method: "POST", credentials: "same-origin", headers: {}, body: blob};
		var tok = loadtoken();
		if (tok) opt.headers["X-Web-Token"] = tok;
		fetch(url, opt).then(function(r){
			return r.text().then(function(txt){
				var j = parse(txt);
				done(r.ok && j && j.code === 100, pick(r.ok && j && j.code === 100, j, r.status, txt));
			});
		}).catch(function(e){
			done(false, {code: 900, data: String(e)});
		});
	}

	function pick(ok, j, status, txt) {
		if (ok && j && j.data != null && typeof j.data === "object")
			return j.data;
		return j || {code: status, data: txt};
	}

	function loadtoken() {
		if (token) return token;
		try { token = sessionStorage.getItem("sk_web") || ""; } catch (e) {}
		if (token) return token;
		try { token = localStorage.getItem("sk_web") || ""; } catch (e) {}
		return token;
	}

	function savetoken(t, keep) {
		token = t || "";
		try {
			if (token) sessionStorage.setItem("sk_web", token);
			else sessionStorage.removeItem("sk_web");
		}
		catch (e) {}
		try {
			if (token && keep) localStorage.setItem("sk_web", token);
			else localStorage.removeItem("sk_web");
		}
		catch (e) {}
	}

	function parse(txt) {
		try { return JSON.parse(txt); } catch (e) { return null; }
	}

	function is401(data) { return data && (data.code === 401 || data.code === 403); }
	function msg(data) {
		if (!data) return "";
		if (typeof data.data === "string") return data.data;
		return "";
	}

	function dlurl(p) {
		var parts = (p || "").replace(/\\/g, "/").split("/");
		var out = [];
		for (var i = 0; i < parts.length; i++) {
			if (parts[i]) out.push(encodeURIComponent(parts[i]));
		}
		return "/f/" + out.join("/");
	}

	function join(dir, name) {
		dir = (dir || "").replace(/\\/g, "/").replace(/^\/+|\/+$/g, "");
		return dir ? dir + "/" + name : name;
	}

	function parentof(p) {
		p = (p || "").replace(/\\/g, "/");
		var i = p.lastIndexOf("/");
		return i < 0 ? "" : p.substring(0, i);
	}

	function size(n) {
		n = Number(n) || 0;
		if (n < 1024) return n + " B";
		if (n < 1024 * 1024) return (n / 1024).toFixed(1) + " KB";
		if (n < 1024 * 1024 * 1024) return (n / (1024 * 1024)).toFixed(1) + " MB";
		return (n / (1024 * 1024 * 1024)).toFixed(2) + " GB";
	}

	function when(unix) {
		unix = Number(unix) || 0;
		if (unix <= 0) return "—";
		var d = new Date(unix * 1000);
		var pad = function(x){ return x < 10 ? "0" + x : "" + x; };
		return d.getFullYear() + "-" + pad(d.getMonth() + 1) + "-" + pad(d.getDate())
			+ " " + pad(d.getHours()) + ":" + pad(d.getMinutes());
	}

	function enc(s) { return encodeURIComponent(s || ""); }

	function el(id) { return document.getElementById(id); }
	function on(id, ev, fn) {
		var n = el(id);
		if (n) n.addEventListener(ev, fn);
	}
	function settxt(id, s) {
		var n = el(id);
		if (!n) return;
		var lbl = n.querySelector(".lbl");
		if (lbl) lbl.textContent = s;
		else n.textContent = s;
	}
	function setph(id, s) {
		var n = el(id);
		if (n) n.placeholder = s;
	}
	function showel(id, onoff) {
		var n = el(id);
		if (n) n.hidden = !onoff;
	}
	function hide(id) { showel(id, false); }
	function showerr(id, s) {
		var n = el(id);
		if (!n) return;
		n.textContent = s || "";
		n.hidden = !s;
	}
	function svguse(id) {
		var ns = "http://www.w3.org/2000/svg";
		var s = document.createElementNS(ns, "svg");
		s.setAttribute("class", "ico");
		s.setAttribute("aria-hidden", "true");
		var u = document.createElementNS(ns, "use");
		u.setAttribute("href", "#" + id);
		u.setAttributeNS("http://www.w3.org/1999/xlink", "href", "#" + id);
		s.appendChild(u);
		return s;
	}
	function link(label, href, blank, icon) {
		var a = document.createElement("a");
		a.href = href;
		if (blank) a.target = "_blank";
		if (icon) a.appendChild(svguse(icon));
		var sp = document.createElement("span");
		sp.className = "lbl";
		sp.textContent = label;
		a.appendChild(sp);
		return a;
	}
	function btna(label, fn, danger, icon) {
		var b = document.createElement("button");
		b.type = "button";
		if (danger) b.className = "danger";
		if (icon) b.appendChild(svguse(icon));
		var sp = document.createElement("span");
		sp.className = "lbl";
		sp.textContent = label;
		b.appendChild(sp);
		b.addEventListener("click", fn);
		return b;
	}
})();
