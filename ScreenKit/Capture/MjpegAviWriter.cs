using System.Text;

namespace ScreenKit;

/// <summary>MJPEG AVI：每帧一张 JPEG，不依赖 FFmpeg。有声时停录后把 WAV 以 PCM 封进 AVI。</summary>
sealed class MjpegAviWriter : IRecordVideoSink {
	const int JpegQuality = 80;
	const long SizeCap = 1900L * 1024 * 1024;

	readonly int srcW, srcH, fps;
	readonly AviFile avi;
	byte[] held;
	long frameIndex;
	bool disposed;

	public int OutWidth { get; }
	public int OutHeight { get; }
	public string CodecName { get; }
	public string OpenedEncoder => "MJPG";

	public MjpegAviWriter(string path, int captureW, int captureH, RecordOptions opt) {
		if (path == null) throw new ArgumentNullException(nameof(path));
		opt ??= new RecordOptions();
		opt.Clamp();
		srcW = Math.Max(2, captureW / 2 * 2);
		srcH = Math.Max(2, captureH / 2 * 2);
		opt.FitSize(srcW, srcH, out var ow, out var oh);
		OutWidth = ow;
		OutHeight = oh;
		fps = opt.Fps;
		CodecName = opt.Codec;
		if (ow < 16 || oh < 16) throw new ArgumentException("录制区域过小");
		avi = new AviFile();
		avi.Begin(path, ow, oh, fps, 0, 0);
	}

	public void WriteBgra(byte[] bgra, int stride, long pts) {
		if (disposed) throw new ObjectDisposedException(nameof(MjpegAviWriter));
		if (bgra == null) return;
		if (pts < frameIndex) pts = frameIndex;
		var jpeg = RecordBgra.Jpeg(bgra, stride, srcW, srcH, OutWidth, OutHeight, JpegQuality);
		var gap = pts - frameIndex;
		// 漏掉的时刻重复上一张。把新画面填回过去，播放时画面会早于声音。
		var fill = held ?? jpeg;
		for (var i = 0; i < gap; i++)
			writevideo(fill);
		writevideo(jpeg);
		held = jpeg;
		frameIndex = pts + 1;
	}

	void writevideo(byte[] jpeg) {
		if (avi.Length + jpeg.Length > SizeCap)
			throw new InvalidOperationException("MJPEG AVI 超过约 1.9GB，请缩短录制或改用 H.264");
		avi.WriteVideo(jpeg);
	}

	public void Finish() {
		try { avi?.Finish(); } catch { }
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		try { Finish(); } catch { }
		avi?.Dispose();
	}

	/// <summary>把已写好的无声 MJPEG AVI 与 WAV 合成有声 AVI。</summary>
	public static void MuxPcm(string aviPath, string wavPath, string outPath, int rate, bool mono) {
		if (!AviFile.TryReadVideo(aviPath, out var info))
			throw new InvalidOperationException("无法读取 MJPEG AVI");
		using var pcm = new RecordPcm(wavPath, rate, mono);
		using var dst = new AviFile();
		dst.Begin(outPath, info.Width, info.Height, info.Fps, pcm.Rate, pcm.Channels);
		var block = pcm.BlockAlign;
		long audioBytes = 0;
		var frame = 0;
		foreach (var jpeg in info.Frames) {
			dst.WriteVideo(jpeg);
			frame++;
			long want = (long)frame * pcm.Rate * block / Math.Max(1, info.Fps);
			want -= want % block;
			var n = (int)Math.Min(int.MaxValue, want - audioBytes);
			if (n <= 0) continue;
			var buf = new byte[n];
			var got = pcm.Read16(buf, n);
			if (got <= 0) {
				dst.WriteAudio(buf);
				audioBytes += n;
			}
			else if (got < n) {
				var part = new byte[got];
				Buffer.BlockCopy(buf, 0, part, 0, got);
				dst.WriteAudio(part);
				audioBytes += got;
			}
			else {
				dst.WriteAudio(buf);
				audioBytes += n;
			}
		}
		dst.Finish();
	}

	/// <summary>音轨必须是 scale=1、rate=采样率，否则部分播放器会两倍速。</summary>
	internal static string CheckAudioClock() {
		var dir = Path.Combine(Path.GetTempPath(), "sk_mjpeg_clk");
		var avi = Path.Combine(dir, "v.avi");
		var wav = Path.Combine(dir, "a.wav");
		var dst = Path.Combine(dir, "av.avi");
		try {
			Directory.CreateDirectory(dir);
			var opt = new RecordOptions { Codec = "mjpeg", Fps = 10 };
			opt.Clamp();
			using (var w = new MjpegAviWriter(avi, 32, 32, opt)) {
				var bgra = new byte[32 * 32 * 4];
				for (var i = 0; i < 10; i++)
					w.WriteBgra(bgra, 32 * 4, i);
				w.Finish();
			}
			writepcm(wav, 22050, 22050);
			MuxPcm(avi, wav, dst, 22050, true);
			if (!readclock(dst, out var scale, out var rate, out var length, out var fmtSize))
				return "读不到音轨";
			if (scale != 1 || rate != 22050)
				return $"时间基 {scale}/{rate}";
			if (fmtSize != 16)
				return "strf " + fmtSize;
			if (length < 22000 || length > 22100)
				return "样本数 " + length;
			return null;
		}
		catch (Exception ex) { return ex.Message; }
		finally {
			try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
		}
	}

	static void writepcm(string path, int rate, int samples) {
		using var fs = File.Create(path);
		var data = samples * 2;
		void raw(byte[] b) => fs.Write(b, 0, b.Length);
		raw(Encoding.ASCII.GetBytes("RIFF"));
		raw(BitConverter.GetBytes(36 + data));
		raw(Encoding.ASCII.GetBytes("WAVEfmt "));
		raw(BitConverter.GetBytes(16));
		raw(BitConverter.GetBytes((short)1));
		raw(BitConverter.GetBytes((short)1));
		raw(BitConverter.GetBytes(rate));
		raw(BitConverter.GetBytes(rate * 2));
		raw(BitConverter.GetBytes((short)2));
		raw(BitConverter.GetBytes((short)16));
		raw(Encoding.ASCII.GetBytes("data"));
		raw(BitConverter.GetBytes(data));
		fs.Write(new byte[data], 0, data);
	}

	static bool readclock(string path, out int scale, out int rate, out int length, out int fmtSize) {
		scale = rate = length = fmtSize = 0;
		var data = File.ReadAllBytes(path);
		var pos = 12;
		while (pos + 8 <= data.Length) {
			var id = Encoding.ASCII.GetString(data, pos, 4);
			var size = BitConverter.ToInt32(data, pos + 4);
			var body = pos + 8;
			if (size < 0 || body + size > data.Length) return false;
			if (id == "LIST") {
				var typ = Encoding.ASCII.GetString(data, body, 4);
				if (typ == "strl")
					readstrl(data, body + 4, body + size, ref scale, ref rate, ref length, ref fmtSize);
				else if (typ == "hdrl")
					pos = body + 4;
			}
			if (id != "LIST" || Encoding.ASCII.GetString(data, body, 4) != "hdrl")
				pos = body + size + (size & 1);
		}
		return rate > 0 && fmtSize > 0;
	}

	static void readstrl(byte[] data, int start, int end, ref int scale, ref int rate, ref int length, ref int fmtSize) {
		var pos = start;
		while (pos + 8 <= end) {
			var id = Encoding.ASCII.GetString(data, pos, 4);
			var size = BitConverter.ToInt32(data, pos + 4);
			var body = pos + 8;
			if (size < 0 || body + size > data.Length) return;
			if (id == "strh" && size >= 48 && Encoding.ASCII.GetString(data, body, 4) == "auds") {
				scale = BitConverter.ToInt32(data, body + 20);
				rate = BitConverter.ToInt32(data, body + 24);
				length = BitConverter.ToInt32(data, body + 32);
			}
			else if (id == "strf" && size >= 16 && data[body] == 1)
				fmtSize = size;
			pos = body + size + (size & 1);
		}
	}

	sealed class VideoInfo {
		public int Width, Height, Fps;
		public List<byte[]> Frames;
	}

	/// <summary>经典 RIFF AVI（约 2GB 内）。</summary>
	sealed class AviFile : IDisposable {
		FileStream fs;
		long riffSizePos, avihFramesPos, avihMaxBytesPos, avihSuggestPos;
		long strhLenPos, strhSuggestPos, audLenPos, audSuggestPos, moviSizePos, moviFccPos;
		readonly List<(string id, int offset, int len)> index = new();
		int frames, maxJpeg, maxAud, fps = 1, audBlock = 1, audRate;
		long audioBytes;
		bool withAudio, finished;

		public long Length => fs?.Length ?? 0;

		public void Begin(string path, int w, int h, int frameRate, int sampleRate, int channels) {
			fps = Math.Max(1, frameRate);
			withAudio = channels > 0 && sampleRate > 0;
			audRate = Math.Max(1, sampleRate);
			audBlock = withAudio ? channels * 2 : 1;
			var dir = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
			fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
			fcc("RIFF");
			riffSizePos = fs.Position;
			u32(0);
			fcc("AVI ");
			fcc("LIST");
			var hdrlSizePos = fs.Position;
			u32(0);
			var hdrlStart = fs.Position;
			fcc("hdrl");
			fcc("avih");
			u32(56);
			u32((uint)(1000000 / fps));
			avihMaxBytesPos = fs.Position;
			u32(0);
			u32(0);
			u32(withAudio ? 0x110u : 0x10u);
			avihFramesPos = fs.Position;
			u32(0);
			u32(0);
			u32(withAudio ? 2u : 1u);
			avihSuggestPos = fs.Position;
			u32(0);
			u32((uint)w);
			u32((uint)h);
			u32(0); u32(0); u32(0); u32(0);
			writestrh("vids", "MJPG", 1, fps, 0, w, h, video: true);
			// 声音的一格是一个采样（dwScale=1，dwRate=采样率）。
			// 若写成 dwScale=块字节、dwRate=每秒字节，按「样本数/dwRate」计时的播放器会把声音放成两倍速。
			if (withAudio)
				writestrh("auds", null, 1, audRate, audBlock, 0, 0, video: false);
			patch(hdrlSizePos, (int)(fs.Position - hdrlStart));
			fcc("LIST");
			moviSizePos = fs.Position;
			u32(0);
			moviFccPos = fs.Position;
			fcc("movi");
		}

		void writestrh(string typ, string handler, int scale, int rate, int sampleSize, int w, int h, bool video) {
			fcc("LIST");
			var listSizePos = fs.Position;
			u32(0);
			var start = fs.Position;
			fcc("strl");
			fcc("strh");
			u32(56);
			fcc(typ);
			if (handler != null && handler.Length == 4) fcc(handler);
			else u32(0);
			u32(0);
			u16(0); u16(0);
			u32(0);
			u32((uint)scale);
			u32((uint)rate);
			u32(0);
			if (video) strhLenPos = fs.Position;
			else audLenPos = fs.Position;
			u32(0);
			if (video) strhSuggestPos = fs.Position;
			else audSuggestPos = fs.Position;
			u32((uint)sampleSize);
			u32(0xFFFFFFFFu);
			u32((uint)sampleSize);
			u16(0); u16(0); u16((ushort)w); u16((ushort)h);
			if (video) {
				fcc("strf");
				u32(40);
				u32(40);
				u32((uint)w);
				u32((uint)h);
				u16(1);
				u16(24);
				fcc("MJPG");
				u32(0); u32(0); u32(0); u32(0); u32(0);
			}
			else {
				// PCM 用 16 字节 WAVEFORMAT，不带 cbSize。
				fcc("strf");
				u32(16);
				u16(1);
				u16((ushort)Math.Max(1, audBlock / 2));
				u32((uint)audRate);
				u32((uint)(audRate * Math.Max(1, audBlock)));
				u16((ushort)audBlock);
				u16(16);
			}
			patch(listSizePos, (int)(fs.Position - start));
		}

		public void WriteVideo(byte[] jpeg) {
			if (jpeg == null || jpeg.Length == 0) return;
			if (jpeg.Length > maxJpeg) maxJpeg = jpeg.Length;
			chunk("00dc", jpeg);
			frames++;
		}

		public void WriteAudio(byte[] pcm) {
			if (!withAudio || pcm == null || pcm.Length == 0) return;
			if (pcm.Length > maxAud) maxAud = pcm.Length;
			chunk("01wb", pcm);
			audioBytes += pcm.Length;
		}

		public void Finish() {
			if (finished || fs == null) return;
			finished = true;
			patch(moviSizePos, (int)(fs.Position - moviFccPos));
			fcc("idx1");
			u32((uint)(index.Count * 16));
			foreach (var e in index) {
				fcc(e.id);
				u32(0x10u);
				u32((uint)e.offset);
				u32((uint)e.len);
			}
			patch(riffSizePos, (int)(fs.Length - 8));
			patch(avihFramesPos, frames);
			patch(avihMaxBytesPos, maxJpeg * fps);
			patch(avihSuggestPos, maxJpeg);
			patch(strhLenPos, frames);
			patch(strhSuggestPos, maxJpeg);
			if (withAudio && audLenPos > 0)
				patch(audLenPos, (int)(audioBytes / Math.Max(1, audBlock)));
			if (withAudio && audSuggestPos > 0)
				patch(audSuggestPos, Math.Max(maxAud, audBlock));
			fs.Flush();
		}

		public void Dispose() {
			try { Finish(); } catch { }
			try { fs?.Dispose(); } catch { }
			fs = null;
		}

		void chunk(string id, byte[] data) {
			var start = fs.Position;
			fcc(id);
			u32((uint)data.Length);
			fs.Write(data, 0, data.Length);
			if ((data.Length & 1) != 0) fs.WriteByte(0);
			index.Add((id, (int)(start - moviFccPos), data.Length));
		}

		void fcc(string s) => fs.Write(Encoding.ASCII.GetBytes(s), 0, 4);

		void u32(uint v) => fs.Write(BitConverter.GetBytes(v), 0, 4);

		void u16(ushort v) => fs.Write(BitConverter.GetBytes(v), 0, 2);

		void patch(long pos, int value) {
			var cur = fs.Position;
			fs.Position = pos;
			u32((uint)value);
			fs.Position = cur;
		}

		public static bool TryReadVideo(string path, out VideoInfo info) {
			info = null;
			try {
				using var fs = File.OpenRead(path);
				var hdr = new byte[12];
				if (fs.Read(hdr, 0, 12) < 12) return false;
				if (Encoding.ASCII.GetString(hdr, 0, 4) != "RIFF") return false;
				if (Encoding.ASCII.GetString(hdr, 8, 4) != "AVI ") return false;
				int w = 0, h = 0, rate = 0, scale = 1;
				long moviData = 0;
				var moviSize = 0;
				while (fs.Position + 8 <= fs.Length) {
					var idb = new byte[8];
					if (fs.Read(idb, 0, 8) < 8) break;
					var id = Encoding.ASCII.GetString(idb, 0, 4);
					var size = BitConverter.ToInt32(idb, 4);
					if (size < 0) return false;
					var dataPos = fs.Position;
					if (id == "LIST") {
						var typb = new byte[4];
						if (fs.Read(typb, 0, 4) < 4) return false;
						var typ = Encoding.ASCII.GetString(typb);
						if (typ == "movi") {
							moviData = fs.Position;
							moviSize = size - 4;
							break;
						}
						if (typ == "hdrl")
							readhdrl(fs, dataPos + size, ref w, ref h, ref scale, ref rate);
						fs.Position = dataPos + size + (size & 1);
						continue;
					}
					fs.Position = dataPos + size + (size & 1);
				}
				if (moviData == 0 || w < 2 || h < 2) return false;
				if (scale <= 0) scale = 1;
				if (rate <= 0) rate = 24;
				var frames = new List<byte[]>();
				var end = moviData + moviSize;
				fs.Position = moviData;
				while (fs.Position + 8 <= end) {
					var idb = new byte[8];
					if (fs.Read(idb, 0, 8) < 8) break;
					var id = Encoding.ASCII.GetString(idb, 0, 4);
					var size = BitConverter.ToInt32(idb, 4);
					if (size < 0 || fs.Position + size > fs.Length) break;
					if (id == "00dc" && size > 0) {
						var jpeg = new byte[size];
						if (fs.Read(jpeg, 0, size) < size) break;
						frames.Add(jpeg);
					}
					else fs.Position += size;
					if ((size & 1) != 0 && fs.Position < end) fs.Position++;
				}
				if (frames.Count == 0) return false;
				info = new VideoInfo {
					Width = w, Height = h,
					Fps = Math.Max(1, rate / scale),
					Frames = frames,
				};
				return true;
			}
			catch {
				info = null;
				return false;
			}
		}

		static void readhdrl(FileStream fs, long hdrlEnd, ref int w, ref int h, ref int scale, ref int rate) {
			while (fs.Position + 8 <= hdrlEnd) {
				var idb = new byte[8];
				if (fs.Read(idb, 0, 8) < 8) return;
				var id = Encoding.ASCII.GetString(idb, 0, 4);
				var size = BitConverter.ToInt32(idb, 4);
				var data = fs.Position;
				if (id == "avih" && size >= 40) {
					var buf = new byte[size];
					if (fs.Read(buf, 0, size) < size) return;
					w = BitConverter.ToInt32(buf, 32);
					h = BitConverter.ToInt32(buf, 36);
				}
				else if (id == "LIST") {
					var typb = new byte[4];
					if (fs.Read(typb, 0, 4) < 4) return;
					if (Encoding.ASCII.GetString(typb) == "strl")
						readstrl(fs, data + size, ref scale, ref rate);
					fs.Position = data + size + (size & 1);
					continue;
				}
				else fs.Position = data + size;
				if ((fs.Position & 1) != 0) fs.Position++;
			}
		}

		static void readstrl(FileStream fs, long end, ref int scale, ref int rate) {
			while (fs.Position + 8 <= end) {
				var idb = new byte[8];
				if (fs.Read(idb, 0, 8) < 8) return;
				var id = Encoding.ASCII.GetString(idb, 0, 4);
				var size = BitConverter.ToInt32(idb, 4);
				var data = fs.Position;
				if (id == "strh" && size >= 32) {
					var buf = new byte[Math.Min(size, 56)];
					if (fs.Read(buf, 0, buf.Length) < buf.Length) return;
					if (Encoding.ASCII.GetString(buf, 0, 4) == "vids") {
						scale = BitConverter.ToInt32(buf, 20);
						rate = BitConverter.ToInt32(buf, 24);
					}
					fs.Position = data + size + (size & 1);
					continue;
				}
				fs.Position = data + size + (size & 1);
			}
		}
	}
}
