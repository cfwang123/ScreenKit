namespace ScreenKit;

/// <summary>
/// 录屏 codec 短测：走 ScreenRecorder，探测写出文件的视频编码。
/// x264/x265/AV1 用 FFmpeg；mf 用系统 H.264；mjpeg 用 MJPEG AVI。
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
		if (!shortsideok(log))
			bad++;

		if (want != "mf" && want != "mjpeg") {
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
		}
		else log($"skip find_encoder codec={want}");

		var region = parseregion(regionArg);
		log($"region={region.X},{region.Y} {region.Width}x{region.Height}");

		for (var i = 1; i <= repeat; i++) {
			log($"--- run {i}/{repeat} ---");
			if (!onerun(want, region, seconds, outDir, i, log))
				bad++;
		}
		if (want == "mf" && !liveaac(region, outDir, log))
			bad++;

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
				: want == "mf"
					? new[] { "mf", "mediafoundation", "h264_mf" }
					: want == "mjpeg"
						? new[] { "mjpeg", "mjpg", "mjpegavi" }
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

	static bool shortsideok(Action<string> log) {
		var o = new RecordOptions { MaxSizeEnabled = true, ShortPx = 100, Codec = "mf" };
		o.Clamp();
		o.FitSize(200, 400, out var w, out var h);
		if (w != 100 || h != 200) {
			log($"FAIL 短边缩小 {w}x{h} 期望 100x200");
			return false;
		}
		o.FitSize(80, 40, out w, out h);
		if (w != 80 || h != 40) {
			log($"FAIL 短边未超不应放大 {w}x{h}");
			return false;
		}
		o.MaxSizeEnabled = false;
		o.FitSize(200, 400, out w, out h);
		if (w != 200 || h != 400) {
			log($"FAIL 关闭短边限制仍缩放 {w}x{h}");
			return false;
		}
		log("短边限制：超过才缩小，不放大");
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
			var ext = opt.FileExt;
			var dest = Path.Combine(outDir, $"rec_{want}_{idx}_{DateTime.Now:HHmmss}{ext}");
			File.Copy(src, dest, true);
			var probed = probe(want, dest);
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

	/// <summary>系统 H.264 在录制时写入 AAC，不再于结束时合成第二份文件。</summary>
	static bool liveaac(System.Drawing.Rectangle region, string outDir, Action<string> log) {
		if (MfH264Writer.AacHz(22050) != 44100 || MfH264Writer.AacHz(44100) != 44100
			|| MfH264Writer.AacHz(8000) != 44100 || MfH264Writer.AacHz(48000) != 48000
			|| MfH264Writer.AacHz(50000) != 48000) {
			log("FAIL AacHz");
			return false;
		}
		var direct = Path.Combine(outDir, "live_aac.mp4");
		try { if (File.Exists(direct)) File.Delete(direct); } catch { }
		Exception writeEx = null;
		var th = new Thread(() => {
			try {
				var opt = new RecordOptions {
					Codec = "mf", Fps = 10, AudioHz = 22050, AudioMono = true, AudioKbps = 96,
				};
				opt.Clamp();
				using var w = new MfH264Writer(direct, 320, 180, opt, true);
				if (w.AudioRate != 44100)
					throw new InvalidOperationException("AudioRate=" + w.AudioRate);
				var frame = new byte[320 * 180 * 4];
				var stride = 320 * 4;
				for (var i = 0; i < 4; i++)
					w.WriteBgra(frame, stride, i);
				var bytes = w.AudioRate / 5 * w.AudioChannels * 2;
				w.WritePcm(new byte[bytes], bytes);
				if (!w.WroteAudio)
					throw new InvalidOperationException("没有写入 PCM");
			}
			catch (Exception ex) { writeEx = ex; }
		});
		th.IsBackground = true;
		th.SetApartmentState(ApartmentState.MTA);
		th.Start();
		if (!th.Join(20000)) {
			log("FAIL 直接写入 AAC 超时");
			return false;
		}
		if (writeEx != null) {
			log("FAIL 直接写入 AAC: " + writeEx.Message);
			RecordLog.Ex("liveaac.write", writeEx);
			return false;
		}
		if (!hasaac(direct, log, "live writer")) return false;

		ScreenRecorder rec = null;
		string copied = null;
		try {
			var opt = new RecordOptions {
				Codec = "mf", Fps = 10, AudioHz = 22050, AudioMono = false, AudioKbps = 96, AudioEnabled = true,
			};
			opt.Clamp();
			rec = new ScreenRecorder(region, RecordAudioMode.Speakers, opt);
			rec.Start();
			log($"live backend={rec.Backend}");
			if (rec.Backend == null || rec.Backend.IndexOf("44100", StringComparison.Ordinal) < 0) {
				log("FAIL 录制采样率未改成 44100: " + rec.Backend);
				return false;
			}
			Thread.Sleep(1000);
			rec.Stop();
			rec.WaitFinalize(15000);
			if (!string.IsNullOrEmpty(rec.AudioError)) {
				log("FAIL 录屏声音: " + rec.AudioError);
				return false;
			}
			if (!rec.HasAudio) {
				log("FAIL 录屏没有写入声音");
				return false;
			}
			var src = rec.TempPath ?? "";
			if (src.IndexOf("_av", StringComparison.OrdinalIgnoreCase) >= 0) {
				log("FAIL 仍生成了合成文件 " + src);
				return false;
			}
			copied = Path.Combine(outDir, "live_spk_" + DateTime.Now.ToString("HHmmss") + ".mp4");
			File.Copy(src, copied, true);
			if (!hasaac(copied, log, "live speakers")) return false;
			log("live aac OK（录制时 44100，无第二次合成）");
			return true;
		}
		catch (Exception ex) {
			log("FAIL live speakers: " + ex.Message);
			RecordLog.Ex("liveaac.speakers", ex);
			return false;
		}
		finally {
			try { rec?.DiscardTemps(); } catch { }
			try { rec?.Dispose(); } catch { }
		}
	}

	static bool hasaac(string path, Action<string> log, string label) {
		try {
			if (!MfH264Writer.LooksLikeH264(path)) {
				log($"FAIL {label} 不是 H.264");
				return false;
			}
			var n = (int)Math.Min(new FileInfo(path).Length, 1024 * 1024);
			var buf = new byte[n];
			using (var fs = File.OpenRead(path))
				if (fs.Read(buf, 0, n) < 12) {
					log($"FAIL {label} 读文件失败");
					return false;
				}
			var s = System.Text.Encoding.ASCII.GetString(buf);
			var ok = s.Contains("mp4a") || s.Contains("aac ");
			log(ok
				? $"{label} OK bytes={new FileInfo(path).Length}"
				: $"FAIL {label} 没有 AAC 音轨");
			return ok;
		}
		catch (Exception ex) {
			log($"FAIL {label}: " + ex.Message);
			return false;
		}
	}

	static string probe(string want, string dest) {
		if (want == "mf")
			return MfH264Writer.LooksLikeH264(dest) ? "h264" : "not-h264";
		if (want == "mjpeg")
			return looksmjpeg(dest) ? "mjpeg" : "not-mjpeg";
		return FfmpegMp4Writer.ProbeVideoCodec(dest);
	}

	static bool looksmjpeg(string path) {
		try {
			var n = (int)Math.Min(new FileInfo(path).Length, 4096);
			if (n < 32) return false;
			var buf = new byte[n];
			using (var fs = File.OpenRead(path))
				if (fs.Read(buf, 0, n) < 32) return false;
			var s = System.Text.Encoding.ASCII.GetString(buf);
			return s.StartsWith("RIFF") && s.Contains("AVI ") && s.Contains("MJPG");
		}
		catch { return false; }
	}

	static bool codecmatch(string want, string probed) {
		probed = (probed ?? "").Trim().ToLowerInvariant();
		if (want == "av1")
			return probed == "av1" || probed == "av01" || probed.Contains("av1");
		if (want == "x265")
			return probed == "hevc" || probed == "h265" || probed.Contains("hevc");
		if (want == "mjpeg")
			return probed == "mjpeg" || probed == "mjpg";
		if (want == "mf")
			return probed == "h264" || probed == "avc1";
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
