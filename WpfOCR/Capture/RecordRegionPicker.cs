using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Brushes = System.Windows.Media.Brushes;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfWindow = System.Windows.Window;
using Rect = System.Windows.Rect;

namespace WpfOCR;

/// <summary>
/// 录屏区域选择：单击窗口（含内部 HWND）或拖拽框选。
/// 返回虚拟屏像素矩形。
/// </summary>
static class RecordRegionPicker {
	[StructLayout(LayoutKind.Sequential)]
	struct NativePoint { public int X, Y; }

	[DllImport("user32.dll")] static extern bool GetCursorPos(out NativePoint lpPoint);
	[DllImport("user32.dll", SetLastError = true)]
	static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

	const uint SWP_SHOWWINDOW = 0x0040;
	static readonly IntPtr HwndTopmost = new(-1);

	public static System.Drawing.Rectangle? Pick() {
		var wins = ScrollCapture.EnumTopWindows();
		System.Drawing.Rectangle? result = null;
		var frame = new System.Windows.Threading.DispatcherFrame();
		var overlays = new List<WpfWindow>();

		void finish(System.Drawing.Rectangle? r) {
			result = r;
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
			var canvas = new Canvas { Background = Brushes.Transparent };
			var mask = new System.Windows.Shapes.Path {
				Fill = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)),
				IsHitTestVisible = false,
			};
			// 仅红框描边，中心透明 + mask 挖空 → 显示原始颜色
			var rsel = new System.Windows.Shapes.Rectangle {
				Stroke = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
				StrokeThickness = 2,
				Fill = Brushes.Transparent,
				Visibility = Visibility.Collapsed,
				IsHitTestVisible = false,
			};
			var hint = new Border {
				Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x11, 0x18, 0x27)),
				CornerRadius = new CornerRadius(8),
				Padding = new Thickness(12, 8, 12, 8),
				Child = new TextBlock {
					Text = "录屏：单击窗口或拖拽框选 · Esc 取消",
					Foreground = Brushes.White,
					FontSize = 14,
				},
			};
			Canvas.SetLeft(hint, 24);
			Canvas.SetTop(hint, 24);
			canvas.Children.Add(mask);
			canvas.Children.Add(rsel);
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

			bool dragging = false;
			int x0 = 0, y0 = 0;
			System.Drawing.Rectangle hover = default;
			bool hasHover = false;

			void setmask(double hx, double hy, double hw, double hh) {
				var cw = canvas.ActualWidth > 1 ? canvas.ActualWidth : ov.Width;
				var ch = canvas.ActualHeight > 1 ? canvas.ActualHeight : ov.Height;
				var g = new PathGeometry { FillRule = FillRule.EvenOdd };
				g.AddGeometry(new RectangleGeometry(new Rect(0, 0, cw, ch)));
				if (hw > 0.5 && hh > 0.5)
					g.AddGeometry(new RectangleGeometry(new Rect(hx, hy, hw, hh)));
				mask.Data = g;
			}

			void showrect(System.Drawing.Rectangle vir) {
				var inter = System.Drawing.Rectangle.Intersect(vir, b);
				if (inter.Width < 2 || inter.Height < 2) {
					rsel.Visibility = Visibility.Collapsed;
					setmask(0, 0, 0, 0);
					return;
				}
				var cw = canvas.ActualWidth > 1 ? canvas.ActualWidth : ov.Width;
				var ch = canvas.ActualHeight > 1 ? canvas.ActualHeight : ov.Height;
				var rx = (inter.Left - monL) * cw / monW;
				var ry = (inter.Top - monT) * ch / monH;
				var rw = inter.Width * cw / monW;
				var rh = inter.Height * ch / monH;
				Canvas.SetLeft(rsel, rx);
				Canvas.SetTop(rsel, ry);
				rsel.Width = rw;
				rsel.Height = rh;
				rsel.Visibility = Visibility.Visible;
				setmask(rx, ry, rw, rh);
			}

			ov.SourceInitialized += (_, _) => {
				var hwnd = new WindowInteropHelper(ov).Handle;
				if (hwnd != IntPtr.Zero)
					SetWindowPos(hwnd, HwndTopmost, monL, monT, monW, monH, SWP_SHOWWINDOW);
			};
			ov.Loaded += (_, _) => setmask(0, 0, 0, 0);

			ov.MouseMove += (_, e) => {
				if (!GetCursorPos(out var p)) return;
				if (dragging) {
					var l = Math.Min(x0, p.X);
					var t = Math.Min(y0, p.Y);
					var r = Math.Max(x0, p.X);
					var bot = Math.Max(y0, p.Y);
					showrect(System.Drawing.Rectangle.FromLTRB(l, t, r, bot));
					return;
				}
				var hit = ScrollCapture.FindAtPublic(wins, p.X, p.Y);
				if (hit != null) {
					hasHover = true;
					hover = hit.Bounds;
					showrect(hover);
				}
				else {
					hasHover = false;
					rsel.Visibility = Visibility.Collapsed;
					setmask(0, 0, 0, 0);
				}
			};

			ov.MouseLeftButtonDown += (_, e) => {
				if (!GetCursorPos(out var p)) return;
				dragging = true;
				x0 = p.X;
				y0 = p.Y;
				// 记录当前悬停窗
				var hit = ScrollCapture.FindAtPublic(wins, p.X, p.Y);
				hasHover = hit != null;
				if (hasHover) hover = hit.Bounds;
				canvas.CaptureMouse();
				e.Handled = true;
			};

			ov.MouseLeftButtonUp += (_, e) => {
				if (!dragging) return;
				dragging = false;
				try { canvas.ReleaseMouseCapture(); } catch { }
				if (!GetCursorPos(out var p)) { finish(null); return; }
				var dx = Math.Abs(p.X - x0);
				var dy = Math.Abs(p.Y - y0);
				System.Drawing.Rectangle rect;
				if (dx < 4 && dy < 4) {
					// 单击：取窗口
					if (!hasHover) { finish(null); return; }
					rect = hover;
				}
				else {
					rect = System.Drawing.Rectangle.FromLTRB(
						Math.Min(x0, p.X), Math.Min(y0, p.Y),
						Math.Max(x0, p.X), Math.Max(y0, p.Y));
				}
				// 偶数宽高（x264 友好）
				if (rect.Width % 2 != 0) rect.Width--;
				if (rect.Height % 2 != 0) rect.Height--;
				if (rect.Width < 16 || rect.Height < 16) { finish(null); return; }
				e.Handled = true;
				finish(rect);
			};

			ov.KeyDown += (_, ke) => {
				if (ke.Key == Key.Escape) {
					ke.Handled = true;
					finish(null);
				}
			};

			overlays.Add(ov);
			ov.Show();
		}

		if (overlays.Count == 0) return null;
		try { overlays[0].Activate(); } catch { }
		System.Windows.Threading.Dispatcher.PushFrame(frame);
		return result;
	}
}
