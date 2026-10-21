(function(){
	var D = SK.codedata;

	SK.regref("u-codes", {
		title: "国家 / 语言 / 货币代码",
		sets: [
			{
				id: "country",
				name: "国家/地区",
				placeholder: "搜索：中国、CN、CHN、156、+86",
				cols: [
					{ name: "代码", mono: true, nowrap: true },
					{ name: "三位", mono: true, nowrap: true },
					{ name: "数字", mono: true, nowrap: true },
					{ name: "中文名" },
					{ name: "电话", mono: true, nowrap: true },
					{ name: "货币", mono: true, nowrap: true }
				],
				widths: ["60px", "64px", "58px", "", "82px", "78px"],
				groups: [
					{ id: "asia", name: "亚洲" },
					{ id: "europe", name: "欧洲" },
					{ id: "africa", name: "非洲" },
					{ id: "americas", name: "美洲" },
					{ id: "oceania", name: "大洋洲" },
					{ id: "antarctic", name: "南极洲" }
				],
				rows: D.country,
				big: 0,
				sub: 3,
				copy: 0,
				defaultPick: "CN",
				sample: function(r){
					return 'Intl.DisplayNames("zh-CN", { type: "region" }).of("' + r.c[0] + '")';
				}
			},
			{
				id: "language",
				name: "语言",
				placeholder: "搜索：中文、zh、chi",
				cols: [
					{ name: "639-1", mono: true, nowrap: true },
					{ name: "639-2/B", mono: true, nowrap: true },
					{ name: "中文名" },
					{ name: "English" }
				],
				widths: ["64px", "80px", "", "150px"],
				groups: [],
				rows: D.language,
				big: 0,
				sub: 2,
				copy: 0,
				defaultPick: "zh",
				sample: function(r){
					return 'Intl.DisplayNames("zh-CN", { type: "language" }).of("' + r.c[0] + '")';
				}
			},
			{
				id: "currency",
				name: "货币",
				placeholder: "搜索：人民币、CNY、¥",
				cols: [
					{ name: "代码", mono: true, nowrap: true },
					{ name: "数字", mono: true, nowrap: true },
					{ name: "符号", mono: true, nowrap: true },
					{ name: "中文名" },
					{ name: "小数位", mono: true, nowrap: true }
				],
				widths: ["64px", "58px", "64px", "", "60px"],
				groups: [],
				rows: D.currency,
				big: 0,
				sub: 3,
				copy: 0,
				defaultPick: "CNY",
				sample: function(r){
					return 'Intl.DisplayNames("zh-CN", { type: "currency" }).of("' + r.c[0] + '")';
				}
			}
		]
	});
})();
