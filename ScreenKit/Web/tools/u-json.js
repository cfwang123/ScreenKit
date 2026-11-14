(function(){
	var page = null;
	var root = null;
	var timer = 0;
	var cap = 0;
	var q = "";

	SK.regutil("u-json", {
		title: "JSON / XML / YAML 查看器",
		noGo: true,
		hideGo: true,
		hideCopy: true,
		hideOut: true,
		live: false,
		body: body,
		open: open,
		dispose: dispose
	});

	function tr(key, fb){
		var s = SK.t ? SK.t(key) : "";
		return s || fb;
	}

	function body(t){
		page = t;
		function opt(v, key, fb){
			return { x: "option", value: v, "data-k": key, c: tr(key, fb) };
		}
		return F.CreateDOM(t, { x: "div.jv", c: [
			{ x: "p.hint", "data-k": "web.json.hint", c: tr("web.json.hint", "粘贴 JSON、XML 或 YAML，或把文件拖进来。点左边的三角折叠节点，双击某个值可以复制。") },
			{ x: "div.jv-bar", c: [
				{ x: "label.jv-lb", c: [
					{ x: "span", "data-k": "web.json.fmt", c: tr("web.json.fmt", "格式") },
					{ x: "select#jv-fmt", name: "jvFmt", c: [
						opt("auto", "web.json.auto", "自动识别"),
						opt("json", "web.json.json", "JSON"),
						opt("xml", "web.json.xml", "XML"),
						opt("yaml", "web.json.yaml", "YAML")
					] }
				] },
				{ x: "input#jv-find.jv-find", name: "jvFind", type: "text", spellcheck: "false",
					"data-kph": "web.json.findph", placeholder: tr("web.json.findph", "过滤键或值") },
				{ x: "button", name: "jvAll", type: "button", "data-k": "web.json.expand", c: tr("web.json.expand", "展开全部"), onclick: function(){ expandall(); } },
				{ x: "button", name: "jvNone", type: "button", "data-k": "web.json.collapse", c: tr("web.json.collapse", "折叠全部"), onclick: function(){ collapseall(); } },
				{ x: "button", name: "jvPick", type: "button", "data-k": "web.json.pick", c: tr("web.json.pick", "选择文件"), onclick: function(){ pickfile(); } },
				{ x: "button", name: "jvClear", type: "button", "data-k": "web.json.clear", c: tr("web.json.clear", "清空"), onclick: function(){ clearall(); } },
				{ x: "input#jv-file", name: "jvFile", type: "file", hidden: true,
					accept: ".json,.xml,.yaml,.yml,.txt,application/json,text/xml,text/yaml" }
			] },
			{ x: "textarea#jv-in.jv-in", name: "jvIn", rows: "8", spellcheck: "false",
				"data-kph": "web.json.ph", placeholder: tr("web.json.ph", "在这里粘贴内容，或把文件拖到窗口里") },
			{ x: "div#jv-tree.jv-tree", name: "jvTree" },
			{ x: "span.jv-stat", name: "jvStat" }
		] });
	}

	function open(t){
		page = t;
		var d = t.doms;
		d.jvIn.oninput = schedule;
		d.jvFind.oninput = schedule;
		d.jvFmt.onchange = schedule;
		d.jvFile.onchange = function(){ pickread(d.jvFile); };
		var box = SK.doms.boxBody;
		if (box) {
			box.ondragover = function(ev){ F.pd(ev); };
			box.ondrop = function(ev){
				F.pd(ev);
				var dt = ev.dataTransfer;
				if (dt && dt.files && dt.files.length) readfile(dt.files[0]);
			};
		}
		d.jvTree.ondblclick = function(ev){ copyval(ev); };
		paint();
	}

	function dispose(){
		if (timer) clearTimeout(timer);
		timer = 0;
		root = null;
		page = null;
		q = "";
	}

	function pickfile(){
		var f = page && page.doms.jvFile;
		if (f) f.click();
	}

	function pickread(input){
		if (input.files && input.files.length) readfile(input.files[0]);
	}

	function readfile(file){
		if (!file) return;
		var reader = new FileReader();
		reader.onload = function(){
			var text = String(reader.result || "");
			if (!page) return;
			page.doms.jvIn.value = text;
			schedule();
		};
		reader.onerror = function(){ say(tr("web.json.nofile", "读取文件失败"), true); };
		reader.readAsText(file, "utf-8");
	}

	function clearall(){
		if (!page) return;
		page.doms.jvIn.value = "";
		page.doms.jvFind.value = "";
		schedule();
	}

	function schedule(){
		if (timer) clearTimeout(timer);
		timer = setTimeout(run, 220);
	}

	function run(){
		timer = 0;
		if (!page) return;
		var text = page.doms.jvIn.value || "";
		root = null;
		if (!text.trim()) {
			page.doms.jvTree.innerHTML = "";
			say("");
			return;
		}
		try {
			root = parse(text, page.doms.jvFmt.value || "auto");
			if (root) root.total = cap;
			say("");
			paint();
		}
		catch (ex) {
			root = null;
			page.doms.jvTree.innerHTML = "";
			say((ex && ex.message) || tr("web.json.bad", "解析失败"), true);
		}
	}

	function say(text, bad){
		if (!page) return;
		var el = page.doms.jvStat;
		if (!el) return;
		el.textContent = text || "";
		el.className = bad ? "jv-stat jv-bad" : "jv-stat";
	}

	function sniff(text){
		var s = String(text).replace(/^\uFEFF/, "").replace(/^\s+/, "");
		var ch = s.charAt(0);
		if (ch === "{" || ch === "[") return "json";
		if (ch === "<") return "xml";
		return "yaml";
	}

	function parse(text, fmt){
		cap = 0;
		if (!fmt || fmt === "auto") fmt = sniff(text);
		var v;
		if (fmt === "json") {
			try { v = F.parse(text); }
			catch (ex) { throw new Error(tr("web.json.bad", "解析失败") + ": " + ((ex && ex.message) || "")); }
			return fromval(v, null);
		}
		if (fmt === "xml") return fromxml(text);
		try { v = yamlparse(text); }
		catch (ex) { throw new Error(tr("web.json.bad", "解析失败") + ": " + ((ex && ex.message) || "")); }
		return fromval(v, null);
	}

	function fromval(v, k){
		if (++cap > 200000) throw new Error(tr("web.json.toobig", "内容太大"));
		if (v === null || v === undefined) return { k: k, t: "null", v: null };
		var ty = typeof v;
		if (ty === "string") return { k: k, t: "string", v: v };
		if (ty === "number") return { k: k, t: "number", v: v };
		if (ty === "boolean") return { k: k, t: "bool", v: v };
		var ch = [];
		var i;
		if (v instanceof Array) {
			for (i = 0; i < v.length; i++) ch.push(fromval(v[i], i));
			return { k: k, t: "array", ch: ch };
		}
		var keys = Object.keys(v);
		for (i = 0; i < keys.length; i++) ch.push(fromval(v[keys[i]], keys[i]));
		return { k: k, t: "object", ch: ch };
	}

	function fromxml(text){
		if (typeof DOMParser !== "function") throw new Error("不支持 XML");
		var doc = new DOMParser().parseFromString(text, "application/xml");
		var err = doc.getElementsByTagName("parsererror");
		if (err && err.length) throw new Error(tr("web.json.bad", "解析失败") + ": XML");
		var el = doc.documentElement;
		if (!el) throw new Error(tr("web.json.bad", "解析失败") + ": XML");
		return fromel(el);
	}

	function fromel(el){
		if (++cap > 200000) throw new Error(tr("web.json.toobig", "内容太大"));
		var node = { k: el.nodeName, t: "elem", ch: [] };
		var i, a, c;
		for (i = 0; i < el.attributes.length; i++) {
			a = el.attributes[i];
			node.ch.push({ k: "@" + a.name, t: "attr", v: a.value });
		}
		for (i = 0; i < el.childNodes.length; i++) {
			c = el.childNodes[i];
			if (c.nodeType === 1) node.ch.push(fromel(c));
			else if (c.nodeType === 3 || c.nodeType === 4) {
				if (c.nodeValue && c.nodeValue.trim()) node.ch.push({ k: null, t: "text", v: c.nodeValue.trim() });
			}
		}
		return node;
	}

	// ==================== YAML ====================

	function yamlparse(text){
		var rows = String(text).replace(/^\uFEFF/, "").replace(/\r\n?/g, "\n").split("\n");
		var lines = [];
		var i, j, s, one;
		for (i = 0; i < rows.length; i++) {
			one = rows[i];
			if (!one.trim()) { lines.push({ b: 1, ind: 0, txt: "", raw: one }); continue; }
			j = 0;
			while (j < one.length && one.charAt(j) === " ") j++;
			if (j === 0 && (one.trim() === "---" || one.trim() === "...")) { lines.push({ b: 1, ind: 0, txt: "", raw: one }); continue; }
			s = stripcomment(one.substring(j));
			if (!s.trim()) { lines.push({ b: 1, ind: j, txt: "", raw: one }); continue; }
			lines.push({ b: 0, ind: j, txt: s.replace(/\s+$/, ""), raw: one });
		}
		var cur = { p: 0 };
		skipblank(lines, cur);
		if (cur.p >= lines.length) return null;
		return yblock(lines, cur, lines[cur.p].ind);
	}

	function skipblank(lines, cur){
		while (cur.p < lines.length && lines[cur.p].b) cur.p++;
	}

	function yblock(lines, cur, ind){
		skipblank(lines, cur);
		var ln = lines[cur.p];
		if (!ln) return null;
		if (ln.ind !== ind) ind = ln.ind;
		if (isdash(ln.txt)) return yseq(lines, cur, ind);
		if (splitkv(ln.txt)) return ymap(lines, cur, ind);
		var v = yscalar(ln.txt);
		cur.p++;
		return v;
	}

	function yseq(lines, cur, ind){
		var arr = [];
		var ln, rest, off;
		for (;;) {
			skipblank(lines, cur);
			ln = lines[cur.p];
			if (!ln || ln.b || ln.ind !== ind || !isdash(ln.txt)) break;
			rest = ln.txt.substring(1).replace(/^\s+/, "");
			if (!rest) {
				cur.p++;
				skipblank(lines, cur);
				var nx = lines[cur.p];
				arr.push(nx && !nx.b && nx.ind > ind ? yblock(lines, cur, nx.ind) : null);
				continue;
			}
			if (isblock(rest)) { cur.p++; arr.push(yblocktext(lines, cur, ind, rest)); continue; }
			if (isdash(rest) || splitkv(rest)) {
				off = ln.raw.indexOf(rest);
				if (off < 0) off = ind + 2;
				ln.ind = off;
				ln.txt = rest;
				arr.push(isdash(rest) ? yseq(lines, cur, off) : ymap(lines, cur, off));
				continue;
			}
			arr.push(yscalar(rest));
			cur.p++;
		}
		return arr;
	}

	function ymap(lines, cur, ind){
		var obj = {};
		var ln, kv, key, rest, val, nx;
		for (;;) {
			skipblank(lines, cur);
			ln = lines[cur.p];
			if (!ln || ln.b || ln.ind !== ind || isdash(ln.txt)) break;
			kv = splitkv(ln.txt);
			if (!kv) { cur.p++; continue; }
			key = unquote(kv.k.trim());
			rest = kv.v.trim();
			cur.p++;
			if (rest.charAt(0) === "&") {
				var sp = rest.indexOf(" ");
				rest = sp < 0 ? "" : rest.substring(sp + 1).replace(/^\s+/, "");
			}
			if (!rest) {
				skipblank(lines, cur);
				nx = lines[cur.p];
				val = nx && !nx.b && nx.ind > ind ? yblock(lines, cur, nx.ind) : null;
			}
			else if (isblock(rest)) val = yblocktext(lines, cur, ind, rest);
			else if (rest.charAt(0) === "*") val = null;
			else val = yscalar(rest);
			if (key === "<<") {
				if (val && typeof val === "object" && !(val instanceof Array)) {
					var m;
					for (m in val) if (Object.prototype.hasOwnProperty.call(val, m) && !Object.prototype.hasOwnProperty.call(obj, m)) obj[m] = val[m];
				}
			}
			else obj[key] = val;
		}
		return obj;
	}

	function yblocktext(lines, cur, ind, header){
		var keep = header.indexOf("+") >= 0;
		var strip = header.indexOf("-") >= 0;
		var fold = header.charAt(0) === ">";
		var buf = [];
		var base = -1;
		var ln;
		while (cur.p < lines.length) {
			ln = lines[cur.p];
			if (ln.b) {
				buf.push("");
				cur.p++;
				continue;
			}
			if (ln.ind <= ind) break;
			if (base < 0) base = ln.ind;
			buf.push(ln.raw.substring(Math.min(base, ln.ind)));
			cur.p++;
		}
		while (buf.length && buf[buf.length - 1].trim() === "") buf.pop();
		var out;
		if (fold) {
			var parts = [];
			var i;
			for (i = 0; i < buf.length; i++) {
				if (!buf[i].trim()) parts.push("\n");
				else if (parts.length && parts[parts.length - 1] !== "\n") parts[parts.length - 1] += " " + buf[i].trim();
				else parts.push(buf[i].trim());
			}
			out = parts.join("");
		}
		else out = buf.join("\n");
		if (strip) return out;
		if (keep) return out + "\n";
		return out;
	}

	function isdash(txt){
		return txt === "-" || txt.indexOf("- ") === 0;
	}

	function isblock(txt){
		return /^[|>][-+]?[0-9]*$/.test(txt);
	}

	function stripcomment(txt){
		var quote = "";
		var i, ch;
		for (i = 0; i < txt.length; i++) {
			ch = txt.charAt(i);
			if (quote) {
				if (ch === quote) quote = "";
				continue;
			}
			if (ch === '"' || ch === "'") { quote = ch; continue; }
			if (ch === "#" && (i === 0 || txt.charAt(i - 1) === " " || txt.charAt(i - 1) === "\t")) return txt.substring(0, i);
		}
		return txt;
	}

	function splitkv(txt){
		var depth = 0;
		var quote = "";
		var i, ch, next;
		for (i = 0; i < txt.length; i++) {
			ch = txt.charAt(i);
			if (quote) {
				if (ch === quote) quote = "";
				continue;
			}
			if (ch === '"' || ch === "'") { quote = ch; continue; }
			if (ch === "[" || ch === "{") { depth++; continue; }
			if (ch === "]" || ch === "}") { depth--; continue; }
			if (ch !== ":" || depth !== 0) continue;
			next = txt.charAt(i + 1);
			if (next && next !== " " && next !== "\t") continue;
			return { k: txt.substring(0, i), v: txt.substring(i + 1) };
		}
		return null;
	}

	function unquote(s){
		if (!s) return s;
		var a = s.charAt(0);
		if (a === '"' && s.charAt(s.length - 1) === '"') {
			try { return F.parse(s); }
			catch (ex) { return s.substring(1, s.length - 1); }
		}
		if (a === "'" && s.charAt(s.length - 1) === "'") return s.substring(1, s.length - 1).replace(/''/g, "'");
		return s;
	}

	function conv(raw){
		var s = raw.replace(/^\s+|\s+$/g, "");
		if (!s) return null;
		if (/^(null|Null|NULL|~)$/.test(s)) return null;
		if (/^(true|True|TRUE)$/.test(s)) return true;
		if (/^(false|False|FALSE)$/.test(s)) return false;
		if (/^[-+]?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][-+]?[0-9]+)?$/.test(s)) return Number(s);
		if (/^0x[0-9a-fA-F]+$/.test(s)) return parseInt(s.substring(2), 16);
		return s;
	}

	function yscalar(txt){
		var s = txt.replace(/^\s+|\s+$/g, "");
		if (!s) return null;
		var a = s.charAt(0);
		if (a === '"' || a === "'") return unquote(s);
		if (a === "[" || a === "{") return yflow(s);
		return conv(s);
	}

	function yflow(s){
		var i = 0;
		function ws(){ while (i < s.length && /\s/.test(s.charAt(i))) i++; }
		function quoted(){
			var qc = s.charAt(i);
			var start = ++i;
			var out = "";
			while (i < s.length) {
				var ch = s.charAt(i);
				if (qc === '"' && ch === "\\") { out += ch + s.charAt(i + 1); i += 2; continue; }
				if (ch === qc) {
					if (qc === "'" && s.charAt(i + 1) === "'") { out += "'"; i += 2; continue; }
					i++;
					break;
				}
				out += ch;
				i++;
			}
			return qc === '"' ? out.replace(/\\"/g, '"') : out;
		}
		function scalar(iskey){
			ws();
			var ch = s.charAt(i);
			if (ch === '"' || ch === "'") return quoted();
			var start = i;
			while (i < s.length) {
				ch = s.charAt(i);
				if (ch === "," || ch === "]" || ch === "}") break;
				if (iskey && ch === ":" && (s.charAt(i + 1) === " " || s.charAt(i + 1) === "" || s.charAt(i + 1) === "}")) break;
				i++;
			}
			return conv(s.substring(start, i));
		}
		function val(){
			ws();
			var ch = s.charAt(i);
			var arr, obj, k, v;
			if (ch === "[") {
				i++;
				arr = [];
				ws();
				if (s.charAt(i) === "]") { i++; return arr; }
				for (;;) {
					arr.push(val());
					ws();
					ch = s.charAt(i);
					if (ch === ",") { i++; continue; }
					if (ch === "]") { i++; return arr; }
					break;
				}
				return arr;
			}
			if (ch === "{") {
				i++;
				obj = {};
				ws();
				if (s.charAt(i) === "}") { i++; return obj; }
				for (;;) {
					k = scalar(true);
					ws();
					if (s.charAt(i) === ":") i++;
					v = val();
					obj[k] = v;
					ws();
					ch = s.charAt(i);
					if (ch === ",") { i++; continue; }
					if (ch === "}") { i++; return obj; }
					break;
				}
				return obj;
			}
			return scalar(false);
		}
		return val();
	}

	// ==================== 渲染 ====================

	function paint(){
		var tree = page.doms.jvTree;
		tree.innerHTML = "";
		if (!root) return;
		q = (page.doms.jvFind.value || "").replace(/^\s+|\s+$/g, "").toLowerCase();
		if (q) {
			marksub(root);
			paintfilter(tree);
		}
		else {
			mkwrap(root, 0, tree);
			if (root.ch && root.ch.length) converted(root);
		}
		stat();
	}

	function converted(node){
		buildkids(node);
		node.built = true;
		node.kids.classList.remove("hide");
		node.tgl.textContent = "▼";
	}

	function paintfilter(tree){
		function walk(node, depth, host){
			if (!node.show) return;
			mkwrap(node, depth, host);
			if (!node.ch || !node.ch.length) return;
			node.built = true;
			node.kids.classList.remove("hide");
			node.tgl.textContent = "▼";
			var i;
			for (i = 0; i < node.ch.length; i++) walk(node.ch[i], depth + 1, node.kids);
		}
		walk(root, 0, tree);
	}

	function marksub(node){
		var hit = false;
		var k = node.k;
		if (k !== null && k !== undefined && String(k).toLowerCase().indexOf(q) >= 0) hit = true;
		if ("string number bool null attr text".indexOf(node.t) >= 0) {
			if (String(node.v).toLowerCase().indexOf(q) >= 0) hit = true;
		}
		node.hit = hit;
		if (node.ch) {
			var i;
			for (i = 0; i < node.ch.length; i++) if (marksub(node.ch[i])) hit = true;
		}
		node.show = hit;
		return hit;
	}

	function mkwrap(node, depth, host){
		node.d = depth;
		var wrap = F.el("div");
		wrap.className = "jv-node";
		var row = F.el("div");
		row.className = "jv-row";
		row.style.paddingLeft = (depth * 14) + "px";
		var kids = F.el("div");
		kids.className = "jv-kids hide";
		node.wrap = wrap;
		node.row = row;
		node.kids = kids;
		node.built = false;
		var cont = !!(node.ch && node.ch.length);
		var tgl = F.el("span");
		tgl.className = "jv-tgl";
		tgl.textContent = cont ? "▶" : "";
		node.tgl = tgl;
		row.appendChild(tgl);
		if (cont) tgl.onclick = function(){ toggle(node); };
		keypart(node, row);
		valuepart(node, row, cont);
		wrap.appendChild(row);
		wrap.appendChild(kids);
		host.appendChild(wrap);
		return wrap;
	}

	function keypart(node, row){
		var el;
		if (node.t === "elem") {
			el = F.el("span");
			el.className = "jv-tag";
			el.textContent = "<" + node.k + ">";
			row.appendChild(el);
			if (q && String(node.k).toLowerCase().indexOf(q) >= 0) el.classList.add("jv-hit");
			return;
		}
		if (node.k === null || node.k === undefined) return;
		el = F.el("span");
		el.className = (typeof node.k === "number") ? "jv-i" : "jv-k";
		el.textContent = String(node.k);
		if (q && String(node.k).toLowerCase().indexOf(q) >= 0) el.classList.add("jv-hit");
		row.appendChild(el);
		row.appendChild(document.createTextNode(": "));
	}

	function valuepart(node, row, cont){
		var el;
		if (cont) {
			el = F.el("span");
			el.className = "jv-p";
			if (node.t === "array") el.textContent = "[…]";
			else if (node.t === "object") el.textContent = "{…}";
			else el.textContent = "…";
			row.appendChild(el);
			var cnt = F.el("span");
			cnt.className = "jv-cnt";
			cnt.textContent = String(node.ch.length);
			row.appendChild(cnt);
			return;
		}
		var text, cls;
		if (node.t === "string" || node.t === "attr" || node.t === "text") {
			text = JSON.stringify(String(node.v));
			cls = "jv-s";
		}
		else if (node.t === "number") { text = String(node.v); cls = "jv-n"; }
		else if (node.t === "bool") { text = node.v ? "true" : "false"; cls = "jv-l"; }
		else if (node.t === "null") { text = "null"; cls = "jv-z"; }
		else if (node.t === "elem") { text = "</>"; cls = "jv-p"; }
		else { text = String(node.v); cls = "jv-s"; }
		el = F.el("span");
		el.className = cls;
		if (text.length > 300) {
			el.textContent = text.substring(0, 300) + "…";
			el.title = text;
		}
		else el.textContent = text;
		if (q && String(node.v).toLowerCase().indexOf(q) >= 0) el.classList.add("jv-hit");
		row.appendChild(el);
		node.sel = el;
	}

	function toggle(node){
		if (!node.ch || !node.ch.length) return;
		if (!node.built) { buildkids(node); node.built = true; }
		if (node.kids.classList.contains("hide")) {
			node.kids.classList.remove("hide");
			node.tgl.textContent = "▼";
		}
		else {
			node.kids.classList.add("hide");
			node.tgl.textContent = "▶";
		}
	}

	function buildkids(node){
		var i;
		for (i = 0; i < node.ch.length; i++) mkwrap(node.ch[i], node.d + 1, node.kids);
	}

	function expanddeep(node){
		if (!node.ch || !node.ch.length) return;
		if (!node.built) { buildkids(node); node.built = true; }
		node.kids.classList.remove("hide");
		node.tgl.textContent = "▼";
		var i;
		for (i = 0; i < node.ch.length; i++) expanddeep(node.ch[i]);
	}

	function expandall(){
		if (!root || !page) return;
		expanddeep(root);
	}

	function collapseall(){
		if (!root || !page) return;
		var tree = page.doms.jvTree;
		tree.innerHTML = "";
		mkwrap(root, 0, tree);
	}

	function stat(){
		if (!page) return;
		var n = root ? (root.total || 0) : 0;
		say(n ? tr("web.json.nodes", "节点") + " " + n : "");
	}

	function copyval(ev){
		var el = ev.target;
		while (el && el !== document.body) {
			if (el.classList && el.classList.contains("jv-s")) break;
			if (el.classList && el.classList.contains("jv-n")) break;
			if (el.classList && el.classList.contains("jv-l")) break;
			if (el.classList && el.classList.contains("jv-z")) break;
			el = el.parentNode;
		}
		if (!el || el === document.body) return;
		var s = el.title || el.textContent || "";
		if (el.classList.contains("jv-s")) {
			try { s = F.parse(s); }
			catch (ex) {}
		}
		if (navigator.clipboard && navigator.clipboard.writeText) {
			navigator.clipboard.writeText(s).then(function(){
				say(tr("web.json.copied", "已复制"));
			}, function(){
				SK.copytext(s, "box-msg");
			});
			return;
		}
		SK.copytext(s, "box-msg");
	}

	SK.jvparse = function(text, fmt){ return parse(text, fmt || "auto"); };
	SK.jvyml = yamlparse;
})();
