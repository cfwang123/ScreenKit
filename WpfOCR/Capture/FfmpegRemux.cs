using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace WpfOCR;

/// <summary>
/// 无声 MP4 + WAV → 带音轨 MP4。
/// 音轨：MPEG Audio Layer 3（MP3 / 0x55），采样率与 WAV 一致（即录屏参数 AudioHz）。
/// 优先 FFmpeg.AutoGen（DLL）；其次本机可用的 ffmpeg.exe。
/// </summary>
static unsafe class FfmpegRemux {
	/// <param name="audioKbps">MP3 码率 kbps，8~128。</param>
	/// <param name="mono">true=单声道编码（低码率更清晰）。</param>
	/// <param name="audioHz">目标采样率；&gt;0 时强制重采样（跳过录制侧规范化时必传）。</param>
	public static string MergeVideoAudio(string videoPath, string wavPath, string outPath,
		int audioKbps, bool mono, out string error, int audioHz = 0) {
		error = null;
		audioKbps = Compat.Clamp(audioKbps, 8, 128);
		if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
			throw new FileNotFoundException("视频不存在", videoPath);

		var vsz = new FileInfo(videoPath).Length;
		diaglog($"begin v={vsz} wav={(File.Exists(wavPath) ? new FileInfo(wavPath).Length : -1)} mono={mono} {audioKbps}k hz={audioHz}");

		if (string.IsNullOrEmpty(wavPath) || !File.Exists(wavPath) || new FileInfo(wavPath).Length < 100) {
			CaptureLog.Info("FfmpegRemux: wav missing/empty, video only");
			error = "wav 缺失或过小";
			File.Copy(videoPath, outPath, true);
			return outPath;
		}

		// 优先 ffmpeg.exe：长文件多线程更快；DLL 作回退
		var exe = findffmpeg();
		if (exe != null) {
			try {
				var ac = mono ? 1 : 2;
				var ar = audioHz > 0 ? $"-ar {audioHz} " : "";
				var args =
					$"-y -i \"{videoPath}\" -i \"{wavPath}\" -map 0:v:0 -map 1:a:0 " +
					$"-c:v copy -c:a libmp3lame -b:a {audioKbps}k -ac {ac} {ar}" +
					$"-threads 0 -shortest \"{outPath}\"";
				diaglog("exe args: " + args);
				var psi = new ProcessStartInfo {
					FileName = exe,
					Arguments = args,
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardError = true,
					RedirectStandardOutput = true,
				};
				using var p = Process.Start(psi);
				if (p != null) {
					var err = p.StandardError.ReadToEnd();
					p.WaitForExit(600_000);
					if (p.ExitCode == 0 && File.Exists(outPath) && new FileInfo(outPath).Length > 1000
						&& HasAudioStream(outPath)) {
						CaptureLog.Info("FfmpegRemux: exe merge ok (mp3)");
						diaglog($"exe merge ok mp3 size={new FileInfo(outPath).Length}");
						return outPath;
					}
					error = "ffmpeg.exe 合成失败 code=" + p.ExitCode;
					diaglog("exe merge fail code=" + p.ExitCode + " " + err);
					try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
				}
			}
			catch (Exception ex) {
				CaptureLog.Ex("FfmpegRemux exe", ex);
				error = "ffmpeg.exe 异常: " + ex.Message;
			}
		}
		else {
			diaglog("no usable ffmpeg.exe, try DLL");
		}

		// 回退：DLL 合成
		if (FfmpegLoader.TryInit(out _)) {
			try {
				mergeWithDll(videoPath, wavPath, outPath, audioKbps, mono, audioHz);
				if (File.Exists(outPath) && new FileInfo(outPath).Length > 1000 && HasAudioStream(outPath)) {
					CaptureLog.Info($"FfmpegRemux: DLL merge ok mono={mono} {audioKbps}k " + outPath);
					diaglog($"DLL merge ok size={new FileInfo(outPath).Length}");
					return outPath;
				}
				error = "DLL 合成输出无音轨";
				diaglog("DLL merge: output missing audio stream");
			}
			catch (Exception ex) {
				CaptureLog.Ex("FfmpegRemux DLL", ex);
				error = "DLL 合成异常: " + ex.Message;
				diaglog("DLL merge EX: " + ex);
				try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
			}
		}

		if (error == null) error = "合成失败，回退纯视频";
		diaglog("fallback video-only: " + error);
		File.Copy(videoPath, outPath, true);
		return outPath;
	}

	/// <summary>DLL：视频 copy + 音频解码 / MP3（Layer3）编码。</summary>
	static void mergeWithDll(string videoPath, string wavPath, string outPath, int audioKbps, bool mono,
		int audioHz = 0) {
		AVFormatContext* ifmtV = null;
		AVFormatContext* ifmtA = null;
		AVFormatContext* ofmt = null;
		AVCodecContext* adec = null;
		AVCodecContext* aenc = null;
		SwrContext* swr = null;
		AVAudioFifo* fifo = null;
		AVPacket* pkt = null;
		AVFrame* decFrame = null;
		AVFrame* convFrame = null;
		AVFrame* encFrame = null;

		try {
			ffmpeg.avformat_open_input(&ifmtV, videoPath, null, null).ThrowIfError("open v");
			ffmpeg.avformat_find_stream_info(ifmtV, null).ThrowIfError("v info");

			ffmpeg.avformat_open_input(&ifmtA, wavPath, null, null).ThrowIfError("open a");
			ffmpeg.avformat_find_stream_info(ifmtA, null).ThrowIfError("a info");

			ffmpeg.avformat_alloc_output_context2(&ofmt, null, "mp4", outPath).ThrowIfError("alloc o");

			int vIdx = -1, aIdx = -1;
			for (uint i = 0; i < ifmtV->nb_streams; i++) {
				if (ifmtV->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO) {
					vIdx = (int)i;
					break;
				}
			}
			for (uint i = 0; i < ifmtA->nb_streams; i++) {
				if (ifmtA->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO) {
					aIdx = (int)i;
					break;
				}
			}
			if (vIdx < 0) throw new InvalidOperationException("无视频流");
			if (aIdx < 0) throw new InvalidOperationException("无音频流");

			var inV = ifmtV->streams[vIdx];
			var inA = ifmtA->streams[aIdx];

			var outV = ffmpeg.avformat_new_stream(ofmt, null);
			ffmpeg.avcodec_parameters_copy(outV->codecpar, inV->codecpar).ThrowIfError("v copy");
			outV->codecpar->codec_tag = 0;
			outV->time_base = inV->time_base;

			// 解码 wav
			var dec = ffmpeg.avcodec_find_decoder(inA->codecpar->codec_id);
			if (dec == null) throw new InvalidOperationException("无音频解码器");
			adec = ffmpeg.avcodec_alloc_context3(dec);
			ffmpeg.avcodec_parameters_to_context(adec, inA->codecpar).ThrowIfError("par2dec");
			ffmpeg.avcodec_open2(adec, dec, null).ThrowIfError("open dec");

			// MP3（MPEG Audio Layer 3 / 0x55）：采样率与 WAV 一致（录屏 AudioHz）
			var enc = findMp3Encoder();
			if (enc == null)
				throw new InvalidOperationException(
					"当前 ffmpeg64 无 MP3 编码器（需 libmp3lame）。请换带 lame 的 FFmpeg shared 构建。");

			aenc = ffmpeg.avcodec_alloc_context3(enc);
			// 优先使用调用方目标采样率（跳过录制侧规范化时）；否则沿用 wav
			var sr = audioHz > 0 ? audioHz
				: (adec->sample_rate > 0 ? adec->sample_rate : 22050);
			aenc->sample_rate = sr;
			aenc->channels = mono ? 1 : 2;
			aenc->channel_layout = (ulong)(mono ? ffmpeg.AV_CH_LAYOUT_MONO : ffmpeg.AV_CH_LAYOUT_STEREO);
			var bitRate = Math.Max(8000L, audioKbps * 1000L);
			aenc->bit_rate = bitRate;
			aenc->time_base = new AVRational { num = 1, den = aenc->sample_rate };
			aenc->sample_fmt = pickSampleFmt(enc);
			// 部分构建要求
			aenc->strict_std_compliance = -2;

			if ((ofmt->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
				aenc->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

			ffmpeg.avcodec_open2(aenc, enc, null).ThrowIfError("open aenc mp3");
			// open 后 frame_size 一般为 1152
			diaglog($"aenc mp3 name={Marshal.PtrToStringAnsi((IntPtr)enc->name)} sr={aenc->sample_rate} " +
				$"ch={aenc->channels} fmt={aenc->sample_fmt} frame_size={aenc->frame_size} br={bitRate}");

			var outA = ffmpeg.avformat_new_stream(ofmt, null);
			ffmpeg.avcodec_parameters_from_context(outA->codecpar, aenc).ThrowIfError("a par");
			// MP4 里不要手写 0x55（会与 muxer 冲突）；codec_id=MP3 即可，播放器显示为 MPEG Layer 3
			outA->codecpar->codec_id = AVCodecID.AV_CODEC_ID_MP3;
			outA->codecpar->codec_tag = 0;
			outA->codecpar->bit_rate = bitRate;
			outA->codecpar->sample_rate = aenc->sample_rate;
			outA->codecpar->channels = aenc->channels;
			outA->time_base = aenc->time_base;

			// 重采样 → 编码器格式
			ulong inLayout = adec->channel_layout != 0
				? adec->channel_layout
				: (ulong)(adec->channels == 1 ? ffmpeg.AV_CH_LAYOUT_MONO : ffmpeg.AV_CH_LAYOUT_STEREO);
			swr = ffmpeg.swr_alloc_set_opts(null,
				(long)aenc->channel_layout, aenc->sample_fmt, aenc->sample_rate,
				(long)inLayout, adec->sample_fmt, adec->sample_rate,
				0, null);
			if (swr == null) throw new InvalidOperationException("swr_alloc 失败");
			ffmpeg.swr_init(swr).ThrowIfError("swr_init");

			var frameSize = aenc->frame_size > 0 ? aenc->frame_size : 1024;
			fifo = ffmpeg.av_audio_fifo_alloc(aenc->sample_fmt, aenc->channels, frameSize * 8);
			if (fifo == null) throw new InvalidOperationException("av_audio_fifo_alloc 失败");

			if ((ofmt->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0) {
				AVIOContext* io = null;
				ffmpeg.avio_open(&io, outPath, ffmpeg.AVIO_FLAG_WRITE).ThrowIfError("avio out");
				ofmt->pb = io;
			}
			ffmpeg.avformat_write_header(ofmt, null).ThrowIfError("hdr");

			pkt = ffmpeg.av_packet_alloc();
			decFrame = ffmpeg.av_frame_alloc();
			convFrame = ffmpeg.av_frame_alloc();
			encFrame = ffmpeg.av_frame_alloc();

		// 视频包 copy
		int vpkts = 0;
		while (ffmpeg.av_read_frame(ifmtV, pkt) >= 0) {
			if (pkt->stream_index == vIdx) {
				ffmpeg.av_packet_rescale_ts(pkt, inV->time_base, outV->time_base);
				pkt->stream_index = outV->index;
				ffmpeg.av_interleaved_write_frame(ofmt, pkt).ThrowIfError("write v");
				vpkts++;
			}
			ffmpeg.av_packet_unref(pkt);
		}
		diaglog($"video copy done: {vpkts} packets");
		// 视频流已读完：flush 交错缓冲，避免全部视频包滞留内存等待音频
		var fv = ffmpeg.av_interleaved_write_frame(ofmt, null);
		if (fv < 0) diaglog("video flush ret=" + fv);

			// 音频：解码 → 重采样 → FIFO 拼满 frame_size → 编码
			long pts = 0;
			int apkts = 0, aframes = 0;
			while (ffmpeg.av_read_frame(ifmtA, pkt) >= 0) {
				if (pkt->stream_index == aIdx) {
					apkts++;
					if (ffmpeg.avcodec_send_packet(adec, pkt) >= 0) {
						while (ffmpeg.avcodec_receive_frame(adec, decFrame) >= 0) {
							convertToFifo(swr, adec, aenc, decFrame, convFrame, fifo);
							pts = drainFifo(aenc, fifo, encFrame, frameSize, ofmt, outA, pts, pad: false);
							aframes++;
							ffmpeg.av_frame_unref(decFrame);
						}
					}
				}
				ffmpeg.av_packet_unref(pkt);
			}
			diaglog($"audio decode: {apkts} packets, {aframes} frames, pts={pts}");
			// 解码器 flush
			ffmpeg.avcodec_send_packet(adec, null);
			while (ffmpeg.avcodec_receive_frame(adec, decFrame) >= 0) {
				convertToFifo(swr, adec, aenc, decFrame, convFrame, fifo);
				pts = drainFifo(aenc, fifo, encFrame, frameSize, ofmt, outA, pts, pad: false);
				ffmpeg.av_frame_unref(decFrame);
			}
			// swr 尾部
			flushSwrToFifo(swr, aenc, convFrame, fifo);
			// FIFO 尾部：不足一帧则补零再编码
			pts = drainFifo(aenc, fifo, encFrame, frameSize, ofmt, outA, pts, pad: true);
			// 编码器 flush（部分 aac 在 EOF 上返回 EINVAL，已有帧则忽略）
			sendAudio(aenc, null, ofmt, outA, allowFail: true);
			diaglog($"audio done pts={pts} samples~ writing trailer");
			ffmpeg.av_write_trailer(ofmt);
			diaglog("trailer done");
		}
		finally {
			if (pkt != null) ffmpeg.av_packet_free(&pkt);
			if (decFrame != null) ffmpeg.av_frame_free(&decFrame);
			if (convFrame != null) ffmpeg.av_frame_free(&convFrame);
			if (encFrame != null) ffmpeg.av_frame_free(&encFrame);
			if (fifo != null) { var f = fifo; ffmpeg.av_audio_fifo_free(f); }
			if (swr != null) { var s = swr; ffmpeg.swr_free(&s); }
			if (adec != null) { var c = adec; ffmpeg.avcodec_free_context(&c); }
			if (aenc != null) { var c = aenc; ffmpeg.avcodec_free_context(&c); }
			if (ifmtV != null) { var f = ifmtV; ffmpeg.avformat_close_input(&f); }
			if (ifmtA != null) { var f = ifmtA; ffmpeg.avformat_close_input(&f); }
			if (ofmt != null) {
				if ((ofmt->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0 && ofmt->pb != null)
					ffmpeg.avio_closep(&ofmt->pb);
				ffmpeg.avformat_free_context(ofmt);
			}
		}
	}

	static void convertToFifo(SwrContext* swr, AVCodecContext* adec, AVCodecContext* aenc,
		AVFrame* decFrame, AVFrame* convFrame, AVAudioFifo* fifo) {
		var dstNb = (int)ffmpeg.av_rescale_rnd(
			ffmpeg.swr_get_delay(swr, adec->sample_rate) + decFrame->nb_samples,
			aenc->sample_rate, adec->sample_rate, AVRounding.AV_ROUND_UP);
		if (dstNb < 1) dstNb = 1;

		ffmpeg.av_frame_unref(convFrame);
		convFrame->nb_samples = dstNb;
		convFrame->format = (int)aenc->sample_fmt;
		convFrame->channel_layout = aenc->channel_layout;
		convFrame->channels = aenc->channels;
		convFrame->sample_rate = aenc->sample_rate;
		ffmpeg.av_frame_get_buffer(convFrame, 0).ThrowIfError("conv buf");

		var got = ffmpeg.swr_convert(swr, convFrame->extended_data, dstNb,
			decFrame->extended_data, decFrame->nb_samples);
		if (got <= 0) return;
		// 保证 fifo 空间
		if (ffmpeg.av_audio_fifo_space(fifo) < got)
			ffmpeg.av_audio_fifo_realloc(fifo, ffmpeg.av_audio_fifo_size(fifo) + got + 1024);
		ffmpeg.av_audio_fifo_write(fifo, (void**)convFrame->extended_data, got);
	}

	static void flushSwrToFifo(SwrContext* swr, AVCodecContext* aenc, AVFrame* convFrame, AVAudioFifo* fifo) {
		// 排空 swr 延迟缓冲
		for (var guard = 0; guard < 64; guard++) {
			var delay = (int)ffmpeg.swr_get_delay(swr, aenc->sample_rate);
			if (delay < 1) break;
			var dstNb = Math.Max(delay, 1);
			ffmpeg.av_frame_unref(convFrame);
			convFrame->nb_samples = dstNb;
			convFrame->format = (int)aenc->sample_fmt;
			convFrame->channel_layout = aenc->channel_layout;
			convFrame->channels = aenc->channels;
			convFrame->sample_rate = aenc->sample_rate;
			if (ffmpeg.av_frame_get_buffer(convFrame, 0) < 0) break;
			var got = ffmpeg.swr_convert(swr, convFrame->extended_data, dstNb, null, 0);
			if (got <= 0) break;
			if (ffmpeg.av_audio_fifo_space(fifo) < got)
				ffmpeg.av_audio_fifo_realloc(fifo, ffmpeg.av_audio_fifo_size(fifo) + got + 1024);
			ffmpeg.av_audio_fifo_write(fifo, (void**)convFrame->extended_data, got);
		}
	}

	/// <summary>
	/// 从 FIFO 取出完整 AAC 帧并编码。
	/// pad=true 时尾部不足一帧补零。
	/// </summary>
	static long drainFifo(AVCodecContext* aenc, AVAudioFifo* fifo, AVFrame* encFrame, int frameSize,
		AVFormatContext* ofmt, AVStream* outA, long pts, bool pad) {
		while (true) {
			var avail = ffmpeg.av_audio_fifo_size(fifo);
			if (avail >= frameSize) {
				// 满帧
			}
			else if (pad && avail > 0) {
				// 尾部：先读出剩余，再在帧缓冲里补零（靠 av_frame_get_buffer 清零 + 只写 avail）
			}
			else {
				break;
			}

			var n = Math.Min(avail, frameSize);
			if (!pad && n < frameSize) break;

			ffmpeg.av_frame_unref(encFrame);
			encFrame->nb_samples = frameSize;
			encFrame->format = (int)aenc->sample_fmt;
			encFrame->channel_layout = aenc->channel_layout;
			encFrame->channels = aenc->channels;
			encFrame->sample_rate = aenc->sample_rate;
			ffmpeg.av_frame_get_buffer(encFrame, 0).ThrowIfError("enc buf");
			// 缓冲已是 0 填充；读 n 采样（n 可能 < frameSize）
			var rd = ffmpeg.av_audio_fifo_read(fifo, (void**)encFrame->extended_data, n);
			if (rd <= 0) break;
			// MP3 固定 frame_size（多为 1152）；尾部用 get_buffer 的零填充
			encFrame->nb_samples = frameSize;
			encFrame->pts = pts;
			pts += frameSize;
			sendAudio(aenc, encFrame, ofmt, outA, allowFail: false);
			ffmpeg.av_frame_unref(encFrame);

			if (pad && avail < frameSize) break; // 只补一帧尾
		}
		return pts;
	}

	static void sendAudio(AVCodecContext* aenc, AVFrame* frame, AVFormatContext* ofmt, AVStream* outA,
		bool allowFail = false) {
		var pkt = ffmpeg.av_packet_alloc();
		try {
			var ret = ffmpeg.avcodec_send_frame(aenc, frame);
			if (ret < 0 && ret != ffmpeg.AVERROR_EOF) {
				if (allowFail) {
					diaglog("a send flush ret=" + ret);
					return;
				}
				ret.ThrowIfError("a send");
			}
			while (true) {
				ret = ffmpeg.avcodec_receive_packet(aenc, pkt);
				if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF) break;
				if (ret < 0) {
					if (allowFail) {
						diaglog("a recv flush ret=" + ret);
						break;
					}
					ret.ThrowIfError("a recv");
				}
				ffmpeg.av_packet_rescale_ts(pkt, aenc->time_base, outA->time_base);
				pkt->stream_index = outA->index;
				// 视频已全部写入后，交错缓冲可能已 flush，写失败则尝试非交错
				var wr = ffmpeg.av_interleaved_write_frame(ofmt, pkt);
				if (wr < 0)
					wr = ffmpeg.av_write_frame(ofmt, pkt);
				if (wr < 0) {
					if (allowFail) {
						diaglog("write a ret=" + wr);
						ffmpeg.av_packet_unref(pkt);
						break;
					}
					wr.ThrowIfError("write a");
				}
				ffmpeg.av_packet_unref(pkt);
			}
		}
		finally {
			ffmpeg.av_packet_free(&pkt);
		}
	}

	static AVCodec* findMp3Encoder() {
		// libmp3lame 为 MPEG Layer-3 标准编码器
		var enc = ffmpeg.avcodec_find_encoder_by_name("libmp3lame");
		if (enc != null) return enc;
		enc = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_MP3);
		if (enc != null) return enc;
		enc = ffmpeg.avcodec_find_encoder_by_name("mp3_mf"); // Windows MediaFoundation 后端
		if (enc != null) return enc;
		enc = ffmpeg.avcodec_find_encoder_by_name("libshine");
		return enc;
	}

	static AVSampleFormat pickSampleFmt(AVCodec* enc) {
		if (enc == null || enc->sample_fmts == null)
			return AVSampleFormat.AV_SAMPLE_FMT_S16P;
		// 优先有符号 16bit 平面/交错，再 FLTP
		for (var p = enc->sample_fmts; *p != AVSampleFormat.AV_SAMPLE_FMT_NONE; p++) {
			if (*p == AVSampleFormat.AV_SAMPLE_FMT_S16P) return *p;
		}
		for (var p = enc->sample_fmts; *p != AVSampleFormat.AV_SAMPLE_FMT_NONE; p++) {
			if (*p == AVSampleFormat.AV_SAMPLE_FMT_S16) return *p;
		}
		for (var p = enc->sample_fmts; *p != AVSampleFormat.AV_SAMPLE_FMT_NONE; p++) {
			if (*p == AVSampleFormat.AV_SAMPLE_FMT_FLTP) return *p;
		}
		return enc->sample_fmts[0];
	}

	static string findffmpeg() {
		// 完整 static/shared 构建通常 >2MB；过小的多为 stub/旧 launcher
		const long MinBytes = 2_000_000;
		var cands = new List<string> {
			Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg64", "ffmpeg.exe"),
			Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
			@"C:\bin\ffmpeg.exe",
		};
		try {
			var path = Environment.GetEnvironmentVariable("PATH") ?? "";
			foreach (var dir in path.Split(Path.PathSeparator)) {
				if (string.IsNullOrWhiteSpace(dir)) continue;
				cands.Add(Path.Combine(dir.Trim(), "ffmpeg.exe"));
			}
		}
		catch { }
		foreach (var c in cands) {
			try {
				if (File.Exists(c) && new FileInfo(c).Length >= MinBytes)
					return c;
			}
			catch { }
		}
		return null;
	}

	static void diaglog(string msg) {
		// 写入录屏会话日志 + 独立 merge_diag 备份
		try { RecordLog.Step("merge", msg); } catch { }
		try {
			var p = Path.Combine(TmpStore.Root, "merge_diag.log");
			File.AppendAllText(p,
				DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine,
				System.Text.Encoding.UTF8);
		}
		catch { }
	}

	internal static bool HasAudioStream(string path) {
		if (!FfmpegLoader.TryInit(out _)) return false;
		AVFormatContext* fmt = null;
		try {
			if (ffmpeg.avformat_open_input(&fmt, path, null, null) < 0 || fmt == null)
				return false;
			if (ffmpeg.avformat_find_stream_info(fmt, null) < 0)
				return false;
			for (uint i = 0; i < fmt->nb_streams; i++)
				if (fmt->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
					return true;
			return false;
		}
		catch {
			return false;
		}
		finally {
			if (fmt != null) { var f = fmt; ffmpeg.avformat_close_input(&f); }
		}
	}
}
