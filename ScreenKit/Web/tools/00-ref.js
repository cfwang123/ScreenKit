(function(){
	// 速查表常见的字段组合（命令表、函数表都差不多），省得每个表各抄一遍 makeset。
	SK.refset = function(o){
		return {
			id: o.id,
			name: o.name,
			cls: o.cls || "",
			cols: o.cols,
			widths: o.widths,
			groups: o.groups,
			rows: o.rows,
			placeholder: o.placeholder,
			big: o.big == null ? 0 : o.big,
			sub: o.sub == null ? 1 : o.sub,
			cn: o.cn,
			copy: o.copy == null ? 0 : o.copy,
			hl: o.hl || "",
			defaultPick: o.defaultPick,
			mline: o.mline,
			sample: o.sample,
			sampleTitle: o.sampleTitle
		};
	};

	// 通用速查表：搜索、分组、点行复制、右侧详情。
	// spec 可以只给一张表，也可以用 sets 给多张表（顶部切换）。
	// 行格式：{g:"分组", c:["可见列"], x:"额外搜索文字", d:[["详情项","值"]]}
	SK.regref = function(name, spec){
		var sets = spec.sets || [{
			id: "one",
			name: spec.name || "",
			cls: spec.cls || "",
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
			sampleTitle: spec.sampleTitle,
			cn: spec.cn,
			copyfn: spec.copyfn,
			hit: spec.hit,
			tr: spec.tr,
			load: spec.load,
			samplehtml: spec.samplehtml,
			count: spec.count,
			countHit: spec.countHit
		}];
		var store = spec.store || "";
		var setsLabel = spec.setsLabel || "";
		var setat = 0;
		var cur = "all";
		var picked = null;
		var gen = 0;

		// 表里写死的中文统一走这里的 set.tr，默认查 zz-l10n 词典；
		// 个别表（如不规则动词）用自带的双语映射，传 spec.tr 即可。
		function t(set, s){
			return (set.tr || SK.td)(s);
		}

		function cname(set, i){
			var name = set.cols[i].name;
			return typeof name === "function" ? name() : name;
		}

		function gname(set, g){
			return typeof g.name === "function" ? g.name() : g.name;
		}

		function cellcls(set, i, row){
			var cls = set.cols[i].cls;
			var val = typeof cls === "function" ? cls(row) : cls;
			return val ? " " + val : "";
		}

		function rowkey(set, row){
			return String(row.c[set.copy]);
		}

		// 点行复制的文本可以跟行键不同：状态码复制「404 Not Found」，动词复制三种形式。
		function rowcopy(set, row){
			return set.copyfn ? String(set.copyfn(row)) : rowkey(set, row);
		}

		SK.regutil(name, {
			title: spec.title,
			body: function(t){
				var cls = setnow().cls || "";
				var kids = [
					{ x: "div.uquery", c: [
						{ x: "input#" + name + "-q", type: "text", oninput: function(){ paint(); } },
						{ x: "span#" + name + "-msg.msg" }
					] }
				];
				if (sets.length > 1)
					kids.push({ x: "div.refchips.refsets#" + name + "-sets", onclick: function(ev){ onset(ev); } });
				kids.push({ x: "div.refchips#" + name + "-chips", onclick: function(ev){ onchip(ev); } });
				kids.push({ x: "div.refbody", c: [
					{ x: "div.refscroll", c: [
						{ x: "table.reflist.refdata" + (cls ? "." + cls : "") + "#" + name + "-tab", onclick: function(ev){ onrow(ev); } }
					] },
					{ x: "aside.refdetail#" + name + "-detail" }
				] });
				return F.CreateDOM(t, { x: "div.reftab", c: kids });
			},
			run: function(){
				throw new Error("没有这个工具");
			},
			open: function(){
				setat = readsaved();
				cur = "all";
				picked = null;
				if (sets.length > 1) paintsets();
				reload();
			},
			hideGo: true,
			live: false,
			hideOut: true,
			// 换界面语言时重画：分组按钮、表头、行和详情都是渲染时查词典的。
			relang: function(){
				if (sets.length > 1) paintsets();
				paint();
			}
		});

		function setnow(){
			return sets[setat];
		}

		// 行数据要现取的（如不规则动词）先画空表，回来再补；切表时用 gen 丢掉过期结果。
		function reload(){
			var set = setnow();
			if (!set.load) {
				paint();
				return;
			}
			var n = ++gen;
			set.rows = set.rows || [];
			paint();
			set.load(function(rows){
				if (n !== gen) return;
				set.rows = rows || [];
				paintchips(set);
				paint();
			});
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

		function scrollbox(){
			return F.id(name + "-tab").parentNode;
		}

		function groupname(set, id){
			var i;
			for (i = 0; i < set.groups.length; i++) if (set.groups[i].id === id) return set.groups[i].name;
			return "";
		}

		function hit(row, q, set){
			if (!q) return true;
			if (set.hit) return set.hit(row, q, set);
			if (set.numeric && /^[0-9]+$/.test(q)) return String(row.c[0]).indexOf(q) === 0;
			var blob = "";
			var i;
			for (i = 0; i < row.c.length; i++) blob += row.c[i] + " " + t(set, row.c[i]) + " ";
			blob += (row.x || "") + " " + gname(set, groupname(set, row.g)) + " " + t(set, gname(set, groupname(set, row.g)));
			for (i = 0; i < (row.d || []).length; i++) blob += " " + row.d[i][0] + " " + row.d[i][1] + " " + t(set, row.d[i][1]);
			return blob.toLowerCase().indexOf(q) >= 0;
		}

		function paint(){
			var set = setnow();
			var q = query();
			var box = scrollbox();
			var cols = set.cols;
			var html = [];
			var i, j, g, rows, n;
			var total = 0;
			F.id(name + "-q").placeholder = t(set, set.placeholder || "");
			if (set.widths && set.widths.length) {
				html.push("<colgroup>");
				for (i = 0; i < cols.length; i++)
					html.push("<col" + (set.widths[i] ? ' style="width:' + set.widths[i] + '"' : "") + ">");
				html.push("</colgroup>");
			}
			html.push("<thead><tr>");
			for (i = 0; i < cols.length; i++)
				html.push("<th" + (cols[i].nowrap ? ' class="rnw"' : "") + ">" + esc(t(set, cname(set, i))) + "</th>");
			html.push("</tr></thead><tbody>");
			if (set.groups && set.groups.length) {
				for (g = 0; g < set.groups.length; g++) {
					if (cur !== "all" && cur !== set.groups[g].id) continue;
					rows = pick(set, set.groups[g].id, q);
					if (!rows.length) continue;
					total += rows.length;
					html.push('<tr class="refgh"><td colspan="' + cols.length + '">' + esc(t(set, gname(set, set.groups[g])))
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
				html = ['<tbody><tr><td class="refnone" colspan="' + cols.length + '">' + esc(t(set, "没有匹配的条目")) + "</td></tr></tbody>"];
			F.id(name + "-tab").innerHTML = html.join("");
			// 换分类或搜索都从头看；有的内核重排后还会保留旧位置，这里显式回顶部。
			box.scrollTop = 0;
			SK.msg(name + "-msg", total ? t(set, q ? (set.countHit || "%n 条") : (set.count || "共 %n 条")).replace("%n", total) : "");
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
			var key = rowkey(set, row);
			var show = rowcopy(set, row);
			var html = '<tr data-k="' + esc(key) + '" title="' + esc(t(set, "点击复制 %s").replace("%s", show)) + '"'
				+ (key === picked ? ' class="on"' : "") + ">";
			var i;
			for (i = 0; i < set.cols.length; i++) {
				var cls = [];
				if (set.cols[i].nowrap) cls.push("rnw");
				if (set.cols[i].mono) cls.push("rmono");
				if (set.mline === i) cls.push("rmulti");
				var extra = cellcls(set, i, row);
				if (extra) cls.push(extra.substring(1));
				html += "<td" + (cls.length ? ' class="' + cls.join(" ") + '"' : "") + ">"
					+ cellhtml(set, i, row) + "</td>";
			}
			return html + "</tr>";
		}

		function selectrow(set, q){
			var sel = picked ? rowby(set, picked) : null;
			if (!visible(set, sel, q))
				sel = (set.defaultPick && !q) ? rowby(set, set.defaultPick) : null;
			if (!visible(set, sel, q)) sel = firstvisible(set, q);
			if (sel) {
				picked = rowkey(set, sel);
				markrow();
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
				if (rowkey(set, set.rows[i]) === String(key)) return set.rows[i];
			}
			// defaultPick 也可以写主显示值（如魔数表的 "PNG"）。
			for (i = 0; i < set.rows.length; i++) {
				if (set.big != null && String(set.rows[i].c[set.big]) === String(key)) return set.rows[i];
			}
			return null;
		}

		function showdetail(set, row){
			var bigcls = typeof set.bigCls === "function" ? set.bigCls(row) : set.bigCls;
			var html = '<div class="refbig' + (bigcls ? " " + bigcls : "") + '">'
				+ esc(t(set, row.c[set.big != null ? set.big : 0])) + "</div>";
			if (set.sub != null) html += '<div class="refsub">' + esc(t(set, row.c[set.sub])) + "</div>";
			if (set.cn != null) html += '<div class="refcn">' + esc(t(set, row.c[set.cn])) + "</div>";
			html += '<dl class="refmeta">';
			var i;
			for (i = 0; i < (row.d || []).length; i++)
				html += "<dt>" + esc(t(set, row.d[i][0])) + "</dt><dd>" + metaval(set, row.d[i][0], row.d[i][1]) + "</dd>";
			html += "</dl>";
			// samplehtml 给需要自己拼 HTML 的表（如 MIME 的 <wbr> 换行点）用。
			var box = set.samplehtml ? set.samplehtml(row) : (set.sample ? samplehtml(set, set.sample(row)) : "");
			if (box) {
				html += '<div class="refsample-t">' + esc(t(set, set.sampleTitle || "示例")) + "</div>"
					+ '<div class="refsample">' + box + "</div>";
			}
			F.id(name + "-detail").innerHTML = html;
			// 换条目后详情从头显示，别接着上一条的滚动位置。
			F.id(name + "-detail").scrollTop = 0;
		}

		function paintchips(set){
			var box = F.id(name + "-chips");
			if (!set.groups || !set.groups.length) {
				box.innerHTML = "";
				box.hidden = true;
				return;
			}
			box.hidden = false;
			var html = ['<button type="button" data-g="all"' + (cur === "all" ? ' class="on"' : "") + ">" + esc(t(set, "全部")) + "<small>"
				+ set.rows.length + "</small></button>"];
			var i, j, n;
			for (i = 0; i < set.groups.length; i++) {
				n = 0;
				for (j = 0; j < set.rows.length; j++) if (set.rows[j].g === set.groups[i].id) n++;
				// 一个条目都没有的分组不占位置（如只收了部分变位模式的动词表）。
				if (!n) continue;
				html.push('<button type="button" data-g="' + set.groups[i].id + '"'
					+ (set.groups[i].title ? ' title="' + esc(t(set, set.groups[i].title)) + '"' : "")
					+ (cur === set.groups[i].id ? ' class="on"' : "") + ">" + esc(t(set, gname(set, set.groups[i])))
					+ "<small>" + n + "</small></button>");
			}
			box.innerHTML = html.join("");
		}

		function paintsets(){
			var html = [];
			var i;
			if (setsLabel) html.push('<span class="reflabel">' + esc(t(setnow(), setsLabel)) + "</span>");
			for (i = 0; i < sets.length; i++) {
				html.push('<button type="button" data-s="' + i + '"'
					+ (i === setat ? ' class="on"' : "") + ">" + esc(t(sets[i], sets[i].name))
					+ "<small>" + sets[i].rows.length + "</small></button>");
			}
			F.id(name + "-sets").innerHTML = html.join("");
		}

		function onchip(ev){
			var el = ev.target;
			while (el && el !== this) {
				if (el.getAttribute && F.attr(el, "data-g")) {
					cur = F.attr(el, "data-g");
					// 换分类从头看：列表回顶部，选中项也回到该分类第一条。
					picked = null;
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

		// 点行只换高亮和右侧详情，不重建表格，免得列表跟着跳。
		function markrow(){
			var trs = F.qa("tr[data-k]", F.id(name + "-tab"));
			var i;
			for (i = 0; i < trs.length; i++)
				trs[i].className = F.attr(trs[i], "data-k") === picked ? "on" : "";
		}

		function onrow(ev){
			var el = ev.target;
			while (el && el.tagName !== "TR") el = el.parentNode;
			if (!el || !F.attr(el, "data-k")) return;
			var set = setnow();
			var row = rowby(set, F.attr(el, "data-k"));
			if (!row) return;
			picked = rowkey(set, row);
			markrow();
			showdetail(set, row);
			SK.copytext(rowcopy(set, row), name + "-msg");
		}
	};

	function settext(set, s){
		return (set.tr || SK.td)(s);
	}

	function samplehtml(set, text){
		// 代码示例保留换行；逐行高亮，注释行自然变灰，命令、参数、变量各有颜色。
		var parts = String(text == null ? "" : text).split("\n");
		var i;
		for (i = 0; i < parts.length; i++) {
			var line = SK.tdline(parts[i]);
			parts[i] = (set.hl && SK.hl) ? SK.hl(set.hl, line) : esc(line);
		}
		return parts.join("<br>");
	}

	var esc = SK.esc;

	// 只有标了 mono 的列才着色，说明列保持纯文本；复制仍用原始字符串。
	// mline 指向的列直接渲染多行示例，换行、注释和高亮都跟详情里一致。
	function cellhtml(set, i, row){
		if (set.mline === i && row.s) return samplehtml(set, row.s);
		var val = settext(set, row.c[i]);
		if (set.hl && SK.hl && set.cols[i] && set.cols[i].mono === true) return SK.hl(set.hl, val);
		return esc(val);
	}

	function metaval(set, label, val){
		if (set.hl && SK.hl && (label === "示例" || label === "常用示例")) return SK.hl(set.hl, settext(set, val));
		return esc(settext(set, val));
	}
})();
