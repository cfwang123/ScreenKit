using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ScreenKit;

sealed class CastUsbScan {
	public CastRecvSrv Recv;
	public Action<string> Log;
	int lasttry;
	volatile bool scanning;

	public void Tick() {
		if (Recv == null || Recv.Busy || scanning) return;
		var now = Environment.TickCount;
		if (lasttry != 0 && now - lasttry < 1500) return;
		lasttry = now;
		var ips = usbNeighbors();
		if (ips.Count == 0) return;
		addknown(ips);
		scanning = true;
		ThreadPool.QueueUserWorkItem(_ => {
			try { scan(ips); }
			finally { scanning = false; }
		});
	}

	void scan(List<string> ips) {
		var found = new TcpClient[1];
		var n = Math.Min(32, Math.Max(4, ips.Count));
		using var gate = new Semaphore(n, n);
		using var done = new ManualResetEvent(false);
		var left = ips.Count;
		foreach (var ip in ips) {
			gate.WaitOne();
			if (found[0] != null || Recv == null || Recv.Busy) {
				Interlocked.Decrement(ref left);
				gate.Release();
				if (left <= 0) done.Set();
				continue;
			}
			ThreadPool.QueueUserWorkItem(_ => {
				TcpClient cli = null;
				try {
					if (found[0] != null || Recv.Busy) return;
					cli = trycli(ip);
					if (cli == null) return;
					if (Interlocked.CompareExchange(ref found[0], cli, null) == null) {
						cli = null;
						done.Set();
					}
				}
				finally {
					try { cli?.Close(); } catch { }
					gate.Release();
					if (Interlocked.Decrement(ref left) <= 0) done.Set();
				}
			});
		}
		done.WaitOne(2500);
		var c = found[0];
		if (c == null) return;
		try {
			var ep = c.Client?.RemoteEndPoint;
			Log?.Invoke($"USB 网络连上 {ep}");
			Recv.AttachStream(c.GetStream(), $"USB {ep}");
		}
		catch (Exception ex) { Log?.Invoke($"USB 网络: {ex.Message}"); }
		finally { try { c.Close(); } catch { } }
	}

	static TcpClient trycli(string ip) {
		var cli = new TcpClient();
		try {
			cli.NoDelay = true;
			cli.ReceiveBufferSize = 4 * 1024 * 1024;
			cli.SendBufferSize = 512 * 1024;
			var ar = cli.BeginConnect(ip, CastProto.TCP_PORT, null, null);
			if (!ar.AsyncWaitHandle.WaitOne(250)) {
				cli.Close();
				return null;
			}
			cli.EndConnect(ar);
			if (!cli.Connected) {
				cli.Close();
				return null;
			}
			return cli;
		}
		catch {
			try { cli.Close(); } catch { }
			return null;
		}
	}

	static List<string> usbNeighbors() {
		var list = new List<string>();
		var own = new HashSet<string>();
		try {
			foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()) {
				if (ni.OperationalStatus != OperationalStatus.Up) continue;
				if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
				if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) continue;
				if (!isusb(ni)) continue;
				foreach (var ua in ni.GetIPProperties().UnicastAddresses) {
					if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
					var b = ua.Address.GetAddressBytes();
					if (b.Length != 4) continue;
					var me = ua.Address.ToString();
					own.Add(me);
					var p = $"{b[0]}.{b[1]}.{b[2]}";
					int[] first = { 129, 1, 2, 137, 100, 101, 254, 10, 20, 3 };
					foreach (var i in first) {
						var ip = $"{p}.{i}";
						if (!own.Contains(ip) && !list.Contains(ip)) list.Add(ip);
					}
					for (var i = 1; i <= 254; i++) {
						var ip = $"{p}.{i}";
						if (own.Contains(ip) || list.Contains(ip)) continue;
						list.Add(ip);
					}
				}
			}
		}
		catch { }
		return list;
	}

	static void addknown(List<string> list) {
		string[] xs = {
			"192.168.42.129", "192.168.42.1", "192.168.42.2",
			"192.168.137.1", "192.168.137.2", "192.168.137.129",
			"192.168.0.1", "192.168.1.1",
		};
		foreach (var ip in xs)
			if (!list.Contains(ip)) list.Add(ip);
	}

	static bool isusb(NetworkInterface ni) {
		var s = (ni.Name + " " + ni.Description).ToLowerInvariant();
		if (s.Contains("vmware") || s.Contains("virtualbox") || s.Contains("hyper-v") || s.Contains("vethernet"))
			return false;
		if (s.Contains("rndis") || s.Contains("remote ndis") || s.Contains("远程") && s.Contains("ndis") ||
			s.Contains("android") || s.Contains("gadget") || s.Contains("网络共享") ||
			s.Contains("usb ethernet") || s.Contains("usb 以太网") || s.Contains("usb网") ||
			s.Contains("mobile broadband"))
			return true;
		if (s.Contains("ndis") && (s.Contains("internet") || s.Contains("sharing") || s.Contains("共享")))
			return true;
		foreach (var ua in ni.GetIPProperties().UnicastAddresses) {
			if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
			var ip = ua.Address.ToString();
			if (ip.StartsWith("192.168.42.") || ip.StartsWith("192.168.137.")) return true;
		}
		return false;
	}
}
