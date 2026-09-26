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
	var msel = [];
	var longPressTimer = 0;
	var mdialogDone = null;
	var mconfirmFn = null;
	var toastTimer = 0;
	var ctxmenu = null;

	return t;

	function init(opts) {
		t.mobile = !!(opts && opts.mobile);
		zh = loadlang();
		bind();
		if (!t.mobile) inittablectx();
		applylang();
		me();
	}

	function loadlang() {
		var saved = "";
		try { saved = localStorage.getItem("sk_web_lang") || ""; } catch (e) {}
		if (saved === "en") return false;
		if (saved === "zh") return true;
		return !/^en\b/i.test(navigator.language || "");
	}

	function onlang(e) {
		var code = e.currentTarget.getAttribute("data-lang");
		var next = code !== "en";
		if (next === zh) return;
		zh = next;
		try { localStorage.setItem("sk_web_lang", zh ? "zh" : "en"); } catch (e2) {}
		applylang();
	}

	function applylang() {
		document.documentElement.lang = zh ? "zh-CN" : "en";
		document.title = zh ? "ScreenKit 文件" : "ScreenKit Files";
		i18n();
		marklang();
		if (el("rows") && el("main") && !el("main").hidden) render();
	}

	function marklang() {
		var btns = document.querySelectorAll(".langbtn");
		var cur = zh ? "zh" : "en";
		for (var i = 0; i < btns.length; i++)
			btns[i].classList.toggle("on", btns[i].getAttribute("data-lang") === cur);
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
		var langs = document.querySelectorAll(".langbtn");
		for (var li = 0; li < langs.length; li++)
			langs[li].addEventListener("click", onlang);
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
		if (t.mobile) bindmobile();
	}

	function bindmobile() {
		on("mprofile", "click", function(){ openmsheet("maccount"); });
		on("mcheckupdate", "click", mcheckupdate);
		on("mupdateclose", "click", closemoverlays);
		on("mupdatedl", "click", mupdatedownload);
		on("mlogout", "click", function(){ closemoverlays(); logout(); });
		on("mscrim", "click", closemoverlays);
		on("bcam", "click", function(){ var c = el("ecamera"); if (c) c.click(); });
		on("ecamera", "change", oncamera);
		on("mselcancel", "click", clearmsel);
		on("mseldown", "click", mseldownload);
		on("mselzip", "click", mselzip);
		on("mselcopy", "click", mselcopy);
		on("mselrename", "click", mselrename);
		on("mseldel", "click", mseldelconfirm);
		on("mdcancel", "click", closemoverlays);
		on("mdok", "click", mdok);
		on("mdinput", "keydown", function(e){ if (e.key === "Enter") mdok(); });
		on("mccancel", "click", closemoverlays);
		on("mcok", "click", mconfirmok);
	}

	function i18n() {
		settxt("ltitle", zh ? "文件管理" : "Files");
		settxt("lhint", zh
			? "登录后可上传、新建、删除。下载只需正确链接。"
			: "Sign in to upload or manage. A correct download URL is enough.");
		settxt("blogin", zh ? "登录" : "Sign in");
		settxt("lkeep", zh ? "保持登录" : "Stay signed in");
		setph("epass", zh ? "登录密码" : "Password");
		settxt("bzip", zh ? "打包下载" : "Download zip");
		settxt("btitle", zh ? "文件管理" : "Files");
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
		settxt("lpc", zh ? "手机版" : "Phone");
		settxt("ldesk", zh ? "电脑版" : "Desktop");
		setaria("crumbs", zh ? "当前位置" : "Location");
		setaria("mprofile", zh ? "账户" : "Account");
		setaria("mdocknav", zh ? "文件操作" : "File actions");
		setaria("mselacts", zh ? "所选项目操作" : "Selection actions");
		setaria("maccount", zh ? "账户" : "Account");
		if (!t.mobile) return;
		settxt("mhint", zh ? "长按项目可多选" : "Long-press to select");
		settxt("lcam", zh ? "拍照" : "Camera");
		settxt("lmselcancel", zh ? "取消选择" : "Cancel");
		settxt("lmseldown", zh ? "下载" : "Download");
		settxt("lmselzip", zh ? "下载 ZIP" : "ZIP");
		settxt("lmselcopy", zh ? "复制链接" : "Copy link");
		settxt("lmselrename", zh ? "改名" : "Rename");
		settxt("lmseldel", zh ? "删除" : "Delete");
		settxt("lmcheckupdate", zh ? "检查更新" : "Check for updates");
		settxt("lmlogout", zh ? "退出登录" : "Sign out");
		settxt("mupdatetitle", zh ? "检查更新" : "Check for updates");
		settxt("lmupdateclose", zh ? "关闭" : "Close");
		settxt("lmupdatedl", zh ? "下载 APK" : "Download APK");
		settxt("lmdcancel", zh ? "取消" : "Cancel");
		settxt("lmdok", zh ? "确定" : "OK");
		settxt("lmccancel", zh ? "取消" : "Cancel");
		settxt("lmcok", zh ? "删除" : "Delete");
		settxt("emptytitle", zh ? "这个文件夹是空的" : "This folder is empty");
		settxt("emptyhint", zh ? "点击下方「上传」或「新建」" : "Use Upload or New below");
		settxt("maccountsub", zh ? "网页文件管理" : "Web file manager");
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
				mfail(msg(data) || (zh ? "列出失败" : "List failed"));
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
		var list = items.slice();
		if (t.mobile) msort(list);
		var empty = list.length === 0;
		showel("empty", empty);
		for (var i = 0; i < list.length; i++)
			box.appendChild(t.mobile ? mrow(list[i]) : row(list[i]));
		if (t.mobile) {
			var mc = el("mcount");
			if (mc) mc.textContent = list.length + (zh ? " 项" : " items");
			updatemselbar();
		}
		else syncsel();
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
		if (t.mobile) setTimeout(function(){ nav.scrollLeft = nav.scrollWidth; }, 0);
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
			ca.textContent = "—";
			ca.classList.add("muted");
			tr.addEventListener("dblclick", function(){ enter(it.path); });
		}
		else ca.appendChild(link(zh ? "下载" : "Download", dlurl(it.path), true));
		if (!t.mobile) {
			tr.classList.add("rowpick");
			tr.addEventListener("click", function(e){
				if (e.target.closest(".acts a, .acts button")) return;
				if (e.target.closest("input[type=checkbox]")) return;
				ck.checked = !ck.checked;
				syncsel();
			});
			tr.addEventListener("contextmenu", function(e){ showctx(e, it); });
		}
		tr.appendChild(c0);
		tr.appendChild(cn);
		tr.appendChild(cs);
		tr.appendChild(ct);
		tr.appendChild(ca);
		return tr;
	}

	function mrow(it) {
		var li = document.createElement("li");
		var sel = msel.indexOf(it.path || "") >= 0;
		li.className = "file-row" + (sel ? " selected" : "");
		li.appendChild(mfileicon(it));
		var main = document.createElement("div");
		main.className = "file-main";
		main.innerHTML = '<span class="file-name"></span><span class="file-meta"></span>';
		main.querySelector(".file-name").textContent = it.name || it.path || "";
		main.querySelector(".file-meta").textContent = (it.dir
			? (zh ? "文件夹" : "Folder")
			: size(it.size)) + "  ·  " + when(it.mtime);
		li.appendChild(main);
		var end = document.createElement("div");
		end.className = "row-end";
		if (msel.length) {
			var check = document.createElement("span");
			check.className = "select-check";
			check.appendChild(svguse("i-check"));
			end.appendChild(check);
		}
		else if (it.dir) end.appendChild(svguse("i-chevron"));
		else end.appendChild(svguse("i-down"));
		li.appendChild(end);
		bindmpress(li, it);
		return li;
	}

	function namea(it) {
		var n = document.createElement("span");
		n.className = it.dir ? "dir nm" : "nm";
		n.textContent = it.name || it.path || "";
		return n;
	}

	function enter(p) {
		if (t.mobile) clearmsel();
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
		if (t.mobile) {
			openmdialog(zh ? "新建文件夹" : "New folder", zh ? "请输入文件夹名称" : "Folder name", "", function(name){
				mkdirapi(name);
			});
			return;
		}
		var name = window.prompt(zh ? "新文件夹名称" : "Folder name");
		mkdirapi(name);
	}

	function mkdirapi(name) {
		if (!name) return;
		name = name.replace(/[\\/]/g, "").trim();
		if (!name) return;
		var rel = join(path, name);
		api("POST", "/api/web/mkdir", {path: rel}, function(ok, data){
			if (!ok) { mfail(msg(data) || (zh ? "新建失败" : "Failed")); return; }
			load();
		});
	}

	function rename(it) {
		var old = it.name || "";
		if (t.mobile) {
			openmdialog(zh ? "重命名" : "Rename", zh ? "输入新的名称" : "New name", old, function(name){
				renameapi(it, name);
			});
			return;
		}
		var name = window.prompt(zh ? "新名称" : "New name", old);
		renameapi(it, name);
	}

	function renameapi(it, name) {
		var old = it.name || "";
		if (!name || name === old) return;
		name = name.replace(/[\\/]/g, "").trim();
		if (!name) return;
		var to = join(parentof(it.path), name);
		api("POST", "/api/web/rename", {from: it.path, to: to}, function(ok, data){
			if (!ok) { mfail(msg(data) || (zh ? "改名失败" : "Rename failed")); return; }
			if (t.mobile) clearmsel();
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

	function oncamera() {
		var f = el("ecamera");
		if (!f || !f.files || !f.files.length) return;
		var file = f.files[0];
		f.value = "";
		camcompress(file, function(out){
			if (!out) {
				mfail(zh ? "压缩照片失败" : "Could not compress the photo");
				return;
			}
			uploadfiles([out], true);
		});
	}

	function camcompress(file, done) {
		api("GET", "/api/web/photo", null, function(ok, data){
			var opt = photopt(ok && data ? data : null);
			drawcam(file, opt.limit ? opt.max : 0, opt.mime, opt.q / 100, opt.name, function(out){
				if (out) { done(out); return; }
				done(asname(file, opt.name, opt.mime));
			});
		});
	}

	function photopt(data) {
		data = data || {};
		var fmt = data.photoFmt === "png" ? "png" : "jpg";
		var q = data.photoQuality > 0 ? data.photoQuality : 60;
		if (q < 1) q = 1;
		if (q > 100) q = 100;
		var limit = data.photoLimit !== false;
		var max = data.photoMaxPx > 0 ? data.photoMaxPx : 2000;
		if (max < 64) max = 64;
		if (max > 16000) max = 16000;
		return {
			fmt: fmt,
			q: q,
			limit: limit,
			max: max,
			mime: fmt === "png" ? "image/png" : "image/jpeg",
			name: camname(fmt)
		};
	}

	function camname(fmt) {
		var d = new Date();
		function p(n) { return n < 10 ? "0" + n : "" + n; }
		var s = "" + d.getFullYear() + p(d.getMonth() + 1) + p(d.getDate())
			+ "_" + p(d.getHours()) + p(d.getMinutes()) + p(d.getSeconds());
		return "photo_" + s + "." + fmt;
	}

	function drawcam(file, max, mime, quality, name, done) {
		var url = URL.createObjectURL(file);
		var img = new Image();
		img.onload = function() {
			URL.revokeObjectURL(url);
			var sw = img.naturalWidth || img.width;
			var sh = img.naturalHeight || img.height;
			paintof(sw, sh, max, function(g, w, h) {
				g.drawImage(img, 0, 0, w, h);
			}, mime, quality, name, done);
		};
		img.onerror = function() {
			URL.revokeObjectURL(url);
			drawbmp(file, max, mime, quality, name, done);
		};
		img.src = url;
	}

	function drawbmp(file, max, mime, quality, name, done) {
		if (!window.createImageBitmap) { done(null); return; }
		var p;
		try { p = createImageBitmap(file); }
		catch (e) { done(null); return; }
		p.then(function(bmp) {
			paintof(bmp.width, bmp.height, max, function(g, w, h) {
				g.drawImage(bmp, 0, 0, w, h);
			}, mime, quality, name, function(out) {
				if (bmp.close) bmp.close();
				done(out);
			});
		}, function() { done(null); });
	}

	function paintof(sw, sh, max, draw, mime, quality, name, done) {
		var w = sw;
		var h = sh;
		if (!(w > 0) || !(h > 0)) { done(null); return; }
		if (max > 0 && (w > max || h > max)) {
			var s = Math.min(max / w, max / h);
			w = Math.max(1, Math.round(w * s));
			h = Math.max(1, Math.round(h * s));
		}
		var c = document.createElement("canvas");
		c.width = w;
		c.height = h;
		var g = c.getContext("2d");
		if (!g) { done(null); return; }
		if (mime === "image/jpeg") {
			g.fillStyle = "#fff";
			g.fillRect(0, 0, w, h);
		}
		try { draw(g, w, h); }
		catch (e) { done(null); return; }
		if (!c.toBlob) { done(null); return; }
		c.toBlob(function(blob) {
			if (!blob) { done(null); return; }
			done(asname(blob, name, mime));
		}, mime, quality);
	}

	function asname(blob, name, mime) {
		try { return new File([blob], name, {type: mime || blob.type || ""}); }
		catch (e) {
			try { blob.name = name; } catch (e2) {}
			return blob;
		}
	}

	function uploadfiles(files, photo) {
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
			var note = (zh ? "上传中 " : "Uploading ") + f.name + " (" + i + "/" + files.length + ")";
			if (t.mobile) mtoast(note);
			else showerr("err", note);
			var url = "/api/web/upload?path=" + enc(rel);
			if (photo) url += "&photo=1";
			rawpost(url, f, function(ok, data){
				if (!ok) mfail(msg(data) || (zh ? "上传失败" : "Upload failed"));
				step();
			});
		}
	}

	function copylink(p) {
		var url = location.origin + dlurl(p);
		if (navigator.clipboard && navigator.clipboard.writeText) {
			navigator.clipboard.writeText(url).then(function(){
				if (t.mobile) mtoast(zh ? "已复制下载链接" : "Link copied");
				else showerr("err", zh ? "已复制下载链接" : "Link copied");
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
		if (!t.mobile) return;
		showel("mdock", logged);
		if (!logged) {
			clearmsel();
			closemoverlays();
			showel("mselbar", false);
		}
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
		var s = typeof data.data === "string" ? data.data : "";
		if (!s || zh) return s;
		var map = {
			"密码错误": "Wrong password",
			"未登录": "Sign in required",
			"未知路径": "Not found",
			"打包失败": "Zip failed",
			"缺少文件路径": "Missing path",
			"下载仅支持 GET/HEAD": "Download allows GET/HEAD only",
			"页面仅支持 GET/HEAD": "Page allows GET/HEAD only",
			"检查失败": "Check failed"
		};
		if (map[s]) return map[s];
		if (s.indexOf("页面文件缺失") === 0) return "Page file missing";
		return s;
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
	function setaria(id, s) {
		var n = el(id);
		if (n) n.setAttribute("aria-label", s);
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

	function inittablectx() {
		document.addEventListener("click", hidectx);
		document.addEventListener("scroll", hidectx, true);
		window.addEventListener("resize", hidectx);
		document.addEventListener("keydown", function(e){
			if (e.key === "Escape") hidectx();
		});
	}

	function hidectx() {
		if (ctxmenu) ctxmenu.hidden = true;
	}

	function ensurectx() {
		if (ctxmenu) return ctxmenu;
		ctxmenu = document.createElement("div");
		ctxmenu.id = "ctxmenu";
		ctxmenu.hidden = true;
		document.body.appendChild(ctxmenu);
		return ctxmenu;
	}

	function ctxitem(label, fn, danger) {
		var b = document.createElement("button");
		b.type = "button";
		b.textContent = label;
		if (danger) b.className = "danger";
		b.addEventListener("click", function(e){
			e.stopPropagation();
			hidectx();
			fn();
		});
		return b;
	}

	function showctx(e, it) {
		e.preventDefault();
		var m = ensurectx();
		m.innerHTML = "";
		if (it.dir) {
			m.appendChild(ctxitem(zh ? "进入" : "Open", function(){ enter(it.path); }));
			m.appendChild(ctxitem(zh ? "打包" : "Zip", function(){ zipone(it.path); }));
		}
		else {
			m.appendChild(ctxitem(zh ? "下载" : "Download", function(){
				window.open(dlurl(it.path), "_blank");
			}));
			m.appendChild(ctxitem(zh ? "复制链接" : "Copy link", function(){ copylink(it.path); }));
			m.appendChild(ctxitem(zh ? "打包" : "Zip", function(){ zipone(it.path); }));
		}
		m.appendChild(ctxitem(zh ? "改名" : "Rename", function(){ rename(it); }));
		m.appendChild(ctxitem(zh ? "删除" : "Delete", function(){ delone(it.path); }, true));
		m.hidden = false;
		m.style.left = e.clientX + "px";
		m.style.top = e.clientY + "px";
		var r = m.getBoundingClientRect();
		var vw = window.innerWidth;
		var vh = window.innerHeight;
		if (r.right > vw) m.style.left = Math.max(4, vw - r.width - 4) + "px";
		if (r.bottom > vh) m.style.top = Math.max(4, vh - r.height - 4) + "px";
	}

	function msort(list) {
		list.sort(function(a, b){
			if (a.dir !== b.dir) return a.dir ? -1 : 1;
			return (a.name || "").localeCompare(b.name || "", zh ? "zh-CN" : undefined);
		});
	}

	function mkind(it) {
		var n = it.name || "";
		if (it.dir) return "folder";
		if (/\.(jpg|jpeg|png|gif|webp|bmp|heic)$/i.test(n)) return "image";
		if (/\.(mp4|mov|mkv|avi|webm)$/i.test(n)) return "video";
		if (/\.pdf$/i.test(n)) return "pdf";
		if (/\.(zip|7z|rar|tar|gz)$/i.test(n)) return "archive";
		return "file";
	}

	function mfileicon(it) {
		var kind = mkind(it);
		var ids = {folder:"i-folder", image:"i-image", video:"i-video", pdf:"i-pdf", archive:"i-archive", file:"i-file"};
		var box = document.createElement("div");
		box.className = "file-icon " + kind;
		box.appendChild(svguse(ids[kind] || "i-file"));
		return box;
	}

	function bindmpress(row, it) {
		var held = false;
		var startX = 0;
		var startY = 0;
		row.addEventListener("pointerdown", function(e){
			held = false;
			startX = e.clientX;
			startY = e.clientY;
			clearTimeout(longPressTimer);
			longPressTimer = setTimeout(function(){
				held = true;
				selectm(it, true);
				if (navigator.vibrate) navigator.vibrate(25);
			}, 520);
		});
		row.addEventListener("pointermove", function(e){
			if (Math.abs(e.clientX - startX) > 9 || Math.abs(e.clientY - startY) > 9)
				clearTimeout(longPressTimer);
		});
		row.addEventListener("pointerup", function(){ clearTimeout(longPressTimer); });
		row.addEventListener("pointercancel", function(){ clearTimeout(longPressTimer); });
		row.addEventListener("contextmenu", function(e){
			e.preventDefault();
			selectm(it, true);
		});
		row.addEventListener("click", function(e){
			if (held) {
				held = false;
				e.preventDefault();
				return;
			}
			if (msel.length) {
				selectm(it, false);
				return;
			}
			if (it.dir) enter(it.path);
			else window.location.href = dlurl(it.path);
		});
	}

	function selectm(it, forceOn) {
		var p = it.path || "";
		var i = msel.indexOf(p);
		if (i >= 0) {
			if (forceOn) return;
			msel.splice(i, 1);
		}
		else msel.push(p);
		render();
	}

	function clearmsel() {
		if (!msel.length) return;
		msel = [];
		render();
	}

	function updatemselbar() {
		var bar = el("mselbar");
		if (!bar) return;
		var n = msel.length;
		bar.hidden = n === 0;
		var dock = el("mdock");
		if (dock) dock.hidden = n !== 0;
		var sc = el("mselcount");
		if (sc) sc.textContent = (zh ? "已选择 " : "") + n + (zh ? " 项" : " selected");
		var rn = el("mselrename");
		if (rn) rn.disabled = n !== 1;
		var cp = el("mselcopy");
		if (cp) cp.disabled = n === 0;
	}

	function mitem(path) {
		for (var i = 0; i < items.length; i++)
			if (items[i].path === path) return items[i];
		return null;
	}

	function openmsheet(id) {
		closemoverlays();
		el("mscrim").classList.add("show");
		el(id).classList.add("show");
	}

	function showmdialog(id) {
		el("mscrim").classList.add("show");
		el(id).classList.add("show");
	}

	function closemoverlays() {
		var scrim = el("mscrim");
		if (scrim) scrim.classList.remove("show");
		var nodes = document.querySelectorAll(".sheet.show,.dialog.show");
		for (var i = 0; i < nodes.length; i++) nodes[i].classList.remove("show");
	}

	function openmdialog(title, hint, value, done) {
		closemoverlays();
		el("mdtitle").textContent = title;
		el("mdhint").textContent = hint;
		el("mdinput").value = value || "";
		mdialogDone = done;
		showmdialog("mdialog");
		setTimeout(function(){
			el("mdinput").focus();
			el("mdinput").select();
		}, 30);
	}

	function mdok() {
		var value = (el("mdinput").value || "").replace(/[\\/]/g, "").trim();
		var done = mdialogDone;
		closemoverlays();
		mdialogDone = null;
		if (done) done(value);
	}

	function mconfirmok() {
		var fn = mconfirmFn;
		closemoverlays();
		mconfirmFn = null;
		if (fn) fn();
	}

	function mseldownload() {
		if (!msel.length) return;
		var files = [];
		for (var i = 0; i < msel.length; i++) {
			var it = mitem(msel[i]);
			if (it && !it.dir) files.push(it);
		}
		if (!files.length) {
			mtoast(zh ? "文件夹请使用「下载 ZIP」" : "Use ZIP for folders");
			return;
		}
		for (var j = 0; j < files.length; j++)
			window.open(dlurl(files[j].path), "_blank");
		clearmsel();
		if (files.length === 1) mtoast(zh ? "开始下载" : "Downloading");
		else mtoast((zh ? "开始下载 " : "Downloading ") + files.length + (zh ? " 个文件" : " files"));
	}

	function mselzip() {
		if (!msel.length) return;
		window.location.href = zipurl(msel.slice());
		clearmsel();
	}

	function mselcopy() {
		if (!msel.length) return;
		var urls = [];
		for (var i = 0; i < msel.length; i++)
			urls.push(location.origin + dlurl(msel[i]));
		clearmsel();
		if (navigator.clipboard && navigator.clipboard.writeText)
			navigator.clipboard.writeText(urls.join("\n")).then(function(){
				mtoast(zh ? "已复制下载链接" : "Link copied");
			}, function(){ window.prompt(zh ? "复制链接" : "Copy link", urls.join("\n")); });
		else window.prompt(zh ? "复制链接" : "Copy link", urls.join("\n"));
	}

	function mselrename() {
		if (msel.length !== 1) return;
		var it = mitem(msel[0]);
		if (!it) return;
		rename(it);
	}

	function mseldelconfirm() {
		if (!msel.length) return;
		closemoverlays();
		el("mctitle").textContent = msel.length === 1
			? (zh ? "确认删除？" : "Delete?")
			: (zh ? "删除 " + msel.length + " 项？" : "Delete " + msel.length + " items?");
		el("mctext").textContent = msel.length === 1
			? (zh ? "将删除「" + (mitem(msel[0]) && mitem(msel[0]).name || msel[0]) + "」，此操作无法从网页恢复。"
				: "This cannot be undone.")
			: (zh ? "所选文件和文件夹将被删除，此操作无法从网页恢复。"
				: "Selected items will be deleted.");
		mconfirmFn = mseldelete;
		showmdialog("mconfirm");
	}

	function mseldelete() {
		var paths = msel.slice();
		clearmsel();
		var i = 0;
		step();
		function step() {
			if (i >= paths.length) { load(); return; }
			var p = paths[i++];
			if (!p) { step(); return; }
			api("POST", "/api/web/delete?path=" + enc(p), {}, function(){ step(); });
		}
	}

	function mtoast(s) {
		if (!t.mobile) { showerr("err", s); return; }
		var node = el("mtoast");
		if (!node) return;
		node.textContent = s || "";
		node.classList.add("show");
		clearTimeout(toastTimer);
		toastTimer = setTimeout(function(){ node.classList.remove("show"); }, 1900);
	}

	function mfail(s) {
		if (t.mobile) mtoast(s);
		else showerr("err", s);
	}

	var mupdateDlUrl = "";

	function mcheckupdate() {
		mupdateDlUrl = "";
		var dl = el("mupdatedl");
		if (dl) dl.hidden = true;
		el("mupdatemsg").textContent = zh ? "正在从 GitHub 检查…" : "Checking GitHub…";
		closemoverlays();
		showmdialog("mupdate");
		api("GET", "/api/web/apk-update", null, function(ok, data){
			var body = data;
			if (!ok || !body) {
				el("mupdatemsg").textContent = zh ? "检查失败" : "Check failed";
				return;
			}
			if (body.ok === false) {
				el("mupdatemsg").textContent = body.error || (zh ? "检查失败" : "Check failed");
				return;
			}
			var cur = body.current || "—";
			var latest = body.latest || "—";
			var lines = [];
			if (cur && cur !== "—")
				lines.push(zh ? "当前：" + cur : "Current: " + cur);
			lines.push(zh ? "最新：" + latest : "Latest: " + latest);
			if (body.sizeText)
				lines.push(zh ? "大小：" + body.sizeText : "Size: " + body.sizeText);
			if (body.hasUpdate)
				lines.push(zh ? "发现新版本，可下载安装。" : "A newer version is available.");
			else if (latest && latest !== "—")
				lines.push(zh ? "已是最新版本。" : "You are up to date.");
			el("mupdatemsg").textContent = lines.join("\n");
			var url = "";
			if (body.hasUpdate && body.localApkUrl)
				url = body.localApkUrl;
			else if (body.hasApk && body.downloadUrl)
				url = body.downloadUrl;
			else if (body.htmlUrl)
				url = body.htmlUrl;
			mupdateDlUrl = url;
			if (dl && url) dl.hidden = false;
		});
	}

	function mupdatedownload() {
		if (!mupdateDlUrl) return;
		var url = mupdateDlUrl;
		if (url.charAt(0) === "/")
			url = location.origin + url;
		window.open(url, "_blank");
		closemoverlays();
	}
})();
