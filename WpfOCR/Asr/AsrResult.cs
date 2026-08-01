namespace WpfOCR;

/// <summary>ASR 详细结果（文本 + token 级时间戳）。</summary>
sealed class AsrResult {
	public static readonly AsrResult Empty = new() {
		Text = "",
		Tokens = Array.Empty<string>(),
		Timestamps = Array.Empty<float>(),
		Durations = Array.Empty<float>(),
	};

	public string Text;
	public string[] Tokens;
	public float[] Timestamps;
	public float[] Durations;

	public bool HasTokenTimestamps =>
		Tokens != null && Timestamps != null
		&& Tokens.Length > 0 && Timestamps.Length == Tokens.Length;
}
