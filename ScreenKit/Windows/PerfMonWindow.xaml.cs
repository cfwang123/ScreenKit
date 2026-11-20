using System.Diagnostics;
using System.Text;
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
	// 进程排行每行高度（字号 9 + 上下留白），实测不到时的兜底
	const double ROWH = 12;
	double rowhcache;
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
	// 数值列宽，够放 "1.1M/s"、"28°C" 这类最长读数
	const double VALW = 52;
	// 小于这么宽就只排一列，两列的话名称和数值都会被挤掉
	const double TWOMIN = 340;
	bool lasttwo = true;
	static readonly string[] ShowOrder = {
		"cpu", "gpu", "mem", "cputemp", "down", "up", "read", "write", "gputemp", "gpupower", "cpupower",
	};
	static readonly string[] TabKeys = { "tcpu", "tmem", "tdisk", "tnet" };
	readonly Dictionary<string, TextBlock[,]> tabcells = new();
	readonly Dictionary<string, TextBlock> tabnote = new();
	readonly Dictionary<string, Button> tabadmin = new();
	readonly Dictionary<string, Grid> tabhost = new();
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
		Opacity = PerfOpt.NormOpacity(opt.Opacity);
		btop.ToolTip = Loc.T("perf.top");
		bclose.ToolTip = Loc.T("perf.close");
		paintpin();
		buildmenu();
		buildrows();
		buildtabs();
		card.MouseLeftButtonDown += (_, e) => {
			var src = e.OriginalSource as DependencyObject;
			if (under(btop, src) || under(bclose, src) || under(tabs, src) || under(psec, src)) return;
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
		SizeChanged += (_, _) => {
			// 跨过宽度阈值时换成一列或两列；重排会把读数重置成「—」，马上补一次
			if (twocol() != lasttwo) {
				buildrows();
				tick();
			}
			foreach (var key in tabcells.Keys.ToList()) fitrows(key);
		};
		tabs.SizeChanged += (_, _) => {
			foreach (var key in tabcells.Keys.ToList()) fitrows(key);
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
		proctimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(opt.IntervalMs) };
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
		var area = SystemParameters.WorkArea;
		if (opt.HasPos) {
			Left = opt.Left;
			Top = opt.Top;
			// 上次可能在副屏上，副屏拔了就会落在所有屏幕之外：钳回屏内
			WinPlace.KeepOnScreen(this);
			return;
		}
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

	void bclose_Click(object sender, RoutedEventArgs e) {
		Close();
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
		// 窗口窄的时候排成一列，否则两列各占一半、数值会被裁掉
		lasttwo = twocol();
		var cols = lasttwo ? 2 : 1;
		for (var c = 0; c < cols; c++)
			rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		var on = new List<string>();
		foreach (var key in ShowOrder)
			if (opt.On(key)) on.Add(key);
		var halves = new Grid[cols];
		for (var h = 0; h < cols; h++) {
			var half = new Grid { Margin = new Thickness(0, 0, h == cols - 1 ? 0 : 8, 0) };
			half.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			half.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			// 读数按内容定宽，星号列留给折线，宽度再紧也不会把读数截掉
			half.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = VALW });
			halves[h] = half;
			Grid.SetColumn(half, h);
			rows.Children.Add(half);
		}
		for (var i = 0; i < on.Count; i++) {
			var h = i % cols;
			var r = i / cols;
			if (h == 0) {
				for (var k = 0; k < cols; k++)
					halves[k].RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			}
			var name = new TextBlock {
				Text = Loc.T($"perf.{on[i]}"),
				Foreground = nameBrush,
				FontSize = BODY,
				VerticalAlignment = VerticalAlignment.Center,
				TextTrimming = TextTrimming.CharacterEllipsis,
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
			if (!history.ContainsKey(on[i])) history[on[i]] = new List<(int tick, double v)>();
		}
	}

	/// <summary>宽度够就排两列，太窄排一列，免得读数和名称被裁掉</summary>
	bool twocol() {
		var w = ActualWidth > 0 ? ActualWidth : Width;
		if (double.IsNaN(w)) w = TWOMIN;
		return w >= TWOMIN;
	}

	// ==================== 进程排行 ====================
	#region
	void buildtabs() {
		var keep = tabs.SelectedIndex >= 0 ? tabs.SelectedIndex : Array.IndexOf(PerfOpt.Tabs, PerfOpt.NormTab(opt.ProcTab));
		tabs.Items.Clear();
		tabcells.Clear();
		tabnote.Clear();
		tabadmin.Clear();
		tabhost.Clear();
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
		savetab();
		opentab(tabs.SelectedIndex);
	}

	/// <summary>记住选中的进程类别，下次开窗照旧</summary>
	void savetab() {
		var i = tabs.SelectedIndex;
		if (i < 0 || i >= PerfOpt.Tabs.Length) return;
		if (opt.ProcTab == PerfOpt.Tabs[i]) return;
		opt.ProcTab = PerfOpt.Tabs[i];
		try { opt.Save(); } catch { }
	}

	void opentab(int index) {
		if (index < 0 || index >= tabs.Items.Count) return;
		if (tabs.Items[index] is not TabItem item || item.Content is not Grid host) return;
		ensuretab((string)item.Tag, host);
		// 切到另一个类别时不等定时器，立刻采一轮再画
		refreshprocs();
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
				MinWidth = c == 0 ? 40 : 0,
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
				ToolTip = c == 3 ? Loc.T("perf.disktip") : null,
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
		if (key == "tnet") {
			// 网络字节只有连接级统计拿得到，那需要管理员，给个一键提权重开
			var btn = new Button {
				Content = Loc.T("perf.netadmin"),
				Foreground = downBrush,
				FontSize = SMALL,
				Cursor = Cursors.Hand,
				HorizontalAlignment = HorizontalAlignment.Left,
				Padding = new Thickness(0),
				Margin = new Thickness(0, 2, 0, 0),
				Background = Brushes.Transparent,
				BorderThickness = new Thickness(0),
				Visibility = Visibility.Collapsed,
			};
			btn.Click += (_, _) => reopenelevated();
			Grid.SetRow(btn, ProcTop.TOP + 3);
			Grid.SetColumnSpan(btn, heads.Length);
			host.Children.Add(btn);
			host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			tabadmin[key] = btn;
		}
		tabcells[key] = body;
		tabnote[key] = note;
		tabhost[key] = host;
		host.SizeChanged += (_, _) => fitrows(key);
		fitrows(key);
	}

	// 行数不定：按 tab 内容区高度算能放几行，超出的折叠
	void fitrows(string key) {
		if (!tabcells.TryGetValue(key, out var body)) return;
		if (!tabhost.TryGetValue(key, out var host)) return;
		if (body.GetLength(0) == 0) return;
		// 可用高度取 tab 控件高度减掉标签头，内容 Grid 本身是 Auto 高，量它会越算越大
		var avail = tabs.ActualHeight - headerheight();
		if (avail <= 0) return;
		var noteh = tabnote.TryGetValue(key, out var nt) && nt.Visibility == Visibility.Visible ? nt.ActualHeight + nt.Margin.Top : 0;
		var btnh = tabadmin.TryGetValue(key, out var bb) && bb.Visibility == Visibility.Visible ? bb.ActualHeight + bb.Margin.Top : 0;
		var headh = host.RowDefinitions[0].ActualHeight + host.RowDefinitions[1].ActualHeight;
		// 行高用实测值，折叠掉的量不到就沿用上一次的
		var rowh = body[0, 0].ActualHeight > 0 ? body[0, 0].ActualHeight + body[0, 0].Margin.Bottom : rowhcache;
		if (rowh > 0) rowhcache = rowh;
		if (rowh <= 0) rowh = ROWH;
		var fit = (int)((avail - headh - noteh - btnh) / rowh);
		if (fit < 1) fit = 1;
		if (fit > body.GetLength(0)) fit = body.GetLength(0);
		for (var r = 0; r < body.GetLength(0); r++) {
			var vis = r < fit ? Visibility.Visible : Visibility.Collapsed;
			for (var c = 0; c < body.GetLength(1); c++)
				if (body[r, c].Visibility != vis) body[r, c].Visibility = vis;
		}
	}

	double headerheight() {
		var panel = findchild<TabPanel>(tabs);
		return panel != null && panel.ActualHeight > 0 ? panel.ActualHeight + tabs.Margin.Top : 24;
	}

	static T findchild<T>(DependencyObject root) where T : DependencyObject {
		if (root == null) return null;
		var n = VisualTreeHelper.GetChildrenCount(root);
		for (var i = 0; i < n; i++) {
			var c = VisualTreeHelper.GetChild(root, i);
			if (c is T hit) return hit;
			var deeper = findchild<T>(c);
			if (deeper != null) return deeper;
		}
		return null;
	}

	/// <summary>关掉本窗口，以管理员权限重开一个性能监控（只开 --perfmon，不启动主界面）。</summary>
	void reopenelevated() {
		try {
			var exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
			if (string.IsNullOrEmpty(exe)) return;
			var script = "Start-Process -FilePath " + psq(exe) + " -ArgumentList '--perfmon' -Verb RunAs";
			var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
			Process.Start(new ProcessStartInfo {
				FileName = "cmd.exe",
				Arguments = "/c start \"ScreenKit\" /b powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded,
				UseShellExecute = false,
				CreateNoWindow = true,
			});
			Close();
		}
		catch (Exception ex) { lbtitle.Text = ex.Message; }
	}

	static string psq(string text) => "'" + (text ?? "").Replace("'", "''") + "'";

	void startprocs() {
		if (!opt.TopProcs) {
			proctimer.Stop();
			procs?.Dispose();
			procs = null;
			return;
		}
		if (procs == null) {
			procs = new ProcTop { IntervalMs = opt.IntervalMs };
			procs.Start();
		}
		proctimer.Start();
		tickprocs();
		// 刚开窗时快照还是空的，叫后台立刻采一轮，不用等第一个间隔
		refreshprocs();
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

	/// <summary>不等定时器，叫后台马上采一轮，采完回到界面线程刷一次</summary>
	void refreshprocs() {
		if (!opt.TopProcs || procs == null) return;
		procs.Refresh(() => Dispatcher.BeginInvoke(new Action(tickprocs)));
	}

	void painttab(string key, List<ProcTop.Row> list, bool netOk) {
		if (!tabcells.TryGetValue(key, out var body)) return;
		var net = key == "tnet";
		var note = tabnote.TryGetValue(key, out var nb) ? nb : null;
		var btn = tabadmin.TryGetValue(key, out var bb) ? bb : null;
		if (net && !netOk) {
			if (note != null) note.Text = Loc.T("perf.netneed");
			if (btn != null) btn.Visibility = Visibility.Visible;
		} else {
			if (note != null) note.Text = "";
			if (btn != null) btn.Visibility = Visibility.Collapsed;
		}
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
		fitrows(key);
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

	/// <summary>当前是不是两列排版</summary>
	public bool TwoCol => lasttwo;

	/// <summary>排一排的读数有没有被裁掉：按单元格实际宽度比一遍文字宽度</summary>
	public string Clipped() {
		var bad = new List<string>();
		foreach (var pair in cells) {
			var cell = pair.Value;
			cell.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
			var need = cell.DesiredSize.Width;
			if (need < 1) continue;
			var have = cell.ActualWidth;
			if (have < 1) continue;
			if (need > have + 0.5) bad.Add($"{pair.Key}={have:0}<{need:0}");
		}
		return string.Join(",", bad);
	}

	public static int Check(Action<string> log, Action<string> err) {
		var win = new PerfMonWindow();
		win.Show();
		pumpms(50);
		var cpu = win.Shown("cpu");
		var mem = win.Shown("mem");
		log($"win cpu={cpu} mem={mem}");
		// 进程排行由后台线程采样，第一轮快照可能要几十毫秒才好，等它出数据
		// 读当前选中的类别：上次可能停在硬盘或网络页，那几页不一定先建出来
		var tabkey = win.SelectedTabKey;
		var tabcpu = win.TabShown(tabkey, 0, 0);
		for (var i = 0; i < 15 && (tabcpu.Length == 0 || tabcpu == "—"); i++) {
			pumpms(100);
			tabcpu = win.TabShown(tabkey, 0, 0);
		}
		var tabcpuval = win.TabShown(tabkey, 0, 1);
		log($"tab {tabkey} cpu={tabcpu} {tabcpuval} disk={win.TabShown("tdisk", 0, 3)}");
		// 进程磁盘列用的是每进程 I/O 计数（含缓存命中、管道等非物理盘 I/O），
		// 和顶上的 PhysicalDisk 速率不是一个口径，把两者的量级打出来对一下
		log(win.DiskDiag());
		log($"power cpu={win.Shown("cpupower")} gpu={win.Shown("gpupower")} min={win.MinWidth:0}");
		// 宽的时候两列，拖窄之后应该自动变成一列，两种宽度下读数都不能被裁
		win.Width = 470;
		pumpms(20);
		var wide = win.Clipped();
		log($"wide two={win.TwoCol} w={win.ActualWidth:0} clip=[{wide}]");
		win.Width = 350;
		pumpms(20);
		var mid = win.Clipped();
		log($"mid two={win.TwoCol} w={win.ActualWidth:0} clip=[{mid}]");
		win.Width = 240;
		pumpms(20);
		var narrow = win.Clipped();
		log($"narrow two={win.TwoCol} w={win.ActualWidth:0} clip=[{narrow}]");
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
		if (narrow.Length > 0) {
			err("窗口变窄后读数被裁：" + narrow);
			return 1;
		}
		if (wide.Length > 0) {
			err("窗口变宽后读数被裁：" + wide);
			return 1;
		}
		return 0;
	}

	/// <summary>空转若干毫秒，让布局和定时器跑完</summary>
	static void pumpms(int ms) {
		var frame = new DispatcherFrame();
		var pump = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
		var n = 0;
		var times = Math.Max(1, ms / 50);
		pump.Tick += (_, _) => {
			if (++n >= times) {
				pump.Stop();
				frame.Continue = false;
			}
		};
		pump.Start();
		Dispatcher.PushFrame(frame);
	}

	/// <summary>当前选中的进程类别键（tcpu/tmem/tdisk/tnet）</summary>
	public string SelectedTabKey {
		get {
			var i = tabs.SelectedIndex;
			return i >= 0 && i < PerfOpt.Tabs.Length ? PerfOpt.Tabs[i] : PerfOpt.Tabs[0];
		}
	}

	/// <summary>把进程 I/O 合计和顶部物理磁盘速率放在一起，看口径差多少</summary>
	public string DiskDiag() {
		var snap = procs?.Take();
		var sum = snap == null ? 0 : snap.Disk.Sum(r => r.Disk);
		var top1 = snap != null && snap.Disk.Count > 0 ? snap.Disk[0].Disk : 0;
		var physrd = PerfMonTakeDisk(out var physwr);
		return $"proc disk sum={sum / 1048576.0:0.0}M/s top={top1 / 1048576.0:0.0}M/s "
			+ $"phys rd={physrd / 1048576.0:0.0}M/s wr={physwr / 1048576.0:0.0}M/s";
	}

	static double PerfMonTakeDisk(out double wr) {
		var s = PerfMon.Take();
		wr = s.Write;
		return s.Read;
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
		optWin = new PerfMonOptWindow(opt, setinterval, settopprocs, setopacity) { Owner = this, Topmost = Topmost };
		optWin.Closed += (_, _) => optWin = null;
		optWin.Show();
	}

	void setinterval(int ms) {
		opt.IntervalMs = PerfOpt.NormMs(ms);
		timer.Interval = TimeSpan.FromMilliseconds(opt.IntervalMs);
		// 进程排行跟随同一个更新间隔
		proctimer.Interval = TimeSpan.FromMilliseconds(opt.IntervalMs);
		if (procs != null) procs.IntervalMs = opt.IntervalMs;
		opt.Save();
	}

	void settopprocs(bool on) {
		opt.TopProcs = on;
		opt.Save();
		if (procmi != null) procmi.IsChecked = on;
		settopref();
	}

	void setopacity(double v) {
		opt.Opacity = PerfOpt.NormOpacity(v);
		Opacity = opt.Opacity;
		opt.Save();
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


