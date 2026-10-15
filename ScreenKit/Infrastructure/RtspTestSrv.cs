using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace ScreenKit;

/// <summary>
/// 测试用 RTSP 服务。TCP 交织，多客户端。Push 送入带起始码的 H.264，再打成 RTP。
/// </summary>
sealed class RtspTestSrv : IDisposable {
	static readonly Random rand = new();
	readonly bool loopback;
	readonly ConcurrentDictionary<Guid, Session> sessions = new();
	TcpListener listener;
	CancellationTokenSource cts;
	Thread thread;
	byte[] sps;
	byte[] pps;
	volatile bool running;

	public RtspTestSrv(int port, bool loopback = false) {
		Port = port;
		this.loopback = loopback;
	}

	public int Port { get; private set; }
	public bool Audio { get; set; }
	public bool Running => running;
	public int ClientCount => sessions.Count;
	public bool Playing => sessions.Values.Any(s => s.Playing);
	public event Action<string> Log;
	public event Action ClientPlaying;

	public void SetCodec(byte[] spsNal, byte[] ppsNal) {
		if (spsNal != null) sps = spsNal;
		if (ppsNal != null) pps = ppsNal;
	}

	public void Start() {
		if (running) return;
		cts = new CancellationTokenSource();
		listener = new TcpListener(loopback ? IPAddress.Loopback : IPAddress.Any, Port);
		listener.Start();
		Port = ((IPEndPoint)listener.LocalEndpoint).Port;
		running = true;
		say("RTSP 测试服务已开，端口 " + Port);
		if (!loopback) {
			say("本机 rtsp://127.0.0.1:" + Port + "/live");
			foreach (var ip in lan())
				say("局域网 rtsp://" + ip + ":" + Port + "/live");
		}
		thread = new Thread(accept) { IsBackground = true, Name = "rtsp-srv" };
		thread.Start();
	}

	public void Push(byte[] annexb) {
		if (annexb == null || annexb.Length == 0) return;
		foreach (var s in sessions.Values) s.Enqueue(annexb);
	}

	public void Stop() {
		if (!running && listener == null) return;
		running = false;
		try { cts?.Cancel(); } catch { }
		foreach (var s in sessions.Values) s.Close();
		sessions.Clear();
		try { listener?.Stop(); } catch { }
		listener = null;
		try { thread?.Join(800); } catch { }
		say("RTSP 测试服务已停");
	}

	public void Dispose() => Stop();

	public static int Test(Action<string> log, Action<string> err) {
		RtspTestSrv srv = null;
		TcpClient c = null;
		try {
			srv = new RtspTestSrv(0, loopback: true);
			srv.Audio = true;
			srv.Start();
			c = new TcpClient();
			if (!c.ConnectAsync(IPAddress.Loopback, srv.Port).Wait(2000)) {
				err("rtspsrv: 连不上");
				return 1;
			}
			var ns = c.GetStream();
			var req = Encoding.ASCII.GetBytes("OPTIONS rtsp://127.0.0.1:" + srv.Port + "/live RTSP/1.0\r\nCSeq: 1\r\n\r\n");
			ns.Write(req, 0, req.Length);
			var buf = new byte[1024];
			var acc = new StringBuilder();
			var t0 = Environment.TickCount;
			while (Environment.TickCount - t0 < 2000 && acc.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal) < 0) {
				if (!ns.DataAvailable) { Thread.Sleep(10); continue; }
				var n = ns.Read(buf, 0, buf.Length);
				if (n <= 0) break;
				acc.Append(Encoding.ASCII.GetString(buf, 0, n));
			}
			var text = acc.ToString();
			if (text.IndexOf("RTSP/1.0 200", StringComparison.Ordinal) < 0 || text.IndexOf("DESCRIBE", StringComparison.Ordinal) < 0) {
				err("rtspsrv: OPTIONS 应答不对");
				return 1;
			}
			log("rtspsrv: OPTIONS 200");
			var dreq = Encoding.ASCII.GetBytes("DESCRIBE rtsp://127.0.0.1:" + srv.Port + "/live RTSP/1.0\r\nCSeq: 2\r\nAccept: application/sdp\r\n\r\n");
			ns.Write(dreq, 0, dreq.Length);
			acc.Clear();
			t0 = Environment.TickCount;
			while (Environment.TickCount - t0 < 2000) {
				if (!ns.DataAvailable) { Thread.Sleep(10); continue; }
				var n = ns.Read(buf, 0, buf.Length);
				if (n <= 0) break;
				acc.Append(Encoding.ASCII.GetString(buf, 0, n));
				var got = acc.ToString();
				var mark = got.IndexOf("\r\n\r\n", StringComparison.Ordinal);
				if (mark < 0) continue;
				var cm = Regex.Match(got, @"Content-Length:\s*(\d+)", RegexOptions.IgnoreCase);
				var need = cm.Success ? int.Parse(cm.Groups[1].Value) : 0;
				if (got.Length >= mark + 4 + need) break;
			}
			var sdp = acc.ToString();
			if (sdp.IndexOf("m=audio", StringComparison.OrdinalIgnoreCase) < 0
				|| sdp.IndexOf("PCMU/8000", StringComparison.OrdinalIgnoreCase) < 0) {
				err("rtspsrv: SDP 没有 PCMU");
				return 1;
			}
			log("rtspsrv: SDP 含 PCMU");
			if (!FfmpegLoader.TryInit(out var ferr)) {
				log("rtspsrv: 跳过编码 " + ferr);
				return 0;
			}
			using (var enc = new RtspH264(160, 120, 10, 400_000))
			using (var dec = new CastVideoDecoder()) {
				var bgra = new byte[160 * 120 * 4];
				for (var i = 0; i < bgra.Length; i += 4) bgra[i + 3] = 0xFF;
				fillrect(bgra, 160, 48, 40, 40, 40, 0x44, 0x44, 0xFF);
				byte[] au = null;
				for (var i = 0; i < 6; i++) {
					au = enc.Encode(bgra);
					if (au == null) continue;
					var nals = split(au);
					var kinds = "";
					foreach (var n in nals) {
						if (n.Length < 1) continue;
						kinds += (n[0] & 0x1F) + ":" + n.Length + " ";
					}
					log("rtspsrv: nal " + kinds + " head " + hex(au, 16));
					if (!dec.Decode(au, out var pix, out var dw, out var dh, out var st)) continue;
					var red = 0;
					var outred = 0;
					for (var y = 0; y < dh; y++) {
						for (var x = 0; x < dw; x++) {
							var o = y * st + x * 4;
							var isred = pix[o + 2] > 180 && pix[o + 1] < 120 && pix[o] < 120;
							var inside = x >= 48 && x < 88 && y >= 40 && y < 80;
							if (isred && inside) red++;
							else if (isred) outred++;
						}
					}
					log("rtspsrv: 解码 " + dw + "x" + dh + " 方块内红点 " + red + " 外面 " + outred);
					if (red > 1400 && outred < 80) {
						log("rtspsrv: 方块完整");
						return 0;
					}
				}
				err("rtspsrv: 方块被拆开或编出来就不完整");
				return 1;
			}
		}
		catch (Exception ex) {
			err("rtspsrv: " + ex.Message);
			return 1;
		}
		finally {
			try { c?.Close(); } catch { }
			try { srv?.Dispose(); } catch { }
		}
	}

	static void fillrect(byte[] bgra, int stridePx, int x, int y, int rw, int rh, byte b, byte g, byte r) {
		for (var yy = 0; yy < rh; yy++) {
			for (var xx = 0; xx < rw; xx++) {
				var o = ((y + yy) * stridePx + (x + xx)) * 4;
				bgra[o] = b;
				bgra[o + 1] = g;
				bgra[o + 2] = r;
				bgra[o + 3] = 0xFF;
			}
		}
	}

	static string hex(byte[] data, int n) {
		var sb = new StringBuilder();
		var m = Math.Min(n, data.Length);
		for (var i = 0; i < m; i++) {
			if (i > 0) sb.Append(' ');
			sb.Append(data[i].ToString("X2"));
		}
		return sb.ToString();
	}

	void accept() {
		while (running && cts != null && !cts.IsCancellationRequested) {
			TcpClient client;
			try { client = listener.AcceptTcpClient(); }
			catch { break; }
			client.NoDelay = true;
			var ep = client.Client.RemoteEndPoint;
			var session = new Session(this, client);
			sessions[session.Id] = session;
			say("客户端已连接: " + ep + "（" + sessions.Count + "）");
			_ = session.Run(cts.Token);
		}
	}

	void remove(Session s) {
		if (sessions.TryRemove(s.Id, out _))
			say("客户端已断开（剩余 " + sessions.Count + "）");
	}

	void say(string msg) {
		try { Log?.Invoke(msg); } catch { }
	}

	static IEnumerable<IPAddress> lan() {
		foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()) {
			if (ni.OperationalStatus != OperationalStatus.Up) continue;
			if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
			foreach (var addr in ni.GetIPProperties().UnicastAddresses) {
				if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
					yield return addr.Address;
			}
		}
	}

	static List<byte[]> split(byte[] frame) {
		var list = new List<byte[]>();
		var i = 0;
		var start = -1;
		while (i < frame.Length) {
			var sc = 0;
			if (i + 3 < frame.Length && frame[i] == 0 && frame[i + 1] == 0 && frame[i + 2] == 0 && frame[i + 3] == 1) sc = 4;
			else if (i + 2 < frame.Length && frame[i] == 0 && frame[i + 1] == 0 && frame[i + 2] == 1) sc = 3;
			if (sc > 0) {
				if (start >= 0 && i > start) add(list, frame, start, i - start);
				start = i + sc;
				i += sc;
			}
			else i++;
		}
		if (start >= 0 && start < frame.Length) add(list, frame, start, frame.Length - start);
		return list;
	}

	static void add(List<byte[]> list, byte[] src, int off, int len) {
		var n = new byte[len];
		Buffer.BlockCopy(src, off, n, 0, len);
		list.Add(n);
	}

	static byte[] wrap(byte[] nal) {
		var o = new byte[nal.Length + 4];
		o[3] = 1;
		Buffer.BlockCopy(nal, 0, o, 4, nal.Length);
		return o;
	}

	sealed class Session {
		readonly RtspTestSrv owner;
		readonly TcpClient client;
		readonly NetworkStream stream;
		readonly string sid = Guid.NewGuid().ToString("N");
		readonly ConcurrentQueue<byte[]> frames = new();
		readonly AutoResetEvent kick = new(false);
		readonly uint ssrc = (uint)rand.Next();
		ushort seq;
		uint stamp;
		ushort aseq;
		uint astamp;
		int asamp;
		int atick;
		double phase;
		bool audioGo;
		int closed;
		int dropidr = 1;
		public Guid Id { get; } = Guid.NewGuid();
		public bool Playing { get; private set; }

		public Session(RtspTestSrv owner, TcpClient client) {
			this.owner = owner;
			this.client = client;
			stream = client.GetStream();
		}

		public void Enqueue(byte[] frame) {
			if (!Playing || Volatile.Read(ref closed) != 0) return;
			var idr = false;
			foreach (var nal in split(frame)) {
				if (nal.Length > 0 && (nal[0] & 0x1F) == 5) { idr = true; break; }
			}
			if (frames.Count >= 8) {
				while (frames.TryDequeue(out _)) { }
				dropidr = 1;
				if (!idr) return;
				dropidr = 0;
			}
			else if (Volatile.Read(ref dropidr) != 0) {
				if (!idr) return;
				dropidr = 0;
			}
			frames.Enqueue(frame);
			kick.Set();
		}

		public async Task Run(CancellationToken token) {
			try { await talk(token); }
			catch (Exception ex) { Debug.WriteLine("rtspsrv " + ex.Message); }
			finally {
				Close();
				owner.remove(this);
			}
		}

		public void Close() {
			if (Interlocked.Exchange(ref closed, 1) != 0) return;
			Playing = false;
			try { kick.Set(); } catch { }
			try { stream.Close(); } catch { }
			try { client.Close(); } catch { }
		}

		async Task talk(CancellationToken token) {
			while (!token.IsCancellationRequested && client.Connected) {
				var raw = await readreq(token);
				if (raw == null) break;
				var req = parse(raw);
				if (req == null) break;
				if (req.Method == "PLAY") {
					Playing = true;
					seq = (ushort)rand.Next(0, 0x7FFF);
					stamp = (uint)rand.Next();
					while (frames.TryDequeue(out _)) { }
					dropidr = 1;
					try { owner.ClientPlaying?.Invoke(); } catch { }
				}
				var resp = reply(req);
				if (resp == null) break;
				var bytes = Encoding.UTF8.GetBytes(resp);
				await stream.WriteAsync(bytes, 0, bytes.Length, token);
				if (req.Method == "PLAY") {
					await sendloop(token);
					break;
				}
				if (req.Method == "TEARDOWN") break;
			}
		}

		async Task<string> readreq(CancellationToken token) {
			var ms = new MemoryStream();
			var buffer = new byte[4096];
			while (!token.IsCancellationRequested) {
				var read = await stream.ReadAsync(buffer, 0, buffer.Length, token);
				if (read == 0) return null;
				ms.Write(buffer, 0, read);
				var text = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
				if (text.IndexOf("\r\n\r\n", StringComparison.Ordinal) < 0) continue;
				var m = Regex.Match(text, @"Content-Length:\s*(\d+)", RegexOptions.IgnoreCase);
				if (m.Success) {
					var body = int.Parse(m.Groups[1].Value);
					var headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
					if (ms.Length - headerEnd < body) continue;
				}
				return text;
			}
			return null;
		}

		Req parse(string raw) {
			var lines = raw.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
			if (lines.Length == 0) return null;
			var head = lines[0].Split(' ');
			if (head.Length < 2) return null;
			var req = new Req { Method = head[0], Url = head[1] };
			foreach (var line in lines) {
				if (line.StartsWith("CSeq:", StringComparison.OrdinalIgnoreCase) && line.Length > 5)
					int.TryParse(line.Substring(5).Trim(), out req.CSeq);
			}
			owner.say("收到 " + req.Method + " " + req.Url);
			return req;
		}

		string reply(Req req) {
			switch (req.Method) {
			case "OPTIONS":
				return headers(req, "Public: DESCRIBE, SETUP, TEARDOWN, PLAY, PAUSE, GET_PARAMETER");
			case "DESCRIBE":
				var sdp = makesdp(req.Url);
				var n = Encoding.UTF8.GetByteCount(sdp);
				return headers(req,
					"Content-Type: application/sdp",
					"Content-Length: " + n,
					"Content-Base: " + req.Url) + sdp;
			case "SETUP":
				var audioTrack = owner.Audio && req.Url.IndexOf("track1", StringComparison.OrdinalIgnoreCase) >= 0;
				if (audioTrack) audioGo = true;
				return headers(req, "Transport: RTP/AVP/TCP;unicast;interleaved=" + (audioTrack ? "2-3" : "0-1"));
			case "PLAY":
				return headers(req, "Range: npt=0.000-", "RTP-Info: url=" + req.Url + ";seq=" + seq + ";rtptime=" + stamp);
			case "PAUSE":
			case "TEARDOWN":
			case "GET_PARAMETER":
				return headers(req);
			default:
				return null;
			}
		}

		string headers(Req req, params string[] extra) {
			var sb = new StringBuilder();
			sb.Append("RTSP/1.0 200 OK\r\n");
			sb.Append("CSeq: ").Append(req.CSeq).Append("\r\n");
			sb.Append("Session: ").Append(sid).Append("\r\n");
			sb.Append("Server: ScreenKitRtsp/1.0\r\n");
			if (extra != null) {
				foreach (var h in extra) sb.Append(h).Append("\r\n");
			}
			sb.Append("\r\n");
			return sb.ToString();
		}

		string makesdp(string url) {
			var host = "0.0.0.0";
			try { host = new Uri(url).Host; } catch { }
			var sb = new StringBuilder();
			sb.Append("v=0\r\n");
			sb.Append("o=- ").Append(DateTimeOffset.UtcNow.ToUnixTimeSeconds()).Append(" 1 IN IP4 ").Append(host).Append("\r\n");
			sb.Append("s=ScreenKit Test\r\n");
			sb.Append("c=IN IP4 ").Append(host).Append("\r\n");
			sb.Append("t=0 0\r\n");
			sb.Append("a=control:*\r\n");
			sb.Append("m=video 0 RTP/AVP 96\r\n");
			sb.Append("a=rtpmap:96 H264/90000\r\n");
			sb.Append("a=control:track0\r\n");
			sb.Append("a=fmtp:96 packetization-mode=1");
			if (owner.sps != null && owner.pps != null && owner.sps.Length > 4 && owner.pps.Length > 4) {
				sb.Append("; sprop-parameter-sets=");
				sb.Append(Convert.ToBase64String(owner.sps, 4, owner.sps.Length - 4));
				sb.Append(',');
				sb.Append(Convert.ToBase64String(owner.pps, 4, owner.pps.Length - 4));
			}
			sb.Append("\r\n");
			if (owner.Audio) {
				sb.Append("m=audio 0 RTP/AVP 0\r\n");
				sb.Append("a=rtpmap:0 PCMU/8000/1\r\n");
				sb.Append("a=control:track1\r\n");
			}
			return sb.ToString();
		}

		async Task sendloop(CancellationToken token) {
			while (!token.IsCancellationRequested && Playing && client.Connected) {
				await pumpaudio(token);
				if (!kick.WaitOne(100) && frames.IsEmpty) continue;
				while (frames.TryDequeue(out var frame)) {
					var nals = split(frame);
					var sentidr = false;
					for (var i = 0; i < nals.Count; i++) {
						var nal = nals[i];
						if (nal.Length < 1) continue;
						var typ = nal[0] & 0x1F;
						if (typ == 7) { owner.sps = wrap(nal); continue; }
						if (typ == 8) { owner.pps = wrap(nal); continue; }
						if (typ == 5 && !sentidr) {
							sentidr = true;
							if (owner.sps != null) await sendnal(owner.sps, false, token);
							if (owner.pps != null) await sendnal(owner.pps, false, token);
						}
						var last = i + 1 >= nals.Count;
						await sendnal(wrap(nal), last, token);
					}
					stamp += 3000;
				}
			}
		}

		async Task pumpaudio(CancellationToken token) {
			if (!audioGo || !owner.Audio) return;
			if (atick == 0) atick = Environment.TickCount;
			var due = (Environment.TickCount - atick) * 8;
			var n = 0;
			while (asamp + 160 <= due && n < 8) {
				var packet = new byte[12 + 160];
				packet[0] = 0x80;
				packet[1] = 0;
				packet[2] = (byte)(aseq >> 8);
				packet[3] = (byte)aseq;
				packet[4] = (byte)(astamp >> 24);
				packet[5] = (byte)(astamp >> 16);
				packet[6] = (byte)(astamp >> 8);
				packet[7] = (byte)astamp;
				packet[8] = (byte)(ssrc >> 24);
				packet[9] = (byte)(ssrc >> 16);
				packet[10] = (byte)(ssrc >> 8);
				packet[11] = (byte)ssrc;
				for (var i = 0; i < 160; i++) {
					var s = Math.Sin(phase) * 7000.0;
					phase += 2 * Math.PI * 440.0 / 8000.0;
					if (phase > Math.PI * 2) phase -= Math.PI * 2;
					packet[12 + i] = mulaw((short)s);
				}
				var frame = new byte[4 + packet.Length];
				frame[0] = (byte)'$';
				frame[1] = 2;
				frame[2] = (byte)(packet.Length >> 8);
				frame[3] = (byte)packet.Length;
				Buffer.BlockCopy(packet, 0, frame, 4, packet.Length);
				await stream.WriteAsync(frame, 0, frame.Length, token);
				aseq++;
				astamp += 160;
				asamp += 160;
				n++;
			}
		}

		static byte mulaw(short sample) {
			const int BIAS = 0x84;
			const int CLIP = 32635;
			var sign = (sample >> 8) & 0x80;
			if (sign != 0) sample = (short)-sample;
			if (sample > CLIP) sample = CLIP;
			sample += BIAS;
			var exp = 7;
			for (var mask = 0x4000; (sample & mask) == 0 && exp > 0; mask >>= 1) exp--;
			var mantissa = (sample >> (exp + 3)) & 0x0F;
			return (byte)~(sign | (exp << 4) | mantissa);
		}

		async Task sendnal(byte[] nalUnit, bool marker, CancellationToken token) {
			var nalSize = nalUnit.Length - 4;
			if (nalSize <= 0) return;
			var nalHeader = nalUnit[4];
			if (nalSize <= 1400) {
				var packet = new byte[12 + nalSize];
				rtp(packet, marker);
				Buffer.BlockCopy(nalUnit, 4, packet, 12, nalSize);
				await sendrtp(packet, token);
				seq++;
				return;
			}
			var fu = (byte)((nalHeader & 0xE0) | 28);
			var nalType = (byte)(nalHeader & 0x1F);
			var offset = 5;
			var left = nalSize - 1;
			while (left > 0) {
				var chunk = Math.Min(1400, left);
				var first = offset == 5;
				var end = left == chunk;
				byte hdr = nalType;
				if (first) hdr |= 0x80;
				if (end) hdr |= 0x40;
				var packet = new byte[14 + chunk];
				rtp(packet, end && marker);
				packet[12] = fu;
				packet[13] = hdr;
				Buffer.BlockCopy(nalUnit, offset, packet, 14, chunk);
				await sendrtp(packet, token);
				offset += chunk;
				left -= chunk;
				seq++;
			}
		}

		void rtp(byte[] header, bool marker) {
			header[0] = 0x80;
			header[1] = (byte)((marker ? 0x80 : 0) | 96);
			header[2] = (byte)(seq >> 8);
			header[3] = (byte)seq;
			header[4] = (byte)(stamp >> 24);
			header[5] = (byte)(stamp >> 16);
			header[6] = (byte)(stamp >> 8);
			header[7] = (byte)stamp;
			header[8] = (byte)(ssrc >> 24);
			header[9] = (byte)(ssrc >> 16);
			header[10] = (byte)(ssrc >> 8);
			header[11] = (byte)ssrc;
		}

		async Task sendrtp(byte[] data, CancellationToken token) {
			var frame = new byte[4 + data.Length];
			frame[0] = (byte)'$';
			frame[1] = 0;
			frame[2] = (byte)(data.Length >> 8);
			frame[3] = (byte)data.Length;
			Buffer.BlockCopy(data, 0, frame, 4, data.Length);
			await stream.WriteAsync(frame, 0, frame.Length, token);
		}

		sealed class Req {
			public string Method;
			public string Url;
			public int CSeq;
		}
	}
}
