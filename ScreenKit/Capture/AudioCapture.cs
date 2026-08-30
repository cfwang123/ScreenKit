using System.IO;
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
	WaveFileWriter writerLoop;
	WaveFileWriter writerMic;
	WaveFormat fmtLoop;
	WaveFormat fmtMic;
	long bytesLoop;
	long bytesMic;
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

	public string WavPath => wavPath;
	public int OutRate => outRate;
	public bool OutMono => outMono;
	public long BytesLoop => bytesLoop;
	public long BytesMic => bytesMic;
	/// <summary>累计补入的静音字节（环回静音缺口）。</summary>
	public long PadBytesTotal => padBytesTotal;
	/// <summary>
	/// 为 true 时单路（扬声器/麦克风）停录不二次重采样规范化。
	/// 长录屏可显著缩短结束时间；采样率/声道由后续合成阶段处理。
	/// 麦+扬混音仍会规范化。
	/// </summary>
	public bool SkipNormalize { get; set; }
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
				$"bps={fmtLoop.AverageBytesPerSecond} path={pathLoop}");
			writerLoop = new WaveFileWriter(pathLoop, fmtLoop);
			loop.DataAvailable += (_, e) => ondata(ref bytesLoop, writerLoop, fmtLoop, e, "loop");
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
					$"fmt={fmtMic} rate={fmtMic.SampleRate} ch={fmtMic.Channels} path={pathMic}");
				writerMic = new WaveFileWriter(pathMic, fmtMic);
				mic.DataAvailable += (_, e) => ondata(ref bytesMic, writerMic, fmtMic, e, "mic");
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
		if (writerLoop != null && fmtLoop != null)
			padto(ref bytesLoop, writerLoop, fmtLoop, expectedbytes(fmtLoop, ms));
		if (writerMic != null && fmtMic != null)
			padto(ref bytesMic, writerMic, fmtMic, expectedbytes(fmtMic, ms));
	}

	void ondata(ref long written, WaveFileWriter w, WaveFormat fmt, WaveInEventArgs e, string tag) {
		if (stop || paused || w == null || e.BytesRecorded <= 0) return;
		lock (gate) {
			try {
				if (firstDataTick == 0) {
					firstDataTick = Compat.TickCount64;
					RecordLog.Step("audio_first_data",
						$"{tag} ms={FirstDataMs} bytes={e.BytesRecorded} fmt={fmt}");
				}
				dataCallbacks++;
				// 先补静音缺口，再把本包接到时间轴末尾。
				// 注意：若设备时钟略快于墙钟导致 written 超前，不裁剪样本；静音段墙钟会追上。
				var want = expectedbytes(fmt, effectivems());
				var before = want - e.BytesRecorded;
				if (before < written) before = written;
				padto(ref written, w, fmt, before);
				w.Write(e.Buffer, 0, e.BytesRecorded);
				written += e.BytesRecorded;
				// 约 30s 一次音频进度（不每包刷）
				var now = Compat.TickCount64;
				if (now - lastBeatTick >= 30_000) {
					lastBeatTick = now;
					var writtenMs = fmt.AverageBytesPerSecond > 0
						? written * 1000 / fmt.AverageBytesPerSecond : 0;
					RecordLog.Step("audio_beat",
						$"{tag} wallMs={effectivems()} writtenMs={writtenMs} written={written} " +
						$"callbacks={dataCallbacks} padTotal={padBytesTotal} " +
						$"loop={bytesLoop} mic={bytesMic}");
				}
			}
			catch (Exception ex) {
				RecordLog.Ex("audio.ondata." + tag, ex);
			}
		}
	}

	void padto(ref long written, WaveFileWriter w, WaveFormat fmt, long target) {
		if (w == null || fmt == null) return;
		var align = Math.Max(1, fmt.BlockAlign);
		target -= target % align;
		var gap = target - written;
		if (gap < align) return;
		padBytesTotal += gap;
		writesilence(ref written, w, gap);
	}

	static void writesilence(ref long written, WaveFileWriter w, long bytes) {
		if (bytes <= 0) return;
		var buf = new byte[SilenceChunk];
		while (bytes > 0) {
			var n = (int)Math.Min(bytes, SilenceChunk);
			w.Write(buf, 0, n);
			written += n;
			bytes -= n;
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
			$"loop={bytesLoop} mic={bytesMic} padTotal={padBytesTotal}");
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
					$"ms={ms} loopBytes={bytesLoop} micBytes={bytesMic} padTotal={padBytesTotal}");
			}
			catch (Exception ex) {
				CaptureLog.Ex("Audio pad stop", ex);
				RecordLog.Ex("Audio pad stop", ex);
			}
			try { writerLoop?.Flush(); writerLoop?.Dispose(); } catch (Exception ex) { RecordLog.Ex("writerLoop.Dispose", ex); }
			writerLoop = null;
			try { writerMic?.Flush(); writerMic?.Dispose(); } catch (Exception ex) { RecordLog.Ex("writerMic.Dispose", ex); }
			writerMic = null;
		}
		try { loop?.Dispose(); } catch { }
		loop = null;
		try { mic?.Dispose(); } catch { }
		mic = null;

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
}

static class SampleExt {
	public static ISampleProvider ToStereo(this ISampleProvider s) {
		if (s.WaveFormat.Channels == 2) return s;
		return new MonoToStereoSampleProvider(s);
	}
}
