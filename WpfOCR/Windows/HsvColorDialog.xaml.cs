using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WpfOCR;

/// <summary>HSV 选色（含透明度），自 MusicPlayer 移植。</summary>
public partial class HsvColorDialog : Window {
	const int SV_SIZE = 200;
	const int HUE_H = 200;

	double h; // 0~360
	double s; // 0~1
	double v; // 0~1
	byte a = 255;
	bool loading;
	bool dragSv, dragHue, dragAlpha;
	WriteableBitmap bmpSv;
	WriteableBitmap bmpHue;

	public Color SelectedColor { get; private set; }

	public HsvColorDialog(Color initial) {
		InitializeComponent();
		ColorUtil.RgbToHsv(initial.R, initial.G, initial.B, out h, out s, out v);
		a = initial.A;
		SelectedColor = initial;

		Loaded += (_, _) => {
			initbitmaps();
			refreshall();
		};
		SizeChanged += (_, _) => {
			if (IsLoaded) refreshcursors();
		};

		psv.PreviewMouseLeftButtonDown += onsvdown;
		psv.PreviewMouseMove += onsvmove;
		psv.PreviewMouseLeftButtonUp += onsvup;
		psv.LostMouseCapture += (_, _) => dragSv = false;

		phue.PreviewMouseLeftButtonDown += onhuedown;
		phue.PreviewMouseMove += onhuemove;
		phue.PreviewMouseLeftButtonUp += onhueup;
		phue.LostMouseCapture += (_, _) => dragHue = false;

		palpha.PreviewMouseLeftButtonDown += onalphadown;
		palpha.PreviewMouseMove += onalphamove;
		palpha.PreviewMouseLeftButtonUp += onalphaup;
		palpha.LostMouseCapture += (_, _) => dragAlpha = false;

		eh.LostFocus += (_, _) => parsehsvboxes();
		es.LostFocus += (_, _) => parsehsvboxes();
		ev.LostFocus += (_, _) => parsehsvboxes();
		eh.KeyDown += onboxkey;
		es.KeyDown += onboxkey;
		ev.KeyDown += onboxkey;
		ehex.LostFocus += (_, _) => parsehex();
		ehex.KeyDown += (_, e) => {
			if (e.Key == Key.Enter) {
				parsehex();
				e.Handled = true;
			}
		};

		bok.Click += (_, _) => {
			SelectedColor = currentcolor();
			DialogResult = true;
		};
		bcancel.Click += (_, _) => DialogResult = false;
	}

	void onboxkey(object sender, KeyEventArgs e) {
		if (e.Key != Key.Enter) return;
		parsehsvboxes();
		e.Handled = true;
	}

	void onsvdown(object sender, MouseButtonEventArgs e) {
		dragSv = true;
		psv.CaptureMouse();
		updatesv(e.GetPosition(psv));
		e.Handled = true;
	}

	void onsvmove(object sender, MouseEventArgs e) {
		if (!dragSv || e.LeftButton != MouseButtonState.Pressed) return;
		updatesv(e.GetPosition(psv));
		e.Handled = true;
	}

	void onsvup(object sender, MouseButtonEventArgs e) {
		if (!dragSv) return;
		dragSv = false;
		if (psv.IsMouseCaptured) psv.ReleaseMouseCapture();
		e.Handled = true;
	}

	void onhuedown(object sender, MouseButtonEventArgs e) {
		dragHue = true;
		phue.CaptureMouse();
		updatehue(e.GetPosition(phue));
		e.Handled = true;
	}

	void onhuemove(object sender, MouseEventArgs e) {
		if (!dragHue || e.LeftButton != MouseButtonState.Pressed) return;
		updatehue(e.GetPosition(phue));
		e.Handled = true;
	}

	void onhueup(object sender, MouseButtonEventArgs e) {
		if (!dragHue) return;
		dragHue = false;
		if (phue.IsMouseCaptured) phue.ReleaseMouseCapture();
		e.Handled = true;
	}

	void onalphadown(object sender, MouseButtonEventArgs e) {
		dragAlpha = true;
		palpha.CaptureMouse();
		updatealpha(e.GetPosition(palpha));
		e.Handled = true;
	}

	void onalphamove(object sender, MouseEventArgs e) {
		if (!dragAlpha || e.LeftButton != MouseButtonState.Pressed) return;
		updatealpha(e.GetPosition(palpha));
		e.Handled = true;
	}

	void onalphaup(object sender, MouseButtonEventArgs e) {
		if (!dragAlpha) return;
		dragAlpha = false;
		if (palpha.IsMouseCaptured) palpha.ReleaseMouseCapture();
		e.Handled = true;
	}

	void initbitmaps() {
		bmpSv = new WriteableBitmap(SV_SIZE, SV_SIZE, 96, 96, PixelFormats.Bgra32, null);
		imsv.Source = bmpSv;
		bmpHue = new WriteableBitmap(16, HUE_H, 96, 96, PixelFormats.Bgra32, null);
		imhue.Source = bmpHue;
		drawhue();
		drawsv();
	}

	void drawhue() {
		if (bmpHue == null) return;
		var w = bmpHue.PixelWidth;
		var hgt = bmpHue.PixelHeight;
		var pixels = new byte[w * hgt * 4];
		for (var y = 0; y < hgt; y++) {
			var hue = 360.0 * y / Math.Max(1, hgt - 1);
			var c = ColorUtil.HueColor(hue);
			for (var x = 0; x < w; x++) {
				var i = (y * w + x) * 4;
				pixels[i] = c.B;
				pixels[i + 1] = c.G;
				pixels[i + 2] = c.R;
				pixels[i + 3] = 255;
			}
		}
		bmpHue.WritePixels(new Int32Rect(0, 0, w, hgt), pixels, w * 4, 0);
	}

	void drawsv() {
		if (bmpSv == null) return;
		var w = bmpSv.PixelWidth;
		var hgt = bmpSv.PixelHeight;
		var pixels = new byte[w * hgt * 4];
		for (var y = 0; y < hgt; y++) {
			var vv = 1.0 - y / (double)Math.Max(1, hgt - 1);
			for (var x = 0; x < w; x++) {
				var ss = x / (double)Math.Max(1, w - 1);
				var c = ColorUtil.HsvToColor(h, ss, vv, 255);
				var i = (y * w + x) * 4;
				pixels[i] = c.B;
				pixels[i + 1] = c.G;
				pixels[i + 2] = c.R;
				pixels[i + 3] = 255;
			}
		}
		bmpSv.WritePixels(new Int32Rect(0, 0, w, hgt), pixels, w * 4, 0);
	}

	void updatesv(Point p) {
		var w = psv.ActualWidth > 1 ? psv.ActualWidth : SV_SIZE;
		var hgt = psv.ActualHeight > 1 ? psv.ActualHeight : SV_SIZE;
		s = clamp01(p.X / w);
		v = clamp01(1 - p.Y / hgt);
		refreshui(false);
	}

	void updatehue(Point p) {
		var hgt = phue.ActualHeight > 1 ? phue.ActualHeight : HUE_H;
		h = clamp01(p.Y / hgt) * 360;
		drawsv();
		refreshui(false);
	}

	void updatealpha(Point p) {
		var w = palpha.ActualWidth > 1 ? palpha.ActualWidth : 200;
		a = (byte)Math.Round(clamp01(p.X / w) * 255);
		refreshui(false);
	}

	void parsehsvboxes() {
		if (loading) return;
		if (double.TryParse(eh.Text, out var nh)) h = ((nh % 360) + 360) % 360;
		if (double.TryParse(es.Text, out var ns)) s = clamp01(ns > 1.0001 ? ns / 100 : ns);
		if (double.TryParse(ev.Text, out var nv)) v = clamp01(nv > 1.0001 ? nv / 100 : nv);
		drawsv();
		refreshui(true);
	}

	void parsehex() {
		if (loading) return;
		var c = ColorUtil.Parse(ehex.Text, currentcolor());
		ColorUtil.RgbToHsv(c.R, c.G, c.B, out h, out s, out v);
		a = c.A;
		drawsv();
		refreshui(true);
	}

	void refreshall() {
		drawsv();
		refreshui(true);
	}

	void refreshui(bool forceBoxes) {
		loading = true;
		try {
			var c = currentcolor();
			SelectedColor = c;
			ppreview.Background = new SolidColorBrush(c);
			palphafill.Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
			lbalpha.Text = $"{(int)Math.Round(a / 255.0 * 100)}%";

			refreshcursors();

			if (forceBoxes || !eh.IsFocused) eh.Text = ((int)Math.Round(h)).ToString();
			if (forceBoxes || !es.IsFocused) es.Text = ((int)Math.Round(s * 100)).ToString();
			if (forceBoxes || !ev.IsFocused) ev.Text = ((int)Math.Round(v * 100)).ToString();
			if (forceBoxes || !ehex.IsFocused) ehex.Text = ColorUtil.ToHex(c);
		}
		finally {
			loading = false;
		}
	}

	void refreshcursors() {
		var sw = psv.ActualWidth > 1 ? psv.ActualWidth : SV_SIZE;
		var sh = psv.ActualHeight > 1 ? psv.ActualHeight : SV_SIZE;
		cursvTf.X = s * sw - 6;
		cursvTf.Y = (1 - v) * sh - 6;

		var hh = phue.ActualHeight > 1 ? phue.ActualHeight : HUE_H;
		curhue.Margin = new Thickness(0, Math.Max(0, h / 360.0 * hh - 2), 0, 0);
	}

	Color currentcolor() => ColorUtil.HsvToColor(h, s, v, a);

	static double clamp01(double x) => x < 0 ? 0 : (x > 1 ? 1 : x);
}
