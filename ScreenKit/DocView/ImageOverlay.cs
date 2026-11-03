using System;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ScreenKit.DocView;

/// <summary>
/// 文档区图片预览：无边框弹层对齐文档区（pcontent），半透明背景 + fit。
/// 使用顶层 Window 而非 WPF 子元素，避免 WebView2 HWND airspace 把预览盖住。
/// </summary>
static class ImageOverlay {
	const double MIN_ZOOM = 0.05;
	const double MAX_ZOOM = 32.0;
	const double WHEEL_FACTOR = 1.12;

	static OverlayWindow current;

	public static bool IsOpen => current != null && current.IsVisible;

	/// <summary>给 Image 挂上双击预览与右键菜单（复制/复制为文件/保存）。</summary>
	public static void Wire(Image img, BitmapSource bmp = null, string filePath = null) {
		if (img == null) return;
		var src = bmp ?? img.Source as BitmapSource;
		if (src == null) return;
		if (img.Cursor == null || img.Cursor == Cursors.Arrow)
			img.Cursor = Cursors.Hand;
		var tip = img.ToolTip as string;
		if (string.IsNullOrEmpty(tip))
			img.ToolTip = "双击预览 · 右键复制/保存";
		else if (tip.IndexOf("双击", StringComparison.Ordinal) < 0)
			img.ToolTip = tip + "\n双击预览 · 右键复制/保存";
		if (!string.IsNullOrEmpty(filePath))
			img.Tag = filePath;
		img.MouseLeftButtonDown += (s, e) => {
			if (e.Handled || e.ClickCount != 2) return;
			e.Handled = true;
			var bs = bmp ?? (s as Image)?.Source as BitmapSource;
			var path = filePath ?? (s as Image)?.Tag as string;
			if (bs != null) Show(bs, null, path);
		};
		attachctxmenu(img, src, filePath);
	}

	static void attachctxmenu(Image img, BitmapSource bmp, string filePath) {
		var cm = new ContextMenu();
		var miCopy = new MenuItem { Header = "复制图片" };
		miCopy.Click += (_, _) => {
			var bs = bmp ?? img.Source as BitmapSource;
			CopyImage(bs);
		};
		var miFile = new MenuItem { Header = "复制为文件" };
		miFile.Click += (_, _) => {
			var path = filePath ?? img.Tag as string;
			var bs = bmp ?? img.Source as BitmapSource;
			CopyAsFile(path, bs);
		};
		var miSave = new MenuItem { Header = "保存图片..." };
		miSave.Click += (_, _) => {
			var path = filePath ?? img.Tag as string;
			var bs = bmp ?? img.Source as BitmapSource;
			SaveImageAs(Window.GetWindow(img), bs, path);
		};
		cm.Items.Add(miCopy);
		cm.Items.Add(miFile);
		cm.Items.Add(miSave);
		img.ContextMenu = cm;
	}

	/// <summary>位图 → 剪贴板图片（SetDataObject 拷贝，兼容 Word/画图等）。</summary>
	public static bool CopyImage(BitmapSource bmp) {
		if (bmp == null) return false;
		try {
			var ready = toclipboardbitmap(bmp);
			var data = new DataObject();
			data.SetImage(ready);
			// 额外挂 PNG，部分应用只认此格式
			try {
				using (var ms = new MemoryStream()) {
					var enc = new PngBitmapEncoder();
					enc.Frames.Add(BitmapFrame.Create(ready));
					enc.Save(ms);
					data.SetData("PNG", ms.ToArray(), false);
				}
			} catch { /* PNG 可选 */ }
			setclipboard(data);
			DocViewLog.Info($"ImageOverlay copy image {ready.PixelWidth}x{ready.PixelHeight}");
			return true;
		} catch (Exception ex) {
			DocViewLog.Warn($"ImageOverlay.CopyImage: {ex.Message}");
			MessageBox.Show("复制图片失败: " + ex.Message, "DocviewWPF",
				MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}
	}

	/// <summary>
	/// 复制为文件（Explorer 粘贴）：优先本地路径；否则写入临时 PNG 再 FileDrop。
	/// </summary>
	public static bool CopyAsFile(string filePath, BitmapSource bmp = null, string suggestName = null) {
		try {
			string path = null;
			if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
				path = Path.GetFullPath(filePath);
			else if (bmp != null) {
				var dir = Path.Combine(Path.GetTempPath(), "DocviewWPF");
				Directory.CreateDirectory(dir);
				var name = suggestsfilename(suggestName, filePath);
				path = uniquepath(Path.Combine(dir, name));
				SaveBitmap(toclipboardbitmap(bmp), path);
			}
			if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
				MessageBox.Show("无法复制为文件：没有可用的图片数据。", "DocviewWPF",
					MessageBoxButton.OK, MessageBoxImage.Warning);
				return false;
			}
			var col = new StringCollection { path };
			var data = new DataObject();
			data.SetFileDropList(col);
			// Explorer「粘贴」需要 Preferred DropEffect = COPY(1)
			try {
				data.SetData("Preferred DropEffect", new MemoryStream(new byte[] { 5, 0, 0, 0 }));
			} catch { /* ignore */ }
			setclipboard(data);
			DocViewLog.Info("ImageOverlay copy as file: " + path);
			return true;
		} catch (Exception ex) {
			DocViewLog.Warn($"ImageOverlay.CopyAsFile: {ex.Message}");
			MessageBox.Show("复制为文件失败: " + ex.Message, "DocviewWPF",
				MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}
	}

	/// <summary>剪贴板写入（拷贝语义 + 短重试，避免 COM 占用）。</summary>
	static void setclipboard(DataObject data) {
		Exception last = null;
		for (var i = 0; i < 6; i++) {
			try {
				Clipboard.SetDataObject(data, true);
				return;
			} catch (Exception ex) {
				last = ex;
				try { System.Threading.Thread.Sleep(40 + i * 20); } catch { /* ignore */ }
			}
		}
		if (last != null) throw last;
	}

	/// <summary>转成可冻结的内存位图，避免 UriSource/Stream 位图剪贴板空数据。</summary>
	static BitmapSource toclipboardbitmap(BitmapSource src) {
		if (src == null) return null;
		try {
			if (!src.IsFrozen && src.CanFreeze) src.Freeze();
		} catch { /* ignore */ }
		try {
			var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
			var wb = new WriteableBitmap(conv);
			wb.Freeze();
			return wb;
		} catch {
			return src;
		}
	}

	/// <summary>另存为对话框。</summary>
	public static bool SaveImageAs(Window owner, BitmapSource bmp, string sourcePath = null) {
		if (bmp == null) {
			MessageBox.Show("没有可保存的图片。", "DocviewWPF",
				MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}
		try {
			var dlg = new SaveFileDialog {
				Filter = "PNG 图片|*.png|JPEG 图片|*.jpg;*.jpeg|BMP 图片|*.bmp|所有文件|*.*",
				DefaultExt = ".png",
				Title = "保存图片",
				FileName = suggestsfilename(null, sourcePath),
			};
			try {
				if (!string.IsNullOrEmpty(sourcePath))
					dlg.InitialDirectory = Path.GetDirectoryName(sourcePath) ?? "";
			} catch { /* ignore */ }
			var ok = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
			if (ok != true) return false;
			SaveBitmap(bmp, dlg.FileName);
			DocViewLog.Info("ImageOverlay save: " + dlg.FileName);
			return true;
		} catch (Exception ex) {
			DocViewLog.Warn($"ImageOverlay.SaveImageAs: {ex.Message}");
			MessageBox.Show("保存失败: " + ex.Message, "DocviewWPF",
				MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}
	}

	public static BitmapSource LoadFile(string path) {
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
		return loadfile(path);
	}

	public static BitmapSource LoadUri(string src) {
		if (string.IsNullOrWhiteSpace(src)) return null;
		try { return loaduri(src.Trim()); }
		catch { return null; }
	}

	public static void SaveBitmap(BitmapSource src, string path) {
		if (src == null || string.IsNullOrWhiteSpace(path)) return;
		var ext = Path.GetExtension(path)?.ToLowerInvariant() ?? ".png";
		BitmapEncoder enc;
		if (ext == ".jpg" || ext == ".jpeg")
			enc = new JpegBitmapEncoder { QualityLevel = 92 };
		else if (ext == ".bmp")
			enc = new BmpBitmapEncoder();
		else
			enc = new PngBitmapEncoder();
		enc.Frames.Add(BitmapFrame.Create(src));
		using (var fs = File.Create(path))
			enc.Save(fs);
	}

	static string suggestsfilename(string suggestName, string sourcePath) {
		try {
			if (!string.IsNullOrEmpty(sourcePath))
				return Path.GetFileName(sourcePath);
		} catch { /* ignore */ }
		var t = suggestName ?? "image.png";
		foreach (var c in Path.GetInvalidFileNameChars())
			t = t.Replace(c, '_');
		if (string.IsNullOrWhiteSpace(t)) t = "image.png";
		if (string.IsNullOrEmpty(Path.GetExtension(t)))
			t += ".png";
		return t;
	}

	static string uniquepath(string path) {
		if (!File.Exists(path)) return path;
		var dir = Path.GetDirectoryName(path) ?? "";
		var name = Path.GetFileNameWithoutExtension(path);
		var ext = Path.GetExtension(path);
		for (var i = 1; i < 1000; i++) {
			var p = Path.Combine(dir, $"{name}_{i}{ext}");
			if (!File.Exists(p)) return p;
		}
		return Path.Combine(dir, $"{name}_{Environment.TickCount}{ext}");
	}

	public static void ShowFile(string path, string title = null) {
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
		try {
			var bmp = loadfile(path);
			if (bmp != null)
				Show(bmp, title ?? Path.GetFileName(path), path);
		} catch (Exception ex) {
			DocViewLog.Warn($"ImageOverlay.ShowFile: {ex.Message}");
		}
	}

	public static void ShowUriOrPath(string src, string title = null) {
		if (string.IsNullOrWhiteSpace(src)) return;
		src = src.Trim();
		try {
			if (File.Exists(src)) {
				ShowFile(src, title);
				return;
			}
			if (src.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) {
				var local = new Uri(src).LocalPath;
				if (File.Exists(local)) {
					ShowFile(local, title);
					return;
				}
			}
			var bmp = loaduri(src);
			if (bmp != null) Show(bmp, title);
		} catch (Exception ex) {
			DocViewLog.Warn($"ImageOverlay.ShowUriOrPath: {ex.Message}");
		}
	}

	public static void Show(BitmapSource bmp, string title = null, string sourcePath = null) {
		if (bmp == null) return;
		try {
			if (!bmp.IsFrozen && bmp.CanFreeze) bmp.Freeze();
		} catch { /* ignore */ }

		try { current?.Close(); } catch { /* ignore */ }
		current = null;

		Window owner = null;
		FrameworkElement anchor = null;
		try {
			MainWindow main = null;
			foreach (Window w in Application.Current.Windows) {
				if (w is MainWindow mw && mw.IsActive) { main = mw; break; }
			}
			if (main == null) {
				foreach (Window w in Application.Current.Windows) {
					if (w is MainWindow mw) { main = mw; break; }
				}
			}
			owner = main ?? Application.Current?.MainWindow;
			anchor = main?.DocOverlayHost as FrameworkElement ?? owner as FrameworkElement;
		} catch { /* ignore */ }

		var win = new OverlayWindow(bmp, title ?? "图片预览", sourcePath, owner, anchor);
		current = win;
		win.Closed += (_, _) => {
			if (ReferenceEquals(current, win)) current = null;
		};
		try {
			win.Show(); // 非模态，避免堵主窗
			win.Activate();
		} catch (Exception ex) {
			DocViewLog.Warn($"ImageOverlay.Show: {ex.Message}");
		}
		DocViewLog.Info($"ImageOverlay show title={title} {bmp.PixelWidth}x{bmp.PixelHeight}");
	}

	public static void CloseIfOpen() {
		try { current?.Close(); } catch { /* ignore */ }
		current = null;
	}

	static BitmapSource loadfile(string path) {
		using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
			var bi = new BitmapImage();
			bi.BeginInit();
			bi.CacheOption = BitmapCacheOption.OnLoad;
			bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
			bi.StreamSource = fs;
			bi.EndInit();
			if (bi.CanFreeze) bi.Freeze();
			return bi;
		}
	}

	static BitmapSource loaduri(string src) {
		var bi = new BitmapImage();
		bi.BeginInit();
		bi.CacheOption = BitmapCacheOption.OnLoad;
		bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
		bi.UriSource = new Uri(src, UriKind.RelativeOrAbsolute);
		bi.EndInit();
		if (bi.CanFreeze) bi.Freeze();
		return bi;
	}

	// ---------- 对齐文档区的无边框弹层 ----------
	sealed class OverlayWindow : Window {
		readonly BitmapSource bmp;
		readonly string sourcePath;
		readonly FrameworkElement anchor;
		readonly Window ownerWin;
		readonly Image img;
		readonly ScaleTransform scaleTf;
		readonly TranslateTransform panTf;
		readonly TextBlock lbtitle;
		readonly TextBlock lbzoom;
		readonly Grid stage;

		const double PAN_SLOP = 4; // 超过此像素才算拖拽，否则视为单击

		double zoom = 1;
		int rotDeg; // 0/90/180/270
		bool flipH, flipV;
		bool pressArmed;   // 左键已按下
		bool panning;      // 已超过 slop，正在平移
		bool pressOnImage; // 按下时是否在图片上（背景单击才关闭）
		Point panStart;
		double panX0, panY0;
		bool didFit;
		EventHandler locHandler;
		SizeChangedEventHandler sizeHandler;
		EventHandler stateHandler;

		public OverlayWindow(BitmapSource source, string title, string path, Window owner, FrameworkElement host) {
			bmp = source;
			sourcePath = path;
			ownerWin = owner;
			anchor = host;
			Title = title ?? "图片预览";
			Owner = owner;
			WindowStyle = WindowStyle.None;
			ResizeMode = ResizeMode.NoResize;
			ShowInTaskbar = false;
			AllowsTransparency = true;
			Background = Brushes.Transparent;
			// 半透明整窗底
			var chromeBg = new SolidColorBrush(Color.FromArgb(0xE0, 0x0F, 0x0F, 0x12));
			Focusable = true;
			ShowActivated = true;
			WindowStartupLocation = WindowStartupLocation.Manual;

			// 布局：Canvas 显式定位图片（宽高=像素*zoom），杜绝溢出
			scaleTf = new ScaleTransform(1, 1); // 仅镜像
			panTf = new TranslateTransform(0, 0); // 不用；用 Canvas.Left/Top
			img = new Image {
				Source = bmp,
				Stretch = Stretch.Fill,
				SnapsToDevicePixels = true,
				RenderTransform = scaleTf,
				RenderTransformOrigin = new Point(0.5, 0.5),
			};
			RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

			stage = new Grid {
				ClipToBounds = true,
				Background = chromeBg,
			};
			// 用 Canvas 做舞台，便于居中 + pan
			var canvas = new Canvas { ClipToBounds = true, Background = Brushes.Transparent };
			canvas.Children.Add(img);
			stage.Children.Add(canvas);
			// 把 canvas 存到 Tag 方便定位
			stage.Tag = canvas;

			lbtitle = new TextBlock {
				Text = title ?? "",
				Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
				FontSize = 13,
				VerticalAlignment = VerticalAlignment.Center,
				TextTrimming = TextTrimming.CharacterEllipsis,
				Margin = new Thickness(12, 0, 8, 0),
			};
			var bclose = toolbtn("✕", "关闭 (Esc)");
			bclose.Click += (_, _) => Close();
			var top = new DockPanel {
				Height = 40,
				Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x00, 0x00, 0x00)),
			};
			DockPanel.SetDock(bclose, Dock.Right);
			top.Children.Add(bclose);
			top.Children.Add(lbtitle);

			lbzoom = new TextBlock {
				Text = "100%",
				Foreground = new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB)),
				FontSize = 12,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(8, 0, 8, 0),
				MinWidth = 48,
				TextAlignment = TextAlignment.Center,
			};
			var bar = new StackPanel {
				Orientation = Orientation.Horizontal,
				HorizontalAlignment = HorizontalAlignment.Center,
				Margin = new Thickness(0, 6, 0, 8),
			};
			void add(Button b) {
				b.Margin = new Thickness(2, 0, 2, 0);
				bar.Children.Add(b);
			}
			var bzout = toolbtn("－", "缩小");
			bzout.Click += (_, _) => zoomby(1.0 / WHEEL_FACTOR);
			var bzin = toolbtn("＋", "放大");
			bzin.Click += (_, _) => zoomby(WHEEL_FACTOR);
			var bfit = toolbtn("⛶", "适合区域");
			bfit.Click += (_, _) => FitToHost();
			var b1 = toolbtn("1:1", "原始大小");
			b1.Click += (_, _) => { zoom = 1; centerimage(); applyxf(); };
			var brol = toolbtn("↺", "左转");
			brol.Click += (_, _) => rotateby(-90);
			var bror = toolbtn("↻", "右转");
			bror.Click += (_, _) => rotateby(90);
			var bcopy = toolbtn("复制", "Ctrl+C");
			bcopy.Width = 48;
			bcopy.Click += (_, _) => copyimage();
			var bsave = toolbtn("另存", "Ctrl+S");
			bsave.Width = 48;
			bsave.Click += (_, _) => saveimage();
			var breset = toolbtn("⟲", "重置");
			breset.Click += (_, _) => { flipH = flipV = false; rotDeg = 0; FitToHost(); };
			add(bzout);
			add(bzin);
			bar.Children.Add(lbzoom);
			add(bfit);
			add(b1);
			add(brol);
			add(bror);
			add(bcopy);
			add(bsave);
			add(breset);

			var bottom = new Border {
				Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x00, 0x00, 0x00)),
				Padding = new Thickness(8, 4, 8, 4),
				Child = bar,
			};

			var root = new DockPanel { Background = Brushes.Transparent, LastChildFill = true };
			DockPanel.SetDock(top, Dock.Top);
			DockPanel.SetDock(bottom, Dock.Bottom);
			root.Children.Add(top);
			root.Children.Add(bottom);
			root.Children.Add(stage);
			Content = root;

			// 交互：图片/背景均可拖 pan；背景上「纯单击」才关闭
			canvas.PreviewMouseWheel += onwheel;
			canvas.PreviewMouseLeftButtonDown += ondown;
			canvas.PreviewMouseLeftButtonUp += onup;
			canvas.PreviewMouseMove += onmove;
			canvas.LostMouseCapture += (_, _) => endpress(cancel: true);
			stage.PreviewMouseLeftButtonDown += ondown;
			stage.PreviewMouseLeftButtonUp += onup;
			stage.PreviewMouseMove += onmove;
			PreviewKeyDown += onkey;

			Loaded += (_, _) => {
				syncbounds();
				Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
					syncbounds();
					FitToHost();
					try { Focus(); } catch { /* ignore */ }
				}));
			};
			stage.SizeChanged += (_, e) => {
				if (e.NewSize.Width > 40 && e.NewSize.Height > 40 && !didFit) {
					didFit = true;
					FitToHost();
				} else if (didFit) {
					// 宿主尺寸变：保持居中意图，重新 fit 可选；仅重定位
					centerimage();
				}
			};

			// 跟随主窗移动/缩放
			wireowner();
			syncbounds();
		}

		void wireowner() {
			if (ownerWin == null) return;
			locHandler = (_, _) => syncbounds();
			sizeHandler = (_, _) => syncbounds();
			stateHandler = (_, _) => {
				if (ownerWin.WindowState == WindowState.Minimized)
					WindowState = WindowState.Minimized;
				else {
					if (WindowState == WindowState.Minimized)
						WindowState = WindowState.Normal;
					syncbounds();
				}
			};
			ownerWin.LocationChanged += locHandler;
			ownerWin.SizeChanged += sizeHandler;
			ownerWin.StateChanged += stateHandler;
			if (anchor != null)
				anchor.LayoutUpdated += onanchorlayout;
			Closing += (_, _) => unwireowner();
		}

		void unwireowner() {
			try {
				if (ownerWin != null) {
					if (locHandler != null) ownerWin.LocationChanged -= locHandler;
					if (sizeHandler != null) ownerWin.SizeChanged -= sizeHandler;
					if (stateHandler != null) ownerWin.StateChanged -= stateHandler;
				}
				if (anchor != null)
					anchor.LayoutUpdated -= onanchorlayout;
			} catch { /* ignore */ }
		}

		int layoutTick;
		void onanchorlayout(object sender, EventArgs e) {
			// 防抖：布局风暴时合并
			var t = Environment.TickCount;
			if (t - layoutTick < 50) return;
			layoutTick = t;
			try { syncbounds(); } catch { /* ignore */ }
		}

		/// <summary>弹层矩形 = 文档区（pcontent）屏幕坐标。</summary>
		void syncbounds() {
			try {
				var fe = anchor ?? ownerWin as FrameworkElement;
				if (fe == null || !fe.IsVisible) return;
				if (fe.ActualWidth < 20 || fe.ActualHeight < 20) return;
				var tl = fe.PointToScreen(new Point(0, 0));
				var br = fe.PointToScreen(new Point(fe.ActualWidth, fe.ActualHeight));
				// 感知 DPI：PointToScreen 已是设备像素，WPF Window Left/Top 是 DIP
				var src = PresentationSource.FromVisual(fe)
					?? (ownerWin != null ? PresentationSource.FromVisual(ownerWin) : null);
				var fromDev = src?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
				var tlDip = fromDev.Transform(tl);
				var brDip = fromDev.Transform(br);
				Left = tlDip.X;
				Top = tlDip.Y;
				Width = Math.Max(200, brDip.X - tlDip.X);
				Height = Math.Max(160, brDip.Y - tlDip.Y);
			} catch (Exception ex) {
				DocViewLog.Warn("ImageOverlay syncbounds: " + ex.Message);
			}
		}

		Canvas canvas => stage.Tag as Canvas;

		public void FitToHost() {
			try {
				UpdateLayout();
				var cv = canvas;
				var vw = cv?.ActualWidth ?? stage.ActualWidth;
				var vh = cv?.ActualHeight ?? stage.ActualHeight;
				if (vw < 40) vw = Math.Max(40, ActualWidth);
				if (vh < 40) vh = Math.Max(40, ActualHeight - 88);
				var (pw, ph) = contentsize();
				if (pw < 1 || ph < 1) return;
				vw = Math.Max(40, vw - 24);
				vh = Math.Max(40, vh - 24);
				zoom = clamp(Math.Min(vw / pw, vh / ph), MIN_ZOOM, MAX_ZOOM);
				didFit = true;
				applyxf();
				centerimage();
			} catch {
				zoom = 1;
				applyxf();
				centerimage();
			}
		}

		/// <summary>内容逻辑尺寸（含旋转后的宽高）。</summary>
		(double w, double h) contentsize() {
			var w = (double)Math.Max(1, bmp.PixelWidth);
			var h = (double)Math.Max(1, bmp.PixelHeight);
			if (rotDeg % 180 != 0) return (h, w);
			return (w, h);
		}

		void applyxf() {
			var (cw, ch) = contentsize();
			// 显示框 = 内容 * zoom
			var dw = cw * zoom;
			var dh = ch * zoom;
			img.Width = dw;
			img.Height = dh;
			// 旋转：用 LayoutTransform 在固定宽高盒子里转，盒子已是旋转后尺寸
			// 更简单：不旋转 Image 控件几何，用 LayoutTransform 旋转源
			var rot = new RotateTransform(rotDeg);
			var sc = new ScaleTransform(flipH ? -1 : 1, flipV ? -1 : 1);
			var tg = new TransformGroup();
			// 先镜像再旋转
			tg.Children.Add(sc);
			tg.Children.Add(rot);
			// 原图像素对应未旋转尺寸
			img.Width = bmp.PixelWidth * zoom;
			img.Height = bmp.PixelHeight * zoom;
			img.LayoutTransform = tg;
			// 旋转后包围盒与 contentsize*zoom 一致，居中用包围盒
			updatelabel();
			centerimage();
		}

		void centerimage() {
			var cv = canvas;
			if (cv == null) return;
			cv.UpdateLayout();
			img.UpdateLayout();
			// LayoutTransform 后 DesiredSize / RenderSize
			var iw = img.RenderSize.Width;
			var ih = img.RenderSize.Height;
			if (iw < 1 || ih < 1) {
				// 回退逻辑尺寸
				var (cw, ch) = contentsize();
				iw = cw * zoom;
				ih = ch * zoom;
			}
			var vw = cv.ActualWidth;
			var vh = cv.ActualHeight;
			if (vw < 1 || vh < 1) return;
			Canvas.SetLeft(img, (vw - iw) * 0.5 + panTf.X);
			Canvas.SetTop(img, (vh - ih) * 0.5 + panTf.Y);
		}

		void updatelabel() {
			if (lbzoom == null) return;
			var rot = rotDeg != 0 ? $"  {rotDeg}°" : "";
			var fl = (flipH ? " H" : "") + (flipV ? " V" : "");
			lbzoom.Text = $"{(int)Math.Round(zoom * 100)}%{rot}{fl}";
		}

		void zoomby(double factor) {
			var z = clamp(zoom * factor, MIN_ZOOM, MAX_ZOOM);
			if (Math.Abs(z - zoom) < 1e-9) return;
			zoom = z;
			applyxf();
		}

		void rotateby(int d) {
			rotDeg = (rotDeg + d) % 360;
			if (rotDeg < 0) rotDeg += 360;
			panTf.X = 0;
			panTf.Y = 0;
			FitToHost();
		}

		void onwheel(object sender, MouseWheelEventArgs e) {
			zoomby(e.Delta > 0 ? WHEEL_FACTOR : 1.0 / WHEEL_FACTOR);
			e.Handled = true;
		}

		void ondown(object sender, MouseButtonEventArgs e) {
			if (e.ChangedButton != MouseButton.Left || e.ClickCount >= 2) return;
			// 按下：记录起点；背景/图片均可后续 pan，松手时再决定是否关闭
			pressArmed = true;
			panning = false;
			pressOnImage = ishitonimage(e);
			panStart = e.GetPosition(canvas);
			panX0 = panTf.X;
			panY0 = panTf.Y;
			try { Mouse.Capture(canvas); } catch { /* ignore */ }
			e.Handled = true;
		}

		bool ishitonimage(MouseEventArgs e) {
			try {
				var d = e.OriginalSource as DependencyObject;
				while (d != null) {
					if (ReferenceEquals(d, img)) return true;
					if (d is Visual || d is System.Windows.Media.Media3D.Visual3D)
						d = VisualTreeHelper.GetParent(d);
					else
						d = LogicalTreeHelper.GetParent(d);
				}
				var p = e.GetPosition(img);
				return p.X >= 0 && p.Y >= 0
					&& p.X <= img.RenderSize.Width && p.Y <= img.RenderSize.Height
					&& img.RenderSize.Width > 0;
			} catch {
				return false;
			}
		}

		void onmove(object sender, MouseEventArgs e) {
			if (!pressArmed) return;
			var p = e.GetPosition(canvas);
			var dx = p.X - panStart.X;
			var dy = p.Y - panStart.Y;
			if (!panning) {
				if (dx * dx + dy * dy < PAN_SLOP * PAN_SLOP) return;
				// 超过阈值 → 进入 pan（背景/图片均可）
				panning = true;
				Cursor = Cursors.Hand;
			}
			panTf.X = panX0 + dx;
			panTf.Y = panY0 + dy;
			centerimage();
			e.Handled = true;
		}

		void onup(object sender, MouseButtonEventArgs e) {
			if (!pressArmed) return;
			// 纯单击背景（未拖拽）→ 关闭
			var closeBg = !panning && !pressOnImage;
			endpress(cancel: false);
			if (closeBg) {
				Close();
			}
			e.Handled = true;
		}

		void endpress(bool cancel) {
			if (!pressArmed && !panning) return;
			pressArmed = false;
			panning = false;
			pressOnImage = false;
			try { Mouse.Capture(null); } catch { /* ignore */ }
			Cursor = Cursors.Arrow;
		}

		void onkey(object sender, KeyEventArgs e) {
			var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
			if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
			if (ctrl && e.Key == Key.C) { copyimage(); e.Handled = true; return; }
			if (ctrl && e.Key == Key.S) { saveimage(); e.Handled = true; return; }
			if (e.Key == Key.OemPlus || e.Key == Key.Add) { zoomby(WHEEL_FACTOR); e.Handled = true; }
			else if (e.Key == Key.OemMinus || e.Key == Key.Subtract) { zoomby(1.0 / WHEEL_FACTOR); e.Handled = true; }
			else if (e.Key == Key.D0 || e.Key == Key.NumPad0) { FitToHost(); e.Handled = true; }
			else if (e.Key == Key.D1 || e.Key == Key.NumPad1) { zoom = 1; panTf.X = panTf.Y = 0; applyxf(); e.Handled = true; }
			else if (e.Key == Key.OemOpenBrackets) { rotateby(-90); e.Handled = true; }
			else if (e.Key == Key.OemCloseBrackets) { rotateby(90); e.Handled = true; }
			else if (e.Key == Key.H && !ctrl) { flipH = !flipH; applyxf(); e.Handled = true; }
			else if (e.Key == Key.V && !ctrl) { flipV = !flipV; applyxf(); e.Handled = true; }
			else if (e.Key == Key.Left) { panTf.X += 40; centerimage(); e.Handled = true; }
			else if (e.Key == Key.Right) { panTf.X -= 40; centerimage(); e.Handled = true; }
			else if (e.Key == Key.Up) { panTf.Y += 40; centerimage(); e.Handled = true; }
			else if (e.Key == Key.Down) { panTf.Y -= 40; centerimage(); e.Handled = true; }
		}

		void copyimage() {
			if (ImageOverlay.CopyImage(bmp))
				lbtitle.Text = stripnote(lbtitle.Text) + "  ·  已复制";
		}

		void saveimage() {
			try {
				var dlg = new Microsoft.Win32.SaveFileDialog {
					Filter = "PNG 图片|*.png|JPEG 图片|*.jpg;*.jpeg|BMP 图片|*.bmp|所有文件|*.*",
					DefaultExt = ".png",
					Title = "图片另存为",
					FileName = suggestname(),
				};
				if (dlg.ShowDialog(this) != true) return;
				savebitmap(bmp, dlg.FileName);
				lbtitle.Text = stripnote(lbtitle.Text) + "  ·  已保存";
			} catch (Exception ex) {
				MessageBox.Show("保存失败: " + ex.Message, "DocviewWPF",
					MessageBoxButton.OK, MessageBoxImage.Warning);
			}
		}

		static string stripnote(string t) {
			if (string.IsNullOrEmpty(t)) return "";
			var i = t.IndexOf("  ·", StringComparison.Ordinal);
			return i > 0 ? t.Substring(0, i).TrimEnd() : t;
		}

		string suggestname() {
			try {
				if (!string.IsNullOrEmpty(sourcePath))
					return Path.GetFileName(sourcePath);
			} catch { /* ignore */ }
			var t = stripnote(lbtitle.Text ?? "image");
			foreach (var c in Path.GetInvalidFileNameChars())
				t = t.Replace(c, '_');
			if (string.IsNullOrWhiteSpace(t)) t = "image";
			return t.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? t : t + ".png";
		}

		static void savebitmap(BitmapSource src, string path) {
			var ext = Path.GetExtension(path)?.ToLowerInvariant() ?? ".png";
			BitmapEncoder enc;
			if (ext == ".jpg" || ext == ".jpeg")
				enc = new JpegBitmapEncoder { QualityLevel = 92 };
			else if (ext == ".bmp")
				enc = new BmpBitmapEncoder();
			else
				enc = new PngBitmapEncoder();
			enc.Frames.Add(BitmapFrame.Create(src));
			using (var fs = File.Create(path))
				enc.Save(fs);
		}

		Button toolbtn(string text, string tip) {
			var b = new Button {
				Content = text,
				ToolTip = tip,
				Width = text.Length > 2 ? 48 : 36,
				Height = 32,
				FontSize = text.Length > 2 ? 12 : 15,
				Cursor = Cursors.Hand,
				Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)),
				Background = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
				BorderThickness = new Thickness(0),
				Padding = new Thickness(4, 0, 4, 0),
			};
			b.Template = mkbtntmpl();
			return b;
		}

		static ControlTemplate mkbtntmpl() {
			var tmpl = new ControlTemplate(typeof(Button));
			var border = new FrameworkElementFactory(typeof(Border));
			border.Name = "bd";
			border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
			border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
			border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
			var cp = new FrameworkElementFactory(typeof(ContentPresenter));
			cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
			cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
			border.AppendChild(cp);
			tmpl.VisualTree = border;
			var t = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
			t.Setters.Add(new Setter(Border.BackgroundProperty,
				new SolidColorBrush(Color.FromArgb(0x88, 0x60, 0xA5, 0xFA)), "bd"));
			tmpl.Triggers.Add(t);
			return tmpl;
		}

		static double clamp(double v, double lo, double hi) =>
			v < lo ? lo : (v > hi ? hi : v);
	}
}
