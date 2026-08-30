using System.Windows;

namespace ScreenKit;

/// <summary>视频/音频 → SRT 进度（阶段 / 句数 / 字数 / 时轴 / 最新一句 / 取消）。</summary>
public partial class AsrSrtProgressWindow : Window {
	readonly CancellationTokenSource cts;
	readonly int startTick;
	int lastPct;
	bool allowClose;
	bool cancelClicked;

	public CancellationToken Token => cts.Token;
	public bool WasCancelled => cancelClicked || cts.IsCancellationRequested;

	public AsrSrtProgressWindow() {
		InitializeComponent();
		cts = new CancellationTokenSource();
		startTick = Environment.TickCount;
		lastPct = 0;
		lbphase.Text = "正在准备识别引擎…";
		lbdetail.Text = "";
		lblatest.Text = "最新：—";
		lbpos.Text = "进度 —";
		lbpct.Text = "0%";
		pbar.Value = 0;
		WindowEsc.Attach(this, requestcancel);
	}

	void oncancel(object sender, RoutedEventArgs e) => requestcancel();

	void requestcancel() {
		if (cancelClicked || cts.IsCancellationRequested) return;
		cancelClicked = true;
		try { cts.Cancel(); } catch { }
		bcancel.IsEnabled = false;
		bcancel.Content = "取消中…";
		lbphase.Text = "正在取消…";
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
	/// phase: prepare | recognize | save | done | cancel
	/// </summary>
	public void Report(string phase, double posSec, double totalSec,
		int cueCount = 0, int charCount = 0, string latestCue = null) {
		if (!Dispatcher.CheckAccess()) {
			try {
				Dispatcher.BeginInvoke(new Action(() =>
					Report(phase, posSec, totalSec, cueCount, charCount, latestCue)));
			}
			catch { }
			return;
		}
		apply(phase, posSec, totalSec, cueCount, charCount, latestCue);
	}

	void apply(string phase, double posSec, double totalSec,
		int cueCount, int charCount, string latestCue) {
		phase = (phase ?? "recognize").ToLowerInvariant();
		if (totalSec < 0) totalSec = 0;
		if (posSec < 0) posSec = 0;
		if (posSec > totalSec && totalSec > 0) posSec = totalSec;

		int rawPct;
		if (phase is "prepare" or "init" or "load")
			rawPct = 1;
		else if (phase == "save")
			rawPct = 97;
		else if (phase == "done")
			rawPct = 100;
		else if (phase == "cancel")
			rawPct = lastPct;
		else {
			// recognize: 2–95 by audio position
			if (totalSec <= 0.01)
				rawPct = 50;
			else
				rawPct = (int)Compat.Clamp(2 + posSec / totalSec * 93.0, 2, 95);
		}
		var pct = Math.Max(lastPct, Compat.Clamp(rawPct, 0, 100));
		if (phase == "done") pct = 100;
		lastPct = pct;
		pbar.Value = pct;
		lbpct.Text = pct + "%";

		lbphase.Text = phase switch {
			"prepare" or "init" or "load" => "正在加载识别模型…",
			"save" => "正在写入 SRT 文件…",
			"done" => "字幕生成完成",
			"cancel" => "正在取消…",
			_ => "正在识别并生成字幕…",
		};

		lbpos.Text = totalSec > 0
			? $"进度 {AsrSrt.FormatTs(posSec)} / {AsrSrt.FormatTs(totalSec)}"
			: "进度 —";

		var elapsedSec = Math.Max(0, (Environment.TickCount - startTick) / 1000);
		var sb = new System.Text.StringBuilder();
		sb.AppendLine($"已生成 {cueCount} 句 · {charCount} 字");
		sb.Append($"已用时 {formatelapsed(elapsedSec)}");
		if ((phase is "recognize" or "synth") && totalSec > posSec + 0.5 && posSec > 0.5 && elapsedSec >= 2) {
			var remain = estimateremain(posSec, totalSec, elapsedSec);
			if (remain > 0)
				sb.Append($" · 约剩 {formatelapsed(remain)}");
		}
		lbdetail.Text = sb.ToString().TrimEnd();

		if (!string.IsNullOrWhiteSpace(latestCue)) {
			var t = latestCue.Trim();
			if (t.Length > 80) t = t.Substring(0, 80) + "…";
			lblatest.Text = "最新：" + t;
		}
	}

	static long estimateremain(double doneSec, double totalSec, long elapsedSec) {
		if (doneSec <= 0 || elapsedSec <= 0 || totalSec <= doneSec) return 0;
		var per = elapsedSec / doneSec;
		return Math.Max(0, (long)((totalSec - doneSec) * per));
	}

	static string formatelapsed(long sec) {
		if (sec < 0) sec = 0;
		var m = sec / 60;
		var s = sec % 60;
		return m > 0 ? $"{m}:{s:00}" : $"{s} 秒";
	}

	protected override void OnClosed(EventArgs e) {
		try { cts.Dispose(); } catch { }
		base.OnClosed(e);
	}
}
