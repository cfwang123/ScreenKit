(function(){
	var asrItems = [];
	var asrRec = null;
	function loadasr(){
		if (asrItems.length) return;
		SK.get("/api/asr/models", function(data){
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
			SK.fillsel(SK.$("asr-eng"), rows, "");
			fillasrmodels();
		}, "asr-msg");
	}

	function fillasrmodels(){
		var win = SK.$("asr-eng").value === "windows";
		var rows = [];
		var i;
		for (i = 0; i < asrItems.length; i++) {
			var it = asrItems[i];
			if (!it || it.streaming) continue;
			var isWin = it.type === "Windows";
			if (isWin !== win) continue;
			rows.push({ value: it.name || "", label: it.name || "" });
		}
		SK.fillsel(SK.$("asr-model"), rows, "");
	}

	function postasr(b64, filename){
		SK.post("/api/asr", {
			base64: b64,
			filename: filename || "",
			model: SK.$("asr-model").value,
			lang: SK.$("asr-lang").value,
		}, function(data){
			SK.$("asr-out").value = (data && data.text) || "";
		}, "asr-msg");
	}

	function asrfile(){
		var file = SK.$("asr-file").files && SK.$("asr-file").files[0];
		if (!file) {
			SK.msg("asr-msg", "请选择音频", true);
			return;
		}
		SK.readfile(file, function(b64){ postasr(b64, file.name); }, "asr-msg");
	}

	function setasr(on){
		var b = SK.$("asr-rec");
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
			SK.msg("asr-msg", "浏览器不能录音", true);
			return;
		}
		navigator.mediaDevices.getUserMedia({ audio: true }).then(function(stream){
			var chunks = [];
			var rec;
			try { rec = new MediaRecorder(stream); }
			catch (e) {
				stream.getTracks().forEach(function(tr){ tr.stop(); });
				SK.msg("asr-msg", "无法开始录音", true);
				return;
			}
			asrRec = rec;
			setasr(true);
			SK.msg("asr-msg", "正在录音");
			rec.ondataavailable = function(ev){
				if (ev.data && ev.data.size) chunks.push(ev.data);
			};
			rec.onstop = function(){
				stream.getTracks().forEach(function(tr){ tr.stop(); });
				asrRec = null;
				setasr(false);
				var blob = new Blob(chunks, { type: rec.mimeType || "audio/webm" });
				SK.readfile(blob, function(b64){ postasr(b64, "rec.webm"); }, "asr-msg");
			};
			rec.start();
		}, function(){
			SK.msg("asr-msg", "无法使用麦克风", true);
		});
	}

	SK.loadasr = loadasr;
	SK.fillasrmodels = fillasrmodels;
	SK.postasr = postasr;
	SK.asrfile = asrfile;
	SK.setasr = setasr;
	SK.asrrec = asrrec;
})();
