using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace ScreenKit;

static class CastFf {
	public static void Check(this int err, string what) {
		if (err < 0) throw new InvalidOperationException($"{what}: {errstr(err)}");
	}

	public static unsafe string errstr(int err) {
		var buf = stackalloc byte[256];
		ffmpeg.av_strerror(err, buf, 256);
		return Marshal.PtrToStringAnsi((IntPtr)buf) ?? err.ToString();
	}
}
