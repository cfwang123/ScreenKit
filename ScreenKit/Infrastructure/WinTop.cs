using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenKit;

/// <summary>一个窗口：HWND、标题、进程、矩形和样式位。</summary>
sealed class WinTopRow {
	public IntPtr Hwnd { get; set; }
	public string Title { get; set; }
	public string Proc { get; set; }
	public string Cls { get; set; }
	public string Path { get; set; }
	public int Pid { get; set; }
	public int Tid { get; set; }
	public int X { get; set; }
	public int Y { get; set; }
	public int W { get; set; }
	public int H { get; set; }
	public int ClientW { get; set; }
	public int ClientH { get; set; }
	public uint Style { get; set; }
	public uint ExStyle { get; set; }
	public bool Visible { get; set; }
	public bool Enabled { get; set; }
	public bool Min { get; set; }
	public bool Max { get; set; }
	public bool Top { get; set; }
	public IntPtr Parent { get; set; }
	public IntPtr Owner { get; set; }

	public string HwndHex => "0x" + Hwnd.ToInt64().ToString("X8");
	public string HwndDec => Hwnd.ToInt64().ToString();
	public string RectText => $"{X},{Y} {W}x{H}";
	public string ClientText => Loc.T("wintop.client", ClientW, ClientH);
	public string StateText => $"Visible={Visible}  Enabled={Enabled}  Min={Min}  Max={Max}  Topmost={Top}";
	public string StyleHex => "0x" + Style.ToString("X8");
	public string ExStyleHex => "0x" + ExStyle.ToString("X8");
	public string ParentHex => hexof(Parent);
	public string OwnerHex => hexof(Owner);
	public string TopText => Top ? Loc.T("wintop.yes") : "";
	public string DisplayTitle => string.IsNullOrEmpty(Title) ? Loc.T("wintop.notitle") : Title;
	public string ListText => $"{(Top ? "★" : " ")} {HwndHex}  [{Proc}]  {Cls}  {DisplayTitle}";

	static string hexof(IntPtr h) => h == IntPtr.Zero ? "0" : "0x" + h.ToInt64().ToString("X8");
}

/// <summary>窗口树的一个节点，Children 是直接子窗口。</summary>
sealed class WinTopNode {
	public WinTopRow Info { get; set; }
	public List<WinTopNode> Children { get; set; } = new();
	public string Header => Info == null ? "" : $"{Info.HwndHex}  {Info.Cls}  {Info.DisplayTitle}";
}

/// <summary>枚举顶层窗口，并做置顶、显隐、移动、改样式位、发消息和鼠标操作。</summary>
static class WinTop {
	public const int SW_HIDE = 0;
	public const int SW_SHOWNORMAL = 1;
	public const int SW_SHOWMINIMIZED = 2;
	public const int SW_SHOWMAXIMIZED = 3;
	public const int SW_SHOW = 5;
	public const int SW_MINIMIZE = 6;
	public const int SW_RESTORE = 9;

	public const int GWL_STYLE = -16;
	public const int GWL_EXSTYLE = -20;

	public const int GW_OWNER = 4;

	public const uint WS_VISIBLE = 0x10000000;
	public const uint WS_DISABLED = 0x08000000;
	public const uint WS_CAPTION = 0x00C00000;
	public const uint WS_BORDER = 0x00800000;
	public const uint WS_THICKFRAME = 0x00040000;
	public const uint WS_SYSMENU = 0x00080000;
	public const uint WS_MINIMIZEBOX = 0x00020000;
	public const uint WS_MAXIMIZEBOX = 0x00010000;

	public const uint WS_EX_TOPMOST = 0x00000008;
	public const uint WS_EX_TOOLWINDOW = 0x00000080;
	public const uint WS_EX_APPWINDOW = 0x00040000;
	public const uint WS_EX_LAYERED = 0x00080000;
	public const uint WS_EX_TRANSPARENT = 0x00000020;
	public const uint WS_EX_CLIENTEDGE = 0x00000200;

	public const uint WM_CLOSE = 0x0010;
	public const uint WM_SETTEXT = 0x000C;
	public const uint WM_LBUTTONDOWN = 0x0201;
	public const uint WM_LBUTTONUP = 0x0202;
	public const uint WM_LBUTTONDBLCLK = 0x0203;
	public const uint WM_RBUTTONDOWN = 0x0204;
	public const uint WM_RBUTTONUP = 0x0205;
	public const uint WM_MBUTTONDOWN = 0x0207;
	public const uint WM_MBUTTONUP = 0x0208;
	public const uint BM_CLICK = 0x00F5;

	const uint MK_LBUTTON = 0x0001;
	const uint MK_RBUTTON = 0x0002;
	const uint MK_MBUTTON = 0x0010;

	const uint INPUT_MOUSE = 0;
	const uint MOUSEEVENTF_MOVE = 0x0001;
	const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
	const uint MOUSEEVENTF_LEFTUP = 0x0004;
	const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
	const uint MOUSEEVENTF_RIGHTUP = 0x0010;
	const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
	const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
	const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
	const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

	const int SM_CXSCREEN = 0;
	const int SM_CYSCREEN = 1;
	const int SM_XVIRTUALSCREEN = 76;
	const int SM_YVIRTUALSCREEN = 77;
	const int SM_CXVIRTUALSCREEN = 78;
	const int SM_CYVIRTUALSCREEN = 79;

	const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
	const int GA_ROOT = 2;
	const int DWMWA_CLOAKED = 14;
	const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

	const int WS_EX_NOACTIVATE = 0x08000000;
	const int WS_POPUP = unchecked((int)0x80000000);

	const uint SWP_NOSIZE = 0x0001;
	const uint SWP_NOMOVE = 0x0002;
	const uint SWP_NOZORDER = 0x0004;
	const uint SWP_NOACTIVATE = 0x0010;
	const uint SWP_FRAMECHANGED = 0x0020;
	const uint SWP_SHOWWINDOW = 0x0040;

	static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
	static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

	/// <summary>可见的应用窗口（标题非空、非工具窗、非被隐藏的 UWP 幽灵窗）。</summary>
	public static List<WinTopRow> List() => collect(false);

	/// <param name="includeHidden">true 则连隐藏窗口、工具窗一起列。</param>
	public static List<WinTopRow> List(bool includeHidden) => collect(includeHidden);

	public static WinTopRow Describe(IntPtr hwnd) => describe(hwnd, null);

	public static WinTopNode BuildTree(IntPtr root) {
		var node = new WinTopNode { Info = Describe(root) };
		if (!IsAlive(root)) return node;
		var kids = new List<IntPtr>();
		EnumChildWindows(root, (h, _) => {
			// EnumChildWindows 枚举全部后代，这里只挂直接子窗口
			if (GetParent(h) == root) kids.Add(h);
			return true;
		}, IntPtr.Zero);
		foreach (var k in kids) node.Children.Add(BuildTree(k));
		return node;
	}

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

	/// <summary>所属顶层窗口；找不到时返回自身。</summary>
	public static IntPtr RootOf(IntPtr hwnd) {
		if (!IsAlive(hwnd)) return IntPtr.Zero;
		var root = GetAncestor(hwnd, GA_ROOT);
		return root == IntPtr.Zero ? hwnd : root;
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

	public static bool Activate(IntPtr hwnd) {
		if (!IsAlive(hwnd)) return false;
		if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
		BringWindowToTop(hwnd);
		return SetForegroundWindow(hwnd);
	}

	// ShowWindow / EnableWindow 返回的是原状态，不是成功与否，这里只看窗口还在不在
	public static bool SetVisible(IntPtr hwnd, bool on) {
		if (!IsAlive(hwnd)) return false;
		ShowWindow(hwnd, on ? SW_SHOW : SW_HIDE);
		return true;
	}

	public static bool SetEnabled(IntPtr hwnd, bool on) {
		if (!IsAlive(hwnd)) return false;
		EnableWindow(hwnd, on);
		return true;
	}

	public static bool Minimize(IntPtr hwnd) {
		if (!IsAlive(hwnd)) return false;
		ShowWindow(hwnd, SW_MINIMIZE);
		return true;
	}

	public static bool Maximize(IntPtr hwnd) {
		if (!IsAlive(hwnd)) return false;
		ShowWindow(hwnd, SW_SHOWMAXIMIZED);
		return true;
	}

	public static bool Restore(IntPtr hwnd) {
		if (!IsAlive(hwnd)) return false;
		ShowWindow(hwnd, SW_RESTORE);
		return true;
	}

	public static bool CloseWin(IntPtr hwnd) => IsAlive(hwnd) && PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

	public static bool BmClick(IntPtr hwnd) {
		if (!IsAlive(hwnd)) return false;
		SendMessage(hwnd, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
		return true;
	}

	/// <summary>整窗移回虚拟桌面内，至少露出标题栏。</summary>
	public static bool MoveToScreen(IntPtr hwnd) {
		if (!IsAlive(hwnd) || !GetWindowRect(hwnd, out var r)) return false;
		var vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
		var vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
		var vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
		var vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
		if (vw <= 0 || vh <= 0) {
			vx = 0;
			vy = 0;
			vw = GetSystemMetrics(SM_CXSCREEN);
			vh = GetSystemMetrics(SM_CYSCREEN);
		}
		var w = Math.Max(r.Right - r.Left, 50);
		var h = Math.Max(r.Bottom - r.Top, 30);
		var x = r.Left;
		var y = r.Top;
		const int margin = 40;
		if (x + w < vx + margin) x = vx;
		if (y + h < vy + margin) y = vy;
		if (x > vx + vw - margin) x = vx + vw - w;
		if (y > vy + vh - margin) y = vy + margin;
		if (x < vx) x = vx;
		if (y < vy) y = vy;
		if (IsIconic(hwnd) || IsZoomed(hwnd)) ShowWindow(hwnd, SW_RESTORE);
		return SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h,
			SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
	}

	public static bool MoveResize(IntPtr hwnd, int x, int y, int w, int h) {
		if (!IsAlive(hwnd)) return false;
		if (IsIconic(hwnd) || IsZoomed(hwnd)) ShowWindow(hwnd, SW_RESTORE);
		return SetWindowPos(hwnd, IntPtr.Zero, x, y, Math.Max(1, w), Math.Max(1, h),
			SWP_NOZORDER | SWP_NOACTIVATE);
	}

	public static bool SetTitle(IntPtr hwnd, string title) =>
		IsAlive(hwnd) && SetWindowText(hwnd, title ?? "");

	public static bool SetStyleBits(IntPtr hwnd, bool isEx, uint mask, bool on) {
		if (!IsAlive(hwnd)) return false;
		var idx = isEx ? GWL_EXSTYLE : GWL_STYLE;
		var cur = unchecked((uint)GetWindowLong(hwnd, idx));
		var next = on ? (cur | mask) : (cur & ~mask);
		SetWindowLong(hwnd, idx, unchecked((int)next));
		SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
			SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
		return true;
	}

	public static bool Post(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp) =>
		IsAlive(hwnd) && PostMessage(hwnd, msg, wp, lp);

	public static IntPtr Send(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp) =>
		IsAlive(hwnd) ? SendMessage(hwnd, msg, wp, lp) : IntPtr.Zero;

	/// <summary>向窗口客户区发鼠标消息，不移动真实光标。</summary>
	public static bool PostClick(IntPtr hwnd, int cx, int cy, string button, string mode) {
		if (!IsAlive(hwnd)) return false;
		var (downMsg, upMsg, mk) = buttonmsgs(button);
		var lp = new IntPtr(MakeLParam(cx, cy));
		if (mode is "down" or "click" or "dblclick")
			PostMessage(hwnd, downMsg, (IntPtr)mk, lp);
		if (mode is "up" or "click" or "dblclick")
			PostMessage(hwnd, upMsg, IntPtr.Zero, lp);
		if (mode == "dblclick") {
			if (button == "left") PostMessage(hwnd, WM_LBUTTONDBLCLK, (IntPtr)mk, lp);
			else PostMessage(hwnd, downMsg, (IntPtr)mk, lp);
			PostMessage(hwnd, upMsg, IntPtr.Zero, lp);
		}
		return true;
	}

	/// <summary>屏幕坐标点击：SendInput 绝对移动 + 按键。</summary>
	public static void SendInputClick(int x, int y, string button, string mode) {
		var (down, up) = buttonflags(button);
		moveabs(x, y);
		if (mode is "down" or "click" or "dblclick") mouseflags(down);
		if (mode is "up" or "click" or "dblclick") mouseflags(up);
		if (mode == "dblclick") {
			mouseflags(down);
			mouseflags(up);
		}
	}

	public static bool ClientCenter(IntPtr hwnd, out int x, out int y) {
		x = y = 0;
		if (!GetClientRect(hwnd, out var rc)) return false;
		x = Math.Max(0, (rc.Right - rc.Left) / 2);
		y = Math.Max(0, (rc.Bottom - rc.Top) / 2);
		return true;
	}

	public static bool ClientToScreen(IntPtr hwnd, int cx, int cy, out int sx, out int sy) {
		sx = sy = 0;
		var pt = new NativePoint { X = cx, Y = cy };
		if (!ClientToScreenRaw(hwnd, ref pt)) return false;
		sx = pt.X;
		sy = pt.Y;
		return true;
	}

	public static int MakeLParam(int lo, int hi) => (hi << 16) | (lo & 0xFFFF);

	public static IntPtr CreateProbe() =>
		CreateWindowEx((int)WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, "Static", "sk-wintop-probe",
			WS_POPUP, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

	public static void DestroyProbe(IntPtr hwnd) {
		if (hwnd == IntPtr.Zero) return;
		try { DestroyWindow(hwnd); } catch { }
	}

	static List<WinTopRow> collect(bool includeHidden) {
		var list = new List<WinTopRow>();
		var procs = new Dictionary<int, string>();
		EnumWindows((h, _) => {
			if (!IsWindow(h)) return true;
			if (!includeHidden && !isapp(h)) return true;
			if (cloaked(h)) return true;
			list.Add(describe(h, procs));
			return true;
		}, IntPtr.Zero);
		list.Sort(cmp);
		return list;
	}

	static int cmp(WinTopRow a, WinTopRow b) {
		var c = b.Top.CompareTo(a.Top);
		if (c != 0) return c;
		return string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
	}

	static WinTopRow describe(IntPtr hwnd, Dictionary<int, string> procs) {
		var row = new WinTopRow { Hwnd = hwnd };
		if (!IsAlive(hwnd)) return row;
		row.Title = titleof(hwnd);
		row.Cls = classof(hwnd);
		row.Tid = (int)GetWindowThreadProcessId(hwnd, out var pid);
		row.Pid = (int)pid;
		row.Path = pathof(pid);
		row.Proc = string.IsNullOrEmpty(row.Path) ? procname((int)pid, procs) : System.IO.Path.GetFileName(row.Path);
		if (GetWindowRect(hwnd, out var wr)) {
			row.X = wr.Left;
			row.Y = wr.Top;
			row.W = wr.Right - wr.Left;
			row.H = wr.Bottom - wr.Top;
		}
		if (GetClientRect(hwnd, out var cr)) {
			row.ClientW = cr.Right - cr.Left;
			row.ClientH = cr.Bottom - cr.Top;
		}
		row.Style = unchecked((uint)GetWindowLong(hwnd, GWL_STYLE));
		row.ExStyle = unchecked((uint)GetWindowLong(hwnd, GWL_EXSTYLE));
		row.Visible = IsWindowVisible(hwnd);
		row.Enabled = IsWindowEnabled(hwnd);
		row.Min = IsIconic(hwnd);
		row.Max = IsZoomed(hwnd);
		row.Top = (row.ExStyle & WS_EX_TOPMOST) != 0;
		row.Parent = GetParent(hwnd);
		row.Owner = GetWindow(hwnd, GW_OWNER);
		return row;
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

	static string pathof(uint pid) {
		if (pid == 0) return "";
		var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
		if (h == IntPtr.Zero) return "";
		try {
			var sb = new StringBuilder(1024);
			var size = sb.Capacity;
			return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : "";
		}
		finally { CloseHandle(h); }
	}

	static (uint down, uint up) buttonflags(string button) => button switch {
		"right" => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
		"middle" => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
		_ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
	};

	static (uint down, uint up, uint mk) buttonmsgs(string button) => button switch {
		"right" => (WM_RBUTTONDOWN, WM_RBUTTONUP, MK_RBUTTON),
		"middle" => (WM_MBUTTONDOWN, WM_MBUTTONUP, MK_MBUTTON),
		_ => (WM_LBUTTONDOWN, WM_LBUTTONUP, MK_LBUTTON),
	};

	static void moveabs(int x, int y) {
		var vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
		var vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
		var vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
		var vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
		if (vw <= 0) vw = GetSystemMetrics(SM_CXSCREEN);
		if (vh <= 0) vh = GetSystemMetrics(SM_CYSCREEN);
		var nx = (int)Math.Round((x - vx) * 65535.0 / Math.Max(1, vw - 1));
		var ny = (int)Math.Round((y - vy) * 65535.0 / Math.Max(1, vh - 1));
		mouseinput(new MOUSEINPUT {
			dx = nx,
			dy = ny,
			dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
			dwExtraInfo = GetMessageExtraInfo(),
		});
	}

	static void mouseflags(uint flags) =>
		mouseinput(new MOUSEINPUT { dwFlags = flags, dwExtraInfo = GetMessageExtraInfo() });

	static void mouseinput(MOUSEINPUT mi) {
		var input = new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = mi } };
		SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
	}

	delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lparam);
	delegate bool EnumChildProc(IntPtr hwnd, IntPtr lparam);

	[StructLayout(LayoutKind.Sequential)]
	struct NativePoint {
		public int X;
		public int Y;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct RECT {
		public int Left, Top, Right, Bottom;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct INPUT {
		public uint type;
		public InputUnion U;
	}

	[StructLayout(LayoutKind.Explicit)]
	struct InputUnion {
		[FieldOffset(0)] public MOUSEINPUT mi;
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

	[DllImport("user32.dll")]
	static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lparam);

	[DllImport("user32.dll")]
	static extern bool EnumChildWindows(IntPtr parent, EnumChildProc cb, IntPtr lparam);

	[DllImport("user32.dll")]
	static extern bool IsWindow(IntPtr hwnd);

	[DllImport("user32.dll")]
	static extern bool IsWindowVisible(IntPtr hwnd);

	[DllImport("user32.dll")]
	static extern bool IsWindowEnabled(IntPtr hwnd);

	[DllImport("user32.dll")]
	static extern bool IsIconic(IntPtr hwnd);

	[DllImport("user32.dll")]
	static extern bool IsZoomed(IntPtr hwnd);

	[DllImport("user32.dll")]
	static extern bool GetWindowRect(IntPtr hwnd, out RECT rc);

	[DllImport("user32.dll")]
	static extern bool GetClientRect(IntPtr hwnd, out RECT rc);

	[DllImport("user32.dll")]
	static extern bool ClientToScreenRaw(IntPtr hwnd, ref NativePoint pt);

	[DllImport("user32.dll")]
	static extern int GetWindowLong(IntPtr hwnd, int index);

	[DllImport("user32.dll")]
	static extern int SetWindowLong(IntPtr hwnd, int index, int value);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	static extern bool SetWindowText(IntPtr hwnd, string text);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	static extern int GetClassName(IntPtr hwnd, StringBuilder cls, int max);

	[DllImport("user32.dll")]
	static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

	[DllImport("user32.dll")]
	static extern IntPtr GetParent(IntPtr hwnd);

	[DllImport("user32.dll")]
	static extern IntPtr GetWindow(IntPtr hwnd, int cmd);

	[DllImport("user32.dll")]
	static extern IntPtr GetAncestor(IntPtr hwnd, int flags);

	[DllImport("user32.dll")]
	static extern IntPtr WindowFromPoint(NativePoint point);

	[DllImport("user32.dll")]
	static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

	[DllImport("user32.dll")]
	static extern bool ShowWindow(IntPtr hwnd, int cmd);

	[DllImport("user32.dll")]
	static extern bool EnableWindow(IntPtr hwnd, bool enable);

	[DllImport("user32.dll")]
	static extern bool BringWindowToTop(IntPtr hwnd);

	[DllImport("user32.dll")]
	static extern bool SetForegroundWindow(IntPtr hwnd);

	[DllImport("user32.dll")]
	static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);

	[DllImport("user32.dll")]
	static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);

	[DllImport("user32.dll")]
	static extern int GetSystemMetrics(int index);

	[DllImport("user32.dll")]
	static extern uint SendInput(uint n, INPUT[] inputs, int size);

	[DllImport("user32.dll")]
	static extern IntPtr GetMessageExtraInfo();

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern IntPtr CreateWindowEx(int exStyle, string cls, string name, int style,
		int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

	[DllImport("user32.dll", SetLastError = true)]
	static extern bool DestroyWindow(IntPtr hwnd);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	static extern IntPtr GetModuleHandle(string name);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern IntPtr OpenProcess(int access, bool inherit, uint pid);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern bool QueryFullProcessImageName(IntPtr proc, int flags, StringBuilder path, ref int size);

	[DllImport("kernel32.dll")]
	static extern bool CloseHandle(IntPtr handle);

	[DllImport("dwmapi.dll")]
	static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

	[DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
	static extern int DwmGetWindowAttributeRect(IntPtr hwnd, int attr, out RECT rc, int size);
}
