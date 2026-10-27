(function(){
	var sessions = {};
	var aimGen = 0;
	var rec = null;
	var hearing = false;
	var voicesHooked = false;
	var speakGen = 0;
	var packs = {};
	var packsReady = false;
	var LANGS = ["zh", "en", "ja", "ko", "es", "fr", "de", "pt", "ru", "it"];
	var SPEECH = {
		zh: "zh-CN", en: "en-US", ja: "ja-JP", ko: "ko-KR", es: "es-ES",
		fr: "fr-FR", de: "de-DE", pt: "pt-BR", ru: "ru-RU", it: "it-IT"
	};

	SK.regutil("u-aim", {
		title: "浏览器小模型",
		go: "运行",
		own: true,
		live: false,
		body: body,
		open: open,
		run: run,
		dispose: aimclose
	});

	function tx(key, fb){
		var s = SK.t ? SK.t(key) : "";
		return s || fb;
	}

	function body(){
		return '<p class="hint" id="aim-hint"></p>'
			+ '<p class="hint" id="aim-pages"><a id="aim-models" href="/chrome" target="_blank" rel="noopener"></a></p>'
			+ '<label id="aim-tasklab"><select id="aim-task">'
			+ '<option value="detect"></option>'
			+ '<option value="translate"></option>'
			+ '<option value="summarize"></option>'
			+ '<option value="rewrite"></option>'
			+ '<option value="proof"></option>'
			+ '<option value="prompt"></option>'
			+ '<option value="speak"></option>'
			+ '<option value="listen"></option>'
			+ "</select></label>"
			+ '<div class="row" id="aim-speak" hidden>'
			+ '<label id="aim-vlanglab"><select id="aim-vlang"></select></label>'
			+ '<label id="aim-voicelab"><select id="aim-voice"></select></label>'
			+ "</div>"
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
		fillvoices();
		loadpacks();
		labels();
		var task = F.id("aim-task");
		if (task) task.onchange = showtask;
		var vlang = F.id("aim-vlang");
		if (vlang) vlang.onchange = function(){ fillvoices(); };
		var syn = window.speechSynthesis;
		if (syn && !voicesHooked) {
			voicesHooked = true;
			syn.addEventListener("voiceschanged", function(){
				if (SK.curtool === "u-aim") fillvoices();
			});
		}
		showtask();
	}

	function labels(){
		var hint = F.id("aim-hint");
		if (hint) hint.textContent = tx("web.aim.hint", "本地小模型做检测、翻译、摘要、改写、校对和提问。朗读和语音识别用浏览器自己的语音，不下载模型。");
		var models = F.id("aim-models");
		if (models) models.textContent = tx("web.aim.models", "查看和安装模型");
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
		setopt("aim-task", "speak", "web.aim.speak", "朗读");
		setopt("aim-task", "listen", "web.aim.listen", "语音识别");
		setlab("aim-vlanglab", "web.f.lang", "语言");
		setlab("aim-voicelab", "web.aim.voice", "声音");
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
		if (tr) tr.hidden = kind !== "translate" && kind !== "listen";
		var dst = F.id("aim-dstlab");
		if (dst) dst.hidden = kind !== "translate";
		if (sum) sum.hidden = kind !== "summarize";
		if (tone) tone.hidden = kind !== "rewrite";
		var speak = F.id("aim-speak");
		if (speak) speak.hidden = kind !== "speak";
		if (kind !== "listen") stophear();
		if (kind !== "speak") {
			if (window.speechSynthesis) window.speechSynthesis.cancel();
			if (SK.stopwav) SK.stopwav();
		}
		paintgo();
	}

	function paintgo(){
		var go = F.id("box-go");
		var task = F.id("aim-task");
		var kind = task ? task.value : "";
		if (!go) return;
		if (kind === "listen" && hearing) {
			F.seta(go, "data-k", "web.aim.listenstop");
			go.textContent = tx("web.aim.listenstop", "停止");
			return;
		}
		if (kind === "listen") {
			F.seta(go, "data-k", "web.aim.listen");
			go.textContent = tx("web.aim.listen", "语音识别");
			return;
		}
		if (kind === "speak") {
			F.seta(go, "data-k", "web.aim.speak");
			go.textContent = tx("web.aim.speak", "朗读");
			return;
		}
		F.seta(go, "data-k", "web.aim.run");
		go.textContent = tx("web.aim.run", "运行");
	}

	function run(){
		var task = F.id("aim-task");
		var kind = task ? task.value : "detect";
		if (kind === "listen") { listen(); return; }
		if (kind === "speak") { speak(); return; }
		var mine = ++aimGen;
		var box = F.id("aim-in");
		var text = box ? String(box.value || "").trim() : "";
		if (!text) {
			SK.msg("aim-msg", tx("web.aim.empty", "先写一段文字"), true);
			return;
		}
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

	function speechlang(){
		var el = F.id("aim-src");
		var code = el ? el.value : "zh";
		return SPEECH[code] || code || "zh-CN";
	}

	function fillvoices(){
		var syn = window.speechSynthesis;
		var sel = F.id("aim-voice");
		if (!syn || !sel) return;
		var list = syn.getVoices() || [];
		fillvlang(list);
		var want = vlang();
		var prev = sel.value;
		var i, v, o;
		sel.innerHTML = "";
		o = F.el("option");
		o.value = "";
		o.textContent = tx("web.aim.voiceauto", "默认");
		sel.appendChild(o);
		for (i = 0; i < list.length; i++) {
			v = list[i];
			if (want && !samelang(v.lang, want)) continue;
			o = F.el("option");
			o.value = v.voiceURI || v.name;
			o.textContent = voicelabel(v);
			sel.appendChild(o);
		}
		sel.value = prev;
		if (sel.value !== prev) sel.selectedIndex = 0;
	}

	function voicelabel(v){
		var text = v.name + (v.lang ? " (" + v.lang + ")" : "");
		if (!chromevoice(v)) return text;
		if (!packsReady && !v.localService) return text;
		var on = !!v.localService || packon(v);
		var tag = on ? tx("inst.installed", "已安装") : tx("inst.missing", "未安装");
		return text + " · " + tag;
	}

	function chromevoice(v){
		if (!v) return false;
		var name = String(v.name || "");
		var uri = String(v.voiceURI || "");
		if (name.indexOf("Google") === 0 || name.indexOf("Chrome OS") === 0) return true;
		return uri.indexOf("Google") >= 0 || uri.indexOf("Chrome OS") >= 0;
	}

	function packon(v){
		var lang = speaklang(v && v.lang);
		if (!lang) return false;
		var s = packs[lang];
		return s === "installed" || s === "partial";
	}

	function speaklang(code){
		var s = String(code || "").replace(/_/g, "-");
		var l = s.toLowerCase();
		if (l === "zh-hk" || l === "yue" || l === "yue-hk") return "yue-HK";
		if (l === "zh" || l === "zh-cn" || l === "zh-hans") return "zh-CN";
		if (l === "zh-tw" || l === "zh-hant") return "zh-TW";
		if (l === "ja" || l === "ja-jp") return "ja-JP";
		if (l === "ko" || l === "ko-kr") return "ko-KR";
		if (l === "en" || l === "en-us") return "en-US";
		if (l === "en-gb") return "en-GB";
		if (l === "de" || l === "de-de") return "de-DE";
		if (l === "fr" || l === "fr-fr") return "fr-FR";
		if (l === "es" || l === "es-es") return "es-ES";
		if (l === "pt" || l === "pt-br") return "pt-BR";
		if (l === "it" || l === "it-it") return "it-IT";
		if (l === "ru" || l === "ru-ru") return "ru-RU";
		var m = /^([a-z]{2})-([a-zA-Z]{2})$/.exec(s);
		if (m) return m[1].toLowerCase() + "-" + m[2].toUpperCase();
		return s;
	}

	function loadpacks(){
		if (!SK.get) return;
		SK.get("/api/chrome/packs", function(data){
			var items = data && data.items ? data.items : [];
			var map = {};
			var i, it, lang;
			for (i = 0; i < items.length; i++) {
				it = items[i];
				if (!it || it.kind !== "tts" || !it.id) continue;
				lang = speaklang(String(it.id).replace(/^tts:/i, ""));
				if (!lang) continue;
				map[lang] = it.state || "";
			}
			packs = map;
			packsReady = true;
			if (SK.curtool === "u-aim") fillvoices();
		}, "", function(){
			packsReady = true;
			if (SK.curtool === "u-aim") fillvoices();
		});
	}

	function vlang(){
		var el = F.id("aim-vlang");
		return el ? el.value : "";
	}

	function fillvlang(list){
		var sel = F.id("aim-vlang");
		if (!sel) return;
		var prev = sel.value;
		var seen = {};
		var codes = [];
		var i, lang;
		for (i = 0; i < list.length; i++) {
			lang = String((list[i] && list[i].lang) || "").replace(/_/g, "-");
			if (!lang || seen[lang]) continue;
			seen[lang] = true;
			codes.push(lang);
		}
		codes.sort(function(a, b){
			var d = langrank(a) - langrank(b);
			if (d) return d;
			return a < b ? -1 : a > b ? 1 : 0;
		});
		var names = null;
		try { names = new Intl.DisplayNames([document.documentElement.lang || "zh"], { type: "language" }); }
		catch (ex) { names = null; }
		sel.innerHTML = "";
		var o = F.el("option");
		o.value = "";
		o.textContent = tx("lang.all", "全部语言");
		sel.appendChild(o);
		for (i = 0; i < codes.length; i++) {
			o = F.el("option");
			o.value = codes[i];
			o.textContent = langtitle(codes[i], names);
			sel.appendChild(o);
		}
		sel.value = prev;
		if (sel.value !== prev) sel.selectedIndex = 0;
	}

	function langrank(code){
		var c = String(code || "").toLowerCase();
		var prefer = ["zh-cn", "zh-tw", "zh-hk", "yue-hk", "en-us", "en-gb", "ja-jp", "ko-kr"];
		var bases = ["zh", "en", "ja", "ko", "yue", "fr", "de", "es", "pt", "ru", "it"];
		var i;
		for (i = 0; i < prefer.length; i++) if (prefer[i] === c) return i;
		var base = c.split("-")[0];
		for (i = 0; i < bases.length; i++) if (bases[i] === base) return 20 + i;
		return 100;
	}

	function langtitle(code, names){
		var name = "";
		try { if (names) name = names.of(code); }
		catch (ex) { name = ""; }
		if (!name || name === code) return code;
		return name;
	}

	function samelang(a, b){
		a = String(a || "").toLowerCase().replace(/_/g, "-");
		b = String(b || "").toLowerCase().replace(/_/g, "-");
		return a === b;
	}

	function speak(){
		var syn = window.speechSynthesis;
		if (!syn || typeof SpeechSynthesisUtterance !== "function") {
			SK.msg("aim-msg", tx("web.aim.novoice", "这个浏览器不能朗读"), true);
			return;
		}
		var box = F.id("aim-in");
		var text = box ? String(box.value || "").trim() : "";
		if (!text) {
			SK.msg("aim-msg", tx("web.aim.empty", "先写一段文字"), true);
			return;
		}
		stophear();
		syn.cancel();
		if (SK.stopwav) SK.stopwav();
		var mine = ++speakGen;
		var out = F.id("box-out");
		if (out) out.value = text;
		var sel = F.id("aim-voice");
		var uri = sel ? sel.value : "";
		var list = syn.getVoices() || [];
		var picked = null;
		var i;
		for (i = 0; i < list.length; i++) {
			if (uri && (list[i].voiceURI === uri || list[i].name === uri)) {
				picked = list[i];
				break;
			}
		}
		if (!picked) {
			for (i = 0; i < list.length; i++) {
				if (list[i].default) { picked = list[i]; break; }
			}
		}
		if (picked && sysvoice(picked)) {
			speakwin(text, picked, mine);
			return;
		}
		if (picked && remotegoogle(picked)) {
			speakchrome(text, picked, mine);
			return;
		}
		var u = new SpeechSynthesisUtterance(text);
		if (picked) {
			u.voice = picked;
			u.lang = picked.lang;
		}
		if (!u.lang) u.lang = vlang() || speechlang();
		u.onend = function(){
			if (mine === speakGen && SK.curtool === "u-aim") SK.msg("aim-msg", "");
		};
		u.onerror = function(ev){
			if (ev && ev.error === "interrupted") return;
			if (SK.curtool !== "u-aim") return;
			SK.msg("aim-msg", tx("web.aim.novoice", "这个浏览器不能朗读"), true);
		};
		SK.msg("aim-msg", tx("web.aim.speaking", "正在朗读"));
		syn.speak(u);
	}

	function sysvoice(v){
		return /^Microsoft\b/i.test(String(v && v.name || ""));
	}

	function remotegoogle(v){
		if (!v || v.localService) return false;
		var name = String(v.name || "");
		var uri = String(v.voiceURI || "");
		if (name.indexOf("Google") === 0 || name.indexOf("Chrome OS") === 0) return true;
		return uri.indexOf("Google") >= 0 || uri.indexOf("Chrome OS") >= 0;
	}

	function speakchrome(text, voice, mine){
		var xhr = new XMLHttpRequest();
		xhr.open("POST", "/api/tts/speak", true);
		F.hdr(xhr, "Content-Type", "application/json;charset=utf-8");
		xhr.onload = function(){
			var jo, err;
			if (mine !== speakGen) return;
			try { jo = F.parse(xhr.responseText); }
			catch (e) {
				SK.msg("aim-msg", "响应不是 JSON", true);
				return;
			}
			if (!jo || jo.code !== 100) {
				err = jo && jo.data;
				SK.msg("aim-msg", typeof err === "string" && err ? err : "失败", true);
				return;
			}
			if (mine === speakGen && SK.curtool === "u-aim") SK.msg("aim-msg", "");
		};
		xhr.onerror = function(){
			if (mine !== speakGen) return;
			SK.msg("aim-msg", "网络错误", true);
		};
		SK.msg("aim-msg", tx("web.aim.speaking", "正在朗读"));
		xhr.send(F.json({
			text: text,
			engine: "chrome",
			lang: voice.lang || vlang() || speechlang()
		}));
	}

	function sysname(v){
		var name = String(v && v.name || "");
		var cut = name.indexOf(" - ");
		if (cut > 0) name = name.substring(0, cut);
		return name.replace(/^\s+|\s+$/g, "");
	}

	function speakwin(text, voice, mine){
		var body = {
			text: text,
			voice: sysname(voice),
			lang: voice.lang || ""
		};
		SK.msg("aim-msg", tx("web.aim.speaking", "正在朗读"));
		posttts(body, "winrt", mine, function(){
			if (mine !== speakGen) return;
			posttts(body, "sapi", mine, null);
		});
	}

	function posttts(body, engine, mine, again){
		var xhr = new XMLHttpRequest();
		xhr.open("POST", "/api/tts", true);
		F.hdr(xhr, "Content-Type", "application/json;charset=utf-8");
		xhr.onload = function(){
			var jo, err;
			if (mine !== speakGen) return;
			try { jo = F.parse(xhr.responseText); }
			catch (e) {
				if (again) { again(); return; }
				SK.msg("aim-msg", "响应不是 JSON", true);
				return;
			}
			if (!jo || jo.code !== 100) {
				if (again) { again(); return; }
				err = jo && jo.data;
				SK.msg("aim-msg", typeof err === "string" && err ? err : "失败", true);
				return;
			}
			if (SK.curtool !== "u-aim") return;
			SK.playwav(jo.data && jo.data.wav_base64, "aim-msg", function(){
				if (mine === speakGen && SK.curtool === "u-aim") SK.msg("aim-msg", "");
			});
		};
		xhr.onerror = function(){
			if (mine !== speakGen) return;
			if (again) { again(); return; }
			SK.msg("aim-msg", "网络错误", true);
		};
		body.engine = engine;
		xhr.send(F.json(body));
	}

	function listen(){
		if (hearing) { stophear(); return; }
		var Ctor = window.SpeechRecognition || window.webkitSpeechRecognition;
		if (!Ctor) {
			SK.msg("aim-msg", tx("web.aim.nohear", "这个浏览器不能语音识别"), true);
			return;
		}
		if (window.speechSynthesis) window.speechSynthesis.cancel();
		var r = new Ctor();
		r.lang = speechlang();
		r.continuous = true;
		r.interimResults = true;
		r.onresult = function(ev){
			if (SK.curtool !== "u-aim") { stophear(); return; }
			var parts = [];
			var i, res;
			for (i = 0; i < ev.results.length; i++) {
				res = ev.results[i];
				if (res && res[0]) parts.push(res[0].transcript);
			}
			var text = parts.join("");
			var out = F.id("box-out");
			var box = F.id("aim-in");
			if (out) out.value = text;
			if (box) box.value = text;
		};
		r.onerror = function(ev){
			var code = ev && ev.error;
			if (code === "aborted" || code === "no-speech") return;
			hearing = false;
			if (SK.curtool !== "u-aim") return;
			SK.msg("aim-msg", code === "not-allowed" ? tx("web.aim.nomic", "没有允许使用麦克风") : tx("web.aim.nohear", "这个浏览器不能语音识别"), true);
			paintgo();
		};
		r.onend = function(){
			hearing = false;
			rec = null;
			if (SK.curtool === "u-aim") paintgo();
		};
		rec = r;
		hearing = true;
		paintgo();
		SK.msg("aim-msg", tx("web.aim.hearing", "正在听"));
		try { r.start(); }
		catch (ex) {
			hearing = false;
			rec = null;
			paintgo();
			SK.msg("aim-msg", tx("web.aim.nohear", "这个浏览器不能语音识别"), true);
		}
	}

	function stophear(){
		hearing = false;
		if (rec) {
			try { rec.stop(); } catch (ex) {}
			rec = null;
		}
		if (SK.curtool === "u-aim") paintgo();
	}

	function aimclose(){
		stophear();
		if (window.speechSynthesis) window.speechSynthesis.cancel();
		if (SK.stopwav) SK.stopwav();
	}

	function prompt(text){
		var ctor = api("LanguageModel");
		if (!ctor) return Promise.reject(new Error(tx("web.aim.none", "这个浏览器没有本地小模型")));
		return need(ctor).then(function(){
			return session("lm", function(){ return ctor.create(withmon({})); });
		}).then(function(s){ return s.prompt(text); });
	}
})();
