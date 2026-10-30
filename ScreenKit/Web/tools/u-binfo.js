(function(){
	var info = {};
	var po = null;
	var idleAbort = null;
	var screens = null;
	var screensHooked = false;
	var ORDER = ["ua", "brand", "platform", "arch", "cores", "ram", "heap", "storage", "screen", "dpr", "touch", "lang", "net", "pdf", "pressure", "layout", "screens", "idle"];

	SK.regutil("u-binfo", {
		title: "本机信息",
		hideGo: true,
		live: false,
		body: body,
		open: open
	});

	function tx(key, fb){
		var s = SK.t ? SK.t(key) : "";
		return s || fb;
	}

	function body(){
		return '<p class="hint" id="binfo-hint"></p>'
			+ '<div class="ops">'
			+ '<button type="button" id="binfo-refresh"></button>'
			+ '<button type="button" id="binfo-screens"></button>'
			+ '<button type="button" id="binfo-idle"></button>'
			+ "</div>";
	}

	function open(){
		info = {};
		var refresh = F.id("binfo-refresh");
		var scr = F.id("binfo-screens");
		var idle = F.id("binfo-idle");
		if (refresh) refresh.onclick = function(){ collect(); };
		if (scr) scr.onclick = readscreens;
		if (idle) idle.onclick = startidle;
		labels();
		collect();
		startpressure();
	}

	function labels(){
		var hint = F.id("binfo-hint");
		if (hint) hint.textContent = tx("web.binfo.hint", "这个浏览器能读到的本机信息。屏幕和离开检测要点一下才会询问权限。");
		var refresh = F.id("binfo-refresh");
		if (refresh) refresh.textContent = tx("web.binfo.refresh", "刷新");
		var scr = F.id("binfo-screens");
		if (scr) scr.textContent = tx("web.binfo.screens", "读取屏幕");
		var idle = F.id("binfo-idle");
		if (idle) idle.textContent = tx("web.binfo.idle", "离开检测");
	}

	function yn(v){
		return v ? tx("web.binfo.yes", "是") : tx("web.binfo.no", "否");
	}

	function mb(n){
		if (n == null || !isFinite(n)) return "";
		return (n / 1048576).toFixed(1) + " MB";
	}

	function put(key, val){
		info[key] = val == null || val === "" ? "—" : String(val);
		render();
	}

	function render(){
		var el = F.id("box-out");
		if (!el || SK.curtool !== "u-binfo") return;
		var lines = [], i, key;
		for (i = 0; i < ORDER.length; i++) {
			key = ORDER[i];
			if (!Object.prototype.hasOwnProperty.call(info, key)) continue;
			lines.push(tx("web.binfo." + (key === "screens" ? "screensout" : key === "idle" ? "idleout" : key), key) + "\t" + info[key]);
		}
		el.value = lines.join("\n");
	}

	function collect(){
		put("ua", navigator.userAgent || "");
		put("cores", navigator.hardwareConcurrency || "");
		put("ram", navigator.deviceMemory ? navigator.deviceMemory + " GB" : tx("web.binfo.need", "这个浏览器没有这项"));
		put("dpr", window.devicePixelRatio || "");
		put("touch", navigator.maxTouchPoints || 0);
		put("lang", navigator.language || "");
		put("pdf", yn(!!navigator.pdfViewerEnabled));
		var s = window.screen || {};
		put("screen", (s.width || 0) + "x" + (s.height || 0)
			+ "  " + (s.availWidth || 0) + "x" + (s.availHeight || 0)
			+ (s.isExtended ? "  extended" : "")
			+ (s.colorDepth ? "  " + s.colorDepth + "bit" : ""));
		var mem = performance && performance.memory;
		if (mem) put("heap", mb(mem.usedJSHeapSize) + " / " + mb(mem.totalJSHeapSize) + " / " + mb(mem.jsHeapSizeLimit));
		else put("heap", tx("web.binfo.need", "这个浏览器没有这项"));
		var net = navigator.onLine ? tx("web.binfo.yes", "是") : tx("web.binfo.no", "否");
		var conn = navigator.connection;
		if (conn) {
			if (conn.effectiveType) net += "  " + conn.effectiveType;
			if (conn.downlink != null) net += "  " + conn.downlink + "Mb/s";
			if (conn.rtt != null) net += "  " + conn.rtt + "ms";
			if (conn.saveData) net += "  saveData";
		}
		put("net", net);
		var uad = navigator.userAgentData;
		if (uad) {
			var brands = uad.brands || uad.uaList || [];
			var parts = [], i, b;
			for (i = 0; i < brands.length; i++) {
				b = brands[i];
				if (b && b.brand) parts.push(b.brand + " " + (b.version || ""));
			}
			put("brand", parts.join(", "));
			put("platform", (uad.platform || "") + (uad.mobile ? "  mobile" : ""));
			if (uad.getHighEntropyValues) {
				uad.getHighEntropyValues(["architecture", "bitness", "platformVersion", "fullVersionList", "wow64", "model"]).then(function(hi){
					if (SK.curtool !== "u-binfo" || !hi) return;
					put("arch", (hi.architecture || "") + (hi.bitness ? " " + hi.bitness : "") + (hi.wow64 ? " wow64" : ""));
					if (hi.platformVersion) put("platform", (hi.platform || uad.platform || "") + " " + hi.platformVersion + (uad.mobile ? "  mobile" : ""));
					var list = hi.fullVersionList || [];
					var full = [], j, item;
					for (j = 0; j < list.length; j++) {
						item = list[j];
						if (item && item.brand) full.push(item.brand + " " + (item.version || ""));
					}
					if (full.length) put("brand", full.join(", "));
				}, function(){});
			}
			else put("arch", tx("web.binfo.need", "这个浏览器没有这项"));
		}
		else {
			put("brand", tx("web.binfo.need", "这个浏览器没有这项"));
			put("platform", navigator.platform || "");
			put("arch", tx("web.binfo.need", "这个浏览器没有这项"));
		}
		if (navigator.storage && navigator.storage.estimate) {
			navigator.storage.estimate().then(function(est){
				if (SK.curtool !== "u-binfo" || !est) return;
				put("storage", mb(est.usage) + " / " + mb(est.quota));
			}, function(){
				put("storage", tx("web.binfo.need", "这个浏览器没有这项"));
			});
		}
		else put("storage", tx("web.binfo.need", "这个浏览器没有这项"));
		if (navigator.keyboard && navigator.keyboard.getLayoutMap) {
			navigator.keyboard.getLayoutMap().then(function(map){
				if (SK.curtool !== "u-binfo" || !map) return;
				var letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
				var i, ch, got, line = "";
				for (i = 0; i < letters.length; i++) {
					ch = letters.charAt(i);
					got = map.get("Key" + ch);
					if (!got) continue;
					if (line) line += " ";
					line += got;
				}
				put("layout", line || tx("web.binfo.need", "这个浏览器没有这项"));
			}, function(){
				put("layout", tx("web.binfo.need", "这个浏览器没有这项"));
			});
		}
		else put("layout", tx("web.binfo.need", "这个浏览器没有这项"));
	}

	function pressureName(state){
		return tx("web.binfo.p." + state, state || "");
	}

	function stoppressure(){
		if (!po) return;
		try { po.disconnect(); }
		catch (ex) {}
		po = null;
	}

	function startpressure(){
		stoppressure();
		if (!window.PressureObserver) {
			put("pressure", tx("web.binfo.need", "这个浏览器没有这项"));
			return;
		}
		try {
			po = new PressureObserver(function(records){
				if (SK.curtool !== "u-binfo") { stoppressure(); return; }
				var r = records && records.length ? records[records.length - 1] : null;
				if (!r) return;
				put("pressure", pressureName(r.state) + (r.source ? " (" + r.source + ")" : ""));
			});
			Promise.resolve(po.observe("cpu")).then(function(){}, function(){
				put("pressure", tx("web.binfo.need", "这个浏览器没有这项"));
			});
		}
		catch (ex) {
			put("pressure", tx("web.binfo.need", "这个浏览器没有这项"));
		}
	}

	function readscreens(){
		if (!window.getScreenDetails) {
			put("screens", tx("web.binfo.need", "这个浏览器没有这项"));
			return;
		}
		window.getScreenDetails().then(function(d){
			if (SK.curtool !== "u-binfo" || !d) return;
			screens = d;
			showscreens();
			if (!screensHooked && d.addEventListener) {
				screensHooked = true;
				d.addEventListener("screenschange", function(){
					if (SK.curtool !== "u-binfo") return;
					showscreens();
				});
			}
		}, function(){
			put("screens", tx("web.binfo.denied", "没有允许"));
		});
	}

	function showscreens(){
		if (!screens || !screens.screens) return;
		var list = screens.screens;
		var lines = [], i, s;
		for (i = 0; i < list.length; i++) {
			s = list[i];
			lines.push((i + 1) + " " + s.width + "x" + s.height + " @" + s.left + "," + s.top
				+ (s.isPrimary ? " primary" : "")
				+ (s.isInternal ? " internal" : ""));
		}
		put("screens", lines.join(" | "));
	}

	function startidle(){
		if (idleAbort) idleAbort.abort();
		idleAbort = null;
		if (!window.IdleDetector) {
			put("idle", tx("web.binfo.need", "这个浏览器没有这项"));
			return;
		}
		idleAbort = new AbortController();
		var det = new IdleDetector();
		var signal = idleAbort.signal;
		det.addEventListener("change", function(){
			if (SK.curtool !== "u-binfo") return;
			put("idle", (det.userState || "") + " / " + (det.screenState || ""));
		});
		var ask = IdleDetector.requestPermission ? IdleDetector.requestPermission() : Promise.resolve("granted");
		ask.then(function(state){
			if (state !== "granted") throw new Error("denied");
			return det.start({ threshold: 60000, signal: signal });
		}).then(function(){
			if (SK.curtool !== "u-binfo") return;
			put("idle", (det.userState || "") + " / " + (det.screenState || ""));
		}, function(err){
			if (err && err.name === "AbortError") return;
			put("idle", err && err.message === "denied" ? tx("web.binfo.denied", "没有允许") : tx("web.binfo.need", "这个浏览器没有这项"));
		});
	}
})();
