using System.IO;
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
			foreach (var d in Directory.GetDirectories(full))
				arr.Add(item(d, dir: true));
			foreach (var f in Directory.GetFiles(full))
				arr.Add(item(f, dir: false));
		}
		return arr;
	}

	public static void Mkdir(string rel) {
		if (!SendFilePaths.TryResolve(rel, out var full, out var err))
			throw new InvalidOperationException(err ?? "路径非法");
		if (string.IsNullOrEmpty(rel) || rel == "." || rel == "/") return;
		Directory.CreateDirectory(full);
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

	public static void SaveStream(string rel, Stream src) {
		if (string.IsNullOrWhiteSpace(rel) || rel.EndsWith("/") || rel.EndsWith("\\"))
			throw new InvalidOperationException("需要文件路径");
		if (!SendFilePaths.TryResolve(rel, out var full, out var err))
			throw new InvalidOperationException(err ?? "路径非法");
		var dir = Path.GetDirectoryName(full);
		if (!string.IsNullOrEmpty(dir))
			Directory.CreateDirectory(dir);
		using var fs = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None);
		src.CopyTo(fs);
	}

	static void walk(string dir, JsonArray arr) {
		foreach (var d in Directory.GetDirectories(dir)) {
			arr.Add(item(d, dir: true));
			walk(d, arr);
		}
		foreach (var f in Directory.GetFiles(dir))
			arr.Add(item(f, dir: false));
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
