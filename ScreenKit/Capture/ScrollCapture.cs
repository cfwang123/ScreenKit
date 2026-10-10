using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rect = System.Windows.Rect;
using Brushes = System.Windows.Media.Brushes;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfWindow = System.Windows.Window;

namespace ScreenKit;

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

		using (ScreenDpi.PerMonitorV2Scope())
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
			var scale = Math.Max(0.25, ScreenDpi.GetMonitorScale(monL + monW / 2, monT + monH / 2));
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

			ov.SourceInitialized += (_, _) =>
				ScreenDpi.PinWindowToPhysical(ov, monL, monT, monW, monH);
			ov.Loaded += (_, _) => {
				ScreenDpi.PinWindowToPhysical(ov, monL, monT, monW, monH);
				setmask(0, 0, 0, 0);
			};

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

	/// <summary>对窗口滚动拼接长图。失败抛异常。用 Bitmap 找重叠，不加载 OpenCV。</summary>
	public static async Task<BitmapSource> CaptureLongAsync(
		IntPtr hwnd,
		Action<string> status,
		CancellationToken ct) {
		if (hwnd == IntPtr.Zero) throw new ArgumentException("无效窗口句柄");
		if (!GetWindowRect(hwnd, out _))
			throw new InvalidOperationException("无法获取窗口矩形");

		try {
			ShowWindow(hwnd, 9); // SW_RESTORE
			SetForegroundWindow(hwnd);
		}
		catch { }
		await Task.Delay(220, ct);

		var strips = new List<Bgra>();
		Bgra lastFull = null;
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
				if (frame == null || frame.H < 8 || frame.W < 8) break;

				if (lastFull == null) {
					strips.Add(frame);
					lastFull = frame;
				}
				else {
					// 几乎没动：到底或未聚焦
					if (framesimilar(lastFull, frame, 0.992)) {
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
						|| band == null || band.H < 8) {
						status?.Invoke("长截图 · 已到内容底部");
						break;
					}

					// 新增过少且匹配很好 → 可能到底
					if (band.H < Math.Max(16, viewH / 25) && matchScore >= 0.85) {
						status?.Invoke("长截图 · 已到内容底部");
						break;
					}

					// 匹配偏弱：可能滚过了，下一步减小滚动量
					if (matchScore < 0.55)
						scrollNotches = Math.Max(2, scrollNotches - 1);
					else if (matchScore > 0.85 && band.H > viewH * 0.55)
						// 重叠少、新增多：可略增（上限仍保守）
						scrollNotches = Math.Min(estimatenotches(viewH) + 1, scrollNotches + 1);

					strips.Add(band);
					lastFull = frame;
				}

				var totalH = 0;
				foreach (var s in strips) totalH += s.H;
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
			return tosource(mergevertical(strips));
		}
		finally {
			lastFull = null;
			strips.Clear();
		}
	}

	/// <summary>两张合成图的重叠行能否切对。0 为通过。</summary>
	public static int SelfTest() {
		var prev = pattern(48, 80, 0);
		var next = pattern(48, 80, 11);
		if (!framesimilar(prev, prev, 0.992)) return 1;
		if (framesimilar(prev, next, 0.992)) return 2;
		if (!tryuniqueband(prev, next, out var band, out var score)) return 3;
		if (band.H != 11) return 4;
		if (score < 0.5) return 5;
		if (band.P[0] != 7 || band.P[1] != 7 || band.P[2] != 7) return 6;
		var merged = mergevertical(new List<Bgra> { prev, band });
		if (merged.H != prev.H + band.H) return 7;
		if (merged.W != prev.W) return 8;
		if (merged.P[(prev.H * merged.W) * 4] != 7) return 9;
		return 0;
	}

	static int estimatenotches(int viewH) {
		// 约滚动视口 40%：刻数 = 目标像素 / 每刻像素
		var targetPx = (int)(viewH * ScrollViewportRatio);
		var n = (int)Math.Round(targetPx / (double)ApproxPxPerNotch);
		return Compat.Clamp(n, 2, 6); // 最多 6 刻，避免一下到底
	}

	/// <summary>
	/// 在 next 中匹配 prev 底边条带，切出无重叠的下半部分。
	/// matchScore：归一化相关峰值，&lt;0.5 表示可能跳页。
	/// </summary>
	static bool tryuniqueband(Bgra prev, Bgra next, out Bgra band, out double matchScore) {
		band = null;
		matchScore = 0;
		if (prev == null || next == null || prev.H < 8 || next.H < 8) return false;
		var w = Math.Min(prev.W, next.W);
		if (w < 8) return false;

		var bandH = Compat.Clamp(prev.H / 8, 32, 120);
		if (bandH >= prev.H) bandH = Math.Max(12, prev.H / 4);
		if (next.H < bandH + 8) return false;

		matchScore = matchbottom(prev, next, w, bandH, out var matchY);
		int cutY;
		if (matchScore >= 0.50)
			cutY = matchY + bandH;
		else
			// 匹配失败：保守多保留重叠（宁多重叠也不跳页）
			cutY = (int)(next.H * 0.55);
		if (cutY < 0) cutY = 0;
		if (cutY >= next.H - 4) return false;

		var uh = next.H - cutY;
		if (uh < 4) return false;
		band = crop(next, cutY, uh, w);
		return true;
	}

	/// <summary>上一帧底部条带在下一帧中的起始行。返回值约 -1..1。</summary>
	static double matchbottom(Bgra prev, Bgra next, int w, int bandH, out int matchY) {
		matchY = 0;
		var step = Math.Max(1, w / 64);
		var cols = new List<int>();
		for (int x = 0; x < w; x += step) cols.Add(x);
		var nc = cols.Count;
		var pg = sample(prev, cols);
		var ng = sample(next, cols);
		var nlen = bandH * nc;
		var t0 = (prev.H - bandH) * nc;
		double tsum = 0;
		for (int i = 0; i < nlen; i++) tsum += pg[t0 + i];
		var tmean = tsum / nlen;
		double tvar = 0;
		for (int i = 0; i < nlen; i++) {
			var d = pg[t0 + i] - tmean;
			tvar += d * d;
		}
		var best = -2.0;
		var bestY = 0;
		var maxY = next.H - bandH;
		for (int y = 0; y <= maxY; y++) {
			var o = y * nc;
			double psum = 0;
			for (int i = 0; i < nlen; i++) psum += ng[o + i];
			var pmean = psum / nlen;
			double pvar = 0, num = 0;
			for (int i = 0; i < nlen; i++) {
				var pd = ng[o + i] - pmean;
				var td = pg[t0 + i] - tmean;
				pvar += pd * pd;
				num += pd * td;
			}
			var den = Math.Sqrt(pvar * tvar);
			double score;
			if (den < 1e-6)
				score = tvar < 1e-6 ? 1 : 0;
			else
				score = num / den;
			if (score > best) {
				best = score;
				bestY = y;
			}
		}
		matchY = bestY;
		return best;
	}

	static byte[] sample(Bgra img, List<int> cols) {
		var nc = cols.Count;
		var g = new byte[img.H * nc];
		for (int y = 0; y < img.H; y++) {
			var row = y * img.W * 4;
			var dst = y * nc;
			for (int c = 0; c < nc; c++) {
				var i = row + cols[c] * 4;
				g[dst + c] = (byte)((img.P[i] + img.P[i + 1] + img.P[i + 2]) / 3);
			}
		}
		return g;
	}

	/// <summary>两帧是否几乎相同（到底或未滚动）。</summary>
	static bool framesimilar(Bgra a, Bgra b, double thresh) {
		try {
			if (a == null || b == null || a.H < 1 || b.H < 1) return false;
			if (a.W != b.W || a.H != b.H) return false;
			long sum = 0;
			long n = 0;
			var step = 4;
			for (int y = 0; y < a.H; y += step) {
				var row = y * a.W * 4;
				for (int x = 0; x < a.W; x += step) {
					var i = row + x * 4;
					sum += Math.Abs(a.P[i] - b.P[i]);
					sum += Math.Abs(a.P[i + 1] - b.P[i + 1]);
					sum += Math.Abs(a.P[i + 2] - b.P[i + 2]);
					n += 3;
				}
			}
			if (n == 0) return false;
			var mean = sum / (double)n;
			var sim = 1.0 - Math.Min(1.0, mean / 40.0);
			return sim >= thresh;
		}
		catch {
			return false;
		}
	}

	static Bgra mergevertical(List<Bgra> strips) {
		var w = strips[0].W;
		for (int i = 1; i < strips.Count; i++)
			if (strips[i].W < w) w = strips[i].W;
		if (strips.Count == 1 && strips[0].W == w) return strips[0];
		var h = 0;
		foreach (var s in strips) h += s.H;
		var p = new byte[w * h * 4];
		var y = 0;
		foreach (var s in strips) {
			for (int row = 0; row < s.H; row++)
				Buffer.BlockCopy(s.P, row * s.W * 4, p, (y + row) * w * 4, w * 4);
			y += s.H;
		}
		return new Bgra { W = w, H = h, P = p };
	}

	static Bgra crop(Bgra src, int y, int h, int w) {
		var p = new byte[w * h * 4];
		for (int row = 0; row < h; row++)
			Buffer.BlockCopy(src.P, (y + row) * src.W * 4, p, row * w * 4, w * 4);
		return new Bgra { W = w, H = h, P = p };
	}

	/// <summary>第 y 行的内容来自原图 y+shift；超出的行填 7。</summary>
	static Bgra pattern(int w, int h, int shift) {
		var p = new byte[w * h * 4];
		for (int y = 0; y < h; y++) {
			var srcY = y + shift;
			for (int x = 0; x < w; x++) {
				var i = (y * w + x) * 4;
				if (srcY >= h) {
					p[i] = 7;
					p[i + 1] = 7;
					p[i + 2] = 7;
				}
				else {
					p[i] = (byte)(srcY * 3);
					p[i + 1] = (byte)(x * 4);
					p[i + 2] = (byte)(200 - srcY);
				}
				p[i + 3] = 255;
			}
		}
		return new Bgra { W = w, H = h, P = p };
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

	static Bgra CaptureWindow(IntPtr hwnd, NativeRect wr) {
		var w = Math.Max(1, wr.Right - wr.Left);
		var h = Math.Max(1, wr.Bottom - wr.Top);
		// 优先 PrintWindow（含部分分层窗）
		var gdi = PrintWindowToBitmap(hwnd, w, h);
		if (gdi != null) {
			try {
				using var tmp = gdi;
				return frombitmap(tmp);
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
			return frombitmap(bmp);
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

	static Bgra frombitmap(System.Drawing.Bitmap bmp) {
		var w = bmp.Width;
		var h = bmp.Height;
		var rect = new System.Drawing.Rectangle(0, 0, w, h);
		var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
			System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		try {
			var stride = data.Stride;
			var rowBytes = w * 4;
			var pix = new byte[rowBytes * h];
			for (int y = 0; y < h; y++) {
				var src = IntPtr.Add(data.Scan0, y * stride);
				System.Runtime.InteropServices.Marshal.Copy(src, pix, y * rowBytes, rowBytes);
			}
			return new Bgra { W = w, H = h, P = pix };
		}
		finally {
			bmp.UnlockBits(data);
		}
	}

	static BitmapSource tosource(Bgra b) {
		var stride = b.W * 4;
		var bytes = new byte[stride * b.H];
		Buffer.BlockCopy(b.P, 0, bytes, 0, bytes.Length);
		for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
		var bmp = BitmapSource.Create(b.W, b.H, 96, 96, PixelFormats.Bgra32, null, bytes, stride);
		bmp.Freeze();
		return bmp;
	}

	sealed class Bgra {
		public int W;
		public int H;
		public byte[] P;
	}
}
