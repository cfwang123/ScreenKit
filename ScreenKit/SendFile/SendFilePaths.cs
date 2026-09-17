using System.IO;

namespace ScreenKit;

/// <summary>sendfile/ 沙箱路径：所有相对路径必须落在根目录内。</summary>
static class SendFilePaths {
	static string testRoot;

	public static void SetRootForTest(string dir) {
		testRoot = string.IsNullOrWhiteSpace(dir) ? null : Path.GetFullPath(dir);
	}

	public static string Root() {
		if (!string.IsNullOrEmpty(testRoot)) return testRoot;
		return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sendfile"));
	}

	public static void EnsureRoot() {
		var r = Root();
		if (!Directory.Exists(r))
			Directory.CreateDirectory(r);
	}

	/// <summary>相对路径转绝对路径；非法则返回 false。</summary>
	public static bool TryResolve(string rel, out string full, out string err) {
		full = null;
		err = null;
		EnsureRoot();
		var root = Root();
		var rootFull = trail(Path.GetFullPath(root));
		rel = (rel ?? "").Replace('\\', '/').Trim();
		if (rel.StartsWith("/")) rel = rel.TrimStart('/');
		if (rel.Contains('\0')) {
			err = "路径含非法字符";
			return false;
		}
		if (rel.IndexOf(':') >= 0) {
			err = "不允许绝对路径";
			return false;
		}
		string combined;
		try {
			combined = string.IsNullOrEmpty(rel) ? root : Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
			full = Path.GetFullPath(combined);
		}
		catch (Exception ex) {
			err = ex.Message;
			return false;
		}
		var fullTrail = trail(full);
		if (!fullTrail.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) {
			err = "路径越出 sendfile";
			full = null;
			return false;
		}
		return true;
	}

	public static string RelFrom(string full) {
		var root = trail(Root());
		var f = Path.GetFullPath(full ?? "");
		if (f.Length >= root.Length && f.StartsWith(root, StringComparison.OrdinalIgnoreCase))
			return f.Substring(root.Length).Replace('\\', '/');
		if (string.Equals(f.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
			Root().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
			StringComparison.OrdinalIgnoreCase))
			return "";
		return Path.GetFileName(f);
	}

	static string trail(string p) {
		p = (p ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return p + Path.DirectorySeparatorChar;
	}
}
