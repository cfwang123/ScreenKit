(function(){
	SK.regutil("u-diff", {
		title: "文本比对",
		go: "比对",
		body: function(){
			return '<div class="split">' + SK.lab("原文", SK.ta("box-a", 10, "")) + SK.lab("新文", SK.ta("box-b", 10, "")) + "</div>";
		},
		run: function(){
			return linediff(SK.val("box-a"), SK.val("box-b"));
		}
	});
	function linediff(a, b){
		var left = String(a).replace(/\r\n/g, "\n").replace(/\r/g, "\n").split("\n");
		var right = String(b).replace(/\r\n/g, "\n").replace(/\r/g, "\n").split("\n");
		if (left.length > 400 || right.length > 400) throw new Error("每边最多 400 行");
		var n = left.length;
		var m = right.length;
		var dp = new Array(n + 1);
		var i;
		var j;
		for (i = 0; i <= n; i++) dp[i] = new Uint16Array(m + 1);
		for (i = 1; i <= n; i++) {
			for (j = 1; j <= m; j++) {
				if (left[i - 1] === right[j - 1]) dp[i][j] = dp[i - 1][j - 1] + 1;
				else dp[i][j] = dp[i - 1][j] >= dp[i][j - 1] ? dp[i - 1][j] : dp[i][j - 1];
			}
		}
		var out = [];
		i = n;
		j = m;
		while (i > 0 || j > 0) {
			if (i > 0 && j > 0 && left[i - 1] === right[j - 1]) {
				out.push("  " + left[i - 1]);
				i--;
				j--;
			}
			else if (j > 0 && (i === 0 || dp[i][j - 1] >= dp[i - 1][j])) {
				out.push("+ " + right[j - 1]);
				j--;
			}
			else {
				out.push("- " + left[i - 1]);
				i--;
			}
		}
		out.reverse();
		return out.join("\n");
	}

	SK.linediff = linediff;
})();
