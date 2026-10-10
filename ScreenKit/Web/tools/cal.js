(function(){
	function cal(){
		SK.post("/api/calendar", {
			date: SK.$("cal-date").value,
			cal: SK.$("cal-id").value,
		}, function(data){
			var rows = [
				["公历", data.gregorian],
				["干支", data.ganzhi],
				["纪年", data.era],
				["年", data.year],
				["月", data.month],
				["日", data.day],
				["星期", data.week],
				["闰月", data.leap ? "是" : "否"],
			];
			var html = "";
			var i;
			for (i = 0; i < rows.length; i++) {
				if (!rows[i][1] && rows[i][0] !== "闰月") continue;
				html += "<dt>" + rows[i][0] + "</dt><dd>" + SK.esc(rows[i][1]) + "</dd>";
			}
			SK.$("cal-out").innerHTML = html;
		}, "cal-msg");
	}

	SK.cal = cal;
})();
