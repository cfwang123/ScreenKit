using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using PDFtoImage;
using SkiaSharp;
using PdfRenderOptions = PDFtoImage.RenderOptions;

namespace ScreenKit.DocView;

/// <summary>
/// PDFtoImage 封装：
/// 1) string 重载是 Base64，不是路径 → 用 byte[] / Stream
/// 2) pdfium 非线程安全 → 全局串行
/// </summary>
static class PdfIo {
	/// <summary>所有 pdfium / PDFtoImage 调用必须持有此锁（含大纲 P/Invoke）。</summary>
	public static readonly object Gate = new();

	const long MAX_CACHE_BYTES = 120L * 1024 * 1024; // 超过则每次按路径打开流

	public static byte[] TryLoadBytes(string path) {
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			throw new FileNotFoundException("PDF 不存在", path);
		var len = new FileInfo(path).Length;
		DocViewLog.Info($"PdfIo.TryLoadBytes path={path} size={len}");
		if (len <= 0)
			throw new InvalidOperationException("PDF 文件为空");
		if (len > MAX_CACHE_BYTES) {
			DocViewLog.Info($"PdfIo.TryLoadBytes skip cache size>{MAX_CACHE_BYTES}");
			return null;
		}
		// 共享读，避免其它程序占用时失败
		byte[] bytes;
		using (var fs = open(path)) {
			using var ms = new MemoryStream((int)Math.Min(len, int.MaxValue));
			fs.CopyTo(ms);
			bytes = ms.ToArray();
		}
		if (bytes.Length < 5 || bytes[0] != (byte)'%' || bytes[1] != (byte)'P' || bytes[2] != (byte)'D' || bytes[3] != (byte)'F')
			DocViewLog.Warn($"PdfIo.TryLoadBytes magic unexpected: {(bytes.Length > 0 ? bytes[0].ToString("X2") : "empty")}");
		return bytes;
	}

	public static IList<SizeF> GetPageSizes(byte[] data) {
		if (data == null || data.Length == 0)
			throw new ArgumentException("PDF 数据为空");
		lock (Gate) {
			var t0 = Environment.TickCount;
			var sizes = Conversion.GetPageSizes(data, password: null);
			DocViewLog.Info($"PdfIo.GetPageSizes(bytes) pages={sizes?.Count ?? 0} cost={Environment.TickCount - t0}ms");
			return sizes;
		}
	}

	public static IList<SizeF> GetPageSizes(string path) {
		var data = TryLoadBytes(path);
		if (data != null)
			return GetPageSizes(data);
		lock (Gate) {
			using var fs = open(path);
			var sizes = Conversion.GetPageSizes(fs, leaveOpen: true, password: null);
			DocViewLog.Info($"PdfIo.GetPageSizes(stream) pages={sizes?.Count ?? 0}");
			return sizes;
		}
	}

	public static SKBitmap ToImage(byte[] data, int page, PdfRenderOptions options) {
		if (data == null) throw new ArgumentNullException(nameof(data));
		lock (Gate) {
			return Conversion.ToImage(data, page: page, password: null, options: options);
		}
	}

	public static SKBitmap ToImage(string path, int page, PdfRenderOptions options) {
		lock (Gate) {
			using var fs = open(path);
			return Conversion.ToImage(fs, page: page, leaveOpen: true, password: null, options: options);
		}
	}

	/// <summary>在 pdfium 锁内执行（大纲等原生调用）。</summary>
	public static T WithLock<T>(Func<T> fn) {
		lock (Gate) return fn();
	}

	public static void WithLock(Action fn) {
		lock (Gate) fn();
	}

	static FileStream open(string path) {
		if (string.IsNullOrWhiteSpace(path))
			throw new ArgumentException("路径为空", nameof(path));
		if (!File.Exists(path))
			throw new FileNotFoundException("PDF 不存在", path);
		return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1024 * 64);
	}
}
