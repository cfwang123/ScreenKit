using System.Windows.Threading;

namespace ScreenKit;

/// <summary>无边框性能小窗。双列显示，右键开关每一项，图钉固定到前面。</summary>
public partial class PerfMonWindow : Window {
	readonly PerfOpt opt;
	readonly DispatcherTimer timer;
	readonly Dictionary<string, TextBlock> cells = new();
	readonly SolidColorBrush nameBrush = freeze("#E4EAEF");
	readonly SolidColorBrush valueBrush = freeze("#F8F8F8");
	readonly SolidColorBrush pinOn = freeze("#40C070");
	readonly SolidColorBrush pinOff = freeze("#C5D0D6");
	static readonly string[] ShowOrder = {
		"cpu", "gpu", "mem", "cputemp", "down", "up", "read", "write", "gputemp", "gpupower", "cpupower",
	};
	PerfMonOptWindow optWin;
	MenuItem topmi;
	bool placed;

	public PerfMonWindow() {
		opt = PerfOpt.Load(PerfOpt.DefaultPath);
		InitializeComponent();
		Title = Loc.T("perf.title");
		lbtitle.Text = Loc.T("perf.title");
		Topmost = opt.Topmost;
		btop.ToolTip = Loc.T("perf.top");
		paintpin();
		buildmenu();
		buildrows();
		card.MouseLeftButtonDown += (_, e) => {
			if (under(btop, e.OriginalSource as DependencyObject)) return;
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
		topmi = new MenuItem { Header = Loc.T("perf.top"), IsCheckable = true, IsChecked = opt.Topmost };
		topmi.Click += (_, _) => settop(topmi.IsChecked);
		menu.Items.Add(topmi);
		var close = new MenuItem { Header = Loc.T("perf.close") };
		close.Click += (_, _) => Close();
		menu.Items.Add(close);
		card.ContextMenu = menu;
	}

	void btop_Click(object sender, RoutedEventArgs e) {
		settop(!opt.Topmost);
	}

	void settop(bool on) {
		opt.Topmost = on;
		Topmost = on;
		if (topmi != null) topmi.IsChecked = on;
		paintpin();
		opt.Save(PerfOpt.DefaultPath);
	}

	void paintpin() {
		lbtop.Foreground = opt.Topmost ? pinOn : pinOff;
	}

	static bool under(DependencyObject root, DependencyObject node) {
		while (node != null) {
			if (node == root) return true;
			node = VisualTreeHelper.GetParent(node);
		}
		return false;
	}

	void buildrows() {
		rows.Children.Clear();
		cells.Clear();
		foreach (var key in ShowOrder) {
			if (!opt.On(key)) continue;
			var grid = new Grid { Margin = new Thickness(0, 2, 16, 2) };
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
			var name = new TextBlock {
				Text = Loc.T($"perf.{key}"),
				Foreground = nameBrush,
				FontSize = 13,
				VerticalAlignment = VerticalAlignment.Center,
			};
			var val = new TextBlock {
				Text = "—",
				Foreground = valueBrush,
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
		try { paint(PerfMon.Take()); }
		catch (Exception ex) { lbtitle.Text = ex.Message; }
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

	public string Shown(string key) => cells.TryGetValue(key, out var block) ? block.Text ?? "" : "";

	public static int Check(Action<string> log, Action<string> err) {
		var win = new PerfMonWindow();
		win.Show();
		var frame = new DispatcherFrame();
		var pump = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
		var n = 0;
		pump.Tick += (_, _) => {
			n++;
			if (n >= 50) frame.Continue = false;
		};
		pump.Start();
		Dispatcher.PushFrame(frame);
		pump.Stop();
		var cpu = win.Shown("cpu");
		var mem = win.Shown("mem");
		log($"win cpu={cpu} mem={mem}");
		win.Close();
		if (cpu.Length == 0 || cpu == "—") {
			err("窗口没有显示 CPU");
			return 1;
		}
		if (mem.Length == 0 || mem == "—") {
			err("窗口没有显示内存");
			return 1;
		}
		return 0;
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
		if (v >= gb) return $"{v / gb:0.0}G/s";
		if (v >= mb) return $"{v / mb:0.0}M/s";
		if (v >= kb) return $"{v / kb:0}K/s";
		return $"{v:0}B/s";
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

	static SolidColorBrush freeze(string hex) {
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
