(function(){
	var exp = null;
	if (typeof module !== "undefined" && module.exports) exp = module.exports;

	var COL = {
		"う": ["わ", "い", "う", "え", "お", "って", "った"],
		"く": ["か", "き", "く", "け", "こ", "いて", "いた"],
		"ぐ": ["が", "ぎ", "ぐ", "げ", "ご", "いで", "いだ"],
		"す": ["さ", "し", "す", "せ", "そ", "して", "した"],
		"つ": ["た", "ち", "つ", "て", "と", "って", "った"],
		"ぬ": ["な", "に", "ぬ", "ね", "の", "んで", "んだ"],
		"ぶ": ["ば", "び", "ぶ", "べ", "ぼ", "んで", "んだ"],
		"む": ["ま", "み", "む", "め", "も", "んで", "んだ"],
		"る": ["ら", "り", "る", "れ", "ろ", "って", "った"]
	};
	var PICKG = {
		sp: ["特殊变化", "Irregular"],
		cm: ["常见动词", "Common"]
	};
	// g, 辞书形, 读音, 类, flag, sub, 中文, English
	var PICKS = [
		["sp", "する", "する", "3", "", "suru", "做", "do"],
		["sp", "来る", "くる", "3", "", "kuru", "来", "come"],
		["sp", "行く", "いく", "1", "iku", "", "去 · て＝って", "go · te=tte"],
		["sp", "ある", "ある", "1", "arunai", "", "有 · 否定＝ない", "exist · nai=nai"],
		["sp", "いる", "いる", "2", "", "", "在 · 一段", "be · ichidan"],
		["sp", "要る", "いる", "1", "", "", "需要 · 五段", "need · godan"],
		["sp", "くれる", "くれる", "2", "kure", "", "给 · 命令＝くれ", "give · cmd=kure"],
		["sp", "問う", "とう", "1", "ute", "", "问 · て＝うて", "ask · te=ute"],
		["sp", "請う", "こう", "1", "ute", "", "请求 · て＝うて", "request · te=ute"],
		["sp", "なさる", "なさる", "1", "aru", "", "做 · ます＝います", "do · masu=imasu"],
		["sp", "くださる", "くださる", "1", "aru", "", "给 · ます＝います", "give · masu=imasu"],
		["sp", "いらっしゃる", "いらっしゃる", "1", "aru", "", "在/来/去 · ます＝います", "be/come/go · masu=imasu"],
		["sp", "おっしゃる", "おっしゃる", "1", "aru", "", "说 · ます＝います", "say · masu=imasu"],
		["sp", "ござる", "ござる", "1", "aru", "", "有 · ます＝います", "exist · masu=imasu"],
		["sp", "切る", "きる", "1", "", "", "切 · 五段", "cut · godan"],
		["sp", "着る", "きる", "2", "", "", "穿 · 一段", "wear · ichidan"],
		["sp", "帰る", "かえる", "1", "", "", "回 · 五段", "return · godan"],
		["sp", "変える", "かえる", "2", "", "", "改变 · 一段", "change · ichidan"],
		["sp", "入る", "はいる", "1", "", "", "进 · 五段", "enter · godan"],
		["sp", "出る", "でる", "2", "", "", "出 · 一段", "exit · ichidan"],
		["sp", "走る", "はしる", "1", "", "", "跑 · 五段", "run · godan"],
		["sp", "知る", "しる", "1", "", "", "知道 · 五段", "know · godan"],
		["sp", "作る", "つくる", "1", "", "", "做 · 五段", "make · godan"],
		["sp", "取る", "とる", "1", "", "", "取 · 五段", "take · godan"],
		["sp", "乗る", "のる", "1", "", "", "乘 · 五段", "ride · godan"],
		["sp", "座る", "すわる", "1", "", "", "坐 · 五段", "sit · godan"],
		["sp", "終わる", "おわる", "1", "", "", "结束 · 五段", "end · godan"],
		["sp", "始まる", "はじまる", "1", "", "", "开始 · 五段", "begin · godan"],
		["sp", "始める", "はじめる", "2", "", "", "开始 · 一段", "begin · ichidan"],
		["sp", "売る", "うる", "1", "", "", "卖 · 五段", "sell · godan"],
		["sp", "送る", "おくる", "1", "", "", "送 · 五段", "send · godan"],
		["sp", "分かる", "わかる", "1", "", "", "懂 · 五段", "understand · godan"],
		["sp", "入れる", "いれる", "2", "", "", "放入 · 一段", "put in · ichidan"],
		["cm", "食べる", "たべる", "2", "", "", "吃", "eat"],
		["cm", "飲む", "のむ", "1", "", "", "喝", "drink"],
		["cm", "見る", "みる", "2", "", "", "看", "see"],
		["cm", "聞く", "きく", "1", "", "", "听", "hear"],
		["cm", "読む", "よむ", "1", "", "", "读", "read"],
		["cm", "書く", "かく", "1", "", "", "写", "write"],
		["cm", "話す", "はなす", "1", "", "", "说", "speak"],
		["cm", "言う", "いう", "1", "", "", "说", "say"],
		["cm", "買う", "かう", "1", "", "", "买", "buy"],
		["cm", "待つ", "まつ", "1", "", "", "等", "wait"],
		["cm", "会う", "あう", "1", "", "", "见", "meet"],
		["cm", "思う", "おもう", "1", "", "", "想", "think"],
		["cm", "できる", "できる", "2", "", "", "能", "can"],
		["cm", "勉強する", "べんきょうする", "3", "", "suru", "学习", "study"],
		["cm", "起きる", "おきる", "2", "", "", "起床", "get up"],
		["cm", "寝る", "ねる", "2", "", "", "睡觉", "sleep"],
		["cm", "教える", "おしえる", "2", "", "", "教", "teach"],
		["cm", "習う", "ならう", "1", "", "", "学", "learn"],
		["cm", "立つ", "たつ", "1", "", "", "站", "stand"],
		["cm", "出す", "だす", "1", "", "", "拿出", "take out"],
		["cm", "死ぬ", "しぬ", "1", "", "", "死", "die"],
		["cm", "泳ぐ", "およぐ", "1", "", "", "游", "swim"],
		["cm", "遊ぶ", "あそぶ", "1", "", "", "玩", "play"],
		["cm", "急ぐ", "いそぐ", "1", "", "", "急", "hurry"],
		["cm", "貸す", "かす", "1", "", "", "借出", "lend"],
		["cm", "借りる", "かりる", "2", "", "", "借入", "borrow"],
		["cm", "開ける", "あける", "2", "", "", "打开", "open"],
		["cm", "閉める", "しめる", "2", "", "", "关上", "close"],
		["cm", "働く", "はたらく", "1", "", "", "工作", "work"],
		["cm", "休む", "やすむ", "1", "", "", "休息", "rest"],
		["cm", "住む", "すむ", "1", "", "", "住", "live"],
		["cm", "呼ぶ", "よぶ", "1", "", "", "叫", "call"],
		["cm", "使う", "つかう", "1", "", "", "用", "use"],
		["cm", "見せる", "みせる", "2", "", "", "给看", "show"]
	];
	var timer = 0;
	var clip = null;
	var gen = 0;

	if (exp) {
		exp.guess = guess;
		exp.build = build;
		exp.poskind = poskind;
		exp.findpre = findpre;
		return;
	}
	if (typeof SK === "undefined" || !SK.regutil) return;

	SK.regutil("u-jverb", {
		title: "日语动词变形",
		go: "变形",
		live: true,
		own: true,
		noGo: true,
		hideCopy: true,
		hideOut: true,
		body: body,
		open: open,
		relang: open,
		run: run
	});

	function tx(key, fb){
		var s = SK.t ? SK.t(key) : "";
		return s || fb;
	}

	function body(){
		return '<p class="hint" id="jv-hint"></p>'
			+ '<div class="row" id="jv-row">'
			+ '<label id="jv-word">' + SK.combo("jv-in", "食べる", "辞书形，如 食べる、行く") + "</label>"
			+ '<label id="jv-type"><select id="jv-sel">'
			+ '<option value="auto"></option>'
			+ '<option value="1"></option>'
			+ '<option value="2"></option>'
			+ '<option value="3"></option>'
			+ "</select></label>"
			+ "</div>"
			+ '<div id="jv-out"></div>';
	}

	function open(){
		var go = F.id("box-go");
		if (go) {
			F.seta(go, "data-k", "web.jverb.go");
			go.textContent = tx("web.jverb.go", "变形");
		}
		var hint = F.id("jv-hint");
		if (hint) {
			F.seta(hint, "data-k", "web.jverb.hint");
			hint.textContent = tx("web.jverb.hint", "输入辞书形，或点输入框右侧选择常见动词和特殊变化。类型默认自动：先查词典；没有则用示例里的类型；再按词尾（る是 2 类，する、くる是 3 类）。いく的て形、た形是って、った。表的后部是补助动词（ながら、やがる、ている 等）、尊敬、谦让、丁重，以及关西、九州、东北、广岛和标准语不同的说法。");
		}
		setlab("jv-word", "web.jverb.word", "动词");
		SK.combofill("jv-in", pickgroups());
		setlab("jv-type", "web.jverb.type", "类型");
		setopt("auto", "web.jverb.auto", "自动");
		setopt("1", "web.jverb.t1", "1类（五段）");
		setopt("2", "web.jverb.t2", "2类（一段）");
		setopt("3", "web.jverb.t3", "3类（する・くる）");
		var box = F.id("jv-in");
		if (box) {
			F.seta(box, "data-kph", "web.jverb.ph");
			box.placeholder = tx("web.jverb.ph", "辞书形，如 食べる、行く");
		}
		var out = F.id("jv-out");
		if (out && !F.attr(out, "data-bind")) {
			F.seta(out, "data-bind", "1");
			out.onclick = onout;
		}
		run();
	}

	function onout(ev){
		var btn = ev.target;
		var out = F.id("jv-out");
		while (btn && btn !== out && btn.tagName !== "BUTTON") btn = btn.parentNode;
		if (!btn || btn === out) return;
		var text = F.attr(btn, "data-v") || "";
		if (text) speak(text);
	}

	function speak(text){
		SK.stopwav();
		if (clip) {
			try { clip.pause(); } catch (e) {}
			clip = null;
		}
		var audio = new Audio("/api/dict/speak?text=" + encodeURIComponent(text) + "&lang=ja");
		clip = audio;
		audio.onerror = function(){
			if (clip === audio) SK.msg("box-msg", tx("web.jverb.nospeak", "无法发音"), true);
		};
		var played = audio.play();
		if (played && played.then) played.then(function(){}, function(){
			if (clip === audio) SK.msg("box-msg", tx("web.jverb.nospeak", "无法发音"), true);
		});
	}

	function paint(head, name, rows){
		var out = F.id("jv-out");
		if (!out) return;
		if (!rows || !rows.length) { out.innerHTML = ""; return; }
		var tip = esc(tx("dict.speak", "发音"));
		var html = '<div class="jv-head"><b>' + esc(head) + "</b><span>" + esc(name) + "</span></div><table class=\"jv-tab\">";
		var g = null;
		var buf = [];
		var i, rg;
		for (i = 0; i < rows.length; i++) {
			rg = rows[i][2] || "";
			if (g === null || rg !== g) {
				html += flushrows(buf, tip);
				buf = [];
				g = rg;
				if (g) html += '<tr><td class="kv-g" colspan="4">' + esc(g) + "</td></tr>";
			}
			buf.push(rows[i]);
		}
		html += flushrows(buf, tip) + "</table>";
		out.innerHTML = html;
	}

	function flushrows(buf, tip){
		var html = "";
		var j;
		for (j = 0; j < buf.length; j += 2) {
			html += "<tr>" + cellhtml(buf[j], tip);
			html += j + 1 < buf.length ? cellhtml(buf[j + 1], tip) : '<td class="jv-k"></td><td class="jv-v"></td>';
			html += "</tr>";
		}
		return html;
	}

	function pickgroups(){
		var lang = langcode();
		var groups = [];
		var cur = null;
		var i, p, name, gloss, show;
		for (i = 0; i < PICKS.length; i++) {
			p = PICKS[i];
			name = lang === "zh" ? PICKG[p[0]][0] : PICKG[p[0]][1];
			if (!cur || cur.g !== name) {
				cur = { g: name, items: [] };
				groups.push(cur);
			}
			gloss = lang === "zh" ? p[6] : p[7];
			show = p[1];
			if (p[2] && p[2] !== p[1]) show += "  " + p[2];
			if (gloss) show += "  " + gloss;
			cur.items.push({ v: p[1], t: show });
		}
		return groups;
	}

	function cellhtml(item, tip){
		var forms = String(item[1] || "").split("、");
		var bits = "";
		var i;
		for (i = 0; i < forms.length; i++) {
			if (!forms[i]) continue;
			if (bits) bits += " ";
			bits += "<span>" + esc(forms[i]) + "</span>"
				+ '<button type="button" class="jv-spk" data-v="' + esc(forms[i]) + '" title="' + tip + '"><i class="fa-solid fa-volume-high"></i></button>';
		}
		return '<td class="jv-k">' + esc(item[0]) + '</td><td class="jv-v">' + bits + "</td>";
	}

	function esc(s){
		return String(s == null ? "" : s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/"/g, "&quot;");
	}

	function setlab(id, key, fb){
		var el = F.id(id);
		if (!el) return;
		var text = tx(key, fb);
		if (el.childNodes.length && el.childNodes[0].nodeType === 3) el.childNodes[0].nodeValue = text;
		else el.insertBefore(document.createTextNode(text), el.firstChild);
	}

	function setopt(value, key, fb){
		var sel = F.id("jv-sel");
		if (!sel) return;
		var i;
		for (i = 0; i < sel.options.length; i++) {
			if (sel.options[i].value === value) sel.options[i].textContent = tx(key, fb);
		}
	}

	function run(){
		if (timer) clearTimeout(timer);
		timer = setTimeout(go, 180);
	}

	function go(){
		timer = 0;
		var n = ++gen;
		var box = F.id("jv-in");
		var sel = F.id("jv-sel");
		var word = norm(box ? box.value : "");
		var pick = sel ? sel.value : "auto";
		if (!word) {
			paint("", "", []);
			SK.msg("box-msg", "");
			return;
		}
		lookup(word, function(res){
			if (n !== gen) return;
			var use = pick === "auto" ? null : { kind: pick, flag: "", sub: "" };
			var note = "";
			var bad = false;
			var pre = findpre(word);
			if (!res || res.state === "down") {
				note = tx("web.jverb.down", pre && pick === "auto" ? "查不到词典，已按示例。" : "查不到词典，已按词尾判断。");
				bad = true;
			}
			else if (res.state === "missing") {
				note = (res.msg || tx("web.jverb.nodict", "未找到词典库 dict3.bin。请在帮助 → 安装功能里安装词典。"))
					+ (pick === "auto" ? tx("web.jverb.fallback", pre ? "已按示例。" : "已按词尾判断。") : "");
				bad = true;
			}
			if (pick === "auto") {
				var hit = res && res.entry;
				var from = hit ? poskind(hit.pos) : null;
				if (from && from.kind) {
					use = from;
					if (isIku(word)) use.flag = "iku";
					if (isAru(word)) use.flag = "arunai";
					if (isUte(word)) use.flag = "ute";
					if (isSonkeAru(word)) use.flag = "aru";
					if (isKureru(word)) use.flag = "kure";
					note = note || tx("web.jverb.fromdict", "词典");
				}
				else {
					use = guess(word);
					if (pre) {
						if (res && res.state === "ok") {
							note = hit ? tx("web.jverb.notverb", "词典里这个词不是动词，已按示例。")
								: tx("web.jverb.miss", "词典里没有这个词，已按示例。");
						}
					}
					else if (res && res.state === "ok") {
						note = hit ? tx("web.jverb.notverb", "词典里这个词不是动词，已按词尾判断。")
							: tx("web.jverb.miss", "词典里没有这个词，已按词尾判断。");
					}
				}
			}
			else {
				use = { kind: pick, flag: "", sub: "" };
				if (pick === "1" && isIku(word)) use.flag = "iku";
				if (pick === "1" && isAru(word)) use.flag = "arunai";
				if (pick === "1" && isUte(word)) use.flag = "ute";
				if (pick === "1" && isSonkeAru(word)) use.flag = "aru";
				if (pick === "2" && isKureru(word)) use.flag = "kure";
				if (pick === "3") use.sub = isKuru(word) ? "kuru" : (pre && pre[5] ? pre[5] : "");
				if (pre && pre[3] === pick && pre[4] && !use.flag) use.flag = pre[4];
				if (!note) note = tx("web.jverb.forced", "按所选类型");
			}
			if (!use) {
				paint("", "", []);
				SK.msg("box-msg", tx("web.jverb.bad", "看不出是动词的辞书形"), true);
				return;
			}
			var built = build(word, use.kind, use.flag, use.sub, langcode());
			if (!built.ok) {
				paint("", "", []);
				SK.msg("box-msg", built.err, true);
				return;
			}
			paint(built.head, built.name, built.rows);
			SK.msg("box-msg", note || "", bad);
		});
	}

	function langcode(){
		var sel = F.id("lang");
		return sel && sel.value ? sel.value : "zh";
	}

	function lookup(word, done){
		getjson("/api/dict?dict=ja&limit=8&q=" + encodeURIComponent(word), function(jo){
			if (!jo) { done({ state: "down" }); return; }
			if (jo.code !== 100) {
				done({
					state: jo.error === "missing" ? "missing" : "down",
					msg: typeof jo.data === "string" ? jo.data : ""
				});
				return;
			}
			var hits = jo.data && jo.data.hits ? jo.data.hits : [];
			var i = 0;
			var entries = [];
			function next(){
				if (i >= hits.length) {
					done({ state: "ok", entry: choose(entries, word) });
					return;
				}
				var id = hits[i] && hits[i].id;
				i++;
				if (!id) { next(); return; }
				getjson("/api/dict/entry?id=" + encodeURIComponent(id), function(ej){
					if (ej && ej.code === 100 && ej.data) entries.push(ej.data);
					next();
				});
			}
			next();
		});
	}

	function getjson(url, ok){
		var xhr = new XMLHttpRequest();
		xhr.open("GET", url, true);
		xhr.onload = function(){
			try { ok(JSON.parse(xhr.responseText)); }
			catch (e) { ok(null); }
		};
		xhr.onerror = function(){ ok(null); };
		xhr.send();
	}

	function choose(entries, word){
		var exact = [];
		var i, e, forms, j, f;
		var h = hira(word);
		for (i = 0; i < entries.length; i++) {
			e = entries[i];
			forms = entryforms(e);
			for (j = 0; j < forms.length; j++) {
				f = forms[j];
				if (f === word || hira(f) === h) { exact.push(e); break; }
			}
		}
		if (!exact.length) return null;
		function first(pred){
			var k;
			for (k = 0; k < exact.length; k++) if (pred(exact[k])) return exact[k];
			return null;
		}
		if (h === "する" || word === "為る") {
			e = first(function(it){ return String(it.pos || "").toLowerCase().indexOf("suru verb") >= 0 && String(it.pos || "").toLowerCase().indexOf("takes the aux") < 0; });
			if (e) return e;
		}
		if (h === "くる" || word === "来る" || word === "來る") {
			e = first(function(it){ return String(it.pos || "").toLowerCase().indexOf("kuru verb") >= 0; });
			if (e) return e;
		}
		if (h === "いく" || isIku(word)) {
			e = first(function(it){ return String(it.pos || "").toLowerCase().indexOf("iku/yuku") >= 0; });
			if (e) return e;
		}
		e = first(function(it){ var k = poskind(it.pos); return k && k.kind; });
		return e || exact[0];
	}

	function entryforms(e){
		var out = [];
		function add(s){
			s = String(s || "").trim();
			if (!s) return;
			var i = s.indexOf("'");
			if (i >= 0) s = s.substring(0, i).trim();
			if (!s) return;
			out.push(s);
		}
		add(e.word);
		var parts = String(e.kanji || "").split(/[,，]/);
		var i;
		for (i = 0; i < parts.length; i++) add(parts[i]);
		add(e.reading);
		add(e.pron);
		return out;
	}

	function labs(lang){
		if (lang === "zh") return {
			dict: "辞書形", masu: "ます形", masen: "ません", mashita: "ました", masend: "ませんでした",
			nai: "ない形", nakatta: "なかった", nakute: "なくて", naide: "ないで",
			te: "て形", ta: "た形", tara: "たら", ba: "ば形",
			cmd: "命令形", nasai: "なさい", na: "な（=なさい）", stop: "禁止形", vol: "意向形", pot: "可能形",
			pas: "受身形", cau: "使役形", cap: "使役受身", tai: "たい", takunai: "たくない",
			c1: "1类（五段）", c2: "2类（一段）", c3s: "3类（する）", c3k: "3类（くる）",
			iku: "いく",
			gson: "尊敬语", gken: "谦让语", gtei: "丁重语",
			son: "になる", sonm: "になります", sonr: "れる", sonrm: "れます", kud: "ください",
			ken: "する", kenm: "します", itas: "いたす", itasm: "いたします",
			tei: "辞書", teim: "ます",
			bad2: "2类要以る结尾", bad3: "3类是する或くる", bad1: "1类词尾要是う段假名",
			teiru: "ている",
			aux: "补助动词", ngara: "ながら", tagaru: "たがる", yagaru: "やがる",
			sugiru: "すぎる", yasui: "やすい", nikui: "にくい", souda: "そうだ", mashou: "ましょう",
			dks: "方言・关西", dky: "方言・九州", dth: "方言・东北", dhi: "方言・广岛"
		};
		return {
			dict: "dictionary", masu: "masu", masen: "masen", mashita: "mashita", masend: "masen deshita",
			nai: "nai", nakatta: "nakatta", nakute: "nakute", naide: "naide",
			te: "te", ta: "ta", tara: "tara", ba: "ba",
			cmd: "imperative", nasai: "nasai", na: "na (= nasai)", stop: "prohibitive", vol: "volitional", pot: "potential",
			pas: "passive", cau: "causative", cap: "causative-passive", tai: "tai", takunai: "takunai",
			c1: "group 1 (godan)", c2: "group 2 (ichidan)", c3s: "group 3 (suru)", c3k: "group 3 (kuru)",
			iku: "iku",
			gson: "Honorific", gken: "Humble", gtei: "Deferential",
			son: "ni naru", sonm: "masu", sonr: "reru", sonrm: "remasu", kud: "kudasai",
			ken: "suru", kenm: "shimasu", itas: "itasu", itasm: "itashimasu",
			tei: "plain", teim: "masu",
			bad2: "Group 2 must end with る", bad3: "Group 3 is する or くる", bad1: "Group 1 must end with an u-row kana",
			teiru: "teiru",
			aux: "Auxiliaries", ngara: "nagara", tagaru: "tagaru", yagaru: "yagaru",
			sugiru: "sugiru", yasui: "yasui", nikui: "nikui", souda: "souda", mashou: "mashou",
			dks: "Kansai dialect", dky: "Kyushu dialect", dth: "Tohoku dialect", dhi: "Hiroshima dialect"
		};
	}

	function build(word, kind, flag, sub, lang){
		word = norm(word);
		var L = labs(lang || "zh");
		var rows;
		if (kind === "2") rows = ichidan(word, L);
		else if (kind === "3") rows = surukuru(word, sub, L);
		else if (kind === "1") rows = godan(word, flag, L);
		else return { ok: false, err: L.bad1 };
		if (!rows.ok) return rows;
		var name = kind === "2" ? L.c2 : kind === "3" ? (rows.sub === "kuru" ? L.c3k : L.c3s) : L.c1;
		if (flag === "iku" || isIku(word) && kind === "1") name += " · " + L.iku;
		rows.rows = rows.rows.concat(dialectrows(word, kind, rows, L));
		return { ok: true, head: word, name: name, rows: rows.rows };
	}

	function row(lab, val){ return [lab, val]; }

	function pack(L, plain, masu, nai, te, ta, ba, cmd, vol, pot, pas, cau, cap){
		var tai = masu.slice(0, -2) + "たい";
		var rows = [
			row(L.dict, plain),
			row(L.masu, masu),
			row(L.masen, masu.slice(0, -2) + "ません"),
			row(L.mashita, masu.slice(0, -2) + "ました"),
			row(L.masend, masu.slice(0, -2) + "ませんでした"),
			row(L.nai, nai),
			row(L.nakatta, nai.slice(0, -2) + "なかった"),
			row(L.nakute, nai.slice(0, -2) + "なくて"),
			row(L.naide, nai.slice(0, -2) + "ないで"),
			row(L.te, te),
			row(L.ta, ta),
			row(L.tara, ta + "ら"),
			row(L.ba, ba),
			row(L.cmd, cmd)
		].concat(nasairows(L, plain, masu.slice(0, -2)), [
			row(L.stop, plain + "な"),
			row(L.vol, vol),
			row(L.pot, pot),
			row(L.pas, pas),
			row(L.cau, cau),
			row(L.cap, cap),
			row(L.tai, tai),
			row(L.takunai, tai.slice(0, -2) + "たくない")
		]);
		return { ok: true, plain: plain, masu: masu, te: te, ta: ta, nai: nai,
			rows: rows.concat(auxrows(L, plain, masu, te), keigo(plain, masu, pas, L)) };
	}

	// 连用形（和て形）后面常接的补助动词，单独一组。ある・いる 是状态动词，不接这些。
	function auxrows(L, plain, masu, te){
		if (isAru(plain) || isStateIru(plain)) return [];
		var ren = masu.slice(0, -2);
		var rows = [
			row(L.ngara, ren + "ながら"),
			row(L.tagaru, ren + "たがる"),
			row(L.yagaru, ren + "やがる"),
			row(L.sugiru, ren + "すぎる"),
			row(L.yasui, ren + "やすい"),
			row(L.nikui, ren + "にくい"),
			row(L.souda, ren + "そうだ"),
			row(L.mashou, ren + "ましょう"),
			row(L.teiru, te + "いる")
		];
		var i;
		for (i = 0; i < rows.length; i++) rows[i][2] = L.aux;
		return rows;
	}

	function isStateIru(word){
		word = norm(word);
		return word === "居る" || hira(word) === "いる";
	}

	// なさい接连用形（行き＋なさい）；な是なさい的省略，同样接连用形（行きな），
	// 和禁止形的「辞书形＋な」（行くな）不是一回事。なさる、くださる这类尊敬动词本身已是敬语，不加。
	function nasairows(L, plain, ren){
		if (isSonkeAru(plain)) return [];
		return [row(L.nasai, ren + "なさい"), row(L.na, ren + "な")];
	}

	// 方言只列和标准语不同的形，按使用人口和知名度的顺序：关西、九州、东北、广岛。
	// 同一方言内部的细节各地不同，这里取最常见的说法。
	function dialectrows(word, kind, std, L){
		if (isSonkeAru(word)) return [];
		var rows = [];
		var ren = std.masu.slice(0, -2);
		var mz = mizen(word, kind, ren);
		var tes = std.te.slice(0, -1);
		var kuru = kind === "3" && isKuru(word);
		var suru = kind === "3" && !kuru;
		var pre = suru ? ren.slice(0, -1) : "";   // 勉強する → 勉強
		var aru = isAru(word);
		var kure = isKureru(word);
		var nai = suru ? pre + "せえへん" : kuru ? "けえへん" : kansainai(mz, kind);
		var n = suru ? pre + "せん" : kuru ? "こん" : mz + "ん";
		// 关西：ない＝へん，ないで＝んといて，尊敬＝はる，命令＝なはれ
		rows.push([L.nai, nai, L.dks]);
		if (!aru) {
			rows.push([L.naide, (suru ? pre + "せ" : kuru ? "こ" : mz) + "んといて", L.dks]);
			rows.push([L.gson, ren + "はる", L.dks]);
			if (!kure) rows.push([L.cmd, ren + "なはれ", L.dks]);
		}
		// 九州（博多）：ない＝ん，ている＝とる，一段とサ変的命令＝れ
		rows.push([L.nai, n, L.dky]);
		if (!aru) rows.push([L.teiru, tes + "とる", L.dky]);
		if (!kure && (kind === "2" || suru)) rows.push([L.cmd, suru ? pre + "せれ" : mz + "れ", L.dky]);
		// 东北：意向＝べ，一段的命令＝れ
		rows.push([L.vol, std.plain + "べ", L.dth]);
		if (!kure && kind === "2") rows.push([L.cmd, mz + "れ", L.dth]);
		// 广岛：ない＝ん，なかった＝んかった，ている＝とる・ちょる，尊敬＝んさる
		rows.push([L.nai, n, L.dhi]);
		rows.push([L.nakatta, n + "かった", L.dhi]);
		if (!aru) {
			rows.push([L.teiru, tes + "とる、" + tes + "ちょる", L.dhi]);
			rows.push([L.gson, ren + "んさる", L.dhi]);
		}
		return rows;
	}

	// 方言用的未然形：五段是a段（行か），一段是词干（食べ），する是连用形（勉強し），くる是こ。
	function mizen(word, kind, ren){
		if (kind === "3") return isKuru(word) ? "こ" : ren;
		if (kind === "2") return word.slice(0, -1);
		var col = COL[last(word)];
		return col ? word.slice(0, -1) + col[0] : "";
	}

	// 关西的「ない」。一段で词干只有一拍时（見・寝・出）用「やへん」：見やへん、寝やへん。
	function kansainai(mz, kind){
		if (kind === "2" && mz.length === 1) return mz + "やへん";
		return mz + "へん";
	}

	function godan(word, flag, L){
		var end = last(word);
		var col = COL[end];
		if (!col) return { ok: false, err: L.bad1 };
		var stem = word.slice(0, -1);
		var a = stem + col[0];
		var i = stem + col[1];
		var e = stem + col[3];
		var o = stem + col[4];
		var te = stem + col[5];
		var ta = stem + col[6];
		var cmd = e;
		if (flag === "iku" || isIku(word)) { te = stem + "って"; ta = stem + "った"; }
		if (flag === "ute" || isUte(word)) { te = stem + "うて"; ta = stem + "うた"; }
		if (flag === "aru" || isSonkeAru(word)) { i = stem + "い"; cmd = stem + "い"; }
		var nai = isAru(word) ? arunai(word) : a + "ない";
		return pack(L, word, i + "ます", nai, te, ta, e + "ば", cmd, o + "う", e + "る", a + "れる", a + "せる", a + "せられる");
	}

	function ichidan(word, L){
		if (last(word) !== "る") return { ok: false, err: L.bad2 };
		var stem = word.slice(0, -1);
		// くれる的命令形除了「くれ」还有「給え」（〜て給え）。
		var cmd = isKureru(word) ? stem + "、給え" : stem + "ろ、" + stem + "よ";
		return pack(L, word, stem + "ます", stem + "ない", stem + "て", stem + "た", stem + "れば",
			cmd, stem + "よう", stem + "られる", stem + "られる", stem + "させる", stem + "させられる");
	}

	function surukuru(word, sub, L){
		if (sub === "kuru" || isKuru(word)) return kuru(word, L);
		if (!isSuru(word)) return { ok: false, err: L.bad3 };
		var stem = word === "為る" || hira(word) === "する" ? "" : word.slice(0, -2);
		var plain = stem + "する";
		var got = pack(L, plain, stem + "します", stem + "しない", stem + "して", stem + "した", stem + "すれば",
			stem + "しろ、" + stem + "せよ", stem + "しよう", stem + "できる", stem + "される", stem + "させる", stem + "させられる");
		got.sub = "suru";
		return got;
	}

	function kuru(word, L){
		var k = word === "来る" || word === "來る";
		var nai = k ? "来ない" : "こない";
		var masu = k ? "来ます" : "きます";
		var te = k ? "来て" : "きて";
		var ta = k ? "来た" : "きた";
		var plain = k ? "来る" : "くる";
		var got = pack(L, plain, masu, nai, te, ta, k ? "来れば" : "くれば",
			k ? "来い" : "こい", k ? "来よう" : "こよう", k ? "来られる" : "こられる",
			k ? "来られる" : "こられる", k ? "来させる" : "こさせる", k ? "来させられる" : "こさせられる");
		got.sub = "kuru";
		return got;
	}

	function arunai(word){
		if (word === "有る" || word === "在る") return "ない";
		return word.slice(0, -2) + "ない";
	}

	function guess(word){
		word = norm(word);
		if (!word) return null;
		var pre = findpre(word);
		if (pre) return { kind: pre[3], flag: pre[4] || "", sub: pre[5] || "" };
		if (isKuru(word)) return { kind: "3", sub: "kuru", flag: "" };
		if (isSuru(word)) return { kind: "3", sub: "suru", flag: "" };
		if (isAru(word)) return { kind: "1", flag: "arunai", sub: "" };
		if (isSonkeAru(word)) return { kind: "1", flag: "aru", sub: "" };
		if (isKureru(word)) return { kind: "2", flag: "kure", sub: "" };
		if (isUte(word)) return { kind: "1", flag: "ute", sub: "" };
		var end = last(word);
		if (end === "る") return { kind: "2", flag: "", sub: "" };
		if (COL[end]) return { kind: "1", flag: isIku(word) ? "iku" : "", sub: "" };
		return null;
	}

	function findpre(word){
		word = norm(word);
		if (!word) return null;
		var h = hira(word);
		var i, p, n = 0, one = null;
		for (i = 0; i < PICKS.length; i++) {
			p = PICKS[i];
			if (p[1] === word) return p;
			if (p[2] === word || p[2] === h) { n++; one = p; }
		}
		return n === 1 ? one : null;
	}

	function poskind(pos){
		var s = String(pos || "").toLowerCase();
		if (!s) return null;
		var flag = "";
		if (s.indexOf("iku/yuku") >= 0) flag = "iku";
		else if (s.indexOf("ending (special") >= 0 && s.indexOf("'u'") >= 0) flag = "ute";
		else if (s.indexOf("-aru special") >= 0) flag = "aru";
		else if (s.indexOf("irregular verb") >= 0) flag = "arunai";
		var kind = "";
		var sub = "";
		if (s.indexOf("ichidan") >= 0) kind = "2";
		else if (s.indexOf("kuru verb") >= 0) { kind = "3"; sub = "kuru"; }
		else if (s.indexOf("suru verb") >= 0 && s.indexOf("takes the aux") < 0) { kind = "3"; sub = "suru"; }
		else if (s.indexOf("godan") >= 0) kind = "1";
		if (!kind) return null;
		return { kind: kind, flag: flag, sub: sub };
	}

	function isKuru(word){
		word = norm(word);
		return hira(word) === "くる" || word === "来る" || word === "來る";
	}

	function isSuru(word){
		word = norm(word);
		return hira(word) === "する" || word === "為る" || hira(word).slice(-2) === "する";
	}

	function isIku(word){
		word = norm(word);
		if (hira(word).slice(-2) === "いく") return true;
		return word.slice(-2) === "行く" || word.slice(-2) === "逝く" || word.slice(-2) === "往く";
	}

	function isAru(word){
		word = norm(word);
		return word === "有る" || word === "在る" || hira(word).slice(-2) === "ある";
	}

	function norm(s){
		return hira(String(s || "").replace(/\s+/g, ""));
	}

	function hira(s){
		var out = "";
		var i, c;
		for (i = 0; i < s.length; i++) {
			c = s.charCodeAt(i);
			if (c >= 0x30A1 && c <= 0x30F6) out += String.fromCharCode(c - 0x60);
			else out += s.charAt(i);
		}
		return out;
	}

	function isKureru(word){
		word = norm(word);
		return word === "くれる" || word === "呉れる" || hira(word) === "くれる";
	}

	function isUte(word){
		word = norm(word);
		var h = hira(word);
		if (word === "問う" || word === "請う" || word === "乞う" || word === "恋う" || word === "厭う" || word === "訪う") return true;
		return h === "とう" || h === "こう";
	}

	function isSonkeAru(word){
		word = norm(word);
		var h = hira(word);
		if (h === "なさる" || h === "くださる" || h === "いらっしゃる" || h === "おっしゃる" || h === "ござる") return true;
		return word === "為さる" || word === "下さる";
	}

	function keigo(word, masu, pas, L){
		if (isSonkeAru(word)) return [];
		var ren = masu.slice(0, -2);
		var spec = lexOf(word);
		var prod = prodOf(word, ren);
		var blockS = spec && (spec.only || spec.onlySon);
		var blockK = spec && (spec.only || spec.onlyKen);
		var son = spec && spec.son ? spec.son : "";
		var ken = spec && spec.ken ? spec.ken : "";
		if (!blockS) son = joinuniq(son, prod.son);
		if (!blockK) ken = joinuniq(ken, prod.ken);
		var itas = blockK ? "" : (prod.itas || "");
		if (itas && ken.indexOf(itas) >= 0) itas = "";
		var tei = spec && spec.tei ? spec.tei : "";
		if (tei && tei === ken) tei = "";
		var kud = (spec && spec.kud) || (!blockS ? prod.kud : "") || "";
		var rows = [];
		pushk(rows, L.son, son, L.gson);
		pushm(rows, L.sonm, son, L.gson);
		if (pas) {
			pushk(rows, L.sonr, pas, L.gson);
			pushm(rows, L.sonrm, pas, L.gson);
		}
		pushk(rows, L.kud, kud, L.gson);
		pushk(rows, L.ken, ken, L.gken);
		pushm(rows, L.kenm, ken, L.gken);
		pushk(rows, L.itas, itas, L.gken);
		pushm(rows, L.itasm, itas, L.gken);
		pushk(rows, L.tei, tei, L.gtei);
		pushm(rows, L.teim, tei, L.gtei);
		return rows;
	}

	function pushk(rows, lab, val, g){
		if (!val) return;
		rows.push([lab, val, g]);
	}

	function pushm(rows, lab, val, g){
		var m = mapmasu(val);
		if (!m || m === val) return;
		rows.push([lab, m, g]);
	}

	function prodOf(word, ren){
		var empty = { son: "", ken: "", itas: "", kud: "" };
		var h = hira(word);
		if (isSonkeAru(word) || isKureru(word)) return empty;
		if (h === "する" || word === "為る" || isKuru(word)) return empty;
		if (h === "いく" || word === "行く" || word === "逝く" || word === "往く") return empty;
		if (h === "いう" || word === "言う" || word === "云う") return empty;
		if (h === "いる" || word === "居る") return empty;
		if (h === "みる" || word === "見る" || word === "観る") return empty;
		if (isIku(word)) return empty;
		if (isSuru(word)) {
			var pre = word.slice(0, -2);
			if (!pre) return empty;
			if (kanjiOnly(pre)) return {
				son: "ご" + pre + "になる",
				ken: "ご" + pre + "する",
				itas: pre + "いたす",
				kud: pre + "なさってください"
			};
			return {
				son: "お" + pre + "になる",
				ken: "お" + pre + "する",
				itas: pre + "いたす",
				kud: "お" + pre + "ください"
			};
		}
		if (!ren || ren.length < 2) return empty;
		return {
			son: "お" + ren + "になる",
			ken: "お" + ren + "する",
			itas: "お" + ren + "いたす",
			kud: "お" + ren + "ください"
		};
	}

	function lexOf(word){
		var k = keyOf(word);
		if (!k) return null;
		var t = {
			"する": { son: "なさる", ken: "いたす", tei: "いたす", kud: "なさってください", only: true },
			"くる": { son: "いらっしゃる、おいでになる", ken: "参る、伺う", tei: "参る", kud: "いらしてください、お越しください", only: true },
			"いく": { son: "いらっしゃる、おいでになる", ken: "参る、伺う", tei: "参る", kud: "いらしてください", only: true },
			"いる": { son: "いらっしゃる、おいでになる", ken: "おる", tei: "おる", kud: "いらしてください", only: true },
			"みる": { son: "ご覧になる", ken: "拝見する", kud: "ご覧ください", only: true },
			"いう": { son: "おっしゃる", ken: "申す、申し上げる", tei: "申す", kud: "おっしゃってください", only: true },
			"たべる": { son: "召し上がる", ken: "いただく", tei: "いただく", kud: "召し上がってください", onlyKen: true },
			"のむ": { son: "召し上がる", ken: "いただく", tei: "いただく", kud: "召し上がってください", onlyKen: true },
			"くれる": { son: "くださる", kud: "ください", only: true },
			"あげる": { ken: "差し上げる", onlyKen: true },
			"もらう": { ken: "いただく、頂戴する", tei: "いただく", onlyKen: true },
			"しる": { son: "ご存じです", ken: "存じる、存じ上げる", tei: "存じる", kud: "ご存じください" },
			"おもう": { ken: "存じる", tei: "存じる", onlyKen: true },
			"きく": { ken: "伺う" },
			"あう": { ken: "お目にかかる", onlyKen: true },
			"kiru": { son: "お召しになる", kud: "お召しください", onlySon: true },
			"ねる": { son: "お休みになる", kud: "お休みください", onlySon: true },
			"かりる": { ken: "拝借する", onlyKen: true },
			"よむ": { ken: "拝読する", onlyKen: true },
			"ある": { tei: "ござる", only: true },
			"みせる": { ken: "お目にかける", onlyKen: true },
			"できる": { son: "おできになる", kud: "おできください", only: true },
			"しぬ": { son: "お亡くなりになる", kud: "お亡くなりください", only: true }
		};
		return t[k] || null;
	}

	function keyOf(word){
		word = norm(word);
		var h = hira(word);
		var alias = {
			"為る": "する", "来る": "くる", "來る": "くる", "行く": "いく", "逝く": "いく", "往く": "いく",
			"居る": "いる", "見る": "みる", "観る": "みる", "言う": "いう", "云う": "いう",
			"食べる": "たべる", "飲む": "のむ", "呉れる": "くれる", "貰う": "もらう",
			"知る": "しる", "思う": "おもう", "聞く": "きく", "訊く": "きく", "会う": "あう",
			"着る": "kiru", "寝る": "ねる", "借りる": "かりる", "読む": "よむ",
			"有る": "ある", "在る": "ある", "見せる": "みせる", "出来る": "できる", "死ぬ": "しぬ"
		};
		if (alias[word]) return alias[word];
		var kana = {
			"する": "する", "くる": "くる", "いく": "いく", "いる": "いる", "みる": "みる", "いう": "いう",
			"たべる": "たべる", "のむ": "のむ", "くれる": "くれる", "あげる": "あげる", "もらう": "もらう",
			"しる": "しる", "おもう": "おもう", "きく": "きく", "あう": "あう", "ねる": "ねる",
			"かりる": "かりる", "よむ": "よむ", "ある": "ある", "みせる": "みせる", "できる": "できる", "しぬ": "しぬ"
		};
		return kana[h] || "";
	}

	function joinuniq(a, b){
		var parts = [];
		function add(s){
			if (!s) return;
			var i, p = String(s).split("、");
			for (i = 0; i < p.length; i++) {
				if (p[i] && parts.indexOf(p[i]) < 0) parts.push(p[i]);
			}
		}
		add(a);
		add(b);
		return parts.join("、");
	}

	function mapmasu(s){
		if (!s) return "";
		var parts = String(s).split("、");
		var out = [];
		var i, m;
		for (i = 0; i < parts.length; i++) {
			m = masuOne(parts[i]);
			if (m && out.indexOf(m) < 0) out.push(m);
		}
		return out.join("、");
	}

	function masuOne(s){
		var h = hira(s);
		if (ends(s, "です") || ends(h, "です")) return s;
		if (ends(h, "いらっしゃる") || ends(s, "いらっしゃる")) return s.slice(0, -1) + "います";
		if (ends(h, "おっしゃる") || ends(s, "おっしゃる")) return s.slice(0, -1) + "います";
		if (ends(h, "くださる") || ends(s, "くださる") || ends(s, "下さる")) return s.slice(0, -1) + "います";
		if (ends(h, "ござる") || ends(s, "ござる")) return s.slice(0, -1) + "います";
		if (ends(h, "なさる") || ends(s, "なさる") || ends(s, "為さる")) return s.slice(0, -1) + "います";
		if (ends(s, "になる") || ends(h, "になる")) return s.slice(0, -3) + "になります";
		if (ends(s, "する") || ends(h, "する")) return s.slice(0, -2) + "します";
		if (ends(s, "いたす") || ends(h, "いたす")) return s.slice(0, -3) + "いたします";
		if (ends(s, "られる") || ends(s, "れる") || ends(h, "られる") || ends(h, "れる")) return s.slice(0, -1) + "ます";
		if (ends(s, "上げる") || ends(s, "存じる") || ends(s, "かける")) return s.slice(0, -1) + "ます";
		if (ends(h, "あげる") || ends(h, "ぞんじる") || ends(h, "かける")) return s.slice(0, -1) + "ます";
		return godanMasu(s);
	}

	function godanMasu(s){
		var end = last(s);
		var m = { "う": "い", "く": "き", "ぐ": "ぎ", "す": "し", "つ": "ち", "ぬ": "に", "ぶ": "び", "む": "み", "る": "り" };
		if (!m[end]) return "";
		return s.slice(0, -1) + m[end] + "ます";
	}

	function kanjiOnly(s){
		var i, c;
		if (!s) return false;
		for (i = 0; i < s.length; i++) {
			c = s.charCodeAt(i);
			if (c < 0x4E00 || c > 0x9FFF) return false;
		}
		return true;
	}

	function ends(s, suf){
		return s.length >= suf.length && s.slice(-suf.length) === suf;
	}

	function last(s){ return s.charAt(s.length - 1); }
})();
