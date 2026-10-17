using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>gVim / WezTerm 无系统光标时推算屏幕锚点，否则回退 GUI 插入符。</summary>
sealed class ImeCaret : IDisposable {
	const string VIM_CLASS = "ScreenKit.ImeVimMsg";

	/// <summary>主窗创建 <see cref="HangulIme"/> 后可用；划词浮窗在 gVim 下也用来对齐光标。</summary>
	public static ImeCaret Shared { get; private set; }

	readonly object gate = new();
	readonly object vimaskgate = new();
	IntPtr vimhwnd;
	VimWndProc vimwnd;
	EnumChildProc vimenumcb;
	string vimreply;
	int vimkind;
	IntPtr vimfound;
	uint vimpid;
	int vimx, vimy, vimstamp;
	bool vimok;
	string wezbin;
	int wezx, wezy, wezstamp;
	bool wezok;
	bool dead;
	Dispatcher disp;

	internal static void BindShared(ImeCaret c) {
		if (c != null) Shared = c;
	}

	public void AttachDispatcher(Dispatcher d) {
		if (d != null) disp = d;
	}

	public void Start() {
		if (vimhwnd != IntPtr.Zero) return;
		runui(() => {
			if (vimhwnd != IntPtr.Zero) return;
			vimwnd = vimproc;
			vimenumcb = vimenum;
			var wc = new WNDCLASS {
				lpfnWndProc = Marshal.GetFunctionPointerForDelegate(vimwnd),
				hInstance = GetModuleHandle(null),
				lpszClassName = VIM_CLASS
			};
			RegisterClass(ref wc);
			vimhwnd = CreateWindowEx(0, wc.lpszClassName, "", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, wc.hInstance, IntPtr.Zero);
		});
		wezbin = wezwhich();
		var th = new Thread(pollloop) { IsBackground = true };
		th.Start();
	}

	public void Dispose() {
		dead = true;
		if (vimhwnd != IntPtr.Zero) {
			try { DestroyWindow(vimhwnd); } catch { }
			vimhwnd = IntPtr.Zero;
		}
	}

	public bool TryAnchor(out int px, out int py) {
		px = 0;
		py = 0;
		if (vimroot() != IntPtr.Zero) return vimcached(out px, out py);
		if (winclass(GetForegroundWindow()) == "org.wezfurlong.wezterm") return wezfresh(out px, out py);
		syscaret(out px, out py);
		if (px == 0 && py == 0) {
			GetCursorPos(out var pt);
			px = pt.X;
			py = pt.Y;
		}
		return px != 0 || py != 0;
	}

	/// <summary>gVim 前台时同步问光标格子（划词浮窗用）。</summary>
	public bool TryVimCaret(out int px, out int py) {
		px = 0;
		py = 0;
		var root = vimroot();
		if (root == IntPtr.Zero) return false;
		int x = 0, y = 0;
		var ok = false;
		runui(() => { ok = vimpixel(root, out x, out y); });
		if (!ok) return false;
		lock (gate) {
			vimx = x;
			vimy = y;
			vimok = true;
			vimstamp = Environment.TickCount;
		}
		px = x;
		py = y;
		return true;
	}

	/// <summary>WezTerm 前台时同步问窗格光标（划词浮窗用）。</summary>
	public bool TryWezCaret(out int px, out int py) {
		px = 0;
		py = 0;
		var fg = GetForegroundWindow();
		if (winclass(fg) != "org.wezfurlong.wezterm") return false;
		wezread(fg);
		lock (gate) {
			if (!wezok) return false;
			px = wezx;
			py = wezy;
			return true;
		}
	}

	/// <summary>CLI：前台 gVim 时输出诊断。</summary>
	internal string ProbeVim(out int px, out int py) {
		px = 0;
		py = 0;
		var root = vimroot();
		if (root == IntPtr.Zero) return "fg-not-vim";
		var area = IntPtr.Zero;
		var srv = IntPtr.Zero;
		string expr = null;
		runui(() => {
			area = FindWindowEx(root, IntPtr.Zero, "VimTextArea", null);
			srv = vimserver(root);
			if (srv != IntPtr.Zero) expr = vimask(srv);
		});
		if (area == IntPtr.Zero) return "no-VimTextArea";
		if (srv == IntPtr.Zero) return "no-VIM_MESSAGES";
		if (expr == null) return "vimask-fail kind=" + vimkind;
		if (!vimpixel(root, out px, out py)) return "pixel-fail expr=" + expr;
		return "ok expr=" + expr + " xy=" + px + "," + py;
	}

	void runui(Action act) {
		if (act == null) return;
		var d = disp ?? Application.Current?.Dispatcher;
		if (d == null || d.CheckAccess()) act();
		else d.Invoke(act);
	}

	static IntPtr vimroot() {
		var fg = GetForegroundWindow();
		if (fg == IntPtr.Zero) return IntPtr.Zero;
		for (var h = fg; h != IntPtr.Zero; h = GetParent(h)) {
			if (winclass(h) == "Vim") return h;
		}
		return IntPtr.Zero;
	}

	void pollloop() {
		while (!dead) {
			try {
				var fg = GetForegroundWindow();
				var cls = winclass(fg);
				if (cls == "org.wezfurlong.wezterm") wezread(fg);
				else {
					var root = vimroot();
					if (root != IntPtr.Zero) vimread(root);
				}
			} catch { }
			for (int i = 0; i < 16 && !dead; i++) Thread.Sleep(20);
		}
	}

	bool vimcached(out int x, out int y) {
		x = 0;
		y = 0;
		lock (gate) {
			int age = Environment.TickCount - vimstamp;
			if (!vimok || age < 0 || age > 2500) return false;
			x = vimx;
			y = vimy;
			return true;
		}
	}

	void vimread(IntPtr fg) {
		if (!vimpixel(fg, out var x, out var y)) return;
		lock (gate) {
			vimx = x;
			vimy = y;
			vimok = true;
			vimstamp = Environment.TickCount;
		}
	}

	bool vimpixel(IntPtr fg, out int x, out int y) {
		x = 0;
		y = 0;
		if (vimhwnd == IntPtr.Zero) return false;
		var area = FindWindowEx(fg, IntPtr.Zero, "VimTextArea", null);
		if (area == IntPtr.Zero) return false;
		var srv = vimserver(fg);
		var text = vimask(srv);
		if (text == null) return false;
		var parts = text.Split(',');
		if (parts.Length != 4) return false;
		if (!int.TryParse(parts[0], out var row) || !int.TryParse(parts[1], out var col)) return false;
		if (!int.TryParse(parts[2], out var lines) || !int.TryParse(parts[3], out var cols)) return false;
		if (row < 1 || col < 1 || lines < 1 || cols < 1) return false;
		if (!GetWindowRect(area, out var wr)) return false;
		int ch = (wr.bottom - wr.top) / lines;
		int cw = (wr.right - wr.left) / cols;
		if (ch < 1 || cw < 1) return false;
		x = wr.left + (col - 1) * cw;
		y = wr.top + row * ch;
		return true;
	}

	IntPtr vimserver(IntPtr fg) {
		GetWindowThreadProcessId(fg, out vimpid);
		var direct = FindWindowEx(fg, IntPtr.Zero, "VIM_MESSAGES", null);
		if (direct != IntPtr.Zero) {
			GetWindowThreadProcessId(direct, out uint pid);
			if (pid == vimpid) return direct;
		}
		vimfound = IntPtr.Zero;
		EnumWindows(vimenumcb, IntPtr.Zero);
		return vimfound;
	}

	bool vimenum(IntPtr hwnd, IntPtr lp) {
		if (vimfound != IntPtr.Zero) return false;
		if (winclass(hwnd) == "VIM_MESSAGES") {
			GetWindowThreadProcessId(hwnd, out uint pid);
			if (pid == vimpid) { vimfound = hwnd; return false; }
		}
		EnumChildWindows(hwnd, vimenumcb, IntPtr.Zero);
		return vimfound == IntPtr.Zero;
	}

	string vimask(IntPtr srv) {
		if (srv == IntPtr.Zero) return null;
		lock (vimaskgate) {
			vimreply = null;
			vimkind = 0;
			if (!vimsend(srv, 20, "utf-8")) return null;
			if (!vimsend(srv, 10, "screenrow() . ',' . screencol() . ',' . &lines . ',' . &columns")) return null;
			if (vimkind != 11 || string.IsNullOrEmpty(vimreply)) return null;
			return vimreply;
		}
	}

	bool vimsend(IntPtr srv, int kind, string text) {
		var bytes = Encoding.UTF8.GetBytes(text + "\0");
		var mem = Marshal.AllocHGlobal(bytes.Length);
		var ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(COPYDATA)));
		try {
			Marshal.Copy(bytes, 0, mem, bytes.Length);
			var data = new COPYDATA { dwData = (IntPtr)kind, cbData = bytes.Length, lpData = mem };
			Marshal.StructureToPtr(data, ptr, false);
			IntPtr result;
			var ok = SendMessageTimeout(srv, 0x004A, vimhwnd, ptr, 2, 200, out result);
			return ok != IntPtr.Zero && result != IntPtr.Zero;
		} finally {
			Marshal.FreeHGlobal(ptr);
			Marshal.FreeHGlobal(mem);
		}
	}

	IntPtr vimproc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam) {
		if (msg == 0x004A) {
			var data = Marshal.PtrToStructure<COPYDATA>(lParam);
			if (data.dwData == (IntPtr)20) return (IntPtr)1;
			if ((data.dwData == (IntPtr)11 || data.dwData == (IntPtr)12) && data.lpData != IntPtr.Zero) {
				vimkind = (int)data.dwData;
				vimreply = copyutf(data.lpData, data.cbData);
			}
			return (IntPtr)1;
		}
		return DefWindowProc(hwnd, msg, wParam, lParam);
	}

	static string wezwhich() {
		var path = Environment.GetEnvironmentVariable("PATH") ?? "";
		foreach (var part in path.Split(';')) {
			var full = System.IO.Path.Combine(part.Trim(), "wezterm.exe");
			if (full.Length > 12 && System.IO.File.Exists(full)) return full;
		}
		var guess = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WezTerm", "wezterm.exe");
		if (System.IO.File.Exists(guess)) return guess;
		return "wezterm";
	}

	bool wezfresh(out int x, out int y) {
		x = 0;
		y = 0;
		if (winclass(GetForegroundWindow()) != "org.wezfurlong.wezterm") return false;
		lock (gate) {
			int age = Environment.TickCount - wezstamp;
			if (!wezok || age < 0 || age > 2500) return false;
			x = wezx;
			y = wezy;
			return true;
		}
	}

	void wezread(IntPtr hwnd) {
		lock (gate) {
			if (wezok) wezstamp = Environment.TickCount;
		}
		var panesJson = wezcli("cli list --format json");
		var clientsJson = wezcli("cli list-clients --format json");
		if (panesJson == null || clientsJson == null) return;
		if (!wezpoint(hwnd, panesJson, clientsJson, out var x, out var y)) return;
		lock (gate) {
			wezx = x;
			wezy = y;
			wezok = true;
			wezstamp = Environment.TickCount;
		}
	}

	bool wezpoint(IntPtr hwnd, string panesJson, string clientsJson, out int x, out int y) {
		x = 0;
		y = 0;
		var panes = wezobjs(panesJson);
		var clients = wezobjs(clientsJson);
		if (panes.Count == 0 || clients.Count == 0) return false;
		int fid = wezfield(clients[0], "focused_pane_id", -1);
		string focus = null;
		foreach (var p in panes)
			if (wezfield(p, "pane_id", -1) == fid) focus = p;
		if (focus == null) return false;
		int cols = wezfield(focus, "cols");
		int rows = wezfield(focus, "rows");
		int pw = wezfield(focus, "pixel_width");
		int ph = wezfield(focus, "pixel_height");
		if (cols < 1 || rows < 1 || pw < 1 || ph < 1) return false;
		int cellw = pw / cols;
		int cellh = ph / rows;
		if (cellw < 1 || cellh < 1) return false;
		int tab = wezfield(focus, "tab_id", -1);
		bool zoom = wezflag(focus, "is_zoomed");
		int left = zoom ? 0 : wezfield(focus, "left_col");
		int top = zoom ? 0 : wezfield(focus, "top_row");
		int gridw = zoom ? pw : 0;
		int gridh = zoom ? ph : 0;
		if (!zoom) {
			foreach (var p in panes) {
				if (wezfield(p, "tab_id", -1) != tab) continue;
				int right = (wezfield(p, "left_col") + wezfield(p, "cols")) * cellw;
				int bottom = (wezfield(p, "top_row") + wezfield(p, "rows")) * cellh;
				if (right > gridw) gridw = right;
				if (bottom > gridh) gridh = bottom;
			}
		}
		if (!GetClientRect(hwnd, out var cr)) return false;
		if (gridw > cr.right) gridw = cr.right;
		if (gridh > cr.bottom) gridh = cr.bottom;
		var origin = new POINT();
		if (!ClientToScreen(hwnd, ref origin)) return false;
		int extraw = cr.right - gridw;
		int extrah = cr.bottom - gridh;
		int padl = cellw;
		int padt = cellh / 2;
		int padb = cellh - padt;
		int ox = extraw + 2 >= padl + cellw ? padl : Math.Max(0, extraw / 2);
		int oy = extrah >= padt + padb + cellh / 2 ? extrah - padb : Math.Max(0, extrah / 2);
		int row = wezfield(focus, "cursor_y");
		int col = wezfield(focus, "cursor_x");
		var vis = wezstr(focus, "cursor_visibility");
		bool arrow = false;
		bool probe = wezhidden(vis) || (rows > 4 && row < rows / 3);
		if (probe) {
			var screen = wezcli("cli get-text --pane-id " + fid + " --escapes");
			bool have;
			lock (gate) have = wezok;
			if (screen != null && wezinput(screen, rows, out var ir, out var ic)) {
				row = ir;
				col = ic;
				arrow = true;
			} else if (have) return false;
		}
		if (row < 0) row = 0;
		if (col < 0) col = 0;
		x = origin.X + ox + (left + col) * cellw;
		if (arrow || wezhidden(vis)) x += 100;
		y = origin.Y + oy + (top + row + 1) * cellh;
		return true;
	}

	static bool wezhidden(string vis) {
		if (string.IsNullOrEmpty(vis)) return false;
		return vis.IndexOf("hidden", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	static bool wezinput(string text, int rows, out int row, out int col) {
		row = 0;
		col = 0;
		var lines = text.Replace("\r\n", "\n").Split('\n');
		if (lines.Length == rows + 1 && lines[lines.Length - 1].Length == 0) {
			var cut = new string[rows];
			Array.Copy(lines, cut, rows);
			lines = cut;
		}
		int hit = -1;
		for (int i = lines.Length - 1; i >= 0; i--) {
			if (lines[i].IndexOf('→') >= 0) {
				hit = i;
				break;
			}
		}
		if (hit < 0) return false;
		int at = wezcol(lines[hit]);
		if (at < 0) return false;
		row = hit;
		col = at;
		return true;
	}

	static int wezcol(string s) {
		int i = 0, col = 0, arrow = -1, userend = -1;
		bool faint = false;
		while (i < s.Length) {
			if (s[i] == '\u001b') {
				i++;
				if (i < s.Length && s[i] == '[') {
					i++;
					int start = i;
					while (i < s.Length && (s[i] < '@' || s[i] > '~')) i++;
					char fin = i < s.Length ? s[i] : ' ';
					var body = s.Substring(start, Math.Max(0, i - start));
					if (i < s.Length) i++;
					if (fin == 'm') wezfaint(body, ref faint);
				} else if (i < s.Length && s[i] == '(') {
					i = Math.Min(s.Length, i + 2);
				} else if (i < s.Length) i++;
				continue;
			}
			char c = s[i++];
			int w = wide(c);
			if (c == '→' && arrow < 0) arrow = col;
			if (arrow >= 0 && c != '→' && c != ' ' && !faint) userend = col + w;
			col += w;
		}
		if (arrow < 0) return -1;
		if (userend >= 0) return userend;
		return arrow + wide('→') + 1;
	}

	static void wezfaint(string body, ref bool faint) {
		var tops = body.Split(';');
		for (int i = 0; i < tops.Length; i++) {
			var sub = tops[i].Split(':');
			var code = sub.Length == 0 ? "" : sub[0];
			if (code == "0") faint = false;
			else if (code == "2") faint = true;
			else if (code == "22") faint = false;
			else if ((code == "38" || code == "48") && sub.Length == 1 && i + 1 < tops.Length && tops[i + 1] == "2") i += 3;
		}
	}

	static int wide(char c) {
		if (c >= 0x1100 && c <= 0x115F) return 2;
		if (c >= 0x2E80 && c <= 0xA4CF) return 2;
		if (c >= 0xAC00 && c <= 0xD7A3) return 2;
		if (c >= 0xF900 && c <= 0xFAFF) return 2;
		if (c >= 0xFE10 && c <= 0xFE6F) return 2;
		if (c >= 0xFF01 && c <= 0xFF60) return 2;
		if (c >= 0xFFE0 && c <= 0xFFE6) return 2;
		return 1;
	}

	static List<string> wezobjs(string json) {
		var list = new List<string>();
		int depth = 0, start = -1;
		for (int i = 0; i < json.Length; i++) {
			if (json[i] == '{') {
				if (depth == 0) start = i;
				depth++;
			} else if (json[i] == '}') {
				depth--;
				if (depth == 0 && start >= 0) {
					list.Add(json.Substring(start, i - start + 1));
					start = -1;
				}
			}
		}
		return list;
	}

	static int wezfield(string obj, string name, int fallback = 0) {
		var key = "\"" + name + "\"";
		int i = obj.IndexOf(key);
		if (i < 0) return fallback;
		i += key.Length;
		while (i < obj.Length && (obj[i] == ' ' || obj[i] == ':' || obj[i] == '\t')) i++;
		int n = 0;
		bool any = false, neg = false;
		if (i < obj.Length && obj[i] == '-') { neg = true; i++; }
		while (i < obj.Length && obj[i] >= '0' && obj[i] <= '9') { any = true; n = n * 10 + (obj[i] - '0'); i++; }
		if (!any) return fallback;
		return neg ? -n : n;
	}

	static string wezstr(string obj, string name) {
		var key = "\"" + name + "\"";
		int i = obj.IndexOf(key);
		if (i < 0) return "";
		i = obj.IndexOf('"', i + key.Length);
		if (i < 0) return "";
		int j = obj.IndexOf('"', i + 1);
		if (j < 0) return "";
		return obj.Substring(i + 1, j - i - 1);
	}

	static bool wezflag(string obj, string name) {
		var key = "\"" + name + "\"";
		int i = obj.IndexOf(key);
		if (i < 0) return false;
		int n = Math.Min(obj.Length - i, 24);
		return obj.IndexOf("true", i, n) >= 0;
	}

	string wezcli(string args) {
		try {
			var p = new Process();
			p.StartInfo.FileName = wezbin;
			p.StartInfo.Arguments = args;
			p.StartInfo.UseShellExecute = false;
			p.StartInfo.RedirectStandardOutput = true;
			p.StartInfo.CreateNoWindow = true;
			p.StartInfo.StandardOutputEncoding = Encoding.UTF8;
			if (!p.Start()) return null;
			var text = p.StandardOutput.ReadToEnd();
			if (!p.WaitForExit(800)) {
				try { p.Kill(); } catch { }
				return null;
			}
			if (p.ExitCode != 0) return null;
			return text;
		} catch { return null; }
	}

	static void syscaret(out int left, out int bottom) {
		left = 0;
		bottom = 0;
		var info = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
		if (!GetGUIThreadInfo(0, ref info) || info.hwndCaret == IntPtr.Zero) return;
		var rc = info.rcCaret;
		var pt = new POINT { X = rc.left, Y = rc.bottom };
		ClientToScreen(info.hwndCaret, ref pt);
		left = pt.X;
		bottom = pt.Y;
	}

	static string copyutf(IntPtr p, int cb) {
		if (p == IntPtr.Zero || cb <= 0) return "";
		int n = cb;
		if (n > 0) {
			var last = Marshal.ReadByte(p, n - 1);
			if (last == 0) n--;
		}
		if (n <= 0) return "";
		var buf = new byte[n];
		Marshal.Copy(p, buf, 0, n);
		return Encoding.UTF8.GetString(buf);
	}

	static string winclass(IntPtr hwnd) {
		var buf = new StringBuilder(64);
		int n = GetClassName(hwnd, buf, buf.Capacity);
		return n <= 0 ? "" : buf.ToString();
	}

	delegate IntPtr VimWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
	delegate bool EnumChildProc(IntPtr hwnd, IntPtr lp);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	struct WNDCLASS {
		public uint style;
		public IntPtr lpfnWndProc;
		public int cbClsExtra;
		public int cbWndExtra;
		public IntPtr hInstance;
		public IntPtr hIcon;
		public IntPtr hCursor;
		public IntPtr hbrBackground;
		public string lpszMenuName;
		public string lpszClassName;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct COPYDATA {
		public IntPtr dwData;
		public int cbData;
		public IntPtr lpData;
	}

	struct RECT { public int left, top, right, bottom; }
	struct POINT { public int X, Y; }
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

	[DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
	[DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hwnd);
	[DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
	[DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint id, ref GUITHREADINFO info);
	[DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd, ref POINT pt);
	[DllImport("user32.dll")] static extern bool GetCursorPos(out POINT pt);
	[DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rc);
	[DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out RECT rc);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder buf, int n);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
	[DllImport("user32.dll")] static extern bool EnumWindows(EnumChildProc proc, IntPtr lp);
	[DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumChildProc proc, IntPtr lp);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClass(ref WNDCLASS wc);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
	[DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
	[DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
	[DllImport("user32.dll", SetLastError = true)] static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
}
