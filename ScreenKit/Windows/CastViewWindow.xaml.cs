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
	}

	public void SetName(string n) {
		lbname.Text = string.IsNullOrEmpty(n) ? Loc.T("cast.title") : n;
		Title = lbname.Text;
	}

	public void ShowCast() {
		if (WindowState == WindowState.Minimized) {
			WindowStyle = WindowStyle.SingleBorderWindow;
			WindowState = WindowState.Normal;
			WindowStyle = WindowStyle.None;
			applychrome();
		}
		if (!IsVisible) Show();
		Activate();
		Topmost = true;
		Topmost = false;
		Activate();
		Focus();
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
				fitfirst();
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

	void fitfirst() {
		if (sized) return;
		if (WindowState == WindowState.Maximized || full) return;
		if (bmp == null) return;
		var w = pimg.Width > 1 ? pimg.Width : bmp.PixelWidth;
		var h = pimg.Height > 1 ? pimg.Height : bmp.PixelHeight;
		var wa = SystemParameters.WorkArea;
		var maxw = wa.Width * 0.8;
		var maxh = wa.Height * 0.8;
		var s = Math.Min(1, Math.Min(maxw / w, maxh / h));
		Width = Math.Max(240, w * s);
		Height = Math.Max(180, h * s);
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
}
