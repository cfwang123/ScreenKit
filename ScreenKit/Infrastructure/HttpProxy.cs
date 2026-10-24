using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Win32;

namespace ScreenKit;

/// <summary>
/// 出站 HTTP 代理。system 跟随系统；manual 只用手动地址；off 不用代理。
/// 回环 / 内网 / .cn / 国内镜像直连。
/// </summary>
sealed class HttpProxy : IWebProxy {
	public static readonly HttpProxy Instance = new();

	public static bool Enabled;
	public static string Addr = "127.0.0.1:7897";
	/// <summary>system / manual / off。</summary>
	public static string Mode = "system";

	const string CHROMEBYPASS = "<-loopback>;<local>;*.cn;10.0.0.0/8;172.16.0.0/12;192.168.0.0/16;169.254.0.0/16;"
		+ "hf-mirror.com;*.hf-mirror.com;ghfast.top;*.ghfast.top;ghproxy.net;*.ghproxy.net;"
		+ "ghproxy.com;*.ghproxy.com;ghproxy.org;*.ghproxy.org;mirror.ghproxy.com;ghps.cc;*.ghps.cc";

	HttpProxy() {
		Credentials = CredentialCache.DefaultCredentials;
	}

	public ICredentials Credentials { get; set; }

	public static string NormalizeMode(string mode) {
		mode = (mode ?? "").Trim().Trim('"').ToLowerInvariant();
		if (mode is "manual" or "on" or "addr") return "manual";
		if (mode is "off" or "none" or "direct") return "off";
		return "system";
	}

	/// <summary>当前会走代理（手动，或跟随系统且系统代理开着）。安装顺序据此决定先 GitHub 还是先国内镜像。</summary>
	public static bool ProxyOn() {
		if (Mode == "off") return false;
		if (Mode == "manual") return true;
		return systemuri() != null;
	}

	public static void ApplyFrom(OcrOptions o) {
		if (o == null) {
			Enabled = false;
			Mode = "off";
			return;
		}
		Addr = string.IsNullOrWhiteSpace(o.HttpProxyAddr) ? "127.0.0.1:7897" : o.HttpProxyAddr.Trim();
		Mode = string.IsNullOrWhiteSpace(o.HttpProxyMode)
			? (o.HttpProxyEnabled ? "manual" : "system")
			: NormalizeMode(o.HttpProxyMode);
		Enabled = Mode == "manual";
	}

	public static HttpClientHandler CreateHandler() =>
		new() { UseProxy = true, Proxy = Instance };

	/// <summary>当前代理写成 Chrome 启动参数。手动代理不看系统开关。关掉或系统代理未开时是 --no-proxy-server。</summary>
	public static string ChromeArgs() {
		var sb = new StringBuilder();
		AppendChrome(sb, Mode, Addr);
		return sb.ToString();
	}

	/// <summary>按程序的代理设置追加 Chrome 参数。回环、内网、.cn 和国内镜像不走代理。</summary>
	public static void AppendChrome(StringBuilder args, string mode, string addr) {
		if (args == null) return;
		var server = chromeserver(NormalizeMode(mode), addr);
		if (server == null) {
			args.Append("--no-proxy-server ");
			return;
		}
		args.Append("--proxy-server=\"").Append(server).Append("\" ");
		args.Append("--proxy-bypass-list=\"").Append(CHROMEBYPASS).Append("\" ");
		args.Append("--disable-quic ");
	}

	static string chromeserver(string mode, string addr) {
		if (mode == "off") return null;
		var u = mode == "manual" ? parseaddr(addr) : systemuri();
		if (u == null || string.IsNullOrEmpty(u.Host)) return null;
		var host = u.Host;
		if (host.IndexOf(':') >= 0 && !host.StartsWith("[", StringComparison.Ordinal))
			host = $"[{host}]";
		var port = u.IsDefaultPort ? "" : $":{u.Port}";
		if (u.Scheme.StartsWith("socks", StringComparison.OrdinalIgnoreCase))
			return $"{u.Scheme}://{host}{port}";
		return $"{host}{port}";
	}

	static Uri parseaddr(string raw) {
		raw = (raw ?? "").Trim().Trim('"');
		if (raw.Length == 0) return null;
		if (raw.IndexOf("://", StringComparison.Ordinal) < 0)
			raw = "http://" + raw;
		return Uri.TryCreate(raw, UriKind.Absolute, out var u) ? u : null;
	}

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

	/// <summary>off 直连；manual 只用地址；system 只用 Windows 系统代理。</summary>
	static Uri active() {
		if (Mode == "off") return null;
		if (Mode == "manual") return parse();
		return systemuri();
	}

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

	static Uri parse() => parseaddr(Addr);

	static bool iscnmirror(string host) {
		host = (host ?? "").ToLowerInvariant();
		return host == "hf-mirror.com" || host.EndsWith(".hf-mirror.com")
			|| host == "ghfast.top" || host.EndsWith(".ghfast.top")
			|| host == "ghproxy.net" || host.EndsWith(".ghproxy.net")
			|| host == "ghproxy.com" || host.EndsWith(".ghproxy.com")
			|| host == "ghproxy.org" || host.EndsWith(".ghproxy.org")
			|| host == "mirror.ghproxy.com"
			|| host == "ghps.cc" || host.EndsWith(".ghps.cc");
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
