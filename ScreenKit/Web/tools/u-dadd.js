(function(){
	SK.regutil("u-dadd", {
		title: "日期加减",
		go: "计算",
		body: function(){
			return SK.row2(SK.lab("日期", '<input id="box-a" type="date">'), SK.lab("加减天数", SK.textin("box-n", "7")));
		},
		run: function(){
			return dateadd(SK.val("box-a"), SK.val("box-n"));
		},
		open: function(){
			SK.$("box-a").value = SK.ymd(new Date());
		}
	});
	function dateadd(date, days){
		if (!date) throw new Error("请选择日期");
		var n = parseInt(days, 10);
		if (!isFinite(n) || Math.abs(n) > 36600) throw new Error("天数超出范围");
		var d = new Date(date + "T00:00:00");
		if (isNaN(d.getTime())) throw new Error("日期无效");
		d.setDate(d.getDate() + n);
		return SK.ymd(d) + "  周" + "日一二三四五六".charAt(d.getDay());
	}

	SK.dateadd = dateadd;
})();
