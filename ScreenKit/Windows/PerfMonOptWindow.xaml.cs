namespace ScreenKit;

/// <summary>性能小窗的参数：更新间隔、进程排行开关。</summary>
public partial class PerfMonOptWindow : Window {
	readonly Action<int> apply;
	readonly Action<bool> applyproc;

	public PerfMonOptWindow(PerfOpt opt, Action<int> apply, Action<bool> applyproc) {
		this.apply = apply;
		this.applyproc = applyproc;
		InitializeComponent();
		Title = Loc.T("perf.settings");
		lbhint.Text = Loc.T("perf.interval");
		cktopproc.Content = Loc.T("perf.topproc");
		var sec = PerfOpt.NormMs(opt.IntervalMs) / 1000.0;
		eslider.Value = sec;
		showval(sec);
		eslider.ValueChanged += (_, _) => {
			showval(eslider.Value);
			apply((int)Math.Round(eslider.Value * 1000));
		};
		cktopproc.IsChecked = opt.TopProcs;
		cktopproc.Click += (_, _) => applyproc?.Invoke(cktopproc.IsChecked == true);
		WindowEsc.Attach(this);
	}

	void showval(double sec) {
		lbval.Text = $"{sec:0.0} s";
	}
}
