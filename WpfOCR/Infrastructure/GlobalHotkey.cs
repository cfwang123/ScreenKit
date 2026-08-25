using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;

namespace WpfOCR;

/// <summary>
/// 全局热键 RegisterHotKey。热键字符串如 Ctrl+Alt+O。
/// </summary>
sealed class GlobalHotkey : IDisposable {
	const int WM_HOTKEY = 0x0312;
	const uint MOD_ALT = 0x0001;
	const uint MOD_CONTROL = 0x0002;
	const uint MOD_SHIFT = 0x0004;
	const uint MOD_WIN = 0x0008;
	const uint MOD_NOREPEAT = 0x4000;

	[DllImport("user32.dll", SetLastError = true)]
	static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

	[DllImport("user32.dll", SetLastError = true)]
	static extern bool UnregisterHotKey(IntPtr hWnd, int id);

	[DllImport("user32.dll")]
	static extern short GetAsyncKeyState(int vKey);

	readonly Window win;
	readonly int id;
	HwndSource src;
	bool registered;
	string current = "";

	public event Action Fired;
	public string LastError { get; private set; } = "";
	public bool IsRegistered => registered;
	public string CurrentHotkey => current;

	public GlobalHotkey(Window window, int hotkeyId = 0x7001) {
		win = window ?? throw new ArgumentNullException(nameof(window));
		id = hotkeyId;
	}

	public void Attach() {
		if (src != null) return;
		var helper = new WindowInteropHelper(win);
		if (helper.Handle == IntPtr.Zero)
			win.SourceInitialized += onsource;
		else
			hook(helper.Handle);
	}

	void onsource(object sender, EventArgs e) {
		win.SourceInitialized -= onsource;
		hook(new WindowInteropHelper(win).Handle);
	}

	void hook(IntPtr hwnd) {
		src = HwndSource.FromHwnd(hwnd);
		src?.AddHook(wndproc);
	}

	/// <summary>注册热键；空字符串表示禁用（仅注销，返回 true）。</summary>
	public bool Register(string hotkey) {
		Unregister();
		LastError = "";
		hotkey = (hotkey ?? "").Trim();
		// 留空 = 不注册
		if (string.IsNullOrEmpty(hotkey)) {
			current = "";
			return true;
		}
		if (!tryparse(hotkey, out var mod, out var vk)) {
			LastError = $"无法解析热键: {hotkey}";
			return false;
		}
		var hwnd = new WindowInteropHelper(win).Handle;
		if (hwnd == IntPtr.Zero) {
			LastError = "窗口句柄未就绪";
			return false;
		}
		if (src == null) hook(hwnd);
		// MOD_NOREPEAT 避免长按连发
		if (!RegisterHotKey(hwnd, id, mod | MOD_NOREPEAT, vk)) {
			var err = Marshal.GetLastWin32Error();
			LastError = err == 1409
				? $"热键已被占用: {hotkey}"
				: $"注册热键失败 ({err}): {hotkey}";
			return false;
		}
		registered = true;
		current = hotkey;
		return true;
	}

	public void Unregister() {
		if (!registered) return;
		try {
			var hwnd = new WindowInteropHelper(win).Handle;
			if (hwnd != IntPtr.Zero)
				UnregisterHotKey(hwnd, id);
		}
		catch { }
		registered = false;
		current = "";
	}

	IntPtr wndproc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
		if (msg == WM_HOTKEY && wParam.ToInt32() == id) {
			try { Fired?.Invoke(); } catch { }
			handled = true;
		}
		return IntPtr.Zero;
	}

	public void Dispose() {
		Unregister();
		try {
			src?.RemoveHook(wndproc);
			src = null;
		}
		catch { }
	}

	/// <summary>热键组合当前是否全部按下（用于结束听写后等松键再重新注册，避免立刻再触发）。</summary>
	public static bool IsComboDown(string hotkey) {
		if (!tryparse(hotkey, out var mod, out var vk)) return false;
		if ((mod & MOD_CONTROL) != 0 && (GetAsyncKeyState(0x11) & 0x8000) == 0) return false;
		if ((mod & MOD_ALT) != 0 && (GetAsyncKeyState(0x12) & 0x8000) == 0) return false;
		if ((mod & MOD_SHIFT) != 0 && (GetAsyncKeyState(0x10) & 0x8000) == 0) return false;
		if ((mod & MOD_WIN) != 0) {
			var l = (GetAsyncKeyState(0x5B) & 0x8000) != 0;
			var r = (GetAsyncKeyState(0x5C) & 0x8000) != 0;
			if (!l && !r) return false;
		}
		return (GetAsyncKeyState((int)vk) & 0x8000) != 0;
	}

	public static bool tryparse(string text, out uint modifiers, out uint vk) {
		modifiers = 0;
		vk = 0;
		if (string.IsNullOrWhiteSpace(text)) return false;
		var parts = text.Split(new[] { '+', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
		Key? key = null;
		foreach (var raw in parts) {
			var p = raw.Trim();
			if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
				|| p.Equals("Control", StringComparison.OrdinalIgnoreCase))
				modifiers |= MOD_CONTROL;
			else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase))
				modifiers |= MOD_ALT;
			else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase))
				modifiers |= MOD_SHIFT;
			else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase)
				|| p.Equals("Windows", StringComparison.OrdinalIgnoreCase))
				modifiers |= MOD_WIN;
			else {
				if (p.Length == 1) {
					var c = char.ToUpperInvariant(p[0]);
					if (c is >= 'A' and <= 'Z')
						key = Key.A + (c - 'A');
					else if (c is >= '0' and <= '9')
						key = Key.D0 + (c - '0');
				}
				else if (Enum.TryParse<Key>(p, true, out var k))
					key = k;
				else if (p.StartsWith("F", StringComparison.OrdinalIgnoreCase)
					&& int.TryParse(p.Substring(1), out var fn) && fn is >= 1 and <= 24)
					key = Key.F1 + (fn - 1);
			}
		}
		if (key == null) return false;
		vk = (uint)KeyInterop.VirtualKeyFromKey(key.Value);
		return vk != 0;
	}

	/// <summary>将修饰键 + 主键格式化为 Ctrl+Alt+O 风格（与 <see cref="tryparse"/> 对称）。</summary>
	public static string Format(ModifierKeys mods, Key key) {
		if (key == Key.None || ismodkey(key)) return "";
		var parts = new List<string>();
		if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
		if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
		if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
		if ((mods & ModifierKeys.Windows) != 0) parts.Add("Win");
		var name = keyname(key);
		if (string.IsNullOrEmpty(name)) return "";
		parts.Add(name);
		return string.Join("+", parts);
	}

	static bool ismodkey(Key key) =>
		key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
			or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
			or Key.System;

	static string keyname(Key key) {
		if (key is >= Key.A and <= Key.Z)
			return ((char)('A' + (key - Key.A))).ToString();
		if (key is >= Key.D0 and <= Key.D9)
			return ((char)('0' + (key - Key.D0))).ToString();
		if (key is >= Key.NumPad0 and <= Key.NumPad9)
			return ((char)('0' + (key - Key.NumPad0))).ToString();
		if (key is >= Key.F1 and <= Key.F24)
			return "F" + (1 + (key - Key.F1));
		// 常用特殊键：保持 Enum 名，tryparse 可识别
		return key switch {
			Key.Space => "Space",
			Key.Tab => "Tab",
			Key.OemPlus => "OemPlus",
			Key.OemMinus => "OemMinus",
			Key.OemComma => "OemComma",
			Key.OemPeriod => "OemPeriod",
			_ => key.ToString(),
		};
	}
}
