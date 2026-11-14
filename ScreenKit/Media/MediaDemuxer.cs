using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace ScreenKit;

/// <summary>一个已打开的媒体文件：容器 + 音视频流索引 + 时长。只做解封装，不建解码器。</summary>
unsafe sealed class MediaDemuxer : IDisposable {
	AVFormatContext* fmt;
	AVPacket* pkt;
	bool disposed;

	public string Path { get; }
	public double DurationSec { get; private set; }
	public int VideoIndex { get; private set; } = -1;
	public int AudioIndex { get; private set; } = -1;
	public int Width { get; private set; }
	public int Height { get; private set; }
	public double Fps { get; private set; }
	public int Rotation { get; private set; }
	public string VideoCodec { get; private set; } = "";
	public string AudioCodec { get; private set; } = "";
	public int SampleRate { get; private set; }
	public int Channels { get; private set; }
	public bool HasVideo => VideoIndex >= 0;
	public bool HasAudio => AudioIndex >= 0;

	public MediaDemuxer(string path) {
		Path = path;
		if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
			throw new System.IO.FileNotFoundException("媒体文件不存在", path);
		if (!FfmpegLoader.TryInit(out var err))
			throw new InvalidOperationException(err ?? "FFmpeg 未就绪");
		var local = fmt;
		if (ffmpeg.avformat_open_input(&local, path, null, null) != 0 || local == null) {
			if (local != null) { var f = local; ffmpeg.avformat_close_input(&f); }
			throw new InvalidOperationException("无法打开媒体文件");
		}
		fmt = local;
		try {
			if (ffmpeg.avformat_find_stream_info(fmt, null) < 0)
				throw new InvalidOperationException("读取流信息失败");
			probe();
			pkt = ffmpeg.av_packet_alloc();
			if (pkt == null) throw new InvalidOperationException("av_packet_alloc 失败");
		}
		catch {
			if (fmt != null) { var f = fmt; ffmpeg.avformat_close_input(&f); fmt = null; }
			throw;
		}
	}

	void probe() {
		if (fmt->duration > 0) DurationSec = fmt->duration / (double)ffmpeg.AV_TIME_BASE;
		for (uint i = 0; i < fmt->nb_streams; i++) {
			var st = fmt->streams[i];
			if (st == null || st->codecpar == null) continue;
			var par = st->codecpar;
			if (par->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO && VideoIndex < 0) {
				VideoIndex = (int)i;
				VideoCodec = ffmpeg.avcodec_get_name(par->codec_id);
				Width = par->width;
				Height = par->height;
				if (st->avg_frame_rate.den > 0 && st->avg_frame_rate.num > 0)
					Fps = (double)st->avg_frame_rate.num / st->avg_frame_rate.den;
				else if (st->r_frame_rate.den > 0 && st->r_frame_rate.num > 0)
					Fps = (double)st->r_frame_rate.num / st->r_frame_rate.den;
				Rotation = readrotation(st, par);
			}
			else if (par->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO && AudioIndex < 0) {
				AudioIndex = (int)i;
				AudioCodec = ffmpeg.avcodec_get_name(par->codec_id);
				SampleRate = par->sample_rate;
				Channels = par->channels;
			}
		}
	}

	/// <summary>手机竖拍视频靠 stream 元数据 tag 表示旋转，90/270 时显示宽高要对调。</summary>
	static int readrotation(AVStream* st, AVCodecParameters* par) {
		try {
			if (st != null && st->metadata != null) {
				var e = ffmpeg.av_dict_get(st->metadata, "rotate", null, 0);
				if (e != null) {
					var s = Marshal.PtrToStringAnsi((IntPtr)e->value) ?? "";
					if (int.TryParse(s, out var deg)) {
						deg %= 360;
						if (deg < 0) deg += 360;
						return deg;
					}
				}
			}
		}
		catch { }
		return 0;
	}

	public AVCodecParameters* VideoPar => VideoIndex >= 0 ? fmt->streams[VideoIndex]->codecpar : null;
	public AVCodecParameters* AudioPar => AudioIndex >= 0 ? fmt->streams[AudioIndex]->codecpar : null;
	public AVRational VideoTimeBase => VideoIndex >= 0 ? fmt->streams[VideoIndex]->time_base : new AVRational { num = 1, den = 1000 };
	public AVRational AudioTimeBase => AudioIndex >= 0 ? fmt->streams[AudioIndex]->time_base : new AVRational { num = 1, den = 1000 };

	/// <summary>读下一个包；返回 false 表示文件结束或出错。包由调用方负责 unref。</summary>
	public bool ReadPacket(out AVPacket* p) {
		p = null;
		if (disposed || fmt == null || pkt == null) return false;
		while (true) {
			var r = ffmpeg.av_read_frame(fmt, pkt);
			if (r < 0) return false;
			// 只要音视频流，字幕/数据流直接丢弃
			if (pkt->stream_index != VideoIndex && pkt->stream_index != AudioIndex) {
				ffmpeg.av_packet_unref(pkt);
				continue;
			}
			p = pkt;
			return true;
		}
	}

	/// <summary>seek 到指定秒。返回实际落点（可能因关键帧偏移）。</summary>
	public double Seek(double sec) {
		if (disposed || fmt == null) return 0;
		if (sec < 0) sec = 0;
		if (DurationSec > 0 && sec > DurationSec) sec = DurationSec;
		var ts = (long)(sec * ffmpeg.AV_TIME_BASE);
		if (ffmpeg.av_seek_frame(fmt, -1, ts, ffmpeg.AVSEEK_FLAG_BACKWARD) < 0) {
			// 全流 seek 失败时退回视频流定位
			if (VideoIndex >= 0) {
				var vts = (long)(sec / ffmpeg.av_q2d(VideoTimeBase));
				ffmpeg.av_seek_frame(fmt, VideoIndex, vts, ffmpeg.AVSEEK_FLAG_BACKWARD);
			}
		}
		// 换流后缓冲的包必须清掉
		if (pkt != null) ffmpeg.av_packet_unref(pkt);
		return sec;
	}

	/// <summary>读容器元数据（标题、艺术家），供播放列表显示。</summary>
	public void ReadTags(out string title, out string artist) {
		title = "";
		artist = "";
		if (fmt == null || fmt->metadata == null) return;
		try {
			AVDictionaryEntry* e = null;
			while ((e = ffmpeg.av_dict_get(fmt->metadata, "", e, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null) {
				var k = Marshal.PtrToStringAnsi((IntPtr)e->key) ?? "";
				var v = Marshal.PtrToStringAnsi((IntPtr)e->value) ?? "";
				if (k.Equals("title", StringComparison.OrdinalIgnoreCase) && title.Length == 0) title = v;
				else if (k.Equals("artist", StringComparison.OrdinalIgnoreCase) && artist.Length == 0) artist = v;
			}
		}
		catch { }
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		if (pkt != null) { var p = pkt; ffmpeg.av_packet_free(&p); pkt = null; }
		if (fmt != null) { var f = fmt; ffmpeg.avformat_close_input(&f); fmt = null; }
	}
}
