using System.Text;

namespace ScreenKit;

/// <summary>
/// 传输队列「完成后自动撤条目」的自测。
/// 起本机 FTP 服务端当对端，真实跑一次上传和一次失败上传，
/// 确认成功的那条会被移除、失败的留在列表里。
/// </summary>
static class FtpXferSelfTest {
	public static int Run(Action<string> outLog, Action<string> errLog) {
		if (!FtpDeps.Installed) {
			errLog($"依赖未安装，缺: {string.Join(", ", FtpDeps.Missing())}");
			return 2;
		}
		var root = Path.Combine(Path.GetTempPath(), "sk-xfer-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		var fails = 0;
		void ok(string s) => outLog($"OK   {s}");
		void bad(string s) {
			fails++;
			errLog($"FAIL {s}");
		}

		var srv = new FtpServer();
		try {
			srv.Log = null;
			var port = 0;
			for (var p = 47820; p < 47860; p++) {
				try {
					srv.Start(new FtpSrvOptions { Port = p, Root = root, User = "", Pass = "" });
					port = srv.Port;
					break;
				}
				catch { }
			}
			if (port <= 0) {
				errLog("无法启动 FTP 服务");
				return 2;
			}
			ok($"服务已启动 端口 {port}");

			var site = new FtpSite { Kind = FtpProto.Ftp, Host = "127.0.0.1", Port = port, User = "a" };
			var xfer = new FtpXfer();
			var removed = new List<FtpJob>();
			var finished = new List<FtpJob>();
			var gate = new object();
			var done = new ManualResetEventSlim(false);
			int pending = 2;

			xfer.Finished += job => {
				lock (gate) {
					finished.Add(job);
					// 照抄界面那段逻辑：成功的撤掉，失败的留下
					if (job.State == FtpJobState.Done) {
						xfer.Remove(job);
						removed.Add(job);
					}
					pending--;
					if (pending <= 0) done.Set();
				}
			};

			// 一条会成功
			var srcOk = Path.Combine(root, "ok-src.bin");
			var data = Encoding.UTF8.GetBytes(new string('X', 4096));
			File.WriteAllBytes(srcOk, data);
			var jobOk = xfer.EnqueueUpload(site, srcOk, "/ok.bin", "");

			// 一条会失败（源文件不存在）
			var jobBad = xfer.EnqueueUpload(site, Path.Combine(root, "missing.bin"), "/bad.bin", "");

			if (!done.Wait(15000)) {
				bad("任务 15 秒内没跑完");
				return 1;
			}

			lock (gate) {
				if (jobOk.State == FtpJobState.Done) ok("上传任务成功");
				else bad($"上传任务状态 {jobOk.State}");
				if (jobBad.State == FtpJobState.Failed) ok("失败任务状态为 Failed");
				else bad($"失败任务状态 {jobBad.State}（期望 Failed）");

				if (removed.Contains(jobOk)) ok("成功的那条已从队列撤掉");
				else bad("成功的那条还在队列里");
				if (!removed.Contains(jobBad)) ok("失败的那条没有被撤掉（保留可重试）");
				else bad("失败的那条也被撤掉了");
				if (xfer.Jobs.Contains(jobBad)) ok("队列里仍有失败那条");
				else bad("队列里找不到失败那条了");
				if (!xfer.Jobs.Contains(jobOk)) ok("队列里不再有成功那条");
				else bad("队列里还能看到成功那条");

				if (finished.Count == 2) ok("Finished 事件触发 2 次");
				else bad($"Finished 触发 {finished.Count} 次，期望 2");
			}

			if (File.Exists(Path.Combine(root, "ok.bin"))) ok("成功那条确实落盘了");
			else bad("成功那条没落盘");

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
}
