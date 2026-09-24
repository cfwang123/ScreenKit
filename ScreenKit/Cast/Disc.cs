using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenKit;

sealed class CastPeer {
	public string Name, Ip, Role;
	public int Tcp;
	public int LastSeen;
}

sealed class CastDisc : IDisposable {
	UdpClient u;
	readonly int port;
	readonly string role;
	readonly Func<string> name;
	readonly Dictionary<string, CastPeer> peers = new();
	public Action<string> Log;
	bool stop;

	public CastDisc(string role, Func<string> name, int port = CastProto.UDP_PORT) {
		this.role = role;
		this.name = name;
		this.port = port;
		u = new UdpClient();
		u.EnableBroadcast = true;
		u.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
		u.Client.Bind(new IPEndPoint(IPAddress.Any, port));
		u.BeginReceive(onrecv, null);
	}

	public void Beacon(int tcpPort) {
		if (stop) return;
		var json = JsonSerializer.Serialize(new {
			v = 1, app = "screencast", name = name(), role, tcp = tcpPort,
			http = CastHost.Opt?.HttpPort > 0 ? CastHost.Opt.HttpPort : 1224
		});
		var bytes = Encoding.UTF8.GetBytes(json);
		sendto(bytes, IPAddress.Broadcast);
		foreach (var b in subnetbroadcasts())
			sendto(bytes, b);
	}

	void sendto(byte[] bytes, IPAddress ip) {
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
		try {
			var o = JsonNode.Parse(Encoding.UTF8.GetString(data)) as JsonObject;
			if (o == null) return;
			if (CastProto.Jstr(o, "app") != "screencast") return;
			var p = new CastPeer {
				Name = CastProto.Jstr(o, "name"),
				Ip = ep.Address.ToString(),
				Role = CastProto.Jstr(o, "role"),
				Tcp = CastProto.Jint(o, "tcp"),
				LastSeen = Environment.TickCount
			};
			if (string.IsNullOrEmpty(p.Name)) p.Name = ep.Address.ToString();
			if (p.Tcp <= 0) p.Tcp = CastProto.TCP_PORT;
			if (p.Name == name() && p.Role == role) return;
			lock (peers) peers[p.Ip + ":" + p.Role] = p;
			if (p.Role == "send")
				reply(ep, CastProto.Jint(o, "tcp") == 0 ? CastProto.TCP_PORT : p.Tcp);
		}
		catch { }
	}

	void reply(IPEndPoint ep, int tcpPort) {
		var json = JsonSerializer.Serialize(new {
			v = 1, app = "screencast", name = name(), role, tcp = tcpPort,
			http = CastHost.Opt?.HttpPort > 0 ? CastHost.Opt.HttpPort : 1224
		});
		var bytes = Encoding.UTF8.GetBytes(json);
		try { u.Send(bytes, bytes.Length, new IPEndPoint(ep.Address, port)); }
		catch { }
		try { u.Send(bytes, bytes.Length, ep); }
		catch { }
	}

	public void Dispose() {
		stop = true;
		try { u?.Close(); } catch { }
		u = null;
	}
}
