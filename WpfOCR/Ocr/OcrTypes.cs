namespace WpfOCR;

public enum OcrDevice {
	/// <summary>NVIDIA CUDA GPU。</summary>
	Gpu,
	/// <summary>Intel 核显等：DirectML 加速。</summary>
	IntelGpu,
	Cpu,
}

public sealed class OcrLine {
	public string Text;
	public float Score;
	public Point2f[] Box; // 四点，顺时针
}

public sealed class OcrResult {
	public List<OcrLine> Lines = new();
	public string FullText => string.Join(Environment.NewLine, Lines.Select(x => x.Text));
	public string DeviceUsed;
	public string ModelLabel;
	public int LoadMs;
	public int InferMs;
}

public readonly struct Point2f {
	public readonly float X;
	public readonly float Y;
	public Point2f(float x, float y) { X = x; Y = y; }
	public override string ToString() => $"({X:F1},{Y:F1})";
}

public sealed class OcrOptions {
	/// <summary>模型包 Id（ocrmodels 子目录名，如 umi / rapid-ch）。</summary>
	public string ModelPackId = "umi";
	/// <summary>变体标题（configs.txt 第一行，如「简体中文 (det-v4)」）。</summary>
	public string ModelVariant = "";
	/// <summary>解析后的模型包路径（程序目录 ocrmodels/&lt;packId&gt;，由 ModelPackId 推导，非用户配置项）。</summary>
	public string ModelsDir;
	public OcrDevice Device = OcrDevice.Cpu;
	/// <summary>检测边长上限（Umi Rapid 默认 1024）。</summary>
	public int DetLimitSideLen = 1024;
	/// <summary>检测前白边 padding（Umi Rapid 默认 50）。</summary>
	public int DetPadding = 50;
	public float DetThresh = 0.3f;
	public float DetBoxThresh = 0.5f;
	public float DetUnclipRatio = 1.6f;
	public bool DetUseDilation = true;
	public int RecImgH = 48;
	/// <summary>识别基准宽（对应 Rapid rec_img_shape 的 320）；长行会按比例放宽。</summary>
	public int RecMaxWidth = 320;
	/// <summary>识别绝对最大宽，防止超长行爆显存。</summary>
	public int RecAbsMaxWidth = 3200;
	/// <summary>识别批大小（Rapid 默认 6）；1=逐条，结果与批处理在 pad 语义下一致。</summary>
	public int RecBatchNum = 6;
	public bool UseCls = true;
	/// <summary>全局热键，如 Ctrl+Alt+O：切换主窗显示/隐藏。空字符串 = 禁用。</summary>
	public string Hotkey = "Ctrl+Alt+O";
	/// <summary>截图标注热键，默认 Ctrl+Alt+Q。空字符串 = 禁用。</summary>
	public string HotkeySnap = "Ctrl+Alt+Q";
	/// <summary>截图识别热键，默认 Ctrl+Alt+W。空字符串 = 禁用。</summary>
	public string HotkeySnapOcr = "Ctrl+Alt+W";
	/// <summary>屏幕画板热键：全屏冻结后标注。默认空 = 禁用。</summary>
	public string HotkeyBoard = "";
	/// <summary>语音输入热键：开始/结束听写并注入焦点窗口。默认 Ctrl+Alt+V。空 = 禁用。</summary>
	public string HotkeyVoiceInput = "Ctrl+Alt+V";
	/// <summary>系统实时字幕热键：开始/结束桌面流式字幕。默认 Ctrl+Alt+B。空 = 禁用。</summary>
	public string HotkeyLiveCaption = "Ctrl+Alt+B";
	/// <summary>最小化时隐藏到通知栏。</summary>
	public bool MinimizeToTray = true;
	/// <summary>是否启用 HTTP 识图 API（Umi 风格）。</summary>
	public bool HttpEnabled = true;
	/// <summary>HTTP 监听地址，默认仅本机。</summary>
	public string HttpHost = "127.0.0.1";
	/// <summary>HTTP 端口，默认与 Umi-OCR 一致 1224。</summary>
	public int HttpPort = 1224;
	/// <summary>
	/// 服务模式：启动/改参后立即预热引擎并常驻，不主动释放模型内存。
	/// 关闭时保持懒加载（改参后丢弃，下次识别再加载）。
	/// </summary>
	public bool ServiceMode = false;
	/// <summary>PDF 导出时叠加不可见文字层（可检索/可复制）。</summary>
	public bool PdfInvisibleText = true;
	/// <summary>
	/// PDF 光栅化 DPI（仅内部识别用，非页面物理尺寸）。
	/// 导出页大小按原 PDF 页面比例还原；界面不再暴露此项。
	/// </summary>
	public int PdfDpi = 150;
	/// <summary>系统诊断日志：log/capture.log 与录屏 log/record_*.log，默认关闭。</summary>
	public bool CaptureLog = false;
	/// <summary>
	/// 截图历史（screenshots/）保留天数。默认 3；0 = 不限（不自动删除）。
	/// </summary>
	public int ScreenshotKeepDays = 3;
	/// <summary>截图保存格式：png / jpg（默认 png）。</summary>
	public string ScreenshotFormat = "png";
	/// <summary>JPG 质量 1–100（仅 format=jpg 时生效；默认 92）。</summary>
	public int ScreenshotJpgQuality = 92;
	/// <summary>是否限制截图保存最大宽高（等比缩小，不放大）。</summary>
	public bool ScreenshotMaxSizeEnabled = false;
	/// <summary>截图保存最大宽（像素）。</summary>
	public int ScreenshotMaxWidth = 1920;
	/// <summary>截图保存最大高（像素）。</summary>
	public int ScreenshotMaxHeight = 1080;
	/// <summary>截图完成时复制为图片（与 AsFile / AsPath 三选一）。</summary>
	public bool SnapCopyAsImage = true;
	/// <summary>截图完成时复制为文件 FileDrop（与 AsImage / AsPath 三选一）。</summary>
	public bool SnapCopyAsFile = false;
	/// <summary>截图完成时复制文件完整路径文本（与 AsImage / AsFile 三选一）。</summary>
	public bool SnapCopyAsPath = false;
	/// <summary>界面语言：zh / en。</summary>
	public string UiLang = "zh";
	/// <summary>是否已弹出过首次「安装功能」向导（false = 下次启动仍提示）。</summary>
	public bool InstallPromptDone;
	/// <summary>录屏编码参数。</summary>
	public RecordOptions Record = new();
	/// <summary>GIF 录屏参数（低帧率、无声）。</summary>
	public GifOptions GifRecord = new();
	/// <summary>主窗宽度（DIP）；≤0 表示用默认。</summary>
	public double WinW;
	/// <summary>主窗高度（DIP）。</summary>
	public double WinH;
	/// <summary>主窗 Left（DIP）。</summary>
	public double WinL;
	/// <summary>主窗 Top（DIP）。</summary>
	public double WinT;
	/// <summary>启动时是否最大化。</summary>
	public bool WinMax;

	// ─── TTS 上次参数（config.toml [tts]） ───
	/// <summary>Sapi / Sherpa。</summary>
	public string TtsEngine = "Sherpa";
	/// <summary>Auto / Gpu / Cpu / Igpu。</summary>
	public string TtsCompute = "Auto";
	/// <summary>模型目录名（DisplayName）。</summary>
	public string TtsModel = "";
	/// <summary>发音人 Name 或 SAPI Voice.Name。</summary>
	public string TtsVoice = "";
	/// <summary>筛选：zh / en / 空=全部。</summary>
	public string TtsLangFilter = "";
	/// <summary>筛选：male / female / 空=全部。</summary>
	public string TtsGenderFilter = "";
	/// <summary>语速 0.5～2.0。</summary>
	public double TtsRate = 1.0;
	/// <summary>SAPI 音量 0～100。</summary>
	public int TtsVolume = 100;
	/// <summary>导出 MP3 码率 kbps。</summary>
	public int TtsKbps = 192;

	// ─── ASR 上次参数（config.toml [asr]） ───
	/// <summary>离线/字幕 ASR 模型目录名。</summary>
	public string AsrModel = "";
	/// <summary>流式语音输入 ASR 模型目录名。</summary>
	public string AsrModelStream = "";
	/// <summary>Auto / Gpu / Cpu / Igpu。</summary>
	public string AsrCompute = "Auto";
	/// <summary>SenseVoice 语言：auto / zh / en / ja / ko / yue。</summary>
	public string AsrLang = "auto";
	/// <summary>SenseVoice 逆文本归一化（标点）。</summary>
	public bool AsrItn = true;
	/// <summary>录音/实时字幕声音来源：Mic / System / MicAndSystem。</summary>
	public string AsrAudioSource = "Mic";
	/// <summary>桌面实时字幕 OSD 样式。</summary>
	public AsrCaptionStyle AsrCaption = new();

	// ─── 翻译（config.toml [translate]） ───
	/// <summary>Auto / Gpu / Cpu / Igpu（Opus-MT PyTorch 管道）。</summary>
	public string TranslateCompute = "Auto";
}
