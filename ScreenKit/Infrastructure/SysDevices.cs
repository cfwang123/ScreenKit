using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenKit;

/// <summary>系统信息窗口使用的其它只读设备扫描。</summary>
static class SysDevices {
	static readonly Guid CameraClass = new("ca3e7ab9-b4c3-4ae6-8251-579ef933890f");
	static readonly Guid ImageClass = new("6bdd1fc6-810f-11d0-bec7-08002be2092f");
	static readonly Guid BluetoothClass = new("e0cbf06c-cd8b-4647-bb8a-263b43f0f974");
	static readonly Guid KeyboardClass = new("4d36e96b-e325-11ce-bfc1-08002be10318");
	static readonly Guid MouseClass = new("4d36e96f-e325-11ce-bfc1-08002be10318");
	static readonly Guid HidClass = new("745a17a0-74d3-11d0-b6fe-00a0c90f57da");
	static readonly Guid SystemClass = new("4d36e97d-e325-11ce-bfc1-08002be10318");

	public static List<UsbDev> Scan(string kind) => kind switch {
		"display" => ScanDisplays(),
		"camera" => ScanCameras(),
		"bt" => ScanBluetooth(),
		"net" => ScanNetwork(),
		"input" => ScanInput(),
		"disk" => ScanDisks(),
		"battery" => ScanBatteries(),
		"pci" => ScanSystemDevices(),
		"problem" => ScanProblems(),
		_ => throw new ArgumentException("unknown device kind: " + kind),
	};

	public static List<UsbDev> ScanDisplays() {
		var rows = new List<UsbDev>();
		var edids = readedids();
		var screens = System.Windows.Forms.Screen.AllScreens;
		for (var i = 0; i < screens.Length; i++) {
			var screen = screens[i];
			var dd = new DisplayDevice { Cb = Marshal.SizeOf(typeof(DisplayDevice)) };
			var hasdev = EnumDisplayDevices(screen.DeviceName, 0, ref dd, 0);
			var mode = new DevMode { DmSize = (short)Marshal.SizeOf(typeof(DevMode)) };
			var hasmode = EnumDisplaySettings(screen.DeviceName, -1, ref mode);
			var edid = findedid(edids, hasdev ? dd.DeviceId : "", i, screens.Length);
			var name = edid.Name;
			if (name.Length == 0 && hasdev) name = clean(dd.DeviceString);
			if (name.Length == 0) name = screen.DeviceName;
			var resolution = $"{screen.Bounds.Width} × {screen.Bounds.Height}";
			var refresh = hasmode && mode.DmDisplayFrequency > 1 ? mode.DmDisplayFrequency + " Hz" : "";
			var dpi = dpiat(screen.Bounds.Left + 1, screen.Bounds.Top + 1);
			var props = new List<UsbPair>();
			add(props, "usb.p.name", name);
			add(props, "dev.p.device", screen.DeviceName);
			add(props, "dev.p.primary", screen.Primary ? Loc.T("usb.yes") : "");
			add(props, "dev.p.resolution", resolution);
			add(props, "dev.p.refresh", refresh);
			add(props, "dev.p.dpi", dpi);
			add(props, "dev.p.bounds", rect(screen.Bounds));
			add(props, "dev.p.work", rect(screen.WorkingArea));
			add(props, "sys.p.vendor", edid.Manufacturer);
			add(props, "sys.p.model", edid.Name);
			add(props, "sys.p.serial", edid.Serial);
			add(props, "dev.p.pnp", hasdev ? dd.DeviceId : "");
			var row = new UsbDev {
				Name = name,
				ClassName = resolution,
				Com = refresh,
				Status = screen.Primary ? Loc.T("display.primary") : Loc.T("usb.ok"),
				InstanceId = hasdev && dd.DeviceId.Length > 0 ? dd.DeviceId : screen.DeviceName,
				Props = props,
				Active = true,
			};
			finish(row);
			rows.Add(row);
		}
		return rows;
	}

	public static List<UsbDev> ScanCameras() => UsbList.ScanClasses(CameraClass, ImageClass);

	public static List<UsbDev> ScanBluetooth() {
		var rows = UsbList.ScanClasses(BluetoothClass);
		rows.AddRange(UsbList.ScanEnumerators("BTH", "BTHENUM", "BTHLEDEVICE"));
		return distinct(rows);
	}

	public static List<UsbDev> ScanNetwork() {
		var rows = new List<UsbDev>();
		var pnp = netpnp();
		foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()) {
			var props = new List<UsbPair>();
			var id = nic.Id ?? "";
			pnp.TryGetValue(id.Trim('{', '}'), out var extra);
			var status = nicstatus(nic.OperationalStatus);
			var speed = speedtext(nic.Speed);
			var mac = mactext(nic.GetPhysicalAddress());
			add(props, "usb.p.name", nic.Name);
			add(props, "sys.p.desc", nic.Description);
			add(props, "sys.p.kind", nic.NetworkInterfaceType.ToString());
			add(props, "sys.p.status", status);
			add(props, "sys.p.speed", speed);
			add(props, "sys.p.mac", mac);
			add(props, "dev.p.device", id);
			add(props, "usb.p.mfg", extra?.Manufacturer);
			add(props, "dev.p.pnp", extra?.PnpId);
			try {
				var ip = nic.GetIPProperties();
				add(props, "sys.p.ipv4", joinaddr(ip.UnicastAddresses.Select(x => x.Address), AddressFamily.InterNetwork));
				add(props, "sys.p.ipv6", joinaddr(ip.UnicastAddresses.Select(x => x.Address), AddressFamily.InterNetworkV6));
				add(props, "sys.p.gw", joinaddr(ip.GatewayAddresses.Select(x => x.Address), AddressFamily.InterNetwork));
				add(props, "dev.p.dns", joinaddr(ip.DnsAddresses, AddressFamily.Unspecified));
			}
			catch { }
			var row = new UsbDev {
				Name = nic.Name,
				ClassName = nic.NetworkInterfaceType.ToString(),
				Com = speed,
				Status = status,
				InstanceId = id.Length > 0 ? id : nic.Name,
				Props = props,
				Active = nic.OperationalStatus == OperationalStatus.Up,
			};
			finish(row);
			rows.Add(row);
		}
		rows.Sort((a, b) => {
			var c = b.Active.CompareTo(a.Active);
			return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
		});
		return rows;
	}

	public static List<UsbDev> ScanInput() =>
		UsbList.ScanClasses(KeyboardClass, MouseClass, HidClass);

	public static List<UsbDev> ScanDisks() {
		var rows = new List<UsbDev>();
		var health = diskhealth();
		using var search = new ManagementObjectSearcher(
			"SELECT DeviceID, PNPDeviceID, Model, Manufacturer, Size, InterfaceType, MediaType, SerialNumber, FirmwareRevision, Partitions, Status FROM Win32_DiskDrive");
		using var found = search.Get();
		foreach (ManagementObject item in found) {
			using (item) {
				var model = wstr(item, "Model");
				var serial = wstr(item, "SerialNumber");
				var size = bytestext(wulong(item, "Size"));
				var iface = wstr(item, "InterfaceType");
				var status = wstr(item, "Status");
				var h = findhealth(health, serial, model);
				if (h.Length > 0) status = h;
				var props = new List<UsbPair>();
				add(props, "sys.p.model", model);
				add(props, "usb.p.mfg", wstr(item, "Manufacturer"));
				add(props, "sys.p.size", size);
				add(props, "sys.p.iface", iface);
				add(props, "sys.p.media", wstr(item, "MediaType"));
				add(props, "sys.p.serial", serial);
				add(props, "dev.p.firmware", wstr(item, "FirmwareRevision"));
				add(props, "dev.p.partitions", wstr(item, "Partitions"));
				add(props, "dev.p.health", status);
				add(props, "dev.p.device", wstr(item, "DeviceID"));
				add(props, "dev.p.pnp", wstr(item, "PNPDeviceID"));
				var row = new UsbDev {
					Name = model.Length > 0 ? model : wstr(item, "DeviceID"),
					ClassName = iface,
					Com = size,
					Status = status,
					InstanceId = wstr(item, "PNPDeviceID"),
					Props = props,
					Active = true,
				};
				finish(row);
				rows.Add(row);
			}
		}
		return rows;
	}

	public static List<UsbDev> ScanBatteries() {
		var rows = new List<UsbDev>();
		var full = wmifirst(@"root\wmi", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", "FullChargedCapacity");
		var cycles = wmifirst(@"root\wmi", "SELECT CycleCount FROM BatteryCycleCount", "CycleCount");
		using var search = new ManagementObjectSearcher(
			"SELECT DeviceID, PNPDeviceID, Name, Status, BatteryStatus, EstimatedChargeRemaining, EstimatedRunTime, DesignCapacity, FullChargeCapacity, DesignVoltage, Chemistry FROM Win32_Battery");
		using var found = search.Get();
		foreach (ManagementObject item in found) {
			using (item) {
				var name = wstr(item, "Name");
				var charge = wstr(item, "EstimatedChargeRemaining");
				var status = batterystatus(wint(item, "BatteryStatus"));
				var props = new List<UsbPair>();
				add(props, "usb.p.name", name);
				add(props, "sys.p.status", status);
				add(props, "dev.p.charge", charge.Length > 0 ? charge + "%" : "");
				add(props, "dev.p.design", capacity(wstr(item, "DesignCapacity")));
				add(props, "dev.p.full", capacity(wstr(item, "FullChargeCapacity").Length > 0
					? wstr(item, "FullChargeCapacity") : full));
				add(props, "dev.p.voltage", voltage(wstr(item, "DesignVoltage")));
				add(props, "dev.p.chemistry", chemistry(wint(item, "Chemistry")));
				add(props, "dev.p.cycles", cycles);
				add(props, "dev.p.device", wstr(item, "DeviceID"));
				add(props, "dev.p.pnp", wstr(item, "PNPDeviceID"));
				var row = new UsbDev {
					Name = name.Length > 0 ? name : wstr(item, "DeviceID"),
					ClassName = chemistry(wint(item, "Chemistry")),
					Com = charge.Length > 0 ? charge + "%" : "",
					Status = status,
					InstanceId = wstr(item, "PNPDeviceID"),
					Props = props,
					Active = true,
				};
				finish(row);
				rows.Add(row);
			}
		}
		return rows;
	}

	public static List<UsbDev> ScanSystemDevices() {
		var rows = UsbList.ScanClasses(SystemClass);
		rows.AddRange(UsbList.ScanEnumerators("PCI", "ACPI"));
		return distinct(rows);
	}

	public static List<UsbDev> ScanProblems() => UsbList.ScanProblems();

	static List<UsbDev> distinct(IEnumerable<UsbDev> source) {
		var map = new Dictionary<string, UsbDev>(StringComparer.OrdinalIgnoreCase);
		foreach (var row in source) {
			var key = string.IsNullOrWhiteSpace(row.InstanceId) ? $"{row.ClassName}\n{row.Name}" : row.InstanceId;
			if (!map.ContainsKey(key)) map[key] = row;
		}
		var rows = map.Values.ToList();
		rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
		return rows;
	}

	static void finish(UsbDev row) {
		if (string.IsNullOrWhiteSpace(row.InstanceId)) row.InstanceId = row.Name ?? "";
		var sb = new StringBuilder();
		sb.Append(row.Name).Append('\n').Append(row.ClassName).Append('\n').Append(row.Com).Append('\n');
		sb.Append(row.Status).Append('\n').Append(row.InstanceId);
		foreach (var p in row.Props ?? new List<UsbPair>())
			sb.Append('\n').Append(p.Name).Append('\n').Append(p.Value);
		row.Filter = sb.ToString().ToLowerInvariant();
	}

	static void add(List<UsbPair> props, string key, string value) {
		value = (value ?? "").Trim();
		if (value.Length == 0) return;
		props.Add(new UsbPair { Name = Loc.T(key), Value = value });
	}

	static Dictionary<string, NetPnp> netpnp() {
		var map = new Dictionary<string, NetPnp>(StringComparer.OrdinalIgnoreCase);
		try {
			using var search = new ManagementObjectSearcher(
				"SELECT GUID, PNPDeviceID, Manufacturer FROM Win32_NetworkAdapter WHERE GUID IS NOT NULL");
			using var found = search.Get();
			foreach (ManagementObject item in found) {
				using (item) {
					var id = wstr(item, "GUID").Trim('{', '}');
					if (id.Length == 0) continue;
					map[id] = new NetPnp {
						PnpId = wstr(item, "PNPDeviceID"),
						Manufacturer = wstr(item, "Manufacturer"),
					};
				}
			}
		}
		catch (Exception ex) {
			if (SysDeviceAdmin.IsAccessDenied(ex)) throw;
		}
		return map;
	}

	static List<DiskHealth> diskhealth() {
		var rows = new List<DiskHealth>();
		try {
			var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
			using var search = new ManagementObjectSearcher(scope,
				new ObjectQuery("SELECT FriendlyName, SerialNumber, HealthStatus FROM MSFT_PhysicalDisk"));
			using var found = search.Get();
			foreach (ManagementObject item in found) {
				using (item) {
					rows.Add(new DiskHealth {
						Name = wstr(item, "FriendlyName"),
						Serial = wstr(item, "SerialNumber"),
						Status = healthtext(wint(item, "HealthStatus")),
					});
				}
			}
		}
		catch (Exception ex) {
			if (SysDeviceAdmin.IsAccessDenied(ex)) throw;
		}
		return rows;
	}

	static string findhealth(List<DiskHealth> rows, string serial, string model) {
		foreach (var row in rows) {
			if (serial.Length > 0 && row.Serial.Length > 0 &&
				string.Equals(serial.Trim(), row.Serial.Trim(), StringComparison.OrdinalIgnoreCase)) return row.Status;
		}
		foreach (var row in rows) {
			if (model.Length > 0 && row.Name.Length > 0 &&
				model.IndexOf(row.Name, StringComparison.OrdinalIgnoreCase) >= 0) return row.Status;
		}
		return "";
	}

	static List<MonitorEdid> readedids() {
		var rows = new List<MonitorEdid>();
		try {
			var scope = new ManagementScope(@"\\.\root\wmi");
			using var search = new ManagementObjectSearcher(scope,
				new ObjectQuery("SELECT InstanceName, ManufacturerName, UserFriendlyName, SerialNumberID FROM WmiMonitorID"));
			using var found = search.Get();
			foreach (ManagementObject item in found) {
				using (item) {
					rows.Add(new MonitorEdid {
						Instance = wstr(item, "InstanceName"),
						Manufacturer = warr(item, "ManufacturerName"),
						Name = warr(item, "UserFriendlyName"),
						Serial = warr(item, "SerialNumberID"),
					});
				}
			}
		}
		catch (Exception ex) {
			if (SysDeviceAdmin.IsAccessDenied(ex)) throw;
		}
		return rows;
	}

	static MonitorEdid findedid(List<MonitorEdid> rows, string deviceId, int index, int screenCount) {
		var want = (deviceId ?? "").Replace("MONITOR\\", "DISPLAY\\").ToUpperInvariant();
		foreach (var row in rows) {
			if (want.Length > 0 && row.Instance.ToUpperInvariant().StartsWith(want, StringComparison.Ordinal)) return row;
		}
		if (rows.Count == screenCount && index >= 0 && index < rows.Count) return rows[index];
		return new MonitorEdid();
	}

	static string wmifirst(string scopePath, string query, string property) {
		try {
			var scope = new ManagementScope($@"\\.\{scopePath}");
			using var search = new ManagementObjectSearcher(scope, new ObjectQuery(query));
			using var found = search.Get();
			foreach (ManagementObject item in found) {
				using (item) return wstr(item, property);
			}
		}
		catch (Exception ex) {
			if (SysDeviceAdmin.IsAccessDenied(ex)) throw;
		}
		return "";
	}

	static string dpiat(int x, int y) {
		try {
			var monitor = MonitorFromPoint(new PointNative { X = x, Y = y }, 2);
			if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out var dx, out var dy) == 0)
				return $"{dx} × {dy} ({dx / 96.0:0.##}x)";
		}
		catch { }
		return "";
	}

	static string rect(System.Drawing.Rectangle r) => $"{r.Left}, {r.Top}, {r.Width} × {r.Height}";

	static string joinaddr(IEnumerable<System.Net.IPAddress> source, AddressFamily family) {
		var list = source.Where(x => x != null && (family == AddressFamily.Unspecified || x.AddressFamily == family))
			.Select(x => x.ToString()).Distinct().ToList();
		return string.Join(", ", list);
	}

	static string nicstatus(OperationalStatus status) {
		if (status == OperationalStatus.Up) return Loc.T("sys.net.up");
		if (status == OperationalStatus.Down) return Loc.T("sys.net.down");
		return status.ToString();
	}

	static string speedtext(long speed) {
		if (speed <= 0) return "";
		if (speed >= 1000000000) return $"{speed / 1000000000.0:0.##} Gbps";
		if (speed >= 1000000) return $"{speed / 1000000.0:0.##} Mbps";
		return $"{speed / 1000.0:0.##} Kbps";
	}

	static string mactext(PhysicalAddress address) {
		var bytes = address?.GetAddressBytes();
		return bytes == null || bytes.Length == 0 ? "" : BitConverter.ToString(bytes);
	}

	static string bytestext(ulong value) {
		if (value == 0) return "";
		const double gb = 1024.0 * 1024 * 1024;
		return value >= gb ? $"{value / gb:0.##} GB" : $"{value / 1048576.0:0.##} MB";
	}

	static string capacity(string raw) =>
		ulong.TryParse(raw, out var n) && n > 0 ? $"{n} mWh" : "";

	static string voltage(string raw) =>
		ulong.TryParse(raw, out var n) && n > 0 ? $"{n} mV" : "";

	static string healthtext(int value) => value switch {
		0 => Loc.T("dev.health.ok"),
		1 => Loc.T("dev.health.warning"),
		2 => Loc.T("dev.health.bad"),
		_ => Loc.T("dev.health.unknown"),
	};

	static string batterystatus(int value) => value switch {
		1 => Loc.T("dev.battery.discharging"),
		2 => Loc.T("dev.battery.ac"),
		3 => Loc.T("dev.battery.full"),
		6 or 7 or 8 or 9 => Loc.T("dev.battery.charging"),
		_ => Loc.T("dev.health.unknown"),
	};

	static string chemistry(int value) => value switch {
		3 => "Lead Acid",
		4 => "Nickel Cadmium",
		5 => "Nickel Metal Hydride",
		6 => "Lithium-ion",
		7 => "Zinc Air",
		8 => "Lithium Polymer",
		_ => "",
	};

	static string clean(string value) => (value ?? "").Trim().Trim('\0');

	static string wstr(ManagementBaseObject item, string name) {
		try { return clean(item[name]?.ToString()); }
		catch { return ""; }
	}

	static int wint(ManagementBaseObject item, string name) {
		try { return Convert.ToInt32(item[name]); }
		catch { return 0; }
	}

	static ulong wulong(ManagementBaseObject item, string name) {
		try { return Convert.ToUInt64(item[name]); }
		catch { return 0; }
	}

	static string warr(ManagementBaseObject item, string name) {
		try {
			if (item[name] is not ushort[] chars) return "";
			return new string(chars.Where(x => x != 0).Select(x => (char)x).ToArray()).Trim();
		}
		catch { return ""; }
	}

	sealed class NetPnp {
		public string PnpId;
		public string Manufacturer;
	}

	sealed class DiskHealth {
		public string Name;
		public string Serial;
		public string Status;
	}

	sealed class MonitorEdid {
		public string Instance = "";
		public string Manufacturer = "";
		public string Name = "";
		public string Serial = "";
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	struct DisplayDevice {
		public int Cb;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string DeviceName;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string DeviceString;
		public int StateFlags;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string DeviceId;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string DeviceKey;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	struct DevMode {
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string DmDeviceName;
		public short DmSpecVersion;
		public short DmDriverVersion;
		public short DmSize;
		public short DmDriverExtra;
		public int DmFields;
		public int DmPositionX;
		public int DmPositionY;
		public int DmDisplayOrientation;
		public int DmDisplayFixedOutput;
		public short DmColor;
		public short DmDuplex;
		public short DmYResolution;
		public short DmTtOption;
		public short DmCollate;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string DmFormName;
		public short DmLogPixels;
		public int DmBitsPerPel;
		public int DmPelsWidth;
		public int DmPelsHeight;
		public int DmDisplayFlags;
		public int DmDisplayFrequency;
		public int DmIcmMethod;
		public int DmIcmIntent;
		public int DmMediaType;
		public int DmDitherType;
		public int DmReserved1;
		public int DmReserved2;
		public int DmPanningWidth;
		public int DmPanningHeight;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct PointNative {
		public int X;
		public int Y;
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	static extern bool EnumDisplayDevices(string device, int number, ref DisplayDevice display, int flags);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	static extern bool EnumDisplaySettings(string device, int mode, ref DevMode devMode);

	[DllImport("user32.dll")]
	static extern IntPtr MonitorFromPoint(PointNative point, int flags);

	[DllImport("shcore.dll")]
	static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}
