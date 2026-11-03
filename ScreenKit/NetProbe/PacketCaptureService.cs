using System.Collections.ObjectModel;
using System.Net;
using System.Windows.Threading;
using SharpPcap;
using SharpPcap.LibPcap;

namespace ScreenKit;

/// <summary>
/// 全局 TCP 抓包：由用户「开始抓包」启动，记录全部 TCP（不限进程）。
/// 「结束抓包」停止写入但保留缓存；「清空抓包」清除缓存。
/// 双击连接可查看该四元组已保存报文。
/// </summary>
sealed class GlobalPacketCapture : IDisposable {
	public static GlobalPacketCapture Instance { get; } = new();

	readonly object gate = new();
	readonly Dictionary<string, ConnBuffer> buffers = new();
	readonly List<ICaptureDevice> devices = new();
	// key -> 订阅者（PacketWindow）
	readonly Dictionary<string, List<Action<PacketRow>>> subs = new();

	volatile bool stop = true;
	const int MAX_PER_CONN = 1500;
	const int MAX_CONN = 400;

	// 进程过滤：空 = 全部；非空时仅缓存这些连接键（由 UI 根据 PID 的连接表刷新）
	readonly HashSet<string> allowedKeys = new(StringComparer.Ordinal);
	readonly HashSet<int> filterPids = new();
	bool usePidFilter;
	// 连接键 → 进程（监控表周期性推送，供报文标注「进程」列）
	readonly Dictionary<string, (int pid, string name)> connProcess = new(StringComparer.Ordinal);

	public string Status { get; private set; } = "未抓包（点击「开始抓包」）";
	public bool IsRunning { get; private set; }
	/// <summary>曾经开始过抓包且缓存未整体清空（结束抓包后仍为 true，直到清空）。</summary>
	public bool HasSession { get; private set; }
	/// <summary>当前进程过滤（空表示全部进程）。</summary>
	public string FilterDescription {
		get {
			lock (gate) {
				if (!usePidFilter || filterPids.Count == 0) return "";
				return "PID " + string.Join(",", filterPids);
			}
		}
	}
	public int ConnectionBufferCount {
		get { lock (gate) return buffers.Count; }
	}
	public int TotalPacketCount {
		get {
			lock (gate) {
				var n = 0;
				foreach (var b in buffers.Values) n += b.Packets.Count;
				return n;
			}
		}
	}
	public event Action StatusChanged;

	GlobalPacketCapture() { }

	public static string CheckEnvironment() {
		try {
			var list = CaptureDeviceList.Instance;
			if (list == null || list.Count == 0)
				return "未检测到抓包设备。请安装 Npcap（https://npcap.com/），安装时勾选 WinPcap API 兼容。";
			return null;
		} catch (Exception ex) {
			return $"无法初始化抓包库: {ex.Message}\n请安装 Npcap 并以管理员运行。";
		}
	}

	/// <summary>规范化连接键（与方向无关）。</summary>
	public static string MakeKey(string ip1, int port1, string ip2, int port2) {
		ip1 = normIp(ip1);
		ip2 = normIp(ip2);
		var a = $"{ip1}:{port1}";
		var b = $"{ip2}:{port2}";
		return string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
	}

	public static string MakeKey(ConnectionRow c) {
		if (c == null) return "";
		return MakeKey(c.LocalIp, c.LocalPort, c.RemoteIp, c.RemotePort);
	}

	/// <summary>
	/// 设置进程过滤。pids 空/null 清除过滤（抓全部）。
	/// keys 为这些进程当前连接 MakeKey 集合。
	/// </summary>
	public void SetProcessFilter(IEnumerable<int> pids, IEnumerable<string> keys) {
		lock (gate) {
			filterPids.Clear();
			allowedKeys.Clear();
			if (pids != null) {
				foreach (var p in pids) {
					if (p > 0) filterPids.Add(p);
				}
			}
			usePidFilter = filterPids.Count > 0;
			if (usePidFilter && keys != null) {
				foreach (var k in keys) {
					if (!string.IsNullOrEmpty(k)) allowedKeys.Add(k);
				}
			}
		}
	}

	/// <summary>兼容单 PID。</summary>
	public void SetProcessFilter(int pid, IEnumerable<string> keys) {
		if (pid <= 0) SetProcessFilter((IEnumerable<int>)null, null);
		else SetProcessFilter(new[] { pid }, keys);
	}

	/// <summary>在已有进程过滤上合并更多连接键（新建立的连接）。</summary>
	public void MergeAllowedKeys(IEnumerable<string> keys) {
		if (keys == null) return;
		lock (gate) {
			if (!usePidFilter) return;
			foreach (var k in keys) {
				if (!string.IsNullOrEmpty(k)) allowedKeys.Add(k);
			}
		}
	}

	public void ClearProcessFilter() => SetProcessFilter((IEnumerable<int>)null, null);

	public IReadOnlyCollection<int> GetFilterPids() {
		lock (gate) return filterPids.ToArray();
	}

	/// <summary>用当前连接表刷新「连接键→进程」映射，供抓包列表进程列显示。</summary>
	public void UpdateConnProcessMap(IEnumerable<ConnectionRow> conns) {
		if (conns == null) return;
		lock (gate) {
			foreach (var c in conns) {
				if (c == null || c.Pid <= 0) continue;
				var name = c.ProcessName ?? "";
				var k = MakeKey(c);
				if (!string.IsNullOrEmpty(k))
					connProcess[k] = (c.Pid, name);
				// UDP 表常只有本端：按本机 IP:端口 也可反查
				if (!string.IsNullOrEmpty(c.LocalIp) && c.LocalPort > 0)
					connProcess[$"L|{normIp(c.LocalIp)}|{c.LocalPort}"] = (c.Pid, name);
			}
		}
	}

	void applyProcess(PacketRow row, string key) {
		if (row == null) return;
		if (!string.IsNullOrEmpty(key) && connProcess.TryGetValue(key, out var info)) {
			row.Pid = info.pid;
			row.ProcessName = info.name;
			return;
		}
		// 端点作本机端口尝试
		if (tryLocal(row.Src, row.SrcPort, out info) || tryLocal(row.Dst, row.DstPort, out info)) {
			row.Pid = info.pid;
			row.ProcessName = info.name;
		}
	}

	bool tryLocal(string ip, int port, out (int pid, string name) info) {
		info = default;
		if (string.IsNullOrEmpty(ip) || port <= 0) return false;
		return connProcess.TryGetValue($"L|{normIp(ip)}|{port}", out info);
	}

	/// <summary>将抓包缓存中 UDP/QUIC 载荷字节并入进程累计（ESTATS 无 UDP）。</summary>
	public void MergeUdpBytesByProcess(Action<int, long, long> addBytes) {
		if (addBytes == null) return;
		lock (gate) {
			foreach (var kv in buffers) {
				foreach (var p in kv.Value.Packets) {
					var proto = p.Protocol ?? "";
					if (!proto.Equals("UDP", StringComparison.OrdinalIgnoreCase) &&
					    !proto.Equals("QUIC", StringComparison.OrdinalIgnoreCase) &&
					    !proto.Equals("DNS", StringComparison.OrdinalIgnoreCase))
						continue;
					var pid = p.Pid;
					if (pid <= 0) {
						applyProcess(p, kv.Key);
						pid = p.Pid;
					}
					if (pid <= 0) continue;
					var n = p.PayloadLength > 0 ? p.PayloadLength : Math.Max(0, p.Length - 42);
					// 粗分：出站/入站未知时各记一半不合适，记入发送侧作活动量
					if (p.Direction == PacketDirection.In)
						addBytes(pid, 0, n);
					else
						addBytes(pid, n, 0);
				}
			}
		}
	}

	public void Start() {
		if (IsRunning) return;
		var env = CheckEnvironment();
		if (env != null) {
			Status = env;
			IsRunning = false;
			StatusChanged?.Invoke();
			return;
		}

		stop = false;
		try {
			var opened = 0;
			var loopbackOk = false;
			foreach (var dev in CaptureDeviceList.Instance) {
				try {
					if (dev is not LibPcapLiveDevice live) continue;
					var friendly = (live.Interface?.FriendlyName ?? live.Name ?? "");
					var isLoop = friendly.IndexOf("Loopback", StringComparison.OrdinalIgnoreCase) >= 0
					             || friendly.IndexOf("Npcap_Loopback", StringComparison.OrdinalIgnoreCase) >= 0
					             || (live.Name?.IndexOf("Loopback", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;

					live.Open(new DeviceConfiguration {
						Mode = DeviceModes.Promiscuous,
						ReadTimeout = 500
					});
					// TCP+UDP（UDP 供流量统计/QUIC 等）；loopback 务必打开
					try { live.Filter = "tcp or udp"; } catch {
						try { live.Filter = "tcp"; } catch { }
					}
					live.OnPacketArrival += onPacket;
					live.StartCapture();
					devices.Add(live);
					opened++;
					if (isLoop) loopbackOk = true;
				} catch { }
			}

			if (opened == 0) {
				Status = "未能打开任何网卡。请确认 Npcap 已安装且以管理员运行。";
				IsRunning = false;
			} else {
				IsRunning = true;
				HasSession = true;
				var filt = FilterDescription;
				var filtPart = string.IsNullOrEmpty(filt) ? "" : $" · 仅{filt}";
				Status = loopbackOk
					? $"抓包中 · {opened} 网卡(含Loopback){filtPart} · 缓存 {ConnectionBufferCount} 连接 / {TotalPacketCount} 包"
					: $"抓包中 · {opened} 网卡(无Loopback,本机回环可能抓不到){filtPart} · 缓存 {ConnectionBufferCount} 连接 / {TotalPacketCount} 包";
			}
		} catch (Exception ex) {
			Status = $"抓包启动失败: {ex.Message}";
			IsRunning = false;
		}
		StatusChanged?.Invoke();
	}

	/// <summary>结束抓包：停止采集，保留已抓历史报文。</summary>
	public void Stop() {
		stop = true;
		foreach (var d in devices) {
			try {
				d.OnPacketArrival -= onPacket;
				d.StopCapture();
				d.Close();
			} catch { }
		}
		devices.Clear();
		IsRunning = false;
		Status = HasSession || TotalPacketCount > 0
			? $"已结束抓包 · 保留缓存 {ConnectionBufferCount} 连接 / {TotalPacketCount} 包"
			: "未抓包（点击「开始抓包」）";
		StatusChanged?.Invoke();
	}

	/// <summary>取出某连接已缓存报文的快照（副本列表）。</summary>
	public List<PacketRow> Snapshot(string key) {
		lock (gate) {
			if (!buffers.TryGetValue(key, out var buf))
				return new List<PacketRow>();
			return new List<PacketRow>(buf.Packets);
		}
	}

	public int CountFor(string key) {
		lock (gate) {
			return buffers.TryGetValue(key, out var b) ? b.Packets.Count : 0;
		}
	}

	/// <summary>统计若干连接键（MakeKey 格式）下的报文总数。</summary>
	public int CountPacketsForKeys(IEnumerable<string> keys) {
		if (keys == null) return 0;
		var n = 0;
		lock (gate) {
			foreach (var k in keys) {
				if (string.IsNullOrEmpty(k)) continue;
				if (buffers.TryGetValue(k, out var b)) n += b.Packets.Count;
			}
		}
		return n;
	}

	/// <summary>测试/演示：向缓存注入报文（不经过网卡）。</summary>
	public void InjectPacket(PacketRow row) {
		if (row == null) return;
		lock (gate) {
			var key = string.IsNullOrEmpty(row.ConnKey)
				? MakeKey(row.Src ?? "127.0.0.1", row.SrcPort, row.Dst ?? "127.0.0.1", row.DstPort)
				: row.ConnKey;
			if (!buffers.TryGetValue(key, out var buf)) {
				buf = new ConnBuffer();
				buffers[key] = buf;
			}
			row.ConnKey = key;
			if (row.Time == default) row.Time = DateTime.Now;
			if (row.Pid <= 0) applyProcess(row, key);
			buf.LastTick = Environment.TickCount;
			buf.Packets.Add(row);
			while (buf.Packets.Count > MAX_PER_CONN)
				buf.Packets.RemoveAt(0);
			HasSession = true;
		}
	}

	/// <summary>
	/// 全部抓包快照（按时间排序，最多 max 条）。
	/// </summary>
	public List<PacketRow> SnapshotAll(int max = 10000) {
		List<PacketRow> all = new();
		lock (gate) {
			foreach (var kv in buffers) {
				foreach (var p in kv.Value.Packets) {
					if (string.IsNullOrEmpty(p.ConnKey)) p.ConnKey = kv.Key;
					all.Add(p);
				}
			}
		}
		if (all.Count == 0) return all;
		all.Sort((a, b) => {
			var c = a.Time.CompareTo(b.Time);
			return c != 0 ? c : a.No.CompareTo(b.No);
		});
		if (all.Count > max)
			all = all.GetRange(all.Count - max, max);
		return all;
	}

	/// <summary>
	/// 按本机/远端统计抓包缓存中的收发字节与报文数（用于 ESTATS 不可用时的补充）。
	/// </summary>
	public void GetTraffic(string localIp, int localPort, string remoteIp, int remotePort,
		out long bytesOut, out long bytesIn, out long pktsOut, out long pktsIn) {
		bytesOut = bytesIn = pktsOut = pktsIn = 0;
		if (string.IsNullOrEmpty(remoteIp) || remotePort <= 0) return;
		var key = MakeKey(localIp, localPort, remoteIp, remotePort);
		lock (gate) {
			if (!buffers.TryGetValue(key, out var buf)) return;
			foreach (var p in buf.Packets) {
				var isOut = string.Equals(normIp(p.Src), normIp(localIp), StringComparison.OrdinalIgnoreCase)
				            && p.SrcPort == localPort;
				var isIn = string.Equals(normIp(p.Dst), normIp(localIp), StringComparison.OrdinalIgnoreCase)
				           && p.DstPort == localPort;
				// 载荷优先，无载荷时计整帧（握手等）
				var n = p.PayloadLength > 0 ? p.PayloadLength : Math.Max(0, p.Length - 54);
				if (isOut) {
					bytesOut += n;
					pktsOut++;
				} else if (isIn) {
					bytesIn += n;
					pktsIn++;
				} else {
					// 本地 IP 未匹配时按远端端口方向兜底
					if (p.DstPort == remotePort && string.Equals(normIp(p.Dst), normIp(remoteIp), StringComparison.OrdinalIgnoreCase)) {
						bytesOut += n;
						pktsOut++;
					} else {
						bytesIn += n;
						pktsIn++;
					}
				}
			}
		}
	}

	/// <summary>订阅某连接的新报文（实时追加）。</summary>
	public void Subscribe(string key, Action<PacketRow> handler) {
		if (string.IsNullOrEmpty(key) || handler == null) return;
		lock (gate) {
			if (!subs.TryGetValue(key, out var list)) {
				list = new List<Action<PacketRow>>();
				subs[key] = list;
			}
			if (!list.Contains(handler)) list.Add(handler);
		}
	}

	public void Unsubscribe(string key, Action<PacketRow> handler) {
		if (string.IsNullOrEmpty(key) || handler == null) return;
		lock (gate) {
			if (!subs.TryGetValue(key, out var list)) return;
			list.Remove(handler);
			if (list.Count == 0) subs.Remove(key);
		}
	}

	/// <summary>清空某连接的缓存。</summary>
	public void ClearConnection(string key) {
		lock (gate) {
			if (buffers.TryGetValue(key, out var buf))
				buf.Packets.Clear();
		}
	}

	/// <summary>清空全部报文缓存（不自动开始/结束抓包）。</summary>
	public void ClearAll() {
		lock (gate) {
			buffers.Clear();
		}
		if (!IsRunning) {
			HasSession = false;
			Status = "已清空 · 未抓包（点击「开始抓包」）";
		} else {
			Status = $"抓包中 · 缓存已清空 · 继续记录…";
		}
		StatusChanged?.Invoke();
	}

	/// <summary>
	/// 在缓存报文中搜索载荷内容，返回命中的连接键集合。
	/// mode: "utf8" 按文本（忽略大小写）子串；"hex" 按十六进制字节序列。
	/// </summary>
	public HashSet<string> FindKeysByPayload(string query, bool hexMode) {
		HashSet<string> keys = new(StringComparer.Ordinal);
		if (string.IsNullOrWhiteSpace(query)) return keys;

		byte[] needle;
		string textNeedle = null;
		if (hexMode) {
			if (!tryParseHex(query, out needle) || needle.Length == 0) return keys;
		} else {
			needle = null;
			textNeedle = query.Trim();
		}

		lock (gate) {
			foreach (var kv in buffers) {
				foreach (var p in kv.Value.Packets) {
					if (hexMode) {
						if (containsBytes(p.Payload, needle) || containsBytes(p.Raw, needle)) {
							keys.Add(kv.Key);
							break;
						}
					} else {
						if (containsUtf8(p.Payload, textNeedle) || containsUtf8(p.Raw, textNeedle)) {
							keys.Add(kv.Key);
							break;
						}
						// 摘要/解析结果也搜
						if (!string.IsNullOrEmpty(p.Summary) &&
						    p.Summary.IndexOf(textNeedle, StringComparison.OrdinalIgnoreCase) >= 0) {
							keys.Add(kv.Key);
							break;
						}
						if (!string.IsNullOrEmpty(p.Protocol) &&
						    p.Protocol.IndexOf(textNeedle, StringComparison.OrdinalIgnoreCase) >= 0) {
							keys.Add(kv.Key);
							break;
						}
					}
				}
			}
		}
		return keys;
	}

	static bool tryParseHex(string s, out byte[] bytes) {
		bytes = null;
		if (string.IsNullOrWhiteSpace(s)) return false;
		var cleaned = s.Replace(" ", "").Replace("-", "").Replace(":", "").Replace("0x", "").Replace("0X", "");
		if (cleaned.Length % 2 != 0) return false;
		try {
			bytes = new byte[cleaned.Length / 2];
			for (var i = 0; i < bytes.Length; i++)
				bytes[i] = Convert.ToByte(cleaned.Substring(i * 2, 2), 16);
			return true;
		} catch {
			return false;
		}
	}

	static bool containsBytes(byte[] hay, byte[] needle) {
		if (hay == null || needle == null || needle.Length == 0 || hay.Length < needle.Length) return false;
		for (var i = 0; i <= hay.Length - needle.Length; i++) {
			var ok = true;
			for (var j = 0; j < needle.Length; j++) {
				if (hay[i + j] != needle[j]) { ok = false; break; }
			}
			if (ok) return true;
		}
		return false;
	}

	static bool containsUtf8(byte[] data, string needle) {
		if (data == null || data.Length == 0 || string.IsNullOrEmpty(needle)) return false;
		try {
			var s = System.Text.Encoding.UTF8.GetString(data);
			return s.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
		} catch {
			return false;
		}
	}

	void onPacket(object sender, PacketCapture e) {
		if (stop || !IsRunning) return;
		try {
			// SharpPcap 6: GetPacket() 带链路类型；loopback 常为 Null/Raw 而非以太网
			var pkt = e.GetPacket();
			var raw = pkt.Data;
			if (raw == null || raw.Length == 0) return;
			var linkType = (int)pkt.LinkLayerType;
			if (!ProtocolParser.TryParseAnyIp(raw, linkType, out var row)) {
				if (!ProtocolParser.TryParseAnyIp(raw, -1, out row)) return;
			}

			var key = MakeKey(row.Src, row.SrcPort, row.Dst, row.DstPort);
			List<Action<PacketRow>> handlers = null;

			lock (gate) {
				// 进程过滤：仅保留允许的连接键
				if (usePidFilter && !allowedKeys.Contains(key))
					return;

				if (!buffers.TryGetValue(key, out var buf)) {
					// 淘汰最久未更新的连接
					if (buffers.Count >= MAX_CONN) {
						string oldest = null;
						var oldestTick = int.MaxValue;
						foreach (var kv in buffers) {
							if (kv.Value.LastTick < oldestTick && !subs.ContainsKey(kv.Key)) {
								oldestTick = kv.Value.LastTick;
								oldest = kv.Key;
							}
						}
						if (oldest != null) buffers.Remove(oldest);
						else if (buffers.Count >= MAX_CONN) {
							// 全部在订阅中，仍丢掉一个最旧的
							oldest = null;
							oldestTick = int.MaxValue;
							foreach (var kv in buffers) {
								if (kv.Value.LastTick < oldestTick) {
									oldestTick = kv.Value.LastTick;
									oldest = kv.Key;
								}
							}
							if (oldest != null) buffers.Remove(oldest);
						}
					}
					buf = new ConnBuffer();
					buffers[key] = buf;
				}
				buf.LastTick = Environment.TickCount;
				row.ConnKey = key;
				applyProcess(row, key);
				buf.Packets.Add(row);
				while (buf.Packets.Count > MAX_PER_CONN)
					buf.Packets.RemoveAt(0);

				if (subs.TryGetValue(key, out var list) && list.Count > 0)
					handlers = new List<Action<PacketRow>>(list);
			}

			if (handlers != null) {
				foreach (var h in handlers) {
					try { h(row); } catch { }
				}
			}
		} catch { }
	}

	/// <summary>刷新状态文案（供 UI 轮询）。</summary>
	public void RefreshStatusText() {
		var filt = FilterDescription;
		var filtPart = string.IsNullOrEmpty(filt) ? "" : $" · 仅{filt}";
		if (IsRunning)
			Status = $"抓包中{filtPart} · 缓存 {ConnectionBufferCount} 连接 / {TotalPacketCount} 包";
		else if (TotalPacketCount > 0 || ConnectionBufferCount > 0)
			Status = $"已结束抓包{filtPart} · 保留缓存 {ConnectionBufferCount} 连接 / {TotalPacketCount} 包";
		// 无缓存时保持现有 Status（如未启动 / 已清空）
	}

	/// <summary>按连接键批量统计报文数（PID 抓包数聚合用）。</summary>
	public Dictionary<string, int> CountByKeys(IEnumerable<string> keys) {
		Dictionary<string, int> map = new(StringComparer.Ordinal);
		if (keys == null) return map;
		lock (gate) {
			foreach (var k in keys) {
				if (string.IsNullOrEmpty(k)) continue;
				if (buffers.TryGetValue(k, out var b))
					map[k] = b.Packets.Count;
			}
		}
		return map;
	}

	public void Dispose() {
		Stop();
		lock (gate) {
			buffers.Clear();
			subs.Clear();
		}
		HasSession = false;
	}

	static string normIp(string ip) {
		if (string.IsNullOrEmpty(ip)) return "";
		if (IPAddress.TryParse(ip, out var a)) return a.ToString();
		return ip;
	}

	sealed class ConnBuffer {
		public List<PacketRow> Packets = new();
		public int LastTick;
	}
}

/// <summary>
/// 某个连接详情窗口的视图模型：加载历史缓存 + 订阅实时追加。
/// 不再单独开网卡，避免重复抓包。
/// </summary>
sealed class PacketSession : IDisposable {
	readonly Dispatcher ui;
	readonly string key;
	readonly string localIp;
	readonly int localPort;
	readonly Action<PacketRow> handler;
	int nextNo = 1;
	bool paused;
	bool disposed;

	public ObservableCollection<PacketRow> Packets { get; } = new();
	public string Status { get; private set; }
	public event Action StatusChanged;

	public PacketSession(Dispatcher dispatcher, ConnectionRow conn) {
		ui = dispatcher;
		localIp = conn.LocalIp ?? "";
		localPort = conn.LocalPort;
		if (localIp is "0.0.0.0" or "::") localIp = "";
		key = GlobalPacketCapture.MakeKey(conn);
		handler = onNew;

		// 先装载历史
		var hist = GlobalPacketCapture.Instance.Snapshot(key);
		foreach (var p in hist) {
			ProtocolParser.ApplyDirection(p, localIp, localPort);
			p.No = nextNo++;
			Packets.Add(p);
		}

		var g = GlobalPacketCapture.Instance;
		if (g.IsRunning) {
			Status = $"已加载历史 {Packets.Count} 条 · 实时追加中 · {localIp}:{localPort} ↔ {conn.RemoteIp}:{conn.RemotePort}";
			g.Subscribe(key, handler);
		} else if (Packets.Count > 0) {
			Status = $"已加载历史 {Packets.Count} 条 · 抓包已结束（仅查看缓存）";
		} else {
			Status = "无缓存报文。请先「开始抓包」，产生流量后再打开。";
		}
	}

	void onNew(PacketRow row) {
		if (paused || disposed) return;
		// 复制一份方向字段（原对象可能被多窗口共用）
		ProtocolParser.ApplyDirection(row, localIp, localPort);
		ui.BeginInvoke(() => {
			if (disposed || paused) return;
			// 避免重复：若已在末尾同一 raw 引用则跳过
			if (Packets.Count > 0 && ReferenceEquals(Packets[^1].Raw, row.Raw)) return;
			row.No = nextNo++;
			Packets.Add(row);
			while (Packets.Count > 5000)
				Packets.RemoveAt(0);
		});
	}

	public void SetPaused(bool value) {
		paused = value;
		Status = paused
			? $"已暂停显示（后台仍在缓存）· 当前 {Packets.Count} 条"
			: $"实时追加中 · 当前 {Packets.Count} 条";
		StatusChanged?.Invoke();
	}

	public void Clear() {
		GlobalPacketCapture.Instance.ClearConnection(key);
		Packets.Clear();
		nextNo = 1;
		Status = "已清空本连接缓存";
		StatusChanged?.Invoke();
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		GlobalPacketCapture.Instance.Unsubscribe(key, handler);
	}
}
