(function(){
	// 表在 EnConj.IR，打开时从 /api/conj/irr 读取。过去式、过去分词里的「/」表示常见变体，归组和复制只按第一个。
	var ROWS = [];

	var PATS = ["AAA", "AAB", "ABA", "ABB", "ABC"];

	// 界面文案只有中英两种，按中文原文查这张表，查不到就显示原文。
	var EN = {
		"全部": "All",
		"点击复制 %s": "Click to copy %s",
		"没有匹配的动词": "No matching verbs",
		"共 %n 个": "%n verbs",
		"%n 个": "%n",
		"原形": "Base",
		"过去式": "Past",
		"过去分词": "Past participle",
		"释义": "Meaning",
		"类型": "Pattern",
		"备注": "Note",
		"三种形式": "Three forms",
		"搜索：go、went、买、ABC": "Search: go, went, buy, ABC",
		"三形相同": "all three forms the same",
		"原形与过去式相同": "base and past the same",
		"原形与过去分词相同": "base and participle the same",
		"过去式与过去分词相同": "past and participle the same",
		"三形都不同": "all three forms different",
		"AAA · 三形相同": "AAA · all three forms the same",
		"AAB · 原形与过去式相同": "AAB · base and past the same",
		"ABA · 原形与过去分词相同": "ABA · base and participle the same",
		"ABB · 过去式与过去分词相同": "ABB · past and participle the same",
		"ABC · 三形都不同": "ABC · all three forms different"
	};

	function tr(s){
		var text = String(s == null ? "" : s);
		return SK.langcode() === "zh" ? text : (EN[text] || text);
	}

	var GROUPS = [
		{ id: "AAA", name: "AAA", title: "AAA · 三形相同" },
		{ id: "AAB", name: "AAB", title: "AAB · 原形与过去式相同" },
		{ id: "ABA", name: "ABA", title: "ABA · 原形与过去分词相同" },
		{ id: "ABB", name: "ABB", title: "ABB · 过去式与过去分词相同" },
		{ id: "ABC", name: "ABC", title: "ABC · 三形都不同" }
	];

	var COLS = [
		{ name: "原形", cls: "ivbase" },
		{ name: "过去式" },
		{ name: "过去分词" },
		{ name: "释义" }
	];

	function prim(s){
		return String(s == null ? "" : s).split("/")[0].replace(/\s+/g, " ").trim();
	}

	function pat(row){
		var b = prim(row[0]);
		var p = prim(row[1]);
		var q = prim(row[2]);
		if (b === p && p === q) return "AAA";
		if (b === p) return "AAB";
		if (b === q) return "ABA";
		if (p === q) return "ABB";
		return "ABC";
	}

	function forms(row){
		return prim(row[0]) + " / " + prim(row[1]) + " / " + prim(row[2]);
	}

	// 拉丁字母按词首匹配，免得查 go 时把 forgo、undergo 也带出来；中文仍按子串。
	function hit(row, q){
		var blob = (row.c[0] + " " + row.c[1] + " " + row.c[2] + " " + row.c[3] + " " + row.g).toLowerCase();
		var terms = q.split(/\s+/);
		var i;
		for (i = 0; i < terms.length; i++) {
			if (!terms[i]) continue;
			if (/^[a-z0-9.]+$/.test(terms[i])) {
				if (!prefix(blob, terms[i])) return false;
			}
			else if (blob.indexOf(terms[i]) < 0) return false;
		}
		return true;
	}

	function prefix(blob, term){
		var words = blob.split(/[^a-z0-9.]+/);
		var i;
		for (i = 0; i < words.length; i++) if (words[i].indexOf(term) === 0) return true;
		return false;
	}

	function tabrow(r){
		var p = pat(r);
		var d = [["过去式", r[1]], ["过去分词", r[2]], ["类型", p]];
		if (r[5]) d.push(["备注", r[5][0]]);
		return { g: p, c: [r[0], r[1], r[2], r[3] || ""], x: (r[5] ? r[5][0] : ""), d: d, s: forms(r) };
	}

	SK.regref("u-iverb", {
		title: "英文不规则动词",
		cls: "iverbs",
		cols: COLS,
		groups: GROUPS,
		rows: [],
		placeholder: "搜索：go、went、买、ABC",
		big: 0,
		sub: 3,
		copy: 0,
		copyfn: forms,
		tr: tr,
		hit: hit,
		count: "共 %n 个",
		countHit: "%n 个",
		sampleTitle: "三种形式",
		load: function(done){
			fetch("/api/conj/irr").then(function(r){ return r.json(); }).then(function(jo){
				var rows = (jo && jo.data && jo.data.rows) || [];
				var out = [];
				var i;
				for (i = 0; i < rows.length; i++) out.push(tabrow(rows[i]));
				done(out);
			}, function(){
				done([]);
			});
		}
	});

	var esc = SK.esc;
})();
