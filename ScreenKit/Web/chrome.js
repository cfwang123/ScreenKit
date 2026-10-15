(function(){
	var rows = [];
	var gen = 0;
	var busy = false;
	var kind = document.getElementById("kind");
	var msg = document.getElementById("msg");
	var tab = document.getElementById("tab");

	kind.onchange = paint;
	document.getElementById("refresh").onclick = function(){ load(true); };
	document.getElementById("install").onclick = function(){ apply("install"); };
	document.getElementById("remove").onclick = function(){ apply("remove"); };
	load(false);

	function load(probe){
		var n = ++gen;
		busy = true;
		paint();
		say(probe ? "正在打开 Chrome 查询…" : "正在读取本机模型…", false);
		get("/api/chrome/packs" + (probe ? "?probe=1" : ""), function(data){
			if (n !== gen) return;
			busy = false;
			rows = data && data.items ? data.items : [];
			var line = counton(rows) + " 个已安装 / " + rows.length + " 项";
			line += probe ? " · 已查询语音包" : " · 语音包尚未查询";
			if (data && data.log) line += " · " + data.log;
			say(line, false);
			paint();
		}, function(){
			if (n !== gen) return;
			busy = false;
			paint();
		});
	}

	function apply(op){
		var ids = picked();
		if (!ids.length) {
			say("先勾选要处理的项", true);
			return;
		}
		var n = ++gen;
		busy = true;
		paint();
		say(op === "remove" ? "正在卸载…" : "正在安装…", false);
		var xhr = new XMLHttpRequest();
		xhr.open("POST", "/api/chrome/packs", true);
		xhr.setRequestHeader("Content-Type", "application/json;charset=utf-8");
		xhr.onload = function(){
			if (n !== gen) return;
			busy = false;
			var jo;
			try { jo = JSON.parse(xhr.responseText); }
			catch (e) {
				say("响应不是 JSON", true);
				paint();
				return;
			}
			if (!jo || jo.code !== 100) {
				var err = jo && jo.data;
				say(typeof err === "string" && err ? err : "失败", true);
				paint();
				return;
			}
			var data = jo.data || {};
			rows = data.items ? data.items : rows;
			var line = (op === "remove" ? "已卸载" : "已安装") + " · " + counton(rows) + " 个已安装";
			if (data.log) line += " · " + data.log;
			say(line, false);
			paint();
		};
		xhr.onerror = function(){
			if (n !== gen) return;
			busy = false;
			say("网络错误", true);
			paint();
		};
		xhr.send(JSON.stringify({ op: op, ids: ids }));
	}

	function get(url, ok, fail){
		var xhr = new XMLHttpRequest();
		xhr.open("GET", url, true);
		xhr.onload = function(){
			var jo;
			try { jo = JSON.parse(xhr.responseText); }
			catch (e) {
				say("响应不是 JSON", true);
				if (fail) fail();
				return;
			}
			if (!jo || jo.code !== 100) {
				var err = jo && jo.data;
				say(typeof err === "string" && err ? err : "失败", true);
				if (fail) fail();
				return;
			}
			ok(jo.data);
		};
		xhr.onerror = function(){
			say("网络错误", true);
			if (fail) fail();
		};
		xhr.send();
	}

	function picked(){
		var boxes = tab.getElementsByTagName("input");
		var ids = [];
		var i;
		for (i = 0; i < boxes.length; i++) {
			if (boxes[i].checked && boxes[i].value) ids.push(boxes[i].value);
		}
		return ids;
	}

	function paint(){
		var filter = kind.value || "all";
		var html = ["<thead><tr><th></th><th>种类</th><th>模型</th><th>说明</th><th>大小</th><th>状态</th></tr></thead><tbody>"];
		var shown = 0;
		var i;
		for (i = 0; i < rows.length; i++) {
			var r = rows[i];
			if (!showrow(r, filter)) continue;
			shown++;
			html.push("<tr><td><input type=\"checkbox\" value=\"" + esc(r.id) + "\"" + (busy ? " disabled" : "") + "></td>"
				+ "<td>" + esc(kindtext(r.kind)) + "</td>"
				+ "<td>" + esc(r.title) + "</td>"
				+ "<td>" + esc(r.detail) + "</td>"
				+ "<td>" + esc(r.sizeText || "") + "</td>"
				+ "<td>" + esc(statetext(r.state)) + "</td></tr>");
		}
		if (!shown) html.push("<tr><td></td><td colspan=\"5\">没有匹配的项</td></tr>");
		html.push("</tbody>");
		tab.innerHTML = html.join("");
		var bar = [
			document.getElementById("refresh"),
			document.getElementById("install"),
			document.getElementById("remove"),
			kind
		];
		for (i = 0; i < bar.length; i++) if (bar[i]) bar[i].disabled = busy;
	}

	function showrow(r, filter){
		if (!r) return false;
		if (filter === "on") return r.state === "installed" || r.state === "partial";
		if (filter === "tr" || filter === "tts") return r.kind === filter;
		return true;
	}

	function kindtext(k){
		if (k === "tr") return "翻译";
		if (k === "tts") return "语音";
		return k || "";
	}

	function statetext(state){
		if (state === "installed") return "已安装";
		if (state === "partial") return "部分";
		if (state === "unknown") return "未查询";
		return "未安装";
	}

	function counton(list){
		var n = 0;
		var i;
		for (i = 0; i < list.length; i++) {
			if (list[i] && (list[i].state === "installed" || list[i].state === "partial")) n++;
		}
		return n;
	}

	function say(text, bad){
		msg.textContent = text || "";
		msg.className = bad ? "bad" : "";
	}

	function esc(s){
		return String(s == null ? "" : s)
			.replace(/&/g, "&amp;")
			.replace(/</g, "&lt;")
			.replace(/"/g, "&quot;");
	}
})();
