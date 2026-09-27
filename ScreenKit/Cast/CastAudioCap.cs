using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ScreenKit;

sealed class CastAudioCap : IDisposable {
	WasapiLoopbackCapture loop;
	WasapiCapture mic;
	readonly object gate = new();
	byte[] pending;
	int pendingN;
	bool disposed;
	bool rawFloat;
	int rawCh;
	bool mixmode;
	BufferedWaveProvider loopBuf;
	BufferedWaveProvider micBuf;
	MixingSampleProvider mixer;
	float[] fsamp;

	public int SampleRate { get; private set; }
	public int Channels { get; private set; }
	public bool FellBack { get; private set; }

	public CastAudioCap() : this(RecordAudioMode.Speakers) { }

	public CastAudioCap(RecordAudioMode mode) {
		if (mode == RecordAudioMode.Mic) {
			openraw(true);
			return;
		}
		if (mode == RecordAudioMode.MicAndSpeakers) {
			try {
				openmix();
				return;
			}
			catch {
				disposecaps();
				FellBack = true;
			}
		}
		openraw(false);
	}

	void openraw(bool useMic) {
		mixmode = false;
		if (useMic) mic = new WasapiCapture();
		else loop = new WasapiLoopbackCapture();
		var fmt = useMic ? mic.WaveFormat : loop.WaveFormat;
		SampleRate = fmt.SampleRate;
		rawCh = fmt.Channels;
		Channels = rawCh <= 1 ? 1 : 2;
		rawFloat = fmt.Encoding == WaveFormatEncoding.IeeeFloat;
		if (useMic) {
			mic.DataAvailable += (_, e) => ondata(e);
			mic.StartRecording();
		}
		else {
			loop.DataAvailable += (_, e) => ondata(e);
			loop.StartRecording();
		}
	}

	void openmix() {
		mixmode = true;
		SampleRate = 48000;
		Channels = 2;
		loop = new WasapiLoopbackCapture();
		loopBuf = makebuf(loop.WaveFormat);
		loop.DataAvailable += (_, e) => pushbuf(loopBuf, e);
		mic = new WasapiCapture();
		micBuf = makebuf(mic.WaveFormat);
		mic.DataAvailable += (_, e) => pushbuf(micBuf, e);
		var a = chain(loopBuf);
		var b = chain(micBuf);
		mixer = new MixingSampleProvider(new[] { a, b }) { ReadFully = false };
		loop.StartRecording();
		mic.StartRecording();
	}

	static BufferedWaveProvider makebuf(WaveFormat fmt) {
		return new BufferedWaveProvider(fmt) {
			DiscardOnBufferOverflow = true,
			BufferDuration = TimeSpan.FromMilliseconds(400),
		};
	}

	static void pushbuf(BufferedWaveProvider b, WaveInEventArgs e) {
		if (e.BytesRecorded > 0) b.AddSamples(e.Buffer, 0, e.BytesRecorded);
	}

	static bool isfloat(WaveFormat fmt) {
		if (fmt == null) return false;
		if (fmt.Encoding == WaveFormatEncoding.IeeeFloat) return true;
		return fmt.BitsPerSample == 32 && fmt.Encoding == WaveFormatEncoding.Extensible;
	}

	static ISampleProvider chain(BufferedWaveProvider b) {
		ISampleProvider s;
		if (isfloat(b.WaveFormat)) s = new WaveToSampleProvider(b);
		else if (b.WaveFormat.BitsPerSample == 16) s = new Pcm16BitToSampleProvider(b);
		else if (b.WaveFormat.BitsPerSample == 24) s = new Pcm24BitToSampleProvider(b);
		else if (b.WaveFormat.BitsPerSample == 32) s = new Pcm32BitToSampleProvider(b);
		else throw new NotSupportedException(b.WaveFormat.ToString());
		var ch = s.WaveFormat.Channels;
		if (ch == 1) s = new MonoToStereoSampleProvider(s);
		else if (ch != 2) {
			var m = new MultiplexingSampleProvider(new[] { s }, 2);
			m.ConnectInputToOutput(0, 0);
			if (ch > 1) m.ConnectInputToOutput(1, 1);
			s = m;
		}
		if (s.WaveFormat.SampleRate != 48000)
			s = new WdlResamplingSampleProvider(s, 48000);
		return s;
	}

	void ondata(WaveInEventArgs e) {
		if (e.BytesRecorded <= 0) return;
		byte[] s16;
		int n;
		if (rawFloat) {
			var samples = e.BytesRecorded / 4;
			var frames = samples / Math.Max(1, rawCh);
			n = frames * Channels * 2;
			s16 = new byte[n];
			var di = 0;
			for (int f = 0; f < frames; f++) {
				for (int c = 0; c < Channels; c++) {
					var si = (f * rawCh + Math.Min(c, rawCh - 1)) * 4;
					var v = BitConverter.ToSingle(e.Buffer, si);
					if (v > 1f) v = 1f;
					if (v < -1f) v = -1f;
					var sh = (short)(v * 32767f);
					s16[di++] = (byte)sh;
					s16[di++] = (byte)(sh >> 8);
				}
			}
		}
		else {
			n = e.BytesRecorded;
			s16 = e.Buffer;
		}
		lock (gate) {
			if (pending == null || pending.Length < pendingN + n)
				Array.Resize(ref pending, Math.Max(pendingN + n, 65536));
			Buffer.BlockCopy(s16, 0, pending, pendingN, n);
			pendingN += n;
		}
	}

	public int Take(byte[] dst) {
		if (mixmode) return takemix(dst);
		lock (gate) {
			var n = Math.Min(dst.Length, pendingN);
			if (n <= 0) return 0;
			Buffer.BlockCopy(pending, 0, dst, 0, n);
			pendingN -= n;
			if (pendingN > 0) Buffer.BlockCopy(pending, n, pending, 0, pendingN);
			return n;
		}
	}

	int takemix(byte[] dst) {
		var max = dst.Length / 2;
		if (max <= 0 || mixer == null) return 0;
		if (fsamp == null || fsamp.Length < max) fsamp = new float[max];
		var n = mixer.Read(fsamp, 0, max);
		for (int i = 0; i < n; i++) {
			var v = fsamp[i];
			if (v > 1f) v = 1f;
			else if (v < -1f) v = -1f;
			var sh = (short)(v * 32767f);
			dst[i * 2] = (byte)sh;
			dst[i * 2 + 1] = (byte)(sh >> 8);
		}
		return n * 2;
	}

	public void Clear() {
		lock (gate) pendingN = 0;
		try { loopBuf?.ClearBuffer(); } catch { }
		try { micBuf?.ClearBuffer(); } catch { }
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		disposecaps();
	}

	void disposecaps() {
		try { loop?.StopRecording(); } catch { }
		try { mic?.StopRecording(); } catch { }
		try { loop?.Dispose(); } catch { }
		try { mic?.Dispose(); } catch { }
		loop = null;
		mic = null;
		loopBuf = null;
		micBuf = null;
		mixer = null;
	}
}
