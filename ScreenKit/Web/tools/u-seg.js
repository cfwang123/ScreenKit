(function(){
	SK.regutil("u-seg", {
		title: "中文分词",
		go: "分词",
		live: true,
		body: body,
		open: open,
		run: run
	});

	var tx = SK.tx;

	function body(t){
		return [
			F.CreateDOM(t, { x: "p#seg-hint.hint" }),
			F.CreateDOM(t, { x: "label#seg-glab", c: [
				{ x: "select#seg-g", c: [
					{ x: "option", value: "word" },
					{ x: "option", value: "sentence" },
					{ x: "option", value: "grapheme" }
				] }
			] }),
			F.CreateDOM(t, { x: "label#seg-inlab", c: [
				{ x: "textarea#seg-in", rows: "8" }
			] })
		];
	}

	function open(){
		var go = SK.dom("box-go");
		if (go) {
			F.seta(go, "data-k", "web.seg.go");
			go.textContent = tx("web.seg.go", "分词");
		}
		var hint = SK.dom("seg-hint");
		if (hint) hint.textContent = tx("web.seg.hint", "用浏览器自带的分词，把中文按词、句或字素切开。");
		setlab("seg-glab", "web.seg.grain", "粒度");
		setlab("seg-inlab", "web.seg.in", "原文");
		setopt("word", "web.seg.word", "词");
		setopt("sentence", "web.seg.sentence", "句");
		setopt("grapheme", "web.seg.grapheme", "字素");
	}

	var setlab = SK.setlab;

	function setopt(value, key, fb){ SK.setopt("seg-g", value, key, fb); }

	function run(){
		if (typeof Intl === "undefined" || !Intl.Segmenter) throw new Error(tx("web.seg.none", "这个浏览器没有分词"));
		var sel = SK.dom("seg-g");
		var box = SK.dom("seg-in");
		var grain = sel ? sel.value : "word";
		var text = box ? String(box.value || "") : "";
		if (!text) return "";
		var seg = new Intl.Segmenter("zh", { granularity: grain });
		var iter = seg.segment(text)[Symbol.iterator]();
		var parts = [];
		var step, piece;
		while (!(step = iter.next()).done) {
			piece = step.value && step.value.segment;
			if (piece) parts.push(piece);
		}
		return parts.join("\n");
	}
})();
