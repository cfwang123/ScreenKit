using System.Windows;
using System.Windows.Media;

namespace ScreenKit;

/// <summary>简易双线 sparkline：上行(绿) / 下行(蓝)。横轴固定两分钟，只画有数据的部分。</summary>
public class Sparkline : FrameworkElement {
	public static readonly DependencyProperty SendValuesProperty =
		DependencyProperty.Register(nameof(SendValues), typeof(object), typeof(Sparkline),
			new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty RecvValuesProperty =
		DependencyProperty.Register(nameof(RecvValues), typeof(object), typeof(Sparkline),
			new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty AgesProperty =
		DependencyProperty.Register(nameof(Ages), typeof(object), typeof(Sparkline),
			new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

	public object SendValues {
		get => GetValue(SendValuesProperty);
		set => SetValue(SendValuesProperty, value);
	}

	public object RecvValues {
		get => GetValue(RecvValuesProperty);
		set => SetValue(RecvValuesProperty, value);
	}

	/// <summary>每点距今毫秒数，越大越靠左；没有就退回按点数均分。</summary>
	public object Ages {
		get => GetValue(AgesProperty);
		set => SetValue(AgesProperty, value);
	}

	const double PENW = 1.2;
	const double SPAN_MS = RateHistory.SPAN_MS;

	static readonly Pen SendPen = freezePen(Color.FromRgb(0x16, 0xA3, 0x4A));
	static readonly Pen RecvPen = freezePen(Color.FromRgb(0x25, 0x63, 0xEB));

	static Pen freezePen(Color c) {
		var p = new Pen(new SolidColorBrush(c), PENW);
		if (p.CanFreeze) p.Freeze();
		return p;
	}

	protected override void OnRender(DrawingContext dc) {
		var w = ActualWidth;
		var h = ActualHeight;
		if (w < 2 || h < 2) return;
		var send = SendValues as double[];
		var recv = RecvValues as double[];
		var ages = Ages as double[];
		var n = Math.Max(send?.Length ?? 0, recv?.Length ?? 0);
		if (n == 0) return;
		double max = 1;
		if (send != null) foreach (var v in send) if (v > max) max = v;
		if (recv != null) foreach (var v in recv) if (v > max) max = v;
		drawSeries(dc, send, ages, w, h, max, SendPen);
		drawSeries(dc, recv, ages, w, h, max, RecvPen);
	}

	static void drawSeries(DrawingContext dc, double[] data, double[] ages, double w, double h, double max, Pen pen) {
		if (data == null || data.Length == 0) return;
		var timed = ages != null && ages.Length == data.Length;
		var right = w - PENW / 2;
		var span = w - PENW;
		double x0 = double.NaN, y0 = 0;
		var open = false;
		var geo = new StreamGeometry();
		using (var ctx = geo.Open()) {
			for (var i = 0; i < data.Length; i++) {
				var v = data[i];
				if (double.IsNaN(v)) {
					open = false;
					continue;
				}
				if (v < 0) v = 0;
				if (v > max) v = max;
				double x;
				if (timed) {
					x = right - ages[i] / SPAN_MS * span;
					if (x < 0) x = 0;
				} else {
					x = data.Length < 2 ? right : i * span / (data.Length - 1);
				}
				var y = h - PENW / 2 - v / max * (h - PENW);
				if (!open) {
					ctx.BeginFigure(new Point(x, y), false, false);
					open = true;
					x0 = x;
					y0 = y;
				} else ctx.LineTo(new Point(x, y), true, false);
			}
		}
		geo.Freeze();
		dc.DrawGeometry(null, pen, geo);
		// 单点时画个点，否则什么都看不见
		if (data.Length == 1 && !double.IsNaN(x0)) dc.DrawEllipse(pen.Brush, null, new Point(x0, y0), PENW, PENW);
	}
}
