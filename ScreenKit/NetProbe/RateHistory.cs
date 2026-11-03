namespace ScreenKit;

/// <summary>进程上下行速率环形缓冲（默认 5 分钟，1 点/秒约 300 点）。</summary>
sealed class RateHistory {
	public static RateHistory Instance { get; } = new();

	const int CAP = 300; // ~5 min @ 1Hz；监控约 2Hz 时约 2.5 分钟
	readonly object gate = new();
	readonly Dictionary<int, Ring> map = new();

	public void Push(int pid, double sendBps, double recvBps) {
		if (pid <= 0) return;
		lock (gate) {
			if (!map.TryGetValue(pid, out var r)) {
				r = new Ring(CAP);
				map[pid] = r;
			}
			r.Add(sendBps, recvBps);
		}
	}

	public void Get(int pid, out double[] send, out double[] recv) {
		lock (gate) {
			if (!map.TryGetValue(pid, out var r)) {
				send = Array.Empty<double>();
				recv = Array.Empty<double>();
				return;
			}
			r.Copy(out send, out recv);
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
		readonly double[] send;
		readonly double[] recv;
		int head, count;
		readonly int cap;

		public Ring(int capacity) {
			cap = capacity;
			send = new double[cap];
			recv = new double[cap];
		}

		public void Add(double s, double r) {
			send[head] = s;
			recv[head] = r;
			head = (head + 1) % cap;
			if (count < cap) count++;
		}

		public void Copy(out double[] s, out double[] r) {
			s = new double[count];
			r = new double[count];
			var start = count < cap ? 0 : head;
			for (var i = 0; i < count; i++) {
				var idx = (start + i) % cap;
				s[i] = send[idx];
				r[i] = recv[idx];
			}
		}
	}
}
