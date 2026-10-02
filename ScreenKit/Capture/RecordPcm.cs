using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ScreenKit;

/// <summary>把录屏 WAV（含未规范化的浮点）读成目标采样率的 16-bit PCM。</summary>
sealed class RecordPcm : IDisposable {
	readonly AudioFileReader reader;
	readonly ISampleProvider samples;
	readonly int channels;

	public int Rate { get; }
	public int Channels => channels;
	public int BlockAlign => channels * 2;

	public RecordPcm(string path, int rate, bool mono) {
		reader = new AudioFileReader(path);
		ISampleProvider s = reader;
		Rate = rate > 0 ? rate : 22050;
		if (s.WaveFormat.SampleRate != Rate)
			s = new WdlResamplingSampleProvider(s, Rate);
		if (mono) {
			if (s.WaveFormat.Channels >= 2)
				s = new StereoToMonoSampleProvider(s) { LeftVolume = 0.5f, RightVolume = 0.5f };
			channels = 1;
		}
		else {
			if (s.WaveFormat.Channels == 1) s = s.ToStereo();
			channels = 2;
		}
		samples = s;
	}

	/// <summary>读满 <paramref name="bytes"/>（块对齐）。不够的尾部填静音。返回实际读到的字节。</summary>
	public int Read16(byte[] dst, int bytes) {
		if (dst == null || bytes <= 0) return 0;
		bytes -= bytes % BlockAlign;
		if (bytes > dst.Length) bytes = dst.Length - dst.Length % BlockAlign;
		var frames = bytes / BlockAlign;
		var floats = new float[frames * channels];
		var n = samples.Read(floats, 0, floats.Length);
		var got = n / channels * BlockAlign;
		for (var i = 0; i < n; i++) {
			var v = floats[i];
			if (v > 1f) v = 1f;
			else if (v < -1f) v = -1f;
			var s = (short)(v * 32767f);
			dst[i * 2] = (byte)s;
			dst[i * 2 + 1] = (byte)(s >> 8);
		}
		if (got < bytes) Array.Clear(dst, got, bytes - got);
		return got;
	}

	public void Dispose() {
		reader?.Dispose();
	}
}
