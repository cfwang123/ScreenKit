using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ScreenKit;

/// <summary>
/// MQTT 3.1.1（也认 3.1 的 MQIsdp）报文编解码，纯字节实现，不依赖任何 MQTT 库。
/// 只做本工具用得到的那几种：连接、发布、订阅、确认和心跳。
/// </summary>
static class MqttProto {
	public const byte CONNECT = 1;
	public const byte CONNACK = 2;
	public const byte PUBLISH = 3;
	public const byte PUBACK = 4;
	public const byte PUBREC = 5;
	public const byte PUBREL = 6;
	public const byte PUBCOMP = 7;
	public const byte SUBSCRIBE = 8;
	public const byte SUBACK = 9;
	public const byte UNSUBSCRIBE = 10;
	public const byte UNSUBACK = 11;
	public const byte PINGREQ = 12;
	public const byte PINGRESP = 13;
	public const byte DISCONNECT = 14;

	public const byte CONNACK_ACCEPTED = 0;
	public const byte CONNACK_BADPROTO = 1;
	public const byte CONNACK_BADID = 2;
	public const byte CONNACK_UNAVAILABLE = 3;
	public const byte CONNACK_BADAUTH = 4;
	public const byte CONNACK_NOAUTH = 5;

	/// <summary>MQTT 字符串上限 65535 字节；超过就得截断或换短串。</summary>
	public const int MAXSTRING = 0xFFFF;
	/// <summary>剩余长度最多 4 字节，即 268435455。</summary>
	public const int MAXREMAIN = 268435455;

	/// <summary>一条完整报文。Type 已去掉标志位，Flags 保留高 4 位（PUBLISH 的 QoS/保留位在这里）。</summary>
	public sealed class Packet {
		public byte Type;
		public byte Flags;
		public byte[] Body = Array.Empty<byte>();

		public int Qos => (Flags >> 1) & 0x03;
		public bool Dup => (Flags & 0x08) != 0;
		public bool Retain => (Flags & 0x01) != 0;
		public override string ToString() => nameof(Type) + "=" + typename(Type) + " len=" + Body.Length;
	}

	public static string typename(byte type) => type switch {
		CONNECT => "CONNECT",
		CONNACK => "CONNACK",
		PUBLISH => "PUBLISH",
		PUBACK => "PUBACK",
		PUBREC => "PUBREC",
		PUBREL => "PUBREL",
		PUBCOMP => "PUBCOMP",
		SUBSCRIBE => "SUBSCRIBE",
		SUBACK => "SUBACK",
		UNSUBSCRIBE => "UNSUBSCRIBE",
		UNSUBACK => "UNSUBACK",
		PINGREQ => "PINGREQ",
		PINGRESP => "PINGRESP",
		DISCONNECT => "DISCONNECT",
		_ => "?" + type,
	};

	// ==================== 编码 ====================

	/// <summary>按「固定头 + 剩余长度 + 正文」拼一条报文。</summary>
	public static byte[] pack(byte type, byte flags, byte[] body) {
		body ??= Array.Empty<byte>();
		var head = new byte[] { (byte)((type << 4) | (flags & 0x0F)) };
		var len = new List<byte>(4);
		var n = body.Length;
		do {
			var b = (byte)(n % 128);
			n /= 128;
			if (n > 0) b |= 0x80;
			len.Add(b);
		} while (n > 0);
		var outbuf = new byte[1 + len.Count + body.Length];
		outbuf[0] = head[0];
		len.CopyTo(outbuf, 1);
		Buffer.BlockCopy(body, 0, outbuf, 1 + len.Count, body.Length);
		return outbuf;
	}

	public static byte[] connect(string clientId, string user, string pass, int keepalive, bool clean, string willTopic,
		string willMsg, byte willQos, bool willRetain) {
		var b = new List<byte>();
		addstring(b, "MQTT");
		b.Add(4); // 3.1.1
		byte flags = 0;
		if (clean) flags |= 0x02;
		var hasWill = !string.IsNullOrEmpty(willTopic);
		if (hasWill) flags |= (byte)(0x04 | (willQos & 0x03) << 3);
		if (hasWill && willRetain) flags |= 0x20;
		if (!string.IsNullOrEmpty(user)) flags |= 0x80;
		if (!string.IsNullOrEmpty(pass)) flags |= 0x40;
		b.Add(flags);
		addshort(b, keepalive);
		addstring(b, clientId ?? "");
		if (hasWill) {
			addstring(b, willTopic);
			addstring(b, willMsg ?? "");
		}
		if (!string.IsNullOrEmpty(user)) addstring(b, user);
		if (!string.IsNullOrEmpty(pass)) addstring(b, pass);
		return pack(CONNECT, 0, b.ToArray());
	}

	public static byte[] connack(byte code, bool sessionPresent) =>
		pack(CONNACK, 0, new[] { (byte)(sessionPresent ? 1 : 0), code });

	public static byte[] publish(string topic, byte[] payload, int qos, bool retain, bool dup, int packetId) {
		var b = new List<byte>();
		addstring(b, topic ?? "");
		if (qos > 0) addshort(b, packetId & 0xFFFF);
		if (payload != null) b.AddRange(payload);
		byte flags = (byte)(((qos & 0x03) << 1) | (retain ? 1 : 0) | (dup ? 8 : 0));
		return pack(PUBLISH, flags, b.ToArray());
	}

	public static byte[] puback(byte type, int packetId) => pack(type, (byte)(type == PUBREL ? 0x02 : 0), new[] { (byte)((packetId >> 8) & 0xFF), (byte)(packetId & 0xFF) });

	public static byte[] subscribe(int packetId, IList<string> topics, IList<byte> qos) {
		var b = new List<byte>();
		addshort(b, packetId & 0xFFFF);
		for (var i = 0; i < topics.Count; i++) {
			addstring(b, topics[i]);
			b.Add((byte)(i < qos.Count ? qos[i] & 0x03 : 0));
		}
		return pack(SUBSCRIBE, 0x02, b.ToArray());
	}

	public static byte[] suback(int packetId, IList<byte> qos) {
		var b = new List<byte>();
		addshort(b, packetId & 0xFFFF);
		foreach (var q in qos) b.Add(q > 2 ? (byte)0x80 : q);
		return pack(SUBACK, 0, b.ToArray());
	}

	public static byte[] unsubscribe(int packetId, IList<string> topics) {
		var b = new List<byte>();
		addshort(b, packetId & 0xFFFF);
		foreach (var t in topics) addstring(b, t);
		return pack(UNSUBSCRIBE, 0x02, b.ToArray());
	}

	public static byte[] unsuback(int packetId) => pack(UNSUBACK, 0, new[] { (byte)((packetId >> 8) & 0xFF), (byte)(packetId & 0xFF) });

	public static byte[] pingreq() => pack(PINGREQ, 0, null);

	public static byte[] pingresp() => pack(PINGRESP, 0, null);

	public static byte[] disconnect() => pack(DISCONNECT, 0, null);

	public static byte[] ack(byte type, int packetId) => pack(type, 0, new[] { (byte)((packetId >> 8) & 0xFF), (byte)(packetId & 0xFF) });

	static void addstring(List<byte> b, string s) {
		var raw = Encoding.UTF8.GetBytes(s ?? "");
		if (raw.Length > MAXSTRING) Array.Resize(ref raw, MAXSTRING);
		addshort(b, raw.Length);
		b.AddRange(raw);
	}

	static void addshort(List<byte> b, int v) {
		b.Add((byte)((v >> 8) & 0xFF));
		b.Add((byte)(v & 0xFF));
	}

	// ==================== 解码 ====================

	/// <summary>连接包里的全部字段。</summary>
	public sealed class ConnectInfo {
		public string Protocol = "";
		public byte Level;
		public bool Clean;
		public int Keepalive;
		public string ClientId = "";
		public string WillTopic = "";
		public string WillMessage = "";
		public byte WillQos;
		public bool WillRetain;
		public string UserName = "";
		public string Password = "";
		public string Error = "";
	}

	public sealed class PublishInfo {
		public string Topic = "";
		public byte[] Payload = Array.Empty<byte>();
		public int Qos;
		public bool Retain;
		public bool Dup;
		public int PacketId;
		public string Error = "";
	}

	/// <summary>从流里读一条报文。超时或对端断开返回 null；报文坏掉时 Type=0 且 Error 有值。</summary>
	public static Packet readpacket(Stream s, int timeoutMs) {
		var t0 = Environment.TickCount;
		var h = readbyte(s, t0, timeoutMs);
		if (h < 0) return null;
		var mult = 1;
		var len = 0;
		for (var i = 0; i < 4; i++) {
			var b = readbyte(s, t0, timeoutMs);
			if (b < 0) return null;
			len += (b & 0x7F) * mult;
			if ((b & 0x80) == 0) break;
			mult *= 128;
			if (i == 3) return new Packet { Type = 0, Body = Encoding.UTF8.GetBytes("剩余长度超过 4 字节") };
		}
		if (len < 0) return new Packet { Type = 0, Body = Encoding.UTF8.GetBytes("剩余长度非法") };
		var body = new byte[len];
		var got = 0;
		while (got < len) {
			var n = readsome(s, body, got, len - got, t0, timeoutMs);
			if (n <= 0) return null;
			got += n;
		}
		return new Packet { Type = (byte)(h >> 4), Flags = (byte)(h & 0x0F), Body = body };
	}

	static int readbyte(Stream s, int t0, int timeoutMs) {
		var one = new byte[1];
		var n = readsome(s, one, 0, 1, t0, timeoutMs);
		return n <= 0 ? -1 : one[0];
	}

	static int readsome(Stream s, byte[] buf, int off, int count, int t0, int timeoutMs) {
		while (Environment.TickCount - t0 < timeoutMs) {
			try {
				if (s is NetworkStream ns) {
					if (!ns.DataAvailable) { Thread.Sleep(5); continue; }
				}
				var n = s.Read(buf, off, count);
				if (n > 0) return n;
				return -1;
			}
			catch (IOException) { return -1; }
			catch (ObjectDisposedException) { return -1; }
			catch (SocketException) { return -1; }
		}
		return 0;
	}

	public static ConnectInfo parseconnect(Packet p) {
		var r = new ConnectInfo();
		if (p == null || p.Type != CONNECT) {
			r.Error = "不是 CONNECT";
			return r;
		}
		var o = 0;
		if (!readstring(p.Body, ref o, out var proto, out r.Error)) return r;
		r.Protocol = proto;
		if (o >= p.Body.Length) { r.Error = "CONNECT 太短"; return r; }
		r.Level = p.Body[o++];
		if (o >= p.Body.Length) { r.Error = "CONNECT 太短"; return r; }
		var flags = p.Body[o++];
		if (o + 2 > p.Body.Length) { r.Error = "CONNECT 太短"; return r; }
		r.Keepalive = (p.Body[o] << 8) | p.Body[o + 1];
		o += 2;
		if (!readstring(p.Body, ref o, out r.ClientId, out r.Error)) return r;
		if ((flags & 0x04) != 0) {
			if (!readstring(p.Body, ref o, out r.WillTopic, out r.Error)) return r;
			if (!readstring(p.Body, ref o, out r.WillMessage, out r.Error)) return r;
			r.WillQos = (byte)((flags >> 3) & 0x03);
			r.WillRetain = (flags & 0x20) != 0;
		}
		if ((flags & 0x80) != 0 && !readstring(p.Body, ref o, out r.UserName, out r.Error)) return r;
		if ((flags & 0x40) != 0 && !readstring(p.Body, ref o, out r.Password, out r.Error)) return r;
		r.Clean = (flags & 0x02) != 0;
		return r;
	}

	public static PublishInfo parsepublish(Packet p) {
		var r = new PublishInfo();
		if (p == null || p.Type != PUBLISH) {
			r.Error = "不是 PUBLISH";
			return r;
		}
		r.Qos = p.Qos;
		r.Retain = p.Retain;
		r.Dup = p.Dup;
		var o = 0;
		if (!readstring(p.Body, ref o, out var topic, out r.Error)) return r;
		r.Topic = topic;
		if (r.Qos > 0) {
			if (o + 2 > p.Body.Length) { r.Error = "PUBLISH 缺报文标识"; return r; }
			r.PacketId = (p.Body[o] << 8) | p.Body[o + 1];
			o += 2;
		}
		r.Payload = new byte[p.Body.Length - o];
		Buffer.BlockCopy(p.Body, o, r.Payload, 0, r.Payload.Length);
		return r;
	}

	/// <summary>SUBSCRIBE 的正文：报文标识 + 若干「主题 + QoS」。</summary>
	public static bool parsesubscribe(Packet p, out int packetId, List<(string topic, byte qos)> list, out string err) {
		packetId = 0;
		err = "";
		list ??= new List<(string, byte)>();
		if (p == null || p.Body.Length < 2) { err = "SUBSCRIBE 太短"; return false; }
		packetId = (p.Body[0] << 8) | p.Body[1];
		var o = 2;
		while (o < p.Body.Length) {
			if (!readstring(p.Body, ref o, out var topic, out err)) return false;
			if (o >= p.Body.Length) { err = "SUBSCRIBE 缺 QoS"; return false; }
			list.Add((topic, p.Body[o++]));
		}
		return true;
	}

	public static bool parseunsubscribe(Packet p, out int packetId, List<string> topics, out string err) {
		packetId = 0;
		err = "";
		if (p == null || p.Body.Length < 2) { err = "UNSUBSCRIBE 太短"; return false; }
		packetId = (p.Body[0] << 8) | p.Body[1];
		var o = 2;
		while (o < p.Body.Length) {
			if (!readstring(p.Body, ref o, out var topic, out err)) return false;
			topics.Add(topic);
		}
		return true;
	}

	public static int readpacketid(Packet p) {
		if (p == null || p.Body.Length < 2) return 0;
		return (p.Body[0] << 8) | p.Body[1];
	}

	static bool readstring(byte[] buf, ref int o, out string s, out string err) {
		s = "";
		err = "";
		if (o + 2 > buf.Length) { err = "字符串长度越界"; return false; }
		var n = (buf[o] << 8) | buf[o + 1];
		o += 2;
		if (o + n > buf.Length) { err = "字符串越界"; return false; }
		s = Encoding.UTF8.GetString(buf, o, n);
		o += n;
		return true;
	}

	// ==================== 主题匹配 ====================

	/// <summary>
	/// 按 MQTT 规则判断主题名是否命中过滤器：<c>+</c> 吃掉一层，<c>#</c> 吃掉后面所有层（只能在末尾）。
	/// 以 <c>$</c> 开头的主题不被 <c>#</c> 或 <c>+</c> 开头的过滤器命中。
	/// </summary>
	public static bool topicmatch(string filter, string topic) {
		if (string.IsNullOrEmpty(filter) || topic == null) return false;
		if (topic.StartsWith("$", StringComparison.Ordinal)
			&& (filter.StartsWith("#", StringComparison.Ordinal) || filter.StartsWith("+", StringComparison.Ordinal)))
			return false;
		var f = filter.Split('/');
		var t = topic.Split('/');
		for (var i = 0; i < f.Length; i++) {
			if (f[i] == "#") return i == f.Length - 1;
			if (i >= t.Length) return false;
			if (f[i] == "+") continue;
			if (!string.Equals(f[i], t[i], StringComparison.Ordinal)) return false;
		}
		return f.Length == t.Length;
	}

	/// <summary>粗略校验：非空、无通配符时不允许含 + 和 #，长度不超 65535。</summary>
	public static bool validtopic(string topic, bool wildcard) {
		if (string.IsNullOrEmpty(topic)) return false;
		if (Encoding.UTF8.GetByteCount(topic) > MAXSTRING) return false;
		if (!wildcard && (topic.IndexOf('+') >= 0 || topic.IndexOf('#') >= 0)) return false;
		if (wildcard) {
			var hash = topic.IndexOf('#');
			if (hash >= 0 && hash != topic.Length - 1) return false;
			if (hash >= 0 && hash > 0 && topic[hash - 1] != '/') return false;
			var plus = topic.IndexOf('+');
			while (plus >= 0) {
				var leftOk = plus == 0 || topic[plus - 1] == '/';
				var rightOk = plus == topic.Length - 1 || topic[plus + 1] == '/';
				if (!leftOk || !rightOk) return false;
				plus = topic.IndexOf('+', plus + 1);
			}
		}
		return true;
	}

	public static string text(byte[] data) {
		if (data == null || data.Length == 0) return "";
		return Encoding.UTF8.GetString(data);
	}

	/// <summary>正文既可能是文本也可能是二进制：可打印就给文本，否则给十六进制。</summary>
	public static string pretty(byte[] data) {
		if (data == null || data.Length == 0) return "";
		var ok = true;
		foreach (var b in data) {
			if (b == 0) { ok = false; break; }
			if (b < 0x09 || (b > 0x0D && b < 0x20)) { ok = false; break; }
		}
		if (ok) {
			try { return Encoding.UTF8.GetString(data); } catch { }
		}
		return hex(data, 64);
	}

	public static string hex(byte[] data, int limit) {
		if (data == null || data.Length == 0) return "";
		var n = Math.Min(data.Length, limit);
		var sb = new StringBuilder(n * 3);
		for (var i = 0; i < n; i++) {
			if (i > 0) sb.Append(' ');
			sb.Append(data[i].ToString("X2"));
		}
		if (n < data.Length) sb.Append(" …(" + data.Length + " B)");
		return sb.ToString();
	}

	/// <summary>把「AA BB CC」或「AABBCC」还原成字节；返回空表示输入不合法。</summary>
	public static byte[] fromhex(string s) {
		if (string.IsNullOrWhiteSpace(s)) return Array.Empty<byte>();
		var sb = new StringBuilder(s.Length);
		foreach (var c in s) {
			if (char.IsWhiteSpace(c) || c == '-' || c == ',') continue;
			if (!Uri.IsHexDigit(c)) return null;
			sb.Append(c);
		}
		if (sb.Length % 2 != 0) return null;
		var outbuf = new byte[sb.Length / 2];
		for (var i = 0; i < outbuf.Length; i++)
			outbuf[i] = Convert.ToByte(sb.ToString(i * 2, 2), 16);
		return outbuf;
	}
}
