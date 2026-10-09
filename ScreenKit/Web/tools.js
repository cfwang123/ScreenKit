window.sktools = (function(){
	var ocrPacks = [];
	var ttsItems = [];
	var asrItems = [];
	var asrRec = null;
	var curcat = "all";
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
		$("qr-make").onclick = qrmake;
		$("qr-scan").onclick = qrscan;
		$("ocr-pack").onchange = fillocrmodels;
		$("ocr-go").onclick = ocrgo;
		$("ocr-copy").onclick = function(){ copytext($("ocr-out").value, "ocr-msg"); };
		$("tts-eng").onchange = fillttsmodels;
		$("tts-model").onchange = fillttsvoices;
		$("tts-go").onclick = ttsgo;
		$("asr-eng").onchange = fillasrmodels;
		$("asr-filego").onclick = asrfile;
		$("asr-rec").onclick = asrrec;
		$("asr-copy").onclick = function(){ copytext($("asr-out").value, "asr-msg"); };
		$("zh-in").addEventListener("keydown", function(ev){
			if (ev.key === "Enter" && (ev.ctrlKey || ev.metaKey)) conv("trad");
		});
		$("tool-q").oninput = paintcats;
		$("box-go").onclick = utilrun;
		$("box-copy").onclick = function(){ copytext($("box-out").value, "box-msg"); };
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
			var cat = el.getAttribute && el.getAttribute("data-cat");
			if (cat) {
				curcat = cat;
				$("tool-q").value = "";
				show("home");
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
		if (name.indexOf("u-") === 0) {
			openutil(name);
			name = "box";
		}
		var panels = document.querySelectorAll(".panel");
		var i;
		for (i = 0; i < panels.length; i++)
			panels[i].className = panels[i].id === name ? "panel on" : "panel";
		$("back").hidden = name === "home";
		if (name === "home") paintcats();
		if (name === "ocr") loadoocr();
		if (name === "tts") loadtts();
		if (name === "asr") loadasr();
	}

	function paintcats(){
		var q = ($("tool-q").value || "").replace(/^\s+|\s+$/g, "").toLowerCase();
		var cats = document.querySelectorAll("#cats button");
		var i, title = "常用工具";
		for (i = 0; i < cats.length; i++) {
			var on = !q && cats[i].getAttribute("data-cat") === curcat;
			cats[i].className = on ? "on" : "";
			if (on) title = cats[i].getAttribute("data-title") || title;
		}
		if (q) title = "搜索";
		$("cat-title").textContent = title;
		var cards = document.querySelectorAll("#cards button");
		var n = 0;
		for (i = 0; i < cards.length; i++) {
			var name = (cards[i].getAttribute("data-name") || "").toLowerCase();
			var cat = cards[i].getAttribute("data-cat") || "";
			var hit = !q || name.indexOf(q) >= 0;
			var incat = curcat === "all" || cat === curcat;
			var vis = q ? hit : incat;
			cards[i].hidden = !vis;
			if (vis) n++;
		}
		$("tool-none").hidden = n !== 0;
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

	function qrmake(){
		post("/api/qrmake", {
			text: $("qr-in").value,
			format: $("qr-fmt").value,
			encoding: $("qr-enc").value,
		}, function(data){
			var img = $("qr-img");
			if (!data || !data.png) {
				img.removeAttribute("src");
				img.hidden = true;
				msg("qr-msg", "没有图片", true);
				return;
			}
			img.src = "data:image/png;base64," + data.png;
			img.hidden = false;
			msg("qr-msg", "已生成");
		}, "qr-msg");
	}

	function qrscan(){
		var file = $("qr-file").files && $("qr-file").files[0];
		if (!file) {
			msg("qr-smsg", "请选择图片", true);
			return;
		}
		var reader = new FileReader();
		reader.onload = function(){
			var raw = String(reader.result || "");
			var comma = raw.indexOf(",");
			var b64 = comma >= 0 ? raw.substring(comma + 1) : raw;
			post("/api/qrscan", { base64: b64, format: "dict" }, function(data){
				var lines = [];
				var i;
				if (typeof data === "string") {
					$("qr-out").value = data;
					return;
				}
				var arr = data;
				if (!arr || typeof arr.length !== "number") arr = [];
				for (i = 0; i < arr.length; i++) {
					var item = arr[i] || {};
					lines.push((item.type || "") + "  " + (item.text || ""));
				}
				$("qr-out").value = lines.join("\n");
				if (!lines.length) msg("qr-smsg", "未检测到条码或二维码", true);
			}, "qr-smsg");
		};
		reader.onerror = function(){ msg("qr-smsg", "读图失败", true); };
		reader.readAsDataURL(file);
	}

	function get(url, done, mid){
		msg(mid, "");
		var xhr = new XMLHttpRequest();
		xhr.open("GET", url, true);
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
			done(jo.data);
		};
		xhr.onerror = function(){ msg(mid, "网络错误", true); };
		xhr.send();
	}

	function fillsel(sel, rows, pick){
		sel.innerHTML = "";
		var i;
		for (i = 0; i < rows.length; i++) {
			var op = document.createElement("option");
			op.value = rows[i].value;
			op.textContent = rows[i].label;
			sel.appendChild(op);
		}
		if (pick) sel.value = pick;
		if (sel.selectedIndex < 0 && sel.options.length) sel.selectedIndex = 0;
	}

	function readfile(file, done, mid){
		var reader = new FileReader();
		reader.onload = function(){
			var raw = String(reader.result || "");
			var comma = raw.indexOf(",");
			done(comma >= 0 ? raw.substring(comma + 1) : raw);
		};
		reader.onerror = function(){ msg(mid, "读取文件失败", true); };
		reader.readAsDataURL(file);
	}

	function loadoocr(){
		if (ocrPacks.length) return;
		get("/api/ocr/models", function(data){
			ocrPacks = (data && data.packs) || [];
			var rows = [];
			var i;
			for (i = 0; i < ocrPacks.length; i++)
				rows.push({ value: ocrPacks[i].id, label: ocrPacks[i].name || ocrPacks[i].id });
			var cur = (data && data.current) || {};
			fillsel($("ocr-pack"), rows, cur.pack || "");
			if (cur.device) $("ocr-dev").value = cur.device;
			fillocrmodels(cur.language || "");
		}, "ocr-msg");
	}

	function ocrpack(){
		var id = $("ocr-pack").value;
		var i;
		for (i = 0; i < ocrPacks.length; i++)
			if (ocrPacks[i].id === id) return ocrPacks[i];
		return null;
	}

	function fillocrmodels(prefer){
		var pack = ocrpack();
		var models = (pack && pack.models) || [];
		var rows = [];
		var i;
		for (i = 0; i < models.length; i++)
			rows.push({ value: models[i].id, label: models[i].name || models[i].id });
		var pick = typeof prefer === "string" ? prefer : "";
		fillsel($("ocr-model"), rows, pick);
		var win = !pack || pack.engine === "winocr";
		$("ocr-dev-lab").style.display = win ? "none" : "";
		$("ocr-model-lab").firstChild.nodeValue = win ? "语言" : "模型";
	}

	function ocrgo(){
		var file = $("ocr-file").files && $("ocr-file").files[0];
		if (!file) {
			msg("ocr-msg", "请选择图片", true);
			return;
		}
		var pack = ocrpack();
		if (!pack) {
			msg("ocr-msg", "没有可用的识别引擎", true);
			return;
		}
		readfile(file, function(b64){
			post("/api/ocr", {
				base64: b64,
				options: {
					"ocr.engine": pack.engine,
					"ocr.pack": pack.id,
					"ocr.language": $("ocr-model").value,
					"ocr.device": $("ocr-dev").value,
					"data.format": "text",
				},
			}, function(data){
				$("ocr-out").value = typeof data === "string" ? data : "";
			}, "ocr-msg");
		}, "ocr-msg");
	}

	function ttslabel(engine){
		if (engine === "sherpa") return "离线模型";
		if (engine === "sapi") return "SAPI";
		if (engine === "winrt") return "Windows 语音";
		if (engine === "edge") return "Edge 在线";
		return engine || "";
	}

	function loadtts(){
		if (ttsItems.length) return;
		get("/api/tts/models", function(data){
			ttsItems = data && data.length != null ? data : [];
			var seen = {};
			var rows = [];
			var i;
			for (i = 0; i < ttsItems.length; i++) {
				var eng = ttsItems[i].engine || "";
				if (seen[eng]) continue;
				seen[eng] = true;
				rows.push({ value: eng, label: ttslabel(eng) });
			}
			fillsel($("tts-eng"), rows, "");
			fillttsmodels();
		}, "tts-msg");
	}

	function fillttsmodels(){
		var eng = $("tts-eng").value;
		var rows = [];
		var i;
		for (i = 0; i < ttsItems.length; i++) {
			if ((ttsItems[i].engine || "") !== eng) continue;
			rows.push({ value: String(i), label: ttsItems[i].name || eng });
		}
		fillsel($("tts-model"), rows, "");
		fillttsvoices();
	}

	function ttsitem(){
		var n = parseInt($("tts-model").value, 10);
		if (isNaN(n) || n < 0 || n >= ttsItems.length) return null;
		return ttsItems[n];
	}

	function fillttsvoices(){
		var item = ttsitem();
		var speakers = (item && item.speakers) || [];
		var rows = [];
		var i;
		for (i = 0; i < speakers.length; i++) {
			var sp = speakers[i] || {};
			var label = sp.name || sp.key || String(i);
			if (sp.lang) label += " · " + sp.lang;
			rows.push({ value: String(i), label: label });
		}
		fillsel($("tts-voice"), rows, "");
	}

	function ttsgo(){
		var item = ttsitem();
		if (!item) {
			msg("tts-msg", "没有可用的语音", true);
			return;
		}
		var speakers = item.speakers || [];
		var vi = parseInt($("tts-voice").value, 10);
		var sp = speakers[vi] || {};
		var speed = parseFloat($("tts-speed").value);
		if (isNaN(speed)) speed = 1;
		post("/api/tts", {
			text: $("tts-in").value,
			engine: item.engine,
			model: item.name,
			voice: sp.key || sp.name || "",
			speaker_id: sp.id != null ? sp.id : vi,
			speed: speed,
		}, function(data){
			var audio = $("tts-audio");
			if (!data || !data.wav_base64) {
				msg("tts-msg", "没有音频", true);
				return;
			}
			audio.src = "data:audio/wav;base64," + data.wav_base64;
			audio.hidden = false;
			audio.play();
		}, "tts-msg");
	}

	function loadasr(){
		if (asrItems.length) return;
		get("/api/asr/models", function(data){
			asrItems = data && data.length != null ? data : [];
			var hasOff = false;
			var hasWin = false;
			var i;
			for (i = 0; i < asrItems.length; i++) {
				if (asrItems[i].streaming) continue;
				if (asrItems[i].type === "Windows") hasWin = true;
				else hasOff = true;
			}
			var rows = [];
			if (hasOff) rows.push({ value: "offline", label: "离线模型" });
			if (hasWin) rows.push({ value: "windows", label: "Windows 语音识别" });
			fillsel($("asr-eng"), rows, "");
			fillasrmodels();
		}, "asr-msg");
	}

	function fillasrmodels(){
		var win = $("asr-eng").value === "windows";
		var rows = [];
		var i;
		for (i = 0; i < asrItems.length; i++) {
			var it = asrItems[i];
			if (!it || it.streaming) continue;
			var isWin = it.type === "Windows";
			if (isWin !== win) continue;
			rows.push({ value: it.name || "", label: it.name || "" });
		}
		fillsel($("asr-model"), rows, "");
	}

	function postasr(b64, filename){
		post("/api/asr", {
			base64: b64,
			filename: filename || "",
			model: $("asr-model").value,
			lang: $("asr-lang").value,
		}, function(data){
			$("asr-out").value = (data && data.text) || "";
		}, "asr-msg");
	}

	function asrfile(){
		var file = $("asr-file").files && $("asr-file").files[0];
		if (!file) {
			msg("asr-msg", "请选择音频", true);
			return;
		}
		readfile(file, function(b64){ postasr(b64, file.name); }, "asr-msg");
	}

	function setasr(on){
		var b = $("asr-rec");
		var i = b.querySelector("i");
		var s = b.querySelector("span");
		if (i) i.className = on ? "fa-solid fa-stop" : "fa-solid fa-microphone";
		if (s) s.textContent = on ? "停止并识别" : "录音";
	}

	function asrrec(){
		if (asrRec) {
			asrRec.stop();
			return;
		}
		if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
			msg("asr-msg", "浏览器不能录音", true);
			return;
		}
		navigator.mediaDevices.getUserMedia({ audio: true }).then(function(stream){
			var chunks = [];
			var rec;
			try { rec = new MediaRecorder(stream); }
			catch (e) {
				stream.getTracks().forEach(function(tr){ tr.stop(); });
				msg("asr-msg", "无法开始录音", true);
				return;
			}
			asrRec = rec;
			setasr(true);
			msg("asr-msg", "正在录音");
			rec.ondataavailable = function(ev){
				if (ev.data && ev.data.size) chunks.push(ev.data);
			};
			rec.onstop = function(){
				stream.getTracks().forEach(function(tr){ tr.stop(); });
				asrRec = null;
				setasr(false);
				var blob = new Blob(chunks, { type: rec.mimeType || "audio/webm" });
				readfile(blob, function(b64){ postasr(b64, "rec.webm"); }, "asr-msg");
			};
			rec.start();
		}, function(){
			msg("asr-msg", "无法使用麦克风", true);
		});
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
