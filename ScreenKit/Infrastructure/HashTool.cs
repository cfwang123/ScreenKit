using System.Security.Cryptography;
using System.Text;

namespace ScreenKit;

sealed class HashRow {
	public string Path;
	public string Name;
	public long Size;
	public string Md5 = "";
	public string Sha1 = "";
	public string Sha256 = "";
	public string Status = "";
}

/// <summary>文件 MD5 / SHA-1 / SHA-256。</summary>
static class HashTool {
	public static HashRow Compute(string path, CancellationToken token) {
		var fi = new FileInfo(path);
		var row = new HashRow {
			Path = path,
			Name = fi.Name,
			Size = fi.Exists ? fi.Length : 0,
		};
		if (!fi.Exists) {
			row.Status = "missing";
			return row;
		}
		using var md5 = MD5.Create();
		using var sha1 = SHA1.Create();
		using var sha256 = SHA256.Create();
		using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 64);
		var buf = new byte[1024 * 64];
		while (true) {
			token.ThrowIfCancellationRequested();
			var n = fs.Read(buf, 0, buf.Length);
			if (n <= 0) break;
			md5.TransformBlock(buf, 0, n, null, 0);
			sha1.TransformBlock(buf, 0, n, null, 0);
			sha256.TransformBlock(buf, 0, n, null, 0);
		}
		md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
		sha1.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
		sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
		row.Md5 = hex(md5.Hash);
		row.Sha1 = hex(sha1.Hash);
		row.Sha256 = hex(sha256.Hash);
		row.Status = "ok";
		return row;
	}

	public static string MatchKind(HashRow row, string expect) {
		var e = NormHex(expect);
		if (e.Length == 0 || row == null) return "";
		if (e == NormHex(row.Sha256)) return "SHA-256";
		if (e == NormHex(row.Sha1)) return "SHA-1";
		if (e == NormHex(row.Md5)) return "MD5";
		return "";
	}

	public static string NormHex(string s) {
		if (string.IsNullOrWhiteSpace(s)) return "";
		var sb = new StringBuilder(s.Length);
		foreach (var ch in s) {
			if (Uri.IsHexDigit(ch))
				sb.Append(char.ToLowerInvariant(ch));
		}
		return sb.ToString();
	}

	static string hex(byte[] bytes) {
		if (bytes == null || bytes.Length == 0) return "";
		var sb = new StringBuilder(bytes.Length * 2);
		foreach (var b in bytes)
			sb.Append(b.ToString("x2"));
		return sb.ToString();
	}

	public static string FormatSize(long n) {
		if (n < 1024) return n + " B";
		double v = n;
		var u = new[] { "B", "KiB", "MiB", "GiB", "TiB" };
		var i = 0;
		while (v >= 1024 && i < u.Length - 1) {
			v /= 1024;
			i++;
		}
		return $"{v:0.##} {u[i]}";
	}
}
