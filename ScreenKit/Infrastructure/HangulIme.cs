using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>
/// 按快捷键接管键盘，行为对齐 Windows 韩语输入法：两套式在光标处组字，没有候选条。
/// 右 Alt 或韩/英键在韩文和英文之间切换。空格、回车和标点先留下已组的字，再把该键交给当前窗口。
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
	Mark mark;
	IntPtr hook;
	HookProc proc;
	IntPtr watch;
	string shown = "";
	bool enabled;
	bool english;
	bool sending;
	bool eat;
	bool dead;

	public event Action Changed;

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
			if (mark != null) mark.Hide();
			Changed?.Invoke();
			return true;
		}
		if (!install()) return false;
		enabled = true;
		english = false;
		watch = GetForegroundWindow();
		ensuremark();
		mark.ShowAt(false);
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
		if (mark != null) mark.ShowAt(english);
		Changed?.Invoke();
	}

	public void ApplyLang() {
		if (mark != null) mark.Tip(Loc.T("hangul.tip"));
	}

	public void Dispose() {
		if (dead) return;
		dead = true;
		try { Set(false); } catch { }
		try { mark?.Close(); } catch { }
		mark = null;
	}

	void pollfg() {
		if (!enabled || english) return;
		var fg = GetForegroundWindow();
		if (fg == IntPtr.Zero) return;
		if (mark != null && mark.Hwnd == fg) return;
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

	void ensuremark() {
		if (mark != null) return;
		mark = new Mark();
		mark.Pick += () => ToggleEnglish();
		ApplyLang();
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
		if (!down) {
			eat = eaten.Remove(vk);
			return;
		}
		bool ctrl = held(VK_CONTROL) || held(0xA2) || held(0xA3);
		bool win = held(VK_LWIN) || held(VK_RWIN);
		if ((vk == VK_RMENU || vk == VK_HANGUL) && !ctrl && !win) {
			ToggleEnglish();
			markeat(vk);
			return;
		}
		if (english) return;
		bool alt = held(VK_MENU) || held(0xA4);
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

	sealed class Mark : Window {
		readonly TextBlock lb;
		public event Action Pick;
		public IntPtr Hwnd { get; private set; }

		public Mark() {
			WindowStyle = WindowStyle.None;
			ResizeMode = ResizeMode.NoResize;
			ShowInTaskbar = false;
			ShowActivated = false;
			Topmost = true;
			Focusable = false;
			Width = 52;
			Height = 36;
			Background = Brushes.Transparent;
			Content = new Border {
				CornerRadius = new CornerRadius(6),
				Child = lb = new TextBlock {
					Text = "한",
					FontSize = 18,
					FontFamily = new FontFamily("Malgun Gothic, Segoe UI"),
					HorizontalAlignment = HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center,
					Foreground = Brushes.White,
				},
			};
			MouseLeftButtonDown += (_, e) => {
				Pick?.Invoke();
				e.Handled = true;
			};
			SourceInitialized += (_, _) => {
				Hwnd = new WindowInteropHelper(this).Handle;
				var ex = GetWindowLongPtr(Hwnd, -20);
				var flags = ex.ToInt64() | 0x08000000L | 0x00000080L;
				SetWindowLongPtr(Hwnd, -20, new IntPtr(flags));
			};
		}

		public void ShowAt(bool en) {
			var box = (Border)Content;
			box.Background = en ? new SolidColorBrush(Color.FromRgb(90, 90, 90)) : new SolidColorBrush(Color.FromRgb(20, 110, 70));
			lb.Text = en ? "A" : "한";
			var area = SystemParameters.WorkArea;
			Left = area.Right - Width - 18;
			Top = area.Bottom - Height - 18;
			if (!IsVisible) Show();
		}

		public void Tip(string text) { ToolTip = text ?? ""; }
	}

	[DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint tid);
	[DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
	[DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
	[DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int cb);
	[DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
	[DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
