using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace ScreenKit;

unsafe sealed class CastAudioEncoder : IDisposable {
	AVCodecContext* enc;
	AVFrame* frame;
	AVPacket* pkt;
	SwrContext* swr;
	byte[] leftover;
	int leftoverN;
	bool disposed;
	readonly int inRate, inCh;
	public int FrameSamples => enc != null ? enc->frame_size : 1024;

	public CastAudioEncoder(int inRate = 48000, int inCh = 2) {
		if (!FfmpegLoader.TryInit(out var err))
			throw new InvalidOperationException(err ?? "FFmpeg 未就绪");
		this.inRate = inRate;
		this.inCh = inCh;
		var codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_AAC);
		if (codec == null) throw new InvalidOperationException("找不到 AAC 编码器");
		enc = ffmpeg.avcodec_alloc_context3(codec);
		enc->sample_rate = 48000;
		enc->channel_layout = (ulong)ffmpeg.AV_CH_LAYOUT_STEREO;
		enc->channels = 2;
		enc->sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_FLTP;
		enc->bit_rate = 128000;
		enc->time_base = new AVRational { num = 1, den = 48000 };
		ffmpeg.avcodec_open2(enc, codec, null).Check("avcodec_open2 aac");
		if (enc->frame_size <= 0) enc->frame_size = 1024;
		frame = ffmpeg.av_frame_alloc();
		frame->nb_samples = enc->frame_size;
		frame->format = (int)enc->sample_fmt;
		frame->channel_layout = enc->channel_layout;
		frame->sample_rate = enc->sample_rate;
		ffmpeg.av_frame_get_buffer(frame, 0).Check("aac frame buf");
		pkt = ffmpeg.av_packet_alloc();
		swr = ffmpeg.swr_alloc_set_opts(null,
			(long)enc->channel_layout, enc->sample_fmt, enc->sample_rate,
			inCh <= 1 ? ffmpeg.AV_CH_LAYOUT_MONO : ffmpeg.AV_CH_LAYOUT_STEREO,
			AVSampleFormat.AV_SAMPLE_FMT_S16, inRate, 0, null);
		ffmpeg.swr_init(swr).Check("swr aac");
		leftover = new byte[enc->frame_size * inCh * 2 * 4];
	}

	public byte[] EncodeS16(byte[] pcm, int nbytes) {
		if (disposed || pcm == null || nbytes <= 0) return null;
		if (leftoverN + nbytes > leftover.Length) {
			var nbuf = new byte[leftoverN + nbytes + 4096];
			Buffer.BlockCopy(leftover, 0, nbuf, 0, leftoverN);
			leftover = nbuf;
		}
		Buffer.BlockCopy(pcm, 0, leftover, leftoverN, nbytes);
		leftoverN += nbytes;
		var need = enc->frame_size * inCh * 2;
		if (leftoverN < need) return null;
		var chunk = new byte[need];
		Buffer.BlockCopy(leftover, 0, chunk, 0, need);
		leftoverN -= need;
		if (leftoverN > 0) Buffer.BlockCopy(leftover, need, leftover, 0, leftoverN);
		ffmpeg.av_frame_make_writable(frame).Check("aac writable");
		fixed (byte* sp = chunk) {
			byte* sptr = sp;
			ffmpeg.swr_convert(swr, frame->extended_data, enc->frame_size, &sptr, enc->frame_size);
		}
		var e = ffmpeg.avcodec_send_frame(enc, frame);
		if (e < 0) return null;
		var r = ffmpeg.avcodec_receive_packet(enc, pkt);
		if (r < 0) return null;
		var raw = new byte[pkt->size];
		Marshal.Copy((IntPtr)pkt->data, raw, 0, pkt->size);
		ffmpeg.av_packet_unref(pkt);
		return addadts(raw, enc->sample_rate, enc->channels);
	}

	static byte[] addadts(byte[] aac, int rate, int ch) {
		int packetLen = aac.Length + 7;
		var p = new byte[packetLen];
		int srIdx = rate >= 48000 ? 3 : 4;
		p[0] = 0xFF;
		p[1] = 0xF9;
		p[2] = (byte)(((2 - 1) << 6) + (srIdx << 2) + (ch >> 2));
		p[3] = (byte)(((ch & 3) << 6) + (packetLen >> 11));
		p[4] = (byte)((packetLen & 0x7FF) >> 3);
		p[5] = (byte)(((packetLen & 7) << 5) + 0x1F);
		p[6] = 0xFC;
		Buffer.BlockCopy(aac, 0, p, 7, aac.Length);
		return p;
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		if (swr != null) { var s = swr; ffmpeg.swr_free(&s); swr = null; }
		if (pkt != null) { var p = pkt; ffmpeg.av_packet_free(&p); pkt = null; }
		if (frame != null) { var f = frame; ffmpeg.av_frame_free(&f); frame = null; }
		if (enc != null) { var e = enc; ffmpeg.avcodec_free_context(&e); enc = null; }
	}
}
