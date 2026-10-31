(function(){
	var page = null;
	var STORE = "sk-pw-opts";
	var SAVEDELAY = 2000;
	var savetimer = 0;
	SK.regutil("u-pw", {
		title: "随机密码",
		go: "生成",
		body: function(t){
			page = t;
			return [
				SK.row2(SK.lab("长度", SK.numin("box-len", "6")), SK.lab("条数", SK.numin("box-n", "10"))),
				SK.check("box-low", "小写", true), SK.check("box-up", "大写", false),
				SK.check("box-dig", "数字", true), SK.check("box-sym", "符号", false),
				SK.check("box-amb", "去掉易混字符（0、O、o、1、l、I）", false),
				SK.check("box-each", "每类至少一个", false),
				SK.check("box-set", "指定字符集", false),
				SK.lab("字符集", SK.textin("box-set-chars", "", "手动输入，重复的只算一次"))
			];
		},
		run: function(){
			return pwtext();
		},
		open: function(t){
			flushopts();
			page = t;
			restoreopts();
			var pwids = ["box-low", "box-up", "box-dig", "box-sym", "box-amb"];
			var pi;
			for (pi = 0; pi < pwids.length; pi++) page.doms[SK.domkey(pwids[pi])].onchange = fillpwset;
			page.doms.boxSet.onchange = pwsetmode;
			pwsetmode();
			watchopts();
			if (SK.utilrun) SK.utilrun();
		}
	});
	if (window.addEventListener) window.addEventListener("pagehide", flushopts);
	function pwsetmode(){
		var on = SK.onbox("box-set");
		var ids = ["box-low", "box-up", "box-dig", "box-sym", "box-amb", "box-each"];
		var i;
		for (i = 0; i < ids.length; i++) {
			var el = page.doms[SK.domkey(ids[i])];
			if (el) el.disabled = on;
		}
		var box = page.doms.boxSetChars;
		if (box) box.disabled = !on;
		if (!on) fillpwset();
	}

	// ---- 记住上次的选项 ----
	// 改动后等两秒再写 localStorage，打开工具时读回来。
	function watchopts(){
		var ids = ["box-len", "box-n", "box-low", "box-up", "box-dig", "box-sym",
			"box-amb", "box-each", "box-set", "box-set-chars"];
		var i;
		var el;
		for (i = 0; i < ids.length; i++) {
			el = page.doms[SK.domkey(ids[i])];
			if (!el || !el.addEventListener) continue;
			el.addEventListener("input", scheduleopts);
			el.addEventListener("change", scheduleopts);
		}
	}

	function scheduleopts(){
		if (savetimer) clearTimeout(savetimer);
		savetimer = setTimeout(saveopts, SAVEDELAY);
	}

	function flushopts(){
		if (!savetimer) return;
		clearTimeout(savetimer);
		savetimer = 0;
		saveopts();
	}

	function saveopts(){
		savetimer = 0;
		if (!page || !page.doms) return;
		var d = page.doms;
		var s = {
			len: d.boxLen ? d.boxLen.value : "",
			n: d.boxN ? d.boxN.value : "",
			low: !!(d.boxLow && d.boxLow.checked),
			up: !!(d.boxUp && d.boxUp.checked),
			dig: !!(d.boxDig && d.boxDig.checked),
			sym: !!(d.boxSym && d.boxSym.checked),
			amb: !!(d.boxAmb && d.boxAmb.checked),
			each: !!(d.boxEach && d.boxEach.checked),
			set: !!(d.boxSet && d.boxSet.checked),
			chars: d.boxSetChars ? d.boxSetChars.value : ""
		};
		try { F.lset(STORE, F.json(s)); } catch (e) {}
	}

	function restoreopts(){
		var raw = "";
		var s;
		try { raw = F.lget(STORE) || ""; } catch (e) { raw = ""; }
		if (!raw) return;
		try { s = F.parse(raw); } catch (e) { return; }
		if (!s || typeof s !== "object") return;
		var d = page.doms;
		if (d.boxLen && isdigits(s.len)) d.boxLen.value = s.len;
		if (d.boxN && isdigits(s.n)) d.boxN.value = s.n;
		setchecked(d.boxLow, s.low);
		setchecked(d.boxUp, s.up);
		setchecked(d.boxDig, s.dig);
		setchecked(d.boxSym, s.sym);
		setchecked(d.boxAmb, s.amb);
		setchecked(d.boxEach, s.each);
		setchecked(d.boxSet, s.set);
		if (d.boxSetChars && typeof s.chars === "string") d.boxSetChars.value = s.chars;
	}

	function isdigits(v){
		return typeof v === "string" && /^[0-9]+$/.test(v);
	}

	function setchecked(el, v){
		if (el && typeof v === "boolean") el.checked = v;
	}

	function pwgroups(){
		var drop = SK.onbox("box-amb");
		function clean(s){
			return drop ? s.replace(/[0Oo1lI]/g, "") : s;
		}
		var groups = [];
		if (SK.onbox("box-low")) groups.push(clean("abcdefghijklmnopqrstuvwxyz"));
		if (SK.onbox("box-up")) groups.push(clean("ABCDEFGHIJKLMNOPQRSTUVWXYZ"));
		if (SK.onbox("box-dig")) groups.push(clean("0123456789"));
		if (SK.onbox("box-sym")) groups.push("!@#$%^&*-_=+?~");
		return groups;
	}

	function fillpwset(){
		if (SK.onbox("box-set")) return;
		var box = page.doms.boxSetChars;
		if (!box) return;
		var groups = pwgroups();
		var s = "";
		var i;
		for (i = 0; i < groups.length; i++) s += groups[i];
		box.value = s;
	}

	function pwcharset(s){
		var seen = {};
		var out = "";
		var i;
		s = String(s || "").replace(/[\r\n]/g, "");
		for (i = 0; i < s.length && out.length < 256; i++) {
			var ch = s.charAt(i);
			if (ch.charCodeAt(0) < 32) continue;
			if (seen[ch]) continue;
			seen[ch] = 1;
			out += ch;
		}
		return out;
	}

	function pwtext(){
		var len = parseInt(SK.val("box-len"), 10);
		var count = parseInt(SK.val("box-n"), 10);
		if (!isFinite(len)) len = 6;
		if (len < 4) len = 4;
		if (len > 128) len = 128;
		if (!isFinite(count)) count = 10;
		if (count < 1) count = 1;
		if (count > 50) count = 50;
		if (SK.onbox("box-set")) {
			var set = pwcharset(SK.val("box-set-chars"));
			if (!set) throw new Error("请填写字符集");
			var made = [];
			var k;
			for (k = 0; k < count; k++) made.push(pwone(len, [set], set, false));
			return made.join("\n");
		}
		var each = SK.onbox("box-each");
		var groups = pwgroups();
		var kept = [];
		var pool = "";
		var g;
		for (g = 0; g < groups.length; g++) {
			if (!groups[g]) throw new Error("去掉易混字符后某一类是空的");
			kept.push(groups[g]);
			pool += groups[g];
		}
		if (!pool) throw new Error("请至少选一类字符");
		if (each && len < kept.length) len = kept.length;
		var lines = [];
		var n;
		for (n = 0; n < count; n++) lines.push(pwone(len, kept, pool, each));
		return lines.join("\n");
	}

	function pwone(len, groups, pool, each){
		var chars = [];
		var g;
		if (each) {
			for (g = 0; g < groups.length; g++)
				chars.push(groups[g].charAt(SK.randint(groups[g].length)));
		}
		while (chars.length < len) chars.push(pool.charAt(SK.randint(pool.length)));
		for (var i = chars.length - 1; i > 0; i--) {
			var j = SK.randint(i + 1);
			var tmp = chars[i];
			chars[i] = chars[j];
			chars[j] = tmp;
		}
		return chars.join("");
	}

	SK.pwsetmode = pwsetmode;
	SK.pwgroups = pwgroups;
	SK.fillpwset = fillpwset;
	SK.pwcharset = pwcharset;
	SK.pwtext = pwtext;
	SK.pwone = pwone;
})();

