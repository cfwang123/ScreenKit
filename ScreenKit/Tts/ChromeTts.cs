using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ScreenKit;

/// <summary>本机 Chrome 的 speechSynthesis（Google 语音）。只出声，不返回采样。和翻译共用一个隐藏进程。</summary>
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

	/// <summary>pid 下面的子进程个数。快照失败返回 -1。</summary>
	public static int ChildCount(int pid) {
		if (pid <= 0) return 0;
		var snap = CreateToolhelp32Snapshot(2, 0);
		if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return -1;
		try {
			var rows = new List<int[]>();
			var pe = new PROCESSENTRY32();
			pe.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
			if (!Process32First(snap, ref pe)) return -1;
			do {
				rows.Add(new[] { (int)pe.th32ProcessID, (int)pe.th32ParentProcessID });
			}
			while (Process32Next(snap, ref pe));
			var n = 0;
			var seen = new HashSet<int>();
			var q = new Queue<int>();
			q.Enqueue(pid);
			seen.Add(pid);
			while (q.Count > 0) {
				var id = q.Dequeue();
				foreach (var row in rows) {
					if (row[1] != id || seen.Contains(row[0])) continue;
					seen.Add(row[0]);
					n++;
					q.Enqueue(row[0]);
				}
			}
			return n;
		}
		finally {
			CloseHandle(snap);
		}
	}

	/// <summary>pid 自己和它下面的进程的工作集。快照失败返回 0。</summary>
	public static long TreeWorkingSet(int pid) {
		if (pid <= 0) return 0;
		var snap = CreateToolhelp32Snapshot(2, 0);
		if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return 0;
		try {
			var rows = new List<int[]>();
			var pe = new PROCESSENTRY32();
			pe.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
			if (!Process32First(snap, ref pe)) return 0;
			do {
				rows.Add(new[] { (int)pe.th32ProcessID, (int)pe.th32ParentProcessID });
			}
			while (Process32Next(snap, ref pe));
			var seen = new HashSet<int>();
			var q = new Queue<int>();
			q.Enqueue(pid);
			seen.Add(pid);
			while (q.Count > 0) {
				var id = q.Dequeue();
				foreach (var row in rows) {
					if (row[1] != id || seen.Contains(row[0])) continue;
					seen.Add(row[0]);
					q.Enqueue(row[0]);
				}
			}
			long sum = 0;
			foreach (var id in seen) {
				try {
					using var p = Process.GetProcessById(id);
					sum += p.WorkingSet64;
				}
				catch { }
			}
			return sum;
		}
		finally {
			CloseHandle(snap);
		}
	}

	public static void Stop() => ChromeHost.Stop();

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
		var list = new List<SapiVoiceItem>();
		foreach (var row in rows) {
			var lang = row.culture.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase)
				? TtsLang.Yue
				: SapiVoiceItem.LangOf(row.culture);
			list.Add(new SapiVoiceItem {
				DisplayName = row.name,
				Key = "chrome:" + row.name,
				Name = row.name,
				Culture = row.culture,
				Lang = lang,
				Gender = row.gender,
				Source = "chrome",
			});
		}
		return list;
	}

}
