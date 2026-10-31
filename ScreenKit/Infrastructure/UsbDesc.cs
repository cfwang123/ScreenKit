using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ScreenKit;

/// <summary>一个 USB 设备的只读描述符文本。</summary>
public sealed class UsbDescDev {
	public string Name { get; set; }
	public string VidPid { get; set; }
	public string InstanceId { get; set; }
	public string Node { get; set; }
	public string Text { get; set; }
	public string Filter { get; set; }
}

/// <summary>
/// 经 USB 集线器的只读 IOCTL 取设备描述符、配置描述符、字符串描述符和端点表。
/// 只发 GET_DESCRIPTOR 控制传输，不发 SET_*、不重置端口、不改设备状态。
/// </summary>
static class UsbDesc {
	const int DIGCF_PRESENT = 2;
	const int DIGCF_DEVICEINTERFACE = 0x10;
	const uint GENERIC_READ = 0x80000000;
	const uint FILE_SHARE_READ = 1;
	const uint FILE_SHARE_WRITE = 2;
	const uint OPEN_EXISTING = 3;

	const uint IOCTL_GET_NODE_INFO = 0x00220408;
	const uint IOCTL_GET_NODE_CONN_INFO_EX = 0x00220448;
	const uint IOCTL_GET_NODE_CONN_DRVKEY = 0x00220420;
	const uint IOCTL_GET_DESC = 0x00220410;
	const uint IOCTL_GET_NODE_CONN_INFO_V2 = 0x0022045C;

	const int CONN_CONNECTED = 1;
	const int HDR = 12;

	static readonly Guid GHub = new Guid("f18a0e88-c30c-11d0-8815-00a0c906bed8");

	/// <summary>列出所有集线器端口上的设备，按拓扑顺序给出描述符文本。</summary>
	public static List<UsbDescDev> Scan() {
		var map = new Dictionary<string, UsbDev>(StringComparer.OrdinalIgnoreCase);
		foreach (var dev in UsbList.Scan()) {
			var key = (dev.DriverKey ?? "").Trim();
			if (key.Length > 0 && !map.ContainsKey(key)) map[key] = dev;
		}
		var list = new List<UsbDescDev>();
		var hub = GHub;
		var h = SetupDiGetClassDevsGuid(ref hub, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
		if (h == IntPtr.Zero || h == new IntPtr(-1))
			throw new Win32Exception(Marshal.GetLastWin32Error());
		try {
			for (var i = 0; ; i++) {
				var ifd = new SpDevInterfaceData { CbSize = Marshal.SizeOf(typeof(SpDevInterfaceData)) };
				if (!SetupDiEnumDeviceInterfaces(h, IntPtr.Zero, ref hub, i, ref ifd)) break;
				var path = detail(h, ref ifd);
				if (path.Length == 0) continue;
				scanhub(list, path, map);
			}
		}
		finally { SetupDiDestroyDeviceInfoList(h); }
		list.Sort((a, b) => string.Compare(a.Node, b.Node, StringComparison.CurrentCultureIgnoreCase));
		return list;
	}

	static void scanhub(List<UsbDescDev> list, string hubpath, Dictionary<string, UsbDev> map) {
		var h = CreateFileW(hubpath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
		if (h == new IntPtr(-1)) return;
		try {
			var ni = new byte[512];
			if (!DeviceIoControl(h, IOCTL_GET_NODE_INFO, ni, ni.Length, ni, ni.Length, out var ret, IntPtr.Zero)) return;
			var ports = ni[6];
			for (var port = 1; port <= ports; port++) {
				var row = readport(h, hubpath, port, map);
				if (row != null) list.Add(row);
			}
		}
		finally { CloseHandle(h); }
	}

	static UsbDescDev readport(IntPtr hub, string hubpath, int port, Dictionary<string, UsbDev> map) {
		var buf = new byte[4096];
		BitConverter.GetBytes((uint)port).CopyTo(buf, 0);
		if (!DeviceIoControl(hub, IOCTL_GET_NODE_CONN_INFO_EX, buf, 35, buf, buf.Length, out var ret, IntPtr.Zero))
			return null;
		if (BitConverter.ToInt32(buf, 31) != CONN_CONNECTED) return null;

		var vid = BitConverter.ToUInt16(buf, 12);
		var pid = BitConverter.ToUInt16(buf, 14);
		var speed = buf[23];
		var ishub = buf[24] != 0;
		var addr = BitConverter.ToUInt16(buf, 25);
		var npipes = BitConverter.ToInt32(buf, 27);
		var devkey = drvkey(hub, port);
		map.TryGetValue(devkey, out var dev);

		var lines = new List<string>();
		var node = $"{(ishub ? "Hub" : "Port")} {port} · {hubname(hubpath)}";
		var head = dev != null ? dev.Name : "";
		lines.Add("Node: " + node);
		lines.Add("Device Address: " + addr);
		if (dev != null) {
			lines.Add("Instance: " + (dev.InstanceId ?? ""));
			lines.Add("Status: " + (dev.Status ?? ""));
			if (!string.IsNullOrWhiteSpace(dev.Service)) lines.Add("Service: " + dev.Service);
			if (!string.IsNullOrWhiteSpace(dev.DescName)) lines.Add("Description: " + dev.DescName);
			if (!string.IsNullOrWhiteSpace(dev.Friendly)) lines.Add("NameFriendly: " + dev.Friendly);
			var sym = symbolic(dev.InstanceId);
			if (sym.Length > 0) lines.Add("NameSymbolic: " + sym);
			if (!string.IsNullOrWhiteSpace(dev.ClassName)) lines.Add("Class: " + dev.ClassName);
			if (!string.IsNullOrWhiteSpace(dev.DriverKey)) lines.Add("Driver Key: " + dev.DriverKey);
		}

		lines.Add(string.Format(CultureInfo.InvariantCulture, "Vendor ID: 0x{0:X4} {1}", vid, vid));
		lines.Add(string.Format(CultureInfo.InvariantCulture, "Product ID: 0x{0:X4} {1}", pid, pid));
		lines.Add("Is Hub: " + (ishub ? "True" : "False"));

		var errs = new List<string>();

		// 设备描述符
		var dd = desc(hub, port, 0x80, 0x06, 0x0100, 0, 18);
		if (dd == null) {
			errs.Add("Device Descriptor: " + lasterr());
		}
		else if (dd.Length >= 18) {
			var bcdusb = BitConverter.ToUInt16(dd, 2);
			var bcddev = BitConverter.ToUInt16(dd, 12);
			lines.Add("USB Version: " + ver(bcdusb));
			lines.Add(string.Format(CultureInfo.InvariantCulture, "Device Version: 0x{0:X4} {1}", bcddev, bcddev));
			lines.Add("Max Packet Size: " + dd[7]);
			lines.Add(devclass(dd[4], dd[5], dd[6]));
			lines.Add("Configurations: " + dd[17]);
		}
		else errs.Add("Device Descriptor: 长度不足 " + dd.Length);

		// 配置描述符
		var cfghead = desc(hub, port, 0x80, 0x06, 0x0200, 0, 9);
		var cfg = null as byte[];
		if (cfghead == null) {
			errs.Add("Config Descriptor: " + lasterr());
		}
		else if (cfghead.Length < 4) {
			errs.Add("Config Descriptor: 长度不足 " + cfghead.Length);
		}
		else {
			var total = cfghead[2] | (cfghead[3] << 8);
			cfg = desc(hub, port, 0x80, 0x06, 0x0200, 0, total <= 0 ? 255 : total);
			if (cfg == null) errs.Add("Config Descriptor: " + lasterr());
			else if (cfg.Length >= 9) {
				var attr = cfg[7];
				lines.Add("Max Power: " + cfg[8] * 2 + " mA");
				lines.Add("Is Wake Up: " + ((attr & 0x20) != 0 ? "True" : "False"));
				lines.Add("Is Self Power: " + ((attr & 0x40) != 0 ? "True" : "False"));
				lines.Add("Is Bus Power: " + ((attr & 0x40) != 0 ? "False" : "True"));
				lines.Add("Interfaces: " + cfg[4]);
				lines.Add("Configuration: " + cfg[5] + (cfg[6] > 0 ? "" : " (iConfiguration=0)"));
			}
		}
		lines.Add("Speed (Low, Full, High): " + speedname(speed));

		// 管道：配置描述符里的端点优先，读不到时退回集线器给的管道表
		var eps = cfg == null ? new List<string>() : endpoints(cfg);
		if (eps.Count > 0) {
			lines.Add("Pipe Count: " + eps.Count);
			for (var i = 0; i < eps.Count; i++)
				lines.Add($"PIPE{i} Description: Pipe [{i + 1}] " + eps[i]);
		}
		else if (npipes > 0) {
			lines.Add("Pipe Count: " + npipes);
			for (var i = 0; i < npipes && 35 + (i + 1) * 11 <= buf.Length; i++) {
				var o = 35 + i * 11;
				var ep = buf[o + 2];
				var kind = pipekind(buf[o + 3]);
				var mps = buf[o + 4] | (buf[o + 5] << 8);
				lines.Add($"PIPE{i} Description: Pipe [{i + 1}] {kind} {mps} {(ep == 0 || ep < 0x80 ? "OUT" : "IN")}");
			}
		}

		if (cfg != null) {
			var n = 0;
			foreach (var line in ifacelines(cfg)) lines.Add("Interface [" + n++ + "]: " + line);
		}

		// 字符串描述符
		var langid = langid(hub, port);
		var idx = new[] { 1, 2, 3 };
		foreach (var i in idx) {
			var s = strdesc(hub, port, i, langid);
			if (s == null) {
				if (errs.Count == 0) errs.Add("String Descriptor [" + i + "]: " + lasterr());
				else errs.Add("String Descriptor [" + i + "]: " + lasterr());
			}
			else if (s.Length > 0) lines.Add("String Descriptor [" + i + "]: " + s);
		}

		// 原始字节，便于核对
		if (dd != null && dd.Length > 0) lines.Add("Device Descriptor: " + hex(dd));
		if (cfg != null && cfg.Length > 0) lines.Add("Config Descriptor: " + hex(cfg));
		foreach (var e in errs) lines.Add(e);

		var name = head;
		if (string.IsNullOrWhiteSpace(name)) name = strdesc(hub, port, 2, langid);
		if (string.IsNullOrWhiteSpace(name)) name = $"USB {vid:X4}:{pid:X4}";
		var vidpid = $"{vid:X4}:{pid:X4}";
		var sb = new StringBuilder();
		sb.Append(name).Append('\n').Append(vidpid).Append('\n').Append(node);
		foreach (var line in lines) sb.Append('\n').Append(line);
		return new UsbDescDev {
			Name = name,
			VidPid = vidpid,
			InstanceId = dev?.InstanceId ?? devkey,
			Node = node,
			Text = string.Join("\n", lines),
			Filter = sb.ToString().ToLowerInvariant(),
		};
	}

	static List<string> endpoints(byte[] cfg) {
		var list = new List<string>();
		var o = 0;
		while (o + 2 <= cfg.Length) {
			var len = cfg[o];
			var type = cfg[o + 1];
			if (len < 2 || o + len > cfg.Length) break;
			if (type == 0x05 && len >= 7) {
				var ep = cfg[o + 2];
				var kind = pipekind(cfg[o + 3]);
				var mps = cfg[o + 4] | (cfg[o + 5] << 8);
				var dir = (ep & 0x80) != 0 ? "IN" : "OUT";
				var interval = cfg[o + 6];
				list.Add($"{kind} {mps} {dir} (0x{ep:X2}, interval {interval})");
			}
			o += len;
		}
		return list;
	}

	static List<string> ifacelines(byte[] cfg) {
		var list = new List<string>();
		var o = 0;
		while (o + 2 <= cfg.Length) {
			var len = cfg[o];
			var type = cfg[o + 1];
			if (len < 2 || o + len > cfg.Length) break;
			if (type == 0x04 && len >= 9) {
				var cls = cfg[o + 5];
				var sub = cfg[o + 6];
				var prot = cfg[o + 7];
				list.Add($"number {cfg[o + 2]} alt {cfg[o + 3]} · {UsbList.usbclsname(cls)} · sub 0x{sub:X2} proto 0x{prot:X2} · endpoints {cfg[o + 4]}");
			}
			o += len;
		}
		return list;
	}

	static string devclass(byte cls, byte sub, byte prot) {
		var name = cls == 0 ? "" : " · " + UsbList.usbclsname(cls);
		return string.Format(CultureInfo.InvariantCulture, "Device Class: 0x{0:X2}{1} · SubClass 0x{2:X2} · Protocol 0x{3:X2}", cls, name, sub, prot);
	}

	static string speedname(byte speed) => speed switch {
		0 => "Low",
		1 => "Full",
		2 => "High",
		3 => "Super",
		_ => "Unknown (" + speed + ")",
	};

	static string pipekind(byte attr) => (attr & 0x03) switch {
		0 => "Control",
		1 => "Isochronous",
		2 => "Bulk",
		_ => "Interrupt",
	};

	static string ver(int bcd) =>
		string.Format(CultureInfo.InvariantCulture, "{0:X2}.{1:X2} (0x{2:X4})", bcd >> 8, bcd & 0xFF, bcd);

	static string hubname(string path) {
		var i = path.IndexOf("#root_hub", StringComparison.OrdinalIgnoreCase);
		if (i >= 0) return "ROOT_HUB";
		var a = path.IndexOf('#');
		var b = path.IndexOf('#', a + 1);
		if (a < 0 || b < 0) return path;
		return path.Substring(a + 1, b - a - 1).ToUpperInvariant();
	}

	static string drvkey(IntPtr hub, int port) {
		var buf = new byte[4096];
		BitConverter.GetBytes((uint)port).CopyTo(buf, 0);
		if (!DeviceIoControl(hub, IOCTL_GET_NODE_CONN_DRVKEY, buf, 8, buf, buf.Length, out _, IntPtr.Zero))
			return "";
		var len = BitConverter.ToInt32(buf, 4);
		if (len <= 8) return "";
		return Encoding.Unicode.GetString(buf, 8, Math.Min(len - 8, buf.Length - 8)).TrimEnd('\0');
	}

	static string symbolic(string id) {
		if (string.IsNullOrEmpty(id)) return "";
		try {
			using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + id + @"\Device Parameters");
			return ((key?.GetValue("SymbolicName") as string) ?? "").Trim();
		}
		catch { return ""; }
	}

	static int langid(IntPtr hub, int port) {
		var raw = desc(hub, port, 0x80, 0x06, 0x0300, 0, 4);
		if (raw == null || raw.Length < 4) return 0x0409;
		return raw[2] | (raw[3] << 8);
	}

	static string strdesc(IntPtr hub, int port, int index, int id) {
		var raw = desc(hub, port, 0x80, 0x06, 0x0300 | index, id, 255);
		if (raw == null || raw.Length < 2) return null;
		var sb = new StringBuilder();
		var n = Math.Min(raw[0], raw.Length);
		for (var i = 2; i + 1 < n; i += 2) sb.Append((char)(raw[i] | (raw[i + 1] << 8)));
		var text = sb.ToString().TrimEnd('\0').Trim();
		for (var i = 0; i < text.Length; i++) {
			var c = text[i];
			if (c < 0x20 && c != '\t') return "";
		}
		return text;
	}

	/// <summary>发一次 GET_DESCRIPTOR 控制传输。wValue 低字节是序号，高字节是类型。</summary>
	static byte[] desc(IntPtr hub, int port, byte bmReq, byte req, int wValue, int wIndex, int wLength) {
		var buf = new byte[HDR + Math.Max(wLength, 16)];
		BitConverter.GetBytes((uint)port).CopyTo(buf, 0);
		buf[4] = bmReq;
		buf[5] = req;
		buf[6] = (byte)(wValue & 0xFF);
		buf[7] = (byte)(wValue >> 8);
		buf[8] = (byte)(wIndex & 0xFF);
		buf[9] = (byte)(wIndex >> 8);
		buf[10] = (byte)(wLength & 0xFF);
		buf[11] = (byte)(wLength >> 8);
		if (!DeviceIoControl(hub, IOCTL_GET_DESC, buf, HDR, buf, buf.Length, out var ret, IntPtr.Zero)) {
			seterr();
			return null;
		}
		var n = ret - HDR;
		if (n <= 0) return new byte[0];
		if (n > buf.Length - HDR) n = buf.Length - HDR;
		var cut = new byte[n];
		Buffer.BlockCopy(buf, HDR, cut, 0, n);
		return cut;
	}

	[ThreadStatic] static string errtext;

	static void seterr() {
		var code = Marshal.GetLastWin32Error();
		errtext = $"读取失败 (错误 {code})";
	}

	static string lasterr() => errtext ?? "读取失败";

	static string hex(byte[] raw) {
		var sb = new StringBuilder(raw.Length * 3);
		for (var i = 0; i < raw.Length; i++) {
			if (i > 0) sb.Append(' ');
			sb.Append(raw[i].ToString("X2", CultureInfo.InvariantCulture));
		}
		return sb.ToString();
	}

	static string detail(IntPtr h, ref SpDevInterfaceData ifd) {
		var buf = new byte[2048];
		BitConverter.GetBytes(IntPtr.Size == 8 ? 8 : 6).CopyTo(buf, 0);
		var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
		try {
			if (!SetupDiGetDeviceInterfaceDetail(h, ref ifd, handle.AddrOfPinnedObject(), buf.Length, out _, IntPtr.Zero))
				return "";
		}
		finally { handle.Free(); }
		return Encoding.Unicode.GetString(buf, 4, buf.Length - 4).Split('\0')[0];
	}

	[StructLayout(LayoutKind.Sequential)]
	struct SpDevInterfaceData {
		public int CbSize;
		public Guid InterfaceClassGuid;
		public int Flags;
		public IntPtr Reserved;
	}

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetClassDevsW")]
	static extern IntPtr SetupDiGetClassDevsGuid(ref Guid cls, IntPtr enumerator, IntPtr parent, int flags);

	[DllImport("setupapi.dll", SetLastError = true)]
	static extern bool SetupDiEnumDeviceInterfaces(IntPtr h, IntPtr devinfo, ref Guid cls, int index, ref SpDevInterfaceData data);

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr h, ref SpDevInterfaceData data, IntPtr detail, int size, out int need, IntPtr devinfo);

	[DllImport("setupapi.dll", SetLastError = true)]
	static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool CloseHandle(IntPtr h);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool DeviceIoControl(IntPtr h, uint code, byte[] inBuf, int inSize, byte[] outBuf, int outSize, out int returned, IntPtr overlapped);
}
