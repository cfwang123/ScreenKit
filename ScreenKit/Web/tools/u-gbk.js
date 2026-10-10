(function(){
	var gbkPages = null;
	var gbkOf = null;
	var gbkLead = null;
	var gbkBits = null;
	var GBK_BITS = "/////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9/AAAAAAAAAAAAAAAA/v////////////9/AAAAAAAAAAAAAAAA/gf+///////nf/4fAAAAAAAAAAAAAAAA/v////////////9/AAAAAAAAAAAAAAAA/v///////////w8AAAAAAAAAAAAAAAAA/v///////////38AAAAAAAAAAAAAAAAA/v//Af7//wH/zzcAAAAAAAAAAAAAAAAA/v///wMA/v///wMA/////////3///z8A/v//b+H/////AwAA////Fv///3//AUAA8P///////////wAA/////////3//////AQAAAAAAAAAAAAAA/////////3//////AQAAAAAAAAAAAAAA/////////3//////AQAAAAAAAAAAAAAA/////////3//////AQAAAAAAAAAAAAAA/////////3//////AQAAAAAAAAAAAAAA/////////3//////AQAAAAAAAAAAAAAA/////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////8D/////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3////////////////////9//////////3//////AQAAAAAAAAAAAAAA/////////3//////AQAAAAAAAAAAAAAA/////////3//////AQAAAAAAAAAAAAAA/////////3//////AQAAAAAAAAAAAAAA/////////3//////AQAAAAAAAAAAAAAA/////////3//////AQAAAAAAAAAAAAAA//8AAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
	SK.regutil("u-gbk", {
		title: "GBK码表",
		go: "显示",
		body: function(){
			return '<div class="uquery"><input id="gbk-q" type="text" placeholder="字、B0A1 或 U+4E00"><span id="gbk-msg" class="msg"></span></div>'
				+ '<p class="hint">点一下选中并复制。左边是首字节，每页 16 列。空位没有字符。</p>'
				+ '<div class="unitab"><div class="ugroups" id="gbk-groups"></div><div class="ugrid g16" id="gbk-grid"></div>'
				+ '<aside class="uprev"><div class="uprev-ch" id="gbk-ch"></div><dl class="uprev-meta" id="gbk-meta"></dl></aside></div>';
		},
		run: function(){
			throw new Error("没有这个工具");
		},
		open: function(){
			gbkbuild();
		},
		hideGo: true,
		hideOut: true
	});

	function gbkbuild(){
		var pages = gbkpages();
		if (!pages) {
			SK.$("gbk-groups").innerHTML = "";
			SK.$("gbk-grid").innerHTML = "";
			SK.msg("gbk-msg", "这个浏览器不能解码 GBK", true);
			return;
		}
		var html = [];
		var i;
		for (i = 0; i < pages.length; i++)
			html.push('<button type="button" data-i="' + i + '" title="' + gbkesc(pages[i].name) + '">' + gbkesc(pages[i].name) + '</button>');
		SK.$("gbk-groups").innerHTML = html.join("");
		SK.$("gbk-groups").onclick = function(ev){
			var el = ev.target;
			while (el && el.id !== "gbk-groups") {
				if (el.getAttribute && el.getAttribute("data-i") != null) {
					gbkshow(parseInt(el.getAttribute("data-i"), 10), -1);
					return;
				}
				el = el.parentNode;
			}
		};
		SK.$("gbk-grid").onclick = function(ev){
			var el = ev.target;
			while (el && el.id !== "gbk-grid") {
				if (el.getAttribute && el.getAttribute("data-j") != null) {
					gbkpick(parseInt(el.getAttribute("data-j"), 10), true);
					return;
				}
				el = el.parentNode;
			}
		};
		SK.$("gbk-q").oninput = gbkquery;
		gbkshow(0, -1);
	}

	function gbkquery(){
		var hit = gbkparse(SK.$("gbk-q").value);
		if (!hit) {
			SK.msg("gbk-msg", "");
			return;
		}
		if (hit.miss) {
			SK.msg("gbk-msg", hit.miss, true);
			return;
		}
		SK.msg("gbk-msg", gbkbytes(hit.cell));
		gbkshow(hit.i, hit.j);
	}

	function gbkshow(index, focus){
		var page = gbkpages()[index];
		var groups = document.querySelectorAll("#gbk-groups button");
		var i;
		for (i = 0; i < groups.length; i++)
			groups[i].className = i === index ? "on" : "";
		if (groups[index]) groups[index].scrollIntoView({ block: "nearest" });
		var pick = focus >= 0 ? focus : gbkfirst(page);
		var html = [];
		for (i = 0; i < page.cells.length; i++) {
			var cell = page.cells[i];
			var glyph = gbkglyph(cell);
			var cls = glyph.ctrl ? "ctrl" : (glyph.empty ? "gap" : "");
			html.push('<button type="button" data-j="' + i + '" title="' + gbkesc(gbktitle(cell)) + '"'
				+ (cls ? ' class="' + cls + '"' : '') + (i === pick ? ' id="gbk-on"' : '') + '>'
				+ glyph.t + '</button>');
		}
		SK.$("gbk-grid").innerHTML = html.join("");
		var on = SK.$("gbk-on");
		if (on) {
			on.className = (on.className ? on.className + " " : "") + "on";
			on.removeAttribute("id");
			if (focus >= 0) on.scrollIntoView({ block: "center" });
		}
		gbkcur = index;
		gbkpreview(page.cells[pick]);
	}

	var gbkcur = 0;

	function gbkpick(j, copy){
		var cell = gbkpages()[gbkcur].cells[j];
		gbkmark(j);
		gbkpreview(cell);
		if (cell.cp < 0) {
			SK.msg("gbk-msg", "空位", true);
			return;
		}
		SK.msg("gbk-msg", gbkbytes(cell));
		if (copy) SK.copytext(String.fromCodePoint(cell.cp), "gbk-msg");
	}

	function gbkmark(j){
		var buttons = document.querySelectorAll("#gbk-grid button");
		var i, cls;
		for (i = 0; i < buttons.length; i++) {
			cls = buttons[i].className.replace(/\bon\b/g, "").replace(/^\s+|\s+$/g, "").replace(/\s+/g, " ");
			if (parseInt(buttons[i].getAttribute("data-j"), 10) === j)
				cls = cls ? cls + " on" : "on";
			buttons[i].className = cls;
		}
	}

	function gbkpreview(cell){
		var glyph = gbkglyph(cell);
		var box = SK.$("gbk-ch");
		if (glyph.empty) {
			box.textContent = "";
			box.className = "uprev-ch gap";
		}
		else if (glyph.ctrl) {
			box.textContent = glyph.t;
			box.className = "uprev-ch ctrl";
		}
		else {
			box.textContent = String.fromCodePoint(cell.cp);
			box.className = "uprev-ch";
		}
		var rows = [
			["GBK", gbkbytes(cell)],
			["Unicode", cell.cp < 0 ? "" : "U+" + gbkhex(cell.cp, 4)],
			["十进制", cell.cp < 0 ? "" : String(cell.cp)],
			["UTF-8", cell.cp < 0 ? "" : gbkutf8(cell.cp)]
		];
		var qw = gbkquwei(cell);
		if (qw) rows.splice(1, 0, ["区位", qw]);
		if (cell.cp < 0) rows.push(["说明", "这个位置没有字符。"]);
		var html = [];
		var i;
		for (i = 0; i < rows.length; i++)
			html.push("<dt>" + rows[i][0] + "</dt><dd>" + gbkesc(rows[i][1]) + "</dd>");
		SK.$("gbk-meta").innerHTML = html.join("");
	}

	function gbkpages(){
		if (gbkPages) return gbkPages;
		var dec;
		try { dec = new TextDecoder("gbk"); }
		catch (e) { return null; }
		gbkOf = {};
		gbkLead = {};
		gbkPages = [];
		var ascii = [];
		var b;
		for (b = 0; b < 128; b++) {
			var acell = { lead: b, trail: -1, cp: b, hex: gbkhex(b, 2) };
			ascii.push(acell);
			gbkOf[b] = { i: 0, j: b };
		}
		gbkPages.push({ name: "ASCII", cells: ascii });
		var lead;
		for (lead = 0x81; lead <= 0xFE; lead++) {
			var cells = [];
			var trail;
			var any = -1;
			for (trail = 0x40; trail <= 0xFF; trail++) {
				var cp = trail === 0x7F || trail === 0xFF ? -1 : gbkdec(dec, lead, trail);
				var cell = { lead: lead, trail: trail, cp: cp, hex: gbkhex(lead, 2) + gbkhex(trail, 2) };
				if (cp >= 0) {
					if (any < 0) any = cp;
					if (gbkOf[cp] == null) gbkOf[cp] = { i: gbkPages.length, j: cells.length };
				}
				cells.push(cell);
			}
			if (any < 0) continue;
			gbkLead[lead] = gbkPages.length;
			gbkPages.push({ name: gbkhex(lead, 2) + " " + String.fromCodePoint(any), cells: cells });
		}
		return gbkPages;
	}

	function gbkdec(dec, lead, trail){
		var index = (lead - 0x81) * 192 + (trail - 0x40);
		if (!gbkok(index)) return -1;
		var s = dec.decode(new Uint8Array([lead, trail]));
		if (!s) return -1;
		var cp = s.codePointAt(0);
		if (cp === 0xFFFD) return -1;
		if (s.length !== (cp > 0xFFFF ? 2 : 1)) return -1;
		return cp;
	}

	function gbkparse(raw){
		raw = String(raw).replace(/^\s+|\s+$/g, "");
		if (!raw) return null;
		var pages = gbkpages();
		if (!pages) return { miss: "这个浏览器不能解码 GBK" };
		var uni = /^(?:[uU]\+)([0-9a-fA-F]{1,6})$/.exec(raw);
		if (uni) return gbkbycp(parseInt(uni[1], 16));
		if (/^[0-9]{1,7}$/.test(raw)) return gbkbycp(parseInt(raw, 10));
		var hex = raw.replace(/^0x/i, "").replace(/\s+/g, "");
		if (/^[0-9a-fA-F]{2}$/.test(hex)) {
			var hit2 = gbkbybyte(parseInt(hex, 16), -1);
			if (hit2) return hit2;
			return { miss: "没有这个编码" };
		}
		if (/^[0-9a-fA-F]{4}$/.test(hex)) {
			var n4 = parseInt(hex, 16);
			var lead4 = n4 >> 8;
			var trail4 = n4 & 0xFF;
			if (lead4 >= 0x81 && lead4 <= 0xFE) {
				var hit4 = gbkbybyte(lead4, trail4);
				if (hit4) return hit4;
				return { miss: "没有这个编码" };
			}
			return gbkbycp(n4);
		}
		var cp = raw.codePointAt(0);
		if (raw.length === (cp > 0xFFFF ? 2 : 1)) return gbkbycp(cp);
		return { miss: "没有这个编码" };
	}

	function gbkbycp(cp){
		if (cp < 0 || cp > 0x10FFFF) return { miss: "没有这个编码" };
		var at = gbkOf[cp];
		if (!at) return { miss: "这个字不在 GBK 里" };
		return { i: at.i, j: at.j, cell: gbkpages()[at.i].cells[at.j] };
	}

	function gbkbybyte(lead, trail){
		if (trail < 0) {
			if (lead < 0 || lead > 0x7F) return null;
			var cell = gbkpages()[0].cells[lead];
			return { i: 0, j: lead, cell: cell };
		}
		var pi = gbkLead[lead];
		if (pi == null || trail < 0x40 || trail > 0xFF) return null;
		var j = trail - 0x40;
		return { i: pi, j: j, cell: gbkpages()[pi].cells[j] };
	}

	function gbkfirst(page){
		var i;
		for (i = 0; i < page.cells.length; i++) if (page.cells[i].cp >= 0) return i;
		return 0;
	}

	function gbkglyph(cell){
		var names = ["NUL", "SOH", "STX", "ETX", "EOT", "ENQ", "ACK", "BEL", "BS", "HT", "LF", "VT", "FF", "CR", "SO", "SI", "DLE", "DC1", "DC2", "DC3", "DC4", "NAK", "SYN", "ETB", "CAN", "EM", "SUB", "ESC", "FS", "GS", "RS", "US"];
		if (cell.cp < 0) return { t: "", empty: true };
		if (cell.cp < 32) return { t: names[cell.cp], ctrl: true };
		if (cell.cp === 0x7F) return { t: "DEL", ctrl: true };
		var ch = String.fromCodePoint(cell.cp);
		if (ch === "&") ch = "&amp;";
		else if (ch === "<") ch = "&lt;";
		else if (ch === ">") ch = "&gt;";
		return { t: ch };
	}

	function gbktitle(cell){
		if (cell.cp < 0) return gbkbytes(cell) + " 空位";
		return gbkbytes(cell) + " U+" + gbkhex(cell.cp, 4);
	}

	function gbkbytes(cell){
		if (cell.trail < 0) return gbkhex(cell.lead, 2);
		return gbkhex(cell.lead, 2) + " " + gbkhex(cell.trail, 2);
	}

	function gbkquwei(cell){
		if (cell.trail < 0) return "";
		if (cell.lead < 0xA1 || cell.lead > 0xF7) return "";
		if (cell.trail < 0xA1 || cell.trail > 0xFE) return "";
		var qu = cell.lead - 0xA0;
		var wei = cell.trail - 0xA0;
		return (qu < 10 ? "0" : "") + qu + "-" + (wei < 10 ? "0" : "") + wei;
	}

	function gbkutf8(cp){
		var bytes;
		if (cp < 0x80) bytes = [cp];
		else if (cp < 0x800) bytes = [0xC0 | (cp >> 6), 0x80 | (cp & 0x3F)];
		else if (cp < 0x10000) bytes = [0xE0 | (cp >> 12), 0x80 | ((cp >> 6) & 0x3F), 0x80 | (cp & 0x3F)];
		else bytes = [0xF0 | (cp >> 18), 0x80 | ((cp >> 12) & 0x3F), 0x80 | ((cp >> 6) & 0x3F), 0x80 | (cp & 0x3F)];
		var i, out = [];
		for (i = 0; i < bytes.length; i++) out.push(gbkhex(bytes[i], 2));
		return out.join(" ");
	}

	function gbkok(index){
		var bits = gbkbits();
		if (!bits) return false;
		return ((bits[index >> 3] >> (index & 7)) & 1) === 1;
	}

	function gbkbits(){
		if (gbkBits) return gbkBits;
		if (!GBK_BITS) return null;
		var bin = atob(GBK_BITS);
		var out = new Uint8Array(bin.length);
		var i;
		for (i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
		gbkBits = out;
		return out;
	}

	function gbkhex(n, width){
		var s = n.toString(16).toUpperCase();
		while (s.length < width) s = "0" + s;
		return s;
	}

	function gbkesc(s){
		return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;");
	}

	SK.gbkof = function(cp){
		if (!gbkpages() || gbkOf[cp] == null) return "";
		var at = gbkOf[cp];
		return gbkbytes(gbkPages[at.i].cells[at.j]);
	};
})();
