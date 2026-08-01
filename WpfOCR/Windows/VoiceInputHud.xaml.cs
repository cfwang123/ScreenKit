using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace WpfOCR;

/// <summary>语音输入状态浮层：不抢焦点，显示在屏幕底部中央。</summary>
public partial class VoiceInputHud : Window {
	const int GWL_EXSTYLE = -20;
	const int WS_EX_NOACTIVATE = 0x08000000;
	const int WS_EX_TOOLWINDOW = 0x00000080;

	[DllImport("user32.dll")]
	static extern int GetWindowLong(IntPtr hWnd, int nIndex);

	[DllImport("user32.dll")]
	static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

	DispatcherTimer blink;
	bool lit = true;

	public VoiceInputHud() {
		InitializeComponent();
		SourceInitialized += (_, _) => {
			var hwnd = new WindowInteropHelper(this).Handle;
			if (hwnd == IntPtr.Zero) return;
			var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
			SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
		};
		Loaded += (_, _) => {
			place();
			blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
			blink.Tick += (_, _) => {
				lit = !lit;
				edot.Fill = new SolidColorBrush(lit ? Color.FromRgb(0xEF, 0x44, 0x44) : Color.FromRgb(0x7F, 0x1D, 0x1D));
			};
			blink.Start();
		};
		Closed += (_, _) => {
			try { blink?.Stop(); } catch { }
		};
	}

	public void SetStatus(string text) {
		if (!Dispatcher.CheckAccess()) {
			Dispatcher.BeginInvoke(new Action(() => SetStatus(text)));
			return;
		}
		lbtext.Text = string.IsNullOrWhiteSpace(text) ? "语音输入中…" : text;
	}

	void place() {
		try {
			// 主显示器工作区底部居中（DIP）
			var wa = SystemParameters.WorkArea;
			UpdateLayout();
			Left = wa.Left + (wa.Width - ActualWidth) / 2;
			Top = wa.Bottom - ActualHeight - 48;
		}
		catch { }
	}
}
