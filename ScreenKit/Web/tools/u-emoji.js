(function(){
	var page = null;
	var cur = "all";
	var picked = "";
	var blob = null;

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
			page.doms.emjQ.oninput = paint;
			page.doms.emjGroups.onclick = ongroup;
			page.doms.emjGrid.onclick = oncell;
			page.doms.emjCh.onclick = pickcopy;
			reset();
			paint();
		},
		hideGo: true,
		live: false,
		hideOut: true,
		relang: function(){
			paint();
		}
	});

	function d(){
		return SK.emojidata || { g: [], s: [], r: [] };
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
		var g = d().g[i] || ["", ""];
		return iszh() ? g[1] || g[0] : g[0] || g[1];
	}

	function sname(i){
		var s = d().s[i] || ["", ""];
		return iszh() ? s[1] || s[0] : s[0] || s[1];
	}

	function gfull(i){
		var g = d().g[i] || ["", ""];
		if (!g[0] || !g[1] || g[0] === g[1]) return gname(i);
		return iszh() ? g[1] + " · " + g[0] : g[0] + " · " + g[1];
	}

	function sfull(i){
		var s = d().s[i] || ["", ""];
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
			var hi = (0xD800 + (u >> 10)).toString(16).toUpperCase();
			var lo = (0xDC00 + (u & 0x3FF)).toString(16).toUpperCase();
			out.push(hi, lo);
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

	// 搜索统一走一份小写文本：emoji、中英文名、中英文关键词、分类名和码位。
	function reset(){
		blob = [];
		var data = d();
		var i;
		for (i = 0; i < data.r.length; i++){
			var r = data.r[i];
			var g = data.g[r[5]] || ["", ""];
			var s = data.s[r[6]] || ["", ""];
			blob.push((r[0] + " " + r[1] + " " + r[2] + " " + r[3] + " " + r[4] + " "
				+ cptext(r[0]) + " " + g[0] + " " + g[1] + " " + s[0] + " " + s[1]).toLowerCase());
		}
	}

	function terms(){
		var raw = String(page.doms.emjQ.value || "").replace(/^\s+|\s+$/g, "").toLowerCase();
		return raw ? raw.split(/\s+/) : [];
	}

	function hit(i, q){
		var j;
		for (j = 0; j < q.length; j++) if (blob[i].indexOf(q[j]) < 0) return false;
		return true;
	}

	function counts(){
		var n = [];
		var data = d();
		var i;
		for (i = 0; i < data.g.length; i++) n.push(0);
		for (i = 0; i < data.r.length; i++) n[data.r[i][5]]++;
		return n;
	}

	function rowby(em){
		var data = d();
		var i;
		if (!em) return null;
		for (i = 0; i < data.r.length; i++) if (data.r[i][0] === em) return data.r[i];
		return null;
	}

	function paint(){
		if (!page) return;
		paintgroups();
		var q = terms();
		var shown = paintgrid(q);
		var sum = page.doms.emjGroups.firstChild;
		if (sum) sum.textContent = q.length ? tx("web.emj.hit", "匹配 {0} 个", shown) : tx("web.emj.total", "共 {0} 个", d().r.length);
		var r = rowby(picked);
		var keep = false;
		var data = d();
		var i;
		for (i = 0; r && i < data.r.length; i++){
			if (data.r[i] !== r) continue;
			keep = (!q.length || hit(i, q)) && (cur === "all" || String(r[5]) === cur);
			break;
		}
		if (!keep) r = first(q);
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

	function paintgroups(){
		var host = page.doms.emjGroups;
		var data = d();
		var countsn = counts();
		while (host.firstChild) host.removeChild(host.firstChild);
		host.appendChild(mk("div", "usum"));
		host.appendChild(gbutton(-1, data.r.length));
		var i;
		for (i = 0; i < data.g.length; i++) host.appendChild(gbutton(i, countsn[i]));
	}

	function gbutton(i, n){
		var btn = mk("button");
		btn.setAttribute("type", "button");
		btn.setAttribute("data-g", String(i));
		btn.title = i < 0 ? tx("web.emj.all", "全部") : gfull(i);
		btn.className = String(i) === cur ? "on" : "";
		btn.appendChild(mk("b", null, i < 0 ? tx("web.emj.all", "全部") : gname(i)));
		btn.appendChild(mk("small", null, String(n)));
		return btn;
	}

	function paintgrid(q){
		var host = page.doms.emjGrid;
		var data = d();
		var byg = {};
		var shown = 0;
		var i;
		while (host.firstChild) host.removeChild(host.firstChild);
		for (i = 0; i < data.r.length; i++){
			var r = data.r[i];
			if (cur !== "all" && String(r[5]) !== cur) continue;
			if (q.length && !hit(i, q)) continue;
			(byg[r[5]] = byg[r[5]] || []).push(r);
			shown++;
		}
		var gi;
		for (gi = 0; gi < data.g.length; gi++){
			if (!byg[gi]) continue;
			if (cur === "all") host.appendChild(subtitle("emjgrp", gname(gi), byg[gi].length));
			var bys = {};
			var k;
			for (k = 0; k < byg[gi].length; k++){
				var si = byg[gi][k][6];
				(bys[si] = bys[si] || []).push(byg[gi][k]);
			}
			for (k = 0; k < data.s.length; k++){
				if (!bys[k]) continue;
				host.appendChild(subtitle("emjsub", sname(k), bys[k].length));
				host.appendChild(cells(bys[k]));
			}
		}
		if (!shown) host.appendChild(mk("div", "emjnone", tx("web.emj.none", "没有匹配的 emoji")));
		host.scrollTop = 0;
		return shown;
	}

	function subtitle(cls, name, n){
		var h = mk("div", cls, name);
		h.appendChild(mk("span", "n", String(n)));
		return h;
	}

	function cells(rows){
		var box = mk("div", "emjrow");
		var i;
		for (i = 0; i < rows.length; i++){
			var b = mk("button", rows[i][0] === picked ? "emjcell on" : "emjcell", rows[i][0]);
			b.setAttribute("type", "button");
			b.setAttribute("data-em", rows[i][0]);
			b.title = names(rows[i]);
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

	function first(q){
		var data = d();
		var i;
		for (i = 0; i < data.r.length; i++){
			if (cur !== "all" && String(data.r[i][5]) !== cur) continue;
			if (q.length && !hit(i, q)) continue;
			return data.r[i];
		}
		return null;
	}

	function ongroup(ev){
		var el = ev.target;
		while (el && el !== this){
			var g = el.getAttribute && F.attr(el, "data-g");
			if (g != null){
				cur = g;
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

	function meta(dl, key, fb, val, mono){
		if (!val) return;
		var dt = mk("dt");
		dt.setAttribute("data-k", key);
		dt.textContent = tx(key, fb);
		dl.appendChild(dt);
		dl.appendChild(mk("dd", mono === false ? "emjplain" : null, val));
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
		meta(dl, "web.emj.name.zh", "中文名", r[2], false);
		meta(dl, "web.emj.name.en", "英文名", r[1], false);
		meta(dl, "web.emj.group", "分类", gfull(r[5]), false);
		meta(dl, "web.emj.sub", "子分类", sfull(r[6]), false);
		meta(dl, "web.emj.cp", "码位", cptext(r[0]));
		meta(dl, "web.emj.utf8", "UTF-8", utf8(r[0]));
		meta(dl, "web.emj.utf16", "UTF-16", utf16(r[0]));
		meta(dl, "web.emj.html", "HTML", entity(r[0]));
		meta(dl, "web.emj.js", "JavaScript", jsesc(r[0]));
		meta(dl, "web.emj.url", "URL", urlesc(r[0]));
		meta(dl, "web.emj.kw.zh", "中文关键词", r[4], false);
		meta(dl, "web.emj.kw.en", "英文关键词", r[3], false);
	}
})();
