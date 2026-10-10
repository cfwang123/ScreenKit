namespace ScreenKit;

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

	/// <summary>共享 Sherpa TTS（可 null）。SAPI / WinRT 由 HTTP 服务自行实例化。</summary>
	public TtsEngine TtsEngine { get; set; }
	public object TtsGate { get; set; } = new();

	public Func<List<AsrModelInfo>> ScanAsr { get; set; }
	public Func<List<TtsModelInfo>> ScanTts { get; set; }

	/// <summary>桌面语音合成页已经加载的模型和发音人。HTTP 线程只读，不在请求里再扫描。</summary>
	public Func<TtsUiCatalog> TtsCatalog { get; set; }
}

/// <summary>语音合成页当前的模型与发音人。发布后里面的列表不再改写。</summary>
sealed class TtsUiCatalog {
	public List<TtsModelInfo> Sherpa { get; set; }
	public List<SapiVoiceItem> Sapi { get; set; }
	public List<SapiVoiceItem> WinRt { get; set; }
	public List<SapiVoiceItem> Edge { get; set; }
}
