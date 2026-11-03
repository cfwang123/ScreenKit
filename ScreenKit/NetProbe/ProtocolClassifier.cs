
namespace ScreenKit;

/// <summary>根据端口与传输层协议推断应用层协议。</summary>
static class ProtocolClassifier {
	static readonly Dictionary<int, NetProtocol> PortMap = new() {
		{ 80, NetProtocol.HTTP },
		{ 8080, NetProtocol.HTTP },
		{ 8000, NetProtocol.HTTP },
		{ 8888, NetProtocol.HTTP },
		{ 443, NetProtocol.HTTPS },
		{ 8443, NetProtocol.HTTPS },
		{ 53, NetProtocol.DNS },
		{ 22, NetProtocol.SSH },
		{ 21, NetProtocol.FTP },
		{ 20, NetProtocol.FTP },
		{ 25, NetProtocol.SMTP },
		{ 465, NetProtocol.SMTP },
		{ 587, NetProtocol.SMTP },
		{ 110, NetProtocol.POP3 },
		{ 995, NetProtocol.POP3 },
		{ 143, NetProtocol.IMAP },
		{ 993, NetProtocol.IMAP },
		{ 3389, NetProtocol.RDP },
		{ 445, NetProtocol.SMB },
		{ 139, NetProtocol.SMB },
		{ 1883, NetProtocol.MQTT },
		{ 8883, NetProtocol.MQTT },
		{ 6379, NetProtocol.Redis },
		{ 3306, NetProtocol.MySQL },
		{ 5432, NetProtocol.PostgreSQL },
		{ 1433, NetProtocol.MSSQL },
		{ 853, NetProtocol.DNS },
		{ 4433, NetProtocol.QUIC },
	};

	public static NetProtocol Classify(NetProtocol transport, int remotePort, int localPort) {
		if (PortMap.TryGetValue(remotePort, out var p)) return p;
		if (PortMap.TryGetValue(localPort, out p)) return p;
		if (transport == NetProtocol.UDP && (remotePort == 443 || localPort == 443))
			return NetProtocol.QUIC;
		return transport == NetProtocol.TCP ? NetProtocol.TCP : transport == NetProtocol.UDP ? NetProtocol.UDP : NetProtocol.Unknown;
	}

	public static string ToDisplay(NetProtocol p) => p switch {
		NetProtocol.HTTP => "HTTP",
		NetProtocol.HTTPS => "HTTPS",
		NetProtocol.DNS => "DNS",
		NetProtocol.SSH => "SSH",
		NetProtocol.FTP => "FTP",
		NetProtocol.SMTP => "SMTP",
		NetProtocol.POP3 => "POP3",
		NetProtocol.IMAP => "IMAP",
		NetProtocol.RDP => "RDP",
		NetProtocol.SMB => "SMB",
		NetProtocol.MQTT => "MQTT",
		NetProtocol.Redis => "Redis",
		NetProtocol.MySQL => "MySQL",
		NetProtocol.PostgreSQL => "PostgreSQL",
		NetProtocol.MSSQL => "MSSQL",
		NetProtocol.QUIC => "QUIC/HTTP3",
		NetProtocol.TCP => "TCP",
		NetProtocol.UDP => "UDP",
		_ => "Other"
	};

	public static string StateText(ConnState s) => s switch {
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
}
