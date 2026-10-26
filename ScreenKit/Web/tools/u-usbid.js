(function(){
	var ROWS = [
		["046D", "C52B", "Logitech", "Unifying Receiver"],
		["046D", "C534", "Logitech", "Unifying Receiver"],
		["046D", "C52F", "Logitech", "Unifying Receiver"],
		["046D", "C077", "Logitech", "USB Optical Mouse"],
		["046D", "C31C", "Logitech", "Keyboard K120"],
		["046D", "0825", "Logitech", "Webcam C270"],
		["05AC", "12A8", "Apple", "iPhone"],
		["05AC", "12AB", "Apple", "iPad"],
		["045E", "0745", "Microsoft", "Nano Transceiver v2.0"],
		["045E", "0750", "Microsoft", "Wired Keyboard 600"],
		["045E", "00CB", "Microsoft", "Basic Optical Mouse"],
		["8087", "0026", "Intel", "Bluetooth"],
		["8087", "0A2A", "Intel", "Bluetooth"],
		["8087", "0AAA", "Intel", "Bluetooth"],
		["0BDA", "8153", "Realtek", "RTL8153 Gigabit"],
		["0BDA", "8152", "Realtek", "RTL8152 Fast Ethernet"],
		["0BDA", "0129", "Realtek", "RTS5129 Card Reader"],
		["0BDA", "5411", "Realtek", "RTS5411 Hub"],
		["0BDA", "B812", "Realtek", "RTL8822BU WiFi"],
		["04E8", "6860", "Samsung", "Galaxy"],
		["04E8", "685D", "Samsung", "Galaxy"],
		["18D1", "4EE7", "Google", "Android ADB"],
		["18D1", "4EE1", "Google", "Nexus / Pixel"],
		["18D1", "D00D", "Google", "Android Accessory"],
		["054C", "05C4", "Sony", "DualShock 4"],
		["054C", "09CC", "Sony", "DualShock 4"],
		["054C", "0CE6", "Sony", "DualSense"],
		["057E", "2009", "Nintendo", "Switch Pro Controller"],
		["28DE", "1142", "Valve", "Steam Controller"],
		["28DE", "1205", "Valve", "Steam Deck"],
		["0403", "6001", "FTDI", "FT232R UART"],
		["0403", "6010", "FTDI", "FT2232 Dual UART"],
		["0403", "6014", "FTDI", "FT232H"],
		["0403", "6015", "FTDI", "FT-X UART"],
		["1A86", "7523", "QinHeng", "CH340"],
		["1A86", "5523", "QinHeng", "CH341"],
		["10C4", "EA60", "Silicon Labs", "CP210x UART"],
		["067B", "2303", "Prolific", "PL2303"],
		["0781", "5581", "SanDisk", "Ultra"],
		["0781", "5567", "SanDisk", "Cruzer Blade"],
		["0951", "1666", "Kingston", "DataTraveler"],
		["058F", "6387", "Alcor", "Flash Drive"],
		["8564", "1000", "Transcend", "JetFlash"],
		["0E8D", "2000", "MediaTek", "Preloader"],
		["0E8D", "201D", "MediaTek", "Android"],
		["12D1", "1F01", "Huawei", "Mobile Broadband"],
		["2717", "FF40", "Xiaomi", "Mi / Redmi"],
		["0A5C", "21E8", "Broadcom", "Bluetooth"],
		["0489", "E0A2", "Foxconn", "Bluetooth"]
	];
	var picked = "";
	var gen = 0;

	SK.regutil("u-usbid", {
		title: "常见 USB ID",
		body: function(){
			return '<div class="uquery"><input id="usb-q" type="text" placeholder="'
				+ esc(SK.td("厂家、产品、046d 或 046d:c52b")) + '">'
				+ '<span id="usb-msg" class="msg"></span></div>'
				+ '<p class="hint">' + esc(SK.td("常见设备的厂家、产品和型号。点一行复制 VID:PID，并按 usb.ids 补上接口。")) + "</p>"
				+ '<div class="usbwrap">'
				+ '<div class="usbscroll"><table class="usblist" id="usb-tab"></table></div>'
				+ '<pre id="usb-detail" class="usbdetail"></pre>'
				+ "</div>";
		},
		run: function(){
			throw new Error("没有这个工具");
		},
		open: function(){
			picked = "";
			F.id("usb-q").oninput = function(){ paint(F.id("usb-q").value); };
			F.id("usb-tab").onclick = onrow;
			paint("");
		},
		hideGo: true,
		live: false,
		hideOut: true,
		relang: function(){ SK.reopenutil(); }
	});

	function paint(q){
		var rows = match(q);
		var html = ["<thead><tr><th>VID</th><th>PID</th><th>" + esc(SK.td("厂家")) + "</th><th>"
			+ esc(SK.td("产品 / 型号")) + "</th></tr></thead><tbody>"];
		var i;
		for (i = 0; i < rows.length; i++) {
			var r = rows[i];
			var id = r[0] + r[1];
			html.push('<tr data-id="' + id + '"' + (id === picked ? ' class="on"' : "") + ">"
				+ "<td>" + r[0] + "</td><td>" + r[1] + "</td><td>" + esc(r[2]) + "</td><td>" + esc(r[3]) + "</td></tr>");
		}
		html.push("</tbody>");
		F.id("usb-tab").innerHTML = html.join("");
		var msg = SK.td("%n 条").replace("%n", rows.length);
		var idq = looks(q);
		if (idq && rows.length === 0) msg = SK.td("不在常见列表里，正在查询…");
		F.id("usb-msg").textContent = msg;
		if (idq && rows.length === 0) ask(idq.vid, idq.pid, null);
	}

	function match(q){
		q = (q || "").replace(/^\s+|\s+$/g, "").toLowerCase();
		if (!q) return ROWS.slice();
		var out = [];
		var i;
		for (i = 0; i < ROWS.length; i++) {
			var r = ROWS[i];
			var blob = (r[0] + " " + r[1] + " " + r[0] + ":" + r[1] + " " + r[2] + " " + r[3]).toLowerCase();
			if (blob.indexOf(q) >= 0) out.push(r);
		}
		return out;
	}

	function onrow(ev){
		var el = ev.target;
		while (el && el.tagName !== "TR") el = el.parentNode;
		if (!el || !F.attr(el, "data-id")) return;
		var id = F.attr(el, "data-id");
		picked = id;
		var r = rowby(id);
		if (!r) return;
		paint(F.id("usb-q").value);
		showlocal(r);
		try {
			var p = F.clip(r[0] + ":" + r[1]);
			if (p && p.then) p.then(function(){}, function(){});
		}
		catch (e) {}
		ask(r[0], r[1], r);
	}

	function rowby(id){
		var i;
		for (i = 0; i < ROWS.length; i++) if (ROWS[i][0] + ROWS[i][1] === id) return ROWS[i];
		return null;
	}

	function showlocal(r){
		F.id("usb-detail").textContent = "USB: " + r[0] + ":" + r[1]
			+ "\n" + SK.td("厂家") + ": " + r[2]
			+ "\n" + SK.td("产品") + ": " + r[3]
			+ "\n" + SK.td("型号") + ": " + r[3];
	}

	function ask(vid, pid, local){
		var n = ++gen;
		var url = "/api/usb?vid=" + encodeURIComponent(vid);
		if (pid) url += "&pid=" + encodeURIComponent(pid);
		SK.get(url, function(data){
			if (n !== gen) return;
			F.id("usb-detail").textContent = data && data.text ? data.text : "";
			F.id("usb-msg").textContent = local ? SK.td("已复制 %s").replace("%s", local[0] + ":" + local[1]) : SK.td("已查到");
		}, "usb-msg", function(){
			if (n !== gen) return;
			if (local) showlocal(local);
		});
	}

	function looks(q){
		q = (q || "").replace(/^\s+|\s+$/g, "");
		if (!q) return null;
		var hw = q.match(/VID_([0-9a-fA-F]{4}).*PID_([0-9a-fA-F]{4})/);
		if (hw) return { vid: hw[1].toUpperCase(), pid: hw[2].toUpperCase() };
		var s = q.replace(/^0x/i, "").replace(/\s+/g, "");
		var m = s.match(/^([0-9a-fA-F]{4})(?::([0-9a-fA-F]{4}))?$/);
		if (!m) return null;
		return { vid: m[1].toUpperCase(), pid: m[2] ? m[2].toUpperCase() : "" };
	}

	function esc(s){
		return String(s).replace(/[&<>"]/g, function(ch){
			if (ch === "&") return "&amp;";
			if (ch === "<") return "&lt;";
			if (ch === ">") return "&gt;";
			return "&quot;";
		});
	}
})();
