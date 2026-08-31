using System.Net;
using System.Net.Http;

namespace ScreenKit;

/// <summary>
/// 出站 HTTP 代理：访问 GitHub 等非中国站点时使用；回环 / 内网 / .cn / 国内镜像直连。
/// </summary>
sealed class HttpProxy : IWebProxy {
	public static readonly HttpProxy Instance = new();

	public static bool Enabled;
	public static string Addr = "127.0.0.1:7897";

	HttpProxy() { }

	public ICredentials Credentials { get; set; }

	public static void ApplyFrom(OcrOptions o) {
		if (o == null) {
			Enabled = false;
			return;
		}
		Enabled = o.HttpProxyEnabled;
		Addr = string.IsNullOrWhiteSpace(o.HttpProxyAddr) ? "127.0.0.1:7897" : o.HttpProxyAddr.Trim();
	}

	public static HttpClientHandler CreateHandler() =>
		new() { UseProxy = true, Proxy = Instance };

	public bool IsBypassed(Uri host) => !Need(host);

	public Uri GetProxy(Uri destination) => parse() ?? destination;

	public static bool Need(string url) {
		if (string.IsNullOrWhiteSpace(url)) return false;
		return Uri.TryCreate(url, UriKind.Absolute, out var u) && Need(u);
	}

	public static bool Need(Uri u) {
		if (!Enabled || parse() == null || u == null) return false;
		var host = u.Host ?? "";
		if (host.Length == 0) return false;
		if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
			|| host == "127.0.0.1" || host == "::1" || host == "[::1]")
			return false;
		if (IPAddress.TryParse(host, out var ip) && isprivate(ip))
			return false;
		if (host.EndsWith(".cn", StringComparison.OrdinalIgnoreCase))
			return false;
		return !iscnmirror(host);
	}

	static Uri parse() {
		var raw = (Addr ?? "").Trim();
		if (raw.Length == 0) return null;
		if (raw.IndexOf("://", StringComparison.Ordinal) < 0)
			raw = "http://" + raw;
		return Uri.TryCreate(raw, UriKind.Absolute, out var u) ? u : null;
	}

	static bool iscnmirror(string host) {
		host = (host ?? "").ToLowerInvariant();
		return host == "hf-mirror.com" || host.EndsWith(".hf-mirror.com")
			|| host == "ghfast.top" || host.EndsWith(".ghfast.top")
			|| host == "ghproxy.net" || host.EndsWith(".ghproxy.net")
			|| host == "ghproxy.com" || host.EndsWith(".ghproxy.com")
			|| host == "ghproxy.org" || host.EndsWith(".ghproxy.org")
			|| host == "mirror.ghproxy.com";
	}

	static bool isprivate(IPAddress ip) {
		if (IPAddress.IsLoopback(ip)) return true;
		var b = ip.GetAddressBytes();
		if (b.Length == 4) {
			if (b[0] == 10) return true;
			if (b[0] == 192 && b[1] == 168) return true;
			if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
		}
		return false;
	}
}
