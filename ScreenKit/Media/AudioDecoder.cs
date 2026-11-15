using System;
using FFmpeg.AutoGen;

namespace ScreenKit;

/// <summary>按流参数建解码器，统一重采样到 S16 立体声 48k，直接喂 WaveOut。</summary>
unsafe sealed class AudioDecoder : IDisposable {
	public const int OUTRATE = 48000;
	public const int OUTCH = 2;

	AVCodecContext* dec;
	AVFrame* frame;
	SwrContext* swr;
	bool swrready;
	bool disposed;

	public int SampleRate => OUTRATE;
	public int Channels => OUTCH;

	public AudioDecoder(AVCodecParameters* par) {
		if (!FfmpegLoader.TryInit(out var err))
			throw new InvalidOperationException(err ?? "FFmpeg 未就绪");
		if (par == null) throw new InvalidOperationException("缺少音频流参数");
		var codec = ffmpeg.avcodec_find_decoder(par->codec_id);
		if (codec == null)
			throw new InvalidOperationException("没有对应的音频解码器: " + ffmpeg.avcodec_get_name(par->codec_id));
		dec = ffmpeg.avcodec_alloc_context3(codec);
		if (dec == null) throw new InvalidOperationException("avcodec_alloc_context3 失败");
		try {
			ffmpeg.avcodec_parameters_to_context(dec, par).ThrowIfError("parameters_to_context");
			ffmpeg.avcodec_open2(dec, codec, null).ThrowIfError("avcodec_open2 audio");
			frame = ffmpeg.av_frame_alloc();
			if (frame == null) throw new InvalidOperationException("av_frame_alloc 失败");
		}
		catch {
			if (dec != null) { var d = dec; ffmpeg.avcodec_free_context(&d); dec = null; }
			throw;
		}
	}

	/// <summary>送一个包，返回原始返回码；EAGAIN 表示要先收帧再送，不能当作已接收。</summary>
	public int Send(AVPacket* p) {
		if (disposed || dec == null) return -1;
		return ffmpeg.avcodec_send_packet(dec, p);
	}

	public void Drain() {
		if (disposed || dec == null) return;
		ffmpeg.avcodec_send_packet(dec, null);
	}

	/// <summary>收一帧并转成 S16 立体声。成功时返回 PCM 字节（长度 = 帧数*4）。</summary>
	public byte[] Receive() {
		if (disposed || dec == null || frame == null) return null;
		var r = ffmpeg.avcodec_receive_frame(dec, frame);
		if (r < 0) return null;
		var nb = frame->nb_samples;
		if (nb <= 0) return null;
		if (!swrready) {
			if (!initswr()) return null;
			swrready = true;
		}
		var outn = ffmpeg.swr_get_out_samples(swr, nb);
		if (outn <= 0) outn = nb;
		var bytes = outn * OUTCH * 2;
		if (bytes <= 0 || bytes > 8_000_000) return null;
		var pcm = new byte[bytes];
		int got;
		fixed (byte* dp = pcm) {
			byte* dptr = dp;
			var n = ffmpeg.swr_convert(swr, &dptr, outn, frame->extended_data, nb);
			if (n < 0) return null;
			got = n * OUTCH * 2;
		}
		if (got <= 0) return null;
		if (got == pcm.Length) return pcm;
		var trim = new byte[got];
		Buffer.BlockCopy(pcm, 0, trim, 0, got);
		return trim;
	}

	bool initswr() {
		var inrate = frame->sample_rate > 0 ? frame->sample_rate : OUTRATE;
		var inlayout = frame->channel_layout != 0
			? frame->channel_layout
			: (ulong)(frame->channels <= 1 ? ffmpeg.AV_CH_LAYOUT_MONO : ffmpeg.AV_CH_LAYOUT_STEREO);
		swr = ffmpeg.swr_alloc_set_opts(null,
			ffmpeg.AV_CH_LAYOUT_STEREO, AVSampleFormat.AV_SAMPLE_FMT_S16, OUTRATE,
			(long)inlayout, (AVSampleFormat)frame->format, inrate,
			0, null);
		if (swr == null) return false;
		return ffmpeg.swr_init(swr) >= 0;
	}

	public void Flush() {
		if (disposed || dec == null) return;
		ffmpeg.avcodec_flush_buffers(dec);
		swrready = false;
		if (swr != null) { var s = swr; ffmpeg.swr_free(&s); swr = null; }
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		if (swr != null) { var s = swr; ffmpeg.swr_free(&s); swr = null; }
		if (frame != null) { var f = frame; ffmpeg.av_frame_free(&f); frame = null; }
		if (dec != null) { var d = dec; ffmpeg.avcodec_free_context(&d); dec = null; }
	}
}
