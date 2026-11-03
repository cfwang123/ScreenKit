using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ScreenKit;

/// <summary>
/// TFTP 服务端设置。目录和用户参数与 FTP 共用，这里只有端口和开关。
/// 注意：TFTP 协议本身没有登录步骤，所以用户/密码对它不起作用，谁来都能读写共享目录。
/// </summary>
public sealed class TftpOptions {
	public bool Enabled;
	/// <summary>TFTP 标准端口是 69。低于 1024 需要管理员权限。</summary>
	public int Port = 69;
	/// <summary>共享根目录，直接沿用 FTP 的设置。</summary>
	public string Root = "";

	public string RootPath {
		get {
			var r = (Root ?? "").Trim().Trim('"');
			if (r.Length > 0) return r;
			return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ftproot");
		}
	}

	public int PortOr() => Port >= 1 && Port <= 65535 ? Port : 69;
}

/// <summary>
/// 简易 TFTP 服务端（RFC 1350 + blksize/timeout/tsize 选项）。
/// 每个传输单独开一个 UDP 端口（TFTP 的 TID 规则），收到 RRQ 就发文件、收到 WRQ 就收文件，
/// 块号满了回绕，丢包按超时重传。
/// 不支持 netascii 以外的特殊模式，只认 octet 和 netascii。
/// </summary>
sealed class TftpServer : IDisposable {
	const int DEF_BLKSIZE = 512;
	const int MIN_BLKSIZE = 8;
	const int MAX_BLKSIZE = 65464;
	const int MAX_RETRY = 5;
	const int DEF_TIMEOUT = 3;

	const ushort OP_RRQ = 1;
	const ushort OP_WRQ = 2;
	const ushort OP_DATA = 3;
	const ushort OP_ACK = 4;
	const ushort OP_ERROR = 5;
	const ushort OP_OACK = 6;

	readonly object gate = new();
	Socket listener;
	Thread accept;
	volatile bool running;
	bool disposed;

	public TftpOptions Opt { get; private set; } = new();

	/// <summary>实际监听端口；未运行为 0。</summary>
	public int Port { get; private set; }

	public bool IsRunning => running && listener != null;

	public string LastError { get; private set; } = "";

	public Action<string> Log { get; set; }

	public void Start(TftpOptions opt) {
		Compat.ThrowIfDisposed(disposed, this);
		Stop();
		Opt = opt ?? new TftpOptions();
		var root = Opt.RootPath;
		try {
			Directory.CreateDirectory(root);
		}
		catch (Exception ex) {
			LastError = ex.Message;
			throw;
		}
		var want = Opt.PortOr();
		Socket s;
		try {
			s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			s.Bind(new IPEndPoint(IPAddress.Any, want));
		}
		catch (Exception ex) {
			LastError = ex.Message;
			throw;
		}
		listener = s;
		Port = want;
		running = true;
		accept = new Thread(recvloop) { IsBackground = true, Name = "TftpsrvRecv" };
		accept.Start();
		log($"TFTP 服务已启动 端口 {Port}  根目录 {root}");
	}

	public void Stop() {
		running = false;
		var l = listener;
		listener = null;
		try { l?.Close(); } catch { }
		Port = 0;
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		Stop();
	}

	void log(string s) {
		try { Log?.Invoke($"{DateTime.Now:HH:mm:ss} {s}"); } catch { }
	}

	// ==================== 收包 ====================
	#region
	void recvloop() {
		var buf = new byte[65536];
		while (running) {
			EndPoint from = new IPEndPoint(IPAddress.Any, 0);
			int n;
			try { n = listener.ReceiveFrom(buf, ref from); }
			catch { break; }
			if (n < 4) continue;
			var op = (ushort)((buf[0] << 8) | buf[1]);
			if (op != OP_RRQ && op != OP_WRQ) {
				// 非请求包：按规范该发错误，但端口 69 常被探测，直接忽略更省事
				continue;
			}
			var req = new byte[n];
			Buffer.BlockCopy(buf, 0, req, 0, n);
			var ep = (IPEndPoint)from;
			var t = new Thread(() => {
				try { handle(ep, req); }
				catch (Exception ex) {
					log($"{ep.Address} 传输异常: {ex.Message}");
				}
			}) { IsBackground = true, Name = "TftpsrvXfer" };
			t.Start();
		}
	}

	void handle(IPEndPoint client, byte[] req) {
		var op = (ushort)((req[0] << 8) | req[1]);
		// filename\0mode\0[opt\0val\0]*
		var pos = 2;
		var name = readstr(req, ref pos);
		var mode = readstr(req, ref pos).ToLowerInvariant();
		if (name == null || mode == null) {
			senderror(client, null, 4, "请求格式不对");
			return;
		}
		if (mode != "octet" && mode != "netascii") {
			senderror(client, null, 4, "只支持 octet / netascii");
			return;
		}
		var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		while (pos < req.Length) {
			var k = readstr(req, ref pos);
			if (k == null || k.Length == 0) break;
			var v = readstr(req, ref pos);
			opts[k] = v ?? "";
		}

		var root = Path.GetFullPath(Opt.RootPath);
		var full = map(root, name);
		if (full == null) {
			senderror(client, null, 2, "路径不允许");
			log($"{client.Address} 拒绝了越界路径 {name}");
			return;
		}

		// 协商选项
		var blksize = DEF_BLKSIZE;
		var timeout = DEF_TIMEOUT;
		var oack = new List<KeyValuePair<string, string>>();
		if (opts.TryGetValue("blksize", out var bs) && int.TryParse(bs, out var bsv)
			&& bsv >= MIN_BLKSIZE && bsv <= MAX_BLKSIZE) {
			blksize = bsv;
			oack.Add(new("blksize", bsv.ToString(CultureInfo.InvariantCulture)));
		}
		if (opts.TryGetValue("timeout", out var to) && int.TryParse(to, out var tov)
			&& tov >= 1 && tov <= 255) {
			timeout = tov;
			oack.Add(new("timeout", tov.ToString(CultureInfo.InvariantCulture)));
		}

		using var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
		try {
			sock.Bind(new IPEndPoint(IPAddress.Any, 0));
		}
		catch (Exception ex) {
			senderror(client, null, 0, ex.Message);
			return;
		}
		sock.ReceiveTimeout = timeout * 1000;

		if (op == OP_RRQ) {
			FileStream fs;
			try {
				if (!File.Exists(full)) {
					senderror(client, sock, 1, "文件不存在");
					log($"{client.Address} 读 {name} 失败: 文件不存在");
					return;
				}
				fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
			}
			catch (Exception ex) {
				senderror(client, sock, 2, ex.Message);
				log($"{client.Address} 读 {name} 失败: {ex.Message}");
				return;
			}
			using (fs) {
				if (opts.ContainsKey("tsize"))
					oack.Add(new("tsize", fs.Length.ToString(CultureInfo.InvariantCulture)));
				log($"{client.Address} 请求下载 {name}");
				if (oack.Count > 0) {
					if (!sendoack(sock, client, oack, timeout)) return;
				}
				sendfile(sock, client, fs, blksize, timeout, name);
			}
			return;
		}

		// WRQ
		Directory.CreateDirectory(Path.GetDirectoryName(full) ?? root);
		FileStream ws;
		try {
			ws = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None);
		}
		catch (Exception ex) {
			senderror(client, sock, 2, ex.Message);
			log($"{client.Address} 写 {name} 失败: {ex.Message}");
			return;
		}
		using (ws) {
			if (opts.ContainsKey("tsize"))
				oack.Add(new("tsize", "0"));
			log($"{client.Address} 请求上传 {name}");
			if (oack.Count > 0) {
				if (!sendoack(sock, client, oack, timeout)) return;
			}
			else {
				// 没有选项协商时，服务端先回 ACK 0 告诉客户端可以发第一块了
				if (!sendack(sock, client, 0)) {
					log($"{client.Address} 上传 {name} ACK 0 发送失败");
					return;
				}
			}
			recvfile(sock, client, ws, blksize, timeout, name);
		}
	}
	#endregion

	// ==================== 收 / 发 ====================
	#region
	void sendfile(Socket sock, IPEndPoint client, FileStream fs, int blksize, int timeout, string name) {
		var buf = new byte[blksize];
		ushort block = 1;
		long total = 0;
		while (true) {
			int n;
			try { n = fs.Read(buf, 0, blksize); }
			catch (Exception ex) {
				senderror(client, sock, 0, ex.Message);
				return;
			}
			if (!senddata(sock, client, block, buf, n, timeout)) {
				log($"{client.Address} 下载 {name} 超时中断");
				return;
			}
			total += n;
			// 不足一块就表示传完了
			if (n < blksize) break;
			block++;
		}
		log($"{client.Address} 下载 {name} {total} 字节");
	}

	bool senddata(Socket sock, IPEndPoint client, ushort block, byte[] data, int len, int timeout) {
		var pkt = new byte[4 + len];
		pkt[0] = 0;
		pkt[1] = (byte)OP_DATA;
		pkt[2] = (byte)(block >> 8);
		pkt[3] = (byte)(block & 0xFF);
		if (len > 0) Buffer.BlockCopy(data, 0, pkt, 4, len);
		for (var i = 0; i < MAX_RETRY; i++) {
			try { sock.SendTo(pkt, client); }
			catch { return false; }
			if (waitack(sock, client, block, timeout)) return true;
		}
		return false;
	}

	/// <summary>等指定的 ACK；收到别的块号就继续等（可能是重复的旧包）。</summary>
	bool waitack(Socket sock, IPEndPoint client, ushort block, int timeout) {
		var buf = new byte[1024];
		var deadline = Environment.TickCount + timeout * 1000;
		while (true) {
			var left = deadline - Environment.TickCount;
			if (left <= 0) return false;
			try { sock.ReceiveTimeout = left; }
			catch { }
			EndPoint from = new IPEndPoint(IPAddress.Any, 0);
			int n;
			try { n = sock.ReceiveFrom(buf, ref from); }
			catch { return false; }
			if (n < 4) continue;
			var ep = (IPEndPoint)from;
			if (!ep.Address.Equals(client.Address) || ep.Port != client.Port) continue;
			var op = (ushort)((buf[0] << 8) | buf[1]);
			if (op == OP_ERROR) return false;
			if (op != OP_ACK) continue;
			var ack = (ushort)((buf[2] << 8) | buf[3]);
			if (ack == block) return true;
		}
	}

	void recvfile(Socket sock, IPEndPoint client, FileStream fs, int blksize, int timeout, string name) {
		var buf = new byte[65536];
		ushort expect = 1;
		long total = 0;
		while (true) {
			var got = false;
			var deadline = Environment.TickCount + timeout * 1000;
			var len = 0;
			while (!got) {
				var left = deadline - Environment.TickCount;
				if (left <= 0) {
					log($"{client.Address} 上传 {name} 超时中断");
					return;
				}
				try { sock.ReceiveTimeout = left; }
				catch { }
				EndPoint from = new IPEndPoint(IPAddress.Any, 0);
				int n;
				try { n = sock.ReceiveFrom(buf, ref from); }
				catch {
					log($"{client.Address} 上传 {name} 超时中断");
					return;
				}
				if (n < 4) continue;
				var ep = (IPEndPoint)from;
				if (!ep.Address.Equals(client.Address)) continue;
				var op = (ushort)((buf[0] << 8) | buf[1]);
				if (op == OP_ERROR) {
					log($"{client.Address} 上传 {name} 被对方取消");
					return;
				}
				if (op != OP_DATA) continue;
				var blk = (ushort)((buf[2] << 8) | buf[3]);
				len = n - 4;
				if (blk == expect) {
					got = true;
					// 可能是客户端换了端口，跟着它走
					client = ep;
				}
				else {
					// 重复的上一块：再 ACK 一次
					sendack(sock, ep, blk);
				}
			}
			if (len > 0) {
				try { fs.Write(buf, 4, len); }
				catch (Exception ex) {
					senderror(client, sock, 3, ex.Message);
					return;
				}
				total += len;
			}
			if (!sendack(sock, client, expect)) {
				log($"{client.Address} 上传 {name} ACK 发送失败");
				return;
			}
			if (len < blksize) break;
			expect++;
		}
		log($"{client.Address} 上传 {name} {total} 字节");
	}

	bool sendack(Socket sock, IPEndPoint client, ushort block) {
		var pkt = new byte[] { 0, (byte)OP_ACK, (byte)(block >> 8), (byte)(block & 0xFF) };
		try { sock.SendTo(pkt, client); }
		catch { return false; }
		return true;
	}

	bool sendoack(Socket sock, IPEndPoint client, List<KeyValuePair<string, string>> opts, int timeout) {
		var ms = new MemoryStream();
		ms.WriteByte(0);
		ms.WriteByte((byte)OP_OACK);
		foreach (var kv in opts) {
			var kb = Encoding.ASCII.GetBytes(kv.Key);
			var vb = Encoding.ASCII.GetBytes(kv.Value);
			ms.Write(kb, 0, kb.Length);
			ms.WriteByte(0);
			ms.Write(vb, 0, vb.Length);
			ms.WriteByte(0);
		}
		var pkt = ms.ToArray();
		// OACK 用 ACK 0 确认
		for (var i = 0; i < MAX_RETRY; i++) {
			try { sock.SendTo(pkt, client); }
			catch { return false; }
			if (waitack(sock, client, 0, timeout)) return true;
		}
		return false;
	}

	static void senderror(IPEndPoint client, Socket sock, ushort code, string msg) {
		var mb = Encoding.UTF8.GetBytes(msg ?? "");
		var pkt = new byte[4 + mb.Length + 1];
		pkt[0] = 0;
		pkt[1] = (byte)OP_ERROR;
		pkt[2] = (byte)(code >> 8);
		pkt[3] = (byte)(code & 0xFF);
		Buffer.BlockCopy(mb, 0, pkt, 4, mb.Length);
		try {
			if (sock != null) sock.SendTo(pkt, client);
			else {
				using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
				s.SendTo(pkt, client);
			}
		}
		catch { }
	}
	#endregion

	// ==================== 工具 ====================
	#region
	static string readstr(byte[] buf, ref int pos) {
		if (pos >= buf.Length) return null;
		var start = pos;
		while (pos < buf.Length && buf[pos] != 0) pos++;
		var s = Encoding.UTF8.GetString(buf, start, pos - start);
		pos++; // 跳过结尾 0
		return s;
	}

	/// <summary>把 TFTP 里的相对路径映射到共享目录内，越界返回 null。</summary>
	static string map(string root, string name) {
		try {
			var rel = (name ?? "").Trim().Replace('\\', '/').TrimStart('/');
			var parts = new List<string>();
			foreach (var seg in rel.Split('/')) {
				if (seg.Length == 0 || seg == ".") continue;
				if (seg == "..") {
					if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
					continue;
				}
				if (seg.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
				parts.Add(seg);
			}
			if (parts.Count == 0) return null;
			var full = Path.GetFullPath(Path.Combine(root,
				string.Join(Path.DirectorySeparatorChar.ToString(), parts)));
			var rootFull = Path.GetFullPath(root);
			if (!full.Equals(rootFull, StringComparison.OrdinalIgnoreCase)
				&& !full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
				return null;
			return full;
		}
		catch { return null; }
	}
	#endregion
}
