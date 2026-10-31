(function(){
	var page = null;
	// 单值表里各格式的顺序和显示名。文本、位串和 hex 不是定宽数值，单独处理。
	var SINGLE = ["i8", "u8", "i16", "u16", "i32", "u32", "i64", "u64",
		"fp16", "bf16", "fp32", "fp64", "ascii", "utf8", "latin1", "base64", "bin", "hex"];
	var CONT = ["u8", "i8", "u16", "i16", "u32", "i32", "u64", "i64",
		"fp16", "bf16", "fp32", "fp64"];
	var WIDTH = { i8: 1, u8: 1, i16: 2, u16: 2, i32: 4, u32: 4, i64: 8, u64: 8,
		fp16: 2, bf16: 2, fp32: 4, fp64: 8 };
	var MAXBYTES = 65536;
	var MAXROWS = 400;

	SK.regutil("u-bytes", {
		title: "字节解读",
		body: function(t){
			page = t;
			var types = [];
			var i;
			for (i = 0; i < CONT.length; i++) types.push({ x: "option", value: CONT[i], c: CONT[i] });
			return [
				F.CreateDOM(t, { x: "p.hint", "data-k": "web.bytes.hint", c: "输入或粘贴十六进制字节，空格、逗号、0x 都行；下面按整数、浮点和文本实时解读，可直接选中文字。" }),
				F.CreateDOM(t, { x: "label", c: [
					{ x: "span", "data-k": "web.bytes.hex", c: "十六进制字节" },
					{ x: "textarea#by-hex.by-hex", name: "byHex", rows: "3", spellcheck: "false", placeholder: "DE AD BE EF" }
				] }),
				F.CreateDOM(t, { x: "div.byopts", c: [
					{ x: "label", c: [
						{ x: "span", "data-k": "web.bytes.endian", c: "端序" },
						{ x: "select#by-endian", name: "byEndian", c: [
							{ x: "option", value: "little", "data-k": "web.bytes.little", c: "小端" },
							{ x: "option", value: "big", "data-k": "web.bytes.big", c: "大端" }
						] }
					] },
					{ x: "label", c: [
						{ x: "span", "data-k": "web.bytes.start", c: "起始偏移" },
						{ x: "input#by-start", name: "byStart", type: "number", min: "0", value: "0" }
					] },
					{ x: "label", c: [
						{ x: "span", "data-k": "web.bytes.type", c: "连续解读" },
						{ x: "select#by-type", name: "byType", c: types }
					] },
					{ x: "span#by-msg.msg", name: "byMsg" }
				] }),
				F.CreateDOM(t, { x: "h3", "data-k": "web.bytes.single", c: "各种格式" }),
				F.CreateDOM(t, { x: "table.bytab", name: "byTable", c: [
					{ x: "thead", c: { x: "tr", c: [
						{ x: "th", "data-k": "web.bytes.format", c: "格式" },
						{ x: "th", "data-k": "web.bytes.value", c: "值" }
					] } },
					{ x: "tbody", name: "byTableBody" }
				] }),
				F.CreateDOM(t, { x: "h3", "data-k": "web.bytes.group", c: "连续分组" }),
				F.CreateDOM(t, { x: "table.bytab", name: "byList", c: [
					{ x: "thead", c: { x: "tr", c: [
						{ x: "th", "data-k": "web.bytes.offset", c: "偏移" },
						{ x: "th", "data-k": "web.bytes.bytes", c: "字节" },
						{ x: "th", "data-k": "web.bytes.value", c: "值" }
					] } },
					{ x: "tbody", name: "byListBody" }
				] })
			];
		},
		open: function(t){
			page = t;
			page.doms.byHex.oninput = refresh;
			page.doms.byEndian.onchange = refresh;
			page.doms.byStart.oninput = refresh;
			page.doms.byType.onchange = refresh;
			refresh();
		},
		run: function(){
			throw new Error("没有这个工具");
		},
		hideGo: true,
		live: false,
		hideOut: true
	});

	function refresh(){
		if (!page || !page.doms) return;
		var text = page.doms.byHex.value || "";
		var little = page.doms.byEndian.value !== "big";
		var bytes = null;
		var err = "";
		if (!trimtext(text).length) {
			renderempty();
			setmsg(tx("web.bytes.empty", "等待输入"), false);
			return;
		}
		try { bytes = parsehex(text); }
		catch (e) { err = e.message; }
		if (!bytes) {
			renderempty();
			setmsg(err, true);
			return;
		}
		var cut = bytes.length > MAXBYTES;
		if (cut) bytes = bytes.subarray(0, MAXBYTES);
		if (!bytes.length) {
			renderempty();
			setmsg(tx("web.bytes.empty", "等待输入"), false);
			return;
		}
		var start = parseInt(page.doms.byStart.value, 10);
		if (!(start >= 0)) start = 0;
		if (start >= bytes.length) start = 0;
		page.doms.byStart.value = String(start);
		var type = page.doms.byType.value || "u32";
		var html = [];
		var i;
		for (i = 0; i < SINGLE.length; i++) html.push(singlerow(SINGLE[i], bytes, start, little));
		page.doms.byTableBody.innerHTML = html.join("");
		var list = listrows(type, bytes, start, little);
		page.doms.byListBody.innerHTML = list.html;
		var note = tx("web.bytes.ready", "共 {0} 字节").replace("{0}", String(bytes.length));
		if (list.cut) note += " · " + tx("web.bytes.trunc", "只显示前 {0} 项").replace("{0}", String(MAXROWS));
		if (cut) note += " · " + tx("web.bytes.cut", "字节过多，只解读前 {0} 字节").replace("{0}", String(MAXBYTES));
		setmsg(note, false);
	}

	function singlerow(name, bytes, at, little){
		var n = WIDTH[name];
		if (n) {
			var v = valueof(name, bytes, at, little);
			if (v === null) return rowhtml(name, "—");
			var u = readu(bytes, at, n, little);
			var shown;
			if (isfloat(name)) shown = fmtfloat(v) + " " + dim("0x" + hexwidth(u, n * 8)) + expohtml(name, u);
			else shown = String(v) + " " + dim("0x" + hexwidth(u, n * 8));
			return rowhtml(name, shown);
		}
		if (name === "ascii") return rowhtml(name, dimify(asciiof(bytes, at)));
		if (name === "utf8") return rowhtml(name, dimify(utf8of(bytes, at)));
		if (name === "latin1") return rowhtml(name, dimify(latin1of(bytes, at)));
		if (name === "base64") return rowhtml(name, dimify(base64of(bytes, at)));
		if (name === "bin") return rowhtml(name, dimify(binof(bytes, at)));
		return rowhtml(name, dimify(hexof(bytes, at)));
	}

	function listrows(type, bytes, at, little){
		var n = WIDTH[type] || 4;
		var html = [];
		var count = 0;
		var i;
		for (i = at; i + n <= bytes.length; i += n) {
			if (count >= MAXROWS) break;
			var u = readu(bytes, i, n, little);
			var v = valueof(type, bytes, i, little);
			var show;
			if (isfloat(type)) show = fmtfloat(v) + " " + dim("0x" + hexwidth(u, n * 8)) + expohtml(type, u);
			else show = String(v) + " " + dim("0x" + hexwidth(u, n * 8));
			html.push("<tr><td>" + padhex(i, 8)
				+ "</td><td>" + bytehex(bytes, i, n) + "</td><td>" + show + "</td></tr>");
			count++;
		}
		if (!html.length) html.push('<tr><td colspan="3" class="by-none">' + esc(tx("web.bytes.none", "没有可解读的字节"))
			+ "</td></tr>");
		return { html: html.join(""), cut: count >= MAXROWS && at + count * n < bytes.length };
	}

	function rowhtml(name, show){
		return '<tr><td class="by-type">' + esc(name) + "</td><td>" + show + "</td></tr>";
	}

	function renderempty(){
		if (!page || !page.doms) return;
		page.doms.byTableBody.innerHTML = '<tr><td colspan="2" class="by-none">'
			+ esc(tx("web.bytes.empty", "等待输入")) + "</td></tr>";
		page.doms.byListBody.innerHTML = '<tr><td colspan="3" class="by-none">'
			+ esc(tx("web.bytes.empty", "等待输入")) + "</td></tr>";
	}

	function setmsg(text, bad){
		var el = page.doms.byMsg;
		if (!el) return;
		el.textContent = text || "";
		el.className = bad ? "msg bad" : "msg";
	}

	function tx(key, fb){
		return SK.tx ? SK.tx(key, fb) : fb;
	}

	function dim(s){
		return '<span class="by-dim">' + esc(s) + "</span>";
	}

	function dimify(s){
		return '<span class="by-text">' + esc(s) + "</span>";
	}

	function valueof(name, bytes, at, little){
		var n = WIDTH[name];
		if (!n || at < 0 || at + n > bytes.length) return null;
		var u = readu(bytes, at, n, little);
		if (name === "fp16") return halfof(u);
		if (name === "bf16") return bfloatof(u);
		if (name === "fp32" || name === "fp64") return floatof(bytes, at, n, little);
		if (name.charAt(0) === "i") return intof(u, n * 8);
		return u;
	}

	function readu(bytes, at, n, little){
		var i;
		if (n === 8) {
			var big = BigInt(0);
			var step = BigInt(8);
			if (little) {
				for (i = n - 1; i >= 0; i--) big = (big << step) | BigInt(bytes[at + i]);
			}
			else {
				for (i = 0; i < n; i++) big = (big << step) | BigInt(bytes[at + i]);
			}
			return big;
		}
		var v = 0;
		if (little) {
			for (i = n - 1; i >= 0; i--) v = v * 256 + bytes[at + i];
		}
		else {
			for (i = 0; i < n; i++) v = v * 256 + bytes[at + i];
		}
		return v;
	}

	function intof(u, bits){
		if (typeof u === "bigint") {
			var half = BigInt(1) << BigInt(bits - 1);
			return u >= half ? u - (BigInt(1) << BigInt(bits)) : u;
		}
		if (bits === 8) return (u << 24) >> 24;
		if (bits === 16) return (u << 16) >> 16;
		return u | 0;
	}

	function halfof(u){
		var sign = (u & 0x8000) ? -1 : 1;
		var exp = (u >> 10) & 0x1f;
		var frac = u & 0x3ff;
		if (exp === 0) return sign * frac * Math.pow(2, -24);
		if (exp === 31) return frac ? NaN : sign * Infinity;
		return sign * (1 + frac / 1024) * Math.pow(2, exp - 15);
	}

	function bfloatof(u){
		var buf = new ArrayBuffer(4);
		var dv = new DataView(buf);
		dv.setUint32(0, (u & 0xffff) << 16);
		return dv.getFloat32(0);
	}

	function floatof(bytes, at, n, little){
		var dv = new DataView(new ArrayBuffer(n));
		var i;
		for (i = 0; i < n; i++) dv.setUint8(i, bytes[at + i]);
		return n === 4 ? dv.getFloat32(0, little) : dv.getFloat64(0, little);
	}

	function fmtfloat(v){
		if (v !== v) return "NaN";
		if (v === Infinity) return "Infinity";
		if (v === -Infinity) return "-Infinity";
		var s = String(v);
		if (s.indexOf("e") < 0 && s.length > 21) s = v.toPrecision(17);
		return s;
	}

	function isfloat(name){
		return name === "fp16" || name === "bf16" || name === "fp32" || name === "fp64";
	}

	// 从位模式拆出规格化的尾数和指数：值 = m × 2^e，1 ≤ |m| < 2。零、无穷和 NaN 没有这种写法。
	function exponentof(name, u){
		var bits = WIDTH[name] * 8;
		var k = name === "fp16" ? 10 : name === "bf16" ? 7 : name === "fp32" ? 23 : 52;
		var bias = name === "fp16" ? 15 : name === "fp64" ? 1023 : 127;
		var one = BigInt(1);
		var val = BigInt(u);
		var sign = (val >> BigInt(bits - 1)) & one;
		var ebits = bits - 1 - k;
		var field = (val >> BigInt(k)) & ((one << BigInt(ebits)) - one);
		var frac = val & ((one << BigInt(k)) - one);
		var m;
		var e;
		if (field === (one << BigInt(ebits)) - one) return null;
		if (field === BigInt(0)) {
			if (frac === BigInt(0)) return null;
			var lead = 0;
			var rest = frac;
			while (rest > one) {
				rest >>= one;
				lead++;
			}
			m = Number(frac) / Math.pow(2, lead);
			e = 1 - bias - k + lead;
		}
		else {
			m = 1 + Number(frac) / Math.pow(2, k);
			e = Number(field) - bias;
		}
		if (sign) m = -m;
		return { m: m, e: e };
	}

	function fmtexp(name, u){
		var d = exponentof(name, u);
		if (!d) return "";
		var m = d.m;
		// fp32 尾数只有 24 位有效，取 9 位有效数字就够，免得带出没意义的尾数。
		if (name === "fp32") m = parseFloat(m.toPrecision(9));
		return String(m) + " × 2^" + String(d.e);
	}

	function expohtml(name, u){
		var s = fmtexp(name, u);
		return s ? " " + dim("= " + s) : "";
	}

	function hexwidth(v, bits){
		var u;
		if (typeof v === "bigint") u = v < BigInt(0) ? v + (BigInt(1) << BigInt(bits)) : v;
		else {
			u = v < 0 ? v + Math.pow(2, bits) : v;
			u = Math.floor(u);
		}
		var s = u.toString(16);
		while (s.length < bits / 4) s = "0" + s;
		return s.toUpperCase();
	}

	function padhex(v, width){
		var s = v.toString(16).toUpperCase();
		while (s.length < width) s = "0" + s;
		return s;
	}

	function bytehex(bytes, at, n){
		var out = [];
		var i;
		for (i = 0; i < n && at + i < bytes.length; i++) {
			var b = bytes[at + i];
			out.push((b < 16 ? "0" : "") + b.toString(16).toUpperCase());
		}
		return out.join(" ");
	}

	function asciiof(bytes, at){
		var out = [];
		var i;
		for (i = at; i < bytes.length; i++) {
			var b = bytes[i];
			out.push(b >= 32 && b <= 126 ? String.fromCharCode(b) : ".");
		}
		return out.join("");
	}

	function latin1of(bytes, at){
		var out = [];
		var i;
		for (i = at; i < bytes.length; i++) out.push(String.fromCharCode(bytes[i]));
		return out.join("");
	}

	function utf8of(bytes, at){
		var view = bytes.subarray(at);
		if (typeof TextDecoder === "function") {
			try { return new TextDecoder("utf-8", { fatal: false }).decode(view); }
			catch (e) {}
		}
		return latin1of(bytes, at);
	}

	function base64of(bytes, at){
		var parts = [];
		var i;
		for (i = at; i < bytes.length; i += 8192) {
			parts.push(String.fromCharCode.apply(null, bytes.subarray(i, Math.min(i + 8192, bytes.length))));
		}
		try { return btoa(parts.join("")); }
		catch (e) { return ""; }
	}

	function binof(bytes, at){
		var out = [];
		var i;
		var end = Math.min(bytes.length, at + 64);
		for (i = at; i < end; i++) {
			var s = bytes[i].toString(2);
			while (s.length < 8) s = "0" + s;
			out.push(s);
		}
		if (end < bytes.length) out.push("…");
		return out.join(" ");
	}

	function hexof(bytes, at){
		var out = [];
		var i;
		for (i = at; i < bytes.length; i++) {
			var b = bytes[i];
			out.push((b < 16 ? "0" : "") + b.toString(16).toUpperCase());
		}
		return out.join("");
	}

	// 十六进制文本转字节：允许空白、逗号、分号、0x / \x 前缀和换行。
	function parsehex(text){
		var s = String(text == null ? "" : text);
		s = s.replace(/0[xX](?=[0-9a-fA-F])/g, "");
		s = s.replace(/\\x(?=[0-9a-fA-F])/gi, "");
		s = s.replace(/[,\s;:_-]+/g, "");
		if (!s.length) return new Uint8Array(0);
		if (/[^0-9a-fA-F]/.test(s)) {
			var bad = s.match(/[^0-9a-fA-F]/)[0];
			throw new Error(tx("web.bytes.bad", "十六进制里有不支持的字符：{0}").replace("{0}", bad));
		}
		if (s.length % 2) throw new Error(tx("web.bytes.odd", "十六进制位数必须是偶数"));
		var out = new Uint8Array(s.length / 2);
		var i;
		for (i = 0; i < out.length; i++) out[i] = parseInt(s.substring(i * 2, i * 2 + 2), 16);
		return out;
	}

	function esc(s){
		return String(s == null ? "" : s)
			.replace(/&/g, "&amp;")
			.replace(/</g, "&lt;")
			.replace(/>/g, "&gt;")
			.replace(/"/g, "&quot;");
	}

	function trimtext(s){
		return String(s == null ? "" : s).replace(/^\s+|\s+$/g, "");
	}

	// 调试和自检用。
	SK.bytes = { parse: parsehex, value: valueof, half: halfof, bf16: bfloatof, int: intof, exp: exponentof, eform: fmtexp };
})();
