// 俄语变形只在 web 端实现，这里复用 u-rverb.js 的 build() 导出 JSON。
// 用法：node scripts/export-ru-conj.js [out.json]
(function(){
	const fs = require("fs");
	const path = require("path");
	const mod = require(path.join(__dirname, "..", "ScreenKit", "Web", "tools", "u-rverb.js"));
	const groups = [
		["verb", "动词", ["читать", "прочитать", "идти", "быть", "писать", "говорить"]],
		["adj", "形容词", ["новый", "большой", "хороший"]],
		["noun", "名词", ["книга", "стол", "окно", "студент"]],
	];
	main();

	function main(){
		const out = process.argv[2] || "conj-ru.json";
		const types = groups.map(function(g){
			return buildtype(g[0], g[1], g[2]);
		});
		const doc = {
			schema: "screenkit.conj.v1",
			lang: "ru",
			exported_at: new Date().toISOString().slice(0, 19).replace("T", " "),
			types: types,
		};
		fs.writeFileSync(out, JSON.stringify(doc, null, 2) + "\n", "utf8");
		console.log("wrote " + path.resolve(out));
		console.log("lang=ru types=" + types.length + " words=" + count(types, "words") + " forms=" + count(types, "forms"));
	}

	function buildtype(kind, name, words){
		return {
			kind: kind,
			name: name,
			words: words.map(function(word){
				return buildword(word, kind, name);
			}),
		};
	}

	function buildword(word, kind, name){
		const r = mod.build(word, "zh", { kind: kind });
		return {
			head: r.head || word,
			name: name,
			detail: "",
			ok: !!r.ok,
			err: r.ok ? "" : (r.err || ""),
			note: r.note || "",
			forms: (r.rows || []).map(function(row){
				return {
					group: row.g || "基础",
					label: row.name || "",
					value: row.text || "",
				};
			}),
		};
	}

	function count(types, key){
		if (key === "words") return types.reduce(function(n, t){ return n + t.words.length; }, 0);
		return types.reduce(function(n, t){
			return n + t.words.reduce(function(m, w){ return m + w.forms.length; }, 0);
		}, 0);
	}
})();
