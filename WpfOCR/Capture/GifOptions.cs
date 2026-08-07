namespace WpfOCR;

/// <summary>GIF 录屏参数（可持久化到 config.toml [gif_record]）。无声音。</summary>
public sealed class GifOptions {
	/// <summary>采集帧率（录屏固定 24fps，写入临时 MP4）。</summary>
	public const int CaptureFps = 24;

	/// <summary>输出帧率 1–24，默认 8（预览窗可选；从 24fps 源抽帧）。</summary>
	public int Fps = 8;
	/// <summary>是否启用最大宽高限制（fit 缩放）。录制前选项；预览窗可再调缩放。</summary>
	public bool MaxSizeEnabled = true;
	/// <summary>输出最大宽。</summary>
	public int MaxWidth = 1280;
	/// <summary>输出最大高。</summary>
	public int MaxHeight = 720;
	/// <summary>调色板颜色数 32–256，默认 128（越小体积越小）。</summary>
	public int Colors = 128;
	/// <summary>预览默认缩放百分比 25–100。</summary>
	public int ScalePercent = 100;

	public GifOptions Clone() => new() {
		Fps = Fps,
		MaxSizeEnabled = MaxSizeEnabled,
		MaxWidth = MaxWidth,
		MaxHeight = MaxHeight,
		Colors = Colors,
		ScalePercent = ScalePercent,
	};

	/// <summary>将另一实例字段复制到当前对象。</summary>
	public void CopyFrom(GifOptions o) {
		if (o == null) return;
		Fps = o.Fps;
		MaxSizeEnabled = o.MaxSizeEnabled;
		MaxWidth = o.MaxWidth;
		MaxHeight = o.MaxHeight;
		Colors = o.Colors;
		ScalePercent = o.ScalePercent;
		Clamp();
	}

	public void Clamp() {
		Fps = Compat.Clamp(Fps, 1, CaptureFps);
		MaxWidth = Math.Max(16, MaxWidth);
		MaxHeight = Math.Max(16, MaxHeight);
		Colors = snapcolors(Colors);
		ScalePercent = Compat.Clamp(ScalePercent, 25, 100);
	}

	static int snapcolors(int n) {
		n = Compat.Clamp(n, 32, 256);
		if (n <= 48) return 32;
		if (n <= 96) return 64;
		if (n <= 192) return 128;
		return 256;
	}

	/// <summary>浮动条/状态用的参数摘要。</summary>
	public string SummaryText(int captureW = 0, int captureH = 0) {
		Clamp();
		FitSize(captureW > 0 ? captureW : 1280, captureH > 0 ? captureH : 720, out var ow, out var oh);
		var sizePart = MaxSizeEnabled
			? (captureW > 0 ? $"out {ow}×{oh}" : $"max {MaxWidth}×{MaxHeight}")
			: "full";
		return $"GIF · 采{CaptureFps}→出{Fps}fps · {Colors}色 · 无声 · {sizePart}";
	}

	/// <summary>将采集宽高 fit 到最大框内（保持比例）。</summary>
	public void FitSize(int srcW, int srcH, out int outW, out int outH) {
		srcW = Math.Max(2, srcW);
		srcH = Math.Max(2, srcH);
		if (!MaxSizeEnabled || MaxWidth < 16 || MaxHeight < 16) {
			outW = srcW;
			outH = srcH;
			return;
		}
		var sx = (double)MaxWidth / srcW;
		var sy = (double)MaxHeight / srcH;
		var s = Math.Min(1.0, Math.Min(sx, sy));
		outW = Math.Max(16, (int)Math.Round(srcW * s));
		outH = Math.Max(16, (int)Math.Round(srcH * s));
	}

	/// <summary>按缩放百分比计算输出尺寸（至少 16）。</summary>
	public static void SizeByScale(int srcW, int srcH, int scalePercent, out int outW, out int outH) {
		srcW = Math.Max(2, srcW);
		srcH = Math.Max(2, srcH);
		var s = Compat.Clamp(scalePercent, 25, 100) / 100.0;
		outW = Math.Max(16, (int)Math.Round(srcW * s));
		outH = Math.Max(16, (int)Math.Round(srcH * s));
	}
}
