namespace ScreenKit;

/// <summary>已加载 ONNX / Sherpa 会话的空闲卸载计时。0 分钟表示不自动卸载。</summary>
static class OnnxIdle {
	public const int DefaultMin = 5;
	public const int MaxMin = 24 * 60;
	public const int TickMs = 30_000;

	public static int ClampMin(int minutes) {
		if (minutes < 0) return 0;
		if (minutes > MaxMin) return MaxMin;
		return minutes;
	}

	public static int LimitMs(int minutes) {
		minutes = ClampMin(minutes);
		if (minutes <= 0) return 0;
		return minutes * 60_000;
	}

	/// <summary>last 为 0 表示当前没有已加载会话。</summary>
	public static bool Due(int last, int limitMs) {
		if (limitMs <= 0 || last == 0) return false;
		return unchecked(Environment.TickCount - last) >= limitMs;
	}
}
