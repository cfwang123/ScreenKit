using System.Windows;

namespace WpfOCR;

/// <summary>检查更新 / 下载进度窗（阶段、百分比、字节、取消）。</summary>
public partial class UpdateProgressWindow : Window {
	readonly CancellationTokenSource cts;
	int lastPct;
	bool allowClose;
	bool cancelClicked;

	public CancellationToken Token => cts.Token;
	public bool WasCancelled => cancelClicked || cts.IsCancellationRequested;

	public UpdateProgressWindow() {
		InitializeComponent();
		cts = new CancellationTokenSource();
		lastPct = 0;
		lbphase.Text = Loc.T("update.checking");
		lbdetail.Text = "";
		lbbytes.Text = "";
		lbpct.Text = "";
		pbar.Value = 0;
		pbar.IsIndeterminate = true;
		bcancel.Content = Loc.T("cancel");
		Title = Loc.T("update.title");
		WindowEsc.Attach(this, requestcancel);
	}

	void oncancel(object sender, RoutedEventArgs e) => requestcancel();

	void requestcancel() {
		if (cancelClicked || cts.IsCancellationRequested) return;
		cancelClicked = true;
		try { cts.Cancel(); } catch { }
		bcancel.IsEnabled = false;
		bcancel.Content = Loc.T("update.cancelling");
		lbphase.Text = Loc.T("update.cancelling");
	}

	void onclosing(object sender, System.ComponentModel.CancelEventArgs e) {
		if (!allowClose) {
			requestcancel();
			e.Cancel = true;
		}
	}

	public void ForceClose() {
		allowClose = true;
		try { Close(); } catch { }
	}

	/// <summary>
	/// 更新进度。可从任意线程调用。
	/// phase: check | download | prepare | done | error
	/// </summary>
	public void Report(string phase, double overall01, string detail = null,
		long bytesDone = 0, long bytesTotal = 0, string fileName = null) {
		if (!Dispatcher.CheckAccess()) {
			try {
				Dispatcher.BeginInvoke(new Action(() =>
					Report(phase, overall01, detail, bytesDone, bytesTotal, fileName)));
			}
			catch { }
			return;
		}
		apply(phase, overall01, detail, bytesDone, bytesTotal, fileName);
	}

	/// <summary>对接 InstallProgress。</summary>
	internal void ReportInstall(InstallProgress p) {
		if (p == null) return;
		Report("download", p.Overall, p.Note, p.BytesDone, p.BytesTotal, p.FileName);
	}

	void apply(string phase, double overall01, string detail,
		long bytesDone, long bytesTotal, string fileName) {
		phase = (phase ?? "check").ToLowerInvariant();
		if (phase is "check" or "prepare") {
			pbar.IsIndeterminate = true;
			lbpct.Text = "";
		}
		else if (overall01 >= 0) {
			pbar.IsIndeterminate = false;
			var raw = (int)Compat.Clamp(overall01 * 100.0, 0, 100);
			var pct = phase == "done" ? 100 : Math.Max(lastPct, Compat.Clamp(raw, 0, 99));
			if (phase == "done") pct = 100;
			lastPct = pct;
			pbar.Value = pct;
			lbpct.Text = pct + "%";
		}

		lbphase.Text = phase switch {
			"check" => Loc.T("update.checking"),
			"download" => Loc.T("update.downloading"),
			"prepare" => Loc.T("update.preparing"),
			"done" => Loc.T("update.done"),
			"error" => Loc.T("update.error"),
			"cancel" => Loc.T("update.cancelling"),
			_ => Loc.T("update.working"),
		};

		if (bytesTotal > 0 || bytesDone > 0)
			lbbytes.Text = formatbytes(bytesDone, bytesTotal);
		else
			lbbytes.Text = "";

		var sb = new System.Text.StringBuilder();
		if (!string.IsNullOrWhiteSpace(fileName))
			sb.AppendLine(fileName);
		if (!string.IsNullOrWhiteSpace(detail))
			sb.Append(detail.Trim());
		lbdetail.Text = sb.ToString().TrimEnd();
	}

	static string formatbytes(long done, long total) {
		if (total > 0)
			return FeatureInstaller.FormatBytes(done) + " / " + FeatureInstaller.FormatBytes(total);
		if (done > 0)
			return FeatureInstaller.FormatBytes(done);
		return "";
	}

	protected override void OnClosed(EventArgs e) {
		try { cts.Dispose(); } catch { }
		base.OnClosed(e);
	}
}
