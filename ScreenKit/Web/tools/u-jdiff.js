(function(){
	SK.regutil("u-jdiff", {
		title: "JSON 差异",
		go: "比对",
		body: function(){
			return '<div class="split">' + SK.lab("JSON 甲", SK.ta("box-a", 10, "")) + SK.lab("JSON 乙", SK.ta("box-b", 10, "")) + "</div>";
		},
		run: function(){
			return jsondiff(SK.val("box-a"), SK.val("box-b"));
		}
	});
	function jsondiff(a, b){
		var left = String(a);
		var right = String(b);
		if (left.length > 200000 || right.length > 200000) throw new Error("文字太长");
		var ja;
		var jb;
		try { ja = JSON.stringify(JSON.parse(left), null, 2); }
		catch (ex) { throw new Error("甲不是合法的 JSON"); }
		try { jb = JSON.stringify(JSON.parse(right), null, 2); }
		catch (ex) { throw new Error("乙不是合法的 JSON"); }
		return SK.linediff(ja, jb);
	}

	SK.jsondiff = jsondiff;
})();
