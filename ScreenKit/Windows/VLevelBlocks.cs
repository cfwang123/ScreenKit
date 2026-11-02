namespace ScreenKit;

/// <summary>竖形电平：从下往上按任意高度填充，绿→黄→红，用在选项行和录屏控制条里。</summary>
public sealed class VLevelBlocks : FrameworkElement {
	public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
		nameof(Value), typeof(double), typeof(VLevelBlocks),
		new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
	public static readonly DependencyProperty DarkProperty = DependencyProperty.Register(
		nameof(Dark), typeof(bool), typeof(VLevelBlocks),
		new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

	static readonly Brush LowBrush = frozen("#22C55E");
	static readonly Brush MidBrush = frozen("#F59E0B");
	static readonly Brush HighBrush = frozen("#EF4444");
	static readonly Brush OffBrush = frozen("#E2E8F0");
	static readonly Brush DarkOffBrush = frozen("#242A33");

	const double MID_AT = 0.55;
	const double HIGH_AT = 0.80;

	public double Value {
		get => (double)GetValue(ValueProperty);
		set => SetValue(ValueProperty, value);
	}

	/// <summary>深色底（黑底条）时未填充部分用暗灰，而不是浅灰。</summary>
	public bool Dark {
		get => (bool)GetValue(DarkProperty);
		set => SetValue(DarkProperty, value);
	}

	protected override void OnRender(DrawingContext dc) {
		double w = ActualWidth;
		double h = ActualHeight;
		if (w <= 1 || h <= 1) return;
		dc.DrawRectangle(Dark ? DarkOffBrush : OffBrush, null, new Rect(0, 0, w, h));
		double v = Value / 100.0;
		if (v <= 0) return;
		if (v > 1) v = 1;
		// 每段都只是一块任意高度的矩形，颜色按音量在整条高度上的位置固定。
		double greenTop = h * (1 - Math.Min(v, MID_AT));
		dc.DrawRectangle(LowBrush, null, new Rect(0, greenTop, w, h - greenTop));
		if (v > MID_AT) {
			double midBottom = h * (1 - MID_AT);
			double midTop = h * (1 - Math.Min(v, HIGH_AT));
			if (midBottom - midTop > 0.4)
				dc.DrawRectangle(MidBrush, null, new Rect(0, midTop, w, midBottom - midTop));
		}
		if (v > HIGH_AT) {
			double highBottom = h * (1 - HIGH_AT);
			double highTop = h * (1 - v);
			if (highBottom - highTop > 0.4)
				dc.DrawRectangle(HighBrush, null, new Rect(0, highTop, w, highBottom - highTop));
		}
	}

	static Brush frozen(string color) {
		var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
		b.Freeze();
		return b;
	}
}
