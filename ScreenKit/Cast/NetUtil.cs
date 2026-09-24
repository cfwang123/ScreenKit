using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text;

namespace ScreenKit;

static class CastNetUtil {
	public static void TryFirewall() {
		runnetsh("advfirewall firewall delete rule name=\"ScreenKit Cast TCP\"");
		runnetsh("advfirewall firewall delete rule name=\"ScreenKit Cast UDP\"");
		runnetsh($"advfirewall firewall add rule name=\"ScreenKit Cast TCP\" dir=in action=allow protocol=TCP localport={CastProto.TCP_PORT}");
		runnetsh($"advfirewall firewall add rule name=\"ScreenKit Cast UDP\" dir=in action=allow protocol=UDP localport={CastProto.UDP_PORT}");
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
		try {
			foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()) {
				if (ni.OperationalStatus != OperationalStatus.Up) continue;
				if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
				foreach (var ua in ni.GetIPProperties().UnicastAddresses) {
					if (ua.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
					var ip = ua.Address.ToString();
					if (ip.StartsWith("127.")) continue;
					if (sb.Length > 0) sb.Append("  ");
					sb.Append(ip);
				}
			}
		}
		catch { }
		return sb.Length == 0 ? "(无 IPv4)" : sb.ToString();
	}
}
