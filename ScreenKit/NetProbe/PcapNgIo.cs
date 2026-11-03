using System.IO;
using System.Text;

namespace ScreenKit;

/// <summary>
/// 精简 pcapng 读写（SHB + IDB + EPB），兼容 Wireshark。
/// 导出：整帧 Raw；导入：解析后注入抓包缓存。
/// </summary>
static class PcapNgIo {
	const uint BT_SHB = 0x0A0D0D0A;
	const uint BT_IDB = 0x00000001;
	const uint BT_EPB = 0x00000006;
	const uint BT_SPB = 0x00000003; // Simple Packet Block（部分文件会用）
	const uint BYTE_ORDER_MAGIC = 0x1A2B3C4D;

	/// <summary>将报文列表写入 pcapng（按时间排序）。</summary>
	public static void Write(string path, IEnumerable<PacketRow> packets) {
		if (string.IsNullOrEmpty(path)) throw new ArgumentException("path");
		List<PacketRow> list = new();
		foreach (var p in packets) {
			if (p?.Raw != null && p.Raw.Length > 0) list.Add(p);
		}
		list.Sort((a, b) => a.Time.CompareTo(b.Time));

		using var fs = File.Create(path);
		using var bw = new BinaryWriter(fs);
		writeShb(bw);
		// 常见链路类型：以太网 1；若帧不像以太网则用 Raw IP 101
		var link = guessLinkType(list);
		writeIdb(bw, link);
		foreach (var p in list)
			writeEpb(bw, p, link);
	}

	/// <summary>读取 pcapng，解析 TCP 为 PacketRow 列表（最多 max 条）。</summary>
	public static List<PacketRow> Read(string path, int max = 20000) {
		if (string.IsNullOrEmpty(path) || !File.Exists(path))
			throw new FileNotFoundException("文件不存在", path);
		using var fs = File.OpenRead(path);
		using var br = new BinaryReader(fs);
		List<PacketRow> rows = new();
		var ifaces = new Dictionary<uint, int>(); // id -> linktype
		uint nextIface = 0;
		var little = true;

		while (fs.Position + 8 <= fs.Length && rows.Count < max) {
			var start = fs.Position;
			var type = readU32(br, little);
			var totalLen = readU32(br, little);
			if (totalLen < 12 || start + totalLen > fs.Length) break;

			if (type == BT_SHB) {
				var magic = br.ReadUInt32();
				if (magic == BYTE_ORDER_MAGIC) little = true;
				else if (magic == 0x4D3C2B1A) little = false;
				// 跳过剩余 SHB
				fs.Position = start + totalLen;
				continue;
			}

			if (type == BT_IDB) {
				var link = readU16(br, little);
				readU16(br, little); // reserved
				readU32(br, little); // snaplen
				ifaces[nextIface++] = link;
				fs.Position = start + totalLen;
				continue;
			}

			if (type == BT_EPB) {
				var ifId = readU32(br, little);
				var tsHigh = readU32(br, little);
				var tsLow = readU32(br, little);
				var capLen = readU32(br, little);
				var orgLen = readU32(br, little);
				if (capLen > 0 && capLen < 16 * 1024 * 1024 && fs.Position + capLen <= fs.Length) {
					var raw = br.ReadBytes((int)capLen);
					var link = ifaces.TryGetValue(ifId, out var lt) ? lt : 1;
					var time = tsToTime(tsHigh, tsLow);
					tryAdd(rows, raw, link, time, max);
				}
				fs.Position = start + totalLen;
				continue;
			}

			if (type == BT_SPB) {
				var orgLen = readU32(br, little);
				var capLen = orgLen;
				// SPB 无接口 id，用 0
				if (capLen > 0 && capLen < 16 * 1024 * 1024 && fs.Position + capLen <= fs.Length) {
					var raw = br.ReadBytes((int)capLen);
					var pad = (4 - (int)(capLen % 4)) % 4;
					if (pad > 0) fs.Position += pad;
					var link = ifaces.TryGetValue(0, out var lt) ? lt : 1;
					tryAdd(rows, raw, link, DateTime.Now, max);
				}
				fs.Position = start + totalLen;
				continue;
			}

			// 未知块跳过
			fs.Position = start + totalLen;
		}

		// 兼容：若完全不是 pcapng，尝试经典 pcap
		if (rows.Count == 0) {
			fs.Position = 0;
			rows = readClassicPcap(br, max);
		}
		return rows;
	}

	static void tryAdd(List<PacketRow> rows, byte[] raw, int link, DateTime time, int max) {
		if (rows.Count >= max) return;
		if (!ProtocolParser.TryParseAnyTcp(raw, link, out var row)) {
			if (!ProtocolParser.TryParseAnyTcp(raw, -1, out row)) return;
		}
		row.Time = time;
		row.Raw = raw;
		rows.Add(row);
	}

	static List<PacketRow> readClassicPcap(BinaryReader br, int max) {
		List<PacketRow> rows = new();
		var fs = br.BaseStream;
		if (fs.Length < 24) return rows;
		var magic = br.ReadUInt32();
		var little = magic == 0xA1B2C3D4 || magic == 0xA1B23C4D;
		var swap = magic == 0xD4C3B2A1 || magic == 0x4D3CB2A1;
		if (!little && !swap) return rows;
		// 跳过头剩余
		fs.Position = 24;
		// linktype at offset 20
		fs.Position = 20;
		var link = little ? br.ReadUInt32() : swapU32(br.ReadUInt32());
		fs.Position = 24;
		while (fs.Position + 16 <= fs.Length && rows.Count < max) {
			var tsSec = little ? br.ReadUInt32() : swapU32(br.ReadUInt32());
			var tsUsec = little ? br.ReadUInt32() : swapU32(br.ReadUInt32());
			var incl = little ? br.ReadUInt32() : swapU32(br.ReadUInt32());
			var orig = little ? br.ReadUInt32() : swapU32(br.ReadUInt32());
			if (incl == 0 || fs.Position + incl > fs.Length) break;
			var raw = br.ReadBytes((int)incl);
			var time = DateTimeOffset.FromUnixTimeSeconds(tsSec).LocalDateTime.AddTicks(tsUsec * 10);
			tryAdd(rows, raw, (int)link, time, max);
		}
		return rows;
	}

	static void writeShb(BinaryWriter bw) {
		const uint body = 4 + 2 + 2 + 8; // magic+ver+sectionlen
		const uint total = 8 + body + 4; // type+len + body + len
		bw.Write(BT_SHB);
		bw.Write(total);
		bw.Write(BYTE_ORDER_MAGIC);
		bw.Write((ushort)1);
		bw.Write((ushort)0);
		bw.Write(unchecked((ulong)-1));
		bw.Write(total);
	}

	static void writeIdb(BinaryWriter bw, int linkType) {
		const uint body = 2 + 2 + 4;
		const uint total = 8 + body + 4;
		bw.Write(BT_IDB);
		bw.Write(total);
		bw.Write((ushort)linkType);
		bw.Write((ushort)0);
		bw.Write(65535u);
		bw.Write(total);
	}

	static void writeEpb(BinaryWriter bw, PacketRow p, int linkType) {
		var raw = p.Raw;
		var capLen = (uint)raw.Length;
		var pad = (4 - (int)(capLen % 4)) % 4;
		var body = 4 + 4 + 4 + 4 + 4 + capLen + (uint)pad;
		var total = 8 + body + 4;
		var (hi, lo) = timeToTs(p.Time);
		bw.Write(BT_EPB);
		bw.Write(total);
		bw.Write(0u); // iface 0
		bw.Write(hi);
		bw.Write(lo);
		bw.Write(capLen);
		bw.Write(capLen);
		bw.Write(raw);
		for (var i = 0; i < pad; i++) bw.Write((byte)0);
		bw.Write(total);
	}

	static int guessLinkType(List<PacketRow> list) {
		foreach (var p in list) {
			var r = p.Raw;
			if (r == null || r.Length < 14) continue;
			// 以太网类型字段
			var eth = (r[12] << 8) | r[13];
			if (eth is 0x0800 or 0x86DD or 0x8100) return 1; // DLT_EN10MB
			if ((r[0] >> 4) == 4 || (r[0] >> 4) == 6) return 101; // raw IP
			if (r.Length >= 4 && ((r[4] >> 4) == 4 || (r[4] >> 4) == 6)) return 0; // NULL/loopback
		}
		return 1;
	}

	static (uint hi, uint lo) timeToTs(DateTime t) {
		// 微秒时间戳（自 Unix epoch）
		var dto = new DateTimeOffset(t.ToUniversalTime());
		var us = dto.ToUnixTimeMilliseconds() * 1000L
		         + (t.Ticks % TimeSpan.TicksPerMillisecond) / 10;
		if (us < 0) us = 0;
		return ((uint)(us >> 32), (uint)(us & 0xFFFFFFFF));
	}

	static DateTime tsToTime(uint hi, uint lo) {
		var us = ((long)hi << 32) | lo;
		try {
			var ms = us / 1000;
			return DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
		} catch {
			return DateTime.Now;
		}
	}

	static uint readU32(BinaryReader br, bool little) {
		var v = br.ReadUInt32();
		return little ? v : swapU32(v);
	}

	static ushort readU16(BinaryReader br, bool little) {
		var v = br.ReadUInt16();
		return little ? v : (ushort)((v >> 8) | (v << 8));
	}

	static uint swapU32(uint v) =>
		((v & 0x000000FFu) << 24) | ((v & 0x0000FF00u) << 8) |
		((v & 0x00FF0000u) >> 8) | ((v & 0xFF000000u) >> 24);
}
