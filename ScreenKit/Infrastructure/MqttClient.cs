using System.Net.Sockets;

namespace ScreenKit;

/// <summary>
/// 调试用的 MQTT 客户端。照 COMNET 那套来：连服务器、订主题、发消息、收消息，QoS 0/1/2 都认，外加心跳。
/// 只有一个读线程收报文；发 QoS&gt;0 的消息时等确认，确认由读线程登记。
/// </summary>
sealed class MqttClient : IDisposable {
	TcpClient tcp;
	NetworkStream ns;
	Thread thread;
	Thread pingthread;
	readonly object wgate = new();
	/// <summary>等某条确认：报文类型 + 标识 → 事件。</summary>
	readonly Dictionary<string, ManualResetEventSlim> waits = new(StringComparer.Ordinal);
	volatile bool running;
	int nextid;
	int lastrecv;

	public string Host { get; private set; } = "";
	public int Port { get; private set; }
	public string ClientId { get; private set; } = "";
	public int Keepalive { get; private set; } = 60;
	public volatile bool Connected;

	/// <summary>收到一条 PUBLISH：主题、正文、QoS、是否保留。</summary>
	public event Action<string, byte[], int, bool> Message;
	/// <summary>文本日志，直接进接收区。</summary>
	public event Action<string> Log;
	/// <summary>意外断开（不是自己调 Disconnect）。参数是原因，可能为空。</summary>
	public event Action<string> Lost;

	/// <summary>连上去握手等 CONNACK。返回空串表示成功，否则是错误说明。</summary>
	public string Connect(string host, int port, string clientId, string user, string pass, int keepalive) {
		if (Connected) Disconnect();
		Host = (host ?? "").Trim();
		Port = port;
		ClientId = string.IsNullOrWhiteSpace(clientId) ? "sk-" + Environment.TickCount.ToString("x") : clientId.Trim();
		Keepalive = keepalive <= 0 ? 60 : keepalive;
		if (Host.Length == 0) return "没填服务器地址";
		if (Port <= 0 || Port > 65535) return "端口不对";
		try {
			tcp = new TcpClient { NoDelay = true };
			if (!tcp.ConnectAsync(Host, Port).Wait(8000)) {
				close();
				return "连接超时";
			}
			ns = tcp.GetStream();
			var pkt = MqttProto.connect(ClientId, user, pass, Keepalive, true, null, null, 0, false);
			ns.Write(pkt, 0, pkt.Length);
			ns.Flush();
			var ack = MqttProto.readpacket(ns, 8000);
			if (ack == null) {
				close();
				return "没等到 CONNACK";
			}
			if (ack.Type != MqttProto.CONNACK) {
				close();
				return "应答不是 CONNACK，是 " + MqttProto.typename(ack.Type);
			}
			if (ack.Body.Length < 2) {
				close();
				return "CONNACK 太短";
			}
			var code = ack.Body[1];
			if (code != 0) {
				close();
				return "服务器拒绝：" + connacktext(code);
			}
			Connected = true;
			running = true;
			lastrecv = Environment.TickCount;
			thread = new Thread(recv) { IsBackground = true, Name = "mqtt-recv" };
			thread.Start();
			pingthread = new Thread(ping) { IsBackground = true, Name = "mqtt-ping" };
			pingthread.Start();
			say($"已连上 {Host}:{Port}  clientId={ClientId}  保活 {Keepalive}s");
			return "";
		}
		catch (Exception ex) {
			close();
			return explain(ex);
		}
	}

	public void Disconnect() {
		if (!running && tcp == null) return;
		running = false;
		try {
			if (ns != null) {
				var pkt = MqttProto.disconnect();
				ns.Write(pkt, 0, pkt.Length);
				ns.Flush();
			}
		}
		catch { }
		close();
		say("已断开");
	}

	public void Dispose() => Disconnect();

	static string connacktext(byte code) => code switch {
		1 => "协议版本不支持",
		2 => "客户端标识被拒",
		3 => "服务不可用",
		4 => "用户名或密码错误",
		5 => "未授权",
		_ => "错误码 " + code,
	};

	/// <summary>ConnectAsync 失败会被包成 AggregateException，剥出最里层的说明。</summary>
	static string explain(Exception ex) {
		var e = ex;
		while (e is AggregateException ae && ae.InnerException != null) e = ae.InnerException;
		if (e is SocketException se) {
			return se.SocketErrorCode switch {
				SocketError.ConnectionRefused => "对方拒绝连接（服务端没开或端口不对）",
				SocketError.TimedOut => "连接超时",
				SocketError.HostNotFound => "找不到这个主机",
				SocketError.NetworkUnreachable => "网络不通",
				_ => se.Message,
			};
		}
		return e.Message;
	}

	/// <summary>订阅一组主题。真正的结果在 SUBACK 里，会写进日志。</summary>
	public bool Subscribe(IList<string> topics, int qos) {
		if (!Connected) return false;
		var qs = new List<byte>();
		foreach (var _ in topics) qs.Add((byte)(qos < 0 ? 0 : qos > 2 ? 2 : qos));
		return write(MqttProto.subscribe(NextId(), topics, qs));
	}

	public bool Unsubscribe(IList<string> topics) {
		if (!Connected) return false;
		return write(MqttProto.unsubscribe(NextId(), topics));
	}

	/// <summary>发一条消息。QoS 1/2 等服务器确认，最多 5 秒。</summary>
	public bool Publish(string topic, byte[] payload, int qos, bool retain, out string err) {
		err = "";
		if (!Connected) { err = "还没连上"; return false; }
		if (!MqttProto.validtopic(topic, false)) { err = "主题不能为空，也别含 + 或 #"; return false; }
		var id = qos > 0 ? NextId() : 0;
		string key = null;
		ManualResetEventSlim ev = null;
		if (qos > 0) {
			key = (qos == 1 ? MqttProto.PUBACK : MqttProto.PUBREC) + ":" + id;
			ev = new ManualResetEventSlim(false);
			lock (wgate) waits[key] = ev;
		}
		try {
			if (!write(MqttProto.publish(topic, payload, qos, retain, false, id))) {
				err = "写不出去";
				return false;
			}
			if (ev != null && !ev.Wait(5000)) {
				err = "等确认超时";
				return false;
			}
		}
		finally {
			if (key != null) {
				lock (wgate) waits.Remove(key);
				try { ev?.Dispose(); } catch { }
			}
		}
		say($"发送 {topic}  {payload?.Length ?? 0} B  QoS{qos}{(retain ? " 保留" : "")}");
		return true;
	}

	void recv() {
		var why = "";
		try {
			while (running) {
				var p = MqttProto.readpacket(ns, 1000);
				if (p == null) {
					if (!running) break;
					// 读超时不算断；真断了这里会反复拿到 null，靠心跳时间兜底
					if (Environment.TickCount - lastrecv > Keepalive * 1500L + 5000) {
						why = "心跳超时";
						break;
					}
					continue;
				}
				lastrecv = Environment.TickCount;
				if (p.Type == 0) {
					why = "报文错误：" + MqttProto.text(p.Body);
					break;
				}
				switch (p.Type) {
				case MqttProto.PUBLISH: {
					var pi = MqttProto.parsepublish(p);
					if (pi.Error.Length > 0) { say("PUBLISH 解析失败：" + pi.Error); continue; }
					// 收到的 QoS 都要回确认，不回服务器会重发
					if (pi.Qos == 1) write(MqttProto.ack(MqttProto.PUBACK, pi.PacketId));
					else if (pi.Qos == 2) {
						write(MqttProto.ack(MqttProto.PUBREC, pi.PacketId));
						write(MqttProto.puback(MqttProto.PUBREL, pi.PacketId));
					}
					try { Message?.Invoke(pi.Topic, pi.Payload, pi.Qos, pi.Retain); } catch { }
					break;
				}
				case MqttProto.PUBREL:
					write(MqttProto.ack(MqttProto.PUBCOMP, MqttProto.readpacketid(p)));
					break;
				case MqttProto.PUBACK:
				case MqttProto.PUBREC:
				case MqttProto.PUBCOMP:
					release(p.Type, MqttProto.readpacketid(p));
					break;
				case MqttProto.SUBACK:
					// 正文 = 报文标识(2 字节) + 每个主题一个返回码
					if (p.Body.Length > 2) {
						var codes = new List<string>();
						for (var i = 2; i < p.Body.Length; i++)
							codes.Add(p.Body[i] == 0x80 ? "失败" : "QoS" + p.Body[i]);
						say("订阅已确认  " + string.Join(" ", codes));
					}
					break;
				case MqttProto.UNSUBACK:
					say("已取消订阅");
					break;
				case MqttProto.PINGRESP:
					break;
				default:
					say("收到 " + MqttProto.typename(p.Type));
					break;
				}
			}
		}
		catch (Exception ex) {
			if (running) why = ex.Message;
		}
		finally {
			var wasRunning = running;
			running = false;
			Connected = false;
			close();
			if (wasRunning) {
				say("连接已断开" + (why.Length > 0 ? "：" + why : ""));
				try { Lost?.Invoke(why); } catch { }
			}
		}
	}

	/// <summary>放行正在等这条确认的发布。</summary>
	void release(byte type, int id) {
		var key = type + ":" + id;
		ManualResetEventSlim ev = null;
		lock (wgate) waits.TryGetValue(key, out ev);
		try { ev?.Set(); } catch { }
	}

	void ping() {
		// 保活的一半发一次，最少 5 秒
		var ms = Math.Max(5000, Keepalive * 500);
		while (running) {
			for (var i = 0; i < ms / 100 && running; i++) Thread.Sleep(100);
			if (!running) break;
			write(MqttProto.pingreq());
		}
	}

	bool write(byte[] pkt) {
		if (pkt == null || ns == null) return false;
		try {
			lock (wgate) {
				ns.Write(pkt, 0, pkt.Length);
				ns.Flush();
			}
			return true;
		}
		catch { return false; }
	}

	void close() {
		try { ns?.Dispose(); } catch { }
		try { tcp?.Close(); } catch { }
		ns = null;
		tcp = null;
		Connected = false;
		List<ManualResetEventSlim> all;
		lock (wgate) {
			all = new List<ManualResetEventSlim>(waits.Values);
			waits.Clear();
		}
		foreach (var ev in all) {
			try { ev.Set(); } catch { }
		}
	}

	int NextId() {
		var v = Interlocked.Increment(ref nextid) & 0xFFFF;
		return v == 0 ? 1 : v;
	}

	void say(string msg) {
		try { Log?.Invoke(msg); } catch { }
	}
}
