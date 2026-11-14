using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;

namespace ScreenKit;

/// <summary>WebSocket 帧编解码。客户端发出去的帧要掩码，服务端回的不掩码。</summary>
static class WsProto {
	public const int OP_CONT = 0, OP_TEXT = 1, OP_BIN = 2, OP_CLOSE = 8, OP_PING = 9, OP_PONG = 10;
	public const int MAXMSG = 32 * 1024 * 1024;
	const string GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

	public static string newkey() {
		var b = new byte[16];
		using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
		return Convert.ToBase64String(b);
	}

	public static string accept(string key) {
		using var sha = SHA1.Create();
		return Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + GUID)));
	}

	public static byte[] frame(int op, bool fin, byte[] payload, bool mask) {
		payload ??= Array.Empty<byte>();
		var len = payload.Length;
		using var ms = new MemoryStream(len + 14);
		ms.WriteByte((byte)((fin ? 0x80 : 0) | (op & 0x0F)));
		var mbit = mask ? 0x80 : 0x00;
		if (len < 126) ms.WriteByte((byte)(mbit | len));
		else if (len <= 0xFFFF) {
			ms.WriteByte((byte)(mbit | 126));
			ms.WriteByte((byte)(len >> 8));
			ms.WriteByte((byte)len);
		}
		else {
			ms.WriteByte((byte)(mbit | 127));
			var n = (ulong)len;
			for (var i = 7; i >= 0; i--) ms.WriteByte((byte)(n >> (i * 8)));
		}
		if (mask) {
			var m = new byte[4];
			using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(m);
			ms.Write(m, 0, 4);
			var buf = new byte[len];
			for (var i = 0; i < len; i++) buf[i] = (byte)(payload[i] ^ m[i & 3]);
			ms.Write(buf, 0, len);
		}
		else ms.Write(payload, 0, len);
		return ms.ToArray();
	}

	public static string opname(int op) => op switch {
		OP_TEXT => "文本",
		OP_BIN => "二进制",
		OP_PING => "ping",
		OP_PONG => "pong",
		OP_CLOSE => "关闭",
		_ => "续帧",
	};

	public static string closename(int code) => code switch {
		1000 => "正常关闭",
		1001 => "对端离开",
		1002 => "协议错误",
		1003 => "数据类型不支持",
		1007 => "数据无效",
		1008 => "违反策略",
		1009 => "消息过大",
		1010 => "需要扩展协商",
		1011 => "服务端内部错误",
		1012 => "服务重启",
		1013 => "稍后重试",
		1015 => "TLS 握手失败",
		_ => "",
	};

	/// <summary>可打印就给文本，否则给 HEX。</summary>
	public static string pretty(byte[] data, bool forcehex, int textlimit) {
		if (data == null || data.Length == 0) return "";
		if (!forcehex) {
			var ok = true;
			foreach (var b in data)
				if (b == 0 || b < 0x09 || (b > 0x0D && b < 0x20)) { ok = false; break; }
			if (ok) {
				var s = Encoding.UTF8.GetString(data);
				if (textlimit > 0 && s.Length > textlimit) s = s.Substring(0, textlimit) + " …";
				return s;
			}
		}
		return hex(data, forcehex ? 512 : 64);
	}

	public static string hex(byte[] data, int limit) {
		if (data == null || data.Length == 0) return "";
		var n = Math.Min(data.Length, limit);
		var sb = new StringBuilder(n * 3);
		for (var i = 0; i < n; i++) {
			if (i > 0) sb.Append(' ');
			sb.Append(data[i].ToString("X2"));
		}
		if (n < data.Length) sb.Append(" …(" + data.Length + " B)");
		return sb.ToString();
	}

	/// <summary>「AA BB CC」或「AABBCC」还原成字节；不合法返回 null。</summary>
	public static byte[] fromhex(string s) {
		if (string.IsNullOrWhiteSpace(s)) return Array.Empty<byte>();
		var sb = new StringBuilder(s.Length);
		foreach (var c in s) {
			if (char.IsWhiteSpace(c) || c == '-' || c == ',') continue;
			if (!Uri.IsHexDigit(c)) return null;
			sb.Append(c);
		}
		if (sb.Length % 2 != 0) return null;
		var outbuf = new byte[sb.Length / 2];
		for (var i = 0; i < outbuf.Length; i++)
			outbuf[i] = Convert.ToByte(sb.ToString(i * 2, 2), 16);
		return outbuf;
	}
}

/// <summary>一条 WebSocket 连接：后台线程读帧、自动回 pong、拼续帧，发帧加写锁。</summary>
sealed class WsConn : IDisposable {
	public readonly bool Mask;
	public string Peer = "";
	public string Proto = "";
	readonly Stream stream;
	readonly object wgate = new();
	Thread thread;
	volatile bool dead;
	byte[] acc = Array.Empty<byte>();
	int accn;
	byte[] frag = Array.Empty<byte>();
	int fragn;
	int fragop = -1;

	/// <summary>收到一条完整消息（op 为 1 或 2）。</summary>
	public event Action<WsConn, int, byte[]> Message;
	/// <summary>收到控制帧（ping / pong / close）。</summary>
	public event Action<WsConn, int, byte[]> Control;
	public event Action<WsConn, string> Ended;

	public bool Alive => !dead;

	public WsConn(Stream stream, bool mask, string peer) {
		this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
		Mask = mask;
		Peer = peer ?? "";
	}

	public void Start() {
		thread = new Thread(loop) { IsBackground = true, Name = "ws-conn" };
		thread.Start();
	}

	public bool Send(int op, byte[] data) {
		if (dead) return false;
		try {
			var f = WsProto.frame(op, true, data, Mask);
			lock (wgate) {
				stream.Write(f, 0, f.Length);
				stream.Flush();
			}
			return true;
		}
		catch (Exception ex) {
			die("发送失败：" + ex.Message);
			return false;
		}
	}

	public void Close(int code, string reason) {
		if (dead) return;
		var r = Encoding.UTF8.GetBytes(reason ?? "");
		var body = new byte[r.Length + 2];
		body[0] = (byte)((code >> 8) & 0xFF);
		body[1] = (byte)(code & 0xFF);
		if (r.Length > 0) Array.Copy(r, 0, body, 2, r.Length);
		Send(WsProto.OP_CLOSE, body);
		die(null);
	}

	void loop() {
		var buf = new byte[8192];
		while (!dead) {
			int n;
			try { n = stream.Read(buf, 0, buf.Length); }
			catch (Exception ex) { die("读取中断：" + ex.Message); return; }
			if (n <= 0) { die("对端关闭连接"); return; }
			if (!append(buf, n)) return;
			if (!parse()) return;
		}
	}

	bool append(byte[] src, int n) {
		if (accn + n > WsProto.MAXMSG) { die("接收缓冲过大"); return false; }
		if (acc.Length < accn + n) {
			var nb = new byte[Math.Max(4096, accn + n)];
			if (accn > 0) Buffer.BlockCopy(acc, 0, nb, 0, accn);
			acc = nb;
		}
		Buffer.BlockCopy(src, 0, acc, accn, n);
		accn += n;
		return true;
	}

	bool parse() {
		while (!dead) {
			if (accn < 2) return true;
			var op = acc[0] & 0x0F;
			var fin = (acc[0] & 0x80) != 0;
			var masked = (acc[1] & 0x80) != 0;
			long len = acc[1] & 0x7F;
			var hdr = 2;
			if (len == 126) {
				if (accn < 4) return true;
				len = (acc[2] << 8) | acc[3];
				hdr = 4;
			}
			else if (len == 127) {
				if (accn < 10) return true;
				len = 0;
				for (var i = 0; i < 8; i++) len = (len << 8) | acc[2 + i];
				hdr = 10;
			}
			if (len < 0 || len > WsProto.MAXMSG) { die("帧长度非法 " + len); return false; }
			var mstart = hdr;
			if (masked) hdr += 4;
			if (accn < hdr + len) return true;
			var data = new byte[len];
			if (len > 0) Buffer.BlockCopy(acc, hdr, data, 0, (int)len);
			if (masked) {
				for (var i = 0; i < data.Length; i++)
					data[i] = (byte)(data[i] ^ acc[mstart + (i & 3)]);
			}
			consume((int)(hdr + len));
			if (!onframe(op, fin, data)) return false;
		}
		return !dead;
	}

	void consume(int n) {
		accn -= n;
		if (accn > 0) Buffer.BlockCopy(acc, n, acc, 0, accn);
	}

	/// <summary>关闭帧前两字节是状态码，后面跟原因文字。</summary>
	static string closereason(byte[] data) {
		if (data == null || data.Length < 2) return "";
		var code = (data[0] << 8) | data[1];
		var name = WsProto.closename(code);
		var why = data.Length > 2 ? Encoding.UTF8.GetString(data, 2, data.Length - 2) : "";
		var text = name.Length > 0 ? $"{code} {name}" : code.ToString();
		return why.Length > 0 ? text + " · " + why : text;
	}

	bool onframe(int op, bool fin, byte[] data) {
		if (op == WsProto.OP_CLOSE) {
			Control?.Invoke(this, op, data);
			try { Send(WsProto.OP_CLOSE, data); } catch { }
			die(closereason(data));
			return false;
		}
		if (op == WsProto.OP_PING) {
			Control?.Invoke(this, op, data);
			Send(WsProto.OP_PONG, data);
			return true;
		}
		if (op == WsProto.OP_PONG) {
			Control?.Invoke(this, op, data);
			return true;
		}
		if (op != WsProto.OP_CONT && fragop < 0) fragop = op;
		if (fragn + data.Length > WsProto.MAXMSG) { die("消息过大"); return false; }
		if (frag.Length < fragn + data.Length) {
			var nb = new byte[Math.Max(4096, fragn + data.Length)];
			if (fragn > 0) Buffer.BlockCopy(frag, 0, nb, 0, fragn);
			frag = nb;
		}
		if (data.Length > 0) Buffer.BlockCopy(data, 0, frag, fragn, data.Length);
		fragn += data.Length;
		if (!fin) return true;
		var msg = new byte[fragn];
		if (fragn > 0) Buffer.BlockCopy(frag, 0, msg, 0, fragn);
		var chop = fragop < 0 ? op : fragop;
		fragn = 0;
		fragop = -1;
		Message?.Invoke(this, chop, msg);
		return true;
	}

	void die(string why) {
		if (dead) return;
		dead = true;
		try { stream.Close(); } catch { }
		try { Ended?.Invoke(this, why ?? ""); } catch { }
	}

	public void Dispose() => die(null);
}

/// <summary>WebSocket 客户端：自己拼握手，便于把请求响应原文给用户看；支持代理和自签证书。</summary>
static class WsClient {
	/// <summary>连不上返回错误文字，成功返回 ""。hs 里是握手请求和响应原文。</summary>
	public static string Connect(string url, string headers, string subprotocol, string origin,
		bool trustAll, int timeoutMs, out WsConn conn, out string hs) {
		conn = null;
		hs = "";
		var spec = (url ?? "").Trim();
		if (spec.Length == 0) return "请先填地址";
		if (!spec.Contains("://")) spec = "ws://" + spec;
		if (!Uri.TryCreate(spec, UriKind.Absolute, out var uri)) return "地址格式不对";
		var secure = uri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase);
		if (!secure && !uri.Scheme.Equals("ws", StringComparison.OrdinalIgnoreCase))
			return "只支持 ws:// 和 wss://";
		var port = uri.Port > 0 ? uri.Port : (secure ? 443 : 80);
		TcpClient tcp = null;
		Stream raw = null;
		var sb = new StringBuilder();
		try {
			var dialHost = uri.Host;
			var dialPort = port;
			var viaProxy = HttpProxy.Need(uri);
			var proxy = viaProxy ? HttpProxy.Instance.GetProxy(uri) : null;
			if (proxy != null) {
				dialHost = proxy.Host;
				dialPort = proxy.Port > 0 ? proxy.Port : 8080;
			}
			tcp = new TcpClient { NoDelay = true };
			if (!tcp.ConnectAsync(dialHost, dialPort).Wait(Math.Max(2000, timeoutMs)))
				return "连接超时";
			raw = tcp.GetStream();
			tcp.ReceiveTimeout = Math.Max(2000, timeoutMs);
			if (proxy != null) {
				var connect = $"CONNECT {uri.Host}:{port} HTTP/1.1\r\nHost: {uri.Host}:{port}\r\n\r\n";
				sb.Append("## 代理 CONNECT\r\n").Append(connect).Append("\r\n");
				write(raw, Encoding.ASCII.GetBytes(connect));
				var pr = readheaders(raw);
				if (!pr.StartsWith("HTTP/1.", StringComparison.OrdinalIgnoreCase) || pr.IndexOf(" 200", StringComparison.Ordinal) < 0)
					return "代理 CONNECT 失败：" + firstline(pr);
				sb.Append(pr).Append("\r\n");
			}
			if (secure) {
				var ssl = new SslStream(raw, false, (_, _, _, errors) => trustAll || errors == SslPolicyErrors.None);
				ssl.AuthenticateAsClient(uri.Host, null, SslProtocols.Tls12, true);
				raw = ssl;
			}
			var key = WsProto.newkey();
			var hosthdr = port == (secure ? 443 : 80) ? uri.Host : uri.Host + ":" + port;
			var req = new StringBuilder();
			req.Append("GET ").Append(string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery)
				.Append(" HTTP/1.1\r\n");
			req.Append("Host: ").Append(hosthdr).Append("\r\n");
			req.Append("Upgrade: websocket\r\nConnection: Upgrade\r\n");
			req.Append("Sec-WebSocket-Key: ").Append(key).Append("\r\n");
			req.Append("Sec-WebSocket-Version: 13\r\n");
			if (!string.IsNullOrWhiteSpace(origin)) req.Append("Origin: ").Append(origin.Trim()).Append("\r\n");
			if (!string.IsNullOrWhiteSpace(subprotocol))
				req.Append("Sec-WebSocket-Protocol: ").Append(subprotocol.Trim()).Append("\r\n");
			foreach (var line in (headers ?? "").Replace("\r\n", "\n").Split('\n')) {
				var t = line.Trim();
				if (t.Length == 0) continue;
				// 这几行上面按规范重写过，用户再填会冲突
				var low = t.ToLowerInvariant();
				if (low.StartsWith("host:") || low.StartsWith("upgrade:") || low.StartsWith("connection:")
					|| low.StartsWith("sec-websocket-key:") || low.StartsWith("sec-websocket-version:")) continue;
				req.Append(t).Append("\r\n");
			}
			req.Append("\r\n");
			sb.Append("## 握手请求\r\n").Append(req);
			write(raw, Encoding.ASCII.GetBytes(req.ToString()));
			var resp = readheaders(raw);
			sb.Append("## 握手响应\r\n").Append(resp).Append("\r\n");
			hs = sb.ToString();
			if (!resp.StartsWith("HTTP/1.1 101", StringComparison.OrdinalIgnoreCase)
				&& !resp.StartsWith("HTTP/1.0 101", StringComparison.OrdinalIgnoreCase))
				return "握手失败：" + firstline(resp);
			var acc = header(resp, "Sec-WebSocket-Accept");
			if (!string.Equals(acc, WsProto.accept(key), StringComparison.Ordinal)) return "握手校验失败：Accept 不对";
			var proto = header(resp, "Sec-WebSocket-Protocol");
			var peer = $"{uri.Host}:{port}";
			conn = new WsConn(raw, true, peer) { Proto = proto };
			tcp.ReceiveTimeout = 0;
			conn.Start();
			tcp = null;
			raw = null;
			return "";
		}
		catch (Exception ex) {
			hs = sb.ToString();
			return ex.Message;
		}
		finally {
			if (tcp != null) { try { tcp.Close(); } catch { } }
			if (raw != null) { try { raw.Dispose(); } catch { } }
		}
	}

	static void write(Stream s, byte[] data) {
		s.Write(data, 0, data.Length);
		s.Flush();
	}

	/// <summary>读到空行为止。只按字节读，握手头不会大。</summary>
	static string readheaders(Stream s) {
		using var ms = new MemoryStream();
		var last = 0;
		var one = new byte[1];
		while (ms.Length < 65536) {
			var n = s.Read(one, 0, 1);
			if (n <= 0) throw new EndOfStreamException("连接提前关闭");
			ms.WriteByte(one[0]);
			last = (last << 8) | one[0];
			if (last == 0x0D0A0D0A) return Encoding.UTF8.GetString(ms.ToArray());
		}
		throw new InvalidOperationException("握手头过长");
	}

	static string firstline(string headers) {
		var i = (headers ?? "").IndexOf("\r\n", StringComparison.Ordinal);
		return i < 0 ? headers ?? "" : headers.Substring(0, i);
	}

	static string header(string headers, string name) {
		if (string.IsNullOrEmpty(headers)) return "";
		foreach (var line in headers.Split(new[] { "\r\n" }, StringSplitOptions.None)) {
			var i = line.IndexOf(':');
			if (i <= 0) continue;
			if (!line.Substring(0, i).Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
			return line.Substring(i + 1).Trim();
		}
		return "";
	}
}

/// <summary>调试用的 WebSocket 服务端：接受升级请求、可回显收到的消息。</summary>
sealed class WsServer : IDisposable {
	sealed class Client {
		public WsConn Conn;
		public string Peer = "";
		public string Proto = "";
	}

	readonly List<Client> clients = new();
	readonly object gate = new();
	TcpListener listener;
	Thread accept;
	volatile bool running;

	public int Port { get; private set; }
	public bool Running => running;
	/// <summary>收到文本或二进制就原样回一份。</summary>
	public bool Echo { get; set; }

	public event Action<string> Log;
	public event Action<WsConn, int, byte[]> Message;
	public event Action<WsConn, int, byte[]> Control;
	public event Action ClientsChanged;

	public WsServer(int port) => Port = port;

	public void Start() {
		if (running) return;
		listener = new TcpListener(IPAddress.Any, Port);
		listener.Start();
		Port = ((IPEndPoint)listener.LocalEndpoint).Port;
		running = true;
		say("服务端已启动，端口 " + Port);
		accept = new Thread(acceptloop) { IsBackground = true, Name = "ws-accept" };
		accept.Start();
	}

	public void Stop() {
		if (!running) return;
		running = false;
		try { listener?.Stop(); } catch { }
		listener = null;
		List<Client> all;
		lock (gate) { all = new List<Client>(clients); clients.Clear(); }
		foreach (var c in all) {
			try { c.Conn.Close(1001, "服务端停止"); } catch { }
			try { c.Conn.Dispose(); } catch { }
		}
		try { accept?.Join(800); } catch { }
		say("服务端已停止");
		ClientsChanged?.Invoke();
	}

	public void Dispose() => Stop();

	/// <summary>在线客户端。Peer 就是「地址:端口」，界面上拿它当唯一标识。</summary>
	public List<(string Peer, string Proto)> Clients() {
		lock (gate) {
			var list = new List<(string, string)>(clients.Count);
			foreach (var c in clients) list.Add((c.Peer, c.Proto));
			return list;
		}
	}

	/// <summary>按对端给一个连接；peer 为空则返回第一个。</summary>
	public WsConn Find(string peer) {
		lock (gate) {
			foreach (var c in clients) {
				if (string.IsNullOrEmpty(peer) || c.Peer == peer) return c.Conn;
			}
			return null;
		}
	}

	public int Broadcast(int op, byte[] data, string only) {
		List<Client> all;
		lock (gate) all = new List<Client>(clients);
		var n = 0;
		foreach (var c in all) {
			if (!string.IsNullOrEmpty(only) && c.Peer != only) continue;
			if (!c.Conn.Alive) continue;
			if (c.Conn.Send(op, data)) n++;
		}
		return n;
	}

	void acceptloop() {
		while (running) {
			TcpClient tcp;
			try { tcp = listener.AcceptTcpClient(); }
			catch { break; }
			if (!running) { try { tcp.Close(); } catch { } break; }
			tcp.NoDelay = true;
			new Thread(() => serve(tcp)) { IsBackground = true, Name = "ws-handshake" }.Start();
		}
	}

	void serve(TcpClient tcp) {
		var peer = "";
		try {
			var ep = tcp.Client.RemoteEndPoint as IPEndPoint;
			peer = ep == null ? "?" : ep.Address + ":" + ep.Port;
			var ns = tcp.GetStream();
			tcp.ReceiveTimeout = 15000;
			var head = readheaders(ns);
			var lines = head.Replace("\r\n", "\n").Split('\n');
			var first = lines.Length > 0 ? lines[0] : "";
			var up = header(head, "Upgrade");
			var key = header(head, "Sec-WebSocket-Key");
			var proto = header(head, "Sec-WebSocket-Protocol");
			var ver = header(head, "Sec-WebSocket-Version");
			if (!first.StartsWith("GET ", StringComparison.OrdinalIgnoreCase)) {
				reject(ns, "只接受 WebSocket 升级请求");
				say(peer + " 拒绝：不是 GET");
				return;
			}
			if (!up.Equals("websocket", StringComparison.OrdinalIgnoreCase) || key.Length == 0) {
				reject(ns, "缺少 Upgrade 或 Sec-WebSocket-Key");
				say(peer + " 拒绝：缺 WebSocket 头");
				return;
			}
			if (ver.Length > 0 && ver != "13") {
				reject(ns, "只支持 Sec-WebSocket-Version: 13");
				say(peer + " 拒绝：版本 " + ver);
				return;
			}
			var pick = pickproto(proto);
			var resp = new StringBuilder();
			resp.Append("HTTP/1.1 101 Switching Protocols\r\n");
			resp.Append("Upgrade: websocket\r\nConnection: Upgrade\r\n");
			resp.Append("Sec-WebSocket-Accept: ").Append(WsProto.accept(key)).Append("\r\n");
			if (pick.Length > 0) resp.Append("Sec-WebSocket-Protocol: ").Append(pick).Append("\r\n");
			resp.Append("\r\n");
			var bytes = Encoding.ASCII.GetBytes(resp.ToString());
			ns.Write(bytes, 0, bytes.Length);
			ns.Flush();
			tcp.ReceiveTimeout = 0;
			var conn = new WsConn(ns, false, peer) { Proto = pick };
			conn.Message += (c, op, data) => {
				say($"← {peer} {WsProto.opname(op)} {data.Length} B");
				Message?.Invoke(c, op, data);
				if (Echo) c.Send(op, data);
			};
			conn.Control += (c, op, data) => Control?.Invoke(c, op, data);
			conn.Ended += (c, why) => {
				lock (gate) clients.RemoveAll(x => ReferenceEquals(x.Conn, c));
				if (!string.IsNullOrEmpty(why)) say($"客户端 {peer} 断开：{why}");
				else say($"客户端 {peer} 断开");
				ClientsChanged?.Invoke();
			};
			lock (gate) {
				if (!running) { conn.Dispose(); return; }
				clients.Add(new Client { Conn = conn, Peer = peer, Proto = pick });
			}
			say($"客户端连上 {peer}" + (pick.Length > 0 ? "  子协议 " + pick : ""));
			ClientsChanged?.Invoke();
			conn.Start();
			tcp = null;
		}
		catch (Exception ex) {
			say($"握手失败 {peer}：{ex.Message}");
		}
		finally {
			if (tcp != null) { try { tcp.Close(); } catch { } }
		}
	}

	void reject(NetworkStream ns, string why) {
		try {
			var body = Encoding.UTF8.GetBytes(why);
			var head = "HTTP/1.1 400 Bad Request\r\nContent-Type: text/plain; charset=utf-8\r\n"
				+ "Content-Length: " + body.Length + "\r\nConnection: close\r\n\r\n";
			var hb = Encoding.ASCII.GetBytes(head);
			ns.Write(hb, 0, hb.Length);
			ns.Write(body, 0, body.Length);
			ns.Flush();
		}
		catch { }
	}

	static string pickproto(string offered) {
		if (string.IsNullOrWhiteSpace(offered)) return "";
		foreach (var one in offered.Split(',')) {
			var t = one.Trim();
			if (t.Length > 0) return t;
		}
		return "";
	}

	static string readheaders(Stream s) {
		using var ms = new MemoryStream();
		var last = 0;
		var one = new byte[1];
		while (ms.Length < 65536) {
			var n = s.Read(one, 0, 1);
			if (n <= 0) throw new EndOfStreamException("对端提前关闭");
			ms.WriteByte(one[0]);
			last = (last << 8) | one[0];
			if (last == 0x0D0A0D0A) return Encoding.UTF8.GetString(ms.ToArray());
		}
		throw new InvalidOperationException("握手头过长");
	}

	static string header(string headers, string name) {
		foreach (var line in headers.Split(new[] { "\r\n" }, StringSplitOptions.None)) {
			var i = line.IndexOf(':');
			if (i <= 0) continue;
			if (!line.Substring(0, i).Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
			return line.Substring(i + 1).Trim();
		}
		return "";
	}

	void say(string msg) {
		try { Log?.Invoke(msg); } catch { }
	}

	/// <summary>--test-wsdbg 用：起服务端，本进程连上去收发一遍。</summary>
	public static int Test(Action<string> log, Action<string> err) {
		var bad = 0;
		WsServer srv = null;
		WsConn client = null;
		try {
			if (WsProto.accept("dGhlIHNhbXBsZSBub25jZQ==") != "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=") {
				err("wsdbg: Accept 计算不对");
				bad++;
			}
			else log("wsdbg: Accept 计算 OK");

			// 帧编解码往返：小帧 / 126 扩展 / 127 扩展各一次
			foreach (var size in new[] { 5, 300, 70000 }) {
				var src = new byte[size];
				for (var i = 0; i < size; i++) src[i] = (byte)(i * 7);
				var f = WsProto.frame(WsProto.OP_BIN, true, src, true);
				var back = decode(f, out var op, out var fin, out var payload, out var mask);
				if (!back || op != WsProto.OP_BIN || !fin || !mask || payload.Length != size) {
					err($"wsdbg: 帧往返失败 size={size}");
					bad++;
				}
				else if (size == 70000 && payload[69999] != src[69999]) {
					err("wsdbg: 帧内容不一致");
					bad++;
				}
			}
			if (bad == 0) log("wsdbg: 帧编解码 OK 5/300/70000 B");

			srv = new WsServer(0);
			srv.Log = m => log("srv: " + m);
			srv.Start();
			var port = srv.Port;
			if (port <= 0) { err("wsdbg: 服务端没起来"); return 1; }

			var got = new List<(int op, string text)>();
			srv.Message += (_, op, data) => {
				lock (got) got.Add((op, Encoding.UTF8.GetString(data)));
			};

			var e = WsClient.Connect($"ws://127.0.0.1:{port}/echo", "", "chat", "", true, 5000, out client, out var hs);
			if (e.Length > 0) { err("wsdbg: 客户端连不上 " + e); return 1; }
			log("wsdbg: 握手 OK" + (hs.Contains("101") ? "" : "（响应里没有 101？）"));
			if (client.Proto != "chat") {
				err("wsdbg: 子协议没回 chat，实际 " + (client.Proto ?? ""));
				bad++;
			}

			var recv = new List<(int op, string text)>();
			client.Message += (_, op, data) => {
				lock (recv) recv.Add((op, Encoding.UTF8.GetString(data)));
			};
			client.Control += (_, op, data) => {
				if (op == WsProto.OP_PONG) lock (recv) recv.Add((op, "pong:" + Encoding.UTF8.GetString(data)));
			};

			client.Send(WsProto.OP_TEXT, Encoding.UTF8.GetBytes("你好"));
			if (!wait(() => { lock (got) return got.Count > 0; }, 3000)) { err("wsdbg: 服务端没收到文本"); bad++; }
			else {
				lock (got) {
					if (got[0].op != WsProto.OP_TEXT || got[0].text != "你好") {
						err("wsdbg: 服务端收到的文本不对");
						bad++;
					}
					else log("wsdbg: 文本上行 OK 你好");
				}
			}

			// 2000 字节走的是 126 扩展长度，顺带覆盖长度头
			var big = new byte[2000];
			for (var i = 0; i < big.Length; i++) big[i] = (byte)i;
			client.Send(WsProto.OP_BIN, big);
			if (!wait(() => { lock (got) return got.Count > 1; }, 3000)) { err("wsdbg: 服务端没收到二进制"); bad++; }
			else log("wsdbg: 二进制上行 OK 2000 B");

			// ping 自动回 pong
			client.Send(WsProto.OP_PING, Encoding.UTF8.GetBytes("p1"));
			if (!wait(() => { lock (recv) return recv.Exists(x => x.op == WsProto.OP_PONG); }, 3000)) {
				err("wsdbg: ping 没回 pong");
				bad++;
			}
			else log("wsdbg: ping/pong OK");

			// 服务端主动推一条
			var sent = srv.Broadcast(WsProto.OP_TEXT, Encoding.UTF8.GetBytes("push"), null);
			if (sent != 1) { err("wsdbg: 推送人数不对 " + sent); bad++; }
			if (!wait(() => { lock (recv) return recv.Exists(x => x.op == WsProto.OP_TEXT && x.text == "push"); }, 3000)) {
				err("wsdbg: 客户端没收到推送");
				bad++;
			}
			else log("wsdbg: 服务端推送 OK");

			// 回显开关
			srv.Echo = true;
			client.Send(WsProto.OP_TEXT, Encoding.UTF8.GetBytes("echo-me"));
			if (!wait(() => { lock (recv) return recv.Exists(x => x.text == "echo-me"); }, 3000)) {
				err("wsdbg: 回显没收到");
				bad++;
			}
			else log("wsdbg: 回显 OK");

			var list = srv.Clients();
			if (list.Count != 1) { err("wsdbg: 在线客户端数不对 " + list.Count); bad++; }

			client.Close(1000, "bye");
			if (!wait(() => srv.Clients().Count == 0, 3000)) {
				err("wsdbg: 关闭后服务端还挂着客户端");
				bad++;
			}
			else log("wsdbg: 关闭 OK");
		}
		catch (Exception ex) {
			err("wsdbg: " + ex);
			bad++;
		}
		finally {
			try { client?.Dispose(); } catch { }
			try { srv?.Dispose(); } catch { }
		}
		log(bad == 0 ? "=== OK：WebSocket ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	static bool decode(byte[] f, out int op, out bool fin, out byte[] payload, out bool mask) {
		op = 0; fin = false; payload = Array.Empty<byte>(); mask = false;
		if (f == null || f.Length < 2) return false;
		op = f[0] & 0x0F;
		fin = (f[0] & 0x80) != 0;
		mask = (f[1] & 0x80) != 0;
		long len = f[1] & 0x7F;
		var hdr = 2;
		if (len == 126) {
			if (f.Length < 4) return false;
			len = (f[2] << 8) | f[3];
			hdr = 4;
		}
		else if (len == 127) {
			if (f.Length < 10) return false;
			len = 0;
			for (var i = 0; i < 8; i++) len = (len << 8) | f[2 + i];
			hdr = 10;
		}
		var mstart = hdr;
		if (mask) hdr += 4;
		if (f.Length < hdr + len) return false;
		payload = new byte[len];
		if (len > 0) Buffer.BlockCopy(f, hdr, payload, 0, (int)len);
		if (mask) {
			for (var i = 0; i < payload.Length; i++)
				payload[i] = (byte)(payload[i] ^ f[mstart + (i & 3)]);
		}
		return true;
	}

	static bool wait(Func<bool> cond, int ms) {
		var until = Environment.TickCount + ms;
		while (Environment.TickCount - until < 0) {
			if (cond()) return true;
			Thread.Sleep(20);
		}
		return cond();
	}
}
