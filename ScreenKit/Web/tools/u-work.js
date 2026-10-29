(function(){
	var page = null;
	SK.regutil("u-work", {
		title: "工作日",
		go: "计算",
		noGo: true,
		body: function(t){
			page = t;
			var onin = function(){ SK.utilrun(); };
			return SK.row2(
				SK.lab("开始", F.CreateDOM(t, { x: "input.box-a", name: "boxA", type: "date", oninput: onin, onchange: onin })),
				SK.lab("结束", F.CreateDOM(t, { x: "input.box-b", name: "boxB", type: "date", oninput: onin, onchange: onin }))
			);
		},
		run: function(){
			return workdays(SK.val("box-a"), SK.val("box-b"));
		},
		open: function(t){
			page = t;
			var day = new Date();
			var a = page.doms.boxA;
			var b = page.doms.boxB;
			b.value = SK.ymd(day);
			day.setDate(day.getDate() - 7);
			a.value = SK.ymd(day);
			SK.utilrun();
		}
	});
	function tr(s){ return SK.tr ? SK.tr(s) : s; }
	function workdays(a, b){
		if (!a || !b) throw new Error("请选择两个日期");
		var da = new Date(a + "T00:00:00");
		var db = new Date(b + "T00:00:00");
		if (isNaN(da.getTime()) || isNaN(db.getTime())) throw new Error("日期无效");
		if (db < da) throw new Error("开始不能晚于结束");
		var days = Math.round((db.getTime() - da.getTime()) / 86400000);
		if (days > 3660) throw new Error("最多 10 年");
		var n = 0;
		var d = da;
		while (d <= db) {
			var w = d.getDay();
			if (w !== 0 && w !== 6) n++;
			d.setDate(d.getDate() + 1);
		}
		return tr("工作日") + "  " + n + " " + tr("天") + "\n" + tr("已跳过周六和周日，不含法定假日");
	}

	SK.workdays = workdays;
})();
