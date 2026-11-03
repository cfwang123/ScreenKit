using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ScreenKit.DocView;

/// <summary>
/// 图片预览：滚轮缩放（以光标为中心）、拖拽平移、适应宽高 / 旋转；只读。
/// </summary>
sealed class ImageViewer : IDocViewer {
	const double MIN_ZOOM = 0.05;
	const double MAX_ZOOM = 16.0;
	const double BASE_ZOOM = 1.0;
	const double WHEEL_FACTOR = 1.15;
	/// <summary>拖拽超过此距离才算 pan，避免误触。</summary>
	const double PAN_SLOP = 2;

	readonly Grid root;
	readonly ScrollViewer scroller;
	readonly Grid canvas;
	readonly Image img;
	readonly ScaleTransform scale;
	readonly RotateTransform rotate;
	readonly TextBlock lberr;

	BitmapSource bmp;
	double zoom = BASE_ZOOM;
	int rotQuarter; // 0..3，每步 90° 顺时针
	int pixelW;
	int pixelH;

	// —— 鼠标 pan ——
	bool panArmed;
	bool panning;
	Point panStart;
	double panH0;
	double panV0;
	MouseButton panButton = MouseButton.Left;

	public FrameworkElement View => root;
	public string FilePath { get; private set; }
	public string Title { get; private set; }
	public DocKind Kind => DocKind.Image;
	public double Zoom => zoom;
	public string StatusText {
		get {
			var tag = "IMG";
			try {
				var ext = Path.GetExtension(FilePath ?? "");
				if (!string.IsNullOrEmpty(ext))
					tag = ext.TrimStart('.').ToUpperInvariant();
			} catch { /* ignore */ }
			var dim = pixelW > 0 && pixelH > 0 ? $"  ·  {pixelW}×{pixelH}" : "";
			var rot = rotQuarter != 0 ? $"  ·  {rotQuarter * 90}°" : "";
			return $"{tag}{dim}{rot}  ·  {(int)(zoom * 100)}%";
		}
	}
	public int PageCount => 1;
	public int CurrentPage => 1;
	public bool HasOutline => false;
	public bool SidePanelVisible => false;

	public event Action StatusChanged;

	public ImageViewer() {
		img = new Image {
			Stretch = Stretch.None,
			SnapsToDevicePixels = true,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
		};
		RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

		scale = new ScaleTransform(1, 1);
		rotate = new RotateTransform(0);
		var tg = new TransformGroup();
		tg.Children.Add(rotate);
		tg.Children.Add(scale);
		img.LayoutTransform = tg;

		canvas = new Grid {
			Background = Brushes.Transparent,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
		};
		canvas.Children.Add(img);

		scroller = new ScrollViewer {
			Content = canvas,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			Background = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
			Focusable = true,
			Cursor = Cursors.Arrow,
		};
		// 滚轮缩放（无需 Ctrl）；左键/中键拖拽平移；双击适应
		scroller.PreviewMouseWheel += onwheel;
		scroller.PreviewMouseDown += onmousedown;
		scroller.PreviewMouseUp += onmouseup;
		scroller.PreviewMouseMove += onmousemove;
		scroller.LostMouseCapture += (_, _) => endpan(cancel: true);
		scroller.MouseLeave += (_, _) => {
			if (!panning && !panArmed) scroller.Cursor = Cursors.Arrow;
		};

		lberr = new TextBlock {
			Text = "",
			Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
			FontSize = 14,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(24),
			Visibility = Visibility.Collapsed,
		};

		root = new Grid { Background = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)) };
		root.Children.Add(scroller);
		root.Children.Add(lberr);
		root.Loaded += (_, _) => {
			if (bmp != null && Math.Abs(zoom - BASE_ZOOM) < 0.001)
				tryfitwidthonce();
		};
	}

	public void Load(string path) {
		path = Path.GetFullPath(path);
		FilePath = path;
		Title = Path.GetFileName(path);
		endpan(cancel: true);
		rotQuarter = 0;
		rotate.Angle = 0;
		zoom = BASE_ZOOM;
		applyzoom();
		pixelW = 0;
		pixelH = 0;
		try {
			bmp = loadbitmap(path);
			if (bmp == null) throw new InvalidOperationException("无法解码图片");
			pixelW = bmp.PixelWidth;
			pixelH = bmp.PixelHeight;
			img.Source = bmp;
			img.Visibility = Visibility.Visible;
			lberr.Visibility = Visibility.Collapsed;
			DocViewLog.Info($"Image Load {pixelW}x{pixelH} path={path}");
		} catch (Exception ex) {
			bmp = null;
			img.Source = null;
			img.Visibility = Visibility.Collapsed;
			lberr.Text = "无法打开图片:\n" + ex.Message;
			lberr.Visibility = Visibility.Visible;
			DocViewLog.Warn($"Image Load fail: {ex.Message} path={path}");
		}
		StatusChanged?.Invoke();
	}

	static BitmapSource loadbitmap(string path) {
		using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
			var bmp = new BitmapImage();
			bmp.BeginInit();
			bmp.CacheOption = BitmapCacheOption.OnLoad;
			bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
			bmp.StreamSource = fs;
			bmp.EndInit();
			if (bmp.CanFreeze) bmp.Freeze();
			return bmp;
		}
	}

	public void SetZoom(double z) {
		zoomat(z, null);
	}

	/// <summary>缩放到 z；若给定 scroller 内坐标，则保持该点内容不漂。</summary>
	void zoomat(double z, Point? pivotInScroller) {
		z = clamp(z, MIN_ZOOM, MAX_ZOOM);
		if (Math.Abs(z - zoom) < 1e-9) return;
		var before = zoom;
		Point? pivot = pivotInScroller;
		double contentX = 0, contentY = 0;
		if (pivot != null) {
			contentX = scroller.HorizontalOffset + pivot.Value.X;
			contentY = scroller.VerticalOffset + pivot.Value.Y;
		}
		zoom = z;
		applyzoom();
		try {
			scroller.UpdateLayout();
			if (pivot != null && before > 1e-9) {
				var ratio = zoom / before;
				scroller.ScrollToHorizontalOffset(Math.Max(0, contentX * ratio - pivot.Value.X));
				scroller.ScrollToVerticalOffset(Math.Max(0, contentY * ratio - pivot.Value.Y));
			}
		} catch { /* ignore */ }
		StatusChanged?.Invoke();
	}

	public void ZoomBy(double factor) => SetZoom(zoom * factor);
	public void ZoomIn() => SetZoom(zoom * WHEEL_FACTOR);
	public void ZoomOut() => SetZoom(zoom / WHEEL_FACTOR);

	public void ZoomFitWidth() {
		if (bmp == null) { SetZoom(1); return; }
		try {
			root.UpdateLayout();
			var vw = scroller.ViewportWidth;
			if (vw < 40) vw = root.ActualWidth;
			if (vw < 40) { SetZoom(1); return; }
			var (w, _) = displaysize();
			if (w < 1) { SetZoom(1); return; }
			SetZoom(vw / w);
		} catch {
			SetZoom(1);
		}
	}

	public void ZoomFitPage() {
		if (bmp == null) { SetZoom(1); return; }
		try {
			root.UpdateLayout();
			var vw = scroller.ViewportWidth;
			var vh = scroller.ViewportHeight;
			if (vw < 40) vw = root.ActualWidth;
			if (vh < 40) vh = root.ActualHeight;
			if (vw < 40 || vh < 40) { SetZoom(1); return; }
			var (w, h) = displaysize();
			if (w < 1 || h < 1) { SetZoom(1); return; }
			SetZoom(Math.Min(vw / w, vh / h));
		} catch {
			SetZoom(1);
		}
	}

	public void RotateBy(int deltaQuarterTurns) {
		if (deltaQuarterTurns == 0) return;
		endpan(cancel: true);
		rotQuarter = (rotQuarter + deltaQuarterTurns) % 4;
		if (rotQuarter < 0) rotQuarter += 4;
		rotate.Angle = rotQuarter * 90;
		try {
			root.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
				try { ZoomFitWidth(); } catch { /* ignore */ }
			}));
		} catch { /* ignore */ }
		StatusChanged?.Invoke();
	}

	public void GoPrevPage() { }
	public void GoNextPage() { }
	public void GoToPage(int page1Based) { }
	public void SetSidePanelVisible(bool show) { }

	public void CaptureViewState(out double h, out double v, out double z, out int sheetOrPage) {
		h = scroller.HorizontalOffset;
		v = scroller.VerticalOffset;
		z = zoom;
		sheetOrPage = rotQuarter;
	}

	public void RestoreViewState(double h, double v, double z, int sheetOrPage) {
		if (sheetOrPage >= 0 && sheetOrPage <= 3) {
			rotQuarter = sheetOrPage;
			rotate.Angle = rotQuarter * 90;
		}
		if (z > 0.01) SetZoom(z);
		try {
			root.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
				try {
					scroller.ScrollToHorizontalOffset(h);
					scroller.ScrollToVerticalOffset(v);
				} catch { /* ignore */ }
			}));
		} catch { /* ignore */ }
	}

	public bool TryCopySelection() {
		try {
			if (bmp == null) return false;
			Clipboard.SetImage(bmp);
			return true;
		} catch { return false; }
	}

	/// <summary>图片另存为 png/jpg/bmp。</summary>
	public bool SaveAs(string path) {
		if (bmp == null || string.IsNullOrWhiteSpace(path)) return false;
		try {
			path = Path.GetFullPath(path);
			var ext = Path.GetExtension(path).ToLowerInvariant();
			BitmapEncoder enc;
			if (ext == ".jpg" || ext == ".jpeg")
				enc = new JpegBitmapEncoder { QualityLevel = 92 };
			else if (ext == ".bmp")
				enc = new BmpBitmapEncoder();
			else {
				if (ext != ".png")
					path = Path.ChangeExtension(path, ".png");
				enc = new PngBitmapEncoder();
			}
			enc.Frames.Add(BitmapFrame.Create(bmp));
			var dir = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
				Directory.CreateDirectory(dir);
			using (var fs = File.Create(path))
				enc.Save(fs);
			DocViewLog.Info($"Image SaveAs path={path}");
			return true;
		} catch (Exception ex) {
			DocViewLog.Warn($"Image SaveAs: {ex.Message}");
			return false;
		}
	}

	public FindResult Find(string text, bool forward, bool ignoreCase, bool restart = false, bool fromView = false) =>
		FindResult.Miss();

	public void ClearFind() { }

	public void Dispose() {
		endpan(cancel: true);
		img.Source = null;
		bmp = null;
	}

	void applyzoom() {
		scale.ScaleX = zoom;
		scale.ScaleY = zoom;
	}

	/// <summary>当前旋转下的显示像素尺寸（未缩放）。</summary>
	(double w, double h) displaysize() {
		if (bmp == null) return (0, 0);
		var w = (double)pixelW;
		var h = (double)pixelH;
		if (rotQuarter % 2 != 0) {
			var t = w; w = h; h = t;
		}
		return (w, h);
	}

	void tryfitwidthonce() {
		try {
			if (bmp == null) return;
			root.UpdateLayout();
			var vw = scroller.ViewportWidth;
			if (vw < 80) return;
			var (w, _) = displaysize();
			if (w <= 0) return;
			if (w > vw)
				SetZoom(vw / w);
		} catch { /* ignore */ }
	}

	// ---------- 滚轮缩放（以光标为中心；Ctrl 可选，不按也缩放） ----------
	void onwheel(object sender, MouseWheelEventArgs e) {
		if (bmp == null) return;
		// Shift+滚轮：横向平移（内容溢出时）
		if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) {
			try {
				var dx = e.Delta > 0 ? -80 : 80;
				scroller.ScrollToHorizontalOffset(Math.Max(0, scroller.HorizontalOffset + dx));
			} catch { /* ignore */ }
			e.Handled = true;
			return;
		}
		try {
			var pos = e.GetPosition(scroller);
			var factor = e.Delta > 0 ? WHEEL_FACTOR : 1.0 / WHEEL_FACTOR;
			zoomat(zoom * factor, pos);
		} catch {
			if (e.Delta > 0) ZoomIn();
			else ZoomOut();
		}
		e.Handled = true;
	}

	// ---------- 拖拽平移 / 双击 ----------
	void onmousedown(object sender, MouseButtonEventArgs e) {
		if (bmp == null) return;
		if (e.ChangedButton != MouseButton.Left && e.ChangedButton != MouseButton.Middle)
			return;
		// 左键双击：适应窗口 ⇄ 100%
		if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2) {
			endpan(cancel: true);
			try {
				root.UpdateLayout();
				var vw = scroller.ViewportWidth;
				var vh = scroller.ViewportHeight;
				if (vw >= 40 && vh >= 40) {
					var (w, h) = displaysize();
					if (w >= 1 && h >= 1) {
						var fit = Math.Min(vw / w, vh / h);
						if (Math.Abs(zoom - fit) / Math.Max(fit, 0.01) < 0.08)
							zoomat(1.0, e.GetPosition(scroller));
						else
							ZoomFitPage();
					}
				}
			} catch { /* ignore */ }
			e.Handled = true;
			return;
		}
		if (e.ClickCount > 1) return;
		try {
			panArmed = true;
			panning = false;
			panButton = e.ChangedButton;
			panStart = e.GetPosition(scroller);
			panH0 = scroller.HorizontalOffset;
			panV0 = scroller.VerticalOffset;
			scroller.CaptureMouse();
			scroller.Cursor = Cursors.Hand;
			scroller.Focus();
			e.Handled = true;
		} catch { /* ignore */ }
	}

	void onmousemove(object sender, MouseEventArgs e) {
		if (!panArmed || bmp == null) return;
		try {
			var pos = e.GetPosition(scroller);
			var dx = pos.X - panStart.X;
			var dy = pos.Y - panStart.Y;
			if (!panning) {
				if (Math.Abs(dx) < PAN_SLOP && Math.Abs(dy) < PAN_SLOP) return;
				panning = true;
				scroller.Cursor = Cursors.SizeAll;
			}
			// 拖图片：鼠标右移 → 内容右移 → 滚动条左移
			scroller.ScrollToHorizontalOffset(Math.Max(0, panH0 - dx));
			scroller.ScrollToVerticalOffset(Math.Max(0, panV0 - dy));
			e.Handled = true;
		} catch { /* ignore */ }
	}

	void onmouseup(object sender, MouseButtonEventArgs e) {
		if (!panArmed) return;
		if (e.ChangedButton != panButton) return;
		endpan(cancel: false);
		e.Handled = true;
	}

	void endpan(bool cancel) {
		if (!panArmed && !panning) return;
		panArmed = false;
		panning = false;
		try {
			if (scroller.IsMouseCaptured)
				scroller.ReleaseMouseCapture();
		} catch { /* ignore */ }
		scroller.Cursor = Cursors.Arrow;
	}

	static double clamp(double v, double a, double b) {
		if (v < a) return a;
		if (v > b) return b;
		return v;
	}
}
