using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ScreenKit;

/// <summary>UDP 调试：监听一个端口，把输入的报文发到指定地址，收到时按设定自动回复。</summary>
static class UdpDebug {
	/// <summary>报文区里不可见字节的占位符。</summary>
	public const char Blank = '\u25A1';

	/// <summary>把输入解析成要发送的字节。hex 模式按十六进制，否则按所选编码。</summary>
	public static byte[] Parse(string text, bool hex, Encoding enc) {
		if (hex) return parsehex(text);
		return (enc ?? Encoding.UTF8).GetBytes(text ?? "");
	}

	/// <summary>报文区里一行的内容：可打印字符原样，其余换成方框。</summary>
	public static string Show(byte[] data, bool hex, Encoding enc) {
		if (data == null || data.Length == 0) return "";
		if (hex) return tohex(data);
		var s = (enc ?? Encoding.UTF8).GetString(data);
		var sb = new StringBuilder(s.Length);
		foreach (var c in s)
			sb.Append(c == '\t' || c >= 0x20 && c != 0x7F ? c : Blank);
		return sb.ToString();
	}

	/// <summary>一行报文头，如 22:52:20.836 发2字节。</summary>
	public static string Head(DateTime when, bool sent, int bytes, string who) {
		var dir = sent ? "发" : "收";
		var tail = string.IsNullOrEmpty(who) ? "" : " " + who;
		return $"[{when:HH:mm:ss.fff} {dir}{bytes}字节{tail}]";
	}

	/// <summary>对方地址。ip 空时用 127.0.0.1。</summary>
	public static IPEndPoint Peer(string ip, int port) {
		if (port < 0 || port > 65535) throw new FormatException("bad port");
		var host = string.IsNullOrWhiteSpace(ip) ? "127.0.0.1" : ip.Trim();
		if (!IPAddress.TryParse(host, out var addr)) throw new FormatException("bad ip");
		return new IPEndPoint(addr, port);
	}

	/// <summary>本机监听端口。空或 0 表示由系统分配。</summary>
	public static int LocalPort(string text) {
		var s = (text ?? "").Trim();
		if (s.Length == 0) return 0;
		if (!int.TryParse(s, out var p) || p < 0 || p > 65535) throw new FormatException("bad port");
		return p;
	}

	/// <summary>上次填写的地址、端口和报文。放 data.json 的 udplast 段。</summary>
	public sealed class Last {
		public string Peer { get; set; } = "";
		public string PeerPort { get; set; } = "";
		public string LocalPort { get; set; } = "";
		public string Payload { get; set; } = "";
		public string Interval { get; set; } = "1000";
		public bool HexShow { get; set; }
		public bool HexSend { get; set; }
		public bool Reply { get; set; }
		public bool Scroll { get; set; } = true;
		public int Enc { get; set; }
		/// <summary>0 为 UDP，1 为 TCP。</summary>
		public int Proto { get; set; }
	}

	const string SECTION = "udplast";

	public static Last LoadLast() {
		try { return DataStore.Get<Last>(SECTION) ?? new Last(); }
		catch { return new Last(); }
	}

	public static void SaveLast(Last st) {
		if (st == null) return;
		try { DataStore.Put(SECTION, st); }
		catch { }
	}

	/// <summary>定时发送间隔，至少 1 毫秒。</summary>
	public static int Interval(string text) {
		if (!int.TryParse((text ?? "").Trim(), out var ms) || ms < 1) throw new FormatException("bad interval");
		return ms;
	}

	static byte[] parsehex(string text) {
		var hex = new StringBuilder();
		foreach (var c in text ?? "") {
			if (Uri.IsHexDigit(c)) hex.Append(c);
			else if (!char.IsWhiteSpace(c)) throw new FormatException("bad hex");
		}
		if (hex.Length % 2 != 0) throw new FormatException("bad hex");
		var n = hex.Length / 2;
		var data = new byte[n];
		for (var i = 0; i < n; i++)
			data[i] = Convert.ToByte(hex.ToString(i * 2, 2), 16);
		return data;
	}

	static string tohex(byte[] data) {
		var sb = new StringBuilder(data.Length * 3);
		for (var i = 0; i < data.Length; i++) {
			if (i > 0) sb.Append(' ');
			sb.Append(data[i].ToString("X2"));
		}
		return sb.ToString();
	}

	/// <summary>
	/// 自测：起两个端口互发，核对 hex 解析、文本编码、收发记录和自动回复。
	/// 返回空表示通过，否则是失败原因。
	/// </summary>
	public static string SelfTest() {
		var hex = Parse("10 0a FF", true, Encoding.UTF8);
		if (hex.Length != 3 || hex[0] != 0x10 || hex[1] != 0x0A || hex[2] != 0xFF)
			return "hex parse";
		try { Parse("10 0", true, Encoding.UTF8); return "odd hex accepted"; }
		catch (FormatException) { }
		try { Parse("zz", true, Encoding.UTF8); return "bad hex accepted"; }
		catch (FormatException) { }

		var text = Parse("AB", false, Encoding.ASCII);
		if (text.Length != 2 || text[0] != (byte)'A') return "text parse";
		var shown = Show(new byte[] { 0x41, 0x01, 0x42 }, false, Encoding.ASCII);
		if (shown.Length != 3 || shown[0] != 'A' || shown[1] != Blank || shown[2] != 'B')
			return "text show " + shown;
		if (Show(new byte[] { 0x10, 0xFF }, true, Encoding.UTF8) != "10 FF") return "hex show";

		var head = Head(new DateTime(2026, 1, 1, 22, 52, 20, 836), true, 2, "");
		if (head != "[22:52:20.836 发2字节]") return "head " + head;
		if (Peer("", 9).ToString() != "127.0.0.1:9") return "peer default";
		if (LocalPort("") != 0 || LocalPort("0") != 0 || Interval("1000") != 1000) return "port";

		using var a = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		using var b = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		var ap = (IPEndPoint)a.Client.LocalEndPoint;
		var bp = (IPEndPoint)b.Client.LocalEndPoint;
		var payload = new byte[] { 0x10, 0x20 };
		a.Send(payload, payload.Length, bp);
		var from = new IPEndPoint(IPAddress.Any, 0);
		var got = b.Receive(ref from);
		if (got.Length != 2 || got[0] != 0x10 || from.Port != ap.Port) return "recv";
		var reply = Parse("10", true, Encoding.UTF8);
		b.Send(reply, reply.Length, from);
		var back = new IPEndPoint(IPAddress.Any, 0);
		var echoed = a.Receive(ref back);
		if (echoed.Length != 1 || echoed[0] != 0x10 || back.Port != bp.Port) return "reply";

		var tcp = tcpprobe();
		if (tcp.Length > 0) return tcp;

		var path = Path.Combine(Path.GetTempPath(), "sk-udpdbg-" + Guid.NewGuid().ToString("N") + ".json");
		try {
			DataStore.UsePath(path);
			SaveLast(new Last { Peer = "10.0.0.8", PeerPort = "9000", Payload = "AB\n", HexSend = true, Enc = 1 });
			var back2 = LoadLast();
			if (back2.Peer != "10.0.0.8" || back2.PeerPort != "9000" || back2.Payload != "AB\n"
				|| !back2.HexSend || back2.Enc != 1)
				return "last";
		}
		finally {
			DataStore.UsePath(null);
			try { File.Delete(path); } catch { }
		}
		return "";
	}

	/// <summary>TCP 回环：客户端连上、发出、服务端读到并回，客户端再读到。</summary>
	static string tcpprobe() {
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		try {
			var port = ((IPEndPoint)listener.LocalEndpoint).Port;
			using var client = new TcpClient();
			client.Connect(IPAddress.Loopback, port);
			using var accepted = listener.AcceptTcpClient();
			var payload = Parse("10", true, Encoding.UTF8);
			client.GetStream().Write(payload, 0, payload.Length);
			var buf = new byte[8];
			var n = accepted.GetStream().Read(buf, 0, buf.Length);
			if (n != 1 || buf[0] != 0x10) return "tcp recv";
			accepted.GetStream().Write(payload, 0, payload.Length);
			var back = new byte[8];
			var m = client.GetStream().Read(back, 0, back.Length);
			if (m != 1 || back[0] != 0x10) return "tcp reply";
			return "";
		}
		catch (Exception ex) { return "tcp " + ex.Message; }
		finally { try { listener.Stop(); } catch { } }
	}
}
