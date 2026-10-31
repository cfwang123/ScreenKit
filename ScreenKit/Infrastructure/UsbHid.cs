using System.Runtime.InteropServices;
using System.Text;

namespace ScreenKit;

/// <summary>
/// 读 HID 设备的报文结构。报告描述符本身（0x22）要打开接口句柄才能取，
/// 键盘鼠标类接口会被拒绝访问；改用 HIDP 的能力表，仍能给出 Usage、
/// 报文长度、按钮与字段的位宽和逻辑范围。
/// </summary>
static class UsbHid {
	const int DIGCF_PRESENT = 2;
	const int DIGCF_DEVICEINTERFACE = 0x10;
	const uint FILE_SHARE_READ = 1;
	const uint FILE_SHARE_WRITE = 2;
	const uint OPEN_EXISTING = 3;
	const int HidP_Input = 0;

	// HIDP_BUTTON_CAPS / HIDP_VALUE_CAPS 的实测记录大小与字段偏移（x64，零填充 dump 反推）：
	// UsagePage@0 ReportID@2 IsRange@12 · 按钮 UsageMin@56 UsageMax@58
	// 值项 BitSize@18 ReportCount@20 LogicalMin@40 LogicalMax@44 Usage@56
	const int CAPSREC = 72;
	const int O_BITSIZE = 18;
	const int O_REPORTCOUNT = 20;
	const int O_LGMIN = 40;
	const int O_LGMAX = 44;
	const int O_USAGE = 56;
	const int O_USAGE_MAX = 58;

	static readonly Guid GHid = new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");

	/// <summary>按 USB 设备实例 ID 归集的 HID 段。</summary>
	public static Dictionary<string, List<string>> Collect() {
		var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		var g = GHid;
		var h = SetupDiGetClassDevsGuid(ref g, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
		if (h == IntPtr.Zero || h == new IntPtr(-1)) return map;
		try {
			for (var i = 0; ; i++) {
				var ifd = new SpDevInterfaceData { CbSize = Marshal.SizeOf(typeof(SpDevInterfaceData)) };
				if (!SetupDiEnumDeviceInterfaces(h, IntPtr.Zero, ref g, i, ref ifd)) break;
				var path = detail(h, ref ifd, out var devinst);
				if (devinst == 0) continue;
				var usb = usbparent(devinst);
				if (usb.Length == 0) continue;
				var seg = read(path, usb);
				if (seg.Count == 0) continue;
				if (!map.TryGetValue(usb, out var list)) map[usb] = list = new List<string>();
				list.AddRange(seg);
			}
		}
		finally { SetupDiDestroyDeviceInfoList(h); }
		return map;
	}

	static List<string> read(string path, string usb) {
		var seg = new List<string>();
		var h = CreateFileW(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
		if (h == new IntPtr(-1)) return seg;
		try {
			if (!HidD_GetPreparsedData(h, out var pp)) return seg;
			try {
				var caps = new HidpCaps { Reserved = new ushort[17] };
				if (HidP_GetCaps(pp, ref caps) < 0) return seg;
				seg.Add("HID Interface: " + usb);
				seg.Add($"HID Usage: Page 0x{caps.UsagePage:X4} {page(caps.UsagePage)} · Usage 0x{caps.Usage:X4}");
				seg.Add($"HID Report Bytes: In {caps.InputReportByteLength} · Out {caps.OutputReportByteLength} · Feature {caps.FeatureReportByteLength}");
				seg.Add("HID Collections: " + caps.NumberLinkCollectionNodes);
				buttons(seg, pp, caps.NumberInputButtonCaps);
				values(seg, pp, caps.NumberInputValueCaps);
			}
			finally { HidD_FreePreparsedData(pp); }
		}
		finally { CloseHandle(h); }
		return seg;
	}

	static void buttons(List<string> seg, IntPtr pp, int count) {
		if (count <= 0) return;
		var buf = Marshal.AllocHGlobal(count * CAPSREC);
		try {
			var n = (ushort)count;
			if (HidP_GetButtonCaps(HidP_Input, buf, ref n, pp) < 0) return;
			for (var i = 0; i < n; i++) {
				var at = IntPtr.Add(buf, i * CAPSREC);
				var pageId = (ushort)Marshal.ReadInt16(at, 0);
				var report = (byte)(Marshal.ReadInt16(at, 2) & 0xFF);
				var isrange = Marshal.ReadByte(at, 12) != 0;
				var umin = (ushort)Marshal.ReadInt16(at, O_USAGE);
				var umax = isrange ? (ushort)Marshal.ReadInt16(at, O_USAGE_MAX) : umin;
				var usages = isrange ? $"0x{umin:X4}-0x{umax:X4}" : $"0x{umin:X4}";
				var span = isrange ? umax - umin + 1 : 1;
				seg.Add($"HID Input Button [{i + 1}]: Page 0x{pageId:X4} {page(pageId)} · usages {usages} · {span} button(s) · report {report}");
			}
		}
		finally { Marshal.FreeHGlobal(buf); }
	}

	static void values(List<string> seg, IntPtr pp, int count) {
		if (count <= 0) return;
		var buf = Marshal.AllocHGlobal(count * CAPSREC);
		try {
			var n = (ushort)count;
			if (HidP_GetValueCaps(HidP_Input, buf, ref n, pp) < 0) return;
			for (var i = 0; i < n; i++) {
				var at = IntPtr.Add(buf, i * CAPSREC);
				var pageId = (ushort)Marshal.ReadInt16(at, 0);
				var report = (byte)(Marshal.ReadInt16(at, 2) & 0xFF);
				var isrange = Marshal.ReadByte(at, 12) != 0;
				var bitsize = (ushort)Marshal.ReadInt16(at, O_BITSIZE);
				var reportcount = (ushort)Marshal.ReadInt16(at, O_REPORTCOUNT);
				var lmin = Marshal.ReadInt32(at, O_LGMIN);
				var lmax = Marshal.ReadInt32(at, O_LGMAX);
				var umin = (ushort)Marshal.ReadInt16(at, O_USAGE);
				var umax = isrange ? (ushort)Marshal.ReadInt16(at, O_USAGE_MAX) : umin;
				var usage = isrange ? $"0x{umin:X4}-0x{umax:X4}" : $"0x{umin:X4}";
				seg.Add($"HID Input Value [{i + 1}]: Page 0x{pageId:X4} {page(pageId)} · usage {usage} · {reportcount}×{bitsize} bit · logic {lmin}..{lmax} · report {report}");
			}
		}
		finally { Marshal.FreeHGlobal(buf); }
	}

	/// <summary>从 HID 设备往上找顶层 USB 实例，如 USB\VID_xxxx&amp;PID_xxxx\序列号。</summary>
	static string usbparent(int devinst) {
		var cur = (uint)devinst;
		for (var i = 0; i < 6; i++) {
			var sb = new StringBuilder(512);
			if (CM_Get_Device_IDW(cur, sb, sb.Capacity, 0) != 0) return "";
			var id = sb.ToString();
			if (id.Length == 0) return "";
			if (id.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)
				&& id.IndexOf("&MI_", StringComparison.OrdinalIgnoreCase) < 0) return id;
			if (CM_Get_Parent(out var parent, cur, 0) != 0) return "";
			cur = parent;
		}
		return "";
	}

	static string page(int code) => code switch {
		0x01 => "Generic Desktop",
		0x02 => "Simulation",
		0x05 => "Game",
		0x06 => "Generic Device",
		0x07 => "Keyboard",
		0x08 => "LED",
		0x09 => "Button",
		0x0B => "Telephony",
		0x0C => "Consumer",
		0x0D => "Digitizer",
		0x0F => "PID",
		0x10 => "Unicode",
		>= 0xFF00 => "Vendor",
		_ => "",
	};

	static string detail(IntPtr h, ref SpDevInterfaceData ifd, out int devinst) {
		devinst = 0;
		var buf = new byte[2048];
		BitConverter.GetBytes(IntPtr.Size == 8 ? 8 : 6).CopyTo(buf, 0);
		var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
		try {
			var did = new SpDevInfoData { CbSize = Marshal.SizeOf(typeof(SpDevInfoData)) };
			if (!SetupDiGetDeviceInterfaceDetailD(h, ref ifd, handle.AddrOfPinnedObject(), buf.Length, out _, ref did))
				return "";
			devinst = did.DevInst;
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

	[StructLayout(LayoutKind.Sequential)]
	struct SpDevInfoData {
		public int CbSize;
		public Guid ClassGuid;
		public int DevInst;
		public IntPtr Reserved;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct HidpCaps {
		public ushort Usage;
		public ushort UsagePage;
		public ushort InputReportByteLength;
		public ushort OutputReportByteLength;
		public ushort FeatureReportByteLength;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
		public ushort NumberLinkCollectionNodes;
		public ushort NumberInputButtonCaps;
		public ushort NumberInputValueCaps;
		public ushort NumberInputDataIndices;
		public ushort NumberOutputButtonCaps;
		public ushort NumberOutputValueCaps;
		public ushort NumberOutputDataIndices;
		public ushort NumberFeatureButtonCaps;
		public ushort NumberFeatureValueCaps;
		public ushort NumberFeatureDataIndices;
	}

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetClassDevsW")]
	static extern IntPtr SetupDiGetClassDevsGuid(ref Guid cls, IntPtr enumerator, IntPtr parent, int flags);

	[DllImport("setupapi.dll", SetLastError = true)]
	static extern bool SetupDiEnumDeviceInterfaces(IntPtr h, IntPtr devinfo, ref Guid cls, int index, ref SpDevInterfaceData data);

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
	static extern bool SetupDiGetDeviceInterfaceDetailD(IntPtr h, ref SpDevInterfaceData data, IntPtr detail, int size, out int need, ref SpDevInfoData devinfo);

	[DllImport("setupapi.dll", SetLastError = true)]
	static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);

	[DllImport("cfgmgr32.dll")]
	static extern int CM_Get_Parent(out uint parent, uint devinst, uint flags);

	[DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
	static extern int CM_Get_Device_IDW(uint devinst, StringBuilder id, int len, uint flags);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool CloseHandle(IntPtr h);

	[DllImport("hid.dll", SetLastError = true)]
	static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr data);

	[DllImport("hid.dll", SetLastError = true)]
	static extern bool HidD_FreePreparsedData(IntPtr data);

	[DllImport("hid.dll", SetLastError = true)]
	static extern int HidP_GetCaps(IntPtr data, ref HidpCaps caps);

	[DllImport("hid.dll")]
	static extern int HidP_GetButtonCaps(int reportType, IntPtr caps, ref ushort count, IntPtr preparsed);

	[DllImport("hid.dll")]
	static extern int HidP_GetValueCaps(int reportType, IntPtr caps, ref ushort count, IntPtr preparsed);
}




