(function(){
	var trLangs = null;
	var trEngines = [];
	// 选模型会连带改语言，这段时间内别让语言回调把模型选择重置回「自动」
	var trLock = false;
	var BCP = {
		zh: "zh-CN", en: "en-US", ja: "ja-JP", ko: "ko-KR", cht: "zh-TW", yue: "zh-HK",
		fr: "fr-FR", de: "de-DE", es: "es-ES", pt: "pt-PT", ru: "ru-RU", it: "it-IT",
		ar: "ar-SA", th: "th-TH", vi: "vi-VN", id: "id-ID", ms: "ms-MY", hi: "hi-IN",
		tr: "tr-TR", pl: "pl-PL", nl: "nl-NL", uk: "uk-UA", sv: "sv-SE", cs: "cs-CZ",
		el: "el-GR", he: "he-IL", fa: "fa-IR", bn: "bn-BD", fi: "fi-FI", hu: "hu-HU",
		ro: "ro-RO", da: "da-DK", no: "nb-NO", tl: "fil-PH", my: "my-MM", km: "km-KH",
		lo: "lo-LA", mn: "mn-MN", uz: "uz-UZ", kk: "kk-KZ", ug: "ug-CN"
	};

	function loadtr(){
		if (trLangs) {
			filltrlang();
			fillengine();
			takedict();
			return;
		}
		SK.get("/api/translate/langs", function(data){
			trLangs = data && data.src ? data : { src: ["auto"], dst: { auto: ["auto"] } };
			filltrlang();
			fillengine();
			takedict();
		}, "tr-msg");
	}

	function takedict(){
		var text = "";
		try {
			text = sessionStorage.getItem("sk.dict.tr") || "";
			if (text) sessionStorage.removeItem("sk.dict.tr");
		}
		catch (e) {}
		if (!text || !F.id("tr-in")) return;
		F.id("tr-in").value = text;
		trgo();
	}

	function langlabel(code){
		if (code === "auto")
			return (trLangs && trLangs.auto_label) || SK.t("lang.auto.zhen") || "自动（中英互译）";
		return SK.t("lang." + code) || code;
	}

	function filltrlang(){
		if (!trLangs || !F.id("tr-src")) return;
		var prev = F.id("tr-src").value || "auto";
		var rows = [];
		var srcs = trLangs.src || ["auto"];
		var i;
		for (i = 0; i < srcs.length; i++)
			rows.push({ value: srcs[i], label: langlabel(srcs[i]) });
		SK.fillsel(F.id("tr-src"), rows, prev);
		filldst(true);
	}

	function filldst(preserve){
		if (!trLangs || !F.id("tr-dst")) return;
		var src = F.id("tr-src").value || "auto";
		var prev = preserve ? (F.id("tr-dst").value || "") : "";
		var codes = (trLangs.dst && trLangs.dst[src]) || [];
		var rows = [];
		var i;
		for (i = 0; i < codes.length; i++)
			rows.push({ value: codes[i], label: langlabel(codes[i]) });
		if (!rows.length) rows.push({ value: "", label: SK.t("lang.none.tgt") || "没有目标语言" });
		SK.fillsel(F.id("tr-dst"), rows, prev);
		trsync();
	}

	function enginelabel(e){
		var id = (e && e.engine) || "";
		if (id === "onnx") return SK.t("tr.engine.onnx") || "本地 ONNX";
		if (id === "chrome") return SK.t("tr.engine.chrome") || "Chrome";
		if (id === "llm") return (e && e.name) || "LLM";
		return SK.t("web.tr.engine.auto") || "自动";
	}

	function enginevalue(e){
		var id = (e && e.engine) || "auto";
		if (id === "llm") return "llm:" + ((e && e.name) || "");
		return id;
	}

	function curengine(){
		var v = F.id("tr-engine") ? (F.id("tr-engine").value || "auto") : "auto";
		var i;
		for (i = 0; i < trEngines.length; i++)
			if (enginevalue(trEngines[i]) === v) return trEngines[i];
		return { engine: "auto" };
	}

	function findmodel(key){
		var models = (trLangs && trLangs.models) || [];
		var i;
		for (i = 0; i < models.length; i++)
			if ((models[i].key || models[i].name) === key) return models[i];
		return null;
	}

	function modellabel(m){
		return langlabel(m.src) + " → " + langlabel(m.dst) + (m.name ? " · " + m.name : "");
	}

	function fillengine(){
		if (!F.id("tr-engine")) return;
		var prev = F.id("tr-engine").value || "auto";
		trEngines = (trLangs && trLangs.engines) || [];
		if (!trEngines.length) trEngines = [{ engine: "auto" }, { engine: "onnx" }, { engine: "chrome" }];
		var rows = [];
		var i;
		for (i = 0; i < trEngines.length; i++)
			rows.push({ value: enginevalue(trEngines[i]), label: enginelabel(trEngines[i]) });
		SK.fillsel(F.id("tr-engine"), rows, prev);
		fillmodel("");
	}

	// 模型只列本地 ONNX；引擎不是 ONNX 时只留「自动」并禁用。
	function fillmodel(keep){
		var sel = F.id("tr-model");
		if (!sel) return;
		var onnx = curengine().engine === "onnx";
		var rows = [{ value: "auto", label: SK.t("web.tr.model.auto") || "自动（按方向）" }];
		var models = (trLangs && trLangs.models) || [];
		var i;
		if (onnx) {
			for (i = 0; i < models.length; i++) {
				if (!models[i].ready) continue;
				rows.push({ value: models[i].key || models[i].name, label: modellabel(models[i]) });
			}
		}
		SK.fillsel(sel, rows, keep || "auto");
		sel.disabled = !onnx;
	}

	// 选定某个本地模型：源/目标语言跟着模型方向走。
	function trmodel(){
		var m = findmodel(F.id("tr-model") ? (F.id("tr-model").value || "") : "");
		if (!m) return;
		trLock = true;
		try {
			filltrkeep(m.src);
			filldst(false);
			setsel("tr-dst", m.dst);
		}
		finally { trLock = false; }
	}

	// 语言改了以后，选中的模型方向不再匹配就退回「自动」。
	function trsync(){
		if (trLock || !F.id("tr-model")) return;
		var m = findmodel(F.id("tr-model").value || "");
		if (!m) return;
		var src = F.id("tr-src").value || "auto";
		var dst = F.id("tr-dst").value || "auto";
		if (src === m.src && dst === m.dst) return;
		fillmodel("auto");
	}

	function setsel(id, code){
		var sel = F.id(id);
		var i;
		if (!sel) return false;
		for (i = 0; i < sel.options.length; i++) {
			if (sel.options[i].value === code) {
				sel.selectedIndex = i;
				return true;
			}
		}
		return false;
	}

	function detectcode(text){
		var han = 0;
		var kana = 0;
		var hangul = 0;
		var letter = 0;
		var i;
		var ch;
		text = String(text || "");
		for (i = 0; i < text.length; i++) {
			ch = text.charCodeAt(i);
			if (ch >= 0x4E00 && ch <= 0x9FFF) han++;
			else if ((ch >= 0x3040 && ch <= 0x30FF)) kana++;
			else if ((ch >= 0xAC00 && ch <= 0xD7AF)) hangul++;
			else if ((ch >= 65 && ch <= 90) || (ch >= 97 && ch <= 122)) letter++;
		}
		if (kana > 0) return "ja";
		if (hangul > 0) return "ko";
		if (han === 0 && letter === 0) return "zh";
		return han * 2 >= letter ? "zh" : "en";
	}

	function trgo(){
		var text = F.id("tr-in").value || "";
		if (!String(text).replace(/^\s+|\s+$/g, "")) {
			SK.msg("tr-msg", SK.t("tr.popup.need_src") || "请输入原文", true);
			return;
		}
		if (text.length > 8000) {
			SK.msg("tr-msg", SK.t("tr.popup.too_long") || "原文过长（建议 ≤8000 字）", true);
			return;
		}
		var src = F.id("tr-src").value || "auto";
		var dst = F.id("tr-dst").value || "auto";
		if (!src || !dst) {
			SK.msg("tr-msg", SK.t("tr.popup.need_lang") || "请选择源/目标语言", true);
			return;
		}
		var eng = curengine();
		var body = { text: text, src: src, dst: dst };
		if (eng.engine === "onnx") {
			body.engine = "onnx";
			var mk = F.id("tr-model") ? (F.id("tr-model").value || "") : "";
			if (mk && mk !== "auto") body.model = mk;
		}
		else if (eng.engine === "chrome") body.engine = "chrome";
		else if (eng.engine === "llm") {
			body.engine = "llm";
			if (eng.name) body.llm = eng.name;
		}
		SK.post("/api/translate", body, function(data){
			var items = (data && data.items) || [];
			var out = items.length ? (items[0].out || "") : "";
			F.id("tr-out").value = out;
			if (!String(out).replace(/^\s+|\s+$/g, "")) {
				SK.msg("tr-msg", SK.t("tr.dst.empty") || "译文为空", true);
				return;
			}
			SK.msg("tr-msg", langlabel(data.src || src) + " → " + langlabel(data.dst || dst));
		}, "tr-msg");
	}

	function trswap(){
		var src = F.id("tr-src").value || "auto";
		var dst = F.id("tr-dst").value || "auto";
		var inn = F.id("tr-in").value || "";
		var out = F.id("tr-out").value || "";
		if (src === "auto" || dst === "auto") {
			var probe = String(out).replace(/^\s+|\s+$/g, "") ? out : inn;
			var a = detectcode(probe);
			var nat = (trLangs && trLangs.auto_native) || "zh";
			var lrn = (trLangs && trLangs.auto_learn) || "en";
			var b = a === nat ? lrn : nat;
			filltrkeep(b);
			filldst(false);
			setsel("tr-dst", a);
		}
		else {
			filltrkeep(dst);
			filldst(false);
			setsel("tr-dst", src);
		}
		if (String(out).replace(/^\s+|\s+$/g, "")) {
			F.id("tr-in").value = out;
			F.id("tr-out").value = inn;
		}
		SK.msg("tr-msg", SK.t("tr.swap.done") || "已交换语言");
	}

	function filltrkeep(code){
		var rows = [];
		var srcs = (trLangs && trLangs.src) || ["auto"];
		var i;
		for (i = 0; i < srcs.length; i++)
			rows.push({ value: srcs[i], label: langlabel(srcs[i]) });
		SK.fillsel(F.id("tr-src"), rows, code);
	}

	function trpaste(){
		if (!navigator.clipboard || !navigator.clipboard.readText) {
			SK.msg("tr-msg", SK.t("tr.popup.clip_empty") || "剪贴板没有文字", true);
			return;
		}
		navigator.clipboard.readText().then(function(clip){
			clip = String(clip || "").replace(/^\s+|\s+$/g, "");
			if (!clip) {
				SK.msg("tr-msg", SK.t("tr.popup.clip_empty") || "剪贴板没有文字", true);
				return;
			}
			F.id("tr-in").value = clip;
			SK.msg("tr-msg", SK.t("pasted") || "已粘贴");
		}, function(){
			SK.msg("tr-msg", SK.t("tr.popup.clip_empty") || "剪贴板没有文字", true);
		});
	}

	function trspeak(side){
		var text = side === "dst" ? (F.id("tr-out").value || "") : (F.id("tr-in").value || "");
		if (!String(text).replace(/^\s+|\s+$/g, "")) {
			SK.msg("tr-msg", side === "dst" ? (SK.t("tr.dst.empty") || "译文为空") : (SK.t("tr.popup.need_src") || "请输入原文"), true);
			return;
		}
		var code = side === "dst" ? (F.id("tr-dst").value || "auto") : (F.id("tr-src").value || "auto");
		if (code === "auto" || !code) code = detectcode(text);
		var lang = BCP[code] || code;
		SK.stopwav();
		SK.post("/api/tts", {
			text: text,
			lang: lang
		}, function(data){
			SK.playwav(data && data.wav_base64, "tr-msg");
		}, "tr-msg");
	}

	SK.loadtr = loadtr;
	SK.filltrlang = filltrlang;
	SK.trdst = filldst;
	SK.trgo = trgo;
	SK.trswap = trswap;
	SK.trpaste = trpaste;
	SK.trspeak = trspeak;
	SK.trmodel = trmodel;
	SK.trsync = trsync;
	SK.trfillmodel = function(){ fillmodel(""); };
	SK.regutil("tr", {
		dispose: function(){
			if (SK.stopwav) SK.stopwav();
		}
	});
})();
