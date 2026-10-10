(function(){
	SK.regutil("u-ico", {
		title: "图片转 ICO",
		go: "生成",
		body: function(){
			return SK.lab("图片", '<input id="box-file" type="file" accept="image/*">')
				+ '<p class="hint">在此页按 Ctrl+V 可粘贴剪贴板里的图片。</p>'
				+ '<img id="ico-img" alt="预览" hidden>'
				+ '<div class="ops">'
				+ SK.radio("ico-size", "ico-16", "16", false) + SK.radio("ico-size", "ico-32", "32", true)
				+ SK.radio("ico-size", "ico-48", "48", false) + SK.radio("ico-size", "ico-64", "64", false)
				+ SK.radio("ico-size", "ico-128", "128", false) + SK.radio("ico-size", "ico-256", "256", false)
				+ "</div>"
				+ '<div class="ico-out"><canvas id="ico-prev" width="32" height="32"></canvas><span id="ico-prev-size">32×32</span></div>'
				+ '<p class="hint">只生成所选的一种尺寸。方图按实际像素显示，边框就是图标大小。画面等比放进方框，空白透明。</p>';
		},
		run: function(){
			icogo();
		},
		open: function(){
			SK.$("box-file").onchange = function(){
				var file = SK.$("box-file").files && SK.$("box-file").files[0];
				if (file) SK.seticopic(file);
			};
			var sizes = document.querySelectorAll('input[name="ico-size"]');
			var s;
			for (s = 0; s < sizes.length; s++) sizes[s].onchange = drawicoprev;
			if (SK.icoUrl) {
				var prev = SK.$("ico-img");
				prev.onload = drawicoprev;
				prev.src = SK.icoUrl;
				prev.hidden = false;
			}
			drawicoprev();
		},
		hideOut: true,
		hideCopy: true,
		own: true
	});
	function icosize(){
		var picked = document.querySelector('input[name="ico-size"]:checked');
		var size = picked ? parseInt(picked.value, 10) : 32;
		if (size !== 16 && size !== 32 && size !== 48 && size !== 64 && size !== 128 && size !== 256)
			size = 32;
		return size;
	}

	function drawicoprev(){
		var canvas = SK.$("ico-prev");
		if (!canvas) return;
		var size = icosize();
		var label = SK.$("ico-prev-size");
		if (label) label.textContent = size + "×" + size;
		canvas.width = size;
		canvas.height = size;
		var g = canvas.getContext("2d");
		if (!g) return;
		g.clearRect(0, 0, size, size);
		var src = SK.$("ico-img");
		if (!src || src.hidden || !src.naturalWidth) return;
		fiticon(g, src, size);
	}

	function fiticon(g, img, size){
		var iw = img.naturalWidth || img.width;
		var ih = img.naturalHeight || img.height;
		if (!iw || !ih) return;
		var scale = Math.min(size / iw, size / ih);
		var w = Math.max(1, Math.round(iw * scale));
		var h = Math.max(1, Math.round(ih * scale));
		g.imageSmoothingEnabled = true;
		try { g.imageSmoothingQuality = "high"; } catch (e) {}
		g.drawImage(img, Math.floor((size - w) / 2), Math.floor((size - h) / 2), w, h);
	}

	function icogo(){
		if (!SK.icoPic) {
			SK.msg("box-msg", "请选择或粘贴图片", true);
			return;
		}
		var size = icosize();
		var sizes = [size];
		var img = new Image();
		img.onload = function(){
			var jobs = [];
			var n;
			for (n = 0; n < sizes.length; n++) jobs.push(iconpng(img, sizes[n]));
			Promise.all(jobs).then(function(parts){
				var blob = icoblob(sizes, parts);
				var a = document.createElement("a");
				var url = URL.createObjectURL(blob);
				a.href = url;
				a.download = iconame(SK.icoPic.name);
				document.body.appendChild(a);
				a.click();
				document.body.removeChild(a);
				setTimeout(function(){ URL.revokeObjectURL(url); }, 4000);
				SK.msg("box-msg", "已下载 " + a.download + "（" + blob.size + " 字节）");
			}, function(){
				SK.msg("box-msg", "生成失败", true);
			});
		};
		img.onerror = function(){ SK.msg("box-msg", "无法读取图片", true); };
		SK.msg("box-msg", "生成中");
		img.src = SK.icoUrl;
	}

	function iconame(name){
		var base = String(name || "icon").replace(/\.[^.]+$/, "");
		if (!base) base = "icon";
		return base + ".ico";
	}

	function iconpng(img, size){
		var canvas = document.createElement("canvas");
		canvas.width = size;
		canvas.height = size;
		var g = canvas.getContext("2d");
		if (!g) return Promise.reject();
		g.clearRect(0, 0, size, size);
		fiticon(g, img, size);
		return new Promise(function(ok, bad){
			canvas.toBlob(function(blob){
				if (!blob) { bad(); return; }
				blob.arrayBuffer().then(ok, bad);
			}, "image/png");
		});
	}

	function icoblob(sizes, parts){
		var count = sizes.length;
		var off = 6 + count * 16;
		var total = off;
		var i;
		for (i = 0; i < parts.length; i++) total += parts[i].byteLength;
		var buf = new Uint8Array(total);
		var view = new DataView(buf.buffer);
		view.setUint16(2, 1, true);
		view.setUint16(4, count, true);
		for (i = 0; i < count; i++) {
			var at = 6 + i * 16;
			var side = sizes[i] >= 256 ? 0 : sizes[i];
			buf[at] = side;
			buf[at + 1] = side;
			view.setUint16(at + 4, 1, true);
			view.setUint16(at + 6, 32, true);
			view.setUint32(at + 8, parts[i].byteLength, true);
			view.setUint32(at + 12, off, true);
			buf.set(new Uint8Array(parts[i]), off);
			off += parts[i].byteLength;
		}
		return new Blob([buf], { type: "image/vnd.microsoft.icon" });
	}

	SK.icosize = icosize;
	SK.drawicoprev = drawicoprev;
	SK.fiticon = fiticon;
	SK.icogo = icogo;
	SK.iconame = iconame;
	SK.iconpng = iconpng;
	SK.icoblob = icoblob;
})();
