using System;
using System.Threading;
using NAudio.Wave;

namespace ScreenKit;

/// <summary>
/// PCM 环形缓冲 + 输出。位置以"真正被声卡读走的字节"计算，不能用写入量——
/// 一旦缓冲溢出丢样本，写入量会把时钟直接推到结尾。
/// 缓冲满时 Push 阻塞，音频解码线程由此自然限速，主时钟才跟得上耳朵。
/// </summary>
sealed class AudioOut : IDisposable {
	readonly WaveOutEvent wo;
	readonly MediaPcmRing ring;
	bool disposed;
	float volume = 1f;

	public const int LATENCYMS = 100;
	/// <summary>环形缓冲容量（毫秒）。太小会断续，太大会拖慢 seek。</summary>
	const int CAPMS = 2000;

	public int Rate { get; }
	public int Channels { get; }

	public AudioOut(int sampleRate = AudioDecoder.OUTRATE, int ch = AudioDecoder.OUTCH) {
		Rate = sampleRate > 0 ? sampleRate : AudioDecoder.OUTRATE;
		Channels = ch <= 1 ? 1 : 2;
		ring = new MediaPcmRing(new WaveFormat(Rate, 16, Channels), Rate * Channels * 2 * CAPMS / 1000);
		wo = new WaveOutEvent { DesiredLatency = LATENCYMS, NumberOfBuffers = 4 };
		wo.Init(ring);
		wo.Volume = 1f;
		wo.Play();
	}

	int BytesPerSec => Rate * Channels * 2;

	/// <summary>已播秒数 = 声卡读走的字节 / 每秒字节。</summary>
	public double PositionSec => ring.Consumed / (double)BytesPerSec;

	/// <summary>缓冲里积压的毫秒数。</summary>
	public int QueuedMs {
		get {
			var ms = (int)(ring.Available / (double)BytesPerSec * 1000);
			return ms < 0 ? 0 : ms;
		}
	}

	public float Volume {
		get => volume;
		set {
			volume = value < 0 ? 0 : value > 1 ? 1 : value;
			try { wo.Volume = volume; } catch { }
		}
	}

	/// <summary>写入 PCM；缓冲满时阻塞等待，别丢样本。</summary>
	public void Push(byte[] pcm) {
		if (disposed || pcm == null || pcm.Length == 0) return;
		var off = 0;
		while (off < pcm.Length && !disposed) {
			var n = ring.Write(pcm, off, pcm.Length - off);
			off += n;
			if (n == 0) Thread.Sleep(4);
		}
	}

	/// <summary>seek 后重定基准：清空缓冲并把已播计数挪到目标位置。</summary>
	public void Rebase(double sec) {
		ring.Rebase((long)((sec < 0 ? 0 : sec) * BytesPerSec));
	}

	public void Pause() {
		if (disposed) return;
		try { wo.Pause(); } catch { }
	}

	public void Resume() {
		if (disposed) return;
		try { wo.Play(); } catch { }
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		try { wo.Stop(); } catch { }
		try { wo.Dispose(); } catch { }
	}
}

/// <summary>NAudio 用的环形缓冲。Read 永不阻塞，不足处补静音。</summary>
sealed class MediaPcmRing : IWaveProvider {
	readonly object sync = new();
	readonly byte[] data;
	int rd, wr, count;
	long consumed;
	long baserebase;

	public MediaPcmRing(WaveFormat fmt, int capacity) {
		WaveFormat = fmt;
		data = new byte[Math.Max(4096, capacity)];
	}

	public WaveFormat WaveFormat { get; }

	/// <summary>真正被读走的字节数（含重定基准前的偏移）。</summary>
	public long Consumed {
		get { lock (sync) return consumed + baserebase; }
	}

	public int Available {
		get { lock (sync) return count; }
	}

	public void Rebase(long bytes) {
		lock (sync) {
			rd = wr = count = 0;
			baserebase = bytes < 0 ? 0 : bytes;
			consumed = 0;
		}
	}

	/// <summary>写进缓冲，返回写入字节数；空间不足时返回 0。</summary>
	public int Write(byte[] src, int off, int len) {
		lock (sync) {
			var free = data.Length - count;
			if (free <= 0) return 0;
			var n = Math.Min(free, len);
			var tail = data.Length - wr;
			if (n <= tail) {
				Buffer.BlockCopy(src, off, data, wr, n);
				wr += n;
				if (wr == data.Length) wr = 0;
			}
			else {
				Buffer.BlockCopy(src, off, data, wr, tail);
				Buffer.BlockCopy(src, off + tail, data, 0, n - tail);
				wr = n - tail;
			}
			count += n;
			return n;
		}
	}

	public int Read(byte[] dst, int off, int n) {
		lock (sync) {
			var got = Math.Min(count, n);
			if (got > 0) {
				var tail = data.Length - rd;
				if (got <= tail) {
					Buffer.BlockCopy(data, rd, dst, off, got);
					rd += got;
					if (rd == data.Length) rd = 0;
				}
				else {
					Buffer.BlockCopy(data, rd, dst, off, tail);
					Buffer.BlockCopy(data, 0, dst, off + tail, got - tail);
					rd = got - tail;
				}
				count -= got;
				consumed += got;
			}
			if (got < n) Array.Clear(dst, off + got, n - got);
			return n;
		}
	}
}
