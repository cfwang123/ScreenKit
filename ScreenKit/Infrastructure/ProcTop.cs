using System.Runtime.InteropServices;
using System.Text;

namespace ScreenKit;

/// <summary>进程占用排行：CPU、内存、磁盘、网络。后台线程每秒采样，UI 只读快照。</summary>
sealed class ProcTop : IDisposable {
	// 采样上限：原本只取前 5，现在要按窗口高度能显示几个就显示几个
	public const int TOP = 40;

	public sealed class Row {
		public int Pid;
		public string Name;
		public double Cpu;
		public long Mem;
		public double Disk;
		public double Net;
	}

	public sealed class Snap {
		public List<Row> Cpu = new();
		public List<Row> Mem = new();
		public List<Row> Disk = new();
		public List<Row> Net = new();
		public bool NetOk;
	}

	const uint QUERY_INFORMATION = 0x0400;
	const uint QUERY_LIMITED = 0x1000;
	const uint VM_READ = 0x0010;
	// 一次采样最多认 2 GB 的变化，超过说明基线变了（PID 复用、计数器重置），不是真流量
	const ulong MAX_DELTA = 2UL << 30;
	// 距上一轮太近就不再采：切页签会补采一次，间隔只有几毫秒，
	// 拿它当除数会把几 MB 的写入放大成几百 M/s
	const int MIN_SAMPLE_MS = 300;

	readonly Thread worker;
	readonly object gate = new();
	// 采样间隔跟着小窗的参数走，改小之后要立刻叫醒等待中的采样线程
	readonly ManualResetEventSlim wake = new(false);
	// 一次性回调：请求立即采样后，采完这一轮叫一声
	readonly object cbgate = new();
	Action sampled;
	volatile int intervalMs = 1000;
	readonly Dictionary<int, ulong> preCpu = new();
	readonly Dictionary<int, ulong> preIo = new();
	readonly Dictionary<string, ConnPrev> preConn = new(StringComparer.Ordinal);
	readonly HashSet<string> triedConn = new(StringComparer.Ordinal);
	// 只有成功开启 ESTATS 的连接才读得到真实字节数，其余读回的是内存垃圾
	readonly HashSet<string> okConn = new(StringComparer.Ordinal);
	readonly Dictionary<int, double> netBps = new();
	readonly StringBuilder pathbuf = new(1024);
	volatile bool stop;
	volatile Snap snap = new();
	int preTick, netTick;
	int[] pids = new int[4096];

	sealed class ConnPrev {
		public int Pid;
		public ulong Out;
		public ulong In;
	}

	public ProcTop() {
		worker = new Thread(loop) { IsBackground = true, Name = "ScreenKit.ProcTop" };
	}

	/// <summary>采样间隔，毫秒。跟小窗参数里的更新间隔一致。</summary>
	public int IntervalMs {
		get => intervalMs;
		set {
			var v = Math.Max(200, Math.Min(5000, value));
			if (v == intervalMs) return;
			intervalMs = v;
			wake.Set();
		}
	}

	public void Start() {
		if (!worker.IsAlive) worker.Start();
	}

	/// <summary>马上采一轮，采完叫 done（在后台线程上）。用于切 tab、刚开窗要立刻看到数据。</summary>
	public void Refresh(Action done) {
		lock (cbgate) sampled = done;
		wake.Set();
	}

	public void Dispose() {
		stop = true;
		wake.Set();
		if (worker.IsAlive) worker.Join(1500);
		wake.Dispose();
	}

	public Snap Take() => snap;

	public int Test(Action<string> log, Action<string> err) {
		sample();
		Thread.Sleep(400);
		sample();
		var s = Take();
		log($"proc cpu={s.Cpu.Count} mem={s.Mem.Count} disk={s.Disk.Count} net={s.Net.Count} netok={s.NetOk}");
		if (s.Disk.Count > 0) {
			var sum = s.Disk.Sum(r => r.Disk);
			log($"disk top={s.Disk[0].Name} {s.Disk[0].Disk / 1048576.0:0.0}M/s sum={sum / 1048576.0:0.0}M/s");
			// 切页签补采会把速率放大几百倍，这里挡住那条路
			if (s.Disk[0].Disk > 4L << 30) {
				err("进程磁盘速率离谱");
				return 1;
			}
		}
		if (s.Cpu.Count == 0) {
			err("进程排行为空");
			return 1;
		}
		if (s.Mem.Count == 0 || s.Disk.Count == 0) {
			err("内存或磁盘排行为空");
			return 1;
		}
		if (s.Cpu[0].Name.Length == 0) {
			err("进程没有名字");
			return 1;
		}
		if (s.Mem[0].Mem <= 0) {
			err("内存占用为 0");
			return 1;
		}
		log($"top cpu: {s.Cpu[0].Name} {s.Cpu[0].Cpu:0.0}% mem {s.Mem[0].Name} {s.Mem[0].Mem}");
		return 0;
	}

	void loop() {
		while (!stop) {
			try { sample(); } catch { }
			notify();
			wake.Wait(intervalMs);
			wake.Reset();
		}
	}

	/// <summary>采完一轮之后叫一次等候的一方，只叫一次</summary>
	void notify() {
		Action cb;
		lock (cbgate) {
			cb = sampled;
			sampled = null;
		}
		if (cb == null) return;
		try { cb(); } catch { }
	}

	void sample() {
		var now = Environment.TickCount;
		var ms = preTick == 0 ? 1000 : unchecked(now - preTick);
		// 离上一轮太近就跳过：速率是差值除间隔，间隔几毫秒会把读数放大几百倍
		if (preTick != 0 && ms < MIN_SAMPLE_MS) return;
		preTick = now;
		var secs = ms / 1000.0;
		lock (gate) {
			var rows = readprocs(ms, secs);
			if (netTick == 0 || unchecked(now - netTick) >= 2000) {
				var netSecs = netTick == 0 ? secs : Math.Max(unchecked(now - netTick) / 1000.0, 0.2);
				netTick = now;
				samplenet(netSecs);
			}
			foreach (var r in rows) r.Net = netBps.TryGetValue(r.Pid, out var b) ? b : 0;
			var s = new Snap {
				Cpu = top(rows, r => r.Cpu),
				Mem = top(rows, r => r.Mem),
				Disk = top(rows, r => r.Disk),
				Net = top(rows, r => r.Net),
				NetOk = netOk,
			};
			snap = s;
		}
	}

	static List<Row> top(List<Row> rows, Func<Row, double> key) =>
		rows.OrderByDescending(key).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Take(TOP).ToList();

	List<Row> readprocs(int ms, double secs) {
		var rows = new List<Row>();
		if (!EnumProcesses(pids, pids.Length * 4, out var need)) return rows;
		if (need >= pids.Length * 4) {
			pids = new int[Math.Min(need / 4 + 1024, 65536)];
			if (!EnumProcesses(pids, pids.Length * 4, out need)) return rows;
		}
		var n = need / 4;
		var seen = new HashSet<int>();
		for (var i = 0; i < n; i++) {
			var pid = pids[i];
			if (pid <= 0 || !seen.Add(pid)) continue;
			var h = OpenProcess(QUERY_INFORMATION | VM_READ, false, pid);
			if (h == IntPtr.Zero) h = OpenProcess(QUERY_LIMITED, false, pid);
			if (h == IntPtr.Zero) continue;
			try {
				if (!GetProcessTimes(h, out _, out _, out var kern, out var user)) continue;
				var cpu = ft(kern) + ft(user);
				var pct = 0.0;
				if (preCpu.TryGetValue(pid, out var ocpu) && cpu >= ocpu && ms > 0)
					pct = (cpu - ocpu) * 100.0 / (ms * 10000.0 * Environment.ProcessorCount);
				preCpu[pid] = cpu;
				if (pct < 0) pct = 0;
				if (pct > 100) pct = 100;
				long mem = 0;
				if (GetProcessMemoryInfo(h, out var pmc, Marshal.SizeOf(typeof(PMCEX))))
					mem = (long)pmc.WorkingSetSize;
				var disk = 0.0;
				if (GetProcessIoCounters(h, out var io)) {
					var bytes = io.ReadTransferCount + io.WriteTransferCount;
					if (preIo.TryGetValue(pid, out var oio) && bytes >= oio) {
						// 单次涨过 2 GB 只当基线变了（PID 复用、计数器重置），不记速率
						var delta = bytes - oio;
						if (delta <= MAX_DELTA) disk = delta / secs;
					}
					preIo[pid] = bytes;
				}
				rows.Add(new Row { Pid = pid, Name = imagepath(h), Cpu = pct, Mem = mem, Disk = disk });
			}
			finally { CloseHandle(h); }
		}
		prunepids(seen);
		return rows;
	}

	void prunepids(HashSet<int> seen) {
		if (preCpu.Count <= seen.Count) return;
		var dead = preCpu.Keys.Where(k => !seen.Contains(k)).ToList();
		foreach (var k in dead) {
			preCpu.Remove(k);
			preIo.Remove(k);
		}
	}

	string imagepath(IntPtr h) {
		pathbuf.Clear();
		var size = pathbuf.Capacity;
		if (!QueryFullProcessImageName(h, 0, pathbuf, ref size) || pathbuf.Length == 0) return "PID";
		var text = pathbuf.ToString();
		var i = text.LastIndexOf('\\');
		return i >= 0 ? text.Substring(i + 1) : text;
	}

	static ulong ft(FT t) => ((ulong)t.High << 32) | t.Low;

	bool netOk;

	void samplenet(double secs) {
		var acc = new Dictionary<int, ulong>();
		var alive = new HashSet<string>(StringComparer.Ordinal);
		try {
			foreach (var row in IpHelper.ReadTcp4()) {
				var pid = (int)row.OwningPid;
				if (pid <= 0) continue;
				var key = $"4|{row.LocalAddr}|{row.LocalPort}|{row.RemoteAddr}|{row.RemotePort}";
				alive.Add(key);
				if ((ConnState)row.State != ConnState.Established) continue;
				if (triedConn.Add(key) && IpHelper.TryEnableEstats4(row)) okConn.Add(key);
				// 没开成的连接读回来是垃圾值，宁可显示 0 也不报假数字
				if (!okConn.Contains(key)) continue;
				if (!IpHelper.TryGetEstats4(row, out var rod)) continue;
				addnet(key, pid, acc, rod.DataBytesOut, rod.DataBytesIn);
			}
			foreach (var row in IpHelper.ReadTcp6()) {
				var pid = (int)row.OwningPid;
				if (pid <= 0) continue;
				var key = $"6|{IpHelper.FormatIpv6(row.LocalAddr)}|{row.LocalPort}|{IpHelper.FormatIpv6(row.RemoteAddr)}|{row.RemotePort}";
				alive.Add(key);
				if ((ConnState)row.State != ConnState.Established) continue;
				if (triedConn.Add(key) && IpHelper.TryEnableEstats6(row)) okConn.Add(key);
				if (!okConn.Contains(key)) continue;
				if (!IpHelper.TryGetEstats6(row, out var rod)) continue;
				addnet(key, pid, acc, rod.DataBytesOut, rod.DataBytesIn);
			}
		}
		catch { }
		foreach (var key in preConn.Keys.Where(k => !alive.Contains(k)).ToList()) preConn.Remove(key);
		triedConn.RemoveWhere(k => !alive.Contains(k));
		okConn.RemoveWhere(k => !alive.Contains(k));
		netBps.Clear();
		foreach (var kv in acc) netBps[kv.Key] = kv.Value / secs;
		netOk = okConn.Count > 0;
	}

	void addnet(string key, int pid, Dictionary<int, ulong> acc, ulong o, ulong i) {
		// 没开成 ESTATS 时读回的是垃圾值，超过 1 PB 一律不认
		if (o >= (1UL << 50) || i >= (1UL << 50)) return;
		if (!preConn.TryGetValue(key, out var p)) {
			preConn[key] = new ConnPrev { Pid = pid, Out = o, In = i };
			return;
		}
		if (p.Pid != pid) {
			p.Pid = pid;
			p.Out = o;
			p.In = i;
			return;
		}
		var dOut = o >= p.Out ? o - p.Out : 0;
		var dIn = i >= p.In ? i - p.In : 0;
		p.Out = o;
		p.In = i;
		// 单次采样涨得太离谱说明基线是垃圾值，只对齐基线不计数
		if (dOut > MAX_DELTA) dOut = 0;
		if (dIn > MAX_DELTA) dIn = 0;
		var d = dOut + dIn;
		if (d == 0) return;
		acc[pid] = acc.TryGetValue(pid, out var v) ? v + d : d;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct FT {
		public uint Low;
		public uint High;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct IO_COUNTERS {
		public ulong ReadOperationCount;
		public ulong WriteOperationCount;
		public ulong ReadTransferCount;
		public ulong WriteTransferCount;
		public ulong OtherOperationCount;
		public ulong OtherTransferCount;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct PMCEX {
		public uint cb;
		public uint PageFaultCount;
		public ulong PeakWorkingSetSize;
		public ulong WorkingSetSize;
		public ulong QuotaPeakPagedPoolUsage;
		public ulong QuotaPagedPoolUsage;
		public ulong QuotaPeakNonPagedPoolUsage;
		public ulong QuotaNonPagedPoolUsage;
		public ulong PagefileUsage;
		public ulong PeakPagefileUsage;
		public ulong PrivateUsage;
	}

	[DllImport("psapi.dll")]
	static extern bool EnumProcesses(int[] pids, int size, out int needed);

	[DllImport("psapi.dll")]
	static extern bool GetProcessMemoryInfo(IntPtr h, out PMCEX pmc, int size);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool CloseHandle(IntPtr h);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool GetProcessTimes(IntPtr h, out FT creation, out FT exit, out FT kernel, out FT user);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool GetProcessIoCounters(IntPtr h, out IO_COUNTERS io);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
}

