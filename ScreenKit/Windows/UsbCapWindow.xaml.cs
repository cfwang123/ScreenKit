using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ScreenKit;

/// <summary>工具 → 网络工具 → USB 抓包。经 USBPcap 看 USB 传输，导入导出 pcapng。</summary>
public partial class UsbCapWindow : Window {
	readonly ObservableCollection<PacketRow> view = new();
	readonly Dictionary<TreeViewItem, ProtoNode> itemMap = new();
	readonly List<ProtoNode> flatFields = new();
	readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(400) };

	List<UsbCapDevice> devs = new();
	Dictionary<string, UsbDev> devmap = new(StringComparer.OrdinalIgnoreCase);
	PacketRow current;
	int shown;
	bool frozen;
	int hlStart = -1;
	int hlLen;
	bool syncing;
	int descgen;
	bool dead;

	public UsbCapWindow() {
		InitializeComponent();
		initbindings();
		Loaded += (_, _) => onloaded();
		Closed += (_, _) => onclosed();
	}

	void initbindings() {
		bcaptoggle.Click += (_, _) => ontoggle();
		bcapclear.Click += (_, _) => onclear();
		bexport.Click += (_, _) => onexport();
		bimport.Click += (_, _) => onimport();
		badmin.Click += (_, _) => reopenelevated();
		brefresh.Click += (_, _) => loadtree();
		bdesc.Click += (_, _) => readdesc();
		bdesccopy.Click += (_, _) => copydesc();
		cdev.SelectionChanged += (_, _) => rebuild(true);
		cxfer.SelectionChanged += (_, _) => rebuild(true);
		eq.TextChanged += (_, _) => rebuild(true);
		ckscroll.Checked += (_, _) => { frozen = false; rebuild(true); scrollend(); };
		ckscroll.Unchecked += (_, _) => frozen = true;
		tvdev.SelectedItemChanged += (_, _) => onpick();
		tvdev.PreviewMouseLeftButtonDown += (_, e) => {
			if (e.OriginalSource is not DependencyObject src) return;
			if (finditem(src) != null) return;
			tvdev.Focus();
			picked = null;
			foreach (var obj in tvdev.Items)
				if (obj is TreeViewItem it) it.IsSelected = false;
			onpick();
		};
		dglist.SelectionChanged += (_, _) => onselected();
		dglist.PreviewMouseLeftButtonDown += (_, _) => {
			frozen = true;
			if (ckscroll.IsChecked == true) ckscroll.IsChecked = false;
		};
		tvproto.SelectedItemChanged += (_, _) => onfield();
		ehex.PreviewMouseLeftButtonUp += (_, _) => onhex();
		rraw.Checked += (_, _) => renderhex();
		rpay.Checked += (_, _) => renderhex();
		timer.Tick += (_, _) => tick();
	}

	void onloaded() {
		dglist.ItemsSource = view;
		loaddevs();
		loadtree();
		var cap = UsbCaptureService.Instance;
		cap.StatusChanged += onstatus;
		lbstatus.Text = cap.Status;
		updatetoggle();
		if (!SysDeviceAdmin.IsAdmin()) badmin.Visibility = Visibility.Visible;
		initdevui();
		timer.Start();
		WindowEsc.Attach(this);
	}

	void onclosed() {
		dead = true;
		timer.Stop();
		var cap = UsbCaptureService.Instance;
		cap.StatusChanged -= onstatus;
		if (cap.IsRunning) cap.Stop();
	}

	UsbTreeNode picked;

	void loadtree() {
		List<UsbTreeNode> roots;
		List<UsbDev> all = null;
		try { all = UsbList.Scan(); }
		catch { }
		try { roots = UsbList.Tree(); }
		catch { roots = new List<UsbTreeNode>(); }
		devmap = new Dictionary<string, UsbDev>(StringComparer.OrdinalIgnoreCase);
		foreach (var d in all ?? new List<UsbDev>()) {
			var id = (d.InstanceId ?? "").Trim();
			if (id.Length > 0 && !devmap.ContainsKey(id)) devmap[id] = d;
		}
		var keep = picked?.InstanceId ?? "";
		tvdev.Items.Clear();
		picked = null;
		foreach (var n in roots) tvdev.Items.Add(treeitem(n, keep));
		onpick();
	}

	TreeViewItem treeitem(UsbTreeNode n, string keep) {
		var label = n.Name;
		if (n.Address >= 0) label += "  · 地址 " + n.Address;
		else if (n.IsHub) label += "  · 集线器";
		if (n.VidPid.Length > 0) label += "  " + n.VidPid;
		var it = new TreeViewItem { Header = label, Tag = n, IsExpanded = true };
		if (keep.Length > 0 && string.Equals(n.InstanceId, keep, StringComparison.OrdinalIgnoreCase)) {
			it.IsSelected = true;
			picked = n;
		}
		foreach (var c in n.Children) it.Items.Add(treeitem(c, keep));
		return it;
	}

	static TreeViewItem finditem(DependencyObject src) {
		while (src != null) {
			if (src is TreeViewItem it) return it;
			src = VisualTreeHelper.GetParent(src);
		}
		return null;
	}

	void onpick() {
		if (tvdev.SelectedItem is TreeViewItem it && it.Tag is UsbTreeNode n) picked = n;
		var cap = UsbCaptureService.Instance;
		showdevdetail();
		// 选设备就看设备：右下角切到详情页，读描述符就在眼前
		if (picked != null && tabdetail != null && tabdesc != null) tabdetail.SelectedItem = tabdesc;
		if (cap.IsRunning) {
			lbstatus.Text = "设备选择在下次开始抓包时生效 · " + cap.Status;
			return;
		}
		lbstatus.Text = picktext();
	}

	// ───────── 设备详情 / 描述符 ─────────

	/// <summary>按钮初始化：读描述符在选中设备后才可用，详情区先给提示。</summary>
	void initdevui() {
		ToolBtnUi.Set(bdesc, ToolBtnUi.Read, "读取描述符");
		ToolBtnUi.Set(bdesccopy, ToolBtnUi.Copy, "复制");
		bdesc.ToolTip = "只读设备的描述符：设备、配置、接口、端点和字符串。不重置设备";
		bdesccopy.ToolTip = "复制右侧的描述符文本";
		showdevdetail();
	}

	UsbDev pickeddev() {
		var id = picked?.InstanceId ?? "";
		return id.Length > 0 && devmap.TryGetValue(id, out var dev) ? dev : null;
	}

	/// <summary>把系统信息里 USB 页的参数字段搬到树的下方；换设备就清掉上次的描述符。</summary>
	void showdevdetail() {
		descgen++;
		var dev = pickeddev();
		var rows = new List<UsbPair>();
		if (picked == null) {
			lbdevtitle.Text = "设备详情";
			lbdevstat.Text = "选择设备后显示";
		}
		else if (dev != null) {
			lbdevtitle.Text = string.IsNullOrWhiteSpace(dev.Name) ? "设备详情" : dev.Name;
			rows = new List<UsbPair>(dev.Props ?? new List<UsbPair>());
			var bits = new List<string>();
			if (!string.IsNullOrWhiteSpace(dev.VidPid)) bits.Add(dev.VidPid);
			if (!string.IsNullOrWhiteSpace(dev.Status)) bits.Add(dev.Status);
			lbdevstat.Text = string.Join(" · ", bits);
		}
		else {
			// 树的节点不一定都能在枚举里找到同名实例：至少给出手上有的信息
			lbdevtitle.Text = string.IsNullOrWhiteSpace(picked.Name) ? "设备详情" : picked.Name;
			if (!string.IsNullOrWhiteSpace(picked.VidPid))
				rows.Add(new UsbPair { Name = "VID:PID", Value = picked.VidPid });
			if (picked.Address >= 0)
				rows.Add(new UsbPair { Name = Loc.T("usb.p.addr"), Value = picked.Address.ToString() });
			if (picked.IsHub)
				rows.Add(new UsbPair { Name = Loc.T("usb.p.cls"), Value = "USB Hub" });
			lbdevstat.Text = "系统枚举里没有对应实例";
		}
		// 抓包过滤用的是总线地址，与系统属性里的「设备地址」可能不同，单独列一行说清楚
		if (picked != null && picked.Address >= 0)
			rows.Insert(0, new UsbPair {
				Name = "抓包地址",
				Value = picked.Address + "（按它过滤报文）",
			});
		icdevprops.ItemsSource = rows;
		svdevprops.ScrollToTop();
		bdesc.IsEnabled = dev != null;
		bdesccopy.IsEnabled = !string.IsNullOrEmpty(edesc.Text);
		edesc.Text = "";
		lbdescstat.Text = dev == null ? "" : "点「读取描述符」查看设备的描述符";
	}

	/// <summary>点「读取描述符」才读，只读所选设备所在的集线器端口。</summary>
	void readdesc() {
		var dev = pickeddev();
		if (dev == null) {
			lbdescstat.Text = "先在左侧选择一台设备";
			return;
		}
		var id = dev.InstanceId;
		var gen = ++descgen;
		setdescbusy(true);
		edesc.Text = "";
		lbdescstat.Text = "正在读取…";
		if (tabdetail != null && tabdesc != null) tabdetail.SelectedItem = tabdesc;
		Task.Run(() => {
			UsbDescDev row = null;
			string err = null;
			try { row = UsbDesc.Read(dev); }
			catch (Exception ex) { err = ex.Message; }
			Dispatcher.BeginInvoke(new Action(() => {
				if (dead || gen != descgen) return;
				setdescbusy(false);
				if (err != null) {
					lbdescstat.Text = "读取失败：" + err;
					return;
				}
				var cur = pickeddev();
				if (cur == null || !string.Equals(cur.InstanceId, id, StringComparison.OrdinalIgnoreCase)) return;
				if (row == null || string.IsNullOrWhiteSpace(row.Text)) {
					lbdescstat.Text = "这台设备没有可读取的描述符";
					return;
				}
				edesc.Text = row.Text;
				edesc.ScrollToHome();
				bdesccopy.IsEnabled = true;
				lbdescstat.Text = "已读取描述符 · 写入描述符总长 " + row.Text.Length + " 字符";
			}));
		});
	}

	void setdescbusy(bool busy) {
		bdesc.IsEnabled = !busy && pickeddev() != null;
		bdesccopy.IsEnabled = !busy && !string.IsNullOrEmpty(edesc.Text);
	}

	void copydesc() {
		var text = edesc.Text;
		if (string.IsNullOrEmpty(text)) return;
		try {
			Clipboard.SetText(text);
			lbdescstat.Text = "已复制描述符";
		}
		catch (Exception ex) {
			lbdescstat.Text = "复制失败：" + ex.Message;
		}
	}

	string picktext() {
		if (picked == null) return "未选择设备 · 抓全部";
		var addrs = picked.Addresses();
		if (addrs.Count == 0) return $"已选 {picked.Name} · 没有可抓的设备地址";
		if (picked.Address >= 0 && picked.Children.Count == 0) return $"已选 {picked.Name} · 只抓地址 {picked.Address}";
		return $"已选 {picked.Name} · 抓 {addrs.Count} 个地址";
	}

	void loaddevs() {
		devs = UsbCaptureService.ListDevices();
		cdev.Items.Clear();
		cdev.Items.Add("全部");
		foreach (var d in devs) cdev.Items.Add(d);
		cdev.SelectedIndex = 0;
	}

	void onstatus() {
		if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(onstatus); return; }
		lbstatus.Text = UsbCaptureService.Instance.Status;
		updatetoggle();
	}

	void tick() {
		var cap = UsbCaptureService.Instance;
		if (!cap.IsRunning && !cap.HasSession) return;
		if (cap.Count == shown) return;
		rebuild(false);
	}

	void ontoggle() {
		var cap = UsbCaptureService.Instance;
		if (cap.IsRunning) {
			cap.Stop();
			lbstatus.Text = cap.Status;
			updatetoggle();
			return;
		}
		var env = UsbCaptureService.CheckEnvironment();
		if (env != null) {
			MessageBox.Show(this, env, "USB 抓包", MessageBoxButton.OK, MessageBoxImage.Warning);
			lbstatus.Text = env;
			return;
		}
		List<UsbCapDevice> pick = new();
		if (cdev.SelectedItem is UsbCapDevice one) pick.Add(one);
		else pick.AddRange(devs);
		if (pick.Count == 0) {
			MessageBox.Show(this, "没有可抓的 USB 根集线器。", "USB 抓包", MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		var addrs = picked?.Addresses();
		if (picked != null && (addrs == null || addrs.Count == 0)) {
			MessageBox.Show(this, "所选设备没有可抓的地址。请选择它下面的具体设备，或点空白处抓全部。",
				"USB 抓包", MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		if (!cap.HasSession) cap.Clear();
		view.Clear();
		shown = 0;
		frozen = false;
		ckscroll.IsChecked = true;
		cap.Start(pick, addrs != null && addrs.Count > 0 ? addrs : null);
		lbstatus.Text = cap.Status;
		updatetoggle();
	}

	void onclear() {
		var cap = UsbCaptureService.Instance;
		var msg = cap.IsRunning ? "将清空已抓报文，并继续抓包。确定？" : "将清空已抓报文。确定？";
		if (MessageBox.Show(this, msg, "清空", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
			return;
		cap.Clear();
		view.Clear();
		shown = 0;
		current = null;
		tvproto.Items.Clear();
		ehex.Document.Blocks.Clear();
		lbcount.Text = "";
		lbstatus.Text = cap.Status;
		updatetoggle();
	}

	void updatetoggle() {
		var run = UsbCaptureService.Instance.IsRunning;
		bcaptoggle.Content = run ? "■ 结束抓包" : "● 开始抓包";
		bdstatus.Background = run
			? new SolidColorBrush(Color.FromRgb(0x7F, 0x1D, 0x1D))
			: new SolidColorBrush(Color.FromRgb(0x1E, 0x1B, 0x4B));
	}

	/// <summary>按当前筛选重建列表。自动滚动时只追加新行，避免整表重绘。</summary>
	void rebuild(bool full) {
		var all = UsbCaptureService.Instance.Snapshot();
		shown = all.Count;
		var want = picked?.Addresses();
		var xfer = cxfer.SelectedIndex;
		var q = eq.Text?.Trim() ?? "";
		List<PacketRow> hit = new();
		foreach (var p in all) {
			if (want != null && want.Count > 0 && !wantaddr(want, p)) continue;
			if (xfer > 0 && p.Protocol != xfername(xfer)) continue;
			if (q.Length > 0 && !contains(p, q)) continue;
			hit.Add(p);
		}
		if (!full && !frozen && view.Count > 0 && view.Count <= hit.Count && hit[0].No == view[0].No) {
			for (var i = view.Count; i < hit.Count; i++) view.Add(hit[i]);
		} else {
			var keep = (dglist.SelectedItem as PacketRow)?.No ?? 0;
			syncing = true;
			try {
				view.Clear();
				foreach (var p in hit) view.Add(p);
				if (keep > 0) {
					foreach (var p in view)
						if (p.No == keep) { dglist.SelectedItem = p; break; }
				}
			} finally { syncing = false; }
		}
		lbcount.Text = $"共 {all.Count} 包 · 显示 {view.Count} 包 · {UsbCaptureService.Instance.DeviceCount} 台设备";
		if (!frozen && ckscroll.IsChecked == true) scrollend();
	}

	static bool wantaddr(List<int> want, PacketRow p) {
		var key = p.ConnKey ?? "";
		var dot = key.IndexOf('.');
		if (dot < 0 || dot + 1 >= key.Length) return false;
		if (!int.TryParse(key.Substring(dot + 1), out var addr)) return false;
		return want.Contains(addr);
	}

	static string xfername(int idx) => idx switch { 1 => "控制", 2 => "批量", 3 => "中断", 4 => "等时", _ => "" };

	static bool contains(PacketRow p, string q) {
		if ((p.Summary ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if ((p.Flags ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if ((p.ConnKey ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (p.Payload == null || p.Payload.Length == 0) return false;
		var n = Math.Min(p.Payload.Length, 512);
		var text = Encoding.ASCII.GetString(p.Payload, 0, n);
		return text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	void scrollend() {
		if (view.Count == 0) return;
		dglist.ScrollIntoView(view[^1]);
	}

	void onselected() {
		if (syncing) return;
		if (dglist.SelectedItem is not PacketRow row) return;
		frozen = true;
		show(row);
	}

	void show(PacketRow row) {
		current = row;
		hlStart = -1;
		hlLen = 0;
		itemMap.Clear();
		flatFields.Clear();
		tvproto.Items.Clear();
		if (row.Tree != null) {
			foreach (var n in row.Tree) {
				if (n == null) continue;
				var it = builditem(n);
				if (it == null) continue;
				tvproto.Items.Add(it);
				collect(n);
			}
			foreach (var obj in tvproto.Items)
				if (obj is TreeViewItem it) it.IsExpanded = true;
		}
		renderhex();
	}

	TreeViewItem builditem(ProtoNode n) {
		if (n == null) return null;
		var it = new TreeViewItem { Header = n.Display ?? "", Tag = n };
		itemMap[it] = n;
		if (n.Children != null) {
			foreach (var c in n.Children) {
				var child = builditem(c);
				if (child != null) it.Items.Add(child);
			}
		}
		return it;
	}

	void collect(ProtoNode n) {
		if (n == null) return;
		if (n.Offset >= 0 && n.Length > 0) flatFields.Add(n);
		if (n.Children == null) return;
		foreach (var c in n.Children) collect(c);
	}

	void onfield() {
		if (current == null) return;
		if (tvproto.SelectedItem is not TreeViewItem it || it.Tag is not ProtoNode n) return;
		if (n.Offset < 0 || n.Length <= 0) { hlStart = -1; hlLen = 0; }
		else highlight(n.Offset, n.Length);
		renderhex();
	}

	void highlight(int off, int len) {
		if (rpay.IsChecked == true) {
			var po = current.PayloadOffset;
			var start = Math.Max(off, po);
			var end = Math.Min(off + len, po + current.PayloadLength);
			if (end <= start) { hlStart = -1; hlLen = 0; return; }
			hlStart = start - po;
			hlLen = end - start;
		} else {
			hlStart = off;
			hlLen = len;
		}
	}

	void renderhex() {
		if (current == null) { ehex.Document.Blocks.Clear(); return; }
		var data = rpay.IsChecked == true ? current.Payload : current.Raw;
		HexViewHelper.Render(ehex, data, hlStart, hlLen);
	}

	void onhex() {
		if (current == null) return;
		try {
			var data = rpay.IsChecked == true ? current.Payload : current.Raw;
			if (data == null || data.Length == 0) return;
			var caret = ehex.CaretPosition;
			if (caret == null) return;
			var before = new TextRange(ehex.Document.ContentStart, caret).Text.Replace("\r", "");
			var full = new TextRange(ehex.Document.ContentStart, ehex.Document.ContentEnd).Text.Replace("\r", "");
			var bi = HexViewHelper.CharIndexToByte(full, before.Length, data.Length);
			if (bi == null) return;
			var frameOff = rpay.IsChecked == true ? current.PayloadOffset + bi.Value : bi.Value;
			ProtoNode best = null;
			foreach (var n in flatFields) {
				if (n.Offset < 0 || n.Length <= 0) continue;
				if (frameOff < n.Offset || frameOff >= n.Offset + n.Length) continue;
				if (best == null || n.Length < best.Length) best = n;
			}
			hlStart = bi.Value;
			hlLen = 1;
			if (best != null) {
				highlight(best.Offset, best.Length);
				foreach (var kv in itemMap) {
					if (!ReferenceEquals(kv.Value, best)) continue;
					kv.Key.IsSelected = true;
					kv.Key.BringIntoView();
					break;
				}
			}
			renderhex();
		} catch { }
	}

	void onexport() {
		try {
			List<PacketRow> rows = new();
			if (view.Count > 0) foreach (var p in view) rows.Add(p);
			else rows = UsbCaptureService.Instance.Snapshot();
			var n = 0;
			foreach (var p in rows)
				if (p.Raw != null && p.Raw.Length > 0) n++;
			if (n == 0) {
				MessageBox.Show(this, "没有可导出的报文。", "导出 pcapng", MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}
			var dlg = new SaveFileDialog {
				Title = "导出 pcapng",
				Filter = "pcapng 文件 (*.pcapng)|*.pcapng|所有文件 (*.*)|*.*",
				FileName = $"usbcap_{DateTime.Now:yyyyMMdd_HHmmss}.pcapng",
				DefaultExt = ".pcapng",
				AddExtension = true
			};
			if (dlg.ShowDialog(this) != true) return;
			PcapNgIo.Write(dlg.FileName, rows, UsbPacketParser.DLT_USBPCAP);
			MessageBox.Show(this, $"已导出 {n} 包\n{dlg.FileName}", "导出 pcapng", MessageBoxButton.OK, MessageBoxImage.Information);
		} catch (Exception ex) {
			MessageBox.Show(this, ex.Message, "导出 pcapng", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	void onimport() {
		try {
			var dlg = new OpenFileDialog {
				Title = "导入 pcapng / pcap",
				Filter = "抓包文件 (*.pcapng;*.pcap)|*.pcapng;*.pcap|所有文件 (*.*)|*.*",
				CheckFileExists = true
			};
			if (dlg.ShowDialog(this) != true) return;
			var rows = PcapNgIo.ReadUsb(dlg.FileName, 20000);
			if (rows.Count == 0) {
				MessageBox.Show(this, "未能从文件中解析出 USB 报文（需要链路类型 USBPcap 249）。", "导入",
					MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}
			var cap = UsbCaptureService.Instance;
			foreach (var r in rows) cap.Inject(r);
			frozen = true;
			ckscroll.IsChecked = false;
			rebuild(true);
			lbstatus.Text = cap.Status;
			MessageBox.Show(this, $"已导入 {rows.Count} 条 USB 报文。\n{Path.GetFileName(dlg.FileName)}",
				"导入", MessageBoxButton.OK, MessageBoxImage.Information);
		} catch (Exception ex) {
			MessageBox.Show(this, ex.Message, "导入", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	/// <summary>关掉本窗口，用 start 以管理员权限重开一个（只开 --usbcap，不启动主界面）。</summary>
	void reopenelevated() {
		try {
			var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
			if (string.IsNullOrEmpty(exe)) {
				MessageBox.Show(this, Loc.T("netmon.noexe"), "以管理员身份打开", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}
			var script = "Start-Process -FilePath " + psq(exe) + " -ArgumentList '--usbcap' -Verb RunAs";
			var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
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
}
