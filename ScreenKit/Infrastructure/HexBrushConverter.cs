using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ScreenKit;

/// <summary>
/// 把 "#RRGGBB" 这样的颜色字符串转成 Brush，给列表里按类型上色的图标用。
/// 认不出就返回透明，不至于让整行不显示。
/// </summary>
public sealed class HexBrushConverter : IValueConverter {
	static readonly Dictionary<string, Brush> cache = new(StringComparer.OrdinalIgnoreCase);

	public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
		var s = value as string;
		if (string.IsNullOrWhiteSpace(s)) return Brushes.Transparent;
		lock (cache) {
			if (cache.TryGetValue(s, out var hit)) return hit;
			Brush b;
			try {
				var c = (Color)ColorConverter.ConvertFromString(s);
				b = new SolidColorBrush(c);
				b.Freeze();
			}
			catch {
				b = Brushes.Transparent;
			}
			cache[s] = b;
			return b;
		}
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
