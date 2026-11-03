using System.Collections.Specialized;
using System.Windows.Documents;
using System.Windows.Controls.Primitives;

namespace ScreenKit;

/// <summary>某条 TCP 连接的报文窗口：装载已缓存报文并实时追加。</summary>
public partial class NetPacketWindow : Window {
	PacketSession session;
	ConnectionRow connRow;
	PacketRow current;
	bool paused;
	bool syncing;
	bool ignoreScroll;
	bool ready;
	ScrollViewer pktScroll;
	int hlStart = -1;
	int hlLen;
	readonly Dictionary<TreeViewItem, ProtoNode> itemMap = new();
	readonly List<ProtoNode> flatFields = new();

	public NetPacketWindow(ConnectionRow conn) {
		InitializeComponent();
		connRow = conn;
		initbindings();
		lbtitle.Text =
			$"{conn.ProcessName} (PID {conn.Pid})  ·  {conn.LocalEndPoint}  ↔  {conn.RemoteEndPoint}  ·  {conn.AppProtocol}";

		session = new PacketSession(Dispatcher, conn);
		dgpkts.ItemsSource = session.Packets;
		lbstatus.Text = session.Status;
		session.StatusChanged += () => {
			try { Dispatcher.BeginInvoke(() => lbstatus.Text = session.Status); } catch { }
		};
		session.Packets.CollectionChanged += packets_CollectionChanged;

		// 在 session 就绪后再绑定自动滚动，避免 XAML IsChecked 初始化时 NRE
		ckscroll.IsChecked = true;
		ready = true;

		Loaded += (_, _) => {
			try {
				if (session != null && session.Packets.Count > 0 && ckscroll.IsChecked == true)
					scrollToLatest(select: true);
				lbstatus.Text = session?.Status;
			} catch { }
		};

		Closed += (_, _) => {
			try {
				ready = false;
				if (session != null) session.Packets.CollectionChanged -= packets_CollectionChanged;
				if (pktScroll != null) pktScroll.ScrollChanged -= pktScroll_ScrollChanged;
				session?.Dispose();
			} catch { }
		};
	}

	void initbindings() {
		ckscroll.Checked += (_, _) => {
			if (!ready || ckscroll == null || session == null) return;
			if (ckscroll.IsChecked == true) scrollToLatest(select: false);
		};
		ckscroll.Unchecked += (_, _) => {
			if (!ready || ckscroll == null || session == null) return;
			if (ckscroll.IsChecked == true) scrollToLatest(select: false);
		};
		dgpkts.Loaded += (_, _) => {
			if (pktScroll != null) return;
			pktScroll = findScrollViewer(dgpkts);
			if (pktScroll != null) pktScroll.ScrollChanged += pktScroll_ScrollChanged;
		};
		dgpkts.SelectionChanged += (_, _) => {
			if (dgpkts.SelectedItem is PacketRow row) showPacket(row);
		};
		dgpkts.MouseDoubleClick += (_, _) => {
			if (dgpkts.SelectedItem is PacketRow row) showPacket(row);
		};
		bhttp.Click += (_, _) => onhttp();
		bpause.Click += (_, _) => {
			paused = !paused;
			session.SetPaused(paused);
			bpause.Content = paused ? "继续" : "暂停";
			lbstatus.Text = session.Status;
		};
		bclear.Click += (_, _) => {
			session.Clear();
			tvproto.Items.Clear();
			ehex.Document.Blocks.Clear();
			current = null;
			itemMap.Clear();
			flatFields.Clear();
			lbstatus.Text = session.Status;
			lbfield.Text = "已清空";
		};
		tvproto.SelectedItemChanged += (_, _) => onfieldselected();
		ehex.PreviewMouseLeftButtonUp += (_, _) => onhexclicked();
		rdraw.Checked += (_, _) => onviewchanged();
		rdpayload.Checked += (_, _) => onviewchanged();
		WindowEsc.Attach(this);
	}

	void packets_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) {
		if (!ready) return;
		if (e.Action != NotifyCollectionChangedAction.Add) return;
		if (ckscroll?.IsChecked != true) return;
		Dispatcher.BeginInvoke(() => {
			if (ready) scrollToLatest(select: true);
		}, System.Windows.Threading.DispatcherPriority.Background);
	}

	void pktScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) {
		if (ignoreScroll || pktScroll == null || ckscroll == null) return;
		if (e.VerticalChange == 0 && e.ExtentHeightChange == 0) return;

		var atBottom = isAtBottom(pktScroll);
		if (atBottom) {
			if (ckscroll.IsChecked != true) ckscroll.IsChecked = true;
		} else {
			// 离开底部：自动取消（Extent 因新增行变高时若仍贴底则 isAtBottom 为 true）
			if (e.ExtentHeightChange != 0 && isAtBottom(pktScroll)) {
				if (ckscroll.IsChecked != true) ckscroll.IsChecked = true;
				return;
			}
			if (ckscroll.IsChecked == true && e.VerticalChange != 0) ckscroll.IsChecked = false;
		}
	}

	void scrollToLatest(bool select) {
		if (!ready || session == null || dgpkts == null) return;
		if (session.Packets.Count == 0) return;
		var last = session.Packets[^1];
		ignoreScroll = true;
		try {
			if (select) dgpkts.SelectedItem = last;
			dgpkts.ScrollIntoView(last);
			pktScroll?.ScrollToEnd();
		} finally {
			Dispatcher.BeginInvoke(() => {
				ignoreScroll = false;
				if (ready && ckscroll != null && isAtBottom(pktScroll)) ckscroll.IsChecked = true;
			}, System.Windows.Threading.DispatcherPriority.Loaded);
		}
	}

	static bool isAtBottom(ScrollViewer sv) {
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

	void onhttp() {
		try {
			if (session == null || session.Packets.Count == 0) {
				MessageBox.Show(this, "暂无报文。", "HTTP流重组", MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}
			var list = HttpStreamReassembler.Reassemble(
				session.Packets.ToList(),
				connRow?.LocalIp ?? "",
				connRow?.LocalPort ?? 0);
			if (list.Count == 0) {
				MessageBox.Show(this, "未能重组出 HTTP 明文消息（可能为 TLS 加密或无载荷）。",
					"HTTP流重组", MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}
			var sb = new System.Text.StringBuilder();
			sb.AppendLine($"共 {list.Count} 条 HTTP 消息\n");
			var i = 1;
			foreach (var m in list) {
				sb.AppendLine($"======== #{i++} [{m.Direction}] ========");
				sb.AppendLine(m.FirstLine);
				if (!string.IsNullOrEmpty(m.Headers)) sb.AppendLine(m.Headers);
				if (m.BodyLength > 0) sb.AppendLine($"\n--- Body ({m.BodyLength} B) ---\n{m.BodyPreview}");
				sb.AppendLine();
			}
			var win = new Window {
				Title = "HTTP 明文流",
				Owner = this,
				Width = 760,
				Height = 540,
				WindowStartupLocation = WindowStartupLocation.CenterOwner,
				Background = (Brush)FindResource("BgApp"),
				Content = new TextBox {
					Text = sb.ToString(),
					IsReadOnly = true,
					FontFamily = new FontFamily("Consolas"),
					FontSize = 12,
					TextWrapping = TextWrapping.Wrap,
					VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
					HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
					Padding = new Thickness(8)
				}
			};
			win.Show();
		} catch (Exception ex) {
			MessageBox.Show(this, ex.Message, "HTTP流重组", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	void showPacket(PacketRow row) {
		try {
			current = row;
			hlStart = -1;
			hlLen = 0;
			itemMap.Clear();
			flatFields.Clear();
			tvproto.Items.Clear();
			if (row.Tree != null) {
				foreach (var n in row.Tree) {
					var it = buildItem(n);
					tvproto.Items.Add(it);
					collectFields(n);
				}
			}
			foreach (TreeViewItem it in tvproto.Items) {
				it.IsExpanded = true;
				expandChildren(it, 2);
			}
			renderHex();
			lbfield.Text = $"帧长 {row.Length} 字节 · 载荷 {row.PayloadLength} 字节 @ offset {row.PayloadOffset} · 点击字段或十六进制联动";
		} catch (Exception ex) {
			MessageBox.Show(this, ex.Message, "显示报文详情", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	void expandChildren(TreeViewItem it, int depth) {
		if (depth <= 0) return;
		foreach (TreeViewItem c in it.Items) {
			c.IsExpanded = true;
			expandChildren(c, depth - 1);
		}
	}

	void collectFields(ProtoNode n) {
		if (n.Offset >= 0 && n.Length > 0) flatFields.Add(n);
		if (n.Children == null) return;
		foreach (var c in n.Children) collectFields(c);
	}

	TreeViewItem buildItem(ProtoNode n) {
		var item = new TreeViewItem {
			Header = n.Display,
			IsExpanded = false,
			Tag = n
		};
		itemMap[item] = n;
		if (n.Children != null) {
			foreach (var c in n.Children) item.Items.Add(buildItem(c));
		}
		return item;
	}

	void onfieldselected() {
		if (syncing || current == null) return;
		if (tvproto.SelectedItem is not TreeViewItem it) return;
		if (it.Tag is not ProtoNode node) return;
		if (node.Offset < 0 || node.Length <= 0) {
			hlStart = -1;
			hlLen = 0;
			renderHex();
			lbfield.Text = node.Display + " （无对应字节范围）";
			return;
		}
		mapHighlight(node.Offset, node.Length);
		renderHex();
		scrollHexToByte(hlStart);
		lbfield.Text = $"{node.Display}  →  视图字节 [{hlStart}..{hlStart + Math.Max(0, hlLen - 1)}]";
	}

	void mapHighlight(int frameOff, int frameLen) {
		if (rdpayload.IsChecked == true) {
			var po = current.PayloadOffset;
			var start = Math.Max(frameOff, po);
			var end = Math.Min(frameOff + frameLen, po + current.PayloadLength);
			if (end <= start) {
				hlStart = -1;
				hlLen = 0;
				return;
			}
			hlStart = start - po;
			hlLen = end - start;
		} else {
			hlStart = frameOff;
			hlLen = frameLen;
		}
	}

	void renderHex() {
		if (ehex == null) return;
		if (current == null) {
			ehex.Document.Blocks.Clear();
			return;
		}
		var data = rdpayload.IsChecked == true ? current.Payload : current.Raw;
		HexViewHelper.Render(ehex, data, hlStart, hlLen);
	}

	void scrollHexToByte(int byteIndex) {
		if (byteIndex < 0 || ehex.Document.Blocks.Count == 0) return;
		try {
			var line = byteIndex / 16;
			ehex.ScrollToVerticalOffset(line * 18);
		} catch { }
	}

	void onhexclicked() {
		if (syncing || current == null) return;
		try {
			var data = rdpayload.IsChecked == true ? current.Payload : current.Raw;
			if (data == null || data.Length == 0) return;

			var caret = ehex.CaretPosition;
			if (caret == null) return;
			var range = new TextRange(ehex.Document.ContentStart, caret);
			var textBefore = range.Text.Replace("\r", "");
			var charIndex = textBefore.Length;
			var full = new TextRange(ehex.Document.ContentStart, ehex.Document.ContentEnd).Text.Replace("\r", "");
			var bi = HexViewHelper.CharIndexToByte(full, charIndex, data.Length);
			if (bi == null) return;

			var frameOff = rdpayload.IsChecked == true ? current.PayloadOffset + bi.Value : bi.Value;

			// 找最贴合的字段（长度最短且覆盖）
			ProtoNode best = null;
			foreach (var n in flatFields) {
				if (n.Offset < 0 || n.Length <= 0) continue;
				if (frameOff < n.Offset || frameOff >= n.Offset + n.Length) continue;
				if (best == null || n.Length < best.Length) best = n;
			}

			hlStart = bi.Value;
			hlLen = 1;
			if (best != null) {
				mapHighlight(best.Offset, best.Length);
				selectTreeNode(best);
				lbfield.Text = $"字节 {frameOff} (视图#{bi.Value}) → {best.Display}";
			} else {
				renderHex();
				lbfield.Text = $"字节 {frameOff} (视图#{bi.Value}) — 无对应解析字段";
			}
			renderHex();
			scrollHexToByte(hlStart);
		} catch { }
	}

	void selectTreeNode(ProtoNode target) {
		syncing = true;
		try {
			foreach (var kv in itemMap) {
				if (!ReferenceEquals(kv.Value, target)) continue;
				expandPath(kv.Key);
				kv.Key.IsSelected = true;
				kv.Key.BringIntoView();
				break;
			}
		} finally { syncing = false; }
	}

	void expandPath(TreeViewItem item) {
		var p = ItemsControl.ItemsControlFromItemContainer(item) as TreeViewItem;
		while (p != null) {
			p.IsExpanded = true;
			p = ItemsControl.ItemsControlFromItemContainer(p) as TreeViewItem;
		}
	}

	void onviewchanged() {
		if (current == null || ehex == null) return;
		if (tvproto.SelectedItem is TreeViewItem it && it.Tag is ProtoNode n && n.Offset >= 0)
			mapHighlight(n.Offset, n.Length);
		else {
			hlStart = -1;
			hlLen = 0;
		}
		renderHex();
	}
}
