namespace WpfOCR;

enum AsrModelType {
	SenseVoice,
	Paraformer,
	Transducer,
	Whisper,
	ZipformerCtc,
}

/// <summary>Sherpa-ONNX ASR 模型目录信息（离线 / 流式）。</summary>
sealed class AsrModelInfo {
	public string DisplayName { get; set; } = "";
	public string ModelDir { get; set; } = "";
	public AsrModelType Type { get; set; } = AsrModelType.SenseVoice;
	/// <summary>true = OnlineRecognizer 流式包（不可用 OfflineRecognizer）。</summary>
	public bool IsStreaming { get; set; }
	/// <summary>主模型文件名（SenseVoice/Paraformer/流式 CTC）或说明。</summary>
	public string ModelFile { get; set; } = "";
	public string TokensFile { get; set; } = "tokens.txt";
	/// <summary>Transducer / Whisper 多文件。</summary>
	public string EncoderFile { get; set; } = "";
	public string DecoderFile { get; set; } = "";
	public string JoinerFile { get; set; } = "";
	public int SampleRate { get; set; } = 16000;
	public string TypeLabel {
		get {
			var baseLabel = Type switch {
				AsrModelType.SenseVoice => "SenseVoice",
				AsrModelType.Paraformer => "Paraformer",
				AsrModelType.Transducer => "Transducer",
				AsrModelType.Whisper => "Whisper",
				AsrModelType.ZipformerCtc => "Zipformer-CTC",
				_ => Type.ToString(),
			};
			return IsStreaming ? "流式 " + baseLabel : baseLabel;
		}
	}
	public string ListName => $"{DisplayName}  [{TypeLabel}]";
	public override string ToString() => ListName;
}
