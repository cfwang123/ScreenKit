using System.Windows.Threading;

namespace ScreenKit;

/// <summary>半透明无边框性能小窗。右键开关每一项。</summary>
public partial class PerfMonWindow : Window {
	readonly PerfOpt opt;
	readonly DispatcherTimer timer;
	readonly Dictionary<string, TextBlock> cells = new();
	PerfMonOptWindow optWin;
	bool busy;
	bool placed;

	public PerfMonWindow() {
		opt = PerfOpt.Load(PerfOpt.DefaultPath);
		InitializeComponent();
		Title = Loc.T("perf.title");
		lbtitle.Text = Loc.T("perf.title");
		Topmost = opt.Topmost;
		buildmenu();
		buildrows();
		card.MouseLeftButtonDown += (_, _) => {
			try { DragMove(); } catch { }
		};
		card.MouseLeftButtonUp += (_, _) => savepos();
		ContentRendered += (_, _) => place();
		Closed += (_, _) => {
			timer.Stop();
			savepos();
		};
		WindowEsc.Attach(this);
		timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PerfOpt.NormMs(opt.IntervalMs)) };
		timer.Tick += (_, _) => tick();
		timer.Start();
		tick();
	}

	void place() {
		if (placed) return;
		placed = true;
		if (opt.HasPos) {
			Left = opt.Left;
			Top = opt.Top;
			return;
		}
		var area = SystemParameters.WorkArea;
		Left = Math.Max(area.Left, area.Right - ActualWidth - 16);
		Top = Math.Max(area.Top, area.Bottom - ActualHeight - 16);
	}

	void buildmenu() {
		var menu = new ContextMenu();
		foreach (var key in PerfOpt.Keys) {
			var item = new MenuItem {
				Header = Loc.T($"perf.{key}"),
				IsCheckable = true,
				IsChecked = opt.On(key),
				Tag = key,
			};
			item.Click += (_, _) => {
				opt.Set((string)item.Tag, item.IsChecked);
				opt.Save(PerfOpt.DefaultPath);
				buildrows();
			};
			menu.Items.Add(item);
		}
		menu.Items.Add(new Separator());
		var settings = new MenuItem { Header = Loc.T("perf.settings") };
		settings.Click += (_, _) => opensettings();
		menu.Items.Add(settings);
		var top = new MenuItem { Header = Loc.T("perf.top"), IsCheckable = true, IsChecked = opt.Topmost };
		top.Click += (_, _) => {
			opt.Topmost = top.IsChecked;
			Topmost = opt.Topmost;
			opt.Save(PerfOpt.DefaultPath);
		};
		menu.Items.Add(top);
		var close = new MenuItem { Header = Loc.T("perf.close") };
		close.Click += (_, _) => Close();
		menu.Items.Add(close);
		card.ContextMenu = menu;
	}

	void buildrows() {
		rows.Children.Clear();
		cells.Clear();
		foreach (var key in PerfOpt.Keys) {
			if (!opt.On(key)) continue;
			var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
			var name = new TextBlock {
				Text = Loc.T($"perf.{key}"),
				Foreground = ColorBrush("#C8FFFFFF"),
				FontSize = 13,
				VerticalAlignment = VerticalAlignment.Center,
			};
			var val = new TextBlock {
				Text = "—",
				Foreground = Brushes.White,
				FontSize = 13,
				FontWeight = FontWeights.SemiBold,
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Center,
			};
			Grid.SetColumn(val, 1);
			grid.Children.Add(name);
			grid.Children.Add(val);
			rows.Children.Add(grid);
			cells[key] = val;
		}
	}

	void tick() {
		if (busy) return;
		busy = true;
		Task.Run(() => {
			PerfMon.Snap snap = null;
			try { snap = PerfMon.Take(); }
			catch { }
			Dispatcher.BeginInvoke(new Action(() => {
				busy = false;
				if (snap != null) paint(snap);
			}));
		});
	}

	void paint(PerfMon.Snap s) {
		set("cpu", pct(s.Cpu));
		set("gpu", pct(s.Gpu));
		set("mem", pct(s.Mem));
		set("down", rate(s.Down));
		set("up", rate(s.Up));
		set("read", rate(s.Read));
		set("write", rate(s.Write));
		set("cputemp", temp(s.CpuTemp));
		set("gputemp", temp(s.GpuTemp));
		set("gpupower", watt(s.GpuPower));
		set("cpupower", watt(s.CpuPower));
	}

	void set(string key, string text) {
		if (cells.TryGetValue(key, out var block)) block.Text = text;
	}

	static string pct(double v) {
		if (double.IsNaN(v)) return "—";
		if (v < 0) v = 0;
		if (v > 100) v = 100;
		return $"{v:0}%";
	}

	static string rate(double v) {
		if (double.IsNaN(v) || v < 0) return "—";
		const double kb = 1024;
		const double mb = 1024 * 1024;
		const double gb = 1024 * 1024 * 1024;
		if (v >= gb) return $"{v / gb:0.0} GB/s";
		if (v >= mb) return $"{v / mb:0.0} MB/s";
		if (v >= kb) return $"{v / kb:0} KB/s";
		return $"{v:0} B/s";
	}

	static string temp(double v) {
		if (double.IsNaN(v)) return "—";
		return $"{v:0}°C";
	}

	static string watt(double v) {
		if (double.IsNaN(v)) return "—";
		if (v >= 10) return $"{v:0} W";
		return $"{v:0.0} W";
	}

	static SolidColorBrush ColorBrush(string hex) {
		var c = (Color)ColorConverter.ConvertFromString(hex);
		var b = new SolidColorBrush(c);
		b.Freeze();
		return b;
	}

	void opensettings() {
		if (optWin != null) {
			optWin.Activate();
			return;
		}
		optWin = new PerfMonOptWindow(opt, setinterval) { Owner = this, Topmost = Topmost };
		optWin.Closed += (_, _) => optWin = null;
		optWin.Show();
	}

	void setinterval(int ms) {
		opt.IntervalMs = PerfOpt.NormMs(ms);
		timer.Interval = TimeSpan.FromMilliseconds(opt.IntervalMs);
		opt.Save(PerfOpt.DefaultPath);
	}

	void savepos() {
		if (!placed) return;
		opt.HasPos = true;
		opt.Left = Left;
		opt.Top = Top;
		opt.Save(PerfOpt.DefaultPath);
	}
}
