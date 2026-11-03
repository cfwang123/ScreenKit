using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ScreenKit;

public enum NetProtocol {
	Unknown,
	TCP,
	UDP,
	HTTP,
	HTTPS,
	DNS,
	SSH,
	FTP,
	SMTP,
	POP3,
	IMAP,
	RDP,
	SMB,
	MQTT,
	Redis,
	MySQL,
	PostgreSQL,
	MSSQL,
	QUIC,
	Other
}

public enum ConnState {
	Unknown = 0,
	Closed = 1,
	Listen = 2,
	SynSent = 3,
	SynRcvd = 4,
	Established = 5,
	FinWait1 = 6,
	FinWait2 = 7,
	CloseWait = 8,
	Closing = 9,
	LastAck = 10,
	TimeWait = 11,
	DeleteTcb = 12
}

/// <summary>单条 TCP/UDP 连接快照。</summary>
public sealed class ConnectionRow : INotifyPropertyChanged {
	public string Key { get; set; }
	public int Pid { get; set; }
	public string ProcessName { get; set; }
	public string ProcessPath { get; set; }
	public NetProtocol Transport { get; set; }
	public NetProtocol AppProtocol { get; set; }
	public string LocalIp { get; set; }
	public int LocalPort { get; set; }
	public string RemoteIp { get; set; }
	public int RemotePort { get; set; }
	public string LocalEndPoint { get; set; }
	/// <summary>远程展示：优先 域名:端口，否则 IP:端口。</summary>
	public string RemoteEndPoint { get; set; }
	/// <summary>纯 IP:端口（始终）。</summary>
	public string RemoteIpPort { get; set; }
	/// <summary>反向/缓存得到的域名，无则为空。</summary>
	public string RemoteHost { get; set; }
	public ConnState State { get; set; }
	public long BytesSent { get; set; }
	public long BytesRecv { get; set; }
	public long PacketsSent { get; set; }
	public long PacketsRecv { get; set; }
	public double SendBps { get; set; }
	public double RecvBps { get; set; }
	public int LastSeenTick { get; set; }
	public bool IsIpv6 { get; set; }
	/// <summary>该连接在抓包缓存中的报文条数。</summary>
	public int CaptureCount { get; set; }
	/// <summary>已断开、因「保留历史」仍显示的连接。</summary>
	public bool IsHistory { get; set; }
	/// <summary>筛选命中高亮。</summary>
	public bool IsHighlight { get; set; }
	/// <summary>规则着色：Black / White / null。</summary>
	public string RuleTag { get; set; }
	public string StateText => IsHistory ? "已断开" : ProtocolClassifierState(State);
	public string ActiveText => IsHistory ? "历史" : "活动";

	static string ProtocolClassifierState(ConnState s) => s switch {
		ConnState.Closed => "CLOSED",
		ConnState.Listen => "LISTEN",
		ConnState.SynSent => "SYN_SENT",
		ConnState.SynRcvd => "SYN_RCVD",
		ConnState.Established => "ESTABLISHED",
		ConnState.FinWait1 => "FIN_WAIT1",
		ConnState.FinWait2 => "FIN_WAIT2",
		ConnState.CloseWait => "CLOSE_WAIT",
		ConnState.Closing => "CLOSING",
		ConnState.LastAck => "LAST_ACK",
		ConnState.TimeWait => "TIME_WAIT",
		ConnState.DeleteTcb => "DELETE_TCB",
		_ => "UNKNOWN"
	};

	public event PropertyChangedEventHandler PropertyChanged;

	public void Notify([CallerMemberName] string name = null) {
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}

	public void NotifyAll() {
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
	}
}

/// <summary>按进程聚合的网络统计。</summary>
public sealed class ProcessNetRow : INotifyPropertyChanged {
	public int Pid { get; set; }
	public string ProcessName { get; set; }
	public string ProcessPath { get; set; }
	public int ConnectionCount { get; set; }
	public long BytesSent { get; set; }
	public long BytesRecv { get; set; }
	public long PacketsSent { get; set; }
	public long PacketsRecv { get; set; }
	public double SendBps { get; set; }
	public double RecvBps { get; set; }
	public int LastSeenTick { get; set; }
	/// <summary>该进程相关连接在抓包缓存中的报文总数。</summary>
	public int CaptureCount { get; set; }
	/// <summary>筛选命中高亮。</summary>
	public bool IsHighlight { get; set; }
	/// <summary>规则着色：Black / White / null。</summary>
	public string RuleTag { get; set; }
	/// <summary>速率历史（sparkline）。</summary>
	public double[] SendHistory { get; set; }
	public double[] RecvHistory { get; set; }

	public string TotalBytesText => FormatBytes(BytesSent + BytesRecv);
	public string SendBpsText => FormatRate(SendBps);
	public string RecvBpsText => FormatRate(RecvBps);
	public string BytesSentText => FormatBytes(BytesSent);
	public string BytesRecvText => FormatBytes(BytesRecv);

	public event PropertyChangedEventHandler PropertyChanged;

	public void NotifyAll() {
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
	}

	public static string FormatBytes(long n) {
		if (n < 1024) return $"{n} B";
		if (n < 1024 * 1024) return $"{n / 1024.0:F1} KB";
		if (n < 1024L * 1024 * 1024) return $"{n / (1024.0 * 1024):F2} MB";
		if (n < 1024L * 1024 * 1024 * 1024) return $"{n / (1024.0 * 1024 * 1024):F2} GB";
		return $"{n / (1024.0 * 1024 * 1024 * 1024):F2} TB";
	}

	public static string FormatRate(double bps) {
		if (bps < 1024) return $"{bps:F0} B/s";
		if (bps < 1024 * 1024) return $"{bps / 1024.0:F1} KB/s";
		return $"{bps / (1024.0 * 1024):F2} MB/s";
	}
}

/// <summary>历史访问过的域名 / IP:端口。</summary>
public sealed class HostRecord : INotifyPropertyChanged {
	public string Key { get; set; }
	public int Pid { get; set; }
	public string ProcessName { get; set; }
	public string Host { get; set; }
	public string IpPort { get; set; }
	public NetProtocol Protocol { get; set; }
	public long HitCount { get; set; }
	public long BytesSent { get; set; }
	public long BytesRecv { get; set; }
	public DateTime FirstSeen { get; set; }
	public DateTime LastSeen { get; set; }
	/// <summary>筛选命中高亮。</summary>
	public bool IsHighlight { get; set; }

	public string ProtocolText => Protocol.ToString();
	/// <summary>总流量（字节），供排序绑定；显示用转换器格式化。</summary>
	public long TotalBytes => BytesSent + BytesRecv;
	public string BytesText => ProcessNetRow.FormatBytes(TotalBytes);
	public string FirstSeenText => FirstSeen.ToString("HH:mm:ss");
	public string LastSeenText => LastSeen.ToString("HH:mm:ss");

	public event PropertyChangedEventHandler PropertyChanged;

	public void NotifyAll() {
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
	}
}
