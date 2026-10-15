using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ScreenKit;

/// <summary>本机 Chrome 的 speechSynthesis（Google 语音）。只出声，不返回采样。</summary>
static class ChromeTts {
	static readonly object gate = new();
	static Process proc;
	static List<SapiVoiceItem> voices;

	public static List<SapiVoiceItem> Voices {
		get {
			voices ??= buildvoices();
			return voices;
		}
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

	/// <summary>打开 Chrome 朗读，直到窗口关掉或取消。</summary>
	public static void Speak(string text, string voiceName, string lang, double rate, double volume, string exe, CancellationToken ct) {
		var p = begin(text, voiceName, lang, rate, volume, exe);
		if (p == null) throw new InvalidOperationException("找不到 chrome.exe");
		while (!p.HasExited) {
			if (ct.IsCancellationRequested) {
				Stop();
				ct.ThrowIfCancellationRequested();
			}
			try { p.WaitForExit(200); }
			catch { break; }
		}
	}

	/// <summary>打开 Chrome 后立即返回。下一次开始或 <see cref="Stop"/> 会关掉上一扇。</summary>
	public static void Start(string text, string voiceName, string lang, double rate, double volume, string exe) {
		if (begin(text, voiceName, lang, rate, volume, exe) == null)
			throw new InvalidOperationException("找不到 chrome.exe");
	}

	public static void Stop() {
		Process p;
		lock (gate) {
			p = proc;
			proc = null;
		}
		if (p == null) return;
		try {
			if (!p.HasExited) p.Kill();
		}
		catch { }
	}

	public static int Test() {
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

	static Process begin(string text, string voiceName, string lang, double rate, double volume, string exe) {
		Stop();
		var path = FindExe(exe);
		if (path == null) return null;
		var dir = Path.Combine(Path.GetTempPath(), "sk-chrome-tts");
		Directory.CreateDirectory(dir);
		var profile = Path.Combine(dir, "profile");
		Directory.CreateDirectory(profile);
		var htmlPath = Path.Combine(dir, "speak.html");
		File.WriteAllText(htmlPath, page(text, voiceName, lang, rate, volume), new UTF8Encoding(false));
		var url = new Uri(htmlPath).AbsoluteUri;
		var args = new StringBuilder();
		args.Append("--user-data-dir=\"").Append(profile).Append("\" ");
		args.Append("--no-first-run --no-default-browser-check --disable-sync --disable-extensions ");
		args.Append("--disable-component-update --disable-popup-blocking ");
		args.Append("--autoplay-policy=no-user-gesture-required ");
		args.Append("--disable-session-crashed-bubble --hide-crash-restore-bubble ");
		args.Append("--window-size=420,180 --app=\"").Append(url).Append('"');
		var psi = new ProcessStartInfo {
			FileName = path,
			Arguments = args.ToString(),
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		var p = Process.Start(psi);
		lock (gate) proc = p;
		return p;
	}

	static string page(string text, string voiceName, string lang, double rate, double volume) {
		rate = Math.Max(0.5, Math.Min(2.0, rate));
		volume = Math.Max(0, Math.Min(1, volume));
		var json = JsonSerializer.Serialize(new {
			text = text ?? "",
			name = voiceName ?? "",
			lang = lang ?? "",
			rate,
			volume,
		});
		json = json.Replace("<", "\\u003c");
		return "<!DOCTYPE html><meta charset=\"utf-8\"><title>ScreenKit</title>"
			+ "<body style=\"font:14px sans-serif;margin:16px;background:#111;color:#eee\">"
			+ "<p id=\"m\"></p><script>var CFG=" + json + @";
var started=false;
function pick(){
var list=speechSynthesis.getVoices(),name=CFG.name||'',lang=(CFG.lang||'').toLowerCase(),i,v,google=[];
if(name){for(i=0;i<list.length;i++)if(list[i].name===name)return list[i];}
for(i=0;i<list.length;i++)if(list[i].name.indexOf('Google')===0)google.push(list[i]);
if(lang){for(i=0;i<google.length;i++)if((google[i].lang||'').toLowerCase().indexOf(lang)===0)return google[i];}
if(google.length)return google[0];
if(lang){for(i=0;i<list.length;i++)if((list[i].lang||'').toLowerCase().indexOf(lang)===0)return list[i];}
return list[0]||null;
}
function go(){
if(started)return;
var v=pick();
if(!v)return;
started=true;
var u=new SpeechSynthesisUtterance(CFG.text||'');
u.voice=v;u.lang=v.lang||CFG.lang||'';u.rate=CFG.rate;u.volume=CFG.volume;
u.onend=function(){window.close();};
u.onerror=function(){window.close();};
document.getElementById('m').textContent=v.name;
speechSynthesis.cancel();
speechSynthesis.speak(u);
}
speechSynthesis.onvoiceschanged=go;
setTimeout(go,300);
setTimeout(function(){if(!started)window.close();},12000);
setTimeout(function(){window.close();},180000);
</script></body>";
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
