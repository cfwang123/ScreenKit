using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ScreenKit;

/// <summary>韩语候选条。不激活。数字键选择，不在这里点。</summary>
sealed class HangulCand : Window {
	const int GWL_EXSTYLE = -20;
	const int WS_EX_NOACTIVATE = 0x08000000;
	const int WS_EX_TOOLWINDOW = 0x00000080;
	const int WM_MOUSEACTIVATE = 0x0021;
	const int MA_NOACTIVATE = 3;

	readonly StackPanel row;

	[DllImport("user32.dll")]
	static extern int GetWindowLong(IntPtr hWnd, int nIndex);

	[DllImport("user32.dll")]
	static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

	public HangulCand() {
		WindowStyle = WindowStyle.None;
		AllowsTransparency = true;
		Background = Brushes.Transparent;
		ShowInTaskbar = false;
		ShowActivated = false;
		Topmost = true;
		SizeToContent = SizeToContent.WidthAndHeight;
		ResizeMode = ResizeMode.NoResize;
		Focusable = false;
		FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Malgun Gothic");
		FontSize = 13;
		var box = new Border {
			Background = Brushes.White,
			BorderBrush = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
			BorderThickness = new Thickness(1),
			Padding = new Thickness(6, 4, 6, 4),
			MaxWidth = 560,
		};
		row = new StackPanel { Orientation = Orientation.Horizontal };
		box.Child = row;
		Content = box;
		SourceInitialized += (_, _) => {
			var src = (HwndSource)PresentationSource.FromVisual(this);
			if (src == null) return;
			src.AddHook(onmsg);
			var hwnd = new WindowInteropHelper(this).Handle;
			if (hwnd == IntPtr.Zero) return;
			var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
			SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
		};
	}

	public void ShowHits(IList<ImeHit> hits, Rect caretPx) {
		row.Children.Clear();
		if (hits == null || hits.Count == 0) {
			HideBar();
			return;
		}
		var n = Math.Min(9, hits.Count);
		for (var i = 0; i < n; i++) {
			var h = hits[i];
			var text = (i + 1) + " " + (h.Text ?? "");
			if (!string.IsNullOrEmpty(h.Comment)) text += " " + h.Comment;
			row.Children.Add(new TextBlock {
				Text = text,
				Margin = new Thickness(i == 0 ? 0 : 10, 0, 0, 0),
				Foreground = Brushes.Black,
			});
		}
		if (!IsVisible) Show();
		UpdateLayout();
		place(caretPx);
	}

	public void HideBar() {
		try { if (IsVisible) Hide(); } catch { }
	}

	void place(Rect caret) {
		var hwnd = new WindowInteropHelper(this).Handle;
		var scale = 1.0;
		if (hwnd != IntPtr.Zero) scale = Math.Max(0.25, ScreenDpi.WindowScale(hwnd));
		var wpx = Math.Max(1, ActualWidth * scale);
		var hpx = Math.Max(1, ActualHeight * scale);
		var (vl, vt, vw, vh) = ScreenDpi.VirtualScreenPixels();
		double left;
		double top;
		if (caret.Width < 1 && caret.Height < 1) {
			var p = System.Windows.Forms.Cursor.Position;
			left = p.X;
			top = p.Y + 18;
		}
		else {
			left = caret.Left;
			top = caret.Bottom + 4;
			if (top + hpx > vt + vh) top = caret.Top - hpx - 4;
		}
		if (left < vl) left = vl;
		if (left + wpx > vl + vw) left = vl + vw - wpx;
		if (top < vt) top = vt;
		if (top + hpx > vt + vh) top = vt + vh - hpx;
		Left = left / scale;
		Top = top / scale;
	}

	IntPtr onmsg(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
		if (msg == WM_MOUSEACTIVATE) {
			handled = true;
			return (IntPtr)MA_NOACTIVATE;
		}
		return IntPtr.Zero;
	}
}
