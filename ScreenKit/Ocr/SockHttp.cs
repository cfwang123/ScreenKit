using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ScreenKit;

/// <summary>TCP 监听。不用独占绑定：taskkill 后独占套接字会留在已退出的进程上，再绑就是 10013。</summary>
static class TcpListen {
	const uint HANDLE_FLAG_INHERIT = 1;

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

	public static TcpListener Open(IPAddress addr, int port, bool dual) {
		var l = new TcpListener(addr, port);
		try {
			l.Server.ExclusiveAddressUse = false;
			l.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
			if (dual)
				l.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
			try { SetHandleInformation(l.Server.Handle, HANDLE_FLAG_INHERIT, 0); } catch { }
			l.Start();
			return l;
		}
		catch {
			try { l.Stop(); } catch { }
			try { l.Server.Close(); } catch { }
			throw;
		}
	}
}

/// <summary>
/// 自管 HTTP/1.1。监听用普通 TCP 套接字，不经过 HTTP.sys，局域网不需要管理员和 URL ACL。
/// </summary>
sealed class SockHttpServer {
	readonly object gate = new();
	TcpListener tcp;
	volatile bool running;
	volatile ManualResetEvent probe;
	Action<SockCtx> onrequest;
	public int Port { get; private set; }

	public void Start(string host, int port, Action<SockCtx> onRequest) {
		if (onRequest == null) throw new ArgumentNullException(nameof(onRequest));
		lock (gate) {
			stopcore();
			onrequest = onRequest;
			Exception last = null;
			for (var i = 0; i < 16; i++) {
				var p = port + i;
				if (p > 65535) break;
				var tries = i == 0 ? 4 : 1;
				for (var n = 0; n < tries; n++) {
					try {
						if (take(host, p)) return;
						last = new IOException($"端口 {p} 已绑定但连接进了残留监听");
						break;
					}
					catch (Exception ex) when (busy(ex)) {
						last = ex;
					}
					if (n + 1 < tries) Thread.Sleep(80);
				}
			}
			Port = 0;
			throw last ?? new IOException($"端口 {port} 无法监听");
		}
	}

	public void Stop() {
		lock (gate) stopcore();
	}

	bool take(string host, int port) {
		var l = bind(host, port);
		var ev = new ManualResetEvent(false);
		tcp = l;
		Port = port;
		probe = ev;
		running = true;
		_ = Task.Run(() => acceptloop(l));
		var own = false;
		try {
			using var c = new TcpClient();
			var ar = c.BeginConnect(IPAddress.Loopback, port, null, null);
			if (!ar.AsyncWaitHandle.WaitOne(500)) {
				releaseprobe(ev);
				stopcore();
				return false;
			}
			c.EndConnect(ar);
			own = ev.WaitOne(500);
		}
		catch { own = false; }
		releaseprobe(ev);
		if (own) return true;
		stopcore();
		return false;
	}

	void releaseprobe(ManualResetEvent ev) {
		if (probe == ev) probe = null;
		try { ev?.Dispose(); } catch { }
	}

	void stopcore() {
		running = false;
		probe = null;
		Port = 0;
		var l = tcp;
		tcp = null;
		if (l == null) return;
		try { l.Stop(); } catch { }
		try { l.Server.Close(); } catch { }
	}

	static bool busy(Exception ex) {
		for (var e = ex; e != null; e = e.InnerException) {
			if (e is SocketException se && (se.SocketErrorCode == SocketError.AddressAlreadyInUse
				|| se.SocketErrorCode == SocketError.AccessDenied))
				return true;
		}
		return false;
	}

	static TcpListener bind(string host, int port) {
		var lan = host == "+" || host == "*" || host == "0.0.0.0";
		if (!lan) return TcpListen.Open(IPAddress.Loopback, port, false);
		try {
			return TcpListen.Open(IPAddress.IPv6Any, port, true);
		}
		catch (SocketException) { }
		return TcpListen.Open(IPAddress.Any, port, false);
	}

	void acceptloop(TcpListener l) {
		while (running) {
			TcpClient c;
			try { c = l.AcceptTcpClient(); }
			catch (ObjectDisposedException) { break; }
			catch (SocketException) {
				if (!running) break;
				continue;
			}
			catch {
				if (!running) break;
				continue;
			}
			try { probe?.Set(); } catch { }
			_ = Task.Run(() => serve(c));
		}
	}

	void serve(TcpClient c) {
		try {
			c.NoDelay = true;
			try { c.ReceiveTimeout = 15000; } catch { }
			try { c.SendTimeout = 120000; } catch { }
			var ns = c.GetStream();
			var ctx = readctx(ns, c.Client.RemoteEndPoint as IPEndPoint);
			if (ctx == null) {
				write400(ns);
				return;
			}
			try { c.ReceiveTimeout = 0; } catch { }
			try { onrequest?.Invoke(ctx); }
			catch { }
			finally {
				if (!ctx.Upgraded && !ctx.RawTaken) {
					try { ctx.Response.Close(); } catch { }
				}
			}
		}
		catch { }
		finally {
			try { c.Close(); } catch { }
		}
	}

	static void write400(Stream ns) {
		var b = Encoding.ASCII.GetBytes(
			"HTTP/1.1 400 Bad Request\r\nConnection: close\r\nContent-Length: 0\r\n\r\n");
		try { ns.Write(b, 0, b.Length); } catch { }
	}

	static SockCtx readctx(NetworkStream ns, IPEndPoint remote) {
		const int MAXHDR = 65536;
		var buf = new byte[MAXHDR];
		var n = 0;
		var end = -1;
		while (n < MAXHDR) {
			int r;
			try { r = ns.Read(buf, n, Math.Min(1024, MAXHDR - n)); }
			catch { return null; }
			if (r <= 0) return null;
			n += r;
			if (n >= 1 && buf[0] == 0x16) return null;
			end = hdrend(buf, n);
			if (end >= 0) break;
		}
		if (end < 0) return null;
		var text = Encoding.GetEncoding("iso-8859-1").GetString(buf, 0, end);
		var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
		if (lines.Length == 0) return null;
		var parts = lines[0].Split(new[] { ' ' }, 3);
		if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0) return null;
		var method = parts[0];
		var target = parts[1];
		var headers = new SockHeaders();
		for (var i = 1; i < lines.Length; i++) {
			var line = lines[i];
			if (line.Length == 0) continue;
			var colon = line.IndexOf(':');
			if (colon <= 0) continue;
			headers.Add(line.Substring(0, colon).Trim(), line.Substring(colon + 1).Trim());
		}
		byte[] leftover;
		if (n > end) {
			leftover = new byte[n - end];
			Buffer.BlockCopy(buf, end, leftover, 0, n - end);
		}
		else leftover = Array.Empty<byte>();
		var ctype = headers["Content-Type"];
		var up = headers["Upgrade"] ?? "";
		var conn = headers["Connection"] ?? "";
		var ws = up.IndexOf("websocket", StringComparison.OrdinalIgnoreCase) >= 0
			&& conn.IndexOf("Upgrade", StringComparison.OrdinalIgnoreCase) >= 0;
		long clen = 0;
		var hasLen = false;
		var cls = headers["Content-Length"];
		if (!string.IsNullOrEmpty(cls)) {
			if (!long.TryParse(cls, NumberStyles.Integer, CultureInfo.InvariantCulture, out clen) || clen < 0)
				return null;
			hasLen = true;
		}
		var chunked = !hasLen && (headers["Transfer-Encoding"] ?? "")
			.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0;
		var expect = headers["Expect"] ?? "";
		if (expect.IndexOf("100-continue", StringComparison.OrdinalIgnoreCase) >= 0 && !ws) {
			var cont = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
			try { ns.Write(cont, 0, cont.Length); } catch { return null; }
		}
		Uri uri;
		try { uri = makeuri(target, headers["Host"]); }
		catch { return null; }
		Stream input;
		byte[] prefetch;
		if (ws) {
			input = Stream.Null;
			prefetch = leftover;
		}
		else if (chunked) {
			input = new SockChunk(ns, leftover);
			prefetch = Array.Empty<byte>();
			clen = -1;
		}
		else {
			input = new SockIn(ns, leftover, clen);
			prefetch = Array.Empty<byte>();
		}
		var req = new SockReq {
			HttpMethod = method,
			Url = uri,
			RawUrl = target,
			Headers = headers,
			InputStream = input,
			ContentEncoding = charsetof(ctype),
			ContentType = ctype,
			ContentLength64 = clen,
			RemoteEndPoint = remote,
			IsWebSocketRequest = ws,
		};
		var ctx = new SockCtx {
			Request = req,
			Net = ns,
			Prefetch = prefetch,
		};
		ctx.Response = new SockRes(ns, ctx);
		return ctx;
	}

	static Uri makeuri(string target, string host) {
		if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
			|| target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			return new Uri(target);
		if (string.IsNullOrWhiteSpace(host)) host = "127.0.0.1";
		if (!target.StartsWith("/")) target = "/" + target;
		return new Uri("http://" + host + target);
	}

	static Encoding charsetof(string ct) {
		if (string.IsNullOrEmpty(ct)) return Encoding.UTF8;
		var i = ct.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
		if (i < 0) return Encoding.UTF8;
		var cs = ct.Substring(i + 8).Trim().Trim('"', '\'');
		var semi = cs.IndexOf(';');
		if (semi >= 0) cs = cs.Substring(0, semi).Trim();
		try { return Encoding.GetEncoding(cs); }
		catch { return Encoding.UTF8; }
	}

	static int hdrend(byte[] b, int n) {
		for (var i = 0; i + 1 < n; i++) {
			if (i + 3 < n && b[i] == 13 && b[i + 1] == 10 && b[i + 2] == 13 && b[i + 3] == 10)
				return i + 4;
			if (b[i] == 10 && b[i + 1] == 10)
				return i + 2;
		}
		return -1;
	}
}

public sealed class SockCtx {
	public SockReq Request;
	public SockRes Response;
	public bool Upgraded;
	public bool RawTaken;
	internal NetworkStream Net;
	internal byte[] Prefetch = Array.Empty<byte>();

	public bool TryUpgrade() {
		if (Upgraded || Net == null) return false;
		var key = (Request?.Headers?["Sec-WebSocket-Key"] ?? "").Trim();
		if (key.Length == 0) return false;
		string accept;
		using (var sha = SHA1.Create()) {
			var hash = sha.ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"));
			accept = Convert.ToBase64String(hash);
		}
		var resp = "HTTP/1.1 101 Switching Protocols\r\n"
			+ "Upgrade: websocket\r\n"
			+ "Connection: Upgrade\r\n"
			+ "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";
		var bytes = Encoding.ASCII.GetBytes(resp);
		Net.Write(bytes, 0, bytes.Length);
		try { Net.Flush(); } catch { }
		Upgraded = true;
		if (Response != null) Response.StatusCode = 101;
		return true;
	}
}

public sealed class SockReq {
	public string HttpMethod;
	public Uri Url;
	public string RawUrl;
	public SockHeaders Headers;
	public Stream InputStream;
	public Encoding ContentEncoding;
	public string ContentType;
	public long ContentLength64;
	public IPEndPoint RemoteEndPoint;
	public bool IsWebSocketRequest;
}

public sealed class SockHeaders {
	readonly List<KeyValuePair<string, string>> items = new();

	public string this[string key] {
		get {
			foreach (var kv in items) {
				if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
					return kv.Value;
			}
			return null;
		}
		set {
			for (var i = 0; i < items.Count; i++) {
				if (!string.Equals(items[i].Key, key, StringComparison.OrdinalIgnoreCase)) continue;
				items[i] = new KeyValuePair<string, string>(items[i].Key, value ?? "");
				for (var j = items.Count - 1; j > i; j--) {
					if (string.Equals(items[j].Key, key, StringComparison.OrdinalIgnoreCase))
						items.RemoveAt(j);
				}
				return;
			}
			items.Add(new KeyValuePair<string, string>(key ?? "", value ?? ""));
		}
	}

	public void Add(string key, string value) {
		items.Add(new KeyValuePair<string, string>(key ?? "", value ?? ""));
	}

	public string[] AllKeys {
		get {
			var keys = new List<string>();
			foreach (var kv in items) {
				var seen = false;
				foreach (var k in keys) {
					if (string.Equals(k, kv.Key, StringComparison.OrdinalIgnoreCase)) { seen = true; break; }
				}
				if (!seen) keys.Add(kv.Key);
			}
			return keys.ToArray();
		}
	}
}

public sealed class SockRes {
	readonly Stream net;
	readonly SockCtx ctx;
	readonly SockOut output;
	public int StatusCode = 200;
	public string ContentType;
	public Encoding ContentEncoding;
	public long ContentLength64 = -1;
	public readonly SockHeaders Headers = new();
	public Stream OutputStream => output;

	public SockRes(Stream net, SockCtx ctx) {
		this.net = net;
		this.ctx = ctx;
		output = new SockOut(net, this);
	}

	public void AppendHeader(string key, string value) => Headers.Add(key, value);

	public void Close() {
		if (ctx != null && (ctx.Upgraded || ctx.RawTaken)) return;
		output.Finish();
	}

	public void Abort() {
		output.Abort();
		try { net.Close(); } catch { }
	}
}

sealed class SockOut : Stream {
	readonly Stream net;
	readonly SockRes res;
	bool sent;
	bool aborted;

	public SockOut(Stream net, SockRes res) {
		this.net = net;
		this.res = res;
	}

	public override bool CanRead => false;
	public override bool CanSeek => false;
	public override bool CanWrite => true;
	public override long Length => throw new NotSupportedException();
	public override long Position {
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	public override void Flush() {
		try { net.Flush(); } catch { }
	}

	public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();

	public override void Write(byte[] buffer, int offset, int count) {
		if (aborted) return;
		sendhdr();
		if (count > 0) net.Write(buffer, offset, count);
	}

	public override void Close() => Finish();

	public void Finish() {
		if (aborted) return;
		if (!sent && res.ContentLength64 < 0) res.ContentLength64 = 0;
		sendhdr();
		try { net.Flush(); } catch { }
	}

	public void Abort() => aborted = true;

	void sendhdr() {
		if (sent || aborted) return;
		sent = true;
		var sb = new StringBuilder();
		sb.Append("HTTP/1.1 ").Append(res.StatusCode).Append(' ').Append(reason(res.StatusCode)).Append("\r\n");
		if (!string.IsNullOrEmpty(res.ContentType))
			sb.Append("Content-Type: ").Append(res.ContentType).Append("\r\n");
		if (res.ContentLength64 >= 0)
			sb.Append("Content-Length: ").Append(res.ContentLength64.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
		var sawConn = false;
		if (res.Headers != null) {
			foreach (var key in res.Headers.AllKeys) {
				var val = res.Headers[key] ?? "";
				if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
				if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
				if (key.Equals("Connection", StringComparison.OrdinalIgnoreCase)) sawConn = true;
				sb.Append(key).Append(": ").Append(val).Append("\r\n");
			}
		}
		if (!sawConn) sb.Append("Connection: close\r\n");
		sb.Append("\r\n");
		var hb = Encoding.UTF8.GetBytes(sb.ToString());
		net.Write(hb, 0, hb.Length);
	}

	static string reason(int code) => code switch {
		200 => "OK",
		204 => "No Content",
		302 => "Found",
		400 => "Bad Request",
		401 => "Unauthorized",
		403 => "Forbidden",
		404 => "Not Found",
		405 => "Method Not Allowed",
		426 => "Upgrade Required",
		500 => "Internal Server Error",
		503 => "Service Unavailable",
		_ => "OK",
	};
}

sealed class SockIn : Stream {
	readonly Stream src;
	readonly byte[] pre;
	int preo;
	long left;

	public SockIn(Stream src, byte[] pre, long len) {
		this.src = src;
		this.pre = pre ?? Array.Empty<byte>();
		left = len < 0 ? 0 : len;
	}

	public override bool CanRead => true;
	public override bool CanSeek => false;
	public override bool CanWrite => false;
	public override long Length => throw new NotSupportedException();
	public override long Position {
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	public override int Read(byte[] buffer, int offset, int count) {
		if (buffer == null) throw new ArgumentNullException(nameof(buffer));
		if (count <= 0 || left <= 0) return 0;
		if (count > left) count = (int)Math.Min(int.MaxValue, left);
		int n;
		if (preo < pre.Length) {
			n = Math.Min(count, pre.Length - preo);
			Buffer.BlockCopy(pre, preo, buffer, offset, n);
			preo += n;
		}
		else {
			try { n = src.Read(buffer, offset, count); }
			catch { n = 0; }
		}
		if (n <= 0) { left = 0; return 0; }
		left -= n;
		return n;
	}

	public override void Flush() { }
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

sealed class SockChunk : Stream {
	readonly SockBuf raw;
	long left = -1;
	bool done;

	public SockChunk(Stream src, byte[] pre) {
		raw = new SockBuf(src, pre);
	}

	public override bool CanRead => true;
	public override bool CanSeek => false;
	public override bool CanWrite => false;
	public override long Length => throw new NotSupportedException();
	public override long Position {
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	public override int Read(byte[] buffer, int offset, int count) {
		if (buffer == null) throw new ArgumentNullException(nameof(buffer));
		if (count <= 0 || done) return 0;
		var got = 0;
		while (got < count) {
			if (left < 0 || left == 0) {
				if (!nextchunk()) { done = true; break; }
			}
			var want = (int)Math.Min(count - got, left);
			var n = raw.Read(buffer, offset + got, want);
			if (n <= 0) { done = true; break; }
			left -= n;
			got += n;
			if (left == 0) skipcrlf();
		}
		return got;
	}

	bool nextchunk() {
		var line = raw.ReadLine();
		if (line == null) return false;
		var semi = line.IndexOf(';');
		if (semi >= 0) line = line.Substring(0, semi);
		if (!long.TryParse(line.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var sz) || sz < 0)
			return false;
		if (sz == 0) {
			while (true) {
				var t = raw.ReadLine();
				if (t == null || t.Length == 0) break;
			}
			return false;
		}
		left = sz;
		return true;
	}

	void skipcrlf() {
		var b = new byte[2];
		if (!raw.ReadFull(b)) return;
		left = -1;
	}

	public override void Flush() { }
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

sealed class SockBuf {
	readonly Stream src;
	byte[] buf;
	int o, n;

	public SockBuf(Stream src, byte[] pre) {
		this.src = src;
		buf = pre ?? Array.Empty<byte>();
		n = buf.Length;
	}

	public int Read(byte[] dst, int off, int count) {
		if (count <= 0) return 0;
		if (o >= n) {
			buf = new byte[8192];
			o = 0;
			try { n = src.Read(buf, 0, buf.Length); }
			catch { n = 0; }
			if (n <= 0) return 0;
		}
		var c = Math.Min(count, n - o);
		Buffer.BlockCopy(buf, o, dst, off, c);
		o += c;
		return c;
	}

	public bool ReadFull(byte[] dst) {
		var g = 0;
		while (g < dst.Length) {
			var r = Read(dst, g, dst.Length - g);
			if (r <= 0) return false;
			g += r;
		}
		return true;
	}

	public string ReadLine() {
		var sb = new StringBuilder();
		while (sb.Length < 8192) {
			var one = new byte[1];
			if (Read(one, 0, 1) <= 0) return null;
			if (one[0] == (byte)'\n') break;
			if (one[0] != (byte)'\r') sb.Append((char)one[0]);
		}
		return sb.ToString();
	}
}
