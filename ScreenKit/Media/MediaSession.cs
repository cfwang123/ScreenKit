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
	/// <summary>
	/// 视频包队列的"饥饿线"。音频队列满时会暂停读包（音频是主时钟），
	/// 但视频队列低于这条线时不能停：解封装一停，画面就没有新包，看起来就是卡住。
	/// </summary>
	const int VIDEOMIN = 24;
	/// <summary>为喂视频，音频包最多能超出 AUDIOQUEUE 多少。防止交织异常时无限增长。</summary>
	const int AUDIOOVER = 64;
	/// <summary>已解码未显示的帧上限；太小会在慢机器上丢帧，太大延迟高。</summary>
	public const int FRAMEBUF = 8;
	/// <summary>丢弃线上的余量：落到目标点前 30ms 的内容留着，别把正好那帧丢掉。</summary>
	const int DROPSLACKMS = 30;
	/// <summary>丢弃线最多等这么久；某一路迟迟不来越过，也必须恢复出画面。</summary>
	const int DROPTIMEOUTMS = 2000;

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

	/// <summary>
	/// seek 落点之前的"丢弃线"（毫秒，int.MinValue = 没有线）。
	/// av_seek_frame 只会落到前一个关键帧（本片 5 秒一个），从那儿到目标点之间的
	/// 画面和声音都是过时的：全丢。视频照样解码，只是不拷进帧队列——参考链不能断。
	/// 两路各记各的，谁先越过谁先撤线。
	/// </summary>
	volatile int seekvms = int.MinValue;
	volatile int seekams = int.MinValue;
	/// <summary>丢弃线的建立时刻（TickCount），某一路一直不来时用来兜底解冻。</summary>
	volatile int droptick;
	/// <summary>seek 后因落在目标点之前而被扔掉的帧数，诊断用。</summary>
	int seekdrop;
	/// <summary>视频队列持续满时进入丢包模式，只留关键帧重建参考链。</summary>
	bool skipvideo;
	int fullsince;

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
	/// <summary>文件里有视频流。画面可能由别处显示（此时 <see cref="DecodeVideo"/> 为 false），这里只看流。</summary>
	public bool HasVideo => demux?.HasVideo ?? false;
	/// <summary>
	/// 是否自己解视频帧。播放器把画面交给 MediaElement 时置 false：少解一路，省 CPU，
	/// 也避免两边各解一遍。关闭状态在 <see cref="Open"/> 时生效。
	/// </summary>
	public bool DecodeVideo { get; set; } = true;
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
	/// <summary>
	/// 播完了没有。以时钟到结尾为准：画面交给 MediaElement 时这里不解视频，
	/// 视频包直接扔掉，解封装会一路读到结尾，光看各流 eof 会立刻误判成播完。
	/// </summary>
	public bool IsEnded {
		get {
			if (DurationSec > 0 && PositionSec >= DurationSec - 0.2) return true;
			return vdec != null && adec != null && eofdemux && eofvideo && eofaudio;
		}
	}
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
	/// <summary>这次 seek 因落在目标点之前被扔掉的帧数，诊断用。</summary>
	public int SeekDropCount => seekdrop;
	/// <summary>seek 落点之前的内容是否还在丢弃中，诊断用。</summary>
	public bool SeekPending => seekvms != int.MinValue || seekams != int.MinValue;
	/// <summary>音频缓冲积压毫秒，诊断用。</summary>
	public int QueuedAudioMs => audio?.QueuedMs ?? 0;
	/// <summary>音频时钟本身的读数，诊断用。</summary>
	public double AudioPosSec => audio?.PositionSec ?? -1;

	public event Action<string> Failed;

	// ==================== 打开 ====================
	#region
	public void Open(string path) {
		Close();
		Path = path;
		demux = new MediaDemuxer(path);
		if (demux.HasVideo && DecodeVideo) vdec = new VideoDecoder(demux.VideoPar, demux.Rotation, demux.VideoTimeBase);
		if (demux.HasAudio) {
			adec = new AudioDecoder(demux.AudioPar, demux.AudioTimeBase);
			audio = new AudioOut();
			audio.Volume = lastvolume;
		}
		// 视频流存在就算可播：画面可能由 MediaElement 出，这里只负责声音和时钟
		if (!demux.HasVideo && adec == null)
			throw new InvalidOperationException("这个文件没有可播放的音视频流");
		if (audio != null) clock.UseAudio(() => audio.PositionSec);
		else clock.UseSystem();
		eofdemux = eofvideo = eofaudio = false;
		// 不解视频时没有视频线程，结尾判定不能等它
		if (vdec == null) eofvideo = true;
		stop = false;
		paused = false;
		vresync = false;
		aresync = false;
		pendingseek = false;
		seektarget = 0;
		seekgen = 0;
		dropcount = 0;
		seekvms = seekams = int.MinValue;
		seekdrop = 0;
		skipvideo = false;
		fullsince = 0;
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
		try {
			while (!stop) {
				int gen;
				lock (locks) {
					if (pendingseek) applyseek();
					gen = seekgen;
				}
				// 音频背压：音频是主时钟，缺包会冻结进度，所以队列满了先等音频线程消费。
				// 但不能把视频一起饿死——视频包见底时继续读，否则解封装一停画面就没包可解。
				if (audiofull()) {
					if (!starving() || audiocount() >= AUDIOQUEUE + AUDIOOVER) {
						sleep(4);
						spin++;
						continue;
					}
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
					// 不解视频时视频包没人消费，留着只会把内存吃光
					if (vdec == null) { ffmpeg.av_packet_unref(p); continue; }
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
		// 不解视频时没有视频线程，结尾判定不能等它
		if (vdec == null) eofvideo = true;
		// 丢包模式是上一次卡顿的残留状态，不重置就会一路丢到下一个关键帧（本片 5 秒一个）
		skipvideo = false;
		fullsince = 0;
		demux.Seek(t);
		vresync = true;
		aresync = true;
		seekvms = seekams = (int)(t * 1000) - DROPSLACKMS;
		seekdrop = 0;
		droptick = Environment.TickCount;
		lock (lockq) Monitor.PulseAll(lockq);
	}

	int videocount() {
		lock (lockq) return vpackets.Count;
	}

	int audiocount() {
		lock (lockq) return apackets.Count;
	}

	bool videofull() {
		return vdec != null && videocount() >= VIDEOQUEUE;
	}

	/// <summary>视频包见底：这时候再为了音频停下来，画面就没包可解了。</summary>
	bool starving() {
		return vdec != null && videocount() < VIDEOMIN;
	}

	bool audiofull() {
		return adec != null && audiocount() >= AUDIOQUEUE;
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
							// 收尾帧跟着当时的代次走，seek 之后就别再塞进去
							while (!stop && vdec.Receive(out var tail)) publish(tail, seekgen);
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
					send(item.Ptr, item.Gen);
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
	void send(AVPacket* p, int gen) {
		while (!stop) {
			var r = vdec.Send(p);
			while (!stop && vdec.Receive(out var pts)) publish(pts, gen);
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
								pushaudio(pcm, seekgen);
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
					sendaudio(item.Ptr, item.Gen);
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

	void sendaudio(AVPacket* p, int gen) {
		while (!stop) {
			var r = adec.Send(p);
			while (!stop) {
				var pcm = adec.Receive();
				if (pcm == null) break;
				pushaudio(pcm, gen);
			}
			if (r != ffmpeg.AVERROR(ffmpeg.EAGAIN)) return;
		}
	}

	/// <summary>
	/// 送 PCM 进声卡。seek 之后 av_seek_frame 落在关键帧，它到目标点之间的声音是过时的，
	/// 直接丢掉：否则时钟一边往上走，耳朵里还是 5 秒前的台词，跟画面字幕全对不上。
	/// 往回跳时旧包的时间戳已经越过丢弃线，只能靠代次分辨，同样必须丢。
	/// </summary>
	void pushaudio(byte[] pcm, int gen) {
		if (pcm == null || pcm.Length == 0) return;
		if (gen != seekgen) return;
		var line = seekams;
		if (line != int.MinValue) {
			var pts = adec.LastPts;
			if (!double.IsNaN(pts) && (int)(pts * 1000) < line) {
				// 某一路一直越不过线时兜底解冻，别让声音永久哑掉
				if (unchecked(Environment.TickCount - droptick) <= DROPTIMEOUTMS) return;
				seekams = int.MinValue;
			}
			else seekams = int.MinValue;
		}
		if (!paused) audio.Push(pcm);
	}

	void publish(double pts, int gen) {
		if (vdec.Buffer == null) return;
		// 解码到入队的这段里刚好 seek 了：这帧属于旧位置，扔（解码本身不能跳，参考链要留）
		if (gen != seekgen) { seekdrop++; return; }
		// 关键帧到目标点之间的画面：照解（参考链不能断），但不往帧队列里塞
		var line = seekvms;
		if (line != int.MinValue) {
			// 没有 pts 的流没法按时间挡，只能信代次（旧包已经在取包时就被丢了）
			var over = double.IsNaN(pts) || unchecked(Environment.TickCount - droptick) > DROPTIMEOUTMS;
			if (over || (int)(pts * 1000) >= line) seekvms = int.MinValue;
			else { seekdrop++; return; }
		}
		var f = new VideoFrame {
			Bgra = (byte[])vdec.Buffer.Clone(),
			Width = vdec.Width,
			Height = vdec.Height,
			Stride = vdec.Stride,
			Pts = pts,
		};
		// 帧队列满就等 UI 消费，否则一路狂解把内存吃光。等的时候也要盯着代次和丢弃线：
		// 这一帧的队列满了、UI 又不肯消费旧位置的帧时，得靠这两个条件把它放出来，否则解码线程就卡死。
		var waited = 0;
		while (!stop) {
			lock (lockframe) {
				if (gen != seekgen) return;
				var l = seekvms;
				if (l != int.MinValue && !double.IsNaN(pts) && (int)(pts * 1000) < l) { seekdrop++; return; }
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
		// 丢弃线先竖起来：解码线程可能正握着一帧旧位置的画面，等它克隆完再清队列就晚了
		seekvms = seekams = (int)(sec * 1000) - DROPSLACKMS;
		seekdrop = 0;
		droptick = Environment.TickCount;
		lock (lockframe) { latest = null; frames.Clear(); }
		audio?.Rebase(sec);
		clock.Rebase(sec);
		lastsent = sec - 0.001;
		dropcount = 0;
		lock (locks) {
			// 代次立刻加一：解码线程手里那一帧（可能是旧位置的）从此对不上号，直接扔。
			// 等解封装线程换完流位置再加就晚了——往回跳时旧帧时间戳比新位置还大，光靠丢弃线挡不住。
			seekgen++;
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

