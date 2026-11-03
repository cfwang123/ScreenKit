using System.Net.Sockets;
using System.Text;

namespace ScreenKit;

/// <summary>
/// FTP 服务端自测：在临时目录起服务，直接用原始命令走一遍登录、LIST、STOR、RETR、SIZE、DELE、MKD、RMD。
/// 不依赖外部客户端，也不碰外网。
/// </summary>
static class FtpServerSelfTest {
	public static int Run(Action<string> outLog, Action<string> errLog) {
		var root = Path.Combine(Path.GetTempPath(), "sk-ftpsrv-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		File.WriteAllText(Path.Combine(root, "hello.txt"), "hello ftp server", new UTF8Encoding(false));
		Directory.CreateDirectory(Path.Combine(root, "sub"));

		var srv = new FtpServer();
		var fails = 0;
		var port = 0;
		void ok(string s) => outLog($"OK   {s}");
		void bad(string s) {
			fails++;
			errLog($"FAIL {s}");
		}

		try {
			srv.Log = line => outLog($"     {line}");
			// 用 0 端口不合适：服务端要固定端口才能连；随便挑高位端口，冲突就退
			for (var p = 47210; p < 47240; p++) {
				try {
					srv.Start(new FtpSrvOptions { Port = p, Root = root, User = "", Pass = "" });
					port = srv.Port;
					break;
				}
				catch { }
			}
			if (port <= 0) {
				errLog("无法在 47210-47239 内启动服务");
				return 2;
			}
			ok($"服务已启动 端口 {port} 根目录 {root}");

			using var c = new Ctrl(port);

			// ---- 匿名登录 ----
			if (c.cmd("USER anonymous").StartsWith("331")) ok("USER anonymous");
			else bad($"USER anonymous -> {c.Last}");
			if (c.cmd("PASS x@x").StartsWith("230")) ok("PASS 匿名登录");
			else bad($"PASS -> {c.Last}");

			// ---- PWD / CWD ----
			if (c.cmd("PWD").Contains("\"/\"")) ok("PWD 是 /");
			else bad($"PWD -> {c.Last}");
			if (c.cmd("CWD /sub").StartsWith("250")) ok("CWD /sub");
			else bad($"CWD -> {c.Last}");
			if (c.cmd("CWD /").StartsWith("250")) ok("CWD /");
			else bad($"CWD 回根 -> {c.Last}");

			// ---- LIST ----
			var listing = c.list("/", out var listErr);
			if (listErr != null) bad($"LIST -> {listErr}");
			else if (!listing.Contains("hello.txt") || !listing.Contains("sub")) bad($"LIST 内容不对: {listing}");
			else ok($"LIST 列出 {listing.Split('\n').Length} 行");

			// ---- RETR ----
			var got = c.retr("/hello.txt", out var retrErr);
			if (retrErr != null) bad($"RETR -> {retrErr}");
			else if (got != "hello ftp server") bad($"RETR 内容 '{got}' 不对");
			else ok("RETR 取回内容一致");

			// ---- SIZE ----
			if (c.cmd("SIZE /hello.txt").StartsWith("213 " + got.Length)) ok($"SIZE {got.Length}");
			else bad($"SIZE -> {c.Last}");

			// ---- STOR ----
			var payload = "abcdefghij0123456789";
			if (c.stor("/up.txt", payload, out var storErr)) ok($"STOR 写入 {payload.Length} 字节");
			else bad($"STOR -> {storErr}");
			var disk = Path.Combine(root, "up.txt");
			if (File.Exists(disk) && File.ReadAllText(disk, Encoding.UTF8) == payload) ok("落盘内容一致");
			else bad("落盘内容不一致");

			// ---- MKD / RMD ----
			if (c.cmd("MKD /newdir").StartsWith("257")) ok("MKD /newdir");
			else bad($"MKD -> {c.Last}");
			if (!Directory.Exists(Path.Combine(root, "newdir"))) bad("MKD 没建出目录");
			if (c.cmd("RMD /newdir").StartsWith("250")) ok("RMD /newdir");
			else bad($"RMD -> {c.Last}");

			// ---- RNFR / RNTO ----
			if (c.cmd("RNFR /up.txt").StartsWith("350") && c.cmd("RNTO /renamed.txt").StartsWith("250"))
				ok("RNFR/RNTO 改名");
			else bad($"RNFR/RNTO -> {c.Last}");

			// ---- DELE ----
			if (c.cmd("DELE /renamed.txt").StartsWith("250")) ok("DELE");
			else bad($"DELE -> {c.Last}");
			if (File.Exists(Path.Combine(root, "renamed.txt"))) bad("DELE 没删掉");

			// ---- 越界防护：爬到根之上会被夹在根目录，不能真的跳出共享目录 ----
			c.cmd("CWD /../../..");
			if (c.cmd("PWD").Contains("\"/\"")) ok("越界路径被夹回根目录");
			else bad($"越界后 PWD -> {c.Last}");

			// ---- 密码模式：换个端口重开带用户的实例 ----
			c.cmd("QUIT");
			srv.Stop();
			var port2 = 0;
			for (var p = port + 1; p < port + 30; p++) {
				try {
					srv.Start(new FtpSrvOptions { Port = p, Root = root, User = "bob", Pass = "secret" });
					port2 = srv.Port;
					break;
				}
				catch { }
			}
			if (port2 <= 0) {
				bad("密码模式起不来");
				return 4;
			}
			using var c2 = new Ctrl(port2);
			c2.cmd("USER bob");
			if (c2.cmd("PASS wrong").StartsWith("530")) ok("错误密码被拒");
			else bad($"错误密码 -> {c2.Last}");
			c2.cmd("USER bob");
			if (c2.cmd("PASS secret").StartsWith("230")) ok("正确密码可登录");
			else bad($"正确密码 -> {c2.Last}");

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

	/// <summary>
	/// 端到端互通：用本项目自己的 FluentFTP 后端连本机 FTP 服务端，
	/// 走完连接、列目录、上传、下载比对、删除。比原始命令测试更接近真实客户端。
	/// </summary>
	public static int RunClient(Action<string> outLog, Action<string> errLog) {
		if (!FtpDeps.Installed) {
			errLog($"依赖未安装，缺: {string.Join(", ", FtpDeps.Missing())}");
			return 2;
		}
		var root = Path.Combine(Path.GetTempPath(), "sk-ftpc-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		var srv = new FtpServer();
		var fails = 0;
		try {
			srv.Log = line => outLog($"     {line}");
			var port = 0;
			for (var p = 47310; p < 47340; p++) {
				try {
					srv.Start(new FtpSrvOptions { Port = p, Root = root, User = "bob", Pass = "secret" });
					port = srv.Port;
					break;
				}
				catch { }
			}
			if (port <= 0) {
				errLog("无法启动服务");
				return 2;
			}
			outLog($"OK   服务已启动 端口 {port}");

			using var be = FtpBackendFactory.Create("ftp");
			var site = new FtpSite { Kind = "ftp", Host = "127.0.0.1", Port = port, User = "bob" };
			be.Connect(site, "secret");
			outLog("OK   客户端连上服务端");

			var list = be.List("/");
			outLog($"OK   列目录 {list.Count} 项");

			var payload = Encoding.UTF8.GetBytes("end to end via FluentFTP " + new string('x', 5000));
			var remote = "/e2e.bin";
			using (var ws = be.OpenWrite(remote, 0)) ws.Write(payload, 0, payload.Length);
			outLog($"OK   上传 {payload.Length} 字节");
			var size = be.Size(remote);
			if (size == payload.Length) outLog($"OK   远端大小 {size}");
			else {
				fails++;
				errLog($"FAIL 远端大小 {size} != {payload.Length}");
			}

			using (var rs = be.OpenRead(remote, 0, 0)) {
				using var ms = new MemoryStream();
				rs.CopyTo(ms);
				var got = ms.ToArray();
				if (got.Length == payload.Length && got.AsSpan().SequenceEqual(payload))
					outLog($"OK   下载内容一致 {got.Length} 字节");
				else {
					fails++;
					errLog($"FAIL 下载 {got.Length} 字节与上传不一致");
				}
			}

			be.RmFile(remote);
			var after = be.List("/");
			if (after.Count == 0) outLog("OK   删除后目录为空");
			else {
				fails++;
				errLog($"FAIL 删除后还剩 {after.Count} 项");
			}
			be.Disconnect();
			outLog("OK   客户端断开");
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

	/// <summary>
	/// 界面自测：造主窗口，确认 FTP 服务页的控件在、状态栏项在、
	/// 端口输入框能读写，以及配置里新增的字段能存进 config.toml。
	/// </summary>
	/// <summary>把字形反查回分类名，只给自测用。</summary>
	static string glyphkind(string g) => g switch {
		"\uE8B7" => "folder",
		"\uEB9F" => "image",
		"\uE714" => "video",
		"\uE8D6" => "audio",
		"\uF012" => "zip",
		"\uEA90" => "pdf",
		"\uE943" => "code",
		"\uE7B8" => "exe",
		"\uE8A5" => "doc",
		"\uE7C3" => "file",
		_ => "file"
	};

	public static int UiSmoke(Action<string> outLog, Action<string> errLog) {
		var fails = 0;
		void ok(string s) => outLog($"OK   {s}");
		void bad(string s) {
			fails++;
			errLog($"FAIL {s}");
		}
		try {
			var w = new MainWindow();
			try {
				// 页面控件
				var tab = w.FindName("tabftpsrv") as System.Windows.Controls.TabItem;
				if (tab == null) bad("找不到 FTP服务 页");
				else ok($"FTP服务 页存在，标题 {tab.Header}");

				foreach (var (name, desc) in new[] {
					("eftpsrvdir", "根目录输入框"),
					("eftpsrvport", "端口输入框"),
					("eftpsrvuser", "用户名输入框"),
					("eftpsrvpass", "密码框"),
					("bftpsrvtoggle", "启停按钮"),
					("bftpsrvbrowse", "浏览按钮"),
					("lbftpsrvstat", "状态行"),
					("eftpsrvlog", "日志框"),
					("etftpport", "TFTP 端口输入框"),
					("btftptoggle", "TFTP 启停按钮"),
					("lbftpsrvtftpstat", "TFTP 状态行"),
					("lbftpsrvtftphint", "TFTP 说明"),
				}) {
					if (w.FindName(name) == null) bad($"缺 {desc}({name})");
					else ok($"{desc} 在");
				}

				// 状态栏项
				foreach (var name in new[] { "bstftpsrv", "lstftpsrvsep" }) {
					if (w.FindName(name) == null) bad($"缺状态栏 {name}");
				}
				var sep = w.FindName("lstftpsrvsep") as System.Windows.FrameworkElement;
				if (sep != null && sep.Visibility != System.Windows.Visibility.Collapsed)
					bad("默认状态下状态栏分隔符不该显示");
				else ok("默认不显示 FTP 状态（服务未启用）");

				// 配置默认值
				var o = new OcrOptions();
				if (o.FtpSrvEnabled) bad("FtpSrvEnabled 默认应为 false");
				else ok("默认不启用");
				if (o.FtpSrvPort != 21) bad($"默认端口应为 21，实际 {o.FtpSrvPort}");
				else ok("默认端口 21");
				if (!o.FtpSrvOpts().Anonymous) bad("用户名留空应算匿名");
				else ok("用户名留空=匿名");
				if (o.TabFtpSrvVisible) bad("TabFtpSrvVisible 默认应为 false");
				else ok("默认不显示页面");

				// TFTP：默认关、端口 69、目录跟 FTP 共用
				if (o.TftpEnabled) bad("TftpEnabled 默认应为 false");
				else ok("TFTP 默认不启用");
				if (o.TftpPort != 69) bad($"TFTP 默认端口应为 69，实际 {o.TftpPort}");
				else ok("TFTP 默认端口 69");
				var shared = new OcrOptions { FtpSrvRoot = @"D:\shared-test" };
				if (shared.TftpOpts().Root != @"D:\shared-test")
					bad("TFTP 没跟 FTP 共用根目录");
				else ok("TFTP 与 FTP 共用根目录");
				var tftpBogus = new TftpOptions { Port = 99999 };
				if (tftpBogus.PortOr() != 69) bad("TFTP 越界端口没退回 69");
				else ok("TFTP 越界端口退回 69");

				// ---- 图标按类型分类 ----
				var seen = new Dictionary<string, string>();
				foreach (var (file, want) in new[] {
					(".", "folder"),
					("a.jpg", "image"), ("a.PNG", "image"), ("a.webp", "image"),
					("a.mp4", "video"), ("a.mkv", "video"),
					("a.mp3", "audio"), ("a.flac", "audio"),
					("a.zip", "zip"), ("a.7z", "zip"), ("a.tar.gz", "zip"),
					("a.pdf", "pdf"),
					("a.cs", "code"), ("a.json", "code"), ("a.py", "code"),
					("a.docx", "doc"), ("a.txt", "doc"), ("a.csv", "doc"),
					("a.exe", "exe"), ("a.dll", "exe"), ("a.apk", "exe"),
					("a.unknownext", "file"), ("noext", "file"),
					(".gitignore", "file"),
				}) {
					bool isdir = file == ".";
					var g = FtpIcon.Glyph(isdir ? "somedir" : file, isdir);
					var got = glyphkind(g);
					if (got != want) bad($"图标分类 {file} 得到 {got}，期望 {want}");
					else seen[want] = g;
				}
				if (seen.Count >= 8) ok($"图标分 {seen.Count} 类且全部正确");
				else bad($"只有 {seen.Count} 类图标被验证到");

				// 不同类型必须给不同字形
				if (FtpIcon.Glyph("x.jpg", false) != FtpIcon.Glyph("x.mp4", false))
					ok("图片与视频图标不同");
				else bad("图片与视频用了同一个图标");
				if (FtpIcon.Glyph("dir", true) != FtpIcon.Glyph("x.txt", false))
					ok("文件夹与文件图标不同");
				else bad("文件夹与文件用了同一个图标");

				// 颜色也得有区分，且都是合法色值
				if (FtpIcon.Color("d", true) == FtpIcon.Color("a.png", false)) bad("文件夹与图片同色");
				else ok("文件夹与图片颜色不同");
				foreach (var (nm, d) in new[] { ("d", true), ("a.png", false), ("a.mp4", false),
					("a.zip", false), ("a.xyz", false) }) {
					var c = FtpIcon.Color(nm, d);
					if (!c.StartsWith("#") || c.Length != 7) bad($"颜色值格式不对: {c}");
					else {
						try { System.Windows.Media.ColorConverter.ConvertFromString(c); }
						catch { bad($"颜色无法解析: {c}"); }
					}
				}
				ok("颜色值都是合法 #RRGGBB");

				// FtpEntry 上要能拿到图标与颜色
				var ent = new FtpEntry { Name = "movie.mp4", IsDir = false };
				if (string.IsNullOrEmpty(ent.Icon)) bad("FtpEntry.Icon 为空");
				else if (string.IsNullOrEmpty(ent.IconBrush)) bad("FtpEntry.IconBrush 为空");
				else ok("FtpEntry 上能取到图标与颜色");

				// ---- 协议表里必须有 tftp，且默认端口 69 ----
				if (Array.IndexOf(FtpProto.All, FtpProto.Tftp) < 0) bad("协议表里没有 tftp");
				else ok("协议表含 tftp");
				if (FtpProto.DefPort(FtpProto.Tftp) != 69) bad("tftp 默认端口不是 69");
				else ok("tftp 默认端口 69");
				if (!FtpProto.IsTftp("TFTP")) bad("IsTftp 大小写不敏感失败");
				else ok("IsTftp 认大写 TFTP");
				if (FtpProto.IsTftp(FtpProto.Sftp)) bad("IsTftp 把 sftp 认成 tftp 了");
				else ok("IsTftp 不会误认 sftp");
				if (FtpBackendFactory.NeedsDeps(FtpProto.Tftp)) bad("tftp 不该要求装库");
				else ok("tftp 不需要第三方库");
				if (!FtpBackendFactory.NeedsDeps(FtpProto.Ftp)) bad("ftp 应该要求装库");
				else ok("ftp 仍需第三方库");

				// ---- FTP 窗口里 TFTP 的控件与模式切换 ----
				var fw = new FtpWindow();
				try {
					foreach (var (name, desc) in new[] {
						("tftptip", "TFTP 说明区"),
						("etftpname", "TFTP 文件名框"),
						("btftpget", "TFTP 下载按钮"),
						("lbtftptiptext", "TFTP 说明文字"),
					}) {
						if (fw.FindName(name) == null) bad($"FTP 窗口缺 {desc}({name})");
						else ok($"FTP 窗口 {desc} 在");
					}
					var tip = fw.FindName("tftptip") as System.Windows.FrameworkElement;
					if (tip != null && tip.Visibility != System.Windows.Visibility.Collapsed)
						bad("TFTP 说明区默认不该显示");
					else ok("TFTP 说明区默认隐藏");
					var cb = fw.FindName("cbKind") as System.Windows.Controls.ComboBox;
					if (cb == null) bad("找不到协议下拉框");
					else if (cb.Items.Count != FtpProto.All.Length)
						bad($"协议项数 {cb.Items.Count} 与 FtpProto.All 不一致");
					else ok($"协议下拉 {cb.Items.Count} 项（含 TFTP）");
				}
				finally {
					try { fw.Close(); } catch { }
				}

				// 端口越界回收
				var bogus = new FtpSrvOptions { Port = 99999 };
				if (bogus.PortOr() != 21) bad("越界端口没退回 21");
				else ok("越界端口退回 21");

				// 默认根目录
				var root = new FtpSrvOptions().RootPath;
				if (!root.EndsWith("ftproot", StringComparison.OrdinalIgnoreCase))
					bad($"默认根目录应为 ftproot，实际 {root}");
				else ok($"默认根目录 {root}");

				return fails == 0 ? 0 : 1;
			}
			finally {
				try { w.Close(); } catch { }
			}
		}
		catch (Exception ex) {
			errLog($"EXC {ex.GetType().Name}: {ex.Message}");
			return 3;
		}
	}

	/// <summary>极简 FTP 控制连接，够自测用。</summary>
	sealed class Ctrl : IDisposable {
		readonly TcpClient tcp;
		readonly NetworkStream st;
		readonly byte[] buf = new byte[1];

		public string Last { get; private set; } = "";

		public Ctrl(int port) {
			tcp = new TcpClient();
			tcp.Connect("127.0.0.1", port);
			tcp.ReceiveTimeout = 8000;
			st = tcp.GetStream();
			Last = readreply();
		}

		/// <summary>读一条（或多行 1xx-2xx）应答，返回首行。</summary>
		string readreply() {
			var first = readline();
			if (first == null) return "";
			if (first.Length >= 4 && first[3] == '-') {
				// 多行：读到 "code " 开头为止
				while (true) {
					var one = readline();
					if (one == null) break;
					if (one.StartsWith(first.Substring(0, 3) + " ", StringComparison.Ordinal)) break;
				}
			}
			return first;
		}

		string readline() {
			var sb = new StringBuilder();
			while (true) {
				int n;
				try { n = st.Read(buf, 0, 1); }
				catch { return sb.Length > 0 ? sb.ToString() : null; }
				if (n <= 0) return sb.Length > 0 ? sb.ToString() : null;
				var ch = (char)buf[0];
				if (ch == '\n') break;
				if (ch == '\r') continue;
				sb.Append(ch);
			}
			return sb.ToString();
		}

		public string cmd(string line) {
			var data = Encoding.UTF8.GetBytes(line + "\r\n");
			st.Write(data, 0, data.Length);
			st.Flush();
			Last = readreply();
			return Last;
		}

		/// <summary>开被动连接执行一次数据传输，返回所有字节。</summary>
		byte[] pasv(Action<TcpClient> transfer) {
			var r = cmd("PASV");
			var port = parsepasv(r);
			if (port <= 0) return null;
			using var d = new TcpClient();
			d.Connect("127.0.0.1", port);
			d.ReceiveTimeout = 8000;
			transfer(d);
			using var ms = new MemoryStream();
			try {
				var b = new byte[8192];
				while (true) {
					var n = d.GetStream().Read(b, 0, b.Length);
					if (n <= 0) break;
					ms.Write(b, 0, n);
				}
			}
			catch { }
			readreply();
			return ms.ToArray();
		}

		static int parsepasv(string r) {
			// 227 Entering Passive Mode (h1,h2,h3,h4,p1,p2)
			var i = r.IndexOf('(');
			var j = r.IndexOf(')', i < 0 ? 0 : i);
			if (i < 0 || j < 0) return 0;
			var parts = r.Substring(i + 1, j - i - 1).Split(',');
			if (parts.Length != 6) return 0;
			if (!int.TryParse(parts[4], out var p1) || !int.TryParse(parts[5], out var p2)) return 0;
			return p1 * 256 + p2;
		}

		public string list(string path, out string err) {
			err = null;
			var data = pasv(d => { cmd("LIST " + path); });
			if (data == null) {
				err = "PASV 失败";
				return "";
			}
			return Encoding.UTF8.GetString(data);
		}

		public string retr(string path, out string err) {
			err = null;
			var data = pasv(d => { cmd("RETR " + path); });
			if (data == null) {
				err = "PASV 失败";
				return "";
			}
			return Encoding.UTF8.GetString(data);
		}

		public bool stor(string path, string text, out string err) {
			err = null;
			var bytes = Encoding.UTF8.GetBytes(text);
			var data = pasv(d => {
				cmd("STOR " + path);
				d.GetStream().Write(bytes, 0, bytes.Length);
				d.GetStream().Flush();
				try { d.Client.Shutdown(SocketShutdown.Send); } catch { }
			});
			if (data == null) {
				err = "PASV 失败";
				return false;
			}
			return true;
		}

		public void Dispose() {
			try { cmd("QUIT"); } catch { }
			try { st.Dispose(); } catch { }
			try { tcp.Close(); } catch { }
		}
	}
}
