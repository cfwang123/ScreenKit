using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>选中文字的矩形和正文。Bounds 是屏幕物理像素。</summary>
sealed class SelHit {
	public string Text = "";
	public Rect Bounds = Rect.Empty;
}

/// <summary>
/// 划词。低级鼠标钩子看拖选和双击，松开后把选中文字交给查词浮窗。
/// 本进程和其它程序都读。词典详情由词典页自己弹出，钩子跳过那一块。
/// 文字用 UI Automation 的 TextPattern 读。
/// </summary>
static class SelBar {
	const int WH_MOUSE_LL = 14;
	const int WH_KEYBOARD_LL = 13;
	const int WM_LBUTTONDOWN = 0x0201;
	const int WM_LBUTTONUP = 0x0202;
	const int WM_MOUSEMOVE = 0x0200;
	const int WM_KEYDOWN = 0x0100;
	const int WM_SYSKEYDOWN = 0x0104;
	const int VK_ESCAPE = 0x1B;
	const int GA_ROOT = 2;
	const int DRAG = 6;
	const int UIA_MS = 1600;

	static IntPtr mouseHook, keyHook;
	static HookProc mouseProc, keyProc;
	static Dispatcher disp;
	static bool on;
	static volatile bool shown;
	static int readers;
	static bool down, hadUp;
	static int downX, downY, lastUpX, lastUpY, lastUpTick, lastMoveTick, gen;
	static IntPtr selroot;

	public static bool On => on;
	public static event Action<string, Rect> Picked;
	public static event Action Hiding;
	public static Func<IntPtr> PopupHwnd;
	static readonly List<WeakReference<FrameworkElement>> details = new();

	/// <summary>词典详情。点在这里时钩子不读选区，由词典页弹出同一个浮窗。</summary>
	public static void WatchDetail(FrameworkElement el) {
		if (el == null) return;
		dropdetail(el);
		details.Add(new WeakReference<FrameworkElement>(el));
	}

	public static void UnwatchDetail(FrameworkElement el) {
		if (el == null) return;
		dropdetail(el);
	}

	static void dropdetail(FrameworkElement el) {
		for (var i = details.Count - 1; i >= 0; i--) {
			if (!details[i].TryGetTarget(out var t) || ReferenceEquals(t, el)) details.RemoveAt(i);
		}
	}

	internal static bool OverDetail(int x, int y) {
		foreach (var one in details) {
			if (!one.TryGetTarget(out var el) || !el.IsVisible) continue;
			try {
				var local = el.PointFromScreen(new Point(x, y));
				if (local.X >= 0 && local.Y >= 0 && local.X < el.ActualWidth && local.Y < el.ActualHeight)
					return true;
			}
			catch { }
		}
		return false;
	}

	public static void SetOn(bool value) {
		if (value == on) return;
		if (!value) {
			on = false;
			gen++;
			hide();
			unhook();
			return;
		}
		disp = Application.Current?.Dispatcher;
		if (disp == null || !hookup()) {
			on = false;
			unhook();
			try { UiToast.Show(null, Loc.T("selbar.hookfail")); } catch { }
			return;
		}
		on = true;
	}

	public static void Shutdown() {
		on = false;
		gen++;
		hide();
		unhook();
	}

	/// <summary>关掉当前外部划词浮窗，不关划词开关。</summary>
	public static void DismissShown() {
		gen++;
		hide();
	}

	/// <summary>拖选或双击才弹出。贴着窗口右缘的竖拖当成滚动条，不弹。</summary>
	internal static bool WantBar(bool drag, bool dbl, bool scroll) {
		if (scroll && !dbl) return false;
		return drag || dbl;
	}

	internal static bool IsScrollbarDrag(int x, int downX, int downY, int upY, int winRight) {
		var vertical = Math.Abs(upY - downY) > Math.Abs(x - downX);
		return x > winRight - 28 && vertical;
	}

	internal static SelHit Read(int x, int y) {
		SelHit hit = null;
		try { hit = fromel(AutomationElement.FromPoint(new Point(x, y))); } catch { }
		if (hit != null && hit.Text.Length > 0) return hit;
		try {
			var focused = fromel(AutomationElement.FocusedElement);
			if (focused != null && focused.Text.Length > 0) return focused;
		}
		catch { }
		return hit;
	}

	public static int Test(Action<string> log) {
		void say(string s) { try { log?.Invoke(s); } catch { } }
		if (!DictWindow.SelWord("hello") || !DictWindow.SelWord("well-known") || !DictWindow.SelWord("你好") || !DictWindow.SelWord("マル・クス")) {
			say("FAIL word");
			return 1;
		}
		if (DictWindow.SelWord("hello, world") || DictWindow.SelWord("结束。") || DictWindow.SelWord(new string('a', 49)) || DictWindow.SelWord(new string('中', 21))) {
			say("FAIL skip");
			return 1;
		}
		if (!WantBar(true, false, false) || !WantBar(false, true, false) || WantBar(false, false, false) || WantBar(true, false, true) || !WantBar(false, true, true)) {
			say("FAIL gesture");
			return 1;
		}
		if (!IsScrollbarDrag(990, 988, 100, 180, 1000) || IsScrollbarDrag(400, 200, 100, 140, 1000)) {
			say("FAIL scroll");
			return 1;
		}
		if (Application.Current != null)
			Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
		Window probe = null;
		System.Windows.Controls.TextBox box = null;
		try {
			box = new System.Windows.Controls.TextBox {
				Text = "payment details",
				FontSize = 16,
				Width = 280,
				Height = 36,
				Padding = new Thickness(8, 6, 8, 6),
			};
			probe = new Window {
				Title = "selbar-probe",
				Width = 360,
				Height = 140,
				WindowStartupLocation = WindowStartupLocation.CenterScreen,
				Content = box,
			};
			probe.Show();
			probe.Activate();
			box.Focus();
			box.SelectAll();
			for (var i = 0; i < 8; i++) pump();
			var hwndp = new WindowInteropHelper(probe).Handle;
			var root = AutomationElement.FromHandle(hwndp);
			var edit = root?.FindFirst(TreeScope.Descendants,
				new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
			var hit = edit == null ? null : fromel(edit);
			if (hit != null && !hit.Bounds.IsEmpty) {
				var cx = (int)(hit.Bounds.Left + hit.Bounds.Width / 2);
				var cy = (int)(hit.Bounds.Top + hit.Bounds.Height / 2);
				var at = Read(cx, cy);
				say($"fromhandle={hit.Text} at {cx},{cy} read={at?.Text ?? ""}");
				if (at != null && at.Text.Length > 0) hit = at;
			}
			else say("fromhandle empty");
			var text = hit?.Text ?? "";
			say($"uia text={text} bounds={(hit == null ? "null" : hit.Bounds.ToString())}");
			if (text.IndexOf("payment", StringComparison.OrdinalIgnoreCase) < 0) {
				say("FAIL: 没有读到选中文字");
				return 2;
			}
		}
		catch (Exception ex) {
			say("probe " + ex.Message);
			return 2;
		}
		finally {
			try { probe?.Close(); } catch { }
		}
		SelBarWindow win = null;
		try {
			win = new SelBarWindow();
			var sel = new Rect(240, 220, 160, 28);
			win.ShowAt(sel);
			for (var i = 0; i < 6; i++) pump();
			var hwnd = new WindowInteropHelper(win).Handle;
			var px = win.ScreenPx();
			say($"bar {px.Width:0}x{px.Height:0} at {px.Left:0},{px.Top:0} noact={SelBarWindow.HasNoActivate(hwnd)} label={win.SearchLabel}");
			if (px.Width < 240 || px.Height < 200 || px.Height > 900 || win.SearchLabel.Length == 0) {
				say("FAIL: 浮窗尺寸不对");
				return 3;
			}
			if (!SelBarWindow.HasNoActivate(hwnd)) {
				say("FAIL: 浮窗会抢焦点");
				return 4;
			}
			var gapBelow = px.Top - (sel.Top + sel.Height);
			var overlaps = px.Top < sel.Bottom - 1 && px.Bottom > sel.Top + 1;
			var near = px.Right > sel.Left - 8 && px.Left < sel.Right + 8;
			say($"gapBelow={gapBelow:0} overlaps={overlaps} near={near}");
			if (overlaps || gapBelow < -2 || gapBelow > 48 || !near) {
				say("FAIL: 浮窗挡住选区或离得太远");
				return 5;
			}
		}
		catch (Exception ex) {
			say("bar " + ex.Message);
			return 3;
		}
		finally {
			try { win?.Close(); } catch { }
		}
		say("=== OK：划词查词浮窗 ===");
		return 0;

		static void pump() {
			var frame = new DispatcherFrame();
			Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
			Dispatcher.PushFrame(frame);
		}
	}

	static bool hookup() {
		mouseProc = onmouse;
		keyProc = onkey;
		var mod = GetModuleHandle(null);
		mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, mod, 0);
		keyHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyProc, mod, 0);
		if (mouseHook != IntPtr.Zero && keyHook != IntPtr.Zero) return true;
		unhook();
		return false;
	}

	static void unhook() {
		var m = mouseHook;
		var k = keyHook;
		mouseHook = IntPtr.Zero;
		keyHook = IntPtr.Zero;
		mouseProc = null;
		keyProc = null;
		if (m != IntPtr.Zero) try { UnhookWindowsHookEx(m); } catch { }
		if (k != IntPtr.Zero) try { UnhookWindowsHookEx(k); } catch { }
	}

	static void hide() {
		var was = shown || selroot != IntPtr.Zero;
		shown = false;
		selroot = IntPtr.Zero;
		if (!was) return;
		try { Hiding?.Invoke(); } catch { }
	}

	static IntPtr onmouse(int code, IntPtr wParam, IntPtr lParam) {
		if (code >= 0 && on) {
			try { onmouseinner(wParam.ToInt32(), lParam); } catch { }
		}
		return CallNextHookEx(mouseHook, code, wParam, lParam);
	}

	static IntPtr onkey(int code, IntPtr wParam, IntPtr lParam) {
		if (code >= 0 && on && shown) {
			try {
				var msg = wParam.ToInt32();
				if ((msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) && Marshal.ReadInt32(lParam) == VK_ESCAPE) {
					gen++;
					disp?.BeginInvoke(new Action(hide));
				}
			}
			catch { }
		}
		return CallNextHookEx(keyHook, code, wParam, lParam);
	}

	static void onmouseinner(int msg, IntPtr lParam) {
		var x = Marshal.ReadInt32(lParam);
		var y = Marshal.ReadInt32(lParam, 4);
		if (msg == WM_MOUSEMOVE) {
			if (!shown) return;
			if (Environment.TickCount - lastMoveTick < 200) return;
			lastMoveTick = Environment.TickCount;
			if (onpopup(x, y)) return;
			var h = WindowFromPoint(new POINT { X = x, Y = y });
			var root = h == IntPtr.Zero ? IntPtr.Zero : GetAncestor(h, GA_ROOT);
			if (root != IntPtr.Zero && root == selroot) return;
			gen++;
			disp?.BeginInvoke(new Action(hide));
			return;
		}
		if (msg == WM_LBUTTONDOWN) {
			if (onpopup(x, y)) return;
			gen++;
			down = true;
			downX = x;
			downY = y;
			disp?.BeginInvoke(new Action(hide));
			return;
		}
		if (msg != WM_LBUTTONUP) return;
		if (onpopup(x, y)) {
			down = false;
			return;
		}
		if (!down) return;
		down = false;
		if (OverDetail(x, y)) return;
		var dx = x - downX;
		var dy = y - downY;
		var drag = dx * (long)dx + dy * (long)dy >= DRAG * (long)DRAG;
		var dbl = hadUp && Environment.TickCount - lastUpTick < GetDoubleClickTime() &&
			Math.Abs(x - lastUpX) < 8 && Math.Abs(y - lastUpY) < 8;
		hadUp = true;
		lastUpX = x;
		lastUpY = y;
		lastUpTick = Environment.TickCount;
		var fg = GetForegroundWindow();
		var scroll = false;
		if (fg != IntPtr.Zero && GetWindowRect(fg, out var rc))
			scroll = IsScrollbarDrag(x, downX, downY, y, rc.Right);
		if (!WantBar(drag, dbl, scroll)) return;
		var g = ++gen;
		var ux = x;
		var uy = y;
		var rootfg = fg;
		if (Interlocked.Increment(ref readers) > 2) {
			Interlocked.Decrement(ref readers);
			return;
		}
		var box = new UiaBox();
		var th = new Thread(() => {
			try { box.Hit = Read(ux, uy); } catch { }
			box.Done = true;
		});
		th.IsBackground = true;
		th.Start();
		var wait = new Thread(() => {
			var t0 = Environment.TickCount;
			while (!box.Done && Environment.TickCount - t0 < UIA_MS)
				Thread.Sleep(20);
			Interlocked.Decrement(ref readers);
			disp?.BeginInvoke(new Action(() => {
				if (!on || g != gen || !box.Done) return;
				if (box.Hit == null || box.Hit.Text.Length == 0) return;
				var bounds = box.Hit.Bounds.IsEmpty ? new Rect(ux, uy - 4, 2, 18) : box.Hit.Bounds;
				bounds = selanchor(rootfg, bounds, ux, uy);
				selroot = rootfg;
				shown = true;
				try { Picked?.Invoke(box.Hit.Text, bounds); } catch { }
			}));
		});
		wait.IsBackground = true;
		wait.Start();
	}

	static SelHit fromel(AutomationElement el) {
		for (var i = 0; i < 8 && el != null; i++) {
			try {
				if (el.TryGetCurrentPattern(TextPattern.Pattern, out var pat) && pat is TextPattern tp) {
					var ranges = tp.GetSelection();
					if (ranges != null && ranges.Length > 0) {
						var text = (ranges[0].GetText(8000) ?? "").Trim();
						if (text.Length > 0)
							return new SelHit { Text = text, Bounds = union(ranges[0].GetBoundingRectangles()) };
					}
				}
			}
			catch { }
			try { el = TreeWalker.ControlViewWalker.GetParent(el); }
			catch { break; }
		}
		return null;
	}

	static Rect union(Rect[] rects) {
		if (rects == null || rects.Length == 0) return Rect.Empty;
		double l = double.MaxValue, t = double.MaxValue, r = double.MinValue, b = double.MinValue;
		foreach (var one in rects) {
			if (one.Width <= 0 || one.Height <= 0 || one.IsEmpty) continue;
			if (one.Left < l) l = one.Left;
			if (one.Top < t) t = one.Top;
			if (one.Right > r) r = one.Right;
			if (one.Bottom > b) b = one.Bottom;
		}
		if (r <= l || b <= t) return Rect.Empty;
		return new Rect(l, t, r - l, b - t);
	}

	static bool onpopup(int x, int y) {
		IntPtr hwnd;
		try { hwnd = PopupHwnd != null ? PopupHwnd() : IntPtr.Zero; }
		catch { return false; }
		if (hwnd == IntPtr.Zero) return false;
		if (!GetWindowRect(hwnd, out var rc)) return false;
		return x >= rc.Left && x < rc.Right && y >= rc.Top && y < rc.Bottom;
	}

	static Rect selanchor(IntPtr root, Rect bounds, int ux, int uy) {
		var vroot = root;
		if (vroot != IntPtr.Zero && winclass(vroot) != "Vim") {
			var anc = GetAncestor(vroot, GA_ROOT);
			if (anc != IntPtr.Zero && winclass(anc) == "Vim") vroot = anc;
		}
		if (vroot != IntPtr.Zero && winclass(vroot) == "Vim") {
			var probe = ImeCaret.Shared;
			if (probe != null && probe.TryVimCaret(out var vx, out var vy))
				return new Rect(vx, vy - 18, 2, 18);
		}
		if (vroot != IntPtr.Zero && winclass(vroot) == "org.wezfurlong.wezterm") {
			var probe = ImeCaret.Shared;
			if (probe != null && probe.TryWezCaret(out var wx, out var wy))
				return new Rect(wx, wy - 18, 2, 18);
		}
		if (!bounds.IsEmpty && (bounds.Width > 800 || bounds.Height > 400)) {
			double cx = bounds.Left + bounds.Width / 2;
			double cy = bounds.Top + bounds.Height / 2;
			double dx = cx - ux;
			double dy = cy - uy;
			if (dx * dx + dy * dy > 120 * 120)
				return new Rect(ux, uy - 4, 2, 18);
		}
		return bounds;
	}

	static string winclass(IntPtr hwnd) {
		var buf = new System.Text.StringBuilder(64);
		int n = GetClassName(hwnd, buf, buf.Capacity);
		return n <= 0 ? "" : buf.ToString();
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder buf, int n);

	[DllImport("user32.dll")]
	static extern IntPtr GetAncestor(IntPtr hwnd, uint ga);

	sealed class UiaBox {
		public SelHit Hit;
		public volatile bool Done;
	}

	delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

	[StructLayout(LayoutKind.Sequential)]
	struct POINT {
		public int X;
		public int Y;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct RECT {
		public int Left, Top, Right, Bottom;
	}

	[DllImport("user32.dll")]
	static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

	[DllImport("user32.dll")]
	static extern bool UnhookWindowsHookEx(IntPtr hhk);

	[DllImport("user32.dll")]
	static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	static extern IntPtr WindowFromPoint(POINT pt);

	[DllImport("user32.dll")]
	static extern IntPtr GetAncestor(IntPtr hwnd, int flags);

	[DllImport("user32.dll")]
	static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	static extern bool GetWindowRect(IntPtr hWnd, out RECT rc);

	[DllImport("user32.dll")]
	static extern uint GetDoubleClickTime();

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	static extern IntPtr GetModuleHandle(string name);
}
