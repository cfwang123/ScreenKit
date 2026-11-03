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
	PacketRow current;
	int shown;
	bool frozen;
	int hlStart = -1;
	int hlLen;
	bool syncing;

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
		cdev.SelectionChanged += (_, _) => rebuild(true);
		edev.TextChanged += (_, _) => rebuild(true);
		cxfer.SelectionChanged += (_, _) => rebuild(true);
		eq.TextChanged += (_, _) => rebuild(true);
		ckscroll.Checked += (_, _) => { frozen = false; rebuild(true); scrollend(); };
		ckscroll.Unchecked += (_, _) => frozen = true;
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
		var cap = UsbCaptureService.Instance;
		cap.StatusChanged += onstatus;
		lbstatus.Text = cap.Status;
		updatetoggle();
		if (!SysDeviceAdmin.IsAdmin()) badmin.Visibility = Visibility.Visible;
		timer.Start();
		WindowEsc.Attach(this);
	}

	void onclosed() {
		timer.Stop();
		var cap = UsbCaptureService.Instance;
		cap.StatusChanged -= onstatus;
		if (cap.IsRunning) cap.Stop();
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
		if (!cap.HasSession) cap.Clear();
		view.Clear();
		shown = 0;
		frozen = false;
		ckscroll.IsChecked = true;
		cap.Start(pick);
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
		var dev = edev.Text?.Trim() ?? "";
		var xfer = cxfer.SelectedIndex;
		var q = eq.Text?.Trim() ?? "";
		List<PacketRow> hit = new();
		foreach (var p in all) {
			if (dev.Length > 0 && (p.ConnKey ?? "").IndexOf(dev, StringComparison.OrdinalIgnoreCase) < 0) continue;
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
