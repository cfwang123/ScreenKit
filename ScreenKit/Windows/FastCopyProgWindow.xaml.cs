namespace ScreenKit;

/// <summary>资源管理器「快速粘贴」的进度窗。</summary>
public partial class FastCopyProgWindow : Window {
	readonly FastCopy.Job job;
	CancellationTokenSource cts;
	bool dead;
	bool done;

	public int Code { get; private set; } = 1;
	public int Ok { get; private set; }
	public int Failed { get; private set; }

	public FastCopyProgWindow(FastCopy.Job job) {
		this.job = job ?? new FastCopy.Job();
		InitializeComponent();
		Title = Loc.T("fc.title");
		bcancel.Content = Loc.T("fc.cancel");
		lbfile.Text = job?.Dest ?? "";
		WindowEsc.Attach(this, () => bcancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
		bcancel.Click += (_, _) => {
			if (done) { Close(); return; }
			try { cts?.Cancel(); } catch { }
		};
		Closing += (_, e) => {
			if (!done) {
				e.Cancel = true;
				try { cts?.Cancel(); } catch { }
			}
			else dead = true;
		};
		Loaded += (_, _) => start();
	}

	void start() {
		cts = new CancellationTokenSource();
		job.Cancel = cts.Token;
		int nextui = Environment.TickCount - 1000;
		job.OnProgress = p => {
			var now = Environment.TickCount;
			if (unchecked(now - nextui) < 80 && p.DoneFiles < p.TotalFiles) return;
			nextui = now;
			if (dead) return;
			Dispatcher.BeginInvoke(new Action(() => show(p)));
		};
		Task.Run(() => FastCopy.Run(job)).ContinueWith(t => {
			FastCopy.Result res = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
			Dispatcher.BeginInvoke(new Action(() => finish(res)));
		});
	}

	void show(FastCopy.Progress p) {
		if (dead) return;
		lbfile.Text = string.IsNullOrEmpty(p.File) ? job.Dest : p.File;
		lbstat.Text = $"{p.DoneFiles}/{p.TotalFiles}";
		if (p.TotalBytes > 0) {
			bar.IsIndeterminate = false;
			var pct = p.DoneBytes * 100.0 / p.TotalBytes;
			if (pct < 0) pct = 0;
			if (pct > 100) pct = 100;
			bar.Value = pct;
		}
		else bar.IsIndeterminate = true;
	}

	void finish(FastCopy.Result res) {
		done = true;
		Ok = res?.Ok ?? 0;
		Failed = res?.Failed ?? 1;
		if (res == null || res.Failed > 0) Code = 1;
		else if (res.Cancelled) Code = 3;
		else Code = 0;
		try { cts?.Dispose(); } catch { }
		if (Code == 0) {
			Close();
			return;
		}
		bcancel.Content = Loc.T("fc.close");
		bar.IsIndeterminate = false;
		if (res != null && res.Cancelled) lbstat.Text = Loc.T("fc.cancelled");
		else if (res != null) lbstat.Text = Loc.T("fc.done", res.Ok, res.Skipped, res.Failed)
			+ (string.IsNullOrEmpty(res.Error) ? "" : "  " + res.Error);
	}
}
