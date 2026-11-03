namespace ScreenKit.DocView;

/// <summary>
/// 移植层读的阅读器选项。原 DocviewWPF 存 %LocalAppData%\DocviewWPF\settings.json，
/// 这里直接由宿主窗口传入，不另建配置文件。
/// </summary>
sealed class DocViewOptions {
	/// <summary>界面字号（DIP）。</summary>
	public double UiFontSize = 12;

	/// <summary>Markdown 缩进列宽（字符）。</summary>
	public int MdTabSize = 3;

	/// <summary>Markdown 标题自动编号。文档阅读器默认不加编号，跟原来的阅读器一致。</summary>
	public bool MdHeadingAutoNumber;

	/// <summary>正文基准字号（DIP）。</summary>
	public double BodyFontSize = 15;

	public static DocViewOptions Current = new();
}
