(function(){
	// 动词变位类工具（英语/日语/韩语/俄语）共用的结果区：分栏排版、发音、计时。
	// 各语言只提供单元格内容、分组取法和发音语言，排版和窗口适配逻辑只留这一份。
	// spec: outId、lang、tabcls、nospeak:[键, 兜底]、groupof(row)、cell(row, tip)。
	SK.verbui = function(spec){
		var tx = SK.tx;
		var outId = spec.outId;
		var lang = spec.lang || "en";
		var tabcls = spec.tabcls || "jv-tab";
		var nospeak = spec.nospeak || ["web.verb.nospeak", "无法发音"];
		var groupof = spec.groupof;
		var cell = spec.cell;
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
			return F.id(outId);
		}

		function paint(head, note, rows, tm){
			var el = out();
			if (!el) return;
			if (!rows || !rows.length) {
				el.innerHTML = "";
				lastpaint = null;
				return;
			}
			lastpaint = { head: head, note: note, rows: rows, tm: tm };
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
				if (tries < 5) { tries++; setTimeout(refit, 40); }
				return;
			}
			tries = 0;
			perline = Math.floor(cw / CELLW);
			if (perline < 1) perline = 1;
			if (perline > 8) perline = 8;
			el.innerHTML = tablehtml(lastpaint.head, lastpaint.note, lastpaint.rows, lastpaint.tm);
		}

		function onresize(){
			if (rtimer) clearTimeout(rtimer);
			rtimer = setTimeout(refit, 150);
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

		function tablehtml(head, note, rows, tm){
			var tip = SK.esc(tx("dict.speak", "发音"));
			var html = '<div class="jv-head"><b>' + SK.esc(head) + "</b><span>" + SK.esc(note) + "</span>"
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

		function flushrows(buf, tip){
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
