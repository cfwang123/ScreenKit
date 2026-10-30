(function(){
	// [MIME 类型, 扩展名, 类别, 说明, 备注(可选)]
	// 类别：text 文本、image 图片、audio 音频、video 视频、font 字体、
	// app 应用、archive 压缩包、model 3D 模型、message 消息。
	var ROWS = [
		["text/plain", ".txt .text .log .ini", "text", "纯文本。最通用的文本类型，字符集用 charset 参数说明。"],
		["text/html", ".html .htm .shtml", "text", "HTML 页面。浏览器按 HTML 解析，现代页面用 UTF-8。"],
		["text/css", ".css", "text", "层叠样式表。字符集可用 @charset 或 HTTP 头指定。"],
		["text/javascript", ".js .mjs .cjs", "text", "JavaScript。RFC 9239 规定的标准类型。"],
		["application/javascript", ".js .mjs", "text", "JavaScript 的旧写法。", "标准类型是 text/javascript，新代码优先用它。"],
		["text/markdown", ".md .markdown", "text", "Markdown 文档。浏览器通常当文本显示或下载。"],
		["text/csv", ".csv", "text", "逗号分隔值。表格数据，注意字符集和分隔符。"],
		["text/tab-separated-values", ".tsv", "text", "制表符分隔值。和 CSV 类似。"],
		["text/xml", ".xml", "text", "XML 文档的旧写法。", "推荐用 application/xml。"],
		["text/calendar", ".ics", "text", "iCalendar 日历。邮件和日历应用可识别。"],
		["text/vcard", ".vcf", "text", "vCard 电子名片。联系人导出常用。"],
		["text/rtf", ".rtf", "text", "RTF 富文本。", "application/rtf 也常用。"],
		["text/event-stream", "", "text", "Server-Sent Events 服务器推送。响应要一直保持打开。"],
		["text/uri-list", ".uri .uris", "text", "URI 列表。每行一个 URI。"],
		["text/x-component", ".htc", "text", "IE 行为脚本。", "旧 IE 专用，已经过时。"],

		["image/png", ".png", "image", "PNG 图片。无损压缩，支持透明。"],
		["image/jpeg", ".jpg .jpeg .jpe", "image", "JPEG 图片。有损压缩，照片最常用。"],
		["image/gif", ".gif", "image", "GIF 图片。支持简单动画和 1 位透明。"],
		["image/webp", ".webp", "image", "WebP 图片。支持有损、无损和透明，体积小。"],
		["image/avif", ".avif", "image", "AVIF 图片。基于 AV1，压缩率高。", "旧浏览器可能不支持。"],
		["image/svg+xml", ".svg", "image", "SVG 矢量图。XML 文本，可缩放。", "SVG 可以包含脚本，嵌入网页时注意 XSS。"],
		["image/bmp", ".bmp", "image", "BMP 位图。未压缩，体积大。"],
		["image/x-icon", ".ico", "image", "Windows 图标。", "标准类型是 image/vnd.microsoft.icon。"],
		["image/vnd.microsoft.icon", ".ico", "image", "ICO 图标的标准类型。"],
		["image/tiff", ".tif .tiff", "image", "TIFF 图片。印刷和扫描常用。", "浏览器通常不直接显示。"],
		["image/apng", ".apng .png", "image", "APNG 动态 PNG。支持透明。"],
		["image/jxl", ".jxl", "image", "JPEG XL 图片。新格式，兼容性有限。"],
		["image/heic", ".heic", "image", "HEIC 图片。苹果设备常用。", "浏览器支持少。"],
		["image/heif", ".heif", "image", "HEIF 图片。高效图像容器。"],
		["image/vnd.adobe.photoshop", ".psd", "image", "Photoshop 工程文件。", "浏览器通常直接下载。"],

		["audio/mpeg", ".mp3 .mpga", "audio", "MP3 音频。最通用的有损音频。"],
		["audio/ogg", ".ogg .oga .opus", "audio", "Ogg 音频。通常装 Vorbis 或 Opus。"],
		["audio/wav", ".wav", "audio", "WAV 音频。未压缩 PCM，体积大。", "audio/x-wav 也常见。"],
		["audio/webm", ".weba .webm", "audio", "WebM 音频。通常装 Opus 或 Vorbis。"],
		["audio/mp4", ".m4a .mp4a", "audio", "MP4 容器里的音频。常用 AAC 编码。"],
		["audio/aac", ".aac", "audio", "AAC 音频。原始 AAC 流。"],
		["audio/flac", ".flac", "audio", "FLAC 音频。无损压缩。"],
		["audio/opus", ".opus", "audio", "Opus 音频。低延迟、低码率，WebRTC 常用。"],
		["audio/midi", ".mid .midi", "audio", "MIDI。记录乐器指令，不是波形。"],
		["audio/x-midi", ".mid .midi", "audio", "MIDI 的旧写法。", "和 audio/midi 等价。"],
		["audio/aiff", ".aif .aiff", "audio", "AIFF 音频。苹果的无损格式。"],
		["audio/amr", ".amr", "audio", "AMR 音频。语音录音常用。"],
		["audio/3gpp", ".3gp .3gpp", "audio", "3GPP 音频。手机录音容器。"],

		["video/mp4", ".mp4 .m4v", "video", "MP4 视频。最通用的视频容器。"],
		["video/webm", ".webm", "video", "WebM 视频。通常装 VP8、VP9 或 AV1。"],
		["video/ogg", ".ogv", "video", "Ogg 视频。通常装 Theora。"],
		["video/quicktime", ".mov .qt", "video", "QuickTime 视频。苹果设备常见。"],
		["video/x-msvideo", ".avi", "video", "AVI 视频。老式容器。"],
		["video/mpeg", ".mpeg .mpg .mpe", "video", "MPEG 视频。MPEG-1/2 流。"],
		["video/mp2t", ".ts .m2ts .mts", "video", "MPEG-TS 流。广播和 HLS 分片常用。"],
		["video/3gpp", ".3gp .3gpp", "video", "3GPP 视频。手机视频容器。"],
		["video/x-matroska", ".mkv", "video", "Matroska 视频。下载视频常见。", "浏览器通常不支持，需要转码。"],
		["video/x-flv", ".flv", "video", "Flash 视频。需要 Flash 或转码。"],
		["video/x-ms-wmv", ".wmv", "video", "WMV 视频。Windows Media。"],
		["video/vnd.mpeg.dash.mpd", ".mpd", "video", "MPEG-DASH 清单。"],
		["application/dash+xml", ".mpd", "video", "MPEG-DASH 清单的另一种类型。"],
		["application/vnd.apple.mpegurl", ".m3u8", "video", "HLS 播放列表。Safari 原生支持。"],
		["application/x-mpegURL", ".m3u8", "video", "HLS 播放列表的旧写法。"],

		["font/woff", ".woff", "font", "WOFF 网页字体。压缩的 OpenType。"],
		["font/woff2", ".woff2", "font", "WOFF2 网页字体。压缩率更高，现代浏览器首选。"],
		["font/ttf", ".ttf", "font", "TrueType 字体。"],
		["font/otf", ".otf", "font", "OpenType 字体。支持更多排版特性。"],
		["font/collection", ".ttc", "font", "字体集合。一个文件包含多个字体。"],
		["application/vnd.ms-fontobject", ".eot", "font", "EOT 字体。旧 IE 专用。"],
		["application/font-woff", ".woff", "font", "WOFF 的旧写法。", "新代码用 font/woff。"],

		["application/json", ".json", "app", "JSON 数据。API 最常用。", "RFC 8259 规定 JSON 是 UTF-8，不需要 charset 参数。"],
		["application/xml", ".xml .xsl .xslt", "app", "XML 文档。通用 XML 类型。", "text/xml 是旧写法。"],
		["application/ld+json", ".jsonld", "app", "JSON-LD 结构化数据。schema.org 常用。"],
		["application/problem+json", "", "app", "错误响应格式。", "RFC 9457（原 7807）定义，适合 API 统一错误。"],
		["application/geo+json", ".geojson", "app", "GeoJSON 地理数据。"],
		["application/manifest+json", ".webmanifest", "app", "Web App Manifest。PWA 清单。"],
		["application/x-ndjson", ".ndjson .jsonl", "app", "换行分隔 JSON。每行一个对象，适合流式处理。"],
		["application/json-seq", "", "app", "JSON 文本序列。", "RFC 7464，用 RS 字符分隔记录。"],
		["application/vnd.api+json", "", "app", "JSON:API 规范的媒体类型。"],
		["application/merge-patch+json", "", "app", "JSON Merge Patch 部分更新。", "RFC 7386。"],
		["application/json-patch+json", "", "app", "JSON Patch 补丁。", "RFC 6902。"],
		["application/graphql-response+json", "", "app", "GraphQL over HTTP 的推荐响应类型。"],
		["application/graphql", ".graphql", "app", "GraphQL 查询文本。"],
		["application/atom+xml", ".atom", "app", "Atom 订阅。"],
		["application/rss+xml", ".rss", "app", "RSS 订阅。"],
		["application/rdf+xml", ".rdf", "app", "RDF/XML 语义网数据。"],
		["application/pdf", ".pdf", "app", "PDF 文档。", "浏览器通常能内嵌预览。"],
		["application/octet-stream", ".bin .dat", "app", "未知二进制数据。", "浏览器通常直接下载；已知格式不要用它。"],
		["application/wasm", ".wasm", "app", "WebAssembly 模块。"],
		["application/x-www-form-urlencoded", "", "app", "普通表单编码。", "不能用于文件上传。"],
		["multipart/form-data", "", "app", "多部分表单。文件上传用，必须带 boundary。"],
		["multipart/byteranges", "", "app", "多区间响应。206 一次返回多个字节区间时使用。"],
		["application/vnd.android.package-archive", ".apk", "app", "Android 安装包。"],
		["application/java-archive", ".jar", "app", "Java 包。包含 class 和资源。"],
		["application/x-msdownload", ".exe .dll", "app", "Windows 可执行文件。", "浏览器会当下载，服务器别把它误当 HTML 返回。"],
		["application/x-msi", ".msi", "app", "Windows Installer 安装包。"],
		["application/x-apple-diskimage", ".dmg", "app", "macOS 磁盘映像。"],
		["application/x-iso9660-image", ".iso", "app", "光盘映像。系统安装镜像常用。"],
		["application/vnd.debian.binary-package", ".deb", "app", "Debian/Ubuntu 安装包。"],
		["application/x-rpm", ".rpm", "app", "RPM 安装包。"],
		["application/epub+zip", ".epub", "app", "EPUB 电子书。"],
		["application/vnd.amazon.ebook", ".azw", "app", "Kindle 电子书。"],
		["application/x-mobipocket-ebook", ".mobi", "app", "Mobi 电子书。旧 Kindle 格式。"],
		["application/msword", ".doc", "app", "Word 文档。旧版二进制格式。"],
		["application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".docx", "app", "Word 文档。Office Open XML。"],
		["application/vnd.ms-excel", ".xls", "app", "Excel 表格。旧版二进制格式。"],
		["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx", "app", "Excel 表格。Office Open XML。"],
		["application/vnd.ms-powerpoint", ".ppt", "app", "PowerPoint 演示。旧版二进制格式。"],
		["application/vnd.openxmlformats-officedocument.presentationml.presentation", ".pptx", "app", "PowerPoint 演示。Office Open XML。"],
		["application/vnd.oasis.opendocument.text", ".odt", "app", "OpenDocument 文本。"],
		["application/vnd.oasis.opendocument.spreadsheet", ".ods", "app", "OpenDocument 表格。"],
		["application/vnd.oasis.opendocument.presentation", ".odp", "app", "OpenDocument 演示。"],
		["application/rtf", ".rtf", "app", "RTF 富文本。", "text/rtf 也常见。"],
		["application/x-sqlite3", ".db .sqlite .sqlite3", "app", "SQLite 数据库。单文件数据库。"],
		["application/x-protobuf", ".pb .proto", "app", "Protocol Buffers。", ".proto 源码通常用 text/plain。"],
		["application/msgpack", ".msgpack", "app", "MessagePack 二进制数据。"],
		["application/x-msgpack", ".msgpack", "app", "MessagePack 的旧写法。"],
		["application/cbor", ".cbor", "app", "CBOR 二进制数据。"],
		["application/yaml", ".yaml .yml", "app", "YAML 配置或数据。", "有些服务用 text/yaml。"],
		["application/x-bittorrent", ".torrent", "app", "BitTorrent 种子。"],
		["application/x-sh", ".sh", "app", "Shell 脚本。"],
		["application/vnd.google-earth.kml+xml", ".kml", "app", "KML 地理标注。Google Earth。"],
		["application/vnd.google-earth.kmz", ".kmz", "app", "KMZ 压缩地理标注。"],
		["application/vnd.apple.installer+xml", ".mpkg", "app", "Apple 安装描述文件。"],

		["application/zip", ".zip", "archive", "ZIP 压缩包。最通用。"],
		["application/gzip", ".gz .tgz", "archive", "GZIP 压缩。单文件压缩，也用于 HTTP 内容编码。"],
		["application/x-tar", ".tar", "archive", "TAR 归档。只打包，不压缩。"],
		["application/x-7z-compressed", ".7z", "archive", "7z 压缩包。压缩率高。"],
		["application/vnd.rar", ".rar", "archive", "RAR 压缩包。"],
		["application/x-bzip2", ".bz2", "archive", "BZIP2 压缩。"],
		["application/zstd", ".zst", "archive", "Zstandard 压缩。现代高压缩率算法。"],
		["application/x-xz", ".xz", "archive", "XZ 压缩。Linux 发行包常用。"],
		["application/x-lz4", ".lz4", "archive", "LZ4 压缩。解压速度优先。"],

		["model/gltf+json", ".gltf", "model", "glTF 模型。JSON 描述加外部资源。"],
		["model/gltf-binary", ".glb", "model", "GLB 模型。单文件 glTF。"],
		["model/obj", ".obj", "model", "OBJ 模型。纯文本网格。"],
		["model/stl", ".stl", "model", "STL 模型。3D 打印常用。"],
		["model/vrml", ".wrl", "model", "VRML 模型。旧式 3D 场景。"],
		["model/x3d+xml", ".x3d", "model", "X3D 模型。XML 3D 场景。"],
		["model/mesh", ".msh", "model", "网格模型。通用网格数据。"],
		["model/vnd.usdz+zip", ".usdz", "model", "USDZ 模型。苹果 AR 常用。"],

		["message/http", "", "message", "完整的 HTTP 请求或响应报文。"],
		["message/rfc822", ".eml", "message", "RFC 822 邮件。"],
		["message/partial", "", "message", "分段消息。长消息分片传输。"],
		["message/external-body", "", "message", "外部消息。正文在外部引用。"]
	];
	var CATS = [
		{ id: "text", name: "文本" },
		{ id: "image", name: "图片" },
		{ id: "audio", name: "音频" },
		{ id: "video", name: "视频" },
		{ id: "font", name: "字体" },
		{ id: "app", name: "应用" },
		{ id: "archive", name: "压缩包" },
		{ id: "model", name: "3D 模型" },
		{ id: "message", name: "消息" }
	];
	var cur = "all";
	var picked = "text/plain";

	SK.regutil("u-mimetab", {
		title: "MIME类型表",
		body: function(){
			return '<div class="reftab">'
				+ '<div class="uquery"><input id="m-q" type="text" placeholder="' + esc(SK.td("搜索：.jpg、image/、json、视频")) + '">'
				+ '<span id="m-msg" class="msg"></span></div>'
				+ '<div class="refchips" id="m-chips"></div>'
				+ '<div class="refbody">'
				+ '<div class="refscroll"><table class="reflist mtypes" id="m-tab"></table></div>'
				+ '<aside class="refdetail" id="m-detail"></aside>'
				+ "</div></div>";
		},
		run: function(){
			throw new Error("没有这个工具");
		},
		open: function(){
			cur = "all";
			picked = "text/plain";
			paintchips();
			F.id("m-q").oninput = paint;
			F.id("m-chips").onclick = onchip;
			F.id("m-tab").onclick = onrow;
			paint();
		},
		hideGo: true,
		live: false,
		hideOut: true,
		relang: function(){ SK.reopenutil(); }
	});

	function catname(id){
		var i;
		for (i = 0; i < CATS.length; i++) if (CATS[i].id === id) return CATS[i].name;
		return "";
	}

	function query(){
		return String(SK.val("m-q") || "").trim().toLowerCase();
	}

	function hit(row, q){
		if (!q) return true;
		var ext = row[1] || "";
		var qq = q.charAt(0) === "." ? q.substring(1) : q;
		var blob = (row[0] + " " + ext + " " + ext.replace(/\./g, " ") + " " + catname(row[2])
			+ " " + row[3] + " " + (row[4] || "")).toLowerCase();
		if (blob.indexOf(q) >= 0) return true;
		return qq !== q && blob.indexOf(qq) >= 0;
	}

	function paintchips(){
		var html = ['<button type="button" data-g="all"' + (cur === "all" ? ' class="on"' : "") + ">" + esc(SK.td("全部")) + "<small>"
			+ ROWS.length + "</small></button>"];
		var i, j, n;
		for (i = 0; i < CATS.length; i++) {
			n = 0;
			for (j = 0; j < ROWS.length; j++) if (ROWS[j][2] === CATS[i].id) n++;
			html.push('<button type="button" data-g="' + CATS[i].id + '"'
				+ (cur === CATS[i].id ? ' class="on"' : "") + ">" + esc(SK.td(CATS[i].name))
				+ "<small>" + n + "</small></button>");
		}
		F.id("m-chips").innerHTML = html.join("");
	}

	function paint(){
		var q = query();
		var html = ["<thead><tr><th>" + esc(SK.td("MIME 类型")) + "</th><th>" + esc(SK.td("扩展名"))
			+ "</th><th>" + esc(SK.td("说明")) + "</th></tr></thead><tbody>"];
		var total = 0;
		var i, j, rows, n;
		for (i = 0; i < CATS.length; i++) {
			if (cur !== "all" && cur !== CATS[i].id) continue;
			rows = [];
			for (j = 0; j < ROWS.length; j++) {
				if (ROWS[j][2] === CATS[i].id && hit(ROWS[j], q)) rows.push(ROWS[j]);
			}
			if (!rows.length) continue;
			total += rows.length;
			html.push('<tr class="refgh"><td colspan="3">' + esc(SK.td(CATS[i].name))
				+ '<span class="n">' + rows.length + "</span></td></tr>");
			for (n = 0; n < rows.length; n++) html.push(rowhtml(rows[n]));
		}
		html.push("</tbody>");
		if (!total) html = ['<tbody><tr><td class="refnone">' + esc(SK.td("没有匹配的类型")) + "</td></tr></tbody>"];
		F.id("m-tab").innerHTML = html.join("");
		SK.msg("m-msg", total ? SK.td(q ? "%n 条" : "共 %n 条").replace("%n", total) : "");
		if (total) {
			var sel = rowby(picked);
			if (!visible(sel, q)) sel = firstvisible(q);
			showdetail(sel || ROWS[0]);
		}
		else F.id("m-detail").innerHTML = "";
	}

	function visible(row, q){
		if (!row) return false;
		if (cur !== "all" && cur !== row[2]) return false;
		return hit(row, q);
	}

	function firstvisible(q){
		var i, j;
		for (i = 0; i < CATS.length; i++) {
			if (cur !== "all" && cur !== CATS[i].id) continue;
			for (j = 0; j < ROWS.length; j++) {
				if (ROWS[j][2] === CATS[i].id && hit(ROWS[j], q)) return ROWS[j];
			}
		}
		return null;
	}

	function rowby(mime){
		var i;
		for (i = 0; i < ROWS.length; i++) if (ROWS[i][0] === mime) return ROWS[i];
		return null;
	}

	function rowhtml(row){
		return '<tr data-mime="' + esc(row[0]) + '" title="' + esc(SK.td("点击复制 %s").replace("%s", row[0])) + '"'
			+ (row[0] === picked ? ' class="on"' : "") + ">"
			+ '<td class="mtype">' + esc(row[0]) + "</td>"
			+ '<td class="mext">' + (row[1] ? esc(row[1]) : "—") + "</td>"
			+ "<td>" + esc(SK.td(row[3])) + "</td></tr>";
	}

	function sample(row){
		var m = row[0];
		if (m.indexOf("multipart/") === 0)
			return "Content-Type: " + m + "; boundary=----WebKitFormBoundary";
		if (m.indexOf("text/") === 0 || m === "application/javascript")
			return "Content-Type: " + m + "; charset=utf-8";
		return "Content-Type: " + m;
	}

	// 在分号后留换行点，窄面板里不会把 charset 从单词中间截断
	function samplehtml(row){
		var parts = sample(row).split("; ");
		var out = esc(parts[0]);
		var i;
		for (i = 1; i < parts.length; i++) out += "; <wbr>" + esc(parts[i]);
		return out;
	}

	function showdetail(row){
		if (!row) return;
		var meta = [
			["类别", SK.td(catname(row[2]))],
			["扩展名", row[1] || "—"],
			["说明", SK.td(row[3])]
		];
		if (row[4]) meta.push(["备注", SK.td(row[4])]);
		var html = '<div class="refbig mime">' + esc(row[0]) + "</div>"
			+ '<div class="refsub">' + esc(SK.td(catname(row[2]))) + "</div>"
			+ '<dl class="refmeta">';
		var i;
		for (i = 0; i < meta.length; i++)
			html += "<dt>" + esc(SK.td(meta[i][0])) + "</dt><dd>" + esc(meta[i][1]) + "</dd>";
		html += "</dl>"
			+ '<div class="refsample">' + samplehtml(row) + "</div>";
		F.id("m-detail").innerHTML = html;
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
		if (!el || !F.attr(el, "data-mime")) return;
		var mime = F.attr(el, "data-mime");
		var row = rowby(mime);
		if (!row) return;
		picked = mime;
		paint();
		showdetail(row);
		SK.copytext(mime, "m-msg");
	}

	function esc(s){
		return String(s == null ? "" : s)
			.replace(/&/g, "&amp;")
			.replace(/</g, "&lt;")
			.replace(/>/g, "&gt;")
			.replace(/"/g, "&quot;");
	}
})();
