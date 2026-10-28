using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

namespace ScreenKit;

/// <summary>系统插入符拿不到时，用 UI Automation 取焦点文本里的光标矩形，给 Chromium 这类自绘光标的窗口定位候选条。
/// 查询放在后台线程：第一次打开 Chrome 的辅助功能可能要上百毫秒，不能占着装键盘钩子的 UI 线程。</summary>
static class ImeUiaCaret {
	const int FRESH = 800;
	static readonly object gate = new();
	static readonly AutoResetEvent wake = new(false);
	static Thread worker;
	static int pending;
	static bool found;
	static int px, py, stamp;
	static IntPtr owner;

	/// <summary>后台查到新光标时触发，在线程池线程上。</summary>
	public static event Action Found;

	[DllImport("user32.dll")]
	static extern IntPtr GetForegroundWindow();

	public static void Request() {
		start();
		if (Interlocked.CompareExchange(ref pending, 1, 0) == 0) {
			try { wake.Set(); } catch { }
		}
	}

	/// <summary>拿同一前台窗口的新鲜结果。超过 <see cref="FRESH"/> 毫秒或换了窗口就作废。</summary>
	public static bool TryGet(IntPtr fg, out int x, out int y) {
		x = 0;
		y = 0;
		lock (gate) {
			if (!found || owner != fg) return false;
			int age = Environment.TickCount - stamp;
			if (age < 0 || age > FRESH) return false;
			x = px;
			y = py;
			return true;
		}
	}

	static void start() {
		lock (gate) {
			if (worker != null) return;
			worker = new Thread(loop) { IsBackground = true, Name = "ime-uia-caret" };
			worker.SetApartmentState(ApartmentState.STA);
			worker.Start();
		}
	}

	static void loop() {
		while (true) {
			try {
				wake.WaitOne(1000);
				Interlocked.Exchange(ref pending, 0);
				var fg = GetForegroundWindow();
				bool ok = query();
				lock (gate) {
					found = ok;
					stamp = Environment.TickCount;
					owner = fg;
				}
				if (ok) {
					try { Found?.Invoke(); } catch { }
				}
			}
			catch { }
		}
	}

	/// <summary>焦点元素当前的文本选区；输入法组字时是折叠光标，取最后一个矩形就是光标所在行。</summary>
	static bool query() {
		var el = AutomationElement.FocusedElement;
		if (el == null) return false;
		if (!el.TryGetCurrentPattern(TextPattern.Pattern, out var pat) || pat is not TextPattern tp) return false;
		var ranges = tp.GetSelection();
		if (ranges == null || ranges.Length == 0) return false;
		var rects = ranges[ranges.Length - 1].GetBoundingRectangles();
		if (rects == null || rects.Length == 0) return false;
		var r = rects[rects.Length - 1];
		if (r.Height <= 0) return false;
		lock (gate) {
			px = (int)Math.Round(r.Left);
			py = (int)Math.Round(r.Bottom);
		}
		return true;
	}
}
