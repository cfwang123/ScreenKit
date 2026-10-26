using System.IO;

namespace ScreenKit;

/// <summary>程序目录下 tmp/：临时文件与过期清理。</summary>
static class TmpStore {
	/// <summary>默认保留 10 小时。</summary>
	public const int ExpireHours = 10;

	public static string Root {
		get {
			var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp");
			try { Directory.CreateDirectory(dir); } catch { }
			return dir;
		}
	}

	/// <summary>名字带随机段：多路并发在同一毫秒里调用也不会撞名。</summary>
	public static string NewPath(string prefix, string ext) {
		var name = $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid().ToString("N").Substring(0, 8)}{ext}";
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
					if (isvoicecache(f) || isusbids(f)) continue;
					var t = File.GetLastWriteTime(f);
					if (t < cutoff) File.Delete(f);
				}
				catch { }
			}
		}
		catch { }
	}

	/// <summary>usb.ids 由网络工具按 7 天自行更新，不走 10 小时清理。</summary>
	static bool isusbids(string path) =>
		string.Equals(Path.GetFileName(path), "usb.ids", StringComparison.OrdinalIgnoreCase);

	/// <summary>词典发音缓存在 tmp/voice，单独保留 1 天，不走上面的 10 小时清理。</summary>
	static bool isvoicecache(string path) {
		var voice = Path.GetFullPath(Path.Combine(Root, "voice"));
		var full = Path.GetFullPath(path);
		return full.StartsWith(voice + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}
}
