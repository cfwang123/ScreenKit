using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace ScreenKit;

/// <summary>用 ffmpeg64 里的 libx264 把 BGRA 编成 Annex-B。不依赖 ffmpeg.exe。</summary>
unsafe sealed class RtspH264 : IDisposable {
	AVCodecContext* enc;
	AVFrame* frame;
	AVPacket* pkt;
	SwsContext* sws;
	long pts;
	int force;
	bool disposed;
	readonly int w, h;

	public byte[] Sps { get; private set; }
	public byte[] Pps { get; private set; }

	public RtspH264(int w, int h, int fps, int bitrate) {
		if (!FfmpegLoader.TryInit(out var err))
			throw new InvalidOperationException(err ?? "FFmpeg 未就绪");
		this.w = w;
		this.h = h;
		fps = Math.Max(1, fps);
		var codec = ffmpeg.avcodec_find_encoder_by_name("libx264");
		if (codec == null) codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_H264);
		if (codec == null) throw new InvalidOperationException("找不到 libx264");
		enc = ffmpeg.avcodec_alloc_context3(codec);
		enc->codec_id = AVCodecID.AV_CODEC_ID_H264;
		enc->width = w;
		enc->height = h;
		enc->time_base = new AVRational { num = 1, den = fps };
		enc->framerate = new AVRational { num = fps, den = 1 };
		enc->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
		enc->gop_size = Math.Max(1, fps / 2);
		enc->max_b_frames = 0;
		enc->bit_rate = bitrate;
		enc->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
		enc->thread_count = 1;
		if (enc->priv_data != null) {
			ffmpeg.av_opt_set(enc->priv_data, "preset", "ultrafast", 0);
			ffmpeg.av_opt_set(enc->priv_data, "tune", "zerolatency", 0);
			ffmpeg.av_opt_set(enc->priv_data, "profile", "baseline", 0);
			ffmpeg.av_opt_set(enc->priv_data, "repeat-headers", "1", 0);
		}
		ffmpeg.avcodec_open2(enc, codec, null).ThrowIfError("avcodec_open2 x264");
		frame = ffmpeg.av_frame_alloc();
		frame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
		frame->width = w;
		frame->height = h;
		ffmpeg.av_frame_get_buffer(frame, 32).ThrowIfError("frame_get_buffer");
		pkt = ffmpeg.av_packet_alloc();
		sws = ffmpeg.sws_getContext(w, h, AVPixelFormat.AV_PIX_FMT_BGRA,
			w, h, AVPixelFormat.AV_PIX_FMT_YUV420P, ffmpeg.SWS_BILINEAR, null, null, null);
		if (sws == null) throw new InvalidOperationException("sws_getContext 失败");
	}

	public void ForceKey() => Interlocked.Exchange(ref force, 1);

	public byte[] Encode(byte[] bgra) {
		if (disposed || bgra == null || bgra.Length < w * h * 4) return null;
		ffmpeg.av_frame_make_writable(frame).ThrowIfError("frame_writable");
		fixed (byte* pSrc = bgra) {
			var srcSlice = new byte_ptrArray8();
			srcSlice[0] = pSrc;
			var srcStride = new int_array8();
			srcStride[0] = w * 4;
			ffmpeg.sws_scale(sws, srcSlice, srcStride, 0, h, frame->data, frame->linesize);
		}
		frame->pts = pts++;
		if (Interlocked.Exchange(ref force, 0) != 0)
			frame->pict_type = AVPictureType.AV_PICTURE_TYPE_I;
		else
			frame->pict_type = AVPictureType.AV_PICTURE_TYPE_NONE;
		ffmpeg.avcodec_send_frame(enc, frame).ThrowIfError("send_frame");
		var r = ffmpeg.avcodec_receive_packet(enc, pkt);
		if (r < 0) return null;
		var nal = new byte[pkt->size];
		Marshal.Copy((IntPtr)pkt->data, nal, 0, pkt->size);
		ffmpeg.av_packet_unref(pkt);
		note(nal);
		return nal;
	}

	void note(byte[] annex) {
		var i = 0;
		while (i + 4 < annex.Length) {
			var sc = 0;
			if (annex[i] == 0 && annex[i + 1] == 0 && annex[i + 2] == 0 && annex[i + 3] == 1) sc = 4;
			else if (annex[i] == 0 && annex[i + 1] == 0 && annex[i + 2] == 1) sc = 3;
			if (sc == 0) { i++; continue; }
			var nal = i + sc;
			if (nal >= annex.Length) break;
			var typ = annex[nal] & 0x1F;
			var next = nal + 1;
			while (next + 3 < annex.Length) {
				if (annex[next] == 0 && annex[next + 1] == 0 && (annex[next + 2] == 1 || (annex[next + 2] == 0 && annex[next + 3] == 1)))
					break;
				next++;
			}
			if (next + 3 >= annex.Length) next = annex.Length;
			if (typ == 7 || typ == 8) {
				var wrapped = new byte[4 + (next - nal)];
				wrapped[3] = 1;
				Buffer.BlockCopy(annex, nal, wrapped, 4, next - nal);
				if (typ == 7) Sps = wrapped;
				else Pps = wrapped;
			}
			i = next;
		}
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
