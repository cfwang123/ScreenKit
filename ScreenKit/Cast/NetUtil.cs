using System.Net.NetworkInformation;
using System.Text;

namespace ScreenKit;

static class CastNetUtil {
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
