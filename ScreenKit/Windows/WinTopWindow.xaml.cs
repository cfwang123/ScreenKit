using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>工具 → 窗口管理：选择 HWND，设置或取消固定在前面。</summary>
public partial class WinTopWindow : Window {
	const int WH_MOUSE_LL = 14;
	const int WM_MOUSEMOVE = 0x0200;
	const int WM_LBUTTONDOWN = 0x0201;
	const int WM_RBUTTONDOWN = 0x0204;

	List<WinTopRow> all = new();
	IntPtr hook;
	HookProc hookproc;
	volatile bool picking;
	volatile int mouseX, mouseY;
	DispatcherTimer timer;
	WinTopFrame frame;
	IntPtr hoverHwnd;

	public WinTopWindow() {
		InitializeComponent();
		applylang();
		initev();
		WindowEsc.Attach(this, onesc);
		timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
		timer.Tick += (_, _) => syncframe();
		timer.Start();
		reload();
	}

	void initev() {
		brefresh.Click += (_, _) => reload();
		bpick.Click += (_, _) => beginpick();
		bpin.Click += (_, _) => apply(true);
		bunpin.Click += (_, _) => apply(false);
		bclose.Click += (_, _) => Close();
		efilter.TextChanged += (_, _) => showlist(keep());
		lv.SelectionChanged += (_, _) => {
			if (lv.SelectedItem is WinTopRow row) {
				ehwnd.Text = row.HwndHex;
				lbstat.Text = Loc.T("wintop.sel", row.HwndHex, row.Title);
			}
			if (!picking) syncframe();
		};
		lv.MouseDoubleClick += (_, _) => {
			if (lv.SelectedItem is WinTopRow row)
				apply(!row.Top);
		};
		Closing += (_, _) => {
			endpick();
			try { timer.Stop(); } catch { }
			try { frame?.Close(); } catch { }
			frame = null;
		};
	}

	void applylang() {
		Title = Loc.T("wintop.title");
		lbhint.Text = Loc.T("wintop.hint");
		lbfilter.Text = Loc.T("wintop.filter");
		efilter.ToolTip = Loc.T("wintop.filter.tip");
		lbhwnd.Text = Loc.T("wintop.hwnd");
		ehwnd.ToolTip = Loc.T("wintop.hwnd.tip");
		coltop.Header = Loc.T("wintop.col.top");
		colhwnd.Header = Loc.T("wintop.col.hwnd");
		coltitle.Header = Loc.T("wintop.col.title");
		colproc.Header = Loc.T("wintop.col.proc");
		ToolBtnUi.Set(brefresh, ToolBtnUi.Swap, Loc.T("wintop.refresh"));
		ToolBtnUi.Set(bpick, ToolBtnUi.Browse, Loc.T("wintop.pick"));
		ToolBtnUi.Set(bpin, ToolBtnUi.Up, Loc.T("wintop.pin"));
		ToolBtnUi.Set(bunpin, ToolBtnUi.Down, Loc.T("wintop.unpin"));
		ToolBtnUi.Set(bclose, ToolBtnUi.Close, Loc.T("imgconv.close"));
	}

	void onesc() {
		if (picking) {
			endpick();
			lbstat.Text = Loc.T("wintop.pick.cancel");
			syncframe();
			return;
		}
		Close();
	}

	void reload(string stat = null) {
		var keepHwnd = keep();
		try { all = WinTop.List(); }
		catch (Exception ex) {
			all = new List<WinTopRow>();
			lbstat.Text = Loc.T("wintop.fail", ex.Message);
			stat = null;
		}
		showlist(keepHwnd, stat);
	}

	void showlist(IntPtr keepHwnd, string stat = null) {
		var q = (efilter.Text ?? "").Trim();
		var view = new List<WinTopRow>();
		foreach (var row in all) {
			if (q.Length == 0 || hit(row, q)) view.Add(row);
		}
		lv.ItemsSource = view;
		WinTopRow sel = null;
		if (keepHwnd != IntPtr.Zero) {
			foreach (var row in view) {
				if (row.Hwnd == keepHwnd) { sel = row; break; }
			}
		}
		if (sel != null) {
			lv.SelectedItem = sel;
			lv.ScrollIntoView(sel);
		}
		if (!string.IsNullOrEmpty(stat)) lbstat.Text = stat;
		else if (lv.SelectedItem == null)
			lbstat.Text = Loc.T("wintop.count", view.Count);
	}

	static bool hit(WinTopRow row, string q) {
		if (row.Title != null && row.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (row.Proc != null && row.Proc.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (row.Cls != null && row.Cls.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (row.HwndHex.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		return false;
	}

	IntPtr keep() {
		if (lv.SelectedItem is WinTopRow row) return row.Hwnd;
		if (WinTop.TryParse(ehwnd.Text, out var h)) return h;
		return IntPtr.Zero;
	}

	IntPtr current() {
		if (WinTop.TryParse(ehwnd.Text, out var h) && WinTop.IsAlive(h)) return h;
		if (lv.SelectedItem is WinTopRow row && WinTop.IsAlive(row.Hwnd)) return row.Hwnd;
		return IntPtr.Zero;
	}

	void apply(bool on) {
		var h = current();
		if (h == IntPtr.Zero) {
			lbstat.Text = Loc.T("wintop.bad");
			ehwnd.Focus();
			return;
		}
		if (!WinTop.SetTop(h, on, out var err)) {
			var msg = err == 0 ? "" : new System.ComponentModel.Win32Exception(err).Message;
			lbstat.Text = Loc.T("wintop.fail", msg);
			return;
		}
		reload(on ? Loc.T("wintop.pinned") : Loc.T("wintop.unpinned"));
	}

	void beginpick() {
		if (picking) return;
		endpick();
		picking = true;
		hookproc = onhook;
		hook = SetWindowsHookEx(WH_MOUSE_LL, hookproc, GetModuleHandle(null), 0);
		if (hook == IntPtr.Zero) {
			picking = false;
			hookproc = null;
			lbstat.Text = Loc.T("wintop.pick.fail");
			return;
		}
		Cursor = Cursors.Cross;
		lbstat.Text = Loc.T("wintop.pick.hint");
		if (GetCursorPos(out var pt)) {
			mouseX = pt.X;
			mouseY = pt.Y;
		}
		syncframe();
	}

	void endpick() {
		picking = false;
		Cursor = null;
		var h = hook;
		hook = IntPtr.Zero;
		hookproc = null;
		if (h != IntPtr.Zero) {
			try { UnhookWindowsHookEx(h); } catch { }
		}
	}

	void syncframe() {
		var h = picking ? hover() : current();
		if (frame == null) {
			if (h == IntPtr.Zero) return;
			frame = new WinTopFrame();
		}
		frame.Place(h);
		if (!picking || h == IntPtr.Zero || h == hoverHwnd) return;
		hoverHwnd = h;
		try {
			var row = WinTop.Describe(h);
			lbstat.Text = Loc.T("wintop.sel", row.HwndHex, row.Title);
		}
		catch { }
	}

	IntPtr hover() {
		var h = WinTop.RootAt(mouseX, mouseY);
		if (frame != null && h == frame.Handle) return frame.Target;
		return h;
	}

	IntPtr onhook(int nCode, IntPtr wParam, IntPtr lParam) {
		if (nCode >= 0 && picking) {
			var msg = wParam.ToInt32();
			if (msg == WM_MOUSEMOVE || msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN) {
				mouseX = Marshal.ReadInt32(lParam);
				mouseY = Marshal.ReadInt32(lParam, 4);
			}
			if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN) {
				picking = false;
				var x = Marshal.ReadInt32(lParam);
				var y = Marshal.ReadInt32(lParam, 4);
				var choose = msg == WM_LBUTTONDOWN;
				Dispatcher.BeginInvoke(new Action(() => finishpick(x, y, choose)));
				return (IntPtr)1;
			}
		}
		return CallNextHookEx(hook, nCode, wParam, lParam);
	}

	void finishpick(int x, int y, bool choose) {
		endpick();
		if (!choose) {
			lbstat.Text = Loc.T("wintop.pick.cancel");
			syncframe();
			return;
		}
		var h = WinTop.RootAt(x, y);
		if (frame != null && h == frame.Handle) h = frame.Target;
		if (!WinTop.IsAlive(h)) {
			lbstat.Text = Loc.T("wintop.pick.miss");
			syncframe();
			return;
		}
		var found = false;
		foreach (var row in all) {
			if (row.Hwnd != h) continue;
			found = true;
			break;
		}
		if (!found) {
			try { all.Insert(0, WinTop.Describe(h)); }
			catch { }
		}
		efilter.Text = "";
		ehwnd.Text = "0x" + h.ToInt64().ToString("X");
		showlist(h);
		syncframe();
		try { Activate(); } catch { }
	}

	[StructLayout(LayoutKind.Sequential)]
	struct NativePoint {
		public int X;
		public int Y;
	}

	delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	static extern bool GetCursorPos(out NativePoint pt);

	[DllImport("user32.dll", SetLastError = true)]
	static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

	[DllImport("user32.dll")]
	static extern bool UnhookWindowsHookEx(IntPtr hhk);

	[DllImport("user32.dll")]
	static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	static extern IntPtr GetModuleHandle(string name);
}
