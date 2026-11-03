using System;
using System.IO;
using System.Text;

namespace ScreenKit.DocView;

/// <summary>
/// 文本文件读写：共享打开 + BOM/UTF-8/GB18030 探测，保存时尽量沿用原编码。
/// </summary>
static class TextFileIo {
	static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
	static readonly Encoding Utf8Bom = new UTF8Encoding(true);
	static Encoding gb;

	static Encoding Gb {
		get {
			if (gb != null) return gb;
			try { gb = Encoding.GetEncoding("GB18030"); }
			catch {
				try { gb = Encoding.GetEncoding(936); }
				catch { gb = Encoding.Default; }
			}
			return gb;
		}
	}

	public sealed class LoadResult {
		public string Text;
		public Encoding Encoding;
		public bool HasBom;
	}

	public static LoadResult Load(string path) {
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			throw new FileNotFoundException("文件不存在", path);
		path = Path.GetFullPath(path);
		byte[] bytes;
		using (var fs = DocFileIo.OpenReadShared(path))
		using (var ms = new MemoryStream()) {
			fs.CopyTo(ms);
			bytes = ms.ToArray();
		}
		return Decode(bytes);
	}

	/// <summary>供单测：对原始字节做与 Load 相同的编码探测。</summary>
	public static LoadResult Decode(byte[] bytes) {
		if (bytes == null) bytes = Array.Empty<byte>();
		// UTF-8 BOM
		if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) {
			return new LoadResult {
				Text = Utf8Bom.GetString(bytes, 3, bytes.Length - 3),
				Encoding = Utf8Bom,
				HasBom = true,
			};
		}
		// UTF-16 LE BOM
		if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) {
			var enc = Encoding.Unicode;
			return new LoadResult {
				Text = enc.GetString(bytes, 2, bytes.Length - 2),
				Encoding = enc,
				HasBom = true,
			};
		}
		// UTF-16 BE BOM
		if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) {
			var enc = Encoding.BigEndianUnicode;
			return new LoadResult {
				Text = enc.GetString(bytes, 2, bytes.Length - 2),
				Encoding = enc,
				HasBom = true,
			};
		}
		// 无 BOM：优先合法 UTF-8，否则 GB18030
		if (IsValidUtf8(bytes)) {
			return new LoadResult {
				Text = Utf8NoBom.GetString(bytes),
				Encoding = Utf8NoBom,
				HasBom = false,
			};
		}
		return new LoadResult {
			Text = Gb.GetString(bytes),
			Encoding = Gb,
			HasBom = false,
		};
	}

	/// <summary>状态栏/菜单用编码显示名。</summary>
	public static string DisplayName(Encoding enc) {
		if (enc == null) return "UTF-8";
		try {
			if (enc is UTF8Encoding u8)
				return u8.GetPreamble().Length > 0 ? "UTF-8 BOM" : "UTF-8";
			if (enc.CodePage == 1200) return "UTF-16 LE";
			if (enc.CodePage == 1201) return "UTF-16 BE";
			if (enc.CodePage == 936 || string.Equals(enc.WebName, "gb2312", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(enc.WebName, "gbk", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(enc.WebName, "gb18030", StringComparison.OrdinalIgnoreCase))
				return "GB18030";
			return enc.WebName?.ToUpperInvariant() ?? enc.EncodingName ?? "UTF-8";
		} catch {
			return "UTF-8";
		}
	}

	/// <summary>常用编码列表（切换用）。</summary>
	public static Encoding[] CommonEncodings() {
		return new[] {
			Utf8NoBom,
			Utf8Bom,
			Gb,
			Encoding.Unicode,
			Encoding.BigEndianUnicode,
			Encoding.ASCII,
		};
	}

	/// <summary>按指定编码重读磁盘（不探测）。</summary>
	public static LoadResult LoadWithEncoding(string path, Encoding encoding) {
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			throw new FileNotFoundException("文件不存在", path);
		path = Path.GetFullPath(path);
		var enc = encoding ?? Utf8NoBom;
		byte[] bytes;
		using (var fs = DocFileIo.OpenReadShared(path))
		using (var ms = new MemoryStream()) {
			fs.CopyTo(ms);
			bytes = ms.ToArray();
		}
		var pre = enc.GetPreamble();
		var start = 0;
		var hasBom = false;
		if (pre != null && pre.Length > 0 && bytes.Length >= pre.Length) {
			var ok = true;
			for (var i = 0; i < pre.Length; i++)
				if (bytes[i] != pre[i]) { ok = false; break; }
			if (ok) { start = pre.Length; hasBom = true; }
		}
		return new LoadResult {
			Text = enc.GetString(bytes, start, bytes.Length - start),
			Encoding = enc,
			HasBom = hasBom,
		};
	}

	public static void Save(string path, string text, Encoding encoding) {
		if (string.IsNullOrWhiteSpace(path))
			throw new ArgumentException("路径为空", nameof(path));
		path = Path.GetFullPath(path);
		var enc = encoding ?? Utf8NoBom;
		var dir = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			Directory.CreateDirectory(dir);
		// 先写临时再替换，避免写到一半损坏
		var tmp = path + ".~tmp";
		var bytes = enc.GetBytes(text ?? "");
		// 带 BOM 的编码（UTF-8 BOM / UTF-16）GetPreamble
		var pre = enc.GetPreamble();
		using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None)) {
			if (pre != null && pre.Length > 0)
				fs.Write(pre, 0, pre.Length);
			fs.Write(bytes, 0, bytes.Length);
		}
		if (File.Exists(path)) {
			try { File.Replace(tmp, path, null); }
			catch {
				File.Copy(tmp, path, true);
				try { File.Delete(tmp); } catch { /* ignore */ }
			}
		} else {
			File.Move(tmp, path);
		}
	}

	static bool IsValidUtf8(byte[] data) {
		if (data == null || data.Length == 0) return true;
		var i = 0;
		while (i < data.Length) {
			var b = data[i];
			if (b <= 0x7F) { i++; continue; }
			int need;
			if ((b & 0xE0) == 0xC0) need = 1;
			else if ((b & 0xF0) == 0xE0) need = 2;
			else if ((b & 0xF8) == 0xF0) need = 3;
			else return false;
			if (i + need >= data.Length) return false;
			for (var k = 1; k <= need; k++) {
				if ((data[i + k] & 0xC0) != 0x80) return false;
			}
			// overlong / 非法码点粗检
			if (need == 1 && b < 0xC2) return false;
			if (need == 2 && b == 0xE0 && data[i + 1] < 0xA0) return false;
			if (need == 3 && b == 0xF0 && data[i + 1] < 0x90) return false;
			if (need == 3 && b > 0xF4) return false;
			i += 1 + need;
		}
		// 纯 ASCII 与合法多字节序列均视为 UTF-8
		return true;
	}
}
