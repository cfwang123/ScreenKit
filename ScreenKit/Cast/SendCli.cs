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
	int interval;
	readonly object encs = new();
	readonly object nslock = new();
	public Action<string> Log;
	public bool Running => !stop && st != null;

	public void Start(string ip, int port, CastQuality quality, bool audio) {
		if (st != null) return;
		if (!FfmpegLoader.TryInit(out var err))
			throw new InvalidOperationException(err ?? "FFmpeg 未就绪");
		q = quality ?? CastQuality.Presets[1];
		wantAudio = audio;
		stop = false;
		interval = Math.Max(8, 1000 / q.Fps);
		if (port <= 0) port = 1224;
		ws = new ClientWebSocket();
		var uri = new Uri($"ws://{ip}:{port}{CastProto.WS_PATH}");
		ws.ConnectAsync(uri, CancellationToken.None).GetAwaiter().GetResult();
		st = new CastWsStream(ws);
		grab = new CastScreenGrab();
		venc = new CastVideoEncoder(grab.Width, grab.Height, q);
		sendjson();
		th = new Thread(loop) { IsBackground = true, Name = "cast-send" };
		th.Start();
		Log?.Invoke($"已连接到 {uri} 编码 {venc.OutWidth}x{venc.OutHeight}@{q.Fps}");
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

	void loop() {
		var stride = grab.Width * 4;
		var bgra = new byte[stride * grab.Height];
		var next = Environment.TickCount;
		byte[] apcm = wantAudio ? new byte[48000] : null;
		try {
			if (wantAudio) {
				try { acap = new CastAudioCap(); aenc = new CastAudioEncoder(acap.SampleRate, acap.Channels); }
				catch (Exception ex) {
					Log?.Invoke($"系统声音采集失败，仅画面: {ex.Message}");
					wantAudio = false;
				}
			}
			while (!stop) {
				var now = Environment.TickCount;
				if (now - next < 0) { Thread.Sleep(1); continue; }
				next = now + interval;
				if (!grab.Grab(bgra, stride)) continue;
				byte[] nal = null;
				lock (encs) nal = venc?.EncodeBgra(bgra, stride);
				if (nal != null) {
					var pkt = CastProto.Pack(CastProto.T_VIDEO, nal);
					lock (nslock) st.Write(pkt, 0, pkt.Length);
				}
				if (wantAudio && acap != null) {
					var n = acap.Take(apcm);
					if (n > 0) {
						var adts = aenc.EncodeS16(apcm, n);
						if (adts != null) {
							var pkt = CastProto.Pack(CastProto.T_AUDIO, adts);
							lock (nslock) st.Write(pkt, 0, pkt.Length);
						}
					}
				}
			}
		}
		catch (Exception ex) {
			if (!stop) Log?.Invoke($"发送中断: {ex.Message}");
		}
	}

	public void Stop() {
		stop = true;
		try { st?.Close(); } catch { }
		try { ws?.Abort(); } catch { }
		st = null;
		ws = null;
		venc?.Dispose(); venc = null;
		aenc?.Dispose(); aenc = null;
		acap?.Dispose(); acap = null;
		grab?.Dispose(); grab = null;
	}

	public void Dispose() => Stop();
}
