using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ScreenKit;

/// <summary>
/// 把一路 RTSP（TCP 交织）收成 H.264，经已有网页端口的 WebSocket /rtsp 转给浏览器。
/// 勾选声音时再 SETUP 音频轨，转发 PCMU、PCMA 或 AAC。相同地址只拉一路。
/// 视频帧：切片前 4 字节是大端微秒时间戳，后面是带起始码的 NAL。
/// 音频帧以 0xA1 开头：类型、采样率、声道、时间戳，后面是载荷。
/// </summary>
static class RtspFwd {
	const int RECV = 262144;
	static readonly object gate = new();
	static readonly Dictionary<string, Pull> pulls = new(StringComparer.OrdinalIgnoreCase);

	public static void Attach(Stream net, byte[] prefetch, string rtspUrl, bool audio = true) {
		if (!okurl(rtspUrl)) {
			try { writeframe(net, 1, Encoding.UTF8.GetBytes("ERROR: rtsp 参数必须是 rtsp:// 开头的 URL")); } catch { }
			return;
		}
		var key = norm(rtspUrl);
		View view;
		Pull pull;
		lock (gate) {
			if (!pulls.TryGetValue(key, out pull) || pull.dead)
				pulls[key] = pull = new Pull(rtspUrl, key);
			view = pull.add(net, prefetch, audio);
		}
		try { view.pump(); }
		finally { pull.leave(view); }
	}

	public static int Test(Action<string> log, Action<string> err) {
		if (!testfua(err)) return 1;
		if (!testaac(err)) return 1;
		TcpListener cam = null;
		SockHttpServer http = null;
		ClientWebSocket ws = null;
		var httpPort = 18776;
		try {
			cam = new TcpListener(IPAddress.Loopback, 0);
			cam.Start();
			var camPort = ((IPEndPoint)cam.LocalEndpoint).Port;
			var accept = new Thread(() => fakeserve(cam)) { IsBackground = true, Name = "rtsp-fake" };
			accept.Start();
			http = new SockHttpServer();
			http.Start("127.0.0.1", httpPort, ctx => {
				var path = (ctx.Request.Url?.AbsolutePath ?? "/").TrimEnd('/');
				if (!path.Equals("/rtsp", StringComparison.OrdinalIgnoreCase)) {
					try { ctx.Response.StatusCode = 404; } catch { }
					return;
				}
				var up = ctx.Request.Headers["Upgrade"] ?? "";
				var isws = ctx.Request.IsWebSocketRequest
					|| up.IndexOf("websocket", StringComparison.OrdinalIgnoreCase) >= 0;
				if (!isws) {
					try { ctx.Response.StatusCode = 426; } catch { }
					return;
				}
				if (!ctx.TryUpgrade()) {
					try { ctx.Response.StatusCode = 400; } catch { }
					return;
				}
				Attach(ctx.Net, ctx.Prefetch, queryof(ctx.Request.Url, "rtsp"));
			});
			ws = new ClientWebSocket();
			var url = "rtsp://127.0.0.1:" + camPort + "/live";
			var uri = new Uri("ws://127.0.0.1:" + httpPort + "/rtsp?rtsp=" + Uri.EscapeDataString(url));
			if (!ws.ConnectAsync(uri, CancellationToken.None).Wait(4000) || ws.State != WebSocketState.Open) {
				err("rtsp: WebSocket 连不上");
				return 1;
			}
			var buf = new byte[8192];
			var t0 = Environment.TickCount;
			var got = false;
			while (Environment.TickCount - t0 < 4000 && ws.State == WebSocketState.Open) {
				var task = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
				if (!task.Wait(500)) continue;
				var r = task.Result;
				if (r.MessageType != WebSocketMessageType.Binary || r.Count < 9) continue;
				if (buf[4] == 0 && buf[5] == 0 && buf[6] == 0 && buf[7] == 1 && (buf[8] & 0x1F) == 5) {
					got = true;
					break;
				}
			}
			if (!got) {
				err("rtsp: 没有收到关键帧");
				return 1;
			}
			log("rtsp: 假相机关键帧已转发");
			return 0;
		}
		catch (Exception ex) {
			err("rtsp: " + ex.Message);
			return 1;
		}
		finally {
			try { ws?.Abort(); } catch { }
			try { http?.Stop(); } catch { }
			try { cam?.Stop(); } catch { }
			lock (gate) {
				foreach (var p in pulls.Values) p.stop();
				pulls.Clear();
			}
		}
	}

	static bool testfua(Action<string> err) {
		var body = new byte[] { 0x11, 0x22, 0x33, 0x44 };
		var nal = new byte[1 + body.Length];
		nal[0] = 0x65;
		Buffer.BlockCopy(body, 0, nal, 1, body.Length);
		var a = new byte[2 + 2];
		var b = new byte[2 + 2];
		a[0] = 0x7C;
		a[1] = 0x85;
		a[2] = body[0];
		a[3] = body[1];
		b[0] = 0x7C;
		b[1] = 0x45;
		b[2] = body[2];
		b[3] = body[3];
		var dep = new Depack();
		if (dep.Process(a, 1, false).Count != 0) {
			err("rtsp: FU-A 起始片不该出帧");
			return false;
		}
		var got = dep.Process(b, 2, true);
		if (got.Count != 1 || got[0].Length != 4 + nal.Length) {
			err("rtsp: FU-A 没有拼回 NAL");
			return false;
		}
		if (got[0][0] != 0 || got[0][1] != 0 || got[0][2] != 0 || got[0][3] != 1 || got[0][4] != 0x65) {
			err("rtsp: FU-A 起始码不对");
			return false;
		}
		return true;
	}

	static bool testaac(Action<string> err) {
		var payload = new byte[] { 0x00, 0x10, 0x00, 0x10, 0x11, 0x22 };
		var aus = Pull.aacframes(payload, 13, 3);
		if (aus.Count != 1 || aus[0].Length != 2 || aus[0][0] != 0x11 || aus[0][1] != 0x22) {
			err("rtsp: AAC 帧没有拆出来");
			return false;
		}
		return true;
	}

	static void fakeserve(TcpListener cam) {
		TcpClient c = null;
		try {
			c = cam.AcceptTcpClient();
			c.NoDelay = true;
			var s = c.GetStream();
			var buf = new byte[4096];
			var acc = new MemoryStream();
			for (var step = 0; step < 4; step++) {
				string req = null;
				while (req == null) {
					var n = s.Read(buf, 0, buf.Length);
					if (n <= 0) return;
					acc.Write(buf, 0, n);
					var raw = acc.ToArray();
					var text = Encoding.ASCII.GetString(raw);
					var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
					if (end < 0) continue;
					req = text.Substring(0, end);
					var rest = raw.Length - (end + 4);
					acc.SetLength(0);
					if (rest > 0) acc.Write(raw, end + 4, rest);
				}
				var cseq = "1";
				var m = Regex.Match(req, @"CSeq:\s*(\d+)", RegexOptions.IgnoreCase);
				if (m.Success) cseq = m.Groups[1].Value;
				byte[] resp;
				if (req.StartsWith("DESCRIBE", StringComparison.OrdinalIgnoreCase)) {
					var sdp = "v=0\r\nm=video 0 RTP/AVP 96\r\na=rtpmap:96 H264/90000\r\na=control:trackID=0\r\n";
					var head = "RTSP/1.0 200 OK\r\nCSeq: " + cseq
						+ "\r\nContent-Type: application/sdp\r\nContent-Length: " + sdp.Length + "\r\n\r\n";
					resp = Encoding.ASCII.GetBytes(head + sdp);
				}
				else if (req.StartsWith("SETUP", StringComparison.OrdinalIgnoreCase)) {
					resp = Encoding.ASCII.GetBytes("RTSP/1.0 200 OK\r\nCSeq: " + cseq
						+ "\r\nSession: 1\r\nTransport: RTP/AVP/TCP;unicast;interleaved=0-1\r\n\r\n");
				}
				else {
					resp = Encoding.ASCII.GetBytes("RTSP/1.0 200 OK\r\nCSeq: " + cseq + "\r\nSession: 1\r\n\r\n");
				}
				s.Write(resp, 0, resp.Length);
			}
			Thread.Sleep(80);
			var rtp = new byte[12 + 4];
			rtp[0] = 0x80;
			rtp[1] = 0xE0;
			rtp[2] = 0;
			rtp[3] = 1;
			rtp[12] = 0x65;
			rtp[13] = 0x88;
			rtp[14] = 0x84;
			rtp[15] = 0x00;
			var frame = new byte[4 + rtp.Length];
			frame[0] = (byte)'$';
			frame[1] = 0;
			frame[2] = (byte)(rtp.Length >> 8);
			frame[3] = (byte)rtp.Length;
			Buffer.BlockCopy(rtp, 0, frame, 4, rtp.Length);
			s.Write(frame, 0, frame.Length);
			Thread.Sleep(1500);
		}
		catch { }
		finally {
			try { c?.Close(); } catch { }
		}
	}

	static bool okurl(string url) {
		return !string.IsNullOrWhiteSpace(url)
			&& url.Trim().StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase);
	}

	static string norm(string url) {
		url = url.Trim();
		if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
		var path = uri.AbsolutePath.TrimEnd('/');
		if (path.Length == 0) path = "/";
		var port = uri.Port > 0 ? uri.Port : 554;
		return "rtsp://" + uri.Host + ":" + port + path;
	}

	static string queryof(Uri url, string key) {
		var q = url?.Query ?? "";
		if (q.StartsWith("?")) q = q.Substring(1);
		foreach (var part in q.Split('&')) {
			if (part.Length == 0) continue;
			var i = part.IndexOf('=');
			var k = Uri.UnescapeDataString(i < 0 ? part : part.Substring(0, i));
			if (!k.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
			return i < 0 ? "" : Uri.UnescapeDataString(part.Substring(i + 1));
		}
		return "";
	}

	static bool writeframe(Stream net, int opcode, byte[] payload) {
		payload ??= Array.Empty<byte>();
		var len = payload.Length;
		byte[] head;
		if (len < 126)
			head = new[] { (byte)(0x80 | opcode), (byte)len };
		else if (len <= 0xFFFF)
			head = new byte[] { (byte)(0x80 | opcode), 126, (byte)(len >> 8), (byte)len };
		else {
			head = new byte[10];
			head[0] = (byte)(0x80 | opcode);
			head[1] = 127;
			var n = (ulong)len;
			for (var i = 7; i >= 0; i--) {
				head[2 + i] = (byte)n;
				n >>= 8;
			}
		}
		try {
			net.Write(head, 0, head.Length);
			if (len > 0) net.Write(payload, 0, len);
			return true;
		}
		catch { return false; }
	}

	sealed class Pull {
		readonly string url;
		readonly string key;
		readonly object vgate = new();
		readonly List<View> views = new();
		readonly Depack dep = new();
		readonly Stopwatch clock = Stopwatch.StartNew();
		TcpClient tcp;
		NetworkStream stream;
		Socket sock;
		Thread thread;
		internal volatile bool dead;
		internal bool Dead => dead;
		volatile bool failed;
		int started;
		int cseq = 1;
		string session;
		int rtpch;
		int rtcpch = 1;
		bool hasAudio;
		int artp = 2;
		int artcp = 3;
		int akind;
		int arate = 8000;
		int ach = 1;
		int asize = 13;
		int aindex = 3;
		string aname = "";
		string acontrol;
		byte[] aacAsc;
		string control;
		string contentbase;
		string user = "";
		string pass = "";
		string realm;
		string nonce;
		string qop;
		string opaque;
		int nc;
		byte[] sps;
		byte[] pps;
		readonly List<byte[]> au = new();
		uint austamp;
		bool auon;
		bool aukey;
		byte[] recv = new byte[RECV];
		int recvlen;
		int parseat;
		int lasttick = Environment.TickCount;

		public Pull(string url, string key) {
			this.url = url.Trim();
			this.key = key;
			if (Uri.TryCreate(this.url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.UserInfo)) {
				var parts = uri.UserInfo.Split(new[] { ':' }, 2);
				user = Uri.UnescapeDataString(parts[0]);
				pass = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
			}
		}

		public View add(Stream net, byte[] prefetch, bool audio) {
			var v = new View(net, prefetch, audio);
			lock (vgate) views.Add(v);
			v.text("INFO: 加入共享流，当前 " + count() + " 人观看");
			if (failed) {
				v.kill();
				return v;
			}
			sendboot(v);
			if (Interlocked.CompareExchange(ref started, 1, 0) == 0) {
				thread = new Thread(run) { IsBackground = true, Name = "rtsp-pull" };
				thread.Start();
			}
			return v;
		}

		public void leave(View v) {
			var last = false;
			lock (vgate) {
				views.Remove(v);
				last = views.Count == 0;
			}
			if (last) stop();
		}

		public void stop() {
			dead = true;
			try { tcp?.Close(); } catch { }
			try { thread?.Join(800); } catch { }
			lock (gate) {
				if (pulls.TryGetValue(key, out var cur) && ReferenceEquals(cur, this))
					pulls.Remove(key);
			}
		}

		int count() {
			lock (vgate) return views.Count;
		}

		void run() {
			try {
				connect();
				lasttick = Environment.TickCount;
				var nextkeep = Environment.TickCount + 30000;
				while (!dead) {
					var avail = 0;
					try { avail = sock != null && sock.Connected ? sock.Available : 0; }
					catch { avail = 0; }
					if (avail > 0) readsome(avail);
					else Thread.Sleep(2);
					if (dead) break;
					if (Environment.TickCount - lasttick > 10000) {
						fail("ERROR: 10 秒没有收到数据");
						break;
					}
					if (Environment.TickCount - nextkeep > 0 && stream != null && session != null) {
						nextkeep = Environment.TickCount + 30000;
						try { writeonly("GET_PARAMETER", baseurl(), null); } catch { }
					}
				}
			}
			catch (Exception ex) {
				if (!dead) fail("ERROR: " + ex.Message);
			}
			finally {
				dead = true;
				try { stream?.Close(); } catch { }
				try { tcp?.Close(); } catch { }
				View[] snap;
				lock (vgate) snap = views.ToArray();
				foreach (var v in snap) v.kill();
			}
		}

		void connect() {
			var uri = new Uri(url);
			var host = uri.Host;
			var port = uri.Port > 0 ? uri.Port : 554;
			tcp = new TcpClient { NoDelay = true, ReceiveBufferSize = RECV, SendBufferSize = 65536 };
			var task = tcp.ConnectAsync(host, port);
			if (!task.Wait(5000)) {
				try { tcp.Close(); } catch { }
				throw new TimeoutException("RTSP 连接超时");
			}
			task.GetAwaiter().GetResult();
			stream = tcp.GetStream();
			sock = tcp.Client;
			var root = "rtsp://" + host + ":" + port + uri.PathAndQuery;
			command("OPTIONS", root, null);
			var sdp = command("DESCRIBE", root, "Accept: application/sdp");
			if (sdp.IndexOf("401", StringComparison.Ordinal) >= 0 && user.Length > 0) {
				parseauth(sdp);
				sdp = command("DESCRIBE", root, "Accept: application/sdp");
			}
			if (sdp.IndexOf("401", StringComparison.Ordinal) >= 0)
				throw new UnauthorizedAccessException("RTSP 认证失败");
			ok("DESCRIBE", sdp);
			parsebase(sdp);
			parseaudio(sdp);
			if (!h264(sdp))
				throw new InvalidOperationException("只转发 H.264，这条流不是 H.264");
			var setup = trackurl(control, host, port, root);
			var setupResp = command("SETUP", setup, "Transport: RTP/AVP/TCP;unicast;interleaved=0-1");
			ok("SETUP", setupResp);
			parsesess(setupResp);
			parsetrans(setupResp, false);
			if (hasAudio) {
				try {
					var setupA = trackurl(acontrol, host, port, root);
					var setupAResp = command("SETUP", setupA, "Transport: RTP/AVP/TCP;unicast;interleaved=2-3");
					ok("SETUP", setupAResp);
					parsetrans(setupAResp, true);
				}
				catch (Exception ex) {
					hasAudio = false;
					textall("INFO: 声音未接通：" + ex.Message);
				}
			}
			var play = string.IsNullOrEmpty(contentbase) ? root : contentbase;
			var playResp = command("PLAY", play, "Range: npt=0.000-");
			ok("PLAY", playResp);
			var hello = "INFO: RTSP 已连接，等待视频流";
			if (hasAudio) hello += "，" + aname;
			textall(hello);
			lock (vgate) {
				foreach (var v in views) sendboot(v);
			}
		}

		void readsome(int avail) {
			var room = recv.Length - recvlen;
			if (room < 4) compact();
			room = recv.Length - recvlen;
			var n = Math.Min(avail, room);
			if (n <= 0) return;
			var got = sock.Receive(recv, recvlen, n, SocketFlags.None);
			if (got <= 0) {
				fail("ERROR: RTSP 源断连");
				return;
			}
			recvlen += got;
			lasttick = Environment.TickCount;
			parse();
		}

		void parse() {
			while (parseat + 4 <= recvlen) {
				var b0 = recv[parseat];
				if (b0 == (byte)'$') {
					var ch = recv[parseat + 1];
					var plen = (recv[parseat + 2] << 8) | recv[parseat + 3];
					var video = ch == rtpch;
					var sound = hasAudio && ch == artp;
					if ((!video && !sound && ch != rtcpch && ch != artcp) || plen <= 0 || plen > recv.Length) {
						parseat++;
						continue;
					}
					var end = parseat + 4 + plen;
					if (end > recvlen) break;
					if (video) onrtp(parseat + 4, plen);
					else if (sound) onaud(parseat + 4, plen);
					parseat = end;
				}
				else if (b0 == (byte)'R' && parseat + 4 <= recvlen
					&& recv[parseat + 1] == (byte)'T' && recv[parseat + 2] == (byte)'S' && recv[parseat + 3] == (byte)'P') {
					var used = skipresp();
					if (used <= 0) break;
					parseat += used;
				}
				else {
					var found = -1;
					for (var i = parseat + 1; i < recvlen; i++) {
						if (recv[i] == (byte)'$' || recv[i] == (byte)'R') { found = i; break; }
					}
					parseat = found > 0 ? found : recvlen;
				}
			}
			compact();
		}

		void onrtp(int off, int len) {
			Rtp pkt;
			try { pkt = Rtp.Parse(recv, off, len); }
			catch { return; }
			var nals = dep.Process(pkt.Payload, pkt.Seq, pkt.Marker);
			foreach (var nal in nals) {
				if (nal.Length <= 4) continue;
				var typ = nal[4] & 0x1F;
				if (typ == 7) sps = (byte[])nal.Clone();
				else if (typ == 8) pps = (byte[])nal.Clone();
				if (typ is not (1 or 5 or 6 or 7 or 8 or 9)) continue;
				if (auon && pkt.Stamp != austamp) flushau();
				austamp = pkt.Stamp;
				auon = true;
				au.Add(nal);
				if (typ == 5) aukey = true;
				if (pkt.Marker) flushau();
			}
		}

		void onaud(int off, int len) {
			Rtp pkt;
			try { pkt = Rtp.Parse(recv, off, len); }
			catch { return; }
			if (pkt.Payload == null || pkt.Payload.Length == 0) return;
			if (akind == 2) {
				foreach (var au in aacframes(pkt.Payload, asize, aindex))
					sendaudio(2, au);
				return;
			}
			sendaudio(akind, pkt.Payload);
		}

		void sendaudio(int kind, byte[] payload) {
			if (payload == null || payload.Length == 0) return;
			var body = new byte[9 + payload.Length];
			body[0] = 0xA1;
			body[1] = (byte)kind;
			body[2] = (byte)(arate >> 8);
			body[3] = (byte)arate;
			body[4] = (byte)ach;
			var p = (uint)Math.Min(clock.Elapsed.TotalMilliseconds * 1000.0, uint.MaxValue);
			body[5] = (byte)(p >> 24);
			body[6] = (byte)(p >> 16);
			body[7] = (byte)(p >> 8);
			body[8] = (byte)p;
			Buffer.BlockCopy(payload, 0, body, 9, payload.Length);
			View[] snap;
			lock (vgate) snap = views.ToArray();
			foreach (var v in snap) v.audio(body);
		}

		void flushau() {
			if (au.Count == 0) { auon = false; aukey = false; return; }
			var len = 0;
			foreach (var n in au) len += n.Length;
			var body = new byte[4 + len];
			var p = (uint)Math.Min(clock.Elapsed.TotalMilliseconds * 1000.0, uint.MaxValue);
			body[0] = (byte)(p >> 24);
			body[1] = (byte)(p >> 16);
			body[2] = (byte)(p >> 8);
			body[3] = (byte)p;
			var o = 4;
			foreach (var n in au) {
				Buffer.BlockCopy(n, 0, body, o, n.Length);
				o += n.Length;
			}
			var key = aukey;
			au.Clear();
			auon = false;
			aukey = false;
			View[] snap;
			lock (vgate) snap = views.ToArray();
			foreach (var v in snap) v.binary(body, key);
		}

		void sendboot(View v) {
			if (hasAudio && akind == 2 && aacAsc != null) {
				var cfg = new byte[9 + aacAsc.Length];
				cfg[0] = 0xA1;
				cfg[1] = 3;
				cfg[2] = (byte)(arate >> 8);
				cfg[3] = (byte)arate;
				cfg[4] = (byte)ach;
				Buffer.BlockCopy(aacAsc, 0, cfg, 9, aacAsc.Length);
				v.audio(cfg);
			}
			if (sps == null || pps == null) return;
			var merged = new byte[sps.Length + pps.Length];
			Buffer.BlockCopy(sps, 0, merged, 0, sps.Length);
			Buffer.BlockCopy(pps, 0, merged, sps.Length, pps.Length);
			v.config(merged);
			v.text("INFO: 等待下一关键帧");
		}

		int skipresp() {
			var headerEnd = -1;
			for (var i = parseat; i < recvlen - 3; i++) {
				if (recv[i] == 13 && recv[i + 1] == 10 && recv[i + 2] == 13 && recv[i + 3] == 10) {
					headerEnd = i + 4;
					break;
				}
			}
			if (headerEnd < 0) return 0;
			var hdr = Encoding.ASCII.GetString(recv, parseat, headerEnd - parseat);
			var m = Regex.Match(hdr, @"Content-Length:\s*(\d+)", RegexOptions.IgnoreCase);
			var body = m.Success ? int.Parse(m.Groups[1].Value) : 0;
			var total = (headerEnd - parseat) + body;
			return parseat + total > recvlen ? 0 : total;
		}

		void compact() {
			if (parseat <= 0) return;
			if (parseat >= recvlen) { recvlen = 0; parseat = 0; return; }
			var r = recvlen - parseat;
			Buffer.BlockCopy(recv, parseat, recv, 0, r);
			recvlen = r;
			parseat = 0;
		}

		string command(string method, string target, string extra) {
			var data = Encoding.UTF8.GetBytes(reqtext(method, target, extra));
			stream.Write(data, 0, data.Length);
			var ms = new MemoryStream();
			var buf = new byte[4096];
			var t0 = Environment.TickCount;
			while (Environment.TickCount - t0 < 5000) {
				if (take(ms, out var resp, out var used)) {
					keepextra(ms.ToArray(), used);
					return Encoding.UTF8.GetString(resp);
				}
				if (sock.Available <= 0) { Thread.Sleep(2); continue; }
				var n = stream.Read(buf, 0, buf.Length);
				if (n <= 0) break;
				ms.Write(buf, 0, n);
			}
			if (take(ms, out var tail, out var tailUsed)) {
				keepextra(ms.ToArray(), tailUsed);
				return Encoding.UTF8.GetString(tail);
			}
			throw new TimeoutException(method + " 超时");
		}

		void writeonly(string method, string target, string extra) {
			var data = Encoding.UTF8.GetBytes(reqtext(method, target, extra));
			stream.Write(data, 0, data.Length);
		}

		string reqtext(string method, string target, string extra) {
			var sb = new StringBuilder();
			sb.Append(method).Append(' ').Append(target).Append(" RTSP/1.0\r\n");
			sb.Append("CSeq: ").Append(cseq++).Append("\r\n");
			if (session != null) sb.Append("Session: ").Append(session).Append("\r\n");
			if (user.Length > 0) {
				if (realm != null) sb.Append(digest(method, target)).Append("\r\n");
				else sb.Append("Authorization: Basic ")
					.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + pass))).Append("\r\n");
			}
			if (!string.IsNullOrEmpty(extra)) sb.Append(extra).Append("\r\n");
			sb.Append("User-Agent: ScreenKitRtsp/1.0\r\n\r\n");
			return sb.ToString();
		}

		bool take(MemoryStream ms, out byte[] response, out int consumed) {
			response = Array.Empty<byte>();
			consumed = 0;
			var raw = ms.ToArray();
			if (raw.Length < 8) return false;
			var pos = 0;
			while (pos + 4 <= raw.Length && raw[pos] == (byte)'$') {
				var plen = (raw[pos + 2] << 8) | raw[pos + 3];
				if (plen < 0 || plen > RECV) return false;
				var frameEnd = pos + 4 + plen;
				if (frameEnd > raw.Length) return false;
				pos = frameEnd;
			}
			if (pos + 12 > raw.Length) return false;
			if (!ascii(raw, pos, "RTSP/1.0")) return false;
			var headerEnd = indexof(raw, pos, "\r\n\r\n");
			if (headerEnd < 0) return false;
			headerEnd += 4;
			var hdr = Encoding.ASCII.GetString(raw, pos, headerEnd - pos);
			var m = Regex.Match(hdr, @"Content-Length:\s*(\d+)", RegexOptions.IgnoreCase);
			var body = m.Success ? int.Parse(m.Groups[1].Value) : 0;
			var end = headerEnd + Math.Max(0, body);
			if (raw.Length < end) return false;
			response = new byte[end - pos];
			Buffer.BlockCopy(raw, pos, response, 0, response.Length);
			consumed = end;
			if (pos > 0) pushrecv(raw, 0, pos);
			return true;
		}

		void keepextra(byte[] raw, int responseEnd) {
			if (responseEnd >= raw.Length) return;
			pushrecv(raw, responseEnd, raw.Length - responseEnd);
		}

		void pushrecv(byte[] raw, int off, int count) {
			if (count <= 0) return;
			if (recvlen + count > recv.Length) compact();
			if (recvlen + count > recv.Length) return;
			Buffer.BlockCopy(raw, off, recv, recvlen, count);
			recvlen += count;
		}

		static void ok(string method, string response) {
			var lineEnd = response.IndexOf('\n');
			var line = lineEnd >= 0 ? response.Substring(0, lineEnd).Trim() : response.Trim();
			if (line.IndexOf(" 200 ", StringComparison.Ordinal) >= 0) return;
			throw new InvalidOperationException(method + " 失败: " + line);
		}

		static bool h264(string sdp) {
			if (Regex.IsMatch(sdp, @"H265|HEVC|sprop-vps\s*=", RegexOptions.IgnoreCase)) return false;
			return true;
		}

		static string trackurl(string ctrl, string host, int port, string root) {
			if (string.IsNullOrEmpty(ctrl) || ctrl == "*") return root;
			if (ctrl.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase)) return ctrl;
			if (ctrl.StartsWith("/")) return "rtsp://" + host + ":" + port + ctrl;
			return root.TrimEnd('/') + "/" + ctrl;
		}

		void parseaudio(string sdp) {
			hasAudio = false;
			var lines = sdp.Split('\n');
			var at = -1;
			for (var i = 0; i < lines.Length; i++) {
				if (lines[i].Trim().StartsWith("m=audio", StringComparison.OrdinalIgnoreCase)) { at = i; break; }
			}
			if (at < 0) return;
			var parts = lines[at].Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
			var pt = -1;
			for (var i = 3; i < parts.Length; i++) {
				if (int.TryParse(parts[i], out var n)) { pt = n; break; }
			}
			string map = null;
			string fmtp = null;
			string ctrl = null;
			for (var i = at + 1; i < lines.Length; i++) {
				var line = lines[i].Trim();
				if (line.StartsWith("m=", StringComparison.OrdinalIgnoreCase)) break;
				if (line.StartsWith("a=rtpmap:", StringComparison.OrdinalIgnoreCase)) {
					var m = Regex.Match(line, @"a=rtpmap:(\d+)", RegexOptions.IgnoreCase);
					if (!m.Success || pt < 0 || m.Groups[1].Value == pt.ToString()) map = line;
				}
				else if (line.StartsWith("a=fmtp:", StringComparison.OrdinalIgnoreCase)) {
					var m = Regex.Match(line, @"a=fmtp:(\d+)", RegexOptions.IgnoreCase);
					if (!m.Success || pt < 0 || m.Groups[1].Value == pt.ToString()) fmtp = line;
				}
				else if (line.StartsWith("a=control:", StringComparison.OrdinalIgnoreCase))
					ctrl = line.Substring("a=control:".Length).Trim();
			}
			var codec = "";
			var rate = 8000;
			var ch = 1;
			if (map != null) {
				var m = Regex.Match(map, @"a=rtpmap:\d+\s+([^/\s]+)/(\d+)(?:/(\d+))?", RegexOptions.IgnoreCase);
				if (m.Success) {
					codec = m.Groups[1].Value;
					int.TryParse(m.Groups[2].Value, out rate);
					if (m.Groups[3].Success) int.TryParse(m.Groups[3].Value, out ch);
				}
			}
			else if (pt == 0) codec = "PCMU";
			else if (pt == 8) codec = "PCMA";
			if (rate <= 0) rate = 8000;
			if (ch <= 0) ch = 1;
			var name = codec.ToUpperInvariant();
			if (name == "PCMU") { akind = 0; aname = "PCMU " + rate + " Hz"; }
			else if (name == "PCMA") { akind = 1; aname = "PCMA " + rate + " Hz"; }
			else if (name == "MPEG4-GENERIC" || name == "MPEG4-GENERIC/AAC" || name.IndexOf("AAC", StringComparison.Ordinal) >= 0) {
				akind = 2;
				aname = "AAC " + rate + " Hz";
				asize = 13;
				aindex = 3;
				if (fmtp != null) {
					var sm = Regex.Match(fmtp, @"sizelength\s*=\s*(\d+)", RegexOptions.IgnoreCase);
					var im = Regex.Match(fmtp, @"indexlength\s*=\s*(\d+)", RegexOptions.IgnoreCase);
					if (sm.Success) int.TryParse(sm.Groups[1].Value, out asize);
					if (im.Success) int.TryParse(im.Groups[1].Value, out aindex);
					var cm = Regex.Match(fmtp, @"config\s*=\s*([0-9a-fA-F]+)", RegexOptions.IgnoreCase);
					if (cm.Success) aacAsc = hexbytes(cm.Groups[1].Value);
				}
				if (aacAsc == null) aacAsc = aacasc(rate, ch);
			}
			else {
				if (codec.Length > 0) textall("INFO: 声音是 " + codec + "，暂不转发");
				return;
			}
			arate = rate;
			ach = ch;
			acontrol = string.IsNullOrEmpty(ctrl) ? control : ctrl;
			hasAudio = true;
		}

		static byte[] aacasc(int rate, int channels) {
			int[] rates = { 96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350 };
			var idx = 4;
			for (var i = 0; i < rates.Length; i++) {
				if (rates[i] == rate) { idx = i; break; }
			}
			if (channels < 1) channels = 1;
			if (channels > 2) channels = 2;
			return new[] { (byte)((2 << 3) | (idx >> 1)), (byte)(((idx & 1) << 7) | (channels << 3)) };
		}

		static byte[] hexbytes(string s) {
			if ((s.Length & 1) != 0) s = "0" + s;
			var b = new byte[s.Length / 2];
			for (var i = 0; i < b.Length; i++)
				b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
			return b;
		}

		internal static List<byte[]> aacframes(byte[] payload, int sizeBits, int indexBits) {
			var list = new List<byte[]>();
			if (payload == null || payload.Length < 2) return list;
			if (sizeBits <= 0) sizeBits = 13;
			if (indexBits < 0) indexBits = 3;
			var headerBits = (payload[0] << 8) | payload[1];
			var headerBytes = (headerBits + 7) / 8;
			if (headerBytes < 0 || 2 + headerBytes > payload.Length) return list;
			var auBits = sizeBits + indexBits;
			if (auBits <= 0) return list;
			var count = headerBits / auBits;
			var bit = 16;
			var data = 2 + headerBytes;
			for (var i = 0; i < count; i++) {
				var size = readbits(payload, bit, sizeBits);
				bit += auBits;
				if (size <= 0 || data + size > payload.Length) break;
				var au = new byte[size];
				Buffer.BlockCopy(payload, data, au, 0, size);
				data += size;
				list.Add(au);
			}
			return list;
		}

		static int readbits(byte[] data, int bit, int n) {
			var v = 0;
			for (var i = 0; i < n; i++) {
				var p = bit + i;
				var b = p >> 3;
				if (b < 0 || b >= data.Length) break;
				v = (v << 1) | ((data[b] >> (7 - (p & 7))) & 1);
			}
			return v;
		}

		string baseurl() {
			var uri = new Uri(url);
			var port = uri.Port > 0 ? uri.Port : 554;
			return "rtsp://" + uri.Host + ":" + port + uri.PathAndQuery;
		}

		void parsebase(string response) {
			var m = Regex.Match(response, @"Content-Base:\s*(\S+)", RegexOptions.IgnoreCase);
			if (m.Success) contentbase = m.Groups[1].Value.Trim();
			var lines = response.Split('\n');
			var video = -1;
			for (var i = 0; i < lines.Length; i++) {
				if (lines[i].Trim().StartsWith("m=video", StringComparison.OrdinalIgnoreCase)) { video = i; break; }
			}
			if (video < 0) return;
			for (var i = video + 1; i < lines.Length; i++) {
				var line = lines[i].Trim();
				if (line.StartsWith("m=", StringComparison.OrdinalIgnoreCase)) break;
				if (line.StartsWith("a=control:", StringComparison.OrdinalIgnoreCase))
					control = line.Substring("a=control:".Length).Trim();
			}
		}

		void parsesess(string response) {
			var m = Regex.Match(response, @"Session:\s*([^\s;]+)", RegexOptions.IgnoreCase);
			if (m.Success) session = m.Groups[1].Value;
		}

		void parsetrans(string response, bool audio) {
			var m = Regex.Match(response, @"interleaved=(\d+)-(\d+)", RegexOptions.IgnoreCase);
			if (!m.Success) return;
			var a = int.Parse(m.Groups[1].Value);
			var b = int.Parse(m.Groups[2].Value);
			if (audio) { artp = a; artcp = b; }
			else { rtpch = a; rtcpch = b; }
		}

		void parseauth(string response) {
			var m = Regex.Match(response, @"WWW-Authenticate:\s*Digest\s+(.*)", RegexOptions.IgnoreCase);
			if (!m.Success) return;
			var p = m.Groups[1].Value;
			realm = authp(p, "realm");
			nonce = authp(p, "nonce");
			qop = authp(p, "qop");
			opaque = authp(p, "opaque");
		}

		static string authp(string s, string name) {
			var m = Regex.Match(s, name + "=(?:\"([^\"]*)\"|([^,\\s]+))", RegexOptions.IgnoreCase);
			if (!m.Success) return null;
			return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
		}

		string digest(string method, string uri) {
			var ha1 = md5(user + ":" + realm + ":" + pass);
			var ha2 = md5(method + ":" + uri);
			string resp;
			string header;
			if (!string.IsNullOrEmpty(qop)) {
				nc++;
				var ncs = nc.ToString("D8");
				var cnonce = Guid.NewGuid().ToString("N");
				resp = md5(ha1 + ":" + nonce + ":" + ncs + ":" + cnonce + ":" + qop + ":" + ha2);
				header = "Authorization: Digest username=\"" + user + "\", realm=\"" + realm
					+ "\", nonce=\"" + nonce + "\", uri=\"" + uri + "\", qop=" + qop
					+ ", nc=" + ncs + ", cnonce=\"" + cnonce + "\", response=\"" + resp + "\"";
			}
			else {
				resp = md5(ha1 + ":" + nonce + ":" + ha2);
				header = "Authorization: Digest username=\"" + user + "\", realm=\"" + realm
					+ "\", nonce=\"" + nonce + "\", uri=\"" + uri + "\", response=\"" + resp + "\"";
			}
			if (opaque != null) header += ", opaque=\"" + opaque + "\"";
			return header;
		}

		static string md5(string s) {
			using var h = MD5.Create();
			var b = h.ComputeHash(Encoding.UTF8.GetBytes(s));
			var sb = new StringBuilder(b.Length * 2);
			foreach (var x in b) sb.Append(x.ToString("x2"));
			return sb.ToString();
		}

		void fail(string msg) {
			failed = true;
			textall(msg);
			View[] snap;
			lock (vgate) snap = views.ToArray();
			foreach (var v in snap) v.kill();
			dead = true;
		}

		void textall(string msg) {
			View[] snap;
			lock (vgate) snap = views.ToArray();
			foreach (var v in snap) v.text(msg);
		}

		static bool ascii(byte[] data, int offset, string text) {
			if (offset + text.Length > data.Length) return false;
			for (var i = 0; i < text.Length; i++)
				if (data[offset + i] != (byte)text[i]) return false;
			return true;
		}

		static int indexof(byte[] data, int offset, string pattern) {
			var pat = Encoding.ASCII.GetBytes(pattern);
			for (var i = offset; i <= data.Length - pat.Length; i++) {
				var ok = true;
				for (var j = 0; j < pat.Length; j++) {
					if (data[i + j] != pat[j]) { ok = false; break; }
				}
				if (ok) return i;
			}
			return -1;
		}
	}

	sealed class View {
		readonly Stream net;
		readonly object qgate = new();
		readonly Queue<Item> q = new();
		readonly WsIn bin = new();
		readonly bool hear;
		volatile bool dead;
		bool waitkey = true;
		byte[] pre;

		public View(Stream net, byte[] prefetch, bool audio) {
			this.net = net;
			hear = audio;
			pre = prefetch ?? Array.Empty<byte>();
		}

		public void text(string s) {
			if (dead) return;
			lock (qgate) q.Enqueue(new Item { op = 1, data = Encoding.UTF8.GetBytes(s ?? "") });
		}

		public void config(byte[] data) {
			if (dead || data == null) return;
			lock (qgate) q.Enqueue(new Item { op = 2, data = data });
			waitkey = true;
		}

		public void audio(byte[] data) {
			if (dead || !hear || data == null) return;
			lock (qgate) {
				while (q.Count > 120) q.Dequeue();
				q.Enqueue(new Item { op = 2, data = data });
			}
		}

		public void binary(byte[] data, bool key) {
			if (dead) return;
			if (waitkey) {
				if (!key) return;
				waitkey = false;
			}
			lock (qgate) {
				while (q.Count > 120) q.Dequeue();
				q.Enqueue(new Item { op = 2, data = data });
			}
		}

		public void kill() { dead = true; }

		public void pump() {
			try {
				if (pre != null && pre.Length > 0) {
					if (!bin.feed(net, pre, 0, pre.Length)) dead = true;
					pre = null;
				}
				while (!dead) {
					flush();
					if (dead) break;
					if (net is NetworkStream ns && ns.DataAvailable) {
						var buf = new byte[4096];
						var n = net.Read(buf, 0, buf.Length);
						if (n <= 0) break;
						if (!bin.feed(net, buf, 0, n)) break;
					}
					else Thread.Sleep(2);
				}
			}
			catch { }
			finally {
				dead = true;
				try { net.Close(); } catch { }
			}
		}

		void flush() {
			while (true) {
				Item item;
				lock (qgate) {
					if (q.Count == 0) return;
					item = q.Dequeue();
				}
				if (!writeframe(net, item.op, item.data)) { dead = true; return; }
			}
		}

		sealed class Item {
			public int op;
			public byte[] data;
		}
	}

	/// <summary>只处理浏览器发来的控制帧。画面是服务器往下发的。</summary>
	sealed class WsIn {
		readonly MemoryStream acc = new();

		public bool feed(Stream net, byte[] buf, int off, int len) {
			acc.Write(buf, off, len);
			var raw = acc.ToArray();
			var pos = 0;
			while (pos + 2 <= raw.Length) {
				var op = raw[pos] & 0x0F;
				var masked = (raw[pos + 1] & 0x80) != 0;
				ulong n = (ulong)(raw[pos + 1] & 0x7F);
				var head = 2;
				if (n == 126) {
					if (pos + 4 > raw.Length) break;
					n = (ulong)((raw[pos + 2] << 8) | raw[pos + 3]);
					head = 4;
				}
				else if (n == 127) {
					if (pos + 10 > raw.Length) break;
					n = 0;
					for (var i = 0; i < 8; i++) n = (n << 8) | raw[pos + 2 + i];
					head = 10;
				}
				var mask = masked ? 4 : 0;
				if (n > 1024 * 1024) return false;
				if ((ulong)(raw.Length - pos) < (ulong)head + (ulong)mask + n) break;
				if (op == 8) return false;
				if (op == 9) {
					var payload = new byte[n];
					var start = pos + head + mask;
					Buffer.BlockCopy(raw, start, payload, 0, (int)n);
					if (masked) {
						for (var i = 0; i < payload.Length; i++)
							payload[i] ^= raw[pos + head + (i % 4)];
					}
					writeframe(net, 0xA, payload);
				}
				pos += head + mask + (int)n;
			}
			acc.SetLength(0);
			if (pos < raw.Length) acc.Write(raw, pos, raw.Length - pos);
			return true;
		}
	}

	sealed class Depack {
		List<byte> fu;
		byte fuhead;

		public List<byte[]> Process(byte[] payload, ushort seq, bool marker) {
			_ = seq;
			_ = marker;
			var result = new List<byte[]>();
			if (payload == null || payload.Length == 0) return result;
			var typ = payload[0] & 0x1F;
			if (typ == 28) fua(payload, result);
			else if (typ == 24) stapa(payload, result);
			else if (typ >= 1 && typ <= 23) result.Add(start(payload));
			return result;
		}

		void fua(byte[] payload, List<byte[]> result) {
			if (payload.Length < 2) return;
			var ind = payload[0];
			var hdr = payload[1];
			var startbit = (hdr & 0x80) != 0;
			var endbit = (hdr & 0x40) != 0;
			var nalType = (byte)(hdr & 0x1F);
			if (startbit) {
				fu = new List<byte>();
				fuhead = (byte)((ind & 0xE0) | nalType);
				fu.Add(fuhead);
				for (var i = 2; i < payload.Length; i++) fu.Add(payload[i]);
			}
			else if (fu != null) {
				for (var i = 2; i < payload.Length; i++) fu.Add(payload[i]);
				if (endbit) {
					result.Add(start(fu.ToArray()));
					fu = null;
				}
			}
		}

		static void stapa(byte[] payload, List<byte[]> result) {
			var off = 1;
			while (off + 2 < payload.Length) {
				var n = (payload[off] << 8) | payload[off + 1];
				off += 2;
				if (off + n > payload.Length) break;
				var nal = new byte[n];
				Buffer.BlockCopy(payload, off, nal, 0, n);
				result.Add(start(nal));
				off += n;
			}
		}

		static byte[] start(byte[] nal) {
			var o = new byte[nal.Length + 4];
			o[3] = 1;
			Buffer.BlockCopy(nal, 0, o, 4, nal.Length);
			return o;
		}
	}

	sealed class Rtp {
		public bool Marker;
		public ushort Seq;
		public uint Stamp;
		public byte[] Payload = Array.Empty<byte>();

		public static Rtp Parse(byte[] data, int offset, int length) {
			if (length < 12) throw new ArgumentException("RTP");
			var p = new Rtp();
			var b0 = data[offset];
			var cc = b0 & 0x0F;
			var ext = ((b0 >> 4) & 1) == 1;
			var b1 = data[offset + 1];
			p.Marker = ((b1 >> 7) & 1) == 1;
			p.Seq = (ushort)((data[offset + 2] << 8) | data[offset + 3]);
			p.Stamp = (uint)((data[offset + 4] << 24) | (data[offset + 5] << 16) | (data[offset + 6] << 8) | data[offset + 7]);
			var head = 12 + cc * 4;
			if (ext) {
				if (length < head + 4) throw new ArgumentException("RTP ext");
				var extLen = (data[offset + head + 2] << 8) | data[offset + head + 3];
				head += 4 + extLen * 4;
			}
			var plen = length - head;
			if (plen > 0) {
				p.Payload = new byte[plen];
				Buffer.BlockCopy(data, offset + head, p.Payload, 0, plen);
			}
			return p;
		}
	}
}
