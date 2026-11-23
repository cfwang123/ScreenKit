namespace ScreenKit;

/// <summary>进程上下行速率历史，只保留最近两分钟，更早的点自动丢掉。</summary>
sealed class RateHistory {
	public static RateHistory Instance { get; } = new();

	/// <summary>时间轴跨度（毫秒），与性能小窗一致。</summary>
	public const int SPAN_MS = 120000;
	// 2 分钟约 2Hz 采样 240 点，留点余量
	const int CAP = 400;

	readonly object gate = new();
	readonly Dictionary<int, Ring> map = new();

	public void Push(int pid, double sendBps, double recvBps) {
		if (pid <= 0) return;
		var now = Environment.TickCount;
		lock (gate) {
			if (!map.TryGetValue(pid, out var r)) {
				r = new Ring(CAP);
				map[pid] = r;
			}
			r.Add(now, sendBps, recvBps);
			r.DropOlder(now - SPAN_MS - 5000);
		}
	}

	/// <param name="ages">每点距今毫秒数，越大越靠左。</param>
	public void Get(int pid, out double[] send, out double[] recv, out double[] ages) {
		lock (gate) {
			if (!map.TryGetValue(pid, out var r)) {
				send = Array.Empty<double>();
				recv = Array.Empty<double>();
				ages = Array.Empty<double>();
				return;
			}
			r.Copy(Environment.TickCount, out send, out recv, out ages);
		}
	}

	/// <summary>淘汰长时间未见的 PID。</summary>
	public void Prune(HashSet<int> alive) {
		lock (gate) {
			List<int> dead = new();
			foreach (var k in map.Keys)
				if (!alive.Contains(k)) dead.Add(k);
			foreach (var k in dead) map.Remove(k);
		}
	}

	sealed class Ring {
		readonly int[] ticks;
		readonly double[] send;
		readonly double[] recv;
		readonly int cap;
		int head, count;

		public Ring(int capacity) {
			cap = capacity;
			ticks = new int[cap];
			send = new double[cap];
			recv = new double[cap];
		}

		public void Add(int tick, double s, double r) {
			ticks[head] = tick;
			send[head] = s;
			recv[head] = r;
			head = (head + 1) % cap;
			if (count < cap) count++;
		}

		public void DropOlder(int cut) {
			while (count > 0) {
				var i = (head - count + cap) % cap;
				if (unchecked(ticks[i] - cut) >= 0) break;
				count--;
			}
		}

		public void Copy(int now, out double[] s, out double[] r, out double[] a) {
			s = new double[count];
			r = new double[count];
			a = new double[count];
			var start = (head - count + cap) % cap;
			for (var i = 0; i < count; i++) {
				var idx = (start + i) % cap;
				s[i] = send[idx];
				r[i] = recv[idx];
				a[i] = unchecked(now - ticks[idx]);
			}
		}
	}
}
