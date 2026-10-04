using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ScreenKit;

/// <summary>进程内存，以及已加载模型在列表里的一行。</summary>
sealed class MemHold {
	public string Id { get; set; } = "";
	public string Engine { get; set; } = "";
	public string Name { get; set; } = "";
	public string Device { get; set; } = "";
	public long Bytes { get; set; }
	public string EngineText { get; set; } = "";
	public string BytesText { get; set; } = "";
}

sealed class MemSnap {
	public long WorkingSet;
	public long PrivateBytes;
	public long GcBytes;
	public List<MemHold> Items = new();
}

static class MemUsage {
	[DllImport("psapi.dll")]
	static extern bool EmptyWorkingSet(IntPtr hProcess);

	public static MemSnap Read() {
		using var p = Process.GetCurrentProcess();
		p.Refresh();
		return new MemSnap {
			WorkingSet = p.WorkingSet64,
			PrivateBytes = p.PrivateMemorySize64,
			GcBytes = GC.GetTotalMemory(false),
		};
	}

	/// <summary>模型目录里的 onnx / bin / fst。不含其它文件。</summary>
	public static long DirWeight(string dir) {
		if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
		long n = 0;
		try {
			foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) {
				var e = Path.GetExtension(f);
				if (e.Equals(".onnx", StringComparison.OrdinalIgnoreCase)
					|| e.Equals(".bin", StringComparison.OrdinalIgnoreCase)
					|| e.Equals(".fst", StringComparison.OrdinalIgnoreCase)) {
					try { n += new FileInfo(f).Length; } catch { }
				}
			}
		}
		catch { }
		return n;
	}

	public static long FileWeight(string path) {
		try {
			if (!string.IsNullOrEmpty(path) && File.Exists(path))
				return new FileInfo(path).Length;
		}
		catch { }
		return 0;
	}

	/// <summary>释放托管垃圾后，请系统收回空闲工作集。</summary>
	public static void Trim() {
		try {
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
		}
		catch { }
		try { EmptyWorkingSet(Process.GetCurrentProcess().Handle); } catch { }
	}

	public static int SelfTest() {
		var dir = Path.Combine(Path.GetTempPath(), "sk-mem-" + Guid.NewGuid().ToString("N"));
		try {
			Directory.CreateDirectory(dir);
			File.WriteAllBytes(Path.Combine(dir, "a.onnx"), new byte[1000]);
			File.WriteAllBytes(Path.Combine(dir, "skip.txt"), new byte[50]);
			var sub = Path.Combine(dir, "sub");
			Directory.CreateDirectory(sub);
			File.WriteAllBytes(Path.Combine(sub, "b.bin"), new byte[20]);
			if (DirWeight(dir) != 1020) return 1;
			if (FileWeight(Path.Combine(dir, "a.onnx")) != 1000) return 2;
		}
		finally {
			try { Directory.Delete(dir, true); } catch { }
		}
		var p = Read();
		if (p.WorkingSet <= 0 || p.PrivateBytes <= 0) return 3;
		return 0;
	}
}
