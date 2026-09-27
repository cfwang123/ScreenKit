using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenKit;

/// <summary>一个顶层窗口：HWND、标题、进程、是否已固定在前面。</summary>
sealed class WinTopRow {
	public IntPtr Hwnd { get; set; }
	public string Title { get; set; }
	public string Proc { get; set; }
	public string Cls { get; set; }
	public bool Top { get; set; }
	public string HwndHex => "0x" + Hwnd.ToInt64().ToString("X");
	public string TopText => Top ? Loc.T("wintop.yes") : "";
}

/// <summary>枚举可见顶层窗口，并用 SetWindowPos 设置或取消 HWND_TOPMOST。</summary>
static class WinTop {
	const int GWL_EXSTYLE = -20;
	const int WS_EX_TOPMOST = 0x00000008;
	const int WS_EX_TOOLWINDOW = 0x00000080;
	const int WS_EX_APPWINDOW = 0x00040000;
	const int WS_EX_NOACTIVATE = 0x08000000;
	const int WS_POPUP = unchecked((int)0x80000000);
	const uint GW_OWNER = 4;
	const uint GA_ROOT = 2;
	const int DWMWA_CLOAKED = 14;
	const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
	const uint SWP_NOSIZE = 0x0001;
	const uint SWP_NOMOVE = 0x0002;
	const uint SWP_NOACTIVATE = 0x0010;
	static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
	static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

	public static List<WinTopRow> List() {
		var list = new List<WinTopRow>();
		var procs = new Dictionary<int, string>();
		EnumWindowsProc cb = (h, _) => {
			if (!isapp(h)) return true;
			list.Add(describe(h, procs));
			return true;
		};
		EnumWindows(cb, IntPtr.Zero);
		list.Sort(cmp);
		return list;
	}

	public static WinTopRow Describe(IntPtr hwnd) => describe(hwnd, null);

	public static bool IsAlive(IntPtr hwnd) => hwnd != IntPtr.Zero && IsWindow(hwnd);

	public static bool IsTop(IntPtr hwnd) {
		if (!IsAlive(hwnd)) return false;
		return (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
	}

	/// <summary>固定或取消固定在前面。不移动、不改大小、不激活。</summary>
	public static bool SetTop(IntPtr hwnd, bool on, out int err) {
		err = 0;
		if (!IsAlive(hwnd)) {
			err = 1400;
			return false;
		}
		var after = on ? HWND_TOPMOST : HWND_NOTOPMOST;
		var ok = SetWindowPos(hwnd, after, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
		if (!ok) err = Marshal.GetLastWin32Error();
		if (IsTop(hwnd) != on) {
			if (err == 0) err = Marshal.GetLastWin32Error();
			return false;
		}
		return true;
	}

	public static bool TryParse(string text, out IntPtr hwnd) {
		hwnd = IntPtr.Zero;
		var s = (text ?? "").Trim();
		if (s.Length == 0) return false;
		var hex = false;
		if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
			s = s.Substring(2);
			hex = true;
		}
		long v;
		if (hex) {
			if (!long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return false;
		}
		else if (!long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) {
			if (!long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return false;
		}
		if (v == 0) return false;
		hwnd = new IntPtr(v);
		return true;
	}

	public static IntPtr RootAt(int x, int y) {
		var h = WindowFromPoint(new NativePoint { X = x, Y = y });
		if (h == IntPtr.Zero) return IntPtr.Zero;
		var root = GetAncestor(h, GA_ROOT);
		return root == IntPtr.Zero ? h : root;
	}

	/// <summary>可见外框（不含阴影）。最小化或过小则失败。</summary>
	public static bool TryBounds(IntPtr hwnd, out int x, out int y, out int w, out int h) {
		x = y = w = h = 0;
		if (!IsAlive(hwnd) || IsIconic(hwnd)) return false;
		RECT rc;
		var ok = false;
		try { ok = DwmGetWindowAttributeRect(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rc, 16) == 0; }
		catch { ok = false; rc = default; }
		if (!ok && !GetWindowRect(hwnd, out rc)) return false;
		w = rc.Right - rc.Left;
		h = rc.Bottom - rc.Top;
		if (w < 8 || h < 8) return false;
		x = rc.Left;
		y = rc.Top;
		return true;
	}

	public static IntPtr CreateProbe() {
		return CreateWindowEx(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, "Static", "sk-wintop-probe",
			WS_POPUP, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
	}

	public static void DestroyProbe(IntPtr hwnd) {
		if (hwnd == IntPtr.Zero) return;
		try { DestroyWindow(hwnd); } catch { }
	}

	static int cmp(WinTopRow a, WinTopRow b) {
		var c = b.Top.CompareTo(a.Top);
		if (c != 0) return c;
		return string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
	}

	static WinTopRow describe(IntPtr hwnd, Dictionary<int, string> procs) {
		GetWindowThreadProcessId(hwnd, out var pid);
		return new WinTopRow {
			Hwnd = hwnd,
			Title = titleof(hwnd),
			Proc = procname((int)pid, procs),
			Cls = classof(hwnd),
			Top = IsTop(hwnd),
		};
	}

	static bool isapp(IntPtr hwnd) {
		if (!IsWindowVisible(hwnd) || cloaked(hwnd)) return false;
		var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
		if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return false;
		var owner = GetWindow(hwnd, GW_OWNER);
		if (owner != IntPtr.Zero && (ex & WS_EX_APPWINDOW) == 0) return false;
		return titleof(hwnd).Length > 0;
	}

	static bool cloaked(IntPtr hwnd) {
		try {
			if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var v, 4) != 0) return false;
			return v != 0;
		}
		catch { return false; }
	}

	static string titleof(IntPtr hwnd) {
		var buf = new StringBuilder(512);
		GetWindowText(hwnd, buf, buf.Capacity);
		return (buf.ToString() ?? "").Trim();
	}

	static string classof(IntPtr hwnd) {
		var buf = new StringBuilder(256);
		GetClassName(hwnd, buf, buf.Capacity);
		return buf.ToString() ?? "";
	}

	static string procname(int pid, Dictionary<int, string> procs) {
		if (pid <= 0) return "";
		if (procs != null && procs.TryGetValue(pid, out var cached)) return cached;
		var name = "";
		try { name = Process.GetProcessById(pid).ProcessName ?? ""; }
		catch { name = pid.ToString(); }
		if (procs != null) procs[pid] = name;
		return name;
	}

	delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lparam);

	[StructLayout(LayoutKind.Sequential)]
	struct NativePoint {
		public int X;
		public int Y;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct RECT {
		public int Left, Top, Right, Bottom;
	}

	[DllImport("user32.dll")]
	static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

	[DllImport("user32.dll")]
	static extern bool IsWindow(IntPtr hwnd);

	[DllImport("user32.dll")]
	static extern bool IsWindowVisible(IntPtr hwnd);

	[DllImport("user32.dll")]
	static extern bool IsIconic(IntPtr hwnd);

	[DllImport("user32.dll")]
	static extern bool GetWindowRect(IntPtr hwnd, out RECT rc);

	[DllImport("user32.dll")]
	static extern int GetWindowLong(IntPtr hwnd, int index);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	static extern int GetWindowText(IntPtr hwnd, StringBuilder lpString, int nMaxCount);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	static extern int GetClassName(IntPtr hwnd, StringBuilder lpClassName, int nMaxCount);

	[DllImport("user32.dll")]
	static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

	[DllImport("user32.dll")]
	static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

	[DllImport("user32.dll")]
	static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

	[DllImport("user32.dll")]
	static extern IntPtr WindowFromPoint(NativePoint point);

	[DllImport("user32.dll", SetLastError = true)]
	static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
		int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

	[DllImport("user32.dll", SetLastError = true)]
	static extern bool DestroyWindow(IntPtr hwnd);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	static extern IntPtr GetModuleHandle(string name);

	[DllImport("dwmapi.dll")]
	static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

	[DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
	static extern int DwmGetWindowAttributeRect(IntPtr hwnd, int attr, out RECT rc, int size);
}
