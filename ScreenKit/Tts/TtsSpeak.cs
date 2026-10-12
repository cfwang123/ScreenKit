using System.IO;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ScreenKit;

/// <summary>一块播放设备。</summary>
sealed class TtsOutDev {
	public string Id = "";
	public string Name = "";
	public int Index;
	public bool Default;
}

/// <summary>把合成好的 PCM 送到一个或多个播放设备。排队或立刻打断。</summary>
static class TtsSpeak {
	static readonly object gate = new();
	static readonly List<TtsSpeakJob> wait = new();
	static TtsSpeakJob cur;
	static bool alive;

	public static List<TtsOutDev> Devices() {
		var list = new List<TtsOutDev>();
		using var en = new MMDeviceEnumerator();
		var defId = "";
		try {
			using var def = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
			defId = def?.ID ?? "";
		}
		catch { }
		var i = 0;
		foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) {
			using (d) {
				var id = d.ID ?? "";
				list.Add(new TtsOutDev {
					Id = id,
					Name = d.FriendlyName ?? "",
					Index = i,
					Default = defId.Length > 0 && id == defId,
				});
			}
			i++;
		}
		return list;
	}

	/// <summary>空列表用默认设备。下标、id、完整名称或唯一的名称片段都可以。</summary>
	public static bool TryPick(IReadOnlyList<string> want, out List<TtsOutDev> picked, out string err) {
		picked = new List<TtsOutDev>();
		err = "";
		List<TtsOutDev> all;
		try { all = Devices(); }
		catch (Exception ex) {
			err = $"枚举播放设备失败: {ex.Message}";
			return false;
		}
		if (all.Count == 0) {
			err = "没有可用的播放设备";
			return false;
		}
		if (want == null || want.Count == 0) {
			var one = all.FirstOrDefault(x => x.Default) ?? all[0];
			picked.Add(one);
			return true;
		}
		foreach (var raw in want) {
			if (!match(all, raw, out var one, out err)) return false;
			if (picked.Any(x => x.Id == one.Id)) continue;
			picked.Add(one);
		}
		if (picked.Count == 0) {
			err = "没有可用的播放设备";
			return false;
		}
		return true;
	}

	/// <summary>返回这一条前面还有几条。0 表示马上播（队列空，或刚刚打断当前）。</summary>
	public static int Enqueue(float[] samples, int sampleRate, IReadOnlyList<string> ids, bool immediate) {
		var path = Path.Combine(Path.GetTempPath(), $"screenkit_say_{Guid.NewGuid():N}.wav");
		TtsPlayer.SaveWav(path, samples, sampleRate);
		var job = new TtsSpeakJob { Wav = path, Ids = ids.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToArray() };
		int ahead;
		lock (gate) {
			if (immediate) {
				cur?.Stop();
				wait.Insert(0, job);
				ahead = 0;
			}
			else {
				ahead = (cur == null ? 0 : 1) + wait.Count;
				wait.Add(job);
			}
			if (!alive) {
				alive = true;
				var t = new Thread(loop) { IsBackground = true, Name = "tts-speak" };
				t.Start();
			}
		}
		return ahead;
	}

	public static void Shutdown() {
		List<TtsSpeakJob> drop;
		TtsSpeakJob playing;
		lock (gate) {
			drop = wait.ToList();
			wait.Clear();
			playing = cur;
		}
		playing?.Stop();
		foreach (var j in drop) {
			j.Stop();
			try { if (File.Exists(j.Wav)) File.Delete(j.Wav); } catch { }
		}
	}

	static void loop() {
		while (true) {
			TtsSpeakJob job;
			lock (gate) {
				if (wait.Count == 0) {
					alive = false;
					cur = null;
					return;
				}
				job = wait[0];
				wait.RemoveAt(0);
				cur = job;
			}
			try { play(job); }
			catch (Exception ex) { CaptureLog.Ex("tts speak", ex); }
			finally {
				try { if (!string.IsNullOrEmpty(job.Wav) && File.Exists(job.Wav)) File.Delete(job.Wav); } catch { }
				lock (gate) { if (cur == job) cur = null; }
			}
		}
	}

	static void play(TtsSpeakJob job) {
		var readers = new List<WaveFileReader>();
		var left = 0;
		var done = new ManualResetEvent(false);
		var started = Environment.TickCount;
		try {
			using var en = new MMDeviceEnumerator();
			foreach (var id in job.Ids) {
				if (job.Cts.IsCancellationRequested) break;
				try {
					var dev = en.GetDevice(id);
					var reader = new WaveFileReader(job.Wav);
					var wo = new WasapiOut(dev, AudioClientShareMode.Shared, false, 120);
					wo.PlaybackStopped += (_, _) => {
						if (Interlocked.Decrement(ref left) <= 0) done.Set();
					};
					wo.Init(reader);
					lock (job.Players) job.Players.Add(wo);
					readers.Add(reader);
					Interlocked.Increment(ref left);
					wo.Play();
				}
				catch (Exception ex) {
					CaptureLog.Ex("tts speak dev", ex);
				}
			}
			if (Volatile.Read(ref left) == 0) return;
			while (!done.WaitOne(200)) {
				if (job.Cts.IsCancellationRequested) break;
				if (unchecked(Environment.TickCount - started) < 400) continue;
				var any = false;
				lock (job.Players) {
					foreach (var p in job.Players) {
						try {
							if (p.PlaybackState is PlaybackState.Playing or PlaybackState.Paused) any = true;
						}
						catch { }
					}
				}
				if (!any) break;
			}
		}
		finally {
			job.Stop();
			lock (job.Players) {
				foreach (var p in job.Players) {
					try { p.Dispose(); } catch { }
				}
				job.Players.Clear();
			}
			foreach (var r in readers) {
				try { r.Dispose(); } catch { }
			}
			done.Dispose();
		}
	}

	static bool match(List<TtsOutDev> all, string raw, out TtsOutDev one, out string err) {
		one = null;
		err = "";
		var q = (raw ?? "").Trim();
		if (q.Length == 0) {
			err = "播放设备为空";
			return false;
		}
		if (int.TryParse(q, out var idx) && idx >= 0 && idx < all.Count) {
			one = all[idx];
			return true;
		}
		var byId = all.FirstOrDefault(x => string.Equals(x.Id, q, StringComparison.OrdinalIgnoreCase));
		if (byId != null) { one = byId; return true; }
		var byName = all.Where(x => string.Equals(x.Name, q, StringComparison.OrdinalIgnoreCase)).ToList();
		if (byName.Count == 1) { one = byName[0]; return true; }
		if (byName.Count > 1) {
			err = $"播放设备不唯一: {q}";
			return false;
		}
		var part = all.Where(x => Compat.Contains(x.Name, q, StringComparison.OrdinalIgnoreCase)).ToList();
		if (part.Count == 1) { one = part[0]; return true; }
		if (part.Count > 1) {
			err = $"播放设备不唯一: {q}";
			return false;
		}
		err = $"未找到播放设备: {q}";
		return false;
	}

	sealed class TtsSpeakJob {
		public string Wav;
		public string[] Ids = Array.Empty<string>();
		public readonly CancellationTokenSource Cts = new();
		public readonly List<IWavePlayer> Players = new();

		public void Stop() {
			try { Cts.Cancel(); } catch { }
			lock (Players) {
				foreach (var p in Players) {
					try { p.Stop(); } catch { }
				}
			}
		}
	}
}
