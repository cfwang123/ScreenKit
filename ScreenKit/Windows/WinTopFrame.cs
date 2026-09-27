using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Interop;

namespace ScreenKit;

/// <summary>窗口外圈绿框。中间挖空，点击穿过，不挡住要选的窗口。</summary>
sealed class WinTopFrame : Window {
	const int GWL_EXSTYLE = -20;
	const int WS_EX_TOOLWINDOW = 0x00000080;
	const int WS_EX_TRANSPARENT = 0x00000020;
	const int WS_EX_NOACTIVATE = 0x08000000;
	const int THICK = 4;
	const int RGN_DIFF = 4;
	const uint SWP_NOSIZE = 0x0001;
	const uint SWP_NOMOVE = 0x0002;
	const uint SWP_NOACTIVATE = 0x0010;
	static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

	int lastX = int.MinValue, lastY, lastW, lastH;
	bool shown;
	public IntPtr Handle { get; private set; }
	public IntPtr Target { get; private set; }

	public WinTopFrame() {
		WindowStyle = WindowStyle.None;
		ResizeMode = ResizeMode.NoResize;
		WindowStartupLocation = WindowStartupLocation.Manual;
		ShowInTaskbar = false;
		ShowActivated = false;
		Topmost = true;
		Background = new SolidColorBrush(Color.FromRgb(0x07, 0xC1, 0x60));
		Width = 8;
		Height = 8;
		AutomationProperties.SetAutomationId(this, "wintop.frame");
		SourceInitialized += (_, _) => tune();
	}

	public void Place(IntPtr target) {
		Target = target;
		if (!WinTop.TryBounds(target, out var x, out var y, out var w, out var h)) {
			hideframe();
			return;
		}
		ensure();
		if (x == lastX && y == lastY && w == lastW && h == lastH) {
			SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
			return;
		}
		lastX = x;
		lastY = y;
		lastW = w;
		lastH = h;
		ScreenDpi.PinWindowToPhysical(this, x, y, w, h);
		applyregion();
	}

	void hideframe() {
		lastX = int.MinValue;
		Target = IntPtr.Zero;
		if (!shown) return;
		shown = false;
		try { Hide(); } catch { }
	}

	void ensure() {
		if (shown) return;
		var helper = new WindowInteropHelper(this);
		helper.EnsureHandle();
		Handle = helper.Handle;
		tune();
		var empty = CreateRectRgn(0, 0, 0, 0);
		if (SetWindowRgn(Handle, empty, false) == 0) DeleteObject(empty);
		Show();
		shown = true;
		Handle = new WindowInteropHelper(this).Handle;
	}

	void tune() {
		if (Handle == IntPtr.Zero)
			Handle = new WindowInteropHelper(this).Handle;
		if (Handle == IntPtr.Zero) return;
		var ex = GetWindowLong(Handle, GWL_EXSTYLE);
		SetWindowLongPtr(Handle, GWL_EXSTYLE, new IntPtr(ex | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE));
	}

	void applyregion() {
		if (Handle == IntPtr.Zero) return;
		if (!GetClientRect(Handle, out var rc)) return;
		var w = rc.Right - rc.Left;
		var h = rc.Bottom - rc.Top;
		if (w <= THICK * 2 + 2 || h <= THICK * 2 + 2) return;
		var outer = CreateRectRgn(0, 0, w, h);
		var inner = CreateRectRgn(THICK, THICK, w - THICK, h - THICK);
		CombineRgn(outer, outer, inner, RGN_DIFF);
		DeleteObject(inner);
		if (SetWindowRgn(Handle, outer, true) == 0) DeleteObject(outer);
	}

	[StructLayout(LayoutKind.Sequential)]
	struct RECT {
		public int Left, Top, Right, Bottom;
	}

	[DllImport("user32.dll")]
	static extern int GetWindowLong(IntPtr hwnd, int index);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
	static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

	[DllImport("user32.dll")]
	static extern bool GetClientRect(IntPtr hwnd, out RECT rc);

	[DllImport("user32.dll", SetLastError = true)]
	static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

	[DllImport("user32.dll")]
	static extern int SetWindowRgn(IntPtr hwnd, IntPtr rgn, bool redraw);

	[DllImport("gdi32.dll")]
	static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

	[DllImport("gdi32.dll")]
	static extern int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int mode);

	[DllImport("gdi32.dll")]
	static extern bool DeleteObject(IntPtr obj);
}
