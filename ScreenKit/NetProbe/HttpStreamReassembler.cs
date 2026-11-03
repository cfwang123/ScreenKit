using System.Text;

namespace ScreenKit;

/// <summary>按 TCP Seq 重组单向流，并切分 HTTP/1.x 明文消息。</summary>
static class HttpStreamReassembler {
	public sealed class HttpMessage {
		public string Direction { get; set; } // Client→Server / Server→Client
		public string FirstLine { get; set; }
		public string Headers { get; set; }
		public string BodyPreview { get; set; }
		public int BodyLength { get; set; }
		public string FullText { get; set; }
	}

	/// <summary>从连接报文列表重组 HTTP 流文本（两侧分别排序拼接后切消息）。</summary>
	public static List<HttpMessage> Reassemble(IList<PacketRow> packets, string localIp, int localPort) {
		List<HttpMessage> result = new();
		if (packets == null || packets.Count == 0) return result;

		// 出站：本机端口为源；入站：本机端口为目的
		var outPkts = new List<PacketRow>();
		var inPkts = new List<PacketRow>();
		foreach (var p in packets) {
			if (p?.Payload == null || p.Payload.Length == 0) continue;
			var isOut = string.Equals(p.Src, localIp, StringComparison.OrdinalIgnoreCase) && p.SrcPort == localPort;
			var isIn = string.Equals(p.Dst, localIp, StringComparison.OrdinalIgnoreCase) && p.DstPort == localPort;
			if (isOut) outPkts.Add(p);
			else if (isIn) inPkts.Add(p);
			else {
				// 无本机 IP 时按方向字段
				if (p.Direction == PacketDirection.Out) outPkts.Add(p);
				else if (p.Direction == PacketDirection.In) inPkts.Add(p);
				else outPkts.Add(p); // 兜底
			}
		}

		result.AddRange(splitStream(mergeBySeq(outPkts), "Client → Server"));
		result.AddRange(splitStream(mergeBySeq(inPkts), "Server → Client"));
		return result;
	}

	/// <summary>Tab2 全局列表：无 local 信息时按源/目的对两侧重组。</summary>
	public static List<HttpMessage> ReassembleBidirectional(IList<PacketRow> packets) {
		List<HttpMessage> result = new();
		if (packets == null || packets.Count == 0) return result;
		// 取第一条推断 a/b 端点
		var first = packets[0];
		var aIp = first.Src;
		var aPort = first.SrcPort;
		var sideA = new List<PacketRow>();
		var sideB = new List<PacketRow>();
		foreach (var p in packets) {
			if (p?.Payload == null || p.Payload.Length == 0) continue;
			if (string.Equals(p.Src, aIp, StringComparison.OrdinalIgnoreCase) && p.SrcPort == aPort)
				sideA.Add(p);
			else
				sideB.Add(p);
		}
		result.AddRange(splitStream(mergeBySeq(sideA), $"{aIp}:{aPort} →"));
		result.AddRange(splitStream(mergeBySeq(sideB), $"→ {aIp}:{aPort}"));
		return result;
	}

	static byte[] mergeBySeq(List<PacketRow> pkts) {
		if (pkts.Count == 0) return Array.Empty<byte>();
		pkts.Sort((x, y) => {
			var c = x.Seq.CompareTo(y.Seq);
			return c != 0 ? c : x.Time.CompareTo(y.Time);
		});
		// 简单拼接（不去重重传，够用）
		using var ms = new System.IO.MemoryStream();
		foreach (var p in pkts) {
			if (p.Payload != null && p.Payload.Length > 0)
				ms.Write(p.Payload, 0, p.Payload.Length);
		}
		return ms.ToArray();
	}

	static List<HttpMessage> splitStream(byte[] stream, string dir) {
		List<HttpMessage> list = new();
		if (stream == null || stream.Length == 0) return list;
		var text = Encoding.UTF8.GetString(stream);
		// 兼容 CRLF
		text = text.Replace("\r\n", "\n");
		var idx = 0;
		while (idx < text.Length) {
			// 找消息头结束
			var sep = text.IndexOf("\n\n", idx, StringComparison.Ordinal);
			if (sep < 0) {
				var rest = text.Substring(idx).Trim();
				if (rest.Length > 0 && looksHttp(rest)) {
					list.Add(new HttpMessage {
						Direction = dir,
						FirstLine = firstLine(rest),
						Headers = rest,
						BodyPreview = "",
						BodyLength = 0,
						FullText = rest
					});
				}
				break;
			}
			var head = text.Substring(idx, sep - idx);
			if (!looksHttp(head)) {
				// 非 HTTP 明文：整段作为预览
				if (list.Count == 0) {
					var preview = text.Length > 2000 ? text.Substring(0, 2000) + "…" : text;
					list.Add(new HttpMessage {
						Direction = dir,
						FirstLine = "(非 HTTP 明文载荷)",
						Headers = "",
						BodyPreview = preview,
						BodyLength = stream.Length,
						FullText = preview
					});
				}
				break;
			}
			var bodyStart = sep + 2;
			var contentLen = parseContentLength(head);
			string body;
			if (contentLen >= 0 && bodyStart + contentLen <= text.Length) {
				body = text.Substring(bodyStart, contentLen);
				idx = bodyStart + contentLen;
			} else {
				// 无 Content-Length：取到下一请求/响应行或结尾
				var next = findNextMessage(text, bodyStart);
				body = text.Substring(bodyStart, next - bodyStart);
				idx = next;
			}
			var bodyPrev = body.Length > 800 ? body.Substring(0, 800) + "…" : body;
			list.Add(new HttpMessage {
				Direction = dir,
				FirstLine = firstLine(head),
				Headers = head,
				BodyPreview = bodyPrev,
				BodyLength = body.Length,
				FullText = head + "\n\n" + bodyPrev
			});
			if (list.Count > 200) break;
		}
		return list;
	}

	static bool looksHttp(string s) {
		if (string.IsNullOrEmpty(s)) return false;
		var line = firstLine(s);
		if (line.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase)) return true;
		if (line.StartsWith("GET ", StringComparison.OrdinalIgnoreCase)) return true;
		if (line.StartsWith("POST ", StringComparison.OrdinalIgnoreCase)) return true;
		if (line.StartsWith("PUT ", StringComparison.OrdinalIgnoreCase)) return true;
		if (line.StartsWith("HEAD ", StringComparison.OrdinalIgnoreCase)) return true;
		if (line.StartsWith("DELETE ", StringComparison.OrdinalIgnoreCase)) return true;
		if (line.StartsWith("OPTIONS ", StringComparison.OrdinalIgnoreCase)) return true;
		if (line.StartsWith("PATCH ", StringComparison.OrdinalIgnoreCase)) return true;
		if (line.StartsWith("CONNECT ", StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}

	static string firstLine(string s) {
		var i = s.IndexOf('\n');
		return i < 0 ? s.Trim() : s.Substring(0, i).Trim();
	}

	static int parseContentLength(string headers) {
		foreach (var line in headers.Split('\n')) {
			var t = line.Trim();
			if (t.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) {
				var v = t.Substring("Content-Length:".Length).Trim();
				if (int.TryParse(v, out var n) && n >= 0) return n;
			}
		}
		return -1;
	}

	static int findNextMessage(string text, int from) {
		for (var i = from; i < text.Length - 4; i++) {
			if (text[i] != '\n') continue;
			var slice = text.Substring(i + 1, Math.Min(16, text.Length - i - 1));
			if (looksHttp(slice)) return i + 1;
		}
		return text.Length;
	}
}
