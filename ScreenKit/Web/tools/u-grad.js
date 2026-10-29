(function(){
	var nexts = ["#2563eb", "#16a34a", "#eab308", "#db2777", "#7c3aed", "#0f766e"];
	SK.regutil("u-grad", {
		title: "CSS 渐变",
		go: "生成",
		noGo: true,
		hideOut: true,
		body: function(t){
			return [
				SK.lab("类型", F.CreateDOM(t, { x: "select#box-type", c: [
					{ x: "option", value: "linear", c: "线性" },
					{ x: "option", value: "radial", c: "径向" },
					{ x: "option", value: "conic", c: "锥形" }
				] })),
				F.CreateDOM(t, { x: "div.grad-lab", c: "颜色" }),
				F.CreateDOM(t, { x: "div#grad-stops.grad-stops" }),
				F.CreateDOM(t, { x: "button#grad-add.ghost", type: "button", c: "加一个颜色", onclick: function(){
					addstop(nexts[(stopcount() - 2) % nexts.length]);
					gradlive();
				} }),
				SK.lab("角度", SK.numin("box-n", "90")),
				F.CreateDOM(t, { x: "div.grad-row", c: [
					F.CreateDOM(t, { x: "div#grad-view.grad-view" }),
					F.CreateDOM(t, { x: "textarea#grad-out", readonly: true })
				] })
			];
		},
		open: function(){
			addstop("#f97316");
			addstop("#111827");
			var body = SK.dom("box-body");
			body.oninput = gradlive;
			body.onchange = gradlive;
			gradlive();
		},
		run: function(){
			return gradcss(SK.val("box-type"), stopcolors(), SK.val("box-n"));
		}
	});
	function addstop(hex){
		var box = SK.dom("grad-stops");
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
		var box = SK.dom("grad-stops");
		return box ? F.qa("input[type=color]", box).length : 0;
	}

	function syncstops(){
		var n = stopcount();
		var box = SK.dom("grad-stops");
		var add = SK.dom("grad-add");
		if (add) add.hidden = n >= 8;
		if (!box) return;
		var btns = F.qa("button", box);
		var i;
		for (i = 0; i < btns.length; i++) btns[i].hidden = n <= 2;
	}

	function stopcolors(){
		var box = SK.dom("grad-stops");
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
		var n = SK.dom("box-n");
		if (n) n.disabled = SK.val("box-type") === "radial";
		syncstops();
		SK.utilrun();
		var shown = SK.dom("grad-out");
		var box = SK.dom("box-out");
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
		var view = SK.dom("grad-view");
		if (view) view.style.background = fn;
		return "background: " + fn + ";";
	}

	SK.gradcss = gradcss;
	SK.gradlive = gradlive;
})();
