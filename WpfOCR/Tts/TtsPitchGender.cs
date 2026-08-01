namespace WpfOCR;

/// <summary>
/// 根据合成波形估计基频 F0，并判定男女声。
/// 帧级归一化自相关，取「高相关峰中最短周期」（抑制倍周期半频），中位数判定。
/// 阈值约 165 Hz（男低女高）。
/// </summary>
static class TtsPitchGender {
	/// <summary>F0 高于此视为女声（Hz）。</summary>
	public const float FemaleThresholdHz = 165f;
	public const float MinF0 = 70f;
	public const float MaxF0 = 400f;

	public sealed class Result {
		public float MedianF0 { get; set; }
		public float MeanF0 { get; set; }
		public int VoicedFrames { get; set; }
		public string Gender { get; set; } = "";
		public bool Ok => VoicedFrames > 0 && MedianF0 > 0;
	}

	/// <summary>从 PCM float 波形估计性别。</summary>
	public static Result Analyze(float[] samples, int sampleRate) {
		var r = new Result();
		if (samples == null || samples.Length < sampleRate / 10 || sampleRate < 8000)
			return r;

		// 去直流 + 预加重
		var work = new float[samples.Length];
		double mean = 0;
		for (int i = 0; i < samples.Length; i++) mean += samples[i];
		mean /= samples.Length;
		work[0] = (float)(samples[0] - mean);
		for (int i = 1; i < samples.Length; i++)
			work[i] = (float)((samples[i] - mean) - 0.97 * (samples[i - 1] - mean));

		var frameLen = Math.Max(256, sampleRate * 40 / 1000);
		var hop = Math.Max(64, sampleRate * 10 / 1000);
		var minLag = Math.Max(2, (int)(sampleRate / MaxF0));
		var maxLag = Math.Min(frameLen - 2, (int)(sampleRate / MinF0));
		if (maxLag <= minLag + 2) return r;

		double sumSq = 0;
		for (int i = 0; i < work.Length; i++) sumSq += work[i] * work[i];
		var globalRms = Math.Sqrt(sumSq / work.Length);
		var energyFloor = Math.Max(1e-5, globalRms * 0.12);

		var f0s = new List<float>(work.Length / hop + 1);
		for (int start = 0; start + frameLen <= work.Length; start += hop) {
			double e = 0;
			for (int i = 0; i < frameLen; i++)
				e += work[start + i] * work[start + i];
			if (Math.Sqrt(e / frameLen) < energyFloor) continue;

			var f0 = framef0(work, start, frameLen, minLag, maxLag, sampleRate);
			if (f0 >= MinF0 && f0 <= MaxF0)
				f0s.Add(f0);
		}

		if (f0s.Count < 3) return r;
		f0s.Sort();
		// 去两端 10% 离群后取中位
		var lo = f0s.Count / 10;
		var hi = f0s.Count - lo;
		if (hi <= lo) { lo = 0; hi = f0s.Count; }
		var mid = f0s.GetRange(lo, hi - lo);
		mid.Sort();
		r.VoicedFrames = f0s.Count;
		r.MedianF0 = mid[mid.Count / 2];
		double sum = 0;
		foreach (var f in mid) sum += f;
		r.MeanF0 = (float)(sum / mid.Count);
		r.Gender = r.MedianF0 >= FemaleThresholdHz ? TtsGender.Female : TtsGender.Male;
		return r;
	}

	/// <summary>
	/// 单帧 ACF：在相关峰中选最短 lag（最高 F0），避免 2T 倍周期误判为半频。
	/// </summary>
	static float framef0(float[] x, int start, int frameLen, int minLag, int maxLag, int sampleRate) {
		double r0 = 0;
		for (int i = 0; i < frameLen; i++)
			r0 += x[start + i] * x[start + i];
		if (r0 < 1e-12) return 0;

		// 先算全程相关
		var ac = new double[maxLag + 1];
		var bestR = -1.0;
		for (int lag = minLag; lag <= maxLag; lag++) {
			double s = 0;
			var n = frameLen - lag;
			for (int i = 0; i < n; i++)
				s += x[start + i] * x[start + i + lag];
			ac[lag] = s / r0;
			if (ac[lag] > bestR) bestR = ac[lag];
		}
		if (bestR < 0.30) return 0;

		// 局部峰：r 超过 best 的 75%，取最短 lag
		var thr = bestR * 0.75;
		var chosen = -1;
		for (int lag = minLag + 1; lag <= maxLag - 1; lag++) {
			if (ac[lag] < thr) continue;
			if (ac[lag] >= ac[lag - 1] && ac[lag] >= ac[lag + 1]) {
				chosen = lag;
				break; // 第一个（最高基频）峰
			}
		}
		if (chosen < 0) {
			// 回退：全局最大
			for (int lag = minLag; lag <= maxLag; lag++) {
				if (ac[lag] >= bestR - 1e-12) { chosen = lag; break; }
			}
		}
		if (chosen < 0) return 0;

		// 抛物线插值
		if (chosen > minLag && chosen < maxLag) {
			var rm = ac[chosen - 1];
			var r0c = ac[chosen];
			var rp = ac[chosen + 1];
			var denom = 2 * (2 * r0c - rp - rm);
			if (Math.Abs(denom) > 1e-9) {
				var delta = (rm - rp) / denom;
				if (delta > -1 && delta < 1)
					return (float)(sampleRate / (chosen + delta));
			}
		}
		return (float)sampleRate / chosen;
	}

	public static string Label(string gender) => TtsGender.Label(gender);
}
