(function(){
	var trLangs = null;
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
			return;
		}
		SK.get("/api/translate/langs", function(data){
			trLangs = data && data.src ? data : { src: ["auto"], dst: { auto: ["auto"] } };
			filltrlang();
		}, "tr-msg");
	}

	function langlabel(code){
		if (code === "auto") return SK.t("lang.auto.zhen") || "自动（中英互译）";
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
		var letter = 0;
		var i;
		var ch;
		text = String(text || "");
		for (i = 0; i < text.length; i++) {
			ch = text.charCodeAt(i);
			if (ch >= 0x4E00 && ch <= 0x9FFF) han++;
			else if ((ch >= 65 && ch <= 90) || (ch >= 97 && ch <= 122)) letter++;
		}
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
		SK.post("/api/translate", { text: text, src: src, dst: dst }, function(data){
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
			var b = a === "zh" ? "en" : "zh";
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
		SK.post("/api/tts/speak", {
			text: text,
			lang: lang,
			play: "now"
		}, function(){
			SK.msg("tr-msg", "");
		}, "tr-msg");
	}

	SK.loadtr = loadtr;
	SK.filltrlang = filltrlang;
	SK.trdst = filldst;
	SK.trgo = trgo;
	SK.trswap = trswap;
	SK.trpaste = trpaste;
	SK.trspeak = trspeak;
})();
