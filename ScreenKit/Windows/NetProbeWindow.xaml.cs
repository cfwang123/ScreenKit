using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using Microsoft.Win32;

namespace ScreenKit;

/// <summary>统计页列表行（Value/ExtraValue 供热力与排序）。</summary>
sealed class NetStatRow {
	public string Name { get; set; }
	public int Pid { get; set; }
	public double Value { get; set; }
	public string ValueText { get; set; }
	public string Extra { get; set; }
	public double ExtraValue { get; set; }
}

/// <summary>工具 → 网络工具 → 网络监控。按进程统计连接与流量，并可抓包看报文。</summary>
public partial class NetProbeWindow : Window {
	NetworkMonitor mon;
	int filterPid = -1;
	bool onlyActive;
	ICollectionView procView, connView, hostView;

	int selProcPid = -1;
	string selConnKey = "";
	string selHostKey = "";
	bool restoringSel;

	// 筛选状态
	string hostQuery = "";
	string payloadQuery = "";
	string protoQuery = ""; // 空 = 全部协议
	bool payloadHex;
	bool searchActive;
	bool suppressProtoEvent;
	readonly HashSet<int> matchPids = new();
	readonly HashSet<string> matchConnKeys = new(StringComparer.Ordinal);
	readonly HashSet<string> matchHostKeys = new(StringComparer.Ordinal);
	readonly HashSet<string> matchCapKeys = new(StringComparer.Ordinal); // MakeKey 格式，用于统计抓包数
	readonly List<string> matchProcNames = new();
	int matchPacketCount;
	int nextLiveSearch; // TickCount 节流

	// 抓包列表
	readonly ObservableCollection<PacketRow> capListView = new();
	PacketRow capCurrent;
	PacketRow capPendingSelect;
	bool capSyncing;
	bool capRefreshing;
	bool capIgnoreScroll;
	bool capUserSelect;
	bool capListFrozen;
	ScrollViewer capScroll;
	int capHlStart = -1, capHlLen;
	string lastCapFilterKey = "";
	string lastCapTailId = "";
	long lastCapTailTicks;
	int lastCapFilteredCount = -1;
	readonly Dictionary<TreeViewItem, ProtoNode> capItemMap = new();
	readonly List<ProtoNode> capFlatFields = new();
	// 统计表：固定 ItemsSource，避免刷新时重置用户列宽
	readonly ObservableCollection<NetStatRow> statTopProc = new();
	readonly ObservableCollection<NetStatRow> statTopHost = new();
	readonly ObservableCollection<NetStatRow> statProto = new();

	public NetProbeWindow() {
		InitializeComponent();
		initbindings();
		Loaded += (_, _) => onloaded();
		Closed += (_, _) => onclosed();
	}

	void initbindings() {
		bcaptoggle.Click += (_, _) => bcaptoggle_Click();
		bcapclear.Click += (_, _) => bcapclear_Click();
		bclearhosts.Click += (_, _) => bclearhosts_Click();
		brules.Click += (_, _) => brules_Click();
		bclearprocfilter.Click += (_, _) => { if (efilter != null) efilter.Text = ""; };
		bsearchclear.Click += (_, _) => clearSearch();
		badmin.Click += (_, _) => reopenelevated();

		efilter.TextChanged += (_, _) => onprocfilterchanged();
		ehostq.TextChanged += (_, _) => scheduleLiveSearch();
		ehostq.KeyDown += (_, e) => {
			if (e.Key == Key.Enter) { liveSearch(true); e.Handled = true; }
		};
		epayq.TextChanged += (_, _) => scheduleLiveSearch();
		epayq.KeyDown += (_, e) => {
			if (e.Key == Key.Enter) { liveSearch(true); e.Handled = true; }
		};
		cproto.SelectionChanged += (_, _) => {
			if (suppressProtoEvent) return;
			liveSearch(false);
		};
		cpaymode.SelectionChanged += (_, _) => scheduleLiveSearch();
		cprocview.SelectionChanged += (_, _) => {
			// 0=全部进程，1=仅活动进程
			onlyActive = cprocview?.SelectedIndex == 1;
			restoringSel = true;
			try {
				procView?.Refresh();
				restoreSelection();
			} finally { restoringSel = false; }
		};

		dgproc.SelectionChanged += (_, _) => {
			if (restoringSel) return;
			if (dgproc.SelectedItem is ProcessNetRow p) {
				selProcPid = p.Pid;
				filterPid = p.Pid;
				connView?.Refresh();
				hostView?.Refresh();
				selConnKey = "";
				selHostKey = "";
				updateTitles();
			}
		};
		dgproc.LoadingRow += (_, e) => {
			if (e.Row.Item is ProcessNetRow p) applyRuleRowColor(e.Row, p.RuleTag);
		};
		mnproccap.Click += (_, _) => mnproccap_Click();
		mnproccapall.Click += (_, _) => mnproccapall_Click();

		dgconn.SelectionChanged += (_, _) => {
			if (restoringSel) return;
			if (dgconn.SelectedItem is ConnectionRow c) selConnKey = c.Key ?? "";
		};
		dgconn.LoadingRow += (_, e) => {
			if (e.Row.Item is ConnectionRow c) applyRuleRowColor(e.Row, c.RuleTag);
		};
		dgconn.MouseDoubleClick += (_, _) => openconnpackets();

		dghosts.SelectionChanged += (_, _) => {
			if (restoringSel) return;
			if (dghosts.SelectedItem is HostRecord h) selHostKey = h.Key ?? "";
		};

		tabs.SelectionChanged += (_, e) => {
			// SelectionChanged 会冒泡：DataGrid 点选也会进这里，必须只处理 Tab 本身切换
			if (e != null && !ReferenceEquals(e.OriginalSource, tabs)) return;
			if (tabs.SelectedIndex == 1) refreshCapList(true);
			else if (tabs.SelectedIndex == 2) refreshStats();
		};

		bcaplistrefresh.Click += (_, _) => refreshCapList(true);
		bcaphttp.Click += (_, _) => bcaphttp_Click();
		bcapexport.Click += (_, _) => bcapexport_Click();
		bcapimport.Click += (_, _) => bcapimport_Click();
		bstatrefresh.Click += (_, _) => refreshStats();

		ccapproto.SelectionChanged += (_, _) => {
			if (suppressProtoEvent) return;
			refreshCapList(true);
		};
		ecapip.TextChanged += (_, _) => refreshCapList(true);
		ecapport.TextChanged += (_, _) => refreshCapList(true);
		ecaplport.TextChanged += (_, _) => refreshCapList(true);

		ckcapscroll.Checked += (_, _) => {
			// 重新勾选 = 解除冻结 + 全量同步 + 滚到底
			capListFrozen = false;
			Dispatcher.BeginInvoke(() => {
				if (ckcapscroll?.IsChecked != true) return;
				capListFrozen = false;
				refreshCapList(true);
				if (capListView.Count > 0) scrollCapListToEnd();
			}, System.Windows.Threading.DispatcherPriority.Background);
		};

		dgcaplist.Loaded += (_, _) => {
			if (capScroll != null) return;
			capScroll = findScrollViewer(dgcaplist);
			if (capScroll != null) capScroll.ScrollChanged += capScroll_ScrollChanged;
		};
		dgcaplist.SelectionChanged += (_, _) => oncaplistselected();
		dgcaplist.PreviewMouseLeftButtonDown += (_, _) => {
			// 只做标记与冻结意图，不在此处改 UI 控件（改 CheckBox 会吞掉点选）
			capUserSelect = true;
			capListFrozen = true;
			Dispatcher.BeginInvoke(() => {
				freezeCapList();
				capUserSelect = false;
			}, System.Windows.Threading.DispatcherPriority.Input);
		};
		dgcaplist.MouseDoubleClick += (_, _) => {
			if (dgcaplist?.SelectedItem is PacketRow row) {
				freezeCapList();
				showCapPacket(row);
			}
		};

		tvcapproto.SelectedItemChanged += (_, _) => oncapfieldselected();
		ecaphex.PreviewMouseLeftButtonUp += (_, _) => oncaphexclicked();
	}

	void onloaded() {
		mon = new NetworkMonitor(Dispatcher);
		dgproc.ItemsSource = mon.Processes;
		dgconn.ItemsSource = mon.Connections;
		dghosts.ItemsSource = mon.HostRecords;

		procView = CollectionViewSource.GetDefaultView(mon.Processes);
		connView = CollectionViewSource.GetDefaultView(mon.Connections);
		hostView = CollectionViewSource.GetDefaultView(mon.HostRecords);
		procView.Filter = filterProc;
		connView.Filter = filterConn;
		hostView.Filter = filterHost;

		mon.KeepHistory = false;
		mon.Updated += onupdated;
		mon.Start();
		initProtoCombo(cproto);
		initProtoCombo(ccapproto);
		dgcaplist.ItemsSource = capListView;
		dgstattopproc.ItemsSource = statTopProc;
		dgstattophost.ItemsSource = statTopHost;
		dgstatproto.ItemsSource = statProto;
		updateCapButtons();
		lbstatus.Text = "监控中 · 未抓包（点击「开始抓包」记录全部 TCP/UDP）";
		lbsearch.Text = "";
		if (!SysDeviceAdmin.IsAdmin()) {
			lbnpadmin.Text = "非管理员：字节数与报文统计为空，需要管理员权限读取 TCP ESTATS";
			badmin.Visibility = Visibility.Visible;
		}
		WindowEsc.Attach(this);
	}

	void onclosed() {
		if (mon != null) {
			mon.Updated -= onupdated;
			mon.Dispose();
			mon = null;
		}
		GlobalPacketCapture.Instance.Dispose();
	}

	static readonly string[] ProtoChoices = {
		"全部", "HTTP", "HTTPS", "HTTP/HTTPS", "DNS", "SSH", "FTP", "SMTP", "POP3", "IMAP",
		"RDP", "SMB", "MQTT", "Redis", "MySQL", "PostgreSQL", "MSSQL", "QUIC", "TCP", "UDP", "TLS", "Data"
	};

	void initProtoCombo(ComboBox cb) {
		if (cb == null) return;
		suppressProtoEvent = true;
		try {
			cb.Items.Clear();
			foreach (var s in ProtoChoices) cb.Items.Add(s);
			cb.SelectedIndex = 0;
		} finally { suppressProtoEvent = false; }
	}

	static string readProtoCombo(ComboBox cb) {
		if (cb == null) return "";
		if (cb.SelectedItem is string s) {
			if (string.IsNullOrEmpty(s) || s == "全部") return "";
			return s;
		}
		var t = cb.Text?.Trim() ?? "";
		if (string.IsNullOrEmpty(t) || t == "全部" || t == "*") return "";
		return t;
	}

	void onupdated() {
		if (mon == null) return;
		if (searchActive) rebuildSearchMatches();

		// 进程过滤抓包：补充新连接键；刷新连接→进程映射（抓包列表进程列）
		syncCaptureProcessKeys();
		if (mon.Connections != null)
			GlobalPacketCapture.Instance.UpdateConnProcessMap(mon.Connections);

		GlobalPacketCapture.Instance.RefreshStatusText();
		lbtotal.Text = $"累计发送 {ProcessNetRow.FormatBytes(mon.TotalBytesSent)}  ·  累计接收 {ProcessNetRow.FormatBytes(mon.TotalBytesRecv)}";

		if (mon.Processes != null) HeatBrushConverter.UpdateFromProcesses(mon.Processes);
		if (mon.Connections != null) HeatBrushConverter.UpdateFromConnections(mon.Connections);
		if (mon.HostRecords != null) HeatBrushConverter.UpdateFromHosts(mon.HostRecords);

		// 仅在筛选结果可能变化时 Refresh（整表重滤很贵）
		var needProcRefresh = searchActive || onlyActive || !string.IsNullOrEmpty(efilter?.Text);
		var needConnRefresh = searchActive || filterPid > 0 || !string.IsNullOrEmpty(efilter?.Text);
		var needHostRefresh = searchActive;
		if (needProcRefresh || needConnRefresh || needHostRefresh) {
			captureSelection();
			restoringSel = true;
			try {
				if (searchActive) applyHighlights();
				if (needProcRefresh) procView?.Refresh();
				if (needConnRefresh) connView?.Refresh();
				if (needHostRefresh) hostView?.Refresh();
				restoreSelection();
			} finally { restoringSel = false; }
		} else if (searchActive) {
			applyHighlights();
		}

		updateTitles();
		updateCapButtons();
		updateSearchLabel();
		updateStatusText();
		if (tabs?.SelectedIndex == 1 && ckcapscroll?.IsChecked == true && !capListFrozen && !capUserSelect)
			refreshCapList(false);
		if (tabs?.SelectedIndex == 2) refreshStats();
	}

	void updateStatusText() {
		if (lbstatus == null || mon == null) return;
		var cap = GlobalPacketCapture.Instance;
		var head = cap.IsRunning
			? "【抓包中】 "
			: (cap.HasSession || cap.TotalPacketCount > 0 ? "【已结束·缓存保留】 " : "");
		lbstatus.Text = head + mon.StatusText + " · " + formatCapStats(cap);
	}

	string formatCapStats(GlobalPacketCapture cap) {
		var totalConn = cap.ConnectionBufferCount;
		var totalPkt = cap.TotalPacketCount;
		if (!searchActive) return $"总抓包 {totalConn}连接/{totalPkt}包";
		return $"总抓包 {totalConn}连接/{totalPkt}包 · 筛选 {matchConnKeys.Count}连接/{matchPacketCount}包 · {matchPids.Count}进程";
	}

	void updateCapButtons() {
		var cap = GlobalPacketCapture.Instance;
		var running = cap.IsRunning;
		var hasCache = cap.TotalPacketCount > 0 || cap.ConnectionBufferCount > 0 || cap.HasSession;

		if (bcaptoggle != null) {
			if (running) {
				bcaptoggle.Content = "■ 结束抓包";
				bcaptoggle.Background = brush("#DC2626");
				bcaptoggle.ToolTip = "停止记录新报文，保留已抓历史包与已断开连接";
			} else {
				bcaptoggle.Content = "● 开始抓包";
				bcaptoggle.Background = brush("#3B82F6");
				bcaptoggle.ToolTip = "开始记录全部 TCP/UDP 报文（不限进程），并保留已断开连接供查看";
			}
		}

		if (bdcap == null || lbcap == null || dotcap == null) return;

		var totalPart = $"{cap.ConnectionBufferCount}连接/{cap.TotalPacketCount}包";
		var filtPart = searchActive ? $" · 筛选{matchConnKeys.Count}连接/{matchPacketCount}包" : "";

		if (running) {
			bdcap.Background = brush("#FEE2E2");
			bdcap.BorderBrush = brush("#DC2626");
			lbcap.Text = $"● 抓包中  总{totalPart}{filtPart}";
			lbcap.Foreground = brush("#991B1B");
			dotcap.Fill = brush("#DC2626");
			if (bdstatus != null) bdstatus.Background = brush("#7F1D1D");
			if (lbstatus != null) lbstatus.Foreground = brush("#FEE2E2");
			if (lbtotal != null) lbtotal.Foreground = brush("#FECACA");
			if (bdprochd != null) bdprochd.Background = brush("#FEE2E2");
			if (lbprochd != null) {
				lbprochd.Text = "连网进程";
				lbprochd.Foreground = brush("#991B1B");
			}
			if (lbproccap != null) {
				lbproccap.Text = searchActive ? $"抓包中 · 筛选{matchPids.Count}进程" : "抓包中…";
				lbproccap.Foreground = brush("#DC2626");
				lbproccap.FontWeight = FontWeights.Bold;
			}
		} else if (hasCache) {
			bdcap.Background = brush("#FEF3C7");
			bdcap.BorderBrush = brush("#D97706");
			lbcap.Text = $"■ 已结束  总{totalPart}{filtPart}";
			lbcap.Foreground = brush("#92400E");
			dotcap.Fill = brush("#D97706");
			if (bdstatus != null) bdstatus.Background = brush("#1E1B4B");
			if (lbstatus != null) lbstatus.Foreground = brush("#E0E7FF");
			if (lbtotal != null) lbtotal.Foreground = brush("#C7D2FE");
			if (bdprochd != null) bdprochd.Background = brush("#EEF2FF");
			if (lbprochd != null) {
				lbprochd.Text = "连网进程";
				lbprochd.Foreground = brush("#312E81");
			}
			if (lbproccap != null) {
				lbproccap.Text = searchActive ? $"已结束 · 筛选{matchPids.Count}进程" : "抓包已结束（缓存可查看）";
				lbproccap.Foreground = brush("#D97706");
				lbproccap.FontWeight = FontWeights.SemiBold;
			}
		} else {
			bdcap.Background = brush("#E8EEF4");
			bdcap.BorderBrush = brush("#9CA3AF");
			lbcap.Text = searchActive ? $"○ 未抓包{filtPart}" : "○ 未抓包";
			lbcap.Foreground = brush("#374151");
			dotcap.Fill = brush("#6B7280");
			if (bdstatus != null) bdstatus.Background = brush("#1E1B4B");
			if (lbstatus != null) lbstatus.Foreground = brush("#E0E7FF");
			if (lbtotal != null) lbtotal.Foreground = brush("#C7D2FE");
			if (bdprochd != null) bdprochd.Background = brush("#EEF2FF");
			if (lbprochd != null) {
				lbprochd.Text = "连网进程";
				lbprochd.Foreground = brush("#312E81");
			}
			if (lbproccap != null) {
				lbproccap.Text = "";
				lbproccap.FontWeight = FontWeights.Normal;
			}
		}
	}

	static SolidColorBrush brush(string hex) {
		var b = (SolidColorBrush)new BrushConverter().ConvertFrom(hex);
		if (b != null && b.CanFreeze) b.Freeze();
		return b;
	}

	void captureSelection() {
		if (dgproc.SelectedItem is ProcessNetRow p) selProcPid = p.Pid;
		if (dgconn.SelectedItem is ConnectionRow c) selConnKey = c.Key ?? "";
		if (dghosts.SelectedItem is HostRecord h) selHostKey = h.Key ?? "";
	}

	void restoreSelection() {
		if (selProcPid > 0) {
			ProcessNetRow match = null;
			foreach (var item in mon.Processes) {
				if (item.Pid == selProcPid) { match = item; break; }
			}
			if (match != null && !ReferenceEquals(dgproc.SelectedItem, match))
				dgproc.SelectedItem = match;
			if (filterPid != selProcPid) filterPid = selProcPid;
		}

		if (!string.IsNullOrEmpty(selConnKey)) {
			ConnectionRow match = null;
			foreach (var item in mon.Connections) {
				if (item.Key == selConnKey) { match = item; break; }
			}
			if (match != null && !ReferenceEquals(dgconn.SelectedItem, match))
				dgconn.SelectedItem = match;
		}

		if (!string.IsNullOrEmpty(selHostKey)) {
			HostRecord match = null;
			foreach (var item in mon.HostRecords) {
				if (item.Key == selHostKey) { match = item; break; }
			}
			if (match != null && !ReferenceEquals(dghosts.SelectedItem, match))
				dghosts.SelectedItem = match;
		}
	}

	void updateTitles() {
		var hist = mon?.KeepHistory == true ? " · 含已断开" : "";
		var hit = searchActive ? " · 筛选中" : "";
		if (filterPid > 0 && !searchActive) {
			lbconn.Text = $"连接详情（PID {filterPid}{hist}）";
			lbhosts.Text = $"访问过的域名 / IP:端口（PID {filterPid}）";
		} else if (filterPid > 0 && searchActive) {
			lbconn.Text = $"连接详情（PID {filterPid}{hist}{hit}）";
			lbhosts.Text = $"访问过的域名 / IP:端口（PID {filterPid}{hit}）";
		} else {
			lbconn.Text = $"连接详情（全部{hist}{hit}）";
			lbhosts.Text = searchActive ? $"访问过的域名 / IP:端口{hit}" : "访问过的域名 / IP:端口";
		}
	}

	void updateSearchLabel() {
		if (lbsearch == null) return;
		if (!searchActive) {
			lbsearch.Text = "";
			return;
		}
		var cap = GlobalPacketCapture.Instance;
		var sb = new StringBuilder();
		sb.Append($"总抓包 {cap.ConnectionBufferCount}连接/{cap.TotalPacketCount}包");
		sb.Append($"  ·  筛选 {matchConnKeys.Count}连接/{matchPacketCount}包/{matchPids.Count}进程");
		if (!string.IsNullOrEmpty(protoQuery)) sb.Append($"  ·  协议={protoQuery}");
		if (matchHostKeys.Count > 0) sb.Append($"  ·  主机记录 {matchHostKeys.Count}");
		if (matchProcNames.Count > 0) {
			sb.Append("  ·  ");
			sb.Append(string.Join(", ", matchProcNames.Take(8)));
			if (matchProcNames.Count > 8) sb.Append($" 等{matchProcNames.Count}个");
		} else {
			sb.Append("  ·  无匹配（进程/连接/主机均为空）");
		}
		lbsearch.Text = sb.ToString();
	}

	// ---------- 筛选逻辑（边输入边筛选） ----------

	void scheduleLiveSearch() {
		var h = ehostq?.Text?.Trim() ?? "";
		var p = epayq?.Text?.Trim() ?? "";
		var pr = readProtoCombo(cproto);
		if (string.IsNullOrEmpty(h) && string.IsNullOrEmpty(p) && string.IsNullOrEmpty(pr)) {
			if (searchActive) clearSearch();
			return;
		}
		var needThrottle = !string.IsNullOrEmpty(p);
		if (needThrottle && nextLiveSearch != 0 && Environment.TickCount - nextLiveSearch < 150) {
			var due = Environment.TickCount + 150;
			Dispatcher.BeginInvoke(new Action(() => {
				if (Environment.TickCount < due - 20) return;
				liveSearch(false);
			}), System.Windows.Threading.DispatcherPriority.Background);
			return;
		}
		nextLiveSearch = Environment.TickCount;
		liveSearch(false);
	}

	/// <summary>根据输入框内容即时重建筛选结果。</summary>
	void liveSearch(bool fromButton) {
		hostQuery = ehostq?.Text?.Trim() ?? "";
		protoQuery = readProtoCombo(cproto);
		var rawPay = epayq?.Text?.Trim() ?? "";
		payloadHex = cpaymode?.SelectedIndex == 1;
		if (payloadHex && !string.IsNullOrEmpty(rawPay) && !isHexReady(rawPay))
			payloadQuery = "";
		else
			payloadQuery = rawPay;

		if (string.IsNullOrEmpty(hostQuery) && string.IsNullOrEmpty(payloadQuery) && string.IsNullOrEmpty(protoQuery)) {
			if (searchActive) clearSearch();
			else if (fromButton)
				MessageBox.Show(this, "请输入域名/IP、协议或报文内容。", "网络监控",
					MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}

		var wasActive = searchActive;
		rebuildSearchMatches();
		refreshAfterSearch(resetProcSel: fromButton || !wasActive);
		updateCapButtons();
		updateStatusText();
	}

	static bool isHexReady(string q) {
		var cleaned = q.Replace(" ", "").Replace("-", "").Replace(":", "");
		if (cleaned.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) cleaned = cleaned[2..];
		if (cleaned.Length < 2 || (cleaned.Length % 2) != 0) return false;
		foreach (var ch in cleaned) {
			var ok = ch is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
			if (!ok) return false;
		}
		return true;
	}

	void clearSearch() {
		hostQuery = "";
		payloadQuery = "";
		protoQuery = "";
		payloadHex = false;
		searchActive = false;
		matchPids.Clear();
		matchConnKeys.Clear();
		matchHostKeys.Clear();
		matchCapKeys.Clear();
		matchProcNames.Clear();
		matchPacketCount = 0;
		filterPid = -1;
		selProcPid = -1;
		clearHighlights();
		if (ehostq != null && ehostq.Text != "") ehostq.Text = "";
		if (epayq != null && epayq.Text != "") epayq.Text = "";
		if (cproto != null) {
			suppressProtoEvent = true;
			try { cproto.SelectedIndex = 0; } finally { suppressProtoEvent = false; }
		}
		if (lbsearch != null) lbsearch.Text = "";
		restoringSel = true;
		try {
			if (dgproc != null) dgproc.SelectedItem = null;
			procView?.Refresh();
			connView?.Refresh();
			hostView?.Refresh();
		} finally { restoringSel = false; }
		updateTitles();
		updateCapButtons();
		updateStatusText();
	}

	void rebuildSearchMatches() {
		matchPids.Clear();
		matchConnKeys.Clear();
		matchHostKeys.Clear();
		matchCapKeys.Clear();
		matchProcNames.Clear();
		matchPacketCount = 0;
		searchActive = !string.IsNullOrEmpty(hostQuery)
		               || !string.IsNullOrEmpty(payloadQuery)
		               || !string.IsNullOrEmpty(protoQuery);
		if (!searchActive || mon == null) return;

		var hasHost = !string.IsNullOrEmpty(hostQuery);
		var hasPay = !string.IsNullOrEmpty(payloadQuery);
		var hasProto = !string.IsNullOrEmpty(protoQuery);

		HashSet<string> seedKeys = null;
		if (hasPay) seedKeys = GlobalPacketCapture.Instance.FindKeysByPayload(payloadQuery, payloadHex);

		foreach (var c in mon.Connections) {
			if (hasHost && !connMatchesHost(c, hostQuery)) continue;
			if (hasProto && !connMatchesProto(c, protoQuery)) continue;
			if (hasPay) {
				var ck = GlobalPacketCapture.MakeKey(c);
				if (seedKeys == null || !seedKeys.Contains(ck)) continue;
				matchCapKeys.Add(ck);
			} else {
				matchCapKeys.Add(GlobalPacketCapture.MakeKey(c));
			}
			matchConnKeys.Add(c.Key);
			matchPids.Add(c.Pid);
		}

		// 仅报文命中、连接表暂无对应项时，仍计入抓包数
		if (hasPay && seedKeys != null) {
			foreach (var k in seedKeys) matchCapKeys.Add(k);
		}

		foreach (var h in mon.HostRecords) {
			if (hasHost && !hostRecordMatches(h, hostQuery)) continue;
			if (hasProto && !hostMatchesProto(h, protoQuery)) continue;
			if (hasPay && !hasHost && matchPids.Count > 0 && !matchPids.Contains(h.Pid)) continue;
			if (hasPay && !hasHost && matchPids.Count == 0) continue;
			matchHostKeys.Add(h.Key);
			matchPids.Add(h.Pid);
		}

		// 仅协议/域名筛到主机记录、连接表未命中时：把同 PID 且协议匹配的连接补上
		if (!hasPay && matchHostKeys.Count > 0) {
			foreach (var c in mon.Connections) {
				if (!matchPids.Contains(c.Pid)) continue;
				if (hasHost && !connMatchesHost(c, hostQuery)) continue;
				if (hasProto && !connMatchesProto(c, protoQuery)) continue;
				matchConnKeys.Add(c.Key);
				matchCapKeys.Add(GlobalPacketCapture.MakeKey(c));
			}
		}

		matchPacketCount = GlobalPacketCapture.Instance.CountPacketsForKeys(matchCapKeys);

		Dictionary<int, string> pidNames = new();
		foreach (var c in mon.Connections) {
			if (matchPids.Contains(c.Pid) && !string.IsNullOrEmpty(c.ProcessName))
				pidNames[c.Pid] = c.ProcessName;
		}
		foreach (var p in mon.Processes) {
			if (matchPids.Contains(p.Pid) && !string.IsNullOrEmpty(p.ProcessName))
				pidNames[p.Pid] = p.ProcessName;
		}
		foreach (var h in mon.HostRecords) {
			if (matchPids.Contains(h.Pid) && !string.IsNullOrEmpty(h.ProcessName))
				pidNames[h.Pid] = h.ProcessName;
		}
		foreach (var kv in pidNames.OrderBy(x => x.Value))
			matchProcNames.Add($"{kv.Value}({kv.Key})");

		applyHighlights();
	}

	static bool connMatchesHost(ConnectionRow c, string q) {
		if (c == null || string.IsNullOrEmpty(q)) return false;
		return contains(c.RemoteHost, q) || contains(c.RemoteIp, q) ||
		       contains(c.RemoteIpPort, q) || contains(c.RemoteEndPoint, q) ||
		       contains(c.LocalEndPoint, q) || contains(c.LocalIp, q);
	}

	static bool hostRecordMatches(HostRecord h, string q) {
		if (h == null || string.IsNullOrEmpty(q)) return false;
		return contains(h.Host, q) || contains(h.IpPort, q) || contains(h.ProcessName, q);
	}

	/// <summary>协议匹配：支持 HTTP/HTTPS 复合、大小写不敏感、子串（如 http 匹配 HTTP）。</summary>
	static bool connMatchesProto(ConnectionRow c, string q) {
		if (c == null || string.IsNullOrEmpty(q)) return true;
		return protoMatch(c.AppProtocol, c.Transport, q);
	}

	static bool hostMatchesProto(HostRecord h, string q) {
		if (h == null || string.IsNullOrEmpty(q)) return true;
		return protoMatch(h.Protocol, NetProtocol.Unknown, q);
	}

	static bool protoMatch(NetProtocol app, NetProtocol transport, string q) {
		q = q.Trim();
		if (string.IsNullOrEmpty(q)) return true;
		if (q.Contains('/') || q.Contains('|') || q.Contains(',')) {
			var parts = q.Split(new[] { '/', '|', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (var p in parts) {
				if (protoMatchOne(app, transport, p.Trim())) return true;
			}
			return false;
		}
		return protoMatchOne(app, transport, q);
	}

	static bool protoMatchOne(NetProtocol app, NetProtocol transport, string q) {
		if (string.IsNullOrEmpty(q)) return true;
		if (q.Equals("HTTP/HTTPS", StringComparison.OrdinalIgnoreCase) ||
		    q.Equals("HTTP+S", StringComparison.OrdinalIgnoreCase))
			return app is NetProtocol.HTTP or NetProtocol.HTTPS;
		if (q.Equals("HTTP", StringComparison.OrdinalIgnoreCase)) return app == NetProtocol.HTTP;
		if (q.Equals("HTTPS", StringComparison.OrdinalIgnoreCase)) return app == NetProtocol.HTTPS;
		if (q.Equals("QUIC", StringComparison.OrdinalIgnoreCase) ||
		    q.Equals("HTTP3", StringComparison.OrdinalIgnoreCase) ||
		    q.Equals("QUIC/HTTP3", StringComparison.OrdinalIgnoreCase))
			return app == NetProtocol.QUIC;
		var appName = app.ToString();
		var trName = transport.ToString();
		if (appName.Equals(q, StringComparison.OrdinalIgnoreCase)) return true;
		if (trName.Equals(q, StringComparison.OrdinalIgnoreCase)) return true;
		return appName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	static bool contains(string s, string q) =>
		!string.IsNullOrEmpty(s) && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

	void applyHighlights() {
		// 仅更新全局关键词，由 HighlightTextBlock 在单元格内高亮匹配文字
		SearchHighlight.Query = searchActive ? hostQuery : "";
	}

	void clearHighlights() {
		SearchHighlight.Query = "";
		if (mon == null) return;
		foreach (var p in mon.Processes) p.IsHighlight = false;
		foreach (var c in mon.Connections) c.IsHighlight = false;
		foreach (var h in mon.HostRecords) h.IsHighlight = false;
	}

	void refreshAfterSearch(bool resetProcSel = false) {
		restoringSel = true;
		try {
			if (searchActive && resetProcSel) {
				filterPid = -1;
				selProcPid = -1;
				if (dgproc != null) dgproc.SelectedItem = null;
			}
			procView?.Refresh();
			connView?.Refresh();
			hostView?.Refresh();
		} finally { restoringSel = false; }
		updateTitles();
		updateSearchLabel();
	}

	bool filterProc(object obj) {
		if (obj is not ProcessNetRow p) return false;
		if (searchActive) {
			if (matchPids.Count == 0) return false;
			if (!matchPids.Contains(p.Pid)) return false;
		}
		var q = efilter.Text?.Trim() ?? "";
		if (!string.IsNullOrEmpty(q)) {
			var nameOk = p.ProcessName != null &&
			             p.ProcessName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
			var pidOk = p.Pid.ToString().Contains(q);
			if (!nameOk && !pidOk) return false;
		}
		if (onlyActive && p.SendBps + p.RecvBps < 1 && p.ConnectionCount == 0) return false;
		return true;
	}

	bool filterConn(object obj) {
		if (obj is not ConnectionRow c) return false;
		if (searchActive) {
			if (matchConnKeys.Count > 0) {
				if (!matchConnKeys.Contains(c.Key)) return false;
			} else if (matchPids.Count > 0) {
				if (!matchPids.Contains(c.Pid)) return false;
				if (!string.IsNullOrEmpty(hostQuery) && !connMatchesHost(c, hostQuery)) return false;
				if (!string.IsNullOrEmpty(protoQuery) && !connMatchesProto(c, protoQuery)) return false;
			} else {
				return false;
			}
			if (filterPid > 0 && c.Pid != filterPid) return false;
		} else if (filterPid > 0 && c.Pid != filterPid) {
			return false;
		}
		var q = efilter.Text?.Trim() ?? "";
		if (!string.IsNullOrEmpty(q) && !searchActive && filterPid <= 0) {
			if ((c.ProcessName == null || c.ProcessName.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) &&
			    (c.RemoteHost == null || c.RemoteHost.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) &&
			    (c.RemoteEndPoint == null || c.RemoteEndPoint.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) &&
			    (c.RemoteIpPort == null || c.RemoteIpPort.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0))
				return false;
		}
		return true;
	}

	bool filterHost(object obj) {
		if (obj is not HostRecord h) return false;
		if (searchActive) {
			if (matchHostKeys.Count > 0) {
				if (!matchHostKeys.Contains(h.Key)) return false;
			} else if (matchPids.Count > 0) {
				if (!matchPids.Contains(h.Pid)) return false;
				if (!string.IsNullOrEmpty(hostQuery) && !hostRecordMatches(h, hostQuery)) return false;
				if (!string.IsNullOrEmpty(protoQuery) && !hostMatchesProto(h, protoQuery)) return false;
			} else {
				return false;
			}
			if (filterPid > 0 && h.Pid != filterPid) return false;
		} else if (filterPid > 0 && h.Pid != filterPid) {
			return false;
		}
		return true;
	}

	void onprocfilterchanged() {
		restoringSel = true;
		try {
			procView?.Refresh();
			connView?.Refresh();
			restoreSelection();
		} finally { restoringSel = false; }
	}

	void openconnpackets() {
		if (dgconn.SelectedItem is not ConnectionRow c) return;
		try {
			if (c.Transport == NetProtocol.UDP) {
				MessageBox.Show(this, "当前仅支持对 TCP 连接查看报文。", "网络监控",
					MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}
			if (string.IsNullOrEmpty(c.RemoteIp) || c.RemotePort <= 0) {
				MessageBox.Show(this, "该连接没有有效远程地址。", "网络监控",
					MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}
			var win = new NetPacketWindow(c) { Owner = this };
			win.Show();
		} catch (Exception ex) {
			MessageBox.Show(this, ex.Message, "打开报文窗口", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	void bcaptoggle_Click() {
		if (GlobalPacketCapture.Instance.IsRunning) {
			GlobalPacketCapture.Instance.Stop();
			Title = "系统网络监控 【已结束抓包】";
		} else {
			startCaptureForProcesses(null);
		}
		updateCapButtons();
		updateTitles();
		updateStatusText();
	}

	/// <summary>pids 空 = 全部进程。</summary>
	void startCaptureForProcesses(IList<int> pids) {
		var env = GlobalPacketCapture.CheckEnvironment();
		if (env != null) {
			MessageBox.Show(this, env, "网络监控", MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}
		var wasRunning = GlobalPacketCapture.Instance.IsRunning;
		if (pids != null && pids.Count > 0) {
			var keys = collectConnKeysForPids(pids);
			GlobalPacketCapture.Instance.SetProcessFilter(pids, keys);
		} else {
			GlobalPacketCapture.Instance.ClearProcessFilter();
		}
		mon.KeepHistory = true;
		if (!wasRunning) {
			GlobalPacketCapture.Instance.Start();
			if (!GlobalPacketCapture.Instance.IsRunning) {
				MessageBox.Show(this, GlobalPacketCapture.Instance.Status, "网络监控",
					MessageBoxButton.OK, MessageBoxImage.Warning);
				if (!GlobalPacketCapture.Instance.HasSession) mon.KeepHistory = false;
				return;
			}
		}
		// 从停止状态开始抓包：清掉上一轮的报文与已断开连接，列表从头开始
		if (!wasRunning) {
			GlobalPacketCapture.Instance.ClearAll();
			mon.ClearConnectionHistory();
		}
		clearCapListUi(skipExistingCache: true);
		capListFrozen = false;
		capUserSelect = false;
		if (ckcapscroll != null && ckcapscroll.IsChecked != true) {
			capIgnoreScroll = true;
			try { ckcapscroll.IsChecked = true; } finally { capIgnoreScroll = false; }
		}
		Title = pids != null && pids.Count > 0
			? $"系统网络监控 【抓包中 · PID {string.Join(",", pids)}】"
			: "系统网络监控 【抓包中】";
		// 开始抓包就直接切到抓包列表，省得再手动点一次
		if (tabs != null && tabs.SelectedIndex != 1) tabs.SelectedIndex = 1;
	}

	/// <summary>
	/// 清空抓包表格与详情。
	/// skipExistingCache=true 时把尾标记到当前缓存末尾，避免之后增量追加把旧包又加回来。
	/// </summary>
	void clearCapListUi(bool skipExistingCache = false) {
		capSyncing = true;
		try {
			capListView.Clear();
			if (dgcaplist != null) dgcaplist.SelectedItem = null;
		} finally { capSyncing = false; }
		capCurrent = null;
		capPendingSelect = null;
		capItemMap.Clear();
		capFlatFields.Clear();
		if (tvcapproto != null) tvcapproto.Items.Clear();
		if (ecaphex?.Document != null) ecaphex.Document.Blocks.Clear();
		lastCapFilteredCount = 0;
		lastCapFilterKey = "";
		if (skipExistingCache) {
			try {
				var snap = GlobalPacketCapture.Instance.SnapshotAll(12000);
				if (snap.Count > 0) {
					var last = snap[^1];
					lastCapTailId = packetIdentity(last);
					lastCapTailTicks = last.Time.Ticks;
				} else {
					lastCapTailId = "";
					lastCapTailTicks = 0;
				}
			} catch {
				lastCapTailId = "";
				lastCapTailTicks = 0;
			}
		} else {
			lastCapTailId = "";
			lastCapTailTicks = 0;
		}
		if (lbcaplist != null)
			lbcaplist.Text = skipExistingCache ? "已开始 · 等待新报文…" : "已清空";
	}

	List<string> collectConnKeysForPids(IEnumerable<int> pids) {
		List<string> keys = new();
		if (mon?.Connections == null || pids == null) return keys;
		var set = new HashSet<int>(pids);
		foreach (var c in mon.Connections) {
			if (!set.Contains(c.Pid)) continue;
			var k = GlobalPacketCapture.MakeKey(c);
			if (!string.IsNullOrEmpty(k)) keys.Add(k);
		}
		return keys;
	}

	void syncCaptureProcessKeys() {
		var pids = GlobalPacketCapture.Instance.GetFilterPids();
		if (pids == null || pids.Count == 0 || mon?.Connections == null) return;
		GlobalPacketCapture.Instance.MergeAllowedKeys(collectConnKeysForPids(pids));
	}

	void mnproccap_Click() {
		List<int> pids = new();
		if (dgproc?.SelectedItems != null) {
			foreach (var o in dgproc.SelectedItems) {
				if (o is ProcessNetRow p && p.Pid > 0) pids.Add(p.Pid);
			}
		}
		if (pids.Count == 0) {
			MessageBox.Show(this, "请先选中一个或多个进程（Ctrl 多选）。", "抓包",
				MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		startCaptureForProcesses(pids);
		updateCapButtons();
		updateTitles();
		updateStatusText();
	}

	void mnproccapall_Click() {
		startCaptureForProcesses(null);
		updateCapButtons();
		updateTitles();
		updateStatusText();
	}

	void brules_Click() {
		var w = new NetRulesWindow { Owner = this };
		if (w.ShowDialog() == true) {
			restoringSel = true;
			try {
				connView?.Refresh();
				procView?.Refresh();
			} finally { restoringSel = false; }
		}
	}

	static void applyRuleRowColor(DataGridRow row, string tag) {
		if (row == null) return;
		if (tag == "Black")
			row.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xCD, 0xD3)); // 浅红
		else if (tag == "White")
			row.Background = new SolidColorBrush(Color.FromRgb(0xBB, 0xF7, 0xD0)); // 浅绿
		else
			row.ClearValue(DataGridRow.BackgroundProperty);
	}

	void bcapclear_Click() {
		var cap = GlobalPacketCapture.Instance;
		var msg = cap.IsRunning
			? "将清空全部报文缓存与已断开连接历史，并继续抓包。确定？"
			: "将清空全部报文缓存与已断开连接历史。确定？";
		if (MessageBox.Show(this, msg, "清空抓包", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
			return;

		cap.ClearAll();
		mon.ClearConnectionHistory();
		clearCapListUi(skipExistingCache: false);
		if (!cap.IsRunning) {
			mon.KeepHistory = false;
			Title = "系统网络监控";
		}
		if (!string.IsNullOrEmpty(payloadQuery) || !string.IsNullOrEmpty(epayq?.Text)) {
			if (epayq != null) epayq.Text = "";
			payloadQuery = "";
		}
		if (searchActive) {
			if (string.IsNullOrEmpty(hostQuery) && string.IsNullOrEmpty(ehostq?.Text?.Trim()))
				clearSearch();
			else
				liveSearch(false);
		}
		updateCapButtons();
		updateTitles();
		updateStatusText();
	}

	/// <summary>关掉本窗口，用 start 以管理员权限重开一个（只开 --netprobe，不启动主界面）。</summary>
	void reopenelevated() {
		try {
			var exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
			if (string.IsNullOrEmpty(exe)) {
				MessageBox.Show(this, Loc.T("netmon.noexe"), "以管理员身份打开",
					MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}
			// 走 cmd start + -EncodedCommand，避免多层引号把路径切坏
			var script = "Start-Process -FilePath " + psq(exe) + " -ArgumentList '--netprobe' -Verb RunAs";
			var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
			Process.Start(new ProcessStartInfo {
				FileName = "cmd.exe",
				Arguments = "/c start \"ScreenKit\" /b powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded,
				UseShellExecute = false,
				CreateNoWindow = true,
			});
			Close();
		} catch (Exception ex) {
			MessageBox.Show(this, ex.Message, "以管理员身份打开", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	static string psq(string text) => "'" + (text ?? "").Replace("'", "''") + "'";

	void bclearhosts_Click() {
		selHostKey = "";
		mon?.ClearHosts();
	}

	// ---------- 抓包列表 ----------

	void bcaphttp_Click() {
		try {
			if (capListView.Count == 0) {
				MessageBox.Show(this, "列表为空。", "HTTP流", MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}
			List<PacketRow> pkts = new();
			foreach (var p in capListView) pkts.Add(p);
			var list = HttpStreamReassembler.ReassembleBidirectional(pkts);
			if (list.Count == 0) {
				MessageBox.Show(this, "未能重组出 HTTP 明文（可能为 TLS 或筛选结果过杂）。\n建议先按端口/连接筛选，或在连接上双击打开报文窗口再点「HTTP流重组」。",
					"HTTP流", MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}
			var sb = new StringBuilder();
			sb.AppendLine($"共 {list.Count} 条消息（基于当前列表）\n");
			var i = 1;
			foreach (var m in list) {
				sb.AppendLine($"======== #{i++} [{m.Direction}] ========");
				sb.AppendLine(m.FirstLine);
				if (!string.IsNullOrEmpty(m.Headers)) sb.AppendLine(m.Headers);
				if (m.BodyLength > 0) sb.AppendLine($"\n--- Body ({m.BodyLength} B) ---\n{m.BodyPreview}");
				sb.AppendLine();
			}
			shownettext("HTTP 明文流（抓包列表）", sb.ToString());
		} catch (Exception ex) {
			MessageBox.Show(this, ex.Message, "HTTP流", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	/// <summary>只读文本窗口，供 HTTP 流等长文本展示。</summary>
	void shownettext(string title, string body) {
		new Window {
			Title = title,
			Owner = this,
			Width = 760,
			Height = 540,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Background = (Brush)FindResource("BgApp"),
			Content = new TextBox {
				Text = body,
				IsReadOnly = true,
				FontFamily = new FontFamily("Consolas"),
				FontSize = 12,
				TextWrapping = TextWrapping.Wrap,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
				Padding = new Thickness(8)
			}
		}.Show();
	}

	void bcapexport_Click() {
		try {
			// 优先导出当前列表展示内容；为空则导出全部缓存
			List<PacketRow> rows = new();
			if (capListView.Count > 0) {
				foreach (var p in capListView) rows.Add(p);
			} else {
				rows = GlobalPacketCapture.Instance.SnapshotAll(20000);
			}
			var withRaw = 0;
			foreach (var p in rows) {
				if (p.Raw != null && p.Raw.Length > 0) withRaw++;
			}
			if (withRaw == 0) {
				MessageBox.Show(this, "没有可导出的报文（需含原始帧数据）。", "导出 pcapng",
					MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}
			var dlg = new SaveFileDialog {
				Title = "导出 pcapng",
				Filter = "pcapng 文件 (*.pcapng)|*.pcapng|所有文件 (*.*)|*.*",
				FileName = $"netmon_{DateTime.Now:yyyyMMdd_HHmmss}.pcapng",
				DefaultExt = ".pcapng",
				AddExtension = true
			};
			if (dlg.ShowDialog(this) != true) return;
			PcapNgIo.Write(dlg.FileName, rows);
			MessageBox.Show(this, $"已导出 {withRaw} 包\n{dlg.FileName}", "导出 pcapng",
				MessageBoxButton.OK, MessageBoxImage.Information);
		} catch (Exception ex) {
			MessageBox.Show(this, ex.Message, "导出 pcapng", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	void bcapimport_Click() {
		try {
			var dlg = new OpenFileDialog {
				Title = "导入 pcapng / pcap",
				Filter = "抓包文件 (*.pcapng;*.pcap)|*.pcapng;*.pcap|所有文件 (*.*)|*.*",
				CheckFileExists = true
			};
			if (dlg.ShowDialog(this) != true) return;
			var rows = PcapNgIo.Read(dlg.FileName, 20000);
			if (rows.Count == 0) {
				MessageBox.Show(this, "未能从文件中解析出 TCP 报文。", "导入",
					MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}
			var cap = GlobalPacketCapture.Instance;
			foreach (var r in rows) cap.InjectPacket(r);
			// 取消自动滚动，避免列表狂刷；切换到抓包 Tab 并刷新
			if (ckcapscroll != null) {
				capIgnoreScroll = true;
				try { ckcapscroll.IsChecked = false; } finally { capIgnoreScroll = false; }
			}
			if (tabs != null) tabs.SelectedIndex = 1;
			lastCapFilterKey = "";
			lastCapFilteredCount = -1;
			lastCapTailId = "";
			lastCapTailTicks = 0;
			refreshCapList(true);
			updateCapButtons();
			updateStatusText();
			MessageBox.Show(this, $"已导入 {rows.Count} 条 TCP 报文。\n{Path.GetFileName(dlg.FileName)}",
				"导入 pcapng", MessageBoxButton.OK, MessageBoxImage.Information);
		} catch (Exception ex) {
			MessageBox.Show(this, ex.Message, "导入 pcapng", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	// ---------- 统计 ----------

	void refreshStats() {
		if (mon == null) return;
		try {
			var cap = GlobalPacketCapture.Instance;
			var procN = mon.Processes?.Count ?? 0;
			var connN = mon.Connections?.Count ?? 0;
			var hostN = mon.HostRecords?.Count ?? 0;
			var capN = cap.TotalPacketCount;
			var capConn = cap.ConnectionBufferCount;
			var filt = cap.FilterDescription;
			var filtPart = string.IsNullOrEmpty(filt) ? "全部进程" : filt;
			if (lbstatsum != null) {
				lbstatsum.Text =
					$"进程 {procN} · 连接 {connN} · 域名记录 {hostN} · " +
					$"抓包缓存 {capConn}连接/{capN}包（过滤: {filtPart}） · " +
					$"累计发送 {ProcessNetRow.FormatBytes(mon.TotalBytesSent)} / 接收 {ProcessNetRow.FormatBytes(mon.TotalBytesRecv)}";
			}

			List<NetStatRow> topProc = new();
			if (mon.Processes != null) {
				foreach (var p in mon.Processes
					         .OrderByDescending(x => x.BytesSent + x.BytesRecv)
					         .Take(30)) {
					var totalBytes = (double)(p.BytesSent + p.BytesRecv);
					topProc.Add(new NetStatRow {
						Name = p.ProcessName ?? "?",
						Pid = p.Pid,
						Value = totalBytes,
						ValueText = ProcessNetRow.FormatBytes(p.BytesSent + p.BytesRecv),
						Extra = p.CaptureCount.ToString(),
						ExtraValue = p.CaptureCount
					});
				}
			}
			HeatBrushConverter.UpdateFromStatRows(topProc, "Stat.Value", "Stat.Extra");
			replaceStatRows(statTopProc, topProc);

			List<NetStatRow> topHost = new();
			if (mon.HostRecords != null) {
				foreach (var h in mon.HostRecords
					         .OrderByDescending(x => x.HitCount)
					         .Take(30)) {
					topHost.Add(new NetStatRow {
						Name = string.IsNullOrEmpty(h.Host) ? h.IpPort : h.Host,
						Pid = h.Pid,
						Value = h.HitCount,
						ValueText = h.HitCount.ToString(),
						Extra = h.ProcessName ?? ""
					});
				}
			}
			HeatBrushConverter.UpdateFromStatRows(topHost, "Stat.HostValue");
			replaceStatRows(statTopHost, topHost);

			Dictionary<string, int> protoMap = new(StringComparer.OrdinalIgnoreCase);
			if (mon.Connections != null) {
				foreach (var c in mon.Connections) {
					var name = c.AppProtocol.ToString();
					if (string.IsNullOrEmpty(name) || name == "Unknown") name = c.Transport.ToString();
					if (!protoMap.ContainsKey(name)) protoMap[name] = 0;
					protoMap[name]++;
				}
			}
			var totalProto = 0;
			foreach (var n in protoMap.Values) totalProto += n;
			List<NetStatRow> protoRows = new();
			foreach (var kv in protoMap.OrderByDescending(x => x.Value)) {
				var pct = totalProto > 0 ? 100.0 * kv.Value / totalProto : 0;
				protoRows.Add(new NetStatRow {
					Name = kv.Key,
					Value = kv.Value,
					ValueText = kv.Value.ToString(),
					Extra = $"{pct:F1}%"
				});
			}
			HeatBrushConverter.UpdateFromStatRows(protoRows, "Stat.ProtoValue");
			replaceStatRows(statProto, protoRows);
		} catch (Exception ex) {
			if (lbstatsum != null) lbstatsum.Text = "统计失败: " + ex.Message;
		}
	}

	/// <summary>更新统计集合内容但不更换 ItemsSource，保留用户拖过的列宽。</summary>
	static void replaceStatRows(ObservableCollection<NetStatRow> dest, List<NetStatRow> src) {
		if (dest == null) return;
		dest.Clear();
		if (src == null) return;
		foreach (var r in src) dest.Add(r);
	}

	// ---------- 抓包列表刷新 ----------

	void capScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) {
		if (capIgnoreScroll || capScroll == null || ckcapscroll == null) return;
		if (e.VerticalChange == 0) return;
		var atBottom = isScrollAtBottom(capScroll);
		if (atBottom) {
			capListFrozen = false;
			if (ckcapscroll.IsChecked != true) ckcapscroll.IsChecked = true;
		} else {
			capListFrozen = true;
			if (ckcapscroll.IsChecked == true) ckcapscroll.IsChecked = false;
		}
	}

	/// <summary>
	/// 刷新抓包列表。
	/// forceFull=false（自动滚动周期）：只向末尾追加新包，绝不 Clear，避免点选时整表重绘。
	/// forceFull=true（筛选/刷新/切 Tab/勾选自动滚动）：允许整表重建。
	/// </summary>
	void refreshCapList(bool forceFull = false) {
		if (dgcaplist == null || capRefreshing) return;
		if (!forceFull && (capListFrozen || capUserSelect)) return;
		if (!forceFull && ckcapscroll?.IsChecked != true) return;

		capRefreshing = true;
		try {
			var proto = readProtoCombo(ccapproto);
			var hostIpq = ecapip?.Text?.Trim() ?? "";
			var dportq = ecapport?.Text?.Trim() ?? "";
			var lportq = ecaplport?.Text?.Trim() ?? "";
			int? dportNum = null, lportNum = null;
			if (!string.IsNullOrEmpty(dportq) && int.TryParse(dportq, out var dp)) dportNum = dp;
			if (!string.IsNullOrEmpty(lportq) && int.TryParse(lportq, out var lp)) lportNum = lp;
			var filterKey = $"{proto}|{hostIpq}|{dportq}|{lportq}";

			var all = GlobalPacketCapture.Instance.SnapshotAll(12000);
			var total = all.Count;
			List<PacketRow> filtered = new();
			foreach (var p in all) {
				if (!string.IsNullOrEmpty(proto) && !packetMatchesProto(p, proto)) continue;
				if (!string.IsNullOrEmpty(hostIpq) && !packetMatchesHostIp(p, hostIpq)) continue;
				if (dportNum.HasValue) {
					if (p.DstPort != dportNum.Value) continue;
				} else if (!string.IsNullOrEmpty(dportq)) {
					if (!p.DstPort.ToString().Contains(dportq)) continue;
				}
				if (lportNum.HasValue) {
					if (p.SrcPort != lportNum.Value) continue;
				} else if (!string.IsNullOrEmpty(lportq)) {
					if (!p.SrcPort.ToString().Contains(lportq)) continue;
				}
				filtered.Add(p);
			}

			var autoScroll = ckcapscroll?.IsChecked == true;
			var filterChanged = filterKey != lastCapFilterKey;

			// —— 自动滚动周期：仅追加 ——
			if (!forceFull && autoScroll && !filterChanged && capListView.Count > 0) {
				var added = appendCapPackets(filtered);
				lastCapFilterKey = filterKey;
				lastCapFilteredCount = filtered.Count;
				if (lbcaplist != null)
					lbcaplist.Text = $"共 {total} 包 · 筛选显示 {capListView.Count} 包";
				if (added > 0) HeatBrushConverter.UpdateFromPackets(capListView);
				if (added > 0 && autoScroll) scrollCapListToEnd();
				return;
			}

			// —— 全量重建 ——
			string keep = null;
			if (!autoScroll || forceFull) {
				keep = packetIdentity(capPendingSelect)
				       ?? packetIdentity(dgcaplist.SelectedItem as PacketRow)
				       ?? packetIdentity(capCurrent);
			}

			capSyncing = true;
			try {
				capListView.Clear();
				var no = 1;
				PacketRow reselect = null;
				foreach (var p in filtered) {
					var row = clonePacketView(p, no++);
					capListView.Add(row);
					if (keep != null && reselect == null && packetIdentity(row) == keep) reselect = row;
				}
				if (lbcaplist != null)
					lbcaplist.Text = $"共 {total} 包 · 筛选显示 {capListView.Count} 包";

				if (reselect != null) {
					dgcaplist.SelectedItem = reselect;
					capCurrent = reselect;
				}
			} finally { capSyncing = false; }

			rememberCapTail(filtered);
			lastCapFilterKey = filterKey;
			lastCapFilteredCount = filtered.Count;
			HeatBrushConverter.UpdateFromPackets(capListView);

			if (autoScroll && capListView.Count > 0) scrollCapListToEnd();
		} catch (Exception ex) {
			if (lbcaplist != null) lbcaplist.Text = "刷新失败: " + ex.Message;
		} finally {
			capRefreshing = false;
			if (capPendingSelect != null) flushCapPendingSelect();
		}
	}

	/// <summary>在列表末尾追加 filtered 中比当前尾更新的包。返回新增条数。</summary>
	int appendCapPackets(List<PacketRow> filtered) {
		if (filtered == null || filtered.Count == 0) return 0;
		var start = 0;
		if (!string.IsNullOrEmpty(lastCapTailId)) {
			var found = false;
			for (var i = filtered.Count - 1; i >= 0; i--) {
				if (packetIdentity(filtered[i]) == lastCapTailId) {
					start = i + 1;
					found = true;
					break;
				}
			}
			// 缓存滑动导致旧尾不在快照中：按时间戳追加更新的包
			if (!found && lastCapTailTicks > 0) {
				start = filtered.Count;
				for (var i = 0; i < filtered.Count; i++) {
					if (filtered[i].Time.Ticks > lastCapTailTicks) {
						start = i;
						break;
					}
				}
			}
		}

		if (start >= filtered.Count) {
			rememberCapTail(filtered);
			return 0;
		}

		var added = 0;
		var no = capListView.Count + 1;
		capSyncing = true;
		try {
			for (var i = start; i < filtered.Count; i++) {
				capListView.Add(clonePacketView(filtered[i], no++));
				added++;
			}
		} finally { capSyncing = false; }
		rememberCapTail(filtered);
		return added;
	}

	void rememberCapTail(List<PacketRow> filtered) {
		if (capListView.Count > 0) {
			var last = capListView[^1];
			lastCapTailId = packetIdentity(last);
			lastCapTailTicks = last.Time.Ticks;
		} else if (filtered != null && filtered.Count > 0) {
			lastCapTailId = packetIdentity(filtered[^1]);
			lastCapTailTicks = filtered[^1].Time.Ticks;
		} else {
			lastCapTailId = "";
			lastCapTailTicks = 0;
		}
	}

	/// <summary>用于列表刷新后恢复选中的稳定键（非显示序号）。</summary>
	static string packetIdentity(PacketRow p) {
		if (p == null) return null;
		return $"{p.Time.Ticks}|{p.Src}:{p.SrcPort}|{p.Dst}:{p.DstPort}|{p.Seq}|{p.Ack}|{p.Length}|{p.Flags}";
	}

	static bool packetMatchesHostIp(PacketRow p, string q) {
		if (p == null || string.IsNullOrEmpty(q)) return true;
		var sip = p.Src ?? "";
		var dip = p.Dst ?? "";
		if (sip.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (dip.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		try {
			var sh = DnsCache.LookupHost(sip);
			var dh = DnsCache.LookupHost(dip);
			if (!string.IsNullOrEmpty(sh) && sh.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
			if (!string.IsNullOrEmpty(dh) && dh.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		} catch { }
		return false;
	}

	void scrollCapListToEnd() {
		if (capListView.Count == 0 || dgcaplist == null) return;
		capIgnoreScroll = true;
		try {
			var last = capListView[^1];
			dgcaplist.ScrollIntoView(last);
			capScroll?.ScrollToEnd();
		} finally {
			// 勿在此处强制勾回「自动滚动」：用户点选取消后会被抢回，导致整表继续刷
			Dispatcher.BeginInvoke(() => { capIgnoreScroll = false; },
				System.Windows.Threading.DispatcherPriority.Loaded);
		}
	}

	static bool isScrollAtBottom(ScrollViewer sv) {
		if (sv == null) return true;
		if (sv.ScrollableHeight <= 0) return true;
		return sv.VerticalOffset + sv.ViewportHeight >= sv.ExtentHeight - 3.0;
	}

	static ScrollViewer findScrollViewer(DependencyObject root) {
		if (root is ScrollViewer sv) return sv;
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) {
			var child = VisualTreeHelper.GetChild(root, i);
			var found = findScrollViewer(child);
			if (found != null) return found;
		}
		return null;
	}

	static PacketRow clonePacketView(PacketRow p, int no) => new() {
		No = no,
		Time = p.Time,
		Direction = p.Direction,
		Src = p.Src,
		Dst = p.Dst,
		SrcPort = p.SrcPort,
		DstPort = p.DstPort,
		Length = p.Length,
		PayloadLength = p.PayloadLength,
		PayloadOffset = p.PayloadOffset,
		Protocol = p.Protocol,
		Summary = p.Summary,
		Flags = p.Flags,
		Seq = p.Seq,
		Ack = p.Ack,
		Raw = p.Raw,
		Payload = p.Payload,
		Tree = p.Tree,
		ConnKey = p.ConnKey,
		Pid = p.Pid,
		ProcessName = p.ProcessName
	};

	static bool packetMatchesProto(PacketRow p, string q) {
		if (p == null || string.IsNullOrEmpty(q)) return true;
		q = q.Trim();
		if (q.Equals("HTTP/HTTPS", StringComparison.OrdinalIgnoreCase))
			return string.Equals(p.Protocol, "HTTP", StringComparison.OrdinalIgnoreCase)
			       || string.Equals(p.Protocol, "HTTPS", StringComparison.OrdinalIgnoreCase)
			       || string.Equals(p.Protocol, "TLS", StringComparison.OrdinalIgnoreCase);
		if (q.Equals("HTTPS", StringComparison.OrdinalIgnoreCase))
			return string.Equals(p.Protocol, "HTTPS", StringComparison.OrdinalIgnoreCase)
			       || string.Equals(p.Protocol, "TLS", StringComparison.OrdinalIgnoreCase);
		if (q.Equals("QUIC", StringComparison.OrdinalIgnoreCase) ||
		    q.Equals("HTTP3", StringComparison.OrdinalIgnoreCase))
			return string.Equals(p.Protocol, "QUIC", StringComparison.OrdinalIgnoreCase);
		return !string.IsNullOrEmpty(p.Protocol) &&
		       p.Protocol.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	// ---------- 抓包列表选中与详情 ----------

	void oncaplistselected() {
		if (dgcaplist?.SelectedItem is not PacketRow row) return;

		// 程序改选中：用户点选则排队，否则忽略
		if (capSyncing) {
			if (capUserSelect || capListFrozen) capPendingSelect = row;
			return;
		}

		// 选中即冻结（勿在 PreviewMouseDown 里改 CheckBox，会打断 DataGrid 点选手势）
		freezeCapList();

		if (capRefreshing) {
			capPendingSelect = row;
			return;
		}

		// 先让选中行样式落地，再渲染协议树/十六进制（避免卡顿像“点不动”）
		var selected = row;
		Dispatcher.BeginInvoke(() => {
			if (dgcaplist?.SelectedItem is not PacketRow cur) return;
			if (!ReferenceEquals(cur, selected) && packetIdentity(cur) != packetIdentity(selected)) return;
			showCapPacket(cur);
		}, System.Windows.Threading.DispatcherPriority.Background);
	}

	/// <summary>冻结抓包列表：关自动滚动，周期刷新不再改表。</summary>
	void freezeCapList() {
		capListFrozen = true;
		if (ckcapscroll == null || ckcapscroll.IsChecked != true) return;
		capIgnoreScroll = true;
		try { ckcapscroll.IsChecked = false; } finally { capIgnoreScroll = false; }
	}

	void flushCapPendingSelect() {
		var pending = capPendingSelect;
		capPendingSelect = null;
		if (pending == null || dgcaplist == null) return;
		var id = packetIdentity(pending);
		PacketRow match = null;
		if (id != null) {
			foreach (var r in capListView) {
				if (packetIdentity(r) == id) { match = r; break; }
			}
		}
		if (match == null && dgcaplist.SelectedItem is PacketRow sel) match = sel;
		if (match == null) return;
		if (!ReferenceEquals(dgcaplist.SelectedItem, match)) {
			capSyncing = true;
			try { dgcaplist.SelectedItem = match; } finally { capSyncing = false; }
		}
		showCapPacket(match);
	}

	void showCapPacket(PacketRow row) {
		if (row == null) return;
		if (capSyncing) {
			capPendingSelect = row;
			return;
		}
		capSyncing = true;
		try {
			capCurrent = row;
			if (ReferenceEquals(capPendingSelect, row)) capPendingSelect = null;
			capHlStart = -1;
			capHlLen = 0;
			capItemMap.Clear();
			capFlatFields.Clear();
			if (tvcapproto != null) {
				tvcapproto.Items.Clear();
				if (row.Tree != null) {
					foreach (var n in row.Tree) {
						if (n == null) continue;
						var it = buildCapTreeItem(n);
						if (it == null) continue;
						tvcapproto.Items.Add(it);
						collectCapFields(n);
					}
				}
				foreach (var obj in tvcapproto.Items) {
					if (obj is TreeViewItem it) expandCapTree(it, 2);
				}
			}
			renderCapHex();
		} catch (Exception ex) {
			MessageBox.Show(this, ex.Message, "显示报文", MessageBoxButton.OK, MessageBoxImage.Warning);
		} finally { capSyncing = false; }
		if (capPendingSelect != null && !ReferenceEquals(capPendingSelect, row))
			flushCapPendingSelect();
	}

	TreeViewItem buildCapTreeItem(ProtoNode n) {
		if (n == null) return null;
		var item = new TreeViewItem { Header = n.Display ?? "", Tag = n };
		capItemMap[item] = n;
		if (n.Children != null) {
			foreach (var c in n.Children) {
				if (c == null) continue;
				var child = buildCapTreeItem(c);
				if (child != null) item.Items.Add(child);
			}
		}
		return item;
	}

	void expandCapTree(TreeViewItem it, int depth) {
		if (it == null || depth <= 0) return;
		it.IsExpanded = true;
		foreach (var obj in it.Items) {
			if (obj is TreeViewItem c) expandCapTree(c, depth - 1);
		}
	}

	void collectCapFields(ProtoNode n) {
		if (n == null) return;
		if (n.Offset >= 0 && n.Length > 0) capFlatFields.Add(n);
		if (n.Children == null) return;
		foreach (var c in n.Children) collectCapFields(c);
	}

	void oncapfieldselected() {
		if (capSyncing || capCurrent == null) return;
		if (tvcapproto.SelectedItem is not TreeViewItem it || it.Tag is not ProtoNode node) return;
		if (node.Offset < 0 || node.Length <= 0) {
			capHlStart = -1;
			capHlLen = 0;
			renderCapHex();
			return;
		}
		mapCapHighlight(node.Offset, node.Length);
		renderCapHex();
	}

	void mapCapHighlight(int frameOff, int frameLen) {
		if (rcappay?.IsChecked == true) {
			var po = capCurrent.PayloadOffset;
			var start = Math.Max(frameOff, po);
			var end = Math.Min(frameOff + frameLen, po + capCurrent.PayloadLength);
			if (end <= start) { capHlStart = -1; capHlLen = 0; return; }
			capHlStart = start - po;
			capHlLen = end - start;
		} else {
			capHlStart = frameOff;
			capHlLen = frameLen;
		}
	}

	void renderCapHex() {
		if (ecaphex == null) return;
		if (capCurrent == null) {
			ecaphex.Document.Blocks.Clear();
			return;
		}
		var data = rcappay?.IsChecked == true ? capCurrent.Payload : capCurrent.Raw;
		HexViewHelper.Render(ecaphex, data, capHlStart, capHlLen);
	}

	void rcapview_Checked(object sender, RoutedEventArgs e) {
		if (capCurrent == null || ecaphex == null) return;
		if (tvcapproto.SelectedItem is TreeViewItem it && it.Tag is ProtoNode n && n.Offset >= 0)
			mapCapHighlight(n.Offset, n.Length);
		else { capHlStart = -1; capHlLen = 0; }
		renderCapHex();
	}

	void oncaphexclicked() {
		if (capSyncing || capCurrent == null) return;
		try {
			var data = rcappay?.IsChecked == true ? capCurrent.Payload : capCurrent.Raw;
			if (data == null || data.Length == 0) return;
			var caret = ecaphex.CaretPosition;
			if (caret == null) return;
			var range = new TextRange(ecaphex.Document.ContentStart, caret);
			var textBefore = range.Text.Replace("\r", "");
			var full = new TextRange(ecaphex.Document.ContentStart, ecaphex.Document.ContentEnd).Text.Replace("\r", "");
			var bi = HexViewHelper.CharIndexToByte(full, textBefore.Length, data.Length);
			if (bi == null) return;
			var frameOff = rcappay?.IsChecked == true ? capCurrent.PayloadOffset + bi.Value : bi.Value;
			ProtoNode best = null;
			foreach (var n in capFlatFields) {
				if (n.Offset < 0 || n.Length <= 0) continue;
				if (frameOff < n.Offset || frameOff >= n.Offset + n.Length) continue;
				if (best == null || n.Length < best.Length) best = n;
			}
			capHlStart = bi.Value;
			capHlLen = 1;
			if (best != null) {
				mapCapHighlight(best.Offset, best.Length);
				capSyncing = true;
				try {
					foreach (var kv in capItemMap) {
						if (!ReferenceEquals(kv.Value, best)) continue;
						kv.Key.IsSelected = true;
						kv.Key.BringIntoView();
						break;
					}
				} finally { capSyncing = false; }
			}
			renderCapHex();
		} catch { }
	}
}
