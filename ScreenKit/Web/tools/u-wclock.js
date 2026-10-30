(function(){
	var COMMON = [
		{ id: "", key: "web.wclock.local" },
		{ id: "Asia/Shanghai", key: "web.wclock.c.sh" },
		{ id: "Asia/Hong_Kong", key: "web.wclock.c.hk" },
		{ id: "Asia/Taipei", key: "web.wclock.c.tp" },
		{ id: "Asia/Tokyo", key: "web.wclock.c.tyo" },
		{ id: "Asia/Seoul", key: "web.wclock.c.sel" },
		{ id: "Asia/Singapore", key: "web.wclock.c.sg" },
		{ id: "Asia/Bangkok", key: "web.wclock.c.bkk" },
		{ id: "Asia/Dubai", key: "web.wclock.c.dxb" },
		{ id: "Europe/Moscow", key: "web.wclock.c.mow" },
		{ id: "Europe/Paris", key: "web.wclock.c.par" },
		{ id: "Europe/London", key: "web.wclock.c.lon" },
		{ id: "America/New_York", key: "web.wclock.c.nyc" },
		{ id: "America/Los_Angeles", key: "web.wclock.c.lax" },
		{ id: "Australia/Sydney", key: "web.wclock.c.syd" },
		{ id: "Etc/UTC", key: "web.wclock.c.utc" }
	];
	var extra = "";
	var shownLang = "";

	SK.regutil("u-wclock", {
		title: "世界时钟",
		hideGo: true,
		hideCopy: true,
		hideOut: true,
		body(){
			return '<div class="wclock">'
				+ '<div class="row wclock-bar">'
				+ "<label><span data-k=\"web.wclock.find\">筛选地区</span>"
				+ '<input id="wclock-q" type="text"></label>'
				+ "<label><span data-k=\"web.wclock.pick\">再看一个地区</span>"
				+ '<select id="wclock-pick"></select></label>'
				+ "</div>"
				+ '<div id="wclock-one"></div>'
				+ '<div class="wclock-h" data-k="web.wclock.common">常用地区</div>'
				+ '<div id="wclock-grid" class="wclock-grid"></div>'
				+ "</div>";
		},
		open(){
			extra = "";
			shownLang = "";
			var title = F.id("box-title");
			if (title) {
				F.seta(title, "data-k", "web.tool.wclock");
				title.textContent = say("web.tool.wclock", "世界时钟");
			}
			F.id("wclock-q").oninput = function(){
				fillpick();
			};
			F.id("wclock-pick").onchange = function(){
				extra = F.id("wclock-pick").value || "";
				paintone();
				markon();
			};
			F.id("wclock-grid").onclick = function(ev){
				var node = ev.target;
				while (node && node.id !== "wclock-grid") {
					if (node.getAttribute && node.getAttribute("data-zone")) {
						var id = node.getAttribute("data-zone");
						if (!id) return;
						extra = id;
						fillpick();
						paintone();
						markon();
						return;
					}
					node = node.parentNode;
				}
			};
			ensurelang();
			paint();
			SK.stoptick();
			SK.tick = setInterval(paint, 1000);
		}
	});

	SK.wclockParts = function(zone, date, loc){
		return {
			time: clocktext(zone, date, loc),
			off: offsettext(zone, date)
		};
	};

	function say(key, fallback){
		var s = SK.t && SK.t(key);
		return s || fallback;
	}

	function uilang(){
		var s = document.documentElement.lang || "zh-CN";
		if (s === "zh") return "zh-CN";
		return s;
	}

	function esc(s){
		return String(s == null ? "" : s)
			.replace(/&/g, "&amp;")
			.replace(/</g, "&lt;")
			.replace(/>/g, "&gt;")
			.replace(/"/g, "&quot;");
	}

	function zones(){
		if (Intl.supportedValuesOf) {
			try { return Intl.supportedValuesOf("timeZone"); }
			catch (e) {}
		}
		var list = [];
		var i;
		for (i = 0; i < COMMON.length; i++) if (COMMON[i].id) list.push(COMMON[i].id);
		return list;
	}

	function zonelabel(id, loc){
		try {
			var name = new Intl.DisplayNames([loc], { type: "timeZone" }).of(id);
			if (name && name !== id) return name;
		}
		catch (e) {}
		return String(id || "").replace(/_/g, " ");
	}

	function cityname(item){
		if (item.key) {
			var s = say(item.key, "");
			if (s) return s;
		}
		if (!item.id) return say("web.wclock.local", "本机");
		return zonelabel(item.id, uilang());
	}

	function iscommon(id){
		var i;
		for (i = 0; i < COMMON.length; i++) if (COMMON[i].id === id) return true;
		return false;
	}

	function clocktext(zone, date, loc){
		var opt = { hour: "2-digit", minute: "2-digit", second: "2-digit", hourCycle: "h23" };
		if (zone) opt.timeZone = zone;
		return new Intl.DateTimeFormat(loc || "zh-CN", opt).format(date);
	}

	function datetext(zone, date, loc){
		var opt = { weekday: "short", month: "short", day: "numeric" };
		if (zone) opt.timeZone = zone;
		return new Intl.DateTimeFormat(loc || "zh-CN", opt).format(date);
	}

	function hourof(zone, date){
		var opt = { hour: "2-digit", hourCycle: "h23" };
		if (zone) opt.timeZone = zone;
		var s = new Intl.DateTimeFormat("en-GB", opt).format(date);
		var h = parseInt(s, 10);
		if (h === 24) h = 0;
		return h;
	}

	function offsettext(zone, date){
		try {
			var opt = { timeZoneName: "shortOffset", hour: "2-digit" };
			if (zone) opt.timeZone = zone;
			var parts = new Intl.DateTimeFormat("en-US", opt).formatToParts(date);
			var i, name = "";
			for (i = 0; i < parts.length; i++) {
				if (parts[i].type === "timeZoneName") name = parts[i].value;
			}
			if (name) {
				name = name.replace("GMT", "UTC");
				if (name === "UTC") return "UTC+0";
				return name;
			}
		}
		catch (e) {}
		return offsetfallback(zone, date);
	}

	function offsetfallback(zone, date){
		var opt = {
			hourCycle: "h23", year: "numeric", month: "2-digit", day: "2-digit",
			hour: "2-digit", minute: "2-digit", second: "2-digit"
		};
		if (zone) opt.timeZone = zone;
		var parts = new Intl.DateTimeFormat("en-US", opt).formatToParts(date);
		var p = {};
		var i;
		for (i = 0; i < parts.length; i++) p[parts[i].type] = parts[i].value;
		var asUtc = Date.UTC(+p.year, +p.month - 1, +p.day, +p.hour % 24, +p.minute, +p.second);
		var min = Math.round((asUtc - date.getTime()) / 60000);
		var sign = min < 0 ? "-" : "+";
		var a = Math.abs(min);
		var h = Math.floor(a / 60);
		var m = a % 60;
		if (m === 0) return "UTC" + sign + h;
		return "UTC" + sign + h + ":" + (m < 10 ? "0" : "") + m;
	}

	function ensurelang(){
		var loc = uilang();
		if (loc === shownLang) return;
		shownLang = loc;
		fillpick();
		buildgrid();
		paintone();
	}

	function fillpick(){
		var sel = F.id("wclock-pick");
		if (!sel) return;
		var q = (F.id("wclock-q").value || "").trim().toLowerCase();
		var loc = uilang();
		var list = zones();
		var rows = [];
		var i, id, label;
		for (i = 0; i < list.length; i++) {
			id = list[i];
			label = zonelabel(id, loc);
			if (q && label.toLowerCase().indexOf(q) < 0 && id.toLowerCase().indexOf(q) < 0) {
				if (id !== extra) continue;
			}
			rows.push({ id: id, label: label });
		}
		rows.sort(function(a, b){
			return a.label.localeCompare(b.label, loc);
		});
		var html = '<option value="">' + esc(say("web.wclock.none", "不额外显示")) + "</option>";
		for (i = 0; i < rows.length; i++) {
			html += '<option value="' + esc(rows[i].id) + '"'
				+ (rows[i].id === extra ? " selected" : "") + ">"
				+ esc(rows[i].label) + "</option>";
		}
		sel.innerHTML = html;
	}

	function cardhtml(id, name){
		return '<div class="wclock-card" data-zone="' + esc(id) + '">'
			+ '<div class="wclock-name">' + esc(name) + "</div>"
			+ '<b class="wclock-time">00:00:00</b>'
			+ '<div class="wclock-sub"><span class="wclock-date"></span>'
			+ ' <span class="wclock-off"></span></div>'
			+ "</div>";
	}

	function buildgrid(){
		var grid = F.id("wclock-grid");
		if (!grid) return;
		var html = "";
		var i;
		for (i = 0; i < COMMON.length; i++)
			html += cardhtml(COMMON[i].id, cityname(COMMON[i]));
		grid.innerHTML = html;
		markon();
	}

	function paintcard(card, zone, date, loc){
		if (!card) return;
		var time = card.querySelector(".wclock-time");
		var day = card.querySelector(".wclock-date");
		var off = card.querySelector(".wclock-off");
		if (time) time.textContent = clocktext(zone, date, loc);
		if (day) day.textContent = datetext(zone, date, loc);
		if (off) off.textContent = offsettext(zone, date);
		var h = hourof(zone, date);
		card.classList.toggle("night", h < 6 || h >= 18);
	}

	function paintone(){
		var box = F.id("wclock-one");
		if (!box) return;
		if (!extra || iscommon(extra)) {
			box.innerHTML = "";
			box.hidden = true;
			return;
		}
		box.hidden = false;
		if (!box.firstChild || box.firstChild.getAttribute("data-zone") !== extra)
			box.innerHTML = cardhtml(extra, zonelabel(extra, uilang()));
		paintcard(box.firstChild, extra, new Date(), uilang());
	}

	function markon(){
		var grid = F.id("wclock-grid");
		if (!grid) return;
		var cards = grid.querySelectorAll(".wclock-card");
		var i;
		for (i = 0; i < cards.length; i++)
			cards[i].classList.toggle("on", extra !== "" && cards[i].getAttribute("data-zone") === extra);
	}

	function paint(){
		ensurelang();
		var date = new Date();
		var loc = uilang();
		var grid = F.id("wclock-grid");
		if (grid) {
			var cards = grid.querySelectorAll(".wclock-card");
			var i, zone;
			for (i = 0; i < cards.length; i++) {
				zone = cards[i].getAttribute("data-zone") || "";
				paintcard(cards[i], zone, date, loc);
			}
		}
		if (extra && !iscommon(extra)) paintcard(F.id("wclock-one").firstChild, extra, date, loc);
	}
})();
