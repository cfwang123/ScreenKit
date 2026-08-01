namespace WpfOCR;

/// <summary>
/// HTTP 服务可调用的引擎与配置（由 MainWindow 注入；后台线程勿碰 UI）。
/// </summary>
sealed class HttpApiServices {
	/// <summary>当前 OCR/ASR/TTS 相关配置快照。</summary>
	public Func<OcrOptions> GetOpts { get; set; }

	public OcrRunner OcrRunner { get; set; }

	/// <summary>共享 ASR 引擎（可 null）。</summary>
	public AsrEngine AsrEngine { get; set; }
	public object AsrGate { get; set; } = new();

	/// <summary>共享 Sherpa TTS（可 null）。</summary>
	public TtsEngine TtsEngine { get; set; }
	public object TtsGate { get; set; } = new();

	public Func<List<AsrModelInfo>> ScanAsr { get; set; }
	public Func<List<TtsModelInfo>> ScanTts { get; set; }
}
