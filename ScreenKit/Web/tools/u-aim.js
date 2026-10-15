(function(){
	var sessions = {};
	var aimGen = 0;
	var LANGS = ["zh", "en", "ja", "ko", "es", "fr", "de", "pt", "ru", "it"];

	SK.regutil("u-aim", {
		title: "浏览器小模型",
		go: "运行",
		own: true,
		live: false,
		body: body,
		open: open,
		run: run
	});

	function tx(key, fb){
		var s = SK.t ? SK.t(key) : "";
		return s || fb;
	}

	function body(){
		return '<p class="hint" id="aim-hint"></p>'
			+ '<label id="aim-tasklab"><select id="aim-task">'
			+ '<option value="detect"></option>'
			+ '<option value="translate"></option>'
			+ '<option value="summarize"></option>'
			+ '<option value="rewrite"></option>'
			+ '<option value="proof"></option>'
			+ '<option value="prompt"></option>'
			+ "</select></label>"
			+ '<div class="row" id="aim-tr" hidden>'
			+ '<label id="aim-srclab"><select id="aim-src"></select></label>'
			+ '<label id="aim-dstlab"><select id="aim-dst"></select></label>'
			+ "</div>"
			+ '<label id="aim-sumlab" hidden><select id="aim-sum">'
			+ '<option value="key-points"></option>'
			+ '<option value="tldr"></option>'
			+ '<option value="teaser"></option>'
			+ '<option value="headline"></option>'
			+ "</select></label>"
			+ '<label id="aim-tonelab" hidden><select id="aim-tone">'
			+ '<option value="more-formal"></option>'
			+ '<option value="more-casual"></option>'
			+ '<option value="shorter"></option>'
			+ '<option value="longer"></option>'
			+ "</select></label>"
			+ '<label id="aim-inlab"><textarea id="aim-in" rows="8"></textarea></label>'
			+ '<p id="aim-msg" class="msg"></p>';
	}

	function open(){
		aimGen++;
		var go = F.id("box-go");
		if (go) {
			F.seta(go, "data-k", "web.aim.run");
			go.textContent = tx("web.aim.run", "运行");
		}
		fillangs();
		labels();
		var task = F.id("aim-task");
		if (task) task.onchange = showtask;
		showtask();
	}

	function labels(){
		var hint = F.id("aim-hint");
		if (hint) hint.textContent = tx("web.aim.hint", "用这个 Chrome 里自带的本地小模型。第一次使用时会下载模型。");
		setlab("aim-tasklab", "web.aim.task", "任务");
		setlab("aim-srclab", "web.aim.src", "原文语言");
		setlab("aim-dstlab", "web.aim.dst", "目标语言");
		setlab("aim-sumlab", "web.aim.sumtype", "摘要样式");
		setlab("aim-tonelab", "web.aim.tone", "语气");
		setlab("aim-inlab", "web.aim.in", "输入");
		setopt("aim-task", "detect", "web.aim.detect", "检测语言");
		setopt("aim-task", "translate", "web.aim.translate", "翻译");
		setopt("aim-task", "summarize", "web.aim.summarize", "摘要");
		setopt("aim-task", "rewrite", "web.aim.rewrite", "改写");
		setopt("aim-task", "proof", "web.aim.proof", "校对");
		setopt("aim-task", "prompt", "web.aim.prompt", "自由提问");
		setopt("aim-sum", "key-points", "web.aim.sum.keypoints", "要点");
		setopt("aim-sum", "tldr", "web.aim.sum.tldr", "一句话");
		setopt("aim-sum", "teaser", "web.aim.sum.teaser", "导语");
		setopt("aim-sum", "headline", "web.aim.sum.headline", "标题");
		setopt("aim-tone", "more-formal", "web.aim.tone.formal", "更正式");
		setopt("aim-tone", "more-casual", "web.aim.tone.casual", "更随意");
		setopt("aim-tone", "shorter", "web.aim.tone.shorter", "更短");
		setopt("aim-tone", "longer", "web.aim.tone.longer", "更长");
	}

	function setlab(id, key, fb){
		var el = F.id(id);
		if (!el) return;
		var text = tx(key, fb);
		if (el.childNodes.length && el.childNodes[0].nodeType === 3) el.childNodes[0].nodeValue = text;
		else el.insertBefore(document.createTextNode(text), el.firstChild);
	}

	function setopt(selId, value, key, fb){
		var sel = F.id(selId);
		if (!sel) return;
		var i, o;
		for (i = 0; i < sel.options.length; i++) {
			o = sel.options[i];
			if (o.value === value) o.textContent = tx(key, fb);
		}
	}

	function fillangs(){
		var names = null;
		try { names = new Intl.DisplayNames([document.documentElement.lang || "zh"], { type: "language" }); }
		catch (ex) { names = null; }
		fillsel("aim-src", "en", names);
		fillsel("aim-dst", "zh", names);
	}

	function fillsel(id, cur, names){
		var sel = F.id(id);
		if (!sel) return;
		var prev = sel.value || cur;
		sel.innerHTML = "";
		var i, o, name;
		for (i = 0; i < LANGS.length; i++) {
			o = F.el("option");
			o.value = LANGS[i];
			name = "";
			try { if (names) name = names.of(LANGS[i]); }
			catch (ex) { name = ""; }
			o.textContent = name ? name + " (" + LANGS[i] + ")" : LANGS[i];
			sel.appendChild(o);
		}
		sel.value = prev;
	}

	function showtask(){
		var task = F.id("aim-task");
		var kind = task ? task.value : "detect";
		var tr = F.id("aim-tr");
		var sum = F.id("aim-sumlab");
		var tone = F.id("aim-tonelab");
		if (tr) tr.hidden = kind !== "translate";
		if (sum) sum.hidden = kind !== "summarize";
		if (tone) tone.hidden = kind !== "rewrite";
	}

	function run(){
		var mine = ++aimGen;
		var box = F.id("aim-in");
		var text = box ? String(box.value || "").trim() : "";
		if (!text) {
			SK.msg("aim-msg", tx("web.aim.empty", "先写一段文字"), true);
			return;
		}
		var task = F.id("aim-task");
		var kind = task ? task.value : "detect";
		SK.msg("aim-msg", "");
		var job = null;
		if (kind === "detect") job = detect(text);
		else if (kind === "translate") job = translate(text);
		else if (kind === "summarize") job = summarize(text);
		else if (kind === "rewrite") job = rewrite(text);
		else if (kind === "proof") job = proof(text);
		else job = prompt(text);
		Promise.resolve(job).then(function(out){
			if (mine !== aimGen || SK.curtool !== "u-aim") return;
			var el = F.id("box-out");
			if (el) el.value = out == null ? "" : String(out);
			SK.msg("aim-msg", "");
		}, function(err){
			if (mine !== aimGen || SK.curtool !== "u-aim") return;
			SK.msg("aim-msg", err && err.message ? err.message : tx("web.aim.nokind", "这个浏览器没有这项"), true);
		});
	}

	function api(name){
		return window[name] || null;
	}

	function avail(ctor, opt){
		if (!ctor || typeof ctor.availability !== "function") return Promise.resolve("unavailable");
		try {
			return Promise.resolve(opt ? ctor.availability(opt) : ctor.availability()).catch(function(){
				try { return ctor.availability(); }
				catch (ex) { return "unavailable"; }
			});
		}
		catch (ex) {
			try { return Promise.resolve(ctor.availability()); }
			catch (e2) { return Promise.resolve("unavailable"); }
		}
	}

	function usable(state){
		return state === "available" || state === "readily" || state === "downloadable" || state === "downloading";
	}

	function withmon(opt){
		var o = opt || {};
		o.monitor = function(m){
			if (!m || !m.addEventListener) return;
			m.addEventListener("downloadprogress", function(e){
				var n = e && e.loaded;
				if (n == null) return;
				if (n > 1) n = e.total ? n / e.total : 0;
				SK.msg("aim-msg", tx("web.aim.down", "正在下载模型") + " " + Math.round(n * 100) + "%");
			});
		};
		return o;
	}

	function session(key, factory){
		if (sessions[key]) return Promise.resolve(sessions[key]);
		return factory().then(function(s){
			sessions[key] = s;
			return s;
		});
	}

	function need(ctor){
		return avail(ctor).then(function(state){
			if (!usable(state)) throw new Error(tx("web.aim.nokind", "这个浏览器没有这项"));
		});
	}

	function detect(text){
		var ctor = api("LanguageDetector");
		if (!ctor) return Promise.reject(new Error(tx("web.aim.none", "这个浏览器没有本地小模型")));
		return need(ctor).then(function(){
			return session("detect", function(){ return ctor.create(withmon({})); });
		}).then(function(det){
			return det.detect(text);
		}).then(function(list){
			if (!list || !list.length) return "";
			var lines = [], i, r, name, c;
			for (i = 0; i < list.length; i++) {
				r = list[i] || {};
				name = r.detectedLanguage || r.language || "";
				c = r.confidence;
				lines.push(name + (c != null ? "  " + Math.round(c * 100) + "%" : ""));
			}
			return lines.join("\n");
		});
	}

	function translate(text){
		var ctor = api("Translator");
		if (!ctor) return Promise.reject(new Error(tx("web.aim.nokind", "这个浏览器没有这项")));
		var srcEl = F.id("aim-src");
		var dstEl = F.id("aim-dst");
		var src = srcEl ? srcEl.value : "en";
		var dst = dstEl ? dstEl.value : "zh";
		var opt = { sourceLanguage: src, targetLanguage: dst };
		return avail(ctor, opt).then(function(state){
			if (!usable(state)) throw new Error(tx("web.aim.nokind", "这个浏览器没有这项"));
			return session("tr:" + src + ":" + dst, function(){ return ctor.create(withmon(opt)); });
		}).then(function(tr){ return tr.translate(text); });
	}

	function summarize(text){
		var ctor = api("Summarizer");
		if (!ctor) return Promise.reject(new Error(tx("web.aim.nokind", "这个浏览器没有这项")));
		var sel = F.id("aim-sum");
		var type = sel ? sel.value : "key-points";
		var opt = {
			type: type,
			format: type === "key-points" ? "markdown" : "plain-text",
			length: "medium"
		};
		return avail(ctor, opt).then(function(state){
			if (!usable(state)) throw new Error(tx("web.aim.nokind", "这个浏览器没有这项"));
			return session("sum:" + type, function(){ return ctor.create(withmon(opt)); });
		}).then(function(s){ return s.summarize(text); });
	}

	function rewrite(text){
		var ctor = api("Rewriter");
		if (!ctor) return Promise.reject(new Error(tx("web.aim.nokind", "这个浏览器没有这项")));
		var sel = F.id("aim-tone");
		var tone = sel ? sel.value : "more-formal";
		var opt = {};
		if (tone === "shorter" || tone === "longer") opt.length = tone;
		else opt.tone = tone;
		return avail(ctor, opt).then(function(state){
			if (!usable(state)) throw new Error(tx("web.aim.nokind", "这个浏览器没有这项"));
			return session("rw:" + tone, function(){ return ctor.create(withmon(opt)); });
		}).then(function(s){ return s.rewrite(text); });
	}

	function proof(text){
		var ctor = api("Proofreader");
		if (!ctor) return Promise.reject(new Error(tx("web.aim.nokind", "这个浏览器没有这项")));
		return need(ctor).then(function(){
			return session("proof", function(){ return ctor.create(withmon({})); });
		}).then(function(s){ return s.proofread(text); }).then(prooftext);
	}

	function prooftext(v){
		if (v == null) return "";
		if (typeof v === "string") return v;
		var out = v.correctedInput || v.output || "";
		var list = v.corrections || [];
		var i, c, line;
		for (i = 0; i < list.length; i++) {
			c = list[i] || {};
			line = c.correction || c.replacement || "";
			if (!line) continue;
			if (out) out += "\n";
			out += line;
		}
		if (out) return out;
		try { return JSON.stringify(v); }
		catch (ex) { return String(v); }
	}

	function prompt(text){
		var ctor = api("LanguageModel");
		if (!ctor) return Promise.reject(new Error(tx("web.aim.none", "这个浏览器没有本地小模型")));
		return need(ctor).then(function(){
			return session("lm", function(){ return ctor.create(withmon({})); });
		}).then(function(s){ return s.prompt(text); });
	}
})();
