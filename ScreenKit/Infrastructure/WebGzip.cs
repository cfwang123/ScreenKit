using System.Globalization;
using System.IO;
using System.IO.Compression;

namespace ScreenKit;

/// <summary>网页和接口响应的 gzip：只压文本，客户端接受才压，太小的不压。</summary>
static class WebGzip {
	/// <summary>小于这么多字节不压，省不下什么还可能更大。</summary>
	public const int MINSIZE = 512;

	/// <summary>缓存条数上限，满了整张表清掉重来。</summary>
	const int CACHEMAX = 256;

	static readonly object cachelock = new();
	static readonly Dictionary<string, byte[]> cache = new(StringComparer.Ordinal);

	/// <summary>Accept-Encoding 里是否接受 gzip。显式 gzip;q=0 就不接受。</summary>
	public static bool Accepts(string accept) {
		if (string.IsNullOrWhiteSpace(accept)) return false;
		var gzip = -1;
		var star = -1;
		foreach (var part in accept.Split(',')) {
			var seg = part.Trim();
			if (seg.Length == 0) continue;
			var semi = seg.IndexOf(';');
			var name = (semi >= 0 ? seg.Substring(0, semi) : seg).Trim();
			var ok = semi < 0 || quality(seg.Substring(semi + 1)) > 0;
			if (name.Equals("gzip", StringComparison.OrdinalIgnoreCase)) gzip = ok ? 1 : 0;
			else if (name is "*") star = ok ? 1 : 0;
		}
		return gzip >= 0 ? gzip == 1 : star == 1;
	}

	static double quality(string s) {
		var i = s.IndexOf("q=", StringComparison.OrdinalIgnoreCase);
		if (i < 0) return 1.0;
		var v = s.Substring(i + 2).Trim();
		var semi = v.IndexOf(';');
		if (semi >= 0) v = v.Substring(0, semi).Trim();
		return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 1.0;
	}

	/// <summary>这种内容压了有收益，图片字体视频本来就不用压。</summary>
	public static bool Compressible(string contentType) {
		var ct = (contentType ?? "").Trim().ToLowerInvariant();
		var semi = ct.IndexOf(';');
		if (semi >= 0) ct = ct.Substring(0, semi).Trim();
		if (ct.Length == 0) return false;
		if (ct.StartsWith("text/", StringComparison.Ordinal)) return true;
		return ct is "application/json" or "application/javascript" or "application/x-javascript"
			or "application/xml" or "application/xhtml+xml" or "image/svg+xml" or "application/manifest+json";
	}

	/// <summary>该压就压，不够大或者类型不该压返回 null。</summary>
	public static byte[] Maybe(string contentType, byte[] data) {
		if (data == null || data.Length < MINSIZE || !Compressible(contentType)) return null;
		return Compress(data);
	}

	/// <summary>同一份内容只压一次。key 里要带上内容版本，内容是内存里的就带上 <see cref="Stamp"/>。</summary>
	public static byte[] Cached(string key, string contentType, byte[] data) {
		if (data == null || data.Length < MINSIZE || !Compressible(contentType)) return null;
		if (!string.IsNullOrEmpty(key)) {
			lock (cachelock)
				if (cache.TryGetValue(key, out var hit)) return hit;
		}
		var gz = Compress(data);
		if (!string.IsNullOrEmpty(key)) {
			lock (cachelock) {
				if (cache.Count > CACHEMAX) cache.Clear();
				cache[key] = gz;
			}
		}
		return gz;
	}

	/// <summary>内存内容的版本号：长度加 FNV-1a，快而且不依赖文件时间。</summary>
	public static string Stamp(byte[] data) {
		if (data == null || data.Length == 0) return "0";
		ulong h = 14695981039346656037UL;
		foreach (var b in data) {
			h ^= b;
			h *= 1099511628211UL;
		}
		return data.Length.ToString(CultureInfo.InvariantCulture) + "-" + h.ToString("x16", CultureInfo.InvariantCulture);
	}

	public static byte[] Compress(byte[] data) {
		if (data == null || data.Length == 0) return data;
		using var ms = new MemoryStream(data.Length / 2 + 256);
		using (var gz = new GZipStream(ms, CompressionMode.Compress, true))
			gz.Write(data, 0, data.Length);
		return ms.ToArray();
	}
}
