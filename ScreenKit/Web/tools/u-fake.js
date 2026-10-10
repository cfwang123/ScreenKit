(function(){
	SK.regutil("u-fake", {
		title: "测试数据",
		go: "生成",
		body: function(){
			return SK.lab("行数", SK.numin("box-n", "5"));
		},
		run: function(){
			return fakedata(SK.val("box-n"));
		}
	});
	function fakedata(count){
		var n = parseInt(count, 10);
		if (!(n >= 1 && n <= 50)) throw new Error("行数要在 1 到 50");
		var sur = ["赵", "钱", "孙", "李", "周", "吴", "郑", "王"];
		var giv = ["伟", "芳", "娜", "敏", "静", "磊", "洋", "艳"];
		var lines = ["姓名,邮箱,手机"];
		var i;
		for (i = 0; i < n; i++) {
			var name = sur[SK.randint(sur.length)] + giv[SK.randint(giv.length)];
			var phone = "1" + String(3 + SK.randint(6));
			var k;
			for (k = 0; k < 9; k++) phone += String(SK.randint(10));
			lines.push(name + ",user" + (i + 1) + "@example.com," + phone);
		}
		return lines.join("\n");
	}

	SK.fakedata = fakedata;
})();
