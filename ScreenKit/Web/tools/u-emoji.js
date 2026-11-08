(function(){
	var page = null;
	var cur = "all";
	var picked = "";
	var meta = null; // 每行的小写搜索文本：名字、关键词分词、码位

	SK.regutil("u-emoji", {
		title: "Emoji查询",
		body: function(t){
			page = t;
			return [
				F.CreateDOM(t, { x: "div.uquery", c: [
					{ x: "input.u-q", name: "emjQ", type: "text", "data-kph": "web.emj.ph", placeholder: "搜索：smile、笑、U+1F600" },
					{ x: "span.u-msg.msg", name: "emjMsg" }
				] }),
				F.CreateDOM(t, { x: "p.hint", "data-k": "web.emj.hint", c: "点一下选中并复制。左边按分类分组，中间按子分类铺开，右边是大字、中英文名和编码。" }),
				F.CreateDOM(t, { x: "p.hint.emjnofont", name: "emjNoFont", hidden: true }),
				F.CreateDOM(t, { x: "div.unitab", c: [
					{ x: "div.ugroups.u-groups", name: "emjGroups" },
					{ x: "div.ugrid.emjgrid", name: "emjGrid" },
					{ x: "aside.uprev", c: [
						{ x: "div.uprev-ch.emjch", name: "emjCh" },
						{ x: "dl.uprev-meta", name: "emjMeta" }
					] }
				] })
			];
		},
		run: function(){
			throw new Error("没有这个工具");
		},
		open: function(t){
			page = t;
			cur = "all";
			picked = "";
			page.doms.emjQ.value = "";
			page.doms.emjQ.oninput = function(){
				picked = "";
				paint();
			};
			page.doms.emjGroups.onclick = ongroup;
			page.doms.emjGrid.onclick = oncell;
			page.doms.emjCh.onclick = pickcopy;
			reset();
			paint();
			fontcheck();
			if (document.fonts && document.fonts.addEventListener)
				document.fonts.addEventListener("loadingdone", fontcheck);
		},
		hideGo: true,
		live: false,
		hideOut: true,
		relang: function(){
			paint();
			fontcheck();
		}
	});

	function data(){
		return SK.emojidata || { g: [], s: [], r: [] };
	}

	// 字体没装（安装包里不带 emoji.woff2）时提示去「安装功能」下载。
	function fontcheck(){
		var el = page && page.doms ? page.doms.emjNoFont : null;
		if (!el) return;
		var ok = false;
		if (document.fonts && document.fonts.forEach) {
			document.fonts.forEach(function(f){
				if (f.family && f.family.indexOf("SK Emoji") >= 0 && f.status === "loaded") ok = true;
			});
		}
		el.hidden = ok;
		if (!ok) el.textContent = tx("web.emj.nofont", "未安装 Emoji 字体，部分组合和新 emoji 会显示成两段或方框。请在「帮助 → 安装功能」里安装「Emoji 字体」。");
	}

	function mk(tag, cls, text){
		var n = document.createElement(tag);
		if (cls) n.className = cls;
		if (text != null) n.textContent = text;
		return n;
	}

	function tx(key, fb, n){
		var s = SK.t ? SK.t(key) : "";
		if (!s) s = fb || "";
		return n == null ? s : s.replace("{0}", String(n));
	}

	function iszh(){
		var code = String(SK.langcode ? SK.langcode() : "zh").substring(0, 2).toLowerCase();
		return code === "zh";
	}

	function gname(i){
		var g = data().g[i] || ["", ""];
		return iszh() ? g[1] || g[0] : g[0] || g[1];
	}

	function sname(i){
		var s = data().s[i] || ["", ""];
		return iszh() ? s[1] || s[0] : s[0] || s[1];
	}

	function gfull(i){
		var g = data().g[i] || ["", ""];
		if (!g[0] || !g[1] || g[0] === g[1]) return gname(i);
		return iszh() ? g[1] + " · " + g[0] : g[0] + " · " + g[1];
	}

	function sfull(i){
		var s = data().s[i] || ["", ""];
		if (!s[0] || !s[1] || s[0] === s[1]) return sname(i);
		return iszh() ? s[1] + " · " + s[0] : s[0] + " · " + s[1];
	}

	function names(r){
		if (!r[1]) return r[2] || "";
		if (!r[2] || r[1] === r[2]) return r[1];
		return r[1] + " / " + r[2];
	}

	function cps(em){
		var out = [];
		var i;
		for (i = 0; i < em.length; i++){
			var cp = em.codePointAt(i);
			var hex = cp.toString(16).toUpperCase();
			while (hex.length < 4) hex = "0" + hex;
			out.push("U+" + hex);
			if (cp > 0xFFFF) i++;
		}
		return out;
	}

	function cptext(em){
		return cps(em).join(" ");
	}

	function utf8(em){
		var out = [];
		var i;
		for (i = 0; i < em.length; i++){
			var cp = em.codePointAt(i);
			if (cp > 0xFFFF) i++;
			var b;
			if (cp < 0x80) b = [cp];
			else if (cp < 0x800) b = [0xC0 | (cp >> 6), 0x80 | (cp & 0x3F)];
			else if (cp < 0x10000) b = [0xE0 | (cp >> 12), 0x80 | ((cp >> 6) & 0x3F), 0x80 | (cp & 0x3F)];
			else b = [0xF0 | (cp >> 18), 0x80 | ((cp >> 12) & 0x3F), 0x80 | ((cp >> 6) & 0x3F), 0x80 | (cp & 0x3F)];
			var j;
			for (j = 0; j < b.length; j++){
				var h = b[j].toString(16).toUpperCase();
				out.push(h.length < 2 ? "0" + h : h);
			}
		}
		return out.join(" ");
	}

	function utf16(em){
		var out = [];
		var i;
		for (i = 0; i < em.length; i++){
			var cp = em.codePointAt(i);
			var hex = cp.toString(16).toUpperCase();
			if (cp <= 0xFFFF){
				while (hex.length < 4) hex = "0" + hex;
				out.push(hex);
				continue;
			}
			i++;
			var u = cp - 0x10000;
			out.push((0xD800 + (u >> 10)).toString(16).toUpperCase(), (0xDC00 + (u & 0x3FF)).toString(16).toUpperCase());
		}
		return out.join(" ");
	}

	function jsesc(em){
		var out = [];
		var i;
		for (i = 0; i < em.length; i++){
			var cp = em.codePointAt(i);
			if (cp > 0xFFFF){
				i++;
				out.push("\\u{" + cp.toString(16).toUpperCase() + "}");
				continue;
			}
			var hex = cp.toString(16).toUpperCase();
			while (hex.length < 4) hex = "0" + hex;
			out.push("\\u" + hex);
		}
		return out.join("");
	}

	function entity(em){
		var dec = [];
		var hex = [];
		var i;
		for (i = 0; i < em.length; i++){
			var cp = em.codePointAt(i);
			if (cp > 0xFFFF) i++;
			dec.push("&#" + cp + ";");
			hex.push("&#x" + cp.toString(16).toUpperCase() + ";");
		}
		return hex.join("") + "  " + dec.join("");
	}

	function urlesc(em){
		return "%" + utf8(em).split(" ").join("%");
	}

	// 每行预先拆好：名字（中英）、关键词按 | 拆成词、码位。分类和子分类名不进索引，免得搜「笑脸和情感」把整类都搜出来。
	function reset(){
		var d = data();
		meta = [];
		var i, j, k;
		for (i = 0; i < d.r.length; i++){
			var r = d.r[i];
			var parts = (r[3] + "|" + r[4]).split("|");
			var tok = [];
			for (j = 0; j < parts.length; j++){
				k = parts[j].replace(/^\s+|\s+$/g, "").toLowerCase();
				if (k) tok.push(k);
			}
			meta.push({
				n: (r[1] + " " + r[2]).toLowerCase(),
				t: tok,
				c: cptext(r[0]).toLowerCase()
			});
		}
	}

	// 分数：3 名字或码位命中，2 关键词整词命中（中文按包含），1 英文词首包含，0 不命中。
	function score(i, t){
		var m = meta[i];
		if (m.n.indexOf(t) >= 0 || m.c.indexOf(t) >= 0) return 3;
		var j;
		for (j = 0; j < m.t.length; j++){
			var w = m.t[j];
			if (w === t) return 2;
			if (w.indexOf(t) === 0 && /^[\x00-\x7f]+$/.test(t)) return 2;
			if (/[^\x00-\x7f]/.test(t) && w.indexOf(t) >= 0) return 2;
		}
		return 0;
	}

	function terms(){
		var raw = String(page.doms.emjQ.value || "").replace(/^\s+|\s+$/g, "").toLowerCase();
		return raw ? raw.split(/\s+/) : [];
	}

	// 返回 {i: 行号, s: 分数}，按分数和原来的顺序排。
	function search(q){
		var out = [];
		var i;
		for (i = 0; i < data().r.length; i++){
			var worst = 9;
			var j;
			for (j = 0; j < q.length; j++){
				var sc = score(i, q[j]);
				if (!sc) { worst = 0; break; }
				if (sc < worst) worst = sc;
			}
			if (worst) out.push({ i: i, s: worst });
		}
		out.sort(function(a, b){ return b.s - a.s || a.i - b.i; });
		return out;
	}

	function counts(){
		var n = [];
		var d = data();
		var i;
		for (i = 0; i < d.g.length; i++) n.push(0);
		for (i = 0; i < d.r.length; i++) n[d.r[i][5]]++;
		return n;
	}

	function rowby(em){
		var d = data();
		var i;
		if (!em) return null;
		for (i = 0; i < d.r.length; i++) if (d.r[i][0] === em) return d.r[i];
		return null;
	}

	function paint(){
		if (!page) return;
		var q = terms();
		var rows = search(q);
		var shown = 0;
		var i;
		for (i = 0; i < rows.length; i++) if (cur === "all" || String(data().r[rows[i].i][5]) === cur) shown++;
		paintgroups(shown, q.length > 0);
		paintgrid(rows, q.length > 0);
		var r = rowby(picked);
		if (r && !inrows(r, rows)) r = null;
		if (!r) r = rows.length ? firstof(rows) : null;
		if (r){
			picked = r[0];
			mark();
			detail(r);
		}
		else {
			picked = "";
			detail(null);
		}
	}

	function inrows(r, rows){
		var i;
		for (i = 0; i < rows.length; i++){
			if (data().r[rows[i].i] === r) return cur === "all" || String(r[5]) === cur;
		}
		return false;
	}

	function firstof(rows){
		var i;
		for (i = 0; i < rows.length; i++){
			var r = data().r[rows[i].i];
			if (cur === "all" || String(r[5]) === cur) return r;
		}
		return null;
	}

	function paintgroups(shown, searching){
		var host = page.doms.emjGroups;
		var d = data();
		var countsn = counts();
		while (host.firstChild) host.removeChild(host.firstChild);
		var sum = mk("div", "usum", searching
			? tx("web.emj.hit", "匹配 {0} 个", shown)
			: tx("web.emj.total", "共 {0} 个", d.r.length));
		host.appendChild(sum);
		host.appendChild(gbutton(-1, d.r.length));
		var i;
		for (i = 0; i < d.g.length; i++) host.appendChild(gbutton(i, countsn[i]));
	}

	function gbutton(i, n){
		var btn = mk("button");
		btn.setAttribute("type", "button");
		btn.setAttribute("data-g", String(i));
		btn.title = i < 0 ? tx("web.emj.all", "全部") : gfull(i);
		btn.className = (i < 0 ? "all" : String(i)) === cur ? "on" : "";
		btn.appendChild(mk("b", null, i < 0 ? tx("web.emj.all", "全部") : gname(i)));
		btn.appendChild(mk("small", null, String(n)));
		return btn;
	}

	function paintgrid(rows, searching){
		var host = page.doms.emjGrid;
		var d = data();
		var list = [];
		var i;
		for (i = 0; i < rows.length; i++){
			var r = d.r[rows[i].i];
			if (cur !== "all" && String(r[5]) !== cur) continue;
			list.push(r);
		}
		while (host.firstChild) host.removeChild(host.firstChild);
		if (searching){
			if (!list.length) host.appendChild(mk("div", "emjnone", tx("web.emj.none", "没有匹配的 emoji")));
			else host.appendChild(cells(list));
		}
		else {
			var byg = {};
			var gi;
			for (gi = 0; gi < d.g.length; gi++){
				var bys = {};
				for (i = 0; i < list.length; i++){
					if (list[i][5] !== gi) continue;
					var si = list[i][6];
					(bys[si] = bys[si] || []).push(list[i]);
				}
				var k;
				var n = 0;
				for (k in bys) if (Object.prototype.hasOwnProperty.call(bys, k)) n += bys[k].length;
				if (!n) continue;
				if (cur === "all") host.appendChild(subtitle("emjgrp", gname(gi), n));
				for (k = 0; k < d.s.length; k++){
					if (!bys[k]) continue;
					host.appendChild(subtitle("emjsub", sname(k), bys[k].length));
					host.appendChild(cells(bys[k]));
				}
			}
			if (!list.length) host.appendChild(mk("div", "emjnone", tx("web.emj.none", "没有匹配的 emoji")));
		}
		host.scrollTop = 0;
	}

	function subtitle(cls, name, n){
		var h = mk("div", cls, name);
		h.appendChild(mk("span", "n", String(n)));
		return h;
	}

	// 每格是「emoji + 中文名 + 英文名」三列，列数按可用宽度自动增减。
	function cells(rows){
		var box = mk("div", "emjrow");
		var i;
		for (i = 0; i < rows.length; i++){
			var r = rows[i];
			var b = mk("button", r[0] === picked ? "emjcell on" : "emjcell");
			b.setAttribute("type", "button");
			b.setAttribute("data-em", r[0]);
			b.title = names(r);
			b.appendChild(mk("span", "emjg", r[0]));
			var txt = mk("span", "emjn");
			txt.appendChild(mk("b", null, iszh() ? r[2] : r[1]));
			txt.appendChild(mk("small", null, iszh() ? r[1] : r[2]));
			b.appendChild(txt);
			box.appendChild(b);
		}
		return box;
	}

	function mark(){
		var cells = F.qa("button[data-em]", page.doms.emjGrid);
		var i;
		for (i = 0; i < cells.length; i++)
			cells[i].className = F.attr(cells[i], "data-em") === picked ? "emjcell on" : "emjcell";
	}

	function ongroup(ev){
		var el = ev.target;
		while (el && el !== this){
			var g = el.getAttribute && F.attr(el, "data-g");
			if (g != null){
				cur = g === "-1" ? "all" : g;
				picked = "";
				paint();
				return;
			}
			el = el.parentNode;
		}
	}

	function oncell(ev){
		var el = ev.target;
		while (el && el !== this){
			var em = el.getAttribute && F.attr(el, "data-em");
			if (em != null){
				var r = rowby(em);
				if (!r) return;
				picked = r[0];
				mark();
				detail(r);
				SK.copytext(r[0], "emj-msg");
				return;
			}
			el = el.parentNode;
		}
	}

	function pickcopy(){
		if (picked) SK.copytext(picked, "emj-msg");
	}

	function metarow(dl, key, fb, val, plain){
		if (!val) return;
		var dt = mk("dt");
		dt.setAttribute("data-k", key);
		dt.textContent = tx(key, fb);
		dl.appendChild(dt);
		dl.appendChild(mk("dd", plain ? "emjplain" : null, val));
	}

	function detail(r){
		var ch = page.doms.emjCh;
		var dl = page.doms.emjMeta;
		while (dl.firstChild) dl.removeChild(dl.firstChild);
		ch.className = "uprev-ch emjch";
		ch.style.fontSize = "";
		if (!r){
			ch.textContent = "";
			ch.title = "";
			return;
		}
		ch.textContent = r[0];
		ch.title = tx("web.emj.copy", "点击复制") + " " + r[0];
		var cp = cps(r[0]).length;
		ch.style.fontSize = cp <= 2 ? "80px" : (cp <= 4 ? "56px" : (cp <= 8 ? "40px" : "28px"));
		metarow(dl, "web.emj.name.zh", "中文名", r[2], true);
		metarow(dl, "web.emj.name.en", "英文名", r[1], true);
		metarow(dl, "web.emj.group", "分类", gfull(r[5]), true);
		metarow(dl, "web.emj.sub", "子分类", sfull(r[6]), true);
		metarow(dl, "web.emj.cp", "码位", cptext(r[0]));
		metarow(dl, "web.emj.utf8", "UTF-8", utf8(r[0]));
		metarow(dl, "web.emj.utf16", "UTF-16", utf16(r[0]));
		metarow(dl, "web.emj.html", "HTML", entity(r[0]));
		metarow(dl, "web.emj.js", "JavaScript", jsesc(r[0]));
		metarow(dl, "web.emj.url", "URL", urlesc(r[0]));
		metarow(dl, "web.emj.kw.zh", "中文关键词", r[4], true);
		metarow(dl, "web.emj.kw.en", "英文关键词", r[3], true);
	}
})();
