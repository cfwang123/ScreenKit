(function(){
	SK.regutil("u-bmi", {
		title: "BMI",
		go: "计算",
		body: function(){
			return SK.row2(SK.lab("身高（厘米）", SK.textin("box-h", "")), SK.lab("体重（千克）", SK.textin("box-w", "")));
		},
		run: function(){
			return bmicalc(SK.val("box-h"), SK.val("box-w"));
		}
	});
	function bmicalc(cm, kg){
		var h = Number(String(cm).trim());
		var w = Number(String(kg).trim());
		if (!(h > 0) || !(w > 0)) throw new Error("请输入身高和体重");
		if (h > 300 || w > 500) throw new Error("数值超出常见范围");
		var bmi = w / ((h / 100) * (h / 100));
		var band = "肥胖";
		if (bmi < 18.5) band = "偏瘦";
		else if (bmi < 24) band = "正常";
		else if (bmi < 28) band = "超重";
		return "BMI " + SK.trimnum(bmi) + "\n" + band;
	}

	SK.bmicalc = bmicalc;
})();
