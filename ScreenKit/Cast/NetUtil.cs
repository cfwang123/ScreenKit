using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace ScreenKit;

static class CastNetUtil {
	public static void TryFirewall(int httpPort = 0) {
		runnetsh("advfirewall firewall delete rule name=\"ScreenKit Cast TCP\"");
		runnetsh("advfirewall firewall delete rule name=\"ScreenKit Cast TCP extra\"");
		runnetsh("advfirewall firewall delete rule name=\"ScreenKit Cast UDP\"");
		if (httpPort <= 0) httpPort = 1224;
		var udp = CastHost.Opt?.SendFileUdpPort > 0 ? CastHost.Opt.SendFileUdpPort : 17531;
		runnetsh($"advfirewall firewall add rule name=\"ScreenKit HTTP\" dir=in action=allow protocol=TCP localport={httpPort}");
		runnetsh($"advfirewall firewall add rule name=\"ScreenKit Discover UDP\" dir=in action=allow protocol=UDP localport={udp}");
	}

	static void runnetsh(string args) {
		try {
			var psi = new ProcessStartInfo {
				FileName = "netsh",
				Arguments = args,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			using var p = Process.Start(psi);
			p?.WaitForExit(4000);
		}
		catch { }
	}

	public static string LocalIps() {
		var sb = new StringBuilder();
		foreach (var a in V4Addrs()) {
			if (sb.Length > 0) sb.Append("  ");
			sb.Append(a);
		}
		return sb.Length == 0 ? "(无 IPv4)" : sb.ToString();
	}

	public static List<IPAddress> V4Addrs() {
		var list = new List<IPAddress>();
		try {
			foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()) {
				if (ni.OperationalStatus != OperationalStatus.Up) continue;
				if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
				foreach (var ua in ni.GetIPProperties().UnicastAddresses) {
					if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
					var ip = ua.Address;
					if (IPAddress.IsLoopback(ip)) continue;
					if (!list.Exists(x => x.Equals(ip))) list.Add(ip);
				}
			}
		}
		catch { }
		return list;
	}
}
