using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ScreenKit;

/// <summary>把抓到的 BGRA 收成编码器要的尺寸。未缩放时不复制。</summary>
static class RecordBgra {
	public static byte[] Tight(byte[] src, int stride, int w, int h, int dw, int dh) {
		if (src == null) return null;
		if (w == dw && h == dh && stride == w * 4) return src;
		using var held = new Held(src, stride, w, h, dw, dh);
		var bmp = held.Bmp;
		var rect = new Rectangle(0, 0, dw, dh);
		var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		try {
			var tight = dw * 4;
			var dst = new byte[tight * dh];
			var srcStride = data.Stride;
			if (srcStride == tight)
				Marshal.Copy(data.Scan0, dst, 0, dst.Length);
			else {
				for (var y = 0; y < dh; y++)
					Marshal.Copy(data.Scan0 + y * srcStride, dst, y * tight, tight);
			}
			return dst;
		}
		finally {
			bmp.UnlockBits(data);
		}
	}

	public static byte[] Jpeg(byte[] src, int stride, int w, int h, int dw, int dh, int quality) {
		using var held = new Held(src, stride, w, h, dw, dh);
		using var ms = new MemoryStream();
		var enc = jpeg();
		using var ep = new EncoderParameters(1);
		ep.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);
		held.Bmp.Save(ms, enc, ep);
		return ms.ToArray();
	}

	static ImageCodecInfo jpeg() {
		foreach (var c in ImageCodecInfo.GetImageEncoders())
			if (c.FormatID == ImageFormat.Jpeg.Guid) return c;
		throw new InvalidOperationException("系统没有 JPEG 编码器");
	}

	sealed class Held : IDisposable {
		GCHandle pin;
		Bitmap src;
		public Bitmap Bmp { get; }

		public Held(byte[] bgra, int stride, int w, int h, int dw, int dh) {
			pin = GCHandle.Alloc(bgra, GCHandleType.Pinned);
			src = new Bitmap(w, h, stride, System.Drawing.Imaging.PixelFormat.Format32bppArgb, pin.AddrOfPinnedObject());
			if (w == dw && h == dh) {
				Bmp = src;
				src = null;
				return;
			}
			var dst = new Bitmap(dw, dh, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
			using (var g = Graphics.FromImage(dst)) {
				g.InterpolationMode = InterpolationMode.Bilinear;
				g.DrawImage(src, 0, 0, dw, dh);
			}
			Bmp = dst;
		}

		public void Dispose() {
			if (!ReferenceEquals(Bmp, src)) Bmp?.Dispose();
			src?.Dispose();
			if (pin.IsAllocated) pin.Free();
		}
	}
}
