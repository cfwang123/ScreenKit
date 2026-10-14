using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ScreenKit;

/// <summary>一个已连接的 USB 设备，以及右侧要显示的参数。</summary>
public sealed class UsbDev {
	public string Name { get; set; }
	public string VidPid { get; set; }
	public string Com { get; set; }
	public string ClassName { get; set; }
	public string Status { get; set; }
	public string InstanceId { get; set; }
	public string Filter { get; set; }
	public List<UsbPair> Props { get; set; }
	public bool Active { get; set; }
	public uint NodeStatus { get; set; }
	public uint ProblemCode { get; set; }
}

/// <summary>参数名和值。</summary>
public sealed class UsbPair {
	public string Name { get; set; }
	public string Value { get; set; }
}

/// <summary>用 SetupAPI 只读列出当前 USB 枚举器上的设备，不打开设备。</summary>
static class UsbList {
	const int DIGCF_PRESENT = 2;
	const int DIGCF_ALLCLASSES = 4;
	const int CR_SUCCESS = 0;
	const uint DN_DRIVER_LOADED = 0x2;
	const uint DN_STARTED = 0x8;
	const uint DN_HAS_PROBLEM = 0x400;
	const uint DN_DISABLEABLE = 0x2000;
	const uint DN_REMOVABLE = 0x4000;
	const uint DEVPROP_TYPE_UINT32 = 7;
	const uint DEVPROP_TYPE_GUID = 13;
	const uint DEVPROP_TYPE_FILETIME = 16;
	const uint DEVPROP_TYPE_BOOLEAN = 17;
	const uint DEVPROP_TYPE_STRING = 18;
	const uint DEVPROP_TYPE_STRING_LIST = 0x2012;
	const uint DEVPROP_TYPE_BINARY = 0x1003;

	static readonly Guid GDev = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
	static readonly Guid GStat = new Guid("4340a6c5-93fa-4706-972c-7b648008a5a7");
	static readonly Guid GDrv = new Guid("a8b865dd-2e3d-4094-ad97-e593a70c75d6");
	static readonly Guid GBus = new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2");
	static readonly Guid GInst = new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57");
	static readonly Guid GBox = new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c");
	static readonly Guid GDate = new Guid("83da6326-97a6-4088-9453-a1923f573b29");

	public static List<UsbDev> Scan() {
		var h = SetupDiGetClassDevs(IntPtr.Zero, "USB", IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
		var list = finish(h);
		list.Sort((a, b) => {
			var c = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
			if (c != 0) return c;
			return string.Compare(a.InstanceId, b.InstanceId, StringComparison.OrdinalIgnoreCase);
		});
		return list;
	}

	/// <summary>当前串口。Ports 类里带 COM 的设备，再加上设备映射里还没出现的口。</summary>
	public static List<UsbDev> ScanPorts() {
		var guid = new Guid("4d36e978-e325-11ce-bfc1-08002be10318");
		var list = finish(SetupDiGetClassDevsGuid(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT));
		var keep = new List<UsbDev>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var dev in list) {
			var com = portname(dev.InstanceId);
			if (com.Length == 0) com = comin(dev.Name);
			if (!com.StartsWith("COM", StringComparison.OrdinalIgnoreCase)) continue;
			com = com.ToUpperInvariant();
			dev.Com = com;
			dev.Props.Insert(0, new UsbPair { Name = Loc.T("com.port"), Value = com });
			dev.Filter = (dev.Filter ?? "") + "\n" + com.ToLowerInvariant();
			if (seen.Add(com)) keep.Add(dev);
		}
		mergecom(keep, seen);
		keep.Sort((a, b) => {
			var c = comnum(a.Com).CompareTo(comnum(b.Com));
			if (c != 0) return c;
			return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
		});
		return keep;
	}

	public static List<UsbDev> ScanClasses(params Guid[] classes) {
		var list = new List<UsbDev>();
		foreach (var cls in classes) {
			var copy = cls;
			try { list.AddRange(finish(SetupDiGetClassDevsGuid(ref copy, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT))); }
			catch (Exception ex) {
				if (SysDeviceAdmin.IsAccessDenied(ex)) throw;
			}
		}
		return distinct(list);
	}

	public static List<UsbDev> ScanEnumerators(params string[] enumerators) {
		var list = new List<UsbDev>();
		foreach (var name in enumerators) {
			if (string.IsNullOrWhiteSpace(name)) continue;
			try { list.AddRange(finish(SetupDiGetClassDevs(IntPtr.Zero, name, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES))); }
			catch (Exception ex) {
				if (SysDeviceAdmin.IsAccessDenied(ex)) throw;
			}
		}
		return distinct(list);
	}

	public static List<UsbDev> ScanProblems() {
		var list = finish(SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES));
		return distinct(list.Where(x => x.ProblemCode != 0));
	}

	static List<UsbDev> distinct(IEnumerable<UsbDev> source) {
		var map = new Dictionary<string, UsbDev>(StringComparer.OrdinalIgnoreCase);
		foreach (var dev in source) {
			var key = string.IsNullOrWhiteSpace(dev.InstanceId) ? $"{dev.ClassName}\n{dev.Name}" : dev.InstanceId;
			if (!map.ContainsKey(key)) map[key] = dev;
		}
		var list = map.Values.ToList();
		list.Sort((a, b) => {
			var c = b.ProblemCode.CompareTo(a.ProblemCode);
			if (c != 0) return c;
			c = string.Compare(a.ClassName, b.ClassName, StringComparison.CurrentCultureIgnoreCase);
			if (c != 0) return c;
			return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
		});
		return list;
	}

	static List<UsbDev> finish(IntPtr h) {
		if (h == IntPtr.Zero || h == new IntPtr(-1))
			throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
		var list = new List<UsbDev>();
		try {
			var data = new SpDevinfoData { CbSize = Marshal.SizeOf(typeof(SpDevinfoData)) };
			for (var i = 0; SetupDiEnumDeviceInfo(h, i, ref data); i++) {
				list.Add(read(h, data));
				data = new SpDevinfoData { CbSize = Marshal.SizeOf(typeof(SpDevinfoData)) };
			}
		}
		finally { SetupDiDestroyDeviceInfoList(h); }
		return list;
	}

	static void mergecom(List<UsbDev> keep, HashSet<string> seen) {
		RegistryKey key = null;
		try {
			key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
			if (key == null) return;
			foreach (var name in key.GetValueNames()) {
				var com = (key.GetValue(name) as string ?? "").Trim().ToUpperInvariant();
				if (!com.StartsWith("COM", StringComparison.Ordinal) || !seen.Add(com)) continue;
				var props = new List<UsbPair> {
					new UsbPair { Name = Loc.T("com.port"), Value = com },
					new UsbPair { Name = Loc.T("com.map"), Value = name ?? "" },
				};
				keep.Add(new UsbDev {
					Name = com,
					Com = com,
					InstanceId = name ?? "",
					Status = "",
					ClassName = "",
					Props = props,
					Filter = (com + "\n" + name).ToLowerInvariant(),
				});
			}
		}
		catch { }
		finally { try { key?.Dispose(); } catch { } }
	}

	static string portname(string id) {
		if (string.IsNullOrEmpty(id)) return "";
		RegistryKey key = null;
		try {
			key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + id + @"\Device Parameters");
			return ((key?.GetValue("PortName") as string) ?? "").Trim();
		}
		catch { return ""; }
		finally { try { key?.Dispose(); } catch { } }
	}

	static string comin(string name) {
		if (string.IsNullOrEmpty(name)) return "";
		var i = name.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
		if (i < 0) return "";
		var j = name.IndexOf(')', i);
		if (j < 0) return "";
		return name.Substring(i + 1, j - i - 1).Trim();
	}

	static int comnum(string com) {
		if (string.IsNullOrEmpty(com) || com.Length < 4) return 9999;
		if (int.TryParse(com.Substring(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return n;
		return 9999;
	}

	static UsbDev read(IntPtr h, SpDevinfoData data) {
		var props = new List<UsbPair>();
		var id = str(h, data, pk(GInst, 256));
		if (id.Length == 0) id = instanceid(h, data);
		var desc = clean(str(h, data, pk(GDev, 2)));
		var friendly = clean(str(h, data, pk(GDev, 14)));
		var bus = clean(str(h, data, pk(GBus, 4)));
		var name = friendly.Length > 0 ? friendly : desc.Length > 0 ? desc : bus.Length > 0 ? bus : id;
		var cls = clean(str(h, data, pk(GDev, 9)));
		var mfg = clean(str(h, data, pk(GDev, 13)));
		uint node = 0, problem = 0;
		var hasNode = CM_Get_DevNode_Status(out node, out problem, (uint)data.DevInst, 0) == CR_SUCCESS;
		var status = statustext(hasNode ? (int)problem : -1);
		var vid = hexkey(id, "VID_");
		var pid = hexkey(id, "PID_");
		var rev = hexkey(id, "REV_");
		var mi = hexkey(id, "MI_");
		var vidpid = vid >= 0 && pid >= 0 ? $"{vid:X4}:{pid:X4}" : "";
		add(props, "usb.p.name", name);
		add(props, "usb.p.desc", desc);
		add(props, "usb.p.bus", bus);
		add(props, "usb.p.mfg", mfg);
		add(props, "usb.p.status", status);
		if (hasNode && problem != 0) add(props, "usb.p.problem", problem + " " + probname((int)problem));
		if (hasNode) add(props, "usb.p.node", "0x" + node.ToString("X8") + nodeflags(node));
		add(props, "usb.p.cls", cls);
		add(props, "usb.p.guid", guidtext(h, data, pk(GDev, 10)));
		add(props, "usb.p.service", str(h, data, pk(GDev, 6)));
		add(props, "usb.p.instance", id);
		if (vid >= 0) add(props, "usb.p.vid", vid.ToString("X4"));
		if (pid >= 0) add(props, "usb.p.pid", pid.ToString("X4"));
		if (rev >= 0) add(props, "usb.p.rev", rev.ToString("X4"));
		if (mi >= 0) add(props, "usb.p.mi", mi.ToString("X2"));
		add(props, "usb.p.serial", serialof(id));
		var ids = str(h, data, pk(GDev, 3)) + "\n" + str(h, data, pk(GDev, 4));
		var ucls = usbclass(ids);
		if (ucls >= 0) add(props, "usb.p.usbcls", usbclsname(ucls));
		var sub = usbfield(ids, "SubClass_");
		var prot = usbfield(ids, "Prot_");
		if (sub >= 0) add(props, "usb.p.sub", sub.ToString("X2"));
		if (prot >= 0) add(props, "usb.p.prot", prot.ToString("X2"));
		if (ids.IndexOf("COMPOSITE", StringComparison.OrdinalIgnoreCase) >= 0)
			add(props, "usb.p.composite", Loc.T("usb.yes"));
		add(props, "usb.p.hwid", joinlist(str(h, data, pk(GDev, 3))));
		add(props, "usb.p.compat", joinlist(str(h, data, pk(GDev, 4))));
		add(props, "usb.p.location", str(h, data, pk(GDev, 15)));
		add(props, "usb.p.locpath", joinlist(str(h, data, pk(GDev, 37))));
		add(props, "usb.p.parent", str(h, data, pk(GStat, 8)));
		add(props, "usb.p.children", joinlist(str(h, data, pk(GStat, 9))));
		add(props, "usb.p.driver", str(h, data, pk(GDev, 11)));
		add(props, "usb.p.drvver", str(h, data, pk(GDrv, 3)));
		add(props, "usb.p.drvdate", filetime(h, data, pk(GDrv, 2)));
		add(props, "usb.p.drvprov", clean(str(h, data, pk(GDrv, 9))));
		add(props, "usb.p.inf", str(h, data, pk(GDrv, 5)));
		add(props, "usb.p.infsec", str(h, data, pk(GDrv, 6)));
		add(props, "usb.p.drvdesc", clean(str(h, data, pk(GDrv, 4))));
		add(props, "usb.p.enum", str(h, data, pk(GDev, 24)));
		add(props, "usb.p.busnum", uinttext(h, data, pk(GDev, 23)));
		add(props, "usb.p.addr", uinttext(h, data, pk(GDev, 30)));
		add(props, "usb.p.pdo", str(h, data, pk(GDev, 16)));
		add(props, "usb.p.caps", capstext(h, data, pk(GDev, 17)));
		add(props, "usb.p.removal", removal(h, data, pk(GDev, 33)));
		add(props, "usb.p.power", power(h, data, pk(GDev, 32)));
		add(props, "usb.p.container", guidtext(h, data, pk(GBox, 2)));
		add(props, "usb.p.install", filetime(h, data, pk(GDate, 100)));
		addparams(props, id);
		var dev = new UsbDev {
			Name = name,
			VidPid = vidpid,
			ClassName = cls,
			Status = status,
			InstanceId = id,
			Props = props,
			NodeStatus = node,
			ProblemCode = problem,
		};
		var sb = new StringBuilder();
		sb.Append(name).Append('\n').Append(vidpid).Append('\n').Append(cls).Append('\n');
		sb.Append(status).Append('\n').Append(id);
		foreach (var p in props)
			sb.Append('\n').Append(p.Name).Append('\n').Append(p.Value);
		dev.Filter = sb.ToString().ToLowerInvariant();
		return dev;
	}

	static void add(List<UsbPair> props, string key, string value) {
		value = (value ?? "").Trim();
		if (value.Length == 0) return;
		if (value.Length > 2000) value = value.Substring(0, 2000);
		props.Add(new UsbPair { Name = Loc.T(key), Value = value });
	}

	static void addparams(List<UsbPair> props, string id) {
		if (string.IsNullOrEmpty(id)) return;
		RegistryKey key = null;
		try {
			key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + id + @"\Device Parameters");
			if (key == null) return;
			var names = key.GetValueNames();
			Array.Sort(names, StringComparer.OrdinalIgnoreCase);
			foreach (var name in names) {
				if (string.IsNullOrEmpty(name)) continue;
				object raw;
				try { raw = key.GetValue(name); }
				catch { continue; }
				var text = regtext(raw);
				if (text.Length == 0) continue;
				props.Add(new UsbPair { Name = Loc.T("usb.p.param", name), Value = text });
			}
		}
		catch { }
		finally { try { key?.Dispose(); } catch { } }
	}

	static string regtext(object raw) {
		if (raw == null) return "";
		if (raw is string s) return s.Trim();
		if (raw is string[] arr) return string.Join(" | ", arr.Where(x => !string.IsNullOrWhiteSpace(x)));
		if (raw is int n) return n.ToString();
		if (raw is long n64) return n64.ToString();
		if (raw is byte[] b) {
			if (b.Length == 0 || b.Length > 32) return b.Length + " bytes";
			return BitConverter.ToString(b);
		}
		return "";
	}

	static string statustext(int problem) {
		if (problem < 0) return "";
		if (problem == 0) return Loc.T("usb.ok");
		var name = probname(problem);
		if (name.Length == 0) return Loc.T("usb.problem.n", problem);
		return Loc.T("usb.problem", problem, name);
	}

	static string nodeflags(uint node) {
		var parts = new List<string>();
		if ((node & DN_STARTED) != 0) parts.Add("Started");
		if ((node & DN_DRIVER_LOADED) != 0) parts.Add("DriverLoaded");
		if ((node & DN_HAS_PROBLEM) != 0) parts.Add("HasProblem");
		if ((node & DN_REMOVABLE) != 0) parts.Add("Removable");
		if ((node & DN_DISABLEABLE) != 0) parts.Add("Disableable");
		if (parts.Count == 0) return "";
		return " " + string.Join(" ", parts);
	}

	static string serialof(string id) {
		if (string.IsNullOrEmpty(id)) return "";
		var i = id.LastIndexOf('\\');
		var tail = i >= 0 ? id.Substring(i + 1) : id;
		if (tail.Length == 0 || tail.IndexOf('&') >= 0) return "";
		if (tail.StartsWith("VID_", StringComparison.OrdinalIgnoreCase)) return "";
		return tail;
	}

	static int hexkey(string id, string key) {
		if (string.IsNullOrEmpty(id)) return -1;
		var i = id.IndexOf(key, StringComparison.OrdinalIgnoreCase);
		if (i < 0) return -1;
		var n = key == "MI_" ? 2 : 4;
		if (i + key.Length + n > id.Length) return -1;
		var s = id.Substring(i + key.Length, n);
		if (int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return v;
		return -1;
	}

	static int usbclass(string ids) {
		var dev = usbfield(ids, "DevClass_");
		if (dev >= 0) return dev;
		if (string.IsNullOrEmpty(ids)) return -1;
		var i = 0;
		while (i < ids.Length) {
			var at = ids.IndexOf("Class_", i, StringComparison.OrdinalIgnoreCase);
			if (at < 0) return -1;
			if (at > 0 && char.IsLetter(ids[at - 1])) {
				i = at + 6;
				continue;
			}
			if (at + 8 <= ids.Length &&
				int.TryParse(ids.Substring(at + 6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
				return v;
			i = at + 6;
		}
		return -1;
	}

	static int usbfield(string ids, string key) {
		if (string.IsNullOrEmpty(ids)) return -1;
		var i = 0;
		while (i < ids.Length) {
			var at = ids.IndexOf(key, i, StringComparison.OrdinalIgnoreCase);
			if (at < 0) return -1;
			if (at + key.Length + 2 <= ids.Length &&
				int.TryParse(ids.Substring(at + key.Length, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
				return v;
			i = at + key.Length;
		}
		return -1;
	}

	static string usbclsname(int code) {
		string zh, en;
		switch (code) {
		case 0x00: zh = "设备"; en = "Device"; break;
		case 0x01: zh = "音频"; en = "Audio"; break;
		case 0x02: zh = "通信"; en = "Communications"; break;
		case 0x03: zh = "人机接口"; en = "HID"; break;
		case 0x05: zh = "物理"; en = "Physical"; break;
		case 0x06: zh = "图像"; en = "Image"; break;
		case 0x07: zh = "打印机"; en = "Printer"; break;
		case 0x08: zh = "大容量存储"; en = "Mass storage"; break;
		case 0x09: zh = "集线器"; en = "Hub"; break;
		case 0x0A: zh = "CDC 数据"; en = "CDC data"; break;
		case 0x0B: zh = "智能卡"; en = "Smart card"; break;
		case 0x0D: zh = "内容安全"; en = "Content security"; break;
		case 0x0E: zh = "视频"; en = "Video"; break;
		case 0x0F: zh = "个人医疗"; en = "Personal healthcare"; break;
		case 0x10: zh = "音视频"; en = "Audio/Video"; break;
		case 0x11: zh = "公告板"; en = "Billboard"; break;
		case 0xDC: zh = "诊断"; en = "Diagnostic"; break;
		case 0xE0: zh = "无线"; en = "Wireless"; break;
		case 0xEF: zh = "其他"; en = "Miscellaneous"; break;
		case 0xFF: zh = "厂商自定义"; en = "Vendor specific"; break;
		default: zh = ""; en = ""; break;
		}
		var word = Loc.IsZh ? zh : en;
		var hex = code.ToString("X2");
		return word.Length == 0 ? hex : word + " (" + hex + ")";
	}

	static string clean(string s) {
		s = (s ?? "").Trim();
		if (s.Length == 0) return "";
		if (s[0] == '@') {
			var i = s.LastIndexOf(';');
			if (i >= 0 && i + 1 < s.Length) s = s.Substring(i + 1).Trim();
		}
		return s;
	}

	static string joinlist(string s) {
		if (string.IsNullOrEmpty(s)) return "";
		var parts = s.Split(new[] { '\0', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		return string.Join(" | ", parts.Select(x => x.Trim()).Where(x => x.Length > 0));
	}

	static string str(IntPtr h, SpDevinfoData data, DevPropKey key) {
		var raw = getprop(h, data, key, out var typ);
		if (raw == null) return "";
		if (typ != DEVPROP_TYPE_STRING && typ != DEVPROP_TYPE_STRING_LIST) return "";
		var text = Encoding.Unicode.GetString(raw).TrimEnd('\0');
		return text;
	}

	static string guidtext(IntPtr h, SpDevinfoData data, DevPropKey key) {
		var raw = getprop(h, data, key, out var typ);
		if (raw == null || typ != DEVPROP_TYPE_GUID || raw.Length < 16) return "";
		var g = new byte[16];
		Buffer.BlockCopy(raw, 0, g, 0, 16);
		return new Guid(g).ToString("B").ToUpperInvariant();
	}

	static string uinttext(IntPtr h, SpDevinfoData data, DevPropKey key) {
		var n = uintof(h, data, key);
		return n.HasValue ? n.Value.ToString() : "";
	}

	static uint? uintof(IntPtr h, SpDevinfoData data, DevPropKey key) {
		var raw = getprop(h, data, key, out var typ);
		if (raw == null || raw.Length < 4) return null;
		if (typ != DEVPROP_TYPE_UINT32 && typ != DEVPROP_TYPE_BOOLEAN) return null;
		return BitConverter.ToUInt32(raw, 0);
	}

	static string filetime(IntPtr h, SpDevinfoData data, DevPropKey key) {
		var raw = getprop(h, data, key, out var typ);
		if (raw == null || typ != DEVPROP_TYPE_FILETIME || raw.Length < 8) return "";
		var ticks = BitConverter.ToInt64(raw, 0);
		if (ticks <= 0) return "";
		try { return DateTime.FromFileTime(ticks).ToString("yyyy-MM-dd"); }
		catch { return ""; }
	}

	static string capstext(IntPtr h, SpDevinfoData data, DevPropKey key) {
		var n = uintof(h, data, key);
		if (!n.HasValue) return "";
		var v = n.Value;
		var parts = new List<string>();
		if ((v & 0x1) != 0) parts.Add("Lock");
		if ((v & 0x2) != 0) parts.Add("Eject");
		if ((v & 0x4) != 0) parts.Add("Removable");
		if ((v & 0x8) != 0) parts.Add("Dock");
		if ((v & 0x10) != 0) parts.Add("UniqueId");
		if ((v & 0x20) != 0) parts.Add("SilentInstall");
		if ((v & 0x40) != 0) parts.Add("RawOk");
		if ((v & 0x80) != 0) parts.Add("SurpriseOk");
		if ((v & 0x100) != 0) parts.Add("HardwareDisabled");
		if ((v & 0x200) != 0) parts.Add("NonDynamic");
		var hex = "0x" + v.ToString("X");
		return parts.Count == 0 ? hex : hex + " " + string.Join(" ", parts);
	}

	static string removal(IntPtr h, SpDevinfoData data, DevPropKey key) {
		var n = uintof(h, data, key);
		if (!n.HasValue) return "";
		if (n.Value == 1) return Loc.T("usb.removal.1");
		if (n.Value == 2) return Loc.T("usb.removal.2");
		if (n.Value == 3) return Loc.T("usb.removal.3");
		return n.Value.ToString();
	}

	static string power(IntPtr h, SpDevinfoData data, DevPropKey key) {
		var raw = getprop(h, data, key, out _);
		if (raw == null || raw.Length < 8) return "";
		var st = BitConverter.ToInt32(raw, 4);
		if (st == 1) return "D0";
		if (st == 2) return "D1";
		if (st == 3) return "D2";
		if (st == 4) return "D3";
		if (st == 0) return "";
		return st.ToString();
	}

	static string instanceid(IntPtr h, SpDevinfoData data) {
		var sb = new StringBuilder(1024);
		if (!SetupDiGetDeviceInstanceId(h, ref data, sb, sb.Capacity, IntPtr.Zero)) return "";
		return sb.ToString();
	}

	static byte[] getprop(IntPtr h, SpDevinfoData data, DevPropKey key, out uint typ) {
		typ = 0;
		var buf = new byte[4096];
		if (!SetupDiGetDeviceProperty(h, ref data, ref key, out typ, buf, (uint)buf.Length, out var need, 0)) {
			var err = Marshal.GetLastWin32Error();
			if (err != 122 || need <= (uint)buf.Length || need > 65536) return null;
			buf = new byte[need];
			if (!SetupDiGetDeviceProperty(h, ref data, ref key, out typ, buf, need, out need, 0))
				return null;
		}
		if (need == 0) return null;
		if (need > buf.Length) need = (uint)buf.Length;
		if (need == buf.Length) return buf;
		var cut = new byte[need];
		Buffer.BlockCopy(buf, 0, cut, 0, (int)need);
		return cut;
	}

	static DevPropKey pk(Guid g, uint pid) => new DevPropKey { Fmtid = g, Pid = pid };

	static string probname(int code) {
		switch (code) {
		case 1: return "CM_PROB_NOT_CONFIGURED";
		case 2: return "CM_PROB_DEVLOADER_FAILED";
		case 3: return "CM_PROB_OUT_OF_MEMORY";
		case 4: return "CM_PROB_ENTRY_IS_WRONG_TYPE";
		case 5: return "CM_PROB_LACKED_ARBITRATOR";
		case 6: return "CM_PROB_BOOT_CONFIG_CONFLICT";
		case 7: return "CM_PROB_FAILED_FILTER";
		case 8: return "CM_PROB_DEVLOADER_NOT_FOUND";
		case 9: return "CM_PROB_INVALID_DATA";
		case 10: return "CM_PROB_FAILED_START";
		case 11: return "CM_PROB_LIAR";
		case 12: return "CM_PROB_NORMAL_CONFLICT";
		case 13: return "CM_PROB_NOT_VERIFIED";
		case 14: return "CM_PROB_NEED_RESTART";
		case 15: return "CM_PROB_REENUMERATION";
		case 16: return "CM_PROB_PARTIAL_LOG_CONF";
		case 17: return "CM_PROB_UNKNOWN_RESOURCE";
		case 18: return "CM_PROB_REINSTALL";
		case 19: return "CM_PROB_REGISTRY";
		case 21: return "CM_PROB_WILL_BE_REMOVED";
		case 22: return "CM_PROB_DISABLED";
		case 23: return "CM_PROB_DEVLOADER_NOT_READY";
		case 24: return "CM_PROB_DEVICE_NOT_THERE";
		case 25: return "CM_PROB_MOVED";
		case 26: return "CM_PROB_TOO_EARLY";
		case 27: return "CM_PROB_NO_VALID_LOG_CONF";
		case 28: return "CM_PROB_FAILED_INSTALL";
		case 29: return "CM_PROB_HARDWARE_DISABLED";
		case 30: return "CM_PROB_CANT_SHARE_IRQ";
		case 31: return "CM_PROB_FAILED_ADD";
		case 32: return "CM_PROB_DISABLED_SERVICE";
		case 33: return "CM_PROB_TRANSLATION_FAILED";
		case 34: return "CM_PROB_NO_SOFTCONFIG";
		case 35: return "CM_PROB_BIOS_TABLE";
		case 36: return "CM_PROB_IRQ_TRANSLATION_FAILED";
		case 37: return "CM_PROB_FAILED_DRIVER_ENTRY";
		case 38: return "CM_PROB_DRIVER_FAILED_PRIOR_UNLOAD";
		case 39: return "CM_PROB_DRIVER_FAILED_LOAD";
		case 40: return "CM_PROB_DRIVER_SERVICE_KEY_INVALID";
		case 41: return "CM_PROB_LEGACY_SERVICE_NO_DEVICES";
		case 42: return "CM_PROB_DUPLICATE_DEVICE";
		case 43: return "CM_PROB_FAILED_POST_START";
		case 44: return "CM_PROB_HALTED";
		case 45: return "CM_PROB_PHANTOM";
		case 46: return "CM_PROB_SYSTEM_SHUTDOWN";
		case 47: return "CM_PROB_HELD_FOR_EJECT";
		case 48: return "CM_PROB_DRIVER_BLOCKED";
		case 49: return "CM_PROB_REGISTRY_TOO_LARGE";
		case 50: return "CM_PROB_SETPROPERTIES_FAILED";
		case 51: return "CM_PROB_WAITING_ON_DEPENDENCY";
		case 52: return "CM_PROB_UNSIGNED_DRIVER";
		case 53: return "CM_PROB_USED_BY_DEBUGGER";
		case 54: return "CM_PROB_DEVICE_RESET";
		case 55: return "CM_PROB_CONSOLE_LOCKED";
		case 56: return "CM_PROB_NEED_CLASS_CONFIG";
		case 57: return "CM_PROB_GUEST_ASSIGNMENT_FAILED";
		default: return "";
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	struct SpDevinfoData {
		public int CbSize;
		public Guid ClassGuid;
		public int DevInst;
		public IntPtr Reserved;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct DevPropKey {
		public Guid Fmtid;
		public uint Pid;
	}

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetClassDevsW")]
	static extern IntPtr SetupDiGetClassDevs(IntPtr cls, string enumerator, IntPtr parent, int flags);

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetClassDevsW")]
	static extern IntPtr SetupDiGetClassDevsGuid(ref Guid cls, IntPtr enumerator, IntPtr parent, int flags);

	[DllImport("setupapi.dll", SetLastError = true)]
	static extern bool SetupDiEnumDeviceInfo(IntPtr h, int index, ref SpDevinfoData data);

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern bool SetupDiGetDeviceInstanceId(IntPtr h, ref SpDevinfoData data, StringBuilder id, int size, IntPtr need);

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetDevicePropertyW")]
	static extern bool SetupDiGetDeviceProperty(IntPtr h, ref SpDevinfoData data, ref DevPropKey key,
		out uint typ, byte[] buf, uint size, out uint need, uint flags);

	[DllImport("setupapi.dll", SetLastError = true)]
	static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);

	[DllImport("cfgmgr32.dll")]
	static extern int CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);
}
