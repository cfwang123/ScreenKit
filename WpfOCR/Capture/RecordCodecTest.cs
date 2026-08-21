namespace WpfOCR;

/// <summary>
/// 录屏 codec 短测：走 ScreenRecorder + FfmpegMp4Writer，探测写出文件的视频编码。
/// </summary>
static class RecordCodecTest {
	public static int Run(string codecArg, string outDir, string regionArg, int seconds, int repeat,
		Action<string> log) {
		if (log == null) log = _ => { };
		if (seconds < 1) seconds = 1;
		if (seconds > 30) seconds = 30;
		if (repeat < 1) repeat = 1;
		if (repeat > 8) repeat = 8;
		if (string.IsNullOrWhiteSpace(outDir))
			outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log", "record_codec");
		outDir = Path.GetFullPath(outDir);
		Directory.CreateDirectory(outDir);

		AppConfig.applylogswitch(true);
		CaptureLog.SessionStart("CLI --test-record-codec");
		RecordLog.Begin("CLI-record-codec");

		var want = RecordOptions.NormalizeCodec(codecArg);
		log($"=== 录屏编码测试 --test-record-codec {want} ===");
		log($"seconds={seconds} repeat={repeat} out={outDir}");

		var bad = 0;
		if (!clampkeeps(want, log))
			bad++;

		try {
			var found = FfmpegMp4Writer.FindEncoderName(new RecordOptions { Codec = want });
			log($"find_encoder selected={want} opened={found}");
		}
		catch (Exception ex) {
			log("FAIL find_encoder: " + ex.Message);
			RecordLog.Ex("FindEncoderName", ex);
			RecordLog.End("fail");
			return 1;
		}

		var region = parseregion(regionArg);
		log($"region={region.X},{region.Y} {region.Width}x{region.Height}");

		for (var i = 1; i <= repeat; i++) {
			log($"--- run {i}/{repeat} ---");
			if (!onerun(want, region, seconds, outDir, i, log))
				bad++;
		}

		RecordLog.End(bad == 0 ? "ok" : "fail");
		if (bad == 0)
			log($"=== OK：{want} 写出 {repeat} 次，探测 codec 匹配 ===");
		else
			log($"=== FAIL：{want} 失败 {bad} 项 ===");
		return bad == 0 ? 0 : 1;
	}

	static bool clampkeeps(string want, Action<string> log) {
		var aliases = want == "av1"
			? new[] { "av1", "AV1", "av01", "libaom-av1" }
			: want == "x265"
				? new[] { "x265", "hevc", "h265" }
				: new[] { "x264", "h264" };
		foreach (var a in aliases) {
			var o = new RecordOptions { Codec = a };
			o.Clamp();
			if (!string.Equals(o.Codec, want, StringComparison.Ordinal)) {
				log($"FAIL Clamp({a}) -> {o.Codec} 期望 {want}");
				return false;
			}
		}
		log($"Clamp 保持 {want}（别名 {string.Join("/", aliases)}）");
		return true;
	}

	static bool onerun(string want, System.Drawing.Rectangle region, int seconds, string outDir, int idx,
		Action<string> log) {
		var opt = new RecordOptions {
			Codec = want,
			Fps = 10,
			Crf = 36,
			AudioEnabled = false,
		};
		opt.Clamp();
		if (!string.Equals(opt.Codec, want, StringComparison.Ordinal)) {
			log($"FAIL 选项被改写: {opt.Codec}");
			return false;
		}

		ScreenRecorder rec = null;
		try {
			rec = new ScreenRecorder(region, RecordAudioMode.Off, opt);
			rec.Start();
			log($"recording backend={rec.Backend}");
			if (string.IsNullOrEmpty(rec.Backend) || rec.Backend.IndexOf(want, StringComparison.OrdinalIgnoreCase) < 0) {
				log($"FAIL backend 未包含所选 codec: {rec.Backend}");
				return false;
			}
			Thread.Sleep(seconds * 1000);
			rec.Stop();
			rec.WaitFinalize(60_000);
			var src = rec.TempPath;
			if (string.IsNullOrEmpty(src) || !File.Exists(src)) {
				log("FAIL 未写出临时文件");
				return false;
			}
			var dest = Path.Combine(outDir, $"rec_{want}_{idx}_{DateTime.Now:HHmmss}.mp4");
			File.Copy(src, dest, true);
			var probed = FfmpegMp4Writer.ProbeVideoCodec(dest);
			var sz = new FileInfo(dest).Length;
			log($"wrote {dest} bytes={sz} probe={probed} selected={want}");
			if (!codecmatch(want, probed)) {
				log($"FAIL 探测 codec={probed} 不是 {want}");
				return false;
			}
			if (sz < 64) {
				log("FAIL 文件过小");
				return false;
			}
			return true;
		}
		catch (Exception ex) {
			log("FAIL encode: " + ex.Message);
			RecordLog.Ex("RecordCodecTest.onerun", ex);
			return false;
		}
		finally {
			try { rec?.DiscardTemps(); } catch { }
			try { rec?.Dispose(); } catch { }
		}
	}

	static bool codecmatch(string want, string probed) {
		probed = (probed ?? "").Trim().ToLowerInvariant();
		if (want == "av1")
			return probed == "av1" || probed == "av01" || probed.Contains("av1");
		if (want == "x265")
			return probed == "hevc" || probed == "h265" || probed.Contains("hevc");
		return probed == "h264" || probed == "avc" || probed.Contains("264");
	}

	static System.Drawing.Rectangle parseregion(string regionArg) {
		if (!string.IsNullOrWhiteSpace(regionArg)) {
			var parts = regionArg.Split(new[] { ',', 'x', 'X', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length >= 4) {
				var r = new System.Drawing.Rectangle(
					int.Parse(parts[0]), int.Parse(parts[1]),
					int.Parse(parts[2]), int.Parse(parts[3]));
				if (r.Width % 2 != 0) r.Width--;
				if (r.Height % 2 != 0) r.Height--;
				if (r.Width >= 16 && r.Height >= 16) return r;
			}
		}
		var s = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
		var w = Math.Min(320, s.Width / 2 * 2);
		var h = Math.Min(180, s.Height / 2 * 2);
		if (w < 16) w = 16;
		if (h < 16) h = 16;
		return new System.Drawing.Rectangle(
			s.Left + (s.Width - w) / 2, s.Top + (s.Height - h) / 2, w, h);
	}
}
