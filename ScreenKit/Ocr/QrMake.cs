using ZXingCpp;

namespace ScreenKit;

/// <summary>二维码生成：ZXingCpp 写码，输出 WPF 位图（不依赖 OpenCV）。</summary>
static class QrMake {
	public static BitmapSource Encode(string text, int scale = 8) {
		if (string.IsNullOrEmpty(text)) throw new ArgumentException("empty");
		if (scale < 1) scale = 1;
		if (scale > 32) scale = 32;
		using var creator = new BarcodeCreator(BarcodeFormat.QRCode);
		using var barcode = creator.From(text);
		if (barcode == null || !barcode.IsValid)
			throw new InvalidOperationException(barcode?.ErrorMsg ?? "QR 生成失败");
		using var img = barcode.ToImage(new WriterOptions {
			Scale = scale,
			AddQuietZones = true,
		});
		if (img == null || img.Width <= 0 || img.Height <= 0)
			throw new InvalidOperationException("QR 图像为空");
		return tobitmap(img);
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
