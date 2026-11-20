namespace ScreenKit;

/// <summary>性能小窗的参数：更新间隔、底色不透明度、进程排行开关。</summary>
public partial class PerfMonOptWindow : Window {
	readonly Action<int> apply;
	readonly Action<bool> applyproc;
	readonly Action<double> applyopacity;

	public PerfMonOptWindow(PerfOpt opt, Action<int> apply, Action<bool> applyproc, Action<double> applyopacity = null) {
		this.apply = apply;
		this.applyproc = applyproc;
		this.applyopacity = applyopacity;
		InitializeComponent();
		Title = Loc.T("perf.settings");
		lbhint.Text = Loc.T("perf.interval");
		lbalpha.Text = Loc.T("perf.opacity");
		cktopproc.Content = Loc.T("perf.topproc");
		var sec = PerfOpt.NormMs(opt.IntervalMs) / 1000.0;
		eslider.Value = sec;
		showval(sec);
		eslider.ValueChanged += (_, _) => {
			showval(eslider.Value);
			apply((int)Math.Round(eslider.Value * 1000));
		};
		var pct = PerfOpt.NormOpacity(opt.Opacity) * 100;
		oslider.Value = pct;
		showopacity(pct);
		oslider.ValueChanged += (_, _) => {
			showopacity(oslider.Value);
			applyopacity?.Invoke(PerfOpt.NormOpacity(oslider.Value / 100));
		};
		cktopproc.IsChecked = opt.TopProcs;
		cktopproc.Click += (_, _) => applyproc?.Invoke(cktopproc.IsChecked == true);
		WindowEsc.Attach(this);
	}

	void showval(double sec) {
		lbval.Text = $"{sec:0.0} s";
	}

	void showopacity(double pct) {
		lbopacity.Text = $"{pct:0}%";
	}
}
