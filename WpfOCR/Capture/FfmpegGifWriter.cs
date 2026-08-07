using FFmpeg.AutoGen;

namespace WpfOCR;

/// <summary>
/// FFmpeg.AutoGen 边收 BGRA 帧边写 GIF（无声、低帧率）。
/// 像素经 swscale 量化为 RGB8（256 色）。
/// </summary>
unsafe sealed class FfmpegGifWriter : IDisposable {
	readonly int srcW, srcH, outW, outH, fps;
	readonly string path;
	AVFormatContext* fmt;
	AVCodecContext* codec;
	AVStream* stream;
	AVFrame* frame;
	AVPacket* packet;
	SwsContext* sws;
	long frameIndex;
	bool headerOk;
	bool disposed;

	public int OutWidth => outW;
	public int OutHeight => outH;
	public string Path => path;
	public long FrameCount => frameIndex;

	public FfmpegGifWriter(string path, int captureW, int captureH, GifOptions opt) {
		if (!FfmpegLoader.TryInit(out var err))
			throw new InvalidOperationException(err ?? "FFmpeg 未就绪");
		this.path = path ?? throw new ArgumentNullException(nameof(path));
		opt ??= new GifOptions();
		opt.Clamp();

		srcW = Math.Max(2, captureW);
		srcH = Math.Max(2, captureH);
		opt.FitSize(srcW, srcH, out outW, out outH);
		fps = opt.Fps;
		if (outW < 16 || outH < 16)
			throw new ArgumentException("录制区域过小");

		open();
	}

	void open() {
		AVFormatContext* f = null;
		ffmpeg.avformat_alloc_output_context2(&f, null, "gif", path).ThrowIfError("avformat_alloc");
		fmt = f;

		var enc = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_GIF);
		if (enc == null)
			enc = ffmpeg.avcodec_find_encoder_by_name("gif");
		if (enc == null)
			throw new InvalidOperationException("找不到 GIF 编码器（ffmpeg64 需含 gif）");

		stream = ffmpeg.avformat_new_stream(fmt, null);
		if (stream == null) throw new InvalidOperationException("avformat_new_stream 失败");

		codec = ffmpeg.avcodec_alloc_context3(enc);
		if (codec == null) throw new InvalidOperationException("avcodec_alloc_context3 失败");

		codec->codec_id = AVCodecID.AV_CODEC_ID_GIF;
		codec->width = outW;
		codec->height = outH;
		codec->time_base = new AVRational { num = 1, den = fps };
		codec->framerate = new AVRational { num = fps, den = 1 };
		codec->pix_fmt = AVPixelFormat.AV_PIX_FMT_RGB8;

		if ((fmt->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
			codec->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

		ffmpeg.avcodec_open2(codec, enc, null).ThrowIfError("avcodec_open2");
		ffmpeg.avcodec_parameters_from_context(stream->codecpar, codec).ThrowIfError("params_from_ctx");
		stream->time_base = codec->time_base;

		// 无限循环播放
		if (fmt->priv_data != null)
			ffmpeg.av_opt_set_int(fmt->priv_data, "loop", 0, 0);

		if ((fmt->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0) {
			AVIOContext* io = null;
			ffmpeg.avio_open(&io, path, ffmpeg.AVIO_FLAG_WRITE).ThrowIfError("avio_open");
			fmt->pb = io;
		}

		ffmpeg.avformat_write_header(fmt, null).ThrowIfError("write_header");
		headerOk = true;

		frame = ffmpeg.av_frame_alloc();
		frame->format = (int)AVPixelFormat.AV_PIX_FMT_RGB8;
		frame->width = outW;
		frame->height = outH;
		ffmpeg.av_frame_get_buffer(frame, 32).ThrowIfError("frame_get_buffer");

		packet = ffmpeg.av_packet_alloc();
		sws = ffmpeg.sws_getContext(
			srcW, srcH, AVPixelFormat.AV_PIX_FMT_BGRA,
			outW, outH, AVPixelFormat.AV_PIX_FMT_RGB8,
			ffmpeg.SWS_BILINEAR, null, null, null);
		if (sws == null) throw new InvalidOperationException("sws_getContext 失败");
	}

	/// <summary>写入一帧 BGRA。PTS 按墙钟（time_base=1/fps）。</summary>
	public void WriteBgra(byte[] bgra, int stride, long pts) {
		if (disposed) throw new ObjectDisposedException(nameof(FfmpegGifWriter));
		if (bgra == null) throw new ArgumentNullException(nameof(bgra));
		ffmpeg.av_frame_make_writable(frame).ThrowIfError("frame_writable");

		fixed (byte* pSrc = bgra) {
			var srcSlice = new byte_ptrArray8();
			srcSlice[0] = pSrc;
			var srcStride = new int_array8();
			srcStride[0] = stride;
			ffmpeg.sws_scale(sws, srcSlice, srcStride, 0, srcH, frame->data, frame->linesize);
		}

		if (pts < frameIndex) pts = frameIndex;
		frame->pts = pts;
		frameIndex = pts + 1;
		encode(frame);
	}

	void encode(AVFrame* f) {
		ffmpeg.avcodec_send_frame(codec, f).ThrowIfError("send_frame");
		while (true) {
			var ret = ffmpeg.avcodec_receive_packet(codec, packet);
			if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
				break;
			ret.ThrowIfError("receive_packet");
			ffmpeg.av_packet_rescale_ts(packet, codec->time_base, stream->time_base);
			packet->stream_index = stream->index;
			ffmpeg.av_interleaved_write_frame(fmt, packet).ThrowIfError("write_frame");
			ffmpeg.av_packet_unref(packet);
		}
	}

	public void Finish() {
		if (disposed || !headerOk) return;
		try {
			encode(null);
			ffmpeg.av_write_trailer(fmt);
		}
		catch { }
		headerOk = false;
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		try { Finish(); } catch { }

		if (packet != null) {
			fixed (AVPacket** pp = &packet) ffmpeg.av_packet_free(pp);
			packet = null;
		}
		if (frame != null) {
			fixed (AVFrame** pf = &frame) ffmpeg.av_frame_free(pf);
			frame = null;
		}
		if (sws != null) {
			ffmpeg.sws_freeContext(sws);
			sws = null;
		}
		if (codec != null) {
			fixed (AVCodecContext** pc = &codec) ffmpeg.avcodec_free_context(pc);
			codec = null;
		}
		if (fmt != null) {
			if ((fmt->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0 && fmt->pb != null)
				ffmpeg.avio_closep(&fmt->pb);
			fixed (AVFormatContext** pf = &fmt)
				ffmpeg.avformat_free_context(*pf);
			fmt = null;
		}
	}
}
