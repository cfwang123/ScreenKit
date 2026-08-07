using System.IO;
using System.Runtime.InteropServices;

namespace WpfOCR;

/// <summary>
/// GIF 区域录屏：先写无声临时 MP4，停录后由预览窗按调色板/缩放重编码为 GIF。
/// </summary>
sealed class GifScreenRecorder : IDisposable {
	System.Drawing.Rectangle region;
	readonly GifOptions gifOpt;
	readonly int fps;
	int grabW, grabH;
	readonly string videoTmp;
	readonly object gate = new();

	FfmpegMp4Writer ff;
	Thread thread;
	volatile bool stop;
	volatile bool paused;
	long startTick;
	long pauseAccum;
	long pauseStart;
	long frames;
	bool disposed;
	bool stopped;
	volatile bool finalizeDone;

	/// <summary>临时无声 MP4（供预览重编码）。</summary>
	public string VideoPath => videoTmp;
	public string TempPath => videoTmp;
	public int SrcWidth { get { lock (gate) return grabW > 0 ? grabW : region.Width; } }
	public int SrcHeight { get { lock (gate) return grabH > 0 ? grabH : region.Height; } }
	public int Fps => fps;
	public GifOptions Options => gifOpt;
	public System.Drawing.Rectangle Region {
		get { lock (gate) return region; }
	}

	/// <summary>更新抓屏区域（可移动/缩放）。编码尺寸在 Start 后固定。</summary>
	public void SetRegion(System.Drawing.Rectangle r) {
		if (r.Width % 2 != 0) r.Width--;
		if (r.Height % 2 != 0) r.Height--;
		if (r.Width < 16 || r.Height < 16) return;
		lock (gate) {
			region = r;
			if (grabW <= 0) {
				// 尚未 Start：同步输出标注尺寸
			}
		}
	}
	public bool IsPaused => paused;
	public bool IsRunning => thread != null && thread.IsAlive;
	public bool IsFinalizeDone => finalizeDone;
	public string Backend { get; private set; }
	public Action<string> Progress;
	public bool HasAudio => false;
	public string AudioError => null;

	public GifScreenRecorder(System.Drawing.Rectangle region, GifOptions options = null) {
		var r = region;
		// H.264 需要偶数边
		if (r.Width % 2 != 0) r.Width--;
		if (r.Height % 2 != 0) r.Height--;
		if (r.Width < 16 || r.Height < 16)
			throw new ArgumentException("录制区域过小");
		this.region = r;
		gifOpt = (options ?? new GifOptions()).Clone();
		gifOpt.Clamp();
		fps = GifOptions.CaptureFps;
		TmpStore.CleanupExpired();
		videoTmp = TmpStore.NewPath("gif", ".mp4");
	}

	public void Start() {
		if (IsRunning) return;
		stop = false;
		paused = false;
		frames = 0;
		pauseAccum = 0;
		finalizeDone = false;
		RecordLog.Begin("GifScreenRecorder");
		System.Drawing.Rectangle r0;
		lock (gate) r0 = region;
		grabW = r0.Width;
		grabH = r0.Height;
		RecordLog.Step("start",
			$"region={r0.Width}x{r0.Height}@{r0.Left},{r0.Top} fps={fps}");
		RecordLog.Step("paths", $"video={videoTmp}");

		if (!FfmpegLoader.TryInit(out var ffErr)) {
			RecordLog.Step("ffmpeg_dll", "fail: " + (ffErr ?? "unknown"));
			if (!FeaturePrompt.EnsureFfmpeg() || !FfmpegLoader.TryInit(out ffErr))
				throw new InvalidOperationException(
					"无法加载 FFmpeg。请通过「安装功能」安装 FFmpeg，"
					+ "或将 FFmpeg 4.4 shared 库放到程序目录 ffmpeg64/。\n" + (ffErr ?? ""));
		}
		RecordLog.Step("ffmpeg_dll", "ok root=" + (FfmpegLoader.DllRoot ?? ""));
		try {
			var ro = new RecordOptions {
				Codec = "x264",
				Fps = fps,
				Crf = 28,
				AudioEnabled = false,
				MaxSizeEnabled = false,
			};
			ro.Clamp();
			ff = new FfmpegMp4Writer(videoTmp, grabW, grabH, ro);
			Backend = $"GIF源 {ff.OutWidth}x{ff.OutHeight}@{fps}fps 无声（预览可选输出帧率）";
			RecordLog.Step("video_writer", Backend);
		}
		catch (Exception ex) {
			CaptureLog.Ex("FfmpegMp4Writer.gif", ex);
			RecordLog.Ex("FfmpegMp4Writer.gif", ex);
			ff = null;
			throw new InvalidOperationException(
				"无法创建临时视频编码器: " + ex.Message
				+ "\n请检查 ffmpeg64 是否完整。", ex);
		}

		startTick = Compat.TickCount64;
		thread = new Thread(loop) { IsBackground = true, Name = "GifScreenRecorder" };
		thread.Start();
		RecordLog.Step("thread", "GifScreenRecorder loop started");
	}

	public void Pause() {
		if (paused) return;
		paused = true;
		pauseStart = Compat.TickCount64;
		RecordLog.Step("pause", $"frames={frames} elapsed={Elapsed}");
	}

	public void Resume() {
		if (!paused) return;
		paused = false;
		pauseAccum += Math.Max(0, Compat.TickCount64 - pauseStart);
		RecordLog.Step("resume", $"pauseAccumMs={pauseAccum}");
	}

	public void Stop() {
		if (stopped) return;
		stopped = true;
		RecordLog.Step("stop_begin", $"frames={frames} elapsed={Elapsed} " + RecordLog.FileInfo(videoTmp));
		report("正在停止采集…");
		stop = true;
		try { thread?.Join(15000); } catch (Exception ex) { RecordLog.Ex("thread.Join", ex); }
		thread = null;
		RecordLog.Step("video_loop_done", $"frames={frames} " + RecordLog.FileInfo(videoTmp));

		report("正在写入视频…");
		lock (gate) {
			try { ff?.Finish(); } catch (Exception ex) { RecordLog.Ex("ff.Finish", ex); }
			try { ff?.Dispose(); } catch { }
			ff = null;
		}
		finalizeDone = true;
		report("完成");
		RecordLog.Step("stop_end", RecordLog.FileInfo(videoTmp));
		RecordLog.End("ok");
	}

	public void WaitFinalize() { }

	public void DiscardTemps() {
		tryDelete(videoTmp);
	}

	static void tryDelete(string path) {
		try {
			if (!string.IsNullOrEmpty(path) && File.Exists(path))
				File.Delete(path);
		}
		catch { }
	}

	void report(string msg) {
		try { Progress?.Invoke(msg); } catch { }
	}

	public TimeSpan Elapsed {
		get {
			if (startTick == 0) return TimeSpan.Zero;
			var now = Compat.TickCount64;
			var pausedPart = pauseAccum;
			if (paused) pausedPart += Math.Max(0, now - pauseStart);
			var wall = (now - startTick) & 0xFFFFFFFFL;
			var ms = Math.Max(0, (long)wall - pausedPart);
			return TimeSpan.FromMilliseconds(ms);
		}
	}

	public long FileBytes {
		get {
			try {
				if (File.Exists(videoTmp))
					return new FileInfo(videoTmp).Length;
			}
			catch { }
			return 0;
		}
	}

	void loop() {
		var interval = 1000.0 / Math.Max(1, fps);
		var next = (double)Compat.TickCount64;
		var nextBeat = Compat.TickCount64 + 30_000;
		var frameEx = 0;
		long lastPts = -1;
		while (!stop) {
			if (paused) {
				Thread.Sleep(40);
				continue;
			}
			var now = Compat.TickCount64;
			var sleep = (int)Math.Round(next - now);
			if (sleep > 1) {
				Thread.Sleep(Math.Min(sleep, 50));
				continue;
			}
			if (now - next > interval * 2) {
				var missed = (long)((now - next) / interval);
				if (missed > 0) next += missed * interval;
			}

			var elapsedMs = (long)Elapsed.TotalMilliseconds;
			var pts = elapsedMs * fps / 1000;
			if (pts <= lastPts) pts = lastPts + 1;
			lastPts = pts;

			try { grabandwrite(pts); }
			catch (Exception ex) {
				frameEx++;
				CaptureLog.Ex("GifScreenRecorder.frame", ex);
				if (frameEx <= 3 || frameEx % 100 == 0)
					RecordLog.Ex($"frame#{frames}", ex);
			}
			now = Compat.TickCount64;
			if (now - nextBeat >= 0) {
				nextBeat = now + 30_000;
				long vsz = 0;
				try { if (File.Exists(videoTmp)) vsz = new FileInfo(videoTmp).Length; } catch { }
				RecordLog.Step("heartbeat",
					$"elapsed={Elapsed:hh\\:mm\\:ss} frames={frames} lastPts={lastPts} " +
					$"frameEx={frameEx} videoBytes={vsz}");
			}
			next += interval;
		}
		RecordLog.Step("loop_exit",
			$"frames={frames} frameEx={frameEx} lastPts={lastPts} elapsed={Elapsed}");
	}

	void grabandwrite(long pts) {
		System.Drawing.Rectangle r;
		lock (gate) r = region;
		var w = r.Width;
		var h = r.Height;
		if (w < 1 || h < 1) return;
		var tw = grabW > 0 ? grabW : w;
		var th = grabH > 0 ? grabH : h;

		using var src = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		using (var g = System.Drawing.Graphics.FromImage(src)) {
			g.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(w, h),
				System.Drawing.CopyPixelOperation.SourceCopy);
		}

		System.Drawing.Bitmap bmp = src;
		System.Drawing.Bitmap scaled = null;
		try {
			if (w != tw || h != th) {
				scaled = new System.Drawing.Bitmap(tw, th, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
				using (var g = System.Drawing.Graphics.FromImage(scaled)) {
					g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
					g.DrawImage(src, 0, 0, tw, th);
				}
				bmp = scaled;
			}
			var rect = new System.Drawing.Rectangle(0, 0, tw, th);
			var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
				System.Drawing.Imaging.PixelFormat.Format32bppArgb);
			try {
				lock (gate) {
					if (ff == null) return;
					var stride = data.Stride;
					var bytes = new byte[Math.Abs(stride) * th];
					Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
					for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
					ff.WriteBgra(bytes, Math.Abs(stride), pts);
					frames++;
				}
			}
			finally {
				bmp.UnlockBits(data);
			}
		}
		finally {
			scaled?.Dispose();
		}
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		try { Stop(); } catch { }
	}
}
