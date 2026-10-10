(function(){
	var ttsItems = [];
	function ttslabel(engine){
		if (engine === "sherpa") return "离线模型";
		if (engine === "sapi") return "SAPI";
		if (engine === "winrt") return "Windows 语音";
		if (engine === "edge") return "Edge 在线";
		return engine || "";
	}

	var ttsEdgeWait = 0;

	function loadtts(){
		if (ttsItems.length) return;
		fetchtts();
	}

	function fetchtts(){
		SK.get("/api/tts/models", function(data){
			var items = data && data.length != null ? data : [];
			var hasEdge = false;
			var i;
			for (i = 0; i < items.length; i++) {
				if ((items[i].engine || "") === "edge") { hasEdge = true; break; }
			}
			if (ttsItems.length && items.length <= ttsItems.length) return;
			ttsItems = items;
			var seen = {};
			var rows = [];
			for (i = 0; i < ttsItems.length; i++) {
				var eng = ttsItems[i].engine || "";
				if (seen[eng]) continue;
				seen[eng] = true;
				rows.push({ value: eng, label: ttslabel(eng) });
			}
			SK.fillsel(SK.$("tts-eng"), rows, SK.$("tts-eng").value);
			fillttsmodels();
			if (!hasEdge && ttsEdgeWait < 2) {
				ttsEdgeWait++;
				setTimeout(fetchtts, ttsEdgeWait === 1 ? 2000 : 4000);
			}
		}, "tts-msg");
	}

	function fillttsmodels(){
		fillttslang();
		var eng = SK.$("tts-eng").value;
		var want = ttswant();
		var prev = SK.$("tts-model").value;
		var rows = [];
		var i;
		for (i = 0; i < ttsItems.length; i++) {
			if ((ttsItems[i].engine || "") !== eng) continue;
			if (!ttsmodelok(ttsItems[i], want)) continue;
			rows.push({ value: String(i), label: ttsItems[i].name || eng });
		}
		SK.fillsel(SK.$("tts-model"), rows, prev);
		fillttsvoices();
	}

	function ttsitem(){
		var n = parseInt(SK.$("tts-model").value, 10);
		if (isNaN(n) || n < 0 || n >= ttsItems.length) return null;
		return ttsItems[n];
	}

	function fillttsvoices(){
		var item = ttsitem();
		var speakers = (item && item.speakers) || [];
		var want = ttswant();
		var prevIdx = parseInt(SK.$("tts-voice").value, 10);
		var prev = speakers[prevIdx] || null;
		var rows = [];
		var pick = "";
		var i;
		for (i = 0; i < speakers.length; i++) {
			var sp = speakers[i] || {};
			if (!ttsspeakerok(item, sp, want)) continue;
			var label = sp.label || sp.name || sp.key || String(i);
			if (!sp.label && sp.lang) label += " · " + sp.lang;
			rows.push({ value: String(i), label: label });
			if (prev && ((prev.key && prev.key === sp.key) || (!prev.key && prev.name && prev.name === sp.name)))
				pick = String(i);
		}
		SK.fillsel(SK.$("tts-voice"), rows, pick);
	}

	function ttsgo(save){
		var item = ttsitem();
		if (!item) {
			SK.msg("tts-msg", "没有可用的语音", true);
			return;
		}
		if (!SK.$("tts-voice").options.length) {
			SK.msg("tts-msg", "没有符合筛选的发音人", true);
			return;
		}
		var speakers = item.speakers || [];
		var vi = parseInt(SK.$("tts-voice").value, 10);
		var sp = speakers[vi] || {};
		var speed = parseFloat(SK.$("tts-speed").value);
		if (isNaN(speed)) speed = 1;
		SK.post("/api/tts", {
			text: SK.$("tts-in").value,
			engine: item.engine,
			model: item.name,
			voice: sp.key || sp.name || "",
			speaker_id: sp.id != null ? sp.id : vi,
			speed: speed,
		}, function(data){
			if (!data || !data.wav_base64) {
				SK.msg("tts-msg", "没有音频", true);
				return;
			}
			if (save) {
				ttssavewav(data.wav_base64, sp, item);
				return;
			}
			var audio = SK.$("tts-audio");
			audio.src = "data:audio/wav;base64," + data.wav_base64;
			audio.hidden = false;
			audio.play();
		}, "tts-msg");
	}

	function ttssavewav(b64, sp, item){
		var bin = atob(b64);
		var bytes = new Uint8Array(bin.length);
		var i;
		for (i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i) & 255;
		var blob = new Blob([bytes], { type: "audio/wav" });
		var a = document.createElement("a");
		var url = URL.createObjectURL(blob);
		var name = (sp && (sp.label || sp.name || sp.key)) || (item && item.name) || "tts";
		name = String(name).replace(/[\\/:*?"<>|]+/g, " ").replace(/^\s+|\s+$/g, "");
		if (!name) name = "tts";
		if (name.length > 40) name = name.substring(0, 40).replace(/\s+$/g, "");
		a.href = url;
		a.download = name + ".wav";
		document.body.appendChild(a);
		a.click();
		document.body.removeChild(a);
		setTimeout(function(){ URL.revokeObjectURL(url); }, 4000);
		SK.msg("tts-msg", "已下载 " + a.download);
	}

	function ttswant(){
		return {
			lang: (SK.$("tts-lang") && SK.$("tts-lang").value) || "",
			gender: (SK.$("tts-gender") && SK.$("tts-gender").value) || "",
		};
	}

	function fillttslang(){
		var eng = SK.$("tts-eng").value;
		var prev = SK.$("tts-lang").value;
		var seen = {};
		var named = [
			["zh", "中文 (zh)"], ["en", "英文 (en)"], ["ja", "日文 (ja)"], ["ko", "韩文 (ko)"],
			["vi", "越南语 (vi)"], ["yue", "粤语 (yue)"], ["fr", "法语 (fr)"], ["de", "德语 (de)"], ["es", "西班牙语 (es)"],
		];
		var i, j;
		for (i = 0; i < ttsItems.length; i++) {
			if ((ttsItems[i].engine || "") !== eng) continue;
			ttslangadd(seen, ttsItems[i].lang);
			var sps = ttsItems[i].speakers || [];
			for (j = 0; j < sps.length; j++) ttslangadd(seen, sps[j].lang);
		}
		var rows = [{ value: "", label: "全部语言" }];
		var used = {};
		for (i = 0; i < named.length; i++) {
			if (!seen[named[i][0]]) continue;
			rows.push({ value: named[i][0], label: named[i][1] });
			used[named[i][0]] = 1;
		}
		var rest = [];
		for (var k in seen) if (!used[k]) rest.push(k);
		rest.sort();
		for (i = 0; i < rest.length; i++) rows.push({ value: rest[i], label: rest[i] });
		SK.fillsel(SK.$("tts-lang"), rows, seen[ttsnormlang(prev)] ? ttsnormlang(prev) : "");
	}

	function ttslangadd(seen, raw){
		var parts = String(raw || "").split(/[,/|+]/);
		var i;
		for (i = 0; i < parts.length; i++) {
			var n = ttsnormlang(parts[i]);
			if (n) seen[n] = 1;
		}
	}

	function ttsnormlang(s){
		s = String(s || "").replace(/^\s+|\s+$/g, "").toLowerCase().replace(/_/g, "-");
		if (!s) return "";
		var dash = s.indexOf("-");
		var p = dash > 0 ? s.substring(0, dash) : s;
		if (p === "zh" || p === "cmn" || p === "chinese" || p === "cn") return "zh";
		if (p === "en" || p === "english") return "en";
		if (p === "vi" || p === "vie" || p === "vietnamese") return "vi";
		if (p === "ja" || p === "jpn" || p === "japanese") return "ja";
		if (p === "ko" || p === "kor" || p === "korean") return "ko";
		if (p === "yue" || p === "cantonese") return "yue";
		return p.length <= 3 ? p : s;
	}

	function ttslangmatch(have, want){
		want = ttsnormlang(want);
		if (!want) return true;
		if (!ttsnormlang(have)) return false;
		var parts = String(have || "").split(/[,/|+]/);
		var i;
		for (i = 0; i < parts.length; i++)
			if (ttsnormlang(parts[i]) === want) return true;
		return false;
	}

	function ttsnormgender(s){
		s = String(s || "").replace(/^\s+|\s+$/g, "").toLowerCase();
		if (s === "m" || s === "male" || s === "man" || s === "男" || s === "男声") return "male";
		if (s === "f" || s === "female" || s === "woman" || s === "女" || s === "女声") return "female";
		return s;
	}

	function ttsgendermatch(have, want){
		want = ttsnormgender(want);
		if (!want) return true;
		have = ttsnormgender(have);
		return !!have && have === want;
	}

	function ttsmodelok(item, want){
		if (!item) return false;
		var speakers = item.speakers || [];
		var i;
		var langOk = !want.lang || ttslangmatch(item.lang, want.lang);
		if (!langOk) {
			for (i = 0; i < speakers.length; i++) {
				if (ttslangmatch((speakers[i] || {}).lang, want.lang)) { langOk = true; break; }
			}
		}
		if (!langOk) return false;
		if (!want.gender) return true;
		if (ttsgendermatch(item.gender, want.gender)) return true;
		var any = false;
		var allEmpty = !ttsnormgender(item.gender);
		for (i = 0; i < speakers.length; i++) {
			var g = ttsnormgender((speakers[i] || {}).gender);
			if (ttsgendermatch(g, want.gender)) any = true;
			if (g) allEmpty = false;
		}
		return any || allEmpty;
	}

	function ttsspeakerok(item, sp, want){
		if (!sp) return false;
		if (want.lang && !ttslangmatch(sp.lang, want.lang) && !ttslangmatch(item && item.lang, want.lang))
			return false;
		if (want.gender) {
			var sg = ttsnormgender(sp.gender);
			var mg = ttsnormgender(item && item.gender);
			if (sg && !ttsgendermatch(sg, want.gender)) return false;
			if (!sg && mg && !ttsgendermatch(mg, want.gender)) return false;
		}
		return true;
	}

	SK.ttslabel = ttslabel;
	SK.loadtts = loadtts;
	SK.fetchtts = fetchtts;
	SK.fillttsmodels = fillttsmodels;
	SK.ttsitem = ttsitem;
	SK.fillttsvoices = fillttsvoices;
	SK.ttsgo = ttsgo;
	SK.ttssavewav = ttssavewav;
	SK.ttswant = ttswant;
	SK.fillttslang = fillttslang;
	SK.ttslangadd = ttslangadd;
	SK.ttsnormlang = ttsnormlang;
	SK.ttslangmatch = ttslangmatch;
	SK.ttsnormgender = ttsnormgender;
	SK.ttsgendermatch = ttsgendermatch;
	SK.ttsmodelok = ttsmodelok;
	SK.ttsspeakerok = ttsspeakerok;
})();
