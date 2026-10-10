(function(){
	var ALL = "all";
	var KINDS = [
		kind("len", "长度", "m", "ft", [
			["mm", "毫米"], ["cm", "厘米"], ["m", "米"], ["km", "千米"],
			["in", "英寸"], ["ft", "英尺"], ["yd", "码"], ["mi", "英里"]
		], { mm: 1, cm: 10, m: 1000, km: 1000000, "in": 25.4, ft: 304.8, yd: 914.4, mi: 1609344 }),
		kind("area", "面积", "m2", "mu", [
			["mm2", "平方毫米"], ["cm2", "平方厘米"], ["m2", "平方米"], ["ha", "公顷"],
			["mu", "亩"], ["km2", "平方千米"], ["ft2", "平方英尺"]
		], { mm2: 0.000001, cm2: 0.0001, m2: 1, ha: 10000, mu: 2000 / 3, km2: 1000000, ft2: 0.09290304 }),
		kind("vol", "体积", "L", "mL", [
			["mL", "毫升"], ["L", "升"], ["m3", "立方米"], ["gal", "美制加仑"]
		], { mL: 0.001, L: 1, m3: 1000, gal: 3.785411784 }),
		kind("mass", "重量", "kg", "jin", [
			["mg", "毫克"], ["g", "克"], ["kg", "千克"], ["t", "吨"],
			["jin", "市斤"], ["liang", "市两"], ["lb", "磅"], ["oz", "盎司"]
		], { mg: 0.001, g: 1, kg: 1000, t: 1000000, jin: 500, liang: 50, lb: 453.59237, oz: 28.349523125 }),
		kind("temp", "温度", "C", "F", [
			["C", "摄氏度"], ["F", "华氏度"], ["K", "开尔文"]
		], null),
		kind("time", "时间", "h", "min", [
			["ms", "毫秒"], ["s", "秒"], ["min", "分"], ["h", "小时"], ["d", "天"], ["week", "周"]
		], { ms: 0.001, s: 1, min: 60, h: 3600, d: 86400, week: 604800 }),
		kind("byte", "存储", "MB", "GB", [
			["B", "字节"], ["KB", "KB"], ["MB", "MB"], ["GB", "GB"], ["TB", "TB"]
		], { B: 1, KB: 1024, MB: 1048576, GB: 1073741824, TB: 1099511627776 }),
		kind("press", "压力", "kPa", "psi", [
			["Pa", "帕"], ["kPa", "千帕"], ["MPa", "兆帕"], ["bar", "巴"],
			["atm", "标准大气压"], ["psi", "磅力/平方英寸"]
		], { Pa: 1, kPa: 1000, MPa: 1000000, bar: 100000, atm: 101325, psi: 6894.757293168 }),
		kind("power", "功率", "kW", "hp", [
			["W", "瓦"], ["kW", "千瓦"], ["MW", "兆瓦"], ["hp", "公制马力"]
		], { W: 1, kW: 1000, MW: 1000000, hp: 735.49875 }),
		kind("px", "像素", "px", "rem", [
			["px", "px"], ["rem", "rem"]
		], null),
		kind("money", "货币", "CNY", "USD", [
			["CNY", "人民币元"], ["jiao", "角"], ["fen", "分"],
			["USD", "美元"], ["HKD", "港币"], ["MOP", "澳门元"], ["EUR", "欧元"], ["GBP", "英镑"],
			["JPY", "日元"], ["KRW", "韩元"], ["AUD", "澳元"], ["CAD", "加元"],
			["SGD", "新加坡元"], ["CHF", "瑞士法郎"], ["THB", "泰铢"], ["TWD", "新台币"]
		], {
			CNY: 1, jiao: 0.1, fen: 0.01,
			USD: 6.733, HKD: 0.858, MOP: 0.8326, EUR: 7.5323, GBP: 8.8872,
			JPY: 0.042571, KRW: 0.004996, AUD: 4.6746, CAD: 4.7204,
			SGD: 5.246, CHF: 8.0821, THB: 0.1994, TWD: 0.2099
		})
	];
	var ALIAS = {
		len: { mm: ["公厘"], cm: ["公分"], m: ["公尺", "meter", "metre"], km: ["公里"], "in": ["吋"], ft: ["呎"] },
		area: { m2: ["平米", "平方米"], km2: ["平方公里"], mu: ["亩"], ha: ["公顷"], ft2: ["平方呎"] },
		vol: { L: ["公升"], mL: ["cc"], m3: ["方"], gal: ["加仑", "美加仑"] },
		mass: { kg: ["公斤"], jin: ["斤"], liang: ["两"], t: ["公吨"], lb: ["英磅"] },
		temp: { C: ["度", "摄氏", "℃", "°c"], F: ["华氏", "℉", "°f"], K: ["开", "开氏", "绝对温度"] },
		time: { ms: ["毫秒"], s: ["秒钟"], min: ["分钟"], h: ["时", "钟头"], d: ["日", "天"], week: ["星期", "礼拜"] },
		byte: { B: ["b"], KB: ["千字节", "kb"], MB: ["兆", "兆字节", "mb"], GB: ["吉字节", "gb"], TB: ["太字节", "tb"] },
		press: { Pa: ["帕斯卡", "pa"], kPa: ["kpa"], MPa: ["mpa"], bar: ["巴"], atm: ["大气压"], psi: ["磅力每平方英寸"] },
		power: { W: ["瓦特"], kW: ["kw"], MW: ["mw"], hp: ["马力"] },
		px: { px: ["像素"], rem: ["倍"] },
		money: {
			CNY: ["元", "块", "人民币", "rmb", "¥"], jiao: ["毛", "角"], fen: ["分"],
			USD: ["美金", "$", "usd"], HKD: ["港元", "hkd"], MOP: ["葡币"], EUR: ["eur"],
			GBP: ["gbp"], JPY: ["日币", "jpy"], KRW: ["krw"], AUD: ["澳币"],
			CAD: ["加币"], SGD: ["新元"], CHF: ["瑞郎"], TWD: ["台币"]
		}
	};
	SK.regutil("u-unit", {
		title: "单位换算",
		go: "换算",
		body: function(){
			var names = [];
			var i;
			for (i = 0; i < KINDS.length; i++) names.push([KINDS[i].id, KINDS[i].name]);
			return SK.row2(
				SK.lab("类型", '<select id="box-kind">' + SK.opts(names, "len") + "</select>"),
				SK.lab("数值", SK.textin("box-in", "1米", "例如 1米、3斤、5kg")))
				+ SK.row2(
					SK.lab("原单位", '<select id="box-from"></select>'),
					SK.lab("新单位", '<select id="box-to"></select>'))
				+ '<div class="row" id="box-root-row" hidden>' + SK.lab("根字号", SK.textin("box-root", "16")) + "</div>"
				+ '<p class="hint">数值里可以带单位，例如 1米、3斤、5kg。新单位选「全部」时，一次列出这一类的所有单位。</p>';
		},
		open: function(){
			var ids = ["box-kind", "box-from", "box-to"];
			var i;
			fillunits(false);
			for (i = 0; i < ids.length; i++) SK.$(ids[i]).onchange = onpick;
			SK.$("box-in").oninput = live;
			SK.$("box-root").oninput = live;
			live();
		},
		run: function(){
			return convert();
		}
	});

	function kind(id, name, from, to, units, scale){
		return { id: id, name: name, from: from, to: to, units: units, scale: scale };
	}

	function findkind(id){
		var i;
		for (i = 0; i < KINDS.length; i++) if (KINDS[i].id === id) return KINDS[i];
		return null;
	}

	function onpick(){
		if (this.id === "box-kind") {
			fillunits(false);
			striptext();
		}
		else if (this.id === "box-from") striptext();
		live();
	}

	function striptext(){
		var picked = detect(SK.val("box-in"));
		if (picked) SK.$("box-in").value = SK.trimnum(picked.num);
	}

	function live(){
		SK.msg("box-msg", "");
		try {
			SK.$("box-out").value = convert();
			SK.msg("box-msg", "完成");
		}
		catch (ex) {
			SK.$("box-out").value = "";
			SK.msg("box-msg", ex && ex.message ? ex.message : "失败", true);
		}
	}

	function allunits(units){
		return [[ALL, "全部"]].concat(units);
	}

	function fillunits(keep){
		var spec = findkind(SK.val("box-kind")) || KINDS[0];
		var from = keep ? SK.val("box-from") : spec.from;
		var to = keep ? SK.val("box-to") : ALL;
		SK.$("box-from").innerHTML = SK.opts(spec.units, from);
		SK.$("box-to").innerHTML = SK.opts(allunits(spec.units), to);
		SK.$("box-root-row").hidden = spec.id !== "px";
	}

	function convert(){
		var raw = String(SK.val("box-in")).trim().replace(/,/g, "");
		var picked = detect(raw);
		var spec;
		var from;
		var n;
		if (picked) {
			spec = findkind(picked.kind);
			if (SK.val("box-kind") !== spec.id) {
				SK.$("box-kind").value = spec.id;
				fillunits(false);
			}
			SK.$("box-from").value = picked.unit;
			from = picked.unit;
			n = picked.num;
		}
		else {
			spec = findkind(SK.val("box-kind"));
			if (!spec) throw new Error("请选择类型");
			n = Number(raw);
			if (raw === "" || !isFinite(n)) throw new Error("请输入数字");
			from = SK.val("box-from");
		}
		return render(spec, n, from, SK.val("box-to"));
	}

	function render(spec, n, from, to){
		var note;
		if (to === ALL) {
			var lines = [line(spec, n, from, from)];
			var i;
			for (i = 0; i < spec.units.length; i++) {
				if (spec.units[i][0] === from) continue;
				lines.push(line(spec, n, from, spec.units[i][0]));
			}
			note = noteof(spec, from, ALL);
			return note ? lines.join("\n") + "\n" + note : lines.join("\n");
		}
		note = noteof(spec, from, to);
		var text = line(spec, n, from, from) + "\n" + line(spec, n, from, to);
		return note ? text + "\n" + note : text;
	}

	function line(spec, n, from, to){
		return SK.trimnum(one(spec, n, from, to)) + unitname(spec.units, to);
	}

	function one(spec, n, from, to){
		if (spec.id === "temp") return tempto(n, from, to);
		if (spec.id === "px") return pxto(n, from, to, rootsize());
		return scaleto(n, from, to, spec.scale);
	}

	function rootsize(){
		var root = Number(String(SK.val("box-root")).trim());
		if (!(root > 0)) throw new Error("根字号要大于 0");
		return root;
	}

	function noteof(spec, from, to){
		if (spec.id === "px" && (from === "rem" || to === ALL || to === "rem"))
			return "按根字号 " + SK.trimnum(rootsize()) + "。";
		if (spec.id === "money" && !cashonly(from, to))
			return "外币按中国银行 2026-10-10 中间价，不是实时牌价。";
		return "";
	}

	function scaleto(n, from, to, scale){
		var a = scale[from];
		var b = scale[to];
		if (!a || !b) throw new Error("请选择单位");
		return n * a / b;
	}

	function tempto(n, from, to){
		var c;
		if (from === "C") c = n;
		else if (from === "F") c = (n - 32) * 5 / 9;
		else if (from === "K") c = n - 273.15;
		else throw new Error("请选择单位");
		if (c < -273.15 - 1e-9) throw new Error("低于绝对零度");
		if (to === "C") return c;
		if (to === "F") return c * 9 / 5 + 32;
		if (to === "K") return c + 273.15;
		throw new Error("请选择单位");
	}

	function pxto(n, from, to, root){
		var px;
		if (from === "px") px = n;
		else if (from === "rem") px = n * root;
		else throw new Error("请选择单位");
		if (to === "px") return px;
		if (to === "rem") return px / root;
		throw new Error("请选择单位");
	}

	function cashonly(from, to){
		return iscash(from) && iscash(to);
	}

	function iscash(id){
		return id === "CNY" || id === "jiao" || id === "fen";
	}

	/** 数值里带了单位（如 1米、3斤）时，认出类型、单位和数值。 */
	function detect(text){
		var s = String(text == null ? "" : text).replace(/[\s,]/g, "");
		var m = /^([+-]?(?:\d+\.?\d*|\.\d+)(?:e[+-]?\d+)?)(.*)$/i.exec(s);
		if (!m) return null;
		var n = Number(m[1]);
		if (!isFinite(n)) return null;
		var tail = m[2].toLowerCase();
		if (!tail) return null;
		var hit = matchunit(tail);
		if (!hit) return null;
		return { kind: hit.kind, unit: hit.unit, num: n };
	}

	/** 先按当前类型找，再按声明顺序找。名称不分大小写、不吃空格。 */
	function matchunit(tail){
		var cur = SK.val("box-kind");
		var order = [];
		var i;
		if (cur) order.push(cur);
		for (i = 0; i < KINDS.length; i++) if (KINDS[i].id !== cur) order.push(KINDS[i].id);
		for (i = 0; i < order.length; i++) {
			var spec = findkind(order[i]);
			if (!spec) continue;
			var j;
			for (j = 0; j < spec.units.length; j++)
				if (namematch(spec, spec.units[j][0], tail)) return { kind: spec.id, unit: spec.units[j][0] };
		}
		return null;
	}

	function namematch(spec, uid, tail){
		var cands = [uid, unitname(spec.units, uid)];
		var extra = (ALIAS[spec.id] || {})[uid];
		if (extra) cands = cands.concat(extra);
		var i;
		for (i = 0; i < cands.length; i++)
			if (String(cands[i]).toLowerCase().replace(/\s+/g, "") === tail) return true;
		return false;
	}

	function unitname(units, id){
		var i;
		for (i = 0; i < units.length; i++) if (units[i][0] === id) return units[i][1];
		return id;
	}
})();
