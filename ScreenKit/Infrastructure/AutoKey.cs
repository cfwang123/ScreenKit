using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenKit;

/// <summary>
/// 自动按键：向前台窗口连续发送一次按键或一次鼠标点击。
/// 前台是本进程时不发送。光标不在前台窗口上时，鼠标点客户区中心再把光标移回。
/// </summary>
static class AutoKey {
	public const int INTERVAL_MIN = 20;
	public const int INTERVAL_MAX = 60000;
	public const int INTERVAL_DEFAULT = 200;

	const int INPUT_MOUSE = 0;
	const int INPUT_KEYBOARD = 1;
	const uint KEYEVENTF_KEYUP = 0x0002;
	const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
	const uint MOUSEEVENTF_LEFTUP = 0x0004;
	const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
	const uint MOUSEEVENTF_RIGHTUP = 0x0010;
	const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
	const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
	const uint MOD_ALT = 0x0001;
	const uint MOD_CONTROL = 0x0002;
	const uint MOD_SHIFT = 0x0004;
	const uint MOD_WIN = 0x0008;
	const ushort VK_SHIFT = 0x10;
	const ushort VK_CONTROL = 0x11;
	const ushort VK_MENU = 0x12;
	const ushort VK_LWIN = 0x5B;
	const uint GA_ROOT = 2;
	const uint WM_KEYDOWN = 0x0100;
	const uint WM_KEYUP = 0x0101;
	const uint WM_SYSKEYDOWN = 0x0104;
	const uint WM_SYSKEYUP = 0x0105;
	const uint WM_LBUTTONDOWN = 0x0201;
	const uint WM_LBUTTONUP = 0x0202;
	const uint WM_RBUTTONDOWN = 0x0204;
	const uint WM_RBUTTONUP = 0x0205;
	const uint WM_MBUTTONDOWN = 0x0207;
	const uint WM_MBUTTONUP = 0x0208;
	const uint PM_REMOVE = 0x0001;
	const uint SMTO_ABORTIFHUNG = 0x0002;
	const int MK_LBUTTON = 0x0001;
	const int MK_RBUTTON = 0x0002;
	const int MK_MBUTTON = 0x0010;
	const ushort VK_LSHIFT = 0xA0;
	const ushort VK_LCONTROL = 0xA2;
	const ushort VK_LMENU = 0xA4;
	const int SW_SHOW = 5;

	[StructLayout(LayoutKind.Sequential)]
	struct INPUT {
		public int type;
		public InputUnion u;
	}

	[StructLayout(LayoutKind.Explicit)]
	struct InputUnion {
		[FieldOffset(0)] public MOUSEINPUT mi;
		[FieldOffset(0)] public KEYBDINPUT ki;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct KEYBDINPUT {
		public ushort wVk;
		public ushort wScan;
		public uint dwFlags;
		public uint time;
		public IntPtr dwExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct MOUSEINPUT {
		public int dx;
		public int dy;
		public uint mouseData;
		public uint dwFlags;
		public uint time;
		public IntPtr dwExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct POINT {
		public int X;
		public int Y;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct RECT {
		public int left, top, right, bottom;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct GUITHREADINFO {
		public int cbSize;
		public int flags;
		public IntPtr hwndActive;
		public IntPtr hwndFocus;
		public IntPtr hwndCapture;
		public IntPtr hwndMenuOwner;
		public IntPtr hwndMoveSize;
		public IntPtr hwndCaret;
		public RECT rcCaret;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct MSG {
		public IntPtr hwnd;
		public uint message;
		public IntPtr wParam;
		public IntPtr lParam;
		public uint time;
		public POINT pt;
	}

	[DllImport("user32.dll", SetLastError = true)]
	static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

	[DllImport("user32.dll")]
	static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

	[DllImport("user32.dll")]
	static extern bool GetCursorPos(out POINT pt);

	[DllImport("user32.dll")]
	static extern bool SetCursorPos(int x, int y);

	[DllImport("user32.dll")]
	static extern IntPtr WindowFromPoint(POINT pt);

	[DllImport("user32.dll")]
	static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

	[DllImport("user32.dll")]
	static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

	[DllImport("user32.dll")]
	static extern bool ClientToScreen(IntPtr hwnd, ref POINT pt);

	[DllImport("user32.dll")]
	static extern bool SetForegroundWindow(IntPtr hwnd);

	[DllImport("user32.dll")]
	static extern bool ScreenToClient(IntPtr hwnd, ref POINT pt);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);

	[DllImport("user32.dll")]
	static extern bool GetKeyboardState(byte[] state);

	[DllImport("user32.dll")]
	static extern bool SetKeyboardState(byte[] state);

	[DllImport("user32.dll")]
	static extern uint MapVirtualKey(uint code, uint mapType);

	[DllImport("user32.dll")]
	static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam,
		uint flags, uint timeout, out IntPtr result);

	[DllImport("user32.dll")]
	static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO info);

	[DllImport("kernel32.dll")]
	static extern uint GetCurrentThreadId();

	[DllImport("user32.dll")]
	static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

	[DllImport("user32.dll")]
	static extern IntPtr SetFocus(IntPtr hwnd);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
		int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

	[DllImport("user32.dll")]
	static extern bool DestroyWindow(IntPtr hwnd);

	[DllImport("user32.dll")]
	static extern bool ShowWindow(IntPtr hwnd, int cmd);

	[DllImport("user32.dll")]
	static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);

	[DllImport("user32.dll")]
	static extern bool TranslateMessage(ref MSG msg);

	[DllImport("user32.dll")]
	static extern IntPtr DispatchMessage(ref MSG msg);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	static extern IntPtr GetModuleHandle(string name);

	delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	struct WNDCLASS {
		public uint style;
		public WndProc lpfnWndProc;
		public int cbClsExtra;
		public int cbWndExtra;
		public IntPtr hInstance;
		public IntPtr hIcon;
		public IntPtr hCursor;
		public IntPtr hbrBackground;
		public string lpszMenuName;
		public string lpszClassName;
	}

	static readonly int InputSize = Marshal.SizeOf(typeof(INPUT));
	static WndProc probeProc;
	static int probeKeys;
	static int probeClicks;

	public struct ActionSpec {
		public bool Mouse;
		public int Button;
		public ushort Vk;
		public bool Ctrl;
		public bool Alt;
		public bool Shift;
		public bool Win;
	}

	public static int ClampInterval(int ms) {
		if (ms < INTERVAL_MIN) return INTERVAL_MIN;
		if (ms > INTERVAL_MAX) return INTERVAL_MAX;
		return ms;
	}

	public static int ParseInterval(string text) {
		if (!int.TryParse((text ?? "").Trim(), out var n)) return INTERVAL_DEFAULT;
		return ClampInterval(n);
	}

	/// <summary>mode：key / left / right / middle。按键文本与全局热键相同，如 Ctrl+Shift+A、F8、Space。</summary>
	public static bool TryMake(string mode, string key, out ActionSpec act) {
		act = default;
		mode = (mode ?? "").Trim().ToLowerInvariant();
		if (mode is "left" or "right" or "middle") {
			act.Mouse = true;
			act.Button = mode == "right" ? 1 : mode == "middle" ? 2 : 0;
			return true;
		}
		if (mode.Length > 0 && mode != "key") return false;
		if (!GlobalHotkey.tryparse(key, out var mod, out var vk) || vk == 0) return false;
		act.Vk = (ushort)vk;
		act.Ctrl = (mod & MOD_CONTROL) != 0;
		act.Alt = (mod & MOD_ALT) != 0;
		act.Shift = (mod & MOD_SHIFT) != 0;
		act.Win = (mod & MOD_WIN) != 0;
		return true;
	}

	public static int EventCount(ActionSpec act) {
		if (act.Mouse) return 2;
		var n = 2;
		if (act.Ctrl) n += 2;
		if (act.Alt) n += 2;
		if (act.Shift) n += 2;
		if (act.Win) n += 2;
		return n;
	}

	/// <summary>发送一次。1 已发送，0 前台是本进程或没有前台窗口，-1 发送失败。</summary>
	public static int Send(ActionSpec act) => send(act, true);

	static int send(ActionSpec act, bool skipSelf) {
		var fg = GetForegroundWindow();
		if (fg == IntPtr.Zero) return 0;
		if (skipSelf && isself(fg)) return 0;
		// 中文键盘布局下 WezTerm 会丢掉 SendInput。改把按键和点击直接送到它的窗口过程。
		var ok = iswezterm(fg)
			? (act.Mouse ? clickwez(fg, act.Button) : tapwez(fg, act))
			: (act.Mouse ? click(fg, act.Button) : tap(act));
		return ok ? 1 : -1;
	}

	static bool iswezterm(IntPtr hwnd) {
		var name = new StringBuilder(64);
		if (GetClassName(hwnd, name, name.Capacity) <= 0) return false;
		return name.ToString() == "org.wezfurlong.wezterm";
	}

	static IntPtr targethwnd(IntPtr fg) {
		var info = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
		var tid = GetWindowThreadProcessId(fg, out _);
		if (!GetGUIThreadInfo(tid, ref info) || info.hwndFocus == IntPtr.Zero) return fg;
		if (info.hwndFocus == fg) return fg;
		return GetAncestor(info.hwndFocus, GA_ROOT) == fg ? info.hwndFocus : fg;
	}

	static bool tapwez(IntPtr fg, ActionSpec act) {
		var hwnd = targethwnd(fg);
		var down = new List<ushort>(5);
		if (act.Ctrl) down.Add(VK_CONTROL);
		if (act.Alt) down.Add(VK_MENU);
		if (act.Shift) down.Add(VK_SHIFT);
		if (act.Win) down.Add(VK_LWIN);
		down.Add(act.Vk);
		var tid = GetWindowThreadProcessId(hwnd, out _);
		var me = GetCurrentThreadId();
		var attached = tid != 0 && tid != me && AttachThreadInput(me, tid, true);
		var state = new byte[256];
		var saved = new byte[256];
		var have = GetKeyboardState(state);
		if (have) Buffer.BlockCopy(state, 0, saved, 0, 256);
		try {
			if (have) {
				markdown(state, act, down);
				SetKeyboardState(state);
			}
			foreach (var vk in down)
				if (!postkey(hwnd, vk, false, act.Alt)) return false;
			for (var i = down.Count - 1; i >= 0; i--) {
				if (have) {
					state[down[i]] = (byte)(state[down[i]] & 0x7F);
					clearpair(state, down[i]);
					SetKeyboardState(state);
				}
				if (!postkey(hwnd, down[i], true, act.Alt)) return false;
			}
			return true;
		}
		finally {
			if (have) SetKeyboardState(saved);
			if (attached) AttachThreadInput(me, tid, false);
		}
	}

	static void markdown(byte[] state, ActionSpec act, List<ushort> down) {
		foreach (var vk in down) state[vk] |= 0x80;
		if (act.Shift) state[VK_LSHIFT] |= 0x80;
		if (act.Ctrl) state[VK_LCONTROL] |= 0x80;
		if (act.Alt) state[VK_LMENU] |= 0x80;
	}

	static void clearpair(byte[] state, ushort vk) {
		if (vk == VK_SHIFT) state[VK_LSHIFT] &= 0x7F;
		else if (vk == VK_CONTROL) state[VK_LCONTROL] &= 0x7F;
		else if (vk == VK_MENU) state[VK_LMENU] &= 0x7F;
	}

	static bool postkey(IntPtr hwnd, ushort vk, bool up, bool alt) {
		var msg = alt ? (up ? WM_SYSKEYUP : WM_SYSKEYDOWN) : (up ? WM_KEYUP : WM_KEYDOWN);
		var ok = SendMessageTimeout(hwnd, msg, (IntPtr)vk, keylparam(vk, up),
			SMTO_ABORTIFHUNG, 80, out _);
		return ok != IntPtr.Zero;
	}

	static IntPtr keylparam(ushort vk, bool up) {
		var scan = MapVirtualKey(vk, 4);
		var lp = 1u | ((scan & 0xFFu) << 16);
		if ((scan & 0xFF00u) != 0) lp |= 1u << 24;
		if (up) lp |= (1u << 30) | (1u << 31);
		return (IntPtr)lp;
	}

	static bool clickwez(IntPtr fg, int button) {
		var hwnd = targethwnd(fg);
		if (!clickpoint(hwnd, out var x, out var y)) return false;
		uint down, up;
		int mk;
		if (button == 1) { down = WM_RBUTTONDOWN; up = WM_RBUTTONUP; mk = MK_RBUTTON; }
		else if (button == 2) { down = WM_MBUTTONDOWN; up = WM_MBUTTONUP; mk = MK_MBUTTON; }
		else { down = WM_LBUTTONDOWN; up = WM_LBUTTONUP; mk = MK_LBUTTON; }
		var lp = (IntPtr)((y << 16) | (x & 0xFFFF));
		if (SendMessageTimeout(hwnd, down, (IntPtr)mk, lp, SMTO_ABORTIFHUNG, 80, out _) == IntPtr.Zero)
			return false;
		return SendMessageTimeout(hwnd, up, IntPtr.Zero, lp, SMTO_ABORTIFHUNG, 80, out _) != IntPtr.Zero;
	}

	static bool clickpoint(IntPtr hwnd, out int x, out int y) {
		x = 0;
		y = 0;
		if (GetCursorPos(out var pt)) {
			var hit = WindowFromPoint(pt);
			var on = hit == hwnd || (hit != IntPtr.Zero && GetAncestor(hit, GA_ROOT) == GetAncestor(hwnd, GA_ROOT));
			if (on && ScreenToClient(hwnd, ref pt)) {
				x = pt.X;
				y = pt.Y;
				return true;
			}
		}
		if (!GetClientRect(hwnd, out var rc)) return false;
		x = (rc.right - rc.left) / 2;
		y = (rc.bottom - rc.top) / 2;
		return true;
	}

	static bool isself(IntPtr hwnd) {
		GetWindowThreadProcessId(hwnd, out var pid);
		return pid == (uint)Process.GetCurrentProcess().Id;
	}

	static bool tap(ActionSpec act) {
		var list = new List<INPUT>(8);
		if (act.Ctrl) addkey(list, VK_CONTROL, false);
		if (act.Alt) addkey(list, VK_MENU, false);
		if (act.Shift) addkey(list, VK_SHIFT, false);
		if (act.Win) addkey(list, VK_LWIN, false);
		addkey(list, act.Vk, false);
		addkey(list, act.Vk, true);
		if (act.Win) addkey(list, VK_LWIN, true);
		if (act.Shift) addkey(list, VK_SHIFT, true);
		if (act.Alt) addkey(list, VK_MENU, true);
		if (act.Ctrl) addkey(list, VK_CONTROL, true);
		return flush(list);
	}

	static bool click(IntPtr fg, int button) {
		var moved = false;
		POINT old = default;
		if (!cursorhits(fg)) {
			if (!clientcenter(fg, out var cx, out var cy)) return false;
			if (!GetCursorPos(out old)) return false;
			if (!SetCursorPos(cx, cy)) return false;
			moved = true;
		}
		uint down, up;
		if (button == 1) { down = MOUSEEVENTF_RIGHTDOWN; up = MOUSEEVENTF_RIGHTUP; }
		else if (button == 2) { down = MOUSEEVENTF_MIDDLEDOWN; up = MOUSEEVENTF_MIDDLEUP; }
		else { down = MOUSEEVENTF_LEFTDOWN; up = MOUSEEVENTF_LEFTUP; }
		var arr = new INPUT[2];
		arr[0] = mouseinput(down);
		arr[1] = mouseinput(up);
		var n = SendInput(2, arr, InputSize);
		if (moved) SetCursorPos(old.X, old.Y);
		return n == 2;
	}

	static bool cursorhits(IntPtr fg) {
		if (!GetCursorPos(out var pt)) return false;
		var hit = WindowFromPoint(pt);
		if (hit == IntPtr.Zero) return false;
		if (hit == fg) return true;
		return GetAncestor(hit, GA_ROOT) == fg;
	}

	static bool clientcenter(IntPtr hwnd, out int x, out int y) {
		x = 0;
		y = 0;
		if (!GetClientRect(hwnd, out var rc)) return false;
		var pt = new POINT {
			X = (rc.right - rc.left) / 2,
			Y = (rc.bottom - rc.top) / 2,
		};
		if (!ClientToScreen(hwnd, ref pt)) return false;
		x = pt.X;
		y = pt.Y;
		return true;
	}

	static INPUT mouseinput(uint flags) => new() {
		type = INPUT_MOUSE,
		u = new InputUnion {
			mi = new MOUSEINPUT { dwFlags = flags },
		},
	};

	static void addkey(List<INPUT> list, ushort vk, bool up) {
		list.Add(new INPUT {
			type = INPUT_KEYBOARD,
			u = new InputUnion {
				ki = new KEYBDINPUT {
					wVk = vk,
					dwFlags = up ? KEYEVENTF_KEYUP : 0u,
				},
			},
		});
	}

	static bool flush(List<INPUT> inputs) {
		if (inputs == null || inputs.Count == 0) return false;
		var arr = inputs.ToArray();
		return SendInput((uint)arr.Length, arr, InputSize) == (uint)arr.Length;
	}

	public static int SelfTest(Action<string> log, Action<string> err) {
		var bad = 0;
		void fail(string m) { bad++; err(m); }
		if (!TryMake("key", "Ctrl+Shift+A", out var a) || a.Mouse || a.Vk != (ushort)'A' || !a.Ctrl || !a.Shift || a.Alt)
			fail("FAIL: Ctrl+Shift+A");
		else if (EventCount(a) != 6)
			fail("FAIL: event count " + EventCount(a));
		if (!TryMake("key", "F8", out var f8) || f8.Vk != 0x77)
			fail("FAIL: F8 vk");
		if (!TryMake("left", "", out var left) || !left.Mouse || left.Button != 0)
			fail("FAIL: left");
		if (!TryMake("right", "Space", out var right) || right.Button != 1)
			fail("FAIL: right");
		if (!TryMake("middle", "", out var mid) || mid.Button != 2)
			fail("FAIL: middle");
		if (TryMake("key", "", out _))
			fail("FAIL: empty key accepted");
		if (TryMake("nope", "A", out _))
			fail("FAIL: bad mode accepted");
		if (ClampInterval(1) != INTERVAL_MIN || ClampInterval(999999) != INTERVAL_MAX)
			fail("FAIL: interval clamp");
		if (ParseInterval("abc") != INTERVAL_DEFAULT || ParseInterval("200") != 200)
			fail("FAIL: interval parse");
		if (bad > 0) return bad;
		log("parse ok");
		var prev = GetForegroundWindow();
		var hwnd = createprobe();
		if (hwnd == IntPtr.Zero) {
			fail("FAIL: probe " + Marshal.GetLastWin32Error());
			return bad;
		}
		try {
			if (!focus(hwnd)) {
				log("skip live send");
				return bad;
			}
			probeKeys = 0;
			probeClicks = 0;
			if (!TryMake("key", "F13", out var f13) || send(f13, false) != 1)
				fail("FAIL: send F13");
			pump(20);
			if (probeKeys < 1) fail("FAIL: probe got no key");
			else log("key " + probeKeys);
			if (!clientcenter(hwnd, out var cx, out var cy))
				fail("FAIL: center");
			else {
				GetCursorPos(out var cur);
				SetCursorPos(cx, cy);
				if (!TryMake("left", "", out var clickAct) || send(clickAct, false) != 1)
					fail("FAIL: send click");
				pump(20);
				SetCursorPos(cur.X, cur.Y);
				if (probeClicks < 1) fail("FAIL: probe got no click");
				else log("click " + probeClicks);
			}
		}
		finally {
			try { DestroyWindow(hwnd); } catch { }
			if (prev != IntPtr.Zero) focus(prev);
		}
		return bad;
	}

	static IntPtr createprobe() {
		probeProc ??= probeproc;
		var cls = new WNDCLASS {
			lpfnWndProc = probeProc,
			hInstance = GetModuleHandle(null),
			lpszClassName = "ScreenKitAutoKeyProbe",
		};
		RegisterClass(ref cls);
		var hwnd = CreateWindowEx(0, cls.lpszClassName, "autokey-probe",
			0x00CF0000 | 0x10000000, 80, 80, 240, 180,
			IntPtr.Zero, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
		if (hwnd != IntPtr.Zero) ShowWindow(hwnd, SW_SHOW);
		return hwnd;
	}

	static IntPtr probeproc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam) {
		if (msg == WM_KEYDOWN) probeKeys++;
		else if (msg == WM_LBUTTONDOWN) probeClicks++;
		return DefWindowProc(hwnd, msg, wParam, lParam);
	}

	static bool focus(IntPtr hwnd) {
		var fg = GetForegroundWindow();
		var fgThread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out _);
		var me = GetCurrentThreadId();
		var attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
		SetForegroundWindow(hwnd);
		SetFocus(hwnd);
		if (attached) AttachThreadInput(me, fgThread, false);
		pump(8);
		return GetForegroundWindow() == hwnd;
	}

	static void pump(int times) {
		for (var i = 0; i < times; i++) {
			while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, PM_REMOVE)) {
				TranslateMessage(ref msg);
				DispatchMessage(ref msg);
			}
			Thread.Sleep(15);
		}
	}
}
