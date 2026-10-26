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

	[DllImport("iphlpapi.dll", SetLastError = true)]
	static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, uint family, int cls, uint reserved);

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

	/// <summary>看这次请求来自哪个 Chrome。kind 为 app（程序的）或 user（用户的），认不出则为空。</summary>
	public static void ClientProfile(int clientPort, out string path, out string kind) {
		path = "";
		kind = "";
		var pid = ownerpid(clientPort);
		if (pid <= 0) return;
		var seen = new HashSet<int>();
		var mine = normdir(ProfileDir());
		for (var n = 0; n < 8 && pid > 0 && seen.Add(pid); n++) {
			if (!procinfo(pid, out var cmd, out var parent)) break;
			path = userdatadir(cmd);
			if (path.Length == 0 && browserchrome(cmd)) path = userchrome();
			if (path.Length > 0) break;
			pid = parent;
		}
		if (path.Length == 0) return;
		kind = string.Equals(normdir(path), mine, StringComparison.OrdinalIgnoreCase) ? "app" : "user";
	}

	static int ownerpid(int port) {
		if (port <= 0 || port > 65535) return 0;
		var self = 0;
		try { self = Process.GetCurrentProcess().Id; } catch { }
		var pid = ownerpid(port, self, 2, false);
		if (pid > 0) return pid;
		return ownerpid(port, self, 23, true);
	}

	static int ownerpid(int port, int self, uint family, bool v6) {
		var buf = tcptable(family);
		if (buf == IntPtr.Zero) return 0;
		try {
			int n = Marshal.ReadInt32(buf);
			if (n < 0 || n > 100000) return 0;
			int stride = v6 ? 56 : 24;
			int portAt = v6 ? 20 : 8;
			int pidAt = v6 ? 52 : 20;
			int stateAt = v6 ? 48 : 0;
			var pid = 0;
			for (int i = 0; i < n; i++) {
				var p = IntPtr.Add(buf, 4 + i * stride);
				if (ntohs(Marshal.ReadInt32(p, portAt)) != port) continue;
				if (Marshal.ReadInt32(p, stateAt) != 5) continue;
				var row = Marshal.ReadInt32(p, pidAt);
				if (row <= 0 || row == self) continue;
				pid = row;
				break;
			}
			return pid;
		}
		finally {
			Marshal.FreeHGlobal(buf);
		}
	}

	static IntPtr tcptable(uint family) {
		int size = 0;
		GetExtendedTcpTable(IntPtr.Zero, ref size, true, family, 5, 0);
		if (size <= 0) return IntPtr.Zero;
		var buf = Marshal.AllocHGlobal(size);
		if (GetExtendedTcpTable(buf, ref size, true, family, 5, 0) == 0) return buf;
		Marshal.FreeHGlobal(buf);
		return IntPtr.Zero;
	}

	static int ntohs(int v) {
		int p = v & 0xFFFF;
		return ((p & 0xFF) << 8) | (p >> 8);
	}

	static bool procinfo(int pid, out string cmd, out int parent) {
		cmd = "";
		parent = 0;
		try {
			using var search = new ManagementObjectSearcher(
				"SELECT CommandLine, ParentProcessId FROM Win32_Process WHERE ProcessId=" + pid);
			using var rows = search.Get();
			foreach (ManagementObject mo in rows) {
				cmd = mo["CommandLine"] as string ?? "";
				try { parent = Convert.ToInt32(mo["ParentProcessId"]); } catch { parent = 0; }
				return true;
			}
		}
		catch { }
		return false;
	}

	static string userdatadir(string cmd) {
		if (string.IsNullOrEmpty(cmd)) return "";
		var key = "--user-data-dir=";
		var i = cmd.IndexOf(key, StringComparison.OrdinalIgnoreCase);
		if (i < 0) return "";
		i += key.Length;
		if (i >= cmd.Length) return "";
		if (cmd[i] == '"') {
			var j = cmd.IndexOf('"', i + 1);
			return j < 0 ? cmd.Substring(i + 1) : cmd.Substring(i + 1, j - i - 1);
		}
		var end = i;
		while (end < cmd.Length && cmd[end] != ' ') end++;
		return cmd.Substring(i, end - i);
	}

	/// <summary>浏览器主进程（不是 --type= 子进程）。按可执行文件名认，命令里带 chrome 字样的别的不算。</summary>
	static bool browserchrome(string cmd) {
		if (string.IsNullOrEmpty(cmd)) return false;
		if (cmd.IndexOf("--type=", StringComparison.OrdinalIgnoreCase) >= 0) return false;
		var image = imagepath(cmd);
		if (image.Length == 0) return false;
		try {
			var name = Path.GetFileName(image);
			return name.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase)
				|| name.Equals("chromium.exe", StringComparison.OrdinalIgnoreCase);
		}
		catch { return false; }
	}

	/// <summary>命令行第一个词（可执行文件）。带引号时取到下一个引号。</summary>
	static string imagepath(string cmd) {
		var s = cmd.TrimStart();
		if (s.Length == 0) return "";
		if (s[0] == '"') {
			var j = s.IndexOf('"', 1);
			return j < 0 ? s.Substring(1) : s.Substring(1, j - 1);
		}
		var end = s.IndexOf(' ');
		return end < 0 ? s : s.Substring(0, end);
	}

	static string userchrome() {
		var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		return Path.Combine(root, "Google", "Chrome", "User Data");
	}

	static string normdir(string path) {
		path = (path ?? "").Trim().TrimEnd('\\', '/');
		if (path.Length == 0) return "";
		try { return Path.GetFullPath(path).TrimEnd('\\', '/'); }
		catch { return path; }
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
		var packs = ChromePack.BaseSpeakers();
		var cover = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var s in packs)
			if (chinesepack(s.lang)) cover.Add(s.lang);
		var list = new List<SapiVoiceItem>();
		foreach (var row in rows) {
			if (cover.Contains(ChromePack.SpeakLang(row.culture))) continue;
			var lang = row.culture.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase)
				? TtsLang.Yue
				: SapiVoiceItem.LangOf(row.culture);
			list.Add(voiceitem(row.name, row.culture, lang, row.gender));
		}
		foreach (var s in packs) {
			if (!cover.Contains(s.lang)) continue;
			var lang = s.lang.Equals("yue-HK", StringComparison.OrdinalIgnoreCase)
				? TtsLang.Yue
				: SapiVoiceItem.LangOf(s.lang);
			list.Add(voiceitem(s.name, s.lang, lang, s.gender));
		}
		return list;
	}

	static bool chinesepack(string lang) {
		return lang.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
			|| lang.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)
			|| lang.Equals("yue-HK", StringComparison.OrdinalIgnoreCase);
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
