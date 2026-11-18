using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>无边框性能小窗。双列显示，右键开关每一项，图钉固定到前面；下方 tab 看进程占用排行。</summary>
public partial class PerfMonWindow : Window {
	readonly PerfOpt opt;
	readonly DispatcherTimer timer;
	readonly DispatcherTimer proctimer;
	readonly Dictionary<string, TextBlock> cells = new();
	readonly Dictionary<string, PerfSpark> sparks = new();
	readonly Dictionary<string, List<(int tick, double v)>> history = new();
	const int SPAN_MS = 120000;
	const double SPARK_H = 12;
	// 数值列与右边界之间留一个空格的宽度
	const double SPACEW = 7;
	const double RESIZE_EDGE = 6;
	const int E_LEFT = 1, E_TOP = 2, E_RIGHT = 4, E_BOTTOM = 8;
	bool resizing;
	int resizeEdge;
	Point resizeFrom;
	double resizeL, resizeT, resizeW, resizeH;
	readonly SolidColorBrush nameBrush = freeze("#9BB0B8");
	readonly SolidColorBrush okBrush = freeze("#3DDC84");
	readonly SolidColorBrush warnBrush = freeze("#F5C518");
	readonly SolidColorBrush hotBrush = freeze("#FF6B5A");
	readonly SolidColorBrush downBrush = freeze("#4CC2F0");
	readonly SolidColorBrush dimBrush = freeze("#7E9098");
	readonly SolidColorBrush headBrush = freeze("#C5D0D6");
	readonly SolidColorBrush gridBrush = freeze("#33455560");
	readonly SolidColorBrush pinOn = freeze("#40C070");
	readonly SolidColorBrush pinOff = freeze("#C5D0D6");
	const double BODY = 10;
	const double SMALL = 9;
	static readonly string[] ShowOrder = {
		"cpu", "gpu", "mem", "cputemp", "down", "up", "read", "write", "gputemp", "gpupower", "cpupower",
	};
	static readonly string[] TabKeys = { "tcpu", "tmem", "tdisk", "tnet" };
	readonly Dictionary<string, TextBlock[,]> tabcells = new();
	readonly Dictionary<string, TextBlock> tabnote = new();
	readonly Dictionary<string, bool> tabbuilt = new();
	PerfMonOptWindow optWin;
	ProcTop procs;
	MenuItem topmi;
	MenuItem procmi;
	bool placed;

	public PerfMonWindow() {
		opt = PerfOpt.Load();
		InitializeComponent();
		Title = Loc.T("perf.title");
		lbtitle.Text = Loc.T("perf.title");
		Topmost = opt.Topmost;
		btop.ToolTip = Loc.T("perf.top");
		paintpin();
		buildmenu();
		buildrows();
		buildtabs();
		card.MouseLeftButtonDown += (_, e) => {
			var src = e.OriginalSource as DependencyObject;
			if (under(btop, src) || under(tabs, src) || under(psec, src)) return;
			var edge = edgeat(e);
			if (edge != 0) {
				startresize(edge, e);
				return;
			}
			if (under(grip, src)) return;
			try { DragMove(); } catch { }
		};
		card.MouseMove += (_, e) => onresizemove(e);
		card.MouseLeftButtonUp += (_, _) => {
			endresize();
			savepos();
		};
		card.MouseLeave += (_, _) => {
			if (!resizing) setcursor(0);
		};
		grip.DragDelta += (_, e) => {
			startmanual();
			Width = Math.Max(MinWidth, ActualWidth + e.HorizontalChange);
			Height = Math.Max(MinHeight, ActualHeight + e.VerticalChange);
		};
		grip.DragCompleted += (_, _) => savepos();
		MouseMove += (_, e) => {
			if (resizing) return;
			setcursor(edgeat(e));
		};
		ContentRendered += (_, _) => {
			place();
		};
		Closed += (_, _) => {
			timer.Stop();
			proctimer.Stop();
			procs?.Dispose();
			savepos();
		};
		WindowEsc.Attach(this);
		timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PerfOpt.NormMs(opt.IntervalMs)) };
		timer.Tick += (_, _) => tick();
		timer.Start();
		tick();
		proctimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
		proctimer.Tick += (_, _) => tickprocs();
		startprocs();
	}

	void place() {
		if (placed) return;
		placed = true;
		if (opt.WinWidth >= MinWidth && opt.WinHeight >= MinHeight) {
			SizeToContent = SizeToContent.Manual;
			Width = opt.WinWidth;
			Height = opt.WinHeight;
		}
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
				opt.Save();
				buildrows();
			};
			menu.Items.Add(item);
		}
		menu.Items.Add(new Separator());
		procmi = new MenuItem { Header = Loc.T("perf.topproc"), IsCheckable = true, IsChecked = opt.TopProcs };
		procmi.Click += (_, _) => settopprocs(procmi.IsChecked);
		menu.Items.Add(procmi);
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
		opt.Save();
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

	// ==================== 边缘拖动改大小 ====================
	#region
	int edgeat(MouseEventArgs e) {
		if (e == null) return 0;
		var p = e.GetPosition(card);
		if (p.X < 0 || p.Y < 0 || p.X > card.ActualWidth || p.Y > card.ActualHeight) return 0;
		var edge = 0;
		if (p.X <= RESIZE_EDGE) edge |= E_LEFT;
		if (p.X >= card.ActualWidth - RESIZE_EDGE) edge |= E_RIGHT;
		if (p.Y <= RESIZE_EDGE) edge |= E_TOP;
		if (p.Y >= card.ActualHeight - RESIZE_EDGE) edge |= E_BOTTOM;
		return edge;
	}

	void setcursor(int edge) {
		Cursor cur;
		switch (edge) {
			case E_LEFT | E_TOP:
			case E_RIGHT | E_BOTTOM: cur = Cursors.SizeNWSE; break;
			case E_RIGHT | E_TOP:
			case E_LEFT | E_BOTTOM: cur = Cursors.SizeNESW; break;
			case E_LEFT:
			case E_RIGHT: cur = Cursors.SizeWE; break;
			case E_TOP:
			case E_BOTTOM: cur = Cursors.SizeNS; break;
			default: cur = Cursors.SizeAll; break;
		}
		if (!ReferenceEquals(card.Cursor, cur)) card.Cursor = cur;
	}

	void startresize(int edge, MouseButtonEventArgs e) {
		resizing = true;
		resizeEdge = edge;
		resizeFrom = e.GetPosition(this);
		startmanual();
		resizeL = Left;
		resizeT = Top;
		resizeW = ActualWidth;
		resizeH = ActualHeight;
		card.CaptureMouse();
	}

	void onresizemove(MouseEventArgs e) {
		if (!resizing) return;
		var p = e.GetPosition(this);
		var dx = p.X - resizeFrom.X;
		var dy = p.Y - resizeFrom.Y;
		var w = resizeW;
		var h = resizeH;
		var l = resizeL;
		var t = resizeT;
		if ((resizeEdge & E_RIGHT) != 0) w = Math.Max(MinWidth, resizeW + dx);
		if ((resizeEdge & E_LEFT) != 0) {
			w = Math.Max(MinWidth, resizeW - dx);
			l = resizeL + resizeW - w;
		}
		if ((resizeEdge & E_BOTTOM) != 0) h = Math.Max(MinHeight, resizeH + dy);
		if ((resizeEdge & E_TOP) != 0) {
			h = Math.Max(MinHeight, resizeH - dy);
			t = resizeT + resizeH - h;
		}
		Width = w;
		Height = h;
		if ((resizeEdge & (E_LEFT | E_TOP)) != 0) {
			Left = l;
			Top = t;
		}
	}

	void endresize() {
		if (!resizing) return;
		resizing = false;
		card.ReleaseMouseCapture();
		setcursor(0);
	}

	void startmanual() {
		if (SizeToContent == SizeToContent.Manual) return;
		SizeToContent = SizeToContent.Manual;
		Width = ActualWidth;
		Height = ActualHeight;
	}
	#endregion

	void buildrows() {
		rows.Children.Clear();
		rows.RowDefinitions.Clear();
		rows.ColumnDefinitions.Clear();
		cells.Clear();
		sparks.Clear();
		history.Clear();
		// 左右各占一半，右边不留空白
		rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		var on = new List<string>();
		foreach (var key in ShowOrder)
			if (opt.On(key)) on.Add(key);
		var halves = new Grid[2];
		for (var h = 0; h < 2; h++) {
			var half = new Grid { Margin = new Thickness(0, 0, h == 0 ? 8 : 0, 0) };
			half.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			half.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			half.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
			halves[h] = half;
			Grid.SetColumn(half, h);
			rows.Children.Add(half);
		}
		for (var i = 0; i < on.Count; i++) {
			var h = i % 2;
			var r = i / 2;
			if (h == 0) {
				halves[0].RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
				halves[1].RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			}
			var name = new TextBlock {
				Text = Loc.T($"perf.{on[i]}"),
				Foreground = nameBrush,
				FontSize = BODY,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 1, 4, 1),
			};
			var spark = new PerfSpark {
				Height = SPARK_H,
				Margin = new Thickness(0, 1, 4, 1),
				VerticalAlignment = VerticalAlignment.Center,
			};
			var val = new TextBlock {
				Text = "—",
				Foreground = dimBrush,
				FontSize = BODY,
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 1, 0, 1),
			};
			Grid.SetRow(name, r);
			Grid.SetColumn(name, 0);
			Grid.SetRow(spark, r);
			Grid.SetColumn(spark, 1);
			Grid.SetRow(val, r);
			Grid.SetColumn(val, 2);
			halves[h].Children.Add(name);
			halves[h].Children.Add(spark);
			halves[h].Children.Add(val);
			cells[on[i]] = val;
			sparks[on[i]] = spark;
			history[on[i]] = new List<(int tick, double v)>();
		}
	}

	// ==================== 进程排行 ====================
	#region
	void buildtabs() {
		var keep = tabs.SelectedIndex;
		tabs.Items.Clear();
		tabcells.Clear();
		tabnote.Clear();
		tabbuilt.Clear();
		if (!opt.TopProcs) {
			tabs.Visibility = Visibility.Collapsed;
			psec.Visibility = Visibility.Collapsed;
			return;
		}
		tabs.Visibility = Visibility.Visible;
		psec.Visibility = Visibility.Visible;
		foreach (var key in TabKeys) {
			var host = new Grid { Margin = new Thickness(0, 4, 0, 0) };
			var item = new TabItem { Header = Loc.T($"perf.{key}"), Content = host, Tag = key };
			tabs.Items.Add(item);
		}
		tabs.SelectionChanged -= ontabchanged;
		tabs.SelectionChanged += ontabchanged;
		tabs.SelectedIndex = keep >= 0 && keep < tabs.Items.Count ? keep : 0;
		opentab(tabs.SelectedIndex);
	}

	void ontabchanged(object sender, SelectionChangedEventArgs e) {
		if (e.OriginalSource != tabs) return;
		opentab(tabs.SelectedIndex);
	}

	void opentab(int index) {
		if (index < 0 || index >= tabs.Items.Count) return;
		if (tabs.Items[index] is not TabItem item || item.Content is not Grid host) return;
		ensuretab((string)item.Tag, host);
	}

	void ensuretab(string key, Grid host) {
		if (tabbuilt.ContainsKey(key)) return;
		tabbuilt[key] = true;
		host.Children.Clear();
		host.ColumnDefinitions.Clear();
		host.RowDefinitions.Clear();
		// 数值列按最长内容加一个空格定宽，剩下的全给进程名
		for (var c = 0; c < 5; c++)
			host.ColumnDefinitions.Add(new ColumnDefinition {
				Width = c == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto,
				MinWidth = c == 0 ? 60 : 0,
			});
		var heads = new[] { "perf.colname", "perf.cpu", "perf.mem", "perf.disk", "perf.net" };
		host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		for (var c = 0; c < heads.Length; c++) {
			var lb = new TextBlock {
				Text = Loc.T(heads[c]),
				Foreground = headBrush,
				FontSize = SMALL,
				Margin = new Thickness(0, 0, c == 0 ? 6 : SPACEW, 2),
				HorizontalAlignment = c == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
			};
			Grid.SetRow(lb, 0);
			Grid.SetColumn(lb, c);
			host.Children.Add(lb);
		}
		var sep = new Border { Height = 1, Background = gridBrush, Margin = new Thickness(0, 0, 0, 2) };
		Grid.SetRow(sep, 1);
		Grid.SetColumnSpan(sep, heads.Length);
		host.Children.Add(sep);
		host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		var body = new TextBlock[ProcTop.TOP, heads.Length];
		for (var r = 0; r < ProcTop.TOP; r++) {
			host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			for (var c = 0; c < heads.Length; c++) {
				var lb = new TextBlock {
					Text = "—",
					Foreground = dimBrush,
					FontSize = SMALL,
					TextTrimming = TextTrimming.CharacterEllipsis,
					Margin = new Thickness(0, 0, c == 0 ? 6 : SPACEW, 1),
					HorizontalAlignment = c == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
				};
				Grid.SetRow(lb, r + 2);
				Grid.SetColumn(lb, c);
				host.Children.Add(lb);
				body[r, c] = lb;
			}
		}
		var note = new TextBlock { Foreground = dimBrush, FontSize = SMALL, Margin = new Thickness(0, 2, 0, 0) };
		Grid.SetRow(note, ProcTop.TOP + 2);
		Grid.SetColumnSpan(note, heads.Length);
		host.Children.Add(note);
		host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		tabcells[key] = body;
		tabnote[key] = note;
	}

	void startprocs() {
		if (!opt.TopProcs) {
			proctimer.Stop();
			procs?.Dispose();
			procs = null;
			return;
		}
		if (procs == null) {
			procs = new ProcTop();
			procs.Start();
		}
		proctimer.Start();
		tickprocs();
	}

	void settopref() {
		buildtabs();
		startprocs();
		if (!opt.TopProcs) {
			var area = SystemParameters.WorkArea;
			Top = Math.Min(Top, Math.Max(area.Top, area.Bottom - ActualHeight - 16));
		}
	}

	void tickprocs() {
		if (!opt.TopProcs) return;
		if (procs == null) return;
		var s = procs.Take();
		painttab("tcpu", s.Cpu, s.NetOk);
		painttab("tmem", s.Mem, s.NetOk);
		painttab("tdisk", s.Disk, s.NetOk);
		painttab("tnet", s.Net, s.NetOk);
	}

	void painttab(string key, List<ProcTop.Row> list, bool netOk) {
		if (!tabcells.TryGetValue(key, out var body)) return;
		var net = key == "tnet";
		var note = tabnote.TryGetValue(key, out var nb) ? nb : null;
		if (net && !netOk) {
			if (note != null) note.Text = Loc.T("perf.netneed");
		} else if (note != null) note.Text = "";
		for (var r = 0; r < ProcTop.TOP; r++) {
			var row = list != null && r < list.Count ? list[r] : null;
			body[r, 0].Text = row == null ? "—" : row.Name;
			body[r, 0].Foreground = row == null ? dimBrush : headBrush;
			body[r, 1].Text = row == null ? "—" : $"{row.Cpu:0.0}%";
			body[r, 1].Foreground = row == null ? dimBrush : pctc(row.Cpu);
			body[r, 2].Text = row == null ? "—" : bytes(row.Mem);
			body[r, 2].Foreground = row == null ? dimBrush : nameBrush;
			body[r, 3].Text = row == null ? "—" : (row.Disk > 0 ? rate(row.Disk) : "0");
			body[r, 3].Foreground = row == null ? dimBrush : (row.Disk > 0 ? downBrush : nameBrush);
			body[r, 4].Text = row == null || !netOk ? "—" : (row.Net > 0 ? rate(row.Net) : "0");
			body[r, 4].Foreground = row == null || !netOk ? dimBrush : (row.Net > 0 ? warnBrush : nameBrush);
		}
	}

	static string bytes(long v) {
		if (v <= 0) return "—";
		const double kb = 1024, mb = 1024 * 1024, gb = 1024 * 1024 * 1024;
		if (v >= gb) return $"{v / gb:0.0}G";
		if (v >= mb) return $"{v / mb:0}M";
		if (v >= kb) return $"{v / kb:0}K";
		return $"{v}B";
	}
	#endregion

	void tick() {
		try { paint(PerfMon.Take()); }
		catch (Exception ex) { lbtitle.Text = ex.Message; }
	}

	void paint(PerfMon.Snap s) {
		set("cpu", pct(s.Cpu), pctc(s.Cpu), s.Cpu, 25);
		set("gpu", pct(s.Gpu), pctc(s.Gpu), s.Gpu, 25);
		set("mem", pct(s.Mem), pctc(s.Mem), s.Mem, 25);
		set("down", rate(s.Down), downBrush, s.Down);
		set("up", rate(s.Up), warnBrush, s.Up);
		set("read", rate(s.Read), downBrush, s.Read);
		set("write", rate(s.Write), warnBrush, s.Write);
		set("cputemp", temp(s.CpuTemp), heat(s.CpuTemp), s.CpuTemp, 80);
		set("gputemp", temp(s.GpuTemp), heat(s.GpuTemp), s.GpuTemp, 80);
		set("gpupower", watt(s.GpuPower), downBrush, s.GpuPower);
		set("cpupower", watt(s.CpuPower), warnBrush, s.CpuPower);
	}

	void push(string key, double v, double max) {
		if (!sparks.TryGetValue(key, out var spark)) return;
		var list = history[key];
		var now = Environment.TickCount;
		// 没读数的不画，占位用 NaN，折线在那一段断开
		list.Add((now, double.IsNaN(v) || v < 0 ? double.NaN : v));
		var cut = now - SPAN_MS - 5000;
		var drop = 0;
		while (drop < list.Count && unchecked(list[drop].tick - cut) < 0) drop++;
		if (drop > 0) list.RemoveRange(0, drop);
		var data = new double[list.Count];
		var ages = new double[list.Count];
		for (var i = 0; i < list.Count; i++) {
			data[i] = list[i].v;
			ages[i] = unchecked(now - list[i].tick);
		}
		if (cells.TryGetValue(key, out var cell)) spark.Stroke = cell.Foreground;
		spark.Push(data, ages, max);
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
		var tabcpu = win.TabShown("tcpu", 0, 0);
		var tabcpuval = win.TabShown("tcpu", 0, 1);
		log($"tab cpu={tabcpu} {tabcpuval}");
		win.Close();
		if (cpu.Length == 0 || cpu == "—") {
			err("窗口没有显示 CPU");
			return 1;
		}
		if (mem.Length == 0 || mem == "—") {
			err("窗口没有显示内存");
			return 1;
		}
		if (tabcpu.Length == 0 || tabcpu == "—") {
			err("进程排行没有显示进程");
			return 1;
		}
		return 0;
	}

	public string TabShown(string key, int row, int col) {
		if (!tabcells.TryGetValue(key, out var body)) return "";
		if (row < 0 || row >= ProcTop.TOP || col < 0 || col >= body.GetLength(1)) return "";
		return body[row, col].Text ?? "";
	}

	void set(string key, string text, SolidColorBrush brush, double value = double.NaN, double max = 0) {
		if (!cells.TryGetValue(key, out var block)) return;
		block.Text = text;
		block.Foreground = text == "—" ? dimBrush : brush;
		push(key, value, max);
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
		optWin = new PerfMonOptWindow(opt, setinterval, settopprocs) { Owner = this, Topmost = Topmost };
		optWin.Closed += (_, _) => optWin = null;
		optWin.Show();
	}

	void setinterval(int ms) {
		opt.IntervalMs = PerfOpt.NormMs(ms);
		timer.Interval = TimeSpan.FromMilliseconds(opt.IntervalMs);
		opt.Save();
	}

	void settopprocs(bool on) {
		opt.TopProcs = on;
		opt.Save();
		if (procmi != null) procmi.IsChecked = on;
		settopref();
	}

	void savepos() {
		if (!placed) return;
		opt.HasPos = true;
		opt.Left = Left;
		opt.Top = Top;
		opt.WinWidth = ActualWidth;
		opt.WinHeight = ActualHeight;
		opt.Save();
	}
}


