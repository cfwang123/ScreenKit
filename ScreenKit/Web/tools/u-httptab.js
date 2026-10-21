(function(){
	// [代码, 原因短语, 中文名, 规范, 是否标准(1/0), 默认缓存, 说明, 备注(可选)]
	// 标准码按 RFC 9110 与 IANA 注册表；非标准码收常见服务器、代理和框架的实现扩展。
	var ROWS = [
		[100, "Continue", "继续", "RFC 9110", 1, "—",
			"服务器已收到请求头，客户端可以继续发送请求体。", "配合 Expect: 100-continue 使用，避免先发大请求体。"],
		[101, "Switching Protocols", "切换协议", "RFC 9110", 1, "—",
			"服务器同意切换协议，后续按 Upgrade 头里的新协议通信。", "WebSocket 握手成功时返回 101。"],
		[102, "Processing", "处理中", "RFC 2518", 1, "—",
			"WebDAV 长请求正在处理，服务器先回一个中间响应，避免客户端超时。", "不是最终响应，客户端应继续等待。"],
		[103, "Early Hints", "早期提示", "RFC 8297", 1, "—",
			"最终响应前先发 Link 头，浏览器可提前预加载样式、脚本等资源。", "常见于配合 200 使用。"],
		[200, "OK", "成功", "RFC 9110", 1, "可缓存",
			"请求成功，响应体按请求的方法返回内容。"],
		[201, "Created", "已创建", "RFC 9110", 1, "—",
			"请求成功并新建了资源，Location 头应指向新资源。", "常用于 POST 创建和 PUT 新建。"],
		[202, "Accepted", "已接受", "RFC 9110", 1, "—",
			"请求已接收，但还没有处理完，适合异步任务。", "响应体可说明进度查询地址。"],
		[203, "Non-Authoritative Information", "非授权信息", "RFC 9110", 1, "可缓存",
			"返回的是经过转换的副本，不是源服务器的原始内容。", "代理改写响应内容时使用。"],
		[204, "No Content", "无内容", "RFC 9110", 1, "可缓存",
			"请求成功但没有响应体。", "常用于 PUT、DELETE，或轮询时没有更新。"],
		[205, "Reset Content", "重置内容", "RFC 9110", 1, "—",
			"要求客户端重置表单或视图，响应体为空。", "实际使用很少，浏览器处理也不完全一致。"],
		[206, "Partial Content", "部分内容", "RFC 9110", 1, "可缓存",
			"Range 请求成功，只返回请求的字节区间。", "响应带 Content-Range，用于断点续传和视频拖动。"],
		[207, "Multi-Status", "多状态", "RFC 4918", 1, "—",
			"WebDAV 批量操作，响应体逐个说明各子请求的结果。"],
		[208, "Already Reported", "已报告", "RFC 5842", 1, "—",
			"WebDAV 同一集合成员已在 207 里报告过，避免重复返回。"],
		[226, "IM Used", "IM 已用", "RFC 3229", 1, "—",
			"服务器对资源应用了实例操作（Delta 编码），返回增量结果。"],
		[300, "Multiple Choices", "多种选择", "RFC 9110", 1, "可缓存",
			"同一资源有多个可选表示，响应可列出选项让客户端选择。"],
		[301, "Moved Permanently", "永久移动", "RFC 9110", 1, "可缓存",
			"资源永久换到新地址，浏览器和搜索引擎会记住。", "POST 跟随 301 时常被改成 GET；要保留方法用 308。"],
		[302, "Found", "找到", "RFC 9110", 1, "显式可缓存",
			"资源临时换到新地址。", "历史上 POST 常被改成 GET；要保留方法用 307。"],
		[303, "See Other", "参见其他", "RFC 9110", 1, "—",
			"让客户端改用 GET 访问 Location，常用于 POST 后跳转到结果页。"],
		[304, "Not Modified", "未修改", "RFC 9110", 1, "复用缓存",
			"缓存仍然有效，响应没有正文。", "请求需带 If-None-Match 或 If-Modified-Since。"],
		[305, "Use Proxy", "使用代理", "RFC 9110", 1, "—",
			"必须通过指定代理访问资源。", "已废弃，不要在新实现里使用。"],
		[307, "Temporary Redirect", "临时重定向", "RFC 9110", 1, "显式可缓存",
			"临时换地址，并保持原请求方法和请求体。", "适合 POST、PUT 的临时跳转。"],
		[308, "Permanent Redirect", "永久重定向", "RFC 9110", 1, "可缓存",
			"永久换地址，并保持原请求方法和请求体。", "和 301 类似，但不会把 POST 改成 GET。"],
		[400, "Bad Request", "请求错误", "RFC 9110", 1, "—",
			"请求语法或参数不合法，服务器无法或拒绝处理。"],
		[401, "Unauthorized", "未认证", "RFC 9110", 1, "—",
			"缺少或无效的身份凭证，响应应带 WWW-Authenticate。", "名字叫 Unauthorized，实际语义是未认证。"],
		[402, "Payment Required", "需要付款", "RFC 9110", 1, "—",
			"保留状态码，实际使用很少。"],
		[403, "Forbidden", "禁止访问", "RFC 9110", 1, "—",
			"身份已认证，但没有权限访问该资源。"],
		[404, "Not Found", "未找到", "RFC 9110", 1, "可缓存",
			"资源不存在，或服务器不想暴露资源是否存在。"],
		[405, "Method Not Allowed", "方法不允许", "RFC 9110", 1, "可缓存",
			"资源不支持该 HTTP 方法，响应应带 Allow 头。", "路径存在但方法不对时使用。"],
		[406, "Not Acceptable", "不可接受", "RFC 9110", 1, "—",
			"无法按 Accept 头产出客户端想要的格式。"],
		[407, "Proxy Authentication Required", "需要代理认证", "RFC 9110", 1, "—",
			"类似 401，但认证对象是代理，响应带 Proxy-Authenticate。"],
		[408, "Request Timeout", "请求超时", "RFC 9110", 1, "—",
			"服务器等待请求体或连接超时，可以关闭连接。"],
		[409, "Conflict", "冲突", "RFC 9110", 1, "—",
			"请求与资源当前状态冲突，如版本冲突、重复创建。"],
		[410, "Gone", "已删除", "RFC 9110", 1, "可缓存",
			"资源曾经存在，且已永久移除。", "比 404 更明确，告诉客户端不用再试。"],
		[411, "Length Required", "需要长度", "RFC 9110", 1, "—",
			"请求必须带 Content-Length 头。"],
		[412, "Precondition Failed", "前置条件失败", "RFC 9110", 1, "—",
			"If-Match、If-Unmodified-Since 等前置条件不满足。"],
		[413, "Content Too Large", "内容过大", "RFC 9110", 1, "—",
			"请求体超过服务器限制。", "旧称 Payload Too Large，很多服务器仍显示旧名。"],
		[414, "URI Too Long", "URI 过长", "RFC 9110", 1, "可缓存",
			"请求目标 URL 太长，服务器拒绝处理。"],
		[415, "Unsupported Media Type", "不支持的媒体类型", "RFC 9110", 1, "—",
			"请求的 Content-Type 不被服务器支持。"],
		[416, "Range Not Satisfiable", "范围无法满足", "RFC 9110", 1, "—",
			"Range 请求超出资源长度。", "响应带 Content-Range: bytes */资源长度。"],
		[417, "Expectation Failed", "期望失败", "RFC 9110", 1, "—",
			"无法满足 Expect 请求头里的期望。"],
		[418, "I'm a teapot", "我是茶壶", "RFC 2324", 0, "—",
			"愚人节 RFC 定义的玩笑代码，常被当作彩蛋或占位。", "IANA 现在把它标为 Unused。"],
		[421, "Misdirected Request", "请求被误导", "RFC 9110", 1, "—",
			"连接被复用到了不匹配的服务器，常见于 HTTP/2 连接合并和 SNI 不一致。"],
		[422, "Unprocessable Content", "无法处理的内容", "RFC 9110", 1, "—",
			"语法正确但语义有错，如表单校验失败。", "旧称 Unprocessable Entity，WebDAV 和 Rails 常用。"],
		[423, "Locked", "已锁定", "RFC 4918", 1, "—",
			"WebDAV 资源被锁定，当前请求无法修改。"],
		[424, "Failed Dependency", "依赖失败", "RFC 4918", 1, "—",
			"WebDAV 因为前一个请求失败而失败。"],
		[425, "Too Early", "太早", "RFC 8470", 1, "—",
			"服务器担心 TLS 早数据被重放，拒绝过早的请求。", "客户端稍后重试即可。"],
		[426, "Upgrade Required", "需要升级", "RFC 9110", 1, "—",
			"客户端需要切换协议，响应带 Upgrade 头。"],
		[428, "Precondition Required", "需要前置条件", "RFC 6585", 1, "—",
			"服务器要求条件请求，防止丢失更新。", "常用于要求 If-Match 的修改接口。"],
		[429, "Too Many Requests", "请求过多", "RFC 6585", 1, "—",
			"触发了限流。", "响应可带 Retry-After 告诉客户端多久后重试。"],
		[431, "Request Header Fields Too Large", "请求头过大", "RFC 6585", 1, "—",
			"单个或全部请求头超过服务器限制。", "常见于 Cookie 或 Authorization 太长。"],
		[451, "Unavailable For Legal Reasons", "因法律原因不可用", "RFC 7725", 1, "—",
			"因法律要求无法提供该资源。", "响应可说明法律依据或申诉渠道。"],
		[500, "Internal Server Error", "服务器内部错误", "RFC 9110", 1, "—",
			"服务器遇到未预期异常，无法完成请求。", "不要把堆栈和内部细节直接返回给用户。"],
		[501, "Not Implemented", "未实现", "RFC 9110", 1, "可缓存",
			"服务器不支持完成请求所需的功能或方法。"],
		[502, "Bad Gateway", "网关错误", "RFC 9110", 1, "—",
			"网关或代理从上游收到无效响应。"],
		[503, "Service Unavailable", "服务不可用", "RFC 9110", 1, "—",
			"服务器过载或维护中，暂时无法处理请求。", "可带 Retry-After 告诉客户端何时重试。"],
		[504, "Gateway Timeout", "网关超时", "RFC 9110", 1, "—",
			"网关或代理等待上游响应超时。"],
		[505, "HTTP Version Not Supported", "HTTP 版本不支持", "RFC 9110", 1, "—",
			"服务器不支持请求使用的 HTTP 版本。"],
		[506, "Variant Also Negotiates", "变体协商失败", "RFC 2295", 1, "—",
			"内容协商配置错误，导致选中的变体又要求继续协商。"],
		[507, "Insufficient Storage", "存储不足", "RFC 4918", 1, "—",
			"WebDAV 服务器空间不足，无法保存资源。"],
		[508, "Loop Detected", "检测到循环", "RFC 5842", 1, "—",
			"WebDAV 处理请求时发现无限循环。"],
		[510, "Not Extended", "未扩展", "RFC 2774", 1, "—",
			"需要额外的 HTTP 扩展才能处理请求，响应会说明所需扩展。"],
		[511, "Network Authentication Required", "需要网络认证", "RFC 6585", 1, "—",
			"需要先完成网络接入认证。", "常见于酒店、机场 WiFi 的登录页。"],
		[419, "Page Expired", "页面过期", "Laravel", 0, "—",
			"CSRF 令牌过期或页面过期。", "Laravel 等框架使用，不是标准状态码。"],
		[420, "Enhance Your Calm", "放轻松", "Twitter API", 0, "—",
			"旧版 Twitter API 的限流代码。", "很多实现现在改用 429。"],
		[444, "Connection Closed Without Response", "直接关闭连接", "nginx", 0, "—",
			"nginx 不返回任何响应就断开连接。", "常用于拒绝恶意请求，客户端只会看到连接被重置。"],
		[449, "Retry With", "重试", "Microsoft IIS", 0, "—",
			"要求客户端补充信息后重试。", "微软扩展，已废弃。"],
		[499, "Client Closed Request", "客户端已断开", "nginx", 0, "—",
			"客户端在服务器响应前主动关闭了连接。", "只出现在 nginx 访问日志里，不会发给客户端。"],
		[509, "Bandwidth Limit Exceeded", "超过带宽限制", "cPanel / Apache", 0, "—",
			"主机商的带宽限制被触发。", "常被当作 429 处理。"],
		[520, "Unknown Error", "未知错误", "Cloudflare", 0, "—",
			"源站返回了 Cloudflare 无法识别的响应。"],
		[521, "Web Server Is Down", "源站宕机", "Cloudflare", 0, "—",
			"Cloudflare 连不上源站，源站的 Web 服务可能没启动。"],
		[522, "Connection Timed Out", "连接超时", "Cloudflare", 0, "—",
			"Cloudflare 与源站建立 TCP 连接超时。"],
		[523, "Origin Is Unreachable", "源站不可达", "Cloudflare", 0, "—",
			"DNS 或路由问题导致 Cloudflare 找不到源站。"],
		[524, "A Timeout Occurred", "响应超时", "Cloudflare", 0, "—",
			"Cloudflare 已连上源站，但等响应等到超时。"],
		[525, "SSL Handshake Failed", "SSL 握手失败", "Cloudflare", 0, "—",
			"Cloudflare 与源站的 TLS 握手失败。"],
		[526, "Invalid SSL Certificate", "SSL 证书无效", "Cloudflare", 0, "—",
			"源站证书无效、过期或不受信任。"],
		[527, "Railgun Error", "Railgun 错误", "Cloudflare", 0, "—",
			"Railgun 加速链路出错。", "该功能已弃用。"],
		[530, "Origin DNS Error", "源站 DNS 错误", "Cloudflare", 0, "—",
			"解析源站域名失败，常见于源站域名写错或 DNS 记录缺失。"],
		[598, "Network Read Timeout Error", "网络读超时", "代理扩展", 0, "—",
			"代理服务器等待后端读数据超时。", "一些代理或反向代理自定义的非标准代码。"],
		[599, "Network Connect Timeout Error", "网络连接超时", "代理扩展", 0, "—",
			"代理服务器连接后端超时。", "一些代理或反向代理自定义的非标准代码。"]
	];
	var GROUPS = [
		{ id: "1", name: "1xx 信息响应" },
		{ id: "2", name: "2xx 成功" },
		{ id: "3", name: "3xx 重定向" },
		{ id: "4", name: "4xx 客户端错误" },
		{ id: "5", name: "5xx 服务端错误" },
		{ id: "x", name: "非标准 / 实现扩展" }
	];
	var cur = "all";
	var picked = 200;

	SK.regutil("u-httptab", {
		title: "HTTP状态码表",
		body: function(){
			return '<div class="reftab">'
				+ '<div class="uquery"><input id="h-q" type="text" placeholder="搜索：404、重定向、Not Found、RFC 6585">'
				+ '<span id="h-msg" class="msg"></span></div>'
				+ '<div class="refchips" id="h-chips"></div>'
				+ '<div class="refbody">'
				+ '<div class="refscroll"><table class="reflist hcodes" id="h-tab"></table></div>'
				+ '<aside class="refdetail" id="h-detail"></aside>'
				+ "</div></div>";
		},
		run: function(){
			throw new Error("没有这个工具");
		},
		open: function(){
			cur = "all";
			picked = 200;
			paintchips();
			F.id("h-q").oninput = paint;
			F.id("h-chips").onclick = onchip;
			F.id("h-tab").onclick = onrow;
			paint();
		},
		hideGo: true,
		live: false,
		hideOut: true
	});

	function groupof(row){
		return row[4] ? String(row[0]).charAt(0) : "x";
	}

	function groupname(id){
		var i;
		for (i = 0; i < GROUPS.length; i++) if (GROUPS[i].id === id) return GROUPS[i].name;
		return "";
	}

	function query(){
		return String(SK.val("h-q") || "").trim().toLowerCase();
	}

	function hit(row, q){
		if (!q) return true;
		// 纯数字只按代码匹配，避免描述里提到的「改用 429」把 420、509 也带出来。
		if (/^[0-9]+$/.test(q)) return String(row[0]).indexOf(q) === 0;
		var blob = String(row[0]) + " " + row[1] + " " + row[2] + " " + row[3] + " "
			+ row[5] + " " + row[6] + " " + (row[7] || "") + " " + groupname(groupof(row));
		return blob.toLowerCase().indexOf(q) >= 0;
	}

	function paintchips(){
		var html = ['<button type="button" data-g="all"' + (cur === "all" ? ' class="on"' : "") + ">全部<small>"
			+ ROWS.length + "</small></button>"];
		var i, j, n;
		for (i = 0; i < GROUPS.length; i++) {
			n = 0;
			for (j = 0; j < ROWS.length; j++) if (groupof(ROWS[j]) === GROUPS[i].id) n++;
			html.push('<button type="button" data-g="' + GROUPS[i].id + '"'
				+ (cur === GROUPS[i].id ? ' class="on"' : "") + ">" + GROUPS[i].name
				+ "<small>" + n + "</small></button>");
		}
		F.id("h-chips").innerHTML = html.join("");
	}

	function paint(){
		var q = query();
		var html = ["<thead><tr><th>代码</th><th>原因短语</th><th>中文</th><th>规范 / 来源</th></tr></thead><tbody>"];
		var total = 0;
		var i, j, rows, n;
		for (i = 0; i < GROUPS.length; i++) {
			if (cur !== "all" && cur !== GROUPS[i].id) continue;
			rows = [];
			for (j = 0; j < ROWS.length; j++) {
				if (groupof(ROWS[j]) === GROUPS[i].id && hit(ROWS[j], q)) rows.push(ROWS[j]);
			}
			if (!rows.length) continue;
			total += rows.length;
			html.push('<tr class="refgh"><td colspan="4">' + GROUPS[i].name
				+ '<span class="n">' + rows.length + "</span></td></tr>");
			for (n = 0; n < rows.length; n++) html.push(rowhtml(rows[n]));
		}
		html.push("</tbody>");
		if (!total) {
			html = ['<tbody><tr><td class="refnone">没有匹配的状态码</td></tr></tbody>'];
		}
		F.id("h-tab").innerHTML = html.join("");
		SK.msg("h-msg", total ? (q ? total + " 条" : "共 " + total + " 条") : "");
		if (total) {
			var sel = rowby(picked);
			if (!visible(sel, q)) sel = firstvisible(q);
			showdetail(sel || ROWS[0]);
		}
		else F.id("h-detail").innerHTML = "";
	}

	function visible(row, q){
		if (!row) return false;
		if (cur !== "all" && cur !== groupof(row)) return false;
		return hit(row, q);
	}

	function firstvisible(q){
		var i, j;
		for (i = 0; i < GROUPS.length; i++) {
			if (cur !== "all" && cur !== GROUPS[i].id) continue;
			for (j = 0; j < ROWS.length; j++) {
				if (groupof(ROWS[j]) === GROUPS[i].id && hit(ROWS[j], q)) return ROWS[j];
			}
		}
		return null;
	}

	function rowby(code){
		var i;
		for (i = 0; i < ROWS.length; i++) if (ROWS[i][0] === code) return ROWS[i];
		return null;
	}

	function rowhtml(row){
		var g = groupof(row);
		return '<tr data-code="' + row[0] + '" title="点击复制 ' + esc(row[0] + " " + row[1]) + '"'
			+ (row[0] === picked ? ' class="on"' : "") + ">"
			+ '<td class="hcode c' + g + '">' + row[0] + "</td>"
			+ "<td>" + esc(row[1]) + "</td>"
			+ "<td>" + esc(row[2]) + "</td>"
			+ '<td class="hspec">' + esc(row[3]) + "</td></tr>";
	}

	function showdetail(row){
		if (!row) return;
		var g = groupof(row);
		var meta = [
			["类别", groupname(g) + (row[4] ? "" : "（非标准）")],
			["规范", row[3]],
			["默认缓存", row[5]],
			["标准", row[4] ? "是，IANA 注册" : "否，实现扩展"],
			["说明", row[6]]
		];
		if (row[7]) meta.push(["备注", row[7]]);
		var html = '<div class="refbig c' + g + '">' + row[0] + "</div>"
			+ '<div class="refsub">' + esc(row[1]) + "</div>"
			+ '<div class="refcn">' + esc(row[2]) + "</div>"
			+ '<dl class="refmeta">';
		var i;
		for (i = 0; i < meta.length; i++)
			html += "<dt>" + meta[i][0] + "</dt><dd>" + esc(meta[i][1]) + "</dd>";
		html += "</dl>";
		F.id("h-detail").innerHTML = html;
	}

	function onchip(ev){
		var el = ev.target;
		while (el && el !== this) {
			if (el.getAttribute && F.attr(el, "data-g")) {
				cur = F.attr(el, "data-g");
				paintchips();
				paint();
				return;
			}
			el = el.parentNode;
		}
	}

	function onrow(ev){
		var el = ev.target;
		while (el && el.tagName !== "TR") el = el.parentNode;
		if (!el || !F.attr(el, "data-code")) return;
		var code = parseInt(F.attr(el, "data-code"), 10);
		var row = rowby(code);
		if (!row) return;
		picked = code;
		paint();
		showdetail(row);
		SK.copytext(row[0] + " " + row[1], "h-msg");
	}

	function esc(s){
		return String(s == null ? "" : s)
			.replace(/&/g, "&amp;")
			.replace(/</g, "&lt;")
			.replace(/>/g, "&gt;")
			.replace(/"/g, "&quot;");
	}
})();
