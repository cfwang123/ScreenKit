using System.IO;
using NAudio.Wave;

namespace ScreenKit;

/// <summary>ASR 音频：读文件 / 麦克风 / 重采样到 16k mono float。</summary>
static class AsrAudio {
	static readonly HashSet<string> VideoExt = new(StringComparer.OrdinalIgnoreCase) {
		".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v",
		".ts", ".mts", ".m2ts", ".mpeg", ".mpg", ".3gp", ".asf", ".rmvb", ".rm",
	};
	static readonly HashSet<string> AudioExt = new(StringComparer.OrdinalIgnoreCase) {
		".wav", ".mp3", ".flac", ".m4a", ".ogg", ".opus", ".wma", ".aac", ".aiff", ".aif",
	};

	public static bool IsVideoPath(string path) {
		if (string.IsNullOrWhiteSpace(path)) return false;
		return VideoExt.Contains(Path.GetExtension(path));
	}

	public static bool IsAudioPath(string path) {
		if (string.IsNullOrWhiteSpace(path)) return false;
		return AudioExt.Contains(Path.GetExtension(path));
	}

	public static bool IsMediaPath(string path) => IsVideoPath(path) || IsAudioPath(path);

	/// <summary>
	/// 打开音频或视频：常见音频优先 NAudio；视频 / 失败时走 FFmpeg DLL 抽 mono PCM。
	/// </summary>
	public static (float[] samples, int sampleRate) LoadMedia(string path, int preferSampleRate = 16000) {
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			throw new FileNotFoundException("音视频文件不存在", path);
		var targetSr = preferSampleRate > 0 ? preferSampleRate : 16000;
		if (IsVideoPath(path))
			return loadViaFfmpegDll(path, targetSr);
		try {
			return LoadFile(path);
		}
		catch (Exception ex) {
			CaptureLog.Info("AsrAudio.LoadFile fail, try FFmpeg DLL: " + ex.Message);
			return loadViaFfmpegDll(path, targetSr);
		}
	}

	/// <summary>从 wav/mp3/flac 等读为 mono float + 采样率。</summary>
	public static (float[] samples, int sampleRate) LoadFile(string path) {
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			throw new FileNotFoundException("音频文件不存在", path);
		using var reader = new AudioFileReader(path);
		var sr = reader.WaveFormat.SampleRate;
		var ch = reader.WaveFormat.Channels;
		var list = new List<float>();
		var buf = new float[4096 * Math.Max(1, ch)];
		int n;
		while ((n = reader.Read(buf, 0, buf.Length)) > 0) {
			if (ch <= 1) {
				for (int i = 0; i < n; i++) list.Add(buf[i]);
			}
			else {
				var frames = n / ch;
				for (int f = 0; f < frames; f++) {
					float s = 0;
					for (int c = 0; c < ch; c++) s += buf[f * ch + c];
					list.Add(s / ch);
				}
			}
		}
		return (list.ToArray(), sr);
	}

	static (float[] samples, int sampleRate) loadViaFfmpegDll(string path, int sampleRate) {
		return FfmpegAudioDecode.DecodeMono(path, sampleRate);
	}

	/// <summary>线性重采样（足够 ASR 用）。</summary>
	public static float[] Resample(float[] samples, int fromRate, int toRate) {
		if (samples == null || samples.Length == 0 || fromRate == toRate || fromRate <= 0 || toRate <= 0)
			return samples ?? Array.Empty<float>();
		var ratio = (double)toRate / fromRate;
		var outLen = Math.Max(1, (int)(samples.Length * ratio));
		var result = new float[outLen];
		for (int i = 0; i < outLen; i++) {
			var src = i / ratio;
			var i0 = (int)src;
			var frac = src - i0;
			var a = samples[Compat.Clamp(i0, 0, samples.Length - 1)];
			var b = samples[Compat.Clamp(i0 + 1, 0, samples.Length - 1)];
			result[i] = (float)(a + (b - a) * frac);
		}
		return result;
	}
}

/// <summary>麦克风录音（16k mono），停止后得到 float 波形；可订阅 <see cref="SamplesAvailable"/> 流式取块。</summary>
sealed class AsrMicRecorder : IDisposable {
	WaveInEvent waveIn;
	readonly List<float> samples = new();
	readonly object gate = new();
	bool disposed;
	bool streamOnly;

	public int SampleRate { get; }
	public bool IsRecording => waveIn != null;
	public int SampleCount {
		get { lock (gate) return samples.Count; }
	}

	/// <summary>每块 PCM 转 float 后回调（录音线程）。流式听写可只订阅此事件。</summary>
	public event Action<float[]> SamplesAvailable;

	public AsrMicRecorder(int sampleRate = 16000) {
		SampleRate = sampleRate > 0 ? sampleRate : 16000;
	}

	/// <param name="streamOnly">true 时不累积内部缓冲（仅推事件），适合长时间听写。</param>
	public void Start(bool streamOnly = false) {
		Stop();
		this.streamOnly = streamOnly;
		lock (gate) samples.Clear();
		waveIn = new WaveInEvent {
			WaveFormat = new WaveFormat(SampleRate, 16, 1),
			BufferMilliseconds = 50,
		};
		waveIn.DataAvailable += ondata;
		waveIn.StartRecording();
	}

	void ondata(object sender, WaveInEventArgs e) {
		if (e.BytesRecorded <= 0) return;
		var n = e.BytesRecorded / 2;
		var chunk = new float[n];
		for (int i = 0; i < n; i++) {
			var s = BitConverter.ToInt16(e.Buffer, i * 2);
			chunk[i] = s / 32768f;
		}
		if (!streamOnly) {
			lock (gate) {
				for (int i = 0; i < n; i++)
					samples.Add(chunk[i]);
			}
		}
		try { SamplesAvailable?.Invoke(chunk); } catch { }
	}

	public float[] Stop() {
		try { waveIn?.StopRecording(); } catch { }
		try { waveIn?.Dispose(); } catch { }
		waveIn = null;
		lock (gate) {
			var arr = samples.ToArray();
			samples.Clear();
			return arr;
		}
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		Stop();
	}
}
