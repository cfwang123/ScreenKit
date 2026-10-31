using System;
using System.IO;
using System.Net;
using System.Text;

namespace ScreenKit.Soap;

/// <summary>统一网络取数：可信任全部证书、可带凭据，本机地址不走代理。</summary>
static class Net {
	public const int TIMEOUT = 30000;

	public static bool TrustAllCerts = true;

	public static void init() {
		try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
	}

	public static bool isLocal(Uri uri) {
		if (uri == null) return false;
		string host = uri.Host;
		return uri.IsLoopback || host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
			host == "127.0.0.1" || host == "::1";
	}

	public static WebRequest create(string url) {
		var req = (HttpWebRequest)WebRequest.Create(url);
		req.Timeout = TIMEOUT;
		req.ReadWriteTimeout = TIMEOUT;
		req.UserAgent = "ScreenKit/1.0";
		req.AllowAutoRedirect = true;
		req.Proxy = ScreenKit.HttpProxy.Instance;
		if (TrustAllCerts) req.ServerCertificateValidationCallback = (_, _, _, _) => true;
		return req;
	}

	public static byte[] fetch(string url) {
		var req = (HttpWebRequest)create(url);
		using var resp = (HttpWebResponse)req.GetResponse();
		using var s = resp.GetResponseStream();
		using var ms = new MemoryStream();
		if (s != null) s.CopyTo(ms);
		return ms.ToArray();
	}

	public static string decode(byte[] data) {
		if (data == null || data.Length == 0) return "";
		int skip = 0;
		Encoding enc = null;
		if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) {
			enc = new UTF8Encoding(false);
			skip = 3;
		} else if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE) {
			enc = Encoding.Unicode;
			skip = 2;
		} else if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF) {
			enc = Encoding.BigEndianUnicode;
			skip = 2;
		}
		if (enc == null) {
			string head = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 400));
			int p = head.IndexOf("encoding=", StringComparison.OrdinalIgnoreCase);
			if (p >= 0) {
				int q = head.IndexOfAny(new[] { '"', '\'' }, p);
				if (q > 0) {
					int r = head.IndexOfAny(new[] { '"', '\'' }, q + 1);
					if (r > q) {
						string name = head.Substring(q + 1, r - q - 1).Trim();
						try { enc = Encoding.GetEncoding(name); } catch { enc = null; }
					}
				}
			}
			if (enc == null) enc = new UTF8Encoding(false);
		}
		return enc.GetString(data, skip, data.Length - skip);
	}

	public static Uri toUri(string location) {
		if (string.IsNullOrWhiteSpace(location)) return null;
		if (location.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
			location.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
			location.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
			return new Uri(location);
		return new Uri(Path.GetFullPath(location));
	}

	public static bool isRemote(string location) =>
		!string.IsNullOrEmpty(location) &&
		(location.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
		 location.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

	/// <summary>file:// URI 与本地路径互相兼容的取数入口。</summary>
	public static byte[] readBytes(string location) {
		if (isRemote(location)) return fetch(location);
		return File.ReadAllBytes(toLocalPath(location));
	}

	public static string readText(string location) => decode(readBytes(location));

	public static string toLocalPath(string location) {
		if (string.IsNullOrEmpty(location)) return location;
		if (location.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
			return new Uri(location).LocalPath;
		return location;
	}

	/// <summary>把任意本地/远端地址规整成绝对 URI 字符串。</summary>
	public static string absolutize(string baseLocation, string location) {
		try {
			var b = toUri(baseLocation);
			if (string.IsNullOrEmpty(location)) return b?.AbsoluteUri ?? baseLocation;
			if (b == null) return toUri(location)?.AbsoluteUri ?? location;
			return new Uri(b, location).AbsoluteUri;
		} catch {
			try { return new Uri(location).AbsoluteUri; } catch { return location; }
		}
	}
}
