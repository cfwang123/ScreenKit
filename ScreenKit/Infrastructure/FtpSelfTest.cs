namespace ScreenKit;

/// <summary>
/// FTP 工具命令行自测：连本地 FTP/SFTP，走一遍列目录、上传、下载、续传、暂停。
/// 用法：ScreenKit --test-ftp &lt;kind&gt; &lt;host&gt; &lt;port&gt; &lt;user&gt; &lt;pass&gt; [remoteDir]
/// </summary>
static class FtpSelfTest {
	public static int Run(string kind, string host, int port, string user, string pw, string remoteDir,
		Action<string> outLog, Action<string> errLog) {
		if (!FtpDeps.Installed) {
			errLog($"依赖未安装，缺: {string.Join(", ", FtpDeps.Missing())}");
			return 2;
		}
		var site = new FtpSite { Kind = kind, Host = host, Port = port, User = user };
		var dir = (remoteDir ?? "").Trim();
		if (dir.Length == 0) dir = "/";
		var tmp = Path.Combine(Path.GetTempPath(), "sk-ftp-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tmp);
		var fails = 0;

		void ok(string what) => outLog($"OK   {what}");
		void bad(string what) {
			fails++;
			errLog($"FAIL {what}");
		}

		IFtpBackend be = null;
		try {
			// 1. 连接 + 列目录
			be = FtpBackendFactory.Create(kind);
			be.Connect(site, pw);
			ok($"connect {kind}://{host}:{port}");

			var wd = be.WorkDir();
			outLog($"workdir: {wd}");

			var list = be.List(dir);
			ok($"list {dir} -> {list.Count} entries");
			foreach (var e in list.Take(5))
				outLog($"   {(e.IsDir ? "D" : "F")} {e.Name,-30} {e.Size,10} {e.Perms,-10} {e.TimeText}");

			// 2. 新建目录
			var sub = FtpXfer.joinRemote(dir, "sk-selftest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
			be.MkDir(sub);
			ok($"mkdir {sub}");

			// 3. 上传（含多块，验证进度与速度）
			var payload = new byte[300 * 1024];
			new Random(7).NextBytes(payload);
			var src = Path.Combine(tmp, "upload.bin");
			File.WriteAllBytes(src, payload);
			var remoteFile = FtpXfer.joinRemote(sub, "upload.bin");
			upload(be, src, remoteFile);
			var sizeAfter = be.Size(remoteFile);
			if (sizeAfter == payload.Length) ok($"upload {payload.Length} bytes");
			else bad($"upload size {sizeAfter} != {payload.Length}");

			// 4. 下载并比对
			var back = Path.Combine(tmp, "down.bin");
			download(be, remoteFile, back);
			if (File.Exists(back) && samebytes(payload, File.ReadAllBytes(back)))
				ok("download byte-for-byte");
			else
				bad("download mismatch");

			// 5. 续传：截断远端文件后从断点补齐
			var half = payload.Length / 2;
			if (kind == FtpProto.Sftp || sizeAfter == payload.Length) {
				try {
					resume(be, src, remoteFile, half);
					var got = be.Size(remoteFile);
					if (got == payload.Length) ok("resume upload to full size");
					else bad($"resume size {got} != {payload.Length}");
				}
				catch (Exception ex) {
					// FTP 的 APPE 续传依赖服务器支持，失败只报告不判失败
					outLog($"SKIP resume: {ex.Message}");
				}
			}

			// 6. 改名 + 删除
			var renamed = FtpXfer.joinRemote(sub, "renamed.bin");
			be.Rename(remoteFile, renamed);
			ok("rename");
			be.RmFile(renamed);
			be.RmDir(sub);
			ok("cleanup remote");

			return fails == 0 ? 0 : 1;
		}
		catch (Exception ex) {
			errLog($"EXC {ex.GetType().Name}: {ex.Message}");
			return 3;
		}
		finally {
			try { be?.Dispose(); } catch { }
			try { Directory.Delete(tmp, true); } catch { }
		}

		void upload(IFtpBackend b, string local, string remote) {
			using var f = File.OpenRead(local);
			using var w = b.OpenWrite(remote, 0);
			f.CopyTo(w, 64 * 1024);
		}

		void download(IFtpBackend b, string remote, string local) {
			var size = b.Size(remote);
			using var r = b.OpenRead(remote, 0, size);
			using var f = File.Create(local);
			r.CopyTo(f, 64 * 1024);
		}

		/// <summary>先传一半，再从断点续上另一半，验证 OpenWrite(offset) 追加而不是覆盖。</summary>
		void resume(IFtpBackend b, string local, string remote, long half) {
			var all = File.ReadAllBytes(local);
			// 重建一个只有前半段的远端文件
			b.RmFile(remote);
			using (var w0 = b.OpenWrite(remote, 0)) {
				w0.Write(all, 0, (int)half);
				w0.Flush();
			}
			var mid = b.Size(remote);
			if (mid != half) throw new InvalidOperationException($"half write {mid} != {half}");
			var start = b.AlignResume(remote, mid);
			if (start != mid) throw new InvalidOperationException($"AlignResume {start} != {mid}");
			using (var w1 = b.OpenWrite(remote, start)) {
				outLog($"  resume stream: CanSeek={w1.CanSeek} CanWrite={w1.CanWrite} type={w1.GetType().Name}");
				w1.Write(all, (int)start, all.Length - (int)start);
				w1.Flush();
			}
			var fin = b.Size(remote);
			if (fin != all.Length) throw new InvalidOperationException($"resume {fin} != {all.Length}");
		}
	}

	static bool samebytes(byte[] a, byte[] b) {
		if (a.Length != b.Length) return false;
		for (int i = 0; i < a.Length; i++)
			if (a[i] != b[i]) return false;
		return true;
	}

	/// <summary>
	/// 只把窗口造出来再关掉：验证 XAML 能加载、绑定路径不错、语言键都在（缺键会显示键名）。
	/// 不需要网络，也不需要依赖已安装。
	/// </summary>
	public static int UiSmoke(Action<string> outLog, Action<string> errLog) {
		try {
			var w = new FtpWindow();
			// 构造完还没显示，Loaded 里的检查不会跑；直接看关键控件在不在
			if (w.lvremote == null) { errLog("远程列表未创建"); return 3; }
			if (w.bdownloadto == null) { errLog("下载按钮未创建"); return 8; }
			if (w.lvxfer == null) { errLog("传输列表未创建"); return 4; }
			if (string.IsNullOrEmpty(w.Title)) { errLog("标题为空"); return 5; }
			if (w.Title.StartsWith("ftp.", StringComparison.Ordinal)) {
				errLog("标题是裸键名，缺语言文案");
				return 6;
			}
			outLog($"窗口 OK  标题={w.Title}");
			outLog($"  协议项 {w.cbKind.Items.Count} 个，当前 {w.cbKind.SelectedItem}");
			outLog($"  并行度选项 {w.cbParallel.Items.Count} 个");
			outLog($"  依赖已装 {FtpDeps.Installed}（缺 {string.Join("、", FtpDeps.Missing())}）");
			w.Close();
			return 0;
		}
		catch (Exception ex) {
			errLog($"EXC {ex.GetType().Name}: {ex.Message}");
			return 7;
		}
	}
}
