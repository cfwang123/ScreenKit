using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ScreenKit;

/// <summary>
/// 桌面实时字幕 OSD：透明置顶、可拖/缩放，句行流式追加并向上滚动。
/// 样式与交互仿 MusicPlayer 桌面歌词。
/// </summary>
public partial class AsrCaptionOsdWindow : Window {
	const double MIN_W = 160;
	const double MIN_H = 56;
	const double GRIP = 12;
	const int MAX_LINES = 400;

	enum ResizeEdge {
		None, N, S, E, W, NE, NW, SE, SW
	}

	readonly AsrCaptionStyle style;
	readonly Action onSave;
	readonly List<string> lines = new();
	string partial = "";
	bool dragging;
	bool resizing;
	bool editMode;
	bool hovering;
	ResizeEdge resizeEdge;
	Point originMouseDip;
	double originLeft, originTop, originW, originH;
	HorizontalAlignment contentAlign = HorizontalAlignment.Center;
	Ellipse[] grips;
	int lastScrollTick;

	/// <summary>拖动/缩放后通知设置窗同步宽高。</summary>
	public event Action GeometryChanged;

	public bool IsEditMode => editMode;

	public AsrCaptionOsdWindow(AsrCaptionStyle style, Action onSave = null) {
		this.style = style ?? new AsrCaptionStyle();
		this.onSave = onSave;
		InitializeComponent();
		grips = new[] { gripNW, gripNE, gripSW, gripSE, gripN, gripS, gripW, gripE };
		placewindow();
		ApplyStyle();
		prootouter.MouseLeftButtonDown += ondown;
		prootouter.MouseMove += onmove;
		prootouter.MouseLeftButtonUp += onup;
		wiregrip(gripNW, ResizeEdge.NW);
		wiregrip(gripNE, ResizeEdge.NE);
		wiregrip(gripSW, ResizeEdge.SW);
		wiregrip(gripSE, ResizeEdge.SE);
		wiregrip(gripN, ResizeEdge.N);
		wiregrip(gripS, ResizeEdge.S);
		wiregrip(gripW, ResizeEdge.W);
		wiregrip(gripE, ResizeEdge.E);
		Loaded += (_, _) => {
			clamptoworkarea();
			style.X = Left;
			style.Y = Top;
			scrolltoend(true);
		};
		LocationChanged += (_, _) => {
			if (dragging || resizing) return;
			style.X = Left;
			style.Y = Top;
			try { onSave?.Invoke(); } catch { }
		};
	}

	void placewindow() {
		// 校验用虚拟屏（含副屏）；默认落点仍用主屏工作区底部居中
		var (va, vt, vw, vh) = ScreenDpi.VirtualScreenDip();
		var wa = SystemParameters.WorkArea;
		var x = style.X;
		var y = style.Y;
		var estW = Math.Max(style.Width, 100);
		var estH = Math.Max(style.Height, 40);
		if (double.IsNaN(x) || double.IsNaN(y)
			|| x > va + vw - 40 || y > vt + vh - 40
			|| x + estW < va + 40 || y + estH < vt + 40) {
			x = wa.Left + (wa.Width - Math.Max(style.Width, 400)) * 0.5;
			y = wa.Bottom - Math.Max(style.Height, 120) - 48;
		}
		Left = x;
		Top = y;
	}

	void wiregrip(Ellipse el, ResizeEdge edge) {
		el.MouseLeftButtonDown += (_, e) => {
			if (!isresizeenabled()) return;
			startresize(edge, e);
			e.Handled = true;
		};
	}

	bool isresizeenabled() => editMode || hovering || resizing;

	public void ApplyStyle() {
		applylayout();
		applychrome();
		rebuildlines();
		scrolltoend(true);
	}

	public void SetEditMode(bool on) {
		editMode = on;
		if (on) {
			bsettings.Visibility = Visibility.Visible;
			if (ActualWidth > 1) style.Width = ActualWidth;
			if (ActualHeight > 1) style.Height = ActualHeight;
		}
		updatechromevis();
		applylayout();
		applychrome();
	}

	/// <summary>替换全部已确认行 + 当前 partial（流式刷新）。</summary>
	public void SetContent(IReadOnlyList<string> committed, string partialText) {
		lines.Clear();
		if (committed != null) {
			for (int i = 0; i < committed.Count; i++) {
				var t = committed[i];
				if (!string.IsNullOrWhiteSpace(t))
					lines.Add(t.Trim());
			}
		}
		trimlines();
		partial = partialText ?? "";
		rebuildlines();
		scrolltoend(false);
	}

	/// <summary>追加一句已确认字幕（新行，向上滚）。</summary>
	public void CommitLine(string text) {
		text = (text ?? "").Trim();
		if (text.Length == 0) {
			partial = "";
			rebuildlines();
			return;
		}
		lines.Add(text);
		trimlines();
		partial = "";
		rebuildlines();
		scrolltoend(true);
	}

	public void SetPartial(string text) {
		partial = text ?? "";
		rebuildlines();
		scrolltoend(false);
	}

	public void Clear() {
		lines.Clear();
		partial = "";
		rebuildlines();
	}

	void trimlines() {
		while (lines.Count > MAX_LINES)
			lines.RemoveAt(0);
	}

	void applylayout() {
		contentAlign = style.Align switch {
			0 => HorizontalAlignment.Left,
			2 => HorizontalAlignment.Right,
			_ => HorizontalAlignment.Center
		};
		plines.HorizontalAlignment = contentAlign;

		var autoW = style.AutoWidth && !editMode;
		var autoH = style.AutoHeight && !editMode;
		if (autoW && autoH)
			SizeToContent = SizeToContent.WidthAndHeight;
		else if (autoW)
			SizeToContent = SizeToContent.Width;
		else if (autoH)
			SizeToContent = SizeToContent.Height;
		else
			SizeToContent = SizeToContent.Manual;

		var (_, _, vw, vh) = ScreenDpi.VirtualScreenDip();
		if (!autoW) {
			var w = style.Width > MIN_W ? style.Width : 720;
			Width = w;
			MinWidth = MIN_W;
			ClearValue(MaxWidthProperty);
			MaxWidth = vw;
		}
		else {
			ClearValue(WidthProperty);
			ClearValue(MinWidthProperty);
			MaxWidth = vw;
		}

		if (!autoH) {
			var h = style.Height > MIN_H ? style.Height : 180;
			Height = h;
			MinHeight = MIN_H;
			ClearValue(MaxHeightProperty);
			MaxHeight = vh;
		}
		else {
			ClearValue(HeightProperty);
			ClearValue(MinHeightProperty);
			MaxHeight = vh;
		}

		proot.ClipToBounds = !autoW || !autoH || editMode;
	}

	void applychrome() {
		var bg = ColorUtil.Parse(style.Background, Color.FromArgb(0x66, 0, 0, 0));
		if (bg.A == 0)
			proot.Background = new SolidColorBrush(Color.FromArgb(1, bg.R, bg.G, bg.B));
		else
			proot.Background = new SolidColorBrush(bg);

		var bc = ColorUtil.Parse(style.BorderColor, Color.FromArgb(0, 0, 0, 0));
		var bt = style.BorderThickness;
		if (bt < 0) bt = 0;
		if (bc.A == 0 || bt < 0.1)
			proot.BorderThickness = new Thickness(0);
		else {
			proot.BorderBrush = new SolidColorBrush(bc);
			proot.BorderThickness = new Thickness(bt);
		}
	}

	void rebuildlines() {
		plines.Children.Clear();
		var ff = new FontFamily(string.IsNullOrWhiteSpace(style.FontFamily)
			? "Microsoft YaHei UI" : style.FontFamily);
		var fs = style.FontSize > 0 ? style.FontSize : 28;
		var fg = ColorUtil.ToBrush(style.Foreground, Colors.White);
		var olColor = ColorUtil.Parse(style.Outline, Color.FromArgb(0xCC, 0, 0, 0));
		var bt = style.BorderThickness > 0 ? style.BorderThickness * 2 : 0;
		var pad = 20.0;
		double maxw;
		if ((!style.AutoWidth || editMode) && style.Width > 80)
			maxw = Math.Max(40, style.Width - bt - pad);
		else
			maxw = style.MaxWidth > 0 ? style.MaxWidth : 900;
		plines.MaxWidth = maxw;

		// 统一用下方确认句样式：SemiBold + 描边，流式中间结果不再换细体/半透明
		for (int i = 0; i < lines.Count; i++)
			plines.Children.Add(buildline(lines[i], ff, fs, fg, olColor, maxw, contentAlign));

		if (!string.IsNullOrWhiteSpace(partial))
			plines.Children.Add(buildline(partial.Trim(), ff, fs, fg, olColor, maxw, contentAlign));

		if (plines.Children.Count == 0)
			plines.Children.Add(buildline("…", ff, fs, fg, olColor, maxw, contentAlign));
	}

	/// <summary>
	/// 硬描边字幕：用 TranslateTransform 偏移描边层（勿用 Margin，会改换行宽导致叠成两种字）。
	/// 字重统一 SemiBold，与原先「已确认句」一致。
	/// </summary>
	static FrameworkElement buildline(string text, FontFamily ff, double size, Brush fg, Color outline,
		double maxw, HorizontalAlignment ha) {
		var host = new Grid {
			HorizontalAlignment = ha,
			Margin = new Thickness(0, 2, 0, 2),
		};
		var align = ha switch {
			HorizontalAlignment.Right => TextAlignment.Right,
			HorizontalAlignment.Center => TextAlignment.Center,
			_ => TextAlignment.Left
		};
		var olBrush = new SolidColorBrush(outline);
		olBrush.Freeze();

		// 四向硬描边（同 MaxWidth，位移用 RenderTransform，换行一致）
		if (outline.A > 0) {
			double[][] offsets = {
				new[] { -1.5, 0 }, new[] { 1.5, 0 }, new[] { 0, -1.5 }, new[] { 0, 1.5 },
			};
			foreach (var off in offsets) {
				var ot = makeline(text, ff, size, olBrush, maxw, ha, align);
				ot.RenderTransform = new TranslateTransform(off[0], off[1]);
				host.Children.Add(ot);
			}
		}
		host.Children.Add(makeline(text, ff, size, fg, maxw, ha, align));
		return host;
	}

	static TextBlock makeline(string text, FontFamily ff, double size, Brush brush, double maxw,
		HorizontalAlignment ha, TextAlignment ta) {
		return new TextBlock {
			Text = text,
			FontFamily = ff,
			FontSize = size,
			Foreground = brush,
			FontWeight = FontWeights.SemiBold,
			TextWrapping = TextWrapping.Wrap,
			MaxWidth = maxw,
			HorizontalAlignment = ha,
			TextAlignment = ta,
		};
	}

	void scrolltoend(bool force) {
		var now = Environment.TickCount;
		if (!force && lastScrollTick != 0 && now - lastScrollTick < 50)
			return;
		lastScrollTick = now;
		try {
			Dispatcher.BeginInvoke(new Action(() => {
				try {
					pscroll.UpdateLayout();
					pscroll.ScrollToVerticalOffset(double.MaxValue);
				}
				catch { }
			}), System.Windows.Threading.DispatcherPriority.Background);
		}
		catch { }
	}

	void updatechromevis() {
		var show = editMode || hovering || resizing;
		var vis = show ? Visibility.Visible : Visibility.Collapsed;
		peditframe.Visibility = vis;
		foreach (var g in grips)
			g.Visibility = vis;
	}

	void onrootenter(object sender, MouseEventArgs e) {
		hovering = true;
		bsettings.Visibility = Visibility.Visible;
		updatechromevis();
	}

	void onrootleave(object sender, MouseEventArgs e) {
		if (IsMouseOver) return;
		if (resizing || dragging) return;
		hovering = false;
		updatechromevis();
		if (!editMode)
			bsettings.Visibility = Visibility.Collapsed;
	}

	void onsettings(object sender, RoutedEventArgs e) {
		e.Handled = true;
		try {
			var owner = Application.Current?.MainWindow;
			if (owner == null || !owner.IsLoaded)
				owner = this;
			AsrCaptionStyleDialog.Open(style, this, owner, () => {
				ApplyStyle();
				try { onSave?.Invoke(); } catch { }
			});
		}
		catch (Exception ex) {
			CaptureLog.Ex("AsrCaptionOsd settings", ex);
		}
	}

	// ---- 拖动 / 缩放 ----

	void startresize(ResizeEdge edge, MouseButtonEventArgs e) {
		resizing = true;
		resizeEdge = edge;
		originMouseDip = screentodip(PointToScreen(e.GetPosition(this)));
		originLeft = Left;
		originTop = Top;
		originW = ActualWidth > 1 ? ActualWidth : Width;
		originH = ActualHeight > 1 ? ActualHeight : Height;
		style.AutoWidth = false;
		style.AutoHeight = false;
		SizeToContent = SizeToContent.Manual;
		prootouter.CaptureMouse();
	}

	void ondown(object sender, MouseButtonEventArgs e) {
		if (e.ChangedButton != MouseButton.Left) return;
		if (e.OriginalSource is DependencyObject d) {
			if (isdescendant(bsettings, d)) return;
			foreach (var g in grips) {
				if (isdescendant(g, d)) return;
			}
		}
		var edge = hitedge(e.GetPosition(this));
		if (edge != ResizeEdge.None) {
			startresize(edge, e);
			updatechromevis();
			e.Handled = true;
			return;
		}
		dragging = true;
		originMouseDip = screentodip(PointToScreen(e.GetPosition(this)));
		originLeft = Left;
		originTop = Top;
		prootouter.CaptureMouse();
		e.Handled = true;
	}

	ResizeEdge hitedge(Point p) {
		var w = ActualWidth > 1 ? ActualWidth : Width;
		var h = ActualHeight > 1 ? ActualHeight : Height;
		var left = p.X <= GRIP;
		var right = p.X >= w - GRIP;
		var top = p.Y <= GRIP;
		var bot = p.Y >= h - GRIP;
		if (top && left) return ResizeEdge.NW;
		if (top && right) return ResizeEdge.NE;
		if (bot && left) return ResizeEdge.SW;
		if (bot && right) return ResizeEdge.SE;
		if (top) return ResizeEdge.N;
		if (bot) return ResizeEdge.S;
		if (left) return ResizeEdge.W;
		if (right) return ResizeEdge.E;
		return ResizeEdge.None;
	}

	static bool isdescendant(DependencyObject root, DependencyObject node) {
		while (node != null) {
			if (node == root) return true;
			node = VisualTreeHelper.GetParent(node);
		}
		return false;
	}

	void onmove(object sender, MouseEventArgs e) {
		if (resizing) {
			var nowDip = screentodip(PointToScreen(e.GetPosition(this)));
			var dx = nowDip.X - originMouseDip.X;
			var dy = nowDip.Y - originMouseDip.Y;
			var l = originLeft;
			var t = originTop;
			var w = originW;
			var h = originH;
			switch (resizeEdge) {
				case ResizeEdge.E: w = originW + dx; break;
				case ResizeEdge.W: w = originW - dx; l = originLeft + dx; break;
				case ResizeEdge.S: h = originH + dy; break;
				case ResizeEdge.N: h = originH - dy; t = originTop + dy; break;
				case ResizeEdge.SE: w = originW + dx; h = originH + dy; break;
				case ResizeEdge.SW: w = originW - dx; l = originLeft + dx; h = originH + dy; break;
				case ResizeEdge.NE: w = originW + dx; h = originH - dy; t = originTop + dy; break;
				case ResizeEdge.NW: w = originW - dx; l = originLeft + dx; h = originH - dy; t = originTop + dy; break;
			}
			if (w < MIN_W) {
				if (resizeEdge is ResizeEdge.W or ResizeEdge.NW or ResizeEdge.SW)
					l = originLeft + originW - MIN_W;
				w = MIN_W;
			}
			if (h < MIN_H) {
				if (resizeEdge is ResizeEdge.N or ResizeEdge.NE or ResizeEdge.NW)
					t = originTop + originH - MIN_H;
				h = MIN_H;
			}
			Left = l;
			Top = t;
			Width = w;
			Height = h;
			style.Width = w;
			style.Height = h;
			style.X = l;
			style.Y = t;
			style.AutoWidth = false;
			style.AutoHeight = false;
			clamptoworkarea();
			return;
		}
		if (dragging) {
			var nowDip = screentodip(PointToScreen(e.GetPosition(this)));
			Left = originLeft + (nowDip.X - originMouseDip.X);
			Top = originTop + (nowDip.Y - originMouseDip.Y);
			clamptoworkarea();
			return;
		}
		if (e.LeftButton != MouseButtonState.Pressed) {
			var edge = hitedge(e.GetPosition(this));
			Cursor = edge switch {
				ResizeEdge.N or ResizeEdge.S => Cursors.SizeNS,
				ResizeEdge.E or ResizeEdge.W => Cursors.SizeWE,
				ResizeEdge.NE or ResizeEdge.SW => Cursors.SizeNESW,
				ResizeEdge.NW or ResizeEdge.SE => Cursors.SizeNWSE,
				_ => Cursors.SizeAll
			};
		}
	}

	void onup(object sender, MouseButtonEventArgs e) {
		if (resizing) {
			resizing = false;
			resizeEdge = ResizeEdge.None;
			if (prootouter.IsMouseCaptured)
				prootouter.ReleaseMouseCapture();
			clamptoworkarea();
			style.X = Left;
			style.Y = Top;
			style.Width = ActualWidth > 1 ? ActualWidth : Width;
			style.Height = ActualHeight > 1 ? ActualHeight : Height;
			try { onSave?.Invoke(); } catch { }
			rebuildlines();
			scrolltoend(true);
			if (!IsMouseOver && !editMode)
				hovering = false;
			updatechromevis();
			GeometryChanged?.Invoke();
			e.Handled = true;
			return;
		}
		if (!dragging) return;
		dragging = false;
		if (prootouter.IsMouseCaptured)
			prootouter.ReleaseMouseCapture();
		clamptoworkarea();
		style.X = Left;
		style.Y = Top;
		try { onSave?.Invoke(); } catch { }
		if (!IsMouseOver && !editMode)
			hovering = false;
		updatechromevis();
		GeometryChanged?.Invoke();
		e.Handled = true;
	}

	Point screentodip(Point screenPx) {
		try {
			var src = PresentationSource.FromVisual(this);
			if (src?.CompositionTarget != null) {
				var m = src.CompositionTarget.TransformFromDevice;
				return m.Transform(screenPx);
			}
		}
		catch { }
		try {
			var dpi = VisualTreeHelper.GetDpi(this);
			return new Point(screenPx.X / dpi.DpiScaleX, screenPx.Y / dpi.DpiScaleY);
		}
		catch {
			return screenPx;
		}
	}

	/// <summary>
	/// 限制在虚拟屏内（所有显示器的并集），保证至少 margin 像素仍可见。
	/// 勿用 SystemParameters.WorkArea（仅主屏），否则副屏拖不动。
	/// </summary>
	void clamptoworkarea() {
		var (va, vt, vw, vh) = ScreenDpi.VirtualScreenDip();
		var vr = va + vw;
		var vb = vt + vh;
		var ww = ActualWidth > 1 ? ActualWidth : 200;
		var wh = ActualHeight > 1 ? ActualHeight : 60;
		const double margin = 40;
		var minL = va - ww + margin;
		var maxL = vr - margin;
		var minT = vt - wh + margin;
		var maxT = vb - margin;
		if (Left < minL) Left = minL;
		if (Left > maxL) Left = maxL;
		if (Top < minT) Top = minT;
		if (Top > maxT) Top = maxT;
	}
}
