using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace ScreenKit;

unsafe sealed class CastAudioDecoder : IDisposable {
	AVCodecContext* dec;
	AVFrame* frame;
	AVPacket* pkt;
	SwrContext* swr;
	bool disposed;
	public int SampleRate { get; private set; } = 48000;
	public int Channels { get; private set; } = 2;

	public CastAudioDecoder() {
		if (!FfmpegLoader.TryInit(out var err))
			throw new InvalidOperationException(err ?? "FFmpeg 未就绪");
		var codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_AAC);
		if (codec == null) throw new InvalidOperationException("无 AAC 解码器");
		dec = ffmpeg.avcodec_alloc_context3(codec);
		ffmpeg.avcodec_open2(dec, codec, null).Check("avcodec_open2 aac");
		frame = ffmpeg.av_frame_alloc();
		pkt = ffmpeg.av_packet_alloc();
	}

	public byte[] DecodeAdts(byte[] adts) {
		if (adts == null || adts.Length == 0 || disposed) return null;
		try {
			ffmpeg.av_packet_unref(pkt);
			if (ffmpeg.av_new_packet(pkt, adts.Length) < 0) return null;
			Marshal.Copy(adts, 0, (IntPtr)pkt->data, adts.Length);
			var e = ffmpeg.avcodec_send_packet(dec, pkt);
			ffmpeg.av_packet_unref(pkt);
			if (e < 0 && e != ffmpeg.AVERROR(ffmpeg.EAGAIN)) return null;
			var r = ffmpeg.avcodec_receive_frame(dec, frame);
			if (r < 0) return null;
			ensureswr();
			if (swr == null) return null;
			var nb = frame->nb_samples;
			if (nb <= 0) return null;
			var outSamples = ffmpeg.swr_get_out_samples(swr, nb);
			if (outSamples <= 0) outSamples = nb;
			var bytes = outSamples * Channels * 2;
			if (bytes <= 0 || bytes > 1_000_000) return null;
			var pcm = new byte[bytes];
			fixed (byte* dp = pcm) {
				byte* dptr = dp;
				var n = ffmpeg.swr_convert(swr, &dptr, outSamples, frame->extended_data, nb);
				if (n < 0) return null;
				var got = n * Channels * 2;
				if (got != pcm.Length) {
					var trim = new byte[got];
					Buffer.BlockCopy(pcm, 0, trim, 0, got);
					return trim;
				}
			}
			return pcm;
		}
		catch {
			return null;
		}
	}

	void ensureswr() {
		if (swr != null) return;
		SampleRate = frame->sample_rate > 0 ? frame->sample_rate : 48000;
		var inLayout = frame->channel_layout != 0
			? frame->channel_layout
			: (ulong)(frame->channels <= 1 ? ffmpeg.AV_CH_LAYOUT_MONO : ffmpeg.AV_CH_LAYOUT_STEREO);
		Channels = 2;
		swr = ffmpeg.swr_alloc_set_opts(null,
			ffmpeg.AV_CH_LAYOUT_STEREO, AVSampleFormat.AV_SAMPLE_FMT_S16, SampleRate,
			(long)inLayout, (AVSampleFormat)frame->format, frame->sample_rate > 0 ? frame->sample_rate : SampleRate,
			0, null);
		if (swr == null) throw new InvalidOperationException("swr_alloc 失败");
		ffmpeg.swr_init(swr).Check("swr_init");
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		if (swr != null) { var s = swr; ffmpeg.swr_free(&s); swr = null; }
		if (pkt != null) { var p = pkt; ffmpeg.av_packet_free(&p); pkt = null; }
		if (frame != null) { var f = frame; ffmpeg.av_frame_free(&f); frame = null; }
		if (dec != null) { var d = dec; ffmpeg.avcodec_free_context(&d); dec = null; }
	}
}
