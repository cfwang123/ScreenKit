using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ScreenKit;

/// <summary>性能小窗的开关和位置。存在 exe 旁的 perf.json。</summary>
public sealed class PerfOpt {
	public static readonly string[] Keys = {
		"cpu", "gpu", "mem", "down", "up", "read", "write", "cputemp", "gputemp", "gpupower", "cpupower",
	};

	public bool Topmost { get; set; } = true;
	public double Left { get; set; }
	public double Top { get; set; }
	public bool HasPos { get; set; }
	/// <summary>采样间隔，毫秒。200～5000，缺省 1000。</summary>
	public int IntervalMs { get; set; } = 1000;
	public Dictionary<string, bool> Flags { get; set; } = new();

	public static int NormMs(int ms) {
		if (ms < 200) return 1000;
		if (ms > 5000) return 5000;
		return ms;
	}

	public static string DefaultPath =>
		Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "perf.json");

	public bool On(string key) {
		if (Flags != null && Flags.TryGetValue(key, out var v)) return v;
		return true;
	}

	public void Set(string key, bool on) {
		if (Flags == null) Flags = new Dictionary<string, bool>();
		Flags[key] = on;
	}

	public static PerfOpt Load(string path) {
		try {
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new PerfOpt();
			var o = JsonSerializer.Deserialize<PerfOpt>(File.ReadAllText(path));
			if (o == null) return new PerfOpt();
			if (o.Flags == null) o.Flags = new Dictionary<string, bool>();
			return o;
		}
		catch { return new PerfOpt(); }
	}

	public void Save(string path) {
		if (string.IsNullOrWhiteSpace(path)) return;
		var json = JsonSerializer.Serialize(this);
		var dir = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		File.WriteAllText(path, json);
	}
}

/// <summary>只读采样：CPU、GPU、内存、网络、磁盘、温度和功率。</summary>
static class PerfMon {
	public sealed class Snap {
		public double Cpu = double.NaN;
		public double Gpu = double.NaN;
		public double Mem = double.NaN;
		public double Down = double.NaN;
		public double Up = double.NaN;
		public double Read = double.NaN;
		public double Write = double.NaN;
		public double CpuTemp = double.NaN;
		public double GpuTemp = double.NaN;
		public double GpuPower = double.NaN;
		public double CpuPower = double.NaN;
	}

	const uint PDH_FMT_DOUBLE = 0x200;
	const uint PDH_MORE_DATA = 0x800007D2;

	static IntPtr query;
	static IntPtr gpuCtr;
	static IntPtr rxCtr;
	static IntPtr txCtr;
	static IntPtr rdCtr;
	static IntPtr wrCtr;
	static IntPtr pwrCtr;
	static bool opened;
	static bool hasCpu;
	static ulong prevIdle, prevKernel, prevUser;
	static double lastCpuTemp = double.NaN;
	static int tempLeft;
	static int tempBusy;
	static bool nvmlTried;
	static bool nvmlOk;
	static IntPtr nvmlLib;
	static NvmlInit nvmlInit;
	static NvmlCount nvmlCount;
	static NvmlHandle nvmlHandle;
	static NvmlTemp nvmlTemp;
	static NvmlPower nvmlPower;
	static NvmlUtilFn nvmlUtil;

	public static Snap Take() {
		var s = new Snap();
		try { s.Cpu = cpu(); } catch { }
		try { s.Mem = mem(); } catch { }
		try { pdh(s); } catch { }
		try { nvml(s); } catch { }
		s.CpuTemp = lastCpuTemp;
		if (tempLeft <= 0) {
			tempLeft = 4;
			kicktemp();
		}
		else tempLeft--;
		return s;
	}

	static void kicktemp() {
		if (Interlocked.CompareExchange(ref tempBusy, 1, 0) != 0) return;
		var th = new Thread(() => {
			try {
				var v = cputemp();
				if (!double.IsNaN(v)) lastCpuTemp = v;
			}
			catch { }
			Interlocked.Exchange(ref tempBusy, 0);
		});
		th.IsBackground = true;
		th.SetApartmentState(ApartmentState.STA);
		th.Start();
	}

	public static int Test(Action<string> log, Action<string> err) {
		log("=== 性能监控 --test-perfmon ===");
		Take();
		Thread.Sleep(350);
		var s = Take();
		log($"cpu={s.Cpu:0.0} gpu={s.Gpu:0.0} mem={s.Mem:0.0} down={s.Down:0} up={s.Up:0} rd={s.Read:0} wr={s.Write:0} cput={s.CpuTemp:0.0} gput={s.GpuTemp:0.0} gpup={s.GpuPower:0.0} cpup={s.CpuPower:0.0}");
		if (double.IsNaN(s.Cpu) || s.Cpu < 0 || s.Cpu > 100) {
			err("CPU 占用不在 0～100");
			return 1;
		}
		if (double.IsNaN(s.Mem) || s.Mem <= 0 || s.Mem > 100) {
			err("内存占用不在 0～100");
			return 1;
		}
		if (double.IsNaN(s.Down) || double.IsNaN(s.Up)) {
			err("没有网络速率");
			return 1;
		}
		if (double.IsNaN(s.Read) || double.IsNaN(s.Write)) {
			err("没有磁盘速率");
			return 1;
		}
		var path = Path.Combine(Path.GetTempPath(), "sk-perf-" + Guid.NewGuid().ToString("N") + ".json");
		try {
			var o = new PerfOpt();
			o.Set("gpu", false);
			o.IntervalMs = 2500;
			o.Save(path);
			var q = PerfOpt.Load(path);
			if (q.On("gpu") || !q.On("cpu") || !q.Topmost || q.IntervalMs != 2500 || PerfOpt.NormMs(0) != 1000) {
				err("开关没有保存");
				return 1;
			}
		}
		finally {
			try { File.Delete(path); } catch { }
		}
		log("=== OK：性能监控 ===");
		return 0;
	}

	static double cpu() {
		if (!GetSystemTimes(out var idle, out var kernel, out var user)) return double.NaN;
		var i = ft(idle);
		var k = ft(kernel);
		var u = ft(user);
		if (!hasCpu) {
			hasCpu = true;
			prevIdle = i;
			prevKernel = k;
			prevUser = u;
			return 0;
		}
		var sys = (k - prevKernel) + (u - prevUser);
		var id = i - prevIdle;
		prevIdle = i;
		prevKernel = k;
		prevUser = u;
		if (sys == 0) return 0;
		var busy = sys > id ? sys - id : 0;
		var pct = busy * 100.0 / sys;
		if (pct < 0) return 0;
		if (pct > 100) return 100;
		return pct;
	}

	static ulong ft(System.Runtime.InteropServices.ComTypes.FILETIME t) =>
		((ulong)(uint)t.dwHighDateTime << 32) | (uint)t.dwLowDateTime;

	static double mem() {
		var st = new MEMORYSTATUSEX { Length = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
		if (!GlobalMemoryStatusEx(ref st)) return double.NaN;
		return st.MemoryLoad;
	}

	static void pdh(Snap s) {
		ensure();
		if (query == IntPtr.Zero) return;
		PdhCollectQueryData(query);
		var gpu = gpupct();
		if (!double.IsNaN(gpu)) s.Gpu = gpu;
		var down = sumctr(rxCtr, true);
		var up = sumctr(txCtr, true);
		if (!double.IsNaN(down)) s.Down = down;
		if (!double.IsNaN(up)) s.Up = up;
		var rd = one(rdCtr);
		var wr = one(wrCtr);
		if (!double.IsNaN(rd)) s.Read = rd;
		if (!double.IsNaN(wr)) s.Write = wr;
		var pwr = sumctr(pwrCtr, false);
		if (!double.IsNaN(pwr)) s.CpuPower = pwr;
	}

	static void ensure() {
		if (opened) return;
		opened = true;
		if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0) {
			query = IntPtr.Zero;
			return;
		}
		gpuCtr = add(@"\GPU Engine(*)\Utilization Percentage");
		rxCtr = add(@"\Network Interface(*)\Bytes Received/sec");
		txCtr = add(@"\Network Interface(*)\Bytes Sent/sec");
		rdCtr = add(@"\PhysicalDisk(_Total)\Disk Read Bytes/sec");
		wrCtr = add(@"\PhysicalDisk(_Total)\Disk Write Bytes/sec");
		pwrCtr = add(@"\Power Meter(*)\Power");
		PdhCollectQueryData(query);
	}

	static IntPtr add(string path) {
		if (PdhAddEnglishCounter(query, path, IntPtr.Zero, out var h) != 0) return IntPtr.Zero;
		return h;
	}

	static double gpupct() {
		var items = array(gpuCtr);
		if (items == null) return double.NaN;
		var by = new Dictionary<int, double>();
		foreach (var it in items) {
			if (it.Name.IndexOf("engtype_3D", StringComparison.OrdinalIgnoreCase) < 0) continue;
			var phys = physof(it.Name);
			if (!by.ContainsKey(phys)) by[phys] = 0;
			by[phys] += it.Value;
		}
		if (by.Count == 0) return double.NaN;
		var max = 0.0;
		foreach (var v in by.Values)
			if (v > max) max = v;
		if (max < 0) return 0;
		if (max > 100) return 100;
		return max;
	}

	static int physof(string name) {
		var i = name.IndexOf("phys_", StringComparison.OrdinalIgnoreCase);
		if (i < 0) return 0;
		i += 5;
		var n = 0;
		while (i < name.Length && name[i] >= '0' && name[i] <= '9') {
			n = n * 10 + (name[i] - '0');
			i++;
		}
		return n;
	}

	static double one(IntPtr counter) {
		if (counter == IntPtr.Zero) return double.NaN;
		var items = array(counter);
		if (items == null || items.Count == 0) return double.NaN;
		var v = items[0].Value;
		if (v < 0) return 0;
		return v;
	}

	static double sumctr(IntPtr counter, bool net) {
		if (counter == IntPtr.Zero) return double.NaN;
		var items = array(counter);
		if (items == null) return double.NaN;
		var sum = 0.0;
		var any = false;
		foreach (var it in items) {
			if (net && skipnet(it.Name)) continue;
			if (it.Value < 0) continue;
			sum += it.Value;
			any = true;
		}
		if (!any) return double.NaN;
		return sum;
	}

	static bool skipnet(string name) {
		if (name.IndexOf("Loopback", StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (name.IndexOf("isatap", StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (name.IndexOf("Teredo", StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (name.IndexOf("Pseudo", StringComparison.OrdinalIgnoreCase) >= 0) return true;
		return false;
	}

	static List<Ctr> array(IntPtr counter) {
		if (counter == IntPtr.Zero) return null;
		uint size = 0;
		uint count = 0;
		var rc = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE, ref size, ref count, IntPtr.Zero);
		if (rc != 0 && rc != PDH_MORE_DATA) return null;
		if (size == 0) return new List<Ctr>();
		var buf = Marshal.AllocHGlobal((int)size);
		try {
			rc = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE, ref size, ref count, buf);
			if (rc != 0) return null;
			var list = new List<Ctr>();
			var stride = Marshal.SizeOf(typeof(PDH_FMT_COUNTERVALUE_ITEM));
			for (var i = 0; i < count; i++) {
				var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(IntPtr.Add(buf, i * stride));
				var name = Marshal.PtrToStringUni(item.Name) ?? "";
				list.Add(new Ctr { Name = name, Value = item.Value.doubleValue });
			}
			return list;
		}
		finally { Marshal.FreeHGlobal(buf); }
	}

	sealed class Ctr {
		public string Name;
		public double Value;
	}

	static void nvml(Snap s) {
		if (!loadnvml()) return;
		if (nvmlCount(out var n) != 0 || n == 0) return;
		var best = -1;
		uint bestUse = 0;
		double temp = double.NaN;
		double power = double.NaN;
		for (uint i = 0; i < n; i++) {
			if (nvmlHandle(i, out var dev) != 0) continue;
			uint use = 0;
			var gotUse = nvmlUtil(dev, out var util) == 0;
			if (gotUse) use = util.gpu;
			uint t = 0;
			var gotT = nvmlTemp(dev, 0, out t) == 0;
			uint mw = 0;
			var gotP = nvmlPower(dev, out mw) == 0;
			if (best < 0 || use >= bestUse) {
				best = (int)i;
				bestUse = use;
				if (gotT) temp = t;
				if (gotP) power = mw / 1000.0;
			}
		}
		if (best < 0) return;
		var pdhGpu = s.Gpu;
		if (double.IsNaN(pdhGpu) || bestUse > pdhGpu) s.Gpu = bestUse;
		if (!double.IsNaN(temp)) s.GpuTemp = temp;
		if (!double.IsNaN(power)) s.GpuPower = power;
	}

	static bool loadnvml() {
		if (nvmlTried) return nvmlOk;
		nvmlTried = true;
		var paths = new[] {
			Path.Combine(Environment.SystemDirectory, "nvml.dll"),
			@"C:\Program Files\NVIDIA Corporation\NVSMI\nvml.dll",
		};
		foreach (var path in paths) {
			if (!File.Exists(path)) continue;
			nvmlLib = LoadLibrary(path);
			if (nvmlLib == IntPtr.Zero) continue;
			nvmlInit = get<NvmlInit>("nvmlInit_v2");
			nvmlCount = get<NvmlCount>("nvmlDeviceGetCount_v2");
			if (nvmlCount == null) nvmlCount = get<NvmlCount>("nvmlDeviceGetCount");
			nvmlHandle = get<NvmlHandle>("nvmlDeviceGetHandleByIndex_v2");
			if (nvmlHandle == null) nvmlHandle = get<NvmlHandle>("nvmlDeviceGetHandleByIndex");
			nvmlTemp = get<NvmlTemp>("nvmlDeviceGetTemperature");
			nvmlPower = get<NvmlPower>("nvmlDeviceGetPowerUsage");
			nvmlUtil = get<NvmlUtilFn>("nvmlDeviceGetUtilizationRates");
			if (nvmlInit == null || nvmlCount == null || nvmlHandle == null || nvmlInit() != 0) {
				nvmlLib = IntPtr.Zero;
				continue;
			}
			nvmlOk = nvmlTemp != null && nvmlPower != null && nvmlUtil != null;
			if (nvmlOk) return true;
		}
		return false;
	}

	static T get<T>(string name) where T : Delegate {
		var p = GetProcAddress(nvmlLib, name);
		if (p == IntPtr.Zero) return null;
		return Marshal.GetDelegateForFunctionPointer<T>(p);
	}

	static double cputemp() =>
		kelvinzone("SELECT Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation", "Temperature");

	static double kelvinzone(string query, string field) {
		double best = double.NaN;
		try {
			using var search = new ManagementObjectSearcher(query);
			search.Options.Timeout = TimeSpan.FromSeconds(2);
			foreach (ManagementObject mo in search.Get()) {
				using (mo) {
					var raw = mo[field];
					if (raw == null) continue;
					var n = Convert.ToDouble(raw);
					var c = n;
					if (n > 2000) c = n / 10.0 - 273.15;
					else if (n > 200) c = n - 273.15;
					if (c < 5 || c > 120) continue;
					if (double.IsNaN(best) || c > best) best = c;
				}
			}
		}
		catch { }
		return best;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct MEMORYSTATUSEX {
		public uint Length;
		public uint MemoryLoad;
		public ulong TotalPhys;
		public ulong AvailPhys;
		public ulong TotalPageFile;
		public ulong AvailPageFile;
		public ulong TotalVirtual;
		public ulong AvailVirtual;
		public ulong AvailExtendedVirtual;
	}

	[StructLayout(LayoutKind.Explicit)]
	struct PDH_FMT_COUNTERVALUE {
		[FieldOffset(0)] public uint CStatus;
		[FieldOffset(8)] public double doubleValue;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct PDH_FMT_COUNTERVALUE_ITEM {
		public IntPtr Name;
		public PDH_FMT_COUNTERVALUE Value;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct NVML_UTIL {
		public uint gpu;
		public uint memory;
	}

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	delegate int NvmlInit();
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	delegate int NvmlCount(out uint count);
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	delegate int NvmlHandle(uint index, out IntPtr device);
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	delegate int NvmlTemp(IntPtr device, int sensor, out uint temp);
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	delegate int NvmlPower(IntPtr device, out uint mw);
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	delegate int NvmlUtilFn(IntPtr device, out NVML_UTIL util);

	[DllImport("kernel32.dll")]
	static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME idle,
		out System.Runtime.InteropServices.ComTypes.FILETIME kernel,
		out System.Runtime.InteropServices.ComTypes.FILETIME user);

	[DllImport("kernel32.dll")]
	static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX stat);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	static extern IntPtr LoadLibrary(string path);

	[DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
	static extern IntPtr GetProcAddress(IntPtr module, string name);

	[DllImport("pdh.dll", CharSet = CharSet.Unicode)]
	static extern uint PdhOpenQuery(string source, IntPtr user, out IntPtr query);

	[DllImport("pdh.dll", CharSet = CharSet.Unicode)]
	static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);

	[DllImport("pdh.dll")]
	static extern uint PdhCollectQueryData(IntPtr query);

	[DllImport("pdh.dll", CharSet = CharSet.Unicode)]
	static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, ref uint count, IntPtr buffer);
}
