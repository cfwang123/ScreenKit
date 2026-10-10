(function(){
	SK.regutil("u-datediff", {
		title: "日期差",
		go: "计算",
		body: function(){
			return SK.row2(SK.lab("开始", '<input id="box-a" type="date">'), SK.lab("结束", '<input id="box-b" type="date">'));
		},
		run: function(){
			return dateconv(SK.val("box-a"), SK.val("box-b"));
		},
		open: function(){
			var day = new Date();
			SK.$("box-b").value = SK.ymd(day);
			day.setDate(day.getDate() - 7);
			SK.$("box-a").value = SK.ymd(day);
		}
	});
	function dateconv(a, b){
		if (!a || !b) throw new Error("请选择两个日期");
		var da = new Date(a + "T00:00:00");
		var db = new Date(b + "T00:00:00");
		if (isNaN(da.getTime()) || isNaN(db.getTime())) throw new Error("日期无效");
		var days = Math.round((db.getTime() - da.getTime()) / 86400000);
		return "从 " + a + " 到 " + b + " 相差 " + days + " 天";
	}

	SK.dateconv = dateconv;
})();
