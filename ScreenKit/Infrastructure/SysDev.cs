using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using NAudio.CoreAudioApi;

namespace ScreenKit;

/// <summary>只读列出打印机和播放/录制设备，不打开设备。</summary>
static class SysDev {
	const uint PRINTER_ENUM_LOCAL = 2;
	const uint PRINTER_ENUM_CONNECTIONS = 4;

	public static List<UsbDev> ScanPrinters() {
		var def = defprinter();
		var raw = enumprinters();
		var list = new List<UsbDev>();
		foreach (var p in raw) {
			var name = (p.pPrinterName ?? "").Trim();
			if (name.Length == 0) continue;
			var port = (p.pPortName ?? "").Trim();
			var driver = (p.pDriverName ?? "").Trim();
			var status = prnstatus(p.Status, p.Attributes);
			var deflt = def.Length > 0 && string.Equals(def, name, StringComparison.OrdinalIgnoreCase);
			var props = new List<UsbPair>();
			add(props, "usb.p.name", name);
			add(props, "prn.p.port", port);
			add(props, "prn.p.driver", driver);
			add(props, "usb.p.status", status);
			if (deflt) add(props, "prn.p.default", Loc.T("usb.yes"));
			add(props, "prn.p.jobs", p.cJobs.ToString());
			add(props, "prn.p.share", p.pShareName);
			add(props, "prn.p.where", p.pLocation);
			add(props, "prn.p.comment", p.pComment);
			add(props, "prn.p.server", p.pServerName);
			add(props, "prn.p.proc", p.pPrintProcessor);
			add(props, "prn.p.type", p.pDatatype);
			if (p.AveragePPM > 0) add(props, "prn.p.ppm", p.AveragePPM.ToString());
			add(props, "prn.p.attr", prnattr(p.Attributes));
			var filter = (name + "\n" + port + "\n" + driver + "\n" + status + "\n" + p.pShareName + "\n" + p.pLocation).ToLowerInvariant();
			list.Add(new UsbDev {
				Name = name,
				Com = port,
				ClassName = driver,
				Status = status,
				InstanceId = name,
				Props = props,
				Filter = filter,
			});
		}
		list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
		return list;
	}

	public static List<UsbDev> ScanAudio() {
		var list = new List<UsbDev>();
		using var en = new MMDeviceEnumerator();
		var roles = defaults(en);
		collect(en, list, roles, DataFlow.Render, Loc.T("aud.kind.out"));
		collect(en, list, roles, DataFlow.Capture, Loc.T("aud.kind.in"));
		list.Sort((a, b) => {
			var c = string.Compare(a.ClassName, b.ClassName, StringComparison.CurrentCulture);
			if (c != 0) {
				if (a.ClassName == Loc.T("aud.kind.out")) return -1;
				if (b.ClassName == Loc.T("aud.kind.out")) return 1;
			}
			var da = string.IsNullOrEmpty(a.Com) ? 1 : 0;
			var db = string.IsNullOrEmpty(b.Com) ? 1 : 0;
			if (da != db) return da.CompareTo(db);
			return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
		});
		return list;
	}

	static void collect(MMDeviceEnumerator en, List<UsbDev> list, Dictionary<string, string> roles, DataFlow flow, string kind) {
		MMDeviceCollection col;
		try { col = en.EnumerateAudioEndPoints(flow, DeviceState.All); }
		catch { return; }
		foreach (var d in col) {
			using (d) {
				string id = "";
				string name = "";
				string dev = "";
				var state = DeviceState.Active;
				try { id = d.ID ?? ""; } catch { }
				try { name = d.FriendlyName ?? ""; } catch { }
				try { dev = d.DeviceFriendlyName ?? ""; } catch { }
				try { state = d.State; } catch { }
				name = name.Trim();
				dev = dev.Trim();
				if (name.Length == 0) name = dev;
				if (name.Length == 0 && id.Length == 0) continue;
				var st = audstate(state);
				var role = "";
				if (id.Length > 0 && roles.TryGetValue(id, out var hit)) role = hit;
				var props = new List<UsbPair>();
				add(props, "usb.p.name", name);
				if (dev.Length > 0 && dev != name) add(props, "aud.p.device", dev);
				add(props, "aud.p.kind", kind);
				add(props, "usb.p.status", st);
				add(props, "aud.p.roles", role);
				add(props, "aud.p.id", id);
				var filter = (name + "\n" + dev + "\n" + kind + "\n" + st + "\n" + role + "\n" + id).ToLowerInvariant();
				list.Add(new UsbDev {
					Name = name,
					Com = role,
					ClassName = kind,
					Status = st,
					InstanceId = id.Length > 0 ? id : name,
					Props = props,
					Filter = filter,
				});
			}
		}
	}

	static Dictionary<string, string> defaults(MMDeviceEnumerator en) {
		var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		mark(en, map, DataFlow.Render, Role.Console, Loc.T("aud.role.console"));
		mark(en, map, DataFlow.Render, Role.Multimedia, Loc.T("aud.role.media"));
		mark(en, map, DataFlow.Render, Role.Communications, Loc.T("aud.role.comm"));
		mark(en, map, DataFlow.Capture, Role.Console, Loc.T("aud.role.console"));
		mark(en, map, DataFlow.Capture, Role.Multimedia, Loc.T("aud.role.media"));
		mark(en, map, DataFlow.Capture, Role.Communications, Loc.T("aud.role.comm"));
		var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var kv in map) roles[kv.Key] = string.Join(", ", kv.Value);
		return roles;
	}

	static void mark(MMDeviceEnumerator en, Dictionary<string, List<string>> map, DataFlow flow, Role role, string label) {
		try {
			using var d = en.GetDefaultAudioEndpoint(flow, role);
			var id = d?.ID ?? "";
			if (id.Length == 0) return;
			if (!map.TryGetValue(id, out var list)) map[id] = list = new List<string>();
			if (!list.Contains(label)) list.Add(label);
		}
		catch { }
	}

	static string audstate(DeviceState s) {
		if (s == DeviceState.Active) return Loc.T("aud.state.active");
		if (s == DeviceState.Disabled) return Loc.T("aud.state.disabled");
		if (s == DeviceState.Unplugged) return Loc.T("aud.state.unplugged");
		if (s == DeviceState.NotPresent) return Loc.T("aud.state.absent");
		var parts = new List<string>();
		if ((s & DeviceState.Active) != 0) parts.Add(Loc.T("aud.state.active"));
		if ((s & DeviceState.Disabled) != 0) parts.Add(Loc.T("aud.state.disabled"));
		if ((s & DeviceState.Unplugged) != 0) parts.Add(Loc.T("aud.state.unplugged"));
		if ((s & DeviceState.NotPresent) != 0) parts.Add(Loc.T("aud.state.absent"));
		return parts.Count == 0 ? s.ToString() : string.Join(", ", parts);
	}

	static List<PrinterInfo2> enumprinters() {
		var flags = PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS;
		EnumPrinters(flags, null, 2, IntPtr.Zero, 0, out var need, out _);
		if (need == 0) return new List<PrinterInfo2>();
		var buf = Marshal.AllocHGlobal((int)need);
		try {
			if (!EnumPrinters(flags, null, 2, buf, need, out _, out var n))
				throw new Win32Exception(Marshal.GetLastWin32Error());
			var list = new List<PrinterInfo2>();
			var size = Marshal.SizeOf(typeof(PrinterInfo2));
			for (var i = 0; i < (int)n; i++) {
				var ptr = IntPtr.Add(buf, i * size);
				list.Add((PrinterInfo2)Marshal.PtrToStructure(ptr, typeof(PrinterInfo2)));
			}
			return list;
		}
		finally { Marshal.FreeHGlobal(buf); }
	}

	static string defprinter() {
		var n = 0;
		GetDefaultPrinter(null, ref n);
		if (n <= 1) return "";
		var sb = new StringBuilder(n);
		if (!GetDefaultPrinter(sb, ref n)) return "";
		return sb.ToString().Trim();
	}

	static string prnstatus(uint status, uint attr) {
		var parts = new List<string>();
		if ((status & 0x1) != 0) parts.Add(Loc.T("prn.st.paused"));
		if ((status & 0x2) != 0) parts.Add(Loc.T("prn.st.error"));
		if ((status & 0x8) != 0) parts.Add(Loc.T("prn.st.jam"));
		if ((status & 0x10) != 0 || (status & 0x40) != 0) parts.Add(Loc.T("prn.st.paper"));
		if ((status & 0x80) != 0 || (attr & 0x400) != 0) parts.Add(Loc.T("prn.st.offline"));
		if ((status & 0x200) != 0) parts.Add(Loc.T("prn.st.busy"));
		if ((status & 0x400) != 0) parts.Add(Loc.T("prn.st.printing"));
		if ((status & 0x400000) != 0) parts.Add(Loc.T("prn.st.door"));
		if ((status & 0x20000) != 0) parts.Add(Loc.T("prn.st.toner"));
		if (parts.Count == 0 && status != 0) parts.Add("0x" + status.ToString("X"));
		if (parts.Count == 0) return Loc.T("prn.st.ready");
		return string.Join(", ", parts);
	}

	static string prnattr(uint attr) {
		var parts = new List<string>();
		if ((attr & 0x40) != 0) parts.Add(Loc.T("prn.attr.local"));
		if ((attr & 0x10) != 0) parts.Add(Loc.T("prn.attr.network"));
		if ((attr & 0x8) != 0) parts.Add(Loc.T("prn.attr.shared"));
		if ((attr & 0x400) != 0) parts.Add(Loc.T("prn.attr.offline"));
		if ((attr & 0x4000) != 0) parts.Add(Loc.T("prn.attr.fax"));
		return string.Join(", ", parts);
	}

	static void add(List<UsbPair> props, string key, string value) {
		value = (value ?? "").Trim();
		if (value.Length == 0) return;
		if (value.Length > 2000) value = value.Substring(0, 2000);
		props.Add(new UsbPair { Name = Loc.T(key), Value = value });
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	struct PrinterInfo2 {
		public string pServerName;
		public string pPrinterName;
		public string pShareName;
		public string pPortName;
		public string pDriverName;
		public string pComment;
		public string pLocation;
		public IntPtr pDevMode;
		public string pSepFile;
		public string pPrintProcessor;
		public string pDatatype;
		public string pParameters;
		public IntPtr pSecurityDescriptor;
		public uint Attributes;
		public uint Priority;
		public uint DefaultPriority;
		public uint StartTime;
		public uint UntilTime;
		public uint Status;
		public uint cJobs;
		public uint AveragePPM;
	}

	[DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern bool EnumPrinters(uint flags, string name, uint level, IntPtr buf, uint cb, out uint needed, out uint returned);

	[DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern bool GetDefaultPrinter(StringBuilder buf, ref int cch);
}
