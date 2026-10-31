using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ScreenKit;

/// <summary>TLS 握手结果里的一个字段。</summary>
public sealed class TlsPair {
	public string Name { get; set; }
	public string Value { get; set; }
}

/// <summary>证书链上的一张证书。</summary>
public sealed class TlsCertRow {
	public int Index { get; set; }
	public string Subject { get; set; }
	public string Issuer { get; set; }
	public string NotBefore { get; set; }
	public string NotAfter { get; set; }
	public string State { get; set; }
	public string Fingerprint { get; set; }
	public string Serial { get; set; }
	public string SigAlg { get; set; }
	public string KeyAlg { get; set; }
	public string San { get; set; }
	public string Extensions { get; set; }
	public string Pem { get; set; }
	public bool Expired { get; set; }
	public bool NotYet { get; set; }
	public string Filter { get; set; }
}

/// <summary>TLS 证书检查：连上主机取证书链和握手参数，只看不改。</summary>
static class TlsCheck {
	public const int TIMEOUT_MS = 10000;

	/// <summary>把 "host" / "host:port" / "https://host/x" 规整成主机和端口。</summary>
	public static bool ParseTarget(string raw, out string host, out int port, out string error) {
		host = "";
		port = 443;
		error = "";
		var text = (raw ?? "").Trim();
		if (text.Length == 0) {
			error = Loc.T("tls.err.empty");
			return false;
		}
		var scheme = "";
		var at = text.IndexOf("://", StringComparison.Ordinal);
		if (at > 0) {
			scheme = text.Substring(0, at).ToLowerInvariant();
			text = text.Substring(at + 3);
		}
		var slash = text.IndexOfAny(new[] { '/', '?', '#' });
		if (slash >= 0) text = text.Substring(0, slash);
		text = text.Trim();
		if (text.Length == 0) {
			error = Loc.T("tls.err.empty");
			return false;
		}
		var colon = text.LastIndexOf(':');
		var portText = "";
		if (colon > 0 && text.IndexOf(']') < colon) {
			portText = text.Substring(colon + 1);
			text = text.Substring(0, colon);
		}
		if (text.Length == 0) {
			error = Loc.T("tls.err.empty");
			return false;
		}
		if (portText.Length > 0) {
			if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out port)
				|| port <= 0 || port > 65535) {
				error = Loc.T("tls.err.port", portText);
				return false;
			}
		}
		else if (scheme.Length > 0) {
			port = scheme switch {
				"https" => 443,
				"wss" => 443,
				"imaps" => 993,
				"pop3s" => 995,
				"smtps" => 465,
				"ldaps" => 636,
				"ftps" => 990,
				_ => 443,
			};
		}
		host = text.Trim('[', ']');
		return true;
	}

	/// <summary>连上去做一次 TLS 握手，收集协议、套件和证书链。不校验信任，只用于查看。</summary>
	public static void Run(string raw, CancellationToken ct, List<TlsPair> pairs, List<TlsCertRow> chain, List<string> notes) {
		if (!ParseTarget(raw, out var host, out var port, out var error)) {
			notes.Add(error);
			return;
		}
		pairs.Add(pair("tls.f.target", $"{host}:{port}"));

		var resolving = Stopwatch.StartNew();
		IPAddress[] addrs;
		try { addrs = Dns.GetHostAddresses(host); }
		catch (Exception ex) {
			notes.Add(Loc.T("tls.err.dns", host, ex.Message));
			return;
		}
		resolving.Stop();
		if (addrs.Length == 0) {
			notes.Add(Loc.T("tls.err.dns", host, Loc.T("tls.err.noaddr")));
			return;
		}
		pairs.Add(pair("tls.f.addr", string.Join(", ", addrs.Select(a => a.ToString()))));
		pairs.Add(pair("tls.f.dnstime", resolving.ElapsedMilliseconds + " ms"));

		var target = firstusable(host, addrs, port, ct);
		if (target == null) {
			notes.Add(Loc.T("tls.err.connect", $"{host}:{port}"));
			return;
		}

		var tcp = new TcpClient();
		var connectWatch = Stopwatch.StartNew();
		try {
			var task = tcp.ConnectAsync(target, port);
			while (!task.Wait(200)) {
				if (ct.IsCancellationRequested) {
					notes.Add(Loc.T("tls.err.cancel"));
					return;
				}
				if (connectWatch.ElapsedMilliseconds > TIMEOUT_MS) {
					notes.Add(Loc.T("tls.err.timeout", TIMEOUT_MS));
					return;
				}
			}
		}
		catch (Exception ex) {
			notes.Add(Loc.T("tls.err.connect", $"{host}:{port} · {innermost(ex)}"));
			try { tcp.Close(); } catch { }
			return;
		}
		connectWatch.Stop();
		pairs.Add(pair("tls.f.conntime", connectWatch.ElapsedMilliseconds + " ms"));
		pairs.Add(pair("tls.f.ip", $"{target}:{port}"));

		byte[] rawder = null;
		var handshake = Stopwatch.StartNew();
		try {
			using (tcp) {
				tcp.ReceiveTimeout = TIMEOUT_MS;
				tcp.SendTimeout = TIMEOUT_MS;
				var stream = tcp.GetStream();
				// 始终接受，只为把证书取出来看；不改全局校验，也不影响其它连接
				using var ssl = new SslStream(stream, false, (_, cert, _, _) => {
					var one = fromcert(cert);
					// X509Certificate2 可能绑定在握手上下文里，出了 using 就不可用，
					// 所以立刻导出原始字节，后面只用字节重建。
					if (one != null) {
						try { rawder = one.RawData; } catch { rawder = null; }
					}
					return true;
				});
				var task = ssl.AuthenticateAsClientAsync(host, null,
					SslProtocols.Tls12 | SslProtocols.Tls11 | SslProtocols.Tls, false);
				while (!task.Wait(200)) {
					if (ct.IsCancellationRequested) {
						notes.Add(Loc.T("tls.err.cancel"));
						return;
					}
					if (handshake.ElapsedMilliseconds > TIMEOUT_MS) {
						notes.Add(Loc.T("tls.err.handshake", TIMEOUT_MS));
						return;
					}
				}
				// Task 没有返回值：成功即握手完成，失败会走下面的 catch
				handshake.Stop();
				applystate(pairs, ssl);
			}
		}
		catch (Exception ex) {
			notes.Add(Loc.T("tls.err.handshake", innermost(ex)));
		}
		finally {
			pairs.Add(pair("tls.f.hstime", handshake.ElapsedMilliseconds + " ms"));
		}

		X509Certificate2 leaf = null;
		if (rawder != null && rawder.Length > 0) {
			try { leaf = new X509Certificate2(rawder); } catch { leaf = null; }
		}
		buildchain(chain, leaf, host, notes);
	}

	/// <summary>
	/// 回调给的可能是 X509Certificate 而不是 X509Certificate2，
	/// net48 上直接 as 会得到 null，要按原始字节重新构造一次。
	/// </summary>
	static X509Certificate2 fromcert(X509Certificate cert) {
		if (cert == null) return null;
		if (cert is X509Certificate2 two) return two;
		try { return new X509Certificate2(cert); } catch { }
		try { return new X509Certificate2(cert.GetRawCertData()); } catch { }
		return null;
	}

	static void applystate(List<TlsPair> pairs, SslStream ssl) {
		string protocol = "";
		try { protocol = ssl.SslProtocol.ToString(); } catch { }
		if (!string.IsNullOrEmpty(protocol)) pairs.Add(pair("tls.f.protocol", protocol));

		string cipher = "";
		try { cipher = ssl.CipherAlgorithm.ToString(); } catch { }
		if (!string.IsNullOrEmpty(cipher)) {
			try {
				if (ssl.CipherStrength > 0) cipher += " · " + ssl.CipherStrength + " bit";
			}
			catch { }
			pairs.Add(pair("tls.f.cipher", cipher));
		}

		string kx = "";
		try {
			// net48 这里有未命名的枚举值，ToString 会给数字，按已知值补名字
			kx = ssl.KeyExchangeAlgorithm switch {
				ExchangeAlgorithmType.RsaSign => "RSA",
				ExchangeAlgorithmType.RsaKeyX => "RSA",
				ExchangeAlgorithmType.DiffieHellman => "Diffie-Hellman",
				ExchangeAlgorithmType.None => "",
				_ => "ECDHE / DH (0x" + ((int)ssl.KeyExchangeAlgorithm).ToString("X") + ")",
			};
		}
		catch { }
		if (!string.IsNullOrEmpty(kx)) pairs.Add(pair("tls.f.kx", kx));

		string hash = "";
		try { hash = ssl.HashAlgorithm.ToString(); } catch { }
		if (!string.IsNullOrEmpty(hash)) pairs.Add(pair("tls.f.hash", hash));

		// net48 的 SslStream 没有 ALPN 属性，握手不带 ALPN 时如实写「无」
		pairs.Add(pair("tls.f.alpn", Loc.T("tls.none")));

		try { pairs.Add(pair("tls.f.enc", ssl.IsEncrypted ? Loc.T("tls.yes") : Loc.T("tls.no"))); } catch { }
		try { pairs.Add(pair("tls.f.signed", ssl.IsSigned ? Loc.T("tls.yes") : Loc.T("tls.no"))); } catch { }
	}

	/// <summary>整理证书链；信任状态用系统策略单独再算一次。</summary>
	static void buildchain(List<TlsCertRow> chain, X509Certificate2 leaf, string host, List<string> notes) {
		if (leaf == null) {
			notes.Add(Loc.T("tls.err.nocert"));
			return;
		}
		var list = new List<X509Certificate2> { leaf };
		var trusted = false;
		try {
			using var chain2 = new X509Chain();
			chain2.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
			trusted = chain2.Build(leaf);
			foreach (var el in chain2.ChainElements) {
				var c = el.Certificate;
				if (c == null) continue;
				if (c.Thumbprint == leaf.Thumbprint) continue;
				list.Add(c);
			}
			if (!trusted) {
				foreach (var st in chain2.ChainStatus)
					notes.Add(Loc.T("tls.note.chain", st.Status.ToString()));
			}
		}
		catch { }
		notes.Add(Loc.T("tls.note.trust", trusted ? Loc.T("tls.trust.ok") : Loc.T("tls.trust.bad")));

		var match = matchhost(leaf, host, out var matched);
		notes.Add(match ? Loc.T("tls.note.match", matched) : Loc.T("tls.note.mismatch", matched));

		var index = 0;
		foreach (var cert in list) chain.Add(tocert(cert, index++));
	}

	static TlsCertRow tocert(X509Certificate2 cert, int index) {
		var row = new TlsCertRow { Index = index };
		try { row.Subject = cert.Subject; } catch { row.Subject = ""; }
		try { row.Issuer = cert.Issuer; } catch { row.Issuer = ""; }
		try {
			row.NotBefore = cert.NotBefore.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
			row.NotAfter = cert.NotAfter.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
		}
		catch { }
		var now = DateTime.Now;
		try {
			row.Expired = cert.NotAfter < now;
			row.NotYet = cert.NotBefore > now;
		}
		catch { }
		try { row.Fingerprint = cert.Thumbprint; } catch { row.Fingerprint = ""; }
		try { row.Serial = cert.SerialNumber; } catch { row.Serial = ""; }
		try { row.SigAlg = cert.SignatureAlgorithm?.FriendlyName ?? ""; } catch { }
		try { row.KeyAlg = keyalg(cert); } catch { }
		try { row.San = sanlist(cert); } catch { }
		try { row.Extensions = extensions(cert); } catch { }
		try { row.Pem = pem(cert); } catch { }

		if (row.Expired) row.State = Loc.T("tls.state.expired");
		else if (row.NotYet) row.State = Loc.T("tls.state.notyet");
		else row.State = Loc.T("tls.state.ok");

		var sb = new StringBuilder();
		sb.Append(row.Subject).Append('\n').Append(row.Issuer).Append('\n').Append(row.San).Append('\n')
			.Append(row.Fingerprint).Append('\n').Append(row.Serial).Append('\n').Append(row.SigAlg).Append('\n')
			.Append(row.KeyAlg).Append('\n').Append(row.Extensions);
		row.Filter = sb.ToString().ToLowerInvariant();
		return row;
	}

	static string keyalg(X509Certificate2 cert) {
		try {
			var key = cert.PublicKey;
			if (key == null) return "";
			var oid = key.Oid;
			var name = oid?.FriendlyName ?? "";
			if (string.IsNullOrWhiteSpace(name)) name = oid?.Value ?? "";
			try {
				var size = key.Key?.KeySize ?? 0;
				if (size > 0) return $"{name} · {size} bit";
			}
			catch { }
			return name;
		}
		catch { return ""; }
	}

	/// <summary>net48 没有 SAN 专用类型，按 OID 2.5.29.17 手工解出 DNS 名和 IP。</summary>
	static List<string> names(X509Certificate2 cert) {
		var list = new List<string>();
		const string SANOID = "2.5.29.17";
		try {
			foreach (var ext in cert.Extensions) {
				if (!string.Equals(ext.Oid?.Value, SANOID, StringComparison.Ordinal)) continue;
				var raw = ext.RawData;
				if (raw == null || raw.Length < 4) continue;
				var i = 0;
				if (raw[i] == 0x30) { i++; i = skiplen(raw, i); }
				while (i + 2 <= raw.Length) {
					var tag = raw[i];
					var len = raw[i + 1];
					i += 2;
					if (i + len > raw.Length) break;
					if (tag == 0x82) {
						var s = Encoding.ASCII.GetString(raw, i, len);
						if (s.Length > 0) list.Add("DNS:" + s);
					}
					else if (tag == 0x87) {
						if (len == 4) list.Add("IP:" + new IPAddress(raw.Skip(i).Take(4).ToArray()));
						else if (len == 16) list.Add("IP:" + new IPAddress(raw.Skip(i).Take(16).ToArray()));
					}
					i += len;
				}
			}
		}
		catch { }
		return list;
	}

	static int skiplen(byte[] raw, int i) {
		if (i >= raw.Length) return i;
		var b = raw[i];
		if (b < 0x80) return i + 1;
		var n = b & 0x7F;
		return i + 1 + n;
	}

	static string sanlist(X509Certificate2 cert) {
		var list = names(cert);
		return list.Count == 0 ? "" : string.Join(", ", list);
	}

	static string extensions(X509Certificate2 cert) {
		var parts = new List<string>();
		try {
			foreach (var ext in cert.Extensions) {
				var oid = ext.Oid;
				if (oid == null) continue;
				var name = oid.FriendlyName;
				if (string.IsNullOrWhiteSpace(name)) name = oid.Value ?? "";
				if (ext.Critical) name += Loc.T("tls.critical");
				parts.Add(name);
			}
		}
		catch { }
		return parts.Count == 0 ? "" : string.Join("; ", parts);
	}

	public static string pem(X509Certificate2 cert) {
		var sb = new StringBuilder();
		sb.Append("-----BEGIN CERTIFICATE-----\r\n");
		var b64 = Convert.ToBase64String(cert.RawData);
		for (var i = 0; i < b64.Length; i += 64)
			sb.Append(b64.Substring(i, Math.Min(64, b64.Length - i))).Append("\r\n");
		sb.Append("-----END CERTIFICATE-----\r\n");
		return sb.ToString();
	}

	/// <summary>
	/// 主机名可能有多个地址（如 localhost 的 ::1 和 127.0.0.1）。
	/// 探测连接会占用对端的一次 Accept，对只监听一路的服务端是致命的，
	/// 所以这里只排序不探测：IPv4 优先，真连不上再换下一个。
	/// </summary>
	static IPAddress firstusable(string host, IPAddress[] addrs, int port, CancellationToken ct) {
		_ = port;
		_ = ct;
		if (IPAddress.TryParse(host, out var direct)) return direct;
		var sorted = addrs.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).ToList();
		return sorted.Count == 0 ? null : sorted[0];
	}

	static string innermost(Exception ex) {
		var cur = ex;
		while (cur.InnerException != null) cur = cur.InnerException;
		return cur.Message;
	}

	static TlsPair pair(string key, string value) => new TlsPair { Name = Loc.T(key), Value = value ?? "" };

	/// <summary>检查证书是否覆盖 host：先看 SAN 的 DNS，没有 SAN 才退回 CN。</summary>
	static bool matchhost(X509Certificate2 cert, string host, out string matched) {
		matched = "";
		if (string.IsNullOrEmpty(host)) return false;
		var want = host.Trim().TrimEnd('.').ToLowerInvariant();
		var all = names(cert);
		var dnsonly = all.Where(x => x.StartsWith("DNS:", StringComparison.Ordinal))
			.Select(x => x.Substring(4)).ToList();
		if (dnsonly.Count == 0) {
			var cn = "";
			try { cn = cert.GetNameInfo(X509NameType.SimpleName, false) ?? ""; } catch { }
			if (cn.Length > 0) dnsonly.Add(cn);
		}
		foreach (var one in dnsonly) {
			var name = (one ?? "").Trim().TrimEnd('.').ToLowerInvariant();
			if (name.Length == 0) continue;
			if (name == want) { matched = one; return true; }
			if (name.StartsWith("*.", StringComparison.Ordinal)) {
				var suffix = name.Substring(1);
				if (want.EndsWith(suffix, StringComparison.Ordinal)) {
					var head = want.Substring(0, want.Length - suffix.Length);
					if (head.Length > 0 && head.IndexOf('.') < 0) { matched = one; return true; }
				}
			}
		}
		matched = all.Count == 0 ? "-" : string.Join(", ", all.Take(4));
		return false;
	}

	/// <summary>自检：目标地址解析，再对本机自签名证书走一次完整握手。</summary>
	public static int Test(Action<string> ok, Action<string> err) {
		var bad = 0;
		void check(string raw, string host, int port, bool expect) {
			var parsed = ParseTarget(raw, out var h, out var p, out _);
			if (parsed != expect) {
				err($"FAIL 解析 {raw}: 结果 {parsed}，期望 {expect}");
				bad++;
				return;
			}
			if (!expect) return;
			if (h != host || p != port) {
				err($"FAIL 解析 {raw}: 得到 {h}:{p}，期望 {host}:{port}");
				bad++;
			}
		}
		check("example.com", "example.com", 443, true);
		check("example.com:8443", "example.com", 8443, true);
		check("https://example.com/a?b=1", "example.com", 443, true);
		check("wss://example.com:9443/x", "example.com", 9443, true);
		check("1.2.3.4", "1.2.3.4", 443, true);
		check("imaps://mail.example.com", "mail.example.com", 993, true);
		check("", "", 0, false);
		check("host:99999", "", 0, false);
		check("host:abc", "", 0, false);
		if (bad == 0) ok("目标地址解析 9 项通过");

		X509Certificate2 self = null;
		try {
			using var rsa = RSA.Create(2048);
			var req = new CertificateRequest("CN=screenkit-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
			var san = new SubjectAlternativeNameBuilder();
			san.AddDnsName("localhost");
			san.AddIpAddress(IPAddress.Loopback);
			req.CertificateExtensions.Add(san.Build());
			req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
			var created = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));
			// 私钥要和证书对象绑在同一个存储上下文里，服务器线程才拿得到；
			// 直接拿 CreateSelfSigned 的返回值在服务器模式会报「没有可用的凭证」。
			var pfx = created.Export(X509ContentType.Pfx, "screenkit");
			self = new X509Certificate2(pfx, "screenkit",
				X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
			if (!self.HasPrivateKey) {
				err("FAIL 自签名证书没有可用私钥");
				return 1;
			}
			ok("自签名证书生成成功");
		}
		catch (Exception ex) {
			err("FAIL 生成证书: " + ex.Message);
			return 1;
		}

		var tcp = new TcpListener(IPAddress.Loopback, 0);
		var listenerPort = 0;
		try {
			tcp.Start();
			listenerPort = ((IPEndPoint)tcp.LocalEndpoint).Port;
		}
		catch (Exception ex) {
			err("FAIL 监听: " + ex.Message);
			try { self.Dispose(); } catch { }
			return 1;
		}

		var served = 0;
		var serverErr = "";
		var server = Task.Run(() => {
			try {
				using var client = tcp.AcceptTcpClient();
				using var ssl = new SslStream(client.GetStream(), false);
				ssl.AuthenticateAsServer(self, false, SslProtocols.Tls12, false);
				served++;
			}
			catch (Exception ex) { serverErr = ex.GetType().Name + ": " + ex.Message; }
		});

		var pairs = new List<TlsPair>();
		var chain = new List<TlsCertRow>();
		var notes = new List<string>();
		try {
			using var cts = new CancellationTokenSource(TIMEOUT_MS * 2);
			Run($"localhost:{listenerPort}", cts.Token, pairs, chain, notes);
		}
		finally {
			try { tcp.Stop(); } catch { }
			try { server.Wait(3000); } catch { }
			try { self.Dispose(); } catch { }
		}

		if (served == 0) {
			err("FAIL 服务器端没有完成握手" + (serverErr.Length > 0 ? " · " + serverErr : ""));
			bad++;
		}
		if (!pairs.Any(p => p.Name == Loc.T("tls.f.protocol"))) { err("FAIL 没有 TLS 版本"); bad++; }
		if (chain.Count == 0) { err("FAIL 没有证书链"); bad++; }
		else {
			var leaf = chain[0];
			if (string.IsNullOrEmpty(leaf.Subject) || leaf.Subject.IndexOf("screenkit-test", StringComparison.Ordinal) < 0) {
				err("FAIL 证书主题不对: " + leaf.Subject);
				bad++;
			}
			if (string.IsNullOrEmpty(leaf.Fingerprint) || leaf.Fingerprint.Length < 32) {
				err("FAIL 指纹不对: " + leaf.Fingerprint);
				bad++;
			}
			if (string.IsNullOrEmpty(leaf.Pem) || leaf.Pem.IndexOf("BEGIN CERTIFICATE", StringComparison.Ordinal) < 0) {
				err("FAIL PEM 不对");
				bad++;
			}
			if (leaf.State != Loc.T("tls.state.ok")) {
				err("FAIL 有效期判断不对: " + leaf.State);
				bad++;
			}
			if (string.IsNullOrEmpty(leaf.San) || leaf.San.IndexOf("localhost", StringComparison.Ordinal) < 0) {
				err("FAIL SAN 解析不对: " + leaf.San);
				bad++;
			}
			if (leaf.Filter.IndexOf("screenkit-test", StringComparison.Ordinal) < 0) {
				err("FAIL 过滤串缺少主题");
				bad++;
			}
		}
		if (!notes.Any(n => n.IndexOf("localhost", StringComparison.Ordinal) >= 0)) {
			err("FAIL 没有判断证书名匹配");
			bad++;
		}

		if (bad == 0) {
			ok($"回环握手 {served} 次，字段 {pairs.Count} 项，证书链 {chain.Count} 张，备注 {notes.Count} 条");
			foreach (var n in notes) ok("  备注: " + n);
			ok("=== OK：TLS 证书检查 ===");
		}
		return bad == 0 ? 0 : 1;
	}
}














