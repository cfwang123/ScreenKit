(function(){
	var ocrPacks = [];
	var ocrPic = null;
	var ocrPicUrl = "";
	var ocrLoading = false;
	var ocrAfter = [];
	var ocrGen = 0;
	function tr(s){ return (SK.tr ? SK.tr(s) : s); }
	function loadoocr(after){
		if (ocrPacks.length) {
			if (after) after();
			return;
		}
		if (after) ocrAfter.push(after);
		if (ocrLoading) return;
		ocrLoading = true;
		SK.get("/api/ocr/models", function(data){
			ocrLoading = false;
			ocrPacks = (data && data.packs) || [];
			var rows = [];
			var i;
			var pickPack = "";
			for (i = 0; i < ocrPacks.length; i++) {
				var one = ocrPacks[i];
				rows.push({ value: one.id, label: one.name || one.id });
				if (one.id === "winocr" && one.models && one.models.length) pickPack = "winocr";
			}
			var cur = (data && data.current) || {};
			if (!pickPack) pickPack = cur.pack || "";
			SK.fillsel(F.id("ocr-pack"), rows, pickPack);
			if (cur.device) F.id("ocr-dev").value = cur.device;
			var langPrefer = "";
			if (pickPack && pickPack === (cur.pack || "")) langPrefer = cur.language || "";
			else if (pickPack === "winocr") langPrefer = ocrwinlang(ocrpack());
			fillocrmodels(langPrefer);
			var wait = ocrAfter;
			ocrAfter = [];
			for (i = 0; i < wait.length; i++) wait[i]();
		}, "ocr-msg", function(){
			ocrLoading = false;
			ocrAfter = [];
		});
	}

	function ocrwinlang(pack){
		var models = (pack && pack.models) || [];
		var tags = ["zh-Hans", "zh-CN", "zh-Hans-CN", "en-US", "en-GB", "en"];
		var i, j;
		for (i = 0; i < tags.length; i++) {
			for (j = 0; j < models.length; j++)
				if (String(models[j].id || "").toLowerCase() === tags[i].toLowerCase())
					return models[j].id;
		}
		return models.length ? (models[0].id || "") : "";
	}

	function ocrpack(){
		var id = F.id("ocr-pack").value;
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
		SK.fillsel(F.id("ocr-model"), rows, pick);
		var win = !pack || pack.engine === "winocr";
		F.id("ocr-dev-lab").style.display = win ? "none" : "";
		var lab = F.q("[data-k]", F.id("ocr-model-lab"));
		if (lab) {
			F.seta(lab, "data-k", win ? "web.f.lang" : "web.f.model");
			lab.textContent = (SK.t && SK.t(F.attr(lab, "data-k"))) || (win ? "语言" : "模型");
		}
	}

	function onocrfile(){
		var file = F.id("ocr-file").files && F.id("ocr-file").files[0];
		if (file) setocrpic(file);
	}

	function onocrpaste(ev){
		if (SK.curtool !== "ocr" && SK.curtool !== "u-ico") return;
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
		F.pd(ev);
		if (SK.curtool === "u-ico") SK.seticopic(file);
		else setocrpic(file);
	}

	function setocrpic(file){
		ocrPic = file;
		if (ocrPicUrl) F.unurl(ocrPicUrl);
		ocrPicUrl = F.url(file);
		var img = F.id("ocr-img");
		img.src = ocrPicUrl;
		img.hidden = false;
		F.id("ocr-out").value = "";
		clearocrmeta();
		loadoocr(ocrgo);
	}

	function clearocrmeta(){
		var meta = F.id("ocr-meta");
		if (!meta) return;
		meta.textContent = "";
		meta.hidden = true;
	}

	function ocrmeta(stat, wallMs){
		if (!stat) return "";
		var inferS = (Math.max(0, Number(stat.infer) || 0) / 1000).toFixed(2);
		var wallS = (Math.max(0, wallMs) / 1000).toFixed(2);
		var loadPart = stat.load > 0 ? " · " + tr("加载") + " " + (stat.load / 1000).toFixed(2) + "s" : "";
		var conf = (Number(stat.score) || 0).toFixed(2);
		var device = stat.device || "";
		var model = stat.model ? " | " + stat.model : "";
		var res = stat.width > 0 && stat.height > 0 ? " | " + stat.width + "×" + stat.height : "";
		return tr("推理") + " " + inferS + "s · " + tr("端到端") + " " + wallS + "s" + loadPart
			+ " | " + tr("置信度") + " " + conf + " | " + device + model + res
			+ " | " + (stat.lines || 0) + " " + tr("行") + " | " + tr("边长") + (stat.limit || 0);
	}

	function ocrgo(){
		var file = ocrPic || (F.id("ocr-file").files && F.id("ocr-file").files[0]);
		if (!file) {
			SK.msg("ocr-msg", "请选择或粘贴图片", true);
			return;
		}
		var pack = ocrpack();
		if (!pack) {
			SK.msg("ocr-msg", "没有可用的识别引擎", true);
			return;
		}
		clearocrmeta();
		var gen = ++ocrGen;
		var wall0 = Date.now();
		SK.msg("ocr-msg", "识别中…");
		SK.readfile(file, function(b64){
			if (gen !== ocrGen) return;
			SK.post("/api/ocr", {
				base64: b64,
				options: {
					"ocr.engine": pack.engine,
					"ocr.pack": pack.id,
					"ocr.language": F.id("ocr-model").value,
					"ocr.device": F.id("ocr-dev").value,
					"data.format": "text",
				},
			}, function(jo){
				if (gen !== ocrGen) return;
				SK.msg("ocr-msg", "");
				var data = jo ? jo.data : "";
				F.id("ocr-out").value = typeof data === "string" ? data : "";
				var meta = F.id("ocr-meta");
				var text = ocrmeta(jo && jo.stat, Date.now() - wall0);
				if (meta) {
					meta.textContent = text;
					meta.hidden = !text;
				}
				if (jo && jo.code === 101)
					SK.msg("ocr-msg", typeof data === "string" && data ? data : "未检测到文字", true);
			}, "ocr-msg", true);
			if (gen === ocrGen) SK.msg("ocr-msg", "识别中…");
		}, "ocr-msg");
	}

	SK.loadoocr = loadoocr;
	SK.ocrpack = ocrpack;
	SK.fillocrmodels = fillocrmodels;
	SK.onocrfile = onocrfile;
	SK.onocrpaste = onocrpaste;
	SK.setocrpic = setocrpic;
	SK.clearocrmeta = clearocrmeta;
	SK.ocrmeta = ocrmeta;
	SK.ocrgo = ocrgo;
})();
