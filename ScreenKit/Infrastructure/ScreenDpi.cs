using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace ScreenKit;

/// <summary>
/// 多显示器 / 混合 DPI：DIP（逻辑）↔ 物理像素 转换。
/// 用于截图选区裁切。
/// <para>
/// 注意：Per-Monitor V2 进程里 <c>LogicalToPhysicalPointForPerMonitorDPI</c> 往往原样返回，
/// 不能用来做 DIP→物理；应使用虚拟屏比例或 WPF <c>PointToScreen</c>。
/// </para>
/// </summary>
static class ScreenDpi {
	const uint MONITOR_DEFAULTTONEAREST = 2;
	const int MDT_EFFECTIVE_DPI = 0;

	[StructLayout(LayoutKind.Sequential)]
	struct POINT {
		public int X, Y;
	}

	[DllImport("user32.dll")]
	static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

	[DllImport("Shcore.dll")]
	static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

	/// <summary>虚拟屏物理像素矩形（与 CopyFromScreen / SystemInformation.VirtualScreen 一致）。</summary>
	public static (int left, int top, int width, int height) VirtualScreenPixels() {
		var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
		return (vs.Left, vs.Top, Math.Max(1, vs.Width), Math.Max(1, vs.Height));
	}

	/// <summary>
	/// 进程/系统 DPI 缩放（96=1.0）。System DPI Aware 下 WPF DIP 一律用此比例，
	/// 不可用副屏的 per-monitor Effective DPI 去算窗口 Width/Height，否则混合 DPI 会缩错。
	/// </summary>
	public static double SystemScale() {
		try {
			using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
			if (g.DpiX > 0) return g.DpiX / 96.0;
		}
		catch { }
		// 回退：虚拟屏 PX/DIP（System Aware 下等于系统缩放）
		try {
			VirtualScreenScale(out var sx, out _);
			if (sx >= 0.25) return sx;
		}
		catch { }
		return 1.0;
	}

	/// <summary>物理像素尺寸 → WPF DIP（按进程系统缩放）。</summary>
	public static double PxToDip(double physicalPx) {
		var s = SystemScale();
		return s > 0 ? physicalPx / s : physicalPx;
	}

	/// <summary>指定物理坐标附近显示器的缩放（96 DPI = 1.0）。</summary>
	public static double GetMonitorScale(int physX, int physY) {
		try {
			var mon = MonitorFromPoint(new POINT { X = physX, Y = physY }, MONITOR_DEFAULTTONEAREST);
			if (mon != IntPtr.Zero
				&& GetDpiForMonitor(mon, MDT_EFFECTIVE_DPI, out var dx, out var dy) == 0
				&& dx > 0)
				return dx / 96.0;
		}
		catch { }
		return SystemScale();
	}

	/// <summary>虚拟屏 DIP 尺寸（WPF SystemParameters）。</summary>
	public static (double left, double top, double width, double height) VirtualScreenDip() {
		return (
			SystemParameters.VirtualScreenLeft,
			SystemParameters.VirtualScreenTop,
			Math.Max(1.0, SystemParameters.VirtualScreenWidth),
			Math.Max(1.0, SystemParameters.VirtualScreenHeight));
	}

	/// <summary>
	/// 虚拟屏整体 DIP→物理 缩放（单 DPI 精确；混合 DPI 为平均比例，作回退用）。
	/// </summary>
	public static void VirtualScreenScale(out double scaleX, out double scaleY) {
		var (_, _, dipW, dipH) = VirtualScreenDip();
		var (_, _, pxW, pxH) = VirtualScreenPixels();
		scaleX = pxW / dipW;
		scaleY = pxH / dipH;
		if (scaleX < 0.25) scaleX = 1;
		if (scaleY < 0.25) scaleY = 1;
	}

	/// <summary>
	/// 将屏幕 DIP 坐标转为物理像素（绝对，含虚拟屏负坐标）。
	/// 用「相对虚拟屏原点的 DIP × 虚拟屏物理/DIP 比例」；不用 LogicalToPhysical（PerMonitorV2 下无效）。
	/// </summary>
	public static void DipToPhysical(double dipX, double dipY, IntPtr hwndHint, out int physX, out int physY) {
		_ = hwndHint;
		var (vLeft, vTop, _, _) = VirtualScreenDip();
		var (vsL, vsT, _, _) = VirtualScreenPixels();
		VirtualScreenScale(out var scaleX, out var scaleY);

		// 相对虚拟屏左上角的 DIP → 位图像素 → 绝对物理
		var relX = dipX - vLeft;
		var relY = dipY - vTop;
		physX = vsL + (int)Math.Round(relX * scaleX);
		physY = vsT + (int)Math.Round(relY * scaleY);

		// 混合 DPI：用落点显示器的有效 DPI 再精修（相对该屏物理原点）
		try {
			var pt = new POINT { X = physX, Y = physY };
			var mon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
			if (mon == IntPtr.Zero) return;
			if (GetDpiForMonitor(mon, MDT_EFFECTIVE_DPI, out var dx, out var dy) != 0 || dx == 0 || dy == 0)
				return;

			// 找到该屏物理 bounds
			System.Drawing.Rectangle bounds = default;
			var found = false;
			foreach (var s in System.Windows.Forms.Screen.AllScreens) {
				var c = s.Bounds;
				if (physX >= c.Left && physX < c.Right && physY >= c.Top && physY < c.Bottom) {
					bounds = c;
					found = true;
					break;
				}
			}
			if (!found) {
				foreach (var s in System.Windows.Forms.Screen.AllScreens) {
					if (s.Bounds.Contains(physX, physY) || s.Bounds.Contains(physX - 1, physY - 1)) {
						bounds = s.Bounds;
						found = true;
						break;
					}
				}
			}
			if (!found) return;

			// 该屏在「均匀缩放」下对应的 DIP 矩形（近似；跨屏仍可能有 1px 误差）
			var monDipX = vLeft + (bounds.Left - vsL) / scaleX;
			var monDipY = vTop + (bounds.Top - vsT) / scaleY;
			var relMonDipX = dipX - monDipX;
			var relMonDipY = dipY - monDipY;
			var monScaleX = dx / 96.0;
			var monScaleY = dy / 96.0;
			physX = bounds.Left + (int)Math.Round(relMonDipX * monScaleX);
			physY = bounds.Top + (int)Math.Round(relMonDipY * monScaleY);
		}
		catch { }
	}

	/// <summary>
	/// 选区（overlay 画布坐标：相对虚拟屏左上角的 DIP）→ 整屏位图上的像素矩形。
	/// </summary>
	public static Int32Rect DipSelectionToBitmapRect(
		double selX, double selY, double selW, double selH,
		int deskW, int deskH, IntPtr hwndOverlay) {
		_ = hwndOverlay;
		var (vLeft, vTop, _, _) = VirtualScreenDip();

		// 四角 DIP（屏幕绝对）→ 物理，取包围盒
		var corners = new[] {
			(vLeft + selX, vTop + selY),
			(vLeft + selX + selW, vTop + selY),
			(vLeft + selX, vTop + selY + selH),
			(vLeft + selX + selW, vTop + selY + selH),
		};
		int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
		foreach (var (dx, dy) in corners) {
			DipToPhysical(dx, dy, IntPtr.Zero, out var px, out var py);
			if (px < minX) minX = px;
			if (py < minY) minY = py;
			if (px > maxX) maxX = px;
			if (py > maxY) maxY = py;
		}

		var (vsL, vsT, _, _) = VirtualScreenPixels();
		return ClampToDesk(minX - vsL, minY - vsT, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY), deskW, deskH);
	}

	/// <summary>
	/// 用整屏物理/DIP 均匀比例，把 overlay 选区 DIP 直接映射到位图像素（单 DPI 最稳）。
	/// </summary>
	public static Int32Rect DipSelectionByUniformScale(
		double selX, double selY, double selW, double selH,
		int deskW, int deskH) {
		VirtualScreenScale(out var scaleX, out var scaleY);
		// 若调用方已有 desk 尺寸，优先用 desk / VirtualScreen DIP（与底图像素一致）
		var (_, _, dipW, dipH) = VirtualScreenDip();
		if (deskW > 0 && deskH > 0) {
			scaleX = deskW / dipW;
			scaleY = deskH / dipH;
		}
		var rx = (int)Math.Floor(selX * scaleX);
		var ry = (int)Math.Floor(selY * scaleY);
		var rw = Math.Max(1, (int)Math.Ceiling((selX + selW) * scaleX) - rx);
		var rh = Math.Max(1, (int)Math.Ceiling((selY + selH) * scaleY) - ry);
		return ClampToDesk(rx, ry, rw, rh, deskW, deskH);
	}

	public static Int32Rect ClampToDesk(int rx, int ry, int rw, int rh, int deskW, int deskH) {
		if (deskW < 1) deskW = 1;
		if (deskH < 1) deskH = 1;
		if (rx < 0) { rw += rx; rx = 0; }
		if (ry < 0) { rh += ry; ry = 0; }
		if (rx + rw > deskW) rw = deskW - rx;
		if (ry + rh > deskH) rh = deskH - ry;
		if (rw < 1) rw = 1;
		if (rh < 1) rh = 1;
		if (rx >= deskW) rx = deskW - 1;
		if (ry >= deskH) ry = deskH - 1;
		return new Int32Rect(rx, ry, rw, rh);
	}

	/// <summary>
	/// 物理点所在显示器 → 相对虚拟屏原点的 DIP 矩形（与 HUD/overlay 画布同坐标系）。
	/// 混合 DPI 下用虚拟屏均匀比例，作 UI 钳位足够。
	/// </summary>
	public static void MonitorDipFromPhysical(int physX, int physY,
		out double left, out double top, out double width, out double height) {
		var (vl, vt, _, _) = VirtualScreenPixels();
		VirtualScreenScale(out var sx, out var sy);
		if (sx < 0.25) sx = 1;
		if (sy < 0.25) sy = 1;
		System.Drawing.Rectangle bounds;
		try {
			var scr = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(physX, physY));
			bounds = scr.Bounds;
		}
		catch {
			bounds = new System.Drawing.Rectangle(vl, vt,
				System.Windows.Forms.SystemInformation.VirtualScreen.Width,
				System.Windows.Forms.SystemInformation.VirtualScreen.Height);
		}
		left = (bounds.Left - vl) / sx;
		top = (bounds.Top - vt) / sy;
		width = Math.Max(1, bounds.Width / sx);
		height = Math.Max(1, bounds.Height / sy);
	}

	/// <summary>诊断文本：各显示器 Bounds / 工作区 / DPI。</summary>
	public static string BuildReport() {
		var sb = new StringBuilder();
		sb.AppendLine("=== 显示器 / DPI ===");
		var (dl, dt, dw, dh) = VirtualScreenDip();
		sb.AppendLine($"VirtualScreen DIP: L={dl}, T={dt}, W={dw}, H={dh}");
		var (vl, vt, vw, vh) = VirtualScreenPixels();
		sb.AppendLine($"VirtualScreen PX:  L={vl}, T={vt}, W={vw}, H={vh}");
		VirtualScreenScale(out var sx, out var sy);
		sb.AppendLine($"VirtualScreen scale (PX/DIP): {sx:0.####} x {sy:0.####}");
		sb.AppendLine($"SystemScale (WPF DIP): {SystemScale():0.####}");
		try {
			using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
			sb.AppendLine($"System DPI (GDI): {g.DpiX:0.##} x {g.DpiY:0.##} (scale {g.DpiX / 96.0:0.##}x)");
		}
		catch (Exception ex) {
			sb.AppendLine("System DPI: " + ex.Message);
		}
		try {
			var i = 0;
			foreach (var s in System.Windows.Forms.Screen.AllScreens) {
				i++;
				sb.AppendLine($"--- Screen {i} {(s.Primary ? "(Primary)" : "")} ---");
				sb.AppendLine($"  DeviceName: {s.DeviceName}");
				sb.AppendLine($"  Bounds PX:  {s.Bounds}");
				sb.AppendLine($"  WorkingArea: {s.WorkingArea}");
				try {
					var c = s.Bounds.Location;
					var mon = MonitorFromPoint(new POINT { X = c.X + 1, Y = c.Y + 1 }, MONITOR_DEFAULTTONEAREST);
					if (mon != IntPtr.Zero && GetDpiForMonitor(mon, MDT_EFFECTIVE_DPI, out var dx, out var dy) == 0)
						sb.AppendLine($"  Effective DPI: {dx} x {dy} (scale {dx / 96.0:0.##}x)");
				}
				catch { }
			}
		}
		catch (Exception ex) {
			sb.AppendLine("Enum screens: " + ex.Message);
		}
		return sb.ToString();
	}
}
