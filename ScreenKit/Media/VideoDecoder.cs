using System;
using FFmpeg.AutoGen;

namespace ScreenKit;

/// <summary>按流参数建解码器，输出 BGRA 连续缓冲。一帧解码完马上拷出，帧对象自己不跨线程。</summary>
unsafe sealed class VideoDecoder : IDisposable {
	AVCodecContext* dec;
	AVFrame* frame;
	SwsContext* sws;
	int swsw, swsh, swsfmt;
	byte[] bgra;
	bool disposed;

	public int Width { get; private set; }
	public int Height { get; private set; }
	public int Stride { get; private set; }
	public int Rotation { get; set; }
	readonly AVRational timebase;

	public VideoDecoder(AVCodecParameters* par, int rotation, AVRational tb) {
		timebase = tb;
		Rotation = rotation;
		if (!FfmpegLoader.TryInit(out var err))
			throw new InvalidOperationException(err ?? "FFmpeg 未就绪");
		if (par == null) throw new InvalidOperationException("缺少视频流参数");
		var codec = ffmpeg.avcodec_find_decoder(par->codec_id);
		if (codec == null)
			throw new InvalidOperationException("没有对应的视频解码器: " + ffmpeg.avcodec_get_name(par->codec_id));
		dec = ffmpeg.avcodec_alloc_context3(codec);
		if (dec == null) throw new InvalidOperationException("avcodec_alloc_context3 失败");
		try {
			ffmpeg.avcodec_parameters_to_context(dec, par).ThrowIfError("parameters_to_context");
			dec->thread_count = 0; // 0 = 让 ffmpeg 按核数自动
			ffmpeg.avcodec_open2(dec, codec, null).ThrowIfError("avcodec_open2 video");
			frame = ffmpeg.av_frame_alloc();
			if (frame == null) throw new InvalidOperationException("av_frame_alloc 失败");
		}
		catch {
			if (dec != null) { var d = dec; ffmpeg.avcodec_free_context(&d); dec = null; }
			throw;
		}
	}

	/// <summary>送一个包，返回原始返回码；EAGAIN 表示要先收帧再送，不能当失败扔包。</summary>
	public int Send(AVPacket* p) {
		if (disposed || dec == null) return -1;
		return ffmpeg.avcodec_send_packet(dec, p);
	}

	/// <summary>排空：送 null 包让解码器吐出剩余帧。</summary>
	public void Drain() {
		if (disposed || dec == null) return;
		ffmpeg.avcodec_send_packet(dec, null);
	}

	/// <summary>收一帧到内部 BGRA 缓冲。成功时 out 指向从 this.bgra 开头的数据，调用方须在下次调用前用完。</summary>
	public bool Receive(out double ptsSec) {
		ptsSec = 0;
		if (disposed || dec == null || frame == null) return false;
		var r = ffmpeg.avcodec_receive_frame(dec, frame);
		if (r < 0) return false;
		var w = frame->width;
		var h = frame->height;
		if (w <= 0 || h <= 0 || w > 8192 || h > 8192) return false;
		Width = w;
		Height = h;
		ensuresws(w, h);
		if (sws == null || bgra == null) return false;
		var dst = new byte_ptrArray8();
		var dstst = new int_array8();
		fixed (byte* dp = bgra) {
			dst[0] = dp;
			dstst[0] = Stride;
			ffmpeg.sws_scale(sws, frame->data, frame->linesize, 0, h, dst, dstst);
		}
		var best = ffmpeg.av_frame_get_best_effort_timestamp(frame);
		if (best == ffmpeg.AV_NOPTS_VALUE) best = frame->pts;
		// 必须用流的 time_base：解码器 context 的 time_base 常常是 1/1000000 或 0
		ptsSec = best == ffmpeg.AV_NOPTS_VALUE ? double.NaN : best * ffmpeg.av_q2d(timebase);
		return true;
	}

	public byte[] Buffer => bgra;
	public bool HasFrame => bgra != null;

	void ensuresws(int w, int h) {
		var fmt = frame->format;
		if (sws != null && swsw == w && swsh == h && swsfmt == fmt) return;
		if (sws != null) { ffmpeg.sws_freeContext(sws); sws = null; }
		swsw = w;
		swsh = h;
		swsfmt = fmt;
		Stride = w * 4;
		bgra = new byte[Stride * h];
		sws = ffmpeg.sws_getContext(w, h, (AVPixelFormat)fmt, w, h,
			AVPixelFormat.AV_PIX_FMT_BGRA, ffmpeg.SWS_BILINEAR, null, null, null);
	}

	public void Flush() {
		if (disposed || dec == null) return;
		ffmpeg.avcodec_flush_buffers(dec);
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		if (sws != null) { ffmpeg.sws_freeContext(sws); sws = null; }
		if (frame != null) { var f = frame; ffmpeg.av_frame_free(&f); frame = null; }
		if (dec != null) { var d = dec; ffmpeg.avcodec_free_context(&d); dec = null; }
		bgra = null;
	}
}
