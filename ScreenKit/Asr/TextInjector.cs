using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace ScreenKit;

/// <summary>
/// 将文本注入当前焦点窗口：优先 SendInput(Unicode)，失败则剪贴板 Ctrl+V。
/// </summary>
static class TextInjector {
	const int INPUT_KEYBOARD = 1;
	const uint KEYEVENTF_KEYUP = 0x0002;
	const uint KEYEVENTF_UNICODE = 0x0004;

	// x64 上 INPUT 必须按官方布局（type + 对齐 + 与 MOUSEINPUT 同大的 union）
	[StructLayout(LayoutKind.Sequential)]
	struct INPUT {
		public int type;
		public InputUnion u;
	}

	[StructLayout(LayoutKind.Explicit)]
	struct InputUnion {
		[FieldOffset(0)] public MOUSEINPUT mi;
		[FieldOffset(0)] public KEYBDINPUT ki;
		[FieldOffset(0)] public HARDWAREINPUT hi;
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
	struct HARDWAREINPUT {
		public uint uMsg;
		public ushort wParamL;
		public ushort wParamH;
	}

	[DllImport("user32.dll", SetLastError = true)]
	static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

	[DllImport("user32.dll")]
	static extern IntPtr GetForegroundWindow();

	static readonly int InputSize = Marshal.SizeOf(typeof(INPUT));

	/// <summary>向焦点控件注入文本。先 Unicode 按键，失败再用剪贴板粘贴。</summary>
	public static bool TypeText(string text) {
		if (string.IsNullOrEmpty(text)) return true;
		text = text.Replace("\r\n", "\n").Replace('\r', '\n');

		if (trySendInput(text)) return true;
		return tryPasteClipboard(text);
	}

	/// <summary>
	/// 删除焦点处光标左侧 <paramref name="count"/> 个字符（Backspace）。
	/// 用于流式识别改写前文时撤掉已注入内容。
	/// </summary>
	public static bool Backspace(int count) {
		if (count <= 0) return true;
		if (count > 5000) count = 5000;
		try {
			var inputs = new List<INPUT>(Math.Min(count * 2, 128));
			for (int i = 0; i < count; i++) {
				addVk(inputs, (ushort)Forms.Keys.Back, false);
				addVk(inputs, (ushort)Forms.Keys.Back, true);
				if (inputs.Count >= 64) {
					if (!flush(inputs)) return false;
					inputs.Clear();
				}
			}
			return inputs.Count == 0 || flush(inputs);
		}
		catch (Exception ex) {
			try { CaptureLog.Info("TextInjector.Backspace ex: " + ex.Message); } catch { }
			return false;
		}
	}

	static bool trySendInput(string text) {
		try {
			var inputs = new List<INPUT>(Math.Min(text.Length * 2, 512));
			foreach (var ch in text) {
				if (ch == '\n') {
					addVk(inputs, (ushort)Forms.Keys.Return, false);
					addVk(inputs, (ushort)Forms.Keys.Return, true);
				}
				else {
					addUnicode(inputs, ch, false);
					addUnicode(inputs, ch, true);
				}
				if (inputs.Count >= 64) {
					if (!flush(inputs)) return false;
					inputs.Clear();
				}
			}
			if (inputs.Count > 0 && !flush(inputs)) return false;
			return true;
		}
		catch (Exception ex) {
			try { CaptureLog.Info("TextInjector.SendInput ex: " + ex.Message); } catch { }
			return false;
		}
	}

	static bool flush(List<INPUT> inputs) {
		if (inputs == null || inputs.Count == 0) return true;
		var arr = inputs.ToArray();
		var n = SendInput((uint)arr.Length, arr, InputSize);
		if (n != (uint)arr.Length) {
			var err = Marshal.GetLastWin32Error();
			try {
				CaptureLog.Info($"TextInjector.SendInput fail n={n}/{arr.Length} err={err} size={InputSize}");
			}
			catch { }
			return false;
		}
		return true;
	}

	static void addUnicode(List<INPUT> list, char ch, bool up) {
		list.Add(new INPUT {
			type = INPUT_KEYBOARD,
			u = new InputUnion {
				ki = new KEYBDINPUT {
					wVk = 0,
					wScan = ch,
					dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0u),
					time = 0,
					dwExtraInfo = IntPtr.Zero,
				},
			},
		});
	}

	static void addVk(List<INPUT> list, ushort vk, bool up) {
		list.Add(new INPUT {
			type = INPUT_KEYBOARD,
			u = new InputUnion {
				ki = new KEYBDINPUT {
					wVk = vk,
					wScan = 0,
					dwFlags = up ? KEYEVENTF_KEYUP : 0u,
					time = 0,
					dwExtraInfo = IntPtr.Zero,
				},
			},
		});
	}

	static bool tryPasteClipboard(string text) {
		try {
			Forms.IDataObject old = null;
			try { old = Forms.Clipboard.GetDataObject(); } catch { }

			var ok = false;
			for (int i = 0; i < 3 && !ok; i++) {
				try {
					Forms.Clipboard.SetText(text);
					ok = true;
				}
				catch {
					Thread.Sleep(30);
				}
			}
			if (!ok) {
				try { CaptureLog.Info("TextInjector.Clipboard.SetText fail"); } catch { }
				return false;
			}

			var inputs = new List<INPUT>(4);
			addVk(inputs, (ushort)Forms.Keys.ControlKey, false);
			addVk(inputs, (ushort)Forms.Keys.V, false);
			addVk(inputs, (ushort)Forms.Keys.V, true);
			addVk(inputs, (ushort)Forms.Keys.ControlKey, true);
			var sent = flush(inputs);

			Thread.Sleep(40);
			try {
				if (old != null) Forms.Clipboard.SetDataObject(old, true);
			}
			catch { }

			if (!sent) {
				try {
					CaptureLog.Info("TextInjector.Ctrl+V fail fg=" + GetForegroundWindow());
				}
				catch { }
			}
			return sent;
		}
		catch (Exception ex) {
			try { CaptureLog.Info("TextInjector.paste ex: " + ex.Message); } catch { }
			return false;
		}
	}
}
