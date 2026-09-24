using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace ScreenKit;

unsafe sealed class CastVideoDecoder : IDisposable {
	AVCodecContext* dec;
	AVFrame* frame;
	AVPacket* pkt;
	SwsContext* sws;
	int swsW, swsH;
	byte[] bgra;
	int stride;
	bool disposed;

	public int Width { get; private set; }
	public int Height { get; private set; }

	public CastVideoDecoder() {
		if (!FfmpegLoader.TryInit(out var err))
			throw new InvalidOperationException(err ?? "FFmpeg 未就绪");
		var codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
		if (codec == null) throw new InvalidOperationException("无 H.264 解码器");
		dec = ffmpeg.avcodec_alloc_context3(codec);
		if (dec == null) throw new InvalidOperationException("avcodec_alloc_context3 失败");
		dec->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
		ffmpeg.avcodec_open2(dec, codec, null).Check("avcodec_open2 h264");
		frame = ffmpeg.av_frame_alloc();
		pkt = ffmpeg.av_packet_alloc();
		if (frame == null || pkt == null) throw new InvalidOperationException("frame/packet alloc 失败");
	}

	public bool Decode(byte[] nal, out byte[] pixels, out int w, out int h, out int st) {
		pixels = null; w = 0; h = 0; st = 0;
		if (nal == null || nal.Length == 0 || disposed) return false;
		try {
			ffmpeg.av_packet_unref(pkt);
			if (ffmpeg.av_new_packet(pkt, nal.Length) < 0) return false;
			Marshal.Copy(nal, 0, (IntPtr)pkt->data, nal.Length);
			var e = ffmpeg.avcodec_send_packet(dec, pkt);
			ffmpeg.av_packet_unref(pkt);
			if (e < 0 && e != ffmpeg.AVERROR(ffmpeg.EAGAIN)) return false;
			var r = ffmpeg.avcodec_receive_frame(dec, frame);
			if (r == ffmpeg.AVERROR(ffmpeg.EAGAIN) || r == ffmpeg.AVERROR_EOF) return false;
			if (r < 0) return false;
			w = frame->width;
			h = frame->height;
			if (w <= 0 || h <= 0 || w > 4096 || h > 4096) return false;
			if (frame->format < 0) return false;
			Width = w; Height = h;
			ensuresws(w, h);
			if (sws == null || bgra == null) return false;
			var dst = new byte_ptrArray8();
			var dstSt = new int_array8();
			fixed (byte* dp = bgra) {
				dst[0] = dp;
				dstSt[0] = stride;
				ffmpeg.sws_scale(sws, frame->data, frame->linesize, 0, h, dst, dstSt);
			}
			pixels = bgra;
			st = stride;
			return true;
		}
		catch {
			return false;
		}
	}

	void ensuresws(int w, int h) {
		if (sws != null && swsW == w && swsH == h) return;
		if (sws != null) { ffmpeg.sws_freeContext(sws); sws = null; }
		swsW = w; swsH = h;
		stride = w * 4;
		bgra = new byte[stride * h];
		sws = ffmpeg.sws_getContext(w, h, (AVPixelFormat)frame->format,
			w, h, AVPixelFormat.AV_PIX_FMT_BGRA, ffmpeg.SWS_FAST_BILINEAR, null, null, null);
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		if (sws != null) { ffmpeg.sws_freeContext(sws); sws = null; }
		if (pkt != null) { var p = pkt; ffmpeg.av_packet_free(&p); pkt = null; }
		if (frame != null) { var f = frame; ffmpeg.av_frame_free(&f); frame = null; }
		if (dec != null) { var d = dec; ffmpeg.avcodec_free_context(&d); dec = null; }
	}
}
