using System.IO;
using FFmpeg.AutoGen;

namespace ScreenKit;

/// <summary>
/// 用 FFmpeg.AutoGen（ffmpeg64 DLL）解码音视频中的音轨为 mono float PCM。
/// </summary>
static unsafe class FfmpegAudioDecode {
	/// <summary>解码为单声道 float（-1~1），并重采样到 <paramref name="outSampleRate"/>。</summary>
	public static (float[] samples, int sampleRate) DecodeMono(string path, int outSampleRate = 16000) {
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			throw new FileNotFoundException("音视频文件不存在", path);
		if (outSampleRate <= 0) outSampleRate = 16000;
		if (!FfmpegLoader.TryInit(out var err))
			throw new InvalidOperationException(
				err ?? "FFmpeg DLL 未就绪（请将 shared 库放到程序目录 ffmpeg64/）");

		AVFormatContext* fmt = null;
		AVCodecContext* dec = null;
		SwrContext* swr = null;
		AVPacket* pkt = null;
		AVFrame* frame = null;
		AVFrame* oframe = null;

		try {
			ffmpeg.avformat_open_input(&fmt, path, null, null).ThrowIfError("avformat_open_input");
			ffmpeg.avformat_find_stream_info(fmt, null).ThrowIfError("find_stream_info");

			var aIdx = -1;
			for (uint i = 0; i < fmt->nb_streams; i++) {
				if (fmt->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO) {
					aIdx = (int)i;
					break;
				}
			}
			if (aIdx < 0)
				throw new InvalidOperationException("文件中未找到音频流");

			var st = fmt->streams[aIdx];
			var codec = ffmpeg.avcodec_find_decoder(st->codecpar->codec_id);
			if (codec == null)
				throw new InvalidOperationException("无对应音频解码器: " + st->codecpar->codec_id);

			dec = ffmpeg.avcodec_alloc_context3(codec);
			if (dec == null) throw new InvalidOperationException("avcodec_alloc_context3 失败");
			ffmpeg.avcodec_parameters_to_context(dec, st->codecpar).ThrowIfError("parameters_to_context");
			ffmpeg.avcodec_open2(dec, codec, null).ThrowIfError("avcodec_open2");

			var inRate = dec->sample_rate > 0 ? dec->sample_rate : outSampleRate;
			ulong inLayout = dec->channel_layout != 0
				? dec->channel_layout
				: (ulong)(dec->channels <= 1 ? ffmpeg.AV_CH_LAYOUT_MONO : ffmpeg.AV_CH_LAYOUT_STEREO);

			swr = ffmpeg.swr_alloc_set_opts(
				null,
				ffmpeg.AV_CH_LAYOUT_MONO, AVSampleFormat.AV_SAMPLE_FMT_FLT, outSampleRate,
				(long)inLayout, dec->sample_fmt, inRate,
				0, null);
			if (swr == null) throw new InvalidOperationException("swr_alloc_set_opts 失败");
			ffmpeg.swr_init(swr).ThrowIfError("swr_init");

			// 预估容量（未知时长时至少留 1 分钟）
			var estimate = outSampleRate * 60;
			if (fmt->duration > 0) {
				var sec = fmt->duration / (double)ffmpeg.AV_TIME_BASE;
				if (sec > 0 && sec < 24 * 3600)
					estimate = Math.Max(outSampleRate, (int)(sec * outSampleRate) + outSampleRate);
			}
			var list = new List<float>(estimate);

			pkt = ffmpeg.av_packet_alloc();
			frame = ffmpeg.av_frame_alloc();
			oframe = ffmpeg.av_frame_alloc();
			if (pkt == null || frame == null || oframe == null)
				throw new InvalidOperationException("av_packet/frame_alloc 失败");

			while (ffmpeg.av_read_frame(fmt, pkt) >= 0) {
				if (pkt->stream_index == aIdx)
					decodePacket(dec, swr, pkt, frame, oframe, outSampleRate, list);
				ffmpeg.av_packet_unref(pkt);
			}
			// flush decoder
			decodePacket(dec, swr, null, frame, oframe, outSampleRate, list);
			// flush resampler
			flushSwr(swr, oframe, outSampleRate, list);

			if (list.Count == 0)
				throw new InvalidOperationException("解码后无音频采样（可能音轨为空）");
			return (list.ToArray(), outSampleRate);
		}
		finally {
			if (pkt != null) ffmpeg.av_packet_free(&pkt);
			if (frame != null) ffmpeg.av_frame_free(&frame);
			if (oframe != null) ffmpeg.av_frame_free(&oframe);
			if (swr != null) { var s = swr; ffmpeg.swr_free(&s); }
			if (dec != null) { var c = dec; ffmpeg.avcodec_free_context(&c); }
			if (fmt != null) { var f = fmt; ffmpeg.avformat_close_input(&f); }
		}
	}

	static void decodePacket(AVCodecContext* dec, SwrContext* swr, AVPacket* pkt,
		AVFrame* frame, AVFrame* oframe, int outRate, List<float> list) {
		var ret = ffmpeg.avcodec_send_packet(dec, pkt);
		if (ret < 0 && ret != ffmpeg.AVERROR_EOF) {
			// 个别损坏包可跳过
			if (pkt != null) return;
			ret.ThrowIfError("avcodec_send_packet");
		}
		while (true) {
			ret = ffmpeg.avcodec_receive_frame(dec, frame);
			if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
				break;
			if (ret < 0) break;
			convertAppend(swr, dec, frame, oframe, outRate, list);
			ffmpeg.av_frame_unref(frame);
		}
	}

	static void convertAppend(SwrContext* swr, AVCodecContext* dec, AVFrame* frame,
		AVFrame* oframe, int outRate, List<float> list) {
		var inRate = dec->sample_rate > 0 ? dec->sample_rate : outRate;
		var dstNb = (int)ffmpeg.av_rescale_rnd(
			ffmpeg.swr_get_delay(swr, inRate) + frame->nb_samples,
			outRate, inRate, AVRounding.AV_ROUND_UP);
		if (dstNb < 1) dstNb = 1;

		ffmpeg.av_frame_unref(oframe);
		oframe->nb_samples = dstNb;
		oframe->format = (int)AVSampleFormat.AV_SAMPLE_FMT_FLT;
		oframe->channel_layout = (ulong)ffmpeg.AV_CH_LAYOUT_MONO;
		oframe->channels = 1;
		oframe->sample_rate = outRate;
		if (ffmpeg.av_frame_get_buffer(oframe, 0) < 0) return;

		var got = ffmpeg.swr_convert(swr, oframe->extended_data, dstNb,
			frame->extended_data, frame->nb_samples);
		if (got <= 0) return;
		copyMonoFloat(oframe, got, list);
	}

	static void flushSwr(SwrContext* swr, AVFrame* oframe, int outRate, List<float> list) {
		for (var guard = 0; guard < 64; guard++) {
			var delay = (int)ffmpeg.swr_get_delay(swr, outRate);
			if (delay < 1) break;
			var dstNb = Math.Max(delay, 1);
			ffmpeg.av_frame_unref(oframe);
			oframe->nb_samples = dstNb;
			oframe->format = (int)AVSampleFormat.AV_SAMPLE_FMT_FLT;
			oframe->channel_layout = (ulong)ffmpeg.AV_CH_LAYOUT_MONO;
			oframe->channels = 1;
			oframe->sample_rate = outRate;
			if (ffmpeg.av_frame_get_buffer(oframe, 0) < 0) break;
			var got = ffmpeg.swr_convert(swr, oframe->extended_data, dstNb, null, 0);
			if (got <= 0) break;
			copyMonoFloat(oframe, got, list);
		}
	}

	static void copyMonoFloat(AVFrame* oframe, int samples, List<float> list) {
		if (samples <= 0 || oframe->extended_data == null || oframe->extended_data[0] == null)
			return;
		var ptr = (float*)oframe->extended_data[0];
		for (int i = 0; i < samples; i++)
			list.Add(ptr[i]);
	}
}
