using System.IO;

namespace ScreenKit;

/// <summary>程序目录下 tmp/：临时文件与过期清理。</summary>
static class TmpStore {
	/// <summary>默认保留 10 小时。</summary>
	public const int ExpireHours = 10;

	/// <summary>「复制文件」固定路径，连续复制复用同一文件。</summary>
	public static string ClipCopyPath => Path.Combine(Root, "clip_copy.png");

	public static string Root {
		get {
			var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp");
			try { Directory.CreateDirectory(dir); } catch { }
			return dir;
		}
	}

	public static string NewPath(string prefix, string ext) {
		var name = $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss_fff}{ext}";
		return Path.Combine(Root, name);
	}

	/// <summary>删除超过 expireHours 的临时文件（含子目录内文件）。</summary>
	public static void CleanupExpired(int expireHours = ExpireHours) {
		try {
			var root = Root;
			if (!Directory.Exists(root)) return;
			var cutoff = DateTime.Now.AddHours(-Math.Max(1, expireHours));
			foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) {
				try {
					var t = File.GetLastWriteTime(f);
					if (t < cutoff) File.Delete(f);
				}
				catch { }
			}
		}
		catch { }
	}
}
