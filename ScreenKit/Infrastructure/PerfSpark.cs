using System.Windows.Media;

namespace ScreenKit;

/// <summary>性能小窗每行前面的迷你折线。刻度取本段峰值，至少 minScale，免得小值贴底看不出波动。</summary>
sealed class PerfSpark : FrameworkElement {
	public static readonly Brush GridBrush = freeze("#3E4A52");

	double[] values;
	Brush stroke = GridBrush;
	double minScale;
	const double PEN = 1.1;

	public static Brush freeze(string hex) {
		var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
		b.Freeze();
		return b;
	}

	public void Push(double[] data, Brush brush, double min) {
		values = data;
		stroke = brush ?? GridBrush;
		minScale = min;
		InvalidateVisual();
	}

	protected override void OnRender(DrawingContext dc) {
		var w = ActualWidth;
		var h = ActualHeight;
		if (w < 4 || h < 4) return;
		var pen = new Pen(stroke, PEN) { LineJoin = PenLineJoin.Round };
		var data = values;
		if (data == null || data.Length < 2) {
			dc.DrawLine(new Pen(GridBrush, 1), new Point(0, h - 1), new Point(w, h - 1));
			return;
		}
		var max = minScale;
		foreach (var v in data)
			if (v > max) max = v;
		if (max <= 0) max = 1;
		var geo = new StreamGeometry();
		using (var ctx = geo.Open()) {
			var step = (w - PEN) / (data.Length - 1);
			for (var i = 0; i < data.Length; i++) {
				var v = data[i];
				if (double.IsNaN(v) || v < 0) v = 0;
				if (v > max) v = max;
				var x = PEN / 2 + i * step;
				var y = h - PEN / 2 - v / max * (h - PEN);
				if (i == 0) ctx.BeginFigure(new Point(x, y), false, false);
				else ctx.LineTo(new Point(x, y), true, false);
			}
		}
		geo.Freeze();
		dc.DrawGeometry(null, pen, geo);
	}
}
