(function(){
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
	SK.regutil("u-unit", {
		title: "单位换算",
		go: "换算",
		body: function(){
			var names = [];
			var i;
			for (i = 0; i < KINDS.length; i++) names.push([KINDS[i].id, KINDS[i].name]);
			return SK.row2(
				SK.lab("类型", '<select id="box-kind">' + SK.opts(names, "len") + "</select>"),
				SK.lab("数值", SK.textin("box-in", "1")))
				+ SK.row2(
					SK.lab("原单位", '<select id="box-from"></select>'),
					SK.lab("新单位", '<select id="box-to"></select>'))
				+ '<div class="row" id="box-root-row" hidden>' + SK.lab("根字号", SK.textin("box-root", "16")) + "</div>";
		},
		open: function(){
			fillunits();
			SK.$("box-kind").onchange = fillunits;
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

	function fillunits(){
		var spec = findkind(SK.val("box-kind")) || KINDS[0];
		SK.$("box-from").innerHTML = SK.opts(spec.units, spec.from);
		SK.$("box-to").innerHTML = SK.opts(spec.units, spec.to);
		SK.$("box-root-row").hidden = spec.id !== "px";
	}

	function convert(){
		var spec = findkind(SK.val("box-kind"));
		if (!spec) throw new Error("请选择类型");
		var raw = String(SK.val("box-in")).trim();
		var n = Number(raw);
		if (raw === "" || !isFinite(n)) throw new Error("请输入数字");
		var from = SK.val("box-from");
		var to = SK.val("box-to");
		var out;
		var note = "";
		if (spec.id === "temp") out = tempto(n, from, to);
		else if (spec.id === "px") {
			var root = Number(String(SK.val("box-root")).trim());
			if (!(root > 0)) throw new Error("根字号要大于 0");
			out = pxto(n, from, to, root);
			if (from === "rem" || to === "rem") note = "按根字号 " + SK.trimnum(root) + "。";
		}
		else out = scaleto(n, from, to, spec.scale);
		if (spec.id === "money" && !cashonly(from, to)) note = "外币按中国银行 2026-10-10 中间价，不是实时牌价。";
		var line = SK.trimnum(out) + " " + unitname(spec.units, to);
		return note ? line + "\n" + note : line;
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

	function unitname(units, id){
		var i;
		for (i = 0; i < units.length; i++) if (units[i][0] === id) return units[i][1];
		return id;
	}
})();
