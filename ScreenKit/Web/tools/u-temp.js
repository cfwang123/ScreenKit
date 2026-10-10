(function(){
	var TEMPU = [["C", "摄氏度"], ["F", "华氏度"], ["K", "开尔文"]];
	SK.regutil("u-temp", {
		title: "温度换算",
		go: "换算",
		body: function(){
			return SK.row2(SK.lab("数值", SK.textin("box-in", "0")), SK.lab("单位", '<select id="box-unit">' + SK.opts(TEMPU, "C") + "</select>"));
		},
		run: function(){
			return tempconv(SK.val("box-in"), SK.val("box-unit"));
		}
	});
	function tempconv(text, unit){
		var raw = String(text).trim();
		var n = Number(raw);
		if (raw === "" || !isFinite(n)) throw new Error("请输入数字");
		var c;
		if (unit === "C") c = n;
		else if (unit === "F") c = (n - 32) * 5 / 9;
		else if (unit === "K") c = n - 273.15;
		else throw new Error("请选择单位");
		if (c < -273.15 - 1e-9) throw new Error("低于绝对零度");
		return "摄氏度  " + SK.trimnum(c) + "\n华氏度  " + SK.trimnum(c * 9 / 5 + 32) + "\n开尔文  " + SK.trimnum(c + 273.15);
	}

	SK.tempconv = tempconv;
})();
