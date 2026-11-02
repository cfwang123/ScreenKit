using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Globalization;
using Microsoft.Win32;
using Vortice.DXGI;

namespace ScreenKit;

/// <summary>只读采集本机处理器、显卡、内存、硬盘和网络。</summary>
static class SysInfo {
	public static List<UsbPair> Scan() {
		var rows = new List<UsbPair>();
		try { pc(rows); } catch { }
		try { osprefs(rows); } catch { }
		try { cpu(rows); } catch { }
		try { mem(rows); } catch { }
		try { sticks(rows); } catch { }
		try { board(rows); } catch { }
		try { bioss(rows); } catch { }
		try { chassis(rows); } catch { }
		try { gpu(rows); } catch { }
		try { gpudrv(rows); } catch { }
		try { disks(rows); } catch { }
		try { volumes(rows); } catch { }
		try { nics(rows); } catch { }
		try { wifi(rows); } catch { }
		try { shares(rows); } catch { }
		try { users(rows); } catch { }
		try { sessions(rows); } catch { }
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

	static void osprefs(List<UsbPair> rows) {
		using var s = new ManagementObjectSearcher(
			"SELECT InstallDate, OSLanguage, CurrentTimeZone FROM Win32_OperatingSystem");
		using var col = s.Get();
		foreach (ManagementObject o in col) {
			using (o) {
				var raw = wstr(o, "InstallDate");
				if (raw.Length > 0) {
					try {
						var dt = ManagementDateTimeConverter.ToDateTime(raw);
						add(rows, "sys.sec.pc", "sys.p.install", dt.ToString("yyyy-MM-dd"));
					}
					catch { }
				}
				var lcid = wint(o, "OSLanguage");
				if (lcid > 0) {
					try { add(rows, "sys.sec.pc", "sys.p.oslang", CultureInfo.GetCultureInfo(lcid).NativeName); }
					catch { }
				}
				var tz = wint(o, "CurrentTimeZone");
				var sign = tz < 0 ? "-" : "+";
				var abs = Math.Abs(tz);
				add(rows, "sys.sec.pc", "sys.p.tz", $"UTC{sign}{abs / 60:00}:{abs % 60:00}");
			}
			break;
		}
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
		memarray(rows);
	}

	/// <summary>主板上的内存槽总数和最大支持容量；MaxCapacity 单位是 KB。</summary>
	static void memarray(List<UsbPair> rows) {
		var slots = 0;
		ulong max = 0;
		using (var s = new ManagementObjectSearcher(
			"SELECT MemoryDevices, MaxCapacity FROM Win32_PhysicalMemoryArray"))
		using (var col = s.Get()) {
			foreach (ManagementObject o in col) {
				using (o) {
					slots += wint(o, "MemoryDevices");
					try {
						var cap = Convert.ToUInt64(o["MaxCapacity"] ?? 0);
						if (cap > max) max = cap;
					}
					catch { }
				}
			}
		}
		if (slots <= 0) return;
		var used = 0;
		using (var s = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory"))
		using (var col = s.Get()) {
			foreach (ManagementObject o in col) {
				using (o) used++;
			}
		}
		add(rows, "sys.sec.mem", "sys.p.slots", used > 0 ? $"{used} / {slots}" : slots.ToString());
		if (max > 0) add(rows, "sys.sec.mem", "sys.p.memmax", bytes(max * 1024UL));
	}

	static void board(List<UsbPair> rows) {
		using var s = new ManagementObjectSearcher(
			"SELECT Manufacturer, Product, Version, SerialNumber FROM Win32_BaseBoard");
		using var col = s.Get();
		foreach (ManagementObject o in col) {
			using (o) {
				add(rows, "sys.sec.board", "sys.p.vendor", wstr(o, "Manufacturer"));
				add(rows, "sys.sec.board", "sys.p.model", wstr(o, "Product"));
				add(rows, "sys.sec.board", "sys.p.boardver", wstr(o, "Version"));
				add(rows, "sys.sec.board", "sys.p.serial", wstr(o, "SerialNumber"));
			}
			break;
		}
	}

	static void bioss(List<UsbPair> rows) {
		using var s = new ManagementObjectSearcher(
			"SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate, SerialNumber FROM Win32_BIOS");
		using var col = s.Get();
		foreach (ManagementObject o in col) {
			using (o) {
				add(rows, "sys.sec.bios", "sys.p.vendor", wstr(o, "Manufacturer"));
				add(rows, "sys.sec.bios", "sys.p.boardver", wstr(o, "SMBIOSBIOSVersion"));
				add(rows, "sys.sec.bios", "sys.p.date", wmidate(wstr(o, "ReleaseDate")));
				add(rows, "sys.sec.bios", "sys.p.serial", wstr(o, "SerialNumber"));
			}
			break;
		}
	}

	static void chassis(List<UsbPair> rows) {
		using var s = new ManagementObjectSearcher(
			"SELECT Manufacturer, ChassisTypes, SerialNumber FROM Win32_SystemEnclosure");
		using var col = s.Get();
		foreach (ManagementObject o in col) {
			using (o) {
				add(rows, "sys.sec.chassis", "sys.p.vendor", wstr(o, "Manufacturer"));
				add(rows, "sys.sec.chassis", "sys.p.kind", chassistype(o));
				add(rows, "sys.sec.chassis", "sys.p.serial", wstr(o, "SerialNumber"));
			}
		}
	}

	static string chassistype(ManagementBaseObject o) {
		try {
			if (o["ChassisTypes"] is ushort[] list && list.Length > 0) return chassisname(list[0]);
		}
		catch { }
		return "";
	}

	static string chassisname(int n) {
		return n switch {
			3 => "Desktop",
			4 => "Low Profile Desktop",
			5 => "Pizza Box",
			6 => "Mini Tower",
			7 => "Tower",
			8 => "Portable",
			9 => "Laptop",
			10 => "Notebook",
			11 => "Hand Held",
			12 => "Docking Station",
			13 => "All in One",
			14 => "Sub Notebook",
			15 => "Space-saving",
			16 => "Lunch Box",
			17 => "Main System Chassis",
			23 => "Rack Mount",
			30 => "Tablet",
			31 => "Convertible",
			32 => "Detachable",
			35 => "Mini PC",
			36 => "Stick PC",
			_ => "Type " + n,
		};
	}

	/// <summary>WMI 的 CIM 时间串转成本地短日期，解析失败时退回取前 8 位。</summary>
	static string wmidate(string raw) {
		if (raw.Length == 0) return "";
		try { return ManagementDateTimeConverter.ToDateTime(raw).ToString("yyyy-MM-dd"); }
		catch { return raw.Length >= 8 ? raw.Substring(0, 4) + "-" + raw.Substring(4, 2) + "-" + raw.Substring(6, 2) : raw; }
	}

	static string wmidatetime(string raw) {
		if (raw.Length == 0) return "";
		try { return ManagementDateTimeConverter.ToDateTime(raw).ToString("yyyy-MM-dd HH:mm"); }
		catch { return raw; }
	}

	static void sticks(List<UsbPair> rows) {
		var found = new List<(string Slot, string Size, string Mhz, string Kind, string Vendor, string Part)>();
		using var s = new ManagementObjectSearcher(
			"SELECT BankLabel, DeviceLocator, Capacity, Speed, SMBIOSMemoryType, Manufacturer, PartNumber FROM Win32_PhysicalMemory");
		using var col = s.Get();
		foreach (ManagementObject o in col) {
			using (o) {
				var slot = wstr(o, "DeviceLocator");
				if (slot.Length == 0) slot = wstr(o, "BankLabel");
				var size = "";
				ulong n;
				if (ulong.TryParse(wstr(o, "Capacity"), out n) && n > 0) size = bytes(n);
				var mhz = wint(o, "Speed");
				found.Add((slot, size, mhz > 0 ? $"{mhz} MHz" : "", ramtype(wint(o, "SMBIOSMemoryType")),
					wstr(o, "Manufacturer"), wstr(o, "PartNumber")));
			}
		}
		for (var i = 0; i < found.Count; i++) {
			var d = found[i];
			var sec = found.Count == 1 ? "sys.sec.stick" : null;
			var head = found.Count == 1 ? "" : " " + (i + 1);
			addn(rows, sec, head, "sys.sec.stick", "sys.p.slot", d.Slot);
			addn(rows, sec, head, "sys.sec.stick", "sys.p.size", d.Size);
			addn(rows, sec, head, "sys.sec.stick", "sys.p.mhz", d.Mhz);
			addn(rows, sec, head, "sys.sec.stick", "sys.p.ramtype", d.Kind);
			addn(rows, sec, head, "sys.sec.stick", "sys.p.vendor", d.Vendor);
			addn(rows, sec, head, "sys.sec.stick", "sys.p.part", d.Part);
		}
	}

	static string ramtype(int n) {
		if (n == 20) return "DDR";
		if (n == 21) return "DDR2";
		if (n == 24) return "DDR3";
		if (n == 26) return "DDR4";
		if (n == 34) return "DDR5";
		if (n == 0) return "";
		return n.ToString();
	}

	static void gpu(List<UsbPair> rows) {
		var found = new List<(string Name, uint Vendor, uint Device, ulong Dedicated, ulong Shared)>();
		using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
		for (var i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++) {
			using (adapter) {
				var d = adapter.Description1;
				if ((d.Flags & AdapterFlags.Software) != 0) continue;
				found.Add((d.Description?.Trim() ?? "", unchecked((uint)d.VendorId), unchecked((uint)d.DeviceId),
					Convert.ToUInt64(d.DedicatedVideoMemory), Convert.ToUInt64(d.SharedSystemMemory)));
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

	static void gpudrv(List<UsbPair> rows) {
		var found = new List<(string Name, string Ver, string Date, string Mode)>();
		using (var s = new ManagementObjectSearcher(
			"SELECT Name, DriverVersion, DriverDate, VideoModeDescription FROM Win32_VideoController"))
		using (var col = s.Get()) {
			foreach (ManagementObject o in col) {
				using (o) {
					found.Add((wstr(o, "Name"), wstr(o, "DriverVersion"),
						wmidate(wstr(o, "DriverDate")), wstr(o, "VideoModeDescription")));
				}
			}
		}
		for (var i = 0; i < found.Count; i++) {
			var g = found[i];
			var sec = found.Count == 1 ? "sys.sec.gpudrv" : null;
			var head = found.Count == 1 ? "" : " " + (i + 1);
			addn(rows, sec, head, "sys.sec.gpudrv", "sys.p.name", g.Name);
			addn(rows, sec, head, "sys.sec.gpudrv", "sys.p.drv", g.Ver);
			addn(rows, sec, head, "sys.sec.gpudrv", "sys.p.date", g.Date);
			addn(rows, sec, head, "sys.sec.gpudrv", "sys.p.mode", g.Mode);
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

	/// <summary>无线网卡的 SSID 和信号；驱动没实现 MSNdis 类时静默跳过。</summary>
	static void wifi(List<UsbPair> rows) {
		try {
			using var s = new ManagementObjectSearcher(@"root\wmi",
				"SELECT InstanceName, SSID, SignalStrength FROM MSNdis_80211_ReceivedSignalStrength");
			using var col = s.Get();
			foreach (ManagementObject o in col) {
				using (o) {
					var ssid = wstr(o, "SSID");
					var name = wstr(o, "InstanceName");
					if (ssid.Length == 0 && name.Length == 0) continue;
					var sig = wint(o, "SignalStrength");
					var value = sig > 0 ? sig + "%" : "";
					rows.Add(new UsbPair {
						Name = Loc.T("sys.sec.wifi") + " · " + (ssid.Length > 0 ? ssid : name),
						Value = value,
					});
				}
			}
		}
		catch { }
	}

	static void shares(List<UsbPair> rows) {
		using var s = new ManagementObjectSearcher("SELECT Name, Path, Description FROM Win32_Share");
		using var col = s.Get();
		foreach (ManagementObject o in col) {
			using (o) {
				var name = wstr(o, "Name");
				if (name.Length == 0) continue;
				var path = wstr(o, "Path");
				var desc = wstr(o, "Description");
				var value = path.Length == 0 ? desc : desc.Length == 0 ? path : path + " · " + desc;
				rows.Add(new UsbPair { Name = Loc.T("sys.sec.shares") + " · " + name, Value = value });
			}
		}
	}

	static void users(List<UsbPair> rows) {
		using var s = new ManagementObjectSearcher(
			"SELECT Name, Disabled, Lockout, LocalAccount, SID FROM Win32_UserAccount WHERE LocalAccount=True");
		using var col = s.Get();
		var count = 0;
		foreach (ManagementObject o in col) {
			using (o) {
				if (++count > 30) break;
				var name = wstr(o, "Name");
				if (name.Length == 0) continue;
				var bits = new List<string>();
				if (wbool(o, "LocalAccount")) bits.Add(Loc.T("sys.p.local"));
				if (wbool(o, "Disabled")) bits.Add(Loc.T("sys.p.disabled"));
				if (wbool(o, "Lockout")) bits.Add(Loc.T("sys.p.locked"));
				var sid = wstr(o, "SID");
				if (sid.Length > 0) bits.Add(sid);
				rows.Add(new UsbPair {
					Name = Loc.T("sys.sec.users") + " · " + name,
					Value = string.Join(" · ", bits),
				});
			}
		}
	}

	static void sessions(List<UsbPair> rows) {
		using var s = new ManagementObjectSearcher(
			"SELECT LogonId, LogonType, StartTime FROM Win32_LogonSession");
		using var col = s.Get();
		var count = 0;
		foreach (ManagementObject o in col) {
			using (o) {
				if (++count > 20) break;
				var id = wstr(o, "LogonId");
				var start = wmidatetime(wstr(o, "StartTime"));
				var value = start.Length == 0 ? "#" + id : start + " · #" + id;
				rows.Add(new UsbPair {
					Name = Loc.T("sys.sec.sessions") + " · " + logontype(wint(o, "LogonType")),
					Value = value,
				});
			}
		}
	}

	static string logontype(int n) => n switch {
		0 => "System",
		2 => "Interactive",
		3 => "Network",
		4 => "Batch",
		5 => "Service",
		7 => "Unlock",
		8 => "Network Cleartext",
		9 => "New Credentials",
		10 => "Remote Interactive",
		11 => "Cached Interactive",
		12 => "Cached Remote",
		13 => "Cached Unlock",
		_ => "Type " + n,
	};

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

	static bool wbool(ManagementBaseObject o, string name) {
		try { return Convert.ToBoolean(o[name]); }
		catch { return false; }
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
