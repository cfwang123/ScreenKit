using System.Diagnostics;
using System.IO;
using System.Text;
using FFmpeg.AutoGen;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace WpfOCR;

/// <summary>
/// 长录音画同步专项：播放 0.1s 有声 / 0.1s 静音循环 → 录 N 秒 → 分析
/// 视频时长、音轨时长、有声脉冲周期是否相对墙钟漂移。
/// </summary>
static unsafe class RecordAvSyncTest {
	/// <summary>有声段时长（秒）。</summary>
	const double OnSec = 0.1;
	/// <summary>静音段时长（秒）。</summary>
	const double OffSec = 0.1;
	/// <summary>一个完整周期（有声+静音）。</summary>
	const double PeriodSec = OnSec + OffSec;
	/// <summary>测试音频率 Hz。</summary>
	const double ToneHz = 880;
	/// <summary>判定有声的 RMS 阈值（相对 full-scale，约 -30dB）。</summary>
	const float RmsOn = 0.02f;
	/// <summary>分析窗 10ms。</summary>
	const double WinSec = 0.01;

	public static int Run(string outDir, string regionArg, int seconds, Action<string> log) {
		if (log == null) log = _ => { };
		void L(string s) {
			try { log(s); } catch { }
		}

		if (seconds < 2) seconds = 2;
		if (seconds > 600) seconds = 600;
		if (string.IsNullOrWhiteSpace(outDir))
			outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log", "record_avsync");
		outDir = Path.GetFullPath(outDir);
		Directory.CreateDirectory(outDir);

		AppConfig.applylogswitch(true);
		CaptureLog.SessionStart("CLI --test-record-avsync");
		RecordLog.Begin("CLI-record-avsync");

		L("=== 录屏音画同步测试 --test-record-avsync ===");
		L($"pattern=有声{OnSec * 1000:0}ms / 静音{OffSec * 1000:0}ms 循环  tone={ToneHz}Hz");
		L($"recordSec={seconds}  out={outDir}");
		L(ScreenDpi.BuildReport());

		var region = parseregion(regionArg);
		L($"region={region.X},{region.Y} {region.Width}x{region.Height}");

		var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
		var reportPath = Path.Combine(outDir, $"avsync_{stamp}.txt");
		var mp4Copy = Path.Combine(outDir, $"avsync_{stamp}.mp4");
		var wavCopy = Path.Combine(outDir, $"avsync_{stamp}.wav");
		var pcmCopy = Path.Combine(outDir, $"avsync_{stamp}_from_mp4.wav");

		ToneBeepPlayer tone = null;
		ScreenRecorder rec = null;
		var bad = 0;
		var sb = new StringBuilder();
		void both(string s) {
			L(s);
			sb.AppendLine(s);
		}

		try {
			// 1) 先起测试音（扬声器环回才能录到）
			tone = new ToneBeepPlayer(ToneHz, OnSec, OffSec);
			tone.Start();
			L("tone player started (loopback source)");
			Thread.Sleep(200); // 给 WASAPI 一点启动时间

			var opt = new RecordOptions {
				Fps = 24,
				Crf = 32,
				Codec = "x264",
				AudioEnabled = true,
				AudioSource = "Speakers",
				AudioHz = 22050,
				AudioMono = true,
				AudioKbps = 64,
			};
			opt.Clamp();
			rec = new ScreenRecorder(region, RecordAudioMode.Speakers, opt);
			var videoTmp = rec.TempPath;
			var wavTmp = Path.ChangeExtension(videoTmp, ".wav");
			both($"backend start… tmpV={videoTmp}");
			rec.Start();
			both($"recording: {rec.Backend}");

			var t0 = Compat.TickCount64;
			var nextBeat = t0 + 2000;
			while (true) {
				var elapsed = (Compat.TickCount64 - t0) & 0xFFFFFFFFL;
				if (elapsed >= seconds * 1000L) break;
				Thread.Sleep(50);
				var now = Compat.TickCount64;
				if (now - nextBeat >= 0) {
					nextBeat = now + 2000;
					long vsz = 0, asz = 0;
					try { if (File.Exists(videoTmp)) vsz = new FileInfo(videoTmp).Length; } catch { }
					try { if (File.Exists(wavTmp)) asz = new FileInfo(wavTmp).Length; } catch { }
					L($"  … {elapsed / 1000.0:0.0}s  videoBytes={vsz} wavBytes={asz} recElapsed={rec.Elapsed.TotalSeconds:0.00}s");
				}
			}
			var wallMs = (Compat.TickCount64 - t0) & 0xFFFFFFFFL;
			var wallSec = wallMs / 1000.0;
			both($"wall stop at {wallSec:0.000}s  recElapsed={rec.Elapsed.TotalSeconds:0.000}s");

			// 2) 停录并等合成
			rec.Stop();
			both("WaitFinalize…");
			rec.WaitFinalize(120_000);
			both($"HasAudio={rec.HasAudio} AudioError={rec.AudioError ?? "-"} final={rec.TempPath}");

			if (!rec.HasAudio) {
				both("FAIL: 最终文件无音轨");
				bad++;
			}

			// 合成成功会删临时 wav；失败时残留可作备份
			try {
				if (File.Exists(wavTmp)) {
					File.Copy(wavTmp, wavCopy, true);
					both($"wav keep: {RecordLog.FileInfo(wavCopy)}");
				}
			}
			catch (Exception ex) {
				both("wav keep EX: " + ex.Message);
			}

			// 3) 拷贝最终 mp4
			try {
				if (File.Exists(rec.TempPath)) {
					File.Copy(rec.TempPath, mp4Copy, true);
					both($"mp4 copy: {RecordLog.FileInfo(mp4Copy)}");
				}
			}
			catch (Exception ex) {
				both("mp4 copy EX: " + ex.Message);
				bad++;
			}

			// 4) 探测流时长
			double vSec = -1, aSec = -1;
			string probeNote = null;
			if (File.Exists(mp4Copy) && TryProbeDurations(mp4Copy, out vSec, out aSec, out probeNote)) {
				both($"probe mp4: video={vSec:0.000}s audio={aSec:0.000}s ({probeNote})");
			}
			else {
				both("probe mp4 FAIL: " + (probeNote ?? "missing mp4"));
				if (File.Exists(mp4Copy) && TryProbeViaFfmpegExe(mp4Copy, out vSec, out aSec, out var note2))
					both($"probe exe: video={vSec:0.000}s audio={aSec:0.000}s ({note2})");
				else
					both("probe exe FAIL");
			}

			// 5) 脉冲分析：优先成片音轨（MF），其次残留 wav / 抽出的 wav
			PulseStats pulse = null;
			string anNote = null;
			if (File.Exists(mp4Copy) && TryAnalyzeMedia(mp4Copy, wallSec, out pulse, out anNote)) {
				both($"analyze source: mp4 ({anNote})");
			}
			else if (File.Exists(wavCopy) && TryAnalyzeMedia(wavCopy, wallSec, out pulse, out anNote)) {
				both($"analyze source: wav ({anNote})");
			}
			else {
				string exNote = null;
				if (File.Exists(mp4Copy) && TryExtractAudioWav(mp4Copy, pcmCopy, out exNote)) {
					both($"extract audio: {RecordLog.FileInfo(pcmCopy)} ({exNote})");
					if (TryAnalyzeMedia(pcmCopy, wallSec, out pulse, out anNote))
						both($"analyze source: extracted wav ({anNote})");
				}
				else
					both("extract/analyze FAIL: " + (exNote ?? anNote ?? "-"));
			}

			if (pulse != null) {
				both("--- 脉冲分析（期望周期 0.200s = 0.1 有声 + 0.1 静音）---");
				both($"  audioFileSec={pulse.DurationSec:0.000}s  rate={pulse.SampleRate} ch={pulse.Channels}");
				both($"  windows={pulse.WindowCount} onRatio={pulse.OnRatio:P1} (期望≈50%)");
				both($"  onEdges={pulse.OnEdgeCount}  meanPeriod={pulse.MeanPeriodSec:0.0000}s  std={pulse.StdPeriodSec:0.0000}s");
				both($"  firstOnSec={pulse.FirstOnSec:0.000}  lastOnSec={pulse.LastOnSec:0.000}");
				both($"  maxSilenceGap={pulse.MaxSilenceGapSec:0.000}s  maxSoundRun={pulse.MaxSoundRunSec:0.000}s");
				if (pulse.Periods.Count > 0) {
					var show = pulse.Periods.Count > 12
						? pulse.Periods.Take(6).Concat(pulse.Periods.Skip(pulse.Periods.Count - 6))
						: pulse.Periods;
					both("  periods(s): " + string.Join(", ", show.Select(p => p.ToString("0.000"))));
					if (pulse.Periods.Count > 12) both("  …(middle omitted)");
				}
			}
			else {
				both("FAIL: 无音频可分析");
				bad++;
			}

			// 6) 判据
			both("--- 判据 ---");
			// 墙钟 vs 视频
			if (vSec > 0) {
				var dv = Math.Abs(vSec - wallSec);
				var ok = dv <= Math.Max(0.35, wallSec * 0.04);
				both($"  video vs wall: |{vSec:0.000}-{wallSec:0.000}|={dv:0.000}s  {(ok ? "OK" : "FAIL")}");
				if (!ok) bad++;
			}
			else {
				both("  video duration: N/A FAIL");
				bad++;
			}
			// 墙钟 vs 音轨
			var audioRef = aSec > 0 ? aSec : pulse?.DurationSec ?? -1;
			if (audioRef > 0) {
				var da = Math.Abs(audioRef - wallSec);
				var ok = da <= Math.Max(0.40, wallSec * 0.05);
				both($"  audio vs wall: |{audioRef:0.000}-{wallSec:0.000}|={da:0.000}s  {(ok ? "OK" : "FAIL")}");
				if (!ok) bad++;
			}
			else {
				both("  audio duration: N/A FAIL");
				bad++;
			}
			// 音画时长差（容器/MP3 帧对齐常有 ~0.1–0.3s 余量）
			if (vSec > 0 && audioRef > 0) {
				var dav = Math.Abs(vSec - audioRef);
				var ok = dav <= Math.Max(0.40, wallSec * 0.04);
				both($"  A/V duration delta: |{vSec:0.000}-{audioRef:0.000}|={dav:0.000}s  {(ok ? "OK" : "FAIL 音画时长不同步")}");
				if (!ok) bad++;
			}
			// 脉冲周期：均值应接近 0.2s（漂移/粘连/丢段会偏）
			if (pulse != null && pulse.MeanPeriodSec > 0) {
				var dp = Math.Abs(pulse.MeanPeriodSec - PeriodSec);
				var ok = dp <= 0.025 && pulse.StdPeriodSec <= 0.040;
				both($"  pulse period: mean={pulse.MeanPeriodSec:0.0000} (expect {PeriodSec:0.000}) Δ={dp:0.0000} std={pulse.StdPeriodSec:0.0000}  {(ok ? "OK" : "FAIL 脉冲周期异常→时间轴压缩/拉伸/丢段")}");
				if (!ok) bad++;
				// 有声占比约 50%
				var okR = pulse.OnRatio >= 0.30 && pulse.OnRatio <= 0.70;
				both($"  onRatio={pulse.OnRatio:P1} (expect ~50%)  {(okR ? "OK" : "FAIL 有声占比异常（声音时有时无/全静音垫）")}");
				if (!okR) bad++;
				// 期望边缘数 ≈ seconds/0.2
				var expectEdges = (int)Math.Round(wallSec / PeriodSec);
				var edgeOk = pulse.OnEdgeCount >= expectEdges * 0.6 && pulse.OnEdgeCount <= expectEdges * 1.4 + 2;
				both($"  onEdges={pulse.OnEdgeCount} expect~{expectEdges}  {(edgeOk ? "OK" : "FAIL 脉冲数偏差大")}");
				if (!edgeOk) bad++;
				// 单段静音不应远大于 0.1s（环回丢数被错误压缩时可能出现长静音或粘连）
				if (pulse.MaxSilenceGapSec > 0.45) {
					both($"  maxSilenceGap={pulse.MaxSilenceGapSec:0.000}s >0.45 FAIL（疑似丢声/补静音异常）");
					bad++;
				}
				else
					both($"  maxSilenceGap={pulse.MaxSilenceGapSec:0.000}s OK");
				if (pulse.MaxSoundRunSec > 0.45) {
					both($"  maxSoundRun={pulse.MaxSoundRunSec:0.000}s >0.45 FAIL（疑似静音未写入→有声段粘连）");
					bad++;
				}
				else
					both($"  maxSoundRun={pulse.MaxSoundRunSec:0.000}s OK");
			}

			both(bad == 0
				? "=== OK：10s 有声/静音循环下未检出明显音画不同步 ==="
				: $"=== FAIL：检出 {bad} 项异常（详见 {reportPath}）===");
			both($"RecordLog={RecordLog.LogPath}");
		}
		catch (Exception ex) {
			both("EX: " + ex);
			bad++;
		}
		finally {
			try { tone?.Dispose(); } catch { }
			try { rec?.DiscardTemps(); } catch { }
			try { rec?.Dispose(); } catch { }
			try {
				File.WriteAllText(reportPath, sb.ToString(), new UTF8Encoding(false));
				L("report: " + reportPath);
			}
			catch { }
			try { RecordLog.End(bad == 0 ? "ok" : "fail"); } catch { }
		}
		return bad == 0 ? 0 : 1;
	}

	static System.Drawing.Rectangle parseregion(string regionArg) {
		if (!string.IsNullOrWhiteSpace(regionArg)) {
			var parts = regionArg.Split(new[] { ',', 'x', 'X', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length >= 4) {
				var r = new System.Drawing.Rectangle(
					int.Parse(parts[0]), int.Parse(parts[1]),
					int.Parse(parts[2]), int.Parse(parts[3]));
				if (r.Width % 2 != 0) r.Width--;
				if (r.Height % 2 != 0) r.Height--;
				return r;
			}
		}
		var s = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
		var w = Math.Min(640, s.Width / 2 * 2);
		var h = Math.Min(360, s.Height / 2 * 2);
		var reg = new System.Drawing.Rectangle(
			s.Left + (s.Width - w) / 2, s.Top + (s.Height - h) / 2, w, h);
		if (reg.Width % 2 != 0) reg.Width--;
		if (reg.Height % 2 != 0) reg.Height--;
		return reg;
	}

	/// <summary>0.1s 正弦 + 0.1s 静音循环播放到默认输出（供环回采集）。</summary>
	sealed class ToneBeepPlayer : IDisposable {
		readonly double freq;
		readonly double onSec;
		readonly double offSec;
		WaveOutEvent wo;
		ISampleProvider src;
		bool disposed;

		public ToneBeepPlayer(double freqHz, double on, double off) {
			freq = freqHz;
			onSec = on;
			offSec = off;
		}

		public void Start() {
			// 用采样级拼：有声块 + 静音块，循环
			var rate = 44100;
			var tone = new SignalGenerator(rate, 1) {
				Frequency = freq,
				Gain = 0.28,
				Type = SignalGeneratorType.Sin,
			};
			src = new BeepLoopSampleProvider(tone, rate, onSec, offSec);
			wo = new WaveOutEvent { DesiredLatency = 50 };
			wo.Init(src.ToWaveProvider16());
			wo.Play();
		}

		public void Dispose() {
			if (disposed) return;
			disposed = true;
			try { wo?.Stop(); } catch { }
			try { wo?.Dispose(); } catch { }
			wo = null;
		}
	}

	/// <summary>将源采样按 on/off 秒数门控循环输出。</summary>
	sealed class BeepLoopSampleProvider : ISampleProvider {
		readonly ISampleProvider src;
		readonly int rate;
		readonly int onSamples;
		readonly int offSamples;
		readonly int period;
		long pos;
		readonly float[] buf = new float[1024];

		public BeepLoopSampleProvider(ISampleProvider source, int sampleRate, double onSec, double offSec) {
			src = source ?? throw new ArgumentNullException(nameof(source));
			rate = sampleRate;
			onSamples = Math.Max(1, (int)(onSec * sampleRate));
			offSamples = Math.Max(1, (int)(offSec * sampleRate));
			period = onSamples + offSamples;
			WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
		}

		public WaveFormat WaveFormat { get; }

		public int Read(float[] buffer, int offset, int count) {
			var n = 0;
			while (n < count) {
				var inPeriod = (int)(pos % period);
				var on = inPeriod < onSamples;
				var take = Math.Min(count - n, on ? onSamples - inPeriod : offSamples - (inPeriod - onSamples));
				if (take <= 0) take = 1;
				if (take > buf.Length) take = buf.Length;
				if (on) {
					var got = src.Read(buf, 0, take);
					if (got <= 0) {
						// 源异常：填 0
						for (var i = 0; i < take; i++) buffer[offset + n + i] = 0;
						n += take;
						pos += take;
						continue;
					}
					for (var i = 0; i < got; i++)
						buffer[offset + n + i] = buf[i];
					// 源不足时补 0
					for (var i = got; i < take; i++)
						buffer[offset + n + i] = 0;
					n += take;
					pos += take;
				}
				else {
					for (var i = 0; i < take; i++)
						buffer[offset + n + i] = 0;
					n += take;
					pos += take;
				}
			}
			return count;
		}
	}

	sealed class PulseStats {
		public double DurationSec;
		public int SampleRate;
		public int Channels;
		public int WindowCount;
		public double OnRatio;
		public int OnEdgeCount;
		public double MeanPeriodSec;
		public double StdPeriodSec;
		public double FirstOnSec = -1;
		public double LastOnSec = -1;
		public double MaxSilenceGapSec;
		public double MaxSoundRunSec;
		public List<double> Periods = new();
	}

	static bool TryAnalyzeMedia(string path, double wallSec, out PulseStats st, out string note) {
		st = null;
		note = null;
		try {
			// wav/mp3 走 AudioFileReader；mp4 走 MediaFoundationReader
			WaveStream ws = null;
			ISampleProvider sp = null;
			try {
				var ext = Path.GetExtension(path)?.ToLowerInvariant();
				if (ext is ".wav" or ".mp3" or ".aiff") {
					var afr = new AudioFileReader(path);
					ws = afr;
					sp = afr;
				}
				else {
					var mf = new MediaFoundationReader(path);
					ws = mf;
					sp = mf.ToSampleProvider();
				}
				st = analyzepulses(sp, ws, wallSec);
				note = ext ?? "media";
				return st != null && st.WindowCount > 0;
			}
			finally {
				try { ws?.Dispose(); } catch { }
			}
		}
		catch (Exception ex) {
			note = ex.Message;
			st = null;
			return false;
		}
	}

	static PulseStats analyzepulses(ISampleProvider sp, WaveStream ws, double wallSec) {
		var st = new PulseStats();
		var fmt = sp.WaveFormat;
		st.SampleRate = fmt.SampleRate;
		st.Channels = fmt.Channels;
		try {
			st.DurationSec = ws != null && ws.TotalTime.TotalSeconds > 0
				? ws.TotalTime.TotalSeconds
				: wallSec;
		}
		catch {
			st.DurationSec = wallSec;
		}

		var winSamples = Math.Max(1, (int)(WinSec * st.SampleRate));
		var ch = Math.Max(1, st.Channels);
		var frame = new float[winSamples * ch];
		var onFlags = new List<bool>((int)(st.DurationSec / WinSec) + 8);

		while (true) {
			var got = sp.Read(frame, 0, frame.Length);
			if (got <= 0) break;
			var frames = got / ch;
			if (frames <= 0) break;
			double sum = 0;
			var n = 0;
			for (var i = 0; i < frames; i++) {
				float s = 0;
				for (var c = 0; c < ch; c++)
					s += frame[i * ch + c];
				s /= ch;
				sum += s * s;
				n++;
			}
			var rms = n > 0 ? Math.Sqrt(sum / n) : 0;
			onFlags.Add(rms >= RmsOn);
		}
		st.WindowCount = onFlags.Count;
		if (st.WindowCount == 0) return st;

		// 用实际窗数修正时长
		st.DurationSec = st.WindowCount * WinSec;
		var onN = onFlags.Count(x => x);
		st.OnRatio = onN / (double)st.WindowCount;

		// 上升沿（静音→有声）
		var edges = new List<double>();
		for (var i = 1; i < onFlags.Count; i++) {
			if (!onFlags[i - 1] && onFlags[i])
				edges.Add(i * WinSec);
		}
		st.OnEdgeCount = edges.Count;
		if (edges.Count > 0) {
			st.FirstOnSec = edges[0];
			st.LastOnSec = edges[edges.Count - 1];
		}
		for (var i = 1; i < edges.Count; i++)
			st.Periods.Add(edges[i] - edges[i - 1]);
		if (st.Periods.Count > 0) {
			st.MeanPeriodSec = st.Periods.Average();
			var mean = st.MeanPeriodSec;
			st.StdPeriodSec = Math.Sqrt(st.Periods.Average(p => (p - mean) * (p - mean)));
		}

		// 最长静音 / 最长有声 run
		var run = 1;
		var last = onFlags[0];
		for (var i = 1; i < onFlags.Count; i++) {
			if (onFlags[i] == last) {
				run++;
			}
			else {
				var sec = run * WinSec;
				if (last) st.MaxSoundRunSec = Math.Max(st.MaxSoundRunSec, sec);
				else st.MaxSilenceGapSec = Math.Max(st.MaxSilenceGapSec, sec);
				last = onFlags[i];
				run = 1;
			}
		}
		{
			var sec = run * WinSec;
			if (last) st.MaxSoundRunSec = Math.Max(st.MaxSoundRunSec, sec);
			else st.MaxSilenceGapSec = Math.Max(st.MaxSilenceGapSec, sec);
		}
		_ = wallSec;
		return st;
	}

	internal static bool TryProbeDurations(string path, out double videoSec, out double audioSec, out string note) {
		videoSec = -1;
		audioSec = -1;
		note = null;
		if (!FfmpegLoader.TryInit(out var err)) {
			note = "ffmpeg dll: " + err;
			return false;
		}
		AVFormatContext* fmt = null;
		try {
			if (ffmpeg.avformat_open_input(&fmt, path, null, null) < 0 || fmt == null) {
				note = "open_input fail";
				return false;
			}
			if (ffmpeg.avformat_find_stream_info(fmt, null) < 0) {
				note = "find_stream_info fail";
				return false;
			}
			for (uint i = 0; i < fmt->nb_streams; i++) {
				var st = fmt->streams[i];
				if (st == null || st->codecpar == null) continue;
				var sec = streamdurationsec(st, fmt);
				if (st->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO) {
					if (sec > videoSec) videoSec = sec;
				}
				else if (st->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO) {
					if (sec > audioSec) audioSec = sec;
				}
			}
			// container duration 兜底
			if (videoSec < 0 && fmt->duration > 0)
				videoSec = fmt->duration / (double)ffmpeg.AV_TIME_BASE;
			if (audioSec < 0 && fmt->duration > 0 && FfmpegRemux.HasAudioStream(path))
				audioSec = fmt->duration / (double)ffmpeg.AV_TIME_BASE;
			note = $"streams={fmt->nb_streams}";
			return videoSec > 0 || audioSec > 0;
		}
		catch (Exception ex) {
			note = ex.Message;
			return false;
		}
		finally {
			if (fmt != null) {
				var f = fmt;
				ffmpeg.avformat_close_input(&f);
			}
		}
	}

	static double streamdurationsec(AVStream* st, AVFormatContext* fmt) {
		if (st->duration > 0 && st->time_base.den > 0) {
			return st->duration * (double)st->time_base.num / st->time_base.den;
		}
		if (st->nb_frames > 0 && st->avg_frame_rate.den > 0 && st->avg_frame_rate.num > 0)
			return st->nb_frames * (double)st->avg_frame_rate.den / st->avg_frame_rate.num;
		if (fmt->duration > 0)
			return fmt->duration / (double)ffmpeg.AV_TIME_BASE;
		return -1;
	}

	static bool TryProbeViaFfmpegExe(string path, out double videoSec, out double audioSec, out string note) {
		videoSec = -1;
		audioSec = -1;
		note = null;
		var exe = findffmpeg();
		if (exe == null) {
			note = "no ffmpeg.exe";
			return false;
		}
		try {
			var psi = new ProcessStartInfo {
				FileName = exe,
				Arguments = $"-hide_banner -i \"{path}\"",
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardError = true,
				RedirectStandardOutput = true,
			};
			using var p = Process.Start(psi);
			if (p == null) {
				note = "start fail";
				return false;
			}
			var err = p.StandardError.ReadToEnd();
			p.WaitForExit(30_000);
			// Duration: 00:00:10.04, start: 0.000000, bitrate: ...
			// Stream #0:0: Video: ...
			// Stream #0:1: Audio: ...
			foreach (var line in err.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
				var t = line.Trim();
				if (t.StartsWith("Duration:", StringComparison.OrdinalIgnoreCase)) {
					var d = parsehhmmss(t);
					if (d > 0) {
						if (videoSec < 0) videoSec = d;
						if (audioSec < 0) audioSec = d;
					}
				}
			}
			note = "ffmpeg -i";
			return videoSec > 0 || audioSec > 0;
		}
		catch (Exception ex) {
			note = ex.Message;
			return false;
		}
	}

	static double parsehhmmss(string line) {
		// Duration: 00:00:10.04,
		var idx = line.IndexOf("Duration:", StringComparison.OrdinalIgnoreCase);
		if (idx < 0) return -1;
		var s = line.Substring(idx + "Duration:".Length).Trim();
		var sp = s.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
		if (sp.Length == 0) return -1;
		var parts = sp[0].Split(':');
		if (parts.Length != 3) return -1;
		if (!double.TryParse(parts[0], out var hh)) return -1;
		if (!double.TryParse(parts[1], out var mm)) return -1;
		if (!double.TryParse(parts[2], System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out var ss)) return -1;
		return hh * 3600 + mm * 60 + ss;
	}

	static bool TryExtractAudioWav(string mp4, string wavOut, out string note) {
		note = null;
		var exe = findffmpeg();
		if (exe == null) {
			note = "no ffmpeg.exe";
			return false;
		}
		try {
			var psi = new ProcessStartInfo {
				FileName = exe,
				Arguments = $"-y -i \"{mp4}\" -vn -acodec pcm_s16le -ar 22050 -ac 1 \"{wavOut}\"",
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardError = true,
				RedirectStandardOutput = true,
			};
			using var p = Process.Start(psi);
			if (p == null) {
				note = "start fail";
				return false;
			}
			var err = p.StandardError.ReadToEnd();
			p.WaitForExit(60_000);
			if (p.ExitCode == 0 && File.Exists(wavOut) && new FileInfo(wavOut).Length > 100) {
				note = "ok";
				return true;
			}
			note = "code=" + p.ExitCode + " " + (err.Length > 200 ? err.Substring(err.Length - 200) : err);
			return false;
		}
		catch (Exception ex) {
			note = ex.Message;
			return false;
		}
	}

	static string findffmpeg() {
		// 与 FfmpegRemux 一致，但阈值放宽：部分构建略小于 2MB 仍可用
		const long MinBytes = 500_000;
		var baseDir = AppDomain.CurrentDomain.BaseDirectory;
		var cands = new List<string> {
			Path.Combine(baseDir, "ffmpeg64", "ffmpeg.exe"),
			Path.Combine(baseDir, "ffmpeg", "ffmpeg.exe"),
			Path.Combine(baseDir, "ffmpeg.exe"),
			@"C:\bin\ffmpeg.exe",
		};
		try {
			var path = Environment.GetEnvironmentVariable("PATH") ?? "";
			foreach (var dir in path.Split(Path.PathSeparator)) {
				if (string.IsNullOrWhiteSpace(dir)) continue;
				cands.Add(Path.Combine(dir.Trim(), "ffmpeg.exe"));
			}
		}
		catch { }
		foreach (var c in cands) {
			try {
				if (File.Exists(c) && new FileInfo(c).Length >= MinBytes)
					return c;
			}
			catch { }
		}
		return null;
	}
}
