using System;
using System.IO;
using System.Threading;
using FFmpeg.AutoGen;
using NAudio.Wave;

namespace ScreenKit;

/// <summary>
/// 旁路片段播放：从媒体文件抽 [start,end] 的音频独立出声，
/// 不碰 MediaSession 的时钟、队列与播放位置，所以点发音时视频不会跳。
/// </summary>
unsafe sealed class MediaClipPlayer : IDisposable {
	/// <summary>片段前后各留的气口，避免辅音被切掉。</summary>
	const double PAD = 0.08;
	/// <summary>太短的片段补到这个长度。</summary>
	const double MINLEN = 0.12;
	/// <summary>
	/// 目标区间前后各再延长这么多秒（默认 0，可在播放器窗口调）。
	/// 字幕时间轴常比声音早到或晚走，调大可让片段多带一点。
	/// </summary>
	double lead = 0;
	/// <summary>为了找到有声段边界，在片段之外多解这么多（够延长用还有余量）。</summary>
	double search => lead + 0.25;
	/// <summary>低于「峰值 × 这个比例」且低于 <see cref="GATEMIN"/> 的采样算静音。</summary>
	const double GATERATIO = 0.03;
	const int GATEMIN = 150;
	/// <summary>峰值不到这个值就当整段没声音，不做裁剪（纯静音字幕按原区间）。</summary>
	const int PEAKMIN = 200;
	const long NOPTS = long.MinValue;
	public const int BYTERATE = AudioDecoder.OUTRATE * AudioDecoder.OUTCH * 2;
	/// <summary>一个采样帧的字节数（S16 立体声 = 4）。裁剪起点必须落在它的整数倍上。</summary>
	const int BLOCK = AudioDecoder.OUTCH * 2;
	/// <summary>首尾淡入淡出的长度（采样帧数），约 4ms。</summary>
	const int FADEFRAMES = AudioDecoder.OUTRATE / 250;

	readonly object sync = new();
	Thread th;
	volatile bool stop;
	WaveOutEvent wo;
	float volume = 1f;

	public event Action Ended;
	public event Action<string> Failed;

	public bool IsPlaying => th != null && th.IsAlive;

	/// <summary>片段两端在切出的区间外各再延长多少秒。0 表示不延长，最大 3 秒。</summary>
	public double Lead {
		get => lead;
		set => lead = double.IsNaN(value) || value < 0 ? 0 : value > 3 ? 3 : value;
	}

	public float Volume {
		get => volume;
		set {
			volume = value < 0 ? 0 : value > 1 ? 1 : value;
			WaveOutEvent w;
			lock (sync) w = wo;
			if (w == null) return;
			try { w.Volume = volume; } catch { }
		}
	}

	// ==================== 播放 ====================
	#region
	public void Play(string path, double start, double end) {
		Stop();
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { fail("文件不存在"); return; }
		span(start, end, out var a, out var b);
		lock (sync) {
			stop = false;
			th = new Thread(() => run(path, a, b)) { IsBackground = true, Name = "media-clip" };
			th.Start();
		}
	}

	/// <summary>只抽片段 PCM（S16 立体声 48k），不出声。自测用；失败返回 null 并触发 Failed。</summary>
	public byte[] Extract(string path, double start, double end) {
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { fail("文件不存在"); return null; }
		span(start, end, out var a, out var b);
		var g0 = Math.Max(0, a - search);
		var raw = grab(path, g0, b + search);
		if (raw == null) return null;
		return trim(raw, g0, a, b);
	}

	public void Stop() {
		Thread t;
		WaveOutEvent w;
		lock (sync) {
			stop = true;
			t = th;
			th = null;
			w = wo;
		}
		try { w?.Stop(); } catch { }
		if (t != null) { try { if (t.IsAlive) t.Join(400); } catch { } }
	}

	/// <summary>
	/// 目标区间：字幕区间前后各延长 <see cref="Lead"/> 秒，再加一点气口。
	/// 解码时在这个区间外再多解 <see cref="search"/>，用来找真正有声段的边界。
	/// </summary>
	void span(double start, double end, out double a, out double b) {
		if (start < 0) start = 0;
		if (end <= start) end = start + MINLEN;
		a = Math.Max(0, start - lead - PAD);
		b = Math.Max(a + MINLEN, end + lead + PAD);
	}

	void run(string path, double a, double b) {
		var g0 = Math.Max(0, a - search);
		var data = grab(path, g0, b + search);
		if (data == null || stop) return;
		data = trim(data, g0, a, b);
		if (data == null || stop) return;
		playout(data);
	}

	/// <summary>
	/// 在目标区间 [a,b] 里再去掉首尾的空白：字幕时间轴与真实发音往往差半拍，
	/// 但延长范围只到 a/b 为止，不会再借解码余量把片段拉长。
	/// g0 是多解出来的起点（片段在 pcm 里的绝对时间基准）。
	/// 整段都静音（峰值太低）就按 [a,b] 原样返回。
	/// </summary>
	byte[] trim(byte[] pcm, double g0, double a, double b) {
		if (pcm == null || pcm.Length < BLOCK) return pcm;
		var frames = pcm.Length / BLOCK;
		var i0 = clampi((int)Math.Round((a - g0) * AudioDecoder.OUTRATE), 0, frames);
		var i1 = clampi((int)Math.Round((b - g0) * AudioDecoder.OUTRATE), 0, frames);
		if (i1 <= i0) return pcm;
		// 只在这个区间内找有声段，区间外的不算
		var peaks = new int[frames];
		var peak = 0;
		for (var i = i0; i < i1; i++) {
			var o = i * BLOCK;
			var v = 0;
			for (var c = 0; c < AudioDecoder.OUTCH; c++) {
				var s = (short)(pcm[o + c * 2] | (pcm[o + c * 2 + 1] << 8));
				var n = s < 0 ? -s : s;
				if (n > v) v = n;
			}
			peaks[i] = v;
			if (v > peak) peak = v;
		}
		if (peak < PEAKMIN) return slice(pcm, g0, a, b);
		var gate = Math.Max(GATEMIN, (int)(peak * GATERATIO));
		var first = -1;
		var last = -1;
		for (var i = i0; i < i1; i++) {
			if (peaks[i] < gate) continue;
			if (first < 0) first = i;
			last = i;
		}
		if (first < 0) return slice(pcm, g0, a, b);
		var lo = g0 + first / (double)AudioDecoder.OUTRATE;
		var hi = g0 + (last + 1) / (double)AudioDecoder.OUTRATE;
		return slice(pcm, g0, Math.Max(a, lo), Math.Min(b, hi));
	}

	/// <summary>按绝对秒从 pcm 里截一段，对齐采样帧并淡入淡出。</summary>
	static byte[] slice(byte[] pcm, double g0, double lo, double hi) {
		if (hi <= lo) return pcm;
		var off = (int)Math.Round((lo - g0) * BYTERATE);
		var len = (int)Math.Round((hi - lo) * BYTERATE);
		off -= off % BLOCK;
		len -= len % BLOCK;
		if (off < 0) { len += off; off = 0; }
		if (off >= pcm.Length || len <= 0) return pcm;
		if (off + len > pcm.Length) len = pcm.Length - off;
		if (len <= 0) return pcm;
		var cut2 = new byte[len];
		Buffer.BlockCopy(pcm, off, cut2, 0, len);
		// 新的首尾是直接切出来的波形中点，必须重新淡入淡出，否则会有「啪」的一声
		return fade(cut2, FADEFRAMES);
	}

	static int clampi(int v, int low, int high) => v < low ? low : v > high ? high : v;

	/// <summary>解码 [a,b] 的音频，返回交叠裁剪后的 PCM。</summary>
	byte[] grab(string path, double a, double b) {
		MediaDemuxer demux = null;
		AudioDecoder adec = null;
		var ms = new MemoryStream();
		try {
			demux = new MediaDemuxer(path);
			if (!demux.HasAudio) { fail("这个文件没有音轨，可用 TTS 发音"); return null; }
			adec = new AudioDecoder(demux.AudioPar);
			// 从 a 前面一秒开始解，让解码器把 seek 落点附近的坏帧吐完，真正要的区间才有正确波形。
			var base0 = Math.Max(0, a - 1.0);
			demux.Seek(base0);
			adec.Flush();
			var tb = ffmpeg.av_q2d(demux.AudioTimeBase);
			var est = base0;
			var got = 0;
			while (!stop) {
				if (!demux.ReadPacket(out var p)) break;
				try {
					if (p->stream_index != demux.AudioIndex) continue;
					var pts = p->pts == NOPTS ? est : p->pts * tb;
					// send 返回 EAGAIN 只表示解码器里还压着帧，包并没有被收下：
					// 先收空再重送同一包。原来直接 continue 会连包带帧一起丢掉，声音就断成一片。
					var r = adec.Send(p);
					while (!stop) {
						while (!stop) {
							var pcm = adec.Receive();
							if (pcm == null) break;
							got += append(ms, pcm, pts, a, b);
							pts += pcm.Length / (double)BYTERATE;
							est = pts;
						}
						if (r != ffmpeg.AVERROR(ffmpeg.EAGAIN)) break;
						r = adec.Send(p);
					}
					if (pts > b + 1.0) break;
				}
				finally {
					ffmpeg.av_packet_unref(p);
				}
			}
			if (!stop) {
				adec.Drain();
				while (!stop) {
					var pcm = adec.Receive();
					if (pcm == null) break;
					got += append(ms, pcm, double.NaN, a, b);
				}
			}
			if (stop) return null;
			if (got < BYTERATE / 50) { fail("片段太短，没抽到声音"); return null; }
			return cut(ms);
		}
		catch (Exception ex) {
			if (!stop) fail("片段抽取失败: " + ex.Message);
			return null;
		}
		finally {
			try { adec?.Dispose(); } catch { }
			try { demux?.Dispose(); } catch { }
			ms.Dispose();
		}
	}

	/// <summary>
	/// 收尾：长度对齐到采样帧（否则立体声左右声道会错位，听起来就是一声爆响），
	/// 两端各加几毫秒淡入淡出，避免从波形半山腰切下去的那一下「啪」。
	/// </summary>
	static byte[] cut(MemoryStream ms) {
		var data = ms.ToArray();
		var len = data.Length - data.Length % BLOCK;
		if (len <= 0) return data;
		if (len != data.Length) Array.Resize(ref data, len);
		return fade(data, FADEFRAMES);
	}

	static byte[] fade(byte[] pcm, int frames) {
		var total = pcm.Length / BLOCK;
		if (total <= 0) return pcm;
		var n = Math.Min(frames, total / 2);
		for (var i = 0; i < n; i++) {
			var g = i / (double)n;
			var o0 = i * BLOCK;
			var o1 = (total - 1 - i) * BLOCK;
			for (var c = 0; c < AudioDecoder.OUTCH; c++) {
				short s0 = (short)(pcm[o0 + c * 2] | (pcm[o0 + c * 2 + 1] << 8));
				pcm[o0 + c * 2] = (byte)((short)(s0 * g) & 0xFF);
				pcm[o0 + c * 2 + 1] = (byte)(((short)(s0 * g) >> 8) & 0xFF);
				short s1 = (short)(pcm[o1 + c * 2] | (pcm[o1 + c * 2 + 1] << 8));
				pcm[o1 + c * 2] = (byte)((short)(s1 * g) & 0xFF);
				pcm[o1 + c * 2 + 1] = (byte)(((short)(s1 * g) >> 8) & 0xFF);
			}
		}
		return pcm;
	}

	/// <summary>把一帧 PCM 按时间与 [a,b] 求交后写入；返回写入字节数。</summary>
	int append(MemoryStream ms, byte[] pcm, double pts, double a, double b) {
		if (double.IsNaN(pts)) {
			// 排空时没有时间戳，只可能落在结尾，按原样接上并补齐到整帧。
			var tail = pcm.Length - pcm.Length % BLOCK;
			if (tail > 0) ms.Write(pcm, 0, tail);
			return tail;
		}
		var seg0 = pts;
		var seg1 = pts + pcm.Length / (double)BYTERATE;
		var lo = Math.Max(seg0, a);
		var hi = Math.Min(seg1, b);
		if (hi <= lo) return 0;
		// off/len 必须按采样帧取整：奇数偏移会把左右声道对调，出来就是刺耳的爆音。
		var off = (int)Math.Round((lo - seg0) * BYTERATE);
		var len = (int)Math.Round((hi - lo) * BYTERATE);
		off -= off % BLOCK;
		len -= len % BLOCK;
		if (off < 0) { len += off; off = 0; }
		if (off >= pcm.Length || len <= 0) return 0;
		if (off + len > pcm.Length) len = pcm.Length - off;
		if (len <= 0) return 0;
		ms.Write(pcm, off, len);
		return len;
	}

	void playout(byte[] data) {
		WaveOutEvent w = null;
		RawSourceWaveStream rs = null;
		var done = new ManualResetEventSlim(false);
		try {
			rs = new RawSourceWaveStream(new MemoryStream(data, writable: false), new WaveFormat(AudioDecoder.OUTRATE, 16, AudioDecoder.OUTCH));
			w = new WaveOutEvent { DesiredLatency = 100, NumberOfBuffers = 4 };
			w.Volume = volume;
			w.PlaybackStopped += (_, _) => done.Set();
			w.Init(rs);
			lock (sync) {
				if (stop) { done.Set(); return; }
				wo = w;
			}
			w.Play();
			while (!stop && !done.Wait(40)) { }
		}
		catch (Exception ex) {
			if (!stop) fail("出声失败: " + ex.Message);
		}
		finally {
			lock (sync) { if (wo == w) wo = null; }
			try { w?.Stop(); } catch { }
			try { w?.Dispose(); } catch { }
			try { rs?.Dispose(); } catch { }
			done.Dispose();
			if (!stop) { try { Ended?.Invoke(); } catch { } }
		}
	}
	#endregion

	void fail(string msg) {
		try { Failed?.Invoke(msg); } catch { }
	}

	public void Dispose() {
		Stop();
		Ended = null;
		Failed = null;
	}
}

