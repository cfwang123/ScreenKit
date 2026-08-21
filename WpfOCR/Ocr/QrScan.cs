using System.Collections.Generic;
using System.Text;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using ZXingCpp;

namespace WpfOCR;

/// <summary>单条码结果（二维码 / 一维条码等）。</summary>
sealed class QrHit {
	/// <summary>码类型，如 QRCode、EAN13、Code39。</summary>
	public string Type;
	public string Text;
	public Point2f[] Box;
}

/// <summary>整图条码扫描结果。</summary>
sealed class QrResult {
	public List<QrHit> Codes = new();
	public int InferMs;

	public string FullText {
		get {
			if (Codes == null || Codes.Count == 0) return "";
			var sb = new StringBuilder();
			foreach (var c in Codes) {
				if (c == null || string.IsNullOrEmpty(c.Text)) continue;
				if (sb.Length > 0) sb.AppendLine();
				var typ = string.IsNullOrEmpty(c.Type) ? "UNKNOWN" : c.Type;
				sb.Append('[').Append(typ).Append("] ").Append(c.Text);
			}
			return sb.ToString();
		}
	}

	public int DecodedCount {
		get {
			if (Codes == null) return 0;
			var n = 0;
			foreach (var c in Codes)
				if (!string.IsNullOrEmpty(c?.Text)) n++;
			return n;
		}
	}
}

/// <summary>
/// 条码识别：ZXingCpp（zxing-cpp 原生，快且稳）。
/// 策略：原图 → 必要时放大 → 底区裁剪放大；命中即停，避免重预处理堆叠。
/// </summary>
static class QrScan {
	public static QrResult Run(BitmapSource bmp) {
		if (bmp == null) throw new ArgumentNullException(nameof(bmp));
		NativeRuntime.EnsureOpenCv();
		var t0 = Environment.TickCount;
		using var mat = ImageUtil.Tobgr(bmp);
		return runmat(mat, t0);
	}

	public static QrResult Run(Mat bgr) {
		if (bgr == null || bgr.Empty()) throw new ArgumentException("empty image");
		return runmat(bgr, Environment.TickCount);
	}

	public static QrResult Run(byte[] imageBytes) {
		if (imageBytes == null || imageBytes.Length == 0)
			throw new ArgumentException("empty bytes");
		NativeRuntime.EnsureOpenCv();
		var t0 = Environment.TickCount;
		using var mat = Cv2.ImDecode(imageBytes, ImreadModes.Color);
		if (mat == null || mat.Empty())
			throw new InvalidOperationException("无法解码图片");
		return runmat(mat, t0);
	}

	static QrResult runmat(Mat bgr, int t0) {
		var hits = decodepipeline(bgr);
		// 极少数 QR 难图：OpenCV 补一枪（轻量）
		if (hits.Count == 0)
			tryopencvqr(bgr, hits);
		dedupe(hits);
		return new QrResult {
			Codes = hits,
			InferMs = Math.Max(0, Environment.TickCount - t0),
		};
	}

	/// <summary>快路径优先，命中即返回。</summary>
	static List<QrHit> decodepipeline(Mat bgr) {
		// 1) 原图
		var hits = decodeonce(bgr, 1f, 1f, 0, 0);
		if (hits.Count > 0) return hits;

		var w = bgr.Width;
		var h = bgr.Height;
		var longSide = Math.Max(w, h);

		// 2) 手机式「拉近」：长边不足约 2400 时放大（证件斜拍 CODE39 常需 ≥1.5x）
		if (longSide < 2400) {
			var s = Math.Max(1.5, 2400.0 / longSide);
			if (s < 3.5) {
				using var scaled = new Mat();
				Cv2.Resize(bgr, scaled, new OpenCvSharp.Size(), s, s, InterpolationFlags.Cubic);
				hits = decodeonce(scaled, (float)s, (float)s, 0, 0);
				if (hits.Count > 0) return hits;
			}
		}

		// 3) 底区裁剪 ×2（行驶证/快递单条码常在下方）
		var y0 = (int)(h * 0.58);
		if (y0 < h - 16) {
			using var roi = new Mat(bgr, new OpenCvSharp.Rect(0, y0, w, h - y0));
			using var bot = new Mat();
			Cv2.Resize(roi, bot, new OpenCvSharp.Size(), 2.0, 2.0, InterpolationFlags.Cubic);
			hits = decodeonce(bot, 2f, 2f, 0, y0);
			if (hits.Count > 0) return hits;
		}

		return hits ?? new List<QrHit>();
	}

	static List<QrHit> decodeonce(Mat bgr, float scaleX, float scaleY, float offX, float offY) {
		var hits = new List<QrHit>();
		if (bgr == null || bgr.Empty()) return hits;
		try {
			// Clone 保证连续内存，避免 Step/ROI 问题
			using var cont = bgr.Clone();
			var reader = new BarcodeReader {
				Formats = BarcodeFormat.All,
				TryHarder = true,
				TryRotate = true,
				TryInvert = true,
				TryDownscale = true,
				MaxNumberOfSymbols = 16,
			};
			var fmt = cont.Channels() == 1 ? ImageFormat.Lum : ImageFormat.BGR;
			var iv = new ImageView(cont.Data, cont.Width, cont.Height, fmt, (int)cont.Step());
			var results = reader.From(iv);
			if (results == null || results.Length == 0) return hits;
			foreach (var b in results) {
				if (b == null || !b.IsValid || string.IsNullOrEmpty(b.Text)) continue;
				hits.Add(new QrHit {
					Type = b.Format.ToString().Replace(" ", ""),
					Text = b.Text.Trim(),
					Box = mapbox(b.Position, scaleX, scaleY, offX, offY),
				});
			}
		}
		catch {
			// 原生库加载失败等
		}
		// 过滤短假阳性 / 子串
		return cleanhits(hits);
	}

	static Point2f[] mapbox(Position pos, float sx, float sy, float ox, float oy) {
		sx = sx <= 1e-6f ? 1f : sx;
		sy = sy <= 1e-6f ? 1f : sy;
		Point2f m(PointI p) => new(p.X / sx + ox, p.Y / sy + oy);
		return new[] {
			m(pos.TopLeft),
			m(pos.TopRight),
			m(pos.BottomRight),
			m(pos.BottomLeft),
		};
	}

	static List<QrHit> cleanhits(List<QrHit> hits) {
		if (hits == null || hits.Count == 0) return hits ?? new List<QrHit>();
		// 丢弃被更长文本包含的短码
		var ordered = hits
			.Where(c => c != null && !string.IsNullOrEmpty(c.Text) && c.Text.Length >= 3)
			.OrderByDescending(c => c.Text.Length)
			.ToList();
		var keep = new List<QrHit>();
		foreach (var c in ordered) {
			var covered = false;
			foreach (var k in keep) {
				if (k.Text.Length > c.Text.Length
					&& k.Text.IndexOf(c.Text, StringComparison.Ordinal) >= 0) {
					covered = true;
					break;
				}
			}
			if (!covered) keep.Add(c);
		}
		// 有 CODE/QR 长码时，丢掉极短 UPC/EAN8 噪声
		var hasStrong = keep.Any(c =>
			c.Text.Length >= 8
			|| (c.Type?.IndexOf("QR", StringComparison.OrdinalIgnoreCase) >= 0)
			|| (c.Type?.IndexOf("Code", StringComparison.OrdinalIgnoreCase) >= 0));
		if (hasStrong) {
			keep = keep.Where(c => {
				var t = c.Type ?? "";
				if ((t.IndexOf("UPC", StringComparison.OrdinalIgnoreCase) >= 0
					|| t.IndexOf("EAN8", StringComparison.OrdinalIgnoreCase) >= 0)
					&& c.Text.Length <= 8)
					return false;
				return true;
			}).ToList();
		}
		return keep;
	}

	static void tryopencvqr(Mat bgr, List<QrHit> hits) {
		try {
			using var det = new QRCodeDetector();
			using var straight = new Mat();
			var text = det.DetectAndDecode(bgr, out OpenCvSharp.Point2f[] single, straight);
			if (string.IsNullOrEmpty(text)) return;
			Point2f[] sbox = null;
			if (single != null && single.Length >= 4) {
				sbox = new Point2f[4];
				for (int k = 0; k < 4; k++)
					sbox[k] = new Point2f(single[k].X, single[k].Y);
			}
			hits.Add(new QrHit {
				Type = "QRCode",
				Text = text,
				Box = sbox,
			});
		}
		catch { }
	}

	static void dedupe(List<QrHit> hits) {
		if (hits == null || hits.Count <= 1) return;
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var keep = new List<QrHit>(hits.Count);
		foreach (var c in hits) {
			if (c == null || string.IsNullOrEmpty(c.Text)) continue;
			var key = (c.Type ?? "") + "\n" + c.Text;
			if (!seen.Add(key)) continue;
			keep.Add(c);
		}
		hits.Clear();
		hits.AddRange(keep);
	}
}
