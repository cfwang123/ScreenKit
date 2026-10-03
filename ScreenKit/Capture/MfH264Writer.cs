using System.Runtime.InteropServices;
using System.Text;

namespace ScreenKit;

/// <summary>Windows Media Foundation H.264，写成 MP4，不依赖 FFmpeg。</summary>
sealed class MfH264Writer : IRecordVideoSink {
	readonly int srcW, srcH, fps;
	readonly string path;
	long frameIndex;
	bool disposed;
	bool started;
	readonly bool withAudio;
	IMFSinkWriter writer;
	IMFMediaType inType;
	int stream;
	int astream = -1;
	int ablock = 2;
	int stride;
	long aframes;
	bool finished;

	public int OutWidth { get; }
	public int OutHeight { get; }
	public string CodecName { get; }
	public string OpenedEncoder { get; private set; } = "H264";
	public int AudioRate { get; private set; }
	public int AudioChannels { get; private set; }
	public bool HasAudioStream => astream >= 0;
	public bool WroteAudio { get; private set; }

	/// <summary>系统 AAC 只接受 44100 与 48000。不低于 46000 用 48000，其余用 44100。</summary>
	public static int AacHz(int hz) => hz >= 46000 ? 48000 : 44100;

	public MfH264Writer(string path, int captureW, int captureH, RecordOptions opt, bool withAudio = false) {
		this.path = path ?? throw new ArgumentNullException(nameof(path));
		opt ??= new RecordOptions();
		opt.Clamp();
		this.withAudio = withAudio;
		srcW = Math.Max(2, captureW / 2 * 2);
		srcH = Math.Max(2, captureH / 2 * 2);
		opt.FitSize(srcW, srcH, out var ow, out var oh);
		OutWidth = ow;
		OutHeight = oh;
		fps = opt.Fps;
		CodecName = opt.Codec;
		if (ow < 16 || oh < 16) throw new ArgumentException("录制区域过小");
		stride = ow * 4;
		try { open(opt); }
		catch {
			Dispose();
			throw;
		}
	}

	void open(RecordOptions opt) {
		MfApi.Startup();
		started = true;
		var dir = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		MfApi.Check(MfApi.MFCreateAttributes(out var attr, 4), "MFCreateAttributes");
		try {
			setguid(attr, MfApi.TranscodeContainer, MfApi.ContainerMpeg4);
			setu32(attr, MfApi.EnableHardware, 1);
			MfApi.Check(MfApi.MFCreateSinkWriterFromURL(path, IntPtr.Zero, attr, out writer),
				"MFCreateSinkWriterFromURL");
		}
		finally { MfApi.Release(attr); }

		MfApi.Check(MfApi.MFCreateMediaType(out var outType), "MFCreateMediaType out");
		try {
			setguid(outType, MfApi.MajorType, MfApi.MediaVideo);
			setguid(outType, MfApi.SubType, MfApi.FmtH264);
			setu64(outType, MfApi.FrameSize, MfApi.Pack((uint)OutWidth, (uint)OutHeight));
			setu64(outType, MfApi.FrameRate, MfApi.Pack((uint)fps, 1));
			setu64(outType, MfApi.PixelAspect, MfApi.Pack(1, 1));
			setu32(outType, MfApi.Interlace, 2);
			setu32(outType, MfApi.AvgBitrate, bitrate());
			setu32(outType, MfApi.LowLatency, 1);
			writer.AddStream(outType, out stream);
		}
		finally { MfApi.Release(outType); }

		MfApi.Check(MfApi.MFCreateMediaType(out inType), "MFCreateMediaType in");
		setguid(inType, MfApi.MajorType, MfApi.MediaVideo);
		setguid(inType, MfApi.SubType, MfApi.FmtRgb32);
		setu64(inType, MfApi.FrameSize, MfApi.Pack((uint)OutWidth, (uint)OutHeight));
		setu64(inType, MfApi.FrameRate, MfApi.Pack((uint)fps, 1));
		setu64(inType, MfApi.PixelAspect, MfApi.Pack(1, 1));
		setu32(inType, MfApi.Interlace, 2);
		setu32(inType, MfApi.Stride, stride);
		setu32(inType, MfApi.AllSamplesIndependent, 1);
		writer.SetInputMediaType(stream, inType, null);
		if (withAudio) openaudio(opt);
		writer.BeginWriting();
		OpenedEncoder = "H264";
	}

	void openaudio(RecordOptions opt) {
		AudioRate = AacHz(opt.AudioHz);
		AudioChannels = opt.AudioMono ? 1 : 2;
		ablock = AudioChannels * 2;
		var bytesPerSec = aacbytes(opt.AudioKbps);
		IMFMediaType aac = null;
		IMFMediaType pcm = null;
		try {
			MfApi.Check(MfApi.MFCreateMediaType(out aac), "aac type");
			setguid(aac, MfApi.MajorType, MfApi.MediaAudio);
			setguid(aac, MfApi.SubType, MfApi.FmtAac);
			setu32(aac, MfApi.AudioRate, AudioRate);
			setu32(aac, MfApi.AudioChannels, AudioChannels);
			setu32(aac, MfApi.AudioBits, 16);
			setu32(aac, MfApi.AudioAvgBytes, bytesPerSec);
			setu32(aac, MfApi.AvgBitrate, bytesPerSec * 8);
			setu32(aac, MfApi.AacPayload, 0);
			setu32(aac, MfApi.AacProfile, 0x29);
			try { writer.AddStream(aac, out astream); }
			catch (Exception ex) { throw new InvalidOperationException("添加 AAC 音轨: " + ex.Message, ex); }

			MfApi.Check(MfApi.MFCreateMediaType(out pcm), "pcm type");
			setguid(pcm, MfApi.MajorType, MfApi.MediaAudio);
			setguid(pcm, MfApi.SubType, MfApi.FmtPcm);
			setu32(pcm, MfApi.AudioRate, AudioRate);
			setu32(pcm, MfApi.AudioChannels, AudioChannels);
			setu32(pcm, MfApi.AudioBits, 16);
			setu32(pcm, MfApi.AudioBlock, ablock);
			setu32(pcm, MfApi.AudioAvgBytes, AudioRate * ablock);
			try { writer.SetInputMediaType(astream, pcm, null); }
			catch (Exception ex) { throw new InvalidOperationException("设置 PCM 输入: " + ex.Message, ex); }
		}
		finally {
			MfApi.Release(pcm);
			MfApi.Release(aac);
		}
	}

	int bitrate() {
		long b = (long)OutWidth * OutHeight * fps * 12 / 100;
		if (b < 400_000) b = 400_000;
		if (b > 40_000_000) b = 40_000_000;
		return (int)b;
	}

	public void WriteBgra(byte[] bgra, int strideIn, long pts) {
		if (disposed || writer == null) return;
		if (bgra == null) return;
		if (pts < frameIndex) pts = frameIndex;
		var tight = RecordBgra.Tight(bgra, strideIn, srcW, srcH, OutWidth, OutHeight);
		var gap = pts - frameIndex;
		for (var i = 0; i <= gap; i++)
			writesample(tight, frameIndex + i);
		frameIndex = pts + 1;
	}

	void writesample(byte[] tight, long pts) {
		IMFMediaBuffer buf = null;
		IMFSample sample = null;
		try {
			MfApi.Check(MfApi.MFCreateMemoryBuffer(tight.Length, out buf), "MFCreateMemoryBuffer");
			buf.Lock(out var ptr, out _, out _);
			try { Marshal.Copy(tight, 0, ptr, tight.Length); }
			finally { buf.Unlock(); }
			buf.SetCurrentLength(tight.Length);
			MfApi.Check(MfApi.MFCreateSample(out sample), "MFCreateSample");
			sample.AddBuffer(buf);
			var time = pts * 10_000_000L / fps;
			sample.SetSampleTime(time);
			sample.SetSampleDuration(10_000_000L / fps);
			writer.WriteSample(stream, sample);
		}
		finally {
			MfApi.Release(sample);
			MfApi.Release(buf);
		}
	}

	public void WritePcm(byte[] data, int count) {
		if (disposed || writer == null || astream < 0 || data == null || count <= 0) return;
		count -= count % Math.Max(1, ablock);
		if (count <= 0) return;
		writepcm(writer, astream, data, count, ablock, AudioRate, ref aframes);
		WroteAudio = true;
	}

	public void Finish() {
		if (writer == null || finished) return;
		finished = true;
		var hr = writer.FinalizeWriter();
		if (hr < 0)
			throw new InvalidOperationException("IMFSinkWriter.Finalize 失败 0x" + hr.ToString("X8"));
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		try { Finish(); } catch { }
		MfApi.Release(inType);
		inType = null;
		MfApi.Release(writer);
		writer = null;
		if (started) {
			started = false;
			try { MfApi.Shutdown(); } catch { }
		}
	}

	static void writepcm(IMFSinkWriter sink, int index, byte[] data, int count, int block, int hz, ref long frameAt) {
		IMFMediaBuffer buf = null;
		IMFSample sample = null;
		try {
			MfApi.Check(MfApi.MFCreateMemoryBuffer(count, out buf), "audio buffer");
			buf.Lock(out var ptr, out _, out _);
			try { Marshal.Copy(data, 0, ptr, count); }
			finally { buf.Unlock(); }
			buf.SetCurrentLength(count);
			MfApi.Check(MfApi.MFCreateSample(out sample), "audio sample");
			sample.AddBuffer(buf);
			var frames = count / Math.Max(1, block);
			sample.SetSampleTime(frameAt * 10_000_000L / hz);
			sample.SetSampleDuration(frames * 10_000_000L / hz);
			sink.WriteSample(index, sample);
			frameAt += frames;
		}
		finally {
			MfApi.Release(sample);
			MfApi.Release(buf);
		}
	}

	static int aacbytes(int kbps) {
		var b = Math.Max(1, kbps) * 1000 / 8;
		var best = 12000;
		var dist = Math.Abs(b - best);
		foreach (var c in new[] { 16000, 20000, 24000 }) {
			var d = Math.Abs(b - c);
			if (d < dist) { best = c; dist = d; }
		}
		return best;
	}

	static void setguid(IMFAttributes a, Guid key, Guid val) => a.SetGUID(ref key, ref val);
	static void setu32(IMFAttributes a, Guid key, int val) => a.SetUINT32(ref key, val);
	static void setu64(IMFAttributes a, Guid key, long val) => a.SetUINT64(ref key, val);

	/// <summary>文件头里是否像 H.264 MP4。</summary>
	public static bool LooksLikeH264(string path) {
		try {
			var n = (int)Math.Min(new FileInfo(path).Length, 512 * 1024);
			if (n < 16) return false;
			var buf = new byte[n];
			using (var fs = File.OpenRead(path))
				if (fs.Read(buf, 0, n) < 12) return false;
			var s = Encoding.ASCII.GetString(buf);
			return s.Contains("ftyp") && (s.Contains("avc1") || s.Contains("H264") || s.Contains("avcC"));
		}
		catch { return false; }
	}
}

static class MfApi {
	public const uint MfVersion = 0x00020070;

	public static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
	public static readonly Guid SubType = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
	public static readonly Guid AvgBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
	public static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
	public static readonly Guid FrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
	public static readonly Guid PixelAspect = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
	public static readonly Guid Interlace = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
	public static readonly Guid Stride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
	public static readonly Guid AllSamplesIndependent = new("c9173739-5e56-461c-b713-46fb995cb95f");
	public static readonly Guid LowLatency = new("9c27891a-ed7a-40e1-88e8-b22727a024ee");
	public static readonly Guid AudioChannels = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
	public static readonly Guid AudioRate = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
	public static readonly Guid AudioBits = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
	public static readonly Guid AudioAvgBytes = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
	public static readonly Guid AudioBlock = new("322de230-9eeb-43bd-ab7a-ff412251541d");
	public static readonly Guid AacPayload = new("bfbabe79-7434-4d1c-94f0-72a3b9e17188");
	public static readonly Guid AacProfile = new("7632f0e6-9538-4d61-acda-ea29c8c14456");
	public static readonly Guid TranscodeContainer = new("150ff23f-4abc-478b-ac4f-e1916fba1cca");
	public static readonly Guid EnableHardware = new("a634a91c-822b-41b9-a494-4de4643612b0");

	public static readonly Guid MediaVideo = new("73646976-0000-0010-8000-00aa00389b71");
	public static readonly Guid MediaAudio = new("73647561-0000-0010-8000-00aa00389b71");
	public static readonly Guid FmtH264 = new("34363248-0000-0010-8000-00aa00389b71");
	public static readonly Guid FmtRgb32 = new("00000016-0000-0010-8000-00aa00389b71");
	public static readonly Guid FmtAac = new("00001610-0000-0010-8000-00aa00389b71");
	public static readonly Guid FmtPcm = new("00000001-0000-0010-8000-00aa00389b71");
	public static readonly Guid ContainerMpeg4 = new("dc6cd05d-b9d0-40ef-bd35-fa622c1ab28a");
	public static readonly Guid EnableHw = EnableHardware;

	public static long Pack(uint hi, uint lo) => (long)(((ulong)hi << 32) | lo);

	public static void Startup() => Check(MFStartup(MfVersion, 0), "MFStartup");

	public static void Shutdown() => MFShutdown();

	public static void Check(int code, string op) {
		if (code >= 0) return;
		throw new InvalidOperationException(op + " 失败 0x" + code.ToString("X8"));
	}

	public static void Release(object o) {
		if (o == null) return;
		try { Marshal.ReleaseComObject(o); } catch { }
	}

	[DllImport("mfplat.dll", ExactSpelling = true)]
	public static extern int MFStartup(uint version, uint flags);

	[DllImport("mfplat.dll", ExactSpelling = true)]
	public static extern int MFShutdown();

	[DllImport("mfplat.dll", ExactSpelling = true)]
	public static extern int MFCreateMediaType(out IMFMediaType pp);

	[DllImport("mfplat.dll", ExactSpelling = true)]
	public static extern int MFCreateSample(out IMFSample pp);

	[DllImport("mfplat.dll", ExactSpelling = true)]
	public static extern int MFCreateMemoryBuffer(int cb, out IMFMediaBuffer pp);

	[DllImport("mfplat.dll", ExactSpelling = true)]
	public static extern int MFCreateAttributes(out IMFAttributes pp, int initial);

	[DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
	public static extern int MFCreateSinkWriterFromURL(
		[MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr stream, IMFAttributes attr, out IMFSinkWriter pp);
}

[ComImport, Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFAttributes {
	void GetItem(ref Guid key, IntPtr value);
	void GetItemType(ref Guid key, out int type);
	void CompareItem(ref Guid key, IntPtr value, [MarshalAs(UnmanagedType.Bool)] out bool result);
	void Compare(IMFAttributes theirs, int matchType, [MarshalAs(UnmanagedType.Bool)] out bool result);
	void GetUINT32(ref Guid key, out int value);
	void GetUINT64(ref Guid key, out long value);
	void GetDouble(ref Guid key, out double value);
	void GetGUID(ref Guid key, out Guid value);
	void GetStringLength(ref Guid key, out int len);
	void GetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int buf, out int len);
	void GetAllocatedString(ref Guid key, out IntPtr value, out int len);
	void GetBlobSize(ref Guid key, out int size);
	void GetBlob(ref Guid key, [MarshalAs(UnmanagedType.LPArray)] byte[] buf, int bufSize, out int size);
	void GetAllocatedBlob(ref Guid key, out IntPtr buf, out int size);
	void GetUnknown(ref Guid key, ref Guid riid, out IntPtr ppv);
	void SetItem(ref Guid key, IntPtr value);
	void DeleteItem(ref Guid key);
	void DeleteAllItems();
	void SetUINT32(ref Guid key, int value);
	void SetUINT64(ref Guid key, long value);
	void SetDouble(ref Guid key, double value);
	void SetGUID(ref Guid key, ref Guid value);
	void SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
	void SetBlob(ref Guid key, [MarshalAs(UnmanagedType.LPArray)] byte[] buf, int size);
	void SetUnknown(ref Guid key, [MarshalAs(UnmanagedType.IUnknown)] object unknown);
	void LockStore();
	void UnlockStore();
	void GetCount(out int count);
	void GetItemByIndex(int index, out Guid key, IntPtr value);
	void CopyAllItems(IMFAttributes dest);
}

[ComImport, Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFMediaType : IMFAttributes {
	void GetMajorType(out Guid major);
	[return: MarshalAs(UnmanagedType.Bool)]
	bool IsCompressedFormat();
	void IsEqual(IMFMediaType other, out int flags);
	void GetRepresentation(Guid guid, out IntPtr rep);
	void FreeRepresentation(Guid guid, IntPtr rep);
}

[ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFMediaBuffer {
	void Lock(out IntPtr ptr, out int max, out int current);
	void Unlock();
	void GetCurrentLength(out int current);
	void SetCurrentLength(int current);
	void GetMaxLength(out int max);
}

// .NET Framework 的 ComImport 继承不把基接口方法排进虚表，这里必须整表重声明。
[ComImport, Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFSample {
	void GetItem(ref Guid key, IntPtr value);
	void GetItemType(ref Guid key, out int type);
	void CompareItem(ref Guid key, IntPtr value, [MarshalAs(UnmanagedType.Bool)] out bool result);
	void Compare(IMFAttributes theirs, int matchType, [MarshalAs(UnmanagedType.Bool)] out bool result);
	void GetUINT32(ref Guid key, out int value);
	void GetUINT64(ref Guid key, out long value);
	void GetDouble(ref Guid key, out double value);
	void GetGUID(ref Guid key, out Guid value);
	void GetStringLength(ref Guid key, out int len);
	void GetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int buf, out int len);
	void GetAllocatedString(ref Guid key, out IntPtr value, out int len);
	void GetBlobSize(ref Guid key, out int size);
	void GetBlob(ref Guid key, [MarshalAs(UnmanagedType.LPArray)] byte[] buf, int bufSize, out int size);
	void GetAllocatedBlob(ref Guid key, out IntPtr buf, out int size);
	void GetUnknown(ref Guid key, ref Guid riid, out IntPtr ppv);
	void SetItem(ref Guid key, IntPtr value);
	void DeleteItem(ref Guid key);
	void DeleteAllItems();
	void SetUINT32(ref Guid key, int value);
	void SetUINT64(ref Guid key, long value);
	void SetDouble(ref Guid key, double value);
	void SetGUID(ref Guid key, ref Guid value);
	void SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
	void SetBlob(ref Guid key, [MarshalAs(UnmanagedType.LPArray)] byte[] buf, int size);
	void SetUnknown(ref Guid key, [MarshalAs(UnmanagedType.IUnknown)] object unknown);
	void LockStore();
	void UnlockStore();
	void GetCount(out int count);
	void GetItemByIndex(int index, out Guid key, IntPtr value);
	void CopyAllItems(IMFAttributes dest);
	void GetSampleFlags(out int flags);
	void SetSampleFlags(int flags);
	void GetSampleTime(out long time);
	void SetSampleTime(long time);
	void GetSampleDuration(out long duration);
	void SetSampleDuration(long duration);
	void GetBufferCount(out int count);
	void GetBufferByIndex(int index, out IMFMediaBuffer buffer);
	void ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
	void AddBuffer(IMFMediaBuffer buffer);
	void RemoveBufferByIndex(int index);
	void RemoveAllBuffers();
	void GetTotalLength(out int length);
	void CopyToBuffer(IMFMediaBuffer buffer);
}

[ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFSinkWriter {
	void AddStream(IMFMediaType target, out int index);
	void SetInputMediaType(int index, IMFMediaType input, IMFAttributes param);
	void BeginWriting();
	void WriteSample(int index, IMFSample sample);
	void SendStreamTick(int index, long timestamp);
	void PlaceMarker(int index, IntPtr context);
	void NotifyEndOfSegment(int index);
	void Flush(int index);
	[PreserveSig] int FinalizeWriter();
	void GetServiceForStream(int index, ref Guid service, ref Guid riid, out IntPtr ppv);
	void GetStatistics(int index, IntPtr stats);
}


