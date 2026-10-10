using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>
/// 投屏 HUD：红色外框 + 黄色操作条。不计入采集。
/// 红线外侧拖动移动选区，八向缩放；松开后通知发送端。
/// </summary>
public partial class CastHud : Window {
	const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
	const uint SWP_SHOWWINDOW = 0x0040;
	const int MIN_REGION = 16;
	const double MOVE_OUT = 5;
	static readonly IntPtr HwndTopmost = new(-1);

	enum DragKind { None, Bar, Mini, Region, NW, N, NE, E, SE, S, SW, W }

	[DllImport("user32.dll")]
	static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);
	[DllImport("user32.dll", SetLastError = true)]
	static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

	static readonly SolidColorBrush DotLive = freeze(0xC6, 0x28, 0x28);
	static readonly SolidColorBrush DotIdle = freeze(0x9E, 0x9E, 0x9E);

	System.Drawing.Rectangle region;
	DispatcherTimer timer;
	bool barCollapsed;
	bool closing;
	bool started;
	bool paused;
	int elapsed;
	int lastui;
	double? barUserX, barUserY;
	DragKind dragKind;
	Point dragOrigin;
	System.Drawing.Rectangle dragRegion0;
	double dragBarX0, dragBarY0;
	double dragBarMinX, dragBarMinY, dragBarMaxX, dragBarMaxY;

	public event Action StopRequested;
	public event Action StartRequested;
	public event Action PauseRequested;
	public event Action<System.Drawing.Rectangle> RegionChanged;
	public Func<(bool audio, string src)> ReadOptions;
	public Action<bool, string> WriteOptions;
	public System.Drawing.Rectangle Region => region;

	public CastHud(System.Drawing.Rectangle region, string target) {
		this.region = even(region);
		InitializeComponent();
		lbregion.Text = $"{this.region.Width}×{this.region.Height}";
		lbto.Text = target ?? "";
		bopt.Content = boptM.Content = Loc.T("cast.tab.opt");
		bopt.ToolTip = boptM.ToolTip = Loc.T("cast.tab.opt");
		bstart.ToolTip = bstartM.ToolTip = Loc.T("cast.hud.start");

		var (vlDip, vtDip, vwDip, vhDip) = ScreenDpi.VirtualScreenDip();
		Left = vlDip;
		Top = vtDip;
		Width = vwDip;
		Height = vhDip;

		var (vl, vt, vw, vh) = ScreenDpi.VirtualScreenPixels();
		SourceInitialized += (_, _) => {
			var hwnd = new WindowInteropHelper(this).Handle;
			if (hwnd == IntPtr.Zero) return;
			SetWindowPos(hwnd, HwndTopmost, vl, vt, vw, vh, SWP_SHOWWINDOW);
			try { SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE); } catch { }
		};
		Loaded += (_, _) => {
			proot.Width = Math.Max(1, ActualWidth);
			proot.Height = Math.Max(1, ActualHeight);
			fillaudio();
			layout(false);
			timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
			timer.Tick += (_, _) => tick();
			timer.Start();
		};
		Closed += (_, _) => { try { timer?.Stop(); } catch { } };
		bstop.Click += (_, _) => requeststop();
		bstopM.Click += (_, _) => requeststop();
		bstart.Click += (_, _) => onstart();
		bstartM.Click += (_, _) => onstart();
		bpause.Click += (_, _) => onpause();
		bpauseM.Click += (_, _) => onpause();
		bopt.Click += (_, _) => onoptions();
		boptM.Click += (_, _) => onoptions();
		bcollapse.Click += (_, _) => setcollapsed(true);
		bexpand.Click += (_, _) => setcollapsed(false);
		setui();
		fillaudio();
		initdrag();
		WindowEsc.Attach(this, requeststop);
	}

	public void MarkLive() {
		started = true;
		paused = false;
		lastui = Environment.TickCount;
		setui();
	}

	public void MarkPaused(bool on) {
		paused = on;
		setui();
	}

	public void Retop() {
		try {
			var (vl, vt, vw, vh) = ScreenDpi.VirtualScreenPixels();
			var hwnd = new WindowInteropHelper(this).Handle;
			if (hwnd == IntPtr.Zero) return;
			SetWindowPos(hwnd, HwndTopmost, vl, vt, vw, vh, SWP_SHOWWINDOW);
			try { SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE); } catch { }
		}
		catch { }
	}

	void requeststop() {
		if (closing) return;
		closing = true;
		try { StopRequested?.Invoke(); } catch { }
	}

	void onstart() {
		if (started || closing) return;
		try { StartRequested?.Invoke(); } catch { }
	}

	void onpause() {
		if (!started || closing) return;
		try { PauseRequested?.Invoke(); } catch { }
	}

	void onoptions() {
		if (closing) return;
		var audio = true;
		var src = "Speakers";
		try {
			if (ReadOptions != null) (audio, src) = ReadOptions();
		}
		catch { }
		var dlg = new CastOptWindow(audio, src);
		try { dlg.Owner = Application.Current?.MainWindow; } catch { }
		var wasTop = Topmost;
		Topmost = false;
		bool ok;
		try { ok = dlg.ShowDialog() == true; }
		finally { Topmost = wasTop; }
		if (!ok) return;
		try { WriteOptions?.Invoke(dlg.Audio, dlg.Src); } catch { }
		fillaudio();
		layout(false);
	}

	void fillaudio() {
		var on = true;
		var src = "Speakers";
		try {
			if (ReadOptions != null) (on, src) = ReadOptions();
		}
		catch { }
		lbaudio.Text = audtext(on, src);
		lbaudio.ToolTip = on ? Loc.T(srckey(src)) : Loc.T("cast.hud.aud.off");
	}

	static string audtext(bool on, string src) {
		if (!on) return Loc.T("cast.hud.aud.off");
		return Loc.T(audkey(src));
	}

	static string audkey(string src) {
		if (string.Equals(src, "Mic", StringComparison.OrdinalIgnoreCase)) return "cast.hud.aud.mic";
		if (string.Equals(src, "MicAndSpeakers", StringComparison.OrdinalIgnoreCase)) return "cast.hud.aud.both";
		return "cast.hud.aud.spk";
	}

	static string srckey(string src) {
		if (string.Equals(src, "Mic", StringComparison.OrdinalIgnoreCase)) return "cast.tab.src.mic";
		if (string.Equals(src, "MicAndSpeakers", StringComparison.OrdinalIgnoreCase)) return "cast.tab.src.both";
		return "cast.tab.src.spk";
	}

	void setui() {
		var before = started ? Visibility.Collapsed : Visibility.Visible;
		var after = started ? Visibility.Visible : Visibility.Collapsed;
		bopt.Visibility = boptM.Visibility = Visibility.Visible;
		bstart.Visibility = bstartM.Visibility = before;
		bpause.Visibility = bpauseM.Visibility = after;
		var showPause = paused ? Visibility.Collapsed : Visibility.Visible;
		var showPlay = paused ? Visibility.Visible : Visibility.Collapsed;
		icoPause.Visibility = icoPauseM.Visibility = showPause;
		icoResume.Visibility = icoResumeM.Visibility = showPlay;
		var tip = paused ? Loc.T("cast.hud.resume") : Loc.T("cast.hud.pause");
		bpause.ToolTip = bpauseM.ToolTip = tip;
		bstop.ToolTip = bstopM.ToolTip = started ? Loc.T("cast.tab.stop") : Loc.T("cast.hud.cancel");
		if (!started) lbstate.Text = Loc.T("cast.hud.ready");
		else if (paused) lbstate.Text = Loc.T("cast.hud.paused");
		else lbstate.Text = Loc.T("cast.hud.live");
		var live = started && !paused;
		edot.Fill = edotM.Fill = live ? DotLive : DotIdle;
		edot.Opacity = edotM.Opacity = 1;
		layout(false);
	}

	static SolidColorBrush freeze(byte r, byte g, byte b) {
		var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
		brush.Freeze();
		return brush;
	}

	void tick() {
		var now = Environment.TickCount;
		if (lastui != 0 && started && !paused) {
			var d = now - lastui;
			if (d > 0 && d < 2000) elapsed += d;
		}
		lastui = now;
		var ts = TimeSpan.FromMilliseconds(Math.Max(0, elapsed));
		lbtime.Text = $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
		if (started && !paused) {
			var on = (now / 500) % 2 == 0;
			edot.Opacity = edotM.Opacity = on ? 1 : 0.35;
		}
		else edot.Opacity = edotM.Opacity = 1;
	}

	void setcollapsed(bool on) {
		barCollapsed = on;
		if (on) {
			barUserX = Canvas.GetLeft(bbar);
			barUserY = Canvas.GetTop(bbar);
		}
		layout(false);
	}

	void layout(bool light) {
		var (vl, vt, _, _) = ScreenDpi.VirtualScreenPixels();
		ScreenDpi.VirtualScreenScale(out var sx, out var sy);
		var bx = (region.Left - vl) / sx;
		var by = (region.Top - vt) / sy;
		var bw = region.Width / sx;
		var bh = region.Height / sy;
		var stroke = 3.0;
		var outM = stroke + 2;
		Canvas.SetLeft(rborder, bx - outM);
		Canvas.SetTop(rborder, by - outM);
		rborder.Width = Math.Max(1, bw + outM * 2);
		rborder.Height = Math.Max(1, bh + outM * 2);
		Canvas.SetLeft(rdot, bx - outM - 4);
		Canvas.SetTop(rdot, by - outM - 4);

		var lineOuter = outM + stroke / 2;
		var ox = bx - lineOuter - MOVE_OUT;
		var oy = by - lineOuter - MOVE_OUT;
		var ow = bw + (lineOuter + MOVE_OUT) * 2;
		var oh = bh + (lineOuter + MOVE_OUT) * 2;
		place(bdragN, ox, oy, ow, MOVE_OUT);
		place(bdragS, ox, oy + oh - MOVE_OUT, ow, MOVE_OUT);
		place(bdragW, ox, oy + MOVE_OUT, MOVE_OUT, Math.Max(1, oh - MOVE_OUT * 2));
		place(bdragE, ox + ow - MOVE_OUT, oy + MOVE_OUT, MOVE_OUT, Math.Max(1, oh - MOVE_OUT * 2));
		const double gs = 12, ge = 8;
		place(g_nw, bx - lineOuter, by - lineOuter, gs, gs);
		place(g_n, bx + 16, by - lineOuter, Math.Max(8, bw - 32), ge);
		place(g_ne, bx + bw + lineOuter - gs, by - lineOuter, gs, gs);
		place(g_e, bx + bw + lineOuter - ge, by + 16, ge, Math.Max(8, bh - 32));
		place(g_se, bx + bw + lineOuter - gs, by + bh + lineOuter - gs, gs, gs);
		place(g_s, bx + 16, by + bh + lineOuter - ge, Math.Max(8, bw - 32), ge);
		place(g_sw, bx - lineOuter, by + bh + lineOuter - gs, gs, gs);
		place(g_w, bx - lineOuter, by + 16, ge, Math.Max(8, bh - 32));

		double useW, useH;
		if (!light) {
			bbar.Visibility = barCollapsed ? Visibility.Collapsed : Visibility.Visible;
			bmini.Visibility = barCollapsed ? Visibility.Visible : Visibility.Collapsed;
			bbar.UpdateLayout();
			bmini.UpdateLayout();
		}
		var bar = barCollapsed ? (FrameworkElement)bmini : bbar;
		useW = bar.ActualWidth > 1 ? bar.ActualWidth : (barCollapsed ? 80 : 280);
		useH = bar.ActualHeight > 1 ? bar.ActualHeight : 28;

		var rcx = region.Left + Math.Max(0, region.Width / 2);
		var rcy = region.Top + Math.Max(0, region.Height / 2);
		ScreenDpi.MonitorDipFromPhysical(rcx, rcy, out var ml, out var mt, out var mw, out var mh);
		var minX = ml + 4;
		var minY = mt + 4;
		var maxX = Math.Max(minX, ml + mw - useW - 4);
		var maxY = Math.Max(minY, mt + mh - useH - 4);
		double barX, barY;
		if (barUserX.HasValue && barUserY.HasValue) {
			barX = barUserX.Value;
			barY = barUserY.Value;
		}
		else {
			barX = bx + (bw - useW) / 2;
			var below = by + bh + outM + 6;
			var above = by - useH - outM - 6;
			if (below + useH <= mt + mh - 4) barY = below;
			else if (above >= mt + 4) barY = above;
			else barY = maxY;
		}
		barX = clamp(barX, minX, maxX);
		barY = clamp(barY, minY, maxY);
		if (barUserX.HasValue) { barUserX = barX; barUserY = barY; }
		Canvas.SetLeft(bar, barX);
		Canvas.SetTop(bar, barY);
		if (!light) {
			try {
				var hwnd = new WindowInteropHelper(this).Handle;
				if (hwnd != IntPtr.Zero)
					SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
			}
			catch { }
		}
	}

	static void place(FrameworkElement el, double x, double y, double w, double h) {
		Canvas.SetLeft(el, x);
		Canvas.SetTop(el, y);
		el.Width = Math.Max(6, w);
		el.Height = Math.Max(6, h);
	}

	static double clamp(double v, double lo, double hi) {
		if (hi < lo) return lo;
		if (v < lo) return lo;
		if (v > hi) return hi;
		return v;
	}

	void initdrag() {
		void wire(UIElement el, DragKind kind) {
			el.MouseLeftButtonDown += (_, e) => {
				if (closing) return;
				if (e.OriginalSource is DependencyObject d && findbutton(d) != null) return;
				dragKind = kind;
				dragOrigin = e.GetPosition(proot);
				dragRegion0 = region;
				var bar = barCollapsed ? (FrameworkElement)bmini : bbar;
				dragBarX0 = Canvas.GetLeft(bar);
				dragBarY0 = Canvas.GetTop(bar);
				if (double.IsNaN(dragBarX0)) dragBarX0 = 0;
				if (double.IsNaN(dragBarY0)) dragBarY0 = 0;
				if (kind is DragKind.Bar or DragKind.Mini) {
					var rcx = region.Left + region.Width / 2;
					var rcy = region.Top + region.Height / 2;
					ScreenDpi.MonitorDipFromPhysical(rcx, rcy, out var ml, out var mt, out var mw, out var mh);
					var bw = bar.ActualWidth > 1 ? bar.ActualWidth : 40;
					var bh = bar.ActualHeight > 1 ? bar.ActualHeight : 28;
					dragBarMinX = ml + 4;
					dragBarMinY = mt + 4;
					dragBarMaxX = Math.Max(dragBarMinX, ml + mw - bw - 4);
					dragBarMaxY = Math.Max(dragBarMinY, mt + mh - bh - 4);
				}
				proot.CaptureMouse();
				e.Handled = true;
			};
		}
		wire(bbar, DragKind.Bar);
		wire(bmini, DragKind.Mini);
		wire(bdragN, DragKind.Region);
		wire(bdragE, DragKind.Region);
		wire(bdragS, DragKind.Region);
		wire(bdragW, DragKind.Region);
		wire(g_nw, DragKind.NW);
		wire(g_n, DragKind.N);
		wire(g_ne, DragKind.NE);
		wire(g_e, DragKind.E);
		wire(g_se, DragKind.SE);
		wire(g_s, DragKind.S);
		wire(g_sw, DragKind.SW);
		wire(g_w, DragKind.W);
		proot.MouseMove += (_, e) => onmove(e);
		proot.MouseLeftButtonUp += (_, e) => enddrag();
		proot.LostMouseCapture += (_, _) => { if (dragKind != DragKind.None) enddrag(); };
	}

	static Button findbutton(DependencyObject d) {
		while (d != null) {
			if (d is Button b) return b;
			d = VisualTreeHelper.GetParent(d);
		}
		return null;
	}

	void onmove(MouseEventArgs e) {
		if (dragKind == DragKind.None || closing) return;
		var p = e.GetPosition(proot);
		var dx = p.X - dragOrigin.X;
		var dy = p.Y - dragOrigin.Y;
		if (dragKind is DragKind.Bar or DragKind.Mini) {
			var x = clamp(dragBarX0 + dx, dragBarMinX, dragBarMaxX);
			var y = clamp(dragBarY0 + dy, dragBarMinY, dragBarMaxY);
			barUserX = x;
			barUserY = y;
			var el = dragKind == DragKind.Mini ? (FrameworkElement)bmini : bbar;
			Canvas.SetLeft(el, x);
			Canvas.SetTop(el, y);
			return;
		}
		ScreenDpi.VirtualScreenScale(out var sx, out var sy);
		var dpx = (int)Math.Round(dx * sx);
		var dpy = (int)Math.Round(dy * sy);
		var (vl, vt, vw, vh) = ScreenDpi.VirtualScreenPixels();
		System.Drawing.Rectangle r;
		if (dragKind == DragKind.Region) {
			r = dragRegion0;
			r.X += dpx;
			r.Y += dpy;
			if (r.Left < vl) r.X = vl;
			if (r.Top < vt) r.Y = vt;
			if (r.Right > vl + vw) r.X = vl + vw - r.Width;
			if (r.Bottom > vt + vh) r.Y = vt + vh - r.Height;
		}
		else {
			r = resize(dragKind, dpx, dpy);
			r = clampbox(r, dragKind, vl, vt, vw, vh);
		}
		apply(r, commit: dragKind == DragKind.Region);
	}

	void enddrag() {
		if (dragKind == DragKind.None) return;
		var kind = dragKind;
		dragKind = DragKind.None;
		try { proot.ReleaseMouseCapture(); } catch { }
		if (kind is not DragKind.Bar and not DragKind.Mini)
			apply(region, commit: true);
	}

	void apply(System.Drawing.Rectangle r, bool commit) {
		r = even(r);
		if (r.Width < MIN_REGION || r.Height < MIN_REGION) return;
		var sizeSame = r.Width == region.Width && r.Height == region.Height;
		region = r;
		lbregion.Text = $"{r.Width}×{r.Height}";
		layout(light: true);
		if (commit || sizeSame)
			try { RegionChanged?.Invoke(r); } catch { }
	}

	System.Drawing.Rectangle resize(DragKind kind, int dpx, int dpy) {
		var r = dragRegion0;
		switch (kind) {
			case DragKind.NW:
				r.X += dpx; r.Y += dpy; r.Width -= dpx; r.Height -= dpy; break;
			case DragKind.N:
				r.Y += dpy; r.Height -= dpy; break;
			case DragKind.NE:
				r.Y += dpy; r.Width += dpx; r.Height -= dpy; break;
			case DragKind.E:
				r.Width += dpx; break;
			case DragKind.SE:
				r.Width += dpx; r.Height += dpy; break;
			case DragKind.S:
				r.Height += dpy; break;
			case DragKind.SW:
				r.X += dpx; r.Width -= dpx; r.Height += dpy; break;
			case DragKind.W:
				r.X += dpx; r.Width -= dpx; break;
		}
		return r;
	}

	System.Drawing.Rectangle clampbox(System.Drawing.Rectangle r, DragKind kind, int vl, int vt, int vw, int vh) {
		if (r.Width < MIN_REGION) {
			if (kind is DragKind.NW or DragKind.W or DragKind.SW)
				r.X = dragRegion0.Right - MIN_REGION;
			r.Width = MIN_REGION;
		}
		if (r.Height < MIN_REGION) {
			if (kind is DragKind.NW or DragKind.N or DragKind.NE)
				r.Y = dragRegion0.Bottom - MIN_REGION;
			r.Height = MIN_REGION;
		}
		if (r.Left < vl) { r.Width -= vl - r.Left; r.X = vl; }
		if (r.Top < vt) { r.Height -= vt - r.Top; r.Y = vt; }
		if (r.Right > vl + vw) r.Width = vl + vw - r.Left;
		if (r.Bottom > vt + vh) r.Height = vt + vh - r.Top;
		return r;
	}

	static System.Drawing.Rectangle even(System.Drawing.Rectangle r) {
		if (r.Width % 2 != 0) r.Width--;
		if (r.Height % 2 != 0) r.Height--;
		if (r.Width < MIN_REGION) r.Width = MIN_REGION;
		if (r.Height < MIN_REGION) r.Height = MIN_REGION;
		if (r.Width % 2 != 0) r.Width++;
		if (r.Height % 2 != 0) r.Height++;
		return r;
	}
}
