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
		return grab(path, a, b);
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

	/// <summary>把 [start,end] 换算成带气口的采样区间。</summary>
	static void span(double start, double end, out double a, out double b) {
		if (start < 0) start = 0;
		if (end <= start) end = start + MINLEN;
		a = Math.Max(0, start - PAD);
		b = Math.Max(a + MINLEN, end + PAD);
	}

	void run(string path, double a, double b) {
		var data = grab(path, a, b);
		if (data == null || stop) return;
		playout(data);
	}

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
