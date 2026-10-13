using System.Runtime.InteropServices;

namespace ScreenKit;

/// <summary>用 Media Foundation 把 H.264 MP4 解成自上而下的 BGRA。不依赖 FFmpeg。</summary>
static class MfH264Reader {
	const int FirstVideo = unchecked((int)0xFFFFFFFC);
	const int FirstAudio = unchecked((int)0xFFFFFFFD);
	const int EndOfStream = 0x2;
	const int StreamError = 0x1;

	public static readonly Guid EnableVideoProcessing = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
	public static readonly Guid FmtNv12 = new("3231564E-0000-0010-8000-00AA00389B71");

	/// <summary>按解码顺序回调。返回 false 则停止。stride 为 outW*4。</summary>
	public static void ForEach(string path, int outW, int outH, FrameFn onFrame, CancellationToken ct = default) {
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			throw new FileNotFoundException("视频不存在", path);
		outW = Math.Max(2, outW);
		outH = Math.Max(2, outH);
		IMFSourceReader reader = null;
		var started = false;
		try {
			MfApi.Startup();
			started = true;
			MfApi.Check(MfApi.MFCreateAttributes(out var attr, 2), "MFCreateAttributes reader");
			try {
				var key = EnableVideoProcessing;
				attr.SetUINT32(ref key, 1);
				MfApi.Check(MFCreateSourceReaderFromURL(path, attr, out reader), "MFCreateSourceReaderFromURL");
			}
			finally { MfApi.Release(attr); }

			try { reader.SetStreamSelection(FirstAudio, false); } catch { }
			reader.GetNativeMediaType(FirstVideo, 0, out var native);
			int srcW, srcH;
			try {
				var fs = MfApi.FrameSize;
				native.GetUINT64(ref fs, out var packed);
				srcW = (int)((ulong)packed >> 32);
				srcH = (int)(packed & 0xFFFFFFFF);
			}
			finally { MfApi.Release(native); }
			if (srcW < 2 || srcH < 2)
				throw new InvalidOperationException("无视频流");

			var nv12 = !tryrgb32(reader, srcW, srcH);
			if (nv12 && !trynv12(reader, srcW, srcH))
				throw new InvalidOperationException("系统 H.264 解码器不能输出 RGB32 或 NV12");
			var stride = strideof(reader);
			if (stride == 0) stride = nv12 ? srcW : srcW * 4;

			long index = 0;
			while (!ct.IsCancellationRequested) {
				var hr = reader.ReadSample(FirstVideo, 0, out _, out var flags, out _, out var sample);
				if (hr < 0)
					throw new InvalidOperationException("读视频帧失败 0x" + hr.ToString("X8"));
				if ((flags & StreamError) != 0)
					throw new InvalidOperationException("读视频帧失败");
				if ((flags & EndOfStream) != 0) {
					MfApi.Release(sample);
					break;
				}
				if (sample == null) continue;
				IMFMediaBuffer buf = null;
				try {
					sample.ConvertToContiguousBuffer(out buf);
					buf.Lock(out var ptr, out _, out var cur);
					byte[] bgra;
					try {
						bgra = nv12 ? nv12tobgra(ptr, cur, srcW, srcH, stride) : rgb32tobgra(ptr, srcW, srcH, stride);
					}
					finally { buf.Unlock(); }
					if (srcW != outW || srcH != outH)
						bgra = RecordBgra.Tight(bgra, srcW * 4, srcW, srcH, outW, outH);
					if (!onFrame(bgra, outW * 4, index++)) break;
				}
				finally {
					MfApi.Release(buf);
					MfApi.Release(sample);
				}
			}
		}
		finally {
			MfApi.Release(reader);
			if (started) {
				try { MfApi.Shutdown(); } catch { }
			}
		}
	}

	public delegate bool FrameFn(byte[] bgra, int stride, long index);

	static bool tryrgb32(IMFSourceReader reader, int w, int h) {
		IMFMediaType t = null;
		try {
			MfApi.Check(MfApi.MFCreateMediaType(out t), "rgb32 type");
			var major = MfApi.MajorType;
			var video = MfApi.MediaVideo;
			var sub = MfApi.SubType;
			var rgb = MfApi.FmtRgb32;
			var fs = MfApi.FrameSize;
			var il = MfApi.Interlace;
			t.SetGUID(ref major, ref video);
			t.SetGUID(ref sub, ref rgb);
			t.SetUINT64(ref fs, MfApi.Pack((uint)w, (uint)h));
			t.SetUINT32(ref il, 2);
			reader.SetCurrentMediaType(FirstVideo, IntPtr.Zero, t);
			return true;
		}
		catch (Exception ex) {
			RecordLog.Step("mf_rgb32", ex.Message);
			return false;
		}
		finally { MfApi.Release(t); }
	}

	static bool trynv12(IMFSourceReader reader, int w, int h) {
		IMFMediaType t = null;
		try {
			MfApi.Check(MfApi.MFCreateMediaType(out t), "nv12 type");
			var major = MfApi.MajorType;
			var video = MfApi.MediaVideo;
			var sub = MfApi.SubType;
			var nv = FmtNv12;
			var fs = MfApi.FrameSize;
			var il = MfApi.Interlace;
			t.SetGUID(ref major, ref video);
			t.SetGUID(ref sub, ref nv);
			t.SetUINT64(ref fs, MfApi.Pack((uint)w, (uint)h));
			t.SetUINT32(ref il, 2);
			reader.SetCurrentMediaType(FirstVideo, IntPtr.Zero, t);
			return true;
		}
		catch (Exception ex) {
			RecordLog.Step("mf_nv12", ex.Message);
			return false;
		}
		finally { MfApi.Release(t); }
	}

	static int strideof(IMFSourceReader reader) {
		reader.GetCurrentMediaType(FirstVideo, out var t);
		try {
			var key = MfApi.Stride;
			t.GetUINT32(ref key, out var stride);
			return stride;
		}
		catch { return 0; }
		finally { MfApi.Release(t); }
	}

	static byte[] rgb32tobgra(IntPtr ptr, int w, int h, int stride) {
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

	static byte[] nv12tobgra(IntPtr ptr, int length, int w, int h, int stride) {
		if (stride < w) stride = w;
		var ySize = stride * h;
		var need = ySize + stride * h / 2;
		var raw = new byte[need];
		Marshal.Copy(ptr, raw, 0, Math.Min(raw.Length, Math.Max(0, length)));
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
				var d = raw[uvi] - 128;
				var e = raw[uvi + 1] - 128;
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

	[DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
	static extern int MFCreateSourceReaderFromURL(
		[MarshalAs(UnmanagedType.LPWStr)] string url, IMFAttributes attr, out IMFSourceReader pp);
}

[ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFSourceReader {
	void GetStreamSelection(int index, [MarshalAs(UnmanagedType.Bool)] out bool selected);
	void SetStreamSelection(int index, [MarshalAs(UnmanagedType.Bool)] bool selected);
	void GetNativeMediaType(int index, int typeIndex, out IMFMediaType type);
	void GetCurrentMediaType(int index, out IMFMediaType type);
	void SetCurrentMediaType(int index, IntPtr reserved, IMFMediaType type);
	void SetCurrentPosition(ref Guid timeFormat, IntPtr position);
	[PreserveSig] int ReadSample(int index, int flags, out int actual, out int streamFlags, out long timestamp, out IMFSample sample);
	void Flush(int index);
	void GetServiceForStream(int index, ref Guid service, ref Guid riid, out IntPtr ppv);
	void GetPresentationAttribute(int index, ref Guid attr, IntPtr value);
}
