using System.Runtime.InteropServices;
using System.Text;

namespace ScreenKit;

/// <summary>进程占用排行：CPU、内存、磁盘、网络。后台线程每秒采样，UI 只读快照。</summary>
sealed class ProcTop : IDisposable {
	public const int TOP = 5;

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

	readonly Thread worker;
	readonly object gate = new();
	readonly Dictionary<int, ulong> preCpu = new();
	readonly Dictionary<int, ulong> preIo = new();
	readonly Dictionary<string, ConnPrev> preConn = new(StringComparer.Ordinal);
	readonly HashSet<string> triedConn = new(StringComparer.Ordinal);
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

	public void Start() {
		if (!worker.IsAlive) worker.Start();
	}

	public void Dispose() {
		stop = true;
		if (worker.IsAlive) worker.Join(1500);
	}

	public Snap Take() => snap;

	public int Test(Action<string> log, Action<string> err) {
		sample();
		Thread.Sleep(300);
		sample();
		var s = Take();
		log($"proc cpu={s.Cpu.Count} mem={s.Mem.Count} disk={s.Disk.Count} net={s.Net.Count} netok={s.NetOk}");
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
			Thread.Sleep(900);
		}
	}

	void sample() {
		var now = Environment.TickCount;
		var ms = preTick == 0 ? 1000 : unchecked(now - preTick);
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
					if (preIo.TryGetValue(pid, out var oio) && bytes >= oio) disk = (bytes - oio) / secs;
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
		var ok = false;
		try {
			foreach (var row in IpHelper.ReadTcp4()) {
				var pid = (int)row.OwningPid;
				if (pid <= 0) continue;
				var key = $"4|{row.LocalAddr}|{row.LocalPort}|{row.RemoteAddr}|{row.RemotePort}";
				alive.Add(key);
				if ((ConnState)row.State != ConnState.Established) continue;
				if (triedConn.Add(key)) IpHelper.TryEnableEstats4(row);
				if (!IpHelper.TryGetEstats4(row, out var rod)) continue;
				ok = true;
				addnet(key, pid, acc, rod.DataBytesOut, rod.DataBytesIn);
			}
			foreach (var row in IpHelper.ReadTcp6()) {
				var pid = (int)row.OwningPid;
				if (pid <= 0) continue;
				var key = $"6|{IpHelper.FormatIpv6(row.LocalAddr)}|{row.LocalPort}|{IpHelper.FormatIpv6(row.RemoteAddr)}|{row.RemotePort}";
				alive.Add(key);
				if ((ConnState)row.State != ConnState.Established) continue;
				if (triedConn.Add(key)) IpHelper.TryEnableEstats6(row);
				if (!IpHelper.TryGetEstats6(row, out var rod)) continue;
				ok = true;
				addnet(key, pid, acc, rod.DataBytesOut, rod.DataBytesIn);
			}
		}
		catch { }
		foreach (var key in preConn.Keys.Where(k => !alive.Contains(k)).ToList()) preConn.Remove(key);
		triedConn.RemoveWhere(k => !alive.Contains(k));
		netBps.Clear();
		foreach (var kv in acc) netBps[kv.Key] = kv.Value / secs;
		netOk = ok;
	}

	void addnet(string key, int pid, Dictionary<int, ulong> acc, ulong o, ulong i) {
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
		var d = (o >= p.Out ? o - p.Out : 0) + (i >= p.In ? i - p.In : 0);
		p.Out = o;
		p.In = i;
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

