(function(){
	var nexts = ["#2563eb", "#16a34a", "#eab308", "#db2777", "#7c3aed", "#0f766e"];
	SK.regutil("u-grad", {
		title: "CSS 渐变",
		go: "生成",
		noGo: true,
		hideOut: true,
		body: function(){
			return SK.lab("类型", '<select id="box-type"><option value="linear">线性</option><option value="radial">径向</option><option value="conic">锥形</option></select>')
				+ '<div class="grad-lab">颜色</div>'
				+ '<div id="grad-stops" class="grad-stops"></div>'
				+ '<button type="button" id="grad-add" class="ghost">加一个颜色</button>'
				+ SK.lab("角度", SK.numin("box-n", "90"))
				+ '<div class="grad-row"><div id="grad-view" class="grad-view"></div><textarea id="grad-out" readonly></textarea></div>';
		},
		open: function(){
			addstop("#f97316");
			addstop("#111827");
			var add = SK.$("grad-add");
			if (add) add.onclick = function(){
				addstop(nexts[(stopcount() - 2) % nexts.length]);
				gradlive();
			};
			var body = SK.$("box-body");
			body.oninput = gradlive;
			body.onchange = gradlive;
			gradlive();
		},
		run: function(){
			return gradcss(SK.val("box-type"), stopcolors(), SK.val("box-n"));
		}
	});
	function addstop(hex){
		var box = SK.$("grad-stops");
		if (!box || stopcount() >= 8) return;
		var row = document.createElement("div");
		row.className = "grad-stop";
		row.innerHTML = '<input type="color" value="' + hex + '">'
			+ '<button type="button" class="ghost">去掉</button>';
		row.querySelector("button").onclick = function(){
			if (stopcount() <= 2) return;
			row.parentNode.removeChild(row);
			gradlive();
		};
		box.appendChild(row);
		syncstops();
	}

	function stopcount(){
		var box = SK.$("grad-stops");
		return box ? box.querySelectorAll("input[type=color]").length : 0;
	}

	function syncstops(){
		var n = stopcount();
		var box = SK.$("grad-stops");
		var add = SK.$("grad-add");
		if (add) add.hidden = n >= 8;
		if (!box) return;
		var btns = box.querySelectorAll("button");
		var i;
		for (i = 0; i < btns.length; i++) btns[i].hidden = n <= 2;
	}

	function stopcolors(){
		var box = SK.$("grad-stops");
		var out = [];
		if (!box) return out;
		var inputs = box.querySelectorAll("input[type=color]");
		var i;
		for (i = 0; i < inputs.length; i++) {
			var rgb = SK.colorrgb("hex", inputs[i].value);
			out.push("#" + SK.hex2(rgb[0]) + SK.hex2(rgb[1]) + SK.hex2(rgb[2]));
		}
		return out;
	}

	function gradlive(){
		var n = SK.$("box-n");
		if (n) n.disabled = SK.val("box-type") === "radial";
		syncstops();
		SK.utilrun();
		var shown = SK.$("grad-out");
		var box = SK.$("box-out");
		if (shown && box) shown.value = box.value;
	}
	function gradcss(type, colors, angle){
		type = String(type || "linear");
		if (type !== "linear" && type !== "radial" && type !== "conic") throw new Error("请选择渐变类型");
		if (!colors || colors.length < 2) throw new Error("至少要两种颜色");
		var n = Number(angle);
		if (type !== "radial" && (!isFinite(n) || n < 0 || n > 360)) throw new Error("角度要在 0 到 360");
		var deg = SK.trimnum(isFinite(n) ? n : 0);
		var list = colors.join(", ");
		var fn;
		if (type === "radial") fn = "radial-gradient(circle, " + list + ")";
		else if (type === "conic") fn = "conic-gradient(from " + deg + "deg, " + list + ")";
		else fn = "linear-gradient(" + deg + "deg, " + list + ")";
		var view = SK.$("grad-view");
		if (view) view.style.background = fn;
		return "background: " + fn + ";";
	}

	SK.gradcss = gradcss;
	SK.gradlive = gradlive;
})();
