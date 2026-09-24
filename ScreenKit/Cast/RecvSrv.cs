using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace ScreenKit;

sealed class CastRecvSrv : IDisposable {
	readonly List<TcpListener> listeners = new();
	readonly HashSet<string> bound = new();
	readonly object bindlock = new();
	volatile bool stop;
	volatile bool started;
	int lastbind;
	volatile bool busy;
	volatile bool drop;
	TcpClient curcli;
	Stream curst;
	readonly object sess = new();
	readonly object wlock = new();
	public Action<string> Log;
	public Action<byte[], int, int, int> OnFrame;
	public Action<string, string> OnHello;
	public Action<int, int> OnSrc;
	public Action OnGone;
	CastVideoDecoder vdec;
	CastAudioDecoder adec;
	CastAudioPlay aplay;
	readonly object declock = new();
	readonly AutoResetEvent nalsig = new(false);
	readonly ConcurrentQueue<byte[]> vqueue = new();
	int vqlen;
	const int VQMAX = 48;
	readonly ConcurrentQueue<byte[]> aqueue = new();
	readonly AutoResetEvent asig = new(false);
	volatile bool decstop;
	int lastpkt;
	int encw, ench, srcw, srch, hellofps, hellobr;
	string hellovia;
	int videomiss;
	int fpsn, fpsv, audion, audiov, audiogot, audiogotv, kbps, rttms, laststat, lastping, lastframe, lastaudiolog, decms;
	int audiohex;
	int sessstart;
	bool hadhello;
	long byteacc;
	bool pipeon;
	int listenport;
	volatile int acceptn;
	public bool Running => !stop && started;
	public bool Busy => busy;
	public int ListenPort => listenport;
	public bool TcpOk { get; private set; }
	public string BindText { get; private set; } = "";

	public void Start() {
		lock (bindlock) {
			if (started) {
				ensuretcp();
				return;
			}
			if (!FfmpegLoader.TryInit(out var err))
				throw new InvalidOperationException(err ?? "FFmpeg 未就绪");
			stop = false;
			startpipe();
			started = true;
			ensuretcp();
		}
		ThreadPool.QueueUserWorkItem(_ => CastNetUtil.TryFirewall());
		Log?.Invoke($"接收已启动 {BindText} USB管道");
	}

	void spawn(TcpListener lis) {
		new Thread(() => loop(lis)) { IsBackground = true, Name = "cast-recv" }.Start();
	}

	void startpipe() {
		if (pipeon) return;
		pipeon = true;
		new Thread(pipeloop) { IsBackground = true, Name = "cast-pipe" }.Start();
	}

	void pipeloop() {
		Log?.Invoke("USB 配件管道已就绪");
		while (!stop) {
			NamedPipeServerStream up = null;
			NamedPipeServerStream down = null;
			try {
				up = new NamedPipeServerStream(
					CastProto.USB_PIPE, PipeDirection.In, 2,
					PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
				down = new NamedPipeServerStream(
					CastProto.USB_PIPE_DOWN, PipeDirection.Out, 2,
					PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
				up.WaitForConnection();
				if (stop) break;
				down.WaitForConnection();
				if (stop) break;
				Log?.Invoke("USB 管道接入");
				var duo = new CastDuplexStream(up, down);
				up = null;
				down = null;
				var hello = runsession(duo);
				try { duo.Dispose(); } catch { }
			}
			catch (Exception ex) {
				if (!stop) Log?.Invoke($"USB 管道: {ex.Message}");
			}
			finally {
				try { up?.Dispose(); } catch { }
				try { down?.Dispose(); } catch { }
			}
		}
	}

	void ensuretcp() {
		if (stop) return;
		if (TcpOk && listeners.Count > 0) return;
		stoplisten();
		for (var i = 0; i < 16; i++) {
			var port = CastProto.TCP_PORT + i;
			freeport(port);
			Thread.Sleep(i == 0 ? 80 : 20);
			if (!trybind(IPAddress.Any, port)) continue;
			listenport = port;
			foreach (var l in listeners.ToArray())
				spawn(l);
			if (selfprobe(port)) {
				TcpOk = true;
				BindText = port == CastProto.TCP_PORT
					? $"TCP {port}"
					: $"TCP {port}（{CastProto.TCP_PORT} 被占）";
				Log?.Invoke($"已独占监听 0.0.0.0:{port}");
				return;
			}
			Log?.Invoke($"TCP {port} 已 bind 但本进程收不到连接（幽灵占用），换口");
			stoplisten();
		}
		listenport = 0;
		TcpOk = false;
		BindText = $"TCP {CastProto.TCP_PORT} 未真正监听";
		Log?.Invoke($"{BindText}，WiFi/ADB 不能弹窗；USB 配件仍走命名管道");
	}

	bool trybind(IPAddress addr, int port) {
		var key = $"{addr}:{port}";
		if (bound.Contains(key)) return true;
		TcpListener l = null;
		try {
			l = new TcpListener(addr, port);
			l.Server.ExclusiveAddressUse = true;
			l.Start();
			bound.Add(key);
			listeners.Add(l);
			return true;
		}
		catch (Exception ex) {
			Log?.Invoke($"bind {addr}:{port} exclusive: {ex.Message}");
			try { l?.Stop(); } catch { }
			return false;
		}
	}

	bool selfprobe(int port) {
		var n0 = acceptn;
		try {
			using var c = new TcpClient();
			var ar = c.BeginConnect(IPAddress.Loopback, port, null, null);
			if (!ar.AsyncWaitHandle.WaitOne(800)) {
				try { c.Close(); } catch { }
				return false;
			}
			c.EndConnect(ar);
			var t0 = Environment.TickCount;
			while (acceptn == n0 && unchecked(Environment.TickCount - t0) < 800)
				Thread.Sleep(20);
			try { c.Close(); } catch { }
			return acceptn > n0;
		}
		catch (Exception ex) {
			Log?.Invoke($"selfprobe {port}: {ex.Message}");
			return false;
		}
	}

	void stoplisten() {
		foreach (var l in listeners) {
			try { l.Stop(); } catch { }
		}
		listeners.Clear();
		bound.Clear();
		TcpOk = false;
		listenport = 0;
	}

	void freeport(int port) {
		var self = Process.GetCurrentProcess().Id;
		try {
			var psi = new ProcessStartInfo {
				FileName = "netstat",
				Arguments = "-ano",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				CreateNoWindow = true,
			};
			var n = Process.Start(psi);
			if (n == null) return;
			var o = n.StandardOutput.ReadToEnd();
			if (!n.WaitForExit(4000)) try { n.Kill(); } catch { }
			var pids = new HashSet<int>();
			var needle = $":{port}";
			foreach (var line in o.Split('\n')) {
				if (line.IndexOf(needle, StringComparison.Ordinal) < 0) continue;
				if (line.IndexOf("LISTENING", StringComparison.OrdinalIgnoreCase) < 0
					&& line.IndexOf("侦听", StringComparison.Ordinal) < 0)
					continue;
				var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length == 0) continue;
				if (!int.TryParse(parts[parts.Length - 1], out var pid) || pid <= 4 || pid == self)
					continue;
				pids.Add(pid);
			}
			foreach (var pid in pids) {
				var alive = false;
				try {
					var pr = Process.GetProcessById(pid);
					alive = true;
					Log?.Invoke($"TCP {port} 被 pid={pid} {pr.ProcessName} 占用，结束该进程");
					pr.Kill();
				}
				catch {
					Log?.Invoke($"TCP {port} 被已退出 pid={pid} 占着（幽灵套接字），换口");
				}
				if (!alive) continue;
				try {
					var tk = new ProcessStartInfo {
						FileName = "taskkill",
						Arguments = $"/F /PID {pid}",
						UseShellExecute = false,
						CreateNoWindow = true,
						RedirectStandardOutput = true,
						RedirectStandardError = true,
					};
					var p = Process.Start(tk);
					p?.WaitForExit(2000);
				}
				catch { }
			}
		}
		catch (Exception ex) { Log?.Invoke($"freeport: {ex.Message}"); }
	}

	public void Tick() {
		var now = Environment.TickCount;
		if (now - laststat >= 1000 || laststat == 0) {
			fpsv = fpsn;
			fpsn = 0;
			audiov = audion;
			audion = 0;
			audiogotv = audiogot;
			audiogot = 0;
			kbps = (int)(byteacc * 8 / 1000);
			byteacc = 0;
			laststat = now;
			if (busy) writestat();
			if (busy && (audiov > 0 || audiogotv > 0) && now - lastaudiolog >= 2000) {
				lastaudiolog = now;
				Log?.Invoke($"音频 {audiov}/{audiogotv} pkt/s");
			}
		}
		if (busy && lastframe != 0 && now - lastping >= 1000) {
			lastping = now;
			SendJson(new { cmd = "ping", t = now });
		}
		if (busy && lastframe != 0 && lastpkt != 0 && now - lastpkt > 2500)
			Kick("2.5s 无包");
		if (busy && hadhello && lastframe == 0 && lastpkt != 0 && now - lastpkt > 8000)
			Kick("无视频");
		if (busy && !hadhello && sessstart != 0 && now - sessstart > 8000)
			Kick("握手超时");
		if (now - lastbind >= 3000 || lastbind == 0) {
			lastbind = now;
			lock (bindlock) ensuretcp();
		}
	}

	public void SendJson(object obj) {
		var s = curst;
		if (s == null || obj == null) return;
		try {
			var buf = CastProto.PackJson(obj);
			lock (wlock) {
				if (!ReferenceEquals(curst, s)) return;
				s.Write(buf, 0, buf.Length);
				s.Flush();
			}
		}
		catch { }
	}

	public string StatText() {
		if (!busy) return Loc.T("cast.stat.idle");
		var kind = CastHost.ViaTag(hellovia);
		var gap = lastframe == 0 ? 0 : Environment.TickCount - lastframe;
		var src = srcw > 0 && srch > 0 ? $"{srcw}x{srch}" : "—";
		var enc = encw > 0 && ench > 0 ? $"{encw}x{ench}" : "—";
		var delay = rttms > 0 ? $"{rttms} ms" : (gap > 0 ? $"间隔 {gap} ms" : "—");
		var br = kbps > 0 ? $"{kbps / 1000.0:0.0} Mbps" : (hellobr > 0 ? $"标称 {hellobr / 1000000.0:0.0} Mbps" : "—");
		var fps = hellofps > 0 ? $"{fpsv} fps (标称 {hellofps})" : $"{fpsv} fps";
		var au = audiogotv > 0 ? $"{audiov}/{audiogotv} pkt/s" : "无";
		return $"{Loc.T("cast.stat.via")} {kind}\n原始 {src}   编码 {enc}\n{fps}   {br}   音频 {au}   延时 {delay}   解码 {decms} ms";
	}

	public void Kick(string why = null) {
		if (!string.IsNullOrEmpty(why) && busy) Log?.Invoke($"断开 {why}");
		drop = true;
		try { curst?.Close(); } catch { }
		try { curcli?.Close(); } catch { }
	}

	public void AttachStream(Stream s, string tag) {
		if (s == null) return;
		if (busy) {
			Log?.Invoke($"{tag} 已有会话，忽略");
			return;
		}
		Log?.Invoke($"{tag} 接入");
		var hello = false;
		try { hello = runsession(s); }
		catch (Exception ex) { Log?.Invoke($"{tag} 结束: {ex.Message}"); }
	}

	void loop(TcpListener lis) {
		while (!stop) {
			TcpClient cli = null;
			try { cli = lis.AcceptTcpClient(); }
			catch { break; }
			if (cli == null) continue;
			Interlocked.Increment(ref acceptn);
			tune(cli);
			var ep = cli.Client.RemoteEndPoint;
			Log?.Invoke($"接入 {ep}");
			var c = cli;
			new Thread(() => onesess(c)) { IsBackground = true, Name = "cast-sess" }.Start();
		}
	}

	static void tune(TcpClient cli) {
		try {
			cli.NoDelay = true;
			cli.ReceiveBufferSize = 4 * 1024 * 1024;
			cli.SendBufferSize = 512 * 1024;
		}
		catch { }
	}

	void onesess(TcpClient cli) {
		if (busy) {
			Log?.Invoke("新连接，断开旧会话");
			Kick("新连接");
			var t0 = Environment.TickCount;
			while (busy && Environment.TickCount - t0 < 2000)
				Thread.Sleep(20);
		}
		var hello = false;
		curcli = cli;
		try {
			try { cli.ReceiveTimeout = 20000; } catch { }
			hello = runsession(cli.GetStream());
		}
		catch (Exception ex) { Log?.Invoke($"会话结束: {ex.Message}"); }
		finally {
			if (ReferenceEquals(curcli, cli)) curcli = null;
			try { cli.Close(); } catch { }
		}
	}

	bool runsession(Stream s) {
		var hello = false;
		var vlog = false;
		var firstpkt = false;
		lock (sess) {
			if (busy) return false;
			busy = true;
			hadhello = false;
			sessstart = Environment.TickCount;
			drop = false;
			decstop = false;
			curst = s;
			var decth = new Thread(decodeloop) { IsBackground = true, Name = "cast-vdec" };
			var ath = new Thread(audioloop) { IsBackground = true, Name = "cast-adec" };
			decth.Start();
			ath.Start();
			try {
				resetdec();
				lastpkt = Environment.TickCount;
				while (!stop && !drop) {
					if (!CastProto.TryRead(s, out var type, out var payload)) {
						Log?.Invoke("对端关闭或读包失败");
						break;
					}
					lastpkt = Environment.TickCount;
					if (!firstpkt) {
						firstpkt = true;
						Log?.Invoke($"首包 type={type} {payload?.Length ?? 0}B");
					}
					if (type == CastProto.T_JSON) {
						if (dojson(payload)) {
							hello = true;
							hadhello = true;
						}
					}
					else if (type == CastProto.T_VIDEO) {
						if (!hello) {
							hello = true;
							hadhello = true;
							OnHello?.Invoke("投屏", "");
						}
						if (!vlog) {
							vlog = true;
							Log?.Invoke($"首个视频包 {payload?.Length ?? 0}B");
						}
						if (payload != null) byteacc += payload.Length;
						enqueuev(payload);
						nalsig.Set();
					}
					else if (type == CastProto.T_AUDIO) {
						if (!hello) {
							hello = true;
							hadhello = true;
							OnHello?.Invoke("投屏", "");
						}
						if (payload != null) byteacc += payload.Length;
						audiogot++;
						aqueue.Enqueue(payload);
						asig.Set();
					}
				}
			}
			finally {
				if (hello) OnGone?.Invoke();
				decstop = true;
				nalsig.Set();
				asig.Set();
				try { decth.Join(800); } catch { }
				try { ath.Join(400); } catch { }
				while (aqueue.TryDequeue(out _)) { }
				drainv();
				if (ReferenceEquals(curst, s)) curst = null;
				resetdec();
				busy = false;
				hadhello = false;
				sessstart = 0;
				lastpkt = 0;
				fpsn = 0;
				fpsv = 0;
				audion = 0;
				audiov = 0;
				audiogot = 0;
				audiogotv = 0;
				audiohex = 0;
				kbps = 0;
				rttms = 0;
				byteacc = 0;
				lastframe = 0;
				videomiss = 0;
				hellovia = null;
			}
		}
		return hello;
	}

	bool dojson(byte[] payload) {
		JsonObject o = null;
		try { o = CastProto.ParseJson(payload); }
		catch { return false; }
		if (o == null) return false;
		var cmd = CastProto.Jstr(o, "cmd");
		if (cmd == "bye") {
			drop = true;
			Kick("对端停止");
			return true;
		}
		if (cmd == "probe") return false;
		if (cmd == "pong") {
			var t = CastProto.Jint(o, "t");
			if (t != 0) rttms = Environment.TickCount - t;
			if (rttms < 0) rttms = 0;
			return false;
		}
		if (cmd == "orient") {
			var dw = CastProto.Jint(o, "dw");
			var dh = CastProto.Jint(o, "dh");
			if (dw > 0 && dh > 0) {
				srcw = dw;
				srch = dh;
				OnSrc?.Invoke(dw, dh);
			}
			return false;
		}
		if (cmd != "hello") {
			Log?.Invoke($"json {cmd}");
			return false;
		}
		var n = CastProto.Jstr(o, "name");
		if (string.IsNullOrEmpty(n)) n = "发送端";
		var w = CastProto.Jint(o, "w");
		var h = CastProto.Jint(o, "h");
		var dw0 = CastProto.Jint(o, "dw");
		var dh0 = CastProto.Jint(o, "dh");
		hellofps = CastProto.Jint(o, "fps");
		hellobr = CastProto.Jint(o, "br");
		var oldw = encw;
		var oldh = ench;
		encw = w;
		ench = h;
		var via = CastProto.Jstr(o, "via");
		hellovia = via;
		Log?.Invoke($"握手 {via} {n} {w}x{h}");
		if (w <= 0 || h <= 0 || w != oldw || h != oldh) resetvdec();
		OnHello?.Invoke(n, via);
		if (dw0 > 0 && dh0 > 0) {
			srcw = dw0;
			srch = dh0;
			OnSrc?.Invoke(dw0, dh0);
		}
		else if (w > 0 && h > 0) {
			srcw = w;
			srch = h;
			OnSrc?.Invoke(w, h);
		}
		return true;
	}

	void audioloop() {
		while (!decstop && !stop && !drop) {
			asig.WaitOne(200);
			while (aqueue.TryDequeue(out var data))
				doaudio(data);
		}
	}

	void enqueuev(byte[] nal) {
		if (nal == null || nal.Length == 0) return;
		if (vqlen >= VQMAX) {
			drainv();
			if (!keynal(nal)) return;
		}
		vqueue.Enqueue(nal);
		Interlocked.Increment(ref vqlen);
	}

	void drainv() {
		while (vqueue.TryDequeue(out _)) { }
		vqlen = 0;
	}

	static bool keynal(byte[] n) {
		if (n == null || n.Length < 5) return false;
		for (var i = 0; i < n.Length - 4; i++) {
			int t;
			if (n[i] == 0 && n[i + 1] == 0 && n[i + 2] == 1)
				t = n[i + 3] & 0x1f;
			else if (n[i] == 0 && n[i + 1] == 0 && n[i + 2] == 0 && n[i + 3] == 1)
				t = n[i + 4] & 0x1f;
			else continue;
			if (t == 5 || t == 7) return true;
		}
		return false;
	}

	void decodeloop() {
		while (!decstop && !stop && !drop) {
			nalsig.WaitOne(200);
			while (vqueue.TryDequeue(out var nal)) {
				Interlocked.Decrement(ref vqlen);
				if (nal != null) dovideo(nal);
			}
		}
	}

	void dovideo(byte[] nal) {
		try {
			lock (declock) {
				if (vdec == null) vdec = new CastVideoDecoder();
				var t0 = Environment.TickCount;
				if (vdec.Decode(nal, out var px, out var w, out var h, out var st)) {
					encw = w;
					ench = h;
					fpsn++;
					videomiss = 0;
					lastframe = Environment.TickCount;
					decms = lastframe - t0;
					OnFrame?.Invoke(px, w, h, st);
				}
				else {
					videomiss++;
					if (videomiss == 24 || videomiss % 120 == 0)
						Log?.Invoke($"视频包 {videomiss} 个未解出帧");
				}
			}
		}
		catch (Exception ex) { Log?.Invoke($"视频解码: {ex.Message}"); }
	}

	void doaudio(byte[] data) {
		try {
			if (data == null || data.Length == 0) return;
			if (audiohex < 4) {
				audiohex++;
				var n = Math.Min(12, data.Length);
				var hex = BitConverter.ToString(data, 0, n);
				Log?.Invoke($"音频包 #{audiohex} {data.Length}B {hex}");
				try {
					var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
					Directory.CreateDirectory(dir);
					File.AppendAllText(Path.Combine(dir, "cast_audio.txt"),
						$"{DateTime.Now:HH:mm:ss} #{audiohex} {data.Length}B {hex}\n");
				}
				catch { }
			}
			if (data.Length < 2 || data[0] != 0xFF || (data[1] & 0xF0) != 0xF0) return;
			byteacc += data.Length;
			if (adec == null) adec = new CastAudioDecoder();
			var pcm = adec.DecodeAdts(data);
			if (pcm == null) return;
			audion++;
			if (aplay == null) aplay = new CastAudioPlay(adec.SampleRate, adec.Channels);
			aplay.Push(pcm);
		}
		catch (Exception ex) { Log?.Invoke($"音频解码: {ex.Message}"); }
	}

	void writestat() {
		try {
			var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
			Directory.CreateDirectory(dir);
			var gap = lastpkt == 0 ? -1 : Environment.TickCount - lastpkt;
			File.WriteAllText(Path.Combine(dir, "cast.log"),
				$"{DateTime.Now:HH:mm:ss} fps={fpsv} audio={audiov}/{audiogotv} kbps={kbps} busy={(busy ? 1 : 0)} gap={gap}\n");
		}
		catch { }
	}

	void resetvdec() {
		lock (declock) {
			vdec?.Dispose(); vdec = null;
		}
	}

	void resetdec() {
		lock (declock) {
			vdec?.Dispose(); vdec = null;
			adec?.Dispose(); adec = null;
			aplay?.Dispose(); aplay = null;
		}
	}

	public void Stop() {
		stop = true;
		started = false;
		Kick();
		try {
			using var wake = new NamedPipeClientStream(".", CastProto.USB_PIPE, PipeDirection.Out);
			wake.Connect(200);
		}
		catch { }
		try {
			using var wake2 = new NamedPipeClientStream(".", CastProto.USB_PIPE_DOWN, PipeDirection.In);
			wake2.Connect(200);
		}
		catch { }
		lock (bindlock) {
			stoplisten();
		}
		pipeon = false;
		resetdec();
		busy = false;
	}

	public void Dispose() => Stop();
}
