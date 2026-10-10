(function(){
	SK.regutil("u-madd", {
		title: "月份加减",
		go: "计算",
		body: function(){
			return SK.row2(SK.lab("日期", '<input id="box-a" type="date">'), SK.lab("加减月数", SK.textin("box-n", "1")));
		},
		run: function(){
			return monthadd(SK.val("box-a"), SK.val("box-n"));
		},
		open: function(){
			SK.$("box-a").value = SK.ymd(new Date());
		}
	});
	function monthadd(date, months){
		if (!date) throw new Error("请选择日期");
		var n = parseInt(months, 10);
		if (!isFinite(n) || Math.abs(n) > 1200) throw new Error("月数超出范围");
		var d = new Date(date + "T00:00:00");
		if (isNaN(d.getTime())) throw new Error("日期无效");
		var day = d.getDate();
		d.setDate(1);
		d.setMonth(d.getMonth() + n);
		var last = new Date(d.getFullYear(), d.getMonth() + 1, 0).getDate();
		d.setDate(day < last ? day : last);
		return SK.ymd(d) + "  周" + "日一二三四五六".charAt(d.getDay());
	}

	SK.monthadd = monthadd;
})();
