using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace ScreenKit;

unsafe sealed class CastVideoEncoder : IDisposable {
	readonly int srcW, srcH, outW, outH, fps, bitrate, crf;
	AVCodecContext* enc;
	AVFrame* frame;
	AVPacket* pkt;
	SwsContext* sws;
	long pts;
	bool disposed;
	public int OutWidth => outW;
	public int OutHeight => outH;

	public CastVideoEncoder(int srcW, int srcH, CastQuality q) {
		if (!FfmpegLoader.TryInit(out var err))
			throw new InvalidOperationException(err ?? "FFmpeg 未就绪");
		q.Fit(srcW, srcH, out outW, out outH);
		this.srcW = Math.Max(2, srcW / 2 * 2);
		this.srcH = Math.Max(2, srcH / 2 * 2);
		fps = q.Fps;
		bitrate = q.Bitrate;
		crf = q.Crf;
		var codec = ffmpeg.avcodec_find_encoder_by_name("libx264");
		if (codec == null) codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_H264);
		if (codec == null) throw new InvalidOperationException("找不到 libx264");
		enc = ffmpeg.avcodec_alloc_context3(codec);
		enc->codec_id = AVCodecID.AV_CODEC_ID_H264;
		enc->width = outW;
		enc->height = outH;
		enc->time_base = new AVRational { num = 1, den = fps };
		enc->framerate = new AVRational { num = fps, den = 1 };
		enc->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
		enc->gop_size = fps;
		enc->max_b_frames = 0;
		enc->bit_rate = bitrate;
		enc->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
		if (enc->priv_data != null) {
			ffmpeg.av_opt_set(enc->priv_data, "preset", "ultrafast", 0);
			ffmpeg.av_opt_set(enc->priv_data, "tune", "zerolatency", 0);
			ffmpeg.av_opt_set(enc->priv_data, "crf", crf.ToString(), 0);
		}
		ffmpeg.avcodec_open2(enc, codec, null).Check("avcodec_open2 x264");
		frame = ffmpeg.av_frame_alloc();
		frame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
		frame->width = outW;
		frame->height = outH;
		ffmpeg.av_frame_get_buffer(frame, 32).Check("frame_get_buffer");
		pkt = ffmpeg.av_packet_alloc();
		sws = ffmpeg.sws_getContext(this.srcW, this.srcH, AVPixelFormat.AV_PIX_FMT_BGRA,
			outW, outH, AVPixelFormat.AV_PIX_FMT_YUV420P, ffmpeg.SWS_BILINEAR, null, null, null);
		if (sws == null) throw new InvalidOperationException("sws_getContext 失败");
	}

	public byte[] EncodeBgra(byte[] bgra, int stride) {
		if (disposed || bgra == null) return null;
		ffmpeg.av_frame_make_writable(frame).Check("frame_writable");
		fixed (byte* pSrc = bgra) {
			var srcSlice = new byte_ptrArray8();
			srcSlice[0] = pSrc;
			var srcStride = new int_array8();
			srcStride[0] = stride;
			ffmpeg.sws_scale(sws, srcSlice, srcStride, 0, srcH, frame->data, frame->linesize);
		}
		frame->pts = pts++;
		ffmpeg.avcodec_send_frame(enc, frame).Check("send_frame");
		var r = ffmpeg.avcodec_receive_packet(enc, pkt);
		if (r < 0) return null;
		var nal = new byte[pkt->size];
		Marshal.Copy((IntPtr)pkt->data, nal, 0, pkt->size);
		ffmpeg.av_packet_unref(pkt);
		return nal;
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		if (sws != null) { ffmpeg.sws_freeContext(sws); sws = null; }
		if (pkt != null) { var p = pkt; ffmpeg.av_packet_free(&p); pkt = null; }
		if (frame != null) { var f = frame; ffmpeg.av_frame_free(&f); frame = null; }
		if (enc != null) { var e = enc; ffmpeg.avcodec_free_context(&e); enc = null; }
	}
}
