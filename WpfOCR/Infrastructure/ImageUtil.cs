using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace WpfOCR;

static class ImageUtil {
	public static Mat Tobgr(BitmapSource src) {
		NativeRuntime.EnsureOpenCv();
		BitmapSource bgra;
		if (src.Format == PixelFormats.Bgra32 || src.Format == PixelFormats.Bgr32)
			bgra = src;
		else
			bgra = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);

		var w = bgra.PixelWidth;
		var h = bgra.PixelHeight;
		var stride = w * 4;
		var pixels = new byte[stride * h];
		bgra.CopyPixels(pixels, stride, 0);

		var mat = new Mat(h, w, MatType.CV_8UC3);
		// BGRA -> BGR
		unsafe {
			fixed (byte* p = pixels) {
				for (int y = 0; y < h; y++) {
					var srcRow = p + y * stride;
					var dstRow = (byte*)mat.Ptr(y);
					for (int x = 0; x < w; x++) {
						dstRow[x * 3 + 0] = srcRow[x * 4 + 0];
						dstRow[x * 3 + 1] = srcRow[x * 4 + 1];
						dstRow[x * 3 + 2] = srcRow[x * 4 + 2];
					}
				}
			}
		}
		return mat;
	}

	/// <summary>
	/// 重写 DPI 元数据（像素不变）。WPF Image Stretch=None 时按 Dpi 决定 DIP 尺寸，
	/// 与 OCR 像素坐标不一致会导致叠加框错位；统一到 96 后 DIP=像素。
	/// </summary>
	public static BitmapSource Withdpi(BitmapSource src, double dpiX = 96, double dpiY = 96) {
		if (src == null) return null;
		if (Math.Abs(src.DpiX - dpiX) < 0.1 && Math.Abs(src.DpiY - dpiY) < 0.1
			&& (src.Format == PixelFormats.Bgra32 || src.Format == PixelFormats.Pbgra32
				|| src.Format == PixelFormats.Bgr32))
			return src;

		BitmapSource bgra = src;
		if (src.Format != PixelFormats.Bgra32 && src.Format != PixelFormats.Pbgra32
			&& src.Format != PixelFormats.Bgr32)
			bgra = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);

		var w = bgra.PixelWidth;
		var h = bgra.PixelHeight;
		var stride = w * 4;
		var pixels = new byte[stride * h];
		bgra.CopyPixels(pixels, stride, 0);
		var bmp = BitmapSource.Create(w, h, dpiX, dpiY, PixelFormats.Bgra32, null, pixels, stride);
		bmp.Freeze();
		return bmp;
	}

	public static BitmapSource Fromfile(string path) {
		var bi = new BitmapImage();
		bi.BeginInit();
		bi.CacheOption = BitmapCacheOption.OnLoad;
		bi.UriSource = new Uri(path, UriKind.Absolute);
		bi.EndInit();
		bi.Freeze();
		return Withdpi(bi);
	}

	/// <summary>
	/// 剪贴板是否有可取用的图片（位图或图片文件路径）。
	/// 托盘菜单启用态等轻量探测，不解码整图。
	/// </summary>
	public static bool Hasclipboardimage() {
		try {
			if (Clipboard.ContainsImage()) return true;
		}
		catch { }
		try {
			if (Clipboard.ContainsFileDropList()) {
				var files = Clipboard.GetFileDropList();
				if (files != null) {
					foreach (string f in files) {
						if (string.IsNullOrWhiteSpace(f) || !File.Exists(f)) continue;
						if (isimagepath(f)) return true;
					}
				}
			}
		}
		catch { }
		// 部分环境 ContainsImage 为 false 但 GetImage 仍可用
		try {
			var img = Clipboard.GetImage();
			if (img != null && img.PixelWidth > 0 && img.PixelHeight > 0) return true;
		}
		catch { }
		return false;
	}

	/// <summary>
	/// 从剪贴板取图：位图 / 资源管理器复制的图片文件路径（FileDrop）。
	/// </summary>
	public static BitmapSource Fromclipboard() {
		// 1) 资源管理器「复制文件」→ FileDrop（png/jpg 等路径，不是位图）
		try {
			if (Clipboard.ContainsFileDropList()) {
				var files = Clipboard.GetFileDropList();
				if (files != null) {
					foreach (string f in files) {
						if (string.IsNullOrWhiteSpace(f) || !File.Exists(f)) continue;
						if (!isimagepath(f)) continue;
						return Fromfile(Path.GetFullPath(f));
					}
				}
			}
		}
		catch { }

		// 2) 截图 / 画图「复制」→ 位图
		// 部分环境 ContainsImage 为 false 但 GetImage 仍可用
		BitmapSource img = null;
		try {
			if (Clipboard.ContainsImage())
				img = Clipboard.GetImage();
		}
		catch { }
		if (img == null) {
			try { img = Clipboard.GetImage(); } catch { }
		}
		if (img == null) return null;

		// 剪贴板常见 Pbgra32，且 Alpha 全 0 → 显示全透明（黑底上“看不见图”），OCR 仍可读 RGB
		// 统一拷成不透明 Bgra32 @ 96 DPI
		BitmapSource bgra = img;
		if (img.Format != PixelFormats.Bgra32 && img.Format != PixelFormats.Pbgra32
			&& img.Format != PixelFormats.Bgr32)
			bgra = new FormatConvertedBitmap(img, PixelFormats.Bgra32, null, 0);

		var w = bgra.PixelWidth;
		var h = bgra.PixelHeight;
		if (w <= 0 || h <= 0) return null;
		var stride = w * 4;
		var pixels = new byte[stride * h];
		bgra.CopyPixels(pixels, stride, 0);

		// 预乘 → 直通，并强制不透明
		var isPremul = bgra.Format == PixelFormats.Pbgra32;
		for (int i = 0; i < pixels.Length; i += 4) {
			if (isPremul) {
				var a = pixels[i + 3];
				if (a > 0 && a < 255) {
					pixels[i + 0] = (byte)Math.Min(255, pixels[i + 0] * 255 / a);
					pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / a);
					pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / a);
				}
			}
			pixels[i + 3] = 255;
		}

		var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
		bmp.Freeze();
		return bmp;
	}

	static bool isimagepath(string path) {
		var ext = Path.GetExtension(path)?.ToLowerInvariant();
		return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".tif" or ".tiff" or ".gif";
	}

	/// <summary>复制位图到剪贴板（不透明 BGRA）。失败时重试，并附带 PNG 格式提高兼容性。</summary>
	/// <param name="existingPngPath">若已有 PNG 文件则复用，避免二次编码。</param>
	public static void Toclipboard(BitmapSource src, string existingPngPath = null) {
		if (src == null) throw new ArgumentNullException(nameof(src));
		var bmp = ensureopaque(src);
		Exception last = null;
		for (var i = 0; i < 4; i++) {
			try {
				var data = new DataObject();
				data.SetImage(bmp);
				tryaddpng(data, bmp, existingPngPath);
				// copy=false：不 OleFlushClipboard，大图避免卡几十秒
				setclip(data);
				return;
			}
			catch (Exception ex) {
				last = ex;
				try { Thread.Sleep(30 + i * 20); } catch { }
			}
		}
		try {
			Clipboard.SetImage(bmp);
			return;
		}
		catch (Exception ex) {
			last = ex;
		}
		throw new InvalidOperationException("写入剪贴板失败: " + (last?.Message ?? "unknown"), last);
	}

	/// <summary>
	/// 同时放入剪贴板位图 + 临时 PNG 文件（资源管理器可粘贴为文件）。
	/// 录屏 HUD 等「只要有结果」场景优先用此方法。
	/// </summary>
	/// <returns>临时 PNG 完整路径。</returns>
	public static string Toclipboardimageandfile(BitmapSource src) {
		if (src == null) throw new ArgumentNullException(nameof(src));
		var bmp = ensureopaque(src);
		var path = TmpStore.NewPath("snap", ".png");
		Savefile(bmp, path);
		Exception last = null;
		for (var i = 0; i < 4; i++) {
			try {
				putimageandfile(bmp, path);
				return path;
			}
			catch (Exception ex) {
				last = ex;
				try { Thread.Sleep(30 + i * 20); } catch { }
			}
		}
		try { Clipboard.SetImage(bmp); } catch (Exception ex) { last = ex; }
		if (last != null && !File.Exists(path))
			throw new InvalidOperationException("截图保存失败: " + last.Message, last);
		return path;
	}

	/// <summary>
	/// 写入剪贴板。必须 copy=false：copy=true 会对大图做 OleFlushClipboard，
	/// 在剪贴板历史/云同步等监听下常阻塞数十秒。
	/// </summary>
	static void setclip(DataObject data) {
		Clipboard.SetDataObject(data, false);
	}

	/// <summary>附加 PNG 格式；仅当已落盘文件确为 PNG 时复用，避免 jpg 误当 PNG。</summary>
	static void tryaddpng(DataObject data, BitmapSource bmp, string existingPath) {
		try {
			MemoryStream ms;
			var isPngFile = !string.IsNullOrWhiteSpace(existingPath)
				&& File.Exists(existingPath)
				&& string.Equals(Path.GetExtension(existingPath), ".png", StringComparison.OrdinalIgnoreCase);
			if (isPngFile) {
				ms = new MemoryStream(File.ReadAllBytes(existingPath));
			}
			else {
				ms = new MemoryStream();
				var enc = new PngBitmapEncoder();
				enc.Frames.Add(BitmapFrame.Create(bmp));
				enc.Save(ms);
			}
			ms.Position = 0;
			data.SetData("PNG", ms, false);
		}
		catch { }
	}

	static void putimageandfile(BitmapSource bmp, string path) {
		var data = new DataObject();
		data.SetImage(bmp);
		tryaddpng(data, bmp, path);
		if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) {
			var files = new System.Collections.Specialized.StringCollection { path };
			data.SetFileDropList(files);
			data.SetData("Preferred DropEffect",
				new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Copy)));
		}
		setclip(data);
	}

	static BitmapSource ensureopaque(BitmapSource src) {
		var bmp = Withdpi(src);
		if (bmp.Format != PixelFormats.Bgra32) {
			var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
			conv.Freeze();
			bmp = conv;
		}
		return bmp;
	}

	/// <summary>程序目录下 screenshots/：截图历史。</summary>
	public static string ScreenshotsDir {
		get {
			var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots");
			try { Directory.CreateDirectory(dir); } catch { }
			return dir;
		}
	}

	/// <summary>
	/// 当前截图历史保留天数（主窗配置同步；0=不限）。
	/// 保存截图时用于清理过期文件。
	/// </summary>
	public static int CurrentScreenshotKeepDays = 3;

	/// <summary>截图保存格式 png / jpg（主窗配置同步）。</summary>
	public static string CurrentScreenshotFormat = "png";

	/// <summary>JPG 质量 1–100（主窗配置同步）。</summary>
	public static int CurrentScreenshotJpgQuality = 92;

	/// <summary>是否限制截图保存最大宽高（主窗配置同步）。</summary>
	public static bool CurrentScreenshotMaxSizeEnabled = false;

	/// <summary>截图保存最大宽（主窗配置同步）。</summary>
	public static int CurrentScreenshotMaxWidth = 1920;

	/// <summary>截图保存最大高（主窗配置同步）。</summary>
	public static int CurrentScreenshotMaxHeight = 1080;

	/// <summary>截图完成时是否以位图放入剪贴板（与 AsFile / AsPath 三选一；主窗配置同步）。</summary>
	public static bool CurrentSnapCopyAsImage = true;

	/// <summary>截图完成时是否以 FileDrop 放入剪贴板（与 AsImage / AsPath 三选一；主窗配置同步）。</summary>
	public static bool CurrentSnapCopyAsFile = false;

	/// <summary>截图完成时是否以路径文本放入剪贴板（与 AsImage / AsFile 三选一；主窗配置同步）。</summary>
	public static bool CurrentSnapCopyAsPath = false;

	/// <summary>最近一次写入 screenshots/ 的截图完整路径（切换复制方式时复用）。</summary>
	public static string LastScreenshotPath { get; private set; }

	/// <summary>
	/// 保存到 screenshots/（文件名含时间戳便于排序），并按当前配置写入剪贴板
	///（图片 / 文件 / 路径文本，三选一）。
	/// 保存前按保留天数清理过期文件（≤0 表示不限，不清理）。
	/// 按配置应用最大宽高（等比缩小）与保存格式/JPG 质量。
	/// </summary>
	/// <returns>保存的完整路径。</returns>
	public static string SaveScreenshotAndCopy(BitmapSource src, string prefix = "shot",
		int? keepDays = null, bool? copyAsImage = null, bool? copyAsFile = null,
		bool? copyAsPath = null) {
		if (src == null) throw new ArgumentNullException(nameof(src));
		var keep = keepDays ?? CurrentScreenshotKeepDays;
		var asImg = copyAsImage ?? CurrentSnapCopyAsImage;
		var asFile = copyAsFile ?? CurrentSnapCopyAsFile;
		var asPath = copyAsPath ?? CurrentSnapCopyAsPath;
		// 三选一：路径 > 文件 > 图片
		if (asPath) { asImg = false; asFile = false; asPath = true; }
		else if (asFile && !asImg) { asImg = false; asFile = true; asPath = false; }
		else { asImg = true; asFile = false; asPath = false; }
		try { CleanupScreenshots(keep); } catch { }
		// 保存用图：可选等比缩小（OCR 主流程仍用原图）
		var toSave = prepareforcapture(src);
		var dir = ScreenshotsDir;
		var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
		var baseName = $"{(string.IsNullOrWhiteSpace(prefix) ? "shot" : prefix.Trim())}_{stamp}";
		var ext = screenshotext();
		var path = Path.Combine(dir, baseName + ext);
		// 极罕见同毫秒：追加序号
		for (var i = 1; File.Exists(path) && i < 100; i++)
			path = Path.Combine(dir, $"{baseName}_{i}{ext}");
		Savefile(toSave, path, CurrentScreenshotJpgQuality);
		LastScreenshotPath = path;
		if (asImg || asFile || asPath)
			copysnapshotclipboard(toSave, path, asImg, asFile, asPath);
		return path;
	}

	/// <summary>
	/// 解析「上次截图」路径：优先本进程最近一次落盘；否则取 screenshots/ 中最新图片。
	/// </summary>
	public static string ResolveLastScreenshotPath() {
		if (!string.IsNullOrWhiteSpace(LastScreenshotPath) && File.Exists(LastScreenshotPath))
			return LastScreenshotPath;
		try {
			var dir = ScreenshotsDir;
			if (!Directory.Exists(dir)) return null;
			string best = null;
			var bestT = DateTime.MinValue;
			foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)) {
				if (!isimagepath(f)) continue;
				DateTime t;
				try { t = File.GetLastWriteTime(f); } catch { continue; }
				if (t <= bestT) continue;
				bestT = t;
				best = f;
			}
			if (!string.IsNullOrWhiteSpace(best))
				LastScreenshotPath = best;
			return best;
		}
		catch { return null; }
	}

	/// <summary>
	/// 按当前（或指定）复制方式，把上次截图重新写入剪贴板；不新建文件。
	/// </summary>
	/// <returns>成功写入的文件路径；无可复用截图或失败时为 null。</returns>
	public static string RecopyLastScreenshot(bool? copyAsImage = null, bool? copyAsFile = null,
		bool? copyAsPath = null) {
		var path = ResolveLastScreenshotPath();
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			return null;
		var asImg = copyAsImage ?? CurrentSnapCopyAsImage;
		var asFile = copyAsFile ?? CurrentSnapCopyAsFile;
		var asPath = copyAsPath ?? CurrentSnapCopyAsPath;
		if (asPath) { asImg = false; asFile = false; asPath = true; }
		else if (asFile && !asImg) { asImg = false; asFile = true; asPath = false; }
		else { asImg = true; asFile = false; asPath = false; }
		BitmapSource bmp = null;
		if (asImg)
			bmp = Fromfile(path);
		copysnapshotclipboard(bmp, path, asImg, asFile, asPath);
		return path;
	}

	/// <summary>当前配置下的截图扩展名（.png / .jpg）。</summary>
	static string screenshotext() {
		var f = (CurrentScreenshotFormat ?? "png").Trim().ToLowerInvariant();
		return f is "jpg" or "jpeg" ? ".jpg" : ".png";
	}

	/// <summary>按配置等比缩小到最大框内（不放大）；未启用则原样返回。</summary>
	public static BitmapSource FitScreenshotMaxSize(BitmapSource src,
		bool? maxEnabled = null, int? maxW = null, int? maxH = null) {
		if (src == null) return null;
		var en = maxEnabled ?? CurrentScreenshotMaxSizeEnabled;
		if (!en) return src;
		var mw = Math.Max(16, maxW ?? CurrentScreenshotMaxWidth);
		var mh = Math.Max(16, maxH ?? CurrentScreenshotMaxHeight);
		return FitMaxSize(src, mw, mh);
	}

	/// <summary>等比 fit 到 maxW×maxH 内（不放大）。已在框内则原样返回。</summary>
	public static BitmapSource FitMaxSize(BitmapSource src, int maxW, int maxH) {
		if (src == null) return null;
		maxW = Math.Max(16, maxW);
		maxH = Math.Max(16, maxH);
		var w = src.PixelWidth;
		var h = src.PixelHeight;
		if (w <= 0 || h <= 0) return src;
		if (w <= maxW && h <= maxH) return src;
		var s = Math.Min((double)maxW / w, (double)maxH / h);
		var nw = Math.Max(1, (int)Math.Round(w * s));
		var nh = Math.Max(1, (int)Math.Round(h * s));
		var scale = new ScaleTransform((double)nw / w, (double)nh / h);
		var tb = new TransformedBitmap(Withdpi(src), scale);
		// 物化：避免后续编码持有变换链
		var bmp = new WriteableBitmap(tb);
		bmp.Freeze();
		return bmp;
	}

	/// <summary>截图落盘前预处理：DPI 统一 + 可选最大宽高。</summary>
	static BitmapSource prepareforcapture(BitmapSource src) =>
		FitScreenshotMaxSize(Withdpi(src));

	/// <summary>
	/// 保存到 screenshots/，并以「复制文件」放入剪贴板（兼容旧调用）。
	/// </summary>
	public static string SaveScreenshotAndCopyAsFile(BitmapSource src, string prefix = "shot",
		int? keepDays = null) =>
		SaveScreenshotAndCopy(src, prefix, keepDays, copyAsImage: false, copyAsFile: true,
			copyAsPath: false);

	/// <summary>按选项写入剪贴板：位图 / FileDrop / 路径文本（三选一）。</summary>
	static void copysnapshotclipboard(BitmapSource src, string path,
		bool asImage, bool asFile, bool asPath) {
		if (asPath)
			copypathtoclipboard(path);
		else if (asFile && !asImage)
			copyfiletoclipboard(path);
		else
			Toclipboard(src, path);
	}

	/// <summary>
	/// 删除 screenshots/ 中超过 keepDays 天的文件。
	/// keepDays ≤ 0：不限，不删除。
	/// </summary>
	public static int CleanupScreenshots(int keepDays) {
		if (keepDays <= 0) return 0;
		var dir = ScreenshotsDir;
		if (!Directory.Exists(dir)) return 0;
		var cutoff = DateTime.Now.AddDays(-keepDays);
		var n = 0;
		foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)) {
			try {
				if (File.GetLastWriteTime(f) < cutoff) {
					File.Delete(f);
					n++;
				}
			}
			catch { }
		}
		return n;
	}

	/// <summary>打开截图历史目录（资源管理器）。</summary>
	public static void OpenScreenshotsFolder() {
		var dir = ScreenshotsDir;
		try {
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
				FileName = dir,
				UseShellExecute = true,
			});
		}
		catch (Exception ex) {
			throw new InvalidOperationException("无法打开截图历史目录: " + ex.Message, ex);
		}
	}

	/// <summary>
	/// 将图片写入 screenshots/ 时间戳文件，并以「复制文件」形式放入剪贴板。
	/// （原 tmp/clip_copy 复用路径已弃用，统一走历史目录。）
	/// </summary>
	public static string Toclipboardasfile(BitmapSource src) =>
		SaveScreenshotAndCopyAsFile(src, "shot");

	static void copyfiletoclipboard(string path) {
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			throw new FileNotFoundException("截图文件不存在", path);
		Exception last = null;
		for (var i = 0; i < 4; i++) {
			try {
				var files = new System.Collections.Specialized.StringCollection { path };
				var data = new DataObject();
				data.SetFileDropList(files);
				// Preferred DropEffect = Copy，避免 Explorer 当作剪切
				data.SetData("Preferred DropEffect",
					new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Copy)));
				setclip(data);
				return;
			}
			catch (Exception ex) {
				last = ex;
				try { Thread.Sleep(30 + i * 20); } catch { }
			}
		}
		if (last != null)
			throw new InvalidOperationException("复制文件到剪贴板失败: " + last.Message, last);
	}

	/// <summary>将文件完整路径作为文本写入剪贴板（可贴到终端/对话框等）。</summary>
	static void copypathtoclipboard(string path) {
		if (string.IsNullOrWhiteSpace(path))
			throw new ArgumentException("路径无效", nameof(path));
		// 尽量用绝对路径，方便外部程序直接引用
		var full = path;
		try { full = Path.GetFullPath(path); } catch { }
		Exception last = null;
		for (var i = 0; i < 4; i++) {
			try {
				var data = new DataObject();
				data.SetText(full);
				setclip(data);
				return;
			}
			catch (Exception ex) {
				last = ex;
				try { Thread.Sleep(30 + i * 20); } catch { }
			}
		}
		try { Clipboard.SetText(full); return; }
		catch (Exception ex) { last = ex; }
		if (last != null)
			throw new InvalidOperationException("复制路径到剪贴板失败: " + last.Message, last);
	}

	/// <summary>保存为 png/jpg/bmp（按扩展名）。jpg 质量默认用当前配置，可覆盖。</summary>
	public static void Savefile(BitmapSource src, string path, int? jpgQuality = null) {
		if (src == null) throw new ArgumentNullException(nameof(src));
		if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("路径无效", nameof(path));
		var bmp = Withdpi(src);
		var ext = Path.GetExtension(path)?.ToLowerInvariant() ?? ".png";
		var q = jpgQuality ?? CurrentScreenshotJpgQuality;
		if (q < 1) q = 1;
		if (q > 100) q = 100;
		BitmapEncoder enc = ext switch {
			".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = q },
			".bmp" => new BmpBitmapEncoder(),
			_ => new PngBitmapEncoder(),
		};
		enc.Frames.Add(BitmapFrame.Create(bmp));
		var dir = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		using var fs = File.Create(path);
		enc.Save(fs);
	}
}
