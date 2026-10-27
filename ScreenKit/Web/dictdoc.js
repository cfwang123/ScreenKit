// 词典词条渲染：C# 先把词条拼成「行 + 行内块」的 JSON，这里按同一份结构画到网页。
// 行：[缩进(0/1), 行后距, [行内块…]]；空行就是行内块为空的行。
// 行内块：["t", 文本, 角色, 语言] / ["k", 文案键, 角色, 后缀, 语言] /
//         ["a", 文本, 查询词, 语言] / ["v", 朗读文本, 语言]
// 词典页和阅读器页共用；文案键由调用方传进来的 t() 翻译。
(function(){
	"use strict";

	var ROLE = { b: "", t: "title", r: "pron", p: "pos", n: "num", l: "lab", x: "ex", e: "exlab" };

	function render(box, doc, opt){
		if (!box) return;
		box.innerHTML = "";
		opt = opt || {};
		var lines = (doc && doc.lines) || [];
		for (var i = 0; i < lines.length; i++) line(box, lines[i], opt);
	}

	function line(box, ln, opt){
		if (!ln || ln.length < 3) return;
		var el = document.createElement("p");
		if (ln[0]) el.style.marginLeft = "16px";
		if (ln[1]) el.style.marginBottom = ln[1] + "px";
		var runs = ln[2] || [];
		for (var i = 0; i < runs.length; i++) run(el, runs[i], opt);
		box.appendChild(el);
	}

	function run(parent, r, opt){
		if (!r || !r.length) return;
		if (r[0] === "t") { span(parent, r[1], r[2], r[3]); return; }
		if (r[0] === "k") {
			var label = opt.t ? (opt.t(r[1]) || "") : "";
			span(parent, label + (r[3] || ""), r[2], r[4]);
			return;
		}
		if (r[0] === "a") {
			var b = document.createElement("button");
			b.type = "button";
			b.className = "link";
			b.setAttribute("data-q", r[2] || r[1]);
			if (r[3]) b.setAttribute("data-lang", r[3]);
			b.textContent = r[1];
			parent.appendChild(b);
			return;
		}
		if (r[0] === "v") {
			parent.appendChild(document.createTextNode(" "));
			var s = document.createElement("button");
			s.type = "button";
			s.className = "spk";
			s.setAttribute("data-say", r[1]);
			s.setAttribute("data-lang", r[2] || "");
			s.textContent = "🔊";
			parent.appendChild(s);
		}
	}

	function span(parent, text, role, lang){
		if (!text) return;
		var el = document.createElement("span");
		var cls = ROLE[role || "b"];
		if (cls) el.className = cls;
		if (lang) el.setAttribute("data-lang", lang);
		el.textContent = text;
		parent.appendChild(el);
	}

	window.SKDictDoc = { render: render };
})();
