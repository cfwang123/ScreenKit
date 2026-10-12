(function(){
	var last = null;
	SK.regutil("u-ph", {
		title: "占位图",
		go: "生成",
		noGo: true,
		hideOut: true,
		body: function(){
			return SK.row2(SK.lab("宽", SK.numin("box-w", "320")), SK.lab("高", SK.numin("box-h", "180")))
				+ SK.row2(SK.lab("底色", SK.textin("box-bg", "#eef1f4")), SK.lab("字色", SK.textin("box-fg", "#333333")))
				+ SK.lab("文字", SK.textin("box-in", "", "留空则写宽高"))
				+ '<img id="box-img" alt="" hidden>';
		},
		run: function(){
			return placeholder(SK.val("box-w"), SK.val("box-h"), SK.val("box-in"), SK.val("box-bg"), SK.val("box-fg"));
		},
		open: function(){
			hookcopy();
			var body = SK.$("box-body");
			if (body) {
				body.oninput = draw;
				body.onchange = draw;
			}
			draw();
		}
	});
	function draw(){
		try {
			placeholder(SK.val("box-w"), SK.val("box-h"), SK.val("box-in"), SK.val("box-bg"), SK.val("box-fg"));
			SK.msg("box-msg", "Ctrl+C 复制图片");
		}
		catch (ex) {
			last = null;
			var img = SK.$("box-img");
			if (img) {
				img.hidden = true;
				img.removeAttribute("src");
			}
			SK.msg("box-msg", ex && ex.message ? ex.message : "失败", true);
		}
	}

	function placesize(w, h){
		var width = parseInt(w, 10);
		var height = parseInt(h, 10);
		if (!(width >= 1 && width <= 2000) || !(height >= 1 && height <= 2000)) throw new Error("宽高要在 1 到 2000");
		return [width, height];
	}

	function placeholder(w, h, label, bg, fg){
		var wh = placesize(w, h);
		var back = SK.colorrgb("hex", bg);
		var ink = SK.colorrgb("hex", fg);
		if (typeof document === "undefined" || !document.createElement) throw new Error("浏览器不能画图");
		var canvas = document.createElement("canvas");
		canvas.width = wh[0];
		canvas.height = wh[1];
		var g = canvas.getContext("2d");
		if (!g) throw new Error("浏览器不能画图");
		g.fillStyle = "#" + SK.hex2(back[0]) + SK.hex2(back[1]) + SK.hex2(back[2]);
		g.fillRect(0, 0, wh[0], wh[1]);
		g.fillStyle = "#" + SK.hex2(ink[0]) + SK.hex2(ink[1]) + SK.hex2(ink[2]);
		g.font = Math.max(12, Math.round(Math.min(wh[0], wh[1]) / 8)) + "px sans-serif";
		g.textAlign = "center";
		g.textBaseline = "middle";
		g.fillText(String(label || "").trim() || (wh[0] + "×" + wh[1]), wh[0] / 2, wh[1] / 2);
		last = canvas;
		var img = SK.$("box-img");
		if (img) {
			img.src = canvas.toDataURL("image/png");
			img.hidden = false;
		}
		return img && img.src ? img.src : "";
	}

	function hookcopy(){
		var btn = SK.$("box-copy");
		if (btn && !btn.getAttribute("data-ph")) {
			btn.setAttribute("data-ph", "1");
			var orig = btn.onclick;
			btn.onclick = function(){
				var img = SK.$("box-img");
				if (img && !img.hidden && last) copyph();
				else if (orig) orig.call(btn);
			};
		}
		if (!hookcopy.bound) {
			hookcopy.bound = true;
			document.addEventListener("keydown", onkey);
		}
	}

	function onkey(ev){
		if ((ev.key !== "c" && ev.key !== "C") || !ev.ctrlKey || ev.altKey || ev.metaKey || ev.shiftKey) return;
		var img = SK.$("box-img");
		if (!img || img.hidden || !last) return;
		var el = document.activeElement;
		if (el && (el.tagName === "INPUT" || el.tagName === "TEXTAREA")) {
			var a = el.selectionStart;
			var b = el.selectionEnd;
			if (typeof a === "number" && typeof b === "number" && b > a) return;
		}
		ev.preventDefault();
		copyph();
	}

	function copyph(){
		if (!last) {
			SK.msg("box-msg", "还没有图片", true);
			return;
		}
		if (!navigator.clipboard || !window.ClipboardItem || !navigator.clipboard.write) {
			SK.msg("box-msg", "浏览器不能复制图片", true);
			return;
		}
		var canvas = last;
		var item = new ClipboardItem({
			"image/png": new Promise(function(resolve, reject){
				canvas.toBlob(function(blob){
					if (blob) resolve(blob);
					else reject(new Error("empty"));
				}, "image/png");
			}),
		});
		navigator.clipboard.write([item]).then(function(){
			SK.msg("box-msg", "已复制图片");
		}, function(){
			SK.msg("box-msg", "复制失败", true);
		});
	}

	SK.placesize = placesize;
	SK.placeholder = placeholder;
})();
