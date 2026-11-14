using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using FFmpeg.AutoGen;

namespace ScreenKit;

/// <summary>解出来的一帧画面。Buffer 是解封装线程之外独立的拷贝，可以安全跨线程。</summary>
sealed class VideoFrame {
	public byte[] Bgra;
	public int Width, Height, Stride;
	public double Pts;
}

/// <summary>
/// 播放会话门面：一个解封装线程分发到视频、音频两条解码线程，队列有界，靠 MediaClock 对齐。
/// 视频帧拷出来后放共享帧槽，UI 线程只读最新的那一帧。
/// </summary>
unsafe sealed class MediaSession : IDisposable {
	public const int VIDEOQUEUE = 32;
	public const int AUDIOQUEUE = 64;
	/// <summary>已解码未显示的帧上限；太小会在慢机器上丢帧，太大延迟高。</summary>
	public const int FRAMEBUF = 8;
	/// <summary>音频缓冲高于此值时解码线程休息，让时钟不被写爆。</summary>
	const int AUDIOTAILMS = 350;

	readonly object lockq = new();
	readonly Queue<AVPacketRef> vpackets = new();
	readonly Queue<AVPacketRef> apackets = new();
	readonly Queue<VideoFrame> frames = new();
	readonly object lockframe = new();

	MediaDemuxer demux;
	VideoDecoder vdec;
	AudioDecoder adec;
	AudioOut audio;
	readonly MediaClock clock = new();

	Thread thdemux, thvideo, thaudio;
	volatile bool stop;
	volatile bool paused;
	volatile bool eofdemux, eofvideo, eofaudio;
	volatile bool disposed;

	VideoFrame latest;
	long frameseq;
	double lastsent;
	int dropcount;

	public string Path { get; private set; } = "";
	public double DurationSec => demux?.DurationSec ?? 0;
	public bool HasVideo => vdec != null;
	public bool HasAudio => adec != null;
	public int Width => demux?.Width ?? 0;
	public int Height => demux?.Height ?? 0;
	public double Fps => demux?.Fps ?? 0;
	public int Rotation => demux?.Rotation ?? 0;
	public MediaDemuxer Demuxer => demux;
	public MediaClock Clock => clock;
	public float Volume {
		get => audio?.Volume ?? 1f;
		set { if (audio != null) audio.Volume = value; }
	}
	public bool IsPaused => paused;
	public bool IsEnded => eofdemux && eofvideo && eofaudio;
	public int QueuedVideo {
		get { lock (lockq) return vpackets.Count; }
	}
	public int QueuedAudio {
		get { lock (lockq) return apackets.Count; }
	}
	/// <summary>已解码待显示的帧数。</summary>
	public int QueuedFrames {
		get { lock (lockframe) return frames.Count; }
	}
	/// <summary>帧队列头部 pts，诊断用。</summary>
	public double? HeadPts {
		get { lock (lockframe) return frames.Count > 0 ? frames.Peek().Pts : null; }
	}
	/// <summary>连续丢帧计数，UI 可据此提示性能不足。</summary>
	public int DropCount => dropcount;

	public event Action<string> Failed;

	// ==================== 打开 ====================
	#region
	public void Open(string path) {
		Close();
		Path = path;
		demux = new MediaDemuxer(path);
		if (demux.HasVideo) vdec = new VideoDecoder(demux.VideoPar, demux.Rotation, demux.VideoTimeBase);
		if (demux.HasAudio) {
			adec = new AudioDecoder(demux.AudioPar);
			audio = new AudioOut();
			audio.Volume = lastvolume;
		}
		if (vdec == null && adec == null)
			throw new InvalidOperationException("这个文件没有可播放的音视频流");
		if (audio != null) clock.UseAudio(() => audio.PositionSec);
		else clock.UseSystem();
		eofdemux = eofvideo = eofaudio = false;
		stop = false;
		paused = false;
		dropcount = 0;
		lock (lockframe) { latest = null; frames.Clear(); frameseq = 0; }
		startthreads();
	}

	float lastvolume = 1f;
	#endregion

	// ==================== 线程 ====================
	#region
	void startthreads() {
		thdemux = new Thread(demuxloop) { IsBackground = true, Name = "media-demux" };
		thdemux.Start();
		if (vdec != null) {
			thvideo = new Thread(videoloop) { IsBackground = true, Name = "media-video" };
			thvideo.Start();
		}
		if (adec != null) {
			thaudio = new Thread(audioloop) { IsBackground = true, Name = "media-audio" };
			thaudio.Start();
		}
	}

	/// <summary>默默读包并按流放进对应队列。视频队列满就丢包，绝不能因此饿死音频。</summary>
	void demuxloop() {
		var spin = 0;
		try {
			while (!stop) {
				// 只对音频做背压：音频是主时钟，缺包会冻结进度
				if (audiofull()) {
					sleep(4);
					spin++;
					continue;
				}
				spin = 0;
				var r = demux.ReadPacket(out var p);
				if (!r) { eofdemux = true; break; }
				var isvideo = p->stream_index == demux.VideoIndex;
				if (isvideo && videofull()) {
					// 视频解码跟不上：丢掉这个包，让音频继续走
					ffmpeg.av_packet_unref(p);
					continue;
				}
				var copy = AVPacketRef.Clone(p);
				ffmpeg.av_packet_unref(p);
				if (copy == null) continue;
				lock (lockq) {
					if (isvideo) vpackets.Enqueue(copy);
					else apackets.Enqueue(copy);
					Monitor.PulseAll(lockq);
				}
			}
		}
		catch (Exception ex) {
			fail("解封装出错: " + ex.Message);
		}
		finally {
			eofdemux = true;
			lock (lockq) Monitor.PulseAll(lockq);
		}
	}

	bool videofull() {
		lock (lockq) return vdec != null && vpackets.Count >= VIDEOQUEUE;
	}

	bool audiofull() {
		lock (lockq) return adec != null && apackets.Count >= AUDIOQUEUE;
	}

	void videoloop() {
		var spin = 0;
		try {
			while (!stop) {
				AVPacketRef item = null;
				lock (lockq) {
					if (vpackets.Count > 0) item = vpackets.Dequeue();
					else Monitor.PulseAll(lockq);
				}
				if (item == null) {
					if (eofdemux) {
						if (!eofvideo) { vdec.Drain(); eofvideo = true; }
						if (drainvideo()) break;
					}
					sleep(spin++ < 200 ? 2 : 8);
					continue;
				}
				spin = 0;
				using (item) {
					vdec.Send(item.Ptr);
				}
				while (!stop && vdec.Receive(out var pts)) publish(pts);
			}
		}
		catch (Exception ex) {
			fail("视频解码出错: " + ex.Message);
		}
		finally {
			eofvideo = true;
		}
	}

	bool drainvideo() {
		while (!stop && vdec.Receive(out var pts)) publish(pts);
		return true;
	}

	void audioloop() {
		var spin = 0;
		try {
			while (!stop) {
				AVPacketRef item = null;
				lock (lockq) {
					if (apackets.Count > 0) item = apackets.Dequeue();
					else Monitor.PulseAll(lockq);
				}
				if (item == null) {
					if (eofdemux) {
						if (!eofaudio) { adec.Drain(); eofaudio = true; }
						while (!stop && drainaudio()) { }
						break;
					}
					sleep(spin++ < 200 ? 2 : 8);
					continue;
				}
				spin = 0;
				using (item) {
					adec.Send(item.Ptr);
				}
				while (!stop) {
					var pcm = adec.Receive();
					if (pcm == null) break;
					if (!paused) audio.Push(pcm);
				}
			}
		}
		catch (Exception ex) {
			fail("音频解码出错: " + ex.Message);
		}
		finally {
			eofaudio = true;
		}
	}

	bool drainaudio() {
		var got = false;
		while (!stop) {
			var pcm = adec.Receive();
			if (pcm == null) break;
			got = true;
			if (!paused) audio.Push(pcm);
		}
		return got;
	}

	void publish(double pts) {
		if (vdec.Buffer == null) return;
		var f = new VideoFrame {
			Bgra = (byte[])vdec.Buffer.Clone(),
			Width = vdec.Width,
			Height = vdec.Height,
			Stride = vdec.Stride,
			Pts = pts,
		};
		// 帧队列满就等 UI 消费，否则一路狂解把内存吃光
		var waited = 0;
		while (!stop) {
			lock (lockframe) {
				if (frames.Count < FRAMEBUF) {
					frames.Enqueue(f);
					latest = f;
					frameseq++;
					Monitor.PulseAll(lockframe);
					return;
				}
			}
			sleep(waited++ < 100 ? 2 : 8);
		}
	}

	static void sleep(int ms) {
		try { Thread.Sleep(ms); } catch { }
	}

	void fail(string msg) {
		try { Failed?.Invoke(msg); } catch { }
	}
	#endregion

	// ==================== 取帧 ====================
	#region
	/// <summary>
	/// 取一帧该上屏的：丢掉落后超 40ms 的，取不晚于时钟的最新一帧。
	/// 返回 null 表示这轮不用画（还没到时候）。
	/// </summary>
	public VideoFrame TakeDue() {
		lock (lockframe) {
			if (frames.Count == 0) return null;
			var t = clock.NowSec;
			VideoFrame pick = null;
			// 只丢"明显过期"的，且至少留一帧，暂停时不丢
			while (frames.Count > 1 && !clock.IsPaused) {
				var head = frames.Peek();
				if (head.Pts > t + 0.005) break;
				pick = frames.Dequeue();
				if (frames.Peek().Pts > t + 0.005) break;
				if (head.Pts >= t - 0.040) break;
				dropcount++;
			}
			if (pick == null) {
				var head = frames.Peek();
				if (double.IsNaN(head.Pts) || head.Pts <= t + 0.005) pick = frames.Dequeue();
			}
			if (pick == null) return null;
			lastsent = pick.Pts;
			return pick;
		}
	}

	/// <summary>暂停或静止时也要能拿到最后一帧刷新窗口。</summary>
	public VideoFrame PeekLatest() {
		lock (lockframe) return latest;
	}

	public long FrameSeq {
		get { lock (lockframe) return frameseq; }
	}
	#endregion

	// ==================== 控制 ====================
	#region
	public void Play() {
		if (disposed) return;
		paused = false;
		clock.Resume();
		audio?.Resume();
	}

	public void Pause() {
		if (disposed) return;
		paused = true;
		clock.Pause();
		audio?.Pause();
	}

	public void TogglePause() {
		if (paused) Play();
		else Pause();
	}

	public double PositionSec => clock.NowSec;

	public void Seek(double sec) {
		if (demux == null || disposed) return;
		var target = demux.Seek(sec);
		lock (lockq) {
			while (vpackets.Count > 0) vpackets.Dequeue().Dispose();
			while (apackets.Count > 0) apackets.Dequeue().Dispose();
		}
		vdec?.Flush();
		adec?.Flush();
		audio?.Rebase(target);
		clock.Rebase(target);
		eofdemux = eofvideo = eofaudio = false;
		lastsent = target - 0.001;
		dropcount = 0;
		lock (lockframe) { latest = null; frames.Clear(); }
	}

	public void SetVolume(float v) {
		lastvolume = v < 0 ? 0 : v > 1 ? 1 : v;
		if (audio != null) audio.Volume = lastvolume;
	}
	#endregion

	// ==================== 关闭 ====================
	#region
	public void Close() {
		stop = true;
		lock (lockq) Monitor.PulseAll(lockq);
		jointhread(thdemux);
		jointhread(thvideo);
		jointhread(thaudio);
		thdemux = thvideo = thaudio = null;
		lock (lockq) {
			while (vpackets.Count > 0) vpackets.Dequeue().Dispose();
			while (apackets.Count > 0) apackets.Dequeue().Dispose();
		}
		lock (lockframe) { latest = null; frames.Clear(); }
		try { vdec?.Dispose(); } catch { }
		try { adec?.Dispose(); } catch { }
		try { audio?.Dispose(); } catch { }
		try { demux?.Dispose(); } catch { }
		vdec = null;
		adec = null;
		audio = null;
		demux = null;
		Path = "";
		stop = false;
		paused = false;
		eofdemux = eofvideo = eofaudio = false;
	}

	static void jointhread(Thread t) {
		if (t == null) return;
		try {
			if (t.IsAlive) t.Join(1500);
		}
		catch { }
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		Close();
	}
	#endregion
}

/// <summary>包引用计数自己的拷贝，队列跨线程时用它保命。</summary>
unsafe sealed class AVPacketRef : IDisposable {
	public AVPacket* Ptr;
	bool disposed;

	public static AVPacketRef Clone(AVPacket* src) {
		if (src == null) return null;
		var p = ffmpeg.av_packet_alloc();
		if (p == null) return null;
		if (ffmpeg.av_packet_ref(p, src) < 0) {
			ffmpeg.av_packet_free(&p);
			return null;
		}
		return new AVPacketRef { Ptr = p };
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		if (Ptr != null) {
			var p = Ptr;
			ffmpeg.av_packet_unref(p);
			ffmpeg.av_packet_free(&p);
			Ptr = null;
		}
	}
}

