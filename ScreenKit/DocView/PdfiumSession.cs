using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingSizeF = System.Drawing.SizeF;

namespace ScreenKit.DocView;

sealed class PdfCharInfo {
	public int Index;
	public char Char;
	/// <summary>页坐标：左上原点、Y 向下，单位与 pageW/H 相同（pt）。</summary>
	public double Left, Top, Right, Bottom;
}

/// <summary>页内嵌入图：页坐标左上原点 Y 向下（pt）。</summary>
sealed class PdfImageInfo {
	public int ObjectIndex;
	public double Left, Top, Right, Bottom;
	public BitmapSource Bitmap;
}

/// <summary>页内链接命中结果（书内 GoTo 或 URI）。</summary>
sealed class PdfLinkHit {
	/// <summary>目标页 0-based；URI 或无目标时为 -1。</summary>
	public int DestPageIndex = -1;
	public bool HasDestY;
	public float DestY;
	/// <summary>距页顶比例 0..1（由 DestY 换算）。</summary>
	public double TopFrac;
	/// <summary>外部 URI（http/https/mailto 等）；书内跳转时为 null。</summary>
	public string Uri;
}

/// <summary>
/// 单文档 pdfium 会话：常开、串行渲染/抽字（调用方保证 PdfIo.Gate）。
/// </summary>
sealed class PdfiumSession : IDisposable {
	static int libInited;

	IntPtr doc;
	bool disposed;

	public int PageCount { get; private set; }
	public DrawingSizeF[] PageSizesPt { get; private set; }

	public static PdfiumSession Open(byte[] pdfBytes) {
		if (pdfBytes == null || pdfBytes.Length == 0)
			throw new ArgumentException("PDF 为空");
		ensurelib();
		var s = new PdfiumSession();
		s.doc = PdfiumNative.FPDF_LoadMemDocument(pdfBytes, pdfBytes.Length, null);
		if (s.doc == IntPtr.Zero)
			throw new InvalidOperationException("无法打开 PDF（格式错误或已损坏）");
		s.PageCount = PdfiumNative.FPDF_GetPageCount(s.doc);
		if (s.PageCount <= 0) {
			s.Dispose();
			throw new InvalidOperationException("PDF 无页面");
		}
		s.PageSizesPt = new DrawingSizeF[s.PageCount];
		for (var i = 0; i < s.PageCount; i++) {
			double w, h;
			if (PdfiumNative.FPDF_GetPageSizeByIndex(s.doc, i, out w, out h) == 0) {
				// fallback
				var page = PdfiumNative.FPDF_LoadPage(s.doc, i);
				if (page != IntPtr.Zero) {
					w = PdfiumNative.FPDF_GetPageWidth(page);
					h = PdfiumNative.FPDF_GetPageHeight(page);
					PdfiumNative.FPDF_ClosePage(page);
				}
			}
			s.PageSizesPt[i] = new DrawingSizeF((float)Math.Max(1, w), (float)Math.Max(1, h));
		}
		DocViewLog.Info($"PdfiumSession open pages={s.PageCount} bytes={pdfBytes.Length}");
		return s;
	}

	static void ensurelib() {
		if (System.Threading.Interlocked.CompareExchange(ref libInited, 1, 0) == 0) {
			try {
				// 若 PDFtoImage 已初始化，再次 Init 通常可容忍
				PdfiumNative.FPDF_InitLibrary();
			} catch (Exception ex) {
				DocViewLog.Warn($"FPDF_InitLibrary: {ex.Message}");
			}
		}
	}

	/// <summary>
	/// 渲染一页（或竖直切片）到 BitmapSource。
	/// pagePixelW/H：整页（旋转后）设备像素尺寸；clipY0/Y1：整页像素坐标系中的竖直裁剪。
	/// rotate：0/1/2/3 = 0°/90°/180°/270°（顺时针，与 pdfium 一致）。
	/// </summary>
	public BitmapSource Render(int pageIndex, int pagePixelW, int pagePixelH, int clipY0, int clipY1, double dipDpi, int rotate = 0) {
		throwif();
		if (pageIndex < 0 || pageIndex >= PageCount) return null;
		if (pagePixelW < 1) pagePixelW = 1;
		if (pagePixelH < 1) pagePixelH = 1;
		if (clipY0 < 0) clipY0 = 0;
		if (clipY1 > pagePixelH) clipY1 = pagePixelH;
		if (clipY1 <= clipY0) clipY1 = clipY0 + 1;
		rotate = ((rotate % 4) + 4) % 4;

		var tileH = clipY1 - clipY0;
		var tileW = pagePixelW;
		// 硬性上限：避免超大位图直接把进程打崩（与 PdfViewer 封顶配合）
		// 提高上限：高缩放时仍尽量按设备像素 1:1 渲染，减少拉伸模糊
		const int MAX_TILE_PIXELS = 8_000_000;
		if ((long)tileW * tileH > MAX_TILE_PIXELS) {
			var r = Math.Sqrt(MAX_TILE_PIXELS / ((double)tileW * tileH));
			var nw = Math.Max(1, (int)(tileW * r));
			var nh = Math.Max(1, (int)(tileH * r));
			// 按比例缩整页目标尺寸与 clip
			var scale = (double)nw / tileW;
			pagePixelW = Math.Max(1, (int)(pagePixelW * scale));
			pagePixelH = Math.Max(1, (int)(pagePixelH * scale));
			clipY0 = Math.Max(0, (int)(clipY0 * scale));
			clipY1 = Math.Min(pagePixelH, Math.Max(clipY0 + 1, (int)(clipY1 * scale)));
			tileW = pagePixelW;
			tileH = clipY1 - clipY0;
		}

		var page = PdfiumNative.FPDF_LoadPage(doc, pageIndex);
		if (page == IntPtr.Zero) return null;
		try {
			var bmp = PdfiumNative.FPDFBitmap_Create(tileW, tileH, 1);
			if (bmp == IntPtr.Zero) return null;
			try {
				// 白底
				PdfiumNative.FPDFBitmap_FillRect(bmp, 0, 0, tileW, tileH, 0xFFFFFFFF);
				// start_y 负值：把整页上移，使 clip 区域落到 bitmap 顶部
				var flags = PdfiumNative.FPDF_ANNOT | PdfiumNative.FPDF_LCD_TEXT;
				PdfiumNative.FPDF_RenderPageBitmap(bmp, page,
					0, -clipY0, pagePixelW, pagePixelH, rotate, flags);

				var buf = PdfiumNative.FPDFBitmap_GetBuffer(bmp);
				var stride = PdfiumNative.FPDFBitmap_GetStride(bmp);
				if (buf == IntPtr.Zero || stride < tileW * 4) return null;

				var pixels = new byte[stride * tileH];
				Marshal.Copy(buf, pixels, 0, pixels.Length);

				// pdfium BGRA → WPF Bgra32（Freeze 后可跨线程交给 UI）
				var wb = new WriteableBitmap(tileW, tileH, dipDpi, dipDpi, PixelFormats.Bgra32, null);
				wb.WritePixels(new Int32Rect(0, 0, tileW, tileH), pixels, stride, 0);
				wb.Freeze();
				return wb;
			} finally {
				PdfiumNative.FPDFBitmap_Destroy(bmp);
			}
		} finally {
			PdfiumNative.FPDF_ClosePage(page);
		}
	}

	public List<PdfCharInfo> ExtractChars(int pageIndex) {
		throwif();
		var list = new List<PdfCharInfo>();
		if (pageIndex < 0 || pageIndex >= PageCount) return list;
		var pageH = PageSizesPt[pageIndex].Height;
		var page = PdfiumNative.FPDF_LoadPage(doc, pageIndex);
		if (page == IntPtr.Zero) return list;
		try {
			var tp = PdfiumNative.FPDFText_LoadPage(page);
			if (tp == IntPtr.Zero) return list;
			try {
				var n = PdfiumNative.FPDFText_CountChars(tp);
				for (var i = 0; i < n; i++) {
					var u = PdfiumNative.FPDFText_GetUnicode(tp, i);
					if (u == 0 || u == 0xFFFF) continue;
					double left, right, bottom, top;
					PdfiumNative.FPDFText_GetCharBox(tp, i, out left, out right, out bottom, out top);
					// PDF 用户空间：原点左下，Y 向上 → 转为左上原点 Y 向下
					var t = pageH - top;
					var b = pageH - bottom;
					if (b < t) { var tmp = t; t = b; b = tmp; }
					if (right - left < 0.01 && b - t < 0.01) continue;
					var ch = u > 0xFFFF ? '?' : (char)u;
					if (char.IsControl(ch) && ch != '\t' && ch != '\n' && ch != '\r') continue;
					list.Add(new PdfCharInfo {
						Index = i,
						Char = ch,
						Left = left,
						Top = t,
						Right = right,
						Bottom = b,
					});
				}
			} finally {
				PdfiumNative.FPDFText_ClosePage(tp);
			}
		} finally {
			PdfiumNative.FPDF_ClosePage(page);
		}
		return list;
	}

	/// <summary>
	/// 枚举页内图片边界（不导出位图）。坐标：左上原点 Y 向下 pt。
	/// </summary>
	public List<PdfImageInfo> ListImageBounds(int pageIndex) {
		throwif();
		var list = new List<PdfImageInfo>();
		if (pageIndex < 0 || pageIndex >= PageCount) return list;
		var pageH = PageSizesPt[pageIndex].Height;
		var page = PdfiumNative.FPDF_LoadPage(doc, pageIndex);
		if (page == IntPtr.Zero) return list;
		try {
			var n = PdfiumNative.FPDFPage_CountObjects(page);
			for (var i = 0; i < n; i++) {
				var obj = PdfiumNative.FPDFPage_GetObject(page, i);
				if (obj == IntPtr.Zero) continue;
				if (PdfiumNative.FPDFPageObj_GetType(obj) != PdfiumNative.FPDF_PAGEOBJ_IMAGE)
					continue;
				float l, b, r, t;
				if (PdfiumNative.FPDFPageObj_GetBounds(obj, out l, out b, out r, out t) == 0)
					continue;
				var top = pageH - t;
				var bottom = pageH - b;
				if (bottom < top) { var tmp = top; top = bottom; bottom = tmp; }
				if (r - l < 2 || bottom - top < 2) continue;
				list.Add(new PdfImageInfo {
					ObjectIndex = i,
					Left = l,
					Top = top,
					Right = r,
					Bottom = bottom,
				});
			}
		} finally {
			PdfiumNative.FPDF_ClosePage(page);
		}
		return list;
	}

	/// <summary>按对象下标导出单张图片位图。</summary>
	public BitmapSource ExtractImageBitmap(int pageIndex, int objectIndex) {
		throwif();
		if (pageIndex < 0 || pageIndex >= PageCount) return null;
		var page = PdfiumNative.FPDF_LoadPage(doc, pageIndex);
		if (page == IntPtr.Zero) return null;
		try {
			var n = PdfiumNative.FPDFPage_CountObjects(page);
			if (objectIndex < 0 || objectIndex >= n) return null;
			var obj = PdfiumNative.FPDFPage_GetObject(page, objectIndex);
			if (obj == IntPtr.Zero) return null;
			if (PdfiumNative.FPDFPageObj_GetType(obj) != PdfiumNative.FPDF_PAGEOBJ_IMAGE)
				return null;
			return tryimagebitmap(doc, page, obj);
		} finally {
			PdfiumNative.FPDF_ClosePage(page);
		}
	}

	static BitmapSource tryimagebitmap(IntPtr document, IntPtr page, IntPtr imageObj) {
		IntPtr native = IntPtr.Zero;
		try {
			// 优先渲染后位图；部分 pdfium 无此 API 则回落
			try {
				native = PdfiumNative.FPDFImageObj_GetRenderedBitmap(document, page, imageObj);
			} catch (EntryPointNotFoundException) {
				native = IntPtr.Zero;
			}
			if (native == IntPtr.Zero) {
				try {
					native = PdfiumNative.FPDFImageObj_GetBitmap(imageObj);
				} catch (EntryPointNotFoundException) {
					return null;
				}
			}
			if (native == IntPtr.Zero) return null;
			return fpdfbitmaptowpf(native, 96);
		} catch (Exception ex) {
			DocViewLog.Warn($"image bitmap: {ex.Message}");
			return null;
		} finally {
			if (native != IntPtr.Zero)
				try { PdfiumNative.FPDFBitmap_Destroy(native); } catch { /* ignore */ }
		}
	}

	static BitmapSource fpdfbitmaptowpf(IntPtr bmp, double dipDpi) {
		var w = PdfiumNative.FPDFBitmap_GetWidth(bmp);
		var h = PdfiumNative.FPDFBitmap_GetHeight(bmp);
		var stride = PdfiumNative.FPDFBitmap_GetStride(bmp);
		var buf = PdfiumNative.FPDFBitmap_GetBuffer(bmp);
		if (w < 1 || h < 1 || buf == IntPtr.Zero || stride < w) return null;
		if ((long)w * h > 16_000_000) return null;

		var src = new byte[stride * h];
		Marshal.Copy(buf, src, 0, src.Length);

		// 统一转为 BGRA32
		byte[] bgra;
		int outStride;
		if (stride >= w * 4) {
			// BGRA / BGRx：补 alpha
			bgra = new byte[w * h * 4];
			outStride = w * 4;
			for (var y = 0; y < h; y++) {
				var s0 = y * stride;
				var d0 = y * outStride;
				for (var x = 0; x < w; x++) {
					var si = s0 + x * 4;
					var di = d0 + x * 4;
					bgra[di] = src[si];
					bgra[di + 1] = src[si + 1];
					bgra[di + 2] = src[si + 2];
					bgra[di + 3] = src[si + 3] == 0 ? (byte)0xFF : src[si + 3];
				}
			}
		} else if (stride >= w * 3) {
			bgra = new byte[w * h * 4];
			outStride = w * 4;
			for (var y = 0; y < h; y++) {
				var s0 = y * stride;
				var d0 = y * outStride;
				for (var x = 0; x < w; x++) {
					var si = s0 + x * 3;
					var di = d0 + x * 4;
					bgra[di] = src[si];
					bgra[di + 1] = src[si + 1];
					bgra[di + 2] = src[si + 2];
					bgra[di + 3] = 0xFF;
				}
			}
		} else {
			// 灰度
			bgra = new byte[w * h * 4];
			outStride = w * 4;
			for (var y = 0; y < h; y++) {
				var s0 = y * stride;
				var d0 = y * outStride;
				for (var x = 0; x < w; x++) {
					var g = src[s0 + x];
					var di = d0 + x * 4;
					bgra[di] = g;
					bgra[di + 1] = g;
					bgra[di + 2] = g;
					bgra[di + 3] = 0xFF;
				}
			}
		}

		var wb = new WriteableBitmap(w, h, dipDpi, dipDpi, PixelFormats.Bgra32, null);
		wb.WritePixels(new Int32Rect(0, 0, w, h), bgra, outStride, 0);
		wb.Freeze();
		return wb;
	}

	public List<PdfOutlineNode> LoadOutline() {
		throwif();
		var roots = new List<PdfOutlineNode>();
		walk(IntPtr.Zero, roots, 0);
		return roots;
	}

	void walk(IntPtr parent, List<PdfOutlineNode> list, int depth) {
		if (depth > 32) return;
		var bm = PdfiumNative.FPDFBookmark_GetFirstChild(doc, parent);
		while (bm != IntPtr.Zero) {
			var node = new PdfOutlineNode { Title = gettitle(bm) };
			filldest(bm, node);
			list.Add(node);
			walk(bm, node.Children, depth + 1);
			bm = PdfiumNative.FPDFBookmark_GetNextSibling(doc, bm);
		}
	}

	static string gettitle(IntPtr bm) {
		var len = PdfiumNative.FPDFBookmark_GetTitle(bm, null, 0);
		if (len <= 2) return "(未命名)";
		var buf = new byte[len];
		PdfiumNative.FPDFBookmark_GetTitle(bm, buf, len);
		var n = (int)len;
		if (n >= 2) n -= 2;
		if (n < 0) n = 0;
		var s = Encoding.Unicode.GetString(buf, 0, n).Trim();
		return string.IsNullOrEmpty(s) ? "(未命名)" : s;
	}

	void filldest(IntPtr bm, PdfOutlineNode node) {
		node.PageIndex = -1;
		node.HasDestY = false;
		node.DestY = 0;
		node.TopFrac = 0;
		var dest = PdfiumNative.FPDFBookmark_GetDest(doc, bm);
		if (dest == IntPtr.Zero) {
			var act = PdfiumNative.FPDFBookmark_GetAction(bm);
			if (act != IntPtr.Zero)
				dest = PdfiumNative.FPDFAction_GetDest(doc, act);
		}
		if (dest == IntPtr.Zero) return;
		if (!tryreaddest(dest, out var idx, out var hasY, out var destY, out var topFrac))
			return;
		node.PageIndex = idx;
		node.HasDestY = hasY;
		node.DestY = destY;
		node.TopFrac = topFrac;
	}

	/// <summary>
	/// 页内链接命中。pageX/Y：未旋转页坐标，左上原点 Y 向下（pt）。
	/// </summary>
	public PdfLinkHit HitLink(int pageIndex, double pageX, double pageY) {
		throwif();
		if (pageIndex < 0 || pageIndex >= PageCount) return null;
		var pageH = PageSizesPt[pageIndex].Height;
		// 转 PDF 用户空间（左下原点 Y 向上）
		var pdfX = pageX;
		var pdfY = pageH - pageY;
		var page = PdfiumNative.FPDF_LoadPage(doc, pageIndex);
		if (page == IntPtr.Zero) return null;
		try {
			var link = PdfiumNative.FPDFLink_GetLinkAtPoint(page, pdfX, pdfY);
			if (link == IntPtr.Zero) return null;
			return resolvelink(link);
		} finally {
			PdfiumNative.FPDF_ClosePage(page);
		}
	}

	PdfLinkHit resolvelink(IntPtr link) {
		var dest = PdfiumNative.FPDFLink_GetDest(doc, link);
		var action = PdfiumNative.FPDFLink_GetAction(link);
		if (dest == IntPtr.Zero && action != IntPtr.Zero)
			dest = PdfiumNative.FPDFAction_GetDest(doc, action);

		if (dest != IntPtr.Zero
			&& tryreaddest(dest, out var idx, out var hasY, out var destY, out var topFrac)) {
			return new PdfLinkHit {
				DestPageIndex = idx,
				HasDestY = hasY,
				DestY = destY,
				TopFrac = topFrac,
			};
		}

		// 外部 URI（可选：Ctrl+点击打开浏览器）
		if (action != IntPtr.Zero
			&& PdfiumNative.FPDFAction_GetType(action) == PdfiumNative.PDFACTION_URI) {
			var uri = getactionuri(action);
			if (!string.IsNullOrEmpty(uri))
				return new PdfLinkHit { DestPageIndex = -1, Uri = uri };
		}
		return null;
	}

	bool tryreaddest(IntPtr dest, out int pageIndex, out bool hasY, out float destY, out double topFrac) {
		pageIndex = -1;
		hasY = false;
		destY = 0;
		topFrac = 0;
		if (dest == IntPtr.Zero) return false;
		var idx = PdfiumNative.FPDFDest_GetDestPageIndex(doc, dest);
		if (idx < 0) return false;
		pageIndex = idx;
		// 页内目标点：把标题放到视口顶部（而非仅滚到页首）
		try {
			if (PdfiumNative.FPDFDest_GetLocationInPage(dest,
				    out _, out var hasYVal, out _, out _, out var y, out _) == 0)
				return true;
			if (hasYVal == 0) return true;
			var pageHpt = idx < PageCount ? PageSizesPt[idx].Height : 0;
			if (pageHpt < 1) return true;
			// PDF Y：左下原点向上 → 距页顶比例
			var fromTop = pageHpt - y;
			if (fromTop < 0) fromTop = 0;
			if (fromTop > pageHpt) fromTop = pageHpt;
			hasY = true;
			destY = y;
			topFrac = fromTop / pageHpt;
		} catch {
			// 部分目标类型无 XYZ，仅页码
		}
		return true;
	}

	string getactionuri(IntPtr action) {
		try {
			var len = PdfiumNative.FPDFAction_GetURIPath(doc, action, null, 0);
			if (len <= 1) return null;
			var buf = new byte[len];
			var n = PdfiumNative.FPDFAction_GetURIPath(doc, action, buf, len);
			if (n <= 1) return null;
			var end = (int)n;
			// 去掉尾部 \0
			while (end > 0 && buf[end - 1] == 0) end--;
			if (end <= 0) return null;
			var s = Encoding.UTF8.GetString(buf, 0, end).Trim();
			return string.IsNullOrEmpty(s) ? null : s;
		} catch {
			return null;
		}
	}

	void throwif() {
		if (disposed || doc == IntPtr.Zero)
			throw new ObjectDisposedException(nameof(PdfiumSession));
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		if (doc != IntPtr.Zero) {
			PdfiumNative.FPDF_CloseDocument(doc);
			doc = IntPtr.Zero;
		}
	}
}
