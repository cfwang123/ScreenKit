using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace WpfOCR;

/// <summary>
/// 录屏 HUD：红色外框 + 浮动控制条。
/// 控制条可拖动/收起；红线外侧 5px 移动选区，八向缩放（开始前/录制中均可）。
/// </summary>
public partial class RecordHud : Window {
	const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
	const uint SWP_SHOWWINDOW = 0x0040;
	const int MIN_REGION = 64;
	/// <summary>红线外侧可拖动移动选区的热区宽度（DIP）。</summary>
	const double MOVE_OUT = 5;
	static readonly IntPtr HwndTopmost = new(-1);

	enum DragKind { None, Bar, Mini, Region, NW, N, NE, E, SE, S, SW, W }

	[DllImport("user32.dll")]
	static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

	[DllImport("user32.dll", SetLastError = true)]
	static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

	System.Drawing.Rectangle region;
	readonly RecordOptions recOpt;
	readonly GifOptions gifOpt;
	readonly bool gifMode;
	ScreenRecorder rec;
	GifScreenRecorder gifRec;
	DispatcherTimer timer;
	bool started;
	bool stopping;
	bool suspendedForCapture;
	bool pausedBeforeSuspend;
	string tmpPath;

	bool barCollapsed;
	double? barUserX, barUserY; // DIP；null=自动贴选区
	DragKind dragKind;
	Point dragOrigin; // DIP
	System.Drawing.Rectangle dragRegion0;
	double dragBarX0, dragBarY0;
	// 拖控制条时缓存钳位，避免每帧查显示器
	double dragBarMinX, dragBarMinY, dragBarMaxX, dragBarMaxY;
	double dragBarW, dragBarH;
	double dragAspect; // 缩放开始时 width/height

	public bool Completed { get; private set; }
	public bool Saved { get; private set; }
	public string SavedPath { get; private set; }
	/// <summary>是否已点开始并在录制（含暂停）。</summary>
	public bool IsRecording => started && (gifMode ? gifRec != null : rec != null) && !stopping;

	public event Action Finished;

	public RecordHud(System.Drawing.Rectangle region, RecordOptions options = null)
		: this(region, options, null, gif: false) { }

	/// <summary>GIF 录屏 HUD（无声、低帧率）。</summary>
	public RecordHud(System.Drawing.Rectangle region, GifOptions gifOptions)
		: this(region, null, gifOptions, gif: true) { }

	RecordHud(System.Drawing.Rectangle region, RecordOptions options, GifOptions gifOptions, bool gif) {
		this.region = region;
		gifMode = gif;
		if (gifMode) {
			gifOpt = (gifOptions ?? new GifOptions()).Clone();
			gifOpt.Clamp();
			recOpt = new RecordOptions { AudioEnabled = false, Fps = GifOptions.CaptureFps };
			try {
				var opt = new OcrOptions();
				AppConfig.LoadInto(opt);
				if (opt.Record != null)
					recOpt.LockAspectWhileRecording = opt.Record.LockAspectWhileRecording;
			}
			catch { }
		}
		else {
			recOpt = (options ?? new RecordOptions()).Clone();
			recOpt.Clamp();
			gifOpt = new GifOptions();
		}
		InitializeComponent();

		var optTip = gifMode ? "GIF 录屏选项" : "录屏选项";
		bopt.Content = boptM.Content = "选项";
		bopt.ToolTip = boptM.ToolTip = optTip;

		var (vlDip, vtDip, vwDip, vhDip) = ScreenDpi.VirtualScreenDip();
		Left = vlDip;
		Top = vtDip;
		Width = vwDip;
		Height = vhDip;

		var (vl, vt, vw, vh) = ScreenDpi.VirtualScreenPixels();
		SourceInitialized += (_, _) => {
			var hwnd = new WindowInteropHelper(this).Handle;
			if (hwnd != IntPtr.Zero) {
				SetWindowPos(hwnd, HwndTopmost, vl, vt, vw, vh, SWP_SHOWWINDOW);
				try { SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE); } catch { }
			}
		};

		Loaded += (_, _) => {
			proot.Width = Math.Max(1, ActualWidth);
			proot.Height = Math.Max(1, ActualHeight);
			fillsummary();
			layoutchrome();
			Dispatcher.BeginInvoke(new Action(() => {
				proot.Width = Math.Max(1, ActualWidth);
				proot.Height = Math.Max(1, ActualHeight);
				layoutchrome();
			}), DispatcherPriority.Loaded);
			timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
			timer.Tick += (_, _) => tickui();
			timer.Start();
		};

		bstart.Click += (_, _) => onstart();
		bstartM.Click += (_, _) => onstart();
		bpause.Click += (_, _) => onpause();
		bpauseM.Click += (_, _) => onpause();
		bstop.Click += (_, _) => onstop();
		bstopM.Click += (_, _) => onstop();
		bopt.Click += (_, _) => onoptions();
		boptM.Click += (_, _) => onoptions();
		bcollapse.Click += (_, _) => setcollapsed(true);
		bexpand.Click += (_, _) => setcollapsed(false);
		setplaypauseui(started: false, paused: false);
		initinteract();
		WindowEsc.Attach(this, () => {
			if (!started && !stopping) closeout(false);
		});
	}

	/// <summary>
	/// 截图识别/标注前：隐藏 HUD，并暂停录制（避免遮罩进录像、挡操作）。
	/// </summary>
	public void SuspendForCapture() {
		if (suspendedForCapture || stopping) return;
		suspendedForCapture = true;
		pausedBeforeSuspend = started && ispaused();
		RecordLog.Step("hud_suspend_capture",
			$"started={started} wasPaused={pausedBeforeSuspend} gif={gifMode}");
		try {
			if (started && !ispaused())
				pause();
		}
		catch (Exception ex) { RecordLog.Ex("hud_suspend.Pause", ex); }
		try { Hide(); } catch { }
	}

	/// <summary>截图结束后恢复 HUD 与暂停状态。</summary>
	public void ResumeAfterCapture() {
		if (!suspendedForCapture) return;
		suspendedForCapture = false;
		RecordLog.Step("hud_resume_capture", $"restorePause={pausedBeforeSuspend}");
		try {
			Show();
			retopmost();
		}
		catch (Exception ex) { RecordLog.Ex("hud_resume.Show", ex); }
		try {
			// 仅当挂起前未暂停时才自动继续
			if (started && !pausedBeforeSuspend && ispaused()) {
				resume();
				setrecordingui(paused: false);
			}
			else if (started && ispaused()) {
				setrecordingui(paused: true);
			}
		}
		catch (Exception ex) { RecordLog.Ex("hud_resume.Resume", ex); }
	}

	bool ispaused() => gifMode ? (gifRec?.IsPaused ?? false) : (rec?.IsPaused ?? false);
	void pause() { if (gifMode) gifRec?.Pause(); else rec?.Pause(); }
	void resume() { if (gifMode) gifRec?.Resume(); else rec?.Resume(); }
	TimeSpan elapsed() => gifMode ? (gifRec?.Elapsed ?? TimeSpan.Zero) : (rec?.Elapsed ?? TimeSpan.Zero);
	long filebytes() => gifMode ? (gifRec?.FileBytes ?? 0) : (rec?.FileBytes ?? 0);
	string backend() => gifMode ? gifRec?.Backend : rec?.Backend;
	bool finalizeDone() => gifMode ? (gifRec?.IsFinalizeDone ?? true) : (rec?.IsFinalizeDone ?? true);

	void retopmost() {
		try {
			var (vl, vt, vw, vh) = ScreenDpi.VirtualScreenPixels();
			var hwnd = new WindowInteropHelper(this).Handle;
			if (hwnd != IntPtr.Zero) {
				SetWindowPos(hwnd, HwndTopmost, vl, vt, vw, vh, SWP_SHOWWINDOW);
				try { SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE); } catch { }
			}
		}
		catch { }
	}

	void fillsummary() {
		int rw, rh, ow, oh;
		string sum;
		bool maxEn;
		if (gifMode) {
			rw = region.Width;
			rh = region.Height;
			gifOpt.FitSize(rw, rh, out ow, out oh);
			sum = gifOpt.SummaryText(rw, rh);
			maxEn = gifOpt.MaxSizeEnabled;
		}
		else {
			rw = region.Width % 2 == 0 ? region.Width : region.Width - 1;
			rh = region.Height % 2 == 0 ? region.Height : region.Height - 1;
			recOpt.FitSize(rw, rh, out ow, out oh);
			sum = recOpt.SummaryText(rw, rh);
			maxEn = recOpt.MaxSizeEnabled;
		}
		lbregion.Text = $"{rw}×{rh}";
		if (maxEn && (ow != rw || oh != rh))
			lbsummary.Text = $"{sum} · out {ow}×{oh}";
		else
			lbsummary.Text = sum;
		var tip = $"选区 {rw}×{rh}\n{sum}\n拖动红线外侧可移动 · 拖边缘/角点缩放";
		ToolTip = tip;
		bbar.ToolTip = tip + "\n拖动控制条可移动位置";
	}

	void layoutchrome(bool light = false) {
		var (vl, vt, _, _) = ScreenDpi.VirtualScreenPixels();
		ScreenDpi.VirtualScreenScale(out var sx, out var sy);
		if (sx < 0.25) sx = 1;
		if (sy < 0.25) sy = 1;
		var bx = (region.Left - vl) / sx;
		var by = (region.Top - vt) / sy;
		var bw = region.Width / sx;
		var bh = region.Height / sy;

		var stroke = rborder.StrokeThickness > 0 ? rborder.StrokeThickness : 3;
		var gap = 2.0;
		var outM = stroke + gap;
		Canvas.SetLeft(rborder, bx - outM);
		Canvas.SetTop(rborder, by - outM);
		rborder.Width = bw + outM * 2;
		rborder.Height = bh + outM * 2;
		Canvas.SetLeft(rdot, bx - outM - 4);
		Canvas.SetTop(rdot, by - outM - 4);

		// 红线外侧 MOVE_OUT：整圈拖动移动（描边居中于矩形边）
		var lineOuter = outM + stroke / 2;
		var ox = bx - lineOuter - MOVE_OUT;
		var oy = by - lineOuter - MOVE_OUT;
		var ow = bw + (lineOuter + MOVE_OUT) * 2;
		var oh = bh + (lineOuter + MOVE_OUT) * 2;
		placegrip(bdragN, ox, oy, ow, MOVE_OUT);
		placegrip(bdragS, ox, oy + oh - MOVE_OUT, ow, MOVE_OUT);
		placegrip(bdragW, ox, oy + MOVE_OUT, MOVE_OUT, Math.Max(1, oh - MOVE_OUT * 2));
		placegrip(bdragE, ox + ow - MOVE_OUT, oy + MOVE_OUT, MOVE_OUT, Math.Max(1, oh - MOVE_OUT * 2));

		// 八向手柄贴红线、向内延伸，不占外侧移动热区
		const double gs = 12, ge = 8;
		placegrip(g_nw, bx - lineOuter, by - lineOuter, gs, gs);
		placegrip(g_n, bx + 16, by - lineOuter, Math.Max(8, bw - 32), ge);
		placegrip(g_ne, bx + bw + lineOuter - gs, by - lineOuter, gs, gs);
		placegrip(g_e, bx + bw + lineOuter - ge, by + 16, ge, Math.Max(8, bh - 32));
		placegrip(g_se, bx + bw + lineOuter - gs, by + bh + lineOuter - gs, gs, gs);
		placegrip(g_s, bx + 16, by + bh + lineOuter - ge, Math.Max(8, bw - 32), ge);
		placegrip(g_sw, bx - lineOuter, by + bh + lineOuter - gs, gs, gs);
		placegrip(g_w, bx - lineOuter, by + 16, ge, Math.Max(8, bh - 32));

		double useW, useH;
		if (light) {
			// 拖动中：不 UpdateLayout / 不改 Visibility / 不写 Affinity
			useW = barCollapsed
				? (bmini.ActualWidth > 1 ? bmini.ActualWidth : 110)
				: (bbar.ActualWidth > 1 ? bbar.ActualWidth : 420);
			useH = barCollapsed
				? (bmini.ActualHeight > 1 ? bmini.ActualHeight : 28)
				: (bbar.ActualHeight > 1 ? bbar.ActualHeight : 28);
		}
		else {
			if (barCollapsed) {
				bbar.Visibility = Visibility.Collapsed;
				bmini.Visibility = Visibility.Visible;
			}
			else {
				bmini.Visibility = Visibility.Collapsed;
				bbar.Visibility = Visibility.Visible;
			}
			bbar.UpdateLayout();
			bmini.UpdateLayout();
			useW = barCollapsed
				? (bmini.ActualWidth > 1 ? bmini.ActualWidth : 110)
				: (bbar.ActualWidth > 1 ? bbar.ActualWidth : 420);
			useH = barCollapsed
				? (bmini.ActualHeight > 1 ? bmini.ActualHeight : 28)
				: (bbar.ActualHeight > 1 ? bbar.ActualHeight : 28);
		}

		// 钳到选区所在显示器（勿用整块虚拟屏，否则副屏更矮/错位时会落到屏外死区）
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
			// 优先在选区下方；下方会出该显示器则改到上方；上下都不够则贴该屏底边内侧
			if (below + useH <= mt + mh - 4)
				barY = below;
			else if (above >= mt + 4)
				barY = above;
			else
				barY = maxY;
		}
		barX = clamp(barX, minX, maxX);
		barY = clamp(barY, minY, maxY);
		// 用户拖过的位置若被钳回屏内，写回以免下次仍用屏外坐标
		if (barUserX.HasValue && barUserY.HasValue) {
			barUserX = barX;
			barUserY = barY;
		}

		if (barCollapsed) {
			Canvas.SetLeft(bmini, barX);
			Canvas.SetTop(bmini, barY);
		}
		else {
			Canvas.SetLeft(bbar, barX);
			Canvas.SetTop(bbar, barY);
		}

		if (light) return;
		try {
			var hwnd = new WindowInteropHelper(this).Handle;
			if (hwnd != IntPtr.Zero)
				SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
		}
		catch { }
	}

	static void placegrip(FrameworkElement el, double x, double y, double w, double h) {
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

	void setcollapsed(bool on) {
		barCollapsed = on;
		// 收起时把当前位置记为用户位置，展开仍留在原处
		if (barCollapsed) {
			barUserX = Canvas.GetLeft(bbar);
			barUserY = Canvas.GetTop(bbar);
			if (double.IsNaN(barUserX.Value)) barUserX = Canvas.GetLeft(bmini);
			if (double.IsNaN(barUserY.Value)) barUserY = Canvas.GetTop(bmini);
		}
		layoutchrome();
	}

	void applyregion(System.Drawing.Rectangle r, bool light = false) {
		r = normalizeRegion(r, even: true);
		if (r.Width < MIN_REGION || r.Height < MIN_REGION) return;
		region = r;
		if (gifMode) gifRec?.SetRegion(r);
		else rec?.SetRegion(r);
		if (light) {
			lbregion.Text = $"{region.Width}×{region.Height}";
			layoutchrome(light: true);
		}
		else {
			fillsummary();
			layoutchrome();
		}
	}

	static System.Drawing.Rectangle normalizeRegion(System.Drawing.Rectangle r, bool even) {
		if (even) {
			if (r.Width % 2 != 0) r.Width--;
			if (r.Height % 2 != 0) r.Height--;
		}
		if (r.Width < MIN_REGION) r.Width = MIN_REGION + (even ? MIN_REGION % 2 : 0);
		if (r.Height < MIN_REGION) r.Height = MIN_REGION + (even ? MIN_REGION % 2 : 0);
		if (even) {
			if (r.Width % 2 != 0) r.Width++;
			if (r.Height % 2 != 0) r.Height++;
		}
		return r;
	}

	void initinteract() {
		void wireBar(UIElement el, DragKind kind) {
			el.MouseLeftButtonDown += (s, e) => {
				if (stopping) return;
				// 点在按钮上不拖条
				if (e.OriginalSource is DependencyObject d && findbutton(d) != null) return;
				begindrag(kind, e);
			};
		}
		wireBar(bbar, DragKind.Bar);
		wireBar(bmini, DragKind.Mini);

		void wireGrip(UIElement el, DragKind kind) {
			el.MouseLeftButtonDown += (s, e) => {
				if (stopping) return;
				begindrag(kind, e);
			};
		}
		wireGrip(bdragN, DragKind.Region);
		wireGrip(bdragE, DragKind.Region);
		wireGrip(bdragS, DragKind.Region);
		wireGrip(bdragW, DragKind.Region);
		wireGrip(g_nw, DragKind.NW);
		wireGrip(g_n, DragKind.N);
		wireGrip(g_ne, DragKind.NE);
		wireGrip(g_e, DragKind.E);
		wireGrip(g_se, DragKind.SE);
		wireGrip(g_s, DragKind.S);
		wireGrip(g_sw, DragKind.SW);
		wireGrip(g_w, DragKind.W);

		proot.MouseMove += (_, e) => ondragmove(e);
		proot.MouseLeftButtonUp += (_, e) => enddrag(e);
		proot.LostMouseCapture += (_, _) => { dragKind = DragKind.None; };
	}

	static Button findbutton(DependencyObject d) {
		while (d != null) {
			if (d is Button b) return b;
			d = VisualTreeHelper.GetParent(d);
		}
		return null;
	}

	void begindrag(DragKind kind, MouseButtonEventArgs e) {
		dragKind = kind;
		dragOrigin = e.GetPosition(proot);
		dragRegion0 = region;
		var barEl = barCollapsed ? (FrameworkElement)bmini : bbar;
		dragBarX0 = Canvas.GetLeft(barEl);
		dragBarY0 = Canvas.GetTop(barEl);
		if (double.IsNaN(dragBarX0)) dragBarX0 = 0;
		if (double.IsNaN(dragBarY0)) dragBarY0 = 0;
		if (kind is DragKind.Bar or DragKind.Mini) {
			dragBarW = barEl.ActualWidth > 1 ? barEl.ActualWidth : 40;
			dragBarH = barEl.ActualHeight > 1 ? barEl.ActualHeight : 28;
			var rcx = region.Left + Math.Max(0, region.Width / 2);
			var rcy = region.Top + Math.Max(0, region.Height / 2);
			ScreenDpi.MonitorDipFromPhysical(rcx, rcy, out var ml, out var mt, out var mw, out var mh);
			dragBarMinX = ml + 4;
			dragBarMinY = mt + 4;
			dragBarMaxX = Math.Max(dragBarMinX, ml + mw - dragBarW - 4);
			dragBarMaxY = Math.Max(dragBarMinY, mt + mh - dragBarH - 4);
		}
		else if (kind is not DragKind.None and not DragKind.Region && uselockaspect())
			dragAspect = (double)dragRegion0.Width / Math.Max(1, dragRegion0.Height);
		proot.CaptureMouse();
		e.Handled = true;
	}

	void ondragmove(MouseEventArgs e) {
		if (dragKind == DragKind.None || stopping) return;
		var p = e.GetPosition(proot);
		var dx = p.X - dragOrigin.X;
		var dy = p.Y - dragOrigin.Y;
		if (dragKind is DragKind.Bar or DragKind.Mini) {
			// 只挪 Canvas 位置，避免整页 layoutchrome / UpdateLayout
			var x = clamp(dragBarX0 + dx, dragBarMinX, dragBarMaxX);
			var y = clamp(dragBarY0 + dy, dragBarMinY, dragBarMaxY);
			barUserX = x;
			barUserY = y;
			var el = barCollapsed ? (FrameworkElement)bmini : bbar;
			Canvas.SetLeft(el, x);
			Canvas.SetTop(el, y);
			return;
		}

		ScreenDpi.VirtualScreenScale(out var sx, out var sy);
		if (sx < 0.25) sx = 1;
		if (sy < 0.25) sy = 1;
		var dpx = (int)Math.Round(dx * sx);
		var dpy = (int)Math.Round(dy * sy);
		System.Drawing.Rectangle r;
		if (dragKind == DragKind.Region) {
			r = dragRegion0;
			r.X = dragRegion0.X + dpx;
			r.Y = dragRegion0.Y + dpy;
			var (vl, vt, vw, vh) = ScreenDpi.VirtualScreenPixels();
			if (r.Left < vl) r.X = vl;
			if (r.Top < vt) r.Y = vt;
			if (r.Right > vl + vw) r.X = vl + vw - r.Width;
			if (r.Bottom > vt + vh) r.Y = vt + vh - r.Height;
		}
		else {
			var (vl, vt, vw, vh) = ScreenDpi.VirtualScreenPixels();
			if (uselockaspect()) {
				r = resizewithaspect(dragKind, dpx, dpy);
				r = clampresizeregion(r, dragKind, dragAspect, vl, vt, vw, vh);
			}
			else {
				r = resizefree(dragKind, dpx, dpy);
				r = clampresizefree(r, dragKind, vl, vt, vw, vh);
			}
		}
		applyregion(r, light: true);
	}

	bool uselockaspect() => started && recOpt.LockAspectWhileRecording;

	System.Drawing.Rectangle resizefree(DragKind kind, int dpx, int dpy) {
		var r = dragRegion0;
		switch (kind) {
			case DragKind.NW:
				r.X = dragRegion0.X + dpx;
				r.Y = dragRegion0.Y + dpy;
				r.Width = dragRegion0.Width - dpx;
				r.Height = dragRegion0.Height - dpy;
				break;
			case DragKind.N:
				r.Y = dragRegion0.Y + dpy;
				r.Height = dragRegion0.Height - dpy;
				break;
			case DragKind.NE:
				r.Y = dragRegion0.Y + dpy;
				r.Width = dragRegion0.Width + dpx;
				r.Height = dragRegion0.Height - dpy;
				break;
			case DragKind.E:
				r.Width = dragRegion0.Width + dpx;
				break;
			case DragKind.SE:
				r.Width = dragRegion0.Width + dpx;
				r.Height = dragRegion0.Height + dpy;
				break;
			case DragKind.S:
				r.Height = dragRegion0.Height + dpy;
				break;
			case DragKind.SW:
				r.X = dragRegion0.X + dpx;
				r.Width = dragRegion0.Width - dpx;
				r.Height = dragRegion0.Height + dpy;
				break;
			case DragKind.W:
				r.X = dragRegion0.X + dpx;
				r.Width = dragRegion0.Width - dpx;
				break;
		}
		return r;
	}

	System.Drawing.Rectangle clampresizefree(
		System.Drawing.Rectangle r, DragKind kind, int vl, int vt, int vw, int vh) {
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

	System.Drawing.Rectangle resizewithaspect(DragKind kind, int dpx, int dpy) {
		var r0 = dragRegion0;
		var asp = dragAspect;
		int w, h, x, y;

		switch (kind) {
			case DragKind.SE:
				pickcornersize(r0.Width + dpx, r0.Height + dpy, dpx, dpy, r0, asp, out w, out h);
				x = r0.X;
				y = r0.Y;
				break;
			case DragKind.NW:
				pickcornersize(r0.Width - dpx, r0.Height - dpy, dpx, dpy, r0, asp, out w, out h);
				x = r0.Right - w;
				y = r0.Bottom - h;
				break;
			case DragKind.NE:
				pickcornersize(r0.Width + dpx, r0.Height - dpy, dpx, dpy, r0, asp, out w, out h);
				x = r0.X;
				y = r0.Bottom - h;
				break;
			case DragKind.SW:
				pickcornersize(r0.Width - dpx, r0.Height + dpy, dpx, dpy, r0, asp, out w, out h);
				x = r0.Right - w;
				y = r0.Y;
				break;
			case DragKind.E:
				w = r0.Width + dpx;
				h = (int)Math.Round(w / asp);
				x = r0.X;
				y = r0.Y + (r0.Height - h) / 2;
				break;
			case DragKind.W:
				w = r0.Width - dpx;
				h = (int)Math.Round(w / asp);
				x = r0.Right - w;
				y = r0.Y + (r0.Height - h) / 2;
				break;
			case DragKind.S:
				h = r0.Height + dpy;
				w = (int)Math.Round(h * asp);
				x = r0.X + (r0.Width - w) / 2;
				y = r0.Y;
				break;
			case DragKind.N:
				h = r0.Height - dpy;
				w = (int)Math.Round(h * asp);
				x = r0.X + (r0.Width - w) / 2;
				y = r0.Bottom - h;
				break;
			default:
				return r0;
		}

		w = Math.Max(MIN_REGION, w);
		h = Math.Max(MIN_REGION, h);
		return new System.Drawing.Rectangle(x, y, w, h);
	}

	static void pickcornersize(int cw, int ch, int dpx, int dpy,
		System.Drawing.Rectangle r0, double asp, out int w, out int h) {
		if (Math.Abs(dpx) * r0.Height >= Math.Abs(dpy) * r0.Width) {
			w = cw;
			h = Math.Max(MIN_REGION, (int)Math.Round(w / asp));
		}
		else {
			h = ch;
			w = Math.Max(MIN_REGION, (int)Math.Round(h * asp));
		}
	}

	System.Drawing.Rectangle clampresizeregion(
		System.Drawing.Rectangle r, DragKind kind, double asp,
		int vl, int vt, int vw, int vh) {
		var w = Math.Max(MIN_REGION, r.Width);
		var h = Math.Max(MIN_REGION, r.Height);
		int x, y;
		var vr = vl + vw;
		var vb = vt + vh;
		var r0 = dragRegion0;

		switch (kind) {
			case DragKind.SE:
				x = r.X;
				y = r.Y;
				limitrefsize(ref w, ref h, asp, vr - x, vb - y);
				break;
			case DragKind.NW:
				limitrefsize(ref w, ref h, asp, r.Right - vl, r.Bottom - vt);
				x = r.Right - w;
				y = r.Bottom - h;
				break;
			case DragKind.NE:
				limitrefsize(ref w, ref h, asp, vr - r.Left, r.Bottom - vt);
				x = r.Left;
				y = r.Bottom - h;
				break;
			case DragKind.SW:
				limitrefsize(ref w, ref h, asp, r.Right - vl, vb - r.Top);
				x = r.Right - w;
				y = r.Top;
				break;
			case DragKind.E:
				x = r.X;
				limitrefsize(ref w, ref h, asp, vr - x, vb - vt);
				y = r.Y + (r0.Height - h) / 2;
				y = clampi(y, vt, vb - h);
				break;
			case DragKind.W:
				limitrefsize(ref w, ref h, asp, r.Right - vl, vb - vt);
				x = r.Right - w;
				y = r.Y + (r0.Height - h) / 2;
				y = clampi(y, vt, vb - h);
				break;
			case DragKind.S:
				y = r.Top;
				limitrefsize(ref w, ref h, asp, vr - vl, vb - y);
				x = r.X + (r0.Width - w) / 2;
				x = clampi(x, vl, vr - w);
				break;
			case DragKind.N:
				limitrefsize(ref w, ref h, asp, vr - vl, r.Bottom - vt);
				y = r.Bottom - h;
				x = r.X + (r0.Width - w) / 2;
				x = clampi(x, vl, vr - w);
				break;
			default:
				return r;
		}

		w = Math.Max(MIN_REGION, w);
		h = Math.Max(MIN_REGION, h);
		return new System.Drawing.Rectangle(x, y, w, h);
	}

	static void limitrefsize(ref int w, ref int h, double asp, int maxW, int maxH) {
		maxW = Math.Max(MIN_REGION, maxW);
		maxH = Math.Max(MIN_REGION, maxH);
		if (w <= maxW && h <= maxH) return;
		var s = Math.Min((double)maxW / w, (double)maxH / h);
		w = Math.Max(MIN_REGION, (int)Math.Round(w * s));
		h = Math.Max(MIN_REGION, (int)Math.Round(w / asp));
		if (h > maxH) {
			h = maxH;
			w = Math.Max(MIN_REGION, (int)Math.Round(h * asp));
		}
		if (w > maxW) {
			w = maxW;
			h = Math.Max(MIN_REGION, (int)Math.Round(w / asp));
		}
	}

	static int clampi(int v, int lo, int hi) {
		if (hi < lo) return lo;
		if (v < lo) return lo;
		if (v > hi) return hi;
		return v;
	}

	void enddrag(MouseButtonEventArgs e) {
		if (dragKind == DragKind.None) return;
		var wasRegion = dragKind is not (DragKind.Bar or DragKind.Mini or DragKind.None);
		dragKind = DragKind.None;
		try { proot.ReleaseMouseCapture(); } catch { }
		if (wasRegion) {
			fillsummary();
			layoutchrome();
		}
		e.Handled = true;
	}

	static readonly SolidColorBrush DotRec =
		new(Color.FromRgb(0xC6, 0x28, 0x28));
	static readonly SolidColorBrush DotIdle =
		new(Color.FromRgb(0x5D, 0x40, 0x37));
	static readonly SolidColorBrush StateRec =
		new(Color.FromRgb(0xB7, 0x1C, 0x1C));
	static readonly SolidColorBrush StateIdle =
		new(Color.FromRgb(0x33, 0x33, 0x33));

	void setpauseicon(bool paused) {
		var visPause = paused ? Visibility.Collapsed : Visibility.Visible;
		var visPlay = paused ? Visibility.Visible : Visibility.Collapsed;
		icoPause.Visibility = visPause;
		icoResume.Visibility = visPlay;
		icoPauseM.Visibility = visPause;
		icoResumeM.Visibility = visPlay;
		var tip = paused ? "继续" : "暂停";
		bpause.ToolTip = tip;
		bpauseM.ToolTip = tip;
	}

	/// <summary>未开始只显示开始；录制中/暂停只显示暂停（图标切换继续）。</summary>
	void setplaypauseui(bool started, bool paused) {
		var optVis = started ? Visibility.Collapsed : Visibility.Visible;
		bopt.Visibility = optVis;
		boptM.Visibility = optVis;
		if (started) {
			bstart.Visibility = Visibility.Collapsed;
			bstartM.Visibility = Visibility.Collapsed;
			bpause.Visibility = Visibility.Visible;
			bpauseM.Visibility = Visibility.Visible;
			bpause.IsEnabled = !stopping;
			setpauseicon(paused);
		}
		else {
			bstart.Visibility = Visibility.Visible;
			bstartM.Visibility = Visibility.Visible;
			bpause.Visibility = Visibility.Collapsed;
			bpauseM.Visibility = Visibility.Collapsed;
			bstart.IsEnabled = !stopping;
			setpauseicon(false);
		}
		bstopM.IsEnabled = bstop.IsEnabled;
	}

	void setdot(bool recording) {
		var fill = recording ? DotRec : DotIdle;
		edot.Fill = fill;
		edotM.Fill = fill;
	}

	void setrecordingui(bool paused) {
		setplaypauseui(started: true, paused: paused);
		if (paused) {
			lbstate.Text = "已暂停";
			lbstate.Foreground = StateIdle;
			setdot(false);
		}
		else {
			lbstate.Text = "录制中";
			lbstate.Foreground = StateRec;
			setdot(true);
		}
	}

	void syncctrlenabled() {
		bstartM.IsEnabled = bstart.IsEnabled;
		bpauseM.IsEnabled = bpause.IsEnabled;
		bstopM.IsEnabled = bstop.IsEnabled;
		boptM.IsEnabled = bopt.IsEnabled;
	}

	void onoptions() {
		if (started || stopping) return;
		try {
			var cfg = new OcrOptions();
			AppConfig.LoadInto(cfg);
			if (gifMode) {
				cfg.GifRecord ??= new GifOptions();
				var dlg = new GifOptionsWindow(cfg.GifRecord);
				try { dlg.Owner = Application.Current?.MainWindow; } catch { }
				dlg.ShowDialog();
				if (!dlg.Applied) return;
				gifOpt.CopyFrom(dlg.Result);
				cfg.GifRecord = gifOpt.Clone();
				if (cfg.Record != null)
					recOpt.LockAspectWhileRecording = cfg.Record.LockAspectWhileRecording;
			}
			else {
				cfg.Record ??= new RecordOptions();
				var dlg = new RecordOptionsWindow(cfg.Record);
				try { dlg.Owner = Application.Current?.MainWindow; } catch { }
				dlg.ShowDialog();
				if (!dlg.Applied) return;
				recOpt.CopyFrom(dlg.Result);
				cfg.Record = recOpt.Clone();
			}
			try { AppConfig.Save(cfg); } catch { }
			fillsummary();
			layoutchrome();
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, gifMode ? "GIF 录屏选项" : "录屏选项",
				MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	void onstart() {
		if (started || stopping) return;
		try {
			Action<string> onProgress = msg => {
				try {
					Dispatcher.BeginInvoke(new Action(() => {
						if (!stopping) return;
						lbstate.Text = string.IsNullOrEmpty(msg) ? "导出中" : msg;
					}));
				}
				catch { }
			};
			if (gifMode) {
				gifRec = new GifScreenRecorder(region, gifOpt);
				gifRec.Progress = onProgress;
				gifRec.Start();
				tmpPath = gifRec.TempPath;
			}
			else {
				rec = new ScreenRecorder(region, recOpt.AudioMode, recOpt);
				rec.Progress = onProgress;
				rec.Start();
				tmpPath = rec.TempPath;
			}
			started = true;
			bstart.IsEnabled = false;
			bpause.IsEnabled = true;
			bstop.IsEnabled = true;
			syncctrlenabled();
			setrecordingui(paused: false);
			var be = backend();
			if (!string.IsNullOrEmpty(be)) {
				lbsummary.Text = be;
				bbar.ToolTip = be;
			}
			RecordLog.Step("hud_start",
				$"gif={gifMode} backend={be ?? ""} log={RecordLog.LogPath ?? ""}");
		}
		catch (Exception ex) {
			RecordLog.Ex("hud_start", ex);
			MessageBox.Show(this, ex.Message, gifMode ? "GIF 录屏" : "录屏",
				MessageBoxButton.OK, MessageBoxImage.Warning);
			try { rec?.Dispose(); } catch { }
			try { gifRec?.Dispose(); } catch { }
			rec = null;
			gifRec = null;
		}
	}

	void onpause() {
		if (!started || stopping || suspendedForCapture) return;
		if (gifMode ? gifRec == null : rec == null) return;
		if (ispaused()) {
			resume();
			setrecordingui(paused: false);
		}
		else {
			pause();
			setrecordingui(paused: true);
		}
	}

	void onstop() {
		if (stopping) return;
		if (!started) {
			closeout(false);
			return;
		}
		stopping = true;
		// 若正在为截图挂起，先恢复再停
		if (suspendedForCapture) {
			suspendedForCapture = false;
			try { Show(); } catch { }
		}
		bstart.IsEnabled = false;
		bpause.IsEnabled = false;
		bstop.IsEnabled = false;
		syncctrlenabled();
		lbstate.Text = "正在停止…";
		lbstate.Foreground = StateIdle;
		setdot(false);
		edot.Opacity = 1;
		edotM.Opacity = 1;
		var mp4 = rec;
		var gif = gifRec;
		// 停采集 + 写索引后立刻弹保存；MP4 合成在后台并行
		Task.Run(() => {
			try {
				if (gifMode) gif?.Stop();
				else mp4?.Stop();
			}
			catch (Exception ex) { RecordLog.Ex("hud_stop", ex); }
			Dispatcher.BeginInvoke(new Action(() => afterstop()));
		});
	}

	void afterstop() {
		try {
			timer?.Stop();
			tmpPath = (gifMode ? gifRec?.TempPath : rec?.TempPath) ?? tmpPath;
			tickui();
			var size = filebytes();
			var el = elapsed();
			RecordLog.Step("hud_ask_save",
				$"gif={gifMode} finalizeDone={finalizeDone()} size={size} path={tmpPath} " +
				$"elapsed={el}");

			if (gifMode) {
				afterstopgif();
				return;
			}

			// 不等合成：立刻选输出路径
			var sfd = new Microsoft.Win32.SaveFileDialog {
				Title = "保存录屏",
				Filter = "MP4 视频|*.mp4",
				FileName = $"rec_{DateTime.Now:yyyyMMdd_HHmmss}.mp4",
				DefaultExt = ".mp4",
				AddExtension = true,
				OverwritePrompt = true,
			};
			if (sfd.ShowDialog(this) != true) {
				RecordLog.Step("hud_save_cancel", "user cancelled save dialog");
				lbstate.Text = "已取消，清理中…";
				var dropMp4 = rec;
				Task.Run(() => {
					try { dropMp4?.DiscardTemps(); }
					catch (Exception ex) { RecordLog.Ex("hud_discard", ex); }
					Dispatcher.BeginInvoke(new Action(() => closeout(true)));
				});
				return;
			}
			var dest = sfd.FileName;
			lbstate.Text = recOpt.AudioEnabled && rec != null && !rec.IsFinalizeDone
				? "正在合成音轨…"
				: "正在保存…";
			var recorder = rec;
			Task.Run(() => {
				string err = null;
				string finalSrc = null;
				try {
					if (recorder != null) {
						if (!recorder.IsFinalizeDone) {
							try { recorder.Progress?.Invoke("正在合成音轨…"); } catch { }
						}
						recorder.WaitFinalize();
						finalSrc = recorder.TempPath;
					}
					else finalSrc = tmpPath;

					if (string.IsNullOrEmpty(finalSrc) || !File.Exists(finalSrc)) {
						err = "临时文件不存在。";
						return;
					}
					try { recorder?.Progress?.Invoke("正在保存…"); } catch { }
					RecordLog.Step("hud_copy",
						$"src={RecordLog.FileInfo(finalSrc)} dest={dest} " +
						$"HasAudio={recorder?.HasAudio} err={recorder?.AudioError ?? "-"}");
					if (string.Equals(Path.GetFullPath(finalSrc), Path.GetFullPath(dest),
						StringComparison.OrdinalIgnoreCase)) {
						// 目标就是临时路径（极少见）
					}
					else {
						var dir = Path.GetDirectoryName(dest);
						if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
						File.Copy(finalSrc, dest, overwrite: true);
						try { File.Delete(finalSrc); } catch { }
						try { recorder?.DiscardTemps(); } catch { }
					}
					Saved = true;
					SavedPath = dest;
				}
				catch (Exception ex) {
					err = ex.Message;
					RecordLog.Ex("hud_copy", ex);
				}
				Dispatcher.BeginInvoke(new Action(() => {
					try {
						if (!string.IsNullOrEmpty(err)) {
							MessageBox.Show(this, err, "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
						}
						else if (Saved) {
							var audioNote = "";
							if (recOpt.AudioEnabled) {
								if (!string.IsNullOrEmpty(recorder?.AudioError))
									audioNote = "\n⚠ 声音: " + recorder.AudioError;
								else if (recorder != null && !recorder.HasAudio)
									audioNote = "\n⚠ 声音可能未写入（请确认有系统声/麦克风权限）";
							}
							if (!string.IsNullOrEmpty(audioNote))
								MessageBox.Show(this,
									$"已保存：\n{dest}{audioNote}",
									"录屏", MessageBoxButton.OK, MessageBoxImage.Warning);
							revealinfile(dest);
						}
					}
					catch (Exception ex) {
						RecordLog.Ex("hud_aftersave_ui", ex);
					}
					finally {
						closeout(true);
					}
				}));
			});
		}
		catch (Exception ex) {
			RecordLog.Ex("hud_afterstop", ex);
			MessageBox.Show(this, ex.Message, gifMode ? "GIF 录屏" : "录屏",
				MessageBoxButton.OK, MessageBoxImage.Warning);
			closeout(true);
		}
	}

	/// <summary>GIF：隐藏 HUD → 预览窗调色板/缩放 → 保存或丢弃。</summary>
	void afterstopgif() {
		var gRecorder = gifRec;
		var video = gRecorder?.VideoPath ?? tmpPath;
		var sw = gRecorder?.SrcWidth ?? region.Width;
		var sh = gRecorder?.SrcHeight ?? region.Height;
		var gfps = gRecorder?.Fps ?? gifOpt.Fps;
		var opts = (gRecorder?.Options ?? gifOpt).Clone();

		lbstate.Text = "打开预览…";
		try { Hide(); } catch { }

		if (string.IsNullOrEmpty(video) || !File.Exists(video)) {
			MessageBox.Show(this, "临时视频不存在。", "GIF 录屏",
				MessageBoxButton.OK, MessageBoxImage.Warning);
			Task.Run(() => {
				try { gRecorder?.DiscardTemps(); } catch { }
				Dispatcher.BeginInvoke(new Action(() => closeout(true)));
			});
			return;
		}

		try {
			var dlg = new GifPreviewWindow(video, sw, sh, gfps, opts);
			try { dlg.Owner = Application.Current?.MainWindow; } catch { }
			dlg.ShowDialog();
			if (dlg.Saved && !string.IsNullOrEmpty(dlg.SavedPath)) {
				Saved = true;
				SavedPath = dlg.SavedPath;
				RecordLog.Step("hud_gif_saved", SavedPath);
			}
			else {
				RecordLog.Step("hud_gif_discard", "preview cancelled");
			}
		}
		catch (Exception ex) {
			RecordLog.Ex("hud_gif_preview", ex);
			MessageBox.Show(this, ex.Message, "GIF 预览",
				MessageBoxButton.OK, MessageBoxImage.Warning);
		}
		finally {
			Task.Run(() => {
				try { gRecorder?.DiscardTemps(); } catch (Exception ex) { RecordLog.Ex("hud_discard", ex); }
				Dispatcher.BeginInvoke(new Action(() => closeout(true)));
			});
		}
	}

	static void revealinfile(string filePath) {
		try {
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
			Process.Start(new ProcessStartInfo {
				FileName = "explorer.exe",
				Arguments = $"/select,\"{filePath}\"",
				UseShellExecute = true,
			});
		}
		catch { }
	}

	void tickui() {
		if (suspendedForCapture) return;
		if (gifMode ? gifRec == null : rec == null) return;
		lbtime.Text = fmt(elapsed());
		lbsize.Text = fmtbytes(filebytes());
		if (started && !stopping && !ispaused()) {
			var on = (Environment.TickCount / 500) % 2 == 0;
			var op = on ? 1.0 : 0.35;
			edot.Opacity = op;
			edotM.Opacity = op;
		}
		else {
			edot.Opacity = 1;
			edotM.Opacity = 1;
		}
	}

	void closeout(bool completed) {
		Completed = completed;
		stopping = false;
		suspendedForCapture = false;
		try { timer?.Stop(); } catch { }
		try { rec?.Dispose(); } catch { }
		try { gifRec?.Dispose(); } catch { }
		rec = null;
		gifRec = null;
		try { Close(); } catch { }
		try { Finished?.Invoke(); } catch { }
	}

	protected override void OnClosed(EventArgs e) {
		try { timer?.Stop(); } catch { }
		base.OnClosed(e);
	}

	static string fmt(TimeSpan t) {
		var h = (int)t.TotalHours;
		if (h > 0)
			return $"{h:00}:{t.Minutes:00}:{t.Seconds:00}";
		return $"00:{t.Minutes:00}:{t.Seconds:00}";
	}

	static string fmtbytes(long n) {
		if (n < 1024) return $"{n} B";
		if (n < 1024 * 1024) return $"{n / 1024.0:0.#} KB";
		return $"{n / (1024.0 * 1024):0.##} MB";
	}
}
