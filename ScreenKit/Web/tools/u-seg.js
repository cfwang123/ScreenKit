(function(){
	SK.regutil("u-seg", {
		title: "中文分词",
		go: "分词",
		live: true,
		body: body,
		open: open,
		run: run
	});

	function tx(key, fb){
		var s = SK.t ? SK.t(key) : "";
		return s || fb;
	}

	function body(){
		return '<p class="hint" id="seg-hint"></p>'
			+ '<label id="seg-glab"><select id="seg-g">'
			+ '<option value="word"></option>'
			+ '<option value="sentence"></option>'
			+ '<option value="grapheme"></option>'
			+ "</select></label>"
			+ '<label id="seg-inlab"><textarea id="seg-in" rows="8"></textarea></label>';
	}

	function open(){
		var go = F.id("box-go");
		if (go) {
			F.seta(go, "data-k", "web.seg.go");
			go.textContent = tx("web.seg.go", "分词");
		}
		var hint = F.id("seg-hint");
		if (hint) hint.textContent = tx("web.seg.hint", "用浏览器自带的分词，把中文按词、句或字素切开。");
		setlab("seg-glab", "web.seg.grain", "粒度");
		setlab("seg-inlab", "web.seg.in", "原文");
		setopt("word", "web.seg.word", "词");
		setopt("sentence", "web.seg.sentence", "句");
		setopt("grapheme", "web.seg.grapheme", "字素");
	}

	function setlab(id, key, fb){
		var el = F.id(id);
		if (!el) return;
		var text = tx(key, fb);
		if (el.childNodes.length && el.childNodes[0].nodeType === 3) el.childNodes[0].nodeValue = text;
		else el.insertBefore(document.createTextNode(text), el.firstChild);
	}

	function setopt(value, key, fb){
		var sel = F.id("seg-g");
		if (!sel) return;
		var i;
		for (i = 0; i < sel.options.length; i++) {
			if (sel.options[i].value === value) sel.options[i].textContent = tx(key, fb);
		}
	}

	function run(){
		if (typeof Intl === "undefined" || !Intl.Segmenter) throw new Error(tx("web.seg.none", "这个浏览器没有分词"));
		var sel = F.id("seg-g");
		var box = F.id("seg-in");
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
		if (grain === "word") return parts.join(" / ");
		return parts.join("\n");
	}
})();
