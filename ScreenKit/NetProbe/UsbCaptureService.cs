using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ScreenKit;

/// <summary>一块 USBPcap 根集线器。</summary>
sealed class UsbCapDevice {
	public string Path;
	public string Name;
	public override string ToString() => Name;
}

/// <summary>
/// USB 抓包。打开 \\.\USBPcapN，发 USBPcap 的缓冲与过滤 IOCTL 后读 pcap 流。
/// 需要安装 USBPcap 并以管理员运行；不经过 Npcap。
/// </summary>
sealed class UsbCaptureService : IDisposable {
	public static UsbCaptureService Instance { get; } = new();

	public const int MaxPackets = 20000;

	public bool IsRunning { get; private set; }
	public bool HasSession { get; private set; }
	public string Status { get; private set; } = "未抓包（点击「开始抓包」）";
	public event Action StatusChanged;

	readonly object gate = new();
	readonly LinkedList<PacketRow> rows = new();
	readonly ConcurrentDictionary<string, byte> devSeen = new();
	long seq;
	volatile bool stop;
	readonly List<Thread> threads = new();
	readonly List<IntPtr> handles = new();

	UsbCaptureService() { }

	/// <summary>命令行自测入口，转给解析器。</summary>
	public static int SelfTest(Action<string> log) => UsbPacketParser.SelfTest(log);

	/// <summary>
	/// 真抓一小段：按地址过滤跑几秒，看有没有读回调、包数和第一个包。
	/// 需要管理员权限，用于排查「开始抓包后一包都没有」。
	/// </summary>
	public static int LiveTest(Action<string> log, int seconds, IReadOnlyCollection<int> addresses) {
		var env = CheckEnvironment();
		log("env: " + (env ?? "ok"));
		var devs = ListDevices();
		log($"root hubs: {devs.Count} -> " + string.Join(", ", devs.Select(d => d.Name)));
		if (env != null || devs.Count == 0) return 1;
		log("filter: " + (addresses == null || addresses.Count == 0 ? "全部" : string.Join(",", addresses)));

		var flt = UsbPacketParser.AddressFilter(addresses);
		log($"filterBytes: {flt.Length} [{string.Join(" ", flt.Select(b => b.ToString("X2")))}]");

		var svc = new UsbCaptureService();
		var callbacks = 0;
		var bytes = 0;
		svc.RawRead = (n, raw) => { Interlocked.Increment(ref callbacks); Interlocked.Add(ref bytes, n); };
		svc.Start(devs, addresses);
		log("status: " + svc.Status);
		Thread.Sleep(Math.Max(1000, seconds * 1000));
		log($"readCallbacks={callbacks} bytes={bytes} packets={svc.Count} devices={svc.DeviceCount}");
		var snap = svc.Snapshot();
		foreach (var r in snap.Take(5))
			log($"  #{r.No} {r.ConnKey} {r.Protocol} {r.Flags} len={r.PayloadLength} {r.InfoText}");
		svc.Stop();
		log("after stop: " + svc.Status);
		svc.Dispose();
		return svc.Count > 0 ? 0 : 2;
	}

	/// <summary>每次底层读到字节就回调一次（字节数、原始内容），用于命令行走查。</summary>
	public Action<int, byte[]> RawRead { get; set; }

	/// <summary>缺驱动或未提权时返回提示，否则返回 null。</summary>
	public static string CheckEnvironment() {
		if (!SysDeviceAdmin.IsAdmin())
			return "USB 抓包需要管理员权限。点「以管理员身份打开」后重试。";
		if (ListDevices().Count == 0)
			return "未检测到 USBPcap。请安装 USBPcap（https://desowin.org/usbpcap/），安装时勾选 USBPcap 过滤驱动。";
		return null;
	}

	/// <summary>枚举 \\.\USBPcapN。优先查注册表枚举键，没有再扫 1..32。</summary>
	public static List<UsbCapDevice> ListDevices() {
		List<UsbCapDevice> list = new();
		try {
			using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USBPcap");
			var names = k?.GetSubKeyNames();
			if (names != null) {
				foreach (var n in names) addDevice(list, n);
			}
		} catch { }
		if (list.Count == 0) {
			for (var i = 1; i <= 32; i++) addDevice(list, "USBPcap" + i);
		}
		list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
		return list;
	}

	static void addDevice(List<UsbCapDevice> list, string name) {
		if (string.IsNullOrEmpty(name) || !name.StartsWith("USBPcap", StringComparison.OrdinalIgnoreCase)) return;
		foreach (var d in list)
			if (string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)) return;
		var path = @"\\.\" + name;
		var h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, 0, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
		if (h == INVALID || h == IntPtr.Zero) return;
		CloseHandle(h);
		list.Add(new UsbCapDevice { Path = path, Name = name });
	}

	public void Start(IReadOnlyList<UsbCapDevice> devs) => Start(devs, null);

	/// <param name="addresses">只抓这些设备地址（0..127）。为空则抓全部，含抓包过程中新插入的。</param>
	public void Start(IReadOnlyList<UsbCapDevice> devs, IReadOnlyCollection<int> addresses) {
		if (IsRunning) return;
		var env = CheckEnvironment();
		if (env != null) {
			Status = env;
			IsRunning = false;
			StatusChanged?.Invoke();
			return;
		}
		if (devs == null || devs.Count == 0) {
			Status = "没有可抓的 USB 根集线器。";
			StatusChanged?.Invoke();
			return;
		}

		stop = false;
		var opened = 0;
		foreach (var dev in devs) {
			var h = CreateFile(dev.Path, GENERIC_READ | GENERIC_WRITE, 0, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
			if (h == INVALID || h == IntPtr.Zero) continue;
			if (!setup(h, addresses)) { CloseHandle(h); continue; }
			handles.Add(h);
			opened++;
			var th = new Thread(() => readloop(h, dev.Name)) { IsBackground = true, Name = "usbcap-" + dev.Name };
			threads.Add(th);
			th.Start();
		}
		if (opened == 0) {
			Status = "未能打开任何 USBPcap 设备。请确认以管理员运行，且 USBPcap 过滤驱动已安装。";
			IsRunning = false;
		} else {
			IsRunning = true;
			HasSession = true;
			var scope = addresses == null || addresses.Count == 0 ? "全部设备" : $"{addresses.Count} 个地址";
			Status = $"抓包中 · {opened} 个根集线器 · {scope} · 缓存 {Count} 包";
		}
		StatusChanged?.Invoke();
	}

	/// <summary>snaplen 1MB、内核缓冲 1MB，再按地址位图开始过滤。</summary>
	static bool setup(IntPtr h, IReadOnlyCollection<int> addresses) {
		var size = new byte[4];
		writeU32(size, 0, 1024 * 1024);
		if (!ioctl(h, IOCTL_SET_SNAPLEN, size)) return false;
		if (!ioctl(h, IOCTL_SETUP_BUFFER, size)) return false;
		return ioctl(h, IOCTL_START_FILTERING, UsbPacketParser.AddressFilter(addresses));
	}

	static bool ioctl(IntPtr h, uint code, byte[] input) {
		return DeviceIoControl(h, code, input, input.Length, null, 0, out _, IntPtr.Zero);
	}

	void readloop(IntPtr h, string name) {
		_ = name;
		var buf = new byte[1024 * 1024];
		var evt = CreateEvent(IntPtr.Zero, true, false, null);
		var ov = new NativeOverlapped();
		var pend = false;
		var carry = new MemoryStream();
		try {
			while (!stop) {
				if (!pend) {
					ResetEvent(evt);
					ov = new NativeOverlapped { EventHandle = evt };
					if (!ReadFile(h, buf, buf.Length, out _, ref ov)) {
						if (Marshal.GetLastWin32Error() != ERROR_IO_PENDING) break;
					}
					pend = true;
				}
				var wr = WaitForSingleObject(evt, 300);
				if (stop) break;
				if (wr == WAIT_TIMEOUT) continue;
				if (wr != WAIT_OBJECT_0) break;
				if (!GetOverlappedResult(h, ref ov, out var got, false)) break;
				pend = false;
				if (got == 0) continue;
				try { RawRead?.Invoke((int)got, buf); } catch { }
				carry.Write(buf, 0, (int)got);
				drain(carry);
			}
		} catch { }
		finally {
			CancelIo(h);
			if (evt != IntPtr.Zero) CloseHandle(evt);
		}
	}

	/// <summary>USBPcap 读出来的是经典 pcap：先 24 字节文件头，之后每条 16 字节记录头加帧。</summary>
	void drain(MemoryStream carry) {
		var data = carry.GetBuffer();
		var len = (int)carry.Length;
		var pos = 0;
		if (len >= 24 && u32(data, 0) == 0xA1B2C3D4) pos = 24;
		while (pos + 16 <= len) {
			var incl = (int)u32(data, pos + 8);
			if (incl < 0 || incl > 16 * 1024 * 1024) { pos = len; break; }
			if (pos + 16 + incl > len) break;
			var frame = new byte[incl];
			Buffer.BlockCopy(data, pos + 16, frame, 0, incl);
			pos += 16 + incl;
			if (UsbPacketParser.TryParse(frame, out var row)) add(row);
		}
		var rest = len - pos;
		if (rest <= 0) carry.SetLength(0);
		else if (pos > 0) {
			Buffer.BlockCopy(data, pos, data, 0, rest);
			carry.SetLength(rest);
			carry.Position = rest;
		}
	}

	void add(PacketRow row) {
		row.No = (int)Interlocked.Increment(ref seq);
		var dev = row.ConnKey ?? "";
		devSeen.TryAdd(dev, 0);
		var dropped = false;
		lock (gate) {
			rows.AddLast(row);
			while (rows.Count > MaxPackets) { rows.RemoveFirst(); dropped = true; }
		}
		var now = Environment.TickCount;
		if (dropped || now - lastStatus >= 400) {
			lastStatus = now;
			Status = $"抓包中 · {devSeen.Count} 台设备 · 缓存 {Count} 包";
			StatusChanged?.Invoke();
		}
	}

	int lastStatus;

	public void Stop() {
		stop = true;
		foreach (var h in handles) { try { CancelIo(h); } catch { } }
		foreach (var t in threads) { try { t.Join(1500); } catch { } }
		foreach (var h in handles) { try { CloseHandle(h); } catch { } }
		handles.Clear();
		threads.Clear();
		IsRunning = false;
		Status = HasSession || Count > 0
			? $"已结束抓包 · 保留缓存 {Count} 包"
			: "未抓包（点击「开始抓包」）";
		StatusChanged?.Invoke();
	}

	public int Count { get { lock (gate) return rows.Count; } }
	public int DeviceCount => devSeen.Count;

	public List<PacketRow> Snapshot() {
		lock (gate) {
			List<PacketRow> list = new(rows.Count);
			foreach (var r in rows) list.Add(r);
			return list;
		}
	}

	public void Inject(PacketRow row) {
		if (row == null) return;
		if (row.No == 0) row.No = (int)Interlocked.Increment(ref seq);
		devSeen.TryAdd(row.ConnKey ?? "", 0);
		lock (gate) {
			rows.AddLast(row);
			while (rows.Count > MaxPackets) rows.RemoveFirst();
		}
		HasSession = true;
	}

	public void Clear() {
		lock (gate) rows.Clear();
		devSeen.Clear();
		Interlocked.Exchange(ref seq, 0);
		HasSession = false;
		Status = IsRunning ? "抓包中 · 缓存已清空 · 继续记录…" : "已清空 · 未抓包（点击「开始抓包」）";
		StatusChanged?.Invoke();
	}

	public void Dispose() {
		if (IsRunning) Stop();
	}

	static long u32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
	static void writeU32(byte[] b, int o, int v) {
		b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24);
	}

	const uint GENERIC_READ = 0x80000000;
	const uint GENERIC_WRITE = 0x40000000;
	const uint OPEN_EXISTING = 3;
	const uint FILE_FLAG_OVERLAPPED = 0x40000000;
	const uint METHOD_BUFFERED = 0;
	const uint FILE_READ_ACCESS = 1;
	const uint FILE_WRITE_ACCESS = 2;
	const uint FILE_DEVICE_UNKNOWN = 0x22;
	const int ERROR_IO_PENDING = 997;
	const uint WAIT_OBJECT_0 = 0;
	const uint WAIT_TIMEOUT = 258;
	static readonly IntPtr INVALID = new(-1);

	static uint ctl(uint fn, uint access) => (FILE_DEVICE_UNKNOWN << 16) | (access << 14) | (fn << 2) | METHOD_BUFFERED;
	static readonly uint IOCTL_SETUP_BUFFER = ctl(0x800, FILE_READ_ACCESS);
	static readonly uint IOCTL_START_FILTERING = ctl(0x801, FILE_READ_ACCESS | FILE_WRITE_ACCESS);
	static readonly uint IOCTL_SET_SNAPLEN = ctl(0x804, FILE_READ_ACCESS);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool CloseHandle(IntPtr h);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool DeviceIoControl(IntPtr h, uint code, byte[] input, int inLen, byte[] output, int outLen, out int returned, IntPtr ov);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool ReadFile(IntPtr h, byte[] buf, int len, out int read, ref NativeOverlapped ov);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool GetOverlappedResult(IntPtr h, ref NativeOverlapped ov, out uint read, bool wait);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool CancelIo(IntPtr h);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern IntPtr CreateEvent(IntPtr attrs, bool manual, bool initial, string name);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool ResetEvent(IntPtr h);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern uint WaitForSingleObject(IntPtr h, int ms);
}
