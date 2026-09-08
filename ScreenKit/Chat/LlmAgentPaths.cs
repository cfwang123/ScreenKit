using System.IO;

namespace ScreenKit;

/// <summary>Agent 沙箱：程序目录下 tmp/llm/。</summary>
static class LlmAgentPaths {
	public static string Root {
		get {
			var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp", "llm");
			try { Directory.CreateDirectory(dir); } catch { }
			return dir;
		}
	}

	/// <summary>
	/// 将相对/绝对路径解析到沙箱内完整路径。越界或非法返回 false。
	/// </summary>
	public static bool TryResolve(string path, out string full, out string rel, out string err) {
		full = null;
		rel = null;
		err = null;
		path = (path ?? "").Trim();
		if (path.Length == 0 || path == "." || path == "./") {
			full = Root;
			rel = ".";
			return true;
		}
		// 禁止 UNC / 多余分隔
		if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) {
			err = "不允许 UNC 路径";
			return false;
		}
		string candidate;
		try {
			var root = Path.GetFullPath(Root);
			if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
				&& !root.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
				root += Path.DirectorySeparatorChar;
			if (Path.IsPathRooted(path))
				candidate = Path.GetFullPath(path);
			else
				candidate = Path.GetFullPath(Path.Combine(Root, path));
			var rootFull = Path.GetFullPath(Root);
			if (!rootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
				&& !rootFull.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
				rootFull += Path.DirectorySeparatorChar;
			var check = candidate;
			if (!check.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
				&& Directory.Exists(check))
				check += Path.DirectorySeparatorChar;
			// 文件：用父目录+文件名比较前缀
			var under = candidate.Equals(Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase)
				|| candidate.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);
			if (!under) {
				err = "路径超出 tmp/llm/ 沙箱";
				return false;
			}
			full = candidate;
			rel = relative(full);
			return true;
		}
		catch (Exception ex) {
			err = "路径无效: " + ex.Message;
			return false;
		}
	}

	public static string RelativeOf(string full) {
		try { return relative(Path.GetFullPath(full)); }
		catch { return full ?? ""; }
	}

	static string relative(string full) {
		var root = Path.GetFullPath(Root);
		full = Path.GetFullPath(full ?? "");
		if (full.Equals(root, StringComparison.OrdinalIgnoreCase)) return ".";
		if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
			|| full.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			return full.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
				.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
		return full;
	}
}
