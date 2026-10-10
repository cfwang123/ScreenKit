(function(){
	var KINDS = [
		["name", "中文姓名"],
		["cnnick", "中文网名"],
		["ennick", "英文网名"],
		["mail", "邮箱"],
		["phone", "手机"],
		["addr", "地址"],
		["corp", "公司"],
		["user", "用户名"],
		["id", "身份证"],
		["mac", "MAC 地址"],
		["uuid", "UUID"]
	];
	var SUR = "赵钱孙李周吴郑王冯陈褚卫蒋沈韩杨朱秦尤许何吕施张";
	var GIV = "伟芳娜敏静磊洋艳勇杰涛明超秀英丽强军平刚桂英";
	SK.regutil("u-fake", {
		title: "生成测试数据",
		go: "生成",
		body: function(){
			var kind = SK.fakepick || "name";
			SK.fakepick = "";
			return SK.row2(
				SK.lab("类型", '<select id="box-kind">' + SK.opts(KINDS, kind) + "</select>"),
				SK.lab("行数", SK.numin("box-n", "5")));
		},
		open: function(){
			if (!SK.fakerun) return;
			SK.fakerun = 0;
			SK.utilrun();
		},
		run: function(){
			return fakedata(SK.val("box-n"), SK.val("box-kind"));
		}
	});
	function fakedata(count, kind){
		var n = parseInt(count, 10);
		if (!(n >= 1 && n <= 50)) throw new Error("行数要在 1 到 50");
		var lines = [];
		var i;
		for (i = 0; i < n; i++) lines.push(one(kind));
		return lines.join("\n");
	}

	function one(kind){
		if (kind === "mail") return mailbox();
		if (kind === "phone") return phone();
		if (kind === "addr") return address();
		if (kind === "corp") return company();
		if (kind === "user") return username();
		if (kind === "id") return idno();
		if (kind === "cnnick") return cnnick();
		if (kind === "ennick") return ennick();
		if (kind === "mac") return mac();
		if (kind === "uuid") return uuid();
		if (kind === "name") return cname();
		throw new Error("请选择类型");
	}

	function mac(){
		if (!window.crypto || typeof crypto.getRandomValues !== "function") throw new Error("浏览器不能生成随机数");
		var b = new Uint8Array(6);
		crypto.getRandomValues(b);
		b[0] = (b[0] & 252) | 2;
		var parts = [];
		var j;
		for (j = 0; j < 6; j++) parts.push(SK.hex2(b[j]).toUpperCase());
		return parts.join(":");
	}

	function uuid(){
		if (!window.crypto || typeof crypto.getRandomValues !== "function") throw new Error("浏览器不能生成随机数");
		var b = new Uint8Array(16);
		crypto.getRandomValues(b);
		b[6] = (b[6] & 15) | 64;
		b[8] = (b[8] & 63) | 128;
		var h = SK.hexbytes(b);
		return h.slice(0, 8) + "-" + h.slice(8, 12) + "-" + h.slice(12, 16) + "-" + h.slice(16, 20) + "-" + h.slice(20);
	}

	function cnnick(){
		var a = ["清风", "晚风", "星河", "月光", "南风", "北岛", "青柠", "半夏", "初雪", "听雨", "浅夏", "暖阳", "远山", "白露", "微凉", "山月"];
		var b = ["不语", "如歌", "未央", "少年", "旅客", "行者", "小筑", "笔记", "无声", "向晚"];
		var s = a[SK.randint(a.length)];
		if (SK.randint(3) === 0) s += b[SK.randint(b.length)];
		return s;
	}

	function ennick(){
		var a = ["silver", "night", "blue", "quiet", "wild", "lucky", "misty", "golden", "little", "swift"];
		var b = ["fox", "river", "moon", "owl", "pine", "cloud", "star", "wolf", "breeze", "maple"];
		var s = cap(a[SK.randint(a.length)]) + cap(b[SK.randint(b.length)]);
		if (SK.randint(2) === 0) s = s.toLowerCase();
		if (SK.randint(3) === 0) s += SK.randint(100);
		return s;
	}

	function cap(s){
		return s.charAt(0).toUpperCase() + s.substring(1);
	}

	function cname(){
		var name = ch(SUR) + ch(GIV);
		if (SK.randint(2) === 1) name += ch(GIV);
		return name;
	}

	function mailbox(){
		var alpha = "abcdefghijklmnopqrstuvwxyz";
		var s = "";
		var n = 5 + SK.randint(4);
		var i;
		for (i = 0; i < n; i++) s += alpha.charAt(SK.randint(alpha.length));
		return s + SK.randint(1000) + "@example.com";
	}

	function phone(){
		var s = "1" + String(3 + SK.randint(7));
		var i;
		for (i = 0; i < 9; i++) s += String(SK.randint(10));
		return s;
	}

	function address(){
		var city = ["北京", "上海", "广州", "深圳", "杭州", "成都", "武汉", "南京"];
		var road = ["中山路", "人民路", "建设路", "解放路", "和平路", "文一路"];
		return city[SK.randint(city.length)] + road[SK.randint(road.length)] + (1 + SK.randint(300)) + "号";
	}

	function company(){
		var brand = ["星海", "青云", "明远", "宏达", "新联", "博文"];
		var tail = ["科技", "网络", "贸易", "信息", "电子"];
		return brand[SK.randint(brand.length)] + tail[SK.randint(tail.length)] + "有限公司";
	}

	function username(){
		var alpha = "abcdefghijklmnopqrstuvwxyz";
		var s = "";
		var n = 4 + SK.randint(5);
		var i;
		for (i = 0; i < n; i++) s += alpha.charAt(SK.randint(26));
		return s + SK.randint(100);
	}

	function idno(){
		var year = 1975 + SK.randint(30);
		var month = 1 + SK.randint(12);
		var day = 1 + SK.randint(28);
		var body = "110101" + year + twodigit(month) + twodigit(day) + threedigit(SK.randint(999));
		var w = [7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2];
		var map = "10X98765432";
		var sum = 0;
		var i;
		for (i = 0; i < 17; i++) sum += (body.charCodeAt(i) - 48) * w[i];
		return body + map.charAt(sum % 11);
	}

	function ch(s){
		return s.charAt(SK.randint(s.length));
	}

	function twodigit(n){
		return (n < 10 ? "0" : "") + n;
	}

	function threedigit(n){
		return (n < 10 ? "00" : n < 100 ? "0" : "") + n;
	}

	SK.fakedata = fakedata;
})();
