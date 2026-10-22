using System.Globalization;
using System.Text;

namespace ScreenKit;

/// <summary>图像头里的压缩参数：JPEG 质量和采样、PNG/GIF/BMP/TIFF 的位深与调色板。</summary>
static partial class FileProbe {
	// IJG 标准亮度量化表，按 JPEG 文件里的 zigzag 顺序排列。
	static readonly int[] jpegstd = {
		16, 11, 12, 14, 12, 10, 16, 14, 13, 14, 18, 17, 16, 19, 24, 40,
		26, 24, 22, 22, 24, 49, 35, 37, 29, 40, 58, 51, 61, 60, 57, 51,
		56, 55, 64, 72, 92, 78, 64, 68, 87, 69, 55, 56, 80, 109, 81, 87,
		95, 98, 103, 104, 103, 62, 77, 113, 121, 112, 100, 120, 92, 101, 103, 99
	};

	static void imageparams(Report rep, string path, string kind) {
		byte[] b;
		try { b = readhead(path, 1 << 20); }
		catch { return; }
		var n = b.Length;
		if (n < 16) return;
		if (kind == "jpg") jpegparams(rep, b, n);
		else if (kind == "png") pngparams(rep, b, n);
		else if (kind == "gif") gifparams(rep, b, n);
		else if (kind == "bmp") bmpparams(rep, b, n);
		else if (kind == "ico") icoparams(rep, b, n);
		else if (kind == "webp") webpparams(rep, b, n);
		else if (kind == "tif") tifparams(rep, b, n);
	}

	static void jpegparams(Report rep, byte[] b, int n) {
		var i = 2;
		var mode = "";
		var comps = 0;
		var h = 0;
		var v = 0;
		var haveq = false;
		var qsum = 0.0;
		while (i + 1 < n) {
			if (b[i] != 0xFF) { i++; continue; }
			while (i < n && b[i] == 0xFF) i++;
			if (i >= n) break;
			var m = b[i++];
			if (m == 0x00) continue;
			if (m == 0xD8 || m == 0x01 || (m >= 0xD0 && m <= 0xD7)) continue;
			if (m == 0xD9 || m == 0xDA) break;
			if (i + 2 > n) break;
			var len = (b[i] << 8) | b[i + 1];
			if (len < 2) break;
			var start = i + 2;
			var end = i + len;
			if (end > n) break;
			if (m == 0xE0 && len >= 16 && b[start] == 'J' && b[start + 1] == 'F'
				&& b[start + 2] == 'I' && b[start + 3] == 'F') {
				var units = b[start + 7];
				var dx = (b[start + 8] << 8) | b[start + 9];
				var dy = (b[start + 10] << 8) | b[start + 11];
				if (units == 1) add(rep, "probe.img.dpi", $"{dx} × {dy}");
				else if (units == 2) add(rep, "probe.img.dpi",
					$"{Math.Round(dx * 2.54)} × {Math.Round(dy * 2.54)}");
			}
			else if (m == 0xC0 || m == 0xC1 || m == 0xC2) {
				mode = m == 0xC2 ? "prog" : "base";
				if (start + 7 < end) {
					comps = b[start + 5];
					h = b[start + 7] >> 4;
					v = b[start + 7] & 15;
				}
			}
			else if (m == 0xDB) {
				var p = start;
				while (p < end) {
					var pq = b[p] >> 4;
					var tq = b[p] & 15;
					p++;
					var cnt = pq == 0 ? 64 : 128;
					if (p + cnt > end) break;
					if (pq == 0 && tq == 0 && !haveq) {
						for (var k = 0; k < 64; k++) qsum += b[p + k] * 100.0 / jpegstd[k];
						haveq = true;
					}
					p += cnt;
				}
			}
			i = end;
		}
		if (mode.Length > 0) add(rep, "probe.img.jpg.mode", mode);
		if (comps > 0) add(rep, "probe.img.jpg.comps", comps.ToString(CultureInfo.InvariantCulture));
		if (h > 0 && v > 0) add(rep, "probe.img.jpg.sub", subsamp(h, v));
		if (haveq) {
			var scale = qsum / 64.0;
			var quality = scale <= 100 ? (200 - scale) / 2 : 5000 / scale;
			quality = Math.Max(1, Math.Min(100, Math.Round(quality)));
			add(rep, "probe.img.jpg.quality", quality.ToString(CultureInfo.InvariantCulture));
		}
	}

	static string subsamp(int h, int v) {
		if (h == 1 && v == 1) return "4:4:4";
		if (h == 2 && v == 1) return "4:2:2";
		if (h == 2 && v == 2) return "4:2:0";
		if (h == 1 && v == 2) return "4:4:0";
		if (h == 4 && v == 1) return "4:1:1";
		return $"{h}x{v}";
	}

	static void pngparams(Report rep, byte[] b, int n) {
		if (n < 33) return;
		var depth = b[24];
		var color = b[25];
		var inter = b[28];
		add(rep, "probe.img.depth", depth.ToString(CultureInfo.InvariantCulture));
		add(rep, "probe.img.color", pngcolor(color));
		if (inter != 0) add(rep, "probe.img.interlace", "yes");
		long palette = -1;
		long trns = -1;
		var i = 8;
		while (i + 8 <= n) {
			var len = be32(b, i);
			if (len < 0 || i + 12 + len > n) break;
			var type = Encoding.ASCII.GetString(b, i + 4, 4);
			if (type == "PLTE") palette = len / 3;
			else if (type == "tRNS") trns = len;
			else if (type == "IDAT" || type == "IEND") break;
			i += (int)(12 + len);
		}
		if (palette >= 0 && color == 3) add(rep, "probe.img.palette", palette.ToString(CultureInfo.InvariantCulture));
		if (trns >= 0 && color == 3) add(rep, "probe.img.trns", trns.ToString(CultureInfo.InvariantCulture));
	}

	static string pngcolor(int type) {
		switch (type) {
			case 0: return "gray";
			case 2: return "rgb";
			case 3: return "palette";
			case 4: return "graya";
			case 6: return "rgba";
			default: return type.ToString(CultureInfo.InvariantCulture);
		}
	}

	static void gifparams(Report rep, byte[] b, int n) {
		if (n < 13) return;
		add(rep, "probe.img.gif.version", Encoding.ASCII.GetString(b, 0, 6));
		var packed = b[10];
		if ((packed & 0x80) != 0) {
			var colors = 1 << ((packed & 7) + 1);
			add(rep, "probe.img.palette", colors.ToString(CultureInfo.InvariantCulture));
		}
	}

	static void bmpparams(Report rep, byte[] b, int n) {
		if (n < 54) return;
		var depth = b[28] | (b[29] << 8);
		if (depth > 0) add(rep, "probe.img.depth", depth.ToString(CultureInfo.InvariantCulture));
		var used = (int)le32(b, 46);
		if (used > 0 && depth <= 8) add(rep, "probe.img.palette", used.ToString(CultureInfo.InvariantCulture));
		else if (depth > 0 && depth <= 8)
			add(rep, "probe.img.palette", (1 << depth).ToString(CultureInfo.InvariantCulture));
	}

	static void icoparams(Report rep, byte[] b, int n) {
		if (n < 6) return;
		var count = b[4] | (b[5] << 8);
		if (count <= 0) return;
		add(rep, "probe.img.ico.count", count.ToString(CultureInfo.InvariantCulture));
		var depths = new List<int>();
		for (var i = 0; i < count; i++) {
			var p = 6 + i * 16;
			if (p + 16 > n) break;
			var d = b[p + 6] | (b[p + 7] << 8);
			if (d > 0 && !depths.Contains(d)) depths.Add(d);
		}
		if (depths.Count > 0) add(rep, "probe.img.depth", string.Join(" / ", depths));
	}

	static void webpparams(Report rep, byte[] b, int n) {
		if (n < 16) return;
		var p = 12;
		while (p + 8 <= n) {
			var four = Encoding.ASCII.GetString(b, p, 4);
			var len = le32(b, p + 4);
			if (len < 0) break;
			if (four == "VP8 ") { add(rep, "probe.img.webp.mode", "lossy"); return; }
			if (four == "VP8L") { add(rep, "probe.img.webp.mode", "lossless"); return; }
			p += (int)(8 + len + (len & 1));
		}
	}

	static void tifparams(Report rep, byte[] b, int n) {
		if (n < 8) return;
		var le = b[0] == 'I' && b[1] == 'I';
		if (!le && !(b[0] == 'M' && b[1] == 'M')) return;
		if ((le ? le16(b, 2) : be16(b, 2)) != 42) return;
		var off = le ? le32(b, 4) : be32(b, 4);
		if (off <= 0 || off + 2 > n) return;
		var count = le ? le16(b, (int)off) : be16(b, (int)off);
		var bits = 0;
		var photo = -1;
		var cmap = -1;
		for (var i = 0; i < count; i++) {
			var p = (int)off + 2 + i * 12;
			if (p + 12 > n) break;
			var tag = le ? le16(b, p) : be16(b, p);
			var typ = le ? le16(b, p + 2) : be16(b, p + 2);
			var cnt = le ? (long)le32(b, p + 4) : be32(b, p + 4);
			if (tag == 258) bits = tifval(b, le, p + 8, typ, cnt, n);
			else if (tag == 262) photo = tifval(b, le, p + 8, typ, cnt, n);
			else if (tag == 320) cmap = (int)cnt;
		}
		if (bits > 0) add(rep, "probe.img.depth", bits.ToString(CultureInfo.InvariantCulture));
		if (photo == 3) {
			add(rep, "probe.img.color", "palette");
			if (cmap > 3) add(rep, "probe.img.palette", (cmap / 3).ToString(CultureInfo.InvariantCulture));
		}
		else if (photo == 2) add(rep, "probe.img.color", "rgb");
		else if (photo == 0 || photo == 1) add(rep, "probe.img.color", "gray");
		else if (photo == 5) add(rep, "probe.img.color", "cmyk");
	}

	static int tifval(byte[] b, bool le, int p, int typ, long cnt, int n) {
		var size = typ == 3 ? 2 : 4;
		long at = p;
		if (size * cnt > 4) at = le ? le32(b, p) : be32(b, p);
		if (at < 0 || at + size > n) return 0;
		if (typ == 3) return le ? le16(b, (int)at) : be16(b, (int)at);
		if (typ == 4) return (int)(le ? le32(b, (int)at) : be32(b, (int)at));
		return 0;
	}

	static int le16(byte[] b, int i) => b[i] | (b[i + 1] << 8);

	static int be16(byte[] b, int i) => (b[i] << 8) | b[i + 1];

	static long le32(byte[] b, int i) =>
		((long)b[i] | ((long)b[i + 1] << 8) | ((long)b[i + 2] << 16) | ((long)b[i + 3] << 24));

	static long be32(byte[] b, int i) =>
		(((long)b[i] << 24) | ((long)b[i + 1] << 16) | ((long)b[i + 2] << 8) | (long)b[i + 3]);
}
