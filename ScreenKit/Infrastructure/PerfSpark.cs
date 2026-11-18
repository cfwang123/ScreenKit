using System.Windows.Media;

namespace ScreenKit;

/// <summary>性能小窗每行前面的迷你折线。横轴固定两分钟，只画有数据的部分。</summary>
sealed class PerfSpark : FrameworkElement {
	public static readonly Brush GridBrush = freeze("#3E4A52");

	double[] values;
	double[] ages;
	double spanMs = 120000;
	Brush stroke = GridBrush;
	double minScale;
	const double PEN = 1.1;

	public Brush Stroke {
		get => stroke;
		set {
			if (ReferenceEquals(stroke, value) || value == null) return;
			stroke = value;
			InvalidateVisual();
		}
	}

	public static Brush freeze(string hex) {
		var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
		b.Freeze();
		return b;
	}

	/// <param name="ages">每点距今毫秒数，越大越靠左。</param>
	public void Push(double[] data, double[] agesMs, double min) {
		values = data;
		ages = agesMs;
		minScale = min;
		InvalidateVisual();
	}

	protected override void OnRender(DrawingContext dc) {
		var w = ActualWidth;
		var h = ActualHeight;
		if (w < 4 || h < 4) return;
		var data = values;
		if (data == null || data.Length == 0) return;
		var max = minScale;
		foreach (var v in data)
			if (v > max) max = v;
		if (max <= 0) max = 1;
		var pen = new Pen(stroke, PEN) { LineJoin = PenLineJoin.Round };
		if (data.Length == 1) {
			if (double.IsNaN(data[0])) return;
			var v0 = Math.Min(Math.Max(data[0], 0), max);
			var age0 = ages != null && ages.Length > 0 ? ages[0] : 0;
			var x0 = Math.Min(Math.Max(w - PEN / 2 - age0 / spanMs * (w - PEN), PEN), w - PEN / 2);
			var y0 = h - PEN / 2 - v0 / max * (h - PEN);
			dc.DrawEllipse(stroke, null, new Point(x0, y0), PEN, PEN);
			return;
		}
		var geo = new StreamGeometry();
		var open = false;
		using (var ctx = geo.Open()) {
			for (var i = 0; i < data.Length; i++) {
				var v = data[i];
				if (double.IsNaN(v)) {
					open = false;
					continue;
				}
				var age = ages != null && i < ages.Length ? ages[i] : 0;
				var x = w - PEN / 2 - age / spanMs * (w - PEN);
				if (x < 0) x = 0;
				if (x > w - PEN / 2) x = w - PEN / 2;
				if (v < 0) v = 0;
				if (v > max) v = max;
				var y = h - PEN / 2 - v / max * (h - PEN);
				if (!open) {
					ctx.BeginFigure(new Point(x, y), false, false);
					open = true;
				} else ctx.LineTo(new Point(x, y), true, false);
			}
		}
		geo.Freeze();
		dc.DrawGeometry(null, pen, geo);
	}
}
