using System.Windows;

namespace WpfOCR;

/// <summary>TTS 导出进度（对齐安卓 TtsExportProgressDialog：阶段 / 段数字数 / 百分比 / 取消）。</summary>
public partial class TtsExportProgressWindow : Window {
	readonly CancellationTokenSource cts;
	readonly int startTick;
	int lastPct;
	bool allowClose;
	bool cancelClicked;

	public CancellationToken Token => cts.Token;
	public bool WasCancelled => cancelClicked || cts.IsCancellationRequested;

	public TtsExportProgressWindow() {
		InitializeComponent();
		cts = new CancellationTokenSource();
		startTick = Environment.TickCount;
		lastPct = 0;
		lbphase.Text = "正在准备朗读引擎…";
		lbdetail.Text = "";
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
		// 合成中点标题栏关闭 = 取消
		if (!allowClose) {
			requestcancel();
			e.Cancel = true;
		}
	}

	/// <summary>任务结束后允许关闭窗口。</summary>
	public void ForceClose() {
		allowClose = true;
		try { Close(); } catch { }
	}

	/// <summary>
	/// 更新进度。可从任意线程调用。
	/// phase: prepare | synth | merge | encode
	/// </summary>
	public void Report(string phase, int doneParts, int totalParts, int doneChars = 0, int totalChars = 0, float partFraction = 0f) {
		if (!Dispatcher.CheckAccess()) {
			try {
				Dispatcher.BeginInvoke(() =>
					Report(phase, doneParts, totalParts, doneChars, totalChars, partFraction));
			}
			catch { }
			return;
		}
		apply(phase, doneParts, totalParts, doneChars, totalChars, partFraction);
	}

	void apply(string phase, int doneParts, int totalParts, int doneChars, int totalChars, float partFraction) {
		var t = Math.Max(1, totalParts);
		var d = Compat.Clamp(doneParts, 0, t);
		var frac = Compat.Clamp(partFraction, 0f, 1f);
		phase = (phase ?? "synth").ToLowerInvariant();

		// 进度 0–100：合成 0–92，合并 93–96，编码 97–99；只增不减
		int rawPct;
		if (phase is "prepare" or "init")
			rawPct = 1;
		else if (phase == "merge")
			rawPct = 94;
		else if (phase == "encode")
			rawPct = 98;
		else if (phase == "done")
			rawPct = 100;
		else {
			double byChars;
			if (totalChars > 0)
				byChars = doneChars / (double)totalChars * 92.0;
			else {
				var bas = d / (double)t * 92.0;
				var within = d < t ? frac * (92.0 / t) : 0;
				byChars = bas + within;
			}
			rawPct = (int)Compat.Clamp(byChars, 0, 92);
		}
		var pct = Math.Max(lastPct, Compat.Clamp(rawPct, 0, 100));
		if (phase == "done") pct = 100;
		lastPct = pct;
		pbar.Value = pct;
		lbpct.Text = pct + "%";

		var currentPart = phase != "synth" ? t
			: d >= t ? t
			: Math.Min(d + 1, t);

		lbphase.Text = phase switch {
			"prepare" or "init" => "正在准备朗读引擎…",
			"merge" => "正在合并音频…",
			"encode" => "正在编码输出文件…",
			"done" => "完成",
			_ => $"合成中：第 {currentPart} / {t} 段",
		};

		var elapsedSec = Math.Max(0, (Environment.TickCount - startTick) / 1000);
		var charsShow = totalChars > 0
			? Compat.Clamp(doneChars, 0, Math.Max(totalChars, doneChars))
			: doneChars;
		var sb = new System.Text.StringBuilder();
		if (totalChars > 0)
			sb.AppendLine($"字数：{charsShow} / {totalChars}");
		if (phase is "synth" or "prepare" || phase == "init") {
			sb.Append($"分段：{currentPart} / {t}");
			if (frac > 0.01f && d < t)
				sb.Append($" · {(int)Compat.Clamp(frac * 100, 0, 99)}%");
			sb.AppendLine();
		}
		sb.Append($"已用时 {formatelapsed(elapsedSec)}");
		if (phase == "synth" && charsShow > 0 && totalChars > charsShow && elapsedSec >= 2) {
			var remain = estimateremain(charsShow, totalChars, elapsedSec);
			if (remain > 0)
				sb.Append($" · 约剩 {formatelapsed(remain)}");
		}
		lbdetail.Text = sb.ToString().TrimEnd();
	}

	static long estimateremain(int done, int total, long elapsedSec) {
		if (done <= 0 || elapsedSec <= 0 || total <= done) return 0;
		var per = elapsedSec / (double)done;
		return Math.Max(0, (long)((total - done) * per));
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
