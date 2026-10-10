(function(){
	SK.regutil("u-color", {
		title: "颜色转换",
		go: "转换",
		body: function(){
			return SK.lab("颜色", SK.textin("box-in", "#3366cc", "HEX、RGB、HSL、HSV 或 CMYK"))
				+ SK.lab("格式", '<select id="box-mode"><option value="hex">HEX</option><option value="rgb">RGB</option><option value="hsl">HSL</option><option value="hsv">HSV</option><option value="cmyk">CMYK</option></select>');
		},
		run: function(){
			return SK.colorconv(SK.val("box-mode"), SK.val("box-in"));
		}
	});
})();
