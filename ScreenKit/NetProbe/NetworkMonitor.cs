using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>
/// 后台轮询 TCP/UDP 连接表与 ESTATS，聚合进程流量并记录域名。
/// 单后台线程 + TickCount 节流，UI 通过 Dispatcher 更新。
/// </summary>
sealed class NetworkMonitor : IDisposable {
	readonly Dispatcher ui;
	readonly Thread worker;
	volatile bool stop;

	// 连接累计（ESTATS 绝对值与增量）
	readonly Dictionary<string, ConnTrack> tracks = new();
	// 已断开但仍展示的历史连接（仅 TCP 有远端）
	readonly Dictionary<string, ConnTrack> history = new();
	// 进程累计（连接关闭后仍保留历史字节）
	readonly Dictionary<int, ProcAccum> procAccum = new();
	// 主机记录
	readonly Dictionary<string, HostRecord> hosts = new();

	int lastsample;
	long totalsent, totalrecv;
	double totalsendbps, totalrecvbps;
	volatile bool keepHistory;

	public ObservableCollection<ProcessNetRow> Processes { get; } = new();
	public ObservableCollection<ConnectionRow> Connections { get; } = new();
	public ObservableCollection<HostRecord> HostRecords { get; } = new();

	public int ProcessCount { get; private set; }
	public int ConnectionCount { get; private set; }
	public long TotalBytesSent => totalsent;
	public long TotalBytesRecv => totalrecv;
	public double TotalSendBps => totalsendbps;
	public double TotalRecvBps => totalrecvbps;
	public string StatusText { get; private set; } = "初始化…";
	/// <summary>
	/// 为 true 时保留已断开 TCP 连接行（抓包开始后启用；清空抓包时关闭并清空）。
	/// </summary>
	public bool KeepHistory {
		get => keepHistory;
		set {
			keepHistory = value;
			if (!value) {
				lock (history) history.Clear();
			}
		}
	}

	/// <summary>仅清空已断开连接历史，不改 KeepHistory 开关。</summary>
	public void ClearConnectionHistory() {
		lock (history) history.Clear();
	}

	public event Action Updated;

	public NetworkMonitor(Dispatcher dispatcher) {
		ui = dispatcher;
		worker = new Thread(loop) {
			IsBackground = true,
			Name = "ScreenKit.NetMonitor"
		};
	}

	public void Start() {
		lastsample = Environment.TickCount;
		worker.Start();
	}

	public void ClearHosts() {
		lock (hosts) hosts.Clear();
		ui.BeginInvoke(() => {
			HostRecords.Clear();
			Updated?.Invoke();
		});
	}

	public void Dispose() {
		stop = true;
		if (worker.IsAlive) worker.Join(2000);
	}

	void loop() {
		while (!stop) {
			try {
				sample();
			} catch (Exception ex) {
				StatusText = $"采样错误: {ex.Message}";
			}
			Thread.Sleep(500);
		}
	}

	void sample() {
		var now = Environment.TickCount;
		var elapsed = now - lastsample;
		if (elapsed < 200) return;
		var secs = Math.Max(elapsed / 1000.0, 0.2);
		lastsample = now;

		DnsCache.RefreshSystemDnsCache();

		Dictionary<string, ConnSnap> snaps = new();
		readTcp4(snaps, now);
		readTcp6(snaps, now);
		readUdp4(snaps, now);
		readUdp6(snaps, now);

		// 更新 tracks 速率 / 累计
		HashSet<string> alive = new(snaps.Keys);
		List<string> dead = new();
		foreach (var kv in tracks) {
			if (!alive.Contains(kv.Key)) dead.Add(kv.Key);
		}
		foreach (var k in dead) {
			var t = tracks[k];
			// 关闭瞬间再合并一次 ESTATS 快照与抓包统计
			if (t.Snap != null) {
				if (t.Snap.EstatsOk) {
					t.BytesSent = Math.Max(t.BytesSent, t.Snap.BytesSent);
					t.BytesRecv = Math.Max(t.BytesRecv, t.Snap.BytesRecv);
					t.PacketsSent = Math.Max(t.PacketsSent, t.Snap.PacketsSent);
					t.PacketsRecv = Math.Max(t.PacketsRecv, t.Snap.PacketsRecv);
				}
				mergeCaptureTraffic(t, t.Snap);
			}
			// 连接结束：把累计并入进程历史
			accumProcess(t.Pid, t.BytesSent, t.BytesRecv, t.PacketsSent, t.PacketsRecv);
			// 可选：保留已断开连接供查看
			if (keepHistory && t.IsTcp && t.Snap != null &&
			    !string.IsNullOrEmpty(t.Snap.RemoteIp) && t.Snap.RemotePort > 0) {
				t.IsHistory = true;
				t.SendBps = 0;
				t.RecvBps = 0;
				t.ClosedTick = now;
				lock (history) {
					history[k] = t;
					if (history.Count > 2000) {
						var oldest = history.Values.OrderBy(x => x.ClosedTick).Take(history.Count - 1800).Select(x => x.Key).ToList();
						foreach (var ok in oldest) history.Remove(ok);
					}
				}
			}
			tracks.Remove(k);
		}

		long tickSent = 0, tickRecv = 0;
		foreach (var kv in snaps) {
			var s = kv.Value;
			// 若重新出现，从历史中移除
			lock (history) history.Remove(kv.Key);
			if (!tracks.TryGetValue(kv.Key, out var t)) {
				// 新连接：基线对齐；若当次 ESTATS 已有累计值，先记入（短连接可能只有一次采样）
				t = new ConnTrack {
					Key = kv.Key,
					Pid = s.Pid,
					IsTcp = s.IsTcp,
					IsIpv6 = s.IsIpv6,
					EstatsEnabled = s.EstatsOk,
					LastBytesSent = s.BytesSent,
					LastBytesRecv = s.BytesRecv,
					LastPktsSent = s.PacketsSent,
					LastPktsRecv = s.PacketsRecv,
					// 首采样即有 ESTATS 绝对值时计入，避免短连接关闭后全是 0
					BytesSent = s.EstatsOk ? s.BytesSent : 0,
					BytesRecv = s.EstatsOk ? s.BytesRecv : 0,
					PacketsSent = s.EstatsOk ? s.PacketsSent : 0,
					PacketsRecv = s.EstatsOk ? s.PacketsRecv : 0,
					IsHistory = false
				};
				tracks[kv.Key] = t;
			} else {
				// 增量（ESTATS 为连接级绝对值）
				long dSent = 0, dRecv = 0, dPs = 0, dPr = 0;
				if (s.EstatsOk) {
					if (!t.EstatsEnabled) {
						// 首次拿到 ESTATS：用当前绝对值覆盖（短连接友好）
						t.BytesSent = Math.Max(t.BytesSent, s.BytesSent);
						t.BytesRecv = Math.Max(t.BytesRecv, s.BytesRecv);
						t.PacketsSent = Math.Max(t.PacketsSent, s.PacketsSent);
						t.PacketsRecv = Math.Max(t.PacketsRecv, s.PacketsRecv);
						t.LastBytesSent = s.BytesSent;
						t.LastBytesRecv = s.BytesRecv;
						t.LastPktsSent = s.PacketsSent;
						t.LastPktsRecv = s.PacketsRecv;
						t.EstatsEnabled = true;
					} else {
						dSent = s.BytesSent >= t.LastBytesSent ? s.BytesSent - t.LastBytesSent : 0;
						dRecv = s.BytesRecv >= t.LastBytesRecv ? s.BytesRecv - t.LastBytesRecv : 0;
						dPs = s.PacketsSent >= t.LastPktsSent ? s.PacketsSent - t.LastPktsSent : 0;
						dPr = s.PacketsRecv >= t.LastPktsRecv ? s.PacketsRecv - t.LastPktsRecv : 0;
						t.LastBytesSent = s.BytesSent;
						t.LastBytesRecv = s.BytesRecv;
						t.LastPktsSent = s.PacketsSent;
						t.LastPktsRecv = s.PacketsRecv;
						t.BytesSent += dSent;
						t.BytesRecv += dRecv;
						t.PacketsSent += dPs;
						t.PacketsRecv += dPr;
					}
				}
				t.SendBps = dSent / secs;
				t.RecvBps = dRecv / secs;
				tickSent += dSent;
				tickRecv += dRecv;
			}

			t.Snap = s;
			t.LastSeenTick = now;
			// 抓包缓存补充（短连接 / ESTATS 失败时仍能显示流量）
			mergeCaptureTraffic(t, s);

			// 主机记录（有远端的）
			if (!string.IsNullOrEmpty(s.RemoteIp) && s.RemotePort > 0 && s.State != ConnState.Listen) {
				recordHost(s, t);
			}
		}

		totalsendbps = tickSent / secs;
		totalrecvbps = tickRecv / secs;

		// 聚合进程
		Dictionary<int, ProcessNetRow> procMap = new();
		foreach (var t in tracks.Values) {
			var s = t.Snap;
			if (s == null) continue;
			if (!procMap.TryGetValue(t.Pid, out var pr)) {
				var (name, path) = ProcessResolver.Resolve(t.Pid);
				pr = new ProcessNetRow {
					Pid = t.Pid,
					ProcessName = name,
					ProcessPath = path
				};
				procMap[t.Pid] = pr;
				// 叠加历史累计
				if (procAccum.TryGetValue(t.Pid, out var acc)) {
					pr.BytesSent = acc.BytesSent;
					pr.BytesRecv = acc.BytesRecv;
					pr.PacketsSent = acc.PacketsSent;
					pr.PacketsRecv = acc.PacketsRecv;
				}
			}
			pr.ConnectionCount++;
			pr.BytesSent += t.BytesSent;
			pr.BytesRecv += t.BytesRecv;
			pr.PacketsSent += t.PacketsSent;
			pr.PacketsRecv += t.PacketsRecv;
			pr.SendBps += t.SendBps;
			pr.RecvBps += t.RecvBps;
			pr.LastSeenTick = now;
		}

		// 仅有历史无活动连接的进程也展示
		foreach (var kv in procAccum) {
			if (procMap.ContainsKey(kv.Key)) continue;
			if (now - kv.Value.LastSeenTick > 30000) continue;
			var (name, path) = ProcessResolver.Resolve(kv.Key);
			procMap[kv.Key] = new ProcessNetRow {
				Pid = kv.Key,
				ProcessName = name,
				ProcessPath = path,
				BytesSent = kv.Value.BytesSent,
				BytesRecv = kv.Value.BytesRecv,
				PacketsSent = kv.Value.PacketsSent,
				PacketsRecv = kv.Value.PacketsRecv,
				LastSeenTick = kv.Value.LastSeenTick
			};
		}

		totalsent = 0;
		totalrecv = 0;
		foreach (var p in procMap.Values) {
			totalsent += p.BytesSent;
			totalrecv += p.BytesRecv;
		}

		// 连接列表：活动连接 + 可选历史断开连接
		List<ConnectionRow> connList = new();
		void addTrack(ConnTrack t, bool hist) {
			var s = t.Snap;
			if (s == null) return;
			if (!hist && s.State == ConnState.Listen) return;
			if (string.IsNullOrEmpty(s.RemoteIp) || s.RemotePort <= 0) return;
			// 展示前再合并抓包统计
			if (!hist) mergeCaptureTraffic(t, s);
			var (name, path) = ProcessResolver.Resolve(t.Pid);
			var host = DnsCache.LookupHost(s.RemoteIp);
			var ipPort = $"{s.RemoteIp}:{s.RemotePort}";
			var capKey = GlobalPacketCapture.MakeKey(s.LocalIp, s.LocalPort, s.RemoteIp, s.RemotePort);
			var capCnt = 0;
			try { capCnt = GlobalPacketCapture.Instance.CountFor(capKey); } catch { }
			connList.Add(new ConnectionRow {
				Key = t.Key,
				Pid = t.Pid,
				ProcessName = name,
				ProcessPath = path,
				Transport = s.Transport,
				AppProtocol = s.AppProtocol,
				LocalIp = s.LocalIp,
				LocalPort = s.LocalPort,
				RemoteIp = s.RemoteIp,
				RemotePort = s.RemotePort,
				LocalEndPoint = $"{s.LocalIp}:{s.LocalPort}",
				RemoteEndPoint = DnsCache.FormatRemote(s.RemoteIp, s.RemotePort),
				RemoteIpPort = ipPort,
				RemoteHost = string.IsNullOrEmpty(host) ? "" : host,
				State = hist ? ConnState.Closed : s.State,
				BytesSent = t.BytesSent,
				BytesRecv = t.BytesRecv,
				PacketsSent = t.PacketsSent,
				PacketsRecv = t.PacketsRecv,
				SendBps = hist ? 0 : t.SendBps,
				RecvBps = hist ? 0 : t.RecvBps,
				LastSeenTick = t.LastSeenTick,
				IsIpv6 = t.IsIpv6,
				CaptureCount = capCnt,
				IsHistory = hist
			});
		}
		foreach (var t in tracks.Values) addTrack(t, false);
		if (keepHistory) {
			lock (history) {
				foreach (var t in history.Values) addTrack(t, true);
			}
		}

		// 进程抓包数：汇总其连接的 CaptureCount
		Dictionary<int, int> capByPid = new();
		foreach (var c in connList) {
			if (!capByPid.ContainsKey(c.Pid)) capByPid[c.Pid] = 0;
			capByPid[c.Pid] += c.CaptureCount;
		}
		foreach (var kv in procMap) {
			kv.Value.CaptureCount = capByPid.TryGetValue(kv.Key, out var n) ? n : 0;
		}
		// UDP/QUIC：抓包缓存字节并入进程（无 ESTATS 时的补充，工作量小于完整 ETW）
		try {
			GlobalPacketCapture.Instance.MergeUdpBytesByProcess((pid, sent, recv) => {
				if (!procMap.TryGetValue(pid, out var pr)) return;
				pr.BytesSent += sent;
				pr.BytesRecv += recv;
			});
		} catch { }

		// 规则：忽略 LISTEN
		var rules = RuleStore.Instance;
		if (rules.IgnoreListen) {
			connList = connList.Where(c => c.State != ConnState.Listen).ToList();
		}
		// 规则着色
		foreach (var c in connList)
			c.RuleTag = rules.MatchColor(c.ProcessName, c.RemoteIp);
		foreach (var p in procMap.Values) {
			p.RuleTag = rules.MatchColor(p.ProcessName, null);
			RateHistory.Instance.Push(p.Pid, p.SendBps, p.RecvBps);
			RateHistory.Instance.Get(p.Pid, out var sh, out var rh);
			p.SendHistory = sh;
			p.RecvHistory = rh;
		}
		try {
			var alivePids = new HashSet<int>(procMap.Keys);
			RateHistory.Instance.Prune(alivePids);
		} catch { }

		// 排序
		var procList = procMap.Values
			.OrderByDescending(p => p.SendBps + p.RecvBps)
			.ThenByDescending(p => p.BytesSent + p.BytesRecv)
			.ThenBy(p => p.ProcessName)
			.ToList();
		connList = connList
			.OrderByDescending(c => c.SendBps + c.RecvBps)
			.ThenByDescending(c => c.BytesSent + c.BytesRecv)
			.ToList();

		List<HostRecord> hostList;
		lock (hosts) {
			hostList = hosts.Values
				.OrderByDescending(h => h.LastSeen)
				.Take(500)
				.ToList();
		}

		ProcessCount = procList.Count;
		ConnectionCount = connList.Count;
		StatusText = $"监控中 · 进程 {ProcessCount} · 连接 {ConnectionCount} · 发送 {ProcessNetRow.FormatRate(totalsendbps)} · 接收 {ProcessNetRow.FormatRate(totalrecvbps)}";

		ui.BeginInvoke(() => publish(procList, connList, hostList));
	}

	void publish(List<ProcessNetRow> procs, List<ConnectionRow> conns, List<HostRecord> hostList) {
		// 仅数值变化时 Notify，避免每拍全表重绑导致卡顿
		syncCollection(Processes, procs, p => p.Pid.ToString(), (old, neu) => {
			var ch = false;
			if (old.ProcessName != neu.ProcessName) { old.ProcessName = neu.ProcessName; ch = true; }
			if (old.ProcessPath != neu.ProcessPath) { old.ProcessPath = neu.ProcessPath; ch = true; }
			if (old.ConnectionCount != neu.ConnectionCount) { old.ConnectionCount = neu.ConnectionCount; ch = true; }
			if (old.BytesSent != neu.BytesSent) { old.BytesSent = neu.BytesSent; ch = true; }
			if (old.BytesRecv != neu.BytesRecv) { old.BytesRecv = neu.BytesRecv; ch = true; }
			if (old.PacketsSent != neu.PacketsSent) { old.PacketsSent = neu.PacketsSent; ch = true; }
			if (old.PacketsRecv != neu.PacketsRecv) { old.PacketsRecv = neu.PacketsRecv; ch = true; }
			if (!near(old.SendBps, neu.SendBps)) { old.SendBps = neu.SendBps; ch = true; }
			if (!near(old.RecvBps, neu.RecvBps)) { old.RecvBps = neu.RecvBps; ch = true; }
			if (old.CaptureCount != neu.CaptureCount) { old.CaptureCount = neu.CaptureCount; ch = true; }
			if (old.RuleTag != neu.RuleTag) { old.RuleTag = neu.RuleTag; ch = true; }
			// 历史数组每次换新引用
			old.SendHistory = neu.SendHistory;
			old.RecvHistory = neu.RecvHistory;
			old.LastSeenTick = neu.LastSeenTick;
			if (ch) old.NotifyAll();
			else {
				// sparkline 仍需刷新
				old.NotifyAll();
			}
		});
		syncCollection(Connections, conns, c => c.Key, (old, neu) => {
			var ch = false;
			if (old.ProcessName != neu.ProcessName) { old.ProcessName = neu.ProcessName; ch = true; }
			if (old.AppProtocol != neu.AppProtocol) { old.AppProtocol = neu.AppProtocol; ch = true; }
			if (old.LocalIp != neu.LocalIp) { old.LocalIp = neu.LocalIp; ch = true; }
			if (old.LocalPort != neu.LocalPort) { old.LocalPort = neu.LocalPort; ch = true; }
			if (old.RemoteIp != neu.RemoteIp) { old.RemoteIp = neu.RemoteIp; ch = true; }
			if (old.RemotePort != neu.RemotePort) { old.RemotePort = neu.RemotePort; ch = true; }
			if (old.LocalEndPoint != neu.LocalEndPoint) { old.LocalEndPoint = neu.LocalEndPoint; ch = true; }
			if (old.RemoteEndPoint != neu.RemoteEndPoint) { old.RemoteEndPoint = neu.RemoteEndPoint; ch = true; }
			if (old.RemoteIpPort != neu.RemoteIpPort) { old.RemoteIpPort = neu.RemoteIpPort; ch = true; }
			if (old.RemoteHost != neu.RemoteHost) { old.RemoteHost = neu.RemoteHost; ch = true; }
			if (old.State != neu.State) { old.State = neu.State; ch = true; }
			if (old.BytesSent != neu.BytesSent) { old.BytesSent = neu.BytesSent; ch = true; }
			if (old.BytesRecv != neu.BytesRecv) { old.BytesRecv = neu.BytesRecv; ch = true; }
			if (old.PacketsSent != neu.PacketsSent) { old.PacketsSent = neu.PacketsSent; ch = true; }
			if (old.PacketsRecv != neu.PacketsRecv) { old.PacketsRecv = neu.PacketsRecv; ch = true; }
			if (!near(old.SendBps, neu.SendBps)) { old.SendBps = neu.SendBps; ch = true; }
			if (!near(old.RecvBps, neu.RecvBps)) { old.RecvBps = neu.RecvBps; ch = true; }
			if (old.IsHistory != neu.IsHistory) { old.IsHistory = neu.IsHistory; ch = true; }
			if (old.IsIpv6 != neu.IsIpv6) { old.IsIpv6 = neu.IsIpv6; ch = true; }
			if (old.CaptureCount != neu.CaptureCount) { old.CaptureCount = neu.CaptureCount; ch = true; }
			if (old.RuleTag != neu.RuleTag) { old.RuleTag = neu.RuleTag; ch = true; }
			if (ch) old.NotifyAll();
		});
		syncCollection(HostRecords, hostList, h => h.Key, (old, neu) => {
			var ch = false;
			if (old.Host != neu.Host) { old.Host = neu.Host; ch = true; }
			if (old.HitCount != neu.HitCount) { old.HitCount = neu.HitCount; ch = true; }
			if (old.BytesSent != neu.BytesSent) { old.BytesSent = neu.BytesSent; ch = true; }
			if (old.BytesRecv != neu.BytesRecv) { old.BytesRecv = neu.BytesRecv; ch = true; }
			if (old.LastSeen != neu.LastSeen) { old.LastSeen = neu.LastSeen; ch = true; }
			if (old.Protocol != neu.Protocol) { old.Protocol = neu.Protocol; ch = true; }
			if (ch) old.NotifyAll();
		});
		Updated?.Invoke();
	}

	static bool near(double a, double b) => Math.Abs(a - b) < 0.5;

	/// <summary>
	/// 就地更新集合：同一 key 复用对象引用，避免整表重建导致选中丢失。
	/// 不 Move 重排，以免打乱用户列排序与当前选中行。
	/// </summary>
	static void syncCollection<T>(ObservableCollection<T> col, List<T> src, Func<T, string> keyOf, Action<T, T> update) {
		Dictionary<string, T> map = new();
		foreach (var x in src) map[keyOf(x)] = x;

		// 删除已消失项
		for (var i = col.Count - 1; i >= 0; i--) {
			if (!map.ContainsKey(keyOf(col[i])))
				col.RemoveAt(i);
		}

		// 更新已有 / 追加新增（保持对象引用稳定）
		Dictionary<string, int> idx = new();
		for (var i = 0; i < col.Count; i++)
			idx[keyOf(col[i])] = i;

		foreach (var n in src) {
			var k = keyOf(n);
			if (idx.TryGetValue(k, out var i))
				update(col[i], n);
			else {
				col.Add(n);
				idx[k] = col.Count - 1;
			}
		}
	}

	/// <summary>用抓包缓存中的字节/报文补齐 ESTATS 缺口（取较大值，避免重复累加）。</summary>
	static void mergeCaptureTraffic(ConnTrack t, ConnSnap s) {
		if (s == null || string.IsNullOrEmpty(s.RemoteIp)) return;
		try {
			GlobalPacketCapture.Instance.GetTraffic(
				s.LocalIp, s.LocalPort, s.RemoteIp, s.RemotePort,
				out var bo, out var bi, out var po, out var pi);
			if (bo > t.BytesSent) t.BytesSent = bo;
			if (bi > t.BytesRecv) t.BytesRecv = bi;
			if (po > t.PacketsSent) t.PacketsSent = po;
			if (pi > t.PacketsRecv) t.PacketsRecv = pi;
		} catch { /* 抓包未启动等 */ }
	}

	void accumProcess(int pid, long sent, long recv, long ps, long pr) {
		if (!procAccum.TryGetValue(pid, out var a)) {
			a = new ProcAccum();
			procAccum[pid] = a;
		}
		a.BytesSent += sent;
		a.BytesRecv += recv;
		a.PacketsSent += ps;
		a.PacketsRecv += pr;
		a.LastSeenTick = Environment.TickCount;
	}

	void recordHost(ConnSnap s, ConnTrack t) {
		// 优先域名；尚未解析完时先显示 IP，解析到后刷新为域名
		var host = DnsCache.FormatHostOrIp(s.RemoteIp);
		var ipPort = $"{s.RemoteIp}:{s.RemotePort}";
		var key = $"{t.Pid}|{ipPort}|{s.AppProtocol}";
		lock (hosts) {
			if (!hosts.TryGetValue(key, out var h)) {
				var (name, _) = ProcessResolver.Resolve(t.Pid);
				h = new HostRecord {
					Key = key,
					Pid = t.Pid,
					ProcessName = name,
					Host = host,
					IpPort = ipPort,
					Protocol = s.AppProtocol,
					HitCount = 1,
					FirstSeen = DateTime.Now,
					LastSeen = DateTime.Now
				};
				hosts[key] = h;
			} else {
				h.LastSeen = DateTime.Now;
				// DNS 异步返回后把 IP 升级为域名
				if (!string.IsNullOrEmpty(host))
					h.Host = host;
			}
			h.BytesSent = t.BytesSent;
			h.BytesRecv = t.BytesRecv;
		}
	}

	/// <summary>ESTATS 只做统计用；没启用时返回的是垃圾数据，超过 1 PB 一律不认。</summary>
	static bool plausible(long v) => v >= 0 && v < (1L << 50);

	void readTcp4(Dictionary<string, ConnSnap> snaps, int now) {
		foreach (var row in IpHelper.ReadTcp4()) {
			var lport = IpHelper.Ntohs(row.LocalPort);
			var rport = IpHelper.Ntohs(row.RemotePort);
			var lip = IpHelper.FormatIpv4(row.LocalAddr);
			var rip = IpHelper.FormatIpv4(row.RemoteAddr);
			var state = (ConnState)row.State;
			var pid = (int)row.OwningPid;
			var key = $"TCP4|{pid}|{lip}:{lport}|{rip}:{rport}";
			long bs = 0, br = 0, ps = 0, pr = 0;
			var estatsOk = false;
			if (state == ConnState.Established) {
				// 必须先 Enable 再 Get；非管理员时 Enable 失败，Get 的数字不可信
				if (IpHelper.TryEnableEstats4(row) && IpHelper.TryGetEstats4(row, out var rod)) {
					bs = (long)rod.DataBytesOut;
					br = (long)rod.DataBytesIn;
					ps = (long)rod.DataSegsOut;
					pr = (long)rod.DataSegsIn;
					estatsOk = plausible(bs) && plausible(br) && plausible(ps) && plausible(pr);
					if (!estatsOk) bs = br = ps = pr = 0;
				}
			}
			var app = ProtocolClassifier.Classify(NetProtocol.TCP, rport, lport);
			snaps[key] = new ConnSnap {
				Key = key,
				Pid = pid,
				IsTcp = true,
				IsIpv6 = false,
				Transport = NetProtocol.TCP,
				AppProtocol = app,
				LocalIp = lip,
				LocalPort = lport,
				RemoteIp = state == ConnState.Listen ? "" : rip,
				RemotePort = state == ConnState.Listen ? 0 : rport,
				State = state,
				BytesSent = bs,
				BytesRecv = br,
				PacketsSent = ps,
				PacketsRecv = pr,
				EstatsOk = estatsOk,
				Tcp4 = row
			};
		}
	}

	void readTcp6(Dictionary<string, ConnSnap> snaps, int now) {
		foreach (var row in IpHelper.ReadTcp6()) {
			var lport = IpHelper.Ntohs(row.LocalPort);
			var rport = IpHelper.Ntohs(row.RemotePort);
			var lip = IpHelper.FormatIpv6(row.LocalAddr);
			var rip = IpHelper.FormatIpv6(row.RemoteAddr);
			var state = (ConnState)row.State;
			var pid = (int)row.OwningPid;
			var key = $"TCP6|{pid}|[{lip}]:{lport}|[{rip}]:{rport}";
			long bs = 0, br = 0, ps = 0, pr = 0;
			var estatsOk = false;
			if (state == ConnState.Established) {
				if (IpHelper.TryEnableEstats6(row) && IpHelper.TryGetEstats6(row, out var rod)) {
					bs = (long)rod.DataBytesOut;
					br = (long)rod.DataBytesIn;
					ps = (long)rod.DataSegsOut;
					pr = (long)rod.DataSegsIn;
					estatsOk = plausible(bs) && plausible(br) && plausible(ps) && plausible(pr);
					if (!estatsOk) bs = br = ps = pr = 0;
				}
			}
			var app = ProtocolClassifier.Classify(NetProtocol.TCP, rport, lport);
			snaps[key] = new ConnSnap {
				Key = key,
				Pid = pid,
				IsTcp = true,
				IsIpv6 = true,
				Transport = NetProtocol.TCP,
				AppProtocol = app,
				LocalIp = lip,
				LocalPort = lport,
				RemoteIp = state == ConnState.Listen ? "" : rip,
				RemotePort = state == ConnState.Listen ? 0 : rport,
				State = state,
				BytesSent = bs,
				BytesRecv = br,
				PacketsSent = ps,
				PacketsRecv = pr,
				EstatsOk = estatsOk,
				Tcp6 = row
			};
		}
	}

	void readUdp4(Dictionary<string, ConnSnap> snaps, int now) {
		foreach (var row in IpHelper.ReadUdp4()) {
			var lport = IpHelper.Ntohs(row.LocalPort);
			var lip = IpHelper.FormatIpv4(row.LocalAddr);
			var pid = (int)row.OwningPid;
			var key = $"UDP4|{pid}|{lip}:{lport}";
			// UDP 无远端与 ESTATS，仅枚举套接字
			var app = ProtocolClassifier.Classify(NetProtocol.UDP, 0, lport);
			snaps[key] = new ConnSnap {
				Key = key,
				Pid = pid,
				IsTcp = false,
				IsIpv6 = false,
				Transport = NetProtocol.UDP,
				AppProtocol = app,
				LocalIp = lip,
				LocalPort = lport,
				RemoteIp = "",
				RemotePort = 0,
				State = ConnState.Unknown,
				BytesSent = 0,
				BytesRecv = 0
			};
		}
	}

	void readUdp6(Dictionary<string, ConnSnap> snaps, int now) {
		foreach (var row in IpHelper.ReadUdp6()) {
			var lport = IpHelper.Ntohs(row.LocalPort);
			var lip = IpHelper.FormatIpv6(row.LocalAddr);
			var pid = (int)row.OwningPid;
			var key = $"UDP6|{pid}|[{lip}]:{lport}";
			var app = ProtocolClassifier.Classify(NetProtocol.UDP, 0, lport);
			snaps[key] = new ConnSnap {
				Key = key,
				Pid = pid,
				IsTcp = false,
				IsIpv6 = true,
				Transport = NetProtocol.UDP,
				AppProtocol = app,
				LocalIp = lip,
				LocalPort = lport,
				RemoteIp = "",
				RemotePort = 0,
				State = ConnState.Unknown
			};
		}
	}

	sealed class ConnTrack {
		public string Key;
		public int Pid;
		public bool IsTcp;
		public bool IsIpv6;
		public bool EstatsEnabled;
		public bool IsHistory;
		public long LastBytesSent, LastBytesRecv;
		public long LastPktsSent, LastPktsRecv;
		public long BytesSent, BytesRecv;
		public long PacketsSent, PacketsRecv;
		public double SendBps, RecvBps;
		public int LastSeenTick;
		public int ClosedTick;
		public ConnSnap Snap;
	}

	sealed class ConnSnap {
		public string Key;
		public int Pid;
		public bool IsTcp, IsIpv6;
		public bool EstatsOk;
		public NetProtocol Transport, AppProtocol;
		public string LocalIp, RemoteIp;
		public int LocalPort, RemotePort;
		public ConnState State;
		public long BytesSent, BytesRecv, PacketsSent, PacketsRecv;
		public IpHelper.MibTcpRowOwnerPid? Tcp4;
		public IpHelper.MibTcp6RowOwnerPid? Tcp6;
	}

	sealed class ProcAccum {
		public long BytesSent, BytesRecv, PacketsSent, PacketsRecv;
		public int LastSeenTick;
	}
}
