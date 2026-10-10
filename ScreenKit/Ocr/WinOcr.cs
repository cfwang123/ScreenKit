using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenCvSharp;
using Windows.Graphics.Imaging;
using WinMedia = Windows.Media.Ocr;

namespace ScreenKit;

/// <summary>本机 Windows.Media.Ocr 的一种语言。</summary>
sealed class WinOcrLang {
	public string Tag { get; set; }
	public string Name { get; set; }
}

/// <summary>
/// Windows 系统 OCR（WinRT）。一次只认一种语言，不加载 ONNX 模型。
/// </summary>
static class WinOcr {
	public const string PackId = "winocr";
	const int MAXSIDE = 4096;

	public static bool IsId(string id) =>
		string.Equals(id, PackId, StringComparison.OrdinalIgnoreCase);

	public static bool Is(OcrOptions o) => o != null && IsId(o.ModelPackId);

	public static ModelPack Pack() => new() {
		Id = PackId,
		NameZh = "Windows 系统 OCR",
		NameEn = "Windows OCR",
		Dir = "",
		Variants = new List<ModelVariant>(),
	};

	public static List<WinOcrLang> Languages() {
		var list = new List<WinOcrLang>();
		IReadOnlyList<Windows.Globalization.Language> src;
		try { src = WinMedia.OcrEngine.AvailableRecognizerLanguages; }
		catch (Exception ex) {
			CaptureLog.Ex("WinOcr languages", ex);
			return list;
		}
		if (src == null) return list;
		foreach (var lang in src) {
			if (lang == null || string.IsNullOrWhiteSpace(lang.LanguageTag)) continue;
			var name = lang.DisplayName;
			if (string.IsNullOrWhiteSpace(name)) name = lang.NativeName;
			if (string.IsNullOrWhiteSpace(name)) name = lang.LanguageTag;
			list.Add(new WinOcrLang { Tag = lang.LanguageTag, Name = name });
		}
		list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));
		return list;
	}

	/// <summary>当前语言。配置为空或已不可用时，优先简体中文，其次英语，再退回第一种。</summary>
	public static string SelectedTag(OcrOptions opt) {
		var avail = Languages();
		if (avail.Count == 0) return "";
		var raw = opt?.WinOcrLangs ?? "";
		var first = raw.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0);
		var hit = avail.FirstOrDefault(x =>
			string.Equals(x.Tag, first, StringComparison.OrdinalIgnoreCase));
		if (hit != null) return hit.Tag;
		return defaulttag(avail);
	}

	public static string Summary(OcrOptions opt) {
		var tag = SelectedTag(opt);
		if (string.IsNullOrEmpty(tag)) return Loc.T("ocr.win.empty");
		var hit = Languages().FirstOrDefault(x =>
			string.Equals(x.Tag, tag, StringComparison.OrdinalIgnoreCase));
		return string.IsNullOrWhiteSpace(hit?.Name) ? tag : hit.Name;
	}

	public static OcrResult Recognize(OcrOptions opt, BitmapSource src) {
		if (src == null) throw new InvalidOperationException("图像为空");
		return offsta(() => recognizebgra(opt, copybgra(src), src.PixelWidth, src.PixelHeight));
	}

	public static OcrResult RecognizeFile(OcrOptions opt, string path) {
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			throw new FileNotFoundException("图像不存在", path);
		return offsta(() => {
			using var fs = File.OpenRead(path);
			var bmp = decode(fs, limitof(opt), out var w, out var h, out var scale);
			return withsize(recognizebmp(opt, bmp, scale), w, h);
		});
	}

	public static OcrResult RecognizeBytes(OcrOptions opt, byte[] image) {
		if (image == null || image.Length == 0)
			throw new InvalidOperationException("图像为空");
		return offsta(() => {
			using var ms = new MemoryStream(image, false);
			var bmp = decode(ms, limitof(opt), out var w, out var h, out var scale);
			return withsize(recognizebmp(opt, bmp, scale), w, h);
		});
	}

	public static OcrResult Recognize(OcrOptions opt, Mat bgr) {
		if (bgr == null || bgr.Empty())
			throw new InvalidOperationException("图像为空");
		var pixels = bgrtobgra(bgr);
		return offsta(() => recognizebgra(opt, pixels, bgr.Width, bgr.Height));
	}

	static OcrResult offsta(Func<OcrResult> run) {
		var disp = Dispatcher.FromThread(Thread.CurrentThread);
		if (disp == null) return run();
		// STA 上直接 GetResult 会卡死，改到线程池再等回来
		return wait(Task.Run(run));
	}

	static string defaulttag(List<WinOcrLang> avail) {
		string take(params string[] tags) {
			foreach (var tag in tags) {
				if (string.IsNullOrWhiteSpace(tag)) continue;
				var hit = avail.FirstOrDefault(x =>
					string.Equals(x.Tag, tag, StringComparison.OrdinalIgnoreCase));
				if (hit != null) return hit.Tag;
			}
			return "";
		}
		var zh = take("zh-Hans", "zh-CN", "zh-Hans-CN");
		if (zh.Length > 0) return zh;
		var en = take("en-US", "en-GB", "en");
		if (en.Length > 0) return en;
		try {
			var eng = WinMedia.OcrEngine.TryCreateFromUserProfileLanguages();
			var profile = take(eng?.RecognizerLanguage?.LanguageTag);
			if (profile.Length > 0) return profile;
		}
		catch { }
		return avail.Count > 0 ? avail[0].Tag : "";
	}

	static int limitof(OcrOptions opt) {
		var limit = opt == null || opt.DetLimitSideLen <= 0 ? 1024 : opt.DetLimitSideLen;
		if (limit < 320) limit = 320;
		if (limit > MAXSIDE) limit = MAXSIDE;
		return limit;
	}

	static OcrResult recognizebgra(OcrOptions opt, byte[] bgra, int width, int height) {
		if (bgra == null || width < 1 || height < 1)
			throw new InvalidOperationException("图像为空");
		var limit = limitof(opt);
		double scale = 1;
		var work = bgra;
		var dw = width;
		var dh = height;
		var maxs = Math.Max(width, height);
		if (maxs > limit) {
			scale = limit / (double)maxs;
			dw = Math.Max(1, (int)Math.Round(width * scale));
			dh = Math.Max(1, (int)Math.Round(height * scale));
			work = resizebgra(bgra, width, height, width * 4, dw, dh);
		}
		var bmp = SoftwareBitmap.CreateCopyFromBuffer(
			work.AsBuffer(), BitmapPixelFormat.Bgra8, dw, dh, BitmapAlphaMode.Ignore);
		return withsize(recognizebmp(opt, bmp, scale), width, height);
	}

	static OcrResult withsize(OcrResult r, int width, int height) {
		if (r == null) return null;
		r.Width = width;
		r.Height = height;
		return r;
	}

	static OcrResult recognizebmp(OcrOptions opt, SoftwareBitmap bmp, double scale) {
		var tag = SelectedTag(opt);
		if (string.IsNullOrEmpty(tag)) {
			try { bmp?.Dispose(); } catch { }
			throw new InvalidOperationException(Loc.T("ocr.win.empty"));
		}
		var t0 = Environment.TickCount;
		try {
			var lang = new Windows.Globalization.Language(tag);
			var engine = WinMedia.OcrEngine.TryCreateFromLanguage(lang);
			if (engine == null)
				throw new InvalidOperationException(Loc.T("ocr.win.empty"));
			WinMedia.OcrResult raw;
			try { raw = wait(engine.RecognizeAsync(bmp).AsTask()); }
			catch (Exception ex) {
				CaptureLog.Ex("WinOcr " + tag, ex);
				throw new InvalidOperationException("Windows OCR 失败: " + ex.Message, ex);
			}
			var lines = new List<OcrLine>();
			var inv = (float)(1.0 / scale);
			if (raw?.Lines != null) {
				foreach (var line in raw.Lines) {
					var text = normtext(line.Text);
					if (text.Length == 0) continue;
					var box = linebox(line, inv);
					if (box == null) continue;
					lines.Add(new OcrLine { Text = text, Score = 1f, Box = box });
				}
			}
			sortlines(lines);
			return new OcrResult {
				Lines = lines,
				DeviceUsed = "Windows",
				ModelLabel = "Windows OCR · " + tag,
				InferMs = Math.Max(0, Environment.TickCount - t0),
			};
		}
		finally {
			try { bmp?.Dispose(); } catch { }
		}
	}

	static SoftwareBitmap decode(Stream stream, int limit, out int width, out int height, out double scale) {
		using var ras = stream.AsRandomAccessStream();
		var decoder = wait(Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras).AsTask());
		var sw = (int)decoder.PixelWidth;
		var sh = (int)decoder.PixelHeight;
		if (sw < 1 || sh < 1) throw new InvalidOperationException("图像为空");
		width = sw;
		height = sh;
		scale = 1;
		var transform = new BitmapTransform {
			InterpolationMode = BitmapInterpolationMode.Linear,
		};
		var maxs = Math.Max(sw, sh);
		if (maxs > limit) {
			scale = limit / (double)maxs;
			transform.ScaledWidth = (uint)Math.Max(1, (int)Math.Round(sw * scale));
			transform.ScaledHeight = (uint)Math.Max(1, (int)Math.Round(sh * scale));
		}
		return wait(decoder.GetSoftwareBitmapAsync(
			BitmapPixelFormat.Bgra8,
			BitmapAlphaMode.Ignore,
			transform,
			ExifOrientationMode.IgnoreExifOrientation,
			ColorManagementMode.DoNotColorManage).AsTask());
	}

	static byte[] copybgra(BitmapSource src) {
		BitmapSource bgra = src;
		if (src.Format != PixelFormats.Bgra32 && src.Format != PixelFormats.Bgr32
			&& src.Format != PixelFormats.Pbgra32)
			bgra = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
		var w = bgra.PixelWidth;
		var h = bgra.PixelHeight;
		if (w < 1 || h < 1) throw new InvalidOperationException("图像为空");
		var stride = w * 4;
		var pixels = new byte[stride * h];
		bgra.CopyPixels(pixels, stride, 0);
		return pixels;
	}

	static byte[] bgrtobgra(Mat bgr) {
		int w = bgr.Width, h = bgr.Height, ch = bgr.Channels();
		if (w < 1 || h < 1 || ch < 1) throw new InvalidOperationException("图像为空");
		int step = (int)bgr.Step();
		var bgra = new byte[w * h * 4];
		var row = new byte[Math.Max(step, w * ch)];
		for (int y = 0; y < h; y++) {
			Marshal.Copy(bgr.Ptr(y), row, 0, Math.Min(step, row.Length));
			for (int x = 0; x < w; x++) {
				int s = x * ch;
				int d = (y * w + x) * 4;
				bgra[d] = row[s];
				bgra[d + 1] = ch > 1 ? row[s + 1] : row[s];
				bgra[d + 2] = ch > 2 ? row[s + 2] : row[s];
				bgra[d + 3] = 255;
			}
		}
		return bgra;
	}

	static byte[] resizebgra(byte[] src, int sw, int sh, int sstride, int dw, int dh) {
		var dst = new byte[dw * dh * 4];
		for (int y = 0; y < dh; y++) {
			var sy = (y + 0.5) * sh / dh - 0.5;
			var y0 = (int)Math.Floor(sy);
			if (y0 < 0) y0 = 0;
			if (y0 >= sh) y0 = sh - 1;
			var y1 = y0 + 1;
			if (y1 >= sh) y1 = sh - 1;
			var fy = sy - y0;
			if (fy < 0) fy = 0;
			if (fy > 1) fy = 1;
			for (int x = 0; x < dw; x++) {
				var sx = (x + 0.5) * sw / dw - 0.5;
				var x0 = (int)Math.Floor(sx);
				if (x0 < 0) x0 = 0;
				if (x0 >= sw) x0 = sw - 1;
				var x1 = x0 + 1;
				if (x1 >= sw) x1 = sw - 1;
				var fx = sx - x0;
				if (fx < 0) fx = 0;
				if (fx > 1) fx = 1;
				int di = (y * dw + x) * 4;
				for (int c = 0; c < 4; c++) {
					var p00 = src[y0 * sstride + x0 * 4 + c];
					var p10 = src[y0 * sstride + x1 * 4 + c];
					var p01 = src[y1 * sstride + x0 * 4 + c];
					var p11 = src[y1 * sstride + x1 * 4 + c];
					var v = p00 * (1 - fx) * (1 - fy) + p10 * fx * (1 - fy)
						+ p01 * (1 - fx) * fy + p11 * fx * fy;
					dst[di + c] = (byte)Math.Round(v);
				}
			}
		}
		return dst;
	}

	/// <summary>汉字、假名、谚文之间不留空格；英文单词之间的空格保留。</summary>
	static string normtext(string text) {
		if (string.IsNullOrWhiteSpace(text)) return "";
		var sb = new StringBuilder(text.Length);
		for (int i = 0; i < text.Length; i++) {
			var c = text[i];
			if (c == ' ' || c == '\u3000') {
				var prev = sb.Length > 0 ? sb[sb.Length - 1] : '\0';
				var next = '\0';
				for (int j = i + 1; j < text.Length; j++) {
					if (text[j] != ' ' && text[j] != '\u3000') { next = text[j]; break; }
				}
				if (iscjk(prev) && iscjk(next)) continue;
				if (sb.Length == 0 || next == '\0') continue;
				if (sb[sb.Length - 1] == ' ') continue;
				sb.Append(' ');
				continue;
			}
			sb.Append(c);
		}
		return sb.ToString().Trim();
	}

	static bool iscjk(char c) =>
		c >= 0x3400 && c <= 0x9FFF
		|| c >= 0xF900 && c <= 0xFAFF
		|| c >= 0x3040 && c <= 0x30FF
		|| c >= 0xAC00 && c <= 0xD7AF;

	static Point2f[] linebox(WinMedia.OcrLine line, float inv) {
		if (line?.Words == null || line.Words.Count == 0) return null;
		double l = double.MaxValue, t = double.MaxValue, r = double.MinValue, btm = double.MinValue;
		foreach (var w in line.Words) {
			var rc = w.BoundingRect;
			if (rc.Width <= 0 || rc.Height <= 0) continue;
			if (rc.X < l) l = rc.X;
			if (rc.Y < t) t = rc.Y;
			if (rc.X + rc.Width > r) r = rc.X + rc.Width;
			if (rc.Y + rc.Height > btm) btm = rc.Y + rc.Height;
		}
		if (r <= l || btm <= t) return null;
		return new[] {
			new Point2f((float)l * inv, (float)t * inv),
			new Point2f((float)r * inv, (float)t * inv),
			new Point2f((float)r * inv, (float)btm * inv),
			new Point2f((float)l * inv, (float)btm * inv),
		};
	}

	static void sortlines(List<OcrLine> lines) {
		lines.Sort((a, b) => {
			var ay = a.Box.Average(p => p.Y);
			var by = b.Box.Average(p => p.Y);
			if (Math.Abs(ay - by) > 10) return ay.CompareTo(by);
			var ax = a.Box.Average(p => p.X);
			var bx = b.Box.Average(p => p.X);
			return ax.CompareTo(bx);
		});
	}

	static T wait<T>(Task<T> task) {
		if (task.IsCompleted) return task.GetAwaiter().GetResult();
		var disp = Dispatcher.FromThread(Thread.CurrentThread);
		if (disp == null) return task.GetAwaiter().GetResult();
		var frame = new DispatcherFrame();
		task.ContinueWith(_ => {
			try { disp.BeginInvoke(new Action(() => frame.Continue = false)); }
			catch { frame.Continue = false; }
		});
		Dispatcher.PushFrame(frame);
		return task.GetAwaiter().GetResult();
	}
}
