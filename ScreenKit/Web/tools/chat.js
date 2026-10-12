(function(){
	var ws = null;
	var me = 0;
	var opening = 0;
	var leaving = 0;
	var beat = 0;
	var beatsince = 0;
	var offers = [];
	var NICK = "sk-chat-nick";
	var GENDER = "sk-chat-gender";

	function open(){
		var nick = "";
		var gender = "";
		try {
			nick = localStorage.getItem(NICK) || "";
			gender = localStorage.getItem(GENDER) || "";
		}
		catch (e) {}
		var box = SK.$("chat-nick");
		var sel = SK.$("chat-gender");
		if (box) box.value = nick;
		if (sel && (gender === "" || gender === "m" || gender === "f")) sel.value = gender;
		SK.$("chat-join").onclick = join;
		SK.$("chat-send").onclick = send;
		SK.$("chat-leave").onclick = leave;
		SK.$("chat-imgbtn").onclick = function(){ pick("image"); };
		SK.$("chat-filebtn").onclick = function(){ pick("file"); };
		SK.$("chat-direct").onclick = function(){ pick("direct"); };
		SK.$("chat-to").onchange = markpeer;
		SK.$("chat-nick").onkeydown = function(ev){
			if (ev.key === "Enter") join();
		};
		SK.$("chat-text").onkeydown = function(ev){
			if (ev.key === "Enter") send();
		};
		if (!ws) showgate();
	}

	function join(){
		if (ws || opening) return;
		var nick = (SK.$("chat-nick").value || "").replace(/^\s+|\s+$/g, "");
		var gender = SK.$("chat-gender").value || "";
		if (!nick) { setmsg(label("web.chat.err.nick", "请填写昵称"), true); return; }
		if (nick.length > 16) { setmsg(label("web.chat.err.long", "昵称最多 16 个字"), true); return; }
		try {
			localStorage.setItem(NICK, nick);
			localStorage.setItem(GENDER, gender);
		}
		catch (e) {}
		var url = (location.protocol === "https:" ? "wss://" : "ws://") + location.host + "/chat";
		opening = 1;
		leaving = 0;
		setmsg("");
		var sock = new WebSocket(url);
		ws = sock;
		sock.onopen = function(){
			if (ws !== sock) return;
			opening = 0;
			startbeat(sock);
			sock.send(JSON.stringify({ op: "join", nick: nick, gender: gender }));
		};
		sock.onmessage = function(ev){
			if (ws !== sock) return;
			beatsince = 0;
			onmsg(ev.data);
		};
		sock.onerror = function(){
			if (ws === sock) opening = 0;
		};
		sock.onclose = function(){
			if (ws !== sock && !leaving) return;
			opening = 0;
			stopbeat();
			if (ws === sock) ws = null;
			me = 0;
			if (leaving) { leaving = 0; return; }
			if (SK.curtool === "chat") {
				setmsg(label("web.chat.err.closed", "连接已断开"), true);
				showgate();
			}
		};
	}

	function leave(){
		leaving = 1;
		opening = 0;
		stopbeat();
		offers = [];
		if (ws) {
			try { ws.close(); } catch (e) {}
			ws = null;
		}
		me = 0;
		showgate();
		setmsg("");
	}

	function startbeat(sock){
		stopbeat();
		beatsince = 0;
		beat = setInterval(function(){
			if (ws !== sock || sock.readyState !== 1) { stopbeat(); return; }
			if (beatsince && Date.now() - beatsince >= 5000) {
				stopbeat();
				try { sock.close(); } catch (e) {}
				return;
			}
			try {
				sock.send(JSON.stringify({ op: "ping" }));
				beatsince = Date.now();
			}
			catch (e) {
				stopbeat();
				try { sock.close(); } catch (e2) {}
			}
		}, 5000);
	}

	function stopbeat(){
		if (beat) clearInterval(beat);
		beat = 0;
		beatsince = 0;
	}

	function targetof(need){
		var sel = SK.$("chat-to");
		var to = sel && sel.value ? parseInt(sel.value, 10) : 0;
		if (!to) to = 0;
		var priv = !!(SK.$("chat-priv") && SK.$("chat-priv").checked);
		if ((need || priv) && !to) {
			setmsg(label("web.chat.err.to", "请选择一个在线的人"), true);
			return null;
		}
		return { to: to, priv: priv };
	}

	function stamp(obj, need){
		var t = targetof(need);
		if (!t) return false;
		if (t.to) obj.to = t.to;
		if (t.priv) obj.priv = true;
		return true;
	}

	function send(){
		if (!ws || ws.readyState !== 1) return;
		var text = (SK.$("chat-text").value || "").replace(/^\s+|\s+$/g, "");
		if (!text) return;
		if (text.length > 500) {
			setmsg(label("web.chat.err.text", "最多 500 个字"), true);
			return;
		}
		var body = { op: "say", text: text };
		if (!stamp(body, false)) return;
		ws.send(JSON.stringify(body));
		SK.$("chat-text").value = "";
		setmsg("");
	}

	function pick(kind){
		if (!ws || ws.readyState !== 1 || opening) return;
		if (kind === "direct" && !targetof(true)) return;
		var input = SK.$(kind === "image" ? "chat-img" : kind === "direct" ? "chat-directfile" : "chat-file");
		if (!input) return;
		input.value = "";
		input.onchange = function(){
			var f = input.files && input.files[0];
			if (f) upload(f, kind);
		};
		input.click();
	}

	function upload(file, kind){
		if (!ws || ws.readyState !== 1) return;
		if (!file || !file.size) {
			setmsg(label("web.chat.err.empty", "文件是空的"), true);
			return;
		}
		if (file.size > 8 * 1024 * 1024) {
			setmsg(label("web.chat.err.big", "太大了（最多 8 MB）"), true);
			return;
		}
		var fd = new FormData();
		fd.append("file", file, file.name || "file");
		var aim = kind === "direct" ? targetof(true) : null;
		if (kind === "direct" && !aim) return;
		fd.append("kind", kind === "image" ? "image" : "file");
		var xhr = new XMLHttpRequest();
		xhr.open("POST", "/chat/file");
		xhr.onload = function(){
			var m;
			try { m = JSON.parse(xhr.responseText); }
			catch (e) { setmsg(label("web.chat.err.bad", "消息无法识别"), true); return; }
			if (!m || m.code !== 100 || !m.id) {
				setmsg(errmsg(m && m.err), true);
				return;
			}
			if (!ws || ws.readyState !== 1) return;
			if (kind === "direct") {
				ws.send(JSON.stringify({ op: "offer", file: m.id, to: aim.to }));
			}
			else {
				var body = { op: "file", file: m.id };
				if (!stamp(body, false)) return;
				ws.send(JSON.stringify(body));
			}
			setmsg("");
		};
		xhr.onerror = function(){
			setmsg(label("web.chat.err.closed", "连接已断开"), true);
		};
		xhr.send(fd);
	}

	function onmsg(raw){
		var m;
		try { m = JSON.parse(raw); }
		catch (e) { return; }
		if (!m || !m.op) return;
		if (m.op === "hello") {
			me = m.id || 0;
			clearlog();
			showroom();
			setmsg("");
			return;
		}
		if (m.op === "hist") {
			var items = m.items || [];
			var i;
			for (i = 0; i < items.length; i++) {
				if (items[i] && items[i].op === "file") addfile(items[i]);
				else addsay(items[i]);
			}
			return;
		}
		if (m.op === "say") { addsay(m); return; }
		if (m.op === "file") { addfile(m); return; }
		if (m.op === "sys") { addsys(m); return; }
		if (m.op === "peers") { paintpeers(m.list || []); return; }
		if (m.op === "offer") { addoffer(m); return; }
		if (m.op === "xfer") { onxfer(m); return; }
		if (m.op === "pong") return;
		if (m.op === "err") setmsg(errmsg(m.code), true);
	}

	function wholine(m){
		var who = document.createElement("span");
		who.className = "who g-" + (m.gender || "x");
		var tail = "";
		if (m.to) tail += " → " + (m.tonick || "");
		if (m.priv) tail += " · " + label("web.chat.priv", "私密");
		who.textContent = (m.nick || "") + gendertag(m.gender) + tail;
		return who;
	}

	function addsay(m){
		var row = document.createElement("div");
		row.className = "chatline" + (m.priv ? " priv" : "") + (m.id === me ? " mine" : "");
		var who = wholine(m);
		var body = document.createElement("span");
		body.textContent = m.text || "";
		row.appendChild(who);
		row.appendChild(body);
		pushlog(row);
	}

	function addfile(m){
		var row = document.createElement("div");
		row.className = "chatline" + (m.priv ? " priv" : "") + (m.id === me ? " mine" : "");
		row.appendChild(wholine(m));
		var id = m.file || "";
		if (!/^[a-f0-9]{32}$/.test(id)) {
			pushlog(row);
			return;
		}
		var url = "/chat/file/" + id;
		if (m.kind === "image") {
			var img = document.createElement("img");
			img.className = "chatpic";
			img.alt = m.name || "";
			img.src = url;
			row.appendChild(img);
		}
		else {
			var a = document.createElement("a");
			a.className = "chatfile";
			a.href = url;
			a.download = m.name || "file";
			a.textContent = (m.name || "file") + " (" + sizestr(m.size) + ")";
			row.appendChild(a);
		}
		pushlog(row);
	}

	function sizestr(n){
		n = n || 0;
		if (n < 1024) return n + " B";
		if (n < 1024 * 1024) return (n / 1024).toFixed(1) + " KB";
		return (n / 1024 / 1024).toFixed(1) + " MB";
	}

	function addsys(m){
		var row = document.createElement("div");
		row.className = "chatline sys";
		var verb = m.kind === "leave"
			? label("web.chat.left", "离开了")
			: label("web.chat.joined", "进入了");
		row.textContent = (m.nick || "") + " " + verb;
		pushlog(row);
	}

	function pushlog(row){
		var log = SK.$("chat-log");
		log.appendChild(row);
		log.scrollTop = log.scrollHeight;
	}

	function paintpeers(list){
		var box = SK.$("chat-peers");
		var sel = SK.$("chat-to");
		var cur = sel ? sel.value : "";
		while (box.firstChild) box.removeChild(box.firstChild);
		if (sel) {
			while (sel.options.length) sel.remove(0);
			var all = document.createElement("option");
			all.value = "";
			all.textContent = label("web.chat.all", "所有人");
			sel.appendChild(all);
		}
		var i;
		for (i = 0; i < list.length; i++) {
			var item = list[i];
			var p = document.createElement("div");
			p.className = "peer g-" + (item.gender || "x");
			p.textContent = (item.nick || "") + gendertag(item.gender);
			p.setAttribute("data-id", String(item.id || ""));
			p.onclick = (function(id){
				return function(){
					if (!sel || String(id) === String(me)) return;
					sel.value = String(id);
					markpeer();
				};
			})(item.id);
			box.appendChild(p);
			if (sel && item.id !== me) {
				var opt = document.createElement("option");
				opt.value = String(item.id);
				opt.textContent = item.nick || "";
				sel.appendChild(opt);
			}
		}
		if (sel) {
			sel.value = cur;
			if (sel.value !== cur) sel.value = "";
		}
		markpeer();
	}

	function markpeer(){
		var sel = SK.$("chat-to");
		var cur = sel ? sel.value : "";
		var box = SK.$("chat-peers");
		if (!box) return;
		var i;
		for (i = 0; i < box.children.length; i++) {
			var n = box.children[i];
			var base = n.className.replace(" on", "");
			n.className = (cur && n.getAttribute("data-id") === cur) ? base + " on" : base;
		}
	}

	function addoffer(m){
		offers.push(m);
		paintoffers();
	}

	function paintoffers(){
		var box = SK.$("chat-offers");
		if (!box) return;
		while (box.firstChild) box.removeChild(box.firstChild);
		var i;
		for (i = 0; i < offers.length; i++) {
			var o = offers[i];
			var row = document.createElement("div");
			row.className = "chatoffer";
			var t = document.createElement("span");
			t.textContent = (o.nick || "") + " " + label("web.chat.incoming", "发来文件")
				+ " " + (o.name || "") + " (" + sizestr(o.size) + ")";
			var ok = document.createElement("button");
			ok.type = "button";
			ok.className = "primary";
			ok.textContent = label("web.chat.accept", "接收");
			ok.onclick = (function(xfer){
				return function(){
					if (ws && ws.readyState === 1) ws.send(JSON.stringify({ op: "take", xfer: xfer }));
				};
			})(o.xfer);
			var no = document.createElement("button");
			no.type = "button";
			no.className = "ghost";
			no.textContent = label("web.chat.reject", "拒绝");
			no.onclick = (function(xfer){
				return function(){
					if (ws && ws.readyState === 1) ws.send(JSON.stringify({ op: "drop", xfer: xfer }));
				};
			})(o.xfer);
			row.appendChild(t);
			row.appendChild(ok);
			row.appendChild(no);
			box.appendChild(row);
		}
	}

	function onxfer(m){
		var i;
		var next = [];
		for (i = 0; i < offers.length; i++) {
			if (offers[i].xfer !== m.xfer) next.push(offers[i]);
		}
		offers = next;
		paintoffers();
		var name = m.name || "";
		if (m.state === "wait") addnote((m.tonick || "") + " " + label("web.chat.wait", "正在等对方接收") + " " + name);
		else if (m.state === "ok") {
			addnote(label("web.chat.got", "对方接收了") + " " + name);
			if (m.file && /^[a-f0-9]{32}$/.test(m.file)) {
				addlink(name, m.file);
				startdown(m.file, name);
			}
		}
		else addnote(label("web.chat.no", "对方拒绝了") + " " + name);
	}

	function startdown(id, name){
		var a = document.createElement("a");
		a.href = "/chat/file/" + id;
		a.download = name || "file";
		document.body.appendChild(a);
		a.click();
		document.body.removeChild(a);
	}

	function addnote(text){
		var row = document.createElement("div");
		row.className = "chatline sys";
		row.textContent = text;
		pushlog(row);
	}

	function addlink(name, id){
		var row = document.createElement("div");
		row.className = "chatline";
		var a = document.createElement("a");
		a.className = "chatfile";
		a.href = "/chat/file/" + id;
		a.download = name || "file";
		a.textContent = name || "file";
		row.appendChild(a);
		pushlog(row);
	}

	function gendertag(g){
		if (g === "m") return " · " + label("web.chat.m", "男");
		if (g === "f") return " · " + label("web.chat.f", "女");
		return "";
	}

	function errmsg(code){
		if (code === "nick") return label("web.chat.err.nick", "请填写昵称");
		if (code === "long") return label("web.chat.err.long", "昵称最多 16 个字");
		if (code === "used") return label("web.chat.err.used", "这个昵称已经有人用了");
		if (code === "full") return label("web.chat.err.full", "人满了");
		if (code === "fast") return label("web.chat.err.fast", "说得太快");
		if (code === "text") return label("web.chat.err.text", "最多 500 个字");
		if (code === "big") return label("web.chat.err.big", "太大了（最多 8 MB）");
		if (code === "empty") return label("web.chat.err.empty", "文件是空的");
		if (code === "img") return label("web.chat.err.img", "这不是图片");
		if (code === "file") return label("web.chat.err.file", "文件已经不在了");
		if (code === "to") return label("web.chat.err.to", "请选择一个在线的人");
		if (code === "join") return label("web.chat.err.nick", "请先进入");
		return label("web.chat.err.bad", "消息无法识别");
	}

	function label(key, fallback){
		var s = SK.t && SK.t(key);
		return s || fallback;
	}

	function clearlog(){
		var log = SK.$("chat-log");
		while (log.firstChild) log.removeChild(log.firstChild);
		var peers = SK.$("chat-peers");
		while (peers.firstChild) peers.removeChild(peers.firstChild);
		offers = [];
		paintoffers();
		var sel = SK.$("chat-to");
		if (sel) sel.value = "";
	}

	function showgate(){
		SK.$("chat-gate").hidden = false;
		SK.$("chat-room").hidden = true;
	}

	function showroom(){
		SK.$("chat-gate").hidden = true;
		SK.$("chat-room").hidden = false;
		var text = SK.$("chat-text");
		if (text) text.focus();
	}

	function setmsg(s, bad){
		SK.msg("chat-msg", s || "", !!bad);
	}

	SK.chatopen = open;
	SK.chatclose = leave;
})();
