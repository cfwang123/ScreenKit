(function(){
	var GROUPS = [
		group("red", "红", [
			["红", "#FF0000"], ["深红", "#8B0000"], ["绯红", "#DC143C"], ["砖红", "#B22222"],
			["印度红", "#CD5C5C"], ["栗色", "#800000"], ["番茄", "#FF6347"], ["珊瑚", "#FF7F50"],
			["鲑鱼", "#FA8072"], ["中国红", "#DE2910"]
		]),
		group("orange", "橙", [
			["橙", "#FFA500"], ["深橙", "#FF8C00"], ["暗橙", "#FF4500"], ["柿", "#FF4D00"],
			["橘", "#F28500"], ["南瓜", "#FF7518"], ["杏", "#FBCEB1"], ["桃", "#FFCBA4"]
		]),
		group("yellow", "黄", [
			["黄", "#FFFF00"], ["金", "#FFD700"], ["暗金", "#B8860B"], ["琥珀", "#FFBF00"],
			["卡其", "#F0E68C"], ["浅黄", "#FFFFE0"], ["玉米", "#FFF8DC"], ["橄榄", "#808000"]
		]),
		group("green", "绿", [
			["绿", "#008000"], ["纯绿", "#00FF00"], ["浅绿", "#90EE90"], ["春绿", "#00FF7F"],
			["草绿", "#7CFC00"], ["海绿", "#2E8B57"], ["森林", "#228B22"], ["橄榄绿", "#6B8E23"],
			["翠绿", "#00A86B"], ["薄荷", "#98FF98"]
		]),
		group("cyan", "青", [
			["青", "#00FFFF"], ["深青", "#008B8B"], ["青绿", "#008080"], ["碧", "#00CED1"],
			["浅青", "#E0FFFF"], ["湖蓝", "#30D5C8"], ["孔雀蓝", "#00A4A6"]
		]),
		group("blue", "蓝", [
			["蓝", "#0000FF"], ["深蓝", "#00008B"], ["海军", "#000080"], ["午夜", "#191970"],
			["皇家蓝", "#4169E1"], ["道奇蓝", "#1E90FF"], ["钢蓝", "#4682B4"], ["天蓝", "#87CEEB"],
			["矢车菊", "#6495ED"], ["普鲁士蓝", "#003153"]
		]),
		group("purple", "紫", [
			["紫", "#800080"], ["深紫", "#4B0082"], ["蓝紫", "#8A2BE2"], ["中紫", "#9370DB"],
			["兰花", "#DA70D6"], ["梅", "#DDA0DD"], ["薰衣草", "#E6E6FA"], ["深紫红", "#C71585"]
		]),
		group("pink", "粉", [
			["粉", "#FFC0CB"], ["浅粉", "#FFB6C1"], ["热粉", "#FF69B4"], ["深粉", "#FF1493"],
			["玫瑰", "#FF007F"], ["浅玫瑰", "#FFE4E1"]
		]),
		group("brown", "棕", [
			["棕", "#A52A2A"], ["巧克力", "#D2691E"], ["咖啡", "#6F4E37"], ["赭", "#CC7722"],
			["沙", "#F4A460"], ["棕褐", "#D2B48C"], ["小麦", "#F5DEB3"], ["米色", "#F5F5DC"]
		]),
		group("gray", "灰", [
			["白", "#FFFFFF"], ["雪白", "#FFFAFA"], ["象牙", "#FFFFF0"], ["浅灰", "#D3D3D3"],
			["银", "#C0C0C0"], ["灰", "#808080"], ["深灰", "#404040"], ["黑", "#000000"]
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
				+ '<div class="uquery"><input id="c-q" type="text" placeholder="名称或十六进制"><span id="box-msg" class="msg"></span></div>'
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
				copyhex(el.getAttribute("data-hex"), el.getAttribute("data-name"));
				return;
			}
			el = el.parentNode;
		}
	}

	function copyhex(hex, name){
		if (navigator.clipboard && navigator.clipboard.writeText) {
			navigator.clipboard.writeText(hex).then(function(){
				SK.msg("box-msg", "已复制 " + hex + " " + name);
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
		for (i = 0; i < rows.length; i++) {
			html += '<button type="button" data-hex="' + rows[i][1] + '" data-name="' + esc(rows[i][0]) + '">'
				+ '<i style="background:' + rows[i][1] + '"></i>'
				+ "<b>" + esc(rows[i][0]) + "</b><small>" + rows[i][1] + "</small></button>";
		}
		if (!html) html = '<p class="cnone">没有这种颜色</p>';
		SK.$("c-grid").innerHTML = html;
	}

	function esc(s){
		return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/"/g, "&quot;");
	}

	SK.ctabgroups = GROUPS;
})();
