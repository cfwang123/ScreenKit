using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ScreenKit;

/// <summary>FTP 服务端设置。存 config.toml，默认关闭。</summary>
public sealed class FtpSrvOptions {
	public bool Enabled;
	public int Port = 21;
	/// <summary>共享的根目录；留空用程序目录下的 ftproot\。</summary>
	public string Root = "";
	/// <summary>用户名；留空表示允许匿名。</summary>
	public string User = "";
	/// <summary>密码；用户名为空时忽略。</summary>
	public string Pass = "";

	public string DefaultRoot =>
		Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ftproot");

	public string RootPath {
		get {
			var r = (Root ?? "").Trim().Trim('"');
			return r.Length == 0 ? DefaultRoot : r;
		}
	}

	public bool Anonymous => (User ?? "").Trim().Length == 0;

	/// <summary>端口超出范围就退回 21。</summary>
	public int PortOr() => Port >= 1 && Port <= 65535 ? Port : 21;
}

/// <summary>
/// 简易 FTP 服务端：单用户或匿名，只做最常用的命令，够本机和其他客户端传文件。
/// 每个连接一个线程，阻塞式读写（FTP 本身就是一问一答）。
/// 支持 PASV/PORT、LIST/NLST、RETR/STOR/APPE、DELE、MKD/RMD、RNFR/RNTO、SIZE、TYPE、PWD/CWD。
/// 不支持 SITE、AUTH TLS、MLSD（返回 502，客户端会自动退回 LIST）。
/// </summary>
sealed class FtpServer : IDisposable {
	const int CTRL_TIMEOUT_MS = 30 * 60 * 1000;

	readonly object gate = new();
	readonly List<Socket> clients = new();
	TcpListener listener;
	Thread accept;
	volatile bool running;
	bool disposed;

	public FtpSrvOptions Opt { get; private set; } = new();

	/// <summary>实际监听端口；未运行为 0。</summary>
	public int Port { get; private set; }

	public bool IsRunning => running && listener != null;

	/// <summary>启动失败的原因，给界面显示。</summary>
	public string LastError { get; private set; } = "";

	/// <summary>每条命令写一行日志（已格式化）。</summary>
	public Action<string> Log { get; set; }

	public void Start(FtpSrvOptions opt) {
		Compat.ThrowIfDisposed(disposed, this);
		Stop();
		Opt = opt ?? new FtpSrvOptions();
		var root = Opt.RootPath;
		try {
			Directory.CreateDirectory(root);
		}
		catch (Exception ex) {
			LastError = ex.Message;
			throw;
		}
		var want = Opt.PortOr();
		LastError = "";
		TcpListener l;
		try {
			l = new TcpListener(IPAddress.Any, want);
			l.Start();
		}
		catch (Exception ex) {
			// 21 端口常被系统服务占着，退一个端口再试
			try {
				l = new TcpListener(IPAddress.Any, want + 1);
				l.Start();
				want++;
				log($"端口 {Opt.PortOr()} 被占用，改用 {want}");
			}
			catch {
				LastError = ex.Message;
				throw;
			}
		}
		listener = l;
		Port = want;
		running = true;
		accept = new Thread(acceptloop) { IsBackground = true, Name = "FtpsrvAccept" };
		accept.Start();
		log($"FTP 服务已启动 端口 {Port}  根目录 {root}");
	}

	public void Stop() {
		running = false;
		var l = listener;
		listener = null;
		try { l?.Stop(); } catch { }
		lock (gate) {
			foreach (var s in clients) closequiet(s);
			clients.Clear();
		}
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

	static void closequiet(Socket s) {
		try { if (s != null && s.Connected) s.Shutdown(SocketShutdown.Both); } catch { }
		try { s?.Close(); } catch { }
	}

	static void stoplistener(TcpListener l) {
		try { l?.Stop(); } catch { }
	}

	// ==================== 连接循环 ====================
	#region
	void acceptloop() {
		while (running) {
			Socket c = null;
			try { c = listener.AcceptSocket(); }
			catch { break; }
			lock (gate) clients.Add(c);
			var t = new Thread(() => session(c)) { IsBackground = true, Name = "FtpsrvSession" };
			t.Start();
		}
	}

	void session(Socket sock) {
		var ip = "?";
		try { ip = ((IPEndPoint)sock.RemoteEndPoint).Address.ToString(); }
		catch { }
		var peer = new Peer {
			Sock = sock,
			Remote = ip,
			Root = Path.GetFullPath(Opt.RootPath),
			Cwd = "/",
			Stream = new NetworkStream(sock, false)
		};
		log($"{ip} 已连接");
		try {
			reply(peer, "220 ScreenKit FTP 就绪");
			while (running) {
				var line = readline(peer);
				if (line == null) break;
				if (line.Length == 0) continue;
				if (!handle(peer, line)) break;
			}
		}
		catch { }
		finally {
			stoplistener(peer.DataListener);
			closequiet(peer.DataSock);
			closequiet(sock);
			lock (gate) clients.Remove(sock);
			log($"{ip} 已断开");
		}
	}

	/// <summary>读一行（\r\n 结尾）。超时按断开处理。</summary>
	static string readline(Peer p) {
		var sb = new StringBuilder(256);
		var buf = new byte[1];
		p.Sock.ReceiveTimeout = CTRL_TIMEOUT_MS;
		while (true) {
			int n;
			try { n = p.Stream.Read(buf, 0, 1); }
			catch { return null; }
			if (n <= 0) return null;
			var ch = (char)buf[0];
			if (ch == '\n') break;
			if (ch == '\r') continue;
			if (sb.Length > 4096) break;
			sb.Append(ch);
		}
		return sb.ToString();
	}

	static void reply(Peer p, string text) {
		try {
			var data = Encoding.UTF8.GetBytes(text + "\r\n");
			p.Stream.Write(data, 0, data.Length);
			p.Stream.Flush();
		}
		catch { }
	}

	/// <summary>返回 false 表示这条连接要结束。</summary>
	bool handle(Peer p, string line) {
		var sp = line.IndexOf(' ');
		var verb = (sp < 0 ? line : line.Substring(0, sp)).ToUpperInvariant();
		var arg = sp < 0 ? "" : line.Substring(sp + 1).Trim();
		log($"{p.Remote} > {line}");
		switch (verb) {
		case "USER":
			p.User = arg;
			if (Opt.Anonymous) {
				p.Authed = true;
				reply(p, "331 匿名登录，直接 PASS 即可");
			}
			else reply(p, "331 需要密码");
			return true;
		case "PASS":
			if (Opt.Anonymous) {
				p.Authed = true;
				reply(p, "230 已登录");
			}
			else if (string.Equals(p.User ?? "", (Opt.User ?? "").Trim(), StringComparison.Ordinal)
				&& string.Equals(arg, Opt.Pass ?? "", StringComparison.Ordinal)) {
				p.Authed = true;
				reply(p, "230 已登录");
			}
			else {
				reply(p, "530 用户名或密码错误");
			}
			return true;
		}
		if (!p.Authed) {
			reply(p, "530 请先登录");
			return true;
		}
		switch (verb) {
		case "QUIT":
			reply(p, "221 再见");
			return false;
		case "NOOP":
			reply(p, "200 OK");
			return true;
		case "SYST":
			reply(p, "215 UNIX Type: L8");
			return true;
		case "FEAT":
			// 不宣告 MLSD/UTF8：客户端会退回 LIST + GBK/UTF-8 试探
			reply(p, "211-Features:\r\n SIZE\r\n REST STREAM\r\n211 End");
			return true;
		case "OPTS":
			reply(p, "200 OK");
			return true;
		case "TYPE":
			reply(p, "200 已切换为二进制");
			return true;
		case "MODE":
			reply(p, "200 OK");
			return true;
		case "PWD":
		case "XPWD":
			reply(p, $"257 \"{p.Cwd}\"");
			return true;
		case "CWD":
		case "XCWD": {
			var full = p.Map(arg);
			if (full == null || !Directory.Exists(full)) {
				reply(p, "550 目录不存在");
				return true;
			}
			p.Cwd = p.Norm(arg);
			reply(p, "250 OK");
			return true;
		}
		case "CDUP":
		case "XCUP":
			p.Cwd = p.Norm("..");
			reply(p, "250 OK");
			return true;
		case "PASV": {
			var port = opendata(p);
			if (port <= 0) {
				reply(p, "425 无法建立数据连接");
				return true;
			}
			var addr = p.Sock.LocalEndPoint as IPEndPoint;
			var ip = addr != null ? addr.Address.ToString() : "127.0.0.1";
			if (ip == "::1") ip = "127.0.0.1";
			var parts = ip.Split('.');
			if (parts.Length != 4) ip = "127.0.0.1";
			parts = ip.Split('.');
			reply(p, $"227 Entering Passive Mode ({parts[0]},{parts[1]},{parts[2]},{parts[3]},"
				+ $"{port / 256},{port % 256})");
			return true;
		}
		case "EPSV": {
			var port = opendata(p);
			if (port <= 0) {
				reply(p, "425 无法建立数据连接");
				return true;
			}
			reply(p, $"229 Entering Extended Passive Mode (|||{port}|)");
			return true;
		}
		case "PORT": {
			var ok = parseport(arg, out var host, out var port);
			if (!ok) {
				reply(p, "501 参数无效");
				return true;
			}
			try {
				var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				s.Connect(host, port);
				stoplistener(p.DataListener);
				p.DataListener = null;
				p.DataSock = s;
				reply(p, "200 已连接");
			}
			catch (Exception ex) {
				reply(p, "425 " + ex.Message);
			}
			return true;
		}
		case "LIST":
		case "NLST":
			return listsend(p, arg, verb == "NLST");
		case "RETR":
			return retr(p, arg);
		case "STOR":
		case "APPE":
			return stor(p, arg, verb == "APPE");
		case "SIZE": {
			var full = p.Map(arg);
			if (full == null || !File.Exists(full)) {
				reply(p, "550 文件不存在");
				return true;
			}
			reply(p, $"213 {new FileInfo(full).Length}");
			return true;
		}
		case "MDTM": {
			var full = p.Map(arg);
			if (full == null || !File.Exists(full)) {
				reply(p, "550 文件不存在");
				return true;
			}
			reply(p, "213 " + new FileInfo(full).LastWriteTimeUtc.ToString("yyyyMMddHHmmss"));
			return true;
		}
		case "DELE": {
			var full = p.Map(arg);
			if (full == null || !File.Exists(full)) {
				reply(p, "550 文件不存在");
				return true;
			}
			try {
				File.Delete(full);
				reply(p, "250 已删除");
			}
			catch (Exception ex) { reply(p, "550 " + ex.Message); }
			return true;
		}
		case "MKD":
		case "XMKD": {
			var full = p.Map(arg);
			if (full == null) {
				reply(p, "550 路径无效");
				return true;
			}
			try {
				Directory.CreateDirectory(full);
				reply(p, $"257 \"{p.Norm(arg)}\" 已创建");
			}
			catch (Exception ex) { reply(p, "550 " + ex.Message); }
			return true;
		}
		case "RMD":
		case "XRMD": {
			var full = p.Map(arg);
			if (full == null || !Directory.Exists(full)) {
				reply(p, "550 目录不存在");
				return true;
			}
			try {
				Directory.Delete(full, true);
				reply(p, "250 已删除");
			}
			catch (Exception ex) { reply(p, "550 " + ex.Message); }
			return true;
		}
		case "RNFR": {
			var full = p.Map(arg);
			if (full == null || (!File.Exists(full) && !Directory.Exists(full))) {
				reply(p, "550 不存在");
				return true;
			}
			p.RenameFrom = full;
			reply(p, "350 请给出新名字");
			return true;
		}
		case "RNTO": {
			if (string.IsNullOrEmpty(p.RenameFrom)) {
				reply(p, "503 先发 RNFR");
				return true;
			}
			var full = p.Map(arg);
			if (full == null) {
				reply(p, "550 路径无效");
				return true;
			}
			try {
				if (Directory.Exists(p.RenameFrom)) Directory.Move(p.RenameFrom, full);
				else File.Move(p.RenameFrom, full);
				reply(p, "250 已改名");
			}
			catch (Exception ex) { reply(p, "550 " + ex.Message); }
			finally { p.RenameFrom = null; }
			return true;
		}
		case "REST":
			if (long.TryParse(arg, out var pos) && pos >= 0) {
				p.Restart = pos;
				reply(p, $"350 从 {pos} 继续");
			}
			else reply(p, "501 参数无效");
			return true;
		case "ABOR":
			closequiet(p.DataSock);
			p.DataSock = null;
			reply(p, "226 已中止");
			return true;
		default:
			reply(p, "502 未实现的命令");
			return true;
		}
	}
	#endregion

	// ==================== 数据连接 ====================
	#region
	/// <summary>开被动端口，返回端口号；失败返回 0。</summary>
	static int opendata(Peer p) {
		stoplistener(p.DataListener);
		closequiet(p.DataSock);
		p.DataSock = null;
		try {
			var l = new TcpListener(IPAddress.Any, 0);
			l.Start();
			p.DataListener = l;
			return ((IPEndPoint)l.LocalEndpoint).Port;
		}
		catch { return 0; }
	}

	/// <summary>取数据连接：被动等客户端连，或返回已经连好的（PORT）。</summary>
	static Socket takedata(Peer p) {
		if (p.DataSock != null) {
			var s = p.DataSock;
			p.DataSock = null;
			return s;
		}
		var l = p.DataListener;
		if (l == null) return null;
		try {
			// 5 秒内客户端必须连上来
			var task = l.AcceptSocketAsync();
			if (!task.Wait(5000)) return null;
			return task.Result;
		}
		catch { return null; }
		finally {
			stoplistener(l);
			p.DataListener = null;
		}
	}

	static bool parseport(string arg, out IPAddress host, out int port) {
		host = null;
		port = 0;
		var parts = (arg ?? "").Split(',');
		if (parts.Length != 6) return false;
		var nums = new int[6];
		for (int i = 0; i < 6; i++)
			if (!int.TryParse(parts[i], out nums[i]) || nums[i] < 0 || nums[i] > 255) return false;
		host = IPAddress.Parse($"{nums[0]}.{nums[1]}.{nums[2]}.{nums[3]}");
		port = nums[4] * 256 + nums[5];
		return port >= 1 && port <= 65535;
	}
	#endregion

	// ==================== 列目录 / 传输 ====================
	#region
	bool listsend(Peer p, string arg, bool namesOnly) {
		var full = p.Map(arg);
		if (full == null || !Directory.Exists(full)) {
			if (full != null && File.Exists(full)) {
				// LIST 一个文件：只回这一行
				var one = listline(new FileInfo(full));
				return senddata(p, Encoding.UTF8.GetBytes(one + "\r\n"));
			}
			reply(p, "550 目录不存在");
			return true;
		}
		var sb = new StringBuilder();
		try {
			foreach (var d in Directory.GetDirectories(full)) {
				if (namesOnly) sb.Append(Path.GetFileName(d) + "\r\n");
				else sb.Append(listline(new DirectoryInfo(d)) + "\r\n");
			}
			foreach (var f in Directory.GetFiles(full)) {
				if (namesOnly) sb.Append(Path.GetFileName(f) + "\r\n");
				else sb.Append(listline(new FileInfo(f)) + "\r\n");
			}
		}
		catch (Exception ex) {
			reply(p, "550 " + ex.Message);
			return true;
		}
		return senddata(p, Encoding.UTF8.GetBytes(sb.ToString()));
	}

	/// <summary>Unix 风格的 LIST 行，客户端拿它看类型、大小和时间。</summary>
	static string listline(FileSystemInfo fi) {
		var isdir = fi is DirectoryInfo || (fi.Attributes & FileAttributes.Directory) != 0;
		var size = 0L;
		try { size = (fi as FileInfo)?.Length ?? 0; } catch { }
		var time = fi.LastWriteTime.ToString("MMM dd HH:mm", CultureInfo.InvariantCulture);
		var perm = isdir ? "drwxr-xr-x" : "-rw-r--r--";
		var name = fi.Name;
		// 名字以空格结尾或含特殊字符时不影响解析，客户端按空格后的整体当名字
		return $"{perm} 1 ftp ftp {size,12} {time} {name}";
	}

	bool retr(Peer p, string arg) {
		var full = p.Map(arg);
		if (full == null || !File.Exists(full)) {
			reply(p, "550 文件不存在");
			return true;
		}
		var sock = takedata(p);
		if (sock == null) {
			reply(p, "425 无法建立数据连接");
			return true;
		}
		var name = Path.GetFileName(full);
		try {
			reply(p, "150 开始传输");
			using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
			if (p.Restart > 0 && p.Restart < fs.Length) fs.Seek(p.Restart, SeekOrigin.Begin);
			p.Restart = 0;
			var buf = new byte[64 * 1024];
			long total = 0;
			while (true) {
				var n = fs.Read(buf, 0, buf.Length);
				if (n <= 0) break;
				sock.Send(buf, 0, n, SocketFlags.None);
				total += n;
			}
			try { sock.Shutdown(SocketShutdown.Send); } catch { }
			closequiet(sock);
			reply(p, "226 传输完成");
			log($"{p.Remote} 下载 {name} {total} 字节");
		}
		catch (Exception ex) {
			closequiet(sock);
			reply(p, "426 " + ex.Message);
			log($"{p.Remote} 下载 {name} 失败: {ex.Message}");
		}
		return true;
	}

	bool stor(Peer p, string arg, bool append) {
		var full = p.Map(arg);
		if (full == null) {
			reply(p, "550 路径无效");
			return true;
		}
		var dir = Path.GetDirectoryName(full);
		if (!string.IsNullOrEmpty(dir)) {
			try { Directory.CreateDirectory(dir); }
			catch (Exception ex) {
				reply(p, "550 " + ex.Message);
				return true;
			}
		}
		var sock = takedata(p);
		if (sock == null) {
			reply(p, "425 无法建立数据连接");
			return true;
		}
		var name = Path.GetFileName(full);
		try {
			reply(p, "150 开始传输");
			var mode = append || p.Restart > 0 ? FileMode.OpenOrCreate : FileMode.Create;
			using var fs = new FileStream(full, mode, FileAccess.Write, FileShare.None);
			if (append) fs.Seek(0, SeekOrigin.End);
			else if (p.Restart > 0) {
				if (p.Restart <= fs.Length) fs.SetLength(p.Restart);
				fs.Seek(Math.Min(p.Restart, fs.Length), SeekOrigin.Begin);
			}
			p.Restart = 0;
			var buf = new byte[64 * 1024];
			long total = 0;
			while (true) {
				int n;
				try { n = sock.Receive(buf); }
				catch { break; }
				if (n <= 0) break;
				fs.Write(buf, 0, n);
				total += n;
			}
			fs.Flush();
			closequiet(sock);
			reply(p, "226 传输完成");
			log($"{p.Remote} 上传 {name} {total} 字节");
		}
		catch (Exception ex) {
			closequiet(sock);
			reply(p, "426 " + ex.Message);
			log($"{p.Remote} 上传 {name} 失败: {ex.Message}");
		}
		return true;
	}

	/// <summary>把整块数据发到数据连接，回 150/226。</summary>
	bool senddata(Peer p, byte[] data) {
		var sock = takedata(p);
		if (sock == null) {
			reply(p, "425 无法建立数据连接");
			return true;
		}
		try {
			reply(p, "150 开始传输");
			sock.Send(data, 0, data.Length, SocketFlags.None);
			try { sock.Shutdown(SocketShutdown.Send); } catch { }
			closequiet(sock);
			reply(p, "226 传输完成");
		}
		catch (Exception ex) {
			closequiet(sock);
			reply(p, "426 " + ex.Message);
		}
		return true;
	}
	#endregion

	// ==================== 路径与连接状态 ====================
	#region
	/// <summary>一条控制连接的会话状态。</summary>
	sealed class Peer {
		public Socket Sock;
		public NetworkStream Stream;
		public string Remote = "";
		public string Root = "";
		public string Cwd = "/";
		public string User = "";
		public bool Authed;
		public long Restart;
		public string RenameFrom;

		public TcpListener DataListener;
		public Socket DataSock;

		/// <summary>把 FTP 路径映射成磁盘路径，越界返回 null。</summary>
		public string Map(string ftpPath) {
			try {
				var rel = (ftpPath ?? "").Trim().Replace('\\', '/');
				if (rel.StartsWith("/", StringComparison.Ordinal)) {
					// 绝对路径：相对根目录
				}
				else {
					// 相对路径：相对当前目录
					rel = Cwd.TrimEnd('/') + "/" + rel;
				}
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
				var full = Path.GetFullPath(Path.Combine(Root, string.Join(Path.DirectorySeparatorChar.ToString(), parts)));
				var rootFull = Path.GetFullPath(Root);
				if (!full.Equals(rootFull, StringComparison.OrdinalIgnoreCase)
					&& !full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
					return null;
				return full;
			}
			catch { return null; }
		}

		/// <summary>把 FTP 路径规整成以 / 开头的绝对形式。</summary>
		public string Norm(string ftpPath) {
			var rel = (ftpPath ?? "").Trim().Replace('\\', '/');
			if (!rel.StartsWith("/", StringComparison.Ordinal)) rel = Cwd.TrimEnd('/') + "/" + rel;
			var parts = new List<string>();
			foreach (var seg in rel.Split('/')) {
				if (seg.Length == 0 || seg == ".") continue;
				if (seg == "..") {
					if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
					continue;
				}
				parts.Add(seg);
			}
			return "/" + string.Join("/", parts);
		}
	}
	#endregion
}
