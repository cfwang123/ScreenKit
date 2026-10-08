using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
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

	public static OcrResult Recognize(OcrOptions opt, Mat bgr) {
		if (bgr == null || bgr.Empty())
			throw new InvalidOperationException("图像为空");
		var disp = Dispatcher.FromThread(Thread.CurrentThread);
		if (disp == null)
			return recognize(opt, bgr);
		// STA 上直接 GetResult 会卡死，改到线程池再等回来
		var task = Task.Run(() => recognize(opt, bgr));
		return wait(task);
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

	static OcrResult recognize(OcrOptions opt, Mat bgr) {
		var tag = SelectedTag(opt);
		if (string.IsNullOrEmpty(tag))
			throw new InvalidOperationException(Loc.T("ocr.win.empty"));
		var t0 = Environment.TickCount;
		double scale = 1;
		Mat scaled = null;
		var work = bgr;
		try {
			var maxs = Math.Max(bgr.Width, bgr.Height);
			if (maxs > MAXSIDE) {
				scale = MAXSIDE / (double)maxs;
				scaled = new Mat();
				Cv2.Resize(bgr, scaled, new OpenCvSharp.Size(
					Math.Max(1, (int)Math.Round(bgr.Width * scale)),
					Math.Max(1, (int)Math.Round(bgr.Height * scale))));
				work = scaled;
			}
			using var bmp = tobitmap(work);
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
			scaled?.Dispose();
		}
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

	static SoftwareBitmap tobitmap(Mat bgr) {
		using var bgra = new Mat();
		Cv2.CvtColor(bgr, bgra, ColorConversionCodes.BGR2BGRA);
		int w = bgra.Width, h = bgra.Height;
		if (w < 1 || h < 1) throw new InvalidOperationException("图像为空");
		var buf = new byte[w * h * 4];
		int step = (int)bgra.Step();
		int row = w * 4;
		if (bgra.IsContinuous() && step == row)
			Marshal.Copy(bgra.Data, buf, 0, buf.Length);
		else {
			for (int y = 0; y < h; y++)
				Marshal.Copy(bgra.Ptr(y), buf, y * row, row);
		}
		return SoftwareBitmap.CreateCopyFromBuffer(
			buf.AsBuffer(), BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Ignore);
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
