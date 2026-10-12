using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Vortice.DXGI;

namespace ScreenKit;

/// <summary>只读采集本机处理器、显卡、内存、硬盘和网络。</summary>
static class SysInfo {
	public static List<UsbPair> Scan() {
		var rows = new List<UsbPair>();
		try { pc(rows); } catch { }
		try { cpu(rows); } catch { }
		try { mem(rows); } catch { }
		try { gpu(rows); } catch { }
		try { disks(rows); } catch { }
		try { volumes(rows); } catch { }
		try { nics(rows); } catch { }
		return rows;
	}

	static void pc(List<UsbPair> rows) {
		add(rows, "sys.sec.pc", "sys.p.machine", Environment.MachineName);
		add(rows, "sys.sec.pc", "sys.p.user", Environment.UserName);
		add(rows, "sys.sec.pc", "sys.p.os", osver());
		add(rows, "sys.sec.pc", "sys.p.arch", Environment.Is64BitOperatingSystem ? "x64" : "x86");
		try {
			using (var s = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
			using (var col = s.Get()) {
				foreach (ManagementObject o in col) {
					using (o) {
						add(rows, "sys.sec.pc", "sys.p.vendor", wstr(o, "Manufacturer"));
						add(rows, "sys.sec.pc", "sys.p.model", wstr(o, "Model"));
					}
					break;
				}
			}
		}
		catch { }
		var ms = GetTickCount64();
		var boot = DateTime.Now.AddMilliseconds(-(double)ms);
		add(rows, "sys.sec.pc", "sys.p.boot", boot.ToString("yyyy-MM-dd HH:mm"));
		var days = (int)(ms / 86400000);
		var hours = (int)(ms / 3600000 % 24);
		var mins = (int)(ms / 60000 % 60);
		var up = days > 0 ? Loc.T("sys.uptime.d", days, hours) : Loc.T("sys.uptime.h", hours, mins);
		add(rows, "sys.sec.pc", "sys.p.uptime", up);
	}

	static string osver() {
		using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
		if (k == null) return Environment.OSVersion.VersionString;
		var product = (k.GetValue("ProductName") as string ?? "").Trim();
		var disp = (k.GetValue("DisplayVersion") as string ?? "").Trim();
		var build = (k.GetValue("CurrentBuildNumber") as string ?? "").Trim();
		var ubr = k.GetValue("UBR");
		var ver = product;
		if (disp.Length > 0) ver += " " + disp;
		if (build.Length > 0) ver += " (" + build + (ubr != null ? "." + ubr : "") + ")";
		return ver.Trim();
	}

	static void cpu(List<UsbPair> rows) {
		var name = "";
		var mhz = "";
		try {
			using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
			name = ((k?.GetValue("ProcessorNameString") as string) ?? "").Trim();
			var cur = k?.GetValue("~MHz");
			if (cur != null) mhz = cur.ToString();
		}
		catch { }
		var cores = 0;
		var logical = 0;
		var max = 0;
		try {
			using var s = new ManagementObjectSearcher(
				"SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
			using var col = s.Get();
			foreach (ManagementObject o in col) {
				using (o) {
					if (name.Length == 0) name = wstr(o, "Name");
					cores += wint(o, "NumberOfCores");
					logical += wint(o, "NumberOfLogicalProcessors");
					var m = wint(o, "MaxClockSpeed");
					if (m > max) max = m;
				}
			}
		}
		catch { }
		if (logical <= 0) logical = Environment.ProcessorCount;
		add(rows, "sys.sec.cpu", "sys.p.name", name);
		if (cores > 0) add(rows, "sys.sec.cpu", "sys.p.cores", cores.ToString());
		add(rows, "sys.sec.cpu", "sys.p.logical", logical.ToString());
		if (max > 0) add(rows, "sys.sec.cpu", "sys.p.mhz", max + " MHz");
		else add(rows, "sys.sec.cpu", "sys.p.mhz", mhz.Length == 0 ? "" : mhz + " MHz");
	}

	static void mem(List<UsbPair> rows) {
		var st = new MEMORYSTATUSEX { Length = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
		if (!GlobalMemoryStatusEx(ref st)) return;
		add(rows, "sys.sec.mem", "sys.p.total", bytes(st.TotalPhys));
		add(rows, "sys.sec.mem", "sys.p.free", bytes(st.AvailPhys));
		if (st.TotalPhys >= st.AvailPhys)
			add(rows, "sys.sec.mem", "sys.p.used", bytes(st.TotalPhys - st.AvailPhys));
		add(rows, "sys.sec.mem", "sys.p.load", st.MemoryLoad + "%");
		add(rows, "sys.sec.mem", "sys.p.page", bytes(st.TotalPageFile));
		add(rows, "sys.sec.mem", "sys.p.pagefree", bytes(st.AvailPageFile));
	}

	static void gpu(List<UsbPair> rows) {
		var found = new List<(string Name, uint Vendor, uint Device, ulong Dedicated, ulong Shared)>();
		using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
		for (var i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++) {
			using (adapter) {
				var d = adapter.Description1;
				if ((d.Flags & AdapterFlags.Software) != 0) continue;
				found.Add((d.Description?.Trim() ?? "", unchecked((uint)d.VendorId), unchecked((uint)d.DeviceId),
					(ulong)d.DedicatedVideoMemory, (ulong)d.SharedSystemMemory));
			}
		}
		for (var i = 0; i < found.Count; i++) {
			var g = found[i];
			var sec = found.Count == 1 ? "sys.sec.gpu" : null;
			var head = found.Count == 1 ? "" : " " + (i + 1);
			addn(rows, sec, head, "sys.sec.gpu", "sys.p.name", g.Name);
			addn(rows, sec, head, "sys.sec.gpu", "sys.p.vendor", vendor(g.Vendor));
			addn(rows, sec, head, "sys.sec.gpu", "sys.p.devid", g.Vendor.ToString("X4") + ":" + g.Device.ToString("X4"));
			if (g.Dedicated > 0) addn(rows, sec, head, "sys.sec.gpu", "sys.p.vram", bytes(g.Dedicated));
			if (g.Shared > 0) addn(rows, sec, head, "sys.sec.gpu", "sys.p.shared", bytes(g.Shared));
		}
	}

	static void disks(List<UsbPair> rows) {
		var found = new List<(string Model, string Size, string Iface, string Media, string Serial, string Status)>();
		using var s = new ManagementObjectSearcher(
			"SELECT Model, Size, InterfaceType, MediaType, SerialNumber, Status FROM Win32_DiskDrive");
		using var col = s.Get();
		foreach (ManagementObject o in col) {
			using (o) {
				found.Add((wstr(o, "Model"), wstr(o, "Size"), wstr(o, "InterfaceType"),
					wstr(o, "MediaType"), wstr(o, "SerialNumber"), wstr(o, "Status")));
			}
		}
		for (var i = 0; i < found.Count; i++) {
			var d = found[i];
			var sec = found.Count == 1 ? "sys.sec.disk" : null;
			var head = found.Count == 1 ? "" : " " + (i + 1);
			addn(rows, sec, head, "sys.sec.disk", "sys.p.model", d.Model);
			ulong n;
			if (ulong.TryParse(d.Size, out n)) addn(rows, sec, head, "sys.sec.disk", "sys.p.size", bytes(n));
			addn(rows, sec, head, "sys.sec.disk", "sys.p.iface", d.Iface);
			addn(rows, sec, head, "sys.sec.disk", "sys.p.media", d.Media);
			addn(rows, sec, head, "sys.sec.disk", "sys.p.serial", d.Serial);
			addn(rows, sec, head, "sys.sec.disk", "sys.p.status", d.Status);
		}
	}

	static void volumes(List<UsbPair> rows) {
		DriveInfo[] drives;
		try { drives = DriveInfo.GetDrives(); }
		catch { return; }
		foreach (var d in drives) {
			var letter = d.Name.TrimEnd('\\');
			var sec = Loc.T("sys.sec.vol") + " " + letter;
			try {
				addraw(rows, sec, "sys.p.kind", drivetype(d.DriveType));
				if (!d.IsReady) {
					addraw(rows, sec, "sys.p.status", Loc.T("sys.drive.na"));
					continue;
				}
				addraw(rows, sec, "sys.p.fs", d.DriveFormat);
				addraw(rows, sec, "sys.p.total", bytes((ulong)d.TotalSize));
				addraw(rows, sec, "sys.p.free", bytes((ulong)d.AvailableFreeSpace));
				addraw(rows, sec, "sys.p.label", d.VolumeLabel);
			}
			catch {
				addraw(rows, sec, "sys.p.status", Loc.T("sys.drive.na"));
			}
		}
	}

	static void nics(List<UsbPair> rows) {
		NetworkInterface[] all;
		try { all = NetworkInterface.GetAllNetworkInterfaces(); }
		catch { return; }
		foreach (var n in all) {
			if (noise(n)) continue;
			var sec = Loc.T("sys.sec.net") + " " + (n.Name ?? "");
			addraw(rows, sec, "sys.p.desc", n.Description);
			addraw(rows, sec, "sys.p.kind", n.NetworkInterfaceType.ToString());
			addraw(rows, sec, "sys.p.status", nicstat(n.OperationalStatus));
			var sp = nicbps(n.Speed);
			if (sp.Length > 0) addraw(rows, sec, "sys.p.speed", sp);
			addraw(rows, sec, "sys.p.mac", mac(n.GetPhysicalAddress()));
			try {
				var ip = n.GetIPProperties();
				var v4 = new List<string>();
				var v6 = new List<string>();
				foreach (var u in ip.UnicastAddresses) {
					if (u.Address.AddressFamily == AddressFamily.InterNetwork) v4.Add(u.Address.ToString());
					else if (u.Address.AddressFamily == AddressFamily.InterNetworkV6) v6.Add(u.Address.ToString());
				}
				if (v4.Count > 0) addraw(rows, sec, "sys.p.ipv4", string.Join(", ", v4));
				if (v6.Count > 0) addraw(rows, sec, "sys.p.ipv6", string.Join(", ", v6));
				var gw = new List<string>();
				foreach (var g in ip.GatewayAddresses) {
					if (g.Address == null) continue;
					if (g.Address.AddressFamily != AddressFamily.InterNetwork) continue;
					gw.Add(g.Address.ToString());
				}
				if (gw.Count > 0) addraw(rows, sec, "sys.p.gw", string.Join(", ", gw));
			}
			catch { }
		}
	}

	static bool noise(NetworkInterface n) {
		if (n.NetworkInterfaceType == NetworkInterfaceType.Loopback) return true;
		var d = n.Description ?? "";
		if (d.IndexOf("Teredo", StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (d.IndexOf("isatap", StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (d.IndexOf("Kernel Debug", StringComparison.OrdinalIgnoreCase) >= 0) return true;
		return false;
	}

	static string nicstat(OperationalStatus s) {
		if (s == OperationalStatus.Up) return Loc.T("sys.net.up");
		if (s == OperationalStatus.Down) return Loc.T("sys.net.down");
		return s.ToString();
	}

	static string nicbps(long bps) {
		if (bps <= 0) return "";
		if (bps >= 1000000000) return (bps / 1000000000.0).ToString("0.##") + " Gbps";
		if (bps >= 1000000) return (bps / 1000000.0).ToString("0.##") + " Mbps";
		return (bps / 1000.0).ToString("0.##") + " Kbps";
	}

	static string mac(PhysicalAddress a) {
		var b = a?.GetAddressBytes();
		if (b == null || b.Length == 0) return "";
		var zero = true;
		foreach (var x in b) if (x != 0) zero = false;
		if (zero) return "";
		return BitConverter.ToString(b);
	}

	static string drivetype(DriveType t) {
		if (t == DriveType.Fixed) return Loc.T("sys.drive.fixed");
		if (t == DriveType.Removable) return Loc.T("sys.drive.removable");
		if (t == DriveType.Network) return Loc.T("sys.drive.network");
		if (t == DriveType.CDRom) return Loc.T("sys.drive.cd");
		if (t == DriveType.Ram) return Loc.T("sys.drive.ram");
		return Loc.T("sys.drive.other");
	}

	static string vendor(uint id) {
		if (id == 0x10DE) return "NVIDIA";
		if (id == 0x8086) return "Intel";
		if (id == 0x1002 || id == 0x1022) return "AMD";
		if (id == 0x1414) return "Microsoft";
		return id.ToString("X4");
	}

	static string bytes(ulong n) {
		const double gb = 1024.0 * 1024 * 1024;
		const double mb = 1024.0 * 1024;
		if (n >= gb) return (n / gb).ToString("0.0") + " GB";
		if (n >= mb) return (n / mb).ToString("0.0") + " MB";
		return n + " B";
	}

	static void add(List<UsbPair> rows, string sec, string key, string value) {
		addraw(rows, Loc.T(sec), key, value);
	}

	static void addn(List<UsbPair> rows, string secKey, string head, string secFallback, string key, string value) {
		var sec = secKey != null ? Loc.T(secKey) : Loc.T(secFallback) + head;
		addraw(rows, sec, key, value);
	}

	static void addraw(List<UsbPair> rows, string sec, string key, string value) {
		value = (value ?? "").Trim();
		if (value.Length == 0) return;
		rows.Add(new UsbPair { Name = sec + " · " + Loc.T(key), Value = value });
	}

	static string wstr(ManagementBaseObject o, string name) {
		try {
			var v = o[name];
			return v == null ? "" : v.ToString().Trim();
		}
		catch { return ""; }
	}

	static int wint(ManagementBaseObject o, string name) {
		try {
			var v = o[name];
			if (v == null) return 0;
			return Convert.ToInt32(v);
		}
		catch { return 0; }
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

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX stat);

	[DllImport("kernel32.dll")]
	static extern ulong GetTickCount64();
}
