using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FFmpeg.AutoGen;

namespace ScreenKit;

/// <summary>用安装功能放进 ffmpeg64 的 shared DLL 读容器和流的参数，不调用外部 ffmpeg.exe。</summary>
static partial class FileProbe {
	static unsafe bool probeav(Report rep, string path) {
		if (!FfmpegLoader.TryInit(out _)) return false;
		AVFormatContext* fmt = null;
		if (ffmpeg.avformat_open_input(&fmt, path, null, null) != 0 || fmt == null) {
			if (fmt != null) ffmpeg.avformat_close_input(&fmt);
			return false;
		}
		try {
			if (ffmpeg.avformat_find_stream_info(fmt, null) < 0) return false;
			var m = new MediaFacts();
			m.Streams = (int)fmt->nb_streams;
			if (fmt->iformat != null)
				m.Container = Marshal.PtrToStringAnsi((IntPtr)fmt->iformat->name) ?? "";
			if (fmt->duration > 0)
				m.Dur = durms((fmt->duration / (double)ffmpeg.AV_TIME_BASE).ToString(CultureInfo.InvariantCulture));
			for (uint i = 0; i < fmt->nb_streams; i++) {
				var st = fmt->streams[i];
				if (st == null || st->codecpar == null) continue;
				var par = st->codecpar;
				if (par->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO && m.VCodec.Length == 0) {
					m.VCodec = ffmpeg.avcodec_get_name(par->codec_id);
					if (par->width > 0 && par->height > 0) {
						m.Vw = par->width.ToString(CultureInfo.InvariantCulture);
						m.Vh = par->height.ToString(CultureInfo.InvariantCulture);
					}
					m.PixFmt = ffmpeg.av_get_pix_fmt_name((AVPixelFormat)par->format) ?? "";
					m.VProfile = ffmpeg.avcodec_profile_name(par->codec_id, par->profile) ?? "";
					if (par->level > 0) m.VLevel = levelfmt(par->level.ToString(CultureInfo.InvariantCulture));
					m.Color = colorspace(par->color_space);
					m.Field = fieldname(par->field_order);
					if (st->avg_frame_rate.den > 0 && st->avg_frame_rate.num > 0)
						m.Fps = ((double)st->avg_frame_rate.num / st->avg_frame_rate.den)
							.ToString("0.###", CultureInfo.InvariantCulture);
					if (par->bit_rate > 0) m.VRate = formatbps(par->bit_rate.ToString(CultureInfo.InvariantCulture));
					if (st->nb_frames > 0) m.Frames = st->nb_frames.ToString(CultureInfo.InvariantCulture);
					if (par->width > 0 && par->height > 0
						&& par->sample_aspect_ratio.num > 0 && par->sample_aspect_ratio.den > 0)
						m.Dar = ratio((long)par->width * par->sample_aspect_ratio.num,
							(long)par->height * par->sample_aspect_ratio.den);
				}
				else if (par->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO && m.ACodec.Length == 0) {
					m.ACodec = ffmpeg.avcodec_get_name(par->codec_id);
					m.AProfile = ffmpeg.avcodec_profile_name(par->codec_id, par->profile) ?? "";
					if (par->sample_rate > 0) m.Sample = par->sample_rate.ToString(CultureInfo.InvariantCulture);
					m.Channels = par->channels.ToString(CultureInfo.InvariantCulture);
					m.Layout = channelname(par->channels, par->channel_layout);
					var bits = par->bits_per_raw_sample > 0 ? par->bits_per_raw_sample : par->bits_per_coded_sample;
					if (bits > 0) m.Bits = bits.ToString(CultureInfo.InvariantCulture);
					if (par->bit_rate > 0) m.ARate = formatbps(par->bit_rate.ToString(CultureInfo.InvariantCulture));
				}
			}
			mediarows(rep, m);
			return true;
		}
		catch { return false; }
		finally {
			ffmpeg.avformat_close_input(&fmt);
		}
	}

	static string channelname(int channels, ulong layout) {
		var buf = new byte[64];
		unsafe {
			fixed (byte* p = buf) ffmpeg.av_get_channel_layout_string(p, buf.Length, channels, layout);
		}
		return Encoding.ASCII.GetString(buf).TrimEnd('\0');
	}

	static string colorspace(AVColorSpace cs) {
		switch (cs) {
			case AVColorSpace.AVCOL_SPC_BT709: return "bt709";
			case AVColorSpace.AVCOL_SPC_BT470BG: return "bt470bg";
			case AVColorSpace.AVCOL_SPC_SMPTE170M: return "smpte170m";
			case AVColorSpace.AVCOL_SPC_SMPTE240M: return "smpte240m";
			case AVColorSpace.AVCOL_SPC_BT2020_NCL: return "bt2020nc";
			case AVColorSpace.AVCOL_SPC_BT2020_CL: return "bt2020c";
			default: return "";
		}
	}

	static string fieldname(AVFieldOrder fo) {
		switch (fo) {
			case AVFieldOrder.AV_FIELD_PROGRESSIVE: return "progressive";
			case AVFieldOrder.AV_FIELD_TT: return "tt";
			case AVFieldOrder.AV_FIELD_BB: return "bb";
			case AVFieldOrder.AV_FIELD_BT: return "bt";
			case AVFieldOrder.AV_FIELD_TB: return "tb";
			default: return "";
		}
	}

	static string ratio(long w, long h) {
		if (w <= 0 || h <= 0) return "";
		var a = w;
		var b = h;
		while (b != 0) {
			var t = a % b;
			a = b;
			b = t;
		}
		if (a <= 0) return "";
		return $"{w / a}:{h / a}";
	}
}
