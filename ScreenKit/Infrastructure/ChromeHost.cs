using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenKit;

/// <summary>一个不出现在任务栏上的 Chrome。朗读和翻译在同一个窗口里排队，这一页保持在前台。空闲时藏起来，正在朗读时停在屏幕外。</summary>
static class ChromeHost {
	static readonly object gate = new();
	static readonly Dictionary<int, Slot> slots = new();
	static readonly Queue<int> order = new();
	static Process proc;
	static string launched;
	static string launchedpx;
	static int debugPort;
	static int pin;
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

	public static List<string> Translate(List<string> texts, string src, string dst, string exe, int idleSec, CancellationToken ct, Action<double> progress = null) {
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
		try {
			return translateonce(id, json, exe, idleSec, ct);
		}
		catch (InvalidOperationException ex) when (needpack(ex)) {
			ChromePack.EnsurePair(src, dst, exe, ct, progress);
			id = Interlocked.Increment(ref seq);
			jo["id"] = id;
			json = jo.ToJsonString().Replace("<", "\\u003c");
			return translateonce(id, json, exe, idleSec, ct);
		}
	}

	static List<string> translateonce(int id, string json, string exe, int idleSec, CancellationToken ct) {
		var done = waiter(true);
		var slot = new Slot { Id = id, Page = "tr.html", Fail = "Chrome 翻译失败", Done = done };
		submit(id, "tr.html", json, slot, exe, idleSec, ct);
		return waitdone(id, done, ct, "Chrome 翻译超时");
	}

	static bool needpack(Exception ex) {
		var msg = ex?.Message ?? "";
		return msg.IndexOf("user gesture", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	public static void Stop() {
		lock (gate) stopunlocked();
	}

	/// <summary>可见模型页用同一份配置。隐藏窗口停在屏幕外，这个位置会被记下来，下次就只露出一条。打开前改回当前屏幕，并清掉崩溃恢复。</summary>
	public static (int left, int top, int width, int height) PrepareVisible() {
		var (left, top, width, height, wl, wt, wr, wb) = visibleframe();
		writeplacement(left, top, width, height, wl, wt, wr, wb);
		clearsessions();
		return (left, top, width, height);
	}

	/// <summary>命令行和配置都可能被忽略。窗口出来后若仍大半在屏幕外，再把它移回给定位置。</summary>
	public static void NudgeVisible(int pid, int left, int top, int width, int height) {
		if (width < 200 || height < 200) return;
		var t = new Thread(() => nudgevisible(pid, left, top, width, height)) { IsBackground = true, Name = "chrome-show" };
		t.Start();
	}

	/// <summary>只打断朗读，翻译任务留着。晚到的停止请求不碰之后新建的朗读。</summary>
	public static void StopSpeak() {
		var cutoff = Volatile.Read(ref seq);
		var ids = new List<int>();
		lock (slots) {
			foreach (var s in slots.Values) {
				if (s.Page == "speak.html" && s.Id <= cutoff) ids.Add(s.Id);
			}
		}
		foreach (var id in ids) cancel(id);
	}

	public static bool TryRun(out int children) {
		children = 0;
		var pid = ownpid();
		if (pid <= 0) return false;
		var n = ChromeTts.ChildCount(pid);
		children = n < 0 ? 0 : n;
		return true;
	}

	/// <summary>本进程和子进程的工作集。没在跑时为 0。</summary>
	public static long WorkingSet() {
		var pid = ownpid();
		if (pid <= 0) return 0;
		return ChromeTts.TreeWorkingSet(pid);
	}

	static int hostpid;
	static int lasthost;

	static int ownpid() {
		Process p;
		lock (gate) p = proc;
		if (up(p)) {
			try { return p.Id; }
			catch { }
		}
		if (string.IsNullOrEmpty(launched)) return 0;
		if (alive(hostpid)) return hostpid;
		hostpid = 0;
		var now = Environment.TickCount;
		if (lasthost != 0 && unchecked(now - lasthost) < 1500) return 0;
		lasthost = now;
		var dbg = debugPort > 0 ? debugPort : readdebug();
		if (dbg <= 0) return 0;
		hostpid = listenpid(dbg);
		return alive(hostpid) ? hostpid : 0;
	}

	static int listenpid(int dbg) {
		try { return PortUse.ListenerPid(dbg); }
		catch { return 0; }
	}

	/// <summary>下载模型时别把窗口藏起来，也别按空闲关掉。</summary>
	public static IDisposable Pin() {
		Interlocked.Increment(ref pin);
		try { cloak(); } catch { }
		return new Pinned();
	}

	sealed class Pinned : IDisposable {
		public void Dispose() {
			if (Interlocked.Decrement(ref pin) < 0) Interlocked.Exchange(ref pin, 0);
		}
	}

	/// <summary>正在跑的这只 Chrome 的调试端口。没有开调试时为 0。</summary>
	public static int DebugPort() {
		if (debugPort <= 0) debugPort = readdebug();
		if (debugPort <= 0) return 0;
		if (TryRun(out _)) return debugPort;
		try {
			var req = WebRequest.Create("http://127.0.0.1:" + debugPort + "/json/version");
			req.Timeout = 400;
			using (req.GetResponse()) { }
			return debugPort;
		}
		catch {
			debugPort = 0;
			return readdebug();
		}
	}

	static int readdebug() {
		try {
			var path = Path.Combine(ChromeTts.ProfileDir(), "DevToolsActivePort");
			if (!File.Exists(path)) return 0;
			var line = File.ReadAllLines(path);
			if (line.Length == 0) return 0;
			return int.TryParse(line[0].Trim(), out var n) ? n : 0;
		}
		catch { return 0; }
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
			if (DebugPort() <= 0) return 3;
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
		var json = "{\"op\":\"speak\",\"id\":" + id + ",\"text\":\"hi\",\"name\":\"Google US English\",\"lang\":\"en-US\",\"rate\":1,\"volume\":1}";
		submit(id, "speak.html", json, slot, "", 60, CancellationToken.None);
		try {
			waitdone(id, done, CancellationToken.None, "Chrome 朗读超时");
		}
		catch (InvalidOperationException ex) {
			if (ex.Message.IndexOf("没有发出声音", StringComparison.Ordinal) < 0) throw;
		}
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
		if (page == "speak.html")
			ThreadPool.QueueUserWorkItem(_ => pumpspeak(id, ct));
	}

	static void pumpspeak(int id, CancellationToken ct) {
		var start = Environment.TickCount;
		var pagePort = port;
		while (unchecked(Environment.TickCount - start) < 90000) {
			if (ct.IsCancellationRequested) return;
			lock (slots) {
				if (!slots.ContainsKey(id)) return;
			}
			var p = DebugPort();
			if (p <= 0) {
				Thread.Sleep(200);
				continue;
			}
			string r;
			try { r = ChromePack.NudgeSpeak(p, id, pagePort, ct); }
			catch { r = ""; }
			if (r == "armed" || r == "busy" || r == "fail") return;
			Thread.Sleep(200);
		}
		lock (slots) {
			if (!slots.ContainsKey(id)) return;
		}
		var debug = DebugPort();
		if (debug <= 0) return;
		try { ChromePack.FailSpeak(debug, id, pagePort, ct); }
		catch { }
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
		var px = HttpProxy.ChromeArgs();
		for (var attempt = 0; attempt < 3; attempt++) {
			lock (gate) {
				Volatile.Write(ref idle, idleSec);
				if (up(proc) && string.Equals(launched, path, StringComparison.OrdinalIgnoreCase)
					&& string.Equals(launchedpx, px, StringComparison.Ordinal)) {
					lock (slots) ready = true;
					return;
				}
				stopunlocked();
				starthttp();
				launch(path, px);
			}
			if (waitready(ct)) {
				debugPort = readdebug();
				lock (slots) {
					ready = true;
					if (slots.Count == 0) armidle();
				}
				return;
			}
			var cancel = ct.IsCancellationRequested;
			lock (gate) stopunlocked();
			if (cancel) throw new OperationCanceledException(ct);
			killstale();
			Thread.Sleep(800);
		}
		throw new InvalidOperationException("Chrome 没有打开朗读页");
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

	static void launch(string path, string px) {
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
		args.Append("--remote-debugging-address=127.0.0.1 ");
		args.Append("--remote-debugging-port=0 ");
		args.Append("--no-first-run --no-default-browser-check --disable-sync ");
		args.Append("--disable-breakpad --disable-crash-reporter ");
		args.Append("--autoplay-policy=no-user-gesture-required ");
		args.Append("--disable-backgrounding-occluded-windows --disable-renderer-backgrounding ");
		args.Append("--disable-features=CalculateNativeWinOcclusion,UseSystemProxyResolver ");
		args.Append("--disable-session-crashed-bubble --hide-crash-restore-bubble ");
		args.Append("--window-position=-32000,-32000 --window-size=160,80 ");
		args.Append(px ?? "");
		args.Append('"').Append(url).Append('"');
		proc = ChromeTts.StartProc(path, args.ToString());
		launched = path;
		launchedpx = px ?? "";
	}

	static bool waitready(CancellationToken ct) {
		var start = Environment.TickCount;
		while (unchecked(Environment.TickCount - start) < 15000) {
			if (ct.IsCancellationRequested) return false;
			if (hastitle("skc-ready")) {
				cloak();
				return true;
			}
			if (!up(proc)) return false;
			Thread.Sleep(100);
		}
		var ok = hastitle("skc-ready");
		if (ok) cloak();
		return ok;
	}

	static void idleloop() {
		while (true) {
			Thread.Sleep(200);
			try {
				cloak();
				lock (gate) {
					if (!due()) continue;
					stopunlocked();
				}
			}
			catch (Exception ex) { CaptureLog.Ex("chrome-hide", ex); }
		}
	}

	static bool due() {
		lock (slots) {
			if (slots.Count > 0 || Volatile.Read(ref pin) > 0 || nextkill == 0) return false;
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
		var dbg = debugPort;
		proc = null;
		launched = null;
		launchedpx = null;
		debugPort = 0;
		hostpid = 0;
		lasthost = 0;
		parked.Clear();
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
		var pid = 0;
		try { if (up(p)) pid = p.Id; } catch { }
		killtree(p);
		if (dbg <= 0) dbg = readdebug();
		var listen = listenpid(dbg);
		if (listen > 0 && listen != pid) ChromeTts.KillPid(listen);
		killstale();
		try { p?.Dispose(); } catch { }
	}

	static void killstale() {
		try {
			using var search = new System.Management.ManagementObjectSearcher(
				"SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='chrome.exe'");
			foreach (System.Management.ManagementObject mo in search.Get()) {
				try {
					var cmd = mo["CommandLine"] as string ?? "";
					if (cmd.IndexOf("sk-chrome-tts", StringComparison.OrdinalIgnoreCase) < 0) continue;
					var pid = Convert.ToInt32(mo["ProcessId"]);
					ChromeTts.KillPid(pid);
				}
				finally { try { mo.Dispose(); } catch { } }
			}
		}
		catch { }
	}

	static void killtree(Process p) {
		if (p == null) return;
		var pid = 0;
		try { if (!p.HasExited) pid = p.Id; }
		catch { return; }
		if (pid > 0) ChromeTts.KillPid(pid);
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
		var query = "";
		var q = url.IndexOf('?');
		if (q >= 0) {
			query = url.Substring(q + 1);
			url = url.Substring(0, q);
		}
		if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)) {
			if (url == "/next") {
				var speakonly = query.IndexOf("speak=1", StringComparison.Ordinal) >= 0;
				writetext(stream, nextpage(speakonly));
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
		if (!ok) {
			if (err == "chrome-google") err = Loc.T("tr.chrome.google");
			slot.Done.TrySetException(new InvalidOperationException(err.Length > 0 ? err : slot.Fail));
		}
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

	static string nextpage(bool speakonly = false) {
		string body = "";
		lock (slots) {
			if (!ready) return "";
			while (order.Count > 0) {
				var id = order.Peek();
				if (!slots.TryGetValue(id, out var slot) || slot.Opened) {
					order.Dequeue();
					continue;
				}
				var path = Path.Combine(workdir(), "jobs", id + ".json");
				if (!File.Exists(path)) break;
				try { body = File.ReadAllText(path); }
				catch { break; }
				if (string.IsNullOrWhiteSpace(body)) break;
				// 正在朗读时只取下一句，翻译任务留在队头，等这一轮朗读让开。
				if (speakonly && body.IndexOf("\"op\":\"speak\"", StringComparison.Ordinal) < 0) {
					body = "";
					break;
				}
				order.Dequeue();
				slot.Opened = true;
				break;
			}
		}
		if (body.Length > 0) cloak();
		return body;
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

	const int GWL_EXSTYLE = -20;
	const int WS_EX_TOOLWINDOW = 0x80;
	const int WS_EX_APPWINDOW = 0x40000;
	const int SW_HIDE = 0;
	const int SW_SHOWNA = 8;
	const int SW_RESTORE = 9;
	const uint GW_OWNER = 4;
	const uint SWP_NOZORDER = 0x0004;
	const uint SWP_NOACTIVATE = 0x0010;
	const uint SWP_SHOWWINDOW = 0x0040;
	const int SM_XVIRTUALSCREEN = 76;
	const int SM_YVIRTUALSCREEN = 77;
	const int SM_CXVIRTUALSCREEN = 78;
	const int SM_CYVIRTUALSCREEN = 79;

	static bool cloakedBusy;
	static readonly HashSet<IntPtr> parked = new();
	static int nexthide;
	const uint SWP_ASYNCWINDOWPOS = 0x4000;

	static void cloak() {
		var busy = false;
		lock (slots) busy = slots.Count > 0 || Volatile.Read(ref pin) > 0;
		var flip = busy != cloakedBusy;
		cloakedBusy = busy;
		var now = Environment.TickCount;
		var retry = nexthide == 0 || unchecked(now - nexthide) >= 0;
		EnumWindows((h, _) => {
			var sb = new StringBuilder(256);
			if (InternalGetWindowText(h, sb, sb.Capacity) <= 0) return true;
			if (!sb.ToString().StartsWith("skc", StringComparison.Ordinal)) return true;
			var ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
			var next = (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
			if (next != ex) SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(next));
			if (busy) {
				if (flip || !parked.Contains(h)) {
					ShowWindowAsync(h, SW_SHOWNA);
					SetWindowPos(h, IntPtr.Zero, -32000, -32000, 160, 80, SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_ASYNCWINDOWPOS);
					parked.Add(h);
				}
			}
			else if (flip || !parked.Contains(h) || (retry && IsWindowVisible(h))) {
				ShowWindowAsync(h, SW_HIDE);
				parked.Add(h);
				nexthide = unchecked(now + 4000);
			}
			return true;
		}, IntPtr.Zero);
	}

	static (int left, int top, int width, int height, int wl, int wt, int wr, int wb) visibleframe() {
		var hwnd = IntPtr.Zero;
		try {
			var win = System.Windows.Application.Current?.MainWindow;
			if (win != null) hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
		}
		catch { }
		var mon = MonitorFromWindow(hwnd, hwnd == IntPtr.Zero ? 1u : 2u);
		var info = new MONITORINFO();
		info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
		if (mon == IntPtr.Zero || !GetMonitorInfo(mon, ref info)) {
			info.rcWork.Left = 0;
			info.rcWork.Top = 0;
			info.rcWork.Right = GetSystemMetrics(0);
			info.rcWork.Bottom = GetSystemMetrics(1);
		}
		var wl = info.rcWork.Left;
		var wt = info.rcWork.Top;
		var wr = info.rcWork.Right;
		var wb = info.rcWork.Bottom;
		var ww = Math.Max(1, wr - wl);
		var wh = Math.Max(1, wb - wt);
		var width = Math.Min(1200, ww - 80);
		var height = Math.Min(820, wh - 80);
		if (width < 480) width = Math.Max(320, ww - 16);
		if (height < 360) height = Math.Max(240, wh - 16);
		if (width > ww) width = ww;
		if (height > wh) height = wh;
		var left = wl + Math.Max(0, (ww - width) / 2);
		var top = wt + Math.Max(0, (wh - height) / 2);
		return (left, top, width, height, wl, wt, wr, wb);
	}

	static void writeplacement(int left, int top, int width, int height, int wl, int wt, int wr, int wb) {
		var path = Path.Combine(ChromeTts.ProfileDir(), "Default", "Preferences");
		if (!File.Exists(path)) return;
		var text = File.ReadAllText(path);
		var root = JsonNode.Parse(text) as JsonObject;
		if (root == null) return;
		var browser = root["browser"] as JsonObject;
		if (browser == null) {
			browser = new JsonObject();
			root["browser"] = browser;
		}
		browser["window_placement"] = new JsonObject {
			["bottom"] = top + height,
			["left"] = left,
			["maximized"] = false,
			["right"] = left + width,
			["top"] = top,
			["work_area_bottom"] = wb,
			["work_area_left"] = wl,
			["work_area_right"] = wr,
			["work_area_top"] = wt,
		};
		var prof = root["profile"] as JsonObject;
		if (prof == null) {
			prof = new JsonObject();
			root["profile"] = prof;
		}
		prof["exit_type"] = "Normal";
		prof["exited_cleanly"] = true;
		var next = root.ToJsonString();
		if (next.Length * 2 < text.Length) return;
		File.WriteAllText(path, next, new UTF8Encoding(false));
	}

	static void clearsessions() {
		var def = Path.Combine(ChromeTts.ProfileDir(), "Default");
		foreach (var name in new[] { "Current Session", "Current Tabs", "Last Session", "Last Tabs" }) {
			try { File.Delete(Path.Combine(def, name)); } catch { }
		}
		var sessions = Path.Combine(def, "Sessions");
		try {
			if (!Directory.Exists(sessions)) return;
			foreach (var f in Directory.GetFiles(sessions))
				try { File.Delete(f); } catch { }
		}
		catch { }
	}

	static void nudgevisible(int pid, int left, int top, int width, int height) {
		var start = Environment.TickCount;
		var stable = 0;
		HashSet<int> extra = null;
		var nextExtra = 0;
		while (unchecked(Environment.TickCount - start) < 5000) {
			HashSet<int> pids;
			if (alive(pid)) pids = treepids(pid);
			else {
				if (extra == null || unchecked(Environment.TickCount - nextExtra) >= 0) {
					extra = profilepids();
					nextExtra = unchecked(Environment.TickCount + 1000);
				}
				pids = extra;
			}
			var any = false;
			var bad = false;
			EnumWindows((h, _) => {
				GetWindowThreadProcessId(h, out var wpid);
				if (pids == null || !pids.Contains((int)wpid)) return true;
				if (GetWindow(h, GW_OWNER) != IntPtr.Zero) return true;
				if (!ischromewin(h) || !IsWindowVisible(h)) return true;
				any = true;
				GetWindowRect(h, out var r);
				if (!needsplace(r)) return true;
				bad = true;
				ShowWindow(h, SW_RESTORE);
				SetWindowPos(h, IntPtr.Zero, left, top, width, height, SWP_NOZORDER | SWP_SHOWWINDOW);
				return true;
			}, IntPtr.Zero);
			if (any && !bad) {
				stable++;
				if (stable >= 3) return;
			} else stable = 0;
			Thread.Sleep(200);
		}
	}

	static bool needsplace(RECT r) {
		var w = r.Right - r.Left;
		var h = r.Bottom - r.Top;
		if (w < 420 || h < 280) return true;
		var vl = GetSystemMetrics(SM_XVIRTUALSCREEN);
		var vt = GetSystemMetrics(SM_YVIRTUALSCREEN);
		var vr = vl + GetSystemMetrics(SM_CXVIRTUALSCREEN);
		var vb = vt + GetSystemMetrics(SM_CYVIRTUALSCREEN);
		var il = Math.Max(r.Left, vl);
		var it = Math.Max(r.Top, vt);
		var ir = Math.Min(r.Right, vr);
		var ib = Math.Min(r.Bottom, vb);
		if (ir <= il || ib <= it) return true;
		return (long)(ir - il) * (ib - it) * 2 < (long)w * h;
	}

	static bool ischromewin(IntPtr h) {
		var sb = new StringBuilder(64);
		return GetClassName(h, sb, sb.Capacity) > 0 && sb.ToString() == "Chrome_WidgetWin_1";
	}

	static bool alive(int pid) {
		if (pid <= 0) return false;
		var h = OpenProcess(0x1000, false, pid);
		if (h == IntPtr.Zero) return false;
		try {
			if (!GetExitCodeProcess(h, out var code)) return false;
			return code == 259;
		}
		finally { CloseHandle(h); }
	}

	static HashSet<int> treepids(int pid) {
		var set = new HashSet<int>();
		if (pid <= 0) return set;
		var snap = CreateToolhelp32Snapshot(2, 0);
		if (snap == IntPtr.Zero || snap == new IntPtr(-1)) {
			set.Add(pid);
			return set;
		}
		try {
			var rows = new List<int[]>();
			var pe = new PROCESSENTRY32();
			pe.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
			if (!Process32First(snap, ref pe)) {
				set.Add(pid);
				return set;
			}
			do rows.Add(new[] { (int)pe.th32ProcessID, (int)pe.th32ParentProcessID });
			while (Process32Next(snap, ref pe));
			var q = new Queue<int>();
			q.Enqueue(pid);
			set.Add(pid);
			while (q.Count > 0) {
				var id = q.Dequeue();
				foreach (var row in rows) {
					if (row[1] != id || !set.Add(row[0])) continue;
					q.Enqueue(row[0]);
				}
			}
			return set;
		}
		finally { CloseHandle(snap); }
	}

	static HashSet<int> profilepids() {
		var set = new HashSet<int>();
		try {
			using var search = new System.Management.ManagementObjectSearcher(
				"SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='chrome.exe'");
			foreach (System.Management.ManagementObject mo in search.Get()) {
				var cmd = mo["CommandLine"] as string ?? "";
				if (cmd.IndexOf("sk-chrome-tts", StringComparison.OrdinalIgnoreCase) < 0) continue;
				set.Add(Convert.ToInt32(mo["ProcessId"]));
			}
		}
		catch { }
		return set;
	}

	static bool hastitle(string part) {
		var ok = false;
		EnumWindows((h, _) => {
			var sb = new StringBuilder(256);
			if (InternalGetWindowText(h, sb, sb.Capacity) <= 0) return true;
			if (sb.ToString().IndexOf(part, StringComparison.Ordinal) >= 0) ok = true;
			return true;
		}, IntPtr.Zero);
		return ok;
	}

	const string boothtml = "<!DOCTYPE html><meta charset=\"utf-8\"><title>skc-ready</title><script>\n"
		+ "var stopping=false,cur=0;\n"
		+ "function busy(){return (window.__skn||0)>0||(window.__skq&&window.__skq.length>0);}\n"
		+ "function poll(){pull(!!busy());}\n"
		+ "function pull(speakonly){if(window.__skpoll)return;window.__skpoll=1;var x=new XMLHttpRequest();x.open('GET',speakonly?'/next?speak=1':'/next',true);x.onload=function(){window.__skpoll=0;var t=x.responseText||'';if(t.charAt(0)!='{'){if(speakonly){pull(false);return;}setTimeout(poll,busy()?80:200);return;}var j;try{j=JSON.parse(t);}catch(e){setTimeout(poll,200);return;}cur=j.id||0;stopping=false;if(j.op=='ping'){post(j,true,'',['P']);return;}if(j.op=='tr'){runtr(j);return;}speak(j);if(window.__skpump)window.__skpump(j.id);setTimeout(poll,30);};x.onerror=function(){window.__skpoll=0;setTimeout(poll,400);};try{x.send();}catch(e){window.__skpoll=0;setTimeout(poll,400);}}\n"
		+ "function post(job,ok,err,items){cur=0;var body=JSON.stringify({id:job.id,ok:!!ok,err:err||'',items:items||[]});var done=function(){setTimeout(poll,50);};try{fetch('/r',{method:'POST',mode:'no-cors',headers:{'Content-Type':'text/plain'},body:body}).then(done,done);}catch(e){done();}}\n"
		+ "function watch(){var arm=window.__skarm||{},ids=[],k;for(k in arm)if(arm[k]===1)ids.push(+k);if(!ids.length)return;var n=0;function hit(){if(stopping||n>=ids.length)return;var id=ids[n++],x=new XMLHttpRequest();x.open('GET','jobs/'+id+'.stop',true);x.onload=function(){if(arm[id]!==1){hit();return;}if((x.responseText||'').charAt(0)=='1'){stopping=true;window.__skq=[];try{if(window.speechSynthesis)speechSynthesis.cancel();}catch(e){}setTimeout(poll,50);return;}hit();};try{x.send();}catch(e){hit();}}hit();}\n"
		+ "setInterval(watch,200);\n"
		+ "function pick(job){var list=speechSynthesis.getVoices(),name=(job.name||'').toLowerCase(),lang=(job.lang||'').toLowerCase(),i,n,vl,google=[];function mine(v){n=v.name||'';return n.indexOf('Google')===0||n.indexOf('Chrome OS')===0;}function canon(s){s=(s||'').toLowerCase().replace(/_/g,'-');if(s==='cmn-cn'||s==='zh-hans')return 'zh-cn';if(s==='cmn-tw'||s==='zh-hant')return 'zh-tw';if(s==='yue'||s==='yue-hk')return 'zh-hk';return s;}for(i=0;i<list.length;i++)if(mine(list[i]))google.push(list[i]);if(name){var namedLocal=null,namedAny=null;for(i=0;i<google.length;i++){if((google[i].name||'').toLowerCase()!==name)continue;if(google[i].localService){namedLocal=google[i];break;}if(!namedAny)namedAny=google[i];}if(namedLocal)return namedLocal;if(namedAny&&canon(lang)==='zh-tw')return namedAny;}function same(v){vl=canon(v.lang);var want=canon(lang);if(!want||!vl)return false;if(vl===want)return true;if(vl.indexOf(want)===0)return true;return want.indexOf(vl)===0&&vl.length>=2;}function zhfam(s){s=canon(s);return s.indexOf('zh')===0||s==='yue'||s.indexOf('yue-')===0;}if(lang){var localGoogle=null,localOs=null,famGoogle=null,famOs=null,remoteNamed=null,remote=null,wantZh=zhfam(lang);for(i=0;i<google.length;i++){n=google[i].name||'';var exact=same(google[i]);var fam=wantZh&&google[i].localService&&zhfam(google[i].lang);if(!exact&&!fam)continue;if(exact&&google[i].localService&&n.indexOf('Google')===0){if(!localGoogle)localGoogle=google[i];}else if(exact&&google[i].localService){if(!localOs)localOs=google[i];}else if(fam&&n.indexOf('Google')===0){if(!famGoogle)famGoogle=google[i];}else if(fam){if(!famOs)famOs=google[i];}else if(exact&&(n||'').toLowerCase()===name){if(!remoteNamed)remoteNamed=google[i];}else if(exact&&!remote)remote=google[i];}return localGoogle||localOs||famGoogle||famOs||remoteNamed||remote;}for(i=0;i<google.length;i++)if(google[i].localService)return google[i];if(google.length)return google[0];return null;}\n"
		+ "function markend(id){window.__skarm=window.__skarm||{};if(window.__skarm[id]!==1)return;window.__skarm[id]=2;window.__skn=Math.max(0,(window.__skn||1)-1);}\n"
		+ "function go(job,v){try{var u=new SpeechSynthesisUtterance(job.text||'');if(v){u.voice=v;u.lang=v.lang||job.lang||'';}else u.lang=job.lang||'';u.rate=job.rate>0?+job.rate:1;u.volume=job.volume==null?1:+job.volume;var heard=false,done=false;u.onstart=function(){heard=true;};u.onend=function(){if(done)return;done=true;markend(job.id);if(stopping)return;post(job,true,'',[]);};u.onerror=function(e){if(done)return;done=true;markend(job.id);if(stopping)return;var m=(e&&e.error)||'';if(!m||m.indexOf('Receiving end')>=0)m='Chrome 没有发出声音';post(job,false,m,[]);};window.__skn=(window.__skn||0)+1;try{speechSynthesis.resume();speechSynthesis.speak(u);}catch(e){if(!done){done=true;markend(job.id);post(job,false,e&&e.message?e.message:'speak',[]);}return;}setTimeout(function tick(){if(stopping||done||heard)return;if(speechSynthesis.speaking||speechSynthesis.pending){setTimeout(tick,500);return;}done=true;markend(job.id);post(job,false,'Chrome 没有发出声音',[]);},8000);}catch(e){post(job,false,e&&e.message?e.message:'speak',[]);}}\n"
		+ "function speak(job){window.__skq=window.__skq||[];window.__skq.push(job);}\n"
		+ "window.__skpump=function(id){window.__skarm=window.__skarm||{};if(window.__skarm[id])return 'busy';var q=window.__skq||[],i,job=null;for(i=0;i<q.length;i++)if(q[i]&&q[i].id===id){job=q[i];break;}if(!job)return 'idle';if(i>0)return 'idle';if(!window.speechSynthesis){q.splice(i,1);post(job,false,'Chrome 没有发出声音',[]);return 'fail';}try{speechSynthesis.getVoices();}catch(e){}var v=pick(job);if(!v)return 'novoice';q.splice(i,1);window.__skarm[id]=1;window.__skbusy=id;go(job,v);return 'armed';};\n"
		+ "window.__skfail=function(id){var q=window.__skq||[],i;for(i=0;i<q.length;i++)if(q[i]&&q[i].id===id){var job=q[i];q.splice(i,1);post(job,false,'Chrome 没有发出声音',[]);return 'fail';}return 'idle';};\n"
		+ "function ctor(){if(typeof Translator==='function')return Translator;if(self.ai&&typeof self.ai.translator==='function')return self.ai.translator;return null;}\n"
		+ "function googup(){var c=new AbortController();var t=setTimeout(function(){try{c.abort();}catch(e){}},8000);return fetch('https://www.google.com/generate_204',{mode:'no-cors',cache:'no-store',signal:c.signal}).then(function(){clearTimeout(t);return true;},function(){clearTimeout(t);return false;});}\n"
		+ "async function runtr(job){var C=ctor();if(!C){post(job,false,'这个 Chrome 没有翻译接口',[]);return;}try{var opt={sourceLanguage:job.src,targetLanguage:job.dst};var avail='available';if(typeof C.availability==='function'){var av=C.availability(opt);avail=await Promise.race([av,new Promise(function(_,rej){setTimeout(function(){rej(new Error('chrome-google'));},8000);})]);}if(avail==='unavailable'||avail==='no'){post(job,false,'Chrome 不能翻译这对语言',[]);return;}var need=avail==='downloadable'||avail==='downloading';var seen=false;if(need){if(!await googup()){post(job,false,'chrome-google',[]);return;}opt.monitor=function(m){if(m&&m.addEventListener)m.addEventListener('downloadprogress',function(){seen=true;});};}var creating=C.create(opt);var t,settled=null;creating.then(function(x){settled={ok:x};},function(e){settled={err:e};});if(need){var t0=Date.now();while(!settled){await new Promise(function(r){setTimeout(r,400);});if(!seen&&Date.now()-t0>20000){post(job,false,'chrome-google',[]);return;}}if(settled.err)throw settled.err;t=settled.ok;}else{t=await creating;}var outs=[],i,s;for(i=0;i<job.items.length;i++){s=job.items[i]||'';outs.push(s.trim()?await t.translate(s):'');}post(job,true,'',outs);}catch(e){var m=e&&e.message?e.message:'Chrome 翻译失败';if(m==='chrome-google'){post(job,false,m,[]);return;}post(job,false,m,[]);}}\n"
		+ "try{if(window.speechSynthesis)speechSynthesis.getVoices();}catch(e){}\n"
		+ "poll();\n"
		+ "</script>";

	delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
	[DllImport("user32.dll")] static extern bool EnumWindows(EnumProc lpEnumFunc, IntPtr lParam);
	[DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "InternalGetWindowText")] static extern int InternalGetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
	[DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
	[DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
	[DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
	[DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
	[DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
	[DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
	[DllImport("user32.dll")] static extern int GetSystemMetrics(int nIndex);
	[DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
	[DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
	[DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW", SetLastError = true)] static extern bool Process32First(IntPtr snap, ref PROCESSENTRY32 pe);
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW", SetLastError = true)] static extern bool Process32Next(IntPtr snap, ref PROCESSENTRY32 pe);
	[DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr handle);
	[DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
	[DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeProcess(IntPtr process, out uint code);

	[StructLayout(LayoutKind.Sequential)]
	struct RECT {
		public int Left, Top, Right, Bottom;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	struct MONITORINFO {
		public int cbSize;
		public RECT rcMonitor;
		public RECT rcWork;
		public uint dwFlags;
	}

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
}
