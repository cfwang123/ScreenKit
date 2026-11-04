using System;
using System.Runtime.InteropServices;

namespace ScreenKit;

/// <summary>
/// 转换时显示整机 CPU / GPU 占用。CPU 走 GetSystemTimes 差值；
/// GPU 走 PDH 的英文计数器「GPU Engine」，不受系统语言影响，取不到就返回 -1。
/// </summary>
static class SysLoad {
	[StructLayout(LayoutKind.Sequential)]
	struct FileTime {
		public uint Low;
		public uint High;
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

	static ulong prevIdle, prevKernel, prevUser;
	static bool havePrev;

	/// <summary>整机 CPU 占用百分比。第一次调用只建立基线，返回 -1。</summary>
	public static double Cpu() {
		try {
			if (!GetSystemTimes(out var idle, out var kernel, out var user)) return -1;
			var i = tou(idle);
			var k = tou(kernel);
			var u = tou(user);
			if (!havePrev) {
				prevIdle = i;
				prevKernel = k;
				prevUser = u;
				havePrev = true;
				return -1;
			}
			var di = i - prevIdle;
			var dk = k - prevKernel;
			var du = u - prevUser;
			prevIdle = i;
			prevKernel = k;
			prevUser = u;
			// kernel 时间包含 idle，所以总量是 kernel + user
			var total = dk + du;
			if (total == 0) return -1;
			var pct = (total - di) * 100.0 / total;
			return pct < 0 ? 0 : pct > 100 ? 100 : pct;
		}
		catch {
			return -1;
		}
	}

	static ulong tou(FileTime t) => ((ulong)t.High << 32) | t.Low;

	/// <summary>开始一轮转换前清掉 CPU 基线，第一秒就能出数。</summary>
	public static void ResetCpu() => havePrev = false;

	// ───────── GPU（PDH 英文计数器，中文系统也能读） ─────────

	const uint PDH_FMT_DOUBLE = 0x00000200;
	const uint PDH_MORE_DATA = 0x800007D2;
	const uint PDH_CSTATUS_VALID_DATA = 0x00000000;
	const uint PDH_CSTATUS_NEW_DATA = 0x00000001;

	[DllImport("pdh.dll", CharSet = CharSet.Unicode)]
	static extern uint PdhOpenQueryW(string dataSource, IntPtr userData, out IntPtr query);

	[DllImport("pdh.dll", CharSet = CharSet.Unicode)]
	static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);

	[DllImport("pdh.dll")]
	static extern uint PdhCollectQueryData(IntPtr query);

	[DllImport("pdh.dll", CharSet = CharSet.Unicode)]
	static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format,
		ref uint bufSize, out uint itemCount, IntPtr buffer);

	[DllImport("pdh.dll")]
	static extern uint PdhCloseQuery(IntPtr query);

	[StructLayout(LayoutKind.Sequential)]
	struct FmtItem {
		public IntPtr Name;
		public uint CStatus;
		public double Value;
	}

	static IntPtr gpuQuery;
	static IntPtr gpuCounter;
	static bool gpuInit;
	static bool gpuBroken;

	/// <summary>整机 GPU 占用百分比（取最忙的引擎，和任务管理器一致）。取不到返回 -1。</summary>
	public static double Gpu() {
		if (gpuBroken) return -1;
		try {
			if (!gpuInit) {
				gpuInit = true;
				var st = PdhOpenQueryW(null, IntPtr.Zero, out gpuQuery);
				if (st != 0 || gpuQuery == IntPtr.Zero) {
					gpuBroken = true;
					return -1;
				}
				st = PdhAddEnglishCounterW(gpuQuery, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out gpuCounter);
				if (st != 0 || gpuCounter == IntPtr.Zero) {
					gpuBroken = true;
					PdhCloseQuery(gpuQuery);
					gpuQuery = IntPtr.Zero;
					return -1;
				}
				// 速率型计数器要采两次才有值，第一次在窗口打开时就采掉
				PdhCollectQueryData(gpuQuery);
			}
			if (PdhCollectQueryData(gpuQuery) != 0) return -1;
			uint size = 0;
			uint count = 0;
			var rc = PdhGetFormattedCounterArrayW(gpuCounter, PDH_FMT_DOUBLE, ref size, out count, IntPtr.Zero);
			if (rc != PDH_MORE_DATA || size == 0) return -1;
			var buf = Marshal.AllocHGlobal((int)size);
			try {
				rc = PdhGetFormattedCounterArrayW(gpuCounter, PDH_FMT_DOUBLE, ref size, out count, buf);
				if (rc != 0) return -1;
				double top = 0;
				var step = Marshal.SizeOf(typeof(FmtItem));
				for (var i = 0; i < count; i++) {
					var it = Marshal.PtrToStructure<FmtItem>(IntPtr.Add(buf, i * step));
					if (it.CStatus != PDH_CSTATUS_VALID_DATA && it.CStatus != PDH_CSTATUS_NEW_DATA) continue;
					if (it.Value > top) top = it.Value;
				}
				return top > 100 ? 100 : top;
			}
			finally {
				Marshal.FreeHGlobal(buf);
			}
		}
		catch {
			gpuBroken = true;
			return -1;
		}
	}
}
