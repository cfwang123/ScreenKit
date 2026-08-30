namespace ScreenKit;

/// <summary>桌面实时字幕 OSD 样式（仿 MusicPlayer 桌面歌词）。</summary>
public sealed class AsrCaptionStyle {
	public double X = double.NaN;
	public double Y = double.NaN;
	public string FontFamily = "Microsoft YaHei UI";
	public double FontSize = 28;
	public string Foreground = "#FFFFFFFF";
	public string Outline = "#CC000000";
	public string Background = "#66000000";
	public string BorderColor = "#00000000";
	public double BorderThickness;
	/// <summary>0=左 1=中 2=右</summary>
	public int Align = 1;
	public double Width = 720;
	public double Height = 180;
	public double MaxWidth = 900;
	public bool AutoWidth = false;
	public bool AutoHeight = false;

	public AsrCaptionStyle Clone() => new() {
		X = X,
		Y = Y,
		FontFamily = FontFamily,
		FontSize = FontSize,
		Foreground = Foreground,
		Outline = Outline,
		Background = Background,
		BorderColor = BorderColor,
		BorderThickness = BorderThickness,
		Align = Align,
		Width = Width,
		Height = Height,
		MaxWidth = MaxWidth,
		AutoWidth = AutoWidth,
		AutoHeight = AutoHeight,
	};

	public void CopyFrom(AsrCaptionStyle s) {
		if (s == null) return;
		X = s.X;
		Y = s.Y;
		FontFamily = s.FontFamily;
		FontSize = s.FontSize;
		Foreground = s.Foreground;
		Outline = s.Outline;
		Background = s.Background;
		BorderColor = s.BorderColor;
		BorderThickness = s.BorderThickness;
		Align = s.Align;
		Width = s.Width;
		Height = s.Height;
		MaxWidth = s.MaxWidth;
		AutoWidth = s.AutoWidth;
		AutoHeight = s.AutoHeight;
	}
}
