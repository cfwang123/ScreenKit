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
				+ '<div class="ops">'
				+ '<label class="check"><input id="ico-mode-fit" name="ico-mode" type="radio" value="fit" checked> fit</label>'
				+ '<label class="check"><input id="ico-mode-stretch" name="ico-mode" type="radio" value="stretch"> 拉伸</label>'
				+ '<label class="check"><input id="ico-mode-crop" name="ico-mode" type="radio" value="crop"> 裁剪</label>'
				+ "</div>"
				+ '<div class="ico-out"><canvas id="ico-prev" width="32" height="32"></canvas><span id="ico-prev-size">32×32</span></div>'
				+ '<p class="hint">只生成所选的一种尺寸。方图按实际像素显示，边框就是图标大小。fit 等比放进方框，空白透明。拉伸铺满方框。裁剪从中间铺满，多出的部分切掉。</p>';
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
			var modes = document.querySelectorAll('input[name="ico-mode"]');
			for (s = 0; s < modes.length; s++) modes[s].onchange = drawicoprev;
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
		fiticon(g, src, size, icomode());
	}

	function icomode(){
		var picked = document.querySelector('input[name="ico-mode"]:checked');
		var mode = picked ? picked.value : "fit";
		if (mode !== "stretch" && mode !== "crop") mode = "fit";
		return mode;
	}

	function fiticon(g, img, size, mode){
		var iw = img.naturalWidth || img.width;
		var ih = img.naturalHeight || img.height;
		if (!iw || !ih) return;
		if (mode !== "stretch" && mode !== "crop") mode = "fit";
		if (mode === "crop") {
			g.imageSmoothingEnabled = true;
			try { g.imageSmoothingQuality = "high"; } catch (e) {}
			var cscale = Math.max(size / iw, size / ih);
			var cw = Math.max(1, Math.round(iw * cscale));
			var ch = Math.max(1, Math.round(ih * cscale));
			g.drawImage(img, Math.floor((size - cw) / 2), Math.floor((size - ch) / 2), cw, ch);
			return;
		}
		var dw = size;
		var dh = size;
		var dx = 0;
		var dy = 0;
		if (mode !== "stretch") {
			var scale = Math.min(size / iw, size / ih);
			dw = Math.max(1, Math.round(iw * scale));
			dh = Math.max(1, Math.round(ih * scale));
			dx = Math.floor((size - dw) / 2);
			dy = Math.floor((size - dh) / 2);
		}
		drawsharp(g, img, 0, 0, iw, ih, dx, dy, dw, dh);
	}

	// 一次 drawImage 把大图缩进 16～32 会发糊。缩小改为按面积平均，预览和 ico 同一条路径。
	function drawsharp(g, img, sx, sy, sw, sh, dx, dy, dw, dh){
		if (dw >= sw && dh >= sh) {
			g.imageSmoothingEnabled = true;
			try { g.imageSmoothingQuality = "high"; } catch (e) {}
			g.drawImage(img, sx, sy, sw, sh, dx, dy, dw, dh);
			return;
		}
		var scaled = boxicon(img, sx, sy, sw, sh, dw, dh);
		if (!scaled) {
			g.imageSmoothingEnabled = true;
			try { g.imageSmoothingQuality = "low"; } catch (e2) {}
			g.drawImage(img, sx, sy, sw, sh, dx, dy, dw, dh);
			return;
		}
		g.drawImage(scaled, dx, dy);
	}

	function boxicon(img, sx, sy, sw, sh, dw, dh){
		var c, cg, src, out, dst, o, og, y, x, y0f, y1f, x0f, x1f, j0, j1, i0, i1, j, i;
		var top, bot, left, right, wy, wx, wgt, si, pa, accA, accR, accG, accB, cov, di;
		try {
			c = document.createElement("canvas");
			c.width = sw;
			c.height = sh;
			cg = c.getContext("2d", { willReadFrequently: true });
			if (!cg) return null;
			cg.drawImage(img, sx, sy, sw, sh, 0, 0, sw, sh);
			src = cg.getImageData(0, 0, sw, sh).data;
			out = cg.createImageData(dw, dh);
			dst = out.data;
			for (y = 0; y < dh; y++) {
				y0f = y * sh / dh;
				y1f = (y + 1) * sh / dh;
				j0 = Math.floor(y0f);
				j1 = Math.ceil(y1f);
				if (j1 > sh) j1 = sh;
				for (x = 0; x < dw; x++) {
					x0f = x * sw / dw;
					x1f = (x + 1) * sw / dw;
					i0 = Math.floor(x0f);
					i1 = Math.ceil(x1f);
					if (i1 > sw) i1 = sw;
					accA = 0;
					accR = 0;
					accG = 0;
					accB = 0;
					cov = 0;
					for (j = j0; j < j1; j++) {
						top = j < y0f ? y0f : j;
						bot = j + 1 > y1f ? y1f : j + 1;
						wy = bot - top;
						if (wy <= 0) continue;
						for (i = i0; i < i1; i++) {
							left = i < x0f ? x0f : i;
							right = i + 1 > x1f ? x1f : i + 1;
							wx = right - left;
							if (wx <= 0) continue;
							wgt = wx * wy;
							si = (j * sw + i) * 4;
							pa = src[si + 3];
							accR += src[si] * pa * wgt;
							accG += src[si + 1] * pa * wgt;
							accB += src[si + 2] * pa * wgt;
							accA += pa * wgt;
							cov += wgt;
						}
					}
					di = (y * dw + x) * 4;
					if (accA > 0 && cov > 0) {
						dst[di] = Math.round(accR / accA);
						dst[di + 1] = Math.round(accG / accA);
						dst[di + 2] = Math.round(accB / accA);
						dst[di + 3] = Math.round(accA / cov);
					}
				}
			}
			o = document.createElement("canvas");
			o.width = dw;
			o.height = dh;
			og = o.getContext("2d");
			if (!og) return null;
			og.putImageData(out, 0, 0);
			return o;
		} catch (err) {
			return null;
		}
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
		fiticon(g, img, size, icomode());
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
