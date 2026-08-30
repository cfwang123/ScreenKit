using System.IO;
using System.Windows.Media.Imaging;

namespace ScreenKit;

/// <summary>
/// 录屏中截图：抓取指定屏幕区域 → 存 PNG → 写入剪贴板。
/// 与 HUD 解耦，便于 CLI 自测与热键复用。
/// </summary>
static class RecordSnap {
	public sealed class Result {
		public bool Ok;
		public string Path;
		public int Width;
		public int Height;
		public double NonBlack;
		public bool ClipboardOk;
		public string Error;
	}

	/// <summary>
	/// 截取物理像素矩形，保存到 outPath（可空=自动 tmp/snap_*.png），并尽力写入剪贴板。
	/// </summary>
	public static Result Capture(System.Drawing.Rectangle region, string outPath = null) {
		var r = new Result();
		try {
			if (region.Width < 1 || region.Height < 1) {
				r.Error = "区域无效";
				return r;
			}
			var bmp = ScreenRecorder.CaptureRegion(region);
			if (bmp == null) {
				r.Error = "CaptureRegion 返回 null";
				return r;
			}
			r.Width = bmp.PixelWidth;
			r.Height = bmp.PixelHeight;
			r.NonBlack = sampleNonBlack(bmp);

			if (string.IsNullOrWhiteSpace(outPath))
				outPath = TmpStore.NewPath("snap", ".png");
			else {
				outPath = Path.GetFullPath(outPath);
				var dir = Path.GetDirectoryName(outPath);
				if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
			}
			ImageUtil.Savefile(bmp, outPath);
			r.Path = outPath;
			if (!File.Exists(outPath) || new FileInfo(outPath).Length < 32) {
				r.Error = "PNG 未写入或过小: " + outPath;
				return r;
			}

			try {
				ImageUtil.Toclipboardimageandfile(bmp);
				r.ClipboardOk = true;
			}
			catch (Exception ex) {
				// 文件已成功；剪贴板失败不判整次失败
				r.ClipboardOk = false;
				r.Error = "剪贴板: " + ex.Message;
				try {
					// 至少保证文件在剪贴板（资源管理器可粘贴）
					var files = new System.Collections.Specialized.StringCollection { outPath };
					var data = new System.Windows.DataObject();
					data.SetFileDropList(files);
					System.Windows.Clipboard.SetDataObject(data, true);
					r.ClipboardOk = true;
					r.Error = "剪贴板仅文件: " + ex.Message;
				}
				catch (Exception ex2) {
					r.Error = "剪贴板失败: " + ex.Message + " / " + ex2.Message;
				}
			}

			r.Ok = true;
			return r;
		}
		catch (Exception ex) {
			r.Error = ex.ToString();
			return r;
		}
	}

	/// <summary>边录边截：开录 → 等 frames → 截图 → 停录（丢弃视频）。</summary>
	public static Result CaptureWhileRecording(System.Drawing.Rectangle region, string outPath = null,
		int waitMs = 800, Action<string> log = null) {
		void L(string s) { try { log?.Invoke(s); } catch { } }
		ScreenRecorder rec = null;
		string videoPath = null;
		try {
			L($"region={region.X},{region.Y} {region.Width}x{region.Height}");
			var opt = new RecordOptions { Fps = 10, Crf = 40, AudioEnabled = false, Codec = "x264" };
			opt.Clamp();
			rec = new ScreenRecorder(region, RecordAudioMode.Off, opt);
			videoPath = rec.TempPath;
			L("Start recorder…");
			rec.Start();
			L("backend=" + (rec.Backend ?? ""));
			var t0 = Environment.TickCount;
			while (Environment.TickCount - t0 < Math.Max(200, waitMs))
				Thread.Sleep(50);
			L($"elapsed={rec.Elapsed} CaptureStill…");
			// 与 HUD 相同路径：录制中 CaptureStill
			var still = rec.CaptureStill();
			if (still == null) {
				L("CaptureStill null, fallback CaptureRegion");
				return Capture(region, outPath);
			}
			if (string.IsNullOrWhiteSpace(outPath))
				outPath = TmpStore.NewPath("snap_rec", ".png");
			else {
				outPath = Path.GetFullPath(outPath);
				var dir = Path.GetDirectoryName(outPath);
				if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
			}
			ImageUtil.Savefile(still, outPath);
			var r = new Result {
				Ok = File.Exists(outPath),
				Path = outPath,
				Width = still.PixelWidth,
				Height = still.PixelHeight,
				NonBlack = sampleNonBlack(still),
			};
			try {
				ImageUtil.Toclipboardimageandfile(still);
				r.ClipboardOk = true;
			}
			catch (Exception ex) {
				r.ClipboardOk = false;
				r.Error = "剪贴板: " + ex.Message;
			}
			L($"saved {r.Path} {r.Width}x{r.Height} nonBlack={r.NonBlack:P1} clip={r.ClipboardOk}");
			return r;
		}
		catch (Exception ex) {
			L("EX: " + ex);
			return new Result { Error = ex.ToString() };
		}
		finally {
			try { rec?.Dispose(); } catch { }
			try {
				if (!string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
					File.Delete(videoPath);
			}
			catch { }
		}
	}

	static double sampleNonBlack(BitmapSource src) {
		if (src == null) return 0;
		var w = src.PixelWidth;
		var h = src.PixelHeight;
		if (w < 1 || h < 1) return 0;
		var stride = w * 4;
		var px = new byte[stride * h];
		var bgra = src;
		if (src.Format != System.Windows.Media.PixelFormats.Bgra32)
			bgra = new System.Windows.Media.Imaging.FormatConvertedBitmap(
				src, System.Windows.Media.PixelFormats.Bgra32, null, 0);
		bgra.CopyPixels(px, stride, 0);
		long nb = 0, n = 0;
		var step = Math.Max(4, (w * h / 2000) * 4);
		if (step % 4 != 0) step = Math.Max(4, (step / 4) * 4);
		for (var i = 0; i + 3 < px.Length; i += step) {
			n++;
			if (px[i] > 12 || px[i + 1] > 12 || px[i + 2] > 12) nb++;
		}
		return n > 0 ? nb / (double)n : 0;
	}
}
