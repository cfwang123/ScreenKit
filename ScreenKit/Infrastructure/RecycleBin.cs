using System.Runtime.InteropServices;
using System.Text;

namespace ScreenKit;

/// <summary>把文件或文件夹移到回收站（可还原）。失败抛异常，不回退成永久删除。</summary>
static class RecycleBin {
	const uint FO_DELETE = 3;
	const ushort FOF_SILENT = 0x0004;
	const ushort FOF_NOCONFIRMATION = 0x0010;
	const ushort FOF_ALLOWUNDO = 0x0040;
	const ushort FOF_NOERRORUI = 0x0400;

	public static void Send(string path) {
		if (string.IsNullOrWhiteSpace(path)) return;
		Send(new[] { path });
	}

	public static void Send(IEnumerable<string> paths) {
		if (paths == null) return;
		var list = new List<string>();
		foreach (var p in paths) {
			if (string.IsNullOrWhiteSpace(p)) continue;
			string full;
			try { full = Path.GetFullPath(p); }
			catch { continue; }
			if (File.Exists(full) || Directory.Exists(full))
				list.Add(full);
		}
		if (list.Count == 0) return;
		var sb = new StringBuilder();
		foreach (var p in list) {
			sb.Append(p);
			sb.Append('\0');
		}
		var mem = Marshal.StringToHGlobalUni(sb.ToString());
		try {
			var op = new ShFileOp {
				wFunc = FO_DELETE,
				pFrom = mem,
				fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT),
			};
			var rc = SHFileOperationW(ref op);
			if (rc != 0) throw new IOException(Loc.T("imgconv.recycle.fail", rc));
			if (op.fAnyOperationsAborted != 0)
				throw new IOException(Loc.T("imgconv.recycle.abort"));
		}
		finally {
			Marshal.FreeHGlobal(mem);
		}
	}

	[StructLayout(LayoutKind.Explicit, Size = 64)]
	struct ShFileOp {
		[FieldOffset(0)] public IntPtr hwnd;
		[FieldOffset(8)] public uint wFunc;
		[FieldOffset(16)] public IntPtr pFrom;
		[FieldOffset(24)] public IntPtr pTo;
		[FieldOffset(32)] public ushort fFlags;
		[FieldOffset(36)] public int fAnyOperationsAborted;
		[FieldOffset(48)] public IntPtr hNameMappings;
		[FieldOffset(56)] public IntPtr lpszProgressTitle;
	}

	[DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
	static extern int SHFileOperationW(ref ShFileOp op);
}
