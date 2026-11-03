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
/// 将 PDF 页渲染结果与编辑叠加层合成，写出多页图像 PDF（可靠、兼容性好）。
/// 说明：会栅格化页面，矢量/可选文字不再保留，但叠加编辑完整可见。
/// </summary>
static class PdfEditSave {
	const double SAVE_DPI = 144; // 1.5×96，清晰度与体积折中

	public static void SaveRasterized(
		PdfiumSession session,
		System.Drawing.SizeF[] pageSizesPt,
		IList<PdfEditItem> edits,
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

			var composed = compose(pageBmp, ptW, ptH, edits, p);
			var jpeg = tojpeg(composed, 90);
			pages.Add((composed.PixelWidth, composed.PixelHeight, jpeg));
			DocViewLog.Info($"PdfEditSave page={p + 1}/{n} {composed.PixelWidth}x{composed.PixelHeight} jpeg={jpeg.Length}");
		}

		var dir = Path.GetDirectoryName(outPath);
		var tmp = Path.Combine(string.IsNullOrEmpty(dir) ? Path.GetTempPath() : dir,
			Path.GetFileNameWithoutExtension(outPath) + ".~pdfedit.tmp");
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

	static BitmapSource compose(BitmapSource pageBmp, double pagePtW, double pagePtH, IList<PdfEditItem> edits, int page) {
		var w = pageBmp.PixelWidth;
		var h = pageBmp.PixelHeight;
		var dv = new DrawingVisual();
		using (var dc = dv.RenderOpen()) {
			dc.DrawImage(pageBmp, new Rect(0, 0, w, h));
			var sx = w / pagePtW;
			var sy = h / pagePtH;
			if (edits != null) {
				foreach (var it in edits) {
					if (it == null || it.Page != page) continue;
					var rx = it.X * sx;
					var ry = it.Y * sy;
					var rw = Math.Max(1, it.W * sx);
					var rh = Math.Max(1, it.H * sy);
					var rect = new Rect(rx, ry, rw, rh);
					if (it.Kind == PdfEditKind.Whiteout || it.BackColor.HasValue) {
						var bg = it.BackColor ?? Colors.White;
						dc.DrawRectangle(new SolidColorBrush(bg), null, rect);
					}
					if (it.Kind == PdfEditKind.Image && it.ImagePng != null && it.ImagePng.Length > 0) {
						try {
							var img = loadpng(it.ImagePng);
							if (img != null) dc.DrawImage(img, rect);
						} catch { /* ignore */ }
					}
					if (it.Kind == PdfEditKind.Text && !string.IsNullOrEmpty(it.Text)) {
						var fs = Math.Max(6, it.FontSizePt * SAVE_DPI / 72.0);
						var weight = it.Bold ? FontWeights.Bold : FontWeights.Normal;
						var style = it.Italic ? FontStyles.Italic : FontStyles.Normal;
						var fam = string.IsNullOrWhiteSpace(it.FontName)
							? "Microsoft YaHei UI, Segoe UI, SimSun"
							: it.FontName + ", Microsoft YaHei UI, Segoe UI";
						Typeface tf;
						try {
							tf = new Typeface(new FontFamily(fam), style, weight, FontStretches.Normal);
						} catch {
							tf = new Typeface("Segoe UI");
						}
						var norm = (it.Text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
						var ft = new FormattedText(
							norm, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
							tf, fs, new SolidColorBrush(it.ForeColor), 1.0);
						ft.MaxTextWidth = Math.Max(1, rw - 2);
						dc.PushClip(new RectangleGeometry(rect));
						dc.DrawText(ft, new Point(rx + 1, ry + 1));
						dc.Pop();
					}
				}
			}
		}
		var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
		rtb.Render(dv);
		rtb.Freeze();
		return rtb;
	}

	static BitmapSource loadpng(byte[] png) {
		using var ms = new MemoryStream(png);
		var dec = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
		var f = dec.Frames[0];
		f.Freeze();
		return f;
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

	/// <summary>最简图像 PDF：每页一张 JPEG。</summary>
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
