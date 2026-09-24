using System.Diagnostics;

namespace ScreenKit;

static class CastAdbFwd {
	static string adb;
	static bool loggedmiss;
	static string lastok;
	public static string LastMsg { get; private set; }

	public static void KillServer(Action<string> log) {
		var exe = findadb();
		if (exe == null) return;
		try {
			var p = new Process();
			p.StartInfo.FileName = exe;
			p.StartInfo.Arguments = "kill-server";
			p.StartInfo.UseShellExecute = false;
			p.StartInfo.CreateNoWindow = true;
			p.StartInfo.RedirectStandardOutput = true;
			p.StartInfo.RedirectStandardError = true;
			p.Start();
			if (!p.WaitForExit(5000)) try { p.Kill(); } catch { }
			log?.Invoke("adb kill-server，释放 WinUSB 给 AOA");
		}
		catch (Exception ex) { log?.Invoke($"adb kill-server: {ex.Message}"); }
	}

	public static bool Reverse(int hostPort, Action<string> log) {
		if (hostPort <= 0) hostPort = CastProto.TCP_PORT;
		var exe = findadb();
		if (exe == null) {
			LastMsg = "未找到 adb.exe，USB(adb) 投屏不可用";
			if (!loggedmiss) { loggedmiss = true; log?.Invoke(LastMsg); }
			return false;
		}
		var list = run(exe, "devices");
		if (list == null) {
			run(exe, "start-server");
			list = run(exe, "devices");
		}
		if (list == null) {
			LastMsg = "adb devices 失败";
			return false;
		}
		var ok = false;
		foreach (var line in list.Split('\n')) {
			var t = line.Trim();
			if (t.Length == 0 || t.StartsWith("List of")) continue;
			var parts = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length < 2) continue;
			var serial = parts[0];
			var state = parts[1];
			if (state != "device") continue;
			if (serial.StartsWith("emulator-")) continue;
			var tcp = run(exe, $"-s {serial} reverse tcp:{CastProto.TCP_PORT} tcp:{hostPort}");
			var abs = run(exe, $"-s {serial} reverse localabstract:{CastProto.ABSTRACT} tcp:{hostPort}");
			if (tcp != null || abs != null) {
				ok = true;
				LastMsg = hostPort == CastProto.TCP_PORT
					? $"USB adb 已转发 {serial}"
					: $"USB adb 已转发 {serial} 设备{CastProto.TCP_PORT}->电脑{hostPort}";
			}
		}
		if (!ok) LastMsg = "未发现 USB 调试设备";
		else if (LastMsg != lastok) {
			lastok = LastMsg;
			log?.Invoke(LastMsg);
		}
		return ok;
	}

	static string findadb() {
		if (adb != null) return adb;
		var home = Environment.GetEnvironmentVariable("ANDROID_HOME")
			?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT")
			?? "";
		var cands = new[] {
			string.IsNullOrEmpty(home) ? "" : Path.Combine(home, "platform-tools", "adb.exe"),
			@"E:\ProgramFiles\androidsdk\platform-tools\adb.exe",
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe"),
			@"C:\Android\platform-tools\adb.exe",
			@"C:\platform-tools\adb.exe",
			"adb.exe",
		};
		foreach (var p in cands) {
			if (string.IsNullOrEmpty(p)) continue;
			if (p == "adb.exe") {
				if (run("adb.exe", "version") != null) { adb = "adb.exe"; return adb; }
				continue;
			}
			if (File.Exists(p)) { adb = p; return adb; }
		}
		return null;
	}

	static string run(string file, string args) {
		try {
			var p = new Process();
			p.StartInfo.FileName = file;
			p.StartInfo.Arguments = args;
			p.StartInfo.UseShellExecute = false;
			p.StartInfo.RedirectStandardOutput = true;
			p.StartInfo.RedirectStandardError = true;
			p.StartInfo.CreateNoWindow = true;
			p.Start();
			var o = p.StandardOutput.ReadToEnd();
			var e = p.StandardError.ReadToEnd();
			if (!p.WaitForExit(8000)) {
				try { p.Kill(); } catch { }
				return null;
			}
			if (p.ExitCode != 0) return null;
			return string.IsNullOrEmpty(o) ? (e ?? "") : o;
		}
		catch {
			return null;
		}
	}
}
