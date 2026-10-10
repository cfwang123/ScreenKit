using System.Drawing;
using System.Net.WebSockets;

namespace ScreenKit;

sealed class CastSendCli : IDisposable {
	ClientWebSocket ws;
	Stream st;
	Thread th;
	volatile bool stop;
	CastScreenGrab grab;
	CastVideoEncoder venc;
	CastAudioEncoder aenc;
	CastAudioCap acap;
	CastQuality q;
	bool wantAudio;
	RecordAudioMode audmode;
	volatile bool paused;
	int interval;
	readonly object encs = new();
	readonly object grablk = new();
	readonly object nslock = new();
	readonly object audlk = new();
	public Action<string> Log;
	public bool Running => !stop && st != null;
	public bool Paused => paused;

	public void Start(string ip, int port, CastQuality quality, bool audio) =>
		Start(ip, port, quality, audio, Rectangle.Empty, RecordAudioMode.Speakers);

	public void Start(string ip, int port, CastQuality quality, bool audio, Rectangle region) =>
		Start(ip, port, quality, audio, region, RecordAudioMode.Speakers);

	public void Start(string ip, int port, CastQuality quality, bool audio, Rectangle region, RecordAudioMode src) {
		if (st != null) return;
		if (!FfmpegLoader.TryInit(out var err))
			throw new InvalidOperationException(err ?? "FFmpeg 未就绪");
		q = quality ?? CastQuality.Presets[1];
		wantAudio = audio;
		audmode = src == RecordAudioMode.Off ? RecordAudioMode.Speakers : src;
		paused = false;
		stop = false;
		interval = Math.Max(8, 1000 / q.Fps);
		if (port <= 0) port = 1224;
		ws = new ClientWebSocket();
		var uri = new Uri($"ws://{ip}:{port}{CastProto.WS_PATH}");
		ws.ConnectAsync(uri, CancellationToken.None).GetAwaiter().GetResult();
		st = new CastWsStream(ws);
		grab = region.Width >= 16 && region.Height >= 16
			? new CastScreenGrab(region) : new CastScreenGrab();
		venc = new CastVideoEncoder(grab.Width, grab.Height, q);
		sendjson();
		if (!waithello()) {
			Stop();
			throw new InvalidOperationException("对方未应答 hello");
		}
		th = new Thread(loop) { IsBackground = true, Name = "cast-send" };
		th.Start();
		Log?.Invoke($"已连接到 {uri} 编码 {venc.OutWidth}x{venc.OutHeight}@{q.Fps}");
	}

	public void SetRegion(Rectangle region) {
		if (grab == null || st == null) return;
		if (region.Width % 2 != 0) region.Width--;
		if (region.Height % 2 != 0) region.Height--;
		if (region.Width < 16 || region.Height < 16) return;
		var moved = false;
		lock (grablk) {
			if (grab == null) return;
			if (grab.Width == region.Width && grab.Height == region.Height) {
				grab.Move(region.X, region.Y);
				moved = true;
			}
			else {
				var ng = new CastScreenGrab(region);
				CastVideoEncoder nv;
				try { nv = new CastVideoEncoder(ng.Width, ng.Height, q); }
				catch {
					ng.Dispose();
					return;
				}
				var old = grab;
				grab = ng;
				lock (encs) {
					venc?.Dispose();
					venc = nv;
				}
				old.Dispose();
			}
		}
		if (!moved) sendjson();
	}

	public void SetPaused(bool on) {
		paused = on;
		if (!on) return;
		lock (audlk) {
			try { acap?.Clear(); } catch { }
		}
	}

	public void ApplyQ(CastQuality nq) {
		if (nq == null || grab == null || st == null) return;
		q = nq;
		interval = Math.Max(8, 1000 / q.Fps);
		try {
			var nv = new CastVideoEncoder(grab.Width, grab.Height, q);
			lock (encs) {
				venc?.Dispose();
				venc = nv;
			}
			sendjson();
			Log?.Invoke($"画质已改为 {q.Name} {venc.OutWidth}x{venc.OutHeight}@{q.Fps}");
		}
		catch (Exception ex) { Log?.Invoke($"改画质失败: {ex.Message}"); }
	}

	void sendjson() {
		var pkt = CastProto.PackJson(new {
			cmd = "hello",
			name = CastHost.Name,
			w = venc.OutWidth,
			h = venc.OutHeight,
			fps = q.Fps,
			br = q.Bitrate,
			audio = wantAudio,
			pix = "h264",
			aud = "aac",
			via = "wifi",
		});
		lock (nslock) st.Write(pkt, 0, pkt.Length);
	}

	bool waithello() {
		var ok = false;
		var th = new Thread(() => {
			try {
				if (!CastProto.TryRead(st, out var type, out var payload)) return;
				if (type != CastProto.T_JSON) return;
				ok = CastProto.Jstr(CastProto.ParseJson(payload), "cmd") == "hello";
			}
			catch { }
		}) { IsBackground = true, Name = "cast-hello" };
		th.Start();
		if (!th.Join(5000)) return false;
		return ok;
	}

	public void SetAudio(bool audio, RecordAudioMode src) {
		if (st == null || stop) return;
		lock (audlk) {
			wantAudio = audio;
			if (src != RecordAudioMode.Off) audmode = src;
			openaudio();
		}
	}

	void openaudio() {
		closeaudio();
		if (!wantAudio) return;
		try {
			acap = new CastAudioCap(audmode);
			if (acap.FellBack) Log?.Invoke("麦克风不可用，仅发送扬声器");
			aenc = new CastAudioEncoder(acap.SampleRate, acap.Channels);
		}
		catch (Exception ex) {
			closeaudio();
			wantAudio = false;
			Log?.Invoke($"声音采集失败，仅画面: {ex.Message}");
		}
	}

	void closeaudio() {
		try { aenc?.Dispose(); } catch { }
		aenc = null;
		try { acap?.Dispose(); } catch { }
		acap = null;
	}

	void loop() {
		byte[] bgra = null;
		var next = Environment.TickCount;
		var apcm = new byte[48000];
		try {
			lock (audlk) {
				if (wantAudio && acap == null) openaudio();
			}
			while (!stop) {
				if (paused) {
					Thread.Sleep(30);
					next = Environment.TickCount;
					lock (audlk) {
						try { acap?.Clear(); } catch { }
					}
					continue;
				}
				var now = Environment.TickCount;
				if (now - next < 0) { Thread.Sleep(1); continue; }
				next = now + interval;
				byte[] nal = null;
				int stride;
				lock (grablk) {
					if (grab == null) continue;
					stride = grab.Width * 4;
					var need = stride * grab.Height;
					if (bgra == null || bgra.Length < need) bgra = new byte[need];
					if (!grab.Grab(bgra, stride)) continue;
					lock (encs) nal = venc?.EncodeBgra(bgra, stride);
				}
				if (nal != null) {
					var pkt = CastProto.Pack(CastProto.T_VIDEO, nal);
					lock (nslock) st.Write(pkt, 0, pkt.Length);
				}
				byte[] adts = null;
				lock (audlk) {
					if (wantAudio && acap != null && aenc != null) {
						var n = acap.Take(apcm);
						if (n > 0) adts = aenc.EncodeS16(apcm, n);
					}
				}
				if (adts != null) {
					var pkt = CastProto.Pack(CastProto.T_AUDIO, adts);
					lock (nslock) st.Write(pkt, 0, pkt.Length);
				}
			}
		}
		catch (Exception ex) {
			if (!stop) Log?.Invoke($"发送中断: {ex.Message}");
			stop = true;
			try { st?.Close(); } catch { }
		}
	}

	public void Stop() {
		stop = true;
		try { st?.Close(); } catch { }
		try { ws?.Abort(); } catch { }
		st = null;
		ws = null;
		venc?.Dispose(); venc = null;
		lock (audlk) closeaudio();
		grab?.Dispose(); grab = null;
	}

	public void Dispose() => Stop();
}
