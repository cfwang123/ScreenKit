namespace ScreenKit;

sealed class CastQuality {
	public string Name;
	public int Width, Height, Fps, Bitrate, Crf;

	public static readonly CastQuality[] Presets = {
		new() { Name = "流畅 540p", Width = 960, Height = 540, Fps = 15, Bitrate = 1_200_000, Crf = 28 },
		new() { Name = "均衡 720p", Width = 1280, Height = 720, Fps = 24, Bitrate = 2_500_000, Crf = 23 },
		new() { Name = "高清 1080p", Width = 1920, Height = 1080, Fps = 30, Bitrate = 4_000_000, Crf = 20 },
	};

	public static CastQuality ByName(string name) {
		foreach (var q in Presets)
			if (q.Name == name) return q;
		return Presets[1];
	}

	public void Fit(int srcW, int srcH, out int outW, out int outH) {
		if (srcW <= 0 || srcH <= 0) { outW = Width; outH = Height; return; }
		var cap = Math.Min(Width, Height);
		var srcMin = Math.Min(srcW, srcH);
		var s = Math.Min(1, cap / (double)srcMin);
		outW = Math.Max(16, ((int)(srcW * s) / 16) * 16);
		outH = Math.Max(16, ((int)(srcH * s) / 16) * 16);
	}
}
