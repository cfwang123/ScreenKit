using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ScreenKit.Json;

namespace ScreenKit;

/// <summary>
/// web 文档阅读器的后端：/api/docview 收文档、转 HTML；/api/docasset 发转换出来的图片和页面。
/// 转换本身复用 WPF 阅读器那两份实现，网页和桌面看到的是同一套排版。
/// </summary>
sealed partial class HttpOcrServer {
	const long DOCVIEWMAX = 60L * 1024 * 1024;
	/// <summary>每个文档一个目录，超过一天或数量过多时清理，别让 tmp 越堆越大。</summary>
	const int DOCVIEWKEEP = 40;

	/// <summary>转换产物根目录：tmp/docview/&lt;随机&gt;/，下面是 page.html 和图片。</summary>
	static string docviewroot() {
		var dir = Path.Combine(TmpStore.Root, "docview");
		Directory.CreateDirectory(dir);
		return dir;
	}

	static bool docviewext(string path) {
		var ext = Path.GetExtension(path ?? "").ToLowerInvariant();
		return ext is ".md" or ".markdown" or ".mdown" or ".mkd" or ".docx" or ".pdf";
	}

	/// <summary>只留文件名，去掉路径和非法字符；不认识的扩展名返回空串。</summary>
	static string docviewname(string raw) {
		var name = Path.GetFileName((raw ?? "").Trim().Replace('\\', '/'));
		foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, ' ');
		name = name.Trim().Trim('.');
		if (name.Length == 0) return "";
		if (name.Length > 160) {
			var ext = Path.GetExtension(name).ToLowerInvariant();
			name = name.Substring(0, 160 - ext.Length) + ext;
		}
		return docviewext(name) ? name : "";
	}

	/// <summary>把上传的字节转成页面。Markdown 走 MdRead.ToHtml，docx/pdf 走 DocViewHtml。</summary>
	void handledocview(SockCtx ctx) {
		if (!ispost(ctx.Request)) {
			writejson(ctx, 405, err(805, "docview 仅支持 POST"));
			return;
		}
		var name = docviewname(query(ctx.Request, "name"));
		if (name.Length == 0) {
			writejson(ctx, 200, err(802, "请上传 .md、.docx 或 .pdf 文件"));
			return;
		}
		var clen = ctx.Request.ContentLength64;
		if (clen > DOCVIEWMAX) {
			writejson(ctx, 200, err(803, "文件超过 60 MB"));
			return;
		}
		var theme = (query(ctx.Request, "theme") ?? "").Trim().ToLowerInvariant();
		var scale = 1.0;
		if (double.TryParse(query(ctx.Request, "scale") ?? "", System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out var sc))
			scale = sc < 0.6 ? 0.6 : (sc > 4 ? 4 : sc);

		var id = Environment.TickCount.ToString("x") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
		var dir = Path.Combine(docviewroot(), id);
		var ext = Path.GetExtension(name).ToLowerInvariant();
		DocViewHtml.Result res;
		try {
			Directory.CreateDirectory(dir);
			var raw = Path.Combine(dir, "src" + ext);
			long total = 0;
			using (var fs = new FileStream(raw, FileMode.Create, FileAccess.Write, FileShare.None)) {
				var buf = new byte[64 * 1024];
				int n;
				while ((n = ctx.Request.InputStream.Read(buf, 0, buf.Length)) > 0) {
					total += n;
					if (total > DOCVIEWMAX) throw new InvalidOperationException("文件超过 60 MB");
					fs.Write(buf, 0, n);
				}
			}
			if (total == 0) throw new InvalidOperationException("文件是空的");
			if (ext is ".md" or ".markdown" or ".mdown" or ".mkd") {
				// 只拿到 md 的字节，同目录的图片取不到，基准目录留空，别生成打不开的 file:// 地址
				var md = MdRead.ReadText(raw);
				var html = MdRead.ToHtml(md, "", name, scale, theme);
				res = new DocViewHtml.Result { Html = rewritehtml(html, id, dir), Pages = 0 };
			}
			else if (ext == ".pdf") {
				// PDF 不预渲染：只留原文件，页面交给 pdf.js 按需取页，翻到哪画到哪
				if (!NativeRuntime.HasPdfium())
					throw new MissingFeatureException("缺少 pdfium.dll，请先在帮助 → 安装功能里安装 PDFium");
				var pages = DocViewPdf.pagecount(File.ReadAllBytes(raw));
				res = new DocViewHtml.Result {
					Html = pdfshell(id, name, pages),
					Pages = pages,
					Images = 0,
				};
				res.KeepSource = true;
			}
			else {
				res = DocViewHtml.Convert(raw, Path.Combine(dir, "img"));
				res.Html = rewritehtml(res.Html, id, dir);
			}
			// md/docx 的页里没有脚本，图片点击要靠这里补一段
			res.Html = injectclick(res.Html);
			File.WriteAllText(Path.Combine(dir, "page.html"), res.Html ?? "", new UTF8Encoding(true));
			// 原文件已经拆成页面和图片，留着占地方；PDF 要留着给按需出页用
			if (!res.KeepSource) {
				try { File.Delete(raw); } catch { }
			}
			docviewtrim();
			writejson(ctx, 200, JSONF.newObject(("code", 100), ("data", JSONF.newObject(
				("id", id), ("name", name), ("kind", ext.TrimStart('.')), ("pages", res.Pages),
				("images", res.Images), ("html", res.Html)))));
		}
		catch (Exception ex) {
			try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
			writejson(ctx, 200, err(failcode(ex), ex.Message));
		}
	}

	/// <summary>
	/// PDF 的外壳页：pdf.js 从 CDN 取，页面按需渲染。
	/// 一页一个占位 div，滚到附近才画，几页的文档打开瞬间就出来，几百页也不会先卡住。
	/// </summary>
	static string pdfshell(string id, string name, int pages) {
		// 这里是脚本里的字符串，&amp; 不会被解码，必须写真正的 &
		var file = "/api/docasset?id=" + id + "&f=src.pdf";
		var title = (name ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
		var sb = new StringBuilder();
		sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\">")
			.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
			.Append("<title>").Append(title).Append("</title>")
			.Append("<style>")
			.Append("html,body{margin:0;padding:0;height:100%;overflow:hidden;background:#f0f2f5;color:#1f2328;")
			.Append("font-family:'Segoe UI','Microsoft YaHei UI',sans-serif}")
			.Append("#scroll{position:absolute;inset:0;overflow:auto;overscroll-behavior:contain}")
			.Append("#scroll.grab{cursor:grab}")
			.Append("#scroll.grabbing{cursor:grabbing}")
			.Append("#pages{padding:18px 14px 40px}")
			/* 页与页的缝隙固定 10px，和桌面阅读器一致。宽度写死 px，auto 外边距让超出时只往右溢出，横向滚动够得着 */
			.Append(".pg{position:relative;margin:0 auto 10px;background:#fff;box-shadow:0 1px 3px rgba(15,23,42,.14);overflow:hidden}")
			.Append(".pg:last-child{margin-bottom:14px}")
			.Append(".pg canvas{display:block}")
			.Append(".pg .ph{position:absolute;left:0;top:0;right:0;bottom:0;text-align:center;color:#8b949e;font-size:13px;padding-top:24px;box-sizing:border-box}")
			/* 文本层：压在画布上的一层透明文字，用来选中和复制，位置字号由 pdf.js 写行内样式。
			   容器不吃鼠标事件，只有文字本身吃，这样拖在空白处是平移、拖在字上是选字 */
			.Append(".pg .tx{position:absolute;left:0;top:0;overflow:hidden;line-height:1;")
			.Append("pointer-events:none;transform-origin:0 0;-webkit-user-select:text;user-select:text}")
			.Append(".pg .tx span,.pg .tx br{position:absolute;color:transparent;white-space:pre;cursor:text;")
			.Append("transform-origin:0% 0%;margin:0;padding:0;pointer-events:auto}")
			.Append(".pg .tx span::selection,.pg .tx br::selection{background:rgba(37,99,235,.3)}")
			.Append("#bar{position:fixed;top:10px;right:14px;z-index:9;display:flex;align-items:center;gap:2px;")
			.Append("padding:4px;border-radius:9px;background:rgba(255,255,255,.95);")
			.Append("box-shadow:0 2px 8px rgba(15,23,42,.16)}")
			.Append("#bar button{border:0;background:transparent;color:#24292f;min-width:26px;height:26px;")
			.Append("border-radius:6px;cursor:pointer;font-size:13px;line-height:1;padding:0 6px}")
			.Append("#bar button:hover{background:rgba(15,23,42,.08)}")
			.Append("#bar button.on{background:rgba(37,99,235,.13);color:#1d4ed8}")
			.Append("#pct{min-width:46px;text-align:center;color:#57606a;font-size:12.5px;")
			.Append("font-variant-numeric:tabular-nums;cursor:default}")
			.Append("#bar .sep{width:1px;height:16px;background:rgba(15,23,42,.12);margin:0 3px}")
			.Append("#tip{position:fixed;left:50%;bottom:14px;transform:translateX(-50%);")
			.Append("padding:5px 12px;border-radius:14px;background:rgba(31,35,40,.78);color:#fff;")
			.Append("font-size:12.5px;pointer-events:none;opacity:0;transition:opacity .2s}")
			.Append("#tip.on{opacity:1}")
			.Append("</style></head><body>")
			.Append("<div id=\"bar\">")
			.Append("<button id=\"zout\" title=\"缩小（Ctrl+滚轮）\">−</button>")
			.Append("<span id=\"pct\">100%</span>")
			.Append("<button id=\"zin\" title=\"放大（Ctrl+滚轮）\">+</button>")
			.Append("<span class=\"sep\"></span>")
			.Append("<button id=\"actual\" class=\"on\" title=\"100% 实际大小\">1:1</button>")
			.Append("<button id=\"fitw\" title=\"适应宽度\">宽</button>")
			.Append("<button id=\"fitp\" title=\"适应页高\">页</button>")
			.Append("</div>")
			.Append("<div id=\"scroll\"><div id=\"pages\"></div></div><div id=\"tip\"></div>")
			.Append("<script src=\"https://cdn.jsdelivr.net/npm/pdfjs-dist@3.11.174/legacy/build/pdf.min.js\"></script>")
			.Append("<script>")
			.Append("(function(){")
			.Append("var FILE='").Append(file).Append("';")
			.Append("var TOTAL=").Append(pages).Append(";")
			.Append(PDFJS_JS)
			.Append("})();")
			.Append("</script></body></html>");
		return sb.ToString();
	}

	/// <summary>
	/// 页面脚本：pdf.js 按页渲染，滚到哪画到哪；每页带一层透明文字可选中复制；
	/// 缩放跟 pdf.js 的 --scale-factor 走，平移用滚动位置实现。
	/// </summary>
	const string PDFJS_JS = """
		var lib = window.pdfjsLib;
		var host = document.getElementById("pages");
		var scroller = document.getElementById("scroll");
		var tip = document.getElementById("tip");
		var pct = document.getElementById("pct");
		var btnin = document.getElementById("zin");
		var btnout = document.getElementById("zout");
		var btnw = document.getElementById("fitw");
		var btnp = document.getElementById("fitp");
		var btna = document.getElementById("actual");

		var doc = null;
		var slots = [];
		var metas = [];
		var drawn = {};
		var busy = {};
		// 默认按 100% 实际大小打开：PDF 的 1 点是 1/72 英寸，屏幕 1 像素是 1/96 英寸
		var fitmode = "actual";
		var zoom = 1;
		var current = 1;
		var tipTok = 0;
		var PT2PX = 96 / 72;

		if (!lib) {
			host.innerHTML = '<div class="pg"><div class="ph">加载 pdf.js 失败，请检查网络后刷新</div></div>';
			return;
		}
		lib.GlobalWorkerOptions.workerSrc =
			"https://cdn.jsdelivr.net/npm/pdfjs-dist@3.11.174/legacy/build/pdf.worker.min.js";

		function say(text){
			tip.textContent = text;
			tip.className = "on";
			var mine = ++tipTok;
			setTimeout(function(){ if (mine === tipTok) tip.className = ""; }, 1500);
		}

		function clamp(v, lo, hi){ return v < lo ? lo : (v > hi ? hi : v); }

		// 正文可用宽度（去掉左右留白和滚动条）
		function availw(){ return Math.max(160, scroller.clientWidth - 28); }

		function pagew(i){ return metas[i] ? metas[i].w : 595; }
		function pageh(i){ return metas[i] ? metas[i].h : 842; }

		// fitmode 决定基准缩放，zoom 再乘上去；返回 CSS 像素 / PDF 点
		function effscale(i){
			var w = pagew(i);
			var s;
			if (fitmode === "width") s = availw() / w;
			else if (fitmode === "page") s = Math.min(availw() / w, (scroller.clientHeight - 76) / pageh(i));
			else s = PT2PX;  // actual：100% 实际大小，A4 约 794px 宽
			return clamp(s * zoom, 0.1, 8);
		}

		function setpct(){
			// 基准缩放已经折算成屏幕实际大小，这里就是用户看到的百分比
			pct.textContent = Math.round(effscale(0) * 100 / PT2PX) + "%";
			btnw.className = fitmode === "width" ? "on" : "";
			btnp.className = fitmode === "page" ? "on" : "";
			btna.className = fitmode === "actual" ? "on" : "";
		}

		function build(total){
			var i, el;
			host.innerHTML = "";
			slots = [];
			metas = [];
			for (i = 0; i < total; i++) {
				el = document.createElement("div");
				el.className = "pg";
				el.style.width = Math.round(595 * PT2PX) + "px";
				el.style.height = Math.round(842 * PT2PX) + "px";
				el.innerHTML = '<div class="ph">第 ' + (i + 1) + " 页</div>";
				host.appendChild(el);
				slots.push(el);
			}
		}

		// 页框尺寸、缩放变量、画布、文本层一起按当前缩放摆好
		function layout(i, v){
			var el = slots[i];
			el.style.width = Math.round(v.width) + "px";
			el.style.height = Math.round(v.height) + "px";
			// 文本层的 width/height/font-size 都写成 calc(var(--scale-factor)*…)，必须和 viewport.scale 一致
			el.style.setProperty("--scale-factor", v.scale);
		}

		function textlayer(el, page, v){
			var tx = document.createElement("div");
			tx.className = "tx";
			return page.getTextContent().then(function(tc){
				return lib.renderTextLayer({
					textContentSource: tc,
					container: tx,
					viewport: v,
					textDivs: []
				}).promise;
			}).then(function(){
				el.appendChild(tx);
			}).catch(function(){
				// 没有文字层（纯扫描件等）不影响看图
			});
		}

		function draw(i){
			if (doc == null || i < 0 || i >= slots.length) return;
			if (busy[i]) return;
			busy[i] = true;
			var el = slots[i];
			doc.getPage(i + 1).then(function(page){
				var vp = page.getViewport({ scale: 1 });
				metas[i] = { w: vp.width, h: vp.height };
				var v = page.getViewport({ scale: effscale(i) });
				layout(i, v);
				var dpr = clamp(window.devicePixelRatio || 1, 1, 2);
				var canvas = document.createElement("canvas");
				canvas.width = Math.max(1, Math.round(v.width * dpr));
				canvas.height = Math.max(1, Math.round(v.height * dpr));
				canvas.style.width = Math.round(v.width) + "px";
				canvas.style.height = Math.round(v.height) + "px";
				var ctx = canvas.getContext("2d", { alpha: false });
				ctx.scale(dpr, dpr);
				return page.render({ canvasContext: ctx, viewport: v }).promise.then(function(){
					el.innerHTML = "";
					el.appendChild(canvas);
					return textlayer(el, page, v);
				}).then(function(){
					drawn[i] = v.scale;
					busy[i] = false;
				});
			}).catch(function(){
				busy[i] = false;
				el.innerHTML = '<div class="ph">第 ' + (i + 1) + " 页打不开</div>";
			});
		}

		// 离视口一屏以内的页先画，滚动时再补其它页
		function scan(){
			if (doc == null) return;
			var sr = scroller.getBoundingClientRect();
			var vh = sr.height;
			var i, r, best = 1, s;
			for (i = 0; i < slots.length; i++) {
				r = slots[i].getBoundingClientRect();
				if (r.bottom <= sr.top - vh || r.top >= sr.bottom + vh) continue;
				// 已经按当前比例画过就别再画，否则每次滚动都重渲染
				s = effscale(i);
				if (drawn[i] && Math.abs(drawn[i] - s) < 0.0005) continue;
				draw(i);
			}
			for (i = 0; i < slots.length; i++) {
				r = slots[i].getBoundingClientRect();
				if (r.bottom > sr.top + 8) { best = i + 1; break; }
			}
			if (best !== current) {
				current = best;
				say(best + " / " + slots.length);
			}
		}

		// 缩放变了：所有页尺寸重算，可见的立即重画，其余的等滚到再看
		function relayout(){
			var i, s;
			for (i = 0; i < slots.length; i++) {
				s = effscale(i);
				slots[i].style.width = Math.round(pagew(i) * s) + "px";
				slots[i].style.height = Math.round(pageh(i) * s) + "px";
				drawn[i] = 0;
			}
			setpct();
			scan();
		}

		// 以光标（或视口中心）为锚点缩放，缩放前后同一处内容尽量留在原地
		function setzoom(z, ox, oy){
			z = clamp(z, 0.25, 6);
			if (Math.abs(z - zoom) < 0.001) return;
			var w = scroller.clientWidth, h = scroller.clientHeight;
			if (ox == null) { ox = w / 2; oy = h / 2; }
			var rx = (scroller.scrollLeft + ox) / Math.max(1, scroller.scrollWidth);
			var ry = (scroller.scrollTop + oy) / Math.max(1, scroller.scrollHeight);
			zoom = z;
			relayout();
			scroller.scrollLeft = rx * scroller.scrollWidth - ox;
			scroller.scrollTop = ry * scroller.scrollHeight - oy;
		}

		var ticking = false;
		scroller.addEventListener("scroll", function(){
			if (ticking) return;
			ticking = true;
			requestAnimationFrame(function(){ ticking = false; scan(); });
		}, { passive: true });

		// Ctrl+滚轮缩放，普通滚轮留给滚动
		scroller.addEventListener("wheel", function(e){
			if (!e.ctrlKey && !e.metaKey) return;
			e.preventDefault();
			var r = scroller.getBoundingClientRect();
			var step = e.deltaY < 0 ? 1.12 : 1 / 1.12;
			setzoom(zoom * step, e.clientX - r.left, e.clientY - r.top);
		}, { passive: false });

		// 点一下页面：找出点中的是哪张插图，只把那张图按原分辨率放大，不是整页。
		// PDF 里的图是 canvas 画上去的，没有 img 元素，只能翻绘制指令 + 变换矩阵倒推位置。
		function hits(i, px, py){
			return doc.getPage(i + 1).then(function(page){
				var v = page.getViewport({ scale: effscale(i) });
				// 把页面坐标（CSS 像素）换回 PDF 用户空间
				var m = lib.Util.inverseTransform(v.transform);
				var ux = m[0] * px + m[2] * py + m[4];
				var uy = m[1] * px + m[3] * py + m[5];
				return page.getOperatorList().then(function(ops){
					var stack = [], ctm = null, found = [], fn = ops.fnArray, ag = ops.argsArray;
					var k, o, a;
					for (k = 0; k < fn.length; k++) {
						o = fn[k];
						a = ag[k];
						if (o === lib.OPS.save) {
							stack.push(ctm ? ctm.slice() : [1, 0, 0, 1, 0, 0]);
						}
						else if (o === lib.OPS.restore) {
							ctm = stack.length ? stack.pop() : null;
						}
						else if (o === lib.OPS.transform) {
							ctm = ctm ? lib.Util.transform(ctm, a) : a.slice();
						}
						else if (o === lib.OPS.paintImageXObject || o === lib.OPS.paintInlineImageXObject) {
							found.push({ id: a[0], ctm: ctm ? ctm.slice() : [1, 0, 0, 1, 0, 0] });
						}
					}
					// 后画的压在上面，从后往前找第一个命中的
					var j, box, h;
					for (j = found.length - 1; j >= 0; j--) {
						h = found[j];
						box = unitbox(h.ctm);
						if (ux >= box[0] && ux <= box[2] && uy >= box[1] && uy <= box[3]) return h;
					}
					return null;
				});
			});
		}

		// 图片总画在单位正方形 (0,0)-(1,1) 里，乘上当时 CTM 就是它在页面上的框
		function unitbox(m){
			var xs = [], ys = [], i, x, y;
			var pts = [[0, 0], [1, 0], [0, 1], [1, 1]];
			for (i = 0; i < 4; i++) {
				x = pts[i][0]; y = pts[i][1];
				xs.push(m[0] * x + m[2] * y + m[4]);
				ys.push(m[1] * x + m[3] * y + m[5]);
			}
			return [Math.min.apply(null, xs), Math.min.apply(null, ys),
				Math.max.apply(null, xs), Math.max.apply(null, ys)];
		}

		// 取那张图的原始像素，铺到画布上再交给外层 Viewer
		function rawimage(i, hit){
			return doc.getPage(i + 1).then(function(page){
				return new Promise(function(res){
					page.objs.get(hit.id, function(data){
						if (!data) { res(0); return; }
						var c = document.createElement("canvas");
						var ctx;
						// 浏览器支持 OffscreenCanvas 时 pdf.js 直接给 ImageBitmap，没有原始字节可转
						if (data.bitmap) {
							c.width = data.bitmap.width;
							c.height = data.bitmap.height;
							ctx = c.getContext("2d", { alpha: false });
							ctx.fillStyle = "#fff";
							ctx.fillRect(0, 0, c.width, c.height);
							ctx.drawImage(data.bitmap, 0, 0);
						}
						else if (data.data && data.width && data.height) {
							var w = data.width, h = data.height;
							c.width = w;
							c.height = h;
							ctx = c.getContext("2d", { alpha: false });
							var img = ctx.createImageData(w, h);
							var out = img.data, src = data.data, kind = data.kind;
							var p, q, n = w * h;
							if (kind === lib.ImageKind.RGBA_32BPP || kind === lib.ImageKind.RGB_24BPP) {
								var bpp = kind === lib.ImageKind.RGBA_32BPP ? 4 : 3;
								for (p = 0, q = 0; p < n; p++, q += 4) {
									out[q] = src[p * bpp];
									out[q + 1] = src[p * bpp + 1];
									out[q + 2] = src[p * bpp + 2];
									out[q + 3] = bpp === 4 ? src[p * bpp + 3] : 255;
								}
							}
							else {
								// 1bpp：每字节 8 个像素，1 是黑
								for (p = 0; p < n; p++) {
									var bit = (src[p >> 3] >> (7 - (p & 7))) & 1;
									out[p * 4] = out[p * 4 + 1] = out[p * 4 + 2] = bit ? 0 : 255;
									out[p * 4 + 3] = 255;
								}
							}
							ctx.putImageData(img, 0, 0);
						}
						else { res(0); return; }
						var url;
						try { url = c.toDataURL("image/png"); } catch (e) { res(0); return; }
						try {
							parent.postMessage({ sk: "docimg", src: url, list: [url], index: 0 }, "*");
						}
						catch (e2) { }
						res(1);
					});
				});
			});
		}

		function bigimage(i, px, py){
			if (doc == null || i < 0 || i >= slots.length) return;
			hits(i, px, py).then(function(hit){
				if (!hit) return;
				return rawimage(i, hit);
			}).catch(function(){ });
		}

		// 平移：左键拖空白/画布、中键拖任意位置。拖在文字上则是选中文字，不拦
		var panning = null;
		var downat = null;
		var moved = false;
		var space = false;
		scroller.addEventListener("mousedown", function(e){
			var ontext = e.target && e.target.closest && e.target.closest(".tx span,.tx br");
			if (ontext && e.button === 0 && !space) return;
			if (e.button !== 0 && e.button !== 1) return;
			if (e.button === 0 && !space && e.target.closest && e.target.closest(".tx")) return;
			panning = {
				x: e.clientX, y: e.clientY,
				l: scroller.scrollLeft, t: scroller.scrollTop
			};
			downat = { x: e.clientX, y: e.clientY };
			moved = false;
			scroller.className = space ? "grab" : "";
			e.preventDefault();
		});
		window.addEventListener("mousemove", function(e){
			if (!panning) return;
			if (!moved && Math.abs(e.clientX - downat.x) + Math.abs(e.clientY - downat.y) > 4) {
				moved = true;
				scroller.className = "grabbing";
			}
			if (!moved) return;
			scroller.scrollLeft = panning.l - (e.clientX - panning.x);
			scroller.scrollTop = panning.t - (e.clientY - panning.y);
		});
		window.addEventListener("mouseup", function(e){
			if (!panning) return;
			var wasclick = !moved && panning && e.button === 0;
			panning = null;
			downat = null;
			scroller.className = space ? "grab" : "";
			if (!wasclick) return;
			// 刚选完字不算点击
			var sel = window.getSelection ? String(window.getSelection()) : "";
			if (sel && sel.length) return;
			var el = document.elementFromPoint(e.clientX, e.clientY);
			var pg = el && el.closest ? el.closest(".pg") : null;
			if (!pg) return;
			// 点击位置换到这一页自己的 CSS 坐标，用来判断点中了哪张图
			var r = pg.getBoundingClientRect();
			bigimage(slots.indexOf(pg), e.clientX - r.left, e.clientY - r.top);
		});
		// 按住空格再拖，空白处和文字上都能平移
		window.addEventListener("keydown", function(e){
			if (e.code === "Space" && !space) {
				space = true;
				scroller.className = "grab";
			}
		});
		window.addEventListener("keyup", function(e){
			if (e.code === "Space") {
				space = false;
				if (!panning) scroller.className = "";
			}
		});

		btnin.onclick = function(){ setzoom(zoom * 1.2); };
		btnout.onclick = function(){ setzoom(zoom / 1.2); };
		btna.onclick = function(){ fitmode = "actual"; zoom = 1; relayout(); };
		btnw.onclick = function(){ fitmode = "width"; zoom = 1; relayout(); };
		btnp.onclick = function(){ fitmode = "page"; zoom = 1; relayout(); };

		var rtimer = 0;
		window.addEventListener("resize", function(){
			clearTimeout(rtimer);
			rtimer = setTimeout(relayout, 160);
		});

		build(TOTAL > 0 ? TOTAL : 1);
		setpct();
		lib.getDocument({ url: FILE, cMapUrl: "https://cdn.jsdelivr.net/npm/pdfjs-dist@3.11.174/cmaps/",
			cMapPacked: true, isOffscreenCanvasSupported: false }).promise.then(function(d){
			doc = d;
			if (d.numPages !== TOTAL) build(d.numPages);
			scan();
		}).catch(function(ex){
			host.innerHTML = '<div class="pg"><div class="ph">PDF 打不开：' +
				String(ex && ex.message ? ex.message : ex) + "</div></div>";
		});
	""";

	/// <summary>
	/// 让文档页里的图片能点开大图：页面在 iframe 里跑，点图时给外层发消息，
	/// 由工具页挂的 Viewer.js 弹层放大（比在 iframe 里再造一套查看器省事）。
	/// </summary>
	const string IMGCLICK_JS = """
		document.addEventListener("click", function(e){
			var img = e.target && e.target.closest ? e.target.closest("img") : null;
			if (!img || !img.src) return;
			// 只认文档里的插图，把工具栏之类的界面元素排除掉
			if (img.closest("#bar")) return;
			e.preventDefault();
			var list = [], all, i;
			all = document.querySelectorAll("img");
			for (i = 0; i < all.length; i++) {
				if (all[i].src) list.push(all[i].src);
			}
			var idx = list.indexOf(img.src);
			try {
				parent.postMessage({
					sk: "docimg",
					src: img.src,
					list: list,
					index: idx < 0 ? 0 : idx
				}, "*");
			}
			catch (err) { }
		}, true);
	""";

	/// <summary>
	/// 在整页 HTML 的 &lt;/body&gt; 前插一段脚本。md/docx 的页没有自己的脚本，图片点击就靠它带出去。
	/// </summary>
	static string injectclick(string html) {
		if (string.IsNullOrEmpty(html)) return html;
		var tag = "<script>" + IMGCLICK_JS + "</script>";
		var i = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
		if (i < 0) return html + tag;
		return html.Substring(0, i) + tag + html.Substring(i);
	}

	static string rewritehtml(string html, string id, string dir) {
		if (string.IsNullOrEmpty(html)) return "";
		var root = Path.GetFullPath(dir);
		return Regex.Replace(html, "file://+([^\"'\\s]*)", m => {
			var full = m.Groups[1].Value.Replace('/', '\\');
			string f = null;
			try {
				var abs = Path.GetFullPath(full);
				if (abs.StartsWith(root, StringComparison.OrdinalIgnoreCase))
					f = abs.Substring(root.Length).TrimStart('\\').Replace('\\', '/');
			}
			catch { }
			if (string.IsNullOrEmpty(f)) {
				// 不在转换目录里（如 md 引用了外部图片），交给浏览器按原地址处理
				return m.Value;
			}
			return "/api/docasset?id=" + id + "&f=" + Uri.EscapeDataString(f);
		});
	}

	/// <summary>发转换产物：page.html 或转换目录里的图片。目录名只认本进程生成的随机名。</summary>
	void handledocasset(SockCtx ctx) {
		if (!isget(ctx.Request)) {
			writejson(ctx, 405, err(805, "docasset 仅支持 GET"));
			return;
		}
		var id = (query(ctx.Request, "id") ?? "").Trim();
		var name = (query(ctx.Request, "f") ?? "").Replace('\\', '/').Trim();
		if (!docviewidok(id) || name.Length == 0 || name.Contains("..") || name.StartsWith("/")) {
			writejson(ctx, 404, err(404, "文件不存在"));
			return;
		}
		var dir = Path.GetFullPath(Path.Combine(docviewroot(), id));
		var full = Path.GetFullPath(Path.Combine(dir, name));
		if (!full.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
			|| !File.Exists(full)) {
			writejson(ctx, 404, err(404, "文件不存在"));
			return;
		}
		var mime = Path.GetExtension(full).ToLowerInvariant() switch {
			".html" => "text/html; charset=utf-8",
			".pdf" => "application/pdf",
			".png" => "image/png",
			".jpg" or ".jpeg" => "image/jpeg",
			".gif" => "image/gif",
			".bmp" => "image/bmp",
			".webp" => "image/webp",
			_ => "application/octet-stream",
		};
		try {
			var bytes = File.ReadAllBytes(full);
			var res = ctx.Response;
			res.StatusCode = 200;
			res.ContentType = mime;
			res.ContentLength64 = bytes.Length;
			res.Headers["Access-Control-Allow-Origin"] = "*";
			res.Headers["Cache-Control"] = mime.StartsWith("text/html", StringComparison.Ordinal)
				? SendFileWebPages.NO_CACHE : SendFileWebPages.CACHE_CONTROL;
			res.OutputStream.Write(bytes, 0, bytes.Length);
			try { res.OutputStream.Close(); } catch { }
			try { res.Close(); } catch { }
		}
		catch (Exception ex) {
			writejson(ctx, 500, err(500, ex.Message));
		}
	}

	/// <summary>目录名只允许十六进制和短横线，避免拼出目录外的路径。</summary>
	static bool docviewidok(string id) {
		if (string.IsNullOrEmpty(id) || id.Length > 40) return false;
		foreach (var c in id) {
			if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && c != '_')
				return false;
		}
		return true;
	}

	/// <summary>转换目录按时间保留 40 个，多的直接删；一天前的也删。</summary>
	static void docviewtrim() {
		try {
			var dirs = Directory.GetDirectories(docviewroot());
			if (dirs.Length <= DOCVIEWKEEP) return;
			Array.Sort(dirs, (a, b) => Directory.GetLastWriteTimeUtc(a).CompareTo(Directory.GetLastWriteTimeUtc(b)));
			for (var i = 0; i < dirs.Length - DOCVIEWKEEP; i++) {
				try { Directory.Delete(dirs[i], true); } catch { }
			}
		}
		catch { }
	}
}
