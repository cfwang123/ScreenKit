(function(){
	var page = null;
	var nexts = ["#2563eb", "#16a34a", "#eab308", "#db2777", "#7c3aed", "#0f766e"];
	SK.regutil("u-grad", {
		title: "CSS 渐变",
		go: "生成",
		noGo: true,
		hideOut: true,
		body: function(t){
			page = t;
			return [
				SK.lab("类型", F.CreateDOM(t, { x: "select.box-type", name: "boxType", c: [
					{ x: "option", value: "linear", c: "线性" },
					{ x: "option", value: "radial", c: "径向" },
					{ x: "option", value: "conic", c: "锥形" }
				] })),
				F.CreateDOM(t, { x: "div.grad-lab", c: "颜色" }),
				F.CreateDOM(t, { x: "div.grad-stops.grad-stops", name: "gradStops" }),
				F.CreateDOM(t, { x: "button.grad-add.ghost", name: "gradAdd", type: "button", c: "加一个颜色", onclick: function(){
					addstop(nexts[(stopcount() - 2) % nexts.length]);
					gradlive();
				} }),
				SK.lab("角度", SK.numin("box-n", "90")),
				F.CreateDOM(t, { x: "div.grad-row", c: [
					{ x: "div.grad-view.grad-view", name: "gradView" },
					{ x: "textarea.grad-out", name: "gradOut", readonly: true }
				] })
			];
		},
		open: function(t){
			page = t;
			addstop("#f97316");
			addstop("#111827");
			var body = SK.doms.boxBody;
			body.oninput = gradlive;
			body.onchange = gradlive;
			gradlive();
		},
		run: function(){
			return gradcss(SK.val("box-type"), stopcolors(), SK.val("box-n"));
		}
	});
	function addstop(hex){
		var box = page.doms.gradStops;
		if (!box || stopcount() >= 8) return;
		var row = F.el("div");
		row.className = "grad-stop";
		row.innerHTML = '<input type="color" value="' + hex + '">'
			+ '<button type="button" class="ghost">去掉</button>';
		F.q("button", row).onclick = function(){
			if (stopcount() <= 2) return;
			row.parentNode.removeChild(row);
			gradlive();
		};
		F.append(box, row);
		syncstops();
	}

	function stopcount(){
		var box = page.doms.gradStops;
		return box ? F.qa("input[type=color]", box).length : 0;
	}

	function syncstops(){
		var n = stopcount();
		var box = page.doms.gradStops;
		var add = page.doms.gradAdd;
		if (add) add.hidden = n >= 8;
		if (!box) return;
		var btns = F.qa("button", box);
		var i;
		for (i = 0; i < btns.length; i++) btns[i].hidden = n <= 2;
	}

	function stopcolors(){
		var box = page.doms.gradStops;
		var out = [];
		if (!box) return out;
		var inputs = F.qa("input[type=color]", box);
		var i;
		for (i = 0; i < inputs.length; i++) {
			var rgb = SK.colorrgb("hex", inputs[i].value);
			out.push("#" + SK.hex2(rgb[0]) + SK.hex2(rgb[1]) + SK.hex2(rgb[2]));
		}
		return out;
	}

	function gradlive(){
		var n = page.doms.boxN;
		if (n) n.disabled = SK.val("box-type") === "radial";
		syncstops();
		SK.utilrun();
		var shown = page.doms.gradOut;
		var box = SK.doms.boxOut;
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
		var view = page.doms.gradView;
		if (view) view.style.background = fn;
		return "background: " + fn + ";";
	}

	SK.gradcss = gradcss;
	SK.gradlive = gradlive;
})();
