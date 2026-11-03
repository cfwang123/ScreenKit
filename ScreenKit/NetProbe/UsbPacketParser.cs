using System.IO;
using System.Text;

namespace ScreenKit;

/// <summary>
/// 解析 USBPcap 帧（链路类型 249）。
/// 头为 USBPCAP_BUFFER_PACKET_HEADER，小端；控制传输多一个 stage 字节，等时传输另有分组表。
/// </summary>
static class UsbPacketParser {
	public const int DLT_USBPCAP = 249;
	const int HDR = 27; // USBPCAP_BUFFER_PACKET_HEADER

	public static bool TryParse(byte[] frame, out PacketRow row) {
		row = null;
		if (frame == null || frame.Length < HDR + 1) return false;
		var hdrLen = u16(frame, 0);
		if (hdrLen < HDR || hdrLen > frame.Length) return false;

		var status = i32(frame, 10);
		var function = u16(frame, 14);
		var info = frame[16];
		var bus = u16(frame, 17);
		var device = u16(frame, 19);
		var endpoint = frame[21];
		var transfer = frame[22];
		var dataLen = u32(frame, 23);
		// 完成包（PDO→FDO）是设备到主机方向
		var inbound = (info & 1) != 0;

		var tree = new List<ProtoNode>();
		var head = ProtoNode.Layer("USBPcap", 0, hdrLen);
		head.Children.Add(ProtoNode.Field("头长度", hdrLen.ToString(), 0, 2));
		head.Children.Add(ProtoNode.Field("IRP", "0x" + u64(frame, 2).ToString("X"), 2, 8));
		head.Children.Add(ProtoNode.Field("状态", statusText(status), 10, 4));
		head.Children.Add(ProtoNode.Field("URB 功能", "0x" + function.ToString("X4"), 14, 2));
		head.Children.Add(ProtoNode.Field("方向", inbound ? "设备 → 主机" : "主机 → 设备", 16, 1));
		head.Children.Add(ProtoNode.Field("总线", bus.ToString(), 17, 2));
		head.Children.Add(ProtoNode.Field("设备地址", device.ToString(), 19, 2));
		head.Children.Add(ProtoNode.Field("端点", endpointText(endpoint), 21, 1));
		head.Children.Add(ProtoNode.Field("传输", transferText(transfer), 22, 1));
		head.Children.Add(ProtoNode.Field("数据长度", dataLen.ToString(), 23, 4));
		tree.Add(head);

		var off = hdrLen;
		var summary = "";
		if (transfer == 2 && off < frame.Length) {
			var stage = frame[off];
			var ctrl = ProtoNode.Layer("控制传输", off, 1);
			ctrl.Children.Add(ProtoNode.Field("阶段", stageText(stage), off, 1));
			tree.Add(ctrl);
			off++;
			// stage 0：8 字节 SETUP，后面可能跟着 DATA OUT
			if (stage == 0 && off + 8 <= frame.Length) {
				var bm = frame[off];
				var breq = frame[off + 1];
				var wvalue = u16(frame, off + 2);
				var windex = u16(frame, off + 4);
				var wlength = u16(frame, off + 6);
				var toDevice = (bm & 0x80) == 0;
				var setup = ProtoNode.Layer("SETUP", off, 8);
				setup.Children.Add(ProtoNode.Field("bmRequestType", "0x" + bm.ToString("X2") + "  " + requestTypeText(bm), off, 1));
				setup.Children.Add(ProtoNode.Field("bRequest", "0x" + breq.ToString("X2") + "  " + requestName(bm, breq), off + 1, 1));
				setup.Children.Add(ProtoNode.Field("wValue", "0x" + wvalue.ToString("X4"), off + 2, 2));
				setup.Children.Add(ProtoNode.Field("wIndex", "0x" + windex.ToString("X4"), off + 4, 2));
				setup.Children.Add(ProtoNode.Field("wLength", wlength.ToString(), off + 6, 2));
				tree.Add(setup);
				summary = requestName(bm, breq) + " " + requestTypeText(bm)
					+ $" wValue=0x{wvalue:X4} wIndex=0x{windex:X4} wLength={wlength}";
				inbound = !toDevice;
				off += 8;
			} else {
				summary = stageText(stage);
			}
		} else if (transfer == 0 && off + 12 <= frame.Length) {
			var n = (int)u32(frame, off + 4);
			var iso = ProtoNode.Layer("等时传输", off, Math.Min(12, frame.Length - off));
			iso.Children.Add(ProtoNode.Field("起始帧", u32(frame, off).ToString(), off, 4));
			iso.Children.Add(ProtoNode.Field("分组数", n.ToString(), off + 4, 4));
			iso.Children.Add(ProtoNode.Field("错误数", u32(frame, off + 8).ToString(), off + 8, 4));
			tree.Add(iso);
			summary = $"等时 {n} 分组";
			off += 12 + Math.Max(0, n) * 12;
			if (off > frame.Length) off = frame.Length;
		}

		var payloadLen = Math.Max(0, frame.Length - off);
		if (payloadLen > 0) tree.Add(ProtoNode.Layer("数据", off, payloadLen));
		if (summary.Length == 0) summary = payloadLen > 0 ? $"{payloadLen} 字节" : statusText(status);

		var epNum = endpoint & 0x7F;
		var epIn = (endpoint & 0x80) != 0;
		row = new PacketRow {
			Time = DateTime.Now,
			Direction = inbound ? PacketDirection.In : PacketDirection.Out,
			Src = inbound ? $"总线{bus} 设备{device}" : "主机",
			Dst = inbound ? "主机" : $"总线{bus} 设备{device}",
			SrcPort = inbound ? device : 0,
			DstPort = inbound ? 0 : device,
			Length = frame.Length,
			PayloadLength = payloadLen,
			PayloadOffset = off,
			Protocol = transferText(transfer),
			Summary = summary,
			Flags = "EP" + epNum + (epIn ? " IN" : " OUT"),
			Seq = (uint)bus,
			Ack = endpoint,
			Raw = frame,
			Payload = payloadLen > 0 ? slice(frame, off, payloadLen) : Array.Empty<byte>(),
			Tree = tree,
			ConnKey = bus + "." + device,
			ProcessName = "EP" + epNum
		};
		return true;
	}

	public static byte[] Slice(byte[] src, int off, int len) => slice(src, off, len);

	static byte[] slice(byte[] src, int off, int len) {
		var n = Math.Max(0, Math.Min(len, src.Length - off));
		var b = new byte[n];
		if (n > 0) Buffer.BlockCopy(src, off, b, 0, n);
		return b;
	}

	static int u16(byte[] b, int o) => b[o] | (b[o + 1] << 8);
	static long u32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
	static int i32(byte[] b, int o) => b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
	static ulong u64(byte[] b, int o) => (ulong)u32(b, o) | ((ulong)u32(b, o + 4) << 32);

	public static string TransferText(int t) => transferText(t);

	static string transferText(int t) => t switch {
		0 => "等时",
		1 => "中断",
		2 => "控制",
		3 => "批量",
		0xFE => "IRP",
		_ => "未知"
	};

	static string stageText(int s) => s switch {
		0 => "SETUP",
		1 => "DATA",
		2 => "STATUS",
		3 => "COMPLETE",
		_ => "阶段 " + s
	};

	static string endpointText(int ep) =>
		"0x" + ep.ToString("X2") + "  EP" + (ep & 0x7F) + ((ep & 0x80) != 0 ? " IN" : " OUT");

	/// <summary>USBD_STATUS：最高位为 1 是错误，0x40000000 一段是挂起。</summary>
	static string statusText(int s) {
		if (s == 0) return "成功";
		if ((s & unchecked((int)0xC0000000)) == unchecked((int)0x40000000)) return "挂起 0x" + s.ToString("X8");
		if (s < 0) return "失败 0x" + s.ToString("X8");
		return "0x" + s.ToString("X8");
	}

	static string requestTypeText(int bm) {
		var dir = (bm & 0x80) != 0 ? "设备到主机" : "主机到设备";
		var type = ((bm >> 5) & 3) switch { 0 => "标准", 1 => "类", 2 => "厂商", _ => "保留" };
		var rec = (bm & 0x1F) switch { 0 => "设备", 1 => "接口", 2 => "端点", 3 => "其他", _ => "接收方" + (bm & 0x1F) };
		return dir + " · " + type + " · " + rec;
	}

	static string requestName(int bm, int req) {
		if (((bm >> 5) & 3) != 0) return "bRequest " + req;
		return req switch {
			0 => "GET_STATUS",
			1 => "CLEAR_FEATURE",
			3 => "SET_FEATURE",
			5 => "SET_ADDRESS",
			6 => "GET_DESCRIPTOR",
			7 => "SET_DESCRIPTOR",
			8 => "GET_CONFIGURATION",
			9 => "SET_CONFIGURATION",
			10 => "GET_INTERFACE",
			11 => "SET_INTERFACE",
			12 => "SYNCH_FRAME",
			_ => "bRequest " + req
		};
	}

	/// <summary>不依赖驱动的解析与 pcapng 往返自测。返回 0 表示通过。</summary>
	public static int SelfTest(Action<string> log) {
		var bad = 0;
		void check(bool ok, string name) {
			log((ok ? "OK  " : "BAD ") + name);
			if (!ok) bad++;
		}

		var setup = new byte[27 + 1 + 8];
		setup[0] = 27; // headerLen
		setup[16] = 0; // 主机 → 设备
		setup[17] = 1; // bus 1
		setup[19] = 5; // device 5
		setup[21] = 0x00; // EP0 OUT
		setup[22] = 2; // control
		setup[27] = 0; // SETUP stage
		setup[28] = 0x80; // device-to-host, standard, device
		setup[29] = 6; // GET_DESCRIPTOR
		setup[30] = 0x00; setup[31] = 0x01; // device descriptor
		setup[34] = 18; // wLength
		check(TryParse(setup, out var srow), "解析控制 SETUP");
		check(srow != null && srow.Protocol == "控制" && srow.ConnKey == "1.5", "控制帧设备键");
		check(srow != null && srow.Direction == PacketDirection.In, "GET_DESCRIPTOR 方向取数据方向");
		check(srow != null && (srow.Summary ?? "").Contains("GET_DESCRIPTOR"), "摘要含 GET_DESCRIPTOR");
		check(srow != null && srow.PayloadLength == 0, "纯 SETUP 无数据");

		var bulk = new byte[27 + 4];
		bulk[0] = 27;
		bulk[16] = 1; // 设备 → 主机
		bulk[17] = 2; bulk[19] = 3;
		bulk[21] = 0x81; // EP1 IN
		bulk[22] = 3; // bulk
		bulk[23] = 4; // dataLength
		bulk[27] = (byte)'A'; bulk[28] = (byte)'B'; bulk[29] = (byte)'C'; bulk[30] = (byte)'D';
		check(TryParse(bulk, out var brow), "解析批量传输");
		check(brow != null && brow.Protocol == "批量" && brow.PayloadLength == 4, "批量载荷 4 字节");
		check(brow != null && brow.Direction == PacketDirection.In && brow.Flags == "EP1 IN", "批量端点与方向");

		var junk = new byte[] { 1, 2, 3, 4, 5 };
		check(!TryParse(junk, out _), "过短帧拒绝");

		var dir = Path.Combine(Path.GetTempPath(), "usbcap-selftest-" + Environment.TickCount.ToString("X"));
		Directory.CreateDirectory(dir);
		try {
			var path = Path.Combine(dir, "t.pcapng");
			PcapNgIo.Write(path, new[] { srow, brow }, DLT_USBPCAP);
			var back = PcapNgIo.ReadUsb(path, 10);
			check(back.Count == 2, "pcapng 往返条数");
			check(back.Count == 2 && back[0].Protocol == "控制" && back[1].Protocol == "批量", "pcapng 往返协议");
			check(back.Count == 2 && back[1].PayloadLength == 4 && back[1].Payload[0] == (byte)'A', "pcapng 往返载荷");
			var classic = PcapNgIo.Read(path, 10);
			check(classic.Count == 0, "USB 帧不进网络抓包解析");
		} finally {
			try { Directory.Delete(dir, true); } catch { }
		}
		log(bad == 0 ? "全部通过" : $"失败 {bad} 项");
		return bad == 0 ? 0 : 1;
	}

	/// <summary>标准描述符的简短说明，供列表摘要。认不出就返回空。</summary>
	public static string DescribeDescriptor(byte[] data) {
		if (data == null || data.Length < 2 || data[0] < 2) return "";
		var kind = data[1];
		var sb = new StringBuilder();
		sb.Append(kind switch {
			1 => "设备描述符",
			2 => "配置描述符",
			3 => "字符串描述符",
			4 => "接口描述符",
			5 => "端点描述符",
			_ => "描述符类型 " + kind
		});
		if (kind == 1 && data.Length >= 18) {
			var vid = data[8] | (data[9] << 8);
			var pid = data[10] | (data[11] << 8);
			sb.Append($"  USB{data[3] >> 4}.{data[3] & 0xF}  {vid:X4}:{pid:X4}");
		} else if (kind == 2 && data.Length >= 9) {
			sb.Append($"  {data[4]} 个接口");
		} else if (kind == 4 && data.Length >= 9) {
			sb.Append($"  接口 {data[2]} 类 {data[5]:X2}");
		}
		return sb.ToString();
	}
}
