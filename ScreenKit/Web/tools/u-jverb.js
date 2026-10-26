(function(){
	var exp = null;
	if (typeof module !== "undefined" && module.exports) exp = module.exports;

	var rtimer = null;
	var lastpaint = null;
	var perline = 2;
	var maxcols = 4;
	var tries = 0;
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

	function paint(head, name, rows, tm){
		var out = F.id("jv-out");
		if (!out) return;
		if (!rows || !rows.length) { out.innerHTML = ""; lastpaint = null; return; }
		lastpaint = { head: head, name: name, rows: rows, tm: tm };
		render(out);
	}

	// 页面越宽排得越多：先按一栏量出最宽的一行，够放几栏就排几栏，最多四栏；
	// 排完仍然超宽再逐栏退回，尽量不出现横向滚动条。
	function render(out){
		if (!out || !lastpaint) return;
		perline = 1;
		out.innerHTML = tablehtml(lastpaint.head, lastpaint.name, lastpaint.rows, lastpaint.tm);
		var tab = out.getElementsByTagName("table")[0];
		var w = tab ? tab.offsetWidth : 0;
		var cw = out.clientWidth;
		if (cw <= 0) {
			// 刚切到工具页时容器可能还没显示，等一帧再量。
			if (tries < 5) { tries++; setTimeout(refit, 40); }
			return;
		}
		tries = 0;
		var n = 1;
		if (w > 0) {
			n = Math.floor((cw + 10) / (w + 10));
			if (n > maxcols) n = maxcols;
			if (n < 1) n = 1;
		}
		if (n > 1) {
			perline = n;
			out.innerHTML = tablehtml(lastpaint.head, lastpaint.name, lastpaint.rows, lastpaint.tm);
		}
		while (perline > 1 && out.scrollWidth > out.clientWidth + 2) {
			perline--;
			out.innerHTML = tablehtml(lastpaint.head, lastpaint.name, lastpaint.rows, lastpaint.tm);
		}
	}

	function onresize(){
		if (rtimer) clearTimeout(rtimer);
		rtimer = setTimeout(refit, 150);
	}

	function refit(){
		var out = F.id("jv-out");
		if (out && lastpaint) render(out);
	}

	function tablehtml(head, name, rows, tm){
		var tip = esc(tx("dict.speak", "发音"));
		var html = '<div class="jv-head"><b>' + esc(head) + "</b><span>" + esc(name) + "</span>"
			+ (tm ? '<span class="jv-time">' + esc(tm) + "</span>" : "") + "</div><table class=\"jv-tab\">";
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
		var t0 = now();
		lookup(word, function(res){
			var tdict = now() - t0;
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
			var t1 = now();
			conjreq({ lang: "ja", q: word, kind: use.kind, flag: use.flag, sub: use.sub, ui: langcode() }, function(d){
				var tcalc = now() - t1;
				if (!d) {
					paint("", "", []);
					SK.msg("box-msg", tx("web.jverb.down", "变形接口不可用"), true);
					return;
				}
				if (!d.ok) {
					paint("", "", []);
					SK.msg("box-msg", d.err, true);
					return;
				}
				paint(d.head, d.name, rowsarr(d.rows), timestr(tdict, tcalc));
				SK.msg("box-msg", note || "", bad);
			});
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

	// 变形算在 C# 端（/api/conj），这里只取结果和渲染。
	function conjreq(params, done){
		var qs = [], k;
		for (k in params) if (params[k] !== undefined && params[k] !== null && params[k] !== "")
			qs.push(k + "=" + encodeURIComponent(params[k]));
		getjson("/api/conj?" + qs.join("&"), function(jo){
			done(jo && jo.code === 100 && jo.data ? jo.data : null);
		});
	}

	function rowsarr(rows){
		var out = [], i;
		if (!rows) return out;
		for (i = 0; i < rows.length; i++) out.push([rows[i].l, rows[i].v, rows[i].g || ""]);
		return out;
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

	// い形容词：高くない、高かった。いい（良い）和 かっこいい 的活用按「よ」走。

	// いい・かっこいい 这类词干是「よ」，「そうだ」要变成「よさそうだ」。

	// な形容词：静かだ、静かだった。

	// 名词：学生だ、学生だった。和な形容词一样接だ・で・だった，连体形用の。

	// 语气句型用的接续形（动词）。

	// ない 的派生：なさ（名词化）、なさげ、なさすぎる。都是 ない 去掉い再加。

	// い形容词的方言：关西 高うない／高うて／高うなる，九州（博多）高か／高うなか／高うて。

	// な形容词、名词的方言：关西的断定是 や，否定 やない，过去 やった。

	// 一条语气句型：肯定和否定并列放在同一格里，各带一个发音按钮。

	// 传闻・推量：pred 是普通形现在的说法（な形容词、名词带だ），plain 去掉だ的形，
	// ta 是普通形过去，attrib 是接ようだ・はずだ时的形（な形容词な，名词の），neg／negta 是它们的否定。

	// 判断・义务、程度・倾向、疑问・确认、原因・理由这些句型。
	// k 是这个词性对应的接续形：
	//   plain 普通形、pred 带だ的普通形、drop 去掉だ、attrib 接わけだ等的连体形（な形容词な、名词な）、
	//   attribN 接ようだ等的连体形（名词の）、ren 连用形、ta 过去、stem 词干。
	// 只有动词能接的（べきだ、がちだ 等）和只有形容词能接的（げ）在这里按词性取舍。

	// だ・です的敬语说法：丁重的 でござる、尊敬的 でいらっしゃる。な形容词和名词都能接。

	// 连用形、て形后面常接的补助动词，和敬体的劝诱・疑问，各成一组。
	// ある・いる 是状态动词，なさる・くださる 本身是敬语，都不接这些。

	// なさい・給え接连用形（行き＋なさい／行きたまえ）；な是なさい的省略，同样接连用形（行きな），
	// 和禁止形的「辞书形＋な」（行くな）不是一回事。なさる、くださる这类尊敬动词本身已是敬语，不加。

	// 方言只列和标准语不同的形，按使用人口和知名度的顺序：关西、九州、东北、广岛。
	// 同一方言内部的细节各地不同，这里取最常见的说法。

	// 方言用的未然形：五段是a段（行か），一段是词干（食べ），する是连用形（勉強し），くる是こ。

	// 关西的「ない」。一段で词干只有一拍时（見・寝・出）用「やへん」：見やへん、寝やへん。

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

	// 查词、计算各计一段；不足 1 毫秒显示 <1 ms。
	function timestr(dictms, calcms){
		var a = "";
		if (dictms >= 0) a = tx("web.verb.tdict", "查词") + " " + fmtms(dictms);
		if (calcms >= 0) a += (a ? " · " : "") + tx("web.verb.tcalc", "计算") + " " + fmtms(calcms);
		return a;
	}

	function fmtms(v){
		v = Math.round(v * 10) / 10;
		return v < 1 ? "<1 ms" : Math.round(v) + " ms";
	}

	function now(){
		return window.performance && window.performance.now ? window.performance.now() : Date.now();
	}

	function last(s){ return s.charAt(s.length - 1); }
})();
