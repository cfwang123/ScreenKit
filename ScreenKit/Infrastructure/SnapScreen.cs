namespace ScreenKit;

/// <summary>整屏截屏：显示器列表与下标（与 CaptureOverlay 相同顺序）。</summary>
static class SnapScreen {
	public static System.Windows.Forms.Screen[] List() =>
		System.Windows.Forms.Screen.AllScreens
			.Where(s => s.Bounds.Width > 0 && s.Bounds.Height > 0)
			.ToArray();

	public static int ClampIndex(int index) {
		var n = List().Length;
		if (n <= 0) return 0;
		if (index < 0) return 0;
		if (index >= n) return n - 1;
		return index;
	}

	public static System.Windows.Forms.Screen Pick(int index) {
		var list = List();
		if (list.Length == 0) return System.Windows.Forms.Screen.PrimaryScreen;
		return list[ClampIndex(index)];
	}

	public static string MenuLabel(System.Windows.Forms.Screen s, int index) {
		var b = s.Bounds;
		var core = Loc.T("snap.screen.item", index + 1, b.Width, b.Height);
		if (!s.Primary) return core;
		var pri = Loc.T("snap.screen.primary");
		return pri.Length == 0 ? core : $"{core} · {pri}";
	}
}
