using NAudio.Wave;

namespace ScreenKit;

/// <summary>
/// 录制线程上把设备格式的音频块变成系统 AAC 能接受的 16-bit PCM。
/// 采样率只使用 44100 或 48000。麦和扬声器都有时先各自转换再相加。
/// </summary>
sealed class MfLivePcm {
	readonly int outRate;
	readonly int outCh;
	readonly Feed loop = new();
	readonly Feed mic = new();
	byte[] scratch;
	bool logged;

	public MfLivePcm(int rate, int channels) {
		outRate = rate > 0 ? rate : 44100;
		outCh = channels <= 1 ? 1 : 2;
	}

	public void Bind(WaveFormat loopFmt, WaveFormat micFmt) {
		if (loopFmt != null) loop.bind(loopFmt, outRate, outCh);
		if (micFmt != null) mic.bind(micFmt, outRate, outCh);
	}

	public void Drain(AudioCapture cap, MfH264Writer writer, bool flush) {
		if (cap == null || writer == null) return;
		while (cap.TryTake(0, out var a)) loop.add(a);
		while (cap.TryTake(1, out var b)) mic.add(b);
		if (flush) {
			loop.Flushing = true;
			mic.Flushing = true;
		}
		loop.pump();
		mic.pump();
		var frameBytes = outCh * 2;
		if (scratch == null || scratch.Length < 1024 * frameBytes)
			scratch = new byte[1024 * frameBytes];
		while (true) {
			var n = pull(scratch);
			if (n <= 0) break;
			if (!logged) {
				logged = true;
				RecordLog.Step("mf_audio_write", $"bytes={n} hz={outRate} ch={outCh}");
			}
			writer.WritePcm(scratch, n);
		}
	}

	int pull(byte[] dst) {
		var fb = outCh * 2;
		var cap = dst.Length / fb;
		if (cap <= 0) return 0;
		var na = loop.On ? loop.OutFrames : 0;
		var nb = mic.On ? mic.OutFrames : 0;
		int n;
		if (loop.On && mic.On) n = Math.Min(na, nb);
		else if (loop.On) n = na;
		else n = nb;
		var flushing = loop.Flushing || mic.Flushing;
		if (n == 0 && flushing && loop.On && mic.On)
			n = Math.Max(na, nb);
		if (n <= 0) return 0;
		if (n > cap) n = cap;
		for (var i = 0; i < n; i++) {
			for (var c = 0; c < outCh; c++) {
				float v = 0;
				if (loop.On && i < na) v += loop.at(i, c);
				if (mic.On && i < nb) v += mic.at(i, c);
				if (v > 1f) v = 1f;
				else if (v < -1f) v = -1f;
				var s = (short)(v * 32767f);
				var o = (i * outCh + c) * 2;
				dst[o] = (byte)s;
				dst[o + 1] = (byte)(s >> 8);
			}
		}
		if (loop.On) loop.consume(Math.Min(n, na));
		if (mic.On) mic.consume(Math.Min(n, nb));
		return n * fb;
	}

	sealed class Feed {
		static readonly Guid PcmSub = new("00000001-0000-0010-8000-00aa00389b71");
		static readonly Guid FloatSub = new("00000003-0000-0010-8000-00aa00389b71");

		WaveFormat fmt;
		bool isFloat;
		int ch = 1;
		int srcRate = 44100;
		int dstRate = 44100;
		int dstCh = 1;
		double pos;
		byte[] carry;
		readonly List<float> samples = new();
		readonly List<float> ready = new();

		public bool On { get; private set; }
		public bool Flushing { get; set; }
		public int OutFrames => ready.Count / Math.Max(1, dstCh);

		public void bind(WaveFormat format, int rate, int channelsOut) {
			fmt = format ?? throw new ArgumentNullException(nameof(format));
			dstRate = rate > 0 ? rate : 44100;
			dstCh = channelsOut <= 1 ? 1 : 2;
			ch = Math.Max(1, fmt.Channels);
			srcRate = fmt.SampleRate;
			if (srcRate < 1000)
				throw new InvalidOperationException("采样率无效: " + fmt);
			var sub = fmt is WaveFormatExtensible ex ? ex.SubFormat : Guid.Empty;
			isFloat = fmt.Encoding == WaveFormatEncoding.IeeeFloat || sub == FloatSub;
			var pcm = fmt.Encoding == WaveFormatEncoding.Pcm || sub == PcmSub;
			if (!isFloat && !pcm)
				throw new InvalidOperationException("不支持的音频格式: " + fmt);
			if (!isFloat && fmt.BitsPerSample != 16 && fmt.BitsPerSample != 32)
				throw new InvalidOperationException("不支持的位深: " + fmt);
			On = true;
		}

		public void add(byte[] src) {
			if (!On || src == null || src.Length == 0) return;
			var blob = src;
			if (carry != null && carry.Length > 0) {
				blob = new byte[carry.Length + src.Length];
				Buffer.BlockCopy(carry, 0, blob, 0, carry.Length);
				Buffer.BlockCopy(src, 0, blob, carry.Length, src.Length);
			}
			var align = Math.Max(1, fmt.BlockAlign);
			var n = blob.Length / align * align;
			var frames = n / align;
			for (var f = 0; f < frames; f++) {
				var o = f * align;
				for (var c = 0; c < ch; c++)
					samples.Add(readone(blob, o, c));
			}
			var rest = blob.Length - n;
			if (rest > 0) {
				carry = new byte[rest];
				Buffer.BlockCopy(blob, n, carry, 0, rest);
			}
			else carry = null;
		}

		public void pump() {
			if (!On) return;
			var step = (double)srcRate / dstRate;
			var n = samples.Count / ch;
			while (true) {
				var i0 = (int)pos;
				if (Flushing) {
					if (i0 >= n) break;
				}
				else if (i0 + 1 >= n) break;
				var i1 = i0 + 1 < n ? i0 + 1 : i0;
				var frac = (float)(pos - i0);
				for (var c = 0; c < dstCh; c++) {
					var s0 = pick(i0, c);
					var s1 = pick(i1, c);
					ready.Add(s0 + (s1 - s0) * frac);
				}
				pos += step;
			}
			var drop = (int)pos;
			if (drop > 0 && drop * ch <= samples.Count) {
				samples.RemoveRange(0, drop * ch);
				pos -= drop;
			}
		}

		public float at(int frame, int c) => ready[frame * dstCh + c];

		public void consume(int frames) {
			if (frames <= 0) return;
			var n = frames * dstCh;
			if (n > ready.Count) n = ready.Count;
			ready.RemoveRange(0, n);
		}

		float pick(int frame, int oc) {
			var b = frame * ch;
			if (dstCh == 1) {
				float s = 0;
				for (var i = 0; i < ch; i++) s += samples[b + i];
				return s / ch;
			}
			if (ch == 1) return samples[b];
			return samples[b + (oc < ch ? oc : 0)];
		}

		float readone(byte[] b, int frameOff, int c) {
			if (isFloat)
				return BitConverter.ToSingle(b, frameOff + c * 4);
			if (fmt.BitsPerSample == 16) {
				var o = frameOff + c * 2;
				var s = (short)(b[o] | (b[o + 1] << 8));
				return s / 32768f;
			}
			return BitConverter.ToInt32(b, frameOff + c * 4) / 2147483648f;
		}
	}
}
