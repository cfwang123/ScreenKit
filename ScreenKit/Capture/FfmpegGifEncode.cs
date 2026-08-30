using System.IO;
using FFmpeg.AutoGen;

namespace ScreenKit;

/// <summary>
/// 将无声视频转成 GIF：解码 → 缩放 → 调色板量化 → GIF（不依赖 avfilter/postproc）。
/// </summary>
static unsafe class FfmpegGifEncode {
	const int PALETTE_SIZE = 1024; // AVPALETTE_SIZE

	/// <summary>
	/// 从临时 MP4 生成 GIF。
	/// <paramref name="outFps"/> 输出帧率；<paramref name="srcFps"/> 源视频帧率（默认 24）。
	/// 输出低于源帧率时按时间轴抽帧，保持时长。
	/// </summary>
	public static void FromVideo(string videoPath, string gifPath, int outW, int outH, int outFps, int colors,
		int srcFps = GifOptions.CaptureFps) {
		if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
			throw new FileNotFoundException("视频不存在", videoPath);
		if (!FfmpegLoader.TryInit(out var err))
			throw new InvalidOperationException(err ?? "FFmpeg 未就绪");

		outW = Math.Max(16, outW);
		outH = Math.Max(16, outH);
		srcFps = Compat.Clamp(srcFps, 1, 60);
		outFps = Compat.Clamp(outFps, 1, srcFps);
		colors = Compat.Clamp(colors, 2, 256);

		var dir = Path.GetDirectoryName(gifPath);
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		try { if (File.Exists(gifPath)) File.Delete(gifPath); } catch { }

		// 先抽样建调色板，再第二遍编码（避免整段帧进内存）
		var samples = sampleBgra(videoPath, outW, outH, maxFrames: 48);
		if (samples.Count == 0)
			throw new InvalidOperationException("视频无有效帧");
		var palette = buildpalette(samples, colors);
		foreach (var s in samples) s.Dispose();
		samples.Clear();

		encodewithpalette(videoPath, gifPath, outW, outH, outFps, srcFps, palette, colors);
		if (!File.Exists(gifPath) || new FileInfo(gifPath).Length < 32)
			throw new InvalidOperationException("GIF 未写出或过小");
		RecordLog.Step("gif_encode",
			$"ok colors={colors} out={outW}x{outH}@{outFps} (src={srcFps}) size={new FileInfo(gifPath).Length}");
	}

	static List<BgraFrame> sampleBgra(string videoPath, int outW, int outH, int maxFrames) {
		var list = new List<BgraFrame>();
		foreachframe(videoPath, outW, outH, (bgra, stride, pts) => {
			if (list.Count >= maxFrames) return false;
			// 均匀抽样：前几帧全收；之后每隔几帧
			if (list.Count >= 8 && pts % 3 != 0) return true;
			var copy = new byte[stride * outH];
			Buffer.BlockCopy(bgra, 0, copy, 0, copy.Length);
			list.Add(new BgraFrame { Data = copy, Stride = stride, W = outW, H = outH });
			return true;
		});
		return list;
	}

	static void encodewithpalette(string videoPath, string gifPath, int outW, int outH, int outFps, int srcFps,
		uint[] palette, int colors) {
		AVFormatContext* ofmt = null;
		AVCodecContext* enc = null;
		AVStream* ostream = null;
		AVFrame* frame = null;
		AVPacket* opkt = null;
		colors = Compat.Clamp(colors, 2, 256);
		outFps = Compat.Clamp(outFps, 1, Math.Max(1, srcFps));
		srcFps = Compat.Clamp(srcFps, 1, 60);
		try {
			AVFormatContext* of = null;
			ffmpeg.avformat_alloc_output_context2(&of, null, "gif", gifPath).ThrowIfError("alloc_gif");
			ofmt = of;
			var gifEnc = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_GIF);
			if (gifEnc == null) throw new InvalidOperationException("找不到 GIF 编码器");
			ostream = ffmpeg.avformat_new_stream(ofmt, null);
			if (ostream == null) throw new InvalidOperationException("new_stream 失败");
			enc = ffmpeg.avcodec_alloc_context3(gifEnc);
			enc->codec_id = AVCodecID.AV_CODEC_ID_GIF;
			enc->width = outW;
			enc->height = outH;
			enc->time_base = new AVRational { num = 1, den = outFps };
			enc->framerate = new AVRational { num = outFps, den = 1 };
			enc->pix_fmt = AVPixelFormat.AV_PIX_FMT_PAL8;
			if ((ofmt->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
				enc->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
			ffmpeg.avcodec_open2(enc, gifEnc, null).ThrowIfError("open_gif_enc");
			ffmpeg.avcodec_parameters_from_context(ostream->codecpar, enc).ThrowIfError("params_from");
			ostream->time_base = enc->time_base;
			if (ofmt->priv_data != null)
				ffmpeg.av_opt_set_int(ofmt->priv_data, "loop", 0, 0);
			if ((ofmt->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0) {
				AVIOContext* io = null;
				ffmpeg.avio_open(&io, gifPath, ffmpeg.AVIO_FLAG_WRITE).ThrowIfError("avio_open");
				ofmt->pb = io;
			}
			ffmpeg.avformat_write_header(ofmt, null).ThrowIfError("write_header");

			frame = ffmpeg.av_frame_alloc();
			frame->format = (int)AVPixelFormat.AV_PIX_FMT_PAL8;
			frame->width = outW;
			frame->height = outH;
			ffmpeg.av_frame_get_buffer(frame, 32).ThrowIfError("frame_buffer");
			if (frame->data[1] == null)
				throw new InvalidOperationException("PAL8 无调色板缓冲");
			fixed (uint* pPal = palette) {
				Buffer.MemoryCopy(pPal, frame->data[1], PALETTE_SIZE, Math.Min(PALETTE_SIZE, palette.Length * 4));
			}

			opkt = ffmpeg.av_packet_alloc();
			long outPts = 0;
			double nextSrc = 0;
			var step = (double)srcFps / outFps; // 源帧步进
			foreachframe(videoPath, outW, outH, (bgra, stride, srcIdx) => {
				if (srcIdx + 1e-6 < nextSrc)
					return true; // 抽帧丢弃
				nextSrc += step;

				ffmpeg.av_frame_make_writable(frame).ThrowIfError("writable");
				fixed (uint* pPal = palette) {
					Buffer.MemoryCopy(pPal, frame->data[1], PALETTE_SIZE, Math.Min(PALETTE_SIZE, palette.Length * 4));
				}
				maptopal8(bgra, stride, outW, outH, palette, colors, frame->data[0], frame->linesize[0]);
				frame->pts = outPts++;
				ffmpeg.avcodec_send_frame(enc, frame).ThrowIfError("send_frame");
				while (true) {
					var er = ffmpeg.avcodec_receive_packet(enc, opkt);
					if (er == ffmpeg.AVERROR(ffmpeg.EAGAIN) || er == ffmpeg.AVERROR_EOF) break;
					er.ThrowIfError("receive_packet");
					ffmpeg.av_packet_rescale_ts(opkt, enc->time_base, ostream->time_base);
					opkt->stream_index = ostream->index;
					ffmpeg.av_interleaved_write_frame(ofmt, opkt).ThrowIfError("write_frame");
					ffmpeg.av_packet_unref(opkt);
				}
				return true;
			});

			ffmpeg.avcodec_send_frame(enc, null);
			while (true) {
				var er = ffmpeg.avcodec_receive_packet(enc, opkt);
				if (er == ffmpeg.AVERROR_EOF || er == ffmpeg.AVERROR(ffmpeg.EAGAIN)) break;
				er.ThrowIfError("flush");
				ffmpeg.av_packet_rescale_ts(opkt, enc->time_base, ostream->time_base);
				opkt->stream_index = ostream->index;
				ffmpeg.av_interleaved_write_frame(ofmt, opkt).ThrowIfError("write_frame");
				ffmpeg.av_packet_unref(opkt);
			}
			ffmpeg.av_write_trailer(ofmt);
		}
		finally {
			if (opkt != null) { var p = opkt; ffmpeg.av_packet_free(&p); }
			if (frame != null) { var f = frame; ffmpeg.av_frame_free(&f); }
			if (enc != null) { var c = enc; ffmpeg.avcodec_free_context(&c); }
			if (ofmt != null) {
				if ((ofmt->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0 && ofmt->pb != null)
					ffmpeg.avio_closep(&ofmt->pb);
				var o = ofmt;
				ffmpeg.avformat_free_context(o);
			}
		}
	}

	delegate bool FrameHandler(byte[] bgra, int stride, long pts);

	static void foreachframe(string videoPath, int outW, int outH, FrameHandler onFrame) {
		AVFormatContext* ifmt = null;
		AVCodecContext* dec = null;
		AVFrame* frame = null;
		AVPacket* ipkt = null;
		SwsContext* sws = null;
		AVFrame* rgb = null;
		var vIdx = -1;
		try {
			ffmpeg.avformat_open_input(&ifmt, videoPath, null, null).ThrowIfError("open_input");
			ffmpeg.avformat_find_stream_info(ifmt, null).ThrowIfError("find_stream_info");
			for (uint i = 0; i < ifmt->nb_streams; i++) {
				if (ifmt->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO) {
					vIdx = (int)i;
					break;
				}
			}
			if (vIdx < 0) throw new InvalidOperationException("无视频流");

			var ist = ifmt->streams[vIdx];
			var decoder = ffmpeg.avcodec_find_decoder(ist->codecpar->codec_id);
			if (decoder == null) throw new InvalidOperationException("无视频解码器");
			dec = ffmpeg.avcodec_alloc_context3(decoder);
			ffmpeg.avcodec_parameters_to_context(dec, ist->codecpar).ThrowIfError("params_to_ctx");
			ffmpeg.avcodec_open2(dec, decoder, null).ThrowIfError("open_decoder");

			var srcW = dec->width;
			var srcH = dec->height;
			sws = ffmpeg.sws_getContext(
				srcW, srcH, dec->pix_fmt,
				outW, outH, AVPixelFormat.AV_PIX_FMT_BGRA,
				ffmpeg.SWS_BILINEAR, null, null, null);
			if (sws == null) throw new InvalidOperationException("sws_getContext 失败");

			rgb = ffmpeg.av_frame_alloc();
			rgb->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
			rgb->width = outW;
			rgb->height = outH;
			ffmpeg.av_frame_get_buffer(rgb, 32).ThrowIfError("rgb buffer");
			frame = ffmpeg.av_frame_alloc();
			ipkt = ffmpeg.av_packet_alloc();
			long pts = 0;
			var cont = true;

			void handlereceived() {
				while (cont) {
					var r = ffmpeg.avcodec_receive_frame(dec, frame);
					if (r == ffmpeg.AVERROR(ffmpeg.EAGAIN) || r == ffmpeg.AVERROR_EOF) break;
					r.ThrowIfError("receive_frame");
					ffmpeg.av_frame_make_writable(rgb).ThrowIfError("rgb writable");
					ffmpeg.sws_scale(sws, frame->data, frame->linesize, 0, srcH,
						rgb->data, rgb->linesize);
					var stride = rgb->linesize[0];
					var bytes = new byte[Math.Abs(stride) * outH];
					fixed (byte* dst = bytes) {
						Buffer.MemoryCopy(rgb->data[0], dst, bytes.Length, bytes.Length);
					}
					cont = onFrame(bytes, Math.Abs(stride), pts++);
					ffmpeg.av_frame_unref(frame);
				}
			}

			while (cont && ffmpeg.av_read_frame(ifmt, ipkt) >= 0) {
				if (ipkt->stream_index == vIdx) {
					ffmpeg.avcodec_send_packet(dec, ipkt).ThrowIfError("send_packet");
					handlereceived();
				}
				ffmpeg.av_packet_unref(ipkt);
			}
			if (cont) {
				ffmpeg.avcodec_send_packet(dec, null);
				handlereceived();
			}
		}
		finally {
			if (ipkt != null) { var p = ipkt; ffmpeg.av_packet_free(&p); }
			if (frame != null) { var f = frame; ffmpeg.av_frame_free(&f); }
			if (rgb != null) { var f = rgb; ffmpeg.av_frame_free(&f); }
			if (sws != null) ffmpeg.sws_freeContext(sws);
			if (dec != null) { var c = dec; ffmpeg.avcodec_free_context(&c); }
			if (ifmt != null) { var f = ifmt; ffmpeg.avformat_close_input(&f); }
		}
	}

	/// <summary>BGRA 调色板，AV 格式为 0xAABBGGRR（小端写 uint）。</summary>
	static uint[] buildpalette(List<BgraFrame> frames, int colors) {
		colors = Compat.Clamp(colors, 2, 256);
		// 抽样像素
		var pixels = new List<uint>(8192);
		foreach (var f in frames) {
			var step = Math.Max(1, (f.W * f.H) / 2000);
			var i = 0;
			for (var y = 0; y < f.H; y++) {
				var row = y * f.Stride;
				for (var x = 0; x < f.W; x++, i++) {
					if (i % step != 0) continue;
					var o = row + x * 4;
					if (o + 3 >= f.Data.Length) continue;
					var b = f.Data[o];
					var g = f.Data[o + 1];
					var r = f.Data[o + 2];
					// FFmpeg RGB32 小端：0xAARRGGBB
					pixels.Add(0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b);
				}
			}
		}
		if (pixels.Count == 0) pixels.Add(0xFF000000u);

		var boxes = new List<ColorBox> { new(pixels) };
		while (boxes.Count < colors) {
			ColorBox best = null;
			var bestRange = -1;
			foreach (var b in boxes) {
				var rg = b.Range;
				if (rg > bestRange && b.Pixels.Count > 1) {
					bestRange = rg;
					best = b;
				}
			}
			if (best == null) break;
			boxes.Remove(best);
			best.Split(out var a, out var b2);
			boxes.Add(a);
			boxes.Add(b2);
		}

		var pal = new uint[256];
		for (var i = 0; i < 256; i++)
			pal[i] = 0xFF000000u;
		for (var i = 0; i < boxes.Count && i < 256; i++)
			pal[i] = boxes[i].Average;
		// 未用槽填最后一色，避免编码器读到脏数据
		var last = boxes.Count > 0 ? boxes[boxes.Count - 1].Average : 0xFF000000u;
		for (var i = boxes.Count; i < 256; i++)
			pal[i] = last;
		return pal;
	}

	static void maptopal8(byte[] bgra, int stride, int w, int h, uint[] palette, int colors,
		byte* dst, int dstStride) {
		var n = Compat.Clamp(colors, 2, 256);
		for (var y = 0; y < h; y++) {
			var srcRow = y * stride;
			var dstRow = dst + y * dstStride;
			for (var x = 0; x < w; x++) {
				var o = srcRow + x * 4;
				var b = bgra[o];
				var g = bgra[o + 1];
				var r = bgra[o + 2];
				var best = 0;
				var bestD = int.MaxValue;
				for (var i = 0; i < n; i++) {
					var p = palette[i];
					var pb = (int)(p & 0xFF);
					var pg = (int)((p >> 8) & 0xFF);
					var pr = (int)((p >> 16) & 0xFF);
					var dr = pr - r;
					var dg = pg - g;
					var db = pb - b;
					var d = dr * dr + dg * dg + db * db;
					if (d < bestD) {
						bestD = d;
						best = i;
						if (d == 0) break;
					}
				}
				dstRow[x] = (byte)best;
			}
		}
	}

	sealed class BgraFrame {
		public byte[] Data;
		public int Stride, W, H;
		public void Dispose() { Data = null; }
	}

	sealed class ColorBox {
		public List<uint> Pixels;
		public ColorBox(List<uint> px) { Pixels = px; }

		public int Range {
			get {
				byte minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
				foreach (var p in Pixels) {
					var b = (byte)(p & 0xFF);
					var g = (byte)((p >> 8) & 0xFF);
					var r = (byte)((p >> 16) & 0xFF);
					if (r < minR) minR = r; if (r > maxR) maxR = r;
					if (g < minG) minG = g; if (g > maxG) maxG = g;
					if (b < minB) minB = b; if (b > maxB) maxB = b;
				}
				return Math.Max(maxR - minR, Math.Max(maxG - minG, maxB - minB));
			}
		}

		public uint Average {
			get {
				long r = 0, g = 0, b = 0;
				foreach (var p in Pixels) {
					b += p & 0xFF;
					g += (p >> 8) & 0xFF;
					r += (p >> 16) & 0xFF;
				}
				var n = Math.Max(1, Pixels.Count);
				return 0xFF000000u | ((uint)(r / n) << 16) | ((uint)(g / n) << 8) | (uint)(b / n);
			}
		}

		public void Split(out ColorBox a, out ColorBox b) {
			byte minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
			foreach (var p in Pixels) {
				var bb = (byte)(p & 0xFF);
				var g = (byte)((p >> 8) & 0xFF);
				var r = (byte)((p >> 16) & 0xFF);
				if (r < minR) minR = r; if (r > maxR) maxR = r;
				if (g < minG) minG = g; if (g > maxG) maxG = g;
				if (bb < minB) minB = bb; if (bb > maxB) maxB = bb;
			}
			var rr = maxR - minR;
			var rg = maxG - minG;
			var rb = maxB - minB;
			int channel = 0; // R in high byte of RGB32
			if (rg >= rr && rg >= rb) channel = 1;
			else if (rb >= rr && rb >= rg) channel = 2;

			Pixels.Sort((x, y) => {
				int vx = channel == 0 ? (int)((x >> 16) & 0xFF)
					: channel == 1 ? (int)((x >> 8) & 0xFF) : (int)(x & 0xFF);
				int vy = channel == 0 ? (int)((y >> 16) & 0xFF)
					: channel == 1 ? (int)((y >> 8) & 0xFF) : (int)(y & 0xFF);
				return vx.CompareTo(vy);
			});
			var mid = Pixels.Count / 2;
			if (mid < 1) mid = 1;
			if (mid >= Pixels.Count) mid = Pixels.Count - 1;
			a = new ColorBox(Pixels.GetRange(0, mid));
			b = new ColorBox(Pixels.GetRange(mid, Pixels.Count - mid));
		}
	}
}
