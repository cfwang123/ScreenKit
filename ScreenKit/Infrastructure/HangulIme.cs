using System.Runtime.InteropServices;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ScreenKit;

/// <summary>
/// 按快捷键接管键盘，行为对齐 Windows 韩语输入法：两套式在光标处组字，没有候选条。
/// 打开后另有一个托盘图标，左键切换韩文和英文，右键可关闭或打开参数。本程序窗口里的按键不接管。
/// 单独按 Shift、右 Alt 或韩/英键在韩文和英文之间切换。Shift 加字母仍是双辅音或双元音。
/// 空格、回车和标点先留下已组的字，再把该键交给当前窗口。
/// </summary>
sealed class HangulIme : IDisposable {
	const int WH_KEYBOARD_LL = 13;
	const int WM_KEYDOWN = 0x0100;
	const int WM_SYSKEYDOWN = 0x0104;
	const int LLKHF_INJECTED = 0x10;
	const int VK_BACK = 0x08;
	const int VK_ESC = 0x1B;
	const int VK_HANGUL = 0x15;
	const int VK_SHIFT = 0x10;
	const int VK_CONTROL = 0x11;
	const int VK_MENU = 0x12;
	const int VK_RMENU = 0xA5;
	const int VK_LWIN = 0x5B;
	const int VK_RWIN = 0x5C;
	const int VK_A = 0x41;

	readonly Hangul hang = new();
	readonly HashSet<int> eaten = new();
	readonly DispatcherTimer timer;
	Forms.NotifyIcon ni;
	Forms.ContextMenuStrip menu;
	Forms.ToolStripMenuItem miset;
	Forms.ToolStripMenuItem miclose;
	System.Drawing.Icon icoko;
	System.Drawing.Icon icoen;
	IntPtr hook;
	HookProc proc;
	IntPtr watch;
	string shown = "";
	bool enabled;
	bool english;
	bool shiftlone;
	bool sending;
	bool eat;
	bool dead;

	public event Action Changed;
	public event Action Settings;

	public bool On => enabled;
	public bool English => english;

	public HangulIme() {
		timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
		timer.Tick += (_, _) => pollfg();
	}

	public bool Set(bool on) {
		if (dead) return false;
		if (on == enabled) return true;
		if (!on) {
			commit();
			enabled = false;
			english = false;
			remove();
			timer.Stop();
			if (ni != null) ni.Visible = false;
			Changed?.Invoke();
			return true;
		}
		if (!install()) return false;
		enabled = true;
		english = false;
		watch = GetForegroundWindow();
		showtray(false);
		timer.Start();
		Changed?.Invoke();
		return true;
	}

	public void ToggleEnglish() {
		if (!enabled) return;
		if (english) english = false;
		else {
			commit();
			english = true;
		}
		showtray(english);
		Changed?.Invoke();
	}

	public void ApplyLang() { applytraylang(); }

	public void Dispose() {
		if (dead) return;
		dead = true;
		try { Set(false); } catch { }
		try {
			if (ni != null) {
				ni.Visible = false;
				ni.Icon = null;
				ni.Dispose();
			}
		}
		catch { }
		ni = null;
		try { menu?.Dispose(); } catch { }
		menu = null;
		try { icoko?.Dispose(); } catch { }
		try { icoen?.Dispose(); } catch { }
		icoko = null;
		icoen = null;
	}

	void pollfg() {
		if (!enabled || english) return;
		var fg = GetForegroundWindow();
		if (fg == IntPtr.Zero) return;
		if (ours(fg)) {
			if (shown.Length > 0) commit();
			return;
		}
		if (watch != IntPtr.Zero && watch != fg && shown.Length > 0) commit();
		watch = fg;
	}

	bool install() {
		if (hook != IntPtr.Zero) return true;
		int n = Marshal.SizeOf(typeof(INPUT));
		if (n != 40) return false;
		proc = callback;
		hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
		return hook != IntPtr.Zero;
	}

	void remove() {
		if (hook == IntPtr.Zero) return;
		UnhookWindowsHookEx(hook);
		hook = IntPtr.Zero;
		eaten.Clear();
	}

	void showtray(bool en) {
		ensuretray();
		ni.Icon = en ? icoen : icoko;
		ni.Text = traytext(en);
		ni.Visible = true;
	}

	void ensuretray() {
		if (ni != null) return;
		icoko = badge(false);
		icoen = badge(true);
		ni = new Forms.NotifyIcon { Visible = false };
		menu = new Forms.ContextMenuStrip();
		miset = new Forms.ToolStripMenuItem();
		miclose = new Forms.ToolStripMenuItem();
		miset.Click += (_, _) => { try { Settings?.Invoke(); } catch { } };
		miclose.Click += (_, _) => Set(false);
		menu.Items.Add(miset);
		menu.Items.Add(miclose);
		ni.ContextMenuStrip = menu;
		ni.MouseClick += (_, e) => {
			if (e.Button == Forms.MouseButtons.Left) ToggleEnglish();
		};
		applytraylang();
	}

	void applytraylang() {
		if (miset != null) miset.Text = Loc.T("hangul.menu.settings");
		if (miclose != null) miclose.Text = Loc.T("hangul.menu.close");
		if (ni != null && enabled) ni.Text = traytext(english);
	}

	static string traytext(bool en) {
		var s = Loc.T(en ? "hangul.en" : "hangul.ko") ?? "";
		if (s.Length > 63) s = s.Substring(0, 63);
		return s;
	}

	static System.Drawing.Icon badge(bool en) {
		using var bmp = new System.Drawing.Bitmap(32, 32);
		using (var g = System.Drawing.Graphics.FromImage(bmp)) {
			g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
			g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
			g.Clear(en ? System.Drawing.Color.FromArgb(90, 90, 90) : System.Drawing.Color.FromArgb(20, 110, 70));
			float size = en ? 18f : 13f;
			using var font = new System.Drawing.Font("Malgun Gothic", size, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
			var text = en ? "A" : "한";
			var box = g.MeasureString(text, font);
			using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
			g.DrawString(text, font, brush, (32f - box.Width) / 2f, (32f - box.Height) / 2f - 1f);
		}
		var h = bmp.GetHicon();
		try {
			using var tmp = System.Drawing.Icon.FromHandle(h);
			return (System.Drawing.Icon)tmp.Clone();
		}
		finally { DestroyIcon(h); }
	}

	IntPtr callback(int nCode, IntPtr wParam, IntPtr lParam) {
		if (nCode < 0 || sending || !enabled) return CallNextHookEx(hook, nCode, wParam, lParam);
		var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
		if ((info.flags & LLKHF_INJECTED) != 0) return CallNextHookEx(hook, nCode, wParam, lParam);
		int msg = wParam.ToInt32();
		bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
		bool up = msg == 0x0101 || msg == 0x0105;
		if (!down && !up) return CallNextHookEx(hook, nCode, wParam, lParam);
		eat = false;
		onkey((int)info.vkCode, down);
		return eat ? (IntPtr)1 : CallNextHookEx(hook, nCode, wParam, lParam);
	}

	void onkey(int vk, bool down) {
		bool shiftkey = vk == VK_SHIFT || vk == 0xA0 || vk == 0xA1;
		if (!down) {
			if (shiftkey && shiftlone) {
				shiftlone = false;
				if (!ours(GetForegroundWindow())) ToggleEnglish();
			}
			eat = eaten.Remove(vk);
			return;
		}
		if (ours(GetForegroundWindow())) {
			shiftlone = false;
			if (shown.Length > 0) commit();
			return;
		}
		bool ctrl = held(VK_CONTROL) || held(0xA2) || held(0xA3);
		bool win = held(VK_LWIN) || held(VK_RWIN);
		bool alt = held(VK_MENU) || held(0xA4);
		if (shiftkey) {
			shiftlone = !ctrl && !alt && !win;
			return;
		}
		shiftlone = false;
		if ((vk == VK_RMENU || vk == VK_HANGUL) && !ctrl && !win) {
			ToggleEnglish();
			markeat(vk);
			return;
		}
		if (english) return;
		if (ctrl || alt || win) {
			commit();
			return;
		}
		if (vk == VK_ESC) {
			cancel();
			return;
		}
		if (vk == VK_BACK) {
			if (hang.Text.Length == 0) return;
			hang.Back();
			sync();
			markeat(vk);
			return;
		}
		if (vk >= VK_A && vk <= 0x5A) {
			char lower = (char)('a' + vk - VK_A);
			bool shift = held(VK_SHIFT) || held(0xA0) || held(0xA1);
			bool ok = shift && hang.Push(char.ToUpper(lower));
			if (!ok) ok = hang.Push(lower);
			if (!ok) {
				commit();
				return;
			}
			sync();
			markeat(vk);
			return;
		}
		commit();
	}

	void cancel() {
		if (hang.Partial.Length > 0) {
			replace(hang.Partial.Length, "");
			hang.Clear();
			shown = "";
			markeat(VK_ESC);
			return;
		}
		if (shown.Length > 0) commit();
	}

	void commit() {
		hang.Clear();
		shown = "";
	}

	void sync() {
		string text = hang.Text;
		int n = 0;
		int m = Math.Min(shown.Length, text.Length);
		while (n < m && shown[n] == text[n]) n++;
		replace(shown.Length - n, text.Substring(n));
		shown = text;
	}

	void markeat(int vk) {
		eat = true;
		eaten.Add(vk);
	}

	static bool held(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

	static bool ours(IntPtr fg) {
		if (fg == IntPtr.Zero) return false;
		GetWindowThreadProcessId(fg, out var pid);
		return pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
	}

	void replace(int dels, string add) {
		var list = new List<INPUT>();
		for (int i = 0; i < dels; i++) {
			list.Add(keyvk(VK_BACK, false));
			list.Add(keyvk(VK_BACK, true));
		}
		if (!string.IsNullOrEmpty(add)) {
			foreach (var ch in add) {
				list.Add(keych(ch, false));
				list.Add(keych(ch, true));
			}
		}
		if (list.Count == 0) return;
		sending = true;
		try {
			var arr = list.ToArray();
			SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(INPUT)));
		}
		finally { sending = false; }
	}

	static INPUT keych(char ch, bool up) {
		return new INPUT {
			type = 1,
			U = new InputUnion {
				ki = new KEYBDINPUT {
					wScan = ch,
					dwFlags = 0x04u | (up ? 0x02u : 0u),
				},
			},
		};
	}

	static INPUT keyvk(ushort code, bool up) {
		return new INPUT {
			type = 1,
			U = new InputUnion {
				ki = new KEYBDINPUT {
					wVk = code,
					dwFlags = up ? 0x02u : 0u,
				},
			},
		};
	}

	delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

	[StructLayout(LayoutKind.Sequential)]
	struct KBDLLHOOKSTRUCT {
		public uint vkCode;
		public uint scanCode;
		public uint flags;
		public uint time;
		public UIntPtr dwExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct INPUT {
		public uint type;
		public InputUnion U;
	}

	[StructLayout(LayoutKind.Explicit)]
	struct InputUnion {
		[FieldOffset(0)] public MOUSEINPUT mi;
		[FieldOffset(0)] public KEYBDINPUT ki;
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
	struct KEYBDINPUT {
		public ushort wVk;
		public ushort wScan;
		public uint dwFlags;
		public uint time;
		public IntPtr dwExtraInfo;
	}

	[DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint tid);
	[DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
	[DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
	[DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int cb);
	[DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
	[DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
	[DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
	[DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
}
