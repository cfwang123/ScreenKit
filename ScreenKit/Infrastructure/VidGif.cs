using System.Threading;

namespace ScreenKit;

/// <summary>把一段视频转成 GIF：起止时间、帧率、调色板、较短边。无声音。</summary>
static class VidGif {
	public const double MAXSEC = 60;

	public static int NormFps(int fps) {
		if (fps <= 0) fps = 8;
		return Compat.Clamp(fps, 1, 30);
	}

	/// <summary>调色板档：32 / 64 / 128 / 256，与 GIF 录屏相同。</summary>
	public static int NormColors(int n) {
		n = Compat.Clamp(n <= 0 ? 128 : n, 32, 256);
		if (n <= 48) return 32;
		if (n <= 96) return 64;
		if (n <= 192) return 128;
		return 256;
	}

	public static bool TryRange(double duration, double start, double end, out double s, out double e, out string err) {
		s = start < 0 ? 0 : start;
		e = end;
		err = "";
		if (duration > 0 && e > duration) e = duration;
		if (e <= s + 0.05) {
			err = "bad";
			return false;
		}
		if (e - s > MAXSEC + 0.05) {
			err = "long";
			return false;
		}
		return true;
	}

	public static void Convert(string src, string gif, int outW, int outH, int fps, int colors,
		double start, double end, CancellationToken ct) {
		if (!FfmpegGifEncode.TryProbe(src, out _, out _, out _, out var srcFps, out var err))
			throw new InvalidOperationException(string.IsNullOrEmpty(err) ? Loc.T("vidgif.noffmpeg") : err);
		var srcI = (int)Math.Round(srcFps);
		if (srcI < 1) srcI = 24;
		srcI = Compat.Clamp(srcI, 1, 60);
		var outFps = Compat.Clamp(NormFps(fps), 1, srcI);
		FfmpegGifEncode.FromVideo(src, gif, Math.Max(16, outW), Math.Max(16, outH), outFps,
			NormColors(colors), srcI, start, end, ct);
	}
}
