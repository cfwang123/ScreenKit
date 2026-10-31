using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ScreenKit;

/// <summary>
/// 调试用的 MQTT 服务端（broker）。纯 TCP 自己解报文，够用就行：
/// 认 CONNECT / SUBSCRIBE / UNSUBSCRIBE / PUBLISH / PINGREQ / DISCONNECT，
/// 支持 + 和 # 通配、QoS 0/1、保留消息；QoS 2 按 1 处理，不重传。
/// </summary>
sealed class MqttBroker : IDisposable {
	sealed class Session {
		public TcpClient Tcp;
		public NetworkStream Ns;
		public string ClientId = "";
		public int Keepalive;
		public int LastRecv;
		public volatile bool Closed;
		public Thread Thread;
		public readonly List<(string Filter, byte Qos)> Subs = new();
		public readonly object Gate = new();

		public void Send(byte[] data) {
			if (Closed || data == null) return;
			try {
				lock (Gate) {
					Ns.Write(data, 0, data.Length);
					Ns.Flush();
				}
			}
			catch { Closed = true; }
		}
	}

	sealed class Retained {
		public byte[] Payload;
		public int Qos;
	}

	readonly List<Session> sessions = new();
	readonly Dictionary<string, Retained> retained = new(StringComparer.Ordinal);
	readonly object gate = new();
	TcpListener listener;
	Thread acceptThread;
	volatile bool running;
	int nextid;

	public int Port { get; private set; }
	public bool Running => running;
	public event Action<string> Log;

	public MqttBroker(int port) => Port = port;

	public void Start() {
		if (running) return;
		listener = new TcpListener(IPAddress.Any, Port);
		listener.Start();
		Port = ((IPEndPoint)listener.LocalEndpoint).Port;
		running = true;
		say("服务端已启动，端口 " + Port);
		acceptThread = new Thread(accept) { IsBackground = true, Name = "mqtt-accept" };
		acceptThread.Start();
	}

	public void Stop() {
		if (!running) return;
		running = false;
		try { listener?.Stop(); } catch { }
		listener = null;
		List<Session> all;
		lock (gate) { all = new List<Session>(sessions); sessions.Clear(); }
		foreach (var s in all) close(s, null);
		try { acceptThread?.Join(600); } catch { }
		say("服务端已停止");
	}

	public void Dispose() => Stop();

	/// <summary>当前连着的客户端（给界面列出来）。</summary>
	public List<(string id, int subs)> Clients() {
		lock (gate) {
			var list = new List<(string, int)>(sessions.Count);
			foreach (var s in sessions) list.Add((s.ClientId, s.Subs.Count));
			return list;
		}
	}

	/// <summary>界面上的「发布」：直接分发给订阅者，跟客户端发来的走同一条路。</summary>
	public int Publish(string topic, byte[] payload, int qos, bool retain) {
		if (!MqttProto.validtopic(topic, false)) return 0;
		var n = dispatch(topic, payload, qos, retain);
		say($"发布 → {topic}  {payload?.Length ?? 0} B  QoS{qos}{(retain ? " 保留" : "")}  送达 {n}");
		return n;
	}

	void accept() {
		while (running) {
			TcpClient c = null;
			try { c = listener.AcceptTcpClient(); }
			catch { break; }
			if (!running) { try { c.Close(); } catch { } break; }
			var s = new Session {
				Tcp = c,
				Ns = c.GetStream(),
				LastRecv = Environment.TickCount,
			};
			c.NoDelay = true;
			s.Thread = new Thread(() => serve(s)) { IsBackground = true, Name = "mqtt-conn" };
			s.Thread.Start();
		}
	}

	void serve(Session s) {
		var peer = "";
		try {
			var ep = s.Tcp.Client.RemoteEndPoint as IPEndPoint;
			if (ep != null) peer = ep.Address + ":" + ep.Port;
			// 首包必须是 CONNECT，给了 10 秒
			var first = MqttProto.readpacket(s.Ns, 10000);
			if (first == null) { close(s, null); return; }
			if (first.Type == 0) { close(s, peer + " 报文错误"); return; }
			if (first.Type != MqttProto.CONNECT) {
				close(s, peer + " 首包不是 CONNECT");
				return;
			}
			var ci = MqttProto.parseconnect(first);
			if (ci.Error.Length > 0) {
				s.Send(MqttProto.connack(MqttProto.CONNACK_BADPROTO, false));
				close(s, peer + " " + ci.Error);
				return;
			}
			var cid = string.IsNullOrEmpty(ci.ClientId) ? "auto-" + Interlocked.Increment(ref nextid) : ci.ClientId;
			// 同名客户端顶掉旧的
			Session dup = null;
			lock (gate) {
				foreach (var o in sessions)
					if (string.Equals(o.ClientId, cid, StringComparison.Ordinal)) { dup = o; break; }
			}
			if (dup != null) close(dup, "被同名客户端 " + cid + " 顶替");
			s.ClientId = cid;
			s.Keepalive = ci.Keepalive;
			lock (gate) sessions.Add(s);
			s.Send(MqttProto.connack(MqttProto.CONNACK_ACCEPTED, false));
			say($"客户端连上 {cid}  {peer}  保活 {ci.Keepalive}s"
				+ (ci.UserName.Length > 0 ? "  用户 " + ci.UserName : ""));
			loop(s);
		}
		catch (Exception ex) {
			say("会话异常 " + (s.ClientId ?? peer) + "：" + ex.Message);
		}
		finally {
			close(s, s.Closed ? null : "对端断开");
		}
	}

	void loop(Session s) {
		while (running && !s.Closed) {
			// 保活 1.5 倍还没动静就当掉线
			var wait = s.Keepalive > 0 ? (int)(s.Keepalive * 1500.0) : 0;
			if (wait <= 0) wait = 3600_000;
			var p = MqttProto.readpacket(s.Ns, wait);
			if (p == null) {
				if (s.Keepalive > 0) say("客户端 " + s.ClientId + " 保活超时");
				return;
			}
			s.LastRecv = Environment.TickCount;
			if (p.Type == 0) { say("客户端 " + s.ClientId + " 报文错误"); return; }
			switch (p.Type) {
			case MqttProto.PINGREQ:
				s.Send(MqttProto.pingresp());
				break;
			case MqttProto.DISCONNECT:
				say("客户端 " + s.ClientId + " 主动断开");
				return;
			case MqttProto.SUBSCRIBE: {
				var list = new List<(string topic, byte qos)>();
				if (!MqttProto.parsesubscribe(p, out var pid, list, out var err)) {
					say("SUBSCRIBE 解析失败：" + err);
					return;
				}
				var granted = new List<byte>();
				foreach (var (topic, qos) in list) {
					if (!MqttProto.validtopic(topic, true)) { granted.Add(0x80); continue; }
					lock (s.Gate) {
						var hit = s.Subs.FindIndex(x => x.Filter == topic);
						if (hit >= 0) s.Subs[hit] = (topic, qos);
						else s.Subs.Add((topic, qos));
					}
					granted.Add((byte)(qos > 1 ? 1 : qos));
					say($"{s.ClientId} 订阅 {topic}  QoS{qos}");
				}
				s.Send(MqttProto.suback(pid, granted));
				// 订阅成功后补发保留消息
				foreach (var (topic, qos) in list) {
					foreach (var kv in snapshot()) {
						if (!MqttProto.topicmatch(topic, kv.Key)) continue;
						var m = kv.Value;
						s.Send(MqttProto.publish(kv.Key, m.Payload, Math.Min(qos, m.Qos), true, false, NextId()));
					}
				}
				break;
			}
			case MqttProto.UNSUBSCRIBE: {
				var list = new List<string>();
				if (!MqttProto.parseunsubscribe(p, out var pid, list, out var err)) {
					say("UNSUBSCRIBE 解析失败：" + err);
					return;
				}
				lock (s.Gate) s.Subs.RemoveAll(x => list.Contains(x.Filter));
				foreach (var t in list) say($"{s.ClientId} 取消订阅 {t}");
				s.Send(MqttProto.unsuback(pid));
				break;
			}
			case MqttProto.PUBLISH: {
				var pi = MqttProto.parsepublish(p);
				if (pi.Error.Length > 0) { say("PUBLISH 解析失败：" + pi.Error); return; }
				if (!MqttProto.validtopic(pi.Topic, false)) { say("PUBLISH 主题非法：" + pi.Topic); return; }
				if (pi.Qos == 1) s.Send(MqttProto.ack(MqttProto.PUBACK, pi.PacketId));
				var n = dispatch(pi.Topic, pi.Payload, pi.Qos, pi.Retain);
				say($"{s.ClientId} → {pi.Topic}  {pi.Payload.Length} B  QoS{pi.Qos}{(pi.Retain ? " 保留" : "")}  转发 {n}");
				break;
			}
			case MqttProto.PUBACK:
			case MqttProto.PUBREC:
			case MqttProto.PUBCOMP:
				break;
			case MqttProto.PUBREL:
				s.Send(MqttProto.ack(MqttProto.PUBCOMP, MqttProto.readpacketid(p)));
				break;
			default:
				say("忽略报文 " + MqttProto.typename(p.Type));
				break;
			}
		}
	}

	/// <summary>把一条消息送给所有命中的订阅者，返回实际送去的人数。发布者自己订阅了也算一份。</summary>
	int dispatch(string topic, byte[] payload, int qos, bool retain) {
		if (retain) {
			lock (gate) {
				if (payload == null || payload.Length == 0) retained.Remove(topic);
				else retained[topic] = new Retained { Payload = payload, Qos = qos };
			}
		}
		List<Session> all;
		lock (gate) all = new List<Session>(sessions);
		var n = 0;
		foreach (var s in all) {
			if (s.Closed) continue;
			byte subqos = 0;
			var hit = false;
			lock (s.Gate) {
				foreach (var (filter, q) in s.Subs) {
					if (!MqttProto.topicmatch(filter, topic)) continue;
					hit = true;
					if (q > subqos) subqos = q;
				}
			}
			if (!hit) continue;
			var use = Math.Min(qos, subqos);
			s.Send(MqttProto.publish(topic, payload, use, false, false, NextId()));
			n++;
		}
		return n;
	}

	Dictionary<string, Retained> snapshot() {
		lock (gate) return new Dictionary<string, Retained>(retained, StringComparer.Ordinal);
	}

	int NextId() {
		var v = Interlocked.Increment(ref nextid) & 0xFFFF;
		return v == 0 ? 1 : v;
	}

	void close(Session s, string why) {
		if (s == null) return;
		s.Closed = true;
		lock (gate) sessions.Remove(s);
		try { s.Ns?.Dispose(); } catch { }
		try { s.Tcp?.Close(); } catch { }
		if (!string.IsNullOrEmpty(why)) say("客户端 " + (s.ClientId ?? "") + " 断开：" + why);
		else if (!string.IsNullOrEmpty(s.ClientId)) say("客户端 " + s.ClientId + " 断开");
	}

	void say(string msg) {
		try { Log?.Invoke(msg); } catch { }
	}

	/// <summary>--test-mqtt 用：起服务端、跑一遍连/订/发/收，返回失败数。</summary>
	public static int Test(Action<string> log, Action<string> err) {
		var bad = 0;
		MqttBroker srv = null;
		TcpClient a = null, b = null;
		try {
			srv = new MqttBroker(0);
			srv.Log = m => log("srv: " + m);
			srv.Start();
			var port = srv.Port;
			if (port <= 0) { err("mqtt: 服务端没起来"); return 1; }

			// 两个客户端：A 订阅，B 发布
			a = new TcpClient();
			if (!a.ConnectAsync(IPAddress.Loopback, port).Wait(3000)) { err("mqtt: A 连不上"); return 1; }
			var na = a.GetStream();
			var caPkt = MqttProto.connect("A", "", "", 30, true, null, null, 0, false);
			na.Write(caPkt, 0, caPkt.Length);
			var ca = MqttProto.readpacket(na, 3000);
			if (ca == null || ca.Type != MqttProto.CONNACK || ca.Body.Length < 2 || ca.Body[1] != 0) {
				err("mqtt: A CONNACK 不对");
				return 1;
			}
			log("mqtt: A CONNACK OK");

			var sub = MqttProto.subscribe(1, new List<string> { "t/+/x" }, new List<byte> { 1 });
			na.Write(sub, 0, sub.Length);
			var sa = MqttProto.readpacket(na, 3000);
			if (sa == null || sa.Type != MqttProto.SUBACK) { err("mqtt: 没有 SUBACK"); return 1; }
			log("mqtt: SUBACK OK");

			b = new TcpClient();
			if (!b.ConnectAsync(IPAddress.Loopback, port).Wait(3000)) { err("mqtt: B 连不上"); return 1; }
			var nb = b.GetStream();
			var cb = MqttProto.connect("B", "", "", 30, true, null, null, 0, false);
			nb.Write(cb, 0, cb.Length);
			var rcb = MqttProto.readpacket(nb, 3000);
			if (rcb == null || rcb.Type != MqttProto.CONNACK) { err("mqtt: B CONNACK 不对"); return 1; }

			var data = Encoding.UTF8.GetBytes("hello");
			var pub = MqttProto.publish("t/1/x", data, 1, false, false, 7);
			nb.Write(pub, 0, pub.Length);
			var ack = MqttProto.readpacket(nb, 3000);
			if (ack == null || ack.Type != MqttProto.PUBACK) { err("mqtt: 发布没回 PUBACK"); bad++; }

			var got = MqttProto.readpacket(na, 3000);
			if (got == null || got.Type != MqttProto.PUBLISH) { err("mqtt: 订阅端没收到"); return bad + 1; }
			var pi = MqttProto.parsepublish(got);
			if (pi.Topic != "t/1/x" || MqttProto.text(pi.Payload) != "hello") {
				err($"mqtt: 收到的不对 {pi.Topic} / {MqttProto.text(pi.Payload)}");
				bad++;
			}
			else log("mqtt: 转发 OK t/1/x = hello");

			// 通配符不匹配的用例
			if (MqttProto.topicmatch("t/+/x", "t/1/2/x")) { err("mqtt: 通配符多层级误匹配"); bad++; }
			if (!MqttProto.topicmatch("t/#", "t/1/2/x")) { err("mqtt: # 未匹配多层"); bad++; }
			if (MqttProto.topicmatch("#", "$SYS/x")) { err("mqtt: # 不该匹配 $ 开头"); bad++; }
			if (!MqttProto.validtopic("a/+/b", true) || MqttProto.validtopic("a/#/b", true)) {
				err("mqtt: 主题校验不对");
				bad++;
			}
		}
		catch (Exception ex) {
			err("mqtt: " + ex);
			bad++;
		}
		finally {
			try { a?.Close(); } catch { }
			try { b?.Close(); } catch { }
			try { srv?.Dispose(); } catch { }
		}
		log(bad == 0 ? "=== OK：MQTT ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}
}
