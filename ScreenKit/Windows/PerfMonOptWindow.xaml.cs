namespace ScreenKit;

/// <summary>性能小窗的更新间隔。</summary>
public partial class PerfMonOptWindow : Window {
	readonly Action<int> apply;

	public PerfMonOptWindow(PerfOpt opt, Action<int> apply) {
		this.apply = apply;
		InitializeComponent();
		Title = Loc.T("perf.settings");
		lbhint.Text = Loc.T("perf.interval");
		var sec = PerfOpt.NormMs(opt.IntervalMs) / 1000.0;
		eslider.Value = sec;
		showval(sec);
		eslider.ValueChanged += (_, _) => {
			showval(eslider.Value);
			apply((int)Math.Round(eslider.Value * 1000));
		};
		WindowEsc.Attach(this);
	}

	void showval(double sec) {
		lbval.Text = $"{sec:0.0} s";
	}
}
