namespace ScreenKit;

/// <summary>分段电平块：按音量逐格点亮，低段绿、中段黄、高段红。</summary>
public sealed class LevelBlocks : FrameworkElement {
	public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
		nameof(Value), typeof(double), typeof(LevelBlocks),
		new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	static readonly Brush LowBrush = frozen("#22C55E");
	static readonly Brush MidBrush = frozen("#F59E0B");
	static readonly Brush HighBrush = frozen("#EF4444");
	static readonly Brush OffBrush = frozen("#E2E8F0");

	const int BLOCKS = 32;

	public double Value {
		get => (double)GetValue(ValueProperty);
		set => SetValue(ValueProperty, value);
	}

	protected override void OnRender(DrawingContext dc) {
		double w = ActualWidth;
		double h = ActualHeight;
		if (w <= 1 || h <= 1) return;
		double gap = Math.Max(1.5, w / 320);
		double bw = (w - gap * (BLOCKS - 1)) / BLOCKS;
		if (bw <= 0.5) return;
		double v = Value / 100.0;
		if (v < 0) v = 0;
		if (v > 1) v = 1;
		int lit = (int)Math.Ceiling(v * BLOCKS);
		double rr = Math.Min(2.5, Math.Min(bw, h) / 2);
		for (int i = 0; i < BLOCKS; i++) {
			var r = new Rect(i * (bw + gap), 0, bw, h);
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
