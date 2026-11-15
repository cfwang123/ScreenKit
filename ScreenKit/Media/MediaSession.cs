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
	public const int VIDEOQUEUE = 96;
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

	/// <summary>seek 互斥：demux 线程读包、解码线程冲缓冲都要等这次 seek 收尾。</summary>
	readonly object locks = new();
	double seektarget;

	Thread thdemux, thvideo, thaudio;
	volatile bool stop;
	volatile bool paused;
	volatile bool eofdemux, eofvideo, eofaudio;
	volatile bool vresync;
	volatile bool aresync;
	volatile bool pendingseek;
	volatile int seekgen;
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
		vresync = false;
		aresync = false;
		pendingseek = false;
		seektarget = 0;
		seekgen = 0;
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

	/// <summary>默默读包并按流放进对应队列。seek 也只在这个线程里做：AVFormatContext 不能多线程同时用。</summary>
	void demuxloop() {
		var spin = 0;
		var skipvideo = false;
		var fullsince = 0;
		try {
			while (!stop) {
				int gen;
				lock (locks) {
					if (pendingseek) applyseek();
					gen = seekgen;
				}
				// 只对音频做背压：音频是主时钟，缺包会冻结进度
				if (audiofull()) {
					sleep(4);
					spin++;
					continue;
				}
				spin = 0;
				var r = demux.ReadPacket(out var p);
				if (!r) {
					// 不退出线程：seek 回来还要接着读
					eofdemux = true;
					sleep(8);
					continue;
				}
				var isvideo = p->stream_index == demux.VideoIndex;
				if (isvideo) {
					if (videofull()) {
						if (fullsince == 0) fullsince = Environment.TickCount;
						var waited = unchecked(Environment.TickCount - fullsince);
						if (!skipvideo && waited < 600) {
							// 解码线程还在追，等它消费，别动画面
							ffmpeg.av_packet_unref(p);
							sleep(4);
							continue;
						}
						skipvideo = true;
					}
					else fullsince = 0;
					// 队列持续满：丢到下一个关键帧，靠它重建参考链
					if (skipvideo) {
						if ((p->flags & ffmpeg.AV_PKT_FLAG_KEY) == 0) {
							ffmpeg.av_packet_unref(p);
							continue;
						}
						skipvideo = false;
						fullsince = 0;
						vresync = true;
					}
				}
				var copy = AVPacketRef.Clone(p);
				ffmpeg.av_packet_unref(p);
				if (copy == null) continue;
				// 带上读包时的代次，seek 之后解码线程据此丢掉旧包
				copy.Gen = gen;
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

	/// <summary>在解封装线程里落地一次 seek：清队列、换流位置，并让两个解码线程各自冲缓冲。</summary>
	void applyseek() {
		pendingseek = false;
		var t = seektarget;
		lock (lockq) {
			while (vpackets.Count > 0) vpackets.Dequeue().Dispose();
			while (apackets.Count > 0) apackets.Dequeue().Dispose();
		}
		eofdemux = eofvideo = eofaudio = false;
		demux.Seek(t);
		seekgen++;
		vresync = true;
		aresync = true;
		lock (lockq) Monitor.PulseAll(lockq);
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
					while (vpackets.Count > 0) {
						var it = vpackets.Dequeue();
						// seek 之前读出来的包一律丢掉，否则拿旧关键帧的 P/B 帧喂新位置必花屏
						if (it.Gen != seekgen) { it.Dispose(); continue; }
						item = it;
						break;
					}
					if (item == null) Monitor.PulseAll(lockq);
				}
				if (item == null) {
					// 到结尾且队列空了：把解码器里剩下的帧吐完，然后歇着等 seek
					if (eofdemux && vpackets.Count == 0) {
						if (!eofvideo) {
							vdec.Drain();
							eofvideo = true;
							while (!stop && vdec.Receive(out var tail)) publish(tail);
						}
					}
					sleep(spin++ < 200 ? 2 : 8);
					continue;
				}
				spin = 0;
				// 取包到送包之间可能刚好发生 seek：旧代次的包喂新位置会解出坏帧
				if (item.Gen != seekgen) {
					item.Dispose();
					continue;
				}
				// 冲缓冲必须在解封装线程改完流位置之后做
				lock (locks) {
					if (vresync) {
						vresync = false;
						vdec.Flush();
					}
				}
				using (item) {
					send(item.Ptr);
				}
			}
		}
		catch (Exception ex) {
			fail("视频解码出错: " + ex.Message);
		}
		finally {
			eofvideo = true;
		}
	}

	/// <summary>send/receive 必须交替：返回 EAGAIN 说明要先收帧，硬送会被丢掉，参考帧一断就花屏。</summary>
	void send(AVPacket* p) {
		while (!stop) {
			var r = vdec.Send(p);
			while (!stop && vdec.Receive(out var pts)) publish(pts);
			if (r != ffmpeg.AVERROR(ffmpeg.EAGAIN)) return;
		}
	}

	void audioloop() {
		var spin = 0;
		try {
			while (!stop) {
				AVPacketRef item = null;
				lock (lockq) {
					while (apackets.Count > 0) {
						var it = apackets.Dequeue();
						if (it.Gen != seekgen) { it.Dispose(); continue; }
						item = it;
						break;
					}
					if (item == null) Monitor.PulseAll(lockq);
				}
				if (item == null) {
					if (eofdemux && apackets.Count == 0) {
						if (!eofaudio) {
							adec.Drain();
							eofaudio = true;
							while (!stop) {
								var pcm = adec.Receive();
								if (pcm == null) break;
								if (!paused) audio.Push(pcm);
							}
						}
					}
					sleep(spin++ < 200 ? 2 : 8);
					continue;
				}
				spin = 0;
				if (item.Gen != seekgen) {
					item.Dispose();
					continue;
				}
				lock (locks) {
					if (aresync) {
						aresync = false;
						adec.Flush();
					}
				}
				using (item) {
					sendaudio(item.Ptr);
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

	void sendaudio(AVPacket* p) {
		while (!stop) {
			var r = adec.Send(p);
			while (!stop) {
				var pcm = adec.Receive();
				if (pcm == null) break;
				if (!paused) audio.Push(pcm);
			}
			if (r != ffmpeg.AVERROR(ffmpeg.EAGAIN)) return;
		}
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

	/// <summary>
	/// 跳到指定秒。真正的换流位置交给解封装线程做（AVFormatContext 不允许多线程同时操作），
	/// 这里只登记目标并清状态，以免在 UI 线程和读包线程之间抢同一个 context。
	/// </summary>
	public void Seek(double sec) {
		if (demux == null || disposed) return;
		if (sec < 0) sec = 0;
		if (DurationSec > 0 && sec > DurationSec) sec = DurationSec;
		lock (lockframe) { latest = null; frames.Clear(); }
		audio?.Rebase(sec);
		clock.Rebase(sec);
		lastsent = sec - 0.001;
		dropcount = 0;
		lock (locks) {
			seektarget = sec;
			pendingseek = true;
		}
		lock (lockq) Monitor.PulseAll(lockq);
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
	/// <summary>读这个包时的 seek 代次；解码线程只认当前代次，旧包直接丢。</summary>
	public int Gen;
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

