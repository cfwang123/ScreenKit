using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

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
		string[] roots = {
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		};
		foreach (var root in roots) {
			if (string.IsNullOrEmpty(root)) continue;
			var path = Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe");
			if (File.Exists(path)) return path;
		}
		var env = Environment.GetEnvironmentVariable("PATH") ?? "";
		foreach (var dir in env.Split(';')) {
			if (string.IsNullOrWhiteSpace(dir)) continue;
			try {
				var path = Path.Combine(dir.Trim(), "chrome.exe");
				if (File.Exists(path)) return path;
			}
			catch { }
		}
		return null;
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
