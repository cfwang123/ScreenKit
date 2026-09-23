using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using GdiBmp = System.Drawing.Bitmap;
using GdiImg = System.Drawing.Image;
using GdiGfx = System.Drawing.Graphics;

namespace ScreenKit;

/// <summary>批量图片格式转换：缩放 / 旋转 / 镜像 / 输出路径。</summary>
static class ImgConvert {
	const int MAXFILES = 2000;

	public static string NormFmt(string fmt) {
		var f = (fmt ?? "jpg").Trim().ToLowerInvariant();
		if (f is "png") return "png";
		if (f is "bmp") return "bmp";
		return "jpg";
	}

	public static string ExtOf(string fmt) => "." + NormFmt(fmt);

	public static List<string> CollectImages(IEnumerable<string> paths) {
		var r = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (paths == null) return r;
		foreach (var p in paths)
			add(p);
		return r;

		void add(string p) {
			if (string.IsNullOrWhiteSpace(p) || r.Count >= MAXFILES) return;
			try { p = Path.GetFullPath(p); } catch { return; }
			if (Directory.Exists(p)) {
				string[] files;
				try { files = Directory.GetFiles(p, "*", SearchOption.AllDirectories); }
				catch { return; }
				foreach (var f in files) {
					if (r.Count >= MAXFILES) return;
					addfile(f);
				}
				return;
			}
			addfile(p);
		}

		void addfile(string f) {
			if (r.Count >= MAXFILES) return;
			if (string.IsNullOrWhiteSpace(f) || !File.Exists(f)) return;
			if (!ImageUtil.IsImagePath(f)) return;
			string full;
			try { full = Path.GetFullPath(f); } catch { return; }
			if (!seen.Add(full)) return;
			r.Add(full);
		}
	}

	public static string SaveTemp(BitmapSource src) {
		var path = TmpStore.NewPath("imgconv", ".png");
		ImageUtil.Savefile(src, path, 92);
		return path;
	}

	public static string MakeOutPath(string src, string fmt, bool beside, string customDir, ISet<string> reserved) {
		fmt = NormFmt(fmt);
		var ext = "." + fmt;
		var name = Path.GetFileNameWithoutExtension(src);
		if (string.IsNullOrWhiteSpace(name)) name = "image";
		string dir;
		if (beside) {
			var srcDir = Path.GetDirectoryName(src);
			if (string.IsNullOrEmpty(srcDir))
				srcDir = AppDomain.CurrentDomain.BaseDirectory;
			dir = Path.Combine(srcDir, "output");
		}
		else {
			dir = (customDir ?? "").Trim();
			if (string.IsNullOrEmpty(dir))
				throw new InvalidOperationException(Loc.T("imgconv.nodir"));
		}
		Directory.CreateDirectory(dir);
		var path = Path.Combine(dir, name + ext);
		path = unique(path, reserved);
		reserved?.Add(path);
		return path;
	}

	public static void ConvertOne(string src, string dst, string fmt, int quality,
		bool maxEn, int maxW, int maxH, int rotate, bool mirror, CancellationToken ct) {
		ConvertOne(src, dst, fmt, quality, maxEn, maxW, maxH, rotate, mirror, ct,
			keepOrigEn: false, keepOrigPct: 80);
	}

	/// <returns>true 表示因体积收益不足而写出原文件。</returns>
	public static bool ConvertOne(string src, string dst, string fmt, int quality,
		bool maxEn, int maxW, int maxH, int rotate, bool mirror, CancellationToken ct,
		bool keepOrigEn, int keepOrigPct) {
		var enc = encodex(src, fmt, quality, maxEn, maxW, maxH, rotate, mirror, ct);
		var dir = Path.GetDirectoryName(dst);
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		var origLen = 0L;
		try { origLen = new FileInfo(src).Length; } catch { }
		var geom = rotate != 0 || mirror || enc.SrcW != enc.OutW || enc.SrcH != enc.OutH;
		if (KeepOriginal(origLen, enc.Bytes.Length, keepOrigEn, keepOrigPct, geom)) {
			var keep = keeppath(src, dst);
			if (!string.Equals(keep, src, StringComparison.OrdinalIgnoreCase))
				File.Copy(src, keep, overwrite: false);
			return true;
		}
		File.WriteAllBytes(dst, enc.Bytes);
		return false;
	}

	/// <summary>压缩后体积 ≥ 原图的 pct% 则用原图；有旋转/镜像/实际缩放时仍用新图。</summary>
	public static bool KeepOriginal(long origLen, int newLen, bool enabled, int pct, bool geomChanged) {
		if (!enabled || geomChanged || origLen <= 0 || newLen < 0) return false;
		pct = Compat.Clamp(pct, 1, 100);
		return newLen * 100L >= origLen * (long)pct;
	}

	static string keeppath(string src, string dst) {
		var dir = Path.GetDirectoryName(dst) ?? "";
		var name = Path.GetFileName(src);
		if (string.IsNullOrEmpty(name)) name = Path.GetFileName(dst);
		var path = Path.Combine(dir, name);
		if (string.Equals(path, src, StringComparison.OrdinalIgnoreCase))
			return Path.Combine(dir, Path.GetFileNameWithoutExtension(name) + "_orig" + Path.GetExtension(name));
		return unique(path, null);
	}

	/// <summary>按目标格式实际编码（JPG 质量会进码流），供预览与落盘共用。</summary>
	public static byte[] Encode(string src, string fmt, int quality,
		bool maxEn, int maxW, int maxH, int rotate, bool mirror, CancellationToken ct) {
		return encodex(src, fmt, quality, maxEn, maxW, maxH, rotate, mirror, ct).Bytes;
	}

	sealed class Encoded {
		public byte[] Bytes;
		public int SrcW, SrcH, OutW, OutH;
	}

	static Encoded encodex(string src, string fmt, int quality,
		bool maxEn, int maxW, int maxH, int rotate, bool mirror, CancellationToken ct) {
		if (string.IsNullOrWhiteSpace(src) || !File.Exists(src))
			throw new FileNotFoundException(src);
		ct.ThrowIfCancellationRequested();
		fmt = NormFmt(fmt);
		quality = Compat.Clamp(quality <= 0 ? 60 : quality, 1, 100);
		rotate = normrot(rotate);
		Exception gdiEx = null;
		try {
			return encodegdi(src, fmt, quality, maxEn, maxW, maxH, rotate, mirror, ct);
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex) { gdiEx = ex; }
		try {
			return encodewpf(src, fmt, quality, maxEn, maxW, maxH, rotate, mirror, ct);
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception wpfEx) {
			throw new InvalidOperationException(wpfEx.Message ?? gdiEx?.Message ?? src, wpfEx);
		}
	}

	static Encoded encodegdi(string src, string fmt, int quality,
		bool maxEn, int maxW, int maxH, int rotate, bool mirror, CancellationToken ct) {
		using var loaded = new GdiBmp(src);
		var srcW = loaded.Width;
		var srcH = loaded.Height;
		using var img = new GdiBmp(loaded);
		ct.ThrowIfCancellationRequested();
		var xf = toflip(rotate, mirror);
		if (xf != RotateFlipType.RotateNoneFlipNone)
			img.RotateFlip(xf);
		GdiImg toSave = img;
		GdiBmp resized = null;
		try {
			if (maxEn) {
				maxW = Math.Max(16, maxW);
				maxH = Math.Max(16, maxH);
				var w = img.Width;
				var h = img.Height;
				if (w > maxW || h > maxH) {
					var s = Math.Min((double)maxW / w, (double)maxH / h);
					var nw = Math.Max(1, (int)Math.Round(w * s));
					var nh = Math.Max(1, (int)Math.Round(h * s));
					resized = new GdiBmp(nw, nh);
					using var g = GdiGfx.FromImage(resized);
					g.InterpolationMode = InterpolationMode.HighQualityBicubic;
					g.PixelOffsetMode = PixelOffsetMode.HighQuality;
					g.DrawImage(img, 0, 0, nw, nh);
					toSave = resized;
				}
			}
			ct.ThrowIfCancellationRequested();
			using var ms = new MemoryStream();
			savegdi(toSave, ms, fmt, quality);
			return new Encoded {
				Bytes = ms.ToArray(),
				SrcW = srcW,
				SrcH = srcH,
				OutW = toSave.Width,
				OutH = toSave.Height,
			};
		}
		finally {
			resized?.Dispose();
		}
	}

	static Encoded encodewpf(string src, string fmt, int quality,
		bool maxEn, int maxW, int maxH, int rotate, bool mirror, CancellationToken ct) {
		var bmp = loadwpf(src);
		var srcW = bmp.PixelWidth;
		var srcH = bmp.PixelHeight;
		ct.ThrowIfCancellationRequested();
		bmp = transformwpf(bmp, rotate, mirror);
		if (maxEn)
			bmp = ImageUtil.FitMaxSize(bmp, maxW, maxH);
		if (!bmp.IsFrozen) {
			var wb = new WriteableBitmap(bmp);
			wb.Freeze();
			bmp = wb;
		}
		ct.ThrowIfCancellationRequested();
		BitmapEncoder enc = fmt switch {
			"jpg" => new JpegBitmapEncoder { QualityLevel = quality },
			"bmp" => new BmpBitmapEncoder(),
			_ => new PngBitmapEncoder(),
		};
		enc.Frames.Add(BitmapFrame.Create(bmp));
		using var ms = new MemoryStream();
		enc.Save(ms);
		return new Encoded {
			Bytes = ms.ToArray(),
			SrcW = srcW,
			SrcH = srcH,
			OutW = bmp.PixelWidth,
			OutH = bmp.PixelHeight,
		};
	}

	static BitmapSource loadwpf(string path) {
		try {
			using var fs = File.OpenRead(path);
			var bi = new BitmapImage();
			bi.BeginInit();
			bi.CacheOption = BitmapCacheOption.OnLoad;
			bi.StreamSource = fs;
			bi.EndInit();
			bi.Freeze();
			return ImageUtil.Withdpi(bi);
		}
		catch (Exception first) {
			if (!NativeRuntime.HasOpenCv()) throw;
			NativeRuntime.EnsureOpenCv();
			using var mat = Cv2.ImRead(path, ImreadModes.Color);
			if (mat == null || mat.Empty())
				throw new InvalidOperationException($"无法读取图片: {path}", first);
			return ImageUtil.Frombgr(mat);
		}
	}

	static BitmapSource transformwpf(BitmapSource src, int rotate, bool mirror) {
		if (src == null) return null;
		rotate = normrot(rotate);
		if (rotate == 0 && !mirror) return src;
		var g = new TransformGroup();
		if (rotate != 0) g.Children.Add(new RotateTransform(rotate));
		if (mirror) g.Children.Add(new ScaleTransform(-1, 1));
		var tb = new TransformedBitmap(ImageUtil.Withdpi(src), g);
		var wb = new WriteableBitmap(tb);
		wb.Freeze();
		return wb;
	}

	static void savegdi(GdiImg img, Stream dst, string fmt, int quality) {
		if (fmt == "jpg") {
			using var flat = as24(img);
			var to = (GdiImg)flat ?? img;
			var codec = jpegcodec();
			if (codec == null) {
				to.Save(dst, ImageFormat.Jpeg);
				return;
			}
			using var ep = new EncoderParameters(1);
			ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
			to.Save(dst, codec, ep);
			return;
		}
		if (fmt == "bmp") {
			img.Save(dst, ImageFormat.Bmp);
			return;
		}
		img.Save(dst, ImageFormat.Png);
	}

	static GdiBmp as24(GdiImg img) {
		if (img == null) return null;
		if (img.PixelFormat == System.Drawing.Imaging.PixelFormat.Format24bppRgb) return null;
		var b = new GdiBmp(img.Width, img.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
		using var g = GdiGfx.FromImage(b);
		g.Clear(System.Drawing.Color.White);
		g.DrawImage(img, 0, 0, img.Width, img.Height);
		return b;
	}

	static ImageCodecInfo jpegcodec() {
		foreach (var c in ImageCodecInfo.GetImageEncoders()) {
			if (c.FormatID == ImageFormat.Jpeg.Guid) return c;
		}
		return null;
	}

	static RotateFlipType toflip(int rotate, bool mirror) => (rotate, mirror) switch {
		(90, false) => RotateFlipType.Rotate90FlipNone,
		(180, false) => RotateFlipType.Rotate180FlipNone,
		(270, false) => RotateFlipType.Rotate270FlipNone,
		(0, true) => RotateFlipType.RotateNoneFlipX,
		(90, true) => RotateFlipType.Rotate90FlipX,
		(180, true) => RotateFlipType.Rotate180FlipX,
		(270, true) => RotateFlipType.Rotate270FlipX,
		_ => RotateFlipType.RotateNoneFlipNone,
	};

	static int normrot(int rotate) {
		rotate %= 360;
		if (rotate < 0) rotate += 360;
		if (rotate is 90 or 180 or 270) return rotate;
		return 0;
	}

	static string unique(string path, ISet<string> reserved) {
		bool used(string p) =>
			File.Exists(p) || (reserved != null && reserved.Contains(p));
		if (!used(path)) return path;
		var dir = Path.GetDirectoryName(path) ?? "";
		var name = Path.GetFileNameWithoutExtension(path);
		var ext = Path.GetExtension(path);
		for (var i = 2; i < 10000; i++) {
			var p = Path.Combine(dir, $"{name}_{i}{ext}");
			if (!used(p)) return p;
		}
		return path;
	}
}
