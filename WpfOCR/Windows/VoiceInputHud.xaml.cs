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
		runui(() => {
			lbtext.Text = string.IsNullOrWhiteSpace(text) ? "语音输入中…" : text;
			place();
		}, async: true);
	}

	/// <summary>第二行：阶段与内容同一行（如「润色中  原文」）。空则隐藏第二行。</summary>
	public void SetDetail(string phase, string content, bool async = true) {
		runui(() => paintbody(join(phase, content)), async);
	}

	/// <summary>润色中与原文同一行。须在 HTTP 前同步刷新，否则 UI 阻塞时浮窗不更新。</summary>
	public void SetPolish(string original) {
		runui(() => paintbody(join("润色中", original)), async: false);
	}

	static string join(string phase, string content) {
		phase = (phase ?? "").Trim();
		content = (content ?? "").Trim();
		if (phase.Length == 0) return content;
		if (content.Length == 0) return phase;
		return $"{phase}  {content}";
	}

	void paintbody(string line2) {
		if (string.IsNullOrWhiteSpace(line2)) {
			lbbody.Text = "";
			lbbody.Visibility = Visibility.Collapsed;
		}
		else {
			lbbody.Text = line2;
			lbbody.Visibility = Visibility.Visible;
		}
		UpdateLayout();
		place();
	}

	void runui(Action a, bool async) {
		if (!Dispatcher.CheckAccess()) {
			if (async) Dispatcher.BeginInvoke(a);
			else Dispatcher.Invoke(a);
			return;
		}
		a();
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
