(function(){
	// 动词变位类工具（英语/日语/韩语/俄语）共用的结果区：分栏排版、发音、计时。
	// 各语言只提供单元格内容、分组取法和发音语言，排版和窗口适配逻辑只留这一份。
	// spec: outId、lang、tabcls、nospeak:[键, 兜底]、groupof(row)、cell(row, tip)。
	// 释义：示例词用示例里那一条，其它词查词典；多个义项用「; 」并成一行。
	SK.verbmean = function(dict, word, gloss, done){
		gloss = String(gloss || "").trim();
		if (gloss) { done(gloss); return; }
		// 词典只有中日韩英四种，其它语言只能用示例里的释义。
		if (!/^(zh|en|ja|ko)$/.test(String(dict || "")) || !word) { done(""); return; }
		var q = "/api/dict?limit=8&dict=" + encodeURIComponent(dict) + "&q=" + encodeURIComponent(word);
		reqjson(q, function(jo){
			var hits = jo && jo.code === 100 && jo.data && jo.data.hits ? jo.data.hits : [];
			SK.verbpick(hits, word, function(h){
				if (!h || !h.id) { done(""); return; }
				reqjson("/api/dict/entry?id=" + encodeURIComponent(h.id), function(ej){
					var e = ej && ej.code === 100 ? ej.data : null;
					done(shorten(senses(e) || h.preview, 100));
				});
			});
		});
	};

	// 同一批搜索结果里挑出这个词本身：词头相同优先，其次词头以它开头的。
	function pickhits(hits, word){
		var low = String(word || "").toLowerCase();
		var same = [], pre = [];
		var i, h, w;
		for (i = 0; hits && i < hits.length; i++) {
			h = hits[i];
			w = String((h && h.word) || "").toLowerCase();
			if (w === low) same.push(h);
			else if (w.indexOf(low) === 0) pre.push(h);
		}
		return same.length ? same : pre;
	}

	// 同形词条（먹다¹ 聋 / 먹다² 吃）取义项最多的那条，生僻的同形词条排在前面也不会被选中。
	SK.verbpick = function(hits, word, done){
		var list = pickhits(hits, word).slice(0, 3);
		if (!list.length) { done(null); return; }
		if (list.length === 1) { done(list[0]); return; }
		var best = list[0], bestn = -1, left = list.length, i;
		for (i = 0; i < list.length; i++) {
			(function(x){
				reqjson("/api/dict/entry?id=" + encodeURIComponent(x.id), function(ej){
					var n = ej && ej.code === 100 && ej.data && ej.data.senses ? ej.data.senses.length : 0;
					if (n > bestn) { bestn = n; best = x; }
					if (--left === 0) done(best);
				});
			})(list[i]);
		}
	};

	// 界面语言对应的那条短释义；同一个说法只留一次，最多五条再并成一行。
	function senses(e){
		var list = e && e.senses ? e.senses : [];
		var code = (SK.langcode ? SK.langcode() : "zh").substring(0, 2);
		var key = code === "en" ? "en" : code === "ja" ? "ja" : code === "ko" ? "ko" : "zh";
		var out = [];
		var i, s, t;
		for (i = 0; i < list.length && out.length < 5; i++) {
			s = list[i];
			if (!s) continue;
			t = String(s[key] || "").trim();
			if (!t && key !== "zh") t = String(s.zh || "").trim();
			if (!t || out.indexOf(t) >= 0) continue;
			out.push(t);
		}
		return out.join("; ");
	}

	// 释义太长会挤掉计时，截一下并加省略号。
	function shorten(t, max){
		t = String(t || "").trim();
		max = max || 100;
		return t.length > max ? t.substring(0, max).replace(/[，,、;；]$/, "") + "…" : t;
	}

	function reqjson(url, ok){
		var xhr = new XMLHttpRequest();
		xhr.open("GET", url, true);
		xhr.onload = function(){
			try { ok(JSON.parse(xhr.responseText)); }
			catch (e) { ok(null); }
		};
		xhr.onerror = function(){ ok(null); };
		xhr.send();
	}

	// 示例词表里同一行的释义，按界面语言取中文或英文；那一栏没写就返回空，让调用方去查词典。
	// 示例后面常跟「 · 」注的活用说明（听 · ㄷ、吃饭 · 带动词宾语），释义只取前半。
	SK.verbmeanrow = function(row, zhcol, encol){
		if (!row) return "";
		var en = (SK.langcode ? SK.langcode() : "zh").substring(0, 2) === "en";
		var t = en ? (encol >= 0 ? String(row[encol] || "") : "") : (zhcol >= 0 ? String(row[zhcol] || "") : "");
		var i = t.indexOf(" · ");
		return (i >= 0 ? t.substring(0, i) : t).trim();
	};

	SK.verbpickmean = function(picks, word, match, zhcol, encol){
		var low = String(word || "").toLowerCase();
		var i, p;
		for (i = 0; i < picks.length; i++) {
			p = picks[i];
			if (String(p[match || 1]).toLowerCase() !== low) continue;
			return SK.verbmeanrow(p, zhcol, encol);
		}
		return "";
	};

	SK.verbui = function(spec){
		var tx = SK.tx;
		var outId = spec.outId;
		var lang = spec.lang || "en";
		var tabcls = spec.tabcls || "jv-tab";
		var nospeak = spec.nospeak || ["web.verb.nospeak", "无法发音"];
		var groupof = spec.groupof;
		var cell = spec.cell;
		var block = spec.block;
		var CELLW = spec.cellw || 200;

		// 栏宽固定，窗口多宽就排几栏；只有 1、2 栏时整行均分。
		var clip = null;
		var lastpaint = null;
		var perline = 2;
		var tries = 0;
		var rtimer = 0;
		var bound = false;

		var ui = {
			paint: paint,
			render: render,
			refit: refit,
			onresize: onresize,
			bind: bind,
			speak: speak,
			dropspeak: dropspeak,
			onout: onout,
			timestr: timestr,
			fmtms: fmtms,
			now: now
		};
		return ui;

		function out(){
			var k = SK.domkey(outId);
			return (SK.curpage && SK.curpage.doms && SK.curpage.doms[k]) || null;
		}

		function paint(head, note, rows, tm, mean){
			var el = out();
			if (!el) return;
			if (!rows || !rows.length) {
				el.innerHTML = "";
				lastpaint = null;
				return;
			}
			lastpaint = { head: head, note: note, rows: rows, tm: tm, mean: mean || "" };
			render(el);
		}

		function tabopen(){
			var n = perline;
			var html = '<table class="' + tabcls + '" style="width:' + (n <= 2 ? "100%" : (n * CELLW) + "px") + '">';
			if (n > 2) {
				html += "<colgroup>";
				for (var i = 0; i < n; i++) html += '<col style="width:' + CELLW + 'px">';
				html += "</colgroup>";
			}
			return html;
		}

		function render(el){
			if (!el || !lastpaint) return;
			var cw = el.clientWidth;
			if (cw <= 0) {
				// 刚切到工具页时容器可能还没显示，等一帧再量。
				if (tries < 5) { tries++; SK.after(refit, 40); }
				return;
			}
			tries = 0;
			perline = Math.floor(cw / CELLW);
			if (perline < 1) perline = 1;
			if (perline > 8) perline = 8;
			el.innerHTML = tablehtml(lastpaint.head, lastpaint.note, lastpaint.rows, lastpaint.tm, lastpaint.mean);
		}

		function onresize(){
			if (rtimer) SK.cancelafter(rtimer);
			rtimer = SK.after(refit, 150);
		}

		function refit(){
			var el = out();
			if (el && lastpaint) render(el);
		}

		// 窗口大小变化只挂一次；面板隐藏再显示不会重复注册。
		function bind(){
			if (bound) return;
			bound = true;
			F.on(window, "resize", onresize);
		}

		function tablehtml(head, note, rows, tm, mean){
			var tip = SK.esc(tx("dict.speak", "发音"));
			var html = '<div class="jv-head"><b>' + SK.esc(head) + "</b>"
				+ "<span>" + SK.esc(note) + "</span>"
				+ (mean ? '<span class="jv-mean">' + SK.esc(mean) + "</span>" : "")
				+ (tm ? '<span class="jv-time">' + SK.esc(tm) + "</span>" : "") + "</div>" + tabopen();
			var g = null;
			var buf = [];
			var i, rg;
			for (i = 0; i < rows.length; i++) {
				rg = groupof(rows[i]);
				if (g === null || rg !== g) {
					html += flushrows(buf, tip);
					buf = [];
					g = rg;
					if (g) html += '<tr><td class="kv-g" colspan="' + perline + '">' + SK.esc(g) + "</td></tr>";
				}
				buf.push(rows[i]);
			}
			return html + flushrows(buf, tip) + "</table>";
		}

		// 分词变格表这类整块内容单独占满一行，不参与分栏。
		function flushrows(buf, tip){
			var html = "";
			var line = [];
			var j, k, b;
			for (j = 0; j < buf.length; j++) {
				b = block ? block(buf[j]) : "";
				if (b) {
					html += flushline(line, tip);
					line = [];
					html += '<tr><td class="kv-blk" colspan="' + perline + '">' + b + "</td></tr>";
					continue;
				}
				line.push(buf[j]);
				if (line.length >= perline) {
					html += flushline(line, tip);
					line = [];
				}
			}
			return html + flushline(line, tip);
		}

		function flushline(line, tip){
			if (!line || !line.length) return "";
			var html = "<tr>";
			for (var k = 0; k < perline; k++) {
				html += k < line.length ? cell(line[k], tip) : '<td class="c-k"></td>';
			}
			return html + "</tr>";
		}

		// 结果表里的喇叭按钮：取 data-v 的文本交给发音接口。
		function onout(ev){
			var btn = ev.target;
			var box = out();
			while (btn && btn !== box && btn.tagName !== "BUTTON") btn = btn.parentNode;
			if (!btn || btn === box) return;
			var text = F.attr(btn, "data-v") || "";
			if (text) speak(text);
		}

		function dropspeak(){
			if (SK.stopwav) SK.stopwav();
			if (!clip) return;
			try { clip.pause(); } catch (e) {}
			clip = null;
		}

		function speak(text){
			SK.stopwav();
			if (clip) {
				try { clip.pause(); } catch (e) {}
				clip = null;
			}
			var audio = new Audio("/api/dict/speak?text=" + encodeURIComponent(text) + "&lang=" + lang);
			clip = audio;
			audio.onerror = function(){
				if (clip === audio) SK.msg("box-msg", tx(nospeak[0], nospeak[1]), true);
			};
			var played = audio.play();
			if (played && played.then) played.then(function(){}, function(){
				if (clip === audio) SK.msg("box-msg", tx(nospeak[0], nospeak[1]), true);
			});
		}

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
	};
})();
