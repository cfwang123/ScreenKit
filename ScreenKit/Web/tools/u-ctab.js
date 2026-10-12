(function(){
	// 每条：[中文名, 十六进制, web 英文名]。英文名用 CSS 命名色，中文专名用通行 web 叫法。
	var GROUPS = [
		group("red", "红", [
			["红", "#FF0000", "Red"], ["深红", "#8B0000", "DarkRed"], ["绯红", "#DC143C", "Crimson"],
			["砖红", "#B22222", "FireBrick"], ["印度红", "#CD5C5C", "IndianRed"], ["栗色", "#800000", "Maroon"],
			["番茄", "#FF6347", "Tomato"], ["珊瑚", "#FF7F50", "Coral"], ["鲑鱼", "#FA8072", "Salmon"],
			["中国红", "#DE2910", "China Red"]
		]),
		group("orange", "橙", [
			["橙", "#FFA500", "Orange"], ["深橙", "#FF8C00", "DarkOrange"], ["暗橙", "#FF4500", "OrangeRed"],
			["柿", "#FF4D00", "Persimmon"], ["橘", "#F28500", "Tangerine"], ["南瓜", "#FF7518", "Pumpkin"],
			["杏", "#FBCEB1", "Apricot"], ["桃", "#FFCBA4", "Peach"]
		]),
		group("yellow", "黄", [
			["黄", "#FFFF00", "Yellow"], ["金", "#FFD700", "Gold"], ["暗金", "#B8860B", "DarkGoldenrod"],
			["琥珀", "#FFBF00", "Amber"], ["卡其", "#F0E68C", "Khaki"], ["浅黄", "#FFFFE0", "LightYellow"],
			["玉米", "#FFF8DC", "Cornsilk"], ["橄榄", "#808000", "Olive"]
		]),
		group("green", "绿", [
			["绿", "#008000", "Green"], ["纯绿", "#00FF00", "Lime"], ["浅绿", "#90EE90", "LightGreen"],
			["春绿", "#00FF7F", "SpringGreen"], ["草绿", "#7CFC00", "LawnGreen"], ["海绿", "#2E8B57", "SeaGreen"],
			["森林", "#228B22", "ForestGreen"], ["橄榄绿", "#6B8E23", "OliveDrab"], ["翠绿", "#00A86B", "Jade"],
			["薄荷", "#98FF98", "Mint"]
		]),
		group("cyan", "青", [
			["青", "#00FFFF", "Cyan"], ["深青", "#008B8B", "DarkCyan"], ["青绿", "#008080", "Teal"],
			["碧", "#00CED1", "DarkTurquoise"], ["浅青", "#E0FFFF", "LightCyan"], ["湖蓝", "#30D5C8", "Turquoise"],
			["孔雀蓝", "#00A4A6", "Peacock Blue"]
		]),
		group("blue", "蓝", [
			["蓝", "#0000FF", "Blue"], ["深蓝", "#00008B", "DarkBlue"], ["海军", "#000080", "Navy"],
			["午夜", "#191970", "MidnightBlue"], ["皇家蓝", "#4169E1", "RoyalBlue"], ["道奇蓝", "#1E90FF", "DodgerBlue"],
			["钢蓝", "#4682B4", "SteelBlue"], ["天蓝", "#87CEEB", "SkyBlue"], ["矢车菊", "#6495ED", "CornflowerBlue"],
			["普鲁士蓝", "#003153", "Prussian Blue"]
		]),
		group("purple", "紫", [
			["紫", "#800080", "Purple"], ["深紫", "#4B0082", "Indigo"], ["蓝紫", "#8A2BE2", "BlueViolet"],
			["中紫", "#9370DB", "MediumPurple"], ["兰花", "#DA70D6", "Orchid"], ["梅", "#DDA0DD", "Plum"],
			["薰衣草", "#E6E6FA", "Lavender"], ["深紫红", "#C71585", "MediumVioletRed"]
		]),
		group("pink", "粉", [
			["粉", "#FFC0CB", "Pink"], ["浅粉", "#FFB6C1", "LightPink"], ["热粉", "#FF69B4", "HotPink"],
			["深粉", "#FF1493", "DeepPink"], ["玫瑰", "#FF007F", "Rose"], ["浅玫瑰", "#FFE4E1", "MistyRose"]
		]),
		group("brown", "棕", [
			["棕", "#A52A2A", "Brown"], ["巧克力", "#D2691E", "Chocolate"], ["咖啡", "#6F4E37", "Coffee"],
			["赭", "#CC7722", "Ochre"], ["沙", "#F4A460", "SandyBrown"], ["棕褐", "#D2B48C", "Tan"],
			["小麦", "#F5DEB3", "Wheat"], ["米色", "#F5F5DC", "Beige"]
		]),
		group("gray", "灰", [
			["白", "#FFFFFF", "White"], ["雪白", "#FFFAFA", "Snow"], ["象牙", "#FFFFF0", "Ivory"],
			["浅灰", "#D3D3D3", "LightGray"], ["银", "#C0C0C0", "Silver"], ["灰", "#808080", "Gray"],
			["深灰", "#404040", "Charcoal"], ["黑", "#000000", "Black"]
		])
	];
	var cur = "all";

	SK.regutil("u-ctab", {
		title: "常用颜色表",
		hideGo: true,
		hideOut: true,
		hideCopy: true,
		body: function(){
			return '<div class="ctab">'
				+ '<div class="uquery"><input id="c-q" type="text" placeholder="名称（中/英）或十六进制"><span id="box-msg" class="msg"></span></div>'
				+ '<div class="cgroups" id="c-groups"></div>'
				+ '<div class="cgrid" id="c-grid"></div>'
				+ "</div>";
		},
		open: function(){
			cur = "all";
			paintgroups();
			paint();
			SK.$("c-q").oninput = paint;
			SK.$("c-groups").onclick = ongroup;
			SK.$("c-grid").onclick = onswatch;
		}
	});

	function group(id, name, colors){
		return { id: id, name: name, colors: colors };
	}

	function ongroup(ev){
		var el = ev.target;
		while (el && el !== this) {
			if (el.getAttribute && el.getAttribute("data-g")) {
				cur = el.getAttribute("data-g");
				paintgroups();
				paint();
				return;
			}
			el = el.parentNode;
		}
	}

	function onswatch(ev){
		var el = ev.target;
		while (el && el !== this) {
			if (el.getAttribute && el.getAttribute("data-hex")) {
				copyhex(el.getAttribute("data-hex"), el.getAttribute("data-name"), el.getAttribute("data-en"));
				return;
			}
			el = el.parentNode;
		}
	}

	function copyhex(hex, name, en){
		var what = en ? hex + " " + name + " " + en : hex + " " + name;
		if (navigator.clipboard && navigator.clipboard.writeText) {
			navigator.clipboard.writeText(hex).then(function(){
				SK.msg("box-msg", "已复制 " + what);
			}, function(){
				SK.fallback(hex, "box-msg");
			});
			return;
		}
		SK.fallback(hex, "box-msg");
	}

	function query(){
		return String(SK.val("c-q") || "").trim().toLowerCase();
	}

	function hit(color, q){
		if (!q) return true;
		if (color[0].toLowerCase().indexOf(q) >= 0) return true;
		if (color[1].toLowerCase().indexOf(q) >= 0) return true;
		var en = color[2] ? color[2].toLowerCase() : "";
		if (en.indexOf(q) >= 0) return true;
		// 也允许连写查询，例如 peacockblue
		if (q.indexOf(" ") < 0 && en.replace(/\s+/g, "").indexOf(q) >= 0) return true;
		return false;
	}

	function listed(){
		var q = query();
		var out = [];
		var i;
		var j;
		for (i = 0; i < GROUPS.length; i++) {
			if (cur !== "all" && GROUPS[i].id !== cur) continue;
			for (j = 0; j < GROUPS[i].colors.length; j++) {
				if (hit(GROUPS[i].colors[j], q)) out.push(GROUPS[i].colors[j]);
			}
		}
		return out;
	}

	function paintgroups(){
		var html = btn("all", "全部", count("all"));
		var i;
		for (i = 0; i < GROUPS.length; i++)
			html += btn(GROUPS[i].id, GROUPS[i].name, GROUPS[i].colors.length);
		SK.$("c-groups").innerHTML = html;
	}

	function count(id){
		if (id !== "all") return 0;
		var n = 0;
		var i;
		for (i = 0; i < GROUPS.length; i++) n += GROUPS[i].colors.length;
		return n;
	}

	function btn(id, name, n){
		return '<button type="button" data-g="' + id + '"' + (id === cur ? ' class="on"' : "") + ">"
			+ "<b>" + esc(name) + "</b><small>" + n + "</small></button>";
	}

	function paint(){
		var rows = listed();
		var html = "";
		var i;
		var en;
		for (i = 0; i < rows.length; i++) {
			en = rows[i][2] || "";
			html += '<button type="button" data-hex="' + rows[i][1] + '" data-name="' + esc(rows[i][0]) + '"'
				+ ' data-en="' + esc(en) + '" title="' + esc(rows[i][0] + " " + en + "  " + rows[i][1]) + '">'
				+ '<i style="background:' + rows[i][1] + '"></i>'
				+ "<b>" + esc(rows[i][0]) + (en ? '<span class="en">' + esc(en) + "</span>" : "") + "</b>"
				+ "<small>" + rows[i][1] + "</small></button>";
		}
		if (!html) html = '<p class="cnone">没有这种颜色</p>';
		SK.$("c-grid").innerHTML = html;
	}

	function esc(s){
		return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/"/g, "&quot;");
	}

	SK.ctabgroups = GROUPS;
})();
