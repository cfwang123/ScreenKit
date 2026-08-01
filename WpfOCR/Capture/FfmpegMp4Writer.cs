using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace WpfOCR;

/// <summary>
/// FFmpeg.AutoGen + x264/x265 边收 BGRA 帧边写 MP4。
/// 支持 CRF、输出分辨率 fit 缩放。
/// </summary>
unsafe sealed class FfmpegMp4Writer : IDisposable {
	readonly int srcW, srcH, outW, outH, fps, crf;
	readonly bool hevc;
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
	public string CodecName => hevc ? "x265" : "x264";

	public FfmpegMp4Writer(string path, int captureW, int captureH, RecordOptions opt) {
		if (!FfmpegLoader.TryInit(out var err))
			throw new InvalidOperationException(err ?? "FFmpeg 未就绪");
		this.path = path ?? throw new ArgumentNullException(nameof(path));
		opt ??= new RecordOptions();
		opt.Clamp();

		srcW = Math.Max(2, captureW / 2 * 2);
		srcH = Math.Max(2, captureH / 2 * 2);
		opt.FitSize(srcW, srcH, out outW, out outH);
		fps = opt.Fps;
		crf = opt.Crf;
		hevc = opt.IsHevc;
		if (outW < 16 || outH < 16)
			throw new ArgumentException("录制区域过小");

		open();
	}

	/// <summary>兼容旧调用。</summary>
	public FfmpegMp4Writer(string path, int width, int height, int fps = 24)
		: this(path, width, height, new RecordOptions { Fps = fps }) { }

	void open() {
		AVFormatContext* f = null;
		ffmpeg.avformat_alloc_output_context2(&f, null, "mp4", path).ThrowIfError("avformat_alloc");
		fmt = f;

		AVCodec* enc = null;
		AVCodecID codecId;
		if (hevc) {
			codecId = AVCodecID.AV_CODEC_ID_HEVC;
			enc = ffmpeg.avcodec_find_encoder_by_name("libx265");
			if (enc == null) enc = ffmpeg.avcodec_find_encoder(codecId);
			if (enc == null)
				throw new InvalidOperationException("找不到 x265/HEVC 编码器（ffmpeg64 需含 libx265）");
		}
		else {
			codecId = AVCodecID.AV_CODEC_ID_H264;
			enc = ffmpeg.avcodec_find_encoder_by_name("libx264");
			if (enc == null) enc = ffmpeg.avcodec_find_encoder(codecId);
			if (enc == null)
				throw new InvalidOperationException("找不到 H.264 编码器（需要 libx264）");
		}

		stream = ffmpeg.avformat_new_stream(fmt, null);
		if (stream == null) throw new InvalidOperationException("avformat_new_stream 失败");

		codec = ffmpeg.avcodec_alloc_context3(enc);
		if (codec == null) throw new InvalidOperationException("avcodec_alloc_context3 失败");

		codec->codec_id = codecId;
		codec->width = outW;
		codec->height = outH;
		codec->time_base = new AVRational { num = 1, den = fps };
		codec->framerate = new AVRational { num = fps, den = 1 };
		codec->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
		codec->gop_size = fps * 2;
		codec->max_b_frames = hevc ? 0 : 0;
		// CRF 为主；给一个温和上限防极端
		var br = (long)outW * outH / 900;
		codec->bit_rate = Compat.Clamp(br, 800_000, 4_000_000);
		codec->rc_max_rate = codec->bit_rate * 2;
		codec->rc_buffer_size = (int)(codec->bit_rate * 2);

		if (codec->priv_data != null) {
			ffmpeg.av_opt_set(codec->priv_data, "preset", hevc ? "fast" : "veryfast", 0);
			if (!hevc)
				ffmpeg.av_opt_set(codec->priv_data, "tune", "zerolatency", 0);
			ffmpeg.av_opt_set(codec->priv_data, "crf", crf.ToString(), 0);
		}

		if ((fmt->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
			codec->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

		ffmpeg.avcodec_open2(codec, enc, null).ThrowIfError("avcodec_open2");
		ffmpeg.avcodec_parameters_from_context(stream->codecpar, codec).ThrowIfError("params_from_ctx");
		stream->time_base = codec->time_base;

		if ((fmt->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0) {
			AVIOContext* io = null;
			ffmpeg.avio_open(&io, path, ffmpeg.AVIO_FLAG_WRITE).ThrowIfError("avio_open");
			fmt->pb = io;
		}

		ffmpeg.avformat_write_header(fmt, null).ThrowIfError("write_header");
		headerOk = true;

		frame = ffmpeg.av_frame_alloc();
		frame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
		frame->width = outW;
		frame->height = outH;
		ffmpeg.av_frame_get_buffer(frame, 32).ThrowIfError("frame_get_buffer");

		packet = ffmpeg.av_packet_alloc();
		// 采集分辨率 → 输出分辨率（可缩放）
		sws = ffmpeg.sws_getContext(
			srcW, srcH, AVPixelFormat.AV_PIX_FMT_BGRA,
			outW, outH, AVPixelFormat.AV_PIX_FMT_YUV420P,
			ffmpeg.SWS_BILINEAR, null, null, null);
		if (sws == null) throw new InvalidOperationException("sws_getContext 失败");
	}

	/// <summary>写入一帧 BGRA（采集分辨率 stride）。PTS 按帧序号递增。</summary>
	public void WriteBgra(byte[] bgra, int stride) => WriteBgra(bgra, stride, frameIndex);

	/// <summary>
	/// 写入一帧 BGRA。
	/// <paramref name="pts"/> 为 time_base=1/fps 下的展示时间戳；
	/// 长时间录屏应按墙钟推算 PTS（可跳号），使视频时长贴近真实时间，避免与按墙钟补齐的音轨漂移。
	/// </summary>
	public void WriteBgra(byte[] bgra, int stride, long pts) {
		if (disposed) throw new ObjectDisposedException(nameof(FfmpegMp4Writer));
		if (bgra == null) throw new ArgumentNullException(nameof(bgra));
		ffmpeg.av_frame_make_writable(frame).ThrowIfError("frame_writable");

		fixed (byte* pSrc = bgra) {
			var srcSlice = new byte_ptrArray8();
			srcSlice[0] = pSrc;
			var srcStride = new int_array8();
			srcStride[0] = stride;
			ffmpeg.sws_scale(sws, srcSlice, srcStride, 0, srcH, frame->data, frame->linesize);
		}

		// 单调递增：同毫秒连写或调度抖动时至少 +1
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

static class FfmpegThrow {
	public static int ThrowIfError(this int err, string where) {
		if (err >= 0) return err;
		var buf = new byte[1024];
		string msg;
		unsafe {
			fixed (byte* p = buf)
				ffmpeg.av_strerror(err, p, (ulong)buf.Length);
			msg = System.Text.Encoding.UTF8.GetString(buf).TrimEnd('\0');
		}
		throw new InvalidOperationException($"{where}: {msg} ({err})");
	}
}
