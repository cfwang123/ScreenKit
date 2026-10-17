using System.Runtime.InteropServices;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ScreenKit;

/// <summary>
/// 按快捷键接管键盘。五笔、日语、韩语共用候选条，词从可安装的 ime.db 来。
/// 组字只显示在候选条上，选定后再送进当前前台窗口（含本程序窗口）。
/// 单独按 Shift 在当前语言和英文之间切换。右 Alt 或韩/英键同样切换英文。
/// </summary>
sealed class HangulIme : IDisposable {
	const int WH_KEYBOARD_LL = 13;
	const int WM_KEYDOWN = 0x0100;
	const int WM_SYSKEYDOWN = 0x0104;
	const int LLKHF_INJECTED = 0x10;
	const int VK_BACK = 0x08;
	const int VK_ESC = 0x1B;
	const int VK_HANGUL = 0x15;
	const int VK_RMENU = 0xA5;
	const uint SWP_NOSIZE = 0x0001;
	const uint SWP_NOMOVE = 0x0002;
	const uint SWP_SHOWWINDOW = 0x0040;

	readonly ImeEngine eng = new();
	readonly HangulCand bar = new();
	readonly ImeCaret caret = new();
	readonly HashSet<int> eaten = new();
	readonly Dictionary<string, System.Drawing.Icon> icons = new();
	readonly DispatcherTimer timer;
	Forms.NotifyIcon ni;
	Forms.ContextMenuStrip menu;
	Forms.ToolStripMenuItem miwubi;
	Forms.ToolStripMenuItem mija;
	Forms.ToolStripMenuItem miko;
	Forms.ToolStripMenuItem mioff;
	Forms.ToolStripMenuItem miset;
	Forms.ToolStripMenuItem midict;
	IntPtr hook;
	HookProc proc;
	IntPtr watch;
	int nexthook;
	bool? vimInsert;
	bool gvimMode;
	Lang lang = Lang.Ko;
	bool trad;
	bool enabled;
	bool sending;
	bool eat;
	bool dead;

	public event Action Changed;
	public event Action Settings;
	public event Action Dict;

	public bool On => enabled;
	public bool English => eng.Now == View.En;
	public bool Paused => eng.Off;
	public bool Trad => eng.Trad;
	public string LangCode => eng.LangCode;
	public string Badge => eng.Badge;

	public HangulIme() {
		ImeCaret.BindShared(caret);
		caret.AttachDispatcher(System.Windows.Application.Current?.Dispatcher ?? bar.Dispatcher);
		caret.Start();
		timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
		timer.Tick += (_, _) => pollfg();
		bar.Picked += onpick;
		bar.Paged += onpage;
		eng.StateChanged += () => {
			if (!enabled) return;
			lang = eng.Cjk;
			trad = eng.Trad;
			ui(() => {
				showtray();
				Changed?.Invoke();
			});
		};
	}

	public void Remember(string code, bool tradOn) {
		trad = tradOn;
		lang = code == "ja" ? Lang.Ja : code == "wubi" ? Lang.Wubi : Lang.Ko;
	}

	public void ApplyGvimMode(bool on) {
		gvimMode = on;
		if (!on) vimInsert = null;
	}

	public void ApplyHotkeys(string zh, string ja, string ko, string paused, string cycle) {
		eng.ApplyOpts(true, true, true,
			Hot.FromCombo(zh), Hot.FromCombo(ja), Hot.FromCombo(ko),
			Hot.FromCombo(paused), Hot.FromCombo(cycle));
	}

	public void Enter(string code) {
		if (!ImeLex.DbInstalled) return;
		var pick = code == "ja" ? Lang.Ja : code == "ko" ? Lang.Ko : Lang.Wubi;
		if (!enabled && !Set(true)) return;
		eng.Enter(pick);
		showtray();
	}

	public void Pause() {
		if (!enabled) return;
		eng.SetOff(true);
		showtray();
	}

	public void Next() {
		if (!ImeLex.DbInstalled) return;
		if (!enabled) { Set(true); return; }
		eng.Next();
		showtray();
	}

	public bool Set(bool on) {
		if (dead) return false;
		if (on == enabled) return true;
		if (!on) {
			eng.Discard();
			hidecand();
			enabled = false;
			remove();
			timer.Stop();
			if (ni != null) ni.Visible = false;
			Changed?.Invoke();
			return true;
		}
		if (!ImeLex.DbInstalled) return false;
		if (!install()) return false;
		ImeDict.SyncAll();
		eng.LoadMeta(trad, lang, false, false);
		enabled = true;
		watch = GetForegroundWindow();
		showtray();
		timer.Start();
		Changed?.Invoke();
		return true;
	}

	public void ToggleEnglish() {
		if (!enabled) return;
		eng.GvimInsert = gvimMode && caret.IsGvimForeground() && caret.TryGvimInsert(out var ins) && ins;
		eng.ToggleEn();
		showtray();
	}

	public void ApplyLang() { applytraylang(); }

	public void Dispose() {
		if (dead) return;
		dead = true;
		try { caret.Dispose(); } catch { }
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
		foreach (var ic in icons.Values) {
			try { ic?.Dispose(); } catch { }
		}
		icons.Clear();
	}

	void pollfg() {
		if (!enabled) return;
		if (Environment.TickCount - nexthook >= 2000) {
			nexthook = Environment.TickCount;
			front();
		}
		if (eng.Off) return;
		var fg = GetForegroundWindow();
		if (fg == IntPtr.Zero) return;
		if (watch != IntPtr.Zero && watch != fg) {
			if (eng.ShowBar) {
				eng.Discard();
				hidecand();
			}
			vimInsert = null;
		}
		watch = fg;
		if (gvimMode && caret.IsGvimForeground()) pollvim();
		else vimInsert = null;
	}

	void pollvim() {
		if (!caret.TryGvimInsert(out var ins)) return;
		if (vimInsert == null) {
			vimInsert = ins;
			if (ins) eng.RestoreGvimInsertEn();
			else eng.ForceEnglishForVim();
			showtray();
			return;
		}
		if (vimInsert == ins) return;
		vimInsert = ins;
		if (ins) eng.RestoreGvimInsertEn();
		else eng.ForceEnglishForVim();
		showtray();
	}

	bool install() {
		if (hook != IntPtr.Zero) return true;
		int n = Marshal.SizeOf(typeof(INPUT));
		if (n != 40) return false;
		proc = callback;
		hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
		return hook != IntPtr.Zero;
	}

	void front() {
		if (hook == IntPtr.Zero || proc == null) return;
		var next = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
		if (next == IntPtr.Zero) return;
		UnhookWindowsHookEx(hook);
		hook = next;
	}

	void remove() {
		if (hook == IntPtr.Zero) return;
		UnhookWindowsHookEx(hook);
		hook = IntPtr.Zero;
		eaten.Clear();
	}

	void showtray() {
		ensuretray();
		if (eng.Off) {
			ni.Visible = false;
			if (mioff != null) mioff.Checked = true;
			return;
		}
		var mark = eng.Badge;
		if (!icons.TryGetValue(mark, out var ic)) {
			ic = badge(mark, eng.Off || eng.Now == View.En);
			icons[mark] = ic;
		}
		ni.Icon = ic;
		var bkey = eng.Off ? "hangul.badge.off"
			: eng.Now == View.Ja ? "hangul.badge.ja"
			: eng.Now == View.Ko ? "hangul.badge.ko"
			: eng.Now == View.En ? "hangul.badge.en"
			: "hangul.badge.wubi";
		var text = Loc.T(bkey) ?? "";
		if (text.Length > 63) text = text.Substring(0, 63);
		ni.Text = text;
		ni.Visible = true;
		if (miwubi != null) {
			miwubi.Checked = !eng.Off && eng.Cjk == Lang.Wubi;
			mija.Checked = !eng.Off && eng.Cjk == Lang.Ja;
			miko.Checked = !eng.Off && eng.Cjk == Lang.Ko;
			mioff.Checked = eng.Off;
		}
	}

	void ensuretray() {
		if (ni != null) return;
		ni = new Forms.NotifyIcon { Visible = false };
		menu = new Forms.ContextMenuStrip();
		miwubi = new Forms.ToolStripMenuItem("", null, (_, _) => Enter("wubi"));
		mija = new Forms.ToolStripMenuItem("", null, (_, _) => Enter("ja"));
		miko = new Forms.ToolStripMenuItem("", null, (_, _) => Enter("ko"));
		mioff = new Forms.ToolStripMenuItem("", null, (_, _) => Pause());
		miset = new Forms.ToolStripMenuItem();
		miset.Click += (_, _) => { try { Settings?.Invoke(); } catch { } };
		midict = new Forms.ToolStripMenuItem();
		midict.Click += (_, _) => { try { Dict?.Invoke(); } catch { } };
		menu.Items.Add(miwubi);
		menu.Items.Add(mija);
		menu.Items.Add(miko);
		menu.Items.Add(mioff);
		menu.Items.Add(miset);
		menu.Items.Add(midict);
		ni.MouseClick += (_, e) => {
			if (e.Button == Forms.MouseButtons.Left) Next();
		};
		ni.MouseUp += (_, e) => {
			if (e.Button == Forms.MouseButtons.Right) showmenu();
		};
		applytraylang();
	}

	void showmenu() {
		if (menu == null) return;
		var p = Forms.Cursor.Position;
		var wa = Forms.Screen.FromPoint(p).WorkingArea;
		var sz = menu.GetPreferredSize(System.Drawing.Size.Empty);
		int x = p.X - sz.Width;
		int y = p.Y - sz.Height;
		if (x + sz.Width > wa.Right) x = wa.Right - sz.Width;
		if (y + sz.Height > wa.Bottom) y = wa.Bottom - sz.Height;
		if (x < wa.Left) x = wa.Left;
		if (y < wa.Top) y = wa.Top;
		SetForegroundWindow(menu.Handle);
		menu.Show(new System.Drawing.Point(x, y));
		SetWindowPos(menu.Handle, new IntPtr(-1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
	}

	void applytraylang() {
		if (miwubi != null) miwubi.Text = Loc.T("hangul.menu.wubi");
		if (mija != null) mija.Text = Loc.T("hangul.menu.ja");
		if (miko != null) miko.Text = Loc.T("hangul.menu.ko");
		if (mioff != null) mioff.Text = Loc.T("hangul.menu.off");
		if (miset != null) miset.Text = Loc.T("hangul.menu.settings");
		if (midict != null) midict.Text = Loc.T("hangul.menu.dict");
		if (ni != null && enabled) showtray();
	}

	static System.Drawing.Icon badge(string mark, bool gray) {
		const int n = 64;
		using var bmp = new System.Drawing.Bitmap(n, n);
		using (var g = System.Drawing.Graphics.FromImage(bmp)) {
			g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
			g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
			g.Clear(gray ? System.Drawing.Color.FromArgb(90, 90, 90) : System.Drawing.Color.FromArgb(20, 110, 70));
			var text = string.IsNullOrEmpty(mark) ? "A" : mark;
			using var font = new System.Drawing.Font("Microsoft YaHei UI", 100f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
			using var path = new System.Drawing.Drawing2D.GraphicsPath();
			path.AddString(text, font.FontFamily, (int)System.Drawing.FontStyle.Bold, 100f, new System.Drawing.PointF(0, 0), System.Drawing.StringFormat.GenericTypographic);
			var box = path.GetBounds();
			float pad = 3f;
			float s = 1f;
			if (box.Width > 1f && box.Height > 1f)
				s = Math.Min((n - pad * 2f) / box.Width, (n - pad * 2f) / box.Height);
			g.TranslateTransform((n - box.Width * s) / 2f - box.X * s, (n - box.Height * s) / 2f - box.Y * s);
			g.ScaleTransform(s, s);
			using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
			g.FillPath(brush, path);
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
		bool ctrl = downctrl();
		bool win = downwin();
		bool alt = downalt();
		bool shift = downshift();
		bool gvim = gvimMode && caret.IsGvimForeground();
		eng.GvimInsert = gvim && caret.TryGvimInsert(out var vins) && vins;
		if (gvim && down && vk == VK_ESC && !ctrl && !alt && !win) {
			eng.ForceEnglishForVim();
			vimInsert = false;
			showtray();
		}
		if (down && (vk == VK_RMENU || vk == VK_HANGUL) && !ctrl && !win) {
			ToggleEnglish();
			markeat(vk);
			refresh();
			return;
		}
		if (!eng.Off) {
			if (vk == Vk.Caps && down) {
				eng.SetCaps((GetKeyState(Vk.Caps) & 1) == 0);
				refresh();
				return;
			}
			if (vk != Vk.Caps) eng.SetCaps((GetKeyState(Vk.Caps) & 1) != 0);
		}
		var o = eng.Key(vk, down, shift, ctrl, alt, win);
		if (o.Eat) eat = true;
		else if (!down) eat = eaten.Remove(vk);
		send(o);
		if (o.OpenDict) ui(() => { try { Dict?.Invoke(); } catch { } });
		refresh();
	}

	void onpick(int visible) {
		var o = eng.Pick(visible);
		send(o);
		refresh();
	}

	void onpage(int delta) {
		eng.PageBy(delta);
		refresh();
	}

	void send(Outcome o) {
		if (o == null || o.Texts.Count == 0) return;
		var sb = new System.Text.StringBuilder();
		foreach (var t in o.Texts) sb.Append(t);
		replace(0, sb.ToString());
	}

	void refresh() {
		if (!enabled) return;
		if (bar.Dispatcher.CheckAccess()) refreshui();
		else bar.Dispatcher.BeginInvoke(new Action(refreshui));
	}

	void refreshui() {
		showtray();
		if (!eng.ShowBar) {
			hidecand();
			return;
		}
		try { bar.ShowEngine(eng, caretbox()); }
		catch { }
	}

	void ui(Action act) {
		if (act == null) return;
		if (bar.Dispatcher.CheckAccess()) act();
		else bar.Dispatcher.BeginInvoke(act);
	}

	void hidecand() {
		try { bar.HideBar(); } catch { }
	}

	Rect caretbox() {
		if (!caret.TryAnchor(out var px, out var py)) return Rect.Empty;
		return new Rect(px, py, 0, 0);
	}

	void markeat(int vk) {
		eat = true;
		eaten.Add(vk);
	}

	static bool downshift() => (GetKeyState(Vk.Shift) & 0x8000) != 0;
	static bool downctrl() => (GetKeyState(Vk.Ctrl) & 0x8000) != 0;
	static bool downalt() => (GetKeyState(Vk.Alt) & 0x8000) != 0;
	static bool downwin() => (GetKeyState(Vk.LWin) & 0x8000) != 0 || (GetKeyState(Vk.RWin) & 0x8000) != 0;

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
	[DllImport("user32.dll")] static extern short GetKeyState(int vKey);
	[DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
	[DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
	[DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
	[DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
