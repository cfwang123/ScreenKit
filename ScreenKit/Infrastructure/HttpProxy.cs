using System.Net;
using System.Net.Http;
using Microsoft.Win32;

namespace ScreenKit;

/// <summary>
/// 出站 HTTP 代理。Windows 系统代理开着时总是用它；否则用参数里的地址。
/// 回环 / 内网 / .cn / 国内镜像直连。
/// </summary>
sealed class HttpProxy : IWebProxy {
	public static readonly HttpProxy Instance = new();

	public static bool Enabled;
	public static string Addr = "127.0.0.1:7897";

	HttpProxy() {
		Credentials = CredentialCache.DefaultCredentials;
	}

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

	public Uri GetProxy(Uri destination) => active() ?? destination;

	public static bool Need(string url) {
		if (string.IsNullOrWhiteSpace(url)) return false;
		return Uri.TryCreate(url, UriKind.Absolute, out var u) && Need(u);
	}

	public static bool Need(Uri u) {
		if (active() == null || u == null) return false;
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

	/// <summary>系统代理优先。没有系统代理时才用手动地址，且要勾选启用。</summary>
	static Uri active() => systemuri() ?? (Enabled ? parse() : null);

	static Uri systemuri() {
		string server = null;
		try {
			using var key = Registry.CurrentUser.OpenSubKey(
				@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
			if (key == null) return null;
			if (!proxyon(key.GetValue("ProxyEnable"))) return null;
			server = key.GetValue("ProxyServer") as string;
		}
		catch { return null; }
		return parseserver(server);
	}

	static bool proxyon(object value) {
		if (value is int i) return i != 0;
		if (value is string s)
			return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
		return false;
	}

	/// <summary>ProxyServer 可能是 host:port，或 http=…;https=…。只用 http/https，不用 socks。</summary>
	static Uri parseserver(string server) {
		server = (server ?? "").Trim();
		if (server.Length == 0) return null;
		string pick = null;
		if (server.IndexOf('=') >= 0) {
			string http = null, https = null;
			foreach (var part in server.Split(';')) {
				var eq = part.IndexOf('=');
				if (eq <= 0) continue;
				var k = part.Substring(0, eq).Trim();
				var v = part.Substring(eq + 1).Trim();
				if (v.Length == 0) continue;
				if (k.Equals("https", StringComparison.OrdinalIgnoreCase)) https = v;
				else if (k.Equals("http", StringComparison.OrdinalIgnoreCase)) http = v;
			}
			pick = https ?? http;
		}
		else pick = server;
		if (string.IsNullOrWhiteSpace(pick)) return null;
		if (pick.IndexOf("://", StringComparison.Ordinal) < 0)
			pick = "http://" + pick;
		return Uri.TryCreate(pick, UriKind.Absolute, out var u) ? u : null;
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
