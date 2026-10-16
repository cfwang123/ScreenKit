using System;
using System.Runtime.InteropServices;
using System.Text;
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
/// Chrome 划词占位条。低级鼠标钩子看拖选和双击，松开后在选区旁显示工具条。
/// 文字用 UI Automation 的 TextPattern 读，读不到时仍按光标位置显示。
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
	const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
	const int DRAG = 6;
	const int UIA_MS = 1600;

	static SelBarWindow bar;
	static IntPtr mouseHook, keyHook;
	static HookProc mouseProc, keyProc;
	static Dispatcher disp;
	static bool on;
	static volatile bool shown;
	static int readers;
	static bool down, hadUp;
	static int downX, downY, lastUpX, lastUpY, lastUpTick, lastFgTick, gen;

	public static bool On => on;
	public static event Action<bool> Changed;

	public static void SetOn(bool value) {
		if (value == on) return;
		if (!value) {
			on = false;
			gen++;
			hide();
			unhook();
			raise();
			return;
		}
		disp = Application.Current?.Dispatcher;
		if (disp == null || !hookup()) {
			on = false;
			unhook();
			try { UiToast.Show(null, Loc.T("selbar.hookfail")); } catch { }
			raise();
			return;
		}
		on = true;
		raise();
	}

	public static void Shutdown() {
		on = false;
		gen++;
		hide();
		unhook();
	}

	public static void ApplyText() {
		try { bar?.ApplyText(); } catch { }
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
			say($"bar {px.Width:0}x{px.Height:0} at {px.Left:0},{px.Top:0} noact={SelBarWindow.HasNoActivate(hwnd)} label={win.lbsearch.Text}");
			if (px.Width < 240 || px.Height < 28 || px.Height > 180 || win.lbsearch.Text.Length == 0) {
				say("FAIL: 浮窗尺寸不对");
				return 3;
			}
			if (!SelBarWindow.HasNoActivate(hwnd)) {
				say("FAIL: 浮窗会抢焦点");
				return 4;
			}
			var gapAbove = sel.Top - (px.Top + px.Height);
			var gapBelow = px.Top - (sel.Top + sel.Height);
			var above = gapAbove >= -2 && gapAbove <= 36;
			var below = gapBelow >= -2 && gapBelow <= 36;
			var near = Math.Abs((px.Left + px.Width / 2) - (sel.Left + sel.Width / 2)) < px.Width;
			say($"gapAbove={gapAbove:0} gapBelow={gapBelow:0} near={near}");
			if ((!above && !below) || !near) {
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
		say("=== OK：划词占位浮窗 ===");
		return 0;

		static void pump() {
			var frame = new DispatcherFrame();
			Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
			Dispatcher.PushFrame(frame);
		}
	}

	static void raise() {
		try { Changed?.Invoke(on); } catch { }
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
		shown = false;
		try {
			if (bar != null && bar.IsVisible) bar.Hide();
		}
		catch { }
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
			if (Environment.TickCount - lastFgTick < 200) return;
			lastFgTick = Environment.TickCount;
			if (onbar(x, y) || ischrome()) return;
			gen++;
			disp?.BeginInvoke(new Action(hide));
			return;
		}
		if (msg == WM_LBUTTONDOWN) {
			if (onbar(x, y)) return;
			gen++;
			down = true;
			downX = x;
			downY = y;
			disp?.BeginInvoke(new Action(hide));
			return;
		}
		if (msg != WM_LBUTTONUP) return;
		if (onbar(x, y)) {
			down = false;
			return;
		}
		if (!down) return;
		down = false;
		if (!ischrome()) return;
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
		disp?.BeginInvoke(new Action(() => {
			if (!on || g != gen) return;
			if (bar != null) bar.Text = "";
			present(ux, uy, null);
		}));
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
				if (bar != null) bar.Text = box.Hit.Text;
				present(ux, uy, box.Hit);
			}));
		});
		wait.IsBackground = true;
		wait.Start();
	}

	static void present(int x, int y, SelHit hit) {
		if (!on) return;
		if (bar == null) {
			bar = new SelBarWindow();
			bar.Act = onact;
		}
		var bounds = hit != null && !hit.Bounds.IsEmpty ? hit.Bounds : new Rect(x, y - 4, 2, 18);
		if (hit != null && !string.IsNullOrEmpty(hit.Text)) bar.Text = hit.Text;
		bar.ShowAt(bounds);
		shown = true;
	}

	static void onact(string id) {
		if (id == "pause") {
			try { UiToast.Show(null, Loc.T("selbar.pause")); } catch { }
			SetOn(false);
			return;
		}
		var label = Loc.T("selbar." + id);
		var t = (bar?.Text ?? "").Trim();
		if (id == "copy") {
			if (t.Length == 0) {
				UiToast.Show(null, Loc.T("selbar.empty"));
				return;
			}
			try {
				Clipboard.SetText(t);
				UiToast.Show(null, Loc.T("selbar.copied"));
			}
			catch {
				UiToast.Show(null, Loc.T("selbar.copyfail"));
			}
			return;
		}
		if (t.Length == 0) UiToast.Show(null, Loc.T("selbar.stubempty", label));
		else UiToast.Show(null, Loc.T("selbar.stub", label, clip(t)));
	}

	static string clip(string t) {
		t = t.Replace('\r', ' ').Replace('\n', ' ').Trim();
		while (t.Contains("  ")) t = t.Replace("  ", " ");
		if (t.Length > 36) return t.Substring(0, 36) + "…";
		return t;
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

	static bool onbar(int x, int y) {
		var hwnd = barhwnd();
		if (hwnd == IntPtr.Zero) return false;
		var h = WindowFromPoint(new POINT { X = x, Y = y });
		if (h == IntPtr.Zero) return false;
		if (h == hwnd) return true;
		return GetAncestor(h, GA_ROOT) == hwnd;
	}

	static IntPtr barhwnd() {
		try {
			if (bar == null) return IntPtr.Zero;
			return new WindowInteropHelper(bar).Handle;
		}
		catch { return IntPtr.Zero; }
	}

	static bool ischrome() {
		var hwnd = GetForegroundWindow();
		if (hwnd == IntPtr.Zero || hwnd == barhwnd()) return false;
		var path = procpath(hwnd);
		return path.EndsWith("\\chrome.exe", StringComparison.OrdinalIgnoreCase);
	}

	static string procpath(IntPtr hwnd) {
		GetWindowThreadProcessId(hwnd, out var pid);
		if (pid == 0) return "";
		var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
		if (h == IntPtr.Zero) return "";
		try {
			var sb = new StringBuilder(520);
			var n = sb.Capacity;
			if (!QueryFullProcessImageName(h, 0, sb, ref n)) return "";
			return sb.ToString();
		}
		finally { CloseHandle(h); }
	}

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
	static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

	[DllImport("user32.dll")]
	static extern bool GetWindowRect(IntPtr hWnd, out RECT rc);

	[DllImport("user32.dll")]
	static extern uint GetDoubleClickTime();

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	static extern IntPtr GetModuleHandle(string name);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern IntPtr OpenProcess(int access, bool inherit, uint pid);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool CloseHandle(IntPtr h);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);
}
