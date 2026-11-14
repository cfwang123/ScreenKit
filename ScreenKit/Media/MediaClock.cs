using System;

namespace ScreenKit;

/// <summary>播放时钟。有声时跟音频走，无声时跟系统时钟走；暂停冻结，seek 重定基准。</summary>
sealed class MediaClock {
	readonly object sync = new();
	Func<double> source;      // 音频位置来源；null = 用系统时钟
	int startTick;            // 基准时刻（TickCount，毫秒）
	double anchor;            // 基准时刻对应的媒体秒数
	bool paused;

	public MediaClock() {
		startTick = Environment.TickCount;
	}

	public void UseAudio(Func<double> f) {
		lock (sync) {
			source = f;
			anchor = f != null ? f() : anchor;
			startTick = Environment.TickCount;
		}
	}

	public void UseSystem() {
		lock (sync) {
			source = null;
			startTick = Environment.TickCount;
		}
	}

	public bool IsPaused {
		get { lock (sync) return paused; }
	}

	public double NowSec {
		get {
			lock (sync) {
				if (paused) return anchor;
				if (source != null) {
					var a = source();
					if (a >= 0) return a;
				}
				var ms = Environment.TickCount - startTick;
				if (ms < 0) ms = 0;
				return anchor + ms / 1000.0;
			}
		}
	}

	public void Pause() {
		lock (sync) {
			if (paused) return;
			anchor = NowSecUnlocked();
			paused = true;
		}
	}

	public void Resume() {
		lock (sync) {
			if (!paused) return;
			startTick = Environment.TickCount;
			paused = false;
		}
	}

	public void Rebase(double sec) {
		lock (sync) {
			anchor = sec < 0 ? 0 : sec;
			startTick = Environment.TickCount;
		}
	}

	double NowSecUnlocked() {
		if (source != null) {
			var a = source();
			if (a >= 0) return a;
		}
		var ms = Environment.TickCount - startTick;
		return anchor + (ms < 0 ? 0 : ms) / 1000.0;
	}
}
