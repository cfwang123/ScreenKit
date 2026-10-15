using System.Runtime.InteropServices;
using System.Text;
using NAudio.Wave;

namespace ScreenKit;

/// <summary>摄像头：Media Foundation 枚举、预览帧、以及带可选麦克风的 MP4。</summary>
static class CamCap {
	const int FirstVideo = unchecked((int)0xFFFFFFFC);
	const int EndOfStream = 0x2;
	const int StreamError = 0x1;
	const int TypeChanged = 0x20;

	static readonly Guid SourceType = new("c60ac5fe-252a-478f-a0ef-bc8fa5f7cad3");
	static readonly Guid Vidcap = new("8ac3587a-4ae7-42d8-99e0-0a6013eef90f");
	static readonly Guid Friendly = new("60d0e559-52f8-4fa2-bbce-acdb34a8ec01");
	static readonly Guid SymLink = new("58f0aad8-22bf-4f8a-bb3d-d2c4978c6e2f");

	public sealed class Dev {
		public string Name;
		public string Link;
		public override string ToString() => string.IsNullOrEmpty(Name) ? Link : Name;
	}

	public static List<Dev> Devices() {
		var list = new List<Dev>();
		var up = false;
		try {
			MfApi.Startup();
			up = true;
			MfApi.Check(MfApi.MFCreateAttributes(out var attr, 2), "MFCreateAttributes cam");
			try {
				var key = SourceType;
				var kind = Vidcap;
				attr.SetGUID(ref key, ref kind);
				var arr = IntPtr.Zero;
				var count = 0;
				try {
					MfApi.Check(MFEnumDeviceSources(attr, out arr, out count), "MFEnumDeviceSources");
					for (var i = 0; i < count; i++) {
						var p = Marshal.ReadIntPtr(arr, i * IntPtr.Size);
						if (p == IntPtr.Zero) continue;
						var obj = Marshal.GetObjectForIUnknown(p);
						Marshal.Release(p);
						var a = obj as IMFAttributes;
						if (a == null) continue;
						try {
							var name = attrstr(a, Friendly);
							var link = attrstr(a, SymLink);
							if (string.IsNullOrWhiteSpace(link)) continue;
							if (string.IsNullOrWhiteSpace(name)) name = "Camera " + (list.Count + 1);
							list.Add(new Dev { Name = name.Trim(), Link = link });
						}
						finally { MfApi.Release(a); }
					}
				}
				finally { if (arr != IntPtr.Zero) Marshal.FreeCoTaskMem(arr); }
			}
			finally { MfApi.Release(attr); }
		}
		finally { if (up) try { MfApi.Shutdown(); } catch { } }
		return list;
	}

	/// <summary>把 RGB32 收成自上而下、无行填充的 BGRA。负 stride 时 origin 是缓冲区开头，首行在内存底部。</summary>
	public static byte[] CopyRgb32(byte[] src, int origin, int w, int h, int stride) {
		var tight = w * 4;
		if (stride == 0) stride = tight;
		var abs = Math.Abs(stride);
		if (abs < tight) abs = tight;
		var dst = new byte[tight * h];
		for (var y = 0; y < h; y++) {
			var at = stride > 0 ? origin + y * stride : origin + (h - 1 - y) * abs;
			Buffer.BlockCopy(src, at, dst, y * tight, tight);
		}
		return dst;
	}

	public static byte[] CopyRgb32(IntPtr ptr, int w, int h, int stride) {
		var tight = w * 4;
		if (stride == 0) stride = tight;
		var abs = Math.Abs(stride);
		if (abs < tight) abs = tight;
		var dst = new byte[tight * h];
		if (stride > 0) {
			for (var y = 0; y < h; y++)
				Marshal.Copy(ptr + y * stride, dst, y * tight, tight);
		}
		else {
			for (var y = 0; y < h; y++)
				Marshal.Copy(ptr + (h - 1 - y) * abs, dst, y * tight, tight);
		}
		return dst;
	}

	/// <summary>去掉奇数的末列末行。宽或高不足 2 时返回空。</summary>
	public static byte[] Even(byte[] src, int w, int h, out int ew, out int eh) {
		ew = w & ~1;
		eh = h & ~1;
		if (ew < 2 || eh < 2 || src == null) {
			ew = 0;
			eh = 0;
			return Array.Empty<byte>();
		}
		if (ew == w && eh == h) return src;
		var dst = new byte[ew * eh * 4];
		var ss = w * 4;
		var ds = ew * 4;
		for (var y = 0; y < eh; y++)
			Buffer.BlockCopy(src, y * ss, dst, y * ds, ds);
		return dst;
	}

	public sealed class Session : IDisposable {
		readonly object gate = new();
		Thread thread;
		volatile bool stop;
		byte[] frame;
		int fw, fh;
		string error;
		bool cleaned;

		IMFSourceReader reader;
		IMFMediaSource source;
		bool nv12;
		int width, height, stride;
		bool started;

		public string Error { get { lock (gate) return error; } }

		public static Session Open(string link) {
			if (string.IsNullOrWhiteSpace(link)) throw new ArgumentException("no camera");
			var s = new Session();
			var ready = new ManualResetEvent(false);
			s.thread = new Thread(() => s.run(link, ready)) { IsBackground = true, Name = "cam" };
			s.thread.Start();
			if (!ready.WaitOne(8000)) {
				s.Dispose();
				throw new InvalidOperationException("摄像头打开超时");
			}
			string err;
			lock (s.gate) err = s.error;
			if (err != null) {
				s.Dispose();
				throw new InvalidOperationException(err);
			}
			return s;
		}

		public bool TryCopy(out byte[] bgra, out int w, out int h) {
			lock (gate) {
				if (frame == null) {
					bgra = null;
					w = 0;
					h = 0;
					return false;
				}
				bgra = (byte[])frame.Clone();
				w = fw;
				h = fh;
				return true;
			}
		}

		public void Dispose() {
			stop = true;
			var t = thread;
			if (t != null && t.IsAlive && !ReferenceEquals(Thread.CurrentThread, t))
				t.Join(5000);
		}

		void run(string link, ManualResetEvent ready) {
			try {
				open(link);
			}
			catch (Exception ex) {
				lock (gate) error = ex.Message;
			}
			ready.Set();
			try {
				if (error == null) loop();
			}
			catch (Exception ex) {
				lock (gate) if (error == null) error = ex.Message;
			}
			finally { cleanup(); }
		}

		void open(string link) {
			MfApi.Startup();
			started = true;
			MfApi.Check(MfApi.MFCreateAttributes(out var attr, 2), "MFCreateAttributes source");
			try {
				var key = SourceType;
				var kind = Vidcap;
				attr.SetGUID(ref key, ref kind);
				var lk = SymLink;
				attr.SetString(ref lk, link);
				MfApi.Check(MFCreateDeviceSource(attr, out var raw), "MFCreateDeviceSource");
				var obj = Marshal.GetObjectForIUnknown(raw);
				Marshal.Release(raw);
				source = obj as IMFMediaSource;
				if (source == null) throw new InvalidOperationException("摄像头源无效");
			}
			finally { MfApi.Release(attr); }

			MfApi.Check(MfApi.MFCreateAttributes(out var ra, 1), "MFCreateAttributes reader");
			var punk = IntPtr.Zero;
			try {
				var ek = MfH264Reader.EnableVideoProcessing;
				ra.SetUINT32(ref ek, 1);
				punk = Marshal.GetIUnknownForObject(source);
				MfApi.Check(MFCreateSourceReaderFromMediaSource(punk, ra, out reader), "MFCreateSourceReaderFromMediaSource");
			}
			finally {
				if (punk != IntPtr.Zero) Marshal.Release(punk);
				MfApi.Release(ra);
			}

			if (!setrgb(reader) && !setnative(reader))
				throw new InvalidOperationException("摄像头不能输出 RGB32 或 NV12");
			readfmt();
		}

		void loop() {
			while (!stop) {
				var hr = reader.ReadSample(FirstVideo, 0, out _, out var flags, out _, out var sample);
				if (stop) {
					MfApi.Release(sample);
					break;
				}
				if (hr < 0) throw new InvalidOperationException("读摄像头失败 0x" + hr.ToString("X8"));
				if ((flags & StreamError) != 0) {
					MfApi.Release(sample);
					throw new InvalidOperationException("读摄像头失败");
				}
				if ((flags & EndOfStream) != 0) {
					MfApi.Release(sample);
					break;
				}
				if ((flags & TypeChanged) != 0) readfmt();
				if (sample == null) continue;
				IMFMediaBuffer buf = null;
				try {
					sample.ConvertToContiguousBuffer(out buf);
					buf.Lock(out var ptr, out _, out var cur);
					byte[] bgra;
					try {
						bgra = nv12
							? nv12tobgra(ptr, cur, width, height, stride)
							: CopyRgb32(ptr, width, height, stride);
					}
					finally { buf.Unlock(); }
					bgra = Even(bgra, width, height, out var ew, out var eh);
					if (ew < 2 || eh < 2) continue;
					lock (gate) {
						frame = bgra;
						fw = ew;
						fh = eh;
					}
				}
				finally {
					MfApi.Release(buf);
					MfApi.Release(sample);
				}
			}
		}

		void cleanup() {
			lock (gate) {
				if (cleaned) return;
				cleaned = true;
			}
			MfApi.Release(reader);
			reader = null;
			if (source != null) {
				try { source.Shutdown(); } catch { }
				MfApi.Release(source);
				source = null;
			}
			if (started) {
				started = false;
				try { MfApi.Shutdown(); } catch { }
			}
		}

		void readfmt() {
			reader.GetCurrentMediaType(FirstVideo, out var t);
			try {
				var sub = MfApi.SubType;
				t.GetGUID(ref sub, out var g);
				nv12 = g == MfH264Reader.FmtNv12;
				var fs = MfApi.FrameSize;
				t.GetUINT64(ref fs, out var packed);
				width = (int)((ulong)packed >> 32);
				height = (int)(packed & 0xFFFFFFFF);
				stride = 0;
				try {
					var sk = MfApi.Stride;
					t.GetUINT32(ref sk, out stride);
				}
				catch { stride = 0; }
			}
			finally { MfApi.Release(t); }
			if (width < 2 || height < 2) throw new InvalidOperationException("摄像头没有画面尺寸");
		}
	}

	/// <summary>把预览帧写成 H.264 MP4。麦克风打不开时继续无声录。</summary>
	public sealed class Movie : IDisposable {
		readonly object gate = new();
		readonly MfH264Writer writer;
		readonly int fps;
		readonly int t0;
		WaveInEvent mic;
		bool disposed;

		public int Width { get; }
		public int Height { get; }
		public bool WantedMic { get; }
		public bool HasMic { get { lock (gate) return mic != null; } }

		public Movie(string path, int w, int h, bool withMic) {
			Width = Math.Max(2, w / 2 * 2);
			Height = Math.Max(2, h / 2 * 2);
			WantedMic = withMic;
			var opt = new RecordOptions {
				Codec = "mf",
				Fps = 24,
				AudioHz = 44100,
				AudioMono = true,
				AudioEnabled = withMic,
				MaxSizeEnabled = false,
			};
			fps = opt.Fps;
			t0 = Environment.TickCount;
			writer = new MfH264Writer(path, Width, Height, opt, withMic);
			if (withMic) trymic();
		}

		public void Write(byte[] bgra, int stride) {
			var elapsed = Environment.TickCount - t0;
			if (elapsed < 0) elapsed = 0;
			var pts = (long)elapsed * fps / 1000;
			lock (gate) {
				if (disposed) return;
				writer.WriteBgra(bgra, stride, pts);
			}
		}

		public void Dispose() {
			WaveInEvent w;
			lock (gate) {
				if (disposed) return;
				disposed = true;
				w = mic;
				mic = null;
			}
			if (w != null) {
				w.DataAvailable -= onmic;
				try { w.StopRecording(); } catch { }
				try { w.Dispose(); } catch { }
			}
			lock (gate) {
				try { writer.Dispose(); } catch { }
			}
		}

		void trymic() {
			WaveInEvent w = null;
			try {
				w = new WaveInEvent {
					DeviceNumber = 0,
					WaveFormat = new WaveFormat(44100, 16, 1),
					BufferMilliseconds = 50,
				};
				w.DataAvailable += onmic;
				w.StartRecording();
				lock (gate) {
					if (disposed) throw new InvalidOperationException("closed");
					mic = w;
					w = null;
				}
			}
			catch {
				if (w != null) {
					w.DataAvailable -= onmic;
					try { w.Dispose(); } catch { }
				}
			}
		}

		void onmic(object sender, WaveInEventArgs e) {
			if (e.BytesRecorded <= 0) return;
			var n = e.BytesRecorded - e.BytesRecorded % 2;
			if (n <= 0) return;
			var copy = new byte[n];
			Buffer.BlockCopy(e.Buffer, 0, copy, 0, n);
			lock (gate) {
				if (disposed) return;
				try { writer.WritePcm(copy, n); } catch { }
			}
		}
	}

	public static int SelfTest(Action<string> log) {
		var bad = 0;
		void fail(string m) { bad++; log("FAIL: " + m); }
		var pad = new byte[3 * 12];
		pad[0] = 0x11;
		pad[12] = 0x22;
		var top = CopyRgb32(pad, 0, 2, 2, 12);
		if (top.Length != 16 || top[0] != 0x11 || top[8] != 0x22) fail("stride+");
		pad[0] = 0x22;
		pad[12] = 0x11;
		var bot = CopyRgb32(pad, 0, 2, 2, -12);
		if (bot.Length != 16 || bot[0] != 0x11 || bot[8] != 0x22) fail("stride-");
		var odd = new byte[3 * 2 * 4];
		odd[8] = 0x33;
		odd[20] = 0x44;
		var even = Even(odd, 3, 2, out var ew, out var eh);
		if (ew != 2 || eh != 2 || even.Length != 16 || even[0] != 0 || even.Length > 8 && even[8] == 0x44) fail("even");
		else if (even[0] != 0) fail("even pix");
		try {
			var list = Devices();
			log("cams " + list.Count);
			if (list.Count > 0) {
				using var s = Session.Open(list[0].Link);
				var t0 = Environment.TickCount;
				byte[] frame = null;
				var w = 0;
				var h = 0;
				while (Environment.TickCount - t0 < 3000) {
					if (s.TryCopy(out frame, out w, out h)) break;
					Thread.Sleep(40);
				}
				if (frame == null) fail("no frame");
				else if ((w & 1) != 0 || (h & 1) != 0 || w < 2 || h < 2 || frame.Length != w * h * 4) fail("frame " + w + "x" + h);
				else log("frame " + w + "x" + h);
			}
		}
		catch (Exception ex) { fail(ex.Message); }
		var root = NativeRuntime.BaseDir;
		var js = Path.Combine(root, "web", "tools", "u-cam.js");
		var page = Path.Combine(root, "web", "tools.html");
		var bundle = Path.Combine(root, "web", "tools.js");
		if (!File.Exists(js) || !File.Exists(page) || !File.Exists(bundle)) fail("missing web");
		else {
			var src = File.ReadAllText(js);
			var html = File.ReadAllText(page);
			var joined = File.ReadAllText(bundle);
			if (src.IndexOf("getUserMedia", StringComparison.Ordinal) < 0 || src.IndexOf("/api/qrscan", StringComparison.Ordinal) < 0)
				fail("u-cam.js");
			if (html.IndexOf("data-tool=\"u-cam\"", StringComparison.Ordinal) < 0) fail("tools.html");
			if (joined.IndexOf("SK.regutil(\"u-cam\"", StringComparison.Ordinal) < 0) fail("tools.js");
		}
		log(bad == 0 ? "cam OK" : "cam FAIL " + bad);
		return bad == 0 ? 0 : 1;
	}

	static bool setrgb(IMFSourceReader reader) => tryfmt(reader, MfApi.FmtRgb32, 0, 0);

	static bool setnative(IMFSourceReader reader) {
		int w, h;
		IMFMediaType native = null;
		try {
			reader.GetNativeMediaType(FirstVideo, 0, out native);
			var fs = MfApi.FrameSize;
			native.GetUINT64(ref fs, out var packed);
			w = (int)((ulong)packed >> 32);
			h = (int)(packed & 0xFFFFFFFF);
		}
		catch { return false; }
		finally { MfApi.Release(native); }
		if (w < 2 || h < 2) return false;
		if (tryfmt(reader, MfApi.FmtRgb32, w, h)) return true;
		return tryfmt(reader, MfH264Reader.FmtNv12, w, h);
	}

	static bool tryfmt(IMFSourceReader reader, Guid subtype, int w, int h) {
		IMFMediaType t = null;
		try {
			MfApi.Check(MfApi.MFCreateMediaType(out t), "cam type");
			var major = MfApi.MajorType;
			var video = MfApi.MediaVideo;
			var sub = MfApi.SubType;
			var fmt = subtype;
			t.SetGUID(ref major, ref video);
			t.SetGUID(ref sub, ref fmt);
			if (w > 0 && h > 0) {
				var fs = MfApi.FrameSize;
				t.SetUINT64(ref fs, MfApi.Pack((uint)w, (uint)h));
			}
			var il = MfApi.Interlace;
			t.SetUINT32(ref il, 2);
			reader.SetCurrentMediaType(FirstVideo, IntPtr.Zero, t);
			return true;
		}
		catch { return false; }
		finally { MfApi.Release(t); }
	}

	static byte[] nv12tobgra(IntPtr ptr, int length, int w, int h, int stride) {
		if (stride < w) stride = w;
		var ySize = stride * h;
		var need = ySize + stride * h / 2;
		var raw = new byte[Math.Max(need, 1)];
		var n = Math.Min(raw.Length, Math.Max(0, length));
		if (n > 0) Marshal.Copy(ptr, raw, 0, n);
		var dst = new byte[w * h * 4];
		var uv = ySize;
		for (var y = 0; y < h; y++) {
			var yrow = y * stride;
			var uvrow = uv + (y / 2) * stride;
			var drow = y * w * 4;
			for (var x = 0; x < w; x++) {
				var c = raw[yrow + x] - 16;
				if (c < 0) c = 0;
				var uvi = uvrow + (x & ~1);
				var d = uvi + 1 < raw.Length ? raw[uvi] - 128 : 0;
				var e = uvi + 1 < raw.Length ? raw[uvi + 1] - 128 : 0;
				var r = (298 * c + 409 * e + 128) >> 8;
				var g = (298 * c - 100 * d - 208 * e + 128) >> 8;
				var b = (298 * c + 516 * d + 128) >> 8;
				if (r < 0) r = 0; else if (r > 255) r = 255;
				if (g < 0) g = 0; else if (g > 255) g = 255;
				if (b < 0) b = 0; else if (b > 255) b = 255;
				var o = drow + x * 4;
				dst[o] = (byte)b;
				dst[o + 1] = (byte)g;
				dst[o + 2] = (byte)r;
				dst[o + 3] = 255;
			}
		}
		return dst;
	}

	static string attrstr(IMFAttributes a, Guid key) {
		try {
			a.GetStringLength(ref key, out var n);
			if (n <= 0) return "";
			var sb = new StringBuilder(n + 1);
			a.GetString(ref key, sb, n + 1, out _);
			return sb.ToString();
		}
		catch { return ""; }
	}

	[DllImport("mf.dll", ExactSpelling = true)]
	static extern int MFEnumDeviceSources(IMFAttributes attr, out IntPtr activates, out int count);

	[DllImport("mf.dll", ExactSpelling = true)]
	static extern int MFCreateDeviceSource(IMFAttributes attr, out IntPtr source);

	[DllImport("mfreadwrite.dll", ExactSpelling = true)]
	static extern int MFCreateSourceReaderFromMediaSource(IntPtr source, IMFAttributes attr, out IMFSourceReader reader);
}

[ComImport, Guid("279A808D-AEC7-40C8-9C6B-A6B492C78A66"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFMediaSource {
	void GetEvent(int flags, out IntPtr ev);
	void BeginGetEvent(IntPtr callback, IntPtr state);
	void EndGetEvent(IntPtr result, out IntPtr ev);
	void QueueEvent(int type, ref Guid ext, int hr, IntPtr value);
	void GetCharacteristics(out int flags);
	void CreatePresentationDescriptor(out IntPtr desc);
	void Start(IntPtr desc, IntPtr timeFormat, IntPtr position);
	void Stop();
	void Pause();
	void Shutdown();
}
