using NAudio.Wave;

namespace ScreenKit;

/// <summary>录音机：枚举麦克风，把 16-bit PCM 写成 WAV，并回读。</summary>
static class VoiceRec {
	public const int RATE = 44100;

	public sealed class Dev {
		public int Index;
		public string Name;
		public override string ToString() => Name ?? "";
	}

	/// <summary>录音、暂停后续录、播放内存里的一段 PCM。</summary>
	public sealed class Session : IDisposable {
		readonly object gate = new();
		WaveInEvent mic;
		WaveOutEvent waveOut;
		RawSourceWaveStream raw;
		MemoryStream playSrc;
		MemoryStream pcm = new();
		WaveFormat fmt = new(RATE, 16, 1);
		int device;
		bool recording;
		bool disposed;

		public bool Recording => recording;
		public WaveFormat Format { get { lock (gate) return fmt; } }
		public int ByteCount { get { lock (gate) return (int)pcm.Length; } }

		public event Action<int> Level;
		public event Action<string> Error;

		public void StartNew(int deviceIndex) {
			StopPlay();
			StopMic();
			lock (gate) {
				pcm.Dispose();
				pcm = new MemoryStream();
				fmt = new WaveFormat(RATE, 16, 1);
				device = deviceIndex;
			}
			startmic();
		}

		public void Resume(int deviceIndex) {
			if (recording) return;
			StopPlay();
			device = deviceIndex;
			startmic();
		}

		public void Pause() => StopMic();

		public void Play() {
			if (recording) Pause();
			byte[] data;
			WaveFormat f;
			lock (gate) {
				data = pcm.ToArray();
				f = fmt;
			}
			int frame = Math.Max(2, f.BlockAlign);
			if (data.Length < frame) return;
			int n = data.Length - (data.Length % frame);
			StopPlay();
			var src = new MemoryStream(data, 0, n, false);
			var rs = new RawSourceWaveStream(src, f);
			var o = new WaveOutEvent();
			try {
				o.Init(rs);
				o.Play();
			}
			catch {
				try { o.Dispose(); } catch { }
				try { rs.Dispose(); } catch { }
				try { src.Dispose(); } catch { }
				throw;
			}
			lock (gate) {
				playSrc = src;
				raw = rs;
				waveOut = o;
			}
		}

		public bool PlayEnded() {
			lock (gate) {
				if (waveOut == null) return true;
				return waveOut.PlaybackState != PlaybackState.Playing;
			}
		}

		public int PlayMs() {
			WaveOutEvent o;
			WaveFormat f;
			lock (gate) {
				o = waveOut;
				f = fmt;
			}
			if (o == null || f.SampleRate <= 0 || f.BlockAlign <= 0) return 0;
			try {
				long frames = o.GetPosition() / f.BlockAlign;
				return (int)(frames * 1000L / f.SampleRate);
			}
			catch { return 0; }
		}

		public byte[] CopyPcm() {
			lock (gate) return pcm.ToArray();
		}

		public void Load(byte[] data, int rate, int channels) {
			StopPlay();
			StopMic();
			int ch = channels > 0 ? channels : 1;
			int sr = rate > 0 ? rate : RATE;
			int frame = 2 * ch;
			int n = data == null ? 0 : data.Length - (data.Length % frame);
			lock (gate) {
				pcm.Dispose();
				pcm = new MemoryStream();
				if (n > 0) pcm.Write(data, 0, n);
				fmt = new WaveFormat(sr, 16, ch);
			}
		}

		public void StopPlay() {
			WaveOutEvent o;
			RawSourceWaveStream rs;
			MemoryStream src;
			lock (gate) {
				o = waveOut;
				rs = raw;
				src = playSrc;
				waveOut = null;
				raw = null;
				playSrc = null;
			}
			if (o != null) {
				try { o.Stop(); } catch { }
				try { o.Dispose(); } catch { }
			}
			try { rs?.Dispose(); } catch { }
			try { src?.Dispose(); } catch { }
		}

		public void Dispose() {
			if (disposed) return;
			disposed = true;
			StopPlay();
			StopMic();
			lock (gate) {
				try { pcm?.Dispose(); } catch { }
				pcm = new MemoryStream();
			}
		}

		void startmic() {
			StopMic();
			var w = new WaveInEvent {
				DeviceNumber = device,
				WaveFormat = new WaveFormat(RATE, 16, 1),
				BufferMilliseconds = 50,
			};
			w.DataAvailable += ondata;
			w.RecordingStopped += onstopped;
			try { w.StartRecording(); }
			catch {
				w.DataAvailable -= ondata;
				w.RecordingStopped -= onstopped;
				try { w.Dispose(); } catch { }
				throw;
			}
			mic = w;
			recording = true;
		}

		void ondata(object sender, WaveInEventArgs e) {
			if (e.BytesRecorded <= 0) return;
			int n = e.BytesRecorded - (e.BytesRecorded % 2);
			if (n <= 0) return;
			lock (gate) {
				if (!recording) return;
				pcm.Write(e.Buffer, 0, n);
			}
			try { Level?.Invoke(Peak(e.Buffer, n)); } catch { }
		}

		void onstopped(object sender, StoppedEventArgs e) {
			if (e.Exception == null) return;
			recording = false;
			try { Error?.Invoke(e.Exception.Message); } catch { }
		}

		void StopMic() {
			var w = mic;
			mic = null;
			recording = false;
			if (w == null) return;
			w.DataAvailable -= ondata;
			w.RecordingStopped -= onstopped;
			try { w.StopRecording(); } catch { }
			try { w.Dispose(); } catch { }
		}
	}

	public static List<Dev> Devices() {
		var list = new List<Dev>();
		int n = WaveIn.DeviceCount;
		for (int i = 0; i < n; i++) {
			string name = "";
			try { name = WaveIn.GetCapabilities(i).ProductName; } catch { }
			name = (name ?? "").Trim();
			if (name.Length == 0) name = "#" + (i + 1);
			list.Add(new Dev { Index = i, Name = name });
		}
		return list;
	}

	public static string Clock(int ms) {
		if (ms < 0) ms = 0;
		int tenth = (ms / 100) % 10;
		int sec = ms / 1000;
		int s = sec % 60;
		int m = (sec / 60) % 60;
		int h = sec / 3600;
		if (h > 0) return $"{h:00}:{m:00}:{s:00}.{tenth}";
		return $"{m:00}:{s:00}.{tenth}";
	}

	public static int DurationMs(int pcmBytes, int rate, int channels) {
		int frame = 2 * Math.Max(1, channels);
		if (rate <= 0 || pcmBytes < frame) return 0;
		long frames = pcmBytes / frame;
		long ms = frames * 1000L / rate;
		if (ms > int.MaxValue) return int.MaxValue;
		return (int)ms;
	}

	public static int Peak(byte[] buf, int count) {
		if (buf == null || count <= 1) return 0;
		int n = count - (count % 2);
		if (n > buf.Length) n = buf.Length - (buf.Length % 2);
		int max = 0;
		for (int i = 0; i < n; i += 2) {
			int s = buf[i] | (buf[i + 1] << 8);
			if (s >= 32768) s -= 65536;
			if (s < 0) s = -s;
			if (s > max) max = s;
		}
		if (max >= 32767) return 100;
		return max * 100 / 32767;
	}

	public static void WriteWav(string path, byte[] pcm, int rate, int channels) {
		int ch = channels > 0 ? channels : 1;
		int sr = rate > 0 ? rate : RATE;
		int frame = 2 * ch;
		int n = pcm == null ? 0 : pcm.Length - (pcm.Length % frame);
		using var w = new WaveFileWriter(path, new WaveFormat(sr, 16, ch));
		if (n > 0) w.Write(pcm, 0, n);
	}

	public static byte[] LoadPcm(string path, out int rate, out int channels) {
		try {
			using var wr = new WaveFileReader(path);
			var f = wr.WaveFormat;
			if (f.Encoding == WaveFormatEncoding.Pcm && f.BitsPerSample == 16 && f.Channels > 0) {
				rate = f.SampleRate;
				channels = f.Channels;
				int len = (int)Math.Min(int.MaxValue, wr.Length);
				var buf = new byte[len];
				int got = 0;
				while (got < buf.Length) {
					int n = wr.Read(buf, got, buf.Length - got);
					if (n <= 0) break;
					got += n;
				}
				if (got == buf.Length) return buf;
				var cut = new byte[got];
				Buffer.BlockCopy(buf, 0, cut, 0, got);
				return cut;
			}
		}
		catch { }
		return loadfloat(path, out rate, out channels);
	}

	public static int SelfTest(Action<string> log) {
		int bad = 0;
		void fail(string m) { bad++; log("FAIL: " + m); }
		if (Clock(0) != "00:00.0") fail("clock 0 " + Clock(0));
		if (Clock(61500) != "01:01.5") fail("clock 61.5 " + Clock(61500));
		if (Clock(3661500) != "01:01:01.5") fail("clock hour " + Clock(3661500));
		if (Peak(new byte[8], 8) != 0) fail("peak silent");
		if (Peak(new byte[] { 0xFF, 0x7F }, 2) != 100) fail("peak max");
		if (DurationMs(RATE * 2, RATE, 1) != 1000) fail("duration 1s");
		var pcm = new byte[200 * 2];
		for (int i = 0; i < 200; i++) {
			short v = (short)(i * 100);
			pcm[i * 2] = (byte)(v & 0xFF);
			pcm[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
		}
		var path = Path.Combine(Path.GetTempPath(), "sk-vrec-" + Environment.TickCount + ".wav");
		try {
			WriteWav(path, pcm, RATE, 1);
			var back = LoadPcm(path, out var rate, out var ch);
			if (rate != RATE || ch != 1) fail("fmt " + rate + "x" + ch);
			if (back.Length != pcm.Length) fail("len " + back.Length);
			else if (back[10] != pcm[10] || back[11] != pcm[11]) fail("sample");
			else log("wav roundtrip " + back.Length);
		}
		catch (Exception ex) { fail(ex.Message); }
		finally { try { if (File.Exists(path)) File.Delete(path); } catch { } }
		try { log("mics " + Devices().Count); }
		catch (Exception ex) { fail("devices " + ex.Message); }
		log(bad == 0 ? "voicerec OK" : "voicerec FAIL " + bad);
		return bad == 0 ? 0 : 1;
	}

	static byte[] loadfloat(string path, out int rate, out int channels) {
		using var reader = new AudioFileReader(path);
		rate = reader.WaveFormat.SampleRate;
		channels = Math.Max(1, reader.WaveFormat.Channels);
		var buf = new float[channels * 4096];
		var ms = new MemoryStream();
		int n;
		while ((n = reader.Read(buf, 0, buf.Length)) > 0) {
			int frames = n / channels;
			for (int i = 0; i < frames * channels; i++) {
				float s = buf[i];
				if (s > 1f) s = 1f;
				if (s < -1f) s = -1f;
				short v = (short)Math.Round(s * 32767f);
				ms.WriteByte((byte)(v & 0xFF));
				ms.WriteByte((byte)((v >> 8) & 0xFF));
			}
		}
		return ms.ToArray();
	}
}
