using System.Text;

namespace ScreenKit;

/// <summary>
/// TFTP 客户端（工具里的「FTP 文件管理」）自测：
/// 起一个本机 TFTP 服务端，用客户端后端跑完整的上传 / 下载 / 大小探测，
/// 再验证不支持的操作确实抛 NotSupportedException。
/// </summary>
static class TftpClientSelfTest {
	public static int Run(Action<string> outLog, Action<string> errLog) {
		var root = Path.Combine(Path.GetTempPath(), "sk-tftpc-" + Guid.NewGuid().ToString("N"));
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
			for (var p = 47720; p < 47760; p++) {
				try {
					srv.Start(new TftpOptions { Port = p, Root = root });
					port = srv.Port;
					break;
				}
				catch { }
			}
			if (port <= 0) {
				errLog("无法启动 TFTP 服务");
				return 2;
			}
			ok($"服务已启动 UDP {port}");

			var site = new FtpSite { Kind = FtpProto.Tftp, Host = "127.0.0.1", Port = port };
			using var be = FtpBackendFactory.Create(FtpProto.Tftp);
			be.Connect(site, "");
			ok("客户端连上（TFTP 无登录）");

			// ---- 上传 ----
			var payload = Encoding.UTF8.GetBytes(new string('T', 3000) + "尾部中文");
			using (var ws = be.OpenWrite("/upload.bin", 0))
				ws.Write(payload, 0, payload.Length);
			var disk = Path.Combine(root, "upload.bin");
			if (File.Exists(disk)) {
				var got = File.ReadAllBytes(disk);
				if (got.Length == payload.Length && got.AsSpan().SequenceEqual(payload))
					ok($"上传 {payload.Length} 字节，落盘逐字节一致");
				else bad($"上传落盘 {got.Length} 字节不匹配");
			}
			else bad("上传后文件不存在");

			// ---- 大小探测 ----
			var size = be.Size("/upload.bin");
			if (size == payload.Length) ok($"tsize 探测到 {size} 字节");
			else bad($"tsize 探测 {size} != {payload.Length}");

			// ---- 下载 ----
			using (var rs = be.OpenRead("/upload.bin", 0, size)) {
				using var ms = new MemoryStream();
				rs.CopyTo(ms);
				var got = ms.ToArray();
				if (got.Length == payload.Length && got.AsSpan().SequenceEqual(payload))
					ok($"下载 {got.Length} 字节逐字节一致");
				else bad($"下载 {got.Length} 字节不匹配");
			}

			// ---- 恰好整块 ----
			var exact = Encoding.UTF8.GetBytes(new string('E', 1024));
			using (var ws = be.OpenWrite("/exact.bin", 0))
				ws.Write(exact, 0, exact.Length);
			var exactSize = be.Size("/exact.bin");
			using (var rs = be.OpenRead("/exact.bin", 0, 0)) {
				using var ms = new MemoryStream();
				rs.CopyTo(ms);
				if (ms.Length == exact.Length) ok("整块文件上传下载长度正确");
				else bad($"整块文件长度 {ms.Length} != {exact.Length}");
			}

			// ---- 不支持的操作 ----
			if (throws(() => be.List("/"))) ok("List 按预期抛 NotSupported");
			else bad("List 竟然没抛异常");
			if (throws(() => be.RmFile("/x"))) ok("RmFile 按预期抛 NotSupported");
			else bad("RmFile 竟然没抛异常");
			if (throws(() => be.MkDir("/d"))) ok("MkDir 按预期抛 NotSupported");
			else bad("MkDir 竟然没抛异常");

			// ---- 续传：TFTP 恒从头 ----
			if (be.AlignResume("/upload.bin", 100) == 0) ok("AlignResume 恒返回 0（无续传）");
			else bad("AlignResume 不该返回非 0");
			if (throws(() => be.OpenWrite("/x", 100))) ok("带偏移写入按预期拒绝");
			else bad("带偏移写入竟然允许了");

			be.Disconnect();
			ok("断开");

			return fails == 0 ? 0 : 1;
		}
		catch (Exception ex) {
			errLog($"EXC {ex.GetType().Name}: {ex.Message}");
			return 3;
		}
		finally {
			try { srv.Dispose(); } catch { }
			try { Directory.Delete(root, true); } catch { }
		}
	}

	static bool throws(Action act) {
		try { act(); return false; }
		catch (NotSupportedException) { return true; }
		catch { return false; }
	}
}
