using System.Collections.Generic;
using System.IO;
using System.Text;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ScreenKit;

/// <summary>
/// WASAPI 采集：扬声器环回 / 麦克风 / 两者（分文件再混合）。
/// 输出指定采样率、单/立体声 16-bit WAV（默认 22050 Hz 立体声）。
/// <para>
/// 环回在无播放时可能长时间不触发 DataAvailable；必须按墙钟补静音，
/// 否则有声段会贴到 t=0，与视频错位。
/// </para>
/// </summary>
sealed class AudioCapture : IDisposable {
	const int SilenceChunk = 8192;
	/// <summary>静音补齐周期：环回无播放时不触发 DataAvailable，需后台按墙钟灌静音。</summary>
	const int PadIntervalMs = 200;
	/// <summary>补静音时相对墙钟的余量，避免与在途缓冲抢写导致重叠。</summary>
	const int PadSlackMs = 50;

	readonly RecordAudioMode mode;
	readonly string wavPath;
	readonly int outRate;
	readonly bool outMono;
	readonly object gate = new();

	WasapiLoopbackCapture loop;
	WasapiCapture mic;
	WavPlace writerLoop;
	WavPlace writerMic;
	WaveFormat fmtLoop;
	WaveFormat fmtMic;
	long bytesLoop;
	long bytesMic;
	long realLoop;
	long realMic;
	int overwriteCount;
	long overwriteBytes;
	string pathLoop;
	string pathMic;
	long startTick;
	long pauseAccum;
	long pauseStart;
	volatile bool stop;
	volatile bool paused;
	bool disposed;
	bool stopped;
	long firstDataTick; // 0=尚未收到
	int dataCallbacks;
	long lastBeatTick;
	long padBytesTotal;
	Thread padThread;
	readonly object qgate = new();
	readonly Queue<byte[]> qloop = new();
	readonly Queue<byte[]> qmic = new();

	public long BytesLoop => bytesLoop;
	public long BytesMic => bytesMic;
	/// <summary>累计补入的静音字节（环回静音缺口）。</summary>
	public long PadBytesTotal => padBytesTotal;
	/// <summary>
	/// 为 true 时单路（扬声器/麦克风）停录不二次重采样规范化。
	/// 长录屏可显著缩短结束时间；采样率/声道由后续合成阶段处理。
	/// 麦+扬混音仍会规范化。系统 H.264 用 <see cref="QueuePcm"/>，不写 WAV。
	/// </summary>
	public bool SkipNormalize { get; set; }
	/// <summary>
	/// 为 true 时不写 WAV。设备字节（含墙钟静音）进入队列，由录制线程拉走。
	/// </summary>
	public bool QueuePcm { get; set; }
	public WaveFormat LoopFormat => fmtLoop;
	public WaveFormat MicFormat => fmtMic;

	/// <summary>取出一路排队的设备字节。0 为扬声器环回，1 为麦克风。</summary>
	public bool TryTake(int source, out byte[] data) {
		var q = source == 0 ? qloop : qmic;
		lock (qgate) {
			if (q.Count == 0) { data = null; return false; }
			data = q.Dequeue();
			return true;
		}
	}
	/// <summary>首包音频相对开始的毫秒；-1 表示从未收到。</summary>
	public long FirstDataMs =>
		firstDataTick == 0 || startTick == 0 ? -1 : Math.Max(0, firstDataTick - startTick);

	public AudioCapture(string wavPath, RecordAudioMode mode, int sampleRateHz = 22050, bool mono = false) {
		this.wavPath = wavPath ?? throw new ArgumentNullException(nameof(wavPath));
		this.mode = mode;
		outRate = sampleRateHz > 0 ? sampleRateHz : 22050;
		outMono = mono;
		if (mode == RecordAudioMode.Off)
			throw new ArgumentException("音频模式为 Off");
	}

	public void Start() {
		if (stop) stop = false;
		paused = false;
		pauseAccum = 0;
		bytesLoop = 0;
		bytesMic = 0;
		realLoop = 0;
		realMic = 0;
		overwriteCount = 0;
		overwriteBytes = 0;
		firstDataTick = 0;
		dataCallbacks = 0;
		padBytesTotal = 0;
		var dir = Path.GetDirectoryName(wavPath);
		var baseName = Path.GetFileNameWithoutExtension(wavPath);
		RecordLog.Step("AudioCapture.Start", $"mode={mode} outRate={outRate} mono={outMono} path={wavPath}");

		if (mode is RecordAudioMode.Speakers or RecordAudioMode.MicAndSpeakers) {
			pathLoop = mode == RecordAudioMode.Speakers
				? wavPath
				: Path.Combine(dir ?? ".", baseName + "_spk.wav");
			// 共享模式环回：采集「当前正在播放」的声音
			loop = new WasapiLoopbackCapture();
			fmtLoop = loop.WaveFormat;
			CaptureLog.Info($"Loopback format={fmtLoop}");
			RecordLog.Step("loopback_open",
				$"fmt={fmtLoop} rate={fmtLoop.SampleRate} ch={fmtLoop.Channels} bits={fmtLoop.BitsPerSample} " +
				$"bps={fmtLoop.AverageBytesPerSecond} queue={QueuePcm} path={pathLoop}");
			if (!QueuePcm)
				writerLoop = WavPlace.Create(pathLoop, fmtLoop);
			loop.DataAvailable += (_, e) => ondata(0, ref bytesLoop, writerLoop, fmtLoop, e, "loop");
			loop.RecordingStopped += (_, e) => {
				if (e.Exception != null) {
					CaptureLog.Ex("Loopback", e.Exception);
					RecordLog.Ex("Loopback.Stopped", e.Exception);
				}
				else
					RecordLog.Step("loopback_stopped", "ok");
			};
			loop.StartRecording();
			RecordLog.Step("loopback_started", "WasapiLoopbackCapture");
		}

		if (mode is RecordAudioMode.Mic or RecordAudioMode.MicAndSpeakers) {
			pathMic = mode == RecordAudioMode.Mic
				? wavPath
				: Path.Combine(dir ?? ".", baseName + "_mic.wav");
			try {
				mic = new WasapiCapture();
				fmtMic = mic.WaveFormat;
				RecordLog.Step("mic_open",
					$"fmt={fmtMic} rate={fmtMic.SampleRate} ch={fmtMic.Channels} queue={QueuePcm} path={pathMic}");
				if (!QueuePcm)
					writerMic = WavPlace.Create(pathMic, fmtMic);
				mic.DataAvailable += (_, e) => ondata(1, ref bytesMic, writerMic, fmtMic, e, "mic");
				mic.RecordingStopped += (_, e) => {
					if (e.Exception != null) {
						CaptureLog.Ex("Mic", e.Exception);
						RecordLog.Ex("Mic.Stopped", e.Exception);
					}
					else
						RecordLog.Step("mic_stopped", "ok");
				};
				mic.StartRecording();
				RecordLog.Step("mic_started", "WasapiCapture");
			}
			catch (Exception ex) {
				if (mode == RecordAudioMode.Mic)
					throw new InvalidOperationException("无法打开麦克风: " + ex.Message, ex);
				CaptureLog.Ex("Mic optional", ex);
				RecordLog.Ex("Mic optional", ex);
			}
		}

		startTick = Compat.TickCount64;
		lastBeatTick = startTick;
		padThread = new Thread(padloop) { IsBackground = true, Name = "AudioCapture.Pad" };
		padThread.Start();
		RecordLog.Step("AudioCapture.started", $"tick={startTick}");
	}

	/// <summary>有效录制毫秒（排除暂停）。</summary>
	long effectivems() {
		if (startTick == 0) return 0;
		var now = Compat.TickCount64;
		var pausedPart = pauseAccum;
		if (paused) pausedPart += Math.Max(0, now - pauseStart);
		// 与视频侧一致：无符号 32 位毫秒差
		var wall = (now - startTick) & 0xFFFFFFFFL;
		return Math.Max(0, (long)wall - pausedPart);
	}

	/// <summary>按墙钟应写入的字节数（对齐到 BlockAlign）。</summary>
	static long expectedbytes(WaveFormat fmt, long ms) {
		if (fmt == null || ms <= 0) return 0;
		var bps = (long)fmt.AverageBytesPerSecond;
		if (bps <= 0) return 0;
		var raw = bps * ms / 1000;
		var align = Math.Max(1, fmt.BlockAlign);
		return raw - (raw % align);
	}

	/// <summary>
	/// 后台按墙钟补静音。声音时有时无时，环回可能数秒不回调；
	/// 若只在下一包到达时补齐，长静音段依赖一次性大块 pad，且与突发缓冲叠加易越写越超前。
	/// </summary>
	void padloop() {
		while (!stop) {
			try { Thread.Sleep(PadIntervalMs); }
			catch { break; }
			if (stop || paused) continue;
			lock (gate) {
				try { padtowardwall(PadSlackMs); }
				catch (Exception ex) { RecordLog.Ex("audio.padloop", ex); }
			}
		}
	}

	/// <summary>若已写时长落后墙钟，补静音到 wall-slack（不缩短已写内容）。</summary>
	void padtowardwall(int slackMs) {
		var ms = Math.Max(0, effectivems() - Math.Max(0, slackMs));
		if (fmtLoop != null && (QueuePcm || writerLoop != null))
			padto(0, ref bytesLoop, writerLoop, fmtLoop, expectedbytes(fmtLoop, ms));
		if (fmtMic != null && (QueuePcm || writerMic != null))
			padto(1, ref bytesMic, writerMic, fmtMic, expectedbytes(fmtMic, ms));
	}

	void ondata(int src, ref long written, WavPlace w, WaveFormat fmt, WaveInEventArgs e, string tag) {
		if (stop || paused || e.BytesRecorded <= 0) return;
		if (!QueuePcm && w == null) return;
		lock (gate) {
			try {
				if (firstDataTick == 0) {
					firstDataTick = Compat.TickCount64;
					RecordLog.Step("audio_first_data",
						$"{tag} ms={FirstDataMs} bytes={e.BytesRecorded} fmt={fmt}");
				}
				dataCallbacks++;
				if (QueuePcm) {
					// 队列不支持改写，仍按到达顺序接在末尾。
					var want = expectedbytes(fmt, effectivems());
					var before = want - e.BytesRecorded;
					if (before < written) before = written;
					padto(src, ref written, w, fmt, before);
					writebytes(src, w, e.Buffer, e.BytesRecorded);
					written += e.BytesRecorded;
				}
				else if (src == 0)
					placepacket(w, fmt, e.Buffer, e.BytesRecorded, ref written, ref realLoop);
				else
					placepacket(w, fmt, e.Buffer, e.BytesRecorded, ref written, ref realMic);
				// 约 30s 一次音频进度（不每包刷）
				var now = Compat.TickCount64;
				if (now - lastBeatTick >= 30_000) {
					lastBeatTick = now;
					var writtenMs = fmt.AverageBytesPerSecond > 0
						? written * 1000 / fmt.AverageBytesPerSecond : 0;
					RecordLog.Step("audio_beat",
						$"{tag} wallMs={effectivems()} writtenMs={writtenMs} written={written} " +
						$"callbacks={dataCallbacks} padTotal={padBytesTotal} " +
						$"overwrite={overwriteCount}/{overwriteBytes} " +
						$"loop={bytesLoop} mic={bytesMic}");
				}
			}
			catch (Exception ex) {
				RecordLog.Ex("audio.ondata." + tag, ex);
			}
		}
	}

	/// <summary>
	/// 把本包放到「结束于当前墙钟」的位置。
	/// 只有大约 20ms 以内的缝才接在上一包后面，免得一声卡顿把后面整段声音提前。
	/// 缝更大，或本包开始位置已经落在已写内容里时，按墙钟改写静音，不接到末尾。
	/// </summary>
	void placepacket(WavPlace w, WaveFormat fmt, byte[] buf, int count, ref long written, ref long realEnd) {
		if (w == null || fmt == null || buf == null) return;
		var align = Math.Max(1, fmt.BlockAlign);
		if (count > buf.Length) count = buf.Length;
		count -= count % align;
		if (count <= 0) return;
		var wall = expectedbytes(fmt, effectivems());
		var start = wall - count;
		if (start < 0) {
			var skip = (int)(-start);
			skip -= skip % align;
			if (skip >= count) return;
			var slice = new byte[count - skip];
			Buffer.BlockCopy(buf, skip, slice, 0, slice.Length);
			buf = slice;
			count = slice.Length;
			start = 0;
		}
		start -= start % align;
		var at = choosewritepos(start, realEnd, align, fmt.AverageBytesPerSecond);
		if (at == realEnd && realEnd > 0) {
			w.WriteAt(realEnd, buf, 0, count);
			realEnd += count;
		}
		else if (at > written) {
			padBytesTotal += start - written;
			w.PadTo(start);
			w.WriteAt(start, buf, 0, count);
			realEnd = start + count;
		}
		else {
			if (at < written) {
				overwriteCount++;
				overwriteBytes += written - at;
			}
			w.WriteAt(at, buf, 0, count);
			realEnd = at + count;
		}
		written = w.DataLength;
	}

	/// <summary>
	/// 本包在文件中的起点。大约 20ms 以内的正向缝接在上一包末尾；
	/// 更长的停顿、或与已写采样重叠时，用墙钟起点（可改写已垫的静音）。
	/// realEnd 为 0 时还没有真实采样，不能粘到文件头。
	/// </summary>
	internal static long choosewritepos(long start, long realEnd, int align, int bytesPerSec) {
		var glue = Math.Max((long)Math.Max(1, align) * 4, Math.Max(1, bytesPerSec) / 50L);
		var hole = start - realEnd;
		if (realEnd > 0 && hole >= 0 && hole <= glue)
			return realEnd;
		return start;
	}

	void padto(int src, ref long written, WavPlace w, WaveFormat fmt, long target) {
		if (fmt == null) return;
		if (!QueuePcm && w == null) return;
		var align = Math.Max(1, fmt.BlockAlign);
		target -= target % align;
		var gap = target - written;
		if (gap < align) return;
		padBytesTotal += gap;
		if (!QueuePcm) {
			w.PadTo(target);
			written = w.DataLength;
			return;
		}
		var buf = new byte[SilenceChunk];
		while (gap > 0) {
			var n = (int)Math.Min(gap, buf.Length);
			n -= n % align;
			if (n <= 0) break;
			writebytes(src, w, buf, n);
			written += n;
			gap -= n;
		}
	}

	void writebytes(int src, WavPlace w, byte[] buf, int count) {
		if (count <= 0 || buf == null) return;
		if (count > buf.Length) count = buf.Length;
		if (QueuePcm) {
			var copy = new byte[count];
			Buffer.BlockCopy(buf, 0, copy, 0, count);
			var q = src == 0 ? qloop : qmic;
			lock (qgate) q.Enqueue(copy);
			return;
		}
		w.WriteAt(w.DataLength, buf, 0, count);
	}

	/// <summary>静音垫上后，迟到的整段采样必须盖住静音，不能接到末尾。短暂停顿也不能把后面的声音整段提前。</summary>
	internal static string CheckWavPlace() {
		var rate = 32000;
		var align = 4;
		var sec = (long)rate;
		if (choosewritepos(sec, 0, align, rate) != sec)
			return "第一包不能粘到文件头";
		var gap100 = sec / 10;
		if (choosewritepos(8000 + gap100, 8000, align, rate) != 8000 + gap100)
			return "100ms 停顿被提前";
		var gap10 = sec / 100;
		if (choosewritepos(8000 + gap10, 8000, align, rate) != 8000)
			return "10ms 缝应接上";
		if (choosewritepos(7000, 8000, align, rate) != 7000)
			return "重叠应改写";
		var path = Path.Combine(Path.GetTempPath(), "sk_wavplace.wav");
		try {
			var fmt = WaveFormat.CreateIeeeFloatWaveFormat(8000, 1);
			var bps = fmt.AverageBytesPerSecond;
			var half = bps / 2;
			half -= half % 4;
			using (var w = WavPlace.Create(path, fmt)) {
				w.PadTo(bps);
				var tone = new byte[half];
				for (var i = 0; i < tone.Length; i += 4) {
					tone[i] = 0;
					tone[i + 1] = 0;
					tone[i + 2] = 0x80;
					tone[i + 3] = 0x3F;
				}
				w.WriteAt(half, tone, 0, tone.Length);
				if (w.DataLength != bps) return "长度 " + w.DataLength;
			}
			using (var r = new AudioFileReader(path)) {
				var buf = new float[bps / 4];
				var n = r.Read(buf, 0, buf.Length);
				if (n < buf.Length / 2) return "读到 " + n;
				if (Math.Abs(buf[1]) > 0.02f) return "头部应是静音 " + buf[1];
				var at = half / 4 + 4;
				if (at >= n) return "后半越界";
				if (Math.Abs(buf[at] - 1f) > 0.02f) return "后半 " + buf[at];
			}
			return null;
		}
		catch (Exception ex) { return ex.Message; }
		finally {
			try { if (File.Exists(path)) File.Delete(path); } catch { }
		}
	}

	public void Pause() {
		if (paused) return;
		paused = true;
		pauseStart = Compat.TickCount64;
		RecordLog.Step("AudioCapture.Pause", $"ms={effectivems()} loop={bytesLoop} mic={bytesMic}");
		// 暂停瞬间把静音补齐，避免恢复后缺口
		lock (gate) {
			try { padtowardwall(0); }
			catch (Exception ex) { RecordLog.Ex("AudioCapture.Pause pad", ex); }
		}
	}

	public void Resume() {
		if (!paused) return;
		paused = false;
		pauseAccum += Math.Max(0, Compat.TickCount64 - pauseStart);
		RecordLog.Step("AudioCapture.Resume", $"pauseAccum={pauseAccum}");
	}

	public void Stop() {
		if (stopped) return;
		stopped = true;
		RecordLog.Step("AudioCapture.Stop",
			$"ms={effectivems()} callbacks={dataCallbacks} firstDataMs={FirstDataMs} " +
			$"loop={bytesLoop} mic={bytesMic} padTotal={padBytesTotal} " +
			$"overwrite={overwriteCount}/{overwriteBytes}");
		stop = true;
		try { padThread?.Join(500); } catch { }
		padThread = null;
		try { loop?.StopRecording(); } catch (Exception ex) { RecordLog.Ex("loop.StopRecording", ex); }
		try { mic?.StopRecording(); } catch (Exception ex) { RecordLog.Ex("mic.StopRecording", ex); }
		try { Thread.Sleep(120); } catch { }

		// 尾部静音：对齐到停止时的墙钟，避免末段无声被截断
		lock (gate) {
			try {
				var ms = effectivems();
				padtowardwall(0);
				CaptureLog.Info($"Audio pad stop ms={ms} loopBytes={bytesLoop} micBytes={bytesMic}");
				RecordLog.Step("audio_pad_stop",
					$"ms={ms} loopBytes={bytesLoop} micBytes={bytesMic} padTotal={padBytesTotal} " +
					$"real={realLoop}/{realMic} overwrite={overwriteCount}/{overwriteBytes}");
			}
			catch (Exception ex) {
				CaptureLog.Ex("Audio pad stop", ex);
				RecordLog.Ex("Audio pad stop", ex);
			}
			try { writerLoop?.Dispose(); } catch (Exception ex) { RecordLog.Ex("writerLoop.Dispose", ex); }
			writerLoop = null;
			try { writerMic?.Dispose(); } catch (Exception ex) { RecordLog.Ex("writerMic.Dispose", ex); }
			writerMic = null;
		}
		try { loop?.Dispose(); } catch { }
		loop = null;
		try { mic?.Dispose(); } catch { }
		mic = null;

		if (QueuePcm) {
			RecordLog.Step("audio_finalize_end", "queue only");
			return;
		}
		// 混合或规范化到目标 wavPath
		try {
			RecordLog.Step("audio_finalize_begin",
				$"mode={mode} pathLoop={RecordLog.FileInfo(pathLoop)} pathMic={RecordLog.FileInfo(pathMic)}");
			finalizewav();
			RecordLog.Step("audio_finalize_end", RecordLog.FileInfo(wavPath));
		}
		catch (Exception ex) {
			CaptureLog.Ex("Audio finalize", ex);
			RecordLog.Ex("Audio finalize", ex);
		}
	}

	void finalizewav() {
		if (mode == RecordAudioMode.Speakers) {
			if (File.Exists(wavPath)) {
				if (SkipNormalize) {
					RecordLog.Step("normalize", "speakers skip (defer to remux) " + RecordLog.FileInfo(wavPath));
				}
				else {
					RecordLog.Step("normalize", "speakers inplace " + RecordLog.FileInfo(wavPath));
					normalizeinplace(wavPath);
				}
			}
			else
				RecordLog.Step("normalize", "speakers missing wav");
			return;
		}
		if (mode == RecordAudioMode.Mic) {
			if (File.Exists(wavPath)) {
				if (SkipNormalize) {
					RecordLog.Step("normalize", "mic skip (defer to remux) " + RecordLog.FileInfo(wavPath));
				}
				else {
					RecordLog.Step("normalize", "mic inplace " + RecordLog.FileInfo(wavPath));
					normalizeinplace(wavPath);
				}
			}
			else
				RecordLog.Step("normalize", "mic missing wav");
			return;
		}
		// MicAndSpeakers：混合两轨
		if (File.Exists(pathLoop) && File.Exists(pathMic)) {
			RecordLog.Step("mix", $"spk+mic -> {wavPath}");
			mixto(pathLoop, pathMic, wavPath);
			try { File.Delete(pathLoop); } catch { }
			try { File.Delete(pathMic); } catch { }
		}
		else if (File.Exists(pathLoop)) {
			RecordLog.Step("mix", "only loop -> normalize");
			normalizefile(pathLoop, wavPath);
			try { if (pathLoop != wavPath) File.Delete(pathLoop); } catch { }
		}
		else if (File.Exists(pathMic)) {
			RecordLog.Step("mix", "only mic -> normalize");
			normalizefile(pathMic, wavPath);
			try { if (pathMic != wavPath) File.Delete(pathMic); } catch { }
		}
		else
			RecordLog.Step("mix", "no source files");
	}

	void normalizeinplace(string path) {
		var tmp = path + ".norm.wav";
		try {
			var before = new FileInfo(path).Length;
			normalizefile(path, tmp);
			if (File.Exists(tmp)) {
				var after = new FileInfo(tmp).Length;
				File.Delete(path);
				File.Move(tmp, path);
				RecordLog.Step("normalize_ok", $"before={before} after={after}");
			}
			else
				RecordLog.Step("normalize_skip", "tmp missing");
		}
		catch (Exception ex) {
			RecordLog.Ex("normalizeinplace", ex);
			try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
		}
	}

	void normalizefile(string src, string dst) {
		RecordLog.Step("normalizefile", $"{RecordLog.FileInfo(src)} -> {dst}");
		using var reader = new AudioFileReader(src);
		WaveFileWriter.CreateWaveFile16(dst, tooutput(reader));
		RecordLog.Step("normalizefile_done", RecordLog.FileInfo(dst));
	}

	void mixto(string spk, string mic, string dst) {
		RecordLog.Step("mixto", $"spk={RecordLog.FileInfo(spk)} mic={RecordLog.FileInfo(mic)}");
		using var r1 = new AudioFileReader(spk);
		using var r2 = new AudioFileReader(mic);
		ISampleProvider s1 = r1;
		ISampleProvider s2 = r2;
		// 混音前统一到同采样率、同声道数
		if (s1.WaveFormat.SampleRate != outRate) s1 = new WdlResamplingSampleProvider(s1, outRate);
		if (s2.WaveFormat.SampleRate != outRate) s2 = new WdlResamplingSampleProvider(s2, outRate);
		if (outMono) {
			s1 = tomono(s1);
			s2 = tomono(s2);
		}
		else {
			s1 = s1.ToStereo();
			s2 = s2.ToStereo();
		}
		var mixer = new MixingSampleProvider(new[] { s1, s2 });
		WaveFileWriter.CreateWaveFile16(dst, mixer);
		RecordLog.Step("mixto_done", RecordLog.FileInfo(dst));
	}

	/// <summary>重采样 + 单/立体声规范化。</summary>
	ISampleProvider tooutput(ISampleProvider samples) {
		if (samples.WaveFormat.SampleRate != outRate)
			samples = new WdlResamplingSampleProvider(samples, outRate);
		return outMono ? tomono(samples) : samples.ToStereo();
	}

	static ISampleProvider tomono(ISampleProvider s) {
		if (s.WaveFormat.Channels == 1) return s;
		return new StereoToMonoSampleProvider(s) { LeftVolume = 0.5f, RightVolume = 0.5f };
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		try { Stop(); } catch { }
	}

	public static int WavMs(string path) {
		try {
			if (string.IsNullOrEmpty(path) || !File.Exists(path)) return 0;
			if (new FileInfo(path).Length < 44) return 0;
			using var r = new WaveFileReader(path);
			var ms = (int)r.TotalTime.TotalMilliseconds;
			return ms < 0 ? 0 : ms;
		}
		catch { return 0; }
	}

	public static void WriteSilence(string path, int ms, int hz, bool mono) {
		if (string.IsNullOrEmpty(path) || ms < 20) return;
		if (hz < 8000) hz = 22050;
		var ch = mono ? 1 : 2;
		var fmt = new WaveFormat(hz, 16, ch);
		var bytes = (int)(fmt.AverageBytesPerSecond * (long)ms / 1000);
		var align = Math.Max(1, fmt.BlockAlign);
		bytes -= bytes % align;
		if (bytes <= 0) return;
		using var w = new WaveFileWriter(path, fmt);
		var buf = new byte[8192];
		while (bytes > 0) {
			var n = Math.Min(buf.Length - (buf.Length % align), bytes);
			if (n <= 0) break;
			w.Write(buf, 0, n);
			bytes -= n;
		}
	}

	public static void ConcatTo(IList<string> parts, string dst, int hz, bool mono) {
		if (parts == null || parts.Count == 0 || string.IsNullOrEmpty(dst)) return;
		if (hz < 8000) hz = 22050;
		var readers = new List<AudioFileReader>();
		try {
			var seq = new List<ISampleProvider>();
			foreach (var p in parts) {
				if (string.IsNullOrEmpty(p) || !File.Exists(p) || new FileInfo(p).Length < 100) continue;
				var r = new AudioFileReader(p);
				readers.Add(r);
				ISampleProvider s = r;
				if (s.WaveFormat.SampleRate != hz)
					s = new WdlResamplingSampleProvider(s, hz);
				s = mono
					? (s.WaveFormat.Channels == 1 ? s : new StereoToMonoSampleProvider(s) { LeftVolume = 0.5f, RightVolume = 0.5f })
					: s.ToStereo();
				seq.Add(s);
			}
			if (seq.Count == 0) return;
			WaveFileWriter.CreateWaveFile16(dst, new ConcatenatingSampleProvider(seq));
		}
		finally {
			foreach (var r in readers)
				try { r.Dispose(); } catch { }
		}
	}
}

/// <summary>可按采样位置改写的 WAV。补过的静音之后还能被真实采样盖住。</summary>
sealed class WavPlace : IDisposable {
	FileStream fs;
	long headerLen;
	long dataLen;
	bool closed;

	public long DataLength => dataLen;

	public static WavPlace Create(string path, WaveFormat fmt) {
		var dir = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		var w = new WavPlace();
		w.fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
		using (var ms = new MemoryStream())
		using (var bw = new BinaryWriter(ms)) {
			fmt.Serialize(bw);
			var fmtBytes = ms.ToArray();
			w.fs.Write(Encoding.ASCII.GetBytes("RIFF"), 0, 4);
			w.fs.Write(BitConverter.GetBytes(0), 0, 4);
			w.fs.Write(Encoding.ASCII.GetBytes("WAVE"), 0, 4);
			// Serialize 只写 fmt 块大小和内容，不含 "fmt " 标记
			w.fs.Write(Encoding.ASCII.GetBytes("fmt "), 0, 4);
			w.fs.Write(fmtBytes, 0, fmtBytes.Length);
			w.fs.Write(Encoding.ASCII.GetBytes("data"), 0, 4);
			w.fs.Write(BitConverter.GetBytes(0), 0, 4);
			w.headerLen = w.fs.Length;
		}
		w.patch();
		return w;
	}

	public void PadTo(long offset) {
		if (closed || offset <= dataLen) return;
		fs.SetLength(headerLen + offset);
		dataLen = offset;
		patch();
	}

	public void WriteAt(long offset, byte[] buf, int index, int count) {
		if (closed || buf == null || count <= 0 || offset < 0) return;
		var end = offset + count;
		if (end > dataLen) {
			fs.SetLength(headerLen + end);
			dataLen = end;
		}
		fs.Position = headerLen + offset;
		fs.Write(buf, index, count);
		patch();
	}

	public void Dispose() {
		if (closed) return;
		closed = true;
		try { patch(); } catch { }
		try { fs?.Dispose(); } catch { }
		fs = null;
	}

	void patch() {
		if (fs == null) return;
		var end = headerLen + dataLen;
		if (fs.Length < end) fs.SetLength(end);
		fs.Position = 4;
		fs.Write(BitConverter.GetBytes((int)(end - 8)), 0, 4);
		fs.Position = headerLen - 4;
		fs.Write(BitConverter.GetBytes((int)dataLen), 0, 4);
	}
}

static class SampleExt {
	public static ISampleProvider ToStereo(this ISampleProvider s) {
		if (s.WaveFormat.Channels == 2) return s;
		return new MonoToStereoSampleProvider(s);
	}
}
