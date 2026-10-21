(function(){
	SK.regref("u-tz", {
		title: "时区表",
		cols: [
			{ name: "IANA 名称", mono: true, nowrap: true },
			{ name: "缩写", mono: true, nowrap: true },
			{ name: "标准", mono: true, nowrap: true },
			{ name: "夏令时", mono: true, nowrap: true },
			{ name: "主要城市" }
		],
		widths: ["165px", "110px", "78px", "78px", ""],
		groups: [
			{ id: "asia", name: "亚洲" },
			{ id: "europe", name: "欧洲" },
			{ id: "africa", name: "非洲" },
			{ id: "americas", name: "美洲" },
			{ id: "oceania", name: "大洋洲" },
			{ id: "special", name: "特殊" }
		],
		rows: SK.tzdata,
		placeholder: "搜索：北京、UTC+8、PST（缩写可能重复，以 IANA 名为准）",
		big: 0,
		sub: 4,
		copy: 0,
		defaultPick: "Asia/Shanghai",
		bigCls: "small",
		sample: function(r){
			return 'Intl.DateTimeFormat("zh-CN", { timeZone: "' + r.c[0] + '" }).format(new Date())';
		}
	});
})();
