using System.Diagnostics;

namespace ScreenKit;

/// <summary>启动时清其它目录的 ScreenKit、残留 AOA 助手。</summary>
static class CastStale {
	public static void Purge(Action<string> log) {
		var n = killprocs(log) + killport(CastProto.TCP_PORT, log);
		if (n > 0) Thread.Sleep(300);
		else log?.Invoke("无残留 ScreenKit / 投屏端口占用");
	}

	static int killprocs(Action<string> log) {
		var self = Process.GetCurrentProcess();
		var selfPath = norm(self);
		var n = 0;
		foreach (var p in Process.GetProcessesByName("ScreenKit")) {
			if (p.Id == self.Id) continue;
			string path;
			try { path = norm(p); }
			catch { path = ""; }
			var same = path.Length > 0 &&
				string.Equals(path, selfPath, StringComparison.OrdinalIgnoreCase);
			if (same && p.MainWindowHandle != IntPtr.Zero) continue;
			var why = same ? "AOA 助手" : (path.Length > 0 ? path : "未知路径");
			try {
				log?.Invoke($"结束残留 ScreenKit pid={p.Id} {why}");
				p.Kill();
				n++;
			}
			catch (Exception ex) {
				log?.Invoke($"结束 pid={p.Id} 失败: {ex.Message}");
				if (taskkill(p.Id)) n++;
			}
			try { p.WaitForExit(1500); } catch { }
		}
		return n;
	}

	static int killport(int port, Action<string> log) {
		var self = Process.GetCurrentProcess().Id;
		var n = 0;
		foreach (var pid in listenpids(port)) {
			if (pid <= 4 || pid == self) continue;
			string name;
			try { name = Process.GetProcessById(pid).ProcessName; }
			catch {
				log?.Invoke($"TCP {port} 被已退出 pid={pid} 占着（幽灵套接字）");
				continue;
			}
			try {
				log?.Invoke($"TCP {port} 被 pid={pid} {name} 占用，结束该进程");
				Process.GetProcessById(pid).Kill();
				n++;
			}
			catch (Exception ex) {
				log?.Invoke($"结束 pid={pid}: {ex.Message}");
				if (taskkill(pid)) n++;
			}
		}
		return n;
	}

	static HashSet<int> listenpids(int port) {
		var pids = new HashSet<int>();
		try {
			var psi = new ProcessStartInfo {
				FileName = "netstat",
				Arguments = "-ano",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				CreateNoWindow = true,
			};
			var p = Process.Start(psi);
			if (p == null) return pids;
			var o = p.StandardOutput.ReadToEnd();
			if (!p.WaitForExit(4000)) try { p.Kill(); } catch { }
			var needle = $":{port}";
			foreach (var line in o.Split('\n')) {
				if (line.IndexOf(needle, StringComparison.Ordinal) < 0) continue;
				if (line.IndexOf("LISTENING", StringComparison.OrdinalIgnoreCase) < 0
					&& line.IndexOf("侦听", StringComparison.Ordinal) < 0)
					continue;
				var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length == 0) continue;
				if (int.TryParse(parts[parts.Length - 1], out var pid) && pid > 4)
					pids.Add(pid);
			}
		}
		catch { }
		return pids;
	}

	static bool taskkill(int pid) {
		try {
			var psi = new ProcessStartInfo {
				FileName = "taskkill",
				Arguments = $"/F /PID {pid}",
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};
			var p = Process.Start(psi);
			if (p == null) return false;
			if (!p.WaitForExit(2000)) try { p.Kill(); } catch { }
			return p.ExitCode == 0;
		}
		catch { return false; }
	}

	static string norm(Process p) {
		var s = p.MainModule?.FileName ?? "";
		if (s.Length == 0) return "";
		try { return Path.GetFullPath(s); }
		catch { return s; }
	}

	public static bool SamePathStillRunning() {
		var self = Process.GetCurrentProcess();
		var selfPath = norm(self);
		foreach (var p in Process.GetProcessesByName("ScreenKit")) {
			if (p.Id == self.Id) continue;
			if (p.MainWindowHandle == IntPtr.Zero) continue;
			try {
				if (string.Equals(norm(p), selfPath, StringComparison.OrdinalIgnoreCase))
					return true;
			}
			catch { }
		}
		return false;
	}
}
