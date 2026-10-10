(function(){
	SK.regutil("u-px", {
		title: "Px / Rem",
		go: "换算",
		body: function(){
			return SK.row2(SK.lab("数值", SK.textin("box-in", "16")), SK.lab("根字号", SK.textin("box-root", "16")))
				+ SK.lab("方向", '<select id="box-mode"><option value="px">px 转 rem</option><option value="rem">rem 转 px</option></select>');
		},
		run: function(){
			return pxconv(SK.val("box-in"), SK.val("box-root"), SK.val("box-mode"));
		}
	});
	function pxconv(px, root, mode){
		var a = Number(String(px).trim());
		var r = Number(String(root).trim());
		if (!(r > 0)) throw new Error("根字号要大于 0");
		if (!isFinite(a)) throw new Error("请输入数字");
		if (mode === "rem") return SK.trimnum(a * r) + " px";
		return SK.trimnum(a / r) + " rem";
	}

	SK.pxconv = pxconv;
})();
