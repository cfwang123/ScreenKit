(function(){
	var ws = null;
	var me = 0;
	var opening = 0;
	var leaving = 0;
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
		ws = new WebSocket(url);
		ws.onopen = function(){
			opening = 0;
			ws.send(JSON.stringify({ op: "join", nick: nick, gender: gender }));
		};
		ws.onmessage = function(ev){ onmsg(ev.data); };
		ws.onerror = function(){ opening = 0; };
		ws.onclose = function(){
			opening = 0;
			ws = null;
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
		if (ws) {
			try { ws.close(); } catch (e) {}
			ws = null;
		}
		me = 0;
		showgate();
		setmsg("");
	}

	function send(){
		if (!ws || ws.readyState !== 1) return;
		var text = (SK.$("chat-text").value || "").replace(/^\s+|\s+$/g, "");
		if (!text) return;
		if (text.length > 500) {
			setmsg(label("web.chat.err.text", "最多 500 个字"), true);
			return;
		}
		ws.send(JSON.stringify({ op: "say", text: text }));
		SK.$("chat-text").value = "";
		setmsg("");
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
			for (i = 0; i < items.length; i++) addsay(items[i]);
			return;
		}
		if (m.op === "say") { addsay(m); return; }
		if (m.op === "sys") { addsys(m); return; }
		if (m.op === "peers") { paintpeers(m.list || []); return; }
		if (m.op === "err") setmsg(errmsg(m.code), true);
	}

	function addsay(m){
		var row = document.createElement("div");
		row.className = "chatline" + (m.id === me ? " mine" : "");
		var who = document.createElement("span");
		who.className = "who g-" + (m.gender || "x");
		who.textContent = (m.nick || "") + gendertag(m.gender);
		var body = document.createElement("span");
		body.textContent = m.text || "";
		row.appendChild(who);
		row.appendChild(body);
		pushlog(row);
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
		while (box.firstChild) box.removeChild(box.firstChild);
		var i;
		for (i = 0; i < list.length; i++) {
			var p = document.createElement("div");
			p.className = "peer g-" + (list[i].gender || "x");
			p.textContent = (list[i].nick || "") + gendertag(list[i].gender);
			box.appendChild(p);
		}
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
