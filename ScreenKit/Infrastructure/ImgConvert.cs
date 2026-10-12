using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
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
	public const string OUTBESIDE = "beside";
	public const string OUTOTHER = "other";
	public const string OUTREPLACE = "replace";
	public const string OUTRECYCLE = "recycle";
	const uint FO_DELETE = 3;
	const ushort FOF_SILENT = 0x0004;
	const ushort FOF_NOCONFIRMATION = 0x0010;
	const ushort FOF_ALLOWUNDO = 0x0040;
	const ushort FOF_NOERRORUI = 0x0400;

	public static readonly int[] PngColorChoices = { 2, 4, 8, 16, 32, 64, 128, 256 };

	public static string NormFmt(string fmt) {
		var f = (fmt ?? "jpg").Trim().ToLowerInvariant();
		if (f is "png8" or "pngpal" or "palette") return "png8";
		if (f is "png") return "png";
		if (f is "bmp") return "bmp";
		return "jpg";
	}

	/// <summary>调色板颜色数对齐到 2/4/8/16/32/64/128/256。无效值取最接近的一档，默认 256。</summary>
	public static int NormPngColors(int n) {
		if (n <= 0) return 256;
		var best = 256;
		var dist = int.MaxValue;
		foreach (var c in PngColorChoices) {
			var d = Math.Abs(c - n);
			if (d < dist || (d == dist && c > best)) {
				dist = d;
				best = c;
			}
		}
		return best;
	}

	public static string ExtOf(string fmt) {
		fmt = NormFmt(fmt);
		if (fmt == "png8") return ".png";
		return "." + fmt;
	}

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

	public static string NormOutMode(string mode) {
		var m = (mode ?? "").Trim().ToLowerInvariant();
		if (m is "other" or "replace" or "recycle") return m;
		return OUTBESIDE;
	}

	public static bool IsReplace(string mode) {
		mode = NormOutMode(mode);
		return mode is OUTREPLACE or OUTRECYCLE;
	}

	public static string MakeOutPath(string src, string fmt, bool beside, string customDir, ISet<string> reserved) =>
		MakeOutPath(src, fmt, beside ? OUTBESIDE : OUTOTHER, customDir, reserved);

	public static string MakeOutPath(string src, string fmt, string mode, string customDir, ISet<string> reserved) {
		fmt = NormFmt(fmt);
		mode = NormOutMode(mode);
		var ext = ExtOf(fmt);
		var name = Path.GetFileNameWithoutExtension(src);
		if (string.IsNullOrWhiteSpace(name)) name = "image";
		string dir;
		if (mode == OUTOTHER) {
			dir = (customDir ?? "").Trim();
			if (string.IsNullOrEmpty(dir))
				throw new InvalidOperationException(Loc.T("imgconv.nodir"));
		}
		else if (IsReplace(mode)) {
			dir = Path.GetDirectoryName(src);
			if (string.IsNullOrEmpty(dir))
				dir = AppDomain.CurrentDomain.BaseDirectory;
			var path = Path.Combine(dir, name + ext);
			if (string.Equals(path, src, StringComparison.OrdinalIgnoreCase)) {
				reserved?.Add(path);
				return path;
			}
			path = unique(path, reserved);
			reserved?.Add(path);
			return path;
		}
		else {
			var srcDir = Path.GetDirectoryName(src);
			if (string.IsNullOrEmpty(srcDir))
				srcDir = AppDomain.CurrentDomain.BaseDirectory;
			dir = Path.Combine(srcDir, "output");
		}
		Directory.CreateDirectory(dir);
		var outPath = Path.Combine(dir, name + ext);
		outPath = unique(outPath, reserved);
		reserved?.Add(outPath);
		return outPath;
	}

	public static void ConvertOne(string src, string dst, string fmt, int quality,
		bool maxEn, int maxW, int maxH, int rotate, bool mirror, CancellationToken ct) {
		ConvertOne(src, dst, fmt, quality, maxEn, maxW, maxH, rotate, mirror, ct,
			keepOrigEn: false, keepOrigPct: 80);
	}

	/// <returns>true 表示因体积收益不足而写出原文件。</returns>
	/// <param name="shortSide">true：maxW 是较短边上限，只等比缩小。false：宽高是框，只缩小进框内（网页拍照）。</param>
	public static bool ConvertOne(string src, string dst, string fmt, int quality,
		bool maxEn, int maxW, int maxH, int rotate, bool mirror, CancellationToken ct,
		bool keepOrigEn, int keepOrigPct, string outMode = null, bool shortSide = false, int pngColors = 256) {
		var enc = encodex(src, fmt, quality, maxEn, maxW, maxH, rotate, mirror, ct, shortSide, pngColors);
		var dir = Path.GetDirectoryName(dst);
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		var origLen = 0L;
		try { origLen = new FileInfo(src).Length; } catch { }
		var geom = rotate != 0 || mirror || enc.SrcW != enc.OutW || enc.SrcH != enc.OutH;
		if (KeepOriginal(origLen, enc.Bytes.Length, keepOrigEn, keepOrigPct, geom)) {
			if (!IsReplace(outMode)) {
				var keep = keeppath(src, dst);
				if (!string.Equals(keep, src, StringComparison.OrdinalIgnoreCase))
					File.Copy(src, keep, overwrite: false);
			}
			return true;
		}
		if (IsReplace(outMode)) {
			writereplace(src, dst, enc.Bytes, NormOutMode(outMode) == OUTRECYCLE);
			return false;
		}
		File.WriteAllBytes(dst, enc.Bytes);
		return false;
	}

	/// <summary>把文件移到回收站（可还原）。失败抛异常，不回退成永久删除。</summary>
	public static void RecycleFile(string path) {
		if (string.IsNullOrWhiteSpace(path)) return;
		string full;
		try { full = Path.GetFullPath(path); }
		catch (Exception ex) { throw new IOException(ex.Message, ex); }
		if (!File.Exists(full)) return;
		var mem = Marshal.StringToHGlobalUni(full + "\0");
		try {
			var op = new ShFileOp {
				wFunc = FO_DELETE,
				pFrom = mem,
				fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT),
			};
			var rc = SHFileOperationW(ref op);
			if (rc != 0) throw new IOException(Loc.T("imgconv.recycle.fail", rc));
			if (op.fAnyOperationsAborted != 0)
				throw new IOException(Loc.T("imgconv.recycle.abort"));
			if (File.Exists(full))
				throw new IOException(Loc.T("imgconv.recycle.fail", rc));
		}
		finally {
			Marshal.FreeHGlobal(mem);
		}
	}

	static void writereplace(string src, string dst, byte[] bytes, bool recycle) {
		if (string.IsNullOrWhiteSpace(dst))
			throw new InvalidOperationException(Loc.T("imgconv.nodir"));
		var same = string.Equals(src, dst, StringComparison.OrdinalIgnoreCase);
		if (!same) {
			var dir = Path.GetDirectoryName(dst);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
			File.WriteAllBytes(dst, bytes);
			delsrc(src, recycle);
			return;
		}
		var tmp = maketmp(dst);
		File.WriteAllBytes(tmp, bytes);
		try {
			delsrc(src, recycle);
			File.Move(tmp, dst);
		}
		catch {
			try {
				if (!File.Exists(dst) && File.Exists(tmp)) File.Move(tmp, dst);
				else if (File.Exists(src) && File.Exists(tmp)) File.Delete(tmp);
			}
			catch { }
			throw;
		}
	}

	static void delsrc(string src, bool recycle) {
		if (string.IsNullOrWhiteSpace(src) || !File.Exists(src)) return;
		if (recycle) RecycleFile(src);
		else File.Delete(src);
	}

	static string maketmp(string dst) {
		var dir = Path.GetDirectoryName(dst) ?? "";
		var name = Path.GetFileNameWithoutExtension(dst);
		var ext = Path.GetExtension(dst);
		for (var i = 0; i < 10000; i++) {
			var suffix = i == 0 ? "" : i.ToString();
			var p = Path.Combine(dir, $"{name}.__skconv__{suffix}{ext}");
			if (!File.Exists(p)) return p;
		}
		return dst + ".sktmp";
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
	/// <param name="shortSide">true：maxW 是较短边上限，只等比缩小、不放大。false：宽高框内缩小。</param>
	public static byte[] Encode(string src, string fmt, int quality,
		bool maxEn, int maxW, int maxH, int rotate, bool mirror, CancellationToken ct,
		bool shortSide = false, int pngColors = 256) {
		return encodex(src, fmt, quality, maxEn, maxW, maxH, rotate, mirror, ct, shortSide, pngColors).Bytes;
	}

	/// <summary>
	/// 网页拍照：较短边超限、扩展名不符或带 EXIF 方向时，按拍照参数覆盖原文件。
	/// 浏览器已经压到限制内则不再压第二次。
	/// </summary>
	public static bool FitPhoto(string path, string fmt, int quality, bool limit, int maxPx) {
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
		fmt = NormFmt(fmt);
		quality = Compat.Clamp(quality <= 0 ? 60 : quality, 1, 100);
		if (maxPx < 64) maxPx = 2000;
		if (!peekphoto(path, out var w, out var h, out var rot, out var mirror)) return false;
		var ext = Path.GetExtension(path) ?? "";
		var extOk = fmt == "jpg"
			? ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
				|| ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
			: ext.Equals(ExtOf(fmt), StringComparison.OrdinalIgnoreCase);
		var over = limit && Math.Min(w, h) > maxPx;
		if (extOk && !over && rot == 0 && !mirror) return false;
		var bytes = Encode(path, fmt, quality, limit, maxPx, maxPx, rot, mirror, CancellationToken.None, shortSide: true);
		if (bytes == null || bytes.Length == 0) return false;
		var tmp = path + ".photo.tmp";
		File.WriteAllBytes(tmp, bytes);
		try {
			File.Replace(tmp, path, null);
		}
		catch {
			File.Copy(tmp, path, true);
			try { File.Delete(tmp); } catch { }
		}
		return true;
	}

	static bool peekphoto(string path, out int w, out int h, out int rot, out bool mirror) {
		w = 0;
		h = 0;
		rot = 0;
		mirror = false;
		try {
			using var img = GdiImg.FromFile(path);
			w = img.Width;
			h = img.Height;
			exifpose(img, out rot, out mirror);
			return w > 0 && h > 0;
		}
		catch {
			return false;
		}
	}

	static void exifpose(GdiImg img, out int rot, out bool mirror) {
		rot = 0;
		mirror = false;
		try {
			const int id = 0x0112;
			var ids = img.PropertyIdList;
			if (ids == null) return;
			var has = false;
			for (var i = 0; i < ids.Length; i++) {
				if (ids[i] != id) continue;
				has = true;
				break;
			}
			if (!has) return;
			var item = img.GetPropertyItem(id);
			if (item?.Value == null || item.Value.Length < 1) return;
			switch (item.Value[0]) {
				case 2: mirror = true; break;
				case 3: rot = 180; break;
				case 4: rot = 180; mirror = true; break;
				case 5: rot = 90; mirror = true; break;
				case 6: rot = 90; break;
				case 7: rot = 270; mirror = true; break;
				case 8: rot = 270; break;
			}
		}
		catch { }
	}

	sealed class Encoded {
		public byte[] Bytes;
		public int SrcW, SrcH, OutW, OutH;
	}

	static Encoded encodex(string src, string fmt, int quality,
		bool maxEn, int maxW, int maxH, int rotate, bool mirror, CancellationToken ct,
		bool shortSide, int pngColors) {
		if (string.IsNullOrWhiteSpace(src) || !File.Exists(src))
			throw new FileNotFoundException(src);
		ct.ThrowIfCancellationRequested();
		fmt = NormFmt(fmt);
		quality = Compat.Clamp(quality <= 0 ? 60 : quality, 1, 100);
		pngColors = NormPngColors(pngColors);
		rotate = normrot(rotate);
		try {
			return encodegdi(src, fmt, quality, maxEn, maxW, maxH, rotate, mirror, ct, shortSide, pngColors);
		}
		catch (OperationCanceledException) { throw; }
		catch { }
		try {
			return encodewpf(src, fmt, quality, maxEn, maxW, maxH, rotate, mirror, ct, shortSide, pngColors);
		}
		catch (OperationCanceledException) { throw; }
		catch { }
		// 已安装 OpenCV 时再读 WebP 等 GDI / WPF 打不开的格式。不弹安装窗。
		if (NativeRuntime.HasOpenCv()) {
			try {
				return encodeopencv(src, fmt, quality, maxEn, maxW, maxH, rotate, mirror, ct, shortSide, pngColors);
			}
			catch (OperationCanceledException) { throw; }
			catch { }
			throw new InvalidOperationException(Loc.T("imgconv.noread.cv"));
		}
		throw new InvalidOperationException(Loc.T("imgconv.noread"));
	}

	static Encoded encodeopencv(string src, string fmt, int quality,
		bool maxEn, int maxW, int maxH, int rotate, bool mirror, CancellationToken ct,
		bool shortSide, int pngColors) {
		using var mat = Cv2.ImRead(src, ImreadModes.Color);
		if (mat == null || mat.Empty())
			throw new InvalidOperationException(Loc.T("imgconv.noread.cv"));
		var bmp = ImageUtil.Frombgr(mat);
		if (bmp == null) throw new InvalidOperationException(Loc.T("imgconv.noread.cv"));
		return encodebmp(bmp, fmt, quality, maxEn, maxW, maxH, rotate, mirror, ct, shortSide, pngColors);
	}

	static Encoded encodegdi(string src, string fmt, int quality,
		bool maxEn, int maxW, int maxH, int rotate, bool mirror, CancellationToken ct,
		bool shortSide, int pngColors) {
		// 读进内存再解码。GDI 用路径打开 JPEG 会独占文件，和缩略图抢锁就失败。
		var raw = File.ReadAllBytes(src);
		using var msIn = new MemoryStream(raw);
		using var loaded = new GdiBmp(msIn);
		var srcW = loaded.Width;
		var srcH = loaded.Height;
		using var img = clone32(loaded);
		ct.ThrowIfCancellationRequested();
		var xf = toflip(rotate, mirror);
		if (xf != RotateFlipType.RotateNoneFlipNone)
			img.RotateFlip(xf);
		GdiImg toSave = img;
		GdiBmp resized = null;
		try {
			if (maxEn && fitbox(img.Width, img.Height, maxW, maxH, shortSide, out var nw, out var nh)) {
				resized = new GdiBmp(nw, nh);
				using var g = GdiGfx.FromImage(resized);
				g.InterpolationMode = InterpolationMode.HighQualityBicubic;
				g.PixelOffsetMode = PixelOffsetMode.HighQuality;
				g.DrawImage(img, 0, 0, nw, nh);
				toSave = resized;
			}
			ct.ThrowIfCancellationRequested();
			using var ms = new MemoryStream();
			savegdi(toSave, ms, fmt, quality, pngColors);
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

	/// <summary>shortSide：较短边超过 boxW 则等比缩小。否则缩进 boxW×boxH。不需要缩放时返回 false。</summary>
	static bool fitbox(int w, int h, int boxW, int boxH, bool shortSide, out int nw, out int nh) {
		nw = w;
		nh = h;
		if (w <= 0 || h <= 0) return false;
		double s;
		if (shortSide) {
			var lim = Math.Max(16, boxW);
			var side = Math.Min(w, h);
			if (side <= lim) return false;
			s = (double)lim / side;
		}
		else {
			boxW = Math.Max(16, boxW);
			boxH = Math.Max(16, boxH);
			if (w <= boxW && h <= boxH) return false;
			s = Math.Min((double)boxW / w, (double)boxH / h);
		}
		nw = Math.Max(1, (int)Math.Round(w * s));
		nh = Math.Max(1, (int)Math.Round(h * s));
		return nw != w || nh != h;
	}

	static Encoded encodewpf(string src, string fmt, int quality,
		bool maxEn, int maxW, int maxH, int rotate, bool mirror, CancellationToken ct,
		bool shortSide, int pngColors) {
		var bmp = loadwpf(src);
		return encodebmp(bmp, fmt, quality, maxEn, maxW, maxH, rotate, mirror, ct, shortSide, pngColors);
	}

	static Encoded encodebmp(BitmapSource bmp, string fmt, int quality,
		bool maxEn, int maxW, int maxH, int rotate, bool mirror, CancellationToken ct,
		bool shortSide, int pngColors) {
		var srcW = bmp.PixelWidth;
		var srcH = bmp.PixelHeight;
		ct.ThrowIfCancellationRequested();
		bmp = transformwpf(bmp, rotate, mirror);
		if (maxEn)
			bmp = shortSide
				? ImageUtil.FitShortSide(bmp, maxW)
				: ImageUtil.FitMaxSize(bmp, maxW, maxH);
		if (!bmp.IsFrozen) {
			var wb = new WriteableBitmap(bmp);
			wb.Freeze();
			bmp = wb;
		}
		ct.ThrowIfCancellationRequested();
		if (fmt == "png8") {
			var raw = bgra32(bmp);
			return new Encoded {
				Bytes = png8bytes(bmp.PixelWidth, bmp.PixelHeight, raw, pngColors),
				SrcW = srcW,
				SrcH = srcH,
				OutW = bmp.PixelWidth,
				OutH = bmp.PixelHeight,
			};
		}
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
			var raw = File.ReadAllBytes(path);
			using var fs = new MemoryStream(raw);
			var bi = new BitmapImage();
			bi.BeginInit();
			bi.CacheOption = BitmapCacheOption.OnLoad;
			bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
			bi.StreamSource = fs;
			bi.EndInit();
			bi.Freeze();
			return ImageUtil.Withdpi(bi);
		}
		catch (Exception first) {
			throw new InvalidOperationException(first.Message, first);
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

	static void savegdi(GdiImg img, Stream dst, string fmt, int quality, int pngColors) {
		if (fmt == "png8") {
			var flat = flattenbgra(img);
			var bytes = png8bytes(flat.W, flat.H, flat.Bgra, pngColors);
			dst.Write(bytes, 0, bytes.Length);
			return;
		}
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

	/// <summary>画到新的 32 位图上。JPEG 的 Bitmap(Image) 拷贝仍挂着文件，保存会失败。</summary>
	static GdiBmp clone32(GdiImg src) {
		var b = new GdiBmp(src.Width, src.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		using var g = GdiGfx.FromImage(b);
		g.Clear(System.Drawing.Color.White);
		g.DrawImage(src, 0, 0, src.Width, src.Height);
		return b;
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

	sealed class FlatBgra {
		public int W, H;
		public byte[] Bgra;
	}

	sealed class PalCube {
		public int R0, R1, G0, G1, B0, B1, Pop;
		public long Sr, Sg, Sb, S2;
	}

	static readonly uint[] Crc32Tab = makecrc32();

	static uint[] makecrc32() {
		var t = new uint[256];
		for (uint n = 0; n < 256; n++) {
			var c = n;
			for (var k = 0; k < 8; k++)
				c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
			t[n] = c;
		}
		return t;
	}

	/// <summary>画到白底，得到自上而下的 BGRA。</summary>
	static FlatBgra flattenbgra(GdiImg img) {
		var w = img.Width;
		var h = img.Height;
		using var b = new GdiBmp(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		using (var g = GdiGfx.FromImage(b)) {
			g.Clear(System.Drawing.Color.White);
			g.DrawImage(img, 0, 0, w, h);
		}
		var rect = new System.Drawing.Rectangle(0, 0, w, h);
		var bd = b.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		try {
			var bgra = new byte[w * h * 4];
			var stride = bd.Stride;
			var rowStride = Math.Abs(stride);
			var origin = stride >= 0 ? bd.Scan0 : IntPtr.Add(bd.Scan0, (h - 1) * stride);
			var row = new byte[rowStride];
			for (var y = 0; y < h; y++) {
				Marshal.Copy(IntPtr.Add(origin, y * rowStride), row, 0, rowStride);
				Buffer.BlockCopy(row, 0, bgra, y * w * 4, w * 4);
			}
			return new FlatBgra { W = w, H = h, Bgra = bgra };
		}
		finally {
			b.UnlockBits(bd);
		}
	}

	static byte[] bgra32(BitmapSource bmp) {
		BitmapSource src = bmp;
		if (bmp.Format != PixelFormats.Bgra32) {
			var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
			if (conv.CanFreeze) conv.Freeze();
			src = conv;
		}
		var w = src.PixelWidth;
		var h = src.PixelHeight;
		var stride = w * 4;
		var buf = new byte[stride * h];
		src.CopyPixels(buf, stride, 0);
		return buf;
	}

	/// <summary>按总平方色差最小分配调色板，写成 8 位索引 PNG。半透明先铺到白底。调色板项是加权平均，不必等于图中某个像素。</summary>
	static byte[] png8bytes(int w, int h, byte[] bgra, int colors) {
		colors = NormPngColors(colors);
		var cnt = new int[32768];
		var sr = new long[32768];
		var sg = new long[32768];
		var sb = new long[32768];
		var s2 = new long[32768];
		var n = w * h;
		for (var i = 0; i < n; i++) {
			var o = i * 4;
			blendwhite(bgra, o, out var r, out var g, out var b);
			var bin = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
			cnt[bin]++;
			sr[bin] += r;
			sg[bin] += g;
			sb[bin] += b;
			s2[bin] += (long)r * r + (long)g * g + (long)b * b;
		}
		var pal = bestpal(cnt, sr, sg, sb, s2, colors);
		var map = palmap(pal, cnt, sr, sg, sb);
		var idx = new byte[n];
		for (var i = 0; i < n; i++) {
			var o = i * 4;
			blendwhite(bgra, o, out var r, out var g, out var b);
			idx[i] = map[((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3)];
		}
		return writeindexedpng(w, h, idx, pal);
	}

	static void blendwhite(byte[] bgra, int o, out int r, out int g, out int b) {
		b = bgra[o];
		g = bgra[o + 1];
		r = bgra[o + 2];
		var a = bgra[o + 3];
		if (a == 255) return;
		r = (r * a + 255 * (255 - a) + 127) / 255;
		g = (g * a + 255 * (255 - a) + 127) / 255;
		b = (b * a + 255 * (255 - a) + 127) / 255;
	}

	/// <summary>先把色差最大的一块沿使两侧平方差之和最小的位置切开，再把每色移到所属像素的平均色。</summary>
	static byte[] bestpal(int[] cnt, long[] sr, long[] sg, long[] sb, long[] s2, int colors) {
		var r0 = 31;
		var r1 = 0;
		var g0 = 31;
		var g1 = 0;
		var b0 = 31;
		var b1 = 0;
		var any = false;
		for (var i = 0; i < 32768; i++) {
			if (cnt[i] == 0) continue;
			any = true;
			var r = i >> 10;
			var g = (i >> 5) & 31;
			var b = i & 31;
			if (r < r0) r0 = r;
			if (r > r1) r1 = r;
			if (g < g0) g0 = g;
			if (g > g1) g1 = g;
			if (b < b0) b0 = b;
			if (b > b1) b1 = b;
		}
		if (!any) return new byte[] { 255, 255, 255 };
		var root = new PalCube { R0 = r0, R1 = r1, G0 = g0, G1 = g1, B0 = b0, B1 = b1 };
		fillcube(root, cnt, sr, sg, sb, s2);
		var cubes = new List<PalCube> { root };
		var dead = new List<bool> { false };
		while (cubes.Count < colors) {
			var pick = -1;
			var best = 0.0;
			for (var i = 0; i < cubes.Count; i++) {
				if (dead[i]) continue;
				var e = cubesse(cubes[i]);
				if (e <= best) continue;
				best = e;
				pick = i;
			}
			if (pick < 0) break;
			if (!minsplit(cubes[pick], cnt, sr, sg, sb, s2, out var left, out var right)) {
				dead[pick] = true;
				continue;
			}
			cubes[pick] = left;
			dead[pick] = false;
			cubes.Add(right);
			dead.Add(false);
		}
		var pal = new byte[cubes.Count * 3];
		for (var i = 0; i < cubes.Count; i++)
			putmean(pal, i, cubes[i].Pop, cubes[i].Sr, cubes[i].Sg, cubes[i].Sb);
		return refinepal(pal, cnt, sr, sg, sb);
	}

	static void fillcube(PalCube c, int[] cnt, long[] sr, long[] sg, long[] sb, long[] s2) {
		c.Pop = 0;
		c.Sr = 0;
		c.Sg = 0;
		c.Sb = 0;
		c.S2 = 0;
		for (var r = c.R0; r <= c.R1; r++) {
			for (var g = c.G0; g <= c.G1; g++) {
				for (var b = c.B0; b <= c.B1; b++) {
					var i = (r << 10) | (g << 5) | b;
					var n = cnt[i];
					if (n == 0) continue;
					c.Pop += n;
					c.Sr += sr[i];
					c.Sg += sg[i];
					c.Sb += sb[i];
					c.S2 += s2[i];
				}
			}
		}
	}

	static double cubesse(PalCube c) => cubesse(c.Pop, c.Sr, c.Sg, c.Sb, c.S2);

	static double cubesse(long pop, long sr, long sg, long sb, long s2) {
		if (pop <= 0) return 0;
		return s2 - ((double)sr * sr + (double)sg * sg + (double)sb * sb) / pop;
	}

	/// <summary>在三轴上找使两侧平方差之和最小的一刀。切不开或不能再降误差时返回 false。</summary>
	static bool minsplit(PalCube c, int[] cnt, long[] sr, long[] sg, long[] sb, long[] s2,
		out PalCube left, out PalCube right) {
		left = null;
		right = null;
		if (c.Pop < 2) return false;
		var parent = cubesse(c);
		if (parent <= 0) return false;
		var best = parent;
		var axis = -1;
		var cut = 0;
		for (var ax = 0; ax < 3; ax++) {
			int lo, hi;
			if (ax == 0) { lo = c.R0; hi = c.R1; }
			else if (ax == 1) { lo = c.G0; hi = c.G1; }
			else { lo = c.B0; hi = c.B1; }
			if (hi <= lo) continue;
			var len = hi - lo + 1;
			var pPop = new long[len];
			var pSr = new long[len];
			var pSg = new long[len];
			var pSb = new long[len];
			var pS2 = new long[len];
			for (var r = c.R0; r <= c.R1; r++) {
				for (var g = c.G0; g <= c.G1; g++) {
					for (var b = c.B0; b <= c.B1; b++) {
						var i = (r << 10) | (g << 5) | b;
						var n = cnt[i];
						if (n == 0) continue;
						var v = (ax == 0 ? r : ax == 1 ? g : b) - lo;
						pPop[v] += n;
						pSr[v] += sr[i];
						pSg[v] += sg[i];
						pSb[v] += sb[i];
						pS2[v] += s2[i];
					}
				}
			}
			for (var i = 1; i < len; i++) {
				pPop[i] += pPop[i - 1];
				pSr[i] += pSr[i - 1];
				pSg[i] += pSg[i - 1];
				pSb[i] += pSb[i - 1];
				pS2[i] += pS2[i - 1];
			}
			for (var i = 0; i < len - 1; i++) {
				var lp = pPop[i];
				if (lp <= 0 || lp >= c.Pop) continue;
				var sse = cubesse(lp, pSr[i], pSg[i], pSb[i], pS2[i])
					+ cubesse(c.Pop - lp, c.Sr - pSr[i], c.Sg - pSg[i], c.Sb - pSb[i], c.S2 - pS2[i]);
				if (sse >= best) continue;
				best = sse;
				axis = ax;
				cut = lo + i;
			}
		}
		if (axis < 0) return false;
		left = copycube(c);
		right = copycube(c);
		if (axis == 0) { left.R1 = cut; right.R0 = cut + 1; }
		else if (axis == 1) { left.G1 = cut; right.G0 = cut + 1; }
		else { left.B1 = cut; right.B0 = cut + 1; }
		fillcube(left, cnt, sr, sg, sb, s2);
		fillcube(right, cnt, sr, sg, sb, s2);
		if (left.Pop == 0 || right.Pop == 0) {
			left = null;
			right = null;
			return false;
		}
		return true;
	}

	static PalCube copycube(PalCube c) => new PalCube {
		R0 = c.R0, R1 = c.R1, G0 = c.G0, G1 = c.G1, B0 = c.B0, B1 = c.B1,
	};

	static void putmean(byte[] pal, int slot, long pop, long sr, long sg, long sb) {
		if (pop < 1) pop = 1;
		pal[slot * 3] = (byte)((sr + pop / 2) / pop);
		pal[slot * 3 + 1] = (byte)((sg + pop / 2) / pop);
		pal[slot * 3 + 2] = (byte)((sb + pop / 2) / pop);
	}

	/// <summary>反复把每个色块分给最近的调色板色，再把该色改成这些色块的平均。空出来的位置改去误差最大的色块。</summary>
	static byte[] refinepal(byte[] pal, int[] cnt, long[] sr, long[] sg, long[] sb) {
		var nslot = pal.Length / 3;
		if (nslot <= 1) return pal;
		var occ = new List<int>();
		for (var i = 0; i < 32768; i++)
			if (cnt[i] != 0) occ.Add(i);
		var prev = double.MaxValue;
		for (var round = 0; round < 8; round++) {
			var np = new int[nslot];
			var nsr = new long[nslot];
			var nsg = new long[nslot];
			var nsb = new long[nslot];
			double err = 0;
			foreach (var i in occ) {
				var n = cnt[i];
				var r = (int)((sr[i] + n / 2) / n);
				var g = (int)((sg[i] + n / 2) / n);
				var b = (int)((sb[i] + n / 2) / n);
				var slot = nearestslot(pal, nslot, r, g, b, out var dist);
				np[slot] += n;
				nsr[slot] += sr[i];
				nsg[slot] += sg[i];
				nsb[slot] += sb[i];
				err += n * (double)dist;
			}
			var moved = false;
			for (var p = 0; p < nslot; p++) {
				if (np[p] == 0) {
					if (reseed(pal, nslot, p, occ, cnt, sr, sg, sb)) moved = true;
					continue;
				}
				var nr = (byte)((nsr[p] + np[p] / 2) / np[p]);
				var ng = (byte)((nsg[p] + np[p] / 2) / np[p]);
				var nb = (byte)((nsb[p] + np[p] / 2) / np[p]);
				if (pal[p * 3] != nr || pal[p * 3 + 1] != ng || pal[p * 3 + 2] != nb) moved = true;
				pal[p * 3] = nr;
				pal[p * 3 + 1] = ng;
				pal[p * 3 + 2] = nb;
			}
			for (var p = 1; p < nslot; p++) {
				for (var q = 0; q < p; q++) {
					if (!sameslot(pal, p, q)) continue;
					if (reseed(pal, nslot, p, occ, cnt, sr, sg, sb)) moved = true;
					break;
				}
			}
			if (!moved || err >= prev - 0.5) break;
			prev = err;
		}
		return compactpal(pal);
	}

	static int nearestslot(byte[] pal, int nslot, int r, int g, int b, out int dist) {
		var best = 0;
		var bestd = int.MaxValue;
		for (var p = 0; p < nslot; p++) {
			var dr = r - pal[p * 3];
			var dg = g - pal[p * 3 + 1];
			var db = b - pal[p * 3 + 2];
			var d = dr * dr + dg * dg + db * db;
			if (d >= bestd) continue;
			bestd = d;
			best = p;
		}
		dist = bestd;
		return best;
	}

	static bool sameslot(byte[] pal, int a, int b) =>
		pal[a * 3] == pal[b * 3] && pal[a * 3 + 1] == pal[b * 3 + 1] && pal[a * 3 + 2] == pal[b * 3 + 2];

	static bool hasrgb(byte[] pal, int nslot, int r, int g, int b) {
		for (var p = 0; p < nslot; p++) {
			if (pal[p * 3] == r && pal[p * 3 + 1] == g && pal[p * 3 + 2] == b) return true;
		}
		return false;
	}

	/// <summary>把空位或重复色挪到当前误差最大、且平均色还不在调色板里的色块上。</summary>
	static bool reseed(byte[] pal, int nslot, int slot, List<int> occ, int[] cnt, long[] sr, long[] sg, long[] sb) {
		double worst = 0;
		var bin = -1;
		foreach (var i in occ) {
			var n = cnt[i];
			var r = (int)((sr[i] + n / 2) / n);
			var g = (int)((sg[i] + n / 2) / n);
			var b = (int)((sb[i] + n / 2) / n);
			if (hasrgb(pal, nslot, r, g, b)) continue;
			nearestslot(pal, nslot, r, g, b, out var dist);
			var score = n * (double)dist;
			if (score <= worst) continue;
			worst = score;
			bin = i;
		}
		if (bin < 0 || worst <= 0) return false;
		var bn = cnt[bin];
		putmean(pal, slot, bn, sr[bin], sg[bin], sb[bin]);
		return true;
	}

	static byte[] compactpal(byte[] pal) {
		var n = pal.Length / 3;
		var keep = new List<byte>();
		for (var p = 0; p < n; p++) {
			var dup = false;
			for (var q = 0; q < keep.Count; q += 3) {
				if (keep[q] == pal[p * 3] && keep[q + 1] == pal[p * 3 + 1] && keep[q + 2] == pal[p * 3 + 2]) {
					dup = true;
					break;
				}
			}
			if (dup) continue;
			keep.Add(pal[p * 3]);
			keep.Add(pal[p * 3 + 1]);
			keep.Add(pal[p * 3 + 2]);
		}
		if (keep.Count == pal.Length) return pal;
		if (keep.Count == 0) return new byte[] { 255, 255, 255 };
		return keep.ToArray();
	}

	/// <summary>每个 5 位色块用自己的平均色去找最近的调色板项，使这块像素的平方差最小。</summary>
	static byte[] palmap(byte[] pal, int[] cnt, long[] sr, long[] sg, long[] sb) {
		var n = pal.Length / 3;
		var map = new byte[32768];
		if (n <= 1) return map;
		for (var i = 0; i < 32768; i++) {
			if (cnt[i] == 0) continue;
			var c = cnt[i];
			var r = (int)((sr[i] + c / 2) / c);
			var g = (int)((sg[i] + c / 2) / c);
			var b = (int)((sb[i] + c / 2) / c);
			map[i] = (byte)nearestslot(pal, n, r, g, b, out _);
		}
		return map;
	}

	static byte[] writeindexedpng(int w, int h, byte[] idx, byte[] pal) {
		var raw = new byte[(w + 1) * h];
		for (var y = 0; y < h; y++) {
			var dst = y * (w + 1);
			raw[dst] = 0;
			Buffer.BlockCopy(idx, y * w, raw, dst + 1, w);
		}
		using var ms = new MemoryStream(raw.Length / 2 + 64);
		ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
		var ihdr = new byte[13];
		putu32(ihdr, 0, w);
		putu32(ihdr, 4, h);
		ihdr[8] = 8;
		ihdr[9] = 3;
		pngchunk(ms, "IHDR", ihdr);
		pngchunk(ms, "PLTE", pal);
		pngchunk(ms, "IDAT", zlibof(raw));
		pngchunk(ms, "IEND", Array.Empty<byte>());
		return ms.ToArray();
	}

	static byte[] zlibof(byte[] raw) {
		using var z = new MemoryStream();
		z.WriteByte(0x78);
		z.WriteByte(0x9C);
		using (var ds = new DeflateStream(z, CompressionLevel.Optimal, true))
			ds.Write(raw, 0, raw.Length);
		var ad = adler32(raw);
		z.WriteByte((byte)(ad >> 24));
		z.WriteByte((byte)(ad >> 16));
		z.WriteByte((byte)(ad >> 8));
		z.WriteByte((byte)ad);
		return z.ToArray();
	}

	static uint adler32(byte[] data) {
		uint a = 1;
		uint b = 0;
		for (var i = 0; i < data.Length; i++) {
			a = (a + data[i]) % 65521;
			b = (b + a) % 65521;
		}
		return (b << 16) | a;
	}

	static void pngchunk(Stream s, string type, byte[] data) {
		var body = new byte[4 + data.Length];
		for (var i = 0; i < 4; i++) body[i] = (byte)type[i];
		if (data.Length > 0) Buffer.BlockCopy(data, 0, body, 4, data.Length);
		var len = new byte[4];
		putu32(len, 0, data.Length);
		s.Write(len, 0, 4);
		s.Write(body, 0, body.Length);
		var crc = crc32(body);
		var cb = new byte[4];
		putu32(cb, 0, (int)crc);
		s.Write(cb, 0, 4);
	}

	static uint crc32(byte[] buf) {
		uint c = 0xFFFFFFFFu;
		for (var i = 0; i < buf.Length; i++)
			c = Crc32Tab[(c ^ buf[i]) & 0xFF] ^ (c >> 8);
		return c ^ 0xFFFFFFFFu;
	}

	static void putu32(byte[] buf, int off, int v) {
		buf[off] = (byte)(v >> 24);
		buf[off + 1] = (byte)(v >> 16);
		buf[off + 2] = (byte)(v >> 8);
		buf[off + 3] = (byte)v;
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

	[StructLayout(LayoutKind.Explicit, Size = 64)]
	struct ShFileOp {
		[FieldOffset(0)] public IntPtr hwnd;
		[FieldOffset(8)] public uint wFunc;
		[FieldOffset(16)] public IntPtr pFrom;
		[FieldOffset(24)] public IntPtr pTo;
		[FieldOffset(32)] public ushort fFlags;
		[FieldOffset(36)] public int fAnyOperationsAborted;
		[FieldOffset(48)] public IntPtr hNameMappings;
		[FieldOffset(56)] public IntPtr lpszProgressTitle;
	}

	[DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
	static extern int SHFileOperationW(ref ShFileOp op);
}
