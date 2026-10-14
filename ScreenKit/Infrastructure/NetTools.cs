using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Windows.Devices.Geolocation;

namespace ScreenKit;

sealed class LanComputer {
	public string HostName { get; set; }
	public string Ip { get; set; }
	public string Mac { get; set; }
}

/// <summary>Ping / DNS / WHOIS（TCP 43，不走 HTTP 代理）。</summary>
static class NetTools {
	static readonly Regex WhoisRefer = new(
		@"^(?:refer|whois)\s*:\s*(\S+)",
		RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
	static readonly Regex ArpLine = new(
		@"(?m)^\s*((?:\d{1,3}\.){3}\d{1,3})\s+((?:[0-9a-f]{2}[-:]){5}[0-9a-f]{2})\s+",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

	public static Task<string> Resolve(string host, CancellationToken ct) => Resolve(host, null, ct);

	/// <summary>通过系统 DNS 解析，并依次显示服务器、正向查询和 PTR 反查。</summary>
	public static async Task<string> Resolve(string host, Action<string> line, CancellationToken ct) {
		var input = (host ?? "").Trim();
		host = AsciiHost(input);
		var rows = new List<string>();
		void emit(string text) {
			rows.Add(text);
			line?.Invoke(text);
		}
		emit("DNS 解析  " + input);
		if (!string.Equals(input.TrimEnd('.'), host, StringComparison.OrdinalIgnoreCase))
			emit("规范化: " + host);
		var servers = dnsservers();
		emit("系统 DNS 服务器: " + (servers.Count > 0 ? string.Join(", ", servers) : "默认配置"));
		ct.ThrowIfCancellationRequested();
		if (IPAddress.TryParse(host, out var ip)) {
			var started = Environment.TickCount;
			emit("PTR 查询: " + ip);
			var entry = await withtimeout(Dns.GetHostEntryAsync(ip), 8000, ct).ConfigureAwait(false);
			emit("主机名: " + entry.HostName + elapsed(started));
			showaliases(entry, emit);
			showaddresses(entry.AddressList, emit, "正向确认");
			return string.Join(Environment.NewLine, rows);
		}
		var begin = Environment.TickCount;
		emit("A / AAAA 查询: " + host);
		var hit = await withtimeout(Dns.GetHostEntryAsync(host), 8000, ct).ConfigureAwait(false);
		if (!string.Equals(hit.HostName, host, StringComparison.OrdinalIgnoreCase))
			emit("规范名称: " + hit.HostName);
		showaliases(hit, emit);
		var addrs = hit.AddressList?.Distinct().ToArray() ?? Array.Empty<IPAddress>();
		if (addrs.Length == 0) throw new InvalidOperationException("no address");
		showaddresses(addrs, emit, "地址");
		emit("正向查询完成" + elapsed(begin));
		var reverseAddrs = addrs.Take(8).ToArray();
		foreach (var addr in reverseAddrs) {
			ct.ThrowIfCancellationRequested();
			var started = Environment.TickCount;
			emit("PTR 反查: " + addr);
			try {
				var reverse = await withtimeout(Dns.GetHostEntryAsync(addr), 2000, ct).ConfigureAwait(false);
				emit("  " + reverse.HostName + elapsed(started));
			}
			catch (Exception ex) when (ex is not OperationCanceledException) {
				emit("  无 PTR 记录: " + (ex.InnerException?.Message ?? ex.Message));
			}
		}
		if (addrs.Length > reverseAddrs.Length)
			emit("其余 " + (addrs.Length - reverseAddrs.Length) + " 个地址未反查");
		return string.Join(Environment.NewLine, rows);
	}

	/// <summary>将主机名或本地 IPv4 地址解析为 ARP 缓存中的 MAC。</summary>
	public static async Task ArpResolve(string query, Action<string> line, CancellationToken ct) {
		var host = AsciiHost(query);
		var ips = new List<IPAddress>();
		if (IPAddress.TryParse(host, out var parsed)) {
			if (parsed.AddressFamily != AddressFamily.InterNetwork)
				throw new ArgumentException("ARP 只支持 IPv4");
			ips.Add(parsed);
		}
		else {
			line?.Invoke("DNS A 查询: " + host);
			var addrs = await withtimeout(Dns.GetHostAddressesAsync(host), 8000, ct).ConfigureAwait(false);
			ips.AddRange(addrs.Where(x => x.AddressFamily == AddressFamily.InterNetwork).Distinct());
			if (ips.Count == 0) throw new InvalidOperationException("没有 IPv4 地址");
			foreach (var ip in ips) line?.Invoke("  " + ip);
		}
		foreach (var ip in ips) {
			ct.ThrowIfCancellationRequested();
			line?.Invoke("ARP 解析: " + ip);
			var local = localarp(ip);
			if (local != null) {
				line?.Invoke("  " + local.Ip + "  " + local.Mac + "  " + local.HostName);
				continue;
			}
			var rows = await arprows(ct).ConfigureAwait(false);
			var hit = rows.FirstOrDefault(x => x.Ip == ip.ToString());
			if (hit == null) {
				line?.Invoke("  缓存未命中，发送本地网络探测…");
				await pingarp(ip, ct).ConfigureAwait(false);
				rows = await arprows(ct).ConfigureAwait(false);
				hit = rows.FirstOrDefault(x => x.Ip == ip.ToString());
			}
			if (hit == null) {
				line?.Invoke("  未找到；ARP 只能解析同一本地网络中的 IPv4 设备");
				continue;
			}
			hit.HostName = await ptrname(ip, ct).ConfigureAwait(false);
			line?.Invoke("  " + hit.Ip + "  " + hit.Mac + "  " + (hit.HostName.Length > 0 ? hit.HostName : "-"));
		}
	}

	/// <summary>按 MAC 地址反查本机及当前 ARP 缓存中的 IPv4 地址。</summary>
	public static async Task ArpReverse(string query, Action<string> line, CancellationToken ct) {
		var hex = MacHex(query);
		if (hex.Length != 12) throw new ArgumentException("请输入完整的 MAC 地址");
		var mac = macshow(hex).Replace(':', '-');
		line?.Invoke("ARP 反查: " + mac);
		line?.Invoke("读取本机接口和 ARP 缓存…");
		var rows = localarps();
		rows.AddRange(await arprows(ct).ConfigureAwait(false));
		var hits = rows.Where(x => string.Equals(x.Mac, mac, StringComparison.OrdinalIgnoreCase))
			.GroupBy(x => x.Ip, StringComparer.Ordinal).Select(x => x.First()).ToList();
		if (hits.Count == 0) {
			line?.Invoke("未找到；反查只检查本机接口和当前 ARP 缓存，可先搜索其它计算机以填充缓存");
			return;
		}
		foreach (var hit in hits) {
			ct.ThrowIfCancellationRequested();
			if (hit.HostName.Length == 0)
				hit.HostName = await ptrname(IPAddress.Parse(hit.Ip), ct).ConfigureAwait(false);
			line?.Invoke(hit.Ip + "  " + hit.Mac + "  " + (hit.HostName.Length > 0 ? hit.HostName : "-"));
		}
	}

	static List<string> dnsservers() {
		var rows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()) {
			if (nic.OperationalStatus != OperationalStatus.Up) continue;
			try {
				foreach (var ip in nic.GetIPProperties().DnsAddresses)
					if (!IPAddress.IsLoopback(ip) && !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.IPv6Any))
						rows.Add(ip.ToString());
			}
			catch { }
		}
		return rows.ToList();
	}

	static void showaliases(IPHostEntry entry, Action<string> emit) {
		foreach (var alias in entry.Aliases ?? Array.Empty<string>())
			emit("别名: " + alias);
	}

	static void showaddresses(IEnumerable<IPAddress> addresses, Action<string> emit, string title) {
		foreach (var address in addresses)
			emit(title + ": " + address.AddressFamily + "  " + address);
	}

	static string elapsed(int started) {
		var ms = Environment.TickCount - started;
		return "  (" + Math.Max(0, ms) + " ms)";
	}

	static async Task<List<LanComputer>> arprows(CancellationToken ct) {
		var text = await Task.Run(readarp, ct).ConfigureAwait(false);
		ct.ThrowIfCancellationRequested();
		return ParseArp(text);
	}

	static async Task pingarp(IPAddress ip, CancellationToken ct) {
		using var ping = new Ping();
		var task = ping.SendPingAsync(ip, 900);
		var done = await Task.WhenAny(task, Task.Delay(1000, ct)).ConfigureAwait(false);
		if (done == task) {
			try { await task.ConfigureAwait(false); }
			catch (PingException) { }
		}
		ct.ThrowIfCancellationRequested();
	}

	static async Task<string> ptrname(IPAddress ip, CancellationToken ct) {
		try {
			var entry = await withtimeout(Dns.GetHostEntryAsync(ip), 2000, ct).ConfigureAwait(false);
			return (entry?.HostName ?? "").Trim().TrimEnd('.');
		}
		catch (Exception ex) when (ex is not OperationCanceledException) {
			return "";
		}
	}

	static LanComputer localarp(IPAddress ip) => localarps().FirstOrDefault(x => x.Ip == ip.ToString());

	static List<LanComputer> localarps() {
		var rows = new List<LanComputer>();
		foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()) {
			if (nic.OperationalStatus != OperationalStatus.Up) continue;
			var bytes = nic.GetPhysicalAddress()?.GetAddressBytes() ?? Array.Empty<byte>();
			if (bytes.Length != 6) continue;
			var mac = string.Join("-", bytes.Select(x => x.ToString("X2", CultureInfo.InvariantCulture)));
			try {
				foreach (var addr in nic.GetIPProperties().UnicastAddresses) {
					if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
					rows.Add(new LanComputer {
						HostName = Environment.MachineName,
						Ip = addr.Address.ToString(),
						Mac = mac,
					});
				}
			}
			catch { }
		}
		return rows;
	}

	static readonly (string Name, ushort Type)[] DnsRecordTypes = {
		("CNAME", 5), ("MX", 15), ("NS", 2), ("TXT", 16), ("SOA", 6),
	};
	static readonly int[] CommonPorts = { 22, 80, 443, 445, 3389, 8080 };

	/// <summary>向系统 DNS 查询 CNAME、MX、NS、TXT 和 SOA，并显示每一步。</summary>
	public static async Task DnsRecords(string host, Action<string> line, CancellationToken ct) {
		var input = (host ?? "").Trim();
		host = AsciiHost(input);
		if (IPAddress.TryParse(host, out _)) throw new ArgumentException("请输入域名");
		var servers = dnsservers();
		if (servers.Count == 0) throw new InvalidOperationException("没有可用的 DNS 服务器");
		line?.Invoke("DNS 记录  " + host);
		line?.Invoke("DNS 服务器: " + string.Join(", ", servers));
		foreach (var rec in DnsRecordTypes) {
			ct.ThrowIfCancellationRequested();
			var started = Environment.TickCount;
			line?.Invoke(rec.Name + " 查询…");
			Exception last = null;
			List<string> rows = null;
			foreach (var server in servers) {
				try {
					rows = await querydns(server, host, rec.Type, ct).ConfigureAwait(false);
					last = null;
					break;
				}
				catch (Exception ex) when (ex is not OperationCanceledException) {
					last = ex;
				}
			}
			if (last != null) {
				line?.Invoke("  失败: " + (last.InnerException?.Message ?? last.Message));
				continue;
			}
			if (rows == null || rows.Count == 0)
				line?.Invoke("  无记录" + elapsed(started));
			else {
				foreach (var row in rows) line?.Invoke("  " + row);
				line?.Invoke("  完成" + elapsed(started));
			}
		}
	}

	/// <summary>测试一个 TCP 端口；未写端口时测试一组常用端口。</summary>
	public static async Task PortProbe(string input, Action<string> line, CancellationToken ct) {
		var target = ParsePortTarget(input);
		var ports = target.Port > 0 ? new[] { target.Port } : CommonPorts;
		line?.Invoke("端口测试  " + target.Host);
		foreach (var port in ports) {
			ct.ThrowIfCancellationRequested();
			var started = Environment.TickCount;
			var result = "关闭或无响应";
			using var tcp = new TcpClient();
			var task = tcp.ConnectAsync(target.Host, port);
			var done = await Task.WhenAny(task, Task.Delay(1500, ct)).ConfigureAwait(false);
			if (done == task) {
				try {
					await task.ConfigureAwait(false);
					result = "连通  " + Math.Max(0, Environment.TickCount - started) + " ms";
				}
				catch (Exception ex) when (ex is not OperationCanceledException) {
					result = "失败: " + (ex.InnerException?.Message ?? ex.Message);
				}
			}
			ct.ThrowIfCancellationRequested();
			line?.Invoke(port + "  " + result);
		}
	}

	/// <summary>列出活动网卡的地址、网关、DNS、速率，以及 IPv4 路由。</summary>
	public static void LocalNetwork(Action<string> line) {
		line?.Invoke("本机网络");
		var found = false;
		foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()) {
			if (nic.OperationalStatus != OperationalStatus.Up) continue;
			found = true;
			var speed = nic.Speed > 0 ? (nic.Speed / 1000000).ToString(CultureInfo.InvariantCulture) + " Mbps" : "-";
			line?.Invoke(nic.Name + "  " + nic.NetworkInterfaceType + "  " + speed);
			var bytes = nic.GetPhysicalAddress()?.GetAddressBytes() ?? Array.Empty<byte>();
			if (bytes.Length == 6)
				line?.Invoke("  MAC: " + string.Join("-", bytes.Select(x => x.ToString("X2", CultureInfo.InvariantCulture))));
			IPInterfaceProperties props = null;
			try { props = nic.GetIPProperties(); }
			catch { }
			if (props == null) continue;
			foreach (var addr in props.UnicastAddresses) {
				if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
					line?.Invoke("  IPv4: " + addr.Address + "  掩码: " + (addr.IPv4Mask?.ToString() ?? "-"));
				else if (addr.Address.AddressFamily == AddressFamily.InterNetworkV6)
					line?.Invoke("  IPv6: " + addr.Address);
			}
			foreach (var gate in props.GatewayAddresses)
				line?.Invoke("  网关: " + gate.Address);
			var dns = props.DnsAddresses.Select(x => x.ToString()).ToArray();
			line?.Invoke("  DNS: " + (dns.Length > 0 ? string.Join(", ", dns) : "-"));
		}
		if (!found) line?.Invoke("没有活动网卡");
		line?.Invoke("IPv4 路由");
		var routes = ipv4routes();
		if (routes.Count == 0) line?.Invoke("  未读取到路由");
		foreach (var route in routes) line?.Invoke("  " + route);
	}

	/// <summary>向广播地址发送 Wake-on-LAN 魔术包。</summary>
	public static void Wake(string macText, Action<string> line) {
		var packet = BuildWake(macText);
		var show = macshow(MacHex(macText)).Replace(':', '-');
		using var udp = new UdpClient();
		udp.EnableBroadcast = true;
		var targets = new List<IPEndPoint> {
			new(IPAddress.Broadcast, 9),
			new(IPAddress.Broadcast, 7),
		};
		foreach (var broadcast in broadcasts())
			targets.Add(new IPEndPoint(broadcast, 9));
		var sent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var target in targets) {
			var key = target.ToString();
			if (!sent.Add(key)) continue;
			udp.Send(packet, packet.Length, target);
			line?.Invoke("已发送  " + show + "  →  " + key);
		}
	}

	internal static (string Host, int Port) ParsePortTarget(string raw) {
		raw = (raw ?? "").Trim();
		if (raw.Length == 0) throw new ArgumentException("empty");
		if (raw.StartsWith("[")) {
			var end = raw.IndexOf(']');
			if (end > 1 && end + 1 < raw.Length && raw[end + 1] == ':') {
				var host6 = raw.Substring(1, end - 1);
				if (!int.TryParse(raw.Substring(end + 2), out var port6) || port6 < 1 || port6 > 65535)
					throw new ArgumentException("无效的端口");
				return (AsciiHost(host6), port6);
			}
		}
		var colon = raw.LastIndexOf(':');
		if (colon > 0 && raw.IndexOf(':') == colon) {
			var tail = raw.Substring(colon + 1);
			if (int.TryParse(tail, out var port) && port >= 1 && port <= 65535)
				return (AsciiHost(raw.Substring(0, colon)), port);
		}
		return (AsciiHost(raw), 0);
	}

	internal static byte[] BuildWake(string macText) {
		var hex = MacHex(macText);
		if (hex.Length != 12) throw new ArgumentException("请输入完整的 MAC 地址");
		var mac = new byte[6];
		for (var i = 0; i < 6; i++)
			mac[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
		var packet = new byte[102];
		for (var i = 0; i < 6; i++) packet[i] = 0xFF;
		for (var i = 0; i < 16; i++)
			Buffer.BlockCopy(mac, 0, packet, 6 + i * 6, 6);
		return packet;
	}

	internal static List<string> ParseDns(byte[] buf) {
		var rows = new List<string>();
		if (buf == null || buf.Length < 12) return rows;
		var rcode = ((buf[2] << 8) | buf[3]) & 0x0F;
		var questions = (buf[4] << 8) | buf[5];
		var answers = (buf[6] << 8) | buf[7];
		var i = 12;
		for (var q = 0; q < questions; q++) {
			dnsname(buf, ref i);
			i += 4;
		}
		if (rcode != 0 && answers == 0) {
			rows.Add("DNS 返回码 " + rcode);
			return rows;
		}
		for (var a = 0; a < answers && i + 10 <= buf.Length; a++) {
			dnsname(buf, ref i);
			var typ = (buf[i] << 8) | buf[i + 1];
			var len = (buf[i + 8] << 8) | buf[i + 9];
			i += 10;
			if (len < 0 || i + len > buf.Length) break;
			var text = dnstext(buf, typ, i, len);
			if (text.Length > 0) rows.Add(text);
			i += len;
		}
		return rows;
	}

	internal static string ParseNetBios(byte[] buf) {
		if (buf == null || buf.Length < 63 || buf[46] != 0 || buf[47] != 0x21) return "";
		var count = buf[62];
		var at = 63;
		var enc = Encoding.Default;
		for (var n = 0; n < count && at + 18 <= buf.Length; n++) {
			var name = enc.GetString(buf, at, 15).Trim().Trim('\0', ' ');
			var suffix = buf[at + 15];
			var flags = (buf[at + 16] << 8) | buf[at + 17];
			at += 18;
			if ((flags & 0x8000) == 0 && suffix == 0 && name.Length > 0) return name;
		}
		return "";
	}

	static async Task<List<string>> querydns(string server, string name, ushort type, CancellationToken ct) {
		var id = (ushort)(Environment.TickCount & 0xFFFF);
		var packet = dnsquery(id, name, type);
		using var udp = new UdpClient();
		var send = udp.SendAsync(packet, packet.Length, server, 53);
		if (await Task.WhenAny(send, Task.Delay(3000, ct)).ConfigureAwait(false) != send)
			throw new TimeoutException("dns timeout");
		await send.ConfigureAwait(false);
		var recv = udp.ReceiveAsync();
		if (await Task.WhenAny(recv, Task.Delay(3000, ct)).ConfigureAwait(false) != recv) {
			ct.ThrowIfCancellationRequested();
			throw new TimeoutException("dns timeout");
		}
		var result = await recv.ConfigureAwait(false);
		if (result.Buffer.Length < 2 || ((result.Buffer[0] << 8) | result.Buffer[1]) != id)
			throw new InvalidOperationException("DNS 应答不匹配");
		return ParseDns(result.Buffer);
	}

	static byte[] dnsquery(ushort id, string name, ushort type) {
		var ms = new MemoryStream();
		void u16(ushort value) {
			ms.WriteByte((byte)(value >> 8));
			ms.WriteByte((byte)value);
		}
		u16(id);
		u16(0x0100);
		u16(1);
		u16(0);
		u16(0);
		u16(0);
		foreach (var label in name.Split('.')) {
			var bytes = Encoding.ASCII.GetBytes(label);
			if (bytes.Length == 0 || bytes.Length > 63) throw new ArgumentException("无效的域名");
			ms.WriteByte((byte)bytes.Length);
			ms.Write(bytes, 0, bytes.Length);
		}
		ms.WriteByte(0);
		u16(type);
		u16(1);
		return ms.ToArray();
	}

	static string dnsname(byte[] buf, ref int i) {
		var labels = new List<string>();
		var jumped = false;
		var end = i;
		for (var hop = 0; hop < 16 && i < buf.Length; hop++) {
			var n = buf[i];
			if (n == 0) {
				i++;
				break;
			}
			if ((n & 0xC0) == 0xC0) {
				if (i + 1 >= buf.Length) break;
				if (!jumped) end = i + 2;
				jumped = true;
				i = ((n & 0x3F) << 8) | buf[i + 1];
				continue;
			}
			i++;
			if (n > 63 || i + n > buf.Length) break;
			labels.Add(Encoding.ASCII.GetString(buf, i, n));
			i += n;
		}
		if (jumped) i = end;
		return string.Join(".", labels);
	}

	static string dnstext(byte[] buf, int type, int at, int len) {
		var end = at + len;
		if (type == 1 && len == 4) return "A " + new IPAddress(buf.Skip(at).Take(4).ToArray());
		if (type == 28 && len == 16) return "AAAA " + new IPAddress(buf.Skip(at).Take(16).ToArray());
		if (type == 5 || type == 2) {
			var i = at;
			return (type == 5 ? "CNAME " : "NS ") + dnsname(buf, ref i);
		}
		if (type == 15 && len >= 3) {
			var pref = (buf[at] << 8) | buf[at + 1];
			var i = at + 2;
			return "MX " + pref + " " + dnsname(buf, ref i);
		}
		if (type == 16) {
			var parts = new List<string>();
			var i = at;
			while (i < end) {
				var n = buf[i++];
				if (i + n > end) break;
				parts.Add(Encoding.UTF8.GetString(buf, i, n));
				i += n;
			}
			return "TXT " + string.Join("", parts);
		}
		if (type == 6) {
			var i = at;
			var primary = dnsname(buf, ref i);
			var mail = dnsname(buf, ref i);
			if (i + 4 <= end) {
				var serial = (uint)((buf[i] << 24) | (buf[i + 1] << 16) | (buf[i + 2] << 8) | buf[i + 3]);
				return "SOA " + primary + " " + mail + " " + serial;
			}
		}
		return "";
	}

	static async Task<string> netbios(IPAddress ip, CancellationToken ct) {
		try {
			var query = new byte[] {
				0x82, 0x28, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20,
				0x43, 0x4B, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
				0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
				0x41, 0x41, 0x41, 0x41, 0x41, 0x00, 0x00, 0x21, 0x00, 0x01,
			};
			using var udp = new UdpClient();
			var send = udp.SendAsync(query, query.Length, new IPEndPoint(ip, 137));
			if (await Task.WhenAny(send, Task.Delay(700, ct)).ConfigureAwait(false) != send) return "";
			await send.ConfigureAwait(false);
			var recv = udp.ReceiveAsync();
			if (await Task.WhenAny(recv, Task.Delay(700, ct)).ConfigureAwait(false) != recv) {
				ct.ThrowIfCancellationRequested();
				return "";
			}
			return ParseNetBios((await recv.ConfigureAwait(false)).Buffer);
		}
		catch (Exception ex) when (ex is not OperationCanceledException) {
			return "";
		}
	}

	static List<IPAddress> broadcasts() {
		var rows = new List<IPAddress>();
		foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()) {
			if (nic.OperationalStatus != OperationalStatus.Up ||
				nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
			try {
				foreach (var addr in nic.GetIPProperties().UnicastAddresses) {
					if (addr.Address.AddressFamily != AddressFamily.InterNetwork || addr.IPv4Mask == null) continue;
					var value = ipuint(addr.Address);
					var mask = ipuint(addr.IPv4Mask);
					if (mask == 0) continue;
					rows.Add(ipof(value | ~mask));
				}
			}
			catch { }
		}
		return rows;
	}

	[DllImport("iphlpapi.dll", SetLastError = true)]
	static extern int GetIpForwardTable(IntPtr table, ref int size, bool order);

	static List<string> ipv4routes() {
		var rows = new List<string>();
		var size = 0;
		GetIpForwardTable(IntPtr.Zero, ref size, true);
		if (size <= 0) return rows;
		var mem = Marshal.AllocHGlobal(size);
		try {
			if (GetIpForwardTable(mem, ref size, true) != 0) return rows;
			var count = Marshal.ReadInt32(mem);
			var max = size > 4 ? (size - 4) / 56 : 0;
			if (count > max) count = max;
			if (count > 64) count = 64;
			for (var i = 0; i < count; i++) {
				var at = IntPtr.Add(mem, 4 + i * 56);
				var dest = swap32((uint)Marshal.ReadInt32(at));
				var mask = swap32((uint)Marshal.ReadInt32(IntPtr.Add(at, 4)));
				var hop = swap32((uint)Marshal.ReadInt32(IntPtr.Add(at, 12)));
				var metric = Marshal.ReadInt32(IntPtr.Add(at, 36));
				if ((dest & 0xF0000000) == 0xE0000000) continue;
				rows.Add(ipof(dest) + "/" + maskbits(mask) + "  via " + ipof(hop) + "  metric " + metric);
			}
		}
		catch { }
		finally { Marshal.FreeHGlobal(mem); }
		return rows;
	}

	static uint swap32(uint value) =>
		(value >> 24) | ((value >> 8) & 0xFF00) | ((value << 8) & 0xFF0000) | (value << 24);

	static int maskbits(uint mask) {
		var n = 0;
		while (mask != 0) {
			n += (int)(mask & 1);
			mask >>= 1;
		}
		return n;
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

	/// <summary>搜索活动网卡所在局域网，通过并发 ICMP 填充 ARP 表，再反查主机名。</summary>
	public static async Task<List<LanComputer>> SearchComputers(Action<string> line, CancellationToken ct) {
		var candidates = lancandidates();
		line?.Invoke(Loc.T("nettool.scan.start", candidates.Count));
		if (candidates.Count == 0) {
			line?.Invoke(Loc.T("nettool.scan.none"));
			return new List<LanComputer>();
		}
		var done = 0;
		var gate = new SemaphoreSlim(48);
		var tasks = new List<Task>();
		try {
			foreach (var value in candidates) {
				ct.ThrowIfCancellationRequested();
				await gate.WaitAsync(ct).ConfigureAwait(false);
				var ip = ipof(value);
				tasks.Add(scanone(ip, gate, () => {
					var n = Interlocked.Increment(ref done);
					if (n == candidates.Count || n % 64 == 0)
						line?.Invoke(Loc.T("nettool.scan.progress", n, candidates.Count));
				}, ct));
			}
		}
		finally {
			try { await Task.WhenAll(tasks).ConfigureAwait(false); }
			catch (OperationCanceledException) { }
		}
		ct.ThrowIfCancellationRequested();
		var arp = await Task.Run(readarp, ct).ConfigureAwait(false);
		var wanted = new HashSet<string>(candidates.Select(x => ipof(x).ToString()), StringComparer.Ordinal);
		var rows = ParseArp(arp).Where(x => wanted.Contains(x.Ip)).ToList();
		await resolvenames(rows, ct).ConfigureAwait(false);
		rows.Sort((a, b) => ipuint(IPAddress.Parse(a.Ip)).CompareTo(ipuint(IPAddress.Parse(b.Ip))));
		if (rows.Count == 0) {
			line?.Invoke(Loc.T("nettool.scan.none"));
			return rows;
		}
		line?.Invoke(Loc.T("nettool.scan.columns"));
		foreach (var row in rows)
			line?.Invoke($"{(row.HostName.Length > 0 ? row.HostName : "-")}\t{row.Ip}\t{row.Mac}");
		line?.Invoke(Loc.T("nettool.scan.found", rows.Count));
		return rows;
	}

	internal static List<LanComputer> ParseArp(string text) {
		var map = new Dictionary<string, LanComputer>(StringComparer.Ordinal);
		foreach (Match match in ArpLine.Matches(text ?? "")) {
			var rawip = match.Groups[1].Value;
			if (!IPAddress.TryParse(rawip, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) continue;
			var mac = match.Groups[2].Value.Replace(':', '-').ToUpperInvariant();
			if (mac == "00-00-00-00-00-00" || mac == "FF-FF-FF-FF-FF-FF") continue;
			map[rawip] = new LanComputer { HostName = "", Ip = rawip, Mac = mac };
		}
		return map.Values.ToList();
	}

	static async Task scanone(IPAddress ip, SemaphoreSlim gate, Action complete, CancellationToken ct) {
		try {
			ct.ThrowIfCancellationRequested();
			using var ping = new Ping();
			try { await ping.SendPingAsync(ip, 350).ConfigureAwait(false); }
			catch (PingException) { }
			ct.ThrowIfCancellationRequested();
		}
		finally {
			complete();
			gate.Release();
		}
	}

	static async Task resolvenames(List<LanComputer> rows, CancellationToken ct) {
		var gate = new SemaphoreSlim(16);
		var tasks = new List<Task>();
		try {
			foreach (var row in rows) {
				await gate.WaitAsync(ct).ConfigureAwait(false);
				tasks.Add(resolveone(row, gate, ct));
			}
		}
		finally {
			try { await Task.WhenAll(tasks).ConfigureAwait(false); }
			catch (OperationCanceledException) { }
		}
		ct.ThrowIfCancellationRequested();
	}

	static async Task resolveone(LanComputer row, SemaphoreSlim gate, CancellationToken ct) {
		try {
			var task = Dns.GetHostEntryAsync(row.Ip);
			var done = await Task.WhenAny(task, Task.Delay(1200, ct)).ConfigureAwait(false);
			if (done == task) {
				var entry = await task.ConfigureAwait(false);
				row.HostName = (entry?.HostName ?? "").Trim().TrimEnd('.');
				if (row.HostName == row.Ip) row.HostName = "";
			}
			if (row.HostName.Length == 0)
				row.HostName = await netbios(IPAddress.Parse(row.Ip), ct).ConfigureAwait(false);
			ct.ThrowIfCancellationRequested();
		}
		catch (Exception ex) when (ex is not OperationCanceledException) { }
		finally { gate.Release(); }
	}

	static List<uint> lancandidates() {
		var own = new HashSet<uint>();
		var all = new HashSet<uint>();
		foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()) {
			if (nic.OperationalStatus != OperationalStatus.Up ||
				nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
			IPInterfaceProperties props;
			try { props = nic.GetIPProperties(); }
			catch { continue; }
			foreach (var unicast in props.UnicastAddresses) {
				var ip = unicast.Address;
				var mask = unicast.IPv4Mask;
				if (ip == null || mask == null || ip.AddressFamily != AddressFamily.InterNetwork) continue;
				var value = ipuint(ip);
				var m = ipuint(mask);
				if (value == 0 || m == 0) continue;
				own.Add(value);
				var network = value & m;
				var broadcast = network | ~m;
				var count = broadcast > network ? (ulong)broadcast - network - 1 : 0;
				if (count > 1024) {
					m = 0xFFFFFF00;
					network = value & m;
					broadcast = network | ~m;
				}
				for (var at = network + 1; at < broadcast && all.Count < 2048; at++)
					all.Add(at);
			}
		}
		all.ExceptWith(own);
		var rows = all.ToList();
		rows.Sort();
		return rows;
	}

	static string readarp() {
		var path = Path.Combine(Environment.SystemDirectory, "arp.exe");
		var psi = new System.Diagnostics.ProcessStartInfo {
			FileName = path,
			Arguments = "-a",
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.Default,
		};
		using var process = System.Diagnostics.Process.Start(psi);
		if (process == null) throw new InvalidOperationException("arp start failed");
		var text = process.StandardOutput.ReadToEnd();
		var error = process.StandardError.ReadToEnd();
		process.WaitForExit();
		if (process.ExitCode != 0)
			throw new InvalidOperationException(error.Trim().Length > 0 ? error.Trim() : "arp exit " + process.ExitCode);
		return text;
	}

	static uint ipuint(IPAddress ip) {
		var b = ip.GetAddressBytes();
		return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
	}

	static IPAddress ipof(uint value) => new(new[] {
		(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value,
	});

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

	/// <summary>Windows.Devices.Geolocation。系统选 Wi-Fi 或 GPS。第一次调用会询问是否允许。</summary>
	public static async Task<(bool Ok, double Lat, double Lon)> Geolocate(Action<string> line, CancellationToken ct) {
		line?.Invoke("系统定位（Wi-Fi / GPS）");
		GeolocationAccessStatus access;
		try {
			access = await Geolocator.RequestAccessAsync().AsTask(ct).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is not OperationCanceledException) {
			line?.Invoke("权限: " + (ex.InnerException?.Message ?? ex.Message));
			return (false, 0, 0);
		}
		line?.Invoke("权限: " + accesszh(access));
		if (access != GeolocationAccessStatus.Allowed) {
			line?.Invoke("请在 Windows 设置 → 隐私和安全性 → 位置 中打开定位服务，并允许桌面应用访问位置。");
			return (false, 0, 0);
		}
		var geo = new Geolocator { DesiredAccuracy = PositionAccuracy.Default };
		Geoposition pos;
		try {
			pos = await geo.GetGeopositionAsync(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(15))
				.AsTask(ct).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is not OperationCanceledException) {
			line?.Invoke("定位失败: " + (ex.InnerException?.Message ?? ex.Message));
			return (false, 0, 0);
		}
		var c = pos.Coordinate;
		var p = c.Point.Position;
		var lat = p.Latitude;
		var lon = p.Longitude;
		line?.Invoke("纬度: " + lat.ToString("0.######", CultureInfo.InvariantCulture));
		line?.Invoke("经度: " + lon.ToString("0.######", CultureInfo.InvariantCulture));
		line?.Invoke("精度: " + c.Accuracy.ToString("0", CultureInfo.InvariantCulture) + " m");
		line?.Invoke("来源: " + sourcezh(c.PositionSource));
		if (!double.IsNaN(p.Altitude))
			line?.Invoke("海拔: " + p.Altitude.ToString("0.#", CultureInfo.InvariantCulture) + " m");
		var civic = pos.CivicAddress;
		if (civic != null) {
			var where = string.Join(" ", new[] { civic.Country, civic.State, civic.City, civic.PostalCode }
				.Where(s => !string.IsNullOrWhiteSpace(s)));
			if (where.Length > 0)
				line?.Invoke("地点: " + where);
		}
		line?.Invoke("时间: " + c.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
		line?.Invoke("OpenStreetMap: " + MapOsm(lat, lon));
		line?.Invoke("高德地图: " + MapAmap(lat, lon));
		return (true, lat, lon);
	}

	/// <summary>WGS84 坐标的 OpenStreetMap 链接。</summary>
	public static string MapOsm(double lat, double lon) {
		var la = lat.ToString("0.######", CultureInfo.InvariantCulture);
		var lo = lon.ToString("0.######", CultureInfo.InvariantCulture);
		return "https://www.openstreetmap.org/?mlat=" + la + "&mlon=" + lo + "#map=16/" + la + "/" + lo;
	}

	/// <summary>高德标注。国内把 WGS84 换成 GCJ-02，标点才落在路上；境外仍用原坐标。</summary>
	public static string MapAmap(double lat, double lon) {
		var g = wgs84togcj(lat, lon);
		var lo = g.Lon.ToString("0.######", CultureInfo.InvariantCulture);
		var la = g.Lat.ToString("0.######", CultureInfo.InvariantCulture);
		return "https://uri.amap.com/marker?position=" + lo + "," + la + "&name=ScreenKit";
	}

	static (double Lat, double Lon) wgs84togcj(double lat, double lon) {
		if (lon < 72.004 || lon > 137.8347 || lat < 0.8293 || lat > 55.8271)
			return (lat, lon);
		const double a = 6378245.0;
		const double ee = 0.00669342162296594323;
		var dlat = gcjlat(lon - 105.0, lat - 35.0);
		var dlon = gcjlon(lon - 105.0, lat - 35.0);
		var rad = lat / 180.0 * Math.PI;
		var magic = Math.Sin(rad);
		magic = 1 - ee * magic * magic;
		var sq = Math.Sqrt(magic);
		dlat = dlat * 180.0 / ((a * (1 - ee)) / (magic * sq) * Math.PI);
		dlon = dlon * 180.0 / (a / sq * Math.Cos(rad) * Math.PI);
		return (lat + dlat, lon + dlon);
	}

	static double gcjlat(double x, double y) {
		var ret = -100.0 + 2.0 * x + 3.0 * y + 0.2 * y * y + 0.1 * x * y + 0.2 * Math.Sqrt(Math.Abs(x));
		ret += (20.0 * Math.Sin(6.0 * x * Math.PI) + 20.0 * Math.Sin(2.0 * x * Math.PI)) * 2.0 / 3.0;
		ret += (20.0 * Math.Sin(y * Math.PI) + 40.0 * Math.Sin(y / 3.0 * Math.PI)) * 2.0 / 3.0;
		ret += (160.0 * Math.Sin(y / 12.0 * Math.PI) + 320.0 * Math.Sin(y * Math.PI / 30.0)) * 2.0 / 3.0;
		return ret;
	}

	static double gcjlon(double x, double y) {
		var ret = 300.0 + x + 2.0 * y + 0.1 * x * x + 0.1 * x * y + 0.1 * Math.Sqrt(Math.Abs(x));
		ret += (20.0 * Math.Sin(6.0 * x * Math.PI) + 20.0 * Math.Sin(2.0 * x * Math.PI)) * 2.0 / 3.0;
		ret += (20.0 * Math.Sin(x * Math.PI) + 40.0 * Math.Sin(x / 3.0 * Math.PI)) * 2.0 / 3.0;
		ret += (150.0 * Math.Sin(x / 12.0 * Math.PI) + 300.0 * Math.Sin(x / 30.0 * Math.PI)) * 2.0 / 3.0;
		return ret;
	}

	static string accesszh(GeolocationAccessStatus s) {
		if (s == GeolocationAccessStatus.Allowed) return "已允许";
		if (s == GeolocationAccessStatus.Denied) return "已拒绝";
		return "未指定";
	}

	static string sourcezh(PositionSource s) {
		if (s == PositionSource.Satellite) return "GPS";
		if (s == PositionSource.WiFi) return "Wi-Fi";
		if (s == PositionSource.Cellular) return "基站";
		if (s == PositionSource.IPAddress) return "IP";
		if (s == PositionSource.Default) return "默认";
		return "未知";
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

	/// <summary>IP 或域名的国家、地区、城市。空字符串查本机出口。</summary>
	public sealed class IpCityInfo {
		public bool Ok { get; set; }
		public string Ip { get; set; } = "";
		public string Country { get; set; } = "";
		public string Region { get; set; } = "";
		public string City { get; set; } = "";
		public string Isp { get; set; } = "";
		public string Error { get; set; } = "";
		public string Text { get; set; } = "";
		public double Lat { get; set; }
		public double Lon { get; set; }
		public bool HasPos { get; set; }
	}

	/// <summary>解析 ip-api / ipinfo / ip.sb 的 JSON。不访问网络。</summary>
	public static IpCityInfo ParseIpCity(string json) {
		var info = new IpCityInfo();
		json = (json ?? "").Trim();
		if (json.Length == 0) {
			info.Error = "empty";
			return info;
		}
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
			if (string.Equals(get("status"), "fail", StringComparison.OrdinalIgnoreCase)) {
				info.Error = get("message");
				if (info.Error.Length == 0) info.Error = "fail";
				info.Text = "fail: " + info.Error;
				return info;
			}
			info.Ip = get("query", "ip");
			info.Country = get("country", "country_name");
			info.Region = get("regionName", "region");
			info.City = get("city");
			info.Isp = get("isp", "org", "organization");
			var latS = get("lat", "latitude");
			var lonS = get("lon", "longitude");
			var loc = get("loc");
			var comma = loc.IndexOf(',');
			if (comma > 0) {
				if (latS.Length == 0) latS = loc.Substring(0, comma).Trim();
				if (lonS.Length == 0) lonS = loc.Substring(comma + 1).Trim();
			}
			if (double.TryParse(latS, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
				&& double.TryParse(lonS, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
				&& lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180) {
				info.Lat = lat;
				info.Lon = lon;
				info.HasPos = true;
			}
			var sb = new StringBuilder();
			if (info.Ip.Length > 0) sb.AppendLine($"IP: {info.Ip}");
			var where = string.Join(" ", new[] { info.Country, info.Region, info.City }.Where(s => s.Length > 0));
			if (where.Length > 0) sb.AppendLine($"位置: {where}");
			if (info.Isp.Length > 0) sb.AppendLine($"ISP: {info.Isp}");
			if (info.HasPos) {
				sb.AppendLine("纬度: " + info.Lat.ToString("0.######", CultureInfo.InvariantCulture));
				sb.AppendLine("经度: " + info.Lon.ToString("0.######", CultureInfo.InvariantCulture));
			}
			info.Text = sb.ToString().TrimEnd();
			info.Ok = info.City.Length > 0 || info.Country.Length > 0 || info.Region.Length > 0;
			if (!info.Ok) {
				info.Error = "empty";
				info.Text = json.Length > 400 ? json.Substring(0, 400) + "…" : json;
			}
			return info;
		}
		catch {
			info.Error = "bad json";
			info.Text = json.Length > 400 ? json.Substring(0, 400) + "…" : json;
			return info;
		}
	}

	/// <summary>查 IP 所在城市。query 为空时查本机出口。走程序里的 HTTP 代理设置。</summary>
	public static async Task<IpCityInfo> IpCity(string query, Action<string> line, CancellationToken ct) {
		var host = (query ?? "").Trim();
		if (host.Length > 0) {
			host = AsciiHost(host);
			if (!cityhost(host)) throw new ArgumentException("无效的 IP 或域名");
		}
		line?.Invoke(host.Length == 0 ? "IP 城市（本机出口）" : $"IP 城市  {host}");
		var path = host.Length == 0 ? "" : citypath(host);
		var urls = new[] {
			"http://ip-api.com/json/" + path + "?fields=status,message,country,regionName,city,isp,query,lat,lon&lang=zh-CN",
			path.Length == 0 ? "https://ipinfo.io/json" : "https://ipinfo.io/" + path + "/json",
			path.Length == 0 ? "https://api.ip.sb/geoip" : "https://api.ip.sb/geoip/" + path,
		};
		IpCityInfo lastInfo = null;
		Exception lastEx = null;
		foreach (var url in urls) {
			ct.ThrowIfCancellationRequested();
			try {
				var body = await httpgetproxy(url, ct).ConfigureAwait(false);
				var info = ParseIpCity(body);
				line?.Invoke(url);
				line?.Invoke(info.Text.Length > 0 ? info.Text : info.Error);
				if (info.Ok) {
					if (info.HasPos) {
						line?.Invoke("OpenStreetMap: " + MapOsm(info.Lat, info.Lon));
						line?.Invoke("高德地图: " + MapAmap(info.Lat, info.Lon));
					}
					return info;
				}
				lastInfo = info;
				if (cityhard(info.Error)) return info;
			}
			catch (Exception ex) when (ex is not OperationCanceledException) {
				lastEx = ex;
				line?.Invoke(url + "  " + (ex.InnerException?.Message ?? ex.Message));
			}
		}
		line?.Invoke("IP 城市查询失败");
		if (lastInfo != null) return lastInfo;
		return new IpCityInfo { Error = lastEx?.InnerException?.Message ?? lastEx?.Message ?? "查询失败" };
	}

	static bool cityhost(string host) {
		if (host.Length == 0 || host.Length > 253) return false;
		if (IPAddress.TryParse(host, out _)) return true;
		var parts = host.Split('.');
		if (parts.Length < 2) return false;
		foreach (var part in parts) {
			if (part.Length == 0 || part.Length > 63) return false;
			for (var i = 0; i < part.Length; i++) {
				var c = part[i];
				var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
					|| (c >= '0' && c <= '9') || c == '-';
				if (!ok) return false;
			}
			if (part[0] == '-' || part[part.Length - 1] == '-') return false;
		}
		return true;
	}

	static string citypath(string host) {
		if (IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6)
			return "[" + host + "]";
		return host;
	}

	static bool cityhard(string error) {
		if (string.IsNullOrEmpty(error)) return false;
		var s = error.ToLowerInvariant();
		return s.IndexOf("private", StringComparison.Ordinal) >= 0
			|| s.IndexOf("reserved", StringComparison.Ordinal) >= 0
			|| s.IndexOf("invalid", StringComparison.Ordinal) >= 0;
	}

	/// <summary>MAC 的 IEEE 登记厂家。</summary>
	public sealed class MacVendorInfo {
		public bool Ok { get; set; }
		public string Mac { get; set; } = "";
		public string Company { get; set; } = "";
		public string Country { get; set; } = "";
		public string Address { get; set; } = "";
		public string Block { get; set; } = "";
		public bool Local { get; set; }
		public bool Multicast { get; set; }
		public string Error { get; set; } = "";
		public string Text { get; set; } = "";
	}

	/// <summary>抽出 6 到 12 位十六进制。冒号、减号、点号和空格都去掉。</summary>
	public static string MacHex(string raw) {
		raw = (raw ?? "").Trim();
		var sb = new StringBuilder();
		foreach (var ch in raw) {
			if (ch == ':' || ch == '-' || ch == '.' || ch == ' ' || ch == '\t') continue;
			var c = ch;
			if (c >= 'a' && c <= 'f') c = (char)(c - 32);
			var ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F');
			if (!ok) throw new ArgumentException("无效的 MAC 地址");
			sb.Append(c);
		}
		if (sb.Length < 6 || sb.Length > 12) throw new ArgumentException("无效的 MAC 地址");
		return sb.ToString();
	}

	/// <summary>解析厂家查询的 JSON 或纯文本。不访问网络。</summary>
	public static MacVendorInfo ParseMacVendor(string body, string mac) {
		var info = new MacVendorInfo { Mac = mac ?? "" };
		body = (body ?? "").Trim();
		if (body.Length == 0) {
			info.Error = "empty";
			return info;
		}
		if (body[0] != '{' && body[0] != '[') {
			info.Company = body;
			info.Ok = true;
			info.Text = mactext(info);
			return info;
		}
		try {
			using var doc = JsonDocument.Parse(body);
			var r = doc.RootElement;
			if (r.ValueKind == JsonValueKind.Array) {
				if (r.GetArrayLength() == 0) {
					info.Error = "未登记厂家";
					info.Text = mactext(info);
					return info;
				}
				r = r[0];
			}
			if (r.ValueKind != JsonValueKind.Object) {
				info.Error = "bad json";
				return info;
			}
			string get(params string[] keys) {
				foreach (var k in keys) {
					if (!r.TryGetProperty(k, out var v)) continue;
					if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? "";
				}
				return "";
			}
			if (r.TryGetProperty("found", out var found) && found.ValueKind == JsonValueKind.False) {
				info.Error = "未登记厂家";
				info.Text = mactext(info);
				return info;
			}
			var company = "";
			if (r.TryGetProperty("vendor", out var ven) && ven.ValueKind == JsonValueKind.Object
				&& ven.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String)
				company = nm.GetString() ?? "";
			if (company.Length == 0) company = get("company", "org");
			info.Company = company;
			info.Country = get("country");
			var a1 = get("addressL1");
			var a2 = get("addressL2");
			var a3 = get("addressL3");
			if (a1.Length + a2.Length + a3.Length > 0)
				info.Address = string.Join(" ", new[] { a1, a2, a3 }.Where(s => s.Length > 0));
			else
				info.Address = get("address");
			var typ = get("type");
			if (typ.StartsWith("MA-", StringComparison.Ordinal)) info.Block = typ;
			info.Ok = company.Length > 0;
			if (!info.Ok) info.Error = "未登记厂家";
			info.Text = mactext(info);
			return info;
		}
		catch {
			info.Error = "bad json";
			info.Text = body.Length > 400 ? body.Substring(0, 400) + "…" : body;
			return info;
		}
	}

	/// <summary>查 MAC 厂家。本地管理地址不访问网络。走程序里的 HTTP 代理。</summary>
	public static async Task<MacVendorInfo> MacVendor(string query, Action<string> line, CancellationToken ct) {
		var hex = MacHex(query);
		var show = macshow(hex);
		var local = macbit(hex, 0x02);
		var multi = macbit(hex, 0x01);
		line?.Invoke("MAC 厂家  " + show);
		if (local) {
			var info = new MacVendorInfo {
				Mac = show,
				Local = true,
				Multicast = multi,
				Error = "本地管理地址，没有厂家登记",
			};
			info.Text = mactext(info);
			line?.Invoke(info.Text);
			return info;
		}
		var urls = new[] {
			"https://macaddresslookup.app/api/v2/" + show,
			"https://api.macvendors.com/" + show,
			"https://macverify.com/api/v1/lookup?mac=" + show,
		};
		MacVendorInfo last = null;
		Exception lastEx = null;
		foreach (var url in urls) {
			ct.ThrowIfCancellationRequested();
			try {
				var body = await httpgetproxy(url, ct).ConfigureAwait(false);
				var info = ParseMacVendor(body, show);
				info.Local = false;
				info.Multicast = multi;
				info.Text = mactext(info);
				line?.Invoke(url);
				line?.Invoke(info.Text.Length > 0 ? info.Text : info.Error);
				if (info.Ok) return info;
				last = info;
			}
			catch (Exception ex) when (ex is not OperationCanceledException) {
				lastEx = ex;
				line?.Invoke(url + "  " + (ex.InnerException?.Message ?? ex.Message));
			}
		}
		line?.Invoke("未查到厂家");
		if (last != null) return last;
		return new MacVendorInfo {
			Mac = show,
			Multicast = multi,
			Error = lastEx?.InnerException?.Message ?? lastEx?.Message ?? "未登记厂家",
		};
	}

	static string macshow(string hex) {
		if ((hex.Length % 2) != 0) return hex;
		var sb = new StringBuilder();
		for (var i = 0; i < hex.Length; i += 2) {
			if (i > 0) sb.Append(':');
			sb.Append(hex, i, 2);
		}
		return sb.ToString();
	}

	static bool macbit(string hex, int bit) {
		var n = Convert.ToInt32(hex.Substring(0, 2), 16);
		return (n & bit) != 0;
	}

	static string mactext(MacVendorInfo info) {
		var sb = new StringBuilder();
		if (info.Mac.Length > 0) sb.AppendLine("MAC: " + info.Mac);
		if (info.Local) sb.AppendLine("本地管理地址，没有厂家登记");
		else if (info.Company.Length > 0) sb.AppendLine("厂家: " + info.Company);
		else if (info.Error.Length > 0) sb.AppendLine(info.Error);
		if (info.Country.Length > 0) sb.AppendLine("国家: " + info.Country);
		if (info.Address.Length > 0) sb.AppendLine("地址: " + info.Address);
		if (info.Block.Length > 0) sb.AppendLine("分配: " + info.Block);
		if (info.Multicast) sb.AppendLine("组播地址");
		return sb.ToString().TrimEnd();
	}

	/// <summary>usb.ids 里一条接口名。</summary>
	public sealed class UsbIfInfo {
		public string Id { get; set; } = "";
		public string Name { get; set; } = "";
	}

	/// <summary>USB ID 在 usb.ids 里的厂家、产品和型号。型号与产品都是设备名，接口另列。</summary>
	public sealed class UsbVendorInfo {
		public bool Ok { get; set; }
		public string Vid { get; set; } = "";
		public string Pid { get; set; } = "";
		public string Vendor { get; set; } = "";
		public string Product { get; set; } = "";
		public string Model { get; set; } = "";
		public List<UsbIfInfo> Interfaces { get; set; } = new();
		public string Error { get; set; } = "";
		public string Text { get; set; } = "";
	}

	/// <summary>解析 VID，或 VID 加 PID。接受 046d、046d:c52b、0x046d、VID_046D&PID_C52B。</summary>
	public static (string Vid, string Pid) UsbIds(string raw) {
		raw = (raw ?? "").Trim();
		if (raw.Length == 0) throw new ArgumentException("无效的 USB ID");
		var up = raw.ToUpperInvariant();
		var vidAt = up.IndexOf("VID_", StringComparison.Ordinal);
		if (vidAt >= 0) {
			var vid = takehex(up, vidAt + 4);
			var pidAt = up.IndexOf("PID_", StringComparison.Ordinal);
			var pid = pidAt >= 0 ? takehex(up, pidAt + 4) : "";
			if (vid.Length != 4 || (pid.Length != 0 && pid.Length != 4))
				throw new ArgumentException("无效的 USB ID");
			return (vid, pid);
		}
		var s = up.Replace("0X", "");
		var sb = new StringBuilder();
		foreach (var ch in s) {
			if (ch == ':' || ch == '-' || ch == '/' || ch == ' ' || ch == '\t' || ch == '_') continue;
			var ok = (ch >= '0' && ch <= '9') || (ch >= 'A' && ch <= 'F');
			if (!ok) throw new ArgumentException("无效的 USB ID");
			sb.Append(ch);
		}
		if (sb.Length != 4 && sb.Length != 8) throw new ArgumentException("无效的 USB ID");
		var all = sb.ToString();
		if (all.Length == 4) return (all, "");
		return (all.Substring(0, 4), all.Substring(4, 4));
	}

	/// <summary>在 usb.ids 文本里查一个 VID，以及可选的 PID。带上该产品的接口。不访问网络。</summary>
	public static UsbVendorInfo ParseUsbIds(string text, string vid, string pid) {
		var info = new UsbVendorInfo {
			Vid = (vid ?? "").ToUpperInvariant(),
			Pid = (pid ?? "").ToUpperInvariant(),
		};
		var wantVid = info.Vid.ToLowerInvariant();
		var wantPid = info.Pid.ToLowerInvariant();
		if (wantVid.Length != 4) {
			info.Error = "无效的 USB ID";
			info.Text = usbtext(info);
			return info;
		}
		var inVendor = false;
		var inDevice = false;
		foreach (var raw in (text ?? "").Replace("\r", "").Split('\n')) {
			if (raw.Length == 0 || raw[0] == '#') continue;
			var tabs = 0;
			while (tabs < raw.Length && raw[tabs] == '\t') tabs++;
			var line = raw.Substring(tabs);
			if (line.Length == 0) continue;
			if (tabs == 0) {
				if (inVendor) break;
				if (line.Length >= 6 && hex4(line, 0) && line[4] == ' '
					&& string.Equals(line.Substring(0, 4), wantVid, StringComparison.OrdinalIgnoreCase)) {
					info.Vendor = idname(line);
					inVendor = true;
					if (wantPid.Length == 0) break;
				}
				continue;
			}
			if (!inVendor) continue;
			if (tabs == 1) {
				if (inDevice) break;
				if (wantPid.Length == 0) continue;
				if (line.Length >= 6 && hex4(line, 0) && line[4] == ' '
					&& string.Equals(line.Substring(0, 4), wantPid, StringComparison.OrdinalIgnoreCase)) {
					info.Product = idname(line);
					info.Model = info.Product;
					inDevice = true;
				}
				continue;
			}
			if (!inDevice || !ifid(line, out var iid, out var iname)) continue;
			info.Interfaces.Add(new UsbIfInfo { Id = iid.ToUpperInvariant(), Name = iname });
		}
		info.Ok = info.Vendor.Length > 0;
		if (!info.Ok) info.Error = "未登记厂家";
		else if (wantPid.Length > 0 && info.Product.Length == 0) info.Error = "未登记产品";
		info.Text = usbtext(info);
		return info;
	}

	/// <summary>查 USB ID。名单来自 usb.ids，缓存在程序目录 tmp，超过 7 天再下载。</summary>
	public static async Task<UsbVendorInfo> UsbVendor(string query, Action<string> line, CancellationToken ct) {
		var id = UsbIds(query);
		var show = id.Pid.Length == 0 ? id.Vid : id.Vid + ":" + id.Pid;
		line?.Invoke("USB ID  " + show);
		string text;
		try {
			text = await loadusbids(line, ct).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is not OperationCanceledException) {
			var msg = ex.InnerException?.Message ?? ex.Message;
			line?.Invoke(msg);
			return new UsbVendorInfo { Vid = id.Vid, Pid = id.Pid, Error = msg, Text = msg };
		}
		var info = ParseUsbIds(text, id.Vid, id.Pid);
		line?.Invoke(info.Text);
		return info;
	}

	static string usbcached;

	static async Task<string> loadusbids(Action<string> line, CancellationToken ct) {
		if (usbcached != null && usbcached.Length > 10000) return usbcached;
		var path = Path.Combine(TmpStore.Root, "usb.ids");
		if (File.Exists(path)) {
			var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
			if (age < TimeSpan.FromDays(7)) {
				var cached = File.ReadAllText(path, Encoding.UTF8);
				if (cached.IndexOf("\n046d  ", StringComparison.OrdinalIgnoreCase) >= 0) {
					usbcached = cached;
					line?.Invoke("名单: " + path);
					return cached;
				}
			}
		}
		var urls = new[] {
			"http://www.linux-usb.org/usb.ids",
			"https://raw.githubusercontent.com/vcrhonek/hwdata/master/usb.ids",
		};
		Exception last = null;
		foreach (var url in urls) {
			ct.ThrowIfCancellationRequested();
			try {
				line?.Invoke(url);
				var body = await httpgetproxy(url, 30, ct).ConfigureAwait(false);
				if (body.IndexOf("\n046d  ", StringComparison.OrdinalIgnoreCase) < 0)
					throw new InvalidOperationException("usb.ids 内容不对");
				try {
					var part = path + ".part";
					File.WriteAllText(part, body, new UTF8Encoding(false));
					if (File.Exists(path)) File.Delete(path);
					File.Move(part, path);
				}
				catch { }
				usbcached = body;
				return body;
			}
			catch (Exception ex) when (ex is not OperationCanceledException) {
				last = ex;
				line?.Invoke(url + "  " + (ex.InnerException?.Message ?? ex.Message));
			}
		}
		throw new IOException(last?.InnerException?.Message ?? last?.Message ?? "无法下载 usb.ids");
	}

	static string takehex(string s, int i) {
		var n = 0;
		while (i + n < s.Length && n < 4) {
			var c = s[i + n];
			var ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F');
			if (!ok) break;
			n++;
		}
		return n == 0 ? "" : s.Substring(i, n);
	}

	static bool hex4(string s, int i) {
		if (i + 4 > s.Length) return false;
		for (var k = 0; k < 4; k++) {
			var c = s[i + k];
			var ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
			if (!ok) return false;
		}
		return true;
	}

	static string idname(string line) {
		var i = 4;
		while (i < line.Length && line[i] == ' ') i++;
		return i < line.Length ? line.Substring(i).Trim() : "";
	}

	static bool ifid(string line, out string id, out string name) {
		id = "";
		name = "";
		if (!hex4(line, 0) && line.Length >= 2) {
			var a = line[0];
			var b = line[1];
			var ha = (a >= '0' && a <= '9') || (a >= 'a' && a <= 'f') || (a >= 'A' && a <= 'F');
			var hb = (b >= '0' && b <= '9') || (b >= 'a' && b <= 'f') || (b >= 'A' && b <= 'F');
			if (!ha || !hb) return false;
			if (line.Length > 2 && line[2] != ' ' && line[2] != '\t') return false;
			id = line.Substring(0, 2);
			var i = 2;
			while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
			name = i < line.Length ? line.Substring(i).Trim() : "";
			return true;
		}
		return false;
	}

	static string usbtext(UsbVendorInfo info) {
		var sb = new StringBuilder();
		var id = info.Pid.Length == 0 ? info.Vid : info.Vid + ":" + info.Pid;
		if (id.Length > 0) sb.AppendLine("USB: " + id);
		if (info.Vendor.Length > 0) sb.AppendLine("厂家: " + info.Vendor);
		if (info.Product.Length > 0) {
			sb.AppendLine("产品: " + info.Product);
			sb.AppendLine("型号: " + (info.Model.Length > 0 ? info.Model : info.Product));
		}
		else if (info.Ok && info.Pid.Length > 0) {
			sb.AppendLine("产品: 未登记");
			sb.AppendLine("型号: 未登记");
		}
		else if (!info.Ok && info.Error.Length > 0) sb.AppendLine(info.Error);
		if (info.Interfaces != null) {
			foreach (var it in info.Interfaces) {
				if (it == null) continue;
				sb.AppendLine("接口: " + it.Id + "  " + it.Name);
			}
		}
		return sb.ToString().TrimEnd();
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

	static Task<string> httpgetproxy(string url, CancellationToken ct) =>
		httpgetproxy(url, 8, ct);

	static async Task<string> httpgetproxy(string url, int timeoutSec, CancellationToken ct) {
		var handler = HttpProxy.CreateHandler();
		handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSec) };
		http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ScreenKit");
		using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
		resp.EnsureSuccessStatusCode();
		return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
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
		if (done != task) {
			ct.ThrowIfCancellationRequested();
			throw new TimeoutException("dns timeout");
		}
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
