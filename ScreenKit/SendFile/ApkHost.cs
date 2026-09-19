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

	public static List<string> LanIPv4s() {
		var list = new List<string>();
		try {
			foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()) {
				try {
					if (ni.OperationalStatus != OperationalStatus.Up) continue;
					if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
					foreach (var ua in ni.GetIPProperties().UnicastAddresses) {
						var ip = ua.Address;
						if (ip == null || ip.AddressFamily != AddressFamily.InterNetwork) continue;
						if (IPAddress.IsLoopback(ip)) continue;
						var b = ip.GetAddressBytes();
						if (b.Length == 4 && b[0] == 169 && b[1] == 254) continue;
						var s = ip.ToString();
						if (!list.Contains(s)) list.Add(s);
					}
				}
				catch { }
			}
		}
		catch { }
		list.Sort((a, b) => ipscore(a).CompareTo(ipscore(b)));
		return list;
	}

	public static string Url(string ip, int port) {
		port = port <= 0 ? 17532 : port;
		return $"http://{ip}:{port}{HttpPath}";
	}

	static int score(string path) {
		var n = Path.GetFileName(path).ToLowerInvariant();
		var d = (Path.GetDirectoryName(path) ?? "").Replace('\\', '/').ToLowerInvariant();
		var s = 0;
		if (n.StartsWith("screenkit")) s += 100;
		if (d.EndsWith("/apk")) s += 50;
		if (d.IndexOf("/release", StringComparison.Ordinal) >= 0) s += 10;
		if (n.IndexOf("debug", StringComparison.Ordinal) >= 0) s -= 40;
		return s;
	}

	static int ipscore(string ip) {
		if (ip.StartsWith("192.168.", StringComparison.Ordinal)) return 0;
		if (ip.StartsWith("10.", StringComparison.Ordinal)) return 1;
		if (ip.StartsWith("172.", StringComparison.Ordinal)) return 2;
		return 3;
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
			yield return Path.Combine(dir, "android", "app", "build", "outputs", "apk", "debug");
		}
	}
}
