(function(){
	// 通用速查表：搜索、分组、点行复制、右侧详情。
	// spec 可以只给一张表，也可以用 sets 给多张表（顶部切换）。
	// 行格式：{g:"分组", c:["可见列"], x:"额外搜索文字", d:[["详情项","值"]]}
	SK.regref = function(name, spec){
		var sets = spec.sets || [{
			id: "one",
			name: spec.name || "",
			cols: spec.cols || [],
			widths: spec.widths || [],
			groups: spec.groups || [],
			rows: spec.rows || [],
			placeholder: spec.placeholder || "",
			big: spec.big,
			sub: spec.sub,
			copy: spec.copy,
			numeric: spec.numeric,
			bigCls: spec.bigCls || "",
			hl: spec.hl || "",
			defaultPick: spec.defaultPick || "",
			sample: spec.sample,
			sampleTitle: spec.sampleTitle
		}];
		var store = spec.store || "";
		var setsLabel = spec.setsLabel || "";
		var setat = 0;
		var cur = "all";
		var picked = null;

		SK.regutil(name, {
			title: spec.title,
			body: function(){
				var html = '<div class="reftab">'
					+ '<div class="uquery"><input id="' + name + '-q" type="text">'
					+ '<span id="' + name + '-msg" class="msg"></span></div>';
				if (sets.length > 1)
					html += '<div class="refchips refsets" id="' + name + '-sets"></div>';
				html += '<div class="refchips" id="' + name + '-chips"></div>'
					+ '<div class="refbody">'
					+ '<div class="refscroll"><table class="reflist refdata" id="' + name + '-tab"></table></div>'
					+ '<aside class="refdetail" id="' + name + '-detail"></aside>'
					+ "</div></div>";
				return html;
			},
			run: function(){
				throw new Error("没有这个工具");
			},
			open: function(){
				setat = readsaved();
				cur = "all";
				picked = null;
				if (sets.length > 1) {
					paintsets();
					F.id(name + "-sets").onclick = onset;
				}
				F.id(name + "-q").oninput = paint;
				F.id(name + "-chips").onclick = onchip;
				F.id(name + "-tab").onclick = onrow;
				paint();
			},
			hideGo: true,
			live: false,
			hideOut: true
		});

		function setnow(){
			return sets[setat];
		}

		// 记住上次选的分组（如发行版）；存的 id 不在列表里就回到第一组。
		function readsaved(){
			var id = "";
			if (!store) return 0;
			try { id = F.lget(store) || ""; } catch (e) { id = ""; }
			var i;
			for (i = 0; i < sets.length; i++) if (sets[i].id === id) return i;
			return 0;
		}

		function writesaved(){
			if (!store) return;
			try { F.lset(store, sets[setat].id); } catch (e) {}
		}

		function query(){
			return String(SK.val(name + "-q") || "").trim().toLowerCase();
		}

		function groupname(set, id){
			var i;
			for (i = 0; i < set.groups.length; i++) if (set.groups[i].id === id) return set.groups[i].name;
			return "";
		}

		function hit(row, q, set){
			if (!q) return true;
			if (set.numeric && /^[0-9]+$/.test(q)) return String(row.c[0]).indexOf(q) === 0;
			var blob = row.c.join(" ") + " " + (row.x || "") + " " + groupname(set, row.g);
			var i;
			for (i = 0; i < (row.d || []).length; i++) blob += " " + row.d[i][1];
			return blob.toLowerCase().indexOf(q) >= 0;
		}

		function paint(){
			var set = setnow();
			var q = query();
			var cols = set.cols;
			var html = [];
			var i, j, g, rows, n;
			var total = 0;
			F.id(name + "-q").placeholder = set.placeholder || "";
			if (set.widths && set.widths.length) {
				html.push("<colgroup>");
				for (i = 0; i < cols.length; i++)
					html.push("<col" + (set.widths[i] ? ' style="width:' + set.widths[i] + '"' : "") + ">");
				html.push("</colgroup>");
			}
			html.push("<thead><tr>");
			for (i = 0; i < cols.length; i++)
				html.push("<th" + (cols[i].nowrap ? ' class="rnw"' : "") + ">" + esc(cols[i].name) + "</th>");
			html.push("</tr></thead><tbody>");
			if (set.groups && set.groups.length) {
				for (g = 0; g < set.groups.length; g++) {
					if (cur !== "all" && cur !== set.groups[g].id) continue;
					rows = pick(set, set.groups[g].id, q);
					if (!rows.length) continue;
					total += rows.length;
					html.push('<tr class="refgh"><td colspan="' + cols.length + '">' + esc(set.groups[g].name)
						+ '<span class="n">' + rows.length + "</span></td></tr>");
					for (n = 0; n < rows.length; n++) html.push(rowhtml(set, rows[n]));
				}
			}
			else {
				rows = pick(set, "", q);
				total = rows.length;
				for (n = 0; n < rows.length; n++) html.push(rowhtml(set, rows[n]));
			}
			html.push("</tbody>");
			if (!total)
				html = ['<tbody><tr><td class="refnone" colspan="' + cols.length + '">没有匹配的条目</td></tr></tbody>'];
			F.id(name + "-tab").innerHTML = html.join("");
			SK.msg(name + "-msg", total ? (q ? total + " 条" : "共 " + total + " 条") : "");
			paintchips(set);
			if (total) selectrow(set, q);
			else F.id(name + "-detail").innerHTML = "";
		}

		function pick(set, gid, q){
			var out = [];
			var i;
			for (i = 0; i < set.rows.length; i++) {
				if (gid && set.rows[i].g !== gid) continue;
				if (hit(set.rows[i], q, set)) out.push(set.rows[i]);
			}
			return out;
		}

		function rowhtml(set, row){
			var key = String(row.c[set.copy]);
			var html = '<tr data-k="' + esc(key) + '" title="点击复制 ' + esc(key) + '"'
				+ (key === picked ? ' class="on"' : "") + ">";
			var i;
			for (i = 0; i < set.cols.length; i++) {
				var cls = [];
				if (set.cols[i].nowrap) cls.push("rnw");
				if (set.cols[i].mono) cls.push("rmono");
				html += "<td" + (cls.length ? ' class="' + cls.join(" ") + '"' : "") + ">"
					+ cellhtml(set, i, row.c[i]) + "</td>";
			}
			return html + "</tr>";
		}

		function selectrow(set, q){
			var sel = picked ? rowby(set, picked) : null;
			if (!visible(set, sel, q))
				sel = (set.defaultPick && !q) ? rowby(set, set.defaultPick) : null;
			if (!visible(set, sel, q)) sel = firstvisible(set, q);
			if (sel) {
				picked = String(sel.c[set.copy]);
				showdetail(set, sel);
			}
			else {
				picked = null;
				F.id(name + "-detail").innerHTML = "";
			}
		}

		function visible(set, row, q){
			if (!row) return false;
			if (set.groups && set.groups.length && cur !== "all" && row.g !== cur) return false;
			return hit(row, q, set);
		}

		function firstvisible(set, q){
			var i;
			for (i = 0; i < set.rows.length; i++) {
				if (visible(set, set.rows[i], q)) return set.rows[i];
			}
			return null;
		}

		function rowby(set, key){
			var i;
			for (i = 0; i < set.rows.length; i++) {
				if (String(set.rows[i].c[set.copy]) === String(key)) return set.rows[i];
			}
			// defaultPick 也可以写主显示值（如魔数表的 "PNG"）。
			for (i = 0; i < set.rows.length; i++) {
				if (set.big != null && String(set.rows[i].c[set.big]) === String(key)) return set.rows[i];
			}
			return null;
		}

		function showdetail(set, row){
			var html = '<div class="refbig' + (set.bigCls ? " " + set.bigCls : "") + '">'
				+ esc(row.c[set.big != null ? set.big : 0]) + "</div>";
			if (set.sub != null) html += '<div class="refsub">' + esc(row.c[set.sub]) + "</div>";
			html += '<dl class="refmeta">';
			var i;
			for (i = 0; i < (row.d || []).length; i++)
				html += "<dt>" + esc(row.d[i][0]) + "</dt><dd>" + metaval(set, row.d[i][0], row.d[i][1]) + "</dd>";
			html += "</dl>";
			var txt = set.sample ? set.sample(row) : "";
			if (txt) {
				html += '<div class="refsample-t">' + esc(set.sampleTitle || "示例") + "</div>"
					+ '<div class="refsample">' + samplehtml(set, txt) + "</div>";
			}
			F.id(name + "-detail").innerHTML = html;
		}

		function paintchips(set){
			var box = F.id(name + "-chips");
			if (!set.groups || !set.groups.length) {
				box.innerHTML = "";
				box.hidden = true;
				return;
			}
			box.hidden = false;
			var html = ['<button type="button" data-g="all"' + (cur === "all" ? ' class="on"' : "") + ">全部<small>"
				+ set.rows.length + "</small></button>"];
			var i, j, n;
			for (i = 0; i < set.groups.length; i++) {
				n = 0;
				for (j = 0; j < set.rows.length; j++) if (set.rows[j].g === set.groups[i].id) n++;
				html.push('<button type="button" data-g="' + set.groups[i].id + '"'
					+ (cur === set.groups[i].id ? ' class="on"' : "") + ">" + esc(set.groups[i].name)
					+ "<small>" + n + "</small></button>");
			}
			box.innerHTML = html.join("");
		}

		function paintsets(){
			var html = [];
			var i;
			if (setsLabel) html.push('<span class="reflabel">' + esc(setsLabel) + "</span>");
			for (i = 0; i < sets.length; i++) {
				html.push('<button type="button" data-s="' + i + '"'
					+ (i === setat ? ' class="on"' : "") + ">" + esc(sets[i].name)
					+ "<small>" + sets[i].rows.length + "</small></button>");
			}
			F.id(name + "-sets").innerHTML = html.join("");
		}

		function onchip(ev){
			var el = ev.target;
			while (el && el !== this) {
				if (el.getAttribute && F.attr(el, "data-g")) {
					cur = F.attr(el, "data-g");
					paint();
					return;
				}
				el = el.parentNode;
			}
		}

		function onset(ev){
			var el = ev.target;
			while (el && el !== this) {
				if (el.getAttribute && F.attr(el, "data-s")) {
					setat = parseInt(F.attr(el, "data-s"), 10);
					writesaved();
					cur = "all";
					picked = null;
					F.id(name + "-q").value = "";
					paintsets();
					paint();
					return;
				}
				el = el.parentNode;
			}
		}

		function onrow(ev){
			var el = ev.target;
			while (el && el.tagName !== "TR") el = el.parentNode;
			if (!el || !F.attr(el, "data-k")) return;
			var set = setnow();
			var row = rowby(set, F.attr(el, "data-k"));
			if (!row) return;
			picked = String(row.c[set.copy]);
			paint();
			showdetail(set, row);
			SK.copytext(picked, name + "-msg");
		}
	};

	function samplehtml(set, text){
		// 代码示例保留换行；逐行高亮，注释行自然变灰，命令、参数、变量各有颜色。
		var parts = String(text == null ? "" : text).split("\n");
		var i;
		for (i = 0; i < parts.length; i++)
			parts[i] = (set.hl && SK.hl) ? SK.hl(set.hl, parts[i]) : esc(parts[i]);
		return parts.join("<br>");
	}

	function esc(s){
		return String(s == null ? "" : s)
			.replace(/&/g, "&amp;")
			.replace(/</g, "&lt;")
			.replace(/>/g, "&gt;")
			.replace(/"/g, "&quot;");
	}

	// 只有标了 mono 的列才着色，说明列保持纯文本；复制仍用原始字符串。
	function cellhtml(set, i, val){
		if (set.hl && SK.hl && set.cols[i] && set.cols[i].mono === true) return SK.hl(set.hl, val);
		return esc(val);
	}

	function metaval(set, label, val){
		if (set.hl && SK.hl && (label === "示例" || label === "常用示例")) return SK.hl(set.hl, val);
		return esc(val);
	}
})();
