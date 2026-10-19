using NAudio.Wave;

namespace ScreenKit;

/// <summary>听本机正在播出的声音，供 MIDI 合成器画波形。系统合成器不给出采样，从回放回路取。</summary>
sealed class MidiWave : IDisposable {
	const int N = 8192;
	readonly object gate = new object();
	readonly float[] ring = new float[N];
	readonly float[] chrono = new float[N];
	WasapiLoopbackCapture cap;
	int write;
	float shown;

	public void Start() {
		if (cap != null) return;
		try {
			cap = new WasapiLoopbackCapture();
			cap.DataAvailable += ondata;
			cap.StartRecording();
		}
		catch {
			try { cap?.Dispose(); } catch { }
			cap = null;
		}
	}

	public void Copy(float[] dst) {
		if (dst == null || dst.Length == 0) return;
		lock (gate) {
			int n = ring.Length;
			int start = write;
			for (int i = 0; i < n; i++) chrono[i] = ring[(start + i) % n];
		}
		AlignView(chrono, dst);
		boost(dst);
	}

	// 上升过零对齐到画面靠左的固定位置。周期信号每一帧落在同一相位，不再左右飘。
	public static bool AlignView(float[] chrono, float[] dst) {
		int n = chrono == null ? 0 : chrono.Length;
		int view = dst == null ? 0 : dst.Length;
		if (n < 2 || view < 2) return false;
		if (n < view + 8 || view < 8) {
			int start = n - view;
			if (start < 0) start = 0;
			for (int i = 0; i < view; i++) dst[i] = start + i < n ? chrono[start + i] : 0;
			return false;
		}
		int target = view / 8;
		int lo = target;
		int hi = n - (view - target);
		float peak = 0;
		for (int i = lo; i < hi; i++) {
			float a = chrono[i];
			if (a < 0) a = -a;
			if (a > peak) peak = a;
		}
		int best = -1;
		if (peak >= 0.004f) {
			best = findcross(chrono, lo, hi, peak * 0.15f);
			if (best < 0) best = findcross(chrono, lo, hi, peak * 0.05f);
		}
		int from = best < 0 ? n - view : best - target;
		for (int i = 0; i < view; i++) dst[i] = chrono[from + i];
		return best >= 0;
	}

	static int findcross(float[] s, int lo, int hi, float hyst) {
		int best = -1;
		int bestDist = int.MaxValue;
		int ideal = hi - 1;
		float trough = 0;
		int begin = lo - 64;
		if (begin < 1) begin = 1;
		for (int i = begin; i < hi; i++) {
			if (s[i] < trough) trough = s[i];
			if (s[i - 1] <= 0f && s[i] > 0f) {
				if (i >= lo && trough <= -hyst) {
					int d = i - ideal;
					if (d < 0) d = -d;
					if (d < bestDist) {
						bestDist = d;
						best = i;
					}
				}
				trough = 0;
			}
		}
		return best;
	}

	// 系统合成器出来的幅度往往很小，按这一段的峰值拉开到接近满幅。静音不放大。
	void boost(float[] dst) {
		float peak = 0;
		for (int i = 0; i < dst.Length; i++) {
			float a = dst[i];
			if (a < 0) a = -a;
			if (a > peak) peak = a;
		}
		if (peak < 0.004f) {
			shown = 0;
			return;
		}
		if (peak > shown) shown = peak;
		else shown *= 0.92f;
		float gain = DisplayGain(shown);
		if (gain <= 1f) return;
		for (int i = 0; i < dst.Length; i++) dst[i] *= gain;
	}

	public static float DisplayGain(float peak) {
		if (peak < 0.004f) return 1f;
		float g = 0.92f / peak;
		if (g < 1f) return 1f;
		if (g > 36f) return 36f;
		return g;
	}

	public void Dispose() {
		var c = cap;
		cap = null;
		if (c == null) return;
		try { c.DataAvailable -= ondata; } catch { }
		try { c.StopRecording(); } catch { }
		try { c.Dispose(); } catch { }
	}

	void ondata(object sender, WaveInEventArgs e) {
		var fmt = cap?.WaveFormat;
		if (fmt == null || e.BytesRecorded <= 0) return;
		int ch = fmt.Channels;
		int bpf = fmt.BlockAlign;
		if (ch < 1 || bpf < 1) return;
		int frames = e.BytesRecorded / bpf;
		lock (gate) {
			for (int i = 0; i < frames; i++) {
				ring[write] = mono(e.Buffer, i * bpf, ch, fmt);
				write++;
				if (write >= ring.Length) write = 0;
			}
		}
	}

	static float mono(byte[] buf, int off, int ch, WaveFormat fmt) {
		int bps = fmt.BitsPerSample / 8;
		if (bps < 1) return 0;
		float sum = 0;
		int got = 0;
		for (int c = 0; c < ch; c++) {
			int o = off + c * bps;
			if (o < 0 || o + bps > buf.Length) break;
			got++;
			if (fmt.Encoding == WaveFormatEncoding.IeeeFloat && bps >= 4)
				sum += BitConverter.ToSingle(buf, o);
			else if (bps == 2)
				sum += BitConverter.ToInt16(buf, o) / 32768f;
			else if (bps >= 3) {
				int v = buf[o] | (buf[o + 1] << 8) | (buf[o + 2] << 16);
				if ((v & 0x800000) != 0) v |= ~0xFFFFFF;
				sum += v / 8388608f;
			}
		}
		if (got == 0) return 0;
		return sum / got;
	}

	/// <summary>把最近一段采样画成示波器。正半周朝上，静音落在中线。</summary>
	public static void Paint(byte[] pix, int w, int h, int stride, float[] samples) {
		if (pix == null || w < 2 || h < 2 || stride < w * 4) return;
		for (int y = 0; y < h; y++) {
			int row = y * stride;
			for (int x = 0; x < w; x++) put(pix, row + x * 4, 0x15, 0x19, 0x1C);
		}
		int mid = h / 2;
		for (int x = 0; x < w; x++) put(pix, mid * stride + x * 4, 80, 85, 88);
		if (samples == null || samples.Length < 2) return;
		int n = samples.Length;
		byte ob = 0x16, og = 0x73, orr = 0xF9;
		for (int x = 0; x < w; x++) {
			int i0 = x * (n - 1) / (w - 1);
			int i1 = (x + 1) * (n - 1) / (w - 1);
			if (i1 <= i0) i1 = i0 + 1;
			if (i1 > n) i1 = n;
			float lo = samples[i0];
			float hi = lo;
			for (int i = i0 + 1; i < i1; i++) {
				float s = samples[i];
				if (s < lo) lo = s;
				if (s > hi) hi = s;
			}
			int y0 = yof(hi, h);
			int y1 = yof(lo, h);
			if (y0 > y1) {
				int t = y0;
				y0 = y1;
				y1 = t;
			}
			for (int y = y0; y <= y1; y++) put(pix, y * stride + x * 4, ob, og, orr);
		}
	}

	public static int SelfTest(Action<string> log) {
		void say(string s) { try { log?.Invoke(s); } catch { } }
		int w = 40;
		int h = 20;
		int stride = w * 4;
		var pix = new byte[stride * h];
		var up = new float[16];
		for (int i = 0; i < up.Length; i++) up[i] = 1f;
		Paint(pix, w, h, stride, up);
		bool top = false;
		for (int x = 0; x < w; x++) {
			int o = x * 4;
			if (pix[o] == 0x16 && pix[o + 1] == 0x73 && pix[o + 2] == 0xF9) top = true;
		}
		if (!top) {
			say("FAIL wave top");
			return 1;
		}
		var down = new float[16];
		for (int i = 0; i < down.Length; i++) down[i] = -1f;
		Paint(pix, w, h, stride, down);
		bool bot = false;
		int yb = h - 1;
		for (int x = 0; x < w; x++) {
			int o = yb * stride + x * 4;
			if (pix[o] == 0x16 && pix[o + 1] == 0x73 && pix[o + 2] == 0xF9) bot = true;
		}
		if (!bot) {
			say("FAIL wave bottom");
			return 1;
		}
		if (DisplayGain(1f) != 1f || DisplayGain(0.001f) != 1f) {
			say("FAIL wave gain idle");
			return 1;
		}
		float g = DisplayGain(0.05f);
		if (g < 16f || g > 20f) {
			say("FAIL wave gain " + g);
			return 1;
		}
		var chrono = new float[4096];
		var view = new float[512];
		int anchor = 512 / 8;
		for (int phase = 0; phase < 64; phase += 13) {
			for (int i = 0; i < chrono.Length; i++)
				chrono[i] = (float)Math.Sin((i + phase) * (Math.PI * 2 / 64));
			if (!AlignView(chrono, view) || !(view[anchor] > 0f) || !(view[anchor - 1] <= 0f)) {
				say("FAIL wave lock phase " + phase);
				return 1;
			}
		}
		say("wave ok");
		return 0;
	}

	static int yof(float s, int h) {
		if (s > 1f) s = 1f;
		if (s < -1f) s = -1f;
		int y = (int)((1f - s) * 0.5f * (h - 1));
		if (y < 0) return 0;
		if (y >= h) return h - 1;
		return y;
	}

	static void put(byte[] pix, int o, byte b, byte g, byte r) {
		pix[o] = b;
		pix[o + 1] = g;
		pix[o + 2] = r;
		pix[o + 3] = 255;
	}
}
