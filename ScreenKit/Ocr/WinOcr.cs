using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Graphics.Imaging;
using WinMedia = Windows.Media.Ocr;

namespace ScreenKit;

/// <summary>本机 Windows.Media.Ocr 的一种语言。</summary>
sealed class WinOcrLang {
	public string Tag { get; set; }
	public string Name { get; set; }
}

/// <summary>
/// Windows 系统 OCR（WinRT）。一次认一种语言，或中英各认一次再合并。不加载 ONNX 模型。
/// </summary>
static class WinOcr {
	public const string PackId = "winocr";
	public const string MIX_TAG = "zh+en";
	/// <summary>短边低于这个值就放大。大约 40 像素高的浅色小字，Windows OCR 只会剩最后一个词。</summary>
	const int MINSHORT = 72;
	/// <summary>墨水行矮于这个值就按 2 倍放大。大约 16 像素的路径行，原图和双线性放大都整行丢失。</summary>
	const int TEXTPX = 24;
	const int TAP = 6;

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

	/// <summary>语言列表。中文和英文都已安装时，最前面加「中英混合」。</summary>
	public static List<WinOcrLang> Choices() {
		var list = Languages();
		if (langpair(list, out _, out _))
			list.Insert(0, new WinOcrLang { Tag = MIX_TAG, Name = Loc.T("ocr.win.mix") });
		return list;
	}

	public static bool IsMix(string tag) =>
		string.Equals((tag ?? "").Trim(), MIX_TAG, StringComparison.OrdinalIgnoreCase);

	/// <summary>当前语言。配置为空或已不可用时，优先简体中文，其次英语，再退回第一种。</summary>
	public static string SelectedTag(OcrOptions opt) {
		var avail = Languages();
		if (avail.Count == 0) return "";
		var raw = opt?.WinOcrLangs ?? "";
		var first = raw.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0);
		if (IsMix(first) && langpair(avail, out _, out _)) return MIX_TAG;
		var hit = avail.FirstOrDefault(x =>
			string.Equals(x.Tag, first, StringComparison.OrdinalIgnoreCase));
		if (hit != null) return hit.Tag;
		return defaulttag(avail);
	}

	public static string Summary(OcrOptions opt) {
		var tag = SelectedTag(opt);
		if (string.IsNullOrEmpty(tag)) return Loc.T("ocr.win.empty");
		if (IsMix(tag)) return Loc.T("ocr.win.mix");
		var hit = Languages().FirstOrDefault(x =>
			string.Equals(x.Tag, tag, StringComparison.OrdinalIgnoreCase));
		return string.IsNullOrWhiteSpace(hit?.Name) ? tag : hit.Name;
	}

	public static OcrResult Recognize(OcrOptions opt, BitmapSource src) {
		if (src == null) throw new InvalidOperationException("图像为空");
		return RecognizeBgra(opt, copybgra(src), src.PixelWidth, src.PixelHeight);
	}

	/// <summary>BGRA32 紧排像素。不使用 OpenCV。</summary>
	public static OcrResult RecognizeBgra(OcrOptions opt, byte[] bgra, int width, int height) =>
		offsta(() => recognizebgra(opt, bgra, width, height));

	public static OcrResult RecognizeFile(OcrOptions opt, string path) {
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			throw new FileNotFoundException("图像不存在", path);
		return offsta(() => {
			using var fs = File.OpenRead(path);
			var bmp = decode(fs, out var w, out var h, out var scale, out var pad);
			var r = withsize(recognizebmp(opt, bmp, scale), w, h);
			shiftfrompad(r, pad, scale);
			return r;
		});
	}

	public static OcrResult RecognizeBytes(OcrOptions opt, byte[] image) {
		if (image == null || image.Length == 0)
			throw new InvalidOperationException("图像为空");
		return offsta(() => {
			using var ms = new MemoryStream(image, false);
			var bmp = decode(ms, out var w, out var h, out var scale, out var pad);
			var r = withsize(recognizebmp(opt, bmp, scale), w, h);
			shiftfrompad(r, pad, scale);
			return r;
		});
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

	/// <summary>引擎能接受的长边。检测边长不参与，只守这个上限。</summary>
	static int maxside() {
		try {
			var n = (int)WinMedia.OcrEngine.MaxImageDimension;
			if (n >= 256) return n;
		}
		catch { }
		return 10000;
	}

	/// <summary>准备送进引擎的图。不按检测边长缩小；字行太矮则 Lanczos 放大。</summary>
	/// <summary>四周留的背景边宽。字贴边时引擎会把偏旁拆开认。</summary>
	static int padof(int w, int h) {
		var s = Math.Min(w, h);
		var p = (int)Math.Round(s * 0.10);
		if (p < 12) p = 12;
		if (p > 64) p = 64;
		return p;
	}

	/// <summary>四周补一圈取四角平均色，中间原样拷贝。</summary>
	static byte[] padbgra(byte[] src, int w, int h, int pad) {
		var nw = w + pad * 2;
		var nh = h + pad * 2;
		var dst = new byte[nw * nh * 4];
		var acc = new int[3];
		var xs = new[] { Math.Min(2, w - 1), Math.Max(0, w - 3) };
		var ys = new[] { Math.Min(2, h - 1), Math.Max(0, h - 3) };
		var n = 0;
		foreach (var y in ys)
			foreach (var x in xs) {
				var i = (y * w + x) * 4;
				for (int c = 0; c < 3; c++) acc[c] += src[i + c];
				n++;
			}
		var bg = new byte[3];
		for (int c = 0; c < 3; c++) bg[c] = (byte)(n > 0 ? acc[c] / n : 255);
		for (int y = 0; y < nh; y++) {
			var sy = y - pad;
			var inside = sy >= 0 && sy < h;
			var srow = sy * w * 4;
			var drow = y * nw * 4;
			for (int x = 0; x < nw; x++) {
				var di = drow + x * 4;
				var sx = x - pad;
				if (inside && sx >= 0 && sx < w) {
					var si = srow + sx * 4;
					dst[di] = src[si];
					dst[di + 1] = src[si + 1];
					dst[di + 2] = src[si + 2];
					dst[di + 3] = src[si + 3];
				}
				else {
					dst[di] = bg[0];
					dst[di + 1] = bg[1];
					dst[di + 2] = bg[2];
					dst[di + 3] = 255;
				}
			}
		}
		return dst;
	}

	/// <summary>结果坐标从补边后的图换算回原图。</summary>
	static void shiftfrompad(OcrResult r, int pad, double scale) {
		if (r?.Lines == null || pad <= 0 || scale <= 0) return;
		var d = (float)(pad / scale);
		foreach (var ln in r.Lines) {
			if (ln?.Box == null) continue;
			for (int i = 0; i < ln.Box.Length; i++)
				ln.Box[i] = new Point2f(ln.Box[i].X - d, ln.Box[i].Y - d);
		}
	}

	static byte[] prepare(byte[] bgra, int width, int height, out int dw, out int dh, out double scale, out int pad) {
		var cap = maxside();
		scale = 1;
		pad = 0;
		dw = width;
		dh = height;
		var work = bgra;
		var maxs = Math.Max(width, height);
		if (maxs > cap) {
			scale = cap / (double)maxs;
			dw = Math.Max(1, (int)Math.Round(width * scale));
			dh = Math.Max(1, (int)Math.Round(height * scale));
			work = resizebgra(bgra, width, height, width * 4, dw, dh);
		}
		var grow = growof(work, dw, dh, cap);
		if (grow > 1.02) {
			var fw = Math.Max(1, (int)Math.Round(dw * grow));
			var fh = Math.Max(1, (int)Math.Round(dh * grow));
			work = lanczos(work, dw, dh, dw * 4, fw, fh);
			scale *= grow;
			dw = fw;
			dh = fh;
		}
		// 文字贴到图像边上时 Windows OCR 会把字认碎，四周补一圈背景色
		pad = padof(dw, dh);
		if (pad > 0) {
			work = padbgra(work, dw, dh, pad);
			dw += pad * 2;
			dh += pad * 2;
		}
		return work;
	}

	static double growof(byte[] bgra, int width, int height, int cap) {
		double grow = 1;
		var band = inkband(bgra, width, height);
		if (band >= 6 && band < TEXTPX) grow = 2;
		var maxs = Math.Max(width, height);
		if (maxs * grow > cap) grow = cap / (double)maxs;
		var mins = Math.Min(width, height) * grow;
		if (mins < MINSHORT && mins >= 1) {
			var up = MINSHORT / mins;
			if (maxs * grow * up > cap) up = cap / (maxs * grow);
			if (up > 1.02) grow *= up;
		}
		if (grow < 1) grow = 1;
		return grow;
	}

	/// <summary>最矮的一行墨水有多高。照片般的碎块返回 0，不再放大。</summary>
	static int inkband(byte[] bgra, int w, int h) {
		if (bgra == null || w < 8 || h < 8) return 0;
		int lum(int x, int y) {
			var i = (y * w + x) * 4;
			return (bgra[i] * 11 + bgra[i + 1] * 59 + bgra[i + 2] * 30) / 100;
		}
		var bg = (lum(2, 2) + lum(w - 3, 2) + lum(2, h - 3) + lum(w - 3, h - 3)) / 4;
		var need = Math.Max(6, w / 80);
		var ink = new bool[h];
		for (int y = 0; y < h; y++) {
			int n = 0;
			var row = y * w * 4;
			for (int x = 0; x < w; x++) {
				var i = row + x * 4;
				var v = (bgra[i] * 11 + bgra[i + 1] * 59 + bgra[i + 2] * 30) / 100;
				if (v < bg - 28 || v > bg + 28) n++;
			}
			ink[y] = n >= need;
		}
		int best = 0, run = 0, bands = 0;
		void close() {
			if (run >= 6) {
				bands++;
				if (best == 0 || run < best) best = run;
			}
			run = 0;
		}
		for (int y = 0; y < h; y++) {
			if (ink[y]) run++;
			else close();
		}
		close();
		if (bands == 0 || bands > 40) return 0;
		return best;
	}

	static OcrResult recognizebgra(OcrOptions opt, byte[] bgra, int width, int height) {
		if (bgra == null || width < 1 || height < 1)
			throw new InvalidOperationException("图像为空");
		var work = prepare(bgra, width, height, out var dw, out var dh, out var scale, out var pad);
		var bmp = SoftwareBitmap.CreateCopyFromBuffer(
			work.AsBuffer(), BitmapPixelFormat.Bgra8, dw, dh, BitmapAlphaMode.Ignore);
		var res = withsize(recognizebmp(opt, bmp, scale), width, height);
		shiftfrompad(res, pad, scale);
		return res;
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
			if (IsMix(tag)) return mixresult(bmp, scale, t0);
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
			byte[] px = null;
			var pw = bmp.PixelWidth;
			var ph = bmp.PixelHeight;
			try {
				px = new byte[pw * ph * 4];
				bmp.CopyToBuffer(px.AsBuffer());
			}
			catch { px = null; }
			var lines = new List<OcrLine>();
			var inv = (float)(1.0 / scale);
			if (raw?.Lines != null) {
				foreach (var line in raw.Lines) {
					foreach (var cell in linesfromrow(engine, line, px, pw, ph, inv))
						lines.Add(cell);
				}
			}
			mergelines(lines);
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

	/// <summary>中文、英文各认一次。汉字留中文结果，拉丁和数字用英文结果，按框对齐后拼行。</summary>
	static OcrResult mixresult(SoftwareBitmap bmp, double scale, int t0) {
		var avail = Languages();
		if (!langpair(avail, out var zh, out var en))
			throw new InvalidOperationException(Loc.T("ocr.win.empty"));
		var pw = bmp.PixelWidth;
		var ph = bmp.PixelHeight;
		byte[] px = null;
		try {
			px = new byte[pw * ph * 4];
			bmp.CopyToBuffer(px.AsBuffer());
		}
		catch { px = null; }
		var zheng = makeeng(zh);
		var eneng = makeeng(en);
		var zraw = recognize(zheng, bmp, zh);
		SoftwareBitmap copy = null;
		if (px != null) {
			try { copy = SoftwareBitmap.CreateCopyFromBuffer(px.AsBuffer(), BitmapPixelFormat.Bgra8, pw, ph); }
			catch { copy = null; }
		}
		WinMedia.OcrResult eraw = null;
		try { eraw = recognize(eneng, copy ?? bmp, en); }
		catch (Exception ex) {
			CaptureLog.Ex("WinOcr mix en", ex);
			eraw = null;
		}
		finally {
			if (copy != null) try { copy.Dispose(); } catch { }
		}
		var etok = eraw == null ? new List<MixTok>() : mixtoks(eraw);
		var inv = (float)(1.0 / scale);
		var lines = mixgridlines(zraw, zheng, px, pw, ph, etok, inv);
		if (lines.Count == 0) {
			var ztok = mixtoks(zraw);
			lines = mixlines(ztok, etok, inv);
		}
		return new OcrResult {
			Lines = lines,
			DeviceUsed = "Windows",
			ModelLabel = $"Windows OCR · {Loc.T("ocr.win.mix")}",
			InferMs = Math.Max(0, Environment.TickCount - t0),
		};
	}

	/// <summary>中英混合：按中文行内词框分列成单元格，格内再与英文词框 picklatin。</summary>
	static List<OcrLine> mixgridlines(WinMedia.OcrResult zraw, WinMedia.OcrEngine zheng, byte[] px, int pw, int ph, List<MixTok> etok, float inv) {
		var lines = new List<OcrLine>();
		if (zraw?.Lines == null) return lines;
		foreach (var band in grouprawlines(zraw.Lines)) {
			var words = new List<WinWord>();
			foreach (var line in band)
				words.AddRange(collectwords(zheng, line, px, pw, ph));
			words.Sort((a, b) => a.Rc.X.CompareTo(b.Rc.X));
			void putcell(OcrLine cell) {
				if (cell?.Box == null || cell.Box.Length < 4) return;
				// 词框是放大后的坐标，格的框已按 inv 缩回原图，这里换回放大尺度再比
				var l = (float)(cell.Box[0].X / inv);
				var t = (float)(cell.Box[0].Y / inv);
				var r = (float)(cell.Box[2].X / inv);
				var btm = (float)(cell.Box[2].Y / inv);
				cell.Text = fixmixline(tidy(pickcellmix(words, l, t, r, btm, etok)));
				if (string.IsNullOrEmpty(cell.Text)) return;
				lines.Add(cell);
			}
			var sep = repeatedsep(words.Select(w => w.Text));
			var parts = sep == null ? null : splitbysep(words, w => w.Text, sep);
			if (parts == null) {
				foreach (var cell in linesfromwords(words, inv))
					putcell(cell);
				continue;
			}
			foreach (var grp in parts)
				putcell(cellfromwords(grp, inv));
		}
		sortlines(lines);
		return lines;
	}

	/// <summary>格内按中文词框与英文词框对齐，只替换拉丁块，不丢掉汉字。</summary>
	static string pickcellmix(List<WinWord> rowWords, float l, float t, float r, float btm, List<MixTok> etok) {
		var cellWords = new List<WinWord>();
		foreach (var wd in rowWords) {
			var rc0 = wd.Rc;
			var cx0 = rc0.X + rc0.Width * 0.5;
			var cy0 = rc0.Y + rc0.Height * 0.5;
			if (cx0 < l - 2 || cx0 > r + 2 || cy0 < t - 2 || cy0 > btm + 2) continue;
			if (string.IsNullOrEmpty(wd.Text)) continue;
			cellWords.Add(wd);
		}
		var sb = new StringBuilder();
		WinWord prev = null;
		var used = new List<MixTok>();
		for (int i = 0; i < cellWords.Count; ) {
			var wd = cellWords[i];
			var zh = wd.Text ?? "";
			if (!runword(zh) || !hasalnum(zh)) {
				put(zh, wd);
				i++;
				continue;
			}
			// 连续的非汉字块整段和英文结果二选一，免得一段里的数字前后各挑一次
			var run = new List<WinWord> { wd };
			i++;
			while (i < cellWords.Count) {
				var t2 = cellWords[i].Text ?? "";
				if (!runword(t2)) break;
				if (hasalnum(t2)) {
					run.Add(cellWords[i]);
					i++;
					continue;
				}
				// 小数点、℃、× 这类夹在两个数字块中间，并进这一段
				if (i + 1 >= cellWords.Count) break;
				var nxt = cellWords[i + 1].Text ?? "";
				if (!hasalnum(nxt) || anycjk(nxt) || !punctlike(t2)) break;
				run.Add(cellWords[i]);
				i++;
			}
			var runText = new StringBuilder();
			foreach (var w2 in run) runText.Append(w2.Text);
			var hits = new List<MixTok>();
			foreach (var e in etok) {
				if (e == null || string.IsNullOrEmpty(e.Text) || used.Contains(e)) continue;
				var hit = false;
				foreach (var w2 in run)
					if (overlapsrect(e, w2.Rc)) { hit = true; break; }
				if (!hit) continue;
				hits.Add(e);
			}
			var piece = runText.ToString();
			if (hits.Count > 0) {
				hits.Sort((x, y) => x.L.CompareTo(y.L));
				piece = picklatin(piece, jointoks(hits));
				foreach (var e in hits) used.Add(e);
			}
			put(piece, run[0]);
		}
		return sb.ToString();

		void put(string piece, WinWord wd) {
			if (piece.Length == 0) return;
			if (prev != null && sb.Length > 0) {
				var gap = wd.Rc.X - (prev.Rc.X + prev.Rc.Width);
				var h = Math.Max(prev.Rc.Height, wd.Rc.Height);
				if (gap > Math.Max(3, h * 0.15) && needsword(sb[sb.Length - 1], piece[0]))
					sb.Append(' ');
			}
			sb.Append(piece);
			prev = wd;
		}
	}

	static bool overlapsrect(MixTok e, Windows.Foundation.Rect rc) {
		if (e == null) return false;
		var left = Math.Max(e.L, rc.X);
		var right = Math.Min(e.R, rc.X + rc.Width);
		if (right - left < 1) return false;
		var top = Math.Max(e.T, rc.Y);
		var bot = Math.Min(e.B, rc.Y + rc.Height);
		return bot - top >= Math.Min(e.H, rc.Height) * 0.35;
	}

	static WinMedia.OcrEngine makeeng(string tag) {
		var lang = new Windows.Globalization.Language(tag);
		var engine = WinMedia.OcrEngine.TryCreateFromLanguage(lang);
		if (engine == null) throw new InvalidOperationException(Loc.T("ocr.win.empty"));
		return engine;
	}

	static WinMedia.OcrResult recognize(WinMedia.OcrEngine engine, SoftwareBitmap bmp, string tag) {
		try { return wait(engine.RecognizeAsync(bmp).AsTask()); }
		catch (Exception ex) {
			CaptureLog.Ex("WinOcr " + tag, ex);
			throw new InvalidOperationException($"Windows OCR 失败: {ex.Message}", ex);
		}
	}

	static List<MixTok> mixtoks(WinMedia.OcrResult raw) {
		var list = new List<MixTok>();
		if (raw?.Lines == null) return list;
		foreach (var line in raw.Lines) {
			if (line?.Words == null) continue;
			foreach (var word in line.Words) {
				var text = word?.Text ?? "";
				if (text.Length == 0) continue;
				var r = word.BoundingRect;
				if (r.Width < 1 || r.Height < 1) continue;
				list.Add(new MixTok {
					Text = text,
					L = r.X,
					T = r.Y,
					R = r.X + r.Width,
					B = r.Y + r.Height,
				});
			}
		}
		return list;
	}

	/// <summary>不含汉字、且含字母或数字时，改用英文引擎的读法；极短的纯汉字误读块（如「主一」当 i）也走英文对齐。</summary>
	static bool preferen(string s) {
		if (string.IsNullOrEmpty(s)) return false;
		if (cjklatinmisread(s)) return true;
		var latin = false;
		foreach (var c in s) {
			if (iscjk(c)) return false;
			if (char.IsLetterOrDigit(c)) latin = true;
		}
		return latin;
	}

	static bool cjklatinmisread(string s) {
		if (string.IsNullOrEmpty(s) || s.Length > 8) return false;
		foreach (var c in s)
			if (!iscjk(c)) return false;
		if (s == "主一" || s == "囗" || s == "口尸曰") return true;
		if (s.Length <= 3 && s.IndexOf('主') >= 0) return true;
		return cjkradicalnoise(s);
	}

	static bool cjkradicalnoise(string s) {
		if (string.IsNullOrEmpty(s) || s.Length > 6) return false;
		foreach (var c in s)
			if (!iscjk(c)) return false;
		return s.IndexOf('囗') >= 0 || s.IndexOf('亻') >= 0 || s.IndexOf('讠') >= 0 || s.IndexOf('尸') >= 0;
	}

	static bool hasalnum(string s) {
		if (string.IsNullOrEmpty(s)) return false;
		foreach (var c in s)
			if (char.IsLetterOrDigit(c)) return true;
		return false;
	}

	/// <summary>能夹在两个数字块中间的符号，像小数点、℃、×。</summary>
	static bool punctlike(string s) {
		if (string.IsNullOrEmpty(s) || s.Length != 1) return false;
		var c = s[0];
		if (char.IsLetterOrDigit(c) || iscjk(c)) return false;
		return c != '|';
	}

	/// <summary>不含汉字的块（符号、数字、拉丁），整段按英文结果挑。</summary>
	static bool runword(string s) => !string.IsNullOrEmpty(s) && (cjklatinmisread(s) || !anycjk(s));

	static bool purelatin(string s) {
		if (string.IsNullOrEmpty(s)) return false;
		foreach (var c in s)
			if (iscjk(c)) return false;
		return true;
	}

	static bool anycjk(string s) {
		if (string.IsNullOrEmpty(s)) return false;
		foreach (var c in s)
			if (iscjk(c)) return true;
		return false;
	}

	static int cjkcharcount(string s) {
		if (string.IsNullOrEmpty(s)) return 0;
		int n = 0;
		foreach (var c in s)
			if (iscjk(c)) n++;
		return n;
	}

	static bool samerow(MixTok a, MixTok b) {
		if (a == null || b == null) return false;
		var top = Math.Max(a.T, b.T);
		var bot = Math.Min(a.B, b.B);
		var small = Math.Min(a.H, b.H);
		return small > 1 && bot - top >= small * 0.4;
	}

	static bool overlaps(MixTok a, MixTok b) {
		if (!samerow(a, b)) return false;
		var left = Math.Max(a.L, b.L);
		var right = Math.Min(a.R, b.R);
		var ov = right - left;
		if (ov <= 1) return false;
		var small = Math.Min(a.W, b.W);
		if (small < 1) return false;
		if (ov >= small * 0.35) return true;
		return b.Cx >= a.L && b.Cx <= a.R;
	}

	static List<OcrLine> mixlines(List<MixTok> zh, List<MixTok> en, float inv) {
		for (int i = 0; i < zh.Count; i++) {
			if (!cjklatinmisread(zh[i].Text)) continue;
			foreach (var e in en) {
				if (e.Used || !overlaps(zh[i], e)) continue;
				if (!purelatin(e.Text) || e.Text.Length > 4) continue;
				zh[i].Text = e.Text;
				e.Used = true;
				break;
			}
		}
		var kept = new List<MixTok>();
		var taken = new bool[zh.Count];
		for (int i = 0; i < zh.Count; i++) {
			if (taken[i] || !preferen(zh[i].Text)) continue;
			var hits = new List<MixTok>();
			foreach (var e in en)
				if (!e.Used && overlaps(zh[i], e)) hits.Add(e);
			if (hits.Count == 0) continue;
			var grew = true;
			while (grew) {
				grew = false;
				for (int j = 0; j < zh.Count; j++) {
					if (!preferen(zh[j].Text)) continue;
					var touch = false;
					foreach (var e in hits)
						if (overlaps(zh[j], e)) { touch = true; break; }
					if (!touch) continue;
					foreach (var e in en) {
						if (e.Used || hits.Contains(e)) continue;
						if (!overlaps(zh[j], e)) continue;
						hits.Add(e);
						grew = true;
					}
				}
			}
			double l = zh[i].L, t = zh[i].T, r = zh[i].R, b = zh[i].B;
			foreach (var e in hits) {
				e.Used = true;
				if (e.L < l) l = e.L;
				if (e.T < t) t = e.T;
				if (e.R > r) r = e.R;
				if (e.B > b) b = e.B;
			}
			var members = new List<MixTok>();
			for (int j = 0; j < zh.Count; j++) {
				if (taken[j]) continue;
				var tok = zh[j];
				var latin = preferen(tok.Text) && hits.Exists(e => overlaps(tok, e));
				var punct = ispuncttok(tok.Text) && tok.Cx >= l - 2 && tok.Cx <= r + 2 && samerow(tok, zh[i]);
				if (!latin && !punct) continue;
				taken[j] = true;
				members.Add(tok);
				if (tok.L < l) l = tok.L;
				if (tok.T < t) t = tok.T;
				if (tok.R > r) r = tok.R;
				if (tok.B > b) b = tok.B;
			}
			if (members.Count == 0) continue;
			hits.Sort((a, c) => a.L.CompareTo(c.L));
			kept.Add(new MixTok {
				Text = picklatin(jointoks(members), jointoks(hits)),
				L = l, T = t, R = r, B = b,
			});
		}
		for (int i = 0; i < zh.Count; i++)
			if (!taken[i]) kept.Add(zh[i]);
		foreach (var e in en) {
			if (e.Used || !preferen(e.Text)) continue;
			var hit = false;
			foreach (var k in kept) {
				if (overlaps(k, e)) { hit = true; break; }
			}
			if (!hit) kept.Add(e);
		}
		kept.Sort((a, b) => {
			var dy = a.Cy.CompareTo(b.Cy);
			return dy != 0 ? dy : a.L.CompareTo(b.L);
		});
		var rows = new List<List<MixTok>>();
		foreach (var t in kept) {
			List<MixTok> row = null;
			foreach (var cand in rows) {
				if (samerow(cand[cand.Count / 2], t)) { row = cand; break; }
			}
			if (row == null) {
				row = new List<MixTok>();
				rows.Add(row);
			}
			row.Add(t);
		}
		var lines = new List<OcrLine>();
		foreach (var row in rows)
			foreach (var ln in mixrowcells(row, inv))
				if (ln != null) lines.Add(ln);
		sortlines(lines);
		return lines;
	}

	static bool ispuncttok(string s) {
		if (string.IsNullOrEmpty(s)) return false;
		foreach (var c in s)
			if (char.IsLetterOrDigit(c) || iscjk(c)) return false;
		return true;
	}

	static string jointoks(List<MixTok> toks) {
		toks.Sort((a, b) => a.L.CompareTo(b.L));
		var sb = new StringBuilder();
		MixTok prev = null;
		foreach (var tok in toks) {
			if (prev != null && sb.Length > 0 && tok.Text.Length > 0) {
				var gap = tok.L - prev.R;
				var h = Math.Max(1, Math.Max(prev.H, tok.H));
				if (gap > Math.Max(3, h * 0.15) && needsword(sb[sb.Length - 1], tok.Text[0]))
					sb.Append(' ');
			}
			sb.Append(tok.Text);
			prev = tok;
		}
		return sb.ToString();
	}

	/// <summary>两边拉丁串对得上时，按空格和 0/O、1/l 这类易混字挑更干净的一版。</summary>
	static string picklatin(string zh, string en) {
		if (string.IsNullOrEmpty(en)) return zh ?? "";
		if (string.IsNullOrEmpty(zh)) return en;
		if (purelatin(en) && anycjk(zh)) {
			if (cjkcharcount(zh) >= 4) return zh;
			if (latinscore(en) >= latinscore(zh) + 2 && en.Length >= zh.Length * 0.55) return en;
			return zh;
		}
		if (purelatin(zh) && purelatin(en)) {
			var dz = digitcount(zh);
			var de = digitcount(en);
			if (de > dz && en.Length <= zh.Length + 2) return en;
			if (dz > de && zh.Length <= en.Length + 2) return zh;
		}
		var zs = skel(zh);
		var es = skel(en);
		if (zs.Length == 0) return en;
		if (es.Length == 0) return zh;
		if (zs == es) return spacemark(en) >= spacemark(zh) ? en : zh;
		if (sameconfuse(zs, es)) return applyconfuse(en, zs, es);
		if (es.StartsWith(zs) && es.Length == zs.Length + 1)
			return spacemark(zh) >= spacemark(en) ? zh : trimlastalnum(en);
		if (zs.StartsWith(es) && zs.Length == es.Length + 1)
			return spacemark(en) >= spacemark(zh) ? en : trimlastalnum(zh);
		if (cjkcharcount(zh) >= 4 && cjkcharcount(en) == 0 && skel(es).Length < skel(zs).Length * 0.45)
			return zh;
		var zsScore = latinscore(zh);
		var esScore = latinscore(en);
		if (esScore > zsScore + 1) return en;
		if (zsScore > esScore + 1) return zh;
		return dirt(en) < dirt(zh) ? en : zh;
	}

	static int digitcount(string s) {
		if (string.IsNullOrEmpty(s)) return 0;
		var n = 0;
		foreach (var c in s)
			if (char.IsDigit(c)) n++;
		return n;
	}

	static int latinscore(string s) {
		if (string.IsNullOrEmpty(s)) return -1000;
		var score = 0;
		if (s.Contains("@@")) score -= 24;
		var at = 0;
		foreach (var c in s)
			if (c == '@') at++;
		if (at == 1) score += 5;
		else if (at > 1) score -= 10 * (at - 1);
		foreach (var c in s)
			if (char.IsDigit(c)) score += 1;
		if (s.IndexOf("npm", StringComparison.OrdinalIgnoreCase) >= 0 && s.Contains("-g")) {
			if (Regex.IsMatch(s, @"npm\s+i\s+-g", RegexOptions.IgnoreCase)) score += 10;
			if (s.Contains("主")) score -= 18;
		}
		if (Regex.IsMatch(s, @"[A-Za-z]\s+[a-z](?=[/@])")) score -= 8;
		if (Regex.IsMatch(s, @"\.\s+exe\b", RegexOptions.IgnoreCase)) score -= 12;
		if (s.Contains("deepseek-a i") || s.Contains("deepseek-a  i")) score -= 15;
		score -= dirt(s);
		return score;
	}

	static string skel(string s) {
		var sb = new StringBuilder();
		foreach (var c in s)
			if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
		return sb.ToString();
	}

	static int spacemark(string s) {
		var score = 0;
		var parts = s.Split(' ');
		if (parts.Length <= 1) return 0;
		for (int i = 0; i < parts.Length; i++) {
			if (parts[i].Length == 0) { score -= 2; continue; }
			if (i == 0) continue;
			var one = parts[i].Length == 1 ? parts[i][0] : '\0';
			if (one != '\0' && one != 'a' && one != 'A' && one != 'I') score -= 3;
			else score += 1;
		}
		return score;
	}

	static int dirt(string s) {
		var d = 0;
		foreach (var raw in s.Split(' ')) {
			if (raw.Length == 0) continue;
			var let = false;
			var dig = false;
			var odd = false;
			var dot = false;
			foreach (var c in raw) {
				if (char.IsLetter(c)) let = true;
				else if (char.IsDigit(c)) dig = true;
				else if (c == '.') dot = true;
				else if (c != '\'' && c != '"' && c != '-' && c != ',') odd = true;
			}
			if (let && dig && !dot) d += 6;
			if (odd) d += 4;
			if (dot && (raw.IndexOf('O') >= 0 || raw.IndexOf('l') >= 0)) d += 3;
		}
		return d;
	}

	static int confgroup(char c) {
		c = char.ToLowerInvariant(c);
		if (c == '0' || c == 'o') return 1;
		if (c == '1' || c == 'l' || c == 'i') return 2;
		if (c == '5' || c == 's') return 3;
		return 0;
	}

	static bool sameconfuse(string zs, string es) {
		if (zs.Length != es.Length || zs.Length == 0) return false;
		for (int i = 0; i < zs.Length; i++) {
			if (zs[i] == es[i]) continue;
			var g = confgroup(zs[i]);
			if (g == 0 || g != confgroup(es[i])) return false;
		}
		return true;
	}

	static string applyconfuse(string en, string zs, string es) {
		var sb = new StringBuilder();
		var k = 0;
		for (int i = 0; i < en.Length; i++) {
			var c = en[i];
			if (!char.IsLetterOrDigit(c)) { sb.Append(c); continue; }
			var zc = k < zs.Length ? zs[k] : c;
			var ec = k < es.Length ? es[k] : c;
			k++;
			if (zc == ec) { sb.Append(c); continue; }
			var prev = i > 0 ? en[i - 1] : '\0';
			var next = i + 1 < en.Length ? en[i + 1] : '\0';
			var digitctx = prev == '.' || next == '.' || char.IsDigit(prev) || char.IsDigit(next);
			if (digitctx) {
				var g = confgroup(zc);
				if (g == 1) sb.Append('0');
				else if (g == 2) sb.Append('1');
				else if (g == 3) sb.Append('5');
				else sb.Append(c);
				continue;
			}
			if (char.IsLetter(c)) { sb.Append(c); continue; }
			var gg = confgroup(c);
			if (gg == 1) sb.Append('O');
			else if (gg == 2) sb.Append('l');
			else if (gg == 3) sb.Append('S');
			else sb.Append(c);
		}
		return sb.ToString();
	}

	static string trimlastalnum(string s) {
		for (int i = s.Length - 1; i >= 0; i--) {
			if (!char.IsLetterOrDigit(s[i])) continue;
			return s.Substring(0, i) + s.Substring(i + 1);
		}
		return s;
	}

	static string tidy(string s) {
		if (string.IsNullOrEmpty(s)) return s ?? "";
		var sb = new StringBuilder();
		for (int i = 0; i < s.Length; i++) {
			var c = s[i];
			if (c == ' ') {
				if (sb.Length > 0 && cjkpunct(sb[sb.Length - 1])) continue;
				if (i + 1 < s.Length && cjkpunct(s[i + 1])) continue;
			}
			if (foldpunct(c) != '\0') {
				if (i + 1 < s.Length && foldpunct(c) == foldpunct(s[i + 1])) continue;
				if (sb.Length > 0 && foldpunct(c) == foldpunct(sb[sb.Length - 1])) continue;
			}
			sb.Append(c);
		}
		return sb.ToString();
	}

	static bool cjkpunct(char c) => c is '，' or '。' or '：' or '；' or '、' or '！' or '？';

	static char foldpunct(char c) => c switch {
		',' or '，' => ',',
		'.' or '。' => '.',
		':' or '：' => ':',
		';' or '；' => ';',
		_ => '\0',
	};

	static bool langpair(List<WinOcrLang> avail, out string zh, out string en) {
		zh = picktag(avail, "zh-Hans-CN", "zh-CN", "zh-Hans");
		en = picktag(avail, "en-US", "en-GB", "en");
		return zh.Length > 0 && en.Length > 0;
	}

	static string picktag(List<WinOcrLang> avail, params string[] tags) {
		if (avail == null) return "";
		foreach (var tag in tags) {
			if (string.IsNullOrWhiteSpace(tag)) continue;
			foreach (var lang in avail) {
				if (string.Equals(lang.Tag, tag, StringComparison.OrdinalIgnoreCase))
					return lang.Tag;
			}
		}
		foreach (var tag in tags) {
			if (string.IsNullOrWhiteSpace(tag) || tag.Length < 2) continue;
			foreach (var lang in avail) {
				if (lang.Tag != null && lang.Tag.StartsWith(tag, StringComparison.OrdinalIgnoreCase))
					return lang.Tag;
			}
		}
		return "";
	}

	sealed class MixTok {
		public string Text;
		public double L, T, R, B;
		public bool Used;
		public double Cx => (L + R) * 0.5;
		public double Cy => (T + B) * 0.5;
		public double W => R - L;
		public double H => B - T;
	}

	static SoftwareBitmap decode(Stream stream, out int width, out int height, out double scale, out int pad) {
		using var ras = stream.AsRandomAccessStream();
		var decoder = wait(Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras).AsTask());
		var sw = (int)decoder.PixelWidth;
		var sh = (int)decoder.PixelHeight;
		if (sw < 1 || sh < 1) throw new InvalidOperationException("图像为空");
		width = sw;
		height = sh;
		var cap = maxside();
		var transform = new BitmapTransform {
			InterpolationMode = BitmapInterpolationMode.Linear,
		};
		var dw = sw;
		var dh = sh;
		if (Math.Max(sw, sh) > cap) {
			var pre = cap / (double)Math.Max(sw, sh);
			dw = Math.Max(1, (int)Math.Round(sw * pre));
			dh = Math.Max(1, (int)Math.Round(sh * pre));
			transform.ScaledWidth = (uint)dw;
			transform.ScaledHeight = (uint)dh;
		}
		var data = wait(decoder.GetPixelDataAsync(
			BitmapPixelFormat.Bgra8,
			BitmapAlphaMode.Ignore,
			transform,
			ExifOrientationMode.IgnoreExifOrientation,
			ColorManagementMode.DoNotColorManage).AsTask());
		var pixels = data.DetachPixelData();
		var work = prepare(pixels, dw, dh, out var fw, out var fh, out var rel, out pad);
		scale = (dw / (double)sw) * rel;
		return SoftwareBitmap.CreateCopyFromBuffer(
			work.AsBuffer(), BitmapPixelFormat.Bgra8, fw, fh, BitmapAlphaMode.Ignore);
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

	/// <summary>
	/// 词框之间按空隙补空格。中文引擎常把英文粘成一个词，再按画面词距拆开，和 Umi 一样。
	/// 标点贴着字时不插，避免 <c>D:\path</c> 被写成 <c>D : \path</c>。
	/// </summary>
	sealed class WinWord {
		public string Text;
		public Windows.Foundation.Rect Rc;
	}

	static List<WinWord> collectwords(WinMedia.OcrEngine engine, WinMedia.OcrLine line, byte[] px, int imgW, int imgH) {
		var list = new List<WinWord>();
		if (line?.Words == null) return list;
		foreach (var w in line.Words) {
			var t = w?.Text;
			if (string.IsNullOrEmpty(t)) continue;
			var rc = w.BoundingRect;
			t = fillword(engine, t, px, imgW, imgH, rc);
			if (t.Length == 0) continue;
			foreach (var piece in explodeword(engine, t, rc, px, imgW, imgH))
				list.Add(piece);
		}
		return list;
	}

	/// <summary>一个词框横跨多列时，按列对比度谷再拆成多个词框（仅数字/英文表格行，避免拆中文段落）。</summary>
	static IEnumerable<WinWord> explodeword(WinMedia.OcrEngine engine, string text, Windows.Foundation.Rect rc, byte[] px, int imgW, int imgH) {
		if (px == null || rc.Width < 48 || !shouldexplodeword(text, rc))
			return new[] { new WinWord { Text = text, Rc = rc } };
		var parts = visualparts(px, imgW, imgH, rc);
		if (parts == null || parts.Count <= 1) return new[] { new WinWord { Text = text, Rc = rc } };
		var bits = new List<string>();
		foreach (var p in parts) {
			var piece = readpiece(engine, px, imgW, p);
			if (string.IsNullOrWhiteSpace(piece)) return new[] { new WinWord { Text = text, Rc = rc } };
			bits.Add(piece.Trim());
		}
		var list = new List<WinWord>();
		for (int i = 0; i < parts.Count; i++) {
			var p = parts[i];
			var dw = p.x1 - p.x0 + 1;
			var dh = p.y1 - p.y0 + 1;
			list.Add(new WinWord {
				Text = bits[i],
				Rc = new Windows.Foundation.Rect(p.x0, p.y0, dw, dh),
			});
		}
		return list;
	}

	static int cjkchars(IEnumerable<WinWord> words) {
		int n = 0;
		if (words == null) return 0;
		foreach (var w in words)
			foreach (var c in w.Text ?? "")
				if (iscjk(c)) n++;
		return n;
	}

	static bool likestable(List<WinWord> words, float cut) {
		if (words == null || words.Count < 2 || cut >= float.MaxValue * 0.5f) return false;
		int big = 0, num = 0, maxI = 1;
		float maxG = 0;
		for (int i = 0; i < words.Count; i++) {
			var t = words[i].Text ?? "";
			if (t.Length > 0 && t.All(c => char.IsDigit(c) || c == '.' || c == '+' || c == '-' || c == 'Δ'))
				num++;
			if (i == 0) continue;
			var g = (float)(words[i].Rc.X - (words[i - 1].Rc.X + words[i - 1].Rc.Width));
			if (g > cut) big++;
			if (g > maxG) { maxG = g; maxI = i; }
		}
		var cjk = cjkchars(words);
		if (cjk > 18) return false;
		if (cjk > 8 && big < 2) return false;
		if (big >= 2) return true;
		if (big != 1 || words.Count < 4 || num < 2) return false;
		var left = new List<WinWord>();
		var right = new List<WinWord>();
		for (int i = 0; i < words.Count; i++) {
			if (i < maxI) left.Add(words[i]);
			else right.Add(words[i]);
		}
		if (cjkchars(left) > 0 && cjkchars(right) > 0) return false;
		return true;
	}

	static bool shouldexplodeword(string text, Windows.Foundation.Rect rc) {
		if (string.IsNullOrEmpty(text) || !haslatin(text)) return false;
		var cjk = 0;
		var lat = 0;
		foreach (var c in text) {
			if (iscjk(c)) cjk++;
			else if (char.IsLetterOrDigit(c)) lat++;
		}
		if (cjk >= lat) return false;
		if (text.IndexOf(' ') >= 0) return true;
		return lat >= 4 && rc.Width > text.Length * 6;
	}

	static float columncutpx(List<float> gaps, float avgH, float charW) {
		if (gaps == null || gaps.Count == 0) return float.MaxValue;
		gaps.Sort();
		var med = gaps[gaps.Count / 2];
		var mx = gaps[gaps.Count - 1];
		var mincut = Math.Max(charW * 2.2f, avgH * 0.38f);
		var need = Math.Max(mincut, med * 2.05f);
		if (mx < need) return float.MaxValue;
		return need;
	}

	static List<List<WinMedia.OcrLine>> grouprawlines(IReadOnlyList<WinMedia.OcrLine> lines) {
		var bands = new List<List<WinMedia.OcrLine>>();
		if (lines == null || lines.Count == 0) return bands;
		foreach (var line in lines) {
			if (line == null) continue;
			var box = linebox(line, 1f);
			if (box == null) { bands.Add(new List<WinMedia.OcrLine> { line }); continue; }
			var cy = (box[0].Y + box[2].Y) * 0.5f;
			var h = box[2].Y - box[0].Y;
			List<WinMedia.OcrLine> hit = null;
			foreach (var band in bands) {
				var b0 = linebox(band[0], 1f);
				if (b0 == null) continue;
				var bcy = (b0[0].Y + b0[2].Y) * 0.5f;
				var bh = b0[2].Y - b0[0].Y;
				if (Math.Abs(cy - bcy) <= Math.Max(h, bh) * 0.55f) { hit = band; break; }
			}
			if (hit == null) bands.Add(new List<WinMedia.OcrLine> { line });
			else hit.Add(line);
		}
		return bands;
	}

	static List<OcrLine> linesfromrow(WinMedia.OcrEngine engine, WinMedia.OcrLine line, byte[] px, int imgW, int imgH, float inv) {
		var words = collectwords(engine, line, px, imgW, imgH);
		return linesfromwords(words, inv, engine, line, px, imgW, imgH);
	}

	static List<OcrLine> linesfromwords(List<WinWord> words, float inv, WinMedia.OcrEngine engine = null, WinMedia.OcrLine line = null, byte[] px = null, int imgW = 0, int imgH = 0) {
		if (words.Count == 0) {
			if (line != null) {
				var t = normtext(line.Text);
				if (t.Length == 0) return new List<OcrLine>();
				var box = linebox(line, inv);
				if (box == null) return new List<OcrLine>();
				return new List<OcrLine> { new OcrLine { Text = t, Score = 1f, Box = box } };
			}
			return new List<OcrLine>();
		}
		var gaps = new List<float>();
		float hsum = 0, wsum = 0;
		int chars = 0;
		for (int i = 0; i < words.Count; i++) {
			hsum += (float)words[i].Rc.Height;
			wsum += (float)words[i].Rc.Width;
			chars += words[i].Text.Length;
			if (i == 0) continue;
			var g = (float)(words[i].Rc.X - (words[i - 1].Rc.X + words[i - 1].Rc.Width));
			if (g >= 0) gaps.Add(g);
		}
		var avgH = hsum / words.Count;
		var charW = chars > 0 ? wsum / chars : avgH;
		var cut = columncutpx(gaps, avgH, charW);
		if (!likestable(words, cut)) {
			var one = cellfromwords(words, inv);
			return one != null ? new List<OcrLine> { one } : new List<OcrLine>();
		}
		var groups = new List<List<WinWord>> { new List<WinWord> { words[0] } };
		for (int i = 1; i < words.Count; i++) {
			var g = (float)(words[i].Rc.X - (words[i - 1].Rc.X + words[i - 1].Rc.Width));
			if (g > cut) groups.Add(new List<WinWord>());
			groups[groups.Count - 1].Add(words[i]);
		}
		var outLines = new List<OcrLine>();
		foreach (var grp in groups) {
			var ln = cellfromwords(grp, inv);
			if (ln != null) outLines.Add(ln);
		}
		return outLines;
	}

	static OcrLine cellfromwords(List<WinWord> grp, float inv) {
		if (grp == null || grp.Count == 0) return null;
		var sb = new StringBuilder();
		double prevR = 0;
		var any = false;
		double l = 0, t = 0, r = 0, btm = 0;
		foreach (var wd in grp) {
			var rc = wd.Rc;
			var text = wd.Text;
			if (any) {
				var gap = rc.X - prevR;
				var h = rc.Height > 1 ? rc.Height : 12;
				if (gap > Math.Max(3, h * 0.18) && sb.Length > 0 && text.Length > 0 &&
					needsword(sb[sb.Length - 1], text[0]) && sb[sb.Length - 1] != ' ')
					sb.Append(' ');
			}
			sb.Append(text);
			if (!any) {
				l = rc.X; t = rc.Y; r = rc.X + rc.Width; btm = rc.Y + rc.Height;
				any = true;
			}
			else {
				if (rc.X < l) l = rc.X;
				if (rc.Y < t) t = rc.Y;
				if (rc.X + rc.Width > r) r = rc.X + rc.Width;
				if (rc.Y + rc.Height > btm) btm = rc.Y + rc.Height;
			}
			if (rc.Width > 0) prevR = rc.X + rc.Width;
		}
		var merged = fixmixline(normtext(sb.ToString()));
		if (merged.Length == 0) return null;
		return new OcrLine {
			Text = merged,
			Score = 1f,
			Box = new[] {
				new Point2f((float)l * inv, (float)t * inv),
				new Point2f((float)r * inv, (float)t * inv),
				new Point2f((float)r * inv, (float)btm * inv),
				new Point2f((float)l * inv, (float)btm * inv),
			},
		};
	}

	static List<OcrLine> mixrowcells(List<MixTok> row, float inv) {
		if (row == null || row.Count == 0) return new List<OcrLine>();
		row.Sort((a, b) => a.L.CompareTo(b.L));
		var sep = repeatedsep(row.Select(t => t.Text));
		var parts = splitbysep(row, t => t.Text, sep);
		if (parts != null) {
			var outLines = new List<OcrLine>();
			foreach (var grp in parts) {
				var ln = mixrowone(grp, inv);
				if (ln != null) outLines.Add(ln);
			}
			return outLines;
		}
		var gaps = new List<float>();
		float hsum = 0, wsum = 0;
		int chars = 0;
		for (int i = 0; i < row.Count; i++) {
			hsum += (float)row[i].H;
			wsum += (float)row[i].W;
			chars += row[i].Text?.Length ?? 0;
			if (i == 0) continue;
			var g = (float)(row[i].L - row[i - 1].R);
			if (g >= 0) gaps.Add(g);
		}
		var avgH = hsum / row.Count;
		var charW = chars > 0 ? (float)(wsum / chars) : avgH;
		var cut = columncutpx(gaps, avgH, charW);
		var wordish = new List<WinWord>();
		foreach (var tok in row)
			wordish.Add(new WinWord { Text = tok.Text, Rc = new Windows.Foundation.Rect(tok.L, tok.T, tok.W, tok.H) });
		if (!likestable(wordish, cut)) {
			var one = mixrowone(row, inv);
			return one != null ? new List<OcrLine> { one } : new List<OcrLine>();
		}
		var groups = new List<List<MixTok>> { new List<MixTok> { row[0] } };
		for (int i = 1; i < row.Count; i++) {
			var g = (float)(row[i].L - row[i - 1].R);
			if (g > cut) groups.Add(new List<MixTok>());
			groups[groups.Count - 1].Add(row[i]);
		}
		var lines = new List<OcrLine>();
		foreach (var grp in groups) {
			var ln = mixrowone(grp, inv);
			if (ln != null) lines.Add(ln);
		}
		return lines;
	}

	static OcrLine mixrowone(List<MixTok> grp, float inv) {
		if (grp == null || grp.Count == 0) return null;
		var sb = new StringBuilder();
		MixTok prev = null;
		double l = 0, t = 0, r = 0, btm = 0;
		var any = false;
		foreach (var tok in grp) {
			if (prev != null) {
				var gap = tok.L - prev.R;
				var h = Math.Max(1, Math.Max(prev.H, tok.H));
				if (gap > Math.Max(3, h * 0.15) && sb.Length > 0 && tok.Text.Length > 0 &&
					needsword(sb[sb.Length - 1], tok.Text[0]))
					sb.Append(' ');
			}
			sb.Append(tok.Text);
			if (!any) { l = tok.L; t = tok.T; r = tok.R; btm = tok.B; any = true; }
			else {
				if (tok.L < l) l = tok.L;
				if (tok.T < t) t = tok.T;
				if (tok.R > r) r = tok.R;
				if (tok.B > btm) btm = tok.B;
			}
			prev = tok;
		}
		var text = fixmixline(tidy(normtext(sb.ToString())));
		if (text.Length == 0 || !any) return null;
		return new OcrLine {
			Text = text,
			Score = 1f,
			Box = new[] {
				new Point2f((float)l * inv, (float)t * inv),
				new Point2f((float)r * inv, (float)t * inv),
				new Point2f((float)r * inv, (float)btm * inv),
				new Point2f((float)l * inv, (float)btm * inv),
			},
		};
	}

	static string linewords(WinMedia.OcrEngine engine, WinMedia.OcrLine line, byte[] px, int imgW, int imgH) {
		var cells = linesfromrow(engine, line, px, imgW, imgH, 1f);
		if (cells.Count == 0) return normtext(line?.Text);
		if (cells.Count == 1) return cells[0].Text;
		var sb = new StringBuilder();
		for (int i = 0; i < cells.Count; i++) {
			if (i > 0) sb.Append('\t');
			sb.Append(cells[i].Text);
		}
		return sb.ToString();
	}

	/// <summary>一个词框里若有多段墨水，按词距拆开再各认一次。纯中文不拆。</summary>
	static string fillword(WinMedia.OcrEngine engine, string text, byte[] px, int imgW, int imgH, Windows.Foundation.Rect rc) {
		if (string.IsNullOrEmpty(text) || px == null || !haslatin(text)) return text ?? "";
		var parts = visualparts(px, imgW, imgH, rc);
		if (parts == null || parts.Count <= 1) return text;
		var spaces = 0;
		foreach (var c in text)
			if (c == ' ') spaces++;
		if (spaces + 1 >= parts.Count) return text;
		var bits = new List<string>();
		foreach (var p in parts) {
			var piece = readpiece(engine, px, imgW, p);
			if (string.IsNullOrWhiteSpace(piece)) return text;
			bits.Add(piece.Trim());
		}
		var joined = joinbits(bits);
		if (text.Length > 0 && joined.Length > 0) {
			var end = text[text.Length - 1];
			if (end is '.' or ',' or '!' or '?' or '。' && joined[joined.Length - 1] != end)
				joined += end;
		}
		return joined;
	}

	static bool haslatin(string s) {
		foreach (var c in s) {
			if (c >= 'A' && c <= 'Z') return true;
			if (c >= 'a' && c <= 'z') return true;
		}
		return false;
	}

	/// <summary>词框内按列对比度找出被词距分开的墨水段。坐标是原图像素，含上下边。</summary>
	static List<(int x0, int y0, int x1, int y1)> visualparts(byte[] bgra, int imgW, int imgH, Windows.Foundation.Rect rc) {
		if (bgra == null || imgW < 2 || imgH < 2 || rc.Width < 4 || rc.Height < 4) return null;
		int x0 = (int)Math.Floor(rc.X);
		int y0 = (int)Math.Floor(rc.Y);
		int x1 = (int)Math.Ceiling(rc.X + rc.Width) - 1;
		int y1 = (int)Math.Ceiling(rc.Y + rc.Height) - 1;
		if (x0 < 0) x0 = 0;
		if (y0 < 0) y0 = 0;
		if (x1 >= imgW) x1 = imgW - 1;
		if (y1 >= imgH) y1 = imgH - 1;
		int ww = x1 - x0 + 1;
		int hh = y1 - y0 + 1;
		if (ww < 8 || hh < 6) return null;
		var contrast = new int[ww];
		int maxC = 0;
		for (int xi = 0; xi < ww; xi++) {
			int mn = 255, mx = 0;
			int xx = x0 + xi;
			for (int yi = 0; yi < hh; yi++) {
				int i = ((y0 + yi) * imgW + xx) * 4;
				int v = (bgra[i + 2] * 30 + bgra[i + 1] * 59 + bgra[i] * 11) / 100;
				if (v < mn) mn = v;
				if (v > mx) mx = v;
			}
			var c = mx - mn;
			contrast[xi] = c;
			if (c > maxC) maxC = c;
		}
		if (maxC < 10) return null;
		int th = Math.Max(12, maxC / 4);
		int minGap = Math.Max(5, (int)Math.Round(hh * 0.18));
		var runs = new List<(int a, int b)>();
		int run = -1;
		for (int xi = 0; xi <= ww; xi++) {
			var ink = xi < ww && contrast[xi] >= th;
			if (ink) {
				if (run < 0) run = xi;
			}
			else if (run >= 0) {
				runs.Add((run, xi - 1));
				run = -1;
			}
		}
		if (runs.Count <= 1) return null;
		var parts = new List<(int x0, int y0, int x1, int y1)>();
		int pa = runs[0].a, pb = runs[0].b;
		for (int i = 1; i < runs.Count; i++) {
			int gap = runs[i].a - pb - 1;
			if (gap >= minGap) {
				parts.Add((x0 + pa, y0, x0 + pb, y1));
				pa = runs[i].a;
			}
			pb = runs[i].b;
		}
		parts.Add((x0 + pa, y0, x0 + pb, y1));
		return parts.Count <= 1 ? null : parts;
	}

	static string readpiece(WinMedia.OcrEngine engine, byte[] bgra, int imgW, (int x0, int y0, int x1, int y1) p) {
		if (engine == null) return "";
		int pad = 6;
		int dw = p.x1 - p.x0 + 1 + pad * 2;
		int dh = p.y1 - p.y0 + 1 + pad * 2;
		if (dw < 1 || dh < 1 || dw > 8000 || dh > 8000) return "";
		var crop = new byte[dw * dh * 4];
		int si = (p.y0 * imgW + p.x0) * 4;
		byte b0 = bgra[si], g0 = bgra[si + 1], r0 = bgra[si + 2];
		for (int i = 0; i < crop.Length; i += 4) {
			crop[i] = b0;
			crop[i + 1] = g0;
			crop[i + 2] = r0;
			crop[i + 3] = 255;
		}
		int rowBytes = (p.x1 - p.x0 + 1) * 4;
		for (int y = p.y0; y <= p.y1; y++) {
			int src = (y * imgW + p.x0) * 4;
			int dst = ((y - p.y0 + pad) * dw + pad) * 4;
			Buffer.BlockCopy(bgra, src, crop, dst, rowBytes);
		}
		var bmp = SoftwareBitmap.CreateCopyFromBuffer(
			crop.AsBuffer(), BitmapPixelFormat.Bgra8, dw, dh, BitmapAlphaMode.Ignore);
		try {
			var raw = wait(engine.RecognizeAsync(bmp).AsTask());
			return readraw(raw);
		}
		catch { return ""; }
		finally { try { bmp.Dispose(); } catch { } }
	}

	static string readraw(WinMedia.OcrResult raw) {
		if (raw?.Lines == null) return "";
		var sb = new StringBuilder();
		foreach (var line in raw.Lines) {
			if (line?.Words == null) continue;
			foreach (var w in line.Words) {
				if (string.IsNullOrEmpty(w?.Text)) continue;
				if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
				sb.Append(w.Text);
			}
		}
		return sb.ToString().Trim();
	}

	static string joinbits(List<string> bits) {
		var sb = new StringBuilder();
		foreach (var bit in bits) {
			if (string.IsNullOrWhiteSpace(bit)) continue;
			var t = bit.Trim();
			if (sb.Length > 0 && needsword(sb[sb.Length - 1], t[0]) && sb[sb.Length - 1] != ' ' && t[0] != ' ')
				sb.Append(' ');
			sb.Append(t);
		}
		return sb.ToString();
	}

	static bool needsword(char a, char b) {
		if (a == '\0' || b == '\0' || a == ' ' || b == ' ') return false;
		if (iscjk(a) && iscjk(b)) return false;
		if (b is ',' or '.' or ';' or ':' or '!' or '?' or ')' or ']' or '}' or '%' or '\\' or '/') return false;
		if (a is '(' or '[' or '{' or '\\' or '/') return false;
		if (b == '@' && (a == 'g' || a == 'G')) return true;
		if (a == '@' || b == '@') return false;
		if (char.IsDigit(a) && b == '.') return false;
		if (a == '.' && (char.IsDigit(b) || b == 'I' || b == 'l')) return false;
		if (char.IsDigit(a) && b == '-') return false;
		if (a == '-' && (char.IsDigit(b) || char.IsLetter(b))) return false;
		if (a == '/' || b == '/') return false;
		if (char.IsLetter(a) && char.IsLetter(b) && a == 'a' && b == 'i') return false;
		if (char.IsLetter(a) && b == '/') return false;
		return true;
	}

	static double l3(double x) {
		x = Math.Abs(x);
		if (x < 1e-8) return 1;
		if (x >= 3) return 0;
		var p = Math.PI * x;
		var q = p / 3;
		return Math.Sin(p) / p * Math.Sin(q) / q;
	}

	static void fillw(int dn, int sn, int[] idx, double[] wt) {
		for (int x = 0; x < dn; x++) {
			var sx = (x + 0.5) * sn / (double)dn - 0.5;
			var i0 = (int)Math.Floor(sx) - 2;
			double sum = 0;
			for (int k = 0; k < TAP; k++) {
				var si = i0 + k;
				if (si < 0) si = 0;
				if (si >= sn) si = sn - 1;
				idx[x * TAP + k] = si;
				var lw = l3(sx - (i0 + k));
				wt[x * TAP + k] = lw;
				sum += lw;
			}
			if (Math.Abs(sum) < 1e-6) sum = 1;
			for (int k = 0; k < TAP; k++) wt[x * TAP + k] /= sum;
		}
	}

	/// <summary>可分离 Lanczos-3。双线性放大后，大约 16 像素的第二行会整行丢失。</summary>
	static byte[] lanczos(byte[] src, int sw, int sh, int sstride, int dw, int dh) {
		var xidx = new int[dw * TAP];
		var xwt = new double[dw * TAP];
		fillw(dw, sw, xidx, xwt);
		var tmp = new float[dw * sh * 4];
		for (int y = 0; y < sh; y++) {
			var row = y * sstride;
			for (int x = 0; x < dw; x++) {
				var di = (y * dw + x) * 4;
				var b = x * TAP;
				for (int c = 0; c < 4; c++) {
					double v = 0;
					for (int k = 0; k < TAP; k++)
						v += src[row + xidx[b + k] * 4 + c] * xwt[b + k];
					if (v < 0) v = 0;
					if (v > 255) v = 255;
					tmp[di + c] = (float)v;
				}
			}
		}
		var yidx = new int[dh * TAP];
		var ywt = new double[dh * TAP];
		fillw(dh, sh, yidx, ywt);
		var dst = new byte[dw * dh * 4];
		for (int y = 0; y < dh; y++) {
			var b = y * TAP;
			for (int x = 0; x < dw; x++) {
				var di = (y * dw + x) * 4;
				for (int c = 0; c < 4; c++) {
					double v = 0;
					for (int k = 0; k < TAP; k++)
						v += tmp[(yidx[b + k] * dw + x) * 4 + c] * ywt[b + k];
					if (v < 0) v = 0;
					if (v > 255) v = 255;
					dst[di + c] = (byte)Math.Round(v);
				}
			}
		}
		return dst;
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

	/// <summary>中英混合行：修版本号/npm 误空格、误选中文块等。</summary>
	static string fixmixline(string s) {
		if (string.IsNullOrEmpty(s)) return s ?? "";
		if (s.Length > 1 && s[0] == '众' && s[1] == '检') s = s.Substring(1);
		s = Regex.Replace(s, @"[—–―]", "-");
		s = Regex.Replace(s, @"\bnprn\b", "npm", RegexOptions.IgnoreCase);
		s = Regex.Replace(s, @"deepseek—a", "deepseek-ai", RegexOptions.IgnoreCase);
		s = Regex.Replace(s, @"npm\s*主一\s*", "npm i ", RegexOptions.IgnoreCase);
		s = Regex.Replace(s, @"npm\s+i\s+一-g", "npm i -g");
		s = Regex.Replace(s, @"npm\s+i\s*[-—一]+\s*g", "npm i -g", RegexOptions.IgnoreCase);
		s = Regex.Replace(s, @"npm\s+i\s*-g", "npm i -g", RegexOptions.IgnoreCase);
		for (int pass = 0; pass < 10; pass++) {
			var t = Regex.Replace(s, @"(\d)\s+\.", "$1.");
			t = Regex.Replace(t, @"\.\s+(\d)", ".$1");
			t = Regex.Replace(t, @"[·•]", ".");
			t = Regex.Replace(t, @"\.\s*([Il])\s*(?=-rc)", ".1", RegexOptions.IgnoreCase);
			t = Regex.Replace(t, @"\.[Il]\s*-rc", ".1-rc", RegexOptions.IgnoreCase);
			t = Regex.Replace(t, @"\(O\.", "(0.");
			t = Regex.Replace(t, @"(\d\.)[OØ](-rc)", "${1}0$2");
			if (t == s) break;
			s = t;
		}
		s = Regex.Replace(s, @"(/dsh)@@", "$1@0", RegexOptions.IgnoreCase);
		s = Regex.Replace(s, @"\.(@)-rc", ".1-rc", RegexOptions.IgnoreCase);
		s = Regex.Replace(s, @"-g@", "-g @", RegexOptions.IgnoreCase);
		s = Regex.Replace(s, @"@[\w-]+(?:\s+[\w-]+)+", m => m.Value.Replace(" ", ""));
		s = Regex.Replace(s, @"/([\w-]+(?:\s+[\w-]+)+)@", m => "/" + Regex.Replace(m.Groups[1].Value, @"\s+", "") + "@");
		s = Regex.Replace(s, @"\b([\w-]{2,})\.\s+exe\b", "$1.exe", RegexOptions.IgnoreCase);
		s = Regex.Replace(s, @"\)\s*[一一寸]\s*，", ")，");
		s = Regex.Replace(s, @"\(\s+", "(");
		s = Regex.Replace(s, @"\s+\)", ")");
		s = Regex.Replace(s,
			@"(\d+\.\d+(?:\.\d+)?(?:-[A-Za-z0-9.]+)?)\s*/\s*(\d+\.\d+(?:\.\d+)?)",
			"$1 / $2");
		s = Regex.Replace(s, @"(-rc\.\d+)/(\d+\.\d+)", "$1 / $2");
		return s;
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
				if (sb.Length > 0 && prev == '.' && char.IsLetter(next) && next != ' ') continue;
				if (sb.Length > 0 && char.IsLetter(prev) && next == '.') continue;
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

	/// <summary>同一基线上被拆开的字拼成一行。横向空档相对整行很宽时仍分成两列。</summary>
	static void mergelines(List<OcrLine> lines) {
		if (lines == null || lines.Count < 2) return;
		var items = new List<Seg>();
		foreach (var ln in lines) {
			if (!segof(ln, out var seg)) continue;
			items.Add(seg);
		}
		if (items.Count < 2) return;
		items.Sort((a, b) => {
			var dy = a.Cy - b.Cy;
			if (Math.Abs(dy) > 1f) return dy < 0 ? -1 : 1;
			return a.L.CompareTo(b.L);
		});
		var rows = new List<List<Seg>>();
		foreach (var it in items) {
			List<Seg> hit = null;
			var best = float.MaxValue;
			foreach (var row in rows) {
				if (!samerow(row, it)) continue;
				var d = Math.Abs(rowcy(row) - it.Cy);
				if (d < best) { best = d; hit = row; }
			}
			if (hit == null) rows.Add(new List<Seg> { it });
			else hit.Add(it);
		}
		lines.Clear();
		foreach (var row in rows) {
			row.Sort((a, b) => a.L.CompareTo(b.L));
			var groups = splitgaps(row);
			foreach (var g in groups) {
				var joined = joingroup(g);
				if (joined != null) lines.Add(joined);
			}
		}
	}

	sealed class Seg {
		public OcrLine Line;
		public float L, T, R, B;
		public float Cy;
		public float H;
	}

	static bool segof(OcrLine ln, out Seg seg) {
		seg = null;
		if (ln?.Box == null || ln.Box.Length == 0 || string.IsNullOrEmpty(ln.Text)) return false;
		float l = float.MaxValue, t = float.MaxValue, r = float.MinValue, b = float.MinValue;
		foreach (var p in ln.Box) {
			if (p.X < l) l = p.X;
			if (p.Y < t) t = p.Y;
			if (p.X > r) r = p.X;
			if (p.Y > b) b = p.Y;
		}
		if (r <= l || b <= t) return false;
		seg = new Seg { Line = ln, L = l, T = t, R = r, B = b, Cy = (t + b) * 0.5f, H = b - t };
		return true;
	}

	static float rowcy(List<Seg> row) {
		float cy = 0;
		foreach (var s in row) cy += s.Cy;
		return cy / row.Count;
	}

	static bool samerow(List<Seg> row, Seg it) {
		float cy = 0, h = 0;
		foreach (var s in row) { cy += s.Cy; h += s.H; }
		cy /= row.Count;
		h /= row.Count;
		if (Math.Abs(it.Cy - cy) > 0.55f * Math.Max(h, it.H)) return false;
		var top = cy - h * 0.5f;
		var bot = cy + h * 0.5f;
		var ov = Math.Min(bot, it.B) - Math.Max(top, it.T);
		return ov >= 0.35f * Math.Min(h, it.H);
	}

	static float charw(List<Seg> row) {
		var ws = new List<float>();
		foreach (var s in row) {
			var n = s.Line.Text?.Length ?? 0;
			if (n < 2) continue;
			var w = (s.R - s.L) / n;
			if (w > 0.5f) ws.Add(w);
		}
		if (ws.Count == 0) return 8f;
		ws.Sort();
		return ws[ws.Count / 2];
	}

	/// <summary>一排卡片、按钮里常夹着图标，OCR 会把图标认成一个反复出现的字。取出现最多的那个单字。</summary>
	static string repeatedsep(IEnumerable<string> texts) {
		var cnt = new Dictionary<string, int>(StringComparer.Ordinal);
		if (texts != null) {
			foreach (var raw in texts) {
				var t = (raw ?? "").Trim();
				if (t.Length != 1 || !iscjk(t[0])) continue;
				cnt[t] = cnt.TryGetValue(t, out var n) ? n + 1 : 1;
			}
		}
		string best = null;
		var max = 0;
		foreach (var kv in cnt) {
			if (kv.Value > max) { max = kv.Value; best = kv.Key; }
		}
		return max >= 3 ? best : null;
	}

	/// <summary>按这个单字切开。切出来每段都至少两个字才认，否则当普通一行处理。</summary>
	static List<List<T>> splitbysep<T>(List<T> row, Func<T, string> textof, string sep) {
		if (row == null || string.IsNullOrEmpty(sep)) return null;
		var groups = new List<List<T>>();
		var cur = new List<T>();
		foreach (var it in row) {
			if (string.Equals((textof(it) ?? "").Trim(), sep, StringComparison.Ordinal)) {
				if (cur.Count > 0) { groups.Add(cur); cur = new List<T>(); }
				continue;
			}
			cur.Add(it);
		}
		if (cur.Count > 0) groups.Add(cur);
		if (groups.Count < 3) return null;
		foreach (var g in groups) {
			var n = 0;
			foreach (var it in g) n += (textof(it) ?? "").Trim().Length;
			if (n < 2) return null;
		}
		return groups;
	}

	static List<List<Seg>> splitgaps(List<Seg> row) {
		var groups = new List<List<Seg>>();
		var cur = new List<Seg> { row[0] };
		groups.Add(cur);
		if (row.Count == 1) return groups;
		var sep = repeatedsep(row.Select(s => s.Line.Text));
		var parts = splitbysep(row, s => s.Line.Text, sep);
		if (parts != null) return parts;
		var cw = charw(row);
		var span = row[row.Count - 1].R - row[0].L;
		var cut = Math.Max(cw * 10f, span * 0.12f);
		for (int i = 1; i < row.Count; i++) {
			var gap = row[i].L - row[i - 1].R;
			if (gap > cut) {
				cur = new List<Seg>();
				groups.Add(cur);
			}
			cur.Add(row[i]);
		}
		return groups;
	}

	static OcrLine joingroup(List<Seg> g) {
		if (g.Count == 1) return g[0].Line;
		var cw = charw(g);
		var sb = new StringBuilder();
		float l = float.MaxValue, t = float.MaxValue, r = float.MinValue, btm = float.MinValue;
		for (int i = 0; i < g.Count; i++) {
			var s = g[i];
			if (s.L < l) l = s.L;
			if (s.T < t) t = s.T;
			if (s.R > r) r = s.R;
			if (s.B > btm) btm = s.B;
			if (i > 0 && needspace(sb, s.Line.Text, s.L - g[i - 1].R, cw)
				&& needsword(sb[sb.Length - 1], s.Line.Text[0]))
				sb.Append(' ');
			sb.Append(s.Line.Text);
		}
		var text = fixmixline(normtext(sb.ToString()));
		if (text.Length == 0) return null;
		return new OcrLine {
			Text = text,
			Score = 1f,
			Box = new[] {
				new Point2f(l, t),
				new Point2f(r, t),
				new Point2f(r, btm),
				new Point2f(l, btm),
			},
		};
	}

	static bool needspace(StringBuilder sb, string next, float gap, float cw) {
		if (gap <= cw * 0.62f) return false;
		if (sb.Length == 0 || string.IsNullOrEmpty(next)) return false;
		if (sb[sb.Length - 1] == ' ' || next[0] == ' ') return false;
		return true;
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
