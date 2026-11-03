using System.Net;
using System.Runtime.InteropServices;

namespace ScreenKit;

/// <summary>Windows IP Helper / TCP ESTATS 原生封装。</summary>
static class IpHelper {
	public const int AF_INET = 2;
	public const int AF_INET6 = 23;
	public const int TCP_TABLE_OWNER_PID_ALL = 5;
	public const int UDP_TABLE_OWNER_PID = 1;
	public const int ERROR_INSUFFICIENT_BUFFER = 122;
	public const int NO_ERROR = 0;

	public enum TcpEstatsType {
		TcpConnectionEstatsData = 1
	}

	// MIB_TCPROW_OWNER_PID：6 个 DWORD，共 24 字节，无尾部填充
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct MibTcpRowOwnerPid {
		public uint State;
		public uint LocalAddr;
		public uint LocalPort;
		public uint RemoteAddr;
		public uint RemotePort;
		public uint OwningPid;
	}

	public struct MibTcp6RowOwnerPid {
		public byte[] LocalAddr;
		public uint LocalScopeId;
		public uint LocalPort;
		public byte[] RemoteAddr;
		public uint RemoteScopeId;
		public uint RemotePort;
		public uint State;
		public uint OwningPid;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct MibUdpRowOwnerPid {
		public uint LocalAddr;
		public uint LocalPort;
		public uint OwningPid;
	}

	public struct MibUdp6RowOwnerPid {
		public byte[] LocalAddr;
		public uint LocalScopeId;
		public uint LocalPort;
		public uint OwningPid;
	}

	// ESTATS 匹配用 MIB_TCPROW（无 PID）
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct MibTcpRow {
		public uint State;
		public uint LocalAddr;
		public uint LocalPort;
		public uint RemoteAddr;
		public uint RemotePort;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct MibTcp6Row {
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
		public byte[] LocalAddr;
		public uint LocalScopeId;
		public uint LocalPort;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
		public byte[] RemoteAddr;
		public uint RemoteScopeId;
		public uint RemotePort;
		public uint State;
	}

	// TCP_ESTATS_DATA_ROD_v0
	// 注意：Win10/11 上 GetPerTcpConnectionEStats 要求 RodSize=96（ERROR_INVALID_USER_BUFFER=1784 若过小）
	// 前部字段布局与 SDK 一致，尾部保留填充。
	[StructLayout(LayoutKind.Sequential, Pack = 8)]
	public struct TcpEstatsDataRod {
		public ulong DataBytesOut;
		public ulong DataBytesIn;
		public ulong DataSegsOut;
		public ulong DataSegsIn;
		public ulong SoftErrors;
		public ulong SoftErrorReason;
		public uint SndUna;
		public uint SndNxt;
		public uint SndMax;
		uint pad0;
		public ulong ThruBytesAcked;
		public uint RcvNxt;
		uint pad1;
		public ulong ThruBytesReceived;
		ulong pad2; // 凑满 96 字节
	}

	public const int TCP_ESTATS_DATA_ROD_SIZE = 96;

	[DllImport("iphlpapi.dll", SetLastError = true)]
	static extern uint GetExtendedTcpTable(
		IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf,
		int tableClass, uint reserved);

	[DllImport("iphlpapi.dll", SetLastError = true)]
	static extern uint GetExtendedUdpTable(
		IntPtr pUdpTable, ref int pdwSize, bool bOrder, int ulAf,
		int tableClass, uint reserved);

	[DllImport("iphlpapi.dll", SetLastError = true)]
	static extern uint SetPerTcpConnectionEStats(
		ref MibTcpRow row, TcpEstatsType estatsType,
		IntPtr rw, uint rwVersion, uint rwSize, uint offset);

	[DllImport("iphlpapi.dll", SetLastError = true)]
	static extern uint GetPerTcpConnectionEStats(
		ref MibTcpRow row, TcpEstatsType estatsType,
		IntPtr rw, uint rwVersion, uint rwSize,
		IntPtr ros, uint rosVersion, uint rosSize,
		IntPtr rod, uint rodVersion, uint rodSize);

	[DllImport("iphlpapi.dll", SetLastError = true)]
	static extern uint SetPerTcp6ConnectionEStats(
		ref MibTcp6Row row, TcpEstatsType estatsType,
		IntPtr rw, uint rwVersion, uint rwSize, uint offset);

	[DllImport("iphlpapi.dll", SetLastError = true)]
	static extern uint GetPerTcp6ConnectionEStats(
		ref MibTcp6Row row, TcpEstatsType estatsType,
		IntPtr rw, uint rwVersion, uint rwSize,
		IntPtr ros, uint rosVersion, uint rosSize,
		IntPtr rod, uint rodVersion, uint rodSize);

	public static ushort Ntohs(uint port) {
		var p = (ushort)(port & 0xFFFF);
		return (ushort)((p >> 8) | (p << 8));
	}

	public static string FormatIpv4(uint addr) {
		var b = BitConverter.GetBytes(addr);
		return $"{b[0]}.{b[1]}.{b[2]}.{b[3]}";
	}

	public static string FormatIpv6(byte[] addr) {
		if (addr == null || addr.Length != 16) return "::";
		return new IPAddress(addr).ToString();
	}

	/// <summary>
	/// 读取 IPv4 TCP 表。注意：MIB_TCPTABLE_OWNER_PID 在 x64 上 table 从偏移 4 开始（无 8 字节对齐填充）。
	/// </summary>
	public static List<MibTcpRowOwnerPid> ReadTcp4() {
		List<MibTcpRowOwnerPid> list = new();
		if (!getbuf(true, AF_INET, out var buf, out var num)) return list;
		try {
			// dwNumEntries 后紧接行数据，每行 24 字节
			var p = IntPtr.Add(buf, 4);
			for (var i = 0; i < num; i++) {
				list.Add(new MibTcpRowOwnerPid {
					State = (uint)Marshal.ReadInt32(p, 0),
					LocalAddr = (uint)Marshal.ReadInt32(p, 4),
					LocalPort = (uint)Marshal.ReadInt32(p, 8),
					RemoteAddr = (uint)Marshal.ReadInt32(p, 12),
					RemotePort = (uint)Marshal.ReadInt32(p, 16),
					OwningPid = (uint)Marshal.ReadInt32(p, 20)
				});
				p = IntPtr.Add(p, 24);
			}
		} finally {
			Marshal.FreeHGlobal(buf);
		}
		return list;
	}

	public static List<MibTcp6RowOwnerPid> ReadTcp6() {
		List<MibTcp6RowOwnerPid> list = new();
		if (!getbuf(true, AF_INET6, out var buf, out var num)) return list;
		try {
			// MIB_TCP6ROW_OWNER_PID = 56 字节，table 从偏移 4 开始
			var p = IntPtr.Add(buf, 4);
			for (var i = 0; i < num; i++) {
				MibTcp6RowOwnerPid row = new() {
					LocalAddr = new byte[16],
					RemoteAddr = new byte[16]
				};
				Marshal.Copy(p, row.LocalAddr, 0, 16);
				row.LocalScopeId = (uint)Marshal.ReadInt32(p, 16);
				row.LocalPort = (uint)Marshal.ReadInt32(p, 20);
				Marshal.Copy(IntPtr.Add(p, 24), row.RemoteAddr, 0, 16);
				row.RemoteScopeId = (uint)Marshal.ReadInt32(p, 40);
				row.RemotePort = (uint)Marshal.ReadInt32(p, 44);
				row.State = (uint)Marshal.ReadInt32(p, 48);
				row.OwningPid = (uint)Marshal.ReadInt32(p, 52);
				list.Add(row);
				p = IntPtr.Add(p, 56);
			}
		} finally {
			Marshal.FreeHGlobal(buf);
		}
		return list;
	}

	public static List<MibUdpRowOwnerPid> ReadUdp4() {
		List<MibUdpRowOwnerPid> list = new();
		if (!getbuf(false, AF_INET, out var buf, out var num)) return list;
		try {
			var p = IntPtr.Add(buf, 4);
			for (var i = 0; i < num; i++) {
				list.Add(new MibUdpRowOwnerPid {
					LocalAddr = (uint)Marshal.ReadInt32(p, 0),
					LocalPort = (uint)Marshal.ReadInt32(p, 4),
					OwningPid = (uint)Marshal.ReadInt32(p, 8)
				});
				p = IntPtr.Add(p, 12);
			}
		} finally {
			Marshal.FreeHGlobal(buf);
		}
		return list;
	}

	public static List<MibUdp6RowOwnerPid> ReadUdp6() {
		List<MibUdp6RowOwnerPid> list = new();
		if (!getbuf(false, AF_INET6, out var buf, out var num)) return list;
		try {
			// MIB_UDP6ROW_OWNER_PID = 28 字节
			var p = IntPtr.Add(buf, 4);
			for (var i = 0; i < num; i++) {
				MibUdp6RowOwnerPid row = new() { LocalAddr = new byte[16] };
				Marshal.Copy(p, row.LocalAddr, 0, 16);
				row.LocalScopeId = (uint)Marshal.ReadInt32(p, 16);
				row.LocalPort = (uint)Marshal.ReadInt32(p, 20);
				row.OwningPid = (uint)Marshal.ReadInt32(p, 24);
				list.Add(row);
				p = IntPtr.Add(p, 28);
			}
		} finally {
			Marshal.FreeHGlobal(buf);
		}
		return list;
	}

	static bool getbuf(bool tcp, int af, out IntPtr buf, out int num) {
		buf = IntPtr.Zero;
		num = 0;
		var size = 0;
		var tableClass = tcp ? TCP_TABLE_OWNER_PID_ALL : UDP_TABLE_OWNER_PID;
		var ret = tcp
			? GetExtendedTcpTable(IntPtr.Zero, ref size, true, af, tableClass, 0)
			: GetExtendedUdpTable(IntPtr.Zero, ref size, true, af, tableClass, 0);
		if (ret != ERROR_INSUFFICIENT_BUFFER && ret != NO_ERROR) return false;

		buf = Marshal.AllocHGlobal(size);
		ret = tcp
			? GetExtendedTcpTable(buf, ref size, true, af, tableClass, 0)
			: GetExtendedUdpTable(buf, ref size, true, af, tableClass, 0);
		if (ret != NO_ERROR) {
			Marshal.FreeHGlobal(buf);
			buf = IntPtr.Zero;
			return false;
		}
		num = Marshal.ReadInt32(buf);
		return true;
	}

	static MibTcpRow toRow4(MibTcpRowOwnerPid row) => new() {
		State = row.State,
		LocalAddr = row.LocalAddr,
		LocalPort = row.LocalPort,
		RemoteAddr = row.RemoteAddr,
		RemotePort = row.RemotePort
	};

	static MibTcp6Row toRow6(MibTcp6RowOwnerPid row) => new() {
		LocalAddr = row.LocalAddr,
		LocalScopeId = row.LocalScopeId,
		LocalPort = row.LocalPort,
		RemoteAddr = row.RemoteAddr,
		RemoteScopeId = row.RemoteScopeId,
		RemotePort = row.RemotePort,
		State = row.State
	};

	public static bool TryEnableEstats4(MibTcpRowOwnerPid row) {
		var r = toRow4(row);
		var rw = Marshal.AllocHGlobal(1);
		try {
			Marshal.WriteByte(rw, 1); // EnableCollection = TRUE
			return SetPerTcpConnectionEStats(ref r, TcpEstatsType.TcpConnectionEstatsData, rw, 0, 1, 0) == NO_ERROR;
		} finally {
			Marshal.FreeHGlobal(rw);
		}
	}

	public static bool TryEnableEstats6(MibTcp6RowOwnerPid row) {
		var r = toRow6(row);
		var rw = Marshal.AllocHGlobal(1);
		try {
			Marshal.WriteByte(rw, 1);
			return SetPerTcp6ConnectionEStats(ref r, TcpEstatsType.TcpConnectionEstatsData, rw, 0, 1, 0) == NO_ERROR;
		} finally {
			Marshal.FreeHGlobal(rw);
		}
	}

	public static bool TryGetEstats4(MibTcpRowOwnerPid row, out TcpEstatsDataRod rod) {
		rod = default;
		var r = toRow4(row);
		return getRod(ref r, out rod);
	}

	public static bool TryGetEstats6(MibTcp6RowOwnerPid row, out TcpEstatsDataRod rod) {
		rod = default;
		var r = toRow6(row);
		return getRod6(ref r, out rod);
	}

	static bool getRod(ref MibTcpRow r, out TcpEstatsDataRod rod) {
		rod = default;
		var rodPtr = Marshal.AllocHGlobal(TCP_ESTATS_DATA_ROD_SIZE);
		try {
			for (var i = 0; i < TCP_ESTATS_DATA_ROD_SIZE; i++) Marshal.WriteByte(rodPtr, i, 0);
			var ret = GetPerTcpConnectionEStats(
				ref r, TcpEstatsType.TcpConnectionEstatsData,
				IntPtr.Zero, 0, 0,
				IntPtr.Zero, 0, 0,
				rodPtr, 0, TCP_ESTATS_DATA_ROD_SIZE);
			if (ret != NO_ERROR) return false;
			// 只解析前部已知字段
			rod.DataBytesOut = (ulong)Marshal.ReadInt64(rodPtr, 0);
			rod.DataBytesIn = (ulong)Marshal.ReadInt64(rodPtr, 8);
			rod.DataSegsOut = (ulong)Marshal.ReadInt64(rodPtr, 16);
			rod.DataSegsIn = (ulong)Marshal.ReadInt64(rodPtr, 24);
			return true;
		} finally {
			Marshal.FreeHGlobal(rodPtr);
		}
	}

	static bool getRod6(ref MibTcp6Row r, out TcpEstatsDataRod rod) {
		rod = default;
		var rodPtr = Marshal.AllocHGlobal(TCP_ESTATS_DATA_ROD_SIZE);
		try {
			for (var i = 0; i < TCP_ESTATS_DATA_ROD_SIZE; i++) Marshal.WriteByte(rodPtr, i, 0);
			var ret = GetPerTcp6ConnectionEStats(
				ref r, TcpEstatsType.TcpConnectionEstatsData,
				IntPtr.Zero, 0, 0,
				IntPtr.Zero, 0, 0,
				rodPtr, 0, TCP_ESTATS_DATA_ROD_SIZE);
			if (ret != NO_ERROR) return false;
			rod.DataBytesOut = (ulong)Marshal.ReadInt64(rodPtr, 0);
			rod.DataBytesIn = (ulong)Marshal.ReadInt64(rodPtr, 8);
			rod.DataSegsOut = (ulong)Marshal.ReadInt64(rodPtr, 16);
			rod.DataSegsIn = (ulong)Marshal.ReadInt64(rodPtr, 24);
			return true;
		} finally {
			Marshal.FreeHGlobal(rodPtr);
		}
	}
}
