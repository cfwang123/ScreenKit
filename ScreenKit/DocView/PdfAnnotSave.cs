using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediaColor = System.Windows.Media.Color;

namespace ScreenKit.DocView;

/// <summary>
/// 将 PDF 页与标注合成后另存为图像 PDF（标注烧入页面）。
/// 说明：页面会栅格化，矢量/可选文字不再保留，但标注完整可见。
/// </summary>
static class PdfAnnotSave {
	const double SAVE_DPI = 144;

	public static void SaveRasterized(
		PdfiumSession session,
		System.Drawing.SizeF[] pageSizesPt,
		IList<PdfAnnotItem> annots,
		string outPath) {
		if (session == null) throw new ArgumentNullException(nameof(session));
		if (pageSizesPt == null || pageSizesPt.Length == 0)
			throw new InvalidOperationException("无页面");
		if (string.IsNullOrWhiteSpace(outPath))
			throw new ArgumentException("路径无效", nameof(outPath));

		var pages = new List<(int W, int H, byte[] Jpeg)>();
		var n = pageSizesPt.Length;
		for (var p = 0; p < n; p++) {
			var ptW = Math.Max(1, (double)pageSizesPt[p].Width);
			var ptH = Math.Max(1, (double)pageSizesPt[p].Height);
			var pixelW = Math.Max(1, (int)Math.Round(ptW * SAVE_DPI / 72.0));
			var pixelH = Math.Max(1, (int)Math.Round(ptH * SAVE_DPI / 72.0));
			if (pixelW > 4000 || pixelH > 4000) {
				var s = Math.Min(4000.0 / pixelW, 4000.0 / pixelH);
				pixelW = Math.Max(1, (int)(pixelW * s));
				pixelH = Math.Max(1, (int)(pixelH * s));
			}

			BitmapSource pageBmp = null;
			PdfIo.WithLock(() => {
				pageBmp = session.Render(p, pixelW, pixelH, 0, pixelH, SAVE_DPI, 0);
			});
			if (pageBmp == null)
				throw new InvalidOperationException($"渲染第 {p + 1} 页失败");

			var composed = compose(pageBmp, ptW, ptH, annots, p);
			var jpeg = tojpeg(composed, 90);
			pages.Add((composed.PixelWidth, composed.PixelHeight, jpeg));
			DocViewLog.Info($"PdfAnnotSave page={p + 1}/{n} {composed.PixelWidth}x{composed.PixelHeight} jpeg={jpeg.Length}");
		}

		var dir = Path.GetDirectoryName(outPath);
		var tmp = Path.Combine(string.IsNullOrEmpty(dir) ? Path.GetTempPath() : dir,
			Path.GetFileNameWithoutExtension(outPath) + ".~annot.pdf.tmp");
		try {
			if (File.Exists(tmp)) File.Delete(tmp);
			writeimagepdf(tmp, pages);
			if (File.Exists(outPath)) {
				var bak = outPath + ".bak";
				try { if (File.Exists(bak)) File.Delete(bak); } catch { /* ignore */ }
				try {
					File.Replace(tmp, outPath, bak);
					try { File.Delete(bak); } catch { /* ignore */ }
				} catch {
					File.Copy(tmp, outPath, true);
					try { File.Delete(tmp); } catch { /* ignore */ }
				}
			} else {
				File.Move(tmp, outPath);
			}
		} catch {
			try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore */ }
			throw;
		}
	}

	static BitmapSource compose(BitmapSource pageBmp, double pagePtW, double pagePtH,
		IList<PdfAnnotItem> annots, int page) {
		var w = pageBmp.PixelWidth;
		var h = pageBmp.PixelHeight;
		var dv = new DrawingVisual();
		using (var dc = dv.RenderOpen()) {
			dc.DrawImage(pageBmp, new Rect(0, 0, w, h));
			var sx = w / pagePtW;
			var sy = h / pagePtH;
			if (annots == null) { /* none */ }
			else {
				foreach (var it in annots) {
					if (it == null || it.Page != page) continue;
					try { drawannot(dc, it, sx, sy); }
					catch (Exception ex) {
						DocViewLog.Warn($"PdfAnnotSave draw page={page} kind={it.KindName}: {ex.Message}");
					}
				}
			}
		}
		var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
		rtb.Render(dv);
		rtb.Freeze();
		return rtb;
	}

	static void drawannot(DrawingContext dc, PdfAnnotItem it, double sx, double sy) {
		var col = it.Color;
		var strokePx = Math.Max(0.8, it.Stroke * Math.Min(sx, sy));

		switch (it.Kind) {
			case PdfAnnotKind.Ink:
			case PdfAnnotKind.Highlight: {
				if (it.Points == null || it.Points.Count < 2) return;
				var geo = new StreamGeometry();
				using (var ctx = geo.Open()) {
					var first = true;
					foreach (var p in it.Points) {
						var pt = new Point(p.X * sx, p.Y * sy);
						if (first) {
							ctx.BeginFigure(pt, false, false);
							first = false;
						} else {
							ctx.LineTo(pt, true, false);
						}
					}
				}
				geo.Freeze();
				var penW = it.Kind == PdfAnnotKind.Highlight
					? Math.Max(6, it.Stroke * Math.Min(sx, sy))
					: strokePx;
				var c = col;
				if (it.Kind == PdfAnnotKind.Highlight) {
					var a = (byte)(it.Opacity > 0 && it.Opacity < 1 ? 255 * it.Opacity : 0x90);
					c = MediaColor.FromArgb(a, col.R, col.G, col.B);
				} else if (it.Opacity > 0 && it.Opacity < 1) {
					c = MediaColor.FromArgb((byte)(255 * it.Opacity), col.R, col.G, col.B);
				}
				var pen = new Pen(new SolidColorBrush(c), penW) {
					StartLineCap = PenLineCap.Round,
					EndLineCap = PenLineCap.Round,
					LineJoin = PenLineJoin.Round,
				};
				dc.DrawGeometry(null, pen, geo);
				break;
			}
			case PdfAnnotKind.Line:
			case PdfAnnotKind.Arrow: {
				var p1 = new Point(it.X * sx, it.Y * sy);
				var p2 = new Point(it.X2 * sx, it.Y2 * sy);
				var pen = new Pen(new SolidColorBrush(col), strokePx) {
					StartLineCap = PenLineCap.Round,
					EndLineCap = PenLineCap.Round,
				};
				dc.DrawLine(pen, p1, p2);
				if (it.Kind == PdfAnnotKind.Arrow) {
					var dx = p2.X - p1.X;
					var dy = p2.Y - p1.Y;
					var len = Math.Sqrt(dx * dx + dy * dy);
					if (len > 1e-3) {
						var ux = dx / len;
						var uy = dy / len;
						var ah = 12.0 * Math.Min(sx, sy) / (SAVE_DPI / 72.0 * 0.5);
						ah = Math.Max(8, Math.Min(28, 10 * Math.Min(sx, sy)));
						var aw = ah * 0.45;
						var bx = p2.X - ux * ah;
						var by = p2.Y - uy * ah;
						var px = -uy * aw;
						var py = ux * aw;
						var tri = new PathGeometry();
						var fig = new PathFigure { StartPoint = p2, IsClosed = true };
						fig.Segments.Add(new LineSegment(new Point(bx + px, by + py), true));
						fig.Segments.Add(new LineSegment(new Point(bx - px, by - py), true));
						tri.Figures.Add(fig);
						tri.Freeze();
						dc.DrawGeometry(new SolidColorBrush(col), null, tri);
					}
				}
				break;
			}
			case PdfAnnotKind.Rect: {
				var r = new Rect(it.X * sx, it.Y * sy, Math.Max(1, it.W * sx), Math.Max(1, it.H * sy));
				var fill = new SolidColorBrush(MediaColor.FromArgb(0x28, col.R, col.G, col.B));
				dc.DrawRectangle(fill, new Pen(new SolidColorBrush(col), strokePx), r);
				break;
			}
			case PdfAnnotKind.Ellipse: {
				var r = new Rect(it.X * sx, it.Y * sy, Math.Max(1, it.W * sx), Math.Max(1, it.H * sy));
				var fill = new SolidColorBrush(MediaColor.FromArgb(0x28, col.R, col.G, col.B));
				dc.DrawEllipse(fill, new Pen(new SolidColorBrush(col), strokePx),
					new Point(r.X + r.Width / 2, r.Y + r.Height / 2),
					r.Width / 2, r.Height / 2);
				break;
			}
			case PdfAnnotKind.Text: {
				var rx = it.X * sx;
				var ry = it.Y * sy;
				var rw = Math.Max(1, it.W * sx);
				var rh = Math.Max(1, it.H * sy);
				var rect = new Rect(rx, ry, rw, rh);
				// 半透明底
				dc.DrawRectangle(new SolidColorBrush(MediaColor.FromArgb(0x28, 0xFF, 0xFF, 0xFF)), null, rect);
				if (string.IsNullOrEmpty(it.Text)) break;
				var fs = Math.Max(6, it.FontSize * SAVE_DPI / 72.0);
				var fam = string.IsNullOrWhiteSpace(it.FontName)
					? "Microsoft YaHei UI, Segoe UI, SimSun"
					: it.FontName + ", Microsoft YaHei UI, Segoe UI";
				Typeface tf;
				try {
					tf = new Typeface(new FontFamily(fam), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
				} catch {
					tf = new Typeface("Segoe UI");
				}
				var norm = (it.Text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
				var ft = new FormattedText(
					norm, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
					tf, fs, new SolidColorBrush(col), 1.0);
				ft.MaxTextWidth = Math.Max(1, rw - 4);
				dc.PushClip(new RectangleGeometry(rect));
				dc.DrawText(ft, new Point(rx + 2, ry + 1));
				dc.Pop();
				break;
			}
			case PdfAnnotKind.Note: {
				// 小气泡 + 右侧正文框（烧入后内容可见）
				var side = Math.Max(14, 18 * Math.Min(sx, sy));
				var cx = it.X * sx + side / 2;
				var cy = it.Y * sy + side / 2;
				var bubble = new SolidColorBrush(MediaColor.FromRgb(0xFF, 0xF5, 0x9D));
				var border = new Pen(new SolidColorBrush(MediaColor.FromRgb(0xF9, 0xA8, 0x25)), 1.5);
				dc.DrawEllipse(bubble, border, new Point(cx, cy), side / 2, side / 2);
				// 简易「…」
				var dotBrush = new SolidColorBrush(MediaColor.FromRgb(0x6B, 0x72, 0x80));
				dc.DrawEllipse(dotBrush, null, new Point(cx - 4, cy), 1.6, 1.6);
				dc.DrawEllipse(dotBrush, null, new Point(cx, cy), 1.6, 1.6);
				dc.DrawEllipse(dotBrush, null, new Point(cx + 4, cy), 1.6, 1.6);

				if (!string.IsNullOrWhiteSpace(it.Text)) {
					var fs = Math.Max(7, it.FontSize * SAVE_DPI / 72.0 * 0.9);
					var fam = string.IsNullOrWhiteSpace(it.FontName)
						? "Microsoft YaHei UI, Segoe UI"
						: it.FontName + ", Microsoft YaHei UI";
					Typeface tf;
					try {
						tf = new Typeface(new FontFamily(fam), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
					} catch {
						tf = new Typeface("Segoe UI");
					}
					// 约 160pt 宽的正文框
					var boxW = 160.0 * sx;
					var norm = it.Text.Replace("\r\n", "\n").Replace('\r', '\n');
					var ft = new FormattedText(
						norm, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
						tf, fs, new SolidColorBrush(MediaColor.FromRgb(0x11, 0x18, 0x27)), 1.0);
					ft.MaxTextWidth = Math.Max(40, boxW - 10);
					var boxH = Math.Min(220.0 * sy, Math.Max(24, ft.Height + 12));
					var bx = it.X * sx + side + 6;
					var by = it.Y * sy;
					var box = new Rect(bx, by, boxW, boxH);
					dc.DrawRoundedRectangle(
						new SolidColorBrush(MediaColor.FromRgb(0xFF, 0xF8, 0xE1)),
						new Pen(new SolidColorBrush(MediaColor.FromRgb(0xF9, 0xA8, 0x25)), 1),
						box, 4, 4);
					dc.PushClip(new RectangleGeometry(box));
					dc.DrawText(ft, new Point(bx + 5, by + 4));
					dc.Pop();
				}
				break;
			}
		}
	}

	static byte[] tojpeg(BitmapSource src, int quality) {
		BitmapSource bgr = src;
		if (src.Format != PixelFormats.Bgr24 && src.Format != PixelFormats.Bgra32) {
			bgr = new FormatConvertedBitmap(src, PixelFormats.Bgr24, null, 0);
			bgr.Freeze();
		}
		var enc = new JpegBitmapEncoder { QualityLevel = Math.Max(50, Math.Min(100, quality)) };
		enc.Frames.Add(BitmapFrame.Create(bgr));
		using var ms = new MemoryStream();
		enc.Save(ms);
		return ms.ToArray();
	}

	static void writeimagepdf(string path, List<(int W, int H, byte[] Jpeg)> pages) {
		using var fs = File.Create(path);
		var offsets = new List<long>();
		void w(string s) {
			var b = Encoding.ASCII.GetBytes(s);
			fs.Write(b, 0, b.Length);
		}

		var body = new MemoryStream();
		var bodyOff = new List<long>();
		void boff() => bodyOff.Add(body.Position);
		void bw(string s) {
			var b = Encoding.ASCII.GetBytes(s);
			body.Write(b, 0, b.Length);
		}
		void braw(byte[] b) => body.Write(b, 0, b.Length);

		var kids = new List<int>();
		var obj = 3;
		for (var i = 0; i < pages.Count; i++) {
			var (pw, ph, jpeg) = pages[i];
			var pageObj = obj++;
			var imgObj = obj++;
			var contObj = obj++;
			kids.Add(pageObj);

			boff();
			bw($"{pageObj} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pw} {ph}] ");
			bw($"/Resources << /XObject << /Im{i} {imgObj} 0 R >> >> ");
			bw($"/Contents {contObj} 0 R >>\nendobj\n");

			boff();
			bw($"{imgObj} 0 obj\n<< /Type /XObject /Subtype /Image /Width {pw} /Height {ph} ");
			bw("/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode ");
			bw($"/Length {jpeg.Length} >>\nstream\n");
			braw(jpeg);
			bw("\nendstream\nendobj\n");

			var cont = $"q {pw} 0 0 {ph} 0 0 cm /Im{i} Do Q\n";
			var contBytes = Encoding.ASCII.GetBytes(cont);
			boff();
			bw($"{contObj} 0 obj\n<< /Length {contBytes.Length} >>\nstream\n");
			braw(contBytes);
			bw("endstream\nendobj\n");
		}

		w("%PDF-1.4\n%âãÏÓ\n");
		offsets.Add(fs.Position);
		w("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
		offsets.Add(fs.Position);
		var kidsStr = new StringBuilder("[");
		foreach (var k in kids) kidsStr.Append(k).Append(" 0 R ");
		kidsStr.Append(']');
		w($"2 0 obj\n<< /Type /Pages /Kids {kidsStr} /Count {pages.Count} >>\nendobj\n");

		var baseOff = fs.Position;
		for (var i = 0; i < bodyOff.Count; i++)
			offsets.Add(baseOff + bodyOff[i]);
		body.Position = 0;
		body.CopyTo(fs);

		var xrefPos = fs.Position;
		w($"xref\n0 {offsets.Count + 1}\n");
		w("0000000000 65535 f \n");
		foreach (var o in offsets)
			w($"{o:D10} 00000 n \n");
		w($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\n");
		w($"startxref\n{xrefPos}\n%%EOF\n");
	}
}
