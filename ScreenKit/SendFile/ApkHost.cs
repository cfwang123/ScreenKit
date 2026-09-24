using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace ScreenKit;

/// <summary>本机 APK：查找文件、局域网 IPv4、下载 URL。由 SendFile HTTP GET /apk 提供。</summary>
static class ApkHost {
	public const string HttpPath = "/apk";
	static string testFile;

	public static void SetFileForTest(string path) => testFile = path;

	public static string FindFile() {
		if (!string.IsNullOrWhiteSpace(testFile) && File.Exists(testFile))
			return testFile;
		var found = new List<string>();
		foreach (var dir in searchdirs()) {
			try {
				if (!Directory.Exists(dir)) continue;
				foreach (var f in Directory.GetFiles(dir, "*.apk")) {
					try {
						if (isdebug(f)) continue;
						if (new FileInfo(f).Length > 64)
							found.Add(Path.GetFullPath(f));
					}
					catch { }
				}
			}
			catch { }
		}
		if (found.Count == 0) return null;
		return found
			.OrderByDescending(score)
			.ThenByDescending(f => {
				try { return File.GetLastWriteTimeUtc(f); }
				catch { return DateTime.MinValue; }
			})
			.First();
	}

	/// <summary>本地没有 APK 时，尝试从 GitHub Releases 拉到 tmp/apk/，再由本机 HTTP 提供。</summary>
	public static async Task<string> EnsureAsync(CancellationToken ct = default) {
		var hit = FindFile();
		if (!string.IsNullOrEmpty(hit)) return hit;
		var info = await AppUpdater.CheckLatestApkAsync(ct).ConfigureAwait(false);
		if (info == null || !info.HasApk || string.IsNullOrWhiteSpace(info.DownloadUrl))
			return null;
		var name = info.AssetName;
		if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
			name = "screenkit.apk";
		foreach (var c in Path.GetInvalidFileNameChars())
			name = name.Replace(c, '_');
		var dest = Path.Combine(TmpStore.Root, "apk", name);
		if (File.Exists(dest) && new FileInfo(dest).Length > 64)
			return dest;
		var urls = FeatureInstaller.ExpandUrls(info.DownloadUrl);
		await FeatureInstaller.DownloadUrlAsync(urls, dest, null, null, ct, info.SizeBytes)
			.ConfigureAwait(false);
		return File.Exists(dest) && new FileInfo(dest).Length > 64 ? dest : null;
	}

	public static string VersionOf(string path) {
		var n = Path.GetFileNameWithoutExtension(path ?? "") ?? "";
		var m = Regex.Match(n, @"\d+\.\d+(?:\.\d+)?");
		if (m.Success) return m.Value;
		if (n.IndexOf("debug", StringComparison.OrdinalIgnoreCase) >= 0) return "debug";
		if (n.IndexOf("release", StringComparison.OrdinalIgnoreCase) >= 0) return "release";
		return n.Length > 0 ? n : "—";
	}

	/// <summary>本机局域网 IPv4，默认把能出网的物理网卡排在前面（供安装二维码选用）。</summary>
	public static List<string> LanIPv4s() {
		var list = new List<string>();
		var score = new Dictionary<string, int>(StringComparer.Ordinal);
		var wan = outboundipv4();
		try {
			foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()) {
				try {
					if (ni.OperationalStatus != OperationalStatus.Up) continue;
					if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
					var virt = isvirt(ni);
					var gw = hasgw(ni);
					foreach (var ua in ni.GetIPProperties().UnicastAddresses) {
						var ip = ua.Address;
						if (ip == null || ip.AddressFamily != AddressFamily.InterNetwork) continue;
						if (IPAddress.IsLoopback(ip)) continue;
						var b = ip.GetAddressBytes();
						if (b.Length == 4 && b[0] == 169 && b[1] == 254) continue;
						var s = ip.ToString();
						if (list.Contains(s)) continue;
						list.Add(s);
						score[s] = ipscore(s, wan, gw, virt);
					}
				}
				catch { }
			}
		}
		catch { }
		list.Sort((a, b) => score[a].CompareTo(score[b]));
		return list;
	}

	public static string Url(string ip, int port) {
		port = port <= 0 ? 1224 : port;
		return $"http://{ip}:{port}{HttpPath}";
	}

	static bool isdebug(string path) {
		var s = (path ?? "").Replace('\\', '/').ToLowerInvariant();
		var n = Path.GetFileName(s) ?? "";
		return n.IndexOf("debug", StringComparison.Ordinal) >= 0
			|| s.IndexOf("/debug/", StringComparison.Ordinal) >= 0;
	}

	static int score(string path) {
		var n = Path.GetFileName(path).ToLowerInvariant();
		var d = (Path.GetDirectoryName(path) ?? "").Replace('\\', '/').ToLowerInvariant();
		var s = 0;
		if (n.StartsWith("screenkit")) s += 100;
		if (d.EndsWith("/apk")) s += 50;
		if (d.IndexOf("/release", StringComparison.Ordinal) >= 0) s += 10;
		return s;
	}

	static string outboundipv4() {
		try {
			using var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			sock.Connect(IPAddress.Parse("8.8.8.8"), 53);
			if (sock.LocalEndPoint is IPEndPoint ep) return ep.Address.ToString();
		}
		catch { }
		return null;
	}

	static bool hasgw(NetworkInterface ni) {
		try {
			foreach (var g in ni.GetIPProperties().GatewayAddresses) {
				var a = g.Address;
				if (a == null || a.AddressFamily != AddressFamily.InterNetwork) continue;
				if (IPAddress.IsLoopback(a) || a.Equals(IPAddress.Any)) continue;
				var b = a.GetAddressBytes();
				if (b.Length == 4 && b[0] == 169 && b[1] == 254) continue;
				return true;
			}
		}
		catch { }
		return false;
	}

	static bool isvirt(NetworkInterface ni) {
		var t = ni.NetworkInterfaceType;
		if (t == NetworkInterfaceType.Tunnel || t == NetworkInterfaceType.Ppp)
			return true;
		var n = ((ni.Name ?? "") + " " + (ni.Description ?? "")).ToLowerInvariant();
		string[] keys = {
			"vmware", "virtualbox", "vbox", "hyper-v", "hyperv", "vethernet",
			"wsl", "docker", "wintun", "wireguard", "openvpn", "nordlynx",
			"clash", "tun2socks", "bluetooth", "isatap", "teredo", "npcap",
			"loopback", "virtual",
		};
		foreach (var k in keys)
			if (n.IndexOf(k, StringComparison.Ordinal) >= 0) return true;
		return false;
	}

	static int ipscore(string ip, string wan, bool gw, bool virt) {
		var s = 100;
		if (!virt && !string.IsNullOrEmpty(wan) && ip == wan) s -= 50;
		else if (!virt && gw) s -= 40;
		else if (!virt) s -= 20;
		else if (gw) s -= 5;
		if (ip.StartsWith("192.168.", StringComparison.Ordinal)) s += 0;
		else if (ip.StartsWith("10.", StringComparison.Ordinal)) s += 1;
		else if (priv172(ip)) s += 2;
		else s += 3;
		return s;
	}

	static bool priv172(string ip) {
		if (!ip.StartsWith("172.", StringComparison.Ordinal)) return false;
		var i = ip.IndexOf('.', 4);
		if (i < 0) return false;
		if (!int.TryParse(ip.Substring(4, i - 4), out var n)) return false;
		return n >= 16 && n <= 31;
	}

	static IEnumerable<string> searchdirs() {
		var baseDir = AppDomain.CurrentDomain.BaseDirectory;
		yield return Path.Combine(baseDir, "apk");
		yield return baseDir;
		yield return Path.Combine(TmpStore.Root, "apk");
		var dir = baseDir;
		for (var i = 0; i < 6; i++) {
			try { dir = Path.GetFullPath(Path.Combine(dir, "..")); }
			catch { break; }
			yield return Path.Combine(dir, "android", "release");
			yield return Path.Combine(dir, "android", "app", "build", "outputs", "apk", "release");
		}
	}
}
