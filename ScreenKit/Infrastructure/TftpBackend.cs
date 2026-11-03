using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ScreenKit;

/// <summary>
/// TFTP 客户端后端。
/// TFTP 只有「按文件名收一个文件」「按文件名发一个文件」两个动作，没有列目录、改名、建目录、改权限，
/// 所以除了 Connect/OpenRead/OpenWrite 之外都抛 NotSupportedException，界面按 FtpProto.IsTftp 事先避开。
/// 协议层面没有断点续传，AlignResume 一律返回 0（从头上传）。
/// </summary>
sealed class TftpBackend : IFtpBackend {
	/// <summary>请求的块大小。1024 在多数实现上都支持，比默认 512 少一半往返。</summary>
	const int WANT_BLKSIZE = 1024;
	const int TIMEOUT_MS = 5000;
	const int RETRY = 5;

	FtpSite site;
	string host = "";
	int port;
	bool disposed;

	public bool Connected { get; private set; }

	public void Connect(FtpSite s, string password) {
		Compat.ThrowIfDisposed(disposed, this);
		if (s == null) throw new ArgumentNullException(nameof(s));
		host = (s.Host ?? "").Trim();
		if (host.Length == 0) throw new InvalidOperationException(Loc.T("ftp.err.nohost"));
		site = s;
		port = s.PortOr(FtpProto.DefPort(FtpProto.Tftp));
		Connected = true;
	}

	public void Disconnect() => Connected = false;

	public void Dispose() {
		disposed = true;
		Connected = false;
	}

	public List<FtpEntry> List(string path) =>
		throw new NotSupportedException(Loc.T("ftp.tftp.nolist"));

	public string WorkDir() => "/";

	public string Full(string path) {
		var p = (path ?? "").Trim().Replace('\\', '/');
		if (p.Length == 0) return "/";
		return p.StartsWith("/", StringComparison.Ordinal) ? p : "/" + p;
	}

	public void MkDir(string path) => throw new NotSupportedException(Loc.T("ftp.tftp.nolist"));

	public void RmDir(string path) => throw new NotSupportedException(Loc.T("ftp.tftp.nolist"));

	public void RmFile(string path) => throw new NotSupportedException(Loc.T("ftp.tftp.nolist"));

	public void Rename(string src, string dst) => throw new NotSupportedException(Loc.T("ftp.tftp.nolist"));

	public void Chmod(string path, int mode) => throw new NotSupportedException(Loc.T("ftp.tftp.nolist"));

	public bool IsDir(string path) => false;

	/// <summary>TFTP 没有续传，永远只能从头写。</summary>
	public long AlignResume(string path, long want) => 0;

	/// <summary>
	/// 用 tsize 选项探一次文件大小。发 RRQ 拿 OACK 里的 tsize，然后立刻发 ERROR 取消传输。
	/// 探不到返回 -1，界面按「长度未知」处理，进度条不确定但能正常传完。
	/// </summary>
	public long Size(string path) {
		var name = tftpname(path);
		if (name.Length == 0) return -1;
		try {
			using var c = new TftpClient(host, port);
			return c.ProbeSize(name);
		}
		catch { return -1; }
	}

	public Stream OpenRead(string path, long offset, long length) {
		Compat.ThrowIfDisposed(disposed, this);
		var name = tftpname(path);
		if (name.Length == 0) throw new InvalidOperationException(Loc.T("ftp.tftp.noname"));
		return new TftpReadStream(host, port, name);
	}

	public Stream OpenWrite(string path, long offset) {
		Compat.ThrowIfDisposed(disposed, this);
		var name = tftpname(path);
		if (name.Length == 0) throw new InvalidOperationException(Loc.T("ftp.tftp.noname"));
		if (offset > 0) throw new NotSupportedException(Loc.T("ftp.tftp.noresume"));
		return new TftpWriteStream(host, port, name);
	}

	/// <summary>把远程路径整理成 TFTP 的「文件名」：去掉开头的 /，其余保留（服务端可以支持子目录）。</summary>
	static string tftpname(string path) =>
		(path ?? "").Trim().Replace('\\', '/').TrimStart('/');
}

// ==================== 协议实现 ====================
#region
/// <summary>
/// 一次 TFTP 传输共用的控制逻辑：发请求、等应答、按块号确认、超时重传。
/// 每个流自己持有一个实例，用完即弃。
/// </summary>
sealed class TftpClient : IDisposable {
	const ushort OP_RRQ = 1;
	const ushort OP_WRQ = 2;
	const ushort OP_DATA = 3;
	const ushort OP_ACK = 4;
	const ushort OP_ERROR = 5;
	const ushort OP_OACK = 6;

	readonly Socket sock;
	IPEndPoint server;
	bool disposed;

	public int BlkSize { get; private set; } = 512;

	public TftpClient(string host, int port) {
		sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
		sock.ReceiveTimeout = TftpConst.TIMEOUT_MS;
		// 目标可能是主机名，解析一次固定下来
		IPAddress addr;
		if (!IPAddress.TryParse(host, out addr)) {
			var addrs = Dns.GetHostAddresses(host);
			addr = null;
			foreach (var a in addrs) {
				if (a.AddressFamily == AddressFamily.InterNetwork) { addr = a; break; }
			}
			if (addr == null) throw new InvalidOperationException(Loc.T("ftp.err.nohost"));
		}
		server = new IPEndPoint(addr, port);
	}

	/// <summary>只探大小：RRQ + tsize，拿到就取消。拿不到返回 -1。</summary>
	public long ProbeSize(string name) {
		var req = buildreq(OP_RRQ, name, TftpConst.WANT_BLKSIZE, withtsize: true);
		sock.SendTo(req, server);
		var buf = new byte[65536];
		EndPoint from = new IPEndPoint(IPAddress.Any, 0);
		int n;
		try { n = sock.ReceiveFrom(buf, ref from); }
		catch { return -1; }
		if (n < 4) return -1;
		var ep = (IPEndPoint)from;
		var op = (ushort)((buf[0] << 8) | buf[1]);
		long size = -1;
		if (op == OP_OACK) {
			var o = parseoack(buf, n);
			if (o.TryGetValue("tsize", out var ts) && long.TryParse(ts, out var v)) size = v;
		}
		// 不管拿到没拿到，都发 ERROR 把这次传输掐掉
		try { sock.SendTo(errpkt(0, "probe"), ep); } catch { }
		return size;
	}

	/// <summary>发 RRQ 并完成协商，之后按块读。</summary>
	public void BeginRead(string name) {
		var req = buildreq(OP_RRQ, name, TftpConst.WANT_BLKSIZE, withtsize: false);
		sock.SendTo(req, server);
	}

	/// <summary>发 WRQ 并完成协商（等 ACK 0 或 OACK）。</summary>
	public void BeginWrite(string name) {
		var req = buildreq(OP_WRQ, name, TftpConst.WANT_BLKSIZE, withtsize: false);
		sock.SendTo(req, server);
	}

	/// <summary>收一个包，返回 (op, block, payloadLen)，数据在 <paramref name="buf"/> 的 4 字节之后。</summary>
	public (ushort op, ushort block, int len) Receive(byte[] buf) {
		EndPoint from = new IPEndPoint(IPAddress.Any, 0);
		var n = sock.ReceiveFrom(buf, ref from);
		if (n < 4) return (0, 0, 0);
		// 跟着服务端的 TID 走
		server = (IPEndPoint)from;
		var op = (ushort)((buf[0] << 8) | buf[1]);
		var block = (ushort)((buf[2] << 8) | buf[3]);
		return (op, block, n - 4);
	}

	public void SendAck(ushort block) {
		var pkt = new byte[] { 0, (byte)OP_ACK, (byte)(block >> 8), (byte)(block & 0xFF) };
		sock.SendTo(pkt, server);
	}

	public void SendData(ushort block, byte[] data, int len) {
		var pkt = new byte[4 + len];
		pkt[0] = 0;
		pkt[1] = (byte)OP_DATA;
		pkt[2] = (byte)(block >> 8);
		pkt[3] = (byte)(block & 0xFF);
		if (len > 0) Buffer.BlockCopy(data, 0, pkt, 4, len);
		sock.SendTo(pkt, server);
	}

	public void SendError(string msg) {
		try { sock.SendTo(errpkt(0, msg), server); } catch { }
	}

	/// <summary>处理协商应答：OACK 时读出块大小并回 ACK 0；ACK 0 表示可以直接开始。</summary>
	public void HandleNegotiation(ushort op, byte[] buf, int len) {
		if (op == OP_OACK) {
			var o = parseoack(buf, len + 4);
			if (o.TryGetValue("blksize", out var bs) && int.TryParse(bs, out var v) && v >= 8)
				BlkSize = v;
			// OACK 用 ACK 0 确认
			SendAck(0);
			return;
		}
		if (op == OP_ERROR) throw new IOException(readmsg(buf, len + 4));
		if (op != OP_ACK || (ushort)((buf[2] << 8) | buf[3]) != 0)
			throw new IOException(Loc.T("ftp.tftp.badreply"));
	}

	static byte[] buildreq(ushort op, string name, int blksize, bool withtsize) {
		var ms = new MemoryStream();
		ms.WriteByte(0);
		ms.WriteByte((byte)op);
		writez(ms, name);
		writez(ms, "octet");
		if (blksize > 0) {
			writez(ms, "blksize");
			writez(ms, blksize.ToString());
		}
		if (withtsize) {
			writez(ms, "tsize");
			writez(ms, "0");
		}
		return ms.ToArray();
	}

	static void writez(MemoryStream ms, string s) {
		var b = Encoding.UTF8.GetBytes(s);
		ms.Write(b, 0, b.Length);
		ms.WriteByte(0);
	}

	static byte[] errpkt(ushort code, string msg) {
		var mb = Encoding.UTF8.GetBytes(msg ?? "");
		var pkt = new byte[4 + mb.Length + 1];
		pkt[0] = 0;
		pkt[1] = (byte)OP_ERROR;
		pkt[2] = (byte)(code >> 8);
		pkt[3] = (byte)(code & 0xFF);
		Buffer.BlockCopy(mb, 0, pkt, 4, mb.Length);
		return pkt;
	}

	static string readmsg(byte[] buf, int n) {
		var start = 4;
		var end = start;
		while (end < n && buf[end] != 0) end++;
		return end > start ? Encoding.UTF8.GetString(buf, start, end - start) : "";
	}

	static Dictionary<string, string> parseoack(byte[] buf, int n) {
		var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var pos = 2;
		while (pos < n) {
			var k = readz(buf, ref pos, n);
			if (k == null || k.Length == 0) break;
			var v = readz(buf, ref pos, n);
			d[k] = v ?? "";
		}
		return d;
	}

	static string readz(byte[] buf, ref int pos, int n) {
		if (pos >= n) return null;
		var start = pos;
		while (pos < n && buf[pos] != 0) pos++;
		var s = Encoding.UTF8.GetString(buf, start, pos - start);
		pos++;
		return s;
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		try { sock.Close(); } catch { }
	}
}

/// <summary>协议里用到的常量，服务端和客户端共用。</summary>
static class TftpConst {
	public const int WANT_BLKSIZE = 1024;
	public const int TIMEOUT_MS = 5000;
	public const int RETRY = 5;
}

/// <summary>
/// 下载流：按块把远端数据读出来。
/// 收满一块就回 ACK，不足一块表示最后一块，读完后关闭。
/// 读的时候自己等应答，所以 Read 在 UI 线程之外调用（队列里就是后台线程）。
/// </summary>
sealed class TftpReadStream : Stream {
	readonly TftpClient c;
	readonly byte[] buf = new byte[65536];
	byte[] cur = new byte[65536];
	int curLen;
	int curPos;
	ushort expect = 1;
	bool done;

	public TftpReadStream(string host, int port, string name) {
		c = new TftpClient(host, port);
		c.BeginRead(name);
		// 先收协商应答或第一块
		var (op, block, len) = c.Receive(buf);
		if (op == 6 || op == 4) {
			c.HandleNegotiation(op, buf, len);
			(op, block, len) = c.Receive(buf);
		}
		if (op == 5) {
			var msg = Encoding.UTF8.GetString(buf, 4, len);
			c.Dispose();
			throw new IOException(msg);
		}
		if (op != 3) {
			c.Dispose();
			throw new IOException(Loc.T("ftp.tftp.badreply"));
		}
		take(block, len);
	}

	void take(ushort block, int len) {
		Buffer.BlockCopy(buf, 4, cur, 0, len);
		curLen = len;
		curPos = 0;
		expect = block;
		// 不足一块就是最后一块，收到就回 ACK，免得服务端一直重传
		if (len < c.BlkSize) {
			done = true;
			try { c.SendAck(block); } catch { }
		}
	}

	public override int Read(byte[] dest, int off, int count) {
		if (curPos >= curLen) {
			if (done) return 0;
			c.SendAck(expect);
			var (op, block, len) = c.Receive(buf);
			if (op == 5) throw new IOException(Encoding.UTF8.GetString(buf, 4, len));
			if (op != 3) throw new IOException(Loc.T("ftp.tftp.badreply"));
			take(block, len);
			if (curLen == 0) return 0;
		}
		var n = Math.Min(count, curLen - curPos);
		Buffer.BlockCopy(cur, curPos, dest, off, n);
		curPos += n;
		return n;
	}

	public override bool CanRead => true;
	public override bool CanSeek => false;
	public override bool CanWrite => false;
	public override long Length => throw new NotSupportedException();
	public override long Position {
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}
	public override void Flush() { }
	public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
	public override void SetLength(long v) => throw new NotSupportedException();
	public override void Write(byte[] b, int o, int n) => throw new NotSupportedException();

	protected override void Dispose(bool disposing) {
		if (disposing) c.Dispose();
		base.Dispose(disposing);
	}
}

/// <summary>
/// 上传流：攒满一块就发出去并等确认，最后不足一块时补发剩余部分。
/// 写完必须 Dispose（队列里用 using），确认最后一块才算传完。
/// </summary>
sealed class TftpWriteStream : Stream {
	readonly TftpClient c;
	readonly byte[] block;
	int pos;
	ushort next = 1;
	bool finished;

	public TftpWriteStream(string host, int port, string name) {
		c = new TftpClient(host, port);
		c.BeginWrite(name);
		var buf = new byte[65536];
		var (op, _, len) = c.Receive(buf);
		if (op == 5) {
			var msg = Encoding.UTF8.GetString(buf, 4, len);
			c.Dispose();
			throw new IOException(msg);
		}
		c.HandleNegotiation(op, buf, len);
		block = new byte[c.BlkSize];
	}

	public override void Write(byte[] src, int off, int count) {
		while (count > 0) {
			var n = Math.Min(count, block.Length - pos);
			Buffer.BlockCopy(src, off, block, pos, n);
			pos += n;
			off += n;
			count -= n;
			if (pos == block.Length) sendblock(block.Length);
		}
	}

	void sendblock(int len) {
		for (var i = 0; i < TftpConst.RETRY; i++) {
			c.SendData(next, block, len);
			var buf = new byte[65536];
			(ushort op, ushort block, int l) got;
			try { got = c.Receive(buf); }
			catch { continue; }
			if (got.op == 5) throw new IOException(Encoding.UTF8.GetString(buf, 4, got.l));
			if (got.op == 4 && got.block == next) {
				next++;
				pos = 0;
				return;
			}
		}
		throw new IOException(Loc.T("ftp.tftp.timeout"));
	}

	/// <summary>
	/// 把余下的数据发出去。pos 为 0 时发的是一块空数据，正好用于「文件大小是块大小整数倍」的收尾。
	/// </summary>
	public void Finish() {
		if (finished) return;
		finished = true;
		sendblock(pos);
	}

	public override void Flush() => Finish();

	public override bool CanRead => false;
	public override bool CanSeek => false;
	public override bool CanWrite => true;
	public override long Length => throw new NotSupportedException();
	public override long Position {
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}
	public override int Read(byte[] b, int o, int n) => throw new NotSupportedException();
	public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
	public override void SetLength(long v) => throw new NotSupportedException();

	protected override void Dispose(bool disposing) {
		if (disposing) {
			try { Finish(); } catch { }
			c.Dispose();
		}
		base.Dispose(disposing);
	}
}
#endregion
