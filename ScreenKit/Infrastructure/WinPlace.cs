using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ScreenKit;

/// <summary>
/// 窗口位置钳位：启动或恢复上次位置时把窗口拽回某个显示器里，
/// 避免改过多屏布局（拔掉副屏、换分辨率、混合 DPI）后窗口落在所有屏幕之外或屏幕之间的空隙里。
/// 全部换算都走本窗口的 <c>CompositionTarget</c>（与 Left/Top 同一套 DIP），不自己猜 DPI 比例。
/// 已经在某块屏里完整可见的窗口一律不动（用户可能故意贴边）；只挪位置不改大小，最大化/最小化不处理。
/// </summary>
static class WinPlace {
	/// <summary>把窗口挪进与它交叠最多的那块屏；完全在屏幕外就居中到主屏。</summary>
	public static void KeepOnScreen(Window w) {
		if (w == null) return;
		try {
			if (w.WindowState != WindowState.Normal) return;
			// 刚设过 Width/Height 时 ActualWidth 可能还是旧值，先落一次布局
			w.UpdateLayout();
			var ww = w.ActualWidth > 1 ? w.ActualWidth : (w.Width > 1 ? w.Width : 0);
			var wh = w.ActualHeight > 1 ? w.ActualHeight : (w.Height > 1 ? w.Height : 0);
			if (ww < 1 || wh < 1) return;
			var left = double.IsNaN(w.Left) ? 0 : w.Left;
			var top = double.IsNaN(w.Top) ? 0 : w.Top;

			// 整窗已经完整落在某块屏里就别动
			if (fullyvisible(w, left, top, ww, wh)) return;

			// 有交叠就塞进那块屏，完全没有交叠（掉进屏间空隙 / 副屏已拔）就在主屏居中
			var area = pick(w, left, top, ww, wh);
			double l, t;
			if (area != null) {
				var (al, at, aw, ah) = area.Value;
				var maxL = aw <= ww ? al : al + aw - ww;
				var maxT = ah <= wh ? at : at + ah - wh;
				l = clamp(left, al, maxL);
				t = clamp(top, at, maxT);
			}
			else {
				var r = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
				var (al, at) = todip(w, r.Left, r.Top);
				var (ar, ab) = todip(w, r.Right, r.Bottom);
				l = al + Math.Max(0, (ar - al - ww) / 2);
				t = at + Math.Max(0, (ab - at - wh) / 2);
			}
			if (Math.Abs(l - left) < 0.5 && Math.Abs(t - top) < 0.5) return;
			w.Left = l;
			w.Top = t;
		}
		catch { }
	}

	/// <summary>
	/// 首次绘制完再钳一次，保证晚于窗口自己的 place()（多数窗口在 Loaded/ContentRendered 里摆位置）；
	/// 之后用户自己拖到哪就哪，不再干预。
	/// </summary>
	public static void Attach(Window w) {
		if (w == null) return;
		var done = false;
		w.ContentRendered += (_, _) => {
			if (done) return;
			done = true;
			KeepOnScreen(w);
		};
	}

	static double clamp(double v, double min, double max) {
		if (max < min) max = min;
		return v < min ? min : (v > max ? max : v);
	}

	/// <summary>整窗（含边框）是否已完整落在某块屏的边界内（看 Bounds 不看工作区，允许盖住任务栏）。</summary>
	static bool fullyvisible(Window w, double left, double top, double ww, double wh) {
		try {
			foreach (var scr in System.Windows.Forms.Screen.AllScreens) {
				var r = scr.Bounds;
				var (al, at) = todip(w, r.Left, r.Top);
				var (ar, ab) = todip(w, r.Right, r.Bottom);
				const double tol = 2;
				if (left >= al - tol && top >= at - tol && left + ww <= ar + tol && top + wh <= ab + tol)
					return true;
			}
		}
		catch { }
		return false;
	}

	/// <summary>与窗口交叠面积最大的那块屏的工作区（已换算成本窗口 DIP）；都不交叠返回 null。</summary>
	static (double l, double t, double w, double h)? pick(Window w, double left, double top, double ww, double wh) {
		double bestScore = 0;
		(double, double, double, double)? best = null;
		try {
			foreach (var scr in System.Windows.Forms.Screen.AllScreens) {
				var r = scr.WorkingArea;
				var (al, at) = todip(w, r.Left, r.Top);
				var (ar, ab) = todip(w, r.Right, r.Bottom);
				var aw = Math.Max(1.0, ar - al);
				var ah = Math.Max(1.0, ab - at);
				var ox = Math.Min(left + ww, al + aw) - Math.Max(left, al);
				var oy = Math.Min(top + wh, at + ah) - Math.Max(top, at);
				if (ox <= 0 || oy <= 0) continue;
				var score = ox * oy;
				if (score <= bestScore) continue;
				bestScore = score;
				best = (al, at, aw, ah);
			}
		}
		catch { }
		return best;
	}

	/// <summary>物理像素 → 本窗口 DIP。没有 CompositionTarget 时按 1:1 兜底。</summary>
	static (double x, double y) todip(Window w, double px, double py) {
		try {
			var src = PresentationSource.FromVisual(w);
			if (src?.CompositionTarget != null) {
				var p = src.CompositionTarget.TransformFromDevice.Transform(new Point(px, py));
				return (p.X, p.Y);
			}
		}
		catch { }
		return (px, py);
	}
}
