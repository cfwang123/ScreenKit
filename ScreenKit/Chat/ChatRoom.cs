using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace ScreenKit;

/// <summary>工具页聊天室。WebSocket /chat，文字帧是 JSON。记录只在本进程内存里。</summary>
static class ChatRoom {
	const int S_OPEN = 1;
	const int S_DEAD = 0;
	const int MAXPEERS = 32;
	const int MAXNICK = 16;
	const int MAXTEXT = 500;
	const int HISTN = 40;
	const int JOINMS = 8000;
	const int IDLEMS = 70000;
	const int PINGMS = 20000;
	const int SAYMS = 200;

	static readonly JsonSerializerOptions jsonopt = new(JsonSerializerOptions.Default) {
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};
	static readonly object gate = new();
	static List<Conn> conns = new();
	static readonly List<string> hist = new();
	static Thread thread;
	static int nextid = 1;
	static volatile bool stop;

	public static void Attach(Stream net, byte[] prefetch) {
		var c = new Conn {
			net = net,
			pre = prefetch ?? Array.Empty<byte>(),
			state = S_OPEN,
			born = Environment.TickCount,
			lastrecv = Environment.TickCount,
			lastping = Environment.TickCount,
			done = new ManualResetEvent(false),
		};
		lock (gate) {
			if (stop) return;
			var n = 0;
			foreach (var x in conns)
				if (x.state == S_OPEN) n++;
			if (n >= MAXPEERS) {
				try { writeframe(net, 1, Encoding.UTF8.GetBytes(errjson("full"))); } catch { }
				return;
			}
			c.id = nextid++;
			if (nextid > 100000000) nextid = 1;
			conns.Add(c);
			ensure();
		}
		try { c.done.WaitOne(); }
		catch { }
	}

	public static void CloseAll() {
		List<Conn> copy;
		Thread th;
		lock (gate) {
			stop = true;
			copy = conns;
			conns = new();
			hist.Clear();
			th = thread;
		}
		foreach (var c in copy) {
			c.state = S_DEAD;
			try { c.net?.Close(); } catch { }
			try { c.done?.Set(); } catch { }
		}
		try { th?.Join(800); } catch { }
		lock (gate) {
			thread = null;
			stop = false;
		}
	}

	static void ensure() {
		if (thread != null && thread.IsAlive) return;
		stop = false;
		thread = new Thread(run) { IsBackground = true, Name = "chatroom" };
		thread.Start();
	}

	static void run() {
		while (!stop) {
			List<Conn> cur;
			lock (gate) cur = conns;
			var next = new List<Conn>();
			foreach (var c in cur) {
				if (stop) break;
				if (step(c)) next.Add(c);
				else drop(c);
			}
			lock (gate) {
				if (stop) break;
				if (!ReferenceEquals(conns, cur)) {
					foreach (var c in conns) {
						if (c.state == S_OPEN && !next.Contains(c)) next.Add(c);
					}
				}
				conns = next;
			}
			Thread.Sleep(10);
		}
	}

	static void drop(Conn c) {
		var was = c.joined;
		c.joined = false;
		c.state = S_DEAD;
		if (was && !string.IsNullOrEmpty(c.nick)) {
			broadcast(sysjson("leave", c), c);
			broadcast(peersjson(), c);
		}
		try { c.done?.Set(); } catch { }
	}

	static bool step(Conn c) {
		try { return onestep(c); }
		catch { return false; }
	}

	static bool onestep(Conn c) {
		if (c.state != S_OPEN) return false;
		var now = Environment.TickCount;
		if (!c.joined && now - c.born >= JOINMS) return false;
		if (now - c.lastrecv >= IDLEMS) return false;
		if (!pull(c)) return false;
		if (c.state != S_OPEN) return false;
		if (c.outq.Count > 0) {
			var b = c.outq.Dequeue();
			if (!writeframe(c.net, 1, b)) return false;
		}
		else if (now - c.lastping >= PINGMS) {
			c.lastping = now;
			if (!writeframe(c.net, 0x09, Array.Empty<byte>())) return false;
		}
		return c.state == S_OPEN;
	}

	static bool pull(Conn c) {
		var ns = c.net as System.Net.Sockets.NetworkStream;
		var have = c.preo < c.pre.Length || (ns != null && ns.DataAvailable);
		if (!have) return true;
		var tmp = new byte[4096];
		int n;
		try {
			if (c.preo < c.pre.Length) {
				n = Math.Min(tmp.Length, c.pre.Length - c.preo);
				Buffer.BlockCopy(c.pre, c.preo, tmp, 0, n);
				c.preo += n;
			}
			else n = c.net.Read(tmp, 0, tmp.Length);
		}
		catch { return false; }
		if (n <= 0) return false;
		append(c, tmp, n);
		return parse(c);
	}

	static void append(Conn c, byte[] src, int n) {
		if (c.accn + n > 1024 * 1024) {
			c.state = S_DEAD;
			return;
		}
		if (c.acc == null || c.acc.Length < c.accn + n) {
			var nb = new byte[Math.Max(4096, c.accn + n)];
			if (c.accn > 0) Buffer.BlockCopy(c.acc, 0, nb, 0, c.accn);
			c.acc = nb;
		}
		Buffer.BlockCopy(src, 0, c.acc, c.accn, n);
		c.accn += n;
	}

	static bool parse(Conn c) {
		while (c.state == S_OPEN) {
			if (c.accn < 2) return true;
			var b1 = c.acc[1];
			var op = c.acc[0] & 0x0F;
			var fin = (c.acc[0] & 0x80) != 0;
			var len = b1 & 0x7F;
			var masked = (b1 & 0x80) != 0;
			var hdr = 2;
			if (len == 126) {
				if (c.accn < 4) return true;
				len = (c.acc[2] << 8) | c.acc[3];
				hdr = 4;
			}
			else if (len == 127) return false;
			if (len < 0 || len > 65536) return false;
			if (masked) hdr += 4;
			if (c.accn < hdr + len) return true;
			var data = new byte[len];
			if (len > 0) Buffer.BlockCopy(c.acc, hdr, data, 0, len);
			if (masked) {
				var m = hdr - 4;
				for (var i = 0; i < len; i++) data[i] ^= c.acc[m + (i & 3)];
			}
			consume(c, hdr + len);
			c.lastrecv = Environment.TickCount;
			if (!onframe(c, op, fin, data)) return false;
		}
		return false;
	}

	static void consume(Conn c, int n) {
		c.accn -= n;
		if (c.accn > 0) Buffer.BlockCopy(c.acc, n, c.acc, 0, c.accn);
	}

	static bool onframe(Conn c, int op, bool fin, byte[] data) {
		if (op == 8) return false;
		if (op == 9) return writeframe(c.net, 0x0A, data);
		if (op == 10) return true;
		if (op == 0 || !fin) {
			if (op != 0) c.fragop = op;
			if (c.fragn + data.Length > 65536) return false;
			if (c.frag == null || c.frag.Length < c.fragn + data.Length) {
				var nb = new byte[Math.Max(256, c.fragn + data.Length)];
				if (c.fragn > 0) Buffer.BlockCopy(c.frag, 0, nb, 0, c.fragn);
				c.frag = nb;
			}
			if (data.Length > 0) Buffer.BlockCopy(data, 0, c.frag, c.fragn, data.Length);
			c.fragn += data.Length;
			if (!fin) return true;
			data = new byte[c.fragn];
			Buffer.BlockCopy(c.frag, 0, data, 0, c.fragn);
			op = c.fragop;
			c.fragn = 0;
		}
		if (op != 1) return true;
		ontext(c, Encoding.UTF8.GetString(data));
		return c.state == S_OPEN;
	}

	static void ontext(Conn c, string s) {
		JsonObject jo;
		try { jo = JsonNode.Parse(s) as JsonObject; }
		catch { enqueue(c, errjson("bad")); return; }
		if (jo == null) { enqueue(c, errjson("bad")); return; }
		var op = jo["op"]?.ToString() ?? "";
		if (op == "join") onjoin(c, jo);
		else if (op == "say") onsay(c, jo);
		else if (op == "ping") enqueue(c, "{\"op\":\"pong\"}");
	}

	static void onjoin(Conn c, JsonObject jo) {
		if (c.joined) { enqueue(c, errjson("joined")); return; }
		var gender = jo["gender"]?.ToString() ?? "";
		if (gender != "m" && gender != "f") gender = "";
		var nick = trimnick(jo["nick"]?.ToString(), out var nickerr);
		if (nickerr != null) { enqueue(c, errjson(nickerr)); return; }
		if (nickused(nick)) { enqueue(c, errjson("used")); return; }
		c.nick = nick;
		c.gender = gender;
		c.joined = true;
		enqueue(c, hellojson(c));
		enqueue(c, histjson());
		broadcast(sysjson("join", c), c);
		broadcast(peersjson(), null);
	}

	static void onsay(Conn c, JsonObject jo) {
		if (!c.joined) { enqueue(c, errjson("join")); return; }
		var text = jo["text"]?.ToString() ?? "";
		text = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
		if (text.Length == 0) return;
		if (text.Length > MAXTEXT) { enqueue(c, errjson("text")); return; }
		if (c.lastsay != 0 && Environment.TickCount - c.lastsay < SAYMS) {
			enqueue(c, errjson("fast"));
			return;
		}
		c.lastsay = Environment.TickCount;
		var line = sayjson(c, text);
		hist.Add(line);
		if (hist.Count > HISTN) hist.RemoveAt(0);
		broadcast(line, null);
	}

	static string trimnick(string s, out string err) {
		err = null;
		s = (s ?? "").Trim();
		if (s.Length == 0) { err = "nick"; return ""; }
		var sb = new StringBuilder(s.Length);
		foreach (var ch in s) {
			if (ch < ' ' || ch == '\u007f') continue;
			sb.Append(ch);
		}
		s = sb.ToString();
		if (s.Length == 0) { err = "nick"; return ""; }
		if (s.Length > MAXNICK) { err = "long"; return ""; }
		return s;
	}

	static bool nickused(string nick) {
		List<Conn> cur;
		lock (gate) cur = conns;
		foreach (var c in cur) {
			if (c.state == S_OPEN && c.joined
				&& string.Equals(c.nick, nick, StringComparison.Ordinal))
				return true;
		}
		return false;
	}

	static void enqueue(Conn c, string json) {
		if (c.outq.Count >= 64) { c.state = S_DEAD; return; }
		c.outq.Enqueue(Encoding.UTF8.GetBytes(json));
	}

	static void broadcast(string json, Conn except) {
		var bytes = Encoding.UTF8.GetBytes(json);
		List<Conn> cur;
		lock (gate) cur = conns;
		foreach (var c in cur) {
			if (c == except || c.state != S_OPEN || !c.joined) continue;
			if (c.outq.Count >= 64) { c.state = S_DEAD; continue; }
			c.outq.Enqueue(bytes);
		}
	}

	static string hellojson(Conn c) => new JsonObject {
		["op"] = "hello",
		["id"] = c.id,
		["nick"] = c.nick,
		["gender"] = c.gender,
	}.ToJsonString(jsonopt);

	static string histjson() {
		var items = new JsonArray();
		foreach (var line in hist) {
			try { items.Add(JsonNode.Parse(line)); }
			catch { }
		}
		return new JsonObject { ["op"] = "hist", ["items"] = items }.ToJsonString(jsonopt);
	}

	static string sayjson(Conn c, string text) => new JsonObject {
		["op"] = "say",
		["id"] = c.id,
		["nick"] = c.nick,
		["gender"] = c.gender,
		["text"] = text,
		["t"] = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
	}.ToJsonString(jsonopt);

	static string sysjson(string kind, Conn c) => new JsonObject {
		["op"] = "sys",
		["kind"] = kind,
		["nick"] = c.nick,
		["gender"] = c.gender,
	}.ToJsonString(jsonopt);

	static string peersjson() {
		var list = new JsonArray();
		List<Conn> cur;
		lock (gate) cur = conns;
		foreach (var c in cur) {
			if (c.state != S_OPEN || !c.joined) continue;
			list.Add(new JsonObject {
				["id"] = c.id,
				["nick"] = c.nick,
				["gender"] = c.gender,
			});
		}
		return new JsonObject { ["op"] = "peers", ["list"] = list }.ToJsonString(jsonopt);
	}

	static string errjson(string code) =>
		new JsonObject { ["op"] = "err", ["code"] = code }.ToJsonString(jsonopt);

	static bool writeframe(Stream net, int opcode, byte[] payload) {
		payload ??= Array.Empty<byte>();
		var len = payload.Length;
		byte[] head;
		if (len < 126)
			head = new[] { (byte)(0x80 | opcode), (byte)len };
		else if (len <= 0xFFFF)
			head = new[] { (byte)(0x80 | opcode), (byte)126, (byte)(len >> 8), (byte)len };
		else return false;
		try {
			net.Write(head, 0, head.Length);
			if (len > 0) net.Write(payload, 0, len);
			net.Flush();
			return true;
		}
		catch { return false; }
	}

	public static int Test(Action<string> log, Action<string> err) {
		const int port = 18771;
		SockHttpServer server = null;
		ClientWebSocket a = null;
		ClientWebSocket b = null;
		ClientWebSocket c = null;
		try {
			server = new SockHttpServer();
			server.Start("127.0.0.1", port, ctx => {
				var path = (ctx.Request.Url?.AbsolutePath ?? "/").TrimEnd('/');
				if (path.Length == 0) path = "/";
				if (!string.Equals(path, "/chat", StringComparison.OrdinalIgnoreCase)) {
					try { ctx.Response.StatusCode = 404; } catch { }
					return;
				}
				var up = ctx.Request.Headers["Upgrade"] ?? "";
				var ws = ctx.Request.IsWebSocketRequest
					|| up.IndexOf("websocket", StringComparison.OrdinalIgnoreCase) >= 0;
				if (!ws) {
					try { ctx.Response.StatusCode = 426; } catch { }
					return;
				}
				if (!ctx.TryUpgrade()) {
					try { ctx.Response.StatusCode = 400; } catch { }
					return;
				}
				Attach(ctx.Net, ctx.Prefetch);
			});
			a = open(port, err);
			b = open(port, err);
			if (a == null || b == null) return 1;
			var holda = new Hold();
			var holdb = new Hold();
			send(a, "{\"op\":\"join\",\"nick\":\"甲\",\"gender\":\"m\"}");
			if (!wait(a, holda, "hello", null, 4000)) {
				err("chat: 甲没有 hello");
				return 1;
			}
			send(b, "{\"op\":\"join\",\"nick\":\"乙\",\"gender\":\"f\"}");
			if (!wait(b, holdb, "hello", null, 4000)) {
				err("chat: 乙没有 hello");
				return 1;
			}
			send(a, "{\"op\":\"say\",\"text\":\"你好\"}");
			if (!wait(b, holdb, "say", "你好", 4000)) {
				err("chat: 乙没有收到 你好");
				return 1;
			}
			c = open(port, err);
			if (c == null) return 1;
			var holdc = new Hold();
			send(c, "{\"op\":\"join\",\"nick\":\"甲\",\"gender\":\"m\"}");
			if (!wait(c, holdc, "err", "used", 4000)) {
				err("chat: 重复昵称没有拒绝");
				return 1;
			}
			log("chat: 两人互发通过");
			return 0;
		}
		catch (Exception ex) {
			err("chat: " + ex.Message);
			return 1;
		}
		finally {
			try { a?.Abort(); } catch { }
			try { b?.Abort(); } catch { }
			try { c?.Abort(); } catch { }
			CloseAll();
			try { server?.Stop(); } catch { }
		}
	}

	static ClientWebSocket open(int port, Action<string> err) {
		var ws = new ClientWebSocket();
		var task = ws.ConnectAsync(new Uri("ws://127.0.0.1:" + port + "/chat"), CancellationToken.None);
		if (!task.Wait(4000) || ws.State != WebSocketState.Open) {
			err("chat: 连不上 /chat");
			try { ws.Abort(); } catch { }
			return null;
		}
		return ws;
	}

	static void send(ClientWebSocket ws, string json) {
		var b = Encoding.UTF8.GetBytes(json);
		ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, CancellationToken.None).Wait(3000);
	}

	static bool wait(ClientWebSocket ws, Hold hold, string op, string text, int ms) {
		var t0 = Environment.TickCount;
		while (Environment.TickCount - t0 < ms) {
			var s = nextmsg(ws, hold, 200);
			if (string.IsNullOrEmpty(s)) continue;
			JsonObject jo;
			try { jo = JsonNode.Parse(s) as JsonObject; }
			catch { continue; }
			if (jo == null) continue;
			if ((jo["op"]?.ToString() ?? "") != op) continue;
			if (text == null) return true;
			var got = op == "err" ? (jo["code"]?.ToString() ?? "") : (jo["text"]?.ToString() ?? "");
			if (got == text) return true;
		}
		return false;
	}

	static string nextmsg(ClientWebSocket ws, Hold hold, int ms) {
		if (hold.pending == null)
			hold.pending = ws.ReceiveAsync(new ArraySegment<byte>(hold.buf), CancellationToken.None);
		var t0 = Environment.TickCount;
		while (!hold.pending.IsCompleted) {
			if (Environment.TickCount - t0 >= ms) return null;
			Thread.Sleep(10);
		}
		WebSocketReceiveResult r;
		try { r = hold.pending.Result; }
		catch { hold.pending = null; return null; }
		hold.pending = null;
		if (r.MessageType != WebSocketMessageType.Text || r.Count <= 0) return "";
		return Encoding.UTF8.GetString(hold.buf, 0, r.Count);
	}

	sealed class Hold {
		public byte[] buf = new byte[16384];
		public Task<WebSocketReceiveResult> pending;
	}

	sealed class Conn {
		public int id;
		public int state;
		public Stream net;
		public byte[] pre = Array.Empty<byte>();
		public int preo;
		public byte[] acc;
		public int accn;
		public byte[] frag;
		public int fragn;
		public int fragop;
		public bool joined;
		public string nick = "";
		public string gender = "";
		public int born;
		public int lastrecv;
		public int lastping;
		public int lastsay;
		public readonly Queue<byte[]> outq = new();
		public ManualResetEvent done;
	}
}
