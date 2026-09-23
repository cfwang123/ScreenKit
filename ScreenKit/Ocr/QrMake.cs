using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using ZXingCpp;

namespace ScreenKit;

/// <summary>二维码 / 条码生成：ZXingCpp 写码，图下方一行原文。</summary>
static class QrMake {
	static bool encReady;

	public static readonly (string Id, string Title)[] Formats = {
		("qr", "QR Code"),
		("datamatrix", "Data Matrix"),
		("code128", "Code 128"),
		("code39", "Code 39"),
		("ean13", "EAN-13"),
		("ean8", "EAN-8"),
		("upca", "UPC-A"),
	};

	public static BitmapSource Encode(string text, int scale = 8) =>
		Encode(text, "qr", "utf8", scale, caption: true);

	public static BitmapSource Encode(string text, string format, string encoding, int scale, bool caption) {
		if (string.IsNullOrEmpty(text)) throw new ArgumentException("empty");
		if (scale < 1) scale = 1;
		if (scale > 32) scale = 32;
		ensureenc();
		var fmt = parsefmt(format);
		var gbk = isgbk(encoding);
		var linear = islinear(fmt);
		using var creator = new BarcodeCreator(fmt);
		ZXingCpp.Barcode barcode;
		if (linear)
			barcode = creator.From(text);
		else {
			var bytes = gbk ? Encoding.GetEncoding(936).GetBytes(text) : Encoding.UTF8.GetBytes(text);
			barcode = creator.From(bytes);
		}
		using (barcode) {
			if (barcode == null || !barcode.IsValid)
				throw new InvalidOperationException(barcode?.ErrorMsg ?? "生成失败");
			using var img = barcode.ToImage(new WriterOptions {
				Scale = linear ? Math.Max(2, scale / 2) : scale,
				AddQuietZones = true,
				AddHRT = false,
			});
			if (img == null || img.Width <= 0 || img.Height <= 0)
				throw new InvalidOperationException("条码图像为空");
			var bmp = tobitmap(img);
			return caption ? withcaption(bmp, text) : bmp;
		}
	}

	static void ensureenc() {
		if (encReady) return;
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		encReady = true;
	}

	static bool isgbk(string encoding) {
		var s = (encoding ?? "").Trim().ToLowerInvariant();
		return s is "gbk" or "gb2312" or "cp936" or "936";
	}

	static bool islinear(BarcodeFormat fmt) =>
		fmt == BarcodeFormat.Code128 || fmt == BarcodeFormat.Code39
		|| fmt == BarcodeFormat.EAN13 || fmt == BarcodeFormat.EAN8
		|| fmt == BarcodeFormat.UPCA;

	static BarcodeFormat parsefmt(string id) {
		var s = (id ?? "qr").Trim().ToLowerInvariant();
		return s switch {
			"datamatrix" or "dm" => BarcodeFormat.DataMatrix,
			"code128" or "128" => BarcodeFormat.Code128,
			"code39" or "39" => BarcodeFormat.Code39,
			"ean13" => BarcodeFormat.EAN13,
			"ean8" => BarcodeFormat.EAN8,
			"upca" or "upc" => BarcodeFormat.UPCA,
			_ => BarcodeFormat.QRCode,
		};
	}

	static BitmapSource withcaption(BitmapSource bmp, string text) {
		var w = Math.Max(1, bmp.PixelWidth);
		var h = Math.Max(1, bmp.PixelHeight);
		var line = (text ?? "").Replace("\r", " ").Replace("\n", " ");
		if (line.Length > 80) line = line.Substring(0, 79) + "…";
		var dpi = 96.0;
		var ft = new FormattedText(
			line,
			CultureInfo.CurrentCulture,
			FlowDirection.LeftToRight,
			new Typeface("Microsoft YaHei UI"),
			14,
			Brushes.Black, dpi);
		ft.MaxTextWidth = w;
		ft.MaxLineCount = 1;
		ft.Trimming = TextTrimming.CharacterEllipsis;
		var pad = 10.0;
		var capH = Math.Ceiling(ft.Height) + pad * 2;
		var totalH = (int)Math.Ceiling(h + capH);
		var dv = new DrawingVisual();
		using (var dc = dv.RenderOpen()) {
			dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, totalH));
			dc.DrawImage(bmp, new Rect(0, 0, w, h));
			var x = Math.Max(0, (w - ft.Width) / 2);
			dc.DrawText(ft, new Point(x, h + pad));
		}
		var rtb = new RenderTargetBitmap(w, totalH, dpi, dpi, PixelFormats.Pbgra32);
		rtb.Render(dv);
		rtb.Freeze();
		return rtb;
	}

	static BitmapSource tobitmap(ZXingCpp.Image img) {
		var w = img.Width;
		var h = img.Height;
		var raw = img.ToArray();
		if (raw == null || raw.Length == 0)
			throw new InvalidOperationException("QR 像素为空");
		byte[] bgra;
		var fmt = img.Format;
		if (fmt == ImageFormat.Lum) {
			if (raw.Length < w * h)
				throw new InvalidOperationException("QR Lum 长度不足");
			bgra = new byte[w * h * 4];
			for (var i = 0; i < w * h; i++) {
				var v = raw[i];
				var o = i * 4;
				bgra[o] = v;
				bgra[o + 1] = v;
				bgra[o + 2] = v;
				bgra[o + 3] = 255;
			}
		}
		else if (fmt == ImageFormat.BGR) {
			if (raw.Length < w * h * 3)
				throw new InvalidOperationException("QR BGR 长度不足");
			bgra = new byte[w * h * 4];
			for (var i = 0; i < w * h; i++) {
				var s = i * 3;
				var o = i * 4;
				bgra[o] = raw[s];
				bgra[o + 1] = raw[s + 1];
				bgra[o + 2] = raw[s + 2];
				bgra[o + 3] = 255;
			}
		}
		else if (fmt == ImageFormat.RGB) {
			if (raw.Length < w * h * 3)
				throw new InvalidOperationException("QR RGB 长度不足");
			bgra = new byte[w * h * 4];
			for (var i = 0; i < w * h; i++) {
				var s = i * 3;
				var o = i * 4;
				bgra[o] = raw[s + 2];
				bgra[o + 1] = raw[s + 1];
				bgra[o + 2] = raw[s];
				bgra[o + 3] = 255;
			}
		}
		else
			throw new InvalidOperationException("不支持的 QR 像素格式: " + fmt);
		var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
		bmp.Freeze();
		return bmp;
	}
}
