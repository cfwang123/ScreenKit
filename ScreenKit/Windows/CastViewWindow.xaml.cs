using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;

namespace ScreenKit;

public partial class CastViewWindow : Window {
	WriteableBitmap bmp;
	bool barlock;
	bool full;
	bool sized;
	int srcw;
	int srch;
	readonly DispatcherTimer stattimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

	public CastViewWindow() {
		InitializeComponent();
		if (CastHost.WinIcon != null) Icon = CastHost.WinIcon;
		applychrome();
		WindowChrome.SetIsHitTestVisibleInChrome(bmin, true);
		WindowChrome.SetIsHitTestVisibleInChrome(bmax, true);
		WindowChrome.SetIsHitTestVisibleInChrome(bclose, true);
		StateChanged += win_StateChanged;
		stattimer.Tick += (_, _) => {
			if (lbst.Visibility == Visibility.Visible)
				lbst.Text = CastHost.Recv?.StatText() ?? "";
		};
		Closing += (_, e) => {
			if (CastHost.Exiting) return;
			e.Cancel = true;
			CastHost.CloseCast();
		};
		applylang();
		ApplyScale();
	}

	void applylang() {
		mnfull.Header = Loc.T("cast.full");
		mnfit.Header = Loc.T("cast.fit");
		mnfill.Header = Loc.T("cast.fill");
	}

	public void ApplyScale() {
		var fill = CastHost.ViewFill;
		vbox.Stretch = fill ? Stretch.UniformToFill : Stretch.Uniform;
		mnfit.IsChecked = !fill;
		mnfill.IsChecked = fill;
	}

	public void SetName(string n, string via = null) {
		var title = Loc.T("cast.title");
		if (!string.IsNullOrEmpty(via))
			title = $"{title} · {CastHost.ViaTag(via)}";
		lbname.Text = string.IsNullOrEmpty(n) || n == Loc.T("cast.title") ? title : n;
		Title = title;
	}

	public string PlaceText { get; private set; }

	public void ShowCast() {
		ShowInTaskbar = true;
		ShowActivated = true;
		if (WindowState == WindowState.Minimized) {
			WindowStyle = WindowStyle.SingleBorderWindow;
			WindowState = WindowState.Normal;
			WindowStyle = WindowStyle.None;
			applychrome();
		}
		if (double.IsNaN(Width) || Width < 200) Width = 640;
		if (double.IsNaN(Height) || Height < 160) Height = 360;
		WindowState = WindowState.Normal;
		Visibility = Visibility.Visible;
		Show();
		if (srcw > 0 && srch > 0 && bmp == null) fitbox(srcw, srch);
		centeronpointer();
		showbar(true);
		lbst.Visibility = Visibility.Collapsed;
		stattimer.Stop();
		ApplyScale();
		tofront();
	}

	void tofront() {
		Topmost = true;
		Activate();
		try { img.Focus(); } catch { }
		try {
			var h = new WindowInteropHelper(this).EnsureHandle();
			ShowWindow(h, 9);
			SetWindowPos(h, HwndTopmost, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
			SetForegroundWindow(h);
			GetWindowRect(h, out var rc);
			PlaceText = $"hwnd={h.ToInt64():X} wr={rc.Left},{rc.Top} {rc.Right - rc.Left}x{rc.Bottom - rc.Top}";
		}
		catch (Exception ex) { PlaceText = ex.Message; }
		Topmost = false;
		try {
			var h = new WindowInteropHelper(this).Handle;
			if (h != IntPtr.Zero)
				SetWindowPos(h, HwndNoTopmost, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
		}
		catch { }
	}

	void centeronpointer() {
		workarea(out var l, out var t, out var aw, out var ah);
		Left = l + Math.Max(0, (aw - Width) / 2);
		Top = t + Math.Max(0, (ah - Height) / 2);
		if (Left < l) Left = l;
		if (Top < t) Top = t;
	}

	void workarea(out double l, out double t, out double w, out double h) {
		var wa = SystemParameters.WorkArea;
		l = wa.Left;
		t = wa.Top;
		w = wa.Width;
		h = wa.Height;
		try {
			if (!GetCursorPos(out var pt)) return;
			var scr = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(pt.X, pt.Y));
			var r = scr.WorkingArea;
			var hwnd = new WindowInteropHelper(this).Handle;
			var sc = ScreenDpi.WindowScale(hwnd);
			if (sc < 0.25) sc = 1;
			l = r.Left / sc;
			t = r.Top / sc;
			w = r.Width / sc;
			h = r.Height / sc;
		}
		catch { }
	}

	public void HideCast() {
		Hide();
		bmp = null;
		img.Source = null;
		sized = false;
		srcw = 0;
		srch = 0;
		lbst.Visibility = Visibility.Collapsed;
		stattimer.Stop();
	}

	public void SetSrc(int dw, int dh) {
		srcw = dw;
		srch = dh;
		layoutcrop();
		if (bmp != null) fitwin(true);
		else if (dw > 0 && dh > 0) fitbox(dw, dh);
	}

	public void Push(byte[] px, int w, int h, int st) {
		if (px == null || w <= 0 || h <= 0 || st < w * 4) return;
		if (px.Length < (h - 1) * st + w * 4) return;
		try {
			if (bmp == null || bmp.PixelWidth != w || bmp.PixelHeight != h) {
				bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
				img.Source = bmp;
				img.Width = w;
				img.Height = h;
				layoutcrop();
				fitwin(true);
			}
			bmp.WritePixels(new Int32Rect(0, 0, w, h), px, st, 0);
		}
		catch { }
	}

	void layoutcrop() {
		if (bmp == null) return;
		var fw = bmp.PixelWidth;
		var fh = bmp.PixelHeight;
		var cw = fw;
		var ch = fh;
		var ox = 0;
		var oy = 0;
		if (srcw > 0 && srch > 0) {
			var s = Math.Min((double)fw / srcw, (double)fh / srch);
			cw = Math.Max(1, (int)Math.Round(srcw * s));
			ch = Math.Max(1, (int)Math.Round(srch * s));
			if (cw > fw) cw = fw;
			if (ch > fh) ch = fh;
			ox = (fw - cw) / 2;
			oy = (fh - ch) / 2;
		}
		pimg.Width = cw;
		pimg.Height = ch;
		img.Width = fw;
		img.Height = fh;
		img.Margin = new Thickness(-ox, -oy, 0, 0);
	}

	void fitbox(double w, double h) {
		if (WindowState == WindowState.Maximized || full) return;
		if (w <= 1 || h <= 1) return;
		workarea(out var l, out var t, out var aw, out var ah);
		var s = Math.Min(1, Math.Min(aw * 0.85 / w, ah * 0.85 / h));
		Width = Math.Max(240, w * s);
		Height = Math.Max(180, h * s);
		Left = l + Math.Max(0, (aw - Width) / 2);
		Top = t + Math.Max(0, (ah - Height) / 2);
	}

	void fitwin(bool force) {
		if (!force && sized) return;
		if (WindowState == WindowState.Maximized || full) return;
		if (pimg.Width <= 1 || pimg.Height <= 1) return;
		fitbox(pimg.Width, pimg.Height);
		sized = true;
	}

	void applychrome() {
		WindowChrome.SetWindowChrome(this, new WindowChrome {
			CaptionHeight = 0,
			ResizeBorderThickness = new Thickness(6),
			GlassFrameThickness = new Thickness(0),
			UseAeroCaptionButtons = false
		});
	}

	void win_StateChanged(object sender, EventArgs e) {
		if (WindowState == WindowState.Minimized) return;
		if (WindowStyle != WindowStyle.None) {
			WindowStyle = WindowStyle.None;
			applychrome();
		}
		bmax.Content = WindowState == WindowState.Maximized ? "❐" : "☐";
	}

	void showbar(bool on) {
		if (barlock && !on) return;
		pbar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
	}

	void win_MouseMove(object sender, MouseEventArgs e) {
		showbar(e.GetPosition(this).Y <= 40 || pbar.IsMouseOver);
	}

	void win_MouseLeave(object sender, MouseEventArgs e) {
		if (!pbar.IsMouseOver) showbar(false);
	}

	void pbar_MouseEnter(object sender, MouseEventArgs e) {
		barlock = true;
		showbar(true);
	}

	void pbar_MouseLeave(object sender, MouseEventArgs e) {
		barlock = false;
		if (e.GetPosition(this).Y > 40) showbar(false);
	}

	void win_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
		if (frombtn(e.OriginalSource as DependencyObject)) return;
		if (e.ClickCount == 2) {
			togglemax();
			e.Handled = true;
			return;
		}
		try { DragMove(); }
		catch { }
	}

	static bool frombtn(DependencyObject d) {
		while (d != null) {
			if (d is Button) return true;
			d = VisualTreeHelper.GetParent(d);
		}
		return false;
	}

	void win_MouseRightButtonUp(object sender, MouseButtonEventArgs e) {
		mn.IsOpen = true;
	}

	void bmin_Click(object sender, RoutedEventArgs e) {
		WindowStyle = WindowStyle.SingleBorderWindow;
		SystemCommands.MinimizeWindow(this);
	}

	void bmax_Click(object sender, RoutedEventArgs e) => togglemax();
	void bclose_Click(object sender, RoutedEventArgs e) => CastHost.CloseCast();
	void mnmin_Click(object sender, RoutedEventArgs e) => bmin_Click(sender, e);
	void mnmax_Click(object sender, RoutedEventArgs e) => togglemax();
	void mnclose_Click(object sender, RoutedEventArgs e) => CastHost.CloseCast();
	void mnset_Click(object sender, RoutedEventArgs e) => CastHost.ShowSet();
	void mndisc_Click(object sender, RoutedEventArgs e) => CastHost.Disconnect();
	void mnfull_Click(object sender, RoutedEventArgs e) => togglefull();
	void mnfit_Click(object sender, RoutedEventArgs e) => CastHost.SetViewFill(false);
	void mnfill_Click(object sender, RoutedEventArgs e) => CastHost.SetViewFill(true);

	void togglemax() {
		if (full) togglefull();
		if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
		else SystemCommands.MaximizeWindow(this);
		bmax.Content = WindowState == WindowState.Maximized ? "❐" : "☐";
	}

	void togglefull() {
		if (!full) {
			full = true;
			SystemCommands.MaximizeWindow(this);
			mnfull.Header = Loc.T("cast.unfull");
			bmax.Content = "❐";
		}
		else {
			full = false;
			SystemCommands.RestoreWindow(this);
			mnfull.Header = Loc.T("cast.full");
			bmax.Content = "☐";
		}
	}

	protected override void OnKeyDown(KeyEventArgs e) {
		if (e.Key == Key.Escape) {
			if (full) togglefull();
			else CastHost.CloseCast();
		}
		base.OnKeyDown(e);
	}

	void win_PreviewKeyDown(object sender, KeyEventArgs e) {
		if (e.Key != Key.Tab) return;
		var on = lbst.Visibility != Visibility.Visible;
		lbst.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
		if (on) {
			lbst.Text = CastHost.Recv?.StatText() ?? "";
			stattimer.Start();
		}
		else stattimer.Stop();
		e.Handled = true;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct NativePoint {
		public int X, Y;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct NativeRect {
		public int Left, Top, Right, Bottom;
	}

	[DllImport("user32.dll")]
	static extern bool ShowWindow(IntPtr h, int cmd);

	[DllImport("user32.dll")]
	static extern bool SetForegroundWindow(IntPtr h);

	[DllImport("user32.dll")]
	static extern bool SetWindowPos(IntPtr h, IntPtr insert, int x, int y, int cx, int cy, uint flags);

	static readonly IntPtr HwndTopmost = new(-1);
	static readonly IntPtr HwndNoTopmost = new(-2);
	const uint SWP_NOSIZE = 0x0001;
	const uint SWP_NOMOVE = 0x0002;
	const uint SWP_SHOWWINDOW = 0x0040;

	[DllImport("user32.dll")]
	static extern bool GetCursorPos(out NativePoint p);

	[DllImport("user32.dll")]
	static extern bool GetWindowRect(IntPtr h, out NativeRect r);
}
