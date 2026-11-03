using System.Collections;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ScreenKit;

/// <summary>数值 → 可读字节/速率字符串；绑定路径保持数值以便 DataGrid 正确排序。</summary>
public sealed class BytesFormatConverter : IValueConverter {
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
		if (value is long l) return ProcessNetRow.FormatBytes(l);
		if (value is int i) return ProcessNetRow.FormatBytes(i);
		if (value is double d) return ProcessNetRow.FormatBytes((long)d);
		if (value is ulong ul) return ProcessNetRow.FormatBytes((long)ul);
		return value?.ToString() ?? "";
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
		Binding.DoNothing;
}

public sealed class RateFormatConverter : IValueConverter {
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
		if (value is double d) return ProcessNetRow.FormatRate(d);
		if (value is float f) return ProcessNetRow.FormatRate(f);
		if (value is long l) return ProcessNetRow.FormatRate(l);
		if (value is int i) return ProcessNetRow.FormatRate(i);
		return value?.ToString() ?? "";
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
		Binding.DoNothing;
}

/// <summary>
/// 表格数值热力背景：0 透明；相对列最大值越高越绿，最深为 #00FF00。
/// ConverterParameter = 列键（如 Proc.Conn、Conn.Cap、Cap.Len）。
/// </summary>
public sealed class HeatBrushConverter : IValueConverter {
	static readonly Dictionary<string, double> MaxMap = new(StringComparer.Ordinal);
	static readonly Brush Transparent = Brushes.Transparent;

	public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
		var v = toDouble(value);
		if (v <= 0) return Transparent;
		var key = parameter as string ?? "";
		if (string.IsNullOrEmpty(key) || !MaxMap.TryGetValue(key, out var max) || max <= 0)
			return Transparent;
		var t = v / max;
		if (t > 1) t = 1;
		if (t < 0) t = 0;
		// 白 → #00FF00：r/b 随 t 降到 0，g 恒为 255
		var r = (byte)(255 * (1 - t));
		var g = (byte)255;
		var b = (byte)(255 * (1 - t));
		var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
		if (brush.CanFreeze) brush.Freeze();
		return brush;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
		Binding.DoNothing;

	public static void SetMax(string key, double max) {
		if (string.IsNullOrEmpty(key)) return;
		MaxMap[key] = max > 0 ? max : 0;
	}

	static double toDouble(object value) {
		if (value is int i) return i;
		if (value is long l) return l;
		if (value is double d) return d;
		if (value is float f) return f;
		if (value is ulong ul) return ul;
		if (value is uint u) return u;
		return 0;
	}

	static double maxOf(IEnumerable items, Func<object, double> pick) {
		double m = 0;
		if (items == null) return 0;
		foreach (var o in items) {
			var v = pick(o);
			if (v > m) m = v;
		}
		return m;
	}

	public static void UpdateFromProcesses(IEnumerable procs) {
		SetMax("Proc.Conn", maxOf(procs, o => o is ProcessNetRow p ? p.ConnectionCount : 0));
		SetMax("Proc.Cap", maxOf(procs, o => o is ProcessNetRow p ? p.CaptureCount : 0));
		SetMax("Proc.BytesSent", maxOf(procs, o => o is ProcessNetRow p ? p.BytesSent : 0));
		SetMax("Proc.BytesRecv", maxOf(procs, o => o is ProcessNetRow p ? p.BytesRecv : 0));
		SetMax("Proc.PktsSent", maxOf(procs, o => o is ProcessNetRow p ? p.PacketsSent : 0));
		SetMax("Proc.PktsRecv", maxOf(procs, o => o is ProcessNetRow p ? p.PacketsRecv : 0));
		SetMax("Proc.SendBps", maxOf(procs, o => o is ProcessNetRow p ? p.SendBps : 0));
		SetMax("Proc.RecvBps", maxOf(procs, o => o is ProcessNetRow p ? p.RecvBps : 0));
	}

	public static void UpdateFromConnections(IEnumerable conns) {
		SetMax("Conn.Cap", maxOf(conns, o => o is ConnectionRow c ? c.CaptureCount : 0));
		SetMax("Conn.BytesSent", maxOf(conns, o => o is ConnectionRow c ? c.BytesSent : 0));
		SetMax("Conn.BytesRecv", maxOf(conns, o => o is ConnectionRow c ? c.BytesRecv : 0));
		SetMax("Conn.PktsSent", maxOf(conns, o => o is ConnectionRow c ? c.PacketsSent : 0));
		SetMax("Conn.PktsRecv", maxOf(conns, o => o is ConnectionRow c ? c.PacketsRecv : 0));
		SetMax("Conn.SendBps", maxOf(conns, o => o is ConnectionRow c ? c.SendBps : 0));
		SetMax("Conn.RecvBps", maxOf(conns, o => o is ConnectionRow c ? c.RecvBps : 0));
	}

	public static void UpdateFromHosts(IEnumerable hosts) {
		SetMax("Host.Hit", maxOf(hosts, o => o is HostRecord h ? h.HitCount : 0));
		SetMax("Host.Bytes", maxOf(hosts, o => o is HostRecord h ? h.TotalBytes : 0));
	}

	public static void UpdateFromPackets(IEnumerable packets) {
		SetMax("Cap.No", maxOf(packets, o => o is PacketRow p ? p.No : 0));
		SetMax("Cap.Len", maxOf(packets, o => o is PacketRow p ? p.Length : 0));
		SetMax("Cap.Pay", maxOf(packets, o => o is PacketRow p ? p.PayloadLength : 0));
	}

	public static void UpdateFromStatRows(IEnumerable rows, string valueKey, string extraKey = null) {
		SetMax(valueKey, maxOf(rows, o => {
			var t = o?.GetType();
			if (t == null) return 0;
			var prop = t.GetProperty("Value");
			if (prop == null) return 0;
			return toDouble(prop.GetValue(o, null));
		}));
		if (!string.IsNullOrEmpty(extraKey)) {
			SetMax(extraKey, maxOf(rows, o => {
				var t = o?.GetType();
				if (t == null) return 0;
				var prop = t.GetProperty("ExtraValue");
				if (prop == null) return 0;
				return toDouble(prop.GetValue(o, null));
			}));
		}
	}
}
