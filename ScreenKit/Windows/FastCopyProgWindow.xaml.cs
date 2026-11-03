namespace ScreenKit;

/// <summary>快速复制的进度窗。复制、剪切、删除和统计大小都走这里，可取消。</summary>
public partial class FastCopyProgWindow : Window {
	readonly FastCopy.Job job;
	readonly IList<string> paths;
	readonly bool filesonly;
	readonly bool withbytes;
	CancellationTokenSource cts;
	bool dead;
	bool done;

	public int Code { get; private set; } = 1;
	public int Ok { get; private set; }
	public int Failed { get; private set; }
	public FastCopy.Stats Stats { get; private set; }

	public FastCopyProgWindow(FastCopy.Job job) {
		this.job = job ?? new FastCopy.Job();
		InitializeComponent();
		Title = Loc.T("fc.title");
		withbytes = string.IsNullOrEmpty(this.job.Link) && string.IsNullOrEmpty(this.job.Delete);
		bcancel.Content = Loc.T("fc.cancel");
		lbfile.Text = this.job.Dest ?? "";
		wire();
	}

	/// <summary>只统计，不复制。filesonly 时只报文件数量和大小。</summary>
	public FastCopyProgWindow(IList<string> paths, bool filesonly) {
		this.paths = paths ?? new List<string>();
		this.filesonly = filesonly;
		InitializeComponent();
		Title = Loc.T("fc.title");
		bcancel.Content = Loc.T("fc.cancel");
		lbfile.Text = Loc.T("fc.stat.scan");
		wire();
	}

	void wire() {
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
		if (job != null) {
			job.Cancel = cts.Token;
			runcopy();
			return;
		}
		int nextui = Environment.TickCount - 1000;
		Task.Run(() => FastCopy.Count(paths, cts.Token, (st, path) => {
			var now = Environment.TickCount;
			if (unchecked(now - nextui) < 80) return;
			nextui = now;
			if (dead) return;
			var shot = new FastCopy.Stats { Files = st.Files, Dirs = st.Dirs, Bytes = st.Bytes };
			Dispatcher.BeginInvoke(new Action(() => showscan(shot, path)));
		})).ContinueWith(t => {
			FastCopy.Stats st = null;
			var cancelled = t.IsCanceled || t.Exception?.InnerException is OperationCanceledException;
			if (t.Status == TaskStatus.RanToCompletion) st = t.Result;
			Dispatcher.BeginInvoke(new Action(() => finishscan(st, cancelled)));
		});
	}

	void runcopy() {
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

	void showscan(FastCopy.Stats st, string path) {
		if (dead) return;
		if (!string.IsNullOrEmpty(path)) lbfile.Text = path;
		lbtotal.Text = Loc.T("fc.prog.total", st.Files);
		lbdone.Text = filesonly ? "" : Loc.T("fc.prog.dirs", st.Dirs);
		lbtotalsize.Text = Loc.T("fc.prog.totalsize", FastCopy.FormatBytes(st.Bytes));
		lbdonesize.Text = "";
		bar.IsIndeterminate = true;
	}

	void show(FastCopy.Progress p) {
		if (dead) return;
		lbfile.Text = string.IsNullOrEmpty(p.File) ? job.Dest : p.File;
		if (p.Scanning) {
			lbtotal.Text = Loc.T("fc.stat.scan");
			lbdone.Text = "";
			lbtotalsize.Text = "";
			lbdonesize.Text = "";
			bar.IsIndeterminate = true;
			return;
		}
		lbtotal.Text = Loc.T("fc.prog.total", p.TotalFiles);
		lbdone.Text = Loc.T("fc.prog.done", p.DoneFiles);
		if (withbytes) {
			lbtotalsize.Text = Loc.T("fc.prog.totalsize", FastCopy.FormatBytes(p.TotalBytes));
			lbdonesize.Text = Loc.T("fc.prog.donesize", FastCopy.FormatBytes(p.DoneBytes));
		}
		else {
			lbtotalsize.Text = "";
			lbdonesize.Text = "";
		}
		if (p.TotalBytes > 0) {
			bar.IsIndeterminate = false;
			var pct = p.DoneBytes * 100.0 / p.TotalBytes;
			if (pct < 0) pct = 0;
			if (pct > 100) pct = 100;
			bar.Value = pct;
		}
		else bar.IsIndeterminate = p.TotalFiles == 0 || p.DoneFiles < p.TotalFiles;
	}

	void finishscan(FastCopy.Stats st, bool cancelled) {
		done = true;
		try { cts?.Dispose(); } catch { }
		if (cancelled || st == null) {
			Code = 3;
			bcancel.Content = Loc.T("fc.close");
			bar.IsIndeterminate = false;
			lbtotal.Text = Loc.T("fc.cancelled");
			return;
		}
		Stats = st;
		Code = 0;
		Close();
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
		if (res != null && res.Cancelled) lbdonesize.Text = Loc.T("fc.cancelled");
		else if (res != null) lbdonesize.Text = Loc.T("fc.done", res.Ok, res.Skipped, res.Failed)
			+ (string.IsNullOrEmpty(res.Error) ? "" : "  " + res.Error);
	}
}
