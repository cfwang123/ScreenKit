namespace WpfOCR;

/// <summary>录屏音频来源（参考常见录屏软件）。</summary>
public enum RecordAudioMode {
	/// <summary>不录声音。</summary>
	Off = 0,
	/// <summary>扬声器（听到的内容 / 环回）。</summary>
	Speakers = 1,
	/// <summary>麦克风。</summary>
	Mic = 2,
	/// <summary>麦克风 + 扬声器。</summary>
	MicAndSpeakers = 3,
}
