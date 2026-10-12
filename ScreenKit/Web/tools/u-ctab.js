(function(){
	// 每条：[中文名, 十六进制, 名字]，第 4 项可选，是备注（如 Transparent 的 A=0）。
	// web 支：CSS 命名色 + 中文专名色，按色相分组。
	var WEB = [
		group("red", "红", [
			["红", "#FF0000", "Red"], ["深红", "#8B0000", "DarkRed"], ["绯红", "#DC143C", "Crimson"],
			["砖红", "#B22222", "FireBrick"], ["印度红", "#CD5C5C", "IndianRed"], ["栗色", "#800000", "Maroon"],
			["番茄", "#FF6347", "Tomato"], ["珊瑚", "#FF7F50", "Coral"], ["鲑鱼", "#FA8072", "Salmon"],
			["浅鲑鱼", "#FFA07A", "LightSalmon"], ["暗鲑鱼", "#E9967A", "DarkSalmon"],
			["浅珊瑚", "#F08080", "LightCoral"], ["中国红", "#DE2910", "China Red"]
		]),
		group("orange", "橙", [
			["橙", "#FFA500", "Orange"], ["深橙", "#FF8C00", "DarkOrange"], ["暗橙", "#FF4500", "OrangeRed"],
			["柿", "#FF4D00", "Persimmon"], ["橘", "#F28500", "Tangerine"], ["南瓜", "#FF7518", "Pumpkin"],
			["杏", "#FBCEB1", "Apricot"], ["桃", "#FFCBA4", "Peach"], ["桃脯", "#FFDAB9", "PeachPuff"],
			["鹿皮色", "#FFE4B5", "Moccasin"], ["纳瓦霍白", "#FFDEAD", "NavajoWhite"], ["浓汤色", "#FFE4C4", "Bisque"]
		]),
		group("yellow", "黄", [
			["黄", "#FFFF00", "Yellow"], ["金", "#FFD700", "Gold"], ["金菊色", "#DAA520", "Goldenrod"],
			["暗金", "#B8860B", "DarkGoldenrod"], ["琥珀", "#FFBF00", "Amber"], ["卡其", "#F0E68C", "Khaki"],
			["暗卡其", "#BDB76B", "DarkKhaki"], ["苍金菊", "#EEE8AA", "PaleGoldenrod"],
			["浅黄", "#FFFFE0", "LightYellow"], ["玉米", "#FFF8DC", "Cornsilk"], ["橄榄", "#808000", "Olive"]
		]),
		group("green", "绿", [
			["绿", "#008000", "Green"], ["暗绿", "#006400", "DarkGreen"], ["纯绿", "#00FF00", "Lime"],
			["酸橙绿", "#32CD32", "LimeGreen"], ["查特绿", "#7FFF00", "Chartreuse"], ["绿黄", "#ADFF2F", "GreenYellow"],
			["黄绿", "#9ACD32", "YellowGreen"], ["浅绿", "#90EE90", "LightGreen"], ["苍绿", "#98FB98", "PaleGreen"],
			["春绿", "#00FF7F", "SpringGreen"], ["中春绿", "#00FA9A", "MediumSpringGreen"],
			["草绿", "#7CFC00", "LawnGreen"], ["海绿", "#2E8B57", "SeaGreen"], ["中海绿", "#3CB371", "MediumSeaGreen"],
			["浅海绿", "#20B2AA", "LightSeaGreen"], ["暗海绿", "#8FBC8F", "DarkSeaGreen"],
			["森林", "#228B22", "ForestGreen"], ["橄榄绿", "#6B8E23", "OliveDrab"],
			["暗橄榄绿", "#556B2F", "DarkOliveGreen"], ["翠绿", "#00A86B", "Jade"], ["薄荷", "#98FF98", "Mint"]
		]),
		group("cyan", "青", [
			["青", "#00FFFF", "Cyan / Aqua"], ["深青", "#008B8B", "DarkCyan"], ["青绿", "#008080", "Teal"],
			["碧", "#00CED1", "DarkTurquoise"], ["浅青", "#E0FFFF", "LightCyan"], ["绿松石", "#40E0D0", "Turquoise"],
			["中绿松石", "#48D1CC", "MediumTurquoise"], ["苍绿松石", "#AFEEEE", "PaleTurquoise"],
			["碧蓝", "#7FFFD4", "Aquamarine"], ["中海蓝", "#66CDAA", "MediumAquamarine"],
			["军校蓝", "#5F9EA0", "CadetBlue"], ["粉蓝", "#B0E0E6", "PowderBlue"],
			["湖蓝", "#30D5C8", "Lake Blue"], ["孔雀蓝", "#00A4A6", "Peacock Blue"]
		]),
		group("blue", "蓝", [
			["蓝", "#0000FF", "Blue"], ["中蓝", "#0000CD", "MediumBlue"], ["深蓝", "#00008B", "DarkBlue"],
			["海军", "#000080", "Navy"], ["午夜", "#191970", "MidnightBlue"], ["皇家蓝", "#4169E1", "RoyalBlue"],
			["道奇蓝", "#1E90FF", "DodgerBlue"], ["深天蓝", "#00BFFF", "DeepSkyBlue"], ["钢蓝", "#4682B4", "SteelBlue"],
			["浅钢蓝", "#B0C4DE", "LightSteelBlue"], ["天蓝", "#87CEEB", "SkyBlue"],
			["浅天蓝", "#87CEFA", "LightSkyBlue"], ["浅蓝", "#ADD8E6", "LightBlue"],
			["矢车菊", "#6495ED", "CornflowerBlue"], ["普鲁士蓝", "#003153", "Prussian Blue"]
		]),
		group("purple", "紫", [
			["紫", "#800080", "Purple"], ["暗洋红", "#8B008B", "DarkMagenta"], ["洋红", "#FF00FF", "Fuchsia / Magenta"],
			["深紫", "#4B0082", "Indigo"], ["丽贝卡紫", "#663399", "RebeccaPurple"], ["板岩蓝", "#6A5ACD", "SlateBlue"],
			["中板岩蓝", "#7B68EE", "MediumSlateBlue"], ["暗板岩蓝", "#483D8B", "DarkSlateBlue"],
			["蓝紫", "#8A2BE2", "BlueViolet"], ["暗紫罗兰", "#9400D3", "DarkViolet"], ["紫罗兰", "#EE82EE", "Violet"],
			["中紫", "#9370DB", "MediumPurple"], ["暗兰花", "#9932CC", "DarkOrchid"],
			["中兰花", "#BA55D3", "MediumOrchid"], ["兰花", "#DA70D6", "Orchid"], ["蓟色", "#D8BFD8", "Thistle"],
			["梅", "#DDA0DD", "Plum"], ["薰衣草", "#E6E6FA", "Lavender"], ["深紫红", "#C71585", "MediumVioletRed"]
		]),
		group("pink", "粉", [
			["粉", "#FFC0CB", "Pink"], ["浅粉", "#FFB6C1", "LightPink"], ["热粉", "#FF69B4", "HotPink"],
			["深粉", "#FF1493", "DeepPink"], ["玫瑰", "#FF007F", "Rose"],
			["苍紫罗兰红", "#DB7093", "PaleVioletRed"], ["浅玫瑰", "#FFE4E1", "MistyRose"]
		]),
		group("brown", "棕", [
			["棕", "#A52A2A", "Brown"], ["马鞍棕", "#8B4513", "SaddleBrown"], ["赭石", "#A0522D", "Sienna"],
			["秘鲁色", "#CD853F", "Peru"], ["巧克力", "#D2691E", "Chocolate"], ["咖啡", "#6F4E37", "Coffee"],
			["赭", "#CC7722", "Ochre"], ["沙", "#F4A460", "SandyBrown"], ["实木色", "#DEB887", "BurlyWood"],
			["棕褐", "#D2B48C", "Tan"], ["小麦", "#F5DEB3", "Wheat"], ["米色", "#F5F5DC", "Beige"],
			["玫瑰棕", "#BC8F8F", "RosyBrown"]
		]),
		group("gray", "灰", [
			["庚斯博罗灰", "#DCDCDC", "Gainsboro"], ["浅灰", "#D3D3D3", "LightGray / Grey"],
			["银", "#C0C0C0", "Silver"], ["板岩灰", "#708090", "SlateGray / Grey"],
			["浅板岩", "#778899", "LightSlateGray / Grey"], ["暗灰", "#A9A9A9", "DarkGray / Grey"],
			["灰", "#808080", "Gray / Grey"], ["昏灰", "#696969", "DimGray / Grey"],
			["暗板岩", "#2F4F4F", "DarkSlateGray / Grey"], ["深灰", "#404040", "Charcoal"], ["黑", "#000000", "Black"]
		]),
		group("paper", "白/纸色", [
			["白", "#FFFFFF", "White"], ["雪白", "#FFFAFA", "Snow"], ["象牙", "#FFFFF0", "Ivory"],
			["烟白", "#F5F5F5", "WhiteSmoke"], ["爱丽丝蓝", "#F0F8FF", "AliceBlue"],
			["幽灵白", "#F8F8FF", "GhostWhite"], ["天青", "#F0FFFF", "Azure"], ["蜜露", "#F0FFF0", "Honeydew"],
			["薄荷奶油", "#F5FFFA", "MintCream"], ["花白", "#FFFAF0", "FloralWhite"],
			["贝壳色", "#FFF5EE", "Seashell"], ["亚麻色", "#FAF0E6", "Linen"], ["老花边", "#FDF5E6", "OldLace"],
			["古董白", "#FAEBD7", "AntiqueWhite"], ["杏仁绸", "#FFEBCD", "BlanchedAlmond"],
			["番木瓜", "#FFEFD5", "PapayaWhip"], ["柠檬绸", "#FFFACD", "LemonChiffon"],
			["淡金菊", "#FAFAD2", "LightGoldenrodYellow"], ["薰衣草粉", "#FFF0F5", "LavenderBlush"]
		])
	];
	// C# 支：.NET 的 System.Drawing.KnownColor，命名色与系统色各一组（数据由本机 .NET 枚举生成）。
		// C# / .NET：System.Drawing.KnownColor，命名色按名字排序（色值取自本机 .NET）
	var CS_NAMED = [
			["爱丽丝蓝", "#F0F8FF", "AliceBlue"],
			["古董白", "#FAEBD7", "AntiqueWhite"],
			["青", "#00FFFF", "Aqua"],
			["碧蓝", "#7FFFD4", "Aquamarine"],
			["天青", "#F0FFFF", "Azure"],
			["米色", "#F5F5DC", "Beige"],
			["浓汤色", "#FFE4C4", "Bisque"],
			["黑", "#000000", "Black"],
			["杏仁绸", "#FFEBCD", "BlanchedAlmond"],
			["蓝", "#0000FF", "Blue"],
			["蓝紫", "#8A2BE2", "BlueViolet"],
			["棕", "#A52A2A", "Brown"],
			["实木色", "#DEB887", "BurlyWood"],
			["军校蓝", "#5F9EA0", "CadetBlue"],
			["查特绿", "#7FFF00", "Chartreuse"],
			["巧克力", "#D2691E", "Chocolate"],
			["珊瑚", "#FF7F50", "Coral"],
			["矢车菊", "#6495ED", "CornflowerBlue"],
			["玉米", "#FFF8DC", "Cornsilk"],
			["绯红", "#DC143C", "Crimson"],
			["青", "#00FFFF", "Cyan"],
			["深蓝", "#00008B", "DarkBlue"],
			["深青", "#008B8B", "DarkCyan"],
			["暗金", "#B8860B", "DarkGoldenrod"],
			["暗灰", "#A9A9A9", "DarkGray"],
			["暗绿", "#006400", "DarkGreen"],
			["暗卡其", "#BDB76B", "DarkKhaki"],
			["暗洋红", "#8B008B", "DarkMagenta"],
			["暗橄榄绿", "#556B2F", "DarkOliveGreen"],
			["深橙", "#FF8C00", "DarkOrange"],
			["暗兰花", "#9932CC", "DarkOrchid"],
			["深红", "#8B0000", "DarkRed"],
			["暗鲑鱼", "#E9967A", "DarkSalmon"],
			["暗海绿", "#8FBC8B", "DarkSeaGreen"],
			["暗板岩蓝", "#483D8B", "DarkSlateBlue"],
			["暗板岩", "#2F4F4F", "DarkSlateGray"],
			["碧", "#00CED1", "DarkTurquoise"],
			["暗紫罗兰", "#9400D3", "DarkViolet"],
			["深粉", "#FF1493", "DeepPink"],
			["深天蓝", "#00BFFF", "DeepSkyBlue"],
			["昏灰", "#696969", "DimGray"],
			["道奇蓝", "#1E90FF", "DodgerBlue"],
			["砖红", "#B22222", "Firebrick"],
			["花白", "#FFFAF0", "FloralWhite"],
			["森林", "#228B22", "ForestGreen"],
			["洋红", "#FF00FF", "Fuchsia"],
			["庚斯博罗灰", "#DCDCDC", "Gainsboro"],
			["幽灵白", "#F8F8FF", "GhostWhite"],
			["金", "#FFD700", "Gold"],
			["金菊色", "#DAA520", "Goldenrod"],
			["灰", "#808080", "Gray"],
			["绿", "#008000", "Green"],
			["绿黄", "#ADFF2F", "GreenYellow"],
			["蜜露", "#F0FFF0", "Honeydew"],
			["热粉", "#FF69B4", "HotPink"],
			["印度红", "#CD5C5C", "IndianRed"],
			["深紫", "#4B0082", "Indigo"],
			["象牙", "#FFFFF0", "Ivory"],
			["卡其", "#F0E68C", "Khaki"],
			["薰衣草", "#E6E6FA", "Lavender"],
			["薰衣草粉", "#FFF0F5", "LavenderBlush"],
			["草绿", "#7CFC00", "LawnGreen"],
			["柠檬绸", "#FFFACD", "LemonChiffon"],
			["浅蓝", "#ADD8E6", "LightBlue"],
			["浅珊瑚", "#F08080", "LightCoral"],
			["浅青", "#E0FFFF", "LightCyan"],
			["淡金菊", "#FAFAD2", "LightGoldenrodYellow"],
			["浅灰", "#D3D3D3", "LightGray"],
			["浅绿", "#90EE90", "LightGreen"],
			["浅粉", "#FFB6C1", "LightPink"],
			["浅鲑鱼", "#FFA07A", "LightSalmon"],
			["浅海绿", "#20B2AA", "LightSeaGreen"],
			["浅天蓝", "#87CEFA", "LightSkyBlue"],
			["浅板岩", "#778899", "LightSlateGray"],
			["浅钢蓝", "#B0C4DE", "LightSteelBlue"],
			["浅黄", "#FFFFE0", "LightYellow"],
			["纯绿", "#00FF00", "Lime"],
			["酸橙绿", "#32CD32", "LimeGreen"],
			["亚麻色", "#FAF0E6", "Linen"],
			["洋红", "#FF00FF", "Magenta"],
			["栗色", "#800000", "Maroon"],
			["中海蓝", "#66CDAA", "MediumAquamarine"],
			["中蓝", "#0000CD", "MediumBlue"],
			["中兰花", "#BA55D3", "MediumOrchid"],
			["中紫", "#9370DB", "MediumPurple"],
			["中海绿", "#3CB371", "MediumSeaGreen"],
			["中板岩蓝", "#7B68EE", "MediumSlateBlue"],
			["中春绿", "#00FA9A", "MediumSpringGreen"],
			["中绿松石", "#48D1CC", "MediumTurquoise"],
			["深紫红", "#C71585", "MediumVioletRed"],
			["午夜", "#191970", "MidnightBlue"],
			["薄荷奶油", "#F5FFFA", "MintCream"],
			["浅玫瑰", "#FFE4E1", "MistyRose"],
			["鹿皮色", "#FFE4B5", "Moccasin"],
			["纳瓦霍白", "#FFDEAD", "NavajoWhite"],
			["海军", "#000080", "Navy"],
			["老花边", "#FDF5E6", "OldLace"],
			["橄榄", "#808000", "Olive"],
			["橄榄绿", "#6B8E23", "OliveDrab"],
			["橙", "#FFA500", "Orange"],
			["暗橙", "#FF4500", "OrangeRed"],
			["兰花", "#DA70D6", "Orchid"],
			["苍金菊", "#EEE8AA", "PaleGoldenrod"],
			["苍绿", "#98FB98", "PaleGreen"],
			["苍绿松石", "#AFEEEE", "PaleTurquoise"],
			["苍紫罗兰红", "#DB7093", "PaleVioletRed"],
			["番木瓜", "#FFEFD5", "PapayaWhip"],
			["桃脯", "#FFDAB9", "PeachPuff"],
			["秘鲁色", "#CD853F", "Peru"],
			["粉", "#FFC0CB", "Pink"],
			["梅", "#DDA0DD", "Plum"],
			["粉蓝", "#B0E0E6", "PowderBlue"],
			["紫", "#800080", "Purple"],
			["红", "#FF0000", "Red"],
			["玫瑰棕", "#BC8F8F", "RosyBrown"],
			["皇家蓝", "#4169E1", "RoyalBlue"],
			["马鞍棕", "#8B4513", "SaddleBrown"],
			["鲑鱼", "#FA8072", "Salmon"],
			["沙", "#F4A460", "SandyBrown"],
			["海绿", "#2E8B57", "SeaGreen"],
			["贝壳色", "#FFF5EE", "SeaShell"],
			["赭石", "#A0522D", "Sienna"],
			["银", "#C0C0C0", "Silver"],
			["天蓝", "#87CEEB", "SkyBlue"],
			["板岩蓝", "#6A5ACD", "SlateBlue"],
			["板岩灰", "#708090", "SlateGray"],
			["雪白", "#FFFAFA", "Snow"],
			["春绿", "#00FF7F", "SpringGreen"],
			["钢蓝", "#4682B4", "SteelBlue"],
			["棕褐", "#D2B48C", "Tan"],
			["青绿", "#008080", "Teal"],
			["蓟色", "#D8BFD8", "Thistle"],
			["番茄", "#FF6347", "Tomato"],
			["透明", "#FFFFFF", "Transparent", "A=0"],
			["绿松石", "#40E0D0", "Turquoise"],
			["紫罗兰", "#EE82EE", "Violet"],
			["小麦", "#F5DEB3", "Wheat"],
			["白", "#FFFFFF", "White"],
			["烟白", "#F5F5F5", "WhiteSmoke"],
			["黄", "#FFFF00", "Yellow"],
			["黄绿", "#9ACD32", "YellowGreen"]
	];
	// .NET 系统色：色值随系统主题，这里是本机当前值
	var CS_SYS = [
			["活动边框", "#B4B4B4", "ActiveBorder"],
			["活动标题栏", "#99B4D1", "ActiveCaption"],
			["活动标题文字", "#000000", "ActiveCaptionText"],
			["应用工作区", "#ABABAB", "AppWorkspace"],
			["按钮面", "#F0F0F0", "ButtonFace"],
			["按钮高光", "#FFFFFF", "ButtonHighlight"],
			["按钮阴影", "#A0A0A0", "ButtonShadow"],
			["控件", "#F0F0F0", "Control"],
			["控件暗色", "#A0A0A0", "ControlDark"],
			["控件深暗色", "#696969", "ControlDarkDark"],
			["控件亮色", "#E3E3E3", "ControlLight"],
			["控件高亮色", "#FFFFFF", "ControlLightLight"],
			["控件文字", "#000000", "ControlText"],
			["桌面", "#000000", "Desktop"],
			["活动标题渐变", "#B9D1EA", "GradientActiveCaption"],
			["非活动标题渐变", "#D7E4F2", "GradientInactiveCaption"],
			["灰文字", "#6D6D6D", "GrayText"],
			["高亮", "#0078D7", "Highlight"],
			["高亮文字", "#FFFFFF", "HighlightText"],
			["热跟踪", "#0066CC", "HotTrack"],
			["非活动边框", "#F4F7FC", "InactiveBorder"],
			["非活动标题栏", "#BFCDDB", "InactiveCaption"],
			["非活动标题文字", "#000000", "InactiveCaptionText"],
			["提示框", "#FFFFE1", "Info"],
			["提示文字", "#000000", "InfoText"],
			["菜单", "#F0F0F0", "Menu"],
			["菜单栏", "#F0F0F0", "MenuBar"],
			["菜单高亮", "#3399FF", "MenuHighlight"],
			["菜单文字", "#000000", "MenuText"],
			["滚动条", "#C8C8C8", "ScrollBar"],
			["窗口", "#FFFFFF", "Window"],
			["窗口边框", "#646464", "WindowFrame"],
			["窗口文字", "#000000", "WindowText"]
	];
	var CS = [
		group("cs-named", "命名色", CS_NAMED),
		group("cs-sys", "系统色（随主题）", CS_SYS)
	];
	// 树：第一层是命名规范，第二层是分组；没有「全部」，两支互不混。
	var TREE = [
		{ id: "web", name: "web 颜色", groups: WEB },
		{ id: "cs", name: "C# 颜色", groups: CS }
	];
	var cur = { set: "web", g: "" };
	var expanded = { web: true, cs: true };

	SK.regutil("u-ctab", {
		title: "颜色表",
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
			cur = { set: "web", g: "" };
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

	function setof(id){
		var i;
		for (i = 0; i < TREE.length; i++) {
			if (TREE[i].id === id) return TREE[i];
		}
		return TREE[0];
	}

	function sizenode(groups){
		var n = 0;
		var i;
		for (i = 0; i < groups.length; i++) n += groups[i].colors.length;
		return n;
	}

	function ongroup(ev){
		var el = ev.target;
		while (el && el !== this) {
			// 点三角只展开/收起，点文字才是选中
			if (el.className && typeof el.className === "string" && el.className.indexOf("tw") === 0
				&& el.className.indexOf("leaf") < 0) {
				var id = el.parentNode.getAttribute("data-set");
				expanded[id] = !expanded[id];
				paintgroups();
				return;
			}
			if (el.getAttribute && el.getAttribute("data-set")) {
				cur = { set: el.getAttribute("data-set"), g: el.getAttribute("data-g") };
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
		// 也允许连写查询，例如 peacockblue、lightgray
		if (q.indexOf(" ") < 0 && en.replace(/\s+/g, "").replace(/\//g, "").indexOf(q) >= 0) return true;
		return false;
	}

	function listed(){
		var groups = setof(cur.set).groups;
		var q = query();
		var out = [];
		var i;
		var j;
		for (i = 0; i < groups.length; i++) {
			if (cur.g && groups[i].id !== cur.g) continue;
			for (j = 0; j < groups[i].colors.length; j++) {
				if (hit(groups[i].colors[j], q)) out.push(groups[i].colors[j]);
			}
		}
		return out;
	}

	function paintgroups(){
		var html = "";
		var i;
		var j;
		var t;
		for (i = 0; i < TREE.length; i++) {
			t = TREE[i];
			html += node(0, t.id, "", t.name, sizenode(t.groups), expanded[t.id]);
			if (!expanded[t.id]) continue;
			for (j = 0; j < t.groups.length; j++)
				html += node(1, t.id, t.groups[j].id, t.groups[j].name, t.groups[j].colors.length, false);
		}
		SK.$("c-groups").innerHTML = html;
	}

	function node(lv, set, g, name, n, opened){
		var on = cur.set === set && cur.g === g;
		return '<button type="button" class="tnode lv' + lv + (on ? " on" : "") + '" data-set="' + set + '" data-g="' + g + '">'
			+ (lv === 0 ? '<i class="tw' + (opened ? " open" : "") + '"></i>' : '<i class="tw leaf"></i>')
			+ "<b>" + esc(name) + "</b><small>" + n + "</small></button>";
	}

	function paint(){
		var rows = listed();
		var html = "";
		var i;
		var en;
		var note;
		for (i = 0; i < rows.length; i++) {
			en = rows[i][2] || "";
			note = rows[i][3] || "";
			html += '<button type="button" data-hex="' + rows[i][1] + '" data-name="' + esc(rows[i][0]) + '"'
				+ ' data-en="' + esc(en) + '"'
				+ ' title="' + esc(rows[i][0] + " " + en + "  " + rows[i][1] + (note ? "  " + note : "")) + '">'
				+ (note ? '<i class="chk"></i>' : '<i style="background:' + rows[i][1] + '"></i>')
				+ "<b>" + esc(rows[i][0]) + (en ? '<span class="en">' + esc(en) + "</span>" : "") + "</b>"
				+ "<small>" + rows[i][1] + (note ? '<span class="note">' + esc(note) + "</span>" : "") + "</small></button>";
		}
		if (!html) html = '<p class="cnone">没有这种颜色</p>';
		SK.$("c-grid").innerHTML = html;
	}

	function esc(s){
		return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/"/g, "&quot;");
	}

	SK.ctabgroups = TREE;
})();
