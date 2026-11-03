using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace ScreenKit;

/// <summary>
/// 把远端文件拖到资源管理器：用 CFSTR_FILEDESCRIPTORW + CFSTR_FILECONTENTS 提供「虚拟文件」，
/// 资源管理器按需从 FTP/SFTP 拉内容、边读边落盘，所以不用先把整个文件下载到临时目录。
/// 目录不参与拖出（虚拟文件名不支持相对路径），界面另给「下载到…」。
/// </summary>
static class FtpDragOut {
	const int S_OK = 0;
	const int E_NOTIMPL = unchecked((int)0x80004001);
	const int OLE_E_ADVISENOTSUPPORTED = unchecked((int)0x80040003);
	const int DV_E_FORMATETC = unchecked((int)0x80040064);
	const int DV_E_DVASPECT = unchecked((int)0x8004006B);
	const int DV_E_LINDEX = unchecked((int)0x80040068);
	const int DV_E_TYMED = unchecked((int)0x80040069);
	const int STG_E_READFAULT = unchecked((int)0x8003001E);

	const uint GMEM_MOVEABLE = 0x0002;
	const uint GMEM_ZEROINIT = 0x0040;

	const int DROPEFFECT_COPY = 1;
	const uint MK_LBUTTON = 0x0001;
	const int DRAGDROP_S_DROP = 0x00040100;
	const int DRAGDROP_S_CANCEL = 0x00040101;
	const int DRAGDROP_S_USEDEFAULTCURSORS = 0x00040102;

	// FILEDESCRIPTORW 标志位
	const uint FD_ATTRIBUTES = 0x00000004;
	const uint FD_WRITESTIME = 0x00000020;
	const uint FD_FILESIZE = 0x00000040;
	const uint FD_PROGRESSUI = 0x00004000;
	const uint FD_UNICODE = 0x80000000;
	const int FILE_ATTRIBUTE_NORMAL = 0x80;

	const int DESCRIPTOR_SIZE = 592;

	[DllImport("ole32.dll")]
	static extern int OleInitialize(IntPtr pvReserved);

	[DllImport("ole32.dll")]
	static extern void OleUninitialize();

	[DllImport("ole32.dll")]
	static extern int DoDragDrop(IntPtr pDataObj, IntPtr pDropSource, int dwOKEffects, out int pdwEffect);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern IntPtr GlobalLock(IntPtr hMem);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool GlobalUnlock(IntPtr hMem);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern IntPtr GlobalFree(IntPtr hMem);

	static readonly short fmtDescriptor = (short)System.Windows.Forms.DataFormats.GetFormat("FileGroupDescriptorW").Id;
	static readonly short fmtContents = (short)System.Windows.Forms.DataFormats.GetFormat("FileContents").Id;
	static readonly short fmtPreferred = (short)System.Windows.Forms.DataFormats.GetFormat("Preferred DropEffect").Id;

	/// <summary>一个要拖出去的文件快照。</summary>
	public sealed class Item {
		public string Remote;
		public string Name;
		public long Size;
		public DateTime Modified;
	}

	/// <summary>
	/// 发起 OLE 拖拽，返回 true 表示资源管理器确实接收了（开始下载）。
	/// 阻塞到用户松开鼠标为止，调用方应在 UI 线程直接调用。
	/// </summary>
	public static bool Drag(FtpSite site, string password, IList<Item> files) {
		if (site == null || files == null || files.Count == 0) return false;
		var inited = OleInitialize(IntPtr.Zero) == S_OK;
		var data = new DragData(site, password ?? "", files);
		var pData = IntPtr.Zero;
		var pSource = IntPtr.Zero;
		int effect = 0;
		try {
			pData = Marshal.GetComInterfaceForObject(data, typeof(ComTypes.IDataObject));
			pSource = Marshal.GetComInterfaceForObject(data, typeof(IDropSource));
			var hr = DoDragDrop(pData, pSource, DROPEFFECT_COPY, out effect);
			return hr == S_OK && (effect & DROPEFFECT_COPY) != 0;
		}
		catch {
			return false;
		}
		finally {
			if (pSource != IntPtr.Zero) Marshal.Release(pSource);
			if (pData != IntPtr.Zero) Marshal.Release(pData);
			GC.KeepAlive(data);
			if (inited) OleUninitialize();
		}
	}

	// ==================== 数据对象 ====================
	#region
	[ComImport, Guid("00000121-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface IDropSource {
		[PreserveSig] int QueryContinueDrag(int fEscapePressed, int grfKeyState);
		[PreserveSig] int GiveFeedback(int dwEffect);
	}

	/// <summary>文件组描述符 + 每个文件的流。资源管理器按 lindex 来取内容。</summary>
	sealed class DragData : ComTypes.IDataObject, IDropSource {
		readonly FtpSite site;
		readonly string password;
		readonly List<Item> files;

		public DragData(FtpSite site, string password, IList<Item> files) {
			this.site = site;
			this.password = password;
			this.files = new List<Item>(files);
		}

		// ---------- IDropSource ----------
		public int QueryContinueDrag(int fEscapePressed, int grfKeyState) {
			if (fEscapePressed != 0) return DRAGDROP_S_CANCEL;
			// 鼠标左键松开即视为放下；放到资源管理器由 OLE 通知
			if ((grfKeyState & MK_LBUTTON) == 0) return DRAGDROP_S_DROP;
			return S_OK;
		}

		public int GiveFeedback(int dwEffect) => DRAGDROP_S_USEDEFAULTCURSORS;

		// ---------- IDataObject ----------
		public void GetData(ref FORMATETC format, out STGMEDIUM medium) {
			medium = new STGMEDIUM { tymed = 0, unionmember = IntPtr.Zero, pUnkForRelease = IntPtr.Zero };
			if (format.cfFormat == fmtDescriptor) {
				if (format.dwAspect != DVASPECT.DVASPECT_CONTENT) throw Marshal.GetExceptionForHR(DV_E_DVASPECT);
				medium.tymed = TYMED.TYMED_HGLOBAL;
				medium.unionmember = hglobal(BuildGroupDescriptor(files));
				return;
			}
			if (format.cfFormat == fmtContents) {
				if (format.dwAspect != DVASPECT.DVASPECT_CONTENT) throw Marshal.GetExceptionForHR(DV_E_DVASPECT);
				if (format.lindex < 0 || format.lindex >= files.Count) throw Marshal.GetExceptionForHR(DV_E_LINDEX);
				var stream = new RemoteStream(site, password, files[format.lindex]);
				medium.tymed = TYMED.TYMED_ISTREAM;
				// 资源管理器释放 STGMEDIUM 时会 Release 这个流
				medium.unionmember = Marshal.GetComInterfaceForObject(stream, typeof(ComTypes.IStream));
				return;
			}
			if (format.cfFormat == fmtPreferred) {
				medium.tymed = TYMED.TYMED_HGLOBAL;
				medium.unionmember = hglobal(BitConverter.GetBytes(DROPEFFECT_COPY));
				return;
			}
			throw Marshal.GetExceptionForHR(DV_E_FORMATETC);
		}

		public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) =>
			throw Marshal.GetExceptionForHR(E_NOTIMPL);

		[PreserveSig]
		public int QueryGetData(ref FORMATETC format) {
			var tymed = format.tymed;
			if (format.cfFormat == fmtDescriptor || format.cfFormat == fmtPreferred) {
				if (tymed != 0 && (tymed & TYMED.TYMED_HGLOBAL) == 0) return DV_E_TYMED;
				return S_OK;
			}
			if (format.cfFormat == fmtContents) {
				if (tymed != 0 && (tymed & TYMED.TYMED_ISTREAM) == 0) return DV_E_TYMED;
				if (format.lindex < 0 || format.lindex >= files.Count) return DV_E_LINDEX;
				return S_OK;
			}
			return DV_E_FORMATETC;
		}

		[PreserveSig]
		public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut) {
			formatOut = formatIn;
			return 0x00040130; // DATA_S_SAMEFORMATETC
		}

		public void SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release) =>
			throw Marshal.GetExceptionForHR(E_NOTIMPL);

		public IEnumFORMATETC EnumFormatEtc(DATADIR direction) {
			if (direction != DATADIR.DATADIR_GET) throw Marshal.GetExceptionForHR(E_NOTIMPL);
			return new FormatEnum(new[] {
				new FORMATETC { cfFormat = fmtDescriptor, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL },
				new FORMATETC { cfFormat = fmtContents, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_ISTREAM },
				new FORMATETC { cfFormat = fmtPreferred, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL }
			});
		}

		[PreserveSig]
		public int DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection) {
			connection = 0;
			return OLE_E_ADVISENOTSUPPORTED;
		}

		public void DUnadvise(int connection) => throw Marshal.GetExceptionForHR(OLE_E_ADVISENOTSUPPORTED);

		[PreserveSig]
		public int EnumDAdvise(out IEnumSTATDATA enumAdvise) {
			enumAdvise = null;
			return OLE_E_ADVISENOTSUPPORTED;
		}
	}

	/// <summary>三个固定格式的枚举器。</summary>
	sealed class FormatEnum : IEnumFORMATETC {
		readonly FORMATETC[] items;
		int pos;

		public FormatEnum(FORMATETC[] items) { this.items = items; }

		[PreserveSig]
		public int Next(int celt, FORMATETC[] rgelt, int[] pceltFetched) {
			int n = 0;
			if (celt > 0 && rgelt != null) {
				while (n < celt && pos < items.Length) rgelt[n++] = items[pos++];
			}
			if (pceltFetched != null && pceltFetched.Length > 0) pceltFetched[0] = n;
			return n == celt ? S_OK : 1; // S_FALSE
		}

		[PreserveSig]
		public int Skip(int celt) {
			pos += celt;
			if (pos > items.Length) pos = items.Length;
			return pos < items.Length ? S_OK : 1;
		}

		[PreserveSig]
		public int Reset() { pos = 0; return S_OK; }

		public void Clone(out IEnumFORMATETC newEnum) {
			var e = new FormatEnum(items) { pos = pos };
			newEnum = e;
		}
	}

	/// <summary>远端文件内容流。资源管理器读多少我们传多少。</summary>
	sealed class RemoteStream : ComTypes.IStream {
		readonly FtpSite site;
		readonly string password;
		readonly Item file;
		readonly object gate = new object();

		IFtpBackend backend;
		Stream src;
		long pos;

		public RemoteStream(FtpSite site, string password, Item file) {
			this.site = site;
			this.password = password;
			this.file = file;
		}

		void ensureopen() {
			if (src != null) return;
			var be = FtpBackendFactory.Create(site);
			try {
				be.Connect(site, password);
				src = be.OpenRead(file.Remote, pos, 0);
				backend = be;
			}
			catch {
				try { be.Dispose(); } catch { }
				throw;
			}
		}

		void closelocked() {
			try { src?.Dispose(); } catch { }
			src = null;
			try { backend?.Dispose(); } catch { }
			backend = null;
		}

		~RemoteStream() {
			try { closelocked(); } catch { }
		}

		public void Read(byte[] pv, int cb, IntPtr pcbRead) {
			int n = 0;
			lock (gate) {
				try {
					ensureopen();
					if (pv != null && cb > 0) {
						n = src.Read(pv, 0, cb);
						pos += n;
					}
				}
				catch {
					closelocked();
					throw Marshal.GetExceptionForHR(STG_E_READFAULT);
				}
				lastRead = n;
			}
			if (pcbRead != IntPtr.Zero) Marshal.WriteInt32(pcbRead, n);
		}

		public void Write(byte[] pv, int cb, IntPtr pcbWritten) => throw Marshal.GetExceptionForHR(E_NOTIMPL);

		public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition) {
			lock (gate) {
				long np = dwOrigin switch {
					1 => pos + dlibMove,
					2 => file.Size + dlibMove,
					_ => dlibMove
				};
				if (np < 0) np = 0;
				pos = np;
				closelocked();
				if (plibNewPosition != IntPtr.Zero) Marshal.WriteInt64(plibNewPosition, pos);
			}
		}

		public void SetSize(long libNewSize) => throw Marshal.GetExceptionForHR(E_NOTIMPL);

		public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten) {
			long total = 0;
			var buf = new byte[64 * 1024];
			while (cb <= 0 || total < cb) {
				int want = buf.Length;
				if (cb > 0 && cb - total < want) want = (int)(cb - total);
				Read(buf, want, IntPtr.Zero);
				int n = lastRead;
				if (n <= 0) break;
				pstm.Write(buf, n, IntPtr.Zero);
				total += n;
			}
			if (pcbRead != IntPtr.Zero) Marshal.WriteInt64(pcbRead, total);
			if (pcbWritten != IntPtr.Zero) Marshal.WriteInt64(pcbWritten, total);
		}

		int lastRead;

		public void Commit(int grfCommitFlags) => throw Marshal.GetExceptionForHR(E_NOTIMPL);

		public void Revert() => throw Marshal.GetExceptionForHR(E_NOTIMPL);

		public void LockRegion(long libOffset, long cb, int dwLockType) => throw Marshal.GetExceptionForHR(E_NOTIMPL);

		public void UnlockRegion(long libOffset, long cb, int dwLockType) => throw Marshal.GetExceptionForHR(E_NOTIMPL);

		public void Stat(out ComTypes.STATSTG pstatstg, int grfStatFlag) {
			pstatstg = new ComTypes.STATSTG {
				type = 2, // STGTY_STREAM
				cbSize = file.Size,
				grfMode = 0,
				grfLocksSupported = 0,
				clsid = Guid.Empty,
				grfStateBits = 0,
				reserved = 0
			};
			if ((grfStatFlag & 1) == 0) pstatstg.pwcsName = file.Name;
			if (file.Modified != default) {
				long ft = file.Modified.ToFileTimeUtc();
				pstatstg.mtime.dwLowDateTime = (int)(ft & 0xFFFFFFFF);
				pstatstg.mtime.dwHighDateTime = (int)(ft >> 32);
			}
		}

		public void Clone(out IStream ppstm) {
			ppstm = new RemoteStream(site, password, file);
		}
	}
	#endregion

	// ==================== 自测 ====================
	#region
	/// <summary>
	/// 自测：按资源管理器的方式向数据对象要文件组描述符和文件流（走 COM 编组），核对名称、大小和字节数。
	/// 不发 OLE 拖拽，所以能命令行跑。返回 0 表示通过。
	/// </summary>
	internal static int SelfTest(FtpSite site, string password, string remote, Action<string> log, Action<string> err) {
		long size;
		string full;
		var probe = FtpBackendFactory.Create(site);
		try {
			probe.Connect(site, password ?? "");
			full = probe.Full(remote);
			size = probe.Size(full);
		}
		catch (Exception ex) {
			err("连接失败: " + ex.Message);
			return 3;
		}
		finally {
			try { probe.Dispose(); } catch { }
		}
		if (size < 0) { err("远端文件不存在: " + remote); return 4; }
		log($"远端 {full}，{size} 字节");

		var item = new Item {
			Remote = full,
			Name = Path.GetFileName(full.TrimEnd('/')),
			Size = size,
			Modified = DateTime.Now
		};
		var data = new DragData(site, password ?? "", new List<Item> { item });

		var fmtGroup = new FORMATETC {
			cfFormat = fmtDescriptor,
			dwAspect = DVASPECT.DVASPECT_CONTENT,
			lindex = -1,
			tymed = TYMED.TYMED_HGLOBAL
		};
		data.GetData(ref fmtGroup, out var group);
		var raw = new byte[4 + DESCRIPTOR_SIZE];
		Marshal.Copy(group.unionmember, raw, 0, raw.Length);
		GlobalFree(group.unionmember);

		int count = BitConverter.ToInt32(raw, 0);
		long fsize = ((long)BitConverter.ToUInt32(raw, 4 + 64) << 32) | BitConverter.ToUInt32(raw, 4 + 68);
		var fname = Encoding.Unicode.GetString(raw, 4 + 72, 520).TrimEnd('\0');
		log($"描述符: {count} 项，名称={fname}，大小={fsize}");
		if (count != 1 || fname != item.Name || fsize != size) { err("描述符内容不对"); return 5; }

		var fmtStream = new FORMATETC {
			cfFormat = fmtContents,
			dwAspect = DVASPECT.DVASPECT_CONTENT,
			lindex = 0,
			tymed = TYMED.TYMED_ISTREAM
		};
		data.GetData(ref fmtStream, out var content);
		var rawStream = content.unionmember;
		var stm = (ComTypes.IStream)Marshal.GetObjectForIUnknown(rawStream);
		long total = 0;
		var buf = new byte[64 * 1024];
		var pcb = Marshal.AllocHGlobal(8);
		try {
			Marshal.WriteInt32(pcb, 0);
			while (true) {
				stm.Read(buf, buf.Length, pcb);
				int n = Marshal.ReadInt32(pcb);
				if (n <= 0) break;
				total += n;
			}
		}
		finally {
			Marshal.FreeHGlobal(pcb);
			Marshal.ReleaseComObject(stm);
			Marshal.Release(rawStream);
		}
		log($"流读取完成: {total} 字节");
		if (total != size) { err("流长度与文件大小不符"); return 6; }
		log("拖出数据对象 OK");
		return 0;
	}
	#endregion

	// ==================== 描述符 ====================
	#region
	static byte[] BuildGroupDescriptor(List<Item> files) {
		var buf = new byte[4 + files.Count * DESCRIPTOR_SIZE];
		using var ms = new MemoryStream(buf, true);
		using var bw = new BinaryWriter(ms, Encoding.Unicode);
		bw.Write((uint)files.Count);
		foreach (var f in files) {
			bw.Write(FD_ATTRIBUTES | FD_FILESIZE | FD_PROGRESSUI | FD_UNICODE
				| (f.Modified == default ? 0u : FD_WRITESTIME));
			bw.Write(new byte[16]);           // CLSID
			bw.Write(0);                      // sizel.cx
			bw.Write(0);                      // sizel.cy
			bw.Write(0);                      // pointl.x
			bw.Write(0);                      // pointl.y
			bw.Write(FILE_ATTRIBUTE_NORMAL);
			bw.Write(0L);                     // ftCreationTime
			bw.Write(0L);                     // ftLastAccessTime
			bw.Write(f.Modified == default ? 0L : f.Modified.ToFileTimeUtc());
			bw.Write((uint)(f.Size >> 32));
			bw.Write((uint)(f.Size & 0xFFFFFFFF));
			var name = f.Name ?? "";
			if (name.Length > 259) name = name.Substring(0, 259);
			var chars = new char[260];
			name.CopyTo(0, chars, 0, name.Length);
			foreach (var ch in chars) bw.Write((ushort)ch);
		}
		bw.Flush();
		return buf;
	}

	static IntPtr hglobal(byte[] data) {
		var h = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (UIntPtr)(uint)data.Length);
		if (h == IntPtr.Zero) return IntPtr.Zero;
		var p = GlobalLock(h);
		if (p == IntPtr.Zero) return h;
		try { Marshal.Copy(data, 0, p, data.Length); }
		finally { GlobalUnlock(h); }
		return h;
	}
	#endregion
}


