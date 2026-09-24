using System.IO;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace ScreenKit;

/// <summary>sendfile 目录上的列出/建目录/上传/删除。</summary>
static class SendFileOps {
	public static JsonArray List(string rel, bool deep) {
		if (!SendFilePaths.TryResolve(rel, out var full, out var err))
			throw new InvalidOperationException(err ?? "路径非法");
		if (!Directory.Exists(full) && !File.Exists(full)) {
			if (string.IsNullOrEmpty(rel) || rel == "." || rel == "/") {
				SendFilePaths.EnsureRoot();
				full = SendFilePaths.Root();
			}
			else
				throw new InvalidOperationException("路径不存在");
		}
		if (File.Exists(full))
			throw new InvalidOperationException("不是目录");
		var arr = new JsonArray();
		if (deep)
			walk(full, arr);
		else {
			foreach (var d in Directory.GetDirectories(full)) {
				if (hiddendir(d)) continue;
				arr.Add(item(d, dir: true));
			}
			foreach (var f in Directory.GetFiles(full)) {
				if (hiddendir(f)) continue;
				arr.Add(item(f, dir: false));
			}
		}
		return arr;
	}

	public static void Mkdir(string rel) {
		if (!SendFilePaths.TryResolve(rel, out var full, out var err))
			throw new InvalidOperationException(err ?? "路径非法");
		if (string.IsNullOrEmpty(rel) || rel == "." || rel == "/") return;
		Directory.CreateDirectory(full);
	}

	/// <summary>沙箱内改名或移动；目标已存在则失败。</summary>
	public static void Rename(string fromRel, string toRel) {
		fromRel = (fromRel ?? "").Replace('\\', '/').Trim('/');
		toRel = (toRel ?? "").Replace('\\', '/').Trim('/');
		if (string.IsNullOrWhiteSpace(fromRel))
			throw new InvalidOperationException("不能改名根目录");
		if (string.IsNullOrWhiteSpace(toRel))
			throw new InvalidOperationException("目标路径空");
		if (!SendFilePaths.TryResolve(fromRel, out var fromFull, out var fromErr))
			throw new InvalidOperationException(fromErr ?? "源路径非法");
		if (!SendFilePaths.TryResolve(toRel, out var toFull, out var toErr))
			throw new InvalidOperationException(toErr ?? "目标路径非法");
		if (string.Equals(fromFull, toFull, StringComparison.OrdinalIgnoreCase))
			return;
		if (!File.Exists(fromFull) && !Directory.Exists(fromFull))
			throw new InvalidOperationException("源不存在");
		if (File.Exists(toFull) || Directory.Exists(toFull))
			throw new InvalidOperationException("目标已存在");
		var destDir = Path.GetDirectoryName(toFull);
		if (!string.IsNullOrEmpty(destDir))
			Directory.CreateDirectory(destDir);
		if (Directory.Exists(fromFull)) {
			if (isunder(toFull, fromFull))
				throw new InvalidOperationException("不能移动到自身内部");
			Directory.Move(fromFull, toFull);
		}
		else
			File.Move(fromFull, toFull);
	}

	public static void Delete(string rel) {
		if (string.IsNullOrWhiteSpace(rel) || rel == "." || rel == "/")
			throw new InvalidOperationException("不能删除根目录");
		if (!SendFilePaths.TryResolve(rel, out var full, out var err))
			throw new InvalidOperationException(err ?? "路径非法");
		if (Directory.Exists(full))
			Directory.Delete(full, true);
		else if (File.Exists(full))
			File.Delete(full);
		else
			throw new InvalidOperationException("路径不存在");
	}

	/// <summary>把外部文件或文件夹拷进 destDirRel（根用空串）。返回目标相对路径。</summary>
	public static string Import(string srcFull, string destDirRel) {
		if (string.IsNullOrWhiteSpace(srcFull))
			throw new InvalidOperationException("源路径空");
		srcFull = Path.GetFullPath(srcFull);
		var isDir = Directory.Exists(srcFull);
		if (!isDir && !File.Exists(srcFull))
			throw new InvalidOperationException("源不存在");
		var name = Path.GetFileName(srcFull);
		if (string.IsNullOrEmpty(name))
			throw new InvalidOperationException("源名非法");
		var destRel = string.IsNullOrWhiteSpace(destDirRel) ? name : destDirRel.Trim('/') + "/" + name;
		destRel = UniqueRel(destRel, isDir);
		if (!SendFilePaths.TryResolve(destRel, out var destFull, out var err))
			throw new InvalidOperationException(err ?? "路径非法");
		if (string.Equals(srcFull, destFull, StringComparison.OrdinalIgnoreCase))
			return destRel;
		if (isDir) {
			if (isunder(destFull, srcFull))
				throw new InvalidOperationException("不能复制到自身内部");
			copydir(srcFull, destFull);
		}
		else {
			var dir = Path.GetDirectoryName(destFull);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
			File.Copy(srcFull, destFull);
		}
		return destRel;
	}

	public static string UniqueRel(string destRel, bool dir) {
		destRel = (destRel ?? "").Replace('\\', '/').Trim('/');
		if (SendFilePaths.TryResolve(destRel, out var full, out _)
			&& !File.Exists(full) && !Directory.Exists(full))
			return destRel;
		var parent = "";
		var i = destRel.LastIndexOf('/');
		var leaf = destRel;
		if (i >= 0) {
			parent = destRel.Substring(0, i);
			leaf = destRel.Substring(i + 1);
		}
		var ext = dir ? "" : Path.GetExtension(leaf);
		var stem = dir ? leaf : Path.GetFileNameWithoutExtension(leaf);
		if (string.IsNullOrEmpty(stem)) stem = "item";
		for (var n = 1; n < 10000; n++) {
			var name = dir ? $"{stem} ({n})" : $"{stem} ({n}){ext}";
			var rel = parent.Length == 0 ? name : parent + "/" + name;
			if (SendFilePaths.TryResolve(rel, out var f, out _)
				&& !File.Exists(f) && !Directory.Exists(f))
				return rel;
		}
		return destRel;
	}

	static bool isunder(string child, string parent) {
		var p = trail(parent);
		var c = trail(child);
		return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
	}

	static string trail(string p) {
		p = (p ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return p + Path.DirectorySeparatorChar;
	}

	static void copydir(string src, string dest) {
		Directory.CreateDirectory(dest);
		foreach (var f in Directory.GetFiles(src))
			File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), overwrite: false);
		foreach (var d in Directory.GetDirectories(src))
			copydir(d, Path.Combine(dest, Path.GetFileName(d)));
	}

	/// <summary>把沙箱内若干文件/目录打成临时 zip。返回临时文件路径与下载名。</summary>
	public static void ZipToTemp(IEnumerable<string> rels, out string tmp, out string zipName) {
		tmp = null;
		zipName = "sendfile.zip";
		if (rels == null)
			throw new InvalidOperationException("没有可打包的路径");
		var list = new List<string>();
		foreach (var r in rels) {
			var rel = (r ?? "").Replace('\\', '/').Trim('/');
			list.Add(rel);
		}
		if (list.Count == 0)
			throw new InvalidOperationException("没有可打包的路径");
		if (list.Count == 1) {
			var one = list[0];
			zipName = string.IsNullOrEmpty(one) ? "sendfile.zip" : Path.GetFileName(one) + ".zip";
			if (string.IsNullOrEmpty(Path.GetFileName(one)))
				zipName = "sendfile.zip";
		}
		tmp = Path.Combine(Path.GetTempPath(), "skzip_" + Guid.NewGuid().ToString("N") + ".zip");
		try {
			using (var zs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
			using (var zip = new ZipArchive(zs, ZipArchiveMode.Create)) {
				var n = 0;
				foreach (var rel in list)
					n += zipadd(zip, rel);
				if (n <= 0)
					throw new InvalidOperationException("没有可打包的文件");
			}
		}
		catch {
			try { if (tmp != null && File.Exists(tmp)) File.Delete(tmp); } catch { }
			throw;
		}
	}

	static int zipadd(ZipArchive zip, string rel) {
		if (!SendFilePaths.TryResolve(rel, out var full, out var err))
			throw new InvalidOperationException(err ?? "路径非法");
		if (File.Exists(full)) {
			if (hiddendir(full)) return 0;
			var name = string.IsNullOrEmpty(rel) ? Path.GetFileName(full) : Path.GetFileName(rel);
			if (string.IsNullOrEmpty(name)) name = "file";
			zip.CreateEntryFromFile(full, name, CompressionLevel.Fastest);
			return 1;
		}
		if (!Directory.Exists(full))
			throw new InvalidOperationException("路径不存在");
		var prefix = string.IsNullOrEmpty(rel) ? "" : Path.GetFileName(rel.TrimEnd('/')) + "/";
		if (string.IsNullOrEmpty(rel))
			prefix = "";
		return zipdir(zip, full, prefix);
	}

	static int zipdir(ZipArchive zip, string fullDir, string prefix) {
		var n = 0;
		string[] files;
		string[] dirs;
		try { files = Directory.GetFiles(fullDir); }
		catch { files = Array.Empty<string>(); }
		try { dirs = Directory.GetDirectories(fullDir); }
		catch { dirs = Array.Empty<string>(); }
		if (files.Length == 0 && dirs.Length == 0) {
			if (!string.IsNullOrEmpty(prefix))
				zip.CreateEntry(prefix);
			return 0;
		}
		foreach (var f in files) {
			if (hiddendir(f)) continue;
			var name = prefix + Path.GetFileName(f);
			zip.CreateEntryFromFile(f, name, CompressionLevel.Fastest);
			n++;
		}
		foreach (var d in dirs) {
			if (hiddendir(d)) continue;
			n += zipdir(zip, d, prefix + Path.GetFileName(d) + "/");
		}
		return n;
	}

	public static void SaveStream(string rel, Stream src, Action<int> onchunk = null) {
		if (string.IsNullOrWhiteSpace(rel) || rel.EndsWith("/") || rel.EndsWith("\\"))
			throw new InvalidOperationException("需要文件路径");
		if (!SendFilePaths.TryResolve(rel, out var full, out var err))
			throw new InvalidOperationException(err ?? "路径非法");
		var dir = Path.GetDirectoryName(full);
		if (!string.IsNullOrEmpty(dir))
			Directory.CreateDirectory(dir);
		using var fs = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None);
		var buf = new byte[64 * 1024];
		int n;
		while ((n = src.Read(buf, 0, buf.Length)) > 0) {
			fs.Write(buf, 0, n);
			onchunk?.Invoke(n);
		}
	}

	static bool hiddendir(string full) {
		var name = Path.GetFileName(full);
		return !string.IsNullOrEmpty(name) && name[0] == '.';
	}

	static void walk(string dir, JsonArray arr) {
		foreach (var d in Directory.GetDirectories(dir)) {
			if (hiddendir(d)) continue;
			arr.Add(item(d, dir: true));
			walk(d, arr);
		}
		foreach (var f in Directory.GetFiles(dir)) {
			if (hiddendir(f)) continue;
			arr.Add(item(f, dir: false));
		}
	}

	static JsonObject item(string full, bool dir) {
		var name = Path.GetFileName(full);
		long size = 0;
		long mtime = 0;
		try {
			if (dir) {
				mtime = new DateTimeOffset(Directory.GetLastWriteTimeUtc(full)).ToUnixTimeSeconds();
			}
			else {
				var fi = new FileInfo(full);
				size = fi.Length;
				mtime = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeSeconds();
			}
		}
		catch { }
		return new JsonObject {
			["name"] = name ?? "",
			["path"] = SendFilePaths.RelFrom(full),
			["dir"] = dir,
			["size"] = size,
			["mtime"] = mtime,
		};
	}
}
