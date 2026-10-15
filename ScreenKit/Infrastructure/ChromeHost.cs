using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenKit;

/// <summary>一个停在屏幕外的 Chrome。朗读和翻译在同一个窗口里排队，这一页保持在前台。</summary>
static class ChromeHost {
	const int SW_HIDE = 0;
	const int SW_SHOWNA = 8;
	static readonly object gate = new();
	static readonly Dictionary<int, Slot> slots = new();
	static readonly Queue<int> order = new();
	static Process proc;
	static string launched;
	static string profile;
	static TcpListener lis;
	static int port;
	static int seq;
	static int idle;
	static int nextkill;
	static bool loops;
	static bool ready;

	sealed class Slot {
		public int Id;
		public string Page;
		public bool Opened;
		public string Fail = "Chrome 失败";
		public TaskCompletionSource<List<string>> Done;
	}

	public static void Speak(string text, string voiceName, string lang, double rate, double volume, string exe, int idleSec, CancellationToken ct, bool wait) {
		var id = Interlocked.Increment(ref seq);
		var jobRate = Math.Max(0.5, Math.Min(2.0, rate));
		var jobVol = Math.Max(0, Math.Min(1, volume));
		var json = JsonSerializer.Serialize(new {
			op = "speak",
			id,
			text = text ?? "",
			name = voiceName ?? "",
			lang = lang ?? "",
			rate = jobRate,
			volume = jobVol,
		}).Replace("<", "\\u003c");
		var done = waiter(wait);
		var slot = new Slot { Id = id, Page = "speak.html", Fail = "Chrome 没有发出声音", Done = done };
		submit(id, "speak.html", json, slot, exe, idleSec, ct);
		if (!wait) return;
		waitdone(id, done, ct, "Chrome 朗读超时");
	}

	public static List<string> Translate(List<string> texts, string src, string dst, string exe, int idleSec, CancellationToken ct) {
		var id = Interlocked.Increment(ref seq);
		var items = new JsonArray();
		if (texts != null) {
			foreach (var s in texts) items.Add(s ?? "");
		}
		var jo = new JsonObject {
			["op"] = "tr",
			["id"] = id,
			["src"] = src ?? "",
			["dst"] = dst ?? "",
			["items"] = items,
		};
		var json = jo.ToJsonString().Replace("<", "\\u003c");
		var done = waiter(true);
		var slot = new Slot { Id = id, Page = "tr.html", Fail = "Chrome 翻译失败", Done = done };
		submit(id, "tr.html", json, slot, exe, idleSec, ct);
		return waitdone(id, done, ct, "Chrome 翻译超时");
	}

	public static void Stop() {
		lock (gate) stopunlocked();
	}

	public static bool TryRun(out int children) {
		children = 0;
		Process p;
		lock (gate) p = proc;
		if (!up(p)) return false;
		int pid;
		try { pid = p.Id; }
		catch { return false; }
		var n = ChromeTts.ChildCount(pid);
		children = n < 0 ? 0 : n;
		return true;
	}

	/// <summary>不启动 Chrome。两路结果同时回来时互不覆盖。</summary>
	public static int Test() {
		lock (slots) {
			slots.Clear();
			order.Clear();
			nextkill = 0;
		}
		var a = new TaskCompletionSource<List<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
		var b = new TaskCompletionSource<List<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
		lock (slots) {
			slots[101] = new Slot { Id = 101, Done = a, Fail = "x" };
			slots[102] = new Slot { Id = 102, Done = b, Fail = "x" };
		}
		var t1 = Task.Run(() => take("{\"id\":101,\"ok\":true,\"items\":[\"A\"]}"));
		var t2 = Task.Run(() => take("{\"id\":102,\"ok\":false,\"err\":\"nope\",\"items\":[]}"));
		if (!Task.WaitAll(new[] { t1, t2 }, 3000)) return 2;
		if (!a.Task.IsCompleted || a.Task.IsFaulted || a.Task.Result.Count != 1 || a.Task.Result[0] != "A") return 2;
		if (!b.Task.IsFaulted || b.Task.Exception.GetBaseException().Message != "nope") return 2;
		lock (slots) {
			if (slots.Count != 0) return 2;
			nextkill = 0;
		}
		return 0;
	}

	/// <summary>本机有 Chrome 时，两个页面同时回传，仍是这一个进程。没有 Chrome 则跳过。</summary>
	public static int TestLive() {
		var exe = ChromeTts.FindExe("");
		if (string.IsNullOrEmpty(exe)) return 0;
		try {
			ensure(exe, 30, CancellationToken.None);
			var a = Task.Run(ping);
			var b = Task.Run(ping);
			if (!Task.WaitAll(new[] { a, b }, 20000)) return 2;
			if (a.Result.Count != 1 || a.Result[0] != "P") return 2;
			if (b.Result.Count != 1 || b.Result[0] != "P") return 2;
			if (!TryRun(out _)) return 2;
			speaklive();
			return 0;
		}
		catch (Exception ex) {
			Console.Error.WriteLine(ex);
			return 2;
		}
		finally { Stop(); }
	}

	static void speaklive() {
		var id = Interlocked.Increment(ref seq);
		var done = waiter(true);
		var slot = new Slot { Id = id, Page = "speak.html", Fail = "speak", Done = done };
		var json = "{\"id\":" + id + ",\"text\":\"hi\",\"name\":\"\",\"lang\":\"en-US\",\"rate\":1,\"volume\":1}";
		submit(id, "speak.html", json, slot, "", 60, CancellationToken.None);
		waitdone(id, done, CancellationToken.None, "Chrome 朗读超时");
	}

	static List<string> ping() {
		var id = Interlocked.Increment(ref seq);
		var done = waiter(true);
		var slot = new Slot { Id = id, Page = "ping.html", Fail = "ping", Done = done };
		submit(id, "ping.html", $"{{\"op\":\"ping\",\"id\":{id}}}", slot, "", 30, CancellationToken.None);
		return waitdone(id, done, CancellationToken.None, "Chrome 朗读超时");
	}

	static TaskCompletionSource<List<string>> waiter(bool wait) {
		if (!wait) return null;
		var done = new TaskCompletionSource<List<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
		done.Task.ContinueWith(t => { var _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
		return done;
	}

	static void submit(int id, string page, string json, Slot slot, string exe, int idleSec, CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		ensure(exe, idleSec, ct);
		slot.Page = page;
		lock (slots) {
			slots[id] = slot;
			order.Enqueue(id);
			nextkill = 0;
		}
		try {
			var dir = Path.Combine(workdir(), "jobs");
			Directory.CreateDirectory(dir);
			try { File.Delete(Path.Combine(dir, id + ".stop")); } catch { }
			File.WriteAllText(Path.Combine(dir, id + ".json"), json, new UTF8Encoding(false));
		}
		catch {
			dropslot(id);
			throw;
		}
	}

	static List<string> waitdone(int id, TaskCompletionSource<List<string>> done, CancellationToken ct, string timeout) {
		using (ct.Register(() => cancel(id))) {
			try {
				if (!done.Task.Wait(180000)) {
					cancel(id);
					if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
					throw new InvalidOperationException(timeout);
				}
			}
			catch (AggregateException ae) {
				var inner = ae.GetBaseException();
				if (ct.IsCancellationRequested || inner is OperationCanceledException)
					throw new OperationCanceledException(ct);
				throw inner;
			}
		}
		if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
		return done.Task.Result ?? new List<string>();
	}

	static void cancel(int id) {
		Slot slot = null;
		lock (slots) {
			if (!slots.TryGetValue(id, out slot)) return;
			slots.Remove(id);
			if (slots.Count == 0) armidle();
		}
		try {
			var dir = Path.Combine(workdir(), "jobs");
			Directory.CreateDirectory(dir);
			File.WriteAllText(Path.Combine(dir, id + ".stop"), "1", new UTF8Encoding(false));
		}
		catch { }
		slot.Done?.TrySetCanceled();
		ThreadPool.QueueUserWorkItem(_ => {
			Thread.Sleep(2000);
			cleanup(id);
		});
	}

	static void dropslot(int id) {
		lock (slots) {
			slots.Remove(id);
			if (slots.Count == 0) armidle();
		}
		cleanup(id);
	}

	static void ensure(string exe, int idleSec, CancellationToken ct) {
		idleSec = ChromeTts.ClampIdle(idleSec);
		var path = ChromeTts.FindExe(exe);
		if (path == null) throw new InvalidOperationException("找不到 chrome.exe");
		lock (gate) {
			Volatile.Write(ref idle, idleSec);
			if (up(proc) && string.Equals(launched, path, StringComparison.OrdinalIgnoreCase)) {
				lock (slots) ready = true;
				return;
			}
			stopunlocked();
			starthttp();
			launch(path);
		}
		if (!waitready(ct)) {
			var cancel = ct.IsCancellationRequested;
			lock (gate) stopunlocked();
			if (cancel) throw new OperationCanceledException(ct);
			throw new InvalidOperationException("Chrome 没有打开朗读页");
		}
		lock (slots) {
			ready = true;
			if (slots.Count == 0) armidle();
		}
	}

	static void starthttp() {
		if (lis != null) return;
		lis = new TcpListener(IPAddress.Loopback, 0);
		lis.Start();
		port = ((IPEndPoint)lis.LocalEndpoint).Port;
		var t = new Thread(acceptloop) { IsBackground = true, Name = "chrome-host" };
		t.Start();
		if (loops) return;
		loops = true;
		var u = new Thread(idleloop) { IsBackground = true, Name = "chrome-hide" };
		u.Start();
	}

	static void launch(string path) {
		var dir = workdir();
		profile = ChromeTts.ProfileDir();
		var jobs = Path.Combine(dir, "jobs");
		Directory.CreateDirectory(jobs);
		try {
			foreach (var old in Directory.GetFiles(jobs))
				try { File.Delete(old); } catch { }
		}
		catch { }
		var utf8 = new UTF8Encoding(false);
		File.WriteAllText(Path.Combine(dir, "boot.html"), boothtml, utf8);
		var url = "http://127.0.0.1:" + port + "/boot.html";
		var args = new StringBuilder();
		args.Append("--user-data-dir=\"").Append(profile).Append("\" ");
		args.Append("--no-first-run --no-default-browser-check --disable-sync ");
		args.Append("--disable-popup-blocking ");
		args.Append("--autoplay-policy=no-user-gesture-required ");
		args.Append("--disable-session-crashed-bubble --hide-crash-restore-bubble ");
		args.Append("--disable-backgrounding-occluded-windows --disable-renderer-backgrounding ");
		args.Append("--disable-background-timer-throttling ");
		args.Append("--window-position=-32000,-32000 --window-size=160,80 ");
		args.Append('"').Append(url).Append('"');
		proc = Process.Start(new ProcessStartInfo {
			FileName = path,
			Arguments = args.ToString(),
			UseShellExecute = false,
			CreateNoWindow = true,
		});
		launched = path;
	}

	static bool waitready(CancellationToken ct) {
		var start = Environment.TickCount;
		while (unchecked(Environment.TickCount - start) < 15000) {
			if (ct.IsCancellationRequested) return false;
			if (hastitle("skc-ready")) return true;
			if (!up(proc)) return false;
			Thread.Sleep(100);
		}
		return hastitle("skc-ready");
	}

	static void idleloop() {
		while (true) {
			Thread.Sleep(200);
			try { hide(); } catch { }
			lock (gate) {
				if (!due()) continue;
				stopunlocked();
			}
		}
	}

	static bool due() {
		lock (slots) {
			if (slots.Count > 0 || nextkill == 0) return false;
			return unchecked(Environment.TickCount - nextkill) >= 0;
		}
	}

	static void armidle() {
		var sec = Volatile.Read(ref idle);
		if (sec <= 0) {
			nextkill = 0;
			return;
		}
		nextkill = unchecked(Environment.TickCount + sec * 1000);
	}

	static void stopunlocked() {
		var p = proc;
		proc = null;
		launched = null;
		List<Slot> left;
		lock (slots) {
			ready = false;
			left = new List<Slot>(slots.Values);
			slots.Clear();
			order.Clear();
			nextkill = 0;
		}
		foreach (var s in left)
			s.Done?.TrySetException(new InvalidOperationException("Chrome 已退出"));
		killtree(p);
	}

	static void killtree(Process p) {
		if (p == null) return;
		try {
			if (!p.HasExited) {
				var kill = Process.Start(new ProcessStartInfo {
					FileName = "taskkill",
					Arguments = $"/T /F /PID {p.Id}",
					UseShellExecute = false,
					CreateNoWindow = true,
				});
				kill?.WaitForExit(3000);
			}
		}
		catch { }
		try { if (!p.HasExited) p.Kill(); } catch { }
		try { if (!p.HasExited) p.WaitForExit(3000); } catch { }
	}

	static bool up(Process p) {
		if (p == null) return false;
		try { return !p.HasExited; }
		catch { return false; }
	}

	static void acceptloop() {
		while (true) {
			TcpClient c;
			try { c = lis.AcceptTcpClient(); }
			catch { Thread.Sleep(200); continue; }
			ThreadPool.QueueUserWorkItem(serve, c);
		}
	}

	static void serve(object state) {
		var c = (TcpClient)state;
		try { readreq(c); }
		catch (Exception ex) { CaptureLog.Ex("chrome-host", ex); }
		try { c.Close(); } catch { }
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
		var line = head.Split('\n')[0].Trim();
		var sp = line.IndexOf(' ');
		var method = sp > 0 ? line.Substring(0, sp) : "";
		var url = sp > 0 ? line.Substring(sp + 1).Trim() : "";
		var sp2 = url.IndexOf(' ');
		if (sp2 > 0) url = url.Substring(0, sp2);
		var q = url.IndexOf('?');
		if (q >= 0) url = url.Substring(0, q);
		try { File.AppendAllText(Path.Combine(workdir(), "http.log"), method + " " + url + "\n"); } catch { }
		if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)) {
			if (url == "/next") {
				writetext(stream, nextpage());
				return;
			}
			writefile(stream, url);
			return;
		}
		try {
			var raw = Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n");
			stream.Write(raw, 0, raw.Length);
		}
		catch { }
		if (method.Equals("POST", StringComparison.OrdinalIgnoreCase))
			take(body);
	}

	static void writefile(Stream stream, string urlPath) {
		if (urlPath.StartsWith("/")) urlPath = urlPath.Substring(1);
		urlPath = urlPath.Replace('/', Path.DirectorySeparatorChar);
		byte[] bytes = null;
		if (urlPath.Length > 0 && !urlPath.Contains("..")) {
			try {
				var root = Path.GetFullPath(workdir());
				var full = Path.GetFullPath(Path.Combine(root, urlPath));
				if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
					bytes = File.ReadAllBytes(full);
			}
			catch { bytes = null; }
		}
		if (bytes == null) {
			var miss = Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
			try { stream.Write(miss, 0, miss.Length); } catch { }
			return;
		}
		var type = urlPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? "application/json" : "text/html";
		var head = "HTTP/1.1 200 OK\r\nContent-Type: " + type + "; charset=utf-8\r\nCache-Control: no-store\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n";
		var hb = Encoding.ASCII.GetBytes(head);
		try {
			stream.Write(hb, 0, hb.Length);
			stream.Write(bytes, 0, bytes.Length);
		}
		catch { }
	}

	static void take(string body) {
		if (string.IsNullOrWhiteSpace(body)) return;
		int id;
		var ok = false;
		var err = "";
		var items = new List<string>();
		try {
			var jo = JsonNode.Parse(body) as JsonObject;
			if (jo == null || jo["id"] == null) return;
			id = jo["id"].GetValue<int>();
			ok = jo["ok"] != null && jo["ok"].GetValue<bool>();
			err = jo["err"]?.GetValue<string>() ?? "";
			if (jo["items"] is JsonArray arr) {
				foreach (var n in arr)
					items.Add(n?.GetValue<string>() ?? "");
			}
		}
		catch { return; }
		Slot slot;
		lock (slots) {
			if (!slots.TryGetValue(id, out slot)) return;
			slots.Remove(id);
			if (slots.Count == 0) armidle();
		}
		cleanup(id);
		if (slot.Done == null) return;
		if (!ok)
			slot.Done.TrySetException(new InvalidOperationException(err.Length > 0 ? err : slot.Fail));
		else
			slot.Done.TrySetResult(items);
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

	static void writetext(Stream stream, string text) {
		var bytes = Encoding.UTF8.GetBytes(text ?? "");
		var head = "HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n";
		var hb = Encoding.ASCII.GetBytes(head);
		try {
			stream.Write(hb, 0, hb.Length);
			stream.Write(bytes, 0, bytes.Length);
		}
		catch { }
	}

	static string nextpage() {
		string url = "";
		lock (slots) {
			if (!ready) return "";
			while (order.Count > 0) {
				var id = order.Dequeue();
				if (!slots.TryGetValue(id, out var slot) || slot.Opened || string.IsNullOrEmpty(slot.Page)) continue;
				slot.Opened = true;
				url = "/" + slot.Page + "#" + slot.Id;
				break;
			}
		}
		if (url.Length > 0) show();
		return url;
	}

	static void cleanup(int id) {
		var dir = Path.Combine(workdir(), "jobs");
		try { File.Delete(Path.Combine(dir, id + ".json")); } catch { }
		try { File.Delete(Path.Combine(dir, id + ".stop")); } catch { }
	}

	static string workdir() {
		var dir = Path.GetDirectoryName(ChromeTts.ProfileDir());
		Directory.CreateDirectory(dir);
		return dir;
	}

	static void show() {
		Process p;
		lock (gate) p = proc;
		int pid = 0;
		if (p != null) {
			try { if (!p.HasExited) pid = p.Id; } catch { pid = 0; }
		}
		EnumWindows((h, _) => {
			var sb = new StringBuilder(256);
			var n = GetWindowText(h, sb, sb.Capacity);
			var text = n > 0 ? sb.ToString() : "";
			GetWindowThreadProcessId(h, out var wpid);
			if ((pid > 0 && (int)wpid == pid) || text.StartsWith("skc", StringComparison.Ordinal))
				ShowWindow(h, SW_SHOWNA);
			return true;
		}, IntPtr.Zero);
	}

	static void hide() {
		return;
		lock (slots) {
			if (slots.Count > 0) return;
		}
		Process p;
		lock (gate) p = proc;
		int pid = 0;
		if (p != null) {
			try { if (!p.HasExited) pid = p.Id; } catch { pid = 0; }
		}
		EnumWindows((h, _) => {
			var sb = new StringBuilder(256);
			var n = GetWindowText(h, sb, sb.Capacity);
			var text = n > 0 ? sb.ToString() : "";
			GetWindowThreadProcessId(h, out var wpid);
			if ((pid > 0 && (int)wpid == pid) || text.StartsWith("skc", StringComparison.Ordinal))
				ShowWindow(h, SW_HIDE);
			return true;
		}, IntPtr.Zero);
	}

	static bool hastitle(string part) {
		var ok = false;
		EnumWindows((h, _) => {
			var sb = new StringBuilder(256);
			if (GetWindowText(h, sb, sb.Capacity) <= 0) return true;
			if (sb.ToString().IndexOf(part, StringComparison.Ordinal) >= 0) ok = true;
			return true;
		}, IntPtr.Zero);
		return ok;
	}

	static string pagehtml(string js, int listenPort) {
		return "<!DOCTYPE html><meta charset=\"utf-8\"><title>skc-job</title><script>"
			+ js.Replace("@@PORT@@", listenPort.ToString())
			+ "</script>";
	}

	static string pinghtml(int listenPort) {
		return pagehtml(pingjs, listenPort);
	}

	const string boothtml = "<!DOCTYPE html><meta charset=\"utf-8\"><title>skc-ready</title><script>function poll(){var x=new XMLHttpRequest();x.open('GET','/next',true);x.onload=function(){var t=(x.responseText||'').replace(/\\s/g,'');if(t.charAt(0)=='/')location.href=t;else setTimeout(poll,200);};x.onerror=function(){setTimeout(poll,400);};try{x.send();}catch(e){setTimeout(poll,400);}}poll();</script>";

	const string qidjs = @"
function qid(){var s=location.hash||'';if(s.charAt(0)=='#')s=s.substring(1);var n=parseInt(s,10);return n||0;}
var id=qid();
var stopping=false;
document.title='skc-job-'+id;
function watch(){
var x=new XMLHttpRequest();
x.open('GET','jobs/'+id+'.stop',true);
x.onload=function(){if((x.responseText||'').charAt(0)=='1'){stopping=true;try{if(window.speechSynthesis)speechSynthesis.cancel();}catch(e){}location.href='/boot.html';}};
try{x.send();}catch(e){}
}
setInterval(watch,200);
";

	const string speakjs = qidjs + @"
function post(ok,err){
var body=JSON.stringify({id:id,ok:!!ok,err:err||'',items:[]});
var done=function(){setTimeout(function(){location.href='/boot.html';},200);};
try{fetch('http://127.0.0.1:@@PORT@@/r',{method:'POST',mode:'no-cors',headers:{'Content-Type':'text/plain'},body:body}).then(done,done);}catch(e){done();}
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
var started=false;
function go(job,v){
if(started)return;
started=true;
try{
var u=new SpeechSynthesisUtterance(job.text||'');
if(v){u.voice=v;u.lang=v.lang||job.lang||'';}else u.lang=job.lang||'';
u.rate=job.rate>0?+job.rate:1;u.volume=job.volume==null?1:+job.volume;
u.onend=function(){post(true,'');};
u.onerror=function(e){if(stopping)return;var m=(e&&e.error)||'';if(!m||m.indexOf('Receiving end')>=0)m='Chrome 没有发出声音';post(false,m);};
document.title='skc-go';
try{speechSynthesis.cancel();}catch(e){}
speechSynthesis.speak(u);
try{if(speechSynthesis.paused)speechSynthesis.resume();}catch(e){}
}catch(e){post(false,e&&e.message?e.message:'speak');}
}
function speak(job){
if(!window.speechSynthesis){post(false,'Chrome 没有发出声音');return;}
var v=pick(job);
if(v){go(job,v);return;}
var t0=Date.now();
var timer=setInterval(function(){
var u=pick(job);
if(u){clearInterval(timer);go(job,u);}
else if(Date.now()-t0>12000){clearInterval(timer);post(false,'Chrome 没有发出声音');}
},200);
speechSynthesis.onvoiceschanged=function(){var u=pick(job);if(u){clearInterval(timer);go(job,u);}};
}
function load(){
document.title='skc-load-'+id;
var x=new XMLHttpRequest();
x.open('GET','jobs/'+id+'.json',true);
x.onload=function(){var j;try{j=JSON.parse(x.responseText||'');}catch(e){post(false,'job');return;}document.title='skc-json';speak(j);};
x.onerror=function(){post(false,'job');};
try{x.send();}catch(e){post(false,'job');}
}
load();
";

	const string trjs = qidjs + @"
function post(jobId,ok,err,items){
var body=JSON.stringify({id:jobId,ok:!!ok,err:err||'',items:items||[]});
var done=function(){setTimeout(function(){location.href='/boot.html';},200);};
try{fetch('http://127.0.0.1:@@PORT@@/r',{method:'POST',mode:'no-cors',headers:{'Content-Type':'text/plain'},body:body}).then(done,done);}catch(e){done();}
}
function ctor(){
if(typeof Translator==='function')return Translator;
if(self.ai&&typeof self.ai.translator==='function')return self.ai.translator;
return null;
}
async function run(job){
var C=ctor();
if(!C){post(job.id,false,'这个 Chrome 没有翻译接口',[]);return;}
try{
var opt={sourceLanguage:job.src,targetLanguage:job.dst};
var avail='available';
if(typeof C.availability==='function')avail=await C.availability(opt);
if(avail==='unavailable'||avail==='no'){post(job.id,false,'Chrome 不能翻译这对语言',[]);return;}
var t=await C.create(opt),outs=[],i,s;
for(i=0;i<job.items.length;i++){
s=job.items[i]||'';
outs.push(s.trim()?await t.translate(s):'');
}
post(job.id,true,'',outs);
}catch(e){post(job.id,false,e&&e.message?e.message:'Chrome 翻译失败',[]);}
}
function load(){
var x=new XMLHttpRequest();
x.open('GET','jobs/'+id+'.json',true);
x.onload=function(){var j;try{j=JSON.parse(x.responseText||'');}catch(e){post(id,false,'job',[]);return;}run(j);};
x.onerror=function(){post(id,false,'job',[]);};
try{x.send();}catch(e){post(id,false,'job',[]);}
}
load();
";

	const string pingjs = @"
function qid(){var s=location.hash||'';if(s.charAt(0)=='#')s=s.substring(1);var n=parseInt(s,10);return n||0;}
var id=qid();
document.title='skc-job-'+id;
var body=JSON.stringify({id:id,ok:true,err:'',items:['P']});
fetch('http://127.0.0.1:@@PORT@@/r',{method:'POST',mode:'no-cors',headers:{'Content-Type':'text/plain'},body:body}).then(function(){location.href='/boot.html';},function(){location.href='/boot.html';});
";

	delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
	[DllImport("user32.dll")] static extern bool EnumWindows(EnumProc lpEnumFunc, IntPtr lParam);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
	[DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
	[DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
