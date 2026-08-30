using System.IO;
using NAudio.Wave;

namespace ScreenKit;

/// <summary>TTS 播放：内存 WAV → WaveOut；支持暂停/继续与异步等待播完。</summary>
sealed class TtsPlayer : IDisposable {
	IWavePlayer player;
	WaveFileReader reader;
	string tempWav;
	bool disposed;
	TaskCompletionSource<bool> playTcs;
	CancellationTokenRegistration playReg;

	public bool IsPlaying {
		get {
			try { return player != null && player.PlaybackState == PlaybackState.Playing; }
			catch { return false; }
		}
	}

	public bool IsPaused {
		get {
			try { return player != null && player.PlaybackState == PlaybackState.Paused; }
			catch { return false; }
		}
	}

	/// <summary>正在播或已暂停（会话中仍有音频源）。</summary>
	public bool HasActiveAudio {
		get {
			try {
				if (player == null) return false;
				var st = player.PlaybackState;
				return st is PlaybackState.Playing or PlaybackState.Paused;
			}
			catch { return false; }
		}
	}

	public void Play(float[] samples, int sampleRate) {
		_ = PlayAsync(samples, sampleRate);
	}

	/// <summary>播放并等待自然结束或 Stop；Pause 期间继续等待。</summary>
	public async Task PlayAsync(float[] samples, int sampleRate, CancellationToken ct = default) {
		Stop();
		if (samples == null || samples.Length == 0)
			return;
		if (ct.IsCancellationRequested)
			ct.ThrowIfCancellationRequested();

		tempWav = Path.Combine(Path.GetTempPath(), $"screenkit_tts_{Guid.NewGuid():N}.wav");
		SaveWav(tempWav, samples, sampleRate);
		reader = new WaveFileReader(tempWav);
		player = new WaveOutEvent();
		var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		playTcs = tcs;
		player.PlaybackStopped += (_, e) => {
			if (e.Exception != null)
				tcs.TrySetException(e.Exception);
			else
				tcs.TrySetResult(true);
		};
		player.Init(reader);
		if (ct.CanBeCanceled) {
			playReg = ct.Register(() => {
				try { Stop(); } catch { }
				tcs.TrySetCanceled(ct);
			});
		}
		player.Play();
		// 轮询：Paused 时不结束；Stopped 结束
		try {
			while (true) {
				if (ct.IsCancellationRequested) {
					Stop();
					ct.ThrowIfCancellationRequested();
				}
				IWavePlayer p;
				try { p = player; }
				catch { break; }
				if (p == null) break;
				PlaybackState st;
				try { st = p.PlaybackState; }
				catch { break; }
				if (st == PlaybackState.Stopped) break;
				// 外部 Pause/Resume 已作用在 player 上
				await Task.Delay(40, ct).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException) {
			throw;
		}
		finally {
			// 确保资源释放（自然播完时 WaveOut 已 Stopped）
			if (player != null) {
				try {
					if (player.PlaybackState != PlaybackState.Stopped)
						player.Stop();
				}
				catch { }
			}
		}
	}

	public void Pause() {
		try {
			if (player != null && player.PlaybackState == PlaybackState.Playing)
				player.Pause();
		}
		catch { }
	}

	public void Resume() {
		try {
			if (player != null && player.PlaybackState == PlaybackState.Paused)
				player.Play();
		}
		catch { }
	}

	/// <summary>停止当前段（用于上一句/下一句跳转），不抛取消。</summary>
	public void StopSegment() {
		playReg.Dispose();
		playReg = default;
		try { player?.Stop(); } catch { }
		try { player?.Dispose(); } catch { }
		player = null;
		try { reader?.Dispose(); } catch { }
		reader = null;
		if (!string.IsNullOrEmpty(tempWav)) {
			try { if (File.Exists(tempWav)) File.Delete(tempWav); } catch { }
			tempWav = null;
		}
		var tcs = playTcs;
		playTcs = null;
		tcs?.TrySetResult(false);
	}

	public void Stop() => StopSegment();

	public static void SaveWav(string path, float[] samples, int sampleRate) {
		var dir = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1));
		writer.WriteSamples(samples, 0, samples.Length);
	}

	/// <summary>拼接多段 float PCM，段间插入静音（秒）。</summary>
	public static float[] Concat(IReadOnlyList<float[]> parts, int sampleRate, float gapSec = 0.12f) {
		if (parts == null || parts.Count == 0) return Array.Empty<float>();
		var gap = Math.Max(0, (int)(sampleRate * gapSec));
		long total = 0;
		foreach (var p in parts) {
			if (p != null) total += p.Length;
		}
		if (parts.Count > 1) total += (long)gap * (parts.Count - 1);
		if (total <= 0 || total > int.MaxValue) return Array.Empty<float>();
		var all = new float[(int)total];
		var o = 0;
		for (var i = 0; i < parts.Count; i++) {
			var p = parts[i];
			if (p == null || p.Length == 0) continue;
			Array.Copy(p, 0, all, o, p.Length);
			o += p.Length;
			if (i < parts.Count - 1 && gap > 0) o += gap;
		}
		if (o == all.Length) return all;
		var trimmed = new float[o];
		Array.Copy(all, trimmed, o);
		return trimmed;
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		Stop();
	}
}
