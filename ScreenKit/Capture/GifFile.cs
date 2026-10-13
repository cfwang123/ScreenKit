namespace ScreenKit;

/// <summary>把系统 H.264 临时视频编成无限循环的无声 GIF。不依赖 FFmpeg。</summary>
static class GifFile {
	public static void FromVideo(string videoPath, string gifPath, int outW, int outH, int outFps, int colors,
		int srcFps = GifOptions.CaptureFps, CancellationToken ct = default) {
		if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
			throw new FileNotFoundException("视频不存在", videoPath);
		outW = Math.Max(16, outW);
		outH = Math.Max(16, outH);
		srcFps = Compat.Clamp(srcFps, 1, 60);
		outFps = Compat.Clamp(outFps, 1, srcFps);
		colors = Compat.Clamp(colors, 2, 256);
		var dir = Path.GetDirectoryName(gifPath);
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		try { if (File.Exists(gifPath)) File.Delete(gifPath); } catch { }

		var samples = new List<byte[]>();
		MfH264Reader.ForEach(videoPath, outW, outH, (bgra, _, index) => {
			if (ct.IsCancellationRequested || samples.Count >= 48) return false;
			if (samples.Count >= 8 && index % 3 != 0) return true;
			samples.Add(bgra);
			return true;
		}, ct);
		ct.ThrowIfCancellationRequested();
		if (samples.Count == 0)
			throw new InvalidOperationException("视频无有效帧");
		var palette = buildpalette(samples, outW, outH, colors);
		samples.Clear();
		var lut = buildlut(palette, colors);

		var delay = Math.Max(2, (100 + outFps / 2) / outFps);
		using (var fs = new FileStream(gifPath, FileMode.Create, FileAccess.Write, FileShare.Read)) {
			writeheader(fs, outW, outH, palette);
			var step = (double)srcFps / outFps;
			double next = 0;
			var wrote = 0;
			MfH264Reader.ForEach(videoPath, outW, outH, (bgra, stride, index) => {
				if (ct.IsCancellationRequested) return false;
				if (index + 1e-6 < next) return true;
				next += step;
				writeframe(fs, indexof(bgra, stride, outW, outH, lut), outW, outH, delay);
				wrote++;
				return true;
			}, ct);
			fs.WriteByte(0x3B);
			if (wrote == 0)
				throw new InvalidOperationException("GIF 没有写出帧");
		}
		if (ct.IsCancellationRequested) {
			try { if (File.Exists(gifPath)) File.Delete(gifPath); } catch { }
			ct.ThrowIfCancellationRequested();
		}
		if (!File.Exists(gifPath) || new FileInfo(gifPath).Length < 32)
			throw new InvalidOperationException("GIF 未写出或过小");
		RecordLog.Step("gif_encode",
			$"mf colors={colors} out={outW}x{outH}@{outFps} (src={srcFps}) size={new FileInfo(gifPath).Length}");
	}

	static uint[] buildpalette(List<byte[]> frames, int w, int h, int colors) {
		var pixels = new List<uint>(8192);
		foreach (var f in frames) {
			var step = Math.Max(1, (w * h) / 2000);
			var i = 0;
			for (var y = 0; y < h; y++) {
				var row = y * w * 4;
				for (var x = 0; x < w; x++, i++) {
					if (i % step != 0) continue;
					var o = row + x * 4;
					if (o + 2 >= f.Length) continue;
					pixels.Add(((uint)f[o + 2] << 16) | ((uint)f[o + 1] << 8) | f[o]);
				}
			}
		}
		if (pixels.Count == 0) pixels.Add(0);
		var boxes = new List<ColorBox> { new(pixels) };
		while (boxes.Count < colors) {
			ColorBox best = null;
			var bestRange = -1;
			foreach (var box in boxes) {
				var rg = box.Range;
				if (rg > bestRange && box.Pixels.Count > 1) {
					bestRange = rg;
					best = box;
				}
			}
			if (best == null) break;
			boxes.Remove(best);
			best.Split(out var a, out var b);
			boxes.Add(a);
			boxes.Add(b);
		}
		var pal = new uint[256];
		for (var i = 0; i < boxes.Count && i < 256; i++)
			pal[i] = boxes[i].Average;
		var last = boxes.Count > 0 ? boxes[boxes.Count - 1].Average : 0u;
		for (var i = boxes.Count; i < 256; i++)
			pal[i] = last;
		return pal;
	}

	static byte[] buildlut(uint[] palette, int colors) {
		var n = Compat.Clamp(colors, 2, 256);
		var lut = new byte[32 * 32 * 32];
		for (var i = 0; i < lut.Length; i++) {
			var r = ((i >> 10) & 31) * 255 / 31;
			var g = ((i >> 5) & 31) * 255 / 31;
			var b = (i & 31) * 255 / 31;
			var best = 0;
			var bestD = int.MaxValue;
			for (var c = 0; c < n; c++) {
				var p = palette[c];
				var dr = r - (int)((p >> 16) & 0xFF);
				var dg = g - (int)((p >> 8) & 0xFF);
				var db = b - (int)(p & 0xFF);
				var d = dr * dr + dg * dg + db * db;
				if (d < bestD) {
					bestD = d;
					best = c;
					if (d == 0) break;
				}
			}
			lut[i] = (byte)best;
		}
		return lut;
	}

	static byte[] indexof(byte[] bgra, int stride, int w, int h, byte[] lut) {
		var idx = new byte[w * h];
		for (var y = 0; y < h; y++) {
			var row = y * stride;
			var drow = y * w;
			for (var x = 0; x < w; x++) {
				var o = row + x * 4;
				var b = bgra[o] >> 3;
				var g = bgra[o + 1] >> 3;
				var r = bgra[o + 2] >> 3;
				idx[drow + x] = lut[(r << 10) | (g << 5) | b];
			}
		}
		return idx;
	}

	static void writeheader(Stream s, int w, int h, uint[] palette) {
		s.Write(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, 0, 6);
		writeu16(s, w);
		writeu16(s, h);
		s.WriteByte(0xF7);
		s.WriteByte(0);
		s.WriteByte(0);
		for (var i = 0; i < 256; i++) {
			var p = palette[i];
			s.WriteByte((byte)((p >> 16) & 0xFF));
			s.WriteByte((byte)((p >> 8) & 0xFF));
			s.WriteByte((byte)(p & 0xFF));
		}
		var app = new byte[] {
			0x21, 0xFF, 0x0B,
			0x4E, 0x45, 0x54, 0x53, 0x43, 0x41, 0x50, 0x45, 0x32, 0x2E, 0x30,
			0x03, 0x01, 0x00, 0x00, 0x00,
		};
		s.Write(app, 0, app.Length);
	}

	static void writeframe(Stream s, byte[] index, int w, int h, int delayCs) {
		s.WriteByte(0x21);
		s.WriteByte(0xF9);
		s.WriteByte(0x04);
		s.WriteByte(0x04);
		writeu16(s, delayCs);
		s.WriteByte(0);
		s.WriteByte(0);
		s.WriteByte(0x2C);
		writeu16(s, 0);
		writeu16(s, 0);
		writeu16(s, w);
		writeu16(s, h);
		s.WriteByte(0);
		writelzw(s, index);
	}

	static void writelzw(Stream s, byte[] pixels) {
		const int minBits = 8;
		s.WriteByte(minBits);
		var clear = 1 << minBits;
		var eof = clear + 1;
		var codeSize = minBits + 1;
		var next = eof + 1;
		var dict = new Dictionary<int, int>();
		var bits = new BitPack();
		bits.Write(clear, codeSize);
		if (pixels.Length == 0) {
			bits.Write(eof, codeSize);
			bits.Flush(s);
			return;
		}
		var prefix = (int)pixels[0];
		for (var i = 1; i < pixels.Length; i++) {
			var b = pixels[i];
			var key = (prefix << 8) | b;
			if (dict.TryGetValue(key, out var code)) {
				prefix = code;
				continue;
			}
			bits.Write(prefix, codeSize);
			if (next < 4096) {
				dict[key] = next++;
				if (next == (1 << codeSize) && codeSize < 12)
					codeSize++;
			}
			else {
				bits.Write(clear, codeSize);
				dict.Clear();
				codeSize = minBits + 1;
				next = eof + 1;
			}
			prefix = b;
		}
		bits.Write(prefix, codeSize);
		bits.Write(eof, codeSize);
		bits.Flush(s);
	}

	static void writeu16(Stream s, int v) {
		s.WriteByte((byte)v);
		s.WriteByte((byte)(v >> 8));
	}

	sealed class BitPack {
		int acc, nbits;
		readonly List<byte> raw = new();

		public void Write(int code, int bits) {
			acc |= code << nbits;
			nbits += bits;
			while (nbits >= 8) {
				raw.Add((byte)acc);
				acc >>= 8;
				nbits -= 8;
			}
		}

		public void Flush(Stream s) {
			if (nbits > 0) raw.Add((byte)acc);
			var i = 0;
			while (i < raw.Count) {
				var n = Math.Min(255, raw.Count - i);
				s.WriteByte((byte)n);
				for (var k = 0; k < n; k++)
					s.WriteByte(raw[i + k]);
				i += n;
			}
			s.WriteByte(0);
		}
	}

	sealed class ColorBox {
		public List<uint> Pixels;
		public ColorBox(List<uint> px) { Pixels = px; }

		public int Range {
			get {
				byte minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
				foreach (var p in Pixels) {
					var b = (byte)(p & 0xFF);
					var g = (byte)((p >> 8) & 0xFF);
					var r = (byte)((p >> 16) & 0xFF);
					if (r < minR) minR = r; if (r > maxR) maxR = r;
					if (g < minG) minG = g; if (g > maxG) maxG = g;
					if (b < minB) minB = b; if (b > maxB) maxB = b;
				}
				return Math.Max(maxR - minR, Math.Max(maxG - minG, maxB - minB));
			}
		}

		public uint Average {
			get {
				long r = 0, g = 0, b = 0;
				foreach (var p in Pixels) {
					b += p & 0xFF;
					g += (p >> 8) & 0xFF;
					r += (p >> 16) & 0xFF;
				}
				var n = Math.Max(1, Pixels.Count);
				return ((uint)(r / n) << 16) | ((uint)(g / n) << 8) | (uint)(b / n);
			}
		}

		public void Split(out ColorBox a, out ColorBox b) {
			byte minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
			foreach (var p in Pixels) {
				var bb = (byte)(p & 0xFF);
				var g = (byte)((p >> 8) & 0xFF);
				var r = (byte)((p >> 16) & 0xFF);
				if (r < minR) minR = r; if (r > maxR) maxR = r;
				if (g < minG) minG = g; if (g > maxG) maxG = g;
				if (bb < minB) minB = bb; if (bb > maxB) maxB = bb;
			}
			var rr = maxR - minR;
			var rg = maxG - minG;
			var rb = maxB - minB;
			var channel = 0;
			if (rg >= rr && rg >= rb) channel = 1;
			else if (rb >= rr && rb >= rg) channel = 2;
			Pixels.Sort((x, y) => {
				int vx = channel == 0 ? (int)((x >> 16) & 0xFF)
					: channel == 1 ? (int)((x >> 8) & 0xFF) : (int)(x & 0xFF);
				int vy = channel == 0 ? (int)((y >> 16) & 0xFF)
					: channel == 1 ? (int)((y >> 8) & 0xFF) : (int)(y & 0xFF);
				return vx.CompareTo(vy);
			});
			var mid = Pixels.Count / 2;
			if (mid < 1) mid = 1;
			if (mid >= Pixels.Count) mid = Pixels.Count - 1;
			a = new ColorBox(Pixels.GetRange(0, mid));
			b = new ColorBox(Pixels.GetRange(mid, Pixels.Count - mid));
		}
	}
}
