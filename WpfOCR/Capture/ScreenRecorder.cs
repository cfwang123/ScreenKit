using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WpfOCR;

/// <summary>
/// 区域录屏：GDI 抓帧 + FFmpeg（ffmpeg64 / FFmpeg.AutoGen），边录边写文件。
/// 不再使用 OpenCV VideoWriter / opencv_videoio_ffmpeg。
/// </summary>
sealed class ScreenRecorder : IDisposable {
	readonly System.Drawing.Rectangle region;
	readonly RecordOptions recOpt;
	readonly int fps;
	readonly int outW, outH;
	readonly RecordAudioMode audioMode;
	readonly string videoTmp;
	readonly string wavTmp;
	readonly object gate = new();

	FfmpegMp4Writer ff;
	AudioCapture audio;
	Thread thread;
	volatile bool stop;
	volatile bool paused;
	long startTick;
	long pauseAccum;
	long pauseStart;
	long frames;
	bool disposed;
	bool stopped;
	string finalPath;
	Task finalizeTask;
	volatile bool finalizeDone;

	/// <summary>最终可交付的 mp4（含音频合成结果）。合成未完成时可能仍是纯视频临时文件。</summary>
	public string TempPath => finalPath ?? videoTmp;
	public System.Drawing.Rectangle Region => region;
	public bool IsPaused => paused;
	public bool IsRunning => thread != null && thread.IsAlive;
	/// <summary>采集已停且视频索引写完；音轨合成可能仍在后台。</summary>
	public bool IsCaptureStopped => stopped;
	/// <summary>音轨收尾+合成是否已结束（无音频时 Stop 后即为 true）。</summary>
	public bool IsFinalizeDone => finalizeDone;
	public string Backend { get; private set; }
	public RecordAudioMode AudioMode => audioMode;
	/// <summary>收尾进度文案（后台线程回调，UI 需 Dispatcher 切换）。</summary>
	public Action<string> Progress;

	public ScreenRecorder(System.Drawing.Rectangle region, RecordAudioMode audio = RecordAudioMode.Off,
		RecordOptions options = null) {
		var r = region;
		if (r.Width % 2 != 0) r.Width--;
		if (r.Height % 2 != 0) r.Height--;
		if (r.Width < 16 || r.Height < 16)
			throw new ArgumentException("录制区域过小");
		this.region = r;
		recOpt = (options ?? new RecordOptions()).Clone();
		recOpt.Clamp();
		fps = recOpt.Fps;
		recOpt.FitSize(r.Width, r.Height, out outW, out outH);
		audioMode = audio;
		TmpStore.CleanupExpired();
		videoTmp = TmpStore.NewPath("rec", ".mp4");
		wavTmp = Path.ChangeExtension(videoTmp, ".wav");
		finalPath = videoTmp;
	}

	/// <summary>兼容旧调用。</summary>
	public ScreenRecorder(System.Drawing.Rectangle region, int fps, RecordAudioMode audio = RecordAudioMode.Off)
		: this(region, audio, new RecordOptions { Fps = fps }) { }

	public void Start() {
		if (IsRunning) return;
		stop = false;
		paused = false;
		frames = 0;
		pauseAccum = 0;
		RecordLog.Begin("ScreenRecorder");
		RecordLog.Step("start",
			$"region={region.Width}x{region.Height}@{region.Left},{region.Top} fps={fps} " +
			$"codec={recOpt.Codec} crf={recOpt.Crf} audio={audioMode} hz={recOpt.AudioHz} " +
			$"mono={recOpt.AudioMono} kbps={recOpt.AudioKbps} out={outW}x{outH}");
		RecordLog.Step("paths", $"video={videoTmp} wav={wavTmp}");

		// 仅 FFmpeg.AutoGen（程序目录 ffmpeg64）
		if (!FfmpegLoader.TryInit(out var ffErr)) {
			RecordLog.Step("ffmpeg_dll", "fail: " + (ffErr ?? "unknown"));
			// 弹窗提示安装
			if (!FeaturePrompt.EnsureFfmpeg() || !FfmpegLoader.TryInit(out ffErr))
				throw new InvalidOperationException(
					"无法加载 FFmpeg。请通过「安装功能」安装 FFmpeg，"
					+ "或将 FFmpeg 4.4 shared 库放到程序目录 ffmpeg64/。\n" + (ffErr ?? ""));
		}
		RecordLog.Step("ffmpeg_dll", "ok root=" + (FfmpegLoader.DllRoot ?? ""));
		try {
			ff = new FfmpegMp4Writer(videoTmp, region.Width, region.Height, recOpt);
			Backend = $"FFmpeg/{ff.CodecName} {ff.OutWidth}x{ff.OutHeight}@{fps} CRF{recOpt.Crf}";
			RecordLog.Step("video_writer", Backend);
		}
		catch (Exception ex) {
			CaptureLog.Ex("FfmpegMp4Writer", ex);
			RecordLog.Ex("FfmpegMp4Writer", ex);
			ff = null;
			if (recOpt.IsHevc)
				throw new InvalidOperationException(
					"x265 不可用: " + ex.Message + "\n请改用 x264，或换含 libx265 的 ffmpeg64。", ex);
			throw new InvalidOperationException(
				"无法创建 FFmpeg 视频编码器: " + ex.Message
				+ "\n请检查 ffmpeg64 是否完整（avcodec 等）。", ex);
		}

		if (audioMode != RecordAudioMode.Off) {
			try {
				audio = new AudioCapture(wavTmp, audioMode, recOpt.AudioHz, recOpt.AudioMono);
				// 单路音源：停录时不二次规范化，交给合成阶段一次完成（长录屏可省数十秒）
				audio.SkipNormalize = audioMode is RecordAudioMode.Speakers or RecordAudioMode.Mic;
				audio.Start();
				var ch = recOpt.AudioMono ? "mono" : "stereo";
				Backend += $"+{audioMode}@{recOpt.AudioHz}Hz/{ch}";
				RecordLog.Step("audio_start", Backend + " " + RecordLog.FileInfo(wavTmp));
			}
			catch (Exception ex) {
				CaptureLog.Ex("AudioCapture", ex);
				RecordLog.Ex("AudioCapture.Start", ex);
				throw new InvalidOperationException("无法开始录音: " + ex.Message, ex);
			}
		}
		else {
			RecordLog.Step("audio_start", "off");
		}

		startTick = Compat.TickCount64;
		thread = new Thread(loop) { IsBackground = true, Name = "ScreenRecorder" };
		thread.Start();
		RecordLog.Step("thread", "ScreenRecorder loop started");
	}

	public void Pause() {
		if (paused) return;
		paused = true;
		pauseStart = Compat.TickCount64;
		RecordLog.Step("pause", $"frames={frames} elapsed={Elapsed}");
		try { audio?.Pause(); } catch (Exception ex) { RecordLog.Ex("audio.Pause", ex); }
	}

	public void Resume() {
		if (!paused) return;
		paused = false;
		pauseAccum += Math.Max(0, Compat.TickCount64 - pauseStart);
		RecordLog.Step("resume", $"pauseAccumMs={pauseAccum}");
		try { audio?.Resume(); } catch (Exception ex) { RecordLog.Ex("audio.Resume", ex); }
	}

	/// <summary>
	/// 停止采集并写完视频索引后立即返回；音频收尾与音视频合成在后台继续。
	/// 保存前请 <see cref="WaitFinalize"/> 再取 <see cref="TempPath"/>。
	/// </summary>
	public void Stop() {
		if (stopped) return;
		stopped = true;
		RecordLog.Step("stop_begin", $"frames={frames} elapsed={Elapsed} " + RecordLog.FileInfo(videoTmp));
		report("正在停止采集…");
		stop = true;
		try { thread?.Join(15000); } catch (Exception ex) { RecordLog.Ex("thread.Join", ex); }
		thread = null;
		RecordLog.Step("video_loop_done", $"frames={frames} " + RecordLog.FileInfo(videoTmp));

		// 视频 trailer 与音频收尾/合成并行：先保证纯视频可播，立刻可弹保存框
		var cap = audio;
		audio = null;
		finalPath = videoTmp;
		HasAudio = false;
		finalizeDone = false;

		report("正在写入视频索引…");
		lock (gate) {
			try { ff?.Finish(); } catch (Exception ex) { RecordLog.Ex("ff.Finish", ex); }
			try { ff?.Dispose(); } catch { }
			ff = null;

		}
		RecordLog.Step("video_finalize", RecordLog.FileInfo(videoTmp));

		if (audioMode == RecordAudioMode.Off || cap == null) {
			finalizeDone = true;
			finalizeTask = Task.CompletedTask;
			report("完成");
			RecordLog.Step("stop_end",
				$"HasAudio=false AudioError=- final={RecordLog.FileInfo(finalPath)} (no_audio_mode)");
			RecordLog.End("ok");
			return;
		}

		// 后台：关 wav → 合成 → 更新 finalPath
		report("正在收尾音频…");
		finalizeTask = Task.Run(() => {
			try {
				try {
					cap.Stop();
					RecordLog.Step("audio_stop",
						$"bytesLoop={cap.BytesLoop} bytesMic={cap.BytesMic} firstDataMs={cap.FirstDataMs} " +
						RecordLog.FileInfo(wavTmp));
				}
				catch (Exception ex) {
					RecordLog.Ex("audio.Stop", ex);
					AudioError = ex.Message;
				}
				finally {
					try { cap.Dispose(); } catch { }
				}

				var wavOk = File.Exists(wavTmp) && new FileInfo(wavTmp).Length > 100;
				var wavSz = wavOk ? new FileInfo(wavTmp).Length : 0;
				RecordLog.Step("merge_check",
					$"mode={audioMode} wavOk={wavOk} wavSize={wavSz} " + RecordLog.FileInfo(wavTmp));
				CaptureLog.Info($"Stop audio mode={audioMode} wavOk={wavOk} size={wavSz}");
				if (!wavOk) {
					if (string.IsNullOrEmpty(AudioError))
						AudioError = "未采集到音频数据（请确认系统有声音输出/麦克风权限）";
					RecordLog.Step("merge_skip", AudioError);
					return;
				}

				try {
					var merged = Path.Combine(
						Path.GetDirectoryName(videoTmp) ?? TmpStore.Root,
						Path.GetFileNameWithoutExtension(videoTmp) + "_av.mp4");
					report("正在合成音轨…");
					RecordLog.Step("merge_begin",
						$"v={RecordLog.FileInfo(videoTmp)} a={RecordLog.FileInfo(wavTmp)} out={merged} " +
						$"kbps={recOpt.AudioKbps} mono={recOpt.AudioMono} hz={recOpt.AudioHz}");
					FfmpegRemux.MergeVideoAudio(videoTmp, wavTmp, merged,
						recOpt.AudioKbps, recOpt.AudioMono, out var mergeErr, recOpt.AudioHz);
					var hasStream = File.Exists(merged) && FfmpegRemux.HasAudioStream(merged);
					RecordLog.Step("merge_result",
						$"hasAudioStream={hasStream} err={mergeErr ?? "-"} " + RecordLog.FileInfo(merged));
					if (hasStream) {
						try { File.Delete(videoTmp); } catch { }
						finalPath = merged;
						HasAudio = true;
						CaptureLog.Info($"Merge done path={merged}");
						RecordLog.Step("merge_ok", RecordLog.FileInfo(finalPath));
					}
					else {
						HasAudio = false;
						AudioError = mergeErr ?? "合成后无音轨";
						CaptureLog.Info($"Merge no audio: {AudioError}");
						RecordLog.Step("merge_fail", AudioError);
					}
				}
				catch (Exception ex) {
					CaptureLog.Ex("Merge AV", ex);
					RecordLog.Ex("Merge AV", ex);
					AudioError = ex.Message;
				}
				finally {
					if (HasAudio) {
						try { File.Delete(wavTmp); } catch { }
						RecordLog.Step("wav_cleanup", "deleted after success");
					}
					else {
						RecordLog.Step("wav_keep", "kept for diagnose: " + RecordLog.FileInfo(wavTmp));
					}
				}
			}
			finally {
				finalizeDone = true;
				report("完成");
				RecordLog.Step("stop_end",
					$"HasAudio={HasAudio} AudioError={AudioError ?? "-"} final={RecordLog.FileInfo(finalPath)}");
				RecordLog.End(HasAudio ? "ok" : "no_audio");
			}
		});
		RecordLog.Step("stop_return", "capture done; audio+merge in background");
	}

	/// <summary>等待后台音频收尾与合成结束（可在选完保存路径后调用）。</summary>
	public void WaitFinalize(int timeoutMs = 600_000) {
		var t = finalizeTask;
		if (t == null || t.IsCompleted) return;
		try {
			if (timeoutMs < 0) t.Wait();
			else t.Wait(timeoutMs);
		}
		catch (Exception ex) { RecordLog.Ex("WaitFinalize", ex); }
	}

	/// <summary>删除临时视频/音频/合成产物（未保存时调用；会先 WaitFinalize）。</summary>
	public void DiscardTemps() {
		try { WaitFinalize(); } catch { }
		tryDelete(finalPath);
		if (!string.Equals(finalPath, videoTmp, StringComparison.OrdinalIgnoreCase))
			tryDelete(videoTmp);
		tryDelete(wavTmp);
		var mergedGuess = Path.Combine(
			Path.GetDirectoryName(videoTmp) ?? TmpStore.Root,
			Path.GetFileNameWithoutExtension(videoTmp) + "_av.mp4");
		if (!string.Equals(finalPath, mergedGuess, StringComparison.OrdinalIgnoreCase))
			tryDelete(mergedGuess);
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

	/// <summary>截取当前录制区域一帧（不中断录屏），返回冻结的 BitmapSource。</summary>
	public BitmapSource CaptureStill() => CaptureRegion(region);

	/// <summary>
	/// 静态：截取指定区域。
	/// 注意：不可写 <c>SourceCopy|CaptureBlt</c> 传给 CopyFromScreen——
	/// 该组合不是已定义枚举成员，会抛 InvalidEnumArgumentException（录屏截图失败的根因）。
	/// 与录屏循环一致用 SourceCopy；需要 CAPTUREBLT 时走 BitBlt。
	/// </summary>
	public static BitmapSource CaptureRegion(System.Drawing.Rectangle r) {
		var w = r.Width;
		var h = r.Height;
		if (w < 1 || h < 1) return null;
		using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		using (var g = System.Drawing.Graphics.FromImage(bmp)) {
			// 与 grabandwrite 相同：仅 SourceCopy（系统会校验枚举 IsDefined）
			g.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(w, h),
				System.Drawing.CopyPixelOperation.SourceCopy);
		}
		return bitmapToSource(bmp);
	}

	static BitmapSource bitmapToSource(System.Drawing.Bitmap bmp) {
		var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
		var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
			System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		try {
			var stride = Math.Abs(data.Stride);
			var nbytes = stride * bmp.Height;
			var pixels = new byte[nbytes];
			Marshal.Copy(data.Scan0, pixels, 0, nbytes);
			for (var i = 3; i < pixels.Length; i += 4)
				pixels[i] = 255;
			var src = BitmapSource.Create(bmp.Width, bmp.Height, 96, 96,
				PixelFormats.Bgra32, null, pixels, stride);
			src.Freeze();
			return src;
		}
		finally {
			bmp.UnlockBits(data);
		}
	}

	/// <summary>最终文件是否含音轨（尽力判断）。</summary>
	public bool HasAudio { get; private set; }
	/// <summary>音频相关错误说明。</summary>
	public string AudioError { get; private set; }

	/// <summary>有效录制时长（排除暂停）。</summary>
	public TimeSpan Elapsed {
		get {
			if (startTick == 0) return TimeSpan.Zero;
			var now = Compat.TickCount64;
			var pausedPart = pauseAccum;
			if (paused) pausedPart += Math.Max(0, now - pauseStart);
			// TickCount64 为无符号 32 位毫秒；差值按 32 位环回
			var wall = (now - startTick) & 0xFFFFFFFFL;
			var ms = Math.Max(0, (long)wall - pausedPart);
			return TimeSpan.FromMilliseconds(ms);
		}
	}

	public long FileBytes {
		get {
			try {
				var p = TempPath;
				if (File.Exists(p))
					return new FileInfo(p).Length;
				if (File.Exists(videoTmp))
					return new FileInfo(videoTmp).Length;
			}
			catch { }
			return 0;
		}
	}

	void loop() {
		// 用 double 累加间隔，避免 (int)(1000/fps) 截断导致每帧少 0.3~0.7ms、长录屏音画漂移
		var interval = 1000.0 / Math.Max(1, fps);
		var next = (double)Compat.TickCount64;
		var nextBeat = Compat.TickCount64 + 30_000; // 每 30s 心跳
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
			// 追不上则丢弃中间时刻（不连拍压缩时间轴）；PTS 仍按墙钟，与音频对齐
			if (now - next > interval * 2) {
				var missed = (long)((now - next) / interval);
				if (missed > 0) next += missed * interval;
			}

			// 墙钟 PTS（1/fps）：视频时长 ≈ 有效录制时长，而非「成功编码帧数/fps」
			var elapsedMs = (long)Elapsed.TotalMilliseconds;
			var pts = elapsedMs * fps / 1000;
			if (pts <= lastPts) pts = lastPts + 1;
			lastPts = pts;

			try {
				grabandwrite(pts);
			}
			catch (Exception ex) {
				frameEx++;
				CaptureLog.Ex("ScreenRecorder.frame", ex);
				if (frameEx <= 3 || frameEx % 100 == 0)
					RecordLog.Ex($"frame#{frames}", ex);
			}
			// 长时间录制进度（便于对照音画）
			now = Compat.TickCount64;
			if (now - nextBeat >= 0) {
				nextBeat = now + 30_000;
				long vsz = 0, asz = 0;
				try { if (File.Exists(videoTmp)) vsz = new FileInfo(videoTmp).Length; } catch { }
				try { if (File.Exists(wavTmp)) asz = new FileInfo(wavTmp).Length; } catch { }
				var expPts = Math.Max(0, elapsedMs) * fps / 1000;
				RecordLog.Step("heartbeat",
					$"elapsed={Elapsed:hh\\:mm\\:ss} frames={frames} expPts={expPts} lastPts={lastPts} " +
					$"frameEx={frameEx} videoBytes={vsz} wavBytes={asz} " +
					$"audioLoop={audio?.BytesLoop ?? 0} audioMic={audio?.BytesMic ?? 0} " +
					$"firstAudioMs={audio?.FirstDataMs ?? -1} pad={audio?.PadBytesTotal ?? 0}");
			}
			next += interval;
		}
		RecordLog.Step("loop_exit",
			$"frames={frames} frameEx={frameEx} lastPts={lastPts} elapsed={Elapsed}");
	}

	void grabandwrite(long pts) {
		var w = region.Width;
		var h = region.Height;
		var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		try {
			using (var g = System.Drawing.Graphics.FromImage(bmp)) {
				g.CopyFromScreen(region.Left, region.Top, 0, 0, new System.Drawing.Size(w, h),
					System.Drawing.CopyPixelOperation.SourceCopy);
			}
			var rect = new System.Drawing.Rectangle(0, 0, w, h);
			var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
				System.Drawing.Imaging.PixelFormat.Format32bppArgb);
			try {
				lock (gate) {
					if (ff == null) return;
					var stride = data.Stride;
					var bytes = new byte[Math.Abs(stride) * h];
					Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
					// 强制 alpha
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
			bmp.Dispose();
		}
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		try { Stop(); } catch { }
		try { WaitFinalize(); } catch { }
	}
}
