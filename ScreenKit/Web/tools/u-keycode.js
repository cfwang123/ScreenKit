(function(){
	// [分组, 按键, event.code, event.key, keyCode, VK, 说明, 备注]
	function R(g, key, code, keyname, keyCode, vk, desc, note){
		return {
			g: g,
			c: [key, code, keyname, String(keyCode), vk],
			x: desc + " " + (note || ""),
			d: [["类别", catname(g)], ["说明", desc], ["备注", note || "—"]]
		};
	}

	var CATS = {
		letter: "字母",
		digit: "数字",
		fn: "功能键",
		edit: "编辑与导航",
		mod: "修饰键",
		numpad: "小键盘",
		symbol: "符号",
		media: "媒体与浏览器"
	};

	var ROWS = [];
	var i;

	for (i = 0; i < 26; i++) {
		var ch = String.fromCharCode(65 + i);
		ROWS.push(R("letter", ch, "Key" + ch, ch.toLowerCase() + " / " + ch, 65 + i,
			"0x" + (65 + i).toString(16).toUpperCase(), "字母 " + ch,
			"event.key 会随 Shift 和键盘布局变化，event.code 固定。"));
	}
	for (i = 0; i < 10; i++) {
		ROWS.push(R("digit", String(i), "Digit" + i, String(i), 48 + i,
			"0x" + (48 + i).toString(16).toUpperCase(), "数字 " + i,
			"key 值受 Shift 和键盘布局影响，code 固定。"));
	}
	for (i = 1; i <= 12; i++) {
		ROWS.push(R("fn", "F" + i, "F" + i, "F" + i, 111 + i,
			"0x" + (111 + i).toString(16).toUpperCase(), "功能键 F" + i, ""));
	}

	ROWS.push(
		R("edit", "Backspace", "Backspace", "Backspace", 8, "0x08", "退格", ""),
		R("edit", "Tab", "Tab", "Tab", 9, "0x09", "制表键", ""),
		R("edit", "Enter", "Enter", "Enter", 13, "0x0D", "回车", "小键盘回车是 NumpadEnter。"),
		R("edit", "Pause", "Pause", "Pause", 19, "0x13", "暂停", ""),
		R("edit", "CapsLock", "CapsLock", "CapsLock", 20, "0x14", "大写锁定", ""),
		R("edit", "Escape", "Escape", "Escape", 27, "0x1B", "取消 / 退出", ""),
		R("edit", "Space", "Space", " ", 32, "0x20", "空格", ""),
		R("edit", "PageUp", "PageUp", "PageUp", 33, "0x21", "向上翻页", ""),
		R("edit", "PageDown", "PageDown", "PageDown", 34, "0x22", "向下翻页", ""),
		R("edit", "End", "End", "End", 35, "0x23", "行尾 / 文末", ""),
		R("edit", "Home", "Home", "Home", 36, "0x24", "行首 / 文首", ""),
		R("edit", "←", "ArrowLeft", "ArrowLeft", 37, "0x25", "左方向键", ""),
		R("edit", "↑", "ArrowUp", "ArrowUp", 38, "0x26", "上方向键", ""),
		R("edit", "→", "ArrowRight", "ArrowRight", 39, "0x27", "右方向键", ""),
		R("edit", "↓", "ArrowDown", "ArrowDown", 40, "0x28", "下方向键", ""),
		R("edit", "PrintScreen", "PrintScreen", "PrintScreen", 44, "0x2C", "截屏键", ""),
		R("edit", "Insert", "Insert", "Insert", 45, "0x2D", "插入 / 改写", ""),
		R("edit", "Delete", "Delete", "Delete", 46, "0x2E", "删除", ""),
		R("edit", "ContextMenu", "ContextMenu", "ContextMenu", 93, "0x5D", "右键菜单键", ""),
		R("edit", "NumLock", "NumLock", "NumLock", 144, "0x90", "数字锁定", ""),
		R("edit", "ScrollLock", "ScrollLock", "ScrollLock", 145, "0x91", "滚动锁定", ""),

		R("mod", "左 Shift", "ShiftLeft", "Shift", 16, "0xA0", "左 Shift", "VK 用左右分开的值。"),
		R("mod", "右 Shift", "ShiftRight", "Shift", 16, "0xA1", "右 Shift", "VK 用左右分开的值。"),
		R("mod", "左 Ctrl", "ControlLeft", "Control", 17, "0xA2", "左 Control", "VK 用左右分开的值。"),
		R("mod", "右 Ctrl", "ControlRight", "Control", 17, "0xA3", "右 Control", "VK 用左右分开的值。"),
		R("mod", "左 Alt", "AltLeft", "Alt", 18, "0xA4", "左 Alt", "VK 用左右分开的值。"),
		R("mod", "右 Alt", "AltRight", "Alt", 18, "0xA5", "右 Alt", "VK 用左右分开的值。"),
		R("mod", "左 Win", "MetaLeft", "Meta", 91, "0x5B", "左 Windows / Command 键", ""),
		R("mod", "右 Win", "MetaRight", "Meta", 92, "0x5C", "右 Windows / Command 键", ""),
		R("mod", "AltGr", "AltGraph", "AltGraph", 225, "—", "AltGr（欧洲键盘）", "部分浏览器把它当 Ctrl+Alt。")
	);

	for (i = 0; i < 10; i++) {
		ROWS.push(R("numpad", "小键盘 " + i, "Numpad" + i, String(i), 96 + i,
			"0x" + (96 + i).toString(16).toUpperCase(), "小键盘数字 " + i, ""));
	}
	ROWS.push(
		R("numpad", "小键盘 *", "NumpadMultiply", "*", 106, "0x6A", "小键盘乘号", ""),
		R("numpad", "小键盘 +", "NumpadAdd", "+", 107, "0x6B", "小键盘加号", ""),
		R("numpad", "小键盘 -", "NumpadSubtract", "-", 109, "0x6D", "小键盘减号", ""),
		R("numpad", "小键盘 .", "NumpadDecimal", ".", 110, "0x6E", "小键盘小数点", ""),
		R("numpad", "小键盘 /", "NumpadDivide", "/", 111, "0x6F", "小键盘除号", ""),
		R("numpad", "小键盘 Enter", "NumpadEnter", "Enter", 13, "0x0D", "小键盘回车", "keyCode 和 Enter 相同，code 不同。"),

		R("symbol", ";", "Semicolon", ";", 186, "0xBA", "分号", "keyCode 随键盘布局变化。"),
		R("symbol", "=", "Equal", "=", 187, "0xBB", "等号", "keyCode 随键盘布局变化。"),
		R("symbol", ",", "Comma", ",", 188, "0xBC", "逗号", "keyCode 随键盘布局变化。"),
		R("symbol", "-", "Minus", "-", 189, "0xBD", "减号 / 下划线", "keyCode 随键盘布局变化。"),
		R("symbol", ".", "Period", ".", 190, "0xBE", "句点", "keyCode 随键盘布局变化。"),
		R("symbol", "/", "Slash", "/", 191, "0xBF", "斜杠", "keyCode 随键盘布局变化。"),
		R("symbol", "`", "Backquote", "`", 192, "0xC0", "反引号", "keyCode 随键盘布局变化。"),
		R("symbol", "[", "BracketLeft", "[", 219, "0xDB", "左方括号", "keyCode 随键盘布局变化。"),
		R("symbol", "\\", "Backslash", "\\", 220, "0xDC", "反斜杠", "keyCode 随键盘布局变化。"),
		R("symbol", "]", "BracketRight", "]", 221, "0xDD", "右方括号", "keyCode 随键盘布局变化。"),
		R("symbol", "'", "Quote", "'", 222, "0xDE", "单引号", "keyCode 随键盘布局变化。"),
		R("symbol", "\\", "IntlBackslash", "\\", 226, "0xE2", "国际键盘的反斜杠键", "ISO 键盘上常见。"),

		R("media", "浏览器后退", "BrowserBack", "BrowserBack", 166, "0xA6", "浏览器后退", ""),
		R("media", "浏览器前进", "BrowserForward", "BrowserForward", 167, "0xA7", "浏览器前进", ""),
		R("media", "刷新", "BrowserRefresh", "BrowserRefresh", 168, "0xA8", "浏览器刷新", ""),
		R("media", "停止", "BrowserStop", "BrowserStop", 169, "0xA9", "停止加载", ""),
		R("media", "搜索", "BrowserSearch", "BrowserSearch", 170, "0xAA", "打开搜索", ""),
		R("media", "收藏", "BrowserFavorites", "BrowserFavorites", 171, "0xAB", "打开收藏夹", ""),
		R("media", "主页", "BrowserHome", "BrowserHome", 172, "0xAC", "浏览器主页", ""),
		R("media", "静音", "AudioVolumeMute", "AudioVolumeMute", 173, "0xAD", "音量静音", ""),
		R("media", "音量 -", "AudioVolumeDown", "AudioVolumeDown", 174, "0xAE", "降低音量", ""),
		R("media", "音量 +", "AudioVolumeUp", "AudioVolumeUp", 175, "0xAF", "提高音量", ""),
		R("media", "下一曲", "MediaTrackNext", "MediaTrackNext", 176, "0xB0", "媒体下一曲", ""),
		R("media", "上一曲", "MediaTrackPrevious", "MediaTrackPrevious", 177, "0xB1", "媒体上一曲", ""),
		R("media", "停止播放", "MediaStop", "MediaStop", 178, "0xB2", "媒体停止", ""),
		R("media", "播放 / 暂停", "MediaPlayPause", "MediaPlayPause", 179, "0xB3", "媒体播放暂停", ""),
		R("media", "邮件", "LaunchMail", "LaunchMail", 180, "0xB4", "打开邮件", ""),
		R("media", "媒体播放器", "LaunchMediaPlayer", "LaunchMediaPlayer", 181, "0xB5", "打开媒体播放器", ""),
		R("media", "应用 1", "LaunchApp1", "LaunchApp1", 182, "0xB6", "打开应用 1", ""),
		R("media", "应用 2", "LaunchApp2", "LaunchApp2", 183, "0xB7", "打开应用 2", "")
	);

	SK.regref("u-keycode", {
		title: "键盘码",
		cols: [
			{ name: "按键", nowrap: true },
			{ name: "event.code", mono: true, nowrap: true },
			{ name: "event.key", mono: true, nowrap: true },
			{ name: "keyCode", mono: true, nowrap: true },
			{ name: "VK", mono: true, nowrap: true }
		],
		widths: ["120px", "150px", "120px", "90px", "90px"],
		groups: [
			{ id: "letter", name: "字母" },
			{ id: "digit", name: "数字" },
			{ id: "fn", name: "功能键" },
			{ id: "edit", name: "编辑与导航" },
			{ id: "mod", name: "修饰键" },
			{ id: "numpad", name: "小键盘" },
			{ id: "symbol", name: "符号" },
			{ id: "media", name: "媒体与浏览器" }
		],
		rows: ROWS,
		placeholder: "搜索：KeyA、Enter、65、方向键",
		big: 0,
		sub: 1,
		copy: 1,
		defaultPick: "KeyA",
		sample: function(r){
			return 'if (e.code === "' + r.c[1] + '") { /* ' + SK.td(r.c[0]) + " */ }";
		}
	});

	function catname(id){
		return CATS[id] || "";
	}
})();
