(function(){
	var rows = [];
	var gen = 0;
	var busy = false;

	SK.regutil("u-chrome", {
		title: "Chrome 模型",
		body: function(){
			return '<div class="uquery chromebar">'
				+ '<select id="chrome-kind">'
				+ '<option value="all">全部</option>'
				+ '<option value="on">已安装</option>'
				+ '<option value="tr">翻译</option>'
				+ '<option value="tts">语音</option>'
				+ '</select>'
				+ '<button type="button" id="chrome-refresh">刷新</button>'
				+ '<button type="button" id="chrome-install">安装所选</button>'
				+ '<button type="button" id="chrome-remove">卸载所选</button>'
				+ '<span id="chrome-msg" class="msg"></span></div>'
				+ '<p class="hint">翻译模型按本机文件显示是否已安装。刷新会打开隐藏的 Chrome，查询语音包。</p>'
				+ '<div class="usbscroll"><table class="usblist" id="chrome-tab"></table></div>';
		},
		run: function(){
			throw new Error("没有这个工具");
		},
		open: function(){
			rows = [];
			F.id("chrome-kind").onchange = paint;
			F.id("chrome-refresh").onclick = function(){ load(true); };
			F.id("chrome-install").onclick = function(){ apply("install"); };
			F.id("chrome-remove").onclick = function(){ apply("remove"); };
			load(false);
		},
		hideGo: true,
		live: false,
		hideOut: true
	});

	function load(probe){
		var n = ++gen;
		busy = true;
		paint();
		SK.get("/api/chrome/packs" + (probe ? "?probe=1" : ""), function(data){
			if (n !== gen) return;
			busy = false;
			rows = data && data.items ? data.items : [];
			var msg = counton(rows) + " 个已安装 / " + rows.length + " 项";
			if (probe) msg += " · 已查询语音包";
			else msg += " · 语音包尚未查询";
			if (data && data.log) msg += " · " + data.log;
			F.id("chrome-msg").textContent = msg;
			paint();
		}, "chrome-msg", function(){
			if (n !== gen) return;
			busy = false;
			paint();
		});
		F.id("chrome-msg").textContent = probe ? "正在打开 Chrome 查询…" : "正在读取本机模型…";
	}

	function apply(op){
		var ids = picked();
		if (!ids.length) {
			F.id("chrome-msg").textContent = "先勾选要处理的项";
			F.id("chrome-msg").className = "msg bad";
			return;
		}
		var n = ++gen;
		busy = true;
		paint();
		F.id("chrome-msg").textContent = op === "remove" ? "正在卸载…" : "正在安装…";
		F.id("chrome-msg").className = "msg";
		var xhr = new XMLHttpRequest();
		xhr.open("POST", "/api/chrome/packs", true);
		F.hdr(xhr, "Content-Type", "application/json;charset=utf-8");
		xhr.onload = function(){
			if (n !== gen) return;
			busy = false;
			var jo;
			try { jo = F.parse(xhr.responseText); }
			catch (e) {
				F.id("chrome-msg").textContent = "响应不是 JSON";
				F.id("chrome-msg").className = "msg bad";
				paint();
				return;
			}
			if (!jo || jo.code !== 100) {
				var err = jo && jo.data;
				F.id("chrome-msg").textContent = typeof err === "string" && err ? err : "失败";
				F.id("chrome-msg").className = "msg bad";
				paint();
				return;
			}
			var data = jo.data || {};
			rows = data.items ? data.items : rows;
			var msg = (op === "remove" ? "已卸载" : "已安装") + " · " + counton(rows) + " 个已安装";
			if (data.log) msg += " · " + data.log;
			F.id("chrome-msg").textContent = msg;
			F.id("chrome-msg").className = "msg";
			paint();
		};
		xhr.onerror = function(){
			if (n !== gen) return;
			busy = false;
			F.id("chrome-msg").textContent = "网络错误";
			F.id("chrome-msg").className = "msg bad";
			paint();
		};
		xhr.send(F.json({ op: op, ids: ids }));
	}

	function picked(){
		var boxes = F.id("chrome-tab").getElementsByTagName("input");
		var ids = [];
		var i;
		for (i = 0; i < boxes.length; i++) {
			if (boxes[i].checked && boxes[i].value) ids.push(boxes[i].value);
		}
		return ids;
	}

	function paint(){
		var tab = F.id("chrome-tab");
		if (!tab) return;
		var kind = F.id("chrome-kind").value || "all";
		var html = ["<thead><tr><th></th><th>种类</th><th>模型</th><th>说明</th><th>大小</th><th>状态</th></tr></thead><tbody>"];
		var shown = 0;
		var i;
		for (i = 0; i < rows.length; i++) {
			var r = rows[i];
			if (!showrow(r, kind)) continue;
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
		var bar = [F.id("chrome-refresh"), F.id("chrome-install"), F.id("chrome-remove"), F.id("chrome-kind")];
		for (i = 0; i < bar.length; i++) if (bar[i]) bar[i].disabled = busy;
	}

	function showrow(r, kind){
		if (!r) return false;
		if (kind === "on") return r.state === "installed" || r.state === "partial";
		if (kind === "tr" || kind === "tts") return r.kind === kind;
		return true;
	}

	function kindtext(kind){
		if (kind === "tr") return "翻译";
		if (kind === "tts") return "语音";
		return kind || "";
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

	function esc(s){
		return String(s == null ? "" : s)
			.replace(/&/g, "&amp;")
			.replace(/</g, "&lt;")
			.replace(/"/g, "&quot;");
	}
})();
