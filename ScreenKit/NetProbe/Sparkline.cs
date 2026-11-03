using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;

namespace ScreenKit;

/// <summary>简易双线 sparkline：上行(绿) / 下行(蓝)。</summary>
public class Sparkline : FrameworkElement {
	public static readonly DependencyProperty SendValuesProperty =
		DependencyProperty.Register(nameof(SendValues), typeof(object), typeof(Sparkline),
			new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty RecvValuesProperty =
		DependencyProperty.Register(nameof(RecvValues), typeof(object), typeof(Sparkline),
			new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

	public object SendValues {
		get => GetValue(SendValuesProperty);
		set => SetValue(SendValuesProperty, value);
	}

	public object RecvValues {
		get => GetValue(RecvValuesProperty);
		set => SetValue(RecvValuesProperty, value);
	}

	static readonly Pen SendPen = freezePen(Color.FromRgb(0x16, 0xA3, 0x4A));
	static readonly Pen RecvPen = freezePen(Color.FromRgb(0x25, 0x63, 0xEB));

	static Pen freezePen(Color c) {
		var p = new Pen(new SolidColorBrush(c), 1.2);
		if (p.CanFreeze) p.Freeze();
		return p;
	}

	protected override void OnRender(DrawingContext dc) {
		var w = ActualWidth;
		var h = ActualHeight;
		if (w < 2 || h < 2) return;
		var send = SendValues as double[];
		var recv = RecvValues as double[];
		var n = Math.Max(send?.Length ?? 0, recv?.Length ?? 0);
		if (n < 2) {
			dc.DrawLine(new Pen(Brushes.LightGray, 1), new Point(0, h / 2), new Point(w, h / 2));
			return;
		}
		double max = 1;
		if (send != null) foreach (var v in send) if (v > max) max = v;
		if (recv != null) foreach (var v in recv) if (v > max) max = v;
		drawSeries(dc, send, n, w, h, max, SendPen);
		drawSeries(dc, recv, n, w, h, max, RecvPen);
	}

	static void drawSeries(DrawingContext dc, double[] data, int n, double w, double h, double max, Pen pen) {
		if (data == null || data.Length < 2) return;
		var geo = new StreamGeometry();
		using (var ctx = geo.Open()) {
			var len = data.Length;
			for (var i = 0; i < len; i++) {
				var x = i * (w - 1) / (n - 1);
				var y = h - 1 - data[i] / max * (h - 2);
				if (i == 0) ctx.BeginFigure(new Point(x, y), false, false);
				else ctx.LineTo(new Point(x, y), true, false);
			}
		}
		geo.Freeze();
		dc.DrawGeometry(null, pen, geo);
	}
}
