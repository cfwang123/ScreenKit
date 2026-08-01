using System.Windows.Media;

namespace WpfOCR;

/// <summary>颜色 #AARRGGBB 解析、画刷与 HSV/RGB 互转（自 MusicPlayer 移植）。</summary>
static class ColorUtil {
	public static Color Parse(string s, Color fallback) {
		try {
			if (string.IsNullOrWhiteSpace(s)) return fallback;
			var t = s.Trim();
			if (!t.StartsWith("#")) t = "#" + t;
			return (Color)ColorConverter.ConvertFromString(t);
		}
		catch {
			return fallback;
		}
	}

	public static string ToHex(Color c) =>
		$"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

	public static Brush ToBrush(string s, Color fallback) {
		var b = new SolidColorBrush(Parse(s, fallback));
		b.Freeze();
		return b;
	}

	/// <summary>H 0~360, S/V 0~1。</summary>
	public static void RgbToHsv(byte r, byte g, byte b, out double h, out double s, out double v) {
		var rf = r / 255.0;
		var gf = g / 255.0;
		var bf = b / 255.0;
		var max = Math.Max(rf, Math.Max(gf, bf));
		var min = Math.Min(rf, Math.Min(gf, bf));
		var d = max - min;
		v = max;
		s = max < 1e-9 ? 0 : d / max;
		if (d < 1e-9) {
			h = 0;
			return;
		}
		if (Math.Abs(max - rf) < 1e-9)
			h = 60 * (((gf - bf) / d) % 6);
		else if (Math.Abs(max - gf) < 1e-9)
			h = 60 * (((bf - rf) / d) + 2);
		else
			h = 60 * (((rf - gf) / d) + 4);
		if (h < 0) h += 360;
	}

	/// <summary>H 0~360, S/V 0~1。</summary>
	public static Color HsvToColor(double h, double s, double v, byte a = 255) {
		if (s < 0) s = 0;
		if (s > 1) s = 1;
		if (v < 0) v = 0;
		if (v > 1) v = 1;
		h = ((h % 360) + 360) % 360;
		var c = v * s;
		var x = c * (1 - Math.Abs((h / 60) % 2 - 1));
		var m = v - c;
		double rf, gf, bf;
		if (h < 60) { rf = c; gf = x; bf = 0; }
		else if (h < 120) { rf = x; gf = c; bf = 0; }
		else if (h < 180) { rf = 0; gf = c; bf = x; }
		else if (h < 240) { rf = 0; gf = x; bf = c; }
		else if (h < 300) { rf = x; gf = 0; bf = c; }
		else { rf = c; gf = 0; bf = x; }
		return Color.FromArgb(a,
			(byte)Math.Round((rf + m) * 255),
			(byte)Math.Round((gf + m) * 255),
			(byte)Math.Round((bf + m) * 255));
	}

	public static Color HueColor(double h) => HsvToColor(h, 1, 1, 255);
}
