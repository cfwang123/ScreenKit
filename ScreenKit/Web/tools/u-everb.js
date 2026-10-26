(function(){
	// 英语动词时态：输入原形，列出基本形式、十六种时态，以及否定、疑问、被动和其他形式。
	// [原形, 过去式, 过去分词, 中文释义, 英文释义]；没有英文词典时用这一小份兜底。
	var IR = [
		["be", "was / were", "been", "是；存在", "be"],
		["have", "had", "had", "有", "have"],
		["do", "did", "done", "做", "do"],
		["go", "went", "gone", "去；走", "go"],
		["come", "came", "come", "来", "come"],
		["get", "got", "got / gotten", "得到；变得", "get"],
		["make", "made", "made", "做；制造", "make"],
		["take", "took", "taken", "拿；取", "take"],
		["see", "saw", "seen", "看见", "see"],
		["give", "gave", "given", "给", "give"],
		["write", "wrote", "written", "写", "write"],
		["speak", "spoke", "spoken", "说话", "speak"],
		["begin", "began", "begun", "开始", "begin"],
		["run", "ran", "run", "跑；经营", "run"],
		["swim", "swam", "swum", "游泳", "swim"],
		["teach", "taught", "taught", "教", "teach"],
		["buy", "bought", "bought", "买", "buy"],
		["think", "thought", "thought", "想；认为", "think"],
		["lie", "lay", "lain", "躺；卧", "lie down"]
	];
	var PICKS = [
		["cm", "walk", "走"],
		["cm", "study", "学习"],
		["cm", "play", "玩"],
		["cm", "stop", "停止"],
		["cm", "work", "工作"],
		["cm", "watch", "看"],
		["cm", "live", "住"],
		["cm", "like", "喜欢"],
		["cm", "open", "打开"],
		["cm", "carry", "搬；携带"],
		["cm", "panic", "惊慌"],
		["irr", "be", "是"],
		["irr", "have", "有"],
		["irr", "do", "做"],
		["irr", "go", "去"],
		["irr", "come", "来"],
		["irr", "get", "得到"],
		["irr", "make", "做；制造"],
		["irr", "take", "拿；取"],
		["irr", "see", "看见"],
		["irr", "give", "给"],
		["irr", "write", "写"],
		["irr", "speak", "说"],
		["irr", "begin", "开始"],
		["irr", "run", "跑"],
		["irr", "swim", "游泳"],
		["irr", "teach", "教"],
		["irr", "buy", "买"],
		["irr", "think", "想"],
		["irr", "lie", "躺"],
		["phr", "give up", "放弃"],
		["phr", "look after", "照顾"],
		["phr", "turn on", "打开（电器）"]
	];
	var PICKG = {
		cm: ["常见规则动词", "Common regular"],
		irr: ["常见不规则动词", "Common irregular"],
		phr: ["短语动词", "Phrasal"]
	};
	var MODALS = {
		can: 1, could: 1, may: 1, might: 1, must: 1, shall: 1, should: 1,
		will: 1, would: 1, ought: 1
	};
	// 短语动词只接这类小品词或介词，避免把两个单词的短语当成动词。
	var PARTS = {
		up: 1, down: 1, off: 1, on: 1, in: 1, out: 1, away: 1, back: 1,
		over: 1, around: 1, about: 1, through: 1, along: 1, across: 1,
		apart: 1, aside: 1, by: 1, for: 1, with: 1, after: 1, at: 1,
		into: 1, onto: 1, upon: 1, from: 1, of: 1, against: 1, together: 1,
		forward: 1, toward: 1, towards: 1
	};
	// 多音节但重音在末音节，-ing / -ed 要双写末尾辅音。
	var DOUBLE = {
		begin: 1, prefer: 1, refer: 1, occur: 1, admit: 1, permit: 1, commit: 1,
		submit: 1, omit: 1, transmit: 1, equip: 1, infer: 1, confer: 1, defer: 1,
		deter: 1, abhor: 1, allot: 1, regret: 1, forget: 1, upset: 1, reset: 1,
		preset: 1, inset: 1, input: 1, output: 1, offset: 1, beset: 1, control: 1,
		patrol: 1, propel: 1, expel: 1, compel: 1, rebel: 1, excel: 1, acquit: 1,
		quiz: 1, quit: 1, format: 1, kidnap: 1, embed: 1, transfer: 1, enrol: 1,
		fulfil: 1, distil: 1, instil: 1
	};
	var VOW = "aeiou";
	var LAB = {
		title: ["英语动词时态", "English verb tenses"],
		hint: ["输入英语动词原形，先列基本形式，再按现在、过去、将来、过去将来列出十六种时态，另附否定、疑问、被动和其他形式。斜杠两边是不同写法，时态表里用第一个；句中的 I、he、they 只是示例主语。",
			"Enter a base verb. The table lists the basic forms, the sixteen tenses (present, past, future and future in the past), then negatives, questions, passive and other forms. Slashes separate variants; the tenses use the first. I, he and they are example subjects."],
		word: ["动词", "Verb"],
		ph: ["动词原形，如 walk、go、study", "Base form, e.g. walk, go, study"],
		reg: ["规则动词", "Regular verb"],
		irr: ["不规则动词", "Irregular verb"],
		bad: ["只支持一个英语动词，可带小品词，如 walk、give up", "One English verb only, with an optional particle, e.g. walk or give up"],
		modal: ["情态动词没有这些时态变化", "Modal verbs have no such tense forms"],
		from: ["由 %s 识别", "from %s"],
		be: ["be 的进行时、完成进行时和被动语态极少用", "be rarely uses continuous, perfect continuous or passive forms"],
		g_form: ["基本形式", "Basic forms"],
		g_pres: ["现在时", "Present"],
		g_past: ["过去时", "Past"],
		g_fut: ["将来时", "Future"],
		g_fip: ["过去将来时", "Future in the past"],
		g_neg: ["否定式", "Negative"],
		g_q: ["疑问式", "Questions"],
		g_pas: ["被动语态（及物动词）", "Passive (transitive)"],
		g_oth: ["其他形式", "Other forms"],
		f_base: ["原形", "Base form"],
		f_s3: ["第三人称单数", "3rd person singular"],
		f_ing: ["现在分词", "Present participle"],
		f_past: ["过去式", "Past"],
		f_pp: ["过去分词", "Past participle"],
		t_pres: ["一般现在时", "Simple present"],
		t_pcont: ["现在进行时", "Present continuous"],
		t_ppf: ["现在完成时", "Present perfect"],
		t_ppc: ["现在完成进行时", "Present perfect continuous"],
		t_past: ["一般过去时", "Simple past"],
		t_pastc: ["过去进行时", "Past continuous"],
		t_pastp: ["过去完成时", "Past perfect"],
		t_pastpc: ["过去完成进行时", "Past perfect continuous"],
		t_fut: ["一般将来时", "Simple future"],
		t_futc: ["将来进行时", "Future continuous"],
		t_futp: ["将来完成时", "Future perfect"],
		t_futpc: ["将来完成进行时", "Future perfect continuous"],
		t_fips: ["一般过去将来时", "Simple future in the past"],
		t_fipc: ["过去将来进行时", "Future in the past continuous"],
		t_fipp: ["过去将来完成时", "Future in the past perfect"],
		t_fippc: ["过去将来完成进行时", "Future in the past perfect continuous"],
		n_pres: ["一般现在时否定", "Simple present negative"],
		n_past: ["一般过去时否定", "Simple past negative"],
		n_pcont: ["现在进行时否定", "Present continuous negative"],
		n_ppf: ["现在完成时否定", "Present perfect negative"],
		n_fut: ["一般将来时否定", "Simple future negative"],
		q_pres: ["一般现在时疑问", "Simple present question"],
		q_past: ["一般过去时疑问", "Simple past question"],
		q_pcont: ["现在进行时疑问", "Present continuous question"],
		q_ppf: ["现在完成时疑问", "Present perfect question"],
		q_fut: ["一般将来时疑问", "Simple future question"],
		p_pres: ["被动 · 一般现在时", "Passive · simple present"],
		p_past: ["被动 · 一般过去时", "Passive · simple past"],
		p_pcont: ["被动 · 现在进行时", "Passive · present continuous"],
		p_pastc: ["被动 · 过去进行时", "Passive · past continuous"],
		p_ppf: ["被动 · 现在完成时", "Passive · present perfect"],
		p_pastp: ["被动 · 过去完成时", "Passive · past perfect"],
		p_fut: ["被动 · 一般将来时", "Passive · simple future"],
		p_futp: ["被动 · 将来完成时", "Passive · future perfect"],
		p_fips: ["被动 · 过去将来时", "Passive · future in the past"],
		o_imp: ["祈使句", "Imperative"],
		o_inf: ["不定式", "Infinitive"],
		o_pinf: ["完成式不定式", "Perfect infinitive"],
		o_ger: ["动名词", "Gerund"],
		o_pcp: ["完成式分词", "Perfect participle"],
		o_pi: ["被动式不定式", "Passive infinitive"]
	};
	var timer = 0;
	var clip = null;

	function build(word, lang){
		lang = lang || "zh";
		function L(pair){ return lang === "zh" ? pair[0] : pair[1]; }
		var parts = norm(word).split(" ");
		var head = parts[0] || "";
		var tail = parts.length > 1 ? " " + parts.slice(1).join(" ") : "";
		var i;
		if (!head || !/^[a-z]+$/.test(head) || parts.length > 2) return { ok: false, err: L(LAB.bad) };
		for (i = 1; i < parts.length; i++) if (!PARTS[parts[i]]) return { ok: false, err: L(LAB.bad) };
		if (MODALS[head]) return { ok: false, err: L(LAB.modal) };
		var hit = findirr(head);
		var row = hit ? hit.row : null;
		var base = row ? String(row[0]).toLowerCase() : head;
		var pasts = row ? varlist(row[1]) : [];
		var pps = row ? varlist(row[2]) : [];
		var isBe = base === "be";
		var b = base + tail;
		var ingF = ing(base) + tail;
		var ppF = (pps.length ? pps[0] : ed(base)) + tail;
		var pastF = (pasts.length ? pasts[0] : ed(base)) + tail;
		var s3F = s3(base) + tail;
		if (isBe) {
			ingF = "being" + tail;
			ppF = "been" + tail;
			pastF = "was" + tail;
			s3F = "is" + tail;
		}
		// 一般现在时按 I / he 各给一个形；be 的 am / is 和 was / were 单独取。
		var p1 = isBe ? "am" : b;
		var p3 = isBe ? "is" : s3F;
		var q1 = isBe ? "was" : pastF;
		var q2 = isBe ? "were" : pastF;
		var n1 = isBe ? "am not" : "do not " + b;
		var n3 = isBe ? "is not" : "does not " + b;
		var rows = [];
		function R(g, name, text){ rows.push({ g: g, name: name, text: text }); }
		var g;
		// 基本形式
		g = L(LAB.g_form);
		R(g, L(LAB.f_base), b);
		R(g, L(LAB.f_s3), isBe ? "am / is / are" : s3F);
		R(g, L(LAB.f_ing), ingF);
		R(g, L(LAB.f_past), isBe ? "was / were" : (pasts.length ? variants(row[1], tail) : ed(base) + tail));
		R(g, L(LAB.f_pp), isBe ? "been" : (pps.length ? variants(row[2], tail) : ed(base) + tail));
		// 现在时
		g = L(LAB.g_pres);
		R(g, L(LAB.t_pres), "I " + p1 + " · he " + p3);
		R(g, L(LAB.t_pcont), "I am " + ingF + " · he is " + ingF);
		R(g, L(LAB.t_ppf), "I have " + ppF + " · he has " + ppF);
		R(g, L(LAB.t_ppc), "I have been " + ingF + " · he has been " + ingF);
		// 过去时
		g = L(LAB.g_past);
		R(g, L(LAB.t_past), "I " + q1 + (isBe ? " · they " + q2 : ""));
		R(g, L(LAB.t_pastc), "I was " + ingF + " · they were " + ingF);
		R(g, L(LAB.t_pastp), "I had " + ppF);
		R(g, L(LAB.t_pastpc), "I had been " + ingF);
		// 将来时
		g = L(LAB.g_fut);
		R(g, L(LAB.t_fut), "I will " + b);
		R(g, L(LAB.t_futc), "I will be " + ingF);
		R(g, L(LAB.t_futp), "I will have " + ppF);
		R(g, L(LAB.t_futpc), "I will have been " + ingF);
		// 过去将来时
		g = L(LAB.g_fip);
		R(g, L(LAB.t_fips), "I would " + b);
		R(g, L(LAB.t_fipc), "I would be " + ingF);
		R(g, L(LAB.t_fipp), "I would have " + ppF);
		R(g, L(LAB.t_fippc), "I would have been " + ingF);
		// 否定
		g = L(LAB.g_neg);
		R(g, L(LAB.n_pres), "I " + n1 + " · he " + n3);
		R(g, L(LAB.n_past), "I " + (isBe ? "was not · they were not" : "did not " + b));
		R(g, L(LAB.n_pcont), "I am not " + ingF + " · he is not " + ingF);
		R(g, L(LAB.n_ppf), "I have not " + ppF + " · he has not " + ppF);
		R(g, L(LAB.n_fut), "I will not " + b);
		// 疑问
		g = L(LAB.g_q);
		R(g, L(LAB.q_pres), isBe ? "Am I? · Is he?" : "Do I " + b + "? · Does he " + b + "?");
		R(g, L(LAB.q_past), isBe ? "Was I? · Were they?" : "Did I " + b + "?");
		R(g, L(LAB.q_pcont), "Am I " + ingF + "? · Is he " + ingF + "?");
		R(g, L(LAB.q_ppf), "Have I " + ppF + "? · Has he " + ppF + "?");
		R(g, L(LAB.q_fut), "Will I " + b + "?");
		// 被动语态；be 没有被动
		if (!isBe) {
			g = L(LAB.g_pas);
			R(g, L(LAB.p_pres), "It is " + ppF + " · They are " + ppF);
			R(g, L(LAB.p_past), "It was " + ppF + " · They were " + ppF);
			R(g, L(LAB.p_pcont), "It is being " + ppF);
			R(g, L(LAB.p_pastc), "It was being " + ppF);
			R(g, L(LAB.p_ppf), "It has been " + ppF);
			R(g, L(LAB.p_pastp), "It had been " + ppF);
			R(g, L(LAB.p_fut), "It will be " + ppF);
			R(g, L(LAB.p_futp), "It will have been " + ppF);
			R(g, L(LAB.p_fips), "It would be " + ppF);
		}
		// 其他形式
		g = L(LAB.g_oth);
		R(g, L(LAB.o_imp), isBe ? "be! · Do not be!" : b + "! · Do not " + b + "!");
		R(g, L(LAB.o_inf), "to " + b);
		R(g, L(LAB.o_pinf), "to have " + ppF);
		R(g, L(LAB.o_ger), ingF);
		R(g, L(LAB.o_pcp), "having " + ppF);
		if (!isBe) R(g, L(LAB.o_pi), "to be " + ppF);
		// 顶部说明
		var note;
		if (row) {
			note = L(LAB.irr);
			if (pasts.length) note += " · " + L(LAB.f_past) + " " + variants(row[1], tail);
			if (pps.length) note += " · " + L(LAB.f_pp) + " " + variants(row[2], tail);
			var mean = tail ? "" : (lang === "zh" ? row[3] : row[4]);
			if (mean) note += " · " + mean;
			if (isBe) note += " · " + L(LAB.be);
			if (hit.kind !== "base") note = fmt(L(LAB.from), norm(word)) + " · " + note;
		}
		else note = L(LAB.reg);
		return { ok: true, head: base + tail, note: note, rows: rows };
	}

	function norm(s){
		s = String(s == null ? "" : s).toLowerCase().replace(/\s+/g, " ").replace(/^\s+|\s+$/g, "");
		return s.replace(/^to /, "");
	}

	function fmt(s, v){
		return String(s).replace("%s", v);
	}

	function irs(){
		return (typeof SK !== "undefined" && SK.iverbrows) || IR;
	}

	function findirr(word){
		var rows = irs();
		var i, r;
		for (i = 0; i < rows.length; i++) {
			if (String(rows[i][0]).toLowerCase() === word) return { kind: "base", row: rows[i] };
		}
		for (i = 0; i < rows.length; i++) {
			r = rows[i];
			if (varhas(r[1], word)) return { kind: "past", row: r };
			if (varhas(r[2], word)) return { kind: "pp", row: r };
		}
		// 过去式和过去分词之外，再认第三人称单数（has、does、goes）和 am / is / are。
		for (i = 0; i < rows.length; i++) {
			r = rows[i];
			if (s3(String(r[0]).toLowerCase()) === word) return { kind: "s3", row: r };
			if (String(r[0]).toLowerCase() === "be" && (word === "am" || word === "are")) return { kind: "s3", row: r };
		}
		return null;
	}

	function varlist(field){
		var out = [];
		var parts = String(field == null ? "" : field).split("/");
		var i, s;
		for (i = 0; i < parts.length; i++) {
			s = parts[i].replace(/^\s+|\s+$/g, "").toLowerCase();
			if (s && out.indexOf(s) < 0) out.push(s);
		}
		return out;
	}

	function varhas(field, word){
		return varlist(field).indexOf(word) >= 0;
	}

	function variants(field, tail){
		var list = varlist(field);
		var i;
		if (!list.length) return "";
		for (i = 0; i < list.length; i++) list[i] += tail;
		return list.join(" / ");
	}

	function cons(ch){
		return ch >= "a" && ch <= "z" && VOW.indexOf(ch) < 0 && "wxy".indexOf(ch) < 0;
	}

	function cvc(w){
		if (w.length < 3) return false;
		var a = w.charAt(w.length - 3);
		var b = w.charAt(w.length - 2);
		var c = w.charAt(w.length - 1);
		return cons(a) && VOW.indexOf(b) >= 0 && cons(c);
	}

	function syll(w){
		var m = w.match(/[aeiouy]+/g);
		return m ? m.length : 1;
	}

	function dbl(w){
		if (DOUBLE[w]) return true;
		return syll(w) === 1 && cvc(w);
	}

	function s3(w){
		if (w === "be") return "is";
		if (w === "have") return "has";
		if (/(s|x|z|ch|sh|o)$/.test(w)) return w + "es";
		if (/[^aeiou]y$/.test(w)) return w.slice(0, -1) + "ies";
		return w + "s";
	}

	function ing(w){
		if (w === "be") return "being";
		if (/ie$/.test(w)) return w.slice(0, -2) + "ying";
		if (/(ee|oe|ye)$/.test(w)) return w + "ing";
		if (/e$/.test(w)) return w.slice(0, -1) + "ing";
		if (/c$/.test(w)) return w + "king";
		if (dbl(w)) return w + w.slice(-1) + "ing";
		return w + "ing";
	}

	function ed(w){
		if (/e$/.test(w)) return w + "d";
		if (/[^aeiou]y$/.test(w)) return w.slice(0, -1) + "ied";
		if (/c$/.test(w)) return w + "ked";
		if (dbl(w)) return w + w.slice(-1) + "ed";
		return w + "ed";
	}

	function saytext(s){
		return String(s == null ? "" : s).replace(/·/g, ",").replace(/\s*\/\s*/g, " or ").replace(/\s+/g, " ").replace(/^\s+|\s+$/g, "");
	}

	if (typeof module !== "undefined" && module.exports) {
		module.exports = {
			build: build,
			findirr: findirr,
			s3: s3,
			ing: ing,
			ed: ed
		};
		return;
	}
	if (typeof SK === "undefined" || !SK.regutil) return;

	SK.regutil("u-everb", {
		title: "英语动词时态",
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

	function tr(pair){
		return langcode() === "zh" ? pair[0] : pair[1];
	}

	function langcode(){
		var sel = F.id("lang");
		return sel && sel.value ? sel.value : "zh";
	}

	function body(){
		return '<p class="hint" id="ev-hint"></p>'
			+ '<div class="row" id="ev-row">'
			+ '<label id="ev-word">' + SK.combo("ev-in", "walk", "动词原形，如 walk、go、study") + "</label>"
			+ "</div>"
			+ '<div id="ev-out"></div>';
	}

	function open(){
		var hint = F.id("ev-hint");
		if (hint) hint.textContent = tr(LAB.hint);
		var title = F.id("box-title");
		if (title) title.textContent = tx("web.tool.everb", LAB.title[0]);
		setlab("ev-word", tr(LAB.word));
		SK.combofill("ev-in", pickgroups());
		var box = F.id("ev-in");
		if (box) box.placeholder = tr(LAB.ph);
		var out = F.id("ev-out");
		if (out && !F.attr(out, "data-bind")) {
			F.seta(out, "data-bind", "1");
			out.onclick = onout;
		}
		run();
	}

	function setlab(id, text){
		var el = F.id(id);
		if (!el) return;
		if (el.childNodes.length && el.childNodes[0].nodeType === 3) el.childNodes[0].nodeValue = text;
		else el.insertBefore(document.createTextNode(text), el.firstChild);
	}

	function pickgroups(){
		var zh = langcode() === "zh";
		var groups = [];
		var cur = null;
		var i, p, name;
		for (i = 0; i < PICKS.length; i++) {
			p = PICKS[i];
			name = zh ? PICKG[p[0]][0] : PICKG[p[0]][1];
			if (!cur || cur.g !== name) {
				cur = { g: name, items: [] };
				groups.push(cur);
			}
			cur.items.push({ v: p[1], t: zh ? p[1] + "  " + p[2] : p[1] });
		}
		return groups;
	}

	function onout(ev){
		var btn = ev.target;
		var out = F.id("ev-out");
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
		var audio = new Audio("/api/dict/speak?text=" + encodeURIComponent(text) + "&lang=en");
		clip = audio;
		audio.onerror = function(){
			if (clip === audio) SK.msg("box-msg", tx("web.everb.nospeak", "无法发音"), true);
		};
		var played = audio.play();
		if (played && played.then) played.then(function(){}, function(){
			if (clip === audio) SK.msg("box-msg", tx("web.everb.nospeak", "无法发音"), true);
		});
	}

	function run(){
		if (timer) clearTimeout(timer);
		timer = setTimeout(go, 150);
	}

	function go(){
		timer = 0;
		var box = F.id("ev-in");
		var word = norm(box ? box.value : "");
		if (!word) {
			paint("", "", []);
			SK.msg("box-msg", "");
			return;
		}
		var built = build(word, langcode());
		if (!built.ok) {
			paint("", "", []);
			SK.msg("box-msg", built.err, true);
			return;
		}
		paint(built.head, built.note, built.rows);
		SK.msg("box-msg", "");
	}

	function paint(head, note, rows){
		var out = F.id("ev-out");
		if (!out) return;
		if (!rows || !rows.length) { out.innerHTML = ""; return; }
		var tip = esc(tx("dict.speak", "发音"));
		var html = '<div class="jv-head"><b>' + esc(head) + "</b><span>" + esc(note) + "</span></div>"
			+ '<table class="jv-tab ev-tab">';
		var g = null;
		var i;
		for (i = 0; i < rows.length; i++) {
			if (rows[i].g !== g) {
				g = rows[i].g;
				html += '<tr><td class="kv-g" colspan="2">' + esc(g) + "</td></tr>";
			}
			html += '<tr><td class="jv-k">' + esc(rows[i].name) + '</td><td class="jv-v"><span>'
				+ esc(rows[i].text) + "</span>"
				+ '<button type="button" class="jv-spk" data-v="' + esc(saytext(rows[i].text)) + '" title="' + tip + '">'
				+ '<i class="fa-solid fa-volume-high"></i></button></td></tr>';
		}
		html += "</table>";
		out.innerHTML = html;
	}

	function esc(s){
		return String(s == null ? "" : s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/"/g, "&quot;");
	}
})();
