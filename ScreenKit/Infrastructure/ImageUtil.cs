using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace ScreenKit;

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
	/// 写入剪贴板。位图必须 persist=false：copy=true 会对大图做 OleFlushClipboard，
	/// 在剪贴板历史/云同步等监听下常阻塞数十秒。路径/文件列表很小，应 persist。
	/// </summary>
	static void setclip(DataObject data, bool persist = false) {
		Clipboard.SetDataObject(data, persist);
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
				setclip(data, persist: true);
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

	const uint CF_TEXT = 1, CF_BITMAP = 2, CF_METAFILEPICT = 3, CF_TIFF = 6;
	const uint CF_OEMTEXT = 7, CF_DIB = 8, CF_UNICODETEXT = 13, CF_ENHMETAFILE = 14;
	const uint CF_HDROP = 15, CF_LOCALE = 16, CF_DIBV5 = 17;

	[DllImport("user32.dll", SetLastError = true)]
	static extern bool OpenClipboard(IntPtr hWndNewOwner);

	[DllImport("user32.dll", SetLastError = true)]
	static extern bool CloseClipboard();

	[DllImport("user32.dll", SetLastError = true)]
	static extern IntPtr GetClipboardData(uint uFormat);

	[DllImport("user32.dll", SetLastError = true)]
	static extern uint EnumClipboardFormats(uint format);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern int GetClipboardFormatName(uint format, StringBuilder lpszFormatName, int cchMaxCount);

	[DllImport("ole32.dll")]
	static extern int OleSetClipboard(IntPtr pDataObj);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern IntPtr GlobalLock(IntPtr hMem);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool GlobalUnlock(IntPtr hMem);

	/// <summary>将文件完整路径作为文本写入剪贴板（公开入口，供主窗复制图片路径等）。</summary>
	public static void CopyPathToClipboard(string path) =>
		copypathtoclipboard(path);

	/// <summary>
	/// 将文件完整路径作为文本写入剪贴板（可贴到终端/对话框等）。
	/// 必须走 OLE 文本 DataObject（persist）：Win32 SetClipboardData 后再 OleFlushClipboard
	/// 会把刚写入的文本清掉（Flush 对 null IDataObject 执行 EmptyClipboard），表现为截图完未复制。
	/// 校验只用 Win32 枚举，不用 WPF GetText/ContainsImage：会把上一张延迟位图重新挂上，微信粘成「▀」。
	/// </summary>
	static void copypathtoclipboard(string path) {
		if (string.IsNullOrWhiteSpace(path))
			throw new ArgumentException("路径无效", nameof(path));
		var full = path;
		try { full = Path.GetFullPath(path); } catch { }
		Exception last = null;
		for (var i = 0; i < 8; i++) {
			try {
				try { OleSetClipboard(IntPtr.Zero); } catch { }
				var data = new DataObject();
				data.SetText(full);
				setclip(data, persist: true);
				if (cliptextispath(full)) return;
				last = new InvalidOperationException("剪贴板校验失败: " + ClipboardFormatList());
			}
			catch (Exception ex) { last = ex; }
			try { Thread.Sleep(30 + i * 20); } catch { }
		}
		throw new InvalidOperationException("复制路径到剪贴板失败: " + (last?.Message ?? "未知"), last);
	}

	/// <summary>剪贴板是否仅为该路径文本、无位图/文件拖放。供 CLI 自检。</summary>
	public static bool ClipboardIsPathOnly(string path) {
		if (string.IsNullOrWhiteSpace(path)) return false;
		try { path = Path.GetFullPath(path); } catch { }
		return cliptextispath(path);
	}

	/// <summary>Win32 枚举剪贴板格式（诊断用，不走 WPF Clipboard）。</summary>
	public static string ClipboardFormatList() {
		if (!OpenClipboard(IntPtr.Zero)) return "OpenClipboard fail";
		try {
			var sb = new StringBuilder();
			uint f = 0;
			while ((f = EnumClipboardFormats(f)) != 0) {
				if (sb.Length > 0) sb.Append(',');
				sb.Append(fmtname(f));
			}
			var got = win32readunicode();
			if (sb.Length == 0) sb.Append("(empty)");
			sb.Append(" text=").Append(got ?? "(null)");
			return sb.ToString();
		}
		finally { CloseClipboard(); }
	}

	/// <summary>Win32 枚举：只要文本（允许 Locale），不要图/HDROP。</summary>
	static bool cliptextispath(string expect) {
		if (!OpenClipboard(IntPtr.Zero)) return false;
		try {
			uint f = 0;
			while ((f = EnumClipboardFormats(f)) != 0) {
				if (isimporfilefmt(f)) return false;
			}
			var got = win32readunicode();
			return string.Equals(got, expect, StringComparison.Ordinal);
		}
		finally {
			CloseClipboard();
		}
	}

	static bool isimporfilefmt(uint f) {
		if (f is CF_BITMAP or CF_METAFILEPICT or CF_TIFF or CF_DIB
			or CF_ENHMETAFILE or CF_HDROP or CF_DIBV5)
			return true;
		if (f is CF_TEXT or CF_OEMTEXT or CF_UNICODETEXT or CF_LOCALE)
			return false;
		var n = fmtname(f);
		if (n.IndexOf("Bitmap", StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (n.IndexOf("PNG", StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (n.IndexOf("DIB", StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (n.IndexOf("FileDrop", StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (n.IndexOf("FileName", StringComparison.OrdinalIgnoreCase) >= 0) return true;
		return false;
	}

	static string fmtname(uint f) {
		if (f == CF_TEXT) return "CF_TEXT";
		if (f == CF_BITMAP) return "CF_BITMAP";
		if (f == CF_METAFILEPICT) return "CF_METAFILEPICT";
		if (f == CF_TIFF) return "CF_TIFF";
		if (f == CF_OEMTEXT) return "CF_OEMTEXT";
		if (f == CF_DIB) return "CF_DIB";
		if (f == CF_UNICODETEXT) return "CF_UNICODETEXT";
		if (f == CF_ENHMETAFILE) return "CF_ENHMETAFILE";
		if (f == CF_HDROP) return "CF_HDROP";
		if (f == CF_LOCALE) return "CF_LOCALE";
		if (f == CF_DIBV5) return "CF_DIBV5";
		var sb = new StringBuilder(128);
		if (GetClipboardFormatName(f, sb, sb.Capacity) > 0) return sb.ToString();
		return f.ToString();
	}

	static string win32readunicode() {
		var h = GetClipboardData(CF_UNICODETEXT);
		if (h == IntPtr.Zero) return null;
		var p = GlobalLock(h);
		if (p == IntPtr.Zero) return null;
		try { return Marshal.PtrToStringUni(p); }
		finally { GlobalUnlock(h); }
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

	/// <summary>
	/// 在 BGR 图上用系统 CJK 字体写字（微软雅黑）。OpenCV Hershey 不含汉字，会画成 ?。
	/// 返回实际用到的字体名。
	/// </summary>
	public static string Putcjk(OpenCvSharp.Mat bgr, string text, float x, float y) {
		if (bgr == null || bgr.Empty() || string.IsNullOrEmpty(text)) return "";
		int imgW = bgr.Cols;
		float em = Math.Max(14f, imgW / 42f);
		using var font = cjkfont(em);
		System.Drawing.SizeF sz;
		using (var probe = new System.Drawing.Bitmap(8, 8))
		using (var pg = System.Drawing.Graphics.FromImage(probe)) {
			pg.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
			sz = pg.MeasureString(text, font);
		}
		int tw = Math.Max(1, (int)Math.Ceiling(sz.Width) + 8);
		int th = Math.Max(1, (int)Math.Ceiling(sz.Height) + 4);
		int tx = Math.Max(0, Math.Min((int)x, bgr.Cols - 1));
		int ty = (int)y - th - 2;
		if (ty < 0) ty = Math.Min(bgr.Rows - 1, (int)y + 2);
		if (ty < 0) ty = 0;
		if (tx + tw > bgr.Cols) tw = bgr.Cols - tx;
		if (ty + th > bgr.Rows) th = bgr.Rows - ty;
		if (tw <= 0 || th <= 0) return font.Name;

		using var bmp = new System.Drawing.Bitmap(tw, th, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		using (var g = System.Drawing.Graphics.FromImage(bmp)) {
			g.Clear(System.Drawing.Color.FromArgb(180, 0, 0, 0));
			g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
			using var fg = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 255, 220, 80));
			g.DrawString(text, font, fg, 2f, 1f);
		}
		var rect = new System.Drawing.Rectangle(0, 0, tw, th);
		var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
			System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		try {
			unsafe {
				byte* src = (byte*)data.Scan0;
				for (int row = 0; row < th; row++) {
					byte* dstRow = (byte*)bgr.Ptr(ty + row) + tx * 3;
					byte* srcRow = src + row * data.Stride;
					for (int col = 0; col < tw; col++) {
						int a = srcRow[col * 4 + 3];
						if (a == 0) continue;
						int b = srcRow[col * 4 + 0];
						int gc = srcRow[col * 4 + 1];
						int r = srcRow[col * 4 + 2];
						if (a >= 250) {
							dstRow[col * 3 + 0] = (byte)b;
							dstRow[col * 3 + 1] = (byte)gc;
							dstRow[col * 3 + 2] = (byte)r;
						}
						else {
							int ia = 255 - a;
							dstRow[col * 3 + 0] = (byte)((b * a + dstRow[col * 3 + 0] * ia) / 255);
							dstRow[col * 3 + 1] = (byte)((gc * a + dstRow[col * 3 + 1] * ia) / 255);
							dstRow[col * 3 + 2] = (byte)((r * a + dstRow[col * 3 + 2] * ia) / 255);
						}
					}
				}
			}
		}
		finally { bmp.UnlockBits(data); }
		return font.Name;
	}

	static System.Drawing.Font cjkfont(float emPx) {
		string[] names = ["Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑", "SimHei", "黑体"];
		foreach (var n in names) {
			try {
				var f = new System.Drawing.Font(n, emPx, System.Drawing.FontStyle.Bold,
					System.Drawing.GraphicsUnit.Pixel);
				var used = f.Name ?? "";
				if (used.IndexOf("YaHei", StringComparison.OrdinalIgnoreCase) >= 0
					|| used.Contains("雅黑") || used.Contains("黑体")
					|| used.IndexOf("SimHei", StringComparison.OrdinalIgnoreCase) >= 0
					|| string.Equals(used, n, StringComparison.OrdinalIgnoreCase))
					return f;
				f.Dispose();
			}
			catch { }
		}
		return new System.Drawing.Font(System.Drawing.SystemFonts.MessageBoxFont.FontFamily, emPx,
			System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
	}

	/// <summary>BGR Mat → 不透明 Bgra32 BitmapSource（96 DPI）。</summary>
	public static BitmapSource Frombgr(OpenCvSharp.Mat bgr) {
		if (bgr == null || bgr.Empty()) return null;
		NativeRuntime.EnsureOpenCv();
		using var bgra = new OpenCvSharp.Mat();
		OpenCvSharp.Cv2.CvtColor(bgr, bgra, OpenCvSharp.ColorConversionCodes.BGR2BGRA);
		var w = bgra.Cols;
		var h = bgra.Rows;
		var stride = w * 4;
		var bytes = new byte[stride * h];
		System.Runtime.InteropServices.Marshal.Copy(bgra.Data, bytes, 0, bytes.Length);
		for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
		var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bytes, stride);
		bmp.Freeze();
		return bmp;
	}
}
