(function(){
	SK.regutil("u-pw", {
		title: "随机密码",
		go: "生成",
		body: function(){
			return SK.row2(SK.lab("长度", SK.numin("box-len", "16")), SK.lab("条数", SK.numin("box-n", "5")))
				+ SK.check("box-low", "小写", true) + SK.check("box-up", "大写", true)
				+ SK.check("box-dig", "数字", true) + SK.check("box-sym", "符号", true)
				+ SK.check("box-amb", "去掉易混字符（0、O、o、1、l、I）", false)
				+ SK.check("box-each", "每类至少一个", true)
				+ SK.check("box-set", "指定字符集", false)
				+ SK.lab("字符集", SK.textin("box-set-chars", "", "手动输入，重复的只算一次"));
		},
		run: function(){
			return pwtext();
		},
		open: function(){
			var pwids = ["box-low", "box-up", "box-dig", "box-sym", "box-amb"];
			var pi;
			for (pi = 0; pi < pwids.length; pi++) SK.$(pwids[pi]).onchange = fillpwset;
			SK.$("box-set").onchange = pwsetmode;
			pwsetmode();
		}
	});
	function pwsetmode(){
		var on = SK.onbox("box-set");
		var ids = ["box-low", "box-up", "box-dig", "box-sym", "box-amb", "box-each"];
		var i;
		for (i = 0; i < ids.length; i++) {
			var el = SK.$(ids[i]);
			if (el) el.disabled = on;
		}
		var box = SK.$("box-set-chars");
		if (box) box.disabled = !on;
		if (!on) fillpwset();
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
		var box = SK.$("box-set-chars");
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
		if (!isFinite(len)) len = 16;
		if (len < 4) len = 4;
		if (len > 128) len = 128;
		if (!isFinite(count)) count = 5;
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
