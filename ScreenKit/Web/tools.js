window.sktools = (function(){
	var t = {
		show: show,
	};
	boot();
	return t;

	function boot(){
		var today = new Date();
		var month = today.getMonth() + 1;
		var day = today.getDate();
		$("cal-date").value = today.getFullYear()
			+ "-" + (month < 10 ? "0" : "") + month
			+ "-" + (day < 10 ? "0" : "") + day;
		document.addEventListener("click", onclick);
		$("zh-trad").onclick = function(){ conv("trad"); };
		$("zh-simp").onclick = function(){ conv("simp"); };
		$("zh-copy").onclick = function(){ copytext($("zh-out").value, "zh-msg"); };
		$("cal-go").onclick = cal;
		$("yo-go").onclick = yomi;
		$("yo-copy").onclick = function(){ copytext($("yo-yomi").value, "yo-msg"); };
		$("tx-copy").onclick = function(){ copytext($("tx-out").value, "tx-msg"); };
		$("zh-in").addEventListener("keydown", function(ev){
			if (ev.key === "Enter" && (ev.ctrlKey || ev.metaKey)) conv("trad");
		});
	}

	function onclick(ev){
		var el = ev.target;
		while (el && el !== document.body) {
			if (el.id === "tx-ops") return;
			var tool = el.getAttribute && el.getAttribute("data-tool");
			if (tool) {
				show(tool);
				return;
			}
			var op = el.getAttribute && el.getAttribute("data-op");
			if (op) {
				textop(op);
				return;
			}
			el = el.parentNode;
		}
	}

	function show(name){
		var panels = document.querySelectorAll(".panel");
		var i;
		for (i = 0; i < panels.length; i++)
			panels[i].className = panels[i].id === name ? "panel on" : "panel";
		var nav = document.querySelectorAll("#nav button");
		for (i = 0; i < nav.length; i++)
			nav[i].className = nav[i].getAttribute("data-tool") === name ? "on" : "";
	}

	function $(id){
		return document.getElementById(id);
	}

	function msg(id, text, bad){
		var el = $(id);
		el.textContent = text || "";
		el.className = bad ? "msg bad" : "msg";
	}

	function post(url, body, done, mid){
		msg(mid, "");
		var xhr = new XMLHttpRequest();
		xhr.open("POST", url, true);
		xhr.setRequestHeader("Content-Type", "application/json;charset=utf-8");
		xhr.onload = function(){
			var jo;
			try { jo = JSON.parse(xhr.responseText); }
			catch (e) {
				msg(mid, "响应不是 JSON", true);
				return;
			}
			if (!jo || jo.code !== 100) {
				var err = jo && jo.data;
				msg(mid, typeof err === "string" && err ? err : "失败", true);
				return;
			}
			done(jo.data || {});
		};
		xhr.onerror = function(){ msg(mid, "网络错误", true); };
		xhr.send(JSON.stringify(body));
	}

	function conv(to){
		post("/api/zhconv", { text: $("zh-in").value, to: to }, function(data){
			$("zh-out").value = data.text || "";
		}, "zh-msg");
	}

	function cal(){
		post("/api/calendar", {
			date: $("cal-date").value,
			cal: $("cal-id").value,
		}, function(data){
			var rows = [
				["公历", data.gregorian],
				["干支", data.ganzhi],
				["纪年", data.era],
				["年", data.year],
				["月", data.month],
				["日", data.day],
				["星期", data.week],
				["闰月", data.leap ? "是" : "否"],
			];
			var html = "";
			var i;
			for (i = 0; i < rows.length; i++) {
				if (!rows[i][1] && rows[i][0] !== "闰月") continue;
				html += "<dt>" + rows[i][0] + "</dt><dd>" + esc(rows[i][1]) + "</dd>";
			}
			$("cal-out").innerHTML = html;
		}, "cal-msg");
	}

	function yomi(){
		post("/api/jpyomi", {
			text: $("yo-in").value,
			mono: $("yo-mono").checked,
		}, function(data){
			$("yo-ruby").value = data.ruby || "";
			$("yo-yomi").value = data.yomi || "";
		}, "yo-msg");
	}

	function textop(op){
		post("/api/text", { text: $("tx-in").value, op: op }, function(data){
			$("tx-out").value = data.text || "";
		}, "tx-msg");
	}

	function copytext(text, mid){
		if (!text) {
			msg(mid, "没有可复制的内容", true);
			return;
		}
		if (navigator.clipboard && navigator.clipboard.writeText) {
			navigator.clipboard.writeText(text).then(function(){
				msg(mid, "已复制");
			}, function(){
				fallback(text, mid);
			});
			return;
		}
		fallback(text, mid);
	}

	function fallback(text, mid){
		var ta = document.createElement("textarea");
		ta.value = text;
		document.body.appendChild(ta);
		ta.select();
		try {
			document.execCommand("copy");
			msg(mid, "已复制");
		}
		catch (e) {
			msg(mid, "复制失败", true);
		}
		document.body.removeChild(ta);
	}

	function esc(s){
		return String(s == null ? "" : s)
			.replace(/&/g, "&amp;")
			.replace(/</g, "&lt;")
			.replace(/>/g, "&gt;");
	}
})();
