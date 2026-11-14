using System;
using System.Collections.Generic;
using System.IO;

namespace ScreenKit;

// ==================== 媒体类型与扩展名 ====================
#region
enum MediaKind { Unknown, Video, Audio }

static class MediaFormat {
	static readonly HashSet<string> videoext = new(StringComparer.OrdinalIgnoreCase) {
		".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v",
		".ts", ".mts", ".m2ts", ".mpeg", ".mpg", ".3gp", ".asf", ".rmvb", ".rm",
		".ogv", ".vob", ".divx", ".f4v",
	};
	static readonly HashSet<string> audioext = new(StringComparer.OrdinalIgnoreCase) {
		".mp3", ".wav", ".flac", ".m4a", ".ogg", ".opus", ".wma", ".aac",
		".aiff", ".aif", ".ape", ".wv", ".dsf", ".dff", ".mka", ".amr", ".au",
	};
	static readonly HashSet<string> subext = new(StringComparer.OrdinalIgnoreCase) {
		".srt", ".vtt", ".ass", ".ssa", ".lrc",
	};

	public static bool IsVideo(string path) =>
		!string.IsNullOrWhiteSpace(path) && videoext.Contains(Path.GetExtension(path));

	public static bool IsAudio(string path) =>
		!string.IsNullOrWhiteSpace(path) && audioext.Contains(Path.GetExtension(path));

	public static bool IsMedia(string path) => IsVideo(path) || IsAudio(path);

	public static bool IsSubtitle(string path) =>
		!string.IsNullOrWhiteSpace(path) && subext.Contains(Path.GetExtension(path));

	public static MediaKind Kind(string path) {
		if (IsVideo(path)) return MediaKind.Video;
		if (IsAudio(path)) return MediaKind.Audio;
		return MediaKind.Unknown;
	}

	/// <summary>按扩展名取媒体文件；枚举目录时用，顺序固定便于测试。</summary>
	public static List<string> EnumerateMedia(string dir, int maxDepth = 8, int maxCount = 3000) {
		var list = new List<string>();
		if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return list;
		try {
			walk(dir, 0);
		}
		catch {
			// 权限不足的子树直接跳过
		}
		return list;

		void walk(string d, int depth) {
			if (list.Count >= maxCount) return;
			IEnumerable<string> files;
			try { files = Directory.EnumerateFiles(d); }
			catch { return; }
			var batch = new List<string>();
			try {
				foreach (var f in files) {
					if (list.Count + batch.Count >= maxCount) break;
					if (IsMedia(f)) batch.Add(f);
				}
			}
			catch { }
			batch.Sort(NaturalCompare.Compare);
			list.AddRange(batch);
			if (depth >= maxDepth || list.Count >= maxCount) return;
			IEnumerable<string> subs;
			try { subs = Directory.EnumerateDirectories(d); }
			catch { return; }
			var dirs = new List<string>();
			try {
				foreach (var s in subs) {
					var name = Path.GetFileName(s);
					if (name.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase)) continue;
					if (name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)) continue;
					var at = File.GetAttributes(s);
					if ((at & FileAttributes.Hidden) != 0 || (at & FileAttributes.System) != 0) continue;
					dirs.Add(s);
				}
			}
			catch { }
			dirs.Sort(NaturalCompare.Compare);
			foreach (var s in dirs) {
				if (list.Count >= maxCount) return;
				walk(s, depth + 1);
			}
		}
	}
}

/// <summary>文件名自然排序：a2 排在 a10 前面。</summary>
static class NaturalCompare {
	public static int Compare(string a, string b) {
		if (a == null) return b == null ? 0 : -1;
		if (b == null) return 1;
		int i = 0, j = 0;
		while (i < a.Length && j < b.Length) {
			if (char.IsDigit(a[i]) && char.IsDigit(b[j])) {
				int si = i, sj = j;
				while (i < a.Length && char.IsDigit(a[i])) i++;
				while (j < b.Length && char.IsDigit(b[j])) j++;
				var na = a.Substring(si, i - si).TrimStart('0');
				var nb = b.Substring(sj, j - sj).TrimStart('0');
				if (na.Length != nb.Length) return na.Length - nb.Length;
				var c = string.CompareOrdinal(na, nb);
				if (c != 0) return c;
				continue;
			}
			var ca = char.ToLowerInvariant(a[i]);
			var cb = char.ToLowerInvariant(b[j]);
			if (ca != cb) return ca - cb;
			i++;
			j++;
		}
		return (a.Length - i) - (b.Length - j);
	}
}
#endregion
