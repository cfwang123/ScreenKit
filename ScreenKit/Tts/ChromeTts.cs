using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ScreenKit;

/// <summary>本机 Chrome 的 speechSynthesis（离线语音）。只出声，不返回采样。和翻译共用一个隐藏进程。</summary>
static class ChromeTts {
	static List<SapiVoiceItem> voices;

	public static List<SapiVoiceItem> Voices {
		get {
			voices ??= buildvoices();
			return voices;
		}
	}

	/// <summary>空闲秒数。小于 0 当成 60，0 表示不自动关，最大 3600。</summary>
	public static int ClampIdle(int sec) {
		if (sec < 0) return 60;
		if (sec > 3600) return 3600;
		return sec;
	}

	public static string FindExe(string configured) {
		if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
			return configured;
		foreach (var path in commonpaths()) {
			try {
				if (File.Exists(path)) return path;
			}
			catch { }
		}
		return null;
	}

	static List<string> commonpaths() {
		var list = new List<string>();
		string[] roots = {
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		};
		string[] tails = {
			@"Google\Chrome\Application\chrome.exe",
			@"Google\Chrome Beta\Application\chrome.exe",
			@"Google\Chrome Dev\Application\chrome.exe",
			@"Google\Chrome SxS\Application\chrome.exe",
			@"Chromium\Application\chrome.exe",
		};
		foreach (var root in roots) {
			if (string.IsNullOrEmpty(root)) continue;
			foreach (var tail in tails)
				list.Add(Path.Combine(root, tail));
		}
		var env = Environment.GetEnvironmentVariable("PATH") ?? "";
		foreach (var dir in env.Split(';')) {
			if (string.IsNullOrWhiteSpace(dir)) continue;
			try { list.Add(Path.Combine(dir.Trim(), "chrome.exe")); }
			catch { }
		}
		regpaths(list);
		return list;
	}

	static void regpaths(List<string> list) {
		var views = new[] { RegistryView.Registry64, RegistryView.Registry32 };
		var hives = new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
		string[] subs = {
			@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe",
			@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Google Chrome",
			@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Google Chrome",
			@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Google Chrome Beta",
			@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Google Chrome Dev",
			@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Google Chrome SxS",
		};
		foreach (var hive in hives) {
			foreach (var view in views) {
				RegistryKey root = null;
				try { root = RegistryKey.OpenBaseKey(hive, view); }
				catch { continue; }
				using (root) {
					foreach (var sub in subs) {
						try {
							using var key = root.OpenSubKey(sub);
							if (key == null) continue;
							addreg(list, key.GetValue(null) as string);
							addreg(list, key.GetValue("Path") as string);
							addreg(list, key.GetValue("InstallLocation") as string);
							addreg(list, key.GetValue("DisplayIcon") as string);
						}
						catch { }
					}
				}
			}
		}
	}

	static void addreg(List<string> list, string raw) {
		if (string.IsNullOrWhiteSpace(raw)) return;
		var s = raw.Trim().Trim('"');
		var exe = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
		if (exe >= 0) {
			list.Add(s.Substring(0, exe + 4).Trim().Trim('"'));
			return;
		}
		try { list.Add(Path.Combine(s, "chrome.exe")); }
		catch { }
	}

	/// <summary>隐藏的 Chrome 里朗读，直到这一句结束、取消，或进程退出。</summary>
	public static void Speak(string text, string voiceName, string lang, double rate, double volume, string exe, int idleSec, CancellationToken ct) {
		ChromeHost.Speak(text, voiceName, lang, rate, volume, exe, idleSec, ct, true);
	}

	/// <summary>交给已经打开或即将打开的隐藏 Chrome，不等读完。和其他请求并行。</summary>
	public static void Start(string text, string voiceName, string lang, double rate, double volume, string exe, int idleSec) {
		ChromeHost.Speak(text, voiceName, lang, rate, volume, exe, idleSec, CancellationToken.None, false);
	}

	/// <summary>本程序启动的 Chrome 还在。朗读和翻译是同一个进程。children 是它下面还活着的子进程数。</summary>
	public static bool TryRun(out int children) => ChromeHost.TryRun(out children);

	/// <summary>本进程和子进程的工作集。没在跑时为 0。</summary>
	public static long WorkingSet() => ChromeHost.WorkingSet();

	/// <summary>脱离父进程的作业和终端再启动。父进程在作业里且不允许脱离时，退回普通启动。</summary>
	public static Process StartProc(string path, string args) {
		const uint CREATE_BREAKAWAY_FROM_JOB = 0x01000000;
		const uint CREATE_NEW_PROCESS_GROUP = 0x00000200;
		const uint DETACHED_PROCESS = 0x00000008;
		var flags = CREATE_NEW_PROCESS_GROUP | DETACHED_PROCESS | CREATE_BREAKAWAY_FROM_JOB;
		if (!spawn(path, args, flags, out var pi)) {
			var err = Marshal.GetLastWin32Error();
			if (err != 5 && err != 87)
				throw new System.ComponentModel.Win32Exception(err);
			flags = CREATE_NEW_PROCESS_GROUP | DETACHED_PROCESS;
			if (!spawn(path, args, flags, out pi))
				throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
		}
		Process child = null;
		try { child = Process.GetProcessById(pi.dwProcessId); }
		catch (ArgumentException) { }
		if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
		if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
		return child;
	}

	/// <summary>结束该进程及其子进程。pid 是本进程时不动。</summary>
	public static void KillPid(int pid) {
		if (pid <= 0) return;
		try { if (pid == Process.GetCurrentProcess().Id) return; }
		catch { return; }
		try {
			using (var kill = Process.Start(new ProcessStartInfo {
				FileName = "taskkill",
				Arguments = "/T /F /PID " + pid,
				UseShellExecute = false,
				CreateNoWindow = true,
			})) {
				kill?.WaitForExit(3000);
			}
		}
		catch { }
	}

	/// <summary>pid 下面的子进程个数。快照失败返回 -1。</summary>
	public static int ChildCount(int pid) {
		var ids = treeids(pid);
		if (ids == null) return -1;
		return ids.Count > 0 ? ids.Count - 1 : 0;
	}

	/// <summary>pid 自己和它下面的进程的工作集。快照失败返回 0。</summary>
	public static long TreeWorkingSet(int pid) {
		var ids = treeids(pid);
		if (ids == null) return 0;
		long sum = 0;
		foreach (var id in ids) sum += workingset(id);
		return sum;
	}

	static List<int> treeids(int pid) {
		if (pid <= 0) return new List<int>();
		var snap = CreateToolhelp32Snapshot(2, 0);
		if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return null;
		try {
			var rows = new List<int[]>();
			var pe = new PROCESSENTRY32();
			pe.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
			if (!Process32First(snap, ref pe)) return null;
			do rows.Add(new[] { (int)pe.th32ProcessID, (int)pe.th32ParentProcessID });
			while (Process32Next(snap, ref pe));
			var seen = new HashSet<int>();
			var q = new Queue<int>();
			q.Enqueue(pid);
			seen.Add(pid);
			while (q.Count > 0) {
				var id = q.Dequeue();
				foreach (var row in rows) {
					if (row[1] != id || !seen.Add(row[0])) continue;
					q.Enqueue(row[0]);
				}
			}
			return new List<int>(seen);
		}
		finally {
			CloseHandle(snap);
		}
	}

	static long workingset(int pid) {
		var h = OpenProcess(0x1000, false, pid);
		if (h == IntPtr.Zero) return 0;
		try {
			var c = new PROCESS_MEMORY_COUNTERS();
			c.cb = (uint)Marshal.SizeOf(typeof(PROCESS_MEMORY_COUNTERS));
			if (!GetProcessMemoryInfo(h, ref c, c.cb)) return 0;
			return (long)c.WorkingSetSize;
		}
		finally { CloseHandle(h); }
	}

	static bool spawn(string path, string args, uint flags, out PROCESS_INFORMATION pi) {
		var cmd = new StringBuilder(32768);
		cmd.Append('"').Append(path).Append('"');
		if (!string.IsNullOrEmpty(args)) cmd.Append(' ').Append(args);
		var si = new STARTUPINFO();
		si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
		return CreateProcessW(path, cmd, IntPtr.Zero, IntPtr.Zero, false, flags, IntPtr.Zero, null, ref si, out pi);
	}

	public static void Stop() => ChromeHost.Stop();

	/// <summary>停掉正在读的那几句，翻译和浏览器进程不动。</summary>
	public static void StopSpeak() => ChromeHost.StopSpeak();

	public static int Test() {
		if (ClampIdle(-1) != 60 || ClampIdle(0) != 0 || ClampIdle(99999) != 3600) return 2;
		if (ChromeHost.Test() != 0) return 2;
		var dir = Path.Combine(Path.GetTempPath(), "sk-chrome-tts-test");
		Directory.CreateDirectory(dir);
		var fake = Path.Combine(dir, "chrome.exe");
		File.WriteAllBytes(fake, new byte[] { 1 });
		try {
			var found = FindExe(fake);
			if (!string.Equals(found, fake, StringComparison.OrdinalIgnoreCase)) return 2;
			if (FindExe("") == null && FindExe(fake) == null) return 2;
			if (Voices == null || Voices.Count < 8) return 2;
			var zh = Voices.Find(v => v.Lang == TtsLang.Zh && v.Source == "chrome");
			if (zh == null || string.IsNullOrEmpty(zh.Key) || !zh.Key.StartsWith("chrome:", StringComparison.Ordinal))
				return 2;
			// 语音包里的 Chrome OS 发音人 speechSynthesis 用不了，不能列成多个中文声音。
			if (Voices.FindAll(v => v.Source == "chrome" && v.Culture == "zh-CN").Count != 1) return 2;
			foreach (var v in Voices)
				if (v.Source == "chrome" && (v.Name ?? "").StartsWith("Chrome OS", StringComparison.OrdinalIgnoreCase)) return 2;
			if (ChildCount(Process.GetCurrentProcess().Id) < 0) return 2;
			if (TryRun(out _)) return 2;
			return 0;
		}
		finally {
			try { File.Delete(fake); } catch { }
		}
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW", SetLastError = true)]
	static extern bool Process32First(IntPtr snap, ref PROCESSENTRY32 pe);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW", SetLastError = true)]
	static extern bool Process32Next(IntPtr snap, ref PROCESSENTRY32 pe);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool CloseHandle(IntPtr handle);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern IntPtr OpenProcess(int access, bool inherit, int pid);

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateProcessW")]
	static extern bool CreateProcessW(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

	[DllImport("kernel32.dll", SetLastError = true, EntryPoint = "K32GetProcessMemoryInfo")]
	static extern bool GetProcessMemoryInfo(IntPtr process, ref PROCESS_MEMORY_COUNTERS counters, uint size);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	struct PROCESSENTRY32 {
		public uint dwSize;
		public uint cntUsage;
		public uint th32ProcessID;
		public IntPtr th32DefaultHeapID;
		public uint th32ModuleID;
		public uint cntThreads;
		public uint th32ParentProcessID;
		public int pcPriClassBase;
		public uint dwFlags;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
		public string szExeFile;
	}

	public static string ProfileDir() {
		var profile = Path.Combine(workdir(), "profile");
		Directory.CreateDirectory(profile);
		return profile;
	}

	/// <summary>模型页那个 Chrome 自己的配置。和装模型的配置分开，装包时只重启后台 Chrome，模型页窗口不动。</summary>
	public static string PageProfileDir() {
		var profile = Path.Combine(Path.GetTempPath(), "sk-chrome-page", "profile");
		Directory.CreateDirectory(profile);
		return profile;
	}

	/// <summary>关掉模型页那个 Chrome。它不在装模型的配置目录里，普通清理不会碰它。</summary>
	public static void StopPage() => killprofile("sk-chrome-page");

	/// <summary>结束命令行里带着某个配置目录的 chrome.exe。子进程的命令行也带着这个目录。</summary>
	static void killprofile(string key) {
		try {
			using var search = new ManagementObjectSearcher(
				"SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='chrome.exe'");
			using var rows = search.Get();
			foreach (ManagementObject mo in rows) {
				try {
					var cmd = mo["CommandLine"] as string ?? "";
					if (cmd.IndexOf(key, StringComparison.OrdinalIgnoreCase) < 0) continue;
					try { KillPid(Convert.ToInt32(mo["ProcessId"])); } catch { }
				}
				finally { try { mo.Dispose(); } catch { } }
			}
		}
		catch { }
	}

	static string workdir() {
		var dir = Path.Combine(Path.GetTempPath(), "sk-chrome-tts");
		Directory.CreateDirectory(dir);
		return dir;
	}

	static List<SapiVoiceItem> buildvoices() {
		(string name, string culture, string gender)[] rows = {
			("Google 普通话（中国大陆）", "zh-CN", "female"),
			("Google 粤語（香港）", "zh-HK", "female"),
			("Google 國語（臺灣）", "zh-TW", "female"),
			("Google US English", "en-US", "female"),
			("Google UK English Female", "en-GB", "female"),
			("Google UK English Male", "en-GB", "male"),
			("Google 日本語", "ja-JP", "female"),
			("Google 한국의", "ko-KR", "female"),
			("Google Deutsch", "de-DE", "female"),
			("Google français", "fr-FR", "female"),
			("Google español", "es-ES", "female"),
			("Google español de Estados Unidos", "es-US", "female"),
			("Google português do Brasil", "pt-BR", "female"),
			("Google italiano", "it-IT", "female"),
			("Google русский", "ru-RU", "female"),
			("Google हिन्दी", "hi-IN", "female"),
			("Google Bahasa Indonesia", "id-ID", "female"),
			("Google Tiếng Việt", "vi-VN", "female"),
			("Google Nederlands", "nl-NL", "female"),
			("Google polski", "pl-PL", "female"),
		};
		// 只列 Chrome 的 speechSynthesis 真正返回的声音。语音包里的 Chrome OS 多发音人
		// 走不到 speechSynthesis，列出来也只会落到同一个默认声音。
		var list = new List<SapiVoiceItem>();
		foreach (var row in rows) {
			var lang = row.culture.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase)
				? TtsLang.Yue
				: SapiVoiceItem.LangOf(row.culture);
			list.Add(voiceitem(row.name, row.culture, lang, row.gender));
		}
		return list;
	}

	static SapiVoiceItem voiceitem(string name, string culture, string lang, string gender) {
		return new SapiVoiceItem {
			DisplayName = name,
			Key = "chrome:" + name,
			Name = name,
			Culture = culture,
			Lang = lang,
			Gender = gender ?? "",
			Source = "chrome",
		};
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	struct STARTUPINFO {
		public int cb;
		public string reserved;
		public string desktop;
		public string title;
		public int x, y, xsize, ysize, xchars, ychars, fill, flags;
		public short show;
		public short reserved2;
		public IntPtr reserved2p;
		public IntPtr stdin, stdout, stderr;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct PROCESS_INFORMATION {
		public IntPtr hProcess;
		public IntPtr hThread;
		public int dwProcessId;
		public int dwThreadId;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct PROCESS_MEMORY_COUNTERS {
		public uint cb;
		public uint pageFaults;
		public UIntPtr peakWorkingSet;
		public UIntPtr WorkingSetSize;
		public UIntPtr quotaPeakPaged;
		public UIntPtr quotaPaged;
		public UIntPtr quotaPeakNonPaged;
		public UIntPtr quotaNonPaged;
		public UIntPtr pagefile;
		public UIntPtr peakPagefile;
	}

}
