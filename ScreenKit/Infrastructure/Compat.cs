namespace ScreenKit;

/// <summary>.NET Framework 4.8 缺少的 BCL API 兼容。</summary>
static class Compat {
	public static int Clamp(int value, int min, int max) {
		if (value < min) return min;
		if (value > max) return max;
		return value;
	}

	public static long Clamp(long value, long min, long max) {
		if (value < min) return min;
		if (value > max) return max;
		return value;
	}

	public static float Clamp(float value, float min, float max) {
		if (value < min) return min;
		if (value > max) return max;
		return value;
	}

	public static double Clamp(double value, double min, double max) {
		if (value < min) return min;
		if (value > max) return max;
		return value;
	}

	/// <summary>net48 无 TickCount64，用毫秒 TickCount 有符号扩展为 long。</summary>
	public static long TickCount64 => Environment.TickCount & 0xFFFFFFFFL;

	public static bool Contains(string s, string value, StringComparison cmp) {
		if (s == null || value == null) return false;
		return s.IndexOf(value, cmp) >= 0;
	}

	public static void ThrowIfDisposed(bool disposed, object instance) {
		if (disposed)
			throw new ObjectDisposedException(instance?.GetType().FullName);
	}

	public static string ProcessPath {
		get {
			try {
				return System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
					?? System.Reflection.Assembly.GetExecutingAssembly().Location;
			}
			catch {
				return System.Reflection.Assembly.GetExecutingAssembly().Location;
			}
		}
	}

	public static string[] SplitTrim(string s, params char[] seps) {
		if (string.IsNullOrEmpty(s)) return Array.Empty<string>();
		var parts = s.Split(seps);
		var list = new List<string>(parts.Length);
		foreach (var p in parts) {
			var t = p?.Trim();
			if (!string.IsNullOrEmpty(t)) list.Add(t);
		}
		return list.ToArray();
	}
}
