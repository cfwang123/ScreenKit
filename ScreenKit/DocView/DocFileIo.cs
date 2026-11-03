using System;
using System.IO;

namespace ScreenKit.DocView;

/// <summary>
/// 文档文件读取：共享读，避免 Word/Excel 占用时打不开；
/// 仍失败则复制到临时文件再读。
/// </summary>
static class DocFileIo {
	const int BUF = 64 * 1024;

	/// <summary>共享读整文件到内存（可在后台线程调用）。</summary>
	public static byte[] ReadAllBytesShared(string path) {
		using var fs = OpenReadShared(path);
		using var ms = new MemoryStream();
		fs.CopyTo(ms);
		return ms.ToArray();
	}

	/// <summary>
	/// 打开只读流（调用方负责 Dispose）。
	/// 优先 FileShare.ReadWrite|Delete；若 IO 失败则复制到 %TEMP% 后以 DeleteOnClose 打开。
	/// </summary>
	public static Stream OpenReadShared(string path) {
		if (string.IsNullOrWhiteSpace(path))
			throw new ArgumentException("路径为空", nameof(path));
		path = Path.GetFullPath(path);
		if (!File.Exists(path))
			throw new FileNotFoundException("文件不存在", path);

		try {
			return openshared(path);
		} catch (IOException ex) {
			DocViewLog.Warn($"DocFileIo 共享打开失败，尝试临时副本: {ex.Message}");
			return openviacopy(path);
		} catch (UnauthorizedAccessException ex) {
			DocViewLog.Warn($"DocFileIo 访问被拒，尝试临时副本: {ex.Message}");
			return openviacopy(path);
		}
	}

	static FileStream openshared(string path) =>
		new FileStream(path, FileMode.Open, FileAccess.Read,
			FileShare.ReadWrite | FileShare.Delete, BUF);

	/// <summary>复制到临时文件再打开（源仍用共享读）。</summary>
	static FileStream openviacopy(string path) {
		var ext = Path.GetExtension(path);
		if (string.IsNullOrEmpty(ext)) ext = ".bin";
		var tmp = Path.Combine(Path.GetTempPath(), "ScreenKit_doc_" + Guid.NewGuid().ToString("N") + ext);
		try {
			using (var src = openshared(path))
			using (var dst = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, BUF))
				src.CopyTo(dst);
		} catch (Exception ex) {
			try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore */ }
			throw new IOException(
				"无法读取文件（可能正被 Word/Excel 独占打开）。\n请先关闭占用程序，或另存为副本后再打开。\n\n" + ex.Message,
				ex);
		}
		DocViewLog.Info($"DocFileIo 使用临时副本 {tmp}");
		// DeleteOnClose：流关闭后删临时文件
		return new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.Read, BUF,
			FileOptions.DeleteOnClose);
	}
}
