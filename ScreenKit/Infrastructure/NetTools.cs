using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenKit;

/// <summary>Ping / DNS / WHOIS（TCP 43，不走 HTTP 代理）。</summary>
static class NetTools {
	static readonly Regex WhoisRefer = new(
		@"^(?:refer|whois)\s*:\s*(\S+)",
		RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

	public static string AsciiHost(string host) {
		host = (host ?? "").Trim();
		if (host.Length == 0) throw new ArgumentException("empty");
		if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
			|| host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			host = new Uri(host).Host;
		var slash = host.IndexOf('/');
		if (slash >= 0) host = host.Substring(0, slash);
		if (IPAddress.TryParse(host, out _)) return host;
		try {
			return new IdnMapping().GetAscii(host.TrimEnd('.'));
		}
		catch {
			return host.TrimEnd('.');
		}
	}

	public static async Task<string> Resolve(string host, CancellationToken ct) {
		host = AsciiHost(host);
		ct.ThrowIfCancellationRequested();
		var sb = new StringBuilder();
		sb.AppendLine("host: " + host);
		if (IPAddress.TryParse(host, out var ip)) {
			sb.AppendLine(ip.AddressFamily + "  " + ip);
			return sb.ToString().TrimEnd();
		}
		var addrs = await withtimeout(Dns.GetHostAddressesAsync(host), 8000, ct).ConfigureAwait(false);
		if (addrs == null || addrs.Length == 0)
			throw new InvalidOperationException("no address");
		foreach (var a in addrs)
			sb.AppendLine(a.AddressFamily + "  " + a);
		return sb.ToString().TrimEnd();
	}

	public static async Task<string> Ping(string host, int count, int timeoutMs,
		Action<string> line, CancellationToken ct) {
		host = AsciiHost(host);
		count = Compat.Clamp(count <= 0 ? 4 : count, 1, 20);
		timeoutMs = Compat.Clamp(timeoutMs <= 0 ? 3000 : timeoutMs, 200, 15000);
		IPAddress addr;
		if (!IPAddress.TryParse(host, out addr)) {
			var addrs = await withtimeout(Dns.GetHostAddressesAsync(host), 8000, ct).ConfigureAwait(false);
			addr = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
				?? addrs.FirstOrDefault()
				?? throw new InvalidOperationException("no address");
		}
		line?.Invoke($"PING {host} ({addr})");
		var times = new List<long>();
		var recv = 0;
		using var ping = new Ping();
		for (var i = 0; i < count; i++) {
			ct.ThrowIfCancellationRequested();
			PingReply reply;
			try {
				reply = await ping.SendPingAsync(addr, timeoutMs).ConfigureAwait(false);
			}
			catch (PingException ex) {
				line?.Invoke($"icmp error: {ex.InnerException?.Message ?? ex.Message}");
				continue;
			}
			if (reply.Status == IPStatus.Success) {
				recv++;
				var ms = reply.RoundtripTime;
				times.Add(ms);
				line?.Invoke($"reply from {reply.Address}: bytes={reply.Buffer?.Length ?? 0} time={ms}ms TTL={reply.Options?.Ttl}");
			}
			else
				line?.Invoke($"timeout ({reply.Status})");
			if (i + 1 < count)
				await Task.Delay(400, ct).ConfigureAwait(false);
		}
		var lost = count - recv;
		var sb = new StringBuilder();
		sb.AppendLine($"sent={count} recv={recv} lost={lost} ({lost * 100 / Math.Max(1, count)}%)");
		if (times.Count > 0)
			sb.AppendLine($"rtt min={times.Min()}ms avg={times.Average():0}ms max={times.Max()}ms");
		var sum = sb.ToString().TrimEnd();
		line?.Invoke(sum);
		return sum;
	}

	public static async Task Trace(string host, int maxHops, Action<string> line, CancellationToken ct) {
		host = AsciiHost(host);
		maxHops = Compat.Clamp(maxHops <= 0 ? 30 : maxHops, 1, 30);
		IPAddress addr;
		if (!IPAddress.TryParse(host, out addr)) {
			var addrs = await withtimeout(Dns.GetHostAddressesAsync(host), 8000, ct).ConfigureAwait(false);
			addr = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
				?? addrs.FirstOrDefault()
				?? throw new InvalidOperationException("no address");
		}
		line?.Invoke($"TRACERT {host} ({addr}) hops≤{maxHops}");
		var payload = new byte[32];
		using var ping = new Ping();
		for (var ttl = 1; ttl <= maxHops; ttl++) {
			ct.ThrowIfCancellationRequested();
			var opt = new PingOptions { Ttl = ttl };
			IPAddress hop = null;
			var times = new List<long>();
			var reached = false;
			for (var p = 0; p < 3; p++) {
				ct.ThrowIfCancellationRequested();
				try {
					var r = await ping.SendPingAsync(addr, 2500, payload, opt).ConfigureAwait(false);
					if (r.Status == IPStatus.Success
						|| r.Status == IPStatus.TtlExpired
						|| r.Status == IPStatus.TimeExceeded) {
						if (r.Address != null) hop = r.Address;
						times.Add(r.RoundtripTime);
						if (r.Status == IPStatus.Success) reached = true;
					}
				}
				catch (PingException) { }
			}
			var rtt = times.Count > 0 ? times.Min() + "ms" : "*";
			var hopStr = hop != null ? hop.ToString() : "*";
			line?.Invoke($"{ttl,2}  {rtt,7}  {hopStr}");
			if (reached) {
				line?.Invoke("reached");
				return;
			}
		}
		line?.Invoke("max hops");
	}

	static readonly (string Name, string Ip, string Place)[] LocateNodes = {
		("AliDNS", "223.5.5.5", "杭州"),
		("DNSPod", "119.29.29.29", "深圳"),
		("百度DNS", "180.76.76.76", "北京"),
		("114DNS", "114.114.114.114", "南京"),
		("HiNet", "168.95.1.1", "台湾"),
		("Google", "8.8.8.8", "Google Anycast"),
		("Cloudflare", "1.1.1.1", "Cloudflare Anycast"),
		("Quad9", "9.9.9.9", "Quad9"),
		("OpenDNS", "208.67.222.222", "OpenDNS"),
		("a.root", "198.41.0.4", "美国"),
		("k.root", "193.0.14.129", "欧洲"),
		("m.root", "202.12.27.33", "亚太"),
	};

	/// <summary>对本机到全球若干节点测 RTT，用最近节点粗略估计所在地区。</summary>
	public static async Task Locate(Action<string> line, CancellationToken ct) {
		line?.Invoke("Ping 全球节点（本机出口大致位置）");
		var rows = new List<(string Name, string Place, long? Ms)>();
		using var ping = new Ping();
		var payload = new byte[32];
		foreach (var n in LocateNodes) {
			ct.ThrowIfCancellationRequested();
			if (!IPAddress.TryParse(n.Ip, out var ip)) continue;
			long? best = null;
			for (var i = 0; i < 2; i++) {
				ct.ThrowIfCancellationRequested();
				try {
					var r = await ping.SendPingAsync(ip, 2000, payload).ConfigureAwait(false);
					if (r.Status == IPStatus.Success) {
						var ms = r.RoundtripTime;
						if (best == null || ms < best.Value) best = ms;
					}
				}
				catch (PingException) { }
			}
			rows.Add((n.Name, n.Place, best));
			var rtt = best != null ? best.Value + "ms" : "timeout";
			line?.Invoke($"{n.Place,-10} {n.Name,-12} {n.Ip,-16} {rtt}");
		}
		var ok = rows.Where(r => r.Ms != null).OrderBy(r => r.Ms.Value).ToList();
		if (ok.Count == 0) {
			line?.Invoke("全部超时，无法估计位置");
			return;
		}
		line?.Invoke("推测位置：" + guessplace(ok.Select(r => (r.Place, r.Ms)).ToList()));
	}

	static readonly (string Name, string Url, string Place)[] HttpSpeedNodes = {
		("百度", "https://www.baidu.com/favicon.ico", "北京"),
		("淘宝", "https://www.taobao.com/favicon.ico", "杭州"),
		("腾讯", "https://www.qq.com/favicon.ico", "深圳"),
		("Cloudflare", "https://www.cloudflare.com/cdn-cgi/trace", "Cloudflare"),
		("Google", "https://www.gstatic.com/generate_204", "Google"),
		("微软", "https://www.msftconnecttest.com/connecttest.txt", "微软"),
		("GitHub", "https://github.com/favicon.ico", "美国"),
		("Apple", "https://www.apple.com/library/test/success.html", "美国"),
	};

	/// <summary>经系统代理 HTTP GET 若干站点，用最短耗时粗略估计出口位置。</summary>
	public static async Task HttpSpeedLocate(Action<string> line, CancellationToken ct) {
		line?.Invoke("HTTP 测速定位（系统代理）");
		IWebProxy sys = null;
		try {
			sys = WebRequest.GetSystemWebProxy();
			if (sys != null)
				sys.Credentials = CredentialCache.DefaultCredentials;
			var probe = new Uri("https://www.google.com/");
			var px = sys?.GetProxy(probe);
			if (px != null && px != probe && !string.Equals(px.Host, probe.Host, StringComparison.OrdinalIgnoreCase))
				line?.Invoke("系统代理: " + px);
			else
				line?.Invoke("系统代理: 无（直连）");
		}
		catch (Exception ex) {
			line?.Invoke("系统代理: " + ex.Message);
		}
		var handler = new HttpClientHandler {
			UseProxy = sys != null,
			Proxy = sys,
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
		};
		var rows = new List<(string Place, long? Ms)>();
		using (handler)
		using (var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) }) {
			http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ScreenKit");
			foreach (var n in HttpSpeedNodes) {
				ct.ThrowIfCancellationRequested();
				long? best = null;
				string extra = "";
				for (var i = 0; i < 2; i++) {
					ct.ThrowIfCancellationRequested();
					var sw = System.Diagnostics.Stopwatch.StartNew();
					try {
						using var resp = await http.GetAsync(n.Url, ct).ConfigureAwait(false);
						var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
						sw.Stop();
						var ms = sw.ElapsedMilliseconds;
						if (best == null || ms < best.Value) best = ms;
						if (n.Url.IndexOf("cdn-cgi/trace", StringComparison.OrdinalIgnoreCase) >= 0) {
							var t = Encoding.UTF8.GetString(bytes ?? Array.Empty<byte>());
							extra = parsecftrace(t);
						}
					}
					catch (Exception ex) {
						sw.Stop();
						if (i == 1 && best == null)
							extra = ex.InnerException?.Message ?? ex.Message;
					}
				}
				rows.Add((n.Place, best));
				var rtt = best != null ? best.Value + "ms" : "timeout";
				var tail = extra.Length > 0 ? "  " + extra : "";
				line?.Invoke($"{n.Place,-12} {n.Name,-12} {rtt}{tail}");
			}
		}
		var ok = rows.Where(r => r.Ms != null).OrderBy(r => r.Ms.Value).ToList();
		if (ok.Count == 0) {
			line?.Invoke("全部超时，无法估计位置");
			return;
		}
		line?.Invoke("推测位置：" + guessplace(ok));
	}

	static string parsecftrace(string t) {
		if (string.IsNullOrEmpty(t)) return "";
		string colo = "", loc = "";
		foreach (var raw in t.Replace("\r", "").Split('\n')) {
			var i = raw.IndexOf('=');
			if (i <= 0) continue;
			var k = raw.Substring(0, i);
			var v = raw.Substring(i + 1).Trim();
			if (k == "colo") colo = v;
			else if (k == "loc") loc = v;
		}
		if (colo.Length == 0 && loc.Length == 0) return "";
		return "colo=" + colo + (loc.Length > 0 ? " loc=" + loc : "");
	}

	static string guessplace(List<(string Place, long? Ms)> ok) {
		if (ok == null || ok.Count == 0) return "";
		var nearest = ok[0];
		var cn = ok.Where(r => r.Place == "杭州" || r.Place == "深圳"
			|| r.Place == "北京" || r.Place == "南京").ToList();
		if (cn.Count > 0 && cn[0].Ms <= 80)
			return "中国大陆，接近 " + cn[0].Place;
		if (nearest.Place == "台湾" && nearest.Ms <= 60)
			return "台湾附近";
		if (nearest.Ms <= 40)
			return "接近 " + nearest.Place + " 节点";
		return "较近：" + nearest.Place + "（" + nearest.Ms + "ms）";
	}

	/// <summary>经 Windows 系统代理用 HTTP 查出口 IP 与地区。</summary>
	public static async Task HttpLocate(Action<string> line, CancellationToken ct) {
		line?.Invoke("HTTP 定位（系统代理）");
		IWebProxy sys = null;
		try {
			sys = WebRequest.GetSystemWebProxy();
			if (sys != null)
				sys.Credentials = CredentialCache.DefaultCredentials;
			var probe = new Uri("https://www.google.com/");
			var px = sys?.GetProxy(probe);
			if (px != null && px != probe && !string.Equals(px.Host, probe.Host, StringComparison.OrdinalIgnoreCase))
				line?.Invoke("系统代理: " + px);
			else
				line?.Invoke("系统代理: 无（直连）");
		}
		catch (Exception ex) {
			line?.Invoke("系统代理: " + ex.Message);
		}
		var urls = new[] {
			"http://ip-api.com/json/?fields=status,message,country,regionName,city,isp,query&lang=zh-CN",
			"https://ipinfo.io/json",
			"https://api.ip.sb/geoip",
		};
		foreach (var url in urls) {
			ct.ThrowIfCancellationRequested();
			try {
				var body = await httpget(url, sys, ct).ConfigureAwait(false);
				line?.Invoke(url);
				line?.Invoke(formatgeo(body));
				return;
			}
			catch (Exception ex) {
				line?.Invoke(url + "  " + (ex.InnerException?.Message ?? ex.Message));
			}
		}
		line?.Invoke("HTTP 定位失败");
	}

	static async Task<string> httpget(string url, IWebProxy proxy, CancellationToken ct) {
		var handler = new HttpClientHandler {
			UseProxy = proxy != null,
			Proxy = proxy,
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
		};
		using (handler)
		using (var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) }) {
			http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ScreenKit");
			using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
			resp.EnsureSuccessStatusCode();
			return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
		}
	}

	static string formatgeo(string json) {
		json = (json ?? "").Trim();
		if (json.Length == 0) return "(empty)";
		try {
			using var doc = JsonDocument.Parse(json);
			var r = doc.RootElement;
			string get(params string[] keys) {
				foreach (var k in keys) {
					if (!r.TryGetProperty(k, out var v)) continue;
					if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? "";
					if (v.ValueKind == JsonValueKind.Number || v.ValueKind == JsonValueKind.True
						|| v.ValueKind == JsonValueKind.False)
						return v.ToString();
				}
				return "";
			}
			if (string.Equals(get("status"), "fail", StringComparison.OrdinalIgnoreCase))
				return "fail: " + get("message");
			var ip = get("query", "ip");
			var country = get("country", "country_name");
			var region = get("regionName", "region");
			var city = get("city");
			var isp = get("isp", "org", "organization");
			var sb = new StringBuilder();
			if (ip.Length > 0) sb.AppendLine("IP: " + ip);
			var where = string.Join(" ", new[] { country, region, city }.Where(s => s.Length > 0));
			if (where.Length > 0) sb.AppendLine("位置: " + where);
			if (isp.Length > 0) sb.AppendLine("ISP: " + isp);
			var s = sb.ToString().TrimEnd();
			return s.Length > 0 ? s : json;
		}
		catch {
			return json.Length > 400 ? json.Substring(0, 400) + "…" : json;
		}
	}

	public static async Task<string> Whois(string query, CancellationToken ct) {
		query = AsciiHost(query);
		var first = await querywhois("whois.iana.org", query, ct).ConfigureAwait(false);
		var next = parserefer(first);
		if (string.IsNullOrEmpty(next)
			|| string.Equals(next, "whois.iana.org", StringComparison.OrdinalIgnoreCase))
			return header("whois.iana.org") + first;
		string second;
		try {
			second = await querywhois(next, query, ct).ConfigureAwait(false);
		}
		catch (Exception ex) {
			return header("whois.iana.org") + first + "\r\n\r\n===== " + next + " =====\r\n" + ex.Message;
		}
		return header("whois.iana.org") + first + "\r\n\r\n===== " + next + " =====\r\n" + second;
	}

	static async Task<T> withtimeout<T>(Task<T> task, int ms, CancellationToken ct) {
		var done = await Task.WhenAny(task, Task.Delay(ms, ct)).ConfigureAwait(false);
		if (done != task) throw new TimeoutException("dns timeout");
		return await task.ConfigureAwait(false);
	}

	static string header(string server) => "===== " + server + " =====\r\n";

	static string parserefer(string text) {
		if (string.IsNullOrEmpty(text)) return "";
		var m = WhoisRefer.Match(text);
		if (!m.Success) return "";
		var h = m.Groups[1].Value.Trim().TrimEnd('.');
		if (h.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return "";
		return h;
	}

	static async Task<string> querywhois(string server, string query, CancellationToken ct) {
		using var client = new TcpClient();
		var connect = client.ConnectAsync(server, 43);
		var done = await Task.WhenAny(connect, Task.Delay(10000, ct)).ConfigureAwait(false);
		if (done != connect) throw new TimeoutException("connect " + server);
		await connect.ConfigureAwait(false);
		ct.ThrowIfCancellationRequested();
		using var stream = client.GetStream();
		stream.ReadTimeout = 12000;
		stream.WriteTimeout = 8000;
		var payload = Encoding.ASCII.GetBytes(query + "\r\n");
		await stream.WriteAsync(payload, 0, payload.Length, ct).ConfigureAwait(false);
		using var ms = new MemoryStream();
		var buf = new byte[4096];
		while (true) {
			ct.ThrowIfCancellationRequested();
			var n = await stream.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false);
			if (n <= 0) break;
			ms.Write(buf, 0, n);
			if (ms.Length > 512 * 1024) break;
		}
		return decode(ms.ToArray());
	}

	static string decode(byte[] bytes) {
		if (bytes == null || bytes.Length == 0) return "";
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		var utf = Encoding.UTF8.GetString(bytes);
		if (utf.IndexOf('\uFFFD') < 0) return utf;
		return Encoding.GetEncoding(936).GetString(bytes);
	}
}
