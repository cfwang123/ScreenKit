using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ScreenKit;

/// <summary>手机首次配对确认：无 Owner、始终置顶，主窗在托盘时也能点到。</summary>
public partial class PairAskWindow : Window {
	static readonly IntPtr HwndTopmost = new(-1);
	const uint SWP_NOSIZE = 0x0001;
	const uint SWP_NOMOVE = 0x0002;
	const uint SWP_SHOWWINDOW = 0x0040;

	public PairAskWindow(string name, string ip) {
		InitializeComponent();
		Title = Loc.T("sendfile.pair.title");
		lbask.Text = Loc.T("sendfile.pair.ask", name ?? "", ip ?? "");
		byes.Content = Loc.T("sendfile.pair.yes");
		bno.Content = Loc.T("sendfile.pair.no");
		byes.Click += (_, _) => { DialogResult = true; };
		bno.Click += (_, _) => { DialogResult = false; };
		SourceInitialized += (_, _) => tofront();
		Loaded += (_, _) => tofront();
		ContentRendered += (_, _) => tofront();
		Activated += (_, _) => pin();
	}

	/// <summary>须在 UI 线程调用。允许返回 true。</summary>
	public static bool Ask(string name, string ip) {
		var w = new PairAskWindow(name, ip);
		return w.ShowDialog() == true;
	}

	void tofront() {
		try {
			Activate();
			Focus();
			pin();
			var hwnd = new WindowInteropHelper(this).Handle;
			if (hwnd != IntPtr.Zero)
				SetForegroundWindow(hwnd);
		}
		catch { }
	}

	void pin() {
		try {
			Topmost = true;
			var hwnd = new WindowInteropHelper(this).Handle;
			if (hwnd == IntPtr.Zero) return;
			SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
		}
		catch { }
	}

	[DllImport("user32.dll")]
	static extern bool SetForegroundWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
