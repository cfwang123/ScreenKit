(function(){
	var ocrPacks = [];
	var ocrPic = null;
	var ocrPicUrl = "";
	var ocrLoading = false;
	var ocrAfter = [];
	var ocrGen = 0;
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
			for (i = 0; i < ocrPacks.length; i++)
				rows.push({ value: ocrPacks[i].id, label: ocrPacks[i].name || ocrPacks[i].id });
			var cur = (data && data.current) || {};
			SK.fillsel(SK.$("ocr-pack"), rows, cur.pack || "");
			if (cur.device) SK.$("ocr-dev").value = cur.device;
			fillocrmodels(cur.language || "");
			var wait = ocrAfter;
			ocrAfter = [];
			for (i = 0; i < wait.length; i++) wait[i]();
		}, "ocr-msg", function(){
			ocrLoading = false;
			ocrAfter = [];
		});
	}

	function ocrpack(){
		var id = SK.$("ocr-pack").value;
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
		SK.fillsel(SK.$("ocr-model"), rows, pick);
		var win = !pack || pack.engine === "winocr";
		SK.$("ocr-dev-lab").style.display = win ? "none" : "";
		SK.$("ocr-model-lab").firstChild.nodeValue = win ? "语言" : "模型";
	}

	function onocrfile(){
		var file = SK.$("ocr-file").files && SK.$("ocr-file").files[0];
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
		ev.preventDefault();
		if (SK.curtool === "u-ico") SK.seticopic(file);
		else setocrpic(file);
	}

	function setocrpic(file){
		ocrPic = file;
		if (ocrPicUrl) URL.revokeObjectURL(ocrPicUrl);
		ocrPicUrl = URL.createObjectURL(file);
		var img = SK.$("ocr-img");
		img.src = ocrPicUrl;
		img.hidden = false;
		SK.$("ocr-out").value = "";
		clearocrmeta();
		loadoocr(ocrgo);
	}

	function clearocrmeta(){
		var meta = SK.$("ocr-meta");
		if (!meta) return;
		meta.textContent = "";
		meta.hidden = true;
	}

	function ocrmeta(stat, wallMs){
		if (!stat) return "";
		var inferS = (Math.max(0, Number(stat.infer) || 0) / 1000).toFixed(2);
		var wallS = (Math.max(0, wallMs) / 1000).toFixed(2);
		var loadPart = stat.load > 0 ? " · 加载 " + (stat.load / 1000).toFixed(2) + "s" : "";
		var conf = (Number(stat.score) || 0).toFixed(2);
		var device = stat.device || "";
		var model = stat.model ? " | " + stat.model : "";
		var res = stat.width > 0 && stat.height > 0 ? " | " + stat.width + "×" + stat.height : "";
		return "推理 " + inferS + "s · 端到端 " + wallS + "s" + loadPart
			+ " | 置信度 " + conf + " | " + device + model + res
			+ " | " + (stat.lines || 0) + " 行 | 边长" + (stat.limit || 0);
	}

	function ocrgo(){
		var file = ocrPic || (SK.$("ocr-file").files && SK.$("ocr-file").files[0]);
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
					"ocr.language": SK.$("ocr-model").value,
					"ocr.device": SK.$("ocr-dev").value,
					"data.format": "text",
				},
			}, function(jo){
				if (gen !== ocrGen) return;
				SK.msg("ocr-msg", "");
				var data = jo ? jo.data : "";
				SK.$("ocr-out").value = typeof data === "string" ? data : "";
				var meta = SK.$("ocr-meta");
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
