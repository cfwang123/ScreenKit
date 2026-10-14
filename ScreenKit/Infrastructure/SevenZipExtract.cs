using System.Runtime.InteropServices;

namespace ScreenKit;

/// <summary>用程序旁的 7za.dll（CreateObject）解压 .7z。不加载 SharpSevenZip。</summary>
static class SevenZipExtract {
	const uint KPID_PATH = 3;
	const uint KPID_IS_DIR = 6;
	const uint ALL_ITEMS = 0xFFFFFFFF;
	const int VT_BOOL = 11;
	const int VT_BSTR = 8;
	const int VT_LPSTR = 30;
	const int VT_LPWSTR = 31;

	static readonly Guid CLSID_7Z = new("23170F69-40C1-278A-1000-000110070000");
	static readonly Guid IID_IN_ARCHIVE = new("23170F69-40C1-278A-0000-000600600000");

	static readonly object gate = new();
	static IntPtr lib;
	static CreateObjectDelegate createobject;

	[DllImport("kernel32.dll", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern IntPtr loadlibrary(string path);

	[DllImport("kernel32.dll", EntryPoint = "GetProcAddress", CharSet = CharSet.Ansi, SetLastError = true)]
	static extern IntPtr getprocaddress(IntPtr module, string name);

	[DllImport("ole32.dll", EntryPoint = "PropVariantClear")]
	static extern int propvariantclear(ref PropVar value);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	delegate int CreateObjectDelegate(
		ref Guid classId, ref Guid interfaceId,
		[MarshalAs(UnmanagedType.Interface)] out object outObject);

	[StructLayout(LayoutKind.Explicit, Size = 24)]
	struct PropVar {
		[FieldOffset(0)] public ushort vt;
		[FieldOffset(8)] public IntPtr ptr;
		[FieldOffset(8)] public short i2;
	}

	[ComImport]
	[Guid("23170F69-40C1-278A-0000-000600100000")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface IArchiveOpenCallback {
		void SetTotal(IntPtr files, IntPtr bytes);
		void SetCompleted(IntPtr files, IntPtr bytes);
	}

	[ComImport]
	[Guid("23170F69-40C1-278A-0000-000300010000")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface ISequentialInStream {
		[PreserveSig]
		int Read(
			[Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] data,
			uint size, IntPtr processedSize);
	}

	[ComImport]
	[Guid("23170F69-40C1-278A-0000-000300030000")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface IInStream {
		[PreserveSig]
		int Read(
			[Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] data,
			uint size, IntPtr processedSize);

		[PreserveSig]
		int Seek(long offset, uint seekOrigin, IntPtr newPosition);
	}

	[ComImport]
	[Guid("23170F69-40C1-278A-0000-000300020000")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface ISequentialOutStream {
		[PreserveSig]
		int Write(
			[In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] data,
			uint size, IntPtr processedSize);
	}

	[ComImport]
	[Guid("23170F69-40C1-278A-0000-000600600000")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface IInArchive {
		[PreserveSig]
		int Open(IInStream stream, IntPtr maxCheckStartPosition, IArchiveOpenCallback openCallback);
		void Close();
		uint GetNumberOfItems();
		void GetProperty(uint index, uint propId, ref PropVar value);
		[PreserveSig]
		int Extract(IntPtr indices, uint numItems, int testMode, IArchiveExtractCallback extractCallback);
	}

	enum AskMode {
		Extract = 0,
		Test = 1,
		Skip = 2,
	}

	[ComImport]
	[Guid("23170F69-40C1-278A-0000-000600200000")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface IArchiveExtractCallback {
		void SetTotal(ulong total);
		void SetCompleted(IntPtr completeValue);
		[PreserveSig]
		int GetStream(uint index, [MarshalAs(UnmanagedType.Interface)] out ISequentialOutStream outStream, AskMode askExtractMode);
		void PrepareOperation(AskMode askExtractMode);
		void SetOperationResult(int operationResult);
	}

	sealed class OpenCb : IArchiveOpenCallback {
		public void SetTotal(IntPtr files, IntPtr bytes) { }
		public void SetCompleted(IntPtr files, IntPtr bytes) { }
	}

	sealed class InWrap : IInStream, ISequentialInStream {
		readonly Stream stream;
		public InWrap(Stream stream) { this.stream = stream; }

		public int Read(byte[] data, uint size, IntPtr processedSize) {
			var n = stream.Read(data, 0, (int)size);
			if (processedSize != IntPtr.Zero) Marshal.WriteInt32(processedSize, n);
			return 0;
		}

		public int Seek(long offset, uint seekOrigin, IntPtr newPosition) {
			var pos = stream.Seek(offset, (SeekOrigin)seekOrigin);
			if (newPosition != IntPtr.Zero) Marshal.WriteInt64(newPosition, pos);
			return 0;
		}
	}

	sealed class OutWrap : ISequentialOutStream {
		readonly Stream stream;
		public OutWrap(Stream stream) { this.stream = stream; }
		public void Dispose() { stream.Dispose(); }

		public int Write(byte[] data, uint size, IntPtr processedSize) {
			stream.Write(data, 0, (int)size);
			if (processedSize != IntPtr.Zero) Marshal.WriteInt32(processedSize, (int)size);
			return 0;
		}
	}

	sealed class ExtractCb : IArchiveExtractCallback {
		readonly IInArchive archive;
		readonly string dest;
		OutWrap current;
		public string Error;

		public ExtractCb(IInArchive archive, string dest) {
			this.archive = archive;
			this.dest = dest;
		}

		public void SetTotal(ulong total) { }
		public void SetCompleted(IntPtr completeValue) { }
		public void PrepareOperation(AskMode askExtractMode) { }

		public int GetStream(uint index, out ISequentialOutStream outStream, AskMode askExtractMode) {
			outStream = null;
			closecurrent();
			if (askExtractMode != AskMode.Extract) return 0;
			try {
				var rel = propstr(index, KPID_PATH);
				if (string.IsNullOrEmpty(rel)) rel = index.ToString();
				var isDir = propbool(index, KPID_IS_DIR) || rel.EndsWith("/") || rel.EndsWith("\\");
				var full = safepath(dest, rel);
				if (isDir) {
					Directory.CreateDirectory(full);
					return 0;
				}
				var parent = Path.GetDirectoryName(full);
				if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
				current = new OutWrap(new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.Read));
				outStream = current;
				return 0;
			}
			catch (Exception ex) {
				Error ??= ex.Message;
				return unchecked((int)0x80004005);
			}
		}

		public void SetOperationResult(int operationResult) {
			closecurrent();
			if (operationResult != 0) Error ??= "解压失败 " + operationResult;
		}

		void closecurrent() {
			if (current == null) return;
			try { current.Dispose(); } catch { }
			current = null;
		}

		string propstr(uint index, uint propId) {
			var pv = new PropVar();
			try {
				archive.GetProperty(index, propId, ref pv);
				if (pv.ptr == IntPtr.Zero) return null;
				if (pv.vt == VT_BSTR) return Marshal.PtrToStringUni(pv.ptr);
				if (pv.vt == VT_LPWSTR) return Marshal.PtrToStringUni(pv.ptr);
				if (pv.vt == VT_LPSTR) return Marshal.PtrToStringAnsi(pv.ptr);
				return null;
			}
			finally { clearprop(ref pv); }
		}

		bool propbool(uint index, uint propId) {
			var pv = new PropVar();
			try {
				archive.GetProperty(index, propId, ref pv);
				return pv.vt == VT_BOOL && pv.i2 != 0;
			}
			finally { clearprop(ref pv); }
		}
	}

	public static void Extract(string archive, string destDir) {
		if (string.IsNullOrEmpty(archive) || !File.Exists(archive))
			throw new FileNotFoundException("找不到压缩包", archive);
		var dll = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "7za.dll");
		if (!File.Exists(dll))
			throw new InvalidOperationException("解压 7z 需要 7za.dll。请在「安装功能」里安装。");
		Directory.CreateDirectory(destDir);
		droplegacy();
		var archiveObj = openarchive(dll);
		var opened = false;
		InWrap input = null;
		FileStream fs = null;
		try {
			fs = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			input = new InWrap(fs);
			var check = Marshal.AllocHGlobal(8);
			try {
				Marshal.WriteInt64(check, 1L << 22);
				var opener = new OpenCb();
				var code = archiveObj.Open(input, check, opener);
				if (code == 1) throw new InvalidOperationException("不是 7z 压缩包");
				if (code < 0) throw new InvalidOperationException("打开 7z 失败 " + code.ToString("X8"));
			}
			finally { Marshal.FreeHGlobal(check); }
			opened = true;
			var cb = new ExtractCb(archiveObj, Path.GetFullPath(destDir));
			var code2 = archiveObj.Extract(IntPtr.Zero, ALL_ITEMS, 0, cb);
			if (code2 < 0) throw new InvalidOperationException("解压 7z 失败 " + code2.ToString("X8"));
			if (cb.Error != null) throw new InvalidOperationException(cb.Error);
		}
		finally {
			if (opened) {
				try { archiveObj.Close(); } catch { }
			}
			try { Marshal.ReleaseComObject(archiveObj); } catch { }
			fs?.Dispose();
		}
	}

	/// <summary>解压内置的小 7z（两个文件，含中文名）。成功返回 0。</summary>
	public static int SelfTest(Action<string> ok, Action<string> err) {
		var root = Path.Combine(Path.GetTempPath(), "screenkit-7za-" + Guid.NewGuid().ToString("N"));
		try {
			Directory.CreateDirectory(root);
			var arc = Path.Combine(root, "sample.7z");
			File.WriteAllBytes(arc, hexbytes(SAMPLE_7Z));
			var dest = Path.Combine(root, "out");
			Extract(arc, dest);
			var hi = File.ReadAllText(Path.Combine(dest, "hi.txt"));
			var zhPath = Path.Combine(dest, "中文.txt");
			var zh = File.ReadAllText(zhPath).TrimStart('\uFEFF');
			if (hi != "hello-7za" || zh != "词典") {
				err?.Invoke("FAIL: 内容不符 hi=" + hi + " zh=" + zh);
				return 1;
			}
			ok?.Invoke("OK " + dest);
			return 0;
		}
		catch (Exception ex) {
			err?.Invoke("FAIL: " + ex.Message);
			return 1;
		}
		finally {
			try { Directory.Delete(root, true); } catch { }
		}
	}

	static IInArchive openarchive(string dll) {
		lock (gate) {
			if (lib == IntPtr.Zero) {
				lib = loadlibrary(dll);
				if (lib == IntPtr.Zero)
					throw new InvalidOperationException("无法加载 7za.dll");
				var proc = getprocaddress(lib, "CreateObject");
				if (proc == IntPtr.Zero)
					throw new InvalidOperationException("7za.dll 没有 CreateObject");
				createobject = (CreateObjectDelegate)Marshal.GetDelegateForFunctionPointer(
					proc, typeof(CreateObjectDelegate));
			}
		}
		var clsid = CLSID_7Z;
		var iid = IID_IN_ARCHIVE;
		var code = createobject(ref clsid, ref iid, out var obj);
		if (code < 0 || obj is not IInArchive archive)
			throw new InvalidOperationException("7za 创建归档对象失败 " + code.ToString("X8"));
		return archive;
	}

	static void clearprop(ref PropVar value) {
		if (value.vt == 0) return;
		propvariantclear(ref value);
	}

	static string safepath(string dest, string rel) {
		rel = (rel ?? "").Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
		if (rel.Length == 0 || Path.IsPathRooted(rel))
			throw new InvalidOperationException("7z 路径越界");
		var root = Path.GetFullPath(dest);
		var sep = Path.DirectorySeparatorChar.ToString();
		if (!root.EndsWith(sep)) root += sep;
		var full = Path.GetFullPath(Path.Combine(root, rel));
		if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("7z 路径越界");
		return full;
	}

	static byte[] hexbytes(string hex) {
		var buf = new byte[hex.Length / 2];
		for (var i = 0; i < buf.Length; i++)
			buf[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
		return buf;
	}

	static void droplegacy() {
		try {
			var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SharpSevenZip.dll");
			if (File.Exists(path)) File.Delete(path);
		}
		catch { }
	}

	// 7-Zip 26 打的两个文件：hi.txt = hello-7za，中文.txt = 词典（UTF-8 BOM）
	const string SAMPLE_7Z = "377abcaf271c0004d8c841c973000000000000002000000000000000141490cf01001168656c6c6f2d377a61efbbbfe8af8de585b8000000813307ae0fcf5d2f0c07c8437f41b1fafde5c77de96d3cf620abd9203f2aff1237429840299227fb100eb7004d000f317a89c3a73b996b9af7db767f934f785f07570c0a2287a1dd214ca898e6e6fc27bd481933bc8ace8591dc0017061601095d00070b01000123030101055d001000000c6e0a013444d8160000";
}
