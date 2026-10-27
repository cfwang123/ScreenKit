(function(){
	// 日本姓名读音查询。数据来自 JMnedict（姓氏与名字的汉字、假名对应）和
	// siikamiika/japanese-family-names 的前 5000 姓人数排名，源文件是 web/jname-s.txt、web/jname-g.txt，由 tmp/jname_gen.py 生成。
	var KINDS = [
		{ id: "all", name: "全部" },
		{ id: "s", name: "姓" },
		{ id: "g", name: "名" }
	];
	// 五十音行的首字，用来按行浏览
	var ROWS = [
		{ id: "a", name: "あ", chars: "あぁいぃうぅえぇおぉゔ" },
		{ id: "k", name: "か", chars: "かがきぎくぐけげこご" },
		{ id: "s", name: "さ", chars: "さざしじすずせぜそぞ" },
		{ id: "t", name: "た", chars: "ただちぢつづてでとど" },
		{ id: "n", name: "な", chars: "なにぬねの" },
		{ id: "h", name: "は", chars: "はばぱひびぴふぶぷへべぺほぼぽ" },
		{ id: "m", name: "ま", chars: "まみむめも" },
		{ id: "y", name: "や", chars: "やゆよゃゅょ" },
		{ id: "r", name: "ら", chars: "らりるれろ" },
		{ id: "w", name: "わ", chars: "わをんゎ" }
	];
	var SHOW = 200;
	var sur = null, giv = null, loading = {};
	var kind = "all", row = "", sep = " | ";

	SK.regutil("u-jname", {
		title: "日本人名",
		go: "查询",
		body: body,
		open: open,
		run: function(){
			throw new Error("没有这个工具");
		},
		hideGo: true,
		hideOut: true,
		live: false
	});

	function body(){
		var i;
		var html = '<div class="uquery"><input id="jn-q" type="text" placeholder="输入汉字或假名，如 佐藤 / さとう">'
			+ '<span id="jn-msg" class="msg"></span></div>'
			+ '<div class="jnseg" id="jn-seg">';
		for (i = 0; i < KINDS.length; i++)
			html += '<button type="button" data-kind="' + KINDS[i].id + '"'
				+ (KINDS[i].id === kind ? ' class="on"' : "") + ">" + KINDS[i].name + "</button>";
		html += '</div><div class="jnidx" id="jn-idx"><button type="button" data-row="" class="on">全部</button>';
		for (i = 0; i < ROWS.length; i++)
			html += '<button type="button" data-row="' + ROWS[i].id + '">' + ROWS[i].name + "</button>";
		html += '</div><div class="jnlist" id="jn-list"></div>'
			+ '<p class="hint">姓氏 9.7 万个、名字 12 万个，取自 JMnedict；前 5000 姓带人数（来自名字由来排名）。'
			+ '输入汉字查读法，输入假名反查汉字；点一行复制。</p>';
		return html;
	}

	function open(){
		var q = F.id("jn-q");
		var seg = F.id("jn-seg");
		var idx = F.id("jn-idx");
		var list = F.id("jn-list");
		if (!q || !list) return;
		var timer = 0;
		q.oninput = function(){
			clearTimeout(timer);
			timer = setTimeout(paint, 120);
		};
		seg.onclick = function(ev){
			var b = ev.target;
			if (!b || !b.getAttribute || !F.attr(b, "data-kind")) return;
			kind = F.attr(b, "data-kind");
			mark(seg, kind);
			load();
		};
		idx.onclick = function(ev){
			var b = ev.target;
			if (!b || !b.getAttribute || F.attr(b, "data-row") === null) return;
			row = F.attr(b, "data-row") || "";
			mark(idx, row || "");
			paint();
		};
		list.onclick = function(ev){
			var el = ev.target;
			while (el && el !== list) {
				if (el.getAttribute && F.attr(el, "data-k")) {
					SK.copytext(F.attr(el, "data-k"), "jn-msg");
					return;
				}
				el = el.parentNode;
			}
		};
		load();
	}

	function mark(box, id){
		var i;
		var bs = box.getElementsByTagName("button");
		for (i = 0; i < bs.length; i++)
			bs[i].className = (F.attr(bs[i], "data-kind") || F.attr(bs[i], "data-row") || "") === id ? "on" : "";
	}

	function load(){
		var need = [];
		if (kind !== "g") need.push("s");
		if (kind !== "s") need.push("g");
		if (!need.length) return paint();
		for (var i = 0; i < need.length; i++) {
			if (!loading[need[i]]) continue;
			paint();
			return;
		}
		var pending = need.length;
		for (i = 0; i < need.length; i++) fetchkind(need[i], function(){
			if (--pending <= 0) paint();
		});
	}

	function fetchkind(k, done){
		if (loading[k]) return;
		loading[k] = true;
		SK.msg("jn-msg", "读取" + (k === "s" ? "姓氏" : "名字") + "…");
		fetch("/sk/jname-" + k + ".txt", { cache: "force-cache" })
			.then(function(r){
				if (!r.ok) throw new Error(String(r.status));
				return r.text();
			})
			.then(function(text){
				if (k === "s") sur = parsesur(text);
				else giv = parsegiv(text);
				loading[k] = false;
				done();
			})
			.catch(function(){
				loading[k] = false;
				SK.msg("jn-msg", "读取姓名数据失败");
			});
	}

	function parsesur(text){
		var out = [];
		var lines = text.split("\n");
		var i, p;
		for (i = 0; i < lines.length; i++) {
			p = lines[i].split("\t");
			if (p.length < 2 || !p[0]) continue;
			out.push({ k: p[0], y: p[1].split(","), p: Number(p[2] || 0) });
		}
		return out;
	}

	function parsegiv(text){
		var out = [];
		var lines = text.split("\n");
		var i, p;
		for (i = 0; i < lines.length; i++) {
			p = lines[i].split("\t");
			if (p.length < 2 || !p[0]) continue;
			out.push({ k: p[0], y: p[1].split(","), p: 0 });
		}
		return out;
	}

	function paint(){
		var box = F.id("jn-list");
		if (!box) return;
		var q = norm(SK.val("jn-q"));
		var hits = [];
		var total = 0;
		var i, j, list;
		if (kind !== "g" && sur) collect(sur, true, q, hits);
		if (kind !== "s" && giv) collect(giv, false, q, hits);
		total = hits.length;
		// 完全一致的排前面，其次按读法开头、人数
		if (q) hits.sort(function(a, b){
			if (b.sc !== a.sc) return b.sc - a.sc;
			return b.r.p - a.r.p;
		});
		hits = hits.slice(0, SHOW);
		var html = [];
		for (i = 0; i < hits.length; i++) html.push(rowhtml(hits[i]));
		box.innerHTML = html.length ? html.join("") : '<p class="jnnone">没有匹配的姓名</p>';
		SK.msg("jn-msg", total ? (total > hits.length ? "共 " + total + " 条，显示前 " + hits.length + " 条"
			: "共 " + total + " 条") : "");
	}

	function rowhtml(hit){
		var r = hit.r;
		var pop = r.p > 0 ? (r.p >= 10000 ? Math.round(r.p / 10000) + " 万" : r.p + " 人") : "";
		return '<button type="button" class="jnrow" data-k="' + esc(r.k + " " + r.y[0]) + '"'
			+ ' title="点击复制">'
			+ '<span class="jnk">' + esc(r.k) + "</span>"
			+ '<span class="jny">' + esc(r.y.join("、")) + "</span>"
			+ (kind === "all" ? '<span class="jnt">' + (hit.s ? "姓" : "名") + "</span>" : "")
			+ '<span class="jnp">' + esc(pop) + "</span></button>";
	}

	function collect(list, issur, q, hits){
		for (var i = 0; i < list.length; i++) {
			if (!inrow(list[i], row)) continue;
			var sc = score(list[i], q);
			if (sc <= 0) continue;
			hits.push({ r: list[i], s: issur, sc: sc });
		}
	}

	// 3 完全一致，2 开头，1 包含，0 不匹配
	function score(r, q){
		var i;
		if (!q) return 1;
		if (r.k === q) return 3;
		if (r.k.indexOf(q) === 0) return 2;
		for (i = 0; i < r.y.length; i++) {
			if (r.y[i] === q) return 3;
			if (r.y[i].indexOf(q) === 0) return 2;
		}
		return match(r, q) ? 1 : 0;
	}

	function inrow(r, id){
		if (!id) return true;
		var i, j, ch;
		for (i = 0; i < ROWS.length; i++) {
			if (ROWS[i].id !== id) continue;
			for (j = 0; j < r.y.length; j++) {
				ch = r.y[j].charAt(0);
				if (ROWS[i].chars.indexOf(ch) >= 0) return true;
			}
			return false;
		}
		return true;
	}

	function match(r, q){
		if (!q) return true;
		if (r.k.indexOf(q) >= 0) return true;
		for (var i = 0; i < r.y.length; i++)
			if (r.y[i].indexOf(q) >= 0) return true;
		return false;
	}

	// 片假名转平假名，全角空格去掉；汉字保持原样
	function norm(s){
		s = String(s || "").trim().replace(/[\u3000\s]+/g, "");
		var out = "", i, c;
		for (i = 0; i < s.length; i++) {
			c = s.charCodeAt(i);
			out += c >= 0x30A1 && c <= 0x30F6 ? String.fromCharCode(c - 0x60) : s.charAt(i);
		}
		return out;
	}

	function esc(s){
		return String(s).replace(/[&<>"]/g, function(c){
			return { "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;" }[c];
		});
	}
})();
