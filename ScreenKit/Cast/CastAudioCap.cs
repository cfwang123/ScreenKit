using NAudio.Wave;

namespace ScreenKit;

sealed class CastAudioCap : IDisposable {
	WasapiLoopbackCapture cap;
	readonly object gate = new();
	byte[] pending;
	int pendingN;
	bool disposed;
	bool isFloat;
	int srcCh;

	public int SampleRate { get; private set; }
	public int Channels { get; private set; }

	public CastAudioCap() {
		cap = new WasapiLoopbackCapture();
		SampleRate = cap.WaveFormat.SampleRate;
		srcCh = cap.WaveFormat.Channels;
		Channels = srcCh <= 1 ? 1 : 2;
		isFloat = cap.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat;
		cap.DataAvailable += ondata;
		cap.StartRecording();
	}

	void ondata(object _, WaveInEventArgs e) {
		if (e.BytesRecorded <= 0) return;
		byte[] s16;
		int n;
		if (isFloat) {
			var samples = e.BytesRecorded / 4;
			var frames = samples / Math.Max(1, srcCh);
			n = frames * Channels * 2;
			s16 = new byte[n];
			var di = 0;
			for (int f = 0; f < frames; f++) {
				for (int c = 0; c < Channels; c++) {
					var si = (f * srcCh + Math.Min(c, srcCh - 1)) * 4;
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
		lock (gate) {
			var n = Math.Min(dst.Length, pendingN);
			if (n <= 0) return 0;
			Buffer.BlockCopy(pending, 0, dst, 0, n);
			pendingN -= n;
			if (pendingN > 0) Buffer.BlockCopy(pending, n, pending, 0, pendingN);
			return n;
		}
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		try { cap?.StopRecording(); } catch { }
		cap?.Dispose();
		cap = null;
	}
}
