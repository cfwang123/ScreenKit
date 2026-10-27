(function(){
	var langAt = 0;
	var filter = "";
	var gen = 0;
	var dgen = 0;
	var sgen = 0;
	var cgen = 0;
	var timer = 0;
	var wantId = 0;
	var cur = null;
	var selText = "";
	var selLang = "en";
	var drag = false;
	var downX = 0;
	var downY = 0;
	var audioUrl = "";
	var hits = [];

	function $(id){ return document.getElementById(id); }
	function langs(){
		if (window.L && L._lang && L._lang.length) return L._lang;
		return ["zh", "en", "ja", "ko", "es", "fr", "pt", "ru", "de"];
	}
	function t(key){
		var row = window.L && L[key];
		if (!row || !row.length) return "";
		var s = row[langAt];
		if (s) return s;
		// 缺译文时回退英文，再回退中文
		var i = langs().indexOf("en");
		if (i >= 0 && row[i]) return row[i];
		return row[0] || "";
	}
	function fmt(pat){
		var s = pat || "";
		var i;
		for (i = 1; i < arguments.length; i++)
			s = s.split("{" + (i - 1) + "}").join(arguments[i] == null ? "" : String(arguments[i]));
		return s;
	}
	function status(s){ $("status").textContent = s || ""; }
	function langname(code){
		if (code === "zh") return t("dict.filter.zh") || "汉语";
		if (code === "ja") return t("dict.filter.ja") || "日语";
		if (code === "ko") return t("dict.filter.ko") || "韩语";
		if (code === "en") return t("dict.filter.en") || "英语";
		return code || "";
	}

	function boot(){
		filllang();
		applylang();
		var params = new URLSearchParams(location.search);
		if (params.get("id")) wantId = parseInt(params.get("id"), 10) || 0;
		$("q").value = params.get("q") || "";
		$("q").addEventListener("input", queue);
		$("filts").addEventListener("click", onfilt);
		$("hits").addEventListener("click", onhit);
		$("speak").addEventListener("click", function(){
			if (!cur) return;
			speak(cur.speak || cur.word, cur.dict);
		});
		$("export").addEventListener("click", doexport);
		$("detail").addEventListener("mousedown", function(ev){
			downX = ev.clientX;
			downY = ev.clientY;
			drag = false;
		});
		$("detail").addEventListener("mousemove", function(ev){
			if (ev.buttons !== 1) return;
			if (Math.abs(ev.clientX - downX) + Math.abs(ev.clientY - downY) > 6) drag = true;
		});
		$("detail").addEventListener("mouseup", function(){
			if (!drag) return;
			setTimeout(openpop, 0);
		});
		$("detail").addEventListener("dblclick", function(){ setTimeout(openpop, 0); });
		$("detail").addEventListener("contextmenu", function(ev){
			ev.preventDefault();
			openpop();
		});
		$("detail").addEventListener("click", ondetail);
		$("pop-hits").addEventListener("click", onpophit);
		$("pop-speak").addEventListener("click", function(){ speak(selText, selLang); });
		$("pop-search").addEventListener("click", function(){
			hidepop();
			if (selText) window.open("/dict?q=" + encodeURIComponent(selText), "_blank");
		});
		$("pop-tr").addEventListener("click", function(){
			hidepop();
			if (!selText) return;
			try { sessionStorage.setItem("sk.dict.tr", selText); } catch (e) {}
			window.open("/#tr", "_blank");
		});
		$("pop-copy").addEventListener("click", function(){
			if (!selText || !navigator.clipboard) return;
			navigator.clipboard.writeText(selText);
		});
		document.addEventListener("mousedown", function(ev){
			var pop = $("pop");
			if (pop.hidden) return;
			if (pop.contains(ev.target)) return;
			hidepop();
		});
		document.addEventListener("keydown", function(ev){
			if (ev.key === "Escape") hidepop();
		});
		if ($("q").value) queue();
		else status(t("dict.ready") || "词典已就绪");
		$("q").focus();
	}

	function filllang(){
		var sel = $("lang");
		var codes = langs();
		var names = (window.L && L._name) || [];
		var i, op;
		sel.innerHTML = "";
		for (i = 0; i < codes.length; i++) {
			op = document.createElement("option");
			op.value = codes[i];
			op.textContent = names[i] || codes[i];
			sel.appendChild(op);
		}
		var use = String(window.SKL || "").toLowerCase();
		langAt = 0;
		for (i = 0; i < codes.length; i++) if (use === codes[i]) langAt = i;
		sel.value = codes[langAt] || codes[0] || "";
		sel.onchange = function(){
			var code = sel.value;
			langAt = 0;
			for (i = 0; i < codes.length; i++) if (codes[i] === code) langAt = i;
			applylang();
			painthits();
			if (cur) paint(cur);
		};
	}

	function applylang(){
		var nodes = document.querySelectorAll("[data-k]");
		var i, s;
		for (i = 0; i < nodes.length; i++) {
			s = t(nodes[i].getAttribute("data-k"));
			if (s) nodes[i].textContent = s;
		}
		nodes = document.querySelectorAll("[data-ktip]");
		for (i = 0; i < nodes.length; i++) {
			s = t(nodes[i].getAttribute("data-ktip"));
			if (s) nodes[i].title = s;
		}
		document.title = t("dict.title") || "词典";
		document.documentElement.lang = (langs()[langAt] || "zh") === "zh" ? "zh-CN" : (langs()[langAt] || "zh");
	}

	function onfilt(ev){
		var b = ev.target.closest("button");
		if (!b) return;
		var buttons = $("filts").querySelectorAll("button");
		var i;
		for (i = 0; i < buttons.length; i++) buttons[i].classList.toggle("on", buttons[i] === b);
		filter = b.getAttribute("data-dict") || "";
		wantId = 0;
		queue();
	}

	function queue(){
		if (timer) clearTimeout(timer);
		timer = setTimeout(run, 180);
	}

	function run(){
		timer = 0;
		var q = $("q").value || "";
		if (!String(q).replace(/^\s+|\s+$/g, "")) {
			gen++;
			hits = [];
			cur = null;
			$("hits").innerHTML = "";
			$("detail").innerHTML = "";
			hidepop();
			status(t("dict.ready") || "词典已就绪");
			return;
		}
		var g = ++gen;
		status(t("dict.searching") || "正在查询…");
		var url = "/api/dict?q=" + encodeURIComponent(q) + "&dict=" + encodeURIComponent(filter);
		fetch(url).then(function(r){ return r.json(); }).then(function(j){
			if (g !== gen) return;
			if (!j || j.code !== 100) {
				fail(j);
				return;
			}
			hits = (j.data && j.data.hits) || [];
			var ms = j.data && j.data.ms || 0;
			painthits();
			status(fmt(t("dict.count") || "{0} 条 · {1} ms", hits.length, ms));
			if (!hits.length) {
				cur = null;
				$("detail").innerHTML = "";
				return;
			}
			var pick = 0;
			var i;
			if (wantId) {
				for (i = 0; i < hits.length; i++) if (hits[i].id === wantId) { pick = i; break; }
				wantId = 0;
			}
			select(pick);
		}).catch(function(ex){
			if (g === gen) status(ex.message || String(ex));
		});
	}

	function fail(j){
		var key = j && j.error;
		if (key === "missing") status(t("dict.missing") || (j && j.data) || "");
		else if (key === "sqlite") status(t("dict.nosqlite") || (j && j.data) || "");
		else status((j && j.data) || "");
	}

	function preview(h){
		var text = h.preview || "";
		if (h.via) {
			var via = fmt(t("dict.via") || "由 {0} 还原", h.via);
			text = text ? text + "  ·  " + via : via;
		}
		return text;
	}

	function dictlab(code){
		if (code === "zh") return t("dict.lab.zh") || "中";
		if (code === "en") return t("dict.lab.en") || "英";
		if (code === "ja") return t("dict.lab.ja") || "日";
		if (code === "ko") return t("dict.lab.ko") || "韩";
		return "";
	}
	function hittitle(h){
		var title = (h && (h.title || h.word)) || "";
		if ((langs()[langAt] || "zh") !== "zh") return title;
		if (title.charAt(0) !== "[") return title;
		var end = title.indexOf("] ");
		if (end < 2 || end > 6) return title;
		var lab = dictlab(title.substring(1, end));
		if (!lab) return title;
		return "[" + lab + "] " + title.substring(end + 2);
	}
	function painthits(){
		var box = $("hits");
		box.innerHTML = "";
		var i, li, txt, b, span, btn;
		for (i = 0; i < hits.length; i++) {
			li = document.createElement("li");
			li.setAttribute("data-i", String(i));
			txt = document.createElement("div");
			txt.className = "hit-txt";
			b = document.createElement("b");
			b.textContent = hittitle(hits[i]);
			span = document.createElement("span");
			span.textContent = preview(hits[i]);
			txt.appendChild(b);
			txt.appendChild(span);
			btn = document.createElement("button");
			btn.type = "button";
			btn.className = "spk";
			btn.setAttribute("data-i", String(i));
			btn.textContent = "🔊";
			li.appendChild(txt);
			li.appendChild(btn);
			box.appendChild(li);
		}
	}

	function onhit(ev){
		var spk = ev.target.closest(".spk");
		if (spk) {
			ev.stopPropagation();
			var h = hits[parseInt(spk.getAttribute("data-i"), 10)];
			if (h) speak(h.speak || h.word, h.dict);
			return;
		}
		var li = ev.target.closest("li");
		if (!li) return;
		select(parseInt(li.getAttribute("data-i"), 10));
	}

	function select(i){
		var nodes = $("hits").children;
		var n;
		for (n = 0; n < nodes.length; n++) nodes[n].classList.toggle("on", n === i);
		if (nodes[i]) nodes[i].scrollIntoView({ block: "nearest" });
		var h = hits[i];
		if (!h) return;
		var g = ++dgen;
		fetch("/api/dict/entry?id=" + encodeURIComponent(h.id)).then(function(r){ return r.json(); }).then(function(j){
			if (g !== dgen) return;
			if (!j || j.code !== 100) { fail(j); return; }
			cur = j.data;
			hidepop();
			paint(cur);
		}).catch(function(ex){
			if (g === dgen) status(ex.message || String(ex));
		});
	}

	function paint(e){
		var box = $("detail");
		box.innerHTML = "";
		var tabs = document.createElement("div");
		tabs.className = "dtabs";
		box.appendChild(tabs);
		var panes = [];
		var i;

		function mk(label){
			var b = document.createElement("button");
			b.type = "button";
			b.className = "dtab";
			b.textContent = label;
			tabs.appendChild(b);
			var pn = document.createElement("div");
			pn.className = "dpane";
			pn.hidden = panes.length > 0;
			box.appendChild(pn);
			panes.push({ b: b, p: pn, on: false });
			return panes.length - 1;
		}
		function show(n){
			var k;
			for (k = 0; k < panes.length; k++) {
				panes[k].p.hidden = k !== n;
				panes[k].b.classList.toggle("on", k === n);
			}
			box.scrollTop = 0;
		}
		var cats = concats(e);
		var first = mk(t("dict.tab.info") || "词条详情");
		paintinfo(panes[first].p, e);
		for (i = 0; i < cats.length; i++) mk(conlabel(cats[i]));
		for (i = 0; i < panes.length; i++) {
			(function(n){
				panes[n].b.onclick = function(){
					show(n);
					if (n > 0 && !panes[n].on) {
						panes[n].on = true;
						loadconj(panes[n].p, e, cats[n - 1]);
					}
				};
			})(i);
		}
		show(0);
	}

	// 词条有哪些能变形的词性：动词、形容词、名词。日语的动词还分五段/一段/サ変/カ変。
	function concats(e){
		var lang = e.dict || "";
		var texts = [];
		var i, out = [];
		if (e.pos) texts.push(e.pos);
		for (i = 0; e.senses && i < e.senses.length; i++) if (e.senses[i].pos) texts.push(e.senses[i].pos);

		function put(v){
			var k;
			for (k = 0; k < out.length; k++) {
				if (out[k].cat !== v.cat) continue;
				if (!out[k].kind && v.kind) { out[k].kind = v.kind; out[k].sub = v.sub; }
				return;
			}
			out.push(v);
		}
		for (i = 0; i < texts.length; i++) {
			var s = texts[i];
			if (lang === "ja") {
				if (/kuru verb/i.test(s)) put({ cat: "v", kind: "3", sub: "kuru" });
				else if (/suru verb|aux\. verb suru/i.test(s)) put({ cat: "v", kind: "3", sub: "suru" });
				else if (/ichidan verb/i.test(s)) put({ cat: "v", kind: "2", sub: "" });
				else if (/godan verb/i.test(s)) put({ cat: "v", kind: "1", sub: "" });
				if (/adjectival nouns/i.test(s)) put({ cat: "a", kind: "an", sub: "" });
				else if (/adjective/i.test(s)) put({ cat: "a", kind: "ai", sub: "" });
				if (/\bnoun\b/i.test(s)) put({ cat: "n", kind: "n", sub: "" });
			}
			else if (lang === "ko") {
				if (/verb|동사/i.test(s)) put({ cat: "v" });
				if (/adjective|형용사/i.test(s)) put({ cat: "a" });
				if (/noun|명사/i.test(s)) put({ cat: "n" });
			}
			else if (lang === "en") {
				var paren = /^\s*\(/.test(s);
				var part = /past|participle|present/i.test(s);
				if (/\bverb\b/i.test(s) || (paren && part)) put({ cat: "v" });
				if (/\badjective\b/i.test(s) || (paren && !part)) put({ cat: "a" });
				if (/\bnoun\b/i.test(s)) put({ cat: "n" });
			}
		}
		return out;
	}

	function conlabel(cat){
		if (cat.cat === "v") return t("dict.tab.verb") || "动词变化";
		if (cat.cat === "a") return t("dict.tab.adj") || "形容词变化";
		return t("dict.tab.noun") || "名词变化";
	}

	// 形态变化页：拉 /api/conj，表格按组显示，上面带一个过滤框。
	function loadconj(pane, e, cat){
		pane.innerHTML = "";
		var bar = document.createElement("div");
		bar.className = "cbar";
		var q = document.createElement("input");
		q.type = "search";
		q.className = "cq";
		q.placeholder = t("dict.conj.search") || "搜索形态变化";
		var cnt = document.createElement("span");
		cnt.className = "ccnt";
		bar.appendChild(q);
		bar.appendChild(cnt);
		pane.appendChild(bar);
		var host = document.createElement("div");
		pane.appendChild(host);

		var lang = e.dict || "";
		// 变形用表记的第一个（日语通常只写假名的词条就是假名）
		var word = conjbase((e.forms && e.forms[0]) || e.word || "");
		var params = { lang: lang, q: word, ui: langs()[langAt] || "zh" };
		if (lang === "ja") {
			if (cat.kind) params.kind = cat.kind;
			if (cat.sub) params.sub = cat.sub;
			if (cat.kind === "3" && cat.sub === "suru" && !/する$/.test(word)) params.q = word + "する";
		}
		else params.type = cat.cat;

		var g = ++cgen;
		fetch("/api/conj?" + qs(params)).then(function(r){ return r.json(); }).then(function(j){
			if (g !== cgen) return;
			var d = j && j.code === 100 ? j.data : null;
			if (!d || !d.ok || !(d.rows || []).length) {
				host.appendChild(one((d && d.err) || t("dict.conj.fail") || "算不出形态变化", "pron", ""));
				return;
			}
			var rows = d.rows;
			var meta = document.createElement("p");
			meta.className = "cmeta";
			meta.textContent = conjmeta(d);
			pane.insertBefore(meta, bar);
			function draw(){
				var k = String(q.value || "").toLowerCase();
				var vis = [], i2;
				for (i2 = 0; i2 < rows.length; i2++) {
					var r = rows[i2];
					if (k && (r.l + " " + r.v).toLowerCase().indexOf(k) < 0) continue;
					vis.push(r);
				}
				cnt.textContent = fmt(t("dict.conj.count") || "{0} / {1} 条", vis.length, rows.length);
				host.innerHTML = "";
				if (!vis.length) {
					host.appendChild(one(t("dict.conj.empty") || "没有匹配的形态", "pron", ""));
					return;
				}
				var tb = document.createElement("table");
				tb.className = "ctab";
				var n = conjcols(host), last = null, buf = [], i3;
				function flush(){
					while (buf.length) {
						var tr = document.createElement("tr");
						for (var c = 0; c < n; c++) tr.appendChild(buf.shift() || conjblank());
						tb.appendChild(tr);
					}
				}
				for (i3 = 0; i3 < vis.length; i3++) {
					if ((vis[i3].g || "") !== last) {
						flush();
						last = vis[i3].g || "";
						if (last) {
							var htr = document.createElement("tr");
							var htd = document.createElement("td");
							htd.className = "cgrp";
							htd.colSpan = n;
							htd.textContent = last;
							htr.appendChild(htd);
							tb.appendChild(htr);
						}
					}
					buf.push(conjcell(vis[i3], lang));
				}
				flush();
				host.appendChild(tb);
			}
			conjdraw = draw;
			q.addEventListener("input", draw);
			draw();
		}).catch(function(){
			if (g === cgen) pane.appendChild(one(t("dict.conj.fail") || "算不出形态变化", "pron", ""));
		});
	}

	// 词条头可能带 ¹²³ 之类的序号，变形要用干净的词形。
	function conjbase(s){
		return String(s || "").replace(/[⁰¹²³⁴⁵⁶⁷⁸⁹₀-₉①-⑳㉑-㉟]+$/, "").trim();
	}

	// 一行里并列多个例句时按分隔符拆开：偶数位是例句，奇数位是原样的分隔符。
	var CONJSEPS = ["\n", "、", " · ", " / "];

	function conjparts(v){
		var s = String(v || ""), parts = [], start = 0, i = 0, j, len, sep;
		while (i < s.length) {
			len = 0;
			for (j = 0; j < CONJSEPS.length; j++) {
				sep = CONJSEPS[j];
				if (s.substr(i, sep.length) === sep) { len = sep.length; break; }
			}
			if (!len) { i++; continue; }
			parts.push(s.slice(start, i));
			parts.push(s.substr(i, len));
			i += len;
			start = i;
		}
		parts.push(s.slice(start));
		return parts;
	}

	// 读这句时用的文字：去掉「肯定：」这类只用于显示的前缀。
	function conjsay(s){
		var t = String(s || "").trim();
		if (!t) return "";
		var c = t.lastIndexOf("：");
		if (c >= 0 && c + 1 < t.length) return t.slice(c + 1).trim();
		c = t.lastIndexOf(": ");
		if (c >= 0 && c + 2 < t.length) return t.slice(c + 2).trim();
		return t;
	}

	// 一格（表格的一个 td）：形态名一行，下面每条例句一行；并列的例句竖排，每句尾巴一个喇叭。
	function conjcell(r, lang){
		var td = document.createElement("td");
		td.className = "c-k";
		var lab = document.createElement("div");
		lab.className = "clab";
		lab.textContent = r.l || "";
		td.appendChild(lab);
		var segs = conjparts(r.v), i, s, sy, b;
		for (i = 0; i < segs.length; i += 2) {
			s = String(segs[i] || "").trim();
			if (!s) continue;
			var line = document.createElement("div");
			line.className = "cex";
			line.appendChild(document.createTextNode(s));
			sy = conjsay(s);
			if (sy) {
				b = document.createElement("button");
				b.type = "button";
				b.className = "spk sm";
				b.setAttribute("data-say", sy);
				b.setAttribute("data-lang", lang);
				b.textContent = "🔊";
				line.appendChild(b);
			}
			td.appendChild(line);
		}
		return td;
	}

	// 一行没填满时补空单元格，横线才能从左贯通到右。
	function conjblank(){
		var td = document.createElement("td");
		td.className = "c-k";
		return td;
	}

	// 按可用宽度排 2～3 栏（至少 2 栏）；窗口宽度变了重排。
	var conjdraw = null, ctimer = 0;

	function conjcols(host){
		var w = host.clientWidth;
		if (!w) w = host.parentNode ? host.parentNode.clientWidth : 0;
		return w >= 870 ? 3 : 2;
	}

	window.addEventListener("resize", function(){
		if (!conjdraw) return;
		clearTimeout(ctimer);
		ctimer = setTimeout(function(){ if (conjdraw) conjdraw(); }, 150);
	});

	// 形态页顶上那行：实际判定的类型和特殊变化（1类/2类/3类、い/な形容词、不规则说明）。
	function conjmeta(d){
		var parts = [];
		var name = d.name || "";
		if (name && name !== "v" && name !== "a" && name !== "n") parts.push(name);
		if (!d.note && d.irr) parts.push(d.irr);
		if (d.note) parts.push(d.note);
		return parts.join(" · ");
	}

	function qs(params){
		var out = [], k;
		for (k in params) {
			if (params[k] === undefined || params[k] === null || params[k] === "") continue;
			out.push(k + "=" + encodeURIComponent(params[k]));
		}
		return out.join("&");
	}

	function paintinfo(box, e){
		var word = e.word || "";
		var lang = e.dict || "en";
		var head = p("title");
		// 日语的表记按服务端给的顺序（通常只写假名的词条假名在前），其它语言就一个词
		var forms = e.forms && e.forms.length ? e.forms : [word];
		for (var fi = 0; fi < forms.length; fi++) {
			if (!forms[fi]) continue;
			add(head, (fi ? "  " : "") + forms[fi], "", lang);
		}
		box.appendChild(head);
		var reading = e.pron || e.reading || "";
		if (reading) box.appendChild(one(bracket(reading), "pron", lang));
		var article = lang === "zh" && e.extra;
		if (e.pos) box.appendChild(one(e.pos, "pos", guess(e.pos, lang)));
		if (!article) {
			if (e.usage && e.usage.length) {
				var tags = p("");
				add(tags, (t("dict.lab.tags") || "标签："), "lab", "");
				add(tags, e.usage.join(" · "), "", guess(e.usage.join(" · "), lang));
				box.appendChild(tags);
			}
			if (e.etymology) {
				box.appendChild(one(t("dict.lab.etym") || "语源", "lab", ""));
				eachline(e.etymology, function(line){ box.appendChild(one(line, "", guess(line, lang))); });
			}
			var ci;
			for (ci = 0; e.conjugations && ci < e.conjugations.length; ci++) {
				var cp = p("");
				add(cp, t("dict.lab.conj") || "活用：", "lab", "");
				add(cp, e.conjugations[ci], "", lang);
				box.appendChild(cp);
			}
			if (e.see && e.see.length) {
				var sp = p("");
				add(sp, (t("dict.lab.see") || "参见") + " ", "lab", "");
				var si, first = true;
				for (si = 0; si < e.see.length; si++) {
					if (!e.see[si]) continue;
					if (!first) add(sp, " · ", "", "");
					first = false;
					link(sp, e.see[si], e.see[si], lang);
				}
				box.appendChild(sp);
			}
			var n = 1;
			var lastpos = "";
			var any = false;
			var linkphrase = lang !== "ko";
			var senses = e.senses || [];
			var k;
			for (k = 0; k < senses.length; k++) {
				var s = senses[k];
				if (s.idiom) {
					if (any) box.appendChild(p("gap"));
					var ip = p("");
					add(ip, (t("dict.lab.phrase") || "词组") + " ", "lab", "");
					if (linkphrase) link(ip, s.idiom, firsttoken(s.idiom), lang);
					else add(ip, s.idiom, "", lang);
					box.appendChild(ip);
				}
				else if (any && !s.sub) box.appendChild(p("gap"));
				any = true;
				if (s.pos && s.pos !== e.pos && s.pos !== lastpos) {
					box.appendChild(one("▶ " + s.pos, "pos", guess(s.pos, lang)));
					lastpos = s.pos;
					n = 1;
				}
				var numbered = false;
				function gloss(tag, lemma, def, glang){
					if (!lemma && !def) return;
					var gp = p("");
					if (!numbered && !s.sub) {
						add(gp, n + ". ", "num", "");
						numbered = true;
						n++;
					}
					if (tag) add(gp, tag + " ", "lab", glang);
					add(gp, join2(lemma, def), "", glang);
					box.appendChild(gp);
				}
				if (s.ko) gloss("", s.ko, "", "ko");
				gloss(t("dict.lab.zh") || "中", s.zh, s.zhDef, "zh");
				gloss(t("dict.lab.en") || "英", s.en, s.enDef, "en");
				gloss(t("dict.lab.ja") || "日", s.ja, s.jaDef, "ja");
				var pi;
				for (pi = 0; s.phrases && pi < s.phrases.length; pi++) {
					var ph = s.phrases[pi];
					var pp = p("");
					add(pp, (t("dict.lab.phrase") || "词组") + " ", "lab", "");
					if (linkphrase && ph.text) link(pp, ph.text, firsttoken(ph.text), lang);
					else add(pp, ph.text || "", "", lang);
					if (!linkphrase) {
						var say = examplesrc(ph.text);
						if (say) spk(pp, say, lang);
					}
					box.appendChild(pp);
					if (ph.zh) {
						var zp = p("");
						add(zp, (t("dict.lab.zh") || "中") + " ", "lab", "zh");
						add(zp, ph.zh, "", "zh");
						box.appendChild(zp);
					}
				}
				if (s.sentences && s.sentences.length)
					box.appendChild(one(t("dict.lab.example") || "例句", "exlab", ""));
				var ei;
				for (ei = 0; s.sentences && ei < s.sentences.length; ei++) {
					var ex = s.sentences[ei];
					var ep = p("ex");
					add(ep, "· " + (ex.text || ""), "ex", lang);
					var esay = examplesrc(ex.text);
					if (esay) spk(ep, esay, lang);
					box.appendChild(ep);
					if (ex.zh) {
						var ez = p("");
						add(ez, (t("dict.lab.zh") || "中") + " ", "lab", "zh");
						add(ez, ex.zh, "", "zh");
						box.appendChild(ez);
					}
				}
				if (!s.sub && !numbered && (!s.phrases || !s.phrases.length) && (!s.sentences || !s.sentences.length)) {
					var empty = p("");
					add(empty, n + ". ", "num", "");
					n++;
					add(empty, t("dict.empty.sense") || "（无释义）", "pron", "");
					box.appendChild(empty);
				}
				else if (!s.sub && !numbered) {
					box.appendChild(one(n + ".", "num", ""));
					n++;
				}
			}
		}
		if (e.extra && (article || lang !== "zh")) {
			if (box.childNodes.length) box.appendChild(p("gap"));
			eachline(e.extra, function(line){
				box.appendChild(one(line, "", article ? "zh" : guess(line, lang)));
			});
		}
		box.scrollTop = 0;
	}

	function p(cls){
		var el = document.createElement("p");
		if (cls) el.className = cls;
		return el;
	}
	function one(text, cls, lang){
		var el = p(cls);
		add(el, text, cls, lang);
		return el;
	}
	function add(parent, text, cls, lang){
		if (!text) return;
		var s = document.createElement("span");
		if (cls) s.className = cls;
		if (lang) s.setAttribute("data-lang", lang);
		s.textContent = text;
		parent.appendChild(s);
	}
	function link(parent, label, q, lang){
		var b = document.createElement("button");
		b.type = "button";
		b.className = "link";
		b.setAttribute("data-q", q || label);
		if (lang) b.setAttribute("data-lang", lang);
		b.textContent = label;
		parent.appendChild(b);
	}
	function spk(parent, text, lang){
		parent.appendChild(document.createTextNode(" "));
		var b = document.createElement("button");
		b.type = "button";
		b.className = "spk";
		b.setAttribute("data-say", text);
		b.setAttribute("data-lang", lang || "");
		b.textContent = "🔊";
		parent.appendChild(b);
	}
	function ondetail(ev){
		var say = ev.target.closest(".spk");
		if (say) {
			speak(say.getAttribute("data-say"), say.getAttribute("data-lang"));
			return;
		}
		var b = ev.target.closest(".link");
		if (!b) return;
		var q = b.getAttribute("data-q") || "";
		if (q) window.open("/dict?q=" + encodeURIComponent(q), "_blank");
	}

	function openpop(){
		var text = "";
		var sel = window.getSelection();
		if (sel && !sel.isCollapsed) text = String(sel.toString() || "");
		text = oneline(text);
		if (!text) return;
		selText = text.length > 80 ? text.substring(0, 80) : text;
		selLang = langof(sel);
		var pop = $("pop");
		pop.hidden = false;
		$("pop-wait").hidden = false;
		$("pop-hits").innerHTML = "";
		placepop(sel);
		var g = ++sgen;
		var q = selText;
		fetch("/api/dict?q=" + encodeURIComponent(q) + "&limit=8").then(function(r){ return r.json(); }).then(function(j){
			if (g !== sgen || pop.hidden) return;
			var rows = (j && j.code === 100 && j.data && j.data.hits) || [];
			if (rows.length > 8) rows = rows.slice(0, 8);
			$("pop-wait").hidden = rows.length > 0;
			var box = $("pop-hits");
			var i, li, b, span;
			for (i = 0; i < rows.length; i++) {
				li = document.createElement("li");
				li.setAttribute("data-word", rows[i].word || "");
				li.setAttribute("data-id", String(rows[i].id || ""));
				b = document.createElement("b");
				b.textContent = hittitle(rows[i]);
				span = document.createElement("span");
				span.textContent = rows[i].preview || "";
				li.appendChild(b);
				li.appendChild(span);
				box.appendChild(li);
			}
			placepop(window.getSelection());
		}).catch(function(){});
	}

	function placepop(sel){
		var pop = $("pop");
		var rect = null;
		try {
			if (sel && sel.rangeCount) rect = sel.getRangeAt(0).getBoundingClientRect();
		}
		catch (e) { rect = null; }
		if (!rect || (!rect.width && !rect.height)) rect = { left: 24, bottom: 48, top: 24, width: 1, height: 16 };
		var gap = 6;
		var y = rect.bottom + gap;
		if (y + pop.offsetHeight > window.innerHeight - 8) y = Math.max(8, rect.top - pop.offsetHeight - gap);
		var x = rect.left;
		if (x + pop.offsetWidth > window.innerWidth - 8) x = window.innerWidth - pop.offsetWidth - 8;
		if (x < 8) x = 8;
		pop.style.left = x + "px";
		pop.style.top = y + "px";
	}

	function hidepop(){ $("pop").hidden = true; }

	function onpophit(ev){
		var li = ev.target.closest("li");
		if (!li) return;
		var word = li.getAttribute("data-word") || "";
		var id = li.getAttribute("data-id") || "";
		hidepop();
		if (!word) return;
		var url = "/dict?q=" + encodeURIComponent(word);
		if (id) url += "&id=" + encodeURIComponent(id);
		window.open(url, "_blank");
	}

	function speak(text, lang){
		text = text || "";
		if (!String(text).replace(/^\s+|\s+$/g, "")) {
			status(lang === "zh" ? (t("dict.nozh") || "这条没有中文释义") : (t("dict.noword") || "没有可朗读的词头"));
			return;
		}
		var url = "/api/dict/speak?lang=" + encodeURIComponent(lang || "en") + "&text=" + encodeURIComponent(text);
		fetch(url).then(function(r){
			var ct = r.headers.get("content-type") || "";
			if (ct.indexOf("audio") < 0) return r.json().then(function(j){ throw j || {}; });
			return r.blob();
		}).then(function(b){
			if (audioUrl) URL.revokeObjectURL(audioUrl);
			audioUrl = URL.createObjectURL(b);
			var a = new Audio(audioUrl);
			a.play();
			status(fmt(t("dict.speaking") || "正在朗读（{0}）", langname(lang)));
		}).catch(function(j){
			if (j && j.error === "novoice")
				status(fmt(t("dict.novoice") || "无法用 {0} 发音", langname(lang)));
			else
				status((j && j.data) || (j && j.message) || "");
		});
	}

	function doexport(){
		var q = $("q").value || "";
		if (!String(q).replace(/^\s+|\s+$/g, "")) {
			status(t("dict.export.empty") || "先输入要搜索的词");
			return;
		}
		status(t("dict.export.search") || "正在检索全部结果…");
		var kind = $("fmt").value || "txt";
		var url = "/api/dict/export?q=" + encodeURIComponent(q) + "&dict=" + encodeURIComponent(filter) + "&fmt=" + encodeURIComponent(kind);
		fetch(url).then(function(r){
			var ct = r.headers.get("content-type") || "";
			if (ct.indexOf("json") >= 0) return r.json().then(function(j){ throw j || {}; });
			var n = r.headers.get("X-Dict-Count") || "";
			return r.blob().then(function(b){ return { b: b, n: n }; });
		}).then(function(got){
			var a = document.createElement("a");
			a.href = URL.createObjectURL(got.b);
			a.download = filename(q, kind);
			document.body.appendChild(a);
			a.click();
			a.remove();
			URL.revokeObjectURL(a.href);
			status(fmt(t("dict.export.done") || "已导出 {0} 条", got.n));
		}).catch(function(j){
			status((j && j.data) || (j && j.message) || (t("dict.export.fail") || "导出失败"));
		});
	}

	function filename(q, kind){
		var s = String(q || "").replace(/^\s+|\s+$/g, "");
		var out = "";
		var i, c;
		for (i = 0; i < s.length && out.length < 40; i++) {
			c = s.charAt(i);
			if (c < " " || "\\/:*?\"<>|".indexOf(c) >= 0) continue;
			out += c;
		}
		if (!out) out = "dict";
		return out + "." + (kind || "txt");
	}

	function langof(sel){
		var allowJa = cur && cur.dict === "ja";
		var score = [0, 0, 0, 0];
		var untagged = "";
		var any = false;
		try {
			if (sel && sel.rangeCount) {
				var range = sel.getRangeAt(0);
				var root = range.commonAncestorContainer;
				var nodes = [];
				if (root.nodeType === 1) nodes = root.querySelectorAll("[data-lang]");
				else if (root.parentElement) nodes = [root.parentElement];
				var i;
				for (i = 0; i < nodes.length; i++) {
					if (!range.intersectsNode(nodes[i])) continue;
					var bit = nodes[i].textContent || "";
					var idx = langindex(nodes[i].getAttribute("data-lang"));
					if (idx >= 0 && (allowJa || idx !== 2)) { score[idx] += bit.length; any = true; }
				}
			}
		}
		catch (e) {}
		addscript(score, any ? untagged : selText, allowJa);
		return bestlang(score, (cur && cur.dict) || "en");
	}
	function langindex(lang){
		if (lang === "zh") return 0;
		if (lang === "en") return 1;
		if (lang === "ja") return 2;
		if (lang === "ko") return 3;
		return -1;
	}
	function addscript(score, text, allowJa){
		if (!text) return;
		var han = 0, kana = 0, hang = 0, lat = 0, i, c;
		for (i = 0; i < text.length; i++) {
			c = text.charCodeAt(i);
			if ((c >= 0x3040 && c <= 0x30FF) || (c >= 0xFF66 && c <= 0xFF9D)) kana++;
			else if (c >= 0xAC00 && c <= 0xD7A3) hang++;
			else if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF)) han++;
			else if ((c >= 65 && c <= 90) || (c >= 97 && c <= 122)) lat++;
		}
		if (kana > 0 && allowJa) score[2] += kana + han;
		else score[0] += han;
		score[3] += hang;
		score[1] += lat;
	}
	function bestlang(score, fallback){
		var names = ["zh", "en", "ja", "ko"];
		var best = -1, n = 0, i;
		for (i = 0; i < 4; i++) if (score[i] > n) { n = score[i]; best = i; }
		if (best < 0) return fallback || "en";
		return names[best];
	}
	function guess(text, fallback){
		var score = [0, 0, 0, 0];
		addscript(score, text, fallback === "ja");
		return bestlang(score, fallback);
	}
	function bracket(s){
		if (!s) return s;
		if (s.charAt(0) === "[" || s.charAt(0) === "［") return s;
		return "[" + s + "]";
	}
	function join2(a, b){
		if (!a) return b || "";
		if (!b) return a;
		return a + "  " + b;
	}
	function examplesrc(text){
		var s = String(text || "").replace(/^\s+|\s+$/g, "").replace(/^·/, "").replace(/^\s+/, "");
		var i = s.indexOf("  ");
		if (i > 0) s = s.substring(0, i).replace(/\s+$/g, "");
		return s;
	}
	function firsttoken(phrase){
		var s = String(phrase || "").replace(/^\s+|\s+$/g, "");
		var i = s.indexOf(" ");
		var j = s.indexOf("　");
		if (i < 0 || (j >= 0 && j < i)) i = j;
		if (i <= 0) return s;
		return s.substring(0, i);
	}
	function eachline(text, fn){
		var parts = String(text || "").replace(/\r\n/g, "\n").replace(/\r/g, "\n").split("\n");
		var i;
		for (i = 0; i < parts.length; i++) fn(parts[i]);
	}
	function oneline(text){
		var s = String(text || "").replace(/🔊/g, "");
		var out = "";
		var sp = false;
		var i, c;
		for (i = 0; i < s.length; i++) {
			c = s.charAt(i);
			if (c === "\r" || c === "\n" || c === "\t" || c === " ") {
				sp = out.length > 0;
				continue;
			}
			if (sp) { out += " "; sp = false; }
			out += c;
		}
		return out;
	}

	if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot);
	else boot();
})();
