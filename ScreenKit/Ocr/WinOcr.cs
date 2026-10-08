using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Automation;
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
/// Windows 系统 OCR（WinRT）。一种语言一个引擎；多选时按顺序各跑一次。
/// 重叠区域保留先选语言的文字，后面的语言只补不重叠的行。不加载 ONNX 模型。
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

	/// <summary>配置里勾选且本机仍可用的语言。空配置时默认简体中文和英语（有则选）。</summary>
	public static List<string> SelectedTags(OcrOptions opt) {
		var avail = Languages();
		var picked = new List<string>();
		var raw = opt?.WinOcrLangs ?? "";
		foreach (var part in raw.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)) {
			var tag = part.Trim();
			var hit = avail.FirstOrDefault(x =>
				string.Equals(x.Tag, tag, StringComparison.OrdinalIgnoreCase));
			if (hit == null) continue;
			if (picked.Any(p => string.Equals(p, hit.Tag, StringComparison.OrdinalIgnoreCase))) continue;
			picked.Add(hit.Tag);
		}
		if (picked.Count > 0) return picked;
		return defaulttags(avail);
	}

	public static string Summary(OcrOptions opt) {
		var tags = SelectedTags(opt);
		if (tags.Count == 0) return Loc.T("ocr.win.empty");
		var avail = Languages();
		var names = new List<string>();
		foreach (var tag in tags) {
			var hit = avail.FirstOrDefault(x =>
				string.Equals(x.Tag, tag, StringComparison.OrdinalIgnoreCase));
			names.Add(string.IsNullOrWhiteSpace(hit?.Name) ? tag : hit.Name);
		}
		return string.Join(", ", names);
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

	static List<string> defaulttags(List<WinOcrLang> avail) {
		var picked = new List<string>();
		void take(params string[] tags) {
			foreach (var tag in tags) {
				if (string.IsNullOrWhiteSpace(tag)) continue;
				var hit = avail.FirstOrDefault(x =>
					string.Equals(x.Tag, tag, StringComparison.OrdinalIgnoreCase));
				if (hit == null) continue;
				if (picked.Any(p => string.Equals(p, hit.Tag, StringComparison.OrdinalIgnoreCase))) return;
				picked.Add(hit.Tag);
				return;
			}
		}
		take("zh-Hans", "zh-CN", "zh-Hans-CN");
		take("en-US", "en-GB", "en");
		if (picked.Count > 0) return picked;
		try {
			var eng = WinMedia.OcrEngine.TryCreateFromUserProfileLanguages();
			take(eng?.RecognizerLanguage?.LanguageTag);
		}
		catch { }
		if (picked.Count == 0 && avail.Count > 0) picked.Add(avail[0].Tag);
		return picked;
	}

	static OcrResult recognize(OcrOptions opt, Mat bgr) {
		var tags = SelectedTags(opt);
		if (tags.Count == 0)
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
			var lines = new List<OcrLine>();
			var used = new List<string>();
			foreach (var tag in tags) {
				Windows.Globalization.Language lang;
				try { lang = new Windows.Globalization.Language(tag); }
				catch { continue; }
				var engine = WinMedia.OcrEngine.TryCreateFromLanguage(lang);
				if (engine == null) continue;
				WinMedia.OcrResult raw;
				try { raw = wait(engine.RecognizeAsync(bmp).AsTask()); }
				catch (Exception ex) {
					CaptureLog.Ex("WinOcr " + tag, ex);
					continue;
				}
				if (raw?.Lines == null) continue;
				used.Add(tag);
				var inv = (float)(1.0 / scale);
				foreach (var line in raw.Lines) {
					var text = normtext(line.Text);
					if (text.Length == 0) continue;
					var box = linebox(line, inv);
					if (box == null) continue;
					merge(lines, new OcrLine { Text = text, Score = 1f, Box = box });
				}
			}
			if (used.Count == 0)
				throw new InvalidOperationException(Loc.T("ocr.win.empty"));
			sortlines(lines);
			return new OcrResult {
				Lines = lines,
				DeviceUsed = "Windows",
				ModelLabel = "Windows OCR · " + string.Join(", ", used),
				InferMs = Math.Max(0, Environment.TickCount - t0),
			};
		}
		finally {
			scaled?.Dispose();
		}
	}

	/// <summary>已有行优先。后来的语言只补上不重叠的行。</summary>
	static void merge(List<OcrLine> lines, OcrLine neu) {
		foreach (var old in lines) {
			if (overlap(old.Box, neu.Box)) return;
		}
		lines.Add(neu);
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

	static bool overlap(Point2f[] a, Point2f[] b) {
		if (a == null || b == null || a.Length == 0 || b.Length == 0) return false;
		var ra = bounds(a);
		var rb = bounds(b);
		var iw = Math.Min(ra.r, rb.r) - Math.Max(ra.l, rb.l);
		var ih = Math.Min(ra.b, rb.b) - Math.Max(ra.t, rb.t);
		if (iw <= 1 || ih <= 1) return false;
		var inter = iw * ih;
		var aa = Math.Max(1f, (ra.r - ra.l) * (ra.b - ra.t));
		var ba = Math.Max(1f, (rb.r - rb.l) * (rb.b - rb.t));
		return inter / Math.Min(aa, ba) >= 0.5f;
	}

	static (float l, float t, float r, float b) bounds(Point2f[] box) {
		float l = float.MaxValue, t = float.MaxValue, r = float.MinValue, b = float.MinValue;
		foreach (var p in box) {
			if (p.X < l) l = p.X;
			if (p.Y < t) t = p.Y;
			if (p.X > r) r = p.X;
			if (p.Y > b) b = p.Y;
		}
		return (l, t, r, b);
	}

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

/// <summary>Windows OCR 语言多选（主窗顶栏与参数设置共用）。</summary>
static class WinOcrUi {
	public static void Fill(Panel host, OcrOptions opt, Action onchanged) {
		if (host == null) return;
		host.Children.Clear();
		var langs = WinOcr.Languages();
		if (langs.Count == 0) {
			host.Children.Add(new TextBlock {
				Text = Loc.T("ocr.win.empty"),
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(4),
				MaxWidth = 260,
			});
			return;
		}
		var sel = new HashSet<string>(WinOcr.SelectedTags(opt), StringComparer.OrdinalIgnoreCase);
		var guard = false;
		foreach (var lang in langs) {
			var cb = new CheckBox {
				Content = lang.Name,
				Tag = lang.Tag,
				IsChecked = sel.Contains(lang.Tag),
				Margin = new Thickness(2, 3, 8, 3),
			};
			AutomationProperties.SetAutomationId(cb, "ocr.winlang." + lang.Tag);
			cb.Checked += (_, _) => changed();
			cb.Unchecked += (_, _) => changed();
			host.Children.Add(cb);

			void changed() {
				if (guard) return;
				if (string.IsNullOrEmpty(Read(host, ""))) {
					guard = true;
					cb.IsChecked = true;
					guard = false;
					return;
				}
				try { onchanged?.Invoke(); } catch { }
			}
		}
	}

	/// <summary>勾选结果。已保存的顺序不变，新勾上的语言接在后面。</summary>
	public static string Read(Panel host, string previous) {
		if (host == null) return "";
		var on = new List<string>();
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var child in host.Children) {
			if (child is CheckBox cb && cb.IsChecked == true && cb.Tag is string tag && tag.Length > 0
				&& set.Add(tag))
				on.Add(tag);
		}
		var ordered = new List<string>();
		foreach (var part in (previous ?? "").Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)) {
			var tag = part.Trim();
			var hit = on.FirstOrDefault(x => string.Equals(x, tag, StringComparison.OrdinalIgnoreCase));
			if (hit == null) continue;
			ordered.Add(hit);
			set.Remove(hit);
		}
		foreach (var tag in on) {
			if (set.Contains(tag)) ordered.Add(tag);
		}
		return string.Join(",", ordered);
	}
}
