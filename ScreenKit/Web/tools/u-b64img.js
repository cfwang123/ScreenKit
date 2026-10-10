(function(){
	SK.regutil("u-b64img", {
		title: "图片 Base64",
		go: "转换",
		body: function(){
			return SK.lab("图片", '<input id="box-file" type="file" accept="image/*">');
		},
		run: function(){
			imgb64();
		},
		own: true
	});
	function imgb64(){
		var input = SK.$("box-file");
		var file = input && input.files && input.files[0];
		if (!file) {
			SK.msg("box-msg", "请选择图片", true);
			return;
		}
		if (file.size > 8 * 1024 * 1024) {
			SK.msg("box-msg", "图片要小于 8 MB", true);
			return;
		}
		var reader = new FileReader();
		reader.onload = function(){
			SK.$("box-out").value = String(reader.result || "");
			SK.msg("box-msg", "完成");
		};
		reader.onerror = function(){ SK.msg("box-msg", "读取失败", true); };
		SK.msg("box-msg", "读取中");
		reader.readAsDataURL(file);
	}

	SK.imgb64 = imgb64;
})();
