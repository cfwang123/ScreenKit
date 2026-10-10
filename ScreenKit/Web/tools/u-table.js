(function(){
	SK.regutil("u-table", {
		title: "HTML 表格",
		go: "生成",
		body: function(){
			return SK.lab("表格文字", SK.ta("box-in", 8, "第一行是表头。用逗号或制表符分列"));
		},
		run: function(){
			return htmltable(SK.val("box-in"));
		}
	});
	function htmltable(text){
		var raw = String(text).replace(/\r\n/g, "\n").replace(/\r/g, "\n");
		if (raw.length > 200000) throw new Error("文字太长");
		var rows = raw.split("\n");
		var body = [];
		var i;
		for (i = 0; i < rows.length; i++) if (rows[i]) body.push(rows[i]);
		if (!body.length) throw new Error("请输入表格文字");
		if (body.length > 500) throw new Error("最多 500 行");
		var sep = body[0].indexOf("\t") >= 0 ? "\t" : ",";
		var html = "<table>\n";
		for (i = 0; i < body.length; i++) {
			var cells = body[i].split(sep);
			var tag = i === 0 ? "th" : "td";
			var j;
			html += "<tr>";
			for (j = 0; j < cells.length; j++) html += "<" + tag + ">" + SK.htmlenc(cells[j]) + "</" + tag + ">";
			html += "</tr>\n";
		}
		return html + "</table>";
	}

	SK.htmltable = htmltable;
})();
