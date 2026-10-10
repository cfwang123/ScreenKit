(function(){
	var uniSlices = null;
	var uniCan = null;
	var uniNot = null;
	var uniInk = {};
	var uniSeen = {};
	SK.regutil("u-ascii", {
		title: "Unicode码表",
		go: "显示",
		body: function(){
			return '<div class="uquery"><input id="u-q" type="text" placeholder="字、U+4E00 或十进制"><span id="u-msg" class="msg"></span></div>'
				+ '<p class="hint">点一下选中并复制。最右边是大字和这个字符的编码。</p>'
				+ '<div class="unitab"><div class="ugroups" id="u-groups"></div><div class="ugrid" id="u-grid"></div>'
				+ '<aside class="uprev"><div class="uprev-ch" id="u-ch"></div><dl class="uprev-meta" id="u-meta"></dl></aside></div>';
		},
		run: function(){
			throw new Error("没有这个工具");
		},
		open: function(){
			unibuild();
		},
		hideGo: true,
		live: false,
		hideOut: true
	});
	function unibuild(){
		var slices = unislices();
		var html = [];
		var i;
		for (i = 0; i < slices.length; i++)
			html.push('<button type="button" data-i="' + i + '" title="' + slices[i][0] + '">' + slices[i][0] + '</button>');
		SK.$("u-groups").innerHTML = html.join("");
		SK.$("u-groups").onclick = function(ev){
			var el = ev.target;
			while (el && el.id !== "u-groups") {
				if (el.getAttribute && el.getAttribute("data-i") != null) {
					unishow(parseInt(el.getAttribute("data-i"), 10), -1);
					return;
				}
				el = el.parentNode;
			}
		};
		SK.$("u-grid").onclick = function(ev){
			var el = ev.target;
			while (el && el.id !== "u-grid") {
				if (el.getAttribute && el.getAttribute("data-cp") != null) {
					var cp = parseInt(el.getAttribute("data-cp"), 10);
					unimark(cp);
					unipreview(cp);
					SK.copytext(String.fromCodePoint(cp), "u-msg");
					return;
				}
				el = el.parentNode;
			}
		};
		SK.$("u-q").oninput = uniquery;
		unishow(0, -1);
	}

	function uniquery(){
		var cp = uniparse(SK.$("u-q").value);
		if (cp === -1) {
			SK.msg("u-msg", "");
			return;
		}
		if (cp < 0) {
			SK.msg("u-msg", "没有这个码位", true);
			return;
		}
		var i = unifind(cp);
		if (i < 0) {
			SK.msg("u-msg", "没有这个码位", true);
			return;
		}
		SK.msg("u-msg", "U+" + unihex(cp));
		unishow(i, cp);
	}

	function unishow(index, focus){
		var slices = unislices();
		var g = slices[index];
		var groups = document.querySelectorAll("#u-groups button");
		var i;
		for (i = 0; i < groups.length; i++)
			groups[i].className = i === index ? "on" : "";
		if (groups[index]) groups[index].scrollIntoView({ block: "nearest" });
		var pick = focus >= g[1] && focus <= g[2] ? focus : g[1];
		var html = [];
		var cp;
		for (cp = g[1]; cp <= g[2]; cp++) {
			var glyph = uniglyph(cp);
			var cls = glyph.ctrl ? "ctrl" : (glyph.empty ? "gap" : (glyph.alt ? "alt" : ""));
			html.push('<button type="button" data-cp="' + cp + '" title="U+' + unihex(cp) + '"'
				+ (cls ? ' class="' + cls + '"' : '') + (cp === pick ? ' id="u-on"' : '') + '>'
				+ glyph.t + '</button>');
		}
		SK.$("u-grid").innerHTML = html.join("");
		var on = SK.$("u-on");
		if (on) {
			on.className = (on.className ? on.className + " " : "") + "on";
			on.removeAttribute("id");
			if (focus >= g[1] && focus <= g[2]) on.scrollIntoView({ block: "center" });
		}
		unipreview(pick);
	}

	function unimark(cp){
		var buttons = document.querySelectorAll("#u-grid button");
		var i, cls;
		for (i = 0; i < buttons.length; i++) {
			cls = buttons[i].className.replace(/\bon\b/g, "").replace(/^\s+|\s+$/g, "").replace(/\s+/g, " ");
			if (parseInt(buttons[i].getAttribute("data-cp"), 10) === cp)
				cls = cls ? cls + " on" : "on";
			buttons[i].className = cls;
		}
	}

	function unipreview(cp){
		var glyph = uniglyph(cp);
		var box = SK.$("u-ch");
		if (glyph.empty) {
			box.textContent = "";
			box.className = "uprev-ch gap";
		}
		else if (glyph.ctrl) {
			box.textContent = glyph.t;
			box.className = "uprev-ch ctrl";
		}
		else {
			var show = glyph.alt || cp;
			var ch = String.fromCodePoint(show);
			if (unicomb(show)) ch = "\u25CC" + ch;
			box.textContent = ch;
			box.className = "uprev-ch";
		}
		var rows = [
			["码位", "U+" + unihex(cp)],
			["十进制", String(cp)],
			["分组", uniblock(cp)],
			["平面", uniplane(cp)],
			["类型", unikind(cp)],
			["UTF-8", uniutf8(cp)],
			["UTF-16", uniutf16(cp)],
			["HTML", "&#" + cp + ";  &#x" + unihex(cp) + ";"],
			["JavaScript", unijs(cp)],
			["URL", uniurl(cp)]
		];
		if (glyph.empty) {
			if (unicn(cp))
				rows.push(["说明", "这个码位没有分配字符，没有字体可以显示。"]);
			else
				rows.push(["说明", "本机字体没有这个字。花园明朝（HanaMin）或 BabelStone Han 里可能有。"]);
		}
		if (glyph.alt)
			rows.push(
				["等价", "U+" + unihex(glyph.alt) + " " + String.fromCodePoint(glyph.alt)],
				["字形", "本机字体没有单独字形，格内是等价汉字。复制的仍是原码位。花园明朝（HanaMin）或 BabelStone Han 有独立字形。"]
			);
		var html = [];
		var i;
		for (i = 0; i < rows.length; i++)
			html.push("<dt>" + rows[i][0] + "</dt><dd>" + unihtml(rows[i][1]) + "</dd>");
		SK.$("u-meta").innerHTML = html.join("");
	}

	function unicn(cp){
		return /^\p{Cn}$/u.test(String.fromCodePoint(cp));
	}

	function unihtml(s){
		return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;");
	}

	function uniblock(cp){
		var i = unifind(cp);
		if (i < 0) return "未收录";
		return unislices()[i][0];
	}

	function uniplane(cp){
		if (cp <= 0xFFFF) return "基本多文种平面";
		if (cp <= 0x1FFFF) return "第一辅助平面";
		if (cp <= 0x2FFFF) return "第二辅助平面";
		return "其他平面";
	}

	function unikind(cp){
		if (cp < 32 || cp === 0x7F || (cp >= 0x80 && cp <= 0x9F)) return "控制字符";
		if ((cp & 0xFFFF) === 0xFFFE || (cp & 0xFFFF) === 0xFFFF) return "非字符";
		if (unicomb(cp)) return "组合字符";
		if (cp === 0x20 || cp === 0xA0 || cp === 0xAD
			|| (cp >= 0x2000 && cp <= 0x200A) || cp === 0x2028 || cp === 0x2029
			|| cp === 0x202F || cp === 0x205F || cp === 0x3000 || cp === 0xFEFF) return "空白";
		return "图形字符";
	}

	function uniutf8(cp){
		var bytes;
		if (cp < 0x80) bytes = [cp];
		else if (cp < 0x800) bytes = [0xC0 | (cp >> 6), 0x80 | (cp & 0x3F)];
		else if (cp < 0x10000) bytes = [0xE0 | (cp >> 12), 0x80 | ((cp >> 6) & 0x3F), 0x80 | (cp & 0x3F)];
		else bytes = [0xF0 | (cp >> 18), 0x80 | ((cp >> 12) & 0x3F), 0x80 | ((cp >> 6) & 0x3F), 0x80 | (cp & 0x3F)];
		var i, out = [];
		for (i = 0; i < bytes.length; i++) {
			var h = bytes[i].toString(16).toUpperCase();
			if (h.length < 2) h = "0" + h;
			out.push(h);
		}
		return out.join(" ");
	}

	function uniutf16(cp){
		if (cp <= 0xFFFF) return unihex(cp);
		var u = cp - 0x10000;
		return unihex(0xD800 + (u >> 10)) + " " + unihex(0xDC00 + (u & 0x3FF));
	}

	function unijs(cp){
		var hex = unihex(cp).toLowerCase();
		if (cp <= 0xFFFF) return "\\u" + hex;
		var pair = uniutf16(cp).toLowerCase().split(" ");
		return "\\u{" + hex + "}   \\u" + pair[0] + "\\u" + pair[1];
	}

	function uniurl(cp){
		var bytes = uniutf8(cp).split(" ");
		var i, out = "";
		for (i = 0; i < bytes.length; i++) out += "%" + bytes[i];
		return out;
	}

	function uniparse(raw){
		raw = String(raw).replace(/^\s+|\s+$/g, "");
		if (!raw) return -1;
		var m = /^(?:[uU]\+|0x)([0-9a-fA-F]{1,6})$/.exec(raw);
		if (m) return parseInt(m[1], 16);
		if (/^[0-9]{1,7}$/.test(raw)) {
			var n = parseInt(raw, 10);
			if (n <= 0x10FFFF) return n;
			return -2;
		}
		if (/^[0-9a-fA-F]{2,6}$/.test(raw) && /[a-fA-F]/.test(raw)) return parseInt(raw, 16);
		if (raw.length > 8) return -1;
		var cp = raw.codePointAt(0);
		if (raw.length === (cp > 0xFFFF ? 2 : 1)) return cp;
		return -1;
	}

	function unifind(cp){
		var slices = unislices();
		var i;
		for (i = 0; i < slices.length; i++) {
			if (cp >= slices[i][1] && cp <= slices[i][2]) return i;
		}
		return -1;
	}

	function unihex(cp){
		var s = cp.toString(16).toUpperCase();
		while (s.length < 4) s = "0" + s;
		return s;
	}

	function uniglyph(cp){
		var names = ["NUL", "SOH", "STX", "ETX", "EOT", "ENQ", "ACK", "BEL", "BS", "HT", "LF", "VT", "FF", "CR", "SO", "SI", "DLE", "DC1", "DC2", "DC3", "DC4", "NAK", "SYN", "ETB", "CAN", "EM", "SUB", "ESC", "FS", "GS", "RS", "US"];
		if (cp < 32) return { t: names[cp], ctrl: true };
		if (cp === 0x7F) return { t: "DEL", ctrl: true };
		if (cp >= 0x80 && cp <= 0x9F) return { t: unihex(cp), ctrl: true };
		if ((cp & 0xFFFF) === 0xFFFE || (cp & 0xFFFF) === 0xFFFF) return { t: unihex(cp), ctrl: true };
		if (cp === 0xAD || (cp >= 0x200B && cp <= 0x200F) || cp === 0x2028 || cp === 0x2029 || cp === 0x2060 || cp === 0xFEFF)
			return { t: unihex(cp), ctrl: true, empty: false, alt: 0 };
		var gap = unigap(cp);
		if (gap.empty) return { t: "", ctrl: false, empty: true, alt: 0 };
		var show = gap.alt || cp;
		var ch = String.fromCodePoint(show);
		if (unicomb(show)) ch = "\u25CC" + ch;
		if (ch === "&") ch = "&amp;";
		else if (ch === "<") ch = "&lt;";
		else if (ch === ">") ch = "&gt;";
		return { t: ch, ctrl: false, empty: false, alt: gap.alt };
	}

	function unigap(cp){
		if (uniSeen[cp]) return uniSeen[cp];
		var ch = String.fromCodePoint(cp);
		var nfkc = ch.normalize ? ch.normalize("NFKC") : ch;
		var eq = nfkc.codePointAt(0);
		var one = nfkc.length === (eq > 0xFFFF ? 2 : 1);
		var alt = 0;
		var miss = unimiss(cp);
		if (miss && one && eq !== cp && !unimiss(eq)) alt = eq;
		var rec = { empty: miss && !alt, alt: alt };
		uniSeen[cp] = rec;
		return rec;
	}

	function unimiss(cp){
		if (uniInk[cp] != null) return uniInk[cp];
		if (uniNot == null) {
			var a = uniink(0xFDD0);
			var b = uniink(0xFDD1);
			uniNot = a === b ? a : -1;
		}
		var miss = uniNot >= 0 && uniink(cp) === uniNot;
		uniInk[cp] = miss;
		return miss;
	}

	function uniink(cp){
		var c = uniCan;
		if (!c) {
			c = document.createElement("canvas");
			c.width = 24;
			c.height = 24;
			uniCan = c;
		}
		var g = c.getContext("2d", { willReadFrequently: true });
		g.clearRect(0, 0, 24, 24);
		g.font = "16px \"Segoe UI Emoji\",\"Segoe UI Symbol\",\"Microsoft YaHei\",\"Meiryo\",\"Yu Gothic\",\"Microsoft JhengHei\",\"MingLiU\",\"Noto Sans SC\",\"Noto Sans JP\",\"Noto Sans KR\",sans-serif";
		g.fillStyle = "#000";
		g.textAlign = "center";
		g.textBaseline = "middle";
		g.fillText(String.fromCodePoint(cp), 12, 12);
		var d = g.getImageData(0, 0, 24, 24).data;
		var n = 0;
		var i;
		for (i = 3; i < d.length; i += 4) n += d[i];
		return n;
	}

	function unicomb(cp){
		return (cp >= 0x0300 && cp <= 0x036F)
			|| (cp >= 0x0483 && cp <= 0x0489)
			|| (cp >= 0x0591 && cp <= 0x05BD)
			|| cp === 0x05BF || (cp >= 0x05C1 && cp <= 0x05C2) || cp === 0x05C4 || cp === 0x05C5 || cp === 0x05C7
			|| (cp >= 0x0610 && cp <= 0x061A) || (cp >= 0x064B && cp <= 0x065F) || cp === 0x0670
			|| (cp >= 0x06D6 && cp <= 0x06ED)
			|| (cp >= 0x1AB0 && cp <= 0x1AFF)
			|| (cp >= 0x1DC0 && cp <= 0x1DFF)
			|| (cp >= 0x20D0 && cp <= 0x20FF)
			|| (cp >= 0xFE20 && cp <= 0xFE2F);
	}

	function unislices(){
		if (uniSlices) return uniSlices;
		var src = [
			["基本拉丁", 0x0000, 0x007F],
			["拉丁补充", 0x0080, 0x00FF],
			["拉丁扩展 A", 0x0100, 0x017F],
			["拉丁扩展 B", 0x0180, 0x024F],
			["国际音标", 0x0250, 0x02AF],
			["修饰字母", 0x02B0, 0x02FF],
			["组合附加", 0x0300, 0x036F],
			["希腊文", 0x0370, 0x03FF],
			["西里尔文", 0x0400, 0x04FF],
			["西里尔补充", 0x0500, 0x052F],
			["亚美尼亚文", 0x0530, 0x058F],
			["希伯来文", 0x0590, 0x05FF],
			["阿拉伯文", 0x0600, 0x06FF],
			["叙利亚文", 0x0700, 0x074F],
			["阿拉伯补充", 0x0750, 0x077F],
			["它拿文", 0x0780, 0x07BF],
			["西非书面文", 0x07C0, 0x07FF],
			["撒马利亚文", 0x0800, 0x083F],
			["曼达文", 0x0840, 0x085F],
			["叙利亚补充", 0x0860, 0x086F],
			["阿拉伯扩展 B", 0x0870, 0x089F],
			["阿拉伯扩展 A", 0x08A0, 0x08FF],
			["天城文", 0x0900, 0x097F],
			["孟加拉文", 0x0980, 0x09FF],
			["果鲁穆奇文", 0x0A00, 0x0A7F],
			["古吉拉特文", 0x0A80, 0x0AFF],
			["奥里亚文", 0x0B00, 0x0B7F],
			["泰米尔文", 0x0B80, 0x0BFF],
			["泰卢固文", 0x0C00, 0x0C7F],
			["卡纳达文", 0x0C80, 0x0CFF],
			["马拉雅拉姆文", 0x0D00, 0x0D7F],
			["僧伽罗文", 0x0D80, 0x0DFF],
			["泰文", 0x0E00, 0x0E7F],
			["老挝文", 0x0E80, 0x0EFF],
			["藏文", 0x0F00, 0x0FFF],
			["缅甸文", 0x1000, 0x109F],
			["格鲁吉亚文", 0x10A0, 0x10FF],
			["谚文字母", 0x1100, 0x11FF],
			["埃塞俄比亚文", 0x1200, 0x137F],
			["埃塞俄比亚补充", 0x1380, 0x139F],
			["切罗基文", 0x13A0, 0x13FF],
			["加拿大音节", 0x1400, 0x167F],
			["欧甘文", 0x1680, 0x169F],
			["卢恩文", 0x16A0, 0x16FF],
			["他加禄文", 0x1700, 0x171F],
			["哈努诺文", 0x1720, 0x173F],
			["布希德文", 0x1740, 0x175F],
			["塔格班瓦文", 0x1760, 0x177F],
			["高棉文", 0x1780, 0x17FF],
			["蒙古文", 0x1800, 0x18AF],
			["加拿大音节扩展", 0x18B0, 0x18FF],
			["林布文", 0x1900, 0x194F],
			["德宏傣文", 0x1950, 0x197F],
			["新傣仂文", 0x1980, 0x19DF],
			["高棉符号", 0x19E0, 0x19FF],
			["布吉文", 0x1A00, 0x1A1F],
			["老傣文", 0x1A20, 0x1AAF],
			["组合附加扩展", 0x1AB0, 0x1AFF],
			["巴厘文", 0x1B00, 0x1B7F],
			["巽他文", 0x1B80, 0x1BBF],
			["巴塔克文", 0x1BC0, 0x1BFF],
			["雷布查文", 0x1C00, 0x1C4F],
			["桑塔利文", 0x1C50, 0x1C7F],
			["西里尔扩展 C", 0x1C80, 0x1C8F],
			["格鲁吉亚扩展", 0x1C90, 0x1CBF],
			["巽他补充", 0x1CC0, 0x1CCF],
			["吠陀扩展", 0x1CD0, 0x1CFF],
			["音标扩展", 0x1D00, 0x1D7F],
			["音标扩展补充", 0x1D80, 0x1DBF],
			["组合附加补充", 0x1DC0, 0x1DFF],
			["拉丁扩展附加", 0x1E00, 0x1EFF],
			["希腊文扩展", 0x1F00, 0x1FFF],
			["常用标点", 0x2000, 0x206F],
			["上下标", 0x2070, 0x209F],
			["货币符号", 0x20A0, 0x20CF],
			["符号用附加", 0x20D0, 0x20FF],
			["字母式符号", 0x2100, 0x214F],
			["数字形式", 0x2150, 0x218F],
			["箭头", 0x2190, 0x21FF],
			["数学运算符", 0x2200, 0x22FF],
			["杂项技术符号", 0x2300, 0x23FF],
			["控制图片", 0x2400, 0x243F],
			["光学识别", 0x2440, 0x245F],
			["带圈字母数字", 0x2460, 0x24FF],
			["制表符", 0x2500, 0x257F],
			["方块元素", 0x2580, 0x259F],
			["几何形状", 0x25A0, 0x25FF],
			["杂项符号", 0x2600, 0x26FF],
			["装饰符号", 0x2700, 0x27BF],
			["数学符号 A", 0x27C0, 0x27EF],
			["箭头补充 A", 0x27F0, 0x27FF],
			["盲文", 0x2800, 0x28FF],
			["箭头补充 B", 0x2900, 0x297F],
			["数学符号 B", 0x2980, 0x29FF],
			["数学运算符补充", 0x2A00, 0x2AFF],
			["符号和箭头", 0x2B00, 0x2BFF],
			["格拉哥里文", 0x2C00, 0x2C5F],
			["拉丁扩展 C", 0x2C60, 0x2C7F],
			["科普特文", 0x2C80, 0x2CFF],
			["格鲁吉亚补充", 0x2D00, 0x2D2F],
			["提非纳文", 0x2D30, 0x2D7F],
			["埃塞俄比亚扩展", 0x2D80, 0x2DDF],
			["西里尔扩展 A", 0x2DE0, 0x2DFF],
			["标点补充", 0x2E00, 0x2E7F],
			["汉字部首补充", 0x2E80, 0x2EFF],
			["康熙部首", 0x2F00, 0x2FDF],
			["表意描述符", 0x2FF0, 0x2FFF],
			["中日韩符号", 0x3000, 0x303F],
			["平假名", 0x3040, 0x309F],
			["片假名", 0x30A0, 0x30FF],
			["注音", 0x3100, 0x312F],
			["谚文兼容字母", 0x3130, 0x318F],
			["汉文", 0x3190, 0x319F],
			["注音扩展", 0x31A0, 0x31BF],
			["汉字笔画", 0x31C0, 0x31EF],
			["片假名扩展", 0x31F0, 0x31FF],
			["带圈中日韩", 0x3200, 0x32FF],
			["中日韩兼容", 0x3300, 0x33FF],
			["汉字扩展 A", 0x3400, 0x4DBF],
			["易经卦象", 0x4DC0, 0x4DFF],
			["汉字", 0x4E00, 0x9FFF],
			["彝文音节", 0xA000, 0xA48F],
			["彝文部首", 0xA490, 0xA4CF],
			["傈僳文", 0xA4D0, 0xA4FF],
			["瓦伊文", 0xA500, 0xA63F],
			["西里尔扩展 B", 0xA640, 0xA69F],
			["巴姆穆文", 0xA6A0, 0xA6FF],
			["声调修饰", 0xA700, 0xA71F],
			["拉丁扩展 D", 0xA720, 0xA7FF],
			["锡尔赫特文", 0xA800, 0xA82F],
			["印度数字", 0xA830, 0xA83F],
			["八思巴文", 0xA840, 0xA87F],
			["索拉什特拉文", 0xA880, 0xA8DF],
			["天城文扩展", 0xA8E0, 0xA8FF],
			["克耶文", 0xA900, 0xA92F],
			["勒姜文", 0xA930, 0xA95F],
			["谚文字母扩展 A", 0xA960, 0xA97F],
			["爪哇文", 0xA980, 0xA9DF],
			["缅甸扩展 B", 0xA9E0, 0xA9FF],
			["占文", 0xAA00, 0xAA5F],
			["缅甸扩展 A", 0xAA60, 0xAA7F],
			["越南傣文", 0xAA80, 0xAADF],
			["曼尼普尔扩展", 0xAAE0, 0xAAFF],
			["埃塞俄比亚扩展 A", 0xAB00, 0xAB2F],
			["拉丁扩展 E", 0xAB30, 0xAB6F],
			["切罗基补充", 0xAB70, 0xABBF],
			["曼尼普尔文", 0xABC0, 0xABFF],
			["谚文音节", 0xAC00, 0xD7A3],
			["谚文字母扩展 B", 0xD7B0, 0xD7FF],
			["兼容汉字", 0xF900, 0xFAFF],
			["字母表现形式", 0xFB00, 0xFB4F],
			["阿拉伯表现 A", 0xFB50, 0xFDFF],
			["变体选择符", 0xFE00, 0xFE0F],
			["竖排标点", 0xFE10, 0xFE1F],
			["组合半角符", 0xFE20, 0xFE2F],
			["中日韩兼容形式", 0xFE30, 0xFE4F],
			["小写变体", 0xFE50, 0xFE6F],
			["阿拉伯表现 B", 0xFE70, 0xFEFF],
			["半角和全角", 0xFF00, 0xFFEF],
			["特殊", 0xFFF0, 0xFFFF],
			["麻将", 0x1F000, 0x1F02F],
			["扑克", 0x1F0A0, 0x1F0FF],
			["带圈字母补充", 0x1F100, 0x1F1FF],
			["带圈汉字补充", 0x1F200, 0x1F2FF],
			["杂项象形", 0x1F300, 0x1F5FF],
			["表情", 0x1F600, 0x1F64F],
			["装饰符号补充", 0x1F650, 0x1F67F],
			["交通和地图", 0x1F680, 0x1F6FF],
			["炼金符号", 0x1F700, 0x1F77F],
			["几何形状扩展", 0x1F780, 0x1F7FF],
			["箭头补充 C", 0x1F800, 0x1F8FF],
			["补充象形", 0x1F900, 0x1F9FF],
			["象棋符号", 0x1FA00, 0x1FA6F],
			["符号扩展 A", 0x1FA70, 0x1FAFF]
		];
		var out = [];
		var i, a, b, p, n, name;
		for (i = 0; i < src.length; i++) {
			name = src[i][0];
			a = src[i][1];
			b = src[i][2];
			if (b - a < 1024) {
				out.push([name, a, b]);
				continue;
			}
			for (p = a; p <= b; p += 1024) {
				n = p + 1023;
				if (n > b) n = b;
				out.push([name + " " + unihex(p), p, n]);
			}
		}
		uniSlices = out;
		return out;
	}

	SK.unibuild = unibuild;
	SK.uniquery = uniquery;
	SK.unishow = unishow;
	SK.unimark = unimark;
	SK.unipreview = unipreview;
	SK.unicn = unicn;
	SK.unihtml = unihtml;
	SK.uniblock = uniblock;
	SK.uniplane = uniplane;
	SK.unikind = unikind;
	SK.uniutf8 = uniutf8;
	SK.uniutf16 = uniutf16;
	SK.unijs = unijs;
	SK.uniurl = uniurl;
	SK.uniparse = uniparse;
	SK.unifind = unifind;
	SK.unihex = unihex;
	SK.uniglyph = uniglyph;
	SK.unigap = unigap;
	SK.unimiss = unimiss;
	SK.uniink = uniink;
	SK.unicomb = unicomb;
	SK.unislices = unislices;
})();
