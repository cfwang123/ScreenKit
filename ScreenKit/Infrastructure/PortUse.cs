using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenKit;

/// <summary>列出正在使用的 TCP / UDP 端口，以及占用它的进程。走系统 iphlpapi，不另引用库。</summary>
static class PortUse {
	const uint AF_INET = 2;
	const uint AF_INET6 = 23;
	const int TCP_OWNER_PID_ALL = 5;
	const int UDP_OWNER_PID = 1;
	const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

	public sealed class Row {
		public string Proto = "";
		public string Local = "";
		public int LocalPort;
		public string Remote = "";
		public int RemotePort;
		public string State = "";
		public int Pid;
		public string Proc = "";
		public string Path = "";
	}

	public static List<Row> List() {
		var rows = new List<Row>();
		var proc = new Dictionary<int, (string name, string path)>();
		readtcp(rows, proc, AF_INET, false);
		readtcp(rows, proc, AF_INET6, true);
		readudp(rows, proc, AF_INET, false);
		readudp(rows, proc, AF_INET6, true);
		rows.Sort((a, b) => {
			int c = rank(a).CompareTo(rank(b));
			if (c != 0) return c;
			c = a.LocalPort.CompareTo(b.LocalPort);
			if (c != 0) return c;
			c = string.Compare(a.Proto, b.Proto, StringComparison.Ordinal);
			if (c != 0) return c;
			return a.Pid.CompareTo(b.Pid);
		});
		return rows;
	}

	/// <summary>谁在监听这个 TCP 端口。没有则 0。只查表，不枚举进程。</summary>
	public static int ListenerPid(int port) {
		if (port <= 0 || port > 65535) return 0;
		var buf = table(AF_INET, TCP_OWNER_PID_ALL, true);
		if (buf == IntPtr.Zero) return 0;
		try {
			int n = Marshal.ReadInt32(buf);
			if (n < 0 || n > 100000) return 0;
			int any = 0;
			for (int i = 0; i < n; i++) {
				var p = IntPtr.Add(buf, 4 + i * 24);
				if (Marshal.ReadInt32(p, 0) != 2) continue;
				if (ntohs(Marshal.ReadInt32(p, 8)) != port) continue;
				int pid = Marshal.ReadInt32(p, 20);
				if (pid <= 0) continue;
				any = pid;
				if (Marshal.ReadInt32(p, 4) == 0x0100007F) return pid;
			}
			return any;
		}
		finally {
			Marshal.FreeHGlobal(buf);
		}
	}

	public static int SelfTest(Action<string> log) {
		void say(string s) { try { log?.Invoke(s); } catch { } }
		TcpListener listener = null;
		try {
			listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			int port = ((IPEndPoint)listener.LocalEndpoint).Port;
			int pid = Process.GetCurrentProcess().Id;
			var rows = List();
			if (rows.Count < 1) return bad(say, "empty");
			bool tcp = false, udp = false, hit = false, past = false;
			foreach (var r in rows) {
				if (r.LocalPort <= 0 || r.LocalPort > 65535) return bad(say, "port " + r.LocalPort);
				bool listen = r.State == "listen";
				if (listen && past) return bad(say, "order");
				if (!listen) past = true;
				if (r.Proto == "TCP") tcp = true;
				if (r.Proto == "UDP") udp = true;
				if (listen && r.LocalPort == port && r.Pid == pid) hit = true;
			}
			if (!tcp) return bad(say, "tcp");
			if (!udp) return bad(say, "udp");
			if (!hit) return bad(say, "listen " + port);
			say("=== OK：网络端口占用");
			return 0;
		}
		catch (Exception ex) {
			return bad(say, ex.Message);
		}
		finally {
			try { listener?.Stop(); } catch { }
		}
	}

	static void readtcp(List<Row> rows, Dictionary<int, (string name, string path)> proc, uint family, bool v6) {
		var buf = table(family, TCP_OWNER_PID_ALL, true);
		if (buf == IntPtr.Zero) return;
		try {
			int n = Marshal.ReadInt32(buf);
			if (n < 0 || n > 100000) return;
			int stride = v6 ? 56 : 24;
			for (int i = 0; i < n; i++) {
				var p = IntPtr.Add(buf, 4 + i * stride);
				var row = new Row { Proto = "TCP" };
				if (!v6) {
					row.State = statetoken(Marshal.ReadInt32(p, 0));
					row.Local = ip4(IntPtr.Add(p, 4));
					row.LocalPort = ntohs(Marshal.ReadInt32(p, 8));
					row.Remote = ip4(IntPtr.Add(p, 12));
					row.RemotePort = ntohs(Marshal.ReadInt32(p, 16));
					row.Pid = Marshal.ReadInt32(p, 20);
				}
				else {
					row.Local = ip6(p, Marshal.ReadInt32(p, 16));
					row.LocalPort = ntohs(Marshal.ReadInt32(p, 20));
					row.Remote = ip6(IntPtr.Add(p, 24), Marshal.ReadInt32(p, 40));
					row.RemotePort = ntohs(Marshal.ReadInt32(p, 44));
					row.State = statetoken(Marshal.ReadInt32(p, 48));
					row.Pid = Marshal.ReadInt32(p, 52);
				}
				fillproc(row, proc);
				rows.Add(row);
			}
		}
		finally {
			Marshal.FreeHGlobal(buf);
		}
	}

	static void readudp(List<Row> rows, Dictionary<int, (string name, string path)> proc, uint family, bool v6) {
		var buf = table(family, UDP_OWNER_PID, false);
		if (buf == IntPtr.Zero) return;
		try {
			int n = Marshal.ReadInt32(buf);
			if (n < 0 || n > 100000) return;
			int stride = v6 ? 28 : 12;
			for (int i = 0; i < n; i++) {
				var p = IntPtr.Add(buf, 4 + i * stride);
				var row = new Row { Proto = "UDP" };
				if (!v6) {
					row.Local = ip4(IntPtr.Add(p, 0));
					row.LocalPort = ntohs(Marshal.ReadInt32(p, 4));
					row.Pid = Marshal.ReadInt32(p, 8);
				}
				else {
					row.Local = ip6(p, Marshal.ReadInt32(p, 16));
					row.LocalPort = ntohs(Marshal.ReadInt32(p, 20));
					row.Pid = Marshal.ReadInt32(p, 24);
				}
				fillproc(row, proc);
				rows.Add(row);
			}
		}
		finally {
			Marshal.FreeHGlobal(buf);
		}
	}

	static void fillproc(Row row, Dictionary<int, (string name, string path)> proc) {
		if (!proc.TryGetValue(row.Pid, out var info)) {
			info = lookup(row.Pid);
			proc[row.Pid] = info;
		}
		row.Proc = info.name;
		row.Path = info.path;
	}

	static (string name, string path) lookup(int pid) {
		if (pid < 0) return ("", "");
		if (pid == 0) return ("Idle", "");
		if (pid == 4) return ("System", "");
		string name = "";
		try {
			using var p = Process.GetProcessById(pid);
			name = p.ProcessName ?? "";
		}
		catch { }
		return (name, imagepath(pid));
	}

	static string imagepath(int pid) {
		var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
		if (h == IntPtr.Zero) return "";
		try {
			int n = 1024;
			var sb = new StringBuilder(n);
			if (!QueryFullProcessImageName(h, 0, sb, ref n)) return "";
			return sb.ToString();
		}
		finally {
			CloseHandle(h);
		}
	}

	static IntPtr table(uint family, int cls, bool tcp) {
		int size = 0;
		if (tcp) GetExtendedTcpTable(IntPtr.Zero, ref size, true, family, cls, 0);
		else GetExtendedUdpTable(IntPtr.Zero, ref size, true, family, cls, 0);
		if (size <= 0) return IntPtr.Zero;
		var buf = Marshal.AllocHGlobal(size);
		uint err = tcp
			? GetExtendedTcpTable(buf, ref size, true, family, cls, 0)
			: GetExtendedUdpTable(buf, ref size, true, family, cls, 0);
		if (err == 0) return buf;
		Marshal.FreeHGlobal(buf);
		return IntPtr.Zero;
	}

	static int rank(Row r) => r.State == "listen" ? 0 : 1;

	static int ntohs(int v) {
		int p = v & 0xFFFF;
		return ((p & 0xFF) << 8) | (p >> 8);
	}

	static string ip4(IntPtr p) {
		var b = new byte[4];
		Marshal.Copy(p, b, 0, 4);
		return new IPAddress(b).ToString();
	}

	static string ip6(IntPtr p, int scope) {
		var b = new byte[16];
		Marshal.Copy(p, b, 0, 16);
		var ip = new IPAddress(b, (uint)scope);
		return ip.ToString();
	}

	static string statetoken(int state) => state switch {
		1 => "closed",
		2 => "listen",
		3 => "syn_sent",
		4 => "syn_rcvd",
		5 => "established",
		6 => "fin_wait1",
		7 => "fin_wait2",
		8 => "close_wait",
		9 => "closing",
		10 => "last_ack",
		11 => "time_wait",
		12 => "delete_tcb",
		_ => "",
	};

	static int bad(Action<string> say, string msg) {
		say("FAIL " + msg);
		return 1;
	}

	[DllImport("iphlpapi.dll", SetLastError = true)]
	static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, uint family, int cls, uint reserved);

	[DllImport("iphlpapi.dll", SetLastError = true)]
	static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool order, uint family, int cls, uint reserved);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern IntPtr OpenProcess(int access, bool inherit, int pid);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool CloseHandle(IntPtr handle);
}
