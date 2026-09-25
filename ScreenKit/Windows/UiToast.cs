using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>仿安卓 Toast：主显示器工作区底部居中浮层，非托盘/系统通知。</summary>
static class UiToast {
	const int GwlExstyle = -20;
	const int WsExNoactivate = 0x08000000;
	const int WsExToolwindow = 0x00000080;
	const int SwShownoactivate = 4;
	const uint SwpNomove = 0x0002;
	const uint SwpNosize = 0x0001;
	const uint SwpShowwindow = 0x0040;
	const uint SwpNoactivate = 0x0010;
	static readonly IntPtr HwndTopmost = new(-1);

	[DllImport("user32.dll")]
	static extern int GetWindowLong(IntPtr hWnd, int nIndex);

	[DllImport("user32.dll")]
	static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

	[DllImport("user32.dll", SetLastError = true)]
	static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
		int x, int y, int cx, int cy, uint uFlags);

	[DllImport("user32.dll")]
	static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

	const int MaxW = 360;
	static Window box;
	static DispatcherTimer hide;

	public static void Show(Window owner, string message, int ms = 1900) {
		if (string.IsNullOrWhiteSpace(message)) return;
		Dispatcher disp = null;
		try { disp = owner?.Dispatcher; } catch { }
		if (disp == null) {
			try { disp = Application.Current?.MainWindow?.Dispatcher; } catch { }
		}
		if (disp == null) {
			try { disp = Application.Current?.Dispatcher; } catch { }
		}
		if (disp == null) return;
		var text = message.Trim();
		var ms2 = ms < 800 ? 800 : (ms > 8000 ? 8000 : ms);
		try {
			if (disp.CheckAccess())
				show(text, ms2, disp);
			else
				disp.BeginInvoke(new Action(() => show(text, ms2, disp)), DispatcherPriority.Normal);
		}
		catch { }
	}

	static void show(string text, int ms, Dispatcher disp) {
		dismiss();
		var root = new Border {
			Background = new SolidColorBrush(Color.FromArgb(230, 45, 45, 48)),
			CornerRadius = new CornerRadius(22),
			Padding = new Thickness(18, 10, 18, 10),
			MaxWidth = MaxW,
			MinWidth = 48,
			Child = new TextBlock {
				Text = text,
				Foreground = Brushes.White,
				FontSize = 14,
				TextWrapping = TextWrapping.Wrap,
				TextAlignment = TextAlignment.Center,
			},
		};
		box = new Window {
			WindowStyle = WindowStyle.None,
			AllowsTransparency = true,
			Background = Brushes.Transparent,
			ShowInTaskbar = false,
			Topmost = true,
			ResizeMode = ResizeMode.NoResize,
			SizeToContent = SizeToContent.WidthAndHeight,
			Content = root,
			ShowActivated = false,
			Focusable = false,
			WindowStartupLocation = WindowStartupLocation.Manual,
		};
		box.SourceInitialized += (_, _) => pin(box);
		box.ContentRendered += (_, _) => place(box);
		try {
			var hwnd = new WindowInteropHelper(box).EnsureHandle();
			if (hwnd != IntPtr.Zero) pin(box);
		}
		catch { }
		place(box);
		box.Show();
		try {
			var hwnd = new WindowInteropHelper(box).Handle;
			if (hwnd != IntPtr.Zero) ShowWindow(hwnd, SwShownoactivate);
		}
		catch { }
		place(box);
		starttimer(disp, ms);
	}

	/// <summary>主显示器工作区底部居中（DIP，与 VoiceInputHud 一致）。</summary>
	static void place(Window w) {
		if (w == null) return;
		try {
			w.UpdateLayout();
			var root = w.Content as FrameworkElement;
			if (root != null) {
				root.Measure(new Size(MaxW, double.PositiveInfinity));
				root.Arrange(new Rect(root.DesiredSize));
			}
			var ww = w.ActualWidth > 1 ? w.ActualWidth : 200;
			var hh = w.ActualHeight > 1 ? w.ActualHeight : 40;
			var wa = SystemParameters.WorkArea;
			w.Left = wa.Left + (wa.Width - ww) / 2;
			w.Top = wa.Bottom - hh - 48;
		}
		catch { }
	}

	static void starttimer(Dispatcher disp, int ms) {
		if (disp == null) return;
		hide = new DispatcherTimer(TimeSpan.FromMilliseconds(ms), DispatcherPriority.Normal,
			(_, _) => dismiss(), disp);
		hide.Start();
	}

	static void dismiss() {
		try { hide?.Stop(); } catch { }
		hide = null;
		if (box != null) {
			try { box.Close(); } catch { }
			box = null;
		}
	}

	static void pin(Window w) {
		try {
			var hwnd = new WindowInteropHelper(w).Handle;
			if (hwnd == IntPtr.Zero) return;
			var ex = GetWindowLong(hwnd, GwlExstyle);
			SetWindowLong(hwnd, GwlExstyle, ex | WsExNoactivate | WsExToolwindow);
			SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpShowwindow | SwpNoactivate);
		}
		catch { }
	}
}
