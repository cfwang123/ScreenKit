using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace ScreenKit;

/// <summary>本机 Chrome 的 speechSynthesis（Google 语音）。只出声，不返回采样。窗口隐藏，空闲后退出。</summary>
static class ChromeTts {
	const int SW_HIDE = 0;
	static readonly object gate = new();
	static readonly object speakGate = new();
	static Process proc;
	static string launched;
	static int seq;
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
		int id;
		lock (speakGate) id = post(text, voiceName, lang, rate, volume, exe, idleSec);
		wait(id, ct);
	}

	/// <summary>交给已经打开或即将打开的隐藏 Chrome，不等读完。</summary>
	public static void Start(string text, string voiceName, string lang, double rate, double volume, string exe, int idleSec) {
		lock (speakGate) post(text, voiceName, lang, rate, volume, exe, idleSec);
	}

	/// <summary>本程序启动的朗读 Chrome 还在。children 是它下面还活着的子进程数。</summary>
	public static bool TryRun(out int children) {
		children = 0;
		Process p;
		lock (gate) p = proc;
		if (!up(p)) return false;
		int pid;
		try { pid = p.Id; }
		catch { return false; }
		var n = ChildCount(pid);
		children = n < 0 ? 0 : n;
		return true;
	}

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

	static bool up(Process p) {
		if (p == null) return false;
		try { return !p.HasExited; }
		catch { return false; }
	}

	public static void Stop() {
		Process p;
		lock (gate) {
			p = proc;
			proc = null;
			launched = null;
		}
		if (p == null) return;
		try {
			if (!p.HasExited) p.Kill();
		}
		catch { }
	}

	public static int Test() {
		if (ClampIdle(-1) != 60 || ClampIdle(0) != 0 || ClampIdle(99999) != 3600) return 2;
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

	static int post(string text, string voiceName, string lang, double rate, double volume, string exe, int idleSec) {
		idleSec = ClampIdle(idleSec);
		var path = FindExe(exe);
		if (path == null) throw new InvalidOperationException("找不到 chrome.exe");
		var dir = workdir();
		var alive = false;
		lock (gate) alive = proc != null && !proc.HasExited && string.Equals(launched, path, StringComparison.OrdinalIgnoreCase);
		if (!alive) {
			Stop();
			write(Path.Combine(dir, "job.json"), new { id = 0, text = "", name = "", lang = "", rate = 1.0, volume = 1.0, idle = idleSec });
			File.WriteAllText(Path.Combine(dir, "speak.html"), page(idleSec), new UTF8Encoding(false));
			launch(path, dir);
			if (!waittitle("sktts-ready", 15000, CancellationToken.None))
				throw new InvalidOperationException("Chrome 没有打开朗读页");
		}
		var id = Interlocked.Increment(ref seq);
		write(Path.Combine(dir, "job.json"), new {
			id,
			text = text ?? "",
			name = voiceName ?? "",
			lang = lang ?? "",
			rate = Math.Max(0.5, Math.Min(2.0, rate)),
			volume = Math.Max(0, Math.Min(1, volume)),
			idle = idleSec,
		});
		return id;
	}

	static void wait(int id, CancellationToken ct) {
		var mark = "sktts-done-" + id;
		var bad = "sktts-err-" + id;
		var start = Environment.TickCount;
		while (true) {
			hide();
			if (ct.IsCancellationRequested) {
				Stop();
				ct.ThrowIfCancellationRequested();
			}
			if (hastitle(bad)) throw new InvalidOperationException("Chrome 没有发出声音");
			if (hastitle(mark)) return;
			Process p;
			lock (gate) p = proc;
			if (p == null || p.HasExited) throw new InvalidOperationException("Chrome 已退出");
			if (unchecked(Environment.TickCount - start) > 180000) {
				Stop();
				throw new InvalidOperationException("Chrome 朗读超时");
			}
			Thread.Sleep(150);
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

	static void launch(string path, string dir) {
		var profile = Path.Combine(dir, "profile");
		Directory.CreateDirectory(profile);
		var url = new Uri(Path.Combine(dir, "speak.html")).AbsoluteUri;
		var args = new StringBuilder();
		args.Append("--user-data-dir=\"").Append(profile).Append("\" ");
		args.Append("--allow-file-access-from-files ");
		args.Append("--no-first-run --no-default-browser-check --disable-sync --disable-extensions ");
		args.Append("--disable-popup-blocking ");
		args.Append("--autoplay-policy=no-user-gesture-required ");
		args.Append("--disable-session-crashed-bubble --hide-crash-restore-bubble ");
		args.Append("--disable-backgrounding-occluded-windows --disable-renderer-backgrounding ");
		args.Append("--disable-background-timer-throttling ");
		args.Append("--window-position=-32000,-32000 --window-size=160,80 ");
		args.Append("--app=\"").Append(url).Append('"');
		var psi = new ProcessStartInfo {
			FileName = path,
			Arguments = args.ToString(),
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		var p = Process.Start(psi);
		lock (gate) {
			proc = p;
			launched = path;
		}
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

	static void write(string path, object job) {
		var json = JsonSerializer.Serialize(job).Replace("<", "\\u003c");
		var tmp = path + ".tmp";
		File.WriteAllText(tmp, json, new UTF8Encoding(false));
		File.Copy(tmp, path, true);
		try { File.Delete(tmp); } catch { }
	}

	static bool waittitle(string part, int ms, CancellationToken ct) {
		var start = Environment.TickCount;
		while (unchecked(Environment.TickCount - start) < ms) {
			hide();
			if (ct.IsCancellationRequested) return false;
			if (hastitle(part)) return true;
			Process p;
			lock (gate) p = proc;
			if (p == null || p.HasExited) return false;
			Thread.Sleep(100);
		}
		return hastitle(part);
	}

	static bool hastitle(string part) {
		var ok = false;
		eachtitles((t) => { if (t.IndexOf(part, StringComparison.Ordinal) >= 0) ok = true; });
		return ok;
	}

	static void hide() {
		eachtitles((_) => { });
	}

	static void eachtitles(Action<string> hit) {
		EnumWindows((h, _) => {
			var sb = new StringBuilder(256);
			if (GetWindowText(h, sb, sb.Capacity) <= 0) return true;
			var t = sb.ToString();
			if (!t.StartsWith("sktts", StringComparison.Ordinal)) return true;
			hit(t);
			ShowWindow(h, SW_HIDE);
			return true;
		}, IntPtr.Zero);
	}

	static string page(int idleSec) {
		return "<!DOCTYPE html><meta charset=\"utf-8\"><title>sktts-ready</title><script>"
			+ "var idleSec=" + idleSec + @";
var jobId=0,speaking=false,timer=null,armedIdle=-1,waitId=0,waitAt=0;
document.title='sktts-ready';
function arm(){
if(speaking||!(idleSec>0)){clearTimeout(timer);timer=null;return;}
if(armedIdle===idleSec&&timer)return;
clearTimeout(timer);
armedIdle=idleSec;
timer=setTimeout(function(){if(!speaking)window.close();},idleSec*1000);
}
function pick(job){
var list=speechSynthesis.getVoices(),name=job.name||'',lang=(job.lang||'').toLowerCase(),i,google=[];
if(name){for(i=0;i<list.length;i++)if(list[i].name===name)return list[i];}
for(i=0;i<list.length;i++)if(list[i].name.indexOf('Google')===0)google.push(list[i]);
if(lang){for(i=0;i<google.length;i++)if((google[i].lang||'').toLowerCase().indexOf(lang)===0)return google[i];}
if(google.length)return google[0];
if(lang){for(i=0;i<list.length;i++)if((list[i].lang||'').toLowerCase().indexOf(lang)===0)return list[i];}
return list[0]||null;
}
function speak(job){
var v=pick(job);
if(!v){document.title='sktts-err-'+job.id;speaking=false;arm();return;}
speaking=true;
clearTimeout(timer);
var u=new SpeechSynthesisUtterance(job.text||'');
u.voice=v;u.lang=v.lang||job.lang||'';u.rate=job.rate;u.volume=job.volume;
function fin(ok){
speaking=false;
armedIdle=-1;
document.title=(ok?'sktts-done-':'sktts-err-')+job.id;
arm();
}
u.onend=function(){fin(true);};
u.onerror=function(){fin(false);};
speechSynthesis.cancel();
speechSynthesis.speak(u);
}
function poll(){
var x=new XMLHttpRequest();
x.open('GET','job.json?'+Date.now(),true);
x.onload=function(){
var j;try{j=JSON.parse(x.responseText||'');}catch(e){return;}
if(!j)return;
if(typeof j.idle==='number'&&j.idle!==idleSec){idleSec=j.idle;armedIdle=-1;}
if(!speaking)arm();
if(speaking||!j.id||!j.text||j.id===jobId)return;
if(!pick(j)){
if(waitId!==j.id){waitId=j.id;waitAt=Date.now();}
if(Date.now()-waitAt>12000){jobId=j.id;document.title='sktts-err-'+j.id;armedIdle=-1;arm();}
return;
}
jobId=j.id;
speak(j);
};
try{x.send();}catch(e){}
}
setInterval(poll,200);
arm();
speechSynthesis.onvoiceschanged=poll;
</script>";
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

	delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
	[DllImport("user32.dll")] static extern bool EnumWindows(EnumProc lpEnumFunc, IntPtr lParam);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
	[DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
