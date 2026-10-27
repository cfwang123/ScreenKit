(function(){
	// 日本姓名读音查询。数据来自 JMnedict（姓氏与名字的汉字、假名对应）和
	// siikamiika/japanese-family-names 的前 5000 姓人数排名，源文件是 web/jname-s.txt、web/jname-g.txt，由 tmp/jname_gen.py 生成。
	var clip = null;
	var KINDS = [
		{ id: "all", name: "全部" },
		{ id: "n", name: "姓名" },
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
	var ROWSPERCOL = 30;
	var sur = null, giv = null, surix = null, givix = null, loading = {};
	var hits = [], page = 0, blank = "";
	var kind = "all", row = "", sep = " | ";
	var markn = 1;

	SK.regutil("u-jname", {
		title: "日本姓名查询",
		go: "查询",
		body: body,
		open: open,
		run: function(){
			throw new Error("没有这个工具");
		},
		hideGo: true,
		hideOut: true,
		live: false,
		dispose: dropspeak
	});

	function body(){
		var i;
		var html = '<div class="uquery"><input id="jn-q" type="text" placeholder="输入汉字、假名或姓名，如 佐藤 / 佐藤太郎">'
			+ '<span id="jn-msg" class="msg"></span></div>'
			+ '<div class="jnseg" id="jn-seg">';
		for (i = 0; i < KINDS.length; i++)
			html += '<button type="button" data-kind="' + KINDS[i].id + '"'
				+ (KINDS[i].id === kind ? ' class="on"' : "") + ">" + KINDS[i].name + "</button>";
		html += '</div><div class="jnidx" id="jn-idx"><button type="button" data-row="" class="on">全部</button>';
		for (i = 0; i < ROWS.length; i++)
			html += '<button type="button" data-row="' + ROWS[i].id + '">' + ROWS[i].name + "</button>";
		html += '</div><div class="jnlist" id="jn-list"></div>'
			+ '<div class="jnpager" id="jn-pager"><button type="button" id="jn-prev">‹</button>'
			+ '<input id="jn-page" class="jnpi" type="text" inputmode="numeric" maxlength="7" title="输入页码后回车跳转">'
			+ '<span id="jn-pages" class="msg"></span>'
			+ '<button type="button" id="jn-next">›</button></div>'
			+ '<p class="hint">姓氏 9.7 万个、名字 12 万个，取自 JMnedict。首次打开要下载约 1MB 数据，之后直接用本机文件。'
			+ '输入汉字查读法，输入假名反查汉字。输入完整姓名（如 佐藤太郎）会拆成姓和名再配读音；文字可以直接选中复制，喇叭听这一条的读音。</p>';
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
			place();
			load();
		};
		idx.onclick = function(ev){
			var b = ev.target;
			if (!b || !b.getAttribute || F.attr(b, "data-row") === null) return;
			row = F.attr(b, "data-row") || "";
			mark(idx, row || "");
			paint();
		};

		var prev = F.id("jn-prev");
		var next = F.id("jn-next");
		var pin = F.id("jn-page");
		if (pin) {
			pin.onkeydown = function(ev){
				if (ev.key === "Enter") {
					ev.preventDefault();
					jumpto();
				}
			};
			pin.onblur = jumpto;
		}
		if (prev) prev.onclick = function(){ goto(page - 1); };
		if (next) next.onclick = function(){ goto(page + 1); };
		list.onclick = onlist;
		window.addEventListener("resize", function(){ render(); });
		place();
		load();
	}

	function place(){
		var q = F.id("jn-q");
		if (!q) return;
		q.placeholder = kind === "n"
			? "输入姓名，如 佐藤太郎 / さとうたろう"
			: "输入汉字、假名或姓名，如 佐藤 / 佐藤太郎";
	}

	function mark(box, id){
		var i;
		var bs = box.getElementsByTagName("button");
		for (i = 0; i < bs.length; i++)
			bs[i].className = (F.attr(bs[i], "data-kind") || F.attr(bs[i], "data-row") || "") === id ? "on" : "";
	}

	function load(){
		var need = [];
		if (kind === "n" || kind !== "g") need.push("s");
		if (kind === "n" || kind !== "s") need.push("g");
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
		SK.msg("jn-msg", "读取" + (k === "s" ? "姓氏" : "名字") + "…首次可能要下载");
		fetch("/sk/jname-" + k + ".txt", { cache: "force-cache" })
			.then(function(r){
				if (!r.ok) throw new Error(String(r.status));
				return r.text();
			})
			.then(function(text){
				if (k === "s") { sur = parsesur(text); surix = null; }
				else { giv = parsegiv(text); givix = null; }
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
		var raw = SK.val("jn-q");
		var q = norm(raw);
		blank = "";
		if (kind === "n") {
			hits = sur && giv ? pairsof(raw) : [];
			if (!String(raw || "").trim()) blank = "输入完整姓名，按姓和名拆开匹配";
			page = 0;
			render();
			return;
		}
		if (kind === "all" && sur && giv && splitraw(raw)) {
			if (!surix) surix = ix(sur);
			if (!givix) givix = ix(giv);
			if (!exactwhole(q)) {
				var found = pairsof(raw);
				if (found.length) {
					hits = found;
					page = 0;
					render();
					return;
				}
			}
		}
		hits = [];
		if (kind !== "g" && sur) collect(sur, true, q, hits);
		if (kind !== "s" && giv) collect(giv, false, q, hits);
		// 完全一致的排前面，其次按读法开头、人数
		if (q) hits.sort(function(a, b){
			if (b.sc !== a.sc) return b.sc - a.sc;
			return b.r.p - a.r.p;
		});
		page = 0;
		render();
	}

	// 按可用宽度算能放几栏，一页 = 栏数 × 每栏行数
	function render(){
		var box = F.id("jn-list");
		if (!box) return;
		var pair = hits.length && hits[0].pair;
		box.className = pair ? "jnlist jnpair" : "jnlist";
		var cols = Math.max(1, Math.min(8, Math.floor(box.clientWidth / (pair ? 440 : 210)) || 1));
		var per = cols * ROWSPERCOL;
		var pages = Math.max(1, Math.ceil(hits.length / per));
		if (page < 0) page = 0;
		if (page > pages - 1) page = pages - 1;
		var from = page * per;
		var slice = hits.slice(from, from + per);
		var html = [];
		for (var i = 0; i < slice.length; i++) html.push(rowhtml(slice[i]));
		box.innerHTML = html.length ? html.join("") : '<p class="jnnone">' + (blank || "没有匹配的姓名") + "</p>";
		SK.msg("jn-msg", hits.length ? "共 " + hits.length + " 条" : "");
		var pin = F.id("jn-page");
		if (pin && document.activeElement !== pin) pin.value = hits.length ? String(page + 1) : "";
		SK.msg("jn-pages", hits.length ? "/ " + pages + " 页" : "");
		var prev = F.id("jn-prev");
		var next = F.id("jn-next");
		if (prev) prev.disabled = page <= 0;
		if (next) next.disabled = page >= pages - 1;
	}

	// 输入页码跳转：留空、不是数字或超出范围都落回最近的页，输入框里换成实际页码
	function jumpto(){
		var pin = F.id("jn-page");
		if (!pin) return;
		var n = parseInt(String(pin.value).replace(/[^0-9]/g, ""), 10);
		if (n > 0) goto(n - 1);
		else render();
		pin.value = hits.length ? String(page + 1) : "";
	}

	function goto(n){
		page = n;
		render();
		var box = F.id("jn-list");
		if (box && box.scrollIntoView) box.scrollIntoView({ block: "nearest" });
	}

	function onlist(ev){
		var btn = ev.target;
		var list = F.id("jn-list");
		while (btn && btn !== list && btn.tagName !== "BUTTON") btn = btn.parentNode;
		if (!btn || btn === list) return;
		var text = F.attr(btn, "data-v") || "";
		if (text) speak(text);
	}

	function dropspeak(){
		if (SK.stopwav) SK.stopwav();
		if (!clip) return;
		try { clip.pause(); } catch (e) {}
		clip = null;
	}

	function speak(text){
		if (SK.stopwav) SK.stopwav();
		if (clip) {
			try { clip.pause(); } catch (e) {}
			clip = null;
		}
		var audio = new Audio("/api/dict/speak?text=" + encodeURIComponent(text) + "&lang=ja");
		clip = audio;
		audio.onerror = function(){
			if (clip === audio) SK.msg("jn-msg", tx("web.jverb.nospeak", "无法发音"), true);
		};
		var played = audio.play();
		if (played && played.then) played.then(function(){}, function(){
			if (clip === audio) SK.msg("jn-msg", tx("web.jverb.nospeak", "无法发音"), true);
		});
	}

	function tx(key, fb){
		var s = SK.t ? SK.t(key) : "";
		return s || fb;
	}

	// 完整姓名：每个切分点上，左边是姓、右边是名，才算一条。空格或间隔号只切一刀。
	function splitraw(raw){
		var s = String(raw || "").trim();
		if (!s) return null;
		var bits = s.split(/[\s\u3000・·.．]+/);
		var parts = [], i, a, b, q, cuts;
		for (i = 0; i < bits.length; i++) if (bits[i]) parts.push(bits[i]);
		if (parts.length === 2) {
			a = norm(parts[0]);
			b = norm(parts[1]);
			if (!a || !b) return null;
			return [[a, b]];
		}
		if (parts.length !== 1) return null;
		q = norm(parts[0]);
		if (q.length < 2) return null;
		cuts = [];
		for (i = 1; i < q.length; i++) cuts.push([q.slice(0, i), q.slice(i)]);
		return cuts;
	}

	function ix(list){
		var byk = {}, byy = {}, i, j, r, y, b;
		for (i = 0; i < list.length; i++) {
			r = list[i];
			b = byk[r.k];
			if (b) b.push(r);
			else byk[r.k] = [r];
			for (j = 0; j < r.y.length; j++) {
				y = r.y[j];
				if (!y) continue;
				b = byy[y];
				if (b) b.push(r);
				else byy[y] = [r];
			}
		}
		return { byk: byk, byy: byy };
	}

	function finds(idx, part){
		var out = [], bags, bi, bag, i, r;
		if (!idx || !part) return out;
		bags = [idx.byk[part], idx.byy[part]];
		markn++;
		for (bi = 0; bi < bags.length; bi++) {
			bag = bags[bi];
			if (!bag) continue;
			for (i = 0; i < bag.length; i++) {
				r = bag[i];
				if (r.mark === markn) continue;
				r.mark = markn;
				out.push(r);
			}
		}
		return out;
	}

	function reading(r, part){
		var i;
		for (i = 0; i < r.y.length; i++) if (r.y[i] === part) return part;
		return r.y.length ? r.y[0] : "";
	}

	function exactwhole(q){
		if (!q || !surix || !givix) return false;
		return !!(surix.byk[q] || surix.byy[q] || givix.byk[q] || givix.byy[q]);
	}

	function pairsof(raw){
		var cuts, out, seen, ci, ss, gs, i, j, sy, gy, key, added;
		if (!sur || !giv) return [];
		if (!surix) surix = ix(sur);
		if (!givix) givix = ix(giv);
		cuts = splitraw(raw);
		if (!cuts) return [];
		out = [];
		seen = {};
		for (ci = 0; ci < cuts.length; ci++) {
			ss = finds(surix, cuts[ci][0]);
			gs = finds(givix, cuts[ci][1]);
			if (!ss.length || !gs.length) continue;
			if (ss.length > 1) ss = ss.slice().sort(function(a, b){ return b.p - a.p; });
			if (ss.length > 40) ss = ss.slice(0, 40);
			if (gs.length > 80) gs = gs.slice(0, 80);
			added = 0;
			for (i = 0; i < ss.length; i++) {
				if (!inrow(ss[i], row)) continue;
				sy = reading(ss[i], cuts[ci][0]);
				for (j = 0; j < gs.length; j++) {
					gy = reading(gs[j], cuts[ci][1]);
					key = ss[i].k + "\t" + gs[j].k + "\t" + sy + "\t" + gy;
					if (seen[key]) continue;
					seen[key] = 1;
					out.push({ pair: true, s: ss[i], g: gs[j], sy: sy, gy: gy });
					if (++added >= 80) break;
				}
				if (added >= 80) break;
			}
		}
		out = sortpairs(out);
		if (out.length > 300) out = out.slice(0, 300);
		return out;
	}

	function sortpairs(out){
		out.sort(function(a, b){
			if (b.s.p !== a.s.p) return b.s.p - a.s.p;
			return b.s.k.length - a.s.k.length;
		});
		return out;
	}

	// 一行放表记、主读法和发音。其余读法在悬停提示里。不显示姓的人数。
	function rowhtml(hit){
		if (hit.pair) return pairhtml(hit);
		var r = hit.r;
		var y = r.y.length ? r.y[0] : "";
		var tip = r.k + " " + r.y.join("、");
		var spk = y
			? '<button type="button" class="jn-spk" data-v="' + esc(y) + '" title="' + esc(tx("dict.speak", "发音")) + '"><i class="fa-solid fa-volume-high"></i></button>'
			: "";
		return '<div class="jnrow" title="' + esc(tip) + '">'
			+ '<span class="jnk">' + esc(r.k) + "</span>"
			+ '<span class="jny">' + esc(y) + "</span>"
			+ spk
			+ (kind === "all" ? '<span class="jnt">' + (hit.s ? "姓" : "名") + "</span>" : "")
			+ "</div>";
	}

	function pairhtml(hit){
		var say = (hit.sy || "") + (hit.gy || "");
		var tip = hit.s.k + " " + hit.s.y.join("、") + " / " + hit.g.k + " " + hit.g.y.join("、");
		var spk = say
			? '<button type="button" class="jn-spk" data-v="' + esc(say) + '" title="' + esc(tx("dict.speak", "发音")) + '"><i class="fa-solid fa-volume-high"></i></button>'
			: "";
		return '<div class="jnrow" title="' + esc(tip) + '">'
			+ '<span class="jnk">' + esc(hit.s.k) + "</span>"
			+ '<span class="jny">' + esc(hit.sy) + "</span>"
			+ '<span class="jntag">姓</span>'
			+ '<span class="jnk">' + esc(hit.g.k) + "</span>"
			+ '<span class="jny">' + esc(hit.gy) + "</span>"
			+ '<span class="jntag">名</span>'
			+ spk + "</div>";
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

	// 按首读法（第一行读音）分行，和列表的排序一致；不然「和架 あいか／わか」会出现在「わ」行里。
	function inrow(r, id){
		if (!id) return true;
		var i, ch;
		if (!r.y.length) return false;
		ch = r.y[0].charAt(0);
		for (i = 0; i < ROWS.length; i++) {
			if (ROWS[i].id !== id) continue;
			return ROWS[i].chars.indexOf(ch) >= 0;
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
