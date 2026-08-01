using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using Rect = System.Windows.Rect;
using Brushes = System.Windows.Media.Brushes;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfWindow = System.Windows.Window;

namespace WpfOCR;

/// <summary>
/// 长截图：点选 HWND 窗口 → 自动向下滚动 → 拼接成长图。
/// </summary>
static class ScrollCapture {
	[StructLayout(LayoutKind.Sequential)]
	struct NativePoint { public int X, Y; }

	[StructLayout(LayoutKind.Sequential)]
	struct NativeRect { public int Left, Top, Right, Bottom; }

	const int GWL_EXSTYLE = -20;
	const int GWL_STYLE = -16;
	const int WS_EX_TOOLWINDOW = 0x00000080;
	const int WS_CHILD = 0x40000000;
	const int DWMWA_CLOAKED = 14;
	const uint SWP_SHOWWINDOW = 0x0040;
	const uint SRCCOPY = 0x00CC0020;
	const uint CAPTUREBLT = 0x40000000;
	const uint PW_RENDERFULLCONTENT = 0x00000002;
	const int WM_MOUSEWHEEL = 0x020A;
	const int WM_VSCROLL = 0x0115;
	const int SB_PAGEDOWN = 3;
	const int SB_LINEDOWN = 1;
	const int VK_NEXT = 0x22;
	const int WM_KEYDOWN = 0x0100;
	const int WM_KEYUP = 0x0101;
	const uint MOUSEEVENTF_WHEEL = 0x0800;
	static readonly IntPtr HwndTopmost = new(-1);

	const int MaxSteps = 120;
	const int MaxHeightPx = 30000;
	/// <summary>每步滚动后等待渲染（过短易截到半成品）。</summary>
	const int ScrollDelayMs = 520;
	/// <summary>目标滚动约为视口高度的比例（偏小更稳，避免一下到底）。</summary>
	const double ScrollViewportRatio = 0.42;
	/// <summary>单次滚轮刻度对应的粗略像素（系统相关，作估算）。</summary>
	const int ApproxPxPerNotch = 72;

	delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

	[DllImport("user32.dll")] static extern bool GetCursorPos(out NativePoint lpPoint);
	[DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
	[DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
	[DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
	[DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);
	[DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out NativeRect lpRect);
	[DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hWnd, ref NativePoint lpPoint);
	[DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
	[DllImport("dwmapi.dll")]
	static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
	[DllImport("user32.dll", SetLastError = true)]
	static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
	[DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
	[DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
	[DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
	[DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr hWnd);
	[DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
	[DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
	[DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
	[DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
	[DllImport("user32.dll")] static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
	[DllImport("user32.dll")] static extern bool SetCursorPos(int X, int Y);
	[DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
	[DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);

	const uint GW_CHILD = 5;
	const uint GW_HWNDNEXT = 2;
	[DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
	[DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
	[DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);
	[DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
	[DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
		IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

	public sealed class PickResult {
		public IntPtr Hwnd;
		public string Title;
		public System.Drawing.Rectangle Bounds;
	}

	/// <summary>全屏点选顶层窗口；取消返回 null。</summary>
	public static PickResult PickWindow() {
		var wins = EnumTopWindows();
		PickResult chosen = null;
		var frame = new System.Windows.Threading.DispatcherFrame();
		var overlays = new List<WpfWindow>();

		void finish(PickResult r) {
			chosen = r;
			foreach (var o in overlays) {
				try { o.Close(); } catch { }
			}
			frame.Continue = false;
		}

		foreach (var scr in System.Windows.Forms.Screen.AllScreens) {
			var b = scr.Bounds;
			if (b.Width < 8 || b.Height < 8) continue;
			var ov = new WpfWindow {
				WindowStyle = WindowStyle.None,
				AllowsTransparency = true,
				Background = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0)),
				Topmost = true,
				ShowInTaskbar = false,
				ResizeMode = ResizeMode.NoResize,
				Cursor = Cursors.Cross,
			};
			// 透明画布 + Path 遮罩（EvenOdd 挖空目标窗，保持原色）
			var canvas = new Canvas { Background = Brushes.Transparent };
			var mask = new System.Windows.Shapes.Path {
				Fill = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)),
				IsHitTestVisible = false,
			};
			var rwin = new System.Windows.Shapes.Rectangle {
				Stroke = new SolidColorBrush(Color.FromRgb(0x07, 0xC1, 0x60)),
				StrokeThickness = 3,
				Fill = Brushes.Transparent,
				Visibility = Visibility.Collapsed,
				IsHitTestVisible = false,
			};
			var hint = new Border {
				Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x11, 0x18, 0x27)),
				CornerRadius = new CornerRadius(8),
				Padding = new Thickness(12, 8, 12, 8),
				Child = new TextBlock {
					Text = "长截图：单击目标窗口 · Esc 取消",
					Foreground = Brushes.White,
					FontSize = 14,
				},
			};
			Canvas.SetLeft(hint, 24);
			Canvas.SetTop(hint, 24);
			canvas.Children.Add(mask);
			canvas.Children.Add(rwin);
			canvas.Children.Add(hint);
			ov.Content = canvas;

			var monL = b.Left;
			var monT = b.Top;
			var monW = b.Width;
			var monH = b.Height;
			// System DPI Aware：WPF DIP 用系统缩放，勿用副屏 monScale
			var scale = Math.Max(0.25, ScreenDpi.SystemScale());
			ov.Width = monW / scale;
			ov.Height = monH / scale;
			ov.Left = monL / scale;
			ov.Top = monT / scale;

			void setmask(double hx, double hy, double hw, double hh) {
				var cw = canvas.ActualWidth > 1 ? canvas.ActualWidth : ov.Width;
				var ch = canvas.ActualHeight > 1 ? canvas.ActualHeight : ov.Height;
				var g = new PathGeometry { FillRule = FillRule.EvenOdd };
				g.AddGeometry(new RectangleGeometry(new Rect(0, 0, cw, ch)));
				if (hw > 0.5 && hh > 0.5)
					g.AddGeometry(new RectangleGeometry(new Rect(hx, hy, hw, hh)));
				mask.Data = g;
			}

			ov.SourceInitialized += (_, _) => {
				var hwnd = new WindowInteropHelper(ov).Handle;
				if (hwnd != IntPtr.Zero)
					SetWindowPos(hwnd, HwndTopmost, monL, monT, monW, monH, SWP_SHOWWINDOW);
			};
			ov.Loaded += (_, _) => setmask(0, 0, 0, 0);

			void onmove(object s, MouseEventArgs e) {
				if (!GetCursorPos(out var p)) return;
				var hit = FindAt(wins, p.X, p.Y);
				if (hit == null) {
					rwin.Visibility = Visibility.Collapsed;
					setmask(0, 0, 0, 0);
					return;
				}
				var inter = System.Drawing.Rectangle.Intersect(hit.Bounds, b);
				if (inter.Width < 4 || inter.Height < 4) {
					rwin.Visibility = Visibility.Collapsed;
					setmask(0, 0, 0, 0);
					return;
				}
				var (cw, ch) = (canvas.ActualWidth > 1 ? canvas.ActualWidth : ov.Width,
					canvas.ActualHeight > 1 ? canvas.ActualHeight : ov.Height);
				var rx = (inter.Left - monL) * cw / monW;
				var ry = (inter.Top - monT) * ch / monH;
				var rw = inter.Width * cw / monW;
				var rh = inter.Height * ch / monH;
				Canvas.SetLeft(rwin, rx);
				Canvas.SetTop(rwin, ry);
				rwin.Width = rw;
				rwin.Height = rh;
				rwin.Visibility = Visibility.Visible;
				setmask(rx, ry, rw, rh);
			}

			void ondown(object s, MouseButtonEventArgs e) {
				if (!GetCursorPos(out var p)) return;
				var hit = FindAt(wins, p.X, p.Y);
				if (hit != null) {
					e.Handled = true;
					finish(hit);
				}
			}

			ov.MouseMove += onmove;
			ov.MouseLeftButtonDown += ondown;
			ov.KeyDown += (_, ke) => {
				if (ke.Key == Key.Escape) {
					ke.Handled = true;
					finish(null);
				}
			};
			overlays.Add(ov);
			ov.Show();
			try { ov.Activate(); } catch { }
		}

		if (overlays.Count == 0) return null;
		try { overlays[0].Focus(); } catch { }
		System.Windows.Threading.Dispatcher.PushFrame(frame);
		return chosen;
	}

	/// <summary>顶层命中后再钻到最深子 HWND（内容区 / 编辑框等）。</summary>
	public static PickResult FindAtPublic(List<PickResult> wins, int x, int y) => FindAt(wins, x, y);

	static PickResult FindAt(List<PickResult> wins, int x, int y) {
		foreach (var w in wins) {
			if (x < w.Bounds.Left || x >= w.Bounds.Right || y < w.Bounds.Top || y >= w.Bounds.Bottom)
				continue;
			var deep = DeepestChildAt(w.Hwnd, x, y);
			if (deep != IntPtr.Zero && deep != w.Hwnd && GetWindowRect(deep, out var cr)) {
				var cw = cr.Right - cr.Left;
				var ch = cr.Bottom - cr.Top;
				if (cw >= 8 && ch >= 8) {
					var title = new StringBuilder(256);
					GetWindowText(deep, title, title.Capacity);
					var t = title.ToString();
					if (string.IsNullOrWhiteSpace(t)) t = w.Title;
					return new PickResult {
						Hwnd = deep,
						Title = t,
						Bounds = new System.Drawing.Rectangle(cr.Left, cr.Top, cw, ch),
					};
				}
			}
			return w;
		}
		return null;
	}

	static IntPtr DeepestChildAt(IntPtr root, int x, int y) {
		if (root == IntPtr.Zero) return IntPtr.Zero;
		var cur = root;
		for (int depth = 0; depth < 64; depth++) {
			IntPtr child = GetWindow(cur, GW_CHILD);
			IntPtr hit = IntPtr.Zero;
			while (child != IntPtr.Zero) {
				try {
					if (IsWindowVisible(child) && !IsIconic(child)
						&& GetWindowRect(child, out var r)) {
						var w = r.Right - r.Left;
						var h = r.Bottom - r.Top;
						if (w >= 4 && h >= 4
							&& x >= r.Left && x < r.Right
							&& y >= r.Top && y < r.Bottom) {
							hit = child;
							break;
						}
					}
				}
				catch { }
				child = GetWindow(child, GW_HWNDNEXT);
			}
			if (hit == IntPtr.Zero) return cur;
			cur = hit;
		}
		return cur;
	}

	public static List<PickResult> EnumTopWindows() {
		var list = new List<PickResult>();
		try {
			EnumWindows((h, _) => {
				try {
					if (h == IntPtr.Zero || !IsWindowVisible(h) || IsIconic(h)) return true;
					var style = GetWindowLong(h, GWL_STYLE);
					if ((style & WS_CHILD) != 0) return true;
					var ex = GetWindowLong(h, GWL_EXSTYLE);
					if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & 0x00040000) == 0) return true;
					try {
						if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
							return true;
					}
					catch { }
					var cls = new StringBuilder(64);
					GetClassName(h, cls, cls.Capacity);
					var cn = cls.ToString();
					if (cn is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW")
						return true;
					if (!GetWindowRect(h, out var r)) return true;
					var w = r.Right - r.Left;
					var hgt = r.Bottom - r.Top;
					if (w < 32 || hgt < 32) return true;
					var title = new StringBuilder(256);
					GetWindowText(h, title, title.Capacity);
					list.Add(new PickResult {
						Hwnd = h,
						Title = title.ToString(),
						Bounds = new System.Drawing.Rectangle(r.Left, r.Top, w, hgt),
					});
				}
				catch { }
				return true;
			}, IntPtr.Zero);
		}
		catch { }
		return list;
	}

	/// <summary>对窗口滚动拼接长图。失败抛异常。</summary>
	public static async Task<BitmapSource> CaptureLongAsync(
		IntPtr hwnd,
		Action<string> status,
		CancellationToken ct) {
		NativeRuntime.EnsureOpenCv();
		if (hwnd == IntPtr.Zero) throw new ArgumentException("无效窗口句柄");
		if (!GetWindowRect(hwnd, out _))
			throw new InvalidOperationException("无法获取窗口矩形");

		try {
			ShowWindow(hwnd, 9); // SW_RESTORE
			SetForegroundWindow(hwnd);
		}
		catch { }
		await Task.Delay(220, ct);

		var strips = new List<Mat>();
		Mat lastFull = null;
		// 自适应滚轮刻度数（初始按视口估算，匹配差时减小）
		var scrollNotches = 0;
		var stagnant = 0;
		try {
			for (int step = 0; step < MaxSteps; step++) {
				ct.ThrowIfCancellationRequested();
				status?.Invoke($"长截图 · 滚动拼接 {step + 1}/{MaxSteps}…");

				if (!GetWindowRect(hwnd, out var wr)) break;
				var viewH = Math.Max(1, wr.Bottom - wr.Top);
				if (scrollNotches <= 0)
					scrollNotches = estimatenotches(viewH);

				var frame = CaptureWindow(hwnd, wr);
				if (frame == null || frame.Empty()) break;

				if (lastFull == null) {
					strips.Add(frame.Clone());
					lastFull = frame;
				}
				else {
					// 几乎没动：到底或未聚焦
					if (framesimilar(lastFull, frame, 0.992)) {
						frame.Dispose();
						stagnant++;
						if (stagnant >= 2) {
							status?.Invoke("长截图 · 已到内容底部");
							break;
						}
						// 再轻滚一步
						ScrollDownGentle(hwnd, wr, Math.Max(2, scrollNotches));
						await Task.Delay(ScrollDelayMs, ct);
						continue;
					}
					stagnant = 0;

					if (!tryuniqueband(lastFull, frame, out var band, out var matchScore)
						|| band == null || band.Rows < 8) {
						band?.Dispose();
						frame.Dispose();
						status?.Invoke("长截图 · 已到内容底部");
						break;
					}

					// 新增过少且匹配很好 → 可能到底
					if (band.Rows < Math.Max(16, viewH / 25) && matchScore >= 0.85) {
						band.Dispose();
						frame.Dispose();
						status?.Invoke("长截图 · 已到内容底部");
						break;
					}

					// 匹配偏弱：可能滚过了，下一步减小滚动量
					if (matchScore < 0.55)
						scrollNotches = Math.Max(2, scrollNotches - 1);
					else if (matchScore > 0.85 && band.Rows > viewH * 0.55)
						// 重叠少、新增多：可略增（上限仍保守）
						scrollNotches = Math.Min(estimatenotches(viewH) + 1, scrollNotches + 1);

					strips.Add(band);
					lastFull.Dispose();
					lastFull = frame;
				}

				var totalH = 0;
				foreach (var s in strips) totalH += s.Rows;
				if (totalH >= MaxHeightPx) {
					status?.Invoke("长截图 · 已达高度上限");
					break;
				}

				ScrollDownGentle(hwnd, wr, scrollNotches);
				await Task.Delay(ScrollDelayMs, ct);
			}

			if (strips.Count == 0)
				throw new InvalidOperationException("未能捕获窗口内容");

			status?.Invoke($"长截图 · 合并 {strips.Count} 段…");
			using var merged = mergevertical(strips);
			return MatToBitmapSource(merged);
		}
		finally {
			lastFull?.Dispose();
			foreach (var s in strips) {
				try { s.Dispose(); } catch { }
			}
		}
	}

	static int estimatenotches(int viewH) {
		// 约滚动视口 40%：刻数 = 目标像素 / 每刻像素
		var targetPx = (int)(viewH * ScrollViewportRatio);
		var n = (int)Math.Round(targetPx / (double)ApproxPxPerNotch);
		return Compat.Clamp(n, 2, 6); // 最多 6 刻，避免一下到底
	}

	/// <summary>
	/// 在 next 中匹配 prev 底边条带，切出无重叠的下半部分。
	/// matchScore：模板匹配峰值，&lt;0.5 表示可能跳页。
	/// </summary>
	static bool tryuniqueband(Mat prev, Mat next, out Mat band, out double matchScore) {
		band = null;
		matchScore = 0;
		if (prev == null || next == null || prev.Empty() || next.Empty()) return false;
		var w = Math.Min(prev.Cols, next.Cols);
		if (w < 8) return false;

		using var p = prev.Cols == w ? prev.Clone() : prev[new OpenCvSharp.Rect(0, 0, w, prev.Rows)].Clone();
		using var n = next.Cols == w ? next.Clone() : next[new OpenCvSharp.Rect(0, 0, w, next.Rows)].Clone();

		// 用 prev 底部较窄条带匹配（更稳）
		var bandH = Compat.Clamp(p.Rows / 8, 32, 120);
		if (bandH >= p.Rows) bandH = Math.Max(12, p.Rows / 4);
		if (n.Rows < bandH + 8) return false;

		using var template = p[new OpenCvSharp.Rect(0, p.Rows - bandH, w, bandH)].Clone();
		using var match = new Mat();
		Cv2.MatchTemplate(n, template, match, TemplateMatchModes.CCoeffNormed);
		Cv2.MinMaxLoc(match, out _, out var maxVal, out _, out var maxLoc);
		matchScore = maxVal;

		int cutY;
		if (maxVal >= 0.50) {
			cutY = maxLoc.Y + bandH;
		}
		else {
			// 匹配失败：保守多保留重叠（宁多重叠也不跳页）
			// 假定只滚了约 35% 视口
			cutY = (int)(n.Rows * 0.55);
		}
		if (cutY < 0) cutY = 0;
		if (cutY >= n.Rows - 4) {
			// 整页几乎重叠 → 到底
			return false;
		}

		var uh = n.Rows - cutY;
		if (uh < 4) return false;
		band = n[new OpenCvSharp.Rect(0, cutY, w, uh)].Clone();
		return true;
	}

	/// <summary>两帧是否几乎相同（到底或未滚动）。</summary>
	static bool framesimilar(Mat a, Mat b, double thresh) {
		try {
			if (a == null || b == null || a.Empty() || b.Empty()) return false;
			if (a.Rows != b.Rows || a.Cols != b.Cols) return false;
			using var diff = new Mat();
			Cv2.Absdiff(a, b, diff);
			using var gray = new Mat();
			if (diff.Channels() == 3)
				Cv2.CvtColor(diff, gray, ColorConversionCodes.BGR2GRAY);
			else
				diff.CopyTo(gray);
			// 平均差异很小
			var mean = Cv2.Mean(gray);
			// mean.Val0 约 0~255；相似时接近 0
			var sim = 1.0 - Math.Min(1.0, mean.Val0 / 40.0);
			return sim >= thresh;
		}
		catch {
			return false;
		}
	}

	static Mat mergevertical(List<Mat> strips) {
		if (strips.Count == 1) return strips[0].Clone();
		var w = strips.Min(s => s.Cols);
		var h = strips.Sum(s => s.Rows);
		var dst = new Mat(h, w, strips[0].Type());
		var y = 0;
		foreach (var s in strips) {
			using var slice = s.Cols == w ? s : s[new OpenCvSharp.Rect(0, 0, w, s.Rows)];
			slice.CopyTo(dst[new OpenCvSharp.Rect(0, y, w, s.Rows)]);
			y += s.Rows;
		}
		return dst;
	}

	/// <summary>
	/// 小步向下滚：仅滚轮若干刻度，不再叠加 PageDown（易一下到底）。
	/// </summary>
	static void ScrollDownGentle(IntPtr hwnd, NativeRect wr, int notches) {
		notches = Compat.Clamp(notches, 1, 8);
		var cx = (wr.Left + wr.Right) / 2;
		var cy = (wr.Top + wr.Bottom) / 2;
		try { SetCursorPos(cx, cy); } catch { }
		try { SetForegroundWindow(hwnd); } catch { }

		// 优先对目标 HWND 发 WM_MOUSEWHEEL（比全局 mouse_event 更可控）
		// wParam: high word = delta，low word = key flags；lParam: x + (y<<16) 屏幕坐标
		var lp = new IntPtr((cx & 0xFFFF) | (cy << 16));
		for (int i = 0; i < notches; i++) {
			try {
				// 负 delta = 向下；一次一刻度 -120
				var wp = new IntPtr(unchecked((int)((uint)(-120) << 16)));
				SendMessage(hwnd, WM_MOUSEWHEEL, wp, lp);
			}
			catch {
				try {
					mouse_event(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)(-120)), UIntPtr.Zero);
				}
				catch { }
			}
			// 刻度间极短间隔，避免系统合并成一次大幅滚动
			try { System.Threading.Thread.Sleep(28); } catch { }
		}
	}

	static Mat CaptureWindow(IntPtr hwnd, NativeRect wr) {
		var w = Math.Max(1, wr.Right - wr.Left);
		var h = Math.Max(1, wr.Bottom - wr.Top);
		// 优先 PrintWindow（含部分分层窗）
		var gdi = PrintWindowToBitmap(hwnd, w, h);
		if (gdi != null) {
			try {
				using var tmp = gdi;
				return BitmapToMat(tmp);
			}
			catch { }
		}
		// 回退屏幕 BitBlt
		try {
			using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
			using (var g = System.Drawing.Graphics.FromImage(bmp)) {
				g.CopyFromScreen(wr.Left, wr.Top, 0, 0, new System.Drawing.Size(w, h),
					System.Drawing.CopyPixelOperation.SourceCopy);
			}
			return BitmapToMat(bmp);
		}
		catch {
			return null;
		}
	}

	static System.Drawing.Bitmap PrintWindowToBitmap(IntPtr hwnd, int w, int h) {
		var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		using (var g = System.Drawing.Graphics.FromImage(bmp)) {
			var hdc = g.GetHdc();
			try {
				if (!PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT)) {
					if (!PrintWindow(hwnd, hdc, 0)) {
						g.ReleaseHdc(hdc);
						bmp.Dispose();
						return null;
					}
				}
			}
			finally {
				try { g.ReleaseHdc(hdc); } catch { }
			}
		}
		// 全透明则失败
		try {
			var px = bmp.GetPixel(w / 2, h / 2);
			if (px.A == 0 && px.R == 0 && px.G == 0 && px.B == 0) {
				// 可能仍有效（真黑），保留
			}
		}
		catch { }
		return bmp;
	}

	static Mat BitmapToMat(System.Drawing.Bitmap bmp) {
		var w = bmp.Width;
		var h = bmp.Height;
		var rect = new System.Drawing.Rectangle(0, 0, w, h);
		var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
			System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		try {
			using var bgra = Mat.FromPixelData(h, w, MatType.CV_8UC4, data.Scan0, data.Stride);
			using var owned = bgra.Clone(); // 脱离 Scan0 生命周期
			var bgr = new Mat();
			Cv2.CvtColor(owned, bgr, ColorConversionCodes.BGRA2BGR);
			return bgr;
		}
		finally {
			bmp.UnlockBits(data);
		}
	}

	static BitmapSource MatToBitmapSource(Mat bgr) {
		using var bgra = new Mat();
		Cv2.CvtColor(bgr, bgra, ColorConversionCodes.BGR2BGRA);
		var w = bgra.Cols;
		var h = bgra.Rows;
		var stride = w * 4;
		var bytes = new byte[stride * h];
		System.Runtime.InteropServices.Marshal.Copy(bgra.Data, bytes, 0, bytes.Length);
		// 强制 alpha
		for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
		var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bytes, stride);
		bmp.Freeze();
		return bmp;
	}
}
