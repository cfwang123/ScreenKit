using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ScreenKit;

/// <summary>本进程的提交大小、CPU 和 GPU。CPU 按全部逻辑核折算，和任务管理器进程列一致。</summary>
static class ProcUse {
	const uint PDH_FMT_DOUBLE = 0x200;
	const uint PDH_MORE_DATA = 0x800007D2;
	const uint PDH_CSTATUS_NO_OBJECT = 0xC0000BB8;
	const uint PDH_CSTATUS_NO_COUNTER = 0xC0000BB9;

	static TimeSpan cpuAt;
	static int wallAt;
	static bool cpuHave;
	static IntPtr query;
	static readonly List<IntPtr> counters = new();
	static string[] paths = Array.Empty<string>();
	static int scanAt;
	static int boundPid;
	static bool gpuMissing;
	static bool gpuPrimed;

	public static long LastCommit;
	public static double LastCpu;
	public static double LastGpu;

	[DllImport("pdh.dll", CharSet = CharSet.Unicode)]
	static extern uint PdhOpenQueryW(string dataSource, IntPtr userData, out IntPtr query);

	[DllImport("pdh.dll", CharSet = CharSet.Unicode)]
	static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);

	[DllImport("pdh.dll", CharSet = CharSet.Unicode)]
	static extern uint PdhExpandWildCardPathW(string dataSource, string path, IntPtr expanded, ref uint length, uint flags);

	[DllImport("pdh.dll")]
	static extern uint PdhCollectQueryData(IntPtr query);

	[DllImport("pdh.dll")]
	static extern uint PdhCloseQuery(IntPtr query);

	[DllImport("pdh.dll")]
	static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr type, out PdhValue value);

	[StructLayout(LayoutKind.Explicit)]
	struct PdhValue {
		[FieldOffset(0)] public uint Status;
		[FieldOffset(8)] public double Number;
	}

	public static void Sample(out long commit, out double cpu, out double gpu) {
		using var proc = Process.GetCurrentProcess();
		proc.Refresh();
		commit = proc.PrivateMemorySize64;
		cpu = readcpu(proc.TotalProcessorTime);
		gpu = readgpu(proc.Id);
		LastCommit = commit;
		LastCpu = cpu;
		LastGpu = gpu;
	}

	public static int SelfTest() {
		Sample(out var commit, out _, out _);
		if (commit <= 0) return 1;
		var until = Environment.TickCount + 500;
		while (unchecked(Environment.TickCount - until) < 0) { }
		Sample(out commit, out var cpu, out var gpu);
		if (commit <= 0) return 2;
		if (cpu <= 0 || cpu > 100) return 3;
		if (gpu < -1 || gpu > 100) return 4;
		return 0;
	}

	static double readcpu(TimeSpan nowCpu) {
		var now = Environment.TickCount;
		double pct = 0;
		if (cpuHave) {
			var dt = unchecked(now - wallAt);
			if (dt > 0) {
				pct = (nowCpu - cpuAt).TotalMilliseconds / dt / Environment.ProcessorCount * 100.0;
				if (pct < 0) pct = 0;
				if (pct > 100) pct = 100;
			}
		}
		cpuAt = nowCpu;
		wallAt = now;
		cpuHave = true;
		return pct;
	}

	static double readgpu(int pid) {
		try {
			ensuregpu(pid);
			if (gpuMissing) return -1;
			if (query == IntPtr.Zero || counters.Count == 0) return 0;
			if (PdhCollectQueryData(query) != 0) return gpuPrimed ? LastGpu : 0;
			double max = 0;
			var any = false;
			foreach (var counter in counters) {
				if (PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE, IntPtr.Zero, out var value) != 0)
					continue;
				if (value.Status != 0) continue;
				any = true;
				if (value.Number > max) max = value.Number;
			}
			if (!any) return gpuPrimed ? LastGpu : 0;
			gpuPrimed = true;
			if (max < 0) max = 0;
			if (max > 100) max = 100;
			return max;
		}
		catch {
			return -1;
		}
	}

	static void ensuregpu(int pid) {
		var now = Environment.TickCount;
		if (query != IntPtr.Zero && pid == boundPid && unchecked(now - scanAt) < 8000) return;
		var found = gpupaths(pid, out var missing);
		scanAt = now;
		boundPid = pid;
		if (missing) {
			closequery();
			gpuMissing = true;
			gpuPrimed = false;
			return;
		}
		gpuMissing = false;
		if (query != IntPtr.Zero && samepaths(found, paths)) return;
		closequery();
		paths = found;
		gpuPrimed = false;
		if (found.Length == 0) return;
		if (PdhOpenQueryW(null, IntPtr.Zero, out query) != 0) {
			query = IntPtr.Zero;
			return;
		}
		foreach (var path in found) {
			if (PdhAddEnglishCounterW(query, path, IntPtr.Zero, out var counter) == 0)
				counters.Add(counter);
		}
		PdhCollectQueryData(query);
	}

	static string[] gpupaths(int pid, out bool missing) {
		missing = false;
		var wild = @"\GPU Engine(pid_" + pid + @"_*)\Utilization Percentage";
		uint len = 0;
		var rc = PdhExpandWildCardPathW(null, wild, IntPtr.Zero, ref len, 0);
		if (rc == PDH_CSTATUS_NO_OBJECT || rc == PDH_CSTATUS_NO_COUNTER) {
			missing = true;
			return Array.Empty<string>();
		}
		if (rc != PDH_MORE_DATA || len < 2) return Array.Empty<string>();
		if (len > 65536) len = 65536;
		var buf = Marshal.AllocHGlobal((int)len * 2);
		try {
			rc = PdhExpandWildCardPathW(null, wild, buf, ref len, 0);
			if (rc != 0) return Array.Empty<string>();
			var text = Marshal.PtrToStringUni(buf) ?? "";
			var list = text.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
			Array.Sort(list, StringComparer.Ordinal);
			return list;
		}
		finally {
			Marshal.FreeHGlobal(buf);
		}
	}

	static bool samepaths(string[] a, string[] b) {
		if (a == null || b == null || a.Length != b.Length) return false;
		for (var i = 0; i < a.Length; i++)
			if (a[i] != b[i]) return false;
		return true;
	}

	static void closequery() {
		counters.Clear();
		paths = Array.Empty<string>();
		if (query == IntPtr.Zero) return;
		try { PdhCloseQuery(query); } catch { }
		query = IntPtr.Zero;
	}
}
