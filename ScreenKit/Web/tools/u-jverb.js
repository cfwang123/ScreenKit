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
	var timer = 0;
	var clip = null;
	var gen = 0;

	if (exp) {
		exp.guess = guess;
		exp.build = build;
		exp.poskind = poskind;
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
			+ '<div class="row">'
			+ '<label id="jv-word"><input id="jv-in" type="text" value="食べる"></label>'
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
			hint.textContent = tx("web.jverb.hint", "输入辞书形。类型默认自动：先查日语词典；没装词典时会提示，并按词尾判断（る是 2 类，する、くる是 3 类，其余是 1 类）。いく的て形、た形是って、った。");
		}
		setlab("jv-word", "web.jverb.word", "动词");
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
		var i;
		for (i = 0; i < rows.length; i += 2) {
			html += "<tr>" + cellhtml(rows[i], tip);
			if (i + 1 < rows.length) html += cellhtml(rows[i + 1], tip);
			else html += '<td class="jv-k"></td><td class="jv-v"></td>';
			html += "</tr>";
		}
		html += "</table>";
		out.innerHTML = html;
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
			if (!res || res.state === "down") {
				note = tx("web.jverb.down", "查不到词典，已按词尾判断。");
				bad = true;
			}
			else if (res.state === "missing") {
				note = (res.msg || tx("web.jverb.nodict", "未找到词典库 dict3.bin。请在帮助 → 安装功能里安装词典。"))
					+ (pick === "auto" ? tx("web.jverb.fallback", "已按词尾判断。") : "");
				bad = true;
			}
			if (pick === "auto") {
				var hit = res && res.entry;
				var from = hit ? poskind(hit.pos) : null;
				if (from && from.kind) {
					use = from;
					if (isIku(word)) use.flag = "iku";
					if (isAru(word)) use.flag = "arunai";
					note = note || tx("web.jverb.fromdict", "词典");
				}
				else {
					use = guess(word);
					if (res && res.state === "ok") {
						note = hit ? tx("web.jverb.notverb", "词典里这个词不是动词，已按词尾判断。")
							: tx("web.jverb.miss", "词典里没有这个词，已按词尾判断。");
					}
				}
			}
			else {
				use = { kind: pick, flag: "", sub: "" };
				if (pick === "1" && isIku(word)) use.flag = "iku";
				if (pick === "1" && isAru(word)) use.flag = "arunai";
				if (pick === "3") use.sub = isKuru(word) ? "kuru" : "";
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
			cmd: "命令形", stop: "禁止形", vol: "意向形", pot: "可能形",
			pas: "受身形", cau: "使役形", cap: "使役受身", tai: "たい", takunai: "たくない",
			c1: "1类（五段）", c2: "2类（一段）", c3s: "3类（する）", c3k: "3类（くる）",
			iku: "いく",
			bad2: "2类要以る结尾", bad3: "3类是する或くる", bad1: "1类词尾要是う段假名"
		};
		return {
			dict: "dictionary", masu: "masu", masen: "masen", mashita: "mashita", masend: "masen deshita",
			nai: "nai", nakatta: "nakatta", nakute: "nakute", naide: "naide",
			te: "te", ta: "ta", tara: "tara", ba: "ba",
			cmd: "imperative", stop: "prohibitive", vol: "volitional", pot: "potential",
			pas: "passive", cau: "causative", cap: "causative-passive", tai: "tai", takunai: "takunai",
			c1: "group 1 (godan)", c2: "group 2 (ichidan)", c3s: "group 3 (suru)", c3k: "group 3 (kuru)",
			iku: "iku",
			bad2: "Group 2 must end with る", bad3: "Group 3 is する or くる", bad1: "Group 1 must end with an u-row kana"
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
		return { ok: true, head: word, name: name, rows: rows.rows };
	}

	function row(lab, val){ return [lab, val]; }

	function pack(L, plain, masu, nai, te, ta, ba, cmd, vol, pot, pas, cau, cap){
		var tai = masu.slice(0, -2) + "たい";
		return {
			ok: true,
			plain: plain,
			masu: masu,
			ta: ta,
			nai: nai,
			rows: [
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
				row(L.cmd, cmd),
				row(L.stop, plain + "な"),
				row(L.vol, vol),
				row(L.pot, pot),
				row(L.pas, pas),
				row(L.cau, cau),
				row(L.cap, cap),
				row(L.tai, tai),
				row(L.takunai, tai.slice(0, -2) + "たくない")
			]
		};
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
		if (flag === "ute") { te = stem + "うて"; ta = stem + "うた"; }
		if (flag === "aru") { i = stem + "い"; cmd = stem + "い"; }
		var nai = isAru(word) ? arunai(word) : a + "ない";
		return pack(L, word, i + "ます", nai, te, ta, e + "ば", cmd, o + "う", e + "る", a + "れる", a + "せる", a + "せられる");
	}

	function ichidan(word, L){
		if (last(word) !== "る") return { ok: false, err: L.bad2 };
		var stem = word.slice(0, -1);
		return pack(L, word, stem + "ます", stem + "ない", stem + "て", stem + "た", stem + "れば",
			stem + "ろ、" + stem + "よ", stem + "よう", stem + "られる", stem + "られる", stem + "させる", stem + "させられる");
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
		if (isKuru(word)) return { kind: "3", sub: "kuru", flag: "" };
		if (isSuru(word)) return { kind: "3", sub: "suru", flag: "" };
		if (isAru(word)) return { kind: "1", flag: "arunai", sub: "" };
		var end = last(word);
		if (end === "る") return { kind: "2", flag: "", sub: "" };
		if (COL[end]) return { kind: "1", flag: isIku(word) ? "iku" : "", sub: "" };
		return null;
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

	function last(s){ return s.charAt(s.length - 1); }
})();
