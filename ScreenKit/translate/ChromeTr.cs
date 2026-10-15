using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenKit;

/// <summary>用隐藏的本机 Chrome（Translator）翻译。不返回采样，结果经本机回传。</summary>
static class ChromeTr {
	const int SW_HIDE = 0;
	static readonly object gate = new();
	static readonly object speakGate = new();
	static readonly Dictionary<int, TaskCompletionSource<List<string>>> waiters = new();
	static Process proc;
	static TcpListener lis;
	static int port;
	static int seq;
	static string launched;

	public static string Bcp(string code) {
		code = TrLang.Normalize(code);
		if (code == TrLang.Zh) return "zh-Hans";
		if (code == "cht") return "zh-Hant";
		return code;
	}

	public static string Translate(string text, string src, string dst, string exe, int idleSec, CancellationToken ct) {
		var list = TranslateMany(new[] { text ?? "" }, src, dst, exe, idleSec, ct);
		return list.Count > 0 ? list[0] ?? "" : "";
	}

	public static List<string> TranslateMany(IReadOnlyList<string> items, string src, string dst, string exe, int idleSec, CancellationToken ct) {
		var list = new List<string>();
		if (items == null || items.Count == 0) return list;
		src = TrLang.Normalize(src);
		dst = TrLang.Normalize(dst);
		if (src.Length == 0 || dst.Length == 0 || src == TrLang.Auto || dst == TrLang.Auto)
			throw new InvalidOperationException("请选择源语言和目标语言");
		if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase)) {
			foreach (var s in items) list.Add(s ?? "");
			return list;
		}
		var texts = new List<string>();
		foreach (var s in items) texts.Add(s ?? "");
		return post(texts, Bcp(src), Bcp(dst), exe, idleSec, ct);
	}

	public static int Test() {
		if (Bcp("zh") != "zh-Hans") return 2;
		if (Bcp("cht") != "zh-Hant") return 2;
		if (Bcp("zh-TW") != "zh-Hant") return 2;
		if (Bcp("en") != "en") return 2;
		if (Bcp("ja") != "ja") return 2;
		return 0;
	}

	static List<string> post(List<string> texts, string src, string dst, string exe, int idleSec, CancellationToken ct) {
		idleSec = ChromeTts.ClampIdle(idleSec);
		var path = ChromeTts.FindExe(exe);
		if (path == null) throw new InvalidOperationException("找不到 chrome.exe");
		var id = Interlocked.Increment(ref seq);
		var tcs = new TaskCompletionSource<List<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
		lock (waiters) waiters[id] = tcs;
		try {
			lock (speakGate) {
				ensurelisten();
				var dir = workdir();
				var alive = false;
				lock (gate) alive = proc != null && !proc.HasExited && string.Equals(launched, path, StringComparison.OrdinalIgnoreCase);
				if (!alive) {
					stopproc();
					writejob(dir, 0, src, dst, idleSec, new List<string>());
					File.WriteAllText(Path.Combine(dir, "tr.html"), page(port, idleSec), new UTF8Encoding(false));
					launch(path, dir);
					if (!waittitle("sktr-ready", 15000, ct))
						throw new InvalidOperationException("Chrome 没有打开翻译页");
				}
				writejob(dir, id, src, dst, idleSec, texts);
			}
			try {
				if (!tcs.Task.Wait(180000, ct))
					throw new InvalidOperationException("Chrome 翻译超时");
				return tcs.Task.Result ?? new List<string>();
			}
			catch (AggregateException ae) {
				throw ae.InnerException ?? ae;
			}
		}
		finally {
			lock (waiters) waiters.Remove(id);
		}
	}

	static void ensurelisten() {
		if (lis != null) return;
		lis = new TcpListener(IPAddress.Loopback, 0);
		lis.Start();
		port = ((IPEndPoint)lis.LocalEndpoint).Port;
		var t = new Thread(acceptloop) { IsBackground = true, Name = "chrome-tr" };
		t.Start();
	}

	static void acceptloop() {
		while (true) {
			TcpClient c = null;
			try { c = lis.AcceptTcpClient(); }
			catch { return; }
			try { readreq(c); }
			catch (Exception ex) { CaptureLog.Ex("chrome-tr", ex); }
			try { c.Close(); } catch { }
		}
	}

	static void readreq(TcpClient c) {
		c.ReceiveTimeout = 8000;
		var stream = c.GetStream();
		var buf = new MemoryStream();
		var tmp = new byte[8192];
		var headerEnd = -1;
		while (headerEnd < 0 && buf.Length < 1024 * 1024) {
			var n = stream.Read(tmp, 0, tmp.Length);
			if (n <= 0) break;
			buf.Write(tmp, 0, n);
			headerEnd = find(buf.GetBuffer(), (int)buf.Length, Encoding.ASCII.GetBytes("\r\n\r\n"));
		}
		if (headerEnd < 0) return;
		var head = Encoding.ASCII.GetString(buf.GetBuffer(), 0, headerEnd);
		var bodyAt = headerEnd + 4;
		var have = (int)buf.Length - bodyAt;
		var len = contentlen(head);
		while (have < len && buf.Length < 2 * 1024 * 1024) {
			var n = stream.Read(tmp, 0, tmp.Length);
			if (n <= 0) break;
			buf.Write(tmp, 0, n);
			have += n;
		}
		var body = Encoding.UTF8.GetString(buf.GetBuffer(), bodyAt, Math.Max(0, Math.Min(len, (int)buf.Length - bodyAt)));
		try {
			var raw = Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n");
			stream.Write(raw, 0, raw.Length);
		}
		catch { }
		take(body);
	}

	static void take(string body) {
		if (string.IsNullOrWhiteSpace(body)) return;
		JsonObject jo;
		try { jo = JsonNode.Parse(body) as JsonObject; }
		catch { return; }
		if (jo == null || jo["id"] == null) return;
		var id = jo["id"].GetValue<int>();
		var ok = jo["ok"] != null && jo["ok"].GetValue<bool>();
		var err = jo["err"]?.GetValue<string>() ?? "";
		var items = new List<string>();
		if (jo["items"] is JsonArray arr) {
			foreach (var n in arr)
				items.Add(n?.GetValue<string>() ?? "");
		}
		TaskCompletionSource<List<string>> tcs;
		lock (waiters) {
			if (!waiters.TryGetValue(id, out tcs)) return;
		}
		if (!ok)
			tcs.TrySetException(new InvalidOperationException(err.Length > 0 ? err : "Chrome 翻译失败"));
		else
			tcs.TrySetResult(items);
	}

	static int contentlen(string head) {
		var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
		foreach (var line in lines) {
			if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) continue;
			if (int.TryParse(line.Substring(15).Trim(), out var n) && n >= 0) return n;
		}
		return 0;
	}

	static int find(byte[] buf, int len, byte[] pat) {
		if (len < pat.Length) return -1;
		for (var i = 0; i <= len - pat.Length; i++) {
			var ok = true;
			for (var j = 0; j < pat.Length; j++) {
				if (buf[i + j] != pat[j]) { ok = false; break; }
			}
			if (ok) return i;
		}
		return -1;
	}

	static void writejob(string dir, int id, string src, string dst, int idleSec, List<string> texts) {
		var items = new JsonArray();
		foreach (var s in texts) items.Add(s ?? "");
		var jo = new JsonObject {
			["id"] = id,
			["src"] = src ?? "",
			["dst"] = dst ?? "",
			["idle"] = idleSec,
			["items"] = items,
		};
		var path = Path.Combine(dir, "job.json");
		var tmp = path + ".tmp";
		File.WriteAllText(tmp, jo.ToJsonString(), new UTF8Encoding(false));
		File.Copy(tmp, path, true);
		try { File.Delete(tmp); } catch { }
	}

	static void launch(string path, string dir) {
		var profile = Path.Combine(dir, "profile");
		Directory.CreateDirectory(profile);
		var url = new Uri(Path.Combine(dir, "tr.html")).AbsoluteUri;
		var args = new StringBuilder();
		args.Append("--user-data-dir=\"").Append(profile).Append("\" ");
		args.Append("--disable-web-security --allow-file-access-from-files ");
		args.Append("--disable-features=BlockInsecurePrivateNetworkRequests ");
		args.Append("--no-first-run --no-default-browser-check --disable-sync --disable-extensions ");
		args.Append("--disable-popup-blocking ");
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

	static void stopproc() {
		Process p;
		lock (gate) {
			p = proc;
			proc = null;
			launched = null;
		}
		if (p == null) return;
		try { if (!p.HasExited) p.Kill(); } catch { }
	}

	public static void Stop() => stopproc();

	public static string ProfileDir() {
		var profile = Path.Combine(workdir(), "profile");
		Directory.CreateDirectory(profile);
		return profile;
	}

	static string workdir() {
		var dir = Path.Combine(Path.GetTempPath(), "sk-chrome-tr");
		Directory.CreateDirectory(dir);
		return dir;
	}

	static bool waittitle(string part, int ms, CancellationToken ct) {
		var start = Environment.TickCount;
		while (unchecked(Environment.TickCount - start) < ms) {
			hide();
			if (ct.IsCancellationRequested) ct.ThrowIfCancellationRequested();
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
		eachtitles(t => { if (t.IndexOf(part, StringComparison.Ordinal) >= 0) ok = true; });
		return ok;
	}

	static void hide() {
		eachtitles(_ => { });
	}

	static void eachtitles(Action<string> hit) {
		EnumWindows((h, _) => {
			var sb = new StringBuilder(256);
			if (GetWindowText(h, sb, sb.Capacity) <= 0) return true;
			var t = sb.ToString();
			if (!t.StartsWith("sktr", StringComparison.Ordinal)) return true;
			hit(t);
			ShowWindow(h, SW_HIDE);
			return true;
		}, IntPtr.Zero);
	}

	static string page(int listenPort, int idleSec) {
		return "<!DOCTYPE html><meta charset=\"utf-8\"><title>sktr-ready</title><script>"
			+ "var PORT=" + listenPort + ",idleSec=" + idleSec + @";
var jobId=0,busy=false,timer=null,armed=-1,cache={};
document.title='sktr-ready';
function arm(){
if(busy||!(idleSec>0)){clearTimeout(timer);timer=null;return;}
if(armed===idleSec&&timer)return;
clearTimeout(timer);
armed=idleSec;
timer=setTimeout(function(){if(!busy)window.close();},idleSec*1000);
}
function ctor(){
if(typeof Translator==='function')return Translator;
if(self.ai&&typeof self.ai.translator==='function')return self.ai.translator;
return null;
}
function post(id,ok,err,items){
var body=JSON.stringify({id:id,ok:ok,err:err||'',items:items||[]});
try{fetch('http://127.0.0.1:'+PORT+'/r',{method:'POST',mode:'no-cors',headers:{'Content-Type':'text/plain'},body:body});}catch(e){}
document.title=(ok?'sktr-done-':'sktr-err-')+id;
busy=false;armed=-1;arm();
}
async function run(job){
var C=ctor();
if(!C){post(job.id,false,'这个 Chrome 没有翻译接口',[]);return;}
var key=job.src+'>'+job.dst;
try{
if(!cache[key]){
var opt={sourceLanguage:job.src,targetLanguage:job.dst};
var avail='available';
if(typeof C.availability==='function')avail=await C.availability(opt);
if(avail==='unavailable'||avail==='no'){post(job.id,false,'Chrome 不能翻译这对语言',[]);return;}
cache[key]=await C.create(opt);
}
var t=cache[key],outs=[],i,s;
for(i=0;i<job.items.length;i++){
s=job.items[i]||'';
outs.push(s.trim()?await t.translate(s):'');
}
post(job.id,true,'',outs);
}catch(e){post(job.id,false,e&&e.message?e.message:'Chrome 翻译失败',[]);}
}
function poll(){
var x=new XMLHttpRequest();
x.open('GET','job.json?'+Date.now(),true);
x.onload=function(){
var j;try{j=JSON.parse(x.responseText||'');}catch(e){return;}
if(!j)return;
if(typeof j.idle==='number'&&j.idle!==idleSec){idleSec=j.idle;armed=-1;}
if(!busy)arm();
if(busy||!j.id||j.id===jobId||!j.items||!j.items.length)return;
jobId=j.id;busy=true;clearTimeout(timer);
run(j);
};
try{x.send();}catch(e){}
}
setInterval(poll,200);
arm();
</script>";
	}

	delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
	[DllImport("user32.dll")] static extern bool EnumWindows(EnumProc lpEnumFunc, IntPtr lParam);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
	[DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
