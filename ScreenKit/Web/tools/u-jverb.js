(function(){
	var exp = null;
	if (typeof module !== "undefined" && module.exports) exp = module.exports;

	var rtimer = null;
	var lastpaint = null;
	var perline = 2;
	var bound = false;

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
	var PICKORD = ["sp", "1", "2", "3", "ai", "an", "n"];
	var PICKNAME = {
		sp: ["特殊", "Irregular"],
		"1": ["1类动词（五段）", "Group 1 verbs"],
		"2": ["2类动词（一段）", "Group 2 verbs"],
		"3": ["3类动词（する・くる）", "Group 3 verbs"],
		ai: ["い形容词", "i-adjectives"],
		an: ["な形容词", "na-adjectives"],
		n: ["名词", "nouns"]
	};
	// g, 辞书形, 读音, 类, flag, sub, 中文, English
	var PICKS = [
		["sp", "する", "する", "3", "", "suru", "做", "do"],
		["sp", "来る", "くる", "3", "", "kuru", "来", "come"],
		["sp", "行く", "いく", "1", "iku", "", "去 · て＝って", "go · te=tte"],
		["sp", "ある", "ある", "1", "arunai", "", "有 · 否定＝ない", "exist · nai=nai"],
		["sp", "くれる", "くれる", "2", "kure", "", "给 · 命令＝くれ", "give · cmd=kure"],
		["sp", "問う", "とう", "1", "ute", "", "问 · て＝うて", "ask · te=ute"],
		["sp", "請う", "こう", "1", "ute", "", "请求 · て＝うて", "request · te=ute"],
		["sp", "なさる", "なさる", "1", "aru", "", "做 · ます＝います", "do · masu=imasu"],
		["sp", "くださる", "くださる", "1", "aru", "", "给 · ます＝います", "give · masu=imasu"],
		["sp", "いらっしゃる", "いらっしゃる", "1", "aru", "", "在/来/去 · ます＝います", "be/come/go · masu=imasu"],
		["sp", "おっしゃる", "おっしゃる", "1", "aru", "", "说 · ます＝います", "say · masu=imasu"],
		["sp", "ござる", "ござる", "1", "aru", "", "有 · ます＝います", "exist · masu=imasu"],
		["cm", "いる", "いる", "2", "", "", "在", "be"],
		["cm", "要る", "いる", "1", "", "", "需要", "need"],
		["cm", "切る", "きる", "1", "", "", "切", "cut"],
		["cm", "着る", "きる", "2", "", "", "穿", "wear"],
		["cm", "帰る", "かえる", "1", "", "", "回", "return"],
		["cm", "変える", "かえる", "2", "", "", "改变", "change"],
		["cm", "入る", "はいる", "1", "", "", "进", "enter"],
		["cm", "出る", "でる", "2", "", "", "出", "exit"],
		["cm", "走る", "はしる", "1", "", "", "跑", "run"],
		["cm", "知る", "しる", "1", "", "", "知道", "know"],
		["cm", "作る", "つくる", "1", "", "", "做", "make"],
		["cm", "取る", "とる", "1", "", "", "取", "take"],
		["cm", "乗る", "のる", "1", "", "", "乘", "ride"],
		["cm", "座る", "すわる", "1", "", "", "坐", "sit"],
		["cm", "終わる", "おわる", "1", "", "", "结束", "end"],
		["cm", "始まる", "はじまる", "1", "", "", "开始", "begin"],
		["cm", "始める", "はじめる", "2", "", "", "开始", "begin"],
		["cm", "売る", "うる", "1", "", "", "卖", "sell"],
		["cm", "送る", "おくる", "1", "", "", "送", "send"],
		["cm", "分かる", "わかる", "1", "", "", "懂", "understand"],
		["cm", "入れる", "いれる", "2", "", "", "放入", "put in"],
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
		["cm", "見せる", "みせる", "2", "", "", "给看", "show"],
		["ai", "高い", "たかい", "ai", "", "", "高", "high"],
		["ai", "安い", "やすい", "ai", "", "", "便宜", "cheap"],
		["ai", "いい", "いい", "ai", "", "", "好 · 否定＝よくない", "good · nai=yokunai"],
		["ai", "大きい", "おおきい", "ai", "", "", "大", "big"],
		["ai", "小さい", "ちいさい", "ai", "", "", "小", "small"],
		["ai", "新しい", "あたらしい", "ai", "", "", "新", "new"],
		["ai", "暑い", "あつい", "ai", "", "", "热", "hot"],
		["ai", "かわいい", "かわいい", "ai", "", "", "可爱 · 规则变化", "cute · regular"],
		["an", "静か", "しずか", "an", "", "", "安静", "quiet"],
		["an", "便利", "べんり", "an", "", "", "方便", "convenient"],
		["an", "きれい", "きれい", "an", "", "", "漂亮 · 不是い形容词", "pretty · not i-adj"],
		["an", "元気", "げんき", "an", "", "", "精神", "healthy"],
		["an", "有名", "ゆうめい", "an", "", "", "有名", "famous"],
		["an", "好き", "すき", "an", "", "", "喜欢", "like"],
		["nn", "学生", "がくせい", "n", "", "", "学生", "student"],
		["nn", "先生", "せんせい", "n", "", "", "老师", "teacher"],
		["nn", "日本人", "にほんじん", "n", "", "", "日本人", "Japanese"],
		["nn", "本", "ほん", "n", "", "", "书", "book"]
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
		title: "日语词形变化",
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
			+ '<label id="jv-type"><select id="jv-sel">'
			+ '<option value="auto"></option>'
			+ '<option value="1"></option>'
			+ '<option value="2"></option>'
			+ '<option value="3"></option>'
			+ '<option value="ai"></option>'
			+ '<option value="an"></option>'
			+ '<option value="n"></option>'
			+ "</select></label>"
			+ '<label id="jv-word">' + SK.combo("jv-in", "食べる", "辞书形，如 食べる、行く") + "</label>"
			+ "</div>"
			+ '<div id="jv-out"></div>';
	}

	function open(){
		var ens = langcode() !== "zh";
		var go = F.id("box-go");
		if (go) {
			F.seta(go, "data-k", "web.jverb.go");
			go.textContent = tx("web.jverb.go", ens ? "Conjugate" : "变形");
		}
		var hint = F.id("jv-hint");
		if (hint) {
			F.seta(hint, "data-k", "web.jverb.hint");
			hint.textContent = tx("web.jverb.hint", "输入辞书形，或点输入框右侧选择常见词和特殊变化。自动先查日语词典，判断是动词、い形容词、な形容词还是名词；查不到时按词尾猜：る＝2类，する・くる＝3类，い＝い形容词，其余按名词；也可在右侧指定类型。动词表的后部是补助动词（ながら、ておく、やがる 等）、敬体（ましょう、ませんか）、尊敬、谦让、丁重，以及关西、九州、东北、广岛和标准语不同的说法。");
		}
		setlab("jv-word", "web.jverb.word", ens ? "Dictionary form" : "辞书形");
		SK.combofill("jv-in", pickgroups());
		setlab("jv-type", "web.jverb.type", ens ? "Type" : "类型");
		setopt("auto", "web.jverb.auto", ens ? "Auto" : "自动");
		setopt("1", "web.jverb.t1", ens ? "Group 1 (godan)" : "1类（五段）");
		setopt("2", "web.jverb.t2", ens ? "Group 2 (ichidan)" : "2类（一段）");
		setopt("3", "web.jverb.t3", ens ? "Group 3 (suru, kuru)" : "3类（する・くる）");
		setopt("ai", "web.jverb.tai", ens ? "i-adjective" : "い形容词");
		setopt("an", "web.jverb.tan", ens ? "na-adjective" : "な形容词");
		setopt("n", "web.jverb.tn", ens ? "Noun" : "名词");
		var sel = F.id("jv-sel");
		if (sel && !F.attr(sel, "data-bind")) {
			F.seta(sel, "data-bind", "1");
			sel.onchange = ontype;
		}
		var box = F.id("jv-in");
		if (box) {
			F.seta(box, "data-kph", "web.jverb.ph");
			box.placeholder = tx("web.jverb.ph", ens ? "Dictionary form, e.g. 食べる、高い、学生" : "辞书形，如 食べる、高い、学生");
		}
		var out = F.id("jv-out");
		if (out && !F.attr(out, "data-bind")) {
			F.seta(out, "data-bind", "1");
			out.onclick = onout;
		}
		if (!bound) {
			bound = true;
			window.addEventListener("resize", onresize);
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

	// 选了类型就把下拉换成这个类型的示例；回到自动再列全部。
	function ontype(){
		SK.combofill("jv-in", pickgroups());
		run();
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
		if (!rows || !rows.length) { out.innerHTML = ""; lastpaint = null; return; }
		lastpaint = { head: head, name: name, rows: rows };
		perline = 2;
		out.innerHTML = tablehtml(head, name, rows);
		fit(out);
	}

	// 两列排不下就退回一列，尽量不出现横向滚动条。
	function fit(out){
		if (perline === 2 && out.scrollWidth > out.clientWidth + 2 && lastpaint) {
			perline = 1;
			out.innerHTML = tablehtml(lastpaint.head, lastpaint.name, lastpaint.rows);
		}
	}

	function onresize(){
		if (rtimer) clearTimeout(rtimer);
		rtimer = setTimeout(refit, 150);
	}

	function refit(){
		var out = F.id("jv-out");
		if (!out || !lastpaint) return;
		perline = 2;
		out.innerHTML = tablehtml(lastpaint.head, lastpaint.name, lastpaint.rows);
		fit(out);
	}

	function tablehtml(head, name, rows){
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
				if (g) html += '<tr><td class="kv-g" colspan="' + perline * 2 + '">' + esc(g) + "</td></tr>";
			}
			buf.push(rows[i]);
		}
		return html + flushrows(buf, tip) + "</table>";
	}

	function flushrows(buf, tip){
		var html = "";
		var j, k;
		for (j = 0; j < buf.length; j += perline) {
			html += "<tr>";
			for (k = 0; k < perline; k++) {
				html += j + k < buf.length ? cellhtml(buf[j + k], tip) : '<td class="jv-k"></td><td class="jv-v"></td>';
			}
			html += "</tr>";
		}
		return html;
	}

	// 下拉按词性分组：特殊（不规则的）在前，然后 1/2/3 类动词、い形容词、な形容词、名词。
	// 选了类型就只列这一类的示例（不规则的词也按它实际的类归进去）。
	function pickgroups(){
		var lang = langcode();
		var sel = F.id("jv-sel");
		var pick = sel ? sel.value : "auto";
		var groups = [];
		var i, j, p, key, items, gloss, show;
		for (i = 0; i < PICKORD.length; i++) {
			key = PICKORD[i];
			if (pick !== "auto" && pick !== key) continue;
			items = [];
			for (j = 0; j < PICKS.length; j++) {
				p = PICKS[j];
				if (pick === "auto" ? pickkey(p) !== key : p[3] !== key) continue;
				gloss = lang === "zh" ? p[6] : p[7];
				show = p[1];
				if (p[2] && p[2] !== p[1]) show += "  " + p[2];
				if (gloss) show += "  " + gloss;
				items.push({ v: p[1], t: show });
			}
			if (!items.length) continue;
			groups.push({ g: lang === "zh" ? PICKNAME[key][0] : PICKNAME[key][1], items: items });
		}
		return groups;
	}

	function pickkey(p){
		if (p[0] === "sp" && p[3] !== "3") return "sp";
		return p[3];
	}

	function cellhtml(item, tip){
		var forms = String(item[1] || "").split("、");
		var bits = "";
		var i;
		for (i = 0; i < forms.length; i++) {
			if (!forms[i]) continue;
			if (bits) bits += "<br>";
			bits += '<span class="jv-u"><span>' + esc(forms[i]) + "</span>"
				+ '<button type="button" class="jv-spk" data-v="' + esc(forms[i]) + '" title="' + tip + '"><i class="fa-solid fa-volume-high"></i></button></span>';
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
			cmd: "命令形", nasai: "なさい", na: "な（=なさい）", tamae: "給え", stop: "禁止形", vol: "意向形", pot: "可能形",
			pas: "受身形", cau: "使役形", cap: "使役受身", tai: "たい", takunai: "たくない",
			c1: "1类（五段）", c2: "2类（一段）", c3s: "3类（する）", c3k: "3类（くる）",
			iku: "いく",
			gson: "尊敬语", gken: "谦让语", gtei: "丁重语",
			son: "になる", sonm: "になります", sonr: "れる", sonrm: "れます", kud: "ください",
			ken: "する", kenm: "します", itas: "いたす", itasm: "いたします",
			tei: "辞書", teim: "ます",
			bad2: "2类要以る结尾", bad3: "3类是する或くる", bad1: "1类词尾要是う段假名",
			teiru: "ている",
			aux: "补助动词", auxte: "て形＋", auxpol: "敬体",
			ngara: "ながら", tsutsu: "つつ", tsutsuaru: "つつある",
			tagaru: "たがる", tagaranai: "たがらない", yagaru: "やがる",
			sugiru: "すぎる", suginai: "すぎない", yasui: "やすい", nikui: "にくい",
			souda: "そうだ", souni: "そうに・そうな",
			kiru: "きる", kireru: "きれる", au: "あう",
			kakeru: "かける", kakaru: "かかる", sokonau: "そこなう",
			teoku: "ておく", teshimau: "てしまう", temiru: "てみる", tearu: "てある",
			tekuru: "てくる", teiku: "ていく", tekudasai: "てください",
			mashou: "ましょう", mashouka: "ましょうか", masenka: "ませんか",
			ai: "い形容词", an: "な形容词", nn: "名词", badai: "い形容词要以い结尾",
			pol: "丁宁形", polneg: "丁宁否定", polpast: "丁宁过去", polpastneg: "丁宁过去否定",
			adv: "副词形", naru: "なる", sa: "さ", rentai: "连体形",
			darou: "だろう", deshou: "でしょう", tarou: "たろう", dearu: "である",
			nda: "んだ", nanda: "なんだ", tte: "って",
			evid: "传闻・推量", tasouda: "たそうだ", rashii: "らしい", youda: "ようだ",
			mitaida: "みたいだ", kamoshirenai: "かもしれない", hazuda: "はずだ",
			kchigai: "に違いない", ktono: "とのことだ", ktoka: "とか", kktite: "って聞いた",
			kppoi: "っぽい", kge: "げ", kkehai: "気配だ",
			duty: "判断・义务", wake: "わけだ", wakenai: "わけがない", beki: "べきだ",
			bekinai: "べきではない", monoda: "ものだ", kotoda: "ことだ", tsumori: "つもりだ", yotei: "予定だ",
			degree: "程度・倾向", gachi: "がちだ", kimi: "気味だ", ippou: "一方だ", bakari: "ばかりだ",
			tokoro1: "ところだ（即将）", tokoro2: "ところだ（刚刚）", tokoro3: "ところだ（正在）",
			confirm: "疑问・确认", kana: "かな", kashira: "かしら", deshoune: "でしょうね", janai: "んじゃないか",
			reason: "原因・理由", nodakara: "のだから", monode: "もので", okage: "おかげで",
			cop: "丁重・尊敬（だ・です）", degozaru: "でござる", degozaimasu: "でございます",
			degozaimashita: "でございました", degozaimashou: "でございましょう",
			deirassharu: "でいらっしゃる", deirasshaimasu: "でいらっしゃいます",
			dks: "方言・关西", dky: "方言・九州", dth: "方言・东北", dhi: "方言・广岛"
		};
		return {
			dict: "dictionary", masu: "masu", masen: "masen", mashita: "mashita", masend: "masen deshita",
			nai: "nai", nakatta: "nakatta", nakute: "nakute", naide: "naide",
			te: "te", ta: "ta", tara: "tara", ba: "ba",
			cmd: "imperative", nasai: "nasai", na: "na (= nasai)", tamae: "tamae", stop: "prohibitive", vol: "volitional", pot: "potential",
			pas: "passive", cau: "causative", cap: "causative-passive", tai: "tai", takunai: "takunai",
			c1: "group 1 (godan)", c2: "group 2 (ichidan)", c3s: "group 3 (suru)", c3k: "group 3 (kuru)",
			iku: "iku",
			gson: "Honorific", gken: "Humble", gtei: "Deferential",
			son: "ni naru", sonm: "masu", sonr: "reru", sonrm: "remasu", kud: "kudasai",
			ken: "suru", kenm: "shimasu", itas: "itasu", itasm: "itashimasu",
			tei: "plain", teim: "masu",
			bad2: "Group 2 must end with る", bad3: "Group 3 is する or くる", bad1: "Group 1 must end with an u-row kana",
			teiru: "teiru",
			aux: "Auxiliaries", auxte: "te-form +", auxpol: "Polite",
			ngara: "nagara", tsutsu: "tsutsu", tsutsuaru: "tsutsu aru",
			tagaru: "tagaru", tagaranai: "tagaranai", yagaru: "yagaru",
			sugiru: "sugiru", suginai: "suginai", yasui: "yasui", nikui: "nikui",
			souda: "souda", souni: "sou ni / sou na",
			kiru: "kiru", kireru: "kireru", au: "au",
			kakeru: "kakeru", kakaru: "kakaru", sokonau: "sokonau",
			teoku: "teoku", teshimau: "teshimau", temiru: "temiru", tearu: "tearu",
			tekuru: "tekuru", teiku: "teiku", tekudasai: "tekudasai",
			mashou: "mashou", mashouka: "mashou ka", masenka: "masen ka",
			ai: "i-adjective", an: "na-adjective", nn: "noun", badai: "An i-adjective must end with い",
			pol: "polite", polneg: "polite negative", polpast: "polite past", polpastneg: "polite past negative",
			adv: "adverbial", naru: "naru", sa: "sa", rentai: "attributive",
			darou: "darou", deshou: "deshou", tarou: "tarou", dearu: "dearu",
			nda: "nda", nanda: "nanda", tte: "tte",
			evid: "Hearsay / conjecture", tasouda: "ta + souda", rashii: "rashii", youda: "youda",
			mitaida: "mitaida", kamoshirenai: "kamoshirenai", hazuda: "hazuda",
			kchigai: "ni chigai nai", ktono: "to no koto da", ktoka: "toka", kktite: "tte kiita",
			kppoi: "ppoi", kge: "ge", kkehai: "kehai da",
			duty: "Judgement / duty", wake: "wake da", wakenai: "wake ga nai", beki: "beki da",
			bekinai: "beki dewa nai", monoda: "mono da", kotoda: "koto da", tsumori: "tsumori da", yotei: "yotei da",
			degree: "Degree / tendency", gachi: "gachi da", kimi: "gimi da", ippou: "ippou da", bakari: "bakari da",
			tokoro1: "tokoro da (about to)", tokoro2: "tokoro da (just did)", tokoro3: "tokoro da (doing)",
			confirm: "Question / confirmation", kana: "kana", kashira: "kashira", deshoune: "deshou ne", janai: "n ja nai ka",
			reason: "Reason", nodakara: "no dakara", monode: "mono de", okage: "okage de",
			cop: "Deferential / honorific copula", degozaru: "de gozaru", degozaimasu: "de gozaimasu",
			degozaimashita: "de gozaimashita", degozaimashou: "de gozaimashou",
			deirassharu: "de irassharu", deirasshaimasu: "de irasshaimasu",
			dks: "Kansai dialect", dky: "Kyushu dialect", dth: "Tohoku dialect", dhi: "Hiroshima dialect"
		};
	}

	function build(word, kind, flag, sub, lang){
		word = norm(word);
		var L = labs(lang || "zh");
		var rows;
		if (kind === "ai") rows = iadj(word, L);
		else if (kind === "an") rows = naadj(word, L);
		else if (kind === "n") rows = nounrows(word, L);
		else if (kind === "2") rows = ichidan(word, L);
		else if (kind === "3") rows = surukuru(word, sub, L);
		else if (kind === "1") rows = godan(word, flag, L);
		else return { ok: false, err: L.bad1 };
		if (!rows.ok) return rows;
		var name = kind === "2" ? L.c2 : kind === "3" ? (rows.sub === "kuru" ? L.c3k : L.c3s)
			: kind === "1" ? L.c1 : kind === "ai" ? L.ai : kind === "an" ? L.an : L.nn;
		if (flag === "iku" || isIku(word) && kind === "1") name += " · " + L.iku;
		if (kind !== "ai" && kind !== "an" && kind !== "n") rows.rows = rows.rows.concat(dialectrows(word, kind, rows, L));
		return { ok: true, head: word, name: name, rows: rows.rows };
	}

	// い形容词：高くない、高かった。いい（良い）和 かっこいい 的活用按「よ」走。
	function iadj(word, L){
		if (word.length < 2 || last(word) !== "い") return { ok: false, err: L.badai };
		var s = iadjstem(word);
		return { ok: true, plain: word, rows: [
			row(L.dict, word),
			row(L.pol, word + "です"),
			row(L.nai, s + "くない"),
			row(L.polneg, s + "くありません"),
			row(L.ta, s + "かった"),
			row(L.polpast, s + "かったです"),
			row(L.nakatta, s + "くなかった"),
			row(L.polpastneg, s + "くありませんでした"),
			row(L.te, s + "くて、" + s + "くなくて"),
			row(L.adv, s + "く"),
			row(L.ba, s + "ければ、" + s + "くなければ"),
			row(L.tara, s + "かったら、" + s + "くなかったら"),
			row(L.naru, s + "くなる"),
			row(L.souda, irri(word) ? s + "さそうだ" : s + "そうだ"),
			row(L.sugiru, s + "すぎる"),
			row(L.sa, s + "さ"),
			row(L.darou, word + "だろう、" + s + "くないだろう、" + s + "かっただろう、" + s + "くなかっただろう"),
			row(L.deshou, word + "でしょう、" + s + "くないでしょう、" + s + "かったでしょう、" + s + "くなかったでしょう"),
			row(L.tarou, s + "かったろう、" + s + "くなかったろう"),
			row(L.nda, word + "んだ、" + s + "くないんだ"),
			row(L.tte, word + "って、" + s + "くないって")
		].concat(evidrows(L, {
			plain: word, pred: word, drop: word, attrib: word, attribN: word,
			ren: s + "く", ta: s + "かった", stem: s,
			neg: s + "くない", negta: s + "くなかった"
		}), patternrows(L, {
			plain: word, pred: word, drop: word, attrib: word, attribN: word,
			ren: s + "く", ta: s + "かった", stem: s,
			neg: s + "くない", negta: s + "くなかった"
		}, "ai")) };
	}

	function iadjstem(word){
		if (irri(word)) return word === "いい" || word === "良い" || word === "よい" || word === "好い" ? "よ" : word.slice(0, -2) + "よ";
		return word.slice(0, -1);
	}

	// いい・かっこいい 这类词干是「よ」，「そうだ」要变成「よさそうだ」。
	function irri(word){
		if (word === "いい" || word === "良い" || word === "よい" || word === "好い") return true;
		return ends(word, "いい") && word !== "かわいい" && word !== "可愛い";
	}

	// な形容词：静かだ、静かだった。
	function naadj(word, L){
		if (ends(word, "な") || ends(word, "だ")) word = word.slice(0, -1);
		if (!word) return { ok: false, err: L.bad3 };
		return { ok: true, plain: word, rows: [
			row(L.dict, word + "だ"),
			row(L.pol, word + "です"),
			row(L.nai, word + "ではない、" + word + "じゃない"),
			row(L.polneg, word + "ではありません"),
			row(L.ta, word + "だった"),
			row(L.polpast, word + "でした"),
			row(L.nakatta, word + "ではなかった、" + word + "じゃなかった"),
			row(L.polpastneg, word + "ではありませんでした"),
			row(L.te, word + "で、" + word + "ではなくて"),
			row(L.rentai, word + "な、" + word + "でない"),
			row(L.adv, word + "に"),
			row(L.ba, word + "なら、" + word + "でなければ"),
			row(L.tara, word + "だったら、" + word + "ではなかったら"),
			row(L.naru, word + "になる"),
			row(L.dearu, word + "である、" + word + "ではない"),
			row(L.souda, word + "そうだ"),
			row(L.sugiru, word + "すぎる"),
			row(L.sa, word + "さ"),
			row(L.darou, word + "だろう、" + word + "ではないだろう、" + word + "だっただろう、" + word + "ではなかっただろう"),
			row(L.deshou, word + "でしょう、" + word + "ではないでしょう、" + word + "だったでしょう、" + word + "ではなかったでしょう"),
			row(L.tarou, word + "だったろう、" + word + "ではなかったろう"),
			row(L.nanda, word + "なんだ、" + word + "ではないんだ"),
			row(L.tte, word + "だって、" + word + "ではないって")
		].concat(evidrows(L, {
			plain: word, pred: word + "だ", drop: word, attrib: word + "な", attribN: word + "な",
			ren: word + "に", ta: word + "だった", stem: word,
			neg: word + "ではない", negta: word + "ではなかった"
		}), patternrows(L, {
			plain: word, pred: word + "だ", drop: word, attrib: word + "な", attribN: word + "な",
			ren: word + "に", ta: word + "だった", stem: word,
			neg: word + "ではない", negta: word + "ではなかった"
		}, "an"), copularows(L, word)) };
	}

	// 名词：学生だ、学生だった。和な形容词一样接だ・で・だった，连体形用の。
	function nounrows(word, L){
		if (ends(word, "だ")) word = word.slice(0, -1);
		if (!word) return { ok: false, err: L.bad3 };
		return { ok: true, plain: word, rows: [
			row(L.dict, word + "だ"),
			row(L.pol, word + "です"),
			row(L.nai, word + "ではない、" + word + "じゃない"),
			row(L.polneg, word + "ではありません"),
			row(L.ta, word + "だった"),
			row(L.polpast, word + "でした"),
			row(L.nakatta, word + "ではなかった、" + word + "じゃなかった"),
			row(L.polpastneg, word + "ではありませんでした"),
			row(L.te, word + "で、" + word + "ではなくて"),
			row(L.rentai, word + "の、" + word + "でない"),
			row(L.ba, word + "なら、" + word + "でなければ"),
			row(L.tara, word + "だったら、" + word + "ではなかったら"),
			row(L.dearu, word + "である、" + word + "ではない"),
			row(L.darou, word + "だろう、" + word + "ではないだろう、" + word + "だっただろう、" + word + "ではなかっただろう"),
			row(L.deshou, word + "でしょう、" + word + "ではないでしょう、" + word + "だったでしょう、" + word + "ではなかったでしょう"),
			row(L.tarou, word + "だったろう、" + word + "ではなかったろう"),
			row(L.nanda, word + "なんだ、" + word + "ではないんだ"),
			row(L.tte, word + "だって、" + word + "ではないって")
		].concat(evidrows(L, {
			plain: word, pred: word + "だ", drop: word, attrib: word + "な", attribN: word + "の",
			ren: word + "に", ta: word + "だった", stem: word,
			neg: word + "ではない", negta: word + "ではなかった"
		}), patternrows(L, {
			plain: word, pred: word + "だ", drop: word, attrib: word + "な", attribN: word + "の",
			ren: word + "に", ta: word + "だった", stem: word,
			neg: word + "ではない", negta: word + "ではなかった"
		}, "n"), copularows(L, word)) };
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
			row(L.tara, ta + "ら、" + nai.slice(0, -2) + "なかったら"),
			row(L.ba, ba + "、" + nai.slice(0, -1) + "ければ"),
			row(L.cmd, cmd)
		].concat(nasairows(L, plain, masu.slice(0, -2)), [
			row(L.stop, plain + "な"),
			row(L.vol, vol),
			row(L.pot, pot),
			row(L.pas, pas),
			row(L.cau, cau),
			row(L.cap, cap),
			row(L.tai, tai),
			row(L.takunai, tai.slice(0, -2) + "たくない"),
			row(L.nda, plain + "んだ、" + nai + "んだ"),
			row(L.tte, plain + "って、" + nai + "って")
		]);
		return { ok: true, plain: plain, masu: masu, te: te, ta: ta, nai: nai,
			rows: rows.concat(auxrows(L, plain, masu, te), evidrows(L, verbforms(plain, masu, nai, te, ta)),
				patternrows(L, verbforms(plain, masu, nai, te, ta), "v"),
				keigo(plain, masu, pas, L)) };
	}

	// 语气句型用的接续形（动词）。
	function verbforms(plain, masu, nai, te, ta){
		return {
			plain: plain, pred: plain, drop: plain, attrib: plain, attribN: plain,
			ren: masu.slice(0, -2), ta: ta, teiru: te + "いる", stem: masu.slice(0, -2),
			neg: nai, negta: nai.slice(0, -2) + "なかった"
		};
	}

	// 一条语气句型：肯定和否定并列放在同一格里，各带一个发音按钮。
	function pair(L, label, pos, neg, group){
		return [[label, neg ? pos + "、" + neg : pos, group]];
	}

	// 传闻・推量：pred 是普通形现在的说法（な形容词、名词带だ），plain 去掉だ的形，
	// ta 是普通形过去，attrib 是接ようだ・はずだ时的形（な形容词な，名词の），neg／negta 是它们的否定。
	function evidrows(L, k){
		return []
			.concat(pair(L, L.souda, k.pred + "そうだ", k.neg + "そうだ", L.evid))
			.concat(pair(L, L.tasouda, k.ta + "そうだ", k.negta + "そうだ", L.evid))
			.concat(pair(L, L.rashii, k.plain + "らしい", k.neg + "らしい", L.evid))
			.concat(pair(L, L.youda, k.attribN + "ようだ", k.neg + "ようだ", L.evid))
			.concat(pair(L, L.mitaida, k.plain + "みたいだ", k.neg + "みたいだ", L.evid))
			.concat(pair(L, L.kamoshirenai, k.plain + "かもしれない", k.neg + "かもしれない", L.evid))
			.concat(pair(L, L.hazuda, k.attribN + "はずだ", k.neg + "はずだ", L.evid));
	}

	// 判断・义务、程度・倾向、疑问・确认、原因・理由这些句型。
	// k 是这个词性对应的接续形：
	//   plain 普通形、pred 带だ的普通形、drop 去掉だ、attrib 接わけだ等的连体形（な形容词な、名词な）、
	//   attribN 接ようだ等的连体形（名词の）、ren 连用形、ta 过去、stem 词干。
	// 只有动词能接的（べきだ、がちだ 等）和只有形容词能接的（げ）在这里按词性取舍。
	function patternrows(L, k, cat){
		var verb = cat === "v";
		var adj = cat === "ai" || cat === "an";
		var noun = cat === "n";
		var rows = []
			.concat(pair(L, L.kchigai, k.drop + "に違いない", k.neg + "に違いない", L.evid))
			.concat(pair(L, L.ktono, k.pred + "とのことだ", k.neg + "とのことだ", L.evid))
			.concat(pair(L, L.ktoka, k.pred + "とか", k.neg + "とか", L.evid))
			.concat(pair(L, L.kktite, k.pred + "って聞いた", k.neg + "って聞いた", L.evid))
			.concat(pair(L, L.kppoi, k.stem + "っぽい", "", L.evid));
		if (adj) rows = rows.concat(pair(L, L.kge, k.stem + "げ", "", L.evid));
		if (verb) rows = rows.concat(pair(L, L.kkehai, k.plain + "気配だ", k.neg + "気配だ", L.evid));
		rows = rows.concat(
			pair(L, L.wake, k.attrib + "わけだ", k.neg + "わけだ", L.duty),
			pair(L, L.wakenai, k.attrib + "わけがない", k.neg + "わけがない", L.duty));
		if (verb) rows = rows.concat(
			pair(L, L.beki, k.plain + "べきだ", "", L.duty),
			pair(L, L.bekinai, k.plain + "べきではない", "", L.duty));
		if (!noun) rows = rows.concat(pair(L, L.monoda, k.attrib + "ものだ", k.neg + "ものだ", L.duty));
		if (verb) rows = rows.concat(
			pair(L, L.kotoda, k.plain + "ことだ", k.neg + "ことだ", L.duty),
			pair(L, L.tsumori, k.plain + "つもりだ", k.neg + "つもりだ", L.duty),
			pair(L, L.yotei, k.plain + "予定だ", k.neg + "予定だ", L.duty),
			pair(L, L.gachi, k.ren + "がちだ", "", L.degree),
			pair(L, L.kimi, k.ren + "気味だ", "", L.degree),
			pair(L, L.ippou, k.plain + "一方だ", "", L.degree),
			pair(L, L.bakari, k.plain + "ばかりだ", "", L.degree),
			// ところだ 有三种：辞書形＝即将，た形＝刚刚，ている＝正在。
			pair(L, L.tokoro1, k.plain + "ところだ", "", L.degree),
			pair(L, L.tokoro2, k.ta + "ところだ", "", L.degree),
			pair(L, L.tokoro3, k.teiru + "ところだ", "", L.degree));
		rows = rows.concat(
			pair(L, L.kana, k.drop + "かな", k.neg + "かな", L.confirm),
			pair(L, L.kashira, k.drop + "かしら", k.neg + "かしら", L.confirm),
			pair(L, L.deshoune, k.drop + "でしょうね", k.neg + "でしょうね", L.confirm),
			pair(L, L.janai, k.attrib + "んじゃないか", k.neg + "んじゃないか", L.confirm));
		rows = rows.concat(
			pair(L, L.nodakara, k.attrib + "のだから", k.neg + "のだから", L.reason),
			pair(L, L.monode, k.attrib + "もので", k.neg + "もので", L.reason));
		if (verb) rows = rows.concat(pair(L, L.okage, k.ta + "おかげで", "", L.reason));
		if (noun) rows = rows.concat(pair(L, L.okage, k.attribN + "おかげで", "", L.reason));
		return rows;
	}

	// だ・です的敬语说法：丁重的 でござる、尊敬的 でいらっしゃる。な形容词和名词都能接。
	function copularows(L, word){
		return [
			[L.degozaru, word + "でござる", L.cop],
			[L.degozaimasu, word + "でございます、" + word + "ではございません", L.cop],
			[L.degozaimashita, word + "でございました、" + word + "ではございませんでした", L.cop],
			[L.degozaimashou, word + "でございましょう", L.cop],
			[L.deirassharu, word + "でいらっしゃる", L.cop],
			[L.deirasshaimasu, word + "でいらっしゃいます", L.cop]
		];
	}

	// 连用形、て形后面常接的补助动词，和敬体的劝诱・疑问，各成一组。
	// ある・いる 是状态动词，なさる・くださる 本身是敬语，都不接这些。
	function auxrows(L, plain, masu, te){
		if (isAru(plain) || isStateIru(plain) || isSonkeAru(plain)) return [];
		var ren = masu.slice(0, -2);
		var rows = [
			row(L.ngara, ren + "ながら"),
			row(L.tsutsu, ren + "つつ"),
			row(L.tsutsuaru, ren + "つつある"),
			row(L.tagaru, ren + "たがる"),
			row(L.tagaranai, ren + "たがらない"),
			row(L.yagaru, ren + "やがる"),
			row(L.sugiru, ren + "すぎる"),
			row(L.suginai, ren + "すぎない"),
			row(L.yasui, ren + "やすい"),
			row(L.nikui, ren + "にくい"),
			row(L.souda, ren + "そうだ"),
			row(L.souni, ren + "そうに、" + ren + "そうな"),
			row(L.kiru, ren + "きる"),
			row(L.kireru, ren + "きれる"),
			row(L.au, ren + "あう"),
			row(L.kakeru, ren + "かける"),
			row(L.kakaru, ren + "かかる"),
			row(L.sokonau, ren + "そこなう")
		];
		var teerows = [
			row(L.teiru, te + "いる"),
			row(L.teoku, te + "おく"),
			row(L.teshimau, te + "しまう"),
			row(L.temiru, te + "みる"),
			row(L.tearu, te + "ある"),
			row(L.tekuru, te + "くる"),
			row(L.teiku, te + "いく"),
			row(L.tekudasai, te + "ください")
		];
		var polrows = [
			row(L.mashou, ren + "ましょう"),
			row(L.mashouka, ren + "ましょうか"),
			row(L.masenka, ren + "ませんか")
		];
		return setgroup(rows, L.aux).concat(setgroup(teerows, L.auxte), setgroup(polrows, L.auxpol));
	}

	function setgroup(rows, g){
		var i;
		for (i = 0; i < rows.length; i++) rows[i][2] = g;
		return rows;
	}

	function isStateIru(word){
		word = norm(word);
		return word === "居る" || hira(word) === "いる";
	}

	// なさい・給え接连用形（行き＋なさい／行きたまえ）；な是なさい的省略，同样接连用形（行きな），
	// 和禁止形的「辞书形＋な」（行くな）不是一回事。なさる、くださる这类尊敬动词本身已是敬语，不加。
	function nasairows(L, plain, ren){
		if (isSonkeAru(plain)) return [];
		return [row(L.nasai, ren + "なさい"), row(L.na, ren + "な"), row(L.tamae, ren + "たまえ")];
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
		var cmd = isKureru(word) ? stem : stem + "ろ、" + stem + "よ";
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
		if (end === "い") return { kind: "ai", flag: "", sub: "" };
		return { kind: "n", flag: "", sub: "" };
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
		if (s.indexOf("keiyodoshi") >= 0) kind = "an";
		else if (s.indexOf("keiyoushi") >= 0) kind = "ai";
		else if (s.indexOf("ichidan") >= 0) kind = "2";
		else if (s.indexOf("kuru verb") >= 0) { kind = "3"; sub = "kuru"; }
		else if (s.indexOf("suru verb") >= 0 && s.indexOf("takes the aux") < 0) { kind = "3"; sub = "suru"; }
		else if (s.indexOf("godan") >= 0) kind = "1";
		else if (s.indexOf("noun") >= 0) kind = "n";
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
