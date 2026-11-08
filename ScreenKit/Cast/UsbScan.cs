using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ScreenKit;

sealed class CastUsbScan {
	public CastRecvSrv Recv;
	public Action<string> Log;
	public Func<List<string>> ExtraIps;
	int lasttry;
	volatile bool scanning;

	public void Tick() {
		if (Recv == null || !Recv.Running || Recv.Busy || scanning) return;
		var now = Environment.TickCount;
		if (lasttry != 0 && now - lasttry < 1500) return;
		lasttry = now;
		var ips = usbNeighbors();
		if (ExtraIps != null) {
			foreach (var ip in ExtraIps()) {
				if (string.IsNullOrEmpty(ip) || ips.Contains(ip)) continue;
				if (ip.StartsWith("192.168.42.") || ip.StartsWith("192.168.137."))
					ips.Insert(0, ip);
			}
		}
		if (ips.Count == 0) return;
		addknown(ips);
		// 本机地址不扫：Wi‑Fi Direct/ICS 段上就是自己，连上会把接收端顶掉
		var mine = CastNetUtil.V4Addrs();
		ips.RemoveAll(x => {
			if (!IPAddress.TryParse(x, out var a)) return true;
			return mine.Exists(m => m.Equals(a));
		});
		if (ips.Count == 0) return;
		scanning = true;
		ThreadPool.QueueUserWorkItem(_ => {
			try { scan(ips); }
			catch (Exception ex) { Log?.Invoke($"USB 扫描: {ex.Message}"); }
			finally { scanning = false; }
		});
	}

	void scan(List<string> ips) {
		var found = new TcpClient[1];
		var giveup = new bool[1];
		var left = ips.Count;
		foreach (var ip in ips)
			ThreadPool.QueueUserWorkItem(_ => {
				TcpClient cli = null;
				try {
					if (Volatile.Read(ref found[0]) != null || Recv == null || Recv.Busy) return;
					cli = trycli(ip);
					if (cli == null) return;
					if (Interlocked.CompareExchange(ref found[0], cli, null) == null) {
						// 超时放弃后连上的，谁拿回来谁负责关，不留给下次
						if (Volatile.Read(ref giveup[0]) &&
							Interlocked.CompareExchange(ref found[0], null, cli) == cli) return;
						cli = null;
					}
				}
				catch { }
				finally {
					try { cli?.Close(); } catch { }
					Interlocked.Decrement(ref left);
				}
			});
		var start = Environment.TickCount;
		while (Volatile.Read(ref left) > 0 && unchecked(Environment.TickCount - start) < 2500) {
			if (Volatile.Read(ref found[0]) != null) break;
			Thread.Sleep(20);
		}
		var c = Interlocked.Exchange(ref found[0], null);
		if (c == null) {
			// 置位后连上的工作项自己关；这里再取一次，补上置位与取出之间的竞态
			giveup[0] = true;
			Thread.Sleep(30);
			c = Interlocked.Exchange(ref found[0], null);
		}
		giveup[0] = true;
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
		};
		foreach (var ip in xs)
			if (!list.Contains(ip)) list.Add(ip);
	}

	static bool isusb(NetworkInterface ni) {
		var s = (ni.Name + " " + ni.Description).ToLowerInvariant();
		if (s.Contains("vmware") || s.Contains("virtualbox") || s.Contains("hyper-v") ||
			s.Contains("vethernet") || s.Contains("tap-windows") || s.Contains("vpn"))
			return false;
		// Windows ICS / Wi‑Fi Direct 默认就是 192.168.137.x，不是手机；按 IP 认网卡会让空扫一直跑
		if (s.Contains("wi-fi direct") || s.Contains("wifi direct") || s.Contains("wfd") ||
			s.Contains("hotspot") || s.Contains("热点") || s.Contains("虚拟"))
			return false;
		foreach (var ua in ni.GetIPProperties().UnicastAddresses) {
			if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
			var ip = ua.Address.ToString();
			if (ip.StartsWith("192.168.42.")) return true;
		}
		if (s.Contains("rndis") || s.Contains("remote ndis") || (s.Contains("远程") && s.Contains("ndis")) ||
			s.Contains("android") || s.Contains("gadget") || s.Contains("网络共享") ||
			s.Contains("ncm") || s.Contains("mobile broadband"))
			return true;
		if (s.Contains("ndis") && (s.Contains("internet") || s.Contains("sharing") || s.Contains("共享")))
			return true;
		return false;
	}
}
