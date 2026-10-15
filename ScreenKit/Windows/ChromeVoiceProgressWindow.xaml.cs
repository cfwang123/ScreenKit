using System.Windows;

namespace ScreenKit;

/// <summary>Chrome 离线语音包安装进度。可取消，取消会关掉这次打开的 Chrome。</summary>
public partial class ChromeVoiceProgressWindow : Window {
	readonly CancellationTokenSource cts;
	int lastPct;
	bool allowClose;
	bool cancelClicked;

	public CancellationToken Token => cts.Token;
	public bool WasCancelled => cancelClicked || cts.IsCancellationRequested;

	public ChromeVoiceProgressWindow() {
		InitializeComponent();
		cts = new CancellationTokenSource();
		Title = Loc.T("tts.chrome.voice.title");
		lbphase.Text = Loc.T("tts.chrome.voice.check");
		lbdetail.Text = "";
		lbbytes.Text = "";
		lbpct.Text = "";
		bcancel.Content = Loc.T("cancel");
		pbar.IsIndeterminate = true;
		WindowEsc.Attach(this, RequestCancel);
	}

	void oncancel(object sender, RoutedEventArgs e) => RequestCancel();

	public void RequestCancel() {
		if (!Dispatcher.CheckAccess()) {
			try { Dispatcher.BeginInvoke(new Action(RequestCancel)); }
			catch { }
			return;
		}
		if (allowClose || cancelClicked || cts.IsCancellationRequested) return;
		cancelClicked = true;
		try { cts.Cancel(); } catch { }
		try {
			bcancel.IsEnabled = false;
			bcancel.Content = Loc.T("update.cancelling");
			lbphase.Text = Loc.T("update.cancelling");
		}
		catch { }
	}

	void onclosing(object sender, System.ComponentModel.CancelEventArgs e) {
		if (!allowClose) {
			RequestCancel();
			e.Cancel = true;
		}
	}

	public void ForceClose() {
		allowClose = true;
		try { Close(); } catch { }
	}

	/// <summary>frac 小于 0 时进度条不显示百分比。可从任意线程调用。</summary>
	public void Report(string detail, double frac, long bytesDone = 0, long bytesTotal = 0) {
		if (!Dispatcher.CheckAccess()) {
			try {
				Dispatcher.BeginInvoke(new Action(() => Report(detail, frac, bytesDone, bytesTotal)));
			}
			catch { }
			return;
		}
		if (cancelClicked) return;
		if (!string.IsNullOrWhiteSpace(detail))
			lbphase.Text = detail;
		if (frac < 0) {
			pbar.IsIndeterminate = true;
			lbpct.Text = "";
		}
		else {
			pbar.IsIndeterminate = false;
			var raw = (int)Compat.Clamp(frac * 100.0, 0, 100);
			var pct = raw >= 100 ? 100 : Math.Max(lastPct, Compat.Clamp(raw, 0, 99));
			lastPct = pct;
			pbar.Value = pct;
			lbpct.Text = pct + "%";
		}
		if (bytesTotal > 0)
			lbbytes.Text = FeatureInstaller.FormatBytes(bytesDone) + " / " + FeatureInstaller.FormatBytes(bytesTotal);
		else if (bytesDone > 0)
			lbbytes.Text = FeatureInstaller.FormatBytes(bytesDone);
		else
			lbbytes.Text = "";
		lbdetail.Text = "";
	}

	protected override void OnClosed(EventArgs e) {
		try { cts.Dispose(); } catch { }
		base.OnClosed(e);
	}
}
