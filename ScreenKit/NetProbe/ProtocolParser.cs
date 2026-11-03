using System.Net;
using System.Text;

namespace ScreenKit;

/// <summary>
/// 从多种链路层格式解析 IP/TCP。
/// 支持以太网、Npcap Loopback(Null)、Raw IP；数据不完整时仍尽量输出 TCP 行（可不做应用层解析）。
/// </summary>
static class ProtocolParser {
	// pcap link types
	const int DLT_NULL = 0;
	const int DLT_EN10MB = 1;
	const int DLT_RAW = 12;
	const int DLT_RAW_ALT = 14;
	const int DLT_LINUX_SLL = 113;
	const int DLT_LOOP = 108;
	const int DLT_IPV4 = 228;
	const int DLT_IPV6 = 229;

	/// <summary>解析任意 TCP（自动探测链路类型）。</summary>
	public static bool TryParseAnyTcp(byte[] frame, out PacketRow row) =>
		TryParseAnyTcp(frame, -1, out row);

	/// <summary>按指定链路类型解析 TCP。</summary>
	public static bool TryParseAnyTcp(byte[] frame, int linkType, out PacketRow row) {
		if (!TryParseAnyIp(frame, linkType, out row)) return false;
		// 仅 TCP 族
		var p = row.Protocol ?? "";
		if (p.Equals("UDP", StringComparison.OrdinalIgnoreCase) ||
		    p.Equals("QUIC", StringComparison.OrdinalIgnoreCase)) {
			row = null;
			return false;
		}
		return true;
	}

	/// <summary>解析 TCP 或 UDP（自动/指定链路类型）。</summary>
	public static bool TryParseAnyIp(byte[] frame, out PacketRow row) =>
		TryParseAnyIp(frame, -1, out row);

	public static bool TryParseAnyIp(byte[] frame, int linkType, out PacketRow row) {
		row = null;
		if (frame == null || frame.Length < 20) return false;

		List<ProtoNode> tree = new();
		var offset = 0;
		var linkName = "Auto";

		if (linkType >= 0) {
			if (!skipLinkHeader(frame, linkType, tree, out offset, out linkName)) {
				if (!autoSkipLink(frame, tree, out offset, out linkName))
					return false;
			}
		} else if (!autoSkipLink(frame, tree, out offset, out linkName)) {
			return false;
		}

		return parseIpL4(frame, offset, tree, out row);
	}

	static bool skipLinkHeader(byte[] frame, int linkType, List<ProtoNode> tree,
		out int offset, out string linkName) {
		offset = 0;
		linkName = $"Link({linkType})";
		switch (linkType) {
			case DLT_NULL:
			case DLT_LOOP:
				if (frame.Length < 4 + 20) return false;
				offset = 4;
				linkName = "Null/Loopback";
				tree.Add(ProtoNode.Layer(linkName, 0, 4));
				return true;
			case DLT_EN10MB:
				return skipEthernet(frame, tree, out offset, out linkName);
			case DLT_RAW:
			case DLT_RAW_ALT:
			case DLT_IPV4:
			case DLT_IPV6:
				offset = 0;
				linkName = "Raw IP";
				tree.Add(ProtoNode.Layer(linkName, 0, 0));
				return true;
			case DLT_LINUX_SLL:
				if (frame.Length < 16 + 20) return false;
				offset = 16;
				linkName = "Linux SLL";
				tree.Add(ProtoNode.Layer(linkName, 0, 16));
				return true;
			default:
				return false;
		}
	}

	static bool autoSkipLink(byte[] frame, List<ProtoNode> tree, out int offset, out string linkName) {
		offset = 0;
		linkName = "Auto";
		if (frame.Length >= 34 && looksLikeEthernet(frame))
			return skipEthernet(frame, tree, out offset, out linkName);
		if (isIpv4(frame, 0) || isIpv6(frame, 0)) {
			offset = 0;
			linkName = "Raw IP";
			tree.Add(ProtoNode.Layer(linkName, 0, 0));
			return true;
		}
		if (frame.Length >= 24 && (isIpv4(frame, 4) || isIpv6(frame, 4))) {
			offset = 4;
			linkName = "Null/Loopback";
			tree.Add(ProtoNode.Layer(linkName, 0, 4));
			return true;
		}
		if (frame.Length >= 36 && (isIpv4(frame, 16) || isIpv6(frame, 16))) {
			offset = 16;
			linkName = "Linux SLL";
			tree.Add(ProtoNode.Layer(linkName, 0, 16));
			return true;
		}
		return false;
	}

	static bool looksLikeEthernet(byte[] f) {
		if (f.Length < 14) return false;
		var ethType = (f[12] << 8) | f[13];
		if (ethType is 0x0800 or 0x86DD or 0x8100) return true;
		// VLAN
		if (ethType == 0x8100 && f.Length >= 18) {
			var t2 = (f[16] << 8) | f[17];
			return t2 is 0x0800 or 0x86DD;
		}
		return false;
	}

	static bool skipEthernet(byte[] frame, List<ProtoNode> tree, out int offset, out string linkName) {
		offset = 0;
		linkName = "Ethernet";
		if (frame.Length < 14) return false;
		var ethType = (frame[12] << 8) | frame[13];
		offset = 14;
		var hdrLen = 14;
		if (ethType == 0x8100 && frame.Length >= 18) {
			ethType = (frame[16] << 8) | frame[17];
			offset = 18;
			hdrLen = 18;
		}
		var ethNode = ProtoNode.Layer("Ethernet", 0, hdrLen);
		ethNode.Children.Add(ProtoNode.Field("Destination", mac(frame, 0), 0, 6));
		ethNode.Children.Add(ProtoNode.Field("Source", mac(frame, 6), 6, 6));
		if (hdrLen == 18)
			ethNode.Children.Add(ProtoNode.Field("VLAN/Type", $"0x{ethType:X4}", 12, 6));
		else
			ethNode.Children.Add(ProtoNode.Field("Type", $"0x{ethType:X4}", 12, 2));
		tree.Add(ethNode);
		if (ethType is not (0x0800 or 0x86DD)) return false;
		return true;
	}

	static string mac(byte[] f, int o) =>
		$"{f[o]:X2}:{f[o + 1]:X2}:{f[o + 2]:X2}:{f[o + 3]:X2}:{f[o + 4]:X2}:{f[o + 5]:X2}";

	static bool isIpv4(byte[] f, int o) =>
		f.Length >= o + 20 && (f[o] >> 4) == 4;

	static bool isIpv6(byte[] f, int o) =>
		f.Length >= o + 40 && (f[o] >> 4) == 6;

	static bool parseIpL4(byte[] frame, int offset, List<ProtoNode> tree, out PacketRow row) {
		row = null;
		if (offset < 0 || frame.Length < offset + 20) return false;

		string srcIp, dstIp;
		int protocol;
		var ipStart = offset;

		var ver = frame[offset] >> 4;
		if (ver == 4) {
			if (!isIpv4(frame, offset)) return false;
			var ipHeaderLen = (frame[offset] & 0x0F) * 4;
			if (ipHeaderLen < 20 || frame.Length < offset + ipHeaderLen) return false;
			protocol = frame[offset + 9];
			srcIp = $"{frame[offset + 12]}.{frame[offset + 13]}.{frame[offset + 14]}.{frame[offset + 15]}";
			dstIp = $"{frame[offset + 16]}.{frame[offset + 17]}.{frame[offset + 18]}.{frame[offset + 19]}";
			var totalLen = (frame[offset + 2] << 8) | frame[offset + 3];
			var ipn = ProtoNode.Layer("Internet Protocol Version 4", ipStart, ipHeaderLen);
			ipn.Children.Add(ProtoNode.Field("Version", "4", ipStart, 1));
			ipn.Children.Add(ProtoNode.Field("Header Length", $"{ipHeaderLen} bytes", ipStart, 1));
			ipn.Children.Add(ProtoNode.Field("Total Length", totalLen.ToString(), ipStart + 2, 2));
			ipn.Children.Add(ProtoNode.Field("Protocol", protocol.ToString(), ipStart + 9, 1));
			ipn.Children.Add(ProtoNode.Field("Source", srcIp, ipStart + 12, 4));
			ipn.Children.Add(ProtoNode.Field("Destination", dstIp, ipStart + 16, 4));
			tree.Add(ipn);
			offset += ipHeaderLen;
		} else if (ver == 6) {
			if (!isIpv6(frame, offset)) return false;
			protocol = frame[offset + 6];
			srcIp = formatV6(frame, offset + 8);
			dstIp = formatV6(frame, offset + 24);
			var ipn = ProtoNode.Layer("Internet Protocol Version 6", ipStart, 40);
			ipn.Children.Add(ProtoNode.Field("Next Header", protocol.ToString(), ipStart + 6, 1));
			ipn.Children.Add(ProtoNode.Field("Source", srcIp, ipStart + 8, 16));
			ipn.Children.Add(ProtoNode.Field("Destination", dstIp, ipStart + 24, 16));
			tree.Add(ipn);
			offset += 40;
		} else {
			return false;
		}

		// 17 = UDP，6 = TCP
		if (protocol == 17)
			return parseUdp(frame, offset, tree, srcIp, dstIp, out row);
		if (protocol != 6) return false;
		if (frame.Length < offset + 20) return false;

		var tcpStart = offset;
		var srcPort = (frame[offset] << 8) | frame[offset + 1];
		var dstPort = (frame[offset + 2] << 8) | frame[offset + 3];
		var seq = readU32(frame, offset + 4);
		var ack = readU32(frame, offset + 8);
		var dataOff = ((frame[offset + 12] >> 4) & 0xF) * 4;
		var fl = frame[offset + 13];
		var flags = tcpFlags(fl);
		if (dataOff < 20) dataOff = 20;
		if (frame.Length < offset + 13) return false;

		var tcpn = ProtoNode.Layer("Transmission Control Protocol", tcpStart, dataOff);
		tcpn.Children.Add(ProtoNode.Field("Source Port", srcPort.ToString(), tcpStart, 2));
		tcpn.Children.Add(ProtoNode.Field("Destination Port", dstPort.ToString(), tcpStart + 2, 2));
		tcpn.Children.Add(ProtoNode.Field("Sequence Number", seq.ToString(), tcpStart + 4, 4));
		tcpn.Children.Add(ProtoNode.Field("Acknowledgment Number", ack.ToString(), tcpStart + 8, 4));
		tcpn.Children.Add(ProtoNode.Field("Header Length", $"{dataOff} bytes", tcpStart + 12, 1));
		tcpn.Children.Add(ProtoNode.Field("Flags", flags, tcpStart + 13, 1));
		tcpn.Children.Add(ProtoNode.Field("Window", ((frame[tcpStart + 14] << 8) | frame[tcpStart + 15]).ToString(), tcpStart + 14, 2));
		tree.Add(tcpn);

		byte[] payload = Array.Empty<byte>();
		var payOff = offset + dataOff;
		if (payOff < frame.Length) {
			var payLen = frame.Length - payOff;
			payload = new byte[payLen];
			Buffer.BlockCopy(frame, payOff, payload, 0, payLen);
			tcpn.Children.Add(ProtoNode.Field("TCP payload", $"{payLen} bytes", payOff, payLen));
			// 扩展层节点覆盖到帧尾
			tcpn.Length = frame.Length - tcpStart;
		} else {
			tcpn.Children.Add(ProtoNode.Field("TCP payload", "0 bytes", payOff, 0));
		}

		var proto = "TCP";
		var summary = flags;
		if (payload.Length > 0) {
			try {
				var app = parseApp(payload, srcPort, dstPort, tree, payOff);
				if (app != null) {
					proto = app.Value.proto;
					summary = app.Value.summary;
				} else {
					summary = $"{flags}  payload={payload.Length}B";
				}
			} catch {
				summary = $"{flags}  payload={payload.Length}B (解析跳过)";
			}
		}

		return finishTcpRow(frame, tree, srcIp, dstIp, srcPort, dstPort, seq, ack, flags, payload, payOff, proto, summary, out row);
	}

	static bool parseUdp(byte[] frame, int offset, List<ProtoNode> tree, string srcIp, string dstIp, out PacketRow row) {
		row = null;
		if (frame.Length < offset + 8) return false;
		var udpStart = offset;
		var srcPort = (frame[offset] << 8) | frame[offset + 1];
		var dstPort = (frame[offset + 2] << 8) | frame[offset + 3];
		var len = (frame[offset + 4] << 8) | frame[offset + 5];
		var udpn = ProtoNode.Layer("User Datagram Protocol", udpStart, 8);
		udpn.Children.Add(ProtoNode.Field("Source Port", srcPort.ToString(), udpStart, 2));
		udpn.Children.Add(ProtoNode.Field("Destination Port", dstPort.ToString(), udpStart + 2, 2));
		udpn.Children.Add(ProtoNode.Field("Length", len.ToString(), udpStart + 4, 2));
		tree.Add(udpn);
		byte[] payload = Array.Empty<byte>();
		var payOff = offset + 8;
		if (payOff < frame.Length) {
			var payLen = frame.Length - payOff;
			payload = new byte[payLen];
			Buffer.BlockCopy(frame, payOff, payload, 0, payLen);
			udpn.Children.Add(ProtoNode.Field("UDP payload", $"{payLen} bytes", payOff, payLen));
			udpn.Length = frame.Length - udpStart;
		}
		var proto = "UDP";
		var summary = $"UDP {payload.Length}B";
		// QUIC 常见 UDP/443
		if (dstPort == 443 || srcPort == 443 || dstPort == 80 || srcPort == 80) {
			if (payload.Length > 0 && (payload[0] & 0x80) != 0) {
				proto = "QUIC";
				summary = "QUIC/UDP";
			}
		}
		if (srcPort == 53 || dstPort == 53) {
			try {
				var dns = tryDns(payload, payOff, payOff, payload.Length);
				if (dns != null) {
					tree.Add(dns.Value.node);
					proto = "DNS";
					summary = dns.Value.summary;
				}
			} catch { }
		}
		row = new PacketRow {
			Time = DateTime.Now,
			Direction = PacketDirection.Unknown,
			Src = srcIp,
			Dst = dstIp,
			SrcPort = srcPort,
			DstPort = dstPort,
			Length = frame.Length,
			PayloadLength = payload.Length,
			PayloadOffset = payOff,
			Protocol = proto,
			Summary = summary,
			Flags = "UDP",
			Seq = 0,
			Ack = 0,
			Raw = frame,
			Payload = payload,
			Tree = tree
		};
		return true;
	}

	static bool finishTcpRow(byte[] frame, List<ProtoNode> tree, string srcIp, string dstIp,
		int srcPort, int dstPort, uint seq, uint ack, string flags, byte[] payload, int payOff,
		string proto, string summary, out PacketRow row) {
		row = new PacketRow {
			Time = DateTime.Now,
			Direction = PacketDirection.Unknown,
			Src = srcIp,
			Dst = dstIp,
			SrcPort = srcPort,
			DstPort = dstPort,
			Length = frame.Length,
			PayloadLength = payload.Length,
			PayloadOffset = payOff,
			Protocol = proto,
			Summary = summary,
			Flags = flags,
			Seq = seq,
			Ack = ack,
			Raw = frame,
			Payload = payload,
			Tree = tree
		};
		return true;
	}

	/// <summary>按本机/远端补全收发方向。</summary>
	public static void ApplyDirection(PacketRow row, string localIp, int localPort) {
		if (row == null) return;
		if (ipEquals(row.Src, localIp) && row.SrcPort == localPort)
			row.Direction = PacketDirection.Out;
		else if (ipEquals(row.Dst, localIp) && row.DstPort == localPort)
			row.Direction = PacketDirection.In;
		else
			row.Direction = PacketDirection.Unknown;
	}

	public static bool TryParseFrame(
		byte[] frame, string localIp, int localPort, string remoteIp, int remotePort,
		out PacketRow row) {
		if (!TryParseAnyTcp(frame, out row)) return false;
		if (!matchTuple(row.Src, row.SrcPort, row.Dst, row.DstPort, localIp, localPort, remoteIp, remotePort)) {
			row = null;
			return false;
		}
		ApplyDirection(row, localIp, localPort);
		return true;
	}

	static (string proto, string summary)? parseApp(byte[] payload, int srcPort, int dstPort, List<ProtoNode> tree, int frameOff) {
		if (payload.Length >= 5 && payload[0] == 0x16 && payload[1] == 0x03) {
			var tls = parseTls(payload, frameOff);
			tree.Add(tls.node);
			return ("TLS", tls.summary);
		}
		if (payload.Length >= 5 && payload[0] is 0x14 or 0x15 or 0x17 && payload[1] == 0x03) {
			var ct = payload[0] switch {
				0x14 => "ChangeCipherSpec",
				0x15 => "Alert",
				0x17 => "Application Data",
				_ => "Record"
			};
			var n = ProtoNode.Layer("Transport Layer Security", frameOff, payload.Length);
			n.Children.Add(ProtoNode.Field("Content Type", ct, frameOff, 1));
			n.Children.Add(ProtoNode.Field("Version", tlsVer(payload[1], payload[2]), frameOff + 1, 2));
			n.Children.Add(ProtoNode.Field("Length", payload.Length.ToString(), frameOff + 3, 2));
			tree.Add(n);
			return ("TLS", ct);
		}

		var http = tryHttp(payload, frameOff);
		if (http != null) {
			tree.Add(http.Value.node);
			return ("HTTP", http.Value.summary);
		}

		if (srcPort == 53 || dstPort == 53) {
			var dnsOff = frameOff;
			var dnsPayload = payload;
			if (payload.Length > 2) {
				var dlen = (payload[0] << 8) | payload[1];
				if (dlen + 2 <= payload.Length) {
					dnsPayload = new byte[payload.Length - 2];
					Buffer.BlockCopy(payload, 2, dnsPayload, 0, dnsPayload.Length);
					dnsOff = frameOff + 2;
				}
			}
			var dns = tryDns(dnsPayload, dnsOff, frameOff, payload.Length);
			if (dns != null) {
				tree.Add(dns.Value.node);
				return ("DNS", dns.Value.summary);
			}
		}

		if (payload[0] is (byte)'*' or (byte)'+' or (byte)'-' or (byte)'$' or (byte)':') {
			var text = toPrintable(payload, 120);
			var n = ProtoNode.Layer("Redis/RESP or text", frameOff, payload.Length);
			n.Children.Add(ProtoNode.Field("Preview", text, frameOff, Math.Min(payload.Length, 120)));
			tree.Add(n);
			return ("RESP", text);
		}

		if (isMostlyText(payload)) {
			var text = toPrintable(payload, 160);
			var n = ProtoNode.Layer("Data", frameOff, payload.Length);
			n.Children.Add(ProtoNode.Field("Text", text, frameOff, Math.Min(payload.Length, 160)));
			tree.Add(n);
			return ("Data", text);
		}

		var bin = ProtoNode.Layer("Data", frameOff, payload.Length);
		bin.Children.Add(ProtoNode.Field("Binary", $"{payload.Length} bytes", frameOff, payload.Length));
		tree.Add(bin);
		return null;
	}

	static (ProtoNode node, string summary) parseTls(byte[] p, int frameOff) {
		var n = ProtoNode.Layer("Transport Layer Security", frameOff, p.Length);
		n.Children.Add(ProtoNode.Field("Content Type", "Handshake (22)", frameOff, 1));
		if (p.Length >= 5)
			n.Children.Add(ProtoNode.Field("Version", tlsVer(p[1], p[2]), frameOff + 1, 2));
		if (p.Length >= 5)
			n.Children.Add(ProtoNode.Field("Length", ((p[3] << 8) | p[4]).ToString(), frameOff + 3, 2));
		var summary = "TLS Handshake";
		if (p.Length > 5 && p[5] == 0x01) {
			n.Children.Add(ProtoNode.Field("Handshake Type", "Client Hello", frameOff + 5, 1));
			var sni = extractSni(p, out var sniOff, out var sniLen);
			if (!string.IsNullOrEmpty(sni)) {
				n.Children.Add(ProtoNode.Field("Server Name (SNI)", sni, frameOff + sniOff, sniLen));
				summary = $"ClientHello SNI={sni}";
			} else summary = "ClientHello";
		} else if (p.Length > 5 && p[5] == 0x02) {
			n.Children.Add(ProtoNode.Field("Handshake Type", "Server Hello", frameOff + 5, 1));
			summary = "ServerHello";
		}
		return (n, summary);
	}

	static string extractSni(byte[] p, out int off, out int len) {
		off = 0;
		len = 0;
		try {
			if (p.Length < 44 || p[0] != 0x16 || p[5] != 0x01) return "";
			var pos = 5 + 4 + 2 + 32;
			if (pos >= p.Length) return "";
			var sidLen = p[pos++];
			pos += sidLen;
			if (pos + 2 > p.Length) return "";
			var csLen = (p[pos] << 8) | p[pos + 1];
			pos += 2 + csLen;
			if (pos >= p.Length) return "";
			var compLen = p[pos++];
			pos += compLen;
			if (pos + 2 > p.Length) return "";
			var extLen = (p[pos] << 8) | p[pos + 1];
			pos += 2;
			var extEnd = Math.Min(p.Length, pos + extLen);
			while (pos + 4 <= extEnd) {
				var et = (p[pos] << 8) | p[pos + 1];
				var el = (p[pos + 2] << 8) | p[pos + 3];
				pos += 4;
				if (pos + el > p.Length) break;
				if (et == 0x0000 && el >= 5) {
					var q = pos + 2;
					if (q + 3 <= pos + el) {
						var nameType = p[q];
						var nameLen = (p[q + 1] << 8) | p[q + 2];
						q += 3;
						if (nameType == 0 && q + nameLen <= p.Length) {
							off = q;
							len = nameLen;
							return Encoding.ASCII.GetString(p, q, nameLen);
						}
					}
				}
				pos += el;
			}
		} catch { }
		return "";
	}

	static (ProtoNode node, string summary)? tryHttp(byte[] p, int frameOff) {
		if (p.Length < 8) return null;
		var headLen = Math.Min(p.Length, 2048);
		var text = Encoding.ASCII.GetString(p, 0, headLen);
		if (!(text.StartsWith("GET ") || text.StartsWith("POST ") || text.StartsWith("HEAD ") ||
		      text.StartsWith("PUT ") || text.StartsWith("DELETE ") || text.StartsWith("OPTIONS ") ||
		      text.StartsWith("PATCH ") || text.StartsWith("HTTP/")))
			return null;

		// 头部结束位置
		var headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
		if (headerEnd < 0) headerEnd = text.IndexOf("\n\n", StringComparison.Ordinal);
		var hdrBytes = headerEnd >= 0 ? headerEnd + 4 : headLen;
		if (hdrBytes > p.Length) hdrBytes = p.Length;

		var n = ProtoNode.Layer("Hypertext Transfer Protocol", frameOff, hdrBytes);
		var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
		var summary = lines[0].Length > 120 ? lines[0][..120] + "…" : lines[0];
		// 粗略：首行从 0 到第一行结束
		var line0End = lines[0].Length;
		n.Children.Add(ProtoNode.Field("Request/Status Line", lines[0], frameOff, Math.Min(line0End, hdrBytes)));
		var pos = line0End + 2; // \r\n
		for (var i = 1; i < lines.Length; i++) {
			var line = lines[i];
			if (string.IsNullOrEmpty(line)) break;
			var idx = line.IndexOf(':');
			var lineLen = line.Length;
			var abs = frameOff + Math.Min(pos, hdrBytes);
			var take = Math.Min(lineLen, Math.Max(0, hdrBytes - (abs - frameOff)));
			if (idx > 0)
				n.Children.Add(ProtoNode.Field(line[..idx].Trim(), line[(idx + 1)..].Trim(), abs, take));
			else
				n.Children.Add(ProtoNode.Field(line, "", abs, take));
			pos += lineLen + 2;
			if (n.Children.Count > 40) break;
		}
		foreach (var c in n.Children) {
			if (string.Equals(c.Title, "Host", StringComparison.OrdinalIgnoreCase)) {
				summary = $"{lines[0]}  Host={c.Value}";
				break;
			}
		}
		return (n, summary);
	}

	static (ProtoNode node, string summary)? tryDns(byte[] p, int dnsOff, int frameOff, int totalPayload) {
		if (p.Length < 12) return null;
		var id = (p[0] << 8) | p[1];
		var flags = (p[2] << 8) | p[3];
		var qr = (flags >> 15) & 1;
		var qd = (p[4] << 8) | p[5];
		var n = ProtoNode.Layer("Domain Name System", frameOff, totalPayload);
		n.Children.Add(ProtoNode.Field("Transaction ID", $"0x{id:X4}", dnsOff, 2));
		n.Children.Add(ProtoNode.Field("Flags", qr == 0 ? "Query" : "Response", dnsOff + 2, 2));
		n.Children.Add(ProtoNode.Field("Questions", qd.ToString(), dnsOff + 4, 2));
		var name = readDnsName(p, 12, out var nameEnd);
		if (!string.IsNullOrEmpty(name))
			n.Children.Add(ProtoNode.Field("Name", name, dnsOff + 12, Math.Max(1, nameEnd - 12)));
		var summary = qr == 0 ? $"DNS Query {name}" : $"DNS Response {name}";
		return (n, summary);
	}

	static string readDnsName(byte[] p, int pos, out int end) {
		var sb = new StringBuilder();
		end = pos;
		var guard = 0;
		while (pos < p.Length && guard++ < 64) {
			var len = p[pos++];
			if (len == 0) { end = pos; break; }
			if ((len & 0xC0) == 0xC0) {
				end = pos + 1;
				break;
			}
			if (pos + len > p.Length) break;
			if (sb.Length > 0) sb.Append('.');
			sb.Append(Encoding.ASCII.GetString(p, pos, len));
			pos += len;
			end = pos;
		}
		return sb.ToString();
	}

	static bool matchTuple(string sIp, int sPort, string dIp, int dPort,
		string localIp, int localPort, string remoteIp, int remotePort) {
		var a = ipEquals(sIp, localIp) && sPort == localPort && ipEquals(dIp, remoteIp) && dPort == remotePort;
		var b = ipEquals(sIp, remoteIp) && sPort == remotePort && ipEquals(dIp, localIp) && dPort == localPort;
		return a || b;
	}

	static bool ipEquals(string a, string b) {
		if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
		if (IPAddress.TryParse(a, out var aa) && IPAddress.TryParse(b, out var bb))
			return aa.Equals(bb);
		return false;
	}

	static string formatV6(byte[] f, int off) {
		var b = new byte[16];
		Buffer.BlockCopy(f, off, b, 0, 16);
		return new IPAddress(b).ToString();
	}

	static uint readU32(byte[] b, int i) {
		if (i + 3 >= b.Length) return 0;
		return ((uint)b[i] << 24) | ((uint)b[i + 1] << 16) | ((uint)b[i + 2] << 8) | b[i + 3];
	}

	static string tcpFlags(byte f) {
		List<string> list = new();
		if ((f & 0x01) != 0) list.Add("FIN");
		if ((f & 0x02) != 0) list.Add("SYN");
		if ((f & 0x04) != 0) list.Add("RST");
		if ((f & 0x08) != 0) list.Add("PSH");
		if ((f & 0x10) != 0) list.Add("ACK");
		if ((f & 0x20) != 0) list.Add("URG");
		return list.Count == 0 ? "NONE" : string.Join(",", list);
	}

	static string tlsVer(byte maj, byte min) {
		if (maj == 3 && min == 1) return "TLS 1.0";
		if (maj == 3 && min == 2) return "TLS 1.1";
		if (maj == 3 && min == 3) return "TLS 1.2";
		if (maj == 3 && min == 4) return "TLS 1.3";
		return $"0x{maj:X2}{min:X2}";
	}

	static bool isMostlyText(byte[] p) {
		var n = Math.Min(p.Length, 64);
		var text = 0;
		for (var i = 0; i < n; i++)
			if (p[i] is >= 32 and < 127 or 9 or 10 or 13) text++;
		return text >= n * 0.85;
	}

	static string toPrintable(byte[] p, int max) {
		var n = Math.Min(p.Length, max);
		var sb = new StringBuilder(n);
		for (var i = 0; i < n; i++) {
			var b = p[i];
			if (b is >= 32 and < 127) sb.Append((char)b);
			else if (b is 10 or 13 or 9) sb.Append(' ');
			else sb.Append('.');
		}
		if (p.Length > max) sb.Append('…');
		return sb.ToString();
	}
}
