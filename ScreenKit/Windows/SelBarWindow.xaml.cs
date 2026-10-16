using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ScreenKit;

/// <summary>划词占位条。置顶、不激活、不进任务栏，点按钮不把前台从浏览器抢走。</summary>
public partial class SelBarWindow : Window {
	const int GWL_EXSTYLE = -20;
	const int WS_EX_NOACTIVATE = 0x08000000;
	const int WS_EX_TOOLWINDOW = 0x00000080;
	const int WM_MOUSEACTIVATE = 0x0021;
	const int MA_NOACTIVATE = 3;
	const uint SWP_NOSIZE = 0x0001;
	const uint SWP_NOACTIVATE = 0x0010;
	const uint SWP_SHOWWINDOW = 0x0040;
	static readonly IntPtr HwndTopmost = new(-1);

	[DllImport("user32.dll")]
	static extern int GetWindowLong(IntPtr hWnd, int nIndex);

	[DllImport("user32.dll")]
	static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

	[DllImport("user32.dll")]
	static extern bool GetWindowRect(IntPtr hWnd, out RECT rc);

	[DllImport("user32.dll", SetLastError = true)]
	static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

	internal Action<string> Act;
	internal string Text = "";

	public SelBarWindow() {
		InitializeComponent();
		bpause.Click += (_, _) => Act?.Invoke("pause");
		bavatar.Click += (_, _) => Act?.Invoke("avatar");
		bsearch.Click += (_, _) => Act?.Invoke("search");
		btr.Click += (_, _) => Act?.Invoke("translate");
		bexplain.Click += (_, _) => Act?.Invoke("explain");
		bcopy.Click += (_, _) => Act?.Invoke("copy");
		bread.Click += (_, _) => Act?.Invoke("read");
		bmore.Click += (_, _) => Act?.Invoke("more");
		ApplyText();
		SourceInitialized += (_, _) => {
			var src = (HwndSource)PresentationSource.FromVisual(this);
			src.AddHook(onmsg);
			var hwnd = new WindowInteropHelper(this).Handle;
			if (hwnd == IntPtr.Zero) return;
			var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
			SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
		};
	}

	public void ApplyText() {
		lbsearch.Text = Loc.T("selbar.search");
		lbtr.Text = Loc.T("selbar.translate");
		lbexplain.Text = Loc.T("selbar.explain");
		lbcopy.Text = Loc.T("selbar.copy");
		lbread.Text = Loc.T("selbar.read");
		bpause.ToolTip = Loc.T("selbar.pause.tip");
		bavatar.ToolTip = Loc.T("selbar.avatar");
		bsearch.ToolTip = Loc.T("selbar.search");
		btr.ToolTip = Loc.T("selbar.translate");
		bexplain.ToolTip = Loc.T("selbar.explain");
		bcopy.ToolTip = Loc.T("selbar.copy");
		bread.ToolTip = Loc.T("selbar.read");
		bmore.ToolTip = Loc.T("selbar.more");
	}

	/// <summary>把整条放在选区物理矩形上方。放不下时改到下方。不激活。</summary>
	public void ShowAt(Rect px) {
		if (px.Width < 1) px.Width = 1;
		if (px.Height < 1) px.Height = 1;
		if (!IsVisible) {
			Left = -32000;
			Top = -32000;
			Show();
		}
		UpdateLayout();
		var hwnd = new WindowInteropHelper(this).Handle;
		var scale = 1.0;
		if (hwnd != IntPtr.Zero) scale = Math.Max(0.25, ScreenDpi.WindowScale(hwnd));
		var wpx = Math.Max(1, ActualWidth * scale);
		var hpx = Math.Max(1, ActualHeight * scale);
		var gap = 8 * scale;
		var (vl, vt, vw, vh) = ScreenDpi.VirtualScreenPixels();
		var left = px.Left + (px.Width - wpx) / 2.0;
		var top = px.Top - hpx - gap;
		if (top < vt) top = px.Bottom + gap;
		if (left < vl) left = vl;
		if (left + wpx > vl + vw) left = vl + vw - wpx;
		if (top + hpx > vt + vh) top = vt + vh - hpx;
		var ix = (int)Math.Round(left);
		var iy = (int)Math.Round(top);
		Left = ix / scale;
		Top = iy / scale;
		if (hwnd != IntPtr.Zero)
			SetWindowPos(hwnd, HwndTopmost, ix, iy, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
	}

	internal Rect ScreenPx() {
		var hwnd = new WindowInteropHelper(this).Handle;
		if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rc)) return Rect.Empty;
		return new Rect(rc.Left, rc.Top, Math.Max(0, rc.Right - rc.Left), Math.Max(0, rc.Bottom - rc.Top));
	}

	internal static bool HasNoActivate(IntPtr hwnd) {
		if (hwnd == IntPtr.Zero) return false;
		return (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_NOACTIVATE) != 0;
	}

	IntPtr onmsg(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
		if (msg == WM_MOUSEACTIVATE) {
			handled = true;
			return (IntPtr)MA_NOACTIVATE;
		}
		return IntPtr.Zero;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct RECT {
		public int Left, Top, Right, Bottom;
	}
}
