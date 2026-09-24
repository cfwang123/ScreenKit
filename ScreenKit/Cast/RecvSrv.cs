using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace ScreenKit;

sealed class CastRecvSrv : IDisposable {
	TcpListener lis;
	Thread th;
	volatile bool stop;
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
	readonly object nallock = new();
	readonly AutoResetEvent nalsig = new(false);
	byte[] pendingnal;
	readonly ConcurrentQueue<byte[]> aqueue = new();
	readonly AutoResetEvent asig = new(false);
	volatile bool decstop;
	int lastpkt;
	int encw, ench, srcw, srch, hellofps, hellobr;
	int videomiss;
	int fpsn, fpsv, audion, audiov, audiogot, audiogotv, kbps, rttms, laststat, lastping, lastframe, lastaudiolog, decms;
	int audiohex;
	int sessstart;
	bool hadhello;
	long byteacc;
	public bool Running => !stop && lis != null;
	public bool Busy => busy;

	public void Start() {
		if (lis != null) return;
		if (!FfmpegLoader.TryInit(out var err))
			throw new InvalidOperationException(err ?? "FFmpeg 未就绪");
		stop = false;
		lis = new TcpListener(IPAddress.Any, CastProto.TCP_PORT);
		lis.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
		lis.Start();
		th = new Thread(loop) { IsBackground = true, Name = "cast-recv" };
		th.Start();
		ThreadPool.QueueUserWorkItem(_ => CastNetUtil.TryFirewall());
		Log?.Invoke($"接收已启动 TCP {CastProto.TCP_PORT}");
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
		if (busy && now - lastping >= 1000) {
			lastping = now;
			SendJson(new { cmd = "ping", t = now });
		}
		if (busy && lastpkt != 0 && now - lastpkt > 15000)
			Kick();
		if (busy && !hadhello && sessstart != 0 && now - sessstart > 2500)
			Kick();
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
		var gap = lastframe == 0 ? 0 : Environment.TickCount - lastframe;
		var src = srcw > 0 && srch > 0 ? $"{srcw}x{srch}" : "—";
		var enc = encw > 0 && ench > 0 ? $"{encw}x{ench}" : "—";
		var delay = rttms > 0 ? $"{rttms} ms" : (gap > 0 ? $"间隔 {gap} ms" : "—");
		var br = kbps > 0 ? $"{kbps / 1000.0:0.0} Mbps" : (hellobr > 0 ? $"标称 {hellobr / 1000000.0:0.0} Mbps" : "—");
		var fps = hellofps > 0 ? $"{fpsv} fps (标称 {hellofps})" : $"{fpsv} fps";
		var au = audiogotv > 0 ? $"{audiov}/{audiogotv} pkt/s" : "无";
		return $"原始 {src}   编码 {enc}\n{fps}   {br}   音频 {au}   延时 {delay}   解码 {decms} ms";
	}

	public void Kick() {
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
		finally { if (hello) OnGone?.Invoke(); }
	}

	void loop() {
		while (!stop) {
			TcpClient cli = null;
			try { cli = lis.AcceptTcpClient(); }
			catch { if (stop) break; continue; }
			if (cli == null) continue;
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
			if (!haspayload(cli, 400)) {
				Log?.Invoke($"探测连接，忽略 {cli.Client?.RemoteEndPoint}");
				try { cli.Close(); } catch { }
				return;
			}
			Log?.Invoke("新连接，断开旧会话");
			Kick();
			var t0 = Environment.TickCount;
			while (busy && Environment.TickCount - t0 < 2000)
				Thread.Sleep(20);
		}
		var hello = false;
		curcli = cli;
		try { hello = runsession(cli.GetStream()); }
		catch (Exception ex) { Log?.Invoke($"会话结束: {ex.Message}"); }
		finally {
			if (ReferenceEquals(curcli, cli)) curcli = null;
			try { cli.Close(); } catch { }
			if (hello) OnGone?.Invoke();
		}
	}

	static bool haspayload(TcpClient cli, int ms) {
		try {
			var t0 = Environment.TickCount;
			while (Environment.TickCount - t0 < ms) {
				try {
					if (cli.Available > 0) return true;
					if (cli.Client == null || !cli.Connected) return false;
					if (cli.Client.Poll(0, SelectMode.SelectRead) && cli.Available == 0)
						return false;
				}
				catch { return false; }
				Thread.Sleep(20);
			}
			return cli.Available > 0;
		}
		catch { return false; }
	}

	bool runsession(Stream s) {
		var hello = false;
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
					if (type == CastProto.T_JSON) {
						if (dojson(payload)) { hello = true; hadhello = true; }
					}
					else if (type == CastProto.T_VIDEO) {
						if (!hello) {
							hello = true;
							hadhello = true;
							OnHello?.Invoke("投屏", "");
						}
						if (payload != null) byteacc += payload.Length;
						lock (nallock) pendingnal = payload;
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
				decstop = true;
				nalsig.Set();
				asig.Set();
				try { decth.Join(800); } catch { }
				try { ath.Join(400); } catch { }
				while (aqueue.TryDequeue(out _)) { }
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
			return true;
		}
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
		encw = w;
		ench = h;
		var via = CastProto.Jstr(o, "via");
		Log?.Invoke($"握手 {via} {n} {w}x{h}");
		resetvdec();
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

	void decodeloop() {
		while (!decstop && !stop && !drop) {
			nalsig.WaitOne(200);
			byte[] nal;
			lock (nallock) {
				nal = pendingnal;
				pendingnal = null;
			}
			if (nal != null) dovideo(nal);
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
		Kick();
		try { lis?.Stop(); } catch { }
		lis = null;
		resetdec();
		busy = false;
	}

	public void Dispose() => Stop();
}
