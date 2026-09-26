using NAudio.Wave;

namespace ScreenKit;

sealed class CastAudioPlay : IDisposable {
	readonly WaveOutEvent wo;
	readonly BufferedWaveProvider buf;
	bool disposed;

	public CastAudioPlay(int sampleRate = 48000, int ch = 2) {
		buf = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, ch)) {
			DiscardOnBufferOverflow = true,
			BufferDuration = TimeSpan.FromMilliseconds(800)
		};
		wo = new WaveOutEvent { DesiredLatency = 200, NumberOfBuffers = 3 };
		wo.Init(new CastPcmOut(buf));
		wo.Volume = 1f;
		wo.Play();
	}

	public void Push(byte[] pcm) {
		if (disposed || pcm == null || pcm.Length == 0) return;
		try { buf.AddSamples(pcm, 0, pcm.Length); }
		catch { }
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		try { wo.Stop(); } catch { }
		wo.Dispose();
	}
}

sealed class CastPcmOut : IWaveProvider {
	readonly BufferedWaveProvider buf;

	public CastPcmOut(BufferedWaveProvider buf) { this.buf = buf; }

	public WaveFormat WaveFormat => buf.WaveFormat;

	public int Read(byte[] dst, int off, int n) {
		var got = buf.Read(dst, off, n);
		if (got < 0) got = 0;
		if (got < n) Array.Clear(dst, off + got, n - got);
		return n;
	}
}
