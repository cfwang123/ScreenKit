using System.Windows.Threading;

namespace ScreenKit;

/// <summary>无边框性能小窗。双列显示，右键开关每一项，图钉固定到前面。</summary>
public partial class PerfMonWindow : Window {
	readonly PerfOpt opt;
	readonly DispatcherTimer timer;
	readonly Dictionary<string, TextBlock> cells = new();
	readonly SolidColorBrush nameBrush = freeze("#9BB0B8");
	readonly SolidColorBrush okBrush = freeze("#3DDC84");
	readonly SolidColorBrush warnBrush = freeze("#F5C518");
	readonly SolidColorBrush hotBrush = freeze("#FF6B5A");
	readonly SolidColorBrush downBrush = freeze("#4CC2F0");
	readonly SolidColorBrush dimBrush = freeze("#7E9098");
	readonly SolidColorBrush pinOn = freeze("#40C070");
	readonly SolidColorBrush pinOff = freeze("#C5D0D6");
	const double BODY = 10;
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
		rows.RowDefinitions.Clear();
		rows.ColumnDefinitions.Clear();
		cells.Clear();
		rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
		rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
		rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
		var on = new List<string>();
		foreach (var key in ShowOrder)
			if (opt.On(key)) on.Add(key);
		for (var i = 0; i < on.Count; i++) {
			var row = i / 2;
			if (i % 2 == 0) rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			var col = i % 2 == 0 ? 0 : 3;
			var name = new TextBlock {
				Text = Loc.T($"perf.{on[i]}"),
				Foreground = nameBrush,
				FontSize = BODY,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 1, 8, 1),
			};
			var val = new TextBlock {
				Text = "—",
				Foreground = dimBrush,
				FontSize = BODY,
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 1, 0, 1),
			};
			Grid.SetRow(name, row);
			Grid.SetColumn(name, col);
			Grid.SetRow(val, row);
			Grid.SetColumn(val, col + 1);
			rows.Children.Add(name);
			rows.Children.Add(val);
			cells[on[i]] = val;
		}
	}

	void tick() {
		try { paint(PerfMon.Take()); }
		catch (Exception ex) { lbtitle.Text = ex.Message; }
	}

	void paint(PerfMon.Snap s) {
		set("cpu", pct(s.Cpu), pctc(s.Cpu));
		set("gpu", pct(s.Gpu), pctc(s.Gpu));
		set("mem", pct(s.Mem), pctc(s.Mem));
		set("down", rate(s.Down), downBrush);
		set("up", rate(s.Up), warnBrush);
		set("read", rate(s.Read), downBrush);
		set("write", rate(s.Write), warnBrush);
		set("cputemp", temp(s.CpuTemp), heat(s.CpuTemp));
		set("gputemp", temp(s.GpuTemp), heat(s.GpuTemp));
		set("gpupower", watt(s.GpuPower), downBrush);
		set("cpupower", watt(s.CpuPower), warnBrush);
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

	void set(string key, string text, SolidColorBrush brush) {
		if (!cells.TryGetValue(key, out var block)) return;
		block.Text = text;
		block.Foreground = text == "—" ? dimBrush : brush;
	}

	SolidColorBrush pctc(double v) {
		if (double.IsNaN(v)) return dimBrush;
		if (v < 50) return okBrush;
		if (v < 80) return warnBrush;
		return hotBrush;
	}

	SolidColorBrush heat(double v) {
		if (double.IsNaN(v)) return dimBrush;
		if (v < 55) return okBrush;
		if (v < 75) return warnBrush;
		return hotBrush;
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
