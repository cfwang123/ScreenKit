using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ScreenKit;

/// <summary>录音机：只列出麦克风，可选同时录系统播放，写成 16-bit PCM WAV。</summary>
static class VoiceRec {
	public const int RATE = 44100;
	/// <summary>ENDPOINT_FORM_FACTOR：Microphone。</summary>
	const int FORM_MIC = 4;
	/// <summary>ENDPOINT_FORM_FACTOR：Handset。</summary>
	const int FORM_HANDSET = 6;
	const int SLACK_MS = 100;

	static readonly Guid SUB_FLOAT = new("00000003-0000-0010-8000-00aa00389b71");
	static readonly Guid SUB_PCM = new("00000001-0000-0010-8000-00aa00389b71");

	public sealed class Dev {
		public string Id;
		public string Name;
		public override string ToString() => Name ?? "";
	}

	/// <summary>录音、暂停后续录、播放内存里的一段 PCM。系统声音走默认播放设备的环回。</summary>
	public sealed class Session : IDisposable {
		readonly object gate = new();
		WaveOutEvent waveOut;
		RawSourceWaveStream raw;
		MemoryStream playSrc;
		MemoryStream pcm = new();
		WaveFormat fmt = new(RATE, 16, 1);
		WasapiCapture mic;
		WasapiLoopbackCapture loop;
		MMDeviceEnumerator micEn;
		MMDevice micDev;
		WaveFormat micFmt;
		WaveFormat loopFmt;
		Lane micLane;
		Lane sysLane;
		Thread worker;
		volatile bool run;
		volatile bool accept;
		volatile bool recording;
		volatile bool useMic;
		volatile bool useSys;
		long segStart;
		long segBase;
		bool disposed;

		public bool Recording => recording;
		public WaveFormat Format { get { lock (gate) return fmt; } }
		public int ByteCount { get { lock (gate) return (int)pcm.Length; } }

		public event Action<int> Level;
		public event Action<string> Error;

		public void StartNew(string deviceId, bool system) {
			StopPlay();
			stopcapture();
			lock (gate) {
				pcm.Dispose();
				pcm = new MemoryStream();
				fmt = new WaveFormat(RATE, 16, 1);
			}
			startcapture(deviceId, system);
		}

		public void Resume(string deviceId, bool system) {
			if (recording) return;
			StopPlay();
			startcapture(deviceId, system);
		}

		public void Pause() => stopcapture();

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
			stopcapture();
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
			stopcapture();
			lock (gate) {
				try { pcm?.Dispose(); } catch { }
				pcm = new MemoryStream();
			}
		}

		void startcapture(string deviceId, bool system) {
			stopcapture();
			bool wantMic = !string.IsNullOrWhiteSpace(deviceId);
			if (!wantMic && !system)
				throw new InvalidOperationException("no input");
			WasapiCapture cap = null;
			WasapiLoopbackCapture lp = null;
			MMDeviceEnumerator en = null;
			MMDevice dev = null;
			try {
				if (wantMic) {
					en = new MMDeviceEnumerator();
					dev = en.GetDevice(deviceId);
					cap = new WasapiCapture(dev);
					micFmt = cap.WaveFormat;
				}
				if (system) {
					lp = new WasapiLoopbackCapture();
					loopFmt = lp.WaveFormat;
				}
			}
			catch {
				try { cap?.Dispose(); } catch { }
				try { lp?.Dispose(); } catch { }
				try { dev?.Dispose(); } catch { }
				try { en?.Dispose(); } catch { }
				throw;
			}
			micLane = new Lane();
			sysLane = new Lane();
			useMic = cap != null;
			useSys = lp != null;
			mic = cap;
			loop = lp;
			micEn = en;
			micDev = dev;
			accept = true;
			if (cap != null) {
				cap.DataAvailable += onmic;
				cap.RecordingStopped += onstopped;
			}
			if (lp != null) {
				lp.DataAvailable += onsys;
				lp.RecordingStopped += onstopped;
			}
			try {
				cap?.StartRecording();
				lp?.StartRecording();
			}
			catch {
				stopcapture();
				throw;
			}
			lock (gate) {
				segBase = pcm.Length / 2;
				segStart = Compat.TickCount64;
			}
			run = true;
			recording = true;
			worker = new Thread(mixloop) { IsBackground = true, Name = "VoiceRec.Mix" };
			worker.Start();
			CaptureLog.Info($"VoiceRec mic={useMic} sys={useSys} micFmt={micFmt} loopFmt={loopFmt}");
		}

		void stopcapture() {
			var cap = mic;
			var lp = loop;
			mic = null;
			loop = null;
			if (cap != null) {
				try { cap.StopRecording(); } catch { }
				cap.DataAvailable -= onmic;
				cap.RecordingStopped -= onstopped;
				try { cap.Dispose(); } catch { }
			}
			if (lp != null) {
				try { lp.StopRecording(); } catch { }
				lp.DataAvailable -= onsys;
				lp.RecordingStopped -= onstopped;
				try { lp.Dispose(); } catch { }
			}
			try { micDev?.Dispose(); } catch { }
			micDev = null;
			try { micEn?.Dispose(); } catch { }
			micEn = null;
			accept = false;
			run = false;
			var t = worker;
			worker = null;
			if (t != null && t != Thread.CurrentThread) {
				try { t.Join(2000); } catch { }
			}
			recording = false;
			useMic = false;
			useSys = false;
			micLane = null;
			sysLane = null;
		}

		void onmic(object sender, WaveInEventArgs e) {
			if (!accept || e.BytesRecorded <= 0) return;
			var lane = micLane;
			var f = micFmt;
			if (lane == null || f == null) return;
			var mono = ToMono(e.Buffer, e.BytesRecorded, f);
			if (mono.Length > 0) lane.Add(mono, f.SampleRate);
		}

		void onsys(object sender, WaveInEventArgs e) {
			if (!accept || e.BytesRecorded <= 0) return;
			var lane = sysLane;
			var f = loopFmt;
			if (lane == null || f == null) return;
			var mono = ToMono(e.Buffer, e.BytesRecorded, f);
			if (mono.Length > 0) lane.Add(mono, f.SampleRate);
		}

		void onstopped(object sender, StoppedEventArgs e) {
			if (e.Exception == null || !recording) return;
			recording = false;
			run = false;
			try { Error?.Invoke(e.Exception.Message); } catch { }
		}

		void mixloop() {
			var a = new float[RATE];
			var b = new float[RATE];
			try {
				while (run) {
					drain(a, b, SLACK_MS);
					Thread.Sleep(30);
				}
				drain(a, b, 0);
				for (int i = 0; i < 20; i++) {
					if (drainraw(a, b) <= 0) break;
				}
			}
			catch (Exception ex) {
				try { Error?.Invoke(ex.Message); } catch { }
			}
		}

		void drain(float[] a, float[] b, int slackMs) {
			for (int i = 0; i < 8; i++) {
				int need = neednow(slackMs);
				if (need <= 0) return;
				if (need > a.Length) need = a.Length;
				writemix(a, b, need, pad: true);
			}
		}

		int neednow(int slackMs) {
			long elapsed = Compat.TickCount64 - segStart;
			if (elapsed < 0) elapsed = 0;
			long want = elapsed * RATE / 1000 - (long)slackMs * RATE / 1000;
			if (want < 0) want = 0;
			long have;
			lock (gate) have = pcm.Length / 2 - segBase;
			long n = want - have;
			if (n <= 0) return 0;
			if (n > int.MaxValue) return int.MaxValue;
			return (int)n;
		}

		int drainraw(float[] a, float[] b) {
			var ml = micLane;
			var sl = sysLane;
			int nMic = useMic && ml != null ? ml.Pull(a, a.Length, RATE, false) : 0;
			int nSys = useSys && sl != null ? sl.Pull(b, b.Length, RATE, false) : 0;
			int n = Math.Max(nMic, nSys);
			if (n <= 0) return 0;
			if (!useMic || nMic < n) Array.Clear(a, useMic ? nMic : 0, n - (useMic ? nMic : 0));
			if (!useSys || nSys < n) Array.Clear(b, useSys ? nSys : 0, n - (useSys ? nSys : 0));
			commit(a, b, n);
			return n;
		}

		void writemix(float[] a, float[] b, int need, bool pad) {
			var ml = micLane;
			var sl = sysLane;
			int nMic = useMic && ml != null ? ml.Pull(a, need, RATE, pad) : 0;
			int nSys = useSys && sl != null ? sl.Pull(b, need, RATE, pad) : 0;
			int n = pad ? need : Math.Max(nMic, nSys);
			if (n <= 0) return;
			if (!useMic) Array.Clear(a, 0, n);
			else if (nMic < n) Array.Clear(a, nMic, n - nMic);
			if (!useSys) Array.Clear(b, 0, n);
			else if (nSys < n) Array.Clear(b, nSys, n - nSys);
			commit(a, b, n);
		}

		void commit(float[] micS, float[] sysS, int n) {
			var raw = new byte[n * 2];
			int peak = 0;
			bool micOn = useMic;
			bool sysOn = useSys;
			for (int i = 0; i < n; i++) {
				float s = 0;
				if (micOn) s += micS[i];
				if (sysOn) s += sysS[i];
				s = MixClamp(s, 0);
				short v = (short)Math.Round(s * 32767f);
				raw[i * 2] = (byte)(v & 0xFF);
				raw[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
				int av = v == short.MinValue ? 32767 : Math.Abs(v);
				if (av > peak) peak = av;
			}
			lock (gate) pcm.Write(raw, 0, raw.Length);
			int level = peak >= 32767 ? 100 : peak * 100 / 32767;
			try { Level?.Invoke(level); } catch { }
		}
	}

	/// <summary>采集端点里只留麦克风和听筒，耳机、扬声器、线路、立体声混音都不算。</summary>
	public static bool IsMicForm(int formFactor) => formFactor == FORM_MIC || formFactor == FORM_HANDSET;

	public static float MixClamp(float a, float b) {
		float s = a + b;
		if (s > 1f) return 1f;
		if (s < -1f) return -1f;
		return s;
	}

	public static List<Dev> Devices() {
		var list = new List<Dev>();
		using var en = new MMDeviceEnumerator();
		string defId = "";
		try {
			using var def = en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
			defId = def?.ID ?? "";
		}
		catch { }
		MMDeviceCollection col;
		try { col = en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active); }
		catch { return list; }
		foreach (var d in col) {
			using (d) {
				int ff = formfactor(d);
				if (!IsMicForm(ff)) continue;
				string id = "";
				string name = "";
				try { id = d.ID ?? ""; } catch { }
				try { name = (d.FriendlyName ?? "").Trim(); } catch { }
				if (id.Length == 0) continue;
				if (name.Length == 0) name = "#" + (list.Count + 1);
				list.Add(new Dev { Id = id, Name = name });
			}
		}
		list.Sort((a, b) => {
			bool ad = a.Id.Equals(defId, StringComparison.OrdinalIgnoreCase);
			bool bd = b.Id.Equals(defId, StringComparison.OrdinalIgnoreCase);
			if (ad != bd) return ad ? -1 : 1;
			return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
		});
		return list;
	}

	static int formfactor(MMDevice d) {
		try {
			var v = d.Properties[PropertyKeys.PKEY_AudioEndpoint_FormFactor].Value;
			if (v == null) return -1;
			return Convert.ToInt32(v);
		}
		catch { return -1; }
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

	/// <summary>PCM 或 float 转成单声道 float，采样率仍是源格式。</summary>
	public static float[] ToMono(byte[] buffer, int bytes, WaveFormat fmt) {
		if (buffer == null || bytes <= 0 || fmt == null) return Array.Empty<float>();
		int ch = Math.Max(1, fmt.Channels);
		bool flt = isfloat(fmt);
		if (flt && fmt.BitsPerSample == 32) return frames(buffer, bytes, 4, ch, readfloat);
		if (fmt.BitsPerSample == 16) return frames(buffer, bytes, 2, ch, read16);
		if (fmt.BitsPerSample == 32 && !flt) return frames(buffer, bytes, 4, ch, read32);
		if (fmt.BitsPerSample == 24) return frames(buffer, bytes, 3, ch, read24);
		return Array.Empty<float>();
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
		if (!IsMicForm(FORM_MIC) || !IsMicForm(FORM_HANDSET)) fail("mic form");
		if (IsMicForm(5) || IsMicForm(3) || IsMicForm(1) || IsMicForm(2) || IsMicForm(10)) fail("playback form");
		if (Math.Abs(MixClamp(0.2f, -0.1f) - 0.1f) > 0.0001f) fail("mix");
		if (MixClamp(0.8f, 0.8f) != 1f || MixClamp(-0.8f, -0.8f) != -1f) fail("mix clamp");
		var mono = ToMono(new byte[] { 0x00, 0x00, 0xFF, 0x7F }, 4, new WaveFormat(RATE, 16, 2));
		if (mono.Length != 1) fail("mono len " + mono.Length);
		else if (mono[0] < 0.45f || mono[0] > 0.55f) fail("mono avg " + mono[0]);
		var lane = new Lane();
		var ones = new float[8];
		for (int i = 0; i < ones.Length; i++) ones[i] = 0.5f;
		lane.Add(ones, RATE);
		var got = new float[4];
		if (lane.Pull(got, 4, RATE, false) != 4) fail("lane pull");
		else if (got[0] < 0.49f || got[3] > 0.51f) fail("lane val " + got[0]);
		var tail = new float[10];
		if (lane.Pull(tail, 10, RATE, true) != 10) fail("lane pad");
		else if (tail[9] != 0f) fail("lane silence");
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
		try {
			var devs = Devices();
			log("mics " + devs.Count);
			foreach (var d in devs) log("mic " + d.Name);
		}
		catch (Exception ex) { fail("devices " + ex.Message); }
		log(bad == 0 ? "voicerec OK" : "voicerec FAIL " + bad);
		return bad == 0 ? 0 : 1;
	}

	static bool isfloat(WaveFormat fmt) {
		if (fmt.Encoding == WaveFormatEncoding.IeeeFloat) return true;
		if (fmt is WaveFormatExtensible ext) {
			if (ext.SubFormat == SUB_FLOAT) return true;
			if (ext.SubFormat == SUB_PCM) return false;
			if (ext.BitsPerSample == 32) return true;
		}
		return false;
	}

	static float[] frames(byte[] buffer, int bytes, int width, int ch, Func<byte[], int, float> read) {
		int framesN = (bytes / width) / ch;
		if (framesN <= 0) return Array.Empty<float>();
		var r = new float[framesN];
		for (int f = 0; f < framesN; f++) {
			float s = 0;
			for (int c = 0; c < ch; c++)
				s += read(buffer, (f * ch + c) * width);
			r[f] = s / ch;
		}
		return r;
	}

	static float readfloat(byte[] b, int o) => BitConverter.ToSingle(b, o);

	static float read16(byte[] b, int o) => BitConverter.ToInt16(b, o) / 32768f;

	static float read32(byte[] b, int o) => BitConverter.ToInt32(b, o) / 2147483648f;

	static float read24(byte[] b, int o) {
		int v = b[o] | (b[o + 1] << 8) | (b[o + 2] << 16);
		if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
		return v / 8388608f;
	}

	static byte[] loadfloat(string path, out int rate, out int channels) {
		using var reader = new AudioFileReader(path);
		rate = reader.WaveFormat.SampleRate;
		channels = Math.Max(1, reader.WaveFormat.Channels);
		var buf = new float[channels * 4096];
		var ms = new MemoryStream();
		int n;
		while ((n = reader.Read(buf, 0, buf.Length)) > 0) {
			int nframes = n / channels;
			for (int i = 0; i < nframes * channels; i++) {
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

	/// <summary>一路采集：按源采样率攒单声道，再拉成 44100。</summary>
	sealed class Lane {
		readonly List<float> src = new();
		int rate;
		double cursor;

		public void Add(float[] mono, int sampleRate) {
			if (mono == null || mono.Length == 0 || sampleRate <= 0) return;
			lock (src) {
				if (rate == 0) rate = sampleRate;
				else if (sampleRate != rate) mono = stretch(mono, sampleRate, rate);
				src.AddRange(mono);
			}
		}

		public int Pull(float[] dst, int count, int outRate, bool pad) {
			if (dst == null || count <= 0) return 0;
			if (count > dst.Length) count = dst.Length;
			lock (src) {
				int n = 0;
				if (rate > 0 && outRate > 0 && src.Count >= 2) {
					double step = (double)rate / outRate;
					while (n < count && cursor + 1 < src.Count) {
						int i0 = (int)cursor;
						double frac = cursor - i0;
						float a = src[i0];
						float b = src[i0 + 1];
						dst[n++] = (float)(a + (b - a) * frac);
						cursor += step;
					}
					int drop = (int)cursor;
					if (drop > 2000 && drop < src.Count) {
						src.RemoveRange(0, drop);
						cursor -= drop;
					}
				}
				if (pad && n < count) {
					Array.Clear(dst, n, count - n);
					return count;
				}
				return n;
			}
		}

		static float[] stretch(float[] samples, int from, int to) {
			double ratio = (double)to / from;
			int outLen = Math.Max(1, (int)(samples.Length * ratio));
			var result = new float[outLen];
			for (int i = 0; i < outLen; i++) {
				double srcAt = i / ratio;
				int i0 = (int)srcAt;
				double frac = srcAt - i0;
				float a = samples[Math.Min(i0, samples.Length - 1)];
				float b = samples[Math.Min(i0 + 1, samples.Length - 1)];
				result[i] = (float)(a + (b - a) * frac);
			}
			return result;
		}
	}
}
