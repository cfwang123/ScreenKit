(function(){
	var rows = [];
	var gen = 0;
	var busy = false;
	var kind = document.getElementById("kind");
	var lang = document.getElementById("lang");
	var msg = document.getElementById("msg");
	var tab = document.getElementById("tab");
	var PREFER = ["zh", "cht", "en", "ja", "ko", "yue", "fr", "de", "es", "pt", "ru"];
	var LNAME = {
		zh: "中文", cht: "繁体中文", en: "英语", ja: "日语", ko: "韩语", yue: "粤语",
		fr: "法语", de: "德语", es: "西班牙语", pt: "葡萄牙语", ru: "俄语",
		ar: "阿拉伯语", bn: "孟加拉语", hi: "印地语", it: "意大利语", nl: "荷兰语",
		pl: "波兰语", th: "泰语", tr: "土耳其语", vi: "越南语", bg: "保加利亚语",
		cs: "捷克语", da: "丹麦语", el: "希腊语", fi: "芬兰语", hr: "克罗地亚语",
		hu: "匈牙利语", id: "印尼语", he: "希伯来语", lt: "立陶宛语", no: "挪威语",
		ro: "罗马尼亚语", sk: "斯洛伐克语", sl: "斯洛文尼亚语", sv: "瑞典语",
		uk: "乌克兰语", kn: "卡纳达语", ta: "泰米尔语", te: "泰卢固语", mr: "马拉地语"
	};

	kind.onchange = paint;
	lang.onchange = paint;
	document.getElementById("refresh").onclick = function(){ load(); };
	document.getElementById("install").onclick = function(){ apply("install"); };
	document.getElementById("remove").onclick = function(){ apply("remove"); };
	load();

	function load(){
		var n = ++gen;
		busy = true;
		paint();
		say("正在读取本机模型…", false);
		get("/api/chrome/packs", function(data){
			if (n !== gen) return;
			rows = data && data.items ? data.items : [];
			say("正在查询这个 Chrome 的语音…", false);
			queryvoices(function(){
				if (n !== gen) return;
				busy = false;
				filllang();
				say(counton(rows) + " 个已安装 / " + rows.length + " 项", false);
				paint();
			});
		}, function(){
			if (n !== gen) return;
			busy = false;
			paint();
		});
	}

	function queryvoices(done){
		if (!window.speechSynthesis) {
			markvoices([]);
			done();
			return;
		}
		var voices = speechSynthesis.getVoices();
		if (voices && voices.length) {
			markvoices(voices);
			done();
			return;
		}
		var finished = false;
		function finish(){
			if (finished) return;
			finished = true;
			speechSynthesis.onvoiceschanged = null;
			markvoices(speechSynthesis.getVoices() || []);
			done();
		}
		speechSynthesis.onvoiceschanged = finish;
		setTimeout(finish, 1200);
	}

	function markvoices(voices){
		var i;
		var j;
		for (i = 0; i < rows.length; i++) {
			var r = rows[i];
			if (!r || r.kind !== "tts") continue;
			var lang = String(r.id || "").replace(/^tts:/i, "");
			var hit = false;
			for (j = 0; j < voices.length; j++) {
				var v = voices[j];
				if (!v || !v.localService) continue;
				if (samelang(v.lang, lang)) {
					hit = true;
					break;
				}
			}
			r.state = hit ? "installed" : "missing";
		}
	}

	function samelang(a, b){
		a = String(a || "").toLowerCase().replace(/_/g, "-");
		b = String(b || "").toLowerCase().replace(/_/g, "-");
		if (a === b) return true;
		if ((a === "zh-hk" || a === "yue-hk" || a === "yue") && (b === "zh-hk" || b === "yue-hk" || b === "yue"))
			return true;
		return false;
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
			filllang();
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
		var want = lang.value || "";
		var html = ["<thead><tr><th></th><th>种类</th><th>模型</th><th>说明</th><th>大小</th><th>状态</th></tr></thead><tbody>"];
		var shown = 0;
		var i;
		for (i = 0; i < rows.length; i++) {
			var r = rows[i];
			if (!showrow(r, filter, want)) continue;
			shown++;
			html.push("<tr><td><input type=\"checkbox\" value=\"" + esc(r.id) + "\"" + (busy ? " disabled" : "") + "></td>"
				+ "<td>" + esc(kindtext(r.kind)) + "</td>"
				+ "<td>" + esc(r.title) + "</td>"
				+ "<td>" + esc(r.detail) + "</td>"
				+ "<td>" + esc(r.sizeText || "") + "</td>"
				+ "<td><span class=\"st " + statecls(r.state) + "\">" + esc(statetext(r.state)) + "</span></td></tr>");
		}
		if (!shown) html.push("<tr><td></td><td colspan=\"5\">没有匹配的项</td></tr>");
		html.push("</tbody>");
		tab.innerHTML = html.join("");
		var bar = [
			document.getElementById("refresh"),
			document.getElementById("install"),
			document.getElementById("remove"),
			kind,
			lang
		];
		for (i = 0; i < bar.length; i++) if (bar[i]) bar[i].disabled = busy;
	}

	function showrow(r, filter, want){
		if (!r) return false;
		if (want && !haslang(r, want)) return false;
		if (filter === "on") return r.state === "installed" || r.state === "partial";
		if (filter === "tr" || filter === "tts") return r.kind === filter;
		return true;
	}

	function haslang(r, want){
		var list = r.langs || [];
		var i;
		for (i = 0; i < list.length; i++) {
			if (list[i] === want) return true;
		}
		return false;
	}

	function filllang(){
		var prev = lang.value || "";
		var set = {};
		var i;
		var j;
		for (i = 0; i < rows.length; i++) {
			var list = rows[i] && rows[i].langs ? rows[i].langs : [];
			for (j = 0; j < list.length; j++) if (list[j]) set[list[j]] = true;
		}
		var codes = [];
		for (i = 0; i < PREFER.length; i++) {
			if (set[PREFER[i]]) {
				codes.push(PREFER[i]);
				delete set[PREFER[i]];
			}
		}
		var rest = [];
		for (var k in set) if (Object.prototype.hasOwnProperty.call(set, k)) rest.push(k);
		rest.sort();
		for (i = 0; i < rest.length; i++) codes.push(rest[i]);
		var html = ["<option value=\"\">全部语言</option>"];
		for (i = 0; i < codes.length; i++)
			html.push("<option value=\"" + esc(codes[i]) + "\">" + esc(langlabel(codes[i])) + "</option>");
		lang.innerHTML = html.join("");
		lang.value = prev;
		if (lang.value !== prev) lang.value = "";
	}

	function langlabel(code){
		return LNAME[code] || code;
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

	function statecls(state){
		if (state === "installed") return "on";
		if (state === "partial") return "part";
		if (state === "unknown") return "unk";
		return "off";
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
