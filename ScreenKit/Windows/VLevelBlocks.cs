namespace ScreenKit;

/// <summary>竖形分段电平块：从下往上点亮，用来嵌在选项行里显示麦克风/系统声音。</summary>
public sealed class VLevelBlocks : FrameworkElement {
	public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
		nameof(Value), typeof(double), typeof(VLevelBlocks),
		new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	static readonly Brush LowBrush = frozen("#22C55E");
	static readonly Brush MidBrush = frozen("#F59E0B");
	static readonly Brush HighBrush = frozen("#EF4444");
	static readonly Brush OffBrush = frozen("#E2E8F0");

	const int BLOCKS = 8;
	const double GAP = 1.5;

	public double Value {
		get => (double)GetValue(ValueProperty);
		set => SetValue(ValueProperty, value);
	}

	protected override void OnRender(DrawingContext dc) {
		double w = ActualWidth;
		double h = ActualHeight;
		if (w <= 1 || h <= 1) return;
		double bh = (h - GAP * (BLOCKS - 1)) / BLOCKS;
		if (bh <= 0.5) return;
		double v = Value / 100.0;
		if (v < 0) v = 0;
		if (v > 1) v = 1;
		int lit = (int)Math.Ceiling(v * BLOCKS);
		double rr = Math.Min(1.2, Math.Min(w, bh) / 2);
		for (int i = 0; i < BLOCKS; i++) {
			double y = h - (i + 1) * bh - i * GAP;
			var r = new Rect(0, y, w, bh);
			dc.DrawRoundedRectangle(i < lit ? onbrush(i) : OffBrush, null, r, rr, rr);
		}
	}

	static Brush onbrush(int i) {
		double t = (double)i / (BLOCKS - 1);
		if (t < 0.55) return LowBrush;
		if (t < 0.8) return MidBrush;
		return HighBrush;
	}

	static Brush frozen(string color) {
		var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
		b.Freeze();
		return b;
	}
}
