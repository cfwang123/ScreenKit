using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace ScreenKit;

/// <summary>外部划词的查词浮窗。置顶、不激活、不进任务栏。内容和词典里的选区浮窗一样。</summary>
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

	internal event Action<DictSelRow> HitClick;
	internal event Action SpeakClick;
	internal event Action SearchClick;
	internal event Action TrClick;
	internal event Action CopyClick;

	public SelBarWindow() {
		InitializeComponent();
		bselspeak.Click += (_, _) => SpeakClick?.Invoke();
		bselsearch.Click += (_, _) => SearchClick?.Invoke();
		bseltr.Click += (_, _) => TrClick?.Invoke();
		bselcopy.Click += (_, _) => CopyClick?.Invoke();
		lsel.PreviewMouseLeftButtonUp += (_, e) => {
			var row = rowat(e.OriginalSource as DependencyObject);
			if (row != null) HitClick?.Invoke(row);
		};
		ApplyText();
		ShowRows(null, true);
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
		bselspeak.Content = Loc.T("dict.speak");
		bselsearch.Content = Loc.T("dict.sel.search");
		bseltr.Content = Loc.T("dict.sel.translate");
		bselcopy.Content = Loc.T("dict.sel.copy");
		lbselwait.Text = Loc.T("dict.sel.searching");
	}

	internal string SearchLabel => bselsearch.Content as string ?? "";

	internal void ShowRows(List<DictSelRow> rows, bool searching) {
		if (rows == null) rows = new List<DictSelRow>();
		lbselwait.Visibility = searching && rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
		lsel.ItemsSource = rows;
		lsel.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
		spsel.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
	}

	/// <summary>浮窗放在选区物理矩形下方。放不下时改到上方。不激活，也不盖住选区。</summary>
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
		var gap = 6 * scale;
		var (vl, vt, vw, vh) = ScreenDpi.VirtualScreenPixels();
		var left = px.Left;
		var below = px.Bottom + gap;
		var above = px.Top - hpx - gap;
		var fitBelow = below + hpx <= vt + vh;
		var fitAbove = above >= vt;
		double top;
		if (fitBelow) top = below;
		else if (fitAbove) top = above;
		else {
			var roomBelow = vt + vh - below;
			top = roomBelow >= px.Top - vt ? below : Math.Max(vt, above);
		}
		if (left < vl) left = vl;
		if (left + wpx > vl + vw) left = vl + vw - wpx;
		if (top < vt) top = vt;
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

	static DictSelRow rowat(DependencyObject d) {
		while (d != null && d is not ListBoxItem)
			d = VisualTreeHelper.GetParent(d);
		return d is ListBoxItem li ? li.Content as DictSelRow : null;
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
