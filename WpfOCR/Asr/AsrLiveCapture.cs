using System.Collections.Generic;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace WpfOCR;

/// <summary>ASR 录音 / 实时字幕的声音来源。</summary>
enum AsrAudioSource {
	/// <summary>麦克风。</summary>
	Mic = 0,
	/// <summary>系统扬声器环回（正在播放的声音）。</summary>
	System = 1,
	/// <summary>麦克风 + 系统声音。</summary>
	MicAndSystem = 2,
}

/// <summary>
/// WASAPI 采集：麦克风 / 系统环回 / 双路混合，输出 16k mono float。
/// 可累积缓冲（录音后识别）或仅推事件（实时字幕）。
/// </summary>
sealed class AsrLiveCapture : IDisposable {
	const int MixFlushMin = 800; // ~50ms @16k，双路凑齐再混

	readonly AsrAudioSource source;
	readonly List<float> samples = new();
	readonly List<float> pendMic = new();
	readonly List<float> pendSys = new();
	readonly object gate = new();

	WasapiCapture mic;
	WasapiLoopbackCapture loop;
	bool disposed;
	bool streamOnly;
	bool recording;
	int lastSoloTick;

	public int SampleRate { get; }
	public AsrAudioSource Source => source;
	public bool IsRecording => recording;
	public int SampleCount {
		get { lock (gate) return samples.Count; }
	}

	/// <summary>每块 16k mono float（采集线程回调）。</summary>
	public event Action<float[]> SamplesAvailable;

	public AsrLiveCapture(AsrAudioSource source = AsrAudioSource.Mic, int sampleRate = 16000) {
		this.source = source;
		SampleRate = sampleRate > 0 ? sampleRate : 16000;
	}

	public static string SourceLabel(AsrAudioSource s) => s switch {
		AsrAudioSource.System => "系统声音",
		AsrAudioSource.MicAndSystem => "麦克风+系统声音",
		_ => "麦克风",
	};

	public static AsrAudioSource ParseSource(string s) {
		if (string.IsNullOrWhiteSpace(s)) return AsrAudioSource.Mic;
		return (s.Trim().ToLowerInvariant()) switch {
			"system" or "speakers" or "loopback" or "spk" => AsrAudioSource.System,
			"micandsystem" or "micandspeakers" or "both" or "mix" => AsrAudioSource.MicAndSystem,
			_ => AsrAudioSource.Mic,
		};
	}

	/// <param name="streamOnly">true 时不累积内部缓冲（仅推事件）。</param>
	public void Start(bool streamOnly = false) {
		Stop();
		this.streamOnly = streamOnly;
		lock (gate) {
			samples.Clear();
			pendMic.Clear();
			pendSys.Clear();
		}
		lastSoloTick = Environment.TickCount;
		Exception micEx = null;
		Exception sysEx = null;

		if (source is AsrAudioSource.Mic or AsrAudioSource.MicAndSystem) {
			try {
				mic = new WasapiCapture();
				mic.DataAvailable += onmic;
				mic.RecordingStopped += (_, e) => {
					if (e.Exception != null) CaptureLog.Ex("AsrLiveCapture.mic", e.Exception);
				};
				mic.StartRecording();
				CaptureLog.Info($"AsrLiveCapture mic fmt={mic.WaveFormat}");
			}
			catch (Exception ex) {
				micEx = ex;
				try { mic?.Dispose(); } catch { }
				mic = null;
				if (source == AsrAudioSource.Mic)
					throw new InvalidOperationException("无法打开麦克风: " + ex.Message, ex);
				CaptureLog.Ex("AsrLiveCapture mic optional", ex);
			}
		}

		if (source is AsrAudioSource.System or AsrAudioSource.MicAndSystem) {
			try {
				loop = new WasapiLoopbackCapture();
				loop.DataAvailable += onsys;
				loop.RecordingStopped += (_, e) => {
					if (e.Exception != null) CaptureLog.Ex("AsrLiveCapture.loop", e.Exception);
				};
				loop.StartRecording();
				CaptureLog.Info($"AsrLiveCapture loop fmt={loop.WaveFormat}");
			}
			catch (Exception ex) {
				sysEx = ex;
				try { loop?.Dispose(); } catch { }
				loop = null;
				if (source == AsrAudioSource.System)
					throw new InvalidOperationException("无法打开系统声音环回: " + ex.Message, ex);
				CaptureLog.Ex("AsrLiveCapture loop optional", ex);
			}
		}

		if (mic == null && loop == null) {
			var msg = "无法打开任何音频设备";
			if (micEx != null) msg += "；麦克风: " + micEx.Message;
			if (sysEx != null) msg += "；系统: " + sysEx.Message;
			throw new InvalidOperationException(msg);
		}

		recording = true;
	}

	void onmic(object sender, WaveInEventArgs e) {
		if (!recording || e.BytesRecorded <= 0 || mic == null) return;
		var chunk = convert(e.Buffer, e.BytesRecorded, mic.WaveFormat);
		if (chunk.Length == 0) return;
		if (source == AsrAudioSource.Mic)
			emit(chunk);
		else
			pushmix(chunk, isMic: true);
	}

	void onsys(object sender, WaveInEventArgs e) {
		if (!recording || e.BytesRecorded <= 0 || loop == null) return;
		var chunk = convert(e.Buffer, e.BytesRecorded, loop.WaveFormat);
		if (chunk.Length == 0) return;
		if (source == AsrAudioSource.System)
			emit(chunk);
		else
			pushmix(chunk, isMic: false);
	}

	void pushmix(float[] chunk, bool isMic) {
		lock (gate) {
			if (isMic) pendMic.AddRange(chunk);
			else pendSys.AddRange(chunk);
			flushmixlocked();
		}
	}

	/// <summary>
	/// 双路：能对齐则混音；若一路长时间无数据则单路放出（系统静音时环回可能不回调）。
	/// </summary>
	void flushmixlocked() {
		var n = Math.Min(pendMic.Count, pendSys.Count);
		if (n >= MixFlushMin) {
			var mixed = new float[n];
			for (int i = 0; i < n; i++) {
				var v = pendMic[i] + pendSys[i];
				if (v > 1f) v = 1f;
				else if (v < -1f) v = -1f;
				mixed[i] = v;
			}
			pendMic.RemoveRange(0, n);
			pendSys.RemoveRange(0, n);
			lastSoloTick = Environment.TickCount;
			emitlocked(mixed);
			return;
		}

		// 单路积压过久：对方可能静音/无回调
		var now = Environment.TickCount;
		if (now - lastSoloTick < 80) return;
		var soloN = 0;
		List<float> solo = null;
		if (pendMic.Count >= MixFlushMin && pendSys.Count == 0) {
			solo = pendMic;
			soloN = Math.Min(pendMic.Count, SampleRate / 5); // 最多 200ms
		}
		else if (pendSys.Count >= MixFlushMin && pendMic.Count == 0) {
			solo = pendSys;
			soloN = Math.Min(pendSys.Count, SampleRate / 5);
		}
		else if (pendMic.Count > SampleRate / 2 && pendMic.Count > pendSys.Count * 3) {
			solo = pendMic;
			soloN = pendMic.Count - pendSys.Count;
		}
		else if (pendSys.Count > SampleRate / 2 && pendSys.Count > pendMic.Count * 3) {
			solo = pendSys;
			soloN = pendSys.Count - pendMic.Count;
		}
		if (solo == null || soloN < MixFlushMin) return;
		soloN = Math.Min(soloN, solo.Count);
		var outChunk = solo.GetRange(0, soloN).ToArray();
		solo.RemoveRange(0, soloN);
		lastSoloTick = now;
		emitlocked(outChunk);
	}

	void emit(float[] chunk) {
		lock (gate) emitlocked(chunk);
	}

	void emitlocked(float[] chunk) {
		if (chunk == null || chunk.Length == 0) return;
		if (!streamOnly) {
			for (int i = 0; i < chunk.Length; i++)
				samples.Add(chunk[i]);
		}
		try { SamplesAvailable?.Invoke(chunk); } catch { }
	}

	float[] convert(byte[] buffer, int bytes, WaveFormat fmt) {
		var mono = pcmtofloatmono(buffer, bytes, fmt);
		if (mono.Length == 0) return mono;
		if (fmt.SampleRate == SampleRate) return mono;
		return AsrAudio.Resample(mono, fmt.SampleRate, SampleRate);
	}

	/// <summary>PCM / IEEE float → mono float（源采样率）。</summary>
	static float[] pcmtofloatmono(byte[] buffer, int bytes, WaveFormat fmt) {
		if (buffer == null || bytes <= 0 || fmt == null) return Array.Empty<float>();
		var ch = Math.Max(1, fmt.Channels);
		var isFloat = fmt.Encoding == WaveFormatEncoding.IeeeFloat
			|| (fmt.BitsPerSample == 32 && fmt.Encoding == WaveFormatEncoding.Extensible);
		// WaveFormatExtensible 环回常见 IeeeFloat
		if (fmt is WaveFormatExtensible ext) {
			// SubFormat 判断：多数环回为 float
			try {
				if (ext.BitsPerSample == 32)
					isFloat = true;
			}
			catch { }
		}

		if (isFloat && fmt.BitsPerSample == 32) {
			var nSamp = bytes / 4;
			var frames = nSamp / ch;
			if (frames <= 0) return Array.Empty<float>();
			var r = new float[frames];
			for (int f = 0; f < frames; f++) {
				float s = 0;
				for (int c = 0; c < ch; c++)
					s += BitConverter.ToSingle(buffer, (f * ch + c) * 4);
				r[f] = s / ch;
			}
			return r;
		}

		if (fmt.BitsPerSample == 16) {
			var nSamp = bytes / 2;
			var frames = nSamp / ch;
			if (frames <= 0) return Array.Empty<float>();
			var r = new float[frames];
			for (int f = 0; f < frames; f++) {
				float s = 0;
				for (int c = 0; c < ch; c++) {
					var v = BitConverter.ToInt16(buffer, (f * ch + c) * 2);
					s += v / 32768f;
				}
				r[f] = s / ch;
			}
			return r;
		}

		if (fmt.BitsPerSample == 32 && !isFloat) {
			var nSamp = bytes / 4;
			var frames = nSamp / ch;
			if (frames <= 0) return Array.Empty<float>();
			var r = new float[frames];
			const float scale = 1f / 2147483648f;
			for (int f = 0; f < frames; f++) {
				float s = 0;
				for (int c = 0; c < ch; c++) {
					var v = BitConverter.ToInt32(buffer, (f * ch + c) * 4);
					s += v * scale;
				}
				r[f] = s / ch;
			}
			return r;
		}

		if (fmt.BitsPerSample == 24) {
			var bps = 3;
			var nSamp = bytes / bps;
			var frames = nSamp / ch;
			if (frames <= 0) return Array.Empty<float>();
			var r = new float[frames];
			const float scale = 1f / 8388608f;
			for (int f = 0; f < frames; f++) {
				float s = 0;
				for (int c = 0; c < ch; c++) {
					var o = (f * ch + c) * 3;
					var v = buffer[o] | (buffer[o + 1] << 8) | (buffer[o + 2] << 16);
					if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
					s += v * scale;
				}
				r[f] = s / ch;
			}
			return r;
		}

		CaptureLog.Info($"AsrLiveCapture unsupported fmt={fmt}");
		return Array.Empty<float>();
	}

	public float[] Stop() {
		recording = false;
		try { mic?.StopRecording(); } catch { }
		try { loop?.StopRecording(); } catch { }
		try { System.Threading.Thread.Sleep(80); } catch { }

		// 双路尾部：尽量混完剩余
		if (source == AsrAudioSource.MicAndSystem) {
			lock (gate) {
				var n = Math.Min(pendMic.Count, pendSys.Count);
				if (n > 0) {
					var mixed = new float[n];
					for (int i = 0; i < n; i++) {
						var v = pendMic[i] + pendSys[i];
						if (v > 1f) v = 1f;
						else if (v < -1f) v = -1f;
						mixed[i] = v;
					}
					pendMic.Clear();
					pendSys.Clear();
					if (!streamOnly)
						samples.AddRange(mixed);
				}
				else {
					// 仅剩单路
					if (!streamOnly) {
						if (pendMic.Count > 0) samples.AddRange(pendMic);
						if (pendSys.Count > 0) samples.AddRange(pendSys);
					}
					pendMic.Clear();
					pendSys.Clear();
				}
			}
		}

		try { mic?.Dispose(); } catch { }
		mic = null;
		try { loop?.Dispose(); } catch { }
		loop = null;

		lock (gate) {
			var arr = samples.ToArray();
			samples.Clear();
			return arr;
		}
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		try { Stop(); } catch { }
	}
}
