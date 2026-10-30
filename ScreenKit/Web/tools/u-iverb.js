(function(){
	// 表在 EnConj.IR，打开时从 /api/conj/irr 读取。过去式、过去分词里的「/」表示常见变体，归组和复制只按第一个。
	var ROWS = [];
	var ivgen = 0;

	var PATS = ["AAA", "AAB", "ABA", "ABB", "ABC"];
	var GROUP = {
		AAA: ["三形相同", "all three forms the same"],
		AAB: ["原形与过去式相同", "base and past the same"],
		ABA: ["原形与过去分词相同", "base and participle the same"],
		ABB: ["过去式与过去分词相同", "past and participle the same"],
		ABC: ["三形都不同", "all three forms different"]
	};
	var LAB = {
		base: ["原形", "Base"],
		past: ["过去式", "Past"],
		pp: ["过去分词", "Past participle"],
		mean: ["释义", "Meaning"],
		type: ["类型", "Pattern"],
		note: ["备注", "Note"],
		forms: ["三种形式", "Three forms"],
		search: ["搜索：go、went、买、ABC", "Search: go, went, buy, ABC"],
		all: ["全部", "All"],
		copy: ["点击复制 %s", "Click to copy %s"],
		none: ["没有匹配的动词", "No matching verbs"],
		count: ["共 %n 个", "%n verbs"],
		hit: ["%n 个", "%n"],
		empty: ["没有这个工具", "No such tool"]
	};
	var cur = "all";
	var picked = 0;

	SK.regutil("u-iverb", {
		title: "英文不规则动词",
		go: "查询",
		live: false,
		hideGo: true,
		hideOut: true,
		body: body,
		open: open,
		relang: function(){ SK.reopenutil(); },
		run: function(){ throw new Error(tr(LAB.empty)); }
	});

	function tx(key, fb){
		var s = SK.t ? SK.t(key) : "";
		return s || fb;
	}

	function langcode(){
		var sel = F.id("lang");
		return sel && sel.value ? sel.value : "zh";
	}

	function tr(pair){
		return langcode() === "zh" ? pair[0] : pair[1];
	}

	function body(){
		return '<div class="reftab">'
			+ '<div class="uquery"><input id="iv-q" type="text" placeholder="' + esc(tr(LAB.search)) + '">'
			+ '<span id="iv-msg" class="msg"></span></div>'
			+ '<div class="refchips" id="iv-chips"></div>'
			+ '<div class="refbody">'
			+ '<div class="refscroll"><table class="reflist iverbs" id="iv-tab"></table></div>'
			+ '<aside class="refdetail" id="iv-detail"></aside>'
			+ "</div></div>";
	}

	function open(){
		var title = F.id("box-title");
		if (title) title.textContent = tx("web.tool.iverb", "英文不规则动词");
		cur = "all";
		picked = 0;
		var box = F.id("iv-q");
		if (box) box.oninput = paint;
		var chips = F.id("iv-chips");
		if (chips) chips.onclick = onchip;
		var tab = F.id("iv-tab");
		if (tab) tab.onclick = onrow;
		loadrows();
	}

	function loadrows(){
		var n = ++ivgen;
		fetch("/api/conj/irr").then(function(r){ return r.json(); }).then(function(jo){
			if (n !== ivgen) return;
			ROWS = (jo && jo.data && jo.data.rows) || [];
			paintchips();
			paint();
		}, function(){
			if (n !== ivgen) return;
			ROWS = [];
			paintchips();
			paint();
		});
	}

	function prim(s){
		return String(s == null ? "" : s).split("/")[0].replace(/\s+/g, " ").trim();
	}

	function pat(row){
		var b = prim(row[0]);
		var p = prim(row[1]);
		var q = prim(row[2]);
		if (b === p && p === q) return "AAA";
		if (b === p) return "AAB";
		if (b === q) return "ABA";
		if (p === q) return "ABB";
		return "ABC";
	}

	function groupcount(id){
		var i, n = 0;
		for (i = 0; i < ROWS.length; i++) if (pat(ROWS[i]) === id) n++;
		return n;
	}

	function meaning(row){
		return langcode() === "zh" ? row[3] : row[4];
	}

	function forms(row){
		return prim(row[0]) + " " + prim(row[1]) + " " + prim(row[2]);
	}

	function query(){
		return String(SK.val("iv-q") || "").trim().toLowerCase();
	}

	function hit(row, q){
		if (!q) return true;
		var blob = (row[0] + " " + row[1] + " " + row[2] + " " + row[3] + " " + row[4] + " " + pat(row)
			+ (row[5] ? " " + row[5][0] + " " + row[5][1] : "")).toLowerCase();
		var terms = q.split(/\s+/);
		var i;
		for (i = 0; i < terms.length; i++) {
			if (!terms[i]) continue;
			// 拉丁字母按词首匹配，免得查 go 时把 forgo、undergo 也带出来；中文仍按子串。
			if (/^[a-z0-9.]+$/.test(terms[i])) {
				if (!prefix(blob, terms[i])) return false;
			}
			else if (blob.indexOf(terms[i]) < 0) return false;
		}
		return true;
	}

	function prefix(blob, term){
		var words = blob.split(/[^a-z0-9.]+/);
		var i;
		for (i = 0; i < words.length; i++) if (words[i].indexOf(term) === 0) return true;
		return false;
	}

	function paintchips(){
		var html = ['<button type="button" data-g="all"' + (cur === "all" ? ' class="on"' : "") + ">"
			+ esc(tr(LAB.all)) + "<small>" + ROWS.length + "</small></button>"];
		var i, n;
		for (i = 0; i < PATS.length; i++) {
			n = groupcount(PATS[i]);
			if (!n) continue;
			html.push('<button type="button" data-g="' + PATS[i] + '" title="'
				+ esc(PATS[i] + " · " + tr(GROUP[PATS[i]])) + '"' + (cur === PATS[i] ? ' class="on"' : "") + ">"
				+ PATS[i] + "<small>" + n + "</small></button>");
		}
		F.id("iv-chips").innerHTML = html.join("");
	}

	function paint(){
		var q = query();
		var html = ["<thead><tr><th>" + esc(tr(LAB.base)) + "</th><th>" + esc(tr(LAB.past))
			+ "</th><th>" + esc(tr(LAB.pp)) + "</th><th>" + esc(tr(LAB.mean)) + "</th></tr></thead><tbody>"];
		var total = 0;
		var i, j, rows, n;
		for (i = 0; i < PATS.length; i++) {
			if (cur !== "all" && cur !== PATS[i]) continue;
			rows = [];
			for (j = 0; j < ROWS.length; j++) {
				if (pat(ROWS[j]) === PATS[i] && hit(ROWS[j], q)) rows.push(j);
			}
			if (!rows.length) continue;
			total += rows.length;
			html.push('<tr class="refgh"><td colspan="4">' + esc(PATS[i] + " · " + tr(GROUP[PATS[i]]))
				+ '<span class="n">' + rows.length + "</span></td></tr>");
			for (n = 0; n < rows.length; n++) html.push(rowhtml(rows[n]));
		}
		html.push("</tbody>");
		if (!total) html = ['<tbody><tr><td class="refnone">' + esc(tr(LAB.none)) + "</td></tr></tbody>"];
		F.id("iv-tab").innerHTML = html.join("");
		SK.msg("iv-msg", total ? (q ? tr(LAB.hit) : tr(LAB.count)).replace("%n", total) : "");
		if (total) {
			var sel = visible(picked, q) ? picked : firstvisible(q);
			if (sel >= 0) {
				picked = sel;
				showdetail(sel);
			}
		}
		else F.id("iv-detail").innerHTML = "";
	}

	function visible(i, q){
		if (i < 0 || i >= ROWS.length) return false;
		if (cur !== "all" && cur !== pat(ROWS[i])) return false;
		return hit(ROWS[i], q);
	}

	function firstvisible(q){
		var i;
		for (i = 0; i < PATS.length; i++) {
			if (cur !== "all" && cur !== PATS[i]) continue;
			var j;
			for (j = 0; j < ROWS.length; j++) {
				if (pat(ROWS[j]) === PATS[i] && hit(ROWS[j], q)) return j;
			}
		}
		return -1;
	}

	function rowhtml(i){
		var row = ROWS[i];
		return '<tr data-iv="' + i + '" title="' + esc(tr(LAB.copy).replace("%s", forms(row))) + '"'
			+ (i === picked ? ' class="on"' : "") + ">"
			+ '<td class="ivbase">' + esc(row[0]) + "</td>"
			+ "<td>" + esc(row[1]) + "</td>"
			+ "<td>" + esc(row[2]) + "</td>"
			+ "<td>" + esc(meaning(row)) + "</td></tr>";
	}

	function showdetail(i){
		var row = ROWS[i];
		if (!row) return;
		var p = pat(row);
		var html = '<div class="refbig">' + esc(row[0]) + "</div>"
			+ '<div class="refsub">' + esc(meaning(row)) + "</div>"
			+ '<dl class="refmeta">'
			+ "<dt>" + esc(tr(LAB.past)) + "</dt><dd>" + esc(row[1]) + "</dd>"
			+ "<dt>" + esc(tr(LAB.pp)) + "</dt><dd>" + esc(row[2]) + "</dd>"
			+ "<dt>" + esc(tr(LAB.type)) + "</dt><dd>" + esc(p + " · " + tr(GROUP[p])) + "</dd>";
		if (row[5]) html += "<dt>" + esc(tr(LAB.note)) + "</dt><dd>" + esc(tr(row[5])) + "</dd>";
		html += "</dl>"
			+ '<div class="refsample-t">' + esc(tr(LAB.forms)) + "</div>"
			+ '<div class="refsample">' + esc(forms(row)) + "</div>";
		F.id("iv-detail").innerHTML = html;
	}

	function onchip(ev){
		var el = ev.target;
		while (el && el !== this) {
			if (el.getAttribute && F.attr(el, "data-g")) {
				cur = F.attr(el, "data-g");
				paintchips();
				paint();
				return;
			}
			el = el.parentNode;
		}
	}

	function onrow(ev){
		var el = ev.target;
		while (el && el.tagName !== "TR") el = el.parentNode;
		if (!el || !F.attr(el, "data-iv")) return;
		var i = parseInt(F.attr(el, "data-iv"), 10);
		if (!(i >= 0 && i < ROWS.length)) return;
		picked = i;
		paint();
		showdetail(i);
		SK.copytext(forms(ROWS[i]), "iv-msg");
	}

	function esc(s){
		return String(s == null ? "" : s)
			.replace(/&/g, "&amp;")
			.replace(/</g, "&lt;")
			.replace(/>/g, "&gt;")
			.replace(/"/g, "&quot;");
	}
})();
