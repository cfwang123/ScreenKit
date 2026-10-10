(function(){
	SK.regutil("u-repl", {
		title: "查找替换",
		go: "替换",
		body: function(){
			return SK.lab("文本", SK.ta("box-in", 8, ""))
				+ SK.row2(SK.lab("查找", SK.textin("box-find", "")), SK.lab("替换为", SK.textin("box-rep", "")))
				+ SK.check("box-re", "按正则", false);
		},
		run: function(){
			return repltext(SK.val("box-in"), SK.val("box-find"), SK.val("box-rep"), SK.onbox("box-re"));
		}
	});
	function repltext(text, find, rep, useRe){
		text = String(text);
		find = String(find);
		rep = String(rep);
		if (!find) throw new Error("请输入查找内容");
		if (text.length > 200000) throw new Error("文字太长");
		if (useRe) {
			if (find.length > 200) throw new Error("表达式太长");
			if (text.length > 20000) throw new Error("正则替换的文本最多 2 万字");
			var re;
			try { re = new RegExp(find, "g"); }
			catch (ex) { throw new Error("表达式无效"); }
			var n = 0;
			var guard = 0;
			var out = text.replace(re, function(){
				n++;
				guard++;
				if (guard > 10000) throw new Error("匹配太多");
				return rep;
			});
			return out + "\n\n替换 " + n + " 处";
		}
		var parts = text.split(find);
		return parts.join(rep) + "\n\n替换 " + (parts.length - 1) + " 处";
	}

	SK.repltext = repltext;
})();
