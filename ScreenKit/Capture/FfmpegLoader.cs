using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace ScreenKit;

/// <summary>加载 ffmpeg64 共享库（与 FFmpeg.AutoGen 配套）。</summary>
static class FfmpegLoader {
	static bool ready;
	static string root;

	public static string DllRoot => root;
	public static bool IsReady => ready;

	public static bool TryInit(out string error) {
		error = null;
		if (ready) return true;
		try {
			root = findroot();
			if (string.IsNullOrEmpty(root)) {
				error = "未找到 ffmpeg64（请将 FFmpeg shared DLL 放到程序目录 ffmpeg64/）";
				return false;
			}
			ffmpeg.RootPath = root;
			// 触发一次加载
			var ver = ffmpeg.av_version_info();
			ready = true;
			CaptureLog.Info($"FFmpeg loaded root={root} ver={ver}");
			return true;
		}
		catch (Exception ex) {
			error = ex.Message;
			ready = false;
			return false;
		}
	}

	static string findroot() {
		// 仅程序目录固定文件夹 ffmpeg64
		var cands = new[] {
			Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg64"),
		};
		foreach (var d in cands) {
			if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) continue;
			// 任意 avcodec-*.dll
			try {
				if (Directory.EnumerateFiles(d, "avcodec-*.dll").Any())
					return Path.GetFullPath(d);
			}
			catch { }
		}
		return null;
	}
}
