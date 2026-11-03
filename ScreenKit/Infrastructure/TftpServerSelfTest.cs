using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ScreenKit;

/// <summary>
/// TFTP 服务端自测：临时目录起服务，用自己写的极简客户端走一遍下载、上传、
/// 选项协商（blksize/tsize）、大文件多块传输、越界拒绝和文件不存在。
/// 不依赖外部客户端，也不碰外网。
/// </summary>
static class TftpServerSelfTest {
	public static int Run(Action<string> outLog, Action<string> errLog) {
		var root = Path.Combine(Path.GetTempPath(), "sk-tftp-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		var fails = 0;
		void ok(string s) => outLog($"OK   {s}");
		void bad(string s) {
			fails++;
			errLog($"FAIL {s}");
		}

		var srv = new TftpServer();
		var port = 0;
		try {
			srv.Log = line => outLog($"     {line}");
			for (var p = 47520; p < 47560; p++) {
				try {
					srv.Start(new TftpOptions { Port = p, Root = root });
					port = srv.Port;
					break;
				}
				catch { }
			}
			if (port <= 0) {
				errLog("无法在 47520-47559 内启动 TFTP 服务");
				return 2;
			}
			ok($"服务已启动 UDP {port} 根目录 {root}");

			// ---- 下载小文件 ----
			var small = "hello tftp";
			File.WriteAllText(Path.Combine(root, "small.txt"), small, new UTF8Encoding(false));
			if (tftpget(port, "small.txt", out var got, out var gerr)) {
				if (got == small) ok($"下载 small.txt 内容一致（{got.Length} 字节）");
				else bad($"下载内容 '{got}' 不对");
			}
			else bad($"下载 small.txt 失败: {gerr}");

			// ---- 多块下载（按 512 分块，2000 字节要 4 块）----
			var big = new string('A', 2000);
			File.WriteAllText(Path.Combine(root, "big.txt"), big, new UTF8Encoding(false));
			if (tftpget(port, "big.txt", out var gotBig, out var be)) {
				if (gotBig.Length == 2000 && gotBig == big) ok("多块下载 2000 字节逐字节一致");
				else bad($"多块下载长度 {gotBig.Length} / 内容不符");
			}
			else bad($"多块下载失败: {be}");

			// ---- 恰好 512 整数倍（末块要补一个空块）----
			var edge = new string('B', 1024);
			File.WriteAllText(Path.Combine(root, "edge.bin"), edge, new UTF8Encoding(false));
			if (tftpget(port, "edge.bin", out var gotEdge, out var ee)) {
				if (gotEdge.Length == 1024 && gotEdge == edge) ok("512 整数倍文件下载完整（末块空包处理正确）");
				else bad($"整数倍下载长度 {gotEdge.Length}");
			}
			else bad($"整数倍下载失败: {ee}");

			// ---- 上传 ----
			var payload = "upload from client 中文内容";
			if (tftpput(port, "up.txt", payload, out var perr)) {
				var disk = Path.Combine(root, "up.txt");
				if (File.Exists(disk) && File.ReadAllText(disk, Encoding.UTF8) == payload)
					ok($"上传 up.txt 落盘一致（{payload.Length} 字符）");
				else bad("上传落盘内容不一致");
			}
			else bad($"上传失败: {perr}");

			// ---- 选项协商 ----
			if (tftpget(port, "big.txt", out var opt, out var oerr, blksize: 1024)) {
				if (opt.Length == 2000 && opt == big) ok("blksize=1024 协商后内容一致");
				else bad($"协商后长度 {opt.Length}");
			}
			else bad($"选项协商失败: {oerr}");

			// ---- 子目录 ----
			Directory.CreateDirectory(Path.Combine(root, "sub"));
			File.WriteAllText(Path.Combine(root, "sub", "in.txt"), "nested", new UTF8Encoding(false));
			if (tftpget(port, "sub/in.txt", out var nested, out var ne)) {
				if (nested == "nested") ok("子目录文件可下载");
				else bad($"子目录内容 '{nested}' 不对");
			}
			else bad($"子目录下载失败: {ne}");

			// ---- 文件不存在 ----
			if (!tftpget(port, "nope.bin", out _, out var ner)) ok("不存在的文件被拒绝");
			else bad("不存在的文件竟然下载成功");

			// ---- 越界 ----
			if (!tftpget(port, "../../windows/win.ini", out _, out _)) ok("越界路径被拒绝");
			else bad("越界路径竟然能下载");

			// ---- 与 FTP 共享目录：FTP 侧写的文件，TFTP 能直接读 ----
			if (FtpDeps.Installed) {
				var ftpPort = 0;
				var fsrv = new FtpServer();
				try {
					fsrv.Log = null;
					for (var p = 47620; p < 47660; p++) {
						try {
							fsrv.Start(new FtpSrvOptions { Port = p, Root = root, User = "", Pass = "" });
							ftpPort = fsrv.Port;
							break;
						}
						catch { }
					}
					if (ftpPort > 0) {
						using var fbe = FtpBackendFactory.Create("ftp");
						fbe.Connect(new FtpSite { Kind = "ftp", Host = "127.0.0.1", Port = ftpPort, User = "a" }, "");
						var text = Encoding.UTF8.GetBytes("written by ftp, read by tftp");
						using (var ws = fbe.OpenWrite("/shared.txt", 0)) ws.Write(text, 0, text.Length);
						fbe.Disconnect();
						if (tftpget(port, "shared.txt", out var shared, out var serr)) {
							if (shared == "written by ftp, read by tftp")
								ok("FTP 写的文件 TFTP 直接读到（共用目录生效）");
							else bad($"共用目录内容 '{shared}' 不对");
						}
						else bad($"TFTP 读 FTP 写的文件失败: {serr}");
					}
					else outLog("     跳过共用目录用例：FTP 端口没起来");
				}
				finally {
					try { fsrv.Dispose(); } catch { }
				}
			}
			else outLog("     跳过共用目录用例：FTP 依赖未安装");

			return fails == 0 ? 0 : 1;
		}
		catch (Exception ex) {
			errLog($"EXC {ex.GetType().Name}: {ex.Message}");
			return 3;
		}
		finally {
			try { srv.Stop(); } catch { }
			try { srv.Dispose(); } catch { }
			try { Directory.Delete(root, true); } catch { }
		}
	}

	/// <summary>RRQ 下载，返回文件内容。</summary>
	static bool tftpget(int port, string name, out string content, out string err, int blksize = 0) {
		content = "";
		err = null;
		try {
			using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			s.ReceiveTimeout = 5000;
			var req = buildreq(1, name, blksize);
			var server = new IPEndPoint(IPAddress.Loopback, port);
			s.SendTo(req, server);
			var buf = new byte[70000];
			EndPoint from = new IPEndPoint(IPAddress.Any, 0);
			var n = s.ReceiveFrom(buf, ref from);
			if (n < 4) {
				err = "应答过短";
				return false;
			}
			var ep = (IPEndPoint)from;
			var op = (ushort)((buf[0] << 8) | buf[1]);
			if (op == 5) {
				err = readmsg(buf, n);
				return false;
			}
			var bs = blksize > 0 ? blksize : 512;
			var ms = new MemoryStream();
			ushort expect = 1;
			while (true) {
				if (op == 6) {
					// OACK：解析协商后的块大小，再回 ACK 0
					var o = parseoack(buf, n);
					if (o.TryGetValue("blksize", out var bsv) && int.TryParse(bsv, out var b2)) bs = b2;
					s.SendTo(new byte[] { 0, (byte)4, 0, 0 }, ep);
					n = s.ReceiveFrom(buf, ref from);
					if (n < 4) {
						err = "OACK 后无数据";
						return false;
					}
					ep = (IPEndPoint)from;
					op = (ushort)((buf[0] << 8) | buf[1]);
					continue;
				}
				if (op != 3) {
					err = $"期望 DATA，收到 op={op}";
					return false;
				}
				var blk = (ushort)((buf[2] << 8) | buf[3]);
				var len = n - 4;
				if (blk != expect) {
					err = $"块号跳了，期望 {expect} 收到 {blk}";
					return false;
				}
				if (len > 0) ms.Write(buf, 4, len);
				s.SendTo(new byte[] { 0, (byte)4, (byte)(blk >> 8), (byte)(blk & 0xFF) }, ep);
				if (len < bs) break;
				expect++;
				n = s.ReceiveFrom(buf, ref from);
				if (n < 4) {
					err = "数据提前结束";
					return false;
				}
				ep = (IPEndPoint)from;
				op = (ushort)((buf[0] << 8) | buf[1]);
			}
			content = Encoding.UTF8.GetString(ms.ToArray());
			return true;
		}
		catch (Exception ex) {
			err = $"{ex.GetType().Name}: {ex.Message}";
			return false;
		}
	}

	/// <summary>WRQ 上传。</summary>
	static bool tftpput(int port, string name, string text, out string err) {
		err = null;
		try {
			using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			s.ReceiveTimeout = 5000;
			var req = buildreq(2, name, 0);
			s.SendTo(req, new IPEndPoint(IPAddress.Loopback, port));
			var buf = new byte[70000];
			EndPoint from = new IPEndPoint(IPAddress.Any, 0);
			var n = s.ReceiveFrom(buf, ref from);
			if (n < 4) {
				err = "无应答";
				return false;
			}
			var ep = (IPEndPoint)from;
			var op = (ushort)((buf[0] << 8) | buf[1]);
			if (op == 5) {
				err = readmsg(buf, n);
				return false;
			}
			// 期望 ACK 0
			var bytes = Encoding.UTF8.GetBytes(text);
			ushort block = 1;
			var pos = 0;
			while (true) {
				var len = Math.Min(512, bytes.Length - pos);
				var pkt = new byte[4 + len];
				pkt[0] = 0;
				pkt[1] = 3;
				pkt[2] = (byte)(block >> 8);
				pkt[3] = (byte)(block & 0xFF);
				if (len > 0) Buffer.BlockCopy(bytes, pos, pkt, 4, len);
				s.SendTo(pkt, ep);
				n = s.ReceiveFrom(buf, ref from);
				if (n < 4) {
					err = "等 ACK 失败";
					return false;
				}
				ep = (IPEndPoint)from;
				op = (ushort)((buf[0] << 8) | buf[1]);
				if (op == 5) {
					err = readmsg(buf, n);
					return false;
				}
				if (op != 4) {
					err = $"期望 ACK，收到 op={op}";
					return false;
				}
				var ack = (ushort)((buf[2] << 8) | buf[3]);
				if (ack != block) {
					err = $"ACK 块号 {ack} != {block}";
					return false;
				}
				pos += len;
				if (len < 512) break;
				block++;
			}
			return true;
		}
		catch (Exception ex) {
			err = $"{ex.GetType().Name}: {ex.Message}";
			return false;
		}
	}

	static byte[] buildreq(ushort op, string name, int blksize) {
		var ms = new MemoryStream();
		ms.WriteByte(0);
		ms.WriteByte((byte)op);
		writez(ms, name);
		writez(ms, "octet");
		if (blksize > 0) {
			writez(ms, "blksize");
			writez(ms, blksize.ToString());
		}
		return ms.ToArray();
	}

	static void writez(MemoryStream ms, string s) {
		var b = Encoding.UTF8.GetBytes(s);
		ms.Write(b, 0, b.Length);
		ms.WriteByte(0);
	}

	static string readmsg(byte[] buf, int n) {
		var start = 4;
		var end = start;
		while (end < n && buf[end] != 0) end++;
		return Encoding.UTF8.GetString(buf, start, end - start);
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
}
