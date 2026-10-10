window.sktools = (function(){
	var ocrPacks = [];
	var ocrPic = null;
	var ocrPicUrl = "";
	var ttsItems = [];
	var asrItems = [];
	var asrRec = null;
	var curcat = "all";
	var curtool = "home";
	var favs = [];
	var util = "";
	var tick = null;
	var watchOn = false;
	var watch0 = 0;
	var watchAcc = 0;
	var LENU = [["mm", "毫米"], ["cm", "厘米"], ["m", "米"], ["km", "千米"], ["in", "英寸"], ["ft", "英尺"], ["yd", "码"], ["mi", "英里"]];
	var LENS = { mm: 1, cm: 10, m: 1000, km: 1000000, "in": 25.4, ft: 304.8, yd: 914.4, mi: 1609344 };
	var BYTEU = [["B", "B"], ["KB", "KB"], ["MB", "MB"], ["GB", "GB"], ["TB", "TB"]];
	var BYTES = { B: 1, KB: 1024, MB: 1048576, GB: 1073741824, TB: 1099511627776 };
	var TEMPU = [["C", "摄氏度"], ["F", "华氏度"], ["K", "开尔文"]];
	var MASSU = [["mg", "毫克"], ["g", "克"], ["kg", "千克"], ["t", "吨"], ["jin", "市斤"], ["liang", "市两"], ["lb", "磅"], ["oz", "盎司"]];
	var MASSS = { mg: 0.001, g: 1, kg: 1000, t: 1000000, jin: 500, liang: 50, lb: 453.59237, oz: 28.349523125 };
	var AREAU = [["mm2", "平方毫米"], ["cm2", "平方厘米"], ["m2", "平方米"], ["ha", "公顷"], ["mu", "亩"], ["km2", "平方千米"], ["ft2", "平方英尺"]];
	var AREAS = { mm2: 0.000001, cm2: 0.0001, m2: 1, ha: 10000, mu: 2000 / 3, km2: 1000000, ft2: 0.09290304 };
	var VOLU = [["mL", "毫升"], ["L", "升"], ["m3", "立方米"], ["gal", "美制加仑"]];
	var VOLS = { mL: 0.001, L: 1, m3: 1000, gal: 3.785411784 };
	var PRESSU = [["Pa", "帕"], ["kPa", "千帕"], ["MPa", "兆帕"], ["bar", "巴"], ["atm", "标准大气压"], ["psi", "磅力/平方英寸"]];
	var PRESSS = { Pa: 1, kPa: 1000, MPa: 1000000, bar: 100000, atm: 101325, psi: 6894.757293168 };
	var POWERU = [["W", "瓦"], ["kW", "千瓦"], ["MW", "兆瓦"], ["hp", "公制马力"]];
	var POWERS = { W: 1, kW: 1000, MW: 1000000, hp: 735.49875 };
	var TIMEU = [["ms", "毫秒"], ["s", "秒"], ["min", "分"], ["h", "小时"], ["d", "天"], ["week", "周"]];
	var TIMES = { ms: 0.001, s: 1, min: 60, h: 3600, d: 86400, week: 604800 };
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
		$("ocr-file").onchange = onocrfile;
		$("ocr-go").onclick = ocrgo;
		document.addEventListener("paste", onocrpaste);
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
		loadfav();
		paintstars();
	}

	function onclick(ev){
		var el = ev.target;
		while (el && el !== document.body) {
			if (el.id === "tx-ops") return;
			var fav = el.getAttribute && el.getAttribute("data-fav");
			if (fav) {
				togglefav(fav);
				return;
			}
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
		stoptick();
		curtool = name;
		if (name.indexOf("u-") === 0) {
			openutil(name);
			name = "box";
		}
		var panels = document.querySelectorAll(".panel");
		var i;
		for (i = 0; i < panels.length; i++)
			panels[i].className = panels[i].id === name ? "panel on" : "panel";
		$("back").hidden = name === "home";
		var favbtn = $("tool-fav");
		if (curtool === "home") {
			favbtn.hidden = true;
			favbtn.removeAttribute("data-fav");
		}
		else {
			favbtn.hidden = false;
			favbtn.setAttribute("data-fav", curtool);
		}
		paintstars();
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
			var id = cards[i].getAttribute("data-tool") || "";
			var name = (cards[i].getAttribute("data-name") || "").toLowerCase();
			var cat = cards[i].getAttribute("data-cat") || "";
			var starred = isfav(id);
			var hit = !q || name.indexOf(q) >= 0;
			var incat = curcat === "all" || cat === curcat || (curcat === "fav" && starred);
			var vis = q ? hit : incat;
			cards[i].hidden = !vis;
			cards[i].style.order = curcat === "fav" && !q && starred ? String(favindex(id)) : "";
			if (vis) n++;
		}
		$("tool-none").textContent = curcat === "fav" && !q ? "还没有收藏。点卡片右侧的星可以加进来。" : "没有匹配的工具";
		$("tool-none").hidden = n !== 0;
	}

	function loadfav(){
		favs = [];
		var raw = "";
		try { raw = localStorage.getItem("sk-tool-fav") || ""; }
		catch (e) { return; }
		var list;
		try { list = JSON.parse(raw || "[]"); }
		catch (e) { return; }
		if (!list || typeof list.length !== "number") return;
		var i, seen = {};
		for (i = 0; i < list.length; i++) {
			var id = String(list[i] || "");
			if (!id || seen[id] || !cardof(id)) continue;
			seen[id] = 1;
			favs.push(id);
		}
	}

	function cardof(id){
		var cards = document.querySelectorAll("#cards button");
		var i;
		for (i = 0; i < cards.length; i++)
			if (cards[i].getAttribute("data-tool") === id) return cards[i];
		return null;
	}

	function favindex(id){
		var i;
		for (i = 0; i < favs.length; i++) if (favs[i] === id) return i;
		return -1;
	}

	function isfav(id){
		return favindex(id) >= 0;
	}

	function togglefav(id){
		if (!id || id === "home" || !cardof(id)) return;
		var next = [];
		var had = false;
		var i;
		for (i = 0; i < favs.length; i++) {
			if (favs[i] === id) had = true;
			else next.push(favs[i]);
		}
		if (!had) next.push(id);
		favs = next;
		try { localStorage.setItem("sk-tool-fav", JSON.stringify(favs)); }
		catch (e) {}
		paintstars();
		if (curtool === "home") paintcats();
	}

	function paintstars(){
		var nodes = document.querySelectorAll("#cards .star");
		var i;
		for (i = 0; i < nodes.length; i++) {
			var on = isfav(nodes[i].getAttribute("data-fav"));
			nodes[i].className = on ? "star on fa-solid fa-star" : "star fa-solid fa-star";
			nodes[i].title = on ? "取消收藏" : "收藏";
		}
		var btn = $("tool-fav");
		if (!btn) return;
		var picked = btn.getAttribute("data-fav") || "";
		var lit = picked && isfav(picked);
		btn.className = lit ? "back on" : "back";
		var label = btn.querySelector("span");
		if (label) label.textContent = lit ? "已收藏" : "收藏";
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

	function onocrfile(){
		var file = $("ocr-file").files && $("ocr-file").files[0];
		if (file) setocrpic(file);
	}

	function onocrpaste(ev){
		if (curtool !== "ocr") return;
		var cd = ev.clipboardData;
		if (!cd) return;
		var file = null;
		var i;
		if (cd.items) {
			for (i = 0; i < cd.items.length; i++) {
				var item = cd.items[i];
				if (item.kind === "file" && item.type.indexOf("image/") === 0) {
					file = item.getAsFile();
					break;
				}
			}
		}
		if (!file && cd.files && cd.files.length && cd.files[0].type.indexOf("image/") === 0)
			file = cd.files[0];
		if (!file) return;
		ev.preventDefault();
		setocrpic(file);
	}

	function setocrpic(file){
		ocrPic = file;
		if (ocrPicUrl) URL.revokeObjectURL(ocrPicUrl);
		ocrPicUrl = URL.createObjectURL(file);
		var img = $("ocr-img");
		img.src = ocrPicUrl;
		img.hidden = false;
		msg("ocr-msg", "已放入图片");
	}

	function ocrgo(){
		var file = ocrPic || ($("ocr-file").files && $("ocr-file").files[0]);
		if (!file) {
			msg("ocr-msg", "请选择或粘贴图片", true);
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

	function openutil(name){
		stoptick();
		var meta = utilmeta(name);
		util = name;
		$("box-title").textContent = meta[0];
		$("box-go").textContent = meta[1];
		$("box-body").innerHTML = utilbody(name);
		$("box-out").value = "";
		msg("box-msg", "");
		if (name === "u-ts") $("box-in").value = String(Math.floor(Date.now() / 1000));
		if (name === "u-datediff" || name === "u-work") {
			var day = new Date();
			$("box-b").value = ymd(day);
			day.setDate(day.getDate() - 7);
			$("box-a").value = ymd(day);
		}
		if (name === "u-ascii") {
			try { $("box-out").value = asciitable(); }
			catch (ex) { msg("box-msg", ex && ex.message ? ex.message : "失败", true); }
		}
		if (name === "u-watch") {
			$("box-start").onclick = watchtoggle;
			$("box-out").value = watchreset();
		}
		if (name === "u-dadd" || name === "u-madd") {
			$("box-a").value = ymd(new Date());
		}
	}

	function utilrun(){
		msg("box-msg", "");
		if (util === "u-sha") {
			var text = val("box-in");
			if (text.length > 200000) {
				msg("box-msg", "文字太长", true);
				return;
			}
			if (!window.crypto || !crypto.subtle || typeof crypto.subtle.digest !== "function") {
				msg("box-msg", "浏览器没有 SHA-256", true);
				return;
			}
			msg("box-msg", "计算中");
			crypto.subtle.digest("SHA-256", utf8bytes(text)).then(function(buf){
				$("box-out").value = hexbytes(new Uint8Array(buf));
				msg("box-msg", "完成");
			}, function(){
				msg("box-msg", "SHA-256 失败", true);
			});
			return;
		}
		if (util === "u-b64img") {
			imgb64();
			return;
		}
		if (util === "u-down") {
			try {
				countdowngo();
				msg("box-msg", "计时中");
			}
			catch (ex) {
				msg("box-msg", ex && ex.message ? ex.message : "失败", true);
			}
			return;
		}
		try {
			$("box-out").value = dispatch();
			msg("box-msg", "完成");
		}
		catch (ex) {
			$("box-out").value = "";
			msg("box-msg", ex && ex.message ? ex.message : "失败", true);
		}
	}

	function utilmeta(name){
		var map = {
			"u-ts": ["时间戳", "转换"],
			"u-datediff": ["日期差", "计算"],
			"u-radix": ["进制转换", "转换"],
			"u-len": ["长度换算", "换算"],
			"u-byte": ["存储换算", "换算"],
			"u-rmb": ["人民币大写", "转换"],
			"u-calc": ["计算器", "计算"],
			"u-bmi": ["BMI", "计算"],
			"u-px": ["Px / Rem", "换算"],
			"u-rand": ["随机数", "生成"],
			"u-pw": ["随机密码", "生成"],
			"u-uuid": ["UUID", "生成"],
			"u-html": ["HTML 编码", "转换"],
			"u-jsonmin": ["JSON 压缩", "压缩"],
			"u-md5": ["MD5", "计算"],
			"u-sha": ["SHA-256", "计算"],
			"u-re": ["正则测试", "测试"],
			"u-diff": ["文本比对", "比对"],
			"u-name": ["变量名", "转换"],
			"u-ascii": ["ASCII 表", "显示"],
			"u-temp": ["温度换算", "换算"],
			"u-mass": ["重量换算", "换算"],
			"u-area": ["面积换算", "换算"],
			"u-vol": ["体积换算", "换算"],
			"u-press": ["压力换算", "换算"],
			"u-power": ["功率换算", "换算"],
			"u-tunit": ["时间单位", "换算"],
			"u-color": ["颜色转换", "转换"],
			"u-rcolor": ["随机颜色", "生成"],
			"u-jesc": ["JSON 转义", "转换"],
			"u-repl": ["查找替换", "替换"],
			"u-ua": ["User-Agent", "解析"],
			"u-cron": ["Crontab", "说明"],
			"u-work": ["工作日", "计算"],
			"u-loan": ["房贷", "计算"],
			"u-comp": ["复利", "计算"],
			"u-mac": ["MAC 地址", "生成"],
			"u-hmin": ["HTML 压缩", "压缩"],
			"u-cmin": ["CSS 压缩", "压缩"],
			"u-grad": ["CSS 渐变", "生成"],
			"u-xml": ["XML", "格式化"],
			"u-table": ["HTML 表格", "生成"],
			"u-rstr": ["随机字符串", "生成"],
			"u-ph": ["占位图", "生成"],
			"u-dadd": ["日期加减", "计算"],
			"u-madd": ["月份加减", "计算"],
			"u-jdiff": ["JSON 差异", "比对"],
			"u-pick": ["取色", "转换"],
			"u-watch": ["秒表", "复位"],
			"u-down": ["倒计时", "开始"],
			"u-sci": ["科学计算", "计算"],
			"u-rename": ["批量改名", "生成"],
			"u-jsmin": ["JS 压缩", "压缩"],
			"u-sql": ["SQL", "格式化"],
			"u-tok": ["Token 估算", "计算"],
			"u-hjs": ["HTML 转 JS", "转换"],
			"u-b64img": ["图片 Base64", "转换"],
			"u-btn": ["CSS 按钮", "生成"],
			"u-fake": ["测试数据", "生成"],
			"u-pal": ["调色板", "生成"],
		};
		return map[name] || ["工具", "计算"];
	}

	function utilbody(name){
		if (name === "u-ts")
			return lab("时间戳或日期", ta("box-in", 4, "留空为现在。秒、毫秒，或 2026-10-09 12:00:00"));
		if (name === "u-datediff")
			return row2(lab("开始", '<input id="box-a" type="date">'), lab("结束", '<input id="box-b" type="date">'));
		if (name === "u-radix")
			return lab("整数", textin("box-in", "", "例如 FF")) + row2(lab("从", numin("box-from", "16")), lab("到", numin("box-to", "10")));
		if (name === "u-len")
			return row2(lab("数值", textin("box-in", "1")), lab("单位", '<select id="box-unit">' + opts(LENU, "m") + "</select>"));
		if (name === "u-byte")
			return row2(lab("数值", textin("box-in", "1")), lab("单位", '<select id="box-unit">' + opts(BYTEU, "MB") + "</select>"));
		if (name === "u-rmb")
			return lab("金额", textin("box-in", "", "例如 10010.50"));
		if (name === "u-calc")
			return lab("算式", ta("box-in", 4, "支持 + - * / % ^ 和括号，% 是取余"));
		if (name === "u-bmi")
			return row2(lab("身高（厘米）", textin("box-h", "")), lab("体重（千克）", textin("box-w", "")));
		if (name === "u-px")
			return row2(lab("数值", textin("box-in", "16")), lab("根字号", textin("box-root", "16")))
				+ lab("方向", '<select id="box-mode"><option value="px">px 转 rem</option><option value="rem">rem 转 px</option></select>');
		if (name === "u-rand")
			return row2(lab("最小", textin("box-min", "1")), lab("最大", textin("box-max", "100")))
				+ row2(lab("个数", numin("box-n", "5")), lab("类型", '<select id="box-kind"><option value="int">整数</option><option value="float">小数</option></select>'));
		if (name === "u-pw")
			return lab("长度", numin("box-len", "16"))
				+ check("box-low", "小写", true) + check("box-up", "大写", true)
				+ check("box-dig", "数字", true) + check("box-sym", "符号", true)
				+ check("box-amb", "去掉易混字符（0、O、o、1、l、I）", false);
		if (name === "u-uuid")
			return lab("个数", numin("box-n", "1"));
		if (name === "u-html")
			return lab("文字", ta("box-in", 8, ""))
				+ lab("方向", '<select id="box-mode"><option value="enc">编码</option><option value="dec">解码</option></select>');
		if (name === "u-jsonmin")
			return lab("JSON", ta("box-in", 10, ""));
		if (name === "u-md5" || name === "u-sha")
			return lab("文字", ta("box-in", 8, ""));
		if (name === "u-re")
			return row2(lab("表达式", textin("box-pat", "", "例如 \\d+")), lab("标志", textin("box-flags", "g", "g i m")))
				+ lab("文本", ta("box-in", 8, ""));
		if (name === "u-diff")
			return '<div class="split">' + lab("原文", ta("box-a", 10, "")) + lab("新文", ta("box-b", 10, "")) + "</div>";
		if (name === "u-name")
			return lab("名称", textin("box-in", "", "foo_bar 或 fooBar"));
		if (name === "u-ascii")
			return '<p class="kicker">0 到 127</p>';
		if (name === "u-temp")
			return row2(lab("数值", textin("box-in", "0")), lab("单位", '<select id="box-unit">' + opts(TEMPU, "C") + "</select>"));
		if (name === "u-mass")
			return row2(lab("数值", textin("box-in", "1")), lab("单位", '<select id="box-unit">' + opts(MASSU, "kg") + "</select>"));
		if (name === "u-area")
			return row2(lab("数值", textin("box-in", "1")), lab("单位", '<select id="box-unit">' + opts(AREAU, "m2") + "</select>"));
		if (name === "u-vol")
			return row2(lab("数值", textin("box-in", "1")), lab("单位", '<select id="box-unit">' + opts(VOLU, "L") + "</select>"));
		if (name === "u-press")
			return row2(lab("数值", textin("box-in", "1")), lab("单位", '<select id="box-unit">' + opts(PRESSU, "kPa") + "</select>"));
		if (name === "u-power")
			return row2(lab("数值", textin("box-in", "1")), lab("单位", '<select id="box-unit">' + opts(POWERU, "kW") + "</select>"));
		if (name === "u-tunit")
			return row2(lab("数值", textin("box-in", "1")), lab("单位", '<select id="box-unit">' + opts(TIMEU, "h") + "</select>"));
		if (name === "u-color")
			return lab("颜色", textin("box-in", "#3366cc", "HEX、RGB、HSL、HSV 或 CMYK"))
				+ lab("格式", '<select id="box-mode"><option value="hex">HEX</option><option value="rgb">RGB</option><option value="hsl">HSL</option><option value="hsv">HSV</option><option value="cmyk">CMYK</option></select>');
		if (name === "u-rcolor")
			return lab("个数", numin("box-n", "5"));
		if (name === "u-jesc")
			return lab("文字", ta("box-in", 8, ""))
				+ lab("方向", '<select id="box-mode"><option value="enc">转义</option><option value="dec">去除转义</option></select>');
		if (name === "u-repl")
			return lab("文本", ta("box-in", 8, ""))
				+ row2(lab("查找", textin("box-find", "")), lab("替换为", textin("box-rep", "")))
				+ check("box-re", "按正则", false);
		if (name === "u-ua")
			return lab("User-Agent", ta("box-in", 6, ""));
		if (name === "u-cron")
			return lab("表达式", textin("box-in", "0 9 * * 1-5", "分 时 日 月 周"));
		if (name === "u-work")
			return row2(lab("开始", '<input id="box-a" type="date">'), lab("结束", '<input id="box-b" type="date">'));
		if (name === "u-loan")
			return row2(lab("金额", textin("box-p", "100000")), lab("年利率 %", textin("box-rate", "3.5")))
				+ row2(lab("月数", numin("box-n", "360")), lab("方式", '<select id="box-mode"><option value="annuity">等额本息</option><option value="principal">等额本金</option></select>'));
		if (name === "u-comp")
			return row2(lab("本金", textin("box-p", "10000")), lab("年利率 %", textin("box-rate", "3")))
				+ row2(lab("年数", textin("box-years", "5")), lab("每年计息次数", numin("box-n", "12")));
		if (name === "u-mac")
			return lab("个数", numin("box-n", "1"));
		if (name === "u-hmin")
			return lab("HTML", ta("box-in", 10, "去掉注释和标签之间的空白。pre、script、style 里面保留"));
		if (name === "u-cmin")
			return lab("CSS", ta("box-in", 10, ""));
		if (name === "u-grad")
			return row2(lab("起始色", textin("box-a", "#f97316")), lab("结束色", textin("box-b", "#111827")))
				+ lab("角度", numin("box-n", "90"));
		if (name === "u-xml")
			return lab("XML", ta("box-in", 10, ""))
				+ lab("方向", '<select id="box-mode"><option value="pretty">格式化</option><option value="min">压缩</option></select>');
		if (name === "u-table")
			return lab("表格文字", ta("box-in", 8, "第一行是表头。用逗号或制表符分列"));
		if (name === "u-rstr")
			return row2(lab("长度", numin("box-len", "16")), lab("个数", numin("box-n", "5")))
				+ lab("字符集", textin("box-set", "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"));
		if (name === "u-ph")
			return row2(lab("宽", numin("box-w", "320")), lab("高", numin("box-h", "180")))
				+ row2(lab("底色", textin("box-bg", "#eef1f4")), lab("字色", textin("box-fg", "#333333")))
				+ lab("文字", textin("box-in", "", "留空则写宽高"))
				+ '<img id="box-img" alt="" hidden>';
		if (name === "u-dadd")
			return row2(lab("日期", '<input id="box-a" type="date">'), lab("加减天数", textin("box-n", "7")));
		if (name === "u-madd")
			return row2(lab("日期", '<input id="box-a" type="date">'), lab("加减月数", textin("box-n", "1")));
		if (name === "u-jdiff")
			return '<div class="split">' + lab("JSON 甲", ta("box-a", 10, "")) + lab("JSON 乙", ta("box-b", 10, "")) + "</div>";
		if (name === "u-pick")
			return lab("颜色", '<input id="box-color" type="color" value="#3366cc">');
		if (name === "u-watch")
			return '<div class="row"><button type="button" id="box-start">开始</button></div>';
		if (name === "u-down")
			return lab("秒数", numin("box-n", "60"));
		if (name === "u-sci")
			return lab("算式", ta("box-in", 4, "支持三角函数（角度）、sqrt、log、ln、abs 和 pi"));
		if (name === "u-rename")
			return lab("原文件名", ta("box-in", 8, "一行一个"))
				+ row2(lab("前缀", textin("box-pre", "")), lab("后缀", textin("box-suf", "-")))
				+ lab("起始序号", numin("box-n", "1"));
		if (name === "u-jsmin")
			return lab("JavaScript", ta("box-in", 10, "去掉注释和行尾空白，不改名"));
		if (name === "u-sql")
			return lab("SQL", ta("box-in", 8, ""))
				+ lab("方向", '<select id="box-mode"><option value="pretty">格式化</option><option value="min">压成一行</option></select>');
		if (name === "u-tok")
			return lab("文字", ta("box-in", 8, "汉字、英文单词和符号粗算，不是模型分词"));
		if (name === "u-hjs")
			return lab("HTML", ta("box-in", 8, ""));
		if (name === "u-b64img")
			return lab("图片", '<input id="box-file" type="file" accept="image/*">');
		if (name === "u-btn")
			return lab("文字", textin("box-in", "按钮"))
				+ row2(lab("底色", textin("box-bg", "#f97316")), lab("字色", textin("box-fg", "#ffffff")))
				+ lab("圆角", numin("box-n", "8"));
		if (name === "u-fake")
			return lab("行数", numin("box-n", "5"));
		if (name === "u-pal")
			return lab("基准色", textin("box-in", "#f97316", "#RRGGBB"));
		return "";
	}

	function dispatch(){
		if (util === "u-ts") return tsconv(val("box-in"));
		if (util === "u-datediff") return dateconv(val("box-a"), val("box-b"));
		if (util === "u-radix") return radixconv(val("box-in"), val("box-from"), val("box-to"));
		if (util === "u-len") return unitconv(val("box-in"), val("box-unit"), LENU, LENS);
		if (util === "u-byte") return unitconv(val("box-in"), val("box-unit"), BYTEU, BYTES);
		if (util === "u-rmb") return rmbupper(val("box-in"));
		if (util === "u-calc") return calcexpr(val("box-in"));
		if (util === "u-bmi") return bmicalc(val("box-h"), val("box-w"));
		if (util === "u-px") return pxconv(val("box-in"), val("box-root"), val("box-mode"));
		if (util === "u-rand") return randtext(val("box-min"), val("box-max"), val("box-n"), val("box-kind") === "float");
		if (util === "u-pw") return pwtext();
		if (util === "u-uuid") return uuidtext(val("box-n"));
		if (util === "u-html") return val("box-mode") === "dec" ? htmldec(val("box-in")) : htmlenc(val("box-in"));
		if (util === "u-jsonmin") return jsonmin(val("box-in"));
		if (util === "u-md5") return md5hex(val("box-in"));
		if (util === "u-re") return retest(val("box-pat"), val("box-flags"), val("box-in"));
		if (util === "u-diff") return linediff(val("box-a"), val("box-b"));
		if (util === "u-name") return nameconv(val("box-in"));
		if (util === "u-ascii") return asciitable();
		if (util === "u-temp") return tempconv(val("box-in"), val("box-unit"));
		if (util === "u-mass") return unitconv(val("box-in"), val("box-unit"), MASSU, MASSS);
		if (util === "u-area") return unitconv(val("box-in"), val("box-unit"), AREAU, AREAS);
		if (util === "u-vol") return unitconv(val("box-in"), val("box-unit"), VOLU, VOLS);
		if (util === "u-press") return unitconv(val("box-in"), val("box-unit"), PRESSU, PRESSS);
		if (util === "u-power") return unitconv(val("box-in"), val("box-unit"), POWERU, POWERS);
		if (util === "u-tunit") return unitconv(val("box-in"), val("box-unit"), TIMEU, TIMES);
		if (util === "u-color") return colorconv(val("box-mode"), val("box-in"));
		if (util === "u-rcolor") return rcolor(val("box-n"));
		if (util === "u-jesc") return jsonesc(val("box-in"), val("box-mode"));
		if (util === "u-repl") return repltext(val("box-in"), val("box-find"), val("box-rep"), onbox("box-re"));
		if (util === "u-ua") return uaparse(val("box-in"));
		if (util === "u-cron") return crondesc(val("box-in"));
		if (util === "u-work") return workdays(val("box-a"), val("box-b"));
		if (util === "u-loan") return loanpay(val("box-p"), val("box-rate"), val("box-n"), val("box-mode"));
		if (util === "u-comp") return compound(val("box-p"), val("box-rate"), val("box-years"), val("box-n"));
		if (util === "u-mac") return macgen(val("box-n"));
		if (util === "u-hmin") return htmlmin(val("box-in"));
		if (util === "u-cmin") return cssmin(val("box-in"));
		if (util === "u-grad") return gradcss(val("box-a"), val("box-b"), val("box-n"));
		if (util === "u-xml") return xmlfmt(val("box-in"), val("box-mode"));
		if (util === "u-table") return htmltable(val("box-in"));
		if (util === "u-rstr") return randstr(val("box-len"), val("box-n"), val("box-set"));
		if (util === "u-ph") return placeholder(val("box-w"), val("box-h"), val("box-in"), val("box-bg"), val("box-fg"));
		if (util === "u-dadd") return dateadd(val("box-a"), val("box-n"));
		if (util === "u-madd") return monthadd(val("box-a"), val("box-n"));
		if (util === "u-jdiff") return jsondiff(val("box-a"), val("box-b"));
		if (util === "u-pick") return colorconv("hex", val("box-color"));
		if (util === "u-watch") return watchreset();
		if (util === "u-sci") return sciexpr(val("box-in"));
		if (util === "u-rename") return renamebatch(val("box-in"), val("box-pre"), val("box-suf"), val("box-n"));
		if (util === "u-jsmin") return jsmin(val("box-in"));
		if (util === "u-sql") return sqlfmt(val("box-in"), val("box-mode"));
		if (util === "u-tok") return tokencount(val("box-in"));
		if (util === "u-hjs") return html2js(val("box-in"));
		if (util === "u-btn") return cssbtn(val("box-in"), val("box-bg"), val("box-fg"), val("box-n"));
		if (util === "u-fake") return fakedata(val("box-n"));
		if (util === "u-pal") return palette(val("box-in"));
		throw new Error("没有这个工具");
	}

	function lab(title, inner){
		return "<label>" + title + inner + "</label>";
	}

	function row2(a, b){
		return '<div class="row">' + a + b + "</div>";
	}

	function ta(id, rows, ph){
		return '<textarea id="' + id + '" rows="' + rows + '" placeholder="' + ph + '"></textarea>';
	}

	function textin(id, value, ph){
		return '<input id="' + id + '" type="text" value="' + value + '" placeholder="' + (ph || "") + '">';
	}

	function numin(id, value){
		return '<input id="' + id + '" type="number" value="' + value + '">';
	}

	function check(id, title, on){
		return '<label class="check"><input id="' + id + '" type="checkbox"' + (on ? " checked" : "") + "> " + title + "</label>";
	}

	function opts(list, sel){
		var html = "";
		var i;
		for (i = 0; i < list.length; i++) {
			html += '<option value="' + list[i][0] + '"' + (list[i][0] === sel ? " selected" : "") + ">" + list[i][1] + "</option>";
		}
		return html;
	}

	function val(id){
		var el = $(id);
		return el && el.value != null ? String(el.value) : "";
	}

	function onbox(id){
		var el = $(id);
		return !!(el && el.checked);
	}

	function ymd(d){
		return d.getFullYear() + "-" + pad2(d.getMonth() + 1) + "-" + pad2(d.getDate());
	}

	function pad2(n){
		return (n < 10 ? "0" : "") + n;
	}

	function fmtdt(d){
		return d.getFullYear() + "-" + pad2(d.getMonth() + 1) + "-" + pad2(d.getDate())
			+ " " + pad2(d.getHours()) + ":" + pad2(d.getMinutes()) + ":" + pad2(d.getSeconds());
	}

	function trimnum(n){
		if (n === 0) return "0";
		if (!isFinite(n)) throw new Error("结果无效");
		var neg = n < 0;
		var a = Math.abs(n);
		var s;
		if (a >= 1e15 || a < 1e-8) s = a.toExponential(6);
		else {
			s = a.toFixed(10);
			s = s.replace(/\.?0+$/, "");
		}
		return (neg ? "-" : "") + s;
	}

	function utf8bytes(s){
		s = String(s);
		if (s.length > 200000) throw new Error("文字太长");
		if (typeof TextEncoder === "function") return new TextEncoder().encode(s);
		var bin = unescape(encodeURIComponent(s));
		var a = new Uint8Array(bin.length);
		var i;
		for (i = 0; i < bin.length; i++) a[i] = bin.charCodeAt(i) & 255;
		return a;
	}

	function hexbytes(arr){
		var s = "";
		var i;
		for (i = 0; i < arr.length; i++) {
			var v = arr[i];
			s += (v < 16 ? "0" : "") + v.toString(16);
		}
		return s;
	}

	function randint(n){
		if (!(n >= 1)) throw new Error("范围无效");
		if (n > 0x100000000) throw new Error("范围太大");
		if (!window.crypto || typeof crypto.getRandomValues !== "function") throw new Error("浏览器不能生成随机数");
		var buf = new Uint32Array(1);
		var limit = Math.floor(0x100000000 / n) * n;
		var x;
		do {
			crypto.getRandomValues(buf);
			x = buf[0];
		} while (x >= limit);
		return x % n;
	}

	function tsconv(raw){
		raw = String(raw || "").trim();
		var d;
		if (!raw) d = new Date();
		else if (/^-?\d+(\.\d+)?$/.test(raw)) {
			var n = Number(raw);
			if (!isFinite(n)) throw new Error("无法解析");
			var ms = Math.abs(n) > 1e12 ? n : n * 1000;
			d = new Date(ms);
		}
		else d = parsedate(raw);
		if (isNaN(d.getTime())) throw new Error("无法解析");
		return "本地 " + fmtdt(d) + "\n秒 " + Math.floor(d.getTime() / 1000) + "\n毫秒 " + d.getTime();
	}

	function parsedate(s){
		var m = /^(\d{4})-(\d{2})-(\d{2})(?:[ T](\d{2}):(\d{2})(?::(\d{2}))?)?$/.exec(String(s).trim());
		if (!m) throw new Error("日期格式用 YYYY-MM-DD HH:mm:ss");
		var d = new Date(+m[1], +m[2] - 1, +m[3], +(m[4] || 0), +(m[5] || 0), +(m[6] || 0));
		if (d.getFullYear() !== +m[1] || d.getMonth() !== +m[2] - 1 || d.getDate() !== +m[3]) throw new Error("日期无效");
		return d;
	}

	function dateconv(a, b){
		if (!a || !b) throw new Error("请选择两个日期");
		var da = new Date(a + "T00:00:00");
		var db = new Date(b + "T00:00:00");
		if (isNaN(da.getTime()) || isNaN(db.getTime())) throw new Error("日期无效");
		var days = Math.round((db.getTime() - da.getTime()) / 86400000);
		return "从 " + a + " 到 " + b + " 相差 " + days + " 天";
	}

	function unitconv(text, unit, table, scale){
		var raw = String(text).trim();
		var n = Number(raw);
		if (raw === "" || !isFinite(n)) throw new Error("请输入数字");
		var base = scale[unit];
		if (!base) throw new Error("请选择单位");
		var v = n * base;
		var lines = [];
		var i;
		for (i = 0; i < table.length; i++) lines.push(table[i][1] + "  " + trimnum(v / scale[table[i][0]]));
		return lines.join("\n");
	}

	function radixconv(text, from, to){
		var a = parseInt(from, 10);
		var b = parseInt(to, 10);
		if (!(a >= 2 && a <= 36) || !(b >= 2 && b <= 36)) throw new Error("进制要在 2 到 36");
		var t = String(text).trim();
		if (!t) throw new Error("请输入整数");
		if (t.length > 40) throw new Error("太长");
		var neg = false;
		if (t.charAt(0) === "-") {
			neg = true;
			t = t.slice(1);
		}
		if (!t) throw new Error("请输入整数");
		var alpha = "0123456789abcdefghijklmnopqrstuvwxyz";
		var n = 0;
		var i;
		for (i = 0; i < t.length; i++) {
			var d = alpha.indexOf(t.charAt(i).toLowerCase());
			if (d < 0 || d >= a) throw new Error("有数字超出进制");
			n = n * a + d;
			if (n > 9007199254740991) throw new Error("超出安全整数");
		}
		return (neg ? "-" : "") + n.toString(b).toUpperCase();
	}

	function rmbupper(text){
		var s = String(text).trim();
		if (!/^\d+(\.\d+)?$/.test(s)) throw new Error("请输入非负金额");
		var parts = s.split(".");
		if (parts[0].length > 12) throw new Error("金额太大");
		var yuan = parseInt(parts[0], 10);
		var frac = (parts[1] || "") + "000";
		var fen = parseInt(frac.slice(0, 2), 10);
		if (parseInt(frac.charAt(2), 10) >= 5) fen++;
		var all = yuan * 100 + fen;
		if (!isFinite(all) || all < 0 || Math.floor(all / 100) > 999999999999) throw new Error("金额太大");
		if (all === 0) return "零元整";
		var digit = "零壹贰叁肆伍陆柒捌玖";
		var small = ["", "拾", "佰", "仟"];
		var big = ["", "万", "亿"];
		var y = Math.floor(all / 100);
		var jiao = Math.floor(all / 10) % 10;
		var f = all % 10;
		var chunks = [];
		while (y > 0) {
			chunks.push(y % 10000);
			y = Math.floor(y / 10000);
		}
		var body = "";
		var gap = false;
		var gi;
		for (gi = chunks.length - 1; gi >= 0; gi--) {
			var sec = chunks[gi];
			if (!sec) {
				gap = true;
				continue;
			}
			var piece = "";
			var zero = false;
			var left = sec;
			var i;
			for (i = 0; i < 4; i++) {
				var d = left % 10;
				if (d === 0) {
					if (piece) zero = true;
				}
				else {
					piece = digit.charAt(d) + small[i] + (zero ? "零" : "") + piece;
					zero = false;
				}
				left = Math.floor(left / 10);
			}
			if (body && (gap || sec < 1000)) body += "零";
			body += piece + big[gi];
			gap = false;
		}
		var tail;
		if (jiao === 0 && f === 0) tail = "整";
		else {
			tail = "";
			if (jiao) tail += digit.charAt(jiao) + "角";
			else if (f && yuan) tail += "零";
			if (f) tail += digit.charAt(f) + "分";
		}
		if (!body) return tail;
		return body + "元" + tail;
	}

	function calcexpr(src){
		var s = String(src == null ? "" : src).replace(/\s+/g, "");
		if (!s) throw new Error("请输入算式");
		if (s.length > 200) throw new Error("算式太长");
		var i = 0;
		var v = expr();
		if (i !== s.length) throw new Error("算式里有无法识别的部分");
		return trimnum(v);
		function expr(){
			var left = term();
			while (peek() === "+" || peek() === "-") {
				var op = peek();
				i++;
				var right = term();
				left = op === "+" ? left + right : left - right;
			}
			return left;
		}
		function term(){
			var left = pow();
			while (peek() === "*" || peek() === "/" || peek() === "%") {
				var op = peek();
				i++;
				var right = pow();
				if (op === "*") left = left * right;
				else if (right === 0) throw new Error("除数不能为 0");
				else left = op === "/" ? left / right : left % right;
			}
			return left;
		}
		function pow(){
			var left = unary();
			if (peek() === "^") {
				i++;
				var right = pow();
				var v = Math.pow(left, right);
				if (!isFinite(v)) throw new Error("乘方结果无效");
				return v;
			}
			return left;
		}
		function unary(){
			if (peek() === "-") {
				i++;
				return -unary();
			}
			if (peek() === "+") {
				i++;
				return unary();
			}
			return primary();
		}
		function primary(){
			if (peek() === "(") {
				i++;
				var v = expr();
				if (peek() !== ")") throw new Error("括号没有配对");
				i++;
				return v;
			}
			return number();
		}
		function number(){
			var start = i;
			while (i < s.length && s.charAt(i) >= "0" && s.charAt(i) <= "9") i++;
			if (peek() === "." && i + 1 < s.length && s.charAt(i + 1) >= "0" && s.charAt(i + 1) <= "9") {
				i++;
				while (i < s.length && s.charAt(i) >= "0" && s.charAt(i) <= "9") i++;
			}
			if (start === i) throw new Error("这里需要数字");
			return Number(s.slice(start, i));
		}
		function peek(){
			return s.charAt(i);
		}
	}

	function bmicalc(cm, kg){
		var h = Number(String(cm).trim());
		var w = Number(String(kg).trim());
		if (!(h > 0) || !(w > 0)) throw new Error("请输入身高和体重");
		if (h > 300 || w > 500) throw new Error("数值超出常见范围");
		var bmi = w / ((h / 100) * (h / 100));
		var band = "肥胖";
		if (bmi < 18.5) band = "偏瘦";
		else if (bmi < 24) band = "正常";
		else if (bmi < 28) band = "超重";
		return "BMI " + trimnum(bmi) + "\n" + band;
	}

	function pxconv(px, root, mode){
		var a = Number(String(px).trim());
		var r = Number(String(root).trim());
		if (!(r > 0)) throw new Error("根字号要大于 0");
		if (!isFinite(a)) throw new Error("请输入数字");
		if (mode === "rem") return trimnum(a * r) + " px";
		return trimnum(a / r) + " rem";
	}

	function randtext(min, max, count, asFloat){
		var a = Number(String(min).trim());
		var b = Number(String(max).trim());
		var c = parseInt(count, 10);
		if (!isFinite(a) || !isFinite(b)) throw new Error("请输入范围");
		if (a > b) throw new Error("最小值不能大于最大值");
		if (!(c >= 1 && c <= 100)) throw new Error("个数要在 1 到 100");
		var lines = [];
		var i;
		if (asFloat) {
			for (i = 0; i < c; i++) lines.push(trimnum(a + (b - a) * (randint(0x1000000) / 0x1000000)));
			return lines.join("\n");
		}
		if (Math.floor(a) !== a || Math.floor(b) !== b) throw new Error("整数范围请填整数");
		if (Math.abs(a) > 9007199254740991 || Math.abs(b) > 9007199254740991) throw new Error("超出安全整数");
		var span = b - a + 1;
		for (i = 0; i < c; i++) lines.push(String(a + randint(span)));
		return lines.join("\n");
	}

	function pwtext(){
		var len = parseInt(val("box-len"), 10);
		if (!(len >= 4 && len <= 64)) throw new Error("长度要在 4 到 64");
		var drop = onbox("box-amb");
		function clean(s){
			return drop ? s.replace(/[0Oo1lI]/g, "") : s;
		}
		var groups = [];
		if (onbox("box-low")) groups.push(clean("abcdefghijklmnopqrstuvwxyz"));
		if (onbox("box-up")) groups.push(clean("ABCDEFGHIJKLMNOPQRSTUVWXYZ"));
		if (onbox("box-dig")) groups.push(clean("0123456789"));
		if (onbox("box-sym")) groups.push("!@#$%^&*-_=+?");
		var pool = "";
		var g;
		for (g = 0; g < groups.length; g++) {
			if (!groups[g]) throw new Error("去掉易混字符后某一类是空的");
			pool += groups[g];
		}
		if (!pool) throw new Error("请至少选一类字符");
		var chars = [];
		for (g = 0; g < groups.length && chars.length < len; g++) chars.push(groups[g].charAt(randint(groups[g].length)));
		while (chars.length < len) chars.push(pool.charAt(randint(pool.length)));
		for (var i = chars.length - 1; i > 0; i--) {
			var j = randint(i + 1);
			var tmp = chars[i];
			chars[i] = chars[j];
			chars[j] = tmp;
		}
		return chars.join("");
	}

	function uuidtext(count){
		var c = parseInt(count, 10);
		if (!(c >= 1 && c <= 20)) throw new Error("个数要在 1 到 20");
		var lines = [];
		var n;
		for (n = 0; n < c; n++) lines.push(uuid1());
		return lines.join("\n");
	}

	function uuid1(){
		var b = new Uint8Array(16);
		if (!window.crypto || typeof crypto.getRandomValues !== "function") throw new Error("浏览器不能生成随机数");
		crypto.getRandomValues(b);
		b[6] = (b[6] & 15) | 64;
		b[8] = (b[8] & 63) | 128;
		var h = hexbytes(b);
		return h.slice(0, 8) + "-" + h.slice(8, 12) + "-" + h.slice(12, 16) + "-" + h.slice(16, 20) + "-" + h.slice(20);
	}

	function htmlenc(text){
		text = String(text);
		if (text.length > 200000) throw new Error("文字太长");
		return text.replace(/[&<>"']/g, function(ch){
			if (ch === "&") return "&amp;";
			if (ch === "<") return "&lt;";
			if (ch === ">") return "&gt;";
			if (ch === '"') return "&quot;";
			return "&#39;";
		});
	}

	function htmldec(text){
		text = String(text);
		if (text.length > 200000) throw new Error("文字太长");
		var named = {
			amp: "&", lt: "<", gt: ">", quot: '"', apos: "'", nbsp: "\u00a0",
			copy: "\u00a9", reg: "\u00ae", trade: "\u2122", mdash: "\u2014",
			ndash: "\u2013", hellip: "\u2026", middot: "\u00b7", times: "\u00d7", divide: "\u00f7",
		};
		return text.replace(/&(#x[0-9a-f]+|#\d+|[a-z][a-z0-9]*);/gi, function(all, body){
			if (body.charAt(0) !== "#") {
				var v = named[body.toLowerCase()];
				return v == null ? all : v;
			}
			var hex = body.charAt(1) === "x" || body.charAt(1) === "X";
			var n = parseInt(body.slice(hex ? 2 : 1), hex ? 16 : 10);
			if (!isFinite(n) || n < 0 || n > 0x10FFFF) return all;
			return String.fromCodePoint(n);
		});
	}

	function jsonmin(text){
		text = String(text);
		if (text.length > 200000) throw new Error("文字太长");
		try { return JSON.stringify(JSON.parse(text)); }
		catch (ex) { throw new Error("不是合法的 JSON"); }
	}

	function md5hex(text){
		var bytes = utf8bytes(text);
		var n = bytes.length;
		var bitLen = n * 8;
		var withPad = n + 1;
		while (withPad % 64 !== 56) withPad++;
		var buf = new Uint8Array(withPad + 8);
		buf.set(bytes);
		buf[n] = 0x80;
		var lo = bitLen >>> 0;
		var hi = Math.floor(bitLen / 0x100000000);
		buf[withPad] = lo & 255;
		buf[withPad + 1] = (lo >>> 8) & 255;
		buf[withPad + 2] = (lo >>> 16) & 255;
		buf[withPad + 3] = (lo >>> 24) & 255;
		buf[withPad + 4] = hi & 255;
		buf[withPad + 5] = (hi >>> 8) & 255;
		buf[withPad + 6] = (hi >>> 16) & 255;
		buf[withPad + 7] = (hi >>> 24) & 255;
		var k = [
			0xd76aa478, 0xe8c7b756, 0x242070db, 0xc1bdceee, 0xf57c0faf, 0x4787c62a, 0xa8304613, 0xfd469501,
			0x698098d8, 0x8b44f7af, 0xffff5bb1, 0x895cd7be, 0x6b901122, 0xfd987193, 0xa679438e, 0x49b40821,
			0xf61e2562, 0xc040b340, 0x265e5a51, 0xe9b6c7aa, 0xd62f105d, 0x02441453, 0xd8a1e681, 0xe7d3fbc8,
			0x21e1cde6, 0xc33707d6, 0xf4d50d87, 0x455a14ed, 0xa9e3e905, 0xfcefa3f8, 0x676f02d9, 0x8d2a4c8a,
			0xfffa3942, 0x8771f681, 0x6d9d6122, 0xfde5380c, 0xa4beea44, 0x4bdecfa9, 0xf6bb4b60, 0xbebfbc70,
			0x289b7ec6, 0xeaa127fa, 0xd4ef3085, 0x04881d05, 0xd9d4d039, 0xe6db99e5, 0x1fa27cf8, 0xc4ac5665,
			0xf4292244, 0x432aff97, 0xab9423a7, 0xfc93a039, 0x655b59c3, 0x8f0ccc92, 0xffeff47d, 0x85845dd1,
			0x6fa87e4f, 0xfe2ce6e0, 0xa3014314, 0x4e0811a1, 0xf7537e82, 0xbd3af235, 0x2ad7d2bb, 0xeb86d391,
		];
		var shift = [
			7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
			5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
			4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
			6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
		];
		var a0 = 0x67452301;
		var b0 = 0xefcdab89;
		var c0 = 0x98badcfe;
		var d0 = 0x10325476;
		var off;
		for (off = 0; off < buf.length; off += 64) {
			var m = new Uint32Array(16);
			var j;
			for (j = 0; j < 16; j++) {
				var p = off + j * 4;
				m[j] = (buf[p] | (buf[p + 1] << 8) | (buf[p + 2] << 16) | (buf[p + 3] << 24)) >>> 0;
			}
			var a = a0;
			var b = b0;
			var c = c0;
			var d = d0;
			var t;
			for (t = 0; t < 64; t++) {
				var f;
				var g;
				if (t < 16) {
					f = (b & c) | (~b & d);
					g = t;
				}
				else if (t < 32) {
					f = (d & b) | (~d & c);
					g = (5 * t + 1) % 16;
				}
				else if (t < 48) {
					f = b ^ c ^ d;
					g = (3 * t + 5) % 16;
				}
				else {
					f = c ^ (b | ~d);
					g = (7 * t) % 16;
				}
				f = (f + a + k[t] + m[g]) >>> 0;
				var rot = ((f << shift[t]) | (f >>> (32 - shift[t]))) >>> 0;
				a = d;
				d = c;
				c = b;
				b = (b + rot) >>> 0;
			}
			a0 = (a0 + a) >>> 0;
			b0 = (b0 + b) >>> 0;
			c0 = (c0 + c) >>> 0;
			d0 = (d0 + d) >>> 0;
		}
		return lehex(a0) + lehex(b0) + lehex(c0) + lehex(d0);
	}

	function lehex(n){
		var s = "";
		var i;
		for (i = 0; i < 4; i++) {
			var b = (n >>> (i * 8)) & 255;
			s += (b < 16 ? "0" : "") + b.toString(16);
		}
		return s;
	}

	function retest(pat, flags, text){
		pat = String(pat);
		flags = String(flags || "");
		text = String(text);
		if (!pat) throw new Error("请输入表达式");
		if (pat.length > 200) throw new Error("表达式太长");
		if (text.length > 20000) throw new Error("文本太长");
		if (!/^[gim]*$/.test(flags)) throw new Error("标志只支持 g、i、m");
		var seen = {};
		var i;
		for (i = 0; i < flags.length; i++) {
			if (seen[flags.charAt(i)]) throw new Error("标志重复了");
			seen[flags.charAt(i)] = 1;
		}
		var re;
		try { re = new RegExp(pat, flags); }
		catch (ex) { throw new Error("表达式无效"); }
		function one(m){
			var line = "[" + m.index + "] " + m[0];
			var g;
			for (g = 1; g < m.length; g++) line += "\n  " + g + ": " + (m[g] == null ? "" : m[g]);
			return line;
		}
		if (flags.indexOf("g") < 0) {
			var hit = re.exec(text);
			return hit ? one(hit) : "无匹配";
		}
		var lines = [];
		var guard = 0;
		var m2;
		while ((m2 = re.exec(text)) && lines.length < 100) {
			lines.push(one(m2));
			if (m2[0].length === 0) re.lastIndex++;
			guard++;
			if (guard > 10000) break;
		}
		if (!lines.length) return "无匹配";
		if (lines.length >= 100) lines.push("…只显示前 100 处");
		return lines.join("\n");
	}

	function linediff(a, b){
		var left = String(a).replace(/\r\n/g, "\n").replace(/\r/g, "\n").split("\n");
		var right = String(b).replace(/\r\n/g, "\n").replace(/\r/g, "\n").split("\n");
		if (left.length > 400 || right.length > 400) throw new Error("每边最多 400 行");
		var n = left.length;
		var m = right.length;
		var dp = new Array(n + 1);
		var i;
		var j;
		for (i = 0; i <= n; i++) dp[i] = new Uint16Array(m + 1);
		for (i = 1; i <= n; i++) {
			for (j = 1; j <= m; j++) {
				if (left[i - 1] === right[j - 1]) dp[i][j] = dp[i - 1][j - 1] + 1;
				else dp[i][j] = dp[i - 1][j] >= dp[i][j - 1] ? dp[i - 1][j] : dp[i][j - 1];
			}
		}
		var out = [];
		i = n;
		j = m;
		while (i > 0 || j > 0) {
			if (i > 0 && j > 0 && left[i - 1] === right[j - 1]) {
				out.push("  " + left[i - 1]);
				i--;
				j--;
			}
			else if (j > 0 && (i === 0 || dp[i][j - 1] >= dp[i - 1][j])) {
				out.push("+ " + right[j - 1]);
				j--;
			}
			else {
				out.push("- " + left[i - 1]);
				i--;
			}
		}
		out.reverse();
		return out.join("\n");
	}

	function nameconv(text){
		var raw = String(text).trim();
		if (!raw) throw new Error("请输入名称");
		if (raw.length > 200) throw new Error("太长");
		var bits = raw.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[^A-Za-z0-9]+/g, " ").split(/\s+/);
		var w = [];
		var i;
		for (i = 0; i < bits.length; i++) if (bits[i]) w.push(bits[i].toLowerCase());
		if (!w.length) throw new Error("没有可用的字母或数字");
		function cap(s){ return s.charAt(0).toUpperCase() + s.slice(1); }
		var camel = w[0];
		var pascal = "";
		for (i = 0; i < w.length; i++) {
			if (i > 0) camel += cap(w[i]);
			pascal += cap(w[i]);
		}
		return "小驼峰  " + camel
			+ "\n大驼峰  " + pascal
			+ "\n下划线  " + w.join("_")
			+ "\n大写下划线  " + w.join("_").toUpperCase()
			+ "\n短横线  " + w.join("-");
	}

	function asciitable(){
		var names = ["NUL", "SOH", "STX", "ETX", "EOT", "ENQ", "ACK", "BEL", "BS", "HT", "LF", "VT", "FF", "CR", "SO", "SI", "DLE", "DC1", "DC2", "DC3", "DC4", "NAK", "SYN", "ETB", "CAN", "EM", "SUB", "ESC", "FS", "GS", "RS", "US"];
		var lines = ["DEC HEX 字符"];
		var i;
		for (i = 0; i < 128; i++) {
			var hex = i.toString(16).toUpperCase();
			if (hex.length < 2) hex = "0" + hex;
			var dec = String(i);
			while (dec.length < 3) dec = " " + dec;
			var ch = i < 32 ? names[i] : (i === 127 ? "DEL" : String.fromCharCode(i));
			lines.push(dec + "  " + hex + "  " + ch);
		}
		return lines.join("\n");
	}

	function tempconv(text, unit){
		var raw = String(text).trim();
		var n = Number(raw);
		if (raw === "" || !isFinite(n)) throw new Error("请输入数字");
		var c;
		if (unit === "C") c = n;
		else if (unit === "F") c = (n - 32) * 5 / 9;
		else if (unit === "K") c = n - 273.15;
		else throw new Error("请选择单位");
		if (c < -273.15 - 1e-9) throw new Error("低于绝对零度");
		return "摄氏度  " + trimnum(c) + "\n华氏度  " + trimnum(c * 9 / 5 + 32) + "\n开尔文  " + trimnum(c + 273.15);
	}

	function colorconv(mode, text){
		var rgb = colorrgb(mode, text);
		var hsl = rgb2hsl(rgb[0], rgb[1], rgb[2]);
		var hsv = rgb2hsv(rgb[0], rgb[1], rgb[2]);
		var cmyk = rgb2cmyk(rgb[0], rgb[1], rgb[2]);
		return "HEX  #" + hex2(rgb[0]) + hex2(rgb[1]) + hex2(rgb[2])
			+ "\nRGB  " + rgb[0] + ", " + rgb[1] + ", " + rgb[2]
			+ "\nHSL  " + hsl[0] + ", " + hsl[1] + "%, " + hsl[2] + "%"
			+ "\nHSV  " + hsv[0] + ", " + hsv[1] + "%, " + hsv[2] + "%"
			+ "\nCMYK  " + cmyk[0] + "%, " + cmyk[1] + "%, " + cmyk[2] + "%, " + cmyk[3] + "%"
			+ "\nCMYK 用的是简单公式，不是印刷配置文件。";
	}

	function colorrgb(mode, text){
		text = String(text).trim();
		if (mode === "hex") {
			var h = text.replace(/^#/, "");
			if (!/^[0-9a-f]{3}$/i.test(h) && !/^[0-9a-f]{6}$/i.test(h)) throw new Error("HEX 用 #RGB 或 #RRGGBB");
			if (h.length === 3) h = h.charAt(0) + h.charAt(0) + h.charAt(1) + h.charAt(1) + h.charAt(2) + h.charAt(2);
			return [parseInt(h.slice(0, 2), 16), parseInt(h.slice(2, 4), 16), parseInt(h.slice(4, 6), 16)];
		}
		var p = text.split(/[,，\s]+/);
		if (p.length < 3 || (mode === "cmyk" && p.length < 4)) throw new Error("请按所选格式填写，用逗号或空格分开");
		if (mode === "rgb") return [byte8(p[0]), byte8(p[1]), byte8(p[2])];
		if (mode === "hsl") return hsl2rgb(hue(p[0]), pct(p[1]), pct(p[2]));
		if (mode === "hsv") return hsv2rgb(hue(p[0]), pct(p[1]), pct(p[2]));
		if (mode === "cmyk") return cmyk2rgb(pct(p[0]), pct(p[1]), pct(p[2]), pct(p[3]));
		throw new Error("请选择格式");
	}

	function byte8(s){
		var n = Number(s);
		if (!isFinite(n) || n < 0 || n > 255) throw new Error("RGB 分量要在 0 到 255");
		return Math.round(n);
	}

	function hue(s){
		var n = Number(String(s).replace(/deg$/i, ""));
		if (!isFinite(n)) throw new Error("色相无效");
		n = n % 360;
		if (n < 0) n += 360;
		return n;
	}

	function pct(s){
		var n = Number(String(s).replace("%", ""));
		if (!isFinite(n) || n < 0 || n > 100) throw new Error("百分比要在 0 到 100");
		return n;
	}

	function hex2(n){
		var s = n.toString(16);
		return (s.length < 2 ? "0" : "") + s;
	}

	function rgb2hsl(r, g, b){
		r /= 255; g /= 255; b /= 255;
		var max = Math.max(r, g, b);
		var min = Math.min(r, g, b);
		var l = (max + min) / 2;
		var h = 0;
		var s = 0;
		if (max !== min) {
			var d = max - min;
			s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
			if (max === r) h = (g - b) / d + (g < b ? 6 : 0);
			else if (max === g) h = (b - r) / d + 2;
			else h = (r - g) / d + 4;
			h *= 60;
		}
		return [Math.round(h), Math.round(s * 100), Math.round(l * 100)];
	}

	function rgb2hsv(r, g, b){
		r /= 255; g /= 255; b /= 255;
		var max = Math.max(r, g, b);
		var min = Math.min(r, g, b);
		var d = max - min;
		var h = 0;
		if (d !== 0) {
			if (max === r) h = (g - b) / d + (g < b ? 6 : 0);
			else if (max === g) h = (b - r) / d + 2;
			else h = (r - g) / d + 4;
			h *= 60;
		}
		var s = max === 0 ? 0 : d / max;
		return [Math.round(h), Math.round(s * 100), Math.round(max * 100)];
	}

	function rgb2cmyk(r, g, b){
		if (r === 0 && g === 0 && b === 0) return [0, 0, 0, 100];
		var rp = r / 255;
		var gp = g / 255;
		var bp = b / 255;
		var k = 1 - Math.max(rp, gp, bp);
		return [
			Math.round((1 - rp - k) / (1 - k) * 100),
			Math.round((1 - gp - k) / (1 - k) * 100),
			Math.round((1 - bp - k) / (1 - k) * 100),
			Math.round(k * 100),
		];
	}

	function hsl2rgb(h, s, l){
		s /= 100; l /= 100;
		var c = (1 - Math.abs(2 * l - 1)) * s;
		var hp = h / 60;
		var x = c * (1 - Math.abs(hp % 2 - 1));
		var m = l - c / 2;
		return hue2rgb(hp, c, x, m);
	}

	function hsv2rgb(h, s, v){
		s /= 100; v /= 100;
		var c = v * s;
		var hp = h / 60;
		var x = c * (1 - Math.abs(hp % 2 - 1));
		var m = v - c;
		return hue2rgb(hp, c, x, m);
	}

	function hue2rgb(hp, c, x, m){
		var r = 0, g = 0, b = 0;
		if (hp < 1) { r = c; g = x; }
		else if (hp < 2) { r = x; g = c; }
		else if (hp < 3) { g = c; b = x; }
		else if (hp < 4) { g = x; b = c; }
		else if (hp < 5) { r = x; b = c; }
		else { r = c; b = x; }
		return [Math.round((r + m) * 255), Math.round((g + m) * 255), Math.round((b + m) * 255)];
	}

	function cmyk2rgb(c, m, y, k){
		c /= 100; m /= 100; y /= 100; k /= 100;
		return [
			Math.round(255 * (1 - c) * (1 - k)),
			Math.round(255 * (1 - m) * (1 - k)),
			Math.round(255 * (1 - y) * (1 - k)),
		];
	}

	function rcolor(count){
		var n = parseInt(count, 10);
		if (!(n >= 1 && n <= 20)) throw new Error("个数要在 1 到 20");
		if (!window.crypto || typeof crypto.getRandomValues !== "function") throw new Error("浏览器不能生成随机数");
		var lines = [];
		var i;
		for (i = 0; i < n; i++) {
			var b = new Uint8Array(3);
			crypto.getRandomValues(b);
			var hex = "#" + hex2(b[0]) + hex2(b[1]) + hex2(b[2]);
			lines.push(hex + "  rgb(" + b[0] + ", " + b[1] + ", " + b[2] + ")");
		}
		return lines.join("\n");
	}

	function jsonesc(text, mode){
		text = String(text);
		if (text.length > 200000) throw new Error("文字太长");
		if (mode === "dec") {
			var src = text.trim();
			if (!src) throw new Error("请输入文字");
			if (src.charAt(0) !== '"') src = '"' + src + '"';
			var v;
			try { v = JSON.parse(src); }
			catch (ex) { throw new Error("不是合法的 JSON 字符串"); }
			if (typeof v !== "string") throw new Error("请输入 JSON 字符串");
			return v;
		}
		return JSON.stringify(text);
	}

	function repltext(text, find, rep, useRe){
		text = String(text);
		find = String(find);
		rep = String(rep);
		if (!find) throw new Error("请输入查找内容");
		if (text.length > 200000) throw new Error("文字太长");
		if (useRe) {
			if (find.length > 200) throw new Error("表达式太长");
			if (text.length > 20000) throw new Error("正则替换的文本最多 2 万字");
			var re;
			try { re = new RegExp(find, "g"); }
			catch (ex) { throw new Error("表达式无效"); }
			var n = 0;
			var guard = 0;
			var out = text.replace(re, function(){
				n++;
				guard++;
				if (guard > 10000) throw new Error("匹配太多");
				return rep;
			});
			return out + "\n\n替换 " + n + " 处";
		}
		var parts = text.split(find);
		return parts.join(rep) + "\n\n替换 " + (parts.length - 1) + " 处";
	}

	function uaparse(text){
		var s = String(text).trim();
		if (!s) throw new Error("请输入 User-Agent");
		if (s.length > 2000) throw new Error("太长");
		var os = "未知";
		if (/Windows NT 10\.0/.test(s)) os = "Windows 10 或 11";
		else if (/Windows NT 6\.3/.test(s)) os = "Windows 8.1";
		else if (/Windows NT 6\.1/.test(s)) os = "Windows 7";
		else if (/Android/.test(s)) os = "Android";
		else if (/iPhone|iPad|iPod/.test(s)) os = "iOS";
		else if (/Mac OS X/.test(s)) os = "macOS";
		else if (/Linux/.test(s)) os = "Linux";
		var br = "未知";
		var m;
		if ((m = /Edg\/(\d+)/.exec(s))) br = "Edge " + m[1];
		else if ((m = /OPR\/(\d+)/.exec(s))) br = "Opera " + m[1];
		else if ((m = /Chrome\/(\d+)/.exec(s))) br = "Chrome " + m[1];
		else if ((m = /Firefox\/(\d+)/.exec(s))) br = "Firefox " + m[1];
		else if (/Safari\//.test(s) && (m = /Version\/(\d+)/.exec(s))) br = "Safari " + m[1];
		return "系统  " + os + "\n浏览器  " + br;
	}

	function crondesc(expr){
		var s = String(expr).trim().replace(/\s+/g, " ");
		if (!s) throw new Error("请输入表达式");
		var alias = {
			"@yearly": "0 0 1 1 *",
			"@annually": "0 0 1 1 *",
			"@monthly": "0 0 1 * *",
			"@weekly": "0 0 * * 0",
			"@daily": "0 0 * * *",
			"@midnight": "0 0 * * *",
			"@hourly": "0 * * * *",
		};
		if (alias[s]) s = alias[s];
		else if (s.charAt(0) === "@") throw new Error("不支持这个 @ 简写");
		var p = s.split(" ");
		var names;
		var ranges;
		if (p.length === 5) {
			names = ["分", "时", "日", "月", "周"];
			ranges = [[0, 59], [0, 23], [1, 31], [1, 12], [0, 7]];
		}
		else if (p.length === 6) {
			names = ["秒", "分", "时", "日", "月", "周"];
			ranges = [[0, 59], [0, 59], [0, 23], [1, 31], [1, 12], [0, 7]];
		}
		else throw new Error("用 5 段：分 时 日 月 周");
		var lines = [];
		var i;
		for (i = 0; i < p.length; i++) lines.push(names[i] + "  " + cronpart(p[i], ranges[i][0], ranges[i][1]));
		return lines.join("\n");
	}

	function cronpart(part, lo, hi){
		if (part === "*") return "每档";
		var step = part.split("/");
		if (step.length > 2) throw new Error("表达式无效");
		var base = step[0];
		var every = "";
		if (step.length === 2) {
			if (!/^\d+$/.test(step[1]) || Number(step[1]) < 1) throw new Error("步长无效");
			every = "，每隔 " + step[1];
		}
		if (base === "*") return "每档" + every;
		var bits = base.split(",");
		var out = [];
		var i;
		for (i = 0; i < bits.length; i++) {
			var bit = bits[i];
			var span = bit.split("-");
			if (span.length === 1) {
				cronnum(span[0], lo, hi);
				out.push(span[0]);
			}
			else if (span.length === 2) {
				cronnum(span[0], lo, hi);
				cronnum(span[1], lo, hi);
				out.push(span[0] + " 到 " + span[1]);
			}
			else throw new Error("表达式无效");
		}
		return out.join("、") + every;
	}

	function cronnum(s, lo, hi){
		if (!/^\d+$/.test(s)) throw new Error("只支持数字");
		var n = Number(s);
		if (n < lo || n > hi) throw new Error("有数字超出范围");
	}

	function workdays(a, b){
		if (!a || !b) throw new Error("请选择两个日期");
		var da = new Date(a + "T00:00:00");
		var db = new Date(b + "T00:00:00");
		if (isNaN(da.getTime()) || isNaN(db.getTime())) throw new Error("日期无效");
		if (db < da) throw new Error("开始不能晚于结束");
		var days = Math.round((db.getTime() - da.getTime()) / 86400000);
		if (days > 3660) throw new Error("最多 10 年");
		var n = 0;
		var d = da;
		while (d <= db) {
			var w = d.getDay();
			if (w !== 0 && w !== 6) n++;
			d.setDate(d.getDate() + 1);
		}
		return "工作日  " + n + " 天\n已跳过周六和周日，不含法定假日";
	}

	function loanpay(p, rate, months, mode){
		var money = Number(String(p).trim());
		var y = Number(String(rate).trim());
		var n = parseInt(months, 10);
		if (!(money > 0)) throw new Error("请输入贷款金额");
		if (!(y >= 0) || y > 100) throw new Error("年利率填百分数，例如 3.5");
		if (!(n >= 1 && n <= 600)) throw new Error("月数要在 1 到 600");
		var r = y / 100 / 12;
		if (mode === "principal") {
			var prin = money / n;
			var first = prin + money * r;
			var last = prin + prin * r;
			var interest = (n + 1) * money * r / 2;
			return "等额本金\n每月本金  " + trimnum(prin)
				+ "\n首月还款  " + trimnum(first)
				+ "\n末月还款  " + trimnum(last)
				+ "\n利息合计  " + trimnum(interest)
				+ "\n还款合计  " + trimnum(money + interest);
		}
		var pay = r === 0 ? money / n : money * r * Math.pow(1 + r, n) / (Math.pow(1 + r, n) - 1);
		var total = pay * n;
		return "等额本息\n每月还款  " + trimnum(pay)
			+ "\n利息合计  " + trimnum(total - money)
			+ "\n还款合计  " + trimnum(total);
	}

	function compound(p, rate, years, times){
		var money = Number(String(p).trim());
		var y = Number(String(rate).trim());
		var t = Number(String(years).trim());
		var n = parseInt(times, 10);
		if (!(money > 0)) throw new Error("请输入本金");
		if (!(y >= 0) || y > 100) throw new Error("年利率填百分数，例如 3");
		if (!(t > 0) || t > 100) throw new Error("年数要大于 0，且不超过 100");
		if (!(n >= 1 && n <= 365)) throw new Error("每年计息次数要在 1 到 365");
		var a = money * Math.pow(1 + y / 100 / n, n * t);
		return "本息  " + trimnum(a) + "\n利息  " + trimnum(a - money);
	}

	function macgen(count){
		var n = parseInt(count, 10);
		if (!(n >= 1 && n <= 20)) throw new Error("个数要在 1 到 20");
		if (!window.crypto || typeof crypto.getRandomValues !== "function") throw new Error("浏览器不能生成随机数");
		var lines = [];
		var i;
		for (i = 0; i < n; i++) {
			var b = new Uint8Array(6);
			crypto.getRandomValues(b);
			b[0] = (b[0] & 252) | 2;
			var parts = [];
			var j;
			for (j = 0; j < 6; j++) parts.push(hex2(b[j]).toUpperCase());
			lines.push(parts.join(":"));
		}
		return lines.join("\n");
	}

	function htmlmin(text){
		var s = String(text);
		if (s.length > 200000) throw new Error("文字太长");
		var out = "";
		var i = 0;
		var keep = "";
		while (i < s.length) {
			if (!keep && s.slice(i, i + 4) === "<!--") {
				var end = s.indexOf("-->", i + 4);
				if (end < 0) { out += s.slice(i); break; }
				i = end + 3;
				continue;
			}
			if (s.charAt(i) === "<") {
				var rest = s.slice(i);
				var open = /^<(pre|script|style)\b/i.exec(rest);
				var close = /^<\/(pre|script|style)\b/i.exec(rest);
				if (!keep && open) keep = open[1].toLowerCase();
				else if (keep && close && close[1].toLowerCase() === keep) keep = "";
			}
			if (!keep && isws(s.charAt(i)) && out.charAt(out.length - 1) === ">") {
				var j = i;
				while (j < s.length && isws(s.charAt(j))) j++;
				if (s.charAt(j) === "<") { i = j; continue; }
			}
			out += s.charAt(i);
			i++;
		}
		return out.trim();
	}

	function isws(c){
		return c === " " || c === "\n" || c === "\r" || c === "\t" || c === "\f";
	}

	function cssmin(text){
		var s = String(text);
		if (s.length > 200000) throw new Error("文字太长");
		var out = "";
		var i = 0;
		var q = "";
		while (i < s.length) {
			var c = s.charAt(i);
			if (q) {
				out += c;
				if (c === "\\" && i + 1 < s.length) { out += s.charAt(i + 1); i += 2; continue; }
				if (c === q) q = "";
				i++;
				continue;
			}
			if (c === '"' || c === "'") { q = c; out += c; i++; continue; }
			if (c === "/" && s.charAt(i + 1) === "*") {
				var end = s.indexOf("*/", i + 2);
				if (end < 0) throw new Error("注释没有结束");
				i = end + 2;
				continue;
			}
			out += c;
			i++;
		}
		return out.replace(/\s+/g, " ").replace(/\s*([{}:;,])\s*/g, "$1").trim();
	}

	function gradcss(a, b, angle){
		var c1 = colorrgb("hex", a);
		var c2 = colorrgb("hex", b);
		var n = Number(angle);
		if (!isFinite(n) || n < 0 || n > 360) throw new Error("角度要在 0 到 360");
		var h1 = "#" + hex2(c1[0]) + hex2(c1[1]) + hex2(c1[2]);
		var h2 = "#" + hex2(c2[0]) + hex2(c2[1]) + hex2(c2[2]);
		return "background: linear-gradient(" + trimnum(n) + "deg, " + h1 + ", " + h2 + ");";
	}

	function stoptick(){
		if (watchOn) watchAcc += Date.now() - watch0;
		watchOn = false;
		if (tick) {
			clearInterval(tick);
			tick = null;
		}
	}

	function fmtwatch(ms){
		if (ms < 0) ms = 0;
		var cs = Math.floor(ms / 10);
		var cent = cs % 100;
		var sec = Math.floor(cs / 100);
		return pad2(Math.floor(sec / 60)) + ":" + pad2(sec % 60) + "." + pad2(cent);
	}

	function fmthms(sec){
		var s = sec < 0 ? 0 : sec;
		return pad2(Math.floor(s / 3600)) + ":" + pad2(Math.floor(s / 60) % 60) + ":" + pad2(s % 60);
	}

	function watchtoggle(){
		var btn = $("box-start");
		if (watchOn) {
			stoptick();
			if (btn) btn.textContent = "开始";
			$("box-out").value = fmtwatch(watchAcc);
			return;
		}
		watch0 = Date.now();
		watchOn = true;
		if (btn) btn.textContent = "停止";
		tick = setInterval(function(){
			$("box-out").value = fmtwatch(watchAcc + Date.now() - watch0);
		}, 50);
	}

	function watchreset(){
		stoptick();
		watchAcc = 0;
		var btn = $("box-start");
		if (btn) btn.textContent = "开始";
		return "00:00.00";
	}

	function countdowngo(){
		stoptick();
		var sec = parseInt(val("box-n"), 10);
		if (!(sec >= 1 && sec <= 86400)) throw new Error("秒数要在 1 到 86400");
		var end = Date.now() + sec * 1000;
		$("box-out").value = fmthms(sec);
		tick = setInterval(function(){
			var left = Math.ceil((end - Date.now()) / 1000);
			if (left <= 0) {
				if (tick) { clearInterval(tick); tick = null; }
				$("box-out").value = "00:00:00\n时间到";
				msg("box-msg", "时间到");
				return;
			}
			$("box-out").value = fmthms(left);
		}, 200);
	}

	function imgb64(){
		var input = $("box-file");
		var file = input && input.files && input.files[0];
		if (!file) {
			msg("box-msg", "请选择图片", true);
			return;
		}
		if (file.size > 8 * 1024 * 1024) {
			msg("box-msg", "图片要小于 8 MB", true);
			return;
		}
		var reader = new FileReader();
		reader.onload = function(){
			$("box-out").value = String(reader.result || "");
			msg("box-msg", "完成");
		};
		reader.onerror = function(){ msg("box-msg", "读取失败", true); };
		msg("box-msg", "读取中");
		reader.readAsDataURL(file);
	}

	function xmlfmt(text, mode){
		var s = String(text).trim();
		if (!s) throw new Error("请输入 XML");
		if (s.length > 200000) throw new Error("文字太长");
		if (typeof DOMParser === "function") {
			var doc = new DOMParser().parseFromString(s, "application/xml");
			if (doc.getElementsByTagName("parsererror").length) throw new Error("不是合法的 XML");
		}
		s = s.replace(/>\s+</g, "><");
		if (mode === "min") return s;
		var lines = s.replace(/(>)(<)/g, "$1\n$2").split("\n");
		var pad = 0;
		var out = [];
		var i;
		for (i = 0; i < lines.length; i++) {
			var line = lines[i];
			if (/^<\/[^>]+>/.test(line)) pad = Math.max(pad - 1, 0);
			var indent = "";
			var k;
			for (k = 0; k < pad; k++) indent += "  ";
			out.push(indent + line);
			if (/^<[^!?/][^>]*>$/.test(line) && !/\/>$/.test(line)) pad++;
		}
		return out.join("\n");
	}

	function htmltable(text){
		var raw = String(text).replace(/\r\n/g, "\n").replace(/\r/g, "\n");
		if (raw.length > 200000) throw new Error("文字太长");
		var rows = raw.split("\n");
		var body = [];
		var i;
		for (i = 0; i < rows.length; i++) if (rows[i]) body.push(rows[i]);
		if (!body.length) throw new Error("请输入表格文字");
		if (body.length > 500) throw new Error("最多 500 行");
		var sep = body[0].indexOf("\t") >= 0 ? "\t" : ",";
		var html = "<table>\n";
		for (i = 0; i < body.length; i++) {
			var cells = body[i].split(sep);
			var tag = i === 0 ? "th" : "td";
			var j;
			html += "<tr>";
			for (j = 0; j < cells.length; j++) html += "<" + tag + ">" + htmlenc(cells[j]) + "</" + tag + ">";
			html += "</tr>\n";
		}
		return html + "</table>";
	}

	function randstr(len, count, set){
		var n = parseInt(len, 10);
		var c = parseInt(count, 10);
		var chars = String(set || "");
		if (!(n >= 1 && n <= 128)) throw new Error("长度要在 1 到 128");
		if (!(c >= 1 && c <= 50)) throw new Error("个数要在 1 到 50");
		if (!chars) throw new Error("请填写字符集");
		if (chars.length > 200) throw new Error("字符集太长");
		var lines = [];
		var i;
		var j;
		for (i = 0; i < c; i++) {
			var s = "";
			for (j = 0; j < n; j++) s += chars.charAt(randint(chars.length));
			lines.push(s);
		}
		return lines.join("\n");
	}

	function placesize(w, h){
		var width = parseInt(w, 10);
		var height = parseInt(h, 10);
		if (!(width >= 1 && width <= 2000) || !(height >= 1 && height <= 2000)) throw new Error("宽高要在 1 到 2000");
		return [width, height];
	}

	function placeholder(w, h, label, bg, fg){
		var wh = placesize(w, h);
		var back = colorrgb("hex", bg);
		var ink = colorrgb("hex", fg);
		if (typeof document === "undefined" || !document.createElement) throw new Error("浏览器不能画图");
		var canvas = document.createElement("canvas");
		canvas.width = wh[0];
		canvas.height = wh[1];
		var g = canvas.getContext("2d");
		if (!g) throw new Error("浏览器不能画图");
		g.fillStyle = "#" + hex2(back[0]) + hex2(back[1]) + hex2(back[2]);
		g.fillRect(0, 0, wh[0], wh[1]);
		g.fillStyle = "#" + hex2(ink[0]) + hex2(ink[1]) + hex2(ink[2]);
		g.font = Math.max(12, Math.round(Math.min(wh[0], wh[1]) / 8)) + "px sans-serif";
		g.textAlign = "center";
		g.textBaseline = "middle";
		g.fillText(String(label || "").trim() || (wh[0] + "×" + wh[1]), wh[0] / 2, wh[1] / 2);
		var url = canvas.toDataURL("image/png");
		var img = $("box-img");
		if (img) {
			img.src = url;
			img.hidden = false;
		}
		return url;
	}

	function dateadd(date, days){
		if (!date) throw new Error("请选择日期");
		var n = parseInt(days, 10);
		if (!isFinite(n) || Math.abs(n) > 36600) throw new Error("天数超出范围");
		var d = new Date(date + "T00:00:00");
		if (isNaN(d.getTime())) throw new Error("日期无效");
		d.setDate(d.getDate() + n);
		return ymd(d) + "  周" + "日一二三四五六".charAt(d.getDay());
	}

	function monthadd(date, months){
		if (!date) throw new Error("请选择日期");
		var n = parseInt(months, 10);
		if (!isFinite(n) || Math.abs(n) > 1200) throw new Error("月数超出范围");
		var d = new Date(date + "T00:00:00");
		if (isNaN(d.getTime())) throw new Error("日期无效");
		var day = d.getDate();
		d.setDate(1);
		d.setMonth(d.getMonth() + n);
		var last = new Date(d.getFullYear(), d.getMonth() + 1, 0).getDate();
		d.setDate(day < last ? day : last);
		return ymd(d) + "  周" + "日一二三四五六".charAt(d.getDay());
	}

	function jsondiff(a, b){
		var left = String(a);
		var right = String(b);
		if (left.length > 200000 || right.length > 200000) throw new Error("文字太长");
		var ja;
		var jb;
		try { ja = JSON.stringify(JSON.parse(left), null, 2); }
		catch (ex) { throw new Error("甲不是合法的 JSON"); }
		try { jb = JSON.stringify(JSON.parse(right), null, 2); }
		catch (ex) { throw new Error("乙不是合法的 JSON"); }
		return linediff(ja, jb);
	}

	function sciexpr(src){
		var s = String(src == null ? "" : src).replace(/\s+/g, "");
		if (!s) throw new Error("请输入算式");
		if (s.length > 200) throw new Error("算式太长");
		var i = 0;
		var v = expr();
		if (i !== s.length) throw new Error("算式里有无法识别的部分");
		return trimnum(v);
		function expr(){
			var left = term();
			while (peek() === "+" || peek() === "-") {
				var op = peek();
				i++;
				var right = term();
				left = op === "+" ? left + right : left - right;
			}
			return left;
		}
		function term(){
			var left = pow();
			while (peek() === "*" || peek() === "/" || peek() === "%") {
				var op = peek();
				i++;
				var right = pow();
				if (op === "*") left = left * right;
				else if (right === 0) throw new Error("除数不能为 0");
				else left = op === "/" ? left / right : left % right;
			}
			return left;
		}
		function pow(){
			var left = unary();
			if (peek() === "^") {
				i++;
				var right = pow();
				var v = Math.pow(left, right);
				if (!isFinite(v)) throw new Error("乘方结果无效");
				return v;
			}
			return left;
		}
		function unary(){
			if (peek() === "-" || peek() === "+") {
				var op = peek();
				i++;
				return op === "-" ? -unary() : unary();
			}
			return primary();
		}
		function primary(){
			if (peek() === "(") {
				i++;
				var v = expr();
				if (peek() !== ")") throw new Error("括号没有配对");
				i++;
				return v;
			}
			if (isletter(peek())) return named();
			return number();
		}
		function named(){
			var start = i;
			while (isletter(peek())) i++;
			var name = s.slice(start, i).toLowerCase();
			if (peek() === "(") {
				i++;
				var arg = expr();
				if (peek() !== ")") throw new Error("括号没有配对");
				i++;
				return applyfn(name, arg);
			}
			if (name === "pi") return Math.PI;
			if (name === "e") return Math.E;
			throw new Error("没有这个名字");
		}
		function number(){
			var start = i;
			while (i < s.length && s.charAt(i) >= "0" && s.charAt(i) <= "9") i++;
			if (peek() === "." && i + 1 < s.length && s.charAt(i + 1) >= "0" && s.charAt(i + 1) <= "9") {
				i++;
				while (i < s.length && s.charAt(i) >= "0" && s.charAt(i) <= "9") i++;
			}
			if (start === i) throw new Error("这里需要数字");
			return Number(s.slice(start, i));
		}
		function peek(){ return s.charAt(i); }
		function isletter(c){ return (c >= "a" && c <= "z") || (c >= "A" && c <= "Z"); }
	}

	function applyfn(name, v){
		var rad = v * Math.PI / 180;
		var out;
		if (name === "sin") out = Math.sin(rad);
		else if (name === "cos") out = Math.cos(rad);
		else if (name === "tan") out = Math.tan(rad);
		else if (name === "asin") out = Math.asin(v) * 180 / Math.PI;
		else if (name === "acos") out = Math.acos(v) * 180 / Math.PI;
		else if (name === "atan") out = Math.atan(v) * 180 / Math.PI;
		else if (name === "ln") out = Math.log(v);
		else if (name === "log") out = Math.log(v) / Math.LN10;
		else if (name === "sqrt") out = Math.sqrt(v);
		else if (name === "abs") out = Math.abs(v);
		else if (name === "floor") out = Math.floor(v);
		else if (name === "ceil") out = Math.ceil(v);
		else throw new Error("没有这个函数");
		if (!isFinite(out)) throw new Error("函数结果无效");
		return out;
	}

	function renamebatch(text, prefix, suffix, start){
		var rows = String(text).replace(/\r\n/g, "\n").replace(/\r/g, "\n").split("\n");
		var names = [];
		var i;
		for (i = 0; i < rows.length; i++) if (rows[i]) names.push(rows[i]);
		if (!names.length) throw new Error("请输入文件名");
		if (names.length > 1000) throw new Error("最多 1000 行");
		var n = parseInt(start, 10);
		if (!isFinite(n)) n = 1;
		var width = String(Math.abs(n + names.length - 1)).length;
		var out = [];
		for (i = 0; i < names.length; i++) {
			var num = String(n + i);
			var bare = num.replace("-", "");
			while (bare.length < width) {
				bare = "0" + bare;
				num = (num.charAt(0) === "-" ? "-" : "") + bare;
				bare = num.replace("-", "");
			}
			out.push(String(prefix || "") + num + String(suffix || "") + names[i]);
		}
		return out.join("\n");
	}

	function jsmin(text){
		var s = String(text);
		if (s.length > 200000) throw new Error("文字太长");
		var out = "";
		var i = 0;
		var q = "";
		while (i < s.length) {
			var c = s.charAt(i);
			var n = s.charAt(i + 1);
			if (q) {
				out += c;
				if (c === "\\" && i + 1 < s.length) { out += n; i += 2; continue; }
				if (c === q) q = "";
				i++;
				continue;
			}
			if (c === '"' || c === "'" || c === "`") { q = c; out += c; i++; continue; }
			if (c === "/" && n === "/") {
				i += 2;
				while (i < s.length && s.charAt(i) !== "\n") i++;
				continue;
			}
			if (c === "/" && n === "*") {
				var end = s.indexOf("*/", i + 2);
				if (end < 0) throw new Error("注释没有结束");
				i = end + 2;
				continue;
			}
			out += c;
			i++;
		}
		return out.replace(/[ \t]+\n/g, "\n").replace(/\n{3,}/g, "\n\n").trim();
	}

	function sqlfmt(text, mode){
		var s = String(text).trim();
		if (!s) throw new Error("请输入 SQL");
		if (s.length > 200000) throw new Error("文字太长");
		s = s.replace(/--[^\n]*/g, " ").replace(/\s+/g, " ").trim();
		if (mode === "min") return s;
		var keys = ["order by", "group by", "inner join", "left join", "right join", "insert into", "delete from", "select", "from", "where", "having", "limit", "values", "update", "set", "and", "or"];
		var i;
		for (i = 0; i < keys.length; i++) {
			var k = keys[i];
			var re = new RegExp("\\b" + k.replace(" ", "\\s+") + "\\b", "ig");
			s = s.replace(re, "\n" + k.toUpperCase());
		}
		return s.replace(/^\n+/, "").replace(/\n{2,}/g, "\n").trim();
	}

	function tokencount(text){
		var s = String(text);
		if (s.length > 200000) throw new Error("文字太长");
		var n = 0;
		var re = /[\u3400-\u9fff]|[A-Za-z]+|\d+|[^\s]/g;
		while (re.exec(s)) n++;
		return "约 " + n + " 个 token\n字符  " + s.length + "\n这是按汉字、英文单词和符号粗算的，不是某个模型的分词器。";
	}

	function html2js(text){
		var s = String(text);
		if (s.length > 200000) throw new Error("文字太长");
		return "var html = " + JSON.stringify(s) + ";";
	}

	function cssbtn(label, bg, fg, radius){
		var back = colorrgb("hex", bg);
		var ink = colorrgb("hex", fg);
		var r = parseInt(radius, 10);
		if (!(r >= 0 && r <= 999)) throw new Error("圆角要在 0 到 999");
		var text = String(label || "按钮");
		return ".btn {\n  background: #" + hex2(back[0]) + hex2(back[1]) + hex2(back[2])
			+ ";\n  color: #" + hex2(ink[0]) + hex2(ink[1]) + hex2(ink[2])
			+ ";\n  border: 0;\n  border-radius: " + r + "px;\n  padding: 8px 14px;\n}\n/* " + text + " */";
	}

	function fakedata(count){
		var n = parseInt(count, 10);
		if (!(n >= 1 && n <= 50)) throw new Error("行数要在 1 到 50");
		var sur = ["赵", "钱", "孙", "李", "周", "吴", "郑", "王"];
		var giv = ["伟", "芳", "娜", "敏", "静", "磊", "洋", "艳"];
		var lines = ["姓名,邮箱,手机"];
		var i;
		for (i = 0; i < n; i++) {
			var name = sur[randint(sur.length)] + giv[randint(giv.length)];
			var phone = "1" + String(3 + randint(6));
			var k;
			for (k = 0; k < 9; k++) phone += String(randint(10));
			lines.push(name + ",user" + (i + 1) + "@example.com," + phone);
		}
		return lines.join("\n");
	}

	function palette(hex){
		var rgb = colorrgb("hex", hex);
		var hsl = rgb2hsl(rgb[0], rgb[1], rgb[2]);
		function one(delta){
			var h = (hsl[0] + delta) % 360;
			if (h < 0) h += 360;
			return hslhex(h, hsl[1], hsl[2]);
		}
		return "基准  " + one(0) + "\n互补  " + one(180) + "\n邻近  " + one(-30) + "\n邻近  " + one(30) + "\n三角  " + one(120) + "\n三角  " + one(240);
	}

	function hslhex(h, s, l){
		var rgb = hsl2rgb(h, s, l);
		return "#" + hex2(rgb[0]) + hex2(rgb[1]) + hex2(rgb[2]);
	}

	function esc(s){
		return String(s == null ? "" : s)
			.replace(/&/g, "&amp;")
			.replace(/</g, "&lt;")
			.replace(/>/g, "&gt;");
	}
})();
