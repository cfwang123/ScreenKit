namespace ScreenKit;

/// <summary>录屏编码参数（可持久化到 config.toml）。</summary>
public sealed class RecordOptions {
	/// <summary>x264 / x265 / av1。</summary>
	public string Codec = "x264";
	/// <summary>帧率 5–60。</summary>
	public int Fps = 24;
	/// <summary>x264/x265 CRF 质量 0–51，越大体积越小。</summary>
	public int Crf = 28;
	/// <summary>AV1 专用 CRF 0–63（与 x264/x265 刻度不同，默认 56 约 x265 CRF28 一半体积）。</summary>
	public int Av1Crf = 56;
	/// <summary>是否录制声音。</summary>
	public bool AudioEnabled = true;
	/// <summary>音频来源：Speakers / Mic / MicAndSpeakers。</summary>
	public string AudioSource = "Speakers";
	/// <summary>音频码率 kbps，8–128。</summary>
	public int AudioKbps = 96;
	/// <summary>音频采样率 Hz（规范化 WAV / 混音输出）。</summary>
	public int AudioHz = 22050;
	/// <summary>单声道（低码率更清晰；默认立体声）。</summary>
	public bool AudioMono = false;
	/// <summary>是否启用最大宽高限制（fit 缩放）。</summary>
	public bool MaxSizeEnabled = false;
	/// <summary>输出最大宽（偶数）。</summary>
	public int MaxWidth = 1920;
	/// <summary>输出最大高（偶数）。</summary>
	public int MaxHeight = 1080;
	/// <summary>录制开始后，HUD 缩放选区时是否锁定宽高比（开始前始终自由缩放）。</summary>
	public bool LockAspectWhileRecording = true;

	public bool IsHevc => IsHevcName(Codec);

	public bool IsAv1 => IsAv1Name(Codec);

	public static bool IsHevcName(string codec) =>
		string.Equals(codec, "x265", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(codec, "hevc", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(codec, "h265", StringComparison.OrdinalIgnoreCase);

	public static bool IsAv1Name(string codec) =>
		string.Equals(codec, "av1", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(codec, "av01", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(codec, "aom", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(codec, "libaom-av1", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(codec, "libsvtav1", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(codec, "librav1e", StringComparison.OrdinalIgnoreCase);

	/// <summary>规范化为 x264 / x265 / av1，未知值回落 x264。</summary>
	public static string NormalizeCodec(string codec) {
		if (IsAv1Name(codec)) return "av1";
		if (IsHevcName(codec)) return "x265";
		return "x264";
	}

	public RecordAudioMode AudioMode {
		get {
			if (!AudioEnabled) return RecordAudioMode.Off;
			return (AudioSource ?? "").Trim() switch {
				"Mic" => RecordAudioMode.Mic,
				"MicAndSpeakers" => RecordAudioMode.MicAndSpeakers,
				_ => RecordAudioMode.Speakers,
			};
		}
	}

	public RecordOptions Clone() => new() {
		Codec = Codec,
		Fps = Fps,
		Crf = Crf,
		Av1Crf = Av1Crf,
		AudioEnabled = AudioEnabled,
		AudioSource = AudioSource,
		AudioKbps = AudioKbps,
		AudioHz = AudioHz,
		AudioMono = AudioMono,
		MaxSizeEnabled = MaxSizeEnabled,
		MaxWidth = MaxWidth,
		MaxHeight = MaxHeight,
		LockAspectWhileRecording = LockAspectWhileRecording,
	};

	/// <summary>将另一实例字段复制到当前对象（HUD 等持有只读引用时使用）。</summary>
	public void CopyFrom(RecordOptions o) {
		if (o == null) return;
		Codec = o.Codec;
		Fps = o.Fps;
		Crf = o.Crf;
		Av1Crf = o.Av1Crf;
		AudioEnabled = o.AudioEnabled;
		AudioSource = o.AudioSource;
		AudioKbps = o.AudioKbps;
		AudioHz = o.AudioHz;
		AudioMono = o.AudioMono;
		MaxSizeEnabled = o.MaxSizeEnabled;
		MaxWidth = o.MaxWidth;
		MaxHeight = o.MaxHeight;
		LockAspectWhileRecording = o.LockAspectWhileRecording;
		Clamp();
	}

	/// <summary>当前编码实际使用的 CRF（AV1 用 Av1Crf，否则 Crf）。</summary>
	public int EffectiveCrf => IsAv1 ? Av1Crf : Crf;

	/// <summary>摘要用质量标签，如 CRF28 / AV1-CRF56。</summary>
	public string CrfLabel => IsAv1 ? $"AV1-CRF{Av1Crf}" : $"CRF{Crf}";

	/// <summary>常用合法采样率（规范化输出）。</summary>
	public static readonly int[] AudioHzChoices = { 8000, 11025, 16000, 22050, 32000, 44100, 48000 };

	public void Clamp() {
		Codec = NormalizeCodec(Codec);
		Fps = Compat.Clamp(Fps, 5, 60);
		Crf = Compat.Clamp(Crf, 0, 51);
		Av1Crf = Compat.Clamp(Av1Crf, 0, 63);
		AudioKbps = Compat.Clamp(AudioKbps, 8, 128);
		AudioHz = snapaudiohz(AudioHz);
		MaxWidth = Math.Max(16, MaxWidth / 2 * 2);
		MaxHeight = Math.Max(16, MaxHeight / 2 * 2);
		if (string.IsNullOrWhiteSpace(AudioSource)
			|| (AudioSource != "Speakers" && AudioSource != "Mic" && AudioSource != "MicAndSpeakers"))
			AudioSource = "Speakers";
	}

	static int snapaudiohz(int hz) {
		if (hz <= 0) return 22050;
		// 精确命中常用值
		foreach (var c in AudioHzChoices)
			if (c == hz) return c;
		// 夹到范围后取最近常用值
		hz = Compat.Clamp(hz, AudioHzChoices[0], AudioHzChoices[AudioHzChoices.Length - 1]);
		var best = AudioHzChoices[0];
		var bestD = Math.Abs(hz - best);
		foreach (var c in AudioHzChoices) {
			var d = Math.Abs(hz - c);
			if (d < bestD) { best = c; bestD = d; }
		}
		return best;
	}

	/// <summary>浮动条/状态用的参数摘要。</summary>
	public string SummaryText(int captureW = 0, int captureH = 0) {
		Clamp();
		FitSize(captureW > 0 ? captureW : 1920, captureH > 0 ? captureH : 1080, out var ow, out var oh);
		var sizePart = MaxSizeEnabled
			? (captureW > 0 ? $"out {ow}×{oh}" : $"max {MaxWidth}×{MaxHeight}")
			: "full";
		var ch = AudioMono ? "mono" : "stereo";
		var aud = !AudioEnabled ? "无声"
			: AudioSource switch {
				"Mic" => $"麦 {AudioKbps}k/{AudioHz}Hz/{ch}",
				"MicAndSpeakers" => $"麦+扬 {AudioKbps}k/{AudioHz}Hz/{ch}",
				_ => $"扬声器 {AudioKbps}k/{AudioHz}Hz/{ch}",
			};
		return $"{Codec} · {Fps}fps · {CrfLabel} · {sizePart} · {aud}";
	}

	/// <summary>将采集宽高 fit 到最大框内（保持比例，偶数）。</summary>
	public void FitSize(int srcW, int srcH, out int outW, out int outH) {
		srcW = Math.Max(2, srcW / 2 * 2);
		srcH = Math.Max(2, srcH / 2 * 2);
		if (!MaxSizeEnabled || MaxWidth < 16 || MaxHeight < 16) {
			outW = srcW;
			outH = srcH;
			return;
		}
		var sx = (double)MaxWidth / srcW;
		var sy = (double)MaxHeight / srcH;
		var s = Math.Min(1.0, Math.Min(sx, sy));
		outW = Math.Max(16, (int)Math.Round(srcW * s) / 2 * 2);
		outH = Math.Max(16, (int)Math.Round(srcH * s) / 2 * 2);
	}
}
