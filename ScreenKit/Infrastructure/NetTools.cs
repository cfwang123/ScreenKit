using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
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
