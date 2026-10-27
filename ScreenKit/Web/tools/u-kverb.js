(function(){
	var exp = null;
	if (typeof module !== "undefined" && module.exports) exp = module.exports;

	var timer = 0, gen = 0;

	var CHO = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
	var JUNG = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";
	var JONG = ["", "ㄱ", "ㄲ", "ㄳ", "ㄴ", "ㄵ", "ㄶ", "ㄷ", "ㄹ", "ㄺ", "ㄻ", "ㄼ", "ㄽ", "ㄾ", "ㄿ", "ㅀ", "ㅁ", "ㅂ", "ㅄ", "ㅅ", "ㅆ", "ㅇ", "ㅈ", "ㅊ", "ㅋ", "ㅌ", "ㅍ", "ㅎ"];
	var A = vi("ㅏ"), AE = vi("ㅐ"), YA = vi("ㅑ"), YAE = vi("ㅒ"), EO = vi("ㅓ"), E = vi("ㅔ");
	var YEO = vi("ㅕ"), YE = vi("ㅖ"), O = vi("ㅗ"), WA = vi("ㅘ"), WAE = vi("ㅙ"), OE = vi("ㅚ");
	var YO = vi("ㅛ"), U = vi("ㅜ"), WEO = vi("ㅝ"), EU = vi("ㅡ"), I = vi("ㅣ");
	var IEUNG = ci("ㅇ"), HIEUT = ci("ㅎ"), RIEULC = ci("ㄹ"), SIOT = ci("ㅅ"), MIEUM = ci("ㅁ");
	var PIEUPC = ci("ㅍ"), NIEUNC = ci("ㄴ"), TTIEUT = ci("ㄸ"), CHIEUT = ci("ㅊ");
	var J_R = zi("ㄹ"), J_B = zi("ㅂ"), J_N = zi("ㄴ"), J_M = zi("ㅁ"), J_SS = zi("ㅆ"), J_H = zi("ㅎ"), J_BS = zi("ㅄ");
;
;
;
;
;
;
;
;
	// 形态被动没有统一规则，只列常见动词的固定说法
;
	// -아/어 있다 只对少数表示结果状态的动词自然
;
;
;
;
	var PICKG = {
		v: ["动词", "동사", "Verbs"],
		a: ["形容词", "형용사", "Adjectives"],
		n: ["名词", "명사", "Nouns"]
	};
	var PICKS = [
		["v", "먹다", "吃", "", "eat"],
		["v", "가다", "去", "", "go"],
		["v", "오다", "来", "", "come"],
		["v", "하다", "做", "", "do"],
		["v", "되다", "成为", "", "become"],
		["v", "보다", "看", "", "see"],
		["v", "주다", "给", "", "give"],
		["v", "사다", "买", "", "buy"],
		["v", "만나다", "见面", "", "meet"],
		["v", "읽다", "读", "", "read"],
		["v", "알다", "知道", "", "know"],
		["v", "살다", "住", "", "live"],
		["v", "만들다", "制作", "", "make"],
		["v", "마시다", "喝", "", "drink"],
		["v", "배우다", "学习", "", "learn"],
		["v", "기다리다", "等待", "", "wait"],
		["v", "공부하다", "学习", "하다", "study"],
		["v", "좋아하다", "喜欢", "하다", "like"],
		["v", "일하다", "工作", "하다", "work"],
		["v", "닫다", "关", "", "close"],
		["v", "믿다", "相信", "", "believe"],
		["v", "잡다", "抓", "", "catch"],
		["v", "웃다", "笑", "", "laugh"],
		["v", "놓다", "放", "", "put"],
		["v", "듣다", "听 · ㄷ", "ㄷ", "listen · ㄷ"],
		["v", "걷다", "走 · ㄷ", "ㄷ", "walk · ㄷ"],
		["v", "묻다", "问 · ㄷ", "ㄷ", "ask · ㄷ"],
		["v", "돕다", "帮 · ㅂ", "ㅂ", "help · ㅂ"],
		["v", "짓다", "建 · ㅅ", "ㅅ", "build · ㅅ"],
		["v", "낫다", "愈 · ㅅ", "ㅅ", "recover · ㅅ"],
		["v", "모르다", "不知 · 르", "르", "not know · 르"],
		["v", "부르다", "叫 · 르", "르", "call · 르"],
		["v", "따르다", "跟随 · ㅡ", "ㅡ", "follow · ㅡ"],
		["v", "쓰다", "写 · ㅡ", "ㅡ", "write · ㅡ"],
		["v", "모으다", "收集 · ㅡ", "ㅡ", "gather · ㅡ"],
		["v", "가시다", "去 · 敬语", "높임", "go · honorific"],
		["v", "계시다", "在 · 敬语", "높임", "stay · honorific"],
		["a", "좋다", "好", "", "good"],
		["a", "많다", "多", "", "many"],
		["a", "크다", "大", "", "big"],
		["a", "작다", "小", "", "small"],
		["a", "높다", "高", "", "high"],
		["a", "낮다", "低", "", "low"],
		["a", "길다", "长", "", "long"],
		["a", "짧다", "短", "", "short"],
		["a", "멀다", "远", "", "far"],
		["a", "넓다", "宽", "", "wide"],
		["a", "좁다", "窄", "", "narrow"],
		["a", "빠르다", "快 · 르", "르", "fast · 르"],
		["a", "다르다", "不同 · 르", "르", "different · 르"],
		["a", "예쁘다", "漂亮 · ㅡ", "ㅡ", "pretty · ㅡ"],
		["a", "아프다", "疼 · ㅡ", "ㅡ", "hurt · ㅡ"],
		["a", "바쁘다", "忙 · ㅡ", "ㅡ", "busy · ㅡ"],
		["a", "가깝다", "近 · ㅂ", "ㅂ", "near · ㅂ"],
		["a", "춥다", "冷 · ㅂ", "ㅂ", "cold · ㅂ"],
		["a", "덥다", "热 · ㅂ", "ㅂ", "hot · ㅂ"],
		["a", "어렵다", "难 · ㅂ", "ㅂ", "difficult · ㅂ"],
		["a", "쉽다", "容易 · ㅂ", "ㅂ", "easy · ㅂ"],
		["a", "맵다", "辣 · ㅂ", "ㅂ", "spicy · ㅂ"],
		["a", "무겁다", "重 · ㅂ", "ㅂ", "heavy · ㅂ"],
		["a", "가볍다", "轻 · ㅂ", "ㅂ", "light · ㅂ"],
		["a", "아름답다", "美 · ㅂ", "ㅂ", "beautiful · ㅂ"],
		["a", "그렇다", "那样 · ㅎ", "ㅎ", "so · ㅎ"],
		["a", "파랗다", "蓝 · ㅎ", "ㅎ", "blue · ㅎ"],
		["a", "어떻다", "怎样 · ㅎ", "ㅎ", "how · ㅎ"],
		["a", "푸르다", "青绿 · 러", "러", "green · 러"],
		["a", "있다", "有", "", "exist"],
		["a", "없다", "没有", "", "not exist"],
		["a", "행복하다", "幸福 · 하다", "하다", "happy · 하다"],
		["v", "아니다", "不是", "", "not be"],
		["n", "학생", "学生", "", "student"],
		["n", "선생님", "老师", "", "teacher"],
		["n", "사람", "人", "", "person"],
		["n", "친구", "朋友", "", "friend"],
		["n", "물", "水", "", "water"],
		["n", "밥", "饭", "", "rice"],
		["n", "책", "书", "", "book"],
		["n", "집", "家", "", "house"],
		["n", "학교", "学校", "", "school"],
		["n", "회사", "公司", "", "company"],
		["n", "한국", "韩国", "", "Korea"],
		["n", "서울", "首尔", "", "Seoul"],
		["n", "시간", "时间", "", "time"],
		["n", "돈", "钱", "", "money"],
		["n", "이름", "名字", "", "name"],
		["n", "의사", "医生", "", "doctor"],
		["n", "커피", "咖啡", "", "coffee"],
		["n", "고양이", "猫", "", "cat"],
		["n", "바다", "海", "", "sea"],
		["n", "꽃", "花", "", "flower"],
		["n", "음식", "食物", "", "food"],
		["n", "영화", "电影", "", "movie"],
		["n", "컴퓨터", "电脑", "", "computer"]
	];
	var clip = null;
	var perline = 2;
	var maxcols = 4;
	var tries = 0;
	var lastpaint = null;
	var rtimer = 0;
	var bound = false;

	if (exp) {

		exp.picks = PICKS;
		return;
	}
	if (typeof SK === "undefined" || !SK.regutil) return;

	SK.regutil("u-kverb", {
		title: "韩语动词变化",
		go: "显示",
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
		return '<p class="hint" id="kv-hint"></p>'
			+ '<div class="row" id="kv-row">'
			+ '<label id="kv-type"><select id="kv-sel">'
			+ '<option value="auto"></option>'
			+ '<option value="v"></option>'
			+ '<option value="a"></option>'
			+ '<option value="n"></option>'
			+ "</select></label>"
			+ '<label id="kv-word">' + SK.combo("kv-in", "먹다", "词典形，如 먹다、예쁘다、학생") + "</label>"
			+ "</div>"
			+ '<div id="kv-out"></div>';
	}

	function open(){
		var hint = F.id("kv-hint");
		if (hint) {
			F.seta(hint, "data-k", "web.kverb.hint");
			hint.textContent = tx("web.kverb.hint", "先选类型（自动/动词/形容词/名词），再输入或在右侧选词。每条旁边的喇叭听发音。걷다 按「走」，묻다 按「问」。名词会按 이다 词尾变化。表末是朝鲜文化语（ㅂ네다 系）、朝鲜播音体（只列与标准语不同的 -시였습니다）和延边朝鲜语（-ㅁ다/슴다、-ㄴ다이）；与标准语同形的说法不列。");
		}
		var title = F.id("box-title");
		if (title) title.textContent = tx("web.tool.kverb", "韩语动词变化");
		setlab("kv-word", "web.kverb.word", "词典形");
		setlab("kv-type", "web.kverb.type", "类型");
		SK.combofill("kv-in", pickgroups());
		setopt("auto", "web.kverb.auto", "自动");
		setopt("v", "web.kverb.verb", "动词");
		setopt("a", "web.kverb.adj", "形容词");
		setopt("n", "web.kverb.noun", "名词");
		var sel = F.id("kv-sel");
		if (sel && !F.attr(sel, "data-bind")) {
			F.seta(sel, "data-bind", "1");
			sel.onchange = ontype;
		}
		var box = F.id("kv-in");
		if (box) {
			F.seta(box, "data-kph", "web.kverb.ph");
			box.placeholder = tx("web.kverb.ph", "词典形，如 먹다、예쁘다、학생");
		}
		var out = F.id("kv-out");
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

	// 换了类型就把下拉限制成这一类；自动时列全部
	function ontype(){
		SK.combofill("kv-in", pickgroups());
		run();
	}

	function pickgroups(){
		var lang = langcode();
		var sel = F.id("kv-sel");
		var pick = sel ? sel.value : "auto";
		var groups = [];
		var cur = null;
		var i, row, gloss, name;
		for (i = 0; i < PICKS.length; i++) {
			row = PICKS[i];
			if (pick !== "auto" && row[0] !== pick) continue;
			name = lp(lang, PICKG[row[0]]);
			if (!cur || cur.g !== name) {
				cur = { g: name, items: [] };
				groups.push(cur);
			}
			gloss = lang === "zh" ? row[2] : lang === "ko" ? row[3] : row[4];
			cur.items.push({ v: row[1], t: gloss ? row[1] + "  " + gloss : row[1] });
		}
		return groups;
	}

	function onout(ev){
		var btn = ev.target;
		var out = F.id("kv-out");
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
		var audio = new Audio("/api/dict/speak?text=" + encodeURIComponent(text) + "&lang=ko");
		clip = audio;
		audio.onerror = function(){
			if (clip === audio) SK.msg("box-msg", tx("web.kverb.nospeak", "无法发音"), true);
		};
		var played = audio.play();
		if (played && played.then) played.then(function(){}, function(){
			if (clip === audio) SK.msg("box-msg", tx("web.kverb.nospeak", "无法发音"), true);
		});
	}

	function paint(word, note, rows, tm){
		var out = F.id("kv-out");
		if (!out) return;
		if (!rows || !rows.length) { out.innerHTML = ""; lastpaint = null; return; }
		lastpaint = { word: word, note: note, rows: rows, tm: tm };
		render(out);
	}

	// 栏宽固定，窗口多宽就排几栏；只有 1、2 栏时整行均分。
	var CELLW = 200;

	function tabopen(cls){
		var n = perline;
		var html = '<table class="' + cls + '" style="width:' + (n <= 2 ? "100%" : (n * CELLW) + "px") + '">';
		if (n > 2) {
			html += "<colgroup>";
			for (var i = 0; i < n; i++) html += '<col style="width:' + CELLW + 'px">';
			html += "</colgroup>";
		}
		return html;
	}

	function render(out){
		if (!out || !lastpaint) return;
		var cw = out.clientWidth;
		if (cw <= 0) {
			// 刚切到工具页时容器可能还没显示，等一帧再量。
			if (tries < 5) { tries++; setTimeout(refit, 40); }
			return;
		}
		tries = 0;
		perline = Math.floor(cw / CELLW);
		if (perline < 1) perline = 1;
		if (perline > 8) perline = 8;
		out.innerHTML = tablehtml(lastpaint.word, lastpaint.note, lastpaint.rows, lastpaint.tm);
	}

	function onresize(){
		if (rtimer) clearTimeout(rtimer);
		rtimer = setTimeout(refit, 150);
	}

	function refit(){
		var out = F.id("kv-out");
		if (out && lastpaint) render(out);
	}

	function tablehtml(word, note, rows, tm){
		var tip = esc(tx("dict.speak", "发音"));
		var html = '<div class="jv-head"><b>' + esc(word) + "</b><span>" + esc(note) + "</span>"
			+ (tm ? '<span class="jv-time">' + esc(tm) + "</span>" : "") + "</div>" + tabopen("jv-tab");
		var g = "";
		var buf = [];
		var i;
		for (i = 0; i < rows.length; i++) {
			if (rows[i].g !== g) {
				html += flush(buf, tip);
				buf = [];
				g = rows[i].g;
				html += '<tr><td class="kv-g" colspan="' + perline + '">' + esc(g) + "</td></tr>";
			}
			buf.push(rows[i]);
		}
		return html + flush(buf, tip) + "</table>";
	}

	function flush(buf, tip){
		var html = "";
		var j, k;
		for (j = 0; j < buf.length; j += perline) {
			html += "<tr>";
			for (k = 0; k < perline; k++) {
				html += j + k < buf.length ? cell(buf[j + k], tip) : '<td class="c-k"></td>';
			}
			html += "</tr>";
		}
		return html;
	}

	function cell(item, tip){
		var forms = String(item.text || "").split(" / ");
		var html = '<td class="c-k"><div class="clab">' + esc(item.name) + "</div>";
		var i;
		for (i = 0; i < forms.length; i++) {
			if (!forms[i]) continue;
			html += '<div class="cex"><span>' + esc(forms[i]) + "</span>"
				+ '<button type="button" class="jv-spk" data-v="' + esc(forms[i]) + '" title="' + tip + '"><i class="fa-solid fa-volume-high"></i></button></div>';
		}
		return html + "</td>";
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
		var sel = F.id("kv-sel");
		if (!sel) return;
		var i;
		for (i = 0; i < sel.options.length; i++) {
			if (sel.options[i].value === value) sel.options[i].textContent = tx(key, fb);
		}
	}

	// 输入变化先防抖 180 ms 再算：现在要先查一次词典，逐字触发太费。
	function run(){
		if (timer) clearTimeout(timer);
		timer = setTimeout(go, 180);
	}

	function go(){
		timer = 0;
		var n = ++gen;
		var box = F.id("kv-in");
		var sel = F.id("kv-sel");
		var word = norm(box ? box.value : "");
		var force = sel ? sel.value : "auto";
		if (!word) {
			paint("", "", []);
			SK.msg("box-msg", "");
			return;
		}
		var t0 = now();
		lookup(word, function(hits){
			if (n !== gen) return;
			var tdict = hits ? now() - t0 : -1;
			var miss = hits && !hits.length ? tx("web.kverb.miss", "词典里没有这个词，已按词尾判断。") : "";
			var t1 = now();
			conjreq({ lang: "ko", q: word, type: force, ui: langcode() }, function(d){
				if (n !== gen) return;
				var tcalc = now() - t1;
				if (!d) {
					paint("", "", []);
					SK.msg("box-msg", tx("web.kverb.down", "变形接口不可用"), true);
					return;
				}
				if (!d.ok) {
					paint("", "", []);
					SK.msg("box-msg", d.err, true);
					return;
				}
				paint(d.head, d.note, objrows(d.rows), timestr(tdict, tcalc, miss));
				SK.msg("box-msg", "");
			});
		});
	}

	// 查词只用来算耗时、提示词条在不在；类型仍按词尾判断。
	function lookup(word, done){
		getjson("/api/dict?dict=ko&limit=8&q=" + encodeURIComponent(word), function(jo){
			done(jo && jo.code === 100 && jo.data ? (jo.data.hits || []) : null);
		});
	}

	function getjson(url, ok){
		fetch(url).then(function(r){ return r.json(); }).then(ok, function(){ ok(null); });
	}

	// 变形算在 C# 端（/api/conj），这里只取结果和渲染。
	function conjreq(params, done){
		var qs = [], k;
		for (k in params) if (params[k] !== undefined && params[k] !== null && params[k] !== "")
			qs.push(k + "=" + encodeURIComponent(params[k]));
		fetch("/api/conj?" + qs.join("&")).then(function(r){ return r.json(); }).then(function(jo){
			done(jo && jo.code === 100 && jo.data ? jo.data : null);
		}, function(){ done(null); });
	}

	function objrows(rows){
		var out = [], i;
		if (!rows) return out;
		for (i = 0; i < rows.length; i++) out.push({ name: rows[i].l, text: rows[i].v, g: rows[i].g || "" });
		return out;
	}

	function langcode(){
		var sel = F.id("lang");
		return sel && sel.value ? sel.value : "zh";
	}

	function ci(ch){ return CHO.indexOf(ch); }
	function vi(ch){ return JUNG.indexOf(ch); }
	function zi(ch){ return JONG.indexOf(ch); }

	function sy(ch){
		var c = ch.charCodeAt(0) - 0xAC00;
		return { cho: Math.floor(c / 588), jung: Math.floor((c % 588) / 28), jong: c % 28 };
	}

	function chOf(o){
		return String.fromCharCode(0xAC00 + (o.cho * 21 + o.jung) * 28 + o.jong);
	}

	function cp(a){
		var o = [];
		var i;
		for (i = 0; i < a.length; i++) o.push({ cho: a[i].cho, jung: a[i].jung, jong: a[i].jong });
		return o;
	}

	function jn(a){
		var s = "";
		var i;
		for (i = 0; i < a.length; i++) s += chOf(a[i]);
		return s;
	}

	function decAll(s){
		var o = [];
		var i, c;
		for (i = 0; i < s.length; i++) {
			c = s.charCodeAt(i);
			if (c < 0xAC00 || c > 0xD7A3) return null;
			c -= 0xAC00;
			o.push({ cho: Math.floor(c / 588), jung: Math.floor((c % 588) / 28), jong: c % 28 });
		}
		return o;
	}

	function euStem(syls, irr){
		var s = cp(syls);
		var last = s[s.length - 1];
		if (irr === "d") {
			last.jong = J_R;
			return { s: s, eu: true, r: false };
		}
		if (irr === "b") {
			last.jong = 0;
			s.push(sy("우"));
			return { s: s, eu: false, r: false };
		}
		if (irr === "s") {
			last.jong = 0;
			return { s: s, eu: true, r: false };
		}
		if (irr === "h") {
			last.jong = 0;
			return { s: s, eu: false, r: false };
		}
		if (last.jong === 0) return { s: s, eu: false, r: false };
		if (last.jong === J_R) return { s: s, eu: false, r: true };
		return { s: s, eu: true, r: false };
	}

	function applyDrop(st, drop){
		if (!(drop && st.r)) return st;
		var s = cp(st.s);
		s[s.length - 1].jong = 0;
		return { s: s, eu: st.eu, r: false };
	}

	function bsida(syls, irr){
		var s;
		if (irr === "si") {
			s = cp(syls);
			s[s.length - 1].jong = J_B;
			return jn(s) + "시다";
		}
		var st = applyDrop(euStem(syls, irr), true);
		if (st.eu) return jn(st.s) + "읍시다";
		s = cp(st.s);
		s[s.length - 1].jong = J_B;
		return jn(s) + "시다";
	}

	function hon(syls, irr){
		if (jn(syls) === "있") return decAll("계시");
		if (irr === "hada") {
			var s = cp(syls);
			s.push(sy("시"));
			return s;
		}
		var st = applyDrop(euStem(syls, irr), true);
		var out = cp(st.s);
		if (st.eu) out.push(sy("으"));
		out.push(sy("시"));
		return out;
	}

	function lp(lang, row){
		var i = lang === "zh" ? 0 : lang === "ko" ? 1 : 2;
		return row[i];
	}

	function norm(s){
		s = String(s || "").replace(/^\s+|\s+$/g, "");
		var i = s.search(/\s/);
		if (i >= 0) s = s.slice(0, i);
		return s.replace(/[.?。．!！,，]+$/g, "");
	}

		// 旧实现：计算已移到 C# 端（/api/conj），保留仅供对照，勿再调用。

	// -ㅂ네다 / -읍네다 这类以 ㅂ 结尾的方言尾：开音节加 ㅂ，闭音节加 읍

	// 济州语 -수다 / -수꽈：动词接 해체形（먹어수다、가수다），形容词和 있다 接词干（좋수다）

	// 把标准语 의문형 -느냐/-냐 换成方言的 -나/-노/-냐

	// 하오체：开音节接 -오（가오），闭音节接 -소（먹소）

	// 形态被动只有固定说法，给出词典形和해요形

	// 文化语 -ㅂ네다 类：开音节和 ㄹ 结尾加 ㅂ，其它闭音节加 습（갑네다 / 먹습네다 / 삽네다）。

	// 延边 -ㅁ다 / -ㅁ까：开音节和 ㄹ 结尾加 ㅁ，其它闭音节加 슴（감다 / 먹슴다 / 삼다）。

	// 延边 하오体 -ㄴ다이 / -는다이，取法与 -는 / -은 相同。

	// 文化语把被动 / 使动的 이 · 히 · 리 · 기 后再加 우：먹히다 → 먹히우다。只给与标准语不同的强调形。

	// 朝鲜文化语、朝鲜播音体、延边朝鲜语三组词尾

	// 名词（이다）在文化语和延边语里的说法

	// 名词（이다）的方言说法：只列证据明确的四种

	// 查词、计算各计一段；不足 1 毫秒显示 <1 ms。
	function timestr(dictms, calcms, extra){
		var a = "";
		if (dictms >= 0) a = tx("web.verb.tdict", "查词") + " " + fmtms(dictms);
		if (calcms >= 0) a += (a ? " · " : "") + tx("web.verb.tcalc", "计算") + " " + fmtms(calcms);
		if (extra) a += (a ? " · " : "") + extra;
		return a;
	}

	function fmtms(v){
		v = Math.round(v * 10) / 10;
		return v < 1 ? "<1 ms" : Math.round(v) + " ms";
	}

	function now(){
		return window.performance && window.performance.now ? window.performance.now() : Date.now();
	}
})();

