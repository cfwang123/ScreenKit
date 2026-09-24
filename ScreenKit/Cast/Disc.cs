using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace ScreenKit;

sealed class CastPeer {
	public string Name, Ip, Role;
	public int Tcp;
	public int LastSeen;
}

/// <summary>投屏发现走传文件同一条 UDP（SCREENKIT_DISCOVER / 17531），不另占端口。</summary>
sealed class CastDisc : IDisposable {
	UdpClient u;
	readonly string role;
	readonly Func<string> name;
	readonly Dictionary<string, CastPeer> peers = new();
	public Action<string> Log;
	bool stop;
	bool own;

	public CastDisc(string role, Func<string> name, int port = 0) {
		this.role = role;
		this.name = name;
		u = new UdpClient();
		u.EnableBroadcast = true;
		var p = port > 0 ? port : udpport();
		try {
			u.Client.Bind(new IPEndPoint(IPAddress.Any, p));
			own = true;
		}
		catch {
			u.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
			own = false;
		}
		u.BeginReceive(onrecv, null);
	}

	static int udpport() {
		var p = CastHost.Opt?.SendFileUdpPort ?? 0;
		return p > 0 ? p : 17531;
	}

	static int httpport() {
		var p = CastHost.Opt?.HttpPort ?? 0;
		return p > 0 ? p : 1224;
	}

	public void Scan() {
		if (stop) return;
		var bytes = Encoding.ASCII.GetBytes(SendFileServer.DISCOVER);
		var p = udpport();
		sendto(bytes, IPAddress.Broadcast, p);
		foreach (var b in subnetbroadcasts())
			sendto(bytes, b, p);
	}

	public void Beacon(int tcpPort) => Scan();

	void sendto(byte[] bytes, IPAddress ip, int port) {
		try { u.Send(bytes, bytes.Length, new IPEndPoint(ip, port)); }
		catch { }
	}

	static List<IPAddress> subnetbroadcasts() {
		var list = new List<IPAddress>();
		try {
			foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()) {
				if (ni.OperationalStatus != OperationalStatus.Up) continue;
				if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
				foreach (var ua in ni.GetIPProperties().UnicastAddresses) {
					if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
					if (ua.IPv4Mask == null) continue;
					var ip = ua.Address.GetAddressBytes();
					var mask = ua.IPv4Mask.GetAddressBytes();
					if (ip.Length != 4 || mask.Length != 4) continue;
					var b = new byte[4];
					for (int i = 0; i < 4; i++)
						b[i] = (byte)(ip[i] | (byte)~mask[i]);
					list.Add(new IPAddress(b));
				}
			}
		}
		catch { }
		return list;
	}

	public List<CastPeer> Snapshot() {
		var now = Environment.TickCount;
		var list = new List<CastPeer>();
		lock (peers) {
			foreach (var p in peers.Values) {
				if (now - p.LastSeen > 8000) continue;
				list.Add(p);
			}
		}
		return list;
	}

	void onrecv(IAsyncResult ar) {
		IPEndPoint ep = null;
		byte[] data = null;
		try { data = u.EndReceive(ar, ref ep); }
		catch { }
		if (!stop)
			try { u.BeginReceive(onrecv, null); } catch { }
		if (data == null || ep == null) return;
		var s = Encoding.UTF8.GetString(data).Trim();
		if (own && s.StartsWith(SendFileServer.DISCOVER, StringComparison.OrdinalIgnoreCase)) {
			reply(ep);
			return;
		}
		if (!s.StartsWith("{")) return;
		try {
			var o = JsonNode.Parse(s) as JsonObject;
			if (o == null) return;
			var http = CastProto.Jint(o, "httpPort");
			if (http <= 0) http = CastProto.Jint(o, "http");
			if (http <= 0) http = CastProto.Jint(o, "tcp");
			if (http <= 0) return;
			var p = new CastPeer {
				Name = CastProto.Jstr(o, "name"),
				Ip = ep.Address.ToString(),
				Role = CastProto.Jstr(o, "role"),
				Tcp = http,
				LastSeen = Environment.TickCount
			};
			if (string.IsNullOrEmpty(p.Name)) p.Name = ep.Address.ToString();
			if (string.IsNullOrEmpty(p.Role)) p.Role = "pc";
			if (p.Role == "send") return;
			if (p.Name == name() && islocal(ep.Address)) return;
			lock (peers) peers[p.Ip] = p;
		}
		catch { }
	}

	static bool islocal(IPAddress ip) {
		if (IPAddress.IsLoopback(ip)) return true;
		foreach (var a in CastNetUtil.V4Addrs())
			if (a.Equals(ip)) return true;
		return false;
	}

	void reply(IPEndPoint ep) {
		var o = CastHost.Opt;
		if (o != null && !o.SendFileEnabled && !o.CastRecvEnabled) return;
		var nm = string.IsNullOrWhiteSpace(o?.SendFileName) ? name() : o.SendFileName.Trim();
		var json = new JsonObject {
			["v"] = 1,
			["name"] = nm,
			["httpPort"] = httpport(),
			["pcId"] = o?.SendFilePcId ?? "",
			["cmd"] = "discover",
			["cast"] = true,
		}.ToJsonString();
		var bytes = Encoding.UTF8.GetBytes(json);
		try { u.Send(bytes, bytes.Length, ep); }
		catch { }
	}

	public void Dispose() {
		stop = true;
		try { u?.Close(); } catch { }
		u = null;
	}
}
