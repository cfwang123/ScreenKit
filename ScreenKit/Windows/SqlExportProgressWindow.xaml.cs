using System.Windows;

namespace ScreenKit;

/// <summary>
/// 导出进度：阶段 / 表序号 / 已写行数 / 百分比 / 取消。
/// 自身负责线程封送，导出线程可以直接调 <see cref="report"/>。
/// </summary>
public partial class SqlExportProgressWindow : Window, IProgress<SqlExportProgress> {
	readonly CancellationTokenSource cts = new();
	readonly int startTick = Environment.TickCount;
	int lastPct;
	long lastRows;
	bool allowClose;
	bool cancelClicked;

	public CancellationToken Token => cts.Token;
	public bool WasCancelled => cancelClicked || cts.IsCancellationRequested;

	public SqlExportProgressWindow() {
		InitializeComponent();
		applylang();
		WindowEsc.Attach(this, requestcancel);
	}

	void applylang() {
		Title = Loc.T("sql.export.progress.title");
		lbptitle.Text = Loc.T("sql.export.progress.label");
		bcancel.Content = Loc.T("cancel");
		lbphase.Text = Loc.T("sql.export.progress.starting");
		lbdetail.Text = "";
		lbpct.Text = "0%";
		pbar.Value = 0;
	}

	void oncancel(object sender, RoutedEventArgs e) => requestcancel();

	void requestcancel() {
		if (cancelClicked || cts.IsCancellationRequested) return;
		cancelClicked = true;
		try { cts.Cancel(); } catch { }
		bcancel.IsEnabled = false;
		bcancel.Content = Loc.T("sql.export.progress.cancelling");
		lbphase.Text = Loc.T("sql.export.progress.cancelling");
	}

	void onclosing(object sender, System.ComponentModel.CancelEventArgs e) {
		// 导出中点标题栏关闭 = 取消
		if (!allowClose) {
			requestcancel();
			e.Cancel = true;
		}
	}

	/// <summary>任务结束后允许关闭窗口。</summary>
	public void forceclose() {
		allowClose = true;
		try { Close(); } catch { }
	}

	/// <summary>更新进度；可从任意线程调用。</summary>
	void IProgress<SqlExportProgress>.Report(SqlExportProgress value) => report(value);

	/// <summary>更新进度；可从任意线程调用。</summary>
	internal void report(SqlExportProgress p) {
		if (p == null) return;
		if (!Dispatcher.CheckAccess()) {
			try { Dispatcher.BeginInvoke(() => report(p)); }
			catch { }
			return;
		}
		apply(p);
	}

	void apply(SqlExportProgress p) {
		var total = Math.Max(1, p.Total);
		var index = Compat.Clamp(p.Index, 1, total);
		lastRows = Math.Max(lastRows, p.Rows);

		// 每张表占一个等宽的槽，表内按行数在小槽里推进；总表数未知时按已完表数走
		var pct = phasepct(p, index, total);
		pct = Math.Max(lastPct, Compat.Clamp(pct, 0, 100));
		lastPct = pct;
		pbar.Value = pct;
		lbpct.Text = pct + "%";

		lbphase.Text = total > 1
			? Loc.T("sql.export.progress.table", index, total, string.IsNullOrEmpty(p.Table) ? "-" : p.Table)
			: Loc.T("sql.export.progress.single", string.IsNullOrEmpty(p.Table) ? "-" : p.Table);

		var sec = Math.Max(0, (Environment.TickCount - startTick) / 1000);
		var sb = new System.Text.StringBuilder();
		sb.Append(Loc.T("sql.export.progress.rows", lastRows));
		if (p.TableTotal > 0)
			sb.Append($" / {p.TableTotal}");
		sb.AppendLine();
		sb.Append(Loc.T("sql.export.progress.elapsed", formatelapsed(sec)));
		lbdetail.Text = sb.ToString().TrimEnd();
	}

	static int phasepct(SqlExportProgress p, int index, int total) {
		var per = 100.0 / total;
		var basepct = (index - 1) * per;
		// 行数未知时用渐近曲线：前 200 行推进最快，之后越来越慢，但始终在动
		var within = p.TableTotal > 0
			? Math.Min(1.0, p.TableRows / (double)p.TableTotal)
			: p.TableRows / (p.TableRows + 200.0);
		return p.Phase switch {
			SqlExportPhase.Schema => (int)basepct,
			SqlExportPhase.Data => (int)(basepct + per * within),
			SqlExportPhase.Index => (int)(basepct + per * 0.98),
			_ => (int)(basepct + per),
		};
	}

	public void done(bool ok, string text) {
		if (!Dispatcher.CheckAccess()) {
			try { Dispatcher.BeginInvoke(() => done(ok, text)); }
			catch { }
			return;
		}
		pbar.Value = ok ? 100 : lastPct;
		lbpct.Text = pbar.Value + "%";
		if (ok) {
			lbphase.Text = Loc.T("sql.export.progress.done");
			lbdetail.Text = text ?? "";
		}
		else {
			lbphase.Text = text ?? "";
			lbdetail.Text = "";
		}
	}

	static string formatelapsed(long sec) {
		if (sec < 0) sec = 0;
		var m = sec / 60;
		var s = sec % 60;
		return m > 0 ? $"{m}:{s:00}" : $"{s} s";
	}

	protected override void OnClosed(EventArgs e) {
		try { cts.Dispose(); } catch { }
		base.OnClosed(e);
	}
}
